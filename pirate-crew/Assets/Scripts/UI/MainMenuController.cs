using PirateCrew.Campaign;
using PirateCrew.Core;
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
    ///   - 单人战役 → <see cref="M3Scenes.LevelSelect"/>；船员管理 → <see cref="M3Scenes.CrewManagement"/>
    ///     （M3 打通；Godot 版这两处是 TODO pass）。
    ///   - 顺带承担 M3 管理循环的**接线点**：订阅结算事件 + 读取存档进度
    ///     （见 <see cref="CampaignApi.EnsureBootstrapped"/> / <see cref="CampaignApi.LoadProgress"/>）。
    ///
    /// 【注意】
    ///   文本组件使用 UnityEngine.UI.Text（本工程未安装 TextMeshPro 包，见 SceneSetup 说明）。
    ///   TODO: 安装 com.unity.textmeshpro 后把 Text 换成 TextMeshProUGUI。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        /// <summary>EventBus 场景切换事件名（payload 为 string 场景名，与 SceneLoader 约定一致）。</summary>
        const string ChangeSceneEvent = "change_scene";

        [SerializeField] Button battleButton;
        [SerializeField] Button campaignButton;
        [SerializeField] Button crewButton;
        [SerializeField] Text statusText;

        bool _loadedProgress;

        void Awake()
        {
            if (battleButton != null)
                battleButton.onClick.AddListener(OnBattleClicked);
            if (campaignButton != null)
                campaignButton.onClick.AddListener(OnCampaignClicked);
            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);

            // M3 管理循环接线：订阅战斗结算事件（幂等），并读一次存档进度。
            CampaignApi.EnsureBootstrapped();
            _loadedProgress = CampaignApi.LoadProgress();
        }

        void Start()
        {
            if (statusText != null)
                statusText.text = _loadedProgress ? "已读取存档进度。" : string.Empty;
        }

        void OnDestroy()
        {
            if (battleButton != null)
                battleButton.onClick.RemoveListener(OnBattleClicked);
            if (campaignButton != null)
                campaignButton.onClick.RemoveListener(OnCampaignClicked);
            if (crewButton != null)
                crewButton.onClick.RemoveListener(OnCrewClicked);
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
    }
}
