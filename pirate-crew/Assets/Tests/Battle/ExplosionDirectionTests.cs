using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Combat;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 爆炸结算的方向/分量测试（§5.3 + 交付约束 C）。3D 化后（2026-09-13）的语义：
    ///
    /// <see cref="ExplosionResolver"/> 仍在 Flash 的**平面**约定下算平面分量（单位 px/帧）：
    ///   · <c>DeltaVx</c> / <c>DeltaVy</c> = 3D 径向单位向量的**平面两分量** × 5k —— 它们落到世界的 (X, Z)；
    ///   · <c>DeltaVUp</c> = 径向竖直分量 × 5k + 原版"总是额外上抛"的 6k —— 它落到世界的 **+Y**。
    /// 2D 时代 <c>-6k</c> 是混在 <c>DeltaVy</c> 里的（配合 y 取负表达"向上"），3D 化后已拆出来，
    /// 否则"把人掀起"会被塞进纵深方向（见 docs/3D空间模型对齐.md §5）。
    ///
    /// 本测试同时断言 Flash 原始分量与合成后的 Unity 世界增量（<see cref="ToWorldDelta"/>，
    /// 与 <c>BattleController.ResolveExplosion</c> 的合成方式逐行一致）。
    /// </summary>
    [TestFixture]
    public class ExplosionDirectionTests
    {
        const float Size = 100f;        // radius = 100/2 + 20 = 70
        const float MaxDamage = 50f;
        const float Scale = LevelGeometry.FlashSpeedScale;   // 1.5625（= 1/(16×0.04)，格 1→2 单位后由 0.78125 ×2）

        static ExplosionResult ResolveOne(float cx, float cy, float tx, float ty)
        {
            var targets = new List<ExplosionTarget> { new ExplosionTarget(tx, ty, true) };
            return ExplosionResolver.Resolve(Size, MaxDamage, cx, cy, targets);
        }

        /// <summary>
        /// 复现 <c>BattleController</c> 的合成：平面两分量 → 世界 (X, Z)；<c>DeltaVUp</c> → 世界 +Y。
        /// 本文件里所有"Unity 侧"断言都走它，从而与运行时接线保持同一个口径。
        /// </summary>
        static UnityEngine.Vector3 ToWorldDelta(ExplosionHit hit)
        {
            UnityEngine.Vector3 dv = LevelGeometry.FlashVelocityDeltaToArena(hit.DeltaVx, hit.DeltaVy);
            dv.y = hit.DeltaVUp * Scale;
            return dv;
        }

        [Test]
        public void ZeroDistance_TargetGetsFullDamageAndNetUpwardLift()
        {
            // d == 0：falloff = 1, 方向取零向量, k = 0.06*1*50 = 3
            // Flash: damage = 50；平面分量全 0；抬升 = 6k = 18（原版公式的固定上抛）
            ExplosionResult result = ResolveOne(100f, 100f, 100f, 100f);
            Assert.AreEqual(1, result.HitCount);

            ExplosionHit hit = result.Hits[0];
            Assert.AreEqual(50f, hit.Damage, 1e-4f);
            Assert.AreEqual(0f, hit.DeltaVx, 1e-5f);
            Assert.AreEqual(0f, hit.DeltaVy, 1e-5f, "方向为零向量 → 平面分量无方向");
            Assert.AreEqual(18f, hit.DeltaVUp, 1e-4f, "3D 抬升项 = 6k（原版固定上抛）");

            UnityEngine.Vector3 dv = ToWorldDelta(hit);
            Assert.AreEqual(0f, dv.x, 1e-5f);
            Assert.AreEqual(0f, dv.z, 1e-5f);
            Assert.AreEqual(28.125f, dv.y, 1e-4f, "18（Flash px/帧）× FlashSpeedScale 1.5625 = 28.125");
            Assert.Greater(dv.y, 0f, "爆炸必须把人往上掀（世界 +Y）");
        }

        [Test]
        public void TargetToTheRight_KnockbackPushesRightAndUp()
        {
            // d = 20, radius = 70, falloff = 1 - 20/70 = 5/7
            // k = 0.06 * (5/7) * 50 = 15/7
            // Flash: 平面分量 deltaVx = 5k = 75/7（右）、deltaVy = 0（目标与爆心同纵深）；抬升 deltaVUp = 6k = 90/7
            ExplosionResult result = ResolveOne(100f, 100f, 120f, 100f);
            ExplosionHit hit = result.Hits[0];

            Assert.AreEqual(250f / 7f, hit.Damage, 1e-4f);          // 35.714286
            Assert.AreEqual(75f / 7f, hit.DeltaVx, 1e-4f);           // Flash 正 x = 右
            Assert.AreEqual(0f, hit.DeltaVy, 1e-5f);                 // 同纵深 → 无纵深分量
            Assert.AreEqual(90f / 7f, hit.DeltaVUp, 1e-4f);          // 6k = 固定上抛

            UnityEngine.Vector3 dv = ToWorldDelta(hit);
            Assert.AreEqual(75f / 7f * Scale, dv.x, 1e-4f);          // 8.370536（右）
            Assert.AreEqual(0f, dv.z, 1e-5f);
            Assert.AreEqual(90f / 7f * Scale, dv.y, 1e-4f);          // 10.044643（上）
            Assert.Greater(dv.x, 0f);
            Assert.Greater(dv.y, 0f);
        }

        [Test]
        public void TargetFartherInDepth_PushesAwayAlongDepth()
        {
            // 目标在爆心的"更远纵深"侧（Flash 平面 y=120 > 100）：ny = +1
            // Flash: 平面纵深分量 deltaVy = 1*5k = 75/7；抬升 deltaVUp = 6k = 90/7
            ExplosionResult result = ResolveOne(100f, 100f, 100f, 120f);
            ExplosionHit hit = result.Hits[0];

            Assert.AreEqual(75f / 7f, hit.DeltaVy, 1e-4f);
            UnityEngine.Vector3 dv = ToWorldDelta(hit);
            Assert.AreEqual(75f / 7f * Scale, dv.z, 1e-4f, "平面纵深分量应落到世界 Z");
            Assert.AreEqual(90f / 7f * Scale, dv.y, 1e-4f);
            Assert.Greater(dv.y, 0f, "即使目标在爆心的纵深一侧，原版公式的额外上抛仍使净增量向上");
        }

        [Test]
        public void TargetRaisedInHeight_LiftsHarderThanPlanarTarget()
        {
            // 目标在爆心正上方 h = 20（世界 +Y）：径向竖直分量 nUp = +1
            // Flash: 平面分量全 0；抬升 deltaVUp = 1*5k + 6k = 11k = 165/7 ≈ 23.5714
            // —— 与"平面内 d=20 的目标"（DeltaVUp = 6k）相比，抬升更强（径向项叠加）。
            var targets = new List<ExplosionTarget> { new ExplosionTarget(100f, 100f, 20f, true) };
            ExplosionResult result = ExplosionResolver.Resolve(
                Size, MaxDamage, 100f, 100f, centerHeight: 0f, targets);

            Assert.AreEqual(1, result.HitCount);
            ExplosionHit hit = result.Hits[0];
            Assert.AreEqual(0f, hit.DeltaVx, 1e-5f);
            Assert.AreEqual(0f, hit.DeltaVy, 1e-5f);
            Assert.AreEqual(165f / 7f, hit.DeltaVUp, 1e-4f);
            Assert.Greater(ToWorldDelta(hit).y, 90f / 7f * Scale, "高度差贡献的竖直击退使抬升更强");
        }

        [Test]
        public void TargetOutsideRadius_IsNotHit()
        {
            // d = 100 > radius = 70 → 不命中。
            ExplosionResult result = ResolveOne(100f, 100f, 200f, 100f);
            Assert.AreEqual(0, result.HitCount);
        }

        [Test]
        public void EvilnessGain_EqualsSumOfFalloff()
        {
            // 两个目标：d=0 → falloff 1；d=20 → falloff 5/7；合计 12/7 ≈ 1.714286。
            var targets = new List<ExplosionTarget>
            {
                new ExplosionTarget(100f, 100f, true),
                new ExplosionTarget(120f, 100f, true),
            };
            ExplosionResult result = ExplosionResolver.Resolve(Size, MaxDamage, 100f, 100f, targets);
            Assert.AreEqual(12f / 7f, result.EvilnessGain, 1e-4f);
        }

        [Test]
        public void DeadTarget_IsSkipped()
        {
            var targets = new List<ExplosionTarget> { new ExplosionTarget(100f, 100f, false) };
            ExplosionResult result = ExplosionResolver.Resolve(Size, MaxDamage, 100f, 100f, targets);
            Assert.AreEqual(0, result.HitCount);
            Assert.AreEqual(0f, result.EvilnessGain, 1e-6f);
        }

        [Test]
        public void BattleFlowRules_ScoreCall_MatchesScoreRules()
        {
            // "得分调用"：BattleFlowRules 只是 ScoreRules 的统一入口，二者必须一致。
            Assert.AreEqual(
                ScoreRules.LevelScore(73.5f, 4, 4),
                BattleFlowRules.ComputeLevelScore(73.5f, 4, 4));
        }
    }
}
