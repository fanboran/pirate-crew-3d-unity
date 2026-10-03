using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗交互状态机的状态（见 <see cref="InteractionRules.Advance"/>）。
    ///
    /// 两态模型（创始人裁决 2026-10-03）：自由镜头 ⇄ 选中；选中态内分
    /// 浏览 / 操作中 / 执行中三个子状态。规格唯一出处 =
    /// docs/技术/交互操作契约.md §A；设计意图见 docs/设计/操作与交互.md §2。
    /// </summary>
    public enum InteractionState
    {
        /// <summary>自由镜头：未选中任何单位，编辑器飞行式观察战场（默认起态）。</summary>
        FreeCamera = 0,

        /// <summary>选中 · 浏览：唯一单位为视野中心（可环绕），等待选择操作。</summary>
        SelectedIdle = 1,

        /// <summary>选中 · 操作中：操作模组激活，相机锁定，等待瞄准/确认。</summary>
        OperationActive = 2,

        /// <summary>执行中：弹体/被抛角色飞行（相机跟随），等待落定。</summary>
        Executing = 3,
    }

    /// <summary>驱动 <see cref="InteractionState"/> 迁移的触发源（由编排层从意图帧翻译）。</summary>
    public enum InteractionTrigger
    {
        /// <summary>无触发。</summary>
        None = 0,

        /// <summary>自由镜头下左键点中己方存活单位。</summary>
        SelectUnit = 1,

        /// <summary>取消选中（Esc / 编排层清理）。</summary>
        Deselect = 2,

        /// <summary>换选中（未行动时点其他己方单位；合法性由 <see cref="PirateCrew.Combat.TurnRules.CanSwitchSelection"/> 把关）。</summary>
        SwitchUnit = 3,

        /// <summary>选择操作（点武器格/跳跃）。</summary>
        StartOperation = 4,

        /// <summary>取消操作（Esc）。</summary>
        CancelOperation = 5,

        /// <summary>确认执行（回车/屏幕确认钮）。</summary>
        ConfirmOperation = 6,

        /// <summary>执行落定且行动角色仍有行动（抛自己后 CanShoot 仍在）。</summary>
        SettledCanAct = 7,

        /// <summary>执行落定且无剩余行动（用武器/结束回合），回合推进。</summary>
        SettledDone = 8,

        /// <summary>直接结束回合（end go）。</summary>
        EndGo = 9,
    }

    /// <summary>Esc 矩阵的输入情形（唯一裁决表，交互操作契约 §C）。</summary>
    public enum EscContext
    {
        /// <summary>无任何可退的层（不应出现——编排层兜底按自由镜头处理）。</summary>
        None = 0,

        /// <summary>确认弹窗开着。</summary>
        ConfirmDialog = 1,

        /// <summary>暂停中。</summary>
        Paused = 2,

        /// <summary>操作中（模组激活）。</summary>
        OperationActive = 3,

        /// <summary>选中 · 浏览。</summary>
        SelectedIdle = 4,

        /// <summary>自由镜头。</summary>
        FreeCamera = 5,
    }

    /// <summary>Esc 矩阵的解析结果（对一层退栈动作）。</summary>
    public enum EscAction
    {
        /// <summary>关闭确认弹窗。</summary>
        CloseDialog = 0,

        /// <summary>解除暂停。</summary>
        ResumePause = 1,

        /// <summary>取消操作回浏览（模组参数丢弃）。</summary>
        CancelOperation = 2,

        /// <summary>取消选中回自由镜头。</summary>
        DeselectUnit = 3,

        /// <summary>打开暂停菜单。</summary>
        OpenPause = 4,
    }

    /// <summary>
    /// 两态交互的纯规则层（无头可测）。
    ///
    /// 【分层】本类不引用 MonoBehaviour / GameObject，可在无头验证台直接断言；
    /// 编排薄壳 = <see cref="BattleInteractionController"/>（采样意图 → 翻译触发源 → 调本类 → 应用结果）。
    ///
    /// 【规格出处】docs/技术/交互操作契约.md（改行为先改契约，再动实现，同步测试）。
    /// </summary>
    public static class InteractionRules
    {
        /// <summary>
        /// 状态机一步迁移。纯函数：非法组合一律保持原状态（防御式，编排层负责只发合法触发）。
        /// 迁移表逐条对应交互操作契约 §A 的 T1–T8。
        /// </summary>
        public static InteractionState Advance(InteractionState state, InteractionTrigger trigger)
        {
            switch (state)
            {
                case InteractionState.FreeCamera:
                    if (trigger == InteractionTrigger.SelectUnit)
                        return InteractionState.SelectedIdle;
                    return InteractionState.FreeCamera;

                case InteractionState.SelectedIdle:
                    switch (trigger)
                    {
                        case InteractionTrigger.Deselect:
                        case InteractionTrigger.EndGo:
                            return InteractionState.FreeCamera;
                        case InteractionTrigger.SwitchUnit:
                            return InteractionState.SelectedIdle;   // 换人：状态不变、单位换
                        case InteractionTrigger.StartOperation:
                            return InteractionState.OperationActive;
                        default:
                            return InteractionState.SelectedIdle;
                    }

                case InteractionState.OperationActive:
                    switch (trigger)
                    {
                        case InteractionTrigger.CancelOperation:
                            return InteractionState.SelectedIdle;
                        case InteractionTrigger.ConfirmOperation:
                            return InteractionState.Executing;
                        case InteractionTrigger.Deselect:
                            return InteractionState.FreeCamera;     // 编排层清理（如单位死亡）
                        default:
                            return InteractionState.OperationActive;
                    }

                case InteractionState.Executing:
                    if (trigger == InteractionTrigger.SettledCanAct)
                        return InteractionState.SelectedIdle;
                    if (trigger == InteractionTrigger.SettledDone)
                        return InteractionState.FreeCamera;
                    return InteractionState.Executing;

                default:
                    return InteractionState.FreeCamera;
            }
        }

        /// <summary>
        /// Esc 矩阵解析（唯一裁决表，交互操作契约 §C）：自上而下取第一个命中情形。
        /// 情形由编排层汇总（弹窗开 > 暂停 > 操作中 > 已选中 > 自由镜头），
        /// 本函数按优先级重排后映射——即使编排层传乱序上下文集合也保持裁决序。
        /// </summary>
        public static EscAction ResolveEsc(bool confirmDialogOpen, bool paused, InteractionState state)
        {
            if (confirmDialogOpen)
                return EscAction.CloseDialog;
            if (paused)
                return EscAction.ResumePause;
            if (state == InteractionState.OperationActive)
                return EscAction.CancelOperation;
            if (state == InteractionState.SelectedIdle)
                return EscAction.DeselectUnit;
            return EscAction.OpenPause;
        }

        /// <summary>交互状态对应的相机模式归属（相机行为契约 #1 的纯函数形式）。</summary>
        public static bool CameraAcceptsInput(InteractionState state)
        {
            // 自由镜头（飞行观察）与选中浏览（环绕）吃相机输入；操作中冻结、执行中交跟随。
            return state == InteractionState.FreeCamera || state == InteractionState.SelectedIdle;
        }

        /// <summary>当前是否处于「操作模组激活」态（挂机保护/inactivity 清零的判定口径）。</summary>
        public static bool IsOperationActive(InteractionState state)
        {
            return state == InteractionState.OperationActive;
        }
    }
}
