namespace PirateCrew.Core
{
    /// <summary>
    /// 存档事件名（EventBus 键）——单一事实源（发布方 <see cref="SaveManager"/>）。
    /// 载荷类型的权威登记在 <see cref="RegisterContracts"/>（进 <see cref="EventCatalog"/>），
    /// 人读的登记表见 docs/技术/架构/EventBus事件契约.md。
    /// </summary>
    public static class SaveEvents
    {
        /// <summary>存档完成（载荷 = int 槽位；当前零订阅方）。</summary>
        public const string SaveCompleted = "save_completed";

        /// <summary>读档完成（载荷 = int 槽位；当前零订阅方）。</summary>
        public const string LoadCompleted = "load_completed";

        /// <summary>自动存档触发（载荷 = int 槽位；当前零订阅方）。</summary>
        public const string AutoSaveTriggered = "auto_save_triggered";

        /// <summary>把本类事件与期望载荷类型登记进 <see cref="EventCatalog"/>（由唯一入口调用）。</summary>
        [GameBootstrap(GameBootstrapPhase.Contracts, order: 11)]
        public static void RegisterContracts()
        {
            EventCatalog.Add<int>(SaveCompleted);
            EventCatalog.Add<int>(LoadCompleted);
            EventCatalog.Add<int>(AutoSaveTriggered);
        }
    }
}
