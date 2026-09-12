using NUnit.Framework;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// ExplosionResolver 测试（3D 球泛化）。期望值全部可由逆向文档 §5.3 的公式手算复核，
    /// 口径见 docs/M2-3D空间模型对齐.md §5：
    /// radius = size/2 + 20；d = 3D 距离（平面 (x,y) + 高度 Height）；
    /// falloff = 1 - d/radius；damage = maxDamage*falloff；k = 0.06*falloff*maxDamage；
    /// deltaVx = nx*5k；deltaVy = ny*5k；deltaVUp = nUp*5k + 6k（世界 +Y）。
    /// 高度差 = 0 时 d 退化为平面距离，deltaVx/falloff/damage 与 2D 旧实现逐值相同；
    /// 原 deltaVy 里的 -6k 抬升被拆到 DeltaVUp，故 deltaVy 净增 +6k。
    /// </summary>
    [TestFixture]
    public class ExplosionResolverTests
    {
        private const float Eps = 1e-4f;

        private static ExplosionTarget T(float x, float y, bool alive = true)
        {
            return new ExplosionTarget(x, y, alive);
        }

        private static ExplosionTarget T(float x, float y, float height, bool alive = true)
        {
            return new ExplosionTarget(x, y, height, alive);
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
            // nx=1, ny=0, nUp=0 → deltaVx = 1*5*1.5 = 7.5；deltaVy = 0；deltaVUp = 6*1.5 = 9
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(35f, 0f) });

            Assert.AreEqual(1, result.HitCount);
            var hit = result.Hits[0];
            Assert.AreEqual(0.5f, hit.Falloff, Eps);
            Assert.AreEqual(25f, hit.Damage, Eps);
            Assert.AreEqual(7.5f, hit.DeltaVx, Eps);
            Assert.AreEqual(0f, hit.DeltaVy, Eps, "高度差=0、ny=0 → 平面纵向增量为 0");
            Assert.AreEqual(9f, hit.DeltaVUp, Eps, "固定 6k 抬升已拆到 DeltaVUp（世界 +Y）");
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
            Assert.AreEqual(0f, result.Hits[0].DeltaVUp, Eps, "k=0 → 抬升项也归零");
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
            // deltaVx = deltaVy = 0；deltaVUp = 6*3 = 18（仅保留"额外上抛"分量，不臆造水平推力）
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(0f, 0f) });

            Assert.AreEqual(1, result.HitCount);
            var hit = result.Hits[0];
            Assert.AreEqual(1f, hit.Falloff, Eps);
            Assert.AreEqual(50f, hit.Damage, Eps);
            Assert.AreEqual(0f, hit.DeltaVx, Eps);
            Assert.AreEqual(0f, hit.DeltaVy, Eps);
            Assert.AreEqual(18f, hit.DeltaVUp, Eps);
            Assert.IsFalse(float.IsNaN(hit.Damage), "d==0 不得产生 NaN");
            Assert.IsFalse(float.IsNaN(hit.DeltaVy), "d==0 不得产生 NaN");
            Assert.IsFalse(float.IsNaN(hit.DeltaVUp), "d==0 不得产生 NaN");
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
            // 3D 化后"向上"不再由平面 y 表达，而是独立的 DeltaVUp（世界 +Y）。
            // 目标在爆心平面下方 (0,35)：ny=+1 → deltaVy = 1*5*1.5 = 7.5；nUp=0 → deltaVUp = 6*1.5 = 9 > 0。
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, new[] { T(0f, 35f) });

            Assert.AreEqual(0f, result.Hits[0].DeltaVx, Eps);
            Assert.AreEqual(7.5f, result.Hits[0].DeltaVy, Eps);
            Assert.AreEqual(9f, result.Hits[0].DeltaVUp, Eps);
            Assert.Greater(result.Hits[0].DeltaVUp, 0f, "固定 6k 保证总是额外上抛（世界 +Y）");
        }

        [Test]
        public void Resolve_TargetAboveCenterInHeight_GetsStrongerUpwardPush()
        {
            // 高度维度的"在爆心上方"：target 平面 (0,0)、高度 35 → dh=35, d=35
            // falloff = 0.5；k = 1.5；nUp = 35/35 = 1
            // deltaVx = deltaVy = 0（平面无偏移）；deltaVUp = 1*5*1.5 + 6*1.5 = 7.5 + 9 = 16.5
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, new[] { T(0f, 0f, 35f) });

            Assert.AreEqual(0f, result.Hits[0].DeltaVx, Eps);
            Assert.AreEqual(0f, result.Hits[0].DeltaVy, Eps);
            Assert.AreEqual(16.5f, result.Hits[0].DeltaVUp, Eps);
        }

        [Test]
        public void Resolve_3D_MixedDirection_UsesRadialUnitVectorOver3DDistance()
        {
            // 纯 3D 方向：target (21, 0, 28) 相对爆心 → d = sqrt(21²+0²+28²) = 35
            // （21-28-35 为整数勾股组，便于逐值手算）
            // falloff = 0.5；k = 1.5
            // nx = 21/35 = 0.6；ny = 0；nUp = 28/35 = 0.8
            // deltaVx = 0.6*5*1.5 = 4.5；deltaVy = 0；deltaVUp = 0.8*5*1.5 + 6*1.5 = 6 + 9 = 15
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, new[] { T(21f, 0f, 28f) });

            Assert.AreEqual(1, result.HitCount);
            var hit = result.Hits[0];
            Assert.AreEqual(0.5f, hit.Falloff, Eps);
            Assert.AreEqual(25f, hit.Damage, Eps);
            Assert.AreEqual(4.5f, hit.DeltaVx, Eps);
            Assert.AreEqual(0f, hit.DeltaVy, Eps);
            Assert.AreEqual(15f, hit.DeltaVUp, Eps);
        }

        [Test]
        public void Resolve_3D_HeightIncreasesDistanceAndReducesDamage()
        {
            // 平面 d=40 的目标：高度 0 → d=40；高度 30 → d=sqrt(40²+30²)=50
            // radius = 70
            //   h=0 ：falloff = 1 - 40/70 = 0.4285714 → damage = 21.428572
            //   h=30：falloff = 1 - 50/70 = 0.2857143 → damage = 14.285714
            var flatOnly = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, new[] { T(40f, 0f, 0f) });
            var elevated = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, new[] { T(40f, 0f, 30f) });

            float flatFalloff = 1f - 40f / 70f;
            float elevatedFalloff = 1f - 50f / 70f;

            Assert.AreEqual(1, flatOnly.HitCount);
            Assert.AreEqual(1, elevated.HitCount);
            Assert.AreEqual(flatFalloff, flatOnly.Hits[0].Falloff, Eps);
            Assert.AreEqual(elevatedFalloff, elevated.Hits[0].Falloff, Eps);
            Assert.AreEqual(50f * flatFalloff, flatOnly.Hits[0].Damage, Eps);
            Assert.AreEqual(50f * elevatedFalloff, elevated.Hits[0].Damage, Eps);
            Assert.Less(elevated.Hits[0].Falloff, flatOnly.Hits[0].Falloff, "高度差计入距离后衰减更狠");
            Assert.Less(elevated.Hits[0].Damage, flatOnly.Hits[0].Damage);
        }

        [Test]
        public void Resolve_3D_HeightBeyondRadius_Misses()
        {
            // 平面无偏移、高度 70（=radius）→ d=70 命中但 falloff=0；高度 71 → 未命中
            var edge = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, new[] { T(0f, 0f, 70f) });
            var beyond = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, new[] { T(0f, 0f, 71f) });

            Assert.AreEqual(1, edge.HitCount);
            Assert.AreEqual(0f, edge.Hits[0].Falloff, Eps);
            Assert.AreEqual(0f, edge.Hits[0].DeltaVx, Eps, "边缘 k=0");
            Assert.AreEqual(0f, edge.Hits[0].DeltaVy, Eps, "边缘 k=0");
            Assert.AreEqual(0f, edge.Hits[0].DeltaVUp, Eps, "边缘 k=0，抬升项也归零");
            Assert.AreEqual(0f, beyond.HitCount);
        }

        [Test]
        public void Resolve_3D_HonorsCenterHeight()
        {
            // 爆心高度 100、目标高度 128 → dh = 28，与"爆心 0、目标高 28"等价（21-28-35 用例）
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 100f, new[] { T(21f, 0f, 128f) });

            Assert.AreEqual(1, result.HitCount);
            var hit = result.Hits[0];
            Assert.AreEqual(0.5f, hit.Falloff, Eps);
            Assert.AreEqual(4.5f, hit.DeltaVx, Eps);
            Assert.AreEqual(15f, hit.DeltaVUp, Eps);
        }

        [Test]
        public void Resolve_HeightZero_MatchesLegacyPlanarValues()
        {
            // 硬约束回归：高度差 = 0 时，falloff / damage / deltaVx 与原 2D 实现逐值相同。
            // 原实现 deltaVy = ny*5k - 6k；新实现把 -6k 拆到 DeltaVUp，故：
            //   deltaVy_new = deltaVy_legacy + 6k，deltaVUp = 6k。
            var targets = new[] { T(35f, 0f), T(0f, 35f), T(0f, -35f), T(20f, 20f) };
            var result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, targets);

            Assert.AreEqual(4, result.HitCount);
            for (int i = 0; i < targets.Length; i++)
            {
                float dx = targets[i].X;
                float dy = targets[i].Y;
                float d = (float)System.Math.Sqrt(dx * dx + dy * dy);
                float legacyFalloff = 1f - d / 70f;
                float legacyK = 0.06f * legacyFalloff * 50f;
                float legacyDx = dx / d * 5f * legacyK;
                float legacyDy = dy / d * 5f * legacyK - 6f * legacyK;

                ExplosionHit hit = result.Hits[i];
                Assert.AreEqual(legacyFalloff, hit.Falloff, Eps, "falloff 逐值不变");
                Assert.AreEqual(50f * legacyFalloff, hit.Damage, Eps, "damage 逐值不变");
                Assert.AreEqual(legacyDx, hit.DeltaVx, Eps, "deltaVx 逐值不变");
                Assert.AreEqual(legacyDy + 6f * legacyK, hit.DeltaVy, Eps,
                    "deltaVy 净增 6k（抬升项被拆出）");
                Assert.AreEqual(6f * legacyK, hit.DeltaVUp, Eps,
                    "高度差=0 时 DeltaVUp 恒为固定 6k 抬升项");
            }
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
