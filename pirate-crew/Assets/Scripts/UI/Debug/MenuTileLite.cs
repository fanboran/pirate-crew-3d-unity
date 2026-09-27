using System.Collections;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>轻量点击件（页签/列表行用——Image+文字的组合，无 Selectable 状态机）。</summary>
    sealed class MenuTileLite : MonoBehaviour, IPointerClickHandler
    {
        System.Action _click;

        public void Bind(System.Action click) => _click = click;

        public void OnPointerClick(PointerEventData eventData) => _click?.Invoke();
    }
}
