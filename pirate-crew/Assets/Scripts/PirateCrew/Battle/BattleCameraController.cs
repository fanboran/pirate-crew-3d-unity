using System.Collections.Generic;
using Cinemachine;
using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;
// 类型别名：俯角常量与像素化出图口径同源（见 OrthoPitchDegrees 的注释），
// 全限定写太长、直接 using 整个命名空间又怕与既有类型重名，故用同类别名。
using PixelartPilotScene = PirateCrew.Rendering.Pixelart.PixelartPilotScene;

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
    /// 【默认机位 — 等距像素卡通（创始人 2026-09-22 口述裁决：「游戏内应该俯仰角固定 30 度，
    ///   但是左右可以随便旋转」）】
    ///   相机为**正交投影**：视野由 <see cref="CloseUpOrthoSize"/>/<see cref="FullFieldOrthoSize"/>
    ///   等整数档 OrthoSize 决定（像素网格在世界空间对齐的前提）；Transposer 距离固定
    ///   <see cref="OrthoTransposerDistance"/>（只定机位，不再表达视野）；
    ///   **俯角锁 <see cref="OrthoPitchDegrees"/> 30° + 方位可自由旋转**：右键拖拽环绕只改方位角，
    ///   俯角没有任何输入路径（观察模式的自由俯仰是主动调试出口）；
    ///   中键自由锚与观察模式（我的世界式自由视角）保留为调试出口。
    ///   开局与每次换行动单位进入**跟随特写档**（size <see cref="CloseUpOrthoSize"/>），
    ///   lookAt 抬高 = 单位视觉高 1.85 × <see cref="LookAtHeightRatio"/> 0.65 ≈ 1.20。
    ///   滚轮按整数档缩放 OrthoSize，夹 [<see cref="MinOrthoSize"/>, <see cref="MaxOrthoSize"/>]。
    ///
    /// 【旧透视口径（距离档位 12/30/160 + 30°↔45°↔55° 俯角插值 + FOV 特效）已于 2026-09-21
    ///   随等距像素卡通切换退役】；Scope 瞄准/推近/旁观等"FOV 特效"以**当量比率**映射到
    ///   OrthoSize（<see cref="ApplyFov"/>：size = 当前手动档 × fov 当量 / 烘焙 FOV），手感量级连续。
    ///
    /// 【M4 手感（docs/M4-大海域世界化.md §3.2，数值提案/待定）】
    ///   · Scope 模式：AimThrowController 里 Shift 切换，本类把等效 FOV 从基准 60 平滑收敛到 28（0.25s）
    ///     ——正交下表现为 OrthoSize 等比收小（画面放大）；
    ///   · 力度-镜头耦合：炮台蓄力比例越大相机越拉远（特写 → 全景线性映射），松手恢复蓄力前档位；
    ///   · 弹体追焦：跟随弹体时聚焦平滑更慢更轻（<see cref="CameraFeelRules.ProjectileFollowFocusScale"/>），
    ///     落点震屏逻辑不变。
    ///
    /// 【安全底线（勿破坏）】
    ///   · **场景里烘焙的** Transposer FollowOffset = 30°/距离 30 + 正交 Lens（size
    ///     <see cref="FullFieldOrthoSize"/>）：<c>BattleSceneWiringTests.AssertOrthographicTiltedCamera</c>
    ///     断言该**烘焙值**（正交 + 俯角 30° + 距离 30 + offset.x=0，经 <see cref="BakedDistance"/> /
    ///     <see cref="BakedPitchDegrees"/>/<see cref="BakedOrthoSize"/> 读出）；运行时本脚本把它覆盖为
    ///     特写档，同测试另断言"运行时默认 = 特写档"（<see cref="RuntimeOrthoSize"/>）。
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

        [Tooltip("瞄准/投掷控制器（同场景显式注入，由 EditorTools.BattleLookupWiring 接线）。"
                 + "本类每帧采样它的 IsScopeActive / IsTurretAiming / ChargeRatio（Scope 视野混合、力度-镜头耦合），"
                 + "是**热路径依赖**——所以必须显式注入，绝不做每帧回退扫描（清退报告 2026-09-21）。")]
        [SerializeField] AimThrowController aimThrow;

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

        [Header("玩家相机微操（等距口径：俯角锁 30° + 右键方位环绕；滚轮整数档缩放 OrthoSize；观察模式/中键自由锚为调试出口）")]
        [Tooltip("滚轮缩放（OrthoSize 整数档，夹在 [MinOrthoSize, MaxOrthoSize] 常量内）。默认开。")]
        [SerializeField] bool enableManualZoom = true;

        [Tooltip("观察模式/环绕遗留的鼠标灵敏度（度/单位 Mouse X）。")]
        [SerializeField] float orbitDegreesPerMouseUnit = 3f;

        [Tooltip("手动相机平滑速率（1/s）。")]
        [SerializeField] float manualSmoothingPerSecond = 10f;

        // ------------------------------------------------------------------
        // 机位档位常量（等距像素卡通 · 正交口径：创始人裁决 2026-09-21，
        // docs/技术/渲染管线-等距像素卡通.md §2；数值为【AI 提案·首轮试产校准】，
        // 美术指南待定项 #2/#4 落定后回写）。
        //
        // 【为什么是 const 而不是 SerializeField】场景 Battle.unity 由 M2BattleSceneSetup 烘焙，
        //   序列化字段的旧值会盖掉新档位口径（×2 扫荡期的老教训）；档位一律走常量。
        // ------------------------------------------------------------------

        /// <summary>正交下 Transposer 距离不再表达视野（视野由 OrthoSize 决定），固定 30 只决定
        /// 机位高度与裁剪范围（far clip 200/400 足够覆盖世界图大跨度）。</summary>
        public const float OrthoTransposerDistance = 30f;

        /// <summary>
        /// 游戏内俯角（度）：**固定 30°**，方位角不进本常量（方位由 <c>_manualYaw</c> 承担、可自由旋转）。
        ///
        /// 【创始人 2026-09-22 口述裁决】「游戏内应该俯仰角固定 30 度，但是左右可以随便旋转」。
        /// 取值直接引用像素化出图口径 <see cref="PixelartPilotScene.PitchDegrees"/>，
        /// **不是各写一份的镜像**——出图与游戏内必须是同一个投影，"宣传图里的观感"才等于"玩的时候的观感"。
        /// sin 30° = 0.5 ⇒ 地面轴在屏幕上是横移 2 像素 / 下降 1 像素的规则像素阶梯；
        /// 真等距 35.264°（arctan(1/√2)，2026-09-21 的旧裁决）的 sin = 0.5773 与像素网格无整数比，
        /// 阶梯长短不一，故已废。
        /// </summary>
        public const float OrthoPitchDegrees = PixelartPilotScene.PitchDegrees;

        /// <summary>特写档 OrthoSize（正交半高，世界单位，整数）。开局/换行动单位的默认机位：
        /// RT 高 360px 下 1u ≈ 36px，单位 1.85u ≈ 67px（判据 A-2 的 ≥25px 富余充足）。</summary>
        public const int CloseUpOrthoSize = 5;

        /// <summary>全场档 OrthoSize：纵向 2×17=34u 覆盖样板关 30u 全场；与旧透视全场档
        /// （距离 30 + FOV 60 → 等效半高 30×tan30° ≈ 17.3）同量级取整。</summary>
        public const int FullFieldOrthoSize = 17;

        /// <summary>滚轮前推最近档（整数；再近单位贴脸、RT 下像素过粗）。</summary>
        public const int MinOrthoSize = 3;

        /// <summary>滚轮后拉最远档（整数；覆盖世界图全景跨度）。</summary>
        public const int MaxOrthoSize = 60;

        /// <summary>滚轮每格缩放的 OrthoSize 步进（整数档——像素网格在世界空间对齐的前提）。</summary>
        public const int OrthoZoomStep = 1;

        /// <summary>全景档 OrthoSize = clamp(round(span × 0.3), 全场档, 60)：span 100u → 30、160u → 48。</summary>
        public static int PanoramaOrthoSizeForSpan(float spanUnits)
        {
            return Mathf.Clamp(Mathf.RoundToInt(spanUnits * 0.3f), FullFieldOrthoSize, MaxOrthoSize);
        }

        // ---- M4 大海域档位（docs/M4-大海域世界化.md §1/§3.2；正交 size 化，取值为提案/待定）----

        /// <summary>默认地图可玩跨度（世界单位）= 现行竞技场 100u；未调 <see cref="SetWorldSpan"/> 时的缺省。</summary>
        public const float DefaultWorldSpan = 100f;

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

        // ---- FOV 当量 / 正交 size ----
        float _baseFov = 60f;             // 烘焙 FOV（正交 Lens 里仍写 60）：Scope/推近/旁观等特效的当量分母
        float _baseOrthoSize = FullFieldOrthoSize; // 烘焙 OrthoSize（正交视野档的捕获基准）
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
        // 正交口径：距离恒 <see cref="OrthoTransposerDistance"/>（不再被缩放改写），
        // 视野档位由 _manual/_targetOrthoSize 承载（整数档目标，浮点平滑中值）。
        float _manualOrthoSize = FullFieldOrthoSize;
        float _targetOrthoSize = FullFieldOrthoSize;

        // ---- M4：Scope / 力度-镜头耦合 / 全景档（数值见 CameraFeelRules 与上方常量区）----
        float _panoramaOrthoSize = PanoramaOrthoSizeForSpan(DefaultWorldSpan);
        float _scopeBlend;              // Scope 等效 FOV 混合系数 0..1（按 ScopeBlendSeconds 线性推进）
        bool _chargeZoomActive;         // 炮台蓄力拉远生效中（结束时恢复蓄力前档位）
        float _preChargeOrthoSize;

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

        /// <summary>
        /// <see cref="aimThrow"/> 是否来自**装配期注入**（而非 Awake 的一次性兜底）。
        /// 供 PlayMode 装配完整性测试区分"装配接线"与"兜底也能跑"——兜底成功不算过关。
        /// </summary>
        public bool AimThrowWiredByAssembly { get; private set; }

        /// <summary>场景里**烘焙的** Transposer 距离（Awake 从 FollowOffset 捕获；正交口径下恒 = <see cref="OrthoTransposerDistance"/> 30）。</summary>
        public float BakedDistance => _baseDistance;

        /// <summary>场景里**烘焙的** Transposer 俯角（度，由 FollowOffset 反推；= <see cref="OrthoPitchDegrees"/> 30°）。</summary>
        public float BakedPitchDegrees => PitchOf(_baseOffsetDirection);

        /// <summary>场景里**烘焙的** OrthoSize（= <see cref="FullFieldOrthoSize"/> 全场档）。</summary>
        public float BakedOrthoSize => _baseOrthoSize;

        /// <summary>运行时距离（正交口径下恒 <see cref="OrthoTransposerDistance"/>，不再表达视野；保留给接线测试断言）。</summary>
        public float RuntimeDistance => OrthoTransposerDistance;

        /// <summary>运行时俯角（度；正交口径统一 <see cref="OrthoPitchDegrees"/> 30°，观察模式除外）。</summary>
        public float RuntimePitchDegrees =>
            ObserveMode ? _observePitchDegrees : _dragPitchDegrees;

        /// <summary>运行时 OrthoSize 档（四舍五入到整数；默认特写档 <see cref="CloseUpOrthoSize"/>，滚轮可改到 [3,60]）。</summary>
        public int RuntimeOrthoSize => Mathf.RoundToInt(_targetOrthoSize);

        /// <summary>
        /// 运行时取景的等效"可见高度"（米）= 2 × OrthoSize（正交半高 ×2）。
        ///
        /// 【为什么要这个读数】它是**出图取景表的同一个单位**（<c>PixelartLevelScene</c> 的 wide/mid/close
        /// 就是按"可见多少米高"给的），所以创始人可以在游戏里滚轮挑一个最顺眼的距离、
        /// 念出这个数，取景表照改即可（HUD 提示条把它显示出来）。1 个整数档 = 2 m。
        /// </summary>
        public float RuntimeVisibleMeters => _targetOrthoSize * 2f;

        /// <summary>当前全景档 OrthoSize（由 <see cref="SetWorldSpan"/> 决定；默认跨度 100u → 30）。</summary>
        public int PanoramaOrthoSize => Mathf.RoundToInt(_panoramaOrthoSize);

        /// <summary>
        /// 【M4 新增】按地图可玩跨度设置全景档：OrthoSize = clamp(round(span × 0.3), 全场档, 60)
        /// （docs/M4-大海域世界化.md §1/§3.2 的正交化转写）。由 Battle 场景接线方在世界地图建成后调用；
        /// 不调用时默认 span=100（全景 30），既有特写档/手动缩放行为不变。
        /// </summary>
        public void SetWorldSpan(float spanUnits)
        {
            _panoramaOrthoSize = PanoramaOrthoSizeForSpan(spanUnits);
        }

        void Awake()
        {
            // 依赖解析：一次性（装配期注入优先，兜底只跑一次）——见 ResolveAimThrowOnce。
            ResolveAimThrowOnce();

            ConfigureVirtualCamera();
            CaptureBaseLens();
            CaptureManualCameraBase();
            // 开局即进入跟随特写档（用户裁决 2026-09-14）：覆盖场景里烘焙的 45°/15 出厂机位。
            EnterCloseUpView();
            if (cameraTarget != null)
                _cleanPosition = cameraTarget.position;
        }

        /// <summary>
        /// 解析 <see cref="aimThrow"/>：**只在 Awake 跑一次**（不是每帧轮询）。
        ///
        /// 【为什么是显式注入】它是每帧热路径依赖（Update 的蓄力耦合、LateUpdate 的 Scope 混合），
        /// 旧写法 `if (aimThrow == null) aimThrow = FindObjectOfType&lt;...&gt;()` 把"每帧全场扫描"
        /// 当成了懒加载兜底——改名/挪层级/失活都会静默失效且无编译期保护。
        ///
        /// 【兜底为什么保留一次】场景是**装配脚本的产物**：没有跑
        /// <c>EditorTools.BattleLookupWiring.Wire</c> 的旧场景里该字段为空。一次兜底把
        /// "静默失效"降级成"可用但吵闹"，同时把真正的装配缺陷交给
        /// <see cref="AimThrowWiredByAssembly"/>（PlayMode 测试断言它）去失败。
        /// </summary>
        void ResolveAimThrowOnce()
        {
            AimThrowWiredByAssembly = aimThrow != null;
            if (aimThrow != null)
                return;

            aimThrow = FindObjectOfType<AimThrowController>();
            Log.Warn("[BattleCameraController] aimThrow 未经装配接线，已一次性兜底解析"
                     + (aimThrow != null ? "成功" : "失败（Scope 视野混合与力度-镜头耦合将不生效）")
                     + "。修复：跑 PirateCrew.EditorTools.BattleLookupWiring.Wire（写 Battle.unity），"
                     + "把它写进 BattleCameraController.aimThrow 序列化字段。");
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
            EventBus.Subscribe<TurnStartedPayload>(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe<int>(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe<Transform>(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
            EventBus.Subscribe<ActionSelectedPayload>(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Subscribe<ProjectileDetonatedPayload>(BattleEvents.ProjectileDetonated, OnProjectileDetonated);
            EventBus.Subscribe<CrewDamagedPayload>(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe<CrewDiedPayload>(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe<AiThinkingPayload>(BattleEvents.AiThinking, OnAiThinking);
            EventBus.Subscribe<MatchFinishedPayload>(BattleEvents.MatchFinished, OnMatchFinished);

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
            // 方位环绕（右键）与观察模式/中键自由锚/滚轮缩放在此处理；俯角锁定不进输入。
            UpdateManualCameraInput();

            // M4 §3.2 力度-镜头耦合：炮台蓄力越大相机越拉远（近档→全景线性映射），松手恢复。
            // 独立于手动缩放开关——它是瞄准手感的一部分。
            UpdateChargeZoom();
        }

        /// <summary>Scope FOV 混合推进（LateUpdate，暂停时也收敛）：目标态取自 <see cref="AimThrowController.IsScopeActive"/>。</summary>
        void AdvanceScopeBlend(float unscaledDt)
        {
            // aimThrow 由 Awake 一次性解析（装配注入优先），此处只读——不再每帧 FindObjectOfType。
            bool desired = aimThrow != null && aimThrow.IsScopeActive;
            float step = unscaledDt / Mathf.Max(1e-4f, CameraFeelRules.ScopeBlendSeconds);
            _scopeBlend = Mathf.MoveTowards(_scopeBlend, desired ? 1f : 0f, step);
        }

        /// <summary>
        /// 力度-镜头耦合（M4 §3.2 的正交转写，提案）：炮台瞄准期间把缩放档覆写为
        /// "特写档 → 全景档 × 蓄力比例"的线性映射；瞄准结束恢复蓄力前的手动档。
        /// 期间滚轮已由 <see cref="AimThrowController.IsTurretAiming"/> 让给力度，二者不打架。
        /// </summary>
        void UpdateChargeZoom()
        {
            // 同上：只读 Awake 解析好的注入引用。
            bool charging = aimThrow != null && aimThrow.IsTurretAiming;
            if (charging)
            {
                if (!_chargeZoomActive)
                {
                    _chargeZoomActive = true;
                    _preChargeOrthoSize = _targetOrthoSize;
                }
                _targetOrthoSize = ChargeZoomOrthoSize(
                    CloseUpOrthoSize, _panoramaOrthoSize, aimThrow.ChargeRatio);
            }
            else if (_chargeZoomActive)
            {
                _chargeZoomActive = false;
                _targetOrthoSize = _preChargeOrthoSize;
            }
        }

        /// <summary>蓄力比例 → 特写档与全景档之间的线性 OrthoSize（旧透视版 ChargeZoomDistance 的正交当量）。</summary>
        public static float ChargeZoomOrthoSize(int closeUpSize, float panoramaSize, float chargeRatio)
        {
            return Mathf.Lerp(closeUpSize, panoramaSize, Mathf.Clamp01(chargeRatio));
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

        /// <summary>
        /// 俯角（度）→ **相机相对焦点的单位方向**：水平分量在 +X/+Z 上等分
        /// （即方位 45°——只有它给出对称菱形构图）、竖直分量 sinθ。θ=30° 时 = (0.6124, 0.5, 0.6124)，
        /// 与出图口径 <see cref="PixelartPilotScene.CameraDirection"/> 逐分量相同（俯角与方位同一个定义，
        /// 装配器/出图脚本/运行期三方不再各写一份）。
        /// `_manualYaw` 是相对该基准的方位偏移（右键环绕），故 0 方位 = 烘焙机位朝向，
        /// 不是"面向 +Z"。
        /// </summary>
        public static Vector3 OffsetDirectionForPitch(float pitchDegrees)
        {
            float p = pitchDegrees * Mathf.Deg2Rad;
            float horiz = Mathf.Cos(p) * 0.70710678f; // 水平分量在 X/Z 等分（方位 45°）
            return new Vector3(horiz, Mathf.Sin(p), horiz);
        }

        /// <summary>
        /// 进入跟随特写档：OrthoSize 立刻切到 <see cref="CloseUpOrthoSize"/>，yaw 归零
        /// （回到烘焙机位朝向：方位 45° 对称构图）、俯角切 <see cref="OrthoPitchDegrees"/> 30°，
        /// 并立即写进 Transposer/Lens —— "开局 / 换行动单位即特写"，不平滑过渡。
        /// </summary>
        void EnterCloseUpView()
        {
            _manualYaw = 0f;
            _targetYaw = 0f;
            _manualOrthoSize = CloseUpOrthoSize;
            _targetOrthoSize = CloseUpOrthoSize;
            // 换行动单位即脱离炮台瞄准：力度-镜头耦合立即失效（否则恢复逻辑会盖掉这次聚焦）。
            _chargeZoomActive = false;
            _dragPitchDegrees = OrthoPitchDegrees;
            WriteFollowOffset();
        }

        /// <summary>把当前 yaw / 俯角写进 Transposer 的 FollowOffset；距离用烘焙基准
        /// （正交口径下恒 <see cref="OrthoTransposerDistance"/>，缩放不再改写距离）。</summary>
        void WriteFollowOffset()
        {
            if (!_manualCaptured || _transposer == null)
                return;

            Vector3 direction = OffsetDirectionForPitch(
                ObserveMode ? _observePitchDegrees : _dragPitchDegrees);
            _transposer.m_FollowOffset =
                Quaternion.AngleAxis(_manualYaw, Vector3.up) * (direction * _baseDistance);
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
            {
                _baseFov = virtualCamera.m_Lens.FieldOfView;
                _baseOrthoSize = virtualCamera.m_Lens.OrthographicSize;
            }
            else if (fallbackCamera != null)
            {
                _baseFov = fallbackCamera.fieldOfView;
                _baseOrthoSize = fallbackCamera.orthographicSize;
            }

            if (_baseFov <= 0f)
                _baseFov = 60f;
            if (_baseOrthoSize <= 0f)
                _baseOrthoSize = FullFieldOrthoSize;
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

            // FOV 当量链（透视时代的特效语义原样保留）：
            // Scope 基准 60 → 28 随混合系数收敛（M4 §3.2）；推近/旁观在其上小幅度叠加。
            float fov = CameraFeelRules.ScopeFov(_baseFov, _scopeBlend);
            if (aiSpectatorEnabled)
                fov = CameraFeelRules.SpectatorFov(fov, CameraFeelRules.SpectatorFovDeltaDegrees, _spectator);
            if (_pushInActive)
                fov = CameraFeelRules.PushInFov(
                    fov, selectionPushInDegrees, _pushInElapsed, selectionPushInDurationSeconds);

            // 正交当量换算：视角比率 fov/baseFov ≈ OrthoSize 的缩放比率（Scope 时 ×28/60 ≈ 画面放大 2.1 倍），
            // 作用于**当前手动档**——滚轮档位与特效缩放自然叠加。
            float ratio = fov / Mathf.Max(1e-3f, _baseFov);
            float size = Mathf.Max(1f, _manualOrthoSize * ratio);

            if (virtualCamera != null)
            {
                LensSettings lens = virtualCamera.m_Lens;
                if (Mathf.Approximately(lens.OrthographicSize, size))
                    return;
                lens.OrthographicSize = size;
                virtualCamera.m_Lens = lens;
            }
            else if (fallbackCamera != null)
            {
                fallbackCamera.orthographicSize = size;
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

        // aimThrow 的声明见文件顶部「组装引用」段（[SerializeField] 注入，Awake 一次性解析）。
        GameObject _freeAnchor;
        Transform _followBeforeFree;

        // ---- 观察模式（我的世界同款；HUD 快捷键 3 进入、1/2/Esc 或任何聚焦退出）----
        public bool ObserveMode { get; private set; }
        float _observePitchDegrees = OrthoPitchDegrees;
        float _dragPitchDegrees = OrthoPitchDegrees;
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

            // 正交口径：距离不进手动态（恒烘焙值 30）；视野档从烘焙 OrthoSize 起步
            //（Awake 随后的 EnterCloseUpView 会切到特写档）。
            _manualOrthoSize = _baseOrthoSize;
            _targetOrthoSize = _baseOrthoSize;
            _manualYaw = 0f;
            _targetYaw = 0f;
            _dragPitchDegrees = Mathf.Clamp(PitchOf(_baseOffsetDirection), 12f, 78f);
            _manualCaptured = true;
        }

        void UpdateManualCameraInput()
        {
            if (!_manualCaptured)
                return;

            // aimThrow 已由 Awake 一次性解析（见 ResolveAimThrowOnce）——本方法在 Update 路径上，
            // 任何"回退再扫一次"都是每帧全场查询，绝不允许。

            // 【等距像素卡通 · 俯角锁定 + 方位可旋转（创始人 2026-09-22 口述裁决）】
            // 右键拖拽环绕 = 只改方位角 yaw；俯角恒 OrthoPitchDegrees 30°（_dragPitchDegrees 无输入
            // 路径改它，观察模式的自由俯仰是主动调试出口）。r12 的"左键空白处环绕"保持退役——
            // 左键是瞄准拖拽，环绕让给右键。注意方位角可自由转是**裁决明确的允许项**：
            // 地面轴的屏幕斜率 = sinθ·tanφ / sinθ·cotφ，只有方位 45°（及其对称位）那两支斜率相等，
            // 别的方位下两组地面线阶梯长短不同——这是已知取舍，不再 snap 回 45°。
            if (Input.GetMouseButton(1))
                _targetYaw += Input.GetAxis("Mouse X") * orbitDegreesPerMouseUnit;

            // 【r12 用户反馈】中键解除/恢复跟随锚定：解除后相机冻结在当前焦点，缩放即自由视角；
            // 任何一次聚焦/跟随（Follow 被赋值处）自动退出自由视角回到角色。
            if (Input.GetMouseButtonDown(2))
                ToggleFreeAnchor();

            // 【观察模式（我的世界同款）】鼠标移动即转视角（不按任何键），垂直改俯仰（12°..78° 夹紧）；
            // WASD 相机相对平移 + Space/Shift 升降（驾驶跟随锚，角色聚焦会自动收回去）。
            // 观察模式是主动的调试出口，允许破坏等距口径（含自由 yaw/俯仰）。
            if (ObserveMode)
            {
                _targetYaw += Input.GetAxis("Mouse X") * orbitDegreesPerMouseUnit;
                _observePitchDegrees = Mathf.Clamp(
                    _observePitchDegrees - Input.GetAxis("Mouse Y") * 0.35f, 12f, 78f);
                UpdateObserveFly();
            }

            // 滚轮缩放 OrthoSize（整数档）；炮台瞄准时滚轮让给力度（AimThrowController），不再同时拉相机。
            bool zoomBlocked = aimThrow != null && aimThrow.IsTurretAiming;
            if (enableManualZoom && !zoomBlocked)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 1e-5f)
                {
                    // 滚轮向上（scroll>0）= 拉近 = size 收小；按整数档步进，浮点先取整防漂移。
                    _targetOrthoSize = Mathf.Clamp(
                        Mathf.Round(_targetOrthoSize) - Mathf.Sign(scroll) * OrthoZoomStep,
                        MinOrthoSize, MaxOrthoSize);
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

            if (!Mathf.Approximately(_manualOrthoSize, _targetOrthoSize))
            {
                _manualOrthoSize = Mathf.Lerp(_manualOrthoSize, _targetOrthoSize, t);
                changed = true;
            }

            // 关键：无输入且已收敛时**不写** FollowOffset —— 保证"无输入时保持当前跟随目标"不抖。
            // OrthoSize 的写入统一走 ApplyFov（当量比率作用于当前手动档），此处只推进平滑值。
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

        void OnTurnStarted(TurnStartedPayload turn)
        {
            // §3.2 panToCharacter：TurnStartedPayload 已带默认镜头目标。
            _spectator = false;

            if (turn.PanTarget != null)
                FocusOn(turn.PanTarget);
        }

        void OnTurnEnded(int teamNumber)
        {
            // 载荷是队伍编号，本控制器不用——只借“回合结束”这个时机退出旁观态。
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
            // 载荷（队伍 / 待评估角色数）不进镜头逻辑，只用“开始思考”这个时机。
            // §8.1：AI 决策中相机停止自动滚动；配合更慢的聚焦 + 略外扩 FOV 形成"旁观"感。
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
