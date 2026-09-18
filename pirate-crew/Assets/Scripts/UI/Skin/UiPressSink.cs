using UnityEngine;
using UnityEngine.EventSystems;

namespace PirateCrew.UI
{
    /// <summary>
    /// 按压下沉（卡通按钮手感）：按下时控件向下沉 <see cref="UiSkin.PressSinkPixels"/>，
    /// 松开弹回。挂在 Button 所在节点，与 ColorBlock 乘色四态叠加。
    /// unscaled 时间语义不适用（状态驱动而非时间驱动）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPressSink : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        RectTransform _rect;
        Vector2 _basePosition;
        bool _initialized;

        void EnsureInitialized()
        {
            if (_initialized)
                return;
            _rect = (RectTransform)transform;
            _basePosition = _rect.anchoredPosition;
            _initialized = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            EnsureInitialized();
            _rect.anchoredPosition = _basePosition + Vector2.down * UiSkin.PressSinkPixels;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_initialized)
                return;
            _rect.anchoredPosition = _basePosition;
        }

        void OnDisable()
        {
            // 按住时面板被隐藏等边界：复位，避免带着 2px 偏移回来。
            if (_initialized)
                _rect.anchoredPosition = _basePosition;
        }
    }
}
