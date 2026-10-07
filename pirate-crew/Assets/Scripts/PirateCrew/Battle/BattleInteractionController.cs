using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗交互编排薄壳（两态状态机的持有者）。
    ///
    /// 【职责】每帧采样一次 <see cref="BattleInputReader"/>（唯一输入采样点），把意图翻译成
    /// <see cref="InteractionTrigger"/> 交给纯规则 <see cref="InteractionRules"/> 迁移，然后应用结果：
    /// 点选/换人/取消（选中域）、操作模组的激活/瞄准/确认/取消（操作域）、执行落定分流（回合经济域）。
    /// 规格唯一出处 = docs/技术/交互操作契约.md；设计意图见 docs/设计/操作与交互.md。
    ///
    /// 【操作模组】现役 = 标准投掷（<see cref="StandardThrowRules"/> 三参数）；专用模组（轨道轰炸等）
    /// 按「新增模组规则类 + 此处分派」接入，不改状态机（交互操作契约 §F）。
    ///
    /// 【与相机】相机驱动拉取本控制器的 <see cref="State"/> / <see cref="SelectedTarget"/> /
    /// <see cref="LastIntent"/> 决定自身模式（FreeOrbit/Orbit/OpLock/Follow）——拉模型，无帧序依赖。
    ///
    /// 【挂机保护】操作中（<see cref="IsOperationActive"/>）计入 <c>BattleController.IsAnythingActive</c>，
    /// 回合推进器的 inactivity 清零（契约不变量 5）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleInteractionController : MonoBehaviour
    {
        [Header("组装引用（场景内直连）")]
        [SerializeField] Camera battleCamera;
        [SerializeField] BattleController battle;
        [SerializeField] BattleCameraDriver cameraDriver;
        [SerializeField] TrajectoryPreview trajectory;

        BattleInputReader _input;
        BattleIntentFrame _intent;

        InteractionState _state = InteractionState.FreeCamera;
        PirateBase _selected;
        PirateBase _hovered;

        // 操作模组状态（现役仅标准投掷；专用模组各持参数集）
        bool _opIsWeapon;
        WeaponId _opWeapon;
        ThrowParams _params;

        /// <summary>当前交互状态（相机驱动据此分派模式）。</summary>
        public InteractionState State => _state;

        /// <summary>当前选中角色（无则 null）。</summary>
        public PirateBase SelectedCharacter => _selected;

        /// <summary>选中单位的 Transform（相机环绕目标；无则 null）。</summary>
        public Transform SelectedTarget => _selected != null ? _selected.transform : null;

        /// <summary>操作模组是否激活（挂机保护口径：契约不变量 5）。</summary>
        public bool IsOperationActive => _state == InteractionState.OperationActive;

        /// <summary>标准投掷模组当前参数（HUD 读数用；非操作中为上次残留，消费方自查状态）。</summary>
        public ThrowParams CurrentParams => _params;

        /// <summary>本帧采样的意图帧（相机驱动在 LateUpdate 拉取——所有 Update 已跑完，无帧序竞态）。</summary>
        public BattleIntentFrame LastIntent => _intent;

        /// <summary>当前操作是否为武器操作（false = 跳跃/抛自己）。</summary>
        public bool OperationIsWeapon => _opIsWeapon;

        void Awake()
        {
            if (battleCamera == null)
                battleCamera = Camera.main;

            _input = GetComponent<BattleInputReader>();
            if (_input == null)
                _input = gameObject.AddComponent<BattleInputReader>();
        }

        void OnEnable()
        {
            EventBus.Subscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe(BattleEvents.TurnEnded, OnTurnEnded);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Unsubscribe(BattleEvents.TurnEnded, OnTurnEnded);
            ResetAll();
        }

        void Update()
        {
            if (battle == null || battleCamera == null)
                return;

            _intent = _input.Sample();

            // P = 全局暂停键（不经 Esc 矩阵；对局结束的守卫在 HUD 的 OpenPause 内）。
            if (_intent.PausePressed)
                TogglePause();

            // Esc 矩阵：唯一解析点（交互操作契约 §C）；UI 层动作经 BattleUiBridge 跨程序集门面。
            if (_intent.CancelPressed)
            {
                switch (InteractionRules.ResolveEsc(BattleUiBridge.ConfirmDialogOpen, BattlePause.IsPaused, _state))
                {
                    case EscAction.CloseDialog:
                        BattleUiBridge.RequestCloseDialog();
                        break;
                    case EscAction.ResumePause:
                        BattleUiBridge.RequestResumePause();
                        break;
                    case EscAction.CancelOperation:
                        CancelOperation();
                        break;
                    case EscAction.DeselectUnit:
                        Deselect();
                        break;
                    case EscAction.OpenPause:
                        BattleUiBridge.RequestOpenPause();
                        break;
                }
            }

            // 暂停中：除上面的 P/Esc 外冻结一切玩法输入。
            if (BattlePause.IsPaused)
            {
                SetHoverTarget(null);
                return;
            }

            switch (_state)
            {
                case InteractionState.FreeCamera:
                case InteractionState.SelectedIdle:
                    HandleSelectionInput();
                    break;
                case InteractionState.OperationActive:
                    HandleOperation();
                    break;
                case InteractionState.Executing:
                    HandleExecuting();
                    break;
            }

            UpdateHover();
        }

        // ------------------------------------------------------------------
        // 选中域（自由镜头 / 浏览）
        // ------------------------------------------------------------------

        /// <summary>左键点选（自由镜头与浏览共用：选中 / 换人；点空白无操作——取消只走 Esc，契约 §B）。</summary>
        void HandleSelectionInput()
        {
            if (!_intent.PrimaryPressed || IsPointerOverUi())
                return;

            PirateBase picked = PickTeamCharacter(Input.mousePosition);
            if (picked == null || picked == _selected)
                return;

            // 换人合法性由队伍规则把关（未行动可换、动作后锁定——TurnRules.CanSwitchSelection）。
            if (!battle.SelectCharacter(picked))
                return;

            int oldId = _selected != null ? _selected.PirateId : -1;
            bool wasSelected = _selected != null;
            if (wasSelected)
                _selected.Inventory.Unequip();

            _selected = picked;
            _state = InteractionRules.Advance(
                _state, wasSelected ? InteractionTrigger.SwitchUnit : InteractionTrigger.SelectUnit);

            if (wasSelected)
                EventBus.Publish(BattleEvents.SelectionChanged, new SelectionChangedPayload(oldId, false));
            EventBus.Publish(BattleEvents.SelectionChanged,
                new SelectionChangedPayload(picked.PirateId, true));
        }

        /// <summary>取消选中（Esc 矩阵 C4 / 回合推进清理）。</summary>
        void Deselect()
        {
            if (_selected == null)
                return;

            EventBus.Publish(BattleEvents.SelectionChanged,
                new SelectionChangedPayload(_selected.PirateId, false));
            _selected = null;
            _state = InteractionRules.Advance(_state, InteractionTrigger.Deselect);
            if (trajectory != null)
                trajectory.Hide();
        }

        // ------------------------------------------------------------------
        // 操作域（标准投掷模组）
        // ------------------------------------------------------------------

        /// <summary>
        /// HUD 入口：选择武器 → 进入该武器的标准投掷操作。
        /// 装备库存里的该武器并激活模组；未选中/已行动（CanShoot=false）/库存没有 → false。
        /// </summary>
        public bool SelectWeapon(WeaponId weapon)
        {
            if (_state != InteractionState.SelectedIdle || _selected == null || !_selected.Alive)
                return false;
            if (!_selected.CurrentAction.CanShoot)
                return false;

            WeaponInventory inventory = _selected.Inventory;
            int index = inventory.FirstIndexOf(weapon);
            if (index < 0)
                return false;

            inventory.Equip(index);
            _opIsWeapon = true;
            _opWeapon = weapon;
            BeginOperation();
            return true;
        }

        /// <summary>HUD 入口：选择"跳跃"（抛自己）→ 进入标准投掷操作。本回合已抛过 → false。</summary>
        public bool SelectThrowSelf()
        {
            if (_state != InteractionState.SelectedIdle || _selected == null || !_selected.Alive)
                return false;
            if (!_selected.CurrentAction.CanThrow)
                return false;

            _selected.Inventory.Unequip();
            _opIsWeapon = false;
            _opWeapon = (WeaponId)(-1);
            BeginOperation();
            return true;
        }

        void BeginOperation()
        {
            // 方向角以玩家正对方向起步（契约 §B10）：取相机当前朝向的世界方位角。
            float yaw = cameraDriver != null ? cameraDriver.FocusYawDegrees : 0f;
            _params = StandardThrowRules.Initial(yaw);
            _state = InteractionRules.Advance(_state, InteractionTrigger.StartOperation);
            EventBus.Publish(BattleEvents.OperationChanged, new OperationChangedPayload(
                _selected.PirateId, _opIsWeapon, _opWeapon, active: true));
        }

        /// <summary>取消操作回浏览（Esc 矩阵 C3；模组参数丢弃）。</summary>
        public void CancelOperation()
        {
            if (_state != InteractionState.OperationActive)
                return;

            _state = InteractionRules.Advance(_state, InteractionTrigger.CancelOperation);
            EventBus.Publish(BattleEvents.OperationChanged, new OperationChangedPayload(
                _selected.PirateId, _opIsWeapon, _opWeapon, active: false));
            if (trajectory != null)
                trajectory.Hide();
        }

        /// <summary>操作中：推进三参数、刷新预览、确认执行。</summary>
        void HandleOperation()
        {
            if (_selected == null || !_selected.Alive)
            {
                Deselect();
                return;
            }

            _params = StandardThrowRules.Advance(
                _params, _intent.ThrowYawInput, _intent.ThrowElevationInput, _intent.ThrowPowerInput,
                Time.deltaTime);

            Vector3 origin = StandardThrowRules.ThrowOrigin(_selected.transform.position);
            Vector3 velocity = StandardThrowRules.LaunchVelocity(_params);
            if (trajectory != null)
                trajectory.Show(origin, velocity);

            if (_intent.ConfirmPressed)
                Execute(origin, velocity);
        }

        /// <summary>确认执行（回车；屏幕确认钮走同一入口）——把初速给角色（抛自己）或生成弹体（用武器）。</summary>
        void Execute(Vector3 origin, Vector3 velocity)
        {
            PirateBase pirate = _selected;

            if (_opIsWeapon)
            {
                if (pirate.MarkUseWeapon(out WeaponId used))
                    SpawnWeapon(pirate, used, origin, velocity);
                // 【提案/待定】MarkUseWeapon 失败（库存空/复用尽）仍发布 UseWeapon——回合语义待裁决，
                // 保持与旧链一致的现状不改行为。
                PublishAction(pirate, BattleActionKind.UseWeapon);
            }
            else
            {
                pirate.ApplyLaunchVelocity(velocity);
                pirate.MarkThrowSelf();
                PublishAction(pirate, BattleActionKind.ThrowSelf);
            }

            EventBus.Publish(BattleEvents.ShotReleased, velocity.magnitude);
            if (trajectory != null)
                trajectory.Hide();

            _state = InteractionRules.Advance(_state, InteractionTrigger.ConfirmOperation);
            EventBus.Publish(BattleEvents.OperationChanged, new OperationChangedPayload(
                pirate.PirateId, _opIsWeapon, _opWeapon, active: false));
        }

        /// <summary>屏幕确认钮入口（HUD 调用；与回车同一条执行路径）。</summary>
        public void ConfirmOperationFromUi()
        {
            if (_state != InteractionState.OperationActive || _selected == null)
                return;

            Vector3 origin = StandardThrowRules.ThrowOrigin(_selected.transform.position);
            Execute(origin, StandardThrowRules.LaunchVelocity(_params));
        }

        /// <summary>生成武器弹体（通用抛掷，BattleController 的单一弹体路径）。</summary>
        void SpawnWeapon(PirateBase pirate, WeaponId weapon, Vector3 origin, Vector3 velocity)
        {
            if (battle == null)
                return;
            battle.SpawnWeaponProjectiles(WeaponCatalog.Get(weapon), pirate, origin, velocity);
        }

        // ------------------------------------------------------------------
        // 执行域（落定分流）
        // ------------------------------------------------------------------

        /// <summary>执行落定判定：全部活动静止后按回合经济分流（T7/T8）。</summary>
        void HandleExecuting()
        {
            if (battle.IsAnythingActive())
                return;

            bool canContinue = _selected != null && _selected.Alive
                && battle.CurrentTeam != null
                && !battle.CurrentTeam.AiControlled
                && battle.CurrentTeam.SelectedCharacter == _selected
                && _selected.CurrentAction.CanAct;

            if (canContinue)
            {
                _state = InteractionRules.Advance(_state, InteractionTrigger.SettledCanAct);
                // 回到浏览：重新宣告选中（HUD 借此重开操作菜单）。
                EventBus.Publish(BattleEvents.SelectionChanged,
                    new SelectionChangedPayload(_selected.PirateId, true));
            }
            else
            {
                if (_selected != null)
                    EventBus.Publish(BattleEvents.SelectionChanged,
                        new SelectionChangedPayload(_selected.PirateId, false));
                _selected = null;
                _state = InteractionRules.Advance(_state, InteractionTrigger.SettledDone);
            }
        }

        /// <summary>HUD 入口：结束回合（end go）——直接清选中回自由镜头，回合推进交给回合机。</summary>
        public bool EndGo()
        {
            if (_selected == null)
                return false;

            _selected.MarkEndGo();
            PublishAction(_selected, BattleActionKind.EndGo);

            EventBus.Publish(BattleEvents.SelectionChanged,
                new SelectionChangedPayload(_selected.PirateId, false));
            _selected = null;
            _state = InteractionRules.Advance(_state, InteractionTrigger.EndGo);
            if (trajectory != null)
                trajectory.Hide();
            return true;
        }

        // ------------------------------------------------------------------
        // 事件回调
        // ------------------------------------------------------------------

        void OnBattleStarted(BattleStartedPayload payload)
        {
            ResetAll();
        }

        void OnTurnEnded(int teamNumber)
        {
            // 回合推进时若仍持有选中（异常路径：未消耗动作被超时推进等）——清理回自由镜头。
            if (_state == InteractionState.SelectedIdle || _state == InteractionState.OperationActive)
                ResetAll();
        }

        void ResetAll()
        {
            if (_selected != null)
                EventBus.Publish(BattleEvents.SelectionChanged,
                    new SelectionChangedPayload(_selected.PirateId, false));
            _selected = null;
            _state = InteractionState.FreeCamera;
            SetHoverTarget(null);
            if (trajectory != null)
                trajectory.Hide();
        }

        // ------------------------------------------------------------------
        // 悬停反馈（§4.5：30px 内最近本队存活角色高亮）
        // ------------------------------------------------------------------

        void UpdateHover()
        {
            bool pickable = (_state == InteractionState.FreeCamera || _state == InteractionState.SelectedIdle)
                && !IsPointerOverUi();
            PirateBase target = pickable ? PickTeamCharacter(Input.mousePosition) : null;
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

        /// <summary>鼠标是否悬停在 UGUI 控件上（无 EventSystem 视为不在 UI 上，不阻塞操作）。</summary>
        static bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        /// <summary>屏幕空间 30px 内最近的本队存活角色（§3.4 minD2=900，契约 #1 沿用）。</summary>
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

        void PublishAction(PirateBase pirate, BattleActionKind kind)
        {
            // battle.NotifyActionSelected 内部发布 ActionSelected 频道并清零 inactivity；
            // 未接线时退化为直接发布，避免事件丢失。
            if (battle != null)
                battle.NotifyActionSelected(pirate, kind);
            else
                EventBus.Publish(BattleEvents.ActionSelected, new ActionSelectedPayload(
                    pirate.PirateId, pirate.TeamIndex, kind));
        }

        void TogglePause()
        {
            if (BattlePause.IsPaused)
                BattleUiBridge.RequestResumePause();
            else
                BattleUiBridge.RequestOpenPause();
        }
    }
}
