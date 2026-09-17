using System.IO;
using PirateCrew.Campaign;
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
    /// 【本波次改造（中文化 + 海盗风重设计）】
    ///   · 按 docs/UI-UX与中文本地化规范.md §3.3 / §3.4 线框图重排；木板 / 羊皮纸 / 黄铜三段材质；
    ///   · 文本统一 TMP + 中文字体（<see cref="MenuUiBuilder"/>），行字号 = 正文下限 20；
    ///   · 选关页把「上一局结算」从单行横幅改为**模态弹窗**（§3.6）：星级 / 评分 / 经验 / 招募 /
    ///     首次通关 + 星级规则提示，数据源仍是 CampaignApi（沿用 LevelSelectController 原逻辑）；
    ///   · 新增自定义结算弹窗节点并接线到 <see cref="LevelSelectController"/>。
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

            Debug.Log("[M3SceneSetup] M3 管理场景重建完成（全中文 + 木板/羊皮纸/黄铜材质）。\n"
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

            MenuUiBuilder.CreateWoodBackdrop("WoodBackdrop", canvas.transform, Color.white);
            MenuUiBuilder.CreateWoodBackdrop("Vignette", canvas.transform, new Color(0f, 0f, 0f, 0.35f));

            TextMeshProUGUI title = MenuUiBuilder.CreateText("Title", canvas.transform, UiStrings.CrewTitle,
                UiTheme.FontTitle, TextAlignmentOptions.Center, UiTheme.BrassLight, titleFont);
            MenuUiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(900f, 50f),
                new Vector2(0f, -36f));
            MenuUiBuilder.ApplyTitleOutline(title);

            TextMeshProUGUI summary = MenuUiBuilder.CreateText("SummaryText", canvas.transform, string.Empty,
                UiTheme.FontBody, TextAlignmentOptions.Center, UiTheme.TextLight, bodyFont);
            MenuUiBuilder.SetAnchored(summary.rectTransform, TopCenterAnchor, new Vector2(1700f, 36f),
                new Vector2(0f, -96f));

            // 名册容器（木板底 + 黄铜框；行由 CrewManagementController 运行时生成）。
            RectTransform list = MenuUiBuilder.CreatePanel("CrewList", canvas.transform,
                CenterAnchor, CenterAnchor, new Vector2(0f, 24f), new Vector2(1000f, 600f),
                UiSprites.Kind.PanelWood);

            TextMeshProUGUI status = MenuUiBuilder.CreateText("StatusText", canvas.transform, string.Empty,
                UiTheme.FontHint, TextAlignmentOptions.Center, UiTheme.TextLight, secondaryFont);
            MenuUiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(1700f, 36f),
                new Vector2(0f, 128f));

            Button levelSelectButton = MenuUiBuilder.CreateButton("LevelSelectButton", canvas.transform,
                UiStrings.CrewLevelSelect, BottomCenterAnchor, new Vector2(-300f, 56f), new Vector2(260f, 52f),
                bodyFont, UiSprites.Kind.ButtonWood);
            Button saveButton = MenuUiBuilder.CreateButton("SaveButton", canvas.transform,
                UiStrings.CrewSave, BottomCenterAnchor, new Vector2(0f, 56f), new Vector2(260f, 52f),
                bodyFont, UiSprites.Kind.ButtonParchment);
            Button backButton = MenuUiBuilder.CreateButton("BackButton", canvas.transform,
                UiStrings.BackToMainMenu, BottomCenterAnchor, new Vector2(300f, 56f), new Vector2(260f, 52f),
                bodyFont, UiSprites.Kind.ButtonWood);

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

            MenuUiBuilder.CreateWoodBackdrop("WoodBackdrop", canvas.transform, Color.white);
            MenuUiBuilder.CreateWoodBackdrop("Vignette", canvas.transform, new Color(0f, 0f, 0f, 0.35f));

            TextMeshProUGUI title = MenuUiBuilder.CreateText("Title", canvas.transform, UiStrings.LevelTitle,
                UiTheme.FontTitle, TextAlignmentOptions.Center, UiTheme.BrassLight, titleFont);
            MenuUiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(900f, 50f),
                new Vector2(0f, -32f));
            MenuUiBuilder.ApplyTitleOutline(title);

            TextMeshProUGUI header = MenuUiBuilder.CreateText("HeaderText", canvas.transform, string.Empty,
                UiTheme.FontHud, TextAlignmentOptions.Center, UiTheme.TextLight, bodyFont);
            MenuUiBuilder.SetAnchored(header.rectTransform, TopCenterAnchor, new Vector2(1700f, 36f),
                new Vector2(0f, -88f));

            TextMeshProUGUI chapterName = MenuUiBuilder.CreateText("ChapterNameText", canvas.transform,
                string.Empty, UiTheme.FontBody, TextAlignmentOptions.Center, UiTheme.BrassLight, secondaryFont);
            MenuUiBuilder.SetAnchored(chapterName.rectTransform, TopCenterAnchor, new Vector2(1200f, 30f),
                new Vector2(0f, -128f));

            // 章节页签行（按钮由控制器运行时生成，尺寸 160×44，间距 170）。
            RectTransform chapters = MenuUiBuilder.CreateRect("ChapterContainer", canvas.transform);
            MenuUiBuilder.SetAnchored(chapters, TopCenterAnchor, new Vector2(520f, 44f), new Vector2(0f, -186f));

            // 关卡列表容器（木板底 + 黄铜框；行由控制器运行时生成）。
            RectTransform list = MenuUiBuilder.CreatePanel("LevelList", canvas.transform,
                CenterAnchor, CenterAnchor, new Vector2(0f, 52f), new Vector2(1000f, 560f),
                UiSprites.Kind.PanelWood);

            // 出战加载说明（文案与实际行为一致：选哪关加载哪关）+ 状态提示。
            TextMeshProUGUI hint = MenuUiBuilder.CreateText("FixedArenaHint", canvas.transform,
                UiStrings.LevelStatusFixedArena, UiTheme.FontHint, TextAlignmentOptions.Center,
                UiTheme.WithAlpha(UiTheme.TextLight, 0.8f), secondaryFont);
            MenuUiBuilder.SetAnchored(hint.rectTransform, BottomCenterAnchor, new Vector2(1700f, 30f),
                new Vector2(0f, 96f));

            TextMeshProUGUI status = MenuUiBuilder.CreateText("StatusText", canvas.transform, string.Empty,
                UiTheme.FontHint, TextAlignmentOptions.Center, UiTheme.BrassLight, secondaryFont);
            MenuUiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(1700f, 32f),
                new Vector2(0f, 128f));

            Button crewButton = MenuUiBuilder.CreateButton("CrewButton", canvas.transform,
                UiStrings.MainCrew, BottomCenterAnchor, new Vector2(-180f, 52f), new Vector2(260f, 52f),
                bodyFont, UiSprites.Kind.ButtonWood);
            Button backButton = MenuUiBuilder.CreateButton("BackButton", canvas.transform,
                UiStrings.Back, BottomCenterAnchor, new Vector2(180f, 52f), new Vector2(260f, 52f),
                bodyFont, UiSprites.Kind.ButtonWood);

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

            RectTransform root = MenuUiBuilder.CreateRect("SettlementModal", canvas);
            MenuUiBuilder.Stretch(root);
            refs.Root = root.gameObject;

            MenuUiBuilder.CreateDimOverlay("DimOverlay", root);

            RectTransform card = MenuUiBuilder.CreatePanel("SettlementCard", root,
                CenterAnchor, CenterAnchor, Vector2.zero, new Vector2(900f, 620f), UiSprites.Kind.PanelWood);

            TextMeshProUGUI title = MenuUiBuilder.CreateText("Title", card, string.Empty,
                UiTheme.FontBanner, TextAlignmentOptions.Center, UiTheme.BrassLight, titleFont);
            MenuUiBuilder.SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(800f, 60f),
                new Vector2(0f, -30f));
            MenuUiBuilder.ApplyTitleOutline(title);
            refs.Title = title;

            // 星级图标（3 枚，点亮 = 黄铜）。
            var stars = new Image[StarRules.MaxStars];
            for (int i = 0; i < stars.Length; i++)
            {
                RectTransform icon = MenuUiBuilder.CreateRect("Star" + i, card);
                MenuUiBuilder.SetAnchored(icon, new Vector2(0.5f, 1f), new Vector2(44f, 44f),
                    new Vector2((i - (stars.Length - 1) * 0.5f) * 52f, -110f));

                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = MenuUiBuilder.GetSprite(UiSprites.Kind.Star);
                image.raycastTarget = false;
                image.color = UiTheme.Brass;
                stars[i] = image;
            }
            refs.Stars = stars;

            TextMeshProUGUI starsText = MenuUiBuilder.CreateText("StarsText", card, string.Empty,
                UiTheme.FontBody, TextAlignmentOptions.Center, UiTheme.TextLight, bodyFont);
            MenuUiBuilder.SetAnchored(starsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(320f, 30f),
                new Vector2(0f, -160f));
            refs.StarsText = starsText;

            // 黄铜分隔线。
            RectTransform divider = MenuUiBuilder.CreateRect("Divider", card);
            MenuUiBuilder.SetAnchored(divider, new Vector2(0.5f, 1f), new Vector2(760f, 2f),
                new Vector2(0f, -196f));
            var dividerImage = divider.gameObject.AddComponent<Image>();
            dividerImage.color = UiTheme.Brass;
            dividerImage.raycastTarget = false;

            refs.LevelText = BuildSettlementRow(card, "LevelRow", -232f, bodyFont);
            refs.ScoreText = BuildSettlementRow(card, "ScoreRow", -276f, bodyFont);
            refs.XpText = BuildSettlementRow(card, "XpRow", -320f, bodyFont);
            refs.UnlockText = BuildSettlementRow(card, "UnlockRow", -364f, bodyFont);
            refs.FirstClearText = BuildSettlementRow(card, "FirstClearRow", -408f, bodyFont);

            TextMeshProUGUI rule = MenuUiBuilder.CreateText("StarRuleText", card,
                UiStrings.SettlementStarRuleHint, UiTheme.FontHint, TextAlignmentOptions.Center,
                UiTheme.WithAlpha(UiTheme.TextLight, 0.85f), secondaryFont);
            MenuUiBuilder.SetAnchored(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(840f, 44f),
                new Vector2(0f, -456f));
            refs.StarRuleText = rule;

            refs.ReplayButton = MenuUiBuilder.CreateButton("ReplayButton", card, UiStrings.LevelReplay,
                new Vector2(0.5f, 0f), new Vector2(-150f, 52f), new Vector2(260f, 52f),
                bodyFont, UiSprites.Kind.ButtonBrass, UiTheme.Ink);
            refs.BackButton = MenuUiBuilder.CreateButton("BackToSelectButton", card,
                UiStrings.SettlementBackToSelect, new Vector2(0.5f, 0f), new Vector2(150f, 52f),
                new Vector2(260f, 52f), bodyFont, UiSprites.Kind.ButtonWood);

            root.gameObject.SetActive(false);
            return refs;
        }

        /// <summary>结算弹窗里的「左标签 + 右值」行（用整行一个左对齐文本，形如「关卡　第 3 关」）。</summary>
        static TextMeshProUGUI BuildSettlementRow(Transform card, string name, float y, TMP_FontAsset bodyFont)
        {
            TextMeshProUGUI text = MenuUiBuilder.CreateText(name, card, string.Empty, UiTheme.FontBody,
                TextAlignmentOptions.MidlineLeft, UiTheme.TextLight, bodyFont);
            MenuUiBuilder.SetAnchored(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 36f),
                new Vector2(0f, y));
            return text;
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
            scaler.matchWidthOrHeight = 0.5f;

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
