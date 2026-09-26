using System;
using System.Collections.Generic;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite <c>ComboBox</c> 语义移植（<c>src/ui/combobox.cpp</c> 全文 + theme.xml
    /// <c>combobox</c> / <c>combobox_button</c> 样式）。此前实现是「AseMenuKit 菜单弹层 + 每点必开」，
    /// 现按源改成：两件套（sunken2 词条 + mini_button 箭头钮）、弹层 = View(sunken 边框) 里的
    /// ListBox、点词条/点钮**开合切换**、点弹层外收、选项选中即回填词条文本、底越界翻到上方。
    ///
    /// 源里三件套的分工（combobox.cpp:36-80、82-107、341-412）：
    /// - <c>ComboBoxEntry</c>（词条）按下 → <c>switchListBox()</c>（486-499）；
    /// - <c>ComboBoxButton</c>（箭头钮）Click 信号 → <c>onButtonClick()</c> → <c>switchListBox()</c>（603-606），
    ///   而弹层开着时按下被消息过滤器吃掉（364-393），于是「点组合框 = 收」而不是「关旧开新」；
    /// - <c>ComboBoxListBox</c> 单选变更 → <c>setSelectedItemIndex</c> 回填词条（593-600、290-303），
    ///   列表里鼠标抬起 → <c>closeListBox()</c>（568）。
    ///
    /// 未移植（登记在交接报告）：可编辑词条（editable/Entry 光标/onEntryChange）、
    /// 组合框自身 sizeHint 反推宽度（combobox.cpp:438-452，本移植宽度由调用方给定）、
    /// useCustomWidget、kFocusEnterMessage 的 Buddy 来源开合（402-407）。
    /// </summary>
    public sealed class AseComboBox : MonoBehaviour, IPointerDownHandler
    {
        /// <summary>箭头钮宽 = combobox_button(mini_button) border 3+3 + 图标 9（theme.xml 件表）。</summary>
        public const float ButtonWidth = 15f;

        /// <summary>词条文字左缩 = <c>combobox</c> 样式 sunken2 切片（w1=5）。</summary>
        public const float EntryTextInset = 5f;

        Image _entryFace;
        TextMeshProUGUI _value;
        Button _button;
        string[] _options = new string[0];
        int _selected;
        Action<int> _onPick;
        Transform _popupHost;
        AseComboBoxPopup _popup;

        /// <summary>当前选中项下标（combobox.cpp:285-288 getSelectedItemIndex）。</summary>
        public int SelectedIndex { get { return _selected; } }

        /// <summary>弹层是否开着（combobox.cpp m_window 的生命周期）。</summary>
        public bool IsOpen { get { return _popup != null; } }

        /// <summary>值文本件（AseDialogLoader 注册 id 用；同时是组合框根的直系子件）。</summary>
        public TextMeshProUGUI ValueLabel { get { return _value; } }

        internal TextMeshProUGUI Build(RectTransform root, string[] options, int initial,
            Action<int> onPick, Transform popupHost)
        {
            if (options == null || options.Length == 0)
                options = new[] { string.Empty };
            _options = options;
            _selected = Mathf.Clamp(initial, 0, options.Length - 1);
            _onPick = onPick;
            _popupHost = popupHost != null ? popupHost : root.parent;

            // 词条面：combobox 样式（sunken2_normal / state="focus" 换 sunken2_focused）；
            // ComboBox::onResize(423-436)：钮占右缘 ButtonWidth，词条占其余 —— 面与钮各画各的
            RectTransform entry = UiKit.CreateRect("Entry", root);
            entry.anchorMin = new Vector2(0f, 0f);
            entry.anchorMax = new Vector2(1f, 1f);
            entry.pivot = new Vector2(0.5f, 0.5f);
            entry.offsetMin = Vector2.zero;
            entry.offsetMax = new Vector2(-ButtonWidth, 0f);
            var face = entry.gameObject.AddComponent<Image>();
            face.sprite = PixelSkin.Ase("sunken2_normal");
            face.type = Image.Type.Sliced;
            face.pixelsPerUnitMultiplier = 1f;
            face.color = Color.white;
            face.raycastTarget = true;
            _entryFace = face;

            // 词条文字（Entry::drawEntryText 按 clientBounds；combobox 样式 border = sunken2 切片 5）
            _value = UiKit.CreateText("Value", root, options[_selected], UiSkin.Font.Tiny,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, DebugWindowKit.HandFont);
            _value.enableWordWrapping = false;
            _value.raycastTarget = false;
            RectTransform valueRect = _value.rectTransform;
            valueRect.anchorMin = new Vector2(0f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(0.5f, 0.5f);
            valueRect.offsetMin = new Vector2(EntryTextInset, 0f);
            valueRect.offsetMax = new Vector2(-(ButtonWidth + EntryTextInset), 0f);

            // 箭头钮：mini_button 态（theme combobox_button extends mini_button padding 0）
            RectTransform buttonRect = UiKit.CreateRect("ComboButton", root);
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(ButtonWidth, 0f);
            buttonRect.anchoredPosition = Vector2.zero;
            var buttonFace = buttonRect.gameObject.AddComponent<Image>();
            buttonFace.sprite = PixelSkin.Ase("buttonset_item_normal");
            buttonFace.type = Image.Type.Sliced;
            buttonFace.pixelsPerUnitMultiplier = 1f;
            buttonFace.color = Color.white;
            _button = buttonRect.gameObject.AddComponent<Button>();
            _button.transition = Selectable.Transition.SpriteSwap;
            _button.spriteState = new SpriteState
            {
                highlightedSprite = PixelSkin.Ase("buttonset_item_hot"),    // mini_button state="mouse"
                pressedSprite = PixelSkin.Ase("buttonset_item_pushed"),     // state="selected"=capture
                disabledSprite = PixelSkin.Ase("buttonset_item_normal"),    // "mouse disabled" 回落常态件
            };
            // Button::Click 信号（combobox.cpp:98）→ switchListBox
            _button.onClick.AddListener(Toggle);

            // 图标在钮客户区（border 3/3/3/5）里 CENTER|MIDDLE：客户区中心比钮中心高 1 格
            RectTransform arrow = UiKit.CreateRect("Arrow", buttonRect);
            arrow.anchorMin = arrow.anchorMax = arrow.pivot = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(9f, 8f);
            arrow.anchoredPosition = new Vector2(0f, 1f);
            var arrowImage = arrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = PixelSkin.Ase("combobox_arrow_down");
            arrowImage.raycastTarget = false;
            // 图标按压变体（theme combobox_button：icon state="selected" → arrow_down_selected）
            buttonRect.gameObject.AddComponent<AseComboBoxArrow>().Bind(arrowImage);

            return _value;
        }

        // combobox.cpp:486-499 ComboBoxEntry::kMouseDownMessage → switchListBox()
        public void OnPointerDown(PointerEventData eventData)
        {
            Toggle();
        }

        /// <summary>开合切换（combobox.cpp:684-690 switchListBox）。</summary>
        public void Toggle()
        {
            if (IsOpen)
                CloseListBox();
            else
                OpenListBox();
        }

        /// <summary>combobox.cpp:608-662 openListBox：建 Window(WithoutTitleBar)+View+ListBox，
        /// 位置 = 词条下方、宽度 = 组合框宽、高度 = Σ 项高（钳进 [textHeight, maxVal]）。</summary>
        public void OpenListBox()
        {
            if (!isActiveAndEnabled || _popup != null)   // combobox.cpp:610
                return;

            AseMenuKit.CloseAll();     // 同屏只留一层弹层（源里 m_window 唯一、开新即关旧；菜单包重写后旧 ClosePopup 并入 CloseAll）

            RectTransform comboRect = (RectTransform)transform;
            RectTransform host = _popupHost as RectTransform;
            if (host == null)
                host = comboRect.parent as RectTransform;
            if (host == null)
                return;

            float w = Mathf.Round(comboRect.rect.width);
            float h = Mathf.Round(comboRect.rect.height);
            Rect hostRect = host.rect;

            // 组合框左上角在宿主局部空间（Aseprite 里 bounds() 是全树同坐标系的绝对坐标，
            // 这里用 InverseTransformPoint 换到宿主锚参考系，等价）
            var corners = new Vector3[4];
            comboRect.GetWorldCorners(corners);   // 0 左下 / 1 左上 / 2 右上 / 3 右下
            Vector2 localTopLeft = host.InverseTransformPoint(corners[1]);
            float anchorX = -hostRect.width * host.pivot.x;
            float anchorY = hostRect.height * (1f - host.pivot.y);
            float comboLeft = localTopLeft.x - anchorX;   // 距宿主左缘
            float comboTop = anchorY - localTopLeft.y;    // 距宿主上缘（向下为正）
            float entryBottom = comboTop + h;             // == entryBounds.y2()

            // 弹层高度（combobox.cpp:629-640）：**先钳视口高**（size.h = Σ 项高，钳进
            // [textHeight, maxVal]），再加 View 边框得到窗口高——源里窗口尺寸由
            // viewport 的 sizeHint 反推（remapWindow），边框是后加的。
            float itemsH = _options.Length * AseListBox.RowHeight;
            float viewBorderW = AseComboBoxPopup.ViewBorderLeft + AseComboBoxPopup.ViewBorderRight;
            float viewBorderH = AseComboBoxPopup.ViewBorderTop + AseComboBoxPopup.ViewBorderBottom;
            float maxVal = Mathf.Max(comboTop, hostRect.height - entryBottom) - 8f;
            float minVal = UiSkin.Font.Tiny;   // textHeight()
            float viewH = Mathf.Round(Mathf.Clamp(itemsH, minVal, Mathf.Max(minVal, maxVal)));
            float popupH = viewH + viewBorderH;

            float itemsW = w - viewBorderW;    // combobox.cpp:628 size.w = button.x2 - entry.x - view border

            // 位置：候选矩形 = 词条左下到钮右下（combobox.cpp:692-707 updateListBoxPos），
            // fit_bounds 的 fitLogic 先把越底界者翻到词条上方，再夹进 workarea
            float left = Mathf.Round(Mathf.Clamp(comboLeft, 0f, Mathf.Max(0f, hostRect.width - 1f - w)));
            float top = entryBottom;
            if (top + popupH > hostRect.height)
                top -= popupH + h;
            top = Mathf.Round(Mathf.Clamp(top, 0f, Mathf.Max(0f, hostRect.height - 1f - popupH)));

            _popup = AseComboBoxPopup.Open(this, host, w, popupH, itemsW, _options, _selected);
            RectTransform popupRect = _popup.Rect;
            popupRect.anchoredPosition = new Vector2(anchorX + left, anchorY - top);
            popupRect.SetAsLastSibling();

            // 焦点态（combobox 样式 state="focus"）：源里开弹层时焦点落在词条/列表上，
            // 组合框作为焦点祖先带 HAS_FOCUS —— 本移植没有焦点系统，以「弹层开着」表达
            if (_entryFace != null)
                _entryFace.sprite = PixelSkin.Ase("sunken2_focused");

            // combobox.cpp:648-659 开完 initTheme + remap；列表选中当前项由 ListBox 构造带入
        }

        /// <summary>combobox.cpp:664-682 closeListBox：收弹层、焦点回词条、面回常态。</summary>
        public void CloseListBox()
        {
            if (_popup == null)
                return;
            AseComboBoxPopup popup = _popup;
            _popup = null;
            popup.Hide();
            if (_entryFace != null)
                _entryFace.sprite = PixelSkin.Ase("sunken2_normal");
        }

        /// <summary>列表单选变更回调（combobox.cpp:593-600 → 290-303 setSelectedItemIndex）：
        /// 选中项文本回填词条 + onChange。</summary>
        internal void OnItemChanged(int index)
        {
            if (index < 0 || index >= _options.Length)
                return;
            _selected = index;
            if (_value != null)
                _value.text = _options[index];
            _onPick?.Invoke(index);
        }

        // combobox.cpp:344 kCloseMessage → closeListBox（宿主窗收起即收弹层）
        void OnDisable()
        {
            CloseListBox();
        }
    }

    /// <summary>组合框箭头图标的按压变体（theme <c>combobox_button</c> 的
    /// <c>&lt;icon part="combobox_arrow_down_selected" state="selected"/&gt;</c>；
    /// 源里按钮的 selected 态就是 capture=按下）。与 <see cref="Button"/> 同 GameObject
    /// —— UGUI 的 <c>ExecuteEvents</c> 会广播给该对象上**所有**同接口组件，互不抢事件。</summary>
    internal sealed class AseComboBoxArrow : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        Image _arrow;

        public void Bind(Image arrow)
        {
            _arrow = arrow;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Set(PixelState.Pressed);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Set(PixelState.Normal);
        }

        void Set(PixelState state)
        {
            if (_arrow != null)
                _arrow.sprite = PixelSkin.ArrowDown(state);
        }
    }

    /// <summary>
    /// 组合框下拉弹层 = Aseprite <c>Window(WithoutTitleBar)</c> + <c>View</c> + <c>ComboBoxListBox</c>
    /// （combobox.cpp:615-649）。视觉两层：View 的 <c>background=window_face</c> #2C2C30 色层 +
    /// <c>border part=sunken_normal</c> 九宫（theme <c>view</c>：border 3、border-top 4）。
    /// 内容 = <see cref="AseListBox"/> 的行，视口溢出时按 <see cref="AseView"/>
    /// （<c>View::updateView</c> → <c>setup_scrollbars</c>）挂 theme 滚动条（12 宽，
    /// theme <c>scrollbar_size</c>）并收窄视口——横竖两根都按源的 IfNeeded 条件判定。
    ///
    /// 弹层的关闭语义（combobox.cpp）：
    /// - 列表里鼠标抬起 → 收（ComboBoxListBox::kMouseUpMessage，568）；
    /// - <c>filterMessages</c> 注册的 kMouseDownMessage 过滤器（739-755）：落点不在列表里 → 收
    ///   （364-393），且**这一击照常传给下层控件**（过滤器返回 false 后仍发给原收件控件）；
    /// - kClose/kWinMove → 收（344-349）；kKeyEsc → 收（351-362）；列表 kKey space/enter → 收（570-580）。
    /// </summary>
    internal sealed class AseComboBoxPopup : MonoBehaviour, IPointerUpHandler
    {
        /// <summary>theme <c>view</c>：border="3" border-top="4"。</summary>
        public const float ViewBorderLeft = 3f;
        public const float ViewBorderRight = 3f;
        public const float ViewBorderTop = 4f;
        public const float ViewBorderBottom = 3f;

        static readonly List<RaycastResult> s_hits = new List<RaycastResult>();

        AseComboBox _combo;
        RectTransform _rect;
        Vector3 _comboWorldAtOpen;
        int _openFrame;

        public RectTransform Rect { get { return _rect; } }

        internal static AseComboBoxPopup Open(AseComboBox combo, Transform host, float width, float height,
            float itemsWidth, string[] options, int selected)
        {
            RectTransform rect = UiKit.CreateRect("AseComboBoxPopup", host);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);

            var popup = rect.gameObject.AddComponent<AseComboBoxPopup>();
            popup._combo = combo;
            popup._rect = rect;
            popup._openFrame = Time.frameCount;
            popup._comboWorldAtOpen = combo.transform.position;
            popup.Build(itemsWidth, options, selected);
            return popup;
        }

        void Build(float itemsWidth, string[] options, int selected)
        {
            // View 面（色层）+ 边框（件层）——与 theme view 样式逐层对应
            var bg = _rect.gameObject.AddComponent<Image>();
            bg.color = PixelSkin.Theme.Face;   // window_face #2C2C30
            bg.raycastTarget = false;

            RectTransform border = UiKit.CreateRect("ViewBorder", _rect);
            UiKit.Stretch(border);
            var borderImage = border.gameObject.AddComponent<Image>();
            borderImage.sprite = PixelSkin.Ase("sunken_normal");
            borderImage.type = Image.Type.Sliced;
            borderImage.pixelsPerUnitMultiplier = 1f;
            borderImage.color = Color.white;
            borderImage.raycastTarget = true;   // 边框区也算弹层内（抬起冒泡到弹层根即收）

            // View 内核（view.cpp / scroll_helper.cpp 移植）：视口 + 按需滚动条（横竖两根）
            AseView view = AseView.Attach(_rect,
                (int)ViewBorderLeft, (int)ViewBorderTop, (int)ViewBorderRight, (int)ViewBorderBottom);

            // combobox.cpp:628 size.w = button.x2 - entry.x - view.border().width()
            AseListBox list = AseListBox.Create(view.Viewport, "ListBox", 0f, 0f, itemsWidth,
                options, selected, _combo.OnItemChanged);
            view.AttachToView((RectTransform)list.transform);

            // 内容 sizeHint（Viewport::calculateNeededSize，viewport.cpp:59-71）：
            // ListBox::onSizeHint（listbox.cpp:358-378）= 逐轴 max 行 sizeHint / Σ 行高；
            // 行 sizeHint.w = 文字宽 + list_item border 2（listitem.cpp:57-86）——
            // 横条是否出现就看它是否超过视口宽（源 IfNeeded 条件），不是看弹层宽。
            int hintW = Mathf.CeilToInt(RowHintWidth(list)) + AseListBox.ItemBorder * 2;
            int hintH = Mathf.RoundToInt(options.Length * AseListBox.RowHeight);
            view.SetContentHint(hintW, hintH);
            view.UpdateView();   // View::updateView（view.cpp:143-190）

            StretchRows(list);
        }

        /// <summary>
        /// <c>ListBox::onResize</c>（listbox.cpp:341-356）的等价：把每行铺到 ListBox 的内容宽
        /// （源里 <c>child-&gt;setBounds(childrenBounds())</c>）。行宽跟随是 ListBox 的职责、
        /// 但 <c>AseListbox.cs</c> 不在本包文件域内，故落在组合框这一侧；
        /// 只有内容被撑到初始宽之外（横向可滚 / 竖条占位）时才会真的改到行宽。
        /// </summary>
        static void StretchRows(AseListBox list)
        {
            RectTransform content = (RectTransform)list.transform;
            float w = content.sizeDelta.x;
            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform row = content.GetChild(i) as RectTransform;
                if (row != null && !Mathf.Approximately(row.sizeDelta.x, w))
                    row.sizeDelta = new Vector2(w, row.sizeDelta.y);
            }
        }

        /// <summary>max 行文字自然宽（UGUI 侧量 TMP 的 <c>preferredWidth</c>，
        /// 等价源里 <c>Widget::textSize()</c> 给 ListItem::onSizeHint 的量）。</summary>
        static float RowHintWidth(AseListBox list)
        {
            float max = 0f;
            TextMeshProUGUI[] labels = list.GetComponentsInChildren<TextMeshProUGUI>();
            for (int i = 0; i < labels.Length; i++)
                max = Mathf.Max(max, labels[i].preferredWidth);
            return max;
        }

        // combobox.cpp:565-568 ComboBoxListBox::kMouseUpMessage → closeListBox
        public void OnPointerUp(PointerEventData eventData)
        {
            Close();
        }

        void Update()
        {
            if (_combo == null)
                return;

            // combobox.cpp:344 kCloseMessage（宿主窗收起 → 组合框失活即收）
            if (!_combo.isActiveAndEnabled)
            {
                Close();
                return;
            }

            // combobox.cpp:346-349 kWinMoveMessage：宿主窗一移动就收弹层
            if ((_combo.transform.position - _comboWorldAtOpen).sqrMagnitude > 0.25f)
            {
                Close();
                return;
            }

            // combobox.cpp:351-362 kKeyEsc → 收；570-580 列表 kKey space/enter → 收
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
            {
                Close();
                return;
            }

            // combobox.cpp:364-393 kMouseDownMessage 过滤器：落点既不在列表也不在组合框内 → 收。
            // 只观察不消费：源里这一击会照常传给下层控件（返回 false → 仍发给原收件控件），
            // 铺全屏捕获板（AseMenuKit 的 MenuCatcher）会把这一击吃掉，行为不等价。
            if (Time.frameCount != _openFrame && Input.GetMouseButtonDown(0)
                && !IsPointerOverComboOrPopup())
            {
                Close();
            }
        }

        void Close()
        {
            if (_combo != null)
                _combo.CloseListBox();
            else
                Hide();
        }

        internal void Hide()
        {
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        /// <summary>鼠标（按下/抬起）是否落在组合框或弹层上。</summary>
        bool IsPointerOverComboOrPopup()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return false;
            var data = new PointerEventData(es);
            data.position = Input.mousePosition;
            s_hits.Clear();
            es.RaycastAll(data, s_hits);
            for (int i = 0; i < s_hits.Count; i++)
            {
                Transform t = s_hits[i].gameObject.transform;
                if (t == transform || t.IsChildOf(transform))
                    return true;
                if (_combo != null && (t == _combo.transform || t.IsChildOf(_combo.transform)))
                    return true;
            }
            return false;
        }
    }
}
