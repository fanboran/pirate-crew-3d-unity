using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PirateCrew.UI;

// StickKit.Button 方法与 UGUI Button 类型同名（gd button → PascalCase 的必然结果；
// C# 的 Color-Color 规则不覆盖方法，方法体内简单名 Button 会解析成方法组），类型引用
// 一律走本别名。
using UnityButton = UnityEngine.UI.Button;
using SketchButtonControl = PirateCrew.UI.Stick.SketchButton;  // 方法名 SketchButton 与控件类型同名，走别名

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 组件装配器 —— 模板共用的小型控件构造入口。移植自 stick-world
    /// <c>modules/ui_global/scripts/theme/stick_kit.gd</c>，方法面 1:1（gd 有即 cs 有，
    /// 命名转 PascalCase，参数语义一致）。
    ///
    /// 设计：界面骨架放场景/容器（锚定布局），内容用本装配器按数据装配（加一项 = 加
    /// 一行数据）。所有控件从 <see cref="StickTokens"/> / Sketch 皮肤出口取样式，
    /// 不手写字面量颜色/字号。
    ///
    /// 用法：
    ///   var section = StickKit.Section(parent, "按钮族");
    ///   StickKit.Button(section.transform, "普通按钮", OnPressed);
    ///   StickKit.Label(section.transform, "说明文字", StickKit.StickLabelKind.Hint);
    /// </summary>
    public static class StickKit
    {
        // ---------------- 枚举（与 gd 同名同序） ----------------

        /// <summary>标签档位（gd StickKit.LabelKind）。</summary>
        public enum StickLabelKind
        {
            Title,
            Section,
            Body,
            Hint,
            Tiny,
        }

        /// <summary>按钮档位（gd StickKit.ButtonKind；与 SketchButton 变体表同名同序，
        /// 直转零映射。Normal → 变体表 Dark 暗底默认档，对齐 gd「NORMAL→DARK」）。</summary>
        public enum StickButtonKind
        {
            Normal,
            Accent,
            Primary,
            Danger,
            Paper,
            IconSquare,
        }

        /// <summary>屏幕四角（gd StickKit.Corner）。</summary>
        public enum StickCorner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
        }

        /// <summary>gd PanelContainer tone（SketchPanel.Tone.DARK=0 / LIGHT=1）。</summary>
        public const int ToneDark = 0;
        public const int ToneLight = 1;

        // ---------------- 常量（gd 单一真相源搬移） ----------------

        /// <summary>HUD 预留区高度：顶栏 GlobalHUD 60 + 材料横条（gd HUD_TOP_RESERVED=104）。</summary>
        public const float HudTopReserved = 104f;

        /// <summary>底部模式面板 ModePanel 高（gd HUD_BOTTOM_RESERVED=88）。</summary>
        public const float HudBottomReserved = 88f;

        static TMP_FontAsset _cachedFont;

        static TMP_FontAsset HandFont()
        {
            if (_cachedFont == null)
                _cachedFont = UiKit.RuntimeFont(UiKit.RuntimeFontKind.Body);
            return _cachedFont;
        }

        // ---------------- 标签 ----------------

        /// <summary>建标签（gd StickKit.label：kind 定字号与默认色，显式 color 覆盖默认色）。</summary>
        public static TextMeshProUGUI Label(Transform parent, string text,
            StickLabelKind kind = StickLabelKind.Body, Color? color = null)
        {
            float fontSize;
            Color tint;
            switch (kind)
            {
                case StickLabelKind.Title:
                    fontSize = StickTokens.FONT_TITLE;
                    tint = StickTokens.TEXT;
                    break;
                case StickLabelKind.Section:
                    fontSize = StickTokens.FONT_SECTION;
                    tint = StickTokens.ACCENT;
                    break;
                case StickLabelKind.Hint:
                    fontSize = StickTokens.FONT_HINT;
                    tint = StickTokens.TEXT_DIM;
                    break;
                case StickLabelKind.Tiny:
                    fontSize = StickTokens.FONT_TINY;
                    tint = StickTokens.TEXT_DIM;
                    break;
                default:
                    fontSize = StickTokens.FONT_BODY;
                    tint = StickTokens.TEXT;
                    break;
            }
            // gd：显式色覆盖 kind 默认色（除本身带默认暗/强调色的档位）
            if (color.HasValue)
                tint = color.Value;
            return UiKit.CreateText("Label", parent, text, (int)fontSize,
                TextAlignmentOptions.MidlineLeft, tint, HandFont());
        }

        // ---------------- 按钮 ----------------

        /// <summary>
        /// 原生 Button 兜底场景（模板陈列/表单，gd 走引擎主题 stylebox；Unity 无主题层，
        /// 近似为 BTN_BG 平涂 + 四态明度）。游戏内手绘按钮走 <see cref="SketchButton"/>。
        /// </summary>
        public static UnityButton Button(Transform parent, string text, Action callback = null,
            StickButtonKind kind = StickButtonKind.Normal, float height = StickTokens.BTN_H)
        {
            RectTransform rect = NewButtonRect(parent, text, height, StickTokens.TEXT);
            var button = rect.gameObject.AddComponent<UnityButton>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.colors = FlatStates();
            rect.gameObject.AddComponent<UiPressSink>();
            SetupButton(button, callback, kind);
            return button;
        }

        /// <summary>
        /// 手绘按钮（游戏内默认）：委托 <c>SketchButton</c> 控件（P1 收口——
        /// 变体表驱动/四态沸腾/五态字色/伪粗/IconSquare 自绘底全在控件本体内，
        /// 本方法只负责 gd stick_kit.sketch_button 的调用语义：text+min 高+kind+回调）。
        /// gd 宽度内容自适应、Unity 定宽 120（调用方可再调 sizeDelta）。
        /// </summary>
        public static SketchButtonControl SketchButton(Transform parent, string text, Action callback = null,
            StickButtonKind kind = StickButtonKind.Normal, float height = StickTokens.BTN_H)
        {
            SketchButtonControl b = SketchButtonControl.Create(parent, "SketchButton",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(120f, height),
                // stick-world 全局 StickHand 单字体（sketch_fonts 口径），走手写体主档
                UiKit.RuntimeFont(UiKit.RuntimeFontKind.Title),
                KindToVariant(kind), label: text);
            if (callback != null)
                b.onClick.AddListener(() => callback());
            return b;
        }

        /// <summary>按钮：直出手绘四态（auto_button 为 gd 旧调用名保留的别名）。</summary>
        public static UnityButton AutoButton(Transform parent, string text, Action callback = null,
            StickButtonKind kind = StickButtonKind.Normal, float height = StickTokens.BTN_H)
        {
            return SketchButton(parent, text, callback, kind, height);
        }

        /// <summary>kind → 变体表键（gd「枚举同名同序直转」；Normal → Dark 暗底默认档）。</summary>
        static StickTokens.SketchButtonKind KindToVariant(StickButtonKind kind)
        {
            switch (kind)
            {
                case StickButtonKind.Accent: return StickTokens.SketchButtonKind.Accent;
                case StickButtonKind.Primary: return StickTokens.SketchButtonKind.Primary;
                case StickButtonKind.Danger: return StickTokens.SketchButtonKind.Danger;
                case StickButtonKind.Paper: return StickTokens.SketchButtonKind.Paper;
                case StickButtonKind.IconSquare: return StickTokens.SketchButtonKind.IconSquare;
                default: return StickTokens.SketchButtonKind.Dark;
            }
        }

        static RectTransform NewButtonRect(Transform parent, string text, float height,
            Color textColor)
        {
            RectTransform rect = UiKit.CreateRect("Button", parent);
            var image = rect.gameObject.AddComponent<Image>();
            // 主题兜底场景：平涂 BTN_BG（无贴图件——像素件纪律只管"有烘焙色阶的件"，
            // 平涂底不在此列；真正带皮的按钮一律走 SketchButton）。
            image.color = StickTokens.BTN_BG;
            image.raycastTarget = true;

            TextMeshProUGUI label = UiKit.CreateText("Text", rect, text, (int)StickTokens.FONT_BODY,
                TextAlignmentOptions.Center, textColor, HandFont());
            label.enableWordWrapping = false;
            UiKit.Stretch(label.rectTransform, 10f);

            StickLayoutElement min = rect.gameObject.AddComponent<StickLayoutElement>();
            min.MinSize = new Vector2(0f, height);   // = gd custom_minimum_size (0, height)
            min.CrossAlign = StickCrossAlign.Center;
            return rect;
        }

        static ColorBlock FlatStates()
        {
            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1f, 1f, 1f, 1f),
                pressedColor = new Color(0.9f, 0.9f, 0.92f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = UiSkin.ButtonFadeSeconds,
            };
        }

        /// <summary>母题角标：管线图标叠挂按钮左缘，不占排版位。返回角标 Image 便于
        /// 调 color（如禁用态调暗，对齐 gd 返回 TextureRect 调 modulate）。</summary>
        public static Image MotifBadge(UnityButton btn, string motif, float size = 20f, float pad = 10f)
        {
            Sprite tex = StickTokens.LoadIcon(motif);
            if (tex == null)
                return null;
            RectTransform rect = UiKit.CreateRect("MotifBadge", btn.transform);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = tex;
            image.raycastTarget = false;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(pad, 0f);
            return image;
        }

        /// <summary>按钮公共装配：hover 微缩放（gd create_tween 的等价协程）。
        /// 统一 UI 点击音在 Unity 侧暂无 AudioManager 出口，接音频服务时补（gd 框架先行、
        /// 资产未就位时静默跳过，语义同为"可缺席"）。</summary>
        static void SetupButton(UnityButton b, Action callback, StickButtonKind kind)
        {
            if (callback != null)
                b.onClick.AddListener(() => callback());
            b.gameObject.AddComponent<StickHoverScale>();
        }

        // ---------------- 区块 ----------------

        /// <summary>带小标题的 VBox 区块（面板内分节；separation 6 对齐 gd）。</summary>
        public static StickLayoutGroup Section(Transform parent, string title)
        {
            RectTransform rect = UiKit.CreateRect("Section", parent);
            StickLayoutGroup box = rect.gameObject.AddComponent<StickLayoutGroup>();
            box.Orientation = StickOrientation.Vertical;
            box.Separation = 6f;
            Label(rect, title, StickLabelKind.Section);
            return box;
        }

        /// <summary>横向行容器（gd StickKit.row）。</summary>
        public static StickLayoutGroup Row(Transform parent, float separation = 8f)
        {
            RectTransform rect = UiKit.CreateRect("Row", parent);
            StickLayoutGroup row = rect.gameObject.AddComponent<StickLayoutGroup>();
            row.Orientation = StickOrientation.Horizontal;
            row.Separation = separation;
            return row;
        }

        /// <summary>竖直分隔线（横向 HUD 行内分节用）：像素皮蚀刻线贴图（1u 厚 = 3px）。</summary>
        public static void VSeparator(Transform parent)
        {
            RectTransform rect = UiKit.CreateRect("VSeparator", parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Separator(false);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：线色烘在贴图里
            image.raycastTarget = false;
            StickLayoutElement min = rect.gameObject.AddComponent<StickLayoutElement>();
            min.MinSize = new Vector2(PixelSkin.Unit, 0f);   // 线厚取 1u（3px）整数倍
        }

        /// <summary>水平分隔线：像素皮蚀刻线贴图（旧版为手绘波浪线自绘；
        /// 换装后线宽/羽化由烘焙器负责，装配侧只报方向）。</summary>
        public static void Separator(Transform parent)
        {
            RectTransform rect = UiKit.CreateRect("Separator", parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Separator(true);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            StickLayoutElement min = rect.gameObject.AddComponent<StickLayoutElement>();
            min.MinSize = new Vector2(0f, PixelSkin.Unit);   // 线厚取 1u（3px）——旧 8px 是给波浪羽化留的余量
        }

        // ---------------- 皮肤路由 ----------------

        /// <summary>面板：像素 Plate 底 + 底垫投影（DARK = Frame tone / LIGHT = Light tone；
        /// gd SketchPanel 等价）。返回面板根，内容区请用 <see cref="PanelContent"/> 建
        /// （底板/投影与布局必须兄弟隔离，见 StickScreen 注释）。</summary>
        public static RectTransform Panel(Transform parent, int tone = ToneDark)
        {
            RectTransform rect = UiKit.CreateRect("Panel", parent);
            PixelTone pixelTone = tone == ToneLight ? PixelTone.Light : PixelTone.Frame;

            // 投影先建（子件绘制按加入序，投影必须垫在本体之下），偏移右下 1u。
            RectTransform shadow = UiKit.CreateRect("Shadow", rect);
            StickUIKit.FullRect(shadow);
            shadow.anchoredPosition = PixelSkin.ShadowOffset;
            var shadowImage = shadow.gameObject.AddComponent<Image>();
            shadowImage.sprite = PixelSkin.ShadowSprite;
            shadowImage.type = Image.Type.Sliced;
            shadowImage.color = Color.white;
            shadowImage.raycastTarget = false;

            RectTransform backplate = UiKit.CreateRect("Backplate", rect);
            StickUIKit.FullRect(backplate);
            var image = backplate.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(pixelTone);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：tone 色阶烘在贴图里
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>面板内容区：FullRect + SketchPanel 内边距令牌（16/12）的垂直容器。
        /// gd 的 PanelContainer 自带 content margin，此出口承担同一职责。</summary>
        public static StickLayoutGroup PanelContent(RectTransform panel)
        {
            RectTransform content = UiKit.CreateRect("Content", panel);
            StickUIKit.FullRect(content);
            StickLayoutGroup box = content.gameObject.AddComponent<StickLayoutGroup>();
            box.Orientation = StickOrientation.Vertical;
            box.Separation = 8f;
            box.PaddingLeft = StickTokens.SketchPanelPadX;
            box.PaddingRight = StickTokens.SketchPanelPadX;
            box.PaddingTop = StickTokens.SketchPanelPadY;
            box.PaddingBottom = StickTokens.SketchPanelPadY;
            return box;
        }

        // ---------------- 键值行 ----------------

        /// <summary>左标签右控件的设置行（返回行容器，控件经 StickKit 装配进 row）。</summary>
        public static StickLayoutGroup FieldRow(Transform parent, string nameText, string hintText = "")
        {
            StickLayoutGroup row = Row(parent, 12f);
            StickLayoutElement rowMin = row.gameObject.AddComponent<StickLayoutElement>();
            rowMin.MinSize = new Vector2(0f, StickTokens.ROW_H);
            TextMeshProUGUI name = Label(row.transform, nameText, StickLabelKind.Body);
            StickLayoutElement nameMin = name.rectTransform.gameObject.AddComponent<StickLayoutElement>();
            nameMin.MinSize = new Vector2(160f, 0f);
            if (!string.IsNullOrEmpty(hintText))
            {
                TextMeshProUGUI hint = Label(row.transform, hintText, StickLabelKind.Hint);
                StickLayoutElement hintGrow = hint.rectTransform.gameObject.AddComponent<StickLayoutElement>();
                hintGrow.Grow = 1f;
            }
            return row;
        }

        // ---------------- 布局约束（摆放 UI 的唯一正确姿势） ----------------

        /// <summary>把面板居中到父节点中央（anchor 方案，不受窗口尺寸影响；gd center_on_screen）。</summary>
        public static void CenterOnScreen(RectTransform panel, Vector2 size)
        {
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = size;
            panel.anchoredPosition = Vector2.zero;
        }

        /// <summary>把控件停靠到屏幕某角，自动留 SCREEN_MARGIN 安全边距（禁止贴边；gd dock）。</summary>
        public static void Dock(RectTransform node, StickCorner corner, Vector2 size,
            float margin = StickTokens.SCREEN_MARGIN)
        {
            switch (corner)
            {
                case StickCorner.TopLeft:
                    node.anchorMin = node.anchorMax = node.pivot = new Vector2(0f, 1f);
                    node.anchoredPosition = new Vector2(margin, -margin);
                    break;
                case StickCorner.TopRight:
                    node.anchorMin = node.anchorMax = node.pivot = new Vector2(1f, 1f);
                    node.anchoredPosition = new Vector2(-margin, -margin);
                    break;
                case StickCorner.BottomLeft:
                    node.anchorMin = node.anchorMax = node.pivot = new Vector2(0f, 0f);
                    node.anchoredPosition = new Vector2(margin, margin);
                    break;
                case StickCorner.BottomRight:
                    node.anchorMin = node.anchorMax = node.pivot = new Vector2(1f, 0f);
                    node.anchoredPosition = new Vector2(-margin, margin);
                    break;
            }
            node.sizeDelta = size;
            StickLayoutElement min = node.GetComponent<StickLayoutElement>();
            if (min == null)
                min = node.gameObject.AddComponent<StickLayoutElement>();
            min.MinSize = size;   // = gd custom_minimum_size = size
        }

        // ---------------- HUD 预留区与安全矩形（防 UI 重合） ----------------

        /// <summary>计算"安全矩形"：宿主 rect 减去 HUD 预留区（顶栏+材料条 / 底部面板）。
        /// 弹窗初始定位、浮动窗口开窗都必须把自身矩形夹进安全矩形——一劳永逸防"弹窗
        /// 盖住常驻 HUD 按钮"。返回父 rect 空间（原点中心、y 向上）的矩形。</summary>
        public static Rect SafeRect(RectTransform control, float margin = StickTokens.SCREEN_MARGIN)
        {
            Rect vp = control.parent is RectTransform host ? host.rect : new Rect(0f, 0f, 1920f, 1080f);
            float minX = vp.xMin + margin;
            float maxX = vp.xMax - margin;
            float minY = vp.yMin + HudBottomReserved;
            float maxY = vp.yMax - HudTopReserved;
            return Rect.MinMaxRect(minX, Mathf.Min(minY, maxY), maxX, Mathf.Max(minY, maxY));
        }

        /// <summary>把期望矩形夹进安全矩形（保持尺寸，只平移；过大的窗口缩到安全区大小）。</summary>
        public static Rect ClampToSafeRect(RectTransform control, Rect desired,
            float margin = StickTokens.SCREEN_MARGIN)
        {
            Rect safe = SafeRect(control, margin);
            Vector2 size = new Vector2(
                Mathf.Min(desired.width, safe.width),
                Mathf.Min(desired.height, safe.height));
            Vector2 pos = desired.position;
            pos.x = Mathf.Clamp(pos.x, safe.xMin, Mathf.Max(safe.xMin, safe.xMax - size.x));
            pos.y = Mathf.Clamp(pos.y, safe.yMin, Mathf.Max(safe.yMin, safe.yMax - size.y));
            return new Rect(pos, size);
        }

        // ---------------- Toast ----------------

        /// <summary>系统层路由（toast/确认框统一挂载）：找 <see cref="StickUIRoot"/> 的
        /// SystemOverlay 槽（Z_SYSTEM，模态之上）；无 UIRoot（如主菜单场景）回退调用者层。</summary>
        public static Transform SystemOverlayOf(Transform layer)
        {
            return StickUIRoot.Instance != null &&
                   StickUIRoot.Instance.TryGetSlot(StickUIRoot.SlotSystemOverlay, out RectTransform slot)
                ? (Transform)slot
                : layer;
        }

        /// <summary>在指定层弹一条 toast（自动淡出销毁）。底部居中（离底 80px）。
        /// 像素皮：Plate(Light) 暖白条 + 底垫投影（同 SketchWidgets.Toast 口径）。</summary>
        public static void Toast(Transform layer, string text, string kind = "info")
        {
            Transform host = SystemOverlayOf(layer);
            Color tint = kind == "info" ? StickTokens.INFO
                : kind == "warn" ? StickTokens.WARN
                : StickTokens.DANGER;

            RectTransform panel = UiKit.CreateRect("Toast", host);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
            panel.sizeDelta = new Vector2(420f, 57f);   // 高度取 Unit(3) 整数倍
            panel.anchoredPosition = new Vector2(0f, 80f);

            // 投影先建（子件绘制按加入序），再建 Plate 本体——两件都铺满根矩形。
            RectTransform shadow = UiKit.CreateRect("Shadow", panel);
            StickUIKit.FullRect(shadow);
            shadow.anchoredPosition = PixelSkin.ShadowOffset;
            var shadowImage = shadow.gameObject.AddComponent<Image>();
            shadowImage.sprite = PixelSkin.ShadowSprite;
            shadowImage.type = Image.Type.Sliced;
            shadowImage.color = Color.white;
            shadowImage.raycastTarget = false;

            RectTransform plate = UiKit.CreateRect("Plate", panel);
            StickUIKit.FullRect(plate);
            var image = plate.gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(PixelTone.Light);
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色
            image.raycastTarget = false;
            UiKit.CreateText("Msg", panel, text, (int)StickTokens.FONT_BODY,
                TextAlignmentOptions.Center, tint, HandFont());
            panel.gameObject.AddComponent<ToastFader>();
        }

        /// <summary>Toast 消失器（T_TOAST 停留 + T_PANEL×2 淡出销毁，对齐 gd create_tween 序列）。</summary>
        sealed class ToastFader : MonoBehaviour
        {
            IEnumerator Start()
            {
                yield return new WaitForSecondsRealtime(StickTokens.T_TOAST);
                var group = gameObject.AddComponent<CanvasGroup>();
                float t = 0f;
                while (t < StickTokens.T_PANEL * 2f)
                {
                    t += Time.unscaledDeltaTime;
                    group.alpha = 1f - Mathf.Clamp01(t / (StickTokens.T_PANEL * 2f));
                    yield return null;
                }
                Destroy(gameObject);
            }
        }

        // ---------------- 确认框 ----------------

        /// <summary>模态确认框（确认框族最小实现）：message + 确定/取消。onConfirm 在点
        /// 确定后调用；ESC/取消关闭。游戏内入 <see cref="StickModalStack"/> CONFIRM 层
        ///（逐层退栈）；无 UIRoot 环境（主菜单）回退自管理（直接 Open）。</summary>
        public static void Confirm(Transform layer, string title, string message,
            Action onConfirm, string confirmText = "确定",
            StickButtonKind kind = StickButtonKind.Accent)
        {
            Transform host = SystemOverlayOf(layer);
            GameObject go = new GameObject("ConfirmDialog", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var dialog = go.AddComponent<StickConfirmDialog>();
            dialog.Setup(title, message, onConfirm, confirmText, kind);
            StickModalStack stack = StickUIRoot.Instance != null ? StickUIRoot.Instance.ModalStack : null;
            if (stack != null)
                stack.Push(dialog, StickModalLayer.Confirm);
            else
                dialog.Open();
        }

        // ---------------- hover 微缩放（按钮"浮起"感） ----------------

        /// <summary>gd _setup_button 的 hover tween 移植：pivot 双向居中（装配时宽度未定，
        /// 每帧跟随——锚在左缘会向右下放大，创始人否了），进入 1.03 / 0.08s，退出 1.0 / 0.1s。</summary>
        sealed class StickHoverScale : MonoBehaviour,
            UnityEngine.EventSystems.IPointerEnterHandler,
            UnityEngine.EventSystems.IPointerExitHandler
        {
            const float HoverScale = 1.03f;

            Coroutine _active;

            void Update()
            {
                if (transform is RectTransform rt && rt != null)
                    rt.pivot = new Vector2(0.5f, 0.5f);   // 装配时宽度未定，持续居中
            }

            void UnityEngine.EventSystems.IPointerEnterHandler.OnPointerEnter(
                UnityEngine.EventSystems.PointerEventData eventData)
            {
                ScaleTo(HoverScale, 0.08f);
            }

            void UnityEngine.EventSystems.IPointerExitHandler.OnPointerExit(
                UnityEngine.EventSystems.PointerEventData eventData)
            {
                ScaleTo(1f, 0.1f);
            }

            void ScaleTo(float target, float duration)
            {
                if (_active != null)
                    StopCoroutine(_active);
                _active = StartCoroutine(ScaleRoutine(target, duration));
            }

            IEnumerator ScaleRoutine(float target, float duration)
            {
                Vector2 from = transform.localScale;
                Vector2 to = new Vector2(target, target);
                float t = 0f;
                while (t < duration)
                {
                    t += Time.unscaledDeltaTime;
                    transform.localScale = Vector2.Lerp(from, to, Mathf.Clamp01(t / duration));
                    yield return null;
                }
                transform.localScale = to;
                _active = null;
            }
        }
    }
}
