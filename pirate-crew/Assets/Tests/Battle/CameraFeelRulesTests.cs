using NUnit.Framework;
using PirateCrew.Rendering.Pixelart;
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
        // 默认机位档位（等距像素卡通 · 正交口径，创始人裁决 2026-09-22/23：
        //   正交投影 + 整数 OrthoSize + 俯角统一 30°；两档取景制（2026-10-05 走查反馈）下
        //   近景 = 基准档、远景 = 全景档，Tab/滚轮切换——连续无级缩放仍退役）。
        //   这些是 CameraFraming 的 public const 常量/纯函数，无需实例化 MonoBehaviour。
        //   旧透视档位与连续滚轮缩放（MinOrthoSize/MaxOrthoSize/OrthoZoomStep）已随
        //   2026-09-23 裁决退役，相关断言一并移除；M4 全景档语义转写为 size 档（span × 0.3）。
        // ------------------------------------------------------------------

        [Test]
        public void CloseUpPreset_IsTheNearFramingTier()
        {
            // 【距离恒定收口（创始人 2026-10-07 裁决禁多镜头距离）】唯一恒定档 = 16.0
            // （1080p·2× 等效，可见 32 m）；像素密度常量 WorldPerPixel = 0.05925926 与
            // 像素化试点场景（调试关）序列化值逐位同源——调试关与战斗关同密度。
            Assert.AreEqual(16f, CameraFraming.CloseUpOrthoSize, "恒定档 = 可见 32 m（1080p·2× 等效）");
            Assert.AreEqual(32f, CameraFraming.CloseUpOrthoSize * 2f, 1e-4f, "可见高度 = 2 × OrthoSize");
            Assert.AreEqual(0.05925926f, CameraFraming.WorldPerPixel, 1e-8f,
                "像素密度常量与调试关 rig 序列化值逐位同源");
            Assert.AreEqual(540f * CameraFraming.WorldPerPixel / 2f, CameraFraming.CloseUpOrthoSize, 1e-4f,
                "CloseUpOrthoSize = 1080p·2× 下按 WorldPerPixel 反推的正交半高");
        }

        [Test]
        public void OrthographicPreset_UsesFixed30DegreesAndBakingDistance()
        {
            // 等距轴测：俯角锁 30°（不再随档位插值，方位角另由右键环绕自由旋转）；
            // 机位距离恒 30（只定机位，不表达视野）；
            // 全场档 size 17 = 旧透视全场档（距离 30 + FOV 60 → 等效半高 30×tan30° ≈ 17.3）取整。
            // 【创始人 2026-09-22 裁决】游戏内俯角 = 出图俯角 30°（sin=0.5 → 规则像素阶梯）；
            // 旧的"真等距 35.264°"（2026-09-21）已废。
            Assert.AreEqual(30f, CameraFraming.BasePitchDegrees, 1e-3f);
            Assert.AreEqual(30f, CameraFraming.BaseDistance, 1e-4f);
            Assert.AreEqual(17, CameraFraming.FullFieldOrthoSize);
        }

        [Test]
        public void PanoramaOrthoSizeForSpan_ClampsPerM4Contract()
        {
            // 【距离恒定收口（创始人 2026-10-07 裁决禁多镜头距离）】全景档与近景档合一——
            // 任何跨度都返回恒定档，不再随 span 变化。
            Assert.AreEqual(16, CameraFraming.PanoramaOrthoSizeForSpan(100f));
            Assert.AreEqual(16, CameraFraming.PanoramaOrthoSizeForSpan(50f));
            Assert.AreEqual(16, CameraFraming.PanoramaOrthoSizeForSpan(300f));
            Assert.AreEqual(CameraFraming.PanoramaOrthoSizeForSpan(
                CameraFraming.DefaultWorldSpan), 16, "默认跨度也 = 恒定档");
        }

        [Test]
        public void ToggleFramingTier_FlipsBetweenNearAndPanorama()
        {
            // 【距离恒定收口】Tab 两档翻转退役——恒返回恒定档，翻转不再改变画面距离。
            Assert.AreEqual(CameraFraming.CloseUpOrthoSize,
                CameraFraming.ToggleFramingTier(CameraFraming.CloseUpOrthoSize, 16f));
            Assert.AreEqual(CameraFraming.CloseUpOrthoSize,
                CameraFraming.ToggleFramingTier(16f, 16f));
        }

        // ------------------------------------------------------------------
        // M4 手感：弹体追焦（Scope/旁观已随两态重构退役）
        // ------------------------------------------------------------------

        [Test]
        public void ProjectileFollowFocusScale_IsSubUnitDecelerator()
        {
            Assert.That(CameraFeelRules.ProjectileFollowFocusScale,
                Is.InRange(0f, 1f), "追焦平滑缩放应是减速因子");
            Assert.Less(CameraFeelRules.ProjectileFollowFocusScale, 1f, "追焦要比回焦更慢（迟滞感）");
        }

        [Test]
        public void SteppedPixelScale_ClampsToRuledRange()
        {
            // 2026-09-24 滚轮分域裁决：像素比例档只在 1:2–1:5 间步进，默认 1:3。
            Assert.AreEqual(4, PixelartCameraRig.SteppedPixelScale(3, 1));
            Assert.AreEqual(2, PixelartCameraRig.SteppedPixelScale(3, -1));
            Assert.AreEqual(5, PixelartCameraRig.SteppedPixelScale(5, 1), "上限 1:5 到顶不再步进");
            Assert.AreEqual(2, PixelartCameraRig.SteppedPixelScale(2, -1), "下限 1:2 到底不再步进");
            Assert.AreEqual(3, PixelartCameraRig.SteppedPixelScale(3, 0), "无输入不步进");
        }

        [Test]
        public void OffsetDirectionForPitch_RoundTripsThroughPitchOf()
        {
            // 烘焙机位：offset 三分量中水平两分量等分（方位 45°）、竖直分量 sinθ；
            // 两个 helper 必须互逆。原「x==0（+Z/+Y 平面）」是方位 0°
            // 的旧提案口径，随渲染篇 §2.1 真等距裁决废（yaw 0 = 等距基准方位 45°）。
            foreach (float pitch in new[] { 30f, 35.264f, 25f, 45f })
            {
                Vector3 dir = CameraFraming.OffsetDirectionForPitch(pitch);
                Assert.AreEqual(1f, dir.magnitude, 1e-4f, "单位方向模长应为 1");
                Assert.AreEqual(dir.z, dir.x, 1e-5f, "真等距：水平分量在 X/Z 上等分（方位 45°）");
                Assert.AreEqual(pitch, CameraFraming.PitchOf(dir), 1e-3f);
            }
        }

        [Test]
        public void LookAtHeight_FollowsUnitHeightAndRatio()
        {
            // lookAt 抬高 = 单位视觉高 × 比例；视觉高 = 两件式常量推导 2.0321（2026-10-05 裁决
            // "船员 = 标定人偶体格"，预制体重烘落盘 Visual 补偿后定案）。
            Assert.That(CameraFraming.UnitVisualHeight, Is.EqualTo(2.0321f).Within(1e-4f),
                "单位视觉总高应 = 2.0321（身体 1.3215 + 头心 1.7321 + 头半径 0.3）");
            Assert.That(CameraFraming.LookAtHeightRatio, Is.InRange(0.6f, 0.7f),
                "lookAt 抬高比例应在 0.6–0.7");
            Assert.That(CameraFraming.LookAtHeight,
                Is.EqualTo(2.0321f * 0.65f).Within(1e-4f), "lookAt 抬高 ≈ 1.32");
        }
    }
}
