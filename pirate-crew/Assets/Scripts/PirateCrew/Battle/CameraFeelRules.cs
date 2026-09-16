using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 相机"跟随弹体"状态机的状态（见 <see cref="CameraFeelRules.Advance"/>）。
    /// </summary>
    public enum CameraFollowState
    {
        /// <summary>不跟随：相机只聚焦当前回合行动角色（默认态）。</summary>
        None = 0,

        /// <summary>跟随飞行中的弹体 / 被抛出的角色。</summary>
        FollowProjectile = 1,

        /// <summary>命中/引爆瞬间：在落点短暂停留（让玩家看清效果），不立即回焦。</summary>
        DetonationHold = 2,

        /// <summary>从落点缓动回战场焦点。</summary>
        ReturnToFocus = 3,
    }

    /// <summary>
    /// 驱动 <see cref="CameraFollowState"/> 迁移的触发源。全部来自现有 EventBus 事件，
    /// 不新增事件（见 docs/EventBus事件契约.md）。
    /// </summary>
    public enum CameraFollowTrigger
    {
        /// <summary>无触发（仅用于按时间推进超时/停留计时）。</summary>
        None = 0,

        /// <summary>一次投掷/发射已发生（<see cref="BattleEvents.ActionSelected"/>）。</summary>
        ShotFired = 1,

        /// <summary>弹体命中/引爆（<see cref="BattleEvents.ProjectileDetonated"/>）。</summary>
        Detonated = 2,

        /// <summary>被跟随目标消失或已静止。</summary>
        TargetLost = 3,

        /// <summary>外部请求聚焦（回合开始 / 选中角色，<see cref="BattleEvents.CameraFocusRequested"/>）。</summary>
        FocusRequested = 4,
    }

    /// <summary>跟随状态机的时间参数（秒）。全部为 <b>提案/待定</b>（原版无镜头跟随机制，见类头）。</summary>
    public readonly struct CameraFeelTimings
    {
        /// <summary>弹体跟随的最长时长（兜底，防止弹体卡住时相机不回来）。</summary>
        public readonly float FollowTimeoutSeconds;

        /// <summary>命中/引爆后在落点停留的时长。</summary>
        public readonly float DetonationHoldSeconds;

        /// <summary>从落点缓动回焦点的时长（状态机侧；实际缓动是指数平滑，见 FocusLerpPerSecond）。</summary>
        public readonly float ReturnSeconds;

        public CameraFeelTimings(float followTimeoutSeconds, float detonationHoldSeconds, float returnSeconds)
        {
            FollowTimeoutSeconds = followTimeoutSeconds;
            DetonationHoldSeconds = detonationHoldSeconds;
            ReturnSeconds = returnSeconds;
        }
    }

    /// <summary>一次震屏的强度参数（相机局部空间）。</summary>
    public readonly struct ShakeProfile
    {
        /// <summary>峰值位移（世界单位，作用于相机局部水平/竖直轴）。</summary>
        public readonly float Amplitude;

        /// <summary>峰值滚转（度）。</summary>
        public readonly float RollDegrees;

        /// <summary>总时长（秒）。</summary>
        public readonly float DurationSeconds;

        /// <summary>振荡频率（Hz）。</summary>
        public readonly float FrequencyHz;

        /// <summary>是否有效（振幅与滚转都为 0 时视为无需震屏）。</summary>
        public bool IsActive => Amplitude > 0f || RollDegrees > 0f;

        public ShakeProfile(float amplitude, float rollDegrees, float durationSeconds, float frequencyHz)
        {
            Amplitude = amplitude;
            RollDegrees = rollDegrees;
            DurationSeconds = durationSeconds;
            FrequencyHz = frequencyHz;
        }
    }

    /// <summary>
    /// 战斗"手感"规则的纯 C# 汇总层（震屏曲线 / 聚焦缓动 / 跟随状态机 / 顿帧安全上限）。
    ///
    /// 【分层】本类不引用 <c>MonoBehaviour</c> / <c>Cinemachine</c>，也不实例化 GameObject，
    ///         可在无头验证台（<c>external/harness-*</c>）直接断言；胶水层是
    ///         <see cref="BattleCameraController"/>（订阅 EventBus、读写 Transform/Lens/Time）。
    ///
    /// 【对应章节】§5.3（爆炸 falloff 形状，震屏强度借它的线性衰减）、
    ///             §8.1（相机优先级：AI 决策中停止滚动 → 本类的"旁观"态）、
    ///             §3.1（回合节奏 inactivity &gt; 10 帧 ≈ 0.4s → 聚焦时长的依据）。
    ///
    /// 【数值出处】原版 Flash **没有任何镜头震动/跟随/顿帧机制**（只有 §8.1 的滚动与 panToCharacter），
    ///   因此本类全部强度/时长参数都是<b>提案/待定</b>，依据是"对齐原版回合节奏"：
    ///   · 原版回合推进阈值 = 10 帧 @25fps = 0.4s（§3.1），所以任何聚焦动画应在 ≲0.4s 内完成；
    ///   · 原版 AI 决策后先给镜头再执行（§6.1），Unity 侧 <c>AiController.executeDelayFrames = 12</c>
    ///     ≈ 0.2s @60fps，故跟随回焦也要短；
    ///   · 震屏幅度取"不遮挡 30px 选中判定"的量级：峰值 ≈ 0.7 世界单位 ≈ 11px
///     （px 口径不变；格 1→2 单位后世界值 ×2，见 <see cref="DefaultMaxShakeAmplitude"/>）。
    /// </summary>
    public static class CameraFeelRules
    {
        // ------------------------------------------------------------------
        // 震屏（§5.3 falloff 形状借用于强度曲线）
        // ------------------------------------------------------------------

        /// <summary>默认震屏时长（秒，提案）。0.3s ≈ 原版 7.5 帧。</summary>
        public const float DefaultShakeDurationSeconds = 0.30f;

        /// <summary>默认震屏频率（Hz，提案；≈ 每 3.3 帧一次往复）。</summary>
        public const float DefaultShakeFrequencyHz = 18f;

        /// <summary>默认震屏峰值位移（世界单位，提案）：0.7 ≈ 11px（px 口径不变），小于 30px 选中半径。</summary>
        public const float DefaultMaxShakeAmplitude = 0.7f;

        /// <summary>默认峰值滚转（度，提案）：1.2° 属"轻微"，不产生晕动。</summary>
        public const float DefaultMaxShakeRollDegrees = 1.2f;

        /// <summary>震屏强度衰减的射程倍数：落到爆心 <c>radius × 该值</c> 处衰减到 0（提案）。</summary>
        public const float ShakeRangeRadiusMultiplier = 2f;

        /// <summary>低于该衰减值的请求不触发震屏（避免全图远端的无关爆炸也抖一下）。</summary>
        public const float MinShakeFalloff = 0.02f;

        /// <summary>
        /// 震屏强度衰减：<c>clamp01(1 − d / (radius × 2))</c>。
        /// 形状沿用 §5.3 爆炸伤害的线性 falloff（<c>1 − d/radius</c>），
        /// 但射程放大到 2×radius，使半径边缘之外的命中仍有可感知的轻微反馈。
        /// </summary>
        public static float ShakeFalloff(float distanceWorld, float explosionRadiusWorld)
        {
            float range = Mathf.Max(1e-4f, explosionRadiusWorld * ShakeRangeRadiusMultiplier);
            return Mathf.Clamp01(1f - Mathf.Max(0f, distanceWorld) / range);
        }

        /// <summary>
        /// 按"爆心到相机的距离 / 爆炸半径"生成震屏参数。距离越远、半径越小 → 越弱。
        /// </summary>
        public static ShakeProfile ExplosionShake(
            float distanceWorld, float explosionRadiusWorld,
            float maxAmplitude, float maxRollDegrees, float durationSeconds,
            float frequencyHz = DefaultShakeFrequencyHz)
        {
            float falloff = ShakeFalloff(distanceWorld, explosionRadiusWorld);
            if (falloff < MinShakeFalloff)
                return new ShakeProfile(0f, 0f, 0f, frequencyHz);

            return new ShakeProfile(
                maxAmplitude * falloff,
                maxRollDegrees * falloff,
                Mathf.Max(0f, durationSeconds),
                frequencyHz);
        }

        /// <summary>递减包络 <c>(1 − t)²</c>（t 归一化到 [0,1]）。t≥1 归零。</summary>
        public static float ShakeEnvelope(float normalizedTime)
        {
            float inv = 1f - Mathf.Clamp01(normalizedTime);
            return inv * inv;
        }

        /// <summary>
        /// 相机局部平面位移采样（x = 相机右、y = 相机上）。
        /// 衰减 <see cref="ShakeEnvelope"/> × 两个不同频率的正弦（非整数倍，避免 xy 同步成一条直线）。
        /// 每轴绝对值 ≤ amplitude；<paramref name="elapsed"/> 为 0 或 ≥ duration 时返回零。
        /// </summary>
        public static Vector2 ShakeOffset2D(
            float elapsed, float duration, float amplitude, float frequencyHz, float phase)
        {
            if (duration <= 0f || amplitude <= 0f || elapsed <= 0f || elapsed >= duration)
                return Vector2.zero;

            float env = ShakeEnvelope(elapsed / duration);
            float w = 2f * Mathf.PI * frequencyHz * elapsed;
            float x = Mathf.Sin(w + phase);
            float y = Mathf.Sin(w * 0.77f + phase * 1.7f);
            return new Vector2(x, y) * (amplitude * env);
        }

        /// <summary>滚转采样（度）：衰减包络 × 第三个频率的正弦，绝对值 ≤ maxRollDegrees。</summary>
        public static float ShakeRoll(
            float elapsed, float duration, float maxRollDegrees, float frequencyHz, float phase)
        {
            if (duration <= 0f || maxRollDegrees <= 0f || elapsed <= 0f || elapsed >= duration)
                return 0f;

            float env = ShakeEnvelope(elapsed / duration);
            float w = 2f * Mathf.PI * frequencyHz * elapsed;
            return Mathf.Sin(w * 1.13f + phase) * maxRollDegrees * env;
        }

        /// <summary>新震屏是否应替换进行中的震屏：强者的有效振幅更大才替换（不叠加，避免 AoE 连抖）。</summary>
        public static bool ShouldReplaceShake(float currentEffectiveAmplitude, float candidateAmplitude)
        {
            return candidateAmplitude > currentEffectiveAmplitude + 1e-5f;
        }

        // ------------------------------------------------------------------
        // 聚焦缓动 / 轻度推近 / 旁观
        // ------------------------------------------------------------------

        /// <summary>指数平滑的默认到位比例（0.9 → 该时长内走完 90%）。</summary>
        public const float FocusTargetFraction = 0.9f;

        /// <summary>
        /// 由"期望时长"反推指数平滑速率 k（1/s）：<c>k = −ln(1 − 0.9) / duration</c>。
        /// 依据：原版回合推进阈值 10 帧 @25fps = 0.4s（§3.1），故默认取 duration=0.4s → k≈5.76。
        /// 现有场景参数 <c>focusLerpPerSecond = 6</c> 对应 0.384s，与该节奏基本一致。
        /// </summary>
        public static float FocusLerpPerSecond(float durationSeconds)
        {
            if (durationSeconds <= 0f)
                return 0f;
            return -Mathf.Log(1f - FocusTargetFraction) / durationSeconds;
        }

        /// <summary>指数平滑本帧插值系数 <c>1 − exp(−k·dt)</c>（与既有 LateUpdate 的写法同源）。</summary>
        public static float ApproachAlpha(float lerpPerSecond, float deltaTime)
        {
            if (lerpPerSecond <= 0f || deltaTime <= 0f)
                return 0f;
            return 1f - Mathf.Exp(-lerpPerSecond * deltaTime);
        }

        /// <summary>选中/回合聚焦时的轻微"推近"峰值（度，提案）：FOV 收窄 1.5° 即有明显推近感。</summary>
        public const float SelectionPushInDegrees = 1.5f;

        /// <summary>"推近"单程时长（秒，提案）：0.3s 略短于回合节奏 0.4s，不拖回合。</summary>
        public const float SelectionPushInDurationSeconds = 0.30f;

        /// <summary>
        /// FOV 推近曲线：<c>base − peak·sin(π·t)</c>，t=0 / t≥1 回到 base，t=0.5 达峰值。
        /// <b>为什么用 FOV 而不是改距离</b>：PlayMode 用例断言 Transposer 距离恒为 18、俯角 45、
        /// offset.x=0（<c>BattleSceneWiringTests.AssertPerspectiveTiltedCamera</c>），改 FOV 不动这些量，
        /// 断言仍绿；改距离会直接违反。
        /// </summary>
        public static float PushInFov(float baseFov, float peakDegrees, float elapsed, float duration)
        {
            if (duration <= 0f || peakDegrees <= 0f)
                return baseFov;

            float t = Mathf.Clamp01(elapsed / duration);
            return baseFov - peakDegrees * Mathf.Sin(Mathf.PI * t);
        }

        /// <summary>"旁观"态（AI 回合）的 FOV 外扩量（度，提案）：略拉远，体现"轮到对手"。</summary>
        public const float SpectatorFovDeltaDegrees = 1.5f;

        /// <summary>"旁观"态的聚焦速率缩放（提案）：×0.6 更慢、更"看戏"，不抢操作节奏。</summary>
        public const float SpectatorFocusScale = 0.6f;

        /// <summary>旁观 FOV：旁观时 <c>base + delta</c>，否则为 base（不写回时不显示变化）。</summary>
        public static float SpectatorFov(float baseFov, float deltaDegrees, bool spectator)
        {
            return spectator ? baseFov + deltaDegrees : baseFov;
        }

        // ------------------------------------------------------------------
        // 落水 / 死亡的下压
        // ------------------------------------------------------------------

        /// <summary>落水时相机焦点下压的位移（世界单位，提案）：0.9 ≈ 14px（px 口径不变），克制、不抢戏。</summary>
        public const float DrownDipWorldUnits = 0.9f;

        /// <summary>下压持续时长（秒，提案）。</summary>
        public const float DrownDipDurationSeconds = 0.5f;

        /// <summary>落水后镜头停留在落水点的时长（秒，提案）：够看清下沉，随即回焦。</summary>
        public const float DrownFocusHoldSeconds = 0.45f;

        /// <summary>普通死亡（非落水）的轻微震屏峰值（世界单位，提案）：0.36 ≈ 5.8px（px 口径不变）。</summary>
        public const float DeathShakeAmplitude = 0.36f;

        /// <summary>
        /// 下压曲线：<c>−amount · (1 − t)²</c>（向下为负），t≥1 回到 0。
        /// 起点立即下压到峰值再缓回，模拟"镜头随尸体沉一下"。
        /// </summary>
        public static float DrownDipOffset(float elapsed, float duration, float amountWorldUnits)
        {
            if (duration <= 0f || amountWorldUnits <= 0f || elapsed < 0f || elapsed >= duration)
                return 0f;
            return -amountWorldUnits * ShakeEnvelope(elapsed / duration);
        }

        // ------------------------------------------------------------------
        // Scope 瞄准（M4 §3.2，提案/待定：战舰世界式瞄准仪式，数值待手感实测调参）
        // ------------------------------------------------------------------

        /// <summary>Scope 模式的目标 FOV（度，提案）：60 → 28，长焦"瞄准仪式"。</summary>
        public const float ScopeTargetFov = 28f;

        /// <summary>Scope 进入/退出的平滑收敛时长（秒，提案）：0.25s。</summary>
        public const float ScopeBlendSeconds = 0.25f;

        /// <summary>Scope 模式的瞄准灵敏度缩放（提案）：炮台 yaw/力度增速/滚轮微调同步 ×0.4。</summary>
        public const float ScopeAimSensitivityScale = 0.4f;

        /// <summary>
        /// Scope 混合后的 FOV：<c>lerp(base, 28, blend01)</c>。blend 由胶水层按
        /// <see cref="ScopeBlendSeconds"/> 线性推进（进入与退出同一条曲线，"退出平滑回"）。
        /// </summary>
        public static float ScopeFov(float baseFov, float blend01)
        {
            return Mathf.Lerp(baseFov, ScopeTargetFov, Mathf.Clamp01(blend01));
        }

        // ------------------------------------------------------------------
        // 力度-镜头耦合（M4 §3.2，提案/待定：蓄力越大相机越拉远，松手恢复）
        // ------------------------------------------------------------------

        /// <summary>
        /// 蓄力期间的目标距离：近档（特写）→ 全景档随蓄力比例线性映射。
        /// 端点由胶水层给（近档 = 特写距离 12；全景 = 地图跨度档），曲线恒为线性。
        /// </summary>
        public static float ChargeZoomDistance(float nearDistance, float panoramaDistance, float power01)
        {
            return Mathf.Lerp(nearDistance, panoramaDistance, Mathf.Clamp01(power01));
        }

        // ------------------------------------------------------------------
        // 弹体追焦（M4 §3.2：抛出后镜头轻跟弹体，带迟滞）
        // ------------------------------------------------------------------

        /// <summary>
        /// 跟随弹体时的聚焦平滑缩放（提案）：&lt;1 = 比"回焦点"更慢、更轻，形成迟滞感；
        /// 落点震屏（爆炸/伤害）逻辑不受影响，仍按各自规则施加。
        /// </summary>
        public const float ProjectileFollowFocusScale = 0.75f;

        // ------------------------------------------------------------------
        // 跟随状态机
        // ------------------------------------------------------------------

        /// <summary>默认跟随时间参数（提案；依据见类头）。</summary>
        public static CameraFeelTimings DefaultFollowTimings
            => new CameraFeelTimings(2.5f, 0.35f, 0.35f);

        /// <summary>
        /// 跟随状态机一步迁移。纯函数：同样的 (state, trigger, targetActive, elapsedInState, timings)
        /// 必得同样的结果，便于无头断言。
        ///
        /// <para><b>FocusRequested 优先级最高</b>：任何时候外部请求聚焦（回合开始 / 选中角色）都立即
        /// 取消跟随回到 Default 态——保证回合推进/选中操作绝不会被跟随拖住。</para>
        ///
        /// <paramref name="targetActive"/> = true 表示被跟目标仍在运动（弹体 <c>IsInFlight</c> /
        /// 被抛角色 <c>IsMoving()</c>）；false 触发 <see cref="CameraFollowTrigger.TargetLost"/> 语义
        /// （静止/消失即回焦）。
        /// </summary>
        public static CameraFollowState Advance(
            CameraFollowState state, CameraFollowTrigger trigger, bool targetActive,
            float elapsedInState, CameraFeelTimings timings)
        {
            if (trigger == CameraFollowTrigger.FocusRequested)
                return CameraFollowState.None;

            switch (state)
            {
                case CameraFollowState.None:
                    if (trigger == CameraFollowTrigger.ShotFired)
                        return CameraFollowState.FollowProjectile;
                    if (trigger == CameraFollowTrigger.Detonated)
                        return CameraFollowState.DetonationHold;
                    return CameraFollowState.None;

                case CameraFollowState.FollowProjectile:
                    if (trigger == CameraFollowTrigger.Detonated)
                        return CameraFollowState.DetonationHold;
                    if (trigger == CameraFollowTrigger.TargetLost || !targetActive
                        || elapsedInState >= timings.FollowTimeoutSeconds)
                        return CameraFollowState.ReturnToFocus;
                    return CameraFollowState.FollowProjectile;

                case CameraFollowState.DetonationHold:
                    if (elapsedInState >= timings.DetonationHoldSeconds)
                        return CameraFollowState.ReturnToFocus;
                    return CameraFollowState.DetonationHold;

                case CameraFollowState.ReturnToFocus:
                    if (elapsedInState >= timings.ReturnSeconds)
                        return CameraFollowState.None;
                    return CameraFollowState.ReturnToFocus;

                default:
                    return CameraFollowState.None;
            }
        }

        // ------------------------------------------------------------------
        // 顿帧（hitstop）安全上限
        //
        // 【为什么不破坏回合推进】（读 TurnManager / BattleFlowRules 后确认）：
        //   · TurnManager.Update 每渲染帧执行一次 `_inactivityFrames++`，它**不读 timeScale**
        //     （只受 Time.deltaTime 的"值"影响，而帧计数本身照常）；因此压低 timeScale 不会暂停/加快
        //     回合阈值判定，`BattleFlowRules.DecideAdvance` 的 10 帧阈值语义不变。
        //   · 顿帧只在引爆瞬间发生，此时弹体仍在 IsAnythingActive() 为真，`_inactivityFrames` 本就被清零，
        //     不存在"顿帧期间把空闲帧攒够"的情况。
        //   · PhysX 每个固定步仍以 Time.fixedDeltaTime(=1/25s) 积分（timeScale 只改变每秒发生多少步，
        //     不改变单步 dt），故 ThrowTrajectory 与实弹"逐步等价（预览=实弹）"不受影响。
        //   · AiController 的时间片用 Stopwatch（真实时间）、executeDelayFrames 用帧计数，均不受 timeScale 影响。
        // 【唯一真实风险】timeScale 被留在 0/低位未恢复 → 物理停步、IsAnythingActive 恒真、回合死锁。
        //   因此：下限严格 >0（0.05）、时长 ≤2 帧、恢复用 unscaled 计时、OnDisable/OnMatchFinished 兜底恢复、
        //   且 timeScale 已被他人接管（≠1）时一律不施加。
        // ------------------------------------------------------------------

        /// <summary>
        /// 顿帧允许的最低 timeScale（提案）。必须 <b>严格大于 0</b>：
        /// timeScale=0 会让 PhysX 完全停步，<c>BattleController.IsAnythingActive()</c> 因弹体"仍在飞"
        /// 恒为 true、<c>TurnManager.Update</c> 的 inactivity 永远被清零，回合推进即死锁。
        /// 0.05 仍留出物理可推进余量，且 0.05 至少 ±1 个物理步不会改变"预览=实弹"的逐步等价。
        /// </summary>
        public const float MinHitStopTimeScale = 0.05f;

        /// <summary>
        /// 顿帧最长时长（秒，提案）= 2 帧 @25fps。原版回合节奏以 10 帧（0.4s）为推进单位（§3.1），
        /// 2 帧的顿帧不改变任何阈值判定；再长就会让玩家误以为"卡了"或与 AI 的 30ms 时间片观感冲突。
        /// </summary>
        public const float MaxHitStopSeconds = 2f * LevelGeometry.FrameSeconds;

        /// <summary>默认顿帧时长（秒，提案）：1 帧 @25fps，极短、只在爆点"钉"一下。</summary>
        public const float DefaultHitStopSeconds = LevelGeometry.FrameSeconds;

        /// <summary>默认顿帧 timeScale（提案）：压到 0.15 再弹回。</summary>
        public const float DefaultHitStopTimeScale = 0.15f;

        /// <summary>把请求时长夹到 [0, <see cref="MaxHitStopSeconds"/>]。</summary>
        public static float ClampHitStopDuration(float requestedSeconds)
        {
            if (requestedSeconds <= 0f)
                return 0f;
            return Mathf.Min(requestedSeconds, MaxHitStopSeconds);
        }

        /// <summary>把请求 timeScale 夹到 [<see cref="MinHitStopTimeScale"/>, 1]。</summary>
        public static float ClampHitStopTimeScale(float requestedTimeScale)
        {
            return Mathf.Clamp(requestedTimeScale, MinHitStopTimeScale, 1f);
        }

        /// <summary>帧数 → 顿帧时长（秒），并按安全上限夹取。</summary>
        public static float HitStopDurationFromFrames(int frames)
        {
            if (frames <= 0)
                return 0f;
            return ClampHitStopDuration(frames * LevelGeometry.FrameSeconds);
        }

        /// <summary>
        /// 参数是否在安全范围内（供运行时防御：越界就不施加顿帧，而不是赌它没事）。
        /// </summary>
        public static bool IsHitStopSafe(float durationSeconds, float timeScale)
        {
            return durationSeconds >= 0f
                   && durationSeconds <= MaxHitStopSeconds + 1e-6f
                   && timeScale >= MinHitStopTimeScale - 1e-6f
                   && timeScale <= 1f + 1e-6f;
        }

        /// <summary>
        /// 当前是否允许施加顿帧：对局已结束、场景过渡中、或 timeScale 已被他人接管（≠1，如暂停菜单）
        /// 时都不施加，避免与别的系统抢 timeScale。
        /// </summary>
        public static bool ShouldApplyHitStop(bool matchOver, bool sceneTransitioning, float currentTimeScale)
        {
            if (matchOver || sceneTransitioning)
                return false;
            return Mathf.Abs(currentTimeScale - 1f) < 1e-4f;
        }
    }
}
