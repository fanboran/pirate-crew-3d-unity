using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// Aseprite dark 按钮皮（**已退役 kind 体系**）——theme &lt;style id="button"&gt;（theme.xml:608-620）
    /// 的 UGUI 复刻：底 = sheet.png 直切件 <c>button_normal/hot/focused/selected</c> 四态 SpriteSwap，
    /// 字色按态同步（theme.xml:614-619）。
    ///
    /// 【四态映射（theme.xml:609-612 + 源码状态位实锄）】Normal→button_normal、
    /// Highlighted(mouse)→button_hot、Selected(键盘)→button_focused（蓝描边）、
    /// Sticky（业务选中，如设置分类）→button_selected；
    /// **Pressed→button_selected（蓝面 + 白字）**——ButtonBase 按下 = selected+capture
    /// 状态位（button.cpp:168-175），层匹配（theme.cpp:69-73）命中 state="selected"。
    /// 按压位移（PressOffset）随 ×1 终局整体退役。
    ///
    /// 【禁用态（theme.xml:617-619）】双层字：background 色 (x+1,y+1) 垫底影子 + disabled 色
    /// (#202125) 盖面，底皮仍 button_normal——忠实复刻，不走 CanvasGroup 压 alpha。
    ///
    /// 【位图字纪律】禁伪粗/禁描边（创始人 2026-09-24 走查：TMP 合成加粗糊死位图字形），
    /// 层级只靠字色与皮态表达。
    ///
    /// 【状态层引擎（单一真源）】上述"几态换哪件/哪色"已不再逐态写死在本类，而是把
    /// UGUI 选择态 + Sticky 折算成 Aseprite 状态位（<see cref="AseButtonBase.FlagsOf"/>），
    /// 交 <see cref="AseThemeLayers"/>（theme.cpp:53-130 for_each_layer 移植）从
    /// theme.xml &lt;style id="button"&gt; 解析件与字色。唯一与原手写版的差异登记在交付报告：
    /// 键盘焦点态（UGUI Selected）字色由 #FFFFFF 回归源的 #C0C0C0（button 样式无 focus 文字层）。
    /// </summary>
    public sealed class SketchButton : AseButtonBase
    {
        /// <summary>业务选中态（本类对 <see cref="AseButtonBase.Active"/> 的别名）：常显
        /// button_selected 皮 + 白字（设置分类选中、当前页签钮）。运行期可切——setter 即时重挂皮与字色。</summary>
        public bool Sticky
        {
            get => _active;
            set
            {
                if (_active == value)
                    return;
                _active = value;
                ApplySkin();
            }
        }

        private bool _applied;
        private TextMeshProUGUI _shadowLabel;   // 禁用态双层字：background 色 (x+1,y+1) 垫底

        // 可见标签 Label 已上移 AseButtonBase 基类（含影子层案的完整警示注释）。

        /// <summary>禁用态影子层（兄弟序 0 的不可见层）。按名兜底重取，同 <see cref="Label"/>。</summary>
        private TextMeshProUGUI ShadowLabel
        {
            get
            {
                if (_shadowLabel == null)
                {
                    Transform child = transform.Find("LabelShadow");
                    _shadowLabel = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
                }
                return _shadowLabel;
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
            image.raycastTarget = true;     // 可点件：命中面 = 按钮本体

            var button = rect.gameObject.AddComponent<SketchButton>();
            button.InitAseSkin(image);      // targetGraphic + SpriteSwap + 全白 ColorBlock

            button._label = AddLabel(rect, label, font, fontSize, shadowMode: false);
            button._shadowLabel = AddLabel(rect, label, font, fontSize, shadowMode: true);
            button._shadowLabel.transform.SetSiblingIndex(button._label.transform.GetSiblingIndex());

            button.ApplySkin();
            return button;
        }

        /// <summary>theme 样式 id（theme.xml:608-620）——件/字色全部由 <see cref="AseThemeLayers"/> 按本 id 解析。</summary>
        protected override string StyleId => "button";

        /// <summary>
        /// 挂四态皮（theme.xml:608-620）。Sticky 运行时可切（设置分类选中态），每次重挂。
        /// 【状态层引擎】件 id / 字色不再逐态写死——全部经 <see cref="AseThemeLayers"/>
        /// 从 theme.xml <c>&lt;style id="button"&gt;</c> 按状态位解析（for_each_layer 移植）。
        /// </summary>
        public override void ApplySkin()
        {
            _applied = true;
            base.ApplySkin();
        }

        /// <summary>
        /// 状态分发：底皮四态由 <see cref="Selectable.Transition.SpriteSwap"/> 在 base 里切换；
        /// 本覆盖按引擎解析的字色层刷字（theme.xml:614-619），禁用态双层字（影子背景色
        /// (x+1,y+1) + disabled 盖面）由引擎返回的**两条 text 层**驱动——同源里 newlayer 切段后的绘制序。
        /// 映射依据：Pressed = Selected|Capture——ButtonBase::onMouseDown
        /// <c>setSelected(true)+captureMouse()</c>（button.cpp:168-175）；UGUI Selected = Focus 位 +
        /// Sticky 再叠 Selected——for_each_layer 取最大命中层（theme.cpp:69-73），button 样式无
        /// selected focus 层，粘滞+焦点自然解析到 button_selected、非粘滞焦点到 button_focused
        /// （theme.xml:607-608）；Disabled 独占不让位（否则 Sticky+禁用会命中 button_selected 而非常态皮）。
        /// </summary>
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (!_applied)
                return;

            List<AseThemeLayer> layers = AseThemeLayers.ResolveTextLayers("button", FlagsOf(state));

            TextMeshProUGUI label = Label;
            if (label != null)
            {
                // 最上层 text 层 = 盖面字色（常态/悬停 #c0c0c0、选中/按下白、禁用 #202125）。
                Color32 color = layers.Count > 0 ? layers[layers.Count - 1].Color : PixelSkin.Theme.Text;
                label.color = color;
            }

            TextMeshProUGUI shadow = ShadowLabel;
            if (shadow != null)
            {
                // 禁用态引擎返回两条 text 层（段一 = 垫底影子），常态/悬停/选中只有一条。
                bool show = layers.Count >= 2;
                shadow.gameObject.SetActive(show);
                if (show)
                {
                    shadow.color = layers[0].Color;
                    shadow.rectTransform.anchoredPosition =
                        new Vector2(layers[0].Offset.x, -layers[0].Offset.y);
                }
            }
        }

        private static TextMeshProUGUI AddLabel(RectTransform root, string content,
            TMP_FontAsset font, float fontSize, bool shadowMode)
        {
            // 像素纪律四件套（档解析就近选档 / 图集钉 Point / PixelSnap / 禁接光）走
            // UiKit.CreateText 单点——此处只留按钮字区的特化（字盒边距 + 影子层）。
            float size = fontSize > 0f ? fontSize : UiSkin.Font.Body;
            var label = UiKit.CreateText(shadowMode ? "LabelShadow" : "Label", root,
                content ?? string.Empty, Mathf.RoundToInt(size),
                TextAlignmentOptions.Center, Color.white, font, wrap: false);
            label.fontSize = size;   // 保留 float 字号（不取整档位的中间号）
            RectTransform rt = label.rectTransform;
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
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }
    }
}
