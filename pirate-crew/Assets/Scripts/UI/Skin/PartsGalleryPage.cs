using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// **Aseprite dark 直切件陈列廊（实机）**：theme.xml &lt;parts&gt; **全表**部件的运行时展示页
    /// （创始人 2026-09-25 裁决「全部几百个组件全部移动过来，即便本端没必要有的」）。
    ///
    /// 【数据驱动零硬编码】不维护任何件清单——直接枚举 <see cref="PixelSkinAsset"/> 的
    /// aseParts/asePartNames/asePartFamilies 三平行数组（烘焙器按 theme.xml 全表 + 家族序填，
    /// 见 BeveledPixelSpriteBuilder.GenerateAtlas）。theme 加件重烘焙后本页自动长出来。
    ///
    /// 【版式（行业组件画廊/storybook 惯例）】按家族分面板（窗体/按钮/时间轴/光标…），
    /// 面板 = 蓝字分组线（theme horizontal_separator 语法）+ 等宽格子流；每格 =
    /// 件按**原生尺寸 ×1** 展示（Sliced 件九宫格原尺寸渲染 = 原件）+ id 标签（两行截断）
    /// + 尺寸/九宫标注。挂接在 <see cref="UiShowcaseBoot"/> 的滚动 content 里、
    /// 旧演示页（<see cref="PixelShowcasePage"/>）下方。
    /// </summary>
    public static class PartsGalleryPage
    {
        /// <summary>格子流：8 列 × 114 宽 + 2 缝（960 画布扣右侧滚动条 10 与左右边距 12）。</summary>
        const float MarginX = 12f;
        /// <summary>格子流基准列数（Build 按可用宽自适应，本值只作文档参照）。</summary>
        const int Columns = 8;
        const float CellW = 114f;
        const float CellH = 64f;
        const float CellGap = 2f;

        /// <summary>格内三段：件图区（上 36）+ id 两行（中 18）+ 尺寸行（下 10）。</summary>
        const float ImageZoneH = 36f;
        const float IdZoneH = 18f;

        /// <summary>
        /// 建陈列廊到 <paramref name="content"/> 下，从 <paramref name="topOffset"/>（画布单位，
        /// 自 content 顶向下）起排。返回新增内容高（画布单位）。
        /// <paramref name="width"/> = 可用内容宽（列数自适应：展示场景全宽 926 / 调试窗窄幅皆可）。
        /// </summary>
        public static float Build(RectTransform content, float topOffset, float width = 926f)
        {
            int columns = Mathf.Max(4, Mathf.FloorToInt((width - MarginX * 2f + CellGap) / (CellW + CellGap)));
            float galleryW = MarginX * 2f + columns * CellW + (columns - 1) * CellGap;

            PixelSkinAsset a = PixelSkin.Asset;
            if (a == null || a.aseParts == null || a.asePartNames == null || a.asePartFamilies == null
                || a.aseParts.Length == 0
                || a.aseParts.Length != a.asePartNames.Length
                || a.aseParts.Length != a.asePartFamilies.Length)
            {
                Debug.LogError("[PartsGalleryPage] 图集直切件表缺失或不对齐——先重烘焙（PirateCrew/UI/重烘焙 Beveled Pixel 九宫格）。");
                return 0f;
            }

            float y = PageHeader(content, topOffset, a.aseParts.Length, galleryW);

            // 家族面板：数组序 = 家族序（烘焙器排好），连续同族一段
            int n = a.aseParts.Length;
            int familyStart = 0;
            for (int i = 1; i <= n; i++)
            {
                if (i == n || a.asePartFamilies[i] != a.asePartFamilies[familyStart])
                {
                    y = FamilyPanel(content, y, a.asePartFamilies[familyStart], a, familyStart, i - familyStart, columns, galleryW);
                    familyStart = i;
                }
            }
            return y - topOffset;
        }

        static float PageHeader(RectTransform content, float y, int total, float galleryW)
        {
            TextMeshProUGUI title = UiKit.CreateText("GalleryTitle", content,
                "Aseprite dark 直切件陈列廊（全量 " + total + " 件）",
                UiSkin.Font.Body, TextAlignmentOptions.TopLeft, PixelSkin.Theme.Text, null);
            Place(title.rectTransform, MarginX, y, galleryW, 14f);
            TextMeshProUGUI sub = UiKit.CreateText("GallerySub", content,
                "theme.xml <parts> 全表 · 原生尺寸 ×1 · 按家族分组 · 九宫件标注切片",
                UiSkin.Font.Tiny, TextAlignmentOptions.TopLeft, PixelSkin.Theme.StatusText, null);
            Place(sub.rectTransform, MarginX, y + 14f, galleryW, 10f);
            return y + 30f;
        }

        static float FamilyPanel(RectTransform content, float y, string family,
            PixelSkinAsset a, int start, int count, int columns, float galleryW)
        {
            y = FamilyHeader(content, y, family, count, galleryW);
            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                float x = MarginX + col * (CellW + CellGap);
                Cell(content, x, y + row * (CellH + CellGap), a.aseParts[start + i], a.asePartNames[start + i]);
            }
            int rows = (count + columns - 1) / columns;
            return y + rows * CellH + (rows - 1) * CellGap + 12f;
        }

        /// <summary>蓝字分组线（theme horizontal_separator 语法：x=4 蓝字 + 标签右缘后起铺的点线）。</summary>
        static float FamilyHeader(RectTransform content, float y, string family, int count, float galleryW)
        {
            RectTransform row = UiKit.CreateRect("Family_" + family, content);
            TopLeft(row);
            row.sizeDelta = new Vector2(galleryW, 13f);
            row.anchoredPosition = new Vector2(0f, -y);

            TextMeshProUGUI label = UiKit.CreateText("Label", row, family + " · " + count + " 件",
                UiSkin.Font.Tiny, TextAlignmentOptions.Left, PixelSkin.Theme.SeparatorLabel, null);
            Place(label.rectTransform, 4f, 0f, 160f, 13f);

            float lineX = 4f + Mathf.Ceil(label.preferredWidth) + 2f;
            SketchSeparator.Create(row, "Line", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(lineX, 0f), new Vector2(galleryW - lineX, 1f),
                SketchSeparator.Direction.Horizontal);
            return y + 13f + 4f;
        }

        static void Cell(RectTransform content, float x, float y, Sprite sprite, string id)
        {
            RectTransform cell = UiKit.CreateRect("Part_" + id, content);
            TopLeft(cell);
            cell.sizeDelta = new Vector2(CellW, CellH);
            cell.anchoredPosition = new Vector2(x, -y);

            var bg = cell.gameObject.AddComponent<Image>();
            bg.color = PixelSkin.Theme.Background;   // theme listitem_normal_face 纯色卡底
            bg.raycastTarget = false;

            float w = 0f, h = 0f;
            bool sliced = false;
            if (sprite != null)
            {
                w = Mathf.RoundToInt(sprite.rect.width);
                h = Mathf.RoundToInt(sprite.rect.height);
                sliced = sprite.border.sqrMagnitude > 0f;

                RectTransform art = UiKit.CreateRect("Art", cell);
                TopLeft(art);
                // 原生尺寸居中（件最大 32×32 < 图区）；九宫件按原尺寸 Sliced = 原件本身
                art.sizeDelta = new Vector2(w, h);
                art.anchoredPosition = new Vector2(Mathf.Round((CellW - w) * 0.5f), 2f);
                var image = art.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                image.pixelsPerUnitMultiplier = 1f;
                image.color = Color.white;          // 像素件禁止乘色
                image.raycastTarget = false;
            }

            TextMeshProUGUI idLabel = UiKit.CreateText("Id", cell, id,
                UiSkin.Font.Tiny, TextAlignmentOptions.TopLeft, PixelSkin.Theme.Text, null);
            Place(idLabel.rectTransform, 2f, ImageZoneH, CellW - 4f, IdZoneH);
            idLabel.enableWordWrapping = true;
            idLabel.overflowMode = TextOverflowModes.Ellipsis;

            TextMeshProUGUI sizeLabel = UiKit.CreateText("Size", cell,
                sprite == null ? "缺件" : w + "×" + h + (sliced ? " 九宫" : string.Empty),
                UiSkin.Font.Tiny, TextAlignmentOptions.TopLeft, PixelSkin.Theme.StatusText, null);
            Place(sizeLabel.rectTransform, 2f, ImageZoneH + IdZoneH, CellW - 4f, 10f);
            sizeLabel.enableWordWrapping = false;
        }

        static void TopLeft(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        }

        static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            TopLeft(rect);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, -y);
        }
    }
}
