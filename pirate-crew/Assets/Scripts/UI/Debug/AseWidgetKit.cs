using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 调试面板公共小件工厂（theme 语义件的一次调用版本）——「一个小界面几百行」的
    /// 架构病收口：凹槽输入框 / 复选行 / 两件套组合框，全在此一处实现，
    /// 各面板（实摆画廊、New Sprite 对话框…）只声明布局与行为。
    /// 件与色的出处同各调用点注释：theme.xml 对应 style/parts 条目。
    ///
    /// 刻意不再提供的：通用「悬停换面」件。Aseprite 的悬停态是**逐件按 styles 表**给的
    /// （check_box/button 有 mouse 层，list_item/tab **没有**），一个通用 HoverFace 会把
    /// 有悬停的件和没悬停的件一起点亮——列表行的「悬停变暗」正是这么错的。
    /// 复选行的鼠标态见 <see cref="AseCheckBoxFace"/>，列表行见 <see cref="AseListBox"/>。
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

        /// <summary>复选/单选行（theme check_box / radio_button：**常态无底色层**，
        /// <c>state="mouse"</c> 才铺 <c>check_hot_face</c>/<c>radio_hot_face</c> #575B61；
        /// 图标 8×8 @x2 或 @x14 文字）。行宽按件表尺寸提示实收
        /// （8 图标 + 2 左缩 + 4 缝 + 文字 + 2 右边框）——悬停面的**范围**必须等于件本身，
        /// 旧版固定 160 宽会把文字右侧的空白也点亮。返回行根（可再查 Icon 换图标）。</summary>
        public static Button CheckRow(RectTransform parent, string label, float x, float y,
            bool initial, System.Action<bool> onChanged, string kind = "check")
        {
            var rowGo = new GameObject(kind + "_" + label, typeof(RectTransform));
            RectTransform rect = rowGo.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);

            var face = rowGo.AddComponent<Image>();
            face.color = new Color(0f, 0f, 0f, 0f);
            face.raycastTarget = true;
            rowGo.AddComponent<AseCheckBoxFace>().Bind(face, kind == "radio");

            var icon = UiKit.CreateRect("Icon", rect);
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 1f);
            icon.sizeDelta = new Vector2(8f, 8f);
            icon.anchoredPosition = new Vector2(2f, -4f);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;

            TextMeshProUGUI labelText = DebugWindowKit.PlaceLabel(rect, label, UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, 14f, 0f, 140f);

            // 尺寸提示（件表）：文字 @x14 + 正文宽 + 右边框 2（图标侧 2+8+4 = 14 已含在 x14 里）
            rect.sizeDelta = new Vector2(14f + Mathf.Ceil(labelText.preferredWidth) + 2f, 16f);

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

        /// <summary>两件套组合框（theme combobox：sunken2 词条 + 右缘 15 宽 mini_button 箭头钮）。
        /// 行为逐条按 combobox.cpp：点词条/点钮开合切换、弹层 = View(sunken) 里的 ListBox、
        /// 点弹层外收、选中项回填词条文本。实现见 <see cref="AseComboBox"/>。
        /// <paramref name="popupOverlay"/> = 弹层宿主（必须是调试根 overlay——弹层要盖过
        /// **所有**窗；缺省取 parent.parent，仅当组合框直接挂在窗根下时成立）。
        /// 宽度由调用方给定（组合框自身的 sizeHint 反推宽度未移植，见交接报告）。</summary>
        public static TextMeshProUGUI ComboBox(RectTransform parent, string name, float x, float y,
            float w, string[] options, int initial, System.Action<int> onPick = null,
            Transform popupOverlay = null)
        {
            RectTransform root = UiKit.CreateRect(name, parent);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(x, -y);
            root.sizeDelta = new Vector2(w, 12f);

            // 弹层宿主缺省 = parent.parent（窗的父级 overlay）——**不是** parent（窗）本身：
            // 弹层挂在窗里会被窗裁/随窗移动。
            Transform host = popupOverlay != null
                ? popupOverlay
                : (parent != null ? parent.parent : null);
            var combo = root.gameObject.AddComponent<AseComboBox>();
            return combo.Build(root, options, initial, onPick, host);
        }
    }

    /// <summary>复选/单选行的鼠标态（唯一的一层额外底色）：theme <c>check_box</c> /
    /// <c>radio_button</c> 样式 <c>&lt;background color="check_hot_face" state="mouse"/&gt;</c>
    /// （#575B61，radio 同名色）。常态**没有**底色层（非 disabled/focus/mouse 时不铺任何 background），
    /// 所以退出即回到全透明。禁用态 #2C2C30、焦点态 #41444A + check_focus 环、以及
    /// <c>state="mouse disabled"</c> 的回落都未移植（调试面板没有禁用/键盘焦点两种态）。</summary>
    public sealed class AseCheckBoxFace : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image _face;
        Color32 _hot;

        public void Bind(Image face, bool radio)
        {
            _face = face;
            // theme.xml <color id="check_hot_face"> = <color id="radio_hot_face"> = #575B61
            _hot = PixelSkin.Theme.HotFace;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_face != null)
                _face.color = _hot;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_face != null)
                _face.color = new Color(0f, 0f, 0f, 0f);
        }
    }
}
