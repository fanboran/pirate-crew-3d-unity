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
    /// <see cref="PixelSkin.WindowTitleBand"/> = 15，右侧避开窗控钮区），板上收
    /// OnDrag → 平移窗体 anchoredPosition。拖动**只保标题带可抓**（窗口可推到只剩一条带
    /// 在屏内，其余随你摆——不做「至少留 3/4 在屏内」这种自造限制）。
    /// 命中板不与窗控钮/标题文字抢事件：文字件 raycastTarget 本就为 false，
    /// 窗控钮在命中板右界之外。PointerDown 沿命中链冒泡——窗内任何子件被按下都会置顶。
    /// </summary>
    public sealed class WindowDragger : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IPointerDownHandler
    {
        /// <summary>窗控钮让位区宽（? 钮 9 + × 钮 9 + 右边距 3 + 缝）。</summary>
        const float RightReserve = 26f;

        /// <summary>标题带至少留在屏内的可抓高度（拖出界下限的唯一约束）。</summary>
        const float GrabStrip = 16f;

        RectTransform _window;
        RectTransform _canvas;
        Vector2 _dragStart;
        Vector2 _windowStart;

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

        /// <summary>按下即置顶（含窗内任意子件被点——事件沿命中链冒泡到窗根）。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_window != null)
                _window.SetAsLastSibling();
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
            rect.offsetMin = new Vector2(0f, -PixelSkin.WindowTitleBand);
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
            _windowStart = _window.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_window == null || _canvas == null)
                return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas, eventData.position, eventData.pressEventCamera, out Vector2 now))
                return;

            // 窗体锚/枢轴均为 (0,1)（左上角），anchoredPosition = (x, -y)。
            // 夹取只保「标题带还在屏内可抓」（Aseprite 桌面语义：窗口可推到大半出屏）：
            // x ∈ [24−w, canvasW−24]（左右各留一条可抓带），y ∈ [−4, canvasH−带高]。
            Vector2 pos = _windowStart + now - _dragStart;
            float w = _window.rect.width;
            float h = _window.rect.height;
            Vector2 canvasSize = _canvas.rect.size;
            float x = Mathf.Clamp(pos.x, 24f - w, canvasSize.x - 24f);
            float y = Mathf.Clamp(-pos.y, -4f, canvasSize.y - Mathf.Min(h, GrabStrip));
            _window.anchoredPosition = new Vector2(x, -y);
        }
    }
}
