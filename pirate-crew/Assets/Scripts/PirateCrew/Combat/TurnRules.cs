namespace PirateCrew.Combat
{
    /// <summary>
    /// 单回合两阶段操作的纯状态。
    /// 一个角色每回合可做「抛自己一次」+「用一件武器一次」；用武器即结束回合。
    /// </summary>
    public readonly struct ActionState
    {
        /// <summary>本回合是否已抛出自己。</summary>
        public readonly bool Thrown;

        /// <summary>本回合是否已使用武器。</summary>
        public readonly bool Fired;

        /// <summary>是否还允许抛出自己。</summary>
        public readonly bool CanThrow;

        /// <summary>是否还允许使用武器。</summary>
        public readonly bool CanShoot;

        public ActionState(bool thrown, bool fired, bool canThrow, bool canShoot)
        {
            Thrown = thrown;
            Fired = fired;
            CanThrow = canThrow;
            CanShoot = canShoot;
        }

        /// <summary>回合开始时的初始状态。</summary>
        public static ActionState Start => new ActionState(false, false, true, true);

        /// <summary>是否还有任何可执行动作。</summary>
        public bool CanAct => CanThrow || CanShoot;

        public override string ToString()
        {
            return $"ActionState(Thrown={Thrown}, Fired={Fired}, CanThrow={CanThrow}, CanShoot={CanShoot})";
        }
    }

    /// <summary>
    /// 对局结果。
    /// </summary>
    public enum MatchOutcome
    {
        /// <summary>team0（1P 模式下的玩家 / 2P 的红队）获胜。</summary>
        Team0Win = 0,

        /// <summary>team1（2P 的蓝队）获胜。</summary>
        Team1Win = 1,

        /// <summary>双方同归于尽（2P 热座平局）。</summary>
        Draw = 2,

        /// <summary>1P 模式下玩家失败（含双方全灭的罕见情况）。</summary>
        LevelFailed = 3
    }

    /// <summary>
    /// 回合流转与胜负规则。
    /// 空闲帧阈值推进回合、<c>isTurnComplete</c> 回合状态机、回合交替与胜负判定、
    /// 单回合两阶段行动经济，均为本工程设计值（<b>【提案/待定】</b>——尚无已裁决文档为这些规则取值背书）。
    /// 全部为静态纯函数/纯状态迁移，不依赖 MonoBehaviour / GameObject。
    /// </summary>
    public static class TurnRules
    {
        /// <summary>空闲帧阈值：inactivity &gt; 10（严格大于）才推进回合（【提案/待定】）。</summary>
        public const int DefaultInactivityThreshold = 10;

        /// <summary>
        /// 回合是否已结束：
        /// 选中的角色死亡，或它既不能抛也不能用武器。
        /// </summary>
        public static bool IsTurnComplete(bool selectedAlive, bool canThrow, bool canShoot)
        {
            return !selectedAlive || (!canThrow && !canShoot);
        }

        /// <summary>ActionState 重载。</summary>
        public static bool IsTurnComplete(bool selectedAlive, ActionState state)
        {
            return IsTurnComplete(selectedAlive, state.CanThrow, state.CanShoot);
        }

        /// <summary>
        /// 换人规则（交互操作契约 §D）：当前选中**未消耗任何动作**（未抛自己、未用武器、
        /// 也没走过 end-go——后者把 CanThrow/CanShoot 全清）时，才允许改选其他单位；
        /// 动作后锁定到回合结束。
        /// </summary>
        public static bool CanSwitchSelection(ActionState selectedAction)
        {
            return !selectedAction.Thrown && !selectedAction.Fired && selectedAction.CanAct;
        }

        /// <summary>
        /// 空闲帧数是否已超过阈值：inactivityFrames &gt; threshold。
        /// 注意是严格大于：正好 10 帧不算，11 帧才算。
        /// </summary>
        public static bool InactivityExceeded(int inactivityFrames, int threshold = DefaultInactivityThreshold)
        {
            return inactivityFrames > threshold;
        }

        /// <summary>
        /// 是否应推进到下一队。
        /// 空闲超时（inactivity &gt; 10）时清零计数，再按 isTurnComplete() 分两路
        /// （complete → 推进回合，否则 → 继续本回合）。
        /// "推进回合"只对应 complete 的那一路，故本方法 = 超时 且 回合已完成。
        /// 未完成时的另一路请用 <see cref="ShouldContinueTurn"/>。
        /// </summary>
        public static bool ShouldAdvanceTurn(
            int inactivityFrames, bool turnComplete, int threshold = DefaultInactivityThreshold)
        {
            return InactivityExceeded(inactivityFrames, threshold) && turnComplete;
        }

        /// <summary>
        /// 超时但本回合尚未完成时，是否应让同一角色继续第 2 个动作。
        /// </summary>
        public static bool ShouldContinueTurn(
            int inactivityFrames, bool turnComplete, int threshold = DefaultInactivityThreshold)
        {
            return InactivityExceeded(inactivityFrames, threshold) && !turnComplete;
        }

        // ------------------------------------------------------------------
        // 行动经济
        // ------------------------------------------------------------------

        /// <summary>
        /// 抛自己：thrown=true、canThrow=false；canShoot 保持 true → 回合继续。
        /// </summary>
        public static ActionState ApplyThrowSelf(ActionState state)
        {
            return new ActionState(true, state.Fired, false, state.CanShoot);
        }

        /// <summary>
        /// 用武器：fired=true、canShoot=false、canThrow=false → 用武器即结束回合。
        /// </summary>
        public static ActionState ApplyUseWeapon(ActionState state)
        {
            return new ActionState(state.Thrown, true, false, false);
        }

        /// <summary>
        /// 点 end go：canThrow=false、canShoot=false → 立即结束回合。
        /// （表现层的 thrown/throwFinished 标记与 unequip 不参与行动经济，故不在此建模。）
        /// </summary>
        public static ActionState ApplyEndGo(ActionState state)
        {
            return new ActionState(state.Thrown, state.Fired, false, false);
        }

        // ------------------------------------------------------------------
        // 回合交替与胜负
        // ------------------------------------------------------------------

        /// <summary>下一队编号：1 ↔ 2 交替。</summary>
        public static int NextTeamNumber(int currentTeamNumber)
        {
            return currentTeamNumber == 1 ? 2 : 1;
        }

        /// <summary>对局是否已分出结果（任一方全灭即结束）。</summary>
        public static bool IsMatchOver(bool team0AnyAlive, bool team1AnyAlive)
        {
            return !team0AnyAlive || !team1AnyAlive;
        }

        /// <summary>
        /// 计算对局结果。
        /// 前提：调用方已确认 <see cref="IsMatchOver"/> 为 true。
        ///   · team1IsAI=true（1P 模式）：玩家存活且 AI 全灭 → Team0Win；
        ///     玩家全灭 → LevelFailed（含 AI 也全灭的罕见全灭）。
        ///   · team1IsAI=false（2P 热座）：一方全灭 → 该方败；双方全灭 → Draw。
        /// 若双方都存活（未满足前提），返回 Draw 表示无胜负。
        /// </summary>
        public static MatchOutcome ComputeOutcome(bool team0AnyAlive, bool team1AnyAlive, bool team1IsAI)
        {
            if (!IsMatchOver(team0AnyAlive, team1AnyAlive))
            {
                return MatchOutcome.Draw;   // 未结束，无胜负
            }

            if (team1IsAI)
            {
                // 1P 模式：玩家（team0）全灭即失败，AI 是否全灭不影响失败判定。
                return team0AnyAlive ? MatchOutcome.Team0Win : MatchOutcome.LevelFailed;
            }

            // 2P 热座
            if (!team0AnyAlive && !team1AnyAlive)
            {
                return MatchOutcome.Draw;
            }

            return team0AnyAlive ? MatchOutcome.Team0Win : MatchOutcome.Team1Win;
        }
    }
}
