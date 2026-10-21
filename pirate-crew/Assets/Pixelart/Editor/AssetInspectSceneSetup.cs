using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PirateCrew.AssetInspect;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.EditorTools.BuildSystem;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **资产特写观察调试场**装配（`Assets/Scenes/Pixelart/PixelartAssetInspect.unity`）。
    ///
    /// 【这个场景是干什么的】创始人要一个"任意游戏内资产单独上台、环绕/缩放/灯光档观察"的场
    /// （2026-10-07 提出）——渲染/建模/描边调参不再依赖进战斗场景拍图。机制全部复用既有口径：
    /// 像素化路径（<see cref="PixelartPathInstaller"/> + <see cref="PixelartCameraRig"/>）、
    /// 材质配方（<see cref="PixelartStageKit"/>）、机位方向（<see cref="PixelartPilotScene"/> 常量）。
    /// 资产装载/换装/取景的运行时逻辑在 <see cref="AssetInspectController"/>，本装配器只负责
    /// "把台子摆出来"。
    ///
    /// 【内容刻意最小】地面（40×40）+ 1m 参照立方 + 2m 参照柱（实物标尺，同角色镜头调试场）
    /// + 空的特写件挂点。不放陈设——这里是看单件的台子，不是样板关。
    ///
    /// 【为什么不开太阳投影】与 PixelartCharCamDebugSetup 同理：投影的调参尾巴会让暗面判读
    /// 混进阴影偏差；观察台默认干净色带，要看投影面板里开。
    ///
    /// 【不进像素场景契约测试】`PixelartSceneContractTests` 是显式白名单，本场景不在其中：
    /// 观察台的取景/像素档就是要被实时改的，序列化值必然漂离契约——那是它的职责，不是漂移。
    ///
    /// 入口：菜单 <c>PirateCrew/Pixelart/烘焙资产特写观察场景</c>；
    /// 无头 <c>-executeMethod PirateCrew.EditorTools.AssetInspectSceneSetup.BuildAll</c>。
    /// </summary>
    public static class AssetInspectSceneSetup
    {
        static readonly string ScenePath = BuildScenes.PathOf(AssetInspectController.SceneName);

        /// <summary>日志前缀（装配过程的行都带它，出问题时按前缀捞）。</summary>
        const string LogTag = "[AssetInspectSceneSetup]";

        const string MaterialFolder = PixelartStageKit.MaterialFolder;
        const string DitherFolder = PixelartStageKit.DitherFolder;

        /// <summary>装配期默认构图中心（运行时控制器装载资产后按包围盒重算）。</summary>
        static readonly Vector3 FramingTarget = new Vector3(0f, 0.5f, 0f);

        [MenuItem("PirateCrew/Pixelart/烘焙资产特写观察场景")]
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
            Material ground = PixelartStageKit.EnsureMaterial("PixelartAssetInspect_Ground",
                PixelartStageKit.Hex("4A6E86"), 2f, outlinePixels: 0f);
            Material prop = PixelartStageKit.EnsureMaterial("PixelartAssetInspect_Prop",
                PixelartStageKit.Hex("D8BE8A"), 3f);

            var root = new GameObject(AssetInspectController.SceneName);

            // ---------------- 地面（40×40）+ 参照件（1m 立方 / 2m 柱：实物标尺）----------------
            GameObject groundMesh = PixelartStageKit.NewPrimitive(PrimitiveType.Plane, "Ground",
                root.transform, ground);
            groundMesh.transform.localPosition = Vector3.zero;
            groundMesh.transform.localScale = new Vector3(4f, 1f, 4f);   // Plane 图元 10×10
            // 大平面只做接收者不投影（160m 教训的 40m 版：进阴影贴图必自遮挡，投影只有噪声）。
            groundMesh.GetComponent<MeshRenderer>().shadowCastingMode
                = UnityEngine.Rendering.ShadowCastingMode.Off;

            PixelartStageKit.AddBox(root.transform, "ReferenceCube1m",
                new Vector3(2.2f, 0.5f, 0.8f), new Vector3(1f, 1f, 1f), prop);
            PixelartStageKit.AddBox(root.transform, "ReferencePillar2m",
                new Vector3(-2.4f, 1.0f, 1.2f), new Vector3(0.6f, 2f, 0.6f), prop);

            // ---------------- 特写件挂点（空的；控制器装载资产时往里实例化）----------------
            var subjectGo = new GameObject("InspectSubject");
            subjectGo.transform.SetParent(root.transform);
            subjectGo.transform.localPosition = Vector3.zero;

            // ---------------- 光（投影关：见类头）----------------
            Light sun = PixelartStageKit.CreateSunAndAmbient(root.transform);

            // ---------------- 相机（正交；位姿初值取控制器默认口径，运行时由控制器接管）----------------
            var camGo = new GameObject("PixelartAssetInspectCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            camGo.transform.position = FramingTarget + PixelartPilotScene.CameraDirection(
                AssetInspectController.DefaultPitchDegrees,
                AssetInspectController.DefaultAzimuthDegrees)
                * AssetInspectController.CameraDistance;
            camGo.transform.LookAt(FramingTarget);

            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            // 参考画布下的取景（运行时由 rig 按 worldPerPixel × 艺术像素数重算）。
            camera.orthographicSize = AssetInspectController.DefaultVisibleMeters * 0.5f;
            camera.nearClipPlane = 0.3f;
            // 大件资产（云田/化工厂）半径可观，裁剪面放宽——正交相机距离只影响裁剪。
            camera.farClipPlane = 600f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PixelartStageKit.Hex("0A0F1C");
            camGo.AddComponent<AudioListener>();

            var castGo = new GameObject("Pixelart Cast Camera");
            castGo.transform.SetParent(camGo.transform, false);   // local 恒等
            var castCamera = castGo.AddComponent<Camera>();
            castCamera.GetUniversalAdditionalCameraData();        // 确保存在（URP 才会认它的渲染器索引）
            castCamera.GetUniversalAdditionalCameraData().SetRenderer(castIndex);

            var rig = camGo.AddComponent<PixelartCameraRig>();
            rig.pixelScale = AssetInspectController.DefaultPixelScale;
            rig.worldPerPixel = AssetInspectController.WorldPerPixel(
                AssetInspectController.DefaultVisibleMeters, AssetInspectController.DefaultPixelScale);
            rig.sun = sun;
            rig.castCamera = castCamera;
            rig.castRendererIndex = castIndex;
            rig.screenRendererIndex = screenIndex;

            // ---------------- 控制器（引用在此一次接全；资产清单运行时按目录扫）----------------
            var controllerGo = new GameObject("AssetInspectController");
            controllerGo.transform.SetParent(root.transform);
            var controller = controllerGo.AddComponent<AssetInspectController>();
            controller.rig = rig;
            controller.cameraTransform = camGo.transform;
            controller.sun = sun;
            controller.subjectRoot = subjectGo.transform;
            controller.sceneRoot = root.transform;

            AssertWiring(controller, rig);
            PixelartStageKit.AssertObjectShaderOnly(root, LogTag);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            PixelartStageKit.RegisterScene(ScenePath, LogTag);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogTag + " 资产特写观察场烘焙完成：" + ScenePath
                + "（Cast 渲染器 " + castIndex + " / Screen 渲染器 " + screenIndex
                + "；默认可见 " + AssetInspectController.DefaultVisibleMeters + "m、像素档 "
                + AssetInspectController.DefaultPixelScale + "×、俯角 "
                + AssetInspectController.DefaultPitchDegrees + "°、方位角 "
                + AssetInspectController.DefaultAzimuthDegrees + "°）。"
                + "进场景按 F1 收起/唤出面板，F12 截图，Esc 返回主菜单。");
        }

        /// <summary>
        /// 接线断言：控制器与 rig 的引用字段一个都不能空。这条路失效的方式是**静默**的
        /// （面板在、列表在、点了没反应），装配期点名比运行时排查省事（同 CharCamDebug 场的纪律）。
        /// </summary>
        static void AssertWiring(AssetInspectController controller, PixelartCameraRig rig)
        {
            int missing = 0;
            if (controller.rig == null || controller.cameraTransform == null
                || controller.sun == null || controller.subjectRoot == null
                || controller.sceneRoot == null || rig == null)
            {
                Debug.LogError(LogTag + " 控制器的引用有空"
                    + "（rig/cameraTransform/sun/subjectRoot/sceneRoot）。");
                missing++;
            }

            if (missing == 0)
                Debug.Log(LogTag + " 接线断言通过：控制器/镜头引用齐全。");
        }
    }
}
