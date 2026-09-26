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
    /// </summary>
    public static class AseMenuKit
    {
        // ------------------------------------------------------------------
        // 常量（每条注明源码出处；数值以源码为准，不目测）
        // ------------------------------------------------------------------

        /// <summary>menuitem 边框：skin_theme.cpp:1203 <c>BORDER(2 * scale)</c>。</summary>
        const int ItemBorder = 2;

        /// <summary>menuitem 子间距：skin_theme.cpp:1204 <c>setChildSpacing(18 * scale)</c>。</summary>
        const int ItemChildSpacing = 18;

        /// <summary>弹窗边框切片：theme.xml:166 <c>part id="menu" w1=3 w2=10 w3=3 h1=3 h2=9 h3=4</c>。</summary>
        const int MenuSliceL = 3, MenuSliceR = 3, MenuSliceT = 3, MenuSliceB = 4;

        /// <summary>分隔线 border：theme.xml:718 <c>horizontal_separator border="2"</c>（menu_separator 继承）。</summary>
        const int SeparatorBorder = 2;

        /// <summary>子菜单悬停展开延时：menu.cpp:35 <c>kTimeoutToOpenSubmenu = 250</c>。</summary>
        const float SubmenuDelay = 0.25f;

        /// <summary>勾选件尺寸：theme.xml:150-152 check_* 8×8。</summary>
        const float CheckSize = 8f;

        /// <summary>勾选件横向落点增量：skin_theme.cpp:1607 <c>bounds.x + 4 * scale - icon->width() / 2</c>。</summary>
        const float CheckCenterOffset = 4f;

        /// <summary>子菜单箭头宽：skin_theme.cpp:1627 <c>for (c = 0; c &lt; 3 * scale; c++)</c>。</summary>
        const float ArrowWidth = 3f;

        /// <summary>二层子菜单上移：menu.cpp:917 <c>bounds.y = itemBounds.y - 3 * scale</c>。</summary>
        const float SubmenuAnchorUp = 3f;

        /// <summary>choose_side 的贴边重叠：menu.cpp:121-122 左右候选各让 1*scale 与父窗重叠。</summary>
        const float SideOverlap = 1f;

        /// <summary>分隔线行高：separator.cpp:35-62 sizeHint = max(border,styleBorder) 高 = 2+2。</summary>
        const float SeparatorHeight = SeparatorBorder * 2f;

        // theme.xml colors 段（menu.cpp / paintMenuItem 直接用作 fillRect 面色与 drawText 字色）
        static readonly Color32 FaceNormal = new Color32(0x2C, 0x2C, 0x30, 0xFF);   // menuitem_normal_face
        static readonly Color32 FaceHot = new Color32(0x2C, 0x2C, 0x30, 0xFF);      // menuitem_hot_face
        static readonly Color32 FaceHighlight = new Color32(0xC0, 0xC0, 0xC0, 0xFF); // menuitem_highlight_face
        static readonly Color32 TextNormal = new Color32(0xC0, 0xC0, 0xC0, 0xFF);   // menuitem_normal_text
        static readonly Color32 TextHot = new Color32(0x7D, 0x7D, 0x7D, 0xFF);      // menuitem_hot_text
        static readonly Color32 TextHighlight = new Color32(0x2C, 0x2C, 0x30, 0xFF); // menuitem_highlight_text
        static readonly Color32 TextDisabled = new Color32(0x20, 0x21, 0x25, 0xFF); // disabled
        static readonly Color32 TextDisabledShadow = new Color32(0x41, 0x44, 0x4A, 0xFF); // background（disabled 字 +1,+1 影）

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
        /// 建菜单栏（横向标题行）。返回栏根（顶左锚，高 = textHeight + 2*2）——调用方按 return 自行定位。
        /// 弹层不在栏子树里：它挂在画布层（源码里 popup 是显示器上的独立 window），由本件自找画布。
        /// </summary>
        public static RectTransform BuildMenuBar(Transform parent, string name,
            (string title, Item[] items)[] menus)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform barRect = (RectTransform)go.transform;
            barRect.SetParent(parent, false);
            barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0f, 1f);

            var session = go.AddComponent<AseMenuSession>();
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

            RectTransform host = FindOverlay(anchor);
            if (host == null)
                host = overlay as RectTransform;
            if (host == null)
                return;

            var go = new GameObject("AseMenuPopupSession", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(host, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = Vector2.zero;

            var session = go.AddComponent<AseMenuSession>();
            session.BuildStandalone(host, go, anchor, items, side);
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

        /// <summary>件所在画布（popup 挂画布层：源码 popup 是显示器上的独立 window）。</summary>
        internal static RectTransform FindOverlay(Transform t)
        {
            if (t == null)
                return null;
            Canvas canvas = t.GetComponentInParent<Canvas>();
            if (canvas != null)
                return (RectTransform)canvas.transform;
            RectTransform root = t as RectTransform;
            while (root != null && root.parent is RectTransform parent)
                root = parent;
            return root;
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

        // ------------------------------------------------------------------
        // 运行时对象（ui::Menu / MenuBoxWindow / MenuItem 的对应物）
        // ------------------------------------------------------------------

        /// <summary>一个菜单层（MenuBox + Menu）：栏层或弹层。</summary>
        sealed class MenuScope
        {
            public RectTransform Rect;            // 栏根或弹窗根（命中板所在）
            public bool InBar;
            public MenuRow Owner;                 // m_menuitem：本层由哪个行展开（栏层为 null）
            public readonly List<MenuRow> Rows = new List<MenuRow>();
            public MenuRow Highlighted;           // menu->getHighlightedItem()
            public float Width, Height;           // 弹层窗尺寸（栏层 = 栏根尺寸）
        }

        /// <summary>一个菜单项（ui::MenuItem）。</summary>
        sealed class MenuRow
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

        /// <summary>
        /// MenuBox::onProcessMessage 的事件面：挂在**本层命中板**上（栏根 / 弹窗框），
        /// 行由指针坐标拾取——等价源码 <c>manager()->pickFromScreenPos()</c>，行不各自吃事件。
        /// </summary>
        sealed class MenuScopeInput : MonoBehaviour,
            IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler,
            IPointerDownHandler, IPointerUpHandler
        {
            public AseMenuSession Session;
            public MenuScope Scope;

            public void OnPointerEnter(PointerEventData e) => Session.ScopePointerMove(Scope, e);
            public void OnPointerMove(PointerEventData e) => Session.ScopePointerMove(Scope, e);
            public void OnPointerExit(PointerEventData e) => Session.ScopePointerExit(Scope);
            public void OnPointerDown(PointerEventData e) => Session.ScopePointerDown(Scope, e);
            public void OnPointerUp(PointerEventData e) => Session.ScopePointerUp(Scope);
        }

        /// <summary>外点捕获板（源码 startFilteringMouseDown 拦到的 kMouseDownMessage：关全部且不穿透）。</summary>
        sealed class MenuCatcher : MonoBehaviour, IPointerDownHandler
        {
            public AseMenuSession Session;

            public void OnPointerDown(PointerEventData e) => Session.CloseMenus();
        }

        /// <summary>
        /// 菜单会话：一份 MenuBaseData（menu.cpp:84-105）+ 菜单条/弹层状态机。
        /// 挂在栏根上；因弹层挂画布层，Update 只跑计时器。
        /// </summary>
        sealed class AseMenuSession : MonoBehaviour
        {
            // MenuBaseData
            bool _wasClicked;        // was_clicked：展开后为 true——「悬浮切换/悬停高亮」的总闸
            bool _isProcessing;      // is_processing：open/close 消息链重入闸
            bool _closing;

            RectTransform _overlay;   // workarea = 画布（源码的显示器）
            float _overlayW, _overlayH;
            MenuScope _bar;
            GameObject _hostGo;       // 独立弹出菜单时本会话所在的宿主件（关闭即销）
            bool _standalone;
            readonly List<MenuScope> _popups = new List<MenuScope>();
            readonly List<GameObject> _catchers = new List<GameObject>();
            MenuScope _hoverScope;
            MenuRow _hoverRow;

            // ------------------------------------------------------------------
            // 建栏（Menu / MenuBar 装配）
            // ------------------------------------------------------------------

            public void Build(RectTransform barRect, (string title, Item[] items)[] menus)
            {
                _overlay = FindOverlay(barRect);
                _overlayW = _overlay.rect.width;
                _overlayH = _overlay.rect.height;

                _bar = new MenuScope { Rect = barRect, InBar = true };

                var hit = barRect.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;
                var input = barRect.gameObject.AddComponent<MenuScopeInput>();
                input.Session = this;
                input.Scope = _bar;

                float textH = TextHeight(UiSkin.Font.Tiny);
                float barH = textH + ItemBorder * 2f;
                float x = 0f;
                foreach ((string title, Item[] items) in menus)
                {
                    var row = new MenuRow
                    {
                        Data = new Item { Label = title, Children = items },
                        Scope = _bar,
                        Rect = MakeRect("Menu_" + title, barRect),
                    };
                    row.Face = row.Rect.gameObject.AddComponent<Image>();
                    row.Face.color = FaceNormal;
                    row.Face.raycastTarget = false;
                    row.Label = DebugWindowKit.Label(row.Rect, title, UiSkin.Font.Tiny,
                        TextNormal, TextAlignmentOptions.Center);
                    UiKit.Stretch(row.Label.rectTransform);

                    // MenuItem::onSizeHint 栏内档：文字 + childSpacing/4 + border（menu.cpp:1049）
                    float w = Mathf.Ceil(row.Label.preferredWidth) + ItemChildSpacing / 4 + ItemBorder * 2;
                    row.Rect.sizeDelta = new Vector2(w, barH);
                    row.Rect.anchoredPosition = new Vector2(x, 0f);
                    row.X = x;
                    row.Y = 0f;
                    row.W = w;
                    row.H = barH;
                    x += w;                                          // Menu::onResize 栏内 childSpacing=0
                    _bar.Rows.Add(row);
                }
                _bar.Rect.sizeDelta = new Vector2(x, barH);
                _bar.Width = x;
                _bar.Height = barH;
            }

            /// <summary>
            /// 独立弹出菜单（Menu::showPopup 装配）：无栏，base->was_clicked 直接置 true
            /// （menu.cpp:329）——悬停即高亮、点开即展开。
            /// </summary>
            public void BuildStandalone(RectTransform overlay, GameObject host,
                RectTransform anchor, Item[] items, bool side)
            {
                _standalone = true;
                _hostGo = host;
                _overlay = overlay;
                _overlayW = overlay.rect.width;
                _overlayH = overlay.rect.height;

                MenuScope popup = BuildPopup(null, items, anchor, side);
                _wasClicked = true;                                // menu.cpp:329
                if (popup == null)
                    CloseMenus();
            }

            // ------------------------------------------------------------------
            // 计时器（kTimerMessage → kTimeoutToOpenSubmenu 250ms，menu.cpp:996-1012）
            // ------------------------------------------------------------------

            void Update()
            {
                if (_popups.Count == 0)
                    return;

                for (int i = 0; i < _popups.Count; i++)
                {
                    MenuScope scope = _popups[i];
                    for (int r = 0; r < scope.Rows.Count; r++)
                    {
                        MenuRow row = scope.Rows[r];
                        if (row.Timer < 0f)
                            continue;

                        row.Timer += Time.unscaledDeltaTime;
                        if (row.Timer < SubmenuDelay)
                            continue;

                        // Stop timer, then open if still closed and not processing（menu.cpp:1005-1010）
                        row.Timer = -1f;
                        if (row.Submenu == null && !_isProcessing)
                            OpenSubmenu(row, false);
                    }
                }
            }

            void OnDisable()
            {
                CloseMenus();
            }

            // ------------------------------------------------------------------
            // MenuBox::onProcessMessage 语义
            // ------------------------------------------------------------------

            /// <summary>kMouseMoveMessage / kMouseEnterMessage（menu.cpp:482-491、543-550）。</summary>
            public void ScopePointerMove(MenuScope scope, PointerEventData e)
            {
                if (scope == null)
                    return;

                MenuRow row = RowAt(scope, e);
                SetHover(scope, row);

                if (!_wasClicked || _isProcessing)
                    return;              // 冷态菜单条：移动不处理（源码 kMouseMove 门）
                if (row == null || !row.Enabled || row.Data.Separator)
                    return;              // 拾取到非菜单项：was_clicked=true 时不动（menu.cpp:569）
                if (row.Highlighted)
                    return;

                // 菜单条：悬停即展开切换；弹层：只高亮（展开交给计时器/按下）
                HighlightItem(scope, row, false, scope.InBar, false);
            }

            /// <summary>kMouseLeaveMessage（menu.cpp:576-590）。</summary>
            public void ScopePointerExit(MenuScope scope)
            {
                if (_hoverScope == scope)
                {
                    if (_hoverRow != null)
                    {
                        _hoverRow.Hovered = false;
                        OnRowLeave(_hoverRow);
                        Repaint(_hoverRow);
                    }
                    _hoverScope = null;
                    _hoverRow = null;
                }

                if (scope != null && scope.Highlighted != null && scope.Highlighted.Submenu == null)
                    Unhighlight(scope);
            }

            /// <summary>kMouseDownMessage / kDoubleClickMessage（menu.cpp:494-574）。</summary>
            public void ScopePointerDown(MenuScope scope, PointerEventData e)
            {
                if (scope == null || _isProcessing)
                    return;

                MenuRow row = RowAt(scope, e);

                // 点在菜单树之外/窗框/栏空白：closeAll（menu.cpp:524-535）
                if (row == null)
                {
                    CloseMenus();
                    return;
                }
                // 拾取到分隔线等非菜单项：不动（menu.cpp:539 else 分支）
                if (row.Data.Separator)
                    return;
                if (!row.Enabled)
                {
                    if (!_wasClicked)
                        Unhighlight(scope);
                    return;
                }

                if (!row.Highlighted)
                {
                    // 栏内恒展开、弹层「按下才展开」（menu.cpp:546-547 open_submenu = bar || mousedown）
                    HighlightItem(scope, row, false, true, false);
                }
                else if (row.HasSubmenu)
                {
                    // 已高亮且带子菜单：先停计时器；未展开则展开，已展开且是栏项 → 收起整套（menu.cpp:554-566）
                    StopTimer(row);
                    if (row.Submenu == null)
                        OpenSubmenu(row, false);
                    else if (row.InBar)
                        CloseMenus();
                }
            }

            /// <summary>kMouseUpMessage（menu.cpp:592-609）：叶子项在松开且无待展开计时器时执行。</summary>
            public void ScopePointerUp(MenuScope scope)
            {
                if (scope == null || _isProcessing)
                    return;

                MenuRow h = scope.Highlighted;
                if (h != null && h.Submenu == null && h.Timer < 0f)
                {
                    CloseMenus();
                    h.Data.Action?.Invoke();
                }
            }

            // ------------------------------------------------------------------
            // Menu::highlightItem / unhighlightItem（menu.cpp:1108-1194）
            // ------------------------------------------------------------------

            void HighlightItem(MenuScope scope, MenuRow row, bool click, bool openSubmenu, bool selectFirst)
            {
                for (int i = 0; i < scope.Rows.Count; i++)
                {
                    MenuRow other = scope.Rows[i];
                    if (other != row && other.Highlighted)
                    {
                        other.Highlighted = false;
                        Repaint(other);
                    }
                }

                if (row == null)
                {
                    scope.Highlighted = null;
                    return;
                }

                if (!row.Highlighted)
                {
                    row.Highlighted = true;
                    scope.Highlighted = row;
                    Repaint(row);
                }

                // 父链高亮（menu.cpp:1163-1167）
                if (scope.Owner != null)
                    HighlightItem(scope.Owner.Scope, scope.Owner, false, false, false);

                if (row.HasSubmenu)
                {
                    if (openSubmenu)
                    {
                        if (row.Submenu == null)
                            OpenSubmenu(row, selectFirst);
                        _wasClicked = true;               // menu.cpp:1177-1180
                    }
                }
                else if (click)
                {
                    CloseMenus();                          // menu.cpp:1184-1187
                    row.Data.Action?.Invoke();
                }
            }

            void Unhighlight(MenuScope scope)
            {
                HighlightItem(scope, null, false, false, false);
            }

            // ------------------------------------------------------------------
            // MenuItem::openSubmenu / closeSubmenu（menu.cpp:1201-1290）
            // ------------------------------------------------------------------

            void OpenSubmenu(MenuRow row, bool selectFirst)
            {
                if (row == null || !row.HasSubmenu || _isProcessing)
                    return;

                // 先关掉同层其它已展开的兄弟（menu.cpp:1216-1226）
                for (int i = 0; i < row.Scope.Rows.Count; i++)
                {
                    MenuRow sibling = row.Scope.Rows[i];
                    if (sibling != row && sibling.Submenu != null)
                        CloseSubmenu(sibling, false);
                }

                _isProcessing = true;                          // base->is_processing（menu.cpp:1241）
                MenuScope popup = BuildPopup(row, row.Data.Children, row.Rect, false);
                if (popup == null)
                {
                    _isProcessing = false;
                    return;
                }
                if (selectFirst)
                {
                    MenuRow first = null;
                    for (int i = 0; i < popup.Rows.Count; i++)
                    {
                        MenuRow child = popup.Rows[i];
                        if (!child.Data.Separator && child.Enabled)
                        {
                            first = child;
                            break;
                        }
                    }
                    if (first != null)
                        HighlightItem(popup, first, false, false, false);
                    else
                        Unhighlight(popup);
                }
                else
                {
                    Unhighlight(popup);                        // menu.cpp:945
                }
                _isProcessing = false;                         // menu.cpp:950
            }

            void CloseSubmenu(MenuRow row, bool lastOfCloseChain)
            {
                if (row == null || row.Submenu == null)
                    return;

                MenuScope popup = row.Submenu;
                for (int i = 0; i < popup.Rows.Count; i++)
                {
                    MenuRow child = popup.Rows[i];
                    if (child.Submenu != null)
                        CloseSubmenu(child, false);            // 递归关孙层（menu.cpp:1264-1270）
                }

                DestroyPopup(popup);                           // window->closeWindow()（menu.cpp:969）
                row.Highlighted = false;
                Repaint(row);
                if (lastOfCloseChain)
                    _isProcessing = false;
            }

            /// <summary>Menu::closeAll（menu.cpp:1307-1350）。</summary>
            public void CloseMenus()
            {
                if (_closing)
                    return;

                _closing = true;
                _wasClicked = false;                           // base->was_clicked = false
                _isProcessing = false;
                var all = new List<MenuScope>(_popups);
                for (int i = all.Count - 1; i >= 0; i--)
                    DestroyPopup(all[i]);
                _popups.Clear();

                if (_bar != null)
                {
                    for (int i = 0; i < _bar.Rows.Count; i++)
                    {
                        MenuRow row = _bar.Rows[i];
                        row.Highlighted = false;
                        row.Timer = -1f;
                        Repaint(row);
                    }
                    _bar.Highlighted = null;
                }

                _hoverScope = null;
                _hoverRow = null;
                DestroyCatchers();
                _closing = false;

                // 独立弹出菜单：会话随弹层一起收
                if (_standalone && _hostGo != null)
                {
                    GameObject host = _hostGo;
                    _hostGo = null;
                    DestroySafe(host);
                }
            }

            // ------------------------------------------------------------------
            // 弹层构建（MenuBoxWindow + fit_bounds）
            // ------------------------------------------------------------------

            /// <summary>
            /// 建一层弹窗（MenuBoxWindow + fit_bounds）。<paramref name="owner"/>=展开本层的行
            /// （栏项/父层行）；为 null 即独立弹出菜单（<see cref="BuildStandalone"/>），
            /// 此时按 <paramref name="anchor"/> 落位。
            /// </summary>
            MenuScope BuildPopup(MenuRow owner, Item[] items, RectTransform anchor, bool side)
            {
                if (items == null || items.Length == 0)
                    return null;

                EnsureCatchers();                              // 外点捕获板（先于弹层建，弹层压其上）

                var scope = new MenuScope { InBar = false, Owner = owner };
                scope.Rect = MakeRect("AseMenuPopup", _overlay);
                var frame = scope.Rect.gameObject.AddComponent<Image>();
                frame.sprite = PixelSkin.Ase("menu");      // theme.xml:166 直切件
                frame.type = Image.Type.Sliced;
                frame.pixelsPerUnitMultiplier = 1f;
                frame.color = Color.white;
                frame.raycastTarget = true;                    // 本层命中板

                var input = scope.Rect.gameObject.AddComponent<MenuScopeInput>();
                input.Session = this;
                input.Scope = scope;

                var faceRect = UiKit.CreateRect("Face", scope.Rect);
                faceRect.anchorMin = faceRect.anchorMax = new Vector2(0.5f, 0.5f);
                faceRect.pivot = new Vector2(0.5f, 0.5f);
                faceRect.offsetMin = new Vector2(MenuSliceL, MenuSliceB);
                faceRect.offsetMax = new Vector2(-MenuSliceR, -MenuSliceT);
                var face = faceRect.gameObject.AddComponent<Image>();
                face.color = FaceNormal;
                face.raycastTarget = false;

                float textH = TextHeight(UiSkin.Font.Tiny);
                float menuW = 0f;
                float y = MenuSliceT;
                foreach (Item data in items)
                {
                    MenuRow row = CreateRow(scope, data, textH, y);
                    scope.Rows.Add(row);
                    menuW = Mathf.Max(menuW, RowHintWidth(row, data));
                    y += row.H;                                // Menu::onSizeHint 纵缝 childSpacing=0
                }

                float popupW = menuW + MenuSliceL + MenuSliceR;
                float popupH = y + MenuSliceB;
                scope.Width = popupW;
                scope.Height = popupH;
                scope.Rect.sizeDelta = new Vector2(popupW, popupH);

                for (int i = 0; i < scope.Rows.Count; i++)
                    LayoutRow(scope.Rows[i], menuW);

                Vector2 pos = FitBounds(owner, anchor, side, popupW, popupH);
                scope.Rect.anchoredPosition = new Vector2(pos.x, -pos.y);

                if (owner != null)
                    owner.Submenu = scope;
                _popups.Add(scope);
                scope.Rect.SetAsLastSibling();
                return scope;
            }

            /// <summary>建一行（MenuItem 装配；宽度/子件落点待 menuW 定后由 <see cref="LayoutRow"/> 收回）。</summary>
            MenuRow CreateRow(MenuScope scope, Item data, float textH, float yTop)
            {
                var row = new MenuRow
                {
                    Data = data,
                    Scope = scope,
                    Rect = MakeRect(data.Separator ? "Sep" : "Row_" + data.Label, scope.Rect),
                };
                row.Y = yTop;
                row.H = data.Separator
                    ? SeparatorHeight
                    : textH + ItemBorder * 2f;

                if (!data.Separator)
                {
                    row.Face = row.Rect.gameObject.AddComponent<Image>();
                    row.Face.color = FaceNormal;
                    row.Face.raycastTarget = false;
                    row.Label = DebugWindowKit.Label(row.Rect, data.Label, UiSkin.Font.Tiny,
                        TextNormal, TextAlignmentOptions.Left);
                    if (!string.IsNullOrEmpty(data.Shortcut))
                    {
                        row.ShortcutLabel = DebugWindowKit.Label(row.Rect, data.Shortcut, UiSkin.Font.Tiny,
                            TextNormal, TextAlignmentOptions.Right);
                    }
                    if (data.Checked)
                    {
                        var checkRect = UiKit.CreateRect("Check", row.Rect);
                        row.Check = checkRect.gameObject.AddComponent<Image>();
                        row.Check.raycastTarget = false;
                    }
                    if (!data.Enabled)
                    {
                        // 禁用字：theme disabled 字色 + background 1px 影（skin_theme.cpp:1895-1904）
                        var shadow = row.Label.gameObject.AddComponent<UnityEngine.UI.Shadow>();
                        shadow.effectColor = TextDisabledShadow;
                        shadow.effectDistance = new Vector2(1f, -1f);
                    }
                }
                return row;
            }

            /// <summary>行内落点：paintMenuItem 逐条换算（skin_theme.cpp:1600-1674）。</summary>
            void LayoutRow(MenuRow row, float menuW)
            {
                row.W = menuW;
                row.Rect.sizeDelta = new Vector2(menuW, row.H);
                // 行落在弹窗 **内容区**（左/上各让边框 3）：源码 Menu 是 menubox 的客户区孩子
                row.Rect.anchoredPosition = new Vector2(MenuSliceL, -row.Y);

                if (row.Data.Separator)
                {
                    // menu_separator：行高 4（border 2+2），线件 separator_horz 9×5 顶对齐画出——
                    // 件第 2 行是点线（第 0/1/3/4 行透明），故线自然落在行内第 2 行且不越色
                    var lineRect = UiKit.CreateRect("Line", row.Rect);
                    lineRect.anchorMin = lineRect.anchorMax = lineRect.pivot = new Vector2(0f, 1f);
                    lineRect.sizeDelta = new Vector2(menuW, 5f);
                    lineRect.anchoredPosition = Vector2.zero;
                    var line = lineRect.gameObject.AddComponent<Image>();
                    line.sprite = PixelSkin.Ase("separator_horz");
                    line.type = Image.Type.Tiled;              // 3px 周期点线，拉伸会变实线
                    line.pixelsPerUnitMultiplier = 1f;
                    line.color = Color.white;
                    line.raycastTarget = false;
                    return;
                }

                float left = ItemBorder + ItemChildSpacing / 2f;   // pos.offset(childSpacing()/2, 0)
                PlaceLeft(row.Label.rectTransform, left, 0f,
                    menuW - left - ItemBorder, row.H);

                if (row.ShortcutLabel != null)
                {
                    // pos.w -= childSpacing()/4 → 右缘 = 行右 - border - 4
                    float rightInset = ItemBorder + ItemChildSpacing / 4f;
                    PlaceRight(row.ShortcutLabel.rectTransform, rightInset, menuW, row.H);
                }

                if (row.Check != null)
                {
                    float iconTop = ItemBorder
                        + Mathf.FloorToInt((row.H - ItemBorder * 2f) * 0.5f)
                        - CheckSize * 0.5f;
                    var rect = row.Check.rectTransform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.sizeDelta = new Vector2(CheckSize, CheckSize);
                    rect.anchoredPosition = new Vector2(
                        ItemBorder + CheckCenterOffset - CheckSize * 0.5f, -iconTop);
                }

                if (row.HasSubmenu)
                {
                    // 子菜单箭头：源码逐条 drawVLine 的 3 段三角（skin_theme.cpp:1627-1640）
                    int cy = ItemBorder + Mathf.FloorToInt((row.H - ItemBorder * 2f) * 0.5f);
                    for (int c = 0; c < (int)ArrowWidth; c++)
                    {
                        float ax = menuW - (ItemBorder + ArrowWidth) - c;
                        float ah = 2 * c + 1;
                        var barRect = UiKit.CreateRect("Arrow" + c, row.Rect);
                        barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0f, 1f);
                        barRect.sizeDelta = new Vector2(1f, ah);
                        barRect.anchoredPosition = new Vector2(ax, -(cy - c));
                        var bar = barRect.gameObject.AddComponent<Image>();
                        bar.color = TextNormal;
                        bar.raycastTarget = false;
                        row.Arrow.Add(bar);
                    }
                }
            }

            /// <summary>行宽提示：文字 + 子间距 + 边 + 快捷键（AppMenuItem::onSizeHint）。</summary>
            float RowHintWidth(MenuRow row, Item data)
            {
                if (data.Separator)
                    return SeparatorBorder * 2f;
                float textW = row.Label != null ? Mathf.Ceil(row.Label.preferredWidth) : 0f;
                float shortcutW = row.ShortcutLabel != null ? Mathf.Ceil(row.ShortcutLabel.preferredWidth) : 0f;
                return HintWidth(textW, shortcutW, false);
            }

            // ------------------------------------------------------------------
            // fit_bounds / choose_side 移植（fit_bounds.cpp:110-157、menu.cpp:115-146、901-922）
            // ------------------------------------------------------------------

            /// <summary>弹层定位（单显示器分支：workarea = 画布）。</summary>
            Vector2 FitBounds(MenuRow owner, RectTransform anchor, bool side, float popupW, float popupH)
            {
                if (owner != null && !owner.InBar)
                {
                    // 二层子菜单（menu.cpp:912-918）：行左上 3px + choose_side（父窗 = 本层窗）
                    Rect itemBounds = BoundsInOverlay(owner.Rect);
                    float sy = Mathf.Clamp(itemBounds.yMin - SubmenuAnchorUp, 0f,
                        Mathf.Max(0f, _overlayH - popupH));
                    Rect parentBounds = BoundsInOverlay(owner.Scope.Rect);
                    return new Vector2(ChooseSideX(parentBounds, popupW, popupH, sy), sy);
                }

                // 一层下拉 / 独立弹出：锚件正下、左缘钳进 workarea（menu.cpp:906-909）
                Rect a = BoundsInOverlay(anchor);
                float x = side
                    ? Mathf.Clamp(a.xMax, 0f, Mathf.Max(0f, _overlayW - popupW))   // 侧向：锚件右侧
                    : Mathf.Clamp(a.xMin, 0f, Mathf.Max(0f, _overlayW - popupW));
                float y = side ? a.yMin : a.yMax;
                y = Mathf.Clamp(y, 0f, Mathf.Max(0f, _overlayH - popupH));
                return new Vector2(x, y);
            }

            /// <summary>choose_side（menu.cpp:115-146）：左右两侧各算与父窗的重叠，取重叠少的一侧。</summary>
            float ChooseSideX(Rect parentBounds, float w, float h, float y)
            {
                float xLeft = parentBounds.xMin - w + SideOverlap;
                float xRight = parentBounds.xMax - SideOverlap;
                float maxX = Mathf.Max(0f, _overlayW - w);
                xLeft = Mathf.Clamp(xLeft, 0f, maxX);
                xRight = Mathf.Clamp(xRight, 0f, maxX);

                float s1 = IntersectArea(xLeft, y, w, h, parentBounds);
                float s2 = IntersectArea(xRight, y, w, h, parentBounds);

                if (s2 <= 0f)
                    return xRight;
                if (s1 <= 0f)
                    return xLeft;
                return s2 <= s1 ? xRight : xLeft;
            }

            static float IntersectArea(float x, float y, float w, float h, Rect other)
            {
                float x0 = Mathf.Max(x, other.xMin);
                float x1 = Mathf.Min(x + w, other.xMax);
                float y0 = Mathf.Max(y, other.yMin);
                float y1 = Mathf.Min(y + h, other.yMax);
                if (x1 <= x0 || y1 <= y0)
                    return 0f;
                return (x1 - x0) * (y1 - y0);
            }

            // ------------------------------------------------------------------
            // 外点捕获板（MenuBox::startFilteringMouseDown/stopFilteringMouseDown 的 UGUI 替身）
            // ------------------------------------------------------------------

            void EnsureCatchers()
            {
                if (_catchers.Count > 0)
                    return;

                if (_bar == null)
                {
                    // 独立弹出菜单：菜单树只有弹窗本身，全屏一块（源码点哪里都在外）
                    AddCatcher(0f, 0f, _overlayW, _overlayH);
                    return;
                }

                Rect b = BoundsInOverlay(_bar.Rect);
                // 栏矩形之外的 4 块：菜单条自己不吃捕获板（源码在「同一菜单树」上按住是切换/开合）
                AddCatcher(0f, 0f, _overlayW, b.yMin);
                AddCatcher(0f, b.yMax, _overlayW, _overlayH - b.yMax);
                AddCatcher(0f, b.yMin, b.xMin, b.height);
                AddCatcher(b.xMax, b.yMin, _overlayW - b.xMax, b.height);
            }

            void AddCatcher(float x, float y, float w, float h)
            {
                if (w <= 0f || h <= 0f)
                    return;
                RectTransform rect = MakeRect("MenuCatcher", _overlay);
                rect.sizeDelta = new Vector2(w, h);
                rect.anchoredPosition = new Vector2(x, -y);
                var image = rect.gameObject.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);
                image.raycastTarget = true;
                var catcher = rect.gameObject.AddComponent<MenuCatcher>();
                catcher.Session = this;
                _catchers.Add(rect.gameObject);
            }

            void DestroyCatchers()
            {
                for (int i = 0; i < _catchers.Count; i++)
                    DestroySafe(_catchers[i]);
                _catchers.Clear();
            }

            // ------------------------------------------------------------------
            // 命中拾取 / 高亮态 / 重绘
            // ------------------------------------------------------------------

            MenuRow RowAt(MenuScope scope, PointerEventData e)
            {
                if (scope == null || scope.Rect == null)
                    return null;
                Camera cam = e.pressEventCamera != null ? e.pressEventCamera : e.enterEventCamera;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        scope.Rect, e.position, cam, out Vector2 local))
                    return null;

                if (scope.InBar)
                {
                    float x = local.x;                         // pivot (0,1)：x 自左缘
                    for (int i = 0; i < scope.Rows.Count; i++)
                    {
                        MenuRow row = scope.Rows[i];
                        if (x >= row.X && x < row.X + row.W)
                            return row;
                    }
                }
                else
                {
                    float yDown = -local.y;                    // pivot (0,1)：y 向下为正
                    for (int i = 0; i < scope.Rows.Count; i++)
                    {
                        MenuRow row = scope.Rows[i];
                        if (yDown >= row.Y && yDown < row.Y + row.H)
                            return row;
                    }
                }
                return null;
            }

            void SetHover(MenuScope scope, MenuRow row)
            {
                if (_hoverScope == scope && _hoverRow == row)
                    return;

                if (_hoverRow != null)
                {
                    _hoverRow.Hovered = false;
                    OnRowLeave(_hoverRow);
                    Repaint(_hoverRow);
                }
                _hoverScope = scope;
                _hoverRow = row;
                if (row != null)
                {
                    row.Hovered = true;
                    OnRowEnter(row);
                    Repaint(row);
                }
            }

            /// <summary>MenuItem kMouseEnter（menu.cpp:841-851）：带子菜单的弹层行起 250ms 计时器。</summary>
            void OnRowEnter(MenuRow row)
            {
                if (!row.Enabled || !row.HasSubmenu)
                    return;
                if (!row.InBar)                                // MenuBar::expandOnMouseover() = false
                    row.Timer = 0f;
            }

            /// <summary>MenuItem kMouseLeave（menu.cpp:853-865）：子菜单未展开则去高亮，停计时器。</summary>
            void OnRowLeave(MenuRow row)
            {
                if (row.Highlighted && row.Submenu == null && row.Scope != null)
                    Unhighlight(row.Scope);
                StopTimer(row);
            }

            void StopTimer(MenuRow row)
            {
                row.Timer = -1f;
            }

            void Repaint(MenuRow row)
            {
                if (row == null)
                    return;

                Color32 fg;
                if (!row.Enabled)
                {
                    fg = TextDisabled;
                    if (row.Face != null) row.Face.color = FaceNormal;   // disabled → menuitem_normal_face
                }
                else if (row.Highlighted)
                {
                    fg = TextHighlight;
                    if (row.Face != null) row.Face.color = FaceHighlight;
                    if (row.Label != null) row.Label.color = fg;
                    if (row.ShortcutLabel != null) row.ShortcutLabel.color = fg;
                    RepaintArrow(row, fg);
                    RepaintCheck(row);
                    return;
                }
                else if (row.Hovered)
                {
                    fg = TextHot;
                    if (row.Face != null) row.Face.color = FaceHot;
                }
                else
                {
                    fg = TextNormal;
                    if (row.Face != null) row.Face.color = FaceNormal;
                }

                if (row.Label != null) row.Label.color = fg;
                if (row.ShortcutLabel != null) row.ShortcutLabel.color = fg;
                RepaintArrow(row, fg);
                RepaintCheck(row);
            }

            void RepaintArrow(MenuRow row, Color32 fg)
            {
                for (int i = 0; i < row.Arrow.Count; i++)
                    if (row.Arrow[i] != null)
                        row.Arrow[i].color = fg;
            }

            void RepaintCheck(MenuRow row)
            {
                if (row.Check == null)
                    return;
                // 禁用勾选换 check_disabled 件（skin_theme.cpp:1604-1605）
                row.Check.sprite = PixelSkin.Ase(row.Enabled ? "check_selected" : "check_disabled");
            }

            // ------------------------------------------------------------------
            // 小工具
            // ------------------------------------------------------------------

            void DestroyPopup(MenuScope scope)
            {
                if (scope == null)
                    return;
                _popups.Remove(scope);
                if (scope.Owner != null && scope.Owner.Submenu == scope)
                    scope.Owner.Submenu = null;
                if (_hoverRow != null && _hoverRow.Scope == scope)
                {
                    _hoverScope = null;
                    _hoverRow = null;
                }
                if (scope.Rect != null)
                    DestroySafe(scope.Rect.gameObject);
                scope.Rect = null;
            }

            /// <summary>件在画布左上原点、y 向下坐标系的矩形（workarea/父窗都按它算）。</summary>
            Rect BoundsInOverlay(RectTransform rect)
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);                 // 0 左下 / 1 左上 / 2 右上 / 3 右下
                Vector2 tl = ToOverlay(corners[1]);
                Vector2 br = ToOverlay(corners[3]);
                return new Rect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
            }

            Vector2 ToOverlay(Vector3 world)
            {
                Vector2 local = _overlay.InverseTransformPoint(world);
                return new Vector2(local.x + _overlayW * 0.5f, _overlayH * 0.5f - local.y);
            }

            static RectTransform MakeRect(string name, Transform parent)
            {
                var go = new GameObject(name, typeof(RectTransform));
                RectTransform rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                return rect;
            }

            static void PlaceLeft(RectTransform rect, float x, float y, float w, float h)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(w, h);
                rect.anchoredPosition = new Vector2(x, -y);
            }

            static void PlaceRight(RectTransform rect, float rightInset, float w, float h)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(w, h);
                rect.anchoredPosition = new Vector2(-rightInset, 0f);
            }

            static void DestroySafe(GameObject go)
            {
                if (go == null)
                    return;
                if (Application.isPlaying)
                    Object.Destroy(go);
                else
                    Object.DestroyImmediate(go);
            }
        }
    }
}
