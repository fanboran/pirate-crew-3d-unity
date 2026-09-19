using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 统一 UI 控件工厂（多彩卡通皮肤的唯一构建入口）。
    ///
    /// 【为什么需要它】此前 Editor 玻璃构建器（MenuUiBuilder）与运行时构建器
    /// （M3UiBuilder/UiSprites 木纸系）双栈并行，运行时程序集拿不到新皮肤与字号映射，
    /// 同屏两种风格——这是"简陋感"的主要技术根源。本类落在运行时程序集，
    /// Editor 装配与运行时动态件**走同一套工厂**，皮肤 / 字号 / 动效从此单轨。
    ///
    /// 【用法纪律】
    ///   · 字号一律传 <see cref="UiSkin.Font"/> 档位；
    ///   · 颜色一律传 <see cref="UiSkin"/> / <see cref="UiTheme"/> Token；
    ///   · 可点击件用 <see cref="ChipButton"/> / <see cref="IconButton"/>（自带四态 + 按压下沉）。
    ///
    /// 皮肤来源：<see cref="CartoonSpriteFactory"/>（平色九宫格，tintable 染色）；
    /// 符号图标：<see cref="UiGlyphs"/>。本类触碰 UGUI（ECall），只可在 Unity 里用。
    /// </summary>
    public static class UiKit
    {
        // ------------------------------------------------------------------
        // 布局原语
        // ------------------------------------------------------------------

        /// <summary>建一个带 RectTransform 的 UI 对象。</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>铺满父容器（可选内边距）。</summary>
        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>按锚点 + 尺寸摆放（anchoredPosition 相对锚点，pivot = anchor）。</summary>
        public static void SetAnchored(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        // ------------------------------------------------------------------
        // 文本（字号 = UiSkin.Font 单轨）
        // ------------------------------------------------------------------

        /// <summary>建 TMP 文本（字号请传 <see cref="UiSkin.Font"/> 档位常量）。</summary>
        public static TextMeshProUGUI CreateText(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font, bool raycast = false)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            if (font != null)
                text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = raycast;
            return text;
        }

        // ------------------------------------------------------------------
        // Sprite 来源（双轨同源：Editor 装配场景必须引用持久 PNG，运行时用内存 Sprite）
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        /// <summary>Editor 下从 <c>Assets/Art/Sprites/UI/</c> 取烘焙 PNG（场景序列化不丢引用）；
        /// 资产缺失（未跑 <c>UiSkinAssetBaker</c>）时回落内存 Sprite 并保持可用。</summary>
        static Sprite SkinSprite(CartoonSpriteFactory.Shape shape)
        {
            Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Sprites/UI/Cartoon_" + shape + ".png");
            return sprite != null ? sprite : CartoonSpriteFactory.Get(shape);
        }

        static Sprite GlyphSprite(UiGlyphs.Glyph glyph)
        {
            Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Sprites/UI/Glyph_" + glyph + ".png");
            return sprite != null ? sprite : UiGlyphs.Get(glyph);
        }
#else
        static Sprite SkinSprite(CartoonSpriteFactory.Shape shape) => CartoonSpriteFactory.Get(shape);
        static Sprite GlyphSprite(UiGlyphs.Glyph glyph) => UiGlyphs.Get(glyph);
#endif

        // ------------------------------------------------------------------
        // 图形件（tintable 染色）
        // ------------------------------------------------------------------

        /// <summary>建 tintable 图形（<see cref="CartoonSpriteFactory"/> 皮肤 × Image.color 染色）。</summary>
        public static Image CreateTinted(string name, Transform parent, CartoonSpriteFactory.Shape shape, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SkinSprite(shape);
            image.type = shape == CartoonSpriteFactory.Shape.Ring
                || shape == CartoonSpriteFactory.Shape.Circle
                ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>建符号图标（<see cref="UiGlyphs"/> × 染色；Type.Simple，不切片）。</summary>
        public static Image CreateGlyph(string name, Transform parent, UiGlyphs.Glyph glyph, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = GlyphSprite(glyph);
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>建深底面板（固定色 InkDeep + 亮边线；承载暖白 / 金 / 彩色件）。</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SkinSprite(CartoonSpriteFactory.Shape.PanelInk);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>全屏压暗遮罩（模态语义：raycastTarget 开着挡底下的点击）。</summary>
        public static RectTransform CreateDimOverlay(string name, Transform parent, float alpha = 0.62f)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, alpha);
            image.raycastTarget = true;
            return rect;
        }

        // ------------------------------------------------------------------
        // 按钮（四态乘色 + 按压下沉）
        // ------------------------------------------------------------------

        /// <summary>
        /// 按钮 ColorBlock（tint 底的乘色四态）：hover 提亮 8%、pressed 压暗 10% +
        /// alpha 抬满（把"按实"读出来）、disabled 去饱和压半透明。
        /// 数学沿用 <see cref="UiTheme.Hover"/>/<see cref="UiTheme.Pressed"/>/<see cref="UiTheme.Disabled"/>。
        /// </summary>
        public static ColorBlock FourState(Color baseColor)
        {
            var block = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = Mult(UiTheme.Hover(baseColor), 1f),
                pressedColor = UiTheme.Pressed(baseColor),
                selectedColor = Color.white,
                disabledColor = Mult(UiTheme.Disabled(Color.white), 1f),
                colorMultiplier = 1f,
                fadeDuration = UiSkin.ButtonFadeSeconds,
            };
            // hover 白×亮化版：直接用"基色提亮"当乘色（白底 Image.color=baseColor 时等效于提亮基色）。
            block.highlightedColor = new Color(
                Mathf.Min(1f, 1.12f), Mathf.Min(1f, 1.12f), Mathf.Min(1f, 1.14f), 1f);
            block.pressedColor = new Color(0.82f, 0.82f, 0.84f, 1f);
            return block;
        }

        static Color Mult(Color color, float multiplier)
        {
            return new Color(color.r * multiplier, color.g * multiplier, color.b * multiplier, color.a);
        }

        /// <summary>建文字按钮（彩色 chip 底 + 居中文字）。</summary>
        public static Button ChipButton(string name, Transform parent, string label, Vector2 anchoredPosition,
            Vector2 size, Color chipColor, Color labelColor, TMP_FontAsset font,
            int fontSize = UiSkin.Font.Body, CartoonSpriteFactory.Shape shape = CartoonSpriteFactory.Shape.Chip)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SkinSprite(CartoonSpriteFactory.Shape.Chip);
            image.type = Image.Type.Sliced;
            image.color = chipColor;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = FourState(chipColor);
            rect.gameObject.AddComponent<UiPressSink>();

            TextMeshProUGUI text = CreateText("Text", rect, label, fontSize,
                TextAlignmentOptions.Center, labelColor, font, raycast: false);
            Stretch(text.rectTransform);
            return button;
        }

        /// <summary>
        /// 建图标按钮（彩色 chip 底 + 符号图标 + 可选快捷键角标）。
        /// 图标优先原则的落点：动作钮（暂停 / 返回 / 投掷 / 结束回合 / 模式切换）全走这里。
        /// </summary>
        public static Button IconButton(string name, Transform parent, UiGlyphs.Glyph glyph,
            Vector2 anchoredPosition, Vector2 size, Color chipColor, Color glyphColor,
            string hotkey = null, Color? hotkeyColor = null)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SkinSprite(CartoonSpriteFactory.Shape.Chip);
            image.type = Image.Type.Sliced;
            image.color = chipColor;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = FourState(chipColor);
            rect.gameObject.AddComponent<UiPressSink>();

            Image icon = CreateGlyph("Icon", rect, glyph, glyphColor);
            float inset = Mathf.Min(size.x, size.y) * 0.22f;
            Stretch(icon.rectTransform, inset);

            if (!string.IsNullOrEmpty(hotkey))
            {
                // 快捷键角标（右上角小字；默认给 chip 反相色，可显式覆盖）。
                TextMeshProUGUI key = CreateText("Hotkey", rect, hotkey, UiSkin.Font.Tiny,
                    TextAlignmentOptions.Center, hotkeyColor ?? InverseOf(chipColor), null);
                key.enableWordWrapping = false;
                key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(1f, 1f);
                key.rectTransform.pivot = new Vector2(1f, 1f);
                key.rectTransform.anchoredPosition = new Vector2(-3f, -1f);
                key.rectTransform.sizeDelta = new Vector2(16f, 14f);
            }

            return button;
        }

        /// <summary>chip 的反相字色（亮底给深墨、深底给暖白——角标 / 覆盖文字的便捷取色）。</summary>
        public static Color InverseOf(Color chipColor)
        {
            return UiSkin.ContrastRatio(chipColor, UiSkin.InkOnGold) >= UiSkin.ContrastRatio(chipColor, UiSkin.TextOnInk)
                ? UiSkin.InkOnGold
                : UiSkin.TextOnInk;
        }

        // ------------------------------------------------------------------
        // 血条（凹槽 + 白色 damage ghost + 主填充）
        // ------------------------------------------------------------------

        /// <summary>血条视图（驱动走 <see cref="UiMotion.SetFillPairTarget"/>）。</summary>
        public sealed class BarView
        {
            public RectTransform Root;
            public Image Track;
            public Image Ghost;
            public Image Fill;
        }

        /// <summary>
        /// 建双层血条：凹槽底 → 白色 ghost（受击残影，垫在下）→ 主填充（队色/职业色，在上）。
        /// 两个填充都从左侧 anchorMax.x 表达比例。
        /// </summary>
        public static BarView CreateBar(string name, Transform parent, Vector2 anchoredPosition,
            Vector2 size, Color fillColor)
        {
            RectTransform root = CreateRect(name, parent);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = size;
            root.anchoredPosition = anchoredPosition;

            var track = root.gameObject.AddComponent<Image>();
            track.sprite = SkinSprite(CartoonSpriteFactory.Shape.BarTrack);
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;

            Image ghost = CreateTinted("Ghost", root, CartoonSpriteFactory.Shape.Pill, UiSkin.DamageGhost);
            Stretch(ghost.rectTransform);

            Image fill = CreateTinted("Fill", root, CartoonSpriteFactory.Shape.Pill, fillColor);
            Stretch(fill.rectTransform);

            return new BarView { Root = root, Track = track, Ghost = ghost, Fill = fill };
        }

        // ------------------------------------------------------------------
        // 模态（Dim + 深底卡片；入场 pop / 出场淡出由 UiMotion 驱动）
        // ------------------------------------------------------------------

        /// <summary>模态视图。</summary>
        public sealed class ModalView
        {
            public GameObject Root;
            public RectTransform Card;
        }

        /// <summary>建模态（全屏 Stretch 的根 + Dim 遮罩 + 居中卡片；默认隐藏）。</summary>
        public static ModalView CreateModal(string name, Transform parent, Vector2 cardSize)
        {
            RectTransform root = CreateRect(name, parent);
            Stretch(root);
            CreateDimOverlay("DimOverlay", root);

            RectTransform card = CreatePanel("Card", root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, cardSize);

            root.gameObject.SetActive(false);
            return new ModalView { Root = root.gameObject, Card = card };
        }
    }
}
