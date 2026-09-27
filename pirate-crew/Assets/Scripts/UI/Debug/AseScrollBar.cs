using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
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
}
