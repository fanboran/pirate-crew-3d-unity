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

        /// <summary>行有子菜单（ui::MenuItem::hasSubmenu 同义：submenu 非空且孩子非空）。</summary>
        internal static bool HasChildren(Item item)
        {
            return item != null && item.Children != null && item.Children.Length > 0;
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
                    // 助记符 '&' 不进显示文本（Widget::processMnemonicFromText，widget.cpp:1587-1612）
                    string barText = DisplayText(title);
                    var row = new MenuRow
                    {
                        Data = new Item { Label = title, Children = items },
                        Scope = _bar,
                        Rect = MakeRect("Menu_" + title, barRect),
                    };
                    row.Face = row.Rect.gameObject.AddComponent<Image>();
                    row.Face.color = FaceNormal;
                    row.Face.raycastTarget = false;
                    row.Label = DebugWindowKit.Label(row.Rect, barText, UiSkin.Font.Tiny,
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
                // 键盘泵（Input.GetKeyDown；本工程既有姿势见 SketchSlider.HandleKeyboard）
                HandleKeyboard();

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

                // 不清本行的 m_highlighted —— 源码 MenuItem::closeSubmenu 只销子菜单窗并**把焦点还回
                // 父 menubox**（menu.cpp:969-975），行的高亮位保持不动：Esc 收掉一层弹层后，父层那一行
                // 仍是高亮的（栏上就是「栏项仍高亮 = 菜单栏重新持焦」的键盘入口）。
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
            // 键盘导航（MenuBox::onProcessMessage kKeyDownMessage，menu.cpp:611-795）
            //   + cancelMenuLoop（menu.cpp:1379-1398）
            //   + check_for_letter / find_nextitem / find_previtem（menu.cpp:1414-1477）
            //   + Widget::isMnemonicPressed / processMnemonicFromText（widget.cpp:1587-1621）
            //
            // 【焦点等价判定】源码里键盘消息只发给**持焦控件**（菜单栏，或某个 MenuBox 弹窗）：
            //   · 弹窗一开，MenuBoxWindow 的 menubox 是 focus magnet，管理器的 _openWindow 直接夺焦
            //     （menu.cpp:1487 + manager.cpp:2394-2400 findMagneticWidget/setFocus）→ 最深弹层持焦；
            //   · 收掉一层弹层时 closeSubmenu 把焦点还回父 menubox（menu.cpp:969-975），且行的高亮位
            //     不清 → 收完最后一层后**菜单栏重新持焦**（栏项仍高亮）；
            //   · 全程收干净后 cancelMenuLoop 调 freeFocus（menu.cpp:1398）→ 键盘彻底交还。
            // 本工程无焦点系统，按上表落成：**弹层链开着 → 最深弹层接管；弹层全关而栏项还高亮 →
            // 菜单栏接管；两者都不成立 → 谁也不接管**。
            //
            // 【键位映射】aseprite scancode → Unity KeyCode：
            //   kKeyEsc→Escape / kKeyUp→UpArrow / kKeyDown→DownArrow / kKeyLeft→LeftArrow /
            //   kKeyRight→RightArrow / kKeyEnter,kKeyEnterPad→Return,KeypadEnter /
            //   修饰键 kKeyAlt→LeftAlt|RightAlt、kKeyCtrl|kKeyCmd→LeftControl|RightControl|
            //   LeftCommand|RightCommand、kKeyShift→LeftShift|RightShift。键位按下用 Input.GetKeyDown 泵。
            // 登记偏差：kKeyEnterPad 在源里与 kKeyEnter 同分支，本移植把 KeypadEnter 并入即可；IME
            // 组合中/中文键入走 Input.inputString（助记符 unicodeChar 通道），无 IME 预编辑态处理。
            // ------------------------------------------------------------------

            /// <summary>菜单栏的助记符总闸（MenuBar::processTopLevelShortcuts，menu.h:96-100）。
            /// 参考程序的主菜单栏按 kNo 构造（<c>src/app/ui/main_menu_bar.cpp:25</c>），故**栏上
            /// Alt+字母不触发助记符**——本移植保持同档；要开只改这一个常量。</summary>
            const bool ProcessTopLevelShortcuts = false;

            /// <summary>本帧的键盘宿主层（见上方焦点判定）：最深弹层 / 高亮中的菜单栏 / 无。</summary>
            MenuScope FocusScope()
            {
                if (_popups.Count > 0)
                    return _popups[_popups.Count - 1];   // 子层总是晚于父层建，列表尾 = 最深
                return _bar != null && _bar.Highlighted != null ? _bar : null;
            }

            void HandleKeyboard()
            {
                if (_closing || _isProcessing)             // menu.cpp:620-622 base->is_processing
                    return;

                MenuScope focus = FocusScope();
                if (focus == null)                         // 源码：没有持焦控件就收不到 kKeyDown
                    return;

                if (!AnyKeyDown())
                    return;

                // base->was_clicked = false（menu.cpp:625）——键盘一介入，「悬停切换」立即停。
                // 源码对**任意** kKeyDownMessage 都置位；Input.anyKeyDown 含鼠标键，故排掉三个
                // 鼠标键近似「键盘/手柄键」（approx：手柄键仍会误触本行）。
                _wasClicked = false;

                bool alt = AltHeld();
                bool ctrl = CtrlOrCmdHeld();
                bool shift = ShiftHeld();

                // --- ALT+助记符（menu.cpp:627-641）---
                // 弹层（kMenuBoxWidget）：修饰键为「无」或「仅 Alt」都行；菜单栏（kMenuBarWidget）：
                // 必须「仅 Alt」且 processTopLevelShortcuts()。
                bool mnemonicAllowed = focus.InBar
                    ? (alt && !ctrl && !shift && ProcessTopLevelShortcuts)
                    : (!ctrl && !shift);
                if (mnemonicAllowed)
                {
                    MenuRow hit = CheckForLetter(focus);
                    if (hit != null)
                    {
                        HighlightItem(focus, hit, true, true, true);   // menu.cpp:636
                        return;
                    }
                }

                // --- 高亮移动（menu.cpp:640-787，外层 this->hasFocus() 恒真）---
                if (HandleMovementKey(focus))
                    return;

                // --- 只按 Alt：关全部（menu.cpp:789-792 cancelMenuLoop）---
                if (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt))
                    CancelMenuLoop();
            }

            /// <summary>menu.cpp:640-783 的 <c>switch (scancode)</c>。返回是否消费了按键。</summary>
            bool HandleMovementKey(MenuScope scope)
            {
                // Search a child with highlight or the submenu opened（menu.cpp:646-656）
                MenuRow highlight = scope.Highlighted;
                MenuRow childWithSubmenuOpened = ChildWithSubmenuOpened(scope);
                if (highlight == null && childWithSubmenuOpened != null)
                    highlight = childWithSubmenuOpened;

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    KeyEscape(scope, childWithSubmenuOpened);
                    return true;
                }

                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    if (scope.InBar)
                    {
                        // 菜单栏：收掉展开的那层子菜单（menu.cpp:684-687）
                        if (childWithSubmenuOpened != null)
                            CloseSubmenu(childWithSubmenuOpened, true);
                    }
                    else
                    {
                        // 弹层：上一个（menu.cpp:690-693）
                        HighlightItem(scope, FindPrevItem(scope, highlight), false, false, false);
                    }
                    return true;
                }

                if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    if (scope.InBar)
                    {
                        // 菜单栏：选中当前高亮项（带 click → 无子菜单的栏项直接执行，menu.cpp:699-702）
                        HighlightItem(scope, highlight, true, true, true);
                    }
                    else
                    {
                        // 弹层：下一个（menu.cpp:704-708）
                        HighlightItem(scope, FindNextItem(scope, highlight), false, false, false);
                    }
                    return true;
                }

                if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    if (scope.InBar)
                    {
                        // 菜单栏：上一项，**不展开**（menu.cpp:714-718）
                        HighlightItem(scope, FindPrevItem(scope, highlight), false, false, false);
                    }
                    else if (scope.Owner != null)
                    {
                        // menu.cpp:722-740：看父层是不是菜单栏
                        MenuScope parent = scope.Owner.Scope;
                        if (parent != null && parent.InBar)
                        {
                            // 退到菜单栏，移到上一项并展开（menu.cpp:728-733）
                            HighlightItem(parent, FindPrevItem(parent, parent.Highlighted), false, true, true);
                        }
                        else
                        {
                            // 只退回一层父菜单（menu.cpp:736-739）
                            CloseSubmenu(scope.Owner, true);
                        }
                    }
                    return true;
                }

                if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    if (scope.InBar)
                    {
                        // 菜单栏：下一项，**不展开**（menu.cpp:747-751）
                        HighlightItem(scope, FindNextItem(scope, highlight), false, false, false);
                    }
                    else if (highlight != null && highlight.HasSubmenu)
                    {
                        // 进子菜单（menu.cpp:755-757）
                        HighlightItem(scope, highlight, true, true, true);
                    }
                    else if (scope.Owner != null)
                    {
                        // 回到根菜单，移到下一项并展开（menu.cpp:759-772）
                        MenuScope root = RootScope(scope);
                        HighlightItem(root, FindNextItem(root, root != null ? root.Highlighted : null),
                            false, true, true);
                    }
                    return true;
                }

                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    // kKeyEnter / kKeyEnterPad（menu.cpp:777-781）
                    if (highlight != null)
                        HighlightItem(scope, highlight, true, true, true);
                    return true;
                }

                return false;
            }

            /// <summary>kKeyEsc 分支（menu.cpp:660-681）。</summary>
            void KeyEscape(MenuScope scope, MenuRow childWithSubmenuOpened)
            {
                if (scope.InBar)
                {
                    // 菜单栏：有高亮才关全部（menu.cpp:661-666）
                    if (scope.Highlighted != null)
                        CancelMenuLoop();
                    return;
                }

                if (childWithSubmenuOpened != null)
                {
                    // 先收掉本层展开的子菜单（menu.cpp:669-672）
                    CloseSubmenu(childWithSubmenuOpened, true);
                }
                else if (scope.Owner != null)
                {
                    // 退回一层父菜单（menu.cpp:674-678）
                    CloseSubmenu(scope.Owner, true);
                }
                else
                {
                    // 顶层独立弹层（showPopup）：源码走前台模态循环被 kCloseDisplayMessage 收掉
                    // （menu.cpp:461-467 menu->closeAll），等价地在这里直接收。
                    CancelMenuLoop();
                }
            }

            /// <summary>MenuBox::cancelMenuLoop（menu.cpp:1379-1398）：closeAll + 弃键盘焦点。
            /// <c>Manager::freeFocus()</c> 无对应物（无焦点系统）；收干净后栏项高亮被清（Menu::closeAll
            /// 的 menu->unhighlightItem()，menu.cpp:1330），FocusScope 自然回到 null。</summary>
            void CancelMenuLoop()
            {
                CloseMenus();
            }

            /// <summary>menu.cpp:1414-1427 check_for_letter：只扫**本层**子件（不递归）。
            /// 注意源码此处**不查 enabled**——禁用项带助记符照样被选中。</summary>
            static MenuRow CheckForLetter(MenuScope scope)
            {
                for (int i = 0; i < scope.Rows.Count; i++)
                {
                    MenuRow row = scope.Rows[i];
                    if (row.Data.Separator)
                        continue;
                    if (MnemonicPressed(MnemonicOf(row.Data.Label)))
                        return row;
                }
                return null;
            }

            /// <summary>menu.cpp:1429-1451 find_nextitem：从 menuitem 之后找第一个启用项；
            /// 到尾则绕回开头（<paramref name="from"/> == null 时从首项起，找不到即 null）。</summary>
            static MenuRow FindNextItem(MenuScope scope, MenuRow from)
            {
                int start = 0;
                if (from != null)
                {
                    int i = scope.Rows.IndexOf(from);
                    start = i >= 0 ? i + 1 : 0;      // std::find 落空 → it==end → 递归从头找
                }
                for (int i = start; i < scope.Rows.Count; i++)
                    if (IsNavigable(scope.Rows[i]))
                        return scope.Rows[i];
                return from != null ? FindNextItem(scope, null) : null;
            }

            /// <summary>menu.cpp:1454-1477 find_previtem：反向找，语义同 <see cref="FindNextItem"/>。</summary>
            static MenuRow FindPrevItem(MenuScope scope, MenuRow from)
            {
                int start = scope.Rows.Count - 1;
                if (from != null)
                {
                    int i = scope.Rows.IndexOf(from);
                    start = i >= 0 ? i - 1 : scope.Rows.Count - 1;
                }
                for (int i = start; i >= 0; i--)
                    if (IsNavigable(scope.Rows[i]))
                        return scope.Rows[i];
                return from != null ? FindPrevItem(scope, null) : null;
            }

            /// <summary>源码的过滤条件：<c>type == kMenuItemWidget &amp;&amp; isEnabled()</c>——分隔线
            /// 不是 MenuItem 故天然跳过，禁用项跳过（menu.cpp:1444-1445、1467-1468）。</summary>
            static bool IsNavigable(MenuRow row)
            {
                return row != null && !row.Data.Separator && row.Enabled;
            }

            /// <summary>本层带子菜单展开的那个子件（menu.cpp:648-655；源码后写覆盖前值，无 break——
            /// 同层同时只可能开一个，这里照抄语义）。</summary>
            static MenuRow ChildWithSubmenuOpened(MenuScope scope)
            {
                MenuRow found = null;
                for (int i = 0; i < scope.Rows.Count; i++)
                    if (scope.Rows[i].Submenu != null)
                        found = scope.Rows[i];
                return found;
            }

            /// <summary>根层菜单（get_base_menubox 取到的根 Menu，menu.cpp:1058-1098）：沿 m_menuitem
            /// 链上溯（Menu::m_menuitem = 展开本层的那一行）。</summary>
            static MenuScope RootScope(MenuScope scope)
            {
                while (scope != null && scope.Owner != null)
                    scope = scope.Owner.Scope;
                return scope;
            }

            /// <summary>Widget::processMnemonicFromText（widget.cpp:1587-1612）：<c>'&amp;'</c> 后的字符是
            /// 助记符且 <c>'&amp;'</c> 本身**不显示**；<c>"&amp;&amp;"</c> 是字面 <c>'&amp;'</c>（不设助记符）；
            /// <c>'&amp;'</c> 后是空白不设助记符（<c>'&amp;'</c> 原样留着）；串以 <c>'&amp;'</c> 结尾则截断。</summary>
            internal static string StripMnemonic(string label, out char mnemonic)
            {
                mnemonic = '\0';
                if (string.IsNullOrEmpty(label))
                    return label;

                var sb = new System.Text.StringBuilder(label.Length);
                for (int i = 0; i < label.Length; i++)
                {
                    char c = label[i];
                    if (c != '&')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i + 1 >= label.Length)
                        break;                            // 畸形串（以 escape 结尾）：源码 break

                    char next = label[++i];
                    if (char.IsWhiteSpace(next))
                        sb.Append('&').Append(next);      // 空格不做助记符
                    else if (next != '&')
                        mnemonic = next;                  // 吃到 '&'，下一字符作助记符
                    else
                        sb.Append('&');                   // "&&" → 字面 '&'
                }
                return sb.ToString();
            }

            internal static char MnemonicOf(string label)
            {
                char mnemonic;
                StripMnemonic(label, out mnemonic);
                return mnemonic;
            }

            /// <summary>显示用文本（吃掉助记符转义，见 <see cref="StripMnemonic"/>）。</summary>
            internal static string DisplayText(string label)
            {
                char ignored;
                return StripMnemonic(label, out ignored);
            }

            /// <summary>Widget::isMnemonicPressed（widget.cpp:1615-1621）：不区分大小写地比 mnemonic
            /// （unicodeChar 相等，或 scancode 落在 a-z / 0-9 区间）。本移植：a-z / 0-9 走 KeyCode 区间，
            /// 其余（含中文助记符）走 Input.inputString 的 unicode 通道。</summary>
            static bool MnemonicPressed(char mnemonic)
            {
                if (mnemonic == '\0')
                    return false;

                char lower = char.ToLowerInvariant(mnemonic);
                if (lower >= 'a' && lower <= 'z')
                    return Input.GetKeyDown((KeyCode)((int)KeyCode.A + (lower - 'a')));
                if (lower >= '0' && lower <= '9')
                    return Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + (lower - '0')));

                string typed = Input.inputString;
                for (int i = 0; i < typed.Length; i++)
                    if (char.ToLowerInvariant(typed[i]) == lower)
                        return true;
                return false;
            }

            static bool AltHeld()
            {
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            }

            static bool ShiftHeld()
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }

            /// <summary>kKeyCtrl / kKeyCmd 在 Unity 里落在 Control 与 Command 两族（approx：源码按平台
            /// 二选一，这里两族都收）。</summary>
            static bool CtrlOrCmdHeld()
            {
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                    || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            }

            /// <summary>本帧是否有「键盘键」按下：Input.anyKeyDown 含鼠标键，排掉三个鼠标键（approx）。</summary>
            static bool AnyKeyDown()
            {
                return Input.anyKeyDown
                    && !Input.GetMouseButtonDown(0)
                    && !Input.GetMouseButtonDown(1)
                    && !Input.GetMouseButtonDown(2);
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

                float contentH = y - MenuSliceT;               // 行列表总高（Menu::onSizeHint）
                float popupW = menuW + MenuSliceL + MenuSliceR;
                float popupH = y + MenuSliceB;

                // 定位（fit_bounds 单显示器分支的 fitLogic）：先按锚件算原始落点，
                // 再由 add_scrollbars 钳进 workarea——钳取会**同时**收缩窗口并（竖轴）加滚动条。
                Vector2 pos = FitBounds(owner, anchor, side, popupW, popupH);
                float winX = pos.x, winY = pos.y, winW = popupW, winH = popupH;
                bool scrollable = AddScrollbarsIfNeeded(ref winX, ref winY, ref winW, ref winH);

                scope.Width = winW;
                scope.Height = winH;
                scope.Rect.sizeDelta = new Vector2(winW, winH);
                scope.Rect.anchoredPosition = new Vector2(winX, -winY);

                if (scrollable)
                    BuildScrollableView(scope, menuW, contentH, winW, winH);
                else
                    for (int i = 0; i < scope.Rows.Count; i++)
                        LayoutRow(scope.Rows[i], menuW, MenuSliceL, 0f);

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
                    row.Label = DebugWindowKit.Label(row.Rect, DisplayText(data.Label),
                        UiSkin.Font.Tiny, TextNormal, TextAlignmentOptions.Left);
                    // 快捷键只在**没有子菜单**的行上画（paintMenuItem 的 if/else if：带子菜单画箭头、
                    // 否则才画快捷键，skin_theme.cpp:1626-1667）
                    if (!string.IsNullOrEmpty(data.Shortcut) && !HasChildren(data))
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

            /// <summary>行内落点：paintMenuItem 逐条换算（skin_theme.cpp:1600-1674）。
            /// <paramref name="x"/>/<paramref name="yBase"/> 是所在容器的行坐标原点：
            /// 未溢出 = 弹窗根（<c>MenuSliceL</c>, 0，行落在弹窗内容区、左/上各让边框 3）；
            /// 溢出 = View 里的 <c>Menu</c> 容器（0, <c>MenuSliceT</c>，行坐标相对内容区原点）。</summary>
            void LayoutRow(MenuRow row, float menuW, float x, float yBase)
            {
                row.W = menuW;
                row.Rect.sizeDelta = new Vector2(menuW, row.H);
                row.Rect.anchoredPosition = new Vector2(x, -(row.Y - yBase));

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
                        Mathf.Max(0f, _overlayH - 1f - popupH));
                    Rect parentBounds = BoundsInOverlay(owner.Scope.Rect);
                    return new Vector2(ChooseSideX(parentBounds, popupW, popupH, sy), sy);
                }

                // 一层下拉 / 独立弹出：锚件正下、左缘钳进 workarea（menu.cpp:905-910）
                Rect a = BoundsInOverlay(anchor);
                float x = side
                    ? Mathf.Clamp(a.xMax, 0f, Mathf.Max(0f, _overlayW - 1f - popupW))   // 侧向：锚件右侧
                    : Mathf.Clamp(a.xMin, 0f, Mathf.Max(0f, _overlayW - 1f - popupW));

                if (owner != null && owner.InBar)
                {
                    // 栏项下拉（menu.cpp:909）：y = max(workarea.y, itemBounds.y2())，**不因超高上移**——
                    // 放不下交给 add_scrollbars 收高度加滚动条。源码次序是先 add_scrollbars 再钳位置，
                    // 而末尾钳位置用的已是收缩后的高度，对 y 恒为恒等，故这里不重复钳。
                    return new Vector2(x, Mathf.Max(0f, a.yMax));
                }

                // 独立弹出（showPopup menu.cpp:336-344）：choose_side 已把 y 用自然高度钳进 workarea
                float y = side ? a.yMin : a.yMax;
                y = Mathf.Clamp(y, 0f, Mathf.Max(0f, _overlayH - 1f - popupH));
                return new Vector2(x, y);
            }

            /// <summary>choose_side（menu.cpp:115-146）：左右两侧各算与父窗的重叠，取重叠少的一侧。</summary>
            float ChooseSideX(Rect parentBounds, float w, float h, float y)
            {
                float xLeft = parentBounds.xMin - w + SideOverlap;
                float xRight = parentBounds.xMax - 1f - SideOverlap;   // 源 x2()=x+w-1（闭区间），比 xMax 紧 1px
                float maxX = Mathf.Max(0f, _overlayW - 1f - w);
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
            // add_scrollbars（scroll_window.cpp:23-91）+ 菜单滚轮（menu.cpp:797-800）
            // ------------------------------------------------------------------

            /// <summary>
            /// scroll_window.cpp:23-91 <c>add_scrollbars(window, workarea, bounds, IfNeeded)</c>：
            /// 窗口矩形先横向、再纵向钳进 workarea（<c>x2()</c>/<c>y2()</c> 是开区间 = x+w / y+h）；
            /// 只有真的发生钳取（<c>rc != bounds</c>）才需要 View。竖轴钳取还会给窗口加宽
            /// <c>2 * 滚动条宽</c>（源 <c>barWidth = theme getScrollbarSize()</c> = theme.xml
            /// <c>scrollbar_size</c> = 12），让被滚内容不被滚动条吃掉宽度。返回 true = 溢出。
            /// </summary>
            bool AddScrollbarsIfNeeded(ref float x, ref float y, ref float w, ref float h)
            {
                float rx = x, ry = y, rw = w, rh = h;
                float workX2 = _overlayW;                // workarea.x2()（开区间）
                float workY2 = _overlayH;

                if (rx < 0f)                             // scroll_window.cpp:30-33
                {
                    rw -= (0f - rx);
                    rx = 0f;
                }
                if (rx + rw > workX2)                    // scroll_window.cpp:34-36
                    rw = workX2 - rx;

                bool vScrollbarsAdded = false;           // scroll_window.cpp:38-47
                if (ry < 0f)
                {
                    rh -= (0f - ry);
                    ry = 0f;
                    vScrollbarsAdded = true;
                }
                if (ry + rh > workY2)
                {
                    rh = workY2 - ry;
                    vScrollbarsAdded = true;
                }

                if (rx == x && ry == y && rw == w && rh == h)
                    return false;                        // scroll_window.cpp:49-50（IfNeeded 未钳取 → 不加 View）

                if (vScrollbarsAdded)                    // scroll_window.cpp:59-72
                {
                    float barWidth = AseLayout.ScrollbarSize;
                    rw += 2f * barWidth;
                    if (rx + rw > workX2)
                    {
                        rx = workX2 - rw;
                        if (rx < 0f)
                        {
                            rx = 0f;
                            rw = _overlayW;
                        }
                    }
                }

                x = rx;
                y = ry;
                w = rw;
                h = rh;
                return true;
            }

            /// <summary>
            /// add_scrollbars 的 UGUI 落地（源里 <c>window</c> = MenuBoxWindow、被滚件 = MenuBox/Menu）：
            /// View 用 <c>view_base</c> 样式（theme.xml:582 只有 window_face 底色、**无边框**），
            /// 填满弹窗客户区；行列表挂进 Viewport 当被滚件。视口溢出时由 <see cref="AseView"/>
            /// 按 setup_scrollbars 的 IfNeeded 判定挂 THEME 滚动条（不重造滚动条）。
            /// </summary>
            void BuildScrollableView(MenuScope scope, float menuW, float contentH, float winW, float winH)
            {
                RectTransform viewRect = MakeRect("View", scope.Rect);
                viewRect.sizeDelta = new Vector2(
                    Mathf.Max(0f, winW - MenuSliceL - MenuSliceR),
                    Mathf.Max(0f, winH - MenuSliceT - MenuSliceB));
                viewRect.anchoredPosition = new Vector2(MenuSliceL, -MenuSliceT);

                AseView view = AseView.Attach(viewRect, 0, 0, 0, 0);

                RectTransform content = MakeRect("Menu", scope.Rect);
                view.AttachToView(content);
                view.SetContentHint(Mathf.Max(1, Mathf.RoundToInt(menuW)),
                    Mathf.Max(1, Mathf.RoundToInt(contentH)));
                view.UpdateView();                       // view.cpp:143-190

                scope.View = view;
                scope.Content = content;

                // 源里 MenuBox 被 View 撑到视口大小（view.cpp:389-402 Viewport::onResize），Menu 再铺满
                // MenuBox（Menu::onResize menu.cpp:390-419）→ 行宽 = 视口宽（不是 menuW）。
                // 行从弹窗根改挂到 Menu 容器（源 attachToView 把 Menu 塞进 viewport）。
                float rowW = content.sizeDelta.x;
                for (int i = 0; i < scope.Rows.Count; i++)
                {
                    scope.Rows[i].Rect.SetParent(content, false);
                    LayoutRow(scope.Rows[i], rowW, 0f, MenuSliceT);
                }

                // menu.cpp:797-800 MenuBox::kMouseWheelMessage → View::scrollByMessage：
                // 滚轮在整个弹窗客户区都能滚（源里收件件是撑满视口的 MenuBox）。
                viewRect.gameObject.AddComponent<AseViewWheel>().Bind(view);
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
                    if (scope.View != null)
                    {
                        // 行在 View 的视口里：先把指针换到**视口局部**（顺带做裁剪判定——滚出视口的行
                        // 在源里 pick 不到），再加滚动偏移对到内容坐标。
                        RectTransform vp = scope.View.Viewport;
                        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                vp, e.position, cam, out Vector2 vpLocal))
                            return null;
                        float vx = vpLocal.x;
                        float vy = -vpLocal.y;                 // pivot (0,1)：y 向下为正
                        if (vx < 0f || vy < 0f || vx > vp.rect.width || vy > vp.rect.height)
                            return null;

                        float contentY = vy + scope.View.ViewScroll.y;
                        for (int i = 0; i < scope.Rows.Count; i++)
                        {
                            MenuRow row = scope.Rows[i];
                            float top = scope.RowTopInContent(row);
                            if (contentY >= top && contentY < top + row.H)
                                return row;
                        }
                        return null;
                    }

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
