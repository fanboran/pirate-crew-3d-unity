using UnityEngine;
using UnityEngine.UI;
using PirateCrew.UI;
using TMPro;   // TextMeshProUGUI / TextAlignmentOptions（标题栏标签）

namespace PirateCrew.UI.Stick
{
    /// <summary>非模态窗口行为（gd StickWindow.Behavior）。</summary>
    public enum StickWindowBehavior
    {
        /// <summary>可拖动标题栏、位置自由（编制管理、工具窗）。</summary>
        Floating,

        /// <summary>固定停靠屏幕角落（建造菜单）。</summary>
        Dock,

        /// <summary>跟随锚点弹出、点击面板外自动关闭（下拉/选择器/提示）。</summary>
        Popover,
    }

    /// <summary>
    /// 非模态浮动窗口基类 —— 移植自 stick-world <c>stick_window.gd</c>。
    /// 与 <see cref="StickScreen"/>（排他模态）相对：**不画全屏遮罩**、不挡游戏、
    /// 可与其他页面元素交互；**不入模态栈**。
    ///
    /// 行为：<see cref="StickWindowBehavior.Floating"/> 拖标题栏移动 /
    /// <see cref="StickWindowBehavior.Dock"/> 停靠角（StickKit.Dock，右上 + SCREEN_MARGIN）/
    /// <see cref="StickWindowBehavior.Popover"/> 锚点弹出、点面板外自关。
    /// ESC 关闭自身（消费，不触发宿主暂停菜单；**必须只在 IsOpen 时消费**，否则
    /// 隐藏窗口会把暂停菜单的 ESC 吞掉——对齐 gd _unhandled_input 的注释纪律）。
    /// gd 的安全矩形夹取挂在 resized 信号上重夹；Unity 侧面板尺寸由 WindowSize 定死
    /// （无内容测量重排），故只在初始定位与拖动后夹取。
    /// </summary>
    public class StickWindow : MonoBehaviour, IStickScreen
    {
        // ---------------- 参数（gd @export） ----------------

        public Vector2 WindowSize = new Vector2(640f, 480f);
        public string WindowTitle = "";
        public StickWindowBehavior Behavior = StickWindowBehavior.Floating;

        /// <summary>POPOVER 锚点（父容器左上原点，向下右为正——Godot 坐标语义）。</summary>
        public Vector2 AnchorPos = new Vector2(200f, 200f);

        // ---------------- 骨架 ----------------

        protected RectTransform Panel;
        protected StickLayoutGroup Body;

        bool _built;

        // ---------------- 构建窗口骨架（无遮罩、可交互） ----------------

        void Awake()
        {
            gameObject.SetActive(false);   // gd _ready: visible=false
        }

        /// <summary>构建窗口骨架（幂等；Open() 自动触发）。</summary>
        public void Build()
        {
            if (_built)
                return;
            _built = true;

            StickUIKit.FullRect((RectTransform)transform);

            // 面板：像素九宫格底 + 投影 + 透明 raycast 拦截（gd _panel.mouse_filter=STOP——
            // 根不拦截、面板拦截，面板外事件穿透到游戏）
            Panel = UiKit.CreateRect("Panel", transform);
            RectTransform backplate = UiKit.CreateRect("Backplate", Panel);
            StickUIKit.FullRect(backplate);
            UiKit.EnsurePanel(backplate, PixelTone.Frame);
            Image blocker = Panel.gameObject.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0f);
            blocker.raycastTarget = true;

            // 内容（separation 4 对齐 gd vbox）
            RectTransform content = UiKit.CreateRect("Content", Panel);
            StickUIKit.FullRect(content);
            StickLayoutGroup vbox = content.gameObject.AddComponent<StickLayoutGroup>();
            vbox.Orientation = StickOrientation.Vertical;
            vbox.Separation = 4f;
            vbox.PaddingLeft = StickTokens.SketchPanelPadX;
            vbox.PaddingRight = StickTokens.SketchPanelPadX;
            vbox.PaddingTop = StickTokens.SketchPanelPadY;
            vbox.PaddingBottom = StickTokens.SketchPanelPadY;

            // 标题栏（拖动把手 + 关闭，高 30）
            RectTransform titleBar = UiKit.CreateRect("TitleBar", content);
            StickLayoutGroup titleRow = titleBar.gameObject.AddComponent<StickLayoutGroup>();
            titleRow.Orientation = StickOrientation.Horizontal;
            titleRow.Separation = 8f;
            StickLayoutElement titleBarMin = titleBar.gameObject.AddComponent<StickLayoutElement>();
            titleBarMin.MinSize = new Vector2(0f, 30f);
            // 拖动把手：标题栏自挂透明 Graphic 承接指针事件（TMP 默认不参与 raycast，
            // 拦截块在 Panel 上——把手必须自己有 Graphic 才能先于面板块命中）
            Image dragArea = titleBar.gameObject.AddComponent<Image>();
            dragArea.color = new Color(0f, 0f, 0f, 0f);
            dragArea.raycastTarget = true;
            if (Behavior == StickWindowBehavior.Floating)
            {
                WindowDragHandle handle = titleBar.gameObject.AddComponent<WindowDragHandle>();
                handle.Init(this);
            }

            TextMeshProUGUI title = UiKit.CreateText("Title", titleBar, WindowTitle,
                (int)StickTokens.FONT_SECTION, TextAlignmentOptions.MidlineLeft,
                StickTokens.TEXT, UiKit.RuntimeFont(UiKit.RuntimeFontKind.Title));
            title.enableWordWrapping = false;
            StickLayoutElement titleGrow = title.rectTransform.gameObject.AddComponent<StickLayoutElement>();
            titleGrow.Grow = 1f;

            var closeBtn = StickKit.AutoButton(titleBar, "✕", Close, StickKit.StickButtonKind.Normal, 30f);
            // Button（Selectable）无 rectTransform 属性，显式取 transform。
            StickLayoutElement closeMin = ((RectTransform)closeBtn.transform).gameObject.AddComponent<StickLayoutElement>();
            closeMin.MinSize = new Vector2(30f, 30f);
            closeMin.CrossAlign = StickCrossAlign.Fill;

            // 内容区（纵向吃满剩余空间）
            RectTransform bodyRect = UiKit.CreateRect("Body", content);
            Body = bodyRect.gameObject.AddComponent<StickLayoutGroup>();
            Body.Orientation = StickOrientation.Vertical;
            Body.Separation = 8f;
            StickLayoutElement bodyGrow = bodyRect.gameObject.AddComponent<StickLayoutElement>();
            bodyGrow.Grow = 1f;

            BuildContent();
            PositionPanel();
        }

        /// <summary>子类实现：往 <see cref="Body"/> 添加内容。</summary>
        protected virtual void BuildContent() { }

        // ---------------- 初始定位（FLOATING 居中；DOCK 停靠；POPOVER 锚点） ----------------

        void PositionPanel()
        {
            Panel.sizeDelta = WindowSize;
            switch (Behavior)
            {
                case StickWindowBehavior.Dock:
                    // 停靠右上角 + 安全边距（gd StickKit.dock(TOP_RIGHT, window_size)）
                    StickKit.Dock(Panel, StickKit.StickCorner.TopRight, WindowSize);
                    break;
                case StickWindowBehavior.Popover:
                    Panel.anchorMin = new Vector2(0f, 1f);
                    Panel.anchorMax = new Vector2(0f, 1f);
                    Panel.pivot = new Vector2(0f, 1f);
                    Panel.anchoredPosition = new Vector2(AnchorPos.x, -AnchorPos.y);
                    break;
                default:
                    Panel.anchorMin = new Vector2(0.5f, 0.5f);
                    Panel.anchorMax = new Vector2(0.5f, 0.5f);
                    Panel.pivot = new Vector2(0.5f, 0.5f);
                    Panel.anchoredPosition = Vector2.zero;
                    break;
            }
            // 尺寸定稿后夹进安全矩形，防"弹窗盖住常驻 HUD 按钮"（Dock 不夹，对齐 gd）
            if (Behavior != StickWindowBehavior.Dock)
                ClampPanelIntoSafeRect();
        }

        /// <summary>把面板矩形夹进安全矩形（保持尺寸，只平移；换算 anchoredPosition 见下）。</summary>
        internal void ClampPanelIntoSafeRect()
        {
            if (Panel == null || Panel.parent == null)
                return;
            Vector2 anchorPoint = AnchorPointOf(Panel);
            Vector2 bl = anchorPoint + Panel.anchoredPosition - Vector2.Scale(Panel.pivot, Panel.sizeDelta);
            Rect clamped = StickKit.ClampToSafeRect(Panel, new Rect(bl, Panel.sizeDelta));
            Panel.anchoredPosition = clamped.position - anchorPoint + Vector2.Scale(Panel.pivot, Panel.sizeDelta);
        }

        /// <summary>锚点在父 rect 空间的位置（anchorMin == anchorMax 的点锚前提）。</summary>
        static Vector2 AnchorPointOf(RectTransform rt)
        {
            Rect parent = ((RectTransform)rt.parent).rect;
            return new Vector2(
                Mathf.LerpUnclamped(parent.xMin, parent.xMax, rt.anchorMin.x),
                Mathf.LerpUnclamped(parent.yMin, parent.yMax, rt.anchorMin.y));
        }

        // ---------------- 拖动（FLOATING，把手在标题栏） ----------------

        /// <summary>记抓取偏移（gd _drag_offset = mouse − panel.position）。
        /// parentLocal 与 anchoredPosition 之差在拖动中保持不变，anchor 偏移自然消去。</summary>
        internal void BeginDrag(Vector2 parentLocalPoint)
        {
            DragOffset = parentLocalPoint - Panel.anchoredPosition;
        }

        /// <summary>拖动跟随 + 夹回安全矩形。</summary>
        internal void DragTo(Vector2 parentLocalPoint)
        {
            Panel.anchoredPosition = parentLocalPoint - DragOffset;
            ClampPanelIntoSafeRect();
        }

        /// <summary>拖动抓取偏移（父 rect 空间指针位置 − 面板 anchoredPosition）。</summary>
        internal Vector2 DragOffset { get; private set; }

        // ---------------- ESC 自关 / POPOVER 点外自关（轮询，gd _unhandled_input 等价） ----------------

        void Update()
        {
            // 必须检查 IsOpen：本节点常驻场景树，隐藏时若仍消费 ESC 会吞掉宿主暂停菜单的 ESC
            if (!IsOpen)
                return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            if (Behavior == StickWindowBehavior.Popover &&
                (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) &&
                !RectTransformUtility.RectangleContainsScreenPoint(Panel, Input.mousePosition, CanvasCamera()))
            {
                Close();
            }
        }

        Camera CanvasCamera()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            return canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera
                ? canvas.worldCamera
                : null;
        }

        // ---------------- 开关 ----------------

        public virtual void Open()
        {
            Build();
            gameObject.SetActive(true);
        }

        public virtual void Close()
        {
            gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public bool IsOpen
        {
            get { return gameObject.activeSelf; }
        }

        /// <summary>标题栏拖动把手（gd _on_title_input 的 UGUI 等价：按下记偏移、拖动跟随）。</summary>
        sealed class WindowDragHandle : MonoBehaviour, UnityEngine.EventSystems.IBeginDragHandler,
            UnityEngine.EventSystems.IDragHandler
        {
            StickWindow _window;
            bool _dragging;

            public void Init(StickWindow window)
            {
                _window = window;
            }

            void UnityEngine.EventSystems.IBeginDragHandler.OnBeginDrag(
                UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (_window == null || !_window.IsOpen)
                    return;
                _dragging = true;
                _window.DragOffset = ParentLocalPoint(eventData) - _window.Panel.anchoredPosition;
            }

            void UnityEngine.EventSystems.IDragHandler.OnDrag(
                UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (!_dragging || _window == null)
                    return;
                _window.DragTo(ParentLocalPoint(eventData));
            }

            Vector2 ParentLocalPoint(UnityEngine.EventSystems.PointerEventData eventData)
            {
                RectTransform parent = (RectTransform)_window.Panel.parent;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out Vector2 local);
                return local;
            }
        }
    }
}
