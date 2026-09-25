using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 调试窗工厂与内容排版小件（调试菜单族的公共地基层）。
    ///
    /// 窗 = <see cref="SketchPanel"/> titled（theme window 直切件：标题带 15 + 窗控钮 ×）+
    /// <see cref="WindowDragger"/> 拖动带；内容排版用「顶左锚 + 纵向游标」手工摆
    /// （对齐 theme 的 border=6 内容边距，不引 LayoutGroup——像素纪律下手工坐标最可控）。
    /// </summary>
    public static class DebugWindowKit
    {
        /// <summary>内容边距（theme window_with_title border=6）。</summary>
        public const float Pad = 6f;

        /// <summary>内容起始 y（标题带底 + 边距）。</summary>
        public static float ContentTop => PixelSkin.WindowTitleBand + Pad;

        /// <summary>调试窗标题字体（与展示页同源：FusionPixel SDF，按字号解析档）。</summary>
        public static TMP_FontAsset HandFont => PixelShowcasePage.PixelFont();

        /// <summary>
        /// 建一枚可拖动调试窗（默认带 × 关闭）。<paramref name="topLeft"/> = 距画布左上角的
        /// 画布坐标；窗体锚/枢轴固定 (0,1)，拖动带与夹取按此约定。
        /// </summary>
        public static RectTransform CreateWindow(Transform canvas, string name, string title,
            Vector2 topLeft, Vector2 size, bool closeButton = true)
        {
            SketchPanel panel = SketchPanel.Create(canvas, name,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(topLeft.x, -topLeft.y), size,
                SketchPanel.Tone.Dark, titled: true);
            RectTransform root = (RectTransform)panel.transform;
            root.SetAsLastSibling();
            UiKit.EnsureTitleLabel(root, title, HandFont, UiSkin.Font.Body);
            if (closeButton)
            {
                Button close = UiKit.CreateWindowButton(root, "CloseButton",
                    PixelSkin.WindowIconSprite(PixelSkin.WindowIcon.Close),
                    AseLayout.Px(AseLayout.CloseButtonMarginRight));
                close.onClick.AddListener(() => root.gameObject.SetActive(false));
            }
            WindowDragger.Attach(root);
            return root;
        }

        /// <summary>文本件（UiKit.CreateText 同源：字档解析 + 顶点像素对齐；禁换行）。</summary>
        public static TextMeshProUGUI Label(Transform parent, string content, int fontSize, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TextMeshProUGUI text = UiKit.CreateText("Label", parent, content, fontSize, align, color, HandFont);
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>摆一个文本件到 (x, y)（顶左锚），给足宽度。</summary>
        public static TextMeshProUGUI PlaceLabel(RectTransform parent, string content, int fontSize,
            Color color, float x, float y, float w, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TextMeshProUGUI text = Label(parent, content, fontSize, color, align);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w, fontSize + 4f);
            rect.anchoredPosition = new Vector2(x, -y);
            return text;
        }

        /// <summary>蓝字分组线（theme horizontal_separator：x=4 蓝字 + 标签右缘后起铺点线）。
        /// 推进游标 <paramref name="y"/>。</summary>
        public static void Section(RectTransform parent, string title, float width, ref float y)
        {
            var row = new GameObject("Section_" + title, typeof(RectTransform));
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, 13f);
            rect.anchoredPosition = new Vector2(0f, -y);

            TextMeshProUGUI label = Label(rect, title, UiSkin.Font.Tiny,
                PixelSkin.Theme.SeparatorLabel, TextAlignmentOptions.Left);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(200f, 13f);
            labelRect.anchoredPosition = new Vector2(AseLayout.Px(AseLayout.SeparatorTextX), 0f);

            float lineX = AseLayout.Px(AseLayout.SeparatorTextX) + Mathf.Ceil(label.preferredWidth)
                + AseLayout.Px(AseLayout.SeparatorBorder);
            SketchSeparator.Create(rect, "Line", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(lineX, 0f), new Vector2(width - Pad - lineX, 1f),
                SketchSeparator.Direction.Horizontal);
            y += 13f + 4f;
        }
    }
}
