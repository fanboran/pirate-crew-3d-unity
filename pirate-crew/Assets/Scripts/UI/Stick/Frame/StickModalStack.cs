using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>可入模态栈的屏幕契约（对齐 gd 侧 duck-typing 的 has_method("open"/"close")）。
    /// 由 <see cref="StickScreen"/>（模态弹窗）、<see cref="StickConfirmDialog"/> 实现；
    /// <see cref="StickWindow"/> 不入栈但也实现（供宿主统一开关）。</summary>
    public interface IStickScreen
    {
        /// <summary>打开（模态类实现内部自带"打开即请求暂停"簿记）。</summary>
        void Open();

        /// <summary>关闭（确认框实现为关闭并销毁；栈 pop 时经此退栈）。</summary>
        void Close();

        /// <summary>当前是否打开（gd visible 的等价读法）。</summary>
        bool IsOpen { get; }
    }

    /// <summary>模态层键（gd UIModalStack.Layer，同名同序 1:1；数值越大越靠上，即 ESC 退栈顺序）。</summary>
    public enum StickModalLayer
    {
        /// <summary>0 最底：暂停菜单（ESC 空栈时的默认目标）。</summary>
        PauseMenu = 0,

        /// <summary>1 设置。</summary>
        Settings = 1,

        /// <summary>2 存档管理。</summary>
        SavePanel = 2,

        /// <summary>3 功能大界面空面板（K/O/J/L，同类单例，替换不叠加）。</summary>
        EmpirePanel = 3,

        /// <summary>4 背包（E 键；确认框需盖在其上）。</summary>
        Inventory = 4,

        /// <summary>5 角色属性面板（C 键）。</summary>
        Stats = 5,

        /// <summary>6 最顶：确认框（SystemOverlay，ESC = 取消）。</summary>
        Confirm = 6,
    }

    /// <summary>
    /// 统一模态栈 —— 层键字典 + 逐层 pop（纯 C# 类，不依赖 MonoBehaviour 生命周期）。
    /// 移植自 stick-world <c>ui_modal_stack.gd</c>，语义逐条对齐：
    ///   - 按层索引字典：每层同时只挂一个激活对象（同类单例）
    ///   - 压栈而非互斥：设置上可再开确认框；ESC 逐层退栈（pop 栈顶）
    ///   - 输入屏蔽随栈统一：首层入栈自动暂停、栈空恢复；压上层自动盖住下层
    ///     （防双重遮罩，揭示时恢复可见）
    ///
    /// 【暂停语义】gd 侧直接"拉引擎暂停总闸"（TimeManager.set_speed(PAUSED)，
    /// 记录/恢复原速度）；Unity 侧本类不碰 <c>Time.timeScale</c>——以
    /// <see cref="PauseRequested"/>（true = 首层入栈，false = 栈空恢复）暴露，
    /// 接暂停是宿主的事（宿主自判当前是否已被别的系统暂停，对齐 gd 的
    /// is_paused 接管判断）。非模态窗口（StickWindow Floating/Dock/Popover）
    /// 不入栈，ESC 自关。
    /// </summary>
    public sealed class StickModalStack
    {
        /// <summary>无层（gd UIModalStack.NONE）。</summary>
        public const StickModalLayer None = (StickModalLayer)(-1);

        /// <summary>层键 → 激活对象（每层同时只挂一个 IStickScreen）。</summary>
        readonly Dictionary<StickModalLayer, IStickScreen> _layers = new();

        /// <summary>被本栈盖住的层（仍在栈中）——覆盖时置 true，揭示时清除。</summary>
        readonly Dictionary<IStickScreen, bool> _covered = new();

        /// <summary>
        /// 暂停请求：首层入栈发 true（对齐 gd「拉引擎暂停总闸」），栈空恢复发 false。
        /// 宿主订阅后在回调里挂 <c>Time.timeScale</c> / 输入屏蔽；本栈自身不碰引擎。
        /// </summary>
        public event Action<bool> PauseRequested;

        bool _pauseHeld;

        // ---------------- 压栈 ----------------

        /// <summary>把模态压入指定层。同一实例重复 push = 提到栈顶；同层被不同实例占用 = 替换（同类单例）。</summary>
        public void Push(IStickScreen screen, StickModalLayer layer)
        {
            if (screen == null)
                return;
            if (_layers.TryGetValue(layer, out IStickScreen existing))
            {
                if (ReferenceEquals(existing, screen))
                {
                    RaiseToTop(layer);
                    return;
                }
                // 同层不同实例：先退旧再压新（不 reveal/restore，避免同帧闪烁）
                _layers.Remove(layer);
                _covered.Remove(existing);
                existing.Close();
            }
            // 盖住比 layer 低的全部已开层（防双重遮罩；仍在栈中，pop 上层后揭示）
            foreach (KeyValuePair<StickModalLayer, IStickScreen> kv in _layers)
            {
                if (kv.Key < layer)
                {
                    _covered[kv.Value] = true;
                    SetVisible(kv.Value, false);
                }
            }
            _layers[layer] = screen;
            PauseIfNeeded();
            screen.Open();
        }

        /// <summary>同类单例：把 layer 提到栈顶（清掉叠在上面的层；若被盖住则揭示）。</summary>
        public void RaiseToTop(StickModalLayer layer)
        {
            if (!_layers.ContainsKey(layer))
                return;
            StickModalLayer t = TopLayer();
            while (t != None && t > layer)
            {
                Pop(t);
                t = TopLayer();
            }
            if (_layers.TryGetValue(layer, out IStickScreen s) && _covered.ContainsKey(s))
            {
                _covered.Remove(s);
                SetVisible(s, true);
            }
        }

        // ---------------- 退栈 ----------------

        /// <summary>关闭并移除指定层（close 由层自身负责）。</summary>
        public void Pop(StickModalLayer layer)
        {
            if (!_layers.TryGetValue(layer, out IStickScreen screen))
                return;
            _layers.Remove(layer);
            _covered.Remove(screen);
            screen.Close();
            RevealTop();
            RestoreIfEmpty();
        }

        /// <summary>从栈顶逐层 pop 到指定层（inclusive=true 时连该层一起退）。</summary>
        public void PopUntil(StickModalLayer layer, bool inclusive = false)
        {
            StickModalLayer t = TopLayer();
            while (t != None)
            {
                if (t < layer || (t == layer && !inclusive))
                    break;
                Pop(t);
                t = TopLayer();
            }
        }

        /// <summary>清空整个栈。</summary>
        public void Clear()
        {
            PopUntil(None, true);
        }

        /// <summary>ESC 统一退栈：有模态 → pop 栈顶并消费（返回 true）；无模态 → 返回 false（调用方决定开暂停菜单）。</summary>
        public bool HandleEscape()
        {
            if (!IsAnyOpen)
                return false;
            Pop(TopLayer());
            return true;
        }

        // ---------------- 查询 ----------------

        /// <summary>当前栈顶层键（无则 <see cref="None"/>）。顺带清理失效引用（场景切换后旧实例已销毁）。</summary>
        public StickModalLayer TopLayer()
        {
            StickModalLayer top = None;
            List<StickModalLayer> dead = null;
            foreach (KeyValuePair<StickModalLayer, IStickScreen> kv in _layers)
            {
                if (IsDestroyed(kv.Value))
                {
                    (dead ??= new List<StickModalLayer>()).Add(kv.Key);
                    continue;
                }
                if (kv.Key > top)
                    top = kv.Key;
            }
            if (dead != null)
                foreach (StickModalLayer key in dead)
                {
                    _covered.Remove(_layers[key]);
                    _layers.Remove(key);
                }
            return top;
        }

        /// <summary>当前栈顶对象（无则 null）。</summary>
        public IStickScreen Top()
        {
            StickModalLayer t = TopLayer();
            return t != None && _layers.TryGetValue(t, out IStickScreen s) ? s : null;
        }

        public bool IsAnyOpen
        {
            get
            {
                TopLayer();   // 顺带清理失效引用
                return _layers.Count > 0;
            }
        }

        public bool IsOpen(StickModalLayer layer)
        {
            return _layers.ContainsKey(layer);
        }

        public IStickScreen GetEntry(StickModalLayer layer)
        {
            return _layers.TryGetValue(layer, out IStickScreen s) && !IsDestroyed(s) ? s : null;
        }

        // ---------------- 暂停随栈（宿主接线） ----------------

        /// <summary>首个模态入栈时请求暂停；栈空时请求恢复。暂停是否生效由宿主决定（对齐 gd 侧 is_paused 接管判断）。</summary>
        void PauseIfNeeded()
        {
            if (_pauseHeld)
                return;
            _pauseHeld = true;
            PauseRequested?.Invoke(true);
        }

        void RestoreIfEmpty()
        {
            if (_layers.Count > 0 || !_pauseHeld)
                return;
            _pauseHeld = false;
            PauseRequested?.Invoke(false);
        }

        // ---------------- 面板主动关闭的同步出口 ----------------

        /// <summary>
        /// 面板自身按钮 close（如设置"关闭"）→ 本栈同步退栈，保证栈状态与真实可见性一致。
        /// gd 侧靠 visibility_changed 信号自动同步；Unity 无对应生命周期事件，
        /// 由屏幕实现在自关时调用本方法（被栈盖住的层除外，对齐 gd _covered 分支）。
        /// </summary>
        public void NotifyClosed(IStickScreen screen)
        {
            StickModalLayer layer = FindLayer(screen);
            if (layer == None)
                return;
            _layers.Remove(layer);
            _covered.Remove(screen);
            RevealTop();
            RestoreIfEmpty();
        }

        StickModalLayer FindLayer(IStickScreen screen)
        {
            foreach (KeyValuePair<StickModalLayer, IStickScreen> kv in _layers)
                if (ReferenceEquals(kv.Value, screen))
                    return kv.Key;
            return None;
        }

        /// <summary>揭示当前栈顶（若被盖住）。</summary>
        void RevealTop()
        {
            StickModalLayer t = TopLayer();
            if (t == None)
                return;
            if (_layers.TryGetValue(t, out IStickScreen s) && _covered.ContainsKey(s))
            {
                _covered.Remove(s);
                SetVisible(s, true);
            }
        }

        // ---------------- 引擎侧辅助 ----------------

        /// <summary>盖层/揭示统一走 SetActive(false/true)（gd visible 的 UGUI 等价）。</summary>
        static void SetVisible(IStickScreen screen, bool visible)
        {
            if (screen is MonoBehaviour mb && mb != null)
                mb.gameObject.SetActive(visible);
        }

        static bool IsDestroyed(IStickScreen screen)
        {
            return screen is MonoBehaviour mb && mb == null;   // Unity 重载 ==：销毁后为 true
        }
    }
}
