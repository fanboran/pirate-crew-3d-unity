using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>复选/单选行的鼠标态（唯一的一层额外底色）：theme <c>check_box</c> /
    /// <c>radio_button</c> 样式 <c>&lt;background color="check_hot_face" state="mouse"/&gt;</c>
    /// （#575B61，radio 同名色）。底色经 <see cref="AseThemeLayers"/> 解析——常态
    /// <b>没有</b> background 层命中（→ 全透明），悬停命中 hot 层。禁用 #2C2C30 / 焦点
    /// #41444A + check_focus 环虽已可解析，但调试面板没有禁用/键盘焦点两种态，未接线。</summary>
    public sealed class AseCheckBoxFace : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image _face;
        string _styleId;

        public void Bind(Image face, bool radio)
        {
            _face = face;
            _styleId = radio ? "radio_button" : "check_box";
            Apply(AseStates.None);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Apply(AseStates.Mouse);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Apply(AseStates.None);
        }

        void Apply(AseStates states)
        {
            if (_face == null)
                return;
            Color32? c = AseThemeLayers.ResolveBackgroundColor(_styleId, states);
            _face.color = c.HasValue ? (Color)c.Value : new Color(0f, 0f, 0f, 0f);
        }
    }
}
