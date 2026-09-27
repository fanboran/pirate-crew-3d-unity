using System;
using System.Collections.Generic;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>组合框箭头图标的按压变体（theme <c>combobox_button</c> 的
    /// <c>&lt;icon part="combobox_arrow_down_selected" state="selected"/&gt;</c>；
    /// 源里按钮的 selected 态就是 capture=按下）。与 <see cref="Button"/> 同 GameObject
    /// —— UGUI 的 <c>ExecuteEvents</c> 会广播给该对象上**所有**同接口组件，互不抢事件。</summary>
    internal sealed class AseComboBoxArrow : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        Image _arrow;

        public void Bind(Image arrow)
        {
            _arrow = arrow;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Set(PixelState.Pressed);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Set(PixelState.Normal);
        }

        void Set(PixelState state)
        {
            if (_arrow == null)
                return;
            Sprite sprite = AseComboBox.ArrowPart(AseComboBox.ArrowFlags(state));
            if (sprite != null)
                _arrow.sprite = sprite;
        }
    }
}
