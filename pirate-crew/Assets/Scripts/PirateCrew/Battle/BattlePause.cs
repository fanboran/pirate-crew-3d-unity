using PirateCrew.Core;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗暂停状态（模块级静态开关）。
    ///
    /// 【为什么不只靠 <c>Time.timeScale = 0</c>】timeScale 能冻结物理/粒子/协程（缩放时间），
    /// 也顺带冻结挂在物理步上的计数——TurnManager 的 inactivity 帧数在 <c>FixedUpdate</c>
    /// 计数，timeScale=0 时物理步停摆、计数天然不走。但渲染帧的 <c>Update</c> 照跑
    /// （timeScale 不缩放 Update 的调用频率），里面的逻辑（输入/状态机/UI）仍会"暂停期间自己推进"。
    /// 所以暂停 = timeScale 归零（冻物理与 FixedUpdate 计数）+ 各 Update 入口查询
    /// <see cref="IsPaused"/>（冻逻辑）。
    ///
    /// 【谁查询】TurnManager / BattleCameraDriver（Update 入口）、
    /// BattleHud（面板）、BattleInteractionController（玩法输入入口自查 IsPaused）。
    ///
    /// 【静态残留】关闭 Domain Reload 时静态字段跨播放存活（与 CampaignApi 同一手法），
    /// 进入播放前强制复位；战斗重开/离场前也必须先 <see cref="Resume"/>。
    /// </summary>
    public static class BattlePause
    {
        /// <summary>当前是否暂停（true 时 timeScale 已被压到 0）。</summary>
        public static bool IsPaused { get; private set; }

        /// <summary>进入暂停：timeScale 压到 0。重复调用幂等。</summary>
        public static void Pause()
        {
            if (IsPaused)
                return;

            IsPaused = true;
            Time.timeScale = 0f;
        }

        /// <summary>解除暂停：timeScale 恢复 1。重复调用幂等。</summary>
        public static void Resume()
        {
            if (!IsPaused)
                return;

            IsPaused = false;
            Time.timeScale = 1f;
        }

        /// <summary>场景重载/进播放前的强制复位（无论当前状态，timeScale 一律归 1）。</summary>
        public static void ForceResume()
        {
            IsPaused = false;
            Time.timeScale = 1f;
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态字段跨播放存活，进入播放前强制复位
        /// （由唯一入口 <c>Core/GameEntryPoint</c> 调用。）
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.ResetStatics, order: 32)]
        internal static void ResetStatics()
        {
            ForceResume();
        }
    }
}
