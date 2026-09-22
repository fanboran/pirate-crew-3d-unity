using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗相机 **Driver**（三件套之三，主相机的唯一写入者）：
    /// 订阅 EventBus、维护全部表现状态机（跟随/震屏/顿帧/下压/推近/旁观/Scope 混合），
    /// 每帧把它们组装成一个 <see cref="CameraFrame"/> 直接写到主相机的 transform/lens 上。
    ///
    /// 【三件套分工】<see cref="CameraFraming"/>（纯数学：基准机位/偏移/OrthoSize 合成，无头可测）→
    /// <see cref="CameraInputReader"/>（只读输入出增量）→ 本类（唯一写入者）。
    /// **除本类外任何系统不得写主相机的 transform / orthographicSize / near / far**
    /// （PlayMode 守卫测试逐帧比对相机实况与 <see cref="LastFrame"/>）。
    ///
    /// 【去 Cinemachine 化（2026-09-23，创始人认可方案）】正交竞技场相机 = 定长偏移 + 平滑跟随 +
    /// 抬高看点，全部数学在 <see cref="CameraFraming"/>；原 Brain 的掩码筛选与镜头应用链
    /// 造成过两次实机事故（r13 修复档案），随本重构退役。
    /// 与原链路的**逐位等价关系**（实机探针 + CM 2.9.7 源码共同验证，2026-09-23）：
    ///   · 原链 = Transposer（阻尼 0）只写位置 + Aim 档为空 ⇒ 主相机**朝向恒为烘焙机位**，
    ///     右键环绕只平移轨道、视线不变——本类 <see cref="CameraFraming.ComputeRotation"/> 保持该行为；
    ///   · 原链的 lens near/far 由 Brain 每帧推送（0.1/200）——本类在 Awake 写一次同值；
    ///   · 跟随弹体时原链把 vcam.Follow 切到弹体（无阻尼 ⇒ 弹体位置精确上屏）——
    ///     本类在 FollowProjectile 态直接用弹体位置做焦点（不平滑），其余态用平滑焦点；
    ///   · 原链把震屏/下压写在"相机目标"上再由 Transposer 叠加——本类在帧组装时叠加，和相同。
    ///
    /// 【对应章节】§3.2（panToCharacter）、§4.5（选中反馈）、§8.1（AI 决策中停止滚动）、
    ///             §5.3（爆炸 falloff 借形）、M4 §3.2（Scope/力度-镜头耦合/弹体追焦）。
    ///
    /// 【安全底线（勿破坏）】
    ///   · 震屏只加在最终位置上，且**玩家按住左键（拖拽瞄准）时不施加**；
    ///     <see cref="FocusPoint"/> 返回**去震屏**的干净位置，不影响"离相机中心最近"判定。
    ///   · 顿帧的 timeScale 安全上限在 <see cref="CameraFeelRules"/>；暂停中不施加、退场兜底恢复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleCameraDriver : MonoBehaviour
    {
        [Header("写入目标（唯一写入者的对象）")]
        [Tooltip("主相机。本类是它 transform/lens 的唯一写入者（像素化 rig 只改掩码/渲染器，不碰取景）。")]
        [SerializeField] Camera mainCamera;

        [Tooltip("输入读取器（同物体自动补建；装配链显式接线）。")]
        [SerializeField] CameraInputReader input;

        [Header("组装引用（手感数据源；留空时跟随/死亡反馈降级，其余仍工作）")]
        [Tooltip("战斗根。用于把 PirateId 解析成角色、把弹体取出做跟随。留空时：震屏与聚焦仍工作，"
                 + "但投掷跟随、落水/死亡的定焦无法定位（需在场景里接线）。")]
        [SerializeField] BattleController battle;

        [Tooltip("瞄准/投掷控制器（同场景显式注入，由 EditorTools.BattleLookupWiring 接线）。"
                 + "本类每帧采样它的 IsScopeActive / IsTurretAiming / ChargeRatio（Scope 视野混合、力度-镜头耦合），"
                 + "是**热路径依赖**——所以必须显式注入，绝不做每帧回退扫描（清退报告 2026-09-21）。")]
        [SerializeField] AimThrowController aimThrow;

        [Header("参数")]
        [Tooltip("聚焦平滑速度（1/s）。默认 6 → 90% 到位约 0.384s，贴合原版 10 帧 @25fps 的回合节奏（§3.1）。")]
        [SerializeField] float focusLerpPerSecond = 6f;

        [Header("震屏（提案/待定，原版无此机制）")]
        [Tooltip("总开关：关闭后命中/爆炸/死亡都不震屏。")]
        [SerializeField] bool enableShake = true;

        [Tooltip("震屏强度整体缩放（1 = 默认）。")]
        [SerializeField] float shakeAmplitudeScale = 1f;

        [Tooltip("峰值位移（世界单位）；默认 0.7 ≈ 11px（px 口径不变），小于 30px 选中半径。")]
        [SerializeField] float maxShakeAmplitude = CameraFeelRules.DefaultMaxShakeAmplitude;

        [Tooltip("峰值滚转（度）；默认 1.2°。")]
        [SerializeField] float maxShakeRollDegrees = CameraFeelRules.DefaultMaxShakeRollDegrees;

        [Tooltip("单次震屏时长（秒）；默认 0.30s ≈ 7.5 帧。")]
        [SerializeField] float shakeDurationSeconds = CameraFeelRules.DefaultShakeDurationSeconds;

        [Tooltip("震屏振荡频率（Hz）；默认 18。")]
        [SerializeField] float shakeFrequencyHz = CameraFeelRules.DefaultShakeFrequencyHz;

        [Header("顿帧（hitstop；提案/待定，原版无此机制）")]
        [Tooltip("总开关。安全上限硬编码在 CameraFeelRules（≤2 帧 @25fps、timeScale ≥ 0.05）。")]
        [SerializeField] bool enableHitStop = true;

        [Tooltip("顿帧时长（秒）；会被夹到 CameraFeelRules.MaxHitStopSeconds。")]
        [SerializeField] float hitStopDurationSeconds = CameraFeelRules.DefaultHitStopSeconds;

        [Tooltip("顿帧期间的 timeScale；会被夹到 [MinHitStopTimeScale, 1]。")]
        [SerializeField] float hitStopTimeScale = CameraFeelRules.DefaultHitStopTimeScale;

        [Header("聚焦表现（提案/待定）")]
        [Tooltip("选中/回合聚焦时的轻微推近峰值（度，当量映射到 OrthoSize）。")]
        [SerializeField] float selectionPushInDegrees = CameraFeelRules.SelectionPushInDegrees;

        [Tooltip("推近单程时长（秒）。")]
        [SerializeField] float selectionPushInDurationSeconds = CameraFeelRules.SelectionPushInDurationSeconds;

        [Tooltip("AI 回合进入旁观态（聚焦更慢 + 视野略外扩）。")]
        [SerializeField] bool aiSpectatorEnabled = true;

        [Header("跟随弹体（提案/待定）")]
        [Tooltip("跟随的最长时长（秒），兜底防止弹体卡住时相机不回来。")]
        [SerializeField] float followTimeoutSeconds = 2.5f;

        [Tooltip("命中/引爆后在落点停留的时长（秒）。")]
        [SerializeField] float detonationHoldSeconds = 0.35f;

        [Tooltip("从落点缓动回焦点进入 ReturnToFocus 态的时长（秒）。")]
        [SerializeField] float followReturnSeconds = 0.35f;

        [Header("落水/死亡（提案/待定）")]
        [Tooltip("落水时相机焦点下压位移（世界单位）。")]
        [SerializeField] float drownDipWorldUnits = CameraFeelRules.DrownDipWorldUnits;

        [Tooltip("手动相机平滑速率（1/s）。")]
        [SerializeField] float manualSmoothingPerSecond = 10f;

        // ---- 聚焦目标 / 焦点（原控制器的等价物，语义逐位保留）----
        Transform _focusTarget;
        Vector3 _goalPosition;

        // ---- 干净位置（不含震屏/下压；FocusPoint 与距离判定都用它）----
        Vector3 _cleanPosition;
        bool _cleanInitialized;

        // ---- 震屏 ----
        float _shakeElapsed;
        float _shakeDuration;
        float _shakeAmplitude;
        float _shakeRoll;
        float _shakeFrequency;
        float _shakePhase;

        // ---- 顿帧 ----
        bool _hitStopActive;
        float _hitStopRemaining;
        float _hitStopSavedTimeScale = 1f;
        bool _sceneUnloading;

        // ---- 推近 / 旁观 ----
        bool _baseOrthoSizeCaptured;
        float _pushInElapsed;
        bool _pushInActive;
        bool _spectator;

        // ---- 跟随状态机 ----
        CameraFollowState _followState = CameraFollowState.None;
        Transform _followTarget;
        PirateBase _followPirate;
        WeaponProjectile _followProjectile;
        Vector3 _followLastPosition;
        Vector3 _returnGoal;
        float _followStateElapsed;

        // ---- 落水/死亡定焦 ----
        float _deathHoldUntilUnscaled;
        Vector3 _deathHoldPosition;
        float _dipElapsed;
        float _dipDuration;
        float _dipAmount;
        bool _dipActive;

        // ---- 手动环绕（方位角；俯角锁 30° 无输入路径）----
        float _manualYaw;
        float _targetYaw;
        float _manualOrthoSize = CameraFraming.FullFieldOrthoSize;
        float _targetOrthoSize = CameraFraming.FullFieldOrthoSize;

        // ---- 观察模式 / 自由锚（标注的调试/辅助出口，见 CameraInputReader 类头）----
        public bool ObserveMode { get; private set; }
        float _observePitchDegrees = CameraFraming.BasePitchDegrees;
        Vector3 _freeAnchorPosition;
        bool _freeAnchorActive;
        Vector3 _cameraTargetDirtyPosition;   // 原链 cameraTarget.position 的等价物（干净焦点+下压+震屏）

        // ---- M4：Scope / 力度-镜头耦合 / 全景档 ----
        float _panoramaOrthoSize = CameraFraming.PanoramaOrthoSizeForSpan(CameraFraming.DefaultWorldSpan);
        float _scopeBlend;              // Scope 等效 FOV 混合系数 0..1（按 ScopeBlendSeconds 线性推进）
        bool _chargeZoomActive;         // 炮台蓄力拉远生效中（结束时恢复蓄力前档位）
        float _preChargeOrthoSize;
        float _baseOrthoSize = CameraFraming.FullFieldOrthoSize;

        /// <summary>本帧实际落到主相机上的取景（守卫测试用它逐帧比对相机实况）。</summary>
        public CameraFrame LastFrame { get; private set; }

        /// <summary>跟随状态机的时间参数。</summary>
        CameraFeelTimings Timings => new CameraFeelTimings(
            followTimeoutSeconds, detonationHoldSeconds, followReturnSeconds);

        /// <summary>
        /// 当前相机聚焦参考点（供"离相机中心最近"判定）。
        /// <b>返回去震屏的干净位置</b>：震屏是纯表现，不应影响"选哪个角色当镜头目标"的判定。
        /// </summary>
        public Vector3 FocusPoint
        {
            get
            {
                if (_cleanInitialized)
                    return _cleanPosition;
                return mainCamera != null ? mainCamera.transform.position : transform.position;
            }
        }

        /// <summary>当前跟随状态（调试/测试用）。</summary>
        public CameraFollowState FollowState => _followState;

        /// <summary>当前是否处于 AI 旁观态（调试/测试用）。</summary>
        public bool SpectatorMode => _spectator;

        /// <summary>
        /// <see cref="aimThrow"/> 是否来自**装配期注入**（而非 Awake 的一次性兜底）。
        /// 供 PlayMode 装配完整性测试区分"装配接线"与"兜底也能跑"——兜底成功不算过关。
        /// </summary>
        public bool AimThrowWiredByAssembly { get; private set; }

        /// <summary>运行时 OrthoSize 档（四舍五入到整数；基准档 <see cref="CameraFraming.CloseUpOrthoSize"/>）。</summary>
        public int RuntimeOrthoSize => Mathf.RoundToInt(_targetOrthoSize);

        /// <summary>
        /// 运行时取景的等效"可见高度"（米）= 2 × OrthoSize（正交半高 ×2）。
        /// 它是**出图取景表的同一个单位**（HUD 提示条的临时调参读数）。
        /// </summary>
        public float RuntimeVisibleMeters => _targetOrthoSize * 2f;

        /// <summary>当前全景档 OrthoSize（由 <see cref="SetWorldSpan"/> 决定；默认跨度 100u → 30）。</summary>
        public int PanoramaOrthoSize => Mathf.RoundToInt(_panoramaOrthoSize);

        /// <summary>
        /// 按地图可玩跨度设置全景档：OrthoSize = clamp(round(span × 0.3), 全场档, 60)。
        /// 由 Battle 场景接线方在世界地图建成后调用；不调用时默认 span=100（全景 30）。
        /// </summary>
        public void SetWorldSpan(float spanUnits)
        {
            _panoramaOrthoSize = CameraFraming.PanoramaOrthoSizeForSpan(spanUnits);
        }

        void Awake()
        {
            // 依赖解析：一次性（装配期注入优先，兜底只跑一次）。
            ResolveAimThrowOnce();
            EnsureInputReader();
            CaptureBaseOrthoSize();
            ApplyLensClips();
            // 开局即进入基准机位（创始人裁决：取景恒为这一档；覆盖场景里烘焙的全场档出厂机位）。
            EnterCloseUpView();
            if (mainCamera != null)
            {
                // 从烘焙机位反推干净焦点（烘焙位置 = 场地中心 + 同一条机位偏移公式）。
                _cleanPosition = mainCamera.transform.position
                    - CameraFraming.ComputeFocusOffset(0f, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance);
                _cameraTargetDirtyPosition = _cleanPosition;
            }
        }

        /// <summary>
        /// 解析 <see cref="aimThrow"/>：**只在 Awake 跑一次**（不是每帧轮询）。
        /// 兜底把"静默失效"降级成"可用但吵闹"，真正的装配缺陷交给
        /// <see cref="AimThrowWiredByAssembly"/>（PlayMode 测试断言它）去失败。
        /// </summary>
        void ResolveAimThrowOnce()
        {
            AimThrowWiredByAssembly = aimThrow != null;
            if (aimThrow != null)
                return;

            aimThrow = FindObjectOfType<AimThrowController>();
            Log.Warn("[BattleCameraDriver] aimThrow 未经装配接线，已一次性兜底解析"
                     + (aimThrow != null ? "成功" : "失败（Scope 视野混合与力度-镜头耦合将不生效）")
                     + "。修复：跑 PirateCrew.EditorTools.BattleLookupWiring.Wire（写 Battle.unity），"
                     + "把它写进 BattleCameraDriver.aimThrow 序列化字段。");
        }

        /// <summary>输入读取器缺省时同物体补建（装配链会显式接线，此为旧场景兜底）。</summary>
        void EnsureInputReader()
        {
            if (input != null)
                return;
            input = GetComponent<CameraInputReader>();
            if (input == null)
                input = gameObject.AddComponent<CameraInputReader>();
        }

        /// <summary>捕获烘焙 OrthoSize 基准（当量链分母已常量化，此值仅作"烘焙值语义"的记录与推近开关）。</summary>
        void CaptureBaseOrthoSize()
        {
            if (mainCamera != null && mainCamera.orthographicSize > 0f)
                _baseOrthoSize = mainCamera.orthographicSize;
            if (_baseOrthoSize <= 0f)
                _baseOrthoSize = CameraFraming.FullFieldOrthoSize;
            _baseOrthoSizeCaptured = true;
        }

        /// <summary>
        /// 正交近/远裁剪写一次（原链路由 Brain 每帧从虚机 Lens 推送 0.1/200——
        /// 同值常量化在 <see cref="CameraFraming"/>；主相机烘焙的 400 运行期原本一直被覆盖成 200）。
        /// </summary>
        void ApplyLensClips()
        {
            if (mainCamera == null)
                return;
            mainCamera.nearClipPlane = CameraFraming.OrthoNearClip;
            mainCamera.farClipPlane = CameraFraming.OrthoFarClip;
        }

        void OnEnable()
        {
            _sceneUnloading = false;
            EventBus.Subscribe<TurnStartedPayload>(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe<int>(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe<Transform>(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Subscribe<ActionSelectedPayload>(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Subscribe<ProjectileDetonatedPayload>(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Subscribe<CrewDamagedPayload>(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe<CrewDiedPayload>(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe<AiThinkingPayload>(BattleEvents.AiThinking, OnAiThinking);
            EventBus.Subscribe<MatchFinishedPayload>(BattleEvents.MatchFinished, OnMatchFinished);
        }

        void OnDisable()
        {
            // 【退订，不是再订阅】订阅/退订必须成对（历史上曾把 Subscribe 原样抄进 OnDisable，
            // 每次禁用都让订阅翻倍——r13 修复档案 11c1919）。
            EventBus.Unsubscribe<TurnStartedPayload>(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe<int>(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Unsubscribe<Transform>(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Unsubscribe<ActionSelectedPayload>(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Unsubscribe<ProjectileDetonatedPayload>(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Unsubscribe<CrewDamagedPayload>(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Unsubscribe<CrewDiedPayload>(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Unsubscribe<AiThinkingPayload>(BattleEvents.AiThinking, OnAiThinking);
            EventBus.Unsubscribe<MatchFinishedPayload>(BattleEvents.MatchFinished, OnMatchFinished);

            // 兜底：任何情况下都不能把 timeScale 留在压低状态（否则整个工程"卡死"）。
            _sceneUnloading = true;
            RestoreTimeScale();
        }

        void Update()
        {
            // 暂停中：冻结手动相机输入，画面平滑交由 LateUpdate 原样收敛。
            if (BattlePause.IsPaused)
                return;

            // 拉模型采样输入（消费顺序确定，无双 Update 帧序抖动）。
            if (input != null)
            {
                CameraInputFrame frame = input.Sample(ObserveMode,
                    mainCamera != null ? mainCamera.transform : transform);

                _targetYaw += frame.YawDeltaDegrees;

                if (frame.FreeAnchorToggleRequested)
                    ToggleFreeAnchor();

                if (ObserveMode)
                {
                    _observePitchDegrees = Mathf.Clamp(
                        _observePitchDegrees + frame.ObservePitchDeltaDegrees, 12f, 78f);
                    if (_freeAnchorActive && frame.ObserveFlyDelta != Vector3.zero)
                        _freeAnchorPosition += frame.ObserveFlyDelta;
                }
            }

            // M4 §3.2 力度-镜头耦合：炮台蓄力越大相机越拉远（特写→全景线性映射），松手恢复。
            UpdateChargeZoom();
        }

        /// <summary>
        /// 力度-镜头耦合（M4 §3.2 的正交转写，提案）：炮台瞄准期间把缩放档覆写为
        /// "特写档 → 全景档 × 蓄力比例"的线性映射；瞄准结束恢复蓄力前的档位。
        /// </summary>
        void UpdateChargeZoom()
        {
            bool charging = aimThrow != null && aimThrow.IsTurretAiming;
            if (charging)
            {
                if (!_chargeZoomActive)
                {
                    _chargeZoomActive = true;
                    _preChargeOrthoSize = _targetOrthoSize;
                }
                _targetOrthoSize = CameraFraming.ChargeZoomOrthoSize(
                    CameraFraming.CloseUpOrthoSize, _panoramaOrthoSize, aimThrow.ChargeRatio);
            }
            else if (_chargeZoomActive)
            {
                _chargeZoomActive = false;
                _targetOrthoSize = _preChargeOrthoSize;
            }
        }

        void LateUpdate()
        {
            float unscaledDt = Time.unscaledDeltaTime;

            AdvanceFollowState(unscaledDt);
            AdvancePushIn(unscaledDt);
            AdvanceHitStop(unscaledDt);
            AdvanceDip(unscaledDt);
            AdvanceScopeBlend(unscaledDt);

            Vector3 goal = ResolveGoalPosition();

            // 聚焦平滑速率：旁观更慢（看戏）；跟随弹体时也更慢更轻（M4 §3.2"轻跟+迟滞"）。
            float focusScale = _spectator ? CameraFeelRules.SpectatorFocusScale : 1f;
            if (_followState == CameraFollowState.FollowProjectile)
                focusScale = Mathf.Min(focusScale, CameraFeelRules.ProjectileFollowFocusScale);
            float lerp = focusLerpPerSecond * focusScale;
            float t = CameraFeelRules.ApproachAlpha(lerp, Time.deltaTime);
            if (!_cleanInitialized)
            {
                _cleanPosition = goal;
                _cleanInitialized = true;
            }
            else
            {
                _cleanPosition = Vector3.Lerp(_cleanPosition, goal, t);
            }

            // 手动档平滑（yaw / OrthoSize 各自的指数曲线）。
            SmoothManualCamera(Time.deltaTime);

            // ---- 震屏采样：玩家按住左键（拖拽瞄准）期间一律为 0，保证不影响瞄准精度判定 ----
            Vector2 shake2D = Vector2.zero;
            float roll = 0f;
            if (enableShake && !IsAimingInputHeld()
                && _shakeElapsed < _shakeDuration && _shakeDuration > 0f)
            {
                shake2D = CameraFeelRules.ShakeOffset2D(
                    _shakeElapsed, _shakeDuration, _shakeAmplitude, _shakeFrequency, _shakePhase);
                roll = CameraFeelRules.ShakeRoll(
                    _shakeElapsed, _shakeDuration, _shakeRoll, _shakeFrequency, _shakePhase);
            }
            _shakeElapsed += unscaledDt;

            float dipOffset = _dipActive
                ? CameraFeelRules.DrownDipOffset(_dipElapsed, _dipDuration, _dipAmount)
                : 0f;

            // ---- 组装本帧取景 ----
            // 朝向先定（烘焙机位 + 滚转）——震屏的相机平面基向量取自它（与原 fallbackCamera 基相同）。
            Quaternion rotation = CameraFraming.ComputeRotation(roll);

            // 焦点：自由锚 > 跟随弹体（原链 vcam.Follow 直切弹体、无阻尼 → 精确位置）> 平滑焦点。
            Vector3 appliedFocus = ResolveAppliedFocus();

            // 原链 cameraTarget.position 的等价物（锚定/观察进入时取它，保持同一瞬时值语义）。
            _cameraTargetDirtyPosition = appliedFocus + Vector3.up * dipOffset
                + CameraFraming.PlaneOffsetToWorld(shake2D, rotation);

            Vector3 position = CameraFraming.ComputePosition(
                appliedFocus, _manualYaw, OffsetPitchForFraming, CameraFraming.BaseDistance)
                + Vector3.up * dipOffset
                + CameraFraming.PlaneOffsetToWorld(shake2D, rotation);

            float orthoSize = CameraFraming.ComposeOrthoSize(
                _manualOrthoSize, _scopeBlend, aiSpectatorEnabled, _spectator,
                _pushInActive, _pushInElapsed, selectionPushInDurationSeconds, selectionPushInDegrees);

            var frame = new CameraFrame
            {
                Position = position,
                Rotation = rotation,
                OrthoSize = orthoSize,
            };
            LastFrame = frame;
            ApplyFrame(frame);
        }

        /// <summary>把一帧取景写到主相机。**全工程只有这里写主相机的 transform / orthographicSize**（near/far 在 Awake 写一次）。</summary>
        void ApplyFrame(in CameraFrame frame)
        {
            if (mainCamera == null)
                return;
            mainCamera.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
            if (!Mathf.Approximately(mainCamera.orthographicSize, frame.OrthoSize))
                mainCamera.orthographicSize = frame.OrthoSize;
        }

        /// <summary>
        /// 机位偏移用的俯角：正常恒锁 <see cref="CameraFraming.BasePitchDegrees"/> 30°；
        /// 观察模式（调试出口）允许自由俯仰——只改**偏移方向**，不改朝向（实机等价，见 CameraFraming 类头）。
        /// </summary>
        float OffsetPitchForFraming => ObserveMode ? _observePitchDegrees : CameraFraming.BasePitchDegrees;

        /// <summary>
        /// 本帧的取景焦点：自由锚激活 → 锚点（相机冻结）；跟随弹体 → 弹体精确位置
        /// （原链 vcam.Follow 直切弹体、Transposer 阻尼 0 → 无平滑）；其余 → 平滑焦点。
        /// </summary>
        Vector3 ResolveAppliedFocus()
        {
            if (_freeAnchorActive)
                return _freeAnchorPosition;
            if (_followState == CameraFollowState.FollowProjectile && _followTarget != null)
                return _followTarget.position;
            return _cleanPosition;
        }

        // ------------------------------------------------------------------
        // 对外 API（保持既有语义）
        // ------------------------------------------------------------------

        /// <summary>
        /// 聚焦到某 Transform（回合开始 / 选中角色时调用）。**同时进入基准机位**（用户裁决：
        /// "开局/每次换行动单位时镜头进入跟随特写"），lookAt 抬到单位胸/头高度。
        /// </summary>
        public void FocusOn(Transform target)
        {
            if (target == null)
                return;

            _focusTarget = target;
            _goalPosition = CameraFraming.FocusTargetPoint(target.position);
            CancelFollow();
            EnterCloseUpView();
            StartPushIn();
        }

        /// <summary>聚焦到某世界坐标（一次性）。同样回到基准机位（换机位即回到"操作当前角色"的距离感）。</summary>
        public void FocusOn(Vector3 worldPosition)
        {
            _focusTarget = null;
            _goalPosition = worldPosition;
            CancelFollow();
            EnterCloseUpView();
            StartPushIn();
        }

        /// <summary>解除跟随（相机停在当前位置）。</summary>
        public void Release()
        {
            _focusTarget = null;
            CancelFollow();
        }

        // ------------------------------------------------------------------
        // 机位档位
        // ------------------------------------------------------------------

        /// <summary>
        /// 进入基准机位：OrthoSize 立刻切到 <see cref="CameraFraming.CloseUpOrthoSize"/>，yaw 归零
        /// （回到烘焙机位朝向：方位 45° 对称构图），并立即生效——"开局 / 换行动单位即基准"，不平滑过渡。
        /// </summary>
        void EnterCloseUpView()
        {
            _manualYaw = 0f;
            _targetYaw = 0f;
            _manualOrthoSize = CameraFraming.CloseUpOrthoSize;
            _targetOrthoSize = CameraFraming.CloseUpOrthoSize;
            // 换行动单位即脱离炮台瞄准：力度-镜头耦合立即失效（否则恢复逻辑会盖掉这次聚焦）。
            _chargeZoomActive = false;
        }

        // ------------------------------------------------------------------
        // 目标解析
        // ------------------------------------------------------------------

        Vector3 ResolveGoalPosition()
        {
            switch (_followState)
            {
                case CameraFollowState.FollowProjectile:
                    return _followTarget != null ? _followTarget.position : _followLastPosition;

                case CameraFollowState.DetonationHold:
                    return _goalPosition;

                case CameraFollowState.ReturnToFocus:
                    return _focusTarget != null ? CameraFraming.FocusTargetPoint(_focusTarget.position) : _returnGoal;
            }

            // None：落水定焦窗口内先看落水点，否则看当前行动角色（含 lookAt 抬高）。
            if (Time.unscaledTime < _deathHoldUntilUnscaled)
                return _deathHoldPosition;
            return _focusTarget != null ? CameraFraming.FocusTargetPoint(_focusTarget.position) : _goalPosition;
        }

        // ------------------------------------------------------------------
        // 震屏
        // ------------------------------------------------------------------

        /// <summary>玩家是否按住左键（正在拖拽瞄准）。此时抑制震屏，保证瞄准用的相机基向量稳定。</summary>
        static bool IsAimingInputHeld()
        {
            return Input.GetMouseButton(0);
        }

        float CurrentShakeAmplitude()
        {
            if (_shakeDuration <= 0f || _shakeElapsed <= 0f || _shakeElapsed >= _shakeDuration)
                return 0f;
            return _shakeAmplitude * CameraFeelRules.ShakeEnvelope(_shakeElapsed / _shakeDuration);
        }

        void ApplyShake(in ShakeProfile profile)
        {
            if (!enableShake || !profile.IsActive)
                return;

            // 不叠加：只有更强者才替换（避免 AoE 同时命中多单位时连抖）。
            if (_shakeElapsed > 0f && _shakeElapsed < _shakeDuration
                && !CameraFeelRules.ShouldReplaceShake(CurrentShakeAmplitude(), profile.Amplitude))
                return;

            _shakeElapsed = 0f;
            _shakeDuration = profile.DurationSeconds;
            _shakeAmplitude = profile.Amplitude;
            _shakeRoll = profile.RollDegrees;
            _shakeFrequency = profile.FrequencyHz;
            _shakePhase = Random.Range(0f, Mathf.PI * 2f);
        }

        // ------------------------------------------------------------------
        // 顿帧（timeScale 瞬时压低再恢复）
        // ------------------------------------------------------------------

        void StartHitStop()
        {
            if (!enableHitStop || _sceneUnloading)
                return;

            // 暂停中不启动顿帧（timeScale 已被 BattlePause 归 0，两者都改 timeScale 会互相踩）。
            if (BattlePause.IsPaused)
                return;

            bool matchOver = battle != null && battle.IsMatchOver;
            if (!CameraFeelRules.ShouldApplyHitStop(matchOver, sceneTransitioning: false, Time.timeScale))
                return;

            float duration = CameraFeelRules.ClampHitStopDuration(hitStopDurationSeconds);
            float scale = CameraFeelRules.ClampHitStopTimeScale(hitStopTimeScale);
            if (duration <= 0f || scale >= 1f || !CameraFeelRules.IsHitStopSafe(duration, scale))
                return;

            if (!_hitStopActive)
                _hitStopSavedTimeScale = Time.timeScale;

            _hitStopActive = true;
            _hitStopRemaining = duration;
            Time.timeScale = scale;
        }

        void AdvanceHitStop(float unscaledDt)
        {
            if (!_hitStopActive)
                return;

            _hitStopRemaining -= unscaledDt;
            if (_hitStopRemaining <= 0f)
                RestoreTimeScale();
        }

        void RestoreTimeScale()
        {
            if (!_hitStopActive)
                return;

            _hitStopActive = false;
            // 顿帧期间玩家可能按了暂停（timeScale 被压到 0）：此时不能把 saved 值(1)盖回去，
            // 否则"暂停"被静默解除——保持 0，等 BattlePause.Resume 统一恢复。
            Time.timeScale = BattlePause.IsPaused ? 0f : _hitStopSavedTimeScale;
            _hitStopSavedTimeScale = 1f;
        }

        // ------------------------------------------------------------------
        // 跟随状态机
        // ------------------------------------------------------------------

        void BeginFollowProjectile(WeaponProjectile projectile)
        {
            if (projectile == null || projectile.transform == null)
                return;

            _followProjectile = projectile;
            _followPirate = null;
            BeginFollow(projectile.transform);
        }

        void BeginFollowPirate(PirateBase pirate)
        {
            if (pirate == null)
                return;

            _followPirate = pirate;
            _followProjectile = null;
            BeginFollow(pirate.transform);
        }

        void BeginFollow(Transform target)
        {
            // 任何一次跟随开始都退出自由锚（原链 BeginFollow → ExitFreeAnchor 的语义）。
            ExitFreeAnchor();
            _followTarget = target;
            _followState = CameraFollowState.FollowProjectile;
            _followStateElapsed = 0f;
            _followLastPosition = target.position;
            _returnGoal = _focusTarget != null
                ? CameraFraming.FocusTargetPoint(_focusTarget.position)
                : _cleanPosition;
        }

        /// <summary>结束跟随并停在 <paramref name="position"/>（平滑焦点同步搬过去，切回不跳帧）。</summary>
        void EndFollowAt(Vector3 position)
        {
            ExitFreeAnchor();
            _followLastPosition = position;
            _cleanPosition = position;
            _goalPosition = position;
            _cleanInitialized = true;

            _followPirate = null;
            _followProjectile = null;
            _followTarget = null;
        }

        void CancelFollow()
        {
            if (_followState == CameraFollowState.None && _followTarget == null)
                return;

            if (_followState == CameraFollowState.FollowProjectile)
                ExitFreeAnchor();

            _followState = CameraFollowState.None;
            _followTarget = null;
            _followPirate = null;
            _followProjectile = null;
            _followStateElapsed = 0f;
        }

        bool IsFollowTargetActive()
        {
            // 抛自己：AddForce 的冲量要到下一个物理步才体现速度，开局给一个短暂宽限。
            if (_followPirate != null)
                return _followPirate.IsMoving() || _followStateElapsed < 0.2f;

            if (_followProjectile != null)
                return _followProjectile.IsInFlight;

            return false;
        }

        void AdvanceFollowState(float unscaledDt)
        {
            if (_followState == CameraFollowState.None)
                return;

            // 目标被销毁（非引爆路径，如出界）→ 视为 TargetLost。
            CameraFollowTrigger trigger = CameraFollowTrigger.None;
            if (_followState == CameraFollowState.FollowProjectile && _followTarget == null)
                trigger = CameraFollowTrigger.TargetLost;

            CameraFollowState next = CameraFeelRules.Advance(
                _followState, trigger, IsFollowTargetActive(), _followStateElapsed + unscaledDt, Timings);

            if (next == _followState)
            {
                if (_followState == CameraFollowState.FollowProjectile && _followTarget != null)
                    _followLastPosition = _followTarget.position;
                _followStateElapsed += unscaledDt;
                return;
            }

            // 进入 DetonationHold 由 OnProjectileDetonated 直接设定；此处只处理其余迁移。
            if (next == CameraFollowState.None || next == CameraFollowState.ReturnToFocus)
            {
                ExitFreeAnchor();
                _followTarget = null;
                _followPirate = null;
                _followProjectile = null;
                if (next == CameraFollowState.ReturnToFocus)
                {
                    _returnGoal = _focusTarget != null
                        ? CameraFraming.FocusTargetPoint(_focusTarget.position)
                        : _followLastPosition;
                    // 从落点平滑回焦，而不是瞬移。
                    _goalPosition = _returnGoal;
                }
            }

            _followState = next;
            _followStateElapsed = 0f;
        }

        // ------------------------------------------------------------------
        // 观察模式 / 自由锚（标注的调试/辅助出口）
        // ------------------------------------------------------------------

        /// <summary>
        /// 进入/退出观察模式（HUD 模式 3 调用）：进入 = 焦点冻结在当前锚点 + 鼠标转视角 + WASD 飞行；
        /// 退出 = 恢复跟随。
        /// </summary>
        public void SetObserveMode(bool on)
        {
            if (ObserveMode == on)
                return;

            ObserveMode = on;
            if (on)
            {
                _observePitchDegrees = Mathf.Clamp(CameraFraming.BasePitchDegrees, 12f, 78f);
                if (!_freeAnchorActive)
                    _freeAnchorPosition = CurrentFollowOrDirtyPosition();
                _freeAnchorActive = true;
            }
            else
            {
                ExitFreeAnchor();
            }
        }

        /// <summary>中键：把焦点冻结在当前位置（相机不再跟人，环绕即自由视角）；再按恢复。
        /// 只动锚不动观察模式——两个调试出口互相独立（与原实现一致）。</summary>
        void ToggleFreeAnchor()
        {
            if (_freeAnchorActive)
            {
                _freeAnchorActive = false;
            }
            else
            {
                _freeAnchorPosition = CurrentFollowOrDirtyPosition();
                _freeAnchorActive = true;
            }
        }

        /// <summary>
        /// 自由锚的取点：原链取 vcam.Follow 的位置（跟随中 = 弹体；否则 = cameraTarget 的**瞬时脏值**，
        /// 含当帧震屏/下压——同一语义）。
        /// </summary>
        Vector3 CurrentFollowOrDirtyPosition()
        {
            if (_followState == CameraFollowState.FollowProjectile && _followTarget != null)
                return _followTarget.position;
            return _cameraTargetDirtyPosition;
        }

        /// <summary>任何一次聚焦/跟随赋前调用：自由视角是临时态，聚焦即回归跟随（原 ExitFreeAnchor 语义）。</summary>
        void ExitFreeAnchor()
        {
            ObserveMode = false;
            _freeAnchorActive = false;
        }

        // ------------------------------------------------------------------
        // Scope 混合 / 推近 / 下压
        // ------------------------------------------------------------------

        /// <summary>Scope FOV 混合推进（暂停时也收敛）：目标态取自 <see cref="AimThrowController.IsScopeActive"/>。</summary>
        void AdvanceScopeBlend(float unscaledDt)
        {
            // aimThrow 由 Awake 一次性解析（装配注入优先），此处只读——不再每帧 FindObjectOfType。
            bool desired = aimThrow != null && aimThrow.IsScopeActive;
            float step = unscaledDt / Mathf.Max(1e-4f, CameraFeelRules.ScopeBlendSeconds);
            _scopeBlend = Mathf.MoveTowards(_scopeBlend, desired ? 1f : 0f, step);
        }

        void StartPushIn()
        {
            if (!_baseOrthoSizeCaptured || selectionPushInDegrees <= 0f
                || selectionPushInDurationSeconds <= 0f)
                return;

            _pushInElapsed = 0f;
            _pushInActive = true;
        }

        void AdvancePushIn(float unscaledDt)
        {
            if (!_pushInActive)
                return;

            _pushInElapsed += unscaledDt;
            if (_pushInElapsed >= selectionPushInDurationSeconds)
            {
                _pushInElapsed = selectionPushInDurationSeconds;
                _pushInActive = false;
            }
        }

        void StartDrownDip(Vector3 position)
        {
            _deathHoldUntilUnscaled = Time.unscaledTime + CameraFeelRules.DrownFocusHoldSeconds;
            _deathHoldPosition = position;
            _dipActive = true;
            _dipElapsed = 0f;
            _dipDuration = CameraFeelRules.DrownDipDurationSeconds;
            _dipAmount = drownDipWorldUnits;
        }

        void AdvanceDip(float unscaledDt)
        {
            if (!_dipActive)
                return;

            _dipElapsed += unscaledDt;
            if (_dipElapsed >= _dipDuration)
            {
                _dipElapsed = _dipDuration;
                _dipActive = false;
            }
        }

        /// <summary>手动档平滑：yaw 与 OrthoSize 各自的指数曲线（落盘统一在 LateUpdate 的帧组装里）。</summary>
        void SmoothManualCamera(float deltaTime)
        {
            float t = CameraFeelRules.ApproachAlpha(manualSmoothingPerSecond, deltaTime);

            if (!Mathf.Approximately(_manualYaw, _targetYaw))
                _manualYaw = Mathf.Lerp(_manualYaw, _targetYaw, t);

            if (!Mathf.Approximately(_manualOrthoSize, _targetOrthoSize))
                _manualOrthoSize = Mathf.Lerp(_manualOrthoSize, _targetOrthoSize, t);
        }

        // ------------------------------------------------------------------
        // EventBus 回调
        // ------------------------------------------------------------------

        void OnTurnStarted(TurnStartedPayload turn)
        {
            // §3.2 panToCharacter：TurnStartedPayload 已带默认镜头目标。
            _spectator = false;

            if (turn.PanTarget != null)
                FocusOn(turn.PanTarget);
        }

        void OnTurnEnded(int teamNumber)
        {
            // 载荷是队伍编号，本控制器不用——只借"回合结束"这个时机退出旁观态。
            _spectator = false;
        }

        void OnCameraFocusRequested(Transform target)
        {
            // FocusRequested 在规则层优先级最高：任何时候都取消跟随（回合推进绝不被跟随拖住）。
            if (target != null)
                FocusOn(target);
        }

        void OnAiThinking(AiThinkingPayload thinking)
        {
            // §8.1：AI 决策中相机停止自动滚动；配合更慢的聚焦 + 略外扩形成"旁观"感。
            if (aiSpectatorEnabled)
                _spectator = true;
        }

        void OnActionSelected(ActionSelectedPayload action)
        {
            if (battle == null)
                return;

            switch (action.Kind)
            {
                case BattleActionKind.UseWeapon:
                {
                    WeaponProjectile projectile = FindNewestInFlightProjectile();
                    if (projectile != null)
                        BeginFollowProjectile(projectile);
                    break;
                }

                case BattleActionKind.ThrowSelf:
                {
                    PirateBase pirate = FindPirate(action.PirateId);
                    if (pirate != null)
                        BeginFollowPirate(pirate);
                    break;
                }
            }
        }

        void OnProjectileDetonated(ProjectileDetonatedPayload detonated)
        {
            bool wasFollowing = _followState == CameraFollowState.FollowProjectile;
            if (wasFollowing)
            {
                // 事件在 Destroy(gameObject) 之前同步发布，此处切回是安全的。
                EndFollowAt(detonated.Position);
                _followState = CameraFollowState.DetonationHold;
                _followStateElapsed = 0f;
                _goalPosition = detonated.Position;
            }

            StartExplosionShake(detonated);

            // 顿帧只在"这一炸在观感范围内"时施加：远端地雷自爆不该让整局卡一下。
            if (IsImpactVisible(detonated))
                StartHitStop();
        }

        void OnCrewDamaged(CrewDamagedPayload damaged)
        {
            if (!enableShake)
                return;

            if (damaged.MaxHealth <= 0)
                return;

            float ratio = Mathf.Clamp01(damaged.Damage / damaged.MaxHealth);
            float amplitude = maxShakeAmplitude * shakeAmplitudeScale * 0.4f * ratio;
            if (amplitude <= 0.001f)
                return;

            // "命中反馈"：比爆炸弱得多；爆炸随后到达时会以更强振幅替换它（strongest-wins）。
            ApplyShake(new ShakeProfile(
                amplitude, 0f, shakeDurationSeconds * 0.5f, shakeFrequencyHz));
        }

        void OnCrewDied(CrewDiedPayload died)
        {
            if (battle == null)
                return;

            PirateBase pirate = FindPirate(died.PirateId);
            if (pirate == null)
                return;

            if (pirate.Drowned)
            {
                // §4.4 落水即死：焦点短暂停在落水点并轻微下压（下沉/淡出由视觉层负责，这里只做镜头）。
                StartDrownDip(pirate.transform.position);
            }
            else if (enableShake)
            {
                ApplyShake(new ShakeProfile(
                    CameraFeelRules.DeathShakeAmplitude * shakeAmplitudeScale,
                    0f, shakeDurationSeconds * 0.6f, shakeFrequencyHz));
            }
        }

        void OnMatchFinished(MatchFinishedPayload payload)
        {
            _spectator = false;
            _pushInActive = false;
            CancelFollow();
            _dipActive = false;
            _deathHoldUntilUnscaled = 0f;
            RestoreTimeScale();
        }

        void StartExplosionShake(ProjectileDetonatedPayload detonated)
        {
            if (!enableShake)
                return;

            float radiusWorld = ExplosionRadiusWorld(detonated.Weapon);
            float distance = Vector3.Distance(_cleanPosition, detonated.Position);
            ShakeProfile profile = CameraFeelRules.ExplosionShake(
                distance, radiusWorld,
                maxShakeAmplitude * shakeAmplitudeScale, maxShakeRollDegrees,
                shakeDurationSeconds, shakeFrequencyHz);
            ApplyShake(profile);
        }

        /// <summary>爆炸在观感范围内（震屏衰减不为 0）。用于决定是否施加顿帧。</summary>
        bool IsImpactVisible(ProjectileDetonatedPayload detonated)
        {
            float radiusWorld = ExplosionRadiusWorld(detonated.Weapon);
            float distance = Vector3.Distance(_cleanPosition, detonated.Position);
            return CameraFeelRules.ShakeFalloff(distance, radiusWorld) >= CameraFeelRules.MinShakeFalloff;
        }

        /// <summary>武器爆炸半径（世界单位）；无爆炸数值时给 1 个单位的名义半径（保证近处仍有反馈）。</summary>
        static float ExplosionRadiusWorld(WeaponId weapon)
        {
            if (WeaponCatalog.TryGet(weapon, out WeaponStats stats) && stats.HasExplosion)
                return LevelGeometry.PixelsToUnits(ExplosionResolver.Radius(stats.ExplosionSize));
            return 1f;
        }

        // ------------------------------------------------------------------
        // 查找工具（全部走 battle 的公开只读列表，不用 GameObject.Find）
        // ------------------------------------------------------------------

        WeaponProjectile FindNewestInFlightProjectile()
        {
            if (battle == null)
                return null;

            IReadOnlyList<WeaponProjectile> all = battle.AllProjectiles;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                WeaponProjectile projectile = all[i];
                if (projectile != null && projectile.IsInFlight)
                    return projectile;
            }

            return null;
        }

        PirateBase FindPirate(int pirateId)
        {
            if (battle == null)
                return null;

            IReadOnlyList<PirateBase> all = battle.AllPirates;
            for (int i = 0; i < all.Count; i++)
            {
                PirateBase pirate = all[i];
                if (pirate != null && pirate.PirateId == pirateId)
                    return pirate;
            }

            return null;
        }
    }
}
