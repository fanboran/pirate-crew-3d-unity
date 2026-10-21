using System.Collections.Generic;
using NUnit.Framework;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// 直击结算通道的数学测试：命中带边界判定 + 固定伤害（无衰减）+ 速度比例（线性）。
    /// 断言用具体数字钉数学关系，不涉及逐格伤害定案值。
    /// </summary>
    [TestFixture]
    public class DirectHitResolverTests
    {
        const float Eps = 1e-4f;

        // ------------------------------------------------------------------
        // 命中带边界
        // ------------------------------------------------------------------

        [Test]
        public void Contains_BoundaryIsInclusive()
        {
            var band = new DirectHitBand(halfWidth: 48f, heightMin: -64f, heightMax: 0f);

            // 半宽边界：|dx| == 48 命中（闭区间），49 不命中。
            Assert.IsTrue(DirectHitResolver.Contains(band, 100f, 148f, -32f));
            Assert.IsTrue(DirectHitResolver.Contains(band, 100f, 52f, -32f));
            Assert.IsFalse(DirectHitResolver.Contains(band, 100f, 149f, -32f));
            Assert.IsFalse(DirectHitResolver.Contains(band, 100f, 51f, -32f));

            // 高度边界：上下界均含。
            Assert.IsTrue(DirectHitResolver.Contains(band, 100f, 100f, -64f));
            Assert.IsTrue(DirectHitResolver.Contains(band, 100f, 100f, 0f));
            Assert.IsFalse(DirectHitResolver.Contains(band, 100f, 100f, -65f));
            Assert.IsFalse(DirectHitResolver.Contains(band, 100f, 100f, 1f));
        }

        // ------------------------------------------------------------------
        // 固定伤害直击
        // ------------------------------------------------------------------

        [Test]
        public void ResolveFixed_AllInBandTargetsTakeSameDamage_NoFalloff()
        {
            var band = new DirectHitBand(halfWidth: 48f, heightMin: -64f, heightMax: 0f);
            var targets = new List<ExplosionTarget>
            {
                new ExplosionTarget(120f, 10f, -32f, alive: true),   // 带内（近缘）
                new ExplosionTarget(100f, 0f, -1f, alive: true),     // 带内（中心）
                new ExplosionTarget(300f, 0f, -32f, alive: true),    // 带外（水平）
                new ExplosionTarget(110f, 0f, 50f, alive: true),     // 带外（高度）
                new ExplosionTarget(110f, 0f, -32f, alive: false),   // 带内但死亡 → 跳过
            };

            DirectHit[] hits = DirectHitResolver.ResolveFixed(band, 100f, fixedDamage: 60f, targets);

            Assert.AreEqual(2, hits.Length);
            // 带内两目标同伤害——距离带心远近不影响（无衰减是与爆炸通道的本质区别）。
            Assert.AreEqual(0, hits[0].Index);
            Assert.AreEqual(60f, hits[0].Damage, Eps);
            Assert.AreEqual(1, hits[1].Index);
            Assert.AreEqual(60f, hits[1].Damage, Eps);
        }

        [Test]
        public void ResolveFixed_NoTargets_ReturnsEmpty()
        {
            var band = new DirectHitBand(10f, 0f, 10f);
            Assert.AreEqual(0, DirectHitResolver.ResolveFixed(band, 0f, 60f, null).Length);
            Assert.AreEqual(0, DirectHitResolver.ResolveFixed(band, 0f, 60f, new List<ExplosionTarget>()).Length);
        }

        // ------------------------------------------------------------------
        // 速度比例直击
        // ------------------------------------------------------------------

        [Test]
        public void ResolveVelocityProportional_DamageIsSpeedTimesFactor()
        {
            var band = new DirectHitBand(halfWidth: 32f, heightMin: -100f, heightMax: 100f);
            var targets = new List<ExplosionTarget>
            {
                new ExplosionTarget(10f, 0f, 0f, alive: true),
                new ExplosionTarget(30f, 5f, 20f, alive: true),
            };

            // speed=4、系数 1.5 → 每个带内目标 6（速度是弹体属性，与目标位置无关）。
            DirectHit[] hits = DirectHitResolver.ResolveVelocityProportional(
                band, 0f, speed: 4f, damagePerSpeed: 1.5f, targets);
            Assert.AreEqual(2, hits.Length);
            Assert.AreEqual(6f, hits[0].Damage, Eps);
            Assert.AreEqual(6f, hits[1].Damage, Eps);
        }

        [Test]
        public void ResolveVelocityProportional_LinearInSpeed_AndClampsNegative()
        {
            var band = new DirectHitBand(32f, -100f, 100f);
            var targets = new List<ExplosionTarget> { new ExplosionTarget(0f, 0f, 0f, true) };

            // 线性：速度翻倍伤害翻倍。
            DirectHit[] slow = DirectHitResolver.ResolveVelocityProportional(band, 0f, 2f, 1.5f, targets);
            DirectHit[] fast = DirectHitResolver.ResolveVelocityProportional(band, 0f, 4f, 1.5f, targets);
            Assert.AreEqual(3f, slow[0].Damage, Eps);
            Assert.AreEqual(6f, fast[0].Damage, Eps);
            Assert.AreEqual(2f, fast[0].Damage / slow[0].Damage, Eps);

            // 负速度按 0 处理（调用方取模的防御性口径）。
            DirectHit[] negative = DirectHitResolver.ResolveVelocityProportional(band, 0f, -5f, 1.5f, targets);
            Assert.AreEqual(0f, negative[0].Damage, Eps);
        }
    }
}
