using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// 调试面板公共小件工厂（theme 语义件的一次调用版本）——「一个小界面几百行」的
    /// 架构病收口：凹槽输入框 / 复选行 / 两件套组合框，全在此一处实现，
    /// 各面板（实摆画廊、New Sprite 对话框…）只声明布局与行为。
    /// 件与色的出处同各调用点注释：theme.xml 对应 style/parts 条目。
    ///
    /// 刻意不再提供的：通用「悬停换面」件。Aseprite 的悬停态是**逐件按 styles 表**给的
    /// （check_box/button 有 mouse 层，list_item/tab **没有**），一个通用 HoverFace 会把
    /// 有悬停的件和没悬停的件一起点亮——列表行的「悬停变暗」正是这么错的。
    /// 复选行的鼠标态见 <see cref="AseCheckBoxFace"/>，列表行见 <see cref="AseListBox"/>。
    /// </summary>
    public static class AseWidgetKit
    {
        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------

        /// <summary>凹槽数值/文本输入（theme textedit：sunken 件自绘 + 自建编辑语义 + 可选框内右缘后缀）。
        /// 锚定父件左上 (x, -y)，尺寸 w×12。
        ///
        /// 【实现已换轨】编辑语义走 <see cref="AseEntry"/>（entry.cpp 编辑路径逐函数移植），
        /// **不再**用 TMP_InputField 自带编辑。但公共契约不变：仍返回一枚 <c>TMP_InputField</c>
        /// （调用方 AseDialogLoader 用它注册 id / 取 transform 当布局 rect，DebugMenuHost 读写
        /// <c>.text</c>）。该 TMP_InputField 在根上以**禁用态**纯作外向文本模型：程序写
        /// <c>.text</c> 经 onValueChanged/轮询被 AseEntry 收养，AseEntry 的编辑回写 <c>.text</c>。
        ///
        /// 【参数映射】
        ///   · <paramref name="contentType"/> == <c>IntegerNumber</c> → 挂 <see cref="AseIntEntry"/>
        ///     （源 IntEntry：只收数字、失焦夹取；值域取 int.Min/Max 即无界），maxsize 1024
        ///     （对齐 <c>&lt;expr&gt;</c> 的 ExprEntry 默认）；其余类型 → 挂 <see cref="AseEntry"/>
        ///     （不过滤），maxsize 6（源 <c>&lt;entry&gt;</c> 默认）。
        ///   · 其余 ContentType（Decimal/Alphanumeric/Name…）一律当 Standard，登记为忽略。
        ///   · <paramref name="text"/> = 初始文本；<paramref name="suffix"/> = 右锚后缀标签
        ///     （色取 theme.xml:48 entry_suffix #c6c6c6）。</summary>
        public static TMP_InputField SunkenEntry(RectTransform parent, string name, float x, float y,
            float w, string text, TMP_InputField.ContentType contentType
                = TMP_InputField.ContentType.IntegerNumber, string suffix = null)
        {
            RectTransform entry = UiKit.CreateRect(name, parent);
            UiKit.PlaceTopLeft(entry, x, y, new Vector2(w, 12f));
            var sunken = entry.gameObject.AddComponent<Image>();
            AseUi.SetRawPart(sunken, "sunken_normal");   // theme sunken 常态件（Sliced + ppum×1 + 白见 AseUi）
            sunken.raycastTarget = true;

            // 外向文本模型：禁用态 TMP_InputField（不参与编辑，只作 .text 存根与 id 注册件）。
            // 必须在 AseEntry 之前挂：AseEntry.Build 绑定它并订阅 onValueChanged。
            var field = entry.gameObject.AddComponent<TMP_InputField>();
            field.enabled = false;
            field.interactable = false;

            bool integer = contentType == TMP_InputField.ContentType.IntegerNumber;
            AseEntry control = integer
                ? (AseEntry)entry.gameObject.AddComponent<AseIntEntry>()
                : entry.gameObject.AddComponent<AseEntry>();
            if (integer)
                ((AseIntEntry)control).Init(int.MinValue, int.MaxValue);   // 无界（ExprEntry 语义）
            control.Build(field, text, suffix, integer ? 1024 : 6);

            return field;
        }

        // ------------------------------------------------------------------
        // 复选
        // ------------------------------------------------------------------

        /// <summary>复选/单选行（theme check_box / radio_button：**常态无底色层**，
        /// <c>state="mouse"</c> 才铺 <c>check_hot_face</c>/<c>radio_hot_face</c> #575B61；
        /// 图标 8×8 @x2 或 @x14 文字）。行宽按件表尺寸提示实收
        /// （8 图标 + 2 左缩 + 4 缝 + 文字 + 2 右边框）——悬停面的**范围**必须等于件本身，
        /// 旧版固定 160 宽会把文字右侧的空白也点亮。返回行根（可再查 Icon 换图标）。</summary>
        public static Button CheckRow(RectTransform parent, string label, float x, float y,
            bool initial, System.Action<bool> onChanged, string kind = "check")
        {
            RectTransform rect = UiKit.CreateRect(kind + "_" + label, parent);
            UiKit.PlaceTopLeft(rect, x, y, Vector2.zero);
            GameObject rowGo = rect.gameObject;

            var face = rowGo.AddComponent<Image>();
            face.color = new Color(0f, 0f, 0f, 0f);
            face.raycastTarget = true;
            rowGo.AddComponent<AseCheckBoxFace>().Bind(face, kind == "radio");

            // 图标 8×8 @ (2,4)：theme.xml:150 check_normal 件尺寸 + 样式 icon x=2。
            RectTransform icon = UiKit.CreateRect("Icon", rect);
            UiKit.PlaceTopLeft(icon, 2f, 4f, new Vector2(8f, 8f));
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.raycastTarget = false;

            TextMeshProUGUI labelText = DebugWindowKit.PlaceLabel(rect, label, UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, 14f, 0f, 140f);

            // 尺寸提示（件表）：文字 @x14 + 正文宽 + 右边框 2（图标侧 2+8+4 = 14 已含在 x14 里）
            rect.sizeDelta = new Vector2(14f + Mathf.Ceil(labelText.preferredWidth) + 2f, 16f);

            var button = rowGo.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            bool on = initial;
            ApplyCheckIcon(iconImage, kind, on);
            button.onClick.AddListener(() =>
            {
                on = !on;
                ApplyCheckIcon(iconImage, kind, on);
                onChanged?.Invoke(on);
            });
            return button;
        }

        static void ApplyCheckIcon(Image icon, string kind, bool on)
        {
            // 选中图标由引擎按 state 层给出（check_box/radio_button 的 icon 层：
            // check_normal / check_selected / radio_normal / radio_selected）。
            string styleId = kind == "radio" ? "radio_button" : "check_box";
            string part = AseThemeLayers.ResolveIconPart(
                styleId, on ? AseStates.Selected : AseStates.None);
            if (part != null)
                AseUi.SetRawPart(icon, part);
        }

        // ------------------------------------------------------------------
        // 滚动视图
        // ------------------------------------------------------------------

        /// <summary>theme <c>view</c>：border="3" border-top="4"（theme.xml:581）。</summary>
        public const float ViewBorderLeft = 3f;
        /// <summary>theme <c>view</c>：border="3" border-top="4"（theme.xml:581）。</summary>
        public const float ViewBorderTop = 4f;
        /// <summary>theme <c>view</c>：border="3" border-top="4"（theme.xml:581）。</summary>
        public const float ViewBorderRight = 3f;
        /// <summary>theme <c>view</c>：border="3" border-top="4"（theme.xml:581）。</summary>
        public const float ViewBorderBottom = 3f;

        /// <summary>theme <c>view</c> 样式滚动视图整装（点锚版）：window_face 色层 +
        /// sunken_normal 九宫边框 + <see cref="AseView"/> 内核。几何全按源——
        /// 12 宽滚动条**按需**出现在框内右缘、视口被条挤窄（scroll_helper.cpp:75-81，
        /// 源里没有「框外独立条」的组合）；条是否出现由内容 sizeHint 对视口的 IfNeeded
        /// 判定，不是恒挂。用法：内容挂 <see cref="AseView.AttachToView"/> →
        /// <see cref="AseView.SetContentHint"/> → <see cref="AseView.UpdateView"/>。
        /// 拉伸锚的整页滚动区用 <see cref="PaintViewSkin"/> + <see cref="AseView.Attach"/>。</summary>
        public static AseView ScrollView(RectTransform parent, string name, float x, float y,
            float w, float h)
        {
            RectTransform view = UiKit.CreateRect(name, parent);
            UiKit.PlaceTopLeft(view, x, y, new Vector2(w, h));
            PaintViewSkin(view);
            return AseView.Attach(view,
                (int)ViewBorderLeft, (int)ViewBorderTop, (int)ViewBorderRight, (int)ViewBorderBottom);
        }

        /// <summary>只贴 view 两层皮（色层 + 边框件层），几何内核另接
        /// <see cref="AseView.Attach"/>；点锚整装版见 <see cref="ScrollView"/>。</summary>
        public static void PaintViewSkin(RectTransform view)
        {
            // theme view 样式逐层（经 AseThemeLayers 解析：background color=window_face、
            // border part=sunken_normal；state="focus" 换 sunken_focused——滚动区无键盘
            // 焦点态，不接）。像素件禁止乘色。
            var bg = view.gameObject.AddComponent<Image>();
            Color32? face = AseThemeLayers.ResolveBackgroundColor("view", AseStates.None);
            bg.color = face.HasValue ? (Color)face.Value : (Color)PixelSkin.Theme.Face;
            bg.raycastTarget = true;    // 命中区 = 整个 View bounds（widget.cpp hitTest）——
                                        // 底面不接光会点穿到被盖住的下层窗

            RectTransform border = UiKit.CreateRect("ViewBorder", view);
            UiKit.Stretch(border);
            var borderImage = border.gameObject.AddComponent<Image>();
            string part = AseThemeLayers.ResolveBackgroundPart("view", AseStates.None);
            if (part != null)
                borderImage.sprite = PixelSkin.Ase(part);
            borderImage.type = Image.Type.Sliced;
            borderImage.pixelsPerUnitMultiplier = 1f;
            borderImage.color = Color.white;
            borderImage.raycastTarget = true;   // 边框区接事件（弹层收合语义靠它；滚动区无害）
        }

        // ------------------------------------------------------------------
        // 组合框
        // ------------------------------------------------------------------

        /// <summary>两件套组合框（theme combobox：sunken2 词条 + 右缘 15 宽 mini_button 箭头钮）。
        /// 行为逐条按 combobox.cpp：点词条/点钮开合切换、弹层 = View(sunken) 里的 ListBox、
        /// 点弹层外收、选中项回填词条文本。实现见 <see cref="AseComboBox"/>。
        /// 弹层宿主取 parent.parent（窗的父级 overlay）——**不是** parent（窗）本身：
        /// 弹层挂在窗里会被窗裁/随窗移动。
        /// 宽度由调用方给定（组合框自身的 sizeHint 反推宽度未移植，见交接报告）。</summary>
        public static TextMeshProUGUI ComboBox(RectTransform parent, string name, float x, float y,
            float w, string[] options, int initial, System.Action<int> onPick = null)
        {
            RectTransform root = UiKit.CreateRect(name, parent);
            UiKit.PlaceTopLeft(root, x, y, new Vector2(w, 12f));

            // 弹层宿主一律 null → AseComboBox.Build 缺省走 AseUi.OverlayOf（画布根）。
            // 旧 parent.parent 只在组合框直挂窗根时碰巧等于 overlay；嵌套在盒/格里时
            // 弹层会挂进窗内（随窗移动、被窗压层），与源语义（Manager 顶层 popup，
            // combobox.cpp:615+652）不符——W3 登记项收口。
            var combo = root.gameObject.AddComponent<AseComboBox>();
            return combo.Build(root, options, initial, onPick, null);
        }
    }

    /// <summary>复选/单选行的鼠标态（唯一的一层额外底色）：theme <c>check_box</c> /
    /// <c>radio_button</c> 样式 <c>&lt;background color="check_hot_face" state="mouse"/&gt;</c>
    /// （#575B61，radio 同名色）。底色经 <see cref="AseThemeLayers"/> 解析——常态
    /// <b>没有</b> background 层命中（→ 全透明），悬停命中 hot 层。禁用 #2C2C30 / 焦点
    /// #41444A + check_focus 环虽已可解析，但调试面板没有禁用/键盘焦点两种态，未接线。</summary>
    public sealed class AseCheckBoxFace : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image _face;
        string _styleId;

        public void Bind(Image face, bool radio)
        {
            _face = face;
            _styleId = radio ? "radio_button" : "check_box";
            Apply(AseStates.None);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Apply(AseStates.Mouse);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Apply(AseStates.None);
        }

        void Apply(AseStates states)
        {
            if (_face == null)
                return;
            Color32? c = AseThemeLayers.ResolveBackgroundColor(_styleId, states);
            _face.color = c.HasValue ? (Color)c.Value : new Color(0f, 0f, 0f, 0f);
        }
    }
}
