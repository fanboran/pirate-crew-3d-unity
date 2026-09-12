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
    ///   - 单人战役 / 船员管理：M3 开放，点击时仅在状态栏提示（对应 Godot 的 TODO pass）。
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

        void Awake()
        {
            if (battleButton != null)
                battleButton.onClick.AddListener(OnBattleClicked);
            if (campaignButton != null)
                campaignButton.onClick.AddListener(OnCampaignClicked);
            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
        }

        void Start()
        {
            // 对应 Godot 里场景初始无提示语的状态。
            if (statusText != null)
                statusText.text = string.Empty;
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

        /// <summary>进入战斗：走 EventBus → SceneLoader 链路。</summary>
        void OnBattleClicked()
        {
            EventBus.Publish(ChangeSceneEvent, SceneNames.Battle);
        }

        void OnCampaignClicked()
        {
            ShowPlaceholder("战役模块将在 M3 开放");
        }

        void OnCrewClicked()
        {
            ShowPlaceholder("船员模块将在 M3 开放");
        }

        void ShowPlaceholder(string message)
        {
            if (statusText != null)
                statusText.text = message;

            Debug.Log("[MainMenu] " + message);
        }
    }
}
