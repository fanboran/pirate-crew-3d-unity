using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 滚动视图委托（源 <c>src/ui/scroll_bar.h:15-21</c> <c>ScrollableViewDelegate</c>）：
    /// 滚动条只认这三件事——可视区大小、当前滚动偏移、设置滚动偏移。
    /// </summary>
    public interface IAseScrollView
    {
        /// <summary>visibleSize()（view.cpp:124-128）。</summary>
        Vector2Int VisibleSize { get; }
        /// <summary>viewScroll()（view.cpp:130-133）。</summary>
        Vector2Int ViewScroll { get; }
        /// <summary>setViewScroll()（view.cpp:135-138 → onSetViewScroll）。</summary>
        void SetViewScroll(Vector2Int pt);
    }

    /// <summary>
    /// Aseprite <c>View</c> + <c>Viewport</c> + <c>setup_scrollbars</c> 逐函数移植
    /// （源：<c>src/ui/view.cpp</c>、<c>src/ui/viewport.cpp:30-53</c>、<c>src/ui/scroll_helper.cpp:16-82</c>）。
    ///
    /// 【坐标口径】内部一律用 Aseprite 的整数像素坐标：客户区左上为原点、y 向下；
    /// 子件（视口/滚动条/内容）都挂在 <c>Client</c> 这一层上（= View 的 clientBounds，
    /// 边框内缩由调用方给）。UGUI 侧只做一次映射：
    /// <c>anchoredPosition = (x, -y)</c>，anchor/pivot 全取左上。
    ///
    /// 【移植映射（源 → 本文件）】
    /// <list type="bullet">
    /// <item><c>View::setScrollableSize</c>（view.cpp:98-122）→ <see cref="SetScrollableSize"/></item>
    /// <item><c>View::updateView</c>（view.cpp:143-190）→ <see cref="UpdateView"/></item>
    /// <item><c>View::onSetViewScroll</c>（view.cpp:274-376）→ <see cref="SetViewScroll"/></item>
    /// <item><c>View::limitScrollPosToViewport</c>（view.cpp:404-410）→ 同名私有方法</item>
    /// <item><c>View::updateAttachedWidgetBounds</c>（view.cpp:389-402）→ <see cref="LayoutAttachedWidget"/></item>
    /// <item><c>Viewport::onResize</c>（viewport.cpp:30-53）→ 同一份 <see cref="LayoutAttachedWidget"/></item>
    /// <item><c>setup_scrollbars</c>（scroll_helper.cpp:16-82）→ <see cref="SetupScrollbars"/></item>
    /// <item><c>View::scrollByMessage</c>（view.cpp:212-231）→ <see cref="ScrollByMessage"/></item>
    /// </list>
    ///
    /// 【未移植（登记在交付报告）】<c>hideScrollBars/showScrollBars</c>（view.cpp:81-91）、
    /// <c>makeVisibleAllScrollableArea</c>（72-79，靠 sizeHint 反推窗体尺寸，调用方已给尺寸）、
    /// <c>onScrollRegion</c>/<c>move_region</c> 局部重绘（view.cpp:283-376 的显示层优化，
    /// UGUI 由 RectMask2D + 整窗重绘替代）、<c>View::getView</c>（静态反查父链，
    /// 本移植由挂载方直接持有引用）。
    /// </summary>
    public sealed class AseView : MonoBehaviour, IAseScrollView
    {
        RectTransform _rect;        // View 自身（尺寸由调用方给定）
        RectTransform _client;      // childrenBounds()：View 边框内的客户区（子件坐标原点）
        RectTransform _viewport;    // m_viewport
        RectTransform _content;     // attachedWidget()

        AseScrollBar _hbar;
        AseScrollBar _vbar;

        int _borderL, _borderT, _borderR, _borderB;
        bool _hasBars = true;
        Vector2Int _contentHint;    // Viewport::calculateNeededSize()：子件 sizeHint 的逐轴最大值
        Vector2Int _scrollableSize; // getScrollableSize()：= (hbar.size(), vbar.size())
        RectInt _viewportArea;      // 当前视口区（客户区坐标，滚动条已扣）

        /// <summary>视口（把内容挂进来；对应 <c>view->viewport()</c>）。</summary>
        public RectTransform Viewport { get { return _viewport; } }

        /// <summary>按 View 边框建一个 View（源里 View 的边框来自 theme <c>view</c> 样式，
        /// 由调用方按自己的样式表传入）。</summary>
        public static AseView Attach(RectTransform viewRect, int borderLeft, int borderTop,
            int borderRight, int borderBottom)
        {
            AseView view = viewRect.gameObject.AddComponent<AseView>();
            view.Build(viewRect, borderLeft, borderTop, borderRight, borderBottom);
            return view;
        }

        void Build(RectTransform viewRect, int borderLeft, int borderTop, int borderRight, int borderBottom)
        {
            _rect = viewRect;
            _borderL = borderLeft;
            _borderT = borderTop;
            _borderR = borderRight;
            _borderB = borderBottom;

            _client = UiKit.CreateRect("Client", _rect);
            _client.anchorMin = Vector2.zero;
            _client.anchorMax = Vector2.one;
            _client.pivot = new Vector2(0f, 1f);
            _client.offsetMin = new Vector2(_borderL, _borderB);
            _client.offsetMax = new Vector2(-_borderR, -_borderT);

            // Viewport（skin_theme.cpp:1249-1252 kViewViewportWidget：BORDER(0) + childSpacing 0）
            _viewport = UiKit.CreateRect("Viewport", _client);
            UiKit.SetAnchored(_viewport, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            // 源在绘制层把视口当裁剪区（Viewport::onPaint → paintViewViewport，视口自身透明）；
            // UGUI 侧经 AseUi.ClipViewport 单点挂 RectMask2D + 透明接光面。
            AseUi.ClipViewport(_viewport);

            _hbar = AseScrollBar.Create(this, _client, true);
            _vbar = AseScrollBar.Create(this, _client, false);
            _hbar.SetAttached(false);
            _vbar.SetAttached(false);
            _hbar.SetVisible(false);
            _vbar.SetVisible(false);
        }

        /// <summary><c>View::attachToView</c>（view.cpp:62-65）：把被滚件挂进视口。</summary>
        public void AttachToView(RectTransform content)
        {
            _content = content;
            content.SetParent(_viewport, false);
            // listbox.cpp:258-259 kMouseWheelMessage → View::scrollByMessage：滚轮事件挂在
            // 被滚件上（源的收件件就是它），事件沿父链上浮到 View。UGUI 侧挂在 **viewport**
            // 这一层：指针落在被滚件的图形上、或落在视口空白接光面（<see cref="AseUi.ClipViewport"/>
            // 的透明 raycast 面）上，命中级联都恰好经过这里——挂 content 本体会漏掉空白区。
            _viewport.gameObject.AddComponent<AseViewWheel>().Bind(this);
        }

        /// <summary>
        /// 被滚件的 sizeHint（= <c>Viewport::calculateNeededSize()</c>，viewport.cpp:59-71：
        /// 子件 sizeHint 的逐轴最大值）。UGUI 的 RectTransform 没有 sizeHint 概念
        /// （sizeDelta 是拉伸后的实际尺寸），故由挂载方量好供给。
        /// </summary>
        public void SetContentHint(int w, int h)
        {
            _contentHint = new Vector2Int(w, h);
        }

        /// <summary>getScrollableSize()（view.cpp:93-96）。</summary>
        public Vector2Int ScrollableSize { get { return _scrollableSize; } }

        /// <summary>visibleSize()（view.cpp:124-128）——视口边界减视口边框（视口 border = 0）。</summary>
        public Vector2Int VisibleSize
        {
            get { return new Vector2Int(_viewportArea.width, _viewportArea.height); }
        }

        /// <summary>viewScroll()（view.cpp:130-133）：滚动偏移就是两根条的位置。</summary>
        public Vector2Int ViewScroll
        {
            get { return new Vector2Int(_hbar.Pos, _vbar.Pos); }
        }

        /// <summary>
        /// <c>View::updateView</c>（view.cpp:143-190）：先按「无内容」撤掉条，再按内容 hint 重排，
        /// 有条款时再排一次（条会吃掉可视区、可能反过来又需要另一根条），最后恢复滚动位。
        /// </summary>
        public void UpdateView(bool restoreScrollPos = true)
        {
            if (_content == null)
                return;

            Vector2Int scroll = ViewScroll;

            // view.cpp:158-167
            SetScrollableSize(Vector2Int.zero, false);
            SetScrollableSize(_contentHint, false);
            if (_hbar.Attached || _vbar.Attached)
                SetScrollableSize(_contentHint, false);

            // view.cpp:169 m_viewport.setBounds(m_viewport.bounds()) → Viewport::onResize
            ApplyViewportArea();

            if (restoreScrollPos)
                SetViewScroll(scroll);       // view.cpp:170-175
            else
                SetViewScroll(Vector2Int.zero);

            LayoutAttachedWidget(ViewScroll);   // view.cpp:177-182
        }

        /// <summary><c>View::setScrollableSize</c>（view.cpp:98-122）。</summary>
        public void SetScrollableSize(Vector2Int sz, bool setScrollPos = true)
        {
            RectInt viewportArea = ClientArea();

            if (_hasBars)
            {
                viewportArea = SetupScrollbars(sz, viewportArea);   // view.cpp:103
            }
            else
            {
                // view.cpp:105-114（本移植没有 hideScrollBars 的调用点，保留分支备查）
                DetachBar(_hbar);
                DetachBar(_vbar);
                _hbar.SetVisible(false);
                _vbar.SetVisible(false);
                _hbar.SetSize(sz.x);
                _vbar.SetSize(sz.y);
            }

            _viewportArea = viewportArea;
            _scrollableSize = sz;
            ApplyViewportArea();                            // view.cpp:115 setBoundsQuietly

            if (setScrollPos)
                SetViewScroll(ViewScroll);                  // view.cpp:118-121
        }

        /// <summary>
        /// <c>View::onSetViewScroll</c>（view.cpp:274-376）去掉显示层重绘：
        /// 钳取 → 挪被滚件 → 同步两根条的位置 → onScrollChange。
        /// </summary>
        public void SetViewScroll(Vector2Int pt)
        {
            if (!isActiveAndEnabled)                        // view.cpp:277 if (!isVisible()) return;
                return;

            Vector2Int oldScroll = ViewScroll;
            Vector2Int newScroll = LimitScrollPosToViewport(pt);
            if (newScroll == oldScroll)
                return;                                     // view.cpp:282-283

            LayoutAttachedWidget(newScroll);                // view.cpp:325
            _hbar.SetPos(newScroll.x);                      // view.cpp:328
            _vbar.SetPos(newScroll.y);                      // view.cpp:329
            // onScrollChange()（view.cpp:384-387）是空钩子，无对端
        }

        /// <summary><c>View::limitScrollPosToViewport</c>（view.cpp:404-410）。</summary>
        Vector2Int LimitScrollPosToViewport(Vector2Int pt)
        {
            Vector2Int maxSize = _scrollableSize;
            Vector2Int visible = VisibleSize;
            return new Vector2Int(
                Mathf.Clamp(pt.x, 0, Mathf.Max(0, maxSize.x - visible.x)),
                Mathf.Clamp(pt.y, 0, Mathf.Max(0, maxSize.y - visible.y)));
        }

        /// <summary><c>View::scrollByMessage</c>（view.cpp:212-231）+ ListBox 的滚轮转发（listbox.cpp:258-259）。
        ///
        /// 乘数 = 被滚件 <c>textHeight()*3</c>（源里缺省值），本移植被滚件是 ListBox（无文本，
        /// 走字体度量的行高）=<see cref="UiSkin.Font"/>.Tiny(8) × 3 = 24。
        ///
        /// 【符号口径】源的 wheelDelta 是 os 层原值、跨平台不一致：<c>state_with_wheel_behavior.cpp:285-290</c>
        /// 明说 "on macOS the mouse wheel is correct, up increase …, But on Windows and Linux it's inverted"，
        /// 即 Windows 上滚轮 → wheelDelta.y = -1。UGUI 的 scrollDelta.y 已归一（上滚 = +1），
        /// 故取负号对齐 Windows 版实感（上滚 = 看上面）。
        /// 源另有 preciseWheel 分支（精确滚轮按原值累加）——UGUI 不暴露该标志，
        /// 以「非整档 delta（触控板连续滚动）当精确」近似。
        /// </summary>
        public void ScrollByMessage(float dx, float dy)
        {
            bool precise = Mathf.Abs(dx) < 1f && Mathf.Abs(dy) < 1f;
            Vector2Int scroll = ViewScroll;
            if (precise)
            {
                scroll.x += Mathf.RoundToInt(-dx);
                scroll.y += Mathf.RoundToInt(-dy);
            }
            else
            {
                scroll.x += Mathf.RoundToInt(-dx) * WheelMultiplier;
                scroll.y += Mathf.RoundToInt(-dy) * WheelMultiplier;
            }
            SetViewScroll(scroll);
        }

        /// <summary>View::scrollByMessage 的缺省乘数：被滚件 textHeight()×3（view.cpp:220-221）。</summary>
        const int WheelMultiplier = UiSkin.Font.Tiny * 3;

        // ------------------------------------------------------------------
        // 布局（下行全是私有实现，逐条对源）
        // ------------------------------------------------------------------

        /// <summary>childrenBounds()：View 边框内的客户区，客户区左上为原点、y 向下。</summary>
        RectInt ClientArea()
        {
            int w = Mathf.RoundToInt(_rect.rect.width) - (_borderL + _borderR);
            int h = Mathf.RoundToInt(_rect.rect.height) - (_borderT + _borderB);
            return new RectInt(0, 0, Mathf.Max(0, w), Mathf.Max(0, h));
        }

        /// <summary>
        /// <c>setup_scrollbars</c>（scroll_helper.cpp:16-82）。
        ///
        /// <c>NEED_BAR(w,h)</c> 是个 token 替换把戏：宏体里写的是 <c>scrollableSize.w</c>／
        /// <c>viewportArea.w</c>，形参名 w/h 会替换进这些成员名，于是
        /// <c>NEED_BAR(w,h)</c> 比 w 轴、<c>NEED_BAR(h,w)</c> 比 h 轴。此处用
        /// <see cref="NeedBar"/> 的轴序（0=w，1=h）忠实复刻：
        /// <c>NEED_BAR(x,y)</c> = 该轴 scrollable &gt; viewportArea，且两根条都塞得进原视口。
        /// </summary>
        RectInt SetupScrollbars(Vector2Int scrollableSize, RectInt viewportArea)
        {
            Vector2Int fullViewportArea = new Vector2Int(viewportArea.width, viewportArea.height);

            _hbar.SetSize(scrollableSize.x);        // scroll_helper.cpp:28
            _vbar.SetSize(scrollableSize.y);        // scroll_helper.cpp:29

            DetachBar(_hbar);                       // scroll_helper.cpp:31-34
            DetachBar(_vbar);

            bool NeedBar(int x, int y)
            {
                return Axis(scrollableSize, x) > Axis(viewportArea, x)
                    && _vbar.BarWidth < Axis(fullViewportArea, x)
                    && _hbar.BarWidth < Axis(fullViewportArea, y);
            }

            if (NeedBar(0, 1))                      // scroll_helper.cpp:36-50
            {
                viewportArea.height -= _hbar.BarWidth;
                AttachBar(_hbar);

                if (NeedBar(1, 0))
                {
                    viewportArea.width -= _vbar.BarWidth;
                    if (NeedBar(0, 1))
                        AttachBar(_vbar);
                    else
                    {
                        viewportArea.width += _vbar.BarWidth;
                        viewportArea.height += _hbar.BarWidth;
                        DetachBar(_hbar);
                    }
                }
            }
            else if (NeedBar(1, 0))                 // scroll_helper.cpp:51-65
            {
                viewportArea.width -= _vbar.BarWidth;
                AttachBar(_vbar);

                if (NeedBar(0, 1))
                {
                    viewportArea.height -= _hbar.BarWidth;
                    if (NeedBar(1, 0))
                        AttachBar(_hbar);
                    else
                    {
                        viewportArea.width += _vbar.BarWidth;
                        viewportArea.height += _hbar.BarWidth;
                        DetachBar(_vbar);
                    }
                }
            }

            if (_hbar.Attached)                     // scroll_helper.cpp:67-73
            {
                _hbar.SetBounds(new RectInt(viewportArea.x,
                    viewportArea.y + viewportArea.height,   // y2()（开区间）
                    viewportArea.width, _hbar.BarWidth));
                _hbar.SetVisible(true);
            }
            else
            {
                _hbar.SetVisible(false);
            }

            if (_vbar.Attached)                     // scroll_helper.cpp:75-81
            {
                _vbar.SetBounds(new RectInt(viewportArea.x + viewportArea.width,   // x2()（开区间）
                    viewportArea.y, _vbar.BarWidth, viewportArea.height));
                _vbar.SetVisible(true);
            }
            else
            {
                _vbar.SetVisible(false);
            }

            return viewportArea;
        }

        /// <summary>把视口摆到当前视口区（<c>setBoundsQuietly</c> + <c>Viewport::onResize</c>）。</summary>
        void ApplyViewportArea()
        {
            _viewport.anchoredPosition = new Vector2(_viewportArea.x, -_viewportArea.y);
            _viewport.sizeDelta = new Vector2(_viewportArea.width, _viewportArea.height);
            LayoutAttachedWidget(ViewScroll);
            // 源在 onPaint 时才用「当时的视口尺寸」算拇指几何（scroll_bar.cpp:199-208），
            // UGUI 侧没有逐帧重绘，故视口区一变就主动重算一次，避免用到旧可视尺寸。
            _hbar.RefreshThumb();
            _vbar.RefreshThumb();
        }

        /// <summary>
        /// <c>View::updateAttachedWidgetBounds</c>（view.cpp:389-402）＝ <c>Viewport::onResize</c>
        /// （viewport.cpp:30-53）：被滚件左上角 = 视口左上 − 滚动偏移，
        /// 尺寸逐轴取 max(自身 sizeHint, 视口可视尺寸)。视口 border = 0，故不另减。
        /// </summary>
        void LayoutAttachedWidget(Vector2Int scrollPos)
        {
            if (_content == null)
                return;

            int w = Mathf.Max(_contentHint.x, _viewportArea.width);
            int h = Mathf.Max(_contentHint.y, _viewportArea.height);
            _content.anchoredPosition = new Vector2(-scrollPos.x, -scrollPos.y);
            _content.sizeDelta = new Vector2(w, h);
        }

        /// <summary>源 <c>parent.addChild(&amp;bar)</c>：UGUI 侧 = 显示 + 提到最后（压在被滚件上）。</summary>
        void AttachBar(AseScrollBar bar)
        {
            bar.SetAttached(true);
            bar.Rect.SetParent(_client, false);
            bar.Rect.SetAsLastSibling();
            bar.SetVisible(true);
        }

        /// <summary>源 <c>parent.removeChild(&amp;bar)</c>：UGUI 侧 = 隐藏（不参与绘制与命中）。</summary>
        static void DetachBar(AseScrollBar bar)
        {
            bar.SetAttached(false);
            bar.SetVisible(false);
        }

        /// <summary>轴序取成员：0 = w/x、1 = h/y（供 NEED_BAR 的 token 把戏直译）。</summary>
        static int Axis(Vector2Int v, int i)
        {
            return i == 0 ? v.x : v.y;
        }

        static int Axis(RectInt r, int i)
        {
            return i == 0 ? r.width : r.height;
        }
    }

    /// <summary>
    /// Aseprite <c>ScrollBar</c> 逐函数移植（源 <c>src/ui/scroll_bar.cpp</c> 全文 +
    /// theme.xml <c>scrollbar</c>/<c>scrollbar_thumb</c> 样式）。
    ///
    /// 【源里没有箭头钮】——<c>scroll_bar.cpp</c> 只有「轨道 + 拇指」两件：
    /// 点拇指＝抓取拖动（scroll_bar.cpp:94-96/110-112）、点拇指前/后＝翻页（± 可视尺寸/2，
    /// 99-105/114-122），<c>ScrollBar</c> 上没有任何箭头子件，theme 也没有 scrollbar 箭头件
    /// （<c>theme.xml</c> parts 段只有 scrollbar_bg/scrollbar_thumb + mini/transparent 两族）。
    ///
    /// 【几何】<c>m_barWidth = theme()->getScrollbarSize()</c>（scroll_bar.cpp:196）
    /// = theme.xml <c>&lt;dim id="scrollbar_size" value="12"/&gt;</c> → 条宽 12（×1 终局即画布像素）。
    /// 拇指长度/位置公式见 <see cref="GetScrollBarInfo"/>（scroll_bar.cpp:210-250）。
    ///
    /// 【细节件】轨道 <c>scrollbar_bg</c>、拇指 <c>scrollbar_thumb</c>，九宫格切片按 theme 声明
    /// （w1=5/w2=6/w3=5 → 原生 16×16），直接贴不乘色（滚动条样式只有常态一层背景：
    /// theme.xml:767-771；<c>state="mouse"</c> 换件只属于 mini/transparent scrollbar，773-787）。
    /// 拇指的 9 宫格在 12 宽容器里按比例压缩——这是源自身的行为（16 原生件画进 12 宽件），
    /// 不是本移植的取舍。
    /// </summary>
    public sealed class AseScrollBar : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        bool _horizontal;
        IAseScrollView _delegate;
        RectTransform _rect;
        RectTransform _thumbRect;
        Image _track;
        Image _thumb;

        int _barWidth = AseLayout.ScrollbarSize;
        int _pos;       // m_pos：滚动偏移
        int _size;      // m_size：可滚内容尺寸
        RectInt _bounds;
        bool _attached;

        // 源里是 static（用户同一时刻只能拖一根条）：m_wherepos/m_whereclick/m_dragging
        bool _pressed;
        bool _dragging;
        int _wherePos;
        int _whereClick;
        Camera _cam;

        /// <summary><c>getBarWidth()</c>（= 主题滚动条尺寸 12）。</summary>
        public int BarWidth { get { return _barWidth; } }

        /// <summary><c>getPos()</c>。</summary>
        public int Pos { get { return _pos; } }

        /// <summary>是否挂在 View 上（源里即 <c>parent()-&gt;hasChild(this)</c>）。</summary>
        public bool Attached { get { return _attached; } }

        /// <summary>部件矩形（UGUI 侧）。</summary>
        public RectTransform Rect { get { return _rect; } }

        internal static AseScrollBar Create(IAseScrollView owner, RectTransform parent, bool horizontal)
        {
            RectTransform rect = UiKit.CreateRect(horizontal ? "HScrollBar" : "VScrollBar", parent);
            UiKit.SetAnchored(rect, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            AseScrollBar bar = rect.gameObject.AddComponent<AseScrollBar>();
            bar.Build(owner, rect, horizontal);
            return bar;
        }

        void Build(IAseScrollView owner, RectTransform rect, bool horizontal)
        {
            _delegate = owner;
            _rect = rect;
            _horizontal = horizontal;
            _cam = FindCamera(rect);

            // 轨道：theme.xml <style id="scrollbar"><background part="scrollbar_bg"/>
            _track = rect.gameObject.AddComponent<Image>();
            AseUi.SetPart(_track, "scrollbar", AseStates.None);   // 引擎解析（scrollbar 样式无 mouse 层）
            _track.raycastTarget = true;   // 命中测试在 ScrollBar 自己身上（scroll_bar.cpp:66-135）

            // 拇指：theme.xml <style id="scrollbar_thumb"><background part="scrollbar_thumb"/>
            _thumbRect = UiKit.CreateRect("Thumb", rect);
            UiKit.SetAnchored(_thumbRect, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            _thumb = _thumbRect.gameObject.AddComponent<Image>();
            AseUi.SetPart(_thumb, "scrollbar_thumb", AseStates.None);
            _thumb.raycastTarget = false;
        }

        /// <summary>theme 滚动条件（<see cref="AseThemeLayers"/> 解析；scrollbar / scrollbar_thumb
        /// 样式只有常态 background 层，无状态件）。</summary>
        static Sprite ScrollPart(string styleId)
        {
            string part = AseThemeLayers.ResolveBackgroundPart(styleId, AseStates.None);
            return part != null ? PixelSkin.Ase(part) : null;
        }

        /// <summary>
        /// <c>kMouseEnterMessage</c>/<c>kMouseLeaveMessage → invalidate()</c>（scroll_bar.cpp:183-188）的对应：
        /// 源里只是标脏重绘。本端无脏标记，改为重解一次件——**对 <c>scrollbar</c> 样式无视觉差**
        /// （它没有 mouse 层；换件只属于 mini/transparent scrollbar，theme.xml:773-787）。
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            RefreshTheme();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RefreshTheme();
        }

        void RefreshTheme()
        {
            if (_track != null)
            {
                Sprite track = ScrollPart("scrollbar");
                if (track != null)
                    _track.sprite = track;
            }
            if (_thumb != null)
            {
                Sprite thumb = ScrollPart("scrollbar_thumb");
                if (thumb != null)
                    _thumb.sprite = thumb;
            }
        }

        /// <summary><c>setPos</c>（scroll_bar.cpp:40-46）。</summary>
        public void SetPos(int pos)
        {
            if (_pos == pos)
                return;
            _pos = pos;
            RefreshThumb();
        }

        /// <summary><c>setSize</c>（scroll_bar.cpp:48-54）。</summary>
        public void SetSize(int size)
        {
            if (_size == size)
                return;
            _size = size;
            RefreshThumb();
        }

        internal void SetAttached(bool on)
        {
            _attached = on;
        }

        internal void SetVisible(bool on)
        {
            gameObject.SetActive(on);
        }

        internal void SetBounds(RectInt bounds)
        {
            _bounds = bounds;
            _rect.anchoredPosition = new Vector2(bounds.x, -bounds.y);
            _rect.sizeDelta = new Vector2(bounds.width, bounds.height);
            RefreshThumb();
        }

        /// <summary><c>getScrollBarThemeInfo</c>（scroll_bar.cpp:56-59）。</summary>
        void GetScrollBarThemeInfo(out int pos, out int len)
        {
            GetScrollBarInfo(out pos, out len, out _, out _);
        }

        /// <summary>
        /// <c>getScrollBarInfo</c>（scroll_bar.cpp:210-250）：拇指长度 = 条长 × 可视/内容，
        /// 下限 = 主题滚动条尺寸×2（12×2=24），位置 = (条长−拇指长) × 偏移 / (内容−可视)。
        /// 滚动条样式没有 border 声明（theme.xml <c>scrollbar</c> 只有 background 层），
        /// 故 border().width()/height() = 0。
        /// </summary>
        void GetScrollBarInfo(out int posOut, out int lenOut, out int barSizeOut, out int viewportSizeOut)
        {
            int barSize, viewportSize;

            if (_horizontal)
            {
                barSize = _bounds.width;
                viewportSize = _delegate.VisibleSize.x;
            }
            else
            {
                barSize = _bounds.height;
                viewportSize = _delegate.VisibleSize.y;
            }

            int pos, len;
            if (_size <= viewportSize)              // scroll_bar.cpp:227-230
            {
                len = barSize;
                pos = 0;
            }
            else if (_size > 0)                     // scroll_bar.cpp:231-237
            {
                len = barSize * viewportSize / _size;
                // 源下限 min(getScrollbarSize()*2 - border_width, bar_size)：border 来自轨道件
                // 九宫切片（scrollbar_bg 5/6/5 → 竖条左右 5+5=10），非 0——漏减会把拇指下限抬高。
                Sprite trackSprite = _track != null ? _track.sprite : ScrollPart("scrollbar");
                int borderWidth = trackSprite == null ? 0 : (_horizontal
                    ? Mathf.RoundToInt(trackSprite.border.y + trackSprite.border.w)   // 横条：上+下
                    : Mathf.RoundToInt(trackSprite.border.x + trackSprite.border.z)); // 竖条：左+右
                len = Mathf.Clamp(len, Mathf.Min(AseLayout.ScrollbarSize * 2 - borderWidth, barSize), barSize);
                pos = (barSize - len) * _pos / (_size - viewportSize);
                pos = Mathf.Clamp(pos, 0, barSize - len);
            }
            else                                    // scroll_bar.cpp:238-240
            {
                len = 0;
                pos = 0;
            }

            posOut = pos;
            lenOut = len;
            barSizeOut = barSize;
            viewportSizeOut = viewportSize;
        }

        /// <summary>拇指重画（= <c>onPaint</c> 里 thumbBounds 的算法，scroll_bar.cpp:199-208）。</summary>
        internal void RefreshThumb()
        {
            if (_thumbRect == null)
                return;

            GetScrollBarThemeInfo(out int pos, out int len);
            if (_horizontal)
            {
                _thumbRect.sizeDelta = new Vector2(len, _bounds.height);
                _thumbRect.anchoredPosition = new Vector2(pos, 0f);
            }
            else
            {
                _thumbRect.sizeDelta = new Vector2(_bounds.width, len);
                _thumbRect.anchoredPosition = new Vector2(0f, -pos);
            }
        }

        // ------------------------------------------------------------------
        // 消息（scroll_bar.cpp:61-191）
        // ------------------------------------------------------------------

        /// <summary>
        /// <c>kMouseDownMessage</c>（scroll_bar.cpp:67-135）：拇指上 → 抓取（setSelected + captureMouse）；
        /// 拇指前 → 偏移 −可视/2；拇指后 → +可视/2（翻页，不抓取）。
        /// 轴的命中区间（<c>MOUSE_IN</c>）在条内等价于「轴向坐标落在 [pos, pos+len-1] / &lt;pos / ≥pos+len」。
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            GetScrollBarThemeInfo(out int pos, out int len);

            _wherePos = pos;                                        // scroll_bar.cpp:76
            _whereClick = AxisAt(eventData.position);                // scroll_bar.cpp:77
            _dragging = false;                                      // scroll_bar.cpp:78

            bool pageStep = false;
            Vector2Int scroll = _delegate.ViewScroll;

            if (_whereClick >= pos && _whereClick <= pos + len - 1)
            {
                // 源：在拇指里 → 只 captureMouse()，不翻页
            }
            else if (_whereClick < pos)                             // scroll_bar.cpp:98-102 / 114-117
            {
                if (_horizontal)
                    scroll.x -= _delegate.VisibleSize.x / 2;
                else
                    scroll.y -= _delegate.VisibleSize.y / 2;
                pageStep = true;
            }
            else if (_whereClick >= pos + len)                      // scroll_bar.cpp:103-106 / 119-122
            {
                if (_horizontal)
                    scroll.x += _delegate.VisibleSize.x / 2;
                else
                    scroll.y += _delegate.VisibleSize.y / 2;
                pageStep = true;
            }

            if (pageStep)                                           // scroll_bar.cpp:125-128
            {
                _delegate.SetViewScroll(scroll);
                return;
            }

            _pressed = true;                                        // setSelected(true) + captureMouse()
        }

        /// <summary><c>kMouseUpMessage</c>（scroll_bar.cpp:177-181）：releaseMouse。</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        /// <summary>拖动三件套 = **阻断冒泡壳**：UGUI 选拖动目标时沿父链找最近的
        /// IDragHandler，滚动条若不实现，目标是窗根的 WindowDragger——「拖滚动条变成
        /// 拖整个窗」。实际拖动仍在 <see cref="Update"/> 轮询里（源 capture 后逐消息的
        /// 对应，且点轨道=翻页不抓取的区分也靠按下语义，不走 UGUI 拖动）。</summary>
        public void OnBeginDrag(PointerEventData eventData) { }
        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) { }

        void Release()
        {
            _pressed = false;
            _dragging = false;
            // setSelected(false)：theme scrollbar 无 selected 层，无视觉可复位
        }

        /// <summary>
        /// <c>kMouseMoveMessage</c>（scroll_bar.cpp:137-175）带 capture 的拖动。
        /// UGUI 的 OnDrag 要过 pixelDragThreshold 才首发（首帧就跳一格），
        /// 源明确要求「没移动就不启动拖动、避免 1px 跳」（139-149），
        /// 故这里用按下期间的逐帧轮询代替 OnDrag——与 capture 后的逐消息等价。
        /// </summary>
        void Update()
        {
            if (!_pressed)
                return;
            if (!Input.GetMouseButton(0))       // 鼠标在条外松开也复位（同源里 capture 丢失）
            {
                Release();
                return;
            }

            int m = AxisAt(Input.mousePosition);

            if (!_dragging)                     // scroll_bar.cpp:143-149
            {
                if (m == _whereClick)
                    return;
                _dragging = true;
            }

            GetScrollBarInfo(out int pos, out int len, out int barSize, out int viewportSize);
            if (barSize <= len)                 // scroll_bar.cpp:155
                return;

            pos = Mathf.Clamp(_wherePos + m - _whereClick, 0, barSize - len);
            Vector2Int scroll = _delegate.ViewScroll;
            if (_horizontal)
                scroll.x = (_size - viewportSize) * pos / (barSize - len);
            else
                scroll.y = (_size - viewportSize) * pos / (barSize - len);

            _delegate.SetViewScroll(scroll);
        }

        /// <summary>指针在条内的轴向坐标（相对条左上角，整数像素）。</summary>
        int AxisAt(Vector2 screenPoint)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPoint, _cam, out local);
            float v = _horizontal ? local.x : -local.y;
            return Mathf.FloorToInt(v + 0.5f);
        }

        static Camera FindCamera(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;
            Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }
    }

    /// <summary>
    /// 滚轮转发件（<c>listbox.cpp:258-259</c> <c>kMouseWheelMessage → View::scrollByMessage(this, msg)</c>）：
    /// 挂在**被滚件**上（源的收件件就是它），指针在行上滚轮时事件沿父链上浮到这里。
    /// </summary>
    internal sealed class AseViewWheel : MonoBehaviour, IScrollHandler
    {
        AseView _view;

        public void Bind(AseView view)
        {
            _view = view;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_view == null)
                return;
            _view.ScrollByMessage(eventData.scrollDelta.x, eventData.scrollDelta.y);
            eventData.Use();
        }
    }
}
