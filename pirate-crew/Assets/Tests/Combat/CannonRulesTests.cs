using NUnit.Framework;
using PirateCrew.Combat;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// <see cref="CannonRules"/> 测试（§5.2 cannon 行 / §5.1 速度汇总 / §6.3 AI）。
    /// 蓄力公式与角度映射是文档未给数值的部分，按规则类头标注的「提案/待定」口径断言。
    /// </summary>
    [TestFixture]
    public class CannonRulesTests
    {
        const float Eps = 1e-3f;

        [Test]
        public void Constants_MatchSection5_1_And5_2()
        {
            Assert.AreEqual(30f, CannonRules.MaxFireStrength);
            Assert.AreEqual(4f, CannonRules.NoFireThreshold);
            Assert.AreEqual(30f, CannonRules.FireThreshold);
            Assert.AreEqual(25, CannonRules.AiFireDelayFrames);
            Assert.AreEqual(100f, CannonRules.CannonballExplosionSize);
            Assert.AreEqual(50f, CannonRules.CannonballExplosionMaxDamage);
            Assert.AreEqual(0.25f, CannonRules.ChargePerDragPx);
        }

        [Test]
        public void ChargeFromDrag_IsQuarterOfDragCappedAt30()
        {
            Assert.AreEqual(0f, CannonRules.ChargeFromDrag(0f), Eps);
            Assert.AreEqual(10f, CannonRules.ChargeFromDrag(40f), Eps);
            Assert.AreEqual(30f, CannonRules.ChargeFromDrag(120f), Eps);
            Assert.AreEqual(30f, CannonRules.ChargeFromDrag(500f), Eps, "上限 30（§5.1）");
            Assert.AreEqual(0f, CannonRules.ChargeFromDrag(-5f), Eps);
        }

        [Test]
        public void FullChargeDrag_Is120Px()
        {
            Assert.AreEqual(120f, CannonRules.FullChargeDragPx, Eps);
        }

        [Test]
        public void ShouldFire_OnlyAtFullCharge30()
        {
            Assert.IsFalse(CannonRules.ShouldFire(29.9f));
            Assert.IsTrue(CannonRules.ShouldFire(30f));
        }

        [Test]
        public void IsBelowNoFireThreshold_DocumentedBand()
        {
            Assert.IsTrue(CannonRules.IsBelowNoFireThreshold(4f));
            Assert.IsTrue(CannonRules.IsBelowNoFireThreshold(0f));
            Assert.IsFalse(CannonRules.IsBelowNoFireThreshold(5f));
        }

        [Test]
        public void AimAngleRadians_TwangStyleBackwardDrag()
        {
            // 后拉 (1,0) → 向 -x 发射 = angle π；后拉 (0,1) → 向 -y 发射 = angle -π/2。
            Assert.AreEqual((float)System.Math.PI, CannonRules.AimAngleRadians(1f, 0f), Eps);
            Assert.AreEqual(-(float)System.Math.PI / 2f, CannonRules.AimAngleRadians(0f, 1f), Eps);
            Assert.AreEqual(0f, CannonRules.AimAngleRadians(-1f, 0f), Eps);
        }

        [Test]
        public void LaunchVelocity_UsesFireStrengthAsSpeed()
        {
            CannonRules.LaunchVelocity(30f, 0f, out float vx, out float vy);
            Assert.AreEqual(30f, vx, Eps);
            Assert.AreEqual(0f, vy, Eps);

            CannonRules.LaunchVelocity(30f, (float)System.Math.PI / 2f, out vx, out vy);
            Assert.AreEqual(0f, vx, Eps);
            Assert.AreEqual(30f, vy, Eps);
        }

        [Test]
        public void LaunchVelocity_ClampsSpeedToMax30()
        {
            CannonRules.LaunchVelocity(999f, 0f, out float vx, out _);
            Assert.AreEqual(30f, vx, Eps);
            CannonRules.LaunchVelocity(-5f, 0f, out vx, out _);
            Assert.AreEqual(0f, vx, Eps);
        }

        [Test]
        public void ShouldAiFire_At25Frames()
        {
            Assert.IsFalse(CannonRules.ShouldAiFire(24));
            Assert.IsTrue(CannonRules.ShouldAiFire(25));
        }
    }
}
