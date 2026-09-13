using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 沙 / 草 / 岩三族「程序化噪声贴图」的**编辑器落盘器**（幂等）。
    ///
    /// 【它解决什么问题】r3 复验 N4（高）：地形/沙地材质是**纯色平面**——同一可见面 90px 内只有 7 种
    ///   颜色、单面 RGB 恒定 <c>#E9C77E</c>。判据（美术品控 P-9 / P-10、美术风格指南 §3.1-3.2）要求：
    ///     · 同材质 200×200 窗灰度标准差 &gt; 6（P-9，阈值见 美术品控评审规程.md:213）；
    ///     · 同一可见面内有颜色起伏（不能整面同色）；
    ///     · 沙/草/岩相邻面 smoothness 差 ≥ 0.15（§3.2 纪律 1）。
    ///   片元里的 FBM（<c>PirateSurface.shader:253-268</c>）只给**低频**斑块，压不出像素级纹理能量
    ///   （P-10 量的是高频能量），所以必须在 albedo/normal 上落一层细粒度的确定性纹理。
    ///
    /// 【为什么这不是"外部贴图"】本工程 0 外部贴图（AGENTS.md）。本类是**程序化资产**管线的一员，
    ///   与 <see cref="FxAssetBuilder"/>（特效贴图）、<see cref="AudioAssetBuilder"/>（wav 导入）
    ///   同一哲学：算法在本仓库内、可复算、可审查、可重跑出**逐像素一致**的结果。
    ///   风格指南 §3.3 也明确「先程序化生成法线/噪声贴图，再考虑手绘」。
    ///
    /// 【产物】<c>Assets/Art/Textures/Materials/</c>（与 Fx/、Water/ 同级，新目录）
    ///   Noise_Sand_Albedo.png   / Noise_Sand_Normal.png
    ///   Noise_Grass_Albedo.png  / Noise_Grass_Normal.png
    ///   Noise_Rock_Albedo.png   / Noise_Rock_Normal.png
    ///   6 张 256×256（风格指南 §3.3：环境 albedo/法线各 256×256，平铺 2-4m）。
    ///
    /// 【两类贴图的编码口径（踩不对就整体变色，必读）】
    ///   1. albedo 细节图 = **均值保持的乘性调制图**，不是材质基色图：
    ///      像素值 = 线性域系数（均值**精确** 0.5）编码到 sRGB。shader 侧 <c>color * 2</c> 后均值恰为 1.0，
    ///      于是「原地加起伏、不改材质已调好的平均色」——调色板（GDD §10.4 三档色）与 r3 已裁决的
    ///      光照/曝光口径都不被扰动。sRGB 编码是必须的：贴图导入为 sRGB=true，硬件采样时会解码回线性，
    ///      存 sRGB 才能让采样值等于我们算出的线性系数（见 <see cref="SrgbToLinear"/>/<see cref="LinearToSrgb"/>）。
    ///   2. 法线图 = 切空间法线 <c>n*0.5+0.5</c>，A=255（**A 必须是 1**）。
    ///      桌面 D3D11 走 core <c>Packing.hlsl:214 UnpackNormalmapRGorAG</c>，它会做
    ///      <c>packed.x *= packed.w</c> 来同时兼容 RGBA 与 DXT5nm 两种布局；A=1 时 RGBA 路径才成立。
    ///      导入为 NormalMap 类型 + sRGB=false（线性数据）+ Uncompressed（避免 BC 压缩把细噪压出块状伪影）。
    ///
    /// 【无缝（tileable）】全部噪声用整数周期格点的 value 噪声，频率按 2 的幂递增（4/8/16/32…），
    ///   周期正好整除贴图尺寸 → 平铺无接缝。法线用**前向差分 + 周期回绕**（<c>(x+1)%size</c>），
    ///   所以左右/上下边缘的梯度也连续，不会在平铺接缝处出现一条硬法线断线。
    ///
    /// 【强度不是拍脑袋】法线强度用「整张高度场的梯度 RMS 归一到目标 RMS 斜率」反推
    ///   （<see cref="BuildNormalMap"/>），目标值直接对应坡角：沙 0.07(≈4°)、草 0.16(≈9°)、
    ///   岩 0.28(≈15.6°)。这样换噪声函数也不会把法线搞暴；albedo 则实测并打印线性明度标准差，
    ///   作为 P-9「同材质窗内 std」的**程序化判据**（AGENTS.md 要求判图用程序化判据，不靠"看着像"）。
    ///
    /// 【幂等】再次执行时字节一致则不重写文件（同 FxAssetBuilder），只校正导入设置；.mat 不在此类改。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/渲染/生成程序化材质噪声贴图（幂等）
    ///         PirateCrew/渲染/强制重建程序化材质噪声贴图
    ///   无头: -batchmode -nographics -quit
    ///         -executeMethod PirateCrew.EditorTools.MaterialNoiseBuilder.BuildAll
    ///   本类也被 <see cref="ArtGate"/> ⓪.5 步与 <c>BattleSceneLighting.BuildAll</c> 调用
    ///   （材质要引用贴图资产，必须先生成）。
    /// </summary>
    public static class MaterialNoiseBuilder
    {
        // ------------------------------------------------------------------
        // 路径 / 规格
        // ------------------------------------------------------------------

        /// <summary>贴图目录（新目录；与 Textures/Fx、Textures/Water 同级）。</summary>
        public const string TextureFolder = "Assets/Art/Textures/Materials";

        /// <summary>贴图边长（风格指南 §3.3：环境 albedo/法线各 256×256）。</summary>
        public const int TextureSize = 256;

        /// <summary>六张贴图的种类。命名 = <c>Noise_{族}_{Albedo|Normal}</c>。</summary>
        public enum NoiseKind
        {
            SandAlbedo,
            SandNormal,
            GrassAlbedo,
            GrassNormal,
            RockAlbedo,
            RockNormal,
        }

        /// <summary>全部种类（供 ArtGate 等做"6 张都在吗"的自检，顺序固定）。</summary>
        public static readonly NoiseKind[] AllKinds =
        {
            NoiseKind.SandAlbedo,  NoiseKind.SandNormal,
            NoiseKind.GrassAlbedo, NoiseKind.GrassNormal,
            NoiseKind.RockAlbedo,  NoiseKind.RockNormal,
        };

        public static string FileNameOf(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.SandAlbedo:  return "Noise_Sand_Albedo";
                case NoiseKind.SandNormal:  return "Noise_Sand_Normal";
                case NoiseKind.GrassAlbedo: return "Noise_Grass_Albedo";
                case NoiseKind.GrassNormal: return "Noise_Grass_Normal";
                case NoiseKind.RockAlbedo:  return "Noise_Rock_Albedo";
                case NoiseKind.RockNormal:  return "Noise_Rock_Normal";
            }
            return "Noise_Unknown";
        }

        public static string PathOf(NoiseKind kind)
        {
            return TextureFolder + "/" + FileNameOf(kind) + ".png";
        }

        static bool IsNormalMap(NoiseKind kind)
        {
            return kind == NoiseKind.SandNormal || kind == NoiseKind.GrassNormal || kind == NoiseKind.RockNormal;
        }

        /// <summary>取已生成的贴图资产；未生成（没跑过本类）时返回 null，调用方必须对 null 做兜底。</summary>
        public static Texture2D Load(NoiseKind kind)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(kind));
        }

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/渲染/生成程序化材质噪声贴图（幂等）", priority = 20)]
        public static void BuildAll()
        {
            Build(false);
        }

        [MenuItem("PirateCrew/渲染/强制重建程序化材质噪声贴图", priority = 21)]
        public static void RebuildAll()
        {
            Build(true);
        }

        /// <summary>
        /// 生成/校正全部 6 张贴图。任何单张失败只记 Warning 继续（不中断 batchmode）。
        /// 【刻意不用 StartAssetEditing/StopAssetEditing】需要逐张 SaveAndReimport 立刻落实导入设置，
        ///   理由与 <see cref="FxAssetBuilder"/> 完全相同（见该文件 Build() 的注释）。
        /// </summary>
        public static void Build(bool force)
        {
            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Textures");
            EnsureFolder(TextureFolder);

            int written = 0;
            int skipped = 0;
            int failed = 0;
            string report = "";

            for (int i = 0; i < AllKinds.Length; i++)
            {
                NoiseKind kind = AllKinds[i];
                string stat;
                int result = Generate(kind, force, out stat);
                if (result < 0)
                    failed++;
                else if (result == 0)
                    skipped++;
                else
                    written++;

                report += "\n  · " + FileNameOf(kind) + "：" + stat;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MaterialNoiseBuilder] 程序化材质噪声贴图完成：新写 " + written
                + " / 已是最新 " + skipped + " / 失败 " + failed + "，共 " + AllKinds.Length
                + " 张 → " + TextureFolder + (force ? "（强制重建）" : "（幂等）")
                + report
                + "\n  判据：albedo 的「线性明度 std」= 乘性系数（均值 1.0）的起伏幅度；"
                + "经 shader ×2 后作用在材质 albedo 上，可直接对照 P-9「同材质 200×200 窗 std > 6」。");
        }

        // ------------------------------------------------------------------
        // 单张贴图
        // ------------------------------------------------------------------

        /// <summary>返回 1=写入、0=字节一致跳过、-1=失败。</summary>
        static int Generate(NoiseKind kind, bool force, out string stat)
        {
            string assetPath = PathOf(kind);
            string absolutePath = ToAbsolutePath(assetPath);
            stat = "（未生成）";

            byte[] png;
            try
            {
                Color32[] pixels;
                if (IsNormalMap(kind))
                    pixels = BuildNormalPixels(kind, TextureSize, out stat);
                else
                    pixels = BuildAlbedoPixels(kind, TextureSize, out stat);

                var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MaterialNoiseBuilder] 生成贴图 " + kind + " 失败：" + e.Message);
                stat = "生成失败：" + e.Message;
                return -1;
            }

            if (png == null || png.Length == 0)
            {
                Debug.LogWarning("[MaterialNoiseBuilder] 贴图 " + kind + " 编码结果为空。");
                stat = "编码结果为空";
                return -1;
            }

            int result = 0;
            if (force || !BytesEqual(absolutePath, png))
            {
                File.WriteAllBytes(absolutePath, png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                result = 1;
            }

            ConfigureImporter(assetPath, IsNormalMap(kind));
            return result;
        }

        /// <summary>
        /// 写死导入设置。要点：
        ///   · albedo：<c>sRGBTexture = true</c>（我们的像素存的是 sRGB 编码，采样时解码回线性系数）；
        ///   · 法线：<c>TextureImporterType.NormalMap</c> + <c>sRGBTexture = false</c>（线性数据）；
        ///   · <c>wrapMode = Repeat</c>：世界空间平铺，绝不能 Clamp；
        ///   · <c>mipmapEnabled = true</c>：世界空间平铺 + 高频细噪，没有 mip 远处会闪烁/摩尔纹；
        ///   · <c>Uncompressed</c>：BC 压缩会把 ±6% 的明度噪声压成块状伪影，把"细腻"做成"脏"；
        ///   · <c>anisoLevel = 4</c>：地面是 45° 俯视的近水平面，各向异性过滤让远处贴图不糊。
        /// </summary>
        static void ConfigureImporter(string assetPath, bool isNormalMap)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[MaterialNoiseBuilder] 无法读取 TextureImporter：" + assetPath
                    + "，导入设置未生效（贴图本身已生成）。");
                return;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            settings.textureShape = TextureImporterShape.Texture2D;
            settings.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            settings.sRGBTexture = !isNormalMap;
            settings.mipmapEnabled = true;
            settings.wrapMode = TextureWrapMode.Repeat;
            settings.filterMode = FilterMode.Bilinear;
            settings.aniso = 4;
            settings.npotScale = TextureImporterNPOTScale.None;
            // 法线图必须保留源 alpha（桌面 UnpackNormalmapRGorAG 会读 w 通道，A 丢了法线会歪）。
            settings.alphaSource = TextureImporterAlphaSource.FromInput;
            settings.alphaIsTransparency = false;
            settings.readable = false;
            if (isNormalMap)
                settings.normalMapFilter = TextureImporterNormalFilter.Standard;

            importer.SetTextureSettings(settings);
            // 压缩/尺寸在 SetTextureSettings 之后再设，避免被 settings 覆盖。
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = TextureSize;

            importer.SaveAndReimport();
        }

        // ==================================================================
        // albedo 细节图（沙 / 草 / 岩）
        // ==================================================================

        /// <summary>
        /// 生成「均值保持的乘性微色斑图」：像素 = 线性域系数（均值精确 0.5）的 sRGB 编码。
        /// 两遍：先按各族配方算未归一系数 → 再按实测均值归一（保证均值恰 0.5，材质平均色零漂移）。
        /// 同时实测「乘性系数（= 归一后 ×2）」的 Rec.709 线性明度标准差，作为 P-9 的程序化判据。
        /// </summary>
        static Color32[] BuildAlbedoPixels(NoiseKind kind, int size, out string stat)
        {
            int count = size * size;
            var raw = new Color[count];
            double sumR = 0, sumG = 0, sumB = 0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;
                    Color c = AlbedoFactor(kind, u, v, size);
                    raw[y * size + x] = c;
                    sumR += c.r; sumG += c.g; sumB += c.b;
                }
            }

            float meanR = (float)(sumR / count);
            float meanG = (float)(sumG / count);
            float meanB = (float)(sumB / count);

            var pixels = new Color32[count];
            var luma = new float[count];
            double lumaSum = 0;

            for (int i = 0; i < count; i++)
            {
                Color c = raw[i];
                // 归一：线性均值 → 0.5（shader 侧 ×2 后均值 = 1.0）
                float r = c.r * (0.5f / Mathf.Max(meanR, 1e-4f));
                float g = c.g * (0.5f / Mathf.Max(meanG, 1e-4f));
                float b = c.b * (0.5f / Mathf.Max(meanB, 1e-4f));

                pixels[i] = new Color32(
                    ToByte(LinearToSrgb(r)),
                    ToByte(LinearToSrgb(g)),
                    ToByte(LinearToSrgb(b)),
                    255);

                // 统计的是「乘性系数」（= ×2），Rec.709 线性明度
                float f = 2f * (0.2126f * r + 0.7152f * g + 0.0722f * b);
                luma[i] = f;
                lumaSum += f;
            }

            float lumaMean = (float)(lumaSum / count);
            double varSum = 0;
            for (int i = 0; i < count; i++)
            {
                double d = luma[i] - lumaMean;
                varSum += d * d;
            }
            float lumaStd = Mathf.Sqrt((float)(varSum / count));

            stat = "albedo 256²，乘性系数均值 " + lumaMean.ToString("0.000")
                 + "（期望 1.000）、线性明度 std " + lumaStd.ToString("0.000")
                 + "（P-9：该系数直接乘进 albedo，渲染后 8bit 灰度 std 应 ≈ "
                 + (lumaStd * 255f * 0.5f).ToString("0.0") + " 量级）";
            return pixels;
        }

        /// <summary>
        /// 一族的「未归一乘性系数」（线性域，均值≈1）。返回 Color，通道可各不相同（保留色相起伏）。
        /// 各族配方与参数见各分支注释；共同点：所有噪声都是整数周期 → 无缝平铺。
        /// </summary>
        static Color AlbedoFactor(NoiseKind kind, float u, float v, int size)
        {
            switch (kind)
            {
                case NoiseKind.SandAlbedo:
                    return SandAlbedoFactor(u, v, size);
                case NoiseKind.GrassAlbedo:
                    return GrassAlbedoFactor(u, v);
                case NoiseKind.RockAlbedo:
                    return RockAlbedoFactor(u, v);
            }
            return new Color(1f, 1f, 1f, 1f);
        }

        // 沙 albedo（任务书：基色 #E8D5A3/#C4A76A 之间按噪声取色 + ±6% 明度扰动 + 稀疏贝壳白点 <0.5%）
        //   参数表：主色斑 FBM 基准周期 4（≈0.7 世界单位一块，200px 窗里能看 3-4 块）、对比拉伸 2.4；
        //           中频 FBM 周期 16（≈0.18 单位）权重 0.35；
        //           明度扰动 FBM 周期 64、幅度 ±6%（任务书值）；细颗粒周期 192、幅度 ±4%；
        //           贝壳：16×16 单元格、4.5% 单元格出点、半径 0.06-0.11 格 → 覆盖率 ≈0.14%（<0.5%）。
        static Color SandAlbedoFactor(float u, float v, int size)
        {
            float nPatch = Contrast(Fbm(u, v, 4, 4, 4, SeedSandPatch), 2.4f);
            float nMid   = Contrast(Fbm(u, v, 16, 16, 3, SeedSandMid), 1.4f);
            float mix    = Mathf.Clamp01(0.65f * nPatch + 0.35f * nMid);

            Color lightLin = SrgbToLinear(SandLightSrgb);
            Color midLin   = SrgbToLinear(SandMidSrgb);
            Color meanLin  = (lightLin + midLin) * 0.5f;
            Color baseLin  = Color.Lerp(lightLin, midLin, mix);

            // 明度扰动 ±6% + 细颗粒 ±4%（都作用在"系数"上，故不破坏平均色）
            float lum = 1f + (Fbm(u, v, 64, 64, 2, SeedSandGrain) - 0.5f) * 2f * 0.06f;
            float fine = 1f + (Fbm(u, v, 192, 192, 2, SeedSandFine) - 0.5f) * 2f * 0.04f;

            Color factor = RatioToMean(baseLin, meanLin) * (lum * fine);

            // 稀疏贝壳白点：亮而小，靠 mask 混入（覆盖率极低 → 均值几乎不动）
            float shell = ShellMask(u, v, size, SeedSandShell);
            if (shell > 0.001f)
                factor = Color.Lerp(factor, RatioToMean(SrgbToLinear(SandShellSrgb), meanLin), shell * 0.85f);

            return factor;
        }

        // 草 albedo（任务书：#4A8C4A/#7BC67E 双色 patch）
        //   参数表：双色 patch FBM 周期 6（≈0.67 单位）、对比拉伸 2.2；细碎斑 FBM 周期 24、权重 0.3；
        //           深色草缝（叶隙阴影）用周期 48 的阈值噪声打点，最多压暗 22%。
        static Color GrassAlbedoFactor(float u, float v)
        {
            float nPatch = Contrast(Fbm(u, v, 6, 6, 4, SeedGrassPatch), 2.2f);
            float nMid   = Contrast(Fbm(u, v, 24, 24, 3, SeedGrassMid), 1.3f);
            float mix    = Mathf.Clamp01(0.7f * nPatch + 0.3f * nMid);

            Color darkLin  = SrgbToLinear(GrassDarkSrgb);
            Color lightLin = SrgbToLinear(GrassLightSrgb);
            Color meanLin  = (darkLin + lightLin) * 0.5f;
            Color baseLin  = Color.Lerp(darkLin, lightLin, mix);

            // 草叶/叶隙：高频阈值噪声 → 细碎的暗点（让"绒毛感"来自纹理而非只靠法线）
            float gap = Mathf.SmoothStep(0f, 0.06f, Fbm(u, v, 48, 48, 2, SeedGrassGap) - 0.62f);
            float lum = (1f + (Fbm(u, v, 64, 64, 2, SeedGrassGrain) - 0.5f) * 2f * 0.06f) * (1f - 0.22f * gap);

            return RatioToMean(baseLin, meanLin) * lum;
        }

        // 岩 albedo（任务书：#8C7B6A 基础上斑块 + 裂缝暗线（阈值化噪声））
        //   参数表：斑块在暗档 #5C4F42 与亮档 #B8A99A 间按 FBM（周期 5，对比 2.0）取色；
        //           裂缝 = 阈值化 FBM（周期 8、4 阶），|n-0.5| < 0.035 处为暗线，最多压暗 45%；
        //           岩层的各向异性拉长（周期 (6,24)）给沉积纹理；细颗粒 ±5%。
        static Color RockAlbedoFactor(float u, float v)
        {
            float nPatch = Contrast(Fbm(u, v, 5, 5, 4, SeedRockPatch), 2.0f);
            float nStrata = Contrast(Fbm(u, v, 6, 24, 3, SeedRockStrata), 1.2f);
            float mix = Mathf.Clamp01(0.7f * nPatch + 0.3f * nStrata);

            Color darkLin  = SrgbToLinear(RockDarkSrgb);
            Color lightLin = SrgbToLinear(RockLightSrgb);
            Color meanLin  = (darkLin + lightLin) * 0.5f;
            Color baseLin  = Color.Lerp(darkLin, lightLin, mix);

            float crack = CrackMask(u, v);
            float lum = 1f + (Fbm(u, v, 96, 96, 2, SeedRockGrain) - 0.5f) * 2f * 0.05f;

            return RatioToMean(baseLin, meanLin) * (lum * (1f - 0.45f * crack));
        }

        // ==================================================================
        // 法线图（沙 / 草 / 岩）
        // ==================================================================

        /// <summary>
        /// 从高度场生成切空间法线图：每纹素前向差分（周期回绕 → 平铺无缝）→ 梯度 RMS 归一到目标斜率。
        /// 目标斜率 = tan(坡角)：沙 0.07(≈4°，细沙波纹温和)、草 0.16(≈9°)、岩 0.28(≈15.6°，断裂感强)。
        /// 【为什么用 RMS 归一而不是手调 gain】换噪声函数/换振幅时法线强度不会失控，
        ///   且打印出来的实测值就是验收证据（不用进编辑器就能核对"温和/强"的量化口径）。
        /// </summary>
        static Color32[] BuildNormalPixels(NoiseKind kind, int size, out string stat)
        {
            int count = size * size;
            var height = new float[count];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    height[y * size + x] = HeightAt(kind, (x + 0.5f) / size, (y + 0.5f) / size);
                }
            }

            float targetSlope = TargetSlope(kind);

            var gx = new float[count];
            var gy = new float[count];
            double sqSum = 0;
            for (int y = 0; y < size; y++)
            {
                int yNext = (y + 1) % size;              // 周期回绕 → 接缝处梯度也连续
                for (int x = 0; x < size; x++)
                {
                    int xNext = (x + 1) % size;
                    int i = y * size + x;
                    float dx = height[y * size + xNext] - height[i];
                    float dy = height[yNext * size + x] - height[i];
                    gx[i] = dx;
                    gy[i] = dy;
                    sqSum += dx * dx + dy * dy;
                }
            }

            float rms = Mathf.Sqrt((float)(sqSum / count));
            float gain = targetSlope / Mathf.Max(rms, 1e-6f);

            var pixels = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                float nx = -gx[i] * gain;
                float ny = -gy[i] * gain;
                float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);
                pixels[i] = new Color32(
                    ToByte(nx * inv * 0.5f + 0.5f),
                    ToByte(ny * inv * 0.5f + 0.5f),
                    ToByte(inv * 0.5f + 0.5f),
                    255);                                 // A=255：桌面 UnpackNormalmapRGorAG 需要 w=1
            }

            stat = "normal 256²，高度场梯度 RMS " + rms.ToString("0.0000")
                 + " → 增益 " + gain.ToString("0.00")
                 + " → 目标 RMS 斜率 " + targetSlope.ToString("0.00")
                 + "（≈" + (Mathf.Atan(targetSlope) * Mathf.Rad2Deg).ToString("0.0") + "° 坡角）";
            return pixels;
        }

        static float TargetSlope(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.SandNormal:  return 0.07f;   // 细沙波纹：温和
                case NoiseKind.GrassNormal: return 0.16f;   // 草簇压痕：中等
                case NoiseKind.RockNormal:  return 0.28f;   // 凿痕/裂隙：强
            }
            return 0.1f;
        }

        static float HeightAt(NoiseKind kind, float u, float v)
        {
            switch (kind)
            {
                case NoiseKind.SandNormal:  return SandHeight(u, v);
                case NoiseKind.GrassNormal: return GrassHeight(u, v);
                case NoiseKind.RockNormal:  return RockHeight(u, v);
            }
            return 0f;
        }

        // 沙高度：主频 ripples 与 shader 的沙纹方向同源（rdir = normalize(5,3)，见 PirateSurface.shader 的 _RippleScale 段）；
        //   用整数波数 (5,3) 沿 u/v → 既与 shader 沙纹同向，又天然无缝。
        //   叠加：低频起伏（周期 16）+ 细沙扰动（周期 64，权重 0.25）。
        static float SandHeight(float u, float v)
        {
            float phase = 2f * Mathf.PI * (5f * u + 3f * v)
                        + (Fbm(u, v, 4, 4, 3, SeedSandRippleWarp) - 0.5f) * Mathf.PI * 2f * 0.55f;
            float ripple = Mathf.Sin(phase);
            float mid    = (Fbm(u, v, 16, 16, 2, SeedSandNormalMid) - 0.5f) * 2f;
            float fine   = (Fbm(u, v, 64, 64, 3, SeedSandNormalFine) - 0.5f) * 2f;
            return ripple * 0.75f + mid * 0.5f + fine * 0.25f;
        }

        // 草高度：各向异性"草叶"（沿 v 拉长：u 高频 48 / v 低频 16）+ 中频草簇（周期 8）+ 细碎（周期 96）。
        static float GrassHeight(float u, float v)
        {
            float blade = (Fbm(u, v, 48, 16, 3, SeedGrassBlade) - 0.5f) * 2f;
            float clump = (Fbm(u, v, 8, 8, 3, SeedGrassClump) - 0.5f) * 2f;
            float fine  = (Fbm(u, v, 96, 96, 2, SeedGrassFine) - 0.5f) * 2f;
            return blade * 0.55f + clump * 0.7f + fine * 0.2f;
        }

        // 岩高度：裂缝（阈值化噪声 → 窄脊，做成**负**高度 = 凹陷）+ 各向异性岩层（沿 v 拉长）+ 块状起伏。
        static float RockHeight(float u, float v)
        {
            float crack  = CrackMask(u, v);
            float strata = (Fbm(u, v, 6, 24, 4, SeedRockStrataN) - 0.5f) * 2f;
            float chunk  = (Fbm(u, v, 10, 10, 3, SeedRockChunk) - 0.5f) * 2f;
            return -crack * 1.0f + strata * 0.35f + chunk * 0.5f;
        }

        // ==================================================================
        // 噪声内核（整数周期 value 噪声 → 无缝）
        // ==================================================================

        // 种子：一族一档，改动任何一个都会让贴图整体变化（贴图是资产，改了要重跑并复核）。
        const int SeedSandPatch      = 101;
        const int SeedSandMid        = 103;
        const int SeedSandGrain      = 107;
        const int SeedSandFine       = 109;
        const int SeedSandShell      = 113;
        const int SeedSandRippleWarp = 127;
        const int SeedSandNormalMid  = 131;
        const int SeedSandNormalFine = 137;
        const int SeedGrassPatch     = 201;
        const int SeedGrassMid       = 203;
        const int SeedGrassGrain     = 207;
        const int SeedGrassGap       = 209;
        const int SeedGrassBlade     = 211;
        const int SeedGrassClump     = 223;
        const int SeedGrassFine      = 227;
        const int SeedRockPatch      = 301;
        const int SeedRockStrata     = 303;
        const int SeedRockGrain      = 307;
        const int SeedRockCrack      = 311;
        const int SeedRockStrataN    = 313;
        const int SeedRockChunk      = 317;

        /// <summary>整数哈希 → [0,1)。纯整数位运算，跨平台/跨会话逐位一致（可复算的确定性）。</summary>
        static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u
                       + (uint)y * 668265263u
                       + (uint)seed * 1442695041u;
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        static int Wrap(int v, int period)
        {
            int m = v % period;
            return m < 0 ? m + period : m;
        }

        /// <summary>
        /// value 噪声：格点数 = period（整数）→ 在 [0,1) 的 uv 上正好 period 个格子 → **平铺无缝**。
        /// 双线性 + smoothstep 缓和（与 shader 里的 PirateValueNoise 同构，但这里是 CPU 侧确定性版本）。
        /// </summary>
        static float ValueNoise(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            float u = fx * fx * (3f - 2f * fx);
            float v = fy * fy * (3f - 2f * fy);

            float a = Hash01(Wrap(x0, periodX), Wrap(y0, periodY), seed);
            float b = Hash01(Wrap(x0 + 1, periodX), Wrap(y0, periodY), seed);
            float c = Hash01(Wrap(x0, periodX), Wrap(y0 + 1, periodY), seed);
            float d = Hash01(Wrap(x0 + 1, periodX), Wrap(y0 + 1, periodY), seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>4 阶 fBm，返回 [0,1]。每阶周期 ×2（仍整除贴图边长 → 仍然无缝）。</summary>
        static float Fbm(float u, float v, int periodX, int periodY, int octaves, int seed)
        {
            float sum = 0f;
            float amp = 0.5f;
            float norm = 0f;
            int px = periodX;
            int py = periodY;

            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * px, v * py, px, py, seed + o * 131) * amp;
                norm += amp;
                amp *= 0.5f;
                px *= 2;
                py *= 2;
            }
            return sum / Mathf.Max(norm, 1e-5f);
        }

        /// <summary>把 [0,1] 的噪声按对比 k 拉伸到 [0,1]（value 噪声分布偏集中，不拉伸斑块太淡）。</summary>
        static float Contrast(float n, float k)
        {
            return Mathf.Clamp01(0.5f + (n - 0.5f) * k);
        }

        /// <summary>裂缝掩码：|n-0.5| &lt; 0.035 处为 1（窄线），否则 0，边缘 0.035 宽软过渡。无缝（n 无缝）。</summary>
        static float CrackMask(float u, float v)
        {
            float n = Fbm(u, v, 8, 8, 4, SeedRockCrack);
            return 1f - Mathf.SmoothStep(0f, 0.035f, Mathf.Abs(n - 0.5f));
        }

        /// <summary>
        /// 贝壳掩码：16×16 个周期单元格，4.5% 的格子里放一颗半径 0.06-0.11 格的贝壳。
        /// 覆盖率 ≈ 11.5 颗 × π×(1.4px)² ≈ 80px²/65536 ≈ 0.12%（&lt;0.5%，符合任务书）。
        /// 3×3 邻域扫描保证单元格边界处的贝壳跨格连续；全部按 16 取模 → 平铺无缝。
        /// </summary>
        static float ShellMask(float u, float v, int size, int seed)
        {
            const int cells = 16;
            float x = u * cells;
            float y = v * cells;
            int cx = Mathf.FloorToInt(x);
            int cy = Mathf.FloorToInt(y);
            float fx = x - cx;
            float fy = y - cy;

            float best = 0f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int gx = Wrap(cx + ox, cells);
                    int gy = Wrap(cy + oy, cells);
                    if (Hash01(gx, gy, seed) > 0.045f)
                        continue;

                    float px = Hash01(gx, gy, seed + 7);
                    float py = Hash01(gx, gy, seed + 13);
                    float radius = 0.06f + Hash01(gx, gy, seed + 17) * 0.05f;

                    float dx = (fx - ox) - px;
                    float dy = (fy - oy) - py;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    best = Mathf.Max(best, 1f - Mathf.SmoothStep(0f, radius, d));
                }
            }
            return best;
        }

        // ==================================================================
        // 调色板（sRGB 十六进制，与 GDD §10.4 / BattleSceneLighting 同一套口径）
        // ==================================================================

        static readonly Color SandLightSrgb  = Hex("#E8D5A3");   // 沙亮档（GDD §10.4）
        static readonly Color SandMidSrgb    = Hex("#C4A76A");   // 沙中档
        static readonly Color SandShellSrgb  = Hex("#F2EAD6");   // 贝壳白【AI 提案】
        static readonly Color GrassDarkSrgb  = Hex("#4A8C4A");   // 草中档（任务书双色之一）
        static readonly Color GrassLightSrgb = Hex("#7BC67E");   // 草亮档（任务书双色之二）
        static readonly Color RockDarkSrgb   = Hex("#5C4F42");   // 岩暗档（GDD §10.4）
        static readonly Color RockLightSrgb  = Hex("#B8A99A");   // 岩亮档

        static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
                return c;

            Debug.LogWarning("[MaterialNoiseBuilder] 无法解析颜色 " + hex + "，退回品红以便肉眼发现问题。");
            return Color.magenta;
        }

        // ==================================================================
        // 色空间换算（手写而非 Color.linear/Color.gamma：公式写在这里可核对，且与硬件 sRGB 采样
        // 用的同一条传递函数，保证"存的线性系数 = 采样回来的线性系数"）
        // ==================================================================

        static float SrgbToLinear(float c)
        {
            c = Mathf.Clamp01(c);
            return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        static Color SrgbToLinear(Color c)
        {
            return new Color(SrgbToLinear(c.r), SrgbToLinear(c.g), SrgbToLinear(c.b), c.a);
        }

        static float LinearToSrgb(float c)
        {
            c = Mathf.Clamp01(c);
            return c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
        }

        static byte ToByte(float v)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
        }

        /// <summary>逐通道 <c>base / mean</c>：得到"相对平均色"的系数，色相起伏保留、明度均值归 1。</summary>
        static Color RatioToMean(Color baseLin, Color meanLin)
        {
            return new Color(
                baseLin.r / Mathf.Max(meanLin.r, 1e-4f),
                baseLin.g / Mathf.Max(meanLin.g, 1e-4f),
                baseLin.b / Mathf.Max(meanLin.b, 1e-4f),
                1f);
        }

        // ==================================================================
        // 文件 / 目录辅助（与 FxAssetBuilder 同款，刻意重复以免耦合）
        // ==================================================================

        static string ToAbsolutePath(string assetPath)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), assetPath);
        }

        static bool BytesEqual(string absolutePath, byte[] bytes)
        {
            if (!File.Exists(absolutePath))
                return false;

            var existing = File.ReadAllBytes(absolutePath);
            if (existing.Length != bytes.Length)
                return false;

            for (int i = 0; i < bytes.Length; i++)
            {
                if (existing[i] != bytes[i])
                    return false;
            }
            return true;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
