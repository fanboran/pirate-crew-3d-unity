using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// Aseprite theme <c>buttonset_item_text_top_icon_bottom</c> 变体（New Sprite 对话框的
    /// Color Mode / Background 三连）：与 <see cref="SketchButtonSet"/>（居中文字款）平行的
    /// 选项块——**文字上置 + 16×16 图标下置**（theme.xml:1118-1128：padding-top 2 /
    /// padding-bottom 1，图标取 parts 表直切件如 icon_rgb / icon_transparent，sheet 原色）。
    /// 四态 SpriteSwap + 业务"当前值"换 hot 件（selected→hot，无彩色），同 buttonset 语义。
    /// </summary>
    public sealed class SketchButtonSetIcon : Button
    {
        private bool _active;

        /// <summary>业务"当前值"态（换 buttonset_item_hot 件；与 hover/按压正交）。</summary>
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                ApplySkin();
                DoStateTransition(currentSelectionState, true);
            }
        }

        /// <summary>建一枚图文选项块。图标按件原生 16×16 贴底居中，文字贴顶居中。</summary>
        public static SketchButtonSetIcon Create(Transform parent, string name, string label,
            string iconPart, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = go.AddComponent<Image>();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = Color.white;          // 像素件禁止乘色
            image.raycastTarget = true;

            var item = go.AddComponent<SketchButtonSetIcon>();
            item.targetGraphic = image;
            item.transition = Selectable.Transition.SpriteSwap;
            ColorBlock colors = item.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
            colors.fadeDuration = 0f;
            item.colors = colors;

            // 文字 top（style padding-top 2），字色 button_normal_text #C0C0C0
            var labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -2f);
            labelRect.sizeDelta = new Vector2(size.x - 6f, 10f);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = UiSkin.Font.Tiny;
            TMP_FontAsset resolved = UiKit.ResolvePixelFont(UiSkin.Font.Tiny, null);
            if (resolved != null)
            {
                tmp.font = resolved;
                PixelAtlasPointFilter.Ensure(resolved);
            }
            tmp.fontStyle = FontStyles.Normal;   // 位图字禁伪粗
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.color = PixelSkin.Theme.Text;
            tmp.raycastTarget = false;
            tmp.gameObject.AddComponent<PixelSnapText>();

            // 图标 bottom（16×16 直切件，sheet 原色，style padding-bottom 1）
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0f);
            iconRect.pivot = new Vector2(0.5f, 0f);
            iconRect.anchoredPosition = new Vector2(0f, 1f);
            iconRect.sizeDelta = new Vector2(16f, 16f);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = PixelSkin.Ase(iconPart);
            icon.pixelsPerUnitMultiplier = 1f;
            icon.color = Color.white;            // 像素件禁止乘色
            icon.raycastTarget = false;

            item.ApplySkin();
            return item;
        }

        /// <summary>按业务态挂皮（同 <see cref="SketchButtonSet.ApplySkin"/> 口径）。
        /// 【状态层引擎】件 id 经 <see cref="AseThemeLayers"/> 从 theme.xml
        /// <c>&lt;style id="buttonset_item"&gt;</c> 解析（<c>buttonset_item_text_top_icon_bottom</c>
        /// 继承同一件表，底皮无差异）。</summary>
        public void ApplySkin()
        {
            var bg = targetGraphic as Image;
            if (bg != null)
            {
                string part = PartOf(SelectionState.Normal);
                if (part != null)
                    bg.sprite = PixelSkin.Ase(part);
                bg.type = Image.Type.Sliced;
                bg.pixelsPerUnitMultiplier = 1f;
                bg.color = Color.white;
            }

            SpriteState state = spriteState;
            state.highlightedSprite = SpriteOf(SelectionState.Highlighted);
            state.pressedSprite = SpriteOf(SelectionState.Pressed);
            state.selectedSprite = SpriteOf(SelectionState.Selected);
            state.disabledSprite = SpriteOf(SelectionState.Disabled);
            spriteState = state;
        }

        /// <summary>UGUI 选择态 → Aseprite 状态位（同 <see cref="SketchButtonSet"/> 口径）。</summary>
        static AseStates FlagsFor(SelectionState state, bool active)
        {
            switch (state)
            {
                case SelectionState.Disabled:
                    return AseStates.Disabled;
                case SelectionState.Pressed:
                    return AseStates.Selected | AseStates.Capture;
                case SelectionState.Selected:
                    // 源里当前项 = selected+focus → buttonset_item_hot_focused（theme.xml:1071，
                    // 白面 + 琥珀虚线环 #786050）；focus-only（未选中却被键盘聚焦）才是
                    // buttonset_item_focused 描边件（:1075）。漏带 Selected 位会让当前项
                    // 丢 hot 面只剩描边件。
                    return AseStates.Focus | (active ? AseStates.Selected : AseStates.None);
                case SelectionState.Highlighted:
                    return AseStates.Mouse | (active ? AseStates.Selected : AseStates.None);
                default:
                    return active ? AseStates.Selected : AseStates.None;
            }
        }

        string PartOf(SelectionState state)
        {
            return AseThemeLayers.ResolveBackgroundPart("buttonset_item", FlagsFor(state, _active));
        }

        Sprite SpriteOf(SelectionState state)
        {
            string part = PartOf(state);
            return part != null ? PixelSkin.Ase(part) : null;
        }
    }
}
