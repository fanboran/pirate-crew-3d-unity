using PirateCrew.Combat;
using PirateCrew.Data;
using DataTrigger = PirateCrew.Data.WeaponTrigger;
using CombatTrigger = PirateCrew.Combat.WeaponTrigger;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 武器引爆判定的桥接（纯 C#）。
    ///
    /// 【为什么要桥接】数值层 <see cref="WeaponStats.Trigger"/> 用 <see cref="DataTrigger"/>
    /// （[Flags]，一件武器可并列多个触发条件，如 banana = OnRest | OnClick）；
    /// 而 §5.2 的判定规则实现（已有 86 条测试）在 <see cref="WeaponTriggerRules.ShouldDetonate"/>，
    /// 它只接受单个 <see cref="CombatTrigger"/>。本类负责把 flags 拆成五个单值触发类，
    /// 逐个复用 <see cref="WeaponTriggerRules.ShouldDetonate"/>（<b>不另写一套判定</b>），
    /// 任一为真即引爆。
    ///
    /// 【覆盖 §5.2 五类触发】
    ///   接触即爆   DataTrigger.OnContact      → CombatTrigger.Contact
    ///   静止引爆   DataTrigger.OnRest         → CombatTrigger.AtRest
    ///   点击引爆   DataTrigger.OnClick        → CombatTrigger.Click
    ///   邻近引信   DataTrigger.ProximityFuse  → CombatTrigger.ProximityFuse
    ///   被爆炸命中 DataTrigger.OnExplosionHit → CombatTrigger.BlastContact
    ///   OnPlace / Special / None 不产生主动引爆（放置与专用机制各自处理）。
    ///
    /// 【出处】静态逆向文档 §5.2「触发/引爆条件」列。
    /// </summary>
    public static class ProjectileTriggerRules
    {
        /// <summary>本帧是否应引爆（对 flags 里每个触发类取 OR）。</summary>
        public static bool ShouldDetonateThisFrame(DataTrigger trigger, in TriggerContext context)
        {
            if (Has(trigger, DataTrigger.OnContact)
                && WeaponTriggerRules.ShouldDetonate(CombatTrigger.Contact, context))
                return true;

            if (Has(trigger, DataTrigger.OnRest)
                && WeaponTriggerRules.ShouldDetonate(CombatTrigger.AtRest, context))
                return true;

            if (Has(trigger, DataTrigger.OnClick)
                && WeaponTriggerRules.ShouldDetonate(CombatTrigger.Click, context))
                return true;

            if (Has(trigger, DataTrigger.ProximityFuse)
                && WeaponTriggerRules.ShouldDetonate(CombatTrigger.ProximityFuse, context))
                return true;

            if (Has(trigger, DataTrigger.OnExplosionHit)
                && WeaponTriggerRules.ShouldDetonate(CombatTrigger.BlastContact, context))
                return true;

            return false;
        }

        /// <summary>是否具备「被爆炸命中即爆」的连锁触发（火药桶）。</summary>
        public static bool CanChainFromBlast(DataTrigger trigger)
        {
            return Has(trigger, DataTrigger.OnExplosionHit);
        }

        /// <summary>是否具备接触触发（撞瓦片/箱/敌人即判定）。</summary>
        public static bool HasContactTrigger(DataTrigger trigger)
        {
            return Has(trigger, DataTrigger.OnContact);
        }

        /// <summary>是否具备点击触发（banana 的玩家点击引爆）。</summary>
        public static bool HasClickTrigger(DataTrigger trigger)
        {
            return Has(trigger, DataTrigger.OnClick);
        }

        /// <summary>是否具备邻近引信（mine）。</summary>
        public static bool HasProximityFuse(DataTrigger trigger)
        {
            return Has(trigger, DataTrigger.ProximityFuse);
        }

        /// <summary>是否具备静止触发（dynamite / banana）。</summary>
        public static bool HasRestTrigger(DataTrigger trigger)
        {
            return Has(trigger, DataTrigger.OnRest);
        }

        static bool Has(DataTrigger value, DataTrigger flag)
        {
            return (value & flag) != 0;
        }
    }
}
