using PirateCrew.Data;
using PirateCrew.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 重建器（**本波次：文字占位版 · 紧凑重排**）。
    ///
    /// 【谁调用】<see cref="BattleUiTheme.Apply"/>（由 <c>BattleSceneSetup.BuildHud</c> 回调）；
    /// 本构建器先清掉旧 HUD 子节点再重建，产出 <see cref="Result"/> 交 BattleUiTheme 回写。
    ///
    /// 【信息架构（2026-09-24 创始人三连裁决）】
    ///   · **物品图标全部退役换文字**：17 武器格 = 武器中文名文字钮，职业头像 pips = 小方格
    ///     （存活空格 / 阵亡「×」），右列头像格删除，模式钮改文字——图标系统整体停用，
    ///     等后续图标批次再回收；
    ///   · **HUD 按 3:1 艺术像素收敛**：旧布局按 1080p 全分辨率排布（条 30px 高、按钮 48px、
    ///     格 60px），与 640×360 艺术画布的世界颗粒不匹配——本版条 24px（8 艺术像素）、
    ///     按钮 24px、武器格 96×24，整屏填充率约收半；
    ///   · **文字解除像素栅格**：字号按可读性自由取（<see cref="UiSkin.Font"/> 新档），
    ///     非文字件尺寸仍取 <see cref="PixelSkin.Unit"/> 整数倍。
    ///   · 顶栏：双队合成血条（每单位一段 + 暖白 damage ghost）+ pips + 中央回合徽章
    ///     （暖金方环 + 黄铜宝石）+ 徽章下提示文字 + 右上三枚模式文字钮；
    ///   · 底部：武器面板（6×3 文字格 + 底部名/说明行 + 右列 = 名 / HP / 跳跃 / 结束回合）；
    ///   · 左下：暂停 / 返回文字钮；模态：暂停 / 结算 / 返回确认统一 <see cref="UiKit.CreateModal"/>。
    ///
    /// 【皮肤】全部走 <see cref="UiKit"/> / <see cref="PixelSkin"/>（像素件九宫格）。
    /// 像素件**禁止 Image.color 乘色**：明暗色阶烘死在贴图里，状态反馈靠换贴图
    /// （<see cref="UiKit.ApplyThemeButton"/> 的 SpriteSwap 四态）。
    /// </summary>
    public static class BattleHudBuilder
    {
        /// <summary>武器槽位（= WeaponCatalog.Count，索引 = WeaponId 枚举值）。</summary>
        const int WeaponSlots = 17;

        /// <summary>每队血条段数 / pip 数（与 BattleHud.MaxSegmentsPerTeam 一致）。</summary>
        const int SegmentsPerTeam = 6;

        /// <summary>小地图面板名（必须与 HudMinimapSceneSetup.MinimapPanelName 一致，且为 Canvas 直接子节点）。</summary>
        public const string MinimapPanelName = "MinimapPanel";

        const float Safe = 4f;   // HUD 几何边缘安全距（画布像素；×3 时代曾按 3px 栅格取值，现值为低清栈直取）

        // ------------------------------------------------------------------
        // HUD zone 表（对齐 game-2 hud_zone_layout 的"定位归表"思路——对齐关系在这里
        // 一次算清，部件不再各自手写坐标；越界/相撞由 Build 末尾的防撞自检兜底）。
        //
        // 尺寸纪律：可见包边件不低于九宫格切片和（Track/Plate 见 PixelSkin.PlateMinRender），
        // anchoredPosition 至少取整。文字尺寸不在纪律内（原生档四档，见 UiSkin.Font）。
        // ------------------------------------------------------------------

        /// <summary>顶部带垂直中心**距屏顶**的像素（UI y 轴向上、屏顶在 1080——
        /// 锚顶件直接用本值做偏移；锚中心件（模式钮）的偏移 = 1080 - 本值 - 540）。</summary>
        const float TopBandFromTop = 8f;   // 8u

        /// <summary>队血条宽 / 高（红蓝镜像等长；段宽运行时按实际人数重排）。
        /// 沿革数字（×3 时代）：297px = 99u；24px = Track 最小渲染高。现行 99/8 为 ÷3 取整
        /// 产物——99 非 Unit=2 整数倍、8 低于 Track 贴图切片和，几何复核挂重构波。</summary>
        const float TeamBarWidth = 99f;
        const float TeamBarHeight = 8f;

        /// <summary>段间距 / 段区两端内边距（1u；与 BattleHud.SegmentGap 同源）。
        /// **两端各缩 1u**：Track 的 1u 外环在左右两缘都露出——此前只缩左端，末段盖掉
        /// 右缘外环，走查读感「血条右边界没有描边」。</summary>
        const float SegmentGap = 1f;
        const float SegmentInset = 1f;

        /// <summary>红条左端 = 小地图右缘 + 8；蓝条右端 = 1920 - 同值（镜像对称）。</summary>
        const float TeamBarInsetX = 71f;

        /// <summary>pip 尺寸 / 间距（血条正下方一排小方格，文字占位）。</summary>
        const float PipSize = 6f;    // 6u
        const float PipGap = 2f;

        /// <summary>回合徽章（暖金方环 + 黄铜宝石 + 数字）：36 = 12u——顶带只是配重，不做视觉主角。</summary>
        const float BadgeSize = 12f;

        /// <summary>模式文字钮尺寸（宽 = <see cref="UiSkin.Px.ButtonWidth"/>，高 48 = 16u，Aseprite 按钮原生高）。</summary>
        const float ModeButtonHeight = 16f;

        // ---------------- 底部带 ----------------

        /// <summary>武器面板：贴底居中（bottom = Safe）。990 = 330u、240 = 80u——
        /// 6×3 武器文字格（格 156；正文 12 原生档 5 字仅 60，宽裕）+ 底部名/说明行 + 右列。</summary>
        const float WeaponPanelWidth = 330f;
        const float WeaponPanelHeight = 80f;

        /// <summary>武器文字格尺寸 / 间距 / 列数（6×3 = 18 格，17 武器 + 1 空）。
        /// 156 = 52u = 5 字 × 10 艺术像素（位图字号档）+ 2u 余量；48 = 16u（Aseprite 按钮原生高）。</summary>
        const float WeaponCell = 52f;
        const float WeaponCellGap = 2f;
        const int WeaponColumns = 6;

        /// <summary>HUD 紧凑按钮高（48 = 16u：Aseprite 按钮原生高，模板 1:1 零拉伸）。</summary>
        const float HudButtonHeight = 16f;

        /// <summary>小地图面板尺寸（高度含标题条）。192×132 = 64×44u。</summary>
        const float MinimapWidth = 64f;
        const float MinimapHeight = 44f;
        const float MinimapCaptionHeight = 5f;

        /// <summary>构建产物：全部需要回写给 BattleHud 的引用。</summary>
        public sealed class Result
        {
            public BattleHud.TeamBarView teamBarRed;
            public BattleHud.TeamBarView teamBarBlue;
            public Image badgeRing;
            public TextMeshProUGUI badgeText;
            public TextMeshProUGUI turnHintText;
            public GameObject weaponPanelRoot;
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
            // 隔壁纪律「文字统一 StickHand」已由像素字体阶梯接管：HUD 常读文字全部走
            // <see cref="UiKit.ResolvePixelFont"/> 就近档，传入字体仅作资产缺失兜底。
            TMP_FontAsset body = MenuUiBuilder.TitleFont;
            TMP_FontAsset secondary = MenuUiBuilder.TitleFont;

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
            // 【3:1 铆定口径】画布 = 屏幕像素 1:1（ConstantPixelSize），自检基准取 1920×1080
            // 最小画布：全部件按角/边/中心锚定，画布更大只会更分散，不会产生新碰撞。
            AssertNoOverlaps(canvas.transform, hudRoot, new Vector2(960f, 540f));

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

            // 留 2px 容差（像素件贴邻不算事故）。
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

        /// <summary>建一队的血条 + pips（红蓝以屏幕中轴镜像等长；条中心距屏顶 24）。</summary>
        static BattleHud.TeamBarView BuildOneTeamBar(RectTransform hudRoot, string teamName,
            int teamIndex, bool mirror)
        {
            var view = new BattleHud.TeamBarView();

            float barTop = TopBandFromTop - TeamBarHeight * 0.5f;
            RectTransform barRoot = UiKit.CreateRect("TeamBar_" + teamName, hudRoot);
            if (mirror)
            {
                barRoot.anchorMin = barRoot.anchorMax = barRoot.pivot = new Vector2(1f, 1f);
                barRoot.anchoredPosition = new Vector2(-TeamBarInsetX, -barTop);
            }
            else
            {
                barRoot.anchorMin = barRoot.anchorMax = barRoot.pivot = new Vector2(0f, 1f);
                barRoot.anchoredPosition = new Vector2(TeamBarInsetX, -barTop);
            }
            barRoot.sizeDelta = new Vector2(TeamBarWidth, TeamBarHeight);

            // 凹槽底 = Track(Frame)（旧 progress_bg 槽的对应像素件）。
            var groove = barRoot.gameObject.AddComponent<Image>();
            groove.sprite = PixelSkin.Track(PixelTone.Frame);
            groove.type = Image.Type.Sliced;
            groove.color = Color.white;
            groove.raycastTarget = false;
            view.root = barRoot.gameObject;
            view.segmentRoot = barRoot;

            // 段：每单位一格（段内暖白 ghost + 队色 fill 双层；段间缝隙露出凹槽 = 分格读感）。
            // **两端各内缩 1u**（SegmentInset）：Track 的 1u 外环在左右两缘都露出来——
            // 修复此前「末段盖掉右缘外环、血条右边界没有描边」的走查缺陷。
            float innerWidth = TeamBarWidth - 2f * SegmentInset;
            float segmentWidth = (innerWidth - (SegmentsPerTeam - 1) * SegmentGap) / SegmentsPerTeam;
            view.segments = new BattleHud.UnitSegmentView[SegmentsPerTeam];
            for (int i = 0; i < SegmentsPerTeam; i++)
            {
                float x = SegmentInset + i * (segmentWidth + SegmentGap);
                RectTransform segment = UiKit.CreateRect("Segment_" + i, barRoot);
                segment.anchorMin = segment.anchorMax = new Vector2(0f, 0.5f);
                segment.pivot = new Vector2(0f, 0.5f);
                segment.sizeDelta = new Vector2(segmentWidth, TeamBarHeight - 6f);
                segment.anchoredPosition = new Vector2(x, 0f);

                // ghost/fill 都是 Fill 件（九宫格边框仅 1u，薄条也能安全切片）。
                Image ghost = UiKit.CreateFill("Ghost", segment, PixelFillKind.Neutral);
                UiKit.Stretch(ghost.rectTransform);
                Image fill = UiKit.CreateFill("Fill", segment, TeamFillKind(teamIndex));
                UiKit.Stretch(fill.rectTransform);

                view.segments[i] = new BattleHud.UnitSegmentView
                {
                    root = segment.gameObject,
                    fill = fill,
                    ghost = ghost,
                };
            }

            // pips：条下一排小方格（文字占位：存活空格 / 阵亡「×」由运行时写，
            // 职业头像图标已退役），与血条同侧对齐。
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

                // 格底 = Plate(Dense)（cell 槽的对应像素件）。死亡压暗走 CanvasGroup（不烘黑图）。
                Image frame = pip.gameObject.AddComponent<Image>();
                frame.sprite = PixelSkin.Plate(PixelTone.Dense);
                frame.type = Image.Type.Sliced;
                frame.color = Color.white;
                frame.raycastTarget = false;
                pip.gameObject.AddComponent<CanvasGroup>();

                TextMeshProUGUI label = UiKit.CreateText("Label", pip, string.Empty,
                    UiSkin.Font.Tiny, TextAlignmentOptions.Center, PixelSkin.PaperWhite, font: null);
                label.enableWordWrapping = false;
                UiKit.Stretch(label.rectTransform);

                view.pips[i] = new BattleHud.UnitPipView
                {
                    root = pip.gameObject,
                    label = label,
                    frame = frame,
                };
            }

            return view;
        }

        /// <summary>队号 → 填充档（0=红队 Red，其余蓝队 Blue；与头顶血条同口径）。</summary>
        static PixelFillKind TeamFillKind(int teamIndex)
        {
            return teamIndex == 0 ? PixelFillKind.Red : PixelFillKind.Blue;
        }

        // ------------------------------------------------------------------
        // 中央回合徽章 + 提示
        // ------------------------------------------------------------------

        static void BuildBadge(RectTransform hudRoot, Result result)
        {
            RectTransform badge = UiKit.CreateRect("TurnBadge", hudRoot);
            badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 1f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            badge.anchoredPosition = new Vector2(0f, -TopBandFromTop);

            // 外环 = PixelSkin.Ring（拉伸/九宫格环厚自动保持）；内核 = Pip(true) 居中。
            result.badgeRing = badge.gameObject.AddComponent<Image>();
            result.badgeRing.sprite = PixelSkin.Ring;
            result.badgeRing.type = Image.Type.Sliced;
            result.badgeRing.color = Color.white;
            result.badgeRing.raycastTarget = false;

            Image core = UiKit.CreateRect("Core", badge).gameObject.AddComponent<Image>();
            core.sprite = PixelSkin.Pip(true);
            core.type = Image.Type.Simple;
            core.color = Color.white;
            core.raycastTarget = false;
            core.rectTransform.anchorMin = core.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            core.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            core.rectTransform.sizeDelta = new Vector2(BadgeSize - 2f, BadgeSize - 2f);
            core.rectTransform.anchoredPosition = Vector2.zero;

            // 数字压在黄铜宝石上 → 墨字（像素皮调色板的浅底正文字色）。
            result.badgeText = UiKit.CreateText("TurnText", badge, "1", UiSkin.Font.Hud,
                TextAlignmentOptions.Center, PixelSkin.Ink, MenuUiBuilder.TitleFont);
            UiKit.Stretch(result.badgeText.rectTransform);

            // 提示文字（「轮到你了 / 敌方行动中」）：徽章正下方**纯文字**，
            // 深色四向 Outline + 斜投影保可读（沙地亮部会吞裸浅色字），字色 = 暖白。
            result.turnHintText = UiKit.CreateText("TurnHintText", hudRoot, string.Empty,
                UiSkin.Font.Hud, TextAlignmentOptions.Center, PixelSkin.PaperWhite, MenuUiBuilder.TitleFont);
            result.turnHintText.enableWordWrapping = false;
            result.turnHintText.rectTransform.anchorMin = result.turnHintText.rectTransform.anchorMax =
                new Vector2(0.5f, 1f);
            result.turnHintText.rectTransform.pivot = new Vector2(0.5f, 1f);
            result.turnHintText.rectTransform.sizeDelta = new Vector2(100f, 8f);
            result.turnHintText.rectTransform.anchoredPosition = new Vector2(0f,
                -TopBandFromTop - BadgeSize * 0.5f - 6f);
            var hintOutline = result.turnHintText.gameObject.AddComponent<UnityEngine.UI.Outline>();
            hintOutline.effectColor = new Color(PixelSkin.Ink.r / 255f, PixelSkin.Ink.g / 255f,
                PixelSkin.Ink.b / 255f, 0.78f);   // 调色板墨色收编（原近黑字面量）
            hintOutline.effectDistance = new Vector2(1.5f, 1.5f);
            var hintShadow = result.turnHintText.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            hintShadow.effectColor = new Color(PixelSkin.Ink.r / 255f, PixelSkin.Ink.g / 255f,
                PixelSkin.Ink.b / 255f, 0.5f);
            hintShadow.effectDistance = new Vector2(1f, -1f);
        }

        // ------------------------------------------------------------------
        // 底部：武器面板（文字格 + 右列）
        // ------------------------------------------------------------------

        static void BuildWeaponPanel(RectTransform hudRoot, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            // 底部带主体：Plate(Frame) + 投影，贴底居中，与左下系统钮共享底边线。
            RectTransform panel = UiKit.CreatePanel("WeaponPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, Safe), new Vector2(WeaponPanelWidth, WeaponPanelHeight));
            result.weaponPanelRoot = panel.gameObject;

            // ---- 武器文字格区：GridLayoutGroup 6 列（格 96×48、缝 2u）——格坐标由布局器排，
            //      容器只声明自己的位置与格尺寸，不再逐格手摆坐标。----
            float gridWidth = WeaponColumns * WeaponCell + (WeaponColumns - 1) * WeaponCellGap;
            float gridHeight = (WeaponSlots / WeaponColumns) * HudButtonHeight
                + (WeaponSlots / WeaponColumns - 1) * WeaponCellGap;
            RectTransform weaponGrid = UiKit.CreateRect("WeaponGrid", panel);
            weaponGrid.pivot = new Vector2(0f, 1f);
            UiKit.SetAnchored(weaponGrid, new Vector2(0f, 1f), new Vector2(gridWidth, gridHeight),
                new Vector2(4f, -4f));
            UiLayout.Grid(weaponGrid, new Vector2(WeaponCell, HudButtonHeight), WeaponColumns, 2);

            var buttons = new Button[WeaponSlots];
            var frames = new Image[WeaponSlots];

            for (int i = 0; i < WeaponSlots; i++)
            {
                RectTransform cell = UiKit.CreateRect("WeaponCell_" + (WeaponId)i, weaponGrid);

                // 格底 = Plate(Dense) 三态换图（SpriteSwap）；选中态 = 悬停档 + Focus 环（运行时开）。
                Image frame = cell.gameObject.AddComponent<Image>();
                var button = cell.gameObject.AddComponent<Button>();
                UiKit.ApplyThemeButton(button, frame);
                UiKit.CreateFocusRing("Focus", cell);

                // 文字占位（2026-09-24 创始人裁决：物品图标全部退役）：格中 = 武器中文名。
                // 位图字号禁 AutoSize（非原生档必然糊栅格）——格宽 156 本就装得下 5 字全名。
                TextMeshProUGUI label = UiKit.CreateText("Name", cell,
                    UiTextRules.WeaponName((WeaponId)i), UiSkin.Font.Body,
                    TextAlignmentOptions.Center, PixelSkin.PaperWhite, secondary);
                label.enableWordWrapping = false;
                label.enableAutoSizing = false;
                UiKit.Stretch(label.rectTransform, 3f);

                buttons[i] = button;
                frames[i] = frame;
            }

            result.weaponButtons = buttons;
            result.weaponFrames = frames;

            // ---- 底部说明行：武器名（黄铜强调）+ 说明（次级暖白，单行截断） ----
            result.weaponNameText = UiKit.CreateText("WeaponName", panel, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.MidlineLeft, PixelSkin.LightOf(PixelTone.Primary), secondary);
            result.weaponNameText.enableWordWrapping = false;
            UiKit.SetAnchored(result.weaponNameText.rectTransform, new Vector2(0f, 0f),
                new Vector2(100f, 6f), new Vector2(4f, 14f));

            result.weaponDescText = UiKit.CreateText("WeaponDesc", panel, string.Empty, UiSkin.Font.Hint,
                TextAlignmentOptions.MidlineLeft, UiSkin.WithAlpha(PixelSkin.PaperWhite, 0.72f), secondary);
            result.weaponDescText.enableWordWrapping = false;
            result.weaponDescText.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.SetAnchored(result.weaponDescText.rectTransform, new Vector2(0f, 0f),
                new Vector2(152f, 6f), new Vector2(4f, 7f));

            // ---- 右列：名 / HP / 跳跃 / 结束回合（当前单位信息区；头像格随图标退役）----
            // ---- 右列：两枚文字钮顶上、名 / HP 贴底（VBox 声明顺序 + 弹性占位顶开，
            //      右缘/顶缘各留 12 内边距；不再手摆每个元素的 y 坐标）。----
            RectTransform unitColumn = UiKit.CreateRect("UnitColumn", panel);
            unitColumn.pivot = new Vector2(1f, 1f);
            UiKit.SetAnchored(unitColumn, new Vector2(1f, 1f), new Vector2(150f, 216f),
                new Vector2(-4f, -4f));
            UiLayout.VBox(unitColumn, 2, default(UiPadding), controlHeights: true);

            // 紧凑文字按钮（宽 = 标签宽 + 8 艺术像素、高 48 = 16u——Aseprite 原生档）。
            result.throwSelfButton = UiKit.ActionButton("ThrowSelfButton", unitColumn,
    UiStrings.BattleThrowSelf,
                Vector2.zero,
                new Vector2(UiSkin.Px.ButtonWidth(UiStrings.BattleThrowSelf), HudButtonHeight), body);
            UiLayout.Element(result.throwSelfButton.gameObject,
                UiSkin.Px.ButtonWidth(UiStrings.BattleThrowSelf), HudButtonHeight);
            result.endGoButton = UiKit.ActionButton("EndGoButton", unitColumn,
    UiStrings.BattleEndGo,
                Vector2.zero,
                new Vector2(UiSkin.Px.ButtonWidth(UiStrings.BattleEndGo), HudButtonHeight), body);
            UiLayout.Element(result.endGoButton.gameObject,
                UiSkin.Px.ButtonWidth(UiStrings.BattleEndGo), HudButtonHeight);

            RectTransform push = UiKit.CreateRect("Push", unitColumn);
            UiLayout.Flexible(push.gameObject);

            result.unitNameText = UiKit.CreateText("UnitName", unitColumn, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.Center, PixelSkin.TextColorOn(PixelTone.Frame), body);
            result.unitNameText.enableWordWrapping = false;
            UiLayout.Element(result.unitNameText.gameObject, 150f, 18f);

            UiKit.BarView hpBar = UiKit.CreateBar("UnitHp", unitColumn,
                Vector2.zero, new Vector2(50f, UiSkin.Px.Bar),
                UiSkin.TeamRed);
            UiLayout.Element(hpBar.Root.gameObject, 150f, UiSkin.Px.Bar);
            result.unitHpBar = new BattleHud.HpBarView
            {
                track = hpBar.Track,
                ghost = hpBar.Ghost,
                fill = hpBar.Fill,
            };
        }

        // ------------------------------------------------------------------
        // 底部带（暂停 / 返回）+ 右上模式钮 + 准星
        // ------------------------------------------------------------------

        static void BuildBottomBar(RectTransform hudRoot, TMP_FontAsset secondary, Result result)
        {
            // 底部带两段共享底边线 y=Safe：[暂停 返回]（面板左外侧，**文字钮**）→ [武器面板]。
            // 【右下提示条已删】（创始人 2026-09-23 走查"没有必要"）。
            // 【坐标口径】水平 = 画布底**中心**锚（随武器面板走，画布变宽不漂移）；
            // 垂直 = 底边锚（y = Safe + 半高，任何分辨率都贴底边线）。
            float panelLeft = -(WeaponPanelWidth * 0.5f);     // 面板左缘相对底中心（-396，3 的倍数）
            float textButtonY = Safe + HudButtonHeight * 0.5f;   // 底边锚：Safe + 半高
            Vector2 pauseSize = new Vector2(UiSkin.Px.ButtonWidth(UiStrings.BattlePause), HudButtonHeight);
            Vector2 backSize = new Vector2(UiSkin.Px.ButtonWidth(UiStrings.Back), HudButtonHeight);
            result.pauseButton = UiKit.ActionButton("PauseButton", hudRoot,
                UiStrings.BattlePause,
                new Vector2(panelLeft - 6f - pauseSize.x * 0.5f, textButtonY),   // 缝 6 = 2u
                pauseSize, secondary);
            result.backButton = UiKit.ActionButton("BackButton", hudRoot,
                UiStrings.Back,
                new Vector2(panelLeft - 6f - pauseSize.x - 6f - backSize.x * 0.5f, textButtonY),
                backSize, secondary);
            // 底中心锚：UiKit.ActionButton 默认锚在画布中心，这里改挂底边（y 轴）。
            foreach (Button b in new[] { result.pauseButton, result.backButton })
            {
                RectTransform rect = (RectTransform)b.transform;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
            }

            // 右上：模式三**文字钮**成组贴右缘（0=移动 1=操作 2=观察；快捷键 1/2/3 角标保留）。
            // 选中态由运行时开环 + 换悬停档贴图（不再乘色）。
            string[] modeLabels =
            {
                UiStrings.BattleModeMove, UiStrings.BattleModeAction, UiStrings.BattleModeObserve,
            };
            result.modeButtons = new Button[3];
            result.modeFrames = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                Vector2 size = new Vector2(UiSkin.Px.ButtonWidth(modeLabels[i]) + 6f, ModeButtonHeight);   // +6：快捷键角标让位
                // 右上**角锚**（1,1）：x/y 都相对屏角累退（Safe + 同排前钮宽 + 缝），任何分辨率贴角不漂移。
                Vector2 position = new Vector2(
                    -(Safe + (2 - i) * (size.x + 6f) + size.x * 0.5f),
                    -(TopBandFromTop));
                Button button = UiKit.ActionButton("ModeButton_" + (BattleHud.BattleHudMode)i, hudRoot,
                    modeLabels[i], position, size, secondary);
                {
                    RectTransform rect = (RectTransform)button.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                }

                // 快捷键角标（右上角小字）——图标退役后快捷键提示的唯一载体。
                // 角标盒 = 10 艺术像素见方（位图字号 Tiny 档），贴钮内缘。
                TextMeshProUGUI hotkey = UiKit.CreateText("Hotkey", button.transform, (i + 1).ToString(),
                    UiSkin.Font.Tiny, TextAlignmentOptions.Center,
                    UiSkin.WithAlpha(PixelSkin.PaperWhite, 0.66f), secondary);
                hotkey.enableWordWrapping = false;
                hotkey.rectTransform.anchorMin = hotkey.rectTransform.anchorMax = new Vector2(1f, 1f);
                hotkey.rectTransform.pivot = new Vector2(1f, 1f);
                hotkey.rectTransform.sizeDelta = new Vector2(10f, 10f);
                hotkey.rectTransform.anchoredPosition = new Vector2(-3f, -3f);
                UiKit.CreateFocusRing("Focus", button.transform);

                result.modeButtons[i] = button;
                result.modeFrames[i] = button.image;
            }
        }

        /// <summary>屏幕中心准星（观察模式；FPS 式细十字）。</summary>
        static void BuildCrosshair(Transform hudRoot)
        {
            RectTransform crosshair = UiKit.CreateRect("Crosshair", hudRoot);
            UiKit.SetAnchored(crosshair, new Vector2(0.5f, 0.5f), new Vector2(6f, 6f), Vector2.zero);

            void Bar(string name, float w, float h)
            {
                var rect = UiKit.CreateRect(name, crosshair);
                rect.sizeDelta = new Vector2(w, h);
                rect.anchoredPosition = Vector2.zero;
                var img = rect.gameObject.AddComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = false;
                var outline = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(PixelSkin.Ink.r / 255f, PixelSkin.Ink.g / 255f,
                    PixelSkin.Ink.b / 255f, 0.55f);   // 调色板墨色收编
                outline.effectDistance = new Vector2(1f, -1f);
            }
            Bar("CrossH", 18f, 2f);
            Bar("CrossV", 2f, 18f);
        }

        // ------------------------------------------------------------------
        // 模态层
        // ------------------------------------------------------------------

        /// <summary>暂停面板：Dim + 卡片 + 继续 / 再来一局 / 返回主菜单（三钮同宽纵排）。</summary>
        static void BuildPausePanel(RectTransform hudRoot, TMP_FontAsset title, TMP_FontAsset body,
            Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("PausePanel", hudRoot, new Vector2(120f, 84f),
                title: UiStrings.BattlePauseTitle, titleFont: title, titleFontSize: UiSkin.Font.Title,
                closeButton: false);   // 隐藏 ≠ 取消暂停——恢复必须走「继续」按钮
            result.pausePanelRoot = modal.Root;
            result.pauseCard = modal.Card;

            // 流式内容（UiLayout）：三钮纵排（标题已上窗体标题带，不再单独建横幅）——
            // 间距/内边距全 u 档，卡片内不再手摆 y 坐标。
            // Flow 是卡片 VBox 的弹性子件（宽吃满内容区）；卡片高由 ContentSizeFitter 贴内容。
            RectTransform flow = UiKit.CreateRect("Flow", modal.Card);
            UiLayout.Flexible(flow.gameObject);
            UiLayout.VBox(flow, 2, UiPadding.Uniform(2), alignment: TextAnchor.MiddleCenter, controlHeights: true);

            // 三钮 = 文字贴合（创始人裁决：按钮大小跟文字走，不再统一最宽档）；纵排顺序即声明顺序。
            result.resumeButton = UiKit.ActionButton("ResumeButton", flow,
    UiStrings.BattleResume,
                Vector2.zero, new Vector2(35f, HudButtonHeight), body);
            UiKit.FitToLabel(result.resumeButton);
            result.pauseRestartButton = UiKit.ActionButton("PauseRestartButton", flow,
    UiStrings.BattleRestart,
                Vector2.zero, new Vector2(35f, HudButtonHeight), body);
            UiKit.FitToLabel(result.pauseRestartButton);
            result.pauseBackButton = UiKit.ActionButton("PauseBackButton", flow,
    UiStrings.BackToMainMenu,
                Vector2.zero, new Vector2(35f, HudButtonHeight), body);
            UiKit.FitToLabel(result.pauseBackButton);
        }

        /// <summary>结算面板：横幅 + 三星 + 明细 + 再来一局 / 返回。
        /// 按钮成对收进卡内（旧版按钮挂在卡外是历史布局事故，本波一并归位）。</summary>
        static void BuildSettlementPanel(RectTransform hudRoot, TMP_FontAsset title, TMP_FontAsset secondary,
            Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("SettlementPanel", hudRoot, new Vector2(200f, 140f));
            result.settlementPanelRoot = modal.Root;
            result.settlementCard = modal.Card;

            // 流式内容：标题 / 星级行 / 明细 / 按钮行——VBox 声明顺序 = 视觉顺序，纵缝全 u 档。
            RectTransform flow = UiKit.CreateRect("Flow", modal.Card);
            UiLayout.Flexible(flow.gameObject);
            UiLayout.VBox(flow, 2, UiPadding.Uniform(2), alignment: TextAnchor.MiddleCenter, controlHeights: true);

            result.settlementTitleText = UiKit.CreateText("SettlementTitle", flow, string.Empty,
                UiSkin.Font.Banner, TextAlignmentOptions.Center, PixelSkin.LightOf(PixelTone.Primary), title);
            UiLayout.Element(result.settlementTitleText.gameObject, 160f, 16f);

            RectTransform starRow = UiKit.CreateRect("Stars", flow);
            UiLayout.HStack(starRow, 3, default(UiPadding), alignment: TextAnchor.MiddleCenter);
            var stars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                // 星级是 icon 族（不受乘色禁令约束），但仍取调色板：暗星 = 暖白压 alpha。
                Image star = UiKit.CreateGlyph("Star" + i, starRow, UiGlyphs.Glyph.Star,
                    UiSkin.WithAlpha(PixelSkin.PaperWhite, 0.28f));
                star.rectTransform.sizeDelta = new Vector2(12f, 12f);
                UiLayout.Element(star.gameObject, 12f, 12f);
                stars[i] = star;
            }
            result.settlementStars = stars;

            result.settlementLinesText = UiKit.CreateText("SettlementLines", flow, string.Empty,
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.TextColorOn(PixelTone.Frame), secondary);
            UiLayout.Element(result.settlementLinesText.gameObject, 160f, 56f);

            RectTransform actionRow = UiKit.CreateRect("Actions", flow);
            UiLayout.HStack(actionRow, 4, default(UiPadding), alignment: TextAnchor.MiddleCenter);
            result.settlementRestartButton = UiKit.ActionButton("SettlementRestartButton", actionRow,
    UiStrings.BattleRestart,
                Vector2.zero, new Vector2(30f, HudButtonHeight), secondary);
            UiKit.FitToLabel(result.settlementRestartButton);
            result.settlementBackButton = UiKit.ActionButton("SettlementBackButton", actionRow,
    UiStrings.SettlementBackToSelect,
                Vector2.zero, new Vector2(30f, HudButtonHeight), secondary);
            UiKit.FitToLabel(result.settlementBackButton);
        }

        /// <summary>返回确认弹窗（挂在 Canvas 直下压过全部元素）。</summary>
        static void BuildBackConfirm(Transform canvas, TMP_FontAsset body, Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("BackConfirmDialog", canvas, new Vector2(160f, 68f),
                title: UiStrings.ConfirmTitle, titleFont: body, titleFontSize: UiSkin.Font.Body);
            result.confirmDialogRoot = modal.Root;
            result.confirmCard = modal.Card;

            // 流式内容：正文 + 按钮行（VBox 居中块，缝 3u）。
            RectTransform flow = UiKit.CreateRect("Flow", modal.Card);
            UiLayout.Flexible(flow.gameObject);
            UiLayout.VBox(flow, 3, UiPadding.Uniform(2), alignment: TextAnchor.MiddleCenter, controlHeights: true);

            result.confirmMessage = UiKit.CreateText("Message", flow, UiStrings.BackConfirm,
                UiSkin.Font.Section, TextAlignmentOptions.Center, PixelSkin.TextColorOn(PixelTone.Frame), body);
            UiLayout.Element(result.confirmMessage.gameObject, 134f, 20f);   // 宽取偶：内容区 148 居中整格（133 落 x.5 半格）

            RectTransform actionRow = UiKit.CreateRect("Actions", flow);
            UiLayout.HStack(actionRow, 4, default(UiPadding), alignment: TextAnchor.MiddleCenter);
            result.confirmOkButton = UiKit.ActionButton("OkButton", actionRow,
    UiStrings.Confirm,
                Vector2.zero, new Vector2(UiSkin.Px.ButtonWidth(UiStrings.Confirm), HudButtonHeight), body);
            UiKit.FitToLabel(result.confirmOkButton);
            result.confirmCancelButton = UiKit.ActionButton("CancelButton", actionRow,
    UiStrings.Cancel,
                Vector2.zero, new Vector2(UiSkin.Px.ButtonWidth(UiStrings.Cancel), HudButtonHeight), body);
            UiKit.FitToLabel(result.confirmCancelButton);
        }

        // ------------------------------------------------------------------
        // 小地图（左上；面板名/层级契约不变）
        // ------------------------------------------------------------------

        static void BuildMinimap(Transform canvas)
        {
            Transform existing = canvas.Find(MinimapPanelName);
            RectTransform panel = existing as RectTransform;
            if (panel == null)
                panel = UiKit.CreateRect(MinimapPanelName, canvas);

            UiKit.SetAnchored(panel, new Vector2(0f, 1f), new Vector2(MinimapWidth, MinimapHeight),
                new Vector2(Safe, -Safe));

            // 复用旧面板时清掉历史根 Image 与描边（外观件统一由 EnsurePanel 兜底——
            // 场景重存会洗组件，这是历史教训）。
            var legacyImage = panel.GetComponent<Image>();
            if (legacyImage != null)
                Object.DestroyImmediate(legacyImage);
            var legacyOutline = panel.GetComponent<Outline>();
            if (legacyOutline != null)
                Object.DestroyImmediate(legacyOutline);

            // 海图容器 = Plate(Sea) + 投影。
            UiKit.EnsurePanel(panel, PixelTone.Sea);

            // 点阵层：四周退内边距，顶部让出标题条（HudMinimapSceneSetup 契约）。
            RectTransform dotLayer = panel.Find("DotLayer") as RectTransform;
            if (dotLayer == null)
                dotLayer = UiKit.CreateRect("DotLayer", panel);

            dotLayer.anchorMin = Vector2.zero;
            dotLayer.anchorMax = Vector2.one;
            dotLayer.pivot = new Vector2(0.5f, 0.5f);
            dotLayer.offsetMin = new Vector2(6f, 6f);
            dotLayer.offsetMax = new Vector2(-6f, -(6f + MinimapCaptionHeight));

            // 内底 = Track(Sea) 凹槽，垫在点位层之下（序号 0）。
            Image well = FindOrCreateImage(dotLayer, "Well", PixelTone.Sea);
            well.rectTransform.SetSiblingIndex(0);
            UiKit.Stretch(well.rectTransform);

            EnsureChildRect(dotLayer, "TileLayer");

            TextMeshProUGUI caption = null;
            Transform captionNode = panel.Find("MinimapCaption");
            if (captionNode != null)
                caption = captionNode.GetComponent<TextMeshProUGUI>();
            if (caption == null)
            {
                caption = UiKit.CreateText("MinimapCaption", panel, UiStrings.BattleMinimapTitle,
                    UiSkin.Font.Hint, TextAlignmentOptions.MidlineLeft,
                    PixelSkin.TextColorOn(PixelTone.Sea), MenuUiBuilder.TitleFont);
            }

            UiKit.SetAnchored(caption.rectTransform, new Vector2(0f, 1f),
                new Vector2(34f, MinimapCaptionHeight), new Vector2(2f, -1.5f));
        }

        /// <summary>取（必要时建）名为 <paramref name="name"/> 的凹槽 Image（幂等重建用）。</summary>
        static Image FindOrCreateImage(RectTransform parent, string name, PixelTone tone)
        {
            Transform child = parent.Find(name);
            Image image = child != null ? child.GetComponent<Image>() : null;
            if (image == null)
                image = UiKit.CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Track(tone);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        static void EnsureChildRect(Transform parent, string name)
        {
            if (parent.Find(name) != null)
                return;

            RectTransform rect = UiKit.CreateRect(name, parent);
            UiKit.Stretch(rect);
        }
    }
}
