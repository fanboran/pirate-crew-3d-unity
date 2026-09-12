using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 爆炸结算的坐标/方向翻转测试（§5.3 + 交付约束 C）。
    ///
    /// <see cref="ExplosionResolver"/> 在 Flash 约定下工作（y 向下、单位 px）：
    /// <c>deltaVy = ny*5k - 6k</c>，其中 <c>-6k</c> 表示<b>向上</b>。
    /// 接入 Unity 前必须用 <see cref="LevelGeometry.FlashVelocityDeltaToWorld"/> 做 y 取负，
    /// 使"向上"在 Unity 里仍是 +Y。本测试同时断言 Flash 原始值与翻转后的 Unity 值。
    /// </summary>
    [TestFixture]
    public class ExplosionDirectionTests
    {
        const float Size = 100f;        // radius = 100/2 + 20 = 70
        const float MaxDamage = 50f;
        const float Scale = LevelGeometry.FlashSpeedScale;   // 0.78125

        static ExplosionResult ResolveOne(float cx, float cy, float tx, float ty)
        {
            var targets = new List<ExplosionTarget> { new ExplosionTarget(tx, ty, true) };
            return ExplosionResolver.Resolve(Size, MaxDamage, cx, cy, targets);
        }

        [Test]
        public void ZeroDistance_TargetGetsFullDamageAndNetUpwardLift()
        {
            // d == 0：falloff = 1, nx = ny = 0, k = 0.06*1*50 = 3
            // Flash: damage = 50；deltaVy = 0 - 6*3 = -18（负 y = 向上）
            ExplosionResult result = ResolveOne(100f, 100f, 100f, 100f);
            Assert.AreEqual(1, result.HitCount);

            ExplosionHit hit = result.Hits[0];
            Assert.AreEqual(50f, hit.Damage, 1e-4f);
            Assert.AreEqual(0f, hit.DeltaVx, 1e-5f);
            Assert.AreEqual(-18f, hit.DeltaVy, 1e-4f, "Flash 约定：-6k = 向上");

            // Unity 换算：y 取负 → +14.0625（向上）
            UnityEngine.Vector3 dv = LevelGeometry.FlashVelocityDeltaToWorld(hit.DeltaVx, hit.DeltaVy);
            Assert.AreEqual(0f, dv.x, 1e-5f);
            Assert.AreEqual(14.0625f, dv.y, 1e-4f);
            Assert.Greater(dv.y, 0f, "Flash 的向上增量翻到 Unity 必须仍是 +Y 向上");
        }

        [Test]
        public void TargetToTheRight_KnockbackPushesRightAndUp()
        {
            // d = 20, radius = 70, falloff = 1 - 20/70 = 5/7
            // k = 0.06 * (5/7) * 50 = 15/7
            // Flash: deltaVx = 5k = 75/7 ≈ 10.714286；deltaVy = -6k = -90/7 ≈ -12.857143
            ExplosionResult result = ResolveOne(100f, 100f, 120f, 100f);
            ExplosionHit hit = result.Hits[0];

            Assert.AreEqual(250f / 7f, hit.Damage, 1e-4f);          // 35.714286
            Assert.AreEqual(75f / 7f, hit.DeltaVx, 1e-4f);           // Flash 正 x = 右
            Assert.AreEqual(-90f / 7f, hit.DeltaVy, 1e-4f);          // Flash 负 y = 上

            UnityEngine.Vector3 dv = LevelGeometry.FlashVelocityDeltaToWorld(hit.DeltaVx, hit.DeltaVy);
            Assert.AreEqual(75f / 7f * Scale, dv.x, 1e-4f);          // 8.370536（右）
            Assert.AreEqual(90f / 7f * Scale, dv.y, 1e-4f);          // 10.044643（上）
            Assert.Greater(dv.x, 0f);
            Assert.Greater(dv.y, 0f);
        }

        [Test]
        public void TargetBelowCenter_StillHasNetUpwardLift()
        {
            // 目标在 Flash 里更靠下（y=120 > 100）：ny = +1（向下）。
            // deltaVy = 1*5k - 6k = -k = -15/7（净向上）。
            // Unity：y 取负 → +15/7 * 0.78125 ≈ 1.674107（向上）。
            ExplosionResult result = ResolveOne(100f, 100f, 100f, 120f);
            ExplosionHit hit = result.Hits[0];

            Assert.AreEqual(-15f / 7f, hit.DeltaVy, 1e-4f);
            UnityEngine.Vector3 dv = LevelGeometry.FlashVelocityDeltaToWorld(hit.DeltaVx, hit.DeltaVy);
            Assert.AreEqual(15f / 7f * Scale, dv.y, 1e-4f);
            Assert.Greater(dv.y, 0f, "即使目标在爆心下方，原版公式的额外上抛仍使净增量向上");
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
