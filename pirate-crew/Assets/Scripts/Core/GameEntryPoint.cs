using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 全工程**唯一**的 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 入口。
    ///
    /// 【它解决什么问题】重构前有 14 处隐式初始化分散在 5 个程序集里（静态重置、服务自建、
    /// 工具装配各自为政），初始化顺序"靠执行时机"而不是靠代码——出问题时既不好推理也不好测试。
    /// 现在进程启动期的动作只有这一处，它按固定顺序做四件事：
    ///   ① <b>清空静态状态</b>（事件总线订阅表 / 契约违规记录 / 事件登记表 / 服务注册表 /
    ///      命令行快照，再跑一遍各模块的 <see cref="GameBootstrapPhase.ResetStatics"/>）
    ///   ② <b>解析命令行</b>（<see cref="CommandLineOptions.Parse"/>，全程只解析这一次）
    ///   ③ <b>登记事件契约</b>（<see cref="GameBootstrapPhase.Contracts"/>）
    ///   ④ <b>保证服务宿主存在</b>（<see cref="Bootstrapper.EnsureInstalled"/>，
    ///      于是从**任意场景**直接按 Play 也能跑）+ 跑各模块的
    ///      <see cref="GameBootstrapPhase.Initialize"/> 接线（服务创建、事件订阅、工具模式装配）
    ///
    /// 【为什么选 BeforeSceneLoad（论证）】
    ///   · <c>SubsystemRegistration</c> 发生在程序集加载**之前/之中**，而本入口要做的事里
    ///     「发现各模块的接线入口」「命令行工具装配」都需要目标程序集已经加载完毕——
    ///     放在 <c>SubsystemRegistration</c> 会漏掉尚未加载的程序集；
    ///   · <c>AfterSceneLoad</c> 太晚：首个场景的 <c>Awake</c> 已经跑过，而"从任意场景按 Play
    ///     也能拿到服务"这条要求必须早于场景里的消费者 Awake（否则第一个 Awake 里查服务就是空）；
    ///   · <c>BeforeSceneLoad</c> 恰好落在"程序集已全部加载（AfterAssembliesLoaded 之后、
    ///     首个场景 Awake 之前）"这段窗口里，两种情况都满足。
    ///
    /// 【为什么三个阶段都在同一个回调里】三个 <see cref="GameBootstrapPhase"/> 是**工作分类**
    /// （重置 / 登记 / 初始化），不是三个引擎时机——拆成多个 <c>[RuntimeInitializeOnLoadMethod]</c>
    /// 就把刚收掉的"时机耦合"又请回来了。同一个回调里按枚举顺序跑完，顺序由枚举 + Order 决定。
    ///
    /// 【只跑一次】Unity 的 <c>BeforeSceneLoad</c> 回调在每次播放/启动只触发一次
    /// （首个场景加载前），后续场景切换不会重入——所以本入口不加"已跑过"标志：
    /// 那类标志本身会跨播放残留（关闭 Domain Reload 时），反而制造"第二次播放什么也没装配"的事故。
    ///
    /// 【测试】测试**不要**调用本类：它碰 Player 生命周期与真实 argv。
    /// 需要单测的部件各自独立可测（<see cref="EventBus"/> / <see cref="Services"/> /
    /// <see cref="EventCatalog"/> / <see cref="CommandLineOptions"/> / <see cref="GameBootstrap"/>）。
    /// </summary>
    public static class GameEntryPoint
    {
        /// <summary>唯一入口（进入播放前，首个场景加载之前）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Enter()
        {
            ResetStatics();
            CommandLineOptions.Parse();
            int contracts = GameBootstrap.RunPhase(GameBootstrapPhase.Contracts);
            bool hostCreated = Bootstrapper.EnsureInstalled();
            int initialized = GameBootstrap.RunPhase(GameBootstrapPhase.Initialize);

            Log.Info("[GameEntryPoint] 启动装配完成：契约 " + contracts + " 项、接线 " + initialized
                     + " 项" + (hostCreated ? "、新建服务宿主" : "、服务宿主已在位") + "。");
        }

        /// <summary>
        /// 清空静态残留（顺序：Core 自己的状态 → 各模块的 <see cref="GameBootstrapPhase.ResetStatics"/>）。
        /// 关闭 Domain Reload 时静态字段跨播放存活，这一步是"上一局的残留不带进这一局"的唯一保险。
        /// </summary>
        static void ResetStatics()
        {
            EventBus.ResetForNewSession();
            EventCatalog.Clear();
            Services.Clear();
            CommandLineOptions.Reset();

            GameBootstrap.RunPhase(GameBootstrapPhase.ResetStatics);
        }
    }
}
