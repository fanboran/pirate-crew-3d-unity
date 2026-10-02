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

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 批量搭建 M1 三个场景并写入 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/批量重建 M1 场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.SceneSetup.BuildAll
    ///
    /// 【产物】Assets/Scenes/Game/{Bootstrapper,MainMenu}.unity（Battle 归 BattleSceneSetup 重建）；
    ///   Build Settings 登记 6 场景（见 RegisterBuildSettings）。
    ///
    /// 【主菜单视觉口径（Beveled Pixel 像素皮）】深暖色清屏（透底语义）+ StickHand 标题
    /// （像素皮 Frame tone 浅字 + 墨色描边）+ <see cref="SketchButton"/> 菜单列
    /// （tone 九宫格 + 三态 SpriteSwap，高 BTN_H=32）+ <see cref="UiKit.EnsureWindow"/> /
    /// <see cref="UiKit.EnsurePanel"/>（Ase 直切件皮）的设置/退出确认弹窗 + <see cref="SketchSeparator"/> 蚀刻分隔线；
    /// 文字/字号仍取 <see cref="UiSkin"/> 令牌。控制器
    /// <see cref="MainMenuController"/> 的 [SerializeField] 引用契约不变（按字段名回写）。
    /// </summary>
    public static class SceneSetup
    {
        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);

        /// <summary>无头 -executeMethod 入口。</summary>
        [MenuItem("PirateCrew/Scenes/批量重建 M1 场景")]
        public static void BuildAll()
        {
            EnsureFolder(SceneNames.GameFolder);

            BuildBootstrapperScene();
            BuildMainMenuScene();
            // Battle.unity 自 M2 起归 BattleSceneSetup 全量重建（完整战斗场景），
            // 这里**不再生成 M1 占位场景**——否则会覆盖 M2 的产物（ArtGate 第⑦步的输出
            // 被第⑨步覆盖的事故由此而来）。占位场景仅存在于 M1 时代。
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SceneSetup] 菜单场景重建完成：Assets/Scenes/Game/{Bootstrapper,MainMenu}.unity（含视频设置服务与设置面板），"
                + "Build Settings 登记 6 场景。");
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

            // 清屏色 = theme 桌面（window_face #2C2C30），为窗体提供透底。
            CreateCamera(PixelSkin.Theme.Face);   // theme desktop = window_face #2C2C30

            Canvas canvas = CreateCanvas("MainMenuCanvas");
            CreateEventSystem();

            // 像素字体单字体纪律（UiKit.RuntimeFont 同口径）：全部 Fusion Pixel 位图档。
            TMP_FontAsset handFont = MenuUiBuilder.TitleFont;

            // z=0 背景：全屏一层 theme 桌面色（CreateStickBackdrop），不铺死黑、不叠压暗 vignette。
            CreateStickBackdrop(canvas.transform);

            // 主菜单**窗体化**（执行案 §4，创始人 2026-09-25 裁决）：window_with_title 容器
            // 装标题带 + 按钮列，标题进带内——废「黑底 + 文字 + 下划线」自由排版。
            // 几何（×1 设计格）：标题带 15 原生，**带下内容顶随标题字高动态**
            // （EnsureWindow 量行高：12 号 → 23，字溢出带是创始人裁决的接受态）；
            // 按钮高 24（参考库 OK 钮）；相邻钮间距 = 上钮下切片 6 + 下钮上切片 4 = 10
            // （theme 无 spacing 概念，缝 = 两者 border 相加）；下 border 6。
            RectTransform menuWindow = RuntimeUiBuilder.CreateRect("MenuWindow", canvas.transform);
            UiKit.EnsureWindow(menuWindow, PixelTone.Frame, UiStrings.MainTitle,
                handFont, UiSkin.Font.Body, helpButton: false, closeButton: false);
            float contentTop = UiKit.WindowContentTopOf(menuWindow);
            Vector2 windowSize = new Vector2(132f, contentTop + 24f * 5 + 10f * 4 + 6f);
            // 奇高 + 中心锚 → 缘落半格（横线变浅的元凶）：y 补 -0.5 回整数格
            float yNudge = Mathf.Abs(windowSize.y % 2f) > 0.01f ? -0.5f : 0f;
            MenuUiBuilder.SetAnchored(menuWindow, CenterAnchor, windowSize, new Vector2(0f, -6.5f + yNudge));

            var menuColumn = menuWindow.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (menuColumn == null)
                menuColumn = menuWindow.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            menuColumn.padding = new RectOffset(
                (int)AseLayout.Px(AseLayout.WindowBorder), (int)AseLayout.Px(AseLayout.WindowBorder),
                (int)AseLayout.Px(contentTop), (int)AseLayout.Px(AseLayout.WindowBorder));
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
            // 组件展示：主菜单直达部件陈列廊（创始人 2026-09-25「主页放一个测试场景按钮」）。
            SketchButton showcaseButton = CreateMenuButton(menuWindow.transform, "ShowcaseButton",
                UiStrings.MainShowcase, handFont);
            SketchButton quitButton = CreateMenuButton(menuWindow.transform, "QuitButton",
                UiStrings.MainQuit, handFont);

            // 左下：版本号 + 存档状态（安全边距 12；角标 Tiny = 8 原生档 + 亮字 32% 档，
            // 状态反馈用 55% 档略提一级——原 StickTokens.TEXT_FAINT/TEXT_DIM 同值，
            // 遗留层根除后落字面量）。版本数字运行时取
            // Application.version（单一真源链 BuildVersion.Current → bundleVersion）。
            TextMeshProUGUI versionText = MenuUiBuilder.CreateTextExact("VersionText", canvas.transform,
                UiStrings.MainVersionPrefix + " " + Application.version, UiSkin.Font.Tiny,
                TextAlignmentOptions.BottomLeft, new Color(0.93f, 0.94f, 0.96f, 0.32f), handFont);
            MenuUiBuilder.SetAnchored(versionText.rectTransform,
                new Vector2(0f, 0f), new Vector2(133f, 12f), new Vector2(12f, 12f));

            TextMeshProUGUI statusText = MenuUiBuilder.CreateTextExact("StatusText", canvas.transform,
                string.Empty, UiSkin.Font.Tiny, TextAlignmentOptions.BottomLeft,
                new Color(0.93f, 0.94f, 0.96f, 0.55f), handFont);
            MenuUiBuilder.SetAnchored(statusText.rectTransform,
                new Vector2(0f, 0f), new Vector2(167f, 12f),
                new Vector2(12f, 12f + 13f));

            // 设置界面（真接线：音量滑条 ×4 / 画质档 / 窗口模式；默认隐藏；theme window 直切件底板）。
            MenuUiBuilder.SettingsPanelResult settings = MenuUiBuilder.BuildSettingsPanel(canvas.transform);

            // 退出确认框（默认隐藏；正文为退出确认文案；theme window 直切件底板）。
            MenuUiBuilder.ConfirmDialogResult quitConfirm =
                MenuUiBuilder.BuildConfirmDialog(canvas.transform, UiStrings.MainQuitConfirm);

            // 控制器对象 + 序列化引用绑定（字段名契约零改动：SketchButton 是 Button 子类、
            // 窗体根 GameObject 照常赋 [SerializeField] GameObject）。
            var controllerGo = new GameObject("MainMenuController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<MainMenuController>();

            var so = new SerializedObject(controller);
            so.FindProperty("battleButton").objectReferenceValue = battleButton;
            so.FindProperty("crewButton").objectReferenceValue = crewButton;
            so.FindProperty("settingsButton").objectReferenceValue = settingsButton;
            so.FindProperty("showcaseButton").objectReferenceValue = showcaseButton;
            so.FindProperty("quitButton").objectReferenceValue = quitButton;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("versionText").objectReferenceValue = versionText;
            so.FindProperty("settingsPanel").objectReferenceValue = settings.Root;
            so.FindProperty("settingsBackButton").objectReferenceValue = settings.BackButton;
            so.FindProperty("settingsCloseButton").objectReferenceValue = settings.CloseButton;
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

        /// <summary>全屏底 = theme 桌面（`desktop` style：window_face #2C2C30）纯色 Image，
        /// raycast 关闭（装饰层，不挡主菜单按钮命中）。旧自造「窗户」黑半透底已随
        /// StickTokens 遗留层根除替换为桌面色。</summary>
        static void CreateStickBackdrop(Transform parent)
        {
            RectTransform rect = MenuUiBuilder.CreateRect("WindowBackdrop", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = PixelSkin.Theme.Face;   // theme desktop = window_face（不透明）
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
            string path = SceneNames.PathOf(sceneName);
            if (!EditorSceneManager.SaveScene(scene, path))
                Debug.LogError("[SceneSetup] 保存场景失败: " + path);
        }

        static void RegisterBuildSettings()
        {
            // 【单一真源（2026-09-28 W1C 收口）】清单只在 BuildScenes.EditorRegistrationSet 维护
            // （发行集 + UIShowcase）——三个装配器曾各持硬编码表互相覆盖（UIShowcase 被砍四次）。
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
