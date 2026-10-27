using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PirateCrew.Core;
using PirateCrew.Rendering;
using PirateCrew.WaterLab;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 实验水体场景（WaterLab）的装配器：代码建场景（沿 Battle/Pixelart 试点惯例，重跑即重建），
    /// 菜单 <c>Tools/PirateCrew/WaterLab/Build Scene</c>。
    ///
    /// 【组成】太阳平行光 + 透视相机 + 程序化沙滩地形网格（顶点直接放 TerrainHeight 高度）
    /// + 水面平面（顶点位移由 WaterLabOcean.shader 采样模拟高度场）+ WaterLabSimulation 驱动物体。
    ///
    /// 【资产】材质落盘 <c>Assets/Art/Materials/WaterLab/</c>（场景不内嵌材质，必须存资产）；
    /// compute 引用按路径加载注入（场景序列化 PPtr）。实验场景不进 Build Settings（编辑器验证用）。
    /// 场景路径经 <see cref="SceneNames.PathOf"/> 推导（Lab 二级目录的唯一真源）。
    /// </summary>
    public static class WaterLabSetup
    {
        const string ShaderPathOcean = "Assets/Art/Shaders/WaterLab/WaterLabOcean.shader";
        const string ShaderPathSand = "Assets/Art/Shaders/WaterLab/WaterLabSand.shader";
        const string ShaderPathWhitewater = "Assets/Art/Shaders/WaterLab/WaterLabWhitewater.shader";
        const string ShaderPathProp = "Assets/Art/Shaders/WaterLab/WaterLabProp.shader";
        const string ComputePath = "Assets/Art/Shaders/WaterLab/WaterLabSim.compute";
        const string MaterialFolder = "Assets/Art/Materials/WaterLab";
        // I5b 挂接的复刻 shader 件（I5a d11ed7c0 提交）与 Strata 层理图（sa0 pid212，
        // I5a 已按数据贴图口径入库：sRGB off/Repeat/Bilinear）。
        const string ShaderPathBorders = "Assets/Art/Shaders/WaterLab/ReplicaBorders.shader";
        const string ShaderPathSkybox = "Assets/Art/Shaders/WaterLab/ReplicaSkybox.shader";
        const string ShaderPathFrame = "Assets/Art/Shaders/WaterLab/ReplicaFrame.shader";
        const string StrataRefPath = "Assets/Art/Textures/WaterLab/Strata_Ref.png";

        /// <summary>主菜单快照（I7 ReplicaMode 数据底座）：B 流 frida dump 的
        /// groundHeight/waterAmount，256² float32，Unity 按 TextAsset 导入。
        /// MenuWeights512 = W1 耦合权重场（B2 定案，512² float32 = 4bit 权重/15）。</summary>
        const string ReplicaFolder = "Assets/Art/WaterLabReplica";
        const string ReplicaGroundPath = ReplicaFolder + "/MenuGround256.bytes";
        const string ReplicaWaterPath = ReplicaFolder + "/MenuWater256.bytes";
        const string ReplicaWeightsPath = ReplicaFolder + "/MenuWeights512.bytes";

        /// <summary>
        /// ReplicaMode（协调者 Phase2 裁决）：快照资产可加载即开——地形/水位/装配走
        /// 主菜单快照；任一缺失（或校验失败）回退程序剖面。装配期先把快照灌进静态
        /// 采样器，<see cref="WaterLabRules.TerrainHeight"/> 的委托即刻生效，地形网格、
        /// 水底参照物与模拟初值自动同源。
        /// </summary>
        public static bool ReplicaMode => WaterLabReplicaSnapshot.HasData;

        /// <summary>装配开始时调用：把 .bytes 灌进静态采样器（已加载则跳过）。</summary>
        static void EnsureReplicaSnapshotLoaded()
        {
            if (WaterLabReplicaSnapshot.HasData)
                return;
            var ground = AssetDatabase.LoadAssetAtPath<TextAsset>(ReplicaGroundPath);
            var water = AssetDatabase.LoadAssetAtPath<TextAsset>(ReplicaWaterPath);
            if (ground == null || water == null)
            {
                Debug.LogWarning($"[WaterLabSetup] ReplicaMode=关：快照资产缺失"
                    + $"（{ReplicaGroundPath} / {ReplicaWaterPath}），使用程序剖面回退。");
                return;
            }
            try
            {
                WaterLabReplicaSnapshot.Load(ground.bytes, water.bytes);
                Debug.Log($"[WaterLabSetup] ReplicaMode=开（主菜单快照）{WaterLabReplicaSnapshot.Describe()}");
            }
            catch (System.Exception e)
            {
                WaterLabReplicaSnapshot.Unload();
                Debug.LogError($"[WaterLabSetup] 快照校验失败，回退程序剖面：{e.Message}");
            }
        }

        /// <summary>地形渲染网格每轴段数——**仅程序剖面回退用**；ReplicaMode 下网格
        /// 直接从快照 256² 出（见 CreateTerrain）。回退 512 段在 30 m 域上 = 5.86 cm/顶点，
        /// 恰为模拟地形格距（GroundCells=1024 的 2 倍距），不再有"取一半分辨率"的省略。</summary>
        const int TerrainSegments = 512;

        /// <summary>水面渲染网格每轴段数：30 m/512 = 5.86 cm/顶点，与水模拟格距一致
        ///（顶点距 = 1 格，η 高度场的每个格都有顶点承接）。</summary>
        const int SurfaceSegments = 512;

        [MenuItem("Tools/PirateCrew/WaterLab/Build Scene")]
        public static void BuildScene()
        {
            // 确认闸（审计 T2/P1）：本链会 NewScene(Single) 顶掉当前场景、并整目录删除材质库，
            // 两个确认都放在动作之前——拒绝时什么都不动（NewScene 一旦执行，当前场景就回不来了）。
            if (!EditorDestructiveGuard.ConfirmSceneReplacement("重建 WaterLab 实验水体场景"))
                return;

            // 资产删除确认：材质目录重建语义 = 先删后建，目录里若被手放过材质会一并删除。
            if (!EditorDestructiveGuard.ConfirmAssetDeletion(
                    "重建 WaterLab 实验水体场景", EditorDestructiveGuard.CountAssetsInFolder(MaterialFolder)))
            {
                Debug.LogWarning("[WaterLabSetup] 已取消重建：材质目录删除未被确认（未动任何内容）。");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ReplicaMode 先行（I7）：快照可加载与否决定本轮装配走复刻件还是程序回退。
            EnsureReplicaSnapshotLoaded();

            // 材质目录先删后建：shader 默认值改动只影响新建材质，旧 .mat 会锁死旧参数
            //（序列化陷阱）。重建语义 = 全量重装配。
            AssetDatabase.DeleteAsset(MaterialFolder);
            EnsureFolder(MaterialFolder); // 材质目录先建（装配过程中就要写材质资产）

            CreateLighting();
            var camera = CreateCamera();
            CreateTerrain();
            CreateBottomProps(); // 水底参照物：透明感的载体（不透明队列，被水体折射）
            CreateWaterSurface();
            EnsureBorders(); // I5b：Back/FrontGround/FrontWater 三剖面 + Frame 白模件（H 流几何契约）
            EnsureSkybox(); // I5b：RenderSettings.skybox = ReplicaSkybox（t=1/3 菜单时刻真值）
            CreateSimulation(camera); // 相机随注：白水逐粒视锥剔除要用它的 6 平面
            EnsureWhitewaterFeature();
            EnsurePostProcess(camera); // I4：DoF+Vignette（F 流参数直译，无 tonemap/bloom）

            Directory.CreateDirectory("Assets/Scenes/Lab");
            var path = SceneNames.PathOf(SceneNames.WaterLab);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[WaterLabSetup] 场景已装配并保存：{path}");
        }

        /// <summary>无头出图组合入口：重建场景 → 武装截图器（它进 Play 抓完自己 Exit 收口）。
        /// 必须带图形跑（capture 铁律禁 -nographics）；输出目录 <c>PC3D_CAPTURE_DIR</c>。
        /// 不经 HeadlessChain：截图器是异步武装流程，链式器的同步收口会掐死它。</summary>
        public static void BuildSceneAndCapture()
        {
            BuildScene();
            BattleHudScreenshot.CaptureHeadless();
        }

        static void CreateLighting()
        {
            // 【I8 固定时刻移植（2026-10-27，menu-state.json 三帧全同）】主菜单
            // DayCycler.timelapse=0（静止）、t=1/3 Morning（elevation z=330°）：
            // Sun 色 (1,0.940429,0.872353)×1.0、Soft（level0 组件参数 strength=1.0/
            // bias=0.1/normalBias=1.0）、世界旋转 q(0.023437,−0.748097,0.536788,−0.389435)
            //（forward=(0.6078,−0.7849,−0.1204)）；**MoonLight 在该时刻 enabled=false（黑）**
            // ——环境光走 RenderSettings.ambientSkyColor (0.698039,0.564706,0.569923)
            //（environmentGradient@t=1/3 过 hueVariation −0.109）；fog=false。
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.0f;
            light.shadowStrength = 1.0f;
            light.shadowBias = 0.1f;
            light.shadowNormalBias = 1.0f;
            light.color = new Color(1.0f, 0.940429f, 0.872353f);   // t=1/3 sunGradient
            sun.transform.rotation = new Quaternion(0.023437f, -0.748097f, 0.536788f, -0.389435f);
            sun.transform.position = new Vector3(-6.078317f, 7.848856f, 1.203975f);

            // MoonLight：菜单时刻关闭（enabled=false, 色黑）——保留 GameObject 但禁用，
            // 昼夜循环移植后由 t 驱动启停。
            var sky = new GameObject("SkyLight");
            var skyLight = sky.AddComponent<Light>();
            skyLight.type = LightType.Directional;
            skyLight.shadows = LightShadows.None;
            skyLight.enabled = false;   // menu-state t=1/3：moon.enabled=false
            skyLight.color = new Color(0.0627f, 0.1333f, 0.2196f); // 昼夜循环其它时刻的序列化色

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.698039f, 0.564706f, 0.569923f); // ambientSkyColor@t=1/3
            RenderSettings.sun = light; // WaterLabSimulation.PublishSunDir 解析源
        }

        static Camera CreateCamera()
        {
            var go = new GameObject("LabCamera");
            // 出图链（BattleHudScreenshot）按 Camera.main 取景；本场景只有这一台相机，
            // 打 tag 同时消掉 ShotUICam 建立后 FindObjectOfType 回退误命中的风险。
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = false;
            // 【I8 相机移植（menu-state.json 三帧全同，矩阵级）】主菜单 LabCamera：
            // FOV 20、near 8.736618、far 46.223755、pos (7.570009,20.671303,−21.924673)、
            // rot q(0.37687,−0.16043,0.066452,0.909844)、bg (0.801526,0.506065,0.560869)。
            // 原作 projectionMatrix [3.190096,5.671281,…] 与 FOV20/aspect1.778 解析一致
            //（竖 FOV 2·atan(1/5.671281)≈20°），Unity 重算投影即可，不改 near/far 以外的项。
            cam.fieldOfView = 20f;
            cam.nearClipPlane = 8.736618f;
            cam.farClipPlane = 46.223755f;
            cam.backgroundColor = new Color(0.801526f, 0.506065f, 0.560869f);
            go.transform.position = new Vector3(7.570009f, 20.671303f, -21.924673f);
            go.transform.rotation = new Quaternion(0.37687f, -0.16043f, 0.066452f, 0.909844f);
            return cam;
        }

        static void CreateTerrain()
        {
            // ReplicaMode：顶点直接从快照 256² 出（格角栅格 257² 覆盖全域，边缘半格
            // clamp 延伸），高度 = groundHeight 世界 Y 原值（TerrainHeight 已委托采样器）；
            // 回退：程序剖面 512 段。
            int segments = ReplicaMode ? WaterLabReplicaSnapshot.Resolution : TerrainSegments;
            var mesh = new Mesh { name = "WaterLabTerrain" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            int verts = segments + 1;
            float step = WaterLabRules.DomainSize / segments;

            var vertices = new Vector3[verts * verts];
            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    float wx = -WaterLabRules.DomainSize * 0.5f + x * step;
                    float wz = -WaterLabRules.DomainSize * 0.5f + z * step;
                    vertices[x + z * verts] = new Vector3(wx, WaterLabRules.TerrainHeight(wx, wz), wz);
                }
            }

            var triangles = new int[segments * segments * 6];
            int t = 0;
            for (int z = 0; z < segments; z++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i0 = x + z * verts;
                    int i1 = i0 + 1;
                    int i2 = i0 + verts;
                    int i3 = i2 + 1;
                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var sandMat = LoadOrCreateMaterial("WaterLab_Sand", ShaderPathSand);
            ApplySandReplicaValues(sandMat); // I2：焦散真身 + Carbonate 预设 + 沙噪声真图
            // 深水沙变体开启（r99 复盘：σ≡0 下浅水≈亮沙，原作浅水青蓝正身是
            // lerp(沙色, 4×waterColor, min(10d,1))——10cm 水深即全混入；I2 预留的
            // "待抓帧终审"由 r99 首帧色带证据裁决为开）。
            sandMat.EnableKeyword("_WATERLAB_DEEP_SAND");
            go.AddComponent<MeshRenderer>().sharedMaterial = sandMat;
            Debug.Log($"[WaterLabSetup] 地形网格：{(ReplicaMode ? "快照 256² 直出" : "程序剖面")}，"
                + $"{verts * verts} 顶点 / {segments} 段");
        }

        /// <summary>
        /// 沙/礁石统一的显示链曝光因子（与 shader 默认值同源；此处显式下发是因为
        /// <see cref="LoadOrCreateMaterial"/> 只在资产不存在时取 shader 默认值——
        /// 改了 shader 默认值而不回填，已存在的 .mat 会一直留着旧数）。
        /// 【1.08】环境项抬到 (0.18,0.20,0.22) 补原作 GI 的暗部亮度后，受光核略增；
        /// 同步把曝光从 1.20 压到 1.08，保持参考受光干沙 (253,229,169) 同亮度。
        /// </summary>
        const float LightScale = 1.00f;

        /// <summary>
        /// 沙面材质复刻值（I2，真值单源）：全部为 D 流第一手真值，逐条带出处；重建时全量
        /// 下发，.mat 旧值残留不生效（D 表§4.2：原作运行时同样用预设覆写材质）。
        /// 颜色一律 Vector 下发——Linear 工程的 Color 属性会被 sRGB→线性再压一次
        /// （交接§6 陷阱），预设的 linear 序列化值必须原样直达 shader。
        /// </summary>
        static void ApplySandReplicaValues(Material m)
        {
            // 【Carbonate 预设三色】D 表§4.2 从 Refs 裸字节恢复（主菜单 = SandType.Carbonate：
            // Sand 材质序列化 sandColor 与 type0.color 逐位相同）。旧 _DrySandColor=(1.0,0.92,0.69)
            // 头图反定与「湿/干只暗 2–13%」定标依据一并作废（湿/干比真值 ≈0.85/0.86/0.89）。
            m.SetVector("_SandColorDry", SandColorDry);     // cb0[127] = 预设 color
            m.SetVector("_SandColorWet", SandColorWet);     // cb0[129] = 预设 wetColor
            m.SetVector("_SandColorThird", SandColorThird); // cb0[130] = 预设 color2（水侧场×20 混入）
            // 【水色】Water 材质序列化 waterColor（materials-dump.txt / 交接§3 台账）
            // ——深水沙变体 o3 的 4× 目标色（σ≡0，见 shader 深水段）。
            m.SetVector("_WaterColor", SandWaterColor);
            // 焦散真身（原作 Caustics Texture3D）与沙噪声 1024²（原作 Lit_Sand t3）。
            // 构建失败返回 null = 槽落空：焦散被淹没门控自然置 0、相位退化为常量，
            // 场景仍可装配（不阻断重建）。
            m.SetTexture("_CausticsTex", WaterLabCausticsImport.EnsureCausticsAsset());
            m.SetTexture("_WaterLabSandMap", EnsureSandMapAsset());
            m.SetFloat("_LightScale", LightScale);
            m.SetColor("_AmbientColor", AmbientColor);
            // 天光第二方向光（level0 A 级真值，见 CreateLighting）：Vector 下发不走色彩空间
            m.SetVector("_SkyLightDir", new Vector4(0.35f, 0.90f, 0.26f, 0f));
            m.SetVector("_SkyLightColor", new Vector4(0.0627f, 0.1333f, 0.2196f, 1f));
            // 【I2 删项】_SparkleStrength/_SparkleScale（自造哈希闪沙——原作无独立闪沙，
            // D 表§5-3）；_CausticScale/_CausticGain/_CausticSampleOffset（细节法线散度程序
            // 焦散的旋钮——焦散换真身后强度链全是原作字面量，D 表§2.3 段 C/E）；
            // _GrainStrength/_GrainScale（哈希粒噪——沙噪声真图接入后幅度 0.2、尺度 ×0.3
            // 均为原作字面量，D 表§5-4）；_UnderwaterSandColor（水下色由第三色/深水变体真值承担）。
        }

        // 【Carbonate 预设三色 + 水色】出处：预设三色 = D 表§4.2（Refs 裸字节恢复，
        // linear 序列化值）；水色 = Water 材质序列化 waterColor（materials-dump.txt）。
        static readonly Vector4 SandColorDry   = new Vector4(0.9255f, 0.8863f, 0.7255f, 1f);
        static readonly Vector4 SandColorWet   = new Vector4(0.7890f, 0.7592f, 0.6476f, 1f);
        static readonly Vector4 SandColorThird = new Vector4(0.8353f, 0.8078f, 0.6902f, 1f);
        static readonly Vector4 SandWaterColor = new Vector4(0.9603f, 0.9858f, 0.9995f, 1f);

        /// <summary>焦散采样世界尺度（1/m）：周期 = 1/此值。【I2 后仅 WaterLabProp 消费】——
        /// 沙面焦散已换原作真身（Texture3D，强度链为原作字面量，见 ApplySandReplicaValues），
        /// 本仓程序焦散旋钮只剩水底参照物在用。</summary>
        const float CausticScale = 0.5f;

        /// <summary>焦散增益（prop 用，口径同 <see cref="CausticScale"/> 注）。</summary>
        const float CausticGain = 0.6f;

        /// <summary>
        /// 天光环境项（沙/石共用）。原作 HDR+GI 下暗部由天空 + 沙面反弹补亮；本仓只有
        /// 这一个常数项，取 (0.18,0.20,0.22) 近似合成亮度——旧 (0.08,0.14,0.21) 只保留
        /// 天空色，阴影里的沙红通道过低、发青发死。曝光因子已同步压到 1.08。
        /// </summary>
        static readonly Color AmbientColor = new Color(0.10f, 0.11f, 0.12f);

        /// <summary>
        /// 散射显示域强度：原作散射色 (0.568,0.357,0) 是 HDR 管线里的文件值，经 tonemap
        /// 压缩后才是参考图里的暖调；本仓 LDR 直出若按 1.0 用，深水 R 通道会比参考高
        /// 4–5 倍（r54 深带 R=67 vs 参考 16）。0.12 按参考深带反定——保留近岸暖散射，
        /// 深水不再泛紫。
        /// </summary>
        const float ScatterStrength = 0.04f;

        /// <summary>
        /// 天空反射标定（原作 PS 步骤 8 是「按菲涅尔混环境色」，环境色依赖原作的
        /// HDR 后处理/GI——本仓没有那一层，用线性/HDR 域的环境色近似）。
        /// 【为什么是 Vector4 而不是 Color】Linear 工程会把 Color 属性按 sRGB→线性再压一次：
        /// 旧 (0,0.2,0.8) 进 shader 实际只有 (0,0.033,0.603)，绿通道被压掉 6 倍，深水只剩蓝、
        /// 没有参考那种带青的宝蓝。环境色必须按线性值原样下发。
        /// 【定标】参考深带线性化 (0.0024,0.053,0.216)；顶部（viewDir.y≈0.53 → fresnel≈0.023）
        /// 强度 9.5 得权重≈0.19，(0.01,0.30,1.15)×0.19≈(0.002,0.057,0.219)，命中参考深带。
        /// 近岸俯视（viewDir.y≈0.85 → fresnel≈7.6e-5）几乎不受影响。
        /// 菲涅尔用宏观视角而非逐像素法线（r52 实证后者会把整片水刷亮）。
        /// </summary>
        static readonly Vector4 SkyReflectionColor = new Vector4(0.01f, 0.30f, 1.15f, 1f);
        const float SkyReflectionStrength = 9.5f;

        /// <summary>
        /// 深水 GI 兜底（原作体素 Gi 的显示域近似，C 级自造项）：本仓无 GI/HDR tonemap，
        /// 深水 G 通道塌成墨蓝（shelf14-r1 实测 y3=(10,41,97) vs 参考最深带 (16,70,119)）。
        /// 【加法项 col += 色 × saturate(光程×0.4)】（r2-gi 用 lerp 权重饱和到 1，深水被
        /// 平涂成纯色、深度渐变/天空反射全被盖掉——GI 是加法 irradiance，lrep 会抹层次）。
        /// 色值 = y3 出射差 (0.0026,0.0385,0.0692) ÷ giW(y3)≈0.88 → (0.003,0.044,0.079)
        /// （线性域，Vector 下发不走色彩空间）。
        /// 【依据】逆向源码实锤原作链 = HDR RT + PPv2 tonemap + 体素 Gi（128³ giVoxels），
        /// profile 数值 typetree 剥离（D 级）→ 按参考画面反定，与 σ 同法。
        /// </summary>
        static readonly Vector4 GIFillColor = new Vector4(0.002f, 0.038f, 0.068f, 1f);
        const float GIFillStrength = 1.0f;

        /// <summary>
        /// 细节法线贴图：水面碎金的高频法线源，同时是沙侧焦散的坡度场源。
        /// 【优先级】原作提取贴图 <see cref="RefNormalAssetPath"/>（项目所有者授权的
        /// 临时复用，观感对标验证用，之后替换；通道与导入口径见同目录 README.md）
        /// → 找不到时回退程序生成版 <see cref="WaterLabDetailNormal"/>（统计对齐原作、
        /// 形态为 Voronoi 胞近似的等价物）。生成一次落资产，重建时已存在则复用。
        /// </summary>
        static Texture2D LoadOrCreateDetailNormal()
        {
            var refTex = EnsureRefNormalAsset();
            if (refTex != null) return refTex;

            var path = $"{MaterialFolder}/WaterLabDetailNormal.asset";
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (detail == null)
            {
                detail = WaterLabDetailNormal.Create();
                AssetDatabase.CreateAsset(detail, path);
            }
            return detail;
        }

        /// <summary>原作提取细节法线贴图的资产路径（临时复用；.meta 固定 guid 防重建漂移）。</summary>
        const string RefNormalAssetPath = MaterialFolder + "/WaterNormal2D_Ref.png";
        const string RefNormalAssetMetaPath = RefNormalAssetPath + ".meta";
        const string RefNormalAssetGuid = "e3a1f20c8b7d4e5f9a6b0c1d2e3f4a5b";
        const string MaterialFolderReadmePath = MaterialFolder + "/README.md";

        /// <summary>
        /// 确保原作提取贴图已入库并按数据贴图口径导入，返回它；源不可得时返回 null
        /// （调用方 <see cref="LoadOrCreateDetailNormal"/> 会回退程序生成版）。
        ///
        /// 【为何要自愈】<see cref="BuildScene"/> 对材质目录是「先删后建」——本贴图与
        /// README 会被一并删掉，这里负责从工程外源拷回，保证 git 无漂移（PNG 字节一致、
        /// .meta 固定 guid、README 固定文案）。
        /// 【导入口径】linear 色空间、Repeat wrap、mip 开、不压缩、Default 纹理类型：
        /// NormalMap 导入器会把通道重排成 DXT5nm 布局，破坏「RG=法线 xy、B=相位/高度」
        /// 的原作编码——shader 端按 <c>.rg * 2 - 1</c> 自行解码，必须保通道原样。
        /// </summary>
        static Texture2D EnsureRefNormalAsset()
        {
            if (!File.Exists(RefNormalAssetPath))
            {
                var src = RefNormalSourceCandidates().FirstOrDefault(File.Exists);
                if (src == null)
                {
                    Debug.LogWarning(
                        "[WaterLabSetup] 未找到原作细节法线源 PNG（external 工作台缺失），回退程序生成贴图。");
                    return null;
                }
                File.Copy(src, RefNormalAssetPath);
                File.WriteAllText(RefNormalAssetMetaPath,
                    $"fileFormatVersion: 2\nguid: {RefNormalAssetGuid}\nTextureImporter: {{}}\n");
                // 先 ImportAsset 让 AssetDatabase 感知新文件，否则下方 GetAtPath 返回 null。
                AssetDatabase.ImportAsset(RefNormalAssetPath);
            }
            if (!File.Exists(MaterialFolderReadmePath))
                File.WriteAllText(MaterialFolderReadmePath, RefNormalReadmeText);

            var importer = AssetImporter.GetAtPath(RefNormalAssetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[WaterLabSetup] {RefNormalAssetPath} 不是 TextureImporter 资产，回退程序生成贴图。");
                return null;
            }
            // 主类型校验别省：meta 里 generateCubemap=FullCubemap 会把 Default 贴图导入成
            // Cubemap（importer 各项设置却全部「正确」），LoadAssetAtPath<Texture2D> 静默 null。
            var mainType = AssetDatabase.GetMainAssetTypeAtPath(RefNormalAssetPath);
            if (mainType != typeof(Texture2D)
                || importer.textureType != TextureImporterType.Default
                || importer.sRGBTexture
                || importer.wrapMode != TextureWrapMode.Repeat
                || !importer.mipmapEnabled
                || importer.filterMode != FilterMode.Bilinear
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.generateCubemap = TextureImporterGenerateCubemap.None;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(RefNormalAssetPath);
            if (texture == null)
                Debug.LogWarning($"[WaterLabSetup] {RefNormalAssetPath} 导入后拿不到 Texture2D（检查导入日志），回退程序生成贴图。");
            return texture;
        }

        /// <summary>源 PNG 候选：环境变量优先，其后工程根外层的 external 工作台
        /// （主仓与 worktree 同构：&lt;工程根&gt;/../external/sandcastle-ref/）。</summary>
        static IEnumerable<string> RefNormalSourceCandidates()
        {
            var env = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_REF_NORMAL_SRC");
            if (!string.IsNullOrEmpty(env)) yield return env;
            yield return Path.GetFullPath(System.IO.Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "../external/sandcastle-ref/WaterNormal2D-extracted.png"));
        }

        /// <summary>原作沙噪声贴图入库路径（Lit_Sand t3 = Texture path_id 232 "Sand"，
        /// 1024²：R=逐 texel 白噪声 std 0.161、G=低频流供焦散相位 base——D 表§2.3 段 A/§5-4；
        /// meta 固定 guid 防重建漂移，自愈模式同 EnsureRefNormalAsset）。</summary>
        const string SandMapAssetPath = "Assets/Art/Textures/WaterLab/SandNoise_Ref.png";
        const string SandMapAssetGuid = "c8d5e9f3a2b64d178e50f3b9c7a26d41";

        /// <summary>
        /// 确保原作沙噪声贴图已入库并按**数据贴图**口径导入，返回它；源不可得时返回 null
        /// （shader 端退化为 gray=0.5：白噪声调制归零、焦散相位成常量，场景仍可装配）。
        /// 【导入口径必须保数据语义】G 通道进焦散相位 base（PS:41-43），sRGB 解码或压缩
        /// 都会改写相位标定：linear、Repeat、mip 开、Bilinear、不压缩、Default 纹理类型。
        /// </summary>
        static Texture2D EnsureSandMapAsset()
        {
            if (!File.Exists(SandMapAssetPath))
            {
                string src = SandMapSourceCandidates().FirstOrDefault(File.Exists);
                if (src == null)
                {
                    Debug.LogWarning(
                        "[WaterLabSetup] 未找到原作沙噪声提取件（external 工作台缺失），焦散相位退化为常量。");
                    return null;
                }
                File.Copy(src, SandMapAssetPath);
                File.WriteAllText(SandMapAssetPath + ".meta",
                    $"fileFormatVersion: 2\nguid: {SandMapAssetGuid}\nTextureImporter: {{}}\n");
                AssetDatabase.ImportAsset(SandMapAssetPath);
            }
            var importer = AssetImporter.GetAtPath(SandMapAssetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[WaterLabSetup] {SandMapAssetPath} 不是 TextureImporter 资产，沙噪声落空。");
                return null;
            }
            if (importer.textureType != TextureImporterType.Default
                || importer.sRGBTexture
                || importer.wrapMode != TextureWrapMode.Repeat
                || !importer.mipmapEnabled
                || importer.filterMode != FilterMode.Bilinear
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(SandMapAssetPath);
        }

        /// <summary>沙噪声源候选：env 优先，其后工程根外层工作台（dataPath 上两级 =
        /// 仓库/worktree 根，主仓与 temp worktree 同构）。</summary>
        static IEnumerable<string> SandMapSourceCandidates()
        {
            var env = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_SANDMAP_SRC");
            if (!string.IsNullOrEmpty(env)) yield return env;
            yield return Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath,
                "../../external/sandcastle-ref/extracted/tex_sharedassets0.assets_232_Sand.png"));
        }

        /// <summary>与入库版逐字节一致的 README 文案（自愈重写用，防 git 噪音）。</summary>
        const string RefNormalReadmeText =
            "# WaterNormal2D_Ref.png —— 原作细节法线贴图（临时复用）\n" +
            "\n" +
            "**来源**：Sandcastle 原作发行包提取的 `WaterNormal2D`（UnityPy 回读序列化纹理，\n" +
            "提取件存 `external/sandcastle-ref/WaterNormal2D-extracted.png`，1024×1024 RGBA）。\n" +
            "\n" +
            "**通道编码**（与 shader `SAMPLE_TEXTURE2D(...).rg * 2 - 1` 的自解码约定对应）：\n" +
            "\n" +
            "- R/G = 法线 xy，0..255 → [-1, 1]\n" +
            "- B = 相位噪声/高度（当前 shader 未消费，保留原样）\n" +
            "- A = 1\n" +
            "\n" +
            "**导入口径**：linear 色空间（`sRGBTexture: 0`）、Repeat wrap、mip 开、**Default 纹理类型**——\n" +
            "不用 NormalMap 导入器（它会把通道重排成 DXT5nm 布局，破坏 RG/B 语义）、不压缩（保编码保真）。\n" +
            "\n" +
            "**临时性质**：本贴图是原作资产，经项目所有者授权**临时复用**于观感对标验证\n" +
            "（对比程序生成版 `WaterLabDetailNormal.asset` 与原作真实水纹的差异），之后会被替换掉。\n" +
            "`WaterLabSetup.LoadOrCreateDetailNormal()` 优先加载本贴图，找不到时回退程序生成版。\n" +
            "\n" +
            "**自愈说明**：`WaterLabSetup.BuildScene()` 重建材质目录时会把本文件连同 README 一并删除；\n" +
            "`WaterLabSetup.EnsureRefNormalAsset()` 会在缺失时从 `<工程根>/../external/sandcastle-ref/WaterNormal2D-extracted.png`\n" +
            "（可用环境变量 `PC3D_WATERLAB_REF_NORMAL_SRC` 覆盖）重新拷贝，并按固定 guid（见 .meta）恢复\n" +
            "导入设置，保证 git 与资产引用稳定。\n";

        /// <summary>
        /// 水底参照物（礁石/卵石/藻球）。**透明感靠它们传达**：只有沙纹时沙本身是亮的、
        /// 水体只轻微调色，观感就是「一片亮沙」而非「一汪清水」；参考游戏水底有礁石/珊瑚/
        /// 海草（深色、有轮廓、随焦散明暗），透过水看到它们才是透明的读法。
        /// 走不透明队列（Geometry）→ 水体 Transparent+10 采 _CameraOpaqueTexture 时已含它们。
        /// 确定性散布（固定种子），只落在真有水的高程区（地形低于静水面）。
        /// </summary>
        static void CreateBottomProps()
        {
            var detail = LoadOrCreateDetailNormal();
            var root = new GameObject("BottomProps");
            var rocks = new GameObject("Rocks").transform;
            var seagrass = new GameObject("Seagrass").transform;
            var coral = new GameObject("Coral").transform;
            rocks.SetParent(root.transform, false);
            seagrass.SetParent(root.transform, false);
            coral.SetParent(root.transform, false);
            var beach = new GameObject("BeachDetails");
            var beachGrass = new GameObject("BeachGrass").transform;
            var starfish = new GameObject("Starfish").transform;
            var gravel = new GameObject("BeachGravel").transform;
            beachGrass.SetParent(beach.transform, false);
            starfish.SetParent(beach.transform, false);
            gravel.SetParent(beach.transform, false);

            // 深色水底参照物是「透明感」的载体：参考图水底是密集的大丛海草/珊瑚，
            // 旧版 80 件 0.1–0.8 m 散石在画面里只占 <2% 像素，读不出水深。
            // 【色板依据】原作水底装饰是 Imposter 图集（实测不透明像均色）：
            // 橙褐脑珊瑚 (0.687,0.440,0.128)、粉珊瑚 (0.740,0.521,0.445)、
            // 灰绿生菜珊瑚 (0.610,0.560,0.431)、海带 (0.678,0.526,0.221)、
            // 海草 (0.36,0.46,0.21)（均为 sRGB 均值）。这里取 sRGB→线性值后
            // **再乘约 1.8–2 倍显示域补偿**：本仓水体按 5.6/1.94/0.35 每米消光，
            // 0.5 m 水深处红光只剩 ~2%、绿光 ~27%，不补的话任何色板都会读成暗蓝剪影。
            // 干滩的海星/滩草不做补偿（不在水下）。
            var rockMats = new[]
            {
                MakePropMaterial("WaterLab_Rock0", new Color(0.55f, 0.52f, 0.45f), detail),
                MakePropMaterial("WaterLab_Rock1", new Color(0.65f, 0.55f, 0.42f), detail),
                MakePropMaterial("WaterLab_Rock2", new Color(0.45f, 0.50f, 0.45f), detail),
            };
            var grassMats = new[]
            {
                MakePropMaterial("WaterLab_Grass0", new Color(0.22f, 0.38f, 0.08f), detail),
                MakePropMaterial("WaterLab_Grass1", new Color(0.30f, 0.44f, 0.10f), detail),
                MakePropMaterial("WaterLab_Grass2", new Color(0.52f, 0.45f, 0.12f), detail),
                MakePropMaterial("WaterLab_Grass3", new Color(0.34f, 0.26f, 0.08f), detail),
            };
            var coralMats = new[]
            {
                MakePropMaterial("WaterLab_Coral0", new Color(0.95f, 0.32f, 0.04f), detail),
                MakePropMaterial("WaterLab_Coral1", new Color(0.90f, 0.40f, 0.30f), detail),
                MakePropMaterial("WaterLab_Coral2", new Color(0.62f, 0.50f, 0.20f), detail),
            };
            var beachGrassMats = new[]
            {
                MakePropMaterial("WaterLab_BeachGrass0", new Color(0.12f, 0.17f, 0.05f), detail),
                MakePropMaterial("WaterLab_BeachGrass1", new Color(0.18f, 0.19f, 0.07f), detail),
            };
            var starfishMats = new[]
            {
                MakePropMaterial("WaterLab_Starfish0", new Color(0.55f, 0.18f, 0.08f), detail),
                MakePropMaterial("WaterLab_Starfish1", new Color(0.40f, 0.13f, 0.26f), detail),
            };
            // 原作沙面没有卵石 albedo 贴图：参考图里那些随机暗点是 **Gravel 粒子**
            // （实体小石网格，`Gravel` 材质 baseColor1/2 来自 sandParameters.gravelColor）。
            // 这里用同一套低模岩石网格缩到砾石尺寸，颜色取深灰/褐/浅贝壳三档。
            var gravelMats = new[]
            {
                MakePropMaterial("WaterLab_Gravel0", new Color(0.30f, 0.27f, 0.22f), detail),
                MakePropMaterial("WaterLab_Gravel1", new Color(0.42f, 0.36f, 0.28f), detail),
                MakePropMaterial("WaterLab_Gravel2", new Color(0.52f, 0.47f, 0.38f), detail),
                MakePropMaterial("WaterLab_Gravel3", new Color(0.72f, 0.67f, 0.56f), detail),
            };

            var rockShapes = new Mesh[6];
            for (int i = 0; i < rockShapes.Length; i++)
                rockShapes[i] = BuildRockMesh(20261018 + i * 977, 0.16f + 0.06f * i);
            var grassShapes = new Mesh[12];
            for (int i = 0; i < grassShapes.Length; i++)
                grassShapes[i] = BuildBladeCluster(20261020 + i * 613, 8, 14, 0.50f, 1.50f, 0.050f, 0.100f, 0.85f);
            var coralShapes = new Mesh[8];
            for (int i = 0; i < coralShapes.Length; i++)
                coralShapes[i] = BuildCoralMesh(20261120 + i * 431);
            var beachGrassShapes = new Mesh[8];
            for (int i = 0; i < beachGrassShapes.Length; i++)
                beachGrassShapes[i] = BuildBladeCluster(20261220 + i * 389, 5, 10, 0.25f, 0.60f, 0.020f, 0.040f, 0.25f);
            var starfishShapes = new Mesh[3];
            for (int i = 0; i < starfishShapes.Length; i++)
                starfishShapes[i] = BuildStarfishMesh(20261320 + i * 211);

            var rnd = new System.Random(20261020); // 确定性：每次重建同一散布

            // 【I7】散布域与干湿判据参数化：散布范围按域比例缩放（旧 64 m 取景设计值 ×s，
            // 回退模式 s=1 数值不变、ReplicaMode 收进 30 m 网格内）；干湿判据统一改成
            // 海平面相对量（SeaLevel 切 1.0 后旧的绝对高程判据会把整场读成"干的"）。
            float s = WaterLabRules.DomainSize / 64f;
            float zSpan = 28f * s;
            float sea = WaterLabRules.SeaLevel;

            // 参考原作：珊瑚/海草是成簇分布，不是均匀撒点。先取 16 个浅水簇心，
            // 70–75% 的实例落在簇心 0.4–2.4 m 内，其余仍全场点缀。
            var clusters = new Vector2[16];
            for (int i = 0; i < clusters.Length; i++)
            {
                clusters[i] = new Vector2(5f * s, 0f); // 兜底：近岸浅水
                for (int attempt = 0; attempt < 200; attempt++)
                {
                    // 簇心压在**水线附近的透亮带**（静态水深 0.02–0.30 m；高潮时
                    // 实际水深约 0.2–0.5 m）：水体的红光消光约 5.6/m，0.5 m 以下
                    // 任何色板都会退成暗蓝剪影。参考的彩色珊瑚/海草床也在这一带。
                    // 代价是低潮时部分簇会露出湿沙——参考的潮间带本来就有这种暴露。
                    float cx = Mathf.Lerp(5.0f * s, 7.2f * s, (float)rnd.NextDouble());
                    float cz = Mathf.Lerp(-zSpan, zSpan, (float)rnd.NextDouble());
                    float ch = WaterLabRules.TerrainHeight(cx, cz);
                    if (ch < sea - 0.02f && ch > sea - 0.30f)
                    {
                        clusters[i] = new Vector2(cx, cz);
                        break;
                    }
                }
            }

            int rockCount = 0;
            for (int attempt = 0; attempt < 2400 && rockCount < 90; attempt++)
            {
                // 礁石只撒在可见的浅/中水带；旧版撒到深水，62 块落在 1.5–2.4 m，
                // 水下只剩一团团黑斑。
                float x = Mathf.Lerp(-14f * s, 12f * s, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-zSpan, zSpan, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h > sea - 0.04f) continue; // 只落在有水处（地形低于海平面）

                // 尺寸按当地水深封顶：露出水面就不是「水底参照物」了
                float depth = sea - h;
                // 原作礁石多为 0.2–1.5 m 直径；旧上限 1.30 是半径，直径到 2.6 m，
                // 在画面里就是一团大黑块。缩到 0.75 半径并抬到半埋高度，露出礁石轮廓。
                float size = Mathf.Min(Mathf.Lerp(0.08f, 0.75f, (float)rnd.NextDouble()), depth * 0.80f);
                if (size < 0.08f) continue;
                var go = NewProp($"Rock_{rockCount}", rocks, rockShapes[rnd.Next(rockShapes.Length)],
                    rockMats[rnd.Next(rockMats.Length)]);
                go.transform.position = new Vector3(x, h + size * 0.35f, z);
                go.transform.rotation = Quaternion.Euler(
                    (float)rnd.NextDouble() * 25f - 12f,
                    (float)rnd.NextDouble() * 360f,
                    (float)rnd.NextDouble() * 25f - 12f);
                go.transform.localScale = new Vector3(size, size * Mathf.Lerp(0.55f, 1.0f, (float)rnd.NextDouble()), size);
                rockCount++;
            }

            int grassCount = 0;
            for (int attempt = 0; attempt < 3600 && grassCount < 150; attempt++)
            {
                float x, z;
                if (rnd.NextDouble() < 0.85)
                {
                    Vector2 c = clusters[rnd.Next(clusters.Length)];
                    float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float rad = Mathf.Lerp(0.4f, 2.4f, (float)rnd.NextDouble());
                    x = c.x + Mathf.Cos(ang) * rad;
                    z = c.y + Mathf.Sin(ang) * rad;
                }
                else
                {
                    x = Mathf.Lerp(-30f * s, 10f * s, (float)rnd.NextDouble());
                    z = Mathf.Lerp(-zSpan, zSpan, (float)rnd.NextDouble());
                }
                float h = WaterLabRules.TerrainHeight(x, z);
                float depth = sea - h;
                if (depth < 0.02f || depth > 0.80f) continue;
                // 主要分布在 0.05–0.45 m 的透亮带；更深只保留 20% 剪影点缀
                if ((depth < 0.05f || depth > 0.45f) && rnd.NextDouble() > 0.20) continue;

                float scale = Mathf.Lerp(0.70f, 1.30f, (float)rnd.NextDouble());
                scale = Mathf.Min(scale, Mathf.Max(0.35f, depth * 0.90f)); // 草尖基本不出水
                var go = NewProp($"Seagrass_{grassCount}", seagrass, grassShapes[rnd.Next(grassShapes.Length)],
                    grassMats[rnd.Next(grassMats.Length)]);
                go.transform.position = new Vector3(x, h - 0.02f, z);
                go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                go.transform.localScale = new Vector3(scale, scale, scale);
                grassCount++;
            }

            int coralCount = 0;
            for (int attempt = 0; attempt < 1200 && coralCount < 42; attempt++)
            {
                float x, z;
                if (rnd.NextDouble() < 0.85)
                {
                    Vector2 c = clusters[rnd.Next(clusters.Length)];
                    float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float rad = Mathf.Lerp(0.4f, 2.2f, (float)rnd.NextDouble());
                    x = c.x + Mathf.Cos(ang) * rad;
                    z = c.y + Mathf.Sin(ang) * rad;
                }
                else
                {
                    x = Mathf.Lerp(-30f * s, 8f * s, (float)rnd.NextDouble());
                    z = Mathf.Lerp(-zSpan, zSpan, (float)rnd.NextDouble());
                }
                float h = WaterLabRules.TerrainHeight(x, z);
                float depth = sea - h;
                if (depth < 0.02f || depth > 0.70f) continue;
                // 主要分布在 0.05–0.40 m 的透亮带；更深只保留 15% 剪影点缀
                if ((depth < 0.05f || depth > 0.40f) && rnd.NextDouble() > 0.15) continue;

                float scale = Mathf.Lerp(0.55f, 1.05f, (float)rnd.NextDouble());
                scale = Mathf.Min(scale, Mathf.Max(0.35f, depth * 0.90f)); // 珊瑚枝基本不出水
                var go = NewProp($"Coral_{coralCount}", coral, coralShapes[rnd.Next(coralShapes.Length)],
                    coralMats[rnd.Next(coralMats.Length)]);
                go.transform.position = new Vector3(x, h - 0.03f, z);
                go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                go.transform.localScale = new Vector3(scale, scale, scale);
                coralCount++;
            }

            int beachGrassCount = 0;
            for (int attempt = 0; attempt < 2000 && beachGrassCount < 160; attempt++)
            {
                float x = Mathf.Lerp(11f * s, 30f * s, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-zSpan, zSpan, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h < sea + 0.12f) continue;
                float scale = Mathf.Lerp(0.60f, 1.30f, (float)rnd.NextDouble());
                var go = NewProp($"BeachGrass_{beachGrassCount}", beachGrass,
                    beachGrassShapes[rnd.Next(beachGrassShapes.Length)],
                    beachGrassMats[rnd.Next(beachGrassMats.Length)]);
                go.transform.position = new Vector3(x, h - 0.02f, z);
                go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                go.transform.localScale = new Vector3(scale, scale, scale);
                beachGrassCount++;
            }

            int starfishCount = 0;
            for (int attempt = 0; attempt < 500 && starfishCount < 30; attempt++)
            {
                float x = Mathf.Lerp(9f * s, 30f * s, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-zSpan, zSpan, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h < sea + 0.03f) continue;
                float scale = Mathf.Lerp(0.70f, 1.30f, (float)rnd.NextDouble());
                var go = NewProp($"Starfish_{starfishCount}", starfish,
                    starfishShapes[rnd.Next(starfishShapes.Length)],
                    starfishMats[rnd.Next(starfishMats.Length)]);
                go.transform.position = new Vector3(x, h + 0.015f, z);
                go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                go.transform.localScale = new Vector3(scale, scale, scale);
                starfishCount++;
            }

            // 干滩砾石：原作是 3D 粒子（不是 shader 点阵）。按参考图的稀疏程度
            // 撒在 h>0.05 的干沙上，半径 2–6 cm；少量浅色贝壳用扁平的 gravelMats[3]。
            int gravelCount = 0;
            for (int attempt = 0; attempt < 3000 && gravelCount < 320; attempt++)
            {
                float x = Mathf.Lerp(10f * s, 30f * s, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-zSpan, zSpan, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h < sea + 0.05f) continue; // 只落在干沙/后滨，不进水线浮沫区
                float size = Mathf.Lerp(0.02f, 0.06f, (float)rnd.NextDouble());
                bool shell = rnd.NextDouble() < 0.08;
                var go = NewProp($"Gravel_{gravelCount}", gravel,
                    rockShapes[rnd.Next(rockShapes.Length)],
                    shell ? gravelMats[3] : gravelMats[rnd.Next(3)]);
                go.transform.position = new Vector3(x, h + size * 0.20f, z);
                go.transform.rotation = Quaternion.Euler(
                    (float)rnd.NextDouble() * 360f,
                    (float)rnd.NextDouble() * 360f,
                    (float)rnd.NextDouble() * 360f);
                go.transform.localScale = new Vector3(
                    shell ? size * 1.5f : size,
                    size * (shell ? 0.22f : Mathf.Lerp(0.55f, 1.0f, (float)rnd.NextDouble())),
                    shell ? size * 1.1f : size);
                gravelCount++;
            }

            Debug.Log($"[WaterLabSetup] 水底参照物：礁石 {rockCount} / 海草 {grassCount} 丛 / 珊瑚 {coralCount} 丛；" +
                      $"滩面草簇 {beachGrassCount} / 海星 {starfishCount} / 砾石 {gravelCount}");
        }

        static Material MakePropMaterial(string name, Color color, Texture2D detail)
        {
            var m = LoadOrCreateMaterial(name, ShaderPathProp);
            m.SetColor("_BaseColor", color);
            m.SetTexture("_WaterLabDetailNormal", detail);
            m.SetFloat("_LightScale", LightScale);
            // 调试口：PC3D_WATERLAB_PROP_NOSHADOW=1 时关闭 prop 阴影采样，
            // 用来分辨“自阴影导致暗团”与“水柱吸收导致暗团”。
            m.SetFloat("_ShadowStrength",
                System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_PROP_NOSHADOW") == "1" ? 0f : 1f);
            m.SetColor("_AmbientColor", AmbientColor);
            m.SetFloat("_CausticScale", CausticScale);
            m.SetFloat("_CausticGain", CausticGain);
            return m;
        }

        static GameObject NewProp(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>低模岩石：正二十面体（20 面）顶点径向抖动 + **平面着色**。
        /// 平面法线显式按「面心朝外」定向并同步翻转绕序 → 不依赖灵感来源的顶点序，
        /// 背面剔除与法线都不会翻（低模岩石的棱面观感也由此而来）。</summary>
        static Mesh BuildRockMesh(int seed, float roughness)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new[]
            {
                new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
                new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
                new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
            };
            var tri = new[]
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            var rnd = new System.Random(seed);
            var dir = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                dir[i] = v[i].normalized;
                dir[i] *= 1f + (float)(rnd.NextDouble() * 2.0 - 1.0) * roughness;
            }

            var verts = new Vector3[tri.Length];
            var norms = new Vector3[tri.Length];
            var tris = new int[tri.Length];
            for (int f = 0; f < tri.Length; f += 3)
            {
                Vector3 a = dir[tri[f]], b = dir[tri[f + 1]], c = dir[tri[f + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 center = (a + b + c) / 3f;
                if (Vector3.Dot(n, center) < 0f) { (b, c) = (c, b); n = -n; } // 面心朝外
                verts[f] = a; verts[f + 1] = b; verts[f + 2] = c;
                n.Normalize();
                norms[f] = n; norms[f + 1] = n; norms[f + 2] = n;
                tris[f] = f; tris[f + 1] = f + 1; tris[f + 2] = f + 2;
            }

            var mesh = new Mesh { name = "WaterLabRock" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>海草/滩草簇：一丛若干片锥形草叶（三棱柱两段，低模平面着色）。
        /// 单丛一个 mesh，避免每片草叶一个 GameObject 的 draw call 爆炸。</summary>
        static Mesh BuildBladeCluster(int seed, int minBlades, int maxBlades,
            float minHeight, float maxHeight, float minWidth, float maxWidth, float spread)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            int blades = rnd.Next(minBlades, maxBlades + 1);
            for (int b = 0; b < blades; b++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Lerp(0.02f, spread, (float)rnd.NextDouble());
                float bx = Mathf.Cos(ang) * rad;
                float bz = Mathf.Sin(ang) * rad;
                float h = Mathf.Lerp(minHeight, maxHeight, (float)rnd.NextDouble());
                float w = Mathf.Lerp(minWidth, maxWidth, (float)rnd.NextDouble());
                float lean = 0.12f + 0.30f * (float)rnd.NextDouble();
                Vector3 p0 = new Vector3(bx, 0f, bz);
                Vector3 p1 = p0 + new Vector3(Mathf.Cos(ang) * lean * 0.45f, h * 0.55f, Mathf.Sin(ang) * lean * 0.45f);
                Vector3 p2 = p0 + new Vector3(Mathf.Cos(ang) * lean, h, Mathf.Sin(ang) * lean);
                AddTaperedPrism(verts, norms, tris, p0, p1, p2, w, w * 0.55f, w * 0.10f, 3);
            }
            return FinishMesh("WaterLabBladeCluster", verts, norms, tris);
        }

        /// <summary>珊瑚丛：若干向上/向外分叉的四棱锥枝，两段收尖。</summary>
        static Mesh BuildCoralMesh(int seed)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            int branches = rnd.Next(5, 9);
            for (int b = 0; b < branches; b++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float len = Mathf.Lerp(0.40f, 0.95f, (float)rnd.NextDouble());
                float outward = Mathf.Lerp(0.12f, 0.45f, (float)rnd.NextDouble());
                Vector3 p0 = new Vector3(Mathf.Cos(ang) * 0.05f, -0.02f, Mathf.Sin(ang) * 0.05f);
                Vector3 p1 = p0 + new Vector3(Mathf.Cos(ang) * outward * 0.55f, len * 0.58f, Mathf.Sin(ang) * outward * 0.55f);
                Vector3 p2 = p1 + new Vector3(Mathf.Cos(ang) * outward * 0.45f + ((float)rnd.NextDouble() - 0.5f) * 0.10f,
                                             len * 0.42f,
                                             Mathf.Sin(ang) * outward * 0.45f + ((float)rnd.NextDouble() - 0.5f) * 0.10f);
                AddTaperedPrism(verts, norms, tris, p0, p1, p2, 0.085f, 0.055f, 0.018f, 4);
            }
            return FinishMesh("WaterLabCoral", verts, norms, tris);
        }

        /// <summary>海星：五角星薄片（顶面扇形 10 三角），平贴沙面。</summary>
        static Mesh BuildStarfishMesh(int seed)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float outer = Mathf.Lerp(0.16f, 0.22f, (float)rnd.NextDouble());
            float inner = outer * 0.42f;
            Vector3 center = new Vector3(0f, 0.025f, 0f);
            var rim = new Vector3[10];
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5f;
                float r = (i % 2 == 0) ? outer : inner;
                rim[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            for (int i = 0; i < 10; i++)
                AddTriangle(verts, norms, tris, center, rim[i], rim[(i + 1) % 10], center - Vector3.up);
            return FinishMesh("WaterLabStarfish", verts, norms, tris);
        }

        /// <summary>把两段锥形棱柱（p0→p1→p2，半径 r0/r1/r2）写进共享缓冲。
        /// 面法线按「背离轴心」定向，背面剔除在任意视角都至少留一面。</summary>
        static void AddTaperedPrism(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            Vector3 p0, Vector3 p1, Vector3 p2, float r0, float r1, float r2, int sides)
        {
            Vector3[] ring0 = Ring(p0, p1, r0, sides);
            Vector3[] ring1 = Ring(p1, p2, r1, sides);
            Vector3[] ring2 = Ring(p2, p2 + (p2 - p1), r2, sides);
            AddRingBand(verts, norms, tris, ring0, ring1);
            AddRingBand(verts, norms, tris, ring1, ring2);
        }

        static Vector3[] Ring(Vector3 a, Vector3 b, float radius, int sides)
        {
            Vector3 axis = b - a;
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.up;
            axis.Normalize();
            Vector3 right = Vector3.Cross(axis, Vector3.forward);
            if (right.sqrMagnitude < 1e-5f) right = Vector3.Cross(axis, Vector3.right);
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, axis).normalized;
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float ang = i * Mathf.PI * 2f / sides;
                ring[i] = a + (right * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)) * radius;
            }
            return ring;
        }

        static void AddRingBand(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            Vector3[] lo, Vector3[] hi)
        {
            int sides = lo.Length;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = lo[i], b = lo[(i + 1) % sides];
                Vector3 c = hi[(i + 1) % sides], d = hi[i];
                Vector3 mid = (a + b + c + d) * 0.25f;
                AddTriangle(verts, norms, tris, a, b, c, mid);
                AddTriangle(verts, norms, tris, a, c, d, mid);
            }
        }

        static void AddTriangle(List<Vector3> verts, List<Vector3> norms, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 outwardFrom)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            if (Vector3.Dot(n, (a + b + c) / 3f - outwardFrom) < 0f)
            {
                (b, c) = (c, b);
                n = -n;
            }
            int i0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        }

        static Mesh FinishMesh(string name, List<Vector3> verts, List<Vector3> norms, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void CreateWaterSurface()
        {
            var mesh = new Mesh { name = "WaterLabSurface" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            int verts = SurfaceSegments + 1;
            float step = WaterLabRules.DomainSize / SurfaceSegments;

            var vertices = new Vector3[verts * verts];
            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    vertices[x + z * verts] = new Vector3(
                        -WaterLabRules.DomainSize * 0.5f + x * step, 0f,
                        -WaterLabRules.DomainSize * 0.5f + z * step);

            var triangles = new int[SurfaceSegments * SurfaceSegments * 6];
            int t = 0;
            for (int z = 0; z < SurfaceSegments; z++)
            {
                for (int x = 0; x < SurfaceSegments; x++)
                {
                    int i0 = x + z * verts;
                    int i1 = i0 + 1;
                    int i2 = i0 + verts;
                    int i3 = i2 + 1;
                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            var go = new GameObject("WaterSurface");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = LoadOrCreateMaterial("WaterLab_Ocean", ShaderPathOcean);
            // 吸收 σ 显式下发（同 LightScale 的理由：已存在的 .mat 不会跟着 shader 默认值走）。
            // 值 = 对参考头图**三锚点同时命中**解出：本仓 dist=0.2/0.5/≥2 m 分别落在参考的
            // 浅滩 (157,200,171) / 中带 (74,153,161) / 最深带 (16,70,119) 上
            // → 有效 (5.6,1.94,0.35)/m ÷ 8.65617（tint 域）。通道比值取自参考自身的
            // T=(0.0036,0.142,0.703)：σ_R:σ_G:σ_B = 15.9:5.52:1。
            // 【SetVector，不是 SetColor】Linear 工程会把 Color 属性按 sRGB→线性再压一次，
            // σ 被压成 (0.365,0.039,0.0033)，绿色消光只剩 1/5.7 → 深水发青绿。
            renderer.sharedMaterial.SetVector("_Absorption",
                new Vector4(0.6468f, 0.2241f, 0.0404f, 1f));
            // 天空反射（原作 PS 步骤 8 缺件）：线性/HDR 域环境色，见上面对应常量注释。
            renderer.sharedMaterial.SetVector("_SkyReflectionColor", SkyReflectionColor);
            renderer.sharedMaterial.SetFloat("_SkyReflectionStrength", SkyReflectionStrength);
            // 散射显示域强度：原作 HDR+tonemap 的文件值在 LDR 直出链里过强，见 ScatterStrength 注释。
            renderer.sharedMaterial.SetFloat("_ScatterStrength", ScatterStrength);
            // 深水 GI 兜底（体素 Gi 的显示域近似）：见 GIFillColor 常量注释。
            renderer.sharedMaterial.SetVector("_GIFillColor", GIFillColor);
            renderer.sharedMaterial.SetFloat("_GIFillStrength", GIFillStrength);
            // 原作 Lit_Water PS 的 discard 阈值是 1e-5（逐顶点可见水深），没有 6 mm 档；
            // 残留水膜应由 VS 的逐顶点可见水深解决，不能靠抬高裁剪阈值掩盖。
            renderer.sharedMaterial.SetFloat("_DryClip", 0.00001f);
            // 【一次性探针】PC3D_WATERLAB_MAT_PROBE=1：把水色乘子强制纯红、σ 提到 5/m 均匀。
            // 若水面在画 → 整幅被 ×T 染红（dist≥0.3 即近黑红）；若不在画（被裁/未绘）→ 看到的是
            // 沙面本色。用于分辨「水没渲染」与「水渲染了但颜色不对」这两类完全不同的成因。
            if (System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_MAT_PROBE") == "1")
            {
                renderer.sharedMaterial.SetColor("_WaterTint", new Color(1f, 0f, 0f));
                renderer.sharedMaterial.SetVector("_Absorption", new Vector4(0.5776f, 0.5776f, 0.5776f, 1f));
                Debug.Log("[WaterLabSetup] 探针：水色乘子=纯红，σ=5/m 均匀（看水面是否在画）");
            }
            // 原作水面不投影（Lit_Water blob 实证无 ShadowCaster pass）；水底波影
            // 由沙侧「阴影 × exp(-0.721×水深)」承担——开投影反而整网格盖灰影（实拍实证）
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterial.SetTexture("_WaterLabDetailNormal", LoadOrCreateDetailNormal());
        }

        static void CreateSimulation(Camera renderCamera)
        {
            var go = new GameObject("WaterLabSimulation");
            var sim = go.AddComponent<WaterLabSimulation>();
            sim.computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            sim.renderCamera = renderCamera; // 白水视锥剔除的平面来源（取景相机）
            // ReplicaMode 接线（I7/W1）：TextAsset 引用随场景序列化，构建后同样有效；
            // 模拟 Awake 里加载校验，缺资产 = 回退程序剖面（判定 = 资产可加载）。
            // 权重场缺失不回退 ReplicaMode，只降级回退代理场（模拟侧显式报错）。
            sim.replicaGroundSnapshot = AssetDatabase.LoadAssetAtPath<TextAsset>(ReplicaGroundPath);
            sim.replicaWaterSnapshot = AssetDatabase.LoadAssetAtPath<TextAsset>(ReplicaWaterPath);
            sim.replicaWeights = AssetDatabase.LoadAssetAtPath<TextAsset>(ReplicaWeightsPath);
            if (sim.computeShader == null)
                Debug.LogError($"[WaterLabSetup] 找不到 compute：{ComputePath}（检查文件是否入库/被导入）");
            Debug.Log($"[WaterLabSetup] 模拟驱动：ReplicaMode={(sim.ReplicaMode ? "开" : "关（程序剖面）")}（Awake 后以模拟侧加载为准）");
        }

        /// <summary>白水出画 Feature 幂等注入现役渲染器资产（Balanced+Performant，
        /// WaterLab 取景跟随质量档）。实例以子资产形式存进 .asset（URP 惯例），
        /// 清单/映射同步补位；白水材质随注（已有实例缺材质也补）。</summary>
        /// <summary>
        /// I4 后处理（F 流定案：原作实际生效仅 DoF + Vignette，无 tonemap/bloom/曝光）。
        /// DoF{focus 37.49（autofocus 每帧 raycast 回写的静止值——机制移植 TODO）、
        /// aperture 1.1、focalLength 106mm、kernel VeryLarge→URP Bokeh 高档}；
        /// Vignette{intensity 0.296 其余默认}。URP postProcessing 开关在相机上启用。
        /// </summary>
        static void EnsurePostProcess(Camera cam)
        {
            // batchmode 下 URP 相机组件不是立即自动挂——用扩展方法（缺则加）
            UnityEngine.Rendering.Universal.CameraExtensions
                .GetUniversalAdditionalCameraData(cam).renderPostProcessing = true;

            var volGo = new GameObject("ReplicaPostVolume");
            volGo.layer = 0;
            var vol = volGo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            profile.name = "ReplicaPost";
            var vig = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vig.intensity.Override(0.296f);   // F 流：唯一 override 项
            var dof = profile.Add<UnityEngine.Rendering.Universal.DepthOfField>(true);
            dof.mode.Override(UnityEngine.Rendering.Universal.DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(37.49f);
            dof.aperture.Override(1.1f);
            dof.focalLength.Override(106f);   // VeryLarge 档的焦段口径（F 报告）
            vol.sharedProfile = profile;
            Debug.Log("[WaterLabSetup] I4 后处理：Vignette 0.296 + DoF(37.49/1.1/106) 已挂");
        }

        // ====================================================================
        // I5b 边框/天空挂接（H 流真值 reports/H-borders-sky.md + I5a shader 件
        // d11ed7c0）。一切在代码装配里做（BuildScene 是 NewScene 全重建，不手摆）。
        // ====================================================================

        /// <summary>边框盒世界高度（B 流：沙盘 bounds center(0,5,0) size 30×10×30
        /// ——本地立方体高 1 × 缩放 10 = 10 m）。</summary>
        const float BordersHeight = 10f;

        /// <summary>
        /// FrontWater 剖面带的吸收 σ：R 抓帧运行时真值 cb0[132]（tint 域，
        /// 有效消光 6σ/m=(5.958,1.052,0.335)，交接§3 R 行定案）。与
        /// <see cref="WaterLabReplicaGlobals"/> 的 _WaterLabSigma **单源同值**
        ///（Globals 那份管水面、这份管 Borders 剖面带）——抓帧对账若改两处同改。
        /// A 流 σ≡0 旧结论已被 R 推翻，勿回退。
        /// </summary>
        static readonly Vector4 ReplicaSigma = new Vector4(0.9930291f, 0.1753813f, 0.0557871f, 0f);

        /// <summary>ReplicaBorders 三模式（shader _Mode 的 Enum 同表，三选一）。</summary>
        enum BordersMode
        {
            Back = 1,
            FrontGround = 2,
            FrontWater = 3,
        }

        /// <summary>
        /// I5b 边框挂接：Back/FrontGround/FrontWater 三 renderer 共用 16 顶点自定义
        /// 立方体（sa0 pid378 复刻），Transform 恒等（H 报告 §1.1）、缩放 = 域尺寸
        /// (30,10,30)——视觉分化全在 shader（Cull/clip/keyword），几何契约见
        /// ReplicaBorders.shader 文件头。Frame（Lit/Base 白模）件就位但不可见
        /// （Transform 位置未 dump，TODO 见下）。原作场景树 Beach/Borders{四件}
        /// → 本仓 "Borders" 根同名子物体。
        /// </summary>
        static void EnsureBorders()
        {
            var root = new GameObject("Borders");
            root.transform.position = Vector3.zero; // 域中心；立方体 XZ ±0.5 → 世界 ±15
            var cube = BuildBordersCubeMesh();

            MakeBordersObject("Back", root.transform, cube,
                MakeBordersMaterial("WaterLab_BordersBack", BordersMode.Back));
            MakeBordersObject("FrontGround", root.transform, cube,
                MakeBordersMaterial("WaterLab_BordersFrontGround", BordersMode.FrontGround));
            MakeBordersObject("FrontWater", root.transform, cube,
                MakeBordersMaterial("WaterLab_BordersFrontWater", BordersMode.FrontWater));

            // Frame：Lit/Base 白模（非 Borders 三件套成员，H 报告 §1.1），平面网格
            // 1.23×1.23（sa0 pid398）。Transform 位置/朝向未 dump（H 的
            // borders-scene.json Borders 树为空、§4 也标"可后置"）→
            // TODO(I5b 遗留)：位置待 H 补 dump 或 L2 抓帧回读；先建件挂边框组下、
            // renderer 关闭（无 draw call 完全不可见），材质就位后只差 Transform。
            var frame = MakeBordersObject("Frame", root.transform, BuildFrameMesh(),
                LoadOrCreateMaterial("WaterLab_Frame", ShaderPathFrame));
            var frameRenderer = frame.GetComponent<MeshRenderer>();
            frameRenderer.enabled = false;

            Debug.Log($"[WaterLabSetup] I5b 边框：Back/FrontGround/FrontWater 三 renderer " +
                $"恒等 Transform scale({WaterLabRules.DomainSize},{BordersHeight},{WaterLabRules.DomainSize})，" +
                $"Frame 白模就位（disabled，位置 TODO）");
        }

        /// <summary>边框件：恒等 Transform（position/rotation 默认零值），只设缩放。</summary>
        static GameObject MakeBordersObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(
                WaterLabRules.DomainSize, BordersHeight, WaterLabRules.DomainSize);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// 按模式建边框材质：keyword 三选一（shader_feature_local，面板 Toggle 双轨
        /// 同步）+ 各模式真值。值一律 Vector 下发（Linear 工程 Color 属性会被再 gamma
        /// 一次，交接§6 陷阱），重建先删后建保证 shader 默认值生效、显式下发防漂移。
        /// </summary>
        static Material MakeBordersMaterial(string name, BordersMode mode)
        {
            var m = LoadOrCreateMaterial(name, ShaderPathBorders);
            const string KwBack = "_REPLICA_MODE_BACK";
            const string KwGround = "_REPLICA_MODE_FRONTGROUND";
            const string KwWater = "_REPLICA_MODE_FRONTWATER";
            m.SetFloat("_Mode", (float)mode);
            m.SetFloat("_ModeBack", mode == BordersMode.Back ? 1f : 0f);
            m.SetFloat("_ModeFrontGround", mode == BordersMode.FrontGround ? 1f : 0f);
            m.SetFloat("_ModeFrontWater", mode == BordersMode.FrontWater ? 1f : 0f);
            m.DisableKeyword(KwBack);
            m.DisableKeyword(KwGround);
            m.DisableKeyword(KwWater);
            switch (mode)
            {
                case BordersMode.Back:
                    // 纯白内壁剪影（PS 恒量 (1,1,1,1)，无参数）——只勾模式。
                    m.EnableKeyword(KwBack);
                    break;
                case BordersMode.FrontGround:
                    m.EnableKeyword(KwGround);
                    // Strata 层理真图（sa0 pid212 仅 R）+ Carbonate 预设沙色——与
                    // ApplySandReplicaValues 同源（cb0[127]/[128] 运行时 = 预设
                    // color/color2；H 报告 §1.3：Borders 材质序列化色是惰性镜像勿用）。
                    // _StrataLevelOffset 留 shader 默认 1.0（cb0[113].x 疑水位，
                    // TODO 对账注释在 shader）。贴图缺失不阻断：shader 默认 gray=0.5。
                    m.SetTexture("_StrataTex", AssetDatabase.LoadAssetAtPath<Texture2D>(StrataRefPath));
                    m.SetVector("_SandColor", SandColorDry);
                    m.SetVector("_SandColor2", SandColorThird);
                    break;
                case BordersMode.FrontWater:
                    m.EnableKeyword(KwWater);
                    // 剖面带 exp2(−8.65617·σ·d)×waterColor：σ = R 抓帧运行时真值
                    // （ReplicaSigma，同源 WaterLabReplicaGlobals._WaterLabSigma），
                    // 水色 = Water 材质序列化值（同源 ApplySandReplicaValues 的
                    // _WaterColor 与 ReplicaGlobals.WaterColor 三处一字不改）。
                    m.SetVector("_Sigma", ReplicaSigma);
                    m.SetVector("_WaterColor", SandWaterColor);
                    break;
            }
            return m;
        }

        /// <summary>
        /// 沙盘边框自定义立方体（sa0 pid378 复刻）：16 顶点 24 索引 = **四面侧墙**
        /// 每面独立 4 顶点（相邻面不共享——同一 worldXZ 落在不同墙面要携带不同的
        /// 域列 UV），无顶/底面（原作 8 三角恰好铺满四侧墙；边框只露竖剖面）。
        /// 局部 AABB：底 y=0、center(0,0.5,0) extent(0.5,0.5,0.5)。
        /// 【UV=域列坐标（非表面参数化）】u=x+0.5、v=z+0.5（局部）→ 缩放后即
        /// 域 (X→u, Z→v)（B 流轴向 row=Z/col=X）——每个顶点携带自己水柱的域地址；
        /// 同一面竖墙的竖边两顶点 UV 相同（整列同柱），三角形内部插值竖向恒定。
        /// 法线按面显式写出（顶点不共享，无 RecalculateNormals 歧义）。
        /// </summary>
        static Mesh BuildBordersCubeMesh()
        {
            var mesh = new Mesh { name = "WaterLabBordersCube" };
            // 面 1 Front（z=−0.5，法线朝 −Z，出图相机侧）：竖剖面看的是域前缘水柱列
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),   // v0 v1
                new Vector3(0.5f, 1f, -0.5f), new Vector3(-0.5f, 1f, -0.5f),   // v2 v3
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),     // v4 v5
                new Vector3(0.5f, 1f, 0.5f), new Vector3(-0.5f, 1f, 0.5f),     // v6 v7
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),   // v8 v9
                new Vector3(-0.5f, 1f, 0.5f), new Vector3(-0.5f, 1f, -0.5f),   // v10 v11
                new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f),     // v12 v13
                new Vector3(0.5f, 1f, 0.5f), new Vector3(0.5f, 1f, -0.5f),     // v14 v15
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),   // v0 v1（Front：u=X 列）
                new Vector2(1f, 0f), new Vector2(0f, 0f),   // v2 v3（竖边同柱 → 同 UV）
                new Vector2(0f, 1f), new Vector2(1f, 1f),   // v4 v5（Back：v=Z=1）
                new Vector2(1f, 1f), new Vector2(0f, 1f),   // v6 v7
                new Vector2(0f, 0f), new Vector2(0f, 1f),   // v8 v9（Left：u=0，v=Z 列）
                new Vector2(0f, 1f), new Vector2(0f, 0f),   // v10 v11
                new Vector2(1f, 0f), new Vector2(1f, 1f),   // v12 v13（Right：u=1）
                new Vector2(1f, 1f), new Vector2(1f, 0f),   // v14 v15
            };
            var nFront = new Vector3(0f, 0f, -1f);
            var nBack = new Vector3(0f, 0f, 1f);
            var nLeft = new Vector3(-1f, 0f, 0f);
            var nRight = new Vector3(1f, 0f, 0f);
            mesh.normals = new[]
            {
                nFront, nFront, nFront, nFront,
                nBack, nBack, nBack, nBack,
                nLeft, nLeft, nLeft, nLeft,
                nRight, nRight, nRight, nRight,
            };
            // 绕序 = 正面朝外（法线 = Cross(b−a, c−a)，逐三角核对过）。
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,       // Front（外法线 −Z：Cull Back 时朝相机可见）
                4, 5, 6, 4, 6, 7,       // Back
                8, 9, 10, 8, 10, 11,    // Left
                12, 14, 13, 12, 15, 14, // Right
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// I5b 天空挂接：RenderSettings.skybox = ReplicaSkybox 材质（原作 level0
        /// RenderSettings.m_SkyboxMaterial 同位，H 报告 §3：背景是程序化 Skybox
        /// shader，不是纯色）。t=1/3 菜单时刻真值显式下发（同 ApplySandReplicaValues
        /// 的理由：防 .mat 锁旧值）。
        /// 【相机 clearFlags 不动】任务口径 = 保持 SolidColor 原值；URP 实际绘制条件
        /// 是 clearFlags==Skybox 且 RenderSettings.skybox 非 null——本仓 LabCamera 未
        /// 显式设过 clearFlags（Unity 默认 Skybox）。两种情形的行为差异作为出图对账点
        /// 记 TODO，本方法不碰相机域。
        /// 【I5a 遗留 TODO 注释链】cb0[122] 天空基色（_SkyBaseTint）默认 (1,1,1) 待
        /// 抓帧；Skybox 绘制序差异已把原作 ZTest Always 收口为 LEqual（shader 文件头）。
        /// </summary>
        static void EnsureSkybox()
        {
            var m = LoadOrCreateMaterial("WaterLab_Skybox", ShaderPathSkybox);
            // t=1/3 Morning（menu-state.json，DayCycler Evaluate 真值；与 CreateLighting
            // 的灯色/环境光同值——灯走 Light.color（sRGB 语义）、这里走 Vector（物理量）
            // 不冲突）。
            m.SetVector("_SkySunColor", new Vector4(1f, 0.940429f, 0.872353f, 1f));
            m.SetVector("_SkyAmbient", new Vector4(0.698039f, 0.564706f, 0.569923f, 1f));
            // cb0[122] 运行时专用待抓帧（shader 文件头 TODO 1）；边界半宽按 B 流
            // 沙盘 30×30 的一半（shader 文件头 TODO 2）。
            m.SetVector("_SkyBaseTint", new Vector4(1f, 1f, 1f, 1f));
            m.SetVector("_SkyBorderSize", new Vector4(15f, 15f, 0f, 0f));
            RenderSettings.skybox = m;
            Debug.Log("[WaterLabSetup] I5b 天空：RenderSettings.skybox = ReplicaSkybox" +
                "（t=1/3 真值；相机 clearFlags 未动）");
        }

        /// <summary>Frame 白模网格（sa0 pid398 复刻）：1.23×1.23 平面、中心原点。
        /// 朝向/位置未 dump（见 EnsureBorders 的 Frame TODO），先按 XY 竖面（法线 −Z）
        /// 建——disabled 状态下不参与渲染，只保件就位。</summary>
        static Mesh BuildFrameMesh()
        {
            const float half = 1.23f * 0.5f;
            var mesh = new Mesh { name = "WaterLabFrameQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-half, -half, 0f), new Vector3(half, -half, 0f),
                new Vector3(half, half, 0f), new Vector3(-half, half, 0f),
            };
            var n = new Vector3(0f, 0f, -1f);
            mesh.normals = new[] { n, n, n, n };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; // 正面朝 −Z（绕序已核对）
            mesh.RecalculateBounds();
            return mesh;
        }

        static void EnsureWhitewaterFeature()
        {
            foreach (var path in new[]
            {
                "Assets/Settings/URP/PC_Balanced_Renderer.asset",
                "Assets/Settings/URP/PC_Performant_Renderer.asset",
            })
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null)
                {
                    Debug.LogError($"[WaterLabSetup] 找不到渲染器资产：{path}");
                    continue;
                }

                bool found = false;
                foreach (var existing in data.rendererFeatures.OfType<WaterLabRefractionFeature>())
                {
                    found = true;
                    // 材质后到：已有 Feature 但没挂白水材质（如早前轮次注入的）也补上
                    if (existing.WhitewaterMaterial == null)
                    {
                        existing.WhitewaterMaterial = LoadOrCreateMaterial("WaterLab_Whitewater", ShaderPathWhitewater);
                        EditorUtility.SetDirty(existing);
                    }
                }

                if (found)
                    continue;

                var feature = ScriptableObject.CreateInstance<WaterLabRefractionFeature>();
                feature.name = "WaterLabRefraction";
                feature.WhitewaterMaterial = LoadOrCreateMaterial("WaterLab_Whitewater", ShaderPathWhitewater);
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.ImportAsset(path); // 子资产登记后才有 localId（map 语义 = localId）

                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                list.GetArrayElementAtIndex(idx).objectReferenceValue = feature;
                var map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue =
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId)
                        ? localId : 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log($"[WaterLabSetup] 已挂真折射 Feature：{path}");
            }

            AssetDatabase.SaveAssets();
        }

        static Material LoadOrCreateMaterial(string name, string shaderPath)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                Debug.LogError($"[WaterLabSetup] 找不到 shader：{shaderPath}");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader != shader)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
            // 新文件夹要先被 AssetDatabase 认领，紧接着的 CreateAsset 才不会失败
            AssetDatabase.Refresh();
        }
    }
}
