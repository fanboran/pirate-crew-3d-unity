using System.Collections;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>悬停 0.3s 弹 theme tooltip（蓝底 #4069C2 + #C0C0C0 字），移开即收。</summary>
    sealed class HoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        string _text;
        Coroutine _pending;
        GameObject _tip;

        public void Bind(string text) => _text = text;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pending = StartCoroutine(ShowLater());
        }

        public void OnPointerExit(PointerEventData eventData) => Hide();

        // 悬停中宿主窗被 × 关掉（SetActive(false)）不会派发 PointerExit——
        // 不在失活时收气泡，蓝底 tooltip 会孤儿一样常驻画布顶层。
        void OnDisable() => Hide();

        void Hide()
        {
            if (_pending != null)
            {
                StopCoroutine(_pending);
                _pending = null;
            }
            if (_tip != null)
            {
                Destroy(_tip);
                _tip = null;
            }
        }

        IEnumerator ShowLater()
        {
            yield return new WaitForSeconds(0.3f);   // tooltips.cpp:29 kDefaultTooltipDelayMsecs=300

            RectTransform canvas = AseUi.OverlayOf(transform);
            RectTransform tip = UiKit.CreateRect("AseTooltip", canvas);
            tip.anchorMin = tip.anchorMax = tip.pivot = new Vector2(0f, 1f);
            var bg = tip.gameObject.AddComponent<Image>();
            AseUi.SetRawPart(bg, "tooltip");   // theme tooltip（九宫 5/6/5×5/5/6）
            bg.raycastTarget = false;

            TextMeshProUGUI text = UiKit.CreateText("Text", tip, _text, UiSkin.Font.Tiny,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, DebugWindowKit.HandFont);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            // 件是九宫（5/6/5 × 5/5/6）——文字按内容区 inset 摆，不压边框
            float textW = Mathf.Ceil(text.preferredWidth);
            UiKit.SetAnchored(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(textW, 12f),
                Vector2.zero);
            tip.sizeDelta = new Vector2(textW + 12f, 22f);

            // 气泡水平居中于目标钮、底边高于钮顶 2px：(0,1) 点锚 = 一行换算（EdgesOf）
            // + 一行落位（PlaceByEdges）。tip 已量宽——左缘 = 钮中心 − 半宽（等价旧
            // 底边中心锚语义，漏减半宽会整体右偏半气泡）。
            RectTransform target = (RectTransform)transform;
            Vector2 edges = AseUi.EdgesOf(target, canvas);
            AseUi.PlaceByEdges(tip,
                edges.x + target.rect.width * 0.5f - tip.sizeDelta.x * 0.5f,
                edges.y - 2f);
            tip.SetAsLastSibling();
            _tip = tip.gameObject;
        }
    }
}
