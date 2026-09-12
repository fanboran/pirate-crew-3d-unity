using System.Collections.Generic;
using System.Diagnostics;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 敌方 AI 的 MonoBehaviour 薄壳：时间片驱动 <see cref="AiEvaluation"/>，并把决定交回
    /// <see cref="TurnManager"/> 执行。
    ///
    /// 【对应章节】§6.1（<c>Team.advance</c> 的 30ms 时间片、逐角色推进 <c>aiThink</c>、
    ///             汇总 <c>aiMoveList</c> 取 max、先给镜头再执行、<c>aiCanBailOut</c> 跳过回合）、
    ///             §3.2（startTurn 全队可动 / continueTurn 只有已选角色 canShoot 且 canThrow=false）、
    ///             §3.4（抛自己 / 用武器 / end go 三路径行动经济）。
    ///
    /// 【分层】所有决策与打分都在纯 C# 的 <see cref="AiEvaluation"/> 里；本类只做
    ///         「快照组装 → 分帧驱动 → 发布事件 → 改动 <see cref="PirateBase"/> 状态位」。
    ///         本类不 <c>new GameObject</c>、不 <c>Find</c>，引用全部来自 [SerializeField]。
    ///
    /// 【兜底】<see cref="TurnManager"/> 另设看门狗：本类若因异常/无候选迟迟不返回，
    ///         由 TurnManager 强制结束回合（AI 队回合绝不卡死）。
    ///
    /// 【M2 已知边界（与武器运行时相关，属另一 agent）】
    ///   §6.3 的 tidalWave / seagull / voodooDoll / 箱体 / cannon 等特殊武器的<b>实际效果</b>
    ///   （生成浪、海鸥投弹、交换速度、放置箱体）需要各自武器脚本；本类当前只完成
    ///   「选中角色 + 装备槽位 + 扣行动经济 + 广播 ai_decided（含落点/目标/速度）」，
    ///   效果执行由后续武器运行时订阅 <see cref="BattleEvents.AiDecided"/> 或由 BattleController 接线。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AiController : MonoBehaviour
    {
        /// <summary>§6.1 每帧给 AI 的时间片预算（毫秒）。</summary>
        public const float DefaultSliceMilliseconds = 30f;

        /// <summary>每个角色在世界里的一个评估会话。</summary>
        enum Phase
        {
            Idle = 0,
            Evaluating = 1,
            DelayBeforeExecute = 2,
            Executed = 3,
        }

        [Header("组装引用（场景内直连）")]
        [SerializeField] BattleController battle;
        [SerializeField] TurnManager turnManager;

        [Header("评估参数")]
        [Tooltip("AI 随机种子（§6.4）。同一 seed 的评估完全可复现，便于录屏/回放与排错。")]
        [SerializeField] int randomSeed = 20260912;

        [Tooltip("每帧时间片预算（§6.1 = 30ms）。")]
        [SerializeField] float sliceMilliseconds = DefaultSliceMilliseconds;

        [Tooltip("选定动作后、真正执行前的延迟帧数：对应 §6.1「先 panToCharacter 给镜头，再执行」。")]
        [SerializeField] int executeDelayFrames = 12;

        // 地形近似：竞技场是一块 XZ 矩形（地面顶面恒为世界 y=0），边界外即水面。
        // 尺寸来自 BattleController.Plan（WorldWidth/WorldDepth），无需额外序列化字段。

        readonly List<AiEvaluationSession> _sessions = new List<AiEvaluationSession>();
        readonly List<AiMoveCandidate> _candidates = new List<AiMoveCandidate>();
        readonly Dictionary<int, PirateBase> _actors = new Dictionary<int, PirateBase>();
        readonly Stopwatch _sliceWatch = new Stopwatch();

        BattleTeam _team;
        Phase _phase = Phase.Idle;
        int _sessionIndex;
        int _delayRemaining;
        bool _continuePhase;      // true = §3.2 continueTurn（canThrow=false，只有已选角色可 shoot）
        bool _canBailOut;         // §6.1 aiCanBailOut（startTurn=false；continueTurn=true）
        AiDecision _decision;
        bool _hasDecision;
        int _totalEvaluationCount;

        /// <summary>是否正在思考（评估中或等镜头），供 <see cref="TurnManager"/> 暂停 inactivity 计数。</summary>
        public bool IsThinking => _phase == Phase.Evaluating || _phase == Phase.DelayBeforeExecute;

        /// <summary>最近一次决定（调试/测试用）。</summary>
        public bool HasDecision => _hasDecision;

        /// <summary>最近一次决定。</summary>
        public AiDecision LastDecision => _decision;

        void Awake()
        {
            if (sliceMilliseconds <= 0f)
                sliceMilliseconds = DefaultSliceMilliseconds;
        }

        // ------------------------------------------------------------------
        // 对外入口（TurnManager 调用）
        // ------------------------------------------------------------------

        /// <summary>
        /// §3.2 <c>startTurn</c>：AI 队整队评估（每个存活角色都可抛自己 + 用武器）。
        /// </summary>
        public void BeginAiTurn(BattleTeam team)
        {
            _continuePhase = false;
            _canBailOut = false;
            StartEvaluation(team);
        }

        /// <summary>
        /// §3.2 <c>continueTurn</c>：同一角色做第 2 个动作——<c>canThrow = false</c>、
        /// 且只有已选角色 <c>canShoot = true</c>；此时 <c>aiCanBailOut = true</c>（可跳过）。
        /// </summary>
        public void BeginAiContinueTurn(BattleTeam team)
        {
            _continuePhase = true;
            _canBailOut = true;
            StartEvaluation(team);
        }

        /// <summary>中止当前 AI 回合（对局结束等）；幂等。</summary>
        public void CancelAiTurn()
        {
            _phase = Phase.Idle;
            _sessions.Clear();
            _actors.Clear();
            _candidates.Clear();
        }

        // ------------------------------------------------------------------
        // 组装 + 分帧驱动
        // ------------------------------------------------------------------

        void StartEvaluation(BattleTeam team)
        {
            _team = team;
            _sessions.Clear();
            _candidates.Clear();
            _actors.Clear();
            _sessionIndex = 0;
            _hasDecision = false;
            _totalEvaluationCount = 0;

            if (team == null || battle == null)
            {
                _phase = Phase.Idle;
                return;
            }

            PirateBase selected = team.SelectedCharacter;
            IReadOnlyList<PirateBase> characters = team.Characters;

            for (int i = 0; i < characters.Count; i++)
            {
                PirateBase actor = characters[i];
                if (actor == null || !actor.Alive)
                    continue;
                if (_continuePhase && actor != selected)
                    continue;   // §3.2 continueTurn：只允许已选角色用武器

                bool canThrow = !_continuePhase;   // startTurn 可抛自己；continueTurn 不可
                bool canShoot = true;              // 只有已选角色会进入本循环

                AiBattlefield field = BuildField(actor, canThrow, canShoot);
                if (field == null)
                    continue;

                // 每个角色一个独立随机源（seed 由角色 id 派生）→ 各角色评估互不干扰且可复现。
                var random = new AiRandom(unchecked(randomSeed + actor.PirateId * 7919));
                _sessions.Add(new AiEvaluationSession(field, random, new AiEvaluationOptions()));
                _actors[actor.PirateId] = actor;
            }

            EventBus.Publish(BattleEvents.AiThinking, new AiThinkingPayload(team.Number, _sessions.Count));

            _phase = Phase.Evaluating;
        }

        /// <summary>把当前角色的运行时状态快照成 Flash 像素域的 <see cref="AiBattlefield"/>。</summary>
        AiBattlefield BuildField(PirateBase actor, bool canThrow, bool canShoot)
        {
            if (actor == null || battle == null)
                return null;

            IReadOnlyList<PirateBase> all = battle.AllPirates;
            var units = new List<AiUnit>(all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                PirateBase p = all[i];
                if (p == null)
                    continue;

                // 世界坐标 → Flash 平面像素（x = 世界 X、y = 世界 Z 纵深；忽略高度）。
                Vector2 px = LevelGeometry.ArenaToPixel(p.transform.position);
                units.Add(new AiUnit(
                    p.PirateId, p.TeamIndex, px.x, px.y,
                    p.Health, p.MaxHealth, p.Alive, p.Evilness, p.Luck));
            }

            var weapons = new List<AiWeaponSlot>();
            if (canShoot)
            {
                IReadOnlyList<WeaponId> ids = actor.Inventory.ToWeaponIdList();
                for (int i = 0; i < ids.Count; i++)
                    weapons.Add(new AiWeaponSlot(i, ids[i]));
            }

            AiTerrain terrain = BuildTerrain();
            // tidalWave 的「近水带」判据 <c>y &gt;= waterY - 300</c> 里的 waterY 是平面像素标量；
            // 3D 水面是高度常量，取 WaterSurfaceY 的像素等价量。平坦竞技场上所有单位都站在同一地面，
            // 因此该带自然覆盖全部单位（与「浪对所有角色一视同仁」一致）。
            float waterPixelY = LevelGeometry.UnitsToPixels(LevelGeometry.WaterSurfaceY);

            return new AiBattlefield(
                units, actor.PirateId, canThrow, canShoot, weapons, terrain, waterPixelY);
        }

        /// <summary>
        /// 近似地形：竞技场 = 一块 XZ 平面矩形 + 瓦片地形抬升网格（地面顶面恒为世界 y=0）。
        /// 尺寸取 <see cref="BattleController.Plan"/> 的 WorldWidth / WorldDepth（瓦片 = 世界单位），
        /// 抬升网格取 <see cref="BattleController.Terrain"/>（同场景直接引用，不做 Find）。
        /// </summary>
        AiTerrain BuildTerrain()
        {
            float widthPx = 3000f;
            float depthPx = 3000f;

            if (battle != null && battle.Plan != null)
            {
                if (battle.Plan.WorldWidth > 0f)
                    widthPx = battle.Plan.WorldWidth * LevelGeometry.PixelsPerUnit;
                if (battle.Plan.WorldDepth > 0f)
                    depthPx = battle.Plan.WorldDepth * LevelGeometry.PixelsPerUnit;
            }

            TileTerrainGrid grid = battle != null ? battle.Terrain : null;
            return new AiTerrain(0f, widthPx, 0f, depthPx, grid);
        }

        void Update()
        {
            switch (_phase)
            {
                case Phase.Evaluating:
                    RunSlices();
                    break;

                case Phase.DelayBeforeExecute:
                    // §6.1：先 panToCharacter 给镜头，镜头到位后才执行 aiMoveDetails。
                    if (_delayRemaining > 0)
                        _delayRemaining--;
                    if (_delayRemaining <= 0)
                        ExecuteDecision();
                    break;
            }
        }

        /// <summary>在 30ms 预算内推进各角色会话（§6.1 <c>do { aiThink() } while(elapsed &lt; 30)</c>）。</summary>
        void RunSlices()
        {
            _sliceWatch.Restart();
            int safety = 0;

            while (_phase == Phase.Evaluating && _sessionIndex < _sessions.Count)
            {
                if (_sliceWatch.Elapsed.TotalMilliseconds >= sliceMilliseconds)
                    break;
                if (++safety > 100000)
                    break;   // 防御：单帧工作单元上限，避免极端参数下卡帧

                AiEvaluationSession session = _sessions[_sessionIndex];
                if (!session.StepOnce())
                {
                    _totalEvaluationCount += session.EvaluationCount;
                    _sessionIndex++;
                }
            }

            if (_phase == Phase.Evaluating && _sessionIndex >= _sessions.Count)
                FinishEvaluation();
        }

        /// <summary>§6.1：<c>all = concat(所有角色 aiMoveList); best = argmax(success)</c>。</summary>
        void FinishEvaluation()
        {
            _candidates.Clear();
            int fallbackActorId = -1;

            for (int i = 0; i < _sessions.Count; i++)
            {
                AiEvaluationSession session = _sessions[i];
                IReadOnlyList<AiMoveCandidate> list = session.Candidates;
                for (int j = 0; j < list.Count; j++)
                    _candidates.Add(list[j]);

                if (fallbackActorId < 0 && list.Count > 0)
                    fallbackActorId = list[0].ActorUnitId;
            }

            AiMoveCandidate? best = AiEvaluation.PickBest(_candidates);
            int actingId = best.HasValue ? best.Value.ActorUnitId : fallbackActorId;
            if (actingId < 0 && _actors.Count > 0)
            {
                foreach (KeyValuePair<int, PirateBase> kv in _actors)
                {
                    actingId = kv.Key;
                    break;
                }
            }

            _decision = AiEvaluation.BuildDecisionFromBest(
                actingId, best, _canBailOut, _totalEvaluationCount);
            _hasDecision = true;

            var payload = new AiDecidedPayload(
                _decision.ActorUnitId, _actors.ContainsKey(_decision.ActorUnitId)
                    ? _actors[_decision.ActorUnitId].TeamIndex
                    : -1,
                (int)_decision.Kind, _decision.WeaponSlotIndex,
                _decision.TargetUnitId, _decision.Success, _decision.ShouldBailOut);
            EventBus.Publish(BattleEvents.AiDecided, payload);

            // §6.1 panToCharacter：先给镜头，再执行（由 executeDelayFrames 兑现）。
            if (_actors.TryGetValue(_decision.ActorUnitId, out PirateBase pan) && pan != null)
                EventBus.Publish(BattleEvents.CameraFocusRequested, pan.transform);

            _phase = Phase.DelayBeforeExecute;
            _delayRemaining = Mathf.Max(0, executeDelayFrames);
            if (_delayRemaining == 0)
                ExecuteDecision();
        }

        /// <summary>
        /// §6.1 <c>aiMoveDetails</c> 执行：<c>weapon &gt;= 0 → equip + aiPerform</c>；
        /// <c>weapon == -1</c> → 直接把速度赋给角色自身。执行后把回合推进交还 TurnManager。
        /// </summary>
        void ExecuteDecision()
        {
            _phase = Phase.Executed;

            if (!_hasDecision)
            {
                EndTurnSafely(null);
                return;
            }

            if (!_actors.TryGetValue(_decision.ActorUnitId, out PirateBase actor) || actor == null || !actor.Alive)
            {
                EndTurnSafely(null);
                return;
            }

            switch (_decision.Kind)
            {
                case AiActionKind.ThrowSelf:
                {
                    _team?.Select(actor, again: _continuePhase);
                    actor.MarkThrowSelf();
                    actor.ApplyLaunchVelocity(_decision.Vx, _decision.Vy);
                    battle?.NotifyActionSelected(actor, BattleActionKind.ThrowSelf);
                    // 抛自己后 canShoot 仍为 true → 回合未完成，TurnManager 稍后会走 continueTurn。
                    break;
                }

                case AiActionKind.UseWeapon:
                {
                    _team?.Select(actor, again: _continuePhase);
                    if (actor.Inventory.Equip(_decision.WeaponSlotIndex))
                    {
                        // 装备 + 扣行动经济，然后交给统一的弹体生成入口（§5.2）。
                        // 至此 AI 用武器不再只改状态：弹体会飞行、按条件引爆并走 ResolveExplosion 结算。
                        if (actor.MarkUseWeapon(out WeaponId used) && battle != null)
                        {
                            WeaponStats stats = WeaponCatalog.Get(used);
                            Vector3 aimWorld = LevelGeometry.PixelToArena(_decision.AimX, _decision.AimY);
                            battle.SpawnWeaponProjectiles(
                                stats, actor, actor.transform.position, aimWorld,
                                _decision.Vx, _decision.Vy);
                        }
                    }
                    else
                    {
                        actor.MarkEndGo();   // 槽位非法 → 不冒险，直接收尾
                    }

                    battle?.NotifyActionSelected(actor, BattleActionKind.UseWeapon);
                    break;
                }

                default:
                {
                    EndTurnSafely(actor);
                    break;
                }
            }
        }

        /// <summary>end go / 兜底收尾 + §6.1 bailout（无正收益且允许放弃 → 直接推进回合）。</summary>
        void EndTurnSafely(PirateBase actor)
        {
            if (actor == null && _team != null)
                actor = _team.SelectedCharacter ?? _team.FirstAlive();

            if (actor != null)
            {
                _team?.Select(actor, again: _continuePhase);
                actor.MarkEndGo();
                battle?.NotifyActionSelected(actor, BattleActionKind.EndGo);
            }

            // §6.1：bailout 直接 nextTurn；其它 end go 也立即交还，保证 AI 回合不空转等待。
            if (turnManager != null)
                turnManager.RequestEndTurn();
        }
    }
}
