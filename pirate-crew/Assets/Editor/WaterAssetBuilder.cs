using System.IO;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.Water;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 水面模拟的构建期资产烘焙入口：把"地形高于水面的格"烘成一张障碍图，
    /// 供 <c>PirateWater.shader</c> 采样（障碍接触带泡沫加亮）与
    /// <see cref="WaterSimulationDriver"/> 读取（诺伊曼反射墙）。
    ///
    /// 【产物】<c>Assets/Art/Textures/Water/WaterObstacleMap.png</c>
    ///   · 128×128（= 模拟域格数），R 通道 255 = 障碍（岛/礁，反射墙），0 = 开阔水；
    ///   · 线性（sRGB off）、无 mipmap、Clamp、Bilinear、Is Readable（驱动要 GetPixels32）；
    ///   · 覆盖 64×64 世界单位、以竞技场中心为中心 —— 与 <see cref="WaterSimulationDriver"/>
    ///     的默认域参数一致，改一边必须改另一边。
    ///
    /// 【数据来源】<see cref="TerrainCatalog.Build"/>（只读，不改地形模块）：
    ///   <c>grid.SurfaceWorldYAtWorld(worldX, worldZ) &gt;= LevelGeometry.WaterSurfaceY</c> 视为障碍。
    ///   竞技场内部（沙岛本体）整片是障碍 —— 这正是"浪拍岸反射"要的边界。
    ///   岛外海床台阶在水面之下 → 不是障碍（只写深度，供浅深水色/焦散）。
    ///
    /// 【缺口，留给协调者】SceneArt 的船体/礁石没有公开几何查询接口，本烘焙**只含静态地形**。
    ///   需要把礁石也算障碍时，请给 SceneArt 加一个"返回占位 AABB/格列表"的公开 API，再在这里并集进来。
    ///
    /// 【无头入口】<c>-executeMethod PirateCrew.EditorTools.WaterAssetBuilder.BakeObstacleMap</c>
    /// </summary>
    public static class WaterAssetBuilder
    {
        public const string TextureFolder = "Assets/Art/Textures/Water";
        public const string ObstacleMapPath = TextureFolder + "/WaterObstacleMap.png";

        /// <summary>
        /// 海水材质资产路径（<see cref="BattleSceneLighting.WaterMaterial"/> 的同名资产）。
        /// 材质参数由本类 <see cref="ApplyMaterialDefaults"/> 显式落盘。
        /// </summary>
        public const string OceanMaterialPath = "Assets/Art/Materials/Environment/Water_Ocean.mat";

        /// <summary>海水 shader 名（与 PirateWater.shader 的 Shader 声明一致）。</summary>
        public const string WaterShaderName = "PirateCrew/PirateWater";

        /// <summary>
        /// M4 大海域海面材质路径（<see cref="OceanRig"/> 的推荐显式材质）。
        /// 与上方 <see cref="OceanMaterialPath"/>（旧 PirateWater 用的 Water_Ocean.mat）是两个资产。
        /// 参数由 <see cref="ApplyOceanMaterialDefaults"/> 显式落盘。
        /// </summary>
        public const string OceanM4MaterialPath = "Assets/Art/Materials/Environment/Ocean_Water.mat";

        /// <summary>大海域 shader 名（与 Art/Shaders/Ocean/PirateOcean.shader 的声明一致）。</summary>
        public const string OceanShaderName = "PirateCrew/Ocean";

        /// <summary>Battle 场景未打开时的兜底关卡号（与 Assets/Scenes/Battle.unity 的 fallbackLevelNumber 一致）。</summary>
        public const int FallbackLevelNumber = 1;

        [MenuItem("Tools/PirateCrew/Water/Bake Obstacle Map")]
        public static void BakeObstacleMap()
        {
            int levelNumber = ResolveLevelNumber();
            LevelData level = LevelCatalog.Get(levelNumber);
            if (level.WidthTiles <= 0 || level.HeightTiles <= 0)
            {
                Debug.LogError("[WaterAssetBuilder] 关卡 " + levelNumber + " 尺寸无效，烘焙中止。");
                return;
            }

            int width = level.WidthTiles;
            int depth = level.HeightTiles;
            TileTerrainGrid grid = TerrainCatalog.Build(levelNumber, width, depth);
            bool transcribed = grid != null;
            if (grid == null)
                grid = TileTerrainGrid.Flat(width, depth);

            int cells = WaterSimRules.DefaultCellsPerAxis;
            float domainSize = WaterSimRules.DefaultDomainSize;
            var center = new Vector2(width * 0.5f, depth * 0.5f);
            float waterY = LevelGeometry.WaterSurfaceY;

            // 判据在纯 C# 的 ObstacleMapRules 里（无头可测），这里只做编码。
            bool[] mask = ObstacleMapRules.Bake(grid, center, domainSize, cells, waterY, width, depth);
            int obstacleCount = ObstacleMapRules.CountObstacles(mask);

            var pixels = new Color32[cells * cells];
            for (int i = 0; i < mask.Length; i++)
            {
                bool isObstacle = mask[i];
                byte r = isObstacle ? (byte)255 : (byte)0;
                // 障碍用暖红、水用深蓝，便于直接人眼核对（shader 只读 R 通道）。
                byte g = isObstacle ? (byte)40 : (byte)20;
                byte b = isObstacle ? (byte)40 : (byte)120;
                pixels[i] = new Color32(r, g, b, 255);
            }

            EnsureFolder();
            var tex = new Texture2D(cells, cells, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(ObstacleMapPath, png);
            AssetDatabase.ImportAsset(ObstacleMapPath, ImportAssetOptions.ForceUpdate);
            ApplyImporterSettings(cells);

            Debug.Log("[WaterAssetBuilder] 障碍图烘焙完成：" + ObstacleMapPath
                      + " | 关卡 " + levelNumber + (transcribed ? "（已转写地形）" : "（未转写→平坦地面）")
                      + " | 域中心 (" + center.x + ", " + center.y + ") 边长 " + domainSize
                      + " | 障碍格 " + obstacleCount + " / " + (cells * cells));

            // 材质参数在此一并落盘：ArtGate 的步骤 ①（BattleSceneLighting.BuildAll）会用旧默认值写一遍
            // Water_Ocean.mat，而本步骤（⑥.5）在其后执行 —— 顺序保证 PirateWater.shader 的新默认值生效。
            ApplyMaterialDefaults();
            ApplyOceanMaterialDefaults();
        }

        static int ResolveLevelNumber()
        {
            BattleController battle = Object.FindObjectOfType<BattleController>();
            if (battle != null)
                return battle.LevelNumber;
            return FallbackLevelNumber;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/Textures"))
                    AssetDatabase.CreateFolder("Assets/Art", "Textures");
                AssetDatabase.CreateFolder("Assets/Art/Textures", "Water");
            }
        }

        static void ApplyImporterSettings(int size)
        {
            var importer = AssetImporter.GetAtPath(ObstacleMapPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[WaterAssetBuilder] 找不到 TextureImporter: " + ObstacleMapPath);
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;          // 数据图，不是颜色
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.isReadable = true;            // 驱动要 GetPixels32
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(64, size);
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------
        // 海水材质参数落盘
        // ------------------------------------------------------------------

        /// <summary>
        /// 把 <c>PirateWater.shader</c> 的默认参数**显式写进** <see cref="OceanMaterialPath"/>。
        ///
        /// 【为什么必须落盘而不是只改 shader Properties 默认值】
        ///   Water_Ocean.mat 里已序列化了一整份旧参数（如 <c>_ShallowColor #4DA6D9</c>、
        ///   <c>_ReflectionStrength 0.55</c>）——序列化值优先于 shader 默认值，只改 shader 不会生效。
        ///   本方法把新默认值逐条写进材质资产，保证出图用的就是这批参数（r7：含下调后的四个镜射权重）。
        ///
        /// 【幂等】可重复执行；材质文件缺失时按 <see cref="WaterShaderName"/> 新建。
        /// 【调用点】<see cref="BakeObstacleMap"/> 末尾（ArtGate 步骤 ⑥.5，晚于步骤 ① 的材质生成）；
        ///   也可单独用菜单 <c>Tools/PirateCrew/Water/Apply Ocean Material Defaults</c>
        ///   或无头 <c>-executeMethod PirateCrew.EditorTools.WaterAssetBuilder.ApplyMaterialDefaults</c>。
        /// </summary>
        [MenuItem("Tools/PirateCrew/Water/Apply Ocean Material Defaults")]
        public static void ApplyMaterialDefaults()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(OceanMaterialPath);
            if (m == null)
            {
                Shader shader = Shader.Find(WaterShaderName);
                if (shader == null)
                {
                    Debug.LogError("[WaterAssetBuilder] 找不到 shader " + WaterShaderName + "，跳过海水材质参数落盘。");
                    return;
                }

                EnsureMaterialFolder();
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, OceanMaterialPath);
                Debug.Log("[WaterAssetBuilder] 新建海水材质：" + OceanMaterialPath);
            }

            // ---- r6 审美校正：三档水色回蓝（暖金只留在太阳镜射瓣 + 掠射 sheen）----
            // r5 把暖色铺进基础色/反射 → 整片海读成泥黄浊水；r6 回蓝：比 r4 暗蓝亮，但明确是蓝。
            SetColor(m, "_ShallowColor", "#4FA8CC");
            SetColor(m, "_MidColor", "#2E86B5");
            SetColor(m, "_DeepColor", "#1E5E88");
            // 浅→深完成深度 4 → 5：同深度下更多面积停留在较亮档 = 降等效吸收系数。
            SetFloat(m, "_ShoreFadeDistance", 5f);
            // ---- 太阳光路（M4 实拍修正：高机位俯视整海染金的收紧项，与 shader 默认值同源）----

            // ---- 大尺度低频破坏噪声（打断 34-81px 可见重复花纹 / tiling 自相关）----
            SetFloat(m, "_BreakupScale", 0.0222f);      // ≈1/45 世界单位（世界尺度 30-60 内）
            SetFloat(m, "_BreakupTintDepth", 0.04f);    // ±4%，均值零漂移
            SetFloat(m, "_BreakupSpeed", 0.06f);

            // ---- 太阳光路（暖金 + 平水面镜射宽瓣；r6 删除伪地平线暖带，r7 下调镜射权重让水读蓝）----
            // 【r7 关键】r6 只回蓝了基础色、没动镜射权重 → 复验"水回蓝在渲染上没发生"（掠射亮部 hue
            // 与 r5 逐像素同为 42-45 暖金、中性灰 sat<0.10 占 20-56%）。落盘这四键与 shader Properties
            // 默认值同步：宽瓣主项 1.6→0.8、宽瓣辅项 0.65→0.4、窄瓣 8.0→5.0、掠射 sheen 0.20→0.10。
            // 判据：水窗蓝像素(hue 190-225, sat>0.15)>60%、中性灰<15%、暖亮仅 2-6% 且在太阳方位窄条。
            SetColor(m, "_SunSpecColor", "#FFDB73");       // (1.0, 0.86, 0.45) 暖金 hue≈45
            SetFloat(m, "_SunSpecBroadStrength", 0.5f);  // M4 实拍修正（0.8 旧机位口径）
            SetFloat(m, "_SunSpecLaneShininess", 24f);
            SetFloat(m, "_SunSpecWaveStrength", 0.4f);
            SetFloat(m, "_SunSpecBroadShininess", 50f);
            SetFloat(m, "_SunSpecLaneWidth", 2.5f);
            SetFloat(m, "_SunSpecPatchScale", 8f);
            SetFloat(m, "_SunSpecPatchDepth", 0.40f);
            SetFloat(m, "_SunSpecCrestBias", 0.25f);  // M4 实拍修正（整海染金主因之一）
            SetFloat(m, "_SunSpecSlopeBoost", 12f);
            SetFloat(m, "_SunSpecStrength", 5f);
            SetFloat(m, "_SunSpecShininess", 320f);
            SetFloat(m, "_SunSpecGlitter", 0.55f);
            SetFloat(m, "_SunSheenStrength", 0.10f);

            // ---- 亮点不再被中性白加色拉青：镜面降权、反射降权（r6：1.0 → 0.7，让基础蓝透出来）----
            SetFloat(m, "_SpecularIntensity", 0.5f);
            SetFloat(m, "_ReflectionStrength", 0.7f);
            SetFloat(m, "_RefractionBlend", 0.22f);

            // ---- 其余参数与 shader 默认值对齐（避免旧序列化值残留）----
            SetColor(m, "_CausticColor", "#CCFFEB");       // (0.80, 1.0, 0.92)
            SetFloat(m, "_CausticStrength", 0.30f);
            SetFloat(m, "_CausticScale", 0.55f);
            SetFloat(m, "_CausticSpeed", 0.35f);
            SetFloat(m, "_CausticWarp", 0.60f);
            SetFloat(m, "_CausticDepthFade", 2.2f);

            SetColor(m, "_FoamColor", "#F0F7FF");          // (0.941, 0.969, 1.0)
            SetFloat(m, "_FoamWidth", 1.6f);
            SetFloat(m, "_FoamNoiseScale", 5f);
            SetFloat(m, "_FoamNoiseScale2", 13f);
            SetFloat(m, "_FoamSpeed", 0.25f);
            SetFloat(m, "_FoamStrength", 0.85f);
            SetFloat(m, "_ShorelineFoamGain", 1.6f);
            SetFloat(m, "_FoamBreakup", 0.45f);
            SetFloat(m, "_FoamPulseSpeed", 0.55f);
            SetFloat(m, "_FoamPulseFrequency", 1.2f);
            SetFloat(m, "_FoamPulseStrength", 0.60f);

            SetFloat(m, "_RefractionStrength", 0.035f);
            SetFloat(m, "_RefractionDepthFade", 1.6f);
            SetFloat(m, "_HeightFieldNormalStrength", 0.35f);
            SetFloat(m, "_HeightFieldFoamStrength", 0.60f);
            SetFloat(m, "_ObstacleFoamBoost", 1.20f);
            SetFloat(m, "_FresnelPower", 5f);
            SetFloat(m, "_FresnelStrength", 1f);
            SetFloat(m, "_Smoothness", 0.92f);
            SetFloat(m, "_Opacity", 0.94f);   // M4 实拍修正：0.82 近场看穿海床
            SetFloat(m, "_DebugMode", 0f);

            SetVector(m, "_W1Dir", new Vector4(1f, 0f, 0.25f, 0f));
            SetFloat(m, "_W1Length", 13f);
            SetFloat(m, "_W1Amp", 0.055f);
            SetFloat(m, "_W1Steep", 0.65f);
            SetFloat(m, "_W1Speed", 1f);
            SetVector(m, "_W2Dir", new Vector4(0.6f, 0f, 1f, 0f));
            SetFloat(m, "_W2Length", 7.5f);
            SetFloat(m, "_W2Amp", 0.04f);
            SetFloat(m, "_W2Steep", 0.60f);
            SetFloat(m, "_W2Speed", 1.15f);
            SetVector(m, "_W3Dir", new Vector4(-0.3f, 0f, 1f, 0f));
            SetFloat(m, "_W3Length", 4.2f);
            SetFloat(m, "_W3Amp", 0.028f);
            SetFloat(m, "_W3Steep", 0.55f);
            SetFloat(m, "_W3Speed", 1.30f);
            SetVector(m, "_W4Dir", new Vector4(1f, 0f, -0.5f, 0f));
            SetFloat(m, "_W4Length", 2.4f);
            SetFloat(m, "_W4Amp", 0.016f);
            SetFloat(m, "_W4Steep", 0.50f);
            SetFloat(m, "_W4Speed", 1.50f);

            SetFloat(m, "_WaveScaleA", 0.32f);
            SetFloat(m, "_WaveSpeedA", 0.45f);
            SetFloat(m, "_WaveStrengthA", 0.55f);
            SetVector(m, "_WaveDirectionA", new Vector4(1f, 0f, 0.35f, 0f));
            SetFloat(m, "_WaveScaleB", 0.95f);
            SetFloat(m, "_WaveSpeedB", 0.85f);
            SetFloat(m, "_WaveStrengthB", 0.28f);
            SetVector(m, "_WaveDirectionB", new Vector4(-0.4f, 0f, 1f, 0f));

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            Debug.Log("[WaterAssetBuilder] 海水材质参数已落盘：" + OceanMaterialPath
                      + "（水色回蓝 / 镜射四权重下调[宽瓣1.6→0.8,辅项0.65→0.4,窄瓣8→5,sheen0.2→0.1] / 反射 0.7 / 低频破坏噪声）");
        }

        static void EnsureMaterialFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art/Materials"))
                AssetDatabase.CreateFolder("Assets/Art", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Art/Materials/Environment"))
                AssetDatabase.CreateFolder("Assets/Art/Materials", "Environment");
        }

        // ------------------------------------------------------------------
        // M4 大海域海面材质落盘（PirateCrew/Ocean + OceanRig 用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把 M4 大海域参数显式写进 <see cref="OceanMaterialPath"/>（PirateCrew/Ocean shader）。
        ///
        /// 【单一事实源】波表/半径/淡出全部取自 <see cref="OceanRules"/> 与 <see cref="OceanGridRules"/>
        /// 常量及 <see cref="WaterRules.DefaultWaves"/>（chop ×2 口径——旧 Water_Ocean.mat 里的
        /// _W1Length=13 是格 1→2 换算前的遗留值，新海洋材质与 C# 契约对齐）。
        /// 【幂等】可重复执行；材质缺失时按 <see cref="OceanShaderName"/> 新建。
        /// 【调用点】<see cref="BakeObstacleMap"/> 末尾；也可单独菜单/无头
        /// <c>-executeMethod PirateCrew.EditorTools.WaterAssetBuilder.ApplyOceanMaterialDefaults</c>。
        /// </summary>
        [MenuItem("Tools/PirateCrew/Water/Apply Ocean (M4) Material Defaults")]
        public static void ApplyOceanMaterialDefaults()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(OceanM4MaterialPath);
            if (m == null)
            {
                Shader shader = Shader.Find(OceanShaderName);
                if (shader == null)
                {
                    Debug.LogError("[WaterAssetBuilder] 找不到 shader " + OceanShaderName
                                   + "（Ocean/PirateOcean.shader 未导入？），跳过 M4 海洋材质落盘。");
                    return;
                }

                EnsureMaterialFolder();
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, OceanM4MaterialPath);
                Debug.Log("[WaterAssetBuilder] 新建 M4 海洋材质：" + OceanM4MaterialPath);
            }

            // ---- 三档海水色（美术风格指南 §2.1：#4DA6D9 / #2B7AB8 / #1A4F7A）----
            SetColor(m, "_ShallowColor", "#4DA6D9");
            SetColor(m, "_MidColor", "#2B7AB8");
            SetColor(m, "_DeepColor", "#1A4F7A");
            SetFloat(m, "_ShoreFadeDistance", 5f);

            // ---- 长涌 swell ×2（OceanRules.DefaultSwellWaves）----
            SetVector(m, "_S1Dir", new Vector4(1f, 0f, 0.15f, 0f));
            SetFloat(m, "_S1Length", 120f);
            SetFloat(m, "_S1Amp", 1.10f);
            SetFloat(m, "_S1Steep", 0.75f);
            SetFloat(m, "_S1Speed", 1f);
            SetVector(m, "_S2Dir", new Vector4(0.55f, 0f, 1f, 0f));
            SetFloat(m, "_S2Length", 72f);
            SetFloat(m, "_S2Amp", 0.65f);
            SetFloat(m, "_S2Steep", 0.70f);
            SetFloat(m, "_S2Speed", 1.05f);

            // ---- 4 波 chop（WaterRules.DefaultWaves，格 1→2 单位 ×2 契约值）----
            SetVector(m, "_W1Dir", new Vector4(1f, 0f, 0.25f, 0f));
            SetFloat(m, "_W1Length", 26f);
            SetFloat(m, "_W1Amp", 0.110f);
            SetFloat(m, "_W1Steep", 0.65f);
            SetFloat(m, "_W1Speed", 1f);
            SetVector(m, "_W2Dir", new Vector4(0.6f, 0f, 1f, 0f));
            SetFloat(m, "_W2Length", 15f);
            SetFloat(m, "_W2Amp", 0.080f);
            SetFloat(m, "_W2Steep", 0.60f);
            SetFloat(m, "_W2Speed", 1.15f);
            SetVector(m, "_W3Dir", new Vector4(-0.3f, 0f, 1f, 0f));
            SetFloat(m, "_W3Length", 8.4f);
            SetFloat(m, "_W3Amp", 0.056f);
            SetFloat(m, "_W3Steep", 0.55f);
            SetFloat(m, "_W3Speed", 1.30f);
            SetVector(m, "_W4Dir", new Vector4(1f, 0f, -0.5f, 0f));
            SetFloat(m, "_W4Length", 4.8f);
            SetFloat(m, "_W4Amp", 0.032f);
            SetFloat(m, "_W4Steep", 0.50f);
            SetFloat(m, "_W4Speed", 1.50f);

            // ---- 网格密度与淡出（OceanGridRules / OceanRules 常量）----
            SetFloat(m, "_GridCellSize", OceanGridRules.CellSize);
            SetFloat(m, "_GridUniformRadius", OceanGridRules.UniformRadius);
            SetFloat(m, "_GridRingGrowth", OceanGridRules.RingGrowth);
            SetFloat(m, "_DisplaceFadeStart", OceanRules.DisplaceFadeStart);
            SetFloat(m, "_DisplaceFadeEnd", OceanRules.DisplaceFadeEnd);
            SetColor(m, "_HorizonColor", "#B0D4F1");
            SetFloat(m, "_HorizonFadeStart", OceanRules.HorizonFadeStart);
            SetFloat(m, "_HorizonFadeEnd", OceanRules.HorizonFadeEnd);
            SetFloat(m, "_DetailFarFadeStart", OceanRules.DetailFarFadeStart);
            SetFloat(m, "_DetailFarFadeEnd", OceanRules.DetailFarFadeEnd);

            // ---- 白帽（OceanRules 常量）----
            SetFloat(m, "_WhitecapStrength", 1f);
            SetFloat(m, "_WhitecapJacobianThreshold", OceanRules.WhitecapJacobianThreshold);
            SetFloat(m, "_WhitecapSoftness", OceanRules.WhitecapSoftness);
            SetFloat(m, "_WhitecapShoreScale", OceanRules.WhitecapShoreScale);
            SetFloat(m, "_WhitecapNoiseScale", 0.08f);

            // ---- 波背背光透射/薄层散射（技法吸收自 HPWater BSDF diffT；色 #8FD4EC 与浅水色同族）----
            SetColor(m, "_SssColor", "#8FD4EC");
            SetFloat(m, "_SssStrength", 0.5f);
            SetFloat(m, "_SssHeightRef", 0.9f);
            SetFloat(m, "_SssPathScale", 2.5f);
            SetFloat(m, "_SssExtinction", 1.2f);
            SetFloat(m, "_SssPhaseG", 0.6f);

            // ---- 折射 / 焦散 / 岸沫 / 高度场（沿旧件 r7 参数）----
            SetFloat(m, "_RefractionStrength", 0.035f);
            SetFloat(m, "_RefractionBlend", 0.22f);
            SetFloat(m, "_RefractionDepthFade", 1.6f);
            SetColor(m, "_CausticColor", "#CCFFEB");
            SetFloat(m, "_CausticStrength", 0.30f);
            SetFloat(m, "_CausticScale", 0.55f);
            SetFloat(m, "_CausticSpeed", 0.35f);
            SetFloat(m, "_CausticWarp", 0.60f);
            SetFloat(m, "_CausticDepthFade", 2.2f);
            SetColor(m, "_FoamColor", "#F0F7FF");
            SetFloat(m, "_FoamWidth", 1.6f);
            SetFloat(m, "_FoamNoiseScale", 5f);
            SetFloat(m, "_FoamNoiseScale2", 13f);
            SetFloat(m, "_FoamSpeed", 0.25f);
            SetFloat(m, "_FoamStrength", 0.85f);
            SetFloat(m, "_ShorelineFoamGain", 1.6f);
            SetFloat(m, "_FoamBreakup", 0.45f);
            SetFloat(m, "_FoamPulseSpeed", 0.55f);
            SetFloat(m, "_FoamPulseFrequency", 1.2f);
            SetFloat(m, "_FoamPulseStrength", 0.60f);
            SetFloat(m, "_HeightFieldNormalStrength", 0.35f);
            SetFloat(m, "_HeightFieldFoamStrength", 0.60f);
            SetFloat(m, "_ObstacleFoamBoost", 1.20f);

            // ---- 破坏噪声 / 菲涅尔 / 反射 / 太阳光路（沿旧件 r7 权重）----
            SetFloat(m, "_BreakupScale", 0.0222f);
            SetFloat(m, "_BreakupTintDepth", 0.04f);
            SetFloat(m, "_BreakupSpeed", 0.06f);
            SetFloat(m, "_FresnelPower", 5f);
            SetFloat(m, "_FresnelStrength", 1f);
            SetFloat(m, "_ReflectionStrength", 0.7f);
            SetFloat(m, "_Smoothness", 0.92f);
            SetFloat(m, "_SpecularIntensity", 0.5f);
            SetColor(m, "_SunSpecColor", "#FFDB73");
            SetFloat(m, "_SunSpecBroadStrength", 0.5f);  // M4 实拍修正（0.8 旧机位口径）
            SetFloat(m, "_SunSpecLaneShininess", 24f);
            SetFloat(m, "_SunSpecWaveStrength", 0.4f);
            SetFloat(m, "_SunSpecBroadShininess", 50f);
            SetFloat(m, "_SunSpecLaneWidth", 2.5f);
            SetFloat(m, "_SunSpecPatchScale", 8f);
            SetFloat(m, "_SunSpecPatchDepth", 0.40f);
            SetFloat(m, "_SunSpecCrestBias", 0.25f);  // M4 实拍修正（整海染金主因之一）
            SetFloat(m, "_SunSpecSlopeBoost", 12f);
            SetFloat(m, "_SunSpecStrength", 5f);
            SetFloat(m, "_SunSpecShininess", 320f);
            SetFloat(m, "_SunSpecGlitter", 0.55f);
            SetFloat(m, "_SunSheenStrength", 0.10f);

            // ---- 细节法线 / 不透明度 / 调试 ----
            SetFloat(m, "_WaveScaleA", 0.32f);
            SetFloat(m, "_WaveSpeedA", 0.45f);
            SetFloat(m, "_WaveStrengthA", 0.55f);
            SetVector(m, "_WaveDirectionA", new Vector4(1f, 0f, 0.35f, 0f));
            SetFloat(m, "_WaveScaleB", 0.95f);
            SetFloat(m, "_WaveSpeedB", 0.85f);
            SetFloat(m, "_WaveStrengthB", 0.28f);
            SetVector(m, "_WaveDirectionB", new Vector4(-0.4f, 0f, 1f, 0f));
            SetFloat(m, "_Opacity", 0.94f);   // M4 实拍修正：0.82 近场看穿海床
            SetFloat(m, "_DebugMode", 0f);

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            Debug.Log("[WaterAssetBuilder] M4 海洋材质参数已落盘：" + OceanMaterialPath
                      + "（§2.1 三档海水色 / swell×2+chop×4 / 包络与淡出取 OceanRules 常量）");
        }

        static void SetFloat(Material m, string name, float value)
        {
            if (m.HasProperty(name))
                m.SetFloat(name, value);
            else
                Debug.LogWarning("[WaterAssetBuilder] 材质缺少属性 " + name + "（shader 与材质不同步？）");
        }

        static void SetColor(Material m, string name, string hex)
        {
            if (!m.HasProperty(name))
            {
                Debug.LogWarning("[WaterAssetBuilder] 材质缺少属性 " + name + "（shader 与材质不同步？）");
                return;
            }
            m.SetColor(name, ParseHex(hex));
        }

        static void SetVector(Material m, string name, Vector4 value)
        {
            if (m.HasProperty(name))
                m.SetVector(name, value);
            else
                Debug.LogWarning("[WaterAssetBuilder] 材质缺少属性 " + name + "（shader 与材质不同步？）");
        }

        /// <summary>解析 "#RRGGBB"（sRGB，与 BattleSceneLighting.Hex 同口径）为 Color。</summary>
        static Color ParseHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color color))
                return color;
            Debug.LogWarning("[WaterAssetBuilder] 无法解析颜色 " + hex + "，退回白色。");
            return Color.white;
        }
    }
}
