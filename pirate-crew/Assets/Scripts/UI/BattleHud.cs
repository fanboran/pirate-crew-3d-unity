using System;
using System.Collections;
using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Audio;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Combat;
using PirateCrew.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗 HUD（多彩卡通 · 图标优先版，2026-09-19 重设计）。
    ///
    /// 【信息架构（本波次定案，用户三轮裁决）】
    ///   · **顶栏双队合成血条**：左右屏缘各一条，每名存活单位 = 一段分格（受击只掉自己那段，
    ///     白色 damage ghost 残影延迟回落），条下一排职业色头像 pips（死亡换骷髅）——
    ///     替代旧左下 12 行名册列表（名册整块退役，P3-2 名册文案随之消灭）；
    ///   · **单位头顶血条**（<see cref="OverheadHealthBar"/>）：个人血条从列表挪进 3D 世界；
    ///   · **中央回合徽章**：队色环 + 数字，回合切换弹跳；
    ///   · **武器面板全图标化**：17 武器各占一格彩色图标（选中才显示名字与说明一行），
    ///     投掷 / 结束回合为图标主按钮——原版 Mutiny 的全图标交互语言；
    ///   · 模式开关 = 靴位 / 准星 / 眼睛三图标钮；暂停 / 返回 = 图标钮；文字只剩提示条与结算横幅。
    ///
    /// 【架构约定（沿用）】全部引用走 <c>[SerializeField]</c>（<c>BattleUiTheme.WireHud</c> 回写，
    /// 不做 GameObject.Find——旧 DeepFind 已随重写清退）；状态刷新全部 EventBus 事件驱动；
    /// 皮肤 / 字号 / 颜色一律 <see cref="UiSkin"/> Token。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleHud : MonoBehaviour
    {
        /// <summary>§4.1 血条总帧数（28 帧，长度 = 1 + ceil(27·hp/max)）。</summary>
        const int HealthBarFrames = 28;

        /// <summary>每队最多段数（当前关卡上限 6v6；装配侧按此建段）。</summary>
        const int MaxSegmentsPerTeam = 6;

        // ------------------------------------------------------------------
        // 视图子结构（装配脚本按字段名回写）
        // ------------------------------------------------------------------

        /// <summary>顶栏一队的合成血条（段容器 + pip 容器）。</summary>
        [Serializable]
        public sealed class TeamBarView
        {
            public GameObject root;
            public RectTransform segmentRoot;
            public RectTransform pipRoot;
            public UnitSegmentView[] segments = new UnitSegmentView[MaxSegmentsPerTeam];
            public UnitPipView[] pips = new UnitPipView[MaxSegmentsPerTeam];
        }

        /// <summary>一名单位的血条段（fill + 白色残影 ghost）。</summary>
        [Serializable]
        public sealed class UnitSegmentView
        {
            public GameObject root;
            public Image fill;
            public Image ghost;
        }

        /// <summary>一名单位的 pip（职业头像 + 彩色格底）。</summary>
        [Serializable]
        public sealed class UnitPipView
        {
            public GameObject root;
            public Image icon;
            public Image frame;
        }

        /// <summary>武器面板头条的 HP 条（凹槽 + ghost + fill）。</summary>
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
        [SerializeField] AimThrowController aimController;
        [Tooltip("战斗相机控制器（同场景显式注入，由 EditorTools.BattleLookupWiring 接线）："
                 + "观察模式开关要转交给它（SetObserveMode）。缺失时观察模式只切 UI 态、相机不动。")]
        [SerializeField] BattleCameraController cameraController;

        [Header("顶栏双队血条")]
        [SerializeField] TeamBarView teamBarRed;
        [SerializeField] TeamBarView teamBarBlue;

        [Header("回合徽章（中央）")]
        [SerializeField] Image badgeRing;
        [SerializeField] MaskableGraphic badgeText;
        [SerializeField] MaskableGraphic turnHintText;

        [Header("武器面板（底部中央）")]
        [SerializeField] GameObject weaponPanelRoot;
        [SerializeField] Image unitPortrait;
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

        [Header("模式开关（右上，索引 = BattleHudMode）")]
        [SerializeField] Button[] modeButtons = new Button[3];
        [SerializeField] Image[] modeFrames = new Image[3];

        [Header("系统按钮与提示")]
        [SerializeField] Button backButton;
        [SerializeField] Button pauseButton;
        [SerializeField] MaskableGraphic hintText;

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

        /// <summary>段索引 → 单位（两队各一份）。</summary>
        readonly Dictionary<int, PirateBase> _pirateBySegment = new Dictionary<int, PirateBase>();

        /// <summary>这一局是否战役局（结算面板按它决定显示哪些行）。</summary>
        bool _campaignBattle;

        /// <summary>对局第 N 手（每次 turn_started 递增；原版 §3 无回合上限）。</summary>
        int _turnNumber;

        UiMotion _motion;
        CanvasGroup _panelGroup;
        RectTransform _panelRect;
        bool _panelVisible;
        /// <summary>首次状态直接落位（不打动效、不出声），之后的翻转才播动效。</summary>
        bool _panelResolved;

        readonly List<OverheadHealthBar> _overheadBars = new List<OverheadHealthBar>();

        static readonly Dictionary<string, Sprite> PortraitCache = new Dictionary<string, Sprite>();

        // ------------------------------------------------------------------
        // 装配自检（供 PlayMode 结构断言）
        // ------------------------------------------------------------------

        /// <summary>HUD 是否已接好核心战场引用。</summary>
        public bool HasCoreReferences => battle != null && turnManager != null && aimController != null;

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

        /// <summary>双队血条是否接好（段 + pips 全链）。</summary>
        public bool HasTeamBarWiring
        {
            get
            {
                return TeamBarWired(teamBarRed) && TeamBarWired(teamBarBlue);
            }
        }

        static bool TeamBarWired(TeamBarView bar)
        {
            if (bar == null || bar.root == null || bar.segmentRoot == null || bar.pipRoot == null)
                return false;
            if (bar.segments == null || bar.segments.Length != MaxSegmentsPerTeam)
                return false;
            if (bar.pips == null || bar.pips.Length != MaxSegmentsPerTeam)
                return false;

            for (int i = 0; i < MaxSegmentsPerTeam; i++)
            {
                UnitSegmentView segment = bar.segments[i];
                if (segment == null || segment.root == null || segment.fill == null || segment.ghost == null)
                    return false;
                UnitPipView pip = bar.pips[i];
                if (pip == null || pip.root == null || pip.icon == null || pip.frame == null)
                    return false;
            }
            return true;
        }

        /// <summary>模式开关（3 段图标钮）是否接好。</summary>
        public bool HasModeWiring
        {
            get
            {
                if (modeButtons == null || modeButtons.Length != 3)
                    return false;
                if (modeFrames == null || modeFrames.Length != 3)
                    return false;
                for (int i = 0; i < 3; i++)
                {
                    if (modeButtons[i] == null || modeFrames[i] == null)
                        return false;
                }
                return true;
            }
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();
            // 依赖解析必须在 InitModeButtons 之前（它内部会走 SetHudMode → 驱动相机）。
            ResolveCameraControllerOnce();
            WireWeaponButtons();
            WireCommandButtons();
            InitModeButtons();
        }

        void OnEnable()
        {
            EventBus.Subscribe<BattleStartedPayload>(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe<TurnStartedPayload>(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe<int>(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe<ActionSelectedPayload>(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Subscribe<CrewDamagedPayload>(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe<CrewDiedPayload>(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe<MatchFinishedPayload>(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Subscribe<Transform>(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
        }

        void OnDisable()
        {
            // 离场兜底：暂停中直接回主菜单/选关，绝不能把 timeScale=0 带出战斗场景。
            BattlePause.ForceResume();

            EventBus.Subscribe<BattleStartedPayload>(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe<TurnStartedPayload>(BattleEvents.TurnStarted, OnTurnStarted);
            EventBus.Subscribe<int>(BattleEvents.TurnEnded, OnTurnEnded);
            EventBus.Subscribe<ActionSelectedPayload>(BattleEvents.ActionSelected, OnActionSelected);
            EventBus.Subscribe<CrewDamagedPayload>(BattleEvents.CrewDamaged, OnCrewDamaged);
            EventBus.Subscribe<CrewDiedPayload>(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe<MatchFinishedPayload>(BattleEvents.MatchFinished, OnMatchFinished);
            EventBus.Subscribe<Transform>(BattleEvents.CameraFocusRequested, OnCameraFocusRequested);
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
                confirmCancelButton.onClick.AddListener(HideConfirmDialog);
            if (settlementRestartButton != null)
                settlementRestartButton.onClick.AddListener(RestartBattle);
            if (settlementBackButton != null)
                settlementBackButton.onClick.AddListener(OnBackClicked);
        }

        // ------------------------------------------------------------------
        // 模式系统（r12 用户裁决；本波次图标化 + DeepFind 清退）
        //
        // 【移动】左键=选角色；按住角色拖拽=跳跃；拖空白=转视角。
        // 【操作】炮台模式：AD 转向、WS 力度、左键/空格=开火。
        // 【观察】我的世界同款：鼠标移动=转视角（准星只在此时显示）。
        // ------------------------------------------------------------------

        public enum BattleHudMode { Move, Act, Observe }

        BattleHudMode _mode = BattleHudMode.Move;
        Transform _crosshair;

        /// <summary>
        /// 战斗相机解析：**只在 Awake 跑一次**（旧写法在 <see cref="SetHudMode"/> 里
        /// <c>if (_cameraController == null) _cameraController = FindObjectOfType&lt;...&gt;()</c>，
        /// 于是每次模式切换都可能全场扫描一次）。
        ///
        /// 装配期注入优先；未注入时一次性兜底并吵闹——真正的装配缺陷由
        /// <see cref="CameraControllerWiredByAssembly"/>（PlayMode 装配测试断言）钉住。
        /// </summary>
        void ResolveCameraControllerOnce()
        {
            CameraControllerWiredByAssembly = cameraController != null;
            if (cameraController != null)
                return;

            cameraController = FindObjectOfType<BattleCameraController>();
            Log.Warn("[BattleHud] cameraController 未经装配接线，已一次性兜底解析"
                     + (cameraController != null ? "成功" : "失败（观察模式将不再驱动相机）")
                     + "。修复：跑 PirateCrew.EditorTools.BattleLookupWiring.Wire（写 Battle.unity）。");
        }

        /// <summary>本类的 <see cref="cameraController"/> 是否来自**装配期注入**。PlayMode 装配测试读它。</summary>
        public bool CameraControllerWiredByAssembly { get; private set; }

        void InitModeButtons()
        {
            for (int i = 0; i < 3; i++)
            {
                BattleHudMode mode = (BattleHudMode)i;
                if (modeButtons != null && modeButtons[i] != null)
                {
                    Button button = modeButtons[i];
                    button.onClick.AddListener(() =>
                    {
                        ButtonFeedback(button, success: true);
                        SetHudMode(mode);
                    });
                }
            }

            // 准星与旧版同构：canvas 全树找一次（静态装饰节点，装配契约兜底）。
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            Transform searchRoot = parentCanvas != null ? parentCanvas.transform : transform;
            _crosshair = DeepFind(searchRoot, "Crosshair");

            SetHudMode(BattleHudMode.Move);
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
                // Esc 优先级：暂停中→恢复；观察模式→退出观察；瞄准中→不抢；否则→打开暂停。
                if (BattlePause.IsPaused)
                    ClosePause();
                else if (_mode == BattleHudMode.Observe)
                    SetHudMode(BattleHudMode.Move);
                else if (aimController == null || !aimController.IsAiming)
                    OpenPause();
            }

            // 暂停时冻结瞄准输入。
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

            // 相机引用在 Awake 已解析完（ResolveCameraControllerOnce）——此处不再查找。
            if (cameraController != null)
                cameraController.SetObserveMode(mode == BattleHudMode.Observe);
            if (aimController != null)
            {
                aimController.SetWeaponPreference(mode == BattleHudMode.Act);
                aimController.InputEnabled = mode != BattleHudMode.Observe;
            }

            RefreshModeSegments();
            RefreshModeHint();
            RefreshCrosshair();
        }

        /// <summary>准星只属于观察模式；只在模式切换时刷一次。</summary>
        void RefreshCrosshair()
        {
            if (_crosshair == null)
                return;

            bool visible = _mode == BattleHudMode.Observe;
            if (_crosshair.gameObject.activeSelf != visible)
                _crosshair.gameObject.SetActive(visible);
        }

        /// <summary>模式图标钮状态：当前段 = 金底 + 深墨字；其余 = 深底 + 暖白。</summary>
        void RefreshModeSegments()
        {
            if (modeFrames == null)
                return;

            for (int i = 0; i < modeFrames.Length && i < 3; i++)
            {
                if (modeFrames[i] == null)
                    continue;

                bool selected = i == (int)_mode;
                modeFrames[i].color = selected ? UiSkin.Gold : UiSkin.InkSoft;

                Transform icon = modeFrames[i].transform.Find("Icon");
                var graphic = icon != null ? icon.GetComponent<MaskableGraphic>() : null;
                if (graphic != null)
                    UiTextUtil.SetColor(graphic, selected ? UiSkin.InkOnGold : UiSkin.TextOnInk);
            }
        }

        void RefreshModeHint()
        {
            if (hintText == null)
                return;
            string text = _mode == BattleHudMode.Move
                ? "左键选角色　拖动转视角　滚轮力度　空格跳"
                : _mode == BattleHudMode.Act
                    ? "AD 转向　WS 力度　回车开炮"
                    : "准星点人返回　WASD 移动　Esc 返回";
            UiTextUtil.SetText(hintText, text);
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
            if (aimController == null)
                return;

            bool selected = aimController.SelectWeapon((WeaponId)weaponId);
            ButtonFeedback(weaponButtons != null && weaponId >= 0 && weaponId < weaponButtons.Length
                ? weaponButtons[weaponId]
                : null, selected);
            if (selected)
            {
                RefreshWeaponPanel();
                PunchWeaponFrame(weaponId);
            }
        }

        void PunchWeaponFrame(int weaponId)
        {
            if (_motion == null || weaponFrames == null
                || weaponId < 0 || weaponId >= weaponFrames.Length || weaponFrames[weaponId] == null)
                return;

            _motion.Punch(weaponFrames[weaponId], UiMotionRules.PunchSeconds);
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
            OpenModal(pausePanelRoot, pauseCard);
        }

        void ClosePause()
        {
            if (!BattlePause.IsPaused)
                return;

            BattlePause.Resume();
            CloseModal(pausePanelRoot);
            if (aimController != null)
                aimController.InputEnabled = _mode != BattleHudMode.Observe;
            AudioService.PlayUi(SfxId.UiClick);
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
            OpenModal(confirmDialogRoot, confirmCard);
        }

        void ConfirmLeaveBattle()
        {
            CloseModal(confirmDialogRoot);
            BattlePause.ForceResume();
            AudioService.PlayUi(SfxId.UiClick);
            EventBus.Publish(SceneEvents.GoBack);
        }

        void HideConfirmDialog()
        {
            CloseModal(confirmDialogRoot);
        }

        /// <summary>
        /// 结算乐句的唯一决策口（纯函数；音频层裁决对 HUD 生效，含「2P 热座蓝队胜不放乐句」口径）。
        /// </summary>
        public static bool SettlementJingleFor(int outcome, bool team1IsAi, out SfxId jingle)
        {
            bool hasMusic = AudioEventMapper.MusicForMatchOutcome(
                (MatchOutcome)outcome, team1IsAi, out jingle);
            return hasMusic;
        }

        /// <summary>结算面板：胜负大字 + 三星逐颗 pop + 得分 / 战役明细行。</summary>
        void ShowSettlement(MatchFinishedPayload finished)
        {
            if (settlementPanelRoot == null)
                return;

            // 「显示哪几行 / 亮几颗星」是纯规则（SettlementPanelRules，可无头测）；
            // 本方法只负责：读数据源 → 交给规则 → 把行种类渲染成文本 → 摆 UI。
            CampaignSettlement settlement = CampaignApi.LastSettlement ?? default;
            bool hasCampaignSettlement = CampaignApi.LastSettlement != null;
            CrewRewardPayload? reward = CampaignApi.LastReward;

            var input = new SettlementPanelRules.PanelInput(
                finished.Score,
                _campaignBattle,
                hasCampaignSettlement,
                settlement.Stars,
                settlement.FirstClear,
                reward != null,
                reward?.XpPerCrew ?? 0,
                reward?.UnlockedCrewIds?.Length ?? 0);

            if (settlementTitleText != null)
            {
                UiTextUtil.SetText(settlementTitleText,
                    UiTextRules.OutcomeTitle((MatchOutcome)finished.Outcome, finished.Team1IsAi));
            }

            List<SettlementPanelRules.RowKind> rows = SettlementPanelRules.RowsFor(input);
            var lines = new List<string>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                string line = SettlementRowText(rows[i], input, settlement, reward);
                if (line != null)
                    lines.Add(line);
            }

            if (settlementLinesText != null)
                UiTextUtil.SetText(settlementLinesText, string.Join("\n", lines));

            SetSettlementStars(SettlementPanelRules.LitStarsFor(input));
            OpenModal(settlementPanelRoot, settlementCard);

            bool played = SettlementJingleFor(finished.Outcome, finished.Team1IsAi, out SfxId jingle)
                          && AudioService.PlayMusic(jingle);
            if (!played)
                AudioService.PlayUi(SfxId.UiPanelOpen);
        }

        /// <summary>把一行行种类渲染成文本（文案全部来自 <see cref="UiTextRules"/>，本方法只做映射）。</summary>
        static string SettlementRowText(SettlementPanelRules.RowKind kind,
            in SettlementPanelRules.PanelInput input, in CampaignSettlement settlement, CrewRewardPayload? reward)
        {
            switch (kind)
            {
                case SettlementPanelRules.RowKind.Score:
                    return UiTextRules.SettlementScore(input.Score);
                case SettlementPanelRules.RowKind.Level:
                    return UiTextRules.SettlementLevel(MapDisplayName(settlement.MapId));
                case SettlementPanelRules.RowKind.Stars:
                    return UiTextRules.SettlementStars(settlement.Stars, StarRules.MaxStars);
                case SettlementPanelRules.RowKind.Xp:
                    return UiTextRules.SettlementXp(input.XpPerCrew);
                case SettlementPanelRules.RowKind.Unlock:
                    // 规则只在 HasReward 时才会给出 Unlock 行；这里再守一道，避免数据源中途变了就 NRE。
                    return reward.HasValue
                        ? UiTextRules.SettlementUnlock(string.Join("、", DisplayNamesOf(reward.Value.UnlockedCrewIds)))
                        : null;
                case SettlementPanelRules.RowKind.FirstClear:
                    return UiStrings.SettlementRowFirstClear;
                default:
                    return null;
            }
        }

        /// <summary>三星逐颗点亮：颜色分层 + 逐颗延迟 pop（juice）。</summary>
        void SetSettlementStars(int stars)
        {
            if (settlementStars == null)
                return;

            for (int i = 0; i < settlementStars.Length; i++)
            {
                if (settlementStars[i] == null)
                    continue;

                bool lit = i < stars;
                settlementStars[i].color = lit ? UiSkin.Gold : UiSkin.WithAlpha(UiSkin.DeadGray, 0.6f);
                if (lit && isActiveAndEnabled)
                    StartCoroutine(PopStarDelayed(settlementStars[i], 0.12f * i));
            }
        }

        IEnumerator PopStarDelayed(Image star, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (_motion != null)
                _motion.Pop(star);
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

        /// <summary>海图 id → 中文海图名（目录查不到时回退原始 id）。</summary>
        static string MapDisplayName(string mapId)
        {
            return WorldMapCatalog.TryGet(mapId, out WorldMapDefinition map)
                ? map.DisplayName
                : mapId;
        }

        // ------------------------------------------------------------------
        // EventBus 回调
        // ------------------------------------------------------------------

        void OnBattleStarted(BattleStartedPayload payload)
        {
            // 载荷（关卡/队伍数）不进 HUD，只用“一局开始”这个时机重建面板。
            _turnNumber = 0;
            RefreshBadge();

            // BattleStarted 时仍有待结算关卡 = 这一局从选关进来（结算面板要显示星级/经验）。
            _campaignBattle = CampaignApi.HasPendingMap;

            // 重开一局经场景重载进来：清掉可能残留的暂停态与旧模态。
            BattlePause.ForceResume();
            // HidePausePanel();  // 临时注释：方法尚未落地（隔壁会话中间态），编译窗口用
            CloseModal(settlementPanelRoot);
            CloseModal(confirmDialogRoot);

            BuildTeamBars();
            AttachOverheadBars();
            RefreshTurnHint();
            RefreshWeaponPanel(hide: true);
        }

        void OnTurnStarted(TurnStartedPayload payload)
        {
            // 行动角色/镜头目标由相机层消费，HUD 只刷新回合徽章与队伍条。
            _turnNumber++;
            RefreshBadge(punch: true);
            RefreshTurnHint();
            RefreshTeamBars();
            RefreshWeaponPanel();
        }

        void OnTurnEnded(int teamNumber)
        {
            // 队伍编号不进 HUD，只用“回合结束”这个时机收起武器面板。
            RefreshWeaponPanel(hide: true);
        }

        void OnActionSelected(ActionSelectedPayload action)
        {
            // 动作种类不进 HUD（面板收起与队伍条刷新与种类无关）。
            RefreshWeaponPanel(hide: true);
            RefreshTeamBars();
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
        }

        void OnCameraFocusRequested(Transform target)
        {
            // 回合开始 pan 与玩家点选角色都会走这里；点选时 aimController.SelectedCharacter 已就绪。
            // 焦点 Transform 由相机层消费（HUD 只借这个时机收起/弹出武器面板）。
            RefreshWeaponPanel();
        }

        // ------------------------------------------------------------------
        // 回合徽章与提示
        // ------------------------------------------------------------------

        void RefreshBadge(bool punch = false)
        {
            BattleTeam team = turnManager != null ? turnManager.CurrentTeam : null;

            if (badgeText != null)
                UiTextUtil.SetText(badgeText, Mathf.Max(1, _turnNumber).ToString());

            if (badgeRing != null)
                badgeRing.color = team != null ? UiSkin.TeamFill(team.TeamIndex) : UiSkin.Gold;

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

        // ------------------------------------------------------------------
        // 顶栏双队血条（段 = 存活单位；pip = 职业头像）
        // ------------------------------------------------------------------

        void BuildTeamBars()
        {
            _pirateBySegment.Clear();

            if (battle == null)
                return;

            BuildOneTeamBar(teamBarRed, battle.GetTeam(0), 0);
            BuildOneTeamBar(teamBarBlue, battle.GetTeam(1), 1);
        }

        void BuildOneTeamBar(TeamBarView bar, BattleTeam team, int teamIndex)
        {
            if (bar == null)
                return;

            int count = 0;
            if (team != null)
            {
                var characters = team.Characters;
                for (int i = 0; i < characters.Count && count < MaxSegmentsPerTeam; i++)
                {
                    PirateBase pirate = characters[i];
                    if (pirate == null)
                        continue;

                    int slot = count;
                    _pirateBySegment[SegmentKey(teamIndex, slot)] = pirate;

                    UnitSegmentView segment = bar.segments != null && slot < bar.segments.Length
                        ? bar.segments[slot]
                        : null;
                    if (segment != null && segment.root != null)
                    {
                        segment.root.SetActive(true);
                        if (segment.fill != null)
                            segment.fill.color = UiSkin.TeamFill(teamIndex);
                        if (segment.ghost != null)
                            _motion.SnapFillPair(segment.fill, segment.ghost,
                                pirate.Alive ? HealthRatio(pirate.Health, pirate.MaxHealth) : 0f);
                    }

                    UnitPipView pip = bar.pips != null && slot < bar.pips.Length ? bar.pips[slot] : null;
                    if (pip != null && pip.root != null)
                    {
                        pip.root.SetActive(true);
                        bool alive = pirate.Alive;
                        if (pip.frame != null)
                            pip.frame.color = alive
                                ? UiSkin.CellBase(UiSkin.CrewColor(pirate.CrewType))
                                : UiSkin.WithAlpha(UiSkin.DeadGray, 0.55f);
                        if (pip.icon != null)
                        {
                            pip.icon.sprite = alive
                                ? LoadPortrait(pirate.CrewType)
                                : UiGlyphs.Get(UiGlyphs.Glyph.Skull);
                            pip.icon.color = alive
                                ? Color.white
                                : UiSkin.WithAlpha(UiSkin.DeadGray, 0.9f);
                        }
                    }

                    count++;
                }
            }

            // 隐藏多余段 / pips。
            for (int i = count; i < MaxSegmentsPerTeam; i++)
            {
                if (bar.segments != null && i < bar.segments.Length && bar.segments[i] != null
                    && bar.segments[i].root != null)
                    bar.segments[i].root.SetActive(false);
                if (bar.pips != null && i < bar.pips.Length && bar.pips[i] != null
                    && bar.pips[i].root != null)
                    bar.pips[i].root.SetActive(false);
            }

            // 段按实际人数满格重排（4v4 时每段 1/4 宽，不留空槽——装配期按 6 人预建只是骨架）。
            if (count > 0 && bar.segments != null && bar.segmentRoot != null)
            {
                float trackWidth = bar.segmentRoot.sizeDelta.x;
                float segmentWidth = (trackWidth - 4f - (count - 1) * SegmentGap) / count;
                for (int i = 0; i < count && i < bar.segments.Length; i++)
                {
                    var rect = bar.segments[i] != null
                        ? bar.segments[i].root.transform as RectTransform
                        : null;
                    if (rect == null)
                        continue;

                    rect.sizeDelta = new Vector2(segmentWidth, rect.sizeDelta.y);
                    rect.anchoredPosition = new Vector2(2f + i * (segmentWidth + SegmentGap), 0f);
                }
            }
        }

        /// <summary>段间距（与 BattleHudBuilder.SegmentGap 同源）。</summary>
        const float SegmentGap = 4f;

        static int SegmentKey(int teamIndex, int slot) => teamIndex * 100 + slot;

        /// <summary>全量刷两队（回合切换时）。</summary>
        void RefreshTeamBars()
        {
            if (battle == null)
                return;

            RefreshOneTeam(teamBarRed, 0);
            RefreshOneTeam(teamBarBlue, 1);
        }

        void RefreshOneTeam(TeamBarView bar, int teamIndex)
        {
            if (bar == null)
                return;

            for (int slot = 0; slot < MaxSegmentsPerTeam; slot++)
            {
                if (!_pirateBySegment.TryGetValue(SegmentKey(teamIndex, slot), out PirateBase pirate)
                    || pirate == null)
                    continue;

                ApplySegmentHealth(bar, slot, pirate.Alive ? pirate.Health : 0, pirate.MaxHealth);
                if (!pirate.Alive)
                    MarkPipDead(teamIndex, pirate.PirateId);   // 幂等（已死跳过）
            }
        }

        /// <summary>按事件更新单段（受击 / 死亡）。</summary>
        void UpdateUnitSegment(int teamIndex, int pirateId, int health, int maxHealth)
        {
            TeamBarView bar = teamIndex == 0 ? teamBarRed : teamBarBlue;
            if (bar == null)
                return;

            for (int slot = 0; slot < MaxSegmentsPerTeam; slot++)
            {
                if (!_pirateBySegment.TryGetValue(SegmentKey(teamIndex, slot), out PirateBase pirate)
                    || pirate == null || pirate.PirateId != pirateId)
                    continue;

                ApplySegmentHealth(bar, slot, health > 0 ? health : 0, maxHealth);
                return;
            }
        }

        void ApplySegmentHealth(TeamBarView bar, int slot, int health, int maxHealth)
        {
            UnitSegmentView segment = bar.segments != null && slot < bar.segments.Length
                ? bar.segments[slot]
                : null;
            if (segment == null)
                return;

            float ratio = HealthRatio(health, maxHealth);
            if (_motion != null)
                _motion.SetFillPairTarget(segment.fill, segment.ghost, ratio);
            else if (segment.fill != null)
                segment.fill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        }

        void MarkPipDead(int teamIndex, int pirateId)
        {
            TeamBarView bar = teamIndex == 0 ? teamBarRed : teamBarBlue;
            if (bar == null)
                return;

            for (int slot = 0; slot < MaxSegmentsPerTeam; slot++)
            {
                if (!_pirateBySegment.TryGetValue(SegmentKey(teamIndex, slot), out PirateBase pirate)
                    || pirate == null || pirate.PirateId != pirateId)
                    continue;

                UnitPipView pip = bar.pips != null && slot < bar.pips.Length ? bar.pips[slot] : null;
                if (pip == null)
                    return;

                bool alreadyDead = pip.icon != null && pip.icon.sprite == UiGlyphs.Get(UiGlyphs.Glyph.Skull);
                if (alreadyDead)
                    return;

                if (pip.frame != null)
                    pip.frame.color = UiSkin.WithAlpha(UiSkin.DeadGray, 0.55f);
                if (pip.icon != null)
                {
                    pip.icon.sprite = UiGlyphs.Get(UiGlyphs.Glyph.Skull);
                    pip.icon.color = UiSkin.WithAlpha(UiSkin.DeadGray, 0.9f);
                }
                if (_motion != null && pip.frame != null)
                    _motion.Punch(pip.frame, UiMotionRules.PunchSeconds);
                return;
            }
        }

        /// <summary>职业头像（烘焙 PNG，按短名缓存；缺失时回退符号图标）。</summary>
        static Sprite LoadPortrait(string crewType)
        {
            string key = UiSkin.CrewKey(crewType);
            if (PortraitCache.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            Sprite sprite = Resources.Load<Sprite>("UIIcons/Crew_" + key);
            if (sprite == null)
            {
                // 烘焙资产缺失（未跑 UiSkinAssetBaker）时退到舵轮符号，不留白格。
                sprite = UiGlyphs.Get(UiGlyphs.Glyph.Helm);
            }

            PortraitCache[key] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------
        // 单位头顶血条
        // ------------------------------------------------------------------

        void AttachOverheadBars()
        {
            _overheadBars.Clear();
            if (battle == null)
                return;

            var pirates = battle.AllPirates;
            for (int i = 0; i < pirates.Count; i++)
            {
                OverheadHealthBar bar = OverheadHealthBar.Attach(pirates[i]);
                if (bar != null)
                    _overheadBars.Add(bar);
            }
        }

        // ------------------------------------------------------------------
        // 武器面板（底部中央，全图标格）
        // ------------------------------------------------------------------

        /// <summary>显示条件：已选中角色 &amp;&amp; 角色存活 &amp;&amp; 当前队非 AI。</summary>
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

            // 右列头条：头像 + 队色职业名 + HP 条。
            if (unitPortrait != null)
                unitPortrait.sprite = LoadPortrait(selected.CrewType);

            if (unitNameText != null)
            {
                UiTextUtil.SetText(unitNameText,
                    UiTextRules.CrewNameByBattleSymbol(selected.CrewType));
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

            // 已装备的武器名 / 说明（未装备时显示选择提示——文字退位的兜底：只有一行）。
            WeaponInventory inventory = selected.Inventory;
            bool equipped = inventory != null && inventory.HasEquipped;
            var equippedId = equipped ? (WeaponId)inventory.EquippedIndex : (WeaponId)(-1);

            if (weaponNameText != null)
            {
                UiTextUtil.SetText(weaponNameText, equipped
                    ? UiTextRules.WeaponName(equippedId)
                    : UiStrings.BattleWeaponPickHint);
                UiTextUtil.SetColor(weaponNameText, equipped ? UiSkin.Gold : UiSkin.TextDim);
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
                weaponButtons[i].interactable = owned;

                // 格底 = 武器语义色暗档（CellBase：静物全彩跳出灰底）；未拥有压暗；已装备换金。
                if (weaponFrames != null && i < weaponFrames.Length && weaponFrames[i] != null)
                {
                    Color frameColor = equipped && i == (int)equippedId
                        ? UiSkin.Gold
                        : owned ? UiSkin.WeaponCellBase(id) : UiSkin.WithAlpha(UiSkin.InkSoft, 0.55f);
                    weaponFrames[i].color = frameColor;
                }
            }
        }

        /// <summary>武器面板显隐唯一入口：状态翻转才动效 + 音。</summary>
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
        // 血量比例（原版 §4.1 的 28 帧口径，保留）
        // ------------------------------------------------------------------

        /// <summary>长度 = 1 + ceil(27·hp/max)，折算 0-1。</summary>
        public static float HealthRatio(int health, int maxHealth)
        {
            if (maxHealth <= 0 || health <= 0)
                return 0f;

            int frames = 1 + Mathf.CeilToInt(27f * Mathf.Clamp(health, 0, maxHealth) / maxHealth);
            return Mathf.Clamp01((float)frames / HealthBarFrames);
        }
    }
}
