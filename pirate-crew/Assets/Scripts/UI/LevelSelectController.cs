using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 关卡选择界面（M3）：3 章节 × 5 关网格，显示解锁/星级状态，点选即进战斗。
    ///
    /// 【对应 Godot】<c>modules/campaign/ui/level_select.gd</c> 是空骨架
    ///   （<c>_update_display()</c> 只有 <c># TODO: Phase 3 生成关卡按钮网格</c>），
    ///   本界面把那句 TODO 落地为运行时生成按钮网格。
    ///
    /// 【M3 边界（界面上必须说清）】本轮 Battle 场景固定加载第 1 关竞技场，
    ///   选关只决定「结算记到哪一关」——见 <see cref="CampaignApi"/> 类头边界说明。
    ///   界面底部的提示文本会写明这一点，避免演示时误以为真的换了地图。
    /// </summary>
    public sealed class LevelSelectController : MonoBehaviour
    {
        const string ChangeSceneEvent = "change_scene";
        const string GoBackEvent = "go_back";

        const float RowHeight = 44f;

        [Header("引用（场景内直连）")]
        [SerializeField] Text headerText;
        [SerializeField] Text resultText;
        [SerializeField] Text statusText;
        [SerializeField] Transform chapterContainer;
        [SerializeField] Transform levelListContainer;
        [SerializeField] Button crewButton;
        [SerializeField] Button backButton;

        int _chapter = 1;

        void Awake()
        {
            CampaignApi.EnsureBootstrapped();

            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);

            EventBus.Subscribe(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
        }

        void Start()
        {
            _chapter = CampaignApi.CurrentChapter;
            if (_chapter < 1 || _chapter > CampaignCatalog.ChapterCount)
                _chapter = 1;

            ShowLastSettlement();
            Refresh();
        }

        void OnDestroy()
        {
            if (crewButton != null)
                crewButton.onClick.RemoveListener(OnCrewClicked);
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackClicked);

            EventBus.Unsubscribe(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
        }

        // ------------------------------------------------------------------
        // 结算横幅
        // ------------------------------------------------------------------

        /// <summary>把上一局结算结果显示一次（打完回来时 Start 会走到这里）。</summary>
        void ShowLastSettlement()
        {
            if (resultText == null)
                return;

            CampaignSettlement? settlement = CampaignApi.LastSettlement;
            if (settlement == null)
            {
                resultText.text = string.Empty;
                return;
            }

            CampaignSettlement value = settlement.Value;
            if (!value.Cleared)
            {
                resultText.text = value.LevelId + " 挑战失败：编成与武器保留，回管理界面可再战。";
            }
            else
            {
                string text = value.LevelId + " 通关！　评分 " + value.Score
                              + "　星级 " + value.Stars + "/" + StarRules.MaxStars;

                CrewRewardPayload? reward = CampaignApi.LastReward;
                if (reward != null && reward.Value.XpPerCrew > 0)
                    text += "　每名出战船员 +" + reward.Value.XpPerCrew + " XP";

                if (reward != null && reward.Value.UnlockedCrewIds.Length > 0)
                    text += "　新招募：" + string.Join("、", DisplayNames(reward.Value.UnlockedCrewIds));

                if (value.FirstClear)
                    text += "　（首次通关）";

                resultText.text = text;
            }

            CampaignApi.ClearLastResult();
            if (statusText != null)
                statusText.text = "提示：本轮 Battle 固定加载第 1 关竞技场，选关只决定结算归属的关卡。";
        }

        // ------------------------------------------------------------------
        // 列表
        // ------------------------------------------------------------------

        /// <summary>重建章节按钮与当前章节的关卡列表。</summary>
        public void Refresh()
        {
            RefreshHeader();
            RebuildChapterButtons();
            RebuildLevelList();
        }

        void RefreshHeader()
        {
            if (headerText == null)
                return;

            string next = CampaignApi.NextLevelId();
            string nextText = next == null
                ? "　全部关卡已通关"
                : "　建议下一关：" + CampaignCatalog.Get(next).DisplayName;

            headerText.text = "单人战役　第 " + _chapter + "/" + CampaignCatalog.ChapterCount + " 章"
                              + "　　总星数 " + CampaignApi.Progress.TotalStars
                              + "/" + (CampaignCatalog.TotalLevels * StarRules.MaxStars)
                              + nextText;
        }

        void RebuildChapterButtons()
        {
            if (chapterContainer == null)
                return;

            M3UiBuilder.ClearChildren(chapterContainer);

            for (int chapter = 1; chapter <= CampaignCatalog.ChapterCount; chapter++)
            {
                int captured = chapter;
                Button button = M3UiBuilder.CreateButton("Chapter" + chapter, chapterContainer,
                    "第 " + chapter + " 章", 20);

                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(160f, 44f);
                rect.anchoredPosition = new Vector2((chapter - 1) * 170f, 0f);

                if (captured == _chapter)
                    button.targetGraphic.color = new Color(0.22f, 0.42f, 0.32f, 1f);

                button.onClick.AddListener(() => OnChapterClicked(captured));
            }
        }

        void RebuildLevelList()
        {
            if (levelListContainer == null)
                return;

            M3UiBuilder.ClearChildren(levelListContainer);

            IReadOnlyList<CampaignLevel> levels = CampaignApi.GetChapterLevels(_chapter);
            for (int i = 0; i < levels.Count; i++)
            {
                CampaignLevel level = levels[i];
                bool unlocked = CampaignApi.IsLevelUnlocked(level.LevelId);
                int stars = CampaignApi.GetStars(level.LevelId);

                RectTransform row = M3UiBuilder.CreateRow(levelListContainer, i, RowHeight);

                string state = !unlocked
                    ? "未解锁"
                    : (stars > 0 ? "已通关　" + StarText(stars) : "可挑战");

                string label = level.DisplayName + "　" + state
                               + (level.HasData ? string.Empty : "　（占位竞技场）");

                Text text = M3UiBuilder.CreateText("Label", row, label, 20, TextAnchor.MiddleLeft,
                    unlocked ? Color.white : new Color(0.6f, 0.62f, 0.68f, 1f));

                Button action = M3UiBuilder.CreateButton("Action", row, string.Empty, 18);
                string levelId = level.LevelId;

                if (unlocked)
                {
                    action.GetComponentInChildren<Text>().text = stars > 0 ? "再战" : "出战";
                    action.onClick.AddListener(() => OnLevelClicked(levelId));
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

        static string StarText(int stars)
        {
            return new string('★', Mathf.Clamp(stars, 0, StarRules.MaxStars));
        }

        static string[] DisplayNames(string[] crewIds)
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

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------

        void OnChapterClicked(int chapter)
        {
            _chapter = chapter;
            CampaignApi.CurrentChapter = chapter;
            Refresh();
        }

        void OnLevelClicked(string levelId)
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                if (statusText != null)
                    statusText.text = "编成阵容为空：先去船员管理界面编入至少 1 名船员。";
                return;
            }

            // 成功时由 CampaignApi 直接请求切场景；失败（未解锁）留个提示。
            if (!CampaignApi.SelectLevel(levelId)
                && statusText != null)
            {
                statusText.text = "该关卡尚未解锁。";
            }
        }

        void OnCrewClicked()
        {
            EventBus.Publish(ChangeSceneEvent, M3Scenes.CrewManagement);
        }

        void OnBackClicked()
        {
            EventBus.Publish(GoBackEvent);
        }

        void OnCrewUnlocked(object payload)
        {
            if (statusText != null && payload is CrewUnlockedPayload unlocked)
                statusText.text = "新船员已加入名册：" + unlocked.DisplayName;
        }
    }
}
