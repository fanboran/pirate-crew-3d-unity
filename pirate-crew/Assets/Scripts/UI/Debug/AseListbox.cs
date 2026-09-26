using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite <c>ListBox</c> + <c>ListItem</c> 语义移植（<c>src/ui/listbox.cpp</c>、
    /// <c>src/ui/listitem.cpp</c> + theme.xml 的 <c>list_item</c> 样式）。只做调试面板需要的
    /// **单选**子集（视口溢出时由承载的 ScrollRect 竖向滚动）：
    ///
    /// - 行高 = 文本高 + border(1)×2 —— listitem.cpp:57-86 <c>onSizeHint = textSize + border</c>；
    /// - 行宽 = 视口宽 —— listbox.cpp:341-356 <c>onResize</c> 把每个子件铺到 <c>childrenBounds()</c>；
    /// - 选中是**唯一态** —— listbox.cpp:112 单选分支 <c>newState = (child == item)</c>；
    ///   面色 <c>listitem_selected_face</c> #E1B85F + 字 <c>listitem_selected_text</c> #41444A；
    /// - 常态面色 <c>listitem_normal_face</c> #41444A + 字 <c>listitem_normal_text</c> #C0C0C0；
    /// - **行没有悬停态** —— theme.xml <c>list_item</c> 只有 normal/selected/disabled 三层，
    ///   没有 <c>state="mouse"</c>。此前画廊列表行悬停变暗 #2C2C30 + 字灰 #7D7D7D 抄的是
    ///   <c>recent_file</c>/<c>news_item</c>（弹层里的文件行，用 menuitem_hot 对）的语法，不是列表行。
    /// - 按下即选 + 按住拖动扫选 —— listbox.cpp:201-256：<c>kMouseDownMessage</c> 先
    ///   <c>captureMouse()</c>，随后带 capture 的 <c>kMouseMoveMessage</c> 持续
    ///   <c>selectChild(拾取到的 ListItem)</c>；<c>kMouseUpMessage</c> 释放（listbox.cpp:250-256）。
    ///   这里用「行按下置 pressed + 鼠标键仍按住时行 enter 即选」复刻；捕获的释放用
    ///   <c>Input.GetMouseButton</c> 兜底（鼠标在面板外松开也能复位，同源里 capture 丢失的处理）。
    /// - <c>onChange</c> 只在选中态真的变化时发 —— listbox.cpp:115-127 <c>didChange</c>。
    /// - **键盘导航** —— listbox.cpp:263-328 <c>kKeyDownMessage</c>（上下/Home/End/PageUp/PageDown
    ///   选行、左右横向滚动）+ listbox.cpp:397-438 <c>advanceIndexThroughVisibleItems</c>/
    ///   <c>findParentListItem</c>；选行后滚动到可见 = listbox.cpp:145-160 <c>makeChildVisible</c>
    ///   （selectChild 里调）；开弹层时居中选中项 = listbox.cpp:163-176 <c>centerScroll</c>
    ///   （kOpenMessage，listbox.cpp:197）。键盘接管判定 = listbox.cpp:392-395
    ///   <c>onAcceptKeyInput()</c>=<c>hasFocus()</c>，本工程无焦点系统，等价落成
    ///   「列表挂在开着的组合框弹层里」（见 <see cref="AcceptKeyInput"/>）。
    ///
    /// 未移植（登记在交接报告）：多选 multiselect 分支（76-110）、<c>findParentListItem</c>
    /// （440-450，本移植只允许直接子件命中）、HIDDEN 行与 Separator 行的导航跳过
    /// （本移植的行表里没有这两类，故源码里那两条过滤恒真）、左右键横向滚动
    /// （内容宽恒等于视口宽，无横向溢出可滚，见 <see cref="HandleKeyboard"/> 的 Left/Right）。
    /// </summary>
    public sealed class AseListBox : MonoBehaviour
    {
        /// <summary>theme <c>list_item</c> border=1（四边）。</summary>
        public const int ItemBorder = 1;

        /// <summary>行高 = 文本高 + border 上下之和（listitem.cpp:82-85）。</summary>
        public static float RowHeight { get { return UiSkin.Font.Tiny + ItemBorder * 2f; } }

        readonly List<AseListItem> _rows = new List<AseListItem>();
        int _selected = -1;
        bool _pressed;
        Action<int> _onChange;

        /// <summary>待居中计数：>=0 = kOpenMessage 的 centerScroll 还没跑成（弹层那帧 ScrollRect
        /// 尚未挂上/还没布局），再试几帧后放弃（listbox.cpp:197）。</summary>
        int _centerTries = -1;

        /// <summary>当前选中行下标（无选中 = -1）——listbox.cpp:55-67 getSelectedIndex。</summary>
        public int SelectedIndex { get { return _selected; } }

        public int ItemsCount { get { return _rows.Count; } }

        /// <summary>列表面（skin_theme.cpp:1182-1185 <c>kListBoxWidget</c>：BORDER(0) + childSpacing 0，
        /// 所以 ListBox 自身无面无框，行才是可见件）。</summary>
        public static AseListBox Create(RectTransform parent, string name, float x, float y,
            float width, string[] labels, int selected, Action<int> onChange = null)
        {
            width = Mathf.Round(width);
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, labels.Length * RowHeight);

            var box = rect.gameObject.AddComponent<AseListBox>();
            box.Build(rect, width, labels, selected, onChange);
            return box;
        }

        void Build(RectTransform rect, float width, string[] labels, int selected, Action<int> onChange)
        {
            _onChange = onChange;
            for (int i = 0; i < labels.Length; i++)
                _rows.Add(AseListItem.Create(this, rect, i, labels[i], width, i * RowHeight));

            // 开弹层时把当前项置为选中（combobox.cpp:646 m_listbox->selectIndex(m_selected)）——
            // 首建不算「变更」，不发 onChange（源里 selectIndex → didChange 也会被
            // ComboBoxListBox::onChange 的 m_selected != index 挡掉，净效果相同）。
            if (selected >= 0 && selected < _rows.Count)
            {
                _selected = selected;
                _rows[selected].SetSelected(true);
                _centerTries = 0;   // kOpenMessage → centerScroll（listbox.cpp:197）
            }
        }

        internal void NotifyRowDown(AseListItem item)
        {
            _pressed = true;
            SelectChild(item);
        }

        internal void NotifyRowEnter(AseListItem item)
        {
            if (_pressed)
                SelectChild(item);
        }

        /// <summary>单选语义（listbox.cpp:69-128）：newState = (child == item)，
        /// 有变化才 onChange；空 item 会清空全部选中。滚到可见与 onChange 都按源码次序
        /// （makeChildVisible 无条件、onChange 只在 didChange，listbox.cpp:136-138）。</summary>
        public void SelectChild(AseListItem item)
        {
            int index = item != null ? item.Index : -1;
            bool didChange = index != _selected;

            if (didChange)
            {
                if (_selected >= 0 && _selected < _rows.Count)
                    _rows[_selected].SetSelected(false);
                _selected = index;
                if (index >= 0 && index < _rows.Count)
                    _rows[index].SetSelected(true);
            }

            // listbox.cpp:136-137：选中后把该行滚进视口（View 不存在则不动）
            if (item != null)
                MakeChildVisible(index);

            if (didChange)
                _onChange?.Invoke(_selected);
        }

        /// <summary>选中第 index 项（listbox.cpp:130-138：越界静默返回）。</summary>
        public void SelectIndex(int index)
        {
            if (index < 0 || index >= _rows.Count)
                return;
            SelectChild(_rows[index]);
        }

        // listbox.cpp:250-256 kMouseUpMessage → releaseMouse（捕获释放；面板外松开也复位）
        void Update()
        {
            if (_pressed && !Input.GetMouseButton(0))
                _pressed = false;

            // kOpenMessage → centerScroll（listbox.cpp:197）：弹层那帧 ScrollRect 还没挂上，
            // 故延后到拿到 View 的那一帧做；试几次拿不到（本列表不滚动）就放弃。
            if (_centerTries >= 0)
            {
                RectTransform content;
                float viewportH;
                if (TryView(out content, out viewportH))
                {
                    CenterScroll();
                    _centerTries = -1;
                }
                else if (++_centerTries > 2)
                {
                    _centerTries = -1;
                }
            }

            HandleKeyboard();
        }

        // ------------------------------------------------------------------
        // 键盘导航（listbox.cpp:263-328 kKeyDownMessage）
        //   + advanceIndexThroughVisibleItems（listbox.cpp:397-438）
        //   + makeChildVisible / centerScroll（listbox.cpp:145-176）
        // ------------------------------------------------------------------

        /// <summary>
        /// listbox.cpp:392-395 <c>onAcceptKeyInput()</c>（源码 = <c>hasFocus()</c>）。
        /// 本工程无焦点系统，等价判定 = **列表是「当前弹层列表」**：沿父链找到开着的组合框弹层
        /// <see cref="AseComboBoxPopup"/> 即接管（弹层件只在开的时候存在，存在即开着；
        /// 和菜单侧「弹层链开着 = 菜单接管键盘」同一口径）。画廊里平铺的列表不在弹层内 → 不接管。
        /// </summary>
        bool AcceptKeyInput()
        {
            return _rows.Count > 0 && GetComponentInParent<AseComboBoxPopup>() != null;
        }

        void HandleKeyboard()
        {
            if (!AcceptKeyInput())
                return;                                  // listbox.cpp:264 onAcceptKeyInput()

            int bottom = Mathf.Max(0, _rows.Count - 1);   // listbox.cpp:266
            int select = _selected;                       // listbox.cpp:265 getSelectedIndex()

            KeyCode scancode;
            if (Input.GetKeyDown(KeyCode.UpArrow)) scancode = KeyCode.UpArrow;
            else if (Input.GetKeyDown(KeyCode.DownArrow)) scancode = KeyCode.DownArrow;
            else if (Input.GetKeyDown(KeyCode.Home)) scancode = KeyCode.Home;
            else if (Input.GetKeyDown(KeyCode.End)) scancode = KeyCode.End;
            else if (Input.GetKeyDown(KeyCode.PageUp)) scancode = KeyCode.PageUp;
            else if (Input.GetKeyDown(KeyCode.PageDown)) scancode = KeyCode.PageDown;
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) scancode = KeyCode.LeftArrow;
            else if (Input.GetKeyDown(KeyCode.RightArrow)) scancode = KeyCode.RightArrow;
            else return;                                  // default: return Widget::onProcessMessage（无事可做）

            // keymsg->onlyCmdPressed()（listbox.cpp:271-276）：只按 Cmd/Ctrl 时 上/下 = Home/End。
            // 本工程把 Ctrl/Command 当 Cmd（approx：源码按平台二选一），"only" 落成无 Shift 无 Alt。
            if (CtrlOrCmdHeld() && !ShiftHeld() && !AltHeld())
            {
                if (scancode == KeyCode.UpArrow) scancode = KeyCode.Home;
                else if (scancode == KeyCode.DownArrow) scancode = KeyCode.End;
            }

            RectTransform content;
            float viewportH;
            bool hasView = TryView(out content, out viewportH);   // View::getView(this)
            // textHeight()（Widget::textHeight，widget.cpp:969-977 = 字体 descent-ascent 跨度）——
            // 用与菜单同源的度量口径（AseMenuKit.TextHeight 就是按 TMP faceInfo 算的这个）
            float textH = Mathf.Max(1f, AseMenuKit.TextHeight(UiSkin.Font.Tiny));

            switch (scancode)
            {
                case KeyCode.UpArrow:
                    // 有选中 → 上一个；没选中 → 从表尾往上挑第一个（listbox.cpp:279-289）
                    if (select >= 0)
                        select = AdvanceIndexThroughVisibleItems(select, -1, true);
                    else
                        select = AdvanceIndexThroughVisibleItems(bottom + 1, -1, true);
                    break;
                case KeyCode.DownArrow:
                    select = AdvanceIndexThroughVisibleItems(select, +1, true);
                    break;
                case KeyCode.Home:
                    select = AdvanceIndexThroughVisibleItems(-1, +1, false);
                    break;
                case KeyCode.End:
                    select = AdvanceIndexThroughVisibleItems(bottom + 1, -1, false);
                    break;
                case KeyCode.PageUp:
                    if (hasView)
                        select = AdvanceIndexThroughVisibleItems(select, -(int)(viewportH / textH), false);
                    else
                        select = 0;
                    break;
                case KeyCode.PageDown:
                    if (hasView)
                        select = AdvanceIndexThroughVisibleItems(select, +(int)(viewportH / textH), false);
                    else
                        select = bottom;
                    break;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    // listbox.cpp:310-321：横向滚 viewport.w/2。本移植的列表内容宽恒等于视口宽
                    // （无横向溢出、无横向滚动条），故照源码位置置空转——登记 approx。
                    break;
            }

            SelectIndex(Mathf.Clamp(select, 0, bottom));   // listbox.cpp:325
        }

        /// <summary>listbox.cpp:397-438 advanceIndexThroughVisibleItems：从 startIndex 沿 delta
        /// 方向走 |delta| 个**可见项**（跳过 HIDDEN 与 Separator——本移植行表里没有这两类，恒真）；
        /// 走到头且 loop 则绕回（绕一圈回来即停）。</summary>
        int AdvanceIndexThroughVisibleItems(int startIndex, int delta, bool loop)
        {
            int bottom = Mathf.Max(0, _rows.Count - 1);
            int sgn = Math.Sign(delta);
            int index = startIndex;

            startIndex = Mathf.Clamp(startIndex, 0, bottom);
            int lastVisibleIndex = startIndex;

            bool cycle = false;

            while (delta != 0)
            {
                index += sgn;

                if (cycle && index == startIndex)
                {
                    break;
                }
                else if (index < 0)
                {
                    if (!loop)
                        break;
                    index = bottom - sgn;
                    cycle = true;
                }
                else if (index > bottom)
                {
                    if (!loop)
                        break;
                    index = 0 - sgn;
                    cycle = true;
                }
                else if (index >= 0 && index < _rows.Count)
                {
                    AseListItem item = _rows[index];
                    if (item != null)
                    {
                        lastVisibleIndex = index;
                        delta -= sgn;
                    }
                }
            }
            return lastVisibleIndex;
        }

        /// <summary>listbox.cpp:145-160 makeChildVisible：把第 index 行滚进视口。</summary>
        public void MakeChildVisible(int index)
        {
            if (index < 0 || index >= _rows.Count)
                return;

            RectTransform content;
            float viewportH;
            if (!TryView(out content, out viewportH))
                return;                                  // View::getView(this) == null → 不动

            float scrollY = -content.anchoredPosition.y;  // view->viewScroll().y
            float rowTop = index * RowHeight;
            float rowH = RowHeight;

            if (rowTop < scrollY)
                scrollY = rowTop;
            else if (rowTop > scrollY + viewportH - rowH)
                scrollY = rowTop - viewportH + rowH;

            SetScrollY(content, viewportH, scrollY);
        }

        /// <summary>listbox.cpp:163-176 centerScroll：开弹层时把选中行摆到视口正中。</summary>
        public void CenterScroll()
        {
            if (_selected < 0 || _selected >= _rows.Count)
                return;

            RectTransform content;
            float viewportH;
            if (!TryView(out content, out viewportH))
                return;

            float scrollY = _selected * RowHeight
                - Mathf.FloorToInt(viewportH * 0.5f)      // vp.h / 2（整数除）
                + Mathf.FloorToInt(RowHeight * 0.5f);     // item->bounds().h / 2
            SetScrollY(content, viewportH, scrollY);
        }

        /// <summary>承载本列表的 View 等价物：视口上的 <see cref="ScrollRect"/>
        /// （View::getView(this)，listbox.cpp:147）。<paramref name="viewportH"/> 为视口高（画布像素）。</summary>
        bool TryView(out RectTransform content, out float viewportH)
        {
            content = null;
            viewportH = 0f;

            ScrollRect scroll = GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null)
                return false;

            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float h = viewport.rect.height;
            if (h <= 0f)
                return false;

            content = scroll.content;
            viewportH = Mathf.Round(h);
            return true;
        }

        /// <summary>view->setViewScroll（纵轴）：内容顶对齐时 y=0（content.anchoredPosition.y == 0，
        /// pivot (0,1) 锚顶左），钳进 [0, contentH - viewportH]。ScrollRect 的 LateUpdate 会在
        /// 同帧用 <c>UpdateScrollbars</c> 把滚动条拇指同步到本位置（ugui ScrollRect.cs:916-934）。
        /// 横向不动（本列表无横向溢出）。</summary>
        void SetScrollY(RectTransform content, float viewportH, float y)
        {
            float max = Mathf.Max(0f, _rows.Count * RowHeight - viewportH);
            y = Mathf.Round(Mathf.Clamp(y, 0f, max));
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, -y);
        }

        static bool AltHeld()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        static bool CtrlOrCmdHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        }
    }

    /// <summary>列表行（listitem.cpp：<c>ListItem</c>，kListItemWidget）。行面与字色直接写
    /// list_item 样式的色层（theme 这里给的是 <c>&lt;background color=…&gt;</c> 色层而非件，
    /// 所以是纯色 Image，不是乘色贴图）。</summary>
    public sealed class AseListItem : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
    {
        AseListBox _owner;
        Image _face;
        TextMeshProUGUI _label;

        public int Index { get; private set; }

        public string Text { get { return _label != null ? _label.text : string.Empty; } }

        internal static AseListItem Create(AseListBox owner, RectTransform parent, int index, string text,
            float width, float y)
        {
            RectTransform rect = UiKit.CreateRect("Item" + index, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, AseListBox.RowHeight);
            rect.anchoredPosition = new Vector2(0f, -y);

            var item = rect.gameObject.AddComponent<AseListItem>();
            item._owner = owner;
            item.Index = index;

            var face = rect.gameObject.AddComponent<Image>();
            face.color = PixelSkin.Theme.Background;   // listitem_normal_face #41444A
            face.raycastTarget = true;
            item._face = face;

            // list_item 文字层：align left middle x=1（字色 #C0C0C0 / 选中 #41444A）
            TextMeshProUGUI label = UiKit.CreateText("Text", rect, text, UiSkin.Font.Tiny,
                TextAlignmentOptions.Left, PixelSkin.Theme.Text, DebugWindowKit.HandFont);
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(width - AseListBox.ItemBorder * 2f, AseListBox.RowHeight);
            labelRect.anchoredPosition = new Vector2(AseListBox.ItemBorder, 0f);
            item._label = label;

            return item;
        }

        internal void SetSelected(bool on)
        {
            _face.color = on ? PixelSkin.Theme.Selected : PixelSkin.Theme.Background;
            _label.color = on ? PixelSkin.Theme.SelectedText : PixelSkin.Theme.Text;
        }

        // listbox.cpp:201-247：listbox 先 captureMouse，再由拾取到的子件走 selectChild
        public void OnPointerDown(PointerEventData eventData)
        {
            _owner.NotifyRowDown(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _owner.NotifyRowEnter(this);
        }
    }
}
