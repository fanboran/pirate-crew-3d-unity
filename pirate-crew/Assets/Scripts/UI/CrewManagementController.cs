using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Battle.WorldMaps;
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
    /// 行/按钮换羊皮纸 + 木板九宫格；行字号走 <see cref="UiTheme.FontBody"/> 旧档入口
    /// （渲染经 MenuUiBuilder.ScaleLegacyFont 落到裁决后的 FontScale.Body 15，见 UiTheme 字号段说明）。
    /// 事件契约与订阅清单不变。
    /// </summary>
    public sealed class CrewManagementController : MonoBehaviour
    {

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

        /// <summary>按钮/列表动效驱动（菜单 juice 与战斗内同口径；数值/曲线全在 UiMotionRules）。</summary>
        UiMotion _motion;

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();

            if (levelSelectButton != null)
                levelSelectButton.onClick.AddListener(OnLevelSelectClicked);
            if (saveButton != null)
                saveButton.onClick.AddListener(OnSaveClicked);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);

            EventBus.Subscribe<RosterUpdatedPayload>(CrewManagementEvents.RosterUpdated, OnRosterChanged);
            EventBus.Subscribe<CrewUnlockedPayload>(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
            EventBus.Subscribe<CampaignMapCompletedPayload>(CampaignEvents.MapCompleted, OnMapCompleted);
        }

        void Start()
        {
            SetStatus(string.Empty);
            Refresh();

            // 列表首现动效（只在进场播一次；之后的 roster 事件重建不重播，避免每次上阵都闪动）。
            if (crewListContainer != null)
                M3UiBuilder.OpenPanel(crewListContainer.gameObject, _motion);
        }

        void OnDestroy()
        {
            if (levelSelectButton != null)
                levelSelectButton.onClick.RemoveListener(OnLevelSelectClicked);
            if (saveButton != null)
                saveButton.onClick.RemoveListener(OnSaveClicked);
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackClicked);

            EventBus.Unsubscribe<RosterUpdatedPayload>(CrewManagementEvents.RosterUpdated, OnRosterChanged);
            EventBus.Unsubscribe<CrewUnlockedPayload>(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
            EventBus.Unsubscribe<CampaignMapCompletedPayload>(CampaignEvents.MapCompleted, OnMapCompleted);
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
                    : UiTextRules.CrewRowLocked(entry.DisplayName, entry.UnlockStars);

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
                    action.onClick.AddListener(() => OnToggleActive(crewId, action));
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

        void OnToggleActive(string crewId, Button action)
        {
            if (CrewManagementApi.IsActive(crewId))
            {
                CrewManagementApi.RemoveFromActive(crewId);
                M3UiBuilder.ButtonFeedback(action, true, _motion);
                SetStatus(string.Format(UiStrings.CrewStatusRemovedFormat, DisplayName(crewId)));
            }
            else if (CrewManagementApi.AddToActive(crewId))
            {
                M3UiBuilder.ButtonFeedback(action, true, _motion);
                SetStatus(string.Format(UiStrings.CrewStatusEnlistedFormat, DisplayName(crewId)));
            }
            else
            {
                M3UiBuilder.ButtonFeedback(action, false, _motion);
                SetStatus(string.Format(UiStrings.CrewStatusFullFormat, CrewManagementApi.Roster.MaxSize));
            }

            // 名册事件已驱动刷新；这里不重复调用 Refresh，避免二次重建列表。
        }

        void OnLevelSelectClicked()
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                M3UiBuilder.ButtonFeedback(levelSelectButton, false, _motion);
                SetStatus(UiStrings.CrewStatusEmptyRoster);
                return;
            }

            M3UiBuilder.ButtonFeedback(levelSelectButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.LevelSelect);
        }

        void OnSaveClicked()
        {
            bool saved = CampaignApi.SaveProgress();
            M3UiBuilder.ButtonFeedback(saveButton, saved, _motion);
            SetStatus(saved
                ? string.Format(UiStrings.CrewStatusSavedFormat, CampaignApi.ProgressSlot)
                : UiStrings.CrewStatusSaveFailed);
        }

        void OnBackClicked()
        {
            M3UiBuilder.ButtonFeedback(backButton, true, _motion);
            EventBus.Publish(SceneEvents.GoBack);
        }

        // ------------------------------------------------------------------
        // 事件回调
        // ------------------------------------------------------------------

        void OnRosterChanged(RosterUpdatedPayload payload)
        {
            // 列表数据从 CrewManagementApi 现取，载荷只作“名册变了”的信号。
            Refresh();
        }

        void OnCrewUnlocked(CrewUnlockedPayload unlocked)
        {
            SetStatus(string.Format(UiStrings.CrewStatusNewCrewFormat, unlocked.DisplayName));
        }

        void OnMapCompleted(CampaignMapCompletedPayload completed)
        {
            string levelName = MapDisplayName(completed.MapId);
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

        /// <summary>海图 id → 中文海图名（目录查不到时回退原始 id）。</summary>
        static string MapDisplayName(string mapId)
        {
            return WorldMapCatalog.TryGet(mapId, out WorldMapDefinition map)
                ? map.DisplayName
                : mapId;
        }
    }
}
