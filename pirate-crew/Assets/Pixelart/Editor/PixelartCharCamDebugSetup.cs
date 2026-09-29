using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PirateCrew.CharCamDebug;
using PirateCrew.Rendering.Pixelart;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **角色/镜头参数调试场**装配（`Assets/Scenes/PixelartCharCamDebug.unity`）。
    ///
    /// 【这个场景是干什么的】创始人要一个"画面中心一个角色、实时调角色体格（上下径/高度/头径）
    /// 与镜头取景（可见米数/像素档/俯仰角）"的场，在**像素化风格化渲染**下定镜头参数。
    /// 机制全部复用既有口径：像素化路径（<see cref="PixelartPathInstaller"/> + <see cref="PixelartCameraRig"/>）、
    /// 角色几何（<see cref="CrewMeshFactory"/>，分段与正式两件式同源）、材质配方（<see cref="PixelartStageKit"/>）。
    /// 本装配器只负责"把场摆出来"，参数的运行时调整与读数在 <see cref="CharCamDebugController"/>。
    ///
    /// 【内容刻意最小】地面（40×40）+ 1m 参照立方 + 2m 参照柱（给"每艺术像素多少米"一个实物标尺）
    /// + 中央一具可调角色。不放台阶/陈设——这里是量参数的台子，不是看观感的样板关（那三关已有）。
    ///
    /// 【为什么不开太阳投影】投影还在"大平面自遮挡"的调参尾巴上（见 PixelartStageKit.CreateSunAndAmbient
    /// 注释），开着会让暗面判读混进阴影偏差——量镜头参数要的是干净的色带。
    ///
    /// 【不进像素场景契约测试】`PixelartSceneContractTests` 的场景名单是显式白名单，本场景不在其中：
    /// 调试场的 pixelScale/取景就是要被实时改的，序列化值必然漂离契约——那是它的职责，不是漂移。
    ///
    /// 【运行时网格不落资产】角色两块网格由控制器运行时经 <see cref="CrewMeshFactory"/> 生成，
    /// 保存场景时作为子资产嵌进场景文件；正式角色资产（`Assets/Art/Models/Generated`）不动。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙角色镜头调试场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.PixelartCharCamDebugSetup.BuildAll</c>。
    /// </summary>
    public static class PixelartCharCamDebugSetup
    {
        const string ScenePath = "Assets/Scenes/" + CharCamDebugController.SceneName + ".unity";

        /// <summary>日志前缀（装配过程的行都带它，出问题时按前缀捞）。</summary>
        const string LogTag = "[PixelartCharCamDebugSetup]";

        const string MaterialFolder = PixelartStageKit.MaterialFolder;
        const string DitherFolder = PixelartStageKit.DitherFolder;

        [MenuItem("PirateCrew/Pixelart/烘焙角色镜头调试场景")]
        public static void BuildAll()
        {
            if (!PixelartPathInstaller.TryInstall(out int castIndex, out int screenIndex, out string error))
            {
                Debug.LogError(LogTag + " 渲染器装配失败，场景未烘焙：" + error);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(DitherFolder + "/ToonDither_0.png") == null)
                DitherPatternBaker.Bake();

            PixelartStageKit.EnsureFolder("Assets/Art/Materials");
            PixelartStageKit.EnsureFolder(MaterialFolder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- 材质（第二个参数 = 色带档数，第三个 = 是否参与描边）----------------
            Material ground = PixelartStageKit.EnsureMaterial("PixelartCharCamDebug_Ground",
                PixelartStageKit.Hex("4A6E86"), 2f, outlinePixels: 0f);
            Material prop = PixelartStageKit.EnsureMaterial("PixelartCharCamDebug_Prop",
                PixelartStageKit.Hex("D8BE8A"), 3f);
            Material crewRed = PixelartStageKit.CrewRed();
            Material crewHead = PixelartStageKit.CrewHead();

            var root = new GameObject(CharCamDebugController.SceneName);

            // ---------------- 地面（40×40：调试场只站一个角色，不需要 160m 大平原）----------------
            GameObject groundMesh = PixelartStageKit.NewPrimitive(PrimitiveType.Plane, "Ground",
                root.transform, ground);
            groundMesh.transform.localPosition = Vector3.zero;
            groundMesh.transform.localScale = new Vector3(4f, 1f, 4f);   // Plane 图元 10×10
            // 大平面只做接收者不投影：40m 地面进阴影贴图必自遮挡（CreateSunAndAmbient 注释的教训），
            // 且场内没有比它高的东西——它的"投影"只有噪声。
            groundMesh.GetComponent<MeshRenderer>().shadowCastingMode
                = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ---------------- 参照件（实物标尺：读"角色占多少艺术像素"时的 1m/2m 基准）----------------
            PixelartStageKit.AddBox(root.transform, "ReferenceCube1m",
                new Vector3(2.2f, 0.5f, 0.8f), new Vector3(1f, 1f, 1f), prop);
            PixelartStageKit.AddBox(root.transform, "ReferencePillar2m",
                new Vector3(-2.4f, 1.0f, 1.2f), new Vector3(0.6f, 2f, 0.6f), prop);

            // ---------------- 中央角色骨架（网格由控制器生成；两件式同正式角色口径）----------------
            var subject = BuildSubjectSkeleton(crewRed);

            // ---------------- 光（投影关：见类头）----------------
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform);

            // ---------------- 相机（正交；位姿初值取控制器的默认口径，运行时由控制器接管）----------------
            var camGo = new GameObject("PixelartCharCamDebugCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            camGo.transform.position = CharCamDebugController.FramingTarget
                + PixelartPilotScene.CameraDirection(
                    CharCamDebugController.DefaultPitchDegrees,
                    CharCamDebugController.DefaultAzimuthDegrees)
                    * CharCamDebugController.CameraDistance;
            camGo.transform.LookAt(CharCamDebugController.FramingTarget);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            // 参考画布下的取景（运行时由 rig 按 worldPerPixel × 艺术像素数重算）。
            camera.orthographicSize = CharCamDebugController.DefaultVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PixelartStageKit.Hex("0A0F1C");
            camGo.AddComponent<AudioListener>();

            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);   // local 恒等
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData();        // 确保存在（URP 才会认它的渲染器索引）
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.pixelScale = CharCamDebugController.DefaultPixelScale;
            rig.worldPerPixel = CharCamDebugController.WorldPerPixel(
                CharCamDebugController.DefaultVisibleMeters, CharCamDebugController.DefaultPixelScale);
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;

            // ---------------- 控制器（滑杆/读数/网格重建；引用在此一次接全）----------------
            var controller = subject.AddComponent<CharCamDebugController>();
            controller.bodyPivot = subject.transform.Find("BodyPivot");
            controller.headPivot = subject.transform.Find("HeadPivot");
            controller.bodyFilter = controller.bodyPivot != null
                ? controller.bodyPivot.GetComponent<MeshFilter>() : null;
            controller.headFilter = controller.headPivot != null
                ? controller.headPivot.GetComponent<MeshFilter>() : null;
            controller.rig = rig;
            controller.cameraTransform = camGo.transform;

            controller.ApplyAll();   // 编辑态生成网格：不开 Play 场景里也能看到角色

            AssertWiring(controller, rig);
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            PixelartStageKit.RegisterScene(ScenePath, LogTag);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogTag + " 调试场烘焙完成：" + ScenePath
                + "（Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；默认可见 " + CharCamDebugController.DefaultVisibleMeters + "m、像素档 "
                + CharCamDebugController.DefaultPixelScale + "×、俯角 "
                + CharCamDebugController.DefaultPitchDegrees + "°、方位角 "
                + CharCamDebugController.DefaultAzimuthDegrees + "°）。进场景按 F1 收起/唤出面板。");
        }

        /// <summary>
        /// 建一具两件式角色骨架（BodyPivot + HeadPivot，网格与摆位由控制器 <c>RebuildMeshes</c> 写），
        /// 站在画面中心。头顶球用共用木色，身体材质由装配器给定。
        /// </summary>
        static GameObject BuildSubjectSkeleton(Material bodyMaterial)
        {
            var subject = new GameObject("DebugSubject");
            subject.transform.SetParent(root.transform);
            subject.transform.localPosition = Vector3.zero;

            var bodyPivot = new GameObject("BodyPivot");
            bodyPivot.transform.SetParent(subject.transform, false);
            bodyPivot.AddComponent<MeshFilter>();
            bodyPivot.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

            var headPivot = new GameObject("HeadPivot");
            headPivot.transform.SetParent(subject.transform, false);
            headPivot.AddComponent<MeshFilter>();
            headPivot.AddComponent<MeshRenderer>().sharedMaterial = PixelartStageKit.CrewHead();

            return subject;
        }

        /// <summary>
        /// 接线断言：控制器与 rig 的引用字段一个都不能空。这条路失效的方式是**静默**的
        /// （面板在、滑杆动、角色没反应），装配期点名比运行时排查省事（BattleLookupWiring 同一条纪律）。
        /// </summary>
        static void AssertWiring(CharCamDebugController controller, PixelartCameraRig rig)
        {
            int missing = 0;
            if (controller.bodyPivot == null || controller.headPivot == null
                || controller.bodyFilter == null || controller.headFilter == null)
            {
                Debug.LogError(LogTag + " 控制器的角色骨架引用有空（bodyPivot/headPivot/bodyFilter/headFilter）。");
                missing++;
            }
            if (controller.rig == null || controller.cameraTransform == null || rig == null)
            {
                Debug.LogError(LogTag + " 控制器的镜头引用有空（rig/cameraTransform）。");
                missing++;
            }
            if (controller.bodyFilter != null && controller.bodyFilter.sharedMesh == null
                || controller.headFilter != null && controller.headFilter.sharedMesh == null)
            {
                Debug.LogError(LogTag + " 编辑态网格没生成（ApplyAll 未生效或 CrewMeshFactory 抛了异常）。");
                missing++;
            }

            if (missing == 0)
                Debug.Log(LogTag + " 接线断言通过：骨架/镜头引用齐全，编辑态网格已生成。");
        }
    }
}
