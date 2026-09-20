using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>模式面板切换契约（gd 侧 ModePanel.switch_to(panel_type) 的接口化）。
    /// 模式→面板映射由装配方负责注入/实现，本层不依赖业务模式枚举（断 ui_global↔player_control 环）。</summary>
    public interface IStickModePanel
    {
        /// <summary>切换到指定面板类型（int 直传，语义同 gd UIAPI.PanelType）。</summary>
        void SwitchTo(int panelType);
    }

    /// <summary>
    /// 上下文面板结构锚：挂在 ContextPanel 下"场景声明的结构性子槽"上，清理动态内容时跳过。
    /// 对齐 gd 侧 <c>child.owner == null</code> 判定（场景声明节点 owner 非空）——Unity 无
    /// owner 概念，改由显式组件标记（如 ContextPanel/SquadInspector 这类槽）。
    /// </summary>
    public sealed class StickContextAnchor : MonoBehaviour { }

    /// <summary>
    /// UI 根容器 —— 三层 UI 的总装与槽位化路由。移植自 stick-world <c>ui_root.gd</c>。
    ///
    /// 子节点结构（z 从低到高，见 <see cref="StickLayerOrder"/>）：
    ///   GlobalHUD / ModePanel / ContextPanel / ResourceBar / HudOverlay
    ///   ModalOverlay（Z_MODAL，模态遮罩盖住全部 UI） / SystemOverlay（Z_SYSTEM，toast/确认框）
    ///
    /// 【槽位化路由铁律】槽是布局唯一真相源：模块 UI 一律经 <see cref="AddToSlot"/>
    /// 注册进具名槽，由槽管理显隐与布局空间；**槽不存在时报错**（对齐 gd push_warning，
    /// Unity 侧升级为 LogError 以便在控制台直接定位），禁止散落 add_child。
    /// gd 的 zone 定位引擎（place_in_zone）与主题挂载（StickTheme）不在 P1 范围：
    /// zone 引擎待移植（注释占位），Unity 侧样式走 StickTokens/SketchSkin 令牌，
    /// 无"整树挂 Theme"概念。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class StickUIRoot : MonoBehaviour
    {
        // ---------------- 具名槽（与 gd ui_root.tscn 槽位表同名） ----------------

        public const string SlotGlobalHud = "GlobalHUD";
        public const string SlotModePanel = "ModePanel";
        public const string SlotContextPanel = "ContextPanel";
        public const string SlotResourceBar = "ResourceBar";
        public const string SlotHudOverlay = "HudOverlay";
        public const string SlotModalOverlay = "ModalOverlay";
        public const string SlotSystemOverlay = "SystemOverlay";

        /// <summary>槽名 → 层序键（gd LayerOrder.Z_*；HUD 组全为 Z_HUD=0，同值按声明序稳定排）。</summary>
        static readonly (string name, int z)[] Slots =
        {
            (SlotGlobalHud, StickLayerOrder.ZHud),
            (SlotModePanel, StickLayerOrder.ZHud),
            (SlotContextPanel, StickLayerOrder.ZHud),
            (SlotResourceBar, StickLayerOrder.ZHud),
            (SlotHudOverlay, StickLayerOrder.ZHud),
            (SlotModalOverlay, StickLayerOrder.ZModal),
            (SlotSystemOverlay, StickLayerOrder.ZSystem),
        };

        /// <summary>全局根引用（gd 侧 group "ui_root" 查找的等价出口；供 StickKit 系统层路由）。</summary>
        public static StickUIRoot Instance { get; private set; }

        /// <summary>统一模态栈（ESC/输入屏蔽/暂停的单一权威；gd _setup_modal_stack 装配）。</summary>
        public StickModalStack ModalStack { get; private set; }

        void Awake()
        {
            Instance = this;
            ApplySlotZOrders();
            ModalStack = new StickModalStack();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>槽位层序统一走 StickLayerOrder 常量（容器场景不写顺序，单一真相源）：
        /// 槽表按 Z 升序声明，逐个 SetSiblingIndex 落位即等价按 Z 排序，值大者后绘制（在上）。</summary>
        void ApplySlotZOrders()
        {
            int cursor = 0;
            foreach ((string name, int z) in Slots)
            {
                Transform slot = GetSlotOrNull(name);
                if (slot == null)
                    continue;   // 裸环境允许缺槽（对齐 gd get_node_or_null），路由时才报错
                slot.SetSiblingIndex(cursor++);
            }
        }

        // ---------------- 槽位化路由（公共 API） ----------------

        /// <summary>取具名槽（HudOverlay / ModePanel / ModalOverlay 等）。槽不存在时
        /// LogError 并返回 null——这是"槽位化路由"铁律的强制点，禁散落 add_child。</summary>
        public RectTransform GetSlot(string slotName)
        {
            Transform slot = GetSlotOrNull(slotName);
            if (slot == null)
            {
                Debug.LogError($"[StickUIRoot] 槽不存在: {slotName}（槽在根容器场景中声明，是布局唯一真相源）", this);
                return null;
            }
            return (RectTransform)slot;
        }

        Transform GetSlotOrNull(string slotName)
        {
            return transform.Find(slotName);
        }

        /// <summary>静默版槽查找（供 StickKit 系统层路由等"缺槽走回退"的场景；路由 API 仍走报错版）。</summary>
        public bool TryGetSlot(string slotName, out RectTransform slot)
        {
            if (GetSlotOrNull(slotName) is Transform found)
            {
                slot = (RectTransform)found;
                return true;
            }
            slot = null;
            return false;
        }

        /// <summary>挂到具名槽：模块 UI 通过此方法注册进槽，由槽管理显隐与布局空间。
        /// 子控件自身的 anchor 由它自己负责（全屏面板用 StickUIKit.FullRect，角落 HUD 自设锚点）。</summary>
        public bool AddToSlot(string slotName, Component node)
        {
            Transform slot = GetSlotOrNull(slotName);
            if (slot == null)
            {
                Debug.LogError($"[StickUIRoot] 槽不存在: {slotName}（拒绝散落挂载）", this);
                return false;
            }
            node.transform.SetParent(slot, false);
            return true;
        }

        // ---------------- 模式切换响应 ----------------

        /// <summary>切换模式面板（panel_type 语义同 gd UIAPI.PanelType；模式→面板映射由装配方负责）。
        /// 找 ModePanel 槽下全部 IStickModePanel 逐个 SwitchTo（gd 调 mode_panel.switch_to）。</summary>
        public void SwitchModePanel(int panelType)
        {
            Transform slot = GetSlotOrNull(SlotModePanel);
            if (slot == null)
                return;
            foreach (IStickModePanel panel in slot.GetComponentsInChildren<IStickModePanel>(true))
                panel.SwitchTo(panelType);
            // 战斗面板打开时清空上下文面板：gd 在 apply_mode_panel 内特判
            // PanelType.BATTLE；本层不依赖业务枚举，宿主在映射回调里自行调 ClearContext()。
        }

        // ---------------- 上下文面板 ----------------

        /// <summary>设置上下文面板内容（节点会 reparent 到 ContextPanel；先清动态内容）。</summary>
        public void SetContextContent(Component content)
        {
            RectTransform slot = GetSlotOrNull(SlotContextPanel) as RectTransform;
            if (slot == null)
                return;
            ClearContext(slot);
            if (content != null)
                content.transform.SetParent(slot, false);
        }

        /// <summary>清空上下文面板（只清运行时动态内容；挂 StickContextAnchor 的结构性槽跳过）。</summary>
        public void ClearContext()
        {
            Transform slot = GetSlotOrNull(SlotContextPanel);
            if (slot != null)
                ClearContext(slot);
        }

        static void ClearContext(Transform slot)
        {
            for (int i = slot.childCount - 1; i >= 0; i--)
            {
                Transform child = slot.GetChild(i);
                if (child.GetComponent<StickContextAnchor>() != null)
                    continue;   // 场景声明的结构性槽不属动态内容（gd owner != null 分支）
                Destroy(child.gameObject);
            }
        }

        // ---------------- 模态弹窗（gd open_modal / close_all_modals） ----------------

        /// <summary>打开模态弹窗：挂进 ModalOverlay 槽。入栈请走 <see cref="ModalStack"/>.Push
        ///（gd 侧 StickKit.confirm 即 push 到栈；open_modal 只负责挂载槽位）。</summary>
        public void OpenModal(Component modal)
        {
            RectTransform slot = GetSlotOrNull(SlotModalOverlay) as RectTransform;
            if (slot == null)
                return;
            modal.transform.SetParent(slot, false);
        }

        /// <summary>关闭所有模态弹窗（销毁 ModalOverlay 全部子项；不逐层 pop，对齐 gd queue_free）。</summary>
        public void CloseAllModals()
        {
            Transform slot = GetSlotOrNull(SlotModalOverlay);
            if (slot == null)
                return;
            for (int i = slot.childCount - 1; i >= 0; i--)
                Destroy(slot.GetChild(i).gameObject);
        }
    }
}
