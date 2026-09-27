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
    /// 批量搭建管理侧两个场景（船员管理 / 关卡选择）并把 5 个场景写进 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/重建管理场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.ManagementSceneSetup.BuildAll
    ///
    /// 【产物】
    ///   Assets/Scenes/CrewManagement.unity、Assets/Scenes/LevelSelect.unity；
    ///   Build Settings = Bootstrapper(0) / MainMenu(1) / Battle(2) / CrewManagement(3) / LevelSelect(4)。
    ///
    /// 【视觉层（Beveled Pixel 像素皮，docs/UI-UX与中文本地化规范.md §3.3 / §3.4 / §3.6 线框不变）】
    ///   · 背景 = WINDOW_BG 令牌（alpha 提到 1）全屏底板；相机背景不动，只换 UI 层；
    ///   · 列表容器 = **带标题窗体**（theme window 直切件，经 <see cref="UiKit.EnsureWindow"/>，
    ///     顶 15u 标题带）+ **view 凹槽底**（theme view：sunken 九宫格，行区 padding 3/顶 4）；
    ///     列表行 = theme list_item 纯色三态（RuntimeUiBuilder.CreateRow：常态灰 / 选中金 / 禁用暗）；
    ///   · 按钮 = <see cref="SketchButton"/>（Dark 为次级行动，Primary 为屏内主行动；三态 SpriteSwap）；
    ///   · 文字层级取 <see cref="StickTokens"/> 字号档 + <see cref="PixelSkin"/> 的 tone 字色；
    ///   · 分隔线 = <see cref="SketchSeparator"/> 蚀刻线贴图（1u 厚）；
    ///   · 文本统一 TMP + 中文字体（字体入口仍在 <see cref="MenuUiBuilder"/>）；
    ///   · 选关页「上一局结算」为模态弹窗（§3.6），数据源 CampaignApi（沿用
    ///     LevelSelectController 原逻辑），接线契约不变。
    /// </summary>
    public static class ManagementSceneSetup
    {
        const string ScenesFolder = "Assets/Scenes";

        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCenterAnchor = new Vector2(0.5f, 1f);
        static readonly Vector2 BottomCenterAnchor = new Vector2(0.5f, 0f);

        static readonly Color BackgroundColor = PixelSkin.Theme.Face;   // theme 桌面/窗体面 #2C2C30（Aseprite desktop = window_face）

        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Scenes/重建管理场景")]
        public static void BuildAll()
        {
            EnsureFolder(ScenesFolder);

            BuildCrewManagementScene();
            BuildLevelSelectScene();
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ManagementSceneSetup] 管理场景重建完成（StickUI 复刻层视觉）。\n"
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

            // 文字层级：标题 Title / 概况 Body / 状态 Hint（满精度阶梯：标题与正文同 36、
            // 层级靠颜色；提示 30 = ArkPixel 10px 原生档）。
            TextMeshProUGUI title = RuntimeUiBuilder.CreateText("Title", canvas.transform, UiStrings.CrewTitle,
                UiSkin.Font.Title, TextAlignmentOptions.Center, PixelSkin.Theme.Text, titleFont);
            RuntimeUiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(300f, 15f),
                new Vector2(0f, -44f));

            TextMeshProUGUI summary = RuntimeUiBuilder.CreateText("SummaryText", canvas.transform, string.Empty,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(summary.rectTransform, TopCenterAnchor, new Vector2(567f, 15f),
                new Vector2(0f, -104f));

            // 名册容器：**带标题窗体**（theme window：顶 15u 标题带「船员名册」；行由
            // CrewManagementController 运行时生成）。166 = 6 行×16 + 行缝 5×2 + view 内缩 14 + 窗体边 46。
            // 宽 334（原 333）：中心锚 + 奇数宽 → 左右缘落 x.5 画布格（半格相位），取偶归整。
            RectTransform list = CreateTitledListPanel("CrewList", canvas.transform,
                CenterAnchor, new Vector2(0f, 8f), new Vector2(334f, 166f),
                UiStrings.CrewRosterTitle, titleFont);

            TextMeshProUGUI status = RuntimeUiBuilder.CreateText("StatusText", canvas.transform, string.Empty,
                UiSkin.Font.Hint, TextAlignmentOptions.Center, PixelSkin.Theme.TabNormalText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(567f, 12f),
                new Vector2(0f, 152f));

            // 屏内按钮：选关 / 保存 / 返回主菜单 = **一行居中**（缝 = theme 按钮左右切片相加 8），
            // 行位 BottomCenter y=72。旧版是逐枚手写坐标（-264 / -24 / +42，第三个还错位到下一行）
            // ——×2 量纲时代残留，实拍"按钮互相没对齐"的根源。
            Button[] crewButtons = CreateCenteredButtonRow(canvas.transform, "BottomActions", 72f, bodyFont,
                (UiStrings.CrewLevelSelect, MenuUiBuilder.ButtonSize(UiStrings.CrewLevelSelect)),
                (UiStrings.CrewSave, MenuUiBuilder.ButtonSize(UiStrings.CrewSave)),
                (UiStrings.BackToMainMenu, MenuUiBuilder.ButtonSize(UiStrings.BackToMainMenu)));
            Button levelSelectButton = crewButtons[0];
            Button saveButton = crewButtons[1];
            Button backButton = crewButtons[2];

            var controllerGo = new GameObject("CrewManagementController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<CrewManagementController>();

            var so = new SerializedObject(controller);
            so.FindProperty("summaryText").objectReferenceValue = summary;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("crewListContainer").objectReferenceValue = AddListContent(list);
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

            // 文字层级：标题 Title / 统计行 Hud / 章节字 Section / 说明与状态 Hint（满精度阶梯）。
            TextMeshProUGUI title = RuntimeUiBuilder.CreateText("Title", canvas.transform, UiStrings.LevelTitle,
                UiSkin.Font.Title, TextAlignmentOptions.Center, PixelSkin.Theme.Text, titleFont);
            RuntimeUiBuilder.SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(300f, 15f),
                new Vector2(0f, -44f));

            TextMeshProUGUI header = RuntimeUiBuilder.CreateText("HeaderText", canvas.transform, string.Empty,
                UiSkin.Font.Hud, TextAlignmentOptions.Center, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(header.rectTransform, TopCenterAnchor, new Vector2(567f, 15f),
                new Vector2(0f, -104f));

            TextMeshProUGUI chapterName = RuntimeUiBuilder.CreateText("ChapterNameText", canvas.transform,
                string.Empty, UiSkin.Font.Section, TextAlignmentOptions.Center,
                PixelSkin.Theme.Text, secondaryFont);
            RuntimeUiBuilder.SetAnchored(chapterName.rectTransform, TopCenterAnchor, new Vector2(400f, 15f),
                new Vector2(0f, -160f));

            // 章节页签行（容器保留契约；页签按钮已退役，当前只承载占位）。
            RectTransform chapters = RuntimeUiBuilder.CreateRect("ChapterContainer", canvas.transform);
            RuntimeUiBuilder.SetAnchored(chapters, TopCenterAnchor, new Vector2(173f, 15f), new Vector2(0f, -72f));

            // 海图列表容器：**带标题窗体**「关卡列表」；行由控制器运行时生成。
            // 240 = 10 行×16 + 行缝 9×2 + view 内缩 14 + 窗体边 46 + 余 2；中心 y=-35：
            // 顶缘离章节名 10、底缘离下方提示 13（窗体加高后曾与提示叠印，实测修正）。
            // 宽 334（原 333）：中心锚 + 奇数宽 → 半格相位，取偶归整（同船员名册窗体）。
            RectTransform list = CreateTitledListPanel("LevelList", canvas.transform,
                CenterAnchor, new Vector2(0f, -35f), new Vector2(334f, 240f),
                UiStrings.LevelListTitle, titleFont);

            // 出战加载说明（文案与实际行为一致：选哪关加载哪关）+ 状态提示。
            // y 让位加高后的列表窗体（窗体底缘 115，说明顶 102）：说明 90 / 状态 40（旧 148/104
            // 与窗体叠印，且状态再下让避免压「船员管理」钮）。
            TextMeshProUGUI hint = RuntimeUiBuilder.CreateText("FixedArenaHint", canvas.transform,
                UiStrings.LevelStatusFixedArena, UiSkin.Font.Hint, TextAlignmentOptions.Center,
                PixelSkin.Theme.TabNormalText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(hint.rectTransform, BottomCenterAnchor, new Vector2(567f, 12f),
                new Vector2(0f, 90f));

            TextMeshProUGUI status = RuntimeUiBuilder.CreateText("StatusText", canvas.transform, string.Empty,
                UiSkin.Font.Hint, TextAlignmentOptions.Center, PixelSkin.Theme.TabNormalText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(567f, 12f),
                new Vector2(0f, 40f));

            // 船员管理 / 返回 = **一行居中**（缝 8）。旧坐标（-84 / +40）分两行且第二个压状态行
            // （y=21 与状态 y=40 叠印），是 ×2 时代残留。y=56：行占 56..80，上方让出提示行
            // （y=90）、下方让出状态行（y=40）——三者互不叠印。
            Button[] levelButtons = CreateCenteredButtonRow(canvas.transform, "BottomActions", 56f, bodyFont,
                (UiStrings.MainCrew, MenuUiBuilder.ButtonSize(UiStrings.MainCrew)),
                (UiStrings.Back, MenuUiBuilder.ButtonSize(UiStrings.Back)));
            Button crewButton = levelButtons[0];
            Button backButton = levelButtons[1];

            SettlementRefs settlement = BuildSettlementModal(canvas.transform, titleFont, bodyFont, secondaryFont);

            var controllerGo = new GameObject("LevelSelectController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<LevelSelectController>();

            var so = new SerializedObject(controller);
            so.FindProperty("headerText").objectReferenceValue = header;
            so.FindProperty("chapterNameText").objectReferenceValue = chapterName;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("chapterContainer").objectReferenceValue = chapters;
            so.FindProperty("levelListContainer").objectReferenceValue = AddListContent(list);
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

            RectTransform root = RuntimeUiBuilder.CreateRect("SettlementModal", canvas);
            RuntimeUiBuilder.Stretch(root);
            refs.Root = root.gameObject;

            CreateDimOverlay(root);

            // 结算卡片（theme window_without_title 直切件 "menu"；模态主底档）。736 = 满精度标题档重排后高度。
            RectTransform card = CreateStickPanel("SettlementCard", root,
                CenterAnchor, CenterAnchor, Vector2.zero, new Vector2(300f, 245f));

            // 文字层级：胜负横幅 Display / 行值 Body / 细则 Hint（满精度阶梯）。
            TextMeshProUGUI title = RuntimeUiBuilder.CreateText("Title", card, string.Empty,
                UiSkin.Font.Display, TextAlignmentOptions.Center, PixelSkin.Theme.Text, titleFont);
            RuntimeUiBuilder.SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(800f, 44f),
                new Vector2(0f, -40f));
            refs.Title = title;

            // 星级图标（3 枚，点亮 = ACCENT 金阶；运行时由 LevelSelectController.ApplyStars 重写）。
            var stars = new Image[StarRules.MaxStars];
            for (int i = 0; i < stars.Length; i++)
            {
                RectTransform icon = RuntimeUiBuilder.CreateRect("Star" + i, card);
                RuntimeUiBuilder.SetAnchored(icon, new Vector2(0.5f, 1f), new Vector2(72f, 72f),
                    new Vector2((i - (stars.Length - 1) * 0.5f) * 84f, -136f));

                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = RuntimeUiBuilder.StarIcon;
                image.raycastTarget = false;
                image.color = PixelSkin.Theme.Selected;   // theme selected 金 #E1B85F（原 StickTokens.ACCENT 自造金）
                stars[i] = image;
            }
            refs.Stars = stars;

            TextMeshProUGUI starsText = RuntimeUiBuilder.CreateText("StarsText", card, string.Empty,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(starsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(320f, 44f),
                new Vector2(0f, -220f));
            refs.StarsText = starsText;

            // 蚀刻分隔线（SketchSeparator 像素皮档；线厚由控件内部钉到 1u=3px，高度参数不再当线宽用）。
            SketchSeparator.Create(card, "Divider", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -276f), new Vector2(759f, 3f));

            refs.LevelText = BuildSettlementRow(card, "LevelRow", -312f, bodyFont);
            refs.ScoreText = BuildSettlementRow(card, "ScoreRow", -368f, bodyFont);
            refs.XpText = BuildSettlementRow(card, "XpRow", -424f, bodyFont);
            refs.UnlockText = BuildSettlementRow(card, "UnlockRow", -480f, bodyFont);
            refs.FirstClearText = BuildSettlementRow(card, "FirstClearRow", -536f, bodyFont);

            TextMeshProUGUI rule = RuntimeUiBuilder.CreateText("StarRuleText", card,
                UiStrings.SettlementStarRuleHint, UiSkin.Font.Hint, TextAlignmentOptions.Center,
                PixelSkin.Theme.StatusText, secondaryFont);
            RuntimeUiBuilder.SetAnchored(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(840f, 36f),
                new Vector2(0f, -596f));
            refs.StarRuleText = rule;

            // 弹窗按钮：再战 / 返回选图 = 一行居中（缝 8）。旧坐标 (-120, 52) / (+84, 52) 手写，
            // 整对中心 -18 ≠ 卡心、缝 160 设计格——同"底部按钮错位"一族。
            Button[] settlementButtons = CreateCenteredButtonRow(card, "SettlementActions", 52f, bodyFont,
                (UiStrings.LevelReplay, MenuUiBuilder.ButtonSize(UiStrings.LevelReplay)),
                (UiStrings.SettlementBackToSelect, MenuUiBuilder.ButtonSize(UiStrings.SettlementBackToSelect)));
            refs.ReplayButton = settlementButtons[0];
            refs.BackButton = settlementButtons[1];

            root.gameObject.SetActive(false);
            return refs;
        }

        /// <summary>结算弹窗里的「左标签 + 右值」行（用整行一个左对齐文本，形如「关卡　第 3 关」）。</summary>
        static TextMeshProUGUI BuildSettlementRow(Transform card, string name, float y, TMP_FontAsset bodyFont)
        {
            TextMeshProUGUI text = RuntimeUiBuilder.CreateText(name, card, string.Empty, UiSkin.Font.Body,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, bodyFont);
            RuntimeUiBuilder.SetAnchored(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 44f),
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
            RectTransform rect = RuntimeUiBuilder.CreateRect("WindowBackdrop", parent);
            RuntimeUiBuilder.Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();
            Color bg = PixelSkin.Theme.Face;   // theme window_face（原 StickTokens 近黑半透自造底）
            bg.a = 1f;
            image.color = bg;
            image.raycastTarget = false;
        }

        /// <summary>建像素面板底（theme <c>window_without_title</c> 直切件 <c>"menu"</c>：
        /// 无标题面板，内容沿 <see cref="AseLayout.PopupBorder"/>=3 内缩）。</summary>
        static RectTransform CreateStickPanel(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            UiKit.EnsurePanel(rect, PixelTone.Frame);   // 皮 = Ase 直切件 "menu"（无标题面板底）
            return rect;
        }


        /// <summary>建**带标题带的列表窗体**（theme window 直切件：顶 15u 标题带随切片落位），
        /// 带内左上标题文字经 <see cref="UiKit.EnsureTitleLabel"/> 唯一入口。列表内容区走
        /// <see cref="AddListContent"/>（view 凹槽语法）。pivot 沿用面板中心口径。</summary>
        static RectTransform CreateTitledListPanel(string name, Transform parent, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size, string title, TMP_FontAsset titleFont)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = CenterAnchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            // 皮 = theme window 直切件；标题走唯一入口（带内左上、边距 5 设计格、灰字 #c0c0c0、
            // 字号 12 正文档、顶点像素对齐）——与主菜单窗体/模态/设置面板标题同源，不再本处手摆。
            UiKit.EnsureWindow(rect, PixelTone.Frame, title, titleFont, UiSkin.Font.Body,
                helpButton: false, closeButton: false);

            return rect;
        }

        /// <summary>给列表窗体加内层滚动内容区（**theme view 复刻**）：ListContent 铺窗体内容区
        /// （window_with_title border=6 / border-top=17），本体挂 <see cref="PixelSkin.Sunken"/>
        /// 凹槽九宫格作 view 皮；行 VerticalLayoutGroup 排，padding = view border（3 / 顶 4）、
        /// 行缝 1 设计格（theme listitem 相接密度）。返回控制器应接线的容器。</summary>
        static RectTransform AddListContent(RectTransform listPanel)
        {
            RectTransform content = RuntimeUiBuilder.CreateRect("ListContent", listPanel);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 0.5f);
            // 窗体内容区：theme window_with_title border=6（左/右/下）；顶 = 实际内容顶
            // （标题字大时 > 17——EnsureTitleLabel 已按字号挂带组件，防标题与列表穿模）。
            content.offsetMin = new Vector2(
                AseLayout.Px(AseLayout.WindowBorder), AseLayout.Px(AseLayout.WindowBorder));
            content.offsetMax = new Vector2(
                -AseLayout.Px(AseLayout.WindowBorder),
                -AseLayout.Px(UiKit.WindowContentTopOf(listPanel)));

            // view 皮：sunken 凹槽（theme view 的 border part = sunken_normal；列表常态不聚焦）。
            // 行画在其上（子件后画），凹槽边框自带 4u 立体感。
            var view = content.gameObject.AddComponent<Image>();
            view.sprite = PixelSkin.Sunken(false);
            view.type = Image.Type.Sliced;
            view.color = Color.white;   // 像素件禁止乘色
            view.raycastTarget = false;

            // 行区：布局组 padding = view border（theme view：内容离凹槽边 3 / 顶 4）。
            var group = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            group.childAlignment = TextAnchor.UpperCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.spacing = AseLayout.Px(1);   // 行缝 1 设计格
            group.padding = new RectOffset(
                (int)AseLayout.Px(AseLayout.ViewBorder), (int)AseLayout.Px(AseLayout.ViewBorder),
                (int)AseLayout.Px(AseLayout.ViewBorderTop), (int)AseLayout.Px(AseLayout.ViewBorder));
            return content;
        }

    /// <summary>建 SketchButton（theme button 四态皮 + 双层禁用字，调用方不另配色）。
    /// 字号取正文档（UiSkin.Font.Body）；pivot 沿用旧按钮装配口径 (0.5, 0.5)。</summary>
    /// <summary>底部按钮行的按钮间隙：theme button 左右切片各 4 设计格相加 = 8
    /// （与主菜单按钮列的纵向缝 10 = 上下切片 6+4 同一条"缝 = border 相加"口径）。</summary>
    const float ButtonRowGap = 8f;

    /// <summary>
    /// 建一行**居中排布**的底部按钮（行宽 = 各钮宽之和 + 缝，锚 BottomCenter）。
    /// 返回按钮数组，顺序同传入。
    ///
    /// 【为什么要有它】此前各屏按钮是逐枚手写 anchoredPosition（-264 / -24 / +42…），
    /// 那是 ×2 量纲时代的残留坐标：两个钮相距 184 设计格、第三个错位到下一行，
    /// 实拍就是"按钮相互没对齐"。行内排布交给 HStack(controlWidths) 后，
    /// 缝与居中都是声明式的，不会再随按钮文案宽度漂移。
    /// </summary>
    static Button[] CreateCenteredButtonRow(Transform parent, string name, float bottomOffset,
        TMP_FontAsset font, params (string label, Vector2 size)[] buttons)
    {
        float total = ButtonRowGap * (buttons.Length - 1);
        foreach ((string _, Vector2 size) in buttons)
            total += size.x;

        RectTransform row = RuntimeUiBuilder.CreateRect(name, parent);
        // pivot = anchor = (0.5, 0) → 行自 bottomOffset 起向上撑起，与旧逐钮摆放的基准同向。
        RuntimeUiBuilder.SetAnchored(row, BottomCenterAnchor, new Vector2(total, UiSkin.Px.Button),
            new Vector2(0f, bottomOffset));
        // 【必须 controlWidths: false】按钮身上没有内层布局组，若让 HStack 接管宽，
        // 布局系统会去问按钮自己的 Image（ILayoutElement）——九宫格切片件的首选宽 =
        // 切片和（≈8 格），按钮会被挤成窄条（实拍：只见字不见盒）。各钮宽由
        // MenuUiBuilder.ButtonSize 造出，组只负责按缝排布 + 整行居中。
        UiLayout.HStack(row, (int)ButtonRowGap, default(UiPadding), controlWidths: false,
            alignment: TextAnchor.MiddleCenter);

        var result = new Button[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
            result[i] = CreateSketchButton(name + i, row, buttons[i].label,
                CenterAnchor, Vector2.zero, buttons[i].size, font);
        return result;
    }

    static Button CreateSketchButton(string name, Transform parent, string label, Vector2 anchor,
        Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font)
    {
        return SketchButton.Create(parent, name, anchor, new Vector2(0.5f, 0.5f),
            anchoredPosition, size, font, label, UiSkin.Font.Body);
    }

        /// <summary>模态压暗遮罩：MODAL_DIM 令牌；拦截点击承载模态语义。</summary>
        static void CreateDimOverlay(Transform parent)
        {
            RectTransform rect = RuntimeUiBuilder.CreateRect("DimOverlay", parent);
            RuntimeUiBuilder.Stretch(rect);

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
                Debug.LogError("[ManagementSceneSetup] 保存场景失败: " + path);
        }

        /// <summary>写编辑器 Build Settings——清单单一真源在
        /// <see cref="BuildScenes.EditorRegistrationScenes"/>（发行集 + UIShowcase）。</summary>
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
