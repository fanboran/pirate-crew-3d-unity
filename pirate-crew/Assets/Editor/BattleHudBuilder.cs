using PirateCrew.Data;
using PirateCrew.UI;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 重建器（**本波次：Aseprite 观感换装，2026-09-28 创始人令**）。
    ///
    /// 【谁调用】<see cref="BattleUiTheme.Apply"/>（由 <c>BattleSceneSetup.BuildHud</c> 回调）；
    /// 本构建器先清掉旧 HUD 子节点再重建，产出 <see cref="Result"/> 交 BattleUiTheme 回写。
    ///
    /// 【本波裁决（创始人 2026-09-28）】游戏内 UI 整体切到菜单系统已定稿的 Aseprite dark
    /// 设计语言，窗口布局彻底重排（"完全不适配新的UI风格"）：
    ///   · **海图窗**：theme window 直切窗体 + 标题带「海图」；**内部内容清空**（暂态）——
    ///     点位 / 瓦片点阵 / 海图岛层整棵不建，<c>BattleMinimap</c> 见 dotLayer 为空即休眠；
    ///   · **武器面板**：窗体化（标题带「选择武器」），格宽 52→64（「降落伞炸弹」5 字此前
    ///     溢格），旧右列改底行动行（名 / HP / 跳跃 / 结束回合）+ 单行名/说明；
    ///   · **顶栏血条 / pips / 徽章**：beveled tone 件退役——血条 = theme 暗底平涂槽 +
    ///     队色平涂段 + 白 ghost；pips = 队色平涂方格；徽章 = theme button_selected 金面钮；
    ///   · 模式钮 / 武器格选中态 = <see cref="UiKit.ApplyThemeButton"/> sticky 金面四态
    ///     （Focus 环退役——金面本身就是选中表达，与菜单系统同源）。
    ///
    /// 【皮肤】全部走 <see cref="UiKit"/> / <see cref="PixelSkin"/>。烘焙像素件**禁止乘色**；
    /// 平涂色块（血条段 / pips / ghost / 槽底）是无烘焙色阶的素面 quad，<c>Image.color</c>
    /// 取 theme / <see cref="UiSkin"/> 令牌色不属于乘色禁令（同 <see cref="UiKit.CreateDimOverlay"/>
    /// 先例）。文字信息架构沿用 2026-09-24 三连裁决（图标退役换文字）。
    /// </summary>
    public static class BattleHudBuilder
    {
        /// <summary>武器槽位（= WeaponCatalog.Count，索引 = WeaponId 枚举值）。</summary>
        const int WeaponSlots = 17;

        /// <summary>每队血条段数 / pip 数（与 BattleHud.MaxSegmentsPerTeam 一致）。</summary>
        const int SegmentsPerTeam = 6;

        /// <summary>小地图面板名（必须与 HudMinimapSceneSetup.MinimapPanelName 一致，且为 Canvas 直接子节点）。</summary>
        public const string MinimapPanelName = "MinimapPanel";

        // 全部 HUD 几何 / 摆位常量已收敛到 BattleHudZones 单一真源（改数字只改那里），
        // 本构建器只消费；下方引用点一律写 BattleHudZones.Xxx。

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
            public GameObject operationPanelRoot;
            public TextMeshProUGUI yawReadoutText;
            public TextMeshProUGUI elevationReadoutText;
            public TextMeshProUGUI powerReadoutText;
            public Button confirmOperationButton;
            public Button cancelOperationButton;
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
            // HUD 常读文字全部走 <see cref="UiKit.ResolvePixelFont"/> 就近档，
            // 传入字体仅作资产缺失兜底。
            TMP_FontAsset body = MenuUiBuilder.TitleFont;
            TMP_FontAsset secondary = MenuUiBuilder.TitleFont;

            RectTransform hudRoot = UiKit.CreateRect("HudLayout", canvas.transform);
            UiKit.Stretch(hudRoot);

            var result = new Result();

            BuildMinimap(canvas.transform);
            BuildTeamBars(hudRoot, result);
            BuildBadge(hudRoot, result);
            BuildWeaponPanel(hudRoot, body, secondary, result);
            BuildOperationPanel(hudRoot, body, secondary, result);
            BuildBottomBar(hudRoot, secondary, result);

            // 模态层最后建（同级后建者画在上层）。
            BuildPausePanel(hudRoot, title, body, result);
            BuildSettlementPanel(hudRoot, title, secondary, result);
            BuildBackConfirm(canvas.transform, body, result);

            // zone 防撞自检（对齐 game-2 hud_zone_layout 的越界警告合同）：顶层部件
            // 两两 AABB 相交即报警——布局事故在装配期就会被点名。
            // 画布 = 恒定像素密度（ConstantPixelSize × Unit=2），自检基准取 1080p 的
            // 960×540 逻辑画布：全部件按角/边/中心锚定，画布更大只会更分散。
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
                // 休眠件也不参与：操作 HUD 与武器面板共用贴底位（互斥弹出），
                // 休眠方不占位——防撞只对"同屏可见"的部件生效。
                bool stretch = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
                bool tiny = rect.rect.width < 4f || rect.rect.height < 4f;
                if (stretch || tiny || !rect.gameObject.activeSelf)
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
        // 平涂色块（无烘焙色阶的素面 quad；Image.color 取令牌色不属于乘色禁令）
        // ------------------------------------------------------------------

        /// <summary>建平涂色块：铺满父容器（血条段 ghost/fill 等内层件）。</summary>
        static Image FlatFill(string name, Transform parent, Color color)
        {
            Image image = UiKit.CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = null;   // 素面 quad：无烘焙色阶，取色即成色
            image.color = color;
            image.raycastTarget = false;
            UiKit.Stretch(image.rectTransform);
            return image;
        }

        // ------------------------------------------------------------------
        // 顶栏：双队合成血条 + pips
        // ------------------------------------------------------------------

        static void BuildTeamBars(RectTransform hudRoot, Result result)
        {
            result.teamBarRed = BuildOneTeamBar(hudRoot, "Red", teamIndex: 0, mirror: false);
            result.teamBarBlue = BuildOneTeamBar(hudRoot, "Blue", teamIndex: 1, mirror: true);
        }

        /// <summary>建一队的血条 + pips（红蓝以屏幕中轴镜像等长；条中心距屏顶 8）。
        /// 槽 = theme 最暗面平涂（#202125），段 = 队色平涂 + 白 ghost 双层，段间 1 格缝露槽。</summary>
        static BattleHud.TeamBarView BuildOneTeamBar(RectTransform hudRoot, string teamName,
            int teamIndex, bool mirror)
        {
            var view = new BattleHud.TeamBarView();

            float barTop = BattleHudZones.TopBandFromTop - BattleHudZones.TeamBarHeight * 0.5f;
            RectTransform barRoot = UiKit.CreateRect("TeamBar_" + teamName, hudRoot);
            if (mirror)
            {
                barRoot.anchorMin = barRoot.anchorMax = barRoot.pivot = new Vector2(1f, 1f);
                barRoot.anchoredPosition = new Vector2(-BattleHudZones.TeamBarInsetX, -barTop);
            }
            else
            {
                barRoot.anchorMin = barRoot.anchorMax = barRoot.pivot = new Vector2(0f, 1f);
                barRoot.anchoredPosition = new Vector2(BattleHudZones.TeamBarInsetX, -barTop);
            }
            barRoot.sizeDelta = new Vector2(BattleHudZones.TeamBarWidth, BattleHudZones.TeamBarHeight);

            // 槽底 = theme disabled 面平涂（暗于 window_face，队色段在其上最跳）。
            var trough = barRoot.gameObject.AddComponent<Image>();
            trough.sprite = null;
            trough.color = PixelSkin.Theme.Disabled;
            trough.raycastTarget = false;
            view.root = barRoot.gameObject;
            view.segmentRoot = barRoot;

            // 段：每单位一格（段内白 ghost 垫底 + 队色 fill 在上；段间缝隙露出暗槽 = 分格读感）。
            // 段宽向下取整：平涂段边缘落整格（分数宽会在色块边缘糊出抗锯齿带）。
            float innerWidth = BattleHudZones.TeamBarWidth - 2f * BattleHudZones.SegmentInset;
            float segmentWidth = Mathf.Floor((innerWidth - (SegmentsPerTeam - 1) * BattleHudZones.SegmentGap) / SegmentsPerTeam);
            view.segments = new BattleHud.UnitSegmentView[SegmentsPerTeam];
            for (int i = 0; i < SegmentsPerTeam; i++)
            {
                float x = BattleHudZones.SegmentInset + i * (segmentWidth + BattleHudZones.SegmentGap);
                RectTransform segment = UiKit.CreateRect("Segment_" + i, barRoot);
                segment.anchorMin = segment.anchorMax = new Vector2(0f, 0.5f);
                segment.pivot = new Vector2(0f, 0.5f);
                segment.sizeDelta = new Vector2(segmentWidth, BattleHudZones.TeamBarHeight - 2f);   // 上下各让 1 露槽
                segment.anchoredPosition = new Vector2(x, 0f);

                Image ghost = FlatFill("Ghost", segment, UiSkin.DamageGhost);
                Image fill = FlatFill("Fill", segment, UiSkin.TeamFill(teamIndex));

                view.segments[i] = new BattleHud.UnitSegmentView
                {
                    root = segment.gameObject,
                    fill = fill,
                    ghost = ghost,
                };
            }

            // pips：条下一排队色方格（存活 = 队色 / 阵亡 = 「×」+ CanvasGroup 压暗，
            // 运行时写入），与血条同侧对齐。
            float pipTop = barTop + BattleHudZones.TeamBarHeight + 6f;
            RectTransform pipRoot = UiKit.CreateRect("Pips_" + teamName, hudRoot);
            if (mirror)
            {
                pipRoot.anchorMin = pipRoot.anchorMax = new Vector2(1f, 1f);
                pipRoot.pivot = new Vector2(1f, 1f);
                pipRoot.anchoredPosition = new Vector2(-BattleHudZones.TeamBarInsetX, -pipTop);
            }
            else
            {
                pipRoot.anchorMin = pipRoot.anchorMax = new Vector2(0f, 1f);
                pipRoot.pivot = new Vector2(0f, 1f);
                pipRoot.anchoredPosition = new Vector2(BattleHudZones.TeamBarInsetX, -pipTop);
            }
            pipRoot.sizeDelta = new Vector2(SegmentsPerTeam * (BattleHudZones.PipSize + BattleHudZones.PipGap) - BattleHudZones.PipGap, BattleHudZones.PipSize);
            view.pipRoot = pipRoot;

            view.pips = new BattleHud.UnitPipView[SegmentsPerTeam];
            for (int i = 0; i < SegmentsPerTeam; i++)
            {
                RectTransform pip = UiKit.CreateRect("Pip_" + i, pipRoot);
                pip.anchorMin = pip.anchorMax = new Vector2(0f, 0.5f);
                pip.pivot = new Vector2(0f, 0.5f);
                pip.sizeDelta = new Vector2(BattleHudZones.PipSize, BattleHudZones.PipSize);
                pip.anchoredPosition = new Vector2(i * (BattleHudZones.PipSize + BattleHudZones.PipGap), 0f);

                // 格底 = 队色平涂方格。死亡压暗走 CanvasGroup（平涂块无烘焙色阶，无乘色禁令顾虑）。
                // raycastTarget 开 = 本格可点（Button 的射线靶）：点击 → 相机跳到对应单位
                // （回调由运行时 BattleHud.BuildOneTeamBar 绑定；transition None，反馈走 Punch）。
                Image frame = pip.gameObject.AddComponent<Image>();
                frame.sprite = null;
                frame.color = UiSkin.TeamFill(teamIndex);
                frame.raycastTarget = true;
                var pipButton = pip.gameObject.AddComponent<Button>();
                pipButton.transition = Selectable.Transition.None;
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
                    button = pipButton,
                };
            }

            return view;
        }

        // ------------------------------------------------------------------
        // 中央回合徽章 + 提示
        // ------------------------------------------------------------------

        static void BuildBadge(RectTransform hudRoot, Result result)
        {
            RectTransform badge = UiKit.CreateRect("TurnBadge", hudRoot);
            badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 1f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.sizeDelta = new Vector2(BattleHudZones.BadgeSize, BattleHudZones.BadgeSize);
            badge.anchoredPosition = new Vector2(0f, -BattleHudZones.TopBandFromTop);

            // 徽章 = theme button_selected 金面钮（当前回合 = 选中语义，与菜单系统同源；
            // 暖金方环 + 黄铜宝石的 beveled 件随换装波退役）。
            result.badgeRing = badge.gameObject.AddComponent<Image>();
            result.badgeRing.sprite = PixelSkin.Ase("button_selected");
            result.badgeRing.type = Image.Type.Sliced;
            result.badgeRing.color = Color.white;
            result.badgeRing.raycastTarget = false;

            // 数字压金面 → theme 选中底深字；Tiny 档（两位数回合号在 12 格内不溢）。
            result.badgeText = UiKit.CreateText("TurnText", badge, "1", UiSkin.Font.Tiny,
                TextAlignmentOptions.Center, PixelSkin.Theme.SelectedText, MenuUiBuilder.TitleFont);
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
                -BattleHudZones.TopBandFromTop - BattleHudZones.BadgeSize * 0.5f - 6f);
            var hintOutline = result.turnHintText.gameObject.AddComponent<UnityEngine.UI.Outline>();
            hintOutline.effectColor = new Color(PixelSkin.Ink.r / 255f, PixelSkin.Ink.g / 255f,
                PixelSkin.Ink.b / 255f, 0.78f);   // 调色板墨色收编
            hintOutline.effectDistance = new Vector2(1.5f, 1.5f);
            var hintShadow = result.turnHintText.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            hintShadow.effectColor = new Color(PixelSkin.Ink.r / 255f, PixelSkin.Ink.g / 255f,
                PixelSkin.Ink.b / 255f, 0.5f);
            hintShadow.effectDistance = new Vector2(1f, -1f);
        }

        // ------------------------------------------------------------------
        // 底部：武器面板（theme 窗体；格区 + 行动行 + 信息行）
        // ------------------------------------------------------------------

        static void BuildWeaponPanel(RectTransform hudRoot, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            // 底部带主体：theme window 直切窗体（标题带「选择武器」），贴底居中，
            // 与左下系统钮共享底边线。
            RectTransform panel = UiKit.CreatePanel("WeaponPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, BattleHudZones.Safe), new Vector2(BattleHudZones.WeaponPanelWidth, BattleHudZones.WeaponPanelHeight));
            result.weaponPanelRoot = panel.gameObject;
            UiKit.EnsureWindow(panel, PixelTone.Frame, UiStrings.BattleWeaponListTitle,
                body, UiSkin.Font.Body, helpButton: false, closeButton: false);
            float contentTop = UiKit.WindowContentTopOf(panel);   // 12 号标题 → 23
            float border = AseLayout.Px(AseLayout.WindowBorder);

            // ---- 武器文字格区：GridLayoutGroup 6 列（格 64×16、缝 2）——格坐标由布局器排，
            //      容器只声明自己的位置与格尺寸，不再逐格手摆坐标。----
            // 行数必须 Ceiling：17/6 整除=2 会把容器高声明成 34（实际 3 行占 52），
            // 下游行动行按错误高度定位 → 整行压在格区第三行上（实拍实锤）。
            int gridRows = Mathf.CeilToInt((float)WeaponSlots / BattleHudZones.WeaponColumns);
            float gridWidth = BattleHudZones.WeaponColumns * BattleHudZones.WeaponCell + (BattleHudZones.WeaponColumns - 1) * BattleHudZones.WeaponCellGap;
            float gridHeight = gridRows * BattleHudZones.WeaponCellHeight + (gridRows - 1) * BattleHudZones.WeaponCellGap;
            RectTransform weaponGrid = UiKit.CreateRect("WeaponGrid", panel);
            weaponGrid.pivot = new Vector2(0f, 1f);
            UiKit.SetAnchored(weaponGrid, new Vector2(0f, 1f), new Vector2(gridWidth, gridHeight),
                new Vector2(border, -contentTop));
            UiLayout.Grid(weaponGrid, new Vector2(BattleHudZones.WeaponCell, BattleHudZones.WeaponCellHeight), BattleHudZones.WeaponColumns,
                (int)BattleHudZones.WeaponCellGap);

            var buttons = new Button[WeaponSlots];
            var frames = new Image[WeaponSlots];

            for (int i = 0; i < WeaponSlots; i++)
            {
                // 【产线合一 W2】武器格改出 SketchButton（theme 状态层引擎单一真源）——
                // 原手写 Image + Button + ApplyThemeButton + CreateText 四件套退役；
                // GridLayoutGroup 接管格位与尺寸（anchor/pivot 传 0.5 仅为建件默认，会被布局覆写）。
                // **文字降一号 10**（创始人 2026-09-28：全 12 号「整个面板文字都一样大」——
                // 格名是密集次级信息，降档建层级）；Hint 即原生档，位图字号禁 AutoSize。
                // 格底四态 + 已装备 sticky 金面由 SketchButton.Sticky 承担（RefreshWeaponPanel）。
                SketchButton button = SketchButton.Create(weaponGrid, "WeaponCell_" + (WeaponId)i,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(BattleHudZones.WeaponCell, BattleHudZones.WeaponCellHeight),
                    secondary, UiTextRules.WeaponName((WeaponId)i), UiSkin.Font.Hint);

                buttons[i] = button;
                frames[i] = button.image;
            }

            result.weaponButtons = buttons;
            result.weaponFrames = frames;

            // ---- 行动行（格区正下方，高 16）：单位名 + HP 条（左）… 跳跃 / 结束回合（右）。----
            float rowTop = contentTop + gridHeight + 2f;
            float rightEdge = BattleHudZones.WeaponPanelWidth - border;

            result.unitNameText = UiKit.CreateText("UnitName", panel, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.MidlineLeft, PixelSkin.PaperWhite, body);
            result.unitNameText.enableWordWrapping = false;
            UiKit.PlaceTopLeft(result.unitNameText.rectTransform, border, rowTop + 4f,
                new Vector2(64f, 12f));

            result.unitHpBar = BuildUnitHpBar(panel, border + 70f, rowTop + 4f);

            float endGoWidth = UiSkin.Px.ButtonWidth(UiStrings.BattleEndGo);
            float throwWidth = UiSkin.Px.ButtonWidth(UiStrings.BattleThrowSelf);
            result.endGoButton = TopLeftButton("EndGoButton", panel, UiStrings.BattleEndGo,
                rightEdge - endGoWidth, rowTop, new Vector2(endGoWidth, BattleHudZones.HudButtonHeight), body);
            result.throwSelfButton = TopLeftButton("ThrowSelfButton", panel, UiStrings.BattleThrowSelf,
                rightEdge - endGoWidth - 2f - throwWidth, rowTop,
                new Vector2(throwWidth, BattleHudZones.HudButtonHeight), body);

            // ---- 信息行（行动行正下方，高 12）：武器名（金强调）+ 说明（次级暖白，单行截断）。----
            float infoTop = rowTop + BattleHudZones.HudButtonHeight + 2f;
            result.weaponNameText = UiKit.CreateText("WeaponName", panel, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.MidlineLeft, PixelSkin.LightOf(PixelTone.Primary), secondary);
            result.weaponNameText.enableWordWrapping = false;
            UiKit.PlaceTopLeft(result.weaponNameText.rectTransform, border, infoTop,
                new Vector2(64f, 12f));

            result.weaponDescText = UiKit.CreateText("WeaponDesc", panel, string.Empty, UiSkin.Font.Hint,
                TextAlignmentOptions.MidlineLeft, UiSkin.WithAlpha(PixelSkin.PaperWhite, 0.72f), secondary);
            result.weaponDescText.enableWordWrapping = false;
            result.weaponDescText.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.PlaceTopLeft(result.weaponDescText.rectTransform, border + 70f, infoTop,
                new Vector2(BattleHudZones.WeaponPanelWidth - border - (border + 70f), 12f));
        }

        /// <summary>当前单位 HP 条：sunken 凹槽（theme textedit 同款）+ 白 ghost + 队色平涂填充。
        /// 槽 12 高（sunken 边框 3 → 填充带 6）。</summary>
        static BattleHud.HpBarView BuildUnitHpBar(Transform panel, float x, float y)
        {
            RectTransform root = UiKit.CreateRect("UnitHp", panel);
            UiKit.PlaceTopLeft(root, x, y, new Vector2(96f, 12f));

            var track = root.gameObject.AddComponent<Image>();
            track.sprite = PixelSkin.Sunken(false);   // theme sunken（边框 3）
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;

            BattleHud.HpBarView view = new BattleHud.HpBarView { track = track };
            foreach (string layer in new[] { "Ghost", "Fill" })
            {
                Image fill = UiKit.CreateRect(layer, root).gameObject.AddComponent<Image>();
                fill.sprite = null;   // 素面 quad
                fill.color = layer == "Ghost" ? UiSkin.DamageGhost : UiSkin.TeamRed;
                fill.raycastTarget = false;
                UiKit.Stretch(fill.rectTransform);
                // 内缩 sunken 边框宽：填充不盖凹槽描边。
                fill.rectTransform.offsetMin = new Vector2(3f, 3f);
                fill.rectTransform.offsetMax = new Vector2(-3f, -3f);
                if (layer == "Ghost")
                    view.ghost = fill;
                else
                    view.fill = fill;
            }
            return view;
        }

        /// <summary>theme 文字钮，按面板内**左上坐标**摆放（ActionButton 建件后重锚顶左）。</summary>
        static Button TopLeftButton(string name, Transform parent, string label,
            float x, float y, Vector2 size, TMP_FontAsset font)
        {
            Button button = UiKit.ActionButton(name, parent, label, Vector2.zero, size, font);
            UiKit.PlaceTopLeft((RectTransform)button.transform, x, y, size);
            return button;
        }

        /// <summary>模态按钮贴合标签后再加宽（创始人 2026-09-28 走查「文字贴边」：
        /// FitToLabel 的 ButtonPadX=8 两侧各 4 格在放大观感下贴边，模态按钮补到每侧 6 格）。</summary>
        static void FitToLabelPadded(Button button, float extraWidth)
        {
            UiKit.FitToLabel(button);
            RectTransform rect = (RectTransform)button.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x + extraWidth, rect.sizeDelta.y);
            var element = button.GetComponent<UnityEngine.UI.LayoutElement>();
            if (element != null)
            {
                element.preferredWidth += extraWidth;
                element.minWidth += extraWidth;
            }
        }

        // ------------------------------------------------------------------
        // 底部带（暂停 / 返回）+ 右上模式钮 + 准星
        // ------------------------------------------------------------------

        static void BuildBottomBar(RectTransform hudRoot, TMP_FontAsset secondary, Result result)
        {
            // 底部带两段共享底边线 y=Safe：[暂停 返回]（面板左外侧，**文字钮**）→ [武器面板]。
            // 【坐标口径】水平 = 画布底**中心**锚（随武器面板走，画布变宽不漂移）；
            // 垂直 = 底边锚（y = Safe + 半高，任何分辨率都贴底边线）。
            float panelLeft = -(BattleHudZones.WeaponPanelWidth * 0.5f);
            float textButtonY = BattleHudZones.Safe + BattleHudZones.HudButtonHeight * 0.5f;   // 底边锚：Safe + 半高
            Vector2 pauseSize = new Vector2(UiSkin.Px.ButtonWidth(UiStrings.BattlePause), BattleHudZones.HudButtonHeight);
            Vector2 backSize = new Vector2(UiSkin.Px.ButtonWidth(UiStrings.Back), BattleHudZones.HudButtonHeight);
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

            // 底部带第三段：状态条（交互操作契约 §G）——武器面板正上方居中的两行文字，
            // 第一行状态名、第二行键位提示（BattleHud.RefreshHint 五态刷新；先行纯文字）。
            // 位置锚在武器面板高度之上：操作面板与武器面板共用贴底位，状态条不随切换漂移。
            result.hintText = UiKit.CreateText("HintText", hudRoot, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.Center, PixelSkin.PaperWhite, secondary);
            result.hintText.enableWordWrapping = false;
            result.hintText.overflowMode = TextOverflowModes.Ellipsis;
            {
                RectTransform rect = result.hintText.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(BattleHudZones.HintBarWidth, BattleHudZones.HintBarHeight);
                rect.anchoredPosition = new Vector2(0f,
                    BattleHudZones.Safe + BattleHudZones.WeaponPanelHeight
                    + BattleHudZones.HintBarGapAbovePanels);
            }
        }

        // ------------------------------------------------------------------
        // 操作 HUD（操作中态：三读数 + 发射/取消；与武器面板互斥共用贴底位）
        // ------------------------------------------------------------------

        /// <summary>
        /// 建操作面板。构建后即休眠（activeSelf=false）——操作中态由 BattleHud 弹出；
        /// 休眠件不参与防撞自检（与武器面板共用贴底位是设计意图，不是事故）。
        /// </summary>
        static void BuildOperationPanel(RectTransform hudRoot, TMP_FontAsset body, TMP_FontAsset secondary,
            Result result)
        {
            RectTransform panel = UiKit.CreatePanel("OperationPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, BattleHudZones.Safe),
                new Vector2(BattleHudZones.OperationPanelWidth, BattleHudZones.OperationPanelHeight));
            result.operationPanelRoot = panel.gameObject;
            UiKit.EnsureWindow(panel, PixelTone.Frame, UiStrings.BattleOperationTitle,
                body, UiSkin.Font.Body, helpButton: false, closeButton: false);
            float contentTop = UiKit.WindowContentTopOf(panel);
            float border = AseLayout.Px(AseLayout.WindowBorder);

            // ---- 读数行：方向 xxx° / 仰角 xx° / xx%（标签静态 + 值动态，左中右三分）。----
            float rowY = contentTop;
            float colThird = (BattleHudZones.OperationPanelWidth - 2f * border) / 3f;
            result.yawReadoutText = BuildReadout(panel, "YawReadout", UiStrings.BattleYawLabel,
                border, rowY, colThird, TextAlignmentOptions.MidlineLeft, secondary);
            result.elevationReadoutText = BuildReadout(panel, "ElevationReadout", UiStrings.BattleElevationLabel,
                border + colThird, rowY, colThird, TextAlignmentOptions.Midline, secondary);
            // 力度读数不带标签（契约口径：只显百分比不标「力度」二字）。
            result.powerReadoutText = UiKit.CreateText("PowerReadout", panel, string.Empty, UiSkin.Font.Hud,
                TextAlignmentOptions.MidlineRight, PixelSkin.PaperWhite, secondary);
            result.powerReadoutText.enableWordWrapping = false;
            UiKit.PlaceTopLeft(result.powerReadoutText.rectTransform,
                border + 2f * colThird, rowY + 4f, new Vector2(colThird, BattleHudZones.OperationReadoutRowHeight));

            // ---- 按钮行：发射（确认）/ 取消。----
            float buttonTop = rowY + BattleHudZones.OperationReadoutRowHeight + 2f;
            float rightEdge = BattleHudZones.OperationPanelWidth - border;
            float confirmWidth = UiSkin.Px.ButtonWidth(UiStrings.BattleConfirmOperation);
            float cancelWidth = UiSkin.Px.ButtonWidth(UiStrings.BattleCancelOperation);
            result.confirmOperationButton = TopLeftButton("ConfirmOperationButton", panel,
                UiStrings.BattleConfirmOperation,
                rightEdge - confirmWidth, buttonTop, new Vector2(confirmWidth, BattleHudZones.HudButtonHeight), body);
            result.cancelOperationButton = TopLeftButton("CancelOperationButton", panel,
                UiStrings.BattleCancelOperation,
                rightEdge - confirmWidth - 2f - cancelWidth, buttonTop,
                new Vector2(cancelWidth, BattleHudZones.HudButtonHeight), body);

            panel.gameObject.SetActive(false);
        }

        /// <summary>建一个带静态标签 + 动态值的读数（标签小字 + 值 12 号，横向并排）。</summary>
        static TextMeshProUGUI BuildReadout(RectTransform panel, string name, string label,
            float x, float y, float width, TextAlignmentOptions valueAlignment, TMP_FontAsset font)
        {
            var labelText = UiKit.CreateText(name + "Label", panel, label, UiSkin.Font.Hint,
                TextAlignmentOptions.MidlineRight, UiSkin.WithAlpha(PixelSkin.PaperWhite, 0.72f), font);
            labelText.enableWordWrapping = false;
            UiKit.PlaceTopLeft(labelText.rectTransform, x, y + 5f, new Vector2(26f, 10f));

            var value = UiKit.CreateText(name, panel, string.Empty, UiSkin.Font.Hud,
                valueAlignment, PixelSkin.PaperWhite, font);
            value.enableWordWrapping = false;
            UiKit.PlaceTopLeft(value.rectTransform, x + 28f, y + 4f,
                new Vector2(width - 28f, BattleHudZones.OperationReadoutRowHeight));
            return value;
        }

        // ------------------------------------------------------------------
        // 模态层
        // ------------------------------------------------------------------

        /// <summary>暂停面板：Dim + 卡片 + 继续 / 再来一局 / 返回主菜单（三钮同宽纵排）。</summary>
        static void BuildPausePanel(RectTransform hudRoot, TMP_FontAsset title, TMP_FontAsset body,
            Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("PausePanel", hudRoot, new Vector2(120f, 84f),
                title: UiStrings.BattlePauseTitle, titleFont: title, titleFontSize: UiSkin.Font.Body,
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
                Vector2.zero, new Vector2(35f, BattleHudZones.HudButtonHeight), body);
            FitToLabelPadded(result.resumeButton, 4f);
            result.pauseRestartButton = UiKit.ActionButton("PauseRestartButton", flow,
                UiStrings.BattleRestart,
                Vector2.zero, new Vector2(35f, BattleHudZones.HudButtonHeight), body);
            FitToLabelPadded(result.pauseRestartButton, 4f);
            result.pauseBackButton = UiKit.ActionButton("PauseBackButton", flow,
                UiStrings.BackToMainMenu,
                Vector2.zero, new Vector2(35f, BattleHudZones.HudButtonHeight), body);
            FitToLabelPadded(result.pauseBackButton, 4f);
        }

        /// <summary>结算面板（窗体标题带「战斗结算」）：窗内胜负大字 + 三星 + 明细 + 再来一局 / 返回。</summary>
        static void BuildSettlementPanel(RectTransform hudRoot, TMP_FontAsset title, TMP_FontAsset secondary,
            Result result)
        {
            UiKit.ModalView modal = UiKit.CreateModal("SettlementPanel", hudRoot, new Vector2(200f, 140f),
                title: UiStrings.SettlementPanelTitle, titleFont: title, titleFontSize: UiSkin.Font.Body);
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
                UiSkin.Font.Body, TextAlignmentOptions.Center, PixelSkin.Theme.Text, secondary);
            UiLayout.Element(result.settlementLinesText.gameObject, 160f, 56f);

            RectTransform actionRow = UiKit.CreateRect("Actions", flow);
            UiLayout.HStack(actionRow, 4, default(UiPadding), alignment: TextAnchor.MiddleCenter);
            result.settlementRestartButton = UiKit.ActionButton("SettlementRestartButton", actionRow,
                UiStrings.BattleRestart,
                Vector2.zero, new Vector2(30f, BattleHudZones.HudButtonHeight), secondary);
            FitToLabelPadded(result.settlementRestartButton, 4f);
            result.settlementBackButton = UiKit.ActionButton("SettlementBackButton", actionRow,
                UiStrings.SettlementBackToSelect,
                Vector2.zero, new Vector2(30f, BattleHudZones.HudButtonHeight), secondary);
            FitToLabelPadded(result.settlementBackButton, 4f);
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
                UiSkin.Font.Section, TextAlignmentOptions.Center, PixelSkin.Theme.Text, body);
            UiLayout.Element(result.confirmMessage.gameObject, 134f, 20f);   // 宽取偶：内容区居中整格

            RectTransform actionRow = UiKit.CreateRect("Actions", flow);
            UiLayout.HStack(actionRow, 4, default(UiPadding), alignment: TextAnchor.MiddleCenter);
            result.confirmOkButton = UiKit.ActionButton("OkButton", actionRow,
                UiStrings.Confirm,
                Vector2.zero, new Vector2(UiSkin.Px.ButtonWidth(UiStrings.Confirm), BattleHudZones.HudButtonHeight), body);
            FitToLabelPadded(result.confirmOkButton, 4f);
            result.confirmCancelButton = UiKit.ActionButton("CancelButton", actionRow,
                UiStrings.Cancel,
                Vector2.zero, new Vector2(UiSkin.Px.ButtonWidth(UiStrings.Cancel), BattleHudZones.HudButtonHeight), body);
            FitToLabelPadded(result.confirmCancelButton, 4f);
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

            UiKit.SetAnchored(panel, new Vector2(0f, 1f), new Vector2(BattleHudZones.MinimapWidth, BattleHudZones.MinimapHeight),
                new Vector2(BattleHudZones.Safe, -BattleHudZones.Safe));

            // 复用旧面板时清掉历史根 Image 与描边（外观件统一由 EnsurePanel 兜底——
            // 场景重存会洗组件，这是历史教训）。
            var legacyImage = panel.GetComponent<Image>();
            if (legacyImage != null)
                Object.DestroyImmediate(legacyImage);
            var legacyOutline = panel.GetComponent<Outline>();
            if (legacyOutline != null)
                Object.DestroyImmediate(legacyOutline);

            // 【内容清空（创始人 2026-09-28 裁决：暂态）】旧内容层整棵销毁——点位 / 瓦片
            // 点阵 / 海图岛层 / 旧标题文字全部退场，窗内只留 theme window 直切外框 +
            // 标题带「海图」。恢复内容时三处同改：这里重建 DotLayer 子树、
            // HudMinimapSceneSetup 恢复层接线、BattleMinimap 撤休眠守卫。
            Transform legacyDots = panel.Find("DotLayer");
            if (legacyDots != null)
                Object.DestroyImmediate(legacyDots.gameObject);
            Transform legacyCaption = panel.Find("MinimapCaption");
            if (legacyCaption != null)
                Object.DestroyImmediate(legacyCaption.gameObject);

            // 海图容器 = theme window 直切窗体（标题带「海图」；无窗控钮——海图不是可关窗）。
            UiKit.EnsureWindow(panel, PixelTone.Frame, UiStrings.BattleMinimapTitle,
                MenuUiBuilder.TitleFont, UiSkin.Font.Body, helpButton: false, closeButton: false);
        }
    }
}
