using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PirateCrew.UI.DebugUi;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// Stick 按钮族公共基类——把 <see cref="SketchButton"/> / <see cref="SketchButtonSet"/> /
    /// <see cref="SketchButtonSetIcon"/> 三份复制的皮肤样板收敛成一处置。
    ///
    /// 【为什么映射住在这里】UGUI 的 <c>SelectionState</c> / <c>currentSelectionState</c> 是
    /// <see cref="Selectable"/> 的 protected 成员，外部静态类摸不到——「选择态 + 业务位 →
    /// Aseprite 状态位」的映射必须住在 Selectable 子类里（AseUi 只登记归属，不实现）。
    ///
    /// 【状态位映射（三处现行最终语义，一字不差）】
    /// <list type="bullet">
    /// <item>Disabled → Disabled（**业务位不让位**：否则 active+禁用会命中 selected 件而非常态皮）。</item>
    /// <item>Pressed → Selected|Capture（button.cpp:168-175 <c>setSelected(true)+captureMouse()</c>）。</item>
    /// <item>Selected → Focus |（active?Selected），Highlighted → Mouse |（active?Selected），
    ///   default → active?Selected——for_each_layer 取最大命中层（theme.cpp:69-73）。</item>
    /// </list>
    ///
    /// 【件挂皮】底皮件 id / 四态 sprite 全部经 <see cref="AseThemeLayers"/> 从 theme.xml 的
    /// <see cref="StyleId"/> 样式按状态位解析（单一真源），本类不写死件名。
    /// </summary>
    public abstract class AseButtonBase : Button
    {
        /// <summary>标签缓存（Create 工厂建好后直写，避免首个 getter 再 Find）。</summary>
        protected TextMeshProUGUI _label;

        /// <summary>可见标签件（<c>Label</c> 孩子）。
        /// **取文案请走这里，别用 <c>GetComponentInChildren&lt;TextMeshProUGUI&gt;</c>**——
        /// 影子层按 theme 绘制序被插到兄弟序 0（压在标签下），泛搜会先取到那层**不可见**的
        /// 影子：文字写进去不显示、可见标签留空还让按钮按空文案收窄
        /// （实拍：选关列表整列出战按钮被挤成细条、船员列表已解锁行按钮无字）。
        /// 场景重载后私有字段不序列化，按名兜底重取。按钮族同款，收在基类。</summary>
        public TextMeshProUGUI Label
        {
            get
            {
                if (_label == null)
                {
                    Transform child = transform.Find("Label");
                    _label = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
                }
                return _label;
            }
        }

        /// <summary>业务"当前值"态。按钮族共用（<see cref="SketchButton"/> 走 <c>Sticky</c> 别名）。</summary>
        protected bool _active;

        /// <summary>业务态（运行期可切）：重挂皮 + 按当前交互态即时刷新，
        /// 悬停/按压中切值时底皮不会停在旧常态。</summary>
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                ApplySkin();
                DoStateTransition(currentSelectionState, true);
            }
        }

        /// <summary>本控件对应的 theme 样式 id（"button" / "buttonset_item"）。</summary>
        protected abstract string StyleId { get; }

        /// <summary>UGUI 选择态 → Aseprite 状态位（语义见类注释）。</summary>
        protected AseStates FlagsOf(SelectionState state)
        {
            switch (state)
            {
                case SelectionState.Disabled:
                    return AseStates.Disabled;
                case SelectionState.Pressed:
                    return AseStates.Selected | AseStates.Capture;
                case SelectionState.Selected:
                    return AseStates.Focus | (_active ? AseStates.Selected : AseStates.None);
                case SelectionState.Highlighted:
                    return AseStates.Mouse | (_active ? AseStates.Selected : AseStates.None);
                default:
                    return _active ? AseStates.Selected : AseStates.None;
            }
        }

        /// <summary>某 UGUI 态命中的底皮件 id（theme.xml <see cref="StyleId"/> 样式）。</summary>
        protected string PartOf(SelectionState state)
        {
            return AseThemeLayers.ResolveBackgroundPart(StyleId, FlagsOf(state));
        }

        /// <summary>某 UGUI 态命中的底皮 sprite（件解析不到 → null）。</summary>
        protected Sprite SpriteOf(SelectionState state)
        {
            string part = PartOf(state);
            return part != null ? PixelSkin.Ase(part) : null;
        }

        /// <summary>建按钮时的成串皮肤样板：targetGraphic + SpriteSwap + 全白 ColorBlock
        /// （disabled 0.5 白、fadeDuration 0）。SpriteSwap 下 ColorBlock 不参与染色，
        /// 仅作占位（像素件禁乘色，状态反馈全靠贴图切换）。</summary>
        protected void InitAseSkin(Image target)
        {
            targetGraphic = target;
            transition = Transition.SpriteSwap;
            colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = Color.white,
                pressedColor = Color.white,
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0f,
            };
        }

        /// <summary>挂 Normal 底皮 + spriteState 四态 + 按当前态刷新一遍。
        /// 子类 override 扩展文字层/影子（先调 base 再补自己的层）。</summary>
        public virtual void ApplySkin()
        {
            Image bg = targetGraphic as Image;
            if (bg != null)
            {
                string part = PartOf(SelectionState.Normal);
                if (part != null)
                    AseUi.SetRawPart(bg, part);
                else
                {
                    bg.type = Image.Type.Sliced;
                    bg.pixelsPerUnitMultiplier = 1f;
                    bg.color = Color.white;   // 像素件禁止乘色
                }
            }

            SpriteState state = spriteState;
            state.highlightedSprite = SpriteOf(SelectionState.Highlighted);
            state.pressedSprite = SpriteOf(SelectionState.Pressed);
            state.selectedSprite = SpriteOf(SelectionState.Selected);
            state.disabledSprite = SpriteOf(SelectionState.Disabled);
            spriteState = state;

            DoStateTransition(currentSelectionState, true);
        }
    }
}
