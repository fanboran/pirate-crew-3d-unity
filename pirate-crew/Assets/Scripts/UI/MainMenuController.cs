using PirateCrew.Campaign;
using PirateCrew.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 主菜单控制器（翻译自 Godot <c>modules/pirate_crew/scripts/ui/main_menu.gd</c>）。
    ///
    /// 【行为】
    ///   - 进入战斗：发布 EventBus "change_scene"（载荷为场景名），由 SceneLoader 统一处理，
    ///     以此示范事件驱动架构——UI 不直接持有 SceneLoader 引用。
    ///   - 单人战役 → <see cref="M3Scenes.LevelSelect"/>；船员管理 → <see cref="M3Scenes.CrewManagement"/>。
    ///   - 设置：**界面占位**（规范 §3.7 / §4.8），只在本场景内开关一个未接线的设置面板，不发任何事件。
    ///   - 退出游戏：直接退出；编辑器下停止播放（破坏性操作，按钮用危险红）。
    ///   - 顺带承担 M3 管理循环的**接线点**：订阅结算事件 + 读取存档进度。
    ///
    /// 【本波次改造】文本改为 <see cref="TextMeshProUGUI"/>（中文字体由场景装配注入），
    /// 全中文文案取自 <see cref="UiStrings"/>；新增「设置」「退出游戏」两个按钮（规范 §4.2，AI 提案）。
    /// 事件契约不变：只发布既有的 "change_scene"。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        /// <summary>EventBus 场景切换事件名（payload 为 string 场景名，与 SceneLoader 约定一致）。</summary>
        const string ChangeSceneEvent = "change_scene";

        [SerializeField] Button battleButton;
        [SerializeField] Button campaignButton;
        [SerializeField] Button crewButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button quitButton;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] TextMeshProUGUI versionText;

        [Header("设置（界面占位，未接线）")]
        [Tooltip("设置面板根节点；默认隐藏，由「设置」按钮开关。")]
        [SerializeField] GameObject settingsPanel;
        [SerializeField] Button settingsBackButton;

        bool _loadedProgress;

        void Awake()
        {
            if (battleButton != null)
                battleButton.onClick.AddListener(OnBattleClicked);
            if (campaignButton != null)
                campaignButton.onClick.AddListener(OnCampaignClicked);
            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnSettingsClicked);
            if (quitButton != null)
                quitButton.onClick.AddListener(OnQuitClicked);
            if (settingsBackButton != null)
                settingsBackButton.onClick.AddListener(CloseSettings);

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            if (versionText != null)
                versionText.text = UiStrings.MainVersion;

            // M3 管理循环接线：订阅战斗结算事件（幂等），并读一次存档进度。
            CampaignApi.EnsureBootstrapped();
            _loadedProgress = CampaignApi.LoadProgress();
        }

        void Start()
        {
            if (statusText != null)
                statusText.text = _loadedProgress ? UiStrings.MainStatusLoaded : UiStrings.MainStatusNoSave;
        }

        void OnDestroy()
        {
            if (battleButton != null)
                battleButton.onClick.RemoveListener(OnBattleClicked);
            if (campaignButton != null)
                campaignButton.onClick.RemoveListener(OnCampaignClicked);
            if (crewButton != null)
                crewButton.onClick.RemoveListener(OnCrewClicked);
            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OnSettingsClicked);
            if (quitButton != null)
                quitButton.onClick.RemoveListener(OnQuitClicked);
            if (settingsBackButton != null)
                settingsBackButton.onClick.RemoveListener(CloseSettings);
        }

        /// <summary>进入战斗：走 EventBus → SceneLoader 链路。这是「非战役入口」，先放弃可能残留的待结算关卡。</summary>
        void OnBattleClicked()
        {
            CampaignApi.AbortPendingLevel();
            EventBus.Publish(ChangeSceneEvent, SceneNames.Battle);
        }

        void OnCampaignClicked()
        {
            EventBus.Publish(ChangeSceneEvent, M3Scenes.LevelSelect);
        }

        void OnCrewClicked()
        {
            EventBus.Publish(ChangeSceneEvent, M3Scenes.CrewManagement);
        }

        /// <summary>设置面板为界面占位：只开关面板，不发事件、不改设置。</summary>
        void OnSettingsClicked()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(true);
        }

        void CloseSettings()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(false);
        }

        void OnQuitClicked()
        {
            // 编辑器里 Application.Quit() 不生效（Unity 会忽略），属预期；构建后正常退出。
            // 刻意不引用 UnityEditor 命名空间：本文件在运行时程序集（PirateCrew.Runtime）里，
            // 不应依赖编辑器程序集。
            Application.Quit();
        }
    }
}
