namespace PirateCrew.Core
{
    /// <summary>
    /// 场景流转事件名（EventBus 键）——单一事实源（AGENTS 事件契约铁律：
    /// 代码侧用常量类承载，禁止散落魔法字符串；下游 UI/Campaign 一律引用此处）。
    /// 载荷类型的权威登记在 <see cref="RegisterContracts"/>（进 <see cref="EventCatalog"/>），
    /// 人读的登记表见 docs/技术/架构/EventBus事件契约.md。
    /// </summary>
    public static class SceneEvents
    {
        /// <summary>请求切场景；载荷 = string 场景名（过渡动画按默认 true）。</summary>
        public const string ChangeScene = "change_scene";

        /// <summary>请求返回上一场景（无载荷）。</summary>
        public const string GoBack = "go_back";

        /// <summary>开始加载（载荷 = string 目标场景名；当前零订阅方）。</summary>
        public const string SceneLoadStarted = "scene_load_started";

        /// <summary>场景已切换（载荷 = string 场景名；当前零订阅方）。</summary>
        public const string SceneLoadCompleted = "scene_load_completed";

        /// <summary>整段过渡（含淡入）结束（载荷 = string 场景名；当前零订阅方）。</summary>
        public const string SceneTransitionFinished = "scene_transition_finished";

        /// <summary>把本类事件与期望载荷类型登记进 <see cref="EventCatalog"/>（由唯一入口调用）。</summary>
        [GameBootstrap(GameBootstrapPhase.Contracts, order: 10)]
        public static void RegisterContracts()
        {
            EventCatalog.Add<string>(ChangeScene);
            EventCatalog.AddNoPayload(GoBack);
            EventCatalog.Add<string>(SceneLoadStarted);
            EventCatalog.Add<string>(SceneLoadCompleted);
            EventCatalog.Add<string>(SceneTransitionFinished);
        }
    }
}
