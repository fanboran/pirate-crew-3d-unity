using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 特效层的自动装配入口。
    ///
    /// 【解决什么问题】战斗场景 / 预制体不在本 agent 的白名单内，无法用场景装配把
    /// <see cref="FxRoot"/> 挂进 Battle.unity。这里用 Unity 官方的
    /// <c>RuntimeInitializeOnLoadMethod</c> 在**进入播放时**自动创建一次根对象
    /// （<c>DontDestroyOnLoad</c>），于是：
    ///   · 不改任何场景 / Prefab / 既有脚本；
    ///   · 不管玩家从主菜单、选关界面还是直接 Play Battle 场景进入，特效层都在位；
    ///   · 它只对战斗事件响应，在菜单场景里处于静默状态（不产生任何开销）。
    ///
    /// 【时机选择 AfterSceneLoad】此时首个场景的所有 <c>Awake</c> 已执行、<c>Start</c> 尚未执行；
    /// 而 <c>battle_started</c> 是在 <c>BattleController.Start</c> 里发布的，因此 FxRoot 一定
    /// 先订阅到、不会漏掉第一局。后续场景切换时 FxRoot 因 <c>DontDestroyOnLoad</c> 持续存活，
    /// 订阅也不会丢。
    ///
    /// 【为什么不用 FindObjectOfType 判断"是不是战斗场景"】那样在"主菜单 → 战斗"的流程里
    /// 首次加载时找不到 BattleController 就永远不建根了（该回调每次播放只触发一次）。
    /// 常驻一个静默根更简单也更稳。
    /// </summary>
    internal static class FxBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            FxRoot.Ensure();
        }
    }
}
