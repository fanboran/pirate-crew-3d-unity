using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 统一 UI 控件工厂（Beveled Pixel 皮肤的唯一构建入口）。
    ///
    /// 【为什么需要它】此前 Editor 玻璃构建器（MenuUiBuilder）与运行时构建器
    /// （RuntimeUiBuilder/UiSprites 木纸系）双栈并行，运行时程序集拿不到新皮肤与字号映射，
    /// 同屏两种风格——这是"简陋感"的主要技术根源。本类落在运行时程序集，
    /// Editor 装配与运行时动态件**走同一套工厂**，皮肤 / 字号 / 动效从此单轨。
    ///
    /// 【用法纪律】
    ///   · 字号一律传 <see cref="UiSkin.Font"/> 档位；
    ///   · **皮肤一律走 <see cref="PixelSkin"/>**——像素件的明暗色阶烘死在贴图里，
    ///     所以像素件禁止 <c>Image.color</c> 乘色（乘了就把烘焙好的三档色阶压平）；
    ///   · 按钮状态反馈一律 UGUI SpriteSwap（theme button 四态直切件），不用 ColorBlock 乘色；
    ///     禁用态 = 常态皮 + 双层影子字（theme.xml:617-619，<see cref="SketchButton"/> 承担）。
    ///   · 可点击件用 <see cref="ActionButton"/>（自带三态换图 + 按压位移）。
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

        /// <summary>顶锚摆位单点：x/y 都是「向右 / 向下为正」的布局值，y 的取负只在此处
        /// 发生。UGUI 顶锚系 anchoredPosition.y 向上为正，散写各处的 `(x, -y)` 惯例
        /// 每一处都是「计算型 y 忘取负把件顶出上缘」的机会（陈列廊件上偏案即此）。
        /// 新代码顶锚摆位一律走本函数；底锚/中心锚等别的锚系不归它管。</summary>
        public static void PlaceTopLeft(RectTransform rect, float x, float y, Vector2 size)
        {
            SetAnchored(rect, new Vector2(0f, 1f), size, new Vector2(x, -y));
        }

        /// <summary>铺满父容器后整体错位（【投影已退役】theme 无影子层——占位保留给未来的居中偏移件）。</summary>
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

        /// <summary>建 TMP 文本（字号请传 <see cref="UiSkin.Font"/> 档位常量）——**像素纪律
        /// 单点**：字体档解析 + 图集点采样 + <see cref="PixelSnapText"/> 顶点对齐 + 禁接光，
        /// 四件套在此一处保证，散装手写每一处都是漏挂事故（按钮标签漏 Snap 案）。
        /// 字体一律按字号经 <see cref="ResolvePixelFont"/> 就近选档——调用方传入的
        /// <paramref name="font"/> 仅作解析失败时的兜底。锚/摆位留给调用方。</summary>
        public static TextMeshProUGUI CreateText(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font, bool raycast = false,
            bool wrap = true)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            TMP_FontAsset resolved = ResolvePixelFont(fontSize, font);
            if (resolved != null)
            {
                text.font = resolved;
                PixelAtlasPointFilter.Ensure(resolved);
            }
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = raycast;
            text.gameObject.AddComponent<PixelSnapText>();   // 顶点像素对齐（治半格糊字）
            return text;
        }

        public static TMP_FontAsset ResolvePixelFont(int fontSize, TMP_FontAsset fallback = null)
        {
            // 原生档纪律（创始人裁决）：字号档 = 字体原生设计档，只在本档渲染，绝不放大缩小
            // （12px 字体烘 24/36 = 翻倍，被否决）。四档（降序）：16 正格点黑16 / 12、10、8 缝合像素。
            // 就近取档；某档资产缺失时循环自然下探到更小档，最后才回落调用方兜底。
            for (int i = 0; i < FontTiers.Length; i++)
            {
                if (fontSize >= FontTiers[i].size)
                {
                    TMP_FontAsset asset = TierFont(i);
                    if (asset != null)
                        return asset;
                }
            }
            return fallback;
        }

        // ------------------------------------------------------------------
        // 字阶表（与 FontAssetBuilder.Specs 同源，降序）：档位字号 → Resources 路径。
        // ------------------------------------------------------------------

        static readonly (int size, string path)[] FontTiers =
        {
            (16, "Fonts/ZhengGeDianHei16"),
            (12, "Fonts/FusionPixel12"),
            (10, "Fonts/FusionPixel10"),
            (8, "Fonts/FusionPixel8"),
        };

        /// <summary>正文档（12 原生档）Resources 路径——PixelShowcasePage 等直取用。</summary>
        internal const string BodyPixelFontPath = "Fonts/FusionPixel12";

        static readonly Dictionary<string, TMP_FontAsset> _tierCache = new Dictionary<string, TMP_FontAsset>();

        static TMP_FontAsset TierFont(int index)
        {
            string path = FontTiers[index].path;
            if (!_tierCache.TryGetValue(path, out TMP_FontAsset asset) || asset == null)
            {
                asset = Resources.Load<TMP_FontAsset>(path);
                _tierCache[path] = asset;
                if (asset == null)
                    Debug.LogWarning("[UiKit] Resources/" + path + " 缺失（跑 PirateCrew/Fonts/强制重建 TMP 中文字体资产）");
            }
            return asset;
        }

        // ------------------------------------------------------------------
        // 像素件（全部走 PixelSkin 同源出口；禁止乘色）
        // ------------------------------------------------------------------

        /// <summary>建面板/卡片底（**直角 Panel 皮**，Aseprite 参照：圆角只属于按钮）。</summary>
        public static Image CreatePanel(string name, Transform parent, PixelTone tone)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Panel(tone);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;   // ppum 固定 1：九宫格纹素补偿已退役；贴图 ×Unit2 落盘与渲染令牌的错位悬案见 UiSkin.Px 类头
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>建凸起块（Plate，九宫格）。像素件白贴图不乘色——色阶烘死在贴图里。</summary>
        public static Image CreatePlate(string name, Transform parent, PixelTone tone,
            PixelState state = PixelState.Normal)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Plate(tone, state);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;   // ppum 固定 1：九宫格纹素补偿已退役；贴图 ×Unit2 落盘与渲染令牌的错位悬案见 UiSkin.Px 类头
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>按钮贴合标签（创始人多轮裁决：**按钮大小 = 文字大小**）：
        /// 宽 = 标签 TMP 真实渲染宽 + <see cref="UiSkin.Px.ButtonPadX"/>，高 = 调用方指定；
        /// 同时改写 RectTransform 与既有 LayoutElement 的 preferred/min——装配期一次收口。
        /// 文字运行期会变的按钮请另走 HStack(controlWidths) 流式（内层横排组随文字收放）。</summary>
        public static void FitToLabel(Button button, float height)
        {
            TMPro.TextMeshProUGUI label = Stick.SketchButton.LabelOf(button);
            if (label == null)
                return;
            float width = Mathf.Max(2f * UiSkin.Font.Body,
                Mathf.Ceil(label.preferredWidth) + UiSkin.Px.ButtonPadX);
            RectTransform rect = (RectTransform)button.transform;
            rect.sizeDelta = new Vector2(width, height);
            var element = button.GetComponent<UnityEngine.UI.LayoutElement>();
            if (element != null)
            {
                element.preferredWidth = width;
                element.minWidth = width;
                element.preferredHeight = height;
            }
        }

        /// <summary>宽高全贴合：高 = 标签真实行高 + 上下各半 <see cref="UiSkin.Px.ButtonPadX"/>。</summary>
        public static void FitToLabel(Button button)
        {
            TMPro.TextMeshProUGUI label = Stick.SketchButton.LabelOf(button);
            if (label == null)
                return;
            float height = Mathf.Max(UiSkin.Px.Button,
                Mathf.Ceil(label.preferredHeight) + UiSkin.Px.ButtonPadX);
            FitToLabel(button, height);
        }

        /// <summary>建凹槽（Track，九宫格）：条状件的空槽底。</summary>
        public static Image CreateTrack(string name, Transform parent, PixelTone tone)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = PixelSkin.Track(tone);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;   // ppum 固定 1：九宫格纹素补偿已退役；贴图 ×Unit2 落盘与渲染令牌的错位悬案见 UiSkin.Px 类头
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
            image.pixelsPerUnitMultiplier = 1f;   // ppum 固定 1：九宫格纹素补偿已退役；贴图 ×Unit2 落盘与渲染令牌的错位悬案见 UiSkin.Px 类头
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
            image.pixelsPerUnitMultiplier = 1f;   // ppum 固定 1：九宫格纹素补偿已退役；贴图 ×Unit2 落盘与渲染令牌的错位悬案见 UiSkin.Px 类头
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
            // 【源码裁决】Aseprite dark 主题 grep "shadow" 零命中——对话框没有影子层。
            // 旧版自造的 Shadow 孩子在这里就地销毁（"面板盖两层"的观感即它）。
            Transform legacyShadow = panel.Find("Shadow");
            if (legacyShadow != null)
                Object.DestroyImmediate(legacyShadow.gameObject);

            Image plate = FindImage(panel, "Plate");
            if (plate == null)
                plate = CreateRect("Plate", panel).gameObject.AddComponent<Image>();
            plate.sprite = PixelSkin.Panel(tone);   // 直角面板皮（圆角只属于按钮）
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 1f;   // ppem 固定 1：九宫格纹素补偿已退役；贴图 ×Unit2 落盘与渲染令牌的错位悬案见 UiSkin.Px 类头
            plate.color = Color.white;
            plate.raycastTarget = true;   // 面板本体挡点击（内容件画在其上，不受影响）
            Stretch(plate.rectTransform);
            plate.rectTransform.SetSiblingIndex(0);
            return plate;
        }

        /// <summary>标题带内文字的孩子名（三处窗体共用，装配幂等按名复用）。</summary>
        const string TitleLabelName = "TitleLabel";

        /// <summary>窗体实际内容顶的载体（挂在窗根上；无此组件 = 原生 17 口径）。
        /// 标题字大时带装不下（创始人裁决：**溢出可以接受**），但内容起点必须随字高
        /// 下移拉开距离（防标题与下方元素穿模）——窗内排版都问它。</summary>
        public sealed class AseWindowTitleBand : MonoBehaviour
        {
            /// <summary>实际内容顶 = 窗顶到内容区起点的距离（原生 = 17：带 15 + 缝 2）。</summary>
            public float ContentTop = 17f;
        }

        /// <summary>窗体实际内容顶（标题字大时 &gt; 17）。窗根无带组件时回落原生口径。</summary>
        public static float WindowContentTopOf(RectTransform window)
        {
            var band = window != null ? window.GetComponent<AseWindowTitleBand>() : null;
            return band != null ? band.ContentTop : 17f;
        }

        /// <summary>
        /// 摆**带标题窗体的标题**（theme window_title_label）：标题带内左上、带内全高垂直居中
        /// （[0,15] 格区），字色 <see cref="PixelSkin.Theme.Text"/>，右侧让出
        /// <paramref name="rightReserve"/> 宽的窗控钮位；横向锚定拉伸，不依赖装配期窗体宽度。
        ///
        /// 【为什么是唯一入口】此前主菜单/模态、设置面板、列表窗体三处各自手摆标题：
        /// 字号 16/12 混杂、边距各写一套、两处漏挂顶点对齐件——实拍「标题压带 / 半格糊字」
        /// 即由此来。收口后只此一处，字号缺省 = 正文档 12（theme 的 15 格带按 8px 拉丁字
        /// 设计，16px 字实拍压带）。
        /// </summary>
        public static TextMeshProUGUI EnsureTitleLabel(RectTransform window, string title,
            TMP_FontAsset font, float titleFontSize = 0f, float rightReserve = 18f)
        {
            TextMeshProUGUI label = FindText(window, TitleLabelName);
            if (label == null)
            {
                label = CreateRect(TitleLabelName, window).gameObject.AddComponent<TextMeshProUGUI>();
                label.fontStyle = FontStyles.Normal;   // 位图字禁伪粗
                // 垂直取 Middle（Left = Middle+Left）而非 Midline：Midline 按字体基线中线对齐，
                // 中文墨迹整体偏上（实拍标题墨迹比带心高 1 画布格）；Middle 与按钮字同口径，
                // 墨迹中心 = 字盒中心。
                label.alignment = TextAlignmentOptions.Left;
                label.raycastTarget = false;
                label.enableWordWrapping = false;   // 长标题换行会溢出标题带成残影
                label.overflowMode = TextOverflowModes.Overflow;
                label.gameObject.AddComponent<PixelSnapText>();   // 顶点像素对齐（治半格糊字）
                RectTransform initial = label.rectTransform;
                initial.anchorMin = new Vector2(0f, 1f);
                initial.anchorMax = new Vector2(1f, 1f);
                initial.pivot = new Vector2(0f, 1f);
            }

            float size = titleFontSize > 0f ? titleFontSize : UiSkin.Font.Body;
            TMP_FontAsset resolved = ResolvePixelFont((int)size, font);
            if (resolved != null)
            {
                label.font = resolved;
                PixelAtlasPointFilter.Ensure(resolved);
            }
            label.fontSize = size;
            label.SetText(title);
            label.color = PixelSkin.Theme.Text;
            UiLayout.Ignore(label.gameObject);   // 标题在带内自定位，不参与内容流

            // 【内容顶随标题字高动态（创始人裁决）】原生带 15 是 theme mini(8 号)字口径；
            // 字大装不下时**溢出带可以接受**，但窗内内容起点必须随字高下移（防标题与
            // 下方元素穿模太紧）：内容顶 = max(17, ceil(行高) + 上边距 5 + 下余 2)。
            // 挂在窗根的 AseWindowTitleBand 上，排版一律经 WindowContentTopOf 问实际值。
            // 本方法是标题唯一入口——只走 EnsureTitleLabel 的路径（如列表窗）也被兜住。
            float pointSize = resolved != null ? Mathf.Max(1f, resolved.faceInfo.pointSize) : size;
            float lineH = resolved != null
                ? resolved.faceInfo.lineHeight * (size / pointSize)
                : size * 1.3f;
            var bandInfo = window.GetComponent<AseWindowTitleBand>();
            if (bandInfo == null)
                bandInfo = window.gameObject.AddComponent<AseWindowTitleBand>();
            bandInfo.ContentTop = Mathf.Max(17f, Mathf.CeilToInt(lineH) + 7f);

            RectTransform rect = label.rectTransform;
            // theme <style id="window_title_label" margin-top="5" margin-left="5">：字盒上边留 5 格、
            // 底边贴带底。字盒高按原生带取，字大溢出带是裁决接受态（内容顶已随字高下移，
            // 溢出的墨迹落在带下缝里，不与内容穿模）。
            rect.offsetMin = new Vector2(AseLayout.Px(AseLayout.TitleMarginLeft),
                -PixelSkin.WindowTitleBand);
            rect.offsetMax = new Vector2(-rightReserve, -AseLayout.Px(AseLayout.TitleMarginTop));
            return label;
        }

        /// <summary>
        /// 带标题窗体（theme window 复刻，皮 = sheet.png 直切件 "window"）：Plate 孩子换
        /// 直切窗体九宫格（顶部 15 标题带随切片自动落位），带内左上标题文字
        /// （经 <see cref="EnsureTitleLabel"/> 唯一入口），右上窗控钮（? / ×）。
        /// **内容区必须从带底往下排**——顶部内边距 ≥ <see cref="PixelSkin.WindowTitleBand"/>。
        /// </summary>
        public static Image EnsureWindow(RectTransform window, PixelTone tone, string title,
            TMP_FontAsset font, float titleFontSize, bool helpButton = false, bool closeButton = true)
        {
            Image plate = EnsurePanel(window, tone);
            plate.sprite = PixelSkin.Ase("window");   // theme.xml:165 直切件（1x 设计格）
            UiLayout.Ignore(plate.gameObject);   // 装饰层不吃父布局流（VBox 只排内容件）

            // 【内容顶随标题字高动态】由 EnsureTitleLabel（标题唯一入口）统一挂带组件——
            // 本函数与其余只摆标题的路径（如列表窗）都被兜住。
            EnsureTitleLabel(window, title, font, titleFontSize,
                18f + AseLayout.Px(AseLayout.WindowButtonGap));

            // 窗控钮：右上（× 最右、? 在其左；theme margin-top 3 / margin-right 3 与 1）
            float right = AseLayout.Px(AseLayout.CloseButtonMarginRight);
            if (closeButton)
            {
                CreateWindowButton(window, "CloseButton", PixelSkin.WindowIconSprite(
                    PixelSkin.WindowIcon.Close), right);
                right += 9f + AseLayout.Px(AseLayout.WindowButtonGap);
            }
            if (helpButton)
                CreateWindowButton(window, "HelpButton",
                    PixelSkin.WindowIconSprite(PixelSkin.WindowIcon.Help), right);

            return plate;
        }

        /// <summary>窗控钮图标自然尺寸 = theme <c>window_close_icon</c> / <c>window_help_icon</c>
        /// 件尺寸 5×6（theme.xml &lt;parts&gt; x=16/36 y=11）。图标一律按自然尺寸画。</summary>
        const float WindowButtonIconWidth = 5f;
        const float WindowButtonIconHeight = 6f;

        /// <summary>窗控钮（theme window_button 9×11 + 图标；纯视觉件，onClick 由调用方接）。</summary>
        public static Button CreateWindowButton(RectTransform window, string name, Sprite icon, float rightMargin)
        {
            Button button;
            Transform existing = window.Find(name);
            if (existing != null)
                button = existing.GetComponent<Button>();
            else
            {
                var go = CreateRect(name, window).gameObject;
                UiLayout.Ignore(go);   // 窗控钮锚右上自定位，不参与内容流
                button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.SpriteSwap;

                var bg = go.AddComponent<Image>();
                bg.type = Image.Type.Simple;   // 9×11 固定尺寸件，不切片
                bg.raycastTarget = true;

                var iconGo = CreateRect("Icon", go.transform);
                // 【图标按自然尺寸居中，禁 Stretch】theme window_close_button 是
                // `<icon part="window_close_icon" color="button_normal_text"/>`——图标按件自然
                // 尺寸 5×6 居中。旧实现 Stretch 进 9×11 的钮盒 → 1.8/1.83 倍非整数缩放，
                // 图标竖线落到小数格（实拍：确认框 × 的 22 个奇数屏像素）。居中取整同
                // Aseprite 的整数除法：(9−5)/2=2、(11−6)/2=2。
                iconGo.anchorMin = iconGo.anchorMax = new Vector2(0f, 1f);
                iconGo.pivot = new Vector2(0f, 1f);
                iconGo.anchoredPosition = new Vector2(2f, -2f);
                iconGo.sizeDelta = new Vector2(WindowButtonIconWidth, WindowButtonIconHeight);
                var iconImage = iconGo.gameObject.AddComponent<Image>();
                iconImage.type = Image.Type.Simple;
                iconImage.pixelsPerUnitMultiplier = 1f;   // ×1 终局：贴图纹素 = 画布像素
                iconImage.raycastTarget = false;

                RectTransform br = go.GetComponent<RectTransform>();
                br.anchorMin = br.anchorMax = new Vector2(1f, 1f);
                br.pivot = new Vector2(1f, 1f);
                br.anchoredPosition = new Vector2(-rightMargin, -AseLayout.Px(AseLayout.WindowButtonMarginTop));
                br.sizeDelta = new Vector2(9f, 11f);   // theme window_button 9×11（×1）
            }

            Image bgImage = button.image != null ? button.image : button.GetComponent<Image>();
            bgImage.sprite = PixelSkin.WindowButton(PixelState.Normal);
            SpriteState states = button.spriteState;
            states.highlightedSprite = PixelSkin.WindowButton(PixelState.Hovered);
            states.pressedSprite = PixelSkin.WindowButton(PixelState.Pressed);
            states.selectedSprite = states.highlightedSprite;
            button.spriteState = states;

            Image iconImage2 = button.transform.Find("Icon").GetComponent<Image>();
            iconImage2.sprite = icon;
            iconImage2.color = Color.white;   // 图标烘焙即 theme 字灰，不乘色
            return button;
        }

        static TextMeshProUGUI FindText(RectTransform parent, string name)
        {
            Transform child = parent.Find(name);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
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
        // 按钮（theme button 四态换图；禁用双层字）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把按钮接成 theme button 皮（theme.xml:608-620 四态 SpriteSwap）：
        /// 常态/悬停/键盘焦点/业务选中（<paramref name="sticky"/>）四张直切件；
        /// 禁用 = 常态皮 + 双层影子字（<see cref="SketchButton"/> 本体承担）。
        /// 【为什么用 SpriteSwap 而不是 ColorBlock】像素件的色阶烘死在贴图里，乘色会压平。
        /// </summary>
        public static void ApplyThemeButton(Button button, Image image, bool sticky = false)
        {
            image.sprite = PixelSkin.Ase(sticky ? "button_selected" : "button_normal");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;   // ×1 终局：贴图纹素 = 画布像素
            image.color = Color.white;
            image.raycastTarget = true;

            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = PixelSkin.Ase(sticky ? "button_selected" : "button_hot"),
                pressedSprite = PixelSkin.Ase("button_selected"),   // 按下 = selected+capture 状态位 → 蓝面（button.cpp:168-175 + theme.cpp:69-73）
                selectedSprite = PixelSkin.Ase("button_focused"),
                disabledSprite = PixelSkin.Ase("button_normal"),
            };
            // 禁用双层字由 SketchButton.DoStateTransition 承担。
        }

        /// <summary>
        /// 图文按钮（**纯文字**）——动作钮的统一长相，theme button 灰面皮。
        /// 【kind 体系已退役（×1 全量对齐波）】theme 无彩面按钮（参考图 OK/Cancel 同为灰面），
        /// 语义由文字与焦点蓝描边表达；Sticky 业务选中态由调用方走 <see cref="ApplyThemeButton"/>。
        /// position 相对父容器中心（anchor/pivot 0.5,0.5）。
        /// </summary>
        public static Button ActionButton(string name, Transform parent,
            string label, Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            ApplyThemeButton(button, image);

            TextMeshProUGUI text = CreateText("Text", rect, label, UiSkin.Font.Body,
                TextAlignmentOptions.Center, PixelSkin.Theme.Text, font, raycast: false);
            text.enableWordWrapping = false;
            Stretch(text.rectTransform, 10f);

            return button;
        }

        // ------------------------------------------------------------------
        // 运行时字体（播放器 / Gallery：字体资产经 Resources 打进包）
        // ------------------------------------------------------------------

        /// <summary>运行时字体档。<see cref="MenuUiBuilder"/> 是 Editor 类，运行时改从
        /// Resources/Fonts/ 取同一批 SDF 资产（由 FontAssetBuilder 复制入 Resources）。</summary>

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

        /// <summary>水平方向内缩（条填充件用）：Track 凹槽左右缘的 1u 外环不被填充盖掉——
        /// 与顶栏队血条的段内缩同口径（此前填充满铺，凹槽右缘描边被整条盖掉，走查读感
        /// 「血条右边界没有描边」）。</summary>
        static void InsetHorizontal(RectTransform rect, float inset)
        {
            rect.offsetMin = new Vector2(inset, rect.offsetMin.y);
            rect.offsetMax = new Vector2(-inset, rect.offsetMax.y);
        }

        /// <summary>
        /// 建双层血条：Track(Frame) 凹槽底 → 暖白 ghost（受击残影，垫在下）→ 主填充（队色档，在上）。
        /// 两个填充都从左侧 anchorMax.x 表达比例；水平各内缩 1u 露出凹槽描边（见 <see cref="InsetHorizontal"/>）。
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
            track.pixelsPerUnitMultiplier = 1f;   // ppum 固定 1：九宫格纹素补偿已退役；贴图 ×Unit2 落盘与渲染令牌的错位悬案见 UiSkin.Px 类头
            track.color = Color.white;
            track.raycastTarget = false;

            Image ghost = CreateFill("Ghost", root, PixelFillKind.Neutral);
            Stretch(ghost.rectTransform);
            InsetHorizontal(ghost.rectTransform, 1f);   // 1 艺术像素内缩;

            Image fill = CreateFill("Fill", root, FillKindOfColor(fillColor));
            Stretch(fill.rectTransform);
            InsetHorizontal(fill.rectTransform, 1f);   // 1 艺术像素内缩;

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

        /// <summary>建模态（全屏 Stretch 的根 + Dim 遮罩 + 居中**带标题窗体**；默认隐藏）。
        /// theme window 口径：顶 15u 标题带 + 右上 × 窗控钮（点击隐藏 Root——只对
        /// 「隐藏即全部语义」的弹窗开，如确认框；暂停这类隐藏≠取消暂停的传 false）；
        /// 内容 VBox 顶部自动避开标题带。</summary>
        public static ModalView CreateModal(string name, Transform parent, Vector2 cardSize,
            string title = null, TMP_FontAsset titleFont = null, float titleFontSize = 0f,
            bool closeButton = true)
        {
            RectTransform root = CreateRect(name, parent);
            Stretch(root);
            CreateDimOverlay("DimOverlay", root);

            RectTransform card = CreatePanel("Card", root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, cardSize);
            bool titled = !string.IsNullOrEmpty(title);
            if (titled)
            {
                EnsureWindow(card, PixelTone.Frame, title,
                    titleFont != null ? titleFont : ResolvePixelFont(UiSkin.Font.Title),
                    titleFontSize > 0f ? titleFontSize : UiSkin.Font.Body,   // 15 格带装 12px 字（16px 实拍压带）
                    helpButton: false, closeButton: closeButton);
            }

            // 卡片纵向贴合内容（创始人裁决：对话框大小跟内容走）：VBox 管内容流，
            // ContentSizeFitter(Vertical=Preferred) 让卡片高 = 内容高 + 上下边距；
            // 宽度仍由调用方的 cardSize.x 指定。
            var box = card.gameObject.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (box == null)
                box = card.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            // theme window_with_title：内容内缩 border=6（带标题时顶 = 实际内容顶——
            // EnsureWindow 已按标题字高定好，12 号 → 23；字溢出带是裁决接受态）
            box.padding = new RectOffset(
                (int)AseLayout.Px(AseLayout.WindowBorder), (int)AseLayout.Px(AseLayout.WindowBorder),
                (int)AseLayout.Px(titled ? WindowContentTopOf(card) : AseLayout.WindowBorder),
                (int)AseLayout.Px(AseLayout.WindowBorder));
            box.spacing = AseLayout.Px(2);   // theme 无显式纵缝——邻件各自带 border，取 2 设计格过渡
            box.childControlWidth = true;
            box.childControlHeight = true;
            box.childForceExpandWidth = false;
            box.childForceExpandHeight = false;
            box.childAlignment = TextAnchor.UpperCenter;
            var fitter = card.gameObject.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter == null)
                fitter = card.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            if (titled && closeButton)
            {
                Button close = CreateWindowButton(card, "CloseButton",
                    PixelSkin.WindowIconSprite(PixelSkin.WindowIcon.Close), 3f);
                close.onClick.AddListener(() => root.gameObject.SetActive(false));
            }

            root.gameObject.SetActive(false);
            return new ModalView { Root = root.gameObject, Card = card };
        }
    }
}
