using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 战斗 HUD 重建器（**本波次：半透明亚克力 / 液态玻璃重铺 + 字号整体下调一档 + 紧凑密度**）。
    ///
    /// 【谁调用】<see cref="BattleUiTheme.Apply"/>——它由 <c>M2BattleSceneSetup.BuildHud</c> 在
    /// 既有 HUD 与接线完成后回调；本构建器**先清掉旧 HUD 子节点**再重建。
    ///
    /// 【视觉语言（本波次换）】旧版是"木板 / 羊皮纸 / 黄铜 = 纯色面板"（用户原话"超级丑，纯色面板
    /// 还不如半透明"）。新版：
    ///   · 面板底 = <see cref="GlassPanelSpriteBuilder"/> 自产的半透明亚克力九宫格
    ///     （圆角 14 + 外 1px 亮描边（上亮下暗）+ 内 1px 暗描边 + 3px 更透的厚度带 + 垂直渐变
    ///      + 顶部 10% 高光带 + ±2% 噪点）；UGUI 无真模糊，取舍见该类的类注释；
    ///   · **玻璃分档 = 对比度算出来的，不是审美选择**：
    ///     <see cref="GlassPanelSpriteBuilder.Tone.Dense"/>（0.88）承载文字（名册整块 / 武器面板整块 /
    ///     提示条 / 顶栏的两块文字槽 / 状态面板的两条），
    ///     <see cref="GlassPanelSpriteBuilder.Tone.Frame"/>（0.70，更透）只做"框"（顶栏 / 状态面板），
    ///     金标题优先放内容片（7.76:1）；框架层自身也达标（4.52:1）作为兜底；
    ///     —— 全套推导与最坏叠加底（纯白场景）见 <see cref="GlassPanelSpriteBuilder"/> 与 <see cref="BattleUiTheme.Tok"/>；
    ///   · 按钮 = 玻璃四态（hover 提亮 22% + alpha 0.80→0.92 变实）；字号 = <see cref="MenuUiBuilder.FontScale"/>。
    ///
    /// 【布局口径（本波次统一，向参照物 game-2 的"紧凑贴边少占屏"看齐）】
    ///   · 外安全边距 <see cref="Safe"/> = 16px（旧 24px；参照物 SCREEN_MARGIN 是 12px）；
    ///   · 面板内边距 <see cref="PanelPadding"/> = 14px（旧 24px）；面板间距 <see cref="Gap"/> = 12px；
    ///   · 顶部信息条 = 一条 640×42 玻璃条（回合计数槽 | 模式开关两段 | 计时槽），
    ///     居中摆在**小地图（左上）与状态面板（右上）之间**，三者互不重叠（旧版 704 宽顶栏
    ///     与小地图水平重叠、把「海图」标题压住了）；
    ///   · 名册面板高度随实际出战行数收缩（VerticalLayoutGroup + ContentSizeFitter），不留空槽；
    ///   · 底部三段自下而上：提示条 16..48 → 武器面板 60..336（净间距 12）→ 名册底 360。
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

        /// <summary>外安全边距（本波次 24→16；参照物 game-2 的 SCREEN_MARGIN = 12，取中偏紧）。</summary>
        const float Safe = 16f;

        /// <summary>面板内边距（本波次 24→14）。</summary>
        const float PanelPadding = 14f;

        /// <summary>面板之间的净间距（间距系统只取 4/6/8/12/16 五档，这里用 12）。</summary>
        const float Gap = 12f;

        // ---------------- 顶部信息条 ----------------

        /// <summary>
        /// 顶部信息条宽度。【为什么是 640】条要居中(0.5,1)且不与两侧面板重叠：
        /// 小地图右缘 = 16+240 = 256，状态面板左缘 = 1920−16−320 = 1584；
        /// 居中条允许的半宽 = min(960−256−12, 1584−960−12) = min(692, 612) = 612 → ≤1224 都行，
        /// 640 是"够放 回合槽 112 + 开关 336 + 计时槽 112 + 内边距"的紧凑值。
        /// </summary>
        const float TopBarWidth = 640f;

        /// <summary>顶部信息条高度（42 = 内边距 6 ×2 + 控件 30）。</summary>
        const float TopBarHeight = 42f;

        /// <summary>条内文字槽（内容片）尺寸：回合计数 / 计时各一块。
        /// 124 宽是按最长文案反推的：「玩家 2，该你了」（回合提示，6 字 × 18px ≈ 108）留 8px 余量。</summary>
        const float TopBarSlotWidth = 124f;
        const float TopBarSlotHeight = 30f;

        /// <summary>文字槽中心偏移：槽外缘退到条边 12px → 半宽 320 − 12 − 62 = 246。</summary>
        const float TopBarSlotCenterX = 246f;

        /// <summary>模式开关单段尺寸（r12 三段：移动/操作/观察，3×104 + 2×12 缝 = 336，占条内中间区）。</summary>
        const float TopBarSegmentWidth = 104f;
        const float TopBarSegmentHeight = 30f;

        /// <summary>模式开关单段中心偏移（−112/0/+112 使三段占 −164..164，与两侧文字槽各留 32px 净间距）。</summary>
        const float TopBarSegmentCenterX = 112f;

        // ---------------- 右上状态 ----------------

        /// <summary>右上状态面板（回合提示 + 双方存活）。</summary>
        const float StatusWidth = 320f;
        const float StatusHeight = 88f;
        const float StatusChipWidth = 292f;
        const float StatusChipHeight = 28f;

        // ---------------- 底部 ----------------

        /// <summary>提示条（r12 起挪到右下角，宽度收窄避开名册/武器面板的视觉轴线）。</summary>
        const float HintBarWidth = 620f;
        const float HintBarHeight = 32f;
        const float HintBarBottom = 16f;

        /// <summary>武器面板宽度 / 高度。</summary>
        /// <remarks>高度预算（自上而下）：内边距 14 + 标题片 26 + 8 + 按钮 30 + 6 + 列表标题片 26 + 4
        /// + 列表 148 + 内边距 14 = 276；列表 148 = (行高 26 + 行距 4) × 5，即一屏 5 行、共 17 行可滚。
        /// 【为什么片高都取 ≥26】小件档九宫格切片边框是 13px，上下边框合计 26 —— 片高 &lt; 26 会被
        /// UGUI 等比压缩边框，圆角/描边被压扁。</remarks>
        const float WeaponPanelWidth = 720f;
        const float WeaponPanelHeight = 276f;

        /// <summary>武器面板底距 = 提示条顶边(16+32) + 净间距 12 = 60。</summary>
        const float WeaponPanelBottom = HintBarBottom + HintBarHeight + Gap;

        /// <summary>武器行高（列表行间距由 VerticalLayoutGroup 的 spacing=4 承担）。</summary>
        const float WeaponRowHeight = 26f;

        // ---------------- 名册（左下）----------------

        /// <summary>名册面板宽度（旧 368 → 304，向参照物的紧凑列表看齐）。</summary>
        const float RosterWidth = 304f;

        /// <summary>名册行高 / 行距（字号 18 × 1.55 ≈ 28；工单要求"行距相应收紧"）。</summary>
        const float RosterRowHeight = 28f;
        const float RosterRowPitch = 30f;

        /// <summary>名册面板底距 = 武器面板顶边(60+252) + Gap 24 = 336（避免压在武器面板上）。</summary>
        const float RosterBottom = WeaponPanelBottom + WeaponPanelHeight + 24f;

        /// <summary>
        /// 名册标题条占位 = 内边距 14 + 标题高 26 + 与首行间距 6（编辑期快照用；运行期由 VerticalLayoutGroup 决定）。
        /// 【用途】只决定**编辑期**（场景快照）里行的坐标；运行期位置由面板的 VerticalLayoutGroup 接管。
        /// </summary>
        const float RosterTitleStrip = 46f;

        /// <summary>名册面板高度 = 标题条 + 12 行 × 行距 + 底部内边距（运行期随实际行数收缩）。</summary>
        const float RosterHeight = RosterTitleStrip + RosterRows * RosterRowPitch + PanelPadding;

        // ---------------- 小地图（左上）----------------

        /// <summary>小地图面板宽度（旧 260 → 240）。</summary>
        const float MinimapWidth = 240f;

        /// <summary>小地图面板高度（含标题条与上下内边距）。</summary>
        const float MinimapHeight = 140f;

        /// <summary>小地图标题条高度（标题条 = 该值 + 6px 间距）。</summary>
        const float MinimapCaptionHeight = 22f;

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

            // 顶部信息条：一条玻璃框架，内容（文字槽/开关段）都是更实的内容片。
            RectTransform topBar = MenuUiBuilder.CreateGlassPanel("TopBar", hudRoot,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Safe),
                new Vector2(TopBarWidth, TopBarHeight), GlassPanelSpriteBuilder.Tone.Frame);

            // 回合计数（左）与计时（右）：各压一块内容片（金/浅米字在框架上对比不足，见 BattleUiTheme.Tok）。
            CreateSlotText(topBar, "TurnCounterText", -TopBarSlotCenterX, UiTextRules.TurnCounter(1, 20),
                BattleUiTheme.Tok.TitleOnGlass, body);
            CreateSlotText(topBar, "TimerText", TopBarSlotCenterX, UiTextRules.Timer(0),
                BattleUiTheme.Tok.TextOnGlass, body);

            // 模式开关（r12 用户裁决：可点击 + 快捷键角标 1/2/3，运行时 BattleHud 接 onClick 与键位）：
            // 【移动】拖拽=跳跃；【操作】炮台模式（AD 转向 WS 力度 空格发射）；【观察】我的世界同款鼠标转视角。
            // 选中段=金玻璃+深墨字+亮金外描边；未选中段=中性深玻璃+浅米字（色相区分，见 BuildToggleSegment）。
            BuildToggleSegment(topBar, "ActionSegment", UiStrings.BattleModeAction, 0f,
                false, body, "2");
            BuildToggleSegment(topBar, "ObserveSegment", UiStrings.BattleModeObserve, TopBarSegmentCenterX,
                false, body, "3");
            BuildToggleSegment(topBar, "MoveSegment", UiStrings.BattleModeMove, -TopBarSegmentCenterX,
                true, body, "1");

            // 右上：回合提示 + 双方存活（两块内容片，各自稳定底衬）。
            RectTransform status = MenuUiBuilder.CreateGlassPanel("TeamStatusPanel", hudRoot,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Safe, -Safe),
                new Vector2(StatusWidth, StatusHeight), GlassPanelSpriteBuilder.Tone.Frame);

            MenuUiBuilder.CreateDenseChip("HintChip", status, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-PanelPadding, -PanelPadding), new Vector2(StatusChipWidth, StatusChipHeight));

            TextMeshProUGUI hint = MenuUiBuilder.CreateTextExact("TurnHintText", status,
                string.Empty, MenuUiBuilder.FontScale.Hud, TextAlignmentOptions.MidlineRight,
                BattleUiTheme.Tok.TitleOnGlass, body);
            MenuUiBuilder.SetAnchored(hint.rectTransform, new Vector2(1f, 1f),
                new Vector2(StatusChipWidth - 20f, StatusChipHeight), new Vector2(-PanelPadding - 10f, -PanelPadding));

            MenuUiBuilder.CreateDenseChip("StatusChip", status, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-PanelPadding, -PanelPadding - StatusChipHeight - 4f),
                new Vector2(StatusChipWidth, StatusChipHeight));

            TextMeshProUGUI teamStatus = MenuUiBuilder.CreateTextExact("TeamStatusText", status,
                string.Empty, MenuUiBuilder.FontScale.Hud, TextAlignmentOptions.MidlineRight,
                BattleUiTheme.Tok.TextOnGlass, secondary);
            MenuUiBuilder.SetAnchored(teamStatus.rectTransform, new Vector2(1f, 1f),
                new Vector2(StatusChipWidth - 20f, StatusChipHeight),
                new Vector2(-PanelPadding - 10f, -PanelPadding - StatusChipHeight - 4f));

            result.turnHintText = hint;
            result.teamStatusText = teamStatus;
        }

        /// <summary>
        /// 顶部信息条里的文字槽：内容片（较实玻璃）+ 居中文字 + 2px <c>#2A2A2A</c> 描边。
        /// 【为什么文字要压内容片】金 <c>#F2D06B</c> 叠纯白最坏底：内容片 0.88 → 7.76:1，
        /// 框架 0.74 → 4.52:1（刚过线）；取内容片留足余量。推导见
        /// <see cref="GlassPanelSpriteBuilder"/> 类注释与 <see cref="BattleUiTheme.Tok"/>。
        /// </summary>
        static void CreateSlotText(Transform topBar, string name, float centerX, string content,
            Color textColor, TMP_FontAsset font)
        {
            MenuUiBuilder.CreateDenseChip(name + "Slot", topBar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(centerX, 0f), new Vector2(TopBarSlotWidth, TopBarSlotHeight));

            TextMeshProUGUI text = MenuUiBuilder.CreateTextExact(name, topBar, content,
                MenuUiBuilder.FontScale.Hud, TextAlignmentOptions.Center, textColor, font);
            text.enableWordWrapping = false;
            MenuUiBuilder.SetAnchored(text.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(TopBarSlotWidth - 8f, TopBarSlotHeight), new Vector2(centerX, 0f));
            ApplyTmpOutline(text, font);
        }

        /// <summary>
        /// 顶部信息条文字用的 TMP 描边材质（持久资产，随场景序列化）。
        /// 【为什么不用 UGUI Outline】<c>TextMeshProUGUI</c> 覆写了 <c>Rebuild</c> 且不走
        /// <c>Graphic.DoMeshGeneration</c>，TMP 3.0.7 里也没有任何 <c>IMeshModifier</c> 钩子——
        /// UGUI 的 <c>Outline</c>/<c>Shadow</c> 加在 TMP 文本上**完全不生效**（静默无描边）。
        /// 只能走 TMP 材质描边；材质又必须是持久资产（内存材质进不了场景，重开场景就丢），
        /// 与 <c>MenuUiBuilder</c> 的标题描边材质同款做法。
        /// </summary>
        const string HudOutlineMaterialPath = "Assets/Art/Materials/UI/TmpHudOutline.mat";

        /// <summary>给 TMP 文本挂 2px <c>#2A2A2A</c> 描边材质（材质缺失时静默降级，靠内容片对比兜底 ≥7.7:1）。</summary>
        static void ApplyTmpOutline(TextMeshProUGUI text, TMP_FontAsset font)
        {
            if (text == null || font == null)
                return;

            Material material = GetOrCreateOutlineMaterial(font);
            if (material != null)
                text.fontSharedMaterial = material;
        }

        static Material GetOrCreateOutlineMaterial(TMP_FontAsset font)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(HudOutlineMaterialPath);
            if (existing != null)
            {
                // TMP 材质携带 _MainTex = 所属字体的 SDF 图集，必须同源；
                // 套错字体的图集会让字形错乱，故图集不匹配时删掉重建。
                if (existing.GetTexture("_MainTex") == font.material.GetTexture("_MainTex"))
                    return existing;

                AssetDatabase.DeleteAsset(HudOutlineMaterialPath);
            }

            try
            {
                MenuUiBuilder.EnsureFolder("Assets/Art/Materials");
                MenuUiBuilder.EnsureFolder("Assets/Art/Materials/UI");

                var material = new Material(font.material) { name = "TmpHudOutline" };
                material.SetColor("_OutlineColor", UiTheme.Ink);   // #2A2A2A
                // TMP 的描边宽度是归一化量（不是像素精确值）：0.20 在本字体材质的
                // _GradientScale 下约 1.5-2px；实机以截图为准。
                material.SetFloat("_OutlineWidth", 0.20f);
                material.EnableKeyword("OUTLINE_ON");
                AssetDatabase.CreateAsset(material, HudOutlineMaterialPath);
                return material;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BattleHudBuilder] 生成 HUD 描边材质失败（" + HudOutlineMaterialPath
                    + "）：" + e.Message + "\n  顶部信息条文字将只靠内容片对比（实测 ≥7.7:1）。");
                return null;
            }
        }

        /// <summary>
        /// 模式开关的一段（玻璃片 + 居中文字）。
        ///
        /// 【状态区分 = 色相 + 描边，不是乘色压暗】当前段 = 金玻璃（唯一强调色，原色不调）+ 深墨字
        /// （最坏底 4.65:1）+ 1px 亮金外描边；非当前段 = 中性深玻璃
        /// （<see cref="GlassPanelSpriteBuilder.Tone.Button"/>）+ 浅米字（4.64:1）。
        /// **为什么不能"同底色压暗一档"**：深墨字压"压暗后的金"在纯黑最坏底上，任何 &lt;1 的乘色
        /// 都跌破 4.5:1（×0.84 → 3.43、×0.95 也才 4.24）—— 推导见 <see cref="BattleUiTheme.Tok"/>。
        /// 参照 game-2：选中页签用琥珀底、未选中用中性底，同一套"色相区分"语言。
        /// </summary>
        static void BuildToggleSegment(Transform parent, string name, string label, float x,
            bool selected, TMP_FontAsset font, string hotkey)
        {
            RectTransform segment = MenuUiBuilder.CreateGlassPanel(name, parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f),
                new Vector2(TopBarSegmentWidth, TopBarSegmentHeight),
                selected ? GlassPanelSpriteBuilder.Tone.Primary : GlassPanelSpriteBuilder.Tone.Button,
                GlassPanelSpriteBuilder.Geo.Chip);

            // r12：挂 Button（onClick 由运行时 BattleHud 绑定）；transitong 用默认（无 targetGraphic 着色）。
            segment.gameObject.AddComponent<Button>();

            // 外描边是「当前」的第二重信号（只画在填充之外，不影响文字对比度）。
            if (selected)
                MenuUiBuilder.AddOutline(segment.gameObject, BattleUiTheme.Tok.ModeActiveStroke, 1f);

            TextMeshProUGUI text = MenuUiBuilder.CreateTextExact("Label", segment, label,
                MenuUiBuilder.FontScale.Hud, TextAlignmentOptions.Center,
                selected ? BattleUiTheme.Tok.InkOnGold : BattleUiTheme.Tok.TextOnGlass, font);
            MenuUiBuilder.Stretch(text.rectTransform);

            // 快捷键角标（右上角小字，r12 用户要求"把快捷键标在附近"）。
            TextMeshProUGUI key = MenuUiBuilder.CreateTextExact("Hotkey", segment, hotkey,
                MenuUiBuilder.FontScale.Hint, TextAlignmentOptions.Center,
                selected ? BattleUiTheme.Tok.InkOnGold : BattleUiTheme.Tok.TextOnGlass, font);
            key.rectTransform.anchorMin = new Vector2(1f, 1f);
            key.rectTransform.anchorMax = new Vector2(1f, 1f);
            key.rectTransform.pivot = new Vector2(1f, 1f);
            key.rectTransform.anchoredPosition = new Vector2(-3f, -1f);
            key.rectTransform.sizeDelta = new Vector2(16f, 14f);
        }

        /// <summary>
        /// 小地图（左上）：Canvas 直接子节点名为 <c>MinimapPanel</c>，
        /// 供 <c>HudMinimapSceneSetup</c> 复用并补 <c>DotLayer</c>/<c>TileLayer</c> 与 BattleMinimap 接线。
        ///
        /// 【样式归属】面板的位置/尺寸/底色/描边由本方法每次重建时强制写回（HudMinimapSceneSetup 只接线、不覆写），
        /// 否则场景里旧面板的木棕底会一直残留——海图面板必须是水蓝系玻璃（<see cref="GlassPanelSpriteBuilder.Tone.Sea"/>）。
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
            dotLayer.offsetMax = new Vector2(-PanelPadding, -(PanelPadding + MinimapCaptionHeight + 6f));

            EnsureChildRect(dotLayer, "TileLayer");

            // 装饰性标题：移入面板安全区（内边距 14），不再压左下边框。
            TextMeshProUGUI caption = null;
            Transform captionNode = panel.Find("MinimapCaption");
            if (captionNode != null)
                caption = captionNode.GetComponent<TextMeshProUGUI>();
            if (caption == null)
            {
                caption = MenuUiBuilder.CreateTextExact("MinimapCaption", panel,
                    UiStrings.BattleMinimapTitle, MenuUiBuilder.FontScale.Hint, TextAlignmentOptions.MidlineLeft,
                    BattleUiTheme.Tok.TitleOnGlass, secondary);
            }

            MenuUiBuilder.SetAnchored(caption.rectTransform, new Vector2(0f, 1f),
                new Vector2(140f, MinimapCaptionHeight), new Vector2(PanelPadding, -PanelPadding));
        }

        /// <summary>
        /// 小地图面板外观：海图玻璃（深蓝绿亚克力，alpha 0.84）+ 烘进贴图的双色描边。
        /// 【为什么比框架实】岛格（沙 #F0D48A / 草 #6FA86F）压在半透明底上，透度越高越难读出轮廓；
        /// 0.84 时金标题 5.47:1、浅字 7.04:1（叠纯白最坏底）。
        /// </summary>
        static void ApplyMinimapSkin(RectTransform panel)
        {
            var image = panel.GetComponent<Image>();
            if (image == null)
                image = panel.gameObject.AddComponent<Image>();

            MenuUiBuilder.ApplyGlassSkin(image, GlassPanelSpriteBuilder.Tone.Sea);

            // 旧实现给的 3px 黄铜 Outline 会外扩吃掉安全边距；玻璃描边已烘进贴图（外扩 0），
            // 若场景里残留旧 Outline 组件则就地清掉。
            var legacyOutline = panel.GetComponent<Outline>();
            if (legacyOutline != null)
                Object.DestroyImmediate(legacyOutline);
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
            // 【r12 用户裁决】FPS 式细小十字（两根 2px 细条，白字深描边），替换 64px 大环；
            // 白色 + 深描边保证在角色/水面/天空任何底色上都可读，跟随锚定时常驻可见。
            RectTransform crosshair = MenuUiBuilder.CreateRect("Crosshair", hudRoot);
            MenuUiBuilder.SetAnchored(crosshair, new Vector2(0.5f, 0.5f), new Vector2(18f, 18f), Vector2.zero);

            Color fill = Color.white;
            void Bar(string name, float w, float h)
            {
                var rect = MenuUiBuilder.CreateRect(name, crosshair);
                rect.sizeDelta = new Vector2(w, h);
                rect.anchoredPosition = Vector2.zero;
                var img = rect.gameObject.AddComponent<Image>();
                img.color = fill;
                img.raycastTarget = false;
                var outline = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
            Bar("CrossH", 18f, 2f);
            Bar("CrossV", 2f, 18f);
        }

        // ------------------------------------------------------------------
        // 左下：船员名册（单层 0.88 玻璃 + 发丝分行，行距 = 字号 × 1.55）
        // ------------------------------------------------------------------

        static void BuildRoster(Transform hudRoot, TMP_FontAsset title, TMP_FontAsset body,
            TMP_FontAsset secondary, Result result)
        {
            // 【为什么用 Dense（0.88）而不是 Frame（0.70）+ 行片】名册整块内区都是文字：
            // ① 队伍色名由**运行期** BattleHud 写成 #FF8A7A/#7FB0FF（不在本波次文件域内），
            //    它叠纯白最坏底要 ≥4.5:1 需背衬 alpha ≥0.841；
            // ② 金标题在内容片上 7.76:1（框架层 4.52:1 只是兜底）。
            // 两条都只能靠"整块 0.88 玻璃"满足；若外面套 0.70 框架、里面再叠 0.88 行片，
            // 合成透度 = 1−(1−0.88)(1−0.70) = 0.964 —— 反而比参照物 game-2 的 WINDOW_BG(0.88) 更实、
            // 且多出一层"盒子套盒子"的杂乱。故名册 = 单层 0.88 玻璃 + 发丝分隔线（游戏-2 的 border 语言）。
            RectTransform panel = MenuUiBuilder.CreateGlassPanel("RosterPanel", hudRoot,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Safe, RosterBottom),
                new Vector2(RosterWidth, RosterHeight), GlassPanelSpriteBuilder.Tone.Dense);

            // 【面板高度随内容收缩】BattleHud.BuildRoster 会把超出实际出战人数的行 SetActive(false)，
            // 而 VerticalLayoutGroup 会跳过未激活子物体 —— 于是面板高度自动收到「标题 + 实际行数」。
            // 12 行容量与行节点数量都不变（BattleHud.MaxRosterRows = 12、装配测试断言 12 行）。
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)PanelPadding, (int)PanelPadding,
                (int)PanelPadding, (int)PanelPadding);
            layout.spacing = RosterRowPitch - RosterRowHeight;   // 行间 2px（行高 28 / 行距 30）
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;   // 宽度恒 304
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;      // 高度随行数

            // 标题：金压 0.88 玻璃 ≥7.76:1（最坏底 = 纯白场景）；进垂直流当第一行。
            TextMeshProUGUI rosterTitle = MenuUiBuilder.CreateTextExact("RosterTitle", panel,
                UiTextRules.RosterTitle(1), MenuUiBuilder.FontScale.Section, TextAlignmentOptions.MidlineLeft,
                BattleUiTheme.Tok.TitleOnGlass, title);
            MenuUiBuilder.SetAnchored(rosterTitle.rectTransform, new Vector2(0f, 1f),
                new Vector2(RosterWidth - PanelPadding * 2f, 26f), new Vector2(PanelPadding, -PanelPadding));
            var titleElement = rosterTitle.gameObject.AddComponent<LayoutElement>();
            titleElement.preferredHeight = 26f;
            titleElement.minHeight = 26f;
            result.rosterTitle = rosterTitle;

            var rows = new BattleHud.RosterRowView[RosterRows];
            for (int i = 0; i < RosterRows; i++)
                rows[i] = CreateRosterRow(panel, i, body, secondary);

            result.rosterRows = rows;
        }

        static BattleHud.RosterRowView CreateRosterRow(Transform parent, int index, TMP_FontAsset body,
            TMP_FontAsset secondary)
        {
            const float rowWidth = RosterWidth - PanelPadding * 2f;   // 276

            // 行本身**不铺底**（底色就是名册的 0.88 玻璃），只留一条 1px 发丝分隔线 ——
            // 参照物 game-2 的列表行也是"无底色 + 悬停才亮"，静态更干净、玻璃感不被盒子切碎。
            RectTransform row = MenuUiBuilder.CreateRect("RosterRow_" + index, parent);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(rowWidth, RosterRowHeight);
            row.anchoredPosition = new Vector2(PanelPadding, -RosterTitleStrip - index * RosterRowPitch);

            // 编辑期坐标仅供场景快照；运行期由面板的 VerticalLayoutGroup 接管位置与行高，
            // 未激活的行（BattleHud 隐藏的多余行）不再占位 → 面板底部不留空槽。
            var rowElement = row.gameObject.AddComponent<LayoutElement>();
            rowElement.preferredHeight = RosterRowHeight;
            rowElement.minHeight = RosterRowHeight;

            // 发丝分隔线（白 8%）：只做"行"的读感，不抢文字。
            RectTransform hairline = MenuUiBuilder.CreateRect("Hairline", row);
            hairline.anchorMin = new Vector2(0f, 0f);
            hairline.anchorMax = new Vector2(1f, 0f);
            hairline.pivot = new Vector2(0.5f, 0f);
            hairline.offsetMin = new Vector2(0f, 0f);
            hairline.offsetMax = new Vector2(0f, 1f);
            var hairlineImage = hairline.gameObject.AddComponent<Image>();
            hairlineImage.color = new Color(1f, 1f, 1f, 0.08f);
            hairlineImage.raycastTarget = false;

            // 姓名（队伍 + 职业中文）。字号 FontScale.Hud = 18（旧 24，用户裁决下调一档）。
            // 队伍身份由行右侧的「队伍色血条」+ 文字里的「红队／蓝队」共同承载。
            TextMeshProUGUI name = MenuUiBuilder.CreateTextExact("NameText", row, string.Empty,
                MenuUiBuilder.FontScale.Hud, TextAlignmentOptions.MidlineLeft, UiTheme.TextLight, body);
            name.enableWordWrapping = false;
            MenuUiBuilder.SetAnchored(name.rectTransform, new Vector2(0f, 1f), new Vector2(130f, 26f),
                new Vector2(6f, -1f));

            // 血条槽（凹槽感：比行片更暗的一层）+ 填充。
            // 【P2-4 队伍编码】填充块与 <c>teamSwatch</c> 指向**同一个 Image**：
            //   · BattleHud.BuildRoster 每帧把 teamSwatch.color 写成 UiTheme.TeamColor（红/蓝）；
            //   · BattleHud.UpdateRowBar 只改 healthFill.rectTransform.anchorMax（血量长度）。
            // 【为什么填充用无 Sprite 的 Image】UiSprites.BarFill 的烘焙基准色是**绿色**
            // （0.42,0.78,0.34），Image.color 是乘色：若沿用该 Sprite，运行期写入队伍色会得到
            // 「绿 × 队伍色」的浑浊结果。改用无 Sprite 的 Image（渲染纯白 quad × color），
            // 队伍色才能 1:1 还原。血条槽底仍用 BarBackground 九宫格。
            RectTransform barBg = MenuUiBuilder.CreateRect("BarBg", row);
            MenuUiBuilder.SetAnchored(barBg, new Vector2(0f, 1f), new Vector2(74f, 12f), new Vector2(140f, -8f));
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
            fillImage.sprite = null;                       // 纯色块（见上「为什么无 Sprite」）
            fillImage.type = Image.Type.Simple;
            fillImage.color = UiTheme.TeamRed;             // 默认红队色，运行期按队改写
            fillImage.raycastTarget = false;

            // 生命数字（FontScale.Tiny = 13，角标档 ≥12）。
            TextMeshProUGUI hp = MenuUiBuilder.CreateTextExact("HpText", row, string.Empty,
                MenuUiBuilder.FontScale.Tiny, TextAlignmentOptions.MidlineRight, UiTheme.TextLight, secondary);
            hp.enableWordWrapping = false;
            MenuUiBuilder.SetAnchored(hp.rectTransform, new Vector2(0f, 1f), new Vector2(56f, 26f),
                new Vector2(216f, -1f));

            return new BattleHud.RosterRowView
            {
                root = row.gameObject,
                // 队伍色块与血条填充是同一 Image：运行期 teamSwatch.color 的队伍色直接作用于血条。
                teamSwatch = fillImage,
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
            // 底部堆叠（自下而上）：提示条 16..48 → 武器面板 60..336；面板底缘与提示条顶边净间距 12px。
            // 【为什么 Dense（0.88）而非 Frame】面板内区全是文字与行片（金标题/行文本/17 行），
            // 单层 0.88 玻璃即可满足全部对比度（金 7.76:1 / 浅米 9.56:1），且避免"框架 + 内容片"
            // 两层盒子把玻璃感切碎（同名册的做法）。
            RectTransform panel = MenuUiBuilder.CreateGlassPanel("WeaponPanel", hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, WeaponPanelBottom),
                new Vector2(WeaponPanelWidth, WeaponPanelHeight), GlassPanelSpriteBuilder.Tone.Dense);
            result.weaponPanelRoot = panel.gameObject;

            // 标题：金压 0.88 玻璃 ≥7.76:1（最坏底 = 纯白场景）。宽度给到 400（文案「水手 · 选择武器」级别留足余量）。
            TextMeshProUGUI panelTitle = MenuUiBuilder.CreateTextExact("WeaponPanelTitle", panel, string.Empty,
                MenuUiBuilder.FontScale.Section, TextAlignmentOptions.Center, BattleUiTheme.Tok.TitleOnGlass, title);
            MenuUiBuilder.SetAnchored(panelTitle.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(400f, 26f), new Vector2(0f, -PanelPadding));
            result.weaponPanelTitle = panelTitle;

            float buttonY = -(PanelPadding + 26f + 8f + 15f);   // 标题片下沿 + 8px 净距 + 半按钮高

            Button throwButton = MenuUiBuilder.CreateButton("ThrowSelfButton", panel, UiStrings.BattleThrowSelf,
                new Vector2(0.5f, 1f), new Vector2(-80f, buttonY), new Vector2(148f, 30f), body,
                UiSprites.Kind.ButtonWood, BattleUiTheme.Tok.TextOnGlass, MenuUiBuilder.FontScale.Body);
            result.throwSelfButton = throwButton;

            Button endGoButton = MenuUiBuilder.CreateButton("EndGoButton", panel, UiStrings.BattleEndGo,
                new Vector2(0.5f, 1f), new Vector2(80f, buttonY), new Vector2(148f, 30f), body,
                UiSprites.Kind.ButtonWood, BattleUiTheme.Tok.TextOnGlass, MenuUiBuilder.FontScale.Body);
            result.endGoButton = endGoButton;

            // 列表小标题：按钮下沿（buttonY − 半高 15）再留 8px，浅米字直接压玻璃（≥9.5:1）。
            // 【锚点口径】SetAnchored 的 pivot = anchor = (0,1) → 这里传的是**左上角**，不是中心。
            float listTitleY = buttonY - 15f - 8f;
            TextMeshProUGUI listTitle = MenuUiBuilder.CreateTextExact("WeaponListTitle", panel,
                UiStrings.BattleWeaponListTitle, MenuUiBuilder.FontScale.Hint, TextAlignmentOptions.MidlineLeft,
                BattleUiTheme.Tok.TextOnGlass, secondary);
            MenuUiBuilder.SetAnchored(listTitle.rectTransform, new Vector2(0f, 1f), new Vector2(150f, 26f),
                new Vector2(PanelPadding, listTitleY));

            // 滚动区：列表小标题下沿再留 4px，高度顶到面板下内边距为止（pivot/anchor 都是 (0.5,1)）。
            float scrollTop = listTitleY - 26f - 4f;
            float scrollHeight = WeaponPanelHeight - Mathf.Abs(scrollTop) - PanelPadding;
            BuildWeaponScroll(panel, scrollTop, scrollHeight, body, result);
        }

        static void BuildWeaponScroll(RectTransform panel, float topOffsetY, float height,
            TMP_FontAsset body, Result result)
        {
            RectTransform scrollGo = MenuUiBuilder.CreateRect("WeaponScroll", panel);
            MenuUiBuilder.SetAnchored(scrollGo, new Vector2(0.5f, 1f),
                new Vector2(WeaponPanelWidth - PanelPadding * 2f, height), new Vector2(0f, topOffsetY));

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
            layout.padding = new RectOffset(0, 0, 0, 0);
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
                // 行 = 深色玻璃小件（比面板实，承载浅米字 ≥9.5:1）；列表行可点，四态由玻璃乘色给出。
                Button button = MenuUiBuilder.CreateButton("WeaponButton_" + id, content,
                    UiTextRules.WeaponName(id), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(WeaponPanelWidth - PanelPadding * 2f, WeaponRowHeight), body,
                    UiSprites.Kind.ButtonWood, BattleUiTheme.Tok.TextOnGlass, MenuUiBuilder.FontScale.Body);

                var element = button.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = WeaponRowHeight;
                element.minHeight = WeaponRowHeight;

                // 左对齐武器名（行文本档 Body=15）：换掉 CreateButton 的居中标签，只留一个 TMP。
                TextMeshProUGUI label = MenuUiBuilder.CreateTextExact("Label", button.transform,
                    UiTextRules.WeaponName(id), MenuUiBuilder.FontScale.Body, TextAlignmentOptions.MidlineLeft,
                    BattleUiTheme.Tok.TextOnGlass, body);
                MenuUiBuilder.Stretch(label.rectTransform, 0f);
                label.margin = new Vector4(12f, 0f, 8f, 0f);

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
            // 操作提示条：屏幕底缘之上 16px（通栏条是 §1.7 允许的近贴边例外），720px 居中。
            // 【底为什么用内容片而非框架】提示条整条都是文字，框不下"框架 + 内容片"两级；
            // 直接用 0.88 的内容片（浅米字 ≥9.5:1），厚度带/高光/噪点照旧 → 仍是玻璃观感。
            // 【r12 用户反馈】提示条从底缘居中挪到右下角（"放在界面旁边"，不占画面中心视线）。
            RectTransform hintBar = MenuUiBuilder.CreateGlassPanel("HintBar", hudRoot,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, HintBarBottom),
                new Vector2(HintBarWidth, HintBarHeight), GlassPanelSpriteBuilder.Tone.Dense,
                GlassPanelSpriteBuilder.Geo.Panel);

            TextMeshProUGUI hint = MenuUiBuilder.CreateTextExact("HintText", hintBar,
                UiStrings.BattleHintMove, MenuUiBuilder.FontScale.Hint, TextAlignmentOptions.Center,
                BattleUiTheme.Tok.TextOnGlass, secondary);
            MenuUiBuilder.Stretch(hint.rectTransform, 8f);

            // 返回主菜单：摆在左下角（与名册面板同侧，但名册底距 336 已让出底部空间）。
            // CreateButton 的 pivot 是中心，故锚点位置取「安全边距 + 半宽/半高」= (96,34)，
            // 按钮外框左边即 16px、底边 16px。
            Button back = MenuUiBuilder.CreateButton("BackButton", hudRoot, UiStrings.BackToMainMenu,
                new Vector2(0f, 0f), new Vector2(96f, 34f), new Vector2(160f, 36f), body,
                UiSprites.Kind.ButtonWood, BattleUiTheme.Tok.TextOnGlass, MenuUiBuilder.FontScale.Body);
            result.backButton = back;

            // 瞄准 / 聚焦标签（默认隐藏，等玩法接线后由逻辑控制显隐）。
            TextMeshProUGUI aim = MenuUiBuilder.CreateTextExact("AimLabel", hudRoot,
                UiStrings.BattleAiming, MenuUiBuilder.FontScale.Hud, TextAlignmentOptions.Center,
                BattleUiTheme.Tok.Select, body);
            MenuUiBuilder.SetAnchored(aim.rectTransform, new Vector2(0.5f, 0f), new Vector2(240f, 28f),
                new Vector2(0f, WeaponPanelBottom + WeaponPanelHeight + 8f));
            aim.gameObject.SetActive(false);

            TextMeshProUGUI focus = MenuUiBuilder.CreateTextExact("FocusLabel", hudRoot,
                UiStrings.BattleFocusing, MenuUiBuilder.FontScale.Body, TextAlignmentOptions.Center,
                BattleUiTheme.Tok.Select, secondary);
            MenuUiBuilder.SetAnchored(focus.rectTransform, new Vector2(0.5f, 1f), new Vector2(200f, 24f),
                new Vector2(0f, -Safe - TopBarHeight - 6f));
            focus.gameObject.SetActive(false);
        }
    }
}
