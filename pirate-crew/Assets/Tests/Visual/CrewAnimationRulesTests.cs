using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Visual.Tests
{
    /// <summary>
    /// <see cref="CrewAnimationRules"/> 断言：状态时长 / 曲线 / 幅度按 docs/角色造型规范.md §4 表
    /// （该表标【AI 提案】，锚定原版死亡/落水/受击行为）。
    /// 纯 C#，可无头跑。
    /// </summary>
    [TestFixture]
    public class CrewAnimationRulesTests
    {
        const float Epsilon = 1e-4f;

        [Test]
        public void Durations_MatchSpecTable()
        {
            Assert.AreEqual(2.8f, CrewAnimationRules.BreathPeriod, "待机呼吸 2.8s");
            Assert.AreEqual(0.40f, CrewAnimationRules.MoveStepSeconds, "步频 0.40s/步");
            Assert.AreEqual(0.25f, CrewAnimationRules.ThrowChargeSeconds, "蓄力 0.25s");
            Assert.AreEqual(0.10f, CrewAnimationRules.ThrowReleaseSeconds, "释放 0.10s");
            Assert.AreEqual(0.30f, CrewAnimationRules.ThrowRecoverSeconds, "恢复 0.30s");
            Assert.AreEqual(0.08f, CrewAnimationRules.HitFlashSeconds, "白闪 0.08s");
            Assert.AreEqual(0.25f, CrewAnimationRules.HitTotalSeconds, "受击总计 0.25s");
            Assert.AreEqual(0.5f, CrewAnimationRules.DeathFallSeconds, "倒地 0.5s");
            Assert.AreEqual(0.5f, CrewAnimationRules.DeathFadeSeconds, "淡出 0.5s");
            Assert.AreEqual(0.6f, CrewAnimationRules.DrownSinkSpeed, "下沉 0.6 单位/秒");
            Assert.AreEqual(8f, CrewAnimationRules.DrownSwayDegrees, "落水摇晃 8°");
            Assert.AreEqual(1.5f, CrewAnimationRules.DrownSecondsMin, "落水 1.5s 起");
            Assert.AreEqual(2.0f, CrewAnimationRules.DrownSecondsMax, "落水 2.0s 止");
        }

        [Test]
        public void Amplitudes_MatchSpec()
        {
            Assert.AreEqual(0.015f, CrewAnimationRules.BreathTorsoScale, "呼吸躯干 ±1.5%");
            // 2026-09-14 角色总高 0.55→1.85（Godot 真比例）：幅度等比放大 3.36×。
            Assert.AreEqual(0.0134f, CrewAnimationRules.BreathHeadOffset, 1e-5f, "呼吸头 ±0.0134");
            Assert.AreEqual(0.040f, CrewAnimationRules.MoveBobOffset, 1e-5f, "移动 bob ±0.040");
            Assert.AreEqual(8f, CrewAnimationRules.MoveLeanDegrees, "移动前倾 8°（规格 6-10° 取中值）");
            Assert.AreEqual(-8f, CrewAnimationRules.ThrowChargeLeanDegrees, "蓄力后仰 8°");
            Assert.AreEqual(12f, CrewAnimationRules.ThrowReleaseLeanDegrees, "释放前倾 12°");
            Assert.AreEqual(5f, CrewAnimationRules.HitRecoilDegrees, "受击后仰 5°");
        }

        [Test]
        public void Breath_OscillatesWithinAmplitude()
        {
            for (float t = 0f; t < 6f; t += 0.05f)
            {
                float scale = CrewAnimationRules.BreathTorsoScaleY(t, 0);
                Assert.That(scale, Is.InRange(1f - 0.015f - Epsilon, 1f + 0.015f + Epsilon), "呼吸缩放幅度");

                float head = CrewAnimationRules.BreathHeadOffsetY(t, 0);
                Assert.That(Mathf.Abs(head), Is.LessThanOrEqualTo(0.0134f + Epsilon), "呼吸头位移幅度");
            }
        }

        [Test]
        public void IdlePhaseOffset_DesyncsProfessions()
        {
            // 规格 §4：不同职业错相位 0.5-1.0s。
            float a = CrewAnimationRules.BreathTorsoScaleY(0f, 0);
            float b = CrewAnimationRules.BreathTorsoScaleY(0f, 1);
            Assert.AreNotEqual(a, b, "相邻职业待机相位应错开");
        }

        [Test]
        public void MoveBob_IsNonNegativeAndBounded()
        {
            for (float t = 0f; t < 2f; t += 0.02f)
            {
                float bob = CrewAnimationRules.MoveBob(t);
                Assert.That(bob, Is.InRange(-Epsilon, 0.040f + Epsilon));
            }
            Assert.That(CrewAnimationRules.MoveBob(0f), Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void WalkSwing_LeftAndRightAreOpposite()
        {
            float left = CrewAnimationRules.WalkLegSwing(0.1f, 1f);
            float right = CrewAnimationRules.WalkLegSwing(0.1f, -1f);
            Assert.AreEqual(-left, right, 1e-5f, "左右腿反相");

            for (float t = 0f; t < 2f; t += 0.02f)
            {
                Assert.That(Mathf.Abs(CrewAnimationRules.WalkLegSwing(t, 1f)),
                    Is.LessThanOrEqualTo(26f + Epsilon), "腿摆幅 ≤ 26°");
            }
        }

        [Test]
        public void ThrowLean_GoesBackThenForwardThenNeutral()
        {
            Assert.That(CrewAnimationRules.ThrowLeanDegrees(CrewVisualState.ThrowCharge, 0f),
                Is.EqualTo(0f).Within(Epsilon));
            Assert.That(CrewAnimationRules.ThrowLeanDegrees(CrewVisualState.ThrowCharge, 0.25f),
                Is.EqualTo(-8f).Within(Epsilon), "蓄力末后仰 8°");
            Assert.That(CrewAnimationRules.ThrowLeanDegrees(CrewVisualState.ThrowRelease, 0f),
                Is.EqualTo(-8f).Within(Epsilon), "释放起 = 蓄力末");
            Assert.That(CrewAnimationRules.ThrowLeanDegrees(CrewVisualState.ThrowRelease, 0.10f),
                Is.EqualTo(12f).Within(Epsilon), "释放末前倾 12°");
            Assert.That(CrewAnimationRules.ThrowLeanDegrees(CrewVisualState.ThrowRecover, 0.30f),
                Is.EqualTo(0f).Within(Epsilon), "恢复回中");
        }

        [Test]
        public void ThrowArm_SwingsBackThenForwards()
        {
            Assert.That(CrewAnimationRules.ThrowArmDegrees(CrewVisualState.ThrowCharge, 0.25f),
                Is.EqualTo(70f).Within(0.5f));
            Assert.That(CrewAnimationRules.ThrowArmDegrees(CrewVisualState.ThrowRelease, 0.10f),
                Is.EqualTo(-55f).Within(0.5f));
        }

        [Test]
        public void HeldItemReleasedAtReleaseFrame()
        {
            Assert.IsFalse(CrewAnimationRules.IsHeldItemReleased(CrewVisualState.ThrowCharge, 0.24f));
            Assert.IsTrue(CrewAnimationRules.IsHeldItemReleased(CrewVisualState.ThrowRelease, 0.05f),
                "释放帧后手持物脱手（R-7）");
            Assert.IsTrue(CrewAnimationRules.IsHeldItemReleased(CrewVisualState.ThrowRecover, 0f));
        }

        [Test]
        public void HitFlash_DecaysOverPointOhEightSeconds()
        {
            Assert.That(CrewAnimationRules.HitFlashStrength(0f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(CrewAnimationRules.HitFlashStrength(0.04f), Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(CrewAnimationRules.HitFlashStrength(0.08f), Is.EqualTo(0f).Within(Epsilon));
            Assert.That(CrewAnimationRules.HitFlashStrength(0.20f), Is.EqualTo(0f).Within(Epsilon));
            Assert.That(CrewAnimationRules.HitRecoilDegreesAt(0f), Is.EqualTo(5f).Within(Epsilon));
            Assert.That(CrewAnimationRules.HitRecoilDegreesAt(0.25f), Is.EqualTo(0f).Within(Epsilon));
        }

        [Test]
        public void DeathFall_ReachesNinetyDegrees()
        {
            Assert.That(CrewAnimationRules.DeathFallAngle(0f), Is.EqualTo(0f).Within(Epsilon));
            Assert.That(CrewAnimationRules.DeathFallAngle(0.5f), Is.EqualTo(90f).Within(Epsilon));
            Assert.That(CrewAnimationRules.DeathFallAngle(1f), Is.EqualTo(90f).Within(Epsilon), "倒地后保持");
        }

        [Test]
        public void FadeScale_ShrinksToMinimum()
        {
            Assert.That(CrewAnimationRules.FadeScale(0f, 0.5f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(CrewAnimationRules.FadeScale(0.5f, 0.5f), Is.EqualTo(0.15f).Within(Epsilon));
            Assert.That(CrewAnimationRules.FadeScale(2f, 0.5f), Is.EqualTo(0.15f).Within(Epsilon));
        }

        [Test]
        public void Drown_SinksAtSpecSpeedAndSways()
        {
            Assert.That(CrewAnimationRules.DrownSinkOffset(1f), Is.EqualTo(-0.6f).Within(Epsilon));
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                Assert.That(Mathf.Abs(CrewAnimationRules.DrownSwayDegreesAt(t)),
                    Is.LessThanOrEqualTo(12f + Epsilon), "摇晃 ≤ 8+4°");
            }
            Assert.That(CrewAnimationRules.DrownProgress(1.0f, 2.0f), Is.EqualTo(0.5f).Within(Epsilon));
        }

        [Test]
        public void StateTiming_IsFinishedAtDuration()
        {
            Assert.IsFalse(CrewAnimationRules.IsFinished(CrewVisualState.ThrowCharge, 0.24f));
            Assert.IsTrue(CrewAnimationRules.IsFinished(CrewVisualState.ThrowCharge, 0.25f));
            Assert.IsFalse(CrewAnimationRules.IsFinished(CrewVisualState.Idle, 999f), "无限状态不判完成");
            Assert.AreEqual(0.65f, CrewAnimationRules.ThrowTotalSeconds, 1e-4f);
            Assert.AreEqual(1.0f, CrewAnimationRules.DeathTotalSeconds, 1e-4f);
        }

        [Test]
        public void SmoothStep_IsClamped()
        {
            Assert.That(CrewAnimationRules.SmoothStep(-1f), Is.EqualTo(0f).Within(Epsilon));
            Assert.That(CrewAnimationRules.SmoothStep(0f), Is.EqualTo(0f).Within(Epsilon));
            Assert.That(CrewAnimationRules.SmoothStep(0.5f), Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(CrewAnimationRules.SmoothStep(1f), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(CrewAnimationRules.SmoothStep(2f), Is.EqualTo(1f).Within(Epsilon));
        }
    }
}
