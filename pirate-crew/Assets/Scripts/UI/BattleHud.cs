using System;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗 HUD（UGUI，翻译自 Godot <c>battle_hud.tscn</c> + <c>scripts/ui/hud.gd</c>）。
    ///
    /// 【对应章节】
    ///   §3.2  回合提示文案「Player N, take your turn」/「Computer, take your turn」；
    ///   §3.4  武器面板显示条件（先选中己方角色才出现）+ 三按钮语义（throw character / end go / button_&lt;weaponId&gt;）；
    ///   §4.1  血条长度 = <c>1 + ceil(27 * shownHealth / maxHealth)</c>（共 28 帧）；
    ///   §4.5  名册/血条位于 BottomLeft。
    ///
    /// 【布局依据】M2-Godot基准摘要 §5.1 第 6 条：TopRight 状态 / BottomCenter 武器 / BottomLeft 名册。
    ///
    /// 【架构约定】
    ///   · 所有引用走 <c>[SerializeField]</c>（由 <c>M2BattleSceneSetup</c> 程序化接好），
    ///     不在运行时 <c>Resources.Load</c> / <c>GameObject.Find</c>；
    ///   · 状态刷新全部由 <see cref="EventBus"/> 的 <see cref="BattleEvents"/> 事件驱动，**不做 Update 轮询**；
    ///   · UI 文本统一用 legacy <see cref="UnityEngine.UI.Text"/>（本工程未装 TMP，见 SceneSetup 类头）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleHud : MonoBehaviour
    {
        /// <summary>返回主菜单事件名（与 SceneLoader / BattlePlaceholder 约定一致）。</summary>
        const string GoBackEvent = "go_back";

        /// <summary>§4.1 血条总帧数（28 帧）。</summary>
        const int HealthBarFrames = 28;

        /// <summary>名册最多显示的行数（当前转写关卡最大 12 人，见 LevelCatalog level_4）。</summary>
        const int MaxRosterRows = 12;

        /// <summary>名册单行控件集合（由装配脚本程序化创建并接线）。</summary>
        [Serializable]
        public sealed class RosterRowView
        {
            public GameObject root;
            public Image teamSwatch;
            public Text nameLabel;
            public Image healthFill;
            public Text healthLabel;
        }

        // ------------------------------------------------------------------
        // 序列化引用（场景内直连）
        // ------------------------------------------------------------------

        [Header("战场引用")]
        [Tooltip("战斗组装根；用于取全部出战角色构建名册。")]
        [SerializeField] BattleController battle;
        [Tooltip("回合驱动器；用于取当前队伍与 AI 判定。")]
        [SerializeField] TurnManager turnManager;
        [Tooltip("瞄准控制器；武器/抛自己/end go 命令的出口。")]
        [SerializeField] AimThrowController aimController;

        [Header("回合提示（TopRight）")]
        [SerializeField] Text turnHintText;
        [SerializeField] Text teamStatusText;

        [Header("武器面板（BottomCenter）")]
        [SerializeField] GameObject weaponPanelRoot;
        [SerializeField] Text weaponPanelTitle;
        [Tooltip("17 个武器按钮，索引 = WeaponId 枚举值。")]
        [SerializeField] Button[] weaponButtons = new Button[17];
        [Tooltip("与 weaponButtons 一一对应的按钮文本。")]
        [SerializeField] Text[] weaponLabels = new Text[17];
        [SerializeField] Button throwSelfButton;
        [SerializeField] Button endGoButton;

        [Header("名册（BottomLeft）")]
        [SerializeField] Text rosterTitle;
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
        // 玩家命令（§3.4 阶段 B）
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
                rosterTitle.text = "Roster — Level " + started.LevelNumber;

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
            // §3.4：任何动作（抛自己/用武器/end go）之后 weaponSelected=true，面板收起；
            // 若玩家再次点选角色，BattleController 会重新发 CameraFocusRequested 让面板再现。
            RefreshWeaponPanel(hide: true);
            RefreshRoster();
        }

        void OnCrewDamaged(object payload)
        {
            if (!(payload is CrewDamagedPayload damaged))
                return;

            UpdateRosterRow(damaged.PirateId, damaged.Health, damaged.MaxHealth, alive: damaged.Health > 0);
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
                    turnHintText.text = OutcomeText(finished);

                if (teamStatusText != null)
                    teamStatusText.text = finished.Team1IsAi ? ("Score " + finished.Score) : string.Empty;
            }

            RefreshWeaponPanel(hide: true);
        }

        void OnCameraFocusRequested(object payload)
        {
            // 回合开始 pan 与玩家点选角色都会走这里；点选时 aimController.SelectedCharacter 已就绪
            //（BattleController.SelectCharacter 先 ResetForSelection 再 Publish）。
            RefreshWeaponPanel();
        }

        // ------------------------------------------------------------------
        // 名册（BottomLeft，§4.1 血条 28 帧）
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
                    view.nameLabel.text = "T" + pirate.TeamNumber + " " + pirate.CrewType;

                if (view.teamSwatch != null)
                    view.teamSwatch.color = TeamColor(pirate.TeamIndex);

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
                    rosterRows[i].nameLabel.text = "T" + pirate.TeamNumber + " " + pirate.CrewType;

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
                view.healthLabel.text = health + "/" + maxHealth;
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
        // 回合提示（TopRight，§3.2 / §3.3）
        // ------------------------------------------------------------------

        void RefreshTurnHint()
        {
            BattleTeam team = turnManager != null ? turnManager.CurrentTeam : null;
            if (team == null)
            {
                if (turnHintText != null)
                    turnHintText.text = string.Empty;
                if (teamStatusText != null)
                    teamStatusText.text = string.Empty;
                return;
            }

            if (turnHintText != null)
                turnHintText.text = team.AiControlled ? "Computer, take your turn" : "Player " + team.Number + ", take your turn";

            if (teamStatusText != null && battle != null)
            {
                int alive = 0;
                int total = 0;
                for (int t = 0; t < battle.TeamCount; t++)
                {
                    BattleTeam bt = battle.GetTeam(t);
                    if (bt == null)
                        continue;

                    total += bt.Characters.Count;
                    for (int c = 0; c < bt.Characters.Count; c++)
                    {
                        PirateBase p = bt.Characters[c];
                        if (p != null && p.Alive)
                            alive++;
                    }
                }

                teamStatusText.text = "Team " + team.Number + " (" + (team.Number == 2 ? "Blue" : "Red")
                    + ")  |  Alive " + alive + "/" + total;
            }
        }

        static string OutcomeText(MatchFinishedPayload finished)
        {
            switch ((MatchOutcome)finished.Outcome)
            {
                case MatchOutcome.Team0Win:
                    return finished.Team1IsAi ? "Player wins!" : "Player 1 wins!";
                case MatchOutcome.Team1Win:
                    return "Player 2 wins!";
                case MatchOutcome.Draw:
                    return "Draw.";
                default:
                    return "Level failed.";
            }
        }

        // ------------------------------------------------------------------
        // 武器面板（BottomCenter，§3.4）
        // ------------------------------------------------------------------

        /// <summary>
        /// §3.4 显示条件：<c>selectedCharacter &amp;&amp; !weaponSelected &amp;&amp; !aiControlled</c>。
        /// 本实现用「当前是否已选中角色 && 当前队非 AI」近似 <c>!weaponSelected</c>：
        /// 选择动作后由 <see cref="OnActionSelected"/> 主动收起，再次点选角色时经
        /// <see cref="OnCameraFocusRequested"/> 重新展开。
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
                weaponPanelTitle.text = selected.CrewType + " — choose action";

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
                    Text label = weaponLabels[i];
                    label.text = DisplayNameOf(id);
                    label.color = owned ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                }

                // 已装备高亮（WeaponInventory.EquippedIndex 单值）。
                Image background = weaponButtons[i].targetGraphic as Image;
                if (background != null)
                {
                    bool equipped = inventory != null && inventory.HasEquipped && inventory.EquippedIndex == i;
                    background.color = equipped ? new Color(1f, 0.85f, 0.3f, 1f) : Color.white;
                }
            }
        }

        static string DisplayNameOf(WeaponId id)
        {
            return WeaponCatalog.TryGet(id, out WeaponStats stats) ? stats.DisplayName : id.ToString();
        }

        static Color TeamColor(int teamIndex)
        {
            return teamIndex == 0 ? new Color(0.85f, 0.25f, 0.25f, 1f) : new Color(0.3f, 0.5f, 0.9f, 1f);
        }
    }
}
