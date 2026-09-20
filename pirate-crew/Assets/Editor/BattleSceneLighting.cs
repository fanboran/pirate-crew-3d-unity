using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PirateCrew.Ambient;

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
    /// 【色空间】本工程 ProjectSettings m_ActiveColorSpace = 1（**Linear**）——PBR 物理正确的前提。
    ///   **关键结论（别再"顺手"把所有 Hex() 改成 .linear，那反而会整体变暗）**：
    ///   Unity 引擎对**普通材质 Color 属性**会自动做 sRGB→Linear 转换，脚本直接写 sRGB 原值即可。
    ///     证据：URP 的 <c>Editor/AssetPostProcessors/MaterialPostprocessor.cs:271-277</c> 注释明确
    ///     "普通 Color 属性会被做 gamma→linear 转换，而 [HDR] 属性不会"（这正是它要给旧工程补 .linear 的原因）；
    ///     <c>Light.color</c> 由引擎的 <c>VisibleLight.finalColor</c> 输出（URP 注释"already returns color in active color space"）；
    ///     <c>RenderSettings.ambientSky/Equator/GroundColor</c> 由 URP 用
    ///     <c>CoreUtils.ConvertSRGBToActiveColorSpace</c> 包好再上传（<c>UniversalRenderPipeline.cs:1695-1697</c>）。
    ///   只有走**原始 Vector 通道**（CommandBuffer.SetVector / SetGlobalColor）的颜色才需要手动 .linear：
    ///     本文件里只有 **Vignette.color**（见 <see cref="EnsureVolumeProfile"/>，URP 的 SetupVignette 不做转换）。
    ///   故 <see cref="Hex"/> 保持"解析 sRGB 十六进制、不做转换"的语义，**所有调用点都不改**。
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

            // ⓪ 程序化细节贴图**必须先生成**：下面 10 个材质要引用它们（幂等：已生成则按字节比对跳过）。
            // 【为什么在这里也调一次】本方法自身是无头入口（-executeMethod BattleSceneLighting.BuildAll），
            //   不能假设调用方（ArtGate）已经跑过这一步——否则直接调本方法会得到"没有细节贴图的材质"。
            // 【为什么吞异常】细节贴图缺失只让沙/草/岩退回"纯色 + 噪声"（细节强度置 0），
            //   材质仍然可用；失败由 ArtGate 的独立步骤（⓪.5）负责汇总成非零退出码，不在这里静默。
            try
            {
                MaterialNoiseBuilder.Build(false);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[BattleSceneLighting] 程序化细节贴图生成失败：沙/草/岩材质将不带细节贴图"
                    + "（对应强度被置 0，画面仍可运行，但 P-9/P-10 会不达标）。原因：\n" + e);
            }

            int materialCount = BuildEnvironmentMaterials();
            VolumeProfile profile = EnsureVolumeProfile();
            ConfigureUrpAsset();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BattleSceneLighting] 渲染资产生成完成。\n"
                + "  环境材质: " + materialCount + " 个 → " + EnvironmentMaterialFolder + "\n"
                + "  细节贴图: " + MaterialNoiseBuilder.TextureFolder + "（由 MaterialNoiseBuilder 生成）\n"
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
        // 环境材质库（10 个；基色为色值、细节为程序化噪声贴图 —— 0 外部贴图）
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
        //           完全无金属、光滑度低（0.25）—— 干沙几乎无高光，匹配 cel-shader-guide §8 的 "specular=0"。
        // 【本次改动（r3 复验 N4 返工）】_Smoothness 0.12 → **0.25**：沙/草/岩三族的粗糙度必须拉开
        //   （美术风格指南.md:199-204 §3.2 纪律 1：相邻面 smoothness 差 ≥ 0.15），
        //   三族基准取 沙 0.25 / 草 0.40 / 岩 0.55（差 0.15）。同时挂上程序化细节贴图（P-9/P-10）。
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
            // 细节贴图：世界 XZ UV，1/0.05 = 20m 平铺（r5 把世界尺度放大 8×，见 MaterialNoiseBuilder
            //   文件头"采样尺度"：原 0.35 时最细八度只有 ~1.3cm，近景被 mip 抹平、特写看不到砂粒）；
            //   微色斑强度 1.0 = 用满贴图里烘好的 ±（明度 std 见 MaterialNoiseBuilder 构建日志）；
            //   法线强度 1.0（贴图本身的 RMS 斜率已被规到 0.07≈4° 的温和档）。
            ApplyDetailTexture(m, MaterialNoiseBuilder.NoiseKind.SandAlbedo, MaterialNoiseBuilder.NoiseKind.SandNormal,
                0.05f, 1f, 1f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.25f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0f);
            // 干沙/湿沙共用同一组湿参数（否则两种材质交界处湿带断裂露缝，见 ApplySandWetParameters）。
            ApplySandWetParameters(m);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 湿沙（提案/待定：GDD 只有一组沙色，没有"湿沙"色值。
        //   本组由沙地暗档继续压暗 + 提饱和得到，湿润参数让水面附近/浪线内侧的沙更暗更亮）。
        // 【本次改动】_Smoothness 0.22 → **0.30**：必须比干沙（0.25）更滑，否则
        //   "湿沙更滑"这条物理直觉会在两种材质的交界处反过来（旧的 0.12/0.22 配对同理）。
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
            // 与干沙**同一组贴图与参数**：两种沙材质若细节强度不同，交界处会露出贴图强度差。
            ApplyDetailTexture(m, MaterialNoiseBuilder.NoiseKind.SandAlbedo, MaterialNoiseBuilder.NoiseKind.SandNormal,
                0.05f, 1f, 1f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.30f);
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
        //           光滑度 **0.40**（本次改动：旧值 0.10 与沙只差 0.02，违反 §3.2 纪律 1 的 ≥0.15；
        //           0.40 与沙 0.25 / 岩 0.55 各差 0.15）。
        // 【本次改动】挂草族细节贴图（r5 世界尺度 0.25 → 0.04，放大 6.25×，见 MaterialNoiseBuilder
        //   文件头"采样尺度"；原 0.25 时最细八度 ~4cm 近景仍被 mip 抹平）。
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
            // 草族贴图：法线强度 1.1（贴图 RMS 斜率 0.16≈9°，比沙强、比岩弱）。
            ApplyDetailTexture(m, MaterialNoiseBuilder.NoiseKind.GrassAlbedo, MaterialNoiseBuilder.NoiseKind.GrassNormal,
                0.04f, 1f, 1.1f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.40f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_EdgeWear", 0f);
            SetFloat(m, "_Wetness", 0f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // 岩石（GDD §10.4 岩石三档：#5C4F42 → #8C7B6A → #B8A99A）
        // 参数理由：岩石是块状硬表面 → 噪声尺度中等（5）、细节法线强（0.9）制造碎裂感；
        //           光滑度 **0.55**（本次改动：旧值 0.28 与草只差 0.18、与沙差 0.16，勉强达标；
        //           0.55 让"岩 vs 草/沙"一眼可比 —— 岩是风化硬面，湿气/矿物的微光比草沙明显）；
        //           边缘磨损 0.3 + 磨损色偏白 —— 岩棱被风化磨亮是风格化写实的常见做法（AI 提案）。
        // 【本次改动】挂岩族细节贴图（r5 世界尺度 0.5 → 0.10，放大 5×；
        //   法线 1.2 × 贴图 RMS 0.28≈15.6° = 强断裂感）。
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
            ApplyDetailTexture(m, MaterialNoiseBuilder.NoiseKind.RockAlbedo, MaterialNoiseBuilder.NoiseKind.RockNormal,
                0.10f, 1f, 1.2f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_Smoothness", 0.55f);
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
            // 木族细节贴图（v2 新增）：低强度**暖噪**（沙族 albedo 的暖色微斑）+ 岩族法线（节疤/木纹起伏）。
            //   与木纹主噪声（_NoiseStretch=(0.18,1) 的各向异性条带）叠加：主噪声给"长条纹理"、
            //   细节贴图给"介质颗粒" —— 只提噪声强度会把木纹做成"塑料拉丝"，加介质色斑才像木头。
            //   世界尺度 0.55（≈1.8m 平铺）：木板/船舷这类 0.5-3m 构件上能看出 2-4 个斑块。
            ApplyDetailTexture(m, MaterialNoiseBuilder.NoiseKind.SandAlbedo, MaterialNoiseBuilder.NoiseKind.RockNormal,
                0.55f, 0.22f, 0.35f);
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
            // 深色木（v2 新增，与木板同款但强度更低）：压暗木不让细节噪点过跳 —— 深色底上
            //   同样的乘性起伏视觉对比更强（人眼对暗部更敏感），故 0.18/0.30 < 木板的 0.22/0.35。
            ApplyDetailTexture(m, MaterialNoiseBuilder.NoiseKind.SandAlbedo, MaterialNoiseBuilder.NoiseKind.RockNormal,
                0.55f, 0.18f, 0.30f);
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
            // 金属本轮不挂细节贴图（细节贴图是"介质的色斑"，金属的不均匀来自边缘磨损）。
            DisableDetailTexture(m);
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
            DisableDetailTexture(m);
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
        //   _ShoreFadeDistance=8：岛外浅台(y=-1.1)/中台(y=-2.4) 与水面(y=-0.4) 的视深度差
        //     约 0.7 / 2.0 → 8 的分母让浅台落在"浅→中"、中台落在"中→深"（对应三档色）。
        //     【格 1→2 单位 ×2】4 → 8，与海床台阶深度同比例，保证同一处仍是同一档水色。
        //   _FoamWidth=3.2：略大于浅台深度差，让泡沫从岛缘向外覆盖一小段而不是一条死线。
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
            SetFloat(m, "_ShoreFadeDistance", 8f);      // 距离类 ×2（格 1→2 单位）

            SetColor(m, "_FoamColor", Hex("#F0F7FF"));
            SetFloat(m, "_FoamWidth", 3.2f);            // 距离类 ×2（格 1→2 单位）
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
        //   _EdgeColor/_EdgeStrength：**写实化后关闭**地形菲涅尔边缘压暗（原 0.35）。
        //     它是"场景物 #2A2A2A 描边"的替代实现；写实方向明确"无描边"，
        //     故置 0（旋钮与色值保留，随时可调回；对应 shader 的默认值也同步改为 0）。
        //   【本次改动（r3 复验 N4 返工）】
        //     1) _Smoothness 0.15 → **0.25**，并新增 _GrassSmoothness=0.40 / _RockSmoothness=0.55：
        //        shader 现按沙/草/岩权重混合光滑度 → 相邻面差 ≥0.15（美术风格指南.md:199-204 §3.2 纪律 1）。
        //     2) 三族细节贴图（沙/草/岩各一套 albedo+法线；r5 世界尺度放大到沙 0.05/草 0.04/岩 0.10）：
        //        解决 P-9「同材质 200×200 窗 std > 6」与 P-10「高频能量」不达标。
        //     3) _BlockTintStrength 0.12 → **0.06** + 新增 _BlockWarp=0.4：
        //        逐块明暗的方格边界按世界 FBM 打散，顶面不再被读成"地砖/编织布"（写实方向也不要"数字化块面"）。
        //   【本次改动（r5 复验 砖缝去蓝灰）】
        //     4) _BlockTintStrength 0.06 → **0.042**（块缘亮度差 -30%）；
        //     5) 新增 _SeamColor=#7C756A / _SeamStrength=0.18 / _SeamWidth=0.12：
        //        格缝由"乘性压暗"（保留蓝灰天空光色相 → 读成饱和蓝灰勾缝）改为 lerp 到显式暖灰 →
        //        "地砖勾缝"变"沙地裂纹"。色值口径见 PirateTerrain.shader 的 _SeamColor 注释。
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
            // 低多边形辨识度靠"相邻块仍有亮度差"保留；0.042 = 原 0.06 的 70%（块缘亮度差降 30%，r5）。
            SetFloat(m, "_BlockTintStrength", 0.042f);
            SetFloat(m, "_FacetStrength", 0.6f);
            SetFloat(m, "_BlockWarp", 0.4f);
            // 格缝暖灰（r5）：色值 #7C756A（HSV 饱和度 14.5% <15%）、强度 0.18、宽 0.12 格。
            SetColor(m, "_SeamColor", Hex("#7C756A"));
            SetFloat(m, "_SeamStrength", 0.18f);
            SetFloat(m, "_SeamWidth", 0.12f);
            ApplyTerrainDetailTextures(m);
            // 粗糙度分区：_Smoothness 是**沙族**基准（0.25），草/岩在 shader 里按权重混合。
            SetFloat(m, "_Smoothness", 0.25f);
            SetFloat(m, "_GrassSmoothness", 0.40f);
            SetFloat(m, "_RockSmoothness", 0.55f);
            SetFloat(m, "_Metallic", 0f);
            SetFloat(m, "_AmbientStrength", 1f);
            SetColor(m, "_EdgeColor", Hex("#2A2A2A"));
            // 写实化：关闭地形"描边兼容"边缘压暗（旧值 0.35）。理由见 BuildTerrainMaterial 顶部注释。
            SetFloat(m, "_EdgeStrength", 0f);
            SetFloat(m, "_EdgePower", 3f);
            SetFloat(m, "_DebugMode", 0f);
            return Save(m);
        }

        // ------------------------------------------------------------------
        // 细节贴图接线（沙/草/岩三族；程序化资产的引用点）
        // ------------------------------------------------------------------

        /// <summary>
        /// 给 <see cref="SurfaceShaderName"/> 的材质挂上 albedo 微色斑图 + 法线图。
        ///
        /// 【两条强度同时是"开关"】shader 里 <c>_DetailAlbedoStrength</c> / <c>_BumpScale</c> 默认 0
        /// （未赋贴图的木/铜/铁必须与加贴图之前逐像素一致，见 PirateSurface.shader 文件头【默认关闭】）；
        /// 本方法**只在贴图确实加载成功时**才把强度打开，贴图缺失则强度留 0 并记 Warning。
        /// 这样"忘了跑 MaterialNoiseBuilder"的表现是"画面退回改动前 + 一条明确警告"，而不是随机变色。
        /// </summary>
        static void ApplyDetailTexture(Material m, MaterialNoiseBuilder.NoiseKind albedoKind,
            MaterialNoiseBuilder.NoiseKind normalKind, float worldScale, float albedoStrength, float bumpScale)
        {
            SetFloat(m, "_NoiseWorldScale", worldScale);
            SetFloat(m, "_NoiseWarpStrength", 0.18f);   // 打断贴图与 1 单位格子的轴向对齐

            bool albedoOk = AssignDetailTexture(m, albedoKind, "_DetailNoiseMap", "_DetailAlbedoStrength", albedoStrength);
            bool normalOk = AssignDetailTexture(m, normalKind, "_BumpMap", "_BumpScale", bumpScale);

            if (!albedoOk || !normalOk)
                WarnDetailMissing(m.name, albedoOk, normalOk);
        }

        /// <summary>地形材质：三族 albedo + 法线共 6 张，各自的平铺米数与强度。</summary>
        static void ApplyTerrainDetailTextures(Material m)
        {
            SetFloat(m, "_NoiseWarpStrength", 0.18f);
            // r5 世界尺度放大 5-7×：沙 0.05（20m）、草 0.04（25m）、岩 0.10（10m）；
            //   见 MaterialNoiseBuilder 文件头"采样尺度"——原值让最细八度近景被 mip 抹平。
            SetFloat(m, "_SandNoiseWorldScale", 0.05f);
            SetFloat(m, "_GrassNoiseWorldScale", 0.04f);
            SetFloat(m, "_RockNoiseWorldScale", 0.10f);

            bool sandAlb  = AssignDetailTexture(m, MaterialNoiseBuilder.NoiseKind.SandAlbedo,  "_SandNoiseMap",  "_SandDetailAlbedoStrength",  1f);
            bool grassAlb = AssignDetailTexture(m, MaterialNoiseBuilder.NoiseKind.GrassAlbedo, "_GrassNoiseMap", "_GrassDetailAlbedoStrength", 1f);
            bool rockAlb  = AssignDetailTexture(m, MaterialNoiseBuilder.NoiseKind.RockAlbedo,  "_RockNoiseMap",  "_RockDetailAlbedoStrength",  1f);
            // 法线强度 = 贴图自带 RMS 斜率的倍率：沙 1.0(4°) / 草 1.1(9°) / 岩 1.2(15.6°)。
            bool sandNrm  = AssignDetailTexture(m, MaterialNoiseBuilder.NoiseKind.SandNormal,  "_SandBumpMap",  "_SandBumpScale",  1.0f);
            bool grassNrm = AssignDetailTexture(m, MaterialNoiseBuilder.NoiseKind.GrassNormal, "_GrassBumpMap", "_GrassBumpScale", 1.1f);
            bool rockNrm  = AssignDetailTexture(m, MaterialNoiseBuilder.NoiseKind.RockNormal,  "_RockBumpMap",  "_RockBumpScale",  1.2f);

            if (!(sandAlb && grassAlb && rockAlb && sandNrm && grassNrm && rockNrm))
                WarnDetailMissing(m.name, sandAlb && grassAlb && rockAlb, sandNrm && grassNrm && rockNrm);
        }

        /// <summary>
        /// 赋一张细节贴图并打开对应强度；返回 false = 贴图缺失（强度保持 0，材质行为退回改动前）。
        /// 先把强度写 0：保证"上次跑过、这次贴图没了"时不会留下旧的开启状态。
        /// </summary>
        static bool AssignDetailTexture(Material m, MaterialNoiseBuilder.NoiseKind kind,
            string textureProperty, string strengthProperty, float strength)
        {
            SetFloat(m, strengthProperty, 0f);
            if (!m.HasProperty(textureProperty))
                return false;

            Texture2D texture = MaterialNoiseBuilder.Load(kind);
            if (texture == null)
                return false;

            m.SetTexture(textureProperty, texture);
            SetFloat(m, strengthProperty, strength);
            return true;
        }

        /// <summary>刻意不挂细节贴图的材质（黄铜/铁）显式关掉两个强度，保证幂等与可预期。
        /// 金属的不均匀来自边缘磨损（_EdgeWear）而不是介质色斑，与 SceneArtBuilder 对 Scene_Metal
        /// 的处理是同一条纪律。</summary>
        static void DisableDetailTexture(Material m)
        {
            SetFloat(m, "_DetailAlbedoStrength", 0f);
            SetFloat(m, "_BumpScale", 0f);
        }

        static void WarnDetailMissing(string materialName, bool albedoOk, bool normalOk)
        {
            Debug.LogWarning("[BattleSceneLighting] 材质 " + materialName + " 的细节贴图缺失："
                + (albedoOk ? "" : "albedo ")
                + (normalOk ? "" : "normal ")
                + "→ 对应强度已置 0（画面退回改动前，P-9/P-10 会不达标）。"
                + "先跑菜单 PirateCrew/渲染/生成程序化材质噪声贴图（或 ArtGate 的 ⓪.5 步）。");
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

            // ---- Tonemapping：Neutral（保色）----
            // 【ACES vs Neutral 的取舍，选 Neutral】
            //   ACES：电影感强、超亮部滚降漂亮；代价是**明显降饱和、并把暖色往橙黄推**。
            //         本工程是"高饱和三档色阶 + 蓝绿海水 + 青色选中描边"的卡通写实，
            //         ACES 会把沙/草/海的色相拉偏（这也正是旧 Gamma 版靠 saturation +10 找补的原因）；
            //         且 ACES 在 Linear 下还会整体再压暗一档，需要额外抬 postExposure。
            //   Neutral（URP 引入的 HDRP Neutral tonemapper）：1.0 以下近似恒等，只对 >1 的 HDR
            //         高光做滚降，色相/饱和度保真度远高于 ACES。
            //   → 写实化要的是"保色"（基准调研的结论正是 tonemap 保色），故选 Neutral；
            //     代价是高光滚降不如 ACES"胶片"，对本项目大面积高饱和块面是正确的取舍。
            //   若后续人眼验收觉得"不够电影感"，可换回 ACES 并把 postExposure 抬到约 +0.3 补偿压暗。
            Tonemapping tonemapping = EnsureVolumeComponent<Tonemapping>(profile);
            Override(tonemapping.mode, true);
            tonemapping.mode.value = TonemappingMode.Neutral;

            // ---- Bloom：写实"阳光感"（threshold 1.05 / intensity 0.42 / scatter 0.62）----
            // 数值出处：docs/阳光感打光调研.md §4 调法 3（17 条官方文档来源）。
            //   threshold 0.85→1.05：URP 文档明确 Threshold 是 gamma 空间截断、默认 0.9；
            //     0.85 会把大片近白地面也点进辉光，是"亮而糊/刺"的直接来源，
            //     且已偏离美术风格指南 §4.5 自己定的 Bloom 区间（1.0-1.2）。
            //   intensity 0.55→0.42、scatter 0.70→0.62：回指南区间（0.25-0.45 / 0.55-0.7），
            //     辉光只从太阳盘/水面镜面亮带/黄铜高光溢出，不再糊整片画面。
            //   tint=#FFF4E0 = GDD 主光暖色 → 高光晕染偏暖，与环境冷色形成冷暖对比。
            //   highQualityFiltering=false → 性能取舍：Bloom 是 soft-knee 多 mip 模糊，
            //     高质量滤波在该分辨率下观感差异小、开销明显（报告里说明）。
            Bloom bloom = EnsureVolumeComponent<Bloom>(profile);
            Override(bloom.threshold, true);
            bloom.threshold.value = 1.05f;
            Override(bloom.intensity, true);
            bloom.intensity.value = 0.42f;
            Override(bloom.scatter, true);
            bloom.scatter.value = 0.62f;
            Override(bloom.tint, true);
            // tint 经 URP PostProcessPass.cs:1136 `m_Bloom.tint.value.linear` 自行线性化，
            // 故这里给 sRGB 原值（给 .linear 会双重转换）。
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
            // 【为何不随 Linear "重标"】temperature/tint 是**物理量纲的参数**（URP 经
            //   ColorUtils.ColorBalanceToLMSCoeffs 换算成 LMS 系数），不是"以 sRGB 编码的颜色"，
            //   色空间切换不会让它偏差 —— 需要人眼复核的是它与 Neutral tonemap 的新组合观感，见实机清单。
            WhiteBalance whiteBalance = EnsureVolumeComponent<WhiteBalance>(profile);
            Override(whiteBalance.temperature, true);
            whiteBalance.temperature.value = 8f;
            Override(whiteBalance.tint, true);
            whiteBalance.tint.value = 0f;

            // ShadowsMidtonesHighlights（lift/gain 的载体）—— **随 Linear 按 2.2 次幂重标**：
            //   旧 Gamma 值是"在 gamma 编码值上乘"的感知幅度；Linear 下同一乘数作用在线性值、
            //   再经 gamma 编码输出，感知幅度会缩水约 gamma(2.2) 倍。为保住"冷阴影/暖高光"的
            //   **观感量级**，把旧倍率取 2.2 次幂：
            //     阴影  0.98^2.2=0.956 / 1.00 / 1.04^2.2=1.090  （略偏蓝，对应环境浅蓝）
            //     高光  1.02^2.2=1.045 / 1.00 / 0.97^2.2=0.935  （略偏暖，对应主光 #FFF4E0）
            //   中间调仍中性。冷暖分离 = 风格化写实最基本的"光影氛围"手段。
            ShadowsMidtonesHighlights smh = EnsureVolumeComponent<ShadowsMidtonesHighlights>(profile);
            Override(smh.shadows, true);
            smh.shadows.value = new Vector4(0.956f, 1.00f, 1.090f, 0f);
            Override(smh.midtones, true);
            smh.midtones.value = new Vector4(1f, 1f, 1f, 0f);
            Override(smh.highlights, true);
            smh.highlights.value = new Vector4(1.045f, 1.00f, 0.935f, 0f);

            // ---- Vignette：轻微 ----
            // 理由：把视线收拢到竞技场中心（相机固定 45° 俯视、竞技场是画面主体）。
            //   intensity=0.22 是"能感觉到、但不会挡到边角单位"的程度；
            //   smoothness=0.45 让暗角过渡宽缓（否则会像贴了一圈黑边）；
            //   color=#101820 深蓝黑（不用纯黑，与冷环境色调一致）。
            //   **必须 .linear**：URP 的 SetupVignette 走
            //     `material.SetVector(_Vignette_Params1, color.rgb)`（PostProcessPass.cs:1225-1253），
            //     **不做** sRGB→Linear；而 vignette 是在 HDR 线性缓冲里做 lerp。
            //     传 sRGB 原值会让暗角在 Linear 下偏亮偏灰、压不住边角。
            //   这是本文件唯一需要手动 .linear 的颜色（其余 Color 属性由引擎自动转换，见类头）。
            Vignette vignette = EnsureVolumeComponent<Vignette>(profile);
            Override(vignette.color, true);
            vignette.color.value = Hex("#101820").linear;
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
        /// 配置 URP Asset：打开软阴影、打开深度图、MSAA 4→2、ColorGradingMode 切 HDR，
        /// 并强制主光阴影三件套（距离 ≥100 / Cascade 4 / 分辨率 2048；距离随格 1→2 单位 ×2，cascade 级数不动）。
        /// 取舍见报告：软阴影是本波次"光影氛围"的必要项；深度图是 PirateWater 泡沫/浅深水的硬依赖；
        /// 阴影三件套是植被"投影 + 受影"（PirateAmbientWind 的 ShadowCaster/ForwardLit）与单位投影的载体；
        /// MSAA 降档用来抵消前两项带来的带宽/开销增长；ColorGradingMode=HDR 是 Linear+HDR 的配套
        /// （LDR LUT 会把 >1 的高光在 tonemap 前压平，见方法内注释）。
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

            // ---- ColorGradingMode：LDR → **HDR**（Linear + HDR 渲染的配套项）----
            // 依据：URP 的 ColorGradingLutPass 在 HDR 模式下用 R16G16B16A16_SFloat 的 LUT
            //   （ColorGradingLutPass.cs:51-52/91），LDR 模式固定用 R8G8B8A8_UNorm（:64）——
            //   后者把 LUT 采样域钳在 [0,1]，**>1 的 HDR 高光在 tonemap 之前就已被压平**，
            //   于是 Bloom threshold 0.85 与 Neutral 的高光滚降拿不到真实亮部梯度。
            //   本 Asset 已开 m_SupportsHDR=1，故开 HDR 调色是与之匹配的正确档位。
            //   代价：HDR LUT 的调色在 log 域进行，contrast/saturation/SMH 的有效强度与 LDR 档不同
            //   → 已列入实机复核项（观感不对可回退本行）。
            asset.colorGradingMode = ColorGradingMode.HighDynamicRange;

            // ---- 主光阴影三重兜底（核验美术风格指南 §4.1 的「Cascade 4 级 / 阴影距离 50 / soft」）----
            // 【格 1→2 单位】阴影距离 50 → 100；Cascade 级数 4 不动（级数是"分几段"而非距离量）。
            // 现状资产已是 50 / 4 / 2048 / soft on（PC_Balanced_URPAsset.asset），这里在代码侧**强制**一遍，
            // 避免有人手改资产或换 URP Asset 后静默退化：植被投影/受影、单位投影都依赖这套配置。
            //   shadowDistance / shadowCascadeCount 有公共 setter；分辨率与 soft 只有 internal setter，
            //   故分辨率走 SerializedObject 写字段 m_MainLightShadowmapResolution（与 m_SoftShadowsSupported 同法）。
            if (asset.shadowDistance < 100f)
                asset.shadowDistance = 100f;       // ×2（格 1→2 单位）：覆盖 100×34 竞技场 + 外扩（观察者常用取景）
            if (asset.shadowCascadeCount != 4)
                asset.shadowCascadeCount = 4;      // 近景角色脚底到远景植被都要有可用精度

            // supportsSoftShadows 的 setter 在 URP 14 是 internal（有意不开放），
            // 故用 SerializedObject 直接写序列化字段 m_SoftShadowsSupported（资产里确实存在该字段）。
            var so = new SerializedObject(asset);
            SetBoolField(so, "m_SoftShadowsSupported", true);
            SetIntField(so, "m_SoftShadowQuality", 2); // 2 = Medium（URP 默认软阴影质量档）
            // 主光阴影图分辨率 2048（枚举 ShadowResolution._2048 = 2048，URP Asset API 为 internal setter）。
            SetIntField(so, "m_MainLightShadowmapResolution", 2048);
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
                    // 即便没有天空盒，也要把"三灯分层"的环境光写进去（写实氛围的主体）。
                    ApplyThreePointAmbient();
                    return false;
                }

                sky = new Material(skyShader) { name = "BattleSky" };
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }

            // 就地更新（而不是"资产已存在就跳过"）：改了这里的观感常量必须能生效，
            // 否则会出现"代码改了、画面没变"的排查陷阱。

            // ---- 太阳盘（写实化新增；基准：让天空有可见的太阳 + 由 Bloom 溢出"阳光感"）----
            //   _SunDisk=2 → High Quality（0=无盘 / 1=简盘 / 2=高质量盘）；
            //   _SunSize 0.04→0.065 → 太阳视直径明显放大（Procedural 的 _SunSize 是归一化角尺寸）；
            //   _SunSizeConvergence 5→3 → 盘边缘更柔更"发光"，配合 Bloom threshold 0.85 晕出光晕。
            //   【已知取景限制，实机复核】主光 Euler(48,140) → 太阳在仰角 48°、方位 -40°；
            //     战斗相机出厂 pitch 45°/FOV 60° → 画面上缘约在水平线下 15°，
            //     太阳盘**在默认取景下不在画面内**（见 CreateDirectionalLight 注释）。
            //     它的价值：① 水面/黄铜的 SH 亮部与近帧溢色；② 玩家把 yaw 转过去或日后放宽 pitch 时立即可见。
            if (sky.HasProperty("_SunDisk"))
                sky.SetFloat("_SunDisk", 2f);
            if (sky.HasProperty("_SunSize"))
                sky.SetFloat("_SunSize", 0.065f);
            if (sky.HasProperty("_SunSizeConvergence"))
                sky.SetFloat("_SunSizeConvergence", 3f);

            // ---- 天空盒「去绿」（诊断 B：r2 实测近地平线 #C4FBAE 黄绿、全图 G 高于 R 14~16%）----
            // 【发绿根因（读 Skybox/Procedural 内置实现后定位）】
            //   该 shader 的地平线是一条 ≤±0.02 的窄混合带：
            //     col = lerp(skyColor, groundColor, saturate(ray.y / 0.02))       // Skybox-Procedural frag
            //     groundColor(v2f) = _Exposure * (cIn + COLOR_2_LINEAR(_GroundColor) * cOut)
            //   旧 `_GroundColor = #7A6A4C` 是**橄榄黄褐**（sRGB R122/G106/B76），在混合带里与天顶蓝
            //   （B>G>R）做 RGB 平均 —— 「蓝 + 黄 = 绿」，于是地平线出现 G 最高的黄绿带。
            //   `_GroundColor` 只进天空盒下半球/混合带；本工程环境光走 **Trilight**（见 ApplyThreePointAmbient），
            //   **不**从天空盒取 SH，故改它不会动到环境光/金属反射口径。
            //
            // 【目标判据（docs/美术风格指南.md §2.1 天空-正午行 + §4.2）】
            //   · 正午地平线色 = **#BFE3F5**（暖白蓝，hue≈200、L* 78-88）；
            //   · 全图 R/G > 0.98（G 不得系统性高于 R）；
            //   · 禁止出现 L* > 95 的绿/黄绿带（hue 60-160、L* 过高即判失败）。
            //   出处：`docs/美术风格指南.md:92`（天空-正午 #BFE3F5）、`:100`（实现在此落地）。
            //
            // 【本次改动（旧值 → 新值）及理由】
            //   _GroundColor       #7A6A4C → #BFE3F5  —— 直接换成目标地平线色，从混合带里移除"黄"这一半，
            //                                             蓝+黄=绿的根因消失；下半球同时变成暖白蓝。
            //   _AtmosphereThickness 0.85 → 0.70      —— Rayleigh 常数 kRAYLEIGH ∝ thickness^2.5
            //                                             （Skybox-Procedural.shader:64），0.85→0.70 使瑞利光学厚度
            //                                             降约 38%，把 r2 过曝的地平线亮带（L*≈92）拉进 78-88。
            //   _SkyTint           #87CFEB（不变）    —— 其 hue≈197、与目标 200 只差 3°，不是发绿来源；
            //                                             无谓改动会连带改天顶蓝，故不动。
            //   _Exposure          1.1（不变）        —— 曝光由主光/环境光口径约束，避免与已调好的阳光感打架。
            if (sky.HasProperty("_AtmosphereThickness"))
                sky.SetFloat("_AtmosphereThickness", 0.70f);
            if (sky.HasProperty("_SkyTint"))
                sky.SetColor("_SkyTint", Hex("#87CFEB"));   // 天空主调，偏青蓝（hue≈197，非发绿来源）
            if (sky.HasProperty("_GroundColor"))
                // 地平线/下半球色 = 正午目标 #BFE3F5（美术风格指南 §2.1）。
                // 【衔接】雾色仍为 #B0D4F1（ApplySceneAtmosphere，与 AmbientTimeOfDayCatalog 正午档逐值相同），
                //   两者同属蓝白色族，远海/远岛与天空地平线不会出现"接缝"。
                sky.SetColor("_GroundColor", Hex("#BFE3F5"));
            if (sky.HasProperty("_Exposure"))
                sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);

            // ---- 环境光来源（视觉遗留 #6，2026-09-17 翻转）----
            // 开关=天空盒时改用三档渐变天空盒（Sky_Noon；三档材质由 SkyAssetBuilder 生成，
            // ArtGate 步骤 ①.5 在场景装配之前）。BattleSky（内置 Procedural）继续维护不删——
            // 它是回退锚点：把 AmbientSkyboxCatalog.DefaultAmbientSource 改回 Trilight 即整体回退。
            if (AmbientSkyboxCatalog.SkyboxAmbientEnabled)
            {
                Material gradientSky = SkyAssetBuilder.LoadMaterial(AmbientTimeOfDay.Noon);
                if (gradientSky != null)
                {
                    RenderSettings.skybox = gradientSky;
                    ApplyThreePointAmbient();   // 其内部分支写 ambientMode=Skybox
                    return true;
                }

                // 跳步执行（没跑 ArtGate ①.5）时的兜底：回退 BattleSky + Trilight，不阻塞装配。
                Debug.LogWarning("[BattleSceneLighting] 环境光来源开关=天空盒，但找不到 "
                    + SkyAssetBuilder.MaterialPath(AmbientTimeOfDay.Noon)
                    + "（先执行 PirateCrew.EditorTools.SkyAssetBuilder.BuildAll）→ 本次回退 BattleSky + Trilight。");
            }

            RenderSettings.skybox = sky;
            ApplyThreePointAmbient();
            return true;
        }

        /// <summary>
        /// 环境光 = **三灯分层的伪造**（写实化的核心手段之一）。
        ///
        /// 【为什么不用实时光】本工程的三个自定义 shader（PirateSurface / PirateTerrain / PirateOutline）
        ///   只取「主方向光 + SH 环境光 + 雾」，不支持 additional lights；再加两盏实时光等于给每个
        ///   shader 翻倍变体编译量，收益却可由 SH 环境光完全覆盖。
        ///
        /// 【参照基准的三灯比例】基准调研（Blender 离线 PBR）：
        ///   暖主光 energy 3.5 / 冷补光 0.22（右前）/ 暖地面反弹 0.35（自下）。
        ///   → 补光:主光 = 0.063、反弹:主光 = 0.10；反弹约为补光的 1.6 倍。
        ///   本项目把「冷补光」放进 ambientSky（朝上的法线）与 ambientEquator（竖直法线）、
        ///   把「暖反弹」放进 ambientGround（朝下的法线），比例按 1:1.6 量级落色。
        ///
        /// 【为什么用 Trilight 而不是 Skybox 模式】
        ///   1. 三色可控：Skybox 模式的 SH 完全由程序化天空盒决定，无法单独配"冷补/暖反弹"；
        ///   2. 取景上天空盒本来就看不到（相机固定俯视 45°、FOV 60°，画面上缘在水平线下 15°），
        ///      切 Trilight 不会损失可见背景，只把环境光的解释权拿回来。
        ///   代价（如实记录）：水面/金属的 SampleSH 反射也从"天空盒 SH"变成这三色，
        ///   故 ambientSkyColor 特意取偏青蓝，让水面反射仍是"天光"而不是任意色。
        ///
        /// 【色值（sRGB 设计色，引擎自动转 Linear；见类头色空间说明）】
        ///   sky     #7EA8CC  冷蓝补   —— 朝上法线（地面/台地顶面）的冷色托底，制造"暖光冷影"
        ///   equator #93A2AC  地平中性 —— 竖直面（角色/箱桶侧面）的中性灰，避免整场偏蓝
        ///   ground  #C9A268  暖沙反弹 —— 朝下法线（悬空物底面/岩檐）的暖色，模拟沙地反光
        ///   三色 sRGB 相对亮度 ≈ 0.58 : 0.63 : 0.79（sky : equator : ground），
        ///   ground/sky ≈ 1.36，与基准反弹/补光 ≈ 1.6 同向（未拉满，避免朝上的沙地顶面被重复加暖
        ///   —— 那里已有暖主光直射）。
        /// </summary>
        public static void ApplyThreePointAmbient()
        {
            // ---- 环境光来源开关（视觉遗留 #6，2026-09-17 翻转）----
            // Skybox 模式：环境光 SH 完全由天空盒卷积而来，下面三色在 Lighting 窗口里仍可填但不生效，
            // 故只切模式直接返回。Trilight 三灯分层路径**完整保留**——它是唯一能单独配"冷补/暖反弹"
            // 的方案，也是翻案出问题时的回退锚点（改回 AmbientSkyboxCatalog.DefaultAmbientSource 即整体回退）。
            if (AmbientSkyboxCatalog.SkyboxAmbientEnabled)
            {
                RenderSettings.ambientMode = AmbientMode.Skybox;
                // Skybox 模式下 ambientIntensity 仍生效（环境探针的整体倍率，Lighting 窗口的
                // "Intensity Multiplier"）。取目录正午档的 0.85 而不是写死字面量——烘焙值必须与
                // 运行时 AmbientDirector.ApplyPreset 写的值逐值一致，否则编辑器与运行时亮度不一致。
                RenderSettings.ambientIntensity =
                    AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Noon).AmbientIntensity;
                return;
            }

            // AmbientMode.Trilight 就是 Lighting 窗口里的 "Gradient"（Skybox / Gradient / Color）。
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#7EA8CC");     // 冷蓝补光（朝上法线）
            RenderSettings.ambientEquatorColor = Hex("#93A2AC"); // 地平中性（竖直法线）
            RenderSettings.ambientGroundColor = Hex("#C9A268");  // 暖地面反弹（朝下法线）
            // 强度 0.85：与 AmbientDirector 正午档的 ambientIntensity=0.85 逐值一致，
            //   保证 applyPresetOnStart 后环境光强度零跳变（该档只写强度，不写三色 → 三色梯度保留）。
            //   1.00→0.85：拉开直射:天光比例（docs/阳光感打光调研.md §4 调法 2，
            //   Unreal 官方"晴天天空约占总照度 20%"≈4:1；旧 1.35:1.00 仅 ≈2.6:1，天光过强=灰蒙蒙）。
            RenderSettings.ambientIntensity = 0.85f;
        }

        /// <summary>
        /// 主方向光。数值出处：GDD §10.4「主方向光：暖色 #FFF4E0，Shadow 开启」；
        /// 强度 1.35 / 姿态 <c>Euler(48,140,0)</c> 依据 docs/场景设计-战斗竞技场.md §6.5
        /// 与 docs/美术风格指南.md §4.1 Q-7 裁决（强度 1.2–1.4，实现取 1.35）。
        ///
        /// 【两案对比：现 (48,140) vs 基准方位 -34° → **取现案**】
        ///   判据 = 光线方向与相机视线方向的夹角，越接近 90° 越"立体"（侧光出明暗交界），
        ///   接近 0° 是正视平光、接近 180° 是逆光剪影。
        ///   · 现案 Euler(48,140,0)：光线方向 ≈ (0.43,-0.74,-0.51)；相机在 (0,+12.7,-12.7)、
        ///     俯视 45° → 视线 ≈ (0,-0.707,+0.707)；两者夹角 ≈ **80.6°**（近侧光，立体感最强）。
        ///   · 基准案（Blender 太阳 (42°,0,-34°) 映射到 Unity 世界）：光线方向 ≈ (0.37,-0.74,+0.56)，
        ///     与相机视线夹角 ≈ **23.5°**（近乎沿视线照射 → 朝镜头的面整体背光，正是旧 -30° 被否掉的
        ///     "画面发平"病灶同族）。基准的 -34° 是 Blender 作者空间的姿态，直接 1:1 搬进 Unity 会逆光。
        ///   · 结论：保留 (48,140,0)。
        ///   【另一条硬约束】运行时的 <c>AmbientDirector</c> 会在 Start 应用正午档预设并**覆写**
        ///   主光颜色/强度/姿态与雾参数（<c>AmbientTimeOfDayCatalog.cs</c> 的正午档逐值等于本方法，
        ///   该文件不在本波次白名单）。若在这里改角度/强度而不改预设，一开局就会被写回旧值 ——
        ///   这也是"取现案"必须成立的理由之一（改基准案需同步改黑名单文件，越界）。
        /// </summary>
        public static Light CreateDirectionalLight()
        {
            var go = new GameObject("Directional Light", typeof(Light));
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = Hex("#FFF4E0");
            // 旧值 1.1 / Euler(50,-30,0) 已作废：方位 -30° 让朝镜头的面全部背光，是画面发平主因之一。
            // 1.35→1.55：与 ambientIntensity 0.85 配对拉开直射:天光到 ≈4:1（阳光感七要素之
            //   "阴影做深+天光收敛"，docs/阳光感打光调研.md §4 调法 2）。
            light.intensity = 1.55f;
            // 投影是"看起来像 3D"的主要深度线索之一（对齐 Godot 基准的 shadow_enabled）。
            // LightShadows.Soft 需要 URP Asset 打开 m_SoftShadowsSupported（ConfigureUrpAsset 已打开）。
            light.shadows = LightShadows.Soft;
            // 0.7→0.86：阴影做深拉清"直射 vs 天光"分界（调研 §4 调法 1）。本工程
            //   PirateSurface.shader:452-454 的环境光不乘阴影衰减，深阴影不会死黑。
            light.shadowStrength = 0.86f;
            // 48° 仰角 + 140° 方位：入射方向约 (0.43,-0.74,-0.51)，光从相机侧后方来，
            // 朝镜头的面受光、阴影朝屏幕右下延伸。**不得回退到 -30°**（见上文裁决出处与两案夹角计算）。
            // 天空盒的太阳盘（ConfigureSkyAndAmbient 的 _SunDisk/_SunSize）由本方向光驱动，
            // 太阳方位 = 仰角 48°/方位 -40°。
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
            // 【写值口径】正午雾色/雾距 = AmbientTimeOfDayCatalog 正午档 = Battle.unity RenderSettings
            //   烘焙值，**三方逐值一致**：运行时 AmbientDirector.ApplyPreset 会覆写 fogColor/fogStart/
            //   fogEnd，只改本处不改另外两处，运行时会跳回预设值、编辑器与运行时画面不一致。
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#B0D4F1");
            // 距离按审计契约「可见海域预算」（docs/审计/视觉审计报告.md §三）【提案/待定】：
            //   全景相机距离 82~160u、55° 俯角下画面可见海面斜距 ≤~350u。
            //   start=150 —— 主战区在全景与近景取景下都不被雾洗白（雾从视野远端才开始）；
            //   end=1200 —— 雾全饱和点落在海洋侧地平线融合（1000→1400）区间内，
            //               远海平滑并入雾色（= 天空地平色族），海天线在雾饱和前收干净。
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 1200f;

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
        /// GDD §10.4 调色板的 sRGB 十六进制 → Unity Color（**保持 sRGB 语义，不做转换**）。
        /// 本工程是 **Linear** 色空间（ProjectSettings m_ActiveColorSpace=1），但普通材质 Color 属性
        /// 与 Light/RenderSettings 颜色都由引擎/URP 自动 sRGB→Linear（证据见类头），
        /// 故这里直接归一化即可；**改成 .linear 会让这些颜色双重变暗**。
        /// 唯一例外是 Vignette.color —— 那条路径 URP 不转换，调用点自行 .linear（见 EnsureVolumeProfile）。
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
