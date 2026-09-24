using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 复选/单选控件（theme.xml check_box / radio_button 复刻，2026-09-25 搬皮批）。
    ///
    /// 【theme 口径】图标 8×8（check/radio，常态/选中两张）贴左 x=2、文字 x=14；
    /// 悬停/聚焦整行铺 <see cref="PixelSkin.Theme.HotFace"/>（#575b61），常态无底色。
    /// 选中态 = **换图标 sprite**（✓ / 中心点），绝不做乘色（像素件禁乘色）。
    ///
    /// 【Button 子类】与 <see cref="SketchButton"/> 同契约：控制器 [SerializeField] Button
    /// 字段直赋兼容；onClick 由调用方接（选中态语义归控制器——本控件只管视觉两态）。
    /// </summary>
    public sealed class SketchCheck : Button
    {
        /// <summary>单选（radio 图标族）还是复选（check 图标族）。</summary>
        public bool Radio;

        /// <summary>选中两态（换图标 sprite，不乘色）。</summary>
        public bool IsOn
        {
            get => _on;
            set
            {
                _on = value;
                ApplyIcon();
            }
        }

        private bool _on;
        private Image _icon;

        /// <summary>
        /// 建一个复选/单选。图标 8×8 贴左（<see cref="AseLayout.CheckIconX"/> = 2 设计格）、
        /// 文字从 <see cref="AseLayout.CheckTextX"/> = 14 设计格起（theme 原值，×Unit 进画布）；
        /// 行高由调用方给（设置行 27）。
        /// </summary>
        public static SketchCheck Create(Transform parent, string name, string label, bool radio,
            TMP_FontAsset font, float fontSize, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, float height, Color? labelColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;

            var check = go.AddComponent<SketchCheck>();
            check.Radio = radio;
            check.transition = Transition.ColorTint;
            check.targetGraphic = check.BuildBackground(rect);

            check.BuildIcon(rect, height);
            check.BuildLabel(rect, label, font, fontSize, height,
                labelColor ?? PixelSkin.TextColorOn(PixelTone.Light));

            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(
                AseLayout.Px(AseLayout.CheckTextX) + fontSize * (label.Length + 1f), height);
            check.IsOn = false;
            return check;
        }

        /// <summary>悬停底：常态全透明，mouse/selected 铺 HotFace（ColorTint 只作用于本 Graphic）。</summary>
        Graphic BuildBackground(RectTransform root)
        {
            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = null;
            bg.color = PixelSkin.Theme.HotFace;
            bg.raycastTarget = true;
            var colorsBlock = colors;
            colorsBlock.normalColor = new Color(1f, 1f, 1f, 0f);
            colorsBlock.highlightedColor = Color.white;
            colorsBlock.selectedColor = Color.white;
            colorsBlock.pressedColor = Color.white;
            colorsBlock.disabledColor = new Color(1f, 1f, 1f, 0f);
            colorsBlock.fadeDuration = 0f;
            colors = colorsBlock;
            return bg;
        }

        void BuildIcon(RectTransform root, float height)
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(root, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(AseLayout.Px(AseLayout.CheckIconX), 0f);
            // theme check/radio 8×8 整格（×Unit 落盘 16×16），不随行高拉伸
            iconRect.sizeDelta = new Vector2(8f * PixelSkin.Unit, 8f * PixelSkin.Unit);

            _icon = iconGo.AddComponent<Image>();
            _icon.type = Image.Type.Simple;
            _icon.color = Color.white;   // 图标不乘色（烘焙色即 theme 色）
            _icon.raycastTarget = false;
        }

        void BuildLabel(RectTransform root, string label, TMP_FontAsset font, float fontSize,
            float height, Color color)
        {
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(root, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(AseLayout.Px(AseLayout.CheckTextX), 0f);
            labelRect.sizeDelta = new Vector2(fontSize * (label.Length + 1f), height);

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Normal;   // 位图字禁伪粗
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.SetText(label);
        }

        void ApplyIcon()
        {
            if (_icon == null)
                return;
            _icon.sprite = Radio ? PixelSkin.Radio(_on) : PixelSkin.Check(_on);
        }

        private void OnEnable() => ApplyIcon();
    }
}
