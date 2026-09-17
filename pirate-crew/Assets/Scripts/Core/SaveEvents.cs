namespace PirateCrew.Core
{
    /// <summary>
    /// 存档事件名（EventBus 键）——单一事实源（发布方 <see cref="SaveManager"/>）。
    /// 载荷与登记表见 docs/EventBus事件契约.md。
    /// </summary>
    public static class SaveEvents
    {
        /// <summary>存档完成（载荷 = int 槽位；当前零订阅方）。</summary>
        public const string SaveCompleted = "save_completed";

        /// <summary>读档完成（载荷 = int 槽位；当前零订阅方）。</summary>
        public const string LoadCompleted = "load_completed";

        /// <summary>自动存档触发（载荷 = int 槽位；当前零订阅方）。</summary>
        public const string AutoSaveTriggered = "auto_save_triggered";
    }
}
