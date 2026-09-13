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
    /// 【布局口径（本轮统一）】
    ///   · 外安全边距 <see cref="Safe"/>：所有角落面板统一 24px（实测可见边 23px 是描边 1px 内缩）；
    ///   · 面板内边距 <see cref="PanelPadding"/>：标题/标签一律退到面板边 24px 以内，不压边框；
    ///   · 通栏条（底部操作提示条）是唯一允许近贴边的元素，仍留 16px 底距，顶边落在屏高 95% 以下；
    ///   · 相邻信息块之间用全角间隔符 <see cref="SeparatorDot"/> 分隔，避免「1/20移动」「操作时间」连读。
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

        /// <summary>外安全边距（§1.7：角落 HUD 件离屏边 ≥24px）。</summary>
        const float Safe = 24f;

        /// <summary>面板内边距（统一口径：标题/标签/内容都退到面板边 24px 以内）。</summary>
        const float PanelPadding = 24f;

        /// <summary>回合/计时文本宽度（放在模式开关两侧，给两侧留出 24px 以上间隔）。</summary>
        const float TopRowWidth = 120f;

        /// <summary>提示条高度（底部通栏条，§1.7 允许近贴边的例外）。</summary>
        const float HintBarHeight = 36f;

        /// <summary>提示条宽度（收窄居中，不再横跨可玩区）。</summary>
        const float HintBarWidth = 960f;

        /// <summary>名册行距（≈ 名册字号 24 × 1.5，规范 §1.4 建议 1.4-1.6 倍字号）。</summary>
        const float RosterRowPitch = 36f;

        /// <summary>名册行高（小于行距，行间留 2px）。</summary>
        const float RosterRowHeight = 34f;

        /// <summary>名册标题条占位 = 内边距 24 + 标题高 32 + 与首行间距 16。</summary>
        const float RosterTitleStrip = 72f;

        /// <summary>名册面板宽度。</summary>
        const float RosterWidth = 368f;

        /// <summary>名册面板高度 = 标题条 + 12 行 × 行距 + 底部内边距。</summary>
        const float RosterHeight = RosterTitleStrip + RosterRows * RosterRowPitch + PanelPadding;

        /// <summary>小地图面板宽度。</summary>
        const float MinimapWidth = 260f;

        /// <summary>小地图面板高度（含标题条与上下内边距）。</summary>
        const float MinimapHeight = 140f;

        /// <summary>小地图标题高度（标题条 = 该值 + 8px 间距）。</summary>
        const float MinimapCaptionHeight = 22f;

        /// <summary>武器面板高度。</summary>
        const float WeaponPanelHeight = 300f;

        /// <summary>底部堆叠：提示条底距（通栏条贴边例外，仍留 16px）。</summary>
        const float HintBarBottom = 16f;

        /// <summary>程序化材质感：垂直亮度微渐变幅度 ±4%（不引入贴图，只用低 alpha 色带）。</summary>
        const float ShadeAlpha = 0.04f;

        /// <summary>
        /// 全角间隔符：分隔相邻信息块（「回合 1/20 · 移动」）。
        /// 【提案/待定】文案唯一来源应是 <c>UiStrings</c>；该文件不在本轮可改文件域内，故暂置本地常量，
        /// 后续收口时挪到 UiStrings（非英文字面量，不违反「零英文残留」红线）。
        /// </summary>
        const string SeparatorDot = "·";

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
            BuildLabelsAndHints(hudRoot, body, secondary, result);

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

            // 回合计数（模式开关左侧）与计时（右侧）：文本矩形都落在模式面板之外，
            // 两侧各加一个全角间隔符，避免与相邻面板文字连读成「回合 1/20移动」「操作时间 0:00」。
            TextMeshProUGUI turn = MenuUiBuilder.CreateText("TurnCounterText", hudRoot,
                UiTextRules.TurnCounter(1, 20), UiTheme.FontHud, TextAlignmentOptions.MidlineRight,
                UiTheme.BrassLight, body);
            MenuUiBuilder.SetAnchored(turn.rectTransform, new Vector2(0.5f, 1f), new Vector2(TopRowWidth, 36f),
                new Vector2(-294f, -30f));
            CreateSeparator(hudRoot, "TurnSeparator", new Vector2(-222f, -30f), body);

            TextMeshProUGUI timer = MenuUiBuilder.CreateText("TimerText", hudRoot,
                UiTextRules.Timer(0), UiTheme.FontHud, TextAlignmentOptions.MidlineLeft,
                UiTheme.TextLight, body);
            MenuUiBuilder.SetAnchored(timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(TopRowWidth, 36f),
                new Vector2(294f, -30f));
            CreateSeparator(hudRoot, "TimerSeparator", new Vector2(222f, -30f), body);

            // 右上：回合提示 + 双方存活（内边距 24，与其它面板统一）。
            RectTransform status = MenuUiBuilder.CreatePanel("TeamStatusPanel", hudRoot,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Safe, -Safe),
                new Vector2(560f, 120f), UiSprites.Kind.PanelWood);

            TextMeshProUGUI hint = MenuUiBuilder.CreateText("TurnHintText", status,
                string.Empty, UiTheme.FontHud, TextAlignmentOptions.TopRight, UiTheme.BrassLight, body);
            MenuUiBuilder.SetAnchored(hint.rectTransform, new Vector2(1f, 1f), new Vector2(512f, 34f),
                new Vector2(-PanelPadding, -PanelPadding));

            TextMeshProUGUI teamStatus = MenuUiBuilder.CreateText("TeamStatusText", status,
                string.Empty, UiTheme.FontBody, TextAlignmentOptions.TopRight, UiTheme.TextLight, secondary);
            MenuUiBuilder.SetAnchored(teamStatus.rectTransform, new Vector2(1f, 1f), new Vector2(512f, 30f),
                new Vector2(-PanelPadding, -66f));

            result.turnHintText = hint;
            result.teamStatusText = teamStatus;
        }

        /// <summary>
        /// 顶部信息条的全角间隔符（「回合 1/20 · 移动」口径）。
        /// 【提案/待定】常量收敛到 UiStrings 见 <see cref="SeparatorDot"/>。
        /// </summary>
        static void CreateSeparator(Transform parent, string name, Vector2 anchoredPosition, TMP_FontAsset font)
        {
            TextMeshProUGUI dot = MenuUiBuilder.CreateText(name, parent, SeparatorDot, UiTheme.FontHud,
                TextAlignmentOptions.Center, UiTheme.BrassLight, font);
            MenuUiBuilder.SetAnchored(dot.rectTransform, new Vector2(0.5f, 1f), new Vector2(24f, 36f),
                anchoredPosition);
        }

        /// <summary>模式开关的一段（铜底或木底的静态块 + 居中文字 + 程序化材质感）。</summary>
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

            // 竖条分隔线的替代：段与段之间由面板底透出（模式开关面板 = PanelWood）。
            AddMaterialShade(segment);

            TextMeshProUGUI text = MenuUiBuilder.CreateText("Label", segment, label, UiTheme.FontBody,
                TextAlignmentOptions.Center, labelColor, font);
            MenuUiBuilder.Stretch(text.rectTransform);
        }

        /// <summary>
        /// 给面板/色块加程序化材质感（规范 §1.2「不做贴图」约束下的材质暗示）：
        /// 上半透一层极淡亮色、下半透一层极淡暗色，形成 ±4% 的垂直亮度微渐变；
        /// 圆角与 1-2px 深色描边由底下的九宫格 Sprite 负责，色带横向内缩 4px 不盖住圆角。
        /// </summary>
        static void AddMaterialShade(RectTransform target)
        {
            AddShadeBand(target, "ShadeTop", 0.5f, 1f, UiTheme.TextLight);
            AddShadeBand(target, "ShadeBottom", 0f, 0.5f, UiTheme.Ink);
        }

        static void AddShadeBand(RectTransform target, string name, float anchorYMin, float anchorYMax, Color tint)
        {
            RectTransform band = MenuUiBuilder.CreateRect(name, target);
            band.anchorMin = new Vector2(0f, anchorYMin);
            band.anchorMax = new Vector2(1f, anchorYMax);
            band.pivot = new Vector2(0.5f, 0.5f);
            band.offsetMin = new Vector2(4f, 0f);
            band.offsetMax = new Vector2(-4f, 0f);

            var image = band.gameObject.AddComponent<Image>();
            image.color = UiTheme.WithAlpha(tint, ShadeAlpha);
            image.raycastTarget = false;
        }

        /// <summary>
        /// 小地图（左上，§3.5）：Canvas 直接子节点名为 <c>MinimapPanel</c>，
        /// 供 <c>HudMinimapSceneSetup</c> 复用并补 <c>DotLayer</c>/<c>TileLayer</c> 与 BattleMinimap 接线。
        ///
        /// 【样式归属】面板的位置/尺寸/底色/描边由本方法每次重建时强制写回（HudMinimapSceneSetup 只接线、不覆写），
        /// 否则场景里旧面板的木棕底会一直残留——海图面板必须是水蓝系底（§1.3 `UI_SEA`）。
        /// </summary>
        static void BuildMinimap(Transform canvas, TMP_FontAsset secondary)
        {
            Transform existing = canvas.Find(MinimapPanelName);
            RectTransform panel = existing as RectTransform;
            if (panel == null)
                panel = MenuUiBuilder.CreateRect(MinimapPanelName, canvas);

            MenuUiBuilder.SetAnchored(panel, new Vector2(0f, 1f), new Vector2(MinimapWidth, MinimapHeight),
                new Vector2(Safe, -Safe));
            ApplyMinimapSkin(panel);

            // 点阵层：四周退到面板内边距，顶部再让出标题条（避免点阵贴到面板边框）。
            RectTransform dotLayer = panel.Find("DotLayer") as RectTransform;
            if (dotLayer == null)
                dotLayer = MenuUiBuilder.CreateRect("DotLayer", panel);

            dotLayer.anchorMin = Vector2.zero;
            dotLayer.anchorMax = Vector2.one;
            dotLayer.pivot = new Vector2(0.5f, 0.5f);
            dotLayer.offsetMin = new Vector2(PanelPadding, PanelPadding);
            dotLayer.offsetMax = new Vector2(-PanelPadding, -(PanelPadding + MinimapCaptionHeight + 8f));

            EnsureChildRect(dotLayer, "TileLayer");

            // 装饰性标题：移入面板安全区（内边距 24），不再压左下边框。
            TextMeshProUGUI caption = null;
            Transform captionNode = panel.Find("MinimapCaption");
            if (captionNode != null)
                caption = captionNode.GetComponent<TextMeshProUGUI>();
            if (caption == null)
            {
                caption = MenuUiBuilder.CreateText("MinimapCaption", panel,
                    UiStrings.BattleMinimapTitle, UiTheme.FontTiny, TextAlignmentOptions.MidlineLeft,
                    UiTheme.BrassLight, secondary);
            }

            MenuUiBuilder.SetAnchored(caption.rectTransform, new Vector2(0f, 1f),
                new Vector2(140f, MinimapCaptionHeight), new Vector2(PanelPadding, -PanelPadding));
        }

        /// <summary>小地图面板外观：羊皮纸九宫格 × 海水蓝 = 水蓝系海图底 + 黄铜框（【提案/待定】色值取 §1.3 Token）。</summary>
        static void ApplyMinimapSkin(RectTransform panel)
        {
            var image = panel.GetComponent<Image>();
            if (image == null)
                image = panel.gameObject.AddComponent<Image>();

            image.sprite = MenuUiBuilder.GetSprite(UiSprites.Kind.PanelParchment);
            image.type = Image.Type.Sliced;
            // 羊皮纸底色 × UI_SEA：得到水蓝系底（约 #276674），区别于木板面板的深棕。
            image.color = UiTheme.Sea;
            image.raycastTarget = false;

            MenuUiBuilder.AddOutline(panel.gameObject, UiTheme.Brass, 3f);
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
        // 左下：船员名册（标题移入面板标题条，行距 = 字号 × 1.5）
        // ------------------------------------------------------------------

        static void BuildRoster(Transform hudRoot, TMP_FontAsset title, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            RectTransform panel = MenuUiBuilder.CreatePanel("RosterPanel", hudRoot,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Safe, 120f),
                new Vector2(RosterWidth, RosterHeight), UiSprites.Kind.PanelWood);

            // 标题进面板标题条（内边距 24），不再压面板上边框；初值用运行期同款「船员名册 · 第 N 关」。
            TextMeshProUGUI rosterTitle = MenuUiBuilder.CreateText("RosterTitle", panel,
                UiTextRules.RosterTitle(1), UiTheme.FontSection, TextAlignmentOptions.MidlineLeft,
                UiTheme.BrassLight, title);
            MenuUiBuilder.SetAnchored(rosterTitle.rectTransform, new Vector2(0f, 1f),
                new Vector2(RosterWidth - PanelPadding * 2f, 32f), new Vector2(PanelPadding, -PanelPadding));
            result.rosterTitle = rosterTitle;

            var rows = new BattleHud.RosterRowView[RosterRows];
            for (int i = 0; i < RosterRows; i++)
                rows[i] = CreateRosterRow(panel, i, body, secondary);

            result.rosterRows = rows;
        }

        static BattleHud.RosterRowView CreateRosterRow(Transform parent, int index, TMP_FontAsset body,
            TMP_FontAsset secondary)
        {
            const float rowWidth = RosterWidth - PanelPadding * 2f;   // 320

            RectTransform row = MenuUiBuilder.CreateRect("RosterRow_" + index, parent);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(rowWidth, RosterRowHeight);
            row.anchoredPosition = new Vector2(PanelPadding, -RosterTitleStrip - index * RosterRowPitch);

            // 队伍色块。
            RectTransform swatch = MenuUiBuilder.CreateRect("Swatch", row);
            MenuUiBuilder.SetAnchored(swatch, new Vector2(0f, 1f), new Vector2(10f, 22f), new Vector2(0f, -6f));
            var swatchImage = swatch.gameObject.AddComponent<Image>();
            swatchImage.color = UiTheme.TeamRed;
            swatchImage.raycastTarget = false;

            // 姓名（队伍 + 职业中文）。字号取 FontHud 24：截图实测 FontBody 20 的字形带仅 18px，
            // 低于 V2「正文中文字号 ≥20px」下限；Q-16 在《美术风格指南》§11 的落地值即为 24（FontHud）。
            TextMeshProUGUI name = MenuUiBuilder.CreateText("NameText", row, string.Empty,
                UiTheme.FontHud, TextAlignmentOptions.MidlineLeft, UiTheme.TextLight, body);
            name.enableWordWrapping = false;
            MenuUiBuilder.SetAnchored(name.rectTransform, new Vector2(0f, 1f), new Vector2(160f, 30f),
                new Vector2(14f, -2f));

            // 血条底 + 填充。
            RectTransform barBg = MenuUiBuilder.CreateRect("BarBg", row);
            MenuUiBuilder.SetAnchored(barBg, new Vector2(0f, 1f), new Vector2(80f, 14f), new Vector2(178f, -10f));
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
            hp.enableWordWrapping = false;
            MenuUiBuilder.SetAnchored(hp.rectTransform, new Vector2(0f, 1f), new Vector2(62f, 28f),
                new Vector2(258f, -3f));

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
            // 底部堆叠（自下而上）：提示条 16..52 → 武器面板 72..372；面板整体上移给底缘提示条让位。
            RectTransform panel = MenuUiBuilder.CreatePanel("WeaponPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Safe * 3f),
                new Vector2(960f, WeaponPanelHeight), UiSprites.Kind.PanelWood);
            result.weaponPanelRoot = panel.gameObject;

            TextMeshProUGUI panelTitle = MenuUiBuilder.CreateText("WeaponPanelTitle", panel, string.Empty,
                UiTheme.FontSection, TextAlignmentOptions.Center, UiTheme.BrassLight, title);
            MenuUiBuilder.SetAnchored(panelTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(912f, 34f),
                new Vector2(0f, -PanelPadding));
            result.weaponPanelTitle = panelTitle;

            Button throwButton = MenuUiBuilder.CreateButton("ThrowSelfButton", panel, UiStrings.BattleThrowSelf,
                new Vector2(0.5f, 1f), new Vector2(-110f, -86f), new Vector2(200f, 40f), body,
                UiSprites.Kind.ButtonWood, UiTheme.TextLight, UiTheme.FontHint);
            result.throwSelfButton = throwButton;

            Button endGoButton = MenuUiBuilder.CreateButton("EndGoButton", panel, UiStrings.BattleEndGo,
                new Vector2(0.5f, 1f), new Vector2(110f, -86f), new Vector2(200f, 40f), body,
                UiSprites.Kind.ButtonWood, UiTheme.TextLight, UiTheme.FontHint);
            result.endGoButton = endGoButton;

            TextMeshProUGUI listTitle = MenuUiBuilder.CreateText("WeaponListTitle", panel,
                UiStrings.BattleWeaponListTitle, UiTheme.FontHint, TextAlignmentOptions.MidlineLeft,
                UiTheme.TextLight, secondary);
            MenuUiBuilder.SetAnchored(listTitle.rectTransform, new Vector2(0f, 1f), new Vector2(240f, 24f),
                new Vector2(PanelPadding, -124f));

            BuildWeaponScroll(panel, body, result);
        }

        static void BuildWeaponScroll(RectTransform panel, TMP_FontAsset body, Result result)
        {
            RectTransform scrollGo = MenuUiBuilder.CreateRect("WeaponScroll", panel);
            MenuUiBuilder.SetAnchored(scrollGo, new Vector2(0.5f, 1f), new Vector2(912f, 116f),
                new Vector2(0f, -156f));

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

        static void BuildLabelsAndHints(Transform hudRoot, TMP_FontAsset body, TMP_FontAsset secondary,
            Result result)
        {
            // 操作提示条：移到屏幕底缘（顶边距屏底 52px，落在屏高 95% 以下），
            // 收窄到 960px 居中，与名册/武器面板保持 ≥16px 间距，不再横跨可玩区。
            RectTransform hintBar = MenuUiBuilder.CreatePanel("HintBar", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, HintBarBottom),
                new Vector2(HintBarWidth, HintBarHeight), UiSprites.Kind.PanelParchment, brassOutline: false);
            var hintImage = hintBar.GetComponent<Image>();
            if (hintImage != null)
                hintImage.color = UiTheme.WithAlpha(Color.white, 0.88f);
            AddMaterialShade(hintBar);

            TextMeshProUGUI hint = MenuUiBuilder.CreateText("HintText", hintBar,
                UiStrings.BattleHintMove, UiTheme.FontHint, TextAlignmentOptions.Center, UiTheme.Ink, secondary);
            MenuUiBuilder.Stretch(hint.rectTransform, 8f);

            // 返回主菜单（§3.5 线框图：160×40 anchor(0,0) 摆在左下角；CreateButton 的 pivot 是中心，
            // 故锚点位置取「外安全边距 + 半宽/半高」= (104,44)，按钮外框左边即 24px）。
            // 【说明】上一版 HUD 重建漏建了它，导致 BattleHud.backButton 被写空、返回通路断掉。
            Button back = MenuUiBuilder.CreateButton("BackButton", hudRoot, UiStrings.BackToMainMenu,
                new Vector2(0f, 0f), new Vector2(104f, 44f), new Vector2(160f, 40f), body,
                UiSprites.Kind.ButtonWood, UiTheme.TextLight, UiTheme.FontHint);
            result.backButton = back;

            // 瞄准 / 聚焦标签（默认隐藏，等玩法接线后由逻辑控制显隐）。
            TextMeshProUGUI aim = MenuUiBuilder.CreateText("AimLabel", hudRoot,
                UiStrings.BattleAiming, UiTheme.FontHud, TextAlignmentOptions.Center, UiTheme.Select, body);
            MenuUiBuilder.SetAnchored(aim.rectTransform, new Vector2(0.5f, 0f), new Vector2(240f, 30f),
                new Vector2(0f, Safe * 3f + WeaponPanelHeight + 8f));
            aim.gameObject.SetActive(false);

            TextMeshProUGUI focus = MenuUiBuilder.CreateText("FocusLabel", hudRoot,
                UiStrings.BattleFocusing, UiTheme.FontBody, TextAlignmentOptions.Center, UiTheme.Select, secondary);
            MenuUiBuilder.SetAnchored(focus.rectTransform, new Vector2(0.5f, 1f), new Vector2(200f, 26f),
                new Vector2(0f, -88f));
            focus.gameObject.SetActive(false);
        }
    }
}
