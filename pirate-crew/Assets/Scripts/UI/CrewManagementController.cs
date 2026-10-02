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
    /// 【界面性质】本界面是 M3 新增的最小可用版。
    ///
    /// 【接线约定】UI 不持有模块内部对象：
    ///   · 状态读写走 <see cref="CrewManagementApi"/>；
    ///   · 场景切换走 EventBus 切场景/返回频道（<see cref="SceneEvents.ChangeScene"/> / <see cref="SceneEvents.GoBack"/>）；
    ///   · 名册变化订阅 <see cref="CrewManagementEvents"/> 频道刷新列表。
    ///
    /// 【本波次改造】文本 TMP 化 + 全中文（<see cref="UiStrings"/> / <see cref="UiTextRules"/>）；
    /// 行/按钮换羊皮纸 + 木板九宫格；行字号走 <see cref="UiSkin.Font"/>（正文档）
    ///（别名 = <see cref="UiSkin.Font.Body"/> 36：像素栅格并档后旧「双轨降档」退役，
    /// RuntimeUiBuilder 直传渲染，字号真值 = <see cref="UiSkin.Font"/>）。
    /// 事件契约与订阅清单不变。
    /// </summary>
    public sealed class CrewManagementController : MonoBehaviour
    {

        const float RowHeight = 16f;   // 令牌按钮 24 + 上下各 12（2026-09-24 紧凑档；与选关行密度一致）

        [Header("引用（场景内直连）")]
        [SerializeField] TextMeshProUGUI summaryText;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] Transform crewListContainer;
        [SerializeField] Button levelSelectButton;
        [SerializeField] Button saveButton;
        [SerializeField] Button backButton;

        [Header("字体（由 ManagementSceneSetup 注入中文字体资产）")]
        [Tooltip("正文中文字体（霞鹜文楷 Medium SDF）；运行时建列表行用。")]
        [SerializeField] TMP_FontAsset bodyFont;

        /// <summary>
        /// 运行时注入 UI 引用（<see cref="UiScreenBoot"/> 自建界面后调用，替代原装配器的
        /// 序列化回写——字段名与原 [SerializeField] 契约零改动）。必须在对象激活前调用。
        /// </summary>
        public void Bind(UiScreenBuilder.CrewRefs refs)
        {
            summaryText = refs.SummaryText;
            statusText = refs.StatusText;
            crewListContainer = refs.CrewListContainer;
            levelSelectButton = refs.LevelSelectButton;
            saveButton = refs.SaveButton;
            backButton = refs.BackButton;
            bodyFont = UiScreenBuilder.BodyFont;
        }

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

            EventBus.Subscribe(CrewManagementEvents.RosterUpdated, OnRosterChanged);
            EventBus.Subscribe(CrewManagementEvents.CrewUnlocked, OnCrewUnlocked);
            EventBus.Subscribe(CampaignEvents.MapCompleted, OnMapCompleted);
        }

        void Start()
        {
            SetStatus(string.Empty);
            Refresh();

            // 列表首现动效（只在进场播一次；之后的 roster 事件重建不重播，避免每次上阵都闪动）。
            if (crewListContainer != null)
                RuntimeUiBuilder.OpenPanel(crewListContainer.gameObject, _motion);
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
            EventBus.Unsubscribe(CampaignEvents.MapCompleted, OnMapCompleted);
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
                : string.Join("、", UiTextRules.CrewDisplayNames(roster.Active));

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
            RuntimeUiBuilder.ClearChildren(crewListContainer);

            for (int i = 0; i < CrewManagementApi.AllCrews.Count; i++)
            {
                CrewRosterEntry entry = CrewManagementApi.AllCrews[i];
                bool unlocked = CrewManagementApi.IsUnlocked(entry.Id);
                bool active = unlocked && CrewManagementApi.IsActive(entry.Id);

                // 行态 = theme list_item 三分支：锁定 = 禁用暗底；已上阵 = 选中金底（深字）；其余常态。
                ListItemState rowState = !unlocked ? ListItemState.Disabled
                    : active ? ListItemState.Selected
                    : ListItemState.Normal;

                // 【list_item 口径】行 = theme list_item：**整行即命中区**（面三态就是它的按钮皮），
                // 行内不再挂文字按钮——list_item 只有「纯色面 + 一条左对齐文本（+可选图标）」，
                // 行尾挂 44px 文字按钮是自创件（Aseprite 行内的交互件只有 timeline_box 那种小图标开关）。
                RectTransform row = RuntimeUiBuilder.CreateRow(crewListContainer, i, RowHeight, rowState);
                Button rowButton = RuntimeUiBuilder.RowButton(row);

                string label = unlocked
                    ? UiTextRules.CrewRow(entry.DisplayName,
                        CrewManagementApi.Progression.GetLevel(entry.Id),
                        CrewManagementApi.Progression.GetXp(entry.Id))
                    : UiTextRules.CrewRowLocked(entry.DisplayName, entry.UnlockStars);

                // 字色与行态同源（theme list_item：常态 text 灰 / 选中金底深字 / 禁用 disabled）。
                TextMeshProUGUI text = RuntimeUiBuilder.CreateText("Label", row, label, UiSkin.Font.Body,
                    TextAlignmentOptions.Left,
                    RuntimeUiBuilder.ListItemTextColor(rowState), bodyFont);

                string crewId = entry.Id;   // 闭包捕获：每轮独立变量
                if (unlocked)
                {
                    // 单击行 = 上阵 / 撤下（行即命中区，无行内按钮）
                    rowButton.onClick.AddListener(() => OnToggleActive(crewId, rowButton));
                }
                else if (rowButton != null)
                {
                    // 锁定行 = list_item 的 disabled 态：面已表态，点击不接（Aseprite 禁用行不可选）。
                    rowButton.interactable = false;
                }

                RuntimeUiBuilder.LayoutRowContent(row, text, RowHeight);
            }
        }

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------

        void OnToggleActive(string crewId, Button action)
        {
            if (CrewManagementApi.IsActive(crewId))
            {
                CrewManagementApi.RemoveFromActive(crewId);
                RuntimeUiBuilder.ButtonFeedback(action, true, _motion);
                SetStatus(string.Format(UiStrings.CrewStatusRemovedFormat, DisplayName(crewId)));
            }
            else if (CrewManagementApi.AddToActive(crewId))
            {
                RuntimeUiBuilder.ButtonFeedback(action, true, _motion);
                SetStatus(string.Format(UiStrings.CrewStatusEnlistedFormat, DisplayName(crewId)));
            }
            else
            {
                RuntimeUiBuilder.ButtonFeedback(action, false, _motion);
                SetStatus(string.Format(UiStrings.CrewStatusFullFormat, CrewManagementApi.Roster.MaxSize));
            }

            // 名册事件已驱动刷新；这里不重复调用 Refresh，避免二次重建列表。
        }

        void OnLevelSelectClicked()
        {
            if (CrewManagementApi.Roster.Active.Count == 0)
            {
                RuntimeUiBuilder.ButtonFeedback(levelSelectButton, false, _motion);
                SetStatus(UiStrings.CrewStatusEmptyRoster);
                return;
            }

            RuntimeUiBuilder.ButtonFeedback(levelSelectButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.LevelSelect);
        }

        void OnSaveClicked()
        {
            bool saved = CampaignApi.SaveProgress();
            RuntimeUiBuilder.ButtonFeedback(saveButton, saved, _motion);
            SetStatus(saved
                ? string.Format(UiStrings.CrewStatusSavedFormat, CampaignApi.ProgressSlot)
                : UiStrings.CrewStatusSaveFailed);
        }

        void OnBackClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(backButton, true, _motion);
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
            string levelName = UiTextRules.MapDisplayName(completed.MapId);
            if (completed.Cleared)
                SetStatus(string.Format(UiStrings.CrewStatusClearedFormat, levelName, completed.Stars));
            else
                SetStatus(string.Format(UiStrings.CrewStatusFailedFormat, levelName));

            // Refresh 已由 CrewManagementEvents.RosterUpdated 频道驱动（关卡结算会广播经验变化）。
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

        // 船员显示名 / 海图名的本地拷贝已收编进 UiTextRules（CrewDisplayNames / MapDisplayName，
        // 与结算 HUD、选关同源），本类只做调用。
    }
}
