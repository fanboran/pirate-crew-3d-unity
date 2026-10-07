using System;
using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Audio;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Combat;
using PirateCrew.Data;
using PirateCrew.UI.Stick;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗 HUD（两态重构版，2026-10-03 裁决：三模式开关退役，交互状态由
    /// <see cref="BattleInteractionController"/> 唯一持有）。
    ///
    /// 【信息架构】
    ///   · **顶栏双队合成血条**：左右屏缘各一条，每名存活单位 = 一段分格（受击只掉自己那段，
    ///     白色 damage ghost 残影延迟回落），条下一排小方格 pips（存活空格 / 阵亡「×」）；
    ///   · **中央回合徽章**：theme 金面钮 + 数字，回合切换弹跳；
    ///   · **操作菜单**（选中·浏览态弹出，原武器面板转型）：17 武器各占一格文字钮，
    ///     跳跃 / 结束回合为文字按钮；
    ///   · **操作 HUD**（操作中弹出，与操作菜单互斥）：方向/仰角/力度三读数 + 发射/取消钮；
    ///   · **提示条**：按交互状态刷新键位提示（自由镜头/浏览/操作中/执行中/暂停）；
    ///   · 暂停 / 返回 = 文字钮（P 键与 Esc 矩阵由交互控制器解析，经本类的公开口开合面板）。
    ///
    /// 【架构约定】引用一律 <c>[SerializeField]</c>（<c>BattleUiTheme.WireHud</c> 回写）；
    /// 状态刷新全部 EventBus 事件驱动；皮肤 / 字号 / 颜色一律 <see cref="UiSkin"/> Token。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class BattleHud : MonoBehaviour
    {
        /// <summary>操作菜单头条的 HP 条（凹槽 + ghost + fill）。</summary>
        [Serializable]
        public sealed class HpBarView
        {
            public Image track;
            public Image ghost;
            public Image fill;
        }

        // ------------------------------------------------------------------
        // 序列化引用
        // ------------------------------------------------------------------

        [Header("战场引用")]
        [SerializeField] BattleController battle;
        [SerializeField] TurnManager turnManager;
        [SerializeField] BattleInteractionController interaction;

        [Header("顶栏双队血条")]
        [SerializeField] TeamBarView teamBarRed;
        [SerializeField] TeamBarView teamBarBlue;

        [Header("回合徽章（中央）")]
        [SerializeField] Image badgeRing;
        [SerializeField] MaskableGraphic badgeText;
        [SerializeField] MaskableGraphic turnHintText;

        [Header("操作菜单（贴底居中，选中·浏览态）")]
        [SerializeField] GameObject weaponPanelRoot;
        [SerializeField] MaskableGraphic unitNameText;
        [SerializeField] HpBarView unitHpBar;
        [SerializeField] MaskableGraphic weaponNameText;
        [SerializeField] MaskableGraphic weaponDescText;
        [Tooltip("17 个武器图标格按钮，索引 = WeaponId 枚举值。")]
        [SerializeField] Button[] weaponButtons = new Button[17];
        [Tooltip("与 weaponButtons 一一对应的格底（染色 / 选中金）。")]
        [SerializeField] Image[] weaponFrames = new Image[17];
        [SerializeField] Button throwSelfButton;
        [SerializeField] Button endGoButton;

        [Header("操作 HUD（贴底居中，操作中态；与操作菜单互斥）")]
        [Tooltip("操作面板根（读数行 + 发射/取消钮）。")]
        [SerializeField] GameObject operationPanelRoot;
        [SerializeField] MaskableGraphic yawReadoutText;
        [SerializeField] MaskableGraphic elevationReadoutText;
        [SerializeField] MaskableGraphic powerReadoutText;
        [SerializeField] Button confirmOperationButton;
        [SerializeField] Button cancelOperationButton;

        [Header("提示条与系统按钮")]
        [SerializeField] SketchButton hintPlate;
        [SerializeField] MaskableGraphic hintText;
        [SerializeField] SketchButton stateIndicator;
        [SerializeField] MaskableGraphic stateIndicatorLabel;
        [SerializeField] Button backButton;
        [SerializeField] Button pauseButton;

        [Header("暂停面板")]
        [SerializeField] GameObject pausePanelRoot;
        [SerializeField] RectTransform pauseCard;
        [SerializeField] Button resumeButton;
        [SerializeField] Button pauseRestartButton;
        [SerializeField] Button pauseBackButton;

        [Header("返回确认弹窗")]
        [SerializeField] GameObject confirmDialogRoot;
        [SerializeField] RectTransform confirmCard;
        [SerializeField] MaskableGraphic confirmMessage;
        [SerializeField] Button confirmOkButton;
        [SerializeField] Button confirmCancelButton;

        [Header("结算面板")]
        [SerializeField] GameObject settlementPanelRoot;
        [SerializeField] RectTransform settlementCard;
        [SerializeField] MaskableGraphic settlementTitleText;
        [SerializeField] MaskableGraphic settlementLinesText;
        [SerializeField] Image[] settlementStars = new Image[3];
        [SerializeField] Button settlementRestartButton;
        [SerializeField] Button settlementBackButton;

        // ------------------------------------------------------------------
        // 运行时状态
        // ------------------------------------------------------------------

        /// <summary>这一局是否战役局（结算面板按它决定显示哪些行）。</summary>
        bool _campaignBattle;

        /// <summary>对局第 N 手（每次 TurnStarted 频道触发递增；原版 §3 无回合上限）。</summary>
        int _turnNumber;

        UiMotion _motion;
        CanvasGroup _panelGroup;
        RectTransform _panelRect;
        bool _panelVisible;
        /// <summary>首次状态直接落位（不打动效、不出声），之后的翻转才播动效。</summary>
        bool _panelResolved;

        /// <summary>操作读数上一次显示的三元组（变化才写文本，避免逐帧写字符串）。</summary>
        int _lastReadoutYaw = int.MinValue;
        int _lastReadoutElevation = int.MinValue;
        int _lastReadoutPower = int.MinValue;

        /// <summary>当前操作名（状态条第一行用，交互操作契约 §G1；空串 = 非操作中）。</summary>
        string _operationLabel = string.Empty;

        // ------------------------------------------------------------------
        // 装配自检（供 PlayMode 结构断言）
        // ------------------------------------------------------------------

        /// <summary>HUD 是否已接好核心战场引用。</summary>
        public bool HasCoreReferences => battle != null && turnManager != null && interaction != null;

        /// <summary>武器槽位数（应为 17）。</summary>
        public int WeaponSlotCount => weaponButtons != null ? weaponButtons.Length : 0;

        /// <summary>17 个武器格是否全部接好。</summary>
        public bool HasWeaponWiring
        {
            get
            {
                if (weaponButtons == null || weaponButtons.Length != 17)
                    return false;
                if (weaponFrames == null || weaponFrames.Length != 17)
                    return false;

                for (int i = 0; i < 17; i++)
                {
                    if (weaponButtons[i] == null || weaponFrames[i] == null)
                        return false;
                }
                return true;
            }
        }

        /// <summary>操作 HUD（读数 + 发射/取消）是否接好。</summary>
        public bool HasOperationWiring =>
            operationPanelRoot != null && yawReadoutText != null && elevationReadoutText != null
            && powerReadoutText != null && confirmOperationButton != null && cancelOperationButton != null;

        /// <summary>
        /// 交互控制器是否来自**装配期注入**（PlayMode 装配测试读它；兜底成功不算过关）。
        /// </summary>
        public bool InteractionWiredByAssembly { get; private set; }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();
            InteractionWiredByAssembly = interaction != null;
            if (interaction == null)
            {
                interaction = FindObjectOfType<BattleInteractionController>();
                Log.Warn("[BattleHud] interaction 未经装配接线，已一次性兜底解析"
                         + (interaction != null ? "成功" : "失败（Esc 矩阵的 UI 层将降级）")
                         + "。修复：跑 PirateCrew.EditorTools.BattleLookupWiring.Wire（写 Battle.unity）。");
            }

            WireWeaponButtons();
            WireCommandButtons();
        }

        void OnEnable()
        {
            // Esc 矩阵的 UI 层接线（BattleUiBridge 跨程序集门面；订阅成对增删）。
            BattleUiBridge.OpenPauseRequested += OpenPause;
            BattleUiBridge.ResumePauseRequested += ClosePause;
            BattleUiBridge.CloseDialogRequested += CloseConfirmDialog;

            EventBus.Subscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Subscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Subscribe(BattleEvents.SelectionChanged, OnSelectionChanged);
            EventBus.Subscribe(BattleEvents.OperationChanged, OnOperationChanged);
        }

        void OnDisable()
        {
            // 离场兜底：暂停中直接回主菜单/选关，绝不能把 timeScale=0 带出战斗场景。
            BattlePause.ForceResume();
            BattleUiBridge.ConfirmDialogOpen = false;
            BattleUiBridge.OpenPauseRequested -= OpenPause;
            BattleUiBridge.ResumePauseRequested -= ClosePause;
            BattleUiBridge.CloseDialogRequested -= CloseConfirmDialog;

            EventBus.Unsubscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Unsubscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Unsubscribe(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Unsubscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Unsubscribe(BattleEvents.SelectionChanged, OnSelectionChanged);
            EventBus.Unsubscribe(BattleEvents.OperationChanged, OnOperationChanged);
        }

        void Update()
        {
            // 操作读数只在操作中刷新（值变化才写文本）。
            if (interaction != null && interaction.IsOperationActive)
                RefreshOperationReadout();
        }

        void WireWeaponButtons()
        {
            if (weaponButtons == null)
                return;

            for (int i = 0; i < weaponButtons.Length; i++)
            {
                if (weaponButtons[i] == null)
                    continue;

                int weaponId = i;   // 闭包捕获局部副本
                weaponButtons[i].onClick.AddListener(() => OnWeaponClicked(weaponId));
            }
        }

        void WireCommandButtons()
        {
            if (throwSelfButton != null)
                throwSelfButton.onClick.AddListener(OnThrowSelfClicked);
            if (endGoButton != null)
                endGoButton.onClick.AddListener(OnEndGoClicked);
            if (backButton != null)
                backButton.onClick.AddListener(ShowBackConfirm);
            if (pauseButton != null)
                pauseButton.onClick.AddListener(OnPauseButtonClicked);
            if (resumeButton != null)
                resumeButton.onClick.AddListener(ClosePause);
            if (pauseRestartButton != null)
                pauseRestartButton.onClick.AddListener(RestartBattle);
            if (pauseBackButton != null)
                pauseBackButton.onClick.AddListener(ShowBackConfirm);
            if (confirmOkButton != null)
                confirmOkButton.onClick.AddListener(ConfirmLeaveBattle);
            if (confirmCancelButton != null)
                confirmCancelButton.onClick.AddListener(CloseConfirmDialog);
            if (settlementRestartButton != null)
                settlementRestartButton.onClick.AddListener(RestartBattle);
            if (settlementBackButton != null)
                settlementBackButton.onClick.AddListener(OnBackClicked);
            if (confirmOperationButton != null)
                confirmOperationButton.onClick.AddListener(OnConfirmOperationClicked);
            if (cancelOperationButton != null)
                cancelOperationButton.onClick.AddListener(OnCancelOperationClicked);
        }

        // ------------------------------------------------------------------
        // 玩家命令
        // ------------------------------------------------------------------

        /// <summary>按钮统一反馈：UiClick（失败时 UiError）+ punch 缩放。</summary>
        void ButtonFeedback(Button button, bool success)
        {
            AudioService.PlayUi(success ? SfxId.UiClick : SfxId.UiError);
            if (button != null && success && _motion != null)
                _motion.Punch(button.image != null ? button.image : button.GetComponent<Graphic>(),
                    UiMotionRules.PunchSeconds);
        }

        void OnWeaponClicked(int weaponId)
        {
            if (interaction == null)
                return;

            bool selected = interaction.SelectWeapon((WeaponId)weaponId);
            ButtonFeedback(weaponButtons != null && weaponId >= 0 && weaponId < weaponButtons.Length
                ? weaponButtons[weaponId]
                : null, selected);
            if (selected)
                RefreshWeaponPanel();
        }

        void OnThrowSelfClicked()
        {
            if (interaction == null)
                return;

            ButtonFeedback(throwSelfButton, success: true);
            interaction.SelectThrowSelf();
        }

        void OnEndGoClicked()
        {
            if (interaction == null)
                return;

            ButtonFeedback(endGoButton, success: true);
            interaction.EndGo();
        }

        void OnConfirmOperationClicked()
        {
            if (interaction == null)
                return;

            ButtonFeedback(confirmOperationButton, success: true);
            interaction.ConfirmOperationFromUi();
        }

        void OnCancelOperationClicked()
        {
            if (interaction == null)
                return;

            ButtonFeedback(cancelOperationButton, success: true);
            interaction.CancelOperation();
        }

        void OnBackClicked()
        {
            ButtonFeedback(settlementBackButton != null && settlementBackButton.interactable
                ? settlementBackButton
                : backButton, success: true);
            EventBus.Publish(SceneEvents.GoBack);
        }

        // ------------------------------------------------------------------
        // 暂停 / 返回确认 / 结算 / 重开
        // ------------------------------------------------------------------

        void OnPauseButtonClicked()
        {
            ButtonFeedback(pauseButton, success: true);
            OpenPause();
        }

        /// <summary>进入暂停：timeScale=0 + BattlePause 状态位（冻逐帧计数，见 BattlePause 类注释）。</summary>
        void OpenPause()
        {
            if (BattlePause.IsPaused || (turnManager != null && battle != null && battle.IsMatchOver))
                return;

            BattlePause.Pause();
            RefreshWeaponPanel(hide: true);
            HideOperationPanel();
            OpenModal(pausePanelRoot, pauseCard);
            RefreshHint();
        }

        /// <summary>解除暂停（按钮 / Esc 矩阵 C2 / P 键经 BattleUiBridge 共用）。</summary>
        public void ClosePause()
        {
            if (!BattlePause.IsPaused)
                return;

            BattlePause.Resume();
            CloseModal(pausePanelRoot);
            AudioService.PlayUi(SfxId.UiClick);
            RefreshWeaponPanel();
            RefreshHint();
        }

        /// <summary>关闭返回确认弹窗（Esc 矩阵 C1 经 BattleUiBridge 的动作口）。</summary>
        public void CloseConfirmDialog()
        {
            BattleUiBridge.ConfirmDialogOpen = false;
            CloseModal(confirmDialogRoot);
        }

        /// <summary>再来一局：重载 Battle 场景（同名目标自动不压栈，栈顶返回点天然保住）。</summary>
        void RestartBattle()
        {
            AudioService.PlayUi(SfxId.UiClick);
            BattlePause.ForceResume();

            if (_campaignBattle && CampaignApi.LastSettlement != null
                && WorldMapRuntime.SetPending(CampaignApi.LastSettlement.Value.MapId))
            {
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
                return;
            }

            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
        }

        /// <summary>模态入场：Dim 淡入 + 卡片滑入 + pop 弹一下 + UiPanelOpen 音。</summary>
        void OpenModal(GameObject root, RectTransform card)
        {
            if (root == null)
                return;

            if (_motion != null)
            {
                _motion.ShowPanel(root, UiMotionRules.PanelShowSeconds, UiMotionRules.PanelSlideOffsetPixels);
                var cardImage = card != null ? card.GetComponent<Image>() : null;
                if (cardImage != null)
                    _motion.Pop(cardImage);
            }
            else
            {
                root.SetActive(true);
            }

            AudioService.PlayUi(SfxId.UiPanelOpen);
        }

        void CloseModal(GameObject root)
        {
            if (root == null)
                return;

            if (_motion != null)
                _motion.HidePanel(root, UiMotionRules.PanelHideSeconds);
            else
                root.SetActive(false);
        }

        /// <summary>返回主菜单/选关前先确认（结算面板的返回按钮不经过这里）。</summary>
        void ShowBackConfirm()
        {
            if (confirmDialogRoot == null)
            {
                OnBackClicked();
                return;
            }

            if (confirmMessage != null)
                UiTextUtil.SetText(confirmMessage, UiStrings.BackConfirm);
            BattleUiBridge.ConfirmDialogOpen = true;
            OpenModal(confirmDialogRoot, confirmCard);
        }

        void ConfirmLeaveBattle()
        {
            BattleUiBridge.ConfirmDialogOpen = false;
            CloseModal(confirmDialogRoot);
            BattlePause.ForceResume();
            AudioService.PlayUi(SfxId.UiClick);
            EventBus.Publish(SceneEvents.GoBack);
        }

        // ------------------------------------------------------------------
        // EventBus 回调
        // ------------------------------------------------------------------

        void OnBattleStarted(BattleStartedPayload payload)
        {
            // 载荷（关卡/队伍数）不进 HUD，只用“一局开始”这个时机重建面板。
            _turnNumber = 0;
            RefreshBadge();

            // BattleStarted 时仍有待结算关卡 = 这一局从选关进来（结算面板要显示星级/招募）。
            _campaignBattle = CampaignApi.HasPendingMap;

            // 重开一局经场景重载进来：清掉可能残留的暂停态与旧模态。
            BattlePause.ForceResume();
            CloseModal(settlementPanelRoot);
            CloseModal(confirmDialogRoot);
            CloseModal(pausePanelRoot);

            BuildTeamBars();
            RefreshTurnHint();
            RefreshWeaponPanel(hide: true);
            HideOperationPanel();
            RefreshHint();
        }

        void OnTurnStarted(TurnStartedPayload payload)
        {
            // 行动角色/镜头目标由相机层消费，HUD 只刷新回合徽章与队伍条。
            _turnNumber++;
            RefreshBadge(punch: true);
            RefreshTurnHint();
            RefreshTeamBars();
            RefreshWeaponPanel(hide: true);
            HideOperationPanel();
            RefreshHint();
        }

        void OnTurnEnded(int teamNumber)
        {
            // 队伍编号不进 HUD，只用“回合结束”这个时机收起操作面。
            RefreshWeaponPanel(hide: true);
            HideOperationPanel();
            RefreshHint();
        }

        void OnActionSelected(ActionSelectedPayload action)
        {
            // 动作种类不进 HUD（面板收起与队伍条刷新与种类无关）。
            RefreshWeaponPanel(hide: true);
            RefreshTeamBars();
            RefreshHint();
        }

        void OnSelectionChanged(SelectionChangedPayload payload)
        {
            // 选中/取消/换人/落定回浏览——操作菜单随之开合；读数面板只在操作中（OperationChanged 管）。
            RefreshWeaponPanel();
            RefreshHint();
        }

        void OnOperationChanged(OperationChangedPayload payload)
        {
            // 操作名进状态条（§G1）：跳跃 = 「跳跃」，武器 = 武器名。
            _operationLabel = payload.Active
                ? (payload.IsWeapon ? UiTextRules.WeaponName(payload.WeaponId) : UiStrings.BattleThrowSelf)
                : string.Empty;

            if (payload.Active)
            {
                RefreshWeaponPanel(hide: true);
                ShowOperationPanel();
                _lastReadoutYaw = int.MinValue;   // 强制下一次读数刷新
            }
            else
            {
                HideOperationPanel();
                // 取消操作回浏览 → 操作菜单重新弹出；进入执行则保持收起（等落定 SelectionChanged）。
                if (interaction != null && interaction.State == InteractionState.SelectedIdle)
                    RefreshWeaponPanel();
            }
            RefreshHint();
        }

        void OnCrewDamaged(CrewDamagedPayload damaged)
        {
            UpdateUnitSegment(damaged.TeamIndex, damaged.PirateId, damaged.Health, damaged.MaxHealth);
            RefreshTurnHint();
        }

        void OnCrewDied(CrewDiedPayload died)
        {
            UpdateUnitSegment(died.TeamIndex, died.PirateId, 0, CrewCatalog.MaxHealth);
            MarkPipDead(died.TeamIndex, died.PirateId);
            RefreshTurnHint();
        }

        void OnMatchFinished(MatchFinishedPayload finished)
        {
            if (turnHintText != null)
            {
                UiTextUtil.SetText(turnHintText, UiTextRules.OutcomeTitle(
                    (MatchOutcome)finished.Outcome, finished.Team1IsAi));
            }

            ShowSettlement(finished);
            RefreshWeaponPanel(hide: true);
            HideOperationPanel();
        }

        // ------------------------------------------------------------------
        // 回合徽章与提示
        // ------------------------------------------------------------------

        void RefreshBadge(bool punch = false)
        {
            if (badgeText != null)
                UiTextUtil.SetText(badgeText, Mathf.Max(1, _turnNumber).ToString());

            // 徽章外观固定为像素皮（暖金环 + 黄铜宝石），不再按队色乘色——
            // 队别由队血条 / 提示文字承载（像素件乘色会压平烘焙色阶）。
            if (punch && _motion != null && badgeRing != null)
                _motion.Pop(badgeRing);
        }

        void RefreshTurnHint()
        {
            BattleTeam team = turnManager != null ? turnManager.CurrentTeam : null;
            if (turnHintText == null)
                return;

            UiTextUtil.SetText(turnHintText, team != null
                ? UiTextRules.TurnHint(team.AiControlled, team.Number)
                : string.Empty);
        }

        /// <summary>
        /// 状态条（交互操作契约 §G）：两行式——第一行状态名（操作中含操作名），第二行该状态的
        /// 键位提示（§B 的玩家可读形式，文案与 §B 同源）；暂停态整体覆盖。
        /// </summary>
        void RefreshHint()
        {
            if (hintText == null)
                return;

            string title;
            string keys;
            if (BattlePause.IsPaused)
            {
                title = UiStrings.BattleStatePaused;
                keys = UiStrings.BattleHintPaused;
            }
            else if (interaction == null)
            {
                title = string.Empty;
                keys = string.Empty;
            }
            else
            {
                switch (interaction.State)
                {
                    case InteractionState.FreeCamera:
                        title = UiStrings.BattleStateFreeCamera;
                        keys = UiStrings.BattleHintFreeCamera;
                        break;
                    case InteractionState.SelectedIdle:
                        title = UiStrings.BattleStateSelected;
                        keys = UiStrings.BattleHintSelected;
                        break;
                    case InteractionState.OperationActive:
                        title = string.Format(UiStrings.BattleStateOperationFormat, _operationLabel);
                        keys = UiStrings.BattleHintOperation;
                        break;
                    case InteractionState.Executing:
                        title = UiStrings.BattleStateExecuting;
                        keys = UiStrings.BattleHintExecuting;
                        break;
                    default:
                        title = string.Empty;
                        keys = string.Empty;
                        break;
                }
            }

            // 状态指示钮（§G4）与状态条第一行同源：自由镜头灰面，其余态金面。
            if (stateIndicatorLabel != null)
                UiTextUtil.SetText(stateIndicatorLabel, title);
            if (stateIndicator != null)
                stateIndicator.Sticky = title.Length > 0 && title != UiStrings.BattleStateFreeCamera;

            // 自由镜头下整条状态盘隐藏（右上状态钮已表达状态——21 走查裁决：浅灰截断键位串
            // 是噪音）；暂停/选中/操作中/执行中显示（墨底盘 + 白字，压世界背景可读）。
            bool showStatusLine = interaction != null
                && (BattlePause.IsPaused || interaction.State != InteractionState.FreeCamera);
            if (hintPlate != null)
                hintPlate.gameObject.SetActive(showStatusLine);
            UiTextUtil.SetText(hintText, showStatusLine ? ComposeHintText(title, keys) : string.Empty);
        }

        /// <summary>两行合成：有状态名有键位 = 两行；只有其一 = 单行。</summary>
        static string ComposeHintText(string title, string keys)
        {
            if (title.Length > 0 && keys.Length > 0)
                return title + "\n" + keys;
            return title.Length > 0 ? title : keys;
        }

        // ------------------------------------------------------------------

        /// <summary>操作菜单显示条件：选中·浏览态 &amp;&amp; 角色存活 &amp;&amp; 当前队非 AI。</summary>
        void RefreshWeaponPanel(bool hide = false)
        {
            PirateBase selected = interaction != null ? interaction.SelectedCharacter : null;
            BattleTeam team = turnManager != null ? turnManager.CurrentTeam : null;

            bool visible = !hide
                && interaction != null
                && interaction.State == InteractionState.SelectedIdle
                && selected != null
                && selected.Alive
                && team != null
                && !team.AiControlled;

            SetWeaponPanelVisible(visible);

            if (!visible)
                return;

            // 右列头条：队色职业名 + HP 条。
            if (unitNameText != null)
            {
                UiTextUtil.SetText(unitNameText,
                    UiTextRules.CrewNameByBattleSymbol(selected.CrewType));
                // 【配色冻结（创始人 2026-10-05：不许再自作主张动色）】维持既有 TeamText 提亮档；
                // 任何配色调整先出截图给创始人过目再落。
                UiTextUtil.SetColor(unitNameText, UiSkin.TeamText(selected.TeamIndex));
            }

            if (unitHpBar != null && unitHpBar.fill != null)
            {
                if (_motion != null)
                    _motion.SetFillPairTarget(unitHpBar.fill, unitHpBar.ghost,
                        HealthRatio(selected.Health, selected.MaxHealth));
                else
                    unitHpBar.fill.rectTransform.anchorMax =
                        new Vector2(HealthRatio(selected.Health, selected.MaxHealth), 1f);
            }

            if (throwSelfButton != null)
                throwSelfButton.interactable = selected.CurrentAction.CanThrow;

            if (endGoButton != null)
                endGoButton.interactable = true;

            // 已装备的武器名 / 说明（未装备时留空——格子本身已表意）。
            WeaponInventory inventory = selected.Inventory;
            bool equipped = inventory != null && inventory.HasEquipped;
            var equippedId = equipped ? (WeaponId)inventory.EquippedIndex : (WeaponId)(-1);

            if (weaponNameText != null)
            {
                UiTextUtil.SetText(weaponNameText, equipped
                    ? UiTextRules.WeaponName(equippedId)
                    : string.Empty);
                UiTextUtil.SetColor(weaponNameText, equipped
                    ? PixelSkin.LightOf(PixelTone.Primary)
                    : UiSkin.WithAlpha(PixelSkin.PaperWhite, 0.72f));
            }

            if (weaponDescText != null)
                UiTextUtil.SetText(weaponDescText, equipped
                    ? UiTextRules.WeaponDescription(equippedId)
                    : string.Empty);

            if (weaponButtons == null)
                return;

            for (int i = 0; i < weaponButtons.Length; i++)
            {
                if (weaponButtons[i] == null)
                    continue;

                var id = (WeaponId)i;
                bool owned = inventory != null && inventory.Contains(id);
                // 未拥有 → 按钮禁用（theme disabled 双层影子字压暗，不烘黑图也不乘色）。
                weaponButtons[i].interactable = owned;

                // 格底状态：已装备 = 金面 sticky；其余 = 常态灰面。
                if (weaponFrames != null && i < weaponFrames.Length && weaponFrames[i] != null)
                {
                    bool chosen = equipped && i == (int)equippedId;
                    if (weaponButtons[i] is SketchButton sketch)
                        sketch.Sticky = chosen;
                    else
                        UiKit.ApplyThemeButton(weaponButtons[i], weaponFrames[i], sticky: chosen);
                }
            }
        }

        /// <summary>操作菜单显隐唯一入口：状态翻转才动效 + 音。</summary>
        void SetWeaponPanelVisible(bool visible)
        {
            if (_panelVisible == visible && _panelResolved)
                return;
            bool animate = _panelResolved;
            _panelVisible = visible;
            _panelResolved = true;

            if (weaponPanelRoot == null)
                return;

            if (_panelRect == null)
                _panelRect = weaponPanelRoot.transform as RectTransform;
            if (_panelGroup == null)
            {
                _panelGroup = weaponPanelRoot.GetComponent<CanvasGroup>();
                if (_panelGroup == null)
                    _panelGroup = weaponPanelRoot.AddComponent<CanvasGroup>();
            }

            if (visible)
            {
                if (animate && _motion != null)
                {
                    _motion.ShowPanel(weaponPanelRoot, UiMotionRules.PanelShowSeconds,
                        UiMotionRules.PanelSlideOffsetPixels);
                    AudioService.PlayUi(SfxId.UiPanelOpen);
                }
                else
                {
                    weaponPanelRoot.SetActive(true);
                    _panelGroup.alpha = 1f;
                    _panelGroup.interactable = true;
                    _panelGroup.blocksRaycasts = true;
                }
            }
            else
            {
                if (animate && _motion != null)
                    _motion.HidePanel(weaponPanelRoot, UiMotionRules.PanelHideSeconds);
                else
                    weaponPanelRoot.SetActive(false);
            }
        }

        // ------------------------------------------------------------------
        // 操作 HUD（操作中态）
        // ------------------------------------------------------------------

        /// <summary>操作面板弹出（OperationChanged Active=true 时）。</summary>
        void ShowOperationPanel()
        {
            if (operationPanelRoot == null)
                return;

            if (_motion != null)
            {
                _motion.ShowPanel(operationPanelRoot, UiMotionRules.PanelShowSeconds,
                    UiMotionRules.PanelSlideOffsetPixels);
                AudioService.PlayUi(SfxId.UiPanelOpen);
            }
            else
                operationPanelRoot.SetActive(true);

            RefreshOperationReadout(force: true);
        }

        /// <summary>操作面板收起（取消/进入执行）。</summary>
        void HideOperationPanel()
        {
            if (operationPanelRoot == null)
                return;

            if (_motion != null && operationPanelRoot.activeSelf)
                _motion.HidePanel(operationPanelRoot, UiMotionRules.PanelHideSeconds);
            else if (operationPanelRoot.activeSelf)
                operationPanelRoot.SetActive(false);
        }

        /// <summary>三参数读数（方向/仰角/力度），值变化才写文本。</summary>
        void RefreshOperationReadout(bool force = false)
        {
            if (interaction == null || yawReadoutText == null)
                return;

            ThrowParams p = interaction.CurrentParams;
            int yaw = UiTextRules.YawRounded(p.YawDegrees);
            int elevation = UiTextRules.ElevationRounded(p.ElevationDegrees);
            int power = UiTextRules.Percent(p.Power);

            if (force || yaw != _lastReadoutYaw)
            {
                UiTextUtil.SetText(yawReadoutText, yaw + "°");
                _lastReadoutYaw = yaw;
            }
            if (force || elevation != _lastReadoutElevation)
            {
                UiTextUtil.SetText(elevationReadoutText, UiTextRules.ElevationDegrees(p.ElevationDegrees));
                _lastReadoutElevation = elevation;
            }
            if (force || power != _lastReadoutPower)
            {
                UiTextUtil.SetText(powerReadoutText, UiTextRules.StrengthPercent(p.Power));
                _lastReadoutPower = power;
            }
        }
    }
}
