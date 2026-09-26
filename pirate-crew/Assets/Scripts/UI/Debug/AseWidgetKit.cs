using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 调试面板公共小件工厂（theme 语义件的一次调用版本）——「一个小界面几百行」的
    /// 架构病收口：凹槽输入框 / 复选行 / 两件套组合框 / 悬停换面，全在此一处实现，
    /// 各面板（实摆画廊、New Sprite 对话框…）只声明布局与行为。
    /// 件与色的出处同各调用点注释：theme.xml 对应 style/parts 条目。
    /// </summary>
    public static class AseWidgetKit
    {
        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------

        /// <summary>凹槽数值/文本输入（theme textedit：sunken 件 + TMP 输入 + 可选框内右缘后缀）。
        /// 返回 InputField（text 由调用方读写）。锚定父件左上 (x, -y)，尺寸 w×12。</summary>
        public static TMP_InputField SunkenEntry(RectTransform parent, string name, float x, float y,
            float w, string text, TMP_InputField.ContentType contentType
                = TMP_InputField.ContentType.IntegerNumber, string suffix = null)
        {
            RectTransform entry = UiKit.CreateRect(name, parent);
            entry.anchorMin = entry.anchorMax = entry.pivot = new Vector2(0f, 1f);
            entry.anchoredPosition = new Vector2(x, -y);
            entry.sizeDelta = new Vector2(w, 12f);
            var sunken = entry.gameObject.AddComponent<Image>();
            sunken.sprite = PixelSkin.Sunken(false);
            sunken.type = Image.Type.Sliced;
            sunken.color = Color.white;
            sunken.raycastTarget = true;

            RectTransform textArea = UiKit.CreateRect("Text", entry);
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.offsetMin = new Vector2(4f, 1f);
            textArea.offsetMax = new Vector2(string.IsNullOrEmpty(suffix) ? -4f : -16f, -1f);
            TextMeshProUGUI input = textArea.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UiKit.ResolvePixelFont(UiSkin.Font.Tiny, DebugWindowKit.HandFont);
            if (font != null)
                input.font = font;
            input.fontSize = UiSkin.Font.Tiny;
            input.color = PixelSkin.Theme.Text;

            var field = entry.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = input;
            field.textViewport = textArea;
            field.text = text;
            field.contentType = contentType;
            field.caretColor = PixelSkin.Theme.Text;
            field.selectionColor = new Color32(0x40, 0x69, 0xC2, 0x60);   // 蓝选区 60% 透明

            if (!string.IsNullOrEmpty(suffix))
            {
                // 后缀锚右缘（entry 拉伸时贴着右边框——expr 的 suffix 语义）
                TextMeshProUGUI suffixLabel = DebugWindowKit.PlaceLabel(entry, suffix,
                    UiSkin.Font.Tiny, PixelSkin.Theme.StatusText, w - 13f, 2f, 10f);
                RectTransform suffixRect = suffixLabel.rectTransform;
                suffixRect.anchorMin = suffixRect.anchorMax = suffixRect.pivot = new Vector2(1f, 1f);
                suffixRect.anchoredPosition = new Vector2(-3f, -2f);
            }
            return field;
        }

        // ------------------------------------------------------------------
        // 复选
        // ------------------------------------------------------------------

        /// <summary>复选行（theme check_box：常态透明面 / 悬停亮面 #575B61 / 图标 8×8 @x2 /
        /// 文字 @x14）。返回行根（可再查 Icon 换图标）。图标名前缀 "check"/"radio" 皆可。</summary>
        public static Button CheckRow(RectTransform parent, string label, float x, float y,
            bool initial, System.Action<bool> onChanged, string kind = "check")
        {
            var rowGo = new GameObject(kind + "_" + label, typeof(RectTransform));
            RectTransform rect = rowGo.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(160f, 16f);
            rect.anchoredPosition = new Vector2(x, -y);

            var face = rowGo.AddComponent<Image>();
            face.color = new Color(0f, 0f, 0f, 0f);
            face.raycastTarget = true;
            rowGo.AddComponent<HoverFace>().Bind(face, new Color32(0x57, 0x5B, 0x61, 0xFF));

            var icon = UiKit.CreateRect("Icon", rect);
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 1f);
            icon.sizeDelta = new Vector2(8f, 8f);
            icon.anchoredPosition = new Vector2(2f, -4f);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;

            DebugWindowKit.PlaceLabel(rect, label, UiSkin.Font.Tiny, PixelSkin.Theme.Text, 14f, 0f, 140f);

            var button = rowGo.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            bool on = initial;
            ApplyCheckIcon(iconImage, kind, on);
            button.onClick.AddListener(() =>
            {
                on = !on;
                ApplyCheckIcon(iconImage, kind, on);
                onChanged?.Invoke(on);
            });
            return button;
        }

        static void ApplyCheckIcon(Image icon, string kind, bool on)
        {
            icon.sprite = PixelSkin.Ase(kind == "radio"
                ? (on ? "radio_selected" : "radio_normal")
                : (on ? "check_selected" : "check_normal"));
        }

        // ------------------------------------------------------------------
        // 组合框
        // ------------------------------------------------------------------

        /// <summary>两件套组合框（theme combobox：sunken 词条 + 右缘 16×16 mini_button 箭头钮，
        /// 点击弹 <see cref="AseMenuKit"/> 下拉）。词条宽 w，选项文字即显示文字。
        /// <paramref name="popupOverlay"/> = 弹层宿主（必须是调试根 overlay——弹层要盖过
        /// **所有**窗；缺省取 parent.parent，仅当组合框直接挂在窗根下时成立）。</summary>
        public static TextMeshProUGUI ComboBox(RectTransform parent, string name, float x, float y,
            float w, string[] options, int initial, System.Action<int> onPick = null,
            Transform popupOverlay = null)
        {
            RectTransform entry = UiKit.CreateRect(name, parent);
            entry.anchorMin = entry.anchorMax = entry.pivot = new Vector2(0f, 1f);
            entry.anchoredPosition = new Vector2(x, -y);
            entry.sizeDelta = new Vector2(w, 12f);
            var sunken = entry.gameObject.AddComponent<Image>();
            sunken.sprite = PixelSkin.Sunken(false);
            sunken.type = Image.Type.Sliced;
            sunken.color = Color.white;
            sunken.raycastTarget = true;

            TextMeshProUGUI value = UiKit.CreateText("Value", entry, options[initial],
                UiSkin.Font.Tiny, TextAlignmentOptions.Left, PixelSkin.Theme.Text, DebugWindowKit.HandFont);
            value.enableWordWrapping = false;
            value.raycastTarget = false;
            RectTransform valueRect = value.rectTransform;
            valueRect.anchorMin = valueRect.anchorMax = new Vector2(0.5f, 0.5f);
            valueRect.pivot = new Vector2(0.5f, 0.5f);
            valueRect.offsetMin = new Vector2(4f, 0f);
            valueRect.offsetMax = new Vector2(-16f, 0f);

            RectTransform buttonRect = UiKit.CreateRect("ComboButton", entry);
            buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = new Vector2(16f, 16f);
            var buttonFace = buttonRect.gameObject.AddComponent<Image>();
            buttonFace.sprite = PixelSkin.Ase("buttonset_item_normal");
            buttonFace.type = Image.Type.Sliced;
            buttonFace.pixelsPerUnitMultiplier = 1f;
            buttonFace.color = Color.white;
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = PixelSkin.Ase("buttonset_item_hot"),
                pressedSprite = PixelSkin.Ase("buttonset_item_pushed"),
                disabledSprite = PixelSkin.Ase("buttonset_item_normal"),
            };

            RectTransform arrow = UiKit.CreateRect("Arrow", buttonRect);
            arrow.anchorMin = arrow.anchorMax = arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(9f, 8f);
            var arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = PixelSkin.Ase("combobox_arrow_down");
            arrowImage.raycastTarget = false;

            var items = new AseMenuKit.Item[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int captured = i;
                items[i] = AseMenuKit.Item_(options[i], check: i == initial, action: () =>
                {
                    value.text = options[captured];
                    onPick?.Invoke(captured);
                });
            }
            button.onClick.AddListener(() => AseMenuKit.OpenPopup(
                popupOverlay != null ? popupOverlay : parent.parent, entry, items));
            return value;
        }
    }

    /// <summary>悬停换底色/字色（check 系亮面 #575B61 / 列表变暗 #2C2C30+字灰——两向都支持）。
    /// 公共件：画廊列表、复选行共用。<see cref="Locked"/> 置位时悬停换色让位
    /// （列表选中行的金底不能被「悬停离开恢复常态」覆写掉）。</summary>
    public sealed class HoverFace : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image _face;
        TextMeshProUGUI _text;
        Color32 _hoverFace;
        Color32 _normalFace;
        Color32 _hoverText;
        Color32 _normalText;
        bool _hasFacePair;
        bool _hasTextPair;

        /// <summary>锁定常态（选中行：悬停不再改色，退出也不再恢复）。</summary>
        public bool Locked;

        /// <summary>常态透明面 → 悬停换色（check 系语义）。</summary>
        public void Bind(Image face, Color32 hover)
        {
            _face = face;
            _hoverFace = hover;
        }

        /// <summary>常态有面 → 悬停/常态成对面色（列表语义）。</summary>
        public void Bind(Image face, Color32 hover, Color32 normal)
        {
            Bind(face, hover);
            _normalFace = normal;
            _hasFacePair = true;
        }

        /// <summary>面色 + 字色成对换（列表悬停变暗 + 字灰 #7d7d7d——menuitem_hot 对）。</summary>
        public void Bind(Image face, Color32 hover, Color32 normal,
            TextMeshProUGUI text, Color32 hoverText, Color32 normalText)
        {
            Bind(face, hover, normal);
            _text = text;
            _hoverText = hoverText;
            _normalText = normalText;
            _hasTextPair = true;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Locked)
                return;
            if (_face != null)
                _face.color = _hoverFace;
            if (_text != null && _hasTextPair)
                _text.color = _hoverText;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Locked)
                return;
            if (_face != null && _hasFacePair)
                _face.color = _normalFace;
            else if (_face != null)
                _face.color = new Color(0f, 0f, 0f, 0f);
            if (_text != null && _hasTextPair)
                _text.color = _normalText;
        }
    }
}
