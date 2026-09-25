using System.Collections.Generic;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite 菜单系统复刻（theme menubar/menu/menuitem 语义）：
    ///
    /// 【菜单栏】平铺标题项（theme menuitem_normal_text #C0C0C0），悬停/展开 =
    /// menuitem_highlight（亮面 #C0C0C0 + 深字 #2C2C30——menu.cpp:549 拾取即高亮，
    /// 源码实锄走 highlight 分支而非 hot 档），点击在其下弹出下拉。
    /// 【下拉】theme <c>menu</c> 直切件边框（16×16 切 3/10/3×3/9/4）+ 面色 menuitem_normal_face
    /// #2C2C30；行 = 文字（x=14 勾选图标位之后）+ 右对齐快捷键 + 分隔线（separator_horz）+
    /// 子菜单箭头（combobox_arrow_right 件）；勾选行画 check_selected（x=2，theme check_box 图标位）。
    /// 【外点关闭】下拉打开时铺一块全屏透明捕获板，点它关闭（再点穿透）。
    /// </summary>
    public static class AseMenuKit
    {
        /// <summary>菜单行高（theme menuitem：字 8 + 上下边 3 ≈ 14）。</summary>
        const float RowH = 14f;
        const float IconX = 2f;
        const float TextX = 14f;
        const float ShortcutRight = 8f;
        const float ArrowRight = 5f;

        /// <summary>一条菜单项声明（分隔线 = Label 空且 <see cref="Separator"/>）。</summary>
        public sealed class Item
        {
            public string Label;
            public string Shortcut;
            public bool Separator;
            public bool Checked;
            public Item[] Children;      // 子菜单（悬停/点击展开）
            public System.Action Action; // 叶子点击
        }

        public static Item Item_(string label, string shortcut = null, System.Action action = null,
            bool check = false, Item[] children = null)
        {
            return new Item { Label = label, Shortcut = shortcut, Action = action, Checked = check, Children = children };
        }

        public static Item Sep() => new Item { Separator = true };

        // ------------------------------------------------------------------
        // 菜单栏
        // ------------------------------------------------------------------

        /// <summary>
        /// 建菜单栏（横向标题行）。返回栏根（顶左锚，高 14）——调用方按 return 自行定位。
        /// </summary>
        public static RectTransform BuildMenuBar(Transform parent, string name,
            (string title, Item[] items)[] menus)
        {
            var bar = new GameObject(name, typeof(RectTransform));
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.SetParent(parent, false);
            barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0f, 1f);

            float x = 0f;
            foreach ((string title, Item[] items) in menus)
            {
                var itemGo = new GameObject("Menu_" + title, typeof(RectTransform));
                RectTransform itemRect = itemGo.GetComponent<RectTransform>();
                itemRect.SetParent(barRect, false);
                itemRect.anchorMin = itemRect.anchorMax = itemRect.pivot = new Vector2(0f, 1f);

                TextMeshProUGUI label = DebugWindowKit.Label(itemRect, title, UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text, TextAlignmentOptions.MidlineRight);
                label.alignment = TextAlignmentOptions.Center;
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(4f, 0f);
                labelRect.offsetMax = new Vector2(-4f, 0f);
                label.raycastTarget = false;

                var face = itemGo.AddComponent<Image>();
                face.color = new Color32(0x2C, 0x2C, 0x30, 0xFF);   // menuitem_normal_face
                face.raycastTarget = true;

                float w = Mathf.Ceil(label.preferredWidth) + 8f;
                itemRect.sizeDelta = new Vector2(w, RowH);
                itemRect.anchoredPosition = new Vector2(x, 0f);
                x += w;

                Item[] captured = items;
                RectTransform capturedRect = itemRect;
                var handler = itemGo.AddComponent<MenuTile>();
                handler.Bind(face, label,
                    open => ApplyHighlight(face, label, open),
                    () => OpenPopup(parent, capturedRect, captured));
            }
            barRect.sizeDelta = new Vector2(x, RowH);
            return barRect;
        }

        static void ApplyHighlight(Image face, TextMeshProUGUI label, bool on)
        {
            // menuitem_highlight 对（menu.cpp:549 拾取即高亮）：亮面 + 深字
            face.color = on
                ? new Color32(0xC0, 0xC0, 0xC0, 0xFF)
                : new Color32(0x2C, 0x2C, 0x30, 0xFF);
            label.color = on
                ? new Color32(0x2C, 0x2C, 0x30, 0xFF)
                : PixelSkin.Theme.Text;
        }

        // ------------------------------------------------------------------
        // 下拉弹层
        // ------------------------------------------------------------------

        /// <summary>当前打开的弹层（同屏一份：开新关旧）。</summary>
        static GameObject s_open;

        /// <summary>在 <paramref name="anchor"/>（栏项）下方弹出一层菜单。</summary>
        public static void OpenPopup(Transform overlay, RectTransform anchor, Item[] items)
        {
            ClosePopup();
            float rowW = MeasureRows(items);

            var popup = new GameObject("AseMenuPopup", typeof(RectTransform));
            RectTransform popupRect = popup.GetComponent<RectTransform>();
            popupRect.SetParent(overlay, false);
            popupRect.anchorMin = popupRect.anchorMax = popupRect.pivot = new Vector2(0f, 1f);

            // 边框 = theme menu 直切件（3/10/3 × 3/9/4）；面色垫在框内
            var border = popup.AddComponent<Image>();
            border.sprite = PixelSkin.Ase("menu");
            border.type = Image.Type.Sliced;
            border.pixelsPerUnitMultiplier = 1f;
            border.color = Color.white;
            border.raycastTarget = false;
            var faceRect = UiKit.CreateRect("Face", popupRect);
            faceRect.anchorMin = faceRect.anchorMax = new Vector2(0.5f, 0.5f);
            faceRect.pivot = new Vector2(0.5f, 0.5f);
            faceRect.offsetMin = new Vector2(3f, 4f);      // 左/下切片
            faceRect.offsetMax = new Vector2(-3f, -3f);    // 右/上切片
            var face = faceRect.gameObject.AddComponent<Image>();
            face.color = new Color32(0x2C, 0x2C, 0x30, 0xFF);   // menuitem_normal_face
            face.raycastTarget = false;

            float y = 3f;
            foreach (Item item in items)
                y = BuildRow(popupRect, popupRect, faceRect, item, rowW, y);

            float h = y + 4f;
            popupRect.sizeDelta = new Vector2(rowW + 6f, h);

            // 定位：锚项正下方（画布顶左系），不出右界
            Vector3[] corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            Vector2 local = (Vector2)overlay.InverseTransformPoint(corners[0]);
            float px = Mathf.Min(local.x, overlay.GetComponentInParent<Canvas>().GetComponent<RectTransform>().rect.width - rowW - 6f - 4f);
            popupRect.anchoredPosition = new Vector2(Mathf.Max(0f, px), local.y - 1f);

            // 全屏捕获板：点外关闭（垫在弹层之下、其它 UI 之上）
            var catcher = new GameObject("MenuCatcher", typeof(RectTransform));
            RectTransform catchRect = catcher.GetComponent<RectTransform>();
            catchRect.SetParent(overlay, false);
            var catchImage = catcher.AddComponent<Image>();
            catchImage.color = new Color(0f, 0f, 0f, 0f);
            catchImage.raycastTarget = true;
            StretchToParent(catchRect);
            catchRect.SetSiblingIndex(popupRect.GetSiblingIndex());
            var closer = catcher.AddComponent<MenuCatcher>();
            closer.Bind(() => ClosePopup());

            s_open = popup;
            popupRect.SetAsLastSibling();
        }

        public static void ClosePopup()
        {
            if (s_open == null)
                return;
            Transform overlay = s_open.transform.parent;
            DestroySafe(s_open);
            Transform catcher = overlay != null ? overlay.Find("MenuCatcher") : null;
            if (catcher != null)
                DestroySafe(catcher.gameObject);
            s_open = null;
        }

        static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }

        static float MeasureRows(Item[] items)
        {
            float w = 60f;
            foreach (Item item in items)
            {
                if (item.Separator)
                    continue;
                float need = TextX + GetStringWidth(item.Label) + 10f;
                if (!string.IsNullOrEmpty(item.Shortcut))
                    need += GetStringWidth(item.Shortcut) + 14f;
                else if (item.Children != null)
                    need += 12f;
                w = Mathf.Max(w, need);
            }
            return w;
        }

        static float GetStringWidth(string text)
        {
            // 估算：CJK 全宽 = 字号，ASCII 半宽——8px 档够用（跑真 preferredWidth 要等布局两帧）
            int wide = 0, narrow = 0;
            foreach (char c in text)
            {
                if (c > 0x2E80) wide++;
                else narrow++;
            }
            return wide * UiSkin.Font.Tiny + narrow * UiSkin.Font.Tiny * 0.5f;
        }

        static float BuildRow(Transform popup, RectTransform popupRect, RectTransform faceRect,
            Item item, float rowW, float y)
        {
            if (item.Separator)
            {
                SketchSeparator.Create(popup, "Sep", new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(3f, -y - 2f), new Vector2(rowW, 5f),
                    SketchSeparator.Direction.Horizontal);
                return y + 8f;
            }

            var rowGo = new GameObject("Row_" + item.Label, typeof(RectTransform));
            RectTransform rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.SetParent(popup, false);
            rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(rowW, RowH);
            rowRect.anchoredPosition = new Vector2(3f, -y);

            var face = rowGo.AddComponent<Image>();
            face.color = new Color32(0x2C, 0x2C, 0x30, 0xFF);
            face.raycastTarget = true;

            TextMeshProUGUI label = DebugWindowKit.Label(rowRect, item.Label, UiSkin.Font.Tiny,
                PixelSkin.Theme.Text, TextAlignmentOptions.Left);
            PlaceAt(rowRect, label.rectTransform, TextX, 0f, rowW - TextX - 4f, RowH);

            if (!string.IsNullOrEmpty(item.Shortcut))
            {
                TextMeshProUGUI shortcut = DebugWindowKit.Label(rowRect, item.Shortcut, UiSkin.Font.Tiny,
                    PixelSkin.Theme.Text, TextAlignmentOptions.Right);
                PlaceAt(rowRect, shortcut.rectTransform, 0f, 0f, rowW - ShortcutRight, RowH, fromRight: true);
                shortcut.alignment = TextAlignmentOptions.Right;
            }

            if (item.Checked)
            {
                var icon = UiKit.CreateRect("Check", rowRect);
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 1f);
                icon.sizeDelta = new Vector2(8f, 8f);
                icon.anchoredPosition = new Vector2(IconX, -3f);
                var iconImage = icon.gameObject.AddComponent<Image>();
                iconImage.sprite = PixelSkin.Ase("check_selected");
                iconImage.raycastTarget = false;
            }

            bool hasSub = item.Children != null && item.Children.Length > 0;
            if (hasSub)
            {
                var arrow = UiKit.CreateRect("Arrow", rowRect);
                arrow.anchorMin = arrow.anchorMax = arrow.pivot = new Vector2(1f, 1f);
                arrow.sizeDelta = new Vector2(9f, 8f);   // combobox_arrow_right 原生 9×8
                arrow.anchoredPosition = new Vector2(-ArrowRight, -3f);
                var arrowImage = arrow.gameObject.AddComponent<Image>();
                arrowImage.sprite = PixelSkin.Ase("combobox_arrow_right");
                arrowImage.raycastTarget = false;
            }

            var handler = rowGo.AddComponent<MenuTile>();
            Item captured = item;
            RectTransform capturedRow = rowRect;
            handler.Bind(face, label,
                on => ApplyHighlight(face, label, on),
                () =>
                {
                    if (captured.Children != null && captured.Children.Length > 0)
                        OpenSubmenu(popup.parent, capturedRow, captured.Children);
                    else
                    {
                        ClosePopup();
                        captured.Action?.Invoke();
                    }
                });
            return y + RowH;
        }

        static void OpenSubmenu(Transform overlay, RectTransform anchorRow, Item[] items)
        {
            // 子菜单 = 同一套弹层，锚到行右侧（OpenPopup 自带关旧开新）
            OpenPopup(overlay, anchorRow, items);
        }

        static void PlaceAt(RectTransform parent, RectTransform rect, float x, float y, float w, float h,
            bool fromRight = false)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(fromRight ? 1f : 0f, 1f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = fromRight ? new Vector2(-x, -y) : new Vector2(x, -y);
        }

        static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // 交互件
        // ------------------------------------------------------------------

        /// <summary>菜单项交互（悬停高亮 + 点击触发）。平铺 Image+文字的行/栏项共用。</summary>
        sealed class MenuTile : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            Image _face;
            TextMeshProUGUI _label;
            System.Action<bool> _highlight;
            System.Action _click;
            bool _down;

            public void Bind(Image face, TextMeshProUGUI label, System.Action<bool> highlight, System.Action click)
            {
                _face = face;
                _label = label;
                _highlight = highlight;
                _click = click;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (_highlight != null)
                    _highlight(true);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (_highlight != null)
                    _highlight(false);
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (_click != null)
                    _click();
            }
        }

        /// <summary>全屏透明捕获板：点它关闭菜单。</summary>
        sealed class MenuCatcher : MonoBehaviour, IPointerClickHandler
        {
            System.Action _close;

            public void Bind(System.Action close) => _close = close;

            public void OnPointerClick(PointerEventData eventData) => _close?.Invoke();
        }
    }
}
