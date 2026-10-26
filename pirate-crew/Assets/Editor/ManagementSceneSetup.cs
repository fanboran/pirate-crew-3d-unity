using System.IO;
using PirateCrew.Core;
using PirateCrew.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 搭建管理侧两个场景（船员管理 / 关卡选择）并写 Build Settings。
    ///
    /// 【架构口径（UI 烘焙，2026-10-26 恢复）】两屏 = 相机 + EventSystem +
    /// <c>Assets/Prefabs/UI/*Screen.prefab</c> 实例——页面（含结算模态）由
    /// <see cref="UiScreenBuilder"/> 的构建代码在**烘焙期**建成（经 <see cref="MenuScreenPrefab"/>），
    /// 打开场景即见 UI。
    /// </summary>
    public static class ManagementSceneSetup
    {
        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Scenes/重建管理场景")]
        public static void BuildAll()
        {
            // 确认闸（审计 T2/P1）：下两步各有一次 NewScene(Single)，都会把当前打开的场景
            // 直接顶掉——入口问一次，覆盖整条链。
            if (!EditorDestructiveGuard.ConfirmSceneReplacement("重建管理场景"))
                return;

            EnsureFolder(SceneNames.GameFolder);

            BuildCrewManagementScene();
            BuildLevelSelectScene();
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ManagementSceneSetup] 管理场景重建完成（Prefab/UI/*Screen 实例，UI 烘焙可见）。\n"
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

            Canvas canvas = MenuScreenPrefab.CreateCanvas("CrewManagementCanvas");
            UiScreenBuilder.CrewRefs refs = UiScreenBuilder.BuildCrewManagement(canvas.transform);
            MenuScreenPrefab.AttachController<CrewManagementController>(
                canvas.transform, "CrewManagementController", c => c.Bind(refs));
            MenuScreenPrefab.SaveAsScreenPrefab(canvas.gameObject, "CrewManagementScreen");

            SaveScene(scene, SceneNames.CrewManagement);
        }

        static void BuildLevelSelectScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(PixelSkin.Theme.Face);
            CreateEventSystem();

            Canvas canvas = MenuScreenPrefab.CreateCanvas("LevelSelectCanvas");
            UiScreenBuilder.LevelRefs refs = UiScreenBuilder.BuildLevelSelect(canvas.transform);
            MenuScreenPrefab.AttachController<LevelSelectController>(
                canvas.transform, "LevelSelectController", c => c.Bind(refs));
            MenuScreenPrefab.SaveAsScreenPrefab(canvas.gameObject, "LevelSelectScreen");

            SaveScene(scene, SceneNames.LevelSelect);
        }

        // ------------------------------------------------------------------
        // 场景基础件（画布工厂在 MenuScreenPrefab，与三屏烘焙共用）
        // ------------------------------------------------------------------

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
