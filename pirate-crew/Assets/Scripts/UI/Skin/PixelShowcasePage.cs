using PirateCrew.Core;
using PirateCrew.UI.Stick;
using TMPro;
// SketchButtonKind 是 StickTokens 的嵌套类型（与 UiGalleryPage 同一别名手法）。
using SketchButtonKind = PirateCrew.UI.Stick.StickTokens.SketchButtonKind;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// **组件展示页（实机）**：把 [组件总表](../../../docs/images/ui-pixel-ref/gen-components.png)
    /// 的同一套构图、同一套令牌表**用运行时真件**建出来——同一行同一个件、同一个尺寸，
    /// 图上是 promise，这一页是兑现（创始人 2026-09-22 走查"你的各种组件呢？说好了 3:1 像素风呢？"：
    /// 旧陈列页是手绘皮肤时代的尺寸与字体，与像素皮对不上）。
    ///
    /// 【口径】画布 1920×1080 = 640×360 艺术像素（1 艺术像素 = <see cref="PixelSkin.Unit"/> 屏幕像素）；
    /// **所有矩形与字号都落在艺术像素栅格上**（尺寸/坐标 = 艺术像素 × 3，偏差 1 屏幕像素都会把
    /// 3px 带糊成 4px）。文字 = <see cref="PixelFont"/>（缝合像素 12px 位图档），
    /// 显示字号取 12 的整数倍：正文 36（= 12 艺术像素）、标题 72（= 24）。
    ///
    /// 【滚动】页面比一屏高（≈1140 艺术像素），走 ScrollRect 纵向滚 + 右侧像素滚动条
    /// （对齐 game-2 组件展示窗口的版式）；"返回总览"由 <see cref="UiShowcaseBoot"/> 钉在画布上不随滚动。
    /// </summary>
    public static class PixelShowcasePage
    {
        /// <summary>1 艺术像素 = 几个画布（屏幕）像素。真源 = <see cref="PixelSkin.Unit"/>。</summary>
        static readonly int A = PixelSkin.Unit;

        static TMP_FontAsset s_font;

        const int DialogTop = 104;      // 对话框顶（距页顶的艺术像素）
        const int TitleBandH = 68;      // 题头高：标题 24 + 副题 12 + 上下边距
        const int CursorStart = DialogTop + 12 + TitleBandH + 24;

        /// <summary>画一块 Plate（对话框/页签宿主等大底板）。</summary>
        static void Lay(RectTransform parent, PixelTone tone, int top, int x, int w, int h)
        {
            SpriteImage(parent, PixelSkin.Plate(tone), x, top, w, h);
        }

        /// <summary>
        /// 建整页到 <paramref name="content"/> 下（content 需已按 1920 宽、顶部对齐铺好）。返回内容高度（画布像素）。
        ///
        /// 【两遍法】整页内容包在一个**大对话框**（Frame 面板）里（创始人 2026-09-23：
        /// "为什么不在外面整个套一个大对话框"）——但对话框高度取决于布局游标，而对话框底板
        /// 必须**先画**（后画会盖住内容）。所以布局跑两遍：第一遍只量游标（不画），得到
        /// 对话框高度；第二遍先铺底板再画内容。
        /// </summary>
        public static float Build(RectTransform content)
        {
            s_font = PixelFont();

            // 第一遍：只量布局
            int bottom = LayoutSections(content, null, CursorStart);
            int dialogH = bottom - DialogTop;

            // 第二遍：先铺对话框底板与题头，再画内容
            var labels = new System.Collections.Generic.List<LabelSpec>();
            Lay(content, PixelTone.Frame, DialogTop, 24, 592, dialogH);
            TitleBand(content, labels, DialogTop + 12);
            bottom = LayoutSections(content, labels, CursorStart);

            return bottom * A;
        }

        /// <summary>题头：标题 + 副题（上边距收窄、题副之间留足空档——2026-09-23 走查行距）。
        /// 【满精度】标题与副题同为 12 艺术像素（36 画布）——展示页不允许 24 艺术像素档
        /// （没有 24px 原生设计位图字体，缩放即降精度），层级靠颜色。</summary>
        static void TitleBand(RectTransform content, System.Collections.Generic.List<LabelSpec> labels, int top)
        {
            Lay(content, PixelTone.Dense, top, 36, 568, TitleBandH);
            labels.Add(new LabelSpec("组件展示（实机）", 48, top + 10, 12, "white"));
            labels.Add(new LabelSpec("画布 640 艺术像素宽 = 1920 屏幕像素；1 艺术像素 = 3 屏幕像素（与 3D 渲染同一颗粒度）",
                48, top + 40, 12, "dim"));
        }

        /// <summary>
        /// 分区内容。<paramref name="labels"/> 传 null = 只量布局不画（第一遍）。
        /// 返回布局完成后的游标（艺术像素，自页顶向下）。所有横向位置都在对话框内边距（36..604）里。
        /// </summary>
        static int LayoutSections(RectTransform content, System.Collections.Generic.List<LabelSpec> labels, int cursor)
        {
            void Name(string text, int top, int h)
            {
                if (labels != null) labels.Add(new LabelSpec(text, 36, top + (h - 12) / 2, 12, "white"));
            }
            void Cap(string text, int x, int top)
            {
                if (labels != null) labels.Add(new LabelSpec(text, x, top, 12, "dim"));
            }
            void Header(string title)
            {
                if (labels != null)
                {
                    labels.Add(new LabelSpec(title, 36, cursor, 12, "white"));
                    SpriteImage(content, PixelSkin.Separator(true), 36, cursor + 18, 568, 2);
                }
                cursor += 26;
            }

            // ---- ① 面板族：7 tone × 三态 ----
            Header("面板 Plate —— 7 个 tone × 三态（悬停 = 抬一档 / 按压 = 沉一档 + 换高光边）");
            for (int s2 = 0; s2 < 3; s2++)
                Cap(new[] { "常态", "悬停", "按压" }[s2], 112 + s2 * 172 + 68, cursor);
            cursor += 20;
            var tones = (PixelTone[])System.Enum.GetValues(typeof(PixelTone));
            string[] toneNames = { "面板", "内容", "羊皮纸", "海图", "黄铜", "危险", "警告" };
            for (int i = 0; i < tones.Length; i++)
            {
                int top = cursor + i * 38;
                Name(toneNames[i], top, 30);
                if (labels != null)
                {
                    SpriteImage(content, PixelSkin.Plate(tones[i], PixelState.Normal), 112, top, 160, 24);
                    SpriteImage(content, PixelSkin.Plate(tones[i], PixelState.Hovered), 284, top, 160, 24);
                    SpriteImage(content, PixelSkin.Plate(tones[i], PixelState.Pressed), 456, top, 160, 24);
                }
            }
            cursor += 6 * 38 + 24 + 28;

            // ---- ② 凹槽：真实血条用法 ----
            Header("凹槽 Track —— 槽 312×24（令牌条高）+ 填充（边距 6；没填满的一段露出槽底）");
            var bars = new (string label, PixelTone tone, PixelFillKind fill, float ratio)[]
            {
                ("生命", PixelTone.Frame, PixelFillKind.Red, 0.62f),
                ("魔法", PixelTone.Frame, PixelFillKind.Blue, 0.35f),
                ("敌船", PixelTone.Danger, PixelFillKind.Red, 0.24f),
            };
            for (int i = 0; i < bars.Length; i++)
            {
                int top = cursor + i * 38;
                Name(bars[i].label, top, 24);
                if (labels != null)
                {
                    SpriteImage(content, PixelSkin.Track(bars[i].tone), 112, top, 312, 24);
                    SpriteImage(content, PixelSkin.Fill(bars[i].fill), 118, top + 6,
                        Mathf.RoundToInt(300 * bars[i].ratio), 12);
                }
            }
            cursor += 2 * 38 + 24 + 14;
            Name("内嵌槽（海图）", cursor, 48);
            if (labels != null)
                SpriteImage(content, PixelSkin.Track(PixelTone.Sea), 112, cursor, 120, 48);
            cursor += 48 + 28;

            // ---- ③ 填充 5 色 ----
            Header("填充 Fill —— 5 色；条高 12（令牌：条 24 = 6 框 + 12 填 + 6 框）");
            var fills = (PixelFillKind[])System.Enum.GetValues(typeof(PixelFillKind));
            string[] fillNames = { "红（生命）", "蓝（魔法）", "暖橙（冷却）", "海蓝（航行）", "中性（进度）" };
            for (int i = 0; i < fills.Length; i++)
            {
                int x = 112 + i * 100;
                if (labels != null)
                {
                    labels.Add(new LabelSpec(fillNames[i], x, cursor, 12, "white"));
                    SpriteImage(content, PixelSkin.Fill(fills[i]), x, cursor + 18, 88, 12);
                }
            }
            cursor += 18 + 12 + 28;

            // ---- ④ 语义件 ----
            Header("语义件 —— 选人圈 / 焦点框 / 位点 / 分隔线 / 投影（各按原生尺寸）");
            int semTop = cursor;
            if (labels != null)
            {
                SpriteImage(content, PixelSkin.Ring, 112, semTop + 8, 24, 24);
                SpriteImage(content, PixelSkin.Focus, 160, semTop + 4, 32, 32);
                SpriteImage(content, PixelSkin.Pip(true), 208, semTop + 14, 12, 12);
                SpriteImage(content, PixelSkin.Pip(false), 228, semTop + 14, 12, 12);
                SpriteImage(content, PixelSkin.Separator(true), 288, semTop + 18, 128, 4);
                SpriteImage(content, PixelSkin.ShadowSprite, 435, semTop + 11, 96, 24);
                SpriteImage(content, PixelSkin.Plate(PixelTone.Frame), 432, semTop + 8, 96, 24);
            }
            string[] semNames = { "选人圈", "焦点框", "位点 亮/暗", "分隔线", "投影" };
            int[] semX = { 112, 160, 208, 288, 432 };
            for (int i = 0; i < semNames.Length; i++)
                Cap(semNames[i], semX[i], semTop + 44);
            cursor += 44 + 12 + 28;

            // ---- ⑤ 页签：零间隙、压住宿主描边（真件可点）----
            Header("页签 Tab —— 等宽相邻零间隙、压住宿主描边；点一下换选中（真件）");
            if (labels != null)
            {
                BuildTabStrip(content, 40, cursor + 2);
                Lay(content, PixelTone.Frame, cursor + 24, 24, 592, 60);
            }
            cursor += 24 + 60 + 28;

            // ---- ⑥ 按钮：三态真件（悬停/按压直接动手上验）----
            Header("按钮 —— 件 48×24 = 标签 24 + 上下各 6 / 左右各 12；悬停 / 按压请直接上手");
            cursor += 12;
            var buttons = new (string label, SketchButtonKind kind)[]
            {
                ("确定", SketchButtonKind.Primary),
                ("菜单", SketchButtonKind.Paper),
                ("退出", SketchButtonKind.Danger),
                ("警告", SketchButtonKind.Accent),
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                int x = 112 + i * 60;
                if (labels != null)
                {
                    if (i == 0)
                        SpriteImage(content, PixelSkin.Focus, x - 2, cursor - 2, 52, 28);
                    SketchButton.Create(content, "Demo_" + buttons[i].label,
                        new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(x * A, -cursor * A), new Vector2(48 * A, 24 * A),
                        s_font, buttons[i].kind, buttons[i].label, 12);
                }
            }
            cursor += 24 + 24;

            // ---- 标签统一落位（文字都在数据里，与生成器的文字清单同构）----
            if (labels != null)
                foreach (LabelSpec spec in labels)
                    Text(content, spec.text, spec.x, spec.y, spec.size, ColorOf(spec.color));

            return cursor;
        }

        // ------------------------------------------------------------------
        // 页签条（零间隙 + 压描边 + 点击换选中）
        // ------------------------------------------------------------------

        static void BuildTabStrip(RectTransform parent, int x, int top)
        {
            string[] titles = { "甲板", "船员", "海图" };
            var tabs = new Image[titles.Length];
            var texts = new TextMeshProUGUI[titles.Length];
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                RectTransform tab = ArtRect(parent, "Tab_" + titles[i], x + i * 48, top, 48, 24);
                Image image = tab.gameObject.AddComponent<Image>();
                image.type = Image.Type.Sliced;
                image.raycastTarget = true;
                tabs[i] = image;

                TextMeshProUGUI text = Text(tab, titles[i], 0, 0, 12, PixelSkin.TextColorOn(PixelTone.Dense));
                UiKit.Stretch(text.rectTransform);
                texts[i] = text;

                var button = tab.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                // 状态靠换贴图（与 SketchWidgets.Tabs 同口径）：悬停 = 预演选中，ColorTint 会乘色毁色阶。
                button.transition = Selectable.Transition.SpriteSwap;
                SpriteState states = button.spriteState;
                states.highlightedSprite = PixelSkin.Tab(PixelTone.Light);
                states.pressedSprite = PixelSkin.Tab(PixelTone.Light);
                states.selectedSprite = PixelSkin.Tab(PixelTone.Light);
                button.spriteState = states;
                button.onClick.AddListener(() =>
                {
                    for (int j = 0; j < tabs.Length; j++)
                    {
                        bool isSel = j == index;
                        tabs[j].sprite = PixelSkin.Tab(isSel ? PixelTone.Light : PixelTone.Dense);
                        texts[j].color = PixelSkin.TextColorOn(isSel ? PixelTone.Light : PixelTone.Dense);
                    }
                });
            }
            ApplyTab(tabs, texts, 1);
        }

        static void ApplyTab(Image[] tabs, TextMeshProUGUI[] texts, int selected)
        {
            for (int j = 0; j < tabs.Length; j++)
            {
                bool isSel = j == selected;
                tabs[j].sprite = PixelSkin.Tab(isSel ? PixelTone.Light : PixelTone.Dense);
                texts[j].color = PixelSkin.TextColorOn(isSel ? PixelTone.Light : PixelTone.Dense);
            }
        }

        // ------------------------------------------------------------------
        // 艺术像素栅格上的小工具（坐标一律"距顶多少艺术像素"）
        // ------------------------------------------------------------------

        static RectTransform ArtRect(Transform parent, string name, int x, int y, int w, int h)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w * A, h * A);
            rect.anchoredPosition = new Vector2(x * A, -y * A);
            return rect;
        }

        static Image SpriteImage(Transform parent, Sprite sprite, int x, int y, int w, int h)
        {
            RectTransform rect = ArtRect(parent, sprite == null ? "Pixel" : sprite.name, x, y, w, h);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：tone 色阶烘在贴图里
            image.raycastTarget = false;
            return image;
        }

        static void Plate(Transform parent, PixelTone tone, int x, int y, int w, int h)
        {
            SpriteImage(parent, PixelSkin.Plate(tone), x, y, w, h);
        }

        static TextMeshProUGUI Text(Transform parent, string content, int x, int y, int sizeArtPx, Color color)
        {
            TextMeshProUGUI text = UiKit.CreateText("Label", parent, content, UiSkin.Font.Body,
                TextAlignmentOptions.TopLeft, color, s_font);
            // 满精度：只允许 12/10 艺术像素（36/30 画布）——其它档位没有原生位图字体，
            // 一律并回 12 并报错（防再混出降精度的字号）。
            if (sizeArtPx != 12 && sizeArtPx != 10)
            {
                Debug.LogError("[PixelShowcasePage] 字号 " + sizeArtPx + " 艺术像素无原生位图字体档，并回 12");
                sizeArtPx = 12;
            }
            text.fontSize = sizeArtPx * A;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot
                = new Vector2(0f, 1f);
            text.rectTransform.sizeDelta = new Vector2(content.Length * sizeArtPx * A + 4 * A, sizeArtPx * A + A);
            text.rectTransform.anchoredPosition = new Vector2(x * A, -y * A);
            return text;
        }

        static void Section(Transform parent, System.Collections.Generic.List<LabelSpec> labels,
            string title, ref int cursor)
        {
            labels.Add(new LabelSpec(title, 24, cursor, 12, "white"));
            SpriteImage(parent, PixelSkin.Separator(true), 24, cursor + 18, 592, 2);
            cursor += 26;
        }

        /// <summary>像素字体（位图档）；显示字号必须取 12 的整数倍（见类头）。</summary>
        public static TMP_FontAsset PixelFont()
        {
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts/FusionPixel12-px");
            if (font == null)
            {
                Debug.LogWarning("[PixelShowcasePage] Resources/Fonts/FusionPixel12-px 缺失"
                    + "（跑 PirateCrew/Fonts/生成 TMP 中文字体资产 后可用），回落默认字体");
                return null;
            }

            // TMP 的动态图集会在运行时**重建**真实贴图（序列化的占位图是 0×0），重建时不设
            // filterMode → 默认 Bilinear → padding 0 下相邻字形格互相渗色，每个字带出邻居的
            // 碎块、读成"重影"（第一张实机截图的病根）。挂一个强制 Point 的纠偏件：
            // 字形按需进图集的整个过程里都钉住最近邻（顶级类 PixelAtlasPointFilter）。
            var fixer = new GameObject("PixelAtlasPointFilter").AddComponent<PixelAtlasPointFilter>();
            fixer.font = font;
            return font;
        }

        static Color Dim()
        {
            // 与生成器/盖章脚本同式：暖白向展示板底色混 45%
            Color white = PixelSkin.PaperWhite;
            Color ink = PixelSkin.Ink;
            Color deep = PixelSkin.DarkOf(PixelTone.Sea);
            Color backdrop = Color.Lerp(deep, ink, 0.45f);
            return Color.Lerp(white, backdrop, 0.45f);
        }

        static Color ColorOf(string role)
        {
            switch (role)
            {
                case "ink": return PixelSkin.Ink;
                case "dim": return Dim();
                default: return PixelSkin.PaperWhite;
            }
        }

        readonly struct LabelSpec
        {
            public readonly string text;
            public readonly int x, y, size;
            public readonly string color;

            public LabelSpec(string text, int x, int y, int size, string color)
            {
                this.text = text;
                this.x = x;
                this.y = y;
                this.size = size;
                this.color = color;
            }
        }
    }
}
