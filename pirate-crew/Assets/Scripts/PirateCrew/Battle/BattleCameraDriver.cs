using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗相机 **Driver**（主相机的唯一写入者）。
    ///
    /// 【位姿全目标化（两态重构核心不变量，相机行为契约 #7）】
    ///   相机位姿 = {焦点, 方位, 俯角, 取景档} 四元目标 + 指数平滑的现实值。
    ///   除**对局开局的首次落位**外，全工程不存在直写现实值的路径——任何状态切换
    ///   （选人/换人/取消/回合 pan）都是目标变更，跳变在结构上不可构造。
    ///   · 取景两档（近景基准档 ⇄ 远景全景档）：Tab / 滚轮在自由镜头与浏览态切换，
///     切换 = 目标档变更 + 指数平滑（契约 #9）；
    ///   · 俯角基准 30°，仅自由镜头允许玩家偏离 [15°, 80°]（契约 #8）。
    ///
    /// 【模式表（相机行为契约 #1）】由 <see cref="BattleInteractionController.State"/> 拉模型分派：
    ///   FreeCamera → **FreeFly**（编辑器飞行式：按住右键转视角/平移，横纵同灵敏度）；
    ///   SelectedIdle → **Orbit**（环绕选中单位，右键拖拽改方位，俯角回基准）；
    ///   OperationActive → **OpLock**（位姿冻结，不接受任何相机输入——契约 #5）；
    ///   Executing → **Follow**（既有跟随状态机：弹体/被抛角色/命中停留/回焦）。
    ///
    /// 【三件套分工】<see cref="CameraFraming"/>（纯数学）→ <see cref="BattleInputReader"/>（唯一输入
    /// 采样，经交互控制器转手）→ 本类（唯一写入者）。**除本类外任何系统不得写主相机的
    /// transform / orthographicSize / near / far**（PlayMode 守卫测试逐帧比对相机实况与
    /// <see cref="LastFrame"/>；far 的唯一例外口是 <see cref="SetFarClipForSpan"/>）。
    ///
    /// 【安全底线（勿破坏）】
    ///   · 震屏只加在最终位置上，且玩家按住左键期间不施加；
    ///     <see cref="FocusPoint"/> 返回**去震屏**的干净位置，不影响"离相机中心最近"判定。
    ///   · 顿帧的 timeScale 安全上限在 <see cref="CameraFeelRules"/>；暂停中不施加、退场兜底恢复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleCameraDriver : MonoBehaviour
    {
        [Header("写入目标（唯一写入者的对象）")]
        [Tooltip("主相机。本类是它 transform/lens 的唯一写入者（像素化 rig 只改掩码/渲染器，不碰取景）。")]
        [SerializeField] Camera mainCamera;

        [Header("组装引用（手感数据源；留空时跟随/死亡反馈降级，其余仍工作）")]
        [Tooltip("战斗根。用于把 PirateId 解析成角色、把弹体取出做跟随。")]
        [SerializeField] BattleController battle;

        [Tooltip("交互控制器（热路径依赖：每帧拉取 State/SelectedTarget/LastIntent 决定相机模式）。"
                 + "必须显式注入，绝不做每帧回退扫描。")]
        [SerializeField] BattleInteractionController interaction;

        [Header("参数")]
        [Tooltip("聚焦平滑速度（1/s）。默认 6 → 90% 到位约 0.384s，贴合回合节奏。")]
        [SerializeField] float focusLerpPerSecond = 6f;

        [Tooltip("环绕方位角的平滑速度（1/s）；自由镜头转视角不走平滑（鼠标输入即目标，无滞后）。")]
        [SerializeField] float orbitSmoothingPerSecond = 10f;

        [Tooltip("取景档平滑速度（1/s）：近景⇄远景两档切换的指数逼近速率（【提案/待定】）。")]
        [SerializeField] float orthoSmoothingPerSecond = 8f;

        [Header("自由镜头（编辑器飞行式；数值【提案/待定】）")]
        [Tooltip("自由镜头飞行速度（m/s，未加速；加速倍率 ×3）。")]
        [SerializeField] float freeFlySpeed = 12f;

        [Tooltip("自由镜头飞行加速倍率（按住 Shift）。")]
        [SerializeField] float freeFlyFastScale = 3f;

        [Tooltip("自由镜头俯仰夹取下限（度）。")]
        [SerializeField] float freePitchMinDegrees = 15f;

        [Tooltip("自由镜头俯仰夹取上限（度）。")]
        [SerializeField] float freePitchMaxDegrees = 80f;

        [Header("震屏（提案/待定）")]
        [Tooltip("总开关：关闭后命中/爆炸/死亡都不震屏。")]
        [SerializeField] bool enableShake = true;

        [Tooltip("震屏强度整体缩放（1 = 默认）。")]
        [SerializeField] float shakeAmplitudeScale = 1f;

        [Tooltip("峰值位移（世界单位）；默认 0.7 ≈ 11px，小于 30px 选中半径。")]
        [SerializeField] float maxShakeAmplitude = CameraFeelRules.DefaultMaxShakeAmplitude;

        [Tooltip("峰值滚转（度）；默认 1.2°。")]
        [SerializeField] float maxShakeRollDegrees = CameraFeelRules.DefaultMaxShakeRollDegrees;

        [Tooltip("单次震屏时长（秒）；默认 0.30s。")]
        [SerializeField] float shakeDurationSeconds = CameraFeelRules.DefaultShakeDurationSeconds;

        [Tooltip("震屏振荡频率（Hz）；默认 18。")]
        [SerializeField] float shakeFrequencyHz = CameraFeelRules.DefaultShakeFrequencyHz;

        [Header("顿帧（hitstop；提案/待定）")]
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

        // ---- 位姿（目标 + 现实；"位姿全目标化"不变量的载体）----
        float _yaw;
        float _targetYaw;
        float _pitch;
        float _targetPitch;
        float _targetOrthoSize = CameraFraming.CloseUpOrthoSize;
        float _orthoSize = CameraFraming.CloseUpOrthoSize;
        float _panoramaOrthoSize = CameraFraming.PanoramaOrthoSizeForSpan(CameraFraming.DefaultWorldSpan);

        // ---- 焦点（干净位置，不含震屏/下压；FocusPoint 与距离判定都用它）----
        Vector3 _cleanPosition;
        Vector3 _goalPosition;
        bool _cleanInitialized;

        // ---- 震屏 ----
        float _shakeElapsed;
        float _shakeDuration;
        float _shakeAmplitude;
        float _shakeRoll;
        float _shakeFrequency;
        float _shakePhase;
        bool _shakeJustStarted;

        // ---- 顿帧 ----
        bool _hitStopActive;
        float _hitStopRemaining;
        float _hitStopSavedTimeScale = 1f;
        bool _sceneUnloading;

        // ---- 推近 ----
        bool _pushInActive;
        float _pushInElapsed;

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

        /// <summary>本帧实际落到主相机上的取景（守卫测试用它逐帧比对相机实况）。</summary>
        public CameraFrame LastFrame { get; private set; }

        /// <summary>跟随状态机的时间参数。</summary>
        CameraFeelTimings Timings => new CameraFeelTimings(
            followTimeoutSeconds, detonationHoldSeconds, followReturnSeconds);

        /// <summary>
        /// 当前相机聚焦参考点（供"离相机中心最近"判定）。
        /// <b>返回去震屏的干净位置</b>：震屏是纯表现，不影响"选哪个角色当镜头目标"的判定。
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

        /// <summary>交互状态读数（camdiag 诊断用；未接线为 &lt;null&gt;）。</summary>
        public string InteractionStateForDiagnostics =>
            interaction != null ? interaction.State.ToString() : "<null>";

        /// <summary>
        /// <see cref="interaction"/> 是否来自**装配期注入**（而非 Awake 的一次性兜底）。
        /// 供 PlayMode 装配完整性测试区分"装配接线"与"兜底也能跑"——兜底成功不算过关。
        /// </summary>
        public bool InteractionWiredByAssembly { get; private set; }

        /// <summary>当前取景档目标（近景 / 远景两档之一；四舍五入读数，camdiag 与测试用）。</summary>
        public int RuntimeOrthoSize => Mathf.RoundToInt(_targetOrthoSize);

        /// <summary>运行时取景的等效"可见高度"（米）= 2 × OrthoSize。</summary>
        public float RuntimeVisibleMeters => _targetOrthoSize * 2f;

        /// <summary>当前全景档 OrthoSize（由 <see cref="SetWorldSpan"/> 决定；默认跨度 100u → 30）。</summary>
        public int PanoramaOrthoSize => Mathf.RoundToInt(_panoramaOrthoSize);

        /// <summary>
        /// 按地图可玩跨度记录全景档：OrthoSize = clamp(round(span × 0.3), 全场档, 60)。
        /// 由 Battle 场景接线方在世界地图建成后调用（远裁剪推导的消费输入）。
        /// </summary>
        public void SetWorldSpan(float spanUnits)
        {
            _panoramaOrthoSize = CameraFraming.PanoramaOrthoSizeForSpan(spanUnits);
        }

        /// <summary>
        /// 相机当前朝向的世界方位角（度；0 = +Z，俯视顺时针——与 <see cref="ThrowParams.YawDegrees"/>
        /// 同约定）。交互控制器用它做投掷方向角的"玩家面向起步"。
        /// </summary>
        public float FocusYawDegrees
        {
            get
            {
                Vector3 toCamera = CameraFraming.ComputeFocusOffset(_targetYaw, _targetPitch, 1f);
                Vector3 forward = new Vector3(-toCamera.x, 0f, -toCamera.z);
                return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            }
        }

        /// <summary>
        /// 按场景跨度抬高远裁剪面（唯一写入者契约的**显式例外**：本类之外不得直写
        /// <c>farClipPlane</c>，统一经此入口——内部保底不低于 <see cref="CameraFraming.OrthoFarClip"/>）。
        /// 供 <see cref="BattleController.SetupBattleEnvironment"/> 按本关可达的最松取景档设置远裁剪。
        /// </summary>
        public void SetFarClipForSpan(float farClipPlane)
        {
            if (mainCamera == null)
                return;
            mainCamera.farClipPlane = Mathf.Max(CameraFraming.OrthoFarClip, farClipPlane);
        }

        void Awake()
        {
            ResolveInteractionOnce();
            ApplyLensClips();

            // 【全工程唯一瞬切点】对局开局从烘焙机位反推初始位姿（相机行为契约 #7 的唯一例外）：
            // 烘焙位置 = 场地中心 + 方位 0 / 俯角 30° 的基准偏移。
            if (mainCamera != null)
            {
                Vector3 offset = CameraFraming.ComputeFocusOffset(
                    0f, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance);
                _cleanPosition = mainCamera.transform.position - offset;
                _goalPosition = _cleanPosition;
                _cleanInitialized = true;
                _yaw = _targetYaw = 0f;
                _pitch = _targetPitch = CameraFraming.BasePitchDegrees;
            }
        }

        /// <summary>装配期注入优先；未注入时一次性兜底并吵闹（装配缺陷交给 PlayMode 断言钉住）。</summary>
        void ResolveInteractionOnce()
        {
            InteractionWiredByAssembly = interaction != null;
            if (interaction != null)
                return;

            interaction = FindObjectOfType<BattleInteractionController>();
            Log.Warn("[BattleCameraDriver] interaction 未经装配接线，已一次性兜底解析"
                     + (interaction != null ? "成功" : "失败（相机将停留在开局机位，FreeFly/Orbit 不生效）")
                     + "。修复：跑 PirateCrew.EditorTools.BattleLookupWiring.Wire（写 Battle.unity）。");
        }

        /// <summary>正交近/远裁剪写一次（常量单源 CameraFraming）。</summary>
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
            EventBus.Subscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Subscribe(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Subscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Subscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);
        }

        void OnDisable()
        {
            // 【退订，不是再订阅】订阅/退订必须成对（历史上曾把 Subscribe 原样抄进 OnDisable）。
            EventBus.Unsubscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Unsubscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Unsubscribe(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Unsubscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Unsubscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);

            // 兜底：任何情况下都不能把 timeScale 留在压低状态（否则整个工程"卡死"）。
            _sceneUnloading = true;
            RestoreTimeScale();
        }

        void LateUpdate()
        {
            float unscaledDt = Time.unscaledDeltaTime;

            AdvanceFollowState(unscaledDt);
            AdvancePushIn(unscaledDt);
            AdvanceHitStop(unscaledDt);
            AdvanceDip(unscaledDt);

            // ---- 按交互状态分派相机模式（拉模型：所有 Update 已跑完，无帧序竞态）----
            BattleIntentFrame intent = interaction != null ? interaction.LastIntent : default;
            InteractionState state = interaction != null
                ? interaction.State
                : InteractionState.FreeCamera;

            if (InteractionRules.CameraAcceptsInput(state))
            {
                if (state == InteractionState.FreeCamera)
                    ApplyFreeFlyInput(intent);
                else
                    ApplyOrbitInput(intent);

                ApplyFramingTierInput(intent);
            }
            // OpLock / Executing：不接受任何相机输入（目标位姿与取景档保持）。

            // ---- 解析焦点目标（跟随态覆盖；环绕态锁定选中单位；自由态用 _goalPosition）----
            _goalPosition = ResolveGoalPosition(state);

            // ---- 平滑 ----
            float focusScale = _followState == CameraFollowState.FollowProjectile
                ? CameraFeelRules.ProjectileFollowFocusScale
                : 1f;
            float t = CameraFeelRules.ApproachAlpha(focusLerpPerSecond * focusScale, Time.deltaTime);
            if (!_cleanInitialized)
            {
                _cleanPosition = _goalPosition;
                _cleanInitialized = true;
            }
            else
            {
                _cleanPosition = Vector3.Lerp(_cleanPosition, _goalPosition, t);
            }

            SmoothManualCamera(Time.deltaTime, state);

            // ---- 震屏采样：玩家按住左键期间一律为 0，保证不影响瞄准精度判定 ----
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
            _shakeJustStarted = false;

            float dipOffset = _dipActive
                ? CameraFeelRules.DrownDipOffset(_dipElapsed, _dipDuration, _dipAmount)
                : 0f;

            // ---- 组装本帧取景 ----
            Vector3 appliedFocus = ResolveAppliedFocus();
            Vector3 basePosition = CameraFraming.ComputePosition(
                appliedFocus, _yaw, _pitch, CameraFraming.BaseDistance);

            // 朝向 = 看向焦点（环绕必须重瞄 = 画面绕焦点转动，2026-09-23 裁决沿用）。
            Quaternion rotation = CameraFraming.ComputeRotationLooking(
                (appliedFocus - basePosition).normalized, roll);

            Vector3 position = basePosition + Vector3.up * dipOffset
                + CameraFraming.PlaneOffsetToWorld(shake2D, rotation);

            if (!Mathf.Approximately(_orthoSize, _targetOrthoSize))
                _orthoSize = Mathf.Lerp(_orthoSize, _targetOrthoSize,
                    CameraFeelRules.ApproachAlpha(orthoSmoothingPerSecond, Time.deltaTime));

            float orthoSize = CameraFraming.ComposeOrthoSize(
                _orthoSize,
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

        /// <summary>把一帧取景写到主相机。**全工程只有这里写主相机的 transform / orthographicSize**。</summary>
        void ApplyFrame(in CameraFrame frame)
        {
            if (mainCamera == null)
                return;
            mainCamera.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
            if (!Mathf.Approximately(mainCamera.orthographicSize, frame.OrthoSize))
                mainCamera.orthographicSize = frame.OrthoSize;
        }

        // ------------------------------------------------------------------
        // 模式输入
        // ------------------------------------------------------------------

        /// <summary>
        /// 自由镜头（编辑器飞行式）：按住右键时转视角 + WASD/QE 飞行。
        /// 转视角是**原地转身**：机位不动——数学上保持 <c>机位 = 焦点 + Offset(方位,俯角)</c>
        /// 的恒等（Offset 从焦点指向相机），方位/俯角变化时用 Offset 差补偿焦点；
        /// 方位/俯角直写（鼠标输入即目标，无平滑滞后，编辑器飞行手感）；俯角夹取 [15°, 80°]。
        /// </summary>
        void ApplyFreeFlyInput(in BattleIntentFrame intent)
        {
            if (!intent.LookHeld)
                return;

            bool rotated = !Mathf.Approximately(intent.LookYawDelta, 0f)
                           || !Mathf.Approximately(intent.LookPitchDelta, 0f);
            if (rotated)
            {
                Vector3 oldOffset = CameraFraming.ComputeFocusOffset(
                    _targetYaw, _targetPitch, CameraFraming.BaseDistance);
                _targetYaw += intent.LookYawDelta;
                _targetPitch = Mathf.Clamp(
                    _targetPitch + intent.LookPitchDelta, freePitchMinDegrees, freePitchMaxDegrees);
                Vector3 newOffset = CameraFraming.ComputeFocusOffset(
                    _targetYaw, _targetPitch, CameraFraming.BaseDistance);

                // 原地转身：机位不动 ⇒ 焦点补偿 Offset 差（机位 = 焦点 + Offset）。
                _cleanPosition += oldOffset - newOffset;
                _goalPosition = _cleanPosition;
            }

            if (intent.FlyMove != Vector3.zero)
            {
                // 沿当前朝向的水平基向量平移（与画面所见一致）。
                Vector3 offset = CameraFraming.ComputeFocusOffset(
                    _yaw, _pitch, CameraFraming.BaseDistance);
                Vector3 forward = new Vector3(-offset.x, 0f, -offset.z).normalized;
                Vector3 right = new Vector3(forward.z, 0f, -forward.x);
                float speed = freeFlySpeed * (intent.FlyFast ? freeFlyFastScale : 1f);
                _cleanPosition += (forward * intent.FlyMove.z + right * intent.FlyMove.x
                    + Vector3.up * intent.FlyMove.y) * (speed * Time.deltaTime);
                _goalPosition = _cleanPosition;
            }
        }

        /// <summary>选中 · 环绕：右键拖拽改方位角（平滑）；俯角目标回基准 30°（平滑）。</summary>
        void ApplyOrbitInput(in BattleIntentFrame intent)
        {
            _targetPitch = CameraFraming.BasePitchDegrees;
            if (intent.OrbitDragHeld)
                _targetYaw += intent.LookYawDelta;
        }

        /// <summary>
        /// 两档取景输入（交互操作契约 §B16/B17）：Tab 翻转近景/远景；滚轮上 = 近景、下 = 远景
        /// （方向性入口，重复滚动停在同档）。只在自由镜头与浏览态被调用（锁定态不进这里）。
        /// 切换只改目标档——现实值由位姿全目标化的指数平滑逼近（无跳变不变量）。
        /// </summary>
        void ApplyFramingTierInput(in BattleIntentFrame intent)
        {
            if (intent.ZoomTogglePressed)
                _targetOrthoSize = CameraFraming.ToggleFramingTier(_targetOrthoSize, _panoramaOrthoSize);
            else if (intent.ScrollDelta > 0.01f)
                _targetOrthoSize = CameraFraming.CloseUpOrthoSize;
            else if (intent.ScrollDelta < -0.01f)
                _targetOrthoSize = _panoramaOrthoSize;
        }

        /// <summary>位姿平滑：环绕/回焦态方位与俯角指数逼近目标；自由镜头直写（转身零滞后）。</summary>
        void SmoothManualCamera(float deltaTime, InteractionState state)
        {
            if (state == InteractionState.FreeCamera)
            {
                // 自由镜头：ApplyFreeFlyInput 已直写目标；现实值直接跟随（无平滑）。
                _yaw = _targetYaw;
                _pitch = _targetPitch;
                return;
            }

            float t = CameraFeelRules.ApproachAlpha(orbitSmoothingPerSecond, deltaTime);
            if (!Mathf.Approximately(_yaw, _targetYaw))
                _yaw = Mathf.Lerp(_yaw, _targetYaw, t);
            if (!Mathf.Approximately(_pitch, _targetPitch))
                _pitch = Mathf.Lerp(_pitch, _targetPitch, t);
        }

        // ------------------------------------------------------------------
        // 焦点目标解析
        // ------------------------------------------------------------------

        /// <summary>本帧的焦点目标：跟随弹体 > 环绕选中单位（取景点）> 自由/操作/执行态的 _goalPosition。</summary>
        Vector3 ResolveGoalPosition(InteractionState state)
        {
            switch (_followState)
            {
                case CameraFollowState.FollowProjectile:
                    return _followTarget != null ? _followTarget.position : _followLastPosition;

                case CameraFollowState.DetonationHold:
                    return _deathHoldUntilUnscaled > Time.unscaledTime
                        ? _deathHoldPosition
                        : _goalPosition;

                case CameraFollowState.ReturnToFocus:
                    if (interaction != null && interaction.SelectedTarget != null)
                        return CameraFraming.FocusTargetPoint(interaction.SelectedTarget.position);
                    return _returnGoal;
            }

            // None：落水定焦窗口内先看落水点；环绕态锁定选中单位；其余维持 _goalPosition。
            if (Time.unscaledTime < _deathHoldUntilUnscaled)
                return _deathHoldPosition;
            if (state == InteractionState.SelectedIdle
                && interaction != null && interaction.SelectedTarget != null)
                return CameraFraming.FocusTargetPoint(interaction.SelectedTarget.position);
            return _goalPosition;
        }

        /// <summary>本帧的取景焦点：跟随弹体 → 弹体精确位置（无平滑，原链阻尼 0 语义）；其余 → 平滑焦点。</summary>
        Vector3 ResolveAppliedFocus()
        {
            if (_followState == CameraFollowState.FollowProjectile && _followTarget != null)
                return _followTarget.position;
            return _cleanPosition;
        }

        // ------------------------------------------------------------------
        // 震屏
        // ------------------------------------------------------------------

        /// <summary>玩家是否按住左键（瞄准输入）。此时抑制震屏，保证瞄准用的相机基向量稳定。</summary>
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
            bool shakePlaying = _shakeJustStarted
                                || (_shakeElapsed > 0f && _shakeElapsed < _shakeDuration);
            float currentAmplitude = _shakeJustStarted
                ? _shakeAmplitude
                : CurrentShakeAmplitude();
            if (shakePlaying && !CameraFeelRules.ShouldReplaceShake(currentAmplitude, profile.Amplitude))
                return;

            _shakeElapsed = 0f;
            _shakeDuration = profile.DurationSeconds;
            _shakeAmplitude = profile.Amplitude;
            _shakeRoll = profile.RollDegrees;
            _shakeFrequency = profile.FrequencyHz;
            _shakePhase = Random.Range(0f, Mathf.PI * 2f);
            _shakeJustStarted = true;
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
            _followTarget = target;
            _followState = CameraFollowState.FollowProjectile;
            _followStateElapsed = 0f;
            _followLastPosition = target.position;
            _returnGoal = interaction != null && interaction.SelectedTarget != null
                ? CameraFraming.FocusTargetPoint(interaction.SelectedTarget.position)
                : _cleanPosition;
        }

        /// <summary>结束跟随并停在 <paramref name="position"/>（平滑焦点同步搬过去，切回不跳帧）。</summary>
        void EndFollowAt(Vector3 position)
        {
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
                _followTarget = null;
                _followPirate = null;
                _followProjectile = null;
                if (next == CameraFollowState.ReturnToFocus)
                {
                    _returnGoal = interaction != null && interaction.SelectedTarget != null
                        ? CameraFraming.FocusTargetPoint(interaction.SelectedTarget.position)
                        : _followLastPosition;
                    // 从落点平滑回焦，而不是瞬移。
                    _goalPosition = _returnGoal;
                }
            }

            _followState = next;
            _followStateElapsed = 0f;
        }

        // ------------------------------------------------------------------
        // 推近 / 下压
        // ------------------------------------------------------------------

        void StartPushIn()
        {
            if (selectionPushInDegrees <= 0f || selectionPushInDurationSeconds <= 0f)
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

        // ------------------------------------------------------------------
        // EventBus 回调
        // ------------------------------------------------------------------

        void OnTurnStarted(TurnStartedPayload turn)
        {
            // §3.2 panToCharacter：回合开始的**平滑提示**（不瞬跳、不抢玩家镜头——
            // 自由镜头下玩家一动输入即接管；选中环绕由 SelectedTarget 每帧锁定，不经这里）。
            if (turn.PanTarget != null)
            {
                _goalPosition = CameraFraming.FocusTargetPoint(turn.PanTarget.position);
                StartPushIn();
            }
        }

        void OnTurnEnded(int teamNumber)
        {
            // 只借"回合结束"这个时机清落水定焦窗口（若有残留）。
            _deathHoldUntilUnscaled = 0f;
        }

        void OnCameraFocusRequested(Transform target)
        {
            // 选中反馈：自由镜头下平滑把焦点挪向该单位（环绕/操作态不抢——环绕由拉模型锁定）。
            if (target == null)
                return;
            if (interaction != null && interaction.State != InteractionState.FreeCamera)
                return;

            _goalPosition = CameraFraming.FocusTargetPoint(target.position);
            StartPushIn();
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
                // §4.4 落水即死：焦点短暂停在落水点并轻微下压。
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
