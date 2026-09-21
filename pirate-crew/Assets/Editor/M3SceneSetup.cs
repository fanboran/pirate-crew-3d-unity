using System.IO;
using PirateCrew.Campaign;
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
    /// 批量搭建 M3 两个场景（船员管理 / 关卡选择）并把 5 个场景写进 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/重建 M3 管理场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.M3SceneSetup.BuildAll
    ///
    /// 【产物】
    ///   Assets/Scenes/CrewManagement.unity、Assets/Scenes/LevelSelect.unity；
    ///   Build Settings = Bootstrapper(0) / MainMenu(1) / Battle(2) / CrewManagement(3) / LevelSelect(4)。
    ///
    /// 【视觉层（StickUI 复刻层，docs/UI-UX与中文本地化规范.md §3.3 / §3.4 / §3.6 线框不变）】
    ///   · 背景 = WINDOW_BG 令牌（alpha 提到 1）全屏底板；相机背景不动，只换 UI 层；
    ///   · 容器底 = <see cref="SketchPanel"/>（Dark 槽 panel）；列表行底由 M3UiBuilder 出
    ///     Light 槽 panel_light；
    ///   · 按钮 = <see cref="SketchButton"/>（Dark 为次级行动，Primary 为屏内主行动）；
    ///   · 文字层级取 <see cref="StickTokens"/> 字号档 + TEXT / TEXT_DIM / TEXT_FAINT 色阶；
    ///   · 分隔线 = <see cref="SketchSeparator"/> 波浪自绘；
    ///   · 文本统一 TMP + 中文字体（字体入口仍在 <see cref="MenuUiBuilder"/>）；
    ///   · 选关页「上一局结算」为模态弹窗（§3.6），数据源 CampaignApi（沿用
    ///     LevelSelectController 原逻辑），接线契约不变。
    /// </summary>
    public static class M3SceneSetup
    {
        const string ScenesFolder = "Assets/Scenes";

        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCenterAnchor = new Vector2(0.5f, 1f);
        static readonly Vector2 BottomCenterAnchor = new Vector2(0.5f, 0f);

        static readonly Color BackgroundColor = new Color(0.14f, 0.10f, 0.07f, 1f);

        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Scenes/重建 M3 管理场景")]
        public static void BuildAll()
        {
            EnsureFolder(ScenesFolder);

            BuildCrewManagementScene();
            BuildLevelSelectScene();
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[M3SceneSetup] M3 管理场景重建完成（StickUI 复刻层视觉）。\n"
                + "  场景: " + ScenesFolder + "/CrewManagement.unity、"
                + ScenesFolder + "/LevelSelect.unity\n"
                + "  Build Settings: Bootstrapper(0) / MainMenu(1) / Battle(2, 未重建) / "
                + "CrewManagement(3) / LevelSelect(4)");
        }

        // ------------------------------------------------------------------
        // 船员管理场景（§3.3）
        // ------------------------------------------------------------------

        static void BuildCrewManagementScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(BackgroundColor);
            Canvas canvas = CreateCanvas("CrewManagementCanvas");
            CreateEventSystem();

            TMP_FontAsset titleFont = MenuUiBuilder.TitleFont;
            TMP_FontAsset bodyFont = MenuUiBuilder.BodyFont;
            TMP_FontAsset secondaryFont = MenuUiBuilder.SecondaryFont;

            CreateWindowBackdrop(canvas.transform);

            // 文字层级：标题 FONT_TITLE + TEXT；概况 FONT_BODY + TEXT；状态 FONT_HINT + TEXT_DIM。
            TextMeshProUGUI title = M3UiBuilder.CreateText("Title", canvas.transform, UiStrings.CrewTitle,
                (int)StickTokens.FONT_TITLE, TextAlignmentOptions.Center, StickTokens.TEXT, titleFont);
            M3UiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(900f, 50f),
                new Vector2(0f, -36f));

            TextMeshProUGUI summary = M3UiBuilder.CreateText("SummaryText", canvas.transform, string.Empty,
                (int)StickTokens.FONT_BODY, TextAlignmentOptions.Center, StickTokens.TEXT, bodyFont);
            M3UiBuilder.SetAnchored(summary.rectTransform, TopCenterAnchor, new Vector2(1700f, 36f),
                new Vector2(0f, -96f));

            // 名册容器（SketchPanel Dark 槽 panel——大面板主底档；行由 CrewManagementController 运行时生成）。
            RectTransform list = CreateStickPanel("CrewList", canvas.transform,
                CenterAnchor, CenterAnchor, new Vector2(0f, 24f), new Vector2(1000f, 600f));

            TextMeshProUGUI status = M3UiBuilder.CreateText("StatusText", canvas.transform, string.Empty,
                (int)StickTokens.FONT_HINT, TextAlignmentOptions.Center, StickTokens.TEXT_DIM, secondaryFont);
            M3UiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(1700f, 36f),
                new Vector2(0f, 128f));

            // 屏内按钮：选关/返回 = Dark 次级档；保存 = Primary 主行动档。
            Button levelSelectButton = CreateSketchButton("LevelSelectButton", canvas.transform,
                UiStrings.CrewLevelSelect, BottomCenterAnchor, new Vector2(-300f, 56f), new Vector2(260f, 52f),
                bodyFont, StickTokens.SketchButtonKind.Dark);
            Button saveButton = CreateSketchButton("SaveButton", canvas.transform,
                UiStrings.CrewSave, BottomCenterAnchor, new Vector2(0f, 56f), new Vector2(260f, 52f),
                bodyFont, StickTokens.SketchButtonKind.Primary);
            Button backButton = CreateSketchButton("BackButton", canvas.transform,
                UiStrings.BackToMainMenu, BottomCenterAnchor, new Vector2(300f, 56f), new Vector2(260f, 52f),
                bodyFont, StickTokens.SketchButtonKind.Dark);

            var controllerGo = new GameObject("CrewManagementController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<CrewManagementController>();

            var so = new SerializedObject(controller);
            so.FindProperty("summaryText").objectReferenceValue = summary;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("crewListContainer").objectReferenceValue = list;
            so.FindProperty("levelSelectButton").objectReferenceValue = levelSelectButton;
            so.FindProperty("saveButton").objectReferenceValue = saveButton;
            so.FindProperty("backButton").objectReferenceValue = backButton;
            so.FindProperty("bodyFont").objectReferenceValue = bodyFont;
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, SceneNames.CrewManagement);
        }

        // ------------------------------------------------------------------
        // 关卡选择场景（§3.4）
        // ------------------------------------------------------------------

        static void BuildLevelSelectScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(BackgroundColor);
            Canvas canvas = CreateCanvas("LevelSelectCanvas");
            CreateEventSystem();

            TMP_FontAsset titleFont = MenuUiBuilder.TitleFont;
            TMP_FontAsset bodyFont = MenuUiBuilder.BodyFont;
            TMP_FontAsset secondaryFont = MenuUiBuilder.SecondaryFont;

            CreateWindowBackdrop(canvas.transform);

            // 文字层级：标题 FONT_TITLE + TEXT；统计行 FONT_HUD + TEXT；章节字 FONT_SECTION + TEXT；
            // 说明/状态 FONT_HINT + TEXT_DIM / TEXT_FAINT。
            TextMeshProUGUI title = M3UiBuilder.CreateText("Title", canvas.transform, UiStrings.LevelTitle,
                (int)StickTokens.FONT_TITLE, TextAlignmentOptions.Center, StickTokens.TEXT, titleFont);
            M3UiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(900f, 50f),
                new Vector2(0f, -32f));

            TextMeshProUGUI header = M3UiBuilder.CreateText("HeaderText", canvas.transform, string.Empty,
                (int)StickTokens.FONT_HUD, TextAlignmentOptions.Center, StickTokens.TEXT, bodyFont);
            M3UiBuilder.SetAnchored(header.rectTransform, TopCenterAnchor, new Vector2(1700f, 36f),
                new Vector2(0f, -88f));

            TextMeshProUGUI chapterName = M3UiBuilder.CreateText("ChapterNameText", canvas.transform,
                string.Empty, (int)StickTokens.FONT_SECTION, TextAlignmentOptions.Center,
                StickTokens.TEXT, secondaryFont);
            M3UiBuilder.SetAnchored(chapterName.rectTransform, TopCenterAnchor, new Vector2(1200f, 30f),
                new Vector2(0f, -128f));

            // 章节页签行（按钮由控制器运行时生成，尺寸 160×44，间距 170）。
            RectTransform chapters = M3UiBuilder.CreateRect("ChapterContainer", canvas.transform);
            M3UiBuilder.SetAnchored(chapters, TopCenterAnchor, new Vector2(520f, 44f), new Vector2(0f, -186f));

            // 海图列表容器（SketchPanel Dark 槽 panel——大面板主底档；行由控制器运行时生成）。
            RectTransform list = CreateStickPanel("LevelList", canvas.transform,
                CenterAnchor, CenterAnchor, new Vector2(0f, 52f), new Vector2(1000f, 560f));

            // 出战加载说明（文案与实际行为一致：选哪关加载哪关）+ 状态提示。
            TextMeshProUGUI hint = M3UiBuilder.CreateText("FixedArenaHint", canvas.transform,
                UiStrings.LevelStatusFixedArena, (int)StickTokens.FONT_HINT, TextAlignmentOptions.Center,
                StickTokens.TEXT_DIM, secondaryFont);
            M3UiBuilder.SetAnchored(hint.rectTransform, BottomCenterAnchor, new Vector2(1700f, 30f),
                new Vector2(0f, 96f));

            TextMeshProUGUI status = M3UiBuilder.CreateText("StatusText", canvas.transform, string.Empty,
                (int)StickTokens.FONT_HINT, TextAlignmentOptions.Center, StickTokens.TEXT_DIM, secondaryFont);
            M3UiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(1700f, 32f),
                new Vector2(0f, 128f));

            Button crewButton = CreateSketchButton("CrewButton", canvas.transform,
                UiStrings.MainCrew, BottomCenterAnchor, new Vector2(-180f, 52f), new Vector2(260f, 52f),
                bodyFont, StickTokens.SketchButtonKind.Dark);
            Button backButton = CreateSketchButton("BackButton", canvas.transform,
                UiStrings.Back, BottomCenterAnchor, new Vector2(180f, 52f), new Vector2(260f, 52f),
                bodyFont, StickTokens.SketchButtonKind.Dark);

            SettlementRefs settlement = BuildSettlementModal(canvas.transform, titleFont, bodyFont, secondaryFont);

            var controllerGo = new GameObject("LevelSelectController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<LevelSelectController>();

            var so = new SerializedObject(controller);
            so.FindProperty("headerText").objectReferenceValue = header;
            so.FindProperty("chapterNameText").objectReferenceValue = chapterName;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("chapterContainer").objectReferenceValue = chapters;
            so.FindProperty("levelListContainer").objectReferenceValue = list;
            so.FindProperty("crewButton").objectReferenceValue = crewButton;
            so.FindProperty("backButton").objectReferenceValue = backButton;
            so.FindProperty("bodyFont").objectReferenceValue = bodyFont;

            so.FindProperty("settlementModal").objectReferenceValue = settlement.Root;
            so.FindProperty("settlementTitle").objectReferenceValue = settlement.Title;
            so.FindProperty("settlementLevelText").objectReferenceValue = settlement.LevelText;
            so.FindProperty("settlementScoreText").objectReferenceValue = settlement.ScoreText;
            so.FindProperty("settlementStarsText").objectReferenceValue = settlement.StarsText;
            so.FindProperty("settlementXpText").objectReferenceValue = settlement.XpText;
            so.FindProperty("settlementUnlockText").objectReferenceValue = settlement.UnlockText;
            so.FindProperty("settlementFirstClearText").objectReferenceValue = settlement.FirstClearText;
            so.FindProperty("settlementStarRuleText").objectReferenceValue = settlement.StarRuleText;
            so.FindProperty("settlementReplayButton").objectReferenceValue = settlement.ReplayButton;
            so.FindProperty("settlementBackButton").objectReferenceValue = settlement.BackButton;

            SerializedProperty stars = so.FindProperty("settlementStars");
            stars.arraySize = settlement.Stars.Length;
            for (int i = 0; i < settlement.Stars.Length; i++)
                stars.GetArrayElementAtIndex(i).objectReferenceValue = settlement.Stars[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, SceneNames.LevelSelect);
        }

        // ------------------------------------------------------------------
        // 结算模态弹窗（§3.6）
        // ------------------------------------------------------------------

        /// <summary>结算弹窗节点集合。</summary>
        sealed class SettlementRefs
        {
            public GameObject Root;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI LevelText;
            public TextMeshProUGUI ScoreText;
            public TextMeshProUGUI StarsText;
            public TextMeshProUGUI XpText;
            public TextMeshProUGUI UnlockText;
            public TextMeshProUGUI FirstClearText;
            public TextMeshProUGUI StarRuleText;
            public Image[] Stars;
            public Button ReplayButton;
            public Button BackButton;
        }

        static SettlementRefs BuildSettlementModal(Transform canvas, TMP_FontAsset titleFont,
            TMP_FontAsset bodyFont, TMP_FontAsset secondaryFont)
        {
            var refs = new SettlementRefs();

            RectTransform root = M3UiBuilder.CreateRect("SettlementModal", canvas);
            M3UiBuilder.Stretch(root);
            refs.Root = root.gameObject;

            CreateDimOverlay(root);

            // 结算卡片（SketchPanel Dark 槽 panel——模态主底档）。
            RectTransform card = CreateStickPanel("SettlementCard", root,
                CenterAnchor, CenterAnchor, Vector2.zero, new Vector2(900f, 620f));

            // 文字层级：胜负横幅 FONT_DISPLAY + TEXT；行值 FONT_BODY + TEXT；细则 FONT_HINT + TEXT_FAINT。
            TextMeshProUGUI title = M3UiBuilder.CreateText("Title", card, string.Empty,
                (int)StickTokens.FONT_DISPLAY, TextAlignmentOptions.Center, StickTokens.TEXT, titleFont);
            M3UiBuilder.SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(800f, 60f),
                new Vector2(0f, -30f));
            refs.Title = title;

            // 星级图标（3 枚，点亮 = ACCENT 金阶；运行时由 LevelSelectController.ApplyStars 重写）。
            var stars = new Image[StarRules.MaxStars];
            for (int i = 0; i < stars.Length; i++)
            {
                RectTransform icon = M3UiBuilder.CreateRect("Star" + i, card);
                M3UiBuilder.SetAnchored(icon, new Vector2(0.5f, 1f), new Vector2(44f, 44f),
                    new Vector2((i - (stars.Length - 1) * 0.5f) * 52f, -110f));

                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = M3UiBuilder.StarIcon;
                image.raycastTarget = false;
                image.color = StickTokens.ACCENT;
                stars[i] = image;
            }
            refs.Stars = stars;

            TextMeshProUGUI starsText = M3UiBuilder.CreateText("StarsText", card, string.Empty,
                (int)StickTokens.FONT_BODY, TextAlignmentOptions.Center, StickTokens.TEXT, bodyFont);
            M3UiBuilder.SetAnchored(starsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(320f, 30f),
                new Vector2(0f, -160f));
            refs.StarsText = starsText;

            // 波浪分隔线（SketchSeparator 自绘档；高度 8 容纳 1.3px 线宽 + 羽化带）。
            SketchSeparator.Create(card, "Divider", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -196f), new Vector2(760f, 8f));

            refs.LevelText = BuildSettlementRow(card, "LevelRow", -232f, bodyFont);
            refs.ScoreText = BuildSettlementRow(card, "ScoreRow", -276f, bodyFont);
            refs.XpText = BuildSettlementRow(card, "XpRow", -320f, bodyFont);
            refs.UnlockText = BuildSettlementRow(card, "UnlockRow", -364f, bodyFont);
            refs.FirstClearText = BuildSettlementRow(card, "FirstClearRow", -408f, bodyFont);

            TextMeshProUGUI rule = M3UiBuilder.CreateText("StarRuleText", card,
                UiStrings.SettlementStarRuleHint, (int)StickTokens.FONT_HINT, TextAlignmentOptions.Center,
                StickTokens.TEXT_FAINT, secondaryFont);
            M3UiBuilder.SetAnchored(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(840f, 44f),
                new Vector2(0f, -456f));
            refs.StarRuleText = rule;

            // 弹窗按钮：再战 = Primary 主行动档；返回选图 = Dark 次级档。
            refs.ReplayButton = CreateSketchButton("ReplayButton", card, UiStrings.LevelReplay,
                new Vector2(0.5f, 0f), new Vector2(-150f, 52f), new Vector2(260f, 52f),
                bodyFont, StickTokens.SketchButtonKind.Primary);
            refs.BackButton = CreateSketchButton("BackToSelectButton", card,
                UiStrings.SettlementBackToSelect, new Vector2(0.5f, 0f), new Vector2(150f, 52f),
                new Vector2(260f, 52f), bodyFont, StickTokens.SketchButtonKind.Dark);

            root.gameObject.SetActive(false);
            return refs;
        }

        /// <summary>结算弹窗里的「左标签 + 右值」行（用整行一个左对齐文本，形如「关卡　第 3 关」）。</summary>
        static TextMeshProUGUI BuildSettlementRow(Transform card, string name, float y, TMP_FontAsset bodyFont)
        {
            TextMeshProUGUI text = M3UiBuilder.CreateText(name, card, string.Empty, (int)StickTokens.FONT_BODY,
                TextAlignmentOptions.MidlineLeft, StickTokens.TEXT, bodyFont);
            M3UiBuilder.SetAnchored(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 36f),
                new Vector2(0f, y));
            return text;
        }

        // ------------------------------------------------------------------
        // StickUI 复刻层装配出口（两屏视觉统一走 StickTokens 令牌 + Sketch 控件族；
        // 中文字体入口仍在 MenuUiBuilder，皮肤族方法不再引用）
        // ------------------------------------------------------------------

        /// <summary>全屏底板：WINDOW_BG 令牌（alpha 由 0.88 提到 1 作不透明背景）。
        /// 场景相机背景不动，只换 UI 层；不拦截点击。</summary>
        static void CreateWindowBackdrop(Transform parent)
        {
            RectTransform rect = M3UiBuilder.CreateRect("WindowBackdrop", parent);
            M3UiBuilder.Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            Color bg = StickTokens.WINDOW_BG;
            bg.a = 1f;
            image.color = bg;
            image.raycastTarget = false;
        }

        /// <summary>建 StickUI 面板底（SketchPanel：panel 槽整图 Tiled + 帧轮换沸腾；
        /// 九宫格 border=10 由导入设置携带）。tone 固定 Dark = 大面板主底档。</summary>
        static RectTransform CreateStickPanel(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            SketchPanel panel = SketchPanel.Create(parent, name, anchor, pivot, anchoredPosition,
                size, SketchPanel.Tone.Dark);
            return (RectTransform)panel.transform;
        }

        /// <summary>建 SketchButton（变体表驱动四态槽/五态字色/伪粗，调用方不另配色）。
        /// 字号取 FONT_BODY 正文档；pivot 沿用旧按钮装配口径 (0.5, 0.5)。</summary>
        static Button CreateSketchButton(string name, Transform parent, string label, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font,
            StickTokens.SketchButtonKind kind)
        {
            return SketchButton.Create(parent, name, anchor, new Vector2(0.5f, 0.5f),
                anchoredPosition, size, font, kind, label, (int)StickTokens.FONT_BODY);
        }

        /// <summary>模态压暗遮罩：MODAL_DIM 令牌；拦截点击承载模态语义。</summary>
        static void CreateDimOverlay(Transform parent)
        {
            RectTransform rect = M3UiBuilder.CreateRect("DimOverlay", parent);
            M3UiBuilder.Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = StickTokens.MODAL_DIM;
            image.raycastTarget = true;   // 挡住底下的点击，符合模态语义。
        }

        // ------------------------------------------------------------------
        // UI 构建辅助
        // ------------------------------------------------------------------

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
                Debug.LogError("[M3SceneSetup] 保存场景失败: " + path);
        }

        /// <summary>
        /// 写 5 个场景。Battle 保持 index 2（SceneLoader 与既有测试都按名字加载，顺序不影响）。
        /// </summary>
        static void RegisterBuildSettings()
        {
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
