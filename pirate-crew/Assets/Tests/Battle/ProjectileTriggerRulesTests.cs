using NUnit.Framework;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;
using DataTrigger = PirateCrew.Data.WeaponTrigger;
using CombatTrigger = PirateCrew.Combat.WeaponTrigger;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileTriggerRules"/> 引爆判定测试。
    ///
    /// 【断言口径（B6 审计修正）】运行时弹体（<c>WeaponProjectile.Update</c>）恒以
    /// <see cref="StandardBombRules.Trigger"/>（= OnContact | OnRest，与武器目录无关）喂给
    /// <see cref="ProjectileTriggerRules.ShouldDetonateThisFrame"/>——所以本测试的引爆行为
    /// 断言一律钉<b>运行时真实入参</b>，不再按每武器目录的 Trigger 断言（那是假覆盖：
    /// 目录值在单一弹体路径下不被消费）。
    /// 每武器 Trigger 字段只测它真正被消费的那条：<c>WeaponProjectile.TriggersOnBlast</c>
    /// → <see cref="ProjectileTriggerRules.CanChainFromBlast"/>（连锁引爆）。
    /// 桥接层「flags → 五类单值触发」的机械映射用<b>合成 flags</b>钉住（不牵武器目录）。
    /// <b>按武器定案的触发回填在武器重做时恢复</b>（届时目录 Trigger 重新接入运行时路径）。
    /// </summary>
    [TestFixture]
    public class ProjectileTriggerRulesTests
    {
        static TriggerContext Ctx(
            bool contact = false, bool clicked = false, bool blast = false,
            float vx = 0f, float vy = 0f, bool fuseArmed = false, int fuseRemaining = 0)
        {
            return new TriggerContext(contact, clicked, blast, vx, vy, fuseArmed, fuseRemaining);
        }

        // ------------------------------------------------------------------
        // 运行时真实行为：StandardBombRules.Trigger 恒 OnContact | OnRest
        //（WeaponProjectile.Update 的唯一实参；「点哪把都是它」）
        // ------------------------------------------------------------------

        [Test]
        public void RuntimeTrigger_IsContactOrRest()
        {
            Assert.AreEqual(DataTrigger.OnContact | DataTrigger.OnRest, StandardBombRules.Trigger);
        }

        [Test]
        public void RuntimeTrigger_DetonatesOnContact_AtAnySpeed()
        {
            DataTrigger t = StandardBombRules.Trigger;
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true)));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(contact: true, vx: 5f, vy: 5f)));
        }

        [Test]
        public void RuntimeTrigger_DetonatesAtRest_Only()
        {
            DataTrigger t = StandardBombRules.Trigger;
            // 静止判据（vx==0 且 |vy|<0.2，Flash 像素速度域）。
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0f, vy: 0.1f)));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx()));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0f, vy: 0.5f)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0.1f, vy: 0f)));
        }

        [Test]
        public void RuntimeTrigger_ClickAndFuseAndBlast_DoNotDetonate()
        {
            DataTrigger t = StandardBombRules.Trigger;
            // 点击 / 邻近引信不在标准炸弹的触发位里；被爆炸命中走 DetonateFromBlast 直调路径
            //（运行时 Update 的 blastHit 恒 false，此处钉的是「flags 无该位即不判定」的机械语义）。
            // 上下文带非零速度避开 OnRest 位（默认 vx=vy=0 是静止即爆形态，会把「不判定」误判成 True）。
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(clicked: true, vx: 1f)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(fuseArmed: true, fuseRemaining: 0, vx: 1f)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(blast: true, vx: 1f)));
        }

        // ------------------------------------------------------------------
        // 桥接机械语义：flags → 五类单值触发（合成 flags，不牵武器目录）
        // ------------------------------------------------------------------

        [Test]
        public void Bridge_ClickFlag_RoutesToClickTrigger()
        {
            const DataTrigger t = DataTrigger.OnClick;
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(clicked: true)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true)));
        }

        [Test]
        public void Bridge_ProximityFuseFlag_RoutesToFuseTrigger()
        {
            const DataTrigger t = DataTrigger.ProximityFuse;
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(fuseArmed: true, fuseRemaining: 0)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(fuseArmed: true, fuseRemaining: 1)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(
                t, Ctx(fuseArmed: false, fuseRemaining: 0)));
        }

        [Test]
        public void Bridge_BlastFlag_RoutesToBlastContact()
        {
            const DataTrigger t = DataTrigger.OnExplosionHit;
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(blast: true)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true)));
        }

        [Test]
        public void Bridge_PlaceAndSpecialAndNone_ProduceNoActiveDetonation()
        {
            // OnPlace / Special / None 不产生主动引爆（放置与专用机制各自处理）。
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(DataTrigger.OnPlace, Ctx(contact: true)));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(DataTrigger.Special, Ctx()));
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame((DataTrigger)0, Ctx(contact: true)));
        }

        [Test]
        public void Bridge_MultipleFlags_TakeOr()
        {
            const DataTrigger t = DataTrigger.OnContact | DataTrigger.OnClick;
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(contact: true, vx: 9f)));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(clicked: true)));
        }

        // ------------------------------------------------------------------
        // 每武器 Trigger 字段的唯一运行时消费点：TriggersOnBlast（连锁引爆）
        // ------------------------------------------------------------------

        [Test]
        public void GunpowderBarrel_ChainsFromBlastHit()
        {
            DataTrigger t = WeaponCatalog.Get(WeaponId.GunpowderBarrel).Trigger;
            Assert.IsTrue(ProjectileTriggerRules.CanChainFromBlast(t),
                "gunpowderBarrel 目录 Trigger 带 OnExplosionHit → WeaponProjectile.TriggersOnBlast");
        }

        [Test]
        public void NonBarrelWeapons_DoNotChain()
        {
            Assert.IsFalse(ProjectileTriggerRules.CanChainFromBlast(
                WeaponCatalog.Get(WeaponId.CherryBomb).Trigger));
            Assert.IsFalse(ProjectileTriggerRules.CanChainFromBlast(
                WeaponCatalog.Get(WeaponId.WoodenCrate).Trigger));
            Assert.IsFalse(ProjectileTriggerRules.CanChainFromBlast(
                WeaponCatalog.Get(WeaponId.Mine).Trigger));
        }
    }
}
