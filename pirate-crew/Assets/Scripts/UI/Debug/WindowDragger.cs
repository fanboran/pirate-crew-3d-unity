using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 窗体拖动带 + 焦点置顶（Aseprite 窗口感：按住标题带拖动整个窗体；**任意点按下即把
    /// 窗体提到最上**——最后点击的窗永远浮顶，桌面窗口管理器语义）。
    ///
    /// 挂法走 <see cref="Attach"/>：在窗体顶部铺一块透明命中板（标题带高 =
    /// <see cref="DebugWindowKit.TitleBand"/> = 件 "window" 顶切片 h1 = 15，右侧避开窗控钮区），
    /// 板上收 OnDrag → 平移窗体 anchoredPosition。拖动**只保标题带可抓**（窗口可推到只剩一条带
    /// 在屏内，其余随你摆——不做「至少留 3/4 在屏内」这种自造限制）。
    /// 命中板不与窗控钮/标题文字抢事件：文字件 raycastTarget 本就为 false，
    /// 窗控钮在命中板右界之外。PointerDown 沿命中链冒泡——窗内任何子件被按下都会置顶。
    ///
    /// 【以库为源】可抓下限 = 源的 <c>limitPosition</c>（window.cpp:782-810）：
    /// <c>titlebarH = childrenBounds().y - bounds().y</c>（= border-top = 17），
    /// 底界 <c>parent.y2() - titlebarH</c>。<b>未对齐项（登记）</b>：源左/右界为
    /// <c>border().right()</c>（= 6）与 <c>rect.y >= 0</c>，本类现为手摆 24 / −6——
    /// 属拖动带几何，本轮按协调者口径只对齐标题带高一处，未改。
    /// </summary>
    public sealed class WindowDragger : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IPointerDownHandler
    {
        /// <summary>窗控钮让位区宽（? 钮 9 + × 钮 9 + 右边距 3 + 缝）。</summary>
        const float RightReserve = 26f;

        /// <summary>标题带至少留在屏内的可抓高度 = 源的 <c>titlebarH</c>
        /// （window.cpp:793 <c>childrenBounds().y - bounds().y</c>）= window_with_title
        /// border-top（theme.xml:472）= <see cref="DebugWindowKit.ContentTop"/> = 17
        /// （带 15 整条 + 带下 2 格缝）。</summary>
        const float GrabStrip = DebugWindowKit.ContentTop;

        RectTransform _window;
        RectTransform _canvas;
        Vector2 _dragStart;
        Vector2 _startTopLeft;

        /// <summary>给窗体挂拖动带（幂等：已挂只补引用）。</summary>
        public static WindowDragger Attach(RectTransform window)
        {
            var dragger = window.GetComponent<WindowDragger>();
            if (dragger == null)
            {
                dragger = window.gameObject.AddComponent<WindowDragger>();
                dragger.BuildZone(window);
            }
            dragger._window = window;
            Canvas canvas = window.GetComponentInParent<Canvas>();
            if (canvas != null)
                dragger._canvas = canvas.transform as RectTransform;
            return dragger;
        }

        /// <summary>把窗体提到画布层最上（沿父链走到画布直属根——面板提的是调试树根，
        /// 主菜单提的是它自己：桌面语义=点击/新开的窗永远在所有窗之上）。</summary>
        public static void RaiseToCanvasTop(RectTransform window)
        {
            Transform t = window;
            while (t.parent != null && t.parent.GetComponent<Canvas>() == null)
                t = t.parent;
            t.SetAsLastSibling();
        }

        /// <summary>按下即置顶（含窗内任意子件被点——事件沿命中链冒泡到窗根）。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_window != null)
                RaiseToCanvasTop(_window);
        }

        void BuildZone(RectTransform window)
        {
            var zone = new GameObject("DragZone", typeof(RectTransform));
            RectTransform rect = zone.GetComponent<RectTransform>();
            rect.SetParent(window, false);
            // 覆盖标题带：横向**拉伸锚**（0,1)-(1,1）+ inset——右让位窗控钮，下探到带底。
            // （第一版把拉伸锚的 inset 写法用在点锚上，宽度成了 −24：命中板不存在、
            // 全部窗体都拖不动——实机走查抓出。）
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(0f, -DebugWindowKit.TitleBand);
            rect.offsetMax = new Vector2(-RightReserve, 0f);
            // 主菜单 MenuWindow 挂着 VerticalLayoutGroup——命中板若参与内容流会被排到
            // 按钮列末尾（窗外），主菜单拖动整条失效；必须豁免布局（同标题字/窗控钮口径）。
            UiLayout.Ignore(zone);
            var hit = zone.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);   // 透明命中板：只吃指针不显形
            hit.raycastTarget = true;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_window == null || _canvas == null)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas, eventData.position, eventData.pressEventCamera, out _dragStart);
            _startTopLeft = TopLeftInCanvas();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_window == null || _canvas == null)
                return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas, eventData.position, eventData.pressEventCamera, out Vector2 now))
                return;

            // 拖动按「窗左上角在画布上的位置」算，再换算回窗体自己的锚/枢轴系——
            // 主菜单是中心锚窗（0.5,0.5），调试窗是左上锚窗，锚系各自不同（实机走查
            // 「拖到画布中线就卡住」即按左上锚语义夹取中心锚窗的坐标所致）。
            Vector2 delta = now - _dragStart;                          // 画布中心系，y 向上
            Vector2 topLeft = _startTopLeft + new Vector2(delta.x, -delta.y);   // 转左上原点 y 向下
            float w = _window.rect.width;
            float h = _window.rect.height;
            float cw = _canvas.rect.width;
            float ch = _canvas.rect.height;
            // 只保标题带可抓（源 limitPosition，window.cpp:782-810）：底界 = 画布底 −
            // 标题带高（GrabStrip = border-top = 17）；左右/顶界仍为手摆值（见类头登记）。
            topLeft.x = Mathf.Clamp(topLeft.x, 24f - w, cw - 24f);
            topLeft.y = Mathf.Clamp(topLeft.y, -6f, ch - GrabStrip);
            _window.anchoredPosition = TopLeftToAnchored(topLeft, w, h);
        }

        /// <summary>窗左上角在画布局部的位置（左上原点、y 向下）。</summary>
        Vector2 TopLeftInCanvas()
        {
            Vector3[] corners = new Vector3[4];
            _window.GetWorldCorners(corners);   // 0 左下 1 左上 2 右上 3 右下
            Vector2 local = _canvas.InverseTransformPoint(corners[1]);
            return new Vector2(local.x + _canvas.rect.width * 0.5f,
                _canvas.rect.height * 0.5f - local.y);
        }

        /// <summary>左上角目标位 → 本窗锚/枢轴系的 anchoredPosition（任意点锚通用）。</summary>
        Vector2 TopLeftToAnchored(Vector2 topLeft, float w, float h)
        {
            Vector2 pivotOffset = new Vector2(_window.pivot.x * w, (1f - _window.pivot.y) * h);
            Vector2 pivotTopLeft = topLeft + pivotOffset;              // 枢轴的左上系位置
            float cw = _canvas.rect.width;
            float ch = _canvas.rect.height;
            // 左上系(y下) → 中心系(y上)
            Vector2 pivotCenter = new Vector2(pivotTopLeft.x - cw * 0.5f, ch * 0.5f - pivotTopLeft.y);
            Vector2 anchor = _window.anchorMin;                        // 点锚（min==max）
            Vector2 anchorLocal = new Vector2(
                Mathf.Lerp(-cw * 0.5f, cw * 0.5f, anchor.x),
                Mathf.Lerp(-ch * 0.5f, ch * 0.5f, anchor.y));
            return pivotCenter - anchorLocal;
        }
    }
}
