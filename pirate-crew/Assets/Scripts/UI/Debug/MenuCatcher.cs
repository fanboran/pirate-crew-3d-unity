using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>外点捕获板（源码 startFilteringMouseDown 拦到的 kMouseDownMessage：关全部且不穿透）。</summary>
    sealed class MenuCatcher : MonoBehaviour, IPointerDownHandler
    {
        public AseMenuSession Session;

        public void OnPointerDown(PointerEventData e) => Session.CloseMenus();
    }
}
