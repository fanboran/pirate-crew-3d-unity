using System.IO;
using PirateCrew.Core;
using PirateCrew.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 批量搭建 M1 三个场景并写入 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/批量重建 M1 场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.SceneSetup.BuildAll
    ///
    /// 【产物】Assets/Scenes/{Bootstrapper,MainMenu,Battle}.unity，Build Settings 顺序 0/1/2。
    ///
    /// 【本波次改造（中文化 + 海盗风重设计）】
    ///   · 主菜单按 docs/UI-UX与中文本地化规范.md §3.2 线框图重排：羊皮纸卷轴标题底板 +
    ///     木板背景 + 黄铜/木板/羊皮纸/危险红四类按钮 + 「设置」「退出游戏」两个新按钮（§4.2）；
    ///   · 文本统一 <see cref="TextMeshProUGUI"/> + 中文字体（<see cref="MenuUiBuilder"/>）；
    ///   · Battle 占位场景文案改中文「战斗场景（占位）」（§4.10 第 16 条）。
    /// </summary>
    public static class SceneSetup
    {
        const string ScenesFolder = "Assets/Scenes";

        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);

        /// <summary>无头 -executeMethod 入口。</summary>
        [MenuItem("PirateCrew/Scenes/批量重建 M1 场景")]
        public static void BuildAll()
        {
            EnsureFolder(ScenesFolder);

            BuildBootstrapperScene();
            BuildMainMenuScene();
            // Battle.unity 自 M2 起归 M2BattleSceneSetup 全量重建（完整战斗场景），
            // 这里**不再生成 M1 占位场景**——否则会覆盖 M2 的产物（ArtGate 第⑦步的输出
            // 被第⑨步覆盖的事故由此而来）。占位场景仅存在于 M1 时代。
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SceneSetup] 菜单场景重建完成：Assets/Scenes/{Bootstrapper,MainMenu}.unity（含视频设置服务与设置面板），"
                + "Build Settings 登记 5 场景。");
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
            // 放在 Bootstrapper 场景里随首场景加载，Awake 即应用持久化的设置。
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

            // 深棕暖色清屏色（木板背景之下的兜底色）。
            CreateCamera(new Color(0.14f, 0.10f, 0.07f, 1f));

            Canvas canvas = CreateCanvas("MainMenuCanvas");
            CreateEventSystem();

            TMP_FontAsset titleFont = MenuUiBuilder.TitleFont;
            TMP_FontAsset bodyFont = MenuUiBuilder.BodyFont;
            TMP_FontAsset secondaryFont = MenuUiBuilder.SecondaryFont;

            // z=0 背景：全屏木板 + 四周压暗，形成「船舱木墙」底。
            MenuUiBuilder.CreateWoodBackdrop("WoodBackdrop", canvas.transform, Color.white);
            MenuUiBuilder.CreateWoodBackdrop("Vignette", canvas.transform, new Color(0f, 0f, 0f, 0.35f));

            // 标题底板：羊皮纸卷轴 + 黄铜描边（§3.2）。
            RectTransform titleBoard = MenuUiBuilder.CreatePanel("TitleBoard", canvas.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f),
                new Vector2(760f, 120f), UiSprites.Kind.PanelParchment);
            TextMeshProUGUI title = MenuUiBuilder.CreateText("Title", titleBoard,
                UiStrings.MainTitle, UiTheme.FontDisplay, TextAlignmentOptions.Center, UiTheme.Ink, titleFont);
            MenuUiBuilder.Stretch(title.rectTransform, 8f);
            MenuUiBuilder.ApplyTitleOutline(title);

            // 5 个按钮竖排居中（§3.2：主按钮 480×64；次按钮 480×52）。
            Button battleButton = MenuUiBuilder.CreateButton("BattleButton", canvas.transform,
                UiStrings.MainBattle, CenterAnchor, new Vector2(0f, 110f), new Vector2(480f, 64f),
                bodyFont, UiSprites.Kind.ButtonBrass, UiTheme.Ink, UiTheme.FontHud);

            Button campaignButton = MenuUiBuilder.CreateButton("CampaignButton", canvas.transform,
                UiStrings.MainCampaign, CenterAnchor, new Vector2(0f, 34f), new Vector2(480f, 64f),
                bodyFont, UiSprites.Kind.ButtonWood);

            Button crewButton = MenuUiBuilder.CreateButton("CrewButton", canvas.transform,
                UiStrings.MainCrew, CenterAnchor, new Vector2(0f, -42f), new Vector2(480f, 64f),
                bodyFont, UiSprites.Kind.ButtonWood);

            Button settingsButton = MenuUiBuilder.CreateButton("SettingsButton", canvas.transform,
                UiStrings.MainSettings, CenterAnchor, new Vector2(0f, -112f), new Vector2(480f, 52f),
                bodyFont, UiSprites.Kind.ButtonParchment);

            Button quitButton = MenuUiBuilder.CreateButton("QuitButton", canvas.transform,
                UiStrings.MainQuit, CenterAnchor, new Vector2(0f, -178f), new Vector2(480f, 52f),
                bodyFont, UiSprites.Kind.ButtonDanger);

            // 左下：版本号 + 存档状态（§3.2 左下 24,24 FONT_HINT）。
            TextMeshProUGUI versionText = MenuUiBuilder.CreateText("VersionText", canvas.transform,
                UiStrings.MainVersion, UiTheme.FontHint, TextAlignmentOptions.BottomLeft,
                UiTheme.BrassLight, secondaryFont);
            MenuUiBuilder.SetAnchored(versionText.rectTransform, new Vector2(0f, 0f), new Vector2(400f, 26f),
                new Vector2(UiTheme.Safe, UiTheme.Safe));

            TextMeshProUGUI statusText = MenuUiBuilder.CreateText("StatusText", canvas.transform,
                string.Empty, UiTheme.FontHint, TextAlignmentOptions.BottomLeft, UiTheme.TextLight, secondaryFont);
            MenuUiBuilder.SetAnchored(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(500f, 26f),
                new Vector2(UiTheme.Safe, UiTheme.Safe + 28f));

            // 设置界面（真接线：音量滑条 ×4 / 画质档 / 窗口模式；默认隐藏）。
            MenuUiBuilder.SettingsPanelResult settings = MenuUiBuilder.BuildSettingsPanel(canvas.transform);

            // 退出确认框（默认隐藏；正文为退出确认文案）。
            MenuUiBuilder.ConfirmDialogResult quitConfirm =
                MenuUiBuilder.BuildConfirmDialog(canvas.transform, UiStrings.MainQuitConfirm);

            // 控制器对象 + 序列化引用绑定。
            var controllerGo = new GameObject("MainMenuController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<MainMenuController>();

            var so = new SerializedObject(controller);
            so.FindProperty("battleButton").objectReferenceValue = battleButton;
            so.FindProperty("campaignButton").objectReferenceValue = campaignButton;
            so.FindProperty("crewButton").objectReferenceValue = crewButton;
            so.FindProperty("settingsButton").objectReferenceValue = settingsButton;
            so.FindProperty("quitButton").objectReferenceValue = quitButton;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("versionText").objectReferenceValue = versionText;
            so.FindProperty("settingsPanel").objectReferenceValue = settings.Root;
            so.FindProperty("settingsBackButton").objectReferenceValue = settings.BackButton;
            so.FindProperty("settingsRestoreButton").objectReferenceValue = settings.RestoreButton;
            so.FindProperty("masterVolumeSlider").objectReferenceValue = settings.MasterSlider;
            so.FindProperty("sfxVolumeSlider").objectReferenceValue = settings.SfxSlider;
            so.FindProperty("musicVolumeSlider").objectReferenceValue = settings.MusicSlider;
            so.FindProperty("ambientVolumeSlider").objectReferenceValue = settings.AmbientSlider;
            so.FindProperty("qualityHighButton").objectReferenceValue = settings.QualityHighButton;
            so.FindProperty("qualitySmoothButton").objectReferenceValue = settings.QualitySmoothButton;
            so.FindProperty("fullscreenOnButton").objectReferenceValue = settings.FullscreenOnButton;
            so.FindProperty("fullscreenOffButton").objectReferenceValue = settings.FullscreenOffButton;
            so.FindProperty("quitConfirmPanel").objectReferenceValue = quitConfirm.Root;
            so.FindProperty("quitConfirmOkButton").objectReferenceValue = quitConfirm.OkButton;
            so.FindProperty("quitConfirmCancelButton").objectReferenceValue = quitConfirm.CancelButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, SceneNames.MainMenu);
        }

        // Battle.unity 自 M2 起归 M2BattleSceneSetup 全量重建（完整战斗场景），
        // 本类不再生成 M1 占位场景（占位场景与 BattlePlaceholder 脚本均已删除）。

        // ------------------------------------------------------------------
        // 场景内容辅助
        // ------------------------------------------------------------------

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
            // 对齐 Godot canvas_items+expand 口径：Expand(1) 外扩参考分辨率（存量场景待重建批次刷新）。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

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
            string path = ScenesFolder + "/" + sceneName + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path))
                Debug.LogError("[SceneSetup] 保存场景失败: " + path);
        }

        static void RegisterBuildSettings()
        {
            // 与 M3SceneSetup.RegisterBuildSettings 同一份 5 场景列表（幂等；顺序即 index）：
            // Bootstrapper=0（入口）、MainMenu=1、Battle=2、CrewManagement=3、LevelSelect=4。
            // SceneLoader 与既有测试都按名字加载，顺序不影响。
            string[] names =
            {
                SceneNames.Bootstrapper,
                SceneNames.MainMenu,
                SceneNames.Battle,
                SceneNames.CrewManagement,
                SceneNames.LevelSelect,
            };
            var scenes = new EditorBuildSettingsScene[names.Length];
            for (int i = 0; i < names.Length; i++)
                scenes[i] = new EditorBuildSettingsScene(ScenesFolder + "/" + names[i] + ".unity", true);

            EditorBuildSettings.scenes = scenes;
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
