using System;
using PirateCrew.Core;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 回合驱动器（MonoBehaviour 薄壳，规则在 <see cref="BattleFlowRules"/> / <see cref="TurnRules"/>）。
    ///
    /// 【对应章节】§3.1（主循环 inactivity 计数 + <c>inactivity &gt; 10</c> 两路分支）、
    ///             §3.2（startTurn / continueTurn / finishTurn / panToCharacter）、
    ///             §3.3（回合交替）。
    ///
    /// 【对 Godot 版的修正】Godot <c>turn_manager.gd</c> 的 <c>end_turn()</c> 全工程无调用方、
    /// 回合永不推进（已 grep 证实）。本实现把推进链真正接上：
    /// <c>Update</c> 累计 inactivity → 超阈值按 isTurnComplete 分路 → <c>EndTeamTurn</c> → 下一队 <c>BeginTeamTurn</c>。
    ///
    /// 【inactivity 语义】原版任何"有事发生"都会清零计数（角色在动 / 武器在飞 / 当前队未选角色等）。
    /// 本实现：<see cref="BattleController.IsAnythingActive"/> 为 true 即清零；
    /// 人类队尚未选角色时视为等待输入，始终清零（对应 §3.1 "Team.advance（当前队且未选角色）清零"）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnManager : MonoBehaviour
    {
        /// <summary>当前队回合开始（参数：队伍编号 1/2）。</summary>
        public event Action<int> TurnStarted;

        /// <summary>当前队回合结束（参数：队伍编号 1/2）。</summary>
        public event Action<int> TurnEnded;

        [Header("组装引用（场景内直连）")]
        [SerializeField] BattleController battle;
        [Tooltip("AI 评估器（§6）。未接线时退化为 autoResolveAiTurns 占位，保证回合循环不卡死。")]
        [SerializeField] AiController aiController;

        [Header("推进参数")]
        [Tooltip("inactivity 阈值（§3.1 = 10，严格大于才推进）。")]
        [SerializeField] int inactivityThreshold = BattleFlowRules.InactivityThreshold;

        [Tooltip("兜底：AI 队回合无决策时自动结束，保证回合循环不卡死（aiController 为空时也走这条）。")]
        [SerializeField] bool autoResolveAiTurns = true;

        [Tooltip("AI 决策看门狗（帧）：超时强制结束本回合，避免评估异常/无候选导致回合卡死。")]
        [SerializeField] int aiTurnTimeoutFrames = 600;

        int _inactivityFrames;
        int _aiWatchdogFrames;
        bool _started;
        BattleTeam _currentTeam;

        /// <summary>当前行动队伍。</summary>
        public BattleTeam CurrentTeam => _currentTeam;

        /// <summary>当前 inactivity 帧数。</summary>
        public int InactivityFrames => _inactivityFrames;

        /// <summary>是否已开始。</summary>
        public bool Started => _started;

        /// <summary>由 <see cref="BattleController.Start"/> 调用：从 teamIndex 0 开始第一回合。</summary>
        public void StartBattle()
        {
            if (_started || battle == null)
                return;

            _started = true;
            BeginTeamTurn(battle.GetTeam(0));
        }

        /// <summary>供外部（角色选择 / 拖拽 / 物理事件）清零 inactivity。</summary>
        public void NotifyActivity()
        {
            _inactivityFrames = 0;
        }

        /// <summary>主动结束当前队回合（玩家点 end go / 测试用）。</summary>
        public void RequestEndTurn()
        {
            if (!_started)
                return;
            _inactivityFrames = 0;
            EndTeamTurn();
        }

        void Update()
        {
            if (!_started || _currentTeam == null || battle == null)
                return;

            // 对局已结束：取消 AI 思考，不再推进。
            if (battle.IsMatchOver)
            {
                aiController?.CancelAiTurn();
                return;
            }

            // 0) AI 队：看门狗 + 思考期间冻结 inactivity。
            if (_currentTeam.AiControlled && aiController != null)
            {
                _aiWatchdogFrames++;
                if (_aiWatchdogFrames > aiTurnTimeoutFrames)
                {
                    ForceResolveAiTurn();   // 兜底：评估失败/超时也必须能推进回合
                    return;
                }

                if (aiController.IsThinking)
                    return;   // 分帧评估中：不累计 inactivity（§6.1 决策期间相机停止滚动）
            }

            // 1) 有任何"活动"（角色在动/瞄准中）→ 清零，不推进。
            if (battle.IsAnythingActive())
            {
                _inactivityFrames = 0;
                return;
            }

            // 2) 人类队尚未选角色：等待输入，不计 inactivity（原版 Team.advance 会持续清零）。
            if (!_currentTeam.AiControlled && _currentTeam.SelectedCharacter == null)
                return;

            // 3) 累计空闲帧并按 §3.1 分两路。
            _inactivityFrames++;
            TurnAdvanceDecision decision = BattleFlowRules.DecideAdvance(
                _inactivityFrames, _currentTeam.IsTurnComplete, inactivityThreshold);

            if (decision == TurnAdvanceDecision.Wait)
                return;

            _inactivityFrames = 0;   // 原版：进入分支即清零
            if (decision == TurnAdvanceDecision.AdvanceTurn)
                EndTeamTurn();
            else
                ContinueTurn();
        }

        /// <summary>
        /// 兜底：AI 队回合迟迟拿不到决策（评估异常/无候选/未接线）时强制收尾，保证循环不卡死。
        /// 与 <see cref="AiController"/> 内部的正常收尾互不冲突（幂等）。
        /// </summary>
        void ForceResolveAiTurn()
        {
            _aiWatchdogFrames = 0;
            aiController?.CancelAiTurn();

            PirateBase pick = _currentTeam.SelectedCharacter ?? _currentTeam.FirstAlive();
            if (pick != null)
            {
                if (_currentTeam.SelectedCharacter == null)
                    _currentTeam.Select(pick, again: false);
                pick.MarkEndGo();
            }

            EndTeamTurn();
        }

        /// <summary>§3.2 continueTurn：同一角色继续第 2 个动作（AI 走评估器；未接线时走占位）。</summary>
        void ContinueTurn()
        {
            if (_currentTeam.AiControlled)
            {
                if (aiController != null)
                {
                    // 真正评估：canThrow=false、只有已选角色 canShoot、aiCanBailOut=true（§3.2/§6.1）。
                    _aiWatchdogFrames = 0;
                    aiController.BeginAiContinueTurn(_currentTeam);
                    return;
                }

                if (autoResolveAiTurns)
                {
                    // 未接线 AiController 时的兜底：选首个存活角色并结束回合，保证循环不卡死。
                    PirateBase pick = _currentTeam.SelectedCharacter ?? _currentTeam.FirstAlive();
                    if (pick == null)
                    {
                        EndTeamTurn();
                        return;
                    }

                    if (_currentTeam.SelectedCharacter == null)
                        _currentTeam.Select(pick, again: false);
                    pick.MarkEndGo();
                    EndTeamTurn();
                    return;
                }
            }

            // 人类玩家：角色还有动作（canThrow/canShoot 至少一个为 true），等其继续操作，不强制推进。
        }

        /// <summary>§3.2 finishTurn + §3.3 回合交替。</summary>
        void EndTeamTurn()
        {
            if (_currentTeam == null)
                return;

            int endedNumber = _currentTeam.Number;
            _currentTeam.FinishTurn();

            TurnEnded?.Invoke(endedNumber);
            EventBus.Publish(BattleEvents.TurnEnded, endedNumber);

            // 对局已结束时不再开下一回合（BattleController 会广播 match_finished）。
            if (battle != null && battle.IsMatchOver)
                return;

            int nextTeamIndex = BattleFlowRules.NextTeamNumber(endedNumber) - 1;
            BeginTeamTurn(battle.GetTeam(nextTeamIndex));
        }

        /// <summary>§3.2 startTurn：清 selectedCharacter、totalTurnsTaken++、逐角色重置、广播、panToCharacter。</summary>
        void BeginTeamTurn(BattleTeam team)
        {
            if (team == null)
                return;

            _currentTeam = team;
            _inactivityFrames = 0;
            _aiWatchdogFrames = 0;

            team.StartTurn();
            battle.ResetTeamForTurnStart(team);

            PirateBase pan = PickPanTarget(team);
            int panId = pan != null ? pan.PirateId : -1;
            Transform panTransform = pan != null ? pan.transform : null;

            TurnStarted?.Invoke(team.Number);
            EventBus.Publish(BattleEvents.TurnStarted, new TurnStartedPayload(
                team.Number, team.TotalTurnsTaken, panId, panTransform));

            // §3.2 panToCharacter：首回合优先船长，否则选离相机中心最近的角色。
            if (pan != null)
                EventBus.Publish(BattleEvents.CameraFocusRequested, pan.transform);

            // §6.1：AI 队回合开始即启动分帧评估（整队逐角色 aiThink）。
            if (team.AiControlled && aiController != null)
                aiController.BeginAiTurn(team);
        }

        /// <summary>§3.2 默认镜头目标选择。</summary>
        PirateBase PickPanTarget(BattleTeam team)
        {
            PirateBase pan = null;

            if (team.TotalTurnsTaken == 1)
                pan = team.Captain();

            if (pan == null && battle != null)
                pan = team.NearestTo(battle.CameraFocusPoint);

            if (pan == null)
                pan = team.FirstAlive();

            return pan;
        }
    }
}
