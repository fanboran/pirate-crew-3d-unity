using System.IO;
using Cinemachine;
using PirateCrew.Ambient;
using PirateCrew.Battle;
using PirateCrew.Data;
using PirateCrew.SceneArt;
using PirateCrew.Visual;
using PirateCrew.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// M2 战斗场景装配器：程序化重建 <c>Assets/Scenes/Battle.unity</c>，创建 PirateBase 预制体，
    /// 并把 <c>Scripts/PirateCrew/Battle/</c> 下各 MonoBehaviour 的 <c>[SerializeField]</c> 引用逐个接好。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/重建 Battle 战斗场景
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.M2BattleSceneSetup.BuildAll
    ///
    /// 【对应章节】
    ///   §3.2（CameraBrain/panToCharacter 的目标点）、§3.3（胜负）、§3.4（两阶段操作 + end go）、
    ///   §4.3（按关卡数据生成出战单位）、§4.4（落水即死 → 可站面必须高于水面；平台关由逐格平台提供，
    ///   不铺整块 y=0 地面（一代退场后海床台阶/远海床兜底装配段已删；重烘后的场景不再含一代残留，
    ///   BattleController 的旧退役器已随之删除——渲染退役在烘焙期落实，见 CreateWaterPlane）。
    ///
    /// 【幂等】
    ///   · 预制体用 <see cref="PrefabUtility.SaveAsPrefabAsset(GameObject,string,out bool)"/> 覆盖同名资产；
    ///   · 场景用 EmptyScene 全新建后覆盖保存（不叠加旧内容）；
    ///   · 材质/文件夹按路径复用，不产生重名副本。
    ///
    /// 【职责边界：本文件是**总编排**，渲染细节已拆出】
    ///   · 光照 / 天空盒 / 环境光 / 雾 / 后处理 / 环境材质库 / URP 设置 → <see cref="BattleSceneLighting"/>；
    ///   · 三个「后续波次填充」的桩钩子（本文件只负责在正确的时机用容错方式调用）：
    ///       Assets/Editor/CrewVisualPrefabBuilder.cs → <see cref="CrewVisualPrefabBuilder.BuildAll"/>（波次 I2 角色建模）
    ///       Assets/Editor/SceneArtBuilder.cs         → <see cref="SceneArtBuilder.Apply"/>（波次 I3 场景美术陈设）
    ///       Assets/Editor/BattleUiTheme.cs           → <see cref="BattleUiTheme.Apply"/>（波次 I4 UI 主题）
    ///     三者在当前提交里都是空实现；调用走 <see cref="RunArtHook"/>，空实现不报错、实现抛异常只记警告。
    ///   · 环境材质统一放 Assets/Art/Materials/Environment/（角色材质由角色波次放 Crew/）。
    ///
    /// 【场景美术 = 关卡无关（烘焙退位）】<see cref="SceneArtBuilder.Apply"/> 仍跑，但只保留两个职责：
    ///   生成/更新材质资产、把湿沙材质接给 <see cref="BattleTerrainView"/>。它烘出的**关卡专属静态陈设**
    ///   （level_1 的平台簇 / 道具合并网格）立即被删掉，改由运行时 <see cref="RuntimeSceneArt"/> 按
    ///   **实际关卡号**重建（<c>BattleController.RebuildSceneArt</c> → <c>RuntimeSceneArt.RebuildFor</c>）。
    ///   本文件负责把烘焙 prefab 引用（SceneArtBaker 产物）写进场景里的该组件——
    ///   材质/几何已烘焙进 prefab，原 21 槽材质数组随运行时几何生成退役（糖豆人式资产架构）。
    ///
    /// 【约定】本工程未装 TMP，UI 一律 legacy UnityEngine.UI（见 SceneSetup 类头）。
    ///         不手写 .unity/.prefab YAML，全部走 UnityEditor API。
    /// </summary>
    public static class M2BattleSceneSetup
    {
        // ------------------------------------------------------------------
        // 路径常量
        // ------------------------------------------------------------------

        const string ScenesFolder = "Assets/Scenes";
        const string PrefabFolder = "Assets/Prefabs/PirateCrew";
        const string MaterialFolder = PrefabFolder + "/Materials";

        /// <summary>
        /// 环境类材质库目录（沙/草/岩/木/金属/水/地形）由 <see cref="BattleSceneLighting"/> 生成与管理。
        /// 天空盒材质仍是 Assets/Art/Materials/BattleSky.mat（位置不变）；
        /// 角色类材质由角色波次放 Assets/Art/Materials/Crew/（本文件不碰）。
        /// </summary>
        static string ArtMaterialFolder => BattleSceneLighting.ArtMaterialFolder;

        const string PiratePrefabPath = PrefabFolder + "/PirateBase.prefab";

        /// <summary>职业视觉预制体目录（波次 I2 产出；命名 = <c>CrewVisualCatalog.PrefabFileName</c>）。</summary>
        const string CrewPrefabFolder = PrefabFolder + "/Crew";
        const string OutlineMaterialPath = MaterialFolder + "/PirateOutlineUnit.mat";
        const string OutlineShaderName = "PirateCrew/PirateOutline";
        const string BattleScenePath = ScenesFolder + "/Battle.unity";

        /// <summary>地形块父节点名（BattleTerrainView 挂在其下）。</summary>
        const string TerrainRootName = "Terrain";

        /// <summary>场景美术陈设根节点名（样板三关的自由几何由 RuntimeSceneArt 运行时装配）。</summary>
        const string SceneArtRootName = "SceneArt";

        /// <summary>名册行数，与 BattleHud.MaxRosterRows 对齐（level_4 最多 12 人）。</summary>
        const int RosterRows = 12;

        static Font _uiFont;

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Scenes/重建 Battle 战斗场景")]
        public static void BuildAll()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(ArtMaterialFolder);
            EnsureFolder(BattleSceneLighting.EnvironmentMaterialFolder);
            EnsureFolder(BattleSceneLighting.ArtRenderingFolder);

            // ---- 渲染基础（环境材质库 / 后处理 VolumeProfile / URP 设置）----
            // 必须先于场景构建：场景要引用这些材质资产；URP 的"深度图 on"与"软阴影 on"
            // 也是水面（PirateWater 读 _CameraDepthTexture）与阴影的前提。
            BattleSceneLighting.BuildAll();

            // ---- 波次 I2 钩子（角色建模）----
            // 当前 C# 侧是空实现；空实现必须不报错，实现层抛异常也只记警告、不阻断场景重建。
            RunArtHook("CrewVisualPrefabBuilder.BuildAll", CrewVisualPrefabBuilder.BuildAll);

            GameObject piratePrefab = BuildPiratePrefab();
            BuildBattleScene(piratePrefab);

            // ---- 小地图接线（场景保存后补）----
            // 【为什么必须在本链内】BuildBattleScene 从零新建场景再 SaveScene 覆盖——
            // BattleMinimap 组件与 unitRoots 接线由 HudMinimapSceneSetup 增量补挂，
            // 只存在于旧场景文件里；单独跑 BuildAll 曾把组件整颗洗掉（c78fdea 复盘，
            // 海图空白静默三轮），故每次重建后立即补接线，不再依赖 ArtGate ⑧ 单独跟跑。
            RunArtHook("HudMinimapSceneSetup.WireMinimap", HudMinimapSceneSetup.WireMinimap);

            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[M2BattleSceneSetup] Battle 战斗场景重建完成。\n"
                + "  场景: " + BattleScenePath + "（Build Settings index 2）\n"
                + "  预制体: " + PiratePrefabPath + "\n"
                + "  内容: 大海域海图（唯一玩法路径）+ 样板三关兜底（ShowcaseLevels）\n"
                + "  渲染: 环境材质库 " + BattleSceneLighting.EnvironmentMaterialFolder
                + " / 后处理 " + BattleSceneLighting.VolumeProfilePath
                + " / URP " + BattleSceneLighting.UrpAssetPath + "（软阴影+深度图+MSAA2）\n"
                + "  接线: BattleController / TurnManager / AimThrowController / TrajectoryPreview / "
                + "BattleCameraController（含 battle 手感源）/ BattleHud / "
                + "crewVisualPrefabs（7 职业）/ SceneArt.Ambient（活物）/ "
                + "RuntimeSceneArt（材质组数组，关卡无关的场景美术）的全部 [SerializeField] 引用。\n"
                + "  场景美术: 关卡专属静态陈设不入场景（烘焙退位），开局由 RuntimeSceneArt 按实际关卡重建。");
        }

        // ------------------------------------------------------------------
        // 预制体
        // ------------------------------------------------------------------

        /// <summary>
        /// 程序化创建 PirateBase 预制体：Cube 视觉（12×16px → 0.75×1.0 单位，1 单位 = 16px）
        /// + BoxCollider + Rigidbody + <see cref="PirateBase"/> + <see cref="UnitOutlineBinder"/>。
        /// </summary>
        static GameObject BuildPiratePrefab()
        {
            // 在临时空场景里搭，随后 NewScene 建 Battle 时会一并丢弃。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PirateBase";
            // §4.1：AABB 12×16px（left/rightExtent=6、top/bottomExtent=8）；1 单位 = 16px
            // （格 1→2 单位后 px 足迹随格放大：0.375×0.5 → 0.75×1.0，与 LevelGeometry.UnitPivotHeight 自洽）。
            go.transform.localScale = new Vector3(
                LevelGeometry.PixelsToUnits(12f), LevelGeometry.PixelsToUnits(16f), LevelGeometry.PixelsToUnits(12f));

            // 单位材质 = PirateOutline（本体 Pass + inverted hull 描边 Pass 一体），
            // 由 UnitOutlineBinder 用 MaterialPropertyBlock 逐单位写 _OutlineState。
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = EnsureOutlineMaterial();

            var body = go.AddComponent<Rigidbody>();
            body.mass = 1f;          // §4.1 weight = 1（击退不乘体重，质量仅给 PhysX 用）
            body.drag = 0f;
            body.angularDrag = 0.05f;

            var collider = go.GetComponent<BoxCollider>();
            var pirate = go.AddComponent<PirateBase>();
            // §4.5 选中/悬停描边：状态位 → 材质属性的每帧绑定。
            go.AddComponent<UnitOutlineBinder>();

            // 显式接线（PirateBase.Awake 也会兜底自动绑定，这里保证预制体上非空）。
            var so = new SerializedObject(pirate);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("bodyCollider").objectReferenceValue = collider;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PiratePrefabPath, out bool success);
            if (!success)
                Debug.LogError("[M2BattleSceneSetup] 保存 PirateBase 预制体失败: " + PiratePrefabPath);

            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------
        // 场景
        // ------------------------------------------------------------------

        static void BuildBattleScene(GameObject piratePrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 场地尺寸 = 样板第 1 关（云端漫步，20×15 格；一代退场后这是兜底场）。
            // 3D 化后 widthTiles → 世界 X，heightTiles → 世界 Z（纵深）；水面高度为全局常量。
            float waterWorldY = LevelGeometry.WaterSurfaceY;
            // 格 1→2 单位（用户裁决 2026-09-14）：竞技场世界尺寸 = 格数 × TileWorldSize。
            float worldWidth = LevelGeometry.TileToWorld(ShowcaseLevels.WidthTiles);
            float worldDepth = LevelGeometry.TileToWorld(ShowcaseLevels.DepthTiles);

            // 竞技场中心（相机与相机目标都以此为准）。
            Vector3 arenaCenter = new Vector3(worldWidth * 0.5f, LevelGeometry.GroundTopY, worldDepth * 0.5f);

            // 天空盒 + 环境光（对齐 Godot 基准的 WorldEnvironment：procedural sky + Sky 环境光）。
            // 天空盒/环境光/主光/雾/后处理全部由 BattleSceneLighting 负责（本文件只管场景编排）。
            bool hasSkybox = BattleSceneLighting.ConfigureSkyAndAmbient();

            Camera camera = CreateCamera(hasSkybox);
            camera.gameObject.AddComponent<CinemachineBrain>();
            camera.transform.position = arenaCenter + BattleCameraOffset;
            camera.transform.rotation = Quaternion.LookRotation(-BattleCameraOffset.normalized, Vector3.up);

            Transform cameraTarget = new GameObject("CameraTarget").transform;
            cameraTarget.position = arenaCenter;
            CinemachineVirtualCamera virtualCamera = CreateVirtualCamera(cameraTarget);

            Light sunLight = BattleSceneLighting.CreateDirectionalLight();

            // 氛围：线性雾 + 全局后处理 Volume + 主相机后处理开关。
            // 注意这是写进场景的 RenderSettings 与场景物体，必须放在相机创建之后。
            BattleSceneLighting.ApplySceneAtmosphere(camera);

            // 地面 = XZ 水平面（顶面 y = 0，角色脚底贴它）；水 = 地面下方一点的水平面。
            // 落水即死因此对 X / Z 任一方向掉出竞技场都成立（§4.4 全局规则）。
            //
            // 【场景烘成关卡无关：不建带碰撞的大地面（烘焙退位）】可站面**全部**由逐格平台提供
            // （运行时由 BattleController 从 PlatformClusterLayout.BuildFor 推导），平台之间是水；
            // 若仍铺一块顶面 y=0 的整块 Ground，从平台间隙掉落的单位会被它接在 y=0（水面之上），
            // 永远触发不了"落水即死"（§4.4）——这是玩法 bug。故**任何关卡**都不建大地面，
            // 视觉兜底改为水面之下的远海床 Seabed_Far（无碰撞）：保证间隙透下去是海。
            Transform ground = null;
            Transform water = CreateWaterPlane(worldWidth, worldDepth, waterWorldY);

            // 瓦片地形（可选）。网格在运行时由 BattleController 从 PlatformClusterLayout 推导并 Render，
            // 这里只创建承载视图的根节点与材质。
            BattleTerrainView terrainView = CreateTerrainView();

            // 场景美术陈设根节点：一代烘焙工具（SceneArtBuilder）已随一代退场删除，
            // 材质资产（Assets/Art/Materials/Scene/）保留，由 RuntimeSceneArt 消费。
            var sceneArt = new GameObject(SceneArtRootName);

            // 运行时陈设装配器：持有烘焙 prefab 引用（SceneArtBaker 产物），
            // 开局由 BattleController.RebuildSceneArt → RuntimeSceneArt.RebuildFor(实际关卡号) 实例化。
            RuntimeSceneArt runtimeSceneArt = sceneArt.AddComponent<RuntimeSceneArt>();
            WireRuntimeSceneArt(runtimeSceneArt);

            Transform team0Root = new GameObject("Team0_Red").transform;
            Transform team1Root = new GameObject("Team1_Blue").transform;

            // 活物（波次 ambient）：挂 SceneArt 子节点，接线 ground/sun/队伍根/相机与瓦片数。
            // 不接线时 AmbientDirector 有容错回落（自动取父节点、Camera.main），但瓦片数只能靠默认 50×17。
            BuildAmbientDirector(sceneArt.transform, ground, sunLight, team0Root, team1Root, camera,
                ShowcaseLevels.WidthTiles, ShowcaseLevels.DepthTiles);

            // 规则宿主。
            var turnManager = new GameObject("TurnManager").AddComponent<TurnManager>();
            var aimController = new GameObject("AimThrowController").AddComponent<AimThrowController>();
            var battleCamera = new GameObject("BattleCameraController").AddComponent<BattleCameraController>();
            TrajectoryPreview trajectory = CreateTrajectoryPreview();
            var battle = new GameObject("BattleController").AddComponent<BattleController>();

            BattleHud hud = BuildHud(battle, turnManager, aimController);

            WireBattleController(battle, piratePrefab, team0Root, team1Root, water, turnManager, aimController, battleCamera, terrainView, runtimeSceneArt);
            WireTurnManager(turnManager, battle);
            WireAimController(aimController, camera, battle, trajectory);
            WireBattleCamera(battleCamera, battle, virtualCamera, cameraTarget, camera);
            WireHud(hud, battle, turnManager, aimController);

            // 供 Debug 查看的层级整理（不影响逻辑引用）。
            battle.transform.position = Vector3.zero;

            if (!EditorSceneManager.SaveScene(scene, BattleScenePath))
                Debug.LogError("[M2BattleSceneSetup] 保存场景失败: " + BattleScenePath);
        }

        // ------------------------------------------------------------------
        // 场景内容
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // 战斗相机参数（pitch 45° / yaw 0 对齐 Godot orbit_camera.gd；
        // 距离 18→15 为提案调整：r2 出图实测出厂机位单位仅 21px < 判据 A-2 的 25px 下限，
        // 18/15 缩放后 ≈25px 达标，出处 docs/M2-3D空间模型对齐.md §相机行 + 美术品控 AR-R2-006）
        // 【格 1→2 单位 ×2】15 → 30：相机取景按"看同样的格数"放大，FOV 不动、俯角不动。
        // ------------------------------------------------------------------

        const float CameraDistance = 30f;
        const float CameraPitchDegrees = 45f;
        const float CameraFieldOfView = 60f;

        /// <summary>
        /// 相机相对焦点的偏移（+Z/+Y 侧俯视竞技场）。这个朝向也是"屏幕拖拽 → 世界 XZ 方向"
        /// 映射的基准（见 <see cref="LevelGeometry.ScreenDragToArenaDirection"/>）。
        /// </summary>
        static Vector3 BattleCameraOffset
        {
            get
            {
                float pitch = CameraPitchDegrees * Mathf.Deg2Rad;
                return new Vector3(
                    0f,
                    CameraDistance * Mathf.Sin(pitch),
                    CameraDistance * Mathf.Cos(pitch));
            }
        }

        static Camera CreateCamera(bool useSkybox)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = useSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.53f, 0.72f, 0.88f, 1f);
            camera.fieldOfView = CameraFieldOfView;
            camera.nearClipPlane = 0.1f;
            // 远裁剪面 ×2：竞技场世界尺寸翻倍后 50 格关 = 100×100 单位，200 已不够。
            camera.farClipPlane = 400f;
            return camera;
        }

        static CinemachineVirtualCamera CreateVirtualCamera(Transform follow)
        {
            var go = new GameObject("BattleVCam");
            var vcam = go.AddComponent<CinemachineVirtualCamera>();
            vcam.Priority = 20;
            vcam.Follow = follow;

            // 参照库 rts-camera-cinemachine 的 "CameraTarget 中转"：vcam 跟随空目标，
            // BattleCameraController 只平滑移动该目标。
            var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetWithWorldUp;
            // 3D 化：45° 俯角、距离 30（对齐 Godot orbit_camera.gd 的 pitch/distance 默认值；格 1→2 单位后 ×2）。
            transposer.m_FollowOffset = BattleCameraOffset;
            transposer.m_XDamping = 0f;
            transposer.m_YDamping = 0f;
            transposer.m_ZDamping = 0f;

            // 透视相机 + 斜俯视 —— 正交侧视是"2D 化"的遗留
            // （详见 docs/M2-3D空间模型对齐.md，改回正交前先读那份文档）。
            LensSettings lens = vcam.m_Lens;
            lens.Orthographic = false;
            lens.FieldOfView = CameraFieldOfView;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 200f;
            vcam.m_Lens = lens;

            go.transform.position = follow.position + BattleCameraOffset;
            go.transform.rotation = Quaternion.LookRotation(-BattleCameraOffset.normalized, Vector3.up);
            return vcam;
        }

        /// <summary>
        /// 水面：**XZ 水平面**（比地面外扩一大圈），无碰撞体——落水判定用世界 Y 阈值，不靠碰撞。
        /// 材质：PirateWater（双层波法线 + 菲涅尔 + Scene Depth 浅深水/岸边泡沫）；
        /// 它依赖 _CameraDepthTexture（URP Asset 已打开）。
        ///
        /// 【r3 修问题 1：水面外扩 40 → 200（每侧 20 → 100）】原水面只到竞技场外 20，
        /// 而远海床到外 44，于是水面矩形的直边在画面里露出来、边外是米色的 Seabed_Far ——
        /// 就是 r2 诊断的"水面矩形直边可见（左 x0-120 / 右 x1700-1920 两条直线斜边）+ 框外米色板"。
        /// 外扩到每侧 100 后，水边落到线性雾（<c>fogEndDistance=140</c>）之外/视锥之外，与天空自然衔接。
        /// </summary>
        static Transform CreateWaterPlane(float worldWidth, float worldDepth, float waterWorldY)
        {
            // margin 是**每侧**外扩量（原实现把它当总量用在 +margin，故每侧只有一半）。
            // 格 1→2 单位后 ×2（100 → 200），保持"水边落在雾/视锥之外"的同一观感。
            const float margin = 200f;

            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "Water";
            // 注意：BattleController.Start 会把 waterPlane.position.y 设为 LevelGeometry.WaterSurfaceY，
            // 所以这里直接放在该高度上（不要再加偏移，否则运行时会跳一下）。
            water.transform.position = new Vector3(
                worldWidth * 0.5f, waterWorldY, worldDepth * 0.5f);
            water.transform.localScale = new Vector3(
                worldWidth + margin * 2f, 0.1f, worldDepth + margin * 2f);

            var collider = water.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            water.GetComponent<MeshRenderer>().sharedMaterial = EnsureEnvironmentMaterial(
                BattleSceneLighting.WaterMaterial, new Color(0.13f, 0.42f, 0.68f, 1f));

            // 【海面渲染统一走 OceanRig（2026-09-19，管线合并前置）】本物体只作为常驻
            // WaterSimulationDriver 的宿主：Renderer 与 WaterTessellator 在烘焙期就禁用，
            // 运行时不再需要"定向退役"逻辑（BattleController 的 RetireLegacyWaterPlane 已删）。
            // 海面由 BattleController.SetupBattleEnvironment 创建的 OceanRig 圆盘渲染——
            // 世界图与样板关同一条路径。水面 shader 没有 ShadowCaster Pass（透明水体不投影）；
            // 组件禁用后 shadowCastingMode 无所谓，仍显式关掉防误开。
            MeshRenderer waterRenderer = water.GetComponent<MeshRenderer>();
            waterRenderer.enabled = false;
            waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Gerstner 顶点位移需要足够的网格密度——Cube 顶面只有 4 个顶点，
            // 不细分则波峰几何画不出来（明暗法线仍正常，因为解析法线逐像素重算）。
            // 细分粒度用组件默认（0.8 单位/格，上限 192 格/轴，见 WaterMeshRules）。
            // 随 Renderer 一并禁用：逐帧细分服务于旧 PirateWater 平面，海面圆盘不需要它。
            var tessellator = water.AddComponent<global::PirateCrew.Water.WaterTessellator>();
            tessellator.enabled = false;

            // 波动方程水面模拟（只驱动观感：法线扰动 + 泡沫源，不参与任何玩法判定）。
            // 障碍图由 WaterAssetBuilder.BakeObstacleMap 烘焙（ArtGate 在本步骤之前执行）。
            var driver = water.AddComponent<global::PirateCrew.Water.WaterSimulationDriver>();
            var obstacleMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Art/Textures/Water/WaterObstacleMap.png");
            if (obstacleMap != null)
            {
                var driverSo = new SerializedObject(driver);
                var mapProp = driverSo.FindProperty("obstacleMap");
                if (mapProp != null)
                {
                    mapProp.objectReferenceValue = obstacleMap;
                    driverSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            else
            {
                Debug.LogWarning("[M2BattleSceneSetup] 未找到 WaterObstacleMap.png——" +
                    "先跑 PirateCrew.EditorTools.WaterAssetBuilder.BakeObstacleMap，水面模拟将退化为无障碍模式");
            }
            return water.transform;
        }

        /// <summary>
        /// 瓦片地形视图：根节点 + <see cref="BattleTerrainView"/>（**纯碰撞层**——隐形 BoxCollider，
        /// 视觉由烘焙 prefab / WorldMapComposer 承担）。具体地形块由运行时
        /// <c>BattleController.BuildTerrain → RenderCollidersOnly</c> 生成。
        /// </summary>
        static BattleTerrainView CreateTerrainView()
        {
            var go = new GameObject(TerrainRootName);
            var view = go.AddComponent<BattleTerrainView>();

            var so = new SerializedObject(view);
            so.FindProperty("blockRoot").objectReferenceValue = go.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        // ------------------------------------------------------------------
        // 场景美术：烘焙退位 + 运行时装配器接线
        // ------------------------------------------------------------------

        /// <summary>
        /// 接线 <see cref="RuntimeSceneArt"/>（糖豆人式资产架构）：只写场景根引用与烘焙 prefab 引用
        /// （按路径装载 SceneArtBaker 的产物，缺资产留 null——运行时该件告警留空、不影响玩法）。
        /// 材质不再接线：几何/材质已烘焙进 prefab（原 21 槽 groupMaterials 数组随运行时几何生成退役）。
        /// </summary>
        static void WireRuntimeSceneArt(RuntimeSceneArt runtimeSceneArt)
        {
            if (runtimeSceneArt == null)
                return;

            var so = new SerializedObject(runtimeSceneArt);

            SerializedProperty root = so.FindProperty("sceneryRoot");
            if (root != null)
                root.objectReferenceValue = runtimeSceneArt.transform;

            SetPrefabRefIfExists(so, "cloudFieldPrefab", "Assets/Art/Models/SceneKit/CloudField.prefab");
            SetPrefabRefIfExists(so, "isletsPrefab", "Assets/Art/Models/SceneKit/Islets_L02.prefab");
            SetPrefabRefIfExists(so, "dangerBorderPrefab", "Assets/Art/Models/SceneKit/ShowcaseDangerBorder.prefab");

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>按路径装载烘焙 prefab 写进序列化引用；资产不存在时留 null 并记提示（不阻断重建）。</summary>
        static void SetPrefabRefIfExists(SerializedObject so, string fieldName, string path)
        {
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError("[M2BattleSceneSetup] RuntimeSceneArt 找不到序列化字段 " + fieldName + "（字段名漂移？）");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            prop.objectReferenceValue = prefab;
            if (prefab == null)
                Debug.LogWarning("[M2BattleSceneSetup] 烘焙件缺失（" + path + "）——先跑 "
                    + "PirateCrew/烘焙/样板场景件（SceneArtBaker.BuildAll），样板关该件将留空。");
        }

        static TrajectoryPreview CreateTrajectoryPreview()
        {
            var go = new GameObject("TrajectoryPreview");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = 0.06f;
            line.numCapVertices = 0;
            line.startColor = new Color(1f, 0.9f, 0.3f, 0.9f);
            line.endColor = new Color(1f, 0.5f, 0.2f, 0.4f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/Trajectory.mat", "Trajectory",
                "Sprites/Default", Color.white);

            var preview = go.AddComponent<TrajectoryPreview>();
            var so = new SerializedObject(preview);
            so.FindProperty("line").objectReferenceValue = line;
            so.ApplyModifiedPropertiesWithoutUndo();
            return preview;
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------

        static BattleHud BuildHud(BattleController battle, TurnManager turnManager, AimThrowController aimController)
        {
            Canvas canvas = CreateCanvas("BattleCanvas");
            CreateEventSystem();

            var controllerGo = new GameObject("BattleHud", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            Stretch(controllerGo.GetComponent<RectTransform>());
            var hud = controllerGo.AddComponent<BattleHud>();

            // 战场三引用在此接线；HUD 的全部节点与序列化字段由 BattleUiTheme.Apply 重建并回写。
            // （旧一代"先搭旧结构再被 Apply 清掉"的装配段已随名册退役删除——Apply 会清掉
            // Canvas 下除 BattleHud/MinimapPanel 之外的全部子节点，旧装配是纯死路径。）
            var so = new SerializedObject(hud);
            so.FindProperty("battle").objectReferenceValue = battle;
            so.FindProperty("turnManager").objectReferenceValue = turnManager;
            so.FindProperty("aimController").objectReferenceValue = aimController;
            so.ApplyModifiedPropertiesWithoutUndo();

            // ---- 波次 I4 钩子（UI 主题）----
            RunArtHook("BattleUiTheme.Apply", () => BattleUiTheme.Apply(canvas.gameObject));

            return hud;
        }

        // ------------------------------------------------------------------
        // 接线（SerializedObject）
        // ------------------------------------------------------------------

        static void WireBattleController(
            BattleController battle, GameObject piratePrefab,
            Transform team0Root, Transform team1Root, Transform waterPlane,
            TurnManager turnManager, AimThrowController aimController, BattleCameraController battleCamera,
            BattleTerrainView terrainView, RuntimeSceneArt runtimeSceneArt)
        {
            var prefabComponent = piratePrefab != null ? piratePrefab.GetComponent<PirateBase>() : null;
            if (prefabComponent == null)
                Debug.LogError("[M2BattleSceneSetup] PirateBase 预制体缺失 PirateBase 组件，BattleController.piratePrefab 无法接线。");

            var so = new SerializedObject(battle);
            SetRef(so, "piratePrefab", prefabComponent);
            SetRef(so, "team0Root", team0Root);
            SetRef(so, "team1Root", team1Root);
            SetRef(so, "waterPlane", waterPlane);
            SetRef(so, "turnManager", turnManager);
            SetRef(so, "aimController", aimController);
            SetRef(so, "battleCamera", battleCamera);
            SetRef(so, "terrainView", terrainView);
            SetRef(so, "sceneArt", runtimeSceneArt);
            SetBool(so, "team1IsAi", true);

            // 职业视觉预制体（波次 I2）：按 CrewVisualCatalog 的职业顺序填 crewVisualPrefabs，
            // 未命中/缺失时该元素留 null，BattleController 会回落 piratePrefab（方块外观兜底）。
            WireCrewVisualPrefabs(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 从 <c>Assets/Prefabs/PirateCrew/Crew/&lt;职业&gt;.prefab</c> 装载 7 个职业预制体，
        /// 按 <see cref="CrewVisualCatalog.AllProfessions"/> 顺序写入 <c>crewVisualPrefabs</c>。
        ///
        /// 【顺序为什么用 AllProfessions 而不是枚举遍历】职业枚举顺序与外观档顺序一致
        /// （<see cref="CrewProfession"/> 0..6），但用目录数组可避免枚举增删后顺序漂移。
        /// 【缺资产怎么办】找不到的项填 null 并汇总一条警告；不阻断场景重建（可能只是没跑
        /// <see cref="CrewVisualPrefabBuilder.BuildAll"/>，此时场景仍可用方块兜底跑起来）。
        /// </summary>
        static void WireCrewVisualPrefabs(SerializedObject battleSo)
        {
            SerializedProperty array = battleSo.FindProperty("crewVisualPrefabs");
            if (array == null)
            {
                Debug.LogError("[M2BattleSceneSetup] BattleController.crewVisualPrefabs 字段未找到（字段名漂移？）");
                return;
            }

            CrewProfession[] professions = CrewVisualCatalog.AllProfessions;
            array.arraySize = professions.Length;

            var missing = new System.Collections.Generic.List<string>();
            for (int i = 0; i < professions.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("profession").enumValueIndex = (int)professions[i];
                element.FindPropertyRelative("prefab").objectReferenceValue =
                    LoadCrewPrefab(professions[i], missing);
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning("[M2BattleSceneSetup] 以下职业预制体缺失，对应单位将回落 piratePrefab（方块）："
                    + string.Join("、", missing) + "。请先跑 PirateCrew.EditorTools.CrewVisualPrefabBuilder.BuildAll。");
            }
        }

        /// <summary>按职业加载 <c>Assets/Prefabs/PirateCrew/Crew/&lt;职业&gt;.prefab</c>；缺失返回 null。</summary>
        static PirateBase LoadCrewPrefab(CrewProfession profession,
            System.Collections.Generic.List<string> missing)
        {
            string path = CrewPrefabFolder + "/" + CrewVisualCatalog.PrefabFileName(profession) + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                missing.Add(CrewVisualCatalog.DisplayName(profession));
                return null;
            }

            PirateBase pirate = prefab.GetComponent<PirateBase>();
            if (pirate == null)
            {
                Debug.LogWarning("[M2BattleSceneSetup] 职业预制体缺 PirateBase 组件: " + path);
                missing.Add(CrewVisualCatalog.DisplayName(profession));
            }
            return pirate;
        }

        /// <summary>
        /// 活物总导演（波次 ambient）：在 SceneArt 下建子节点 <c>Ambient</c> 并接线。
        /// 瓦片数取自当前关卡（level_1 = 50×17），与 <see cref="AmbientDirector"/> 默认值一致；
        /// groundPlane 已接线时它按地面 localScale 推尺寸，瓦片数只是兜底。
        /// </summary>
        static void BuildAmbientDirector(Transform sceneArtRoot, Transform groundPlane, Light sunLight,
            Transform team0Root, Transform team1Root, Camera targetCamera, int widthTiles, int depthTiles)
        {
            var go = new GameObject("Ambient");
            go.transform.SetParent(sceneArtRoot, false);

            var ambient = go.AddComponent<AmbientDirector>();
            var so = new SerializedObject(ambient);
            SetRef(so, "sceneArtRoot", sceneArtRoot);
            SetRef(so, "groundPlane", groundPlane);
            SetRef(so, "sunLight", sunLight);
            SetRef(so, "team0Root", team0Root);
            SetRef(so, "team1Root", team1Root);
            SetRef(so, "targetCamera", targetCamera);
            SetInt(so, "arenaWidthTiles", widthTiles);
            SetInt(so, "arenaDepthTiles", depthTiles);

            // 三档天空盒材质（视觉遗留 #6 接线）：下标 = AmbientTimeOfDay。某档材质缺失就写 null——
            // AmbientDirector 运行时按 AmbientSkyboxCatalog 程序化兜底，不因跳步执行而断。
            // （材质本体由 SkyAssetBuilder.BuildAll 生成，ArtGate 步骤 ①.5 在场景装配之前。）
            SerializedProperty skyboxArray = so.FindProperty("skyboxMaterials");
            if (skyboxArray != null)
            {
                skyboxArray.arraySize = AmbientSkyboxCatalog.Tiers.Length;
                for (int i = 0; i < AmbientSkyboxCatalog.Tiers.Length; i++)
                {
                    Material tierSky = SkyAssetBuilder.LoadMaterial(AmbientSkyboxCatalog.Tiers[i]);
                    skyboxArray.GetArrayElementAtIndex(i).objectReferenceValue = tierSky;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireTurnManager(TurnManager turnManager, BattleController battle)
        {
            var so = new SerializedObject(turnManager);
            SetRef(so, "battle", battle);
            SetInt(so, "inactivityThreshold", BattleFlowRules.InactivityThreshold);
            SetBool(so, "autoResolveAiTurns", true);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireAimController(AimThrowController aimController, Camera battleCamera, BattleController battle, TrajectoryPreview trajectory)
        {
            var so = new SerializedObject(aimController);
            SetRef(so, "battleCamera", battleCamera);
            SetRef(so, "battle", battle);
            SetRef(so, "trajectory", trajectory);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireBattleCamera(BattleCameraController controller, BattleController battle,
            CinemachineVirtualCamera virtualCamera, Transform cameraTarget, Camera fallbackCamera)
        {
            var so = new SerializedObject(controller);
            SetRef(so, "virtualCamera", virtualCamera);
            SetRef(so, "cameraTarget", cameraTarget);
            SetRef(so, "fallbackCamera", fallbackCamera);
            // 手感数据源：投掷跟随/落水定焦要按 PirateId 定位单位与弹体。
            // 不接线时这些反馈静默降级（震屏/聚焦仍工作），故必须在此显式接线。
            SetRef(so, "battle", battle);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireHud(BattleHud hud, BattleController battle, TurnManager turnManager, AimThrowController aimController)
        {
            // BuildHud 已接线；此处仅确保引用仍有效（防御性，幂等重建时不会残留）。
            var so = new SerializedObject(hud);
            SetRef(so, "battle", battle);
            SetRef(so, "turnManager", turnManager);
            SetRef(so, "aimController", aimController);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetRef(SerializedObject so, string name, Object value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null)
            {
                Debug.LogError("[M2BattleSceneSetup] 找不到序列化字段: " + name);
                return;
            }
            prop.objectReferenceValue = value;
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
                prop.intValue = value;
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
                prop.boolValue = value;
        }

        static void SetObjectArray(SerializedProperty arrayProp, Object[] values)
        {
            if (arrayProp == null || values == null)
                return;

            arrayProp.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        // ------------------------------------------------------------------
        // UI 构建辅助（风格对齐 SceneSetup.cs）
        // ------------------------------------------------------------------

        static Font UiFont
        {
            get
            {
                if (_uiFont == null)
                    _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _uiFont;
            }
        }

        static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // 对齐 Godot canvas_items+expand 口径：Expand(1) 外扩参考分辨率，超宽屏不留黑边也不压扁布局
            // （matchWidthOrHeight 仅 MatchWidthOrHeight 模式生效，随之废弃）。存量已建 .unity 场景仍是旧口径，
            // 不改场景文件——重建走本构建器，待界面换装批次统一跑。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return canvas;
        }

        static void CreateEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = CreateUiObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        static Text CreateText(string name, Transform parent, string content, int fontSize,
            TextAnchor alignment, Color color)
        {
            var go = CreateUiObject(name, parent);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = UiFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static Image CreateImage(string name, Transform parent, Vector2 size, Vector2 anchoredPosition, Color color)
        {
            var go = CreateUiObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Button CreateButton(string name, Transform parent, string label, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, out Text labelText)
        {
            var go = CreateUiObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = go.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            labelText = CreateText("Text", go.transform, label, 18, TextAnchor.MiddleCenter, Color.black);
            Stretch(labelText.rectTransform);
            return button;
        }

        static void SetAnchoredRect(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // 材质 / 文件夹 / Build Settings
        // ------------------------------------------------------------------

        /// <summary>
        /// 调一个"后续波次填充"的美术钩子。
        ///
        /// 【为什么要容错】三个桩（<see cref="SceneArtBuilder"/> / <see cref="CrewVisualPrefabBuilder"/> /
        /// <see cref="BattleUiTheme"/>）当前都是空实现，由波次 I2/I3/I4 并行填充；
        /// 骨架重建不能被别人的半成品/异常打断，所以这里把异常降级为警告。
        /// 反过来说：**钩子里的异常不会中断场景重建，但会在 Console 留下警告**，
        /// 实现方调完钩子后要 read_console 确认没有自己的警告。
        /// </summary>
        static void RunArtHook(string hookName, System.Action hook)
        {
            if (hook == null)
                return;

            try
            {
                hook();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[M2BattleSceneSetup] 美术钩子 " + hookName + " 抛出异常（已忽略，不影响场景重建）：\n" + e);
            }
        }

        /// <summary>
        /// 取环境材质库中的材质（由 <see cref="BattleSceneLighting.BuildAll"/> 生成）。
        /// 取不到时退回 URP/Lit 纯色材质，**并且建在同一个 Environment/ 路径下**，
        /// 这样下次生成成功时会被就地替换回程序化材质，不会留下两份同名不同位置的资产。
        /// </summary>
        static Material EnsureEnvironmentMaterial(string materialName, Color fallbackColor)
        {
            Material material = BattleSceneLighting.LoadEnvironmentMaterial(materialName);
            if (material != null)
                return material;

            Debug.LogWarning("[M2BattleSceneSetup] 环境材质 " + materialName
                + " 未找到（BattleSceneLighting 生成失败？），退回 URP/Lit 纯色。"
                + "请先在编辑器里 read_console 确认 Assets/Art/Shaders 下三个 shader 无编译错误。");

            return EnsureMaterial(
                BattleSceneLighting.EnvironmentMaterialFolder + "/" + materialName + ".mat",
                materialName, "Universal Render Pipeline/Lit", fallbackColor, "Standard");
        }

        /// <summary>
        /// 单位材质：<c>PirateOutline</c>（本体 Pass + inverted hull 描边 Pass 一体）。
        ///
        /// 【为什么把描边材质直接当本体材质，而不是另开一圈"描边复制网格"】
        ///   复制网格方案要保持本体为 URP/Lit，但两个共面网格会 z-fighting，且需要额外的
        ///   mesh 复制与 material_override 管理。PirateOutline 自带本体 Pass（简单 Lambert + SH），
        ///   直接当本体材质最省事。
        ///   注：单位**会投影** —— PirateOutline.shader 已补 ShadowCaster Pass
        ///   （2026-09-13；从此单位进主光阴影图，画面纵深感靠它）。
        ///
        /// 【状态 0 必须不可见】shader 的 OutlineColorForState() 在 state==0 时回落到
        ///   <c>_OutlineColor</c>，故把它的 alpha 设为 0 —— 片元里 `alpha &lt; 0.002` 会 discard，
        ///   未悬停/未选中的单位就不会顶着一圈青边。hover/selected 两套色保留原版取值。
        /// </summary>
        static Material EnsureOutlineMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find(OutlineShaderName);
            if (shader == null)
            {
                // shader 编译失败/被剔除时退回不透明本体，至少不出现粉色错误材质。
                Debug.LogWarning("[M2BattleSceneSetup] 未找到 shader " + OutlineShaderName
                    + "，单位退回 URP/Lit 本体材质（无描边，UnitOutlineBinder 会告警）。");
                return EnsureMaterial(
                    MaterialFolder + "/PirateBody.mat", "PirateBody",
                    "Universal Render Pipeline/Lit", new Color(0.85f, 0.82f, 0.7f, 1f), "Standard");
            }

            var material = new Material(shader) { name = "PirateOutlineUnit" };

            // 本体色：默认中性米白；运行时由 UnitOutlineBinder 按队伍用 MPB 覆盖。
            material.SetColor("_BaseColor", new Color(0.85f, 0.82f, 0.70f, 1f));

            // 描边：兜底色（state 0）alpha = 0 → 不可见；hover 淡白细线；selected 青色粗虚线。
            material.SetColor("_OutlineColor", new Color(0.286f, 0.851f, 0.839f, 0f));
            material.SetColor("_OutlineColorHover", new Color(1f, 1f, 1f, 0.22f));
            material.SetColor("_OutlineColorSelected", new Color(0.286f, 0.851f, 0.839f, 0.949f));

            material.SetFloat("_OutlineWidth", 0.006f);
            material.SetFloat("_OutlineWidthHover", 0.0042f);
            material.SetFloat("_OutlineWidthSelected", 0.010f);

            material.SetFloat("_OutlineState", 0f);
            material.SetFloat("_OutlineAlpha", 1f);
            material.SetFloat("_OutlineExpandMode", 0f);              // 0 = 屏幕空间恒定粗细（Godot 等价做法）
            material.SetFloat("_OutlineDistanceAttenuation", 0.4f);   // Godot 默认 0.4
            material.SetFloat("_DashSpeed", 5f);
            // 与 CrewVisualPrefabBuilder.DashFrequencySelected 同步（r5：50→150，
            // OFF 带短于最小部件，防"整件落在 OFF 带"的描边假阴性；推导见该常量注释）。
            material.SetFloat("_DashFrequency", 150f);
            material.SetFloat("_DebugMode", 0f);

            AssetDatabase.CreateAsset(material, OutlineMaterialPath);
            return material;
        }

        static Material EnsureMaterial(string path, string name, string shaderName, Color color, string fallbackShaderName = null)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null && !string.IsNullOrEmpty(fallbackShaderName))
                shader = Shader.Find(fallbackShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[M2BattleSceneSetup] 未找到 shader " + shaderName + "，退回 Standard。");
                shader = Shader.Find("Standard");
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                // 就地更新而不是直接返回旧资产：幂等重建时也要让 shader/颜色跟上代码，
                // 否则改了这里的观感常量却因为"资产已存在"看不到任何变化（排查起来很费时）。
                if (shader != null && existing.shader != shader)
                    existing.shader = shader;
                ApplyMaterialColor(existing, color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var material = new Material(shader) { name = name };
            ApplyMaterialColor(material, color);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>URP/Lit 用 <c>_BaseColor</c>、Standard/Sprites 用 <c>_Color</c>；两者都试，避免遗漏。</summary>
        static void ApplyMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        static void RegisterBuildSettings()
        {
            string[] names = { "Bootstrapper", "MainMenu", "Battle" };
            var scenes = new EditorBuildSettingsScene[names.Length];
            for (int i = 0; i < names.Length; i++)
                scenes[i] = new EditorBuildSettingsScene(ScenesFolder + "/" + names[i] + ".unity", true);

            // 顺序即 index：Bootstrapper=0（入口）、MainMenu=1、Battle=2。
            EditorBuildSettings.scenes = scenes;
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
