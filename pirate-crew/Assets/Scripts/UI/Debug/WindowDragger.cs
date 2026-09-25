using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 窗体拖动带（Aseprite 窗口感：按住标题带拖动整个窗体）。
    ///
    /// 挂法走 <see cref="Attach"/>：在窗体顶部铺一块透明命中板（标题带高 =
    /// <see cref="PixelSkin.WindowTitleBand"/> = 15，右侧避开窗控钮区），板上收
    /// OnDrag → 平移窗体 anchoredPosition，并夹在画布内（拖出界就夹回）。
    /// 命中板不与窗控钮/标题文字抢事件：文字件 raycastTarget 本就为 false，
    /// 窗控钮在命中板右界之外。
    /// </summary>
    public sealed class WindowDragger : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        /// <summary>窗控钮让位区宽（×钮 9 + 右边距 3 + 缓冲）。</summary>
        const float RightReserve = 24f;

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

        void BuildZone(RectTransform window)
        {
            var zone = new GameObject("DragZone", typeof(RectTransform));
            RectTransform rect = zone.GetComponent<RectTransform>();
            rect.SetParent(window, false);
            // 覆盖标题带：锚 (0,1)-(0,1) 起，宽 = 窗宽 − 右让位，高 = 标题带
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(-RightReserve, PixelSkin.WindowTitleBand);
            rect.anchoredPosition = Vector2.zero;
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

            // 窗体锚/枢轴均为 (0,1)（左上角），anchoredPosition = (x, -y)
            Vector2 pos = _windowStart + now - _dragStart;
            float w = _window.rect.width;
            float h = _window.rect.height;
            Vector2 canvasSize = _canvas.rect.size;
            float x = Mathf.Clamp(pos.x, 4f - w * 0.75f, canvasSize.x - w * 0.25f);   // 至少留 1/4 宽在屏内
            float y = Mathf.Clamp(-pos.y, 4f, canvasSize.y - PixelSkin.WindowTitleBand); // 标题带不出下界
            _window.anchoredPosition = new Vector2(x, -y);
        }
    }
}
