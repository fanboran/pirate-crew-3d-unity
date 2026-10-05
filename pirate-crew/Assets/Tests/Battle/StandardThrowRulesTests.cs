using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// StandardThrowRules 测试 = 投掷行为契约 #3/#8/#9 的可执行形式：
    /// 三参数步进与夹取、米制初速合成、落点终止采样。数值全部【提案/待定】的契约钉死。
    /// </summary>
    [TestFixture]
    public class StandardThrowRulesTests
    {
        const float Eps = 1e-4f;

        // ------------------------------------------------------------------
        // 参数步进与夹取（契约 #3）
        // ------------------------------------------------------------------

        [Test]
        public void Advance_YawDirectionSign_DIsClockwise()
        {
            // D = +1 = 顺时针（俯视 Unity yaw 正向），A = -1 = 逆时针
            var p = StandardThrowRules.Initial(0f);
            var d = StandardThrowRules.Advance(p, yawInput: 1, elevationInput: 0, powerInput: 0, dt: 1f);
            var a = StandardThrowRules.Advance(p, yawInput: -1, elevationInput: 0, powerInput: 0, dt: 1f);
            Assert.AreEqual(StandardThrowRules.YawRateDegreesPerSecond, d.YawDegrees, Eps);
            Assert.AreEqual(-StandardThrowRules.YawRateDegreesPerSecond, a.YawDegrees, Eps);
        }

        [Test]
        public void Advance_ElevationClampedToContractRange()
        {
            var p = new ThrowParams(0f, StandardThrowRules.ElevationMaxDegrees, 0.5f);
            var up = StandardThrowRules.Advance(p, 0, 1, 0, dt: 1f);
            Assert.AreEqual(StandardThrowRules.ElevationMaxDegrees, up.ElevationDegrees, Eps, "仰角夹上限");

            var low = new ThrowParams(0f, StandardThrowRules.ElevationMinDegrees, 0.5f);
            var down = StandardThrowRules.Advance(low, 0, -1, 0, dt: 1f);
            Assert.AreEqual(StandardThrowRules.ElevationMinDegrees, down.ElevationDegrees, Eps, "仰角夹下限");
        }

        [Test]
        public void Advance_PowerClamped_NeverBelowMinPower()
        {
            var p = new ThrowParams(0f, 35f, StandardThrowRules.MinPower);
            var down = StandardThrowRules.Advance(p, 0, 0, -1, dt: 10f);
            Assert.AreEqual(StandardThrowRules.MinPower, down.Power, Eps, "力度下限=误触保护");

            var maxed = new ThrowParams(0f, 35f, StandardThrowRules.MaxPower);
            var over = StandardThrowRules.Advance(maxed, 0, 0, 1, dt: 10f);
            Assert.AreEqual(StandardThrowRules.MaxPower, over.Power, Eps, "力度夹上限 1");
        }

        [Test]
        public void Advance_RatesMatchContract()
        {
            var p = StandardThrowRules.Initial(0f);
            var q = StandardThrowRules.Advance(p, 0, 1, 1, dt: 0.04f);
            Assert.AreEqual(35f + 45f * 0.04f, q.ElevationDegrees, Eps);
            Assert.AreEqual(0.6f + 0.45f * 0.04f, q.Power, Eps);
        }

        [Test]
        public void Initial_UsesContractDefaults_AndInheritsYaw()
        {
            var p = StandardThrowRules.Initial(123.5f);
            Assert.AreEqual(123.5f, p.YawDegrees, Eps, "方向角继承玩家面向");
            Assert.AreEqual(StandardThrowRules.DefaultElevationDegrees, p.ElevationDegrees, Eps);
            Assert.AreEqual(StandardThrowRules.DefaultPower, p.Power, Eps);
        }

        // ------------------------------------------------------------------
        // 初速合成（契约 #8）
        // ------------------------------------------------------------------

        [Test]
        public void LaunchDirection_CardinalAxes()
        {
            AssertDirection(Vector3.forward, StandardThrowRules.LaunchDirection(0f, 0f), "yaw0/elev0 → +Z");
            AssertDirection(Vector3.right, StandardThrowRules.LaunchDirection(90f, 0f), "yaw90/elev0 → +X（俯视顺时针）");
            AssertDirection(Vector3.up, StandardThrowRules.LaunchDirection(0f, 90f), "elev90 → +Y");
            AssertDirection(-Vector3.forward, StandardThrowRules.LaunchDirection(180f, 0f));
        }

        static void AssertDirection(Vector3 expected, Vector3 actual, string message = null)
        {
            // 三角构造的基数轴有 ~1e-7 尾差，用分量容差比较（Vector3== 是逐位精确比较）。
            Assert.LessOrEqual(Mathf.Abs(expected.x - actual.x), 1e-5f, message);
            Assert.LessOrEqual(Mathf.Abs(expected.y - actual.y), 1e-5f, message);
            Assert.LessOrEqual(Mathf.Abs(expected.z - actual.z), 1e-5f, message);
        }

        [Test]
        public void LaunchDirection_AlwaysUnitLength_ForAllYawAndElevation()
        {
            for (float yaw = 0f; yaw < 360f; yaw += 17f)
            {
                for (float elev = StandardThrowRules.ElevationMinDegrees;
                     elev <= StandardThrowRules.ElevationMaxDegrees; elev += 9f)
                {
                    Vector3 dir = StandardThrowRules.LaunchDirection(yaw, elev);
                    Assert.AreEqual(1f, dir.magnitude, 1e-3f,
                        $"yaw={yaw} elev={elev} 方向必须归一（仰角不改模长）");
                    Assert.IsTrue(dir.y >= -1e-3f, "仰角为正方向必向上");
                }
            }
        }

        [Test]
        public void LaunchSpeed_IsPowerTimesMax_IndependentOfDirection()
        {
            var p = new ThrowParams(37f, 62f, 0.5f);
            Assert.AreEqual(0.5f * StandardThrowRules.MaxLaunchSpeed,
                StandardThrowRules.LaunchSpeed(p), Eps);

            var q = new ThrowParams(200f, 12f, 1f);
            Assert.AreEqual(StandardThrowRules.MaxLaunchSpeed, StandardThrowRules.LaunchSpeed(q), Eps);
        }

        [Test]
        public void LaunchVelocity_MagnitudeAndDecomposition()
        {
            var p = new ThrowParams(0f, 30f, 1f);
            Vector3 v = StandardThrowRules.LaunchVelocity(p);
            Assert.AreEqual(StandardThrowRules.MaxLaunchSpeed, v.magnitude, 1e-3f);
            // 仰角 30°：竖直分量 = v·sin30，水平分量 = v·cos30 沿 +Z
            Assert.AreEqual(StandardThrowRules.MaxLaunchSpeed * 0.5f, v.y, 1e-3f);
            Assert.AreEqual(StandardThrowRules.MaxLaunchSpeed * Mathf.Cos(30f * Mathf.Deg2Rad), v.z, 1e-3f);
            Assert.AreEqual(0f, v.x, Eps);
        }

        [Test]
        public void MinPower_GivesNonZeroSpeed_NoZeroVectorLaunch()
        {
            var p = new ThrowParams(0f, 35f, StandardThrowRules.MinPower);
            Assert.Greater(StandardThrowRules.LaunchSpeed(p), 0.5f, "误触保护：最小力度也有可观初速");
        }

        [Test]
        public void ThrowOrigin_RaisedByThrowOriginHeight()
        {
            Vector3 origin = StandardThrowRules.ThrowOrigin(new Vector3(3f, 0.25f, -2f));
            Assert.AreEqual(new Vector3(3f, 0.25f + StandardThrowRules.ThrowOriginHeight, -2f), origin);
        }

        // ------------------------------------------------------------------
        // 落点终止采样（契约 #9，ThrowTrajectory.TryPredictUntilImpact）
        // ------------------------------------------------------------------

        [Test]
        public void PredictUntilImpact_AllSamplesAtOrAboveGround_ImpactOnGround()
        {
            var p = new ThrowParams(45f, 35f, 1f);
            Vector3 origin = new Vector3(0f, StandardThrowRules.ThrowOriginHeight, 0f);
            var buffer = new Vector3[StandardThrowRules.PreviewMaxSteps];

            bool landed = ThrowTrajectory.TryPredictUntilImpact(
                origin, StandardThrowRules.LaunchVelocity(p), StandardThrowRules.LaunchGravityY,
                buffer, out int count, out Vector3 impact);

            Assert.IsTrue(landed, "满力 35° 仰角必在兜底步数内穿地");
            Assert.Greater(count, 1);
            Assert.AreEqual(0f, impact.y, 1e-3f, "落点恰在地面");
            for (int i = 0; i < count; i++)
                Assert.GreaterOrEqual(buffer[i].y, -1e-3f, $"采样点 {i} 不得入地");

            // 采样首点与「预览=实弹」的半隐式欧拉逐步一致（同一积分格式）
            Vector3 v = StandardThrowRules.LaunchVelocity(p);
            Vector3 step = origin + new Vector3(
                v.x * 0.04f,
                (v.y + StandardThrowRules.LaunchGravityY * 0.04f) * 0.04f,
                v.z * 0.04f);
            Assert.AreEqual(step, buffer[0]);
        }

        [Test]
        public void PredictUntilImpact_MinPowerStillLands()
        {
            var p = new ThrowParams(0f, StandardThrowRules.ElevationMinDegrees, StandardThrowRules.MinPower);
            Vector3 origin = new Vector3(0f, StandardThrowRules.ThrowOriginHeight, 0f);
            var buffer = new Vector3[StandardThrowRules.PreviewMaxSteps];

            bool landed = ThrowTrajectory.TryPredictUntilImpact(
                origin, StandardThrowRules.LaunchVelocity(p), StandardThrowRules.LaunchGravityY,
                buffer, out _, out Vector3 impact);

            Assert.IsTrue(landed, "最小力度 10° 仰角也应在兜底步数内落地");
            Assert.AreEqual(0f, impact.y, 1e-3f);
        }

        [Test]
        public void PredictUntilImpact_HorizontalEscape_HitsCapWithoutImpact()
        {
            // 兜底语义：永不穿地的轨迹（如水平速度且起点就在地面高度）返回 false，不写落点
            var buffer = new Vector3[8];
            bool landed = ThrowTrajectory.TryPredictUntilImpact(
                new Vector3(0f, 0.5f, 0f), new Vector3(100f, 0f, 0f), 0f,
                buffer, out int count, out Vector3 impact,
                groundY: 0f, maxSteps: 8);

            Assert.IsFalse(landed, "零重力水平直线永不穿地");
            Assert.AreEqual(8, count, "写满兜底步数");
            Assert.AreEqual(Vector3.zero, impact);
        }

        [Test]
        public void PredictUntilImpact_StepEquivalence_WithPlainPredict()
        {
            // 落点前的前 N-1 个采样与 Predict（同一初速/重力/dt）逐步一致——不变量「预览=实弹」的采样等价
            var p = new ThrowParams(90f, 45f, 0.8f);
            Vector3 origin = new Vector3(1f, StandardThrowRules.ThrowOriginHeight, 2f);
            Vector3 v0 = StandardThrowRules.LaunchVelocity(p);
            var untilBuffer = new Vector3[StandardThrowRules.PreviewMaxSteps];
            var plainBuffer = new Vector3[64];

            ThrowTrajectory.TryPredictUntilImpact(
                origin, v0, StandardThrowRules.LaunchGravityY, untilBuffer, out int count, out _);
            ThrowTrajectory.Predict(origin, v0, StandardThrowRules.LaunchGravityY, plainBuffer, 64);

            Assert.Less(count, 64, "45° 仰角 0.8 力度 64 步内应已落地");
            for (int i = 0; i < count - 1; i++)
                Assert.AreEqual(plainBuffer[i], untilBuffer[i], $"第 {i} 步应与裸积分一致");
        }
    }
}
