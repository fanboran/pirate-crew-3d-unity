using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 引爆 → <see cref="ExplosionResolver"/> 结算的边界与连锁测试（§5.3）。
    /// 重点：d == 0 与半径边界不产生 NaN；火药桶连锁判定；初速逆变换。
    /// </summary>
    [TestFixture]
    public class ProjectileExplosionBoundaryTests
    {
        static readonly ExplosionTarget[] CenterTarget = { new ExplosionTarget(0f, 0f, true) };

        // ------------------------------------------------------------------
        // d == 0（爆心）不产生 NaN
        // ------------------------------------------------------------------

        [Test]
        public void Resolve_TargetAtCenter_IsFiniteAndMaxDamage()
        {
            // §5.3：radius = 100/2 + 20 = 70；d=0 → falloff=1；damage = 50×1 = 50
            // k = 0.06×1×50 = 3；爆心处无可定义方向 → 平面推力图 (0, 0)；
            // 3D 竖直抬升 = 6k = 18（原 2D 的 deltaVy = -18 已拆成独立字段 DeltaVUp，
            // 方向取世界 +Y 的"向上"，见 ExplosionHit/ExplosionResolver 的 3D 泛化）。
            ExplosionResult result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, CenterTarget);

            Assert.AreEqual(1, result.HitCount);
            Assert.AreEqual(50f, result.Hits[0].Damage, 1e-5f);
            Assert.AreEqual(1f, result.Hits[0].Falloff, 1e-5f);
            Assert.IsFalse(float.IsNaN(result.Hits[0].DeltaVx));
            Assert.IsFalse(float.IsNaN(result.Hits[0].DeltaVy));
            Assert.IsFalse(float.IsNaN(result.Hits[0].DeltaVUp));
            Assert.AreEqual(0f, result.Hits[0].DeltaVx, 1e-5f);
            Assert.AreEqual(0f, result.Hits[0].DeltaVy, 1e-5f);
            Assert.AreEqual(18f, result.Hits[0].DeltaVUp, 1e-5f);
            Assert.AreEqual(1f, result.EvilnessGain, 1e-5f);
        }

        [Test]
        public void Resolve_ExactlyAtRadius_FalloffZero_NoNaN()
        {
            // radius = 70；目标在 (0, 70)（Flash y 向下）恰好 d=70 → falloff = 1 - 70/70 = 0
            var targets = new[] { new ExplosionTarget(0f, 70f, true) };
            ExplosionResult result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, targets);

            Assert.AreEqual(1, result.HitCount, "d <= radius 应命中");
            Assert.AreEqual(0f, result.Hits[0].Damage, 1e-5f);
            Assert.AreEqual(0f, result.Hits[0].Falloff, 1e-5f);
            Assert.AreEqual(0f, result.Hits[0].DeltaVx, 1e-5f);
            Assert.AreEqual(0f, result.Hits[0].DeltaVy, 1e-5f);
            Assert.AreEqual(0f, result.Hits[0].DeltaVUp, 1e-5f);
            Assert.IsFalse(float.IsNaN(result.Hits[0].DeltaVy));
        }

        [Test]
        public void Resolve_3D_TargetAboveCenter_DistanceIncludesHeight_AndLiftsUp()
        {
            // 3D 球：目标在爆心正上方 height=35（半径 70）→ d=35，falloff = 0.5。
            // damage = 50×0.5 = 25；k = 0.06×0.5×50 = 1.5；
            // 径向竖直分量 = (35/35)×5×1.5 = 7.5；再加固定抬升 6k = 9 → DeltaVUp = 16.5；
            // 水平无偏移 → DeltaVx = DeltaVy = 0。
            var targets = new[] { new ExplosionTarget(0f, 0f, 35f, true) };
            ExplosionResult result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, targets);

            Assert.AreEqual(1, result.HitCount);
            Assert.AreEqual(25f, result.Hits[0].Damage, 1e-4f);
            Assert.AreEqual(0.5f, result.Hits[0].Falloff, 1e-5f);
            Assert.AreEqual(0f, result.Hits[0].DeltaVx, 1e-5f);
            Assert.AreEqual(0f, result.Hits[0].DeltaVy, 1e-5f);
            Assert.AreEqual(16.5f, result.Hits[0].DeltaVUp, 1e-4f);
        }

        [Test]
        public void Resolve_3D_HeightConsumesRadius()
        {
            // 高度差计入 3D 距离：爆心正上方 height=70 恰在半径（70）上 → 命中但 0 伤害；
            // 再高一点（70.001）就脱靶。弹体在高处爆炸时，地面目标的可用半径因此变小。
            var atEdge = new[] { new ExplosionTarget(0f, 0f, 70f, true) };
            ExplosionResult edge = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, atEdge);
            Assert.AreEqual(1, edge.HitCount);
            Assert.AreEqual(0f, edge.Hits[0].Damage, 1e-5f);

            var outside = new[] { new ExplosionTarget(0f, 0f, 70.001f, true) };
            Assert.AreEqual(0, ExplosionResolver.Resolve(100f, 50f, 0f, 0f, 0f, outside).HitCount);
        }

        [Test]
        public void Resolve_JustOutsideRadius_IsMissed()
        {
            var targets = new[] { new ExplosionTarget(0f, 70.001f, true) };
            ExplosionResult result = ExplosionResolver.Resolve(100f, 50f, 0f, 0f, targets);
            Assert.AreEqual(0, result.HitCount);
        }

        [Test]
        public void Resolve_AllWeaponsWithExplosion_AreFiniteAtCenterAndEdge()
        {
            // 覆盖全部带爆炸的武器参数，确保没有任何组合在 d=0 / d=radius 处产生 NaN。
            foreach (WeaponStats stats in WeaponCatalog.All)
            {
                if (!stats.HasExplosion)
                    continue;

                float radius = ExplosionResolver.Radius(stats.ExplosionSize);
                var targets = new List<ExplosionTarget>
                {
                    new ExplosionTarget(0f, 0f, true),       // 爆心 d=0
                    new ExplosionTarget(0f, radius, true),   // 恰好边缘
                    new ExplosionTarget(0f, -radius, true),  // 负方向边缘
                };

                ExplosionResult result = ExplosionResolver.Resolve(
                    stats.ExplosionSize, stats.ExplosionMaxDamage, 0f, 0f, targets);

                Assert.AreEqual(3, result.HitCount, stats.DisplayName);
                for (int i = 0; i < result.Hits.Length; i++)
                {
                    Assert.IsFalse(float.IsNaN(result.Hits[i].Damage), stats.DisplayName);
                    Assert.IsFalse(float.IsNaN(result.Hits[i].DeltaVx), stats.DisplayName);
                    Assert.IsFalse(float.IsNaN(result.Hits[i].DeltaVy), stats.DisplayName);
                    Assert.IsFalse(float.IsNaN(result.Hits[i].DeltaVUp), stats.DisplayName);
                    Assert.GreaterOrEqual(result.Hits[i].Damage, 0f, stats.DisplayName);
                }
            }
        }

        // ------------------------------------------------------------------
        // 火药桶连锁（§5.2 / §5.3）
        // ------------------------------------------------------------------

        [Test]
        public void GunpowderBarrel_Chain_ProducesExplosionDamage()
        {
            // 连锁链：boulder/其它命中火药桶 → DetonateFromBlast（判定见 ProjectileTriggerRulesTests）
            // → 火药桶以 size=150, maxDamage=30 再结算一次。
            // 本测试锁定：火药桶的爆炸参数（150,30）在半径内确实造成伤害。
            Assert.IsTrue(ProjectileTriggerRules.CanChainFromBlast(
                WeaponCatalog.Get(WeaponId.GunpowderBarrel).Trigger));

            // radius = 150/2 + 20 = 95；目标距离 10px → falloff = 1 - 10/95 ≈ 0.8947368
            const float expected = 30f * (1f - 10f / 95f);
            var targets = new[] { new ExplosionTarget(0f, 10f, true) };
            ExplosionResult result = ExplosionResolver.Resolve(150f, 30f, 0f, 0f, targets);

            Assert.AreEqual(1, result.HitCount);
            Assert.AreEqual(expected, result.Hits[0].Damage, 1e-5f);
            Assert.Greater(result.EvilnessGain, 0f);
        }

        [Test]
        public void WoodenCrate_DoesNotChainOrExplode()
        {
            WeaponStats crate = WeaponCatalog.Get(WeaponId.WoodenCrate);
            Assert.IsFalse(crate.HasExplosion);
            Assert.IsFalse(ProjectileTriggerRules.CanChainFromBlast(crate.Trigger));
        }

        // ------------------------------------------------------------------
        // 初速逆变换（弹体运行时回读速度判定静止用）
        // ------------------------------------------------------------------

        [Test]
        public void WorldVelocityToFlash_IsInverseOfFlashVelocityToWorld()
        {
            // 3D 口径：Flash 平面 (vx, vy) → 世界 (x, 0, z)（平面分量落到 X/Z，高度不参与）；
            // vx=4, vy=10 → world (4×0.78125, 0, 10×0.78125) = (3.125, 0, 7.8125) → 逆变换回 (4, 10)。
            Vector3 world = LevelGeometry.FlashVelocityToArena(4f, 10f);
            Vector2 flash = LevelGeometry.ArenaVelocityToFlash(world);
            Assert.AreEqual(4f, flash.x, 1e-5f);
            Assert.AreEqual(10f, flash.y, 1e-5f);
        }
    }
}
