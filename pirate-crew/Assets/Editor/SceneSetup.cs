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
    /// 搭建 M1 场景并写入 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/批量重建 M1 场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.SceneSetup.BuildAll
    ///
    /// 【产物】Assets/Scenes/Game/{Bootstrapper,MainMenu}.unity。
    ///
    /// 【架构口径（UI 运行时化）】MainMenu 场景不再承载 UI 树——场景只留
    /// 相机 + EventSystem + <see cref="UiScreenBoot"/>（Boot 持有序列化的屏类型），
    /// 页面（含设置面板/退出确认）由 <see cref="UiScreenBuilder"/> 在加载时自建。
    /// Bootstrapper 保持手摆规模（组合根 + 视频设置服务）。
    /// Battle 自 M2 起归 BattleSceneSetup 全量重建，本类不再生成。
    /// </summary>
    public static class SceneSetup
    {
        /// <summary>无头 -executeMethod 入口。</summary>
        [MenuItem("PirateCrew/Scenes/批量重建 M1 场景")]
        public static void BuildAll()
        {
            EnsureFolder(SceneNames.GameFolder);

            BuildBootstrapperScene();
            BuildMainMenuScene();
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SceneSetup] 菜单场景重建完成：Assets/Scenes/Game/{Bootstrapper,MainMenu}.unity"
                + "（MainMenu = 最小场景 + UiScreenBoot，UI 运行时自建），Build Settings 登记 6 场景。");
        }

        // ------------------------------------------------------------------
        // 各场景搭建
        // ------------------------------------------------------------------

        static void BuildBootstrapperScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Bootstrapper");
            go.AddComponent<Bootstrapper>();

            // 视频设置服务（全屏 / 画质档切换）：必须持有两份 URP Asset 的序列化引用，
            // 播放器构建才会把它们（及其 Renderer）打进包里，运行时切换才有的换。
            var videoGo = new GameObject("VideoSettings");
            var video = videoGo.AddComponent<global::PirateCrew.Settings.VideoSettingsService>();
            var performant = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(
                "Assets/Settings/URP/PC_Performant_URPAsset.asset");
            var balanced = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(
                "Assets/Settings/URP/PC_Balanced_URPAsset.asset");
            if (performant == null || balanced == null)
                Debug.LogError("[SceneSetup] 未找到 URP 画质资产（Assets/Settings/URP/），视频设置将无法切画质。");
            var videoSo = new SerializedObject(video);
            videoSo.FindProperty("performantPipeline").objectReferenceValue = performant;
            videoSo.FindProperty("balancedPipeline").objectReferenceValue = balanced;
            videoSo.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, SceneNames.Bootstrapper);
        }

        static void BuildMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 清屏色 = theme 桌面（window_face #2C2C30），为窗体提供透底。
            CreateCamera(PixelSkin.Theme.Face);
            CreateEventSystem();

            var boot = new GameObject("MenuSceneBoot");
            boot.AddComponent<UiScreenBoot>().Kind = UiScreenBoot.ScreenKind.MainMenu;

            SaveScene(scene, SceneNames.MainMenu);
        }

        // ------------------------------------------------------------------
        // 场景内容辅助
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
                Debug.LogError("[SceneSetup] 保存场景失败: " + path);
        }

        static void RegisterBuildSettings()
        {
            // 【单一真源】清单只在 BuildScenes.EditorRegistrationSet 维护（发行集 + UIShowcase）。
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
