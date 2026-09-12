using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 船员管理界面（M3）：展示名册与经验、招募状态、编成（上阵/取消），并提供「选关」「保存」「返回」入口。
    ///
    /// 【对应 Godot】Godot 版没有船员管理 UI（<c>modules/crew_management</c> 只有 <c>roster.gd</c> /
    ///   <c>progression.gd</c> 两个骨架脚本，无 <c>ui/</c> 目录），本界面是 M3 新增的最小可用版：
    ///   只做「看清状态 + 编成 + 进选关」，不做皮肤/装备等扩展（<c>api.gd</c> 提到的皮肤未在 M3 范围）。
    ///
    /// 【接线约定】UI 不持有 SceneLoader / SaveManager 之外的模块内部对象：
    ///   · 状态读写走 <see cref="CrewManagementApi"/>；
    ///   · 场景切换走 EventBus <c>change_scene</c> / <c>go_back</c>；
    ///   · 名册变化订阅 <see cref="CrewManagementEvents"/> 事件刷新列表。
    /// </summary>
    public sealed class CrewManagementController : MonoBehaviour
    {
        const string ChangeSceneEvent = "change_scene";
        const string GoBackEvent = "go_back";

        const float RowHeight = 44f;

        [Header("引用（场景内直连）")]
        [SerializeField] Text summaryText;
        [SerializeField] Text statusText;
        [SerializeField] Transform crewListContainer;
        [SerializeField] Button levelSelectButton;
        [SerializeField] Button saveButton;
        [SerializeField] Button backButton;

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
                ? "（未编成）"
                : string.Join("、", DisplayNames(roster.Active));

            summaryText.text = "编成 " + roster.Active.Count + "/" + roster.MaxSize + "：" + active
                + "　　已拥有 " + roster.UnlockedCount + "/" + CrewRosterCatalog.Count
                + "　　总星数 " + CampaignApi.Progress.TotalStars;
        }

        void RebuildCrewList()
        {
            if (crewListContainer == null)
                return;

            M3UiBuilder.ClearChildren(crewListContainer);

            for (int i = 0; i < CrewManagementApi.AllCrews.Count; i++)
            {
                CrewRosterEntry entry = CrewManagementApi.AllCrews[i];
                bool unlocked = CrewManagementApi.IsUnlocked(entry.Id);

                RectTransform row = M3UiBuilder.CreateRow(crewListContainer, i, RowHeight);

                string label = unlocked
                    ? entry.DisplayName + "　Lv." + CrewManagementApi.Progression.GetLevel(entry.Id)
                      + "　XP " + CrewManagementApi.Progression.GetXp(entry.Id)
                    : entry.DisplayName + "　（第 " + entry.UnlockLevelNumber + " 关通关后招募）";

                Text text = M3UiBuilder.CreateText("Label", row, label, 20,
                    TextAnchor.MiddleLeft, unlocked ? Color.white : new Color(0.6f, 0.62f, 0.68f, 1f));

                Button action = M3UiBuilder.CreateButton("Action", row, string.Empty, 18);
                string crewId = entry.Id;   // 闭包捕获：每轮独立变量，别直接捕 entry（结构体枚举变量）

                if (unlocked)
                {
                    bool active = CrewManagementApi.IsActive(crewId);
                    action.GetComponentInChildren<Text>().text = active ? "取消上阵" : "上阵";
                    action.onClick.AddListener(() => OnToggleActive(crewId));
                }
                else
                {
                    action.GetComponentInChildren<Text>().text = "未解锁";
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
                SetStatus("已把 " + DisplayName(crewId) + " 换下。");
            }
            else if (CrewManagementApi.AddToActive(crewId))
            {
                SetStatus("已把 " + DisplayName(crewId) + " 编入阵容。");
            }
            else
            {
                SetStatus("编成上限 " + CrewManagementApi.Roster.MaxSize + " 人，先换下一名再编入。");
            }

            // 名册事件已驱动刷新；这里不重复调用 Refresh，避免二次重建列表。
        }

        void OnLevelSelectClicked()
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                SetStatus("至少要编入 1 名船员才能出战。");
                return;
            }

            EventBus.Publish(ChangeSceneEvent, M3Scenes.LevelSelect);
        }

        void OnSaveClicked()
        {
            SetStatus(CampaignApi.SaveProgress()
                ? "已保存进度到槽位 " + CampaignApi.ProgressSlot + "。"
                : "存档不可用（未经过 Bootstrapper 启动）。");
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
                SetStatus("新船员加入：" + unlocked.DisplayName);
        }

        void OnLevelCompleted(object payload)
        {
            if (!(payload is CampaignLevelCompletedPayload completed))
                return;

            if (completed.Cleared)
            {
                SetStatus(completed.LevelId + " 通关（" + completed.Stars + "★），经验已发放给编成阵容。");
            }
            else
            {
                SetStatus(completed.LevelId + " 挑战失败，编成保留，可再战。");
            }

            // Refresh 已由 crew_roster_updated 驱动（关卡结算会广播经验变化）。
        }

        void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        static string DisplayName(string crewId)
        {
            return CrewRosterCatalog.TryGet(crewId, out CrewRosterEntry entry) ? entry.DisplayName : crewId;
        }
    }
}
