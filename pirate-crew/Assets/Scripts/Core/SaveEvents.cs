namespace PirateCrew.Core
{
    /// <summary>
    /// 存档事件频道（EventBus 键）——单一事实源（发布方 <see cref="SaveManager"/>）。
    /// 人读的登记表见 docs/技术/架构/EventBus事件契约.md。
    /// </summary>
    public static class SaveEvents
    {
        /// <summary>存档完成（载荷 = int 槽位；当前零订阅方）。</summary>
        public static readonly Event<int> SaveCompleted = new();

        /// <summary>读档完成（载荷 = int 槽位；当前零订阅方）。</summary>
        public static readonly Event<int> LoadCompleted = new();

        /// <summary>自动存档触发（载荷 = int 槽位；当前零订阅方）。</summary>
        public static readonly Event<int> AutoSaveTriggered = new();
    }
}
