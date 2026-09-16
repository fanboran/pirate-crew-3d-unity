using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="RollRules"/> 纯逻辑测试（M4 §3.1 落地翻滚，忠实转写 Flash 逆向；
    /// 纯 C# 无 Unity 实例化依赖，可在无头验证台跑）。
    ///
    /// 换算核对基准：1 px/帧 = 1.5625 u/s（<see cref="LevelGeometry.FlashSpeedScale"/>），一帧 0.04s。
    /// 全部期望值按 Flash 出处直推：
    ///   · 空中恒转 rotation += vx*3（Character.as:151-154）→ vx=1px/帧 时 75°/s → 系数 48；
    ///   · 落地摩擦 |vx| -= 2（Solid.as:269）→ 78.125 u/s²；
    ///   · 落地弹跳 vy *= -0.2（Solid.as:263-276）；
    ///   · 接地阻尼 rotation ×= 0.5、&lt;1° 归零（Character.as:685-697）；
    ///   · 落水 rotation += (vx+vy)*4、v×0.8、vy 钳 ≥1.5 px/帧（Character.as:164-180）→ 系数 64。
    /// </summary>
    [TestFixture]
    public class RollRulesTests
    {
        const float SpeedScale = LevelGeometry.FlashSpeedScale;   // 1.5625 u/s per px/帧

        // ------------------------------------------------------------------
        // 换算表核对
        // ------------------------------------------------------------------

        [Test]
        public void SpinCoefficient_MatchesFlashOnePixelPerFrameAs75DegreesPerSecond()
        {
            // 3°/帧 × 25fps = 75°/s per (px/帧)；75 / 1.5625 = 48。
            Assert.AreEqual(75f, RollRules.SpinDegreesPerSecondPerUnitSpeed * SpeedScale, 1e-4f);
            Assert.AreEqual(48f, RollRules.SpinDegreesPerSecondPerUnitSpeed, 1e-4f);
        }

        [Test]
        public void SpinDegreesPerSecond_FullTable()
        {
            // vx = 1 px/帧 = 1.5625 u/s → 75°/s；vx = 2 px/帧 → 150°/s；负速度取大小。
            Assert.AreEqual(75f, RollRules.SpinDegreesPerSecond(1f * SpeedScale), 1e-4f);
            Assert.AreEqual(150f, RollRules.SpinDegreesPerSecond(2f * SpeedScale), 1e-4f);
            Assert.AreEqual(75f, RollRules.SpinDegreesPerSecond(-1f * SpeedScale), 1e-4f, "方向由旋转轴承载，规则层只看大小");
        }

        [Test]
        public void AdvanceRollAngle_OneFrameAtOnePixelPerFrame_TurnsThreeDegrees()
        {
            Assert.AreEqual(3f, RollRules.AdvanceRollAngle(0f, 1f * SpeedScale, LevelGeometry.FrameSeconds), 1e-4f);
            // 累计推进：两帧共 6°。
            float afterOne = RollRules.AdvanceRollAngle(0f, 1f * SpeedScale, LevelGeometry.FrameSeconds);
            Assert.AreEqual(6f, RollRules.AdvanceRollAngle(afterOne, 1f * SpeedScale, LevelGeometry.FrameSeconds), 1e-4f);
            // dt=0 不推进。
            Assert.AreEqual(7f, RollRules.AdvanceRollAngle(7f, 3f * SpeedScale, 0f), 1e-6f);
        }

        [Test]
        public void GroundDeceleration_MatchesTwoPixelsPerFramePerContactFrame()
        {
            // |vx| -= 2 px/帧 逐接触帧 → 2 × 1.5625 / 0.04 = 78.125 u/s²。
            Assert.AreEqual(78.125f, RollRules.GroundDecelerationUnitsPerSecond2, 1e-4f);
            Assert.AreEqual(78.125f, 2f * SpeedScale / LevelGeometry.FrameSeconds, 1e-4f);
        }

        [Test]
        public void WaterSpinCoefficient_MatchesFlashFourDegreesPerFramePerPixelSpeed()
        {
            // (vx+vy)=1 px/帧 → 4°/帧 × 25 = 100°/s；100 / 1.5625 = 64。
            Assert.AreEqual(100f, RollRules.WaterSpinDegreesPerSecondPerUnitSpeed * SpeedScale, 1e-4f);
            Assert.AreEqual(64f, RollRules.WaterSpinDegreesPerSecondPerUnitSpeed, 1e-4f);
            Assert.AreEqual(100f, RollRules.WaterSpinDegreesPerSecond(1f * SpeedScale), 1e-4f);
        }

        [Test]
        public void WaterSinkSpeed_MatchesOneAndHalfPixelsPerFrame()
        {
            Assert.AreEqual(2.34375f, RollRules.WaterSinkSpeedUnitsPerSecond, 1e-4f);
            Assert.AreEqual(1.5f * SpeedScale, RollRules.WaterSinkSpeedUnitsPerSecond, 1e-4f);
        }

        // ------------------------------------------------------------------
        // 接地：摩擦 / 弹跳 / 阻尼
        // ------------------------------------------------------------------

        [Test]
        public void GroundFriction_DeceleratesLinearly_AndClampsAtZero()
        {
            // 一个接触帧（0.04s）衰减 78.125 × 0.04 = 3.125 u/s。
            Assert.AreEqual(6.875f, RollRules.GroundSpeedAfterFriction(10f, LevelGeometry.FrameSeconds), 1e-4f);
            Assert.AreEqual(0f, RollRules.GroundSpeedAfterFriction(1f, LevelGeometry.FrameSeconds), 1e-6f, "线性衰减下限 0");
            Assert.AreEqual(10f, RollRules.GroundSpeedAfterFriction(10f, 0f), 1e-6f, "dt=0 不衰减");
        }

        [Test]
        public void LandBounce_ScalesDownwardSpeedByZeroPointTwo()
        {
            // vy *= -0.2：5 u/s 下落 → 1 u/s 向上反弹。
            Assert.AreEqual(1.0f, RollRules.LandBounceUpSpeed(5f), 1e-6f);
            Assert.AreEqual(0.2f, RollRules.LandBounceUpSpeed(1f), 1e-6f);
            Assert.AreEqual(0f, RollRules.LandBounceUpSpeed(0f), 1e-6f);
        }

        [Test]
        public void LandBounce_BelowNoiseFloor_IsSuppressed()
        {
            // 提案：低于 MinBounceUpSpeed 的微反弹归零，避免数值噪声阻碍刚体入睡。
            Assert.AreEqual(0f, RollRules.LandBounceUpSpeed(0.1f), 1e-6f, "0.1×0.2=0.02 < 0.05 应归零");
            Assert.AreEqual(0.2f, RollRules.LandBounceUpSpeed(1f), 1e-6f, "高于阈值照常反弹");
        }

        [Test]
        public void GroundDamp_HalvesAngleEveryContactStep()
        {
            float pending = 0f;
            // 一步：8° ×0.5 = 4°；再一步：4° ×0.5 = 2°。
            Assert.AreEqual(4f, RollRules.DampGroundAngle(8f, LevelGeometry.FrameSeconds, ref pending), 1e-6f);
            Assert.AreEqual(2f, RollRules.DampGroundAngle(4f, LevelGeometry.FrameSeconds, ref pending), 1e-6f);
            Assert.AreEqual(0f, pending, 1e-6f, "两个整步都恰好被消费，无余数");
        }

        [Test]
        public void GroundDamp_AccumulatesPartialSteps()
        {
            float pending = 0f;
            // 单帧 dt=0.02（半步）不触发阻尼，余数留存；累计两帧才 ×0.5 一次。
            Assert.AreEqual(8f, RollRules.DampGroundAngle(8f, 0.02f, ref pending), 1e-6f);
            Assert.AreEqual(4f, RollRules.DampGroundAngle(8f, 0.02f, ref pending), 1e-6f);
        }

        [Test]
        public void GroundDamp_NormalizesBeforeDamping_AndSnapsBelowOneDegree()
        {
            // 270° 归一到 (-180,180] = -90°，再 ×0.5 = -45°。
            float pending = 0f;
            Assert.AreEqual(-45f, RollRules.DampGroundAngle(270f, LevelGeometry.FrameSeconds, ref pending), 1e-5f);

            // 0.8° 已低于 1° 阈值：一步阻尼后直接归零。
            pending = 0f;
            Assert.AreEqual(0f, RollRules.DampGroundAngle(0.8f, LevelGeometry.FrameSeconds, ref pending), 1e-6f);
        }

        [Test]
        public void GroundDamp_ZeroGroundedSeconds_IsNoOp()
        {
            float pending = 0f;
            Assert.AreEqual(16f, RollRules.DampGroundAngle(16f, 0f, ref pending), 1e-6f);
            Assert.AreEqual(0f, pending, 1e-6f);
        }

        [Test]
        public void NormalizeAngle180_FoldsIntoHalfOpenInterval()
        {
            Assert.AreEqual(0f, RollRules.NormalizeAngle180(0f), 1e-5f);
            Assert.AreEqual(90f, RollRules.NormalizeAngle180(90f), 1e-5f);
            Assert.AreEqual(180f, RollRules.NormalizeAngle180(180f), 1e-5f, "右闭：+180 保留");
            Assert.AreEqual(180f, RollRules.NormalizeAngle180(-180f), 1e-5f, "左开：-180 折到 +180");
            Assert.AreEqual(-90f, RollRules.NormalizeAngle180(270f), 1e-5f);
            Assert.AreEqual(90f, RollRules.NormalizeAngle180(-270f), 1e-5f);
            Assert.AreEqual(180f, RollRules.NormalizeAngle180(540f), 1e-5f);
            Assert.AreEqual(-179f, RollRules.NormalizeAngle180(181f), 1e-5f);
        }

        // ------------------------------------------------------------------
        // 旋转轴（前滚翻方向）
        // ------------------------------------------------------------------

        [Test]
        public void RollAxis_FlippingForwardAlongVelocity()
        {
            // 向 +X 移动：轴 = up×v = -Z，正角绕 -Z = 头顶向 +X 倒（前滚）。
            Vector3 axisX = RollRules.RollAxis(new Vector3(2f, 0f, 0f));
            Assert.AreEqual(0f, axisX.x, 1e-5f);
            Assert.AreEqual(0f, axisX.y, 1e-5f);
            Assert.AreEqual(-1f, axisX.z, 1e-5f);

            // 向 +Z 移动：轴 = +X，正角绕 +X = 头顶向 +Z 倒（前滚）。
            Vector3 axisZ = RollRules.RollAxis(new Vector3(0f, 0f, 2f));
            Assert.AreEqual(1f, axisZ.x, 1e-5f);
            Assert.AreEqual(0f, axisZ.y, 1e-5f);
            Assert.AreEqual(0f, axisZ.z, 1e-5f);

            // 竖直分量与滚动无关：含下落速度的水平轴不变。
            Vector3 withFall = RollRules.RollAxis(new Vector3(0f, -9f, 2f));
            Assert.AreEqual(axisZ, withFall);

            // 零水平速度：轴退化。
            Assert.AreEqual(Vector3.zero, RollRules.RollAxis(Vector3.zero));
            Assert.AreEqual(Vector3.zero, RollRules.RollAxis(new Vector3(0f, -5f, 0f)));
        }

        // ------------------------------------------------------------------
        // 落水演出：速度衰减
        // ------------------------------------------------------------------

        [Test]
        public void WaterDampFactor_RetainsEightyPercentPerContactFrame()
        {
            Assert.AreEqual(1f, RollRules.WaterDampFactor(0f), 1e-6f);
            Assert.AreEqual(0.8f, RollRules.WaterDampFactor(LevelGeometry.FrameSeconds), 1e-5f);
            Assert.AreEqual(0.64f, RollRules.WaterDampFactor(2f * LevelGeometry.FrameSeconds), 1e-5f);
            Assert.AreEqual(1f, RollRules.WaterDampFactor(-1f), 1e-6f, "非法 dt 不衰减");
        }

        // ------------------------------------------------------------------
        // 预览配套（同属 M4 §3.2 操控手感，放这里一起核对换算）
        // ------------------------------------------------------------------

        [Test]
        public void StepsForSpeed_SpansFifteenToSixtyLinearly()
        {
            Assert.AreEqual(ThrowTrajectory.DefaultSteps, ThrowTrajectory.StepsForSpeed(0f, 20f));
            Assert.AreEqual(ThrowTrajectory.DefaultSteps, ThrowTrajectory.StepsForSpeed(-5f, 20f), "负速度夹到下限");
            Assert.AreEqual(ThrowTrajectory.MaxSteps, ThrowTrajectory.StepsForSpeed(20f, 20f), "满力 = 上限");
            Assert.AreEqual(ThrowTrajectory.MaxSteps, ThrowTrajectory.StepsForSpeed(30f, 20f), "超出夹到上限");
            // 半力：lerp(15, 60, 0.5) = 37.5 → 38。
            Assert.AreEqual(38, ThrowTrajectory.StepsForSpeed(10f, 20f));
            Assert.AreEqual(ThrowTrajectory.DefaultSteps, ThrowTrajectory.StepsForSpeed(10f, 0f), "非法 twangMax 退回默认");
        }

        [Test]
        public void StepsForSpeed_IsMonotonicInSpeed()
        {
            int previous = 0;
            for (int i = 0; i <= 10; i++)
            {
                int steps = ThrowTrajectory.StepsForSpeed(20f * i / 10f, 20f);
                Assert.GreaterOrEqual(steps, previous, "步数应随力度单调不减");
                previous = steps;
            }
        }

        [Test]
        public void TryGetImpactPoint_InterpolatesGroundCrossing()
        {
            var samples = new[]
            {
                new Vector3(1f, 1f, 0f),
                new Vector3(2f, -1f, 0f),
                new Vector3(3f, -3f, 0f),
            };

            bool found = ThrowTrajectory.TryGetImpactPoint(
                new Vector3(0f, 2f, 0f), samples, samples.Length, 0f, out Vector3 impact);
            Assert.IsTrue(found);
            Assert.AreEqual(1.5f, impact.x, 1e-5f, "穿地段按 y 线性插值");
            Assert.AreEqual(0f, impact.y, 1e-5f);
            Assert.AreEqual(0f, impact.z, 1e-5f);
        }

        [Test]
        public void TryGetImpactPoint_NoCrossing_ReturnsFalse()
        {
            var samples = new[]
            {
                new Vector3(1f, 3f, 0f),
                new Vector3(2f, 4f, 0f),
            };

            bool found = ThrowTrajectory.TryGetImpactPoint(
                new Vector3(0f, 2f, 0f), samples, samples.Length, 0f, out Vector3 impact);
            Assert.IsFalse(found, "整条弧线都在地面上方时无落点");
            Assert.AreEqual(Vector3.zero, impact);
        }

        [Test]
        public void TryGetImpactPoint_FirstSampleAlreadyBelowGround_InterpolatesFromOrigin()
        {
            var samples = new[] { new Vector3(1f, -1f, 0f) };
            bool found = ThrowTrajectory.TryGetImpactPoint(
                new Vector3(0f, 0.5f, 0f), samples, samples.Length, 0f, out Vector3 impact);
            Assert.IsTrue(found);
            // t = (0.5 - 0) / (0.5 - (-1)) = 1/3。
            Assert.AreEqual(1f / 3f, impact.x, 1e-5f);
            Assert.AreEqual(0f, impact.y, 1e-5f);
        }

        [Test]
        public void TryGetImpactPoint_EmptyInput_ReturnsFalse()
        {
            bool found = ThrowTrajectory.TryGetImpactPoint(
                Vector3.zero, null, 0, 0f, out Vector3 impact);
            Assert.IsFalse(found);
            var empty = new Vector3[0];
            Assert.IsFalse(ThrowTrajectory.TryGetImpactPoint(Vector3.zero, empty, 0, 0f, out impact));
        }
    }
}
