using System.IO;
using Cinemachine;
using PirateCrew.PirateCrew.Ambient;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.Visual;
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
    ///   不铺整块 y=0 地面，见 <see cref="CreateGround"/> / <see cref="CreateFarSeabed"/>）.
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
    ///   本文件负责把材质组材质数组（顺序 = <c>RuntimeSceneArt.GroupNames</c>）写进场景里的该组件。
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
        const string LevelAssetPath = "Assets/Data/Levels/level_1.asset";

        /// <summary>地形块父节点名（BattleTerrainView 挂在其下）。</summary>
        const string TerrainRootName = "Terrain";

        /// <summary>场景美术陈设根节点名（<see cref="SceneArtBuilder.Apply"/> 的挂载点，波次 I3 填充内容）。</summary>
        const string SceneArtRootName = "SceneArt";

        /// <summary>
        /// 场景美术材质资产目录（<c>SceneArtBuilder.SceneMaterialFolder</c> 同值）。
        /// 运行时 <see cref="RuntimeSceneArt"/> 复用这批材质：烘焙时按
        /// <see cref="RuntimeSceneArt.GroupMaterialNames"/> 从本目录装载并写进组件的序列化数组。
        /// </summary>
        const string SceneArtMaterialFolder = "Assets/Art/Materials/Scene";

        /// <summary>场景装配使用的关卡号（LevelCatalog 已转写的 level_1）。</summary>
        const int LevelNumber = 1;

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
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[M2BattleSceneSetup] Battle 战斗场景重建完成。\n"
                + "  场景: " + BattleScenePath + "（Build Settings index 2）\n"
                + "  预制体: " + PiratePrefabPath + "\n"
                + "  关卡数据: " + LevelAssetPath + "（回退 LevelCatalog.Get(" + LevelNumber + ")）\n"
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

            // 关卡数据驱动尺寸（§4.3）。3D 化后 widthTiles → 世界 X，heightTiles → 世界 Z（纵深）；
            // 水面高度改为全局常量（地图是平坦的 XZ 竞技场，不再由 waterTileY 推出）。
            LevelData data = LevelCatalog.Get(LevelNumber);
            float waterWorldY = LevelGeometry.WaterSurfaceY;
            // 格 1→2 单位（用户裁决 2026-09-14）：竞技场世界尺寸 = 格数 × TileWorldSize。
            float worldWidth = LevelGeometry.TileToWorld(data.WidthTiles);
            float worldDepth = LevelGeometry.TileToWorld(data.HeightTiles);

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

            // 水面（含平台间隙）之下的远海床兜底：无碰撞，单位落水判定不受影响。
            CreateFarSeabed(worldWidth, worldDepth, waterWorldY);

            // 岛外海床台阶（纯表现、无碰撞）：PirateWater 的浅深水过渡与岸边泡沫依赖
            // _CameraDepthTexture 有东西可读，详见 CreateSeabedShelves 与 PirateWater.shader 头注释。
            CreateSeabedShelves(worldWidth, worldDepth, waterWorldY);

            // 瓦片地形（可选）。网格在运行时由 BattleController 从 PlatformClusterLayout 推导并 Render，
            // 这里只创建承载视图的根节点与材质。
            BattleTerrainView terrainView = CreateTerrainView();

            // 场景美术陈设根节点。
            // 【烘焙退位】SceneArtBuilder.Apply 仍要跑：它负责**生成/更新材质资产**、
            // 把湿沙材质接给 BattleTerrainView（并关掉运行时的单材质平台底部）。
            // 但它同时会把 level_1 的陈设烘成**静态合并网格**——那些是关卡专属的，现在由
            // 运行时 RuntimeSceneArt 按实际关卡重建，故 Apply 之后立刻把那批 GameObject 删掉
            // （网格资产留在磁盘上备查，不再被场景引用）。
            var sceneArt = new GameObject(SceneArtRootName);
            RunArtHook("SceneArtBuilder.Apply", () => SceneArtBuilder.Apply(sceneArt));
            PruneBakedScenery(sceneArt);

            // 运行时陈设装配器：持有一份材质组材质（顺序 = RuntimeSceneArt.GroupNames），
            // 开局由 BattleController.RebuildSceneArt → RuntimeSceneArt.RebuildFor(实际关卡号) 重建。
            RuntimeSceneArt runtimeSceneArt = sceneArt.AddComponent<RuntimeSceneArt>();
            WireRuntimeSceneArt(runtimeSceneArt);

            Transform team0Root = new GameObject("Team0_Red").transform;
            Transform team1Root = new GameObject("Team1_Blue").transform;

            // 活物（波次 ambient）：挂 SceneArt 子节点，接线 ground/sun/队伍根/相机与瓦片数。
            // 不接线时 AmbientDirector 有容错回落（自动取父节点、Camera.main），但瓦片数只能靠默认 50×17。
            BuildAmbientDirector(sceneArt.transform, ground, sunLight, team0Root, team1Root, camera,
                data.WidthTiles, data.HeightTiles);

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

        /// <summary>
        /// 岛外海床台阶（纯表现、无碰撞、不投影）。
        ///
        /// 【为什么必须有】<c>PirateWater</c> 的浅深水过渡与岸边泡沫靠 <c>_CameraDepthTexture</c>
        /// 读出"水面之下还有多远才是实体"。竞技场是浮在海上的沙岛（地面顶面 y=0、水面 y=-0.2），
        /// 岛外若没有海床，水面之后的场景深度就是天空 → 处处"深水"，既无浅深水过渡、也无泡沫。
        /// 故在岛外铺多层**会写深度**的同心台阶（材质须带 DepthOnly Pass）。
        ///
        /// 【r3 修问题 5：两级 → 五级同心坡】原实现只有两级（-0.6 外扩 6 / -1.6 外扩 16），
        /// 台阶之间是一整块平色，只在两条边界处各出现一条硬色界，读不出"浅滩→中水→深水"渐变。
        /// 现按场景设计 §5.1「海床坡向外 8-12 单位缓降到 y=-3」铺 5 级同心台阶（格 1→2 单位后整段 ×2：向外 16-24、降到 y=-6）
        /// （外扩 10→18→30→48→72，顶面 waterWorldY-0.9 → -7.0），使 waterDepth 连续下沉，
        /// 对上 shader 的三档水色（场景设计判据 Q-17：水色标准差 > 4）。
        /// 用**覆盖全竞技场的同心块**（而不是只有竞技场外的环）：环形会在竞技场矩形内缘留下
        /// 一条"深/浅"硬色界，正好又变成一条直线切边。
        /// 【提案/待定】级数与深度为 AI 取值，观感验收时可调，但**必须保留"写深度"这一职责**。
        /// </summary>
        static void CreateSeabedShelves(float worldWidth, float worldDepth, float waterWorldY)
        {
            // 深度/外扩全为世界距离类 → 格 1→2 单位后 ×2（0.45→0.9 … 36→72），
            // 使 waterDepth 的读数在"同一格数"处取到同一档水色。
            CreateSeabedShelf("Seabed_L0", worldWidth, worldDepth, waterWorldY - 0.90f, 10f,
                BattleSceneLighting.WetSandMaterial, new Color(0.62f, 0.53f, 0.40f, 1f));
            CreateSeabedShelf("Seabed_L1", worldWidth, worldDepth, waterWorldY - 2.00f, 18f,
                BattleSceneLighting.WetSandMaterial, new Color(0.52f, 0.43f, 0.32f, 1f));
            CreateSeabedShelf("Seabed_L2", worldWidth, worldDepth, waterWorldY - 3.60f, 30f,
                BattleSceneLighting.RockMaterial, new Color(0.46f, 0.41f, 0.34f, 1f));
            CreateSeabedShelf("Seabed_L3", worldWidth, worldDepth, waterWorldY - 5.40f, 48f,
                BattleSceneLighting.RockMaterial, new Color(0.42f, 0.38f, 0.32f, 1f));
            CreateSeabedShelf("Seabed_L4", worldWidth, worldDepth, waterWorldY - 7.00f, 72f,
                BattleSceneLighting.RockMaterial, new Color(0.36f, 0.33f, 0.29f, 1f));
        }

        /// <summary>单层海床台阶：Cube 顶面在 <paramref name="topY"/>，向四周外扩 <paramref name="spread"/>。</summary>
        static void CreateSeabedShelf(string name, float worldWidth, float worldDepth, float topY, float spread,
            string materialName, Color fallbackColor)
        {
            const float thickness = 1.0f;   // 世界厚度 ×2（格 1→2 单位）

            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shelf.name = name;
            shelf.transform.position = new Vector3(
                worldWidth * 0.5f, topY - thickness * 0.5f, worldDepth * 0.5f);
            shelf.transform.localScale = new Vector3(
                worldWidth + spread * 2f, thickness, worldDepth + spread * 2f);

            // 无碰撞：它是纯水下观感几何；角色掉出竞技场后应继续落到水面判定线以下（§4.4），
            // 不希望被海床接住。
            var collider = shelf.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            var renderer = shelf.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureEnvironmentMaterial(materialName, fallbackColor);
            // 不投影：水下几何投影会在水面上打出莫名暗斑。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
        /// 地面：**XZ 水平面**（厚 0.2 的 Cube，顶面 y = <see cref="LevelGeometry.GroundTopY"/>）。
        /// 角色脚底贴在顶面上，所以从 X 或 Z 任一侧掉出去都会落到水面以下（§4.4）。
        /// 原先是 XY 竖直薄板（2D 侧视遗留）。
        /// 材质：干沙（PirateSurface 程序化三档沙色，GDD §10.4 沙地三档）。
        ///
        /// 【已退役：不再被 Battle 场景调用】场景烘成关卡无关后，任何关卡的可站面都由
        /// <c>PlatformClusterLayout.BuildFor</c> 的逐格平台提供（见 <see cref="BuildBattleScene"/> 的
        /// "不建带碰撞的大地面"）；整块 y=0 地面会接住从平台间隙掉落的单位、破坏"落水即死"。
        /// 方法保留是因为 <c>M3SceneSetup</c> 等非战斗场景仍可能需要一块实体地面；
        /// 战斗场景的视觉兜底改用无碰撞的 <see cref="CreateFarSeabed"/>。
        /// </summary>
        static Transform CreateGround(float worldWidth, float worldDepth)
        {
            const float thickness = 0.2f;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(
                worldWidth * 0.5f,
                LevelGeometry.GroundTopY - thickness * 0.5f,
                worldDepth * 0.5f);
            ground.transform.localScale = new Vector3(worldWidth, thickness, worldDepth);
            ground.GetComponent<MeshRenderer>().sharedMaterial = EnsureEnvironmentMaterial(
                BattleSceneLighting.DrySandMaterial, new Color(0.82f, 0.74f, 0.53f, 1f));
            return ground.transform;
        }

        /// <summary>
        /// 海面下的"远海床"兜底（仅平台关）：水面之下、**无碰撞**的大平面，铺在五级环形海床台阶
        /// （<see cref="CreateSeabedShelves"/> 的 L0..L4，最深 -2.9）之下，保证平台间隙向下看到的是
        /// 海床而不是天空盒。
        ///
        /// 【为什么必须无碰撞】单位从平台间隙落下后应继续穿越 <see cref="LevelGeometry.WaterSurfaceY"/>
        /// 触发落水即死（§4.4）；任何接在中间（尤其水面之上）的几何都会把落水变成"站在隐形地板上"。
        /// 所以本物体与海床台阶一样销毁 Collider、且不投影。
        ///
        /// 【尺寸】竞技场外扩 88（略大于最外环 L4 的 72，保证环形坡之外仍有兜底），
        /// 材质复用海床台阶的岩材质（保证浅深水读深一致）。
        /// 【提案/待定】高度 -7.2（深于 L4 的 -7.0，不参与浅深水过渡读深）是 AI 调参值；
        /// 观感验收时可调，但**必须保持无碰撞**。
        /// </summary>
        static void CreateFarSeabed(float worldWidth, float worldDepth, float waterWorldY)
        {
            const float thickness = 1.0f;   // 世界厚度 ×2（格 1→2 单位）
            const float margin = 88f;       // 外扩 ×2（须大于最外环 L4 的 72）
            float topY = waterWorldY - 7.2f;

            var seabed = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seabed.name = "Seabed_Far";
            seabed.transform.position = new Vector3(
                worldWidth * 0.5f, topY - thickness * 0.5f, worldDepth * 0.5f);
            seabed.transform.localScale = new Vector3(
                worldWidth + margin * 2f, thickness, worldDepth + margin * 2f);

            var collider = seabed.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            var renderer = seabed.GetComponent<MeshRenderer>();
            // 与 CreateSeabedShelf 的岩材质同色兜底：材质库缺失时不会生成两份不同色的同名资产。
            renderer.sharedMaterial = EnsureEnvironmentMaterial(
                BattleSceneLighting.RockMaterial, new Color(0.42f, 0.38f, 0.32f, 1f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// 水面：**XZ 水平面**（比地面外扩一大圈），无碰撞体——落水判定用世界 Y 阈值，不靠碰撞。
        /// 材质：PirateWater（双层波法线 + 菲涅尔 + Scene Depth 浅深水/岸边泡沫）；
        /// 它依赖 _CameraDepthTexture（URP Asset 已打开）与岛外海床台阶（<see cref="CreateSeabedShelves"/>）。
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
            // 水面 shader 没有 ShadowCaster Pass（透明水体不投影）；显式关掉投影，免去阴影通道空跑一次。
            water.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Gerstner 顶点位移需要足够的网格密度——Cube 顶面只有 4 个顶点，
            // 不细分则波峰几何画不出来（明暗法线仍正常，因为解析法线逐像素重算）。
            // 细分粒度用组件默认（0.8 单位/格，上限 192 格/轴，见 WaterMeshRules）。
            water.AddComponent<global::PirateCrew.PirateCrew.Water.WaterTessellator>();

            // 波动方程水面模拟（只驱动观感：法线扰动 + 泡沫源，不参与任何玩法判定）。
            // 障碍图由 WaterAssetBuilder.BakeObstacleMap 烘焙（ArtGate 在本步骤之前执行）。
            var driver = water.AddComponent<global::PirateCrew.PirateCrew.Water.WaterSimulationDriver>();
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
        /// 瓦片地形视图：根节点 + <see cref="BattleTerrainView"/> + 地块材质。
        /// 具体地形块由运行时 <c>BattleController.BuildTerrain → BattleTerrainView.Render</c> 生成。
        /// 材质：PirateTerrain（按世界高度/坡度混合沙/草/岩 + 低多边形块面感 + #2A2A2A 边缘压暗）。
        /// </summary>
        static BattleTerrainView CreateTerrainView()
        {
            var go = new GameObject(TerrainRootName);
            var view = go.AddComponent<BattleTerrainView>();

            Material material = EnsureEnvironmentMaterial(
                BattleSceneLighting.TerrainMaterial, new Color(0.55f, 0.46f, 0.33f, 1f));

            var so = new SerializedObject(view);
            so.FindProperty("blockRoot").objectReferenceValue = go.transform;
            so.FindProperty("blockMaterial").objectReferenceValue = material;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        // ------------------------------------------------------------------
        // 场景美术：烘焙退位 + 运行时装配器接线
        // ------------------------------------------------------------------

        /// <summary>
        /// 删除 <see cref="SceneArtBuilder.Apply"/> 烘出的**关卡专属静态陈设**（<c>SceneArt_*</c> 合并网格）。
        ///
        /// 【为什么】场景要烘成"关卡无关"：level_1 的平台簇 / 道具位置烘死进场景后，换关卡（地形瓦片会变）
        /// 就会与平台簇脱节。陈设改由运行时 <see cref="RuntimeSceneArt"/> 按实际关卡重建。
        /// Apply 保留的职责：生成/更新材质资产、把湿沙材质接给 <see cref="BattleTerrainView"/>。
        /// 被删的只是场景里的渲染节点（网格资产留在磁盘备查，不再被引用）。
        ///
        /// 【只删 Apply 造的】**必须在建 Ambient 子节点之前调用**（ambient 挂在同一根下，
        /// 这里按 <c>SceneArt_</c> 前缀识别，即使顺序变了也不会误删 Ambient）。
        /// </summary>
        static void PruneBakedScenery(GameObject sceneArtRoot)
        {
            if (sceneArtRoot == null)
                return;

            var doomed = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < sceneArtRoot.transform.childCount; i++)
            {
                GameObject child = sceneArtRoot.transform.GetChild(i).gameObject;
                if (child.name.StartsWith("SceneArt_"))
                    doomed.Add(child);
            }

            for (int i = 0; i < doomed.Count; i++)
                Object.DestroyImmediate(doomed[i]);

            if (doomed.Count > 0)
                Debug.Log("[M2BattleSceneSetup] 烘焙退位：移除 " + doomed.Count
                    + " 个关卡专属静态陈设节点（改由 RuntimeSceneArt 按实际关卡运行时重建）。");
        }

        /// <summary>
        /// 把材质组材质写进 <see cref="RuntimeSceneArt"/> 的序列化数组（顺序 = <see cref="RuntimeSceneArt.GroupNames"/>）。
        /// 材质来自 <c>SceneArtBuilder</c> 生成的 <c>Assets/Art/Materials/Scene/Scene_*.mat</c>；
        /// 缺失的槽留 null（运行时该组跳过并告警，不影响地形与玩法）。
        /// </summary>
        static void WireRuntimeSceneArt(RuntimeSceneArt runtimeSceneArt)
        {
            if (runtimeSceneArt == null)
                return;

            var so = new SerializedObject(runtimeSceneArt);
            SerializedProperty array = so.FindProperty("groupMaterials");
            if (array == null)
            {
                Debug.LogError("[M2BattleSceneSetup] RuntimeSceneArt.groupMaterials 字段未找到（字段名漂移？）");
                return;
            }

            var missing = new System.Collections.Generic.List<string>();
            int count = RuntimeSceneArt.GroupNames.Length;
            array.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                string path = SceneArtMaterialFolder + "/" + RuntimeSceneArt.GroupMaterialNames[i] + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    missing.Add(RuntimeSceneArt.GroupMaterialNames[i]);
                array.GetArrayElementAtIndex(i).objectReferenceValue = material;
            }

            SerializedProperty root = so.FindProperty("sceneryRoot");
            if (root != null)
                root.objectReferenceValue = runtimeSceneArt.transform;

            so.ApplyModifiedPropertiesWithoutUndo();

            if (missing.Count > 0)
            {
                Debug.LogWarning("[M2BattleSceneSetup] RuntimeSceneArt 缺 " + missing.Count
                    + " 个材质组材质（" + string.Join("、", missing) + "）；对应组运行时跳过。"
                    + "请确认 SceneArtBuilder.Apply 已成功跑过（它负责生成这批 .mat）。");
            }
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

            // ---- TopRight：回合提示 / 状态（§3.2）----
            RectTransform topRight = CreatePanel("TopRightPanel", canvas.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -16f), new Vector2(640f, 90f));
            Text turnHint = CreateText("TurnHintText", topRight, string.Empty, 26,
                TextAnchor.UpperRight, new Color(1f, 0.85f, 0.3f, 1f));
            SetAnchoredRect(turnHint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(640f, 34f));
            Text teamStatus = CreateText("TeamStatusText", topRight, string.Empty, 20,
                TextAnchor.UpperRight, new Color(0.85f, 0.9f, 1f, 1f));
            SetAnchoredRect(teamStatus.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -38f), new Vector2(640f, 30f));

            // ---- BottomCenter：武器面板（§3.4；17 种武器分 3 行）----
            RectTransform weaponPanel = CreatePanel("WeaponPanel", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(920f, 200f));
            var panelImage = weaponPanel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.55f);
            panelImage.raycastTarget = false;

            Text weaponTitle = CreateText("WeaponPanelTitle", weaponPanel, string.Empty, 22,
                TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.6f, 1f));
            SetAnchoredRect(weaponTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 78f), new Vector2(880f, 26f));

            Button throwButton = CreateButton("ThrowSelfButton", weaponPanel, "throw character",
                new Vector2(0.5f, 0.5f), new Vector2(-100f, 46f), new Vector2(180f, 34f), out _);
            Button endGoButton = CreateButton("EndGoButton", weaponPanel, "end go",
                new Vector2(0.5f, 0.5f), new Vector2(100f, 46f), new Vector2(180f, 34f), out _);

            // 17 个武器按钮，3 行 × 6 列（索引 = WeaponId 枚举值）。
            var weaponButtons = new Button[WeaponCatalog.Count];
            var weaponLabels = new Text[WeaponCatalog.Count];
            for (int i = 0; i < WeaponCatalog.Count; i++)
            {
                int row = i / 6;
                int col = i % 6;
                float x = -300f + col * 148f;
                float y = 8f - row * 38f;
                weaponButtons[i] = CreateButton("WeaponButton_" + (WeaponId)i, weaponPanel, string.Empty,
                    new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(140f, 34f), out Text label);
                weaponLabels[i] = label;
            }

            // ---- BottomLeft：名册 + 血条（§4.1 / §4.5）----
            Text rosterTitle = CreateText("RosterTitle", canvas.transform, "Roster", 20,
                TextAnchor.LowerLeft, new Color(0.9f, 0.95f, 1f, 1f));
            SetAnchoredRect(rosterTitle.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 420f), new Vector2(340f, 26f));

            RectTransform rosterContainer = CreatePanel("RosterContainer", canvas.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(16f, 16f), new Vector2(340f, 400f));

            var rows = new BattleHud.RosterRowView[RosterRows];
            for (int i = 0; i < RosterRows; i++)
                rows[i] = CreateRosterRow(rosterContainer, i);

            // ---- 返回主菜单（保留 go_back 通路）----
            Button backButton = CreateButton("BackButton", canvas.transform, "返回主菜单",
                new Vector2(0f, 1f), new Vector2(96f, -20f), new Vector2(160f, 40f), out _);

            var so = new SerializedObject(hud);
            so.FindProperty("battle").objectReferenceValue = battle;
            so.FindProperty("turnManager").objectReferenceValue = turnManager;
            so.FindProperty("aimController").objectReferenceValue = aimController;
            so.FindProperty("turnHintText").objectReferenceValue = turnHint;
            so.FindProperty("teamStatusText").objectReferenceValue = teamStatus;
            so.FindProperty("weaponPanelRoot").objectReferenceValue = weaponPanel.gameObject;
            so.FindProperty("weaponPanelTitle").objectReferenceValue = weaponTitle;
            SetObjectArray(so.FindProperty("weaponButtons"), weaponButtons);
            SetObjectArray(so.FindProperty("weaponLabels"), weaponLabels);
            so.FindProperty("throwSelfButton").objectReferenceValue = throwButton;
            so.FindProperty("endGoButton").objectReferenceValue = endGoButton;
            so.FindProperty("rosterTitle").objectReferenceValue = rosterTitle;
            SetRosterRows(so.FindProperty("rosterRows"), rows);
            so.FindProperty("backButton").objectReferenceValue = backButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            // ---- 波次 I4 钩子（UI 主题）----
            // 放在全部子节点与 [SerializeField] 接线完成之后：I4 只换皮、不改接线。
            RunArtHook("BattleUiTheme.Apply", () => BattleUiTheme.Apply(canvas.gameObject));

            return hud;
        }

        static BattleHud.RosterRowView CreateRosterRow(RectTransform parent, int index)
        {
            var rowGo = new GameObject("RosterRow_" + index, typeof(RectTransform));
            var rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.SetParent(parent, false);
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(336f, 26f);
            rowRect.anchoredPosition = new Vector2(0f, -index * 28f);

            var swatch = CreateImage("Swatch", rowRect, new Vector2(10f, 18f), new Vector2(4f, -4f), new Color(0.6f, 0.6f, 0.6f, 1f));
            Text name = CreateText("NameText", rowRect, string.Empty, 17, TextAnchor.MiddleLeft, Color.white);
            SetAnchoredRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -4f), new Vector2(150f, 22f));

            Image barBg = CreateImage("BarBg", rowRect, new Vector2(120f, 12f), new Vector2(178f, -7f), new Color(0.12f, 0.12f, 0.12f, 0.9f));
            Image fill = CreateImage("BarFill", barBg.rectTransform, Vector2.zero, Vector2.zero, new Color(0.35f, 0.85f, 0.35f, 1f));
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            Text hp = CreateText("HpText", rowRect, string.Empty, 15, TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.9f, 1f));
            SetAnchoredRect(hp.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(304f, -4f), new Vector2(32f, 22f));

            return new BattleHud.RosterRowView
            {
                root = rowGo,
                teamSwatch = swatch,
                nameLabel = name,
                healthFill = fill,
                healthLabel = hp,
            };
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
            // 【关卡注入】不指定 level 资产：让 BattleController.BuildPlan 走「战役已选关卡 → 否则 fallback」
            // 分支（CampaignApi.PendingBattleLevelNumberOr(1)）。直接 Play 场景时无待战关卡 → 仍加载 level_1，
            // 与旧行为一致；从选关界面进入时才真正加载所选关卡。level_1.asset 仍由 M2DataAssetGenerator 生成备查。
            SetInt(so, "fallbackLevelNumber", LevelNumber);
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

        static void SetRosterRows(SerializedProperty arrayProp, BattleHud.RosterRowView[] rows)
        {
            if (arrayProp == null || rows == null)
                return;

            arrayProp.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("root").objectReferenceValue = rows[i].root;
                element.FindPropertyRelative("teamSwatch").objectReferenceValue = rows[i].teamSwatch;
                element.FindPropertyRelative("nameLabel").objectReferenceValue = rows[i].nameLabel;
                element.FindPropertyRelative("healthFill").objectReferenceValue = rows[i].healthFill;
                element.FindPropertyRelative("healthLabel").objectReferenceValue = rows[i].healthLabel;
            }
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
            scaler.matchWidthOrHeight = 0.5f;
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
