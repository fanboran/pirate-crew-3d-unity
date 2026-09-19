using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 统一 UI 控件工厂（手绘涂鸦皮肤的唯一构建入口）。
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
    /// 皮肤来源：<see cref="SketchSkin"/>（game-2 手绘涂鸦沸腾贴图，烘焙管线
    /// tools/sketch_ui/）+ <see cref="SketchBoil"/> 沸腾驱动；tint 槽（pip/ring/fill/cell）
    /// 白底墨边，运行时 Image.color 乘色。符号图标：<see cref="UiGlyphs"/>。
    /// 本类触碰 UGUI（ECall），只可在 Unity 里用。
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
        // Sprite 来源（手绘涂鸦贴图：Editor 与播放器都走 Resources/UI/Sketch/ 同源）
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        /// <summary>Editor 下从 Resources 取烘焙手绘贴图（PNG 是持久资产，场景序列化
        /// 引用不丢；未烘焙时返回 null 由调用方告警）。</summary>
        static Sprite SkinSprite(CartoonSpriteFactory.Shape shape)
        {
            return Resources.Load<Sprite>("UI/Sketch/" + SketchSkin.SlotOfShape(shape) + "_f0");
        }

        static Sprite GlyphSprite(UiGlyphs.Glyph glyph)
        {
            Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Sprites/UI/Glyph_" + glyph + ".png");
            return sprite != null ? sprite : UiGlyphs.Get(glyph);
        }
#else
        static Sprite SkinSprite(CartoonSpriteFactory.Shape shape) => SketchSkin.Frame(SkinSlot(shape), 0);
        static Sprite GlyphSprite(UiGlyphs.Glyph glyph) => UiGlyphs.Get(glyph);
#endif

        static string SkinSlot(CartoonSpriteFactory.Shape shape) => SketchSkin.SlotOfShape(shape);

        // ------------------------------------------------------------------
        // 图形件（tintable 染色）
        // ------------------------------------------------------------------

        /// <summary>建 tintable 图形（手绘 tint 槽 × Image.color 染色，带沸腾）。</summary>
        public static Image CreateTinted(string name, Transform parent, CartoonSpriteFactory.Shape shape, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SkinSprite(shape);
            bool simple = shape == CartoonSpriteFactory.Shape.Ring
                || shape == CartoonSpriteFactory.Shape.Circle;
            image.type = simple ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            AddBoil(rect, SkinSlot(shape));
            return image;
        }

        /// <summary>挂沸腾驱动（帧缺失时 SketchSkin 告警一次，Image 保留 f0/null 由导入器兜底）。</summary>
        static void AddBoil(RectTransform rect, string slot, string[] stateSlots = null)
        {
            var boil = rect.gameObject.AddComponent<SketchBoil>();
            boil.Slot = slot;
            if (stateSlots != null)
                boil.BindStates(stateSlots);
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

        /// <summary>建深底手绘面板（panel 槽九砖平铺 + 沸腾；承载暖白 / 金 / 彩色件）。
        /// 根上没有 Image——面板外观由 <see cref="Sketch9Slice"/> 的九个子砖承担，
        /// 换槽用 <c>GetComponent&lt;Sketch9Slice&gt;().SetSlot(...)</c>。</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var slice = rect.gameObject.AddComponent<Sketch9Slice>();
            slice.Slot = "panel";
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
        /// 按钮 ColorBlock——手绘语言下状态反馈靠贴图切换（<see cref="SketchBoil"/> 四槽），
        /// tint 全白只留按压/禁用的极轻明度信号（避免乘色把手绘边框染脏）。
        /// </summary>
        public static ColorBlock FourState(Color baseColor)
        {
            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = Color.white,
                pressedColor = new Color(0.92f, 0.92f, 0.94f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.55f),
                colorMultiplier = 1f,
                fadeDuration = UiSkin.ButtonFadeSeconds,
            };
        }

        /// <summary>chip 底色 → 手绘按钮状态槽组（Gold=金实底主按钮 / Danger=危险 / TextOnInk=纸面 / 其余常规）。</summary>
        public static string[] ButtonStateSlots(Color chipColor)
        {
            if (chipColor == UiSkin.Gold)
                return SketchSkin.BtnPrimary;
            if (chipColor == UiSkin.Danger)
                return SketchSkin.Danger;
            if (chipColor == UiSkin.TextOnInk)
                return SketchSkin.Ink;
            return SketchSkin.Btn;
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

            string[] stateSlots = ButtonStateSlots(chipColor);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SketchSkin.Frame(stateSlots[0], 0);
            image.type = Image.Type.Sliced;
            image.color = Color.white;   // 手绘四态靠贴图切换，底色烘死，不再叠乘色

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = FourState(chipColor);
            rect.gameObject.AddComponent<UiPressSink>();
            AddBoil(rect, stateSlots[0], stateSlots);

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

            string[] stateSlots = ButtonStateSlots(chipColor);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SketchSkin.Frame(stateSlots[0], 0);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = FourState(chipColor);
            rect.gameObject.AddComponent<UiPressSink>();
            AddBoil(rect, stateSlots[0], stateSlots);

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

        // ------------------------------------------------------------------
        // 按钮变体表（对齐 game-2 sketch_style._build_variants 的表驱动思路：
        // 调用点只报"档位"，底色/字色组合的唯一出处在这里，杜绝各处手配漂移）
        // ------------------------------------------------------------------

        /// <summary>按钮档位（对齐隔壁 SketchButton 五 kind）：
        /// Primary=金色主行动点；Dark=深底常规件；Danger=破坏性动作；
        /// Accent=金叠加选中态；Paper=纸面亮背景形态（主菜单暖金天空用）。</summary>
        public enum ButtonKind
        {
            /// <summary>主行动点（投掷 / 继续 / 再来一局 / 确认）：金底深字。全屏同时只该有一枚高亮。</summary>
            Primary,

            /// <summary>常规件（结束回合 / 取消 / 次按钮）：手绘深底暖白字。</summary>
            Dark,

            /// <summary>破坏性动作（返回主菜单 / 放弃）：酒红底暖白字。</summary>
            Danger,

            /// <summary>强调态（选中 / 激活）：金叠加底金边金字。</summary>
            Accent,

            /// <summary>纸面形态（亮背景上的"纸签"）：奶油纸底深墨字。</summary>
            Paper,
        }

        /// <summary>档位 → (chip 底色→槽推断, 字/图标色)。强调色纪律：<see cref="UiSkin.Gold"/> 只在 Primary/Accent 出现。</summary>
        public static (Color chip, Color label) ButtonVariant(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Primary: return (UiSkin.Gold, UiSkin.InkOnGold);
                case ButtonKind.Dark: return (UiSkin.InkSoft, UiSkin.TextOnInk);
                case ButtonKind.Danger: return (UiSkin.Danger, UiSkin.TextOnInk);
                case ButtonKind.Accent: return (UiSkin.Gold, UiSkin.Gold);
                case ButtonKind.Paper: return (UiSkin.TextOnInk, UiSkin.InkOnGold);
                default: return (UiSkin.InkSoft, UiSkin.TextOnInk);
            }
        }

        /// <summary>档位 → 状态槽组（kind 直查——Accent 与 Primary 同为金字面，
        /// 走 chip 色推断会撞槽，故 kind 路径不经 <see cref="ButtonStateSlots"/>）。</summary>
        public static string[] KindStateSlots(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Primary: return SketchSkin.BtnPrimary;
                case ButtonKind.Danger: return SketchSkin.Danger;
                case ButtonKind.Accent: return SketchSkin.Accent;
                case ButtonKind.Paper: return SketchSkin.Ink;
                default: return SketchSkin.Btn;
            }
        }

        /// <summary>
        /// 图文按钮（图标在左、文字跟右）——模式/动作钮的统一长相，档位色走
        /// <see cref="ButtonVariant"/>；BattleHud 的投掷/结束回合与各模态按钮共用。
        /// position 相对父容器中心（anchor/pivot 0.5,0.5）。
        /// </summary>
        public static Button ActionButton(string name, Transform parent, UiGlyphs.Glyph glyph,
            string label, ButtonKind kind, Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font,
            bool withIcon = true)
        {
            (Color chipColor, Color labelColor) = ButtonVariant(kind);
            string[] stateSlots = KindStateSlots(kind);
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SketchSkin.Frame(stateSlots[0], 0);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = FourState(chipColor);
            rect.gameObject.AddComponent<UiPressSink>();
            AddBoil(rect, stateSlots[0], stateSlots);

            if (withIcon)
            {
                Image icon = CreateGlyph("Icon", rect, glyph, labelColor);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                icon.rectTransform.sizeDelta = new Vector2(24f, 24f);
                icon.rectTransform.anchoredPosition = new Vector2(12f, 0f);

                TextMeshProUGUI text = CreateText("Text", rect, label, UiSkin.Font.Body,
                    TextAlignmentOptions.MidlineLeft, labelColor, font, raycast: false);
                text.enableWordWrapping = false;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                text.rectTransform.pivot = new Vector2(0f, 0.5f);
                text.rectTransform.sizeDelta = new Vector2(size.x - 48f, size.y);
                text.rectTransform.anchoredPosition = new Vector2(42f, 0f);
            }
            else
            {
                TextMeshProUGUI text = CreateText("Text", rect, label, UiSkin.Font.Body,
                    TextAlignmentOptions.Center, labelColor, font, raycast: false);
                text.enableWordWrapping = false;
                Stretch(text.rectTransform, 10f);
            }

            return button;
        }

        // ------------------------------------------------------------------
        // 运行时字体（播放器 / Gallery：字体资产经 Resources 打进包）
        // ------------------------------------------------------------------

        /// <summary>运行时字体档。<see cref="MenuUiBuilder"/> 是 Editor 类，运行时改从
        /// Resources/Fonts/ 取同一批 SDF 资产（由 FontAssetBuilder 复制入 Resources）。</summary>
        public enum RuntimeFontKind
        {
            /// <summary>标题手写体。</summary>
            Title,

            /// <summary>正文。</summary>
            Body,

            /// <summary>次级说明。</summary>
            Secondary,
        }

        /// <summary>取运行时可用的字体资产；Resources 缺失（未跑 FontAssetBuilder）时返回
        /// null 交 TMP 默认兜底并告警一次。</summary>
        public static TMP_FontAsset RuntimeFont(RuntimeFontKind kind)
        {
            // 隔壁纪律「文字统一 StickHand」：HUD / 按钮全部手写体（笔画等粗可读性高，
            // 用户 2026-09-20 裁决可读性差后全面切换）；霞鹜文楷保留给未来长文场景。
            string path = "Fonts/StickHand-Regular SDF";
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(path);
            if (font == null)
                Debug.LogWarning("[UiKit] Resources/" + path + " 缺失（跑 PirateCrew/资产/烘焙字体 后可用），回落 TMP 默认字体");
            return font;
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
            AddBoil(root, "progress_bg");

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
