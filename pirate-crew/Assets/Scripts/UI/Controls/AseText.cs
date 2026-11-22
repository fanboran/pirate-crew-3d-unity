using TMPro;
using UnityEngine;

namespace PirateCrew.UI.Controls
{
    /// <summary>
    /// 控件库文本出口：像素字档解析 + 顶点像素对齐（<see cref="UiKit.CreateText"/> 同源），
    /// 字体取正文档 FusionPixel SDF（与展示页同源）。
    ///
    /// 【为什么单列】Ase 控件族原先各自借调调试窗体壳的字体与文本摆放，把「通用控件」
    /// 与「调试窗体壳」粘在一起——控件要上生产屏，字体与文本摆放必须有中立出口；
    /// 调试窗体壳反过来委托本类，调用点不变。
    /// </summary>
    public static class AseText
    {
        /// <summary>正文档像素字体（按字号在 <see cref="UiKit.ResolvePixelFont"/> 里就近取原生档）。</summary>
        public static TMP_FontAsset HandFont => PartsGalleryPage.PixelFont();

        /// <summary>文本件：禁换行、不接射线（控件内的说明文字不抢命中）。</summary>
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
            UiKit.PlaceTopLeft(text.rectTransform, x, y, new Vector2(w, fontSize + 4f));
            return text;
        }

        /// <summary>字高（= 源码 <c>textHeight()</c>：字体 ascent→descent 跨度）。
        /// 像素字体按原生档取 lineHeight；量不到时回落字号 + 2。</summary>
        public static float TextHeight(int fontSize)
        {
            TMP_FontAsset font = UiKit.ResolvePixelFont(fontSize);
            if (font != null)
            {
                var info = font.faceInfo;
                if (info.pointSize > 0f)
                {
                    float h = info.lineHeight * fontSize / info.pointSize;
                    if (h > 0f)
                        return Mathf.Ceil(h);
                }
            }
            return fontSize + 2f;
        }
    }
}
