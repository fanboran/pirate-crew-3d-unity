using System.Collections.Generic;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 瞄准 / 投掷控制器（翻译自 Godot <c>scripts/aiming/aim_controller.gd</c> + <c>battle.gd</c> 的发射段，3D 重写）。
    ///
    /// 【对应章节】§3.4（严格两阶段：点选己方角色 → 选动作/武器 → 拖拽）、
    ///             §5.1（初速 = 0.25 × 拖拽距离、方向取反、twangMax 限速）。
    ///
    /// 【流程】
    ///   1. 点选：屏幕空间 30px 内最近的本队存活角色（对应 §3.4 <c>minD2 = 900</c>）。
    ///   2. 拖拽：<see cref="Camera.ScreenPointToRay"/> + <see cref="Physics.Raycast"/> 取目标点
    ///      （落空时回退到战斗平面 z = planeZ 的数学交点）。
    ///   3. 释放：<see cref="Ballistics.TwangVelocity"/> 算初速，经 <see cref="LevelGeometry"/> 换算后
    ///      以 <see cref="ForceMode.Impulse"/> 施加（Δv = impulse / mass）。
    ///
    /// 【与预览同源】预览与实弹使用同一份 (vx, vy)（同一 <see cref="Ballistics"/> 输出）
    /// 与同一份 <see cref="LevelGeometry"/> 换算常量。
    /// </summary>
    public sealed class AimThrowController : MonoBehaviour
    {
        enum Phase
        {
            Idle = 0,
            CharacterSelected = 1,
            Dragging = 2,
        }

        [Header("组装引用（场景内直连）")]
        [SerializeField] Camera battleCamera;
        [SerializeField] BattleController battle;
        [SerializeField] TrajectoryPreview trajectory;

        [Header("命中层")]
        [SerializeField] LayerMask unitMask = ~0;
        [SerializeField] LayerMask aimPlaneMask = ~0;

        [Header("参数")]
        [Tooltip("拖拽释放的最小距离（px）；低于此值视为误触，不发射（§3.4 松手阈值）。")]
        [SerializeField] float minDragPixels = 1f;
        [SerializeField] float maxRayDistance = 500f;

        Phase _phase = Phase.Idle;
        PirateBase _selected;
        PirateBase _hovered;
        Vector3 _originWorld;
        Vector2 _dragStartScreen;
        Vector2 _dragScreen;
        bool _useWeapon;
        bool _directPlacement;
        float _twangMax = CrewCatalog.TwangMaxForce;
        float _weight = CrewCatalog.Weight;

        /// <summary>是否正在拖拽瞄准（供 TurnManager 的 inactivity 判定）。</summary>
        public bool IsAiming => _phase == Phase.Dragging;

        /// <summary>当前选中角色。</summary>
        public PirateBase SelectedCharacter => _selected;

        /// <summary>当前拟使用的武器（false = 抛自己）。</summary>
        public bool UseWeapon => _useWeapon;

        /// <summary>
        /// 该武器是否走「点击直接放置」而非弹弓拖拽（<b>提案/待定</b>，见 <see cref="IsDirectPlacementWeapon"/>）。
        /// 供 UI/测试查询当前瞄准模式。
        /// </summary>
        public bool IsDirectPlacement => _directPlacement;

        /// <summary>
        /// 四把非弹道机制武器：anchor / seagull / tidalWave / cannon（§5.2「触发/引爆条件」列）。
        /// 它们不是弹弓打出的抛物线弹体，弹弓拖拽预览对它们**没有物理意义**（预览 ≠ 实弹是既知缺陷），
        /// 故本工程对它们隐藏弹弓拖拽预览，改为点击直接放置（最小交互）。
        ///
        /// 【为何不含 voodooDoll / SweepingFlame】
        ///   · voodooDoll 原版就是「弹弓抛出木偶」（twangMax=20，§5.2），属于弹道武器，保持现状；
        ///   · SweepingFlame 由 rumBottle 落地生成，不在背包里直接使用。
        ///
        /// 【最小交互（提案/待定）】原版各有专属交互（锚点击落 / 海鸥点选高度再点投弹 /
        ///   潮汐点击引爆 / 加农炮拖尾部 pin 蓄力）。M2 统一取「点击 → 在点击处生成该武器」：
        ///   锚从点击处上方直落、海鸥从点击纵深飞入、潮汐以点击纵深横扫、加农炮在点击处摆位后
        ///   自动朝最近敌人发射。原版的细粒度交互留待后续（见报告「提案与遗留」）。
        /// </summary>
        public static bool IsDirectPlacementWeapon(WeaponId weapon)
        {
            switch (ProjectileProfile.MechanicFor(weapon))
            {
                case ProjectileMechanic.AnchorDrop:
                case ProjectileMechanic.SeagullFlight:
                case ProjectileMechanic.TidalWaveSweep:
                case ProjectileMechanic.CannonPlacement:
                    return true;
                default:
                    return false;
            }
        }

        void Awake()
        {
            if (battleCamera == null)
                battleCamera = Camera.main;
        }

        /// <summary>观察模式下关闭常规游戏输入（拖拽/取消/炮台/悬停）；准星点选走 <see cref="HandleObserveClick"/>。</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>
        /// 【观察模式】屏幕中心准星点选：命中本队存活角色则选中并返回 true（HUD 据此自动退出观察），
        /// 未命中返回 false（留在观察）。左键点击本身不做任何其他事。
        /// </summary>
        public bool HandleObserveClick()
        {
            if (battle == null)
                return false;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            PirateBase picked = PickTeamCharacter(center);
            if (picked == null)
                return false;
            battle.SelectCharacter(picked);
            return true;
        }

        void Update()
        {
            if (battle == null || battleCamera == null)
                return;

            if (!InputEnabled)
            {
                SetHoverTarget(null);
                return;
            }

            // HUD 点击拦截：鼠标悬在 UGUI 上时不要开始新的选中/瞄准操作。
            // 只拦「按下」——已经开始拖拽后光标掠过面板仍应继续拖，避免手感断裂。
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
                OnPrimaryDown();

            // 【r12 按下归属权契约】松手结算：空白按下若没拖动（<6px，相机没消费它）= 取消选择；
            // 拖动过 = 相机拿去转视角了，不取消。按下瞬间不再立刻 CancelAim（旧口径与空白拖拽转视角打架）。
            if (Input.GetMouseButtonUp(0))
            {
                PressStartedOnUnit = false;
                if (_emptyPressPending)
                {
                    _emptyPressPending = false;
                    if (_phase == Phase.CharacterSelected && !IsTurretAiming
                        && Vector2.Distance(Input.mousePosition, _emptyPressScreen) < 6f)
                        CancelAim();
                }
            }

            if (_phase == Phase.Dragging)
            {
                if (Input.GetMouseButton(0))
                    UpdateDrag();
                if (Input.GetMouseButtonUp(0))
                    ReleaseDrag();
            }

            // 【炮台模式】操作模式 + 已武装武器 + 已选角色时，键盘瞄准（AD/WS/滚轮/空格）。
            UpdateTurret();

            if (Input.GetKeyDown(KeyCode.Escape))
                CancelAim();

            UpdateHover();
        }

        void OnDisable()
        {
            SetHoverTarget(null);
        }

        /// <summary>
        /// §4.5 悬停反馈：鼠标 30px 内最近的本队存活角色高亮（原版 <c>Controller.hoverCharacter</c>）。
        /// 拖拽期间与鼠标位于 UGUI 之上时不悬停——原版拖拽时 <c>corners</c> 也是隐藏的。
        /// </summary>
        void UpdateHover()
        {
            PirateBase target = null;
            if (_phase != Phase.Dragging && !IsPointerOverUi())
                target = PickTeamCharacter(Input.mousePosition);

            SetHoverTarget(target);
        }

        void SetHoverTarget(PirateBase target)
        {
            if (target == _hovered)
                return;

            if (_hovered != null)
                _hovered.SetHover(false);
            _hovered = target;
            if (_hovered != null)
                _hovered.SetHover(true);
        }

        /// <summary>
        /// 鼠标是否悬停在 UGUI 控件上。场景里没有 EventSystem 时视为不在 UI 上（不阻塞操作）。
        /// </summary>
        static bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        // ------------------------------------------------------------------
        // 阶段 A / C
        // ------------------------------------------------------------------

        void OnPrimaryDown()
        {
            Vector2 mouse = Input.mousePosition;

            if (_phase == Phase.Idle)
            {
                PirateBase picked = PickTeamCharacter(mouse);
                PressStartedOnUnit = picked != null;
                if (picked != null)
                    battle.SelectCharacter(picked);
                return;
            }

            if (_phase == Phase.CharacterSelected)
            {
                // 直接放置类武器（锚/海鸥/潮汐/加农）：点击即生成，不走拖拽、不显示弹弓预览。
                if (_useWeapon && _directPlacement)
                {
                    PressStartedOnUnit = true;
                    PlaceDirectWeapon();
                    return;
                }

                PirateBase picked = PickTeamCharacter(mouse);
                if (picked != null && picked != _selected)
                {
                    // 点到别的本队角色：切换选中（角色中心随切）。
                    PressStartedOnUnit = true;
                    battle.SelectCharacter(picked);
                    return;
                }

                // 【r12 用户裁决】其余一切左键按下（含点在当前角色身上、点空白）
                // 都交给相机做"环绕角色转视角"——跳跃瞄准 = 转视角 + AD/滚轮，不再弹弓拖拽。
                // 点空白仍是"延迟取消"（松手没拖动才取消选择）。
                PressStartedOnUnit = false;
                if (picked == null && !IsTurretAiming)
                {
                    _emptyPressPending = true;
                    _emptyPressScreen = mouse;
                }
            }
        }

        // ------------------------------------------------------------------
        // 按下归属权（r12：相机据此让位，帧序无关）
        // ------------------------------------------------------------------

        /// <summary>本次左键按下是否落在单位交互上（选中/切换/瞄准/放置）。相机据此决定是否消费为环绕。</summary>
        public bool PressStartedOnUnit { get; private set; }
        bool _emptyPressPending;
        Vector2 _emptyPressScreen;

        // ------------------------------------------------------------------
        // 炮台模式（r12 用户裁决：操作模式=像发射大炮一样瞄准）
        // ------------------------------------------------------------------

        // 瞄准手感常量（原 r12 数值原样收进常量区，禁散落魔法数）。
        /// <summary>A/D 转向速率（弧度/秒）。</summary>
        const float TurretYawRadiansPerSecond = 1.6f;
        /// <summary>W/S 力度增速（比例/秒）。</summary>
        const float TurretPowerPerSecond = 0.45f;
        /// <summary>滚轮力度微调（比例/格）。</summary>
        const float TurretPowerPerScrollNotch = 0.04f;

        bool _preferWeapon;
        bool _turretAiming;
        float _turretYawRad;
        float _turretPower = 0.6f;
        bool _scopeActive;

        /// <summary>炮台瞄准进行中（相机据此把滚轮让给力度）。</summary>
        public bool IsTurretAiming => _turretAiming;

        /// <summary>当前炮台蓄力比例（0..1；供相机的力度-镜头耦合，M4 §3.2）。</summary>
        public float ChargeRatio => Mathf.Clamp01(_turretPower);

        /// <summary>
        /// Scope 瞄准模式（M4 §3.2，提案）：炮台瞄准中按 Shift 切换。生效时相机 FOV 收敛到
        /// <see cref="CameraFeelRules.ScopeTargetFov"/>、瞄准灵敏度 ×<see cref="CameraFeelRules.ScopeAimSensitivityScale"/>。
        /// 退出炮台瞄准自动退出。
        /// </summary>
        public bool IsScopeActive => _scopeActive;

        /// <summary>HUD 模式开关调用：操作模式=优先用武器（拖空白不再取消武装），移动模式=拖拽即跳跃。</summary>
        public void SetWeaponPreference(bool preferWeapon)
        {
            _preferWeapon = preferWeapon;
            if (!preferWeapon && _phase != Phase.Dragging)
                _useWeapon = false;
        }

        /// <summary>
        /// 大炮式键盘瞄准：A/D 转向、W/S 力度、滚轮微调、空格/回车发射；轨迹预览实时刷新。
        /// 合成等效拖拽向量喂给 <see cref="ResolveThrow"/>——与拖拽路径共用同一套换算，口径不分叉。
        /// Scope（Shift 切换）下灵敏度整体 ×0.4，弹道预览步数随力度延长（M4 §3.2）。
        /// </summary>
        void UpdateTurret()
        {
            // 武器炮台：操作模式 + 已武装武器。
            bool weaponTurret = _preferWeapon && _useWeapon && _selected != null && _selected.Alive
                && _phase != Phase.Dragging;
            // 跳跃炮台：移动模式 + 已选角色（r12 用户裁决：跳跃=环绕角色转视角瞄准，不再弹弓）。
            bool jumpTurret = !_preferWeapon && _phase == Phase.CharacterSelected
                && _selected != null && _selected.Alive;

            _turretAiming = weaponTurret || jumpTurret;
            if (!_turretAiming)
            {
                // 退出瞄准即退出 Scope（相机侧混合目标随之回落）。
                _scopeActive = false;
                return;
            }

            // 【M4 §3.2】Scope：Shift 切换；生效时 yaw/力度/滚轮灵敏度同步 ×0.4。
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
                _scopeActive = !_scopeActive;
            float sensitivity = _scopeActive ? CameraFeelRules.ScopeAimSensitivityScale : 1f;

            if (_selected != null)
                _originWorld = _selected.transform.position;

            float dt = Time.deltaTime;
            float rot = (Input.GetKey(KeyCode.A) ? -1f : 0f) + (Input.GetKey(KeyCode.D) ? 1f : 0f);
            _turretYawRad += rot * TurretYawRadiansPerSecond * sensitivity * dt;
            float pow = (Input.GetKey(KeyCode.W) ? 1f : 0f) + (Input.GetKey(KeyCode.S) ? -1f : 0f);
            _turretPower = Mathf.Clamp01(_turretPower + pow * TurretPowerPerSecond * sensitivity * dt
                + Input.mouseScrollDelta.y * TurretPowerPerScrollNotch * sensitivity);

            float dragLength = Mathf.Max(minDragPixels + 4f, _turretPower * _twangMax / 0.25f);
            _dragScreen = new Vector2(Mathf.Sin(_turretYawRad), Mathf.Cos(_turretYawRad)) * dragLength;

            (Vector3 dir, float speed, float _, float _) = ResolveThrow();
            if (trajectory != null)
            {
                // 弹道预览步数随力度延长（15 步保底 → 大力度长弧线）。
                trajectory.Show(_originWorld, dir, speed, _weight,
                    ThrowTrajectory.StepsForSpeed(speed, _twangMax));
            }

            // 开火：武器=回车（防走火）；跳跃=空格或回车。
            bool fire = Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Return)
                || (jumpTurret && Input.GetKeyDown(KeyCode.Space));
            if (fire)
                ReleaseDrag();
        }

        /// <summary>
        /// 直接放置一次（<see cref="IsDirectPlacementWeapon"/> 的四把武器）：把鼠标点投到地面作为
        /// 生成点，生成弹体并消耗本次武器（用武器即结束回合，§3.4）。
        /// 加农炮额外立刻朝最近敌人发射一发（最小交互，提案/待定）。
        /// </summary>
        void PlaceDirectWeapon()
        {
            PirateBase pirate = _selected;
            if (pirate == null || !pirate.Alive)
            {
                CancelAim();
                return;
            }

            Vector3 aimWorld = ResolveAimPointWorld();

            if (pirate.MarkUseWeapon(out WeaponId used))
            {
                int spawned = SpawnWeapon(pirate, used, aimWorld, 0f, 0f);
                if (spawned > 0 && used == WeaponId.Cannon)
                    TryFirePlacedCannon();

                EventBus_PublishAction(pirate, BattleActionKind.UseWeapon);
            }
            else
            {
                EventBus_PublishAction(pirate, BattleActionKind.UseWeapon);
            }

            EndDirectPlacement();
        }

        /// <summary>加农炮摆位后立刻朝最近敌人发射（提案/待定：原版需玩家拖尾部 pin 蓄力）。</summary>
        void TryFirePlacedCannon()
        {
            if (battle == null)
                return;

            WeaponProjectile cannon = null;
            IReadOnlyList<WeaponProjectile> projectiles = battle.AllProjectiles;
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                WeaponProjectile p = projectiles[i];
                if (p != null && p.WeaponId == WeaponId.Cannon)
                {
                    cannon = p;
                    break;
                }
            }

            if (cannon == null)
                return;

            PirateBase target = FindNearestEnemy();
            if (target != null)
                cannon.FireCannonToward(target.transform.position);
        }

        PirateBase FindNearestEnemy()
        {
            if (battle == null || _selected == null)
                return null;

            PirateBase best = null;
            float bestDistance = float.MaxValue;
            IReadOnlyList<PirateBase> all = battle.AllPirates;
            for (int i = 0; i < all.Count; i++)
            {
                PirateBase p = all[i];
                if (p == null || !p.Alive || p.TeamIndex == _selected.TeamIndex)
                    continue;

                float d = Vector3.Distance(_selected.transform.position, p.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }

            return best;
        }

        void EndDirectPlacement()
        {
            _phase = Phase.Idle;
            _selected = null;
            _useWeapon = false;
            _directPlacement = false;
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
            if (trajectory != null)
                trajectory.Hide();
        }

        void BeginDrag()
        {
            if (_selected == null || !_selected.Alive)
            {
                CancelAim();
                return;
            }

            // 直接放置类武器不用拖拽（点击即放置）；防御性拦截，避免状态机异常时误入拖拽。
            if (_useWeapon && _directPlacement)
                return;

            _originWorld = _selected.transform.position;
            _dragStartScreen = Input.mousePosition;
            _dragScreen = Vector2.zero;
            _phase = Phase.Dragging;
            UpdateDrag();
        }

        void UpdateDrag()
        {
            if (_selected == null || !_selected.Alive)
            {
                CancelAim();
                return;
            }

            _dragScreen = (Vector2)Input.mousePosition - _dragStartScreen;
            (Vector3 dir, float speed, float _, float _) = ResolveThrow();

            if (trajectory != null)
            {
                // 弹道预览步数随力度延长（与炮台路径同口径，M4 §3.2）。
                trajectory.Show(_originWorld, dir, speed, _weight,
                    ThrowTrajectory.StepsForSpeed(speed, _twangMax));
            }
        }

        /// <summary>
        /// 屏幕拖拽 → (XZ 世界水平方向, Flash 初速大小)。
        /// <b>方向</b>按相机基向量投影（§M2-3D 规范 §3），yaw = 0 时等价于 Godot 的 (-dx, 0, -dy)，
        /// 相机绕转后仍正确；<b>大小</b>取 <see cref="Ballistics.TwangVelocity"/> 的模长，
        /// 保留 Flash 的 0.25 系数与 twangMax 限速语义。抬升由 LevelGeometry.ThrowVelocity 统一施加。
        /// </summary>
        (Vector3 dir, float speed, float vxFlash, float vyFlash) ResolveThrow()
        {
            (float vx, float vy) = Ballistics.TwangVelocity(_dragScreen.x, _dragScreen.y, _twangMax);
            float speed = Mathf.Sqrt(vx * vx + vy * vy);

            Transform cam = battleCamera != null ? battleCamera.transform : null;
            Vector3 dir = cam != null
                ? LevelGeometry.ScreenDragToArenaDirection(cam.right, cam.forward, _dragScreen.x, _dragScreen.y)
                : Vector3.zero;

            // 【r12 实测修"跳跃方向和预览相反"】实弹换算（FlashLaunchVelocityToWorld）是世界轴直映射，
            // 相机被环绕/跟随偏转后与预览（相机相对方向）分叉甚至相反。弹弓取反语义由
            // ScreenDragToArenaDirection 统一承载，这里把方向转成世界轴的 Flash 分量供下游换算。
            return (dir, speed, dir.x * speed, dir.z * speed);
        }

        void ReleaseDrag()
        {
            if (_selected == null || !_selected.Alive)
            {
                CancelAim();
                return;
            }

            float dragDistance = _dragScreen.magnitude;
            if (dragDistance < minDragPixels)
            {
                // 误触：不发射，回到"已选角色"状态（对应 §3.4 松手阈值）。
                _phase = Phase.CharacterSelected;
                if (trajectory != null)
                    trajectory.Hide();
                return;
            }

            (Vector3 _, float _, float vx, float vy) = ResolveThrow();

            PirateBase pirate = _selected;
            Vector3 aimWorld = ResolveAimPointWorld();

            // §3.4：抛自己 与 用武器 二选一——只有抛自己才给角色初速，
            // 用武器则把初速给武器弹体（此前代码在分支前无条件抛角色，属违规，已修正）。
            if (_useWeapon)
            {
                if (pirate.MarkUseWeapon(out WeaponId used))
                {
                    SpawnWeapon(pirate, used, aimWorld, vx, vy);
                    EventBus_PublishAction(pirate, BattleActionKind.UseWeapon);
                }
                else
                {
                    EventBus_PublishAction(pirate, BattleActionKind.UseWeapon);
                }
            }
            else
            {
                pirate.ApplyLaunchVelocity(vx, vy);
                pirate.MarkThrowSelf();
                EventBus_PublishAction(pirate, BattleActionKind.ThrowSelf);
            }

            EventBus_PublishShot(dragDistance);

            if (trajectory != null)
                trajectory.Hide();

            _phase = Phase.Idle;
            _selected = null;
            _useWeapon = false;
            _directPlacement = false;
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
        }

        /// <summary>
        /// 生成武器弹体（§5.2）。放置类生成 N 个、常驻；其余生成 1 个并给初速。
        /// 特殊机制（anchor/seagull/tidalWave/voodooDoll/cannon/SweepingFlame）的生成计划由
        /// <see cref="ProjectileSpawnPlanner.PlanSpecial"/> 给出，返回 0 时给出明确警告而非静默失败。
        /// </summary>
        /// <returns>本次生成的弹体数量。</returns>
        int SpawnWeapon(PirateBase pirate, WeaponId weapon, Vector3 aimWorld, float vx, float vy)
        {
            if (battle == null)
                return 0;

            WeaponStats stats = WeaponCatalog.Get(weapon);
            int spawned = battle.SpawnWeaponProjectiles(
                stats, pirate, pirate.transform.position, aimWorld, vx, vy);

            if (spawned == 0)
            {
                global::PirateCrew.Core.Log.Warn("[AimThrowController] 武器 " + weapon
                    + " 的专用机制尚未实现（TODO 见 ProjectileProfile.SupportsGenericProjectile），本次未生成弹体。");
            }

            return spawned;
        }

        // ------------------------------------------------------------------
        // 对外 API（HUD / 测试用）
        // ------------------------------------------------------------------

        /// <summary>由 BattleController 在选中角色后调用，进入阶段 C 准备。</summary>
        public void ResetForSelection(PirateBase pirate)
        {
            _selected = pirate;
            _phase = pirate != null ? Phase.CharacterSelected : Phase.Idle;
            _useWeapon = false;
            _directPlacement = false;
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
            if (_selected != null)
                _selected.Inventory.Unequip();
        }

        /// <summary>
        /// 选择武器（§3.4 阶段 B 的 <c>button_&lt;weaponId&gt;</c>）：
        /// 装备该武器并把 twangMax/weight 切到武器数值（<see cref="WeaponCatalog"/> 单一来源）。
        /// 直接放置类（<see cref="IsDirectPlacementWeapon"/>）不进入拖拽模式，并隐藏弹弓预览。
        /// </summary>
        public bool SelectWeapon(WeaponId weapon)
        {
            if (_selected == null || !_selected.Alive)
                return false;

            WeaponInventory inventory = _selected.Inventory;
            int index = inventory.FirstIndexOf(weapon);
            if (index < 0)
                return false;

            inventory.Equip(index);
            _useWeapon = true;
            _directPlacement = IsDirectPlacementWeapon(weapon);

            WeaponStats stats = WeaponCatalog.Get(weapon);
            _twangMax = stats.TwangMax > 0f ? stats.TwangMax : CrewCatalog.TwangMaxForce;
            _weight = stats.Weight;

            // 非弹道武器不显示弹弓拖拽预览（预览对它们没有物理意义）。
            if (_directPlacement && trajectory != null)
                trajectory.Hide();

            return true;
        }

        /// <summary>选择"抛自己"（§3.4 阶段 B 的 <c>button_throw</c>）。</summary>
        public void SelectThrowSelf()
        {
            _useWeapon = false;
            _directPlacement = false;
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
            if (_selected != null)
                _selected.Inventory.Unequip();
        }

        /// <summary>"end go"：直接结束当前角色回合（§3.4 <c>button_endTurn</c>）。</summary>
        public bool EndGo()
        {
            if (_selected == null)
                return false;

            _selected.MarkEndGo();
            EventBus_PublishAction(_selected, BattleActionKind.EndGo);
            _phase = Phase.Idle;
            _selected = null;
            _useWeapon = false;
            if (trajectory != null)
                trajectory.Hide();
            return true;
        }

        /// <summary>取消瞄准。</summary>
        public void CancelAim()
        {
            _phase = Phase.Idle;
            _selected = null;
            _useWeapon = false;
            _directPlacement = false;
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
            if (trajectory != null)
                trajectory.Hide();
        }

        // ------------------------------------------------------------------
        // 内部工具
        // ------------------------------------------------------------------

        /// <summary>屏幕空间 30px 内最近的本队存活角色（§3.4 <c>minD2 = 900</c>）。</summary>
        PirateBase PickTeamCharacter(Vector2 screenPosition)
        {
            BattleTeam team = battle.CurrentTeam;
            if (team == null)
                return null;

            PirateBase best = null;
            float bestSqr = LevelGeometry.SelectionRadiusPixels * LevelGeometry.SelectionRadiusPixels;

            Vector3 mouse = new Vector3(screenPosition.x, screenPosition.y, 0f);
            for (int i = 0; i < team.Characters.Count; i++)
            {
                PirateBase c = team.Characters[i];
                if (c == null || !c.Alive)
                    continue;

                Vector3 screen = battleCamera.WorldToScreenPoint(c.transform.position);
                if (screen.z < 0f)
                    continue;   // 相机背后

                float sqr = (new Vector2(screen.x, screen.y) - screenPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>
        /// 鼠标位置 → **地面**（y = <see cref="LevelGeometry.GroundTopY"/>）上的世界点，
        /// 供放置类武器的铺开中心使用。优先物理射线（地面层），落空回退数学平面交点。
        /// 3D 化后地面是 XZ 水平面，故平面法线取 +Y（原先是 +Z 的 XY 竖直平面）。
        /// </summary>
        Vector3 ResolveAimPointWorld()
        {
            Ray ray = battleCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, aimPlaneMask, QueryTriggerInteraction.Ignore))
                return hit.point;

            var plane = new Plane(Vector3.up, new Vector3(0f, LevelGeometry.GroundTopY, 0f));
            if (plane.Raycast(ray, out float distance))
                return ray.GetPoint(distance);

            return _originWorld;
        }

        void EventBus_PublishAction(PirateBase pirate, BattleActionKind kind)
        {
            if (pirate == null)
                return;

            // battle.NotifyActionSelected 内部会发 action_selected 并清零 inactivity；
            // 未接线时退化为直接发布，避免事件丢失。
            if (battle != null)
                battle.NotifyActionSelected(pirate, kind);
            else
                Core.EventBus.Publish(BattleEvents.ActionSelected, new ActionSelectedPayload(
                    pirate.PirateId, pirate.TeamIndex, kind));
        }

        void EventBus_PublishShot(float dragDistance)
        {
            Core.EventBus.Publish(BattleEvents.ShotReleased, dragDistance);
        }
    }
}
