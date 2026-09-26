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
    /// <item>业务"当前值" → <c>buttonset_item_hot</c>（theme.xml 的
    ///       <c>&lt;style id="buttonset_item"&gt;</c> 把 <c>state="selected"</c> 映射到它：
    ///       只是"面更亮 + 底边下沉"的**无彩色**件；<c>buttonset_item_active</c> 是另一条
    ///       独立风格（蓝面件），Aseprite 用它表达"正在执行/激活"那种语义，不是当前选中值——
    ///       参考图 New Sprite 的 RGB/Grayscale/Indexed 三连按钮三块同色、没有任何彩色）</item>
    /// </list>
    /// 字色 = <c>button_normal_text</c>（#C0C0C0，与 theme 各态一致）。
    /// </summary>
    public sealed class SketchButtonSet : Button
    {
        /// <summary>业务"当前值"态（换 <c>buttonset_item_hot</c> 件：更亮面 + 底边下沉，无彩色；
        /// 与 hover/focus 正交）。切换后按当前交互态即时重挂皮——悬停/按压中切值时
        /// 底皮不会停在旧常态（同 <see cref="SketchButton.ApplySkin"/> 末尾的即时刷新）。</summary>
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

        /// <summary>皮件。**私有字段场景重载后为空**——走 <see cref="Selectable.targetGraphic"/>
        /// 兜底（序列化字段，重载后仍在）。不兜底则 <see cref="Active"/> 运行期换件静默失效。</summary>
        private Image Background
        {
            get
            {
                if (_bg == null)
                    _bg = targetGraphic as Image;
                return _bg;
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

            // 标签：锚整件拉伸 + 内容区边距（见下方 offset 注释），居中、顶点像素对齐。
            var labelGo = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            // 标签按件**内容区**取盒（切片 w 3/10/3、h 3/8/5）：左右各让 3、底让 5、顶让 3——
            // 内容区中心比几何中心高 1 格（同 SketchButton 标签的 +1 律），字不压底边框。
            // （旧版借用 CheckBorder=2 是 checkbox 的边距，与本件切片差 1。）
            labelRect.offsetMin = new Vector2(3f, 5f);
            labelRect.offsetMax = new Vector2(-3f, -3f);
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

        /// <summary>按业务态挂皮（theme 四态走 SpriteSwap，业务"当前值"换 active 件）。
        /// 【状态层引擎】件 id 不再写死——<see cref="AseThemeLayers"/> 从 theme.xml
        /// <c>&lt;style id="buttonset_item"&gt;</c> 按状态位解析（selected→hot、focus→focused、
        /// pressed=selected+capture→pushed 全部由层表给出）。</summary>
        public void ApplySkin()
        {
            Image bg = Background;
            if (bg != null)
            {
                string part = PartOf(SelectionState.Normal);
                if (part != null)
                    bg.sprite = PixelSkin.Ase(part);
                bg.type = Image.Type.Sliced;
                bg.pixelsPerUnitMultiplier = 1f;
                bg.color = Color.white;   // 像素件禁止乘色
            }

            SpriteState state = spriteState;
            state.highlightedSprite = SpriteOf(SelectionState.Highlighted);
            state.pressedSprite = SpriteOf(SelectionState.Pressed);
            state.selectedSprite = SpriteOf(SelectionState.Selected);
            state.disabledSprite = SpriteOf(SelectionState.Disabled);
            spriteState = state;
        }

        /// <summary>
        /// UGUI 选择态 → Aseprite 状态位。按下的 <c>Selected|Capture</c> 命中 buttonset_item
        /// 的 <c>state="capture selected"</c> / <c>state="mouse capture"</c> → <c>buttonset_item_pushed</c>；
        /// 业务"当前值"（<see cref="Active"/>）= Selected 位（命中 <c>state="selected"</c> → hot）；
        /// 禁用/键盘焦点时不让位（同 <see cref="SketchButton"/> 口径）。
        /// </summary>
        static AseStates FlagsFor(SelectionState state, bool active)
        {
            switch (state)
            {
                case SelectionState.Disabled:
                    return AseStates.Disabled;
                case SelectionState.Pressed:
                    return AseStates.Selected | AseStates.Capture;
                case SelectionState.Selected:
                    return AseStates.Focus;
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
