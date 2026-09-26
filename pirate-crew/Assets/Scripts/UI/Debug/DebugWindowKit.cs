using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>窗体变体（window.h:26 <c>Window::Type</c> 的画布内子集）。</summary>
    public enum WindowVariant
    {
        /// <summary>带标题栏：setText + 标题标签 + 关闭钮（window.cpp:127-130），皮 = 件 "window"。</summary>
        WithTitleBar = 0,
        /// <summary>无标题栏：无标题标签、无关闭钮，皮 = 件 "menu"（window_without_title）。</summary>
        WithoutTitleBar = 1,
    }

    /// <summary>
    /// 调试窗工厂与内容排版小件（调试菜单族的公共地基层）。
    ///
    /// 【以库为源】窗体形状逐项落自 external/aseprite-ref/src/ui/window.cpp + 主题件，
    /// 不再手摆近似：
    ///   · 变体——<see cref="WindowVariant"/> 对应 <c>Window::Type</c>（window.h:26）；
    ///     WithTitleBar 才建标题标签与关闭钮（window.cpp:127-130 构造器；
    ///     window.cpp:708-727 <c>onBuildTitleLabel</c>：文本空则不建标签）。
    ///   · 标题带——theme 件 "window" 顶切片 h1=15（theme.xml:165），内容顶 =
    ///     window_with_title <c>border-top=17</c>（theme.xml:472；=<c>childrenBounds().y - bounds().y</c>，
    ///     window.cpp:793）。
    ///   · 标题字——origin = 窗左上 + (margin-left 5, margin-top 5)（theme.xml:476
    ///     window_title_label；theme.cpp:210-217 setDecorativeWidgetBounds 的 label 分支），
    ///     右缘被 <c>limitTitleLabelBounds</c> 裁到最左装饰钮左缘（window.cpp:729-748）。
    ///   · 关闭钮——theme window_close_button：件 9×11（theme.xml:167-169）+ 图标 5×6 居中
    ///     （theme.xml:170；居中取整 (9-5)/2=2、(11-6)/2=2，theme.cpp paintLayer 的
    ///     align=CENTER|MIDDLE 默认），margin-top 3 / margin-right 3（theme.xml:487），
    ///     位置 = <c>windowBounds.x2() - margin.right() - w</c>（theme.cpp:215-222）。
    ///   · 边框——件 "window" 九宫格切片（左3 下5 右3 上15，theme.xml:165；
    ///     BeveledPixelSpriteBuilder 直切落 Sprite.border）。
    ///   · 尺寸下限——<c>limitSize</c>（window.cpp:776-780）：≥ border().width() × border().height()。
    ///
    /// 窗 = <see cref="SketchPanel"/> titled（Plate 孩子换件 "window"）+
    /// <see cref="WindowDragger"/> 拖动带（= 源 <c>m_isMoveable</c> 的画布内落地）；
    /// 内容排版用「顶左锚 + 纵向游标」手工摆（源<b>不</b>排版内容——非装饰孩子在
    /// windowSetPosition 里被整块塞进 childrenBounds，window.cpp:750-774；本工程改为
    /// 调用方从 <see cref="ContentTop"/> / <see cref="Pad"/> 起排）。
    ///
    /// 【语义面映射】window.h 的三个开关在画布内各有所指：
    ///   · <c>m_isMoveable</c>（:117，非桌面默认 true）→ 是否挂 <see cref="WindowDragger"/>；
    ///   · <c>m_isOnTop</c>（:64 setOnTop）→ <see cref="WindowDragger.RaiseToCanvasTop"/>
    ///     的画布级置顶（点击窗即提层）；
    ///   · <c>m_isSizeable</c>（:173 setSizeable）→ <b>画布内不适用</b>：源的缩放走
    ///     border 命中区 + 九个方向的原生鼠标游标（window.cpp:245-286 / 562-583），
    ///     画布里既无原生窗也无系统游标，登记为范围外。
    ///
    /// 【不适用登记（本工程画布内无原生窗，登记不造假）】native window / 多显示器 /
    /// <c>ownDisplay</c> / <c>remapWindow</c> 的屏幕坐标系（<c>loadLayout/saveLayout</c>）/
    /// maximize-minimize 系统钮 / <c>centerWindow</c>+<c>fit_bounds</c> 的屏幕居中 /
    /// <c>onBroadcastMouseMessage</c> 的多窗鼠标散射 / <c>WindowCloseButton</c> 的 Esc
    /// 关闭（window.cpp:82-99，依赖 <c>shouldProcessEscKeyToCloseWindow()=isForeground()</c>
    /// 的前台窗概念）/ <c>setAutoRemap</c>+<c>remapWindow</c>（window.cpp:163-166 / 311-324，
    /// 只在 native 重开时生效；画布内「建即可见」由构造语义覆盖）/ <c>onInvalidateRegion</c>
    /// 的逐窗重绘裁剪（window.cpp:590-631，UGUI 自带）。
    /// </summary>
    public static class DebugWindowKit
    {
        // ---------------- theme.xml <parts> 件几何（数值即件声明，禁目测） ----------------

        /// <summary>关闭钮件宽 9（theme.xml:167-169 window_button_normal/hot/selected 9×11）。</summary>
        public const float CloseButtonWidth = 9f;
        /// <summary>关闭钮件高 11（同上）。</summary>
        public const float CloseButtonHeight = 11f;

        /// <summary>窗控图标宽 5（theme.xml:170-174 window_close/help/…_icon 5×6）。</summary>
        public const float WindowIconWidth = 5f;
        /// <summary>窗控图标高 6（同上）。</summary>
        public const float WindowIconHeight = 6f;

        // ---------------- theme.xml <styles> 布局（经 AseLayout 令牌表） ----------------

        /// <summary>带标题窗内容边距（左/右/下）= window_with_title border=6（theme.xml:472）。</summary>
        public const float Pad = AseLayout.WindowBorder;

        /// <summary>标题带高 = 件 "window" 顶切片 h1=15（theme.xml:165
        /// <c>h1="15"</c>；与 <see cref="PixelSkin.WindowTitleBand"/> 同值）。</summary>
        public const float TitleBand = PixelSkin.WindowTitleBand;

        /// <summary>带标题窗内容顶 = window_with_title border-top=17（theme.xml:472；
        /// = 标题带 15 整条 + 带下 2 格缝；也是源 <c>childrenBounds().y - bounds().y</c>）。</summary>
        public const float ContentTop = AseLayout.WindowBorderTop;

        /// <summary>无标题变体内容边距 = window_without_title border=3（theme.xml:468）。</summary>
        public const float BarePad = AseLayout.PopupBorder;

        /// <summary>标题右让位（关闭钮占位）= margin-right 3 + 件宽 9（theme.xml:487 + 167）。
        /// 源把标题裁到<b>最左装饰钮</b>左缘（window.cpp:735-747），故这是带一个关闭钮时的值；
        /// 另有左邻按钮时见 <see cref="TitleReserve"/>。</summary>
        public const float TitleRightReserve = AseLayout.CloseButtonMarginRight + CloseButtonWidth;

        /// <summary>调试窗标题字体（与展示页同源：FusionPixel SDF，按字号解析档）。</summary>
        public static TMP_FontAsset HandFont => PixelShowcasePage.PixelFont();

        /// <summary>窗体边框件（theme.xml:165 "window" 3/7/3 × 15/4/5 → 切片 左3 下5 右3 上15；
        /// 无标题变体用 theme.xml:166 "menu" 3/10/3 × 3/9/4 → 切片 左3 下4 右3 上3）。
        /// 源里这是 style 的 <c>&lt;border part="…"/&gt;</c>（theme.xml:470 / 474）。</summary>
        public static Sprite BorderPart(WindowVariant variant = WindowVariant.WithTitleBar)
        {
            return PixelSkin.Ase(variant == WindowVariant.WithTitleBar ? "window" : "menu");
        }

        /// <summary>border()（源里 <c>border().width()/height()</c> 的唯一出口，window.cpp:673/778-779）：
        /// 带标题 = 宽 6+6 / 高 17+6（theme.xml:472），无标题 = 3+3 / 3+3（theme.xml:468）。</summary>
        public static Vector2 BorderSize(WindowVariant variant = WindowVariant.WithTitleBar)
        {
            return variant == WindowVariant.WithTitleBar
                ? new Vector2(AseLayout.WindowBorder * 2f,
                    AseLayout.WindowBorderTop + AseLayout.WindowBorder)
                : new Vector2(AseLayout.PopupBorder * 2f, AseLayout.PopupBorder * 2f);
        }

        /// <summary><c>limitSize</c>（window.cpp:776-780）：窗不小于 border().width() × border().height()。</summary>
        public static Vector2 MinSize(WindowVariant variant = WindowVariant.WithTitleBar)
        {
            return BorderSize(variant);
        }

        /// <summary>
        /// <c>Window::onSizeHint</c>（window.cpp:646-675）画布内子集：非桌面窗的期望尺寸 =
        /// max(标题标签尺寸, 非装饰孩子最大尺寸) + border()；带标题时两轴先有 16 的下限
        /// （源 <c>maxSize.w = maxSize.h = 16 * guiscale()</c>）。
        /// <b>桌面窗分支（manager childrenBounds）不适用</b>——画布内无 native 桌面。
        /// 本工程窗尺寸由调用方显式给，本函数供装配侧按内容反算时对齐源公式（纯函数，可无头断言）。
        /// </summary>
        /// <param name="contentSize">非装饰孩子（内容件）的最大 sizeHint。</param>
        /// <param name="titleLabelSize">标题标签 sizeHint（= 文本尺寸）；无标题传 (0,0)。</param>
        public static Vector2 SizeHint(Vector2 contentSize, Vector2 titleLabelSize = default(Vector2),
            WindowVariant variant = WindowVariant.WithTitleBar)
        {
            bool titled = variant == WindowVariant.WithTitleBar;
            // 源：if (m_titleLabel) maxSize.w = maxSize.h = 16 * guiscale();
            float w = titled ? 16f : 0f;
            float h = titled ? 16f : 0f;
            // 源：遍历非装饰孩子取 max。
            w = Mathf.Max(w, contentSize.x);
            h = Mathf.Max(h, contentSize.y);
            // 源：if (m_titleLabel) maxSize.w = max(maxSize.w, m_titleLabel->sizeHint().w);
            if (titled)
                w = Mathf.Max(w, titleLabelSize.x);
            Vector2 border = BorderSize(variant);
            return new Vector2(w + border.x, h + border.y);
        }

        /// <summary>
        /// 建一枚可拖动调试窗（默认带 × 关闭；可选 ? 帮助钮在其左——New Sprite 等参考图
        /// 实拍窗是两钮）。<paramref name="topLeft"/> = 距画布左上角的画布坐标；窗体锚/枢轴
        /// 固定 (0,1)，拖动带与夹取按此约定。
        ///
        /// 几何全部落自源：标题带 15 / 内容顶 17 / 内容边距 6 / 关闭钮 9×11 距右 3 距顶 3 /
        /// 标题 origin (5,5) 且右缘裁到关闭钮左缘。
        /// </summary>
        public static RectTransform CreateWindow(Transform canvas, string name, string title,
            Vector2 topLeft, Vector2 size, bool closeButton = true, bool helpButton = false)
        {
            SketchPanel panel = SketchPanel.Create(canvas, name,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(topLeft.x, -topLeft.y), ClampSize(size, WindowVariant.WithTitleBar),
                SketchPanel.Tone.Dark, titled: true);
            RectTransform root = (RectTransform)panel.transform;
            root.SetAsLastSibling();

            // 标题标签：origin = (margin-left 5, margin-top 5)，右缘裁到最左装饰钮左缘
            // （window.cpp:729-748；onBuildTitleLabel 在文本空时不建标签，window.cpp:710-716）。
            if (!string.IsNullOrEmpty(title))
                UiKit.EnsureTitleLabel(root, title, HandFont, UiSkin.Font.Body,
                    TitleReserve(closeButton, helpButton));

            if (closeButton)
                CreateCloseButton(root);
            if (helpButton)
                CreateHelpButton(root, closeButton);

            // movable（window.h:117 m_isMoveable；非桌面窗默认 true，window.cpp:116）：
            // 画布内 ≡ 挂标题带拖动带。
            WindowDragger.Attach(root);
            return root;
        }

        /// <summary>
        /// 无标题变体窗（<see cref="WindowVariant.WithoutTitleBar"/>）：无标题标签、无窗控钮，
        /// 皮 = 件 "menu"（window_without_title border=3，theme.xml:468-471），内容从
        /// <see cref="BarePad"/> 起排。movable 仍成立——源只看 isDesktop（window.cpp:116）。
        /// 本工程暂无调用方（登记为覆盖到位、按需启用）。
        /// </summary>
        public static RectTransform CreateBareWindow(Transform canvas, string name,
            Vector2 topLeft, Vector2 size)
        {
            SketchPanel panel = SketchPanel.Create(canvas, name,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(topLeft.x, -topLeft.y), ClampSize(size, WindowVariant.WithoutTitleBar),
                SketchPanel.Tone.Dark, titled: false);
            RectTransform root = (RectTransform)panel.transform;
            root.SetAsLastSibling();

            // 无标题变体的边框件与带标题不同（theme.xml:470 "menu" vs :474 "window"）。
            Image plate = FindPart(root, "Plate");
            if (plate != null)
            {
                plate.sprite = BorderPart(WindowVariant.WithoutTitleBar);
                plate.type = Image.Type.Sliced;
                plate.pixelsPerUnitMultiplier = 1f;
                plate.color = Color.white;
            }

            WindowDragger.Attach(root);
            return root;
        }

        /// <summary>
        /// 关闭钮（<c>WindowCloseButton</c>，window.cpp:62-104）：theme window_close_button
        /// 9×11 件 + 5×6 图标居中，位置 = 窗右缘 − margin-right 3 − 件宽 9、窗顶 + margin-top 3
        /// （theme.cpp:215-222）；三态换件（常态/hot/selected，theme.xml:483-485）。
        /// 点击 = <c>closeWindow()</c>（window.cpp:70-75）→ 本工程画布内退化为隐藏窗根。
        /// </summary>
        public static Button CreateCloseButton(RectTransform window)
        {
            Button close = UiKit.CreateWindowButton(window, "CloseButton",
                PixelSkin.WindowIconSprite(PixelSkin.WindowIcon.Close),
                AseLayout.Px(AseLayout.CloseButtonMarginRight));
            close.onClick.AddListener(() => window.gameObject.SetActive(false));
            return close;
        }

        /// <summary>
        /// ? 帮助钮。<b>本版源库无对应</b>：window.cpp 的 Window 只有关闭钮一个装饰孩子
        /// （window.cpp:129），theme 里的 window_help_button 等是未被本版实例化的遗留件。
        /// 位置沿用工程约定 = 关闭钮左邻一格（gap = window_button margin-right 1），
        /// 登记为 subst（无源可依）。
        /// </summary>
        public static Button CreateHelpButton(RectTransform window, bool closeButton = true)
        {
            float right = AseLayout.Px(AseLayout.CloseButtonMarginRight) + CloseButtonWidth
                + (closeButton ? AseLayout.Px(AseLayout.WindowButtonGap) : 0f);
            Button help = UiKit.CreateWindowButton(window, "HelpButton",
                PixelSkin.WindowIconSprite(PixelSkin.WindowIcon.Help), right);
            help.onClick.AddListener(() => { });
            return help;
        }

        /// <summary>标题右让位（同 limitTitleLabelBounds 取“最左装饰钮左缘”，window.cpp:735-747）：
        /// 关闭钮占 margin-right 3 + 宽 9；再有其左邻钮时各加 gap 1 + 宽 9。</summary>
        public static float TitleReserve(bool closeButton = true, bool helpButton = false)
        {
            float reserve = TitleRightReserve;                 // 关闭钮（最右）
            if (closeButton && helpButton)                     // help 在关闭钮左邻一格
                reserve += AseLayout.Px(AseLayout.WindowButtonGap) + CloseButtonWidth;
            return reserve;
        }

        /// <summary>文本件（UiKit.CreateText 同源：字档解析 + 顶点像素对齐；禁换行）。</summary>
        public static TextMeshProUGUI Label(Transform parent, string content, int fontSize, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TextMeshProUGUI text = UiKit.CreateText("Label", parent, content, fontSize, align, color, HandFont);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>摆一个文本件到 (x, y)（顶左锚），给足宽度。</summary>
        public static TextMeshProUGUI PlaceLabel(RectTransform parent, string content, int fontSize,
            Color color, float x, float y, float w, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TextMeshProUGUI text = Label(parent, content, fontSize, color, align);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w, fontSize + 4f);
            rect.anchoredPosition = new Vector2(x, -y);
            return text;
        }

        /// <summary>蓝字分组线（theme horizontal_separator：x=4 蓝字 + 标签右缘后起铺点线）。
        /// 推进游标 <paramref name="y"/>。</summary>
        public static void Section(RectTransform parent, string title, float width, ref float y)
        {
            var row = new GameObject("Section_" + title, typeof(RectTransform));
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, 13f);
            rect.anchoredPosition = new Vector2(0f, -y);

            TextMeshProUGUI label = Label(rect, title, UiSkin.Font.Tiny,
                PixelSkin.Theme.SeparatorLabel, TextAlignmentOptions.Left);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(200f, 13f);
            labelRect.anchoredPosition = new Vector2(AseLayout.Px(AseLayout.SeparatorTextX), 0f);

            float lineX = AseLayout.Px(AseLayout.SeparatorTextX) + Mathf.Ceil(label.preferredWidth)
                + AseLayout.Px(AseLayout.SeparatorBorder);
            SketchSeparator.Create(rect, "Line", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(lineX, 0f), new Vector2(width - Pad - lineX, 1f),
                SketchSeparator.Direction.Horizontal);
            y += 13f + 4f;
        }

        /// <summary>limitSize 落地（window.cpp:776-780）：窗尺寸先抬到 border 和以上。</summary>
        static Vector2 ClampSize(Vector2 size, WindowVariant variant)
        {
            Vector2 min = MinSize(variant);
            return new Vector2(Mathf.Max(size.x, min.x), Mathf.Max(size.y, min.y));
        }

        /// <summary>按子件名取 Image（SketchPanel 的 Plate 孩子）。</summary>
        static Image FindPart(RectTransform root, string partName)
        {
            Transform part = root.Find(partName);
            return part != null ? part.GetComponent<Image>() : null;
        }
    }
}
