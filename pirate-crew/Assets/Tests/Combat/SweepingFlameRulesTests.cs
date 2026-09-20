using NUnit.Framework;
using PirateCrew.Combat;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// <see cref="SweepingFlameRules"/> 测试（§5.2 表格末行 / rumBottle 行 / §6.1、§6.3 随机击退）。
    /// </summary>
    [TestFixture]
    public class SweepingFlameRulesTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void Constants_MatchSection5_2_And6_3()
        {
            Assert.AreEqual(2, SweepingFlameRules.SpawnCount);
            Assert.AreEqual(8f, SweepingFlameRules.SpreadStep);
            Assert.AreEqual(8f, SweepingFlameRules.HitDistance);
            Assert.AreEqual(30f, SweepingFlameRules.DamagePerSegment);
            Assert.AreEqual(8f, SweepingFlameRules.KnockbackHorizontalScale);
            Assert.AreEqual(6f, SweepingFlameRules.KnockbackVerticalBase);
            Assert.AreEqual(2f, SweepingFlameRules.KnockbackVerticalRandomScale);
        }

        [Test]
        public void SpreadDirections_AreRightAndLeft()
        {
            Assert.AreEqual(2, SweepingFlameRules.SpreadDirections.Length);
            Assert.AreEqual(1, SweepingFlameRules.SpreadDirections[0]);
            Assert.AreEqual(-1, SweepingFlameRules.SpreadDirections[1]);
        }

        [Test]
        public void NextSegmentX_Steps8Px()
        {
            Assert.AreEqual(108f, SweepingFlameRules.NextSegmentX(100f, 1), Eps);
            Assert.AreEqual(92f, SweepingFlameRules.NextSegmentX(100f, -1), Eps);
            Assert.AreEqual(108f, SweepingFlameRules.NextSegmentX(100f, 0), Eps, "0 视作向右（>=0）");
        }

        [Test]
        public void CanSpread_RequiresTileBelowAndNoTileAbove()
        {
            Assert.IsTrue(SweepingFlameRules.CanSpread(hasTileBelow: true, hasTileAbove: false));
            Assert.IsFalse(SweepingFlameRules.CanSpread(true, true));
            Assert.IsFalse(SweepingFlameRules.CanSpread(false, false));
            Assert.IsFalse(SweepingFlameRules.CanSpread(false, true));
        }

        [Test]
        public void ShouldHit_StrictLessThan8Px()
        {
            Assert.IsTrue(SweepingFlameRules.ShouldHit(7.999f));
            Assert.IsFalse(SweepingFlameRules.ShouldHit(8f));
            Assert.IsFalse(SweepingFlameRules.ShouldHit(9f));
        }

        [Test]
        public void Knockback_MatchesRandomFormula()
        {
            // rand=0.5 → vx=(0.5-0.5)*8=0；vy=-(0.5*2+6)=-7。
            SweepingFlameRules.Knockback(0.5f, out float vx, out float vy);
            Assert.AreEqual(0f, vx, Eps);
            Assert.AreEqual(-7f, vy, Eps);

            // rand=0 → vx=-4；vy=-6。
            SweepingFlameRules.Knockback(0f, out vx, out vy);
            Assert.AreEqual(-4f, vx, Eps);
            Assert.AreEqual(-6f, vy, Eps);

            // rand=1 → vx=4；vy=-8。
            SweepingFlameRules.Knockback(1f, out vx, out vy);
            Assert.AreEqual(4f, vx, Eps);
            Assert.AreEqual(-8f, vy, Eps);
        }

        [Test]
        public void SegmentCount_OnePer8Px()
        {
            Assert.AreEqual(1, SweepingFlameRules.SegmentCount(0f));
            Assert.AreEqual(1, SweepingFlameRules.SegmentCount(7f));
            Assert.AreEqual(2, SweepingFlameRules.SegmentCount(8f));
            Assert.AreEqual(3, SweepingFlameRules.SegmentCount(16f));
        }
    }
}
