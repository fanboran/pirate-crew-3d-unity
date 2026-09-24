using PirateCrew.Audio;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 菜单与管理界面的 UGUI 构建辅助（运行时建控件）。
    ///
    /// 【视觉层（Beveled Pixel 像素皮）】
    ///   · 文本统一 <see cref="TextMeshProUGUI"/>（中文字体由调用方注入，见 <see cref="CreateText"/> 的 font 参数）；
    ///   · 行底 = <see cref="SketchPanel"/>（Light Tone → Plate(Light) 暖白片 + 底垫投影）；
    ///   · 按钮 = <see cref="SketchButton"/>（Dark 变体 → Plate(Dense) 暗键帽；三态走 SpriteSwap，
    ///     禁用走 CanvasGroup alpha，全由控件本体承担）；
    ///   · 字色一律 <see cref="PixelSkin.TextColorOn"/>（按底 tone 取可读档）。
    ///
    /// 【列表行数随名册/海图变化】运行时生成比摆 Prefab 更省接线；行数少、非高频，
    ///   不构成性能顾虑。
    /// </summary>
    public static class RuntimeUiBuilder
    {
        /// <summary>按钮不可用态乘色。像素皮禁乘色（<see cref="SketchButton"/> 的三态 SpriteSwap +
        /// CanvasGroup 禁用 alpha 纪律）——禁用视觉由控件本体承担，本常量恒白，
        /// 仅为兼容既有调用点签名保留。</summary>
        public static Color DisabledButtonColor => Color.white;

        // ------------------------------------------------------------------
        // 菜单 juice（UI 审计 P2-9）：复用战斗内同款动效原语与 UI 音效，不新造效果
        // （时长/曲线全在 <see cref="UiMotionRules"/>；音效复用 <see cref="AudioService"/> 既有 UI 音）。
        // ------------------------------------------------------------------

        /// <summary>
        /// 管理/菜单界面按钮统一反馈（与战斗内 <c>BattleHud.ButtonFeedback</c> 同口径）：
        /// 成功 = UiClick 音 + punch 缩放；失败 = UiError 音（不弹，靠状态提示条说话）。
        /// </summary>
        public static void ButtonFeedback(Button button, bool success, UiMotion motion)
        {
            AudioService.PlayUi(success ? SfxId.UiClick : SfxId.UiError);
            if (success && motion != null && button != null && button.image != null)
                motion.Punch(button.image, UiMotionRules.PunchSeconds);
        }

        /// <summary>
        /// 模态面板打开：UiPanelOpen 音 + 自下方 24px 滑入淡入（无 <paramref name="motion"/> 时退化为直接显示）。
        /// 面板缺 CanvasGroup 时补一个（<see cref="UiMotion.ShowPanel"/> 的淡入依赖它）。
        /// </summary>
        public static void OpenPanel(GameObject panel, UiMotion motion)
        {
            if (panel == null)
                return;

            if (motion != null)
            {
                if (panel.GetComponent<CanvasGroup>() == null)
                    panel.AddComponent<CanvasGroup>();
                motion.ShowPanel(panel, UiMotionRules.PanelShowSeconds, UiMotionRules.PanelSlideOffsetPixels);
            }
            else
            {
                panel.SetActive(true);
            }

            AudioService.PlayUi(SfxId.UiPanelOpen);
        }

        /// <summary>模态面板关闭：快速淡出（协程收尾 <c>SetActive(false)</c>；无 motion 时直接隐藏）。</summary>
        public static void ClosePanel(GameObject panel, UiMotion motion)
        {
            if (panel == null)
                return;

            if (motion != null)
                motion.HidePanel(panel, UiMotionRules.PanelHideSeconds);
            else
                panel.SetActive(false);
        }

        /// <summary>建一个带 RectTransform 的空 UI 对象。</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>建文本控件（TMP）。<paramref name="font"/> 可为 null（回落 TMP 默认，会由调用方告警）。</summary>
        public static TextMeshProUGUI CreateText(string name, Transform parent, string content, int fontSize,
            TextAlignmentOptions alignment, Color color, TMP_FontAsset font)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            if (font != null)
                text.font = font;
            TMP_FontAsset pixelResolved = UiKit.ResolvePixelFont(fontSize, font);
            if (pixelResolved != null)
            {
                text.font = pixelResolved;
                PixelAtlasPointFilter.Ensure(pixelResolved);
            }
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 建按钮（像素皮：<see cref="SketchButton"/> Dark 变体 + 居中文本；tone 九宫格 /
        /// 三态 SpriteSwap / 禁用 alpha 由控件本体承担，调用方不再配色）。初值尺寸为占位，
        /// 行内按钮随后由 <see cref="LayoutRowContent"/> 按行高重摆。
        /// </summary>
        public static Button CreateButton(string name, Transform parent, string label, int fontSize,
            TMP_FontAsset font)
        {
            return SketchButton.Create(parent, name,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(50f, UiSkin.Px.Button),   // 令牌按钮高占位；行内会被 <see cref="LayoutRowContent"/> 重摆
                font, StickTokens.SketchButtonKind.Dark, label, fontSize);
        }

        /// <summary>取按钮上的 TMP 文本（建行时写动作文案用）。</summary>
        public static TextMeshProUGUI GetButtonLabel(Button button)
        {
            return button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        }

        /// <summary>星形图标（UiGlyphs 程序化五角星，白形可染色）。</summary>
        public static Sprite StarIcon => UiGlyphs.Get(UiGlyphs.Glyph.Star);

        /// <summary>
        /// 在行内右侧（动作按钮左边）摆一排星级图标（规范 §3.4：星级改图标，不用「★」字符）。
        /// </summary>
        /// <returns>星级容器；可在需要时再隐藏。</returns>
        public static RectTransform CreateStarRow(Transform row, int stars, int maxStars, float iconSize = 36f)
        {
            float step = iconSize + 6f;
            RectTransform container = CreateRect("Stars", row);
            container.anchorMin = new Vector2(1f, 0.5f);
            container.anchorMax = new Vector2(1f, 0.5f);
            container.pivot = new Vector2(1f, 0.5f);
            container.sizeDelta = new Vector2(maxStars * step, iconSize);
            // 动作按钮最宽 4 字（216）+ 右边距 16 + 间隔 8——星级让出按钮区，宁宽勿叠。
            container.anchoredPosition = new Vector2(-(216f + 16f + 8f), 0f);

            for (int i = 0; i < maxStars; i++)
            {
                RectTransform icon = CreateRect("Star" + i, container);
                icon.anchorMin = new Vector2(0f, 0.5f);
                icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(iconSize, iconSize);
                icon.anchoredPosition = new Vector2(i * step, 0f);

                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = StarIcon;
                image.raycastTarget = false;
                // 点亮 = ACCENT（金阶，承接旧黄铜星语义）；熄灭 = INK @ 0.35（暗剪影）。
                image.color = i < stars ? StickTokens.ACCENT : WithAlpha(StickTokens.INK, 0.35f);
            }

            return container;
        }

        /// <summary>在行容器里建一行（纵向堆叠，锚在容器顶部；底为 SketchPanel Light
        /// → Plate(Light) 暖白片 + 底垫投影；行内字色请取 <see cref="PixelSkin.TextColorOn"/>
        /// 的 Light 档，勿再手写字色）。<paramref name="rowHeight"/> 建议取
        /// <see cref="UiSkin.Px.Button"/> + 上下各 12（令牌位点）。</summary>
        public static RectTransform CreateRow(Transform container, int index, float rowHeight,
            float spacing = 6f, float leftPadding = 8f)
        {
            RectTransform row = CreateRect("Row" + index, container);
            // 【行宽 = 容器宽，铁律】父容器挂 VBox 列表时行宽必须交给布局组接管并铺满——
            // 新建 RectTransform 默认宽 100，不接管则行内文本被压成一字宽竖列
            // （船员管理/选关两屏实测事故：标签竖排叠印）。
            if (container != null)
            {
                var parentLayout = container.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                if (parentLayout != null)
                {
                    parentLayout.childControlWidth = true;
                    parentLayout.childForceExpandWidth = true;
                }
            }
            // 行高经 LayoutElement 声明（父 VBox controlHeights 开时生效）。
            var layout = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.preferredHeight = rowHeight;
            layout.minHeight = rowHeight;

            // 行底板：SketchPanel.Create 是点锚出口，建完再拉伸铺满行矩形；
            // 底板不拦截点击（SketchPanel 内 Image raycastTarget=false），命中留给动作钮。
            // 底板是装饰件，声明 ignoreLayout，不参与行内流式排版。
            SketchPanel backplate = SketchPanel.Create(row, "Backplate",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                SketchPanel.Tone.Light);
            UiLayout.Ignore(backplate.gameObject);
            Stretch(backplate.GetComponent<RectTransform>());

            return row;
        }

        /// <summary>清空容器下的全部子对象（重建列表前调用）。</summary>
        public static void ClearChildren(Transform container)
        {
            if (container == null)
                return;

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                GameObject child = container.GetChild(i).gameObject;
                // Destroy 是延迟到帧末执行的；先 SetActive(false) 避免本帧出现新旧两套列表叠在一起。
                child.SetActive(false);
                Object.Destroy(child);
            }
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

        /// <summary>按锚点 + 尺寸摆放（anchoredPosition 相对锚点）。</summary>
        public static void SetAnchored(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>行内排版（全 UGUI 原生布局件，零自定义驱动）：
        /// 行挂 HorizontalLayoutGroup，标签声明 flexibleWidth 吃剩余宽；
        /// 按钮挂 HorizontalLayoutGroup + ContentSizeFitter(Preferred)——UGUI 自带的自贴合，
        /// 按钮宽 = 文字真实渲染宽 + 左右各半 <see cref="UiSkin.Px.ButtonPadX"/>，文本变即自动收放。</summary>
        public static void LayoutRowContent(RectTransform row, TextMeshProUGUI label, Button action, float rowHeight)
        {
            UiLayout.HStack(row, 8, UiPadding.Symmetric(5, 0), controlWidths: true,
                alignment: TextAnchor.MiddleLeft);

            if (label != null)
            {
                UiLayout.Flexible(label.gameObject);
                RectTransform rect = label.rectTransform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, rowHeight);
                label.alignment = TextAlignmentOptions.MidlineLeft;
            }

            if (action != null)
            {
                RectTransform rect = action.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, UiSkin.Px.Button);

                // 按钮自贴合（原生，无双驱动）：按钮自身挂横排组（内层 childControlWidth），
                // 其 preferred 宽 = 左右各半 ButtonPadX + 文字渲染宽——行 HStack(controlWidths)
                // 直接按这个 preferred 收放按钮。不再挂 ContentSizeFitter（父组已在控制，双驱会打架）。
                var fit = action.gameObject.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (fit == null)
                    fit = action.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                fit.childControlWidth = true;
                fit.childControlHeight = false;
                fit.childForceExpandWidth = false;
                fit.childForceExpandHeight = false;
                fit.childAlignment = TextAnchor.MiddleCenter;
                int pad = UiSkin.Px.ButtonPadX / 2;
                fit.padding = new RectOffset(pad, pad, 0, 0);
            }
        }

        /// <summary>把颜色整体乘上 alpha（保留原 RGB；令牌色降透明度的本地出口，
        /// 不再借道 UiTheme）。</summary>
        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
