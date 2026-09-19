using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 重建器（**本波次：多彩卡通 · 图标优先重铺**）。
    ///
    /// 【谁调用】<see cref="BattleUiTheme.Apply"/>（由 <c>M2BattleSceneSetup.BuildHud</c> 回调）；
    /// 本构建器先清掉旧 HUD 子节点再重建，产出 <see cref="Result"/> 交 BattleUiTheme 回写。
    ///
    /// 【信息架构（与 BattleHud 类注释同源）】
    ///   · 顶栏：双队合成血条（每单位一段 + 白色 damage ghost）+ 职业头像 pips + 中央回合徽章
    ///     （队色环 + 数字）+ 徽章下提示 chip + 右上三枚模式图标钮；
    ///   · 底部：武器面板（17 武器全图标格，右列 = 头像 / 名 / HP / 投掷 / 结束回合图文钮）；
    ///   · 左下：暂停 / 返回图标钮；右下：操作提示 chip；
    ///   · 模态：暂停 / 结算 / 返回确认统一 <see cref="UiKit.CreateModal"/>（Dim + 深底卡片）。
    ///   · 左下 12 行名册整块退役（信息被血条分段 + pips + 头顶条覆盖）。
    ///
    /// 【皮肤】全部走 <see cref="UiKit"/>（tintable 平色九宫格 + 符号图标 + 武器静物 PNG），
    /// 字号走 <see cref="UiSkin.Font"/>；旧木板 / 玻璃皮肤不再出现在战斗 HUD。
    /// </summary>
    public static class BattleHudBuilder
    {
        /// <summary>武器槽位（= WeaponCatalog.Count，索引 = WeaponId 枚举值）。</summary>
        const int WeaponSlots = 17;

        /// <summary>每队血条段数 / pip 数（与 BattleHud.MaxSegmentsPerTeam 一致）。</summary>
        const int SegmentsPerTeam = 6;

        /// <summary>小地图面板名（必须与 HudMinimapSceneSetup.MinimapPanelName 一致，且为 Canvas 直接子节点）。</summary>
        public const string MinimapPanelName = "MinimapPanel";

        const float Safe = 16f;

        // ---------------- 顶栏 ----------------

        /// <summary>队血条宽 / 高（每段 = 条宽/6 - 缝）。</summary>
        const float TeamBarWidth = 560f;
        const float TeamBarHeight = 24f;
        const float SegmentGap = 4f;

        /// <summary>红队条起点 x（避开左上小地图：面板右缘 ≈ 16+224）。</summary>
        const float RedBarX = 244f;

        /// <summary>pip 尺寸 / 间距。</summary>
        const float PipSize = 30f;
        const float PipGap = 6f;

        /// <summary>回合徽章（外环 + 内圆 + 数字）。</summary>
        const float BadgeSize = 88f;

        /// <summary>模式图标钮。</summary>
        const float ModeButtonSize = 46f;

        // ---------------- 武器面板 ----------------

        const float WeaponPanelWidth = 880f;
        const float WeaponPanelHeight = 224f;
        const float WeaponPanelBottom = 60f;

        /// <summary>图标格尺寸 / 间距 / 列数（9×2 = 18 格，17 武器 + 1 空）。</summary>
        const float WeaponCell = 66f;
        const float WeaponCellGap = 6f;
        const int WeaponColumns = 9;

        /// <summary>构建产物：全部需要回写给 BattleHud 的引用。</summary>
        public sealed class Result
        {
            public BattleHud.TeamBarView teamBarRed;
            public BattleHud.TeamBarView teamBarBlue;
            public Image badgeRing;
            public TextMeshProUGUI badgeText;
            public TextMeshProUGUI turnHintText;
            public GameObject weaponPanelRoot;
            public Image unitPortrait;
            public TextMeshProUGUI unitNameText;
            public BattleHud.HpBarView unitHpBar;
            public TextMeshProUGUI weaponNameText;
            public TextMeshProUGUI weaponDescText;
            public Button[] weaponButtons;
            public Image[] weaponFrames;
            public Button throwSelfButton;
            public Button endGoButton;
            public Button[] modeButtons;
            public Image[] modeFrames;
            public Button backButton;
            public Button pauseButton;
            public TextMeshProUGUI hintText;

            // ---- 模态：暂停 / 结算 / 返回确认 ----
            public GameObject pausePanelRoot;
            public RectTransform pauseCard;
            public Button resumeButton;
            public Button pauseRestartButton;
            public Button pauseBackButton;
            public GameObject confirmDialogRoot;
            public RectTransform confirmCard;
            public TextMeshProUGUI confirmMessage;
            public Button confirmOkButton;
            public Button confirmCancelButton;
            public GameObject settlementPanelRoot;
            public RectTransform settlementCard;
            public TextMeshProUGUI settlementTitleText;
            public TextMeshProUGUI settlementLinesText;
            public Image[] settlementStars;
            public Button settlementRestartButton;
            public Button settlementBackButton;
        }

        /// <summary>在 <paramref name="canvas"/> 下重建整套 HUD 节点（清旧由调用方处理）。</summary>
        public static Result Build(GameObject canvas)
        {
            MenuUiBuilder.EnsureFonts();
            TMP_FontAsset title = MenuUiBuilder.TitleFont;
            TMP_FontAsset body = MenuUiBuilder.BodyFont;
            TMP_FontAsset secondary = MenuUiBuilder.SecondaryFont;

            RectTransform hudRoot = UiKit.CreateRect("HudLayout", canvas.transform);
            UiKit.Stretch(hudRoot);

            var result = new Result();

            BuildMinimap(canvas.transform);
            BuildTeamBars(hudRoot, result);
            BuildBadge(hudRoot, result);
            BuildWeaponPanel(hudRoot, body, secondary, result);
            BuildBottomBar(hudRoot, secondary, result);
            BuildCrosshair(hudRoot);

            // 模态层最后建（同级后建者画在上层）。
            BuildPausePanel(hudRoot, title, body, result);
            BuildSettlementPanel(hudRoot, title, secondary, result);
            BuildBackConfirm(canvas.transform, body, result);

            return result;
        }

        // ------------------------------------------------------------------
        // 顶栏：双队合成血条 + pips
        // ------------------------------------------------------------------

        static void BuildTeamBars(RectTransform hudRoot, Result result)
        {
            result.teamBarRed = BuildOneTeamBar(hudRoot, "Red", teamIndex: 0, mirror: false);
            result.teamBarBlue = BuildOneTeamBar(hudRoot, "Blue", teamIndex: 1, mirror: true);
        }

        /// <summary>建一队的血条 + pips（mirror = 蓝队从右缘排）。</summary>
        static BattleHud.TeamBarView BuildOneTeamBar(RectTransform hudRoot, string teamName,
            int teamIndex, bool mirror)
        {
            var view = new BattleHud.TeamBarView();

            // 血条条体：凹槽底 + 段容器。
            RectTransform barRoot;
            if (mirror)
            {
                barRoot = UiKit.CreatePanel("TeamBar_" + teamName, hudRoot,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Safe, -Safe),
                    new Vector2(TeamBarWidth, TeamBarHeight));
            }
            else
            {
                barRoot = UiKit.CreatePanel("TeamBar_" + teamName, hudRoot,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(RedBarX, -Safe),
                    new Vector2(TeamBarWidth, TeamBarHeight));
            }

            // 把面板底换成血条凹槽皮肤（CreatePanel 建的是 InkDeep 面板，这里要槽感）。
            var barImage = barRoot.GetComponent<Image>();
            barImage.sprite = LoadSkin(CartoonSpriteFactory.Shape.BarTrack);
            barImage.type = Image.Type.Sliced;
            view.root = barRoot.gameObject;

            view.segmentRoot = barRoot;

            // 段：每单位一格（段内 ghost + fill 双层 pill；段间缝隙露出凹槽 = 分格读感）。
            float segmentWidth = (TeamBarWidth - (SegmentsPerTeam - 1) * SegmentGap - 4f) / SegmentsPerTeam;
            view.segments = new BattleHud.UnitSegmentView[SegmentsPerTeam];
            for (int i = 0; i < SegmentsPerTeam; i++)
            {
                float x = 2f + i * (segmentWidth + SegmentGap);
                RectTransform segment = UiKit.CreateRect("Segment_" + i, barRoot);
                segment.anchorMin = segment.anchorMax = new Vector2(0f, 0.5f);
                segment.pivot = new Vector2(0f, 0.5f);
                segment.sizeDelta = new Vector2(segmentWidth, TeamBarHeight - 6f);
                segment.anchoredPosition = new Vector2(x, 0f);

                Image ghost = UiKit.CreateTinted("Ghost", segment,
                    CartoonSpriteFactory.Shape.Pill, UiSkin.DamageGhost);
                UiKit.Stretch(ghost.rectTransform);
                Image fill = UiKit.CreateTinted("Fill", segment,
                    CartoonSpriteFactory.Shape.Pill, UiSkin.TeamFill(teamIndex));
                UiKit.Stretch(fill.rectTransform);

                view.segments[i] = new BattleHud.UnitSegmentView
                {
                    root = segment.gameObject,
                    fill = fill,
                    ghost = ghost,
                };
            }

            // pips：条下一排职业头像（死亡换骷髅由运行时写）。
            RectTransform pipRoot = UiKit.CreateRect("Pips_" + teamName, hudRoot);
            if (mirror)
            {
                pipRoot.anchorMin = pipRoot.anchorMax = new Vector2(1f, 1f);
                pipRoot.pivot = new Vector2(1f, 1f);
                pipRoot.anchoredPosition = new Vector2(-Safe, -Safe - TeamBarHeight - 6f);
            }
            else
            {
                pipRoot.anchorMin = pipRoot.anchorMax = new Vector2(0f, 1f);
                pipRoot.pivot = new Vector2(0f, 1f);
                pipRoot.anchoredPosition = new Vector2(RedBarX, -Safe - TeamBarHeight - 6f);
            }
            pipRoot.sizeDelta = new Vector2(SegmentsPerTeam * (PipSize + PipGap) - PipGap, PipSize);
            view.pipRoot = pipRoot;

            view.pips = new BattleHud.UnitPipView[SegmentsPerTeam];
            for (int i = 0; i < SegmentsPerTeam; i++)
            {
                RectTransform pip = UiKit.CreateRect("Pip_" + i, pipRoot);
                pip.anchorMin = pip.anchorMax = new Vector2(0f, 0.5f);
                pip.pivot = new Vector2(0f, 0.5f);
                pip.sizeDelta = new Vector2(PipSize, PipSize);
                pip.anchoredPosition = new Vector2(i * (PipSize + PipGap), 0f);

                Image frame = pip.gameObject.AddComponent<Image>();
                frame.sprite = LoadSkin(CartoonSpriteFactory.Shape.Slot);
                frame.type = Image.Type.Sliced;
                frame.color = UiSkin.InkSoft;

                Image icon = UiKit.CreateGlyph("Icon", pip, UiGlyphs.Glyph.Helm, Color.white);
                UiKit.Stretch(icon.rectTransform, 4f);

                view.pips[i] = new BattleHud.UnitPipView
                {
                    root = pip.gameObject,
                    icon = icon,
                    frame = frame,
                };
            }

            return view;
        }

        // ------------------------------------------------------------------
        // 中央回合徽章 + 提示 chip
        // ------------------------------------------------------------------

        static void BuildBadge(RectTransform hudRoot, Result result)
        {
            RectTransform badge = UiKit.CreateRect("TurnBadge", hudRoot);
            badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 1f);
            badge.pivot = new Vector2(0.5f, 1f);
            badge.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            badge.anchoredPosition = new Vector2(0f, -6f);

            result.badgeRing = UiKit.CreateTinted("Ring", badge,
                CartoonSpriteFactory.Shape.Ring, UiSkin.TeamRed);
            UiKit.Stretch(result.badgeRing.rectTransform);

            Image core = UiKit.CreateTinted("Core", badge,
                CartoonSpriteFactory.Shape.Circle, UiSkin.InkDeep);
            UiKit.Stretch(core.rectTransform, 11f);

            result.badgeText = UiKit.CreateText("TurnText", badge, "1", UiSkin.Font.Title,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, MenuUiBuilder.TitleFont);
            UiKit.Stretch(result.badgeText.rectTransform);

            // 提示 chip（徽章下方：「轮到你了 / 地方行动中」）。
            RectTransform chip = UiKit.CreateRect("TurnHintChip", hudRoot);
            chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 1f);
            chip.pivot = new Vector2(0.5f, 1f);
            chip.sizeDelta = new Vector2(320f, 30f);
            chip.anchoredPosition = new Vector2(0f, -BadgeSize - 14f);

            var chipImage = chip.gameObject.AddComponent<Image>();
            chipImage.sprite = LoadSkin(CartoonSpriteFactory.Shape.Chip);
            chipImage.type = Image.Type.Sliced;
            chipImage.color = UiSkin.InkSoft;
            chipImage.raycastTarget = false;

            result.turnHintText = UiKit.CreateText("TurnHintText", chip, string.Empty, UiSkin.Font.Hint,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, MenuUiBuilder.BodyFont);
            UiKit.Stretch(result.turnHintText.rectTransform, 8f);
        }

        // ------------------------------------------------------------------
        // 底部：武器面板（图标格 + 右列）
        // ------------------------------------------------------------------

        static void BuildWeaponPanel(RectTransform hudRoot, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            RectTransform panel = UiKit.CreatePanel("WeaponPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, WeaponPanelBottom), new Vector2(WeaponPanelWidth, WeaponPanelHeight));
            result.weaponPanelRoot = panel.gameObject;

            // ---- 图标格区（9 列 × 2 行）----
            var buttons = new Button[WeaponSlots];
            var frames = new Image[WeaponSlots];

            for (int i = 0; i < WeaponSlots; i++)
            {
                int column = i % WeaponColumns;
                int row = i / WeaponColumns;

                RectTransform cell = UiKit.CreateRect("WeaponCell_" + (WeaponId)i, panel);
                cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                cell.sizeDelta = new Vector2(WeaponCell, WeaponCell);
                cell.anchoredPosition = new Vector2(
                    12f + column * (WeaponCell + WeaponCellGap),
                    -(12f + row * (WeaponCell + WeaponCellGap) + WeaponCell));

                Image frame = cell.gameObject.AddComponent<Image>();
                frame.sprite = LoadSkin(CartoonSpriteFactory.Shape.Slot);
                frame.type = Image.Type.Sliced;
                frame.color = UiSkin.WeaponColor((WeaponId)i);

                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = frame;
                button.colors = UiKit.FourState(frame.color);
                cell.gameObject.AddComponent<UiPressSink>();

                Image icon = UiKit.CreateRect("Icon", cell).gameObject.AddComponent<Image>();
                icon.sprite = LoadWeaponIcon((WeaponId)i);
                icon.type = Image.Type.Simple;
                icon.color = Color.white;
                icon.raycastTarget = false;
                UiKit.Stretch(icon.rectTransform, 8f);

                buttons[i] = button;
                frames[i] = frame;
            }

            result.weaponButtons = buttons;
            result.weaponFrames = frames;

            // ---- 底部说明行：武器名（金）+ 说明（次级） ----
            result.weaponNameText = UiKit.CreateText("WeaponName", panel, string.Empty, UiSkin.Font.Section,
                TextAlignmentOptions.MidlineLeft, UiSkin.Gold, secondary);
            result.weaponNameText.enableWordWrapping = false;
            UiKit.SetAnchored(result.weaponNameText.rectTransform, new Vector2(0f, 0f),
                new Vector2(250f, 28f), new Vector2(16f, 10f));

            result.weaponDescText = UiKit.CreateText("WeaponDesc", panel, string.Empty, UiSkin.Font.Hint,
                TextAlignmentOptions.MidlineLeft, UiSkin.TextDim, secondary);
            result.weaponDescText.enableWordWrapping = false;
            UiKit.SetAnchored(result.weaponDescText.rectTransform, new Vector2(0f, 0f),
                new Vector2(400f, 28f), new Vector2(274f, 10f));

            // ---- 右列：头像 / 名 / HP / 投掷 / 结束回合 ----
            float rightCenterX = 12f + WeaponColumns * (WeaponCell + WeaponCellGap) - WeaponCellGap
                                 + 8f + 100f;    // 图标区右缘 + 8 + 半列宽（列宽 200）

            RectTransform portrait = UiKit.CreateRect("UnitPortrait", panel);
            portrait.anchorMin = portrait.anchorMax = new Vector2(0.5f, 0.5f);
            portrait.pivot = new Vector2(0.5f, 0.5f);
            portrait.sizeDelta = new Vector2(52f, 52f);
            portrait.anchoredPosition = new Vector2(rightCenterX - WeaponPanelWidth * 0.5f, -38f);

            result.unitPortrait = portrait.gameObject.AddComponent<Image>();
            result.unitPortrait.sprite = LoadCrewIcon("sailor");
            result.unitPortrait.type = Image.Type.Simple;
            result.unitPortrait.raycastTarget = false;

            result.unitNameText = UiKit.CreateText("UnitName", panel, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, body);
            result.unitNameText.enableWordWrapping = false;
            UiKit.SetAnchored(result.unitNameText.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(204f, 24f), new Vector2(rightCenterX - WeaponPanelWidth * 0.5f, -76f));

            UiKit.BarView hpBar = UiKit.CreateBar("UnitHp", panel,
                new Vector2(rightCenterX - WeaponPanelWidth * 0.5f, -100f),
                new Vector2(184f, 16f), UiSkin.TeamRed);
            result.unitHpBar = new BattleHud.HpBarView
            {
                track = hpBar.Track,
                ghost = hpBar.Ghost,
                fill = hpBar.Fill,
            };

            result.throwSelfButton = CreateActionButton(panel, "ThrowSelfButton",
                new Vector2(rightCenterX - WeaponPanelWidth * 0.5f, -132f), new Vector2(200f, 44f),
                UiGlyphs.Glyph.ThrowArc, UiStrings.BattleThrowSelf, UiSkin.Gold, UiSkin.InkOnGold, body);
            result.endGoButton = CreateActionButton(panel, "EndGoButton",
                new Vector2(rightCenterX - WeaponPanelWidth * 0.5f, -182f), new Vector2(200f, 44f),
                UiGlyphs.Glyph.Flag, UiStrings.BattleEndGo, UiSkin.InkSoft, UiSkin.TextOnInk, body);
        }

        /// <summary>图文主按钮（图标在左、文字跟右；模式/动作钮的统一长相）。</summary>
        static Button CreateActionButton(Transform parent, string name, Vector2 position, Vector2 size,
            UiGlyphs.Glyph glyph, string label, Color chipColor, Color labelColor, TMP_FontAsset font)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = LoadSkin(CartoonSpriteFactory.Shape.Chip);
            image.type = Image.Type.Sliced;
            image.color = chipColor;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = UiKit.FourState(chipColor);
            rect.gameObject.AddComponent<UiPressSink>();

            Image icon = UiKit.CreateGlyph("Icon", rect, glyph, labelColor);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(24f, 24f);
            icon.rectTransform.anchoredPosition = new Vector2(12f, 0f);

            TextMeshProUGUI text = UiKit.CreateText("Text", rect, label, UiSkin.Font.Body,
                TextAlignmentOptions.MidlineLeft, labelColor, font);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(size.x - 48f, size.y);
            text.rectTransform.anchoredPosition = new Vector2(42f, 0f);

            return button;
        }

        // ------------------------------------------------------------------
        // 右下提示条 / 左下系统钮 / 准星
        // ------------------------------------------------------------------

        static void BuildBottomBar(RectTransform hudRoot, TMP_FontAsset secondary, Result result)
        {
            // 操作提示（右下，r12 裁决"放旁边不占中心"）。
            RectTransform hint = UiKit.CreateRect("HintBar", hudRoot);
            hint.anchorMin = hint.anchorMax = new Vector2(1f, 0f);
            hint.pivot = new Vector2(1f, 0f);
            hint.sizeDelta = new Vector2(560f, 32f);
            hint.anchoredPosition = new Vector2(-Safe, Safe);

            var hintImage = hint.gameObject.AddComponent<Image>();
            hintImage.sprite = LoadSkin(CartoonSpriteFactory.Shape.Chip);
            hintImage.type = Image.Type.Sliced;
            hintImage.color = UiSkin.InkSoft;
            hintImage.raycastTarget = false;

            result.hintText = UiKit.CreateText("HintText", hint, UiStrings.BattleHintMove, UiSkin.Font.Hint,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, secondary);
            UiKit.Stretch(result.hintText.rectTransform, 8f);

            // 左下：暂停 / 返回图标钮。
            result.pauseButton = UiKit.IconButton("PauseButton", hudRoot, UiGlyphs.Glyph.Pause,
                new Vector2(Safe + 23f, Safe + 23f), new Vector2(46f, 46f),
                UiSkin.InkSoft, UiSkin.TextOnInk);
            result.backButton = UiKit.IconButton("BackButton", hudRoot, UiGlyphs.Glyph.Helm,
                new Vector2(Safe + 46f + 8f + 23f, Safe + 23f), new Vector2(46f, 46f),
                UiSkin.InkSoft, UiSkin.TextOnInk);

            // 右上：模式三图标钮（0=移动 1=操作 2=观察；快捷键 1/2/3）。
            result.modeButtons = new Button[3];
            result.modeFrames = new Image[3];
            UiGlyphs.Glyph[] glyphs = { UiGlyphs.Glyph.MovePad, UiGlyphs.Glyph.Crosshair, UiGlyphs.Glyph.Eye };
            for (int i = 0; i < 3; i++)
            {
                // 从右往左排：操作钮（2）在最右。
                int slotFromRight = 2 - i;
                Vector2 position = new Vector2(
                    -(Safe + ModeButtonSize * 0.5f + slotFromRight * (ModeButtonSize + 8f)), -40f);
                Button button = UiKit.IconButton("ModeButton_" + (BattleHud.BattleHudMode)i, hudRoot,
                    glyphs[i], position, new Vector2(ModeButtonSize, ModeButtonSize),
                    UiSkin.InkSoft, UiSkin.TextOnInk, hotkey: (i + 1).ToString());

                result.modeButtons[i] = button;
                result.modeFrames[i] = button.image;
            }
        }

        /// <summary>屏幕中心准星（观察模式；FPS 式细十字）。</summary>
        static void BuildCrosshair(Transform hudRoot)
        {
            RectTransform crosshair = UiKit.CreateRect("Crosshair", hudRoot);
            UiKit.SetAnchored(crosshair, new Vector2(0.5f, 0.5f), new Vector2(18f, 18f), Vector2.zero);

            void Bar(string name, float w, float h)
            {
                var rect = UiKit.CreateRect(name, crosshair);
                rect.sizeDelta = new Vector2(w, h);
                rect.anchoredPosition = Vector2.zero;
                var img = rect.gameObject.AddComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = false;
                var outline = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
            Bar("CrossH", 18f, 2f);
            Bar("CrossV", 2f, 18f);
        }

        // ------------------------------------------------------------------
        // 模态层
        // ------------------------------------------------------------------

        /// <summary>暂停面板：Dim + 卡片 + 继续 / 再来一局 / 返回主菜单。</summary>
        static void BuildPausePanel(RectTransform hudRoot, TMP_FontAsset title, TMP_FontAsset body,
            Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("PausePanel", hudRoot, new Vector2(480f, 400f));
            result.pausePanelRoot = modal.Root;
            result.pauseCard = modal.Card;

            TextMeshProUGUI titleText = UiKit.CreateText("PauseTitle", modal.Card, UiStrings.BattlePauseTitle,
                UiSkin.Font.Banner, TextAlignmentOptions.Center, UiSkin.Gold, title);
            UiKit.SetAnchored(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(360f, 56f),
                new Vector2(0f, -40f));

            result.resumeButton = CreateActionButton(modal.Card, "ResumeButton",
                new Vector2(0f, -148f), new Vector2(340f, 54f),
                UiGlyphs.Glyph.Play, UiStrings.BattleResume, UiSkin.Gold, UiSkin.InkOnGold, body);
            result.pauseRestartButton = CreateActionButton(modal.Card, "PauseRestartButton",
                new Vector2(0f, -216f), new Vector2(340f, 48f),
                UiGlyphs.Glyph.Retry, UiStrings.BattleRestart, UiSkin.InkSoft, UiSkin.TextOnInk, body);
            result.pauseBackButton = CreateActionButton(modal.Card, "PauseBackButton",
                new Vector2(0f, -276f), new Vector2(340f, 48f),
                UiGlyphs.Glyph.Helm, UiStrings.BackToMainMenu, UiSkin.Danger, UiSkin.TextOnInk, body);
        }

        /// <summary>结算面板：横幅 + 三星 + 明细 + 再来一局 / 返回。</summary>
        static void BuildSettlementPanel(RectTransform hudRoot, TMP_FontAsset title, TMP_FontAsset secondary,
            Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("SettlementPanel", hudRoot, new Vector2(680f, 560f));
            result.settlementPanelRoot = modal.Root;
            result.settlementCard = modal.Card;

            result.settlementTitleText = UiKit.CreateText("SettlementTitle", modal.Card, string.Empty,
                UiSkin.Font.Banner, TextAlignmentOptions.Center, UiSkin.Gold, title);
            UiKit.SetAnchored(result.settlementTitleText.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(600f, 64f), new Vector2(0f, -44f));

            var stars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                Image star = UiKit.CreateGlyph("Star" + i, modal.Card, UiGlyphs.Glyph.Star,
                    UiSkin.WithAlpha(UiSkin.DeadGray, 0.6f));
                star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                star.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                star.rectTransform.sizeDelta = new Vector2(56f, 56f);
                star.rectTransform.anchoredPosition = new Vector2((i - 1) * 64f, -150f);
                stars[i] = star;
            }
            result.settlementStars = stars;

            result.settlementLinesText = UiKit.CreateText("SettlementLines", modal.Card, string.Empty,
                UiSkin.Font.Hud, TextAlignmentOptions.Center, UiSkin.TextOnInk, secondary);
            UiKit.SetAnchored(result.settlementLinesText.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(580f, 220f), new Vector2(0f, -20f));

            result.settlementRestartButton = CreateActionButton(modal.Card, "SettlementRestartButton",
                new Vector2(-150f, -460f), new Vector2(260f, 54f),
                UiGlyphs.Glyph.Retry, UiStrings.BattleRestart, UiSkin.Gold, UiSkin.InkOnGold, secondary);
            result.settlementBackButton = CreateActionButton(modal.Card, "SettlementBackButton",
                new Vector2(150f, -460f), new Vector2(260f, 54f),
                UiGlyphs.Glyph.Helm, UiStrings.Back, UiSkin.InkSoft, UiSkin.TextOnInk, secondary);
        }

        /// <summary>返回确认弹窗（挂在 Canvas 直下压过全部元素）。</summary>
        static void BuildBackConfirm(Transform canvas, TMP_FontAsset body, Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("BackConfirmDialog", canvas, new Vector2(560f, 300f));
            result.confirmDialogRoot = modal.Root;
            result.confirmCard = modal.Card;

            result.confirmMessage = UiKit.CreateText("Message", modal.Card, UiStrings.BackConfirm,
                UiSkin.Font.Section, TextAlignmentOptions.Center, UiSkin.TextOnInk, body);
            UiKit.SetAnchored(result.confirmMessage.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(460f, 90f), new Vector2(0f, -120f));

            result.confirmOkButton = CreateActionButton(modal.Card, "OkButton",
                new Vector2(-130f, -210f), new Vector2(220f, 48f),
                UiGlyphs.Glyph.Check, UiStrings.Confirm, UiSkin.Gold, UiSkin.InkOnGold, body);
            result.confirmCancelButton = CreateActionButton(modal.Card, "CancelButton",
                new Vector2(130f, -210f), new Vector2(220f, 48f),
                UiGlyphs.Glyph.Cross, UiStrings.Cancel, UiSkin.InkSoft, UiSkin.TextOnInk, body);
        }

        // ------------------------------------------------------------------
        // 小地图（左上；面板名/层级契约不变，皮肤换卡通）
        // ------------------------------------------------------------------

        /// <summary>小地图面板尺寸（高度含标题条；圆形罗盘化在 BattleMinimap 侧单独任务）。</summary>
        const float MinimapWidth = 224f;
        const float MinimapHeight = 150f;
        const float MinimapCaptionHeight = 22f;

        static void BuildMinimap(Transform canvas)
        {
            Transform existing = canvas.Find(MinimapPanelName);
            RectTransform panel = existing as RectTransform;
            if (panel == null)
                panel = UiKit.CreateRect(MinimapPanelName, canvas);

            UiKit.SetAnchored(panel, new Vector2(0f, 1f), new Vector2(MinimapWidth, MinimapHeight),
                new Vector2(Safe, -Safe));

            var image = panel.GetComponent<Image>();
            if (image == null)
                image = panel.gameObject.AddComponent<Image>();
            image.sprite = LoadSkin(CartoonSpriteFactory.Shape.PanelInk);
            image.type = Image.Type.Sliced;
            image.color = UiSkin.InkDeep;
            image.raycastTarget = false;

            var legacyOutline = panel.GetComponent<Outline>();
            if (legacyOutline != null)
                Object.DestroyImmediate(legacyOutline);

            // 点阵层：四周退内边距，顶部让出标题条（HudMinimapSceneSetup 契约）。
            RectTransform dotLayer = panel.Find("DotLayer") as RectTransform;
            if (dotLayer == null)
                dotLayer = UiKit.CreateRect("DotLayer", panel);

            dotLayer.anchorMin = Vector2.zero;
            dotLayer.anchorMax = Vector2.one;
            dotLayer.pivot = new Vector2(0.5f, 0.5f);
            dotLayer.offsetMin = new Vector2(10f, 10f);
            dotLayer.offsetMax = new Vector2(-10f, -(10f + MinimapCaptionHeight));

            EnsureChildRect(dotLayer, "TileLayer");

            TextMeshProUGUI caption = null;
            Transform captionNode = panel.Find("MinimapCaption");
            if (captionNode != null)
                caption = captionNode.GetComponent<TextMeshProUGUI>();
            if (caption == null)
            {
                caption = UiKit.CreateText("MinimapCaption", panel, UiStrings.BattleMinimapTitle,
                    UiSkin.Font.Hint, TextAlignmentOptions.MidlineLeft, UiSkin.TextDim, MenuUiBuilder.SecondaryFont);
            }

            UiKit.SetAnchored(caption.rectTransform, new Vector2(0f, 1f),
                new Vector2(140f, MinimapCaptionHeight), new Vector2(10f, -8f));
        }

        static void EnsureChildRect(Transform parent, string name)
        {
            if (parent.Find(name) != null)
                return;

            RectTransform rect = UiKit.CreateRect(name, parent);
            UiKit.Stretch(rect);
        }

        // ------------------------------------------------------------------
        // 资产加载
        // ------------------------------------------------------------------

        /// <summary>皮肤 PNG（Editor 装配必须引用持久资产；缺失回落内存 Sprite）。</summary>
        static Sprite LoadSkin(CartoonSpriteFactory.Shape shape)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Sprites/UI/Cartoon_" + shape + ".png");
            return sprite != null ? sprite : CartoonSpriteFactory.Get(shape);
        }

        /// <summary>武器静物图标 PNG（UiSkinAssetBaker 产物）。</summary>
        static Sprite LoadWeaponIcon(WeaponId id)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Resources/UIIcons/Weapon_" + id + ".png");
            if (sprite != null)
                return sprite;

            // 烘焙缺失时退到符号图标 PNG，不留白格。
            Sprite fallback = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Sprites/UI/Glyph_ThrowArc.png");
            return fallback != null ? fallback : UiGlyphs.Get(UiGlyphs.Glyph.ThrowArc);
        }

        /// <summary>职业头像 PNG（UiSkinAssetBaker 产物）。</summary>
        static Sprite LoadCrewIcon(string crewKey)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Resources/UIIcons/Crew_" + crewKey + ".png");
            return sprite != null ? sprite : UiGlyphs.Get(UiGlyphs.Glyph.Helm);
        }
    }
}
