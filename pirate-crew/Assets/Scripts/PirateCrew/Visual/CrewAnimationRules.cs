using UnityEngine;

namespace PirateCrew.PirateCrew.Visual
{
    /// <summary>
    /// 角色视觉状态（表现层状态机，与 <c>PirateBase</c> 的玩法状态位解耦）。
    /// </summary>
    public enum CrewVisualState
    {
        /// <summary>待机（呼吸）。</summary>
        Idle = 0,

        /// <summary>移动（摆动 + 前倾）。</summary>
        Move = 1,

        /// <summary>投掷-蓄力（手臂后拉、躯干后仰）。</summary>
        ThrowCharge = 2,

        /// <summary>投掷-释放（手臂前甩、躯干前倾）。</summary>
        ThrowRelease = 3,

        /// <summary>投掷-恢复。</summary>
        ThrowRecover = 4,

        /// <summary>受击（后仰 + 白闪）。</summary>
        Hit = 5,

        /// <summary>死亡（非落水）：前扑/后倒 + 收拢淡出。</summary>
        Death = 6,

        /// <summary>落水：下沉 + 摇晃 + 淡出。</summary>
        Drown = 7,
    }

    /// <summary>
    /// 角色动画的**纯 C# 规则**：状态时长、曲线、幅度。
    ///
    /// 【出处】docs/角色造型规范.md §4 状态表现表——标注为【AI 提案】，锚定原版行为：
    ///   · 死亡在"血条走空"后播 die（静态文档:352-359）；
    ///   · 落水立即 health=0，速度每帧 ×0.8、rotation += (vx+vy)*4 翻滚（静态文档:361-365）；
    ///   · 中弹时移动中播 hit（静态文档:368）。
    /// 因此以下时长均为**提案**，需与后续战斗节奏（回合/慢动作）对齐。
    ///
    /// 【分层】本类不引用任何 GameObject/MonoBehaviour，可在无头验证台断言；
    ///         <see cref="CrewVisualAnimator"/> 只负责"读本类输出 → 写 Transform"。
    /// </summary>
    public static class CrewAnimationRules
    {
        // ------------------------------------------------------------------
        // 时长（秒）
        // ------------------------------------------------------------------

        /// <summary>待机呼吸周期（§4：2.8s）。</summary>
        public const float BreathPeriod = 2.8f;

        /// <summary>移动步频（每步秒数，§4：0.40s）。</summary>
        public const float MoveStepSeconds = 0.40f;

        /// <summary>投掷-蓄力时长（§4：0.25s）。</summary>
        public const float ThrowChargeSeconds = 0.25f;

        /// <summary>投掷-释放时长（§4：0.10s；释放帧 = 初速施加帧）。</summary>
        public const float ThrowReleaseSeconds = 0.10f;

        /// <summary>投掷-恢复时长（§4：0.30s）。</summary>
        public const float ThrowRecoverSeconds = 0.30f;

        /// <summary>受击白闪时长（§4：0.08s）。</summary>
        public const float HitFlashSeconds = 0.08f;

        /// <summary>受击总时长（§4：0.25s）。</summary>
        public const float HitTotalSeconds = 0.25f;

        /// <summary>死亡倒地时长（§4：0.5s）。</summary>
        public const float DeathFallSeconds = 0.5f;

        /// <summary>死亡淡出时长（§4：0.5s）。</summary>
        public const float DeathFadeSeconds = 0.5f;

        /// <summary>落水下沉速度（单位/秒，§4：0.6）。</summary>
        public const float DrownSinkSpeed = 0.6f;

        /// <summary>落水摇晃幅度（度，§4：8°；对应原版 rotation += (vx+vy)*4 的翻滚）。</summary>
        public const float DrownSwayDegrees = 8f;

        /// <summary>落水表现最短时长（§4：1.5s）。</summary>
        public const float DrownSecondsMin = 1.5f;

        /// <summary>落水表现最长时长（§4：2.0s）。</summary>
        public const float DrownSecondsMax = 2.0f;

        /// <summary>投掷每步总时长（蓄力 + 释放 + 恢复）。</summary>
        public static float ThrowTotalSeconds => ThrowChargeSeconds + ThrowReleaseSeconds + ThrowRecoverSeconds;

        /// <summary>死亡表现总时长（倒地 + 淡出）。</summary>
        public static float DeathTotalSeconds => DeathFallSeconds + DeathFadeSeconds;

        // ------------------------------------------------------------------
        // 幅度
        // ------------------------------------------------------------------

        /// <summary>待机躯干纵向缩放幅度（§4：±1.5%）。</summary>
        public const float BreathTorsoScale = 0.015f;

        /// <summary>待机头上下幅度（§4：±0.004 单位）。</summary>
        public const float BreathHeadOffset = 0.0134f;

        /// <summary>待机手臂微摆幅度（度）。</summary>
        public const float BreathArmSwingDegrees = 3f;

        /// <summary>移动上下 bob 幅度（§4：±0.012 单位）。</summary>
        public const float MoveBobOffset = 0.040f;

        /// <summary>移动身体前倾（§4：6-10°，取中值 8°）。</summary>
        public const float MoveLeanDegrees = 8f;

        /// <summary>投掷蓄力后仰角度（§4：8°）。</summary>
        public const float ThrowChargeLeanDegrees = -8f;

        /// <summary>投掷释放前倾角度（§4：12°）。</summary>
        public const float ThrowReleaseLeanDegrees = 12f;

        /// <summary>受击后仰角度（§4：5°）。</summary>
        public const float HitRecoilDegrees = 5f;

        /// <summary>死亡倒地方向角（前扑/后倒；取后倒 90°）。</summary>
        public const float DeathFallDegrees = 90f;

        /// <summary>待机错相位步长（§4：不同职业错相位 0.5-1.0s）。</summary>
        public const float IdlePhaseStep = 0.7f;

        /// <summary>移动腿部摆幅（度）。</summary>
        public const float WalkLegSwingDegrees = 26f;

        /// <summary>移动手臂反向摆幅（度）。</summary>
        public const float WalkArmSwingDegrees = 20f;

        // ------------------------------------------------------------------
        // 曲线（纯函数）
        // ------------------------------------------------------------------

        /// <summary>待机呼吸相位（含职业错相位）。返回弧度。</summary>
        public static float BreathPhase(float timeSeconds, int professionIndex)
        {
            float period = BreathPeriod;
            return 2f * Mathf.PI * (timeSeconds + professionIndex * IdlePhaseStep) / period;
        }

        /// <summary>待机躯干纵向缩放倍率（1 ± 1.5%）。</summary>
        public static float BreathTorsoScaleY(float timeSeconds, int professionIndex)
        {
            return 1f + BreathTorsoScale * Mathf.Sin(BreathPhase(timeSeconds, professionIndex));
        }

        /// <summary>待机头上下偏移（±0.004 单位，与躯干反相更像呼吸）。</summary>
        public static float BreathHeadOffsetY(float timeSeconds, int professionIndex)
        {
            return BreathHeadOffset * Mathf.Sin(BreathPhase(timeSeconds, professionIndex) + Mathf.PI);
        }

        /// <summary>待机手臂微摆角度（度）。</summary>
        public static float BreathArmSwing(float timeSeconds, int professionIndex)
        {
            return BreathArmSwingDegrees * Mathf.Sin(BreathPhase(timeSeconds, professionIndex));
        }

        /// <summary>移动上下 bob（同一相位的起伏，频率 = 2 倍步频：左右脚各一次）。</summary>
        public static float MoveBob(float timeSeconds)
        {
            float phase = 2f * Mathf.PI * timeSeconds / (MoveStepSeconds * 0.5f);
            return Mathf.Abs(Mathf.Sin(phase)) * MoveBobOffset;
        }

        /// <summary>移动腿部摆动（左右反相）。<paramref name="phaseSign"/> 左 +1 / 右 −1。</summary>
        public static float WalkLegSwing(float timeSeconds, float phaseSign)
        {
            float phase = 2f * Mathf.PI * timeSeconds / MoveStepSeconds;
            return phaseSign * WalkLegSwingDegrees * Mathf.Sin(phase);
        }

        /// <summary>移动手臂摆动（与同侧腿反相）。</summary>
        public static float WalkArmSwing(float timeSeconds, float phaseSign)
        {
            float phase = 2f * Mathf.PI * timeSeconds / MoveStepSeconds;
            return -phaseSign * WalkArmSwingDegrees * Mathf.Sin(phase);
        }

        /// <summary>
        /// 投掷各阶段在 <paramref name="elapsed"/> 时刻的躯干俯仰角（度，正 = 前倾）。
        /// 蓄力 0→−8°、释放 −8°→+12°（释放帧取前倾最大值）、恢复 +12°→0。
        /// </summary>
        public static float ThrowLeanDegrees(CrewVisualState phase, float elapsed)
        {
            switch (phase)
            {
                case CrewVisualState.ThrowCharge:
                {
                    float t = Mathf.Clamp01(elapsed / ThrowChargeSeconds);
                    return Mathf.Lerp(0f, ThrowChargeLeanDegrees, SmoothStep(t));
                }
                case CrewVisualState.ThrowRelease:
                {
                    float t = Mathf.Clamp01(elapsed / ThrowReleaseSeconds);
                    return Mathf.Lerp(ThrowChargeLeanDegrees, ThrowReleaseLeanDegrees, SmoothStep(t));
                }
                case CrewVisualState.ThrowRecover:
                {
                    float t = Mathf.Clamp01(elapsed / ThrowRecoverSeconds);
                    return Mathf.Lerp(ThrowReleaseLeanDegrees, 0f, SmoothStep(t));
                }
                default:
                    return 0f;
            }
        }

        /// <summary>投掷阶段的手臂（右臂）俯仰角（度）：蓄力后拉（正），释放前甩（负）。</summary>
        public static float ThrowArmDegrees(CrewVisualState phase, float elapsed)
        {
            switch (phase)
            {
                case CrewVisualState.ThrowCharge:
                {
                    float t = Mathf.Clamp01(elapsed / ThrowChargeSeconds);
                    return Mathf.Lerp(0f, 70f, SmoothStep(t));
                }
                case CrewVisualState.ThrowRelease:
                {
                    float t = Mathf.Clamp01(elapsed / ThrowReleaseSeconds);
                    return Mathf.Lerp(70f, -55f, SmoothStep(t));
                }
                case CrewVisualState.ThrowRecover:
                {
                    float t = Mathf.Clamp01(elapsed / ThrowRecoverSeconds);
                    return Mathf.Lerp(-55f, 0f, SmoothStep(t));
                }
                default:
                    return 0f;
            }
        }

        /// <summary>投掷状态下手持物是否已脱手（释放帧之后），供表现层隐藏/交给物理。</summary>
        public static bool IsHeldItemReleased(CrewVisualState phase, float elapsed)
        {
            if (phase == CrewVisualState.ThrowRelease)
                return elapsed >= ThrowReleaseSeconds * 0.35f; // 释放帧（约 1/3 处甩出）
            if (phase == CrewVisualState.ThrowRecover)
                return true;
            return false;
        }

        /// <summary>受击白闪强度（0-1，0.08s 内线性衰减）。</summary>
        public static float HitFlashStrength(float elapsed)
        {
            if (elapsed < 0f || elapsed >= HitFlashSeconds)
                return 0f;
            return 1f - elapsed / HitFlashSeconds;
        }

        /// <summary>受击后仰角（度，随总时长衰减）。</summary>
        public static float HitRecoilDegreesAt(float elapsed)
        {
            float t = Mathf.Clamp01(elapsed / HitTotalSeconds);
            return HitRecoilDegrees * (1f - t);
        }

        /// <summary>死亡倒地角（度，0.5s 内倒到 90°）。</summary>
        public static float DeathFallAngle(float elapsed)
        {
            float t = Mathf.Clamp01(elapsed / DeathFallSeconds);
            return DeathFallDegrees * SmoothStep(t);
        }

        /// <summary>死亡/落水收拢缩放（0.5s 内从 1 → 0.15，模拟淡出；shader 无 alpha 通道，见实现说明）。</summary>
        public static float FadeScale(float elapsed, float fadeSeconds)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(fadeSeconds, 1e-4f));
            return Mathf.Lerp(1f, 0.15f, SmoothStep(t));
        }

        /// <summary>落水下沉位移（负值，单位）。</summary>
        public static float DrownSinkOffset(float elapsed)
        {
            return -DrownSinkSpeed * Mathf.Max(elapsed, 0f);
        }

        /// <summary>落水左右摇晃角（度，两个不同频率叠加更像失控）。</summary>
        public static float DrownSwayDegreesAt(float elapsed)
        {
            float a = Mathf.Sin(elapsed * 7.5f) * DrownSwayDegrees;
            float b = Mathf.Sin(elapsed * 3.1f + 1.2f) * DrownSwayDegrees * 0.5f;
            return a + b;
        }

        /// <summary>落水沉没比例（0-1，用于判定表现是否结束）。</summary>
        public static float DrownProgress(float elapsed, float totalSeconds)
        {
            return Mathf.Clamp01(elapsed / Mathf.Max(totalSeconds, 1e-4f));
        }

        /// <summary>状态名义时长（秒）；非限时状态（Idle/Move）返回 0。</summary>
        public static float Duration(CrewVisualState state)
        {
            switch (state)
            {
                case CrewVisualState.ThrowCharge: return ThrowChargeSeconds;
                case CrewVisualState.ThrowRelease: return ThrowReleaseSeconds;
                case CrewVisualState.ThrowRecover: return ThrowRecoverSeconds;
                case CrewVisualState.Hit: return HitTotalSeconds;
                case CrewVisualState.Death: return DeathTotalSeconds;
                case CrewVisualState.Drown: return DrownSecondsMax;
                default: return 0f;
            }
        }

        /// <summary>判断限时状态是否播完。</summary>
        public static bool IsFinished(CrewVisualState state, float elapsed)
        {
            float duration = Duration(state);
            return duration > 0f && elapsed >= duration;
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        /// <summary>平滑阶跃（3t²−2t³），让姿态过渡不机械。</summary>
        public static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
