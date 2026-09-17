using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// §3.1 主循环里 <c>inactivity &gt; 10</c> 之后的分支决策。
    /// </summary>
    public enum TurnAdvanceDecision
    {
        /// <summary>未超过阈值：继续等待/推进物理。</summary>
        Wait = 0,

        /// <summary>超时但本回合未完成：同一角色继续第 2 个动作（原版 <c>continueTurn()</c>）。</summary>
        ContinueTurn = 1,

        /// <summary>超时且回合已完成：推进到下一队（原版 <c>nextTurn()</c>）。</summary>
        AdvanceTurn = 2,
    }

    /// <summary>
    /// 战斗推进/行动经济的纯规则汇总层。
    ///
    /// 【架构】本类<b>只做转译与组合</b>，不重复实现 <see cref="TurnRules"/> 里已有的规则；
    ///         TurnRules 缺失的部分（经验分、分路决策）在此补齐并说明。
    ///         全部静态/纯 C#，不依赖 MonoBehaviour / GameObject。
    ///
    /// 【对应章节】§3.1（inactivity &gt; 10 两路分支）、§3.2（isTurnComplete / 保底武器判定）、
    ///             §3.3（回合交替、胜负、得分）、§3.4（投自己/用武器/end go 三路径行动经济）。
    ///
    /// 【为什么没有"队伍回合状态机"】回合内的选中角色 / 行动经济 / 累计回合数由运行时的
    /// <see cref="BattleTeam"/> 直接持有与校验（比纯数据副本更严）；规则判定统一委托 <see cref="TurnRules"/>。
    /// </summary>
    public static class BattleFlowRules
    {
        /// <summary>§3.1 空闲帧阈值（复用 TurnRules 的常量，单一来源）。</summary>
        public const int InactivityThreshold = TurnRules.DefaultInactivityThreshold;

        // ------------------------------------------------------------------
        // §3.1 回合推进分支
        // ------------------------------------------------------------------

        /// <summary>
        /// 原版 <c>enterFrame</c>：<c>if inactivity &gt; 10</c> 时清零计数，再按 isTurnComplete 分两路。
        /// 本方法给出这一帧应走哪条路；<see cref="TurnAdvanceDecision.Wait"/> 对应未超阈值。
        /// </summary>
        public static TurnAdvanceDecision DecideAdvance(
            int inactivityFrames, bool turnComplete, int threshold = InactivityThreshold)
        {
            if (!TurnRules.InactivityExceeded(inactivityFrames, threshold))
                return TurnAdvanceDecision.Wait;

            return turnComplete ? TurnAdvanceDecision.AdvanceTurn : TurnAdvanceDecision.ContinueTurn;
        }

        /// <summary>是否应推进到下一队（委托 TurnRules.ShouldAdvanceTurn）。</summary>
        public static bool ShouldAdvanceTurn(
            int inactivityFrames, bool turnComplete, int threshold = InactivityThreshold)
        {
            return TurnRules.ShouldAdvanceTurn(inactivityFrames, turnComplete, threshold);
        }

        /// <summary>超时但未完成：是否应让同一角色继续动作（委托 TurnRules.ShouldContinueTurn）。</summary>
        public static bool ShouldContinueTurn(
            int inactivityFrames, bool turnComplete, int threshold = InactivityThreshold)
        {
            return TurnRules.ShouldContinueTurn(inactivityFrames, turnComplete, threshold);
        }

        /// <summary>§3.2 isTurnComplete（委托 TurnRules，统一入口）。</summary>
        public static bool IsTurnComplete(bool selectedAlive, ActionState state)
        {
            return TurnRules.IsTurnComplete(selectedAlive, state);
        }

        // ------------------------------------------------------------------
        // §3.2 回合开始判定
        // ------------------------------------------------------------------

        /// <summary>§3.2/§5.5 保底武器判定：<c>hasWeapons.length &lt; 1</c>。</summary>
        public static bool ShouldGrantFallbackWeapon(int weaponCount)
        {
            return weaponCount < 1;
        }

        // ------------------------------------------------------------------
        // §3.4 行动经济（转译 TurnRules）
        // ------------------------------------------------------------------

        /// <summary>抛自己：thrown=true、canThrow=false，回合继续（canShoot 仍 true）。</summary>
        public static ActionState ThrowSelf(ActionState state)
        {
            return TurnRules.ApplyThrowSelf(state);
        }

        /// <summary>用武器：fired=true、canThrow/canShoot=false → 回合结束。</summary>
        public static ActionState UseWeapon(ActionState state)
        {
            return TurnRules.ApplyUseWeapon(state);
        }

        /// <summary>点 end go：canThrow/canShoot=false → 立即结束回合。</summary>
        public static ActionState EndGo(ActionState state)
        {
            return TurnRules.ApplyEndGo(state);
        }

        // ------------------------------------------------------------------
        // §3.3 回合交替 / 胜负 / 得分
        // ------------------------------------------------------------------

        /// <summary>下一队编号：1 ↔ 2（委托 TurnRules）。</summary>
        public static int NextTeamNumber(int currentTeamNumber)
        {
            return TurnRules.NextTeamNumber(currentTeamNumber);
        }

        /// <summary>对局是否已结束（任一方全灭）。</summary>
        public static bool IsMatchOver(bool team0AnyAlive, bool team1AnyAlive)
        {
            return TurnRules.IsMatchOver(team0AnyAlive, team1AnyAlive);
        }

        /// <summary>由存活情况与 AI 标志计算对局结果（委托 TurnRules.ComputeOutcome）。</summary>
        public static MatchOutcome ComputeOutcome(bool team0AnyAlive, bool team1AnyAlive, bool team1IsAI)
        {
            return TurnRules.ComputeOutcome(team0AnyAlive, team1AnyAlive, team1IsAI);
        }

        /// <summary>
        /// §3.3 / §7.3 1P 关卡得分：委托 <see cref="ScoreRules.LevelScore"/>。
        /// 2P 模式没有该得分口径，调用方应传 0 回合并忽略结果。
        /// </summary>
        public static int ComputeLevelScore(float team0AverageHealth, int team0TotalTurnsTaken, int levelNumber)
        {
            return ScoreRules.LevelScore(team0AverageHealth, team0TotalTurnsTaken, levelNumber);
        }
    }
}
