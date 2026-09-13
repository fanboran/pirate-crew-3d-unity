using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗场景「光照 / 氛围 / 材质」总控（从 <see cref="M2BattleSceneSetup"/> 拆出的渲染基础层）。
    ///
    /// 【为什么独立成文件】M2BattleSceneSetup 是场景编排（相机/单位/规则/HUD 接线），
    ///   渲染氛围（材质库、后处理、雾、URP 设置）是另一条关注点；拆开便于渲染与玩法两条线并行推进。
    ///
    /// 【两个层次，别混用】
    ///   1. **资产层**（<see cref="BuildAll"/>，走 -executeMethod 或菜单）：
    ///      生成环境材质库 → 生成后处理 VolumeProfile → 配置 URP Asset（软阴影 / 深度图 / MSAA）。
    ///      这些都是"改工程资产"，与场景无关，可独立重复执行（幂等）。
    ///   2. **场景层**（<see cref="ConfigureSkyAndAmbient"/> / <see cref="CreateDirectionalLight"/> /
    ///      <see cref="ApplySceneAtmosphere"/>）：由 M2BattleSceneSetup 在重建 Battle 场景时调用，
    ///      写入场景的 RenderSettings / 全局 Volume / 相机后处理开关。
    ///
    /// 【风格契约】GDD §10.4「风格化写实（Stylized PBR）」：
    ///   暖主光 #FFF4E0 + 冷环境光（天空盒 SH，浅蓝）+ 实时软阴影；
    ///   后处理 = ACES + 轻微 Bloom + ColorGrading + Vignette。
    ///
    /// 【色空间】本工程 ProjectSettings m_ActiveColorSpace = 0（**Gamma**），
    ///   故 GDD 的 sRGB 十六进制直接归一化赋值即与色板一致（<see cref="Hex"/> 不做转换）。
    ///   若将来切到 Linear，必须把这里所有 Hex() 改成 Color.linear，否则整体偏亮。
    ///
    /// 【材质目录切分（与其它波次的约定）】
    ///   本文件只生成**环境类**材质，统一放 Assets/Art/Materials/Environment/。
    ///   角色类材质（皮肤/骨骼/皮革/阵营布料/胡须）由角色波次负责，放 Assets/Art/Materials/Crew/ —— 本文件不碰。
    ///
    /// 【无头入口】
    ///   "F:/Unity/.../Unity.exe" -batchmode -nographics -quit
    ///     -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew"
    ///     -executeMethod PirateCrew.EditorTools.BattleSceneLighting.BuildAll -logFile -
    /// </summary>
    public static class BattleSceneLighting
    {
        // ------------------------------------------------------------------
        // 路径
        // ------------------------------------------------------------------

        /// <summary>Art/Materials 根目录（BattleSky.mat 仍在此根，不动）。</summary>
        public const string ArtMaterialFolder = "Assets/Art/Materials";

        /// <summary>环境类材质目录（本文件专属；角色材质走 Crew/，不在此）。</summary>
        public const string EnvironmentMaterialFolder = ArtMaterialFolder + "/Environment";

        /// <summary>渲染资产目录（VolumeProfile 等）。</summary>
        public const string ArtRenderingFolder = "Assets/Art/Rendering";

        /// <summary>程序化天空盒材质（沿用 M2BattleSceneSetup 原先的路径，位置不变）。</summary>
        public const string SkyMaterialPath = ArtMaterialFolder + "/BattleSky.mat";

        /// <summary>全局后处理 VolumeProfile 资产路径。</summary>
        public const string VolumeProfilePath = ArtRenderingFolder + "/BattleGlobalVolumeProfile.asset";

        /// <summary>本工程当前生效的 URP Asset（GraphicsSettings 默认，见 ProjectSettings/GraphicsSettings.asset）。</summary>
        public const string UrpAssetPath = "Assets/Settings/URP/PC_Balanced_URPAsset.asset";

        // ------------------------------------------------------------------
        // Shader 名（与 Assets/Art/Shaders/ 下的三个程序化材质 shader 对应）
        // ------------------------------------------------------------------

        const string SurfaceShaderName = "PirateCrew/PirateSurface";
        const string WaterShaderName   = "PirateCrew/PirateWater";
        const string TerrainShaderName = "PirateCrew/PirateTerrain";

        // ------------------------------------------------------------------
        // 环境材质文件名（不含扩展名）；M2BattleSceneSetup 用这些常量取材质
        // ------------------------------------------------------------------

        public const string DrySandMaterial   = "Surface_DrySand";    // 干沙（也用作竞技场地面）
        public const string WetSandMaterial   = "Surface_WetSand";    // 湿沙（也用作岛外浅海床台阶）
        public const string GrassMaterial     = "Surface_Grass";      // 草地
        public const string RockMaterial      = "Surface_Rock";       // 岩石（也用作深海床台阶）
        public const string WoodPlankMaterial = "Surface_WoodPlank";  // 木板
        public const string WoodDarkMaterial  = "Surface_WoodDark";   // 深色木
        public const string BrassMaterial     = "Surface_Brass";      // 黄铜
        public const string IronMaterial      = "Surface_Iron";       // 铁
        public const string WaterMaterial     = "Water_Ocean";        // 海水
        public const string TerrainMaterial   = "Terrain_Island";     // 瓦片地形

        // ------------------------------------------------------------------
        // 资产层入口
        // ------------------------------------------------------------------

        /// <summary>
        /// 统一入口：生成环境材质库 → 生成后处理 VolumeProfile → 配置 URP Asset。幂等，可重复执行。
        /// </summary>
        [MenuItem("PirateCrew/Rendering/生成材质库与渲染资产")]
        public static void BuildAll()
        {
            EnsureFolder(ArtMaterialFolder);
            EnsureFolder(EnvironmentMaterialFolder);
            EnsureFolder(ArtRenderingFolder);

            int materialCount = BuildEnvironmentMaterials();
            VolumeProfile profile = EnsureVolumeProfile();
            ConfigureUrpAsset();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BattleSceneLighting] 渲染资产生成完成。\n"
                + "  环境材质: " + materialCount + " 个 → " + EnvironmentMaterialFolder + "\n"
                + "  后处理: " + (profile != null ? VolumeProfilePath : "（生成失败）") + "\n"
                + "  URP: " + UrpAssetPath + "（软阴影 on / 深度图 on / MSAA 2）");
        }

        /// <summary>读取环境材质；调用方需对 null 做兜底（生成失败时退回 URP/Lit）。</summary>
        public static Material LoadEnvironmentMaterial(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return null;

            return AssetDatabase.LoadAssetAtPath<Material>(
                EnvironmentMaterialFolder + "/" + materialName + ".mat");
        }

        // ------------------------------------------------------------------
        // 环境材质库（10 个，全部程序化、0 贴图）
        // ------------------------------------------------------------------

        /// <summary>生成/刷新全部环境材质，返回成功处理的数量。</summary>
        static int BuildEnvironmentMaterials()
        {
            int count = 0;

            if (BuildDrySandMaterial())   count++;
            if (BuildWetSandMaterial())   count++;
            if (BuildGrassMaterial())     count++;
            if (BuildRockMaterial())      count++;
            if (BuildWoodPlankMaterial()) count++;
            if (BuildWoodDarkMaterial())  count++;
            if (BuildBrassMaterial())     count++;
            if (BuildIronMaterial())      count++;
            if (BuildWaterMaterial())     count++;
            if (BuildTerrainMaterial())   count++;

            return count;
        }

        // 干沙（GDD §10.4 沙地三档：暗 #8B7355 → 中 #C4A76A → 亮 #E8D5A3）
        // 参数理由：沙是细腻介质 → 噪声尺度偏大（4.5，即约 0.22 世界单位的斑块）、细节法线中等；
        //           完全无金属、光滑度低（0.12）—— 干沙几乎无高光，匹配 cel-shader-guide §8 的 "specular=0"。
        static bool BuildDrySandMaterial()
        {
            Material m = EnsureMaterial(DrySandMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#8B7355"));
            SetColor(m, "_BaseColorB", Hex("#C4A76A"));
            SetColor(m, "_BaseColorC", Hex("#E8D5A3"));
            SetFloat(m, "_ColorRampContrast", 1.5f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 4.5f);
            SetFloat(m, "_NoiseStrength", 0.4f);
            SetVector(m, "_NoiseStretch", new Vector4(1f, 1f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 18f);
            SetFloat(m, "_DetailNormalStrength", 0.45f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.12f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0f);
            // 干沙/湿沙共用同一组湿参数（否则两种材质交界处湿带断裂露缝，见 ApplySandWetParameters）。
            ApplySandWetParameters(m);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 湿沙（提案/待定：GDD 只有一组沙色，没有"湿沙"色值。
        //   本组由沙地暗档继续压暗 + 提饱和得到，湿润参数让水面附近/浪线内侧的沙更暗更亮）。
        static bool BuildWetSandMaterial()
        {
            Material m = EnsureMaterial(WetSandMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#5C4A34"));
            SetColor(m, "_BaseColorB", Hex("#8B7355"));
            SetColor(m, "_BaseColorC", Hex("#A8906A"));
            SetFloat(m, "_ColorRampContrast", 1.4f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 4.0f);
            SetFloat(m, "_NoiseStrength", 0.35f);
            SetVector(m, "_NoiseStretch", new Vector4(1f, 1f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 16f);
            SetFloat(m, "_DetailNormalStrength", 0.3f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.22f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0f);
            // 与干沙同一组湿参数：湿感全部由世界高度（水位 ± _WetBandWidth）驱动，
            // 不再用材质级 _Wetness 常量（旧值 0.75 会让整块湿沙无梯度，且与干沙交界露缝）。
            ApplySandWetParameters(m);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        /// <summary>
        /// 两个沙材质共用的"沙滩湿区"参数（干沙 <see cref="DrySandMaterial"/> 与
        /// 湿沙 <see cref="WetSandMaterial"/> 必须逐值相同，否则材质交界处湿带断裂露缝）。
        ///
        /// 【为什么不合并两个材质】<c>SceneArtBuilder</c> 等处按名字查找 <c>Surface_WetSand</c>，
        /// 删除会触发回退材质与告警；合并列为后续优化（本次只统一参数）。
        ///
        /// 【取值依据】湿带宽度覆盖潮间带坡 0→-0.6（场景设计 §3.3），水位 -0.2 与
        /// PirateWater / LevelGeometry 一致；湿沙色 #A88D5C 是**AI 提案**（介于沙地暗/中档之间）。
        /// <c>_Wetness</c> 固定 0：湿感只由世界高度驱动，设 1 会退化成"整块湿"、失去梯度。
        /// 幂等：每次 BuildAll 就地覆写这些属性，不改其它材质参数。
        /// </summary>
        static void ApplySandWetParameters(Material m)
        {
            SetFloat(m, "_WaterLevelY", -0.2f);          // 与 PirateWater / LevelGeometry.WaterSurfaceY 同源
            SetFloat(m, "_WetBandWidth", 0.45f);         // 水线以上过渡到全干的世界单位宽
            SetFloat(m, "_Wetness", 0f);                 // 关键：不用材质级常量，靠高度掩码出梯度
            SetColor(m, "_WetSandColor", Hex("#A88D5C"));// 湿态 albedo 目标色【AI 提案】
            SetFloat(m, "_WetLineMin", 0.10f);           // 高水位残留线（潮痕）下边界
            SetFloat(m, "_WetLineMax", 0.22f);           // 上边界
            SetFloat(m, "_WetDarken", 0.3f);             // 湿区额外压暗
            SetFloat(m, "_WetSmoothnessBoost", 0.45f);   // 湿沙更滑 → 高光是"湿"的关键信号
            SetFloat(m, "_RippleScale", 16f);            // 沙纹频率（λ ≈ 0.39 世界单位）
            SetFloat(m, "_RippleStrength", 0.10f);       // 沙纹振幅（shader 硬钳 ≤0.15）
            SetFloat(m, "_RippleDistort", 0.35f);        // 沙纹相位扭曲
        }

        // 草地（GDD §10.4 草地三档：#2D5A2D → #4A8C4A → #7BC67E）
        // 参数理由：草是高频细密介质 → 噪声尺度最大（8）、细节法线最强（0.75），制造"绒毛感"；
        //           光滑度 0.1（cel-shader-guide 草地预设 specular=0）。
        static bool BuildGrassMaterial()
        {
            Material m = EnsureMaterial(GrassMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#2D5A2D"));
            SetColor(m, "_BaseColorB", Hex("#4A8C4A"));
            SetColor(m, "_BaseColorC", Hex("#7BC67E"));
            SetFloat(m, "_ColorRampContrast", 1.7f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 8f);
            SetFloat(m, "_NoiseStrength", 0.5f);
            SetVector(m, "_NoiseStretch", new Vector4(1f, 1f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 26f);
            SetFloat(m, "_DetailNormalStrength", 0.75f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.1f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0f);
            SetFloat(m, "_Wetness", 0f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 岩石（GDD §10.4 岩石三档：#5C4F42 → #8C7B6A → #B8A99A）
        // 参数理由：岩石是块状硬表面 → 噪声尺度中等（5）、细节法线强（0.9）制造碎裂感；
        //           光滑度 0.28（cel-shader-guide 岩石预设给了弱 specular 0.05）；
        //           边缘磨损 0.3 + 磨损色偏白 —— 岩棱被风化磨亮是风格化写实的常见做法（AI 提案）。
        static bool BuildRockMaterial()
        {
            Material m = EnsureMaterial(RockMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#5C4F42"));
            SetColor(m, "_BaseColorB", Hex("#8C7B6A"));
            SetColor(m, "_BaseColorC", Hex("#B8A99A"));
            SetFloat(m, "_ColorRampContrast", 1.6f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 5f);
            SetFloat(m, "_NoiseStrength", 0.45f);
            SetVector(m, "_NoiseStretch", new Vector4(1f, 1f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 12f);
            SetFloat(m, "_DetailNormalStrength", 0.9f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.28f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0.3f);
            SetColor(m, "_WearColor", Hex("#C8BCA8"));
            SetFloat(m, "_Wetness", 0f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 木板（GDD §10.4 木材三档：#6B4C28 → #A67B42 → #D4A76A）
        // 参数理由：木纹是**各向异性**条纹 → _NoiseStretch=(0.18, 1.0) 把噪声沿 Z 拉成长条；
        //           光滑度 0.18（cel-shader-guide 木材预设 specular=0，但木板有微弱油光）。
        static bool BuildWoodPlankMaterial()
        {
            Material m = EnsureMaterial(WoodPlankMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#6B4C28"));
            SetColor(m, "_BaseColorB", Hex("#A67B42"));
            SetColor(m, "_BaseColorC", Hex("#D4A76A"));
            SetFloat(m, "_ColorRampContrast", 1.9f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 3.0f);
            SetFloat(m, "_NoiseStrength", 0.45f);
            SetVector(m, "_NoiseStretch", new Vector4(0.18f, 1.0f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 9f);
            SetFloat(m, "_DetailNormalStrength", 0.35f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.18f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0.2f);
            SetColor(m, "_WearColor", Hex("#D8BC8C"));
            SetFloat(m, "_Wetness", 0f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 深色木（提案/待定：GDD 木材三档之外的自定义档，用作船舷/栅栏/箱体的压暗木色）
        static bool BuildWoodDarkMaterial()
        {
            Material m = EnsureMaterial(WoodDarkMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#3A2A18"));
            SetColor(m, "_BaseColorB", Hex("#55391F"));
            SetColor(m, "_BaseColorC", Hex("#6B4C28"));
            SetFloat(m, "_ColorRampContrast", 1.7f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 3.0f);
            SetFloat(m, "_NoiseStrength", 0.4f);
            SetVector(m, "_NoiseStretch", new Vector4(0.18f, 1.0f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 9f);
            SetFloat(m, "_DetailNormalStrength", 0.35f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.22f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0.25f);
            SetColor(m, "_WearColor", Hex("#8A6A3C"));
            SetFloat(m, "_Wetness", 0f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 黄铜（提案/待定：GDD 没有金属色值。取 #B08D3E 一带的黄铜色阶：
        //   暗 #7A5C1E → 中 #B08D3E → 亮 #D9B65A；金属度 1、光滑度 0.75）。
        // 参数理由：金属的 albedo 三档 = 反射率差异，噪声给"铸造/氧化的不均匀"；
        //           光滑度 0.75 让 SH 环境反射与主光高光都能看出金属感。
        static bool BuildBrassMaterial()
        {
            Material m = EnsureMaterial(BrassMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#7A5C1E"));
            SetColor(m, "_BaseColorB", Hex("#B08D3E"));
            SetColor(m, "_BaseColorC", Hex("#D9B65A"));
            SetFloat(m, "_ColorRampContrast", 1.5f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 6f);
            SetFloat(m, "_NoiseStrength", 0.35f);
            SetVector(m, "_NoiseStretch", new Vector4(1f, 1f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 20f);
            SetFloat(m, "_DetailNormalStrength", 0.25f);
            SetFloat(m, "_Metallic", 1f);
            SetFloat(m, "_Smoothness", 0.75f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0.35f);
            SetColor(m, "_WearColor", Hex("#E8D5A3"));
            SetFloat(m, "_Wetness", 0f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 铁（提案/待定：GDD 没有金属色值。冷灰金属：暗 #4A4E52 → 中 #7C8288 → 亮 #A8AFB6；
        //   金属度 1、光滑度 0.45）。光滑度低于黄铜 → 铁是哑光/粗糙金属。
        static bool BuildIronMaterial()
        {
            Material m = EnsureMaterial(IronMaterial, SurfaceShaderName);
            if (m == null) return false;

            SetColor(m, "_BaseColorA", Hex("#4A4E52"));
            SetColor(m, "_BaseColorB", Hex("#7C8288"));
            SetColor(m, "_BaseColorC", Hex("#A8AFB6"));
            SetFloat(m, "_ColorRampContrast", 1.5f);
            SetFloat(m, "_ColorRampBias", 0.5f);
            SetFloat(m, "_NoiseScale", 7f);
            SetFloat(m, "_NoiseStrength", 0.4f);
            SetVector(m, "_NoiseStretch", new Vector4(1f, 1f, 0f, 0f));
            SetFloat(m, "_DetailNoiseScale", 22f);
            SetFloat(m, "_DetailNormalStrength", 0.5f);
            SetFloat(m, "_Metallic", 1f);
            SetFloat(m, "_Smoothness", 0.45f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0.4f);
            SetColor(m, "_WearColor", Hex("#C9C2B4"));
            SetFloat(m, "_Wetness", 0f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 海水（GDD §10.4 海水三档：#4DA6D9 → #2B7AB8 → #1A4F7A）
        // 参数理由：
        //   _ShoreFadeDistance=4：岛外浅台(y=-0.6)/中台(y=-1.6) 与水面(y=-0.2) 的视深度差
        //     约 0.6 / 2.0 → 4 的分母让浅台落在"浅→中"、中台落在"中→深"（对应三档色）。
        //   _FoamWidth=1.6：略大于浅台深度差，让泡沫从岛缘向外覆盖一小段而不是一条死线。
        //   _ShorelineFoamGain=1.6：屏幕空间深度梯度在岛缘会突变 ~10+ 单位，乘以 1.6 直接饱和 → 贴轮廓白线。
        //   _FresnelPower=5 / _FresnelStrength=1 / _ReflectionStrength=0.55：
        //     cel-shader-guide §8 海水预设要求"强高光 + 强边缘光"，SH 反射强度给 0.55 避免远处水面发白糊掉。
        //   _Smoothness=0.92：水面镜面高光很紧（太阳直射会形成一条亮带）。
        static bool BuildWaterMaterial()
        {
            Material m = EnsureMaterial(WaterMaterial, WaterShaderName);
            if (m == null) return false;

            SetColor(m, "_ShallowColor", Hex("#4DA6D9"));
            SetColor(m, "_MidColor", Hex("#2B7AB8"));
            SetColor(m, "_DeepColor", Hex("#1A4F7A"));
            SetFloat(m, "_ShoreFadeDistance", 4f);

            SetColor(m, "_FoamColor", Hex("#F0F7FF"));
            SetFloat(m, "_FoamWidth", 1.6f);
            SetFloat(m, "_FoamNoiseScale", 5f);
            SetFloat(m, "_FoamSpeed", 0.25f);
            SetFloat(m, "_FoamStrength", 0.85f);
            SetFloat(m, "_ShorelineFoamGain", 1.6f);

            // A 层：大尺度慢波（约 3 世界单位一个起伏）→ 主形体；
            // B 层：小尺度快波（约 1 世界单位）→ 细碎闪烁。
            SetFloat(m, "_WaveScaleA", 0.32f);
            SetFloat(m, "_WaveSpeedA", 0.45f);
            SetFloat(m, "_WaveStrengthA", 0.55f);
            SetVector(m, "_WaveDirectionA", new Vector4(1f, 0f, 0.35f, 0f));
            SetFloat(m, "_WaveScaleB", 0.95f);
            SetFloat(m, "_WaveSpeedB", 0.85f);
            SetFloat(m, "_WaveStrengthB", 0.28f);
            SetVector(m, "_WaveDirectionB", new Vector4(-0.4f, 0f, 1f, 0f));

            SetFloat(m, "_FresnelPower", 5f);
            SetFloat(m, "_FresnelStrength", 1f);
            SetFloat(m, "_ReflectionStrength", 0.55f);
            SetFloat(m, "_Smoothness", 0.92f);
            SetFloat(m, "_SpecularIntensity", 2.2f);

            SetFloat(m, "_Opacity", 0.82f);
            // 水体升级后的新参数（Gerstner 四波/折射/焦散/高度场）不在此显式设置——
            // shader Properties 的默认值即推荐值（见 PirateWater.shader 与 WaterRules.DefaultWaves，
            // 两处必须同步）。旧 _VertexWave* 三参数已随 shader 移除，不再写入。
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 瓦片地形（GDD §10.4 各中档色：沙 #C4A76A / 草 #4A8C4A / 岩 #8C7B6A）
        // 参数理由：
        //   _HeightSandGrass=0.6 / _HeightGrassRock=3.0：本工程地块是低平台（1 瓦片 = 1 世界单位），
        //     0.6 让"抬升半格以上"就转草、3.0 让"高于三层"转岩（提案：按 level_1/level_27 的
        //     实际地形高度分布调，观感验收时可改）。
        //   _SlopeRockStart=0.45（约 27° 起）：竖直面必为岩，缓坡仍为沙/草。
        //   _BlockSize=1（1 瓦片）+ _BlockTintStrength=0.12：逐块明暗差是低多边形块面感的辨识特征。
        //   _FacetStrength=0.6：保留几何棱面的同时不至于让光影完全"数字化"。
        //   _EdgeColor=#2A2A2A + 强度 0.35：GDD 规定的场景物描边色，菲涅尔边缘压暗替代 inverted-hull。
        static bool BuildTerrainMaterial()
        {
            Material m = EnsureMaterial(TerrainMaterial, TerrainShaderName);
            if (m == null) return false;

            SetColor(m, "_SandColor", Hex("#C4A76A"));
            SetColor(m, "_GrassColor", Hex("#4A8C4A"));
            SetColor(m, "_RockColor", Hex("#8C7B6A"));
            SetFloat(m, "_HeightSandGrass", 0.6f);
            SetFloat(m, "_HeightGrassRock", 3f);
            SetFloat(m, "_SlopeRockStart", 0.45f);
            SetFloat(m, "_SlopeRockEnd", 0.72f);
            SetFloat(m, "_BlendSoftness", 0.5f);
            SetFloat(m, "_NoiseScale", 2f);
            SetFloat(m, "_NoiseStrength", 0.35f);
            SetFloat(m, "_BlockSize", 1f);
            SetFloat(m, "_BlockTintStrength", 0.12f);
            SetFloat(m, "_FacetStrength", 0.6f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.15f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetColor(m, "_EdgeColor", Hex("#2A2A2A"));
            SetFloat(m, "_EdgeStrength", 0.35f);
            SetFloat(m, "_EdgePower", 3f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // ------------------------------------------------------------------
        // 后处理 VolumeProfile
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成/刷新全局后处理 VolumeProfile。幂等：已存在的组件就地更新覆盖值。
        /// </summary>
        static VolumeProfile EnsureVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "BattleGlobalVolumeProfile";
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            // ---- Tonemapping：ACES ----
            // 理由：URP Asset 已开 HDR（m_SupportsHDR=1），ACES 能把高光滚降得自然、
            // 避免明亮天空/水面直接过曝成死白；风格化写实要求"有电影感的亮部"。
            Tonemapping tonemapping = EnsureVolumeComponent<Tonemapping>(profile);
            Override(tonemapping.mode, true);
            tonemapping.mode.value = TonemappingMode.ACES;

            // ---- Bloom：轻微 ----
            // 理由（数值取舍）：
            //   threshold=1.05（略高于 1）→ 只有 HDR 亮部（太阳附近的天空、水面镜面高光）发光，
            //     地面的沙/草（LDR 范围）不受影响 → 不会整屏发糊。
            //   intensity=0.32 / scatter=0.62 → "轻微 Bloom"，符合 GDD §10.4 光照设置里
            //     "禁用 Glow"的意图（那一版是纯 cel）；这里是风格化写实，取"只让高光有呼吸感"的程度。
            //   tint=#FFF4E0 = GDD 主光暖色 → 高光晕染偏暖，与环境冷色形成冷暖对比。
            //   highQualityFiltering=false → 性能取舍：Bloom 是 soft-knee 多 mip 模糊，
            //     高质量滤波在该分辨率下观感差异小、开销明显（报告里说明）。
            Bloom bloom = EnsureVolumeComponent<Bloom>(profile);
            Override(bloom.threshold, true);
            bloom.threshold.value = 1.05f;
            Override(bloom.intensity, true);
            bloom.intensity.value = 0.32f;
            Override(bloom.scatter, true);
            bloom.scatter.value = 0.62f;
            Override(bloom.tint, true);
            bloom.tint.value = Hex("#FFF4E0");
            Override(bloom.highQualityFiltering, true);
            bloom.highQualityFiltering.value = false;

            // ---- ColorGrading：按调色板调 lift/gain（冷暖分离）----
            // 说明：本工程没有 LUT 贴图（0 贴图约束），故走程序化的 ColorGrading 组件组合，
            //   等价于"用参数搭一张 LUT"。
            // ColorAdjustments：
            //   saturation=+10 —— 风格契约要求"高饱和块面"；ACES 会轻微降饱和，这里补回。
            //   contrast=+12   —— 强化块面明暗分界（低多边形风格的关键）。
            //   postExposure=0 —— 曝光交给主光强度控制，后处理不叠加（便于单独调光）。
            ColorAdjustments colorAdjustments = EnsureVolumeComponent<ColorAdjustments>(profile);
            Override(colorAdjustments.postExposure, true);
            colorAdjustments.postExposure.value = 0f;
            Override(colorAdjustments.contrast, true);
            colorAdjustments.contrast.value = 12f;
            Override(colorAdjustments.saturation, true);
            colorAdjustments.saturation.value = 10f;
            Override(colorAdjustments.colorFilter, true);
            colorAdjustments.colorFilter.value = Color.white;

            // WhiteBalance：temperature=+8 → 整体偏暖，对齐 GDD 暖主光 #FFF4E0。
            WhiteBalance whiteBalance = EnsureVolumeComponent<WhiteBalance>(profile);
            Override(whiteBalance.temperature, true);
            whiteBalance.temperature.value = 8f;
            Override(whiteBalance.tint, true);
            whiteBalance.tint.value = 0f;

            // ShadowsMidtonesHighlights（这就是 lift/gain 的载体）：
            //   阴影 (0.98, 1.00, 1.04) → 略偏蓝，对应 GDD 环境光浅蓝 #C8DDF0；
            //   高光 (1.02, 1.00, 0.97) → 略偏暖，对应主光 #FFF4E0；
            //   中间调保持中性。冷暖分离 = 风格化写实最基本的"光影氛围"手段。
            ShadowsMidtonesHighlights smh = EnsureVolumeComponent<ShadowsMidtonesHighlights>(profile);
            Override(smh.shadows, true);
            smh.shadows.value = new Vector4(0.98f, 1.00f, 1.04f, 0f);
            Override(smh.midtones, true);
            smh.midtones.value = new Vector4(1f, 1f, 1f, 0f);
            Override(smh.highlights, true);
            smh.highlights.value = new Vector4(1.02f, 1.00f, 0.97f, 0f);

            // ---- Vignette：轻微 ----
            // 理由：把视线收拢到竞技场中心（相机固定 45° 俯视、竞技场是画面主体）。
            //   intensity=0.22 是"能感觉到、但不会挡到边角单位"的程度；
            //   smoothness=0.45 让暗角过渡宽缓（否则会像贴了一圈黑边）；
            //   color=#101820 深蓝黑（不用纯黑，与冷环境色调一致）。
            Vignette vignette = EnsureVolumeComponent<Vignette>(profile);
            Override(vignette.color, true);
            vignette.color.value = Hex("#101820");
            Override(vignette.center, true);
            vignette.center.value = new Vector2(0.5f, 0.5f);
            Override(vignette.intensity, true);
            vignette.intensity.value = 0.22f;
            Override(vignette.smoothness, true);
            vignette.smoothness.value = 0.45f;
            Override(vignette.rounded, true);
            vignette.rounded.value = false;

            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ------------------------------------------------------------------
        // URP Asset 配置
        // ------------------------------------------------------------------

        /// <summary>
        /// 配置 URP Asset：打开软阴影、打开深度图、MSAA 4→2。
        /// 取舍见报告：软阴影是本波次"光影氛围"的必要项；深度图是 PirateWater 泡沫/浅深水的硬依赖；
        /// MSAA 降档用来抵消前两项带来的带宽/开销增长（本场景以大面积色块 + 描边为主，2x 足够）。
        /// </summary>
        public static void ConfigureUrpAsset()
        {
            UniversalRenderPipelineAsset asset =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null)
            {
                Debug.LogError("[BattleSceneLighting] 找不到 URP Asset: " + UrpAssetPath
                    + "，软阴影/深度图/MSAA 未配置。请核对 GraphicsSettings 引用的 URP 资产路径。");
                return;
            }

            // 公共 setter：水面 shader 的 SampleSceneDepth 依赖它。
            asset.supportsCameraDepthTexture = true;

            // 公共 setter：MSAA 4 → 2（见上取舍）。
            asset.msaaSampleCount = 2;

            // supportsSoftShadows 的 setter 在 URP 14 是 internal（有意不开放），
            // 故用 SerializedObject 直接写序列化字段 m_SoftShadowsSupported（资产里确实存在该字段）。
            var so = new SerializedObject(asset);
            SetBoolField(so, "m_SoftShadowsSupported", true);
            SetIntField(so, "m_SoftShadowQuality", 2); // 2 = Medium（URP 默认软阴影质量档）
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
        }

        // ------------------------------------------------------------------
        // 场景层：天空 / 环境光 / 主光 / 雾 / Volume / 相机后处理
        // ------------------------------------------------------------------

        /// <summary>
        /// 天空盒 + 环境光（对齐 Godot 基准 WorldEnvironment：procedural sky + Sky 环境光）。
        /// 返回 false 表示找不到天空盒 shader，此时退回纯色背景（不影响可玩性）。
        /// </summary>
        public static bool ConfigureSkyAndAmbient()
        {
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);

            if (sky == null)
            {
                Shader skyShader = Shader.Find("Skybox/Procedural");
                if (skyShader == null)
                {
                    Debug.LogWarning("[BattleSceneLighting] 找不到 Skybox/Procedural，退回纯色背景。");
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    // 浅蓝环境光（GDD §10.4：#C8DDF0）。
                    RenderSettings.ambientLight = Hex("#C8DDF0");
                    return false;
                }

                sky = new Material(skyShader) { name = "BattleSky" };
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }

            // 就地更新（而不是"资产已存在就跳过"）：改了这里的观感常量必须能生效，
            // 否则会出现"代码改了、画面没变"的排查陷阱。
            if (sky.HasProperty("_SunSize"))
                sky.SetFloat("_SunSize", 0.04f);
            if (sky.HasProperty("_AtmosphereThickness"))
                sky.SetFloat("_AtmosphereThickness", 0.85f);
            if (sky.HasProperty("_SkyTint"))
                sky.SetColor("_SkyTint", Hex("#87CFEB"));   // 天空主调，偏青蓝
            if (sky.HasProperty("_GroundColor"))
                // 地平以下（地面方向）的辐照度：竞技场是沙岛，故给沙色调而不是原先的土褐色 ——
                // 让 SH 环境光从下方反射的是沙色，与前景材质一致（AI 提案）。
                sky.SetColor("_GroundColor", Hex("#7A6A4C"));
            if (sky.HasProperty("_Exposure"))
                sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            return true;
        }

        /// <summary>
        /// 主方向光。数值出处：GDD §10.4「主方向光：暖色 #FFF4E0，Shadow 开启」；
        /// 强度 1.35 / 姿态 <c>Euler(48,140,0)</c> 依据 docs/场景设计-战斗竞技场.md §6.5
        /// 与 docs/美术风格指南.md §4.1 Q-7 裁决（强度 1.2–1.4，实现取 1.35）。
        /// </summary>
        public static Light CreateDirectionalLight()
        {
            var go = new GameObject("Directional Light", typeof(Light));
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = Hex("#FFF4E0");
            // 旧值 1.1 / Euler(50,-30,0) 已作废：方位 -30° 让朝镜头的面全部背光，是画面发平主因之一。
            light.intensity = 1.35f;
            // 投影是"看起来像 3D"的主要深度线索之一（对齐 Godot 基准的 shadow_enabled）。
            // LightShadows.Soft 需要 URP Asset 打开 m_SoftShadowsSupported（ConfigureUrpAsset 已打开）。
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;
            // 48° 仰角 + 140° 方位：入射方向约 (0.43,-0.74,-0.51)，光从相机侧后方来，
            // 朝镜头的面受光、阴影朝屏幕右下延伸。**不得回退到 -30°**（见上文裁决出处）。
            go.transform.rotation = Quaternion.Euler(48f, 140f, 0f);
            return light;
        }

        /// <summary>
        /// 场景氛围：线性雾 + 全局 Volume + 主相机后处理开关。
        /// 由 M2BattleSceneSetup 在相机创建后调用（RenderSettings 与 Volume 都随场景保存）。
        /// </summary>
        public static void ApplySceneAtmosphere(Camera camera)
        {
            // ---- 雾（空气透视）----
            // 颜色取天空地平色一带 #B0D4F1（提案：与 Procedural Sky 的地平色近似即可；
            //   若之后调了天空盒参数，需回来对齐，否则远山/远海会与天空"接缝"）。
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#B0D4F1");
            // 距离依据（level_1：竞技场 50×17，相机距中心 18 单位）：
            //   start=25 —— 相机到竞技场远缘约 30~35 单位，取 25 让**前景竞技场基本不被雾洗白**，
            //              只在远端（远景地面/海面）开始出现雾；
            //   end=140   —— 岛外海面最远约 50~80 单位 → 得到 18%~40% 的雾量，形成空气透视但不淹没画面；
            //              远到 140 之外（接近相机 farClip=200）基本完全融入地平色。
            // 每关竞技场尺寸不同（LevelData.WidthTiles/HeightTiles），若关卡明显更大，这两个值需按比例调。
            RenderSettings.fogStartDistance = 25f;
            RenderSettings.fogEndDistance = 140f;

            // ---- 全局 Volume ----
            var volumeGo = new GameObject("GlobalVolume");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (volume.sharedProfile == null)
            {
                Debug.LogWarning("[BattleSceneLighting] 未找到 VolumeProfile: " + VolumeProfilePath
                    + "，后处理不会生效。请先跑 BattleSceneLighting.BuildAll 生成资产。");
            }

            // Volume 物体留在 Default 层：相机 volumeLayerMask 默认 = 1（Default），能取到。
            // 不要把它挪到 UI/其它层，否则相机扫不到、后处理静默失效。

            // ---- 主相机后处理 ----
            if (camera == null)
                return;

            UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null)
                data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

            // 打开后处理（否则 Volume 里的 ACES/Bloom/ColorGrading/Vignette 全部不执行）。
            data.renderPostProcessing = true;
            data.renderShadows = true;
            // MSAA 已在 URP Asset 打开，这里不再叠 SMAA/FXAA —— 重复抗锯齿只增加成本，不改善描边质量。
            data.antialiasing = AntialiasingMode.None;
            // ACES + 大面积渐变天空容易出色带（banding），抖动可显著压住，成本极低。
            data.dithering = true;
            EditorUtility.SetDirty(data);
        }

        // ------------------------------------------------------------------
        // 辅助：Volume 组件
        // ------------------------------------------------------------------

        /// <summary>
        /// 取/建 VolumeProfile 上的组件。新建时必须 <see cref="AssetDatabase.AddObjectToAsset"/>，
        /// 否则组件只是内存对象，资产重载后会丢失（profile.components 里的引用变 null）。
        /// 这一模式照抄 core 包 VolumeProfileFactory.CreateVolumeComponent 的官方实现。
        /// </summary>
        static T EnsureVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out T existing) && existing != null)
                return existing;

            T component = profile.Add<T>(true); // overrides=true：所有参数默认开启覆盖
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>显式设置 overrideState，避免资产里残留 false 导致"参数改了但没生效"。</summary>
        static void Override(VolumeParameter parameter, bool overrideState)
        {
            if (parameter != null)
                parameter.overrideState = overrideState;
        }

        // ------------------------------------------------------------------
        // 辅助：材质 / 字段 / 路径
        // ------------------------------------------------------------------

        /// <summary>取/建环境材质资产，并保证 shader 正确（幂等：已存在的材质会就地换 shader）。</summary>
        static Material EnsureMaterial(string fileName, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[BattleSceneLighting] 找不到 shader " + shaderName
                    + "（材质 " + fileName + " 未生成）。请先用 read_console 确认该 shader 无编译错误。");
                return null;
            }

            string path = EnvironmentMaterialFolder + "/" + fileName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = fileName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        static bool Save(Material material)
        {
            if (material == null)
                return false;

            EditorUtility.SetDirty(material);
            return true;
        }

        static void SetColor(Material m, string property, Color value)
        {
            if (m.HasProperty(property))
                m.SetColor(property, value);
        }

        static void SetFloat(Material m, string property, float value)
        {
            if (m.HasProperty(property))
                m.SetFloat(property, value);
        }

        static void SetVector(Material m, string property, Vector4 value)
        {
            if (m.HasProperty(property))
                m.SetVector(property, value);
        }

        static void SetBoolField(SerializedObject so, string fieldName, bool value)
        {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning("[BattleSceneLighting] URP Asset 上找不到序列化字段 " + fieldName
                    + "（URP 版本可能不同），该项未配置。");
                return;
            }
            property.boolValue = value;
        }

        static void SetIntField(SerializedObject so, string fieldName, int value)
        {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning("[BattleSceneLighting] URP Asset 上找不到序列化字段 " + fieldName
                    + "（URP 版本可能不同），该项未配置。");
                return;
            }
            property.intValue = value;
        }

        /// <summary>
        /// GDD §10.4 调色板的 sRGB 十六进制 → Unity Color。
        /// 本工程是 **Gamma** 色空间（ProjectSettings m_ActiveColorSpace=0），故**不做** sRGB→Linear 转换，
        /// 直接归一化的值在屏幕上即等于色板颜色。切 Linear 时这里必须改为 <c>.linear</c>。
        /// </summary>
        static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color color))
                return color;

            Debug.LogWarning("[BattleSceneLighting] 无法解析颜色 " + hex + "，退回品红以便肉眼发现问题。");
            return Color.magenta;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
