using NUnit.Framework;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// ExplosionResolver 测试。期望值全部可由逆向文档 §5.3 的公式手算复核：
    /// radius = size/2 + 20；falloff = 1 - d/radius；damage = maxDamage*falloff；
    /// k = 0.06*falloff*maxDamage；deltaVx = nx*5k；deltaVy = ny*5k - 6k。
    /// </summary>
    [TestFixture]
    public class ExplosionResolverTests
    {
        private const float Eps = 1e-4f;

        private static ExplosionTarget T(float x, float y, bool alive = true)
        {
            return new ExplosionTarget(x, y, alive);
        }

        [Test]
        public void Radius_Size100_Is70()
        {
            // 100/2 + 20 = 70
            Assert.AreEqual(70f, ExplosionResolver.Radius(100f), Eps);
        }

        [Test]
        public void Radius_Size250_Is145()
        {
            // 250/2 + 20 = 145（dynamite）
            Assert.AreEqual(145f, ExplosionResolver.Radius(250f), Eps);
        }

        [Test]
        public void Resolve_HalfFalloff_MatchesHandComputed()
        {
            // size=100 → radius=70；target=(35,0) → d=35
            // falloff = 1 - 35/70 = 0.5
            // damage  = 50 * 0.5 = 25
            // k       = 0.06 * 0.5 * 50 = 1.5
            // nx=1, ny=0 → deltaVx = 1*5*1.5 = 7.5；deltaVy = 0 - 6*1.5 = -9
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(35f, 0f) });

            Assert.AreEqual(1, result.HitCount);
            var hit = result.Hits[0];
            Assert.AreEqual(0.5f, hit.Falloff, Eps);
            Assert.AreEqual(25f, hit.Damage, Eps);
            Assert.AreEqual(7.5f, hit.DeltaVx, Eps);
            Assert.AreEqual(-9f, hit.DeltaVy, Eps);
            Assert.AreEqual(0.5f, result.EvilnessGain, Eps);
        }

        [Test]
        public void Resolve_AtRadiusEdge_ZeroDamageButCountsAsHit()
        {
            // d == radius == 70 → falloff = 1 - 70/70 = 0 → damage = 0，击退也为 0
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(70f, 0f) });

            Assert.AreEqual(1, result.HitCount, "d<=radius 仍算命中");
            Assert.AreEqual(0f, result.Hits[0].Falloff, Eps);
            Assert.AreEqual(0f, result.Hits[0].Damage, Eps);
            Assert.AreEqual(0f, result.Hits[0].DeltaVx, Eps);
            Assert.AreEqual(0f, result.Hits[0].DeltaVy, Eps);
            Assert.AreEqual(0f, result.EvilnessGain, Eps);
        }

        [Test]
        public void Resolve_BeyondRadius_Misses()
        {
            // d = 71 > 70 → 未命中，不进 Hits，不计 evilness
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(71f, 0f) });

            Assert.AreEqual(0, result.HitCount);
            Assert.AreEqual(0f, result.EvilnessGain, Eps);
        }

        [Test]
        public void Resolve_DistanceZero_FullDamageZeroDirection()
        {
            // ★ d==0 显式处置：falloff = 1（中心满伤），方向取零向量
            // damage = 50 * 1 = 50
            // k = 0.06 * 1 * 50 = 3
            // deltaVx = 0；deltaVy = 0 - 6*3 = -18（仅保留"额外上抛"分量，不臆造水平推力）
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(0f, 0f) });

            Assert.AreEqual(1, result.HitCount);
            var hit = result.Hits[0];
            Assert.AreEqual(1f, hit.Falloff, Eps);
            Assert.AreEqual(50f, hit.Damage, Eps);
            Assert.AreEqual(0f, hit.DeltaVx, Eps);
            Assert.AreEqual(-18f, hit.DeltaVy, Eps);
            Assert.IsFalse(float.IsNaN(hit.Damage), "d==0 不得产生 NaN");
            Assert.IsFalse(float.IsNaN(hit.DeltaVy), "d==0 不得产生 NaN");
            Assert.AreEqual(1f, result.EvilnessGain, Eps);
        }

        [Test]
        public void Resolve_DeadTargetsAreSkipped()
        {
            var result = ExplosionResolver.Resolve(
                100f, 50f, 0f, 0f, new[] { T(0f, 0f, alive: false), T(35f, 0f, alive: true) });

            Assert.AreEqual(1, result.HitCount);
            Assert.AreEqual(1, result.Hits[0].Index);   // 索引保留原列表位置
            Assert.AreEqual(0.5f, result.EvilnessGain, Eps);
        }

        [Test]
        public void Resolve_AlwaysLiftsUpward_EvenForTargetBelowCenter()
        {
            // Flash 坐标系 y 向下：目标在爆心下方 → ny = +1
            // k = 1.5 → deltaVy = 1*5*1.5 - 6*1.5 = 7.5 - 9 = -1.5（负 = 向上）
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(0f, 35f) });

            Assert.AreEqual(0f, result.Hits[0].DeltaVx, Eps);
            Assert.AreEqual(-1.5f, result.Hits[0].DeltaVy, Eps);
            Assert.Less(result.Hits[0].DeltaVy, 0f, "公式 -6k 保证总是额外上抛");
        }

        [Test]
        public void Resolve_TargetAboveCenter_GetsStrongerUpwardPush()
        {
            // 目标在爆心上方 → ny = -1；deltaVy = -1*5*1.5 - 6*1.5 = -16.5
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(0f, -35f) });

            Assert.AreEqual(-16.5f, result.Hits[0].DeltaVy, Eps);
        }

        [Test]
        public void Resolve_EvilnessSumsFalloffOfAllHits()
        {
            // falloff: d=0 → 1；d=35 → 0.5；d=70 → 0；d=71 → 未命中；dead → 跳过
            // Σ falloff = 1 + 0.5 + 0 = 1.5
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[]
            {
                T(0f, 0f),
                T(35f, 0f),
                T(70f, 0f),
                T(71f, 0f),
                T(10f, 0f, alive: false)
            });

            Assert.AreEqual(3, result.HitCount);
            Assert.AreEqual(1.5f, result.EvilnessGain, Eps);
        }

        [Test]
        public void Resolve_OffsetCenter_UsesRelativeDistance()
        {
            // 爆心 (100,100)，目标 (135,100) → d=35，与原点情况等价
            var result = ExplosionResolver.Resolve(100f, 50f, 100f, 100f, new[] { T(135f, 100f) });

            Assert.AreEqual(25f, result.Hits[0].Damage, Eps);
            Assert.AreEqual(7.5f, result.Hits[0].DeltaVx, Eps);
        }

        [Test]
        public void Resolve_Dynamite_Size250FullDamage70AtCenter()
        {
            // radius = 145；目标在 (10,0) → d=10
            // falloff = 1 - 10/145 = 0.9310345
            // damage  = 70 * 0.9310345 = 65.17241
            var result = ExplosionResolver.Resolve(250f, 70f, 0f, 0f, new[] { T(10f, 0f) });

            float expectedFalloff = 1f - 10f / 145f;
            Assert.AreEqual(expectedFalloff, result.Hits[0].Falloff, Eps);
            Assert.AreEqual(70f * expectedFalloff, result.Hits[0].Damage, Eps);
        }

        [Test]
        public void Resolve_EmptyTargetList_ReturnsEmptyResult()
        {
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new ExplosionTarget[0]);

            Assert.AreEqual(0, result.HitCount);
            Assert.AreEqual(0f, result.EvilnessGain, Eps);
        }
    }
}
