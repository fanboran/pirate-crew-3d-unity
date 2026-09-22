using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 按压反馈（像素皮版）：按下时控件**平移** <see cref="PixelSkin.PressOffset"/>（右下 1px），
    /// 松开弹回；同时管禁用态的透明表达。
    ///
    /// 【为什么是平移而不是缩放】像素件的明暗色阶烘死在贴图里，缩放会把 3px 的像素带插值糊掉，
    /// 位移 1u 才是这套语言的手感；贴图里也不烘位移（烘了九宫格切片会错位）。
    /// 【禁用态为什么在这里】UGUI 用 SpriteSwap 时 ColorBlock 被忽略，禁用无法靠乘色表达；
    /// 又**不能烘一张黑图**（会把色阶压死）。故整钮走 CanvasGroup alpha≈0.55——
    /// 本组件每帧只比对一次 <c>interactable</c>，翻转时才写 alpha（无翻转零开销）。
    /// unscaled 时间语义不适用（状态驱动而非时间驱动）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPressSink : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>禁用态整钮透明度（与 UGUI 惯例一致；不烘黑图的原因见类注释）。</summary>
        const float DisabledAlpha = 0.55f;

        RectTransform _rect;
        Vector2 _basePosition;
        bool _initialized;

        Selectable _selectable;
        CanvasGroup _group;
        bool _interactable = true;

        void EnsureInitialized()
        {
            if (_initialized)
                return;
            _rect = (RectTransform)transform;
            _basePosition = _rect.anchoredPosition;
            _selectable = GetComponent<Selectable>();
            _initialized = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            EnsureInitialized();
            _rect.anchoredPosition = _basePosition + PixelSkin.PressOffset;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_initialized)
                return;
            _rect.anchoredPosition = _basePosition;
        }

        void Update()
        {
            // 禁用态：interactable 翻转时才写 alpha（CanvasGroup 惰性创建，避免给全部按钮加组件）。
            if (_selectable == null)
                _selectable = GetComponent<Selectable>();
            if (_selectable == null || _selectable.interactable == _interactable)
                return;

            _interactable = _selectable.interactable;
            if (_group == null)
                _group = GetComponent<CanvasGroup>();
            if (_group == null && !_interactable)
                _group = gameObject.AddComponent<CanvasGroup>();
            if (_group != null)
                _group.alpha = _interactable ? 1f : DisabledAlpha;
        }

        void OnDisable()
        {
            // 按住时面板被隐藏等边界：复位，避免带着偏移回来。
            if (_initialized)
                _rect.anchoredPosition = _basePosition;
        }
    }
}
