using NUnit.Framework;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;
using DataTrigger = PirateCrew.Data.WeaponTrigger;
using CombatTrigger = PirateCrew.Combat.WeaponTrigger;

namespace PirateCrew.Battle.Tests
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
        // 3D 静止判据口径：世界速度 → Flash (vx, vy)（WeaponProjectile.FlashRestComponents）
        //
        // 口径（详见 WeaponProjectile.FlashRestComponents 与 docs/3D空间模型对齐.md §1）：
        //   vx = XZ 平面速度模长（Flash 的横向"是否在动"，3D 里两个水平轴任一有速度都算在动）
        //   vy = 世界 Y 速度 / FlashSpeedScale（Flash 的 vy 是重力轴分量，3D 重力沿 -Y）
        // ------------------------------------------------------------------

        [TestCase(0f, -5f, 0f)]   // 竖直下落（重力轴）
        [TestCase(0f, 5f, 0f)]    // 竖直上抛
        public void RestComponents_3D_VerticalMotionIsNotAtRest(float x, float y, float z)
        {
            // 竖直方向的运动必须由 vy 体现：若把平面重投影的 Z 当 vy，竖直下落会被读成 (0,0) →
            // dynamite 在半空误判静止引爆。这也是文档"dynamite 落水后下沉不静止"的同一条口径。
            WeaponProjectile.FlashRestComponents(new Vector3(x, y, z), out float vx, out float vy);
            Assert.AreEqual(0f, vx, 1e-6f);
            Assert.AreEqual(y / LevelGeometry.FlashSpeedScale, vy, 1e-5f);
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(vx, vy));
        }

        [Test]
        public void RestComponents_3D_HorizontalDriftOnEitherAxisIsNotAtRest()
        {
            // X 或 Z 任一方向在动 → 平面模长非零 → 不静止（只读 world.x 会漏掉沿 Z 的滑行）。
            WeaponProjectile.FlashRestComponents(new Vector3(0f, 0f, 0.5f), out float vxZ, out float vyZ);
            Assert.Greater(vxZ, 0f);
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(vxZ, vyZ));

            WeaponProjectile.FlashRestComponents(new Vector3(0.5f, 0f, 0f), out float vxX, out float vyX);
            Assert.Greater(vxX, 0f);
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(vxX, vyX));
        }

        [Test]
        public void RestComponents_3D_PlaneMagnitudeUsesBothHorizontalAxes()
        {
            // 世界 (3, 0, 4) 单位/秒 → XZ 模长 5 → Flash 5 / 0.78125 = 6.4 px/帧；Y 不参与平面模长。
            WeaponProjectile.FlashRestComponents(new Vector3(3f, 0f, 4f), out float vx, out float vy);
            Assert.AreEqual(5f / LevelGeometry.FlashSpeedScale, vx, 1e-5f);
            Assert.AreEqual(0f, vy, 1e-6f);
        }

        [Test]
        public void RestComponents_3D_RestingOnGroundIsAtRest()
        {
            WeaponProjectile.FlashRestComponents(Vector3.zero, out float vx, out float vy);
            Assert.AreEqual(0f, vx, 1e-6f);
            Assert.AreEqual(0f, vy, 1e-6f);
            Assert.IsTrue(WeaponTriggerRules.IsAtRest(vx, vy));
        }

        [Test]
        public void Dynamite_3D_VerticalFall_DoesNotDetonate()
        {
            // 端到端：把 3D 世界速度喂进静止判据 → 下落的 dynamite 不应引爆。
            DataTrigger t = Trigger(WeaponId.Dynamite);
            WeaponProjectile.FlashRestComponents(new Vector3(0f, -5f, 0f), out float vx, out float vy);
            Assert.IsFalse(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: vx, vy: vy)));
            Assert.IsTrue(ProjectileTriggerRules.ShouldDetonateThisFrame(t, Ctx(vx: 0f, vy: 0f)));
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
