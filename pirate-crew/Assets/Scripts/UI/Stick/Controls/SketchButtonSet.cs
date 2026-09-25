using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// Aseprite theme <c>&lt;style id="buttonset_item"&gt;</c> 复刻：**对话框内的选项块**。
    ///
    /// 【为什么不是 radio】参考库里对话框内的二选一/三选一（New Sprite 的
    /// RGB / Grayscale / Indexed、Transparent / White / Black）走的是 **buttonset**——
    /// 一排**等宽九宫格按钮**，当前值换 <c>buttonset_item_active</c> 件；
    /// radio/check（8×8 图标 + 文字、常态无面）是"列表里的多选项"语法。
    /// 设置页的「画质」「窗口模式」两组是对话框内的二选一 → buttonset。
    ///
    /// 【件与态】件 16×16、切片 w 3/10/3、h 3/8/5（theme.xml &lt;parts&gt;）：
    /// <list type="bullet">
    /// <item>常态 → <c>buttonset_item_normal</c></item>
    /// <item>鼠标悬停 → <c>buttonset_item_hot</c>（theme <c>state="mouse"</c>）</item>
    /// <item>按下 → <c>buttonset_item_pushed</c></item>
    /// <item>键盘焦点 → <c>buttonset_item_focused</c>（<c>state="focus"</c>）</item>
    /// <item>业务"当前值" → <c>buttonset_item_active</c>（theme 单独一条 buttonset_item_active 风格）</item>
    /// </list>
    /// 字色 = <c>button_normal_text</c>（#C0C0C0，与 theme 各态一致）。
    /// </summary>
    public sealed class SketchButtonSet : Button
    {
        /// <summary>业务"当前值"态（常显 <c>buttonset_item_active</c> 件；与 hover/focus 正交）。</summary>
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                ApplySkin();
            }
        }

        private bool _active;
        private Image _bg;
        private TextMeshProUGUI _label;

        /// <summary>可见标签（场景重载后按名兜底重取，同 <see cref="SketchButton.Label"/> 的口径）。</summary>
        public TextMeshProUGUI Label
        {
            get
            {
                if (_label == null)
                {
                    Transform child = transform.Find("Label");
                    _label = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
                }
                return _label;
            }
        }

        /// <summary>建一枚选项块。<paramref name="size"/> 高应取件原生高 16（低于件高就是九宫格压缩）。</summary>
        public static SketchButtonSet Create(Transform parent, string name, string label,
            TMP_FontAsset font, float fontSize, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = go.AddComponent<Image>();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;   // ×1 终局：贴图纹素 = 画布像素
            image.color = Color.white;            // 像素件禁止乘色：色阶烘在贴图里
            image.raycastTarget = true;

            var item = go.AddComponent<SketchButtonSet>();
            item._bg = image;
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

            // 标签：铺满件盒（件自带左右 3 切片留白），垂直居中、顶点像素对齐。
            var labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(AseLayout.Px(AseLayout.CheckBorder), 0f);
            labelRect.offsetMax = new Vector2(-AseLayout.Px(AseLayout.CheckBorder), 0f);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label ?? string.Empty;
            tmp.fontSize = fontSize;
            TMP_FontAsset resolved = UiKit.ResolvePixelFont(Mathf.RoundToInt(fontSize), font);
            if (resolved != null)
            {
                tmp.font = resolved;
                PixelAtlasPointFilter.Ensure(resolved);
            }
            else if (font != null)
            {
                tmp.font = font;
            }
            tmp.fontStyle = FontStyles.Normal;   // 位图字禁伪粗
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.color = PixelSkin.Theme.Text;    // button_normal_text #C0C0C0
            tmp.raycastTarget = false;
            tmp.gameObject.AddComponent<PirateCrew.UI.PixelSnapText>();
            item._label = tmp;

            item.ApplySkin();
            return item;
        }

        /// <summary>按业务态挂皮（theme 四态走 SpriteSwap，业务"当前值"换 active 件）。</summary>
        public void ApplySkin()
        {
            if (_bg != null)
            {
                _bg.sprite = PixelSkin.Ase(_active ? "buttonset_item_active" : "buttonset_item_normal");
                _bg.type = Image.Type.Sliced;
                _bg.pixelsPerUnitMultiplier = 1f;
                _bg.color = Color.white;   // 像素件禁止乘色
            }

            SpriteState state = spriteState;
            state.highlightedSprite = PixelSkin.Ase("buttonset_item_hot");
            state.pressedSprite = PixelSkin.Ase("buttonset_item_pushed");
            state.selectedSprite = PixelSkin.Ase("buttonset_item_focused");
            state.disabledSprite = PixelSkin.Ase("buttonset_item_normal");
            spriteState = state;
        }
    }
}
