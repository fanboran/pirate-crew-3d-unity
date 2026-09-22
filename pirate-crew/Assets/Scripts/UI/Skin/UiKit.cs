using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 统一 UI 控件工厂（Beveled Pixel 皮肤的唯一构建入口）。
    ///
    /// 【为什么需要它】此前 Editor 玻璃构建器（MenuUiBuilder）与运行时构建器
    /// （M3UiBuilder/UiSprites 木纸系）双栈并行，运行时程序集拿不到新皮肤与字号映射，
    /// 同屏两种风格——这是"简陋感"的主要技术根源。本类落在运行时程序集，
    /// Editor 装配与运行时动态件**走同一套工厂**，皮肤 / 字号 / 动效从此单轨。
    ///
    /// 【用法纪律】
    ///   · 字号一律传 <see cref="UiSkin.Font"/> 档位；
    ///   · **皮肤一律走 <see cref="PixelSkin"/>**——像素件的明暗色阶烘死在贴图里，
    ///     所以像素件禁止 <c>Image.color</c> 乘色（乘了就把烘焙好的三档色阶压平）；
    ///   · 按钮状态反馈一律 UGUI SpriteSwap（常态/悬停/按压三张 Plate），不用 ColorBlock 乘色；
    ///     禁用态不烘黑图，靠 <see cref="UiPressSink"/> 的 CanvasGroup alpha≈0.55；
    ///   · 可点击件用 <see cref="ChipButton"/> / <see cref="IconButton"/>（自带三态换图 + 按压位移）。
    ///
    /// 文字色：<see cref="PixelSkin.TextColorOn"/> / <see cref="PixelSkin.Ink"/> /
    /// <see cref="PixelSkin.PaperWhite"/>（icon 与文字不受"禁止乘色"约束，但仍从调色板取色）。
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

        /// <summary>铺满父容器后整体错位（投影件用：stretch + <see cref="PixelSkin.ShadowOffset"/>）。</summary>
        static void StretchOffset(RectTransform rect, Vector2 offset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offset;
            rect.offsetMax = offset;
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
        // 像素件（全部走 PixelSkin 同源出口；禁止乘色）
        // ------------------------------------------------------------------

        /// <summary>建凸起块（Plate，九宫格）。像素件白贴图不乘色——色阶烘死在贴图里。</summary>
        public static Image CreatePlate(string name, Transform parent, PixelTone tone,
            PixelState state = PixelState.Normal)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(tone, state);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>建凹槽（Track，九宫格）：条状件的空槽底。</summary>
        public static Image CreateTrack(string name, Transform parent, PixelTone tone)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Track(tone);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>建填充条（Fill，九宫格）：画在 Track 内容区上的那一层。</summary>
        public static Image CreateFill(string name, Transform parent, PixelFillKind kind)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Fill(kind);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 建键盘焦点环：选中态包在控件**外沿**（比本体大 2px 外扩），默认隐藏由运行时开关。
        /// 选中反馈从此是"多一件 Focus 环"，不再是给本体乘色。
        /// </summary>
        public static Image CreateFocusRing(string name, Transform parent)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Focus;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-2f, -2f);
            rect.offsetMax = new Vector2(2f, 2f);
            image.gameObject.SetActive(false);
            return image;
        }

        /// <summary>（兼容重载）旧 tint 槽调用：**乘色机制退役**，改按 chip 色选 tone；
        /// <paramref name="shape"/> 参数已无意义（贴图由 tone 决定），保留只为不打断旧调用点。</summary>
        public static Image CreateTinted(string name, Transform parent, CartoonSpriteFactory.Shape shape, Color color)
        {
            return CreatePlate(name, parent, ToneOfChip(color));
        }

        static Image FindImage(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            return child != null ? child.GetComponent<Image>() : null;
        }

        /// <summary>
        /// 面板皮肤：确保 panel 下有 <c>Shadow</c>（兄弟序 0）+ <c>Plate</c>（兄弟序 1）两件，
        /// 返回 Plate 的 Image。**根节点自身不带 Graphic**（沿用旧九砖面板的层级口径：
        /// 外观全在孩子上，调用方往根上挂的内容天然画在面板之上）。
        ///
        /// 【为什么投影是孩子】投影必须跟着面板做位移/缩放动画，只能是孩子；又因
        /// 「父 Graphic 先于子 Graphic 绘制」，投影若与面板同体（都在根上）会盖住面板本体——
        /// 故面板本体也下放成 Plate 孩子，兄弟序保证投影在下、本体在上。
        /// 幂等：重跑装配复用同名孩子，只刷新贴图与切片。
        /// </summary>
        public static Image EnsurePanel(RectTransform panel, PixelTone tone)
        {
            Image shadow = FindImage(panel, "Shadow");
            if (shadow == null)
                shadow = CreateRect("Shadow", panel).gameObject.AddComponent<Image>();
            shadow.sprite = PixelSkin.ShadowSprite;
            shadow.type = Image.Type.Sliced;
            shadow.color = Color.white;
            shadow.raycastTarget = false;
            StretchOffset(shadow.rectTransform, PixelSkin.ShadowOffset);
            shadow.rectTransform.SetSiblingIndex(0);

            Image plate = FindImage(panel, "Plate");
            if (plate == null)
                plate = CreateRect("Plate", panel).gameObject.AddComponent<Image>();
            plate.sprite = PixelSkin.Plate(tone, PixelState.Normal);
            plate.type = Image.Type.Sliced;
            plate.color = Color.white;
            plate.raycastTarget = true;   // 面板本体挡点击（内容件画在其上，不受影响）
            Stretch(plate.rectTransform);
            plate.rectTransform.SetSiblingIndex(1);
            return plate;
        }

        // ------------------------------------------------------------------
        // 图形件（符号图标 = 唯一允许 Image.color 染色的族）
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        /// <summary>符号图标 Sprite：优先 Editor 下烘焙 PNG（持久资产引用），缺失退内存生成。</summary>
        static Sprite GlyphSprite(UiGlyphs.Glyph glyph)
        {
            Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Sprites/UI/Glyph_" + glyph + ".png");
            return sprite != null ? sprite : UiGlyphs.Get(glyph);
        }
#else
        static Sprite GlyphSprite(UiGlyphs.Glyph glyph) => UiGlyphs.Get(glyph);
#endif

        /// <summary>建符号图标（<see cref="UiGlyphs"/> × 染色；Type.Simple，不切片）。
        /// icon 不受"像素件禁止乘色"约束（无烘焙色阶），但仍从调色板取色。</summary>
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

        /// <summary>建深底像素面板（Plate(Frame) 九宫格 + 投影错位剪影；承载暖白 / 金 / 彩色件）。
        /// 根上没有 Image——面板本体是 <c>Plate</c> 孩子，投影是 <c>Shadow</c> 孩子。</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            EnsurePanel(rect, PixelTone.Frame);
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
        // 按钮（三态换图 + 按压位移 + 禁用透明）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把按钮接成像素件：常态/悬停/按压三张 Plate 走 UGUI SpriteSwap，禁用不烘黑图
        /// （<see cref="UiPressSink"/> 用 CanvasGroup alpha≈0.55 表达）。
        /// 【为什么用 SpriteSwap 而不是 ColorBlock】像素件的明暗色阶是烘死的，ColorBlock 的乘色
        /// 会把整块压成单色；贴图切换才是这套语言的正确状态机制。
        /// </summary>
        public static void ApplyPlateButton(Button button, Image image, PixelTone tone)
        {
            image.sprite = PixelSkin.Plate(tone, PixelState.Normal);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;

            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = PixelSkin.Plate(tone, PixelState.Hovered),
                pressedSprite = PixelSkin.Plate(tone, PixelState.Pressed),
                selectedSprite = PixelSkin.Plate(tone, PixelState.Hovered),
            };
            if (button.GetComponent<UiPressSink>() == null)
                button.gameObject.AddComponent<UiPressSink>();
        }

        /// <summary>chip 底色 → 像素 tone（旧 Color 乘色槽退役：只保留"选哪一档贴图"的语义）。</summary>
        public static PixelTone ToneOfChip(Color chipColor)
        {
            if (chipColor == UiSkin.Gold)
                return PixelTone.Primary;
            if (chipColor == UiSkin.Danger)
                return PixelTone.Danger;
            if (chipColor == UiSkin.Warn)
                return PixelTone.Warn;
            if (chipColor == UiSkin.TextOnInk)
                return PixelTone.Light;
            if (chipColor == UiSkin.InkDeep || chipColor == UiSkin.InkSoft)
                return PixelTone.Dense;
            return PixelTone.Light;   // btn_normal 族
        }

        /// <summary>建文字按钮（彩色 chip 底 + 居中文字）。字色由 tone 派生，不再手挑。</summary>
        public static Button ChipButton(string name, Transform parent, string label, Vector2 anchoredPosition,
            Vector2 size, Color chipColor, Color labelColor, TMP_FontAsset font,
            int fontSize = UiSkin.Font.Body, CartoonSpriteFactory.Shape shape = CartoonSpriteFactory.Shape.Chip)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            PixelTone tone = ToneOfChip(chipColor);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            ApplyPlateButton(button, image, tone);

            TextMeshProUGUI text = CreateText("Text", rect, label, fontSize,
                TextAlignmentOptions.Center, PixelSkin.TextColorOn(tone), font, raycast: false);
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

            PixelTone tone = ToneOfChip(chipColor);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            ApplyPlateButton(button, image, tone);

            Color foreground = PixelSkin.TextColorOn(tone);
            Image icon = CreateGlyph("Icon", rect, glyph, foreground);
            float inset = Mathf.Min(size.x, size.y) * 0.22f;
            Stretch(icon.rectTransform, inset);

            if (!string.IsNullOrEmpty(hotkey))
            {
                // 快捷键角标（右上角小字；默认取 chip 底上的正文字色，可显式覆盖）。
                TextMeshProUGUI key = CreateText("Hotkey", rect, hotkey, UiSkin.Font.Tiny,
                    TextAlignmentOptions.Center, hotkeyColor ?? foreground, null);
                key.enableWordWrapping = false;
                key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(1f, 1f);
                key.rectTransform.pivot = new Vector2(1f, 1f);
                key.rectTransform.anchoredPosition = new Vector2(-3f, -1f);
                key.rectTransform.sizeDelta = new Vector2(16f, 14f);
            }

            return button;
        }

        // ------------------------------------------------------------------
        // 按钮变体表（档位 → tone；底色/字色组合的唯一出处在这里，杜绝各处手配漂移）
        // ------------------------------------------------------------------

        /// <summary>按钮档位：
        /// Primary=主行动点（黄铜）；Dark=深底常规件；Danger=破坏性动作；
        /// Accent=强调（暖橙）；Paper=浅牌形态（亮背景上的"纸签"）。</summary>
        public enum ButtonKind
        {
            /// <summary>主行动点（投掷 / 继续 / 再来一局 / 确认）：黄铜底深字。全屏同时只该有一枚高亮。</summary>
            Primary,

            /// <summary>常规件（结束回合 / 取消 / 次按钮）：深底亮字。</summary>
            Dark,

            /// <summary>破坏性动作（返回主菜单 / 放弃）：红底亮字。</summary>
            Danger,

            /// <summary>强调态（选中 / 激活）：暖橙底。</summary>
            Accent,

            /// <summary>浅牌形态（亮背景上的"纸签"）：暖白底墨字。</summary>
            Paper,
        }

        /// <summary>档位 → (chip 底色→槽推断, 字/图标色)。保留给旧调用点读色，
        /// 像素按钮实际走 <see cref="ToneOfKind"/> + <see cref="PixelSkin.TextColorOn"/>。</summary>
        public static (Color chip, Color label) ButtonVariant(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Primary: return (UiSkin.Gold, UiSkin.InkOnGold);
                case ButtonKind.Dark: return (UiSkin.InkSoft, UiSkin.TextOnInk);
                case ButtonKind.Danger: return (UiSkin.Danger, UiSkin.TextOnInk);
                case ButtonKind.Accent: return (UiSkin.Warn, UiSkin.InkOnGold);
                case ButtonKind.Paper: return (UiSkin.TextOnInk, UiSkin.InkOnGold);
                default: return (UiSkin.InkSoft, UiSkin.TextOnInk);
            }
        }

        /// <summary>档位 → 像素 tone（Primary/Accent 都是强调，但 Accent 用暖橙 Warn 区分于黄铜主行动点）。</summary>
        public static PixelTone ToneOfKind(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Primary: return PixelTone.Primary;
                case ButtonKind.Danger: return PixelTone.Danger;
                case ButtonKind.Accent: return PixelTone.Warn;
                case ButtonKind.Paper: return PixelTone.Light;
                default: return PixelTone.Dense;
            }
        }

        /// <summary>
        /// 图文按钮（图标在左、文字跟右）——模式/动作钮的统一长相，档位色走 <see cref="ToneOfKind"/>；
        /// BattleHud 的投掷/结束回合与各模态按钮共用。
        /// position 相对父容器中心（anchor/pivot 0.5,0.5）。
        /// </summary>
        public static Button ActionButton(string name, Transform parent, UiGlyphs.Glyph glyph,
            string label, ButtonKind kind, Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font,
            bool withIcon = true)
        {
            PixelTone tone = ToneOfKind(kind);
            Color labelColor = PixelSkin.TextColorOn(tone);
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            ApplyPlateButton(button, image, tone);

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

        /// <summary>旧 bar 语义色（Color）→ 像素填充档。乘色退役后只用来"选哪张 Fill 贴图"。</summary>
        public static PixelFillKind FillKindOfColor(Color fillColor)
        {
            if (fillColor == UiSkin.TeamRed || fillColor == UiSkin.Danger)
                return PixelFillKind.Red;
            if (fillColor == UiSkin.TeamBlue)
                return PixelFillKind.Blue;
            if (fillColor == UiSkin.Warn || fillColor == UiSkin.Gold)
                return PixelFillKind.Warn;
            return PixelFillKind.Neutral;
        }

        /// <summary>
        /// 建双层血条：Track(Frame) 凹槽底 → 暖白 ghost（受击残影，垫在下）→ 主填充（队色档，在上）。
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
            track.sprite = PixelSkin.Track(PixelTone.Frame);
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;

            Image ghost = CreateFill("Ghost", root, PixelFillKind.Neutral);
            Stretch(ghost.rectTransform);

            Image fill = CreateFill("Fill", root, FillKindOfColor(fillColor));
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
