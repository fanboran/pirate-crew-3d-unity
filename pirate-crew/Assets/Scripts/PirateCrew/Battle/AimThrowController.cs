using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
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
        [Tooltip("战斗平面世界 z（与 LevelGeometry.DefaultPlaneZ 一致）。")]
        [SerializeField] float planeZ = LevelGeometry.DefaultPlaneZ;

        Phase _phase = Phase.Idle;
        PirateBase _selected;
        Vector2 _originPixels;
        Vector2 _dragPixels;
        bool _useWeapon;
        float _twangMax = CrewCatalog.TwangMaxForce;
        float _weight = CrewCatalog.Weight;

        /// <summary>是否正在拖拽瞄准（供 TurnManager 的 inactivity 判定）。</summary>
        public bool IsAiming => _phase == Phase.Dragging;

        /// <summary>当前选中角色。</summary>
        public PirateBase SelectedCharacter => _selected;

        /// <summary>当前拟使用的武器（false = 抛自己）。</summary>
        public bool UseWeapon => _useWeapon;

        void Awake()
        {
            if (battleCamera == null)
                battleCamera = Camera.main;
        }

        void Update()
        {
            if (battle == null || battleCamera == null)
                return;

            if (Input.GetMouseButtonDown(0))
                OnPrimaryDown();

            if (_phase == Phase.Dragging)
            {
                if (Input.GetMouseButton(0))
                    UpdateDrag();
                if (Input.GetMouseButtonUp(0))
                    ReleaseDrag();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                CancelAim();
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
                if (picked != null)
                    battle.SelectCharacter(picked);
                return;
            }

            if (_phase == Phase.CharacterSelected)
            {
                PirateBase picked = PickTeamCharacter(mouse);
                if (picked == null)
                {
                    // 点空处：取消选择。
                    CancelAim();
                    return;
                }

                if (picked != _selected)
                    battle.SelectCharacter(picked);

                BeginDrag();
            }
        }

        void BeginDrag()
        {
            if (_selected == null || !_selected.Alive)
            {
                CancelAim();
                return;
            }

            _originPixels = LevelGeometry.WorldToPixel(_selected.transform.position);
            _dragPixels = _originPixels;
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

            Vector2 targetPixels = ResolveAimPointPixels();
            _dragPixels = targetPixels;

            (float vx, float vy) = Ballistics.TwangVelocity(
                targetPixels.x - _originPixels.x,
                targetPixels.y - _originPixels.y,
                _twangMax);

            if (trajectory != null)
                trajectory.Show(_originPixels.x, _originPixels.y, vx, vy, _weight);
        }

        void ReleaseDrag()
        {
            if (_selected == null || !_selected.Alive)
            {
                CancelAim();
                return;
            }

            float dragDistance = (_dragPixels - _originPixels).magnitude;
            if (dragDistance < minDragPixels)
            {
                // 误触：不发射，回到"已选角色"状态（对应 §3.4 松手阈值）。
                _phase = Phase.CharacterSelected;
                if (trajectory != null)
                    trajectory.Hide();
                return;
            }

            (float vx, float vy) = Ballistics.TwangVelocity(
                _dragPixels.x - _originPixels.x,
                _dragPixels.y - _originPixels.y,
                _twangMax);

            PirateBase pirate = _selected;
            pirate.ApplyLaunchVelocity(vx, vy);

            if (_useWeapon)
            {
                if (pirate.MarkUseWeapon(out WeaponId used))
                {
                    EventBus_PublishAction(pirate, BattleActionKind.UseWeapon);
                    Debug.Log("[AimThrowController] 使用武器: " + used);
                }
                else
                {
                    EventBus_PublishAction(pirate, BattleActionKind.UseWeapon);
                }
            }
            else
            {
                pirate.MarkThrowSelf();
                EventBus_PublishAction(pirate, BattleActionKind.ThrowSelf);
            }

            EventBus_PublishShot(dragDistance);

            if (trajectory != null)
                trajectory.Hide();

            _phase = Phase.Idle;
            _selected = null;
            _useWeapon = false;
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
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
            _twangMax = CrewCatalog.TwangMaxForce;
            _weight = CrewCatalog.Weight;
            if (_selected != null)
                _selected.Inventory.Unequip();
        }

        /// <summary>
        /// 选择武器（§3.4 阶段 B 的 <c>button_&lt;weaponId&gt;</c>）：
        /// 装备该武器并把 twangMax/weight 切到武器数值（<see cref="WeaponCatalog"/> 单一来源）。
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

            WeaponStats stats = WeaponCatalog.Get(weapon);
            _twangMax = stats.TwangMax > 0f ? stats.TwangMax : CrewCatalog.TwangMaxForce;
            _weight = stats.Weight;
            return true;
        }

        /// <summary>选择"抛自己"（§3.4 阶段 B 的 <c>button_throw</c>）。</summary>
        public void SelectThrowSelf()
        {
            _useWeapon = false;
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

        /// <summary>把鼠标位置解到 Flash 像素坐标：优先物理射线（地面层），落空回退战斗平面交点。</summary>
        Vector2 ResolveAimPointPixels()
        {
            Ray ray = battleCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, aimPlaneMask, QueryTriggerInteraction.Ignore))
                return LevelGeometry.WorldToPixel(hit.point);

            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, planeZ));
            if (plane.Raycast(ray, out float distance))
                return LevelGeometry.WorldToPixel(ray.GetPoint(distance));

            return _originPixels;
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
