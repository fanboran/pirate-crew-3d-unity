using UnityEngine;
using UnityEngine.EventSystems;

namespace PirateCrew.UI
{
    /// <summary>
    /// 列表行的键盘焦点框：行被 EventSystem 选中（Tab/方向键）时点亮 1px 蓝框，
    /// 失焦/禁用即收。行本体仍是 list_item 的三态纯色面（常态/选中金/禁用），
    /// 焦点框是独立的键盘信号，不改行面语义。
    /// </summary>
    public sealed class UiRowFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        GameObject _frame;

        /// <summary>绑定焦点框根（默认隐藏）。</summary>
        public void Bind(GameObject frame)
        {
            _frame = frame;
            if (_frame != null)
                _frame.SetActive(false);
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (_frame != null)
                _frame.SetActive(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (_frame != null)
                _frame.SetActive(false);
        }

        void OnDisable()
        {
            if (_frame != null)
                _frame.SetActive(false);
        }
    }
}
