using NUnit.Framework;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// <see cref="UiMotionRules"/>（HUD 动效纯逻辑）的曲线与档位测试。
    /// 数值常量本身是 AI 提案/待定，这里守的是曲线的**数学性质**（端点、单调性、有界过冲）
    /// 与档位的相对关系（离开比进入快等设计口径），不钉死具体时长。
    /// </summary>
    public sealed class UiMotionRulesTests
    {
        const float Eps = 1e-4f;

        // ------------------------------------------------------------------
        // punch 曲线
        // ------------------------------------------------------------------

        [Test]
        public void PunchCurve_StartsAtFromScale_AndEndsExactlyAtOne()
        {
            Assert.AreEqual(0.90f, UiMotionRules.PunchCurve(0f, UiMotionRules.PunchFromScale, UiMotionRules.PunchOvershootScale), Eps);
            Assert.AreEqual(1f, UiMotionRules.PunchCurve(1f, UiMotionRules.PunchFromScale, UiMotionRules.PunchOvershootScale), Eps);
        }

        [Test]
        public void PunchCurve_NeverExceedsOvershoot_NorDipsBelowFrom()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i <= 100; i++)
            {
                float v = UiMotionRules.PunchCurve(i / 100f, UiMotionRules.PunchFromScale, UiMotionRules.PunchOvershootScale);
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }

            Assert.GreaterOrEqual(min, UiMotionRules.PunchFromScale - Eps, "曲线不得低于起始缩放");
            Assert.LessOrEqual(max, UiMotionRules.PunchOvershootScale + Eps, "过冲不得超出设计上限");
            Assert.Greater(max, 1f + Eps, "设计上必须有可见的过冲（否则 punch 退化为普通缓动）");
        }

        [Test]
        public void PunchCurve_PhaseOneRises_PhaseTwoSettles()
        {
            // 峰位前单调升、峰位后单调降回 1
            float prev = UiMotionRules.PunchCurve(0f, 0.9f, 1.06f);
            for (int i = 1; i <= 40; i++)
            {
                float t = i / 100f;
                if (t >= UiMotionRules.PunchPeakT) break;
                float v = UiMotionRules.PunchCurve(t, 0.9f, 1.06f);
                Assert.GreaterOrEqual(v, prev - Eps, $"前段应单调升：t={t}");
                prev = v;
            }

            Assert.AreEqual(1.06f, UiMotionRules.PunchCurve(UiMotionRules.PunchPeakT, 0.9f, 1.06f), Eps,
                "峰位处应恰好到达过冲值");

            prev = UiMotionRules.PunchCurve(UiMotionRules.PunchPeakT, 0.9f, 1.06f);
            for (int i = 41; i <= 100; i++)
            {
                float t = i / 100f;
                float v = UiMotionRules.PunchCurve(t, 0.9f, 1.06f);
                Assert.LessOrEqual(v, prev + Eps, $"后段应单调回稳：t={t}");
                prev = v;
            }
        }

        [Test]
        public void PunchCurve_ClampsInputOutOfRange()
        {
            Assert.AreEqual(UiMotionRules.PunchCurve(0f, 0.9f, 1.06f),
                UiMotionRules.PunchCurve(-0.5f, 0.9f, 1.06f), Eps);
            Assert.AreEqual(1f, UiMotionRules.PunchCurve(1.5f, 0.9f, 1.06f), Eps);
        }

        // ------------------------------------------------------------------
        // 面板缓动
        // ------------------------------------------------------------------

        [Test]
        public void PanelEases_EndpointsAndMonotonic()
        {
            float prevOut = 0f, prevIn = 0f;
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                float o = UiMotionRules.EaseOutCubic(t);
                float q = UiMotionRules.EaseInQuad(t);
                Assert.GreaterOrEqual(o, prevOut - Eps, $"ease-out cubic 应单调：t={t}");
                Assert.GreaterOrEqual(q, prevIn - Eps, $"ease-in quad 应单调：t={t}");
                prevOut = o;
                prevIn = q;
            }

            Assert.AreEqual(0f, UiMotionRules.EaseOutCubic(0f), Eps);
            Assert.AreEqual(1f, UiMotionRules.EaseOutCubic(1f), Eps);
            Assert.AreEqual(0f, UiMotionRules.EaseInQuad(0f), Eps);
            Assert.AreEqual(1f, UiMotionRules.EaseInQuad(1f), Eps);
            Assert.Greater(UiMotionRules.EaseOutCubic(0.5f), 0.5f, "ease-out 前段应跑在直线上方");
            Assert.Less(UiMotionRules.EaseInQuad(0.5f), 0.5f, "ease-in 前段应跑在直线下方");
        }

        // ------------------------------------------------------------------
        // 血条滚动
        // ------------------------------------------------------------------

        [Test]
        public void Approach_MovesTowardTarget_AndIsFrameRateIndependentInAggregate()
        {
            // 0.25s（speed=12）应走完约 95%（1-e^-3）
            float oneStep = UiMotionRules.ApproachExponential(0f, 1f, 0.25f, UiMotionRules.HealthDrainSpeedPerSecond);
            Assert.That(oneStep, Is.InRange(0.90f, 0.99f), "0.25s 内应走完大部分差距");

            // 同样时长拆成小步累计应落在同一量级（指数趋近对拆分不敏感度的宽松校验）
            float stepped = 0f;
            for (int i = 0; i < 15; i++)
                stepped = UiMotionRules.ApproachExponential(stepped, 1f, 0.25f / 15f, UiMotionRules.HealthDrainSpeedPerSecond);
            Assert.That(stepped, Is.InRange(oneStep - 0.03f, oneStep + 0.03f));
        }

        [Test]
        public void Approach_SnapsToTarget_WhenCloseEnough_AndGuardsBadInput()
        {
            Assert.AreEqual(1f, UiMotionRules.ApproachExponential(0.9999f, 1f, 1f / 60f, UiMotionRules.HealthDrainSpeedPerSecond),
                "小于吸附阈值时应直接归位，避免永动残影");
            Assert.AreEqual(0f, UiMotionRules.ApproachExponential(0f, 1f, -1f, UiMotionRules.HealthDrainSpeedPerSecond),
                "负 dt 不得移动");
            Assert.AreEqual(1f, UiMotionRules.ApproachExponential(0.3f, 1f, 1f, 0f), "speed<=0 应直接落位");
        }

        // ------------------------------------------------------------------
        // 档位关系（设计口径，不钉死具体数值）
        // ------------------------------------------------------------------

        [Test]
        public void TimingConstants_FollowDesignContract()
        {
            Assert.Less(UiMotionRules.PanelHideSeconds, UiMotionRules.PanelShowSeconds,
                "设计口径：离开要比进入快");
            Assert.That(UiMotionRules.PunchSeconds, Is.InRange(0.05f, 0.40f), "punch 过快看不清、过慢黏手");
            Assert.That(UiMotionRules.PanelShowSeconds, Is.InRange(0.05f, 0.50f));
            Assert.That(UiMotionRules.PanelHideSeconds, Is.InRange(0.05f, 0.50f));
            Assert.That(UiMotionRules.PanelSlideOffsetPixels, Is.InRange(8f, 64f), "滑入位移：可见但不夸张");
            Assert.That(UiMotionRules.HealthDrainSpeedPerSecond, Is.InRange(5f, 30f));
            Assert.Less(UiMotionRules.PunchFromScale, 1f);
            Assert.Greater(UiMotionRules.PunchOvershootScale, 1f);
            Assert.That(UiMotionRules.PunchPeakT, Is.InRange(0.2f, 0.6f), "过冲应发生在前半程");
        }
    }
}
