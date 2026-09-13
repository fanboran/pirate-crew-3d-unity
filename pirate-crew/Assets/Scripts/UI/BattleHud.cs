using System;
using PirateCrew.Core;
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
    ///   顶部（小地图 / 模式开关 / 回合与计时 / 双方存活）、屏幕中心准星、
    ///   左下船员名册、底部中央武器面板（返回按钮移左下）、底部瞄准/聚焦标签与操作提示。
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
        /// <summary>返回主菜单事件名（与 SceneLoader / BattlePlaceholder 约定一致）。</summary>
        const string GoBackEvent = "go_back";

        /// <summary>§4.1 血条总帧数（28 帧）。</summary>
        const int HealthBarFrames = 28;

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

        // ------------------------------------------------------------------
        // 运行时状态
        // ------------------------------------------------------------------

        PirateBase[] _pirateByRow = new PirateBase[MaxRosterRows];

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
            WireWeaponButtons();
            WireCommandButtons();

            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);

            RefreshWeaponPanel(hide: true);
            RefreshRoster();
            RefreshTurnHint();
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

        void OnWeaponClicked(int weaponId)
        {
            if (aimController == null)
                return;

            if (aimController.SelectWeapon((WeaponId)weaponId))
                RefreshWeaponPanel();
        }

        void OnThrowSelfClicked()
        {
            if (aimController == null)
                return;

            aimController.SelectThrowSelf();
            RefreshWeaponPanel();
        }

        void OnEndGoClicked()
        {
            if (aimController == null)
                return;

            aimController.EndGo();
            RefreshWeaponPanel(hide: true);
        }

        void OnBackClicked()
        {
            EventBus.Publish(GoBackEvent);
        }

        // ------------------------------------------------------------------
        // EventBus 回调（刷新入口）
        // ------------------------------------------------------------------

        void OnBattleStarted(object payload)
        {
            if (payload is BattleStartedPayload started && rosterTitle != null)
                UiTextUtil.SetText(rosterTitle, UiTextRules.RosterTitle(started.LevelNumber));

            BuildRoster();
            RefreshTurnHint();
            RefreshWeaponPanel(hide: true);
        }

        void OnTurnStarted(object payload)
        {
            RefreshTurnHint();
            RefreshRoster();
            RefreshWeaponPanel();
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
            }

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
                    // 名文字按队伍着色（r4 评审 N13：奶油色区分弱）。RGB 压暗保对比（alpha 必须保 1，
                    // Color 的 * 会连 alpha 一起乘导致文字半透明）：红/蓝乘 0.75 档在深木底上
                    // 对比 ≈4.6/3.5（≥ WCAG 大文本与非文本 3:1 口径）。
                    Color team = UiTheme.TeamColor(pirate.TeamIndex);
                    view.nameLabel.color = new Color(team.r * 0.75f, team.g * 0.75f, team.b * 0.75f, 1f);
                }

                if (view.teamSwatch != null)
                    view.teamSwatch.color = UiTheme.TeamColor(pirate.TeamIndex);

                UpdateRowBar(view, pirate.Alive ? pirate.Health : 0, pirate.MaxHealth);
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
                return;
            }
        }

        static void UpdateRowBar(RosterRowView view, int health, int maxHealth)
        {
            if (view == null)
                return;

            float ratio = HealthRatio(health, maxHealth);
            if (view.healthFill != null)
                view.healthFill.rectTransform.anchorMax = new Vector2(ratio, 1f);

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

            if (weaponPanelRoot != null)
                weaponPanelRoot.SetActive(visible);

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
    }
}
