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
    /// 回合开始时需要对单个角色做的重置集合（§3.2 <c>Team.startTurn</c>）。
    /// </summary>
    public readonly struct CharacterTurnReset
    {
        /// <summary>行动经济复位（thrown/fired=false，canThrow/canShoot=true）。</summary>
        public readonly ActionState Action;

        /// <summary>邪恶度复位（§3.2 <c>c.evilness = 0</c>）。</summary>
        public readonly int Evilness;

        /// <summary>背包为空，需要补保底武器（§3.2/§5.5 cannonball）。</summary>
        public readonly bool NeedsFallbackWeapon;

        public CharacterTurnReset(ActionState action, int evilness, bool needsFallbackWeapon)
        {
            Action = action;
            Evilness = evilness;
            NeedsFallbackWeapon = needsFallbackWeapon;
        }
    }

    /// <summary>
    /// 单支队伍的回合状态机（纯 C#，可无头测试）。
    ///
    /// 【对应章节】§3.2（startTurn / select / isTurnComplete / continueTurn / finishTurn）、
    ///             §4.3（<b>每回合只有 1 个角色行动</b>：<c>selectedCharacter</c> 是单值）。
    ///
    /// 行动经济规则不在此重复实现，统一委托 <see cref="TurnRules"/>。
    /// </summary>
    public sealed class TeamTurnTracker
    {
        /// <summary>队伍编号（1 = 红队，2 = 蓝队）。</summary>
        public int TeamNumber { get; }

        /// <summary>本回合选中角色在队伍列表中的索引；-1 = 尚未选择。</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>该队累计消耗的回合数（§3.3 得分公式）。</summary>
        public int TotalTurnsTaken { get; private set; }

        /// <summary>当前选中角色的行动经济状态。</summary>
        public ActionState Action { get; private set; } = ActionState.Start;

        /// <summary>本回合是否已选出唯一行动角色。</summary>
        public bool HasSelection => SelectedIndex >= 0;

        public TeamTurnTracker(int teamNumber)
        {
            TeamNumber = teamNumber;
        }

        /// <summary>§3.2 startTurn：totalTurnsTaken++、清 selectedCharacter、行动经济复位。</summary>
        public void StartTurn()
        {
            TotalTurnsTaken++;
            SelectedIndex = -1;
            Action = ActionState.Start;
        }

        /// <summary>
        /// §3.2 select：选中本回合唯一行动角色。
        /// <paramref name="again"/> = true 表示 continueTurn 的"同一角色做第 2 个动作"。
        /// 存活校验失败、或本回合已选过别的角色（且非 again）时返回 false —— 这条即
        /// "<b>每回合每队只有 1 个角色行动</b>" 的强制点。
        /// </summary>
        public bool Select(int index, bool alive, bool again = false)
        {
            if (!alive)
                return false;                 // 原版 select() 内部拒绝 !alive
            if (HasSelection && !again)
                return false;                 // 本回合已锁定角色，不允许换人
            if (index < 0)
                return false;

            SelectedIndex = index;
            if (!again)
                Action = ActionState.Start;   // 首次选择：清 thrown/throwFinished/weaponSelected
            return true;
        }

        /// <summary>§3.2 isTurnComplete：已选角色且行动经济耗尽（或角色死亡）。</summary>
        public bool IsTurnComplete(bool selectedAlive)
        {
            if (!HasSelection)
                return false;                 // 还没选人 → 回合尚未开始动作
            return TurnRules.IsTurnComplete(selectedAlive, Action);
        }

        /// <summary>抛自己（§3.4）：canThrow=false，回合继续。</summary>
        public void ApplyThrowSelf()
        {
            Action = TurnRules.ApplyThrowSelf(Action);
        }

        /// <summary>用武器（§3.4）：canThrow/canShoot 全 false，回合结束。</summary>
        public void ApplyUseWeapon()
        {
            Action = TurnRules.ApplyUseWeapon(Action);
        }

        /// <summary>点 end go（§3.4）：立即结束回合。</summary>
        public void ApplyEndGo()
        {
            Action = TurnRules.ApplyEndGo(Action);
        }

        /// <summary>§3.2 finishTurn：清 selectedCharacter。</summary>
        public void FinishTurn()
        {
            SelectedIndex = -1;
        }
    }

    /// <summary>
    /// 战斗推进/行动经济的纯规则汇总层。
    ///
    /// 【架构】本类<b>只做转译与组合</b>，不重复实现 <see cref="TurnRules"/> 里已有的规则；
    ///         TurnRules 缺失的部分（回合开始重置集合、经验分、分路决策）在此补齐并说明。
    ///         全部静态/纯 C#，不依赖 MonoBehaviour / GameObject。
    ///
    /// 【对应章节】§3.1（inactivity &gt; 10 两路分支）、§3.2（startTurn / finishTurn / isTurnComplete /
    ///             continueTurn / 保底武器 / evilness 复位）、§3.3（回合交替、胜负、得分）、
    ///             §3.4（投自己/用武器/end go 三路径行动经济）。
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
        // §3.2 回合开始重置
        // ------------------------------------------------------------------

        /// <summary>§3.2/§5.5 保底武器判定：<c>hasWeapons.length &lt; 1</c>。</summary>
        public static bool ShouldGrantFallbackWeapon(int weaponCount)
        {
            return weaponCount < 1;
        }

        /// <summary>
        /// §3.2 <c>startTurn</c> 对每个角色要做的重置：
        /// <c>canShoot = true; canThrow = true; weaponSelected = false; weaponLocked = false; evilness = 0</c>，
        /// 若背包为空则标记需要补保底 weapon（cannonball）。
        /// （TurnRules 只建模了行动经济、没有 evilness / 保底武器，故在此补齐。）
        /// </summary>
        public static CharacterTurnReset BeginCharacterTurn(int weaponCount)
        {
            return new CharacterTurnReset(
                ActionState.Start,
                evilness: 0,
                needsFallbackWeapon: ShouldGrantFallbackWeapon(weaponCount));
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
