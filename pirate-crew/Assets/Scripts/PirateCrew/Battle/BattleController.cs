using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 战斗组装与结算根（翻译自 Godot <c>scripts/battle.gd</c> 的运行时职责）。
    ///
    /// 【对应章节】§4.3（按关卡 XML 坐标/队伍实例化出战单位）、§5.5（水位 = waterTileY*32）、
    ///             §5.3（Physics.OverlapSphere 取候选 → <see cref="ExplosionResolver"/> 纯逻辑算分 →
    ///             应用伤害与击退）、§3.3（胜负 <see cref="TurnRules.ComputeOutcome"/> /
    ///             得分 <see cref="ScoreRules.LevelScore"/>）、§4.4（落水即死）。
    ///
    /// 【3D 化决策】坐标/重力/初速换算全部集中在 <see cref="LevelGeometry"/>；
    /// 全局重力设为 Flash weight=1 的等价连续重力，物理帧率设为原版 25fps，
    /// 保证 <see cref="TrajectoryPreview"/> 与实弹轨迹同源（详见 LevelGeometry 类头）。
    ///
    /// 【爆炸坐标约定翻转】<see cref="ExplosionResolver.Resolve"/> 的输入/输出都是 Flash 约定
    /// （y 向下、单位 px）；本类在调用前把世界坐标转成 Flash 像素，调用后用
    /// <see cref="LevelGeometry.FlashVelocityDeltaToWorld"/> 把 <c>DeltaVy</c> 翻成 Unity 的 +Y 向上。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleController : MonoBehaviour
    {
        [Header("关卡数据")]
        [Tooltip("优先使用本资产；为空时回退到 LevelCatalog 的 fallbackLevelNumber。")]
        [SerializeField] LevelDefinition level;
        [SerializeField] int fallbackLevelNumber = 1;

        [Header("组装引用（场景内直连）")]
        [SerializeField] PirateBase piratePrefab;
        [SerializeField] Transform team0Root;
        [SerializeField] Transform team1Root;
        [Tooltip("水面视觉对象；运行时把 y 设为水位（§5.5）。")]
        [SerializeField] Transform waterPlane;
        [SerializeField] TurnManager turnManager;
        [SerializeField] AimThrowController aimController;
        [SerializeField] BattleCameraController battleCamera;

        [Header("层掩码")]
        [Tooltip("爆炸候选与单位射线用的层。")]
        [SerializeField] LayerMask pirateLayerMask = ~0;
        [Tooltip("瞄准目标点射线用的地面/瓦片层。")]
        [SerializeField] LayerMask groundLayerMask = ~0;

        [Header("模式")]
        [Tooltip("true = 1P（team2 由 AI 控制）；false = 2P 热座。")]
        [SerializeField] bool team1IsAi = true;

        readonly List<PirateBase> _allPirates = new List<PirateBase>();
        readonly BattleTeam[] _teams = new BattleTeam[2];
        BattlePlan _plan;
        bool _spawned;
        bool _matchFinished;
        float _waterWorldY;
        int _nextPirateId;

        /// <summary>当前关卡序号。</summary>
        public int LevelNumber => _plan != null ? _plan.LevelNumber : fallbackLevelNumber;

        /// <summary>队伍数量（恒 2）。</summary>
        public int TeamCount => 2;

        /// <summary>出战计划（组装结果）。</summary>
        public BattlePlan Plan => _plan;

        /// <summary>全部角色。</summary>
        public IReadOnlyList<PirateBase> AllPirates => _allPirates;

        /// <summary>水面世界 Y（Unity 约定，y 向上）。</summary>
        public float WaterWorldY => _waterWorldY;

        /// <summary>爆炸/单位射线层掩码。</summary>
        public LayerMask PirateLayerMask => pirateLayerMask;

        /// <summary>地面/瞄准射线层掩码。</summary>
        public LayerMask GroundLayerMask => groundLayerMask;

        /// <summary>当前回合队伍（由 TurnManager 维护）。</summary>
        public BattleTeam CurrentTeam => turnManager != null ? turnManager.CurrentTeam : _teams[0];

        /// <summary>相机聚焦参考点（用于 panToCharacter 的"离相机中心最近"判定）。</summary>
        public Vector3 CameraFocusPoint => battleCamera != null ? battleCamera.FocusPoint : transform.position;

        /// <summary>对局是否已结束。</summary>
        public bool IsMatchOver => TurnRules.IsMatchOver(_teams[0].AnyAlive, _teams[1].AnyAlive);

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        void Awake()
        {
            BuildPlan();
            ApplyPhysicsConvention();
            SpawnTeams();
        }

        void Start()
        {
            if (waterPlane != null)
            {
                Vector3 p = waterPlane.position;
                p.y = _waterWorldY;
                waterPlane.position = p;
            }

            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(LevelNumber, TeamCount));

            if (turnManager != null)
                turnManager.StartBattle();
            else
                Debug.LogError("[BattleController] 未接线 TurnManager，回合不会推进。");
        }

        void Update()
        {
            if (!_spawned || _matchFinished)
                return;

            // §4.4 落水即死：全局规则，每帧对所有存活角色判定。
            for (int i = 0; i < _allPirates.Count; i++)
            {
                PirateBase pirate = _allPirates[i];
                if (pirate != null && pirate.Alive)
                    pirate.CheckWaterDeath(_waterWorldY);
            }

            CheckMatchOver();
        }

        // ------------------------------------------------------------------
        // 组装
        // ------------------------------------------------------------------

        void BuildPlan()
        {
            _plan = level != null
                ? LevelGeometry.BuildBattlePlan(level)
                : LevelGeometry.BuildBattlePlan(LevelCatalog.Get(fallbackLevelNumber));

            _waterWorldY = _plan.WaterWorldY;
            _teams[0] = new BattleTeam(1, aiControlled: false);
            _teams[1] = new BattleTeam(2, aiControlled: team1IsAi);
        }

        /// <summary>
        /// 3D 化决策：把 PhysX 全局重力设为 Flash weight=1 的等价重力（-19.53125），
        /// 并把物理帧率设为原版 25fps，使离散积分与 Ballistics 逐步一致（预览 = 实弹）。
        /// 角色/武器的 Rigidbody 用 <c>useGravity</c> 吃这份全局重力；weight=0 的 cannonball
        /// 由其实弹脚本自行 <c>useGravity = false</c>，weight=1.5 的 boulder 同理自定义。
        /// </summary>
        void ApplyPhysicsConvention()
        {
            Physics.gravity = LevelGeometry.WorldGravity(CrewCatalog.Weight);
            Time.fixedDeltaTime = LevelGeometry.FrameSeconds;
        }

        void SpawnTeams()
        {
            if (piratePrefab == null)
            {
                Debug.LogError("[BattleController] 未配置 PirateBase 预制体，无法生成出战单位。");
                return;
            }

            for (int i = 0; i < _plan.Entries.Count; i++)
            {
                SpawnPlanEntry entry = _plan.Entries[i];
                Transform root = entry.TeamIndex == 0 ? team0Root : team1Root;
                if (root == null)
                    root = transform;

                PirateBase pirate = Instantiate(piratePrefab, entry.WorldPosition, Quaternion.identity, root);
                pirate.Initialize(_nextPirateId++, entry);
                _allPirates.Add(pirate);
                _teams[entry.TeamIndex].Add(pirate);
            }

            _spawned = true;
        }

        /// <summary>取队伍（teamIndex 0/1）。</summary>
        public BattleTeam GetTeam(int teamIndex)
        {
            if (teamIndex < 0 || teamIndex >= _teams.Length)
                return null;
            return _teams[teamIndex];
        }

        /// <summary>§3.2 startTurn：对本队全部存活角色做重置（行动经济 / evilness / 保底武器）。</summary>
        public void ResetTeamForTurnStart(BattleTeam team)
        {
            if (team == null)
                return;

            IReadOnlyList<PirateBase> characters = team.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                PirateBase c = characters[i];
                if (c != null && c.Alive)
                    c.ResetForTurnStart();
            }
        }

        /// <summary>inactivity 判定：是否有角色在动 / 玩家正在瞄准。</summary>
        public bool IsAnythingActive()
        {
            for (int i = 0; i < _allPirates.Count; i++)
            {
                PirateBase pirate = _allPirates[i];
                if (pirate != null && pirate.Alive && pirate.IsMoving())
                    return true;
            }

            return aimController != null && aimController.IsAiming;
        }

        /// <summary>选中角色（AimThrowController 在点选命中后调用）。</summary>
        public void SelectCharacter(PirateBase pirate)
        {
            BattleTeam team = CurrentTeam;
            if (team == null || pirate == null || !pirate.Alive || pirate.TeamIndex != team.TeamIndex)
                return;

            // 已经选过同一角色（continueTurn）或尚未选人时才允许。
            bool again = team.SelectedCharacter == pirate;
            if (!team.Select(pirate, again))
                return;

            if (aimController != null)
                aimController.ResetForSelection(pirate);

            // action_selected 在真正执行动作（抛自己/用武器/end go）时由 AimThrowController 发布；
            // 这里只做镜头聚焦与清零 inactivity（选择本身也是"有活动"）。
            EventBus.Publish(BattleEvents.CameraFocusRequested, pirate.transform);
            if (turnManager != null)
                turnManager.NotifyActivity();
        }

        /// <summary>广播一次动作选择（AimThrowController 在真正执行动作时调用）。</summary>
        public void NotifyActionSelected(PirateBase pirate, BattleActionKind kind)
        {
            if (pirate == null)
                return;

            EventBus.Publish(BattleEvents.ActionSelected, new ActionSelectedPayload(
                pirate.PirateId, pirate.TeamIndex, kind));
            if (turnManager != null)
                turnManager.NotifyActivity();
        }

        // ------------------------------------------------------------------
        // §5.3 爆炸结算
        // ------------------------------------------------------------------

        /// <summary>
        /// 一次爆炸结算：用 <see cref="Physics.OverlapSphere"/> 取候选，再全部交给
        /// <see cref="ExplosionResolver.Resolve"/> 算分（半径/衰减/击退/evilness 均为纯逻辑）。
        /// </summary>
        /// <param name="worldCenter">爆心世界坐标（Unity，y 向上）。</param>
        /// <param name="size">爆炸 size（Flash px，radius = size/2 + 20）。</param>
        /// <param name="maxDamage">爆心最大伤害（Flash）。</param>
        /// <param name="caster">施暴者（累加 evilness；可为 null，如火药桶连锁）。</param>
        public ExplosionResult ResolveExplosion(Vector3 worldCenter, float size, float maxDamage, PirateBase caster)
        {
            float radiusWorld = LevelGeometry.PixelsToUnits(ExplosionResolver.Radius(size));
            Collider[] overlaps = Physics.OverlapSphere(
                worldCenter, radiusWorld, pirateLayerMask, QueryTriggerInteraction.Ignore);

            var candidates = new List<PirateBase>(overlaps.Length);
            var targets = new List<ExplosionTarget>(overlaps.Length);

            for (int i = 0; i < overlaps.Length; i++)
            {
                PirateBase pirate = overlaps[i] != null ? overlaps[i].GetComponentInParent<PirateBase>() : null;
                if (pirate == null || candidates.Contains(pirate))
                    continue;

                candidates.Add(pirate);
                Vector2 px = LevelGeometry.WorldToPixel(pirate.transform.position);
                targets.Add(new ExplosionTarget(px.x, px.y, pirate.Alive));
            }

            Vector2 centerPx = LevelGeometry.WorldToPixel(worldCenter);
            ExplosionResult result = ExplosionResolver.Resolve(size, maxDamage, centerPx.x, centerPx.y, targets);

            for (int i = 0; i < result.Hits.Length; i++)
            {
                ExplosionHit hit = result.Hits[i];
                PirateBase target = candidates[hit.Index];

                // ★ 方向翻转：hit.DeltaVy 是 Flash 约定（y 向下，公式里的 -6k 表示向上），
                //   FlashVelocityDeltaToWorld 取负 y → Unity 的 +Y 向上，语义正确。
                Vector3 deltaV = LevelGeometry.FlashVelocityDeltaToWorld(hit.DeltaVx, hit.DeltaVy);
                target.ApplyImpulseDelta(deltaV);
                target.SubtractHealth(hit.Damage);
            }

            if (caster != null)
                caster.AddEvilness(result.EvilnessGain);

            return result;
        }

        // ------------------------------------------------------------------
        // §3.3 胜负与得分
        // ------------------------------------------------------------------

        /// <summary>检查并结算对局结果（任一方全灭即结束）。幂等。</summary>
        public void CheckMatchOver()
        {
            if (_matchFinished)
                return;

            bool team0Alive = _teams[0].AnyAlive;
            bool team1Alive = _teams[1].AnyAlive;
            if (!TurnRules.IsMatchOver(team0Alive, team1Alive))
                return;

            _matchFinished = true;

            MatchOutcome outcome = TurnRules.ComputeOutcome(team0Alive, team1Alive, _teams[1].AiControlled);

            // §3.3 / §7.3：1P 才有该得分口径；2P 热座记 0。
            int score = 0;
            if (_teams[1].AiControlled)
                score = ScoreRules.LevelScore(_teams[0].AverageHealth, _teams[0].TotalTurnsTaken, LevelNumber);

            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(
                (int)outcome, score, _teams[1].AiControlled));
        }
    }
}
