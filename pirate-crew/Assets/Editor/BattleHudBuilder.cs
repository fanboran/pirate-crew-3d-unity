using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 重建器：按 docs/UI-UX与中文本地化规范.md §3.5 线框图搭出全中文 HUD。
    ///
    /// 【谁调用】<see cref="BattleUiTheme.Apply"/>——它由 <c>M2BattleSceneSetup.BuildHud</c> 在
    /// 既有 HUD 与接线完成后回调；本构建器**先清掉旧 HUD 子节点**再重建，
    /// 属于规范 §3.5「TopRight 拆顶部信息条 / 武器面板改滚动列表 / 名册行中文与字号提档」的落地。
    ///
    /// 【视觉】木板 / 羊皮纸 / 黄铜三段材质语言（§1.2）：面板 = 深木底 + 黄铜描边，
    /// 按钮 = 木板九宫格 + 四态，文字 = TMP 中文字体，配色取 §1.3 Token。
    ///
    /// 【节点契约】返回的 <see cref="Result"/> 必须覆盖 <see cref="BattleHud"/> 的全部
    /// <c>[SerializeField]</c> 文本/按钮/名册引用，由 <see cref="BattleUiTheme"/> 重新接线。
    /// 名册/武器数组的长度与索引口径保持不变（3D 场景装配测试会断言 17 槽 / 12 行）。
    /// </summary>
    public static class BattleHudBuilder
    {
        /// <summary>武器槽位（= WeaponCatalog.Count，索引 = WeaponId 枚举值）。</summary>
        const int WeaponSlots = 17;

        /// <summary>名册行数（与 BattleHud.MaxRosterRows 对齐）。</summary>
        const int RosterRows = 12;

        /// <summary>小地图面板名（必须与 HudMinimapSceneSetup.MinimapPanelName 一致，且为 Canvas 直接子节点）。</summary>
        public const string MinimapPanelName = "MinimapPanel";

        const float Safe = 24f;

        /// <summary>构建产物：全部需要回写给 BattleHud 的引用。</summary>
        public sealed class Result
        {
            public GameObject weaponPanelRoot;
            public TextMeshProUGUI turnHintText;
            public TextMeshProUGUI teamStatusText;
            public TextMeshProUGUI weaponPanelTitle;
            public TextMeshProUGUI rosterTitle;
            public Button[] weaponButtons;
            public MaskableGraphic[] weaponLabels;
            public Button throwSelfButton;
            public Button endGoButton;
            public Button backButton;
            public BattleHud.RosterRowView[] rosterRows;
        }

        /// <summary>在 <paramref name="canvas"/> 下重建整套 HUD 节点（不负责清旧节点，由调用方处理）。</summary>
        public static Result Build(GameObject canvas)
        {
            MenuUiBuilder.EnsureFonts();
            TMP_FontAsset title = MenuUiBuilder.TitleFont;
            TMP_FontAsset body = MenuUiBuilder.BodyFont;
            TMP_FontAsset secondary = MenuUiBuilder.SecondaryFont;

            Transform canvasTransform = canvas.transform;
            RectTransform hudRoot = MenuUiBuilder.CreateRect("HudLayout", canvasTransform);
            MenuUiBuilder.Stretch(hudRoot);

            var result = new Result();

            BuildTopBar(canvasTransform, hudRoot, title, body, secondary, result);
            BuildCrosshair(hudRoot);
            BuildRoster(hudRoot, title, body, secondary, result);
            BuildWeaponPanel(hudRoot, title, body, secondary, result);
            BuildLabelsAndHints(hudRoot, body, secondary);

            return result;
        }

        // ------------------------------------------------------------------
        // 顶部：小地图 / 模式开关 / 回合与计时 / 双方存活
        // ------------------------------------------------------------------

        static void BuildTopBar(Transform canvas, Transform hudRoot, TMP_FontAsset title,
            TMP_FontAsset body, TMP_FontAsset secondary, Result result)
        {
            BuildMinimap(canvas, secondary);

            // 模式开关（顶部中央，420×48；本轮无逻辑驱动，做静态二态展示，不挂 Button 以免出现「按了没反应」）。
            RectTransform modePanel = MenuUiBuilder.CreatePanel("ModeToggle", hudRoot,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Safe),
                new Vector2(420f, 48f), UiSprites.Kind.PanelWood);
            BuildToggleSegment(modePanel, "MoveSegment", UiStrings.BattleModeMove, -105f,
                UiSprites.Kind.ButtonBrass, UiTheme.Ink, body);
            BuildToggleSegment(modePanel, "ActionSegment", UiStrings.BattleModeAction, 105f,
                UiSprites.Kind.ButtonWood, UiTheme.TextLight, body);

            // 回合计数（模式开关左侧）与计时（右侧）。
            TextMeshProUGUI turn = MenuUiBuilder.CreateText("TurnCounterText", hudRoot,
                UiTextRules.TurnCounter(1, 20), UiTheme.FontHud, TextAlignmentOptions.MidlineRight,
                UiTheme.BrassLight, body);
            MenuUiBuilder.SetAnchored(turn.rectTransform, new Vector2(0.5f, 1f), new Vector2(210f, 36f),
                new Vector2(-230f, -30f));

            TextMeshProUGUI timer = MenuUiBuilder.CreateText("TimerText", hudRoot,
                UiTextRules.Timer(0), UiTheme.FontHud, TextAlignmentOptions.MidlineLeft,
                UiTheme.TextLight, body);
            MenuUiBuilder.SetAnchored(timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(210f, 36f),
                new Vector2(230f, -30f));

            // 右上：回合提示 + 双方存活。
            RectTransform status = MenuUiBuilder.CreatePanel("TeamStatusPanel", hudRoot,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Safe, -Safe),
                new Vector2(560f, 108f), UiSprites.Kind.PanelWood);

            TextMeshProUGUI hint = MenuUiBuilder.CreateText("TurnHintText", status,
                string.Empty, UiTheme.FontHud, TextAlignmentOptions.TopRight, UiTheme.BrassLight, body);
            MenuUiBuilder.SetAnchored(hint.rectTransform, new Vector2(1f, 1f), new Vector2(528f, 34f),
                new Vector2(-16f, -14f));

            TextMeshProUGUI teamStatus = MenuUiBuilder.CreateText("TeamStatusText", status,
                string.Empty, UiTheme.FontBody, TextAlignmentOptions.TopRight, UiTheme.TextLight, secondary);
            MenuUiBuilder.SetAnchored(teamStatus.rectTransform, new Vector2(1f, 1f), new Vector2(528f, 30f),
                new Vector2(-16f, -58f));

            result.turnHintText = hint;
            result.teamStatusText = teamStatus;
        }

        /// <summary>模式开关的一段（铜底或木底的静态块 + 居中文字）。</summary>
        static void BuildToggleSegment(Transform parent, string name, string label, float x,
            UiSprites.Kind skin, Color labelColor, TMP_FontAsset font)
        {
            RectTransform segment = MenuUiBuilder.CreateRect(name, parent);
            MenuUiBuilder.SetAnchored(segment, new Vector2(0.5f, 0.5f), new Vector2(206f, 40f),
                new Vector2(x, 0f));

            var image = segment.gameObject.AddComponent<Image>();
            image.sprite = MenuUiBuilder.GetSprite(skin);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;

            TextMeshProUGUI text = MenuUiBuilder.CreateText("Label", segment, label, UiTheme.FontBody,
                TextAlignmentOptions.Center, labelColor, font);
            MenuUiBuilder.Stretch(text.rectTransform);
        }

        /// <summary>
        /// 小地图（左上，§3.5）：Canvas 直接子节点名为 <c>MinimapPanel</c>，
        /// 供 <c>HudMinimapSceneSetup</c> 复用并补 <c>DotLayer</c>/<c>TileLayer</c> 与 BattleMinimap 接线。
        /// </summary>
        static void BuildMinimap(Transform canvas, TMP_FontAsset secondary)
        {
            Transform existing = canvas.Find(MinimapPanelName);
            RectTransform panel = existing as RectTransform;

            if (panel == null)
            {
                panel = MenuUiBuilder.CreatePanel(MinimapPanelName, canvas,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Safe, -Safe),
                    new Vector2(260f, 100f), UiSprites.Kind.PanelWood);
            }

            EnsureChildRect(panel, "DotLayer");
            Transform dotLayer = panel.Find("DotLayer");
            if (dotLayer != null)
                EnsureChildRect(dotLayer, "TileLayer");

            // 装饰性标题（HudMinimapSceneSetup 只改面板自身 Image/Outline，不动子节点，故标题可保留）。
            if (panel.Find("MinimapCaption") == null)
            {
                TextMeshProUGUI caption = MenuUiBuilder.CreateText("MinimapCaption", panel,
                    UiStrings.BattleMinimapTitle, UiTheme.FontTiny, TextAlignmentOptions.BottomLeft,
                    UiTheme.BrassLight, secondary);
                MenuUiBuilder.SetAnchored(caption.rectTransform, new Vector2(0f, 0f), new Vector2(120f, 22f),
                    new Vector2(8f, 6f));
            }
        }

        static void EnsureChildRect(Transform parent, string name)
        {
            if (parent.Find(name) != null)
                return;

            RectTransform rect = MenuUiBuilder.CreateRect(name, parent);
            MenuUiBuilder.Stretch(rect);
        }

        // ------------------------------------------------------------------
        // 屏幕中心准星（z=20 语义；本轮无逻辑驱动，静态显示）
        // ------------------------------------------------------------------

        static void BuildCrosshair(Transform hudRoot)
        {
            RectTransform crosshair = MenuUiBuilder.CreateRect("Crosshair", hudRoot);
            MenuUiBuilder.SetAnchored(crosshair, new Vector2(0.5f, 0.5f), new Vector2(64f, 64f), Vector2.zero);

            var image = crosshair.gameObject.AddComponent<Image>();
            image.sprite = MenuUiBuilder.GetSprite(UiSprites.Kind.Crosshair);
            image.color = UiTheme.Select;
            image.raycastTarget = false;
        }

        // ------------------------------------------------------------------
        // 左下：船员名册
        // ------------------------------------------------------------------

        static void BuildRoster(Transform hudRoot, TMP_FontAsset title, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            TextMeshProUGUI rosterTitle = MenuUiBuilder.CreateText("RosterTitle", hudRoot,
                UiStrings.MainCrew, UiTheme.FontSection, TextAlignmentOptions.BottomLeft,
                UiTheme.BrassLight, title);
            MenuUiBuilder.SetAnchored(rosterTitle.rectTransform, new Vector2(0f, 0f), new Vector2(380f, 32f),
                new Vector2(Safe, 566f));
            result.rosterTitle = rosterTitle;

            RectTransform panel = MenuUiBuilder.CreatePanel("RosterPanel", hudRoot,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Safe, 120f),
                new Vector2(368f, 438f), UiSprites.Kind.PanelWood);

            var rows = new BattleHud.RosterRowView[RosterRows];
            for (int i = 0; i < RosterRows; i++)
                rows[i] = CreateRosterRow(panel, i, body, secondary);

            result.rosterRows = rows;
        }

        static BattleHud.RosterRowView CreateRosterRow(Transform parent, int index, TMP_FontAsset body,
            TMP_FontAsset secondary)
        {
            RectTransform row = MenuUiBuilder.CreateRect("RosterRow_" + index, parent);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(352f, 34f);
            row.anchoredPosition = new Vector2(8f, -index * 36f);

            // 队伍色块。
            RectTransform swatch = MenuUiBuilder.CreateRect("Swatch", row);
            MenuUiBuilder.SetAnchored(swatch, new Vector2(0f, 1f), new Vector2(10f, 22f), new Vector2(4f, -6f));
            var swatchImage = swatch.gameObject.AddComponent<Image>();
            swatchImage.color = UiTheme.TeamRed;
            swatchImage.raycastTarget = false;

            // 姓名（队伍 + 职业中文；字号 ≥ 正文下限 20）。
            TextMeshProUGUI name = MenuUiBuilder.CreateText("NameText", row, string.Empty,
                UiTheme.FontBody, TextAlignmentOptions.MidlineLeft, UiTheme.TextLight, body);
            MenuUiBuilder.SetAnchored(name.rectTransform, new Vector2(0f, 1f), new Vector2(146f, 26f),
                new Vector2(22f, -4f));

            // 血条底 + 填充。
            RectTransform barBg = MenuUiBuilder.CreateRect("BarBg", row);
            MenuUiBuilder.SetAnchored(barBg, new Vector2(0f, 1f), new Vector2(104f, 14f), new Vector2(172f, -10f));
            var barBgImage = barBg.gameObject.AddComponent<Image>();
            barBgImage.sprite = MenuUiBuilder.GetSprite(UiSprites.Kind.BarBackground);
            barBgImage.type = Image.Type.Sliced;
            barBgImage.color = Color.white;
            barBgImage.raycastTarget = false;

            RectTransform fill = MenuUiBuilder.CreateRect("BarFill", barBg);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0.5f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = MenuUiBuilder.GetSprite(UiSprites.Kind.BarFill);
            fillImage.type = Image.Type.Sliced;
            fillImage.color = Color.white;
            fillImage.raycastTarget = false;

            // 生命数字（≥16px 角标下限）。
            TextMeshProUGUI hp = MenuUiBuilder.CreateText("HpText", row, string.Empty,
                UiTheme.FontTiny, TextAlignmentOptions.MidlineRight, UiTheme.TextLight, secondary);
            MenuUiBuilder.SetAnchored(hp.rectTransform, new Vector2(0f, 1f), new Vector2(64f, 26f),
                new Vector2(284f, -4f));

            return new BattleHud.RosterRowView
            {
                root = row.gameObject,
                teamSwatch = swatchImage,
                nameLabel = name,
                healthFill = fillImage,
                healthLabel = hp,
            };
        }

        // ------------------------------------------------------------------
        // 底部中央：武器面板（滚动列表 + 当前武器信息 + 抛自己 / 结束回合）
        // ------------------------------------------------------------------

        static void BuildWeaponPanel(Transform hudRoot, TMP_FontAsset title, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            RectTransform panel = MenuUiBuilder.CreatePanel("WeaponPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Safe),
                new Vector2(960f, 300f), UiSprites.Kind.PanelWood);
            result.weaponPanelRoot = panel.gameObject;

            TextMeshProUGUI panelTitle = MenuUiBuilder.CreateText("WeaponPanelTitle", panel, string.Empty,
                UiTheme.FontSection, TextAlignmentOptions.Center, UiTheme.BrassLight, title);
            MenuUiBuilder.SetAnchored(panelTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 34f),
                new Vector2(0f, -10f));
            result.weaponPanelTitle = panelTitle;

            Button throwButton = MenuUiBuilder.CreateButton("ThrowSelfButton", panel, UiStrings.BattleThrowSelf,
                new Vector2(0.5f, 1f), new Vector2(-110f, -52f), new Vector2(200f, 40f), body,
                UiSprites.Kind.ButtonWood, UiTheme.TextLight, UiTheme.FontHint);
            result.throwSelfButton = throwButton;

            Button endGoButton = MenuUiBuilder.CreateButton("EndGoButton", panel, UiStrings.BattleEndGo,
                new Vector2(0.5f, 1f), new Vector2(110f, -52f), new Vector2(200f, 40f), body,
                UiSprites.Kind.ButtonWood, UiTheme.TextLight, UiTheme.FontHint);
            result.endGoButton = endGoButton;

            TextMeshProUGUI listTitle = MenuUiBuilder.CreateText("WeaponListTitle", panel,
                UiStrings.BattleWeaponListTitle, UiTheme.FontHint, TextAlignmentOptions.MidlineLeft,
                UiTheme.TextLight, secondary);
            MenuUiBuilder.SetAnchored(listTitle.rectTransform, new Vector2(0f, 1f), new Vector2(240f, 24f),
                new Vector2(24f, -96f));

            BuildWeaponScroll(panel, body, result);
        }

        static void BuildWeaponScroll(RectTransform panel, TMP_FontAsset body, Result result)
        {
            RectTransform scrollGo = MenuUiBuilder.CreateRect("WeaponScroll", panel);
            MenuUiBuilder.SetAnchored(scrollGo, new Vector2(0.5f, 1f), new Vector2(912f, 150f),
                new Vector2(0f, -128f));

            var scroll = scrollGo.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            RectTransform viewport = MenuUiBuilder.CreateRect("Viewport", scrollGo);
            MenuUiBuilder.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = MenuUiBuilder.CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            var buttons = new Button[WeaponSlots];
            var labels = new MaskableGraphic[WeaponSlots];

            for (int i = 0; i < WeaponSlots; i++)
            {
                var id = (WeaponId)i;
                Button button = MenuUiBuilder.CreateButton("WeaponButton_" + id, content,
                    UiTextRules.WeaponName(id), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(900f, 34f), body, UiSprites.Kind.ButtonWood, UiTheme.TextLight,
                    UiTheme.FontHint);

                var element = button.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = 34f;
                element.minHeight = 34f;

                TextMeshProUGUI label = MenuUiBuilder.CreateText("Label", button.transform,
                    UiTextRules.WeaponName(id), UiTheme.FontHint, TextAlignmentOptions.MidlineLeft,
                    UiTheme.TextLight, body);
                MenuUiBuilder.Stretch(label.rectTransform, 0f);
                label.margin = new Vector4(16f, 0f, 8f, 0f);

                // 去掉 CreateButton 自带的居中文本，只保留左对齐标签，避免两个 TMP 叠加。
                Transform centered = button.transform.Find("Text");
                if (centered != null)
                    Object.DestroyImmediate(centered.gameObject);

                buttons[i] = button;
                labels[i] = label;
            }

            result.weaponButtons = buttons;
            result.weaponLabels = labels;
        }

        // ------------------------------------------------------------------
        // 底部：瞄准/聚焦标签、操作提示条、返回按钮
        // ------------------------------------------------------------------

        static void BuildLabelsAndHints(Transform hudRoot, TMP_FontAsset body, TMP_FontAsset secondary)
        {
            // 操作提示条（半透羊皮纸底 + 墨字，非当前上下文不显示——本轮固定显示移动模式提示）。
            RectTransform hintBar = MenuUiBuilder.CreatePanel("HintBar", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 330f),
                new Vector2(1200f, 28f), UiSprites.Kind.PanelParchment, brassOutline: false);
            var hintImage = hintBar.GetComponent<Image>();
            if (hintImage != null)
                hintImage.color = UiTheme.WithAlpha(Color.white, 0.88f);

            TextMeshProUGUI hint = MenuUiBuilder.CreateText("HintText", hintBar,
                UiStrings.BattleHintMove, UiTheme.FontHint, TextAlignmentOptions.Center, UiTheme.Ink, secondary);
            MenuUiBuilder.Stretch(hint.rectTransform, 4f);

            // 瞄准 / 聚焦标签（默认隐藏，等玩法接线后由逻辑控制显隐）。
            TextMeshProUGUI aim = MenuUiBuilder.CreateText("AimLabel", hudRoot,
                UiStrings.BattleAiming, UiTheme.FontHud, TextAlignmentOptions.Center, UiTheme.Select, body);
            MenuUiBuilder.SetAnchored(aim.rectTransform, new Vector2(0.5f, 0f), new Vector2(240f, 30f),
                new Vector2(0f, 362f));
            aim.gameObject.SetActive(false);

            TextMeshProUGUI focus = MenuUiBuilder.CreateText("FocusLabel", hudRoot,
                UiStrings.BattleFocusing, UiTheme.FontBody, TextAlignmentOptions.Center, UiTheme.Select, secondary);
            MenuUiBuilder.SetAnchored(focus.rectTransform, new Vector2(0.5f, 1f), new Vector2(200f, 26f),
                new Vector2(0f, -80f));
            focus.gameObject.SetActive(false);
        }
    }
}
