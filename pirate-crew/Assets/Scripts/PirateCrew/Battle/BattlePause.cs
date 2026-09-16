using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 战斗暂停状态（模块级静态开关）。
    ///
    /// 【为什么不只靠 <c>Time.timeScale = 0</c>】timeScale 能冻结物理/粒子/协程（缩放时间），
    /// 但冻结不了 <c>Update</c> 里的**逐帧计数**——TurnManager 的 inactivity 帧数、AiController 的
    /// 看门狗与延迟计数都是纯 Update 计数，暂停时照走就会"暂停期间回合自己推进"。
    /// 所以暂停 = timeScale 归零（冻物理）+ 各 Update 入口查询 <see cref="IsPaused"/>（冻逻辑）。
    ///
    /// 【谁查询】TurnManager / AiController / BattleCameraController（Update 入口）、
    /// BattleHud（输入与面板）、AimThrowController 经由 BattleHud 关掉 InputEnabled。
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode()
        {
            ForceResume();
        }
    }
}
