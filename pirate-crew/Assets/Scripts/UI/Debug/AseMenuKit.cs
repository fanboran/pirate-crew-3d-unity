using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// Aseprite 菜单系统**逐函数移植**（以库为源：<c>external/aseprite-ref/src/ui/menu.cpp</c> 全文 +
    /// <c>fit_bounds.cpp</c> 的菜单分支 + <c>src/app/ui/skin/skin_theme.cpp</c> 的 paintMenuItem）。
    ///
    /// 【移植口径】状态机照 menu.cpp 的 MenuBaseData/MenuItem 语义走，只把「消息队列」换成 UGUI 事件：
    ///   · <c>MenuBox::onProcessMessage</c> → <see cref="MenuScopeInput"/>（挂在栏根/弹窗根这一层命中板上，
    ///     用指针坐标自己拾取行——等价源码的 <c>manager()->pickFromScreenPos()</c>，不是逐行挂 Button）；
    ///   · <c>kMouseMove/onMouseDown/onMouseUp</c> → OnScopePointer{Move,Down,Up}；
    ///   · <c>kTimerMessage</c>(250ms, menu.cpp:35) → <see cref="AseMenuSession.Update"/> 计时；
    ///   · <c>startFilteringMouseDown</c> 的外点关闭 → 栏矩形之外的 4 块捕获板（点即关且不穿透，
    ///     菜单条本身不吃捕获板——源码里点在「同一菜单树」上是切换/开合，不是关闭）。
    ///
    /// 【关键语义（创始人实测「下拉行为不对」的三处根因）】
    ///   1. 定位：一层下拉 = 栏项正下、左缘钳进 workarea（menu.cpp:905-910）；二层子菜单 = 行左上 3px、
    ///      右缘贴父窗右边 -1px，重叠更多时翻到左侧（menu.cpp:917-918 + choose_side menu.cpp:115-146）。
    ///      workarea = **画布**（源码是显示器 workarea），不是宿主调试窗——旧实现按宿主窗宽钳取，
    ///      宿主偏右时弹层越窗、贴边不准。
    ///   2. 悬浮切换：菜单条冷态（base.was_clicked=false）悬停**不高亮**，只有 hot 档（menu.cpp:482-491
    ///      的 kMouseMove 门 + skin_theme.cpp:1590-1593）；一旦有菜单展开（was_clicked=true），
    ///      悬停其它栏项**立即**高亮并切换展开（menu.cpp:543-549，栏内 open_submenu=true，无延时）。
    ///   3. 点击行为：按开/按关是按下语义——点已展开的栏项 = 收起（menu.cpp:554-566）；
    ///      行内悬停 250ms 展开子菜单（menu.cpp:996-1012）；叶子项**松开**才执行（menu.cpp:592-609）；
    ///      点层边框/栏空白 = closeAll（menu.cpp:524-535）。
    ///
    /// 【像素纪律】菜单项在库里就是**平涂色块 + 文字色 + 3 段竖线三角箭头 + check 件**（skin_theme.cpp:1566-1674），
    /// 不是像素件贴图，故面色/字色用 theme.xml 色值直填；箭头按源码逐条 drawVLine 画 3 条 1px 竖条；
    /// 勾选/边框/分隔线用 <see cref="PixelSkin.Ase"/> 直切件（check_selected / menu / separator_horz），不乘色。
    ///
    /// 【溢出滚动】弹层超高时按 <c>add_scrollbars(..., AddScrollBarsOption::IfNeeded)</c>
    /// （menu.cpp:343 独立弹出 / menu.cpp:921 子菜单 → scroll_window.cpp:23-91）：窗口矩形钳进
    /// workarea（= 画布）、竖轴钳取时给窗口加宽 <c>2×滚动条宽</c>，行列表包进 <see cref="AseView"/>
    /// （<c>view_base</c> 无边框样式）；滚轮 = menu.cpp:797-800
    /// <c>kMouseWheelMessage → View::scrollByMessage</c>，滚出视口的行按源码裁剪语义拾取不到。
    /// </summary>
    public static class AseMenuKit
    {
        // ------------------------------------------------------------------
        // 常量（每条注明源码出处；数值以源码为准，不目测）
        // ------------------------------------------------------------------

        /// <summary>menuitem 边框：skin_theme.cpp:1203 <c>BORDER(2 * scale)</c>。</summary>
        internal const int ItemBorder = 2;

        /// <summary>menuitem 子间距：skin_theme.cpp:1204 <c>setChildSpacing(18 * scale)</c>。</summary>
        internal const int ItemChildSpacing = 18;

        /// <summary>弹窗边框切片：theme.xml:166 <c>part id="menu" w1=3 w2=10 w3=3 h1=3 h2=9 h3=4</c>。</summary>
        internal const int MenuSliceL = 3, MenuSliceR = 3, MenuSliceT = 3, MenuSliceB = 4;

        /// <summary>分隔线 border：theme.xml:718 <c>horizontal_separator border="2"</c>（menu_separator 继承）。</summary>
        internal const int SeparatorBorder = 2;

        /// <summary>子菜单悬停展开延时：menu.cpp:35 <c>kTimeoutToOpenSubmenu = 250</c>。</summary>
        internal const float SubmenuDelay = 0.25f;

        /// <summary>勾选件尺寸：theme.xml:150-152 check_* 8×8。</summary>
        internal const float CheckSize = 8f;

        /// <summary>勾选件横向落点增量：skin_theme.cpp:1607 <c>bounds.x + 4 * scale - icon->width() / 2</c>。</summary>
        internal const float CheckCenterOffset = 4f;

        /// <summary>子菜单箭头宽：skin_theme.cpp:1627 <c>for (c = 0; c &lt; 3 * scale; c++)</c>。</summary>
        internal const float ArrowWidth = 3f;

        /// <summary>二层子菜单上移：menu.cpp:917 <c>bounds.y = itemBounds.y - 3 * scale</c>。</summary>
        internal const float SubmenuAnchorUp = 3f;

        /// <summary>choose_side 的贴边重叠：menu.cpp:121-122 左右候选各让 1*scale 与父窗重叠。</summary>
        internal const float SideOverlap = 1f;

        /// <summary>分隔线行高：separator.cpp:35-62 sizeHint = max(border,styleBorder) 高 = 2+2。</summary>
        internal const float SeparatorHeight = SeparatorBorder * 2f;

        // theme.xml colors 段（menu.cpp / paintMenuItem 直接用作 fillRect 面色与 drawText 字色）
        internal static readonly Color32 FaceNormal = new Color32(0x2C, 0x2C, 0x30, 0xFF);   // menuitem_normal_face
        internal static readonly Color32 FaceHot = new Color32(0x2C, 0x2C, 0x30, 0xFF);      // menuitem_hot_face
        internal static readonly Color32 FaceHighlight = new Color32(0xC0, 0xC0, 0xC0, 0xFF); // menuitem_highlight_face
        internal static readonly Color32 TextNormal = new Color32(0xC0, 0xC0, 0xC0, 0xFF);   // menuitem_normal_text
        internal static readonly Color32 TextHot = new Color32(0x7D, 0x7D, 0x7D, 0xFF);      // menuitem_hot_text
        internal static readonly Color32 TextHighlight = new Color32(0x2C, 0x2C, 0x30, 0xFF); // menuitem_highlight_text
        internal static readonly Color32 TextDisabled = new Color32(0x20, 0x21, 0x25, 0xFF); // disabled
        internal static readonly Color32 TextDisabledShadow = new Color32(0x41, 0x44, 0x4A, 0xFF); // background（disabled 字 +1,+1 影）

        // ------------------------------------------------------------------
        // 菜单声明（调用方数据结构）
        // ------------------------------------------------------------------

        /// <summary>一条菜单项声明（分隔线 = <see cref="Separator"/>；子菜单 = <see cref="Children"/>）。</summary>
        public sealed class Item
        {
            public string Label;
            public string Shortcut;
            public bool Separator;
            public bool Checked;
            public bool Enabled = true;
            public Item[] Children;
            public System.Action Action;
        }

        public static Item Item_(string label, string shortcut = null, System.Action action = null,
            bool check = false, Item[] children = null, bool enabled = true)
        {
            return new Item
            {
                Label = label,
                Shortcut = shortcut,
                Action = action,
                Checked = check,
                Children = children,
                Enabled = enabled,
            };
        }

        public static Item Sep() => new Item { Separator = true };

        /// <summary>同一屏只有一条菜单栏；<see cref="CloseAll"/> 供宿主收窗时收菜单。</summary>
        static AseMenuSession s_session;

        /// <summary>同一屏只有一份独立弹出菜单（<see cref="OpenPopup"/>：开新关旧）。</summary>
        static AseMenuSession s_popupSession;

        /// <summary>
        /// 会话销毁时反向解挂静态槽：场景卸载/重建栏会把旧会话连同 GameObject 一起销毁，
        /// 静态引用若不清会隔着 Unity fake-null 悬挂到下一次 Build 才被发现。
        /// </summary>
        internal static void Detach(AseMenuSession session)
        {
            if (s_session == session)
                s_session = null;
            if (s_popupSession == session)
                s_popupSession = null;
        }

        /// <summary>
        /// 建菜单栏（横向标题行）。返回栏根（顶左锚，高 = textHeight + 2*2）——调用方按 return 自行定位。
        /// 弹层不在栏子树里：它挂在画布层（源码里 popup 是显示器上的独立 window），由本件自找画布。
        /// </summary>
        public static RectTransform BuildMenuBar(Transform parent, string name,
            (string title, Item[] items)[] menus)
        {
            // 重复 Build 先显式收旧会话：静态槽直接覆盖会让旧弹层/捕获板悬着没人关
            if (s_session != null)
                s_session.CloseMenus();

            RectTransform barRect = MakeRect(name, parent);

            var session = barRect.gameObject.AddComponent<AseMenuSession>();
            session.Build(barRect, menus);
            s_session = session;
            return barRect;
        }

        /// <summary>
        /// 独立弹出菜单（<c>ui::Menu::showPopup</c> menu.cpp:297-361 的 UGUI 版）：锚在
        /// <paramref name="anchor"/> 之下，点外部即关闭且**不穿透**（源码 startFilteringMouseDown
        /// 拦 mousedown 后 return true）。base->was_clicked 置 true（menu.cpp:329）→ 悬停即高亮。
        /// <paramref name="side"/>=true 时弹到锚件右侧。开新关旧（同屏一份）。
        /// </summary>
        public static void OpenPopup(Transform overlay, RectTransform anchor, Item[] items, bool side = false)
        {
            if (anchor == null || items == null || items.Length == 0)
                return;

            CloseAll();

            RectTransform host = AseUi.OverlayOf(anchor);
            if (host == null)
                host = overlay as RectTransform;
            if (host == null)
                return;

            RectTransform rect = UiKit.CreateRect("AseMenuPopupSession", host);
            UiKit.SetAnchored(rect, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            var session = rect.gameObject.AddComponent<AseMenuSession>();
            session.BuildStandalone(host, rect.gameObject, anchor, items, side);
            s_popupSession = session;
        }

        /// <summary>收起整棵菜单（Menu::closeAll）：清高亮、销弹层、退捕获板。</summary>
        public static void CloseAll()
        {
            if (s_session != null)
                s_session.CloseMenus();
            if (s_popupSession != null)
                s_popupSession.CloseMenus();
        }

        // ------------------------------------------------------------------
        // 尺寸（AppMenuItem::onSizeHint app_menuitem.cpp:133-150；MenuItem::onSizeHint menu.cpp:1044-1055）
        // ------------------------------------------------------------------

        /// <summary>字高（= 源码 textHeight()：字体 ascent→descent 跨度）。像素字体按原生档取 lineHeight。</summary>
        internal static float TextHeight(int fontSize)
        {
            TMP_FontAsset font = UiKit.ResolvePixelFont(fontSize);
            if (font != null)
            {
                var info = font.faceInfo;
                if (info.pointSize > 0f)
                {
                    float h = info.lineHeight * fontSize / info.pointSize;
                    if (h > 0f)
                        return Mathf.Ceil(h);
                }
            }
            return fontSize + 2f;
        }

        /// <summary>
        /// 整行尺寸提示：栏内 = 文字 + childSpacing/4 + border；行内 = 文字 + childSpacing + border，
        /// 有快捷键再加快捷键宽（app_menuitem.cpp:138-148）。
        /// </summary>
        internal static float HintWidth(float textW, float shortcutW, bool inBar)
        {
            return textW
                + (inBar ? ItemChildSpacing / 4 : ItemChildSpacing)
                + ItemBorder * 2
                + shortcutW;
        }

        /// <summary>行有子菜单（ui::MenuItem::hasSubmenu 同义：submenu 非空且孩子非空）。</summary>
        internal static bool HasChildren(Item item)
        {
            return item != null && item.Children != null && item.Children.Length > 0;
        }

        /// <summary>建一个顶左锚（0,1）点锚件——栏/行/弹窗/视图/捕获板的统一出生形态。</summary>
        internal static RectTransform MakeRect(string name, Transform parent)
        {
            RectTransform rect = UiKit.CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            return rect;
        }

        // ------------------------------------------------------------------
        // 运行时对象（ui::Menu / MenuBoxWindow / MenuItem 的对应物）
        // ------------------------------------------------------------------

        /// <summary>一个菜单层（MenuBox + Menu）：栏层或弹层。</summary>
        internal sealed class MenuScope
        {
            public RectTransform Rect;            // 栏根或弹窗根（命中板所在）
            public bool InBar;
            public MenuRow Owner;                 // m_menuitem：本层由哪个行展开（栏层为 null）
            public readonly List<MenuRow> Rows = new List<MenuRow>();
            public MenuRow Highlighted;           // menu->getHighlightedItem()
            public float Width, Height;           // 弹层窗尺寸（栏层 = 栏根尺寸）
            public AseView View;                  // add_scrollbars 加的 View（未溢出 = null）
            public RectTransform Content;         // View 视口里的行容器（未溢出 = null）

            /// <summary>没有 View 时行为弹窗根直系子件（坐标含弹窗边框）；有 View 时行为
            /// <see cref="Content"/> 的子件、纵坐标须减 <see cref="MenuSliceT"/>（见
            /// <see cref="RowTopInContent"/>）。</summary>
            public float RowTopInContent(MenuRow row)
            {
                return View != null ? row.Y - MenuSliceT : row.Y;
            }
        }

        /// <summary>一个菜单项（ui::MenuItem）。</summary>
        internal sealed class MenuRow
        {
            public Item Data;
            public MenuScope Scope;
            public RectTransform Rect;
            public Image Face;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI ShortcutLabel;
            public Image Check;
            public readonly List<Image> Arrow = new List<Image>(3);
            public MenuScope Submenu;             // m_submenu_menubox（null = 未展开）
            public bool Highlighted;              // m_highlighted
            public bool Hovered;                  // widget->hasMouse()
            public float Timer = -1f;             // m_submenu_timer：<0 = 已停
            public float X, Y, W, H;              // 命中范围（栏 = 横排，弹层 = 纵排）

            public bool HasSubmenu => Data.Children != null && Data.Children.Length > 0;
            public bool Enabled => Data.Enabled;
            public bool InBar => Scope != null && Scope.InBar;
        }

    }
}
