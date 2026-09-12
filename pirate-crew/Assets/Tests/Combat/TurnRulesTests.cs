using NUnit.Framework;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// TurnRules 测试。规则出自逆向文档 §3.1（inactivity &gt; 10）、§3.2（isTurnComplete）、
    /// §3.3（回合交替与胜负）、§3.4（行动经济）。
    /// </summary>
    [TestFixture]
    public class TurnRulesTests
    {
        // ------------------------------------------------------------------
        // IsTurnComplete（§3.2）
        // ------------------------------------------------------------------

        [Test]
        public void IsTurnComplete_SelectedDead_IsTrue()
        {
            // 选中的角色死亡 → 回合必然结束（哪怕状态位还是 true）
            Assert.IsTrue(TurnRules.IsTurnComplete(false, true, true));
        }

        [Test]
        public void IsTurnComplete_BothActionsAvailable_IsFalse()
        {
            Assert.IsFalse(TurnRules.IsTurnComplete(true, true, true));
        }

        [Test]
        public void IsTurnComplete_OnlyThrowAvailable_IsFalse()
        {
            // canThrow 仍为 true → 回合继续（抛自己之后的状态）
            Assert.IsFalse(TurnRules.IsTurnComplete(true, true, false));
        }

        [Test]
        public void IsTurnComplete_OnlyShootAvailable_IsFalse()
        {
            // canShoot 仍为 true → 回合继续
            Assert.IsFalse(TurnRules.IsTurnComplete(true, false, true));
        }

        [Test]
        public void IsTurnComplete_NoActionsLeft_IsTrue()
        {
            Assert.IsTrue(TurnRules.IsTurnComplete(true, false, false));
        }

        // ------------------------------------------------------------------
        // inactivity 阈值（§3.1：严格大于 10）
        // ------------------------------------------------------------------

        [Test]
        public void InactivityExceeded_TenFrames_IsFalse()
        {
            // 原版 if inactivity > 10 → 恰好 10 不触发
            Assert.IsFalse(TurnRules.InactivityExceeded(10));
        }

        [Test]
        public void InactivityExceeded_ElevenFrames_IsTrue()
        {
            Assert.IsTrue(TurnRules.InactivityExceeded(11));
        }

        [Test]
        public void ShouldAdvanceTurn_TenFrames_DoesNotAdvance()
        {
            // 边界：10 帧不推进，即使回合已完成
            Assert.IsFalse(TurnRules.ShouldAdvanceTurn(10, true));
        }

        [Test]
        public void ShouldAdvanceTurn_ElevenFrames_Advances()
        {
            Assert.IsTrue(TurnRules.ShouldAdvanceTurn(11, true));
        }

        [Test]
        public void ShouldAdvanceTurn_ElevenFramesButTurnIncomplete_DoesNotAdvance()
        {
            // 未完成 → 走 continueTurn 分支，不推进到下一队
            Assert.IsFalse(TurnRules.ShouldAdvanceTurn(11, false));
            // 同一时刻的另一路：让同一角色继续第 2 个动作
            Assert.IsTrue(TurnRules.ShouldContinueTurn(11, false));
            Assert.IsFalse(TurnRules.ShouldContinueTurn(11, true));
        }

        [Test]
        public void ShouldAdvanceTurn_CustomThreshold()
        {
            Assert.IsFalse(TurnRules.ShouldAdvanceTurn(10, true, threshold: 10));
            Assert.IsTrue(TurnRules.ShouldAdvanceTurn(5, true, threshold: 4));
            Assert.IsFalse(TurnRules.ShouldAdvanceTurn(4, true, threshold: 4));
        }

        // ------------------------------------------------------------------
        // 行动经济（§3.4）三条路径
        // ------------------------------------------------------------------

        [Test]
        public void ApplyThrowSelf_SetsThrownAndDisablesThrowOnly()
        {
            // 抛自己：thrown=true、canThrow=false；canShoot 保持 true → 回合继续
            var next = TurnRules.ApplyThrowSelf(ActionState.Start);

            Assert.IsTrue(next.Thrown);
            Assert.IsFalse(next.Fired);
            Assert.IsFalse(next.CanThrow);
            Assert.IsTrue(next.CanShoot);
            Assert.IsFalse(TurnRules.IsTurnComplete(true, next), "抛自己后回合未结束");
        }

        [Test]
        public void ApplyThrowSelf_ThenUseWeapon_EndsTurn()
        {
            // 路径：抛自己 → 用武器。用武器后 canThrow/canShoot 均 false → 回合结束
            var afterThrow = TurnRules.ApplyThrowSelf(ActionState.Start);
            var afterWeapon = TurnRules.ApplyUseWeapon(afterThrow);

            Assert.IsTrue(afterWeapon.Thrown);
            Assert.IsTrue(afterWeapon.Fired);
            Assert.IsFalse(afterWeapon.CanThrow);
            Assert.IsFalse(afterWeapon.CanShoot);
            Assert.IsTrue(TurnRules.IsTurnComplete(true, afterWeapon));
        }

        [Test]
        public void ApplyUseWeapon_EndsTurnImmediately()
        {
            // 用武器即结束回合：Thrown 保持 false（未抛自己），Fired=true，两个 can 均 false
            var next = TurnRules.ApplyUseWeapon(ActionState.Start);

            Assert.IsFalse(next.Thrown);
            Assert.IsTrue(next.Fired);
            Assert.IsFalse(next.CanThrow);
            Assert.IsFalse(next.CanShoot);
            Assert.IsTrue(TurnRules.IsTurnComplete(true, next));
        }

        [Test]
        public void ApplyEndGo_DisablesBothActions()
        {
            // end go：canThrow=false、canShoot=false → 立即结束；Thrown/Fired 不参与经济，保持不变
            var next = TurnRules.ApplyEndGo(ActionState.Start);

            Assert.IsFalse(next.CanThrow);
            Assert.IsFalse(next.CanShoot);
            Assert.IsFalse(next.Thrown);
            Assert.IsFalse(next.Fired);
            Assert.IsTrue(TurnRules.IsTurnComplete(true, next));
        }

        [Test]
        public void ApplyThrowSelf_WhenAlreadyUsedWeapon_KeepsShootDisabled()
        {
            // 用武器后 canShoot 已 false；再抛自己也不应把它恢复
            var afterWeapon = TurnRules.ApplyUseWeapon(ActionState.Start);
            var afterThrow = TurnRules.ApplyThrowSelf(afterWeapon);

            Assert.IsTrue(afterThrow.Thrown);
            Assert.IsFalse(afterThrow.CanThrow);
            Assert.IsFalse(afterThrow.CanShoot);
            Assert.IsTrue(TurnRules.IsTurnComplete(true, afterThrow));
        }

        // ------------------------------------------------------------------
        // 回合交替与胜负（§3.3）
        // ------------------------------------------------------------------

        [Test]
        public void NextTeamNumber_Alternates()
        {
            Assert.AreEqual(2, TurnRules.NextTeamNumber(1));
            Assert.AreEqual(1, TurnRules.NextTeamNumber(2));
        }

        [Test]
        public void IsMatchOver_OnlyWhenSomeoneIsWipedOut()
        {
            Assert.IsFalse(TurnRules.IsMatchOver(true, true));
            Assert.IsTrue(TurnRules.IsMatchOver(false, true));
            Assert.IsTrue(TurnRules.IsMatchOver(true, false));
            Assert.IsTrue(TurnRules.IsMatchOver(false, false));
        }

        [Test]
        public void ComputeOutcome_1P_PlayerSurvivesAiWipedOut_Win()
        {
            Assert.AreEqual(MatchOutcome.Team0Win, TurnRules.ComputeOutcome(true, false, team1IsAI: true));
        }

        [Test]
        public void ComputeOutcome_1P_PlayerWipedOut_Lose()
        {
            Assert.AreEqual(MatchOutcome.LevelFailed, TurnRules.ComputeOutcome(false, true, team1IsAI: true));
        }

        [Test]
        public void ComputeOutcome_1P_BothWipedOut_RareTotalLoss()
        {
            // §3.3：1P 下玩家全灭且 AI 也全灭 → 罕见全灭，仍算关卡失败
            Assert.AreEqual(MatchOutcome.LevelFailed, TurnRules.ComputeOutcome(false, false, team1IsAI: true));
        }

        [Test]
        public void ComputeOutcome_2P_Team0Wins()
        {
            Assert.AreEqual(MatchOutcome.Team0Win, TurnRules.ComputeOutcome(true, false, team1IsAI: false));
        }

        [Test]
        public void ComputeOutcome_2P_Team1Wins()
        {
            Assert.AreEqual(MatchOutcome.Team1Win, TurnRules.ComputeOutcome(false, true, team1IsAI: false));
        }

        [Test]
        public void ComputeOutcome_2P_BothWipedOut_Draw()
        {
            // §3.3：2P 热座同归于尽 → vs_draw
            Assert.AreEqual(MatchOutcome.Draw, TurnRules.ComputeOutcome(false, false, team1IsAI: false));
        }

        [Test]
        public void ComputeOutcome_NotOver_ReturnsDrawPlaceholder()
        {
            // 双方都存活（未满足 IsMatchOver 前提）→ 无胜负，返回 Draw
            Assert.AreEqual(MatchOutcome.Draw, TurnRules.ComputeOutcome(true, true, team1IsAI: false));
            Assert.AreEqual(MatchOutcome.Draw, TurnRules.ComputeOutcome(true, true, team1IsAI: true));
        }
    }
}
