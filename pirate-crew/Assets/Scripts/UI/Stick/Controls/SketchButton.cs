using TMPro;
using UnityEngine;
using UnityEngine.UI;
// SketchButtonKind / SketchButtonVariant 是 StickTokens 的嵌套类型，using static 提到顶层免逐处限定。
using static PirateCrew.UI.Stick.StickTokens;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦按钮 —— game-2 SketchButton（sketch_button.gd）的 UGUI 复刻（**已换 Beveled Pixel 皮**）。
    ///
    /// 【皮肤口径（换装后）】底 = <see cref="PixelSkin.Plate"/> 的 tone 九宫格（色阶烘在贴图里，
    /// 一条 tone 的明暗阶梯由调色板派生）；状态反馈 = UGUI **SpriteSwap 三态**
    /// （<c>spriteState.highlightedSprite</c> / <c>pressedSprite</c> / <c>selectedSprite</c>），
    /// <b>不再对 Image.color 乘色</b>——乘色会把烘焙色阶乘脏，这是像素皮的硬纪律。
    /// 字色 = <see cref="PixelSkin.TextColorOn"/>（浅底给墨字 / 深底给本 tone 亮档字）。
    /// 禁用态 = <see cref="CanvasGroup"/> alpha 0.55（贴图不烘禁用档）。
    ///
    /// 【与 gd 的继承关系】变体表 <see cref="StickTokens.ButtonVariants"/> 仍是 kind → tone /
    /// 伪粗 / 描边档的查表源（gd SketchStyle.BUTTON_VARIANTS 同表）；换皮只换了"底的表达方式"
    /// （槽位四态贴图沸腾 → 像素九宫格三态 SpriteSwap），变体→语义的映射不变。
    ///
    /// 【有意行为差异】
    ///  · 伪粗：gd SketchFonts.bold 换粗体档 → TMP 合成加粗（fontStyle=Bold，shader
    ///    _WeightBold 顶点外扩近似，粗度取 StickTokens.FontEmbolden）——非真粗体字形；
    ///  · 描边：gd outline_size=3px 固定像素 → TMP _OutlineWidth 是归一化量纲，无直接
    ///    换算式，初值 <see cref="TmpOutlineWidth"/> 待与 Godot 基准并排目测校准；
    ///    材质一律走 fontMaterial 实例，绝不碰共享 fontSharedMaterial；
    ///  · IconSquare 自绘档：旧版走 <c>SketchWobbleGraphic</c> 双层自绘，像素皮改为
    ///    与 Dark 同族的 <see cref="PixelTone.Dense"/> 九宫格（"图标钮 = 暗键帽"），
    ///    自绘层整体下线（文字/图标不受像素件纪律约束，仍可染色）。
    /// </summary>
    public sealed class SketchButton : Button
    {
        /// <summary>TMP 描边宽初值（归一化量纲；gd outline 3px 无直接换算式，待目测校准）。</summary>
        public const float TmpOutlineWidth = 0.2f;

        /// <summary>禁用态整体透明度（像素件不烘禁用档，走 CanvasGroup）。</summary>
        public const float DisabledAlpha = 0.55f;

        private SketchButtonKind _kind = SketchButtonKind.Dark;
        private bool _applied;
        private SketchButtonVariant _variant;
        private Image _bg;
        private TextMeshProUGUI _label;
        private CanvasGroup _group;

        /// <summary>变体档（默认 Dark —— gd Kind.NORMAL(0) → DARK 暗底默认档同义）。</summary>
        public SketchButtonKind Kind
        {
            get => _kind;
            set { _kind = value; ApplyVariant(); }
        }

        /// <summary>底透明度实例覆盖（gd bg_alpha：&lt;0 = 跟随变体默认；0~1 覆盖）。
        /// 像素皮禁乘色，故它只作用于 <see cref="CanvasGroup"/> 的整体 alpha（连字一起），
        /// 不改 Plate 贴图的颜色通道。</summary>
        public float BgAlpha = -1f;

        /// <summary>
        /// 建一枚完整按钮（像素 Plate 底 + SpriteSwap 三态 + 本组件 + 居中文字）。
        /// kind 缺省 Dark。
        /// </summary>
        /// <param name="fontSize">文字字号；0 = 主题默认（gd 主题 Button 档 = FONT_HUD）。</param>
        public static SketchButton Create(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font,
            SketchButtonKind kind = SketchButtonKind.Dark, string label = null, float fontSize = 0f)
        {
            RectTransform rect = NewRect(name, parent, anchor, pivot, anchoredPosition, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 像素件禁止乘色：色阶烘在贴图里，Image.color 恒白
            image.raycastTarget = true;     // 可点件：命中面 = 按钮本体

            var button = rect.gameObject.AddComponent<SketchButton>();
            button._bg = image;
            button.targetGraphic = image;
            button._group = rect.gameObject.AddComponent<CanvasGroup>();  // 禁用态整体调 alpha
            // 状态走 SpriteSwap（贴图切换），ColorBlock 不参与染色（全白仅为占位）。
            button.transition = Selectable.Transition.SpriteSwap;
            button.colors = WhiteStates();

            button._label = AddLabel(rect, label, font, fontSize);

            button.Kind = kind; // 走 setter → ApplyVariant（创建路径与切换路径同一份代码）
            return button;
        }

        /// <summary>
        /// 变体查表应用（tone / 三态贴图 / 字色 / 描边 / 伪粗一次取齐，gd _apply_flats 同纪律）。
        /// kind 运行时可切（设置分类选中态），每次重挂。
        /// </summary>
        public void ApplyVariant()
        {
            if (!StickTokens.ButtonVariants.TryGetValue(_kind, out _variant))
                return;
            _applied = true;

            PixelTone tone = ToneOf(_kind);

            if (_bg != null)
            {
                _bg.sprite = PixelSkin.Plate(tone, PixelState.Normal);
                _bg.type = Image.Type.Sliced;
                _bg.color = Color.white;    // 像素件禁止乘色
            }

            // 三态 SpriteSwap：hover 上抬一档 / 按压高光阴影对调 / selected 同 hover
            //（UGUI 无独立键盘 focus 视觉态，Selected 即键盘/代码选中，语义最接近 gd focus）。
            SpriteState state = spriteState;
            state.highlightedSprite = PixelSkin.Plate(tone, PixelState.Hovered);
            state.pressedSprite = PixelSkin.Plate(tone, PixelState.Pressed);
            state.selectedSprite = state.highlightedSprite;
            state.disabledSprite = null;    // 禁用走 CanvasGroup alpha，不吃贴图
            spriteState = state;

            if (_label != null)
            {
                _label.color = PixelSkin.TextColorOn(tone);

                // 伪粗随变体（主行动/强调笔画加重）：TMP 合成加粗近似 gd SketchFonts.bold
                _label.fontStyle = _variant.FakeBold > 0.5f ? FontStyles.Bold : FontStyles.Normal;
                Material mat = _label.fontMaterial; // 首次访问即实例化——绝不写共享材质
                if (_variant.OutlinePx > 0f)
                {
                    // 暗底浅字靠墨边高对比（gd outline=3 档）
                    mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
                    mat.SetColor(ShaderUtilities.ID_OutlineColor, _variant.OutlineColor);
                    mat.SetFloat(ShaderUtilities.ID_OutlineWidth, TmpOutlineWidth);
                }
                else
                {
                    // 亮底深墨字不加描边（笔画膨胀糊死）
                    mat.DisableKeyword(ShaderUtilities.Keyword_Outline);
                }
                if (_variant.FakeBold > 0.5f)
                    mat.SetFloat("_WeightBold", StickTokens.FontEmbolden); // 合成粗度对齐 gd 伪粗档
            }

            // 立即按当前态刷一遍（贴图三态 / 禁用 alpha）
            DoStateTransition(currentSelectionState, true);
        }

        /// <summary>
        /// 状态分发：三态贴图由 <see cref="Selectable.Transition.SpriteSwap"/> 在 base 里切换；
        /// 本覆盖只补"禁用态整体压 alpha"（像素件不烘禁用档）。
        /// </summary>
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (!_applied)
                return;

            if (_group != null)
            {
                float baseAlpha = BgAlpha >= 0f ? Mathf.Clamp01(BgAlpha) : 1f;
                _group.alpha = state == SelectionState.Disabled
                    ? Mathf.Min(baseAlpha, DisabledAlpha)
                    : baseAlpha;
            }
        }

        /// <summary>变体档 → 像素 tone（槽位映射表：Dark/IconSquare→Dense、Accent→Warn、
        /// Primary→Primary、Danger→Danger、Paper→Light）。</summary>
        private static PixelTone ToneOf(SketchButtonKind kind)
        {
            switch (kind)
            {
                case SketchButtonKind.Primary: return PixelTone.Primary;
                case SketchButtonKind.Accent: return PixelTone.Warn;
                case SketchButtonKind.Danger: return PixelTone.Danger;
                case SketchButtonKind.Paper: return PixelTone.Light;
                default: return PixelTone.Dense;   // Dark / IconSquare：暗键帽
            }
        }

        /// <summary>乘色全白占位：像素皮的状态反馈靠贴图切换（SpriteSwap），不叠乘色。</summary>
        private static ColorBlock WhiteStates()
        {
            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = Color.white,
                pressedColor = Color.white,
                selectedColor = Color.white,
                disabledColor = Color.white,
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };
        }

        private static TextMeshProUGUI AddLabel(RectTransform root, string content,
            TMP_FontAsset font, float fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            // gd 按钮盒 content margins（PAD_X+2, 2）——文字排版边距同源
            rt.offsetMin = new Vector2(StickTokens.PAD_X + 2f, 2f);
            rt.offsetMax = new Vector2(-(StickTokens.PAD_X + 2f), -2f);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = content ?? string.Empty;
            if (font != null)
                label.font = font;
            label.fontSize = fontSize > 0f ? fontSize : StickTokens.FONT_HUD; // gd 主题 Button 默认档
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.margin = Vector4.zero;
            return label;
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }
    }
}
