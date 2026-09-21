using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.Audio;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Settings;
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
    ///   - 单人战役 → <see cref="SceneNames.LevelSelect"/>；船员管理 → <see cref="SceneNames.CrewManagement"/>。
    ///   - 设置（**真接线**）：音量四路实时改 <see cref="AudioService"/>、画质档与全屏立即切
    ///     <see cref="VideoSettingsService"/>；全部改动在关面板时统一落盘（设置槽 9）。
    ///   - 退出游戏：先弹确认框（破坏性操作），确认后退出；编辑器下 Application.Quit 不生效属预期。
    ///   - 顺带承担 M3 管理循环的**接线点**：订阅结算事件 + 读取存档进度。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        /// <summary>EventBus 场景切换事件名（payload 为 string 场景名，与 SceneLoader 约定一致）。</summary>

        [SerializeField] Button battleButton;
        [SerializeField] Button campaignButton;
        [SerializeField] Button crewButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button quitButton;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] TextMeshProUGUI versionText;

        [Header("设置面板（真接线）")]
        [Tooltip("设置面板根节点；默认隐藏，由「设置」按钮开关。")]
        [SerializeField] GameObject settingsPanel;
        [SerializeField] Button settingsBackButton;
        [SerializeField] Button settingsRestoreButton;
        [SerializeField] Slider masterVolumeSlider;
        [SerializeField] Slider sfxVolumeSlider;
        [SerializeField] Slider musicVolumeSlider;
        [SerializeField] Slider ambientVolumeSlider;
        [SerializeField] Button qualityHighButton;
        [SerializeField] Button qualitySmoothButton;
        [SerializeField] Button fullscreenOnButton;
        [SerializeField] Button fullscreenOffButton;

        [Header("退出确认")]
        [SerializeField] GameObject quitConfirmPanel;
        [SerializeField] Button quitConfirmOkButton;
        [SerializeField] Button quitConfirmCancelButton;

        /// <summary>面板开合动效驱动（菜单 juice 与战斗内同口径；数值/曲线全在 UiMotionRules）。</summary>
        UiMotion _motion;

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();

            if (battleButton != null)
                battleButton.onClick.AddListener(OnBattleClicked);
            if (campaignButton != null)
                campaignButton.onClick.AddListener(OnCampaignClicked);
            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
            if (settingsButton != null)
                settingsButton.onClick.AddListener(OpenSettings);
            if (quitButton != null)
                quitButton.onClick.AddListener(OnQuitClicked);

            if (settingsBackButton != null)
                settingsBackButton.onClick.AddListener(CloseSettings);
            if (settingsRestoreButton != null)
                settingsRestoreButton.onClick.AddListener(RestoreDefaults);

            WireVolumeSlider(masterVolumeSlider, AudioCategory.Master);
            WireVolumeSlider(sfxVolumeSlider, AudioCategory.Sfx);
            WireVolumeSlider(musicVolumeSlider, AudioCategory.Music);
            WireVolumeSlider(ambientVolumeSlider, AudioCategory.Ambient);

            if (qualityHighButton != null)
                qualityHighButton.onClick.AddListener(() =>
                {
                    M3UiBuilder.ButtonFeedback(qualityHighButton, true, _motion);
                    SetQuality(VideoSettingsStore.QualityHigh);
                });
            if (qualitySmoothButton != null)
                qualitySmoothButton.onClick.AddListener(() =>
                {
                    M3UiBuilder.ButtonFeedback(qualitySmoothButton, true, _motion);
                    SetQuality(VideoSettingsStore.QualitySmooth);
                });
            if (fullscreenOnButton != null)
                fullscreenOnButton.onClick.AddListener(() =>
                {
                    M3UiBuilder.ButtonFeedback(fullscreenOnButton, true, _motion);
                    SetFullscreen(true);
                });
            if (fullscreenOffButton != null)
                fullscreenOffButton.onClick.AddListener(() =>
                {
                    M3UiBuilder.ButtonFeedback(fullscreenOffButton, true, _motion);
                    SetFullscreen(false);
                });

            if (quitConfirmOkButton != null)
                quitConfirmOkButton.onClick.AddListener(ConfirmQuit);
            if (quitConfirmCancelButton != null)
                quitConfirmCancelButton.onClick.AddListener(CancelQuit);

            if (settingsPanel != null)
                settingsPanel.SetActive(false);
            if (quitConfirmPanel != null)
                quitConfirmPanel.SetActive(false);

            if (versionText != null)
                versionText.text = UiStrings.MainVersion;

            // M3 管理循环接线（订阅结算事件 + 首次读进度）已上移到组合根：
            // Core/GameEntryPoint → CampaignApi.Install()。这里只做一次"进菜单时从存档刷新进度"，
            // 因为本场景可能出现"打完一局回到菜单"的情形（无存档时静默返回 false）。
            CampaignApi.LoadProgress();
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
                settingsButton.onClick.RemoveListener(OpenSettings);
            if (quitButton != null)
                quitButton.onClick.RemoveListener(OnQuitClicked);
            if (settingsBackButton != null)
                settingsBackButton.onClick.RemoveListener(CloseSettings);
            if (settingsRestoreButton != null)
                settingsRestoreButton.onClick.RemoveListener(RestoreDefaults);
            if (quitConfirmOkButton != null)
                quitConfirmOkButton.onClick.RemoveListener(ConfirmQuit);
            if (quitConfirmCancelButton != null)
                quitConfirmCancelButton.onClick.RemoveListener(CancelQuit);
            // 音量滑条/画质与全屏按钮的监听用 lambda 捕获，组件销毁时随之失效，无需手动退订。
        }

        // ------------------------------------------------------------------
        // 导航
        // ------------------------------------------------------------------

        /// <summary>
        /// 进入战斗（一代退场后）：直接出海**第一张海图**（目录声明序）；
        /// 想挑图走「战役」入口的选图页。目录为空（数据异常）时退回选图页兜底。
        /// </summary>
        void OnBattleClicked()
        {
            M3UiBuilder.ButtonFeedback(battleButton, true, _motion);
            IReadOnlyList<WorldMapDefinition> maps = WorldMapCatalog.All;
            if (maps.Count > 0 && WorldMapRuntime.SetPending(maps[0].Id))
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
            else
                EventBus.Publish(SceneEvents.ChangeScene, SceneNames.LevelSelect);
        }

        void OnCampaignClicked()
        {
            M3UiBuilder.ButtonFeedback(campaignButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.LevelSelect);
        }

        void OnCrewClicked()
        {
            M3UiBuilder.ButtonFeedback(crewButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.CrewManagement);
        }

        // ------------------------------------------------------------------
        // 设置（真接线）
        // ------------------------------------------------------------------

        void OpenSettings()
        {
            if (settingsPanel == null)
                return;

            RefreshSettingsControls();
            M3UiBuilder.OpenPanel(settingsPanel, _motion);
        }

        void CloseSettings()
        {
            M3UiBuilder.ButtonFeedback(settingsBackButton, true, _motion);

            // 关面板统一落盘：拖动滑条的过程不写盘，避免一次拖动几十次 IO。
            AudioService.SaveVolumes();
            if (VideoSettingsService.Instance != null)
                VideoSettingsService.Instance.SaveSettings();

            M3UiBuilder.ClosePanel(settingsPanel, _motion);

            if (statusText != null)
                statusText.text = UiStrings.MainStatusSettingsSaved;
        }

        void RestoreDefaults()
        {
            M3UiBuilder.ButtonFeedback(settingsRestoreButton, true, _motion);
            AudioService.SetVolume(AudioCategory.Master, AudioSettingsStore.DefaultMasterVolume);
            AudioService.SetVolume(AudioCategory.Sfx, AudioSettingsStore.DefaultSfxVolume);
            AudioService.SetVolume(AudioCategory.Music, AudioSettingsStore.DefaultMusicVolume);
            AudioService.SetVolume(AudioCategory.Ambient, AudioSettingsStore.DefaultAmbientVolume);

            if (VideoSettingsService.Instance != null)
            {
                VideoSettingsService.Instance.SetFullscreen(VideoSettingsStore.DefaultFullscreen);
                VideoSettingsService.Instance.SetQuality(VideoSettingsStore.DefaultQuality);
            }

            RefreshSettingsControls();
        }

        void WireVolumeSlider(Slider slider, AudioCategory category)
        {
            if (slider == null)
                return;

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(value => AudioService.SetVolume(category, value));
        }

        void SetQuality(int qualityIndex)
        {
            if (VideoSettingsService.Instance != null)
                VideoSettingsService.Instance.SetQuality(qualityIndex);
            RefreshVideoChips();
        }

        void SetFullscreen(bool fullscreen)
        {
            if (VideoSettingsService.Instance != null)
                VideoSettingsService.Instance.SetFullscreen(fullscreen);
            RefreshVideoChips();
        }

        /// <summary>打开面板时把控件对齐到真实状态（音量 ← AudioService，画质/全屏 ← VideoSettingsService）。</summary>
        void RefreshSettingsControls()
        {
            SetSliderNoNotify(masterVolumeSlider, GetVolumeOr(AudioCategory.Master));
            SetSliderNoNotify(sfxVolumeSlider, GetVolumeOr(AudioCategory.Sfx));
            SetSliderNoNotify(musicVolumeSlider, GetVolumeOr(AudioCategory.Music));
            SetSliderNoNotify(ambientVolumeSlider, GetVolumeOr(AudioCategory.Ambient));

            RefreshVideoChips();
        }

        static float GetVolumeOr(AudioCategory category)
        {
            if (AudioService.IsAvailable)
                return AudioService.GetVolume(category);

            return category switch
            {
                AudioCategory.Master => AudioSettingsStore.DefaultMasterVolume,
                AudioCategory.Sfx => AudioSettingsStore.DefaultSfxVolume,
                AudioCategory.Music => AudioSettingsStore.DefaultMusicVolume,
                _ => AudioSettingsStore.DefaultAmbientVolume,
            };
        }

        void RefreshVideoChips()
        {
            int quality = VideoSettingsService.Instance != null
                ? VideoSettingsService.Instance.QualityIndex
                : VideoSettingsStore.DefaultQuality;
            bool fullscreen = VideoSettingsService.Instance != null
                ? VideoSettingsService.Instance.Fullscreen
                : VideoSettingsStore.DefaultFullscreen;

            SetChipSelected(qualityHighButton, quality == VideoSettingsStore.QualityHigh);
            SetChipSelected(qualitySmoothButton, quality == VideoSettingsStore.QualitySmooth);
            SetChipSelected(fullscreenOnButton, fullscreen);
            SetChipSelected(fullscreenOffButton, !fullscreen);
        }

        /// <summary>编程改值走 SetValueWithoutNotify，避免把「刷新」误当成一次用户输入。</summary>
        static void SetSliderNoNotify(Slider slider, float value)
        {
            if (slider == null)
                return;

            slider.SetValueWithoutNotify(Mathf.Clamp01(value));
        }

        /// <summary>
        /// 选项块选中态：选中 = 黄铜亮色底 + 深墨字，未选 = 原玻璃底 + 浅米字。
        /// （玻璃纪律是"贴图别用 color 调皮肤"；这里是运行期的一次性状态高亮，乘色即可，
        /// 且 chip 底色 white × 黄铜 = 状态色、深玻璃 × 黄铜仍可辨。）
        /// </summary>
        static void SetChipSelected(Button chip, bool selected)
        {
            if (chip == null)
                return;

            if (chip.image != null)
                chip.image.color = selected ? UiTheme.BrassLight : Color.white;

            TextMeshProUGUI label = chip.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.color = selected ? UiTheme.Ink : UiTheme.TextLight;
        }

        // ------------------------------------------------------------------
        // 退出（确认框）
        // ------------------------------------------------------------------

        void OnQuitClicked()
        {
            if (quitConfirmPanel != null)
            {
                if (settingsPanel != null)
                    M3UiBuilder.ClosePanel(settingsPanel, _motion);
                M3UiBuilder.OpenPanel(quitConfirmPanel, _motion);
                return;
            }

            // 没装配确认框时退回旧行为（构建后正常退出；编辑器里 Unity 会忽略）。
            // 刻意不引用 UnityEditor 命名空间：本文件在运行时程序集里。
            Application.Quit();
        }

        void ConfirmQuit()
        {
            M3UiBuilder.ButtonFeedback(quitConfirmOkButton, true, _motion);
            Application.Quit();
        }

        void CancelQuit()
        {
            M3UiBuilder.ButtonFeedback(quitConfirmCancelButton, true, _motion);
            M3UiBuilder.ClosePanel(quitConfirmPanel, _motion);
        }
    }
}
