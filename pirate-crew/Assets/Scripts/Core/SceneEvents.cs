namespace PirateCrew.Core
{
    /// <summary>
    /// 场景流转事件名（EventBus 键）——单一事实源（AGENTS 事件契约铁律：
    /// 代码侧用常量类承载，禁止散落魔法字符串；下游 UI/Campaign 一律引用此处）。
    /// 载荷与登记表见 docs/EventBus事件契约.md。
    /// </summary>
    public static class SceneEvents
    {
        /// <summary>请求切场景；载荷 = string 场景名，或 Dictionary&lt;string,object&gt;{path,transition}。</summary>
        public const string ChangeScene = "change_scene";

        /// <summary>请求返回上一场景（无载荷）。</summary>
        public const string GoBack = "go_back";

        /// <summary>开始加载（载荷 = string 目标场景名；当前零订阅方）。</summary>
        public const string SceneLoadStarted = "scene_load_started";

        /// <summary>场景已切换（载荷 = string 场景名；当前零订阅方）。</summary>
        public const string SceneLoadCompleted = "scene_load_completed";

        /// <summary>整段过渡（含淡入）结束（载荷 = string 场景名；当前零订阅方）。</summary>
        public const string SceneTransitionFinished = "scene_transition_finished";
    }
}
