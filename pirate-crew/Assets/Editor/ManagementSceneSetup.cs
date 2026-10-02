using System.IO;
using PirateCrew.Core;
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
    /// 搭建管理侧两个场景（船员管理 / 关卡选择）并写 Build Settings。
    ///
    /// 【架构口径（UI 运行时化）】两屏不再承载 UI 树——场景只留
    /// 相机 + EventSystem + <see cref="UiScreenBoot"/>，页面（含结算模态）由
    /// <see cref="UiScreenBuilder"/> 在加载时自建。装配器只负责生成最小场景。
    /// </summary>
    public static class ManagementSceneSetup
    {
        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Scenes/重建管理场景")]
        public static void BuildAll()
        {
            EnsureFolder(SceneNames.GameFolder);

            BuildCrewManagementScene();
            BuildLevelSelectScene();
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ManagementSceneSetup] 管理场景重建完成（最小场景 + UiScreenBoot，UI 运行时自建）。\n"
                + "  场景: " + SceneNames.GameFolder + "/CrewManagement.unity、"
                + SceneNames.GameFolder + "/LevelSelect.unity");
        }

        // ------------------------------------------------------------------
        // 两屏搭建（同一形态：相机 + EventSystem + Boot，差异只在屏类型）
        // ------------------------------------------------------------------

        static void BuildCrewManagementScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(PixelSkin.Theme.Face);   // theme 桌面 = window_face #2C2C30
            CreateEventSystem();

            var boot = new GameObject("MenuSceneBoot");
            boot.AddComponent<UiScreenBoot>().Kind = UiScreenBoot.ScreenKind.CrewManagement;

            SaveScene(scene, SceneNames.CrewManagement);
        }

        static void BuildLevelSelectScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(PixelSkin.Theme.Face);
            CreateEventSystem();

            var boot = new GameObject("MenuSceneBoot");
            boot.AddComponent<UiScreenBoot>().Kind = UiScreenBoot.ScreenKind.LevelSelect;

            SaveScene(scene, SceneNames.LevelSelect);
        }

        // ------------------------------------------------------------------
        // UI 构建辅助
        // ------------------------------------------------------------------

        static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            // 【恒定像素密度】出厂档 = PixelSkin.Unit；运行期由 PixelScaleService 重写。
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = PixelSkin.Unit;

            return canvas;
        }

        static void CreateEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        static Camera CreateCamera(Color clearColor)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = clearColor;
            return camera;
        }

        // ------------------------------------------------------------------
        // 保存与 Build Settings
        // ------------------------------------------------------------------

        static void SaveScene(Scene scene, string sceneName)
        {
            string path = SceneNames.PathOf(sceneName);
            if (!EditorSceneManager.SaveScene(scene, path))
                Debug.LogError("[ManagementSceneSetup] 保存场景失败: " + path);
        }

        /// <summary>写编辑器 Build Settings——清单单一真源在
        /// <see cref="BuildScenes.EditorRegistrationScenes"/>（发行集，已含 UIShowcase）。</summary>
        static void RegisterBuildSettings()
        {
            EditorBuildSettings.scenes = BuildSystem.BuildScenes.EditorRegistrationScenes();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
