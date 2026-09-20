using NUnit.Framework;
using PirateCrew.Combat;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="BattleFlowRules"/> 测试。
    /// 覆盖：§3.1 inactivity 边界（10 vs 11）与两路分支、AI 看门狗推进决策（审计 §一.3）、
    /// §3.2 保底武器判定、§3.4 行动经济三路径、§3.3 胜负与得分。
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
        // AI 看门狗推进决策（审计 代码审计报告 §一.3：活动期间不得强制收尾）
        // ------------------------------------------------------------------

        [Test]
        public void DecideAiTick_ActivityBeatsTimeout_ResetsWatchdog()
        {
            // 弹体飞行 7-10s 属合法回合内容：即使看门狗早已超时，也必须重置而不是掐回合
            //（否则 OnTurnEnded 会把在飞弹体直接销毁）。
            Assert.AreEqual(AiTickDecision.ResetWatchdogAndWait,
                BattleFlowRules.DecideAiTick(
                    anythingActive: true, isThinking: false, watchdogFrames: 9999, timeoutFrames: 600));
        }

        [Test]
        public void DecideAiTick_ActivityWhileThinking_ResetsWatchdog()
        {
            Assert.AreEqual(AiTickDecision.ResetWatchdogAndWait,
                BattleFlowRules.DecideAiTick(
                    anythingActive: true, isThinking: true, watchdogFrames: 100, timeoutFrames: 600));
        }

        [Test]
        public void DecideAiTick_TimeoutWithoutActivity_ForcesResolve()
        {
            Assert.AreEqual(AiTickDecision.ForceResolve,
                BattleFlowRules.DecideAiTick(
                    anythingActive: false, isThinking: true, watchdogFrames: 601, timeoutFrames: 600));
        }

        [Test]
        public void DecideAiTick_AtExactTimeoutStillThinking_Waits()
        {
            // 严格大于才强制（与 inactivity > 10 同款边界口径）。
            Assert.AreEqual(AiTickDecision.WaitThinking,
                BattleFlowRules.DecideAiTick(
                    anythingActive: false, isThinking: true, watchdogFrames: 600, timeoutFrames: 600));
        }

        [Test]
        public void DecideAiTick_ThinkingWithoutActivity_Waits()
        {
            Assert.AreEqual(AiTickDecision.WaitThinking,
                BattleFlowRules.DecideAiTick(
                    anythingActive: false, isThinking: true, watchdogFrames: 10, timeoutFrames: 600));
        }

        [Test]
        public void DecideAiTick_IdleNotThinking_Proceeds()
        {
            Assert.AreEqual(AiTickDecision.Proceed,
                BattleFlowRules.DecideAiTick(
                    anythingActive: false, isThinking: false, watchdogFrames: 10, timeoutFrames: 600));
        }

        // ------------------------------------------------------------------
        // §3.2 保底武器判定
        // ------------------------------------------------------------------

        [Test]
        public void ShouldGrantFallbackWeapon_EmptyInventory_Grants()
        {
            Assert.IsTrue(BattleFlowRules.ShouldGrantFallbackWeapon(weaponCount: 0));
        }

        [Test]
        public void ShouldGrantFallbackWeapon_HasWeapons_NoGrant()
        {
            Assert.IsFalse(BattleFlowRules.ShouldGrantFallbackWeapon(weaponCount: 2));
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
        // §3.3 回合交替
        // ------------------------------------------------------------------

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
