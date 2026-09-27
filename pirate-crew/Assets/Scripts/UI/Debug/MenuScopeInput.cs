using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static PirateCrew.UI.DebugUi.AseMenuKit;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// MenuBox::onProcessMessage 的事件面：挂在**本层命中板**上（栏根 / 弹窗框），
    /// 行由指针坐标拾取——等价源码 <c>manager()->pickFromScreenPos()</c>，行不各自吃事件。
    /// </summary>
    sealed class MenuScopeInput : MonoBehaviour,
        IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public AseMenuSession Session;
        public MenuScope Scope;

        public void OnPointerEnter(PointerEventData e) => Session.ScopePointerMove(Scope, e);
        public void OnPointerMove(PointerEventData e) => Session.ScopePointerMove(Scope, e);
        public void OnPointerExit(PointerEventData e) => Session.ScopePointerExit(Scope);
        public void OnPointerDown(PointerEventData e) => Session.ScopePointerDown(Scope, e);
        public void OnPointerUp(PointerEventData e) => Session.ScopePointerUp(Scope);
    }
}
