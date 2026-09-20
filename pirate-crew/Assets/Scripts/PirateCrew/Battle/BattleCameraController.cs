using System.Collections.Generic;
using Cinemachine;
using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗相机控制（Cinemachine 跟随 + panToCharacter + 战斗"手感"）。
    ///
    /// 【对应章节】§3.2（首回合/默认镜头目标 <c>panToCharacter</c>）、§4.5（选中反馈）、
    ///             §8.1（相机优先级：AI 决策中停止滚动）、§5.3（爆炸 falloff，震屏强度借用其形状）。
    ///
    /// 【参照模式】取自参照库 <c>rts-camera-cinemachine</c> 的 "CameraTarget 中转"：
    ///   虚拟相机 <c>Follow</c> 指向一个空 <see cref="cameraTarget"/>，本脚本只对该 Transform 做
    ///   平滑平移，从而"跟随当前行动角色"与"手动平移/锁定目标"可以共存，且不与 Cinemachine 阻尼打架。
    ///
    /// 【API 版本】目标 Cinemachine 2.9.7（风险清单 R2）：用 <c>CinemachineVirtualCamera</c>（2.x），
    ///   不是 Unity 6 的 <c>CinemachineCamera</c>。
    ///
    /// 【为什么这里能直接用强类型】<c>PirateCrew.Gameplay.asmdef</c> 的 references 显式列了 <c>"Cinemachine"</c>。
    ///   注意 Unity 只对名称以 "UnityEngine." 开头的程序集（如 uGUI 的 <c>UnityEngine.UI</c>）自动引用；
    ///   Cinemachine 是普通包程序集，asmdef 不显式引用就编译不过——不要误以为"引用了 UnityEngine 就够"。
    ///
    /// 【本文件承担的战斗手感职责（规则全在纯 C# 的 <see cref="CameraFeelRules"/>）】
    ///   1. 命中/爆炸震屏：位移 + 轻微滚转；幅度按"爆心到相机距离 / 爆炸半径"衰减；可关（<c>enableShake</c>）。
    ///   2. 投掷跟随：玩家/AI 一次投掷出手后，vcam 的 <c>Follow</c> 短暂切到弹体（抛自己则切到角色），
    ///      命中/落点短暂停留后缓动回战场焦点。跟随期间**不触碰** <c>TrajectoryPreview</c>——预览线由
    ///      <c>AimThrowController</c> 在松手时隐藏，本类绝不显示它，因此不会出现"误导的预览线"。
    ///   3. 回合/选中聚焦：沿用既有 <c>TurnStarted.PanTarget</c> 平移 + 轻微 FOV 推近；
    ///      AI 回合进入"旁观"态（聚焦更慢 + FOV 略外扩）。
    ///   4. 落水/死亡：落水即死（§4.4）时焦点短暂停在落水点并轻微下压；普通死亡只给一次很轻的震屏。
    ///   5. 顿帧（hitstop）：仅在武器引爆瞬间把 <c>Time.timeScale</c> 压到安全下限再弹回；
    ///      安全上限与"为什么不破坏回合推进"的论证见 <see cref="CameraFeelRules"/> 的顿帧小节。
    ///
    /// 【默认机位 — 用户裁决 2026-09-14：角色特写优先（"以角色特写视角操控这个角色"）】
    ///   开局与**每次换行动单位**（<c>TurnStarted.PanTarget</c> / <c>CameraFocusRequested</c> →
    ///   <see cref="FocusOn(Transform)"/>）相机进入**跟随特写档**：距离 <see cref="CloseUpDistance"/>
    ///   （12 世界单位）、俯角 <see cref="CloseUpPitchDegrees"/>（30°）、看向行动单位（lookAt 抬高 =
    ///   单位视觉高 1.85 × <see cref="LookAtHeightRatio"/> 0.65 ≈ 1.20；1.85 与
    ///   <c>CrewVisualPrefabBuilder.TargetUnitHeight</c> 同源——**角色自身尺寸不随格放大**）。
///   相机取景按「看同样的格数」等比放大：格 1→2 单位后档位距离一律 ×2。滚轮可后拉到旧的 45° 全场档
///   （距离 <see cref="FullFieldDistance"/> 30）再往后到 <see cref="MaxManualDistance"/> 160（M4 大海域档位），
///   前推最近 <see cref="MinManualDistance"/> 6；俯角随距离在 30°↔45°↔55° 间插值
///   （<see cref="PitchForDistance"/>，全景档随 <see cref="SetWorldSpan"/> 的地图跨度自适应）。
///   **"零输入守 45°/15 出厂"的旧口径已废止**；
///   无输入时相机保持当前跟随目标。
///
/// 【M4 手感（docs/M4-大海域世界化.md §3.2，数值提案/待定）】
///   · Scope 模式：AimThrowController 里 Shift 切换，本类把 FOV 从基准 60 平滑收敛到 28（0.25s）；
///   · 力度-镜头耦合：炮台蓄力比例越大相机越拉远（特写 → 全景线性映射），松手恢复蓄力前距离；
///   · 弹体追焦：跟随弹体时聚焦平滑更慢更轻（<see cref="CameraFeelRules.ProjectileFollowFocusScale"/>），
///     落点震屏逻辑不变。
    ///
    /// 【安全底线（勿破坏）】
    ///   · **场景里烘焙的** Transposer FollowOffset = 45°/距离 30（18→15→30 提案；
    ///     <c>M2BattleSceneSetup</c> 同步改为 30）：<c>BattleSceneWiringTests.AssertPerspectiveTiltedCamera</c>
    ///     断言该**烘焙值**（透视 + 45° + 距离 30 + offset.x=0，经 <see cref="BakedDistance"/> /
    ///     <see cref="BakedPitchDegrees"/> 读出）；运行时本脚本把它覆盖为特写档，同测试另断言
    ///     "运行时默认 = 特写档"（<see cref="RuntimeDistance"/> / <see cref="RuntimePitchDegrees"/>）。
    ///   · 震屏只加在"相机目标位置"上，且**玩家按住左键（正在拖拽瞄准）时一律不施加**；
    ///     <see cref="FocusPoint"/> 返回的是**去震屏**的干净位置，不影响 panToCharacter 的"离相机中心最近"判定。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleCameraController : MonoBehaviour
    {
        [Header("Cinemachine（2.9.7）")]
        [Tooltip("CinemachineVirtualCamera 组件（Cinemachine 2.x）。依赖 PirateCrew.Gameplay.asmdef 对 Cinemachine 程序集的显式引用。")]
        [SerializeField] CinemachineVirtualCamera virtualCamera;

        [Tooltip("虚拟相机的 Follow 目标（空物体）；本脚本平滑移动它来实现跟随/平移。")]
        [SerializeField] Transform cameraTarget;

        [Tooltip("无 Cinemachine 时的回退相机（直接移动其 Transform）。")]
        [SerializeField] Camera fallbackCamera;

        [Header("组装引用（手感数据源；留空时跟随/死亡反馈降级，其余仍工作）")]
        [Tooltip("战斗根。用于把 PirateId 解析成角色、把弹体取出做跟随。留空时：震屏与聚焦仍工作，"
                 + "但投掷跟随、落水/死亡的定焦无法定位（需在场景里接线）。")]
        [SerializeField] BattleController battle;

        [Header("参数")]
        [Tooltip("panToCharacter 的平滑速度（1/s）。默认 6 → 90% 到位约 0.384s，贴合原版 10 帧 @25fps 的回合节奏（§3.1）。")]
        [SerializeField] float focusLerpPerSecond = 6f;

        [Tooltip("启用时给虚拟相机设置的优先级（Brain 会自动选优先级最高的 vcam）。")]
        [SerializeField] int activePriority = 20;

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
        [Tooltip("选中/回合聚焦时的轻微 FOV 推近峰值（度）。只改 FOV，不动 Transposer 距离（PlayMode 断言要求距离 30）。")]
        [SerializeField] float selectionPushInDegrees = CameraFeelRules.SelectionPushInDegrees;

        [Tooltip("FOV 推近单程时长（秒）。")]
        [SerializeField] float selectionPushInDurationSeconds = CameraFeelRules.SelectionPushInDurationSeconds;

        [Tooltip("AI 回合进入旁观态（聚焦更慢 + FOV 略外扩）。")]
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

        [Header("玩家相机微操（用户拍板默认开启：右键环绕 + 滚轮缩放；默认特写档，滚轮可拉到旧 45° 全场）")]
        [Tooltip("右键拖拽环绕（改 Transposer 的 yaw，保持当前俯角/距离不变）。默认开。")]
        [SerializeField] bool enableManualOrbit = true;

        [Tooltip("滚轮缩放（改 Transposer 距离，夹在 [MinManualDistance, MaxManualDistance] 常量内）。默认开。")]
        [SerializeField] bool enableManualZoom = true;

        [Tooltip("右键每单位 Mouse X 的环绕角度（度）。")]
        [SerializeField] float orbitDegreesPerMouseUnit = 3f;

        [Tooltip("滚轮每格缩放的距离（世界单位；随格 1→2 单位 ×2 = 3）。")]
        [SerializeField] float zoomStepPerNotch = 3f;

        [Tooltip("手动相机平滑速率（1/s）。")]
        [SerializeField] float manualSmoothingPerSecond = 10f;

        // ------------------------------------------------------------------
        // 机位档位常量（用户裁决 2026-09-14：默认角色特写，滚轮后拉到旧 45° 全场）
        //
        // 【为什么是 const 而不是 SerializeField】场景 Battle.unity 由禁改的 M2BattleSceneSetup 烘焙，
        //   里面仍写着旧的 minManualDistance=12 / maxManualDistance=26；若走序列化字段，本轮的
        //   "最近 6 / 最远 50" 会被场景里的旧值盖掉。故档位口径一律走常量，场景里的旧键失效（Unity 自动忽略）。
        // ------------------------------------------------------------------

        /// <summary>特写档距离（世界单位，旧值 6 ×2 = 12；格 1→2 单位后按「看同样的格数」等比放大）。
        /// 开局 / 换行动单位时的默认机位。</summary>
        public const float CloseUpDistance = 12f;

        /// <summary>特写档俯角（度，用户裁决区间 25–35 取中值 30）。</summary>
        public const float CloseUpPitchDegrees = 30f;

        /// <summary>全场档距离（世界单位）= 18→15→30（格 1→2 单位后 ×2）；滚轮拉到此处即旧 45° 全场视角。</summary>
        public const float FullFieldDistance = 30f;

        /// <summary>全场档俯角（度）= 旧的出厂俯角 45°。</summary>
        public const float FullFieldPitchDegrees = 45f;

        /// <summary>滚轮前推最近距离（世界单位，旧值 3 ×2 = 6）。</summary>
        public const float MinManualDistance = 6f;

        /// <summary>
        /// 滚轮后拉最远距离（世界单位）。<b>M4 改 50 → 160</b>（docs/M4-大海域世界化.md §1/§3.2：
        /// 大地图手动上限外推；既有断言已随 M4 更新）。
        /// </summary>
        public const float MaxManualDistance = 160f;

        // ---- M4 大海域档位（docs/M4-大海域世界化.md §1/§3.2；取值为提案/待定）----

        /// <summary>默认地图可玩跨度（世界单位）= 现行竞技场 100u；未调 <see cref="SetWorldSpan"/> 时的缺省。</summary>
        public const float DefaultWorldSpan = 100f;

        /// <summary>全景档距离随地图跨度的比例：全景 = clamp(span × 0.55, 60, 160)（M4 §1）。</summary>
        public const float PanoramaSpanScale = 0.55f;

        /// <summary>全景档距离下限（世界单位，M4 §1）。</summary>
        public const float MinPanoramaDistance = 60f;

        /// <summary>全景档俯角（度，提案）：从全场档 45° 继续外推到 55°，越远越俯视。</summary>
        public const float PanoramaPitchDegrees = 55f;

        /// <summary>全景档距离 = clamp(span × <see cref="PanoramaSpanScale"/>, <see cref="MinPanoramaDistance"/>, <see cref="MaxManualDistance"/>)。</summary>
        public static float PanoramaDistanceForSpan(float spanUnits)
        {
            return Mathf.Clamp(spanUnits * PanoramaSpanScale, MinPanoramaDistance, MaxManualDistance);
        }

        /// <summary>
        /// 单位视觉总高（世界单位）= 1.85，与 <c>CrewVisualPrefabBuilder.TargetUnitHeight</c> 同源
        /// （Godot `pirate.tscn` 总高；1 格 = 1 Godot 单位 = 1 本工程单位，见该文件类头推导链）。
        /// 运行时不引用 Editor 程序集，故此处以常量镜像。
        /// </summary>
        public const float UnitVisualHeight = 1.85f;

        /// <summary>lookAt 抬高比例（用户裁决区间 0.6–0.7 取中值 0.65）：镜头看向单位胸/头部而非脚底。</summary>
        public const float LookAtHeightRatio = 0.65f;

        /// <summary>lookAt 抬高（世界单位）= 1.85 × 0.65 ≈ 1.2025，作用在相机目标点上。</summary>
        public static float LookAtHeight => UnitVisualHeight * LookAtHeightRatio;

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

        // ---- FOV 推近 / 旁观 ----
        float _baseFov = 60f;
        bool _baseFovCaptured;
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

        // ---- 手动环绕/缩放 ----
        CinemachineTransposer _transposer;
        Vector3 _baseOffsetDirection = Vector3.back;
        float _baseDistance;
        bool _manualCaptured;
        float _manualYaw;
        float _targetYaw;
        float _manualDistance;
        float _targetDistance;

        // ---- M4：Scope / 力度-镜头耦合 / 全景档（数值见 CameraFeelRules 与上方常量区）----
        float _panoramaDistance = PanoramaDistanceForSpan(DefaultWorldSpan);
        float _scopeBlend;              // Scope FOV 混合系数 0..1（按 ScopeBlendSeconds 线性推进）
        bool _chargeZoomActive;         // 炮台蓄力拉远生效中（结束时恢复蓄力前距离）
        float _preChargeDistance;

        /// <summary>跟随状态机的时间参数。</summary>
        CameraFeelTimings Timings => new CameraFeelTimings(
            followTimeoutSeconds, detonationHoldSeconds, followReturnSeconds);

        /// <summary>
        /// 当前相机聚焦参考点（供 panToCharacter 的"离相机中心最近"判定）。
        /// <b>返回去震屏的干净位置</b>：震屏是纯表现，不应影响"选哪个角色当镜头目标"的判定。
        /// </summary>
        public Vector3 FocusPoint
        {
            get
            {
                if (cameraTarget != null)
                    return _cleanInitialized ? _cleanPosition : cameraTarget.position;
                if (fallbackCamera != null)
                    return fallbackCamera.transform.position;
                return transform.position;
            }
        }

        /// <summary>当前跟随状态（调试/测试用）。</summary>
        public CameraFollowState FollowState => _followState;

        /// <summary>当前是否处于 AI 旁观态（调试/测试用）。</summary>
        public bool SpectatorMode => _spectator;

        /// <summary>场景里**烘焙的** Transposer 距离（Awake 从 FollowOffset 捕获；= <see cref="FullFieldDistance"/> 30）。</summary>
        public float BakedDistance => _baseDistance;

        /// <summary>场景里**烘焙的** Transposer 俯角（度，由 FollowOffset 反推；= <see cref="FullFieldPitchDegrees"/> 45°）。</summary>
        public float BakedPitchDegrees => PitchOf(_baseOffsetDirection);

        /// <summary>运行时当前距离目标（默认特写档 <see cref="CloseUpDistance"/>；滚轮可改到 [6,160]）。</summary>
        public float RuntimeDistance => _manualCaptured ? _targetDistance : CloseUpDistance;

        /// <summary>运行时当前俯角（度，由距离插值：特写 30° ↔ 全场 45° ↔ 全景 55° 外推）。</summary>
        public float RuntimePitchDegrees => PitchForDistance(RuntimeDistance, _panoramaDistance);

        /// <summary>当前全景档距离（由 <see cref="SetWorldSpan"/> 决定；默认跨度 100u → 60）。</summary>
        public float PanoramaDistance => _panoramaDistance;

        /// <summary>
        /// 【M4 新增】按地图可玩跨度设置全景档：距离 = clamp(span × 0.55, 60, 160)、
        /// 俯角插值相应外推（docs/M4-大海域世界化.md §1/§3.2）。
        /// 由 Battle 场景接线方在世界地图建成后调用；不调用时默认 span=100（全景 60），
        /// 既有特写档/手动缩放行为不变。
        /// </summary>
        public void SetWorldSpan(float spanUnits)
        {
            _panoramaDistance = PanoramaDistanceForSpan(spanUnits);
        }

        void Awake()
        {
            ConfigureVirtualCamera();
            CaptureBaseLens();
            CaptureManualCameraBase();
            // 开局即进入跟随特写档（用户裁决 2026-09-14）：覆盖场景里烘焙的 45°/15 出厂机位。
            EnterCloseUpView();
            if (cameraTarget != null)
                _cleanPosition = cameraTarget.position;
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
            EventBus.Subscribe(BattleEvents.AiThinking, OnAiThinking);
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Unsubscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Unsubscribe(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Unsubscribe(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Unsubscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Unsubscribe(BattleEvents.AiThinking, OnAiThinking);
            EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);

            // 兜底：任何情况下都不能把 timeScale 留在压低状态（否则整个工程"卡死"）。
            _sceneUnloading = true;
            RestoreTimeScale();
        }

        void Update()
        {
            // 暂停中：冻结手动相机输入（环绕/缩放），画面平滑交由 LateUpdate 原样收敛。
            if (BattlePause.IsPaused)
                return;

            // 手动相机输入用 Update 采样（LateUpdate 处理画面平滑）。
            if (enableManualOrbit || enableManualZoom)
                UpdateManualCameraInput();

            // M4 §3.2 力度-镜头耦合：炮台蓄力越大相机越拉远（近档→全景线性映射），松手恢复。
            // 独立于手动缩放开关——它是瞄准手感的一部分。
            UpdateChargeZoom();
        }

        /// <summary>Scope FOV 混合推进（LateUpdate，暂停时也收敛）：目标态取自 <see cref="AimThrowController.IsScopeActive"/>。</summary>
        void AdvanceScopeBlend(float unscaledDt)
        {
            if (aimThrow == null)
                aimThrow = FindObjectOfType<AimThrowController>();

            bool desired = aimThrow != null && aimThrow.IsScopeActive;
            float step = unscaledDt / Mathf.Max(1e-4f, CameraFeelRules.ScopeBlendSeconds);
            _scopeBlend = Mathf.MoveTowards(_scopeBlend, desired ? 1f : 0f, step);
        }

        /// <summary>
        /// 力度-镜头耦合（M4 §3.2，提案）：炮台瞄准期间把缩放目标覆写为
        /// "特写档 → 全景档 × 蓄力比例"的线性映射；瞄准结束恢复蓄力前的手动距离。
        /// 期间滚轮已由 <see cref="AimThrowController.IsTurretAiming"/> 让给力度，二者不打架。
        /// </summary>
        void UpdateChargeZoom()
        {
            if (aimThrow == null)
                aimThrow = FindObjectOfType<AimThrowController>();

            bool charging = aimThrow != null && aimThrow.IsTurretAiming;
            if (charging)
            {
                if (!_chargeZoomActive)
                {
                    _chargeZoomActive = true;
                    _preChargeDistance = _targetDistance;
                }
                _targetDistance = CameraFeelRules.ChargeZoomDistance(
                    CloseUpDistance, _panoramaDistance, aimThrow.ChargeRatio);
            }
            else if (_chargeZoomActive)
            {
                _chargeZoomActive = false;
                _targetDistance = _preChargeDistance;
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

            ApplyFov();
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

            Vector3 dipOffset = _dipActive
                ? Vector3.up * CameraFeelRules.DrownDipOffset(_dipElapsed, _dipDuration, _dipAmount)
                : Vector3.zero;

            Vector3 finalPosition = _cleanPosition + dipOffset + ShakeOffsetToWorld(shake2D);
            ApplyCameraPosition(finalPosition);
            ApplyRoll(roll);
        }

        // ------------------------------------------------------------------
        // 对外 API（保持既有语义）
        // ------------------------------------------------------------------

        /// <summary>
        /// 聚焦到某 Transform（回合开始 / 选中角色时调用）。**同时进入跟随特写档**（用户裁决 2026-09-14：
        /// "开局/每次换行动单位时镜头进入跟随特写"），lookAt 抬到单位胸/头高度。
        /// </summary>
        public void FocusOn(Transform target)
        {
            if (target == null)
                return;

            _focusTarget = target;
            _goalPosition = FocusTargetPoint(target);
            CancelFollow();
            EnterCloseUpView();
            StartPushIn();
        }

        /// <summary>聚焦到某世界坐标（一次性）。同样回到特写档（换机位即回到"操作当前角色"的距离感）。</summary>
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
        // 机位档位（特写 ↔ 全场）
        // ------------------------------------------------------------------

        /// <summary>聚焦点 = 单位脚底枢轴 + lookAt 抬高（镜头看向胸/头，而非脚底）。</summary>
        public static Vector3 FocusTargetPoint(Transform target)
        {
            return target.position + Vector3.up * LookAtHeight;
        }

        /// <summary>由 Transposer 偏移方向反推俯角（度）：<c>offset = (0, d·sinP, d·cosP)</c>。</summary>
        public static float PitchOf(Vector3 offsetDirection)
        {
            return Mathf.Atan2(offsetDirection.y,
                new Vector2(offsetDirection.x, offsetDirection.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>俯角（度）→ yaw=0 的 +Z/+Y 平面内单位偏移方向（与烘焙机位同格式）。</summary>
        public static Vector3 OffsetDirectionForPitch(float pitchDegrees)
        {
            float p = pitchDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(p), Mathf.Cos(p));
        }

        /// <summary>俯角随距离插值（默认跨度，供测试与工具直调）：
        /// ≤ <see cref="CloseUpDistance"/> → <see cref="CloseUpPitchDegrees"/>（30°）；
        /// ≤ <see cref="FullFieldDistance"/> → 30°..45° 线性（30u 处恰为旧 45° 全场）；
        /// ≤ 全景档 → 45°..<see cref="PanoramaPitchDegrees"/> 继续外推（M4 大海域档位）；
        /// 再远维持全景俯角。</summary>
        public static float PitchForDistance(float distance)
        {
            return PitchForDistance(distance, PanoramaDistanceForSpan(DefaultWorldSpan));
        }

        /// <summary>同上，但全景档距离由调用方给（实例按当前 <see cref="SetWorldSpan"/> 跨度取）。</summary>
        public static float PitchForDistance(float distance, float panoramaDistance)
        {
            if (distance <= FullFieldDistance)
            {
                float nearT = Mathf.InverseLerp(CloseUpDistance, FullFieldDistance, distance);
                return Mathf.Lerp(CloseUpPitchDegrees, FullFieldPitchDegrees, nearT);
            }

            float panorama = Mathf.Max(panoramaDistance, FullFieldDistance + 0.01f);
            float farT = Mathf.InverseLerp(FullFieldDistance, panorama, distance);
            return Mathf.Lerp(FullFieldPitchDegrees, PanoramaPitchDegrees, farT);
        }

        /// <summary>
        /// 进入跟随特写档：距离/俯角立刻切到 <see cref="CloseUpDistance"/> 12 / <see cref="CloseUpPitchDegrees"/> 30°，
        /// yaw 归零（面向 +Z，与烘焙机位同朝向），并立即写进 Transposer —— "开局 / 换行动单位即特写"，不平滑过渡。
        /// </summary>
        void EnterCloseUpView()
        {
            _manualYaw = 0f;
            _targetYaw = 0f;
            _manualDistance = CloseUpDistance;
            _targetDistance = CloseUpDistance;
            // 换行动单位即脱离炮台瞄准：力度-镜头耦合立即失效（否则恢复逻辑会盖掉这次聚焦）。
            _chargeZoomActive = false;
            // 特写档的俯角也要切：_dragPitchDegrees 初始化/捕获自烘焙机位（45°），只切距离的话
            // 特写会带着 45° 烘焙俯角运行（PlayMode 门禁 2026-09-14 抓到——旧实现从未真正进入 30°）。
            _dragPitchDegrees = CloseUpPitchDegrees;
            WriteFollowOffset();
        }

        /// <summary>把当前 yaw / 距离 / 俯角（由距离插值）写进 Transposer 的 FollowOffset。</summary>
        void WriteFollowOffset()
        {
            if (!_manualCaptured || _transposer == null)
                return;

            Vector3 direction = OffsetDirectionForPitch(
                ObserveMode ? _observePitchDegrees : _dragPitchDegrees);
            _transposer.m_FollowOffset =
                Quaternion.AngleAxis(_manualYaw, Vector3.up) * (direction * _manualDistance);
        }

        // ------------------------------------------------------------------
        // 位置 / 朝向应用
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
                    return _focusTarget != null ? FocusTargetPoint(_focusTarget) : _returnGoal;
            }

            // None：落水定焦窗口内先看落水点，否则看当前行动角色（含 lookAt 抬高）。
            if (Time.unscaledTime < _deathHoldUntilUnscaled)
                return _deathHoldPosition;
            return _focusTarget != null ? FocusTargetPoint(_focusTarget) : _goalPosition;
        }

        void ApplyCameraPosition(Vector3 position)
        {
            if (cameraTarget != null)
                cameraTarget.position = position;
            else if (fallbackCamera != null)
                fallbackCamera.transform.position = position;
        }

        /// <summary>把相机局部平面位移（x=右, y=上）映射到世界。无相机基向量时退化为世界 XZ/Y。</summary>
        Vector3 ShakeOffsetToWorld(Vector2 offset2D)
        {
            if (offset2D == Vector2.zero)
                return Vector3.zero;

            Transform basis = null;
            if (fallbackCamera != null)
                basis = fallbackCamera.transform;
            else if (virtualCamera != null)
                basis = virtualCamera.transform;

            if (basis == null)
                return new Vector3(offset2D.x, offset2D.y, 0f);

            return basis.right * offset2D.x + basis.up * offset2D.y;
        }

        void ApplyRoll(float rollDegrees)
        {
            if (virtualCamera == null)
                return;

            LensSettings lens = virtualCamera.m_Lens;
            if (Mathf.Approximately(lens.Dutch, rollDegrees))
                return;

            lens.Dutch = rollDegrees;
            virtualCamera.m_Lens = lens;
        }

        // ------------------------------------------------------------------
        // FOV：推近 + 旁观（只改 FOV，绝不动 Transposer 的 pitch/距离/yaw）
        // ------------------------------------------------------------------

        void CaptureBaseLens()
        {
            if (virtualCamera != null)
                _baseFov = virtualCamera.m_Lens.FieldOfView;
            else if (fallbackCamera != null)
                _baseFov = fallbackCamera.fieldOfView;

            if (_baseFov <= 0f)
                _baseFov = 60f;
            _baseFovCaptured = true;
        }

        void StartPushIn()
        {
            if (!_baseFovCaptured || selectionPushInDegrees <= 0f
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

        void ApplyFov()
        {
            if (!_baseFovCaptured)
                return;

            // Scope 基准：60 → 28 随混合系数收敛（M4 §3.2）；推近/旁观在其上小幅度叠加。
            float fov = CameraFeelRules.ScopeFov(_baseFov, _scopeBlend);
            if (aiSpectatorEnabled)
                fov = CameraFeelRules.SpectatorFov(fov, CameraFeelRules.SpectatorFovDeltaDegrees, _spectator);
            if (_pushInActive)
                fov = CameraFeelRules.PushInFov(
                    fov, selectionPushInDegrees, _pushInElapsed, selectionPushInDurationSeconds);

            if (virtualCamera != null)
            {
                LensSettings lens = virtualCamera.m_Lens;
                if (Mathf.Approximately(lens.FieldOfView, fov))
                    return;
                lens.FieldOfView = fov;
                virtualCamera.m_Lens = lens;
            }
            else if (fallbackCamera != null)
            {
                fallbackCamera.fieldOfView = fov;
            }
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
            _followTarget = target;
            _followState = CameraFollowState.FollowProjectile;
            _followStateElapsed = 0f;
            _followLastPosition = target.position;
            _returnGoal = _focusTarget != null ? FocusTargetPoint(_focusTarget) : _cleanPosition;

            // 参照库"Follow 切到目标"：跟随时把 vcam 的 Follow 直接指向弹体/角色，镜头最跟手。
            if (virtualCamera != null)
            {
                ExitFreeAnchor();
                virtualCamera.Follow = target;
            }
        }

        /// <summary>结束跟随并停在 <paramref name="position"/>（同时把中转 Target 也搬过去，切回不跳帧）。</summary>
        void EndFollowAt(Vector3 position)
        {
            RestoreDefaultFollow();
            _followLastPosition = position;
            _cleanPosition = position;
            _goalPosition = position;
            _cleanInitialized = true;

            if (cameraTarget != null)
                cameraTarget.position = position;

            _followPirate = null;
            _followProjectile = null;
            _followTarget = null;
        }

        void CancelFollow()
        {
            if (_followState == CameraFollowState.None && _followTarget == null)
                return;

            if (_followState == CameraFollowState.FollowProjectile)
                RestoreDefaultFollow();

            _followState = CameraFollowState.None;
            _followTarget = null;
            _followPirate = null;
            _followProjectile = null;
            _followStateElapsed = 0f;
        }

        void RestoreDefaultFollow()
        {
            if (virtualCamera != null)
            {
                ExitFreeAnchor();
                virtualCamera.Follow = cameraTarget;
            }
        }

        // ------------------------------------------------------------------
        // 自由视角锚定（r12：中键切换）
        // ------------------------------------------------------------------

        AimThrowController aimThrow;
        GameObject _freeAnchor;
        Transform _followBeforeFree;

        // ---- 观察模式（我的世界同款；HUD 快捷键 3 进入、1/2/Esc 或任何聚焦退出）----
        public bool ObserveMode { get; private set; }
        float _observePitchDegrees = 45f;
        float _dragPitchDegrees = 45f;
        const float ObserveFlySpeed = 12f;

        /// <summary>观察模式飞行：WASD 沿相机水平朝向平移、Space 升 / Shift 降（我的世界创造式）。</summary>
        void UpdateObserveFly()
        {
            if (_freeAnchor == null || fallbackCamera == null)
                return;

            Transform cam = fallbackCamera.transform;
            Vector3 flatFwd = Vector3.Scale(cam.forward, new Vector3(1f, 0f, 1f)).normalized;
            Vector3 flatRight = Vector3.Scale(cam.right, new Vector3(1f, 0f, 1f)).normalized;

            float fwd = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float side = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float up = (Input.GetKey(KeyCode.Space) ? 1f : 0f)
                - (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1f : 0f);

            if (fwd == 0f && side == 0f && up == 0f)
                return;

            _freeAnchor.transform.position +=
                (flatFwd * fwd + flatRight * side + Vector3.up * up)
                * (ObserveFlySpeed * Time.deltaTime);
        }

        /// <summary>进入/退出观察模式：进入=冻结跟随锚 + 鼠标转视角；退出=恢复跟随。</summary>
        public void SetObserveMode(bool on)
        {
            if (ObserveMode == on)
                return;

            ObserveMode = on;
            if (on)
            {
                if (!_manualCaptured)
                    CaptureManualCameraBase();
                _observePitchDegrees = Mathf.Clamp(PitchOf(_baseOffsetDirection), 12f, 78f);
                if (_freeAnchor == null)
                    ToggleFreeAnchor();
            }
            else
            {
                ExitFreeAnchor();
            }
        }

        /// <summary>中键：把 Follow 换到当前焦点的静态锚（相机不再跟人，环绕/缩放即自由视角）；再按恢复。</summary>
        void ToggleFreeAnchor()
        {
            if (virtualCamera == null)
                return;

            if (_freeAnchor == null)
            {
                _followBeforeFree = virtualCamera.Follow;
                _freeAnchor = new GameObject("CameraFreeAnchor");
                _freeAnchor.transform.position = _followBeforeFree != null
                    ? _followBeforeFree.position
                    : transform.position + transform.forward * 10f;
                virtualCamera.Follow = _freeAnchor.transform;
            }
            else
            {
                if (_followBeforeFree != null)
                    virtualCamera.Follow = _followBeforeFree;
                Destroy(_freeAnchor);
                _freeAnchor = null;
                _followBeforeFree = null;
            }
        }

        /// <summary>任何一次聚焦/跟随赋 Follow 前调用：自由视角是临时态，聚焦即回归跟随。</summary>
        void ExitFreeAnchor()
        {
            ObserveMode = false;
            if (_freeAnchor == null)
                return;
            Destroy(_freeAnchor);
            _freeAnchor = null;
            _followBeforeFree = null;
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
            if (next == CameraFollowState.None)
            {
                RestoreDefaultFollow();
                _followTarget = null;
                _followPirate = null;
                _followProjectile = null;
            }
            else if (next == CameraFollowState.ReturnToFocus)
            {
                RestoreDefaultFollow();
                _followTarget = null;
                _followPirate = null;
                _followProjectile = null;
                _returnGoal = _focusTarget != null ? FocusTargetPoint(_focusTarget) : _followLastPosition;
                // 从落点平滑回焦，而不是瞬移。
                _goalPosition = _focusTarget != null ? FocusTargetPoint(_focusTarget) : _followLastPosition;
            }

            _followState = next;
            _followStateElapsed = 0f;
        }

        // ------------------------------------------------------------------
        // 落水 / 死亡
        // ------------------------------------------------------------------

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
        // 手动环绕 / 缩放（默认开启：右键环绕 + 滚轮缩放特写↔全场）
        // ------------------------------------------------------------------

        void CaptureManualCameraBase()
        {
            if (virtualCamera == null)
                return;

            _transposer = virtualCamera.GetCinemachineComponent<CinemachineTransposer>();
            if (_transposer == null)
                return;

            Vector3 offset = _transposer.m_FollowOffset;
            _baseDistance = offset.magnitude;
            if (_baseDistance > 1e-4f)
                _baseOffsetDirection = offset / _baseDistance;

            _manualDistance = _baseDistance;
            _targetDistance = _baseDistance;
            _manualYaw = 0f;
            _targetYaw = 0f;
            _dragPitchDegrees = Mathf.Clamp(PitchOf(_baseOffsetDirection), 12f, 78f);
            _manualCaptured = true;
        }

        void UpdateManualCameraInput()
        {
            if (!_manualCaptured)
                return;

            // 【r12 用户反馈】左键拖空白处也要能环绕（瞄准拖拽以"按在单位上"开始，二者不打架）。
            if (aimThrow == null)
                aimThrow = FindObjectOfType<AimThrowController>();
            bool leftOrbit = Input.GetMouseButton(0) && !ObserveMode
                && (aimThrow == null || (!aimThrow.IsAiming && !aimThrow.PressStartedOnUnit));
            bool orbitHeld = Input.GetMouseButton(1) || leftOrbit;

            if (enableManualOrbit && orbitHeld)
            {
                // 拖拽环绕 = 水平转 yaw + 垂直改俯仰（12°..78° 夹紧）——左/右键同规则。
                float dx = Input.GetAxis("Mouse X");
                if (Mathf.Abs(dx) > 1e-5f)
                    _targetYaw += dx * orbitDegreesPerMouseUnit;
                float dy = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(dy) > 1e-5f)
                    _dragPitchDegrees = Mathf.Clamp(
                        _dragPitchDegrees - dy * 0.35f, 12f, 78f);
            }

            // 【r12 用户反馈】中键解除/恢复跟随锚定：解除后相机冻结在当前焦点，环绕+缩放即自由视角；
            // 任何一次聚焦/跟随（Follow 被赋值处）自动退出自由视角回到角色。
            if (Input.GetMouseButtonDown(2))
                ToggleFreeAnchor();

            // 【观察模式（我的世界同款）】鼠标移动即转视角（不按任何键），垂直改俯仰（12°..78° 夹紧）；
            // WASD 相机相对平移 + Space/Shift 升降（驾驶跟随锚，角色聚焦会自动收回去）。
            if (ObserveMode)
            {
                _targetYaw += Input.GetAxis("Mouse X") * orbitDegreesPerMouseUnit;
                _observePitchDegrees = Mathf.Clamp(
                    _observePitchDegrees - Input.GetAxis("Mouse Y") * 0.35f, 12f, 78f);
                UpdateObserveFly();
            }

            // 滚轮缩放；炮台瞄准时滚轮让给力度（AimThrowController），不再同时拉相机。
            bool zoomBlocked = aimThrow != null && aimThrow.IsTurretAiming;
            if (enableManualZoom && !zoomBlocked)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 1e-5f)
                {
                    _targetDistance = Mathf.Clamp(
                        _targetDistance - scroll * zoomStepPerNotch,
                        MinManualDistance, MaxManualDistance);
                }
            }
        }

        void SmoothManualCamera(float deltaTime)
        {
            if (!_manualCaptured)
                return;

            float t = CameraFeelRules.ApproachAlpha(manualSmoothingPerSecond, deltaTime);
            bool changed = false;

            if (!Mathf.Approximately(_manualYaw, _targetYaw))
            {
                _manualYaw = Mathf.Lerp(_manualYaw, _targetYaw, t);
                changed = true;
            }

            if (!Mathf.Approximately(_manualDistance, _targetDistance))
            {
                _manualDistance = Mathf.Lerp(_manualDistance, _targetDistance, t);
                changed = true;
            }

            // 关键：无输入且已收敛时**不写** FollowOffset —— 保证"无输入时保持当前跟随目标"不抖；
            // 出厂态不再是 45°/15（该旧口径已废止，默认特写档见 EnterCloseUpView）。
            if (!changed)
                return;

            WriteFollowOffset();
        }

        // ------------------------------------------------------------------
        // Cinemachine 接线（强类型）
        // ------------------------------------------------------------------

        /// <summary>补齐虚拟相机的优先级与 Follow 目标（Follow 只在未手工指定时代填，不覆盖美术/策划配置）。</summary>
        void ConfigureVirtualCamera()
        {
            if (virtualCamera == null)
                return;

            virtualCamera.Priority = activePriority;

            if (virtualCamera.Follow == null && cameraTarget != null)
                virtualCamera.Follow = cameraTarget;
        }

        // ------------------------------------------------------------------
        // EventBus 回调
        // ------------------------------------------------------------------

        void OnTurnStarted(object payload)
        {
            // §3.2 panToCharacter：TurnStartedPayload 已带默认镜头目标。
            _spectator = false;

            if (payload is TurnStartedPayload turn && turn.PanTarget != null)
                FocusOn(turn.PanTarget);
        }

        void OnTurnEnded(object payload)
        {
            _spectator = false;
        }

        void OnCameraFocusRequested(object payload)
        {
            // FocusRequested 在规则层优先级最高：任何时候都取消跟随（回合推进绝不被跟随拖住）。
            if (payload is Transform target)
                FocusOn(target);
        }

        void OnAiThinking(object payload)
        {
            // §8.1：AI 决策中相机停止自动滚动；配合更慢的聚焦 + 略外扩 FOV 形成"旁观"感。
            if (aiSpectatorEnabled)
                _spectator = true;
        }

        void OnActionSelected(object payload)
        {
            if (!(payload is ActionSelectedPayload action) || battle == null)
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

        void OnProjectileDetonated(object payload)
        {
            if (!(payload is ProjectileDetonatedPayload detonated))
                return;

            bool wasFollowing = _followState == CameraFollowState.FollowProjectile;
            if (wasFollowing)
            {
                // 事件在 Destroy(gameObject) 之前同步发布，此处切回 Follow 是安全的。
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

        void OnCrewDamaged(object payload)
        {
            if (!(payload is CrewDamagedPayload damaged) || !enableShake)
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

        void OnCrewDied(object payload)
        {
            if (!(payload is CrewDiedPayload died) || battle == null)
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

        void OnMatchFinished(object payload)
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
