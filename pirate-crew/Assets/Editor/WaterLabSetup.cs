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

        /// <summary>地形渲染网格每轴段数（0.117 m/顶点）。模拟地形场是 1024²（5.86 cm，
        /// 原作 groundResolution 同水格），渲染网格取一半分辨率：再往上翻倍只是把
        /// 场景资产从 ~10 MB 抬到 ~50 MB，画面收益远小于模拟场本身。</summary>
        const int TerrainSegments = 512;

        /// <summary>水面渲染网格每轴段数（0.117 m/顶点）。模拟高度场 1024²（5.86 cm），
        /// 渲染顶点取一半——前缘仍有 2 顶点/水格间距，且不至于把 1M 顶点塞进场景资产。</summary>
        const int SurfaceSegments = 512;

        [MenuItem("Tools/PirateCrew/WaterLab/Build Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 材质目录先删后建：shader 默认值改动只影响新建材质，旧 .mat 会锁死旧参数
            //（序列化陷阱）。重建语义 = 全量重装配。
            AssetDatabase.DeleteAsset(MaterialFolder);
            EnsureFolder(MaterialFolder); // 材质目录先建（装配过程中就要写材质资产）

            CreateLighting();
            var camera = CreateCamera();
            CreateTerrain();
            CreateBottomProps(); // 水底参照物：透明感的载体（不透明队列，被水体折射）
            CreateWaterSurface();
            CreateSimulation(camera); // 相机随注：白水逐粒视锥剔除要用它的 6 平面
            EnsureWhitewaterFeature();

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
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.0f;
            // 参考的礁石/珊瑚投影是柔和接触影，不是硬黑块；0.75 配合 Soft shadows，
            // 避免低模 prop 自阴影在暗部读成第二层黑团。
            light.shadowStrength = 0.75f;
            light.color = new Color(1.0f, 0.95f, 0.85f);
            // 太阳放在相机对面、抬高到仰角 ≈58°：相机现在正对 -x 看岸线，太阳在 -x 侧
            // 才能让水面镜像路径落回取景（与旧 225° 方位同理）；抬高后参考图那种短影
            // 与高角度直射才成立，同时减少 42° 低阳造成的长条黑影。
            // 指向太阳 L=(-0.50,0.82,-0.12)（归一化后仰角≈58°），forward=-L 朝下照地。
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.50f, -0.82f, 0.12f));
            sun.transform.position = new Vector3(-25f, 41f, -6f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // 场景全局环境色只对内置 Lit 生效；自写沙/石/水 shader 读的是材质 _AmbientColor。
            // 这里同步回原作天光色，避免两套环境数再次打架（审计 §4.C 记录过该矛盾）。
            RenderSettings.ambientLight = new Color(0.0627f, 0.1333f, 0.2196f);
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
            // 俯视沙盘取景（向 Battle 的俯角等距观感靠拢）：长焦 + 高机位。
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.88f);
            // 【取景口径】正对岸线（yaw 90°，相机在 +x 侧朝 -x 看）：参考头图的岸线是横的，
            // 旧 45° 斜看会把岸线拍成对角线，也让水色带斜切画面。取景中心 (1,0)、俯角 48°、
            // 机距 46：画面构成 ≈ 沙 26% / 水 74%，顶部是 ≥2 m 的深水、底部是干沙滩，
            // 64 m 域的边界全部落在画外（旧机位右上角 11% 露天空盒的灰色域外区消失）。
            // 俯角 48° 与太阳仰角 58° 配对，水面镜像路径仍横穿取景。
            var center = new Vector3(1f, 0f, 0f);
            float pitch = 42f * Mathf.Deg2Rad; // 距天顶 42° → 实际俯角 48°
            float yaw = 90f * Mathf.Deg2Rad;
            float dist = 46f;
            var dir = new Vector3(
                Mathf.Sin(pitch) * Mathf.Sin(yaw),
                Mathf.Cos(pitch),
                Mathf.Sin(pitch) * Mathf.Cos(yaw)); // 从中心指向机位
            go.transform.position = center + dir * dist;
            go.transform.rotation = Quaternion.LookRotation(-dir);
            return cam;
        }

        static void CreateTerrain()
        {
            var mesh = new Mesh { name = "WaterLabTerrain" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            int verts = TerrainSegments + 1;
            float step = WaterLabRules.DomainSize / TerrainSegments;

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

            var triangles = new int[TerrainSegments * TerrainSegments * 6];
            int t = 0;
            for (int z = 0; z < TerrainSegments; z++)
            {
                for (int x = 0; x < TerrainSegments; x++)
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
            // 焦散源＝水面小尺度坡度场：沙 shader 用同一张细节法线贴图求散度（见其焦散段）
            sandMat.SetTexture("_WaterLabDetailNormal", LoadOrCreateDetailNormal());
            ApplyCalibratedSandColors(sandMat);
            go.AddComponent<MeshRenderer>().sharedMaterial = sandMat;
        }

        /// <summary>
        /// 沙/礁石统一的显示链曝光因子（与 shader 默认值同源；此处显式下发是因为
        /// <see cref="LoadOrCreateMaterial"/> 只在资产不存在时取 shader 默认值——
        /// 改了 shader 默认值而不回填，已存在的 .mat 会一直留着旧数）。
        /// 【1.08】环境项抬到 (0.18,0.20,0.22) 补原作 GI 的暗部亮度后，受光核略增；
        /// 同步把曝光从 1.20 压到 1.08，保持参考受光干沙 (253,229,169) 同亮度。
        /// </summary>
        const float LightScale = 1.08f;

        static void ApplyCalibratedSandColors(Material m)
        {
            // 反解自参考受光干沙 (253,229,169)（线性 (0.982,0.784,0.397)）。
            // 【旧值为什么偏黄】旧沙色 (1.0,0.83,0.38) 是在二次编码截图上标的：截图把
            // 蓝通道从 0.43 抬到约 0.69，看起来刚好；实机单次编码下蓝只剩 89/255，
            // 沙面成浓黄。现按正确显示链反解：干沙 albedo sRGB (1.0,0.92,0.69)
            // → 线性 (1.0,0.831,0.434)，×光照核 (0.992,0.944,0.902) ≈ 参考线性值。
            m.SetColor("_DrySandColor", new Color(1.0f, 0.92f, 0.69f));
            // 湿沙：原作 wetSandColor/sandColor 的 linear 比例 ≈ (0.979,0.914,0.873)，
            // 只轻微变暗；旧值 (0.82,0.72,0.52) 暗 40%+，退潮湿痕会被读成残留水。
            m.SetColor("_WetSandColor", new Color(0.99f, 0.885f, 0.65f));
            // 水下沙：透过浅水看到的亮底沙，偏暖（水的青色会把它拉回绿松石）。
            m.SetColor("_UnderwaterSandColor", new Color(1.0f, 0.93f, 0.72f));
            m.SetFloat("_LightScale", LightScale);
            m.SetColor("_AmbientColor", AmbientColor);
            // 焦散世界尺度：0.05（=20 m 周期）会把细节法线的 Voronoi 胞放大成整片团块，
            // 观感是「水底一片黄绿斑」而不是参考那种**细亮网**；0.5（=2 m 周期）回到网尺度。
            m.SetFloat("_CausticScale", CausticScale);
            m.SetFloat("_CausticGain", CausticGain);
            // 沙面细节按原作 Lit_Sand PS 的结构：没有层理/沙纹/卵石 albedo；
            // 干湿混色、细粒噪声、闪沙、焦散与几何法线各自独立。全部是纯视觉项，
            // 不改变 WaterLabRules 的模拟地形/物理参数。
            m.SetFloat("_GrainStrength", 0.20f); // 原作沙粒噪声 ±0.2×n.y
            m.SetFloat("_GrainScale", 300.0f);   // worldXZ×0.3 的 1024² 白噪声 ≈3.3 mm
            m.SetFloat("_SparkleStrength", 0.02f);
            m.SetFloat("_SparkleScale", 14.0f);
        }

        /// <summary>焦散采样世界尺度（1/m）：周期 = 1/此值。参考的焦散网约 0.5–2 m。</summary>
        const float CausticScale = 0.5f;

        /// <summary>焦散增益：旧 0.2 在出图里几乎看不见网（参考浅滩有明显的亮暗纹）。
        /// 0.6 是显示链下的可读档；沙/石同一值，避免两层焦散强度脱节。</summary>
        const float CausticGain = 0.6f;

        /// <summary>
        /// 天光环境项（沙/石共用）。原作 HDR+GI 下暗部由天空 + 沙面反弹补亮；本仓只有
        /// 这一个常数项，取 (0.18,0.20,0.22) 近似合成亮度——旧 (0.08,0.14,0.21) 只保留
        /// 天空色，阴影里的沙红通道过低、发青发死。曝光因子已同步压到 1.08。
        /// </summary>
        static readonly Color AmbientColor = new Color(0.18f, 0.20f, 0.22f);

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

        /// <summary>细节法线贴图（WaterNormal2D 程序化等价物）：水面碎金的高频法线源，
        /// 同时是沙侧焦散的坡度场源。生成一次落资产，重建时已存在则复用。</summary>
        static Texture2D LoadOrCreateDetailNormal()
        {
            var path = $"{MaterialFolder}/WaterLabDetailNormal.asset";
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (detail == null)
            {
                detail = WaterLabDetailNormal.Create();
                AssetDatabase.CreateAsset(detail, path);
            }
            return detail;
        }

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

            // 参考原作：珊瑚/海草是成簇分布，不是均匀撒点。先取 16 个浅水簇心，
            // 70–75% 的实例落在簇心 0.4–2.4 m 内，其余仍全场点缀。
            var clusters = new Vector2[16];
            for (int i = 0; i < clusters.Length; i++)
            {
                clusters[i] = new Vector2(5f, 0f); // 兜底：近岸浅水
                for (int attempt = 0; attempt < 200; attempt++)
                {
                    // 簇心压在**水线附近的透亮带**（静态水深 0.02–0.30 m；高潮时
                    // 实际水深约 0.2–0.5 m）：水体的红光消光约 5.6/m，0.5 m 以下
                    // 任何色板都会退成暗蓝剪影。参考的彩色珊瑚/海草床也在这一带。
                    // 代价是低潮时部分簇会露出湿沙——参考的潮间带本来就有这种暴露。
                    float cx = Mathf.Lerp(5.0f, 7.2f, (float)rnd.NextDouble());
                    float cz = Mathf.Lerp(-28f, 28f, (float)rnd.NextDouble());
                    float ch = WaterLabRules.TerrainHeight(cx, cz);
                    if (ch < -0.02f && ch > -0.30f)
                    {
                        clusters[i] = new Vector2(cx, cz);
                        break;
                    }
                }
            }

            int rockCount = 0;
            for (int attempt = 0; attempt < 2400 && rockCount < 90; attempt++)
            {
                // 礁石只撒在可见的浅/中水带（x −14…12，约 0–1.0 m）；旧版
                // 撒到 x=−30 的深水，62 块落在 1.5–2.4 m，水下只剩一团团黑斑。
                float x = Mathf.Lerp(-14f, 12f, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-30f, 30f, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h > -0.04f) continue; // 只落在有水处（地形低于静水面 y=0）

                // 尺寸按当地水深封顶：露出水面就不是「水底参照物」了（静水面 y=0，水深=-h）
                float depth = -h;
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
                    x = Mathf.Lerp(-30f, 10f, (float)rnd.NextDouble());
                    z = Mathf.Lerp(-30f, 30f, (float)rnd.NextDouble());
                }
                float h = WaterLabRules.TerrainHeight(x, z);
                float depth = -h;
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
                    x = Mathf.Lerp(-30f, 8f, (float)rnd.NextDouble());
                    z = Mathf.Lerp(-30f, 30f, (float)rnd.NextDouble());
                }
                float h = WaterLabRules.TerrainHeight(x, z);
                float depth = -h;
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
                float x = Mathf.Lerp(11f, 30f, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-30f, 30f, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h < 0.12f) continue;
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
                float x = Mathf.Lerp(9f, 30f, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-30f, 30f, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h < 0.03f) continue;
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
                float x = Mathf.Lerp(10f, 30f, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-30f, 30f, (float)rnd.NextDouble());
                float h = WaterLabRules.TerrainHeight(x, z);
                if (h < 0.05f) continue; // 只落在干沙/后滨，不进水线浮沫区
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
            if (sim.computeShader == null)
                Debug.LogError($"[WaterLabSetup] 找不到 compute：{ComputePath}（检查文件是否入库/被导入）");
        }

        /// <summary>白水出画 Feature 幂等注入现役渲染器资产（Balanced+Performant，
        /// WaterLab 取景跟随质量档）。实例以子资产形式存进 .asset（URP 惯例），
        /// 清单/映射同步补位；白水材质随注（已有实例缺材质也补）。</summary>
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
