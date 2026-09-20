using NUnit.Framework;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// WeaponTriggerRules 测试（加分项）。规则出自逆向文档 §5.2 触发/引爆条件列，
    /// 以及 mine 的 60 帧引信与 beepTimes [0,15,30,38,45,49,53,55,57,59]。
    /// </summary>
    [TestFixture]
    public class WeaponTriggerRulesTests
    {
        private static TriggerContext Ctx(
            bool contact = false, bool clicked = false, bool blastHit = false,
            float vx = 0f, float vy = 0f, bool fuseArmed = false, int fuseRemaining = 0)
        {
            return new TriggerContext(contact, clicked, blastHit, vx, vy, fuseArmed, fuseRemaining);
        }

        // ------------------------------------------------------------------
        // 接触即爆（cannonball / cherryBomb / parachuteBomb / rumBottle / piecesOfEight）
        // ------------------------------------------------------------------

        [Test]
        public void Contact_DetonatesOnTouchOnly()
        {
            Assert.IsTrue(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.Contact, Ctx(contact: true)));
            Assert.IsFalse(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.Contact, Ctx(contact: false)));
        }

        // ------------------------------------------------------------------
        // 静止即爆（dynamite：vx == 0 且 |vy| < 0.2）
        // ------------------------------------------------------------------

        [Test]
        public void AtRest_TrueWhenVxZeroAndVyBelowEpsilon()
        {
            Assert.IsTrue(WeaponTriggerRules.IsAtRest(0f, 0.19f));
            Assert.IsTrue(WeaponTriggerRules.IsAtRest(0f, -0.19f));
            Assert.IsTrue(WeaponTriggerRules.ShouldDetonate(
                WeaponTrigger.AtRest, Ctx(vx: 0f, vy: 0.1f)));
        }

        [Test]
        public void AtRest_FalseWhenVyAtOrAboveEpsilon()
        {
            // 严格小于 0.2：恰好 0.2 不算静止
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(0f, 0.2f));
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(0f, -0.2f));
        }

        [Test]
        public void AtRest_FalseWhenHorizontalVelocityNonZero()
        {
            Assert.IsFalse(WeaponTriggerRules.IsAtRest(0.01f, 0f));
        }

        // ------------------------------------------------------------------
        // 点击引爆（banana / tidalWave）
        // ------------------------------------------------------------------

        [Test]
        public void Click_DetonatesOnClickOnly()
        {
            Assert.IsTrue(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.Click, Ctx(clicked: true)));
            Assert.IsFalse(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.Click, Ctx(clicked: false)));
        }

        // ------------------------------------------------------------------
        // 被爆炸命中触发（gunpowderBarrel 连锁）
        // ------------------------------------------------------------------

        [Test]
        public void BlastContact_DetonatesWhenHitByExplosion()
        {
            Assert.IsTrue(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.BlastContact, Ctx(blastHit: true)));
            Assert.IsFalse(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.BlastContact, Ctx(blastHit: false)));
        }

        // ------------------------------------------------------------------
        // mine：60px 内有人且移动 → 引信 60 帧
        // ------------------------------------------------------------------

        [Test]
        public void ProximityFuse_StartsWhenMovingCharacterWithin60Px()
        {
            Assert.IsTrue(WeaponTriggerRules.ShouldStartFuse(WeaponTrigger.ProximityFuse, 10f, true));
            Assert.IsTrue(WeaponTriggerRules.ShouldStartFuse(WeaponTrigger.ProximityFuse, 60f, true));
        }

        [Test]
        public void ProximityFuse_DoesNotStartBeyond60PxOrWhenStill()
        {
            Assert.IsFalse(WeaponTriggerRules.ShouldStartFuse(WeaponTrigger.ProximityFuse, 60.1f, true));
            Assert.IsFalse(WeaponTriggerRules.ShouldStartFuse(WeaponTrigger.ProximityFuse, 10f, false));
            Assert.IsFalse(WeaponTriggerRules.ShouldStartFuse(WeaponTrigger.ProximityFuse, -1f, true));
        }

        [Test]
        public void ProximityFuse_OnlyAppliesToFuseTrigger()
        {
            Assert.IsFalse(WeaponTriggerRules.ShouldStartFuse(WeaponTrigger.Contact, 0f, true));
        }

        [Test]
        public void ProximityFuse_DetonatesOnlyWhenArmedAndExpired()
        {
            Assert.IsFalse(WeaponTriggerRules.ShouldDetonate(
                WeaponTrigger.ProximityFuse, Ctx(fuseArmed: false, fuseRemaining: 0)));
            Assert.IsFalse(WeaponTriggerRules.ShouldDetonate(
                WeaponTrigger.ProximityFuse, Ctx(fuseArmed: true, fuseRemaining: 1)));
            Assert.IsTrue(WeaponTriggerRules.ShouldDetonate(
                WeaponTrigger.ProximityFuse, Ctx(fuseArmed: true, fuseRemaining: 0)));
        }

        [Test]
        public void TickFuse_CountsDownAndStopsAtZero()
        {
            Assert.AreEqual(59, WeaponTriggerRules.TickFuse(60));
            Assert.AreEqual(0, WeaponTriggerRules.TickFuse(1));
            Assert.AreEqual(0, WeaponTriggerRules.TickFuse(0));
            Assert.AreEqual(0, WeaponTriggerRules.TickFuse(-3));
        }

        [Test]
        public void Fuse_ExpiresAfterExactly60Frames()
        {
            int remaining = WeaponTriggerRules.MineFuseFrames;
            for (int i = 0; i < 59; i++)
            {
                remaining = WeaponTriggerRules.TickFuse(remaining);
            }

            Assert.AreEqual(1, remaining);
            Assert.IsFalse(WeaponTriggerRules.IsFuseExpired(remaining));

            remaining = WeaponTriggerRules.TickFuse(remaining);
            Assert.IsTrue(WeaponTriggerRules.IsFuseExpired(remaining));
        }

        [Test]
        public void FuseEpsilon_IsFiftyNineFramesUntilDetonation()
        {
            // 文档：引信 60 帧；TickFuse(1) → 0 → 到点
            Assert.AreEqual(WeaponTriggerRules.MineFuseFrames, 60);
            Assert.IsTrue(WeaponTriggerRules.IsFuseExpired(WeaponTriggerRules.TickFuse(1)));
        }

        // ------------------------------------------------------------------
        // beepTimes 序列
        // ------------------------------------------------------------------

        [Test]
        public void MineBeepTimes_MatchesDocumentedSequence()
        {
            Assert.AreEqual(
                new[] { 0, 15, 30, 38, 45, 49, 53, 55, 57, 59 },
                WeaponTriggerRules.MineBeepTimes);
        }

        [Test]
        public void ShouldBeep_TrueOnlyOnListedFrames()
        {
            Assert.IsTrue(WeaponTriggerRules.ShouldBeep(0));
            Assert.IsTrue(WeaponTriggerRules.ShouldBeep(15));
            Assert.IsTrue(WeaponTriggerRules.ShouldBeep(38));
            Assert.IsTrue(WeaponTriggerRules.ShouldBeep(59));
            Assert.IsFalse(WeaponTriggerRules.ShouldBeep(14));
            Assert.IsFalse(WeaponTriggerRules.ShouldBeep(60));
        }

        [Test]
        public void BeepCountUpTo_AccumulatesTenBeepsBeforeDetonation()
        {
            Assert.AreEqual(1, WeaponTriggerRules.BeepCountUpTo(0));
            Assert.AreEqual(1, WeaponTriggerRules.BeepCountUpTo(14));
            Assert.AreEqual(2, WeaponTriggerRules.BeepCountUpTo(15));
            Assert.AreEqual(10, WeaponTriggerRules.BeepCountUpTo(59));
            Assert.AreEqual(10, WeaponTriggerRules.BeepCountUpTo(60));
        }

        [Test]
        public void None_NeverDetonates()
        {
            // woodenCrate 等不爆炸
            Assert.IsFalse(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.None, Ctx(
                contact: true, clicked: true, blastHit: true, fuseArmed: true, fuseRemaining: 0)));
        }
    }
}
