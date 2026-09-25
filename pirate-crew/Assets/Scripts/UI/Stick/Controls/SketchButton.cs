using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// Aseprite dark 按钮皮（**已退役 kind 体系**）——theme &lt;style id="button"&gt;（theme.xml:608-620）
    /// 的 UGUI 复刻：底 = sheet.png 直切件 <c>button_normal/hot/focused/selected</c> 四态 SpriteSwap，
    /// 字色按态同步（theme.xml:614-619）。
    ///
    /// 【四态映射（theme.xml:609-612）】Normal→button_normal、Highlighted(mouse)→button_hot、
    /// Selected(键盘)→button_focused（蓝描边）、Sticky（业务选中，如设置分类）→button_selected。
    /// theme 无按压皮（&lt;parts&gt; 无 button_pressed，执行案 §2：按压反馈=pressed 皮的需求不成立），
    /// Pressed 沿用 button_hot；按压位移（PressOffset）随 ×1 终局整体退役。
    ///
    /// 【禁用态（theme.xml:617-619）】双层字：background 色 (x+1,y+1) 垫底影子 + disabled 色
    /// (#202125) 盖面，底皮仍 button_normal——忠实复刻，不走 CanvasGroup 压 alpha。
    ///
    /// 【位图字纪律】禁伪粗/禁描边（创始人 2026-09-24 走查：TMP 合成加粗糊死位图字形），
    /// 层级只靠字色与皮态表达。
    /// </summary>
    public sealed class SketchButton : Button
    {
        /// <summary>业务选中态：常显 button_selected 皮 + 白字（设置分类选中、当前页签钮）。</summary>
        public bool Sticky { get; set; }

        private bool _applied;
        private Image _bg;
        private TextMeshProUGUI _label;
        private TextMeshProUGUI _shadowLabel;   // 禁用态双层字：background 色 (x+1,y+1) 垫底

        /// <summary>可见标签件（<c>Label</c> 孩子）。
        /// **取文案请走这里，别用 <c>GetComponentInChildren&lt;TextMeshProUGUI&gt;</c>**——
        /// 影子层按 theme 绘制序被插到兄弟序 0（压在标签下），泛搜会先取到那层**不可见**的
        /// 影子：文字写进去不显示、可见标签留空还让按钮按空文案收窄
        /// （实拍：选关列表整列出战按钮被挤成细条、船员列表已解锁行按钮无字）。
        /// 场景重载后私有字段不序列化，按名兜底重取。</summary>
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

        /// <summary>取任意按钮的**可见**标签：SketchButton 走本类的名字出口，
        /// 其他按钮回落泛搜（它们没有影子层）。**勿直接用
        /// <c>GetComponentInChildren&lt;TextMeshProUGUI&gt;</c>** —— 见 <see cref="Label"/>。</summary>
        public static TextMeshProUGUI LabelOf(Button button)
        {
            if (button == null)
                return null;
            var sketch = button as SketchButton;
            if (sketch != null)
                return sketch.Label;
            return button.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        /// <summary>
        /// 建一枚完整按钮（theme button 四态皮 + 本组件 + 居中文字）。
        /// </summary>
        public static SketchButton Create(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font,
            string label = null, float fontSize = 0f)
        {
            RectTransform rect = NewRect(name, parent, anchor, pivot, anchoredPosition, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;   // ×1 终局：贴图纹素 = 画布像素，ppum 恒 1
            image.color = Color.white;      // 像素件禁止乘色：色阶烘在贴图里，Image.color 恒白
            image.raycastTarget = true;     // 可点件：命中面 = 按钮本体

            var button = rect.gameObject.AddComponent<SketchButton>();
            button._bg = image;
            button.targetGraphic = image;
            // 状态走 SpriteSwap（贴图切换），ColorBlock 不参与染色（全白仅为占位）。
            button.transition = Selectable.Transition.SpriteSwap;
            button.colors = WhiteStates();

            button._label = AddLabel(rect, label, font, fontSize, shadowMode: false);
            button._shadowLabel = AddLabel(rect, label, font, fontSize, shadowMode: true);
            button._shadowLabel.transform.SetSiblingIndex(button._label.transform.GetSiblingIndex());

            button.ApplySkin();
            return button;
        }

        /// <summary>
        /// 挂四态皮（theme.xml:609-616）。Sticky 运行时可切（设置分类选中态），每次重挂。
        /// </summary>
        public void ApplySkin()
        {
            _applied = true;

            if (_bg != null)
            {
                _bg.sprite = PixelSkin.Ase(Sticky ? "button_selected" : "button_normal");
                _bg.type = Image.Type.Sliced;
                _bg.pixelsPerUnitMultiplier = 1f;
                _bg.color = Color.white;    // 像素件禁止乘色
            }

            SpriteState state = spriteState;
            state.highlightedSprite = PixelSkin.Ase(Sticky ? "button_selected" : "button_hot");
            state.pressedSprite = PixelSkin.Ase("button_hot");      // theme 无按压皮：按下保持 hot
            state.selectedSprite = PixelSkin.Ase("button_focused"); // 键盘焦点 = 蓝描边
            state.disabledSprite = PixelSkin.Ase("button_normal");  // 禁用 = 常态皮 + 双层字
            spriteState = state;

            // 立即按当前态刷一遍（贴图四态 / 字色 / 影子层）
            DoStateTransition(currentSelectionState, true);
        }

        /// <summary>
        /// 状态分发：底皮四态由 <see cref="Selectable.Transition.SpriteSwap"/> 在 base 里切换；
        /// 本覆盖补字色（theme.xml:614-619）与禁用双层影子字。
        /// </summary>
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (!_applied)
                return;

            bool disabled = state == SelectionState.Disabled;
            if (_label != null)
            {
                // theme.xml:614-619——常态/悬停 #c0c0c0；selected 态白（button_selected_text）；
                // Sticky 常显白；禁用 = disabled 色 #202125 盖面。
                _label.color = disabled
                    ? PixelSkin.Theme.Disabled
                    : Sticky || state == SelectionState.Selected && IsStickySelected()
                        ? PixelSkin.Theme.TextSelected
                        : PixelSkin.Theme.Text;
            }
            if (_shadowLabel != null)
            {
                // 影子层只在禁用态显形（background 色 (x+1,y+1) 垫底，theme.xml:617）。
                _shadowLabel.gameObject.SetActive(disabled);
            }
        }

        /// <summary>
        /// Selected 态字色判定：UGUI 的 Selected = 键盘焦点（皮 = button_focused，字仍正文灰），
        /// 只有 Sticky 的业务选中（皮 = button_selected）才吃 button_selected_text 白字。
        /// </summary>
        bool IsStickySelected() => Sticky;

        /// <summary>乘色全白占位：像素皮的状态反馈靠贴图切换（SpriteSwap），不叠乘色。</summary>
        private static ColorBlock WhiteStates()
        {
            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = Color.white,
                pressedColor = Color.white,
                selectedColor = Color.white,
                disabledColor = Color.white,
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
        }

        private static TextMeshProUGUI AddLabel(RectTransform root, string content,
            TMP_FontAsset font, float fontSize, bool shadowMode)
        {
            var go = new GameObject(shadowMode ? "LabelShadow" : "Label", typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            // 【文字盒边距】左右 = PAD_X + 2（gd 按钮盒 content margins 同源）；
            // 上下**不对称**——theme button 切片 h1=4 / h3=6（theme.xml:155），sprite 设计的
            // 字区是 [4, h-6]，其中心比矩形几何中心高 1 画布格。按几何居中（2/2 边距）会让
            // 字落在字区偏下 1 格（实拍：墨迹中心 462 屏像素 vs 盒心 461.5、字区中心 460）。
            // 故底边多让 1 格：字盒 = [3, h-1]，中心 = 几何中心 + 1 格 ✓ 与 sprite 字区对齐。
            rt.offsetMin = new Vector2(StickTokens.PAD_X + 2f, 3f);
            rt.offsetMax = new Vector2(-(StickTokens.PAD_X + 2f), -1f);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = content ?? string.Empty;
            if (font != null)
                label.font = font;
            label.fontSize = fontSize > 0f ? fontSize : UiSkin.Font.Body;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.margin = Vector4.zero;
            label.gameObject.AddComponent<PirateCrew.UI.PixelSnapText>();   // 顶点像素对齐
            PirateCrew.UI.PixelAtlasPointFilter.Ensure(label.font);   // 图集钉 Point：双线性会渗邻字
            if (shadowMode)
            {
                // theme.xml:617：影子色 = theme background（#41444a），偏移 (1,1) 设计格
                label.color = PixelSkin.Theme.Background;
                label.rectTransform.anchoredPosition = new Vector2(1f, -1f);
                label.gameObject.SetActive(false);
            }
            return label;
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }
    }
}
