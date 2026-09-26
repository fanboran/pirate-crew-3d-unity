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
    /// **单选、可无滚动**子集：
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
    ///
    /// 未移植（登记在交接报告）：键盘导航 <c>advanceIndexThroughVisibleItems</c>
    /// （listbox.cpp:397-438）、<c>makeChildVisible</c>/<c>centerScroll</c>（145-176）、多选
    /// multiselect 分支（76-110）、<c>findParentListItem</c>（440-450，本移植只允许直接子件命中）。
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
        /// 有变化才 onChange；空 item 会清空全部选中。</summary>
        public void SelectChild(AseListItem item)
        {
            int index = item != null ? item.Index : -1;
            if (index == _selected)
                return;

            if (_selected >= 0 && _selected < _rows.Count)
                _rows[_selected].SetSelected(false);
            _selected = index;
            if (index >= 0 && index < _rows.Count)
                _rows[index].SetSelected(true);

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
