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
    /// 一排**等宽九宫格按钮**，当前值换 <c>buttonset_item_hot</c> 件；
    /// radio/check（8×8 图标 + 文字、常态无面）是"列表里的多选项"语法。
    /// 设置页的「画质」「窗口模式」两组是对话框内的二选一 → buttonset。
    ///
    /// 【件与态】件 16×16、切片 w 3/10/3、h 3/8/5（theme.xml &lt;parts&gt;）：
    /// 常态 <c>buttonset_item_normal</c>、悬停 <c>buttonset_item_hot</c>（state="mouse"）、
    /// 按下 <c>buttonset_item_pushed</c>、键盘焦点 <c>buttonset_item_focused</c>（state="focus"）、
    /// 业务"当前值" → <c>buttonset_item_hot</c>（theme.xml 的 style 把 state="selected" 映射到它：
    /// 只是"面更亮 + 底边下沉"的**无彩色**件；<c>buttonset_item_active</c> 是另一条独立风格
    /// （蓝面件），表达"正在执行/激活"那种语义，不是当前选中值）。字色 = <c>button_normal_text</c>（#C0C0C0）。
    ///
    /// 【状态位映射与件解析】全部由 <see cref="AseButtonBase"/> 按 <see cref="StyleId"/> 统一实现
    /// （Selected 必须带业务位 → hot_focused，见基类注释与 theme.xml:1071/1075）。
    /// </summary>
    public sealed class SketchButtonSet : AseButtonBase
    {
        // 可见标签 Label 已上移 AseButtonBase 基类。

        /// <summary>theme 样式 id（件表见类注释）。</summary>
        protected override string StyleId => "buttonset_item";

        /// <summary>建一枚选项块。<paramref name="size"/> 高应取件原生高 16（低于件高就是九宫格压缩）。</summary>
        public static SketchButtonSet Create(Transform parent, string name, string label,
            TMP_FontAsset font, float fontSize, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;

            var item = rect.gameObject.AddComponent<SketchButtonSet>();
            item.InitAseSkin(image);      // targetGraphic + SpriteSwap + 全白 ColorBlock

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
    }
}
