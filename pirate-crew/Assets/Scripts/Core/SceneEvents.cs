namespace PirateCrew.Core
{
    /// <summary>
    /// 场景流转事件频道（EventBus 键）——单一事实源（AGENTS 事件契约铁律：
    /// 事件频道集中声明在 XxxEvents 类，禁止在调用点内联 new Event；下游 UI/Campaign 一律引用此处）。
    /// 人读的登记表见 docs/技术/架构/EventBus事件契约.md。
    /// </summary>
    public static class SceneEvents
    {
        /// <summary>请求切场景；载荷 = string 场景名（过渡动画按默认 true）。</summary>
        public static readonly Event<string> ChangeScene = new();

        /// <summary>请求返回上一场景（无载荷）。</summary>
        public static readonly Event GoBack = new();

        /// <summary>开始加载（载荷 = string 目标场景名；订阅方 = AudioService 清理底床/音乐）。</summary>
        public static readonly Event<string> SceneLoadStarted = new();

        /// <summary>场景已切换（载荷 = string 场景名；当前零订阅方）。</summary>
        public static readonly Event<string> SceneLoadCompleted = new();

        /// <summary>整段过渡（含淡入）结束（载荷 = string 场景名；当前零订阅方）。</summary>
        public static readonly Event<string> SceneTransitionFinished = new();
    }
}
