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
    internal sealed class AseComboBoxPopup : MonoBehaviour, IPointerUpHandler, IPointerDownHandler
    {
        /// <summary>theme <c>view</c>：border="3" border-top="4"（公共口径见 <see cref="AseWidgetKit"/>）。</summary>
        public const float ViewBorderLeft = AseWidgetKit.ViewBorderLeft;
        public const float ViewBorderRight = AseWidgetKit.ViewBorderRight;
        public const float ViewBorderTop = AseWidgetKit.ViewBorderTop;
        public const float ViewBorderBottom = AseWidgetKit.ViewBorderBottom;

        static readonly List<RaycastResult> s_hits = new List<RaycastResult>();

        AseComboBox _combo;
        RectTransform _rect;
        Vector3 _comboWorldAtOpen;
        int _openFrame;

        public RectTransform Rect { get { return _rect; } }

        /// <summary>弹层也是顶层窗（源 Manager 直属）：按下即浮顶（combobox.cpp 开窗即
        /// openWindow 入 Manager 栈顶；同帧后开的窗可能压过它，点击要能夺回）。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            WindowDragger.RaiseToCanvasTop(_rect);
        }

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
            // View 面（色层）+ 边框（件层）——公共实现与口径注释见 AseWidgetKit.PaintViewSkin
            // （边框区接光：抬起冒泡到弹层根即收）
            AseWidgetKit.PaintViewSkin(_rect);

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
