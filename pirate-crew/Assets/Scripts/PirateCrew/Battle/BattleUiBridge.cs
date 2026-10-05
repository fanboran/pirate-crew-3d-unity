using System;
using PirateCrew.Core;

namespace PirateCrew.Battle
{
    /// <summary>
    /// Esc 矩阵 **UI 层**的跨程序集门面（Battle 域 → UI 域的请求出口）。
    ///
    /// 【为什么需要它】程序集单向引用：`PirateCrew.UI` → `PirateCrew.Gameplay`，
    /// Battle 域的 <see cref="BattleInteractionController"/> 不能持有 <c>BattleHud</c>（反向依赖）。
    /// 而 Esc 矩阵（交互操作契约 §C）的三个 UI 层动作（关确认弹窗 / 恢复暂停 / 开暂停）
    /// 与一个判定输入（确认弹窗是否开着）必须跨这条边界——本类用**静态门面**承载：
    ///   · HUD 侧维护 <see cref="ConfirmDialogOpen"/> 真值并订阅三个请求事件；
    ///   · 控制器侧读标志、发请求，不认识任何 UI 类型。
    /// （先例：<see cref="BattlePause"/> 的静态状态位同款手法。）
    ///
    /// 【静态残留】关闭 Domain Reload 时跨播放存活：事件在 ResetStatics 清空、标志复位
    /// （由唯一入口 <c>Core/GameEntryPoint</c> 调度）；HUD 的订阅在其 OnEnable/OnDisable
    /// 成对增删，弹窗标志由 HUD 在开合时同步写。
    /// </summary>
    public static class BattleUiBridge
    {
        /// <summary>确认弹窗是否开着（HUD 侧写真值；Esc 矩阵 C1 的判定输入）。</summary>
        public static bool ConfirmDialogOpen { get; set; }

        /// <summary>开暂停菜单（Esc 矩阵 C5 / P 键；HUD 侧执行 OpenPause）。</summary>
        public static event Action OpenPauseRequested;

        /// <summary>恢复暂停（Esc 矩阵 C2 / P 键；HUD 侧执行 ClosePause）。</summary>
        public static event Action ResumePauseRequested;

        /// <summary>关闭确认弹窗（Esc 矩阵 C1；HUD 侧执行 CloseConfirmDialog）。</summary>
        public static event Action CloseDialogRequested;

        /// <summary>发"开暂停"请求（无订阅者时静默——HUD 缺席的降级路径）。</summary>
        public static void RequestOpenPause()
        {
            OpenPauseRequested?.Invoke();
        }

        /// <summary>发"恢复暂停"请求。</summary>
        public static void RequestResumePause()
        {
            ResumePauseRequested?.Invoke();
        }

        /// <summary>发"关确认弹窗"请求。</summary>
        public static void RequestCloseDialog()
        {
            CloseDialogRequested?.Invoke();
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态事件/标志跨播放存活，进入播放前强制复位
        /// （由唯一入口 <c>Core/GameEntryPoint</c> 调度；序号接在 BattlePause.ResetStatics 之后）。
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.ResetStatics, order: 33)]
        internal static void ResetStatics()
        {
            ConfirmDialogOpen = false;
            OpenPauseRequested = null;
            ResumePauseRequested = null;
            CloseDialogRequested = null;
        }
    }
}
