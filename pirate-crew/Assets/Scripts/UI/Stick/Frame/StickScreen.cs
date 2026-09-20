using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PirateCrew.UI;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// 统一模态弹窗基类 —— 移植自 stick-world <c>stick_screen.gd</c>。
    ///
    /// 结构：全屏半透明遮罩（bg_alpha 0.55 纯压暗；gd 的 GenerativeBackdrop 生成艺术
    /// 背景属 P2，此处先用纯色压暗）+ 居中面板（Sketch9Slice 九砖手绘，DARK 槽）+
    /// 垂直骨架（标题 / body 内容区 / footer 底栏）。所有弹窗共用此骨架 → 外观与
    /// 边距天然一致。面板内边距取 SketchPanel 令牌 16/12（gd 侧 PANEL_PAD_X/Y，
    /// stick_screen 头注释的"24/12"是旧 GlassStyle 描述，实际生效的是 SketchPanel 的 16/12）。
    ///
    /// 子类用法：
    ///   class MyDialog : StickScreen
    ///   {
    ///       // 字段初始化器里覆盖 PanelSize / PanelTitle / BgAlpha
    ///       protected override void BuildContent()   // 往 Body / Footer 加内容
    ///   }
    /// 生命周期：Open() / Close() / Toggle() / IsOpen（与 gd 同契约）。
    ///
    /// 【暂停语义】gd open() 打开即拉引擎暂停总闸（记原速度、close 恢复）；
    /// Unity 侧不碰 Time.timeScale——经 <see cref="PauseRequested"/>（true 打开 / false 关闭，
    /// 且只对"由本面板打开的暂停"负责）暴露，接暂停是宿主的事；主菜单等无游戏时间
    /// 的场景不订阅即可（对齐 gd _in_game_context 分支的效果）。
    /// </summary>
    public abstract class StickScreen : MonoBehaviour, IStickScreen
    {
        // ---------------- 参数（gd @export 三件，子类覆盖） ----------------

        /// <summary>居中面板尺寸（gd panel_size，默认 640×480）。</summary>
        public Vector2 PanelSize = new Vector2(640f, 480f);

        /// <summary>遮罩压暗强度（gd bg_alpha 0.55）。</summary>
        [Range(0f, 1f)] public float BgAlpha = 0.55f;

        /// <summary>面板标题（gd panel_title）。</summary>
        public string PanelTitle = "";

        // ---------------- 骨架节点（子类经 Body / Footer 填内容） ----------------

        protected RectTransform Panel;      // 居中面板根（九砖手绘底）
        protected TextMeshProUGUI TitleLabel;
        /// <summary>内容区（gd _body；子类往这里加控件，禁手写 position）。</summary>
        protected StickLayoutGroup Body;
        /// <summary>底栏（gd _footer；子类往这里加动作按钮，右对齐）。</summary>
        protected StickLayoutGroup Footer;

        /// <summary>暂停请求（true = 本面板打开，false = 本面板关闭并交还控制权）。</summary>
        public event Action<bool> PauseRequested;

        bool _built;
        bool _pauseHeld;

        // ---------------- 构建骨架 ----------------

        void Awake()
        {
            gameObject.SetActive(false);   // gd _ready: visible=false
        }

        /// <summary>构建遮罩 + 居中面板 + 统一骨架（幂等；Open() 自动触发，子类也可提前调用）。</summary>
        public void Build()
        {
            if (_built)
                return;
            _built = true;

            // 根铺满父容器（ModalOverlay 槽），否则内部遮罩塌缩成 0 尺寸不可见
            StickUIKit.FullRect((RectTransform)transform);

            // 遮罩：纯压暗（P2 再接生成艺术背景）；raycastTarget 消费鼠标防穿透
            RectTransform dim = UiKit.CreateRect("Dim", transform);
            StickUIKit.FullRect(dim);
            Image dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0.02f, 0.03f, 0.06f, BgAlpha);   // gd dim_color
            dimImage.raycastTarget = true;

            // 居中面板：九砖手绘底（backplate 与布局兄弟隔离——Sketch9Slice 的 9 块砖
            // 是面板的直接子件，若布局挂面板根会把砖当内容排进去）
            Panel = UiKit.CreateRect("Panel", transform);
            RectTransform backplate = UiKit.CreateRect("Backplate", Panel);
            StickUIKit.FullRect(backplate);
            backplate.gameObject.AddComponent<Sketch9Slice>().Slot = "panel";

            // 垂直骨架：标题 / body / footer（separation 12 对齐 gd）
            RectTransform content = UiKit.CreateRect("Content", Panel);
            StickUIKit.FullRect(content);
            StickLayoutGroup vbox = content.gameObject.AddComponent<StickLayoutGroup>();
            vbox.Orientation = StickOrientation.Vertical;
            vbox.Separation = 12f;
            vbox.PaddingLeft = StickTokens.SketchPanelPadX;
            vbox.PaddingRight = StickTokens.SketchPanelPadX;
            vbox.PaddingTop = StickTokens.SketchPanelPadY;
            vbox.PaddingBottom = StickTokens.SketchPanelPadY;

            // 标题（居中，FONT_TITLE）
            TitleLabel = UiKit.CreateText("Title", content, PanelTitle, (int)StickTokens.FONT_TITLE,
                TextAlignmentOptions.Center, StickTokens.TEXT, UiKit.RuntimeFont(UiKit.RuntimeFontKind.Title));
            TitleLabel.enableWordWrapping = false;

            // 内容区（纵向吃满剩余空间，gd SIZE_EXPAND_FILL → Grow=1）
            RectTransform bodyRect = UiKit.CreateRect("Body", content);
            Body = bodyRect.gameObject.AddComponent<StickLayoutGroup>();
            Body.Orientation = StickOrientation.Vertical;
            Body.Separation = 8f;
            StickLayoutElement bodyGrow = bodyRect.gameObject.AddComponent<StickLayoutElement>();
            bodyGrow.Grow = 1f;

            // 底栏（动作按钮右对齐）
            RectTransform footerRect = UiKit.CreateRect("Footer", content);
            Footer = footerRect.gameObject.AddComponent<StickLayoutGroup>();
            Footer.Orientation = StickOrientation.Horizontal;
            Footer.Separation = 8f;
            Footer.MainAlign = StickAlign.End;

            BuildContent();
        }

        /// <summary>子类实现：往 <see cref="Body"/> / <see cref="Footer"/> 添加内容
        ///（走 StickKit 装配器 + StickLayout 布局，禁止手写 position）。</summary>
        protected virtual void BuildContent() { }

        /// <summary>居中定位（gd open() 的 anchor 方案重设——不受窗口尺寸影响）。</summary>
        void ApplyPanelLayout()
        {
            Panel.anchorMin = new Vector2(0.5f, 0.5f);
            Panel.anchorMax = new Vector2(0.5f, 0.5f);
            Panel.pivot = new Vector2(0.5f, 0.5f);
            Panel.sizeDelta = PanelSize;
            Panel.anchoredPosition = Vector2.zero;
        }

        // ---------------- 开关 ----------------

        public virtual void Open()
        {
            Build();
            ApplyPanelLayout();
            if (!_pauseHeld)
            {
                _pauseHeld = true;
                PauseRequested?.Invoke(true);   // 打开即请求拉总闸（宿主决定是否生效）
            }
            gameObject.SetActive(true);
        }

        public virtual void Close()
        {
            gameObject.SetActive(false);
            // 只交还"由本面板打开的暂停"（嵌套模态时由最外层恢复，对齐 gd _prev_speed 簿记）
            if (_pauseHeld)
            {
                _pauseHeld = false;
                PauseRequested?.Invoke(false);
            }
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
    }
}
