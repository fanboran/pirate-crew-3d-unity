using System;
using System.Collections.Generic;
using System.Text;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Audio;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗 HUD（UGUI，翻译自 Godot <c>battle_hud.tscn</c> + <c>scripts/ui/hud.gd</c>）。
    ///
    /// 【布局依据】docs/UI-UX与中文本地化规范.md §3.5 线框图 + §2.3 元素清单：
    ///   顶部（小地图 / 模式开关 / 回合计数 / 双方存活）、屏幕中心准星、
    ///   左下船员名册、底部中央武器面板（返回按钮移左下）、底部操作提示。
    ///   具体节点由 <c>Assets/Editor/BattleHudBuilder.cs</c> 构建，本类只做数据绑定与刷新。
    ///
    /// 【本波次改造】
    ///   · 全中文（<see cref="UiStrings"/> / <see cref="UiTextRules"/>），不再出现
    ///     "Player N, take your turn" / "Roster — Level N" / "T1 redPirate" 等英文；
    ///   · 回合提示与存活拆双方，修正旧实现「只统计当前行动队」的缺陷（验收 V10）；
    ///   · 文本字段类型为 <see cref="MaskableGraphic"/>（TMP 与 legacy Text 的共同基类）：
    ///     新装配写入的是 <see cref="TextMeshProUGUI"/>，而 <c>M2BattleSceneSetup.cs</c>
    ///     （本波次禁改、由并行波次占用）仍会写入 legacy Text，用共同基类才能两边都编译。
    ///     运行期文本读写统一走 <see cref="UiTextUtil"/>。
    ///
    /// 【架构约定】
    ///   · 所有引用走 <c>[SerializeField]</c>（由装配脚本程序化接好），不做 GameObject.Find；
    ///   · 状态刷新全部由 <see cref="EventBus"/> 的 <see cref="BattleEvents"/> 事件驱动，不做 Update 轮询；
    ///   · 事件契约不变：只发布既有的 "go_back"。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleHud : MonoBehaviour
    {
        /// <summary>返回主菜单事件名（与 Core/SceneLoader 的 go_back 约定一致）。</summary>
        const string GoBackEvent = "go_back";

        /// <summary>EventBus 场景切换事件名（与 Core/SceneLoader 约定一致；同场景重载由 SceneLoader 自动不压栈）。</summary>
        const string ChangeSceneEvent = "change_scene";

        /// <summary>§4.1 血条总帧数（28 帧）。</summary>
        const int HealthBarFrames = 28;

        /// <summary>
        /// 名册队色名的文字色（r7：由 <c>UiTheme.TeamColor × 0.75</c> 改为写死的高对比提亮色）。
        ///
        /// 【r6 复验】旧写法把队伍色压暗 0.75 后压在深木底 #3A2A1E 上，实测 WCAG 对比度只有
        /// **3.76（红）/ 2.80（蓝）**，均低于正文所需的 4.5:1。
        /// 【r7 修法】改为**提亮混合**的固定色、不再压暗：红 **#FF8A7A**、蓝 **#7FB0FF**。
        /// 【WCAG 2.1 计算（相对亮度 L；背景 #3A2A1E → L=0.0265）】
        ///   · #FF8A7A：L=0.4084 → (0.4084+0.05)/(0.0265+0.05) = **5.99:1** ✓ ≥4.5
        ///   · #7FB0FF：L=0.4279 → (0.4279+0.05)/(0.0265+0.05) = **6.25:1** ✓ ≥4.5
        /// alpha 固定 1（<c>Color</c> 的 <c>*</c> 会连 alpha 一起乘，文字不能半透明）。
        /// </summary>
        static readonly Color RosterNameRed = new Color(0xFF / 255f, 0x8A / 255f, 0x7A / 255f, 1f);
        static readonly Color RosterNameBlue = new Color(0x7F / 255f, 0xB0 / 255f, 0xFF / 255f, 1f);

        /// <summary>名册最多显示的行数（当前转写关卡最大 12 人）。</summary>
        const int MaxRosterRows = 12;

        /// <summary>名册单行控件集合（由装配脚本程序化创建并接线）。</summary>
        [Serializable]
        public sealed class RosterRowView
        {
            public GameObject root;
            public Image teamSwatch;
            public MaskableGraphic nameLabel;
            public Image healthFill;
            public MaskableGraphic healthLabel;
        }

        // ------------------------------------------------------------------
        // 序列化引用（场景内直连）
        // ------------------------------------------------------------------

        [Header("战场引用")]
        [Tooltip("战斗组装根；用于取全部出战角色构建名册、以及双方存活计数。")]
        [SerializeField] BattleController battle;
        [Tooltip("回合驱动器；用于取当前队伍与 AI 判定。")]
        [SerializeField] TurnManager turnManager;
        [Tooltip("瞄准控制器；武器/抛自己/结束回合命令的出口。")]
        [SerializeField] AimThrowController aimController;

        [Header("回合提示（顶部）")]
        [SerializeField] MaskableGraphic turnHintText;
        [SerializeField] MaskableGraphic teamStatusText;

        [Header("武器面板（底部中央）")]
        [SerializeField] GameObject weaponPanelRoot;
        [SerializeField] MaskableGraphic weaponPanelTitle;
        [Tooltip("17 个武器按钮，索引 = WeaponId 枚举值。")]
        [SerializeField] Button[] weaponButtons = new Button[17];
        [Tooltip("与 weaponButtons 一一对应的按钮文本。")]
        [SerializeField] MaskableGraphic[] weaponLabels = new MaskableGraphic[17];
        [SerializeField] Button throwSelfButton;
        [SerializeField] Button endGoButton;

        [Header("名册（左下）")]
        [SerializeField] MaskableGraphic rosterTitle;
        [SerializeField] RosterRowView[] rosterRows = new RosterRowView[MaxRosterRows];

        [Header("返回")]
        [SerializeField] Button backButton;

        [Header("暂停（发布收口）")]
        [SerializeField] Button pauseButton;
        [SerializeField] GameObject pausePanelRoot;
        [SerializeField] Button resumeButton;
        [SerializeField] Button pauseRestartButton;
        [SerializeField] Button pauseBackButton;

        [Header("返回确认弹窗")]
        [SerializeField] GameObject confirmDialogRoot;
        [SerializeField] MaskableGraphic confirmMessage;
        [SerializeField] Button confirmOkButton;
        [SerializeField] Button confirmCancelButton;

        [Header("结算面板（发布收口）")]
        [SerializeField] GameObject settlementPanelRoot;
        [SerializeField] MaskableGraphic settlementTitleText;
        [SerializeField] MaskableGraphic settlementLinesText;
        [Tooltip("三颗星图标，索引 0-2；未得星压暗，得星点亮黄铜色。")]
        [SerializeField] Image[] settlementStars = new Image[3];
        [SerializeField] Button settlementRestartButton;
        [SerializeField] Button settlementBackButton;

        // ------------------------------------------------------------------
        // 运行时状态
        // ------------------------------------------------------------------

        PirateBase[] _pirateByRow = new PirateBase[MaxRosterRows];

        /// <summary>这一局是否战役局（BattleStarted 时有待结算关卡）。结算面板按它决定显示哪些行。</summary>
        bool _campaignBattle;

        // ---- 动效与 UI 音效（规则在 UiMotionRules，驱动在 UiMotion；数值为提案/待定） ----
        UiMotion _motion;
        CanvasGroup _panelGroup;
        RectTransform _panelRect;
        bool _panelVisible;
        /// <summary>首次状态直接落位（不打动效、不出声），之后的翻转才播动效。</summary>
        bool _panelResolved;

        /// <summary>HUD 是否已接好核心战场引用（供装配自检测试断言）。</summary>
        public bool HasCoreReferences => battle != null && turnManager != null && aimController != null;

        /// <summary>武器槽位数（应为 17）。</summary>
        public int WeaponSlotCount => weaponButtons != null ? weaponButtons.Length : 0;

        /// <summary>名册行数（应为 12，超出部分在 BuildRoster 时隐藏）。</summary>
        public int RosterRowCount => rosterRows != null ? rosterRows.Length : 0;

        /// <summary>17 个武器按钮/文本是否全部接好（供装配自检测试断言）。</summary>
        public bool HasWeaponWiring
        {
            get
            {
                if (weaponButtons == null || weaponButtons.Length != 17)
                    return false;
                if (weaponLabels == null || weaponLabels.Length != 17)
                    return false;

                for (int i = 0; i < weaponButtons.Length; i++)
                {
                    if (weaponButtons[i] == null || weaponLabels[i] == null)
                        return false;
                }
                return true;
            }
        }

        /// <summary>名册行的控件是否全部接好（供装配自检测试断言）。</summary>
        public bool HasRosterWiring
        {
            get
            {
                if (rosterRows == null || rosterRows.Length == 0)
                    return false;

                for (int i = 0; i < rosterRows.Length; i++)
                {
                    RosterRowView row = rosterRows[i];
                    if (row == null || row.root == null || row.teamSwatch == null
                        || row.nameLabel == null || row.healthFill == null || row.healthLabel == null)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        // ------------------------------------------------------------------
        // 生命周期：只做订阅/退订与按钮绑定
        // ------------------------------------------------------------------

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();
            WireWeaponButtons();
            WireCommandButtons();

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
                confirmCancelButton.onClick.AddListener(HideConfirmDialog);
            if (settlementRestartButton != null)
                settlementRestartButton.onClick.AddListener(RestartBattle);
            if (settlementBackButton != null)
                settlementBackButton.onClick.AddListener(OnBackClicked);

            RefreshWeaponPanel(hide: true);
            RefreshRoster();
            RefreshTurnHint();
            InitModeSystem();
        }

        // ------------------------------------------------------------------
        // 模式系统（r12 用户裁决：不同模式左键语义不同；顶栏可点击 + 快捷键 1/2/3）
        //
        // 【移动】左键=选角色；按住角色拖拽=跳跃；拖空白=转视角。
        // 【操作】炮台模式：AD 转向、WS 力度、左键/空格=开火。
        // 【观察】我的世界同款：鼠标移动=转视角，无任何游戏点击（准星只在此时显示）。
        // 【例外声明】本类原约定"事件驱动不 Update"——输入模式轮询无法事件化，特此豁免。
        // ------------------------------------------------------------------

        public enum BattleHudMode { Move, Act, Observe }

        BattleHudMode _mode = BattleHudMode.Move;
        Transform _moveSeg, _actSeg, _observeSeg, _crosshair;
        MaskableGraphic _hintText;
        MaskableGraphic _turnCounterText;
        BattleCameraController _cameraController;

        /// <summary>对局第 N 手（每次 turn_started 递增；原版 §3 无回合上限，故不带满值）。</summary>
        int _turnNumber;

        void InitModeSystem()
        {
            // 【r12 事故修】BattleHud 组件挂在 canvas 下独立的 "BattleHud" 节点上，
            // 而 TopBar/准星/提示条是 canvas 的**兄弟分支**——从自己子树找全是 null，
            // 模式系统整个瞎掉（点击无反应/准星关不掉/无提示）。必须从 Canvas 根全树找。
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            Transform searchRoot = parentCanvas != null ? parentCanvas.transform : transform;
            _moveSeg = DeepFind(searchRoot, "MoveSegment");
            _actSeg = DeepFind(searchRoot, "ActionSegment");
            _observeSeg = DeepFind(searchRoot, "ObserveSegment");
            _crosshair = DeepFind(searchRoot, "Crosshair");
            _hintText = DeepFind(searchRoot, "HintText") != null
                ? DeepFind(searchRoot, "HintText").GetComponent<MaskableGraphic>()
                : null;
            // 回合计数槽与模式开关同属 TopBar（跨分支），走同一条 DeepFind 通道
            // （SerializeField 由 BattleUiTheme 接线，新增槽先走运行时查找兜底）。
            _turnCounterText = DeepFind(searchRoot, "TurnCounterText") != null
                ? DeepFind(searchRoot, "TurnCounterText").GetComponent<MaskableGraphic>()
                : null;

            BindModeButton(_moveSeg, BattleHudMode.Move);
            BindModeButton(_actSeg, BattleHudMode.Act);
            BindModeButton(_observeSeg, BattleHudMode.Observe);

            SetHudMode(BattleHudMode.Move);
        }

        void BindModeButton(Transform seg, BattleHudMode mode)
        {
            if (seg == null)
                return;
            var button = seg.GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(() =>
                {
                    ButtonFeedback(button, success: true);
                    SetHudMode(mode);
                });
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                SetHudMode(BattleHudMode.Move);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                SetHudMode(BattleHudMode.Act);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                SetHudMode(BattleHudMode.Observe);
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Esc 优先级：暂停中→恢复；观察模式→退出观察（r12 口径，提示条有写）；
                // 瞄准中→不抢（AimThrowController 用 Esc 取消瞄准）；否则→打开暂停。
                if (BattlePause.IsPaused)
                    ClosePause();
                else if (_mode == BattleHudMode.Observe)
                    SetHudMode(BattleHudMode.Move);
                else if (aimController == null || !aimController.IsAiming)
                    OpenPause();
            }

            // 暂停时冻结瞄准输入（Esc/点击都不再进 AimThrow；恢复时按当前模式还原）。
            if (aimController != null && BattlePause.IsPaused)
                aimController.InputEnabled = false;

            // 【观察模式】点击=准星点选角色；命中即选中并自动返回移动模式（r12 用户裁决）。
            if (_mode == BattleHudMode.Observe && Input.GetMouseButtonDown(0)
                && aimController != null && aimController.HandleObserveClick())
                SetHudMode(BattleHudMode.Move);
        }

        void SetHudMode(BattleHudMode mode)
        {
            _mode = mode;

            if (_cameraController == null)
                _cameraController = FindObjectOfType<BattleCameraController>();
            if (_cameraController != null)
                _cameraController.SetObserveMode(mode == BattleHudMode.Observe);
            if (aimController != null)
            {
                aimController.SetWeaponPreference(mode == BattleHudMode.Act);
                aimController.InputEnabled = mode != BattleHudMode.Observe;
            }

            RefreshModeSegments();
            RefreshModeHint();
            RefreshCrosshair();
        }

        /// <summary>准星只属于观察模式（r12 用户裁决）；只在模式切换时刷一次，不再每帧 SetActive。</summary>
        void RefreshCrosshair()
        {
            if (_crosshair == null)
                return;

            bool visible = _mode == BattleHudMode.Observe;
            if (_crosshair.gameObject.activeSelf != visible)
                _crosshair.gameObject.SetActive(visible);
        }

        void RefreshModeSegments()
        {
            ApplySegmentVisual(_moveSeg, _mode == BattleHudMode.Move);
            ApplySegmentVisual(_actSeg, _mode == BattleHudMode.Act);
            ApplySegmentVisual(_observeSeg, _mode == BattleHudMode.Observe);
        }

        void ApplySegmentVisual(Transform seg, bool selected)
        {
            if (seg == null)
                return;

            // 描边=当前模式信号（色取自运行时 UiTheme；与烘焙侧 ModeActiveStroke 同源）。
            var outline = seg.GetComponent<Outline>();
            if (outline == null)
            {
                outline = seg.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                outline.useGraphicAlpha = true;
            }
            outline.effectColor = UiTheme.BrassLight;
            outline.enabled = selected;

            var label = seg.Find("Label");
            var graphic = label != null ? label.GetComponent<MaskableGraphic>() : null;
            if (graphic != null)
                UiTextUtil.SetColor(graphic, selected
                    ? new Color(0x2A / 255f, 0x1D / 255f, 0x0E / 255f, 1f)   // InkOnGold 同源
                    : UiTheme.TextLight);
        }

        void RefreshModeHint()
        {
            if (_hintText == null)
                return;
            string text = _mode == BattleHudMode.Move
                ? "左键选角色　拖动=环绕角色转视角　AD 转向　滚轮 力度　空格 跳"
                : _mode == BattleHudMode.Act
                    ? "AD 转向　WS 力度　滚轮微调　回车 开炮（左键只点按钮）"
                    : "点击准星选人并返回　WASD/Space/Shift 移动　滚轮缩放　Esc 返回";
            UiTextUtil.SetText(_hintText, text);
        }

        static Transform DeepFind(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = DeepFind(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        void OnEnable()
        {
            EventBus.Subscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Subscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Subscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
        }

        void OnDisable()
        {
            // 离场兜底：暂停中直接回主菜单/选关，绝不能把 timeScale=0 带出战斗场景
            // （EventBus 静态事件跨场景存活，SceneLoader 的 unscaled 过渡动画不受影响，
            // 但主菜单的所有缩放时间会停摆）。重开一局路径已显式 ForceResume，这里再兜一道。
            BattlePause.ForceResume();

            EventBus.Unsubscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Unsubscribe(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Unsubscribe(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Unsubscribe(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Unsubscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Unsubscribe(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
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
        }

        // ------------------------------------------------------------------
        // 玩家命令
        // ------------------------------------------------------------------

        /// <summary>
        /// 按钮统一反馈：UiClick（失败时 UiError）+ punch 缩放。
        /// 同一模块内的表现层反馈，直接调用 <see cref="AudioService"/> 静态入口，不走 EventBus。
        /// </summary>
        void ButtonFeedback(Button button, bool success)
        {
            AudioService.PlayUi(success ? SfxId.UiClick : SfxId.UiError);
            if (button != null && success && _motion != null)
                _motion.Punch(button.image != null ? button.image : button.GetComponent<Graphic>(),
                    UiMotionRules.PunchSeconds);
        }

        void OnWeaponClicked(int weaponId)
        {
            if (aimController == null)
                return;

            bool selected = aimController.SelectWeapon((WeaponId)weaponId);
            ButtonFeedback(weaponButtons != null && weaponId >= 0 && weaponId < weaponButtons.Length
                ? weaponButtons[weaponId]
                : null, selected);
            if (selected)
                RefreshWeaponPanel();
        }

        void OnThrowSelfClicked()
        {
            if (aimController == null)
                return;

            ButtonFeedback(throwSelfButton, success: true);
            aimController.SelectThrowSelf();
            RefreshWeaponPanel();
        }

        void OnEndGoClicked()
        {
            if (aimController == null)
                return;

            ButtonFeedback(endGoButton, success: true);
            aimController.EndGo();
            RefreshWeaponPanel(hide: true);
        }

        void OnBackClicked()
        {
            ButtonFeedback(settlementBackButton != null && settlementBackButton.interactable
                ? settlementBackButton
                : backButton, success: true);
            EventBus.Publish(GoBackEvent);
        }

        // ------------------------------------------------------------------
        // 暂停 / 返回确认 / 结算 / 重开（发布收口）
        // ------------------------------------------------------------------

        void OnPauseButtonClicked()
        {
            ButtonFeedback(pauseButton, success: true);
            OpenPause();
        }

        /// <summary>
        /// 进入暂停：timeScale=0（冻物理/粒子）+ BattlePause 状态位（冻 TurnManager/AI/相机的
        /// 逐帧计数，见 BattlePause 类注释）。瞄准输入在 Update 里随 IsPaused 持续关闭。
        /// </summary>
        void OpenPause()
        {
            if (BattlePause.IsPaused || (turnManager != null && battle != null && battle.IsMatchOver))
                return;

            BattlePause.Pause();
            RefreshWeaponPanel(hide: true);
            if (pausePanelRoot != null)
                pausePanelRoot.SetActive(true);
            AudioService.PlayUi(SfxId.UiPanelOpen);
        }

        void ClosePause()
        {
            if (!BattlePause.IsPaused)
                return;

            BattlePause.Resume();
            HidePausePanel();
            // 还原瞄准输入口径：观察模式本就禁输入，其余模式放开。
            if (aimController != null)
                aimController.InputEnabled = _mode != BattleHudMode.Observe;
            AudioService.PlayUi(SfxId.UiClick);
        }

        void HidePausePanel()
        {
            if (pausePanelRoot != null)
                pausePanelRoot.SetActive(false);
        }

        /// <summary>
        /// 再来一局：重载 Battle 场景。SceneLoader 对同名目标自动不压栈（原地重载），
        /// 栈顶的返回点（选关/主菜单）天然保住，无需 replaceTop 补丁（已退役）。
        /// </summary>
        void RestartBattle()
        {
            AudioService.PlayUi(SfxId.UiClick);
            BattlePause.ForceResume();

            if (_campaignBattle && CampaignApi.LastSettlement != null)
            {
                CampaignApi.SelectLevel(CampaignApi.LastSettlement.Value.LevelId);
                return;
            }

            EventBus.Publish(ChangeSceneEvent, SceneNames.Battle);
        }

        /// <summary>返回主菜单/选关前先确认（BackConfirm）；对局已结束时直接走（结算面板的返回按钮不经过这里）。</summary>
        void ShowBackConfirm()
        {
            if (confirmDialogRoot == null)
            {
                // 没装配确认框时退回旧行为，保证功能不缺。
                OnBackClicked();
                return;
            }

            if (confirmMessage != null)
                UiTextUtil.SetText(confirmMessage, UiStrings.BackConfirm);
            confirmDialogRoot.SetActive(true);
            AudioService.PlayUi(SfxId.UiPanelOpen);
        }

        void ConfirmLeaveBattle()
        {
            HideConfirmDialog();
            BattlePause.ForceResume();
            AudioService.PlayUi(SfxId.UiClick);
            EventBus.Publish(GoBackEvent);
        }

        void HideConfirmDialog()
        {
            if (confirmDialogRoot != null)
                confirmDialogRoot.SetActive(false);
        }

        /// <summary>
        /// 结算面板：胜负大字 + 得分；战役局追加 关卡/星级/经验/新招募/首次通关 行。
        /// 星级行同时点亮三颗星图标。数据源 = CampaignApi.LastSettlement / LastReward
        /// （CampaignApi 先于本组件订阅 match_finished，此刻必已算完）。
        /// </summary>
        void ShowSettlement(MatchFinishedPayload finished)
        {
            if (settlementPanelRoot == null)
                return;

            var lines = new List<string>();

            if (settlementTitleText != null)
            {
                UiTextUtil.SetText(settlementTitleText,
                    UiTextRules.OutcomeTitle((MatchOutcome)finished.Outcome, finished.Team1IsAi));
            }

            if (finished.Score > 0)
                lines.Add(UiTextRules.SettlementScore(finished.Score));

            CampaignSettlement settlement = CampaignApi.LastSettlement ?? default;
            bool hasCampaign = _campaignBattle && CampaignApi.LastSettlement != null;
            if (hasCampaign)
            {
                lines.Add(UiTextRules.SettlementLevel(UiTextRules.LevelName(settlement.LevelNumber)));
                lines.Add(UiTextRules.SettlementStars(settlement.Stars, StarRules.MaxStars));
                SetSettlementStars(settlement.Stars);

                if (CampaignApi.LastReward is CrewRewardPayload reward)
                {
                    if (reward.XpPerCrew > 0)
                        lines.Add(UiTextRules.SettlementXp(reward.XpPerCrew));

                    if (reward.UnlockedCrewIds is { Length: > 0 })
                    {
                        lines.Add(UiTextRules.SettlementUnlock(
                            string.Join("、", DisplayNamesOf(reward.UnlockedCrewIds))));
                    }
                }

                if (settlement.FirstClear)
                    lines.Add(UiStrings.SettlementRowFirstClear);
            }
            else
            {
                SetSettlementStars(0);
            }

            if (settlementLinesText != null)
                UiTextUtil.SetText(settlementLinesText, string.Join("\n", lines));

            settlementPanelRoot.SetActive(true);
            // 胜负短乐句走音乐通道（Music 分类，受音乐滑条控制）；不可用时回落面板开合音。
            bool jingle = AudioService.PlayMusic((MatchOutcome)finished.Outcome == MatchOutcome.Team0Win
                ? SfxId.VictoryJingle
                : SfxId.DefeatJingle);
            if (!jingle)
                AudioService.PlayUi(SfxId.UiPanelOpen);
        }

        void SetSettlementStars(int stars)
        {
            if (settlementStars == null)
                return;

            for (int i = 0; i < settlementStars.Length; i++)
            {
                if (settlementStars[i] != null)
                    settlementStars[i].color = i < stars
                        ? UiTheme.Brass
                        : UiTheme.WithAlpha(UiTheme.Ink, 0.35f);
            }
        }

        static string[] DisplayNamesOf(string[] crewIds)
        {
            var names = new string[crewIds.Length];
            for (int i = 0; i < crewIds.Length; i++)
            {
                names[i] = CrewRosterCatalog.TryGet(crewIds[i], out CrewRosterEntry entry)
                    ? entry.DisplayName
                    : crewIds[i];
            }

            return names;
        }

        void HideSettlementPanel()
        {
            if (settlementPanelRoot != null)
                settlementPanelRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        // EventBus 回调（刷新入口）
        // ------------------------------------------------------------------

        void OnBattleStarted(object payload)
        {
            if (payload is BattleStartedPayload started && rosterTitle != null)
                UiTextUtil.SetText(rosterTitle, UiTextRules.RosterTitle(started.LevelNumber));

            // 回合计数归零（battle_started 恒先于第一手 turn_started 发布，见 BattleController.Start）。
            _turnNumber = 0;
            RefreshTurnCounter();

            // BattleStarted 时仍有待结算关卡 = 这一局从选关进来（结算面板要显示星级/经验）。
            // 注意时序：CampaignApi 先订阅（主菜单 Awake），它的 stale 清理先跑完才轮到这里。
            _campaignBattle = CampaignApi.PendingLevelId != null;

            // 重开一局经场景重载进来：清掉可能残留的暂停态（静态字段跨场景存活）。
            BattlePause.ForceResume();
            HidePausePanel();
            HideSettlementPanel();
            HideConfirmDialog();

            BuildRoster();
            RefreshTurnHint();
            RefreshWeaponPanel(hide: true);
        }

        void OnTurnStarted(object payload)
        {
            _turnNumber++;
            RefreshTurnCounter();
            RefreshTurnHint();
            RefreshRoster();
            RefreshWeaponPanel();
            PunchTurnBanner();
        }

        /// <summary>回合计数槽（顶部信息条）：显示对局第 N 手，不带上限（原版 §3 无回合上限）。</summary>
        void RefreshTurnCounter()
        {
            if (_turnCounterText != null)
                UiTextUtil.SetText(_turnCounterText, UiTextRules.TurnCounter(Mathf.Max(1, _turnNumber)));
        }

        /// <summary>回合横幅弹出（每次换行动单位都给一次"轮到谁了"的视觉重音）。</summary>
        void PunchTurnBanner()
        {
            if (turnHintText != null && _motion != null)
                _motion.Punch(turnHintText, UiMotionRules.PunchSeconds);
        }

        void OnTurnEnded(object payload)
        {
            RefreshWeaponPanel(hide: true);
        }

        void OnActionSelected(object payload)
        {
            RefreshWeaponPanel(hide: true);
            RefreshRoster();
        }

        void OnCrewDamaged(object payload)
        {
            if (!(payload is CrewDamagedPayload damaged))
                return;

            UpdateRosterRow(damaged.PirateId, damaged.Health, damaged.MaxHealth, alive: damaged.Health > 0);
            RefreshTurnHint();
        }

        void OnCrewDied(object payload)
        {
            if (!(payload is CrewDiedPayload died))
                return;

            UpdateRosterRow(died.PirateId, 0, CrewCatalog.MaxHealth, alive: false);
            RefreshTurnHint();
        }

        void OnMatchFinished(object payload)
        {
            if (payload is MatchFinishedPayload finished)
            {
                if (turnHintText != null)
                {
                    UiTextUtil.SetText(turnHintText, UiTextRules.OutcomeTitle(
                        (MatchOutcome)finished.Outcome, finished.Team1IsAi));
                }

                if (teamStatusText != null)
                {
                    UiTextUtil.SetText(teamStatusText, finished.Team1IsAi && finished.Score > 0
                        ? UiTextRules.SettlementScore(finished.Score)
                        : string.Empty);
                }

                ShowSettlement(finished);
            }

            PunchTurnBanner();
            RefreshWeaponPanel(hide: true);
        }

        void OnCameraFocusRequested(object payload)
        {
            // 回合开始 pan 与玩家点选角色都会走这里；点选时 aimController.SelectedCharacter 已就绪。
            RefreshWeaponPanel();
        }

        // ------------------------------------------------------------------
        // 名册（左下，§4.1 血条 28 帧）
        // ------------------------------------------------------------------

        void BuildRoster()
        {
            for (int i = 0; i < MaxRosterRows; i++)
                _pirateByRow[i] = null;

            if (rosterRows == null || battle == null)
                return;

            var pirates = battle.AllPirates;
            int row = 0;
            for (int i = 0; i < pirates.Count && row < rosterRows.Length; i++)
            {
                PirateBase pirate = pirates[i];
                if (pirate == null)
                    continue;

                _pirateByRow[row] = pirate;
                RosterRowView view = rosterRows[row];
                if (view == null)
                {
                    row++;
                    continue;
                }

                if (view.root != null)
                    view.root.SetActive(true);

                if (view.nameLabel != null)
                {
                    UiTextUtil.SetText(view.nameLabel, UiTextRules.RosterRow(pirate.TeamNumber, pirate.CrewType));
                    // 名文字按队伍着色（r4 评审 N13：奶油色区分弱）。r7 换成写死的高对比提亮色
                    // （旧 TeamColor×0.75 实测仅 3.76/2.80，低于 4.5）——见 RosterNameRed/Blue 的 WCAG 计算。
                    view.nameLabel.color = pirate.TeamIndex == 0 ? RosterNameRed : RosterNameBlue;
                }

                if (view.teamSwatch != null)
                    view.teamSwatch.color = UiTheme.TeamColor(pirate.TeamIndex);

                UpdateRowBar(view, pirate.Alive ? pirate.Health : 0, pirate.MaxHealth, snap: true);
                row++;
            }

            // 隐藏多余行。
            for (int i = row; i < rosterRows.Length; i++)
            {
                if (rosterRows[i] != null && rosterRows[i].root != null)
                    rosterRows[i].root.SetActive(false);
            }
        }

        void RefreshRoster()
        {
            if (rosterRows == null)
                return;

            for (int i = 0; i < rosterRows.Length; i++)
            {
                PirateBase pirate = _pirateByRow[i];
                if (pirate == null || rosterRows[i] == null)
                    continue;

                if (rosterRows[i].nameLabel != null)
                    UiTextUtil.SetText(rosterRows[i].nameLabel, UiTextRules.RosterRow(pirate.TeamNumber, pirate.CrewType));

                UpdateRowBar(rosterRows[i], pirate.Alive ? pirate.Health : 0, pirate.MaxHealth);
            }
        }

        void UpdateRosterRow(int pirateId, int health, int maxHealth, bool alive)
        {
            if (rosterRows == null)
                return;

            for (int i = 0; i < rosterRows.Length; i++)
            {
                PirateBase pirate = _pirateByRow[i];
                if (pirate == null || pirate.PirateId != pirateId || rosterRows[i] == null)
                    continue;

                UpdateRowBar(rosterRows[i], alive ? health : 0, maxHealth);
                // 受击重音：血量标签轻弹一下（死亡行也会经这里滚到 0，一并覆盖）
                if (_motion != null && rosterRows[i].healthLabel != null)
                    _motion.Punch(rosterRows[i].healthLabel, UiMotionRules.PunchSeconds);
                return;
            }
        }

        /// <summary>
        /// 血条落值：默认走 <see cref="UiMotion"/> 的指数滚动（伤害数字先跳、血条紧随）；
        /// <c>snap=true</c> 用于名册初次构建/重建，直接钉位不滚动。
        /// </summary>
        void UpdateRowBar(RosterRowView view, int health, int maxHealth, bool snap = false)
        {
            if (view == null)
                return;

            float ratio = HealthRatio(health, maxHealth);
            if (view.healthFill != null)
            {
                if (_motion == null || snap)
                {
                    view.healthFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                    if (_motion != null)
                        _motion.SnapFill(view.healthFill, ratio);
                }
                else
                {
                    _motion.SetFillTarget(view.healthFill, ratio);
                }
            }

            if (view.healthLabel != null)
                UiTextUtil.SetText(view.healthLabel, UiTextRules.Hp(health, maxHealth));
        }

        /// <summary>§4.1：血条 28 帧，长度 = <c>1 + ceil(27 * health / maxHealth)</c>，折算为 0–1 比例。</summary>
        public static float HealthRatio(int health, int maxHealth)
        {
            if (maxHealth <= 0 || health <= 0)
                return 0f;

            int frames = 1 + Mathf.CeilToInt(27f * Mathf.Clamp(health, 0, maxHealth) / maxHealth);
            return Mathf.Clamp01((float)frames / HealthBarFrames);
        }

        // ------------------------------------------------------------------
        // 回合提示与双方存活（顶部）
        // ------------------------------------------------------------------

        void RefreshTurnHint()
        {
            BattleTeam team = turnManager != null ? turnManager.CurrentTeam : null;
            if (team == null)
            {
                if (turnHintText != null)
                    UiTextUtil.SetText(turnHintText, string.Empty);
                if (teamStatusText != null)
                    UiTextUtil.SetText(teamStatusText, string.Empty);
                return;
            }

            if (turnHintText != null)
                UiTextUtil.SetText(turnHintText, UiTextRules.TurnHint(team.AiControlled, team.Number));

            if (teamStatusText != null && battle != null)
            {
                // 修正旧缺陷（验收 V10）：旧实现只统计当前行动队，标签与数字对不上；
                // 现在红/蓝两队的存活数都算，一次报全。
                BattleTeam red = battle.GetTeam(0);
                BattleTeam blue = battle.GetTeam(1);
                UiTextUtil.SetText(teamStatusText, UiTextRules.TeamStatus(
                    CountAlive(red), CountTotal(red),
                    CountAlive(blue), CountTotal(blue)));
            }
        }

        static int CountAlive(BattleTeam team)
        {
            if (team == null)
                return 0;

            int alive = 0;
            var characters = team.Characters;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] != null && characters[i].Alive)
                    alive++;
            }

            return alive;
        }

        static int CountTotal(BattleTeam team)
        {
            return team != null ? team.Characters.Count : 0;
        }

        // ------------------------------------------------------------------
        // 武器面板（底部中央）
        // ------------------------------------------------------------------

        /// <summary>
        /// 显示条件：已选中角色 &amp;&amp; 角色存活 &amp;&amp; 当前队非 AI。
        /// </summary>
        void RefreshWeaponPanel(bool hide = false)
        {
            PirateBase selected = aimController != null ? aimController.SelectedCharacter : null;
            BattleTeam team = turnManager != null ? turnManager.CurrentTeam : null;

            bool visible = !hide
                && selected != null
                && selected.Alive
                && team != null
                && !team.AiControlled;

            SetWeaponPanelVisible(visible);

            if (!visible)
                return;

            if (weaponPanelTitle != null)
                UiTextUtil.SetText(weaponPanelTitle, UiTextRules.WeaponPanelTitle(selected.CrewType));

            if (throwSelfButton != null)
                throwSelfButton.interactable = selected.CurrentAction.CanThrow;

            if (endGoButton != null)
                endGoButton.interactable = true;

            WeaponInventory inventory = selected.Inventory;
            if (weaponButtons == null)
                return;

            for (int i = 0; i < weaponButtons.Length; i++)
            {
                if (weaponButtons[i] == null)
                    continue;

                var id = (WeaponId)i;
                bool owned = inventory != null && inventory.Contains(id);
                weaponButtons[i].interactable = owned;

                if (weaponLabels != null && i < weaponLabels.Length && weaponLabels[i] != null)
                {
                    MaskableGraphic label = weaponLabels[i];
                    UiTextUtil.SetText(label, UiTextRules.WeaponName(id));
                    UiTextUtil.SetColor(label, owned
                        ? UiTheme.TextLight
                        : UiTheme.WithAlpha(UiTheme.TextLight, 0.35f));
                }

                // 已装备高亮（WeaponInventory.EquippedIndex 单值）。
                Image background = weaponButtons[i].targetGraphic as Image;
                if (background != null)
                {
                    bool equipped = inventory != null && inventory.HasEquipped && inventory.EquippedIndex == i;
                    background.color = equipped ? UiTheme.BrassLight : Color.white;
                }
            }
        }

        /// <summary>
        /// 武器面板显隐的唯一入口：只有状态翻转才动效（<see cref="RefreshWeaponPanel"/> 刷新很频繁，
        /// 不能每次都重播滑入）。出现 = 滑入淡入 + UiPanelOpen 音；消失 = 快速淡出（协程收尾 SetActive(false)）。
        /// </summary>
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
    }
}
