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
    ///   6 张 **512×512**（v2 提档；原 256²。风格指南 §3.3 给的是 256² 下限，512² 让最细八度
    ///   从 1-2 texel/格 变成 2-4 texel/格 —— 同一世界波长下采样更充分、不再靠"每格 1 texel
    ///   的哈希白噪"撑细节。内存代价：RGBA32 + mip 约 1.33 MB/张，6 张 ≈ 8 MB，可接受）。
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
    /// 【无缝（tileable）】全部噪声用整数周期格点的 value 噪声，频率按 2 的幂递增（4/8/16/32…）；
    ///   每个八度在 <c>(u,v)∈[0,1)</c> 上恰好整数个格点周期 → **底层场是以 1 为周期的连续函数**，
    ///   这是平铺无缝的充要条件（v2 的域名扭曲同样满足：扭曲场本身是周期场，故 u=0 与 u=1 的位移
    ///   逐点相等；python 复算实测 f(u=0)-f(u=1) ≤ 1.4e-14）。生成器按 (x+0.5)/size 的**像素中心**
    ///   口径采样，与周期场配合即为标准无缝平铺。法线用**前向差分 + 周期回绕**（<c>(x+1)%size</c>），
    ///   所以左右/上下边缘的梯度也连续，不会在平铺接缝处出现一条硬法线断线。
    ///
    /// 【强度不是拍脑袋】法线强度用「整张高度场的梯度 RMS 归一到目标 RMS 斜率」反推
    ///   （<see cref="BuildNormalMap"/>），目标值直接对应坡角：沙 0.07(≈4°)、草 0.16(≈9°)、
    ///   岩 0.28(≈15.6°)。这样换噪声函数也不会把法线搞暴；albedo 则实测并打印线性明度标准差，
    ///   作为 P-9「同材质窗内 std」的**程序化判据**（AGENTS.md 要求判图用程序化判据，不靠"看着像"）。
    ///
    /// 【幂等】再次执行时字节一致则不重写文件（同 FxAssetBuilder），只校正导入设置；.mat 不在此类改。
    ///
    /// 【r5 对比提档（本类改动）】r4 复验实测 unit-closeup 沙面 120×120 窗 std 只有 2-3、量化色仅 5 种：
    ///   生成端乘性系数起伏太弱。r5 把明度扰动由 ±6% 提到 **±12-18%**（沙 ±18%、草 ±15%、岩 ±14%），
    ///   细粒由 ±4% 提到 **±8%**（三族都补 period-192 细粒），并同步提高沙的**色斑对比**
    ///   （主色斑 2.4→3.2、中频色斑 1.4→2.4）；均值保持的两遍归一化逻辑不变（平均色零漂移）。
    ///   复算（python，生成端逐行同构）：沙乘性系数线性明度 std 0.113→0.149、存储 8bit 灰度 std
    ///   9.6→12.6；草 14.4→15.0、岩 22.3→22.7（8bit std，全族 ≥12）。
    ///   配套的"世界采样尺度放大 5-7×"在 shader / BattleSceneLighting 侧（UV = 世界XZ×_NoiseWorldScale），
    ///   本类的贴图内容尺寸不变（r5 时仍 256²；**v2 已提到 512²，见下节**）。
    ///
    /// 【v2 贴图提档（本类当前实现；观感：从"PS 云彩"变"揉皱的有机斑块"）】
    ///   用户复验反馈"贴图质量也很低"——旧图 256²、纯 value-FBM 4 阶，观感廉价，具体两个成因：
    ///     ① **每格 1 texel 的哈希白噪**撑最细八度（period 256 @ 256²）→ 近景是"抖动的噪点"而不是纹理；
    ///     ② **value-FBM 的方格团块**（bilinear 格子 → 等值线偏方正、局部梯度强度过于均匀）→ "PS 云彩"。
    ///   三处改动（**只动"看起来像什么"，不动"有多亮/多花"**）：
    ///     1. **分辨率 256² → 512²**（六张全部）：同一世界波长下最细八度从 1 texel/格 变 2 texel/格，
    ///        采样更充分；导入 aniso 4 → **8**（地面是 45° 俯视近水平面，斜视更锐）。mip 保持开启。
    ///     2. **value-FBM → 域名扭曲 fBm（domain warp）**：两轮低频噪声场（period 2、2 阶）扰动采样
    ///        坐标后再取目标 fBm —— <see cref="WarpedFbm"/>。低频/中频八度全走扭曲版（消除团块的方正
    ///        边界与"同一个斑块尺寸重复"的云彩感）；**最细的 period 192/256 八度刻意留纯 Fbm**
    ///        （高频扭曲肉眼不可见，白白多花 4 倍噪声求值）。
    ///        强度取 WarpStrength1=0.28 / WarpStrength2=0.14（uv 单位 × [-0.5,0.5] → 最大位移 ±0.14 uv）：
    ///        python 复算 512² 显示等值线长 +4.0%、|grad|CV +10.0%（局部细节"疏密不均"）、FFT 角向能量
    ///        CV -25.5%（方格轴向印记减弱），而**场 std 只 +4.4%**（扭曲搬动格点、不改变分布）。
    ///     3. 法线仍从"扭曲后的高度场"求梯度（<see cref="BuildNormalPixels"/> 的 RMS 归一逻辑不变，
    ///        沙 0.07≈4° 的温和档不变）；沙最细八度权重 0.044 → **0.075**：扭曲把中频的梯度能量抬高了，
    ///        权重不补则细粒占比会从 20% 掉到 7.5%（512² 复算：0.075 → 19.1%，维持"近景有砂粒感"的原意）。
    ///   **不变量（这两条是硬约束，改噪声函数也不许破）**：
    ///     · 均值零漂移 —— 两遍归一化后乘性系数均值**精确** 1.0（复算：沙/草/岩 lumaMean 全 1.0000）；
    ///     · 无缝 —— 扭曲场与目标场都是周期 1 的连续函数（复算：f(u=0)-f(u=1) ≤ 1.4e-14）。
    ///   【复算证据】<c>external/harness-t2/noise-recompute-v2.py</c>（不启动 Unity、numpy 复算）：
    ///     沙 stored8bitStd 12.57→**12.82**（判据 P-9 目标 ≥12）、草 14.96→15.00、岩 22.52→22.61；
    ///     跨接缝相邻差分 ≤ 内部差分的 0.91×（无接缝跳变）。
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

        /// <summary>
        /// 贴图边长。**v2：256 → 512**（六张全部；风格指南 §3.3 的 256² 是下限口径）。
        /// 512² 让最细八度（period 192/256）从"每格 1 texel 的哈希白噪"变成 2-4 texel/格；
        /// 内存 RGBA32 + mip 约 1.33 MB/张、6 张 ≈ 8 MB（Uncompressed 是刻意取舍，见 <see cref="ConfigureImporter"/>）。
        /// </summary>
        public const int TextureSize = 512;

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
        ///     （512² 下 6 张共约 8 MB，是刻意用内存换质量：本工程是 PC 作品集，不是移动端。）
        ///   · <c>anisoLevel = 8</c>：地面是 45° 俯视的近水平面，各向异性过滤让远处贴图不糊。
        ///     （v2：4 → 8。512² 的高频细节更细，斜视时低 aniso 会先糊掉细粒。）
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
            settings.aniso = 8;
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

            stat = "albedo " + size + "²（v2 域名扭曲），乘性系数均值 " + lumaMean.ToString("0.000")
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

        // 沙 albedo（任务书：基色 #E8D5A3/#C4A76A 之间按噪声取色 + 明度扰动 + 稀疏贝壳白点 <0.5%）
        //   参数表（v2 提档；括号内为 r5 值）：色斑走**域名扭曲 fBm**（低频/中频全部扭曲，
        //           最细 period-192 细粒保持纯 Fbm）；主色斑周期 4、对比拉伸 3.2、权重 0.65；
        //           中频色斑周期 16、对比拉伸 2.4、权重 0.35；明度扰动周期 64、幅度 ±18%；
        //           细颗粒周期 192、幅度 ±8%；
        //           贝壳：16×16 单元格、4.5% 单元格出点、半径 0.06-0.11 格 → 覆盖率 ≈0.14%（<0.5%，
        //           与贴图尺寸无关：半径以"格"为单位，512² 与 256² 的覆盖率相同）。
        static Color SandAlbedoFactor(float u, float v, int size)
        {
            float nPatch = Contrast(WarpedFbm(u, v, 4, 4, 4, SeedSandPatch, WarpSeedSand), 3.2f);
            float nMid   = Contrast(WarpedFbm(u, v, 16, 16, 3, SeedSandMid, WarpSeedSand), 2.4f);
            float mix    = Mathf.Clamp01(0.65f * nPatch + 0.35f * nMid);

            Color lightLin = SrgbToLinear(SandLightSrgb);
            Color midLin   = SrgbToLinear(SandMidSrgb);
            Color meanLin  = (lightLin + midLin) * 0.5f;
            Color baseLin  = Color.Lerp(lightLin, midLin, mix);

            // 明度扰动 ±18% + 细颗粒 ±8%（都作用在"系数"上，故不破坏平均色；两遍归一化在 BuildAlbedoPixels）
            float lum = 1f + (WarpedFbm(u, v, 64, 64, 2, SeedSandGrain, WarpSeedSand) - 0.5f) * 2f * 0.18f;
            float fine = 1f + (Fbm(u, v, 192, 192, 2, SeedSandFine) - 0.5f) * 2f * 0.08f;

            Color factor = RatioToMean(baseLin, meanLin) * (lum * fine);

            // 稀疏贝壳白点：亮而小，靠 mask 混入（覆盖率极低 → 均值几乎不动）
            float shell = ShellMask(u, v, size, SeedSandShell);
            if (shell > 0.001f)
                factor = Color.Lerp(factor, RatioToMean(SrgbToLinear(SandShellSrgb), meanLin), shell * 0.85f);

            return factor;
        }

        // 草 albedo（任务书：#4A8C4A/#7BC67E 双色 patch）
        //   参数表（v2）：双色 patch 走域名扭曲 fBm，周期 6、对比拉伸 2.2；细碎斑（扭曲）周期 24、权重 0.3；
        //           明度扰动（扭曲）周期 64、幅度 ±15%；
        //           细粒周期 192、幅度 ±8%（纯 Fbm，高频不扭曲）；
        //           深色草缝（叶隙阴影）用周期 48 的阈值噪声打点，最多压暗 22%（纯 Fbm）。
        static Color GrassAlbedoFactor(float u, float v)
        {
            float nPatch = Contrast(WarpedFbm(u, v, 6, 6, 4, SeedGrassPatch, WarpSeedGrass), 2.2f);
            float nMid   = Contrast(WarpedFbm(u, v, 24, 24, 3, SeedGrassMid, WarpSeedGrass), 1.3f);
            float mix    = Mathf.Clamp01(0.7f * nPatch + 0.3f * nMid);

            Color darkLin  = SrgbToLinear(GrassDarkSrgb);
            Color lightLin = SrgbToLinear(GrassLightSrgb);
            Color meanLin  = (darkLin + lightLin) * 0.5f;
            Color baseLin  = Color.Lerp(darkLin, lightLin, mix);

            // 草叶/叶隙：高频阈值噪声 → 细碎的暗点（让"绒毛感"来自纹理而非只靠法线）
            float gap = Mathf.SmoothStep(0f, 0.06f, Fbm(u, v, 48, 48, 2, SeedGrassGap) - 0.62f);
            float lum  = 1f + (WarpedFbm(u, v, 64, 64, 2, SeedGrassGrain, WarpSeedGrass) - 0.5f) * 2f * 0.15f;
            float fine = 1f + (Fbm(u, v, 192, 192, 2, SeedGrassAlbedoFine) - 0.5f) * 2f * 0.08f;

            return RatioToMean(baseLin, meanLin) * (lum * fine * (1f - 0.22f * gap));
        }

        // 岩 albedo（任务书：#8C7B6A 基础上斑块 + 裂缝暗线（阈值化噪声））
        //   参数表（v2）：斑块在暗档 #5C4F42 与亮档 #B8A99A 间按**域名扭曲** FBM（周期 5，对比 2.0）取色；
        //           裂缝 = 阈值化**扭曲** FBM（周期 8、4 阶），|n-0.5| < 0.035 处为暗线，最多压暗 45%；
        //           岩层的各向异性拉长（周期 (6,24)，扭曲）给沉积纹理；
        //           明度扰动（扭曲）周期 96、幅度 ±14%；细粒周期 192、幅度 ±8%（纯 Fbm，高频不扭曲）。
        static Color RockAlbedoFactor(float u, float v)
        {
            float nPatch = Contrast(WarpedFbm(u, v, 5, 5, 4, SeedRockPatch, WarpSeedRock), 2.0f);
            float nStrata = Contrast(WarpedFbm(u, v, 6, 24, 3, SeedRockStrata, WarpSeedRock), 1.2f);
            float mix = Mathf.Clamp01(0.7f * nPatch + 0.3f * nStrata);

            Color darkLin  = SrgbToLinear(RockDarkSrgb);
            Color lightLin = SrgbToLinear(RockLightSrgb);
            Color meanLin  = (darkLin + lightLin) * 0.5f;
            Color baseLin  = Color.Lerp(darkLin, lightLin, mix);

            float crack = CrackMask(u, v);
            float lum  = 1f + (WarpedFbm(u, v, 96, 96, 2, SeedRockGrain, WarpSeedRock) - 0.5f) * 2f * 0.14f;
            float fine = 1f + (Fbm(u, v, 192, 192, 2, SeedRockAlbedoFine) - 0.5f) * 2f * 0.08f;

            return RatioToMean(baseLin, meanLin) * (lum * fine * (1f - 0.45f * crack));
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

            stat = "normal " + size + "²（v2 域名扭曲），高度场梯度 RMS " + rms.ToString("0.0000")
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
        //   叠加：低频起伏（周期 16，扭曲）+ 细沙扰动（周期 64，扭曲，权重 0.25）+ 最细粒（周期 256，
        //   纯 Fbm，权重 **0.075**）—— 让特写近景有"砂粒感"，而不是只有波纹。
        //   【v2：权重 0.044 → 0.075】法线图最终按整张高度场的梯度 RMS 归一，对"起伏感知"的贡献是
        //   **梯度能量**：domain warp 用链式法则把中频段（ripple/mid/fine）的梯度整体抬高（复算：
        //   扭曲后 totalGradRMS 0.0571→0.0611），若不补权重，ultra 占比会从 20.2% 掉到 7.5%。
        //   0.075 在 512² 复算下回到 **19.1%**，维持"近景有砂粒感"的原意（python 见报告复算表）。
        static float SandHeight(float u, float v)
        {
            float phase = 2f * Mathf.PI * (5f * u + 3f * v)
                        + (WarpedFbm(u, v, 4, 4, 3, SeedSandRippleWarp, WarpSeedSand) - 0.5f) * Mathf.PI * 2f * 0.55f;
            float ripple = Mathf.Sin(phase);
            float mid    = (WarpedFbm(u, v, 16, 16, 2, SeedSandNormalMid, WarpSeedSand) - 0.5f) * 2f;
            float fine   = (WarpedFbm(u, v, 64, 64, 3, SeedSandNormalFine, WarpSeedSand) - 0.5f) * 2f;
            float ultra  = (Fbm(u, v, 256, 256, 2, SeedSandNormalUltra) - 0.5f) * 2f;
            return ripple * 0.75f + mid * 0.5f + fine * 0.25f + ultra * 0.075f;
        }

        // 草高度：各向异性"草叶"（沿 v 拉长：u 高频 48 / v 低频 16，扭曲）+ 中频草簇（周期 8，扭曲）
        //   + 细碎（周期 96，纯 Fbm —— 高频扭曲肉眼不可见，白花 4× 噪声求值）。
        static float GrassHeight(float u, float v)
        {
            float blade = (WarpedFbm(u, v, 48, 16, 3, SeedGrassBlade, WarpSeedGrass) - 0.5f) * 2f;
            float clump = (WarpedFbm(u, v, 8, 8, 3, SeedGrassClump, WarpSeedGrass) - 0.5f) * 2f;
            float fine  = (Fbm(u, v, 96, 96, 2, SeedGrassFine) - 0.5f) * 2f;
            return blade * 0.55f + clump * 0.7f + fine * 0.2f;
        }

        // 岩高度：裂缝（阈值化扭曲噪声 → 窄脊，做成**负**高度 = 凹陷）+ 各向异性岩层（沿 v 拉长）+ 块状起伏。
        static float RockHeight(float u, float v)
        {
            float crack  = CrackMask(u, v);
            float strata = (WarpedFbm(u, v, 6, 24, 4, SeedRockStrataN, WarpSeedRock) - 0.5f) * 2f;
            float chunk  = (WarpedFbm(u, v, 10, 10, 3, SeedRockChunk, WarpSeedRock) - 0.5f) * 2f;
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
        const int SeedSandNormalUltra= 139;   // r5：沙法线最细一档（frequency 256）
        const int SeedGrassPatch     = 201;
        const int SeedGrassMid       = 203;
        const int SeedGrassGrain     = 207;
        const int SeedGrassGap       = 209;
        const int SeedGrassBlade     = 211;
        const int SeedGrassClump     = 223;
        const int SeedGrassFine      = 227;
        const int SeedGrassAlbedoFine= 229;   // r5：草 albedo 细粒（period 192）
        const int SeedRockPatch      = 301;
        const int SeedRockStrata     = 303;
        const int SeedRockGrain      = 307;
        const int SeedRockCrack      = 311;
        const int SeedRockStrataN    = 313;
        const int SeedRockChunk      = 317;
        const int SeedRockAlbedoFine = 331;   // r5：岩 albedo 细粒（period 192）

        // ---- 域名扭曲（domain warp）参数（v2 核心）----
        // 一族一个扭曲场种子：三族的"揉皱形状"互不相同，避免沙/草/岩的斑块在同一处以同一方向鼓包
        // （地形材质是把三族贴图按权重混合的，形状相关性太高会在过渡带露出"同一张图案"的痕迹）。
        const int   WarpSeedSand  = 900001;
        const int   WarpSeedGrass = 910001;
        const int   WarpSeedRock  = 920001;
        /// <summary>扭曲场基准周期（低频：整张图上只有 2 个格点周期 → 只做大尺度"揉皱"）。</summary>
        const int   WarpLowPeriod = 2;
        /// <summary>扭曲场阶数（低频场 2 阶足够）。</summary>
        const int   WarpLowOctaves = 2;
        /// <summary>同一次扭曲的四个噪声场（两轮 × 两轴）之间的种子间隔。
        /// 必须 &gt; 131×阶数（<see cref="Fbm"/> 内部每阶偏移 131），否则会与"阶间种子"撞车 →
        /// 扭曲场的 x/y 分量互相相关（表现为整张图沿对角方向被拉长）。</summary>
        const int   WarpSeedSlotStride = 10007;
        /// <summary>第一轮位移幅度（uv 单位 × [-0.5,0.5] → 最大位移 ±0.14 uv）。</summary>
        const float WarpStrength1 = 0.28f;
        /// <summary>第二轮位移幅度（在已扭曲坐标上**复合**再偏一次，更小；两轮才有"折痕/涡旋"感）。</summary>
        const float WarpStrength2 = 0.14f;

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

        /// <summary>
        /// 域名扭曲 fBm（domain warp，v2 核心）：用**两轮低频噪声场**扰动采样坐标后再取目标 fBm。
        ///
        /// 【为什么需要它】纯 value-FBM 的等值线偏方正、局部梯度强度过于均匀（每个斑块尺寸雷同）——
        ///   肉眼读作"PS 云彩"。把采样坐标先揉皱（domain warp）后，斑块边界变得蜿蜒、局部细节
        ///   疏密不均，像一个被"揉皱的有机图案"而不是"平滑的云"。
        ///   复算（512²，period-4 patch）：等值线长 +4.0%、|grad|CV +10.0%、FFT 角向能量 CV −25.5%，
        ///   而**场 std 只 +4.4%** —— 扭曲搬动格点、不改变分布（所以"多花/多亮"的观感口径不变）。
        ///
        /// 【为什么仍然无缝】扭曲场本身由 <see cref="Fbm"/> 生成 → 以 1 为周期的连续函数，
        ///   故 u=0 与 u=1 处的位移**逐点相等**；目标 fBm 也是周期 1 的 → 复合后仍周期 1（复算实测
        ///   |f(u=0)-f(u=1)| ≤ 1.4e-14，即浮点舍入级）。
        ///
        /// 【性能】一次扭曲 = 目标 Fbm + 4 次低频 Fbm（2 阶）≈ 2.5× 单纯 Fbm 的噪声求值量；
        ///   512²×6 张的构建耗时仍在秒级（构建期一次性，不进运行期热路径）。
        /// </summary>
        /// <param name="warpSeed">一族一个（<see cref="WarpSeedSand"/> 等）。</param>
        static float WarpedFbm(float u, float v, int periodX, int periodY, int octaves, int seed, int warpSeed)
        {
            // 第 1 轮：低频场 → 采样坐标位移。
            float qx = Fbm(u, v, WarpLowPeriod, WarpLowPeriod, WarpLowOctaves, warpSeed) - 0.5f;
            float qy = Fbm(u, v, WarpLowPeriod, WarpLowPeriod, WarpLowOctaves, warpSeed + WarpSeedSlotStride) - 0.5f;
            float u1 = u + qx * WarpStrength1;
            float v1 = v + qy * WarpStrength1;

            // 第 2 轮：在**已扭曲**坐标上再取一次低频场并复合（两轮 = 折痕/涡旋；一轮只是平移）。
            float rx = Fbm(u1, v1, WarpLowPeriod, WarpLowPeriod, WarpLowOctaves, warpSeed + 2 * WarpSeedSlotStride) - 0.5f;
            float ry = Fbm(u1, v1, WarpLowPeriod, WarpLowPeriod, WarpLowOctaves, warpSeed + 3 * WarpSeedSlotStride) - 0.5f;
            float u2 = u1 + rx * WarpStrength2;
            float v2 = v1 + ry * WarpStrength2;

            return Fbm(u2, v2, periodX, periodY, octaves, seed);
        }

        /// <summary>把 [0,1] 的噪声按对比 k 拉伸到 [0,1]（value 噪声分布偏集中，不拉伸斑块太淡）。</summary>
        static float Contrast(float n, float k)
        {
            return Mathf.Clamp01(0.5f + (n - 0.5f) * k);
        }

        /// <summary>裂缝掩码：|n-0.5| &lt; 0.035 处为 1（窄线），否则 0，边缘 0.035 宽软过渡。无缝（n 无缝）。
        /// v2：n 走域名扭曲 → 裂缝不再是"方正的格子边"，而是蜿蜒的岩裂。</summary>
        static float CrackMask(float u, float v)
        {
            float n = WarpedFbm(u, v, 8, 8, 4, SeedRockCrack, WarpSeedRock);
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
