using TMPro;
using UnityEngine;
using UnityEngine.UI;
// SketchButtonKind / SketchButtonVariant 是 StickTokens 的嵌套类型，using static 提到顶层免逐处限定。
using static PirateCrew.UI.Stick.StickTokens;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 手绘涂鸦按钮 —— game-2 SketchButton（sketch_button.gd）的 UGUI 复刻。
    /// 视觉全由 <see cref="StickTokens.ButtonVariants"/> 变体表驱动（gd
    /// SketchStyle.BUTTON_VARIANTS 同表同步生成）：四态贴图槽经
    /// <see cref="SketchBoil.BindStates"/> 切换（语义 = gd SketchTextures 帧驱动 +
    /// add_theme_stylebox 四态 override），五态字色 / 描边 / 伪粗 / 图标模式同表。
    ///
    /// 【有意行为差异】
    ///  · gd focus 态色 → UGUI Selected 态（UGUI 无独立键盘 focus 视觉态，Selected 即
    ///    键盘/代码选中，语义最接近）；
    ///  · 伪粗：gd SketchFonts.bold 换粗体档 → TMP 合成加粗（fontStyle=Bold，shader
    ///    _WeightBold 顶点外扩近似，粗度取 StickTokens.FontEmbolden）——非真粗体字形，
    ///    注释说明与 gd 的差异；
    ///  · 描边：gd outline_size=3px 固定像素 → TMP _OutlineWidth 是归一化量纲，无直接
    ///    换算式，初值 <see cref="TmpOutlineWidth"/> 待与 Godot 基准并排目测校准
    ///    （同 TextSampleBuilder 口径）；材质一律走 fontMaterial 实例，绝不碰共享
    ///    fontSharedMaterial；
    ///  · IconSquare 自绘档：gd SketchGearButton._draw 方底 → 双层
    ///    <see cref="SketchWobbleGraphic"/>（Fill + Outline 异色叠置，官方 idiom），
    ///    四态底/边色按 gear 表（BTN_BG/BORDER 家族 token）在 DoStateTransition 换。
    /// </summary>
    public sealed class SketchButton : Button
    {
        /// <summary>TMP 描边宽初值（归一化量纲；gd outline 3px 无直接换算式，待目测校准）。</summary>
        public const float TmpOutlineWidth = 0.2f;

        private SketchButtonKind _kind = SketchButtonKind.Dark;
        private bool _applied;
        private SketchButtonVariant _variant;
        private Image _bg;
        private SketchBoil _boil;
        private TextMeshProUGUI _label;
        private SketchWobbleGraphic _selfFill;
        private SketchWobbleGraphic _selfOutline;

        /// <summary>变体档（默认 Dark —— gd Kind.NORMAL(0) → DARK 暗底默认档同义）。</summary>
        public SketchButtonKind Kind
        {
            get => _kind;
            set { _kind = value; ApplyVariant(); }
        }

        /// <summary>底透明度实例覆盖（gd bg_alpha：&lt;0 = 跟随变体默认；0~1 覆盖）。</summary>
        public float BgAlpha = -1f;

        /// <summary>
        /// 建一枚完整按钮（底 Image + SketchBoil 沸腾四态 + 本组件 + 居中文字 +
        /// IconSquare 自绘双层底）。kind 缺省 Dark。
        /// </summary>
        /// <param name="fontSize">文字字号；0 = 主题默认（gd 主题 Button 档 = FONT_HUD）。</param>
        public static SketchButton Create(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font,
            SketchButtonKind kind = SketchButtonKind.Dark, string label = null, float fontSize = 0f)
        {
            RectTransform rect = NewRect(name, parent, anchor, pivot, anchoredPosition, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.type = Image.Type.Sliced;
            image.color = Color.white;      // 手绘四态靠贴图切换，乘色全白不染脏边框
            image.raycastTarget = true;     // 可点件：命中面 = 按钮本体

            var button = rect.gameObject.AddComponent<SketchButton>();
            button._bg = image;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = WhiteStates();

            button._boil = rect.gameObject.AddComponent<SketchBoil>();

            // IconSquare 自绘底：Fill/Outline 两层异色叠置（SketchWobbleGraphic 官方 idiom），
            // 默认隐藏，ApplyVariant 按变体开关
            button._selfFill = AddSelfBg(rect, "SelfBgFill", SketchDrawMode.Fill, StickTokens.BTN_BG);
            button._selfOutline = AddSelfBg(rect, "SelfBgOutline", SketchDrawMode.Outline, StickTokens.BORDER);

            button._label = AddLabel(rect, label, font, fontSize);

            button.Kind = kind; // 走 setter → ApplyVariant（创建路径与切换路径同一份代码）
            return button;
        }

        /// <summary>
        /// 变体查表应用（底/字色/描边/伪粗一次取齐，gd _apply_flats 同纪律）。
        /// kind 运行时可切（设置分类选中态），每次重挂。
        /// </summary>
        public void ApplyVariant()
        {
            if (!StickTokens.ButtonVariants.TryGetValue(_kind, out _variant))
                return;
            _applied = true;

            bool selfDraw = string.IsNullOrEmpty(_variant.SlotBase); // gd 变体 self_draw 档
            if (_bg != null)
            {
                if (selfDraw)
                {
                    // 自绘档：贴图底与沸腾驱动下线（gd 置 StyleBoxEmpty），底走 wobble 双层
                    _bg.sprite = null;
                    if (_boil != null) _boil.enabled = false;
                }
                else
                {
                    string[] slots = _variant.SlotsNormalHoverPressedDisabled;
                    _bg.sprite = SketchSkin.Frame(slots[0], 0);
                    _bg.type = Image.Type.Sliced;
                    float alpha = BgAlpha >= 0f ? BgAlpha : _variant.BgAlpha;
                    Color c = Color.white;
                    c.a = alpha;                    // gd bg_alpha &lt;1 时降低盒 modulate alpha 同义
                    _bg.color = c;
                    if (_boil != null)
                    {
                        _boil.enabled = true;
                        _boil.Slot = slots[0];
                        _boil.BindStates(slots);    // 四态槽（缺档回退在 SketchSkin.Frame 告警层）
                    }
                }
            }
            if (_selfFill != null) _selfFill.gameObject.SetActive(selfDraw);
            if (_selfOutline != null) _selfOutline.gameObject.SetActive(selfDraw);

            if (_label != null)
            {
                // 伪粗随变体（主行动/强调笔画加重）：TMP 合成加粗近似 gd SketchFonts.bold
                _label.fontStyle = _variant.FakeBold > 0.5f ? FontStyles.Bold : FontStyles.Normal;
                Material mat = _label.fontMaterial; // 首次访问即实例化——绝不写共享材质
                if (_variant.OutlinePx > 0f)
                {
                    // 暗底白字靠墨边时间戳式高对比（gd outline=3 档）
                    mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
                    mat.SetColor(ShaderUtilities.ID_OutlineColor, _variant.OutlineColor);
                    mat.SetFloat(ShaderUtilities.ID_OutlineWidth, TmpOutlineWidth);
                }
                else
                {
                    // 亮底深墨字不加描边（笔画膨胀糊死，主菜单纸面按钮教训）
                    mat.DisableKeyword(ShaderUtilities.Keyword_Outline);
                }
                if (_variant.FakeBold > 0.5f)
                    mat.SetFloat("_WeightBold", StickTokens.FontEmbolden); // 合成粗度对齐 gd 伪粗档
            }
            // 立即按当前态刷一遍字色/自绘底色
            DoStateTransition(currentSelectionState, true);
        }

        /// <summary>
        /// 状态分发：五态字色（gd font/hover/pressed/focus/disabled 五色 →
        /// Normal/Highlighted/Pressed/Selected/Disabled）+ IconSquare 自绘底四态色。
        /// </summary>
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (!_applied)
                return;
            if (_label != null)
                _label.color = TextColorOf(state);
            if (_selfFill != null && _selfFill.isActiveAndEnabled)
            {
                // gd SketchGearButton._draw 四态底/边色（token 与 gear 表硬编码同值）
                switch (state)
                {
                    case SelectionState.Highlighted:
                        _selfFill.color = StickTokens.BTN_BG_HOVER;     // (1,1,1,0.10)
                        _selfOutline.color = StickTokens.BORDER_PANEL;  // (1,1,1,0.30)
                        break;
                    case SelectionState.Pressed:
                        _selfFill.color = StickTokens.BTN_BG_PRESSED;   // (1,1,1,0.04)
                        _selfOutline.color = Accent90;                  // ACCENT @ 0.9
                        break;
                    case SelectionState.Disabled:
                        _selfFill.color = StickTokens.BTN_BG_DISABLED;  // (1,1,1,0.03)
                        _selfOutline.color = Color.clear;               // 透明（不再描边）
                        break;
                    default:
                        _selfFill.color = StickTokens.BTN_BG;           // (1,1,1,0.07)
                        _selfOutline.color = StickTokens.BORDER;        // (1,1,1,0.16)
                        break;
                }
            }
        }

        private Color TextColorOf(SelectionState state)
        {
            switch (state)
            {
                case SelectionState.Highlighted: return _variant.TextHover;
                case SelectionState.Pressed: return _variant.TextPressed;
                case SelectionState.Selected: return _variant.TextFocus;   // gd focus 态
                case SelectionState.Disabled: return _variant.TextDisabled;
                default: return _variant.TextNormal;
            }
        }

        /// <summary>gd 压下态边 = ACCENT @ 0.9（SketchGearButton DRAW_PRESSED）。</summary>
        private static Color Accent90 =>
            new Color(StickTokens.ACCENT.r, StickTokens.ACCENT.g, StickTokens.ACCENT.b, 0.9f);

        /// <summary>乘色全白：手绘语言的状态反馈靠贴图切换（SketchBoil 四态），不叠色。</summary>
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

        private static SketchWobbleGraphic AddSelfBg(RectTransform root, string partName,
            SketchDrawMode mode, Color color)
        {
            var go = new GameObject(partName, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var g = go.AddComponent<SketchWobbleGraphic>();
            g.Mode = mode;
            g.color = color;
            g.raycastTarget = false;    // 纯视觉层，命中走根上的 Image
            go.SetActive(false);
            return g;
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
