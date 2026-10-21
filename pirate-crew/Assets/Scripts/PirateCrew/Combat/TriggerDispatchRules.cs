using PirateCrew.Data;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 触发<b>事件</b>的种类（武器系统重做 §3.3 六语义的事件侧枚举）。
    /// 与 <see cref="Data.WeaponTrigger"/>（武器数据侧的标志位集合）配对使用：
    /// 运行时发生某个事件 → 经 <see cref="TriggerDispatchRules"/> 问「这件武器的触发配置
    /// 是否被该事件命中」。
    /// </summary>
    public enum TriggerEventKind
    {
        /// <summary>无事件（哨兵）。</summary>
        None = 0,

        /// <summary>接触：碰到瓦片/放置物/敌人（§3.3「接触引爆」的事件侧）。</summary>
        Contact = 1,

        /// <summary>静止：速度满足「静止」判据（§3.3「静止引爆」；判据阈值属武器格定案项）。</summary>
        Rest = 2,

        /// <summary>点击：玩家或 AI 决策主动触发（§3.3「点击引爆」）。</summary>
        Click = 3,

        /// <summary>引信到点：感应条件成立并走完倒计时（§3.3「感应引信」）。</summary>
        FuseElapsed = 4,

        /// <summary>被爆波及：被任意爆炸命中（§3.3「被爆波及」，连锁的机制基础）。</summary>
        BlastHit = 5,

        /// <summary>放置完成：放置/使用即为全部行为（§3.3「放置即生效」）。</summary>
        Placed = 6,
    }

    /// <summary>
    /// 触发方式的结算分发骨架（纯 C# 静态，武器系统地基）：
    /// 给定<b>触发事件</b> → 该事件使命能的 <see cref="Data.WeaponTrigger"/> 标志位，
    /// 以及「武器的触发配置是否被该事件命中」的分发谓词。
    ///
    /// 【与现有两件的关系（不另写一套判定）】
    ///   · <see cref="Data.WeaponTrigger"/> 是唯一的数据结构（[Flags] 位集，本骨架直接复用）；
    ///   · <see cref="Battle.ProjectileTriggerRules"/> + <see cref="WeaponTriggerRules.ShouldDetonate"/>
    ///     判「一帧的上下文是否满足某个触发类」（现役引爆桥，逐帧轮询口径）；
    ///   · 本类判「离散事件命中哪些触发位」（逐格定案后的结算分发口径——事件源可以是
    ///     碰撞系统、玩家输入、引信计时器、爆炸波及扫描、放置流程）。
    /// 两者的位语义一致：本类只做位映射与位查询，不复制任何判定数学。
    /// </summary>
    public static class TriggerDispatchRules
    {
        /// <summary>
        /// 事件使能的触发位（事件 → 数据侧标志位的映射表，配表驱动测试逐行钉住）。
        /// <see cref="TriggerEventKind.None"/> 与 <see cref="Data.WeaponTrigger.Special"/>
        /// 不映射任何事件——Special 由武器专用机制处理，不经分发骨架。
        /// </summary>
        public static Data.WeaponTrigger FlagsFor(TriggerEventKind eventKind)
        {
            switch (eventKind)
            {
                case TriggerEventKind.Contact: return Data.WeaponTrigger.OnContact;
                case TriggerEventKind.Rest: return Data.WeaponTrigger.OnRest;
                case TriggerEventKind.Click: return Data.WeaponTrigger.OnClick;
                case TriggerEventKind.FuseElapsed: return Data.WeaponTrigger.ProximityFuse;
                case TriggerEventKind.BlastHit: return Data.WeaponTrigger.OnExplosionHit;
                case TriggerEventKind.Placed: return Data.WeaponTrigger.OnPlace;
                default: return Data.WeaponTrigger.None;
            }
        }

        /// <summary>
        /// 分发谓词：武器的触发配置（flags）是否被该事件触发——命中即武器应启动自身的
        /// 结算行为（引爆/放置生效/其他，行为本身属武器格实现，本骨架只回答「是否被触发」）。
        /// </summary>
        public static bool IsTriggeredBy(Data.WeaponTrigger triggerFlags, TriggerEventKind eventKind)
        {
            Data.WeaponTrigger eventFlags = FlagsFor(eventKind);
            return eventFlags != Data.WeaponTrigger.None
                   && (triggerFlags & eventFlags) != 0;
        }
    }
}
