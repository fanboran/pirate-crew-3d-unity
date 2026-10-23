using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Battle.Levels;
using PirateCrew.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 出海选关界面：**单列表**列出全部可玩关卡（关卡=唯一概念，创始人 2026-10-23 裁决），
    /// **按关卡号升序**排，点任意一行即出战。没有页签/章节/解锁链——全部内容都可直接打。
    ///
    /// 【加载行为】行 → <see cref="PendingLevel.SetPending"/>（单一待战槽），
    /// Battle 侧由 <c>LevelSourceResolver</c> 解析为该关的关卡资产（关卡自带布阵，不消耗编成阵容）。
    ///
    /// 【结算弹窗】战役结算链**占位冻结休眠中**（创始人从未设计星级/跨关卡玩法，
    /// 见 docs/项目/待办/地图重做.md），弹窗管线保留但当前无数据来源；
    /// 解冻设计跨关卡玩法时再启用（弹窗几何错位审计项 U1 届时必修）。
    /// </summary>
    public sealed class LevelSelectController : MonoBehaviour
    {
        const float RowHeight = 16f;   // 令牌按钮 24 + 上下各 12（2026-09-24 紧凑档；10 行 × 54 = 534 ≤ 列表面板 561）

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
        [SerializeField] TextMeshProUGUI settlementUnlockText;
        [SerializeField] TextMeshProUGUI settlementFirstClearText;
        [SerializeField] TextMeshProUGUI settlementStarRuleText;
        [Tooltip("3 枚星级图标（点亮/熄灭）。")]
        [SerializeField] Image[] settlementStars = new Image[3];
        [SerializeField] Button settlementReplayButton;
        [SerializeField] Button settlementBackButton;

        [Header("字体（由 ManagementSceneSetup 注入中文字体资产）")]
        [SerializeField] TMP_FontAsset bodyFont;

        string _settledMapId;

        /// <summary>结算弹窗开合动效驱动（菜单 juice 与战斗内同口径；数值/曲线全在 UiMotionRules）。</summary>
        UiMotion _motion;

        /// <summary>
        /// 运行时注入 UI 引用（<see cref="UiScreenBoot"/> 自建界面后调用，替代原装配器的
        /// 序列化回写——字段名与原 [SerializeField] 契约零改动）。必须在对象激活前调用。
        /// </summary>
        public void Bind(UiScreenBuilder.LevelRefs refs)
        {
            headerText = refs.HeaderText;
            chapterNameText = refs.ChapterNameText;
            statusText = refs.StatusText;
            chapterContainer = refs.ChapterContainer;
            levelListContainer = refs.LevelListContainer;
            crewButton = refs.CrewButton;
            backButton = refs.BackButton;
            bodyFont = UiScreenBuilder.BodyFont;
            settlementModal = refs.SettlementModal;
            settlementTitle = refs.SettlementTitle;
            settlementLevelText = refs.SettlementLevelText;
            settlementScoreText = refs.SettlementScoreText;
            settlementStarsText = refs.SettlementStarsText;
            settlementUnlockText = refs.SettlementUnlockText;
            settlementFirstClearText = refs.SettlementFirstClearText;
            settlementStarRuleText = refs.SettlementStarRuleText;
            settlementStars = refs.SettlementStars;
            settlementReplayButton = refs.SettlementReplayButton;
            settlementBackButton = refs.SettlementBackButton;
        }

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

            EventBus.Subscribe(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
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
            _settledMapId = value.MapId;

            if (settlementTitle != null)
                settlementTitle.text = value.Cleared ? UiStrings.SettlementWin : UiStrings.SettlementFail;

            if (settlementLevelText != null)
                settlementLevelText.text = UiTextRules.SettlementLevel(UiTextRules.MapDisplayName(value.MapId));

            if (settlementScoreText != null)
                settlementScoreText.text = UiTextRules.SettlementScore(value.Score);

            if (settlementStarsText != null)
                settlementStarsText.text = UiTextRules.SettlementStars(value.Stars, StarRules.MaxStars);

            ApplyStars(value.Stars);

            if (settlementUnlockText != null)
            {
                CrewRewardPayload? reward = CampaignApi.LastReward;
                string[] unlockedIds = reward != null ? reward.Value.UnlockedCrewIds : new string[0];
                bool hasUnlock = unlockedIds.Length > 0;
                settlementUnlockText.text = hasUnlock
                    ? UiTextRules.SettlementUnlock(string.Join("、", UiTextRules.CrewDisplayNames(unlockedIds)))
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
                TextMeshProUGUI replayLabel = RuntimeUiBuilder.GetButtonLabel(settlementReplayButton);
                if (replayLabel != null)
                    replayLabel.text = UiStrings.LevelReplay;
            }

            if (settlementStarRuleText != null)
                settlementStarRuleText.text = UiStrings.SettlementStarRuleHint;

            if (settlementBackButton != null)
            {
                TextMeshProUGUI backLabel = RuntimeUiBuilder.GetButtonLabel(settlementBackButton);
                if (backLabel != null)
                    backLabel.text = UiStrings.SettlementBackToSelect;
            }

            if (settlementModal != null)
                RuntimeUiBuilder.OpenPanel(settlementModal, _motion);

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
                settlementStars[i].color = lit ? PixelSkin.MidOf(PixelTone.Primary) : UiSkin.WithAlpha(PixelSkin.Ink, 0.35f);
            }
        }

        /// <summary>结算弹窗「返回选图」：按钮反馈 + 关弹窗（Add/Remove 同一方法目标，退订可靠）。</summary>
        void OnSettlementBackClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(settlementBackButton, true, _motion);
            CloseSettlement();
        }

        void CloseSettlement()
        {
            RuntimeUiBuilder.ClosePanel(settlementModal, _motion);
        }

        void OnReplayClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(settlementReplayButton, true, _motion);
            // 结算链占位冻结休眠中，本按钮当前不可达；重载即重进待战/兜底关卡
            //（SceneLoader 对同名目标自动不压栈）。
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

            // 页头报的关数必须与列表行数一致（列表 = 全部关卡资产）。
            headerText.text = string.Format(UiStrings.LevelSelectHeaderFormat,
                LevelAssetLibrary.Levels.Count);

            if (chapterNameText != null)
                chapterNameText.text = UiStrings.LevelTabWorldSeas;
        }

        /// <summary>
        /// 选关列表（一张表）：全部关卡资产，按关卡号升序。
        /// </summary>
        void RebuildLevelList()
        {
            if (levelListContainer == null)
                return;

            UiTextUtil.WarnIfMissing(bodyFont, "选关列表");
            RuntimeUiBuilder.ClearChildren(levelListContainer);

            List<LevelListRow> rows = BuildLevelList();
            for (int i = 0; i < rows.Count; i++)
            {
                LevelListRow entry = rows[i];
                // 【list_item 口径】行 = theme list_item：整行即命中区（面三态就是它的按钮皮），
                // 行内不挂文字按钮（自创件）——单击行 = 进该关。
                RectTransform row = RuntimeUiBuilder.CreateRow(levelListContainer, i, RowHeight);
                Button rowButton = RuntimeUiBuilder.RowButton(row);

                TextMeshProUGUI text = RuntimeUiBuilder.CreateText("Label", row, entry.Label, UiSkin.Font.Body,
                    TextAlignmentOptions.Left,
                    RuntimeUiBuilder.ListItemTextColor(ListItemState.Normal), bodyFont);

                int levelNumber = entry.LevelNumber;
                rowButton.onClick.AddListener(() => OnLevelClicked(levelNumber, rowButton));

                RuntimeUiBuilder.LayoutRowContent(row, text, RowHeight);
            }
        }

        /// <summary>
        /// 关卡表：编号取自关卡号本身，按号升序——不依赖声明顺序，
        /// 也不需要在别处维护一张"顺序表"。
        /// </summary>
        static List<LevelListRow> BuildLevelList()
        {
            IReadOnlyList<LevelAssetPayload> levels = LevelAssetLibrary.Levels;
            var rows = new List<LevelListRow>(levels.Count);

            for (int i = 0; i < levels.Count; i++)
            {
                LevelAssetPayload level = levels[i];
                rows.Add(new LevelListRow(level.levelNumber, level.displayName));
            }

            rows.Sort((a, b) => a.LevelNumber.CompareTo(b.LevelNumber));
            return rows;
        }

        /// <summary>选关列表的一行（纯视图数据）。</summary>
        readonly struct LevelListRow
        {
            /// <summary>关卡号（排序键）。</summary>
            public readonly int LevelNumber;

            /// <summary>行标签（theme list_item 的唯一一条左对齐文本）。</summary>
            public readonly string Label;

            public LevelListRow(int levelNumber, string label)
            {
                LevelNumber = levelNumber;
                Label = label;
            }
        }

        // 船员显示名的本地拷贝已收编进 UiTextRules（CrewDisplayNames，与结算 HUD、
        // 船员管理同源），本类只做调用。

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------

        /// <summary>
        /// 出战关卡：<see cref="PendingLevel.SetPending"/> 成功即切 Battle。
        /// 关卡自带布阵（关卡资产里的 units）→ 不做空编成拦截。
        /// </summary>
        void OnLevelClicked(int levelNumber, Button action)
        {
            RuntimeUiBuilder.ButtonFeedback(action, true, _motion);
            if (PendingLevel.SetPending(levelNumber))
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
            else if (statusText != null)
                statusText.text = UiStrings.LevelStatusShowcaseMissing;
        }

        void OnCrewClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(crewButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.CrewManagement);
        }

        void OnBackClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(backButton, true, _motion);
            EventBus.Publish(SceneEvents.GoBack);
        }

        void OnCrewUnlocked(CrewUnlockedPayload unlocked)
        {
            if (statusText != null)
                statusText.text = string.Format(UiStrings.LevelStatusNewCrewFormat, unlocked.DisplayName);
        }
    }
}
