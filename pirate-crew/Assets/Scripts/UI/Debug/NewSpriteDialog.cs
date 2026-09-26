using PirateCrew.UI.Stick;
using UnityEngine;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite「New Sprite」对话框复刻——**以库为源**，逐项照
    /// external/aseprite-ref/data/widgets/new_sprite.xml 的声明落（不照截图目测）：
    ///
    /// <code>
    /// window(help=new-sprite → ？钮) ⊃ vbox {
    ///   separator "Size:"；grid 2 列 { label+entry(px 后缀) ×2 }
    ///   separator "Color Mode:"；buttonset 3 列（文字上/图标下变体，icon_rgb 等 16×16 原件）
    ///   separator "Background:"；buttonset 3 列（icon_transparent/white/black）
    ///   check "Advanced Options"；vbox advanced { "Pixel Aspect Ratio:" + combobox }
    ///   hbox { 弹性空位；homogeneous { OK(60) Cancel(60) } } —— 等宽钮对**靠右**，OK 在左
    /// } }
    /// </code>
    ///
    /// 文案取 data/strings/en.ini [new_sprite]（& 助记符剥掉）；件语义件工厂
    /// (<see cref="AseWidgetKit"/>) 与图文选项块 (<see cref="SketchButtonSetIcon"/>) 承担，
    /// 本文件只剩布局声明。已知偏差（本端字体纪律）：字用 FusionPixel 位图档，非
    /// Aseprite Mini 原字体。
    /// </summary>
    public static class NewSpriteDialog
    {
        const float W = 208f;
        const float EntryX = 76f;
        const float EntryW = 96f;
        const float IconItemH = 36f;        // 3 边 + 8 字 + 2 缝 + 16 图标 + 1 缝 + 5 底边
        const float AdvancedH = 36f;        // 标签 16 + 缝 2 + 组合框 14 + 缝 4
        const float CollapsedH = 228f;

        public static RectTransform Build(Transform overlay, Vector2 topLeft)
        {
            RectTransform window = DebugWindowKit.CreateWindow(overlay, "NewSpriteDialog",
                "New Sprite", topLeft, new Vector2(W, CollapsedH), helpButton: true);

            float y = DebugWindowKit.ContentTop;
            float inner = W - DebugWindowKit.Pad * 2f;

            // ---- separator "Size:" + grid 2 列（标签右对齐 + expr 带 px 后缀）----
            DebugWindowKit.Section(window, "Size:", inner, ref y);
            NumberRow(window, "Width:", "32", y);
            NumberRow(window, "Height:", "32", y + 18f);
            y += 36f;

            // ---- Color Mode / Background 三连（图文选项块，icon 族 16×16 原件）----
            DebugWindowKit.Section(window, "Color Mode:", inner, ref y);
            y = IconTrio(window, y, new[] { "RGBA", "Grayscale", "Indexed" },
                new[] { "icon_rgb", "icon_grayscale", "icon_indexed" });
            DebugWindowKit.Section(window, "Background:", inner, ref y);
            y = IconTrio(window, y, new[] { "Transparent", "White", "Black" },
                new[] { "icon_transparent", "icon_white", "icon_black" });

            // ---- Advanced Options：勾选展开比例组（xml check → vbox advanced 联动）。
            // 流：check 行 y..y+16 → advanced 盒与 OK/Cancel 同起 y+18（展开时按钮整体下移）。
            RectTransform advanced = AdvancedBox(window, y + 18f);
            advanced.gameObject.SetActive(false);
            RectTransform ok = DialogButton(window, "OK", y + 18f, W - DebugWindowKit.Pad - 124f);
            RectTransform cancel = DialogButton(window, "Cancel", y + 18f, W - DebugWindowKit.Pad - 60f);
            AseWidgetKit.CheckRow(window, "Advanced Options", 8f, y, false, on =>
            {
                advanced.gameObject.SetActive(on);
                float shift = on ? AdvancedH : -AdvancedH;
                ok.anchoredPosition += new Vector2(0f, -shift);
                cancel.anchoredPosition += new Vector2(0f, -shift);
                window.sizeDelta = new Vector2(W, CollapsedH + (on ? AdvancedH : 0f));
            });
            return window;
        }

        // ------------------------------------------------------------------

        static void NumberRow(RectTransform window, string label, string value, float y)
        {
            DebugWindowKit.PlaceLabel(window, label, UiSkin.Font.Tiny, PixelSkin.Theme.Text,
                8f, y + 4f, EntryX - 12f, TMPro.TextAlignmentOptions.Right);
            AseWidgetKit.SunkenEntry(window, label + "Field", EntryX, y, EntryW, "32",
                suffix: "px");
        }

        /// <summary>三连图文选项块：等宽三列、互斥单选（当前值换 hot 件）。</summary>
        static float IconTrio(RectTransform window, float y, string[] labels, string[] iconParts)
        {
            float inner = W - DebugWindowKit.Pad * 2f;
            float w = Mathf.Floor((inner - 4f) / 3f);
            var items = new SketchButtonSetIcon[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                items[i] = SketchButtonSetIcon.Create(window, "Opt_" + labels[i], labels[i],
                    iconParts[i], new Vector2(DebugWindowKit.Pad + i * (w + 2f), -y),
                    new Vector2(w, IconItemH));
                int captured = i;
                items[i].onClick.AddListener(() =>
                {
                    for (int j = 0; j < items.Length; j++)
                        items[j].Active = j == captured;
                });
            }
            items[0].Active = true;
            return y + IconItemH + 4f;
        }

        /// <summary>Advanced 组（xml vbox id=advanced）：Pixel Aspect Ratio 标签 + 三项组合框。</summary>
        static RectTransform AdvancedBox(RectTransform window, float y)
        {
            RectTransform box = UiKit.CreateRect("Advanced", window);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
            box.anchoredPosition = new Vector2(0f, -y);
            box.sizeDelta = new Vector2(W, AdvancedH);

            DebugWindowKit.PlaceLabel(box, "Pixel Aspect Ratio:", UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, 8f, 0f, 120f);
            AseWidgetKit.ComboBox(box, "RatioCombo", 8f, 18f, 150f, new[]
                { "Square Pixels (1:1)", "Double-wide Pixels (2:1)", "Double-high Pixels (1:2)" }, 0);
            return box;
        }

        /// <summary>OK/Cancel：等宽 60、贴右缘、OK 在左（xml: expansive 空位 + homogeneous 盒）。</summary>
        static RectTransform DialogButton(RectTransform window, string label, float y, float x)
        {
            SketchButton button = SketchButton.Create(window, "Dialog_" + label,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(60f, 16f), DebugWindowKit.HandFont, label, UiSkin.Font.Tiny);
            button.onClick.AddListener(() => window.gameObject.SetActive(false));
            return (RectTransform)button.transform;
        }
    }
}
