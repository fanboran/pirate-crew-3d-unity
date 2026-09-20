using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="CameraFeelRules"/> 纯逻辑测试（可在无头验证台跑，不碰 MonoBehaviour/Cinemachine）。
    ///
    /// 覆盖：
    ///   · 震屏强度随"距离/爆炸半径"的衰减形状（借 §5.3 falloff）、递减包络、位移/滚转采样边界；
    ///   · 聚焦速率与"90% 到位时长"的反推、FOV 推近曲线、旁观 FOV；
    ///   · 落水下压曲线；
    ///   · 跟随状态机全部迁移（含 FocusRequested 最高优先级、超时/TargetLost 回焦）；
    ///   · 顿帧安全上限（时长 ≤2 帧 @25fps、timeScale 下限 0.05、对局结束/他人占用 timeScale 时不施加）。
    /// </summary>
    [TestFixture]
    public class CameraFeelRulesTests
    {
        // ------------------------------------------------------------------
        // 震屏强度：距离 / 半径
        // ------------------------------------------------------------------

        [Test]
        public void ShakeFalloff_AtCenter_IsFull()
        {
            Assert.AreEqual(1f, CameraFeelRules.ShakeFalloff(0f, 10f), 1e-5f);
        }

        [Test]
        public void ShakeFalloff_AtRadius_IsHalf()
        {
            // 射程 = 2×radius，故 d=radius 处还剩 50%。
            Assert.AreEqual(0.5f, CameraFeelRules.ShakeFalloff(10f, 10f), 1e-5f);
        }

        [Test]
        public void ShakeFalloff_AtTwiceRadius_IsZero()
        {
            Assert.AreEqual(0f, CameraFeelRules.ShakeFalloff(20f, 10f), 1e-5f);
        }

        [Test]
        public void ShakeFalloff_BeyondRange_ClampsToZero()
        {
            Assert.AreEqual(0f, CameraFeelRules.ShakeFalloff(999f, 10f), 1e-5f);
        }

        [Test]
        public void ShakeFalloff_IsMonotonicallyDecreasing()
        {
            float previous = float.MaxValue;
            for (int i = 0; i <= 20; i++)
            {
                float falloff = CameraFeelRules.ShakeFalloff(i, 10f);
                Assert.LessOrEqual(falloff, previous + 1e-6f, "衰减应单调不增");
                previous = falloff;
            }
        }

        [Test]
        public void ExplosionShake_AtCenter_ReachesMaxima()
        {
            ShakeProfile profile = CameraFeelRules.ExplosionShake(
                distanceWorld: 0f, explosionRadiusWorld: 10f,
                maxAmplitude: 0.35f, maxRollDegrees: 1.2f, durationSeconds: 0.3f);

            Assert.AreEqual(0.35f, profile.Amplitude, 1e-5f);
            Assert.AreEqual(1.2f, profile.RollDegrees, 1e-5f);
            Assert.AreEqual(0.3f, profile.DurationSeconds, 1e-5f);
            Assert.IsTrue(profile.IsActive);
        }

        [Test]
        public void ExplosionShake_FarAway_IsInactive()
        {
            ShakeProfile profile = CameraFeelRules.ExplosionShake(
                distanceWorld: 100f, explosionRadiusWorld: 5f,
                maxAmplitude: 0.35f, maxRollDegrees: 1.2f, durationSeconds: 0.3f);

            Assert.IsFalse(profile.IsActive, "远端无关爆炸不应震屏");
            Assert.AreEqual(0f, profile.Amplitude, 1e-6f);
        }

        [Test]
        public void ExplosionShake_CloserIsStronger()
        {
            ShakeProfile near = CameraFeelRules.ExplosionShake(2f, 10f, 0.35f, 1.2f, 0.3f);
            ShakeProfile far = CameraFeelRules.ExplosionShake(12f, 10f, 0.35f, 1.2f, 0.3f);
            Assert.Greater(near.Amplitude, far.Amplitude);
        }

        // ------------------------------------------------------------------
        // 震屏包络 / 采样边界
        // ------------------------------------------------------------------

        [Test]
        public void ShakeEnvelope_StartOne_EndZero()
        {
            Assert.AreEqual(1f, CameraFeelRules.ShakeEnvelope(0f), 1e-5f);
            Assert.AreEqual(0f, CameraFeelRules.ShakeEnvelope(1f), 1e-5f);
            Assert.AreEqual(0f, CameraFeelRules.ShakeEnvelope(2f), 1e-5f);
            Assert.AreEqual(0.25f, CameraFeelRules.ShakeEnvelope(0.5f), 1e-5f);
        }

        [Test]
        public void ShakeOffset2D_ZeroAtStartAndEnd()
        {
            Assert.AreEqual(Vector2.zero, CameraFeelRules.ShakeOffset2D(0f, 0.3f, 0.35f, 18f, 0.7f));
            Assert.AreEqual(Vector2.zero, CameraFeelRules.ShakeOffset2D(0.3f, 0.3f, 0.35f, 18f, 0.7f));
            Assert.AreEqual(Vector2.zero, CameraFeelRules.ShakeOffset2D(0.5f, 0.3f, 0.35f, 18f, 0.7f));
            Assert.AreEqual(Vector2.zero, CameraFeelRules.ShakeOffset2D(0.1f, 0.3f, 0f, 18f, 0.7f));
        }

        [Test]
        public void ShakeOffset2D_PerAxisBoundedByAmplitude()
        {
            const float amplitude = 0.35f;
            for (int i = 1; i < 30; i++)
            {
                float elapsed = 0.3f * i / 30f;
                Vector2 offset = CameraFeelRules.ShakeOffset2D(elapsed, 0.3f, amplitude, 18f, 1.234f);
                Assert.LessOrEqual(Mathf.Abs(offset.x), amplitude + 1e-5f);
                Assert.LessOrEqual(Mathf.Abs(offset.y), amplitude + 1e-5f);
            }
        }

        [Test]
        public void ShakeOffset2D_IsDeterministic()
        {
            Vector2 a = CameraFeelRules.ShakeOffset2D(0.123f, 0.3f, 0.35f, 18f, 0.5f);
            Vector2 b = CameraFeelRules.ShakeOffset2D(0.123f, 0.3f, 0.35f, 18f, 0.5f);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ShakeRoll_BoundedAndZeroAtEnds()
        {
            Assert.AreEqual(0f, CameraFeelRules.ShakeRoll(0f, 0.3f, 1.2f, 18f, 0.4f), 1e-5f);
            Assert.AreEqual(0f, CameraFeelRules.ShakeRoll(0.3f, 0.3f, 1.2f, 18f, 0.4f), 1e-5f);

            for (int i = 1; i < 30; i++)
            {
                float roll = CameraFeelRules.ShakeRoll(0.3f * i / 30f, 0.3f, 1.2f, 18f, 0.4f);
                Assert.LessOrEqual(Mathf.Abs(roll), 1.2f + 1e-5f);
            }
        }

        [Test]
        public void ShouldReplaceShake_OnlyStrongerWins()
        {
            Assert.IsTrue(CameraFeelRules.ShouldReplaceShake(0.1f, 0.2f));
            Assert.IsFalse(CameraFeelRules.ShouldReplaceShake(0.2f, 0.1f));
            Assert.IsFalse(CameraFeelRules.ShouldReplaceShake(0.2f, 0.2f), "等强不替换，避免 AoE 连抖");
        }

        // ------------------------------------------------------------------
        // 聚焦缓动 / FOV
        // ------------------------------------------------------------------

        [Test]
        public void FocusLerpPerSecond_MatchesNinetyPercentInDuration()
        {
            const float duration = 0.4f;   // 原版 10 帧 @25fps
            float k = CameraFeelRules.FocusLerpPerSecond(duration);
            float alpha = CameraFeelRules.ApproachAlpha(k, duration);
            Assert.AreEqual(CameraFeelRules.FocusTargetFraction, alpha, 1e-4f);
        }

        [Test]
        public void FocusLerpPerSecond_ZeroDuration_IsZero()
        {
            Assert.AreEqual(0f, CameraFeelRules.FocusLerpPerSecond(0f), 1e-6f);
        }

        [Test]
        public void ApproachAlpha_ZeroDtOrRate_IsZero()
        {
            Assert.AreEqual(0f, CameraFeelRules.ApproachAlpha(6f, 0f), 1e-6f);
            Assert.AreEqual(0f, CameraFeelRules.ApproachAlpha(0f, 0.016f), 1e-6f);
        }

        [Test]
        public void PushInFov_ReturnsBaseAtEnds_PeaksInMiddle()
        {
            const float baseFov = 60f;
            const float peak = 1.5f;
            const float duration = 0.3f;

            Assert.AreEqual(baseFov, CameraFeelRules.PushInFov(baseFov, peak, 0f, duration), 1e-5f);
            Assert.AreEqual(baseFov, CameraFeelRules.PushInFov(baseFov, peak, duration, duration), 1e-5f);
            Assert.AreEqual(baseFov - peak,
                CameraFeelRules.PushInFov(baseFov, peak, duration * 0.5f, duration), 1e-4f);
        }

        [Test]
        public void PushInFov_ZeroParameters_IsNoOp()
        {
            Assert.AreEqual(60f, CameraFeelRules.PushInFov(60f, 0f, 0.1f, 0.3f), 1e-6f);
            Assert.AreEqual(60f, CameraFeelRules.PushInFov(60f, 1.5f, 0.1f, 0f), 1e-6f);
        }

        [Test]
        public void SpectatorFov_WidensOnlyWhenSpectating()
        {
            Assert.AreEqual(60f, CameraFeelRules.SpectatorFov(60f, 1.5f, false), 1e-6f);
            Assert.AreEqual(61.5f, CameraFeelRules.SpectatorFov(60f, 1.5f, true), 1e-6f);
        }

        // ------------------------------------------------------------------
        // 落水下压
        // ------------------------------------------------------------------

        [Test]
        public void DrownDipOffset_StartsAtNegativePeak_EndsAtZero()
        {
            Assert.AreEqual(-0.45f, CameraFeelRules.DrownDipOffset(0f, 0.5f, 0.45f), 1e-5f);
            Assert.AreEqual(0f, CameraFeelRules.DrownDipOffset(0.5f, 0.5f, 0.45f), 1e-6f);
            Assert.AreEqual(0f, CameraFeelRules.DrownDipOffset(0.6f, 0.5f, 0.45f), 1e-6f);
        }

        [Test]
        public void DrownDipOffset_MonotonicallyRecoversToZero()
        {
            float previous = float.MinValue;
            for (int i = 0; i <= 10; i++)
            {
                float dip = CameraFeelRules.DrownDipOffset(0.5f * i / 10f, 0.5f, 0.45f);
                Assert.GreaterOrEqual(dip, previous - 1e-6f, "下压应单调回到 0");
                previous = dip;
            }
        }

        // ------------------------------------------------------------------
        // 跟随状态机
        // ------------------------------------------------------------------

        static CameraFeelTimings Timings => new CameraFeelTimings(2.5f, 0.35f, 0.35f);

        [Test]
        public void Follow_None_ShotFired_EntersFollow()
        {
            Assert.AreEqual(CameraFollowState.FollowProjectile,
                CameraFeelRules.Advance(CameraFollowState.None, CameraFollowTrigger.ShotFired,
                    targetActive: true, elapsedInState: 0f, timings: Timings));
        }

        [Test]
        public void Follow_None_Detonated_EntersHold()
        {
            Assert.AreEqual(CameraFollowState.DetonationHold,
                CameraFeelRules.Advance(CameraFollowState.None, CameraFollowTrigger.Detonated,
                    targetActive: false, elapsedInState: 0f, timings: Timings));
        }

        [Test]
        public void Follow_Projectile_Detonated_EntersHold()
        {
            Assert.AreEqual(CameraFollowState.DetonationHold,
                CameraFeelRules.Advance(CameraFollowState.FollowProjectile, CameraFollowTrigger.Detonated,
                    targetActive: true, elapsedInState: 0.1f, timings: Timings));
        }

        [Test]
        public void Follow_Projectile_TargetLost_ReturnsToFocus()
        {
            Assert.AreEqual(CameraFollowState.ReturnToFocus,
                CameraFeelRules.Advance(CameraFollowState.FollowProjectile, CameraFollowTrigger.TargetLost,
                    targetActive: true, elapsedInState: 0.1f, timings: Timings));
        }

        [Test]
        public void Follow_Projectile_TargetStops_ReturnsToFocus()
        {
            Assert.AreEqual(CameraFollowState.ReturnToFocus,
                CameraFeelRules.Advance(CameraFollowState.FollowProjectile, CameraFollowTrigger.None,
                    targetActive: false, elapsedInState: 0.1f, timings: Timings));
        }

        [Test]
        public void Follow_Projectile_Timeout_ReturnsToFocus()
        {
            Assert.AreEqual(CameraFollowState.ReturnToFocus,
                CameraFeelRules.Advance(CameraFollowState.FollowProjectile, CameraFollowTrigger.None,
                    targetActive: true, elapsedInState: 2.5f, timings: Timings));
        }

        [Test]
        public void Follow_Projectile_StillActive_StaysFollowing()
        {
            Assert.AreEqual(CameraFollowState.FollowProjectile,
                CameraFeelRules.Advance(CameraFollowState.FollowProjectile, CameraFollowTrigger.None,
                    targetActive: true, elapsedInState: 0.5f, timings: Timings));
        }

        [Test]
        public void Follow_Hold_BeforeAndAfterThreshold()
        {
            Assert.AreEqual(CameraFollowState.DetonationHold,
                CameraFeelRules.Advance(CameraFollowState.DetonationHold, CameraFollowTrigger.None,
                    targetActive: false, elapsedInState: 0.2f, timings: Timings));

            Assert.AreEqual(CameraFollowState.ReturnToFocus,
                CameraFeelRules.Advance(CameraFollowState.DetonationHold, CameraFollowTrigger.None,
                    targetActive: false, elapsedInState: 0.35f, timings: Timings));
        }

        [Test]
        public void Follow_ReturnToFocus_AfterDuration_BecomesNone()
        {
            Assert.AreEqual(CameraFollowState.ReturnToFocus,
                CameraFeelRules.Advance(CameraFollowState.ReturnToFocus, CameraFollowTrigger.None,
                    targetActive: false, elapsedInState: 0.1f, timings: Timings));

            Assert.AreEqual(CameraFollowState.None,
                CameraFeelRules.Advance(CameraFollowState.ReturnToFocus, CameraFollowTrigger.None,
                    targetActive: false, elapsedInState: 0.35f, timings: Timings));
        }

        [TestCase(CameraFollowState.None)]
        [TestCase(CameraFollowState.FollowProjectile)]
        [TestCase(CameraFollowState.DetonationHold)]
        [TestCase(CameraFollowState.ReturnToFocus)]
        public void Follow_FocusRequested_AlwaysCancels(CameraFollowState state)
        {
            // 外部聚焦（回合开始/选中角色）优先级最高：任何时候都立即取消跟随。
            Assert.AreEqual(CameraFollowState.None,
                CameraFeelRules.Advance(state, CameraFollowTrigger.FocusRequested,
                    targetActive: true, elapsedInState: 0f, timings: Timings));
        }

        [Test]
        public void Follow_DefaultTimings_ArePositiveAndOrdered()
        {
            CameraFeelTimings timings = CameraFeelRules.DefaultFollowTimings;
            Assert.Greater(timings.FollowTimeoutSeconds, 0f);
            Assert.Greater(timings.DetonationHoldSeconds, 0f);
            Assert.Greater(timings.ReturnSeconds, 0f);
            Assert.Greater(timings.FollowTimeoutSeconds, timings.ReturnSeconds);
        }

        // ------------------------------------------------------------------
        // 顿帧安全上限
        // ------------------------------------------------------------------

        [Test]
        public void HitStop_MaxDuration_IsTwoFramesAt25Fps()
        {
            Assert.AreEqual(2f * LevelGeometry.FrameSeconds, CameraFeelRules.MaxHitStopSeconds, 1e-6f);
            Assert.AreEqual(0.08f, CameraFeelRules.MaxHitStopSeconds, 1e-6f);
        }

        [Test]
        public void HitStop_ClampDuration()
        {
            Assert.AreEqual(0f, CameraFeelRules.ClampHitStopDuration(-1f), 1e-6f);
            Assert.AreEqual(0.05f, CameraFeelRules.ClampHitStopDuration(0.05f), 1e-6f);
            Assert.AreEqual(CameraFeelRules.MaxHitStopSeconds, CameraFeelRules.ClampHitStopDuration(10f), 1e-6f);
        }

        [Test]
        public void HitStop_ClampTimeScale_KeepsStrictlyPositive()
        {
            Assert.AreEqual(CameraFeelRules.MinHitStopTimeScale,
                CameraFeelRules.ClampHitStopTimeScale(0.0001f), 1e-6f);
            Assert.AreEqual(0.5f, CameraFeelRules.ClampHitStopTimeScale(0.5f), 1e-6f);
            Assert.AreEqual(1f, CameraFeelRules.ClampHitStopTimeScale(5f), 1e-6f);
        }

        [Test]
        public void HitStop_MinTimeScale_IsStrictlyPositive()
        {
            // 必须 > 0：timeScale=0 会让物理停步、IsAnythingActive 恒真 → 回合推进死锁。
            Assert.Greater(CameraFeelRules.MinHitStopTimeScale, 0f);
        }

        [Test]
        public void HitStop_DurationFromFrames_ClampedAtTwoFrames()
        {
            Assert.AreEqual(0f, CameraFeelRules.HitStopDurationFromFrames(0), 1e-6f);
            Assert.AreEqual(LevelGeometry.FrameSeconds, CameraFeelRules.HitStopDurationFromFrames(1), 1e-6f);
            Assert.AreEqual(CameraFeelRules.MaxHitStopSeconds, CameraFeelRules.HitStopDurationFromFrames(2), 1e-6f);
            Assert.AreEqual(CameraFeelRules.MaxHitStopSeconds, CameraFeelRules.HitStopDurationFromFrames(99), 1e-6f);
        }

        [Test]
        public void HitStop_SafetyCheck()
        {
            Assert.IsTrue(CameraFeelRules.IsHitStopSafe(
                CameraFeelRules.MaxHitStopSeconds, CameraFeelRules.MinHitStopTimeScale));
            Assert.IsFalse(CameraFeelRules.IsHitStopSafe(
                CameraFeelRules.MaxHitStopSeconds + 0.01f, CameraFeelRules.MinHitStopTimeScale));
            Assert.IsFalse(CameraFeelRules.IsHitStopSafe(0.05f, CameraFeelRules.MinHitStopTimeScale - 0.01f));
            Assert.IsFalse(CameraFeelRules.IsHitStopSafe(-0.01f, 0.5f));
        }

        [Test]
        public void HitStop_ShouldApply_RespectsMatchOverAndTimeScaleOwner()
        {
            Assert.IsTrue(CameraFeelRules.ShouldApplyHitStop(false, false, 1f));
            Assert.IsFalse(CameraFeelRules.ShouldApplyHitStop(true, false, 1f), "对局结束不顿帧");
            Assert.IsFalse(CameraFeelRules.ShouldApplyHitStop(false, true, 1f), "场景过渡不顿帧");
            Assert.IsFalse(CameraFeelRules.ShouldApplyHitStop(false, false, 0.5f), "timeScale 已被他人接管时不抢");
        }

        // ------------------------------------------------------------------
        // 默认机位档位（用户裁决 2026-09-14：默认角色特写，滚轮可拉到旧 45° 全场）
        //   这些是 BattleCameraController 的 public static 常量/纯函数，无需实例化 MonoBehaviour。
        //   【M4 更新】手动上限 50→160、新增全景档（docs/M4-大海域世界化.md §1/§3.2），
        //   相关断言已随 API 更新（注明 M4）。
        // ------------------------------------------------------------------

        [Test]
        public void CloseUpPreset_IsWithinUserRuledRanges()
        {
            // 裁决：特写俯角 25–35°；距离区间 = 旧 5–7 ×2 = 10–14
            // （相机取景按「看同样的格数」等比放大：格 1→2 单位后档位距离一律 ×2，
            //  见 BattleCameraController.cs:44 与 CloseUpDistance=12 的类头注释）。
            Assert.That(BattleCameraController.CloseUpDistance, Is.InRange(10f, 14f), "特写档距离应在 10–14（旧 5–7 ×2）");
            Assert.That(BattleCameraController.CloseUpPitchDegrees, Is.InRange(25f, 35f), "特写档俯角应在 25–35°");
        }

        [Test]
        public void FullFieldPreset_KeepsLegacy45DegreesAtDistance30()
        {
            // 裁决：滚轮后拉可到旧的 45° 全场视角；格 1→2 单位后距离由 15 ×2 = 30（俯角 45° 不变）。
            Assert.AreEqual(30f, BattleCameraController.FullFieldDistance, 1e-4f);
            Assert.AreEqual(45f, BattleCameraController.FullFieldPitchDegrees, 1e-4f);
        }

        [Test]
        public void ZoomBounds_AllowPushInToSixAndPullBackToFar()
        {
            // 【M4 更新】后拉最远 160（原 50，docs/M4-大海域世界化.md §1 大海域档位）；前推最近 6 不变。
            Assert.AreEqual(6f, BattleCameraController.MinManualDistance, 1e-4f, "前推最近 6（旧 3 ×2）");
            Assert.AreEqual(160f, BattleCameraController.MaxManualDistance, 1e-4f, "后拉最远 160（M4 §3.2 档位放大）");
            // 特写档与全场档都必须落在可用缩放区间内。
            Assert.That(BattleCameraController.CloseUpDistance,
                Is.InRange(BattleCameraController.MinManualDistance, BattleCameraController.MaxManualDistance));
            Assert.That(BattleCameraController.FullFieldDistance,
                Is.InRange(BattleCameraController.MinManualDistance, BattleCameraController.MaxManualDistance));
            Assert.Less(BattleCameraController.CloseUpDistance, BattleCameraController.FullFieldDistance,
                "特写应比全场更近");
        }

        [Test]
        public void PanoramaDistanceForSpan_ClampsPerM4Contract()
        {
            // 【M4 新增】全景档 = clamp(span × 0.55, 60, 160)（docs/M4-大海域世界化.md §1）。
            Assert.AreEqual(60f, BattleCameraController.PanoramaDistanceForSpan(100f), 1e-4f,
                "默认跨度 100u → 55 被 60 下限托住（缺省行为）");
            Assert.AreEqual(60f, BattleCameraController.PanoramaDistanceForSpan(50f), 1e-4f, "小图也保 60 下限");
            Assert.AreEqual(110f, BattleCameraController.PanoramaDistanceForSpan(200f), 1e-4f);
            Assert.AreEqual(160f, BattleCameraController.PanoramaDistanceForSpan(300f), 1e-4f, "大图被 160 上限夹住");
            Assert.AreEqual(BattleCameraController.MaxManualDistance,
                BattleCameraController.PanoramaDistanceForSpan(300f), 1e-4f, "上限与手动缩放上限一致");
            Assert.AreEqual(BattleCameraController.PanoramaDistanceForSpan(
                BattleCameraController.DefaultWorldSpan), 60f, 1e-4f, "默认跨度 = 现行 100u 图");
        }

        [Test]
        public void PitchForDistance_InterpolatesThroughThreeAnchorsAndSaturates()
        {
            // 特写锚：≤12 → 30°；比特写更近也不变。
            Assert.AreEqual(BattleCameraController.CloseUpPitchDegrees,
                BattleCameraController.PitchForDistance(BattleCameraController.CloseUpDistance), 1e-3f);
            Assert.AreEqual(BattleCameraController.CloseUpPitchDegrees,
                BattleCameraController.PitchForDistance(BattleCameraController.MinManualDistance), 1e-3f);
            // 全场锚：30u 处恰为旧 45°。
            Assert.AreEqual(BattleCameraController.FullFieldPitchDegrees,
                BattleCameraController.PitchForDistance(BattleCameraController.FullFieldDistance), 1e-3f);
            // 【M4 更新】全景锚：全景档处外推到 55°，再远维持（原断言"45° 饱和到 50"已随外推废止）。
            float panorama = BattleCameraController.PanoramaDistanceForSpan(100f);
            Assert.AreEqual(BattleCameraController.PanoramaPitchDegrees,
                BattleCameraController.PitchForDistance(panorama), 1e-3f);
            Assert.AreEqual(BattleCameraController.PanoramaPitchDegrees,
                BattleCameraController.PitchForDistance(BattleCameraController.MaxManualDistance), 1e-3f);
            // 单调不减（横跨三段）。
            float previous = float.MinValue;
            for (int i = 0; i <= 20; i++)
            {
                float d = Mathf.Lerp(BattleCameraController.MinManualDistance,
                    BattleCameraController.MaxManualDistance, i / 20f);
                float pitch = BattleCameraController.PitchForDistance(d);
                Assert.GreaterOrEqual(pitch, previous - 1e-4f, "俯角应随距离单调不减");
                previous = pitch;
            }
        }

        // ------------------------------------------------------------------
        // M4 手感：Scope / 力度-镜头耦合（docs/M4-大海域世界化.md §3.2，提案数值）
        // ------------------------------------------------------------------

        [Test]
        public void ScopeFov_BlendsFromBaseToSniperTarget()
        {
            Assert.AreEqual(60f, CameraFeelRules.ScopeFov(60f, 0f), 1e-5f, "未进入 Scope = 基准 FOV");
            Assert.AreEqual(CameraFeelRules.ScopeTargetFov, CameraFeelRules.ScopeFov(60f, 1f), 1e-5f, "完全进入 = 28");
            Assert.AreEqual(44f, CameraFeelRules.ScopeFov(60f, 0.5f), 1e-4f);
        }

        [Test]
        public void Scope_Constants_AreSensible()
        {
            Assert.AreEqual(28f, CameraFeelRules.ScopeTargetFov, 1e-4f, "M4 §3.2：FOV 60→28");
            Assert.AreEqual(0.25f, CameraFeelRules.ScopeBlendSeconds, 1e-4f, "M4 §3.2：平滑收敛 0.25s");
            Assert.AreEqual(0.4f, CameraFeelRules.ScopeAimSensitivityScale, 1e-4f, "M4 §3.2：灵敏度 ×0.4");
            Assert.That(CameraFeelRules.ProjectileFollowFocusScale,
                Is.InRange(0f, 1f), "追焦平滑缩放应是减速因子");
            Assert.Less(CameraFeelRules.ProjectileFollowFocusScale, 1f, "追焦要比回焦更慢（迟滞感）");
        }

        [Test]
        public void ChargeZoomDistance_MapsPowerLinearlyBetweenAnchors()
        {
            Assert.AreEqual(12f, CameraFeelRules.ChargeZoomDistance(12f, 60f, 0f), 1e-5f, "零力度 = 近档");
            Assert.AreEqual(60f, CameraFeelRules.ChargeZoomDistance(12f, 60f, 1f), 1e-5f, "满力 = 全景档");
            Assert.AreEqual(36f, CameraFeelRules.ChargeZoomDistance(12f, 60f, 0.5f), 1e-4f);
            Assert.AreEqual(12f, CameraFeelRules.ChargeZoomDistance(12f, 60f, -1f), 1e-5f, "越界夹回近档");
            Assert.AreEqual(60f, CameraFeelRules.ChargeZoomDistance(12f, 60f, 2f), 1e-5f, "越界夹回全景");
        }

        [Test]
        public void OffsetDirectionForPitch_RoundTripsThroughPitchOf()
        {
            // 烘焙机位的偏移格式是 (0, d·sinP, d·cosP)；两个 helper 必须互逆（PlayMode 用它反推俯角）。
            foreach (float pitch in new[] { 30f, 45f, 25f, 35f })
            {
                Vector3 dir = BattleCameraController.OffsetDirectionForPitch(pitch);
                Assert.AreEqual(1f, dir.magnitude, 1e-4f, "单位方向模长应为 1");
                Assert.AreEqual(0f, dir.x, 1e-5f, "yaw=0 时偏移应在 +Z/+Y 平面内");
                Assert.AreEqual(pitch, BattleCameraController.PitchOf(dir), 1e-3f);
            }
        }

        [Test]
        public void LookAtHeight_FollowsGodotUnitHeightAndRatio()
        {
            // lookAt 抬高 = 单位视觉高 × 比例；视觉高与 CrewVisualPrefabBuilder.TargetUnitHeight 同源（1.85）。
            Assert.That(BattleCameraController.UnitVisualHeight, Is.EqualTo(1.85f).Within(1e-4f),
                "单位视觉总高应 = Godot 1.85（1 格 = 1 Godot 单位 = 1 本工程单位）");
            Assert.That(BattleCameraController.LookAtHeightRatio, Is.InRange(0.6f, 0.7f),
                "lookAt 抬高比例应在 0.6–0.7");
            Assert.That(BattleCameraController.LookAtHeight,
                Is.EqualTo(1.85f * 0.65f).Within(1e-4f), "lookAt 抬高 ≈ 1.20");
        }
    }
}
