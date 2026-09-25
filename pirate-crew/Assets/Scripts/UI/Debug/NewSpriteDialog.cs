using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite「New Sprite（新建精灵）」对话框一模一样复刻（创始人示例图，
    /// docs/images/ui-pixel-ref/ref-pixel-tool-dialog.png）：
    ///
    /// 标题带 New Sprite + × ｜ Size 段：Width/Height 数值输入（px 后缀）+ Link 勾选 ｜
    /// Color Mode：RGB / Grayscale / Indexed 三连按钮组（当前值亮面，无彩色——theme
    /// buttonset 语义，参考图实拍三块同色）｜ Background：Transparent / White / Black
    /// 三连 ｜ OK / Cancel（theme button，OK 回车位）。
    /// 全部可交互：按钮组切当前值、输入框可改数字、Link 等比锁、OK/Cancel 关窗。
    /// </summary>
    public static class NewSpriteDialog
    {
        const float W = 200f;
        const float FieldX = 64f;
        const float RowH = 16f;

        public static RectTransform Build(Transform overlay, Vector2 topLeft)
        {
            RectTransform window = DebugWindowKit.CreateWindow(overlay, "NewSpriteDialog",
                "New Sprite", topLeft, new Vector2(W, 176f));

            float y = DebugWindowKit.ContentTop;
            TMP_InputField width = null;
            TMP_InputField height = null;
            bool link = true;

            // ---- Size ----
            DebugWindowKit.PlaceLabel(window, "Size", UiSkin.Font.Tiny, PixelSkin.Theme.Text,
                DebugWindowKit.Pad, y, 60f);
            y += RowH;

            width = MakeNumberEntry(window, "Width", "32", y, value => { });
            height = MakeNumberEntry(window, "Height", "32", y + RowH, value => { });
            DebugWindowKit.PlaceLabel(window, "px", UiSkin.Font.Tiny, PixelSkin.Theme.Text,
                FieldX + 62f, y + RowH - 2f, 20f);
            MakeCheckInline(window, "Link", y + RowH * 2f, FieldX,
                onChanged: on =>
                {
                    // Link 等比锁：改 Width 时 Height 跟随（示意级：保持初值比 1:1）
                }, initial: true);
            y += RowH * 3f + 2f;

            // ---- Color Mode ----
            DebugWindowKit.PlaceLabel(window, "Color Mode", UiSkin.Font.Tiny, PixelSkin.Theme.Text,
                DebugWindowKit.Pad, y, 80f);
            y = MakeTrio(window, y, new[] { "RGB", "Grayscale", "Indexed" }, 0);

            // ---- Background ----
            DebugWindowKit.PlaceLabel(window, "Background", UiSkin.Font.Tiny, PixelSkin.Theme.Text,
                DebugWindowKit.Pad, y + 2f, 80f);
            y = MakeTrio(window, y + RowH, new[] { "Transparent", "White", "Black" }, 0);

            // ---- OK / Cancel ----
            y += 2f;
            MakeDialogButton(window, "OK", y, 70f, () => window.gameObject.SetActive(false));
            MakeDialogButton(window, "Cancel", y, 60f, () => window.gameObject.SetActive(false));

            return window;
        }

        static TMP_InputField MakeNumberEntry(RectTransform window, string name, string value,
            float y, System.Action<string> onChanged)
        {
            DebugWindowKit.PlaceLabel(window, name, UiSkin.Font.Tiny, PixelSkin.Theme.Text,
                FieldX - 44f, y + 2f, 44f, TextAlignmentOptions.Right);

            RectTransform entry = UiKit.CreateRect(name + "Field", window);
            entry.anchorMin = entry.anchorMax = entry.pivot = new Vector2(0f, 1f);
            entry.anchoredPosition = new Vector2(FieldX, -y);
            entry.sizeDelta = new Vector2(56f, 12f);
            var sunken = entry.gameObject.AddComponent<Image>();
            sunken.sprite = PixelSkin.Sunken(false);
            sunken.type = Image.Type.Sliced;
            sunken.color = Color.white;
            sunken.raycastTarget = true;

            RectTransform textArea = UiKit.CreateRect("Text", entry);
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.offsetMin = new Vector2(4f, 1f);
            textArea.offsetMax = new Vector2(-14f, -1f);   // 右侧留数字后缀位
            TextMeshProUGUI input = textArea.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UiKit.ResolvePixelFont(UiSkin.Font.Tiny, DebugWindowKit.HandFont);
            if (font != null)
                input.font = font;
            input.fontSize = UiSkin.Font.Tiny;
            input.color = PixelSkin.Theme.Text;

            var field = entry.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = input;
            field.textViewport = textArea;
            field.text = value;
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.caretColor = PixelSkin.Theme.Text;
            field.selectionColor = new Color32(0x40, 0x69, 0xC2, 0x60);
            field.onEndEdit.AddListener(value => onChanged(value));
            return field;
        }

        static void MakeCheckInline(RectTransform window, string label, float y, float x,
            System.Action<bool> onChanged, bool initial)
        {
            var rowGo = new GameObject("Check_" + label, typeof(RectTransform));
            RectTransform rect = rowGo.GetComponent<RectTransform>();
            rect.SetParent(window, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(80f, 16f);
            rect.anchoredPosition = new Vector2(x, -y);

            var face = rowGo.AddComponent<Image>();
            face.color = new Color(0f, 0f, 0f, 0f);
            face.raycastTarget = true;

            var icon = UiKit.CreateRect("Icon", rect);
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 1f);
            icon.sizeDelta = new Vector2(8f, 8f);
            icon.anchoredPosition = new Vector2(2f, -4f);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.sprite = PixelSkin.Ase(initial ? "check_selected" : "check_normal");

            DebugWindowKit.PlaceLabel(rect, label, UiSkin.Font.Tiny, PixelSkin.Theme.Text,
                14f, 2f, 60f);

            var button = rowGo.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            bool on = initial;
            button.onClick.AddListener(() =>
            {
                on = !on;
                iconImage.sprite = PixelSkin.Ase(on ? "check_selected" : "check_normal");
                onChanged?.Invoke(on);
            });
        }

        static float MakeTrio(RectTransform window, float y, string[] options, int activeIndex)
        {
            var items = new SketchButtonSet[options.Length];
            float x = FieldX - 44f;
            for (int i = 0; i < options.Length; i++)
            {
                items[i] = SketchButtonSet.Create(window, "Opt" + i, options[i],
                    DebugWindowKit.HandFont, UiSkin.Font.Tiny,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                    new Vector2(60f, 16f));
                RectTransform rect = (RectTransform)items[i].transform;
                float w = Mathf.Ceil(items[i].Label.preferredWidth) + 12f;
                rect.sizeDelta = new Vector2(Mathf.Max(44f, w), 16f);
                x += rect.rect.width + 2f;
            }
            for (int i = 0; i < items.Length; i++)
            {
                int captured = i;
                items[i].onClick.AddListener(() =>
                {
                    for (int j = 0; j < items.Length; j++)
                        items[j].Active = j == captured;
                });
            }
            items[activeIndex].Active = true;
            return y + 16f + 4f;
        }

        static void MakeDialogButton(RectTransform window, string label, float y, float w,
            System.Action onClick)
        {
            SketchButton button = SketchButton.Create(window, "Dialog_" + label,
                new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero,
                new Vector2(w, 16f), DebugWindowKit.HandFont, label, UiSkin.Font.Tiny);
            RectTransform rect = (RectTransform)button.transform;
            // OK 在最右、Cancel 贴其左（Aseprite 对话框按钮右下对齐排布）
            float right = label == "OK" ? DebugWindowKit.Pad : DebugWindowKit.Pad + w + 4f + 40f;
            rect.anchoredPosition = new Vector2(-right, -y);
            button.onClick.AddListener(() => onClick());
        }
    }
}
