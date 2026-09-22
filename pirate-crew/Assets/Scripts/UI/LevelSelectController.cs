using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 出海选关界面（M4）：**单列表**列出全部可玩内容——2 张手作样板关（云端漫步 / 天空之岛）
    /// 与 8 张世界海域图，**按关卡号升序**排（样板 1、3 在前，海图 101–108 在后），点任意一行即出战。
    /// 一代战役的页签/章节/解锁链随一代退场删除——全部内容都可直接打；
    /// 已通关的海图行显示星级（结算进度记在海图 id 上，见 <see cref="CampaignApi"/>），
    /// 样板关不记星（进度表里没有它的键），故样板行不带星级。
    ///
    /// 【加载行为】海图行 → <c>WorldMapRuntime.SetPending</c>；样板行 → <c>SetPendingShowcase</c>，
    /// 两者互斥。Battle 侧由 <c>LevelSourceResolver</c> 一处分叉：海图走 kit 岛 + 俯视海图 + 大海域海面，
    /// 样板关走关卡资产自带的竞技场（关卡自带布阵，两条路都不消耗编成阵容）。
    ///
    /// 【结算弹窗】打完回本页时以模态弹窗显示一次 胜负/星级/评分/经验/招募
    ///（数据源 = <see cref="CampaignApi.LastSettlement"/> / <see cref="CampaignApi.LastReward"/>）；
    /// 星级为 3 枚程序化黄铜五角星图标（规范 §3.4 / §5.4）。
    /// </summary>
    public sealed class LevelSelectController : MonoBehaviour
    {
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

        string _settledMapId;

        /// <summary>结算弹窗开合动效驱动（菜单 juice 与战斗内同口径；数值/曲线全在 UiMotionRules）。</summary>
        UiMotion _motion;

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();

            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
            if (settlementReplayButton != null)
                settlementReplayButton.onClick.AddListener(OnReplayClicked);
            if (settlementBackButton != null)
                settlementBackButton.onClick.AddListener(OnSettlementBackClicked);

            EventBus.Subscribe<CrewUnlockedPayload>(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
        }

        void Start()
        {
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

            EventBus.Unsubscribe<CrewUnlockedPayload>(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
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
            _settledMapId = value.MapId;

            if (settlementTitle != null)
                settlementTitle.text = value.Cleared ? UiStrings.SettlementWin : UiStrings.SettlementFail;

            if (settlementLevelText != null)
                settlementLevelText.text = UiTextRules.SettlementLevel(MapDisplayName(value.MapId));

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

        /// <summary>结算弹窗「返回选图」：按钮反馈 + 关弹窗（Add/Remove 同一方法目标，退订可靠）。</summary>
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
            M3UiBuilder.ButtonFeedback(settlementReplayButton, true, _motion);
            // 再战同一张海图：SetPending(同图) + 原地重载（SceneLoader 对同名目标自动不压栈）。
            if (!string.IsNullOrEmpty(_settledMapId) && WorldMapRuntime.SetPending(_settledMapId))
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
        }

        // ------------------------------------------------------------------
        // 列表
        // ------------------------------------------------------------------

        /// <summary>重建选关列表。</summary>
        public void Refresh()
        {
            RefreshHeader();
            RebuildLevelList();
        }

        void RefreshHeader()
        {
            if (headerText == null)
                return;

            int showcases = LevelAssetLibrary.Levels.Count;
            int maps = WorldMapCatalog.Count;
            // 页头报的关数必须与列表行数一致（列表 = 样板关 + 海图）。
            // 星级满分只算海图（8 × 3 = 24）：星级进度的键是海图 id，样板关不记星，
            // 把它算进满分会让"累计星数"永远差一截。
            headerText.text = string.Format(UiStrings.LevelSelectHeaderFormat,
                showcases + maps, showcases, maps,
                CampaignApi.Progress.TotalStars, maps * StarRules.MaxStars);

            if (chapterNameText != null)
                chapterNameText.text = UiStrings.LevelTabWorldSeas;
        }

        /// <summary>
        /// 选关列表（一张表）：手作样板关 + 全部世界海图，按关卡号升序。
        /// 样板行不画星级（不记星）、按钮「出战」；海图行保留星级与「出海」。
        /// </summary>
        void RebuildLevelList()
        {
            if (levelListContainer == null)
                return;

            UiTextUtil.WarnIfMissing(bodyFont, "选关列表");
            M3UiBuilder.ClearChildren(levelListContainer);

            List<LevelListRow> rows = BuildLevelList();
            for (int i = 0; i < rows.Count; i++)
            {
                LevelListRow entry = rows[i];
                RectTransform row = M3UiBuilder.CreateRow(levelListContainer, i, RowHeight);

                TextMeshProUGUI text = M3UiBuilder.CreateText("Label", row, entry.Label, UiTheme.FontBody,
                    TextAlignmentOptions.MidlineLeft, PixelSkin.TextColorOn(PixelTone.Light), bodyFont);

                // 星级图标（3 枚，点亮 = 黄铜，熄灭 = 暗）——只有记星的海图行才画。
                if (entry.Stars > 0)
                    M3UiBuilder.CreateStarRow(row, entry.Stars, StarRules.MaxStars, 24f);

                Button action = M3UiBuilder.CreateButton("Action", row, string.Empty, UiTheme.FontHint,
                    bodyFont);
                TextMeshProUGUI actionLabel = M3UiBuilder.GetButtonLabel(action);
                if (actionLabel != null)
                    actionLabel.text = entry.ActionLabel;

                if (entry.WorldMapId != null)
                {
                    string mapId = entry.WorldMapId;
                    action.onClick.AddListener(() => OnWorldMapClicked(mapId, action));
                }
                else
                {
                    int levelNumber = entry.LevelNumber;
                    action.onClick.AddListener(() => OnShowcaseClicked(levelNumber, action));
                }

                M3UiBuilder.LayoutRowContent(row, text, action, RowHeight);
            }
        }

        /// <summary>
        /// 合并两类内容成一张表：编号取自关卡号本身（样板关 1/3、海图 101–108，两段不重叠），
        /// 排序后样板关自然靠前——不依赖声明顺序，也不需要在别处维护一张"顺序表"。
        /// </summary>
        static List<LevelListRow> BuildLevelList()
        {
            IReadOnlyList<LevelAssetPayload> showcases = LevelAssetLibrary.Levels;
            IReadOnlyList<WorldMapDefinition> maps = WorldMapCatalog.All;
            var rows = new List<LevelListRow>(showcases.Count + maps.Count);

            for (int i = 0; i < showcases.Count; i++)
            {
                LevelAssetPayload level = showcases[i];
                rows.Add(new LevelListRow(
                    level.levelNumber,
                    level.displayName + "　" + UiStrings.LevelRowShowcaseTag,
                    UiStrings.LevelFight,
                    stars: 0,
                    worldMapId: null));
            }

            for (int i = 0; i < maps.Count; i++)
            {
                WorldMapDefinition map = maps[i];
                int stars = CampaignApi.GetStars(map.Id);
                string label = map.DisplayName + "　"
                               + Mathf.RoundToInt(map.SpanX) + "×" + Mathf.RoundToInt(map.SpanZ)
                               + "　" + (stars > 0 ? UiStrings.WorldRowCleared : UiStrings.WorldRowAvailable);
                rows.Add(new LevelListRow(
                    map.LevelNumber, label, UiStrings.WorldSetSail, stars, map.Id));
            }

            rows.Sort((a, b) => a.LevelNumber.CompareTo(b.LevelNumber));
            return rows;
        }

        /// <summary>
        /// 选关列表的一行（纯视图数据）：把"样板关 / 海图"抹平成同一形状，
        /// 于是排序、建行、按钮派发都只有一条代码路径。
        /// <see cref="WorldMapId"/> 非 null = 海图行，null = 样板关行。
        /// </summary>
        readonly struct LevelListRow
        {
            /// <summary>关卡号（排序键；样板关 1–3 / 海图 101–108）。</summary>
            public readonly int LevelNumber;

            /// <summary>行标签（名称 + 图幅/标识 + 状态）。</summary>
            public readonly string Label;

            /// <summary>动作按钮文案。</summary>
            public readonly string ActionLabel;

            /// <summary>已得星数（样板关恒 0 = 不画星级）。</summary>
            public readonly int Stars;

            /// <summary>海图 id；null = 样板关。</summary>
            public readonly string WorldMapId;

            public LevelListRow(int levelNumber, string label, string actionLabel, int stars, string worldMapId)
            {
                LevelNumber = levelNumber;
                Label = label;
                ActionLabel = actionLabel;
                Stars = stars;
                WorldMapId = worldMapId;
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

        /// <summary>海图 id → 中文海图名（目录查不到时回退原始 id）。</summary>
        static string MapDisplayName(string mapId)
        {
            return WorldMapCatalog.TryGet(mapId, out WorldMapDefinition map)
                ? map.DisplayName
                : mapId;
        }

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------

        /// <summary>出海：<see cref="WorldMapRuntime.SetPending"/> 成功即切 Battle。</summary>
        void OnWorldMapClicked(string mapId, Button action)
        {
            M3UiBuilder.ButtonFeedback(action, true, _motion);
            // 海图战用地图自带布阵（map.Spawns），不消耗编成阵容 → 不做空编成拦截。
            if (WorldMapRuntime.SetPending(mapId))
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
            else if (statusText != null)
                statusText.text = UiStrings.WorldStatusMapMissing;
        }

        /// <summary>
        /// 出战样板关：<see cref="WorldMapRuntime.SetPendingShowcase"/> 成功即切 Battle。
        /// 与海图同理，样板关自带布阵（关卡资产里的 units）→ 不做空编成拦截。
        /// SetPendingShowcase 自己会清掉待战海图，所以刚打完海图再点样板关不会串内容。
        /// </summary>
        void OnShowcaseClicked(int levelNumber, Button action)
        {
            M3UiBuilder.ButtonFeedback(action, true, _motion);
            if (WorldMapRuntime.SetPendingShowcase(levelNumber))
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
            else if (statusText != null)
                statusText.text = UiStrings.LevelStatusShowcaseMissing;
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

        void OnCrewUnlocked(CrewUnlockedPayload unlocked)
        {
            if (statusText != null)
                statusText.text = string.Format(UiStrings.LevelStatusNewCrewFormat, unlocked.DisplayName);
        }
    }
}
