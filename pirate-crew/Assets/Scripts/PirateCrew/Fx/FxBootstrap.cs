using PirateCrew.Core;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 特效层的装配入口（组合根驱动，不是自己的隐式初始化）。
    ///
    /// 【解决什么问题】特效层需要在**任何入口**都在位：不管玩家从主菜单、选关界面还是
    /// 直接 Play Battle 场景进入，都得有一个常驻的 <see cref="FxRoot"/>
    /// （<c>DontDestroyOnLoad</c>）。它只对战斗事件响应，在菜单场景里静默、零开销。
    ///
    /// 【谁调用】唯一入口 <c>Core/GameEntryPoint</c>：本类用 <see cref="GameBootstrapAttribute"/>
    /// 声明接线入口 <see cref="Install"/>，由它统一调用。
    ///
    /// 【时机】只要**早于 <c>BattleController.Start</c>**（<c>battle_started</c> 在那里发布）即可。
    /// 现在统一在 <c>BeforeSceneLoad</c> 装配：<c>AddComponent</c> 会立刻执行 FxRoot.Awake 完成订阅，
    /// 比原来的 AfterSceneLoad 更早、同样不会漏掉第一局；后续场景切换时 FxRoot 因
    /// <c>DontDestroyOnLoad</c> 持续存活，订阅也不会丢。
    ///
    /// 【为什么不用 FindObjectOfType 判断"是不是战斗场景"】那样在"主菜单 → 战斗"的流程里
    /// 首次加载时找不到 BattleController 就永远不建根了（该回调每次播放只触发一次）。
    /// 常驻一个静默根更简单也更稳。
    /// </summary>
    internal static class FxBootstrap
    {
        /// <summary>创建常驻特效根（幂等；已存在则直接复用）。</summary>
        [GameBootstrap(GameBootstrapPhase.Initialize, order: 20)]
        internal static void Install()
        {
            // Ensure 内部有 Application.isPlaying 保护（编辑器里误调用不会往场景里塞对象）。
            // 万一因为时机不对而没建起来，这里必须吵闹——特效层缺席是"静默无效果"的典型症状。
            if (FxRoot.Ensure() == null)
                Log.Error("[FxBootstrap] 特效根 [FxRoot] 未建立（不在播放中？）——特效层将缺席。");
        }
    }
}
