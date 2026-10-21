using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;
using Trigger = PirateCrew.Data.WeaponTrigger;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// 触发方式结算分发骨架的配表驱动测试：事件 → 触发位的映射逐行钉住，
    /// 分发谓词覆盖单触发/组合触发/未配置位/None/Special 的行为。
    /// </summary>
    [TestFixture]
    public class TriggerDispatchRulesTests
    {
        // ------------------------------------------------------------------
        // 配表：事件 → 使能的触发位（§3.3 六语义逐行）
        // ------------------------------------------------------------------

        static readonly (TriggerEventKind Event, Trigger ExpectedFlags)[] EventFlagTable =
        {
            (TriggerEventKind.Contact, Trigger.OnContact),
            (TriggerEventKind.Rest, Trigger.OnRest),
            (TriggerEventKind.Click, Trigger.OnClick),
            (TriggerEventKind.FuseElapsed, Trigger.ProximityFuse),
            (TriggerEventKind.BlastHit, Trigger.OnExplosionHit),
            (TriggerEventKind.Placed, Trigger.OnPlace),
            (TriggerEventKind.None, Trigger.None),
        };

        [Test]
        public void FlagsFor_MatchesTablePerEvent()
        {
            foreach ((TriggerEventKind eventKind, Trigger expected) in EventFlagTable)
                Assert.AreEqual(expected, TriggerDispatchRules.FlagsFor(eventKind), eventKind.ToString());
        }

        [Test]
        public void FlagsFor_EachEventMapsExactlyOneBit()
        {
            // 事件侧与数据侧一一对应：非 None 事件都恰好命中一个位（不串位）。
            foreach ((TriggerEventKind eventKind, Trigger flags) in EventFlagTable)
            {
                if (eventKind == TriggerEventKind.None)
                    continue;
                long bits = (long)flags;
                Assert.IsTrue(bits != 0 && (bits & (bits - 1)) == 0,
                    eventKind + " 应恰好映射一个标志位，实际 " + flags);
            }
        }

        // ------------------------------------------------------------------
        // 分发谓词：单触发 / 组合触发 / 未配置 / None / Special
        // ------------------------------------------------------------------

        [Test]
        public void IsTriggeredBy_SingleFlagWeapon_OnlyMatchingEventTriggers()
        {
            // banana 型组合：OnRest | OnClick（§3.3「一件武器可组合多种触发」）。
            Trigger bananaLike = Trigger.OnRest | Trigger.OnClick;
            Assert.IsTrue(TriggerDispatchRules.IsTriggeredBy(bananaLike, TriggerEventKind.Rest));
            Assert.IsTrue(TriggerDispatchRules.IsTriggeredBy(bananaLike, TriggerEventKind.Click));
            Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(bananaLike, TriggerEventKind.Contact));
            Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(bananaLike, TriggerEventKind.FuseElapsed));
            Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(bananaLike, TriggerEventKind.BlastHit));
            Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(bananaLike, TriggerEventKind.Placed));
        }

        [Test]
        public void IsTriggeredBy_CombinedWeapon_TriggeredByAnyMappedEvent()
        {
            // gunpowderBarrel 型组合：OnPlace | OnExplosionHit（放置生效 + 被爆波及连锁）。
            Trigger barrelLike = Trigger.OnPlace | Trigger.OnExplosionHit;
            Assert.IsTrue(TriggerDispatchRules.IsTriggeredBy(barrelLike, TriggerEventKind.Placed));
            Assert.IsTrue(TriggerDispatchRules.IsTriggeredBy(barrelLike, TriggerEventKind.BlastHit));
            Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(barrelLike, TriggerEventKind.Contact));
        }

        [Test]
        public void IsTriggeredBy_NoneEvent_NeverTriggers()
        {
            // 全配置武器 + None 事件 → 不触发（事件侧哨兵不产生分发）。
            Trigger everything = Trigger.OnContact | Trigger.OnRest | Trigger.OnClick
                                 | Trigger.ProximityFuse | Trigger.OnExplosionHit | Trigger.OnPlace;
            Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(everything, TriggerEventKind.None));
        }

        [Test]
        public void IsTriggeredBy_UnconfiguredFlags_NeverTriggers()
        {
            foreach ((TriggerEventKind eventKind, Trigger _) in EventFlagTable)
                Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(Trigger.None, eventKind),
                    eventKind.ToString());
        }

        [Test]
        public void IsTriggeredBy_SpecialFlag_IsNeverDispatched()
        {
            // Special 由武器专用机制处理，不经事件分发——任何事件都不命中它。
            Trigger specialOnly = Trigger.Special;
            foreach ((TriggerEventKind eventKind, Trigger _) in EventFlagTable)
                Assert.IsFalse(TriggerDispatchRules.IsTriggeredBy(specialOnly, eventKind),
                    eventKind.ToString());
        }

        // ------------------------------------------------------------------
        // 与目录表的兼容：现役 WeaponCatalog 的触发档位都能被正确分发
        // ------------------------------------------------------------------

        [Test]
        public void CatalogTriggers_AreDispatchableByAtLeastOneEvent()
        {
            // 除 Special 系外，目录里每个触发档位至少被一个事件命中（骨架与数据结构对账，
            // 不对具体武器定行为）。SweepingFlame = Special 是唯一例外（专用机制）。
            var covered = new HashSet<TriggerEventKind>();
            foreach ((TriggerEventKind eventKind, Trigger _) in EventFlagTable)
                covered.Add(eventKind);

            foreach (WeaponStats stats in WeaponCatalog.All)
            {
                bool anyHit = false;
                foreach (TriggerEventKind eventKind in covered)
                {
                    if (TriggerDispatchRules.IsTriggeredBy(stats.Trigger, eventKind))
                    {
                        anyHit = true;
                        break;
                    }
                }

                if (stats.Trigger == Trigger.Special)
                    Assert.IsFalse(anyHit, stats.Id + "（Special）不应被事件分发命中");
                else
                    Assert.IsTrue(anyHit, stats.Id + " 的触发档位至少应被一个事件命中");
            }
        }
    }
}
