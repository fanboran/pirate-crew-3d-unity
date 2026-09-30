using System.Collections.Generic;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 瞄准 / 投掷控制器。
    ///
    /// 【对应章节】§3.4（严格两阶段：点选己方角色 → 选动作/武器 → 瞄准发射）、
    ///             §5.1（初速 = 0.25 × 拖拽距离、twangMax 限速）。
    ///
    /// 【交互形态（创始人 2026-09-24 裁决：炮台为唯一瞄准交互）】
    ///   鼠标弹弓拖拽路径已整体移除（r12 起即不可达的死路径），瞄准只有炮台一种：
    ///   A/D 转向、W/S 力度、滚轮微调、空格/回车发射（见 <see cref="UpdateTurret"/>）。
    ///   可感知行为规格见 docs/技术/投掷行为契约.md（单一事实源）。
    ///
    /// 【流程】
    ///   1. 点选：屏幕空间 30px 内最近的本队存活角色（对应 §3.4 <c>minD2 = 900</c>）。
    ///   2. 瞄准：炮台合成等效拖拽向量（转向 × 力度），喂给 <see cref="ResolveThrow"/>。
    ///   3. 发射：<see cref="Ballistics.TwangVelocity"/> 算初速，经 <see cref="LevelGeometry"/> 换算后
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
        }

        [Header("组装引用（场景内直连）")]
        [SerializeField] Camera battleCamera;
        [SerializeField] BattleController battle;
        [SerializeField] TrajectoryPreview trajectory;

        [Header("命中层")]
        [SerializeField] LayerMask unitMask = ~0;
        [SerializeField] LayerMask aimPlaneMask = ~0;

        [Header("参数")]
        [SerializeField] float maxRayDistance = 500f;

        Phase _phase = Phase.Idle;
        PirateBase _selected;
        PirateBase _hovered;
        Vector3 _originWorld;
        Vector2 _dragScreen;
        bool _useWeapon;
        // 投掷物理口径：选中任意武器后统一取标准炸弹口径（StandardBombRules），
        // 抛自己回落角色口径——「点哪把都是它」在瞄准手感层同样成立。
        float _twangMax = CrewCatalog.TwangMaxForce;
        float _weight = CrewCatalog.Weight;

        /// <summary>是否正在炮台瞄准（供 TurnManager 的 inactivity 判定：瞄准中回合不推进）。</summary>
        public bool IsAiming => _turretAiming;

        /// <summary>当前选中角色。</summary>
        public PirateBase SelectedCharacter => _selected;

        /// <summary>当前拟使用的武器（false = 抛自己）。</summary>
        public bool UseWeapon => _useWeapon;

        void Awake()
        {
            if (battleCamera == null)
                battleCamera = Camera.main;
        }

        /// <summary>观察模式下关闭常规游戏输入（拖拽/取消/炮台/悬停）。</summary>
        public bool InputEnabled { get; set; } = true;

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

            // 【炮台模式】操作模式 + 已武装武器 + 已选角色时，键盘瞄准（AD/WS/滚轮/空格）。
            UpdateTurret();

            if (Input.GetKeyDown(KeyCode.Escape))
                CancelAim();

            UpdateHover();
        }

        void OnDisable()
        {
            SetHoverTarget(null);
            // 与 ESC 走同一条 CancelAim 清理路径（全部是幂等赋值 + trajectory.Hide 可重复调用）：
            // 组件失活时不再持有瞄准/拖拽状态，防止重新激活后残留上一次的选中与武装。
            CancelAim();
        }

        /// <summary>
        /// §4.5 悬停反馈：鼠标 30px 内最近的本队存活角色高亮（原版 <c>Controller.hoverCharacter</c>）。
        /// 拖拽期间与鼠标位于 UGUI 之上时不悬停——原版拖拽时 <c>corners</c> 也是隐藏的。
        /// </summary>
        void UpdateHover()
        {
            PirateBase target = null;
            if (!IsPointerOverUi())
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
        /// <summary>炮台虚拟拖拽的下限（px）：力度为 0 时仍保留一个最小初速，避免零向量发射。</summary>
        const float TurretMinDragPixels = 5f;

        bool _preferWeapon;
        bool _turretAiming;
        float _turretYawRad;
        float _turretPower = 0.6f;
        bool _scopeActive;

        /// <summary>炮台瞄准进行中（相机据此把滚轮让给力度；缩放已锁死，瞄准态滚轮不触碰相机）。</summary>
        public bool IsTurretAiming => _turretAiming;

        /// <summary>
        /// Scope 瞄准模式（M4 §3.2，提案）：炮台瞄准中按 Shift 切换。生效时相机 FOV 收敛到
        /// <see cref="CameraFeelRules.ScopeTargetFov"/>、瞄准灵敏度 ×<see cref="CameraFeelRules.ScopeAimSensitivityScale"/>。
        /// 退出炮台瞄准自动退出。
        /// </summary>
        public bool IsScopeActive => _scopeActive;

        /// <summary>HUD 模式开关调用：操作模式=优先用武器（拖空白不再取消武装），移动模式=键盘瞄准跳跃。</summary>
        public void SetWeaponPreference(bool preferWeapon)
        {
            _preferWeapon = preferWeapon;
            if (!preferWeapon)
                _useWeapon = false;
        }

        /// <summary>
        /// 大炮式键盘瞄准：A/D 转向、W/S 力度、滚轮微调、空格/回车发射；轨迹预览实时刷新。
        /// 合成等效拖拽向量喂给 <see cref="ResolveThrow"/>——初速换算的唯一路径，口径不分叉。
        /// Scope（Shift 切换）下灵敏度整体 ×0.4，弹道预览步数随力度延长（M4 §3.2）。
        /// </summary>
        void UpdateTurret()
        {
            // 武器炮台：操作模式 + 已武装武器。
            bool weaponTurret = _preferWeapon && _useWeapon && _selected != null && _selected.Alive;
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

            // 满力拖拽距离 = twangMax / 力度系数（§5.1 的 0.25）——直接复用 Ballistics 的
            // 同一换算（FullForceDragDistance），不手抄 0.25f，系数改动时两处不脱钩。
            float dragLength = Mathf.Max(TurretMinDragPixels,
                _turretPower * Ballistics.FullForceDragDistance(_twangMax));
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
        /// （炮台合成的等效）拖拽向量 → (XZ 世界水平方向, Flash 初速大小)。
        /// <b>方向</b>按相机基向量投影（§M2-3D 规范 §3），
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

        /// <summary>
        /// 发射（炮台开火键触发；无鼠标释放路径）。把初速给角色（抛自己）或武器弹体（§3.4 二选一）。
        /// </summary>
        void ReleaseDrag()
        {
            if (_selected == null || !_selected.Alive)
            {
                CancelAim();
                return;
            }

            float dragDistance = _dragScreen.magnitude;

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
                    // 【提案/待定】MarkUseWeapon 失败（库存空/复用尽）仍发布 UseWeapon 动作，
                    // 与成功分支同构——回合语义需创始人裁决：失败分支应当发 EndGo 结束回合，
                    // 还是不发动作让玩家重选（现行为=回合推进器把失败当作"用过武器"）。
                    // 先保持现状不改行为。
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
            ResetAimState();
        }

        /// <summary>
        /// 生成武器弹体：所有武器都走通用抛掷（<see cref="BattleController.SpawnWeaponProjectiles"/>），
        /// 生成 1 颗标准炸弹并带弹弓初速。
        /// </summary>
        /// <returns>本次生成的弹体数量。</returns>
        int SpawnWeapon(PirateBase pirate, WeaponId weapon, Vector3 aimWorld, float vx, float vy)
        {
            if (battle == null)
                return 0;

            WeaponStats stats = WeaponCatalog.Get(weapon);
            return battle.SpawnWeaponProjectiles(
                stats, pirate, pirate.transform.position, aimWorld, vx, vy);
        }

        // ------------------------------------------------------------------
        // 对外 API（HUD / 测试用）
        // ------------------------------------------------------------------

        /// <summary>由 BattleController 在选中角色后调用，进入阶段 C 准备。</summary>
        public void ResetForSelection(PirateBase pirate)
        {
            _selected = pirate;
            _phase = pirate != null ? Phase.CharacterSelected : Phase.Idle;
            ResetAimState();
            if (_selected != null)
                _selected.Inventory.Unequip();
        }

        /// <summary>
        /// 选择武器（§3.4 阶段 B 的 <c>button_&lt;weaponId&gt;</c>）：
        /// 装备该武器（外壳保留），投掷口径统一取标准炸弹
        /// （<see cref="StandardBombRules"/>：twangMax/weight 不按武器分叉）。
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

            _twangMax = StandardBombRules.TwangMax;
            _weight = StandardBombRules.Weight;

            return true;
        }

        /// <summary>选择"抛自己"（§3.4 阶段 B 的 <c>button_throw</c>）。</summary>
        public void SelectThrowSelf()
        {
            ResetAimState();
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

        /// <summary>取消瞄准（幂等，可重复调用；<see cref="OnDisable"/> 也走这里收尾）。</summary>
        public void CancelAim()
        {
            _phase = Phase.Idle;
            _selected = null;
            ResetAimState();
            if (trajectory != null)
                trajectory.Hide();
        }

        // ------------------------------------------------------------------
        // 内部工具
        // ------------------------------------------------------------------

        /// <summary>
        /// 状态重置的公共块（原先在 ReleaseDrag / ResetForSelection / CancelAim 各抄一份）：
        /// 清武装标记，twangMax/weight 回落到角色口径。
        /// 阶段与选中角色的置位属于各自语义，由调用方另写。
        /// </summary>
        void ResetAimState()
        {
            _useWeapon = false;
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
        }

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

            // battle.NotifyActionSelected 内部会发布 ActionSelected 频道并清零 inactivity；
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
