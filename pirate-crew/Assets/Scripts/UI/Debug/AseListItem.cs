using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>列表行（listitem.cpp：<c>ListItem</c>，kListItemWidget）。行面与字色直接写
    /// list_item 样式的色层（theme 这里给的是 <c>&lt;background color=…&gt;</c> 色层而非件，
    /// 所以是纯色 Image，不是乘色贴图）。</summary>
    public sealed class AseListItem : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
    {
        AseListBox _owner;
        Image _face;
        TextMeshProUGUI _label;

        public int Index { get; private set; }

        /// <summary>选中态（= 源 <c>Widget::isSelected()</c>；多选时每行各自持态）。</summary>
        public bool IsSelected { get; private set; }

        public string Text { get { return _label != null ? _label.text : string.Empty; } }

        internal static AseListItem Create(AseListBox owner, RectTransform parent, int index, string text,
            float width, float y)
        {
            RectTransform rect = UiKit.CreateRect("Item" + index, parent);
            UiKit.PlaceTopLeft(rect, 0f, y, new Vector2(width, AseListBox.RowHeight));

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
            UiKit.SetAnchored(labelRect, new Vector2(0f, 0.5f),
                new Vector2(width - AseListBox.ItemBorder * 2f, AseListBox.RowHeight),
                new Vector2(AseListBox.ItemBorder, 0f));
            item._label = label;

            return item;
        }

        internal void SetSelected(bool on)
        {
            IsSelected = on;
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
