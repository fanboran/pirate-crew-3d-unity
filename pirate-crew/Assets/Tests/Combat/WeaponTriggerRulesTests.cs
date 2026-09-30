using NUnit.Framework;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// WeaponTriggerRules 测试。覆盖各类触发/引爆条件的判定语义。
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
        // 引信语义（ProximityFuse 触发类：点燃且到点才引爆）
        // ------------------------------------------------------------------

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
        public void None_NeverDetonates()
        {
            // woodenCrate 等不爆炸
            Assert.IsFalse(WeaponTriggerRules.ShouldDetonate(WeaponTrigger.None, Ctx(
                contact: true, clicked: true, blastHit: true, fuseArmed: true, fuseRemaining: 0)));
        }
    }
}
