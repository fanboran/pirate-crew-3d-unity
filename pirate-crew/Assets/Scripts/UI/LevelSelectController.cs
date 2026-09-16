using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 关卡选择界面（M3）：3 章节 × 5 关网格，显示解锁/星级状态，点选即进战斗。
    ///
    /// 【M3 边界（界面上必须说清）】本轮 Battle 场景固定加载第 1 关竞技场，
    ///   选关只决定「结算记到哪一关」——底部提示会写明，避免演示时误以为真的换了地图。
    ///
    /// 【本波次改造】
    ///   · 结算由「顶部单行横幅」改为**模态弹窗**（规范 §3.6，AI 提案）；字段照旧取
    ///     <see cref="CampaignApi.LastSettlement"/> / <see cref="CampaignApi.LastReward"/>，数据源不变；
    ///   · 星级由「★★☆」文本改为 3 枚程序化黄铜五角星图标（规范 §3.4 / §5.4）；
    ///   · 文本 TMP 化 + 全中文；章节页签显示完整章节名（加勒比新手海域等）。
    /// 事件契约不变（只发 change_scene / go_back，不新增事件）。
    /// </summary>
    public sealed class LevelSelectController : MonoBehaviour
    {
        const string ChangeSceneEvent = "change_scene";
        const string GoBackEvent = "go_back";

        const float RowHeight = 44f;

        [Header("引用（场景内直连）")]
        [SerializeField] TextMeshProUGUI headerText;
        [SerializeField] TextMeshProUGUI chapterNameText;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] Transform chapterContainer;
        [SerializeField] Transform levelListContainer;
        [SerializeField] Button crewButton;
        [SerializeField] Button backButton;

        [Header("结算弹窗（模态，规范 §3.6）")]
        [SerializeField] GameObject settlementModal;
        [SerializeField] TextMeshProUGUI settlementTitle;
        [SerializeField] TextMeshProUGUI settlementLevelText;
        [SerializeField] TextMeshProUGUI settlementScoreText;
        [SerializeField] TextMeshProUGUI settlementStarsText;
        [SerializeField] TextMeshProUGUI settlementXpText;
        [SerializeField] TextMeshProUGUI settlementUnlockText;
        [SerializeField] TextMeshProUGUI settlementFirstClearText;
        [SerializeField] TextMeshProUGUI settlementStarRuleText;
        [Tooltip("3 枚星级图标（点亮/熄灭）。")]
        [SerializeField] Image[] settlementStars = new Image[3];
        [SerializeField] Button settlementReplayButton;
        [SerializeField] Button settlementBackButton;

        [Header("字体（由 M3SceneSetup 注入中文字体资产）")]
        [SerializeField] TMP_FontAsset bodyFont;

        int _chapter = 1;
        string _settledLevelId;

        void Awake()
        {
            CampaignApi.EnsureBootstrapped();

            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
            if (settlementReplayButton != null)
                settlementReplayButton.onClick.AddListener(OnReplayClicked);
            if (settlementBackButton != null)
                settlementBackButton.onClick.AddListener(CloseSettlement);

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
            if (settlementReplayButton != null)
                settlementReplayButton.onClick.RemoveListener(OnReplayClicked);
            if (settlementBackButton != null)
                settlementBackButton.onClick.RemoveListener(CloseSettlement);

            EventBus.Unsubscribe(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
        }

        // ------------------------------------------------------------------
        // 结算弹窗（§3.6：一次说清 胜负 / 星级 / 评分 / 经验 / 招募）
        // ------------------------------------------------------------------

        /// <summary>把上一局结算结果以模态弹窗显示一次（打完回来时 Start 会走到这里）。</summary>
        void ShowLastSettlement()
        {
            CampaignSettlement? settlement = CampaignApi.LastSettlement;
            if (settlement == null)
            {
                CloseSettlement();
                return;
            }

            CampaignSettlement value = settlement.Value;
            _settledLevelId = value.LevelId;

            if (settlementTitle != null)
                settlementTitle.text = value.Cleared ? UiStrings.SettlementWin : UiStrings.SettlementFail;

            if (settlementLevelText != null)
                settlementLevelText.text = UiTextRules.SettlementLevel(
                    LevelDisplayName(value.LevelId, value.LevelNumber));

            if (settlementScoreText != null)
                settlementScoreText.text = UiTextRules.SettlementScore(value.Score);

            if (settlementStarsText != null)
                settlementStarsText.text = UiTextRules.SettlementStars(value.Stars, StarRules.MaxStars);

            ApplyStars(value.Stars);

            if (settlementXpText != null)
            {
                CrewRewardPayload? reward = CampaignApi.LastReward;
                int xp = reward != null ? reward.Value.XpPerCrew : 0;
                settlementXpText.text = UiTextRules.SettlementXp(xp);
                settlementXpText.gameObject.SetActive(value.Cleared);
            }

            if (settlementUnlockText != null)
            {
                CrewRewardPayload? reward = CampaignApi.LastReward;
                string[] unlockedIds = reward != null ? reward.Value.UnlockedCrewIds : new string[0];
                bool hasUnlock = unlockedIds.Length > 0;
                settlementUnlockText.text = hasUnlock
                    ? UiTextRules.SettlementUnlock(string.Join("、", DisplayNames(unlockedIds)))
                    : string.Empty;
                settlementUnlockText.gameObject.SetActive(hasUnlock);
            }

            if (settlementFirstClearText != null)
            {
                settlementFirstClearText.text = UiStrings.SettlementRowFirstClear;
                settlementFirstClearText.gameObject.SetActive(value.FirstClear);
            }

            if (settlementReplayButton != null)
            {
                settlementReplayButton.gameObject.SetActive(value.Cleared);
                TextMeshProUGUI replayLabel = settlementReplayButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (replayLabel != null)
                    replayLabel.text = UiStrings.LevelReplay;
            }

            if (settlementStarRuleText != null)
                settlementStarRuleText.text = UiStrings.SettlementStarRuleHint;

            if (settlementBackButton != null)
            {
                TextMeshProUGUI backLabel = settlementBackButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (backLabel != null)
                    backLabel.text = UiStrings.SettlementBackToSelect;
            }

            if (settlementModal != null)
                settlementModal.SetActive(true);

            CampaignApi.ClearLastResult();

            if (statusText != null)
                statusText.text = string.Empty;
        }

        void ApplyStars(int stars)
        {
            if (settlementStars == null)
                return;

            for (int i = 0; i < settlementStars.Length; i++)
            {
                if (settlementStars[i] == null)
                    continue;

                bool lit = i < stars;
                settlementStars[i].color = lit ? UiTheme.Brass : UiTheme.WithAlpha(UiTheme.Ink, 0.35f);
            }
        }

        void CloseSettlement()
        {
            if (settlementModal != null)
                settlementModal.SetActive(false);
        }

        void OnReplayClicked()
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                CloseSettlement();
                if (statusText != null)
                    statusText.text = UiStrings.LevelStatusEmptyRoster;
                return;
            }

            if (!string.IsNullOrEmpty(_settledLevelId))
                CampaignApi.SelectLevel(_settledLevelId);
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
                ? UiStrings.LevelHeaderAllClear
                : string.Format(UiStrings.LevelHeaderNextFormat, LevelDisplayName(next, LevelNumberOf(next)));

            headerText.text = UiTextRules.LevelHeader(
                UiStrings.LevelTitle, _chapter, CampaignCatalog.ChapterCount,
                CampaignApi.Progress.TotalStars, CampaignCatalog.TotalLevels * StarRules.MaxStars)
                + nextText;

            if (chapterNameText != null)
                chapterNameText.text = UiTextRules.ChapterName(_chapter);
        }

        void RebuildChapterButtons()
        {
            if (chapterContainer == null)
                return;

            M3UiBuilder.ClearChildren(chapterContainer);

            for (int chapter = 1; chapter <= CampaignCatalog.ChapterCount; chapter++)
            {
                int captured = chapter;
                // 当前页签用黄铜底（规范 §6.3：页签是「当前页」不是「选中对象」，不用青色）。
                UiSprites.Kind skin = captured == _chapter
                    ? UiSprites.Kind.ButtonBrass
                    : UiSprites.Kind.ButtonWood;
                Button button = M3UiBuilder.CreateButton("Chapter" + chapter, chapterContainer,
                    string.Format(UiStrings.LevelChapterFormat, chapter), UiTheme.FontBody, bodyFont, skin);

                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(160f, 44f);
                rect.anchoredPosition = new Vector2((chapter - 1) * 170f, 0f);

                TextMeshProUGUI label = M3UiBuilder.GetButtonLabel(button);
                if (label != null)
                    label.color = UiTheme.Ink;

                button.onClick.AddListener(() => OnChapterClicked(captured));
            }
        }

        void RebuildLevelList()
        {
            if (levelListContainer == null)
                return;

            UiTextUtil.WarnIfMissing(bodyFont, "选关列表");
            M3UiBuilder.ClearChildren(levelListContainer);

            IReadOnlyList<CampaignLevel> levels = CampaignApi.GetChapterLevels(_chapter);
            for (int i = 0; i < levels.Count; i++)
            {
                CampaignLevel level = levels[i];
                bool unlocked = CampaignApi.IsLevelUnlocked(level.LevelId);
                int stars = CampaignApi.GetStars(level.LevelId);

                RectTransform row = M3UiBuilder.CreateRow(levelListContainer, i, RowHeight);

                string state = !unlocked
                    ? UiStrings.LevelRowLocked
                    : (stars > 0 ? UiStrings.LevelRowCleared : UiStrings.LevelRowAvailable);

                string label = level.DisplayName + "　" + state
                               + (level.HasData ? string.Empty : UiStrings.LevelRowPlaceholder);

                TextMeshProUGUI text = M3UiBuilder.CreateText("Label", row, label, UiTheme.FontBody,
                    TextAlignmentOptions.MidlineLeft,
                    unlocked ? UiTheme.Ink : UiTheme.WithAlpha(UiTheme.Ink, UiTheme.DisabledAlpha),
                    bodyFont);

                // 星级图标（3 枚，点亮 = 黄铜，熄灭 = 暗）——已通关行才显示。
                if (stars > 0)
                    M3UiBuilder.CreateStarRow(row, stars, StarRules.MaxStars, 24f);

                Button action = M3UiBuilder.CreateButton("Action", row, string.Empty, UiTheme.FontHint,
                    bodyFont);
                TextMeshProUGUI actionLabel = M3UiBuilder.GetButtonLabel(action);
                string levelId = level.LevelId;

                if (unlocked)
                {
                    if (actionLabel != null)
                        actionLabel.text = stars > 0 ? UiStrings.LevelReplay : UiStrings.LevelFight;
                    action.onClick.AddListener(() => OnLevelClicked(levelId));
                }
                else
                {
                    if (actionLabel != null)
                        actionLabel.text = UiStrings.LevelRowLocked;
                    action.interactable = false;
                    action.targetGraphic.color = M3UiBuilder.DisabledButtonColor;
                }

                M3UiBuilder.LayoutRowContent(row, text, action, RowHeight);
            }
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

        /// <summary>关卡 id / 序号 → 中文关卡名。</summary>
        static string LevelDisplayName(string levelId, int levelNumber)
        {
            if (CampaignCatalog.TryGet(levelId, out CampaignLevel level))
                return level.DisplayName;

            return UiTextRules.LevelName(levelNumber);
        }

        static int LevelNumberOf(string levelId)
        {
            return CampaignCatalog.TryGet(levelId, out CampaignLevel level) ? level.LevelNumber : 0;
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
                    statusText.text = UiStrings.LevelStatusEmptyRoster;
                return;
            }

            // 成功时由 CampaignApi 直接请求切场景；失败（未解锁）留个提示。
            if (!CampaignApi.SelectLevel(levelId)
                && statusText != null)
            {
                statusText.text = UiStrings.LevelStatusLocked;
            }
        }

        void OnCrewClicked()
        {
            EventBus.Publish(ChangeSceneEvent, SceneNames.CrewManagement);
        }

        void OnBackClicked()
        {
            EventBus.Publish(GoBackEvent);
        }

        void OnCrewUnlocked(object payload)
        {
            if (statusText != null && payload is CrewUnlockedPayload unlocked)
                statusText.text = string.Format(UiStrings.LevelStatusNewCrewFormat, unlocked.DisplayName);
        }
    }
}
