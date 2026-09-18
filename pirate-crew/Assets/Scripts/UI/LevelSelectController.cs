using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Battle.WorldMaps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 关卡选择界面（M3）：3 章节 × 5 关网格，显示解锁/星级状态，点选即进战斗；
    /// 第 4 个「大海域」页签列出 M4 的 8 张世界地图，点选出海（UI 审计 P0-1 的修复）。
    ///
    /// 【加载行为】战役页签：出战加载所选关卡的竞技场（<c>CampaignApi.SelectLevel</c> →
    ///   Battle 场景按 <c>LevelCatalog</c> 数据搭建），星级与结算记到该关。
    ///   大海域页签：<c>WorldMapRuntime.SetPending</c> → Battle 走 WorldMaps 分支（kit 岛 +
    ///   俯视海图 + 全图级旗舰）；海图战用地图自带布阵，不消耗编成阵容、不记战役进度。
    ///
    /// 【双通道互斥】SetPending / SelectLevel 各自清对方通道的待战态
    /// （见 <see cref="WorldMapRuntime"/> 类注释），本页是两条通道唯一的交汇 UI。
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

        const float RowHeight = 44f;
        const float TabSpacing = 170f;
        const float TabWidth = 160f;

        /// <summary>
        /// 「大海域」页签的伪章节号：战役章节是 1..<see cref="CampaignCatalog.ChapterCount"/>，
        /// 负数永不撞；不写入 <see cref="CampaignApi.CurrentChapter"/>（章节记忆只归战役页签）。
        /// </summary>
        const int WorldTabChapter = -1;

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

        /// <summary>结算弹窗开合动效驱动（菜单 juice 与战斗内同口径；数值/曲线全在 UiMotionRules）。</summary>
        UiMotion _motion;

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();
            CampaignApi.EnsureBootstrapped();

            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
            if (settlementReplayButton != null)
                settlementReplayButton.onClick.AddListener(OnReplayClicked);
            if (settlementBackButton != null)
                settlementBackButton.onClick.AddListener(OnSettlementBackClicked);

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
                settlementBackButton.onClick.RemoveListener(OnSettlementBackClicked);

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
                M3UiBuilder.OpenPanel(settlementModal, _motion);

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

        /// <summary>结算弹窗「返回选关」：按钮反馈 + 关弹窗（Add/Remove 同一方法目标，退订可靠）。</summary>
        void OnSettlementBackClicked()
        {
            M3UiBuilder.ButtonFeedback(settlementBackButton, true, _motion);
            CloseSettlement();
        }

        void CloseSettlement()
        {
            M3UiBuilder.ClosePanel(settlementModal, _motion);
        }

        void OnReplayClicked()
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                M3UiBuilder.ButtonFeedback(settlementReplayButton, false, _motion);
                CloseSettlement();
                if (statusText != null)
                    statusText.text = UiStrings.LevelStatusEmptyRoster;
                return;
            }

            M3UiBuilder.ButtonFeedback(settlementReplayButton, true, _motion);
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
            if (_chapter == WorldTabChapter)
            {
                if (headerText != null)
                    headerText.text = string.Format(UiStrings.WorldSeasHeaderFormat, WorldMapCatalog.Count);
                if (chapterNameText != null)
                    chapterNameText.text = UiStrings.LevelTabWorldSeas;
                return;
            }

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

            // 页签组相对容器左缘居中：场景把容器按 3 个战役页签的宽度锚定（520 宽），
            // 第 4 个「大海域」是运行时新增，按整体组宽回推起点，加页签后视觉仍居中。
            int tabCount = CampaignCatalog.ChapterCount + 1;
            float groupWidth = TabSpacing * (tabCount - 1) + TabWidth;
            var containerRect = chapterContainer as RectTransform;
            float containerWidth = containerRect != null && containerRect.rect.width > 0f
                ? containerRect.rect.width
                : 520f;
            float groupOffset = (containerWidth - groupWidth) * 0.5f;

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
                rect.sizeDelta = new Vector2(TabWidth, 44f);
                rect.anchoredPosition = new Vector2(groupOffset + (chapter - 1) * TabSpacing, 0f);

                TextMeshProUGUI label = M3UiBuilder.GetButtonLabel(button);
                if (label != null)
                    label.color = UiTheme.Ink;

                button.onClick.AddListener(() =>
                {
                    M3UiBuilder.ButtonFeedback(button, true, _motion);
                    OnChapterClicked(captured);
                });
            }

            // 「大海域」页签（M4 八图入口）。
            bool worldSelected = _chapter == WorldTabChapter;
            Button worldButton = M3UiBuilder.CreateButton("ChapterWorldSeas", chapterContainer,
                UiStrings.LevelTabWorldSeas, UiTheme.FontBody, bodyFont,
                worldSelected ? UiSprites.Kind.ButtonBrass : UiSprites.Kind.ButtonWood);

            RectTransform worldRect = worldButton.GetComponent<RectTransform>();
            worldRect.anchorMin = new Vector2(0f, 0.5f);
            worldRect.anchorMax = new Vector2(0f, 0.5f);
            worldRect.pivot = new Vector2(0f, 0.5f);
            worldRect.sizeDelta = new Vector2(TabWidth, 44f);
            worldRect.anchoredPosition = new Vector2(groupOffset + CampaignCatalog.ChapterCount * TabSpacing, 0f);

            TextMeshProUGUI worldLabel = M3UiBuilder.GetButtonLabel(worldButton);
            if (worldLabel != null)
                worldLabel.color = UiTheme.Ink;

            worldButton.onClick.AddListener(() =>
            {
                M3UiBuilder.ButtonFeedback(worldButton, true, _motion);
                OnWorldTabClicked();
            });
        }

        void RebuildLevelList()
        {
            if (_chapter == WorldTabChapter)
            {
                RebuildWorldMapList();
                return;
            }

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
                    action.onClick.AddListener(() => OnLevelClicked(levelId, action));
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

        /// <summary>
        /// 大海域列表（M4 八图）：全部可出战——海图战用地图自带布阵（<c>map.Spawns</c>），
        /// 没有解锁/星级语义；日后若要接战役进度解锁，在此分支扩展。
        /// </summary>
        void RebuildWorldMapList()
        {
            if (levelListContainer == null)
                return;

            UiTextUtil.WarnIfMissing(bodyFont, "海图列表");
            M3UiBuilder.ClearChildren(levelListContainer);

            IReadOnlyList<WorldMapDefinition> maps = WorldMapCatalog.All;
            for (int i = 0; i < maps.Count; i++)
            {
                WorldMapDefinition map = maps[i];
                RectTransform row = M3UiBuilder.CreateRow(levelListContainer, i, RowHeight);

                string label = map.DisplayName + "　"
                               + Mathf.RoundToInt(map.SpanX) + "×" + Mathf.RoundToInt(map.SpanZ)
                               + "　" + UiStrings.WorldRowAvailable;

                TextMeshProUGUI text = M3UiBuilder.CreateText("Label", row, label, UiTheme.FontBody,
                    TextAlignmentOptions.MidlineLeft, UiTheme.Ink, bodyFont);

                Button action = M3UiBuilder.CreateButton("Action", row, string.Empty, UiTheme.FontHint,
                    bodyFont);
                TextMeshProUGUI actionLabel = M3UiBuilder.GetButtonLabel(action);
                if (actionLabel != null)
                    actionLabel.text = UiStrings.WorldSetSail;

                string mapId = map.Id;
                action.onClick.AddListener(() => OnWorldMapClicked(mapId, action));

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

        void OnWorldTabClicked()
        {
            // 不写 CampaignApi.CurrentChapter：章节记忆只归战役页签，点回战役页签即恢复。
            _chapter = WorldTabChapter;
            Refresh();
        }

        /// <summary>出海（M4 世界图）：<see cref="WorldMapRuntime.SetPending"/> 成功即切 Battle。</summary>
        void OnWorldMapClicked(string mapId, Button action)
        {
            M3UiBuilder.ButtonFeedback(action, true, _motion);
            // 海图战用地图自带布阵（map.Spawns），不消耗编成阵容 → 不做空编成拦截；
            // SetPending 成功时已同步清战役出征注入（双通道互斥，见 WorldMapRuntime 类注释）。
            if (WorldMapRuntime.SetPending(mapId))
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
            else if (statusText != null)
                statusText.text = UiStrings.WorldStatusMapMissing;
        }

        void OnLevelClicked(string levelId, Button action)
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                M3UiBuilder.ButtonFeedback(action, false, _motion);
                if (statusText != null)
                    statusText.text = UiStrings.LevelStatusEmptyRoster;
                return;
            }

            M3UiBuilder.ButtonFeedback(action, true, _motion);
            // 成功时由 CampaignApi 直接请求切场景；失败（未解锁）留个提示。
            if (!CampaignApi.SelectLevel(levelId)
                && statusText != null)
            {
                statusText.text = UiStrings.LevelStatusLocked;
            }
        }

        void OnCrewClicked()
        {
            M3UiBuilder.ButtonFeedback(crewButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.CrewManagement);
        }

        void OnBackClicked()
        {
            M3UiBuilder.ButtonFeedback(backButton, true, _motion);
            EventBus.Publish(SceneEvents.GoBack);
        }

        void OnCrewUnlocked(object payload)
        {
            if (statusText != null && payload is CrewUnlockedPayload unlocked)
                statusText.text = string.Format(UiStrings.LevelStatusNewCrewFormat, unlocked.DisplayName);
        }
    }
}
