using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="BattleFlowRules"/> / <see cref="TeamTurnTracker"/> 测试。
    /// 覆盖：§3.1 inactivity 边界（10 vs 11）与两路分支、§3.2 回合开始重置/保底武器、
    /// §3.4 行动经济三路径、§4.3「每回合每队只有 1 个角色行动」、§3.3 胜负与得分。
    /// </summary>
    [TestFixture]
    public class BattleFlowRulesTests
    {
        // ------------------------------------------------------------------
        // §3.1 inactivity 边界 + 两路分支
        // ------------------------------------------------------------------

        [Test]
        public void DecideAdvance_TenFrames_IsWait()
        {
            // 原版 if inactivity > 10：恰好 10 不触发。
            Assert.AreEqual(TurnAdvanceDecision.Wait,
                BattleFlowRules.DecideAdvance(10, turnComplete: true));
        }

        [Test]
        public void DecideAdvance_ElevenFrames_TurnComplete_Advances()
        {
            Assert.AreEqual(TurnAdvanceDecision.AdvanceTurn,
                BattleFlowRules.DecideAdvance(11, turnComplete: true));
        }

        [Test]
        public void DecideAdvance_ElevenFrames_NotComplete_Continues()
        {
            Assert.AreEqual(TurnAdvanceDecision.ContinueTurn,
                BattleFlowRules.DecideAdvance(11, turnComplete: false));
        }

        [Test]
        public void InactivityThreshold_ReusesTurnRulesConstant()
        {
            Assert.AreEqual(TurnRules.DefaultInactivityThreshold, BattleFlowRules.InactivityThreshold);
            Assert.AreEqual(10, BattleFlowRules.InactivityThreshold);
        }

        // ------------------------------------------------------------------
        // §3.2 回合开始重置 / 保底武器
        // ------------------------------------------------------------------

        [Test]
        public void BeginCharacterTurn_EmptyInventory_FlagsFallback()
        {
            CharacterTurnReset reset = BattleFlowRules.BeginCharacterTurn(weaponCount: 0);
            Assert.IsTrue(reset.NeedsFallbackWeapon);
            Assert.AreEqual(0, reset.Evilness, "§3.2 evilness 每回合清零");
            Assert.IsTrue(reset.Action.CanThrow);
            Assert.IsTrue(reset.Action.CanShoot);
            Assert.IsFalse(reset.Action.Thrown);
            Assert.IsFalse(reset.Action.Fired);
        }

        [Test]
        public void BeginCharacterTurn_HasWeapons_NoFallback()
        {
            CharacterTurnReset reset = BattleFlowRules.BeginCharacterTurn(weaponCount: 2);
            Assert.IsFalse(reset.NeedsFallbackWeapon);
        }

        // ------------------------------------------------------------------
        // §3.4 行动经济三路径
        // ------------------------------------------------------------------

        [Test]
        public void ThrowSelf_LeavesCanShootTrue()
        {
            // 抛自己：thrown=true、canThrow=false，但 canShoot 仍 true → 回合继续。
            ActionState after = BattleFlowRules.ThrowSelf(ActionState.Start);
            Assert.IsTrue(after.Thrown);
            Assert.IsFalse(after.CanThrow);
            Assert.IsTrue(after.CanShoot, "抛自己后仍可用武器（回合继续）");
        }

        [Test]
        public void ThrowSelfThenUseWeapon_EndsTurn()
        {
            ActionState afterThrow = BattleFlowRules.ThrowSelf(ActionState.Start);
            ActionState afterWeapon = BattleFlowRules.UseWeapon(afterThrow);
            Assert.IsTrue(afterWeapon.Fired);
            Assert.IsFalse(afterWeapon.CanThrow);
            Assert.IsFalse(afterWeapon.CanShoot);
        }

        [Test]
        public void UseWeaponDirectly_EndsTurn()
        {
            // 用武器即结束回合（canThrow 也置 false）。
            ActionState after = BattleFlowRules.UseWeapon(ActionState.Start);
            Assert.IsFalse(after.CanThrow);
            Assert.IsFalse(after.CanShoot);
        }

        [Test]
        public void EndGo_ImmediatelyEndsTurn()
        {
            ActionState after = BattleFlowRules.EndGo(ActionState.Start);
            Assert.IsFalse(after.CanThrow);
            Assert.IsFalse(after.CanShoot);
        }

        [Test]
        public void IsTurnComplete_TracksActionState()
        {
            Assert.IsFalse(BattleFlowRules.IsTurnComplete(true, ActionState.Start));
            Assert.IsFalse(BattleFlowRules.IsTurnComplete(true, BattleFlowRules.ThrowSelf(ActionState.Start)));
            Assert.IsTrue(BattleFlowRules.IsTurnComplete(true, BattleFlowRules.UseWeapon(ActionState.Start)));
            Assert.IsTrue(BattleFlowRules.IsTurnComplete(false, ActionState.Start), "选中角色死亡 → 回合结束");
        }

        // ------------------------------------------------------------------
        // §4.3 每回合每队只有 1 个角色行动
        // ------------------------------------------------------------------

        [Test]
        public void TeamTurnTracker_OnlyOneCharacterPerTurn()
        {
            var tracker = new TeamTurnTracker(1);
            tracker.StartTurn();

            Assert.AreEqual(1, tracker.TotalTurnsTaken, "startTurn 使 totalTurnsTaken++");
            Assert.IsFalse(tracker.HasSelection);

            Assert.IsTrue(tracker.Select(0, alive: true));
            Assert.AreEqual(0, tracker.SelectedIndex);

            // 换人应被拒绝（本回合已锁定角色 0）。
            Assert.IsFalse(tracker.Select(1, alive: true), "每回合每队只能有 1 个角色行动");

            // continueTurn：允许再次选中同一角色做第 2 个动作。
            Assert.IsTrue(tracker.Select(0, alive: true, again: true));
        }

        [Test]
        public void TeamTurnTracker_SelectDeadCharacter_Rejected()
        {
            var tracker = new TeamTurnTracker(1);
            tracker.StartTurn();
            Assert.IsFalse(tracker.Select(0, alive: false), "原版 select() 拒绝死亡角色");
            Assert.IsFalse(tracker.HasSelection);
        }

        [Test]
        public void TeamTurnTracker_TurnCompleteLifecycle()
        {
            var tracker = new TeamTurnTracker(2);
            tracker.StartTurn();
            Assert.IsFalse(tracker.IsTurnComplete(true), "未选角色 → 回合未完成");

            tracker.Select(0, alive: true);
            tracker.ApplyThrowSelf();
            Assert.IsFalse(tracker.IsTurnComplete(true), "抛自己后回合继续");

            tracker.ApplyUseWeapon();
            Assert.IsTrue(tracker.IsTurnComplete(true), "用武器后回合完成");

            tracker.FinishTurn();
            Assert.IsFalse(tracker.HasSelection);
        }

        [Test]
        public void NextTeamNumber_Alternates()
        {
            Assert.AreEqual(2, BattleFlowRules.NextTeamNumber(1));
            Assert.AreEqual(1, BattleFlowRules.NextTeamNumber(2));
        }

        // ------------------------------------------------------------------
        // §3.3 胜负与得分
        // ------------------------------------------------------------------

        [Test]
        public void ComputeOutcome_OnePlayerMode()
        {
            // 1P：team1IsAI = true；玩家（team0）全灭 → LevelFailed。
            Assert.AreEqual(MatchOutcome.Team0Win, BattleFlowRules.ComputeOutcome(true, false, true));
            Assert.AreEqual(MatchOutcome.LevelFailed, BattleFlowRules.ComputeOutcome(false, true, true));
            Assert.AreEqual(MatchOutcome.LevelFailed, BattleFlowRules.ComputeOutcome(false, false, true));
        }

        [Test]
        public void ComputeOutcome_TwoPlayerMode()
        {
            Assert.AreEqual(MatchOutcome.Team0Win, BattleFlowRules.ComputeOutcome(true, false, false));
            Assert.AreEqual(MatchOutcome.Team1Win, BattleFlowRules.ComputeOutcome(false, true, false));
            Assert.AreEqual(MatchOutcome.Draw, BattleFlowRules.ComputeOutcome(false, false, false));
        }

        [Test]
        public void ComputeLevelScore_UsesFlashFormula()
        {
            // §3.3: floor(avgHealth*20 - turns*25)，下限 level*10。
            // avg=80, turns=3, level=1 → floor(1600-75)=1525，下限 10 → 1525
            Assert.AreEqual(1525, BattleFlowRules.ComputeLevelScore(80f, 3, 1));

            // avg=5, turns=10, level=3 → floor(100-250)=-150，下限 30 → 30
            Assert.AreEqual(30, BattleFlowRules.ComputeLevelScore(5f, 10, 3));

            // avg=100, turns=0 → 2000
            Assert.AreEqual(2000, BattleFlowRules.ComputeLevelScore(100f, 0, 1));
        }
    }
}
