using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
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
