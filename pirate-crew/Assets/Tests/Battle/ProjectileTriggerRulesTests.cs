using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using DataTrigger = PirateCrew.PirateCrew.Data.WeaponTrigger;
using CombatTrigger = PirateCrew.PirateCrew.Combat.WeaponTrigger;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileTriggerRules"/> 引爆判定测试（§5.2 五类触发）。
    /// 说明：桥接层复用 <see cref="WeaponTriggerRules.ShouldDetonate"/>，本测试同时锁定
    /// 「flags → 单值触发类」的映射正确性与 OR 语义。
    /// </summary>
    [TestFixture]
    public class ProjectileTriggerRulesTests
    {
        static DataTrigger Trigger(WeaponId id) => WeaponCatalog.Get(id).Trigger;

        static TriggerContext Ctx(
            bool contact = false, bool clicked = false, bool blast = false,
            float vx = 0f, float vy = 0f, bool fuseArmed = false, int fuseRemaining = 0)
        {
            return new TriggerContext(contact, clicked, blast, vx, vy, fuseArmed, fuseRemaining);
        }

        // ------------------------------------------------------------------
        // 接触即爆：cannonball / cherryBomb / parachuteBomb / rumBottle / piecesOfEight
        // ------------------------------------------------------------------

        [Test]
        public void Contact_DetonatesOnContact()
        {
            DataTrigger t = Trigger(WeaponId.CherryBomb);
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: false)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(clicked: true, blast: true)));
        }

        [TestCase(WeaponId.Cannonball)]
        [TestCase(WeaponId.ParachuteBomb)]
        [TestCase(WeaponId.RumBottle)]
        [TestCase(WeaponId.PiecesOfEight)]
        public void ContactWeapons_DetonateOnContact(WeaponId id)
        {
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(Trigger(id), Ctx(contact: true)));
        }

        // ------------------------------------------------------------------
        // 静止引爆：dynamite（vx==0 且 |vy|<0.2）
        // ------------------------------------------------------------------

        [Test]
        public void Dynamite_DetonatesOnlyAtRest()
        {
            DataTrigger t = Trigger(WeaponId.Dynamite);
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0f, vy: 0.1f)));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0f, vy: -0.19f)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0f, vy: 0.5f)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0.1f, vy: 0f)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true, vx: 5f, vy: 5f)));
        }

        // ------------------------------------------------------------------
        // 点击引爆：banana（OnRest | OnClick）
        // ------------------------------------------------------------------

        [Test]
        public void Banana_DetonatesOnClickOrRest()
        {
            DataTrigger t = Trigger(WeaponId.Banana);
            Assert.IsTrue(ProjectileTriggerRules.HasClickTrigger(t));
            Assert.IsTrue(ProjectileTriggerRules.HasRestTrigger(t));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(clicked: true)));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0f, vy: 0.1f)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 1f, vy: 1f)));
        }

        // ------------------------------------------------------------------
        // 邻近引信：mine（60px 内有角色且移动 → 60 帧）
        // ------------------------------------------------------------------

        [Test]
        public void Mine_DetonatesOnlyWhenFuseExpires()
        {
            DataTrigger t = Trigger(WeaponId.Mine);
            Assert.IsTrue(ProjectileTriggerRules.HasProximityFuse(t));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(fuseArmed: false, fuseRemaining: 0)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(fuseArmed: true, fuseRemaining: 1)));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(fuseArmed: true, fuseRemaining: 0)));
            // mine 对接触不敏感（接触不引爆）
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true)));
        }

        [Test]
        public void Mine_FuseArmsOnlyForMovingCharacterWithin60px()
        {
            // 复用 WeaponTriggerRules.ShouldStartFuse：点燃条件
            Assert.IsTrue(WeaponTriggerRules.ShouldStartFuse(CombatTrigger.ProximityFuse, 60f, true));
            Assert.IsFalse(WeaponTriggerRules.ShouldStartFuse(CombatTrigger.ProximityFuse, 60f, false));
            Assert.IsFalse(WeaponTriggerRules.ShouldStartFuse(CombatTrigger.ProximityFuse, 60.1f, true));
        }

        [Test]
        public void Mine_FuseCountsDownFrom60ToDetonation()
        {
            // 手算：60 → TickFuse 逐帧 -1；第 60 帧剩余 0 → 可引爆。
            int remaining = WeaponTriggerRules.MineFuseFrames;
            for (int i = 0; i < WeaponTriggerRules.MineFuseFrames; i++)
            {
                Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                    Trigger(WeaponId.Mine), Ctx(fuseArmed: true, fuseRemaining: remaining)));
                remaining = WeaponTriggerRules.TickFuse(remaining);
            }

            Assert.IsTrue(WeaponTriggerRules.IsFuseExpired(remaining));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(
                Trigger(WeaponId.Mine), Ctx(fuseArmed: true, fuseRemaining: remaining)));

            // beepTimes 末响 59，第 60 帧爆。
            Assert.IsTrue(WeaponTriggerRules.ShouldBeep(59));
            Assert.IsFalse(WeaponTriggerRules.ShouldBeep(60));
        }

        // ------------------------------------------------------------------
        // 被爆炸命中触发：gunpowderBarrel（连锁）
        // ------------------------------------------------------------------

        [Test]
        public void GunpowderBarrel_ChainsOnlyFromBlastHit()
        {
            DataTrigger t = Trigger(WeaponId.GunpowderBarrel);
            Assert.IsTrue(ProjectileTriggerRules.CanChainFromBlast(t));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(blast: true)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx()));
        }

        [Test]
        public void NonBarrelWeapons_DoNotChain()
        {
            Assert.IsFalse(ProjectileTriggerRules.CanChainFromBlast(Trigger(WeaponId.CherryBomb)));
            Assert.IsFalse(ProjectileTriggerRules.CanChainFromBlast(Trigger(WeaponId.WoodenCrate)));
            Assert.IsFalse(ProjectileTriggerRules.CanChainFromBlast(Trigger(WeaponId.Mine)));
        }

        // ------------------------------------------------------------------
        // 放置类 / 掩体：永不主动引爆
        // ------------------------------------------------------------------

        [Test]
        public void WoodenCrate_NeverDetonates()
        {
            DataTrigger t = Trigger(WeaponId.WoodenCrate);
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(contact: true, clicked: true, blast: true, vx: 0f, vy: 0f,
                    fuseArmed: true, fuseRemaining: 0)));
        }

        [Test]
        public void Barrel_DoesNotDetonateFromRestOrContact()
        {
            DataTrigger t = Trigger(WeaponId.GunpowderBarrel);
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(contact: true, vx: 0f, vy: 0f)));
        }
    }
}
