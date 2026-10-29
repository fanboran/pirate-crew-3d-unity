using System.Reflection;
using PirateCrew.Battle;
using PirateCrew.CrewProbe;
using PirateCrew.Rendering.Pixelart;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **角色像素探针场**装配（`Assets/Scenes/Pixelart/PixelartCrewProbe.unity`）。
    ///
    /// 【这个场回答什么】战斗里约 10 艺术像素宽的角色在 2× 档下偶尔出现"填充—灰—墨线"的
    /// 一像素漏缝。本场把同一个 `Crew.prefab` 放到绿幕前，用**同一套实机像素管线**观察，
    /// 并把 albedo / depth / connectivity / outline / result 全部落盘。运行时逻辑在
    /// <see cref="CrewProbeController"/>，本装配器只负责摆场与接线。
    ///
    /// 【与战斗同源】
    ///   · 取景：可见 21.6m（正交 10.8）、像素档 2、俯角 30°、方位 45°、焦点抬高
    ///     `CameraFraming.LookAtHeight`；
    ///   · 光照：运行时正午档 `#FFF4E0 / 1.55 / Euler(48,140)` + Trilight 环境（0.85），
    ///     不投实时影（与战斗一致）；
    ///   · 角色：真实 `Crew.prefab`（两件式），Body 用队伍红 #FF3A29、Head 用木色 #D4A76A。
    ///
    /// 【编辑态就能看】rig 打开 `runInEditMode`：Game 视图不按 Play 也走实机六趟。
    /// 保存场景前先把 rig 关一次（OnDisable 还原主相机的 clearFlags/掩码/渲染器索引），
    /// 保存后再打开，避免把运行期的相机状态写进场景资产；资产因此以 disabled 落盘，
    /// 三个“打开观察场”菜单会在 OpenScene 后显式启用 rig。
    ///
    /// 入口：
    ///   `PirateCrew/像素探针/烘焙角色绿幕观察场`
    ///   `PirateCrew/像素探针/打开角色绿幕观察场（编辑态实机管线）`
    ///   `PirateCrew/像素探针/打开角色绿幕观察场（Play + 角度扫描）`
    ///   `PirateCrew/像素探针/抓当前帧（编辑态）`
    /// 无头：`-executeMethod PirateCrew.EditorTools.CrewProbeSceneSetup.BuildAll`
    /// </summary>
    public static class CrewProbeSceneSetup
    {
        const string ScenePath = "Assets/Scenes/Pixelart/PixelartCrewProbe.unity";
        const string LogTag = "[CrewProbeSceneSetup]";

        /// <summary>绿幕背景的 sRGB 目标；写入相机前 .linear 预补偿（渲染管线.md §6）。</summary>
        static readonly Color GreenScreen = new Color(0f, 1f, 0f, 1f);

        [MenuItem("PirateCrew/像素探针/烘焙角色绿幕观察场")]
        public static void BuildAll()
        {
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError(LogTag + " 渲染器装配失败，场景未烘焙：" + error);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                PixelartStageKit.DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            PixelartStageKit.EnsureFolder("Assets/Art/Materials");
            PixelartStageKit.EnsureFolder(PixelartStageKit.MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质 ----------------
            // 地面：单档、无抖动、无内线、不描边、priority=1（可行走地面，接触线可落地面侧）。
            Material ground = PixelartStageKit.EnsureMaterial("PixelartCrewProbe_Ground",
                PixelartStageKit.Hex("D8D8D8"), 1f, outlinePixels: 0f, aaScale: 0f);
            ground.SetFloat("_DitherStrength", 0f);
            ground.SetFloat("_NormalEdgeLevel", 0f);

            // 与战斗运行期派生材质同口径：战斗 `PixelartContentConverter` 从 CrewTeamCloth/CrewWood
            // 派生像素化材质，albedo 取 #FF3A29 / #D4A76A，**档数继承源材质的 4**（不是试点的 3）。
            Material body = PixelartStageKit.EnsureMaterial("PixelartCrewProbe_BodyRed",
                PixelartStageKit.Hex("FF3A29"), 4f);
            Material head = PixelartStageKit.EnsureMaterial("PixelartCrewProbe_Head",
                PixelartStageKit.Hex("D4A76A"), 4f);

            var root = new GameObject(CrewProbeController.SceneName);

            // ---------------- 地面：平地 / 朝相机坡 / 背相机坡 / 屋脊 ----------------
            GameObject flat = PixelartStageKit.NewPrimitive(PrimitiveType.Plane, "GroundFlat",
                root.transform, ground);
            flat.transform.localPosition = Vector3.zero;
            flat.transform.localRotation = Quaternion.identity;
            flat.transform.localScale = new Vector3(4f, 1f, 4f);   // 40m

            GameObject slopeToward = PixelartStageKit.NewPrimitive(PrimitiveType.Plane,
                "GroundSlopeTowardCamera", root.transform, ground);
            slopeToward.transform.localPosition = Vector3.zero;
            slopeToward.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            slopeToward.transform.localScale = new Vector3(4f, 1f, 4f);

            GameObject slopeAway = PixelartStageKit.NewPrimitive(PrimitiveType.Plane,
                "GroundSlopeAwayFromCamera", root.transform, ground);
            slopeAway.transform.localPosition = Vector3.zero;
            slopeAway.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            slopeAway.transform.localScale = new Vector3(4f, 1f, 4f);

            // 屋脊：两块 20×20 平面在 x=0 处对折成 V，脊线正好穿过角色脚底。
            // 平面半宽 10，旋转 ±20° 后内缘的局部 x=±10 落在 (±10·cos20, ±10·sin20)；
            // 把中心反向平移同样的量，内缘就精确落在原点。
            const float ridgeAngle = 20f;
            const float ridgeHalf = 10f;
            float ridgeCos = Mathf.Cos(ridgeAngle * Mathf.Deg2Rad);
            float ridgeSin = Mathf.Sin(ridgeAngle * Mathf.Deg2Rad);

            GameObject ridge = new GameObject("GroundRidge");
            ridge.transform.SetParent(root.transform, false);

            GameObject ridgeA = PixelartStageKit.NewPrimitive(PrimitiveType.Plane,
                "GroundRidgeA", ridge.transform, ground);
            ridgeA.transform.localRotation = Quaternion.Euler(0f, 0f, ridgeAngle);
            ridgeA.transform.localPosition = new Vector3(-ridgeHalf * ridgeCos, -ridgeHalf * ridgeSin, 0f);
            ridgeA.transform.localScale = new Vector3(2f, 1f, 2f);

            GameObject ridgeB = PixelartStageKit.NewPrimitive(PrimitiveType.Plane,
                "GroundRidgeB", ridge.transform, ground);
            ridgeB.transform.localRotation = Quaternion.Euler(0f, 0f, -ridgeAngle);
            ridgeB.transform.localPosition = new Vector3(ridgeHalf * ridgeCos, -ridgeHalf * ridgeSin, 0f);
            ridgeB.transform.localScale = new Vector3(2f, 1f, 2f);

            // 地面只接收不投影（大平面进阴影贴图必自遮挡；战斗也不投实时影）。
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                renderer.shadowCastingMode = ShadowCastingMode.Off;

            // ---------------- 角色：真实 Crew.prefab，脚底落在 y=0 ----------------
            GameObject crew = PixelartStageKit.PlaceCrew(root.transform, "ProbeCrew",
                Vector3.zero, 0f, body, head, LogTag);
            if (crew == null)
            {
                Debug.LogError(LogTag + " 船员预制体摆放失败，场景未保存。");
                return;
            }

            // ---------------- 光照：与 Battle 运行时正午档同值 ----------------
            Light sun = CreateBattleMatchedSun(root.transform);

            // ---------------- 相机 + 像素化 rig ----------------
            Vector3 focus = new Vector3(0f, CameraFraming.LookAtHeight, 0f);
            var camGo = new GameObject("PixelartCrewProbeCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            camGo.transform.position = focus + PixelartPilotScene.CameraDirection(
                CrewProbeController.PitchDegrees, CrewProbeController.BaseAzimuthDegrees)
                * CrewProbeController.CameraDistance;
            camGo.transform.LookAt(focus);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CrewProbeController.BattleVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GreenScreen.linear;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camGo.AddComponent<AudioListener>();

            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);   // local 恒等
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            // 记下主相机的原始渲染器索引（新相机通常是 -1 = 默认）。rig 的 OnDisable 只在
            // 原索引 ≥0 时回写；-1 的情况下会停在 Screen 渲染器上，所以保存前由本装配器显式还原。
            int baseRendererIndex = ReadRendererIndex(camera);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.pixelScale = CrewProbeController.BattlePixelScale;
            rig.worldPerPixel = CrewProbeController.WorldPerPixel(
                CrewProbeController.BattleVisibleMeters, CrewProbeController.BattlePixelScale);
            rig.deriveOrthographicSize = true;
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;
            rig.runInEditMode = true;

            // ---------------- 控制器 ----------------
            var controllerGo = new GameObject("CrewProbeController");
            controllerGo.transform.SetParent(root.transform);
            var controller = controllerGo.AddComponent<CrewProbeController>();
            controller.rig = rig;
            controller.cameraTransform = camGo.transform;
            controller.sun = sun;
            controller.subject = crew;
            controller.groundFlat = flat;
            controller.groundSlopeTowardCamera = slopeToward;
            controller.groundSlopeAwayFromCamera = slopeAway;
            controller.groundRidge = ridge;
            controller.visibleMeters = CrewProbeController.BattleVisibleMeters;
            controller.pixelScale = CrewProbeController.BattlePixelScale;
            controller.azimuthDegrees = CrewProbeController.BaseAzimuthDegrees;
            controller.groundVariant = CrewProbeController.GroundVariant.Flat;
            controller.background = CrewProbeController.ProbeBackground.Green;
            controller.debugView = CrewProbeController.DebugView.Final;

            AssertWiring(controller, rig);
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            // 编辑态先初始化一次，让烘焙完的场景当场可看；保存前会关掉（还原相机序列化状态）。
            rig.enabled = false;
            rig.enabled = true;
            controller.ApplyAll();

            if (!rig.IsReady)
            {
                Debug.LogWarning(LogTag + " rig.IsReady=false（无 GPU/无 compute 的环境正常）："
                    + "场景仍会保存，但编辑态 Game 视图不会出像素化画面。");
            }

            // 保存干净场景：先关 rig（OnDisable 还原主相机），显式把渲染器索引也还原成装配前的值，
            // 保存后再打开给当前会话看。否则场景资产会带着运行期的 Screen 渲染器索引。
            rig.enabled = false;
            WriteRendererIndex(camera, baseRendererIndex);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            rig.enabled = true;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(LogTag + " 角色绿幕探针场烘焙完成：" + ScenePath
                + "\n  Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；可见 " + CrewProbeController.BattleVisibleMeters + "m、像素档 "
                + CrewProbeController.BattlePixelScale + "×、俯角 " + CrewProbeController.PitchDegrees
                + "°、方位 " + CrewProbeController.BaseAzimuthDegrees + "°。"
                + "\n  编辑态直接看 Game 视图（rig.runInEditMode = true）；"
                + "选中 CrewProbeController 在 Inspector 改方位/地面/调试档。"
                + "\n  Play 后：F1 面板、F8 角度扫描、F9 抓当前帧 → export/crew-probe/。"
                + "\n  地面变体：平地 / 朝相机坡 / 背相机坡 / 屋脊（法线不连续，专测接触门控）。");
        }

        /// <summary>
        /// 主光 + 环境：数值取**运行时**正午档（AmbientTimeOfDayCatalog.Noon），不是
        /// PixelartSceneContract 的烘焙档。战斗开场 AmbientDirector 会用前者覆写后者，
        /// 探针要复刻的是玩家实际看到的画面。
        /// </summary>
        static Light CreateBattleMatchedSun(Transform parent)
        {
            var sunGo = new GameObject("ProbeSun");
            sunGo.transform.SetParent(parent.transform);
            sunGo.transform.rotation = Quaternion.Euler(48f, 140f, 0f);

            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = PixelartStageKit.Hex("FFF4E0");
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.235f, 0.222f, 0.205f);
            RenderSettings.ambientEquatorColor = new Color(0.145f, 0.132f, 0.115f);
            RenderSettings.ambientGroundColor = new Color(0.075f, 0.065f, 0.050f);
            RenderSettings.ambientIntensity = 0.85f;
            return sun;
        }

        // ------------------------------------------------------------------
        // 菜单
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/像素探针/打开角色绿幕观察场（编辑态实机管线）")]
        public static void OpenEditMode()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(ScenePath);
            EnableRigInOpenScene();
            var controller = Object.FindFirstObjectByType<CrewProbeController>();
            if (controller != null)
                Selection.activeGameObject = controller.gameObject;
            Debug.Log(LogTag + " 已打开观察场。Game 视图就是实机像素管线；"
                + "选中 CrewProbeController 改 Inspector 的方位/地面/调试档。"
                + "若画面空白，先确认 Game 视图可见、分辨率接近 1920×1080。");
        }

        [MenuItem("PirateCrew/像素探针/打开角色绿幕观察场（Play + 角度扫描）")]
        public static void OpenPlay()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(ScenePath);
            // 不在编辑态启用 rig：编辑态的 OnEnable 会建一套 DontSave 相机/缓冲，
            // 随后进 Play 容易留下陈旧相机（实拍：只剩绿背景、角色不进 G-buffer）。
            // Play 态由 CrewProbeController.Start() 启用。
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// 自动化/桥调用用：直接打开并发 Play，不弹“保存当前场景”对话框。
        /// 探针是调试场景；随后用 `CrewProbeController.StartSubpixelSweepInScene()`（桥 call）
        /// 或 Game 视图里按 F6 启动亚像素扫描。
        /// </summary>
        [MenuItem("PirateCrew/像素探针/打开并 Play（亚像素扫描）")]
        public static void OpenPlayForSweep()
        {
            EditorSceneManager.OpenScene(ScenePath);
            // 同上：Play 路径不在编辑态启用 rig。
            EditorApplication.isPlaying = true;
        }

        /// <summary>场景资产按“rig 禁用”保存（不把运行期相机状态写进资产），打开后由菜单显式启用。</summary>
        static void EnableRigInOpenScene()
        {
            PixelartCameraRig rig = Object.FindFirstObjectByType<PixelartCameraRig>();
            if (rig != null && !rig.enabled)
                rig.enabled = true;
        }

        /// <summary>桥/菜单调：退出 Play，便于重新进 Play 让新脚本生效。</summary>
        [MenuItem("PirateCrew/像素探针/停止 Play")]
        public static void StopPlay()
        {
            EditorApplication.isPlaying = false;
        }

        [MenuItem("PirateCrew/像素探针/抓当前帧（编辑态）")]
        public static void CaptureEditMode()
        {
            var controller = Object.FindFirstObjectByType<CrewProbeController>();
            if (controller == null)
            {
                Debug.LogError(LogTag + " 当前场景里没有 CrewProbeController。"
                    + "先打开 " + ScenePath + "。");
                return;
            }
            controller.CaptureNow();
        }

        // ------------------------------------------------------------------
        // 相机渲染器索引（URP 14 没有公开读取口，走 m_RendererIndex 反射；
        // 与 PixelartCameraRig.ReadRendererIndex 同一手法）
        // ------------------------------------------------------------------

        static int ReadRendererIndex(Camera camera)
        {
            if (camera == null)
                return -1;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            FieldInfo field = typeof(UniversalAdditionalCameraData).GetField(
                "m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.GetValue(data) is int index ? index : -1;
        }

        static void WriteRendererIndex(Camera camera, int index)
        {
            if (camera == null)
                return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            FieldInfo field = typeof(UniversalAdditionalCameraData).GetField(
                "m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return;
            field.SetValue(data, index);
            EditorUtility.SetDirty(data);
        }

        // ------------------------------------------------------------------
        // 接线断言
        // ------------------------------------------------------------------

        static void AssertWiring(CrewProbeController controller, PixelartCameraRig rig)
        {
            int missing = 0;
            if (controller == null || rig == null)
            {
                Debug.LogError(LogTag + " controller/rig 为空。");
                return;
            }
            if (controller.rig == null || controller.cameraTransform == null
                || controller.sun == null || controller.subject == null)
            {
                Debug.LogError(LogTag + " 控制器引用有空（rig/cameraTransform/sun/subject）。");
                missing++;
            }
            if (controller.groundFlat == null || controller.groundSlopeTowardCamera == null
                || controller.groundSlopeAwayFromCamera == null || controller.groundRidge == null)
            {
                Debug.LogError(LogTag + " 地面引用有空（flat/slopeToward/slopeAway/ridge）。");
                missing++;
            }
            if (rig.castCamera == null || rig.castRendererIndex < 0 || rig.screenRendererIndex < 0)
            {
                Debug.LogError(LogTag + " rig 的相机/渲染器索引未接全（castCamera/castIndex/screenIndex）。");
                missing++;
            }
            if (missing == 0)
                Debug.Log(LogTag + " 接线断言通过：角色/相机/地面/渲染器引用齐全。");
        }
    }
}
