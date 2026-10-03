using NUnit.Framework;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// InteractionRules 测试 = 交互操作契约 §A/§C 的可执行形式：
    /// 两态状态机迁移表（T1–T8）与 Esc 矩阵逐条钉死。
    /// </summary>
    [TestFixture]
    public class InteractionRulesTests
    {
        // ------------------------------------------------------------------
        // 迁移表（交互操作契约 §A：T1–T8）
        // ------------------------------------------------------------------

        [Test]
        public void T1_FreeCamera_SelectUnit_GoesSelectedIdle()
        {
            Assert.AreEqual(
                InteractionState.SelectedIdle,
                InteractionRules.Advance(InteractionState.FreeCamera, InteractionTrigger.SelectUnit));
        }

        [Test]
        public void T2_SelectedIdle_Deselect_GoesFreeCamera()
        {
            Assert.AreEqual(
                InteractionState.FreeCamera,
                InteractionRules.Advance(InteractionState.SelectedIdle, InteractionTrigger.Deselect));
        }

        [Test]
        public void T3_SelectedIdle_SwitchUnit_StaysSelectedIdle()
        {
            // 换人：状态不变、单位换（合法性由 TurnRules.CanSwitchSelection 把关，不在此层）
            Assert.AreEqual(
                InteractionState.SelectedIdle,
                InteractionRules.Advance(InteractionState.SelectedIdle, InteractionTrigger.SwitchUnit));
        }

        [Test]
        public void T4_SelectedIdle_StartOperation_GoesOperationActive()
        {
            Assert.AreEqual(
                InteractionState.OperationActive,
                InteractionRules.Advance(InteractionState.SelectedIdle, InteractionTrigger.StartOperation));
        }

        [Test]
        public void T5_OperationActive_CancelOperation_GoesSelectedIdle()
        {
            Assert.AreEqual(
                InteractionState.SelectedIdle,
                InteractionRules.Advance(InteractionState.OperationActive, InteractionTrigger.CancelOperation));
        }

        [Test]
        public void T6_OperationActive_ConfirmOperation_GoesExecuting()
        {
            Assert.AreEqual(
                InteractionState.Executing,
                InteractionRules.Advance(InteractionState.OperationActive, InteractionTrigger.ConfirmOperation));
        }

        [Test]
        public void T7_Executing_SettledCanAct_GoesSelectedIdle()
        {
            // 抛自己落定后仍有武器行动 → 回浏览（continueTurn）
            Assert.AreEqual(
                InteractionState.SelectedIdle,
                InteractionRules.Advance(InteractionState.Executing, InteractionTrigger.SettledCanAct));
        }

        [Test]
        public void T8_Executing_SettledDone_GoesFreeCamera()
        {
            // 用武器/结束回合 → 回合推进 → 自由镜头
            Assert.AreEqual(
                InteractionState.FreeCamera,
                InteractionRules.Advance(InteractionState.Executing, InteractionTrigger.SettledDone));
        }

        [Test]
        public void SelectedIdle_EndGo_GoesFreeCamera()
        {
            Assert.AreEqual(
                InteractionState.FreeCamera,
                InteractionRules.Advance(InteractionState.SelectedIdle, InteractionTrigger.EndGo));
        }

        [Test]
        public void OperationActive_Deselect_GoesFreeCamera()
        {
            // 编排层清理路径（单位死亡/退场）：操作中直接回自由镜头
            Assert.AreEqual(
                InteractionState.FreeCamera,
                InteractionRules.Advance(InteractionState.OperationActive, InteractionTrigger.Deselect));
        }

        [Test]
        public void IllegalTriggers_KeepState()
        {
            // 防御式：非法组合保持原状态（如自由镜头按确认、执行中开操作）
            Assert.AreEqual(InteractionState.FreeCamera,
                InteractionRules.Advance(InteractionState.FreeCamera, InteractionTrigger.ConfirmOperation));
            Assert.AreEqual(InteractionState.FreeCamera,
                InteractionRules.Advance(InteractionState.FreeCamera, InteractionTrigger.StartOperation));
            Assert.AreEqual(InteractionState.Executing,
                InteractionRules.Advance(InteractionState.Executing, InteractionTrigger.StartOperation));
            Assert.AreEqual(InteractionState.Executing,
                InteractionRules.Advance(InteractionState.Executing, InteractionTrigger.None));
            Assert.AreEqual(InteractionState.OperationActive,
                InteractionRules.Advance(InteractionState.OperationActive, InteractionTrigger.SelectUnit));
        }

        // ------------------------------------------------------------------
        // Esc 矩阵（交互操作契约 §C：优先级自上而下）
        // ------------------------------------------------------------------

        [Test]
        public void EscC1_ConfirmDialogOpen_ClosesDialog_EvenWhilePausedOrOperating()
        {
            Assert.AreEqual(EscAction.CloseDialog,
                InteractionRules.ResolveEsc(confirmDialogOpen: true, paused: true,
                    InteractionState.OperationActive));
            Assert.AreEqual(EscAction.CloseDialog,
                InteractionRules.ResolveEsc(true, false, InteractionState.FreeCamera));
        }

        [Test]
        public void EscC2_PausedNotDialog_Resumes()
        {
            Assert.AreEqual(EscAction.ResumePause,
                InteractionRules.ResolveEsc(false, true, InteractionState.OperationActive));
            Assert.AreEqual(EscAction.ResumePause,
                InteractionRules.ResolveEsc(false, true, InteractionState.FreeCamera));
        }

        [Test]
        public void EscC3_OperationActive_CancelsOperation()
        {
            Assert.AreEqual(EscAction.CancelOperation,
                InteractionRules.ResolveEsc(false, false, InteractionState.OperationActive));
        }

        [Test]
        public void EscC4_SelectedIdle_Deselects()
        {
            Assert.AreEqual(EscAction.DeselectUnit,
                InteractionRules.ResolveEsc(false, false, InteractionState.SelectedIdle));
        }

        [Test]
        public void EscC5_FreeCamera_OpensPause()
        {
            Assert.AreEqual(EscAction.OpenPause,
                InteractionRules.ResolveEsc(false, false, InteractionState.FreeCamera));
            // 执行中不归 Esc 层管（落定由活动性判定驱动），兜底也是开暂停
            Assert.AreEqual(EscAction.OpenPause,
                InteractionRules.ResolveEsc(false, false, InteractionState.Executing));
        }

        // ------------------------------------------------------------------
        // 相机归属（相机行为契约 #1 的纯函数形式）
        // ------------------------------------------------------------------

        [Test]
        public void CameraInput_OnlyInFreeCameraAndSelectedIdle()
        {
            Assert.IsTrue(InteractionRules.CameraAcceptsInput(InteractionState.FreeCamera));
            Assert.IsTrue(InteractionRules.CameraAcceptsInput(InteractionState.SelectedIdle));
            Assert.IsFalse(InteractionRules.CameraAcceptsInput(InteractionState.OperationActive),
                "操作中相机冻结（定义性特征）");
            Assert.IsFalse(InteractionRules.CameraAcceptsInput(InteractionState.Executing));
        }

        [Test]
        public void OperationActiveFlag_MatchesState()
        {
            Assert.IsTrue(InteractionRules.IsOperationActive(InteractionState.OperationActive));
            Assert.IsFalse(InteractionRules.IsOperationActive(InteractionState.SelectedIdle));
            Assert.IsFalse(InteractionRules.IsOperationActive(InteractionState.FreeCamera));
        }
    }
}
