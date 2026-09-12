using System.IO;
using Cinemachine;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
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
    ///   §4.3（按关卡数据生成出战单位）、§4.4（落水即死 → 必须有高于水面的地面）.
    ///
    /// 【幂等】
    ///   · 预制体用 <see cref="PrefabUtility.SaveAsPrefabAsset(GameObject,string,out bool)"/> 覆盖同名资产；
    ///   · 场景用 EmptyScene 全新建后覆盖保存（不叠加旧内容）；
    ///   · 材质/文件夹按路径复用，不产生重名副本。
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
        const string PiratePrefabPath = PrefabFolder + "/PirateBase.prefab";
        const string BattleScenePath = ScenesFolder + "/Battle.unity";
        const string LevelAssetPath = "Assets/Data/Levels/level_1.asset";

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

            GameObject piratePrefab = BuildPiratePrefab();
            BuildBattleScene(piratePrefab);
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[M2BattleSceneSetup] Battle 战斗场景重建完成。\n"
                + "  场景: " + BattleScenePath + "（Build Settings index 2）\n"
                + "  预制体: " + PiratePrefabPath + "\n"
                + "  关卡数据: " + LevelAssetPath + "（回退 LevelCatalog.Get(" + LevelNumber + ")）\n"
                + "  接线: BattleController / TurnManager / AimThrowController / TrajectoryPreview / "
                + "BattleCameraController / BattleHud 的全部 [SerializeField] 引用。");
        }

        // ------------------------------------------------------------------
        // 预制体
        // ------------------------------------------------------------------

        /// <summary>
        /// 程序化创建 PirateBase 预制体：Cube 视觉（12×16px → 0.375×0.5 单位，1 单位 = 32px）
        /// + BoxCollider + Rigidbody + <see cref="PirateBase"/>。
        /// </summary>
        static GameObject BuildPiratePrefab()
        {
            // 在临时空场景里搭，随后 NewScene 建 Battle 时会一并丢弃。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PirateBase";
            // §4.1：AABB 12×16px（left/rightExtent=6、top/bottomExtent=8）；1 单位 = 32px。
            go.transform.localScale = new Vector3(12f / 32f, 16f / 32f, 12f / 32f);

            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/PirateBody.mat", "PirateBody",
                "Universal Render Pipeline/Lit", new Color(0.85f, 0.82f, 0.7f, 1f), "Standard");

            var body = go.AddComponent<Rigidbody>();
            body.mass = 1f;          // §4.1 weight = 1（击退不乘体重，质量仅给 PhysX 用）
            body.drag = 0f;
            body.angularDrag = 0.05f;

            var collider = go.GetComponent<BoxCollider>();
            var pirate = go.AddComponent<PirateBase>();

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

            // 关卡数据驱动尺寸/水位（§4.3 / §5.5）。
            LevelData data = LevelCatalog.Get(LevelNumber);
            float waterWorldY = LevelGeometry.WaterWorldY(data.WaterY);
            float worldWidth = data.WidthTiles;

            // 深蓝清屏色；相机置于 -Z 侧、identity 旋转即看向 +Z 方向（场景原点）。
            Camera camera = CreateCamera(new Color(0.02f, 0.05f, 0.12f, 1f));
            camera.gameObject.AddComponent<CinemachineBrain>();
            camera.transform.position = new Vector3(worldWidth * 0.5f, waterWorldY + 6f, -10f);
            camera.transform.rotation = Quaternion.identity;

            Transform cameraTarget = new GameObject("CameraTarget").transform;
            cameraTarget.position = new Vector3(worldWidth * 0.5f, waterWorldY + 6f, 0f);
            CinemachineVirtualCamera virtualCamera = CreateVirtualCamera(cameraTarget);

            CreateDirectionalLight();

            // 水位面正下方是地面平台：保证 §4.4 落水即死的全局规则只在真的掉出平台时触发，
            // 同时让冒烟测试里角色能落地静止、inactivity 能推进回合。
            Transform ground = CreateGround(waterWorldY, worldWidth);
            Transform water = CreateWaterPlane(waterWorldY, worldWidth);

            Transform team0Root = new GameObject("Team0_Red").transform;
            Transform team1Root = new GameObject("Team1_Blue").transform;

            // 规则宿主。
            var turnManager = new GameObject("TurnManager").AddComponent<TurnManager>();
            var aimController = new GameObject("AimThrowController").AddComponent<AimThrowController>();
            var battleCamera = new GameObject("BattleCameraController").AddComponent<BattleCameraController>();
            TrajectoryPreview trajectory = CreateTrajectoryPreview();
            var battle = new GameObject("BattleController").AddComponent<BattleController>();

            BattleHud hud = BuildHud(battle, turnManager, aimController);

            WireBattleController(battle, piratePrefab, team0Root, team1Root, water, turnManager, aimController, battleCamera);
            WireTurnManager(turnManager, battle);
            WireAimController(aimController, camera, battle, trajectory);
            WireBattleCamera(battleCamera, virtualCamera, cameraTarget, camera);
            WireHud(hud, battle, turnManager, aimController);

            // 供 Debug 查看的层级整理（不影响逻辑引用）。
            battle.transform.position = Vector3.zero;

            if (!EditorSceneManager.SaveScene(scene, BattleScenePath))
                Debug.LogError("[M2BattleSceneSetup] 保存场景失败: " + BattleScenePath);
        }

        // ------------------------------------------------------------------
        // 场景内容
        // ------------------------------------------------------------------

        static Camera CreateCamera(Color clearColor)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = clearColor;
            return camera;
        }

        static CinemachineVirtualCamera CreateVirtualCamera(Transform follow)
        {
            var go = new GameObject("BattleVCam");
            var vcam = go.AddComponent<CinemachineVirtualCamera>();
            vcam.Priority = 20;
            vcam.Follow = follow;

            // 参照库 rts-camera-cinemachine 的 "CameraTarget 中转"：vcam 跟随空目标，
            // BattleCameraController 只平滑移动该目标（锁定 XY、保持固定 z 深度）。
            var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetWithWorldUp;
            transposer.m_FollowOffset = new Vector3(0f, 0f, -10f);
            transposer.m_XDamping = 0f;
            transposer.m_YDamping = 0f;
            transposer.m_ZDamping = 0f;

            // 战斗平面是 XY 侧视，用正交相机（对应原版 2D 侧视）。
            LensSettings lens = vcam.m_Lens;
            lens.Orthographic = true;
            lens.OrthographicSize = 10f;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 100f;
            vcam.m_Lens = lens;

            go.transform.position = new Vector3(follow.position.x, follow.position.y, -10f);
            go.transform.rotation = Quaternion.identity;
            return vcam;
        }

        static void CreateDirectionalLight()
        {
            var go = new GameObject("Directional Light", typeof(Light));
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        /// <summary>
        /// 地面平台：Cube 顶面 = 水位 + 0.5（略高于水面），使角色落地后不会触发落水即死。
        /// 平台向后（+z）留出厚度，正面 z 与战斗平面重叠以保证碰撞。
        /// </summary>
        static Transform CreateGround(float waterWorldY, float worldWidth)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(worldWidth * 0.5f, waterWorldY, 0.5f);
            ground.transform.localScale = new Vector3(worldWidth + 4f, 1f, 4f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = EnsureMaterial(
                MaterialFolder + "/BattleGround.mat", "BattleGround",
                "Universal Render Pipeline/Lit", new Color(0.35f, 0.35f, 0.35f, 1f), "Standard");
            return ground.transform;
        }

        /// <summary>水位面：薄 Quad 贴在水位世界 Y 上，放在地面之前保证可见；无碰撞体（纯视觉）。</summary>
        static Transform CreateWaterPlane(float waterWorldY, float worldWidth)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
            water.name = "Water";
            water.transform.position = new Vector3(worldWidth * 0.5f, waterWorldY, -2.2f);
            water.transform.localScale = new Vector3(worldWidth + 4f, 0.3f, 1f);
            water.transform.rotation = Quaternion.identity;

            var collider = water.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            water.GetComponent<MeshRenderer>().sharedMaterial = EnsureMaterial(
                MaterialFolder + "/BattleWater.mat", "BattleWater",
                "Sprites/Default", new Color(0.15f, 0.45f, 0.75f, 0.55f));
            return water.transform;
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
            TurnManager turnManager, AimThrowController aimController, BattleCameraController battleCamera)
        {
            var prefabComponent = piratePrefab != null ? piratePrefab.GetComponent<PirateBase>() : null;
            if (prefabComponent == null)
                Debug.LogError("[M2BattleSceneSetup] PirateBase 预制体缺失 PirateBase 组件，BattleController.piratePrefab 无法接线。");

            var so = new SerializedObject(battle);
            SetRef(so, "level", AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath));
            SetInt(so, "fallbackLevelNumber", LevelNumber);
            SetRef(so, "piratePrefab", prefabComponent);
            SetRef(so, "team0Root", team0Root);
            SetRef(so, "team1Root", team1Root);
            SetRef(so, "waterPlane", waterPlane);
            SetRef(so, "turnManager", turnManager);
            SetRef(so, "aimController", aimController);
            SetRef(so, "battleCamera", battleCamera);
            SetBool(so, "team1IsAi", true);
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

        static void WireBattleCamera(BattleCameraController controller, CinemachineVirtualCamera virtualCamera, Transform cameraTarget, Camera fallbackCamera)
        {
            var so = new SerializedObject(controller);
            SetRef(so, "virtualCamera", virtualCamera);
            SetRef(so, "cameraTarget", cameraTarget);
            SetRef(so, "fallbackCamera", fallbackCamera);
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

        static Material EnsureMaterial(string path, string name, string shaderName, Color color, string fallbackShaderName = null)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find(shaderName);
            if (shader == null && !string.IsNullOrEmpty(fallbackShaderName))
                shader = Shader.Find(fallbackShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[M2BattleSceneSetup] 未找到 shader " + shaderName + "，退回 Standard。");
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader) { name = name };
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            AssetDatabase.CreateAsset(material, path);
            return material;
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
