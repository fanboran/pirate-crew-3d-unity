using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 船员管理界面（M3）：展示名册与经验、招募状态、编成（上阵/取消），并提供「选关」「保存」「返回」入口。
    ///
    /// 【对应 Godot】Godot 版没有船员管理 UI，本界面是 M3 新增的最小可用版。
    ///
    /// 【接线约定】UI 不持有模块内部对象：
    ///   · 状态读写走 <see cref="CrewManagementApi"/>；
    ///   · 场景切换走 EventBus <c>change_scene</c> / <c>go_back</c>；
    ///   · 名册变化订阅 <see cref="CrewManagementEvents"/> 事件刷新列表。
    ///
    /// 【本波次改造】文本 TMP 化 + 全中文（<see cref="UiStrings"/> / <see cref="UiTextRules"/>）；
    /// 行/按钮换羊皮纸 + 木板九宫格；行字号提为正文下限 20（规范 §1.4 硬约束）。
    /// 事件契约与订阅清单不变。
    /// </summary>
    public sealed class CrewManagementController : MonoBehaviour
    {
        const string ChangeSceneEvent = "change_scene";
        const string GoBackEvent = "go_back";

        const float RowHeight = 44f;

        [Header("引用（场景内直连）")]
        [SerializeField] TextMeshProUGUI summaryText;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] Transform crewListContainer;
        [SerializeField] Button levelSelectButton;
        [SerializeField] Button saveButton;
        [SerializeField] Button backButton;

        [Header("字体（由 M3SceneSetup 注入中文字体资产）")]
        [Tooltip("正文中文字体（霞鹜文楷 Medium SDF）；运行时建列表行用。")]
        [SerializeField] TMP_FontAsset bodyFont;

        void Awake()
        {
            // 保证结算监听已挂上（从主菜单进来时主菜单已调用过，这里是幂等的兜底）。
            CampaignApi.EnsureBootstrapped();

            if (levelSelectButton != null)
                levelSelectButton.onClick.AddListener(OnLevelSelectClicked);
            if (saveButton != null)
                saveButton.onClick.AddListener(OnSaveClicked);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);

            EventBus.Subscribe(CrewManagementEvents.RosterUpdated, OnRosterChanged);
            EventBus.Subscribe(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
            EventBus.Subscribe(CampaignEvents.LevelCompleted, OnLevelCompleted);
        }

        void Start()
        {
            SetStatus(string.Empty);
            Refresh();
        }

        void OnDestroy()
        {
            if (levelSelectButton != null)
                levelSelectButton.onClick.RemoveListener(OnLevelSelectClicked);
            if (saveButton != null)
                saveButton.onClick.RemoveListener(OnSaveClicked);
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackClicked);

            EventBus.Unsubscribe(CrewManagementEvents.RosterUpdated, OnRosterChanged);
            EventBus.Unsubscribe(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
            EventBus.Unsubscribe(CampaignEvents.LevelCompleted, OnLevelCompleted);
        }

        // ------------------------------------------------------------------
        // 列表
        // ------------------------------------------------------------------

        /// <summary>重建备选船员列表与顶部概况文本。</summary>
        public void Refresh()
        {
            RefreshSummary();
            RebuildCrewList();
        }

        void RefreshSummary()
        {
            if (summaryText == null)
                return;

            Roster roster = CrewManagementApi.Roster;
            string active = roster.Active.Count == 0
                ? UiStrings.CrewSummaryEmpty
                : string.Join("、", DisplayNames(roster.Active));

            summaryText.text = string.Format(UiStrings.CrewSummaryFormat,
                roster.Active.Count, roster.MaxSize, active,
                roster.UnlockedCount, CrewRosterCatalog.Count,
                CampaignApi.Progress.TotalStars);
        }

        void RebuildCrewList()
        {
            if (crewListContainer == null)
                return;

            UiTextUtil.WarnIfMissing(bodyFont, "船员管理列表");
            M3UiBuilder.ClearChildren(crewListContainer);

            for (int i = 0; i < CrewManagementApi.AllCrews.Count; i++)
            {
                CrewRosterEntry entry = CrewManagementApi.AllCrews[i];
                bool unlocked = CrewManagementApi.IsUnlocked(entry.Id);

                RectTransform row = M3UiBuilder.CreateRow(crewListContainer, i, RowHeight);

                string label = unlocked
                    ? UiTextRules.CrewRow(entry.DisplayName,
                        CrewManagementApi.Progression.GetLevel(entry.Id),
                        CrewManagementApi.Progression.GetXp(entry.Id))
                    : UiTextRules.CrewRowLocked(entry.DisplayName, entry.UnlockLevelNumber);

                TextMeshProUGUI text = M3UiBuilder.CreateText("Label", row, label, UiTheme.FontBody,
                    TextAlignmentOptions.MidlineLeft,
                    unlocked ? UiTheme.Ink : UiTheme.WithAlpha(UiTheme.Ink, UiTheme.DisabledAlpha),
                    bodyFont);

                Button action = M3UiBuilder.CreateButton("Action", row, string.Empty, UiTheme.FontHint,
                    bodyFont);
                TextMeshProUGUI actionLabel = M3UiBuilder.GetButtonLabel(action);
                string crewId = entry.Id;   // 闭包捕获：每轮独立变量

                if (unlocked)
                {
                    bool active = CrewManagementApi.IsActive(crewId);
                    if (actionLabel != null)
                        actionLabel.text = active ? UiStrings.CrewRemove : UiStrings.CrewEnlist;
                    action.onClick.AddListener(() => OnToggleActive(crewId));
                }
                else
                {
                    if (actionLabel != null)
                        actionLabel.text = UiStrings.CrewLocked;
                    action.interactable = false;
                    action.targetGraphic.color = M3UiBuilder.DisabledButtonColor;
                }

                M3UiBuilder.LayoutRowContent(row, text, action, RowHeight);
            }
        }

        static string[] DisplayNames(System.Collections.Generic.IReadOnlyList<string> crewIds)
        {
            var names = new string[crewIds.Count];
            for (int i = 0; i < crewIds.Count; i++)
            {
                names[i] = CrewRosterCatalog.TryGet(crewIds[i], out CrewRosterEntry entry)
                    ? entry.DisplayName
                    : crewIds[i];
            }

            return names;
        }

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------

        void OnToggleActive(string crewId)
        {
            if (CrewManagementApi.IsActive(crewId))
            {
                CrewManagementApi.RemoveFromActive(crewId);
                SetStatus(string.Format(UiStrings.CrewStatusRemovedFormat, DisplayName(crewId)));
            }
            else if (CrewManagementApi.AddToActive(crewId))
            {
                SetStatus(string.Format(UiStrings.CrewStatusEnlistedFormat, DisplayName(crewId)));
            }
            else
            {
                SetStatus(string.Format(UiStrings.CrewStatusFullFormat, CrewManagementApi.Roster.MaxSize));
            }

            // 名册事件已驱动刷新；这里不重复调用 Refresh，避免二次重建列表。
        }

        void OnLevelSelectClicked()
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                SetStatus(UiStrings.CrewStatusEmptyRoster);
                return;
            }

            EventBus.Publish(ChangeSceneEvent, SceneNames.LevelSelect);
        }

        void OnSaveClicked()
        {
            SetStatus(CampaignApi.SaveProgress()
                ? string.Format(UiStrings.CrewStatusSavedFormat, CampaignApi.ProgressSlot)
                : UiStrings.CrewStatusSaveFailed);
        }

        void OnBackClicked()
        {
            EventBus.Publish(GoBackEvent);
        }

        // ------------------------------------------------------------------
        // 事件回调
        // ------------------------------------------------------------------

        void OnRosterChanged(object payload)
        {
            Refresh();
        }

        void OnCrewUnlocked(object payload)
        {
            if (payload is CrewUnlockedPayload unlocked)
                SetStatus(string.Format(UiStrings.CrewStatusNewCrewFormat, unlocked.DisplayName));
        }

        void OnLevelCompleted(object payload)
        {
            if (!(payload is CampaignLevelCompletedPayload completed))
                return;

            string levelName = LevelDisplayName(completed.LevelId, completed.LevelNumber);
            if (completed.Cleared)
                SetStatus(string.Format(UiStrings.CrewStatusClearedFormat, levelName, completed.Stars));
            else
                SetStatus(string.Format(UiStrings.CrewStatusFailedFormat, levelName));

            // Refresh 已由 crew_roster_updated 驱动（关卡结算会广播经验变化）。
        }

        void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        static string DisplayName(string crewId)
        {
            return UiTextRules.CrewNameById(crewId);
        }

        /// <summary>关卡 id / 序号 → 中文关卡名（目录查不到时用「第 N 关」）。</summary>
        static string LevelDisplayName(string levelId, int levelNumber)
        {
            if (CampaignCatalog.TryGet(levelId, out CampaignLevel level))
                return level.DisplayName;

            return UiTextRules.LevelName(levelNumber);
        }
    }
}
