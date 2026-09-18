using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// HUD 动效规则（纯 C#，可无头测试）——曲线与时长档位的唯一真值来源，
    /// 驱动层 <see cref="UiMotion"/> 只喂时间并把值写回 UGUI 控件。
    ///
    /// 【设计口径】
    ///  · **UI 动画一律用 unscaled 时间**（驱动层喂 <c>Time.unscaledDeltaTime</c>）：
    ///    hit-stop 顿帧的意义是"世界停了、手没停"，UI 若跟着冻结，顿帧会连反馈一起吃掉。
    ///    因此本类只提供「归一化时间 → 值」的纯曲线，不含任何引擎状态。
    ///  · 离开要比进入快：面板淡出 0.10s &lt; 滑入 0.16s——离开的动画只为"不突兀"，
    ///    拖久了反而有挡路感。
    ///  · 数值全部 **AI 提案/待定**，需实玩手感验收（改起来只动本文件的常量）。
    /// </summary>
    public static class UiMotionRules
    {
        // ------------------------------------------------------------------
        // 按压 punch（按钮按下 / 回合横幅弹出 共用一条曲线）
        // ------------------------------------------------------------------

        /// <summary>punch 总时长（秒）。快于 0.15s 会看不清、慢于 0.25s 会黏手。</summary>
        public const float PunchSeconds = 0.18f;

        /// <summary>起始缩放（从略小弹起，模拟"按下"的残余）。</summary>
        public const float PunchFromScale = 0.90f;

        /// <summary>过冲缩放。1.06 ≈ 肉眼可辨但不抢戏的上限。</summary>
        public const float PunchOvershootScale = 1.06f;

        /// <summary>曲线峰位（归一化时间）：前段从起始冲到过冲，后段回稳到 1。</summary>
        public const float PunchPeakT = 0.4f;

        /// <summary>
        /// punch 曲线：<c>f(0)=fromScale</c>，前段 ease-out 冲到 <c>overshootScale</c>，
        /// 后段 cosine ease-in-out 回稳到 1，<c>f(1)=1</c> 且终点导数为零（不抖）。
        /// </summary>
        public static float PunchCurve(float t01, float fromScale, float overshootScale)
        {
            float t = Mathf.Clamp01(t01);
            if (t < PunchPeakT)
            {
                float u = t / PunchPeakT;
                u = 1f - (1f - u) * (1f - u);                    // ease-out quad
                return Mathf.Lerp(fromScale, overshootScale, u);
            }

            float v = (t - PunchPeakT) / (1f - PunchPeakT);
            v = 0.5f - 0.5f * Mathf.Cos(v * Mathf.PI);          // cosine ease-in-out
            return Mathf.Lerp(overshootScale, 1f, v);
        }

        // ------------------------------------------------------------------
        // 面板滑入 / 淡出
        // ------------------------------------------------------------------

        /// <summary>面板出现时长（滑入 + 淡入共用）。</summary>
        public const float PanelShowSeconds = 0.16f;

        /// <summary>面板消失时长（只淡出，不位移——离开要快且安静）。</summary>
        public const float PanelHideSeconds = 0.10f;

        /// <summary>滑入起始的纵向偏移（px，1080p 参考系）：从下方 24px 滑上来。</summary>
        public const float PanelSlideOffsetPixels = 24f;

        /// <summary>ease-out cubic：f(0)=0，f(1)=1，单调增，前快后慢。</summary>
        public static float EaseOutCubic(float t01)
        {
            float t = Mathf.Clamp01(t01);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        /// <summary>ease-in quad：f(0)=0，f(1)=1，单调增，前慢后快（用于淡出，起手轻）。</summary>
        public static float EaseInQuad(float t01)
        {
            float t = Mathf.Clamp01(t01);
            return t * t;
        }

        // ------------------------------------------------------------------
        // 血条滚动（名册 28 帧血条的增减不跳变）
        // ------------------------------------------------------------------

        /// <summary>
        /// 指数趋近速率（1/秒）：12 意味着 0.25s 内走完约 95% 差距——
        /// 伤害数字先跳、血条紧随的"追赶感"主要靠它。
        /// </summary>
        public const float HealthDrainSpeedPerSecond = 12f;

        /// <summary>吸附阈值：差值小于 28 帧血条的半格（0.5/28≈0.018）就归位，避免永动残影。</summary>
        public const float HealthDrainSnapEpsilon = 0.0015f;

        /// <summary>
        /// 帧率无关的指数趋近：<c>next = lerp(current, target, 1-e^(-speed·dt))</code>，
        /// 末段小于 <see cref="HealthDrainSnapEpsilon"/> 直接归位。
        /// </summary>
        public static float ApproachExponential(float current, float target, float deltaSeconds, float speedPerSecond)
        {
            if (speedPerSecond <= 0f)
                return target;

            float dt = Mathf.Max(0f, deltaSeconds);
            float next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-speedPerSecond * dt));
            return Mathf.Abs(target - next) < HealthDrainSnapEpsilon ? target : next;
        }

        // ------------------------------------------------------------------
        // damage ghost（血条受击白条残影，格斗游戏经典反馈）
        // ------------------------------------------------------------------

        /// <summary>
        /// ghost 残影的追赶速率（1/秒）。比 <see cref="HealthDrainSpeedPerSecond"/> 慢一档——
        /// 主填充先跳到新血量，白条在后面"拖"出一段受击痕迹，约 1s 内追平。
        /// </summary>
        public const float GhostDrainSpeedPerSecond = 2.2f;

        /// <summary>
        /// ghost 的单帧步进规则：<b>只降不升</b>—— 掉血时慢速追（残影语义），
        /// 回血 / 重建时直接钉到新值（不留白）。返回更新后的 ghost 比例。
        /// </summary>
        public static float StepGhost(float ghost, float fill, float deltaSeconds)
        {
            if (fill >= ghost)
                return fill;   // 回血或持平：白条立即让位
            return ApproachExponential(ghost, fill, deltaSeconds, GhostDrainSpeedPerSecond);
        }

        // ------------------------------------------------------------------
        // pop / back-out（回合徽章 / 星级 / 弹窗的"弹一下"）
        // ------------------------------------------------------------------

        /// <summary>pop 总时长（秒）：比 punch 略慢，因为过冲更大幅度。</summary>
        public const float PopSeconds = 0.24f;

        /// <summary>pop 过冲缩放（1.18 = 明显但不含糊）。</summary>
        public const float PopOvershootScale = 1.18f;

        /// <summary>back-out 缓动（f(0)=0，f(1)=1，先过冲到 &gt;1 再回落——"弹一下"的来源）。</summary>
        public static float EaseOutBack(float t01)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float t = Mathf.Clamp01(t01);
            float inv = t - 1f;
            return 1f + c3 * inv * inv * inv + c1 * inv * inv;
        }

        /// <summary>
        /// pop 缩放曲线：从 0.6 起手，back-out 逼近 1，叠加一个半程正弦过冲脉冲
        /// （峰值 ≈ 1.04 × 1.18 ≈ 1.23；<c>f(1)=1</c> 且两端脉冲为零，不抖）。
        /// </summary>
        public static float PopScale(float t01)
        {
            float t = Mathf.Clamp01(t01);
            float baseScale = 0.6f + 0.4f * EaseOutBack(t);
            float pulse = 1f + (PopOvershootScale - 1f) * Mathf.Sin(Mathf.PI * t);
            return baseScale * pulse;
        }
    }
}
