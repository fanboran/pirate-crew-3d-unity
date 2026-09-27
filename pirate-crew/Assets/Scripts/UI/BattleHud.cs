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
    /// 战斗 HUD（文字占位版，2026-09-24 创始人三连裁决：图标全删换文字、HUD 按 3:1 艺术像素
    /// 收敛、文字解除像素栅格）。
    ///
    /// 【信息架构】
    ///   · **顶栏双队合成血条**：左右屏缘各一条，每名存活单位 = 一段分格（受击只掉自己那段，
    ///     白色 damage ghost 残影延迟回落），条下一排小方格 pips（存活空格 / 阵亡「×」——
    ///     职业头像图标已退役）；**头顶血条已根除**（非像素世界空间件清退），这是唯一血量读数；
    ///   · **中央回合徽章**：theme 金面钮（button_selected）+ 数字，回合切换弹跳；
    ///   · **武器面板**：17 武器各占一格**文字钮**（武器中文名），投掷 / 结束回合为文字按钮；
    ///   · 模式开关 = 移动 / 操作 / 观察三文字钮（快捷键 1/2/3 角标）；暂停 / 返回 = 文字钮。
    ///
    /// 【架构约定】引用一律 <c>[SerializeField]</c>（<c>BattleUiTheme.WireHud</c> 回写）；
    /// 唯一例外是 Crosshair：装配契约缺兜底时的静态装饰节点查找（<see cref="DeepFind"/>，
    /// Awake 一次，不参与逐帧逻辑）；状态刷新全部 EventBus 事件驱动；
    /// 皮肤 / 字号 / 颜色一律 <see cref="UiSkin"/> Token。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class BattleHud : MonoBehaviour
    {
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
        [SerializeField] BattleCameraDriver cameraController;

        [Header("顶栏双队血条")]
        [SerializeField] TeamBarView teamBarRed;
        [SerializeField] TeamBarView teamBarBlue;

        [Header("回合徽章（中央）")]
        [SerializeField] Image badgeRing;
        [SerializeField] MaskableGraphic badgeText;
        [SerializeField] MaskableGraphic turnHintText;

        [Header("武器面板（底部中央）")]
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

        /// <summary>提示条上"镜头 N m"上一次显示的值（-1 = 还没写过；滚轮改档才刷新）。</summary>
        int _lastCameraMeters = -1;

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
            // 离场兜底：暂停中直接回主菜单/选关，绝不能把 timeScale=0 带出战斗场景。
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
        // 【移动】左键=选角色；选角色后 A/D 转向、W/S 力度、空格=跳跃发射；拖空白=转视角。
        // 【操作】炮台模式：AD 转向、WS 力度、回车=开火（防走火）。
        // 【观察】我的世界同款：鼠标移动=转视角（准星只在此时显示）。
        // 可感知行为规格见 docs/技术/投掷行为契约.md。
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

            cameraController = FindObjectOfType<BattleCameraDriver>();
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

            // 提示条上的镜头档读数：滚轮改档后跟一次（见 CameraReadout 的注释）。
            RefreshCameraReadout();

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

        /// <summary>模式钮状态：选中 = 金面（<see cref="UiKit.ApplyThemeButton"/> sticky 档，
        /// 四态全钉 button_selected——悬停/按压不再回落灰面）；未选中 = 常态灰面。
        /// 与菜单系统选中语义同源（2026-09-28 Aseprite 换装波，Focus 环退役）。
        /// 【为什么不乘色】像素件的明暗色阶烘死在贴图里，状态必须换贴图。</summary>
        void RefreshModeSegments()
        {
            if (modeButtons == null || modeFrames == null)
                return;

            for (int i = 0; i < modeButtons.Length && i < 3 && i < modeFrames.Length; i++)
            {
                if (modeButtons[i] == null || modeFrames[i] == null)
                    continue;

                UiKit.ApplyThemeButton(modeButtons[i], modeFrames[i], sticky: i == (int)_mode);
            }
        }

        void RefreshModeHint()
        {
            if (hintText == null)
                return;
            string text = _mode == BattleHudMode.Move
                ? UiStrings.BattleHintMove
                : _mode == BattleHudMode.Act
                    ? UiStrings.BattleHintAiming
                    : UiStrings.BattleHintObserve;
            UiTextUtil.SetText(hintText, text + CameraReadout());
        }

        /// <summary>
        /// 镜头档读数（临时调参用，创始人 2026-09-22：「我在游戏内调整一个我看着最顺眼的距离
        /// 当做基准」）。显示的是**出图取景表的同一个单位**——可见高度米数 = 2 × OrthoSize
        /// （<see cref="BattleCameraDriver.RuntimeVisibleMeters"/>），所以滚轮挑完之后
        /// 念出这个数就能直接改 `PixelartLevelScene` 的 mid/wide/close。
        /// 基准定下后本读数可删（它只是提示条后缀，删掉不影响任何逻辑）。
        /// 【临时】"镜头 N m" 格式串是动态读数，暂留本处拼接；基准定案删除本读数前，
        /// 若要在别处复用再并入 <see cref="UiStrings"/>（字符串模板 + <see cref="UiTextRules"/>）。
        /// </summary>
        string CameraReadout()
        {
            if (cameraController == null)
                return string.Empty;
            return "　｜　镜头 " + Mathf.RoundToInt(cameraController.RuntimeVisibleMeters) + " m";
        }

        /// <summary>滚轮改变了正交档才刷新提示条（避免每帧写文本）。</summary>
        void RefreshCameraReadout()
        {
            if (hintText == null)
                return;
            int meters = cameraController != null
                ? Mathf.RoundToInt(cameraController.RuntimeVisibleMeters)
                : 0;
            if (meters == _lastCameraMeters)
                return;
            _lastCameraMeters = meters;
            RefreshModeHint();
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
            CloseModal(settlementPanelRoot);
            CloseModal(confirmDialogRoot);

            BuildTeamBars();
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

            // 右列头条：队色职业名 + HP 条（头像格随图标退役删除）。
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
                // 文字色从像素皮调色板取（黄铜强调档 / 暖白压 alpha 的次级档）。
                // 字号恒正文档（紧凑面板里 48 区块档会把说明行顶穿——创始人走查）。
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

                // 格底状态：已装备 = 金面 sticky（四态全钉 button_selected）；其余 = 常态灰面。
                if (weaponFrames != null && i < weaponFrames.Length && weaponFrames[i] != null)
                {
                    bool chosen = equipped && i == (int)equippedId;
                    UiKit.ApplyThemeButton(weaponButtons[i], weaponFrames[i], sticky: chosen);
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
    }
}
