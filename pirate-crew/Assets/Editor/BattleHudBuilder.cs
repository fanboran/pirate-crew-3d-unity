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

        // ------------------------------------------------------------------
        // HUD zone 表（对齐 game-2 hud_zone_layout 的"定位归表"思路——对齐关系在这里
        // 一次算清，部件不再各自手写坐标；越界/相撞由 Build 末尾的防撞自检兜底）。
        //
        // 【顶部带】垂直中心线 y=500：小地图占左上角，红条/徽章/蓝条以屏幕中轴**镜像
        // 等长**（640），模式三钮收进右上角——三者互不侵犯（模式组左缘 1750 > 蓝条
        // 右缘 1672，间隙 78px）。
        // 【底部带】底边线统一 y=16：暂停/返回在武器面板左侧同行，操作提示条在其
        // 右侧同底边——一条完整的底边线，不再有部件悬空。
        // ------------------------------------------------------------------

        /// <summary>顶部带垂直中心**距屏顶**的像素（UI y 轴向上、屏顶在 1080——
        /// 锚顶件直接用本值做偏移；锚中心件（IconButton）的偏移 = 1080 - 本值 - 540。
        /// r10 复盘：旧常量 TopBandCenterY=500 被一半代码当"距顶像素"一半当"UI y"用，
        /// 模式钮 y 偏移算错符号沉到屏幕垂直中部，故改为单一"距顶"语义）。</summary>
        const float TopBandFromTop = 40f;

        /// <summary>队血条宽 / 高（红蓝镜像等长；段宽运行时按实际人数重排）。</summary>
        const float TeamBarWidth = 640f;
        const float TeamBarHeight = 26f;
        const float SegmentGap = 4f;

        /// <summary>红条左端 = 小地图右缘 + 8；蓝条右端 = 1920 - 同值（镜像对称）。</summary>
        const float TeamBarInsetX = 248f;

        /// <summary>pip 尺寸 / 间距（血条正下方一排职业头像）。</summary>
        const float PipSize = 30f;
        const float PipGap = 6f;

        /// <summary>回合徽章（外环 + 内圆 + 数字）：64 减重档——顶带只是配重，不做视觉主角。</summary>
        const float BadgeSize = 64f;

        /// <summary>模式图标钮（右上角成组，右缘贴 Safe）。</summary>
        const float ModeButtonSize = 46f;

        // ---------------- 底部带 ----------------

        /// <summary>武器面板：贴底居中（bottom = Safe，不再悬空）。</summary>
        const float WeaponPanelWidth = 1040f;
        const float WeaponPanelHeight = 224f;

        /// <summary>图标格尺寸 / 间距 / 列数（9×2 = 18 格，17 武器 + 1 空）。</summary>
        const float WeaponCell = 70f;
        const float WeaponCellGap = 8f;
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

            // zone 防撞自检（对齐 game-2 hud_zone_layout 的越界警告合同）：顶层部件
            // 两两 AABB 相交即报警——曾因手写坐标出现"模式钮压蓝条 / 提示条压武器面板"
            // 两起真实碰撞（2026-09-19 复盘），此后布局事故在装配期就会被点名。
            var scaler = canvas.GetComponent<CanvasScaler>();
            Vector2 referenceSize = scaler != null
                ? scaler.referenceResolution : new Vector2(1920f, 1080f);
            AssertNoOverlaps(canvas.transform, hudRoot, referenceSize);

            return result;
        }

        /// <summary>
        /// 收集 HUD 顶层部件（hudRoot 与 canvas 的直接子节点），两两做**设计坐标系**
        /// AABB 相交检测；全屏 Stretch 件（模态 Dim 根）与过小件（准星）跳过。
        ///
        /// 【为什么不用世界坐标】batchmode 无头下 ScreenSpaceOverlay Canvas 的像素
        /// 尺寸 ≠ 参考分辨率（CanvasScaler 只在渲染时缩放），世界坐标在装配期失真、
        /// 必然误报；设计坐标（anchor×参考分辨率 + anchoredPosition）与 CanvasScaler
        /// 无关，装配期与运行时同口径。
        /// </summary>
        static void AssertNoOverlaps(Transform canvas, RectTransform hudRoot, Vector2 referenceSize)
        {
            var parts = new System.Collections.Generic.List<RectTransform>();
            CollectTopParts(hudRoot, parts);
            CollectTopParts(canvas, parts);

            for (int i = 0; i < parts.Count; i++)
            {
                for (int j = i + 1; j < parts.Count; j++)
                {
                    if (DesignRectsIntersect(parts[i], parts[j], referenceSize))
                    {
                        Debug.LogWarning("[HUD 布局防撞] 部件重叠: " + parts[i].name
                            + " × " + parts[j].name + "——改 zone 常量，不要手挪单个部件");
                    }
                }
            }
        }

        static void CollectTopParts(Transform parent, System.Collections.Generic.List<RectTransform> parts)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                if (!(parent.GetChild(i) is RectTransform rect))
                    continue;

                // 全屏 Stretch 件（模态根/HudLayout 自身）与过小件（准星）不参与。
                bool stretch = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
                bool tiny = rect.rect.width < 4f || rect.rect.height < 4f;
                if (stretch || tiny)
                    continue;
                parts.Add(rect);
            }
        }

        /// <summary>单点 anchor 件的包围盒，表达为设计坐标（原点=canvas 左下，参考分辨率系）。</summary>
        static void DesignRect(RectTransform rect, Vector2 referenceSize, out Vector2 min, out Vector2 max)
        {
            Vector2 pivotPos = new Vector2(
                rect.anchorMin.x * referenceSize.x + rect.anchoredPosition.x,
                rect.anchorMin.y * referenceSize.y + rect.anchoredPosition.y);
            Vector2 size = rect.sizeDelta;
            min = pivotPos - new Vector2(rect.pivot.x * size.x, rect.pivot.y * size.y);
            max = min + size;
        }

        static bool DesignRectsIntersect(RectTransform a, RectTransform b, Vector2 referenceSize)
        {
            DesignRect(a, referenceSize, out Vector2 aMin, out Vector2 aMax);
            DesignRect(b, referenceSize, out Vector2 bMin, out Vector2 bMax);

            // 留 2px 容差（圆角贴图边缘的贴邻不算事故）。
            return aMin.x < bMax.x - 2f && aMax.x > bMin.x + 2f
                && aMin.y < bMax.y - 2f && aMax.y > bMin.y + 2f;
        }

        // ------------------------------------------------------------------
        // 顶栏：双队合成血条 + pips
        // ------------------------------------------------------------------

        static void BuildTeamBars(RectTransform hudRoot, Result result)
        {
            result.teamBarRed = BuildOneTeamBar(hudRoot, "Red", teamIndex: 0, mirror: false);
            result.teamBarBlue = BuildOneTeamBar(hudRoot, "Blue", teamIndex: 1, mirror: true);
        }

        /// <summary>建一队的血条 + pips（红蓝以屏幕中轴镜像等长；条顶距屏顶 27）。</summary>
        static BattleHud.TeamBarView BuildOneTeamBar(RectTransform hudRoot, string teamName,
            int teamIndex, bool mirror)
        {
            var view = new BattleHud.TeamBarView();

            // 顶部带：条中心距屏顶 TopBandFromTop（条顶 = 40-13 = 27），
            // 红条左端 / 蓝条右端 = TeamBarInsetX 镜像。
            float barTop = TopBandFromTop - TeamBarHeight * 0.5f;
            RectTransform barRoot;
            if (mirror)
            {
                barRoot = UiKit.CreatePanel("TeamBar_" + teamName, hudRoot,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-TeamBarInsetX, -barTop),
                    new Vector2(TeamBarWidth, TeamBarHeight));
            }
            else
            {
                barRoot = UiKit.CreatePanel("TeamBar_" + teamName, hudRoot,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(TeamBarInsetX, -barTop),
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

            // pips：条下一排职业头像（死亡换骷髅由运行时写），与血条同侧对齐。
            float pipTop = barTop + TeamBarHeight + 6f;
            RectTransform pipRoot = UiKit.CreateRect("Pips_" + teamName, hudRoot);
            if (mirror)
            {
                pipRoot.anchorMin = pipRoot.anchorMax = new Vector2(1f, 1f);
                pipRoot.pivot = new Vector2(1f, 1f);
                pipRoot.anchoredPosition = new Vector2(-TeamBarInsetX, -pipTop);
            }
            else
            {
                pipRoot.anchorMin = pipRoot.anchorMax = new Vector2(0f, 1f);
                pipRoot.pivot = new Vector2(0f, 1f);
                pipRoot.anchoredPosition = new Vector2(TeamBarInsetX, -pipTop);
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
            // 徽章：顶带配重件，64 减重档（信息只是回合数，不做视觉主角——
            // r8 外部评审"数字最重但价值最低"）。垂直中心对齐顶部带。
            RectTransform badge = UiKit.CreateRect("TurnBadge", hudRoot);
            badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 1f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            badge.anchoredPosition = new Vector2(0f, -TopBandFromTop);

            result.badgeRing = UiKit.CreateTinted("Ring", badge,
                CartoonSpriteFactory.Shape.Ring, UiSkin.TeamRed);
            UiKit.Stretch(result.badgeRing.rectTransform);

            Image core = UiKit.CreateTinted("Core", badge,
                CartoonSpriteFactory.Shape.Circle, UiSkin.InkDeep);
            UiKit.Stretch(core.rectTransform, 8f);

            result.badgeText = UiKit.CreateText("TurnText", badge, "1", UiSkin.Font.Hud,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, MenuUiBuilder.TitleFont);
            UiKit.Stretch(result.badgeText.rectTransform);

            // 提示文字（「轮到你了 / 敌方行动中」）：徽章正下方**纯文字**，
            // 不再套 chip 容器——中央战场区少一件深底块（r8 裁决：中央要干净）。
            // 深色 1px 斜投影保可读（r9 出图裁决：沙地亮部直接吞掉裸浅灰字，
            // 与准星同口径的 Shadow 方案，不上底色容器）。
            result.turnHintText = UiKit.CreateText("TurnHintText", hudRoot, string.Empty,
                UiSkin.Font.Hint, TextAlignmentOptions.Center, UiSkin.TextOnInk, MenuUiBuilder.BodyFont);
            result.turnHintText.enableWordWrapping = false;
            result.turnHintText.rectTransform.anchorMin = result.turnHintText.rectTransform.anchorMax =
                new Vector2(0.5f, 1f);
            result.turnHintText.rectTransform.pivot = new Vector2(0.5f, 1f);
            result.turnHintText.rectTransform.sizeDelta = new Vector2(260f, 20f);
            result.turnHintText.rectTransform.anchoredPosition = new Vector2(0f,
                -TopBandFromTop - BadgeSize * 0.5f - 6f);
            // 描边组合（r10 复盘：单道 1px 斜投影在沙地亮部上仍被吞）——
            // 1.5px 四向 Outline 近黑 + 1px 斜投影，卡通字的"贴纸边"读感。
            var hintOutline = result.turnHintText.gameObject.AddComponent<UnityEngine.UI.Outline>();
            hintOutline.effectColor = new Color(0f, 0f, 0f, 0.78f);
            hintOutline.effectDistance = new Vector2(2.2f, 2.2f);
            var hintShadow = result.turnHintText.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            hintShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            hintShadow.effectDistance = new Vector2(1f, -1f);
        }

        // ------------------------------------------------------------------
        // 底部：武器面板（图标格 + 右列）
        // ------------------------------------------------------------------

        static void BuildWeaponPanel(RectTransform hudRoot, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            // 底部带主体：贴底居中（bottom = Safe），与左下系统钮 / 右下提示条共享底边线。
            RectTransform panel = UiKit.CreatePanel("WeaponPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, Safe), new Vector2(WeaponPanelWidth, WeaponPanelHeight));
            result.weaponPanelRoot = panel.gameObject;

            // ---- 图标格区（9 列 × 2 行；左 padding 16，格 70/缝 8）----
            float cellsWidth = WeaponColumns * WeaponCell + (WeaponColumns - 1) * WeaponCellGap;
            float rightColumnCenter = -WeaponPanelWidth * 0.5f + 16f + cellsWidth + 16f
                + (WeaponPanelWidth - 32f - cellsWidth - 16f) * 0.5f;   // 右列区中心
            float rightColumnWidth = WeaponPanelWidth - 32f - cellsWidth - 16f - 12f;

            var buttons = new Button[WeaponSlots];
            var frames = new Image[WeaponSlots];

            for (int i = 0; i < WeaponSlots; i++)
            {
                int column = i % WeaponColumns;
                int row = i / WeaponColumns;

                RectTransform cell = UiKit.CreateRect("WeaponCell_" + (WeaponId)i, panel);
                cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0.5f, 1f);
                cell.sizeDelta = new Vector2(WeaponCell, WeaponCell);
                // pivot(0.5,1) 的 pos.y 是格子上边相对面板顶的偏移：row0 顶 14、row1 顶 92、
                // 格区底 162，底部 62px 让给说明行（r9 事故：曾多加一格高使整排下沉压说明行）。
                cell.anchoredPosition = new Vector2(
                    16f + WeaponCell * 0.5f + column * (WeaponCell + WeaponCellGap),
                    -(14f + row * (WeaponCell + WeaponCellGap)));

                Image frame = cell.gameObject.AddComponent<Image>();
                frame.sprite = LoadSkin(CartoonSpriteFactory.Shape.Slot);
                frame.type = Image.Type.Sliced;
                frame.color = UiSkin.WeaponCellBase((WeaponId)i);

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
                new Vector2(420f, 28f), new Vector2(274f, 10f));

            // ---- 右列：头像 / 名 / HP / 投掷 / 结束回合（当前单位信息区）----
            // 头像 = Slot 底格 + 图标层（与 pip 同构；底格压 InkSoft，运行时只换图标层引用）。
            RectTransform portrait = UiKit.CreateRect("UnitPortrait", panel);
            portrait.anchorMin = portrait.anchorMax = portrait.pivot = new Vector2(0.5f, 0.5f);
            portrait.sizeDelta = new Vector2(56f, 56f);
            portrait.anchoredPosition = new Vector2(rightColumnCenter, 64f);

            var portraitFrame = portrait.gameObject.AddComponent<Image>();
            portraitFrame.sprite = LoadSkin(CartoonSpriteFactory.Shape.Slot);
            portraitFrame.type = Image.Type.Sliced;
            portraitFrame.color = UiSkin.InkSoft;
            portraitFrame.raycastTarget = false;

            RectTransform portraitIcon = UiKit.CreateRect("Icon", portrait);
            UiKit.Stretch(portraitIcon, 3f);
            result.unitPortrait = portraitIcon.gameObject.AddComponent<Image>();
            result.unitPortrait.sprite = LoadCrewIcon("sailor");
            result.unitPortrait.type = Image.Type.Simple;
            result.unitPortrait.raycastTarget = false;

            result.unitNameText = UiKit.CreateText("UnitName", panel, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, body);
            result.unitNameText.enableWordWrapping = false;
            result.unitNameText.rectTransform.anchorMin = result.unitNameText.rectTransform.anchorMax =
                result.unitNameText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            result.unitNameText.rectTransform.sizeDelta = new Vector2(rightColumnWidth, 24f);
            result.unitNameText.rectTransform.anchoredPosition = new Vector2(rightColumnCenter, 26f);

            UiKit.BarView hpBar = UiKit.CreateBar("UnitHp", panel,
                new Vector2(rightColumnCenter, 2f), new Vector2(rightColumnWidth - 40f, 14f),
                UiSkin.TeamRed);
            result.unitHpBar = new BattleHud.HpBarView
            {
                track = hpBar.Track,
                ghost = hpBar.Ghost,
                fill = hpBar.Fill,
            };

            result.throwSelfButton = UiKit.ActionButton("ThrowSelfButton", panel,
                UiGlyphs.Glyph.ThrowArc, UiStrings.BattleThrowSelf, UiKit.ButtonKind.Primary,
                new Vector2(rightColumnCenter, -32f), new Vector2(rightColumnWidth, 44f), body);
            result.endGoButton = UiKit.ActionButton("EndGoButton", panel,
                UiGlyphs.Glyph.Flag, UiStrings.BattleEndGo, UiKit.ButtonKind.Dark,
                new Vector2(rightColumnCenter, -84f), new Vector2(rightColumnWidth, 44f), body);
        }

        // ------------------------------------------------------------------
        // 底部带（暂停 / 返回 / 提示条）+ 右上模式钮 + 准星
        // ------------------------------------------------------------------

        static void BuildBottomBar(RectTransform hudRoot, TMP_FontAsset secondary, Result result)
        {
            // 底部带三段共享底边线 y=Safe：
            //   [暂停 返回]（面板左外侧）→ [武器面板 贴底居中] → [提示条]（面板右外侧）。
            // 【坐标口径】UiKit.IconButton 的锚点固定在画布中心（anchor/pivot 0.5,0.5），
            // position 是相对中心的偏移；钮垂直中心 = Safe + 23（与面板底边线对齐）。
            float bottomButtonY = -(540f - Safe - 23f);
            float panelLeft = -(WeaponPanelWidth * 0.5f);     // 面板左缘相对中心
            result.pauseButton = UiKit.IconButton("PauseButton", hudRoot, UiGlyphs.Glyph.Pause,
                new Vector2(panelLeft - 23f - 8f - 23f, bottomButtonY), new Vector2(46f, 46f),
                UiSkin.InkSoft, UiSkin.TextOnInk);
            result.backButton = UiKit.IconButton("BackButton", hudRoot, UiGlyphs.Glyph.Helm,
                new Vector2(panelLeft - 23f - 8f - 8f - 46f - 23f, bottomButtonY), new Vector2(46f, 46f),
                UiSkin.InkSoft, UiSkin.TextOnInk);

            // 操作提示（r12 裁决"放旁边不占中心"）：面板右外侧、同底边线。
            float panelRight = WeaponPanelWidth * 0.5f;
            RectTransform hint = UiKit.CreateRect("HintBar", hudRoot);
            hint.anchorMin = hint.anchorMax = new Vector2(1f, 0f);
            hint.pivot = new Vector2(1f, 0f);
            hint.sizeDelta = new Vector2(1920f - Safe - (960f + panelRight + 20f), 32f);
            hint.anchoredPosition = new Vector2(-Safe, Safe);

            var hintImage = hint.gameObject.AddComponent<Image>();
            hintImage.sprite = LoadSkin(CartoonSpriteFactory.Shape.Chip);
            hintImage.type = Image.Type.Sliced;
            hintImage.color = UiSkin.InkSoft;
            hintImage.raycastTarget = false;

            result.hintText = UiKit.CreateText("HintText", hint, UiStrings.BattleHintMove, UiSkin.Font.Hint,
                TextAlignmentOptions.Center, UiSkin.TextOnInk, secondary);
            UiKit.Stretch(result.hintText.rectTransform, 8f);

            // 右上：模式三图标钮成组贴右缘（0=移动 1=操作 2=观察；快捷键 1/2/3；操作钮在最右）。
            // 垂直中心对齐顶部带（与蓝条右段留 78px 间隙互不侵犯）。
            // 【坐标口径】IconButton 的 position 是相对画布中心的偏移：贴右缘第 i 枚
            // 的绝对中心 x = 1920-Safe-23-(2-i)*54，偏移 = 绝对 - 960。
            result.modeButtons = new Button[3];
            result.modeFrames = new Image[3];
            UiGlyphs.Glyph[] glyphs = { UiGlyphs.Glyph.MovePad, UiGlyphs.Glyph.Crosshair, UiGlyphs.Glyph.Eye };
            for (int i = 0; i < 3; i++)
            {
                float offsetFromRight = 960f - Safe - ModeButtonSize * 0.5f - (2 - i) * (ModeButtonSize + 8f);
                Vector2 position = new Vector2(offsetFromRight, 1080f - TopBandFromTop - 540f);
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

            result.resumeButton = UiKit.ActionButton("ResumeButton", modal.Card,
                UiGlyphs.Glyph.Play, UiStrings.BattleResume, UiKit.ButtonKind.Primary,
                new Vector2(0f, -148f), new Vector2(340f, 54f), body);
            result.pauseRestartButton = UiKit.ActionButton("PauseRestartButton", modal.Card,
                UiGlyphs.Glyph.Retry, UiStrings.BattleRestart, UiKit.ButtonKind.Dark,
                new Vector2(0f, -216f), new Vector2(340f, 48f), body);
            result.pauseBackButton = UiKit.ActionButton("PauseBackButton", modal.Card,
                UiGlyphs.Glyph.Helm, UiStrings.BackToMainMenu, UiKit.ButtonKind.Danger,
                new Vector2(0f, -276f), new Vector2(340f, 48f), body);
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

            result.settlementRestartButton = UiKit.ActionButton("SettlementRestartButton", modal.Card,
                UiGlyphs.Glyph.Retry, UiStrings.BattleRestart, UiKit.ButtonKind.Primary,
                new Vector2(-150f, -460f), new Vector2(260f, 54f), secondary);
            result.settlementBackButton = UiKit.ActionButton("SettlementBackButton", modal.Card,
                UiGlyphs.Glyph.Helm, UiStrings.Back, UiKit.ButtonKind.Dark,
                new Vector2(150f, -460f), new Vector2(260f, 54f), secondary);
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

            result.confirmOkButton = UiKit.ActionButton("OkButton", modal.Card,
                UiGlyphs.Glyph.Check, UiStrings.Confirm, UiKit.ButtonKind.Primary,
                new Vector2(-130f, -210f), new Vector2(220f, 48f), body);
            result.confirmCancelButton = UiKit.ActionButton("CancelButton", modal.Card,
                UiGlyphs.Glyph.Cross, UiStrings.Cancel, UiKit.ButtonKind.Dark,
                new Vector2(130f, -210f), new Vector2(220f, 48f), body);
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
