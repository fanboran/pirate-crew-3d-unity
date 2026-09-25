using System.IO;
using PirateCrew.Core;
using PirateCrew.UI;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
// StickTokens 令牌是 Stick 复刻层的单一真相源，using static 提到顶层免逐处限定（同 SketchButton.cs）。
using static PirateCrew.UI.Stick.StickTokens;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 批量搭建 M1 三个场景并写入 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/批量重建 M1 场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.SceneSetup.BuildAll
    ///
    /// 【产物】Assets/Scenes/{Bootstrapper,MainMenu}.unity（Battle 归 BattleSceneSetup 重建）；
    ///   Build Settings 登记 5 场景（见 RegisterBuildSettings）。
    ///
    /// 【主菜单视觉口径（Beveled Pixel 像素皮）】深暖色清屏（透底语义）+ StickHand 标题
    /// （像素皮 Frame tone 浅字 + INK 墨描边）+ <see cref="SketchButton"/> 菜单列
    /// （tone 九宫格 + 三态 SpriteSwap，高 BTN_H=32）+ <see cref="SketchPanel"/> 底板的
    /// 设置/退出确认弹窗（Plate + 底垫投影）+ <see cref="SketchSeparator"/> 蚀刻分隔线；
    /// 文字/字号仍取 <see cref="StickTokens"/> 令牌。控制器
    /// <see cref="MainMenuController"/> 的 [SerializeField] 引用契约不变（按字段名回写）。
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
            // Battle.unity 自 M2 起归 BattleSceneSetup 全量重建（完整战斗场景），
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

            // 深棕暖色清屏色：StickUI「窗户」语义的透底——WINDOW_BG 88% 黑留 12% 让它透出，不动。
            CreateCamera(new Color(0.14f, 0.10f, 0.07f, 1f));

            Canvas canvas = CreateCanvas("MainMenuCanvas");
            CreateEventSystem();

            // 像素字体单字体纪律（UiKit.RuntimeFont 同口径）：全部 Fusion Pixel 位图档。
            TMP_FontAsset handFont = MenuUiBuilder.TitleFont;

            // z=0 背景：WINDOW_BG 原值（88% 黑）全屏一层——「窗户不是海报」，相机暖色透 12%，
            // 不铺死黑（stick-world window 底同口径；不再叠压暗 vignette 以免毁掉透底）。
            CreateStickBackdrop(canvas.transform);

            // 主菜单**窗体化**（执行案 §4，创始人 2026-09-25 裁决）：window_with_title 容器
            // 装标题带 + 按钮列，标题进带内——废「黑底 + 文字 + 下划线」自由排版。
            // 几何（×1 设计格）：标题带 15 + 带下缝 2 = 顶 17（theme window_with_title border-top）；
            // 按钮高 24（参考库 OK 钮）；相邻钮间距 = 上钮下切片 6 + 下钮上切片 4 = 10
            // （theme 无 spacing 概念，缝 = 两者 border 相加）；下 border 6。
            RectTransform menuWindow = RuntimeUiBuilder.CreateRect("MenuWindow", canvas.transform);
            Vector2 windowSize = new Vector2(132f, 17f + 24f * 4 + 10f * 3 + 6f);
            // 奇高 149 + 中心锚 → 缘落半格（横线变浅的元凶）：y 补 -0.5 回整数格
            MenuUiBuilder.SetAnchored(menuWindow, CenterAnchor, windowSize, new Vector2(0f, -6.5f));
            UiKit.EnsureWindow(menuWindow, PixelTone.Frame, UiStrings.MainTitle,
                handFont, UiSkin.Font.Body, helpButton: false, closeButton: false);

            var menuColumn = menuWindow.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (menuColumn == null)
                menuColumn = menuWindow.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            menuColumn.padding = new RectOffset(
                (int)AseLayout.Px(AseLayout.WindowBorder), (int)AseLayout.Px(AseLayout.WindowBorder),
                (int)AseLayout.Px(AseLayout.WindowBorderTop), (int)AseLayout.Px(AseLayout.WindowBorder));
            menuColumn.spacing = AseLayout.Px(10);   // 按钮下切片 6 + 按钮上切片 4
            menuColumn.childControlWidth = false;
            menuColumn.childControlHeight = false;
            menuColumn.childForceExpandWidth = false;
            menuColumn.childForceExpandHeight = false;
            menuColumn.childAlignment = TextAnchor.UpperCenter;

            // 菜单按钮列：**只有一个进游戏入口**——「进入战斗」（必经选关面板；
            // 创始人 2026-09-23：「单人战役」按钮与直跳海图的捷径已废）。
            // theme 无彩面按钮（kind 退役）：全按钮灰面，语义由文字与焦点蓝描边表达。
            SketchButton battleButton = CreateMenuButton(menuWindow.transform, "BattleButton",
                UiStrings.MainBattle, handFont);
            SketchButton crewButton = CreateMenuButton(menuWindow.transform, "CrewButton",
                UiStrings.MainCrew, handFont);
            SketchButton settingsButton = CreateMenuButton(menuWindow.transform, "SettingsButton",
                UiStrings.MainSettings, handFont);
            SketchButton quitButton = CreateMenuButton(menuWindow.transform, "QuitButton",
                UiStrings.MainQuit, handFont);

            // 左下：版本号 + 存档状态（SCREEN_MARGIN=12 安全边距；角标 Tiny 30 = ArkPixel 10px
            // 原生档 + TEXT_FAINT，状态反馈用 TEXT_DIM 略提一级）。版本数字运行时取
            // Application.version（单一真源链 BuildVersion.Current → bundleVersion）。
            TextMeshProUGUI versionText = MenuUiBuilder.CreateTextExact("VersionText", canvas.transform,
                UiStrings.MainVersionPrefix + " " + Application.version, UiSkin.Font.Tiny,
                TextAlignmentOptions.BottomLeft, TEXT_FAINT, handFont);
            MenuUiBuilder.SetAnchored(versionText.rectTransform,
                new Vector2(0f, 0f), new Vector2(133f, 12f), new Vector2(SCREEN_MARGIN, SCREEN_MARGIN));

            TextMeshProUGUI statusText = MenuUiBuilder.CreateTextExact("StatusText", canvas.transform,
                string.Empty, UiSkin.Font.Tiny, TextAlignmentOptions.BottomLeft, TEXT_DIM, handFont);
            MenuUiBuilder.SetAnchored(statusText.rectTransform,
                new Vector2(0f, 0f), new Vector2(167f, 12f),
                new Vector2(SCREEN_MARGIN, SCREEN_MARGIN + 13f));

            // 设置界面（真接线：音量滑条 ×4 / 画质档 / 窗口模式；默认隐藏；SketchPanel Dark 底板）。
            MenuUiBuilder.SettingsPanelResult settings = MenuUiBuilder.BuildSettingsPanel(canvas.transform);

            // 退出确认框（默认隐藏；正文为退出确认文案；SketchPanel Dark 底板）。
            MenuUiBuilder.ConfirmDialogResult quitConfirm =
                MenuUiBuilder.BuildConfirmDialog(canvas.transform, UiStrings.MainQuitConfirm);

            // 控制器对象 + 序列化引用绑定（字段名契约零改动：SketchButton 是 Button 子类、
            // SketchPanel 根 GameObject 照常赋 [SerializeField] GameObject）。
            var controllerGo = new GameObject("MainMenuController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<MainMenuController>();

            var so = new SerializedObject(controller);
            so.FindProperty("battleButton").objectReferenceValue = battleButton;
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

        /// <summary>
        /// 主菜单窗体按钮列的按钮（theme button 皮）：LayoutElement 声明高 24（参考库 OK 钮），
        /// 宽 = 标签宽 + 8（UiSkin.Px.ButtonWidth）；字号 0 = 控件默认正文档。
        /// </summary>
        static SketchButton CreateMenuButton(Transform parent, string name, string label, TMP_FontAsset font)
        {
            SketchButton button = SketchButton.Create(parent, name,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                MenuUiBuilder.ButtonSize(label), font, label, 0f);
            var element = button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = UiSkin.Px.Button;
            element.minHeight = UiSkin.Px.Button;
            return button;
        }

        /// <summary>全屏「窗户」底：WINDOW_BG 原值（0.88 黑）纯色 Image，raycast 关闭
        /// （装饰层，不挡主菜单按钮命中）。</summary>
        static void CreateStickBackdrop(Transform parent)
        {
            RectTransform rect = MenuUiBuilder.CreateRect("WindowBackdrop", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = WINDOW_BG;        // StickTokens.WINDOW_BG：不铺死黑，留 12% 透相机底色
            image.raycastTarget = false;
        }

        // Battle.unity 自 M2 起归 BattleSceneSetup 全量重建（完整战斗场景），
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
            // 【恒定像素密度（红警2 式）】1 画布单位 = Unit 屏幕像素，画布逻辑尺寸 = 屏幕÷Unit
            // 随分辨率生长（1440p→1280×720）——整数倍、无分数缩放。详见 BattleSceneSetup.CreateCanvas。
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
            string path = ScenesFolder + "/" + sceneName + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path))
                Debug.LogError("[SceneSetup] 保存场景失败: " + path);
        }

        static void RegisterBuildSettings()
        {
            // 与 ManagementSceneSetup.RegisterBuildSettings 同一份 5 场景列表（幂等；顺序即 index）：
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
