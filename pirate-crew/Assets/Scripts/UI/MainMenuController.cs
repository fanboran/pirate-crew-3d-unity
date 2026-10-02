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
    /// 主菜单控制器。
    ///
    /// 【行为】
    ///   - 进入战斗：发布 EventBus 切场景频道（<see cref="SceneEvents.ChangeScene"/>，载荷为场景名），
    ///     由 SceneLoader 统一处理，以此示范事件驱动架构——UI 不直接持有 SceneLoader 引用。
    ///   - 单人战役 → <see cref="SceneNames.LevelSelect"/>；船员管理 → <see cref="SceneNames.CrewManagement"/>。
    ///   - 设置（**真接线**）：音量四路实时改 <see cref="AudioService"/>、画质档与全屏立即切
    ///     <see cref="VideoSettingsService"/>；全部改动在关面板时统一落盘（设置槽 9）。
    ///   - 退出游戏：先弹确认框（破坏性操作），确认后退出；编辑器下 Application.Quit 不生效属预期。
    ///   - 顺带承担 M3 管理循环的**接线点**：订阅结算事件 + 读取存档进度。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        /// <summary>EventBus 切场景频道（<see cref="SceneEvents.ChangeScene"/>；payload 为 string 场景名，与 SceneLoader 约定一致）。</summary>

        [SerializeField] Button battleButton;
        [SerializeField] Button crewButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button showcaseButton;
        [SerializeField] Button quitButton;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] TextMeshProUGUI versionText;

        [Header("设置面板（真接线）")]
        [Tooltip("设置面板根节点；默认隐藏，由「设置」按钮开关。")]
        [SerializeField] GameObject settingsPanel;
        [SerializeField] Button settingsBackButton;
        [SerializeField] Button settingsCloseButton;
        [SerializeField] Button settingsRestoreButton;
        [SerializeField] Slider masterVolumeSlider;
        [SerializeField] Slider sfxVolumeSlider;
        [SerializeField] Slider musicVolumeSlider;
        [SerializeField] Slider ambientVolumeSlider;
        [SerializeField] Button qualityHighButton;
        [SerializeField] Button qualitySmoothButton;
        [SerializeField] Button fullscreenOnButton;
        [SerializeField] Button fullscreenOffButton;

        [Header("像素比例与分辨率（真接线）")]
        [Tooltip("像素比例四档选项块；真源 PixelScaleService（UI 画布与像素化渲染同档）。")]
        [SerializeField] Button pixelScale2Button;
        [SerializeField] Button pixelScale3Button;
        [SerializeField] Button pixelScale4Button;
        [SerializeField] Button pixelScaleAutoButton;
        [Tooltip("分辨率循环钮：点击在 跟随系统 → 各可选分辨率 间循环；真源 VideoSettingsService。")]
        [SerializeField] Button resolutionButton;

        [Header("退出确认")]
        [SerializeField] GameObject quitConfirmPanel;
        [SerializeField] Button quitConfirmOkButton;
        [SerializeField] Button quitConfirmCancelButton;

        /// <summary>面板开合动效驱动（菜单 juice 与战斗内同口径；数值/曲线全在 UiMotionRules）。</summary>
        UiMotion _motion;

        /// <summary>
        /// 运行时注入 UI 引用（<see cref="UiScreenBoot"/> 自建界面后调用，替代原装配器的
        /// 序列化回写——字段名与原 [SerializeField] 契约零改动）。必须在对象激活前调用。
        /// </summary>
        public void Bind(UiScreenBuilder.MainMenuRefs refs)
        {
            battleButton = refs.BattleButton;
            crewButton = refs.CrewButton;
            settingsButton = refs.SettingsButton;
            showcaseButton = refs.ShowcaseButton;
            quitButton = refs.QuitButton;
            statusText = refs.StatusText;
            versionText = refs.VersionText;
            settingsPanel = refs.SettingsPanel;
            settingsBackButton = refs.SettingsBackButton;
            settingsCloseButton = refs.SettingsCloseButton;
            settingsRestoreButton = refs.SettingsRestoreButton;
            masterVolumeSlider = refs.MasterSlider;
            sfxVolumeSlider = refs.SfxSlider;
            musicVolumeSlider = refs.MusicSlider;
            ambientVolumeSlider = refs.AmbientSlider;
            qualityHighButton = refs.QualityHighButton;
            qualitySmoothButton = refs.QualitySmoothButton;
            fullscreenOnButton = refs.FullscreenOnButton;
            fullscreenOffButton = refs.FullscreenOffButton;
            pixelScale2Button = refs.PixelScale2Button;
            pixelScale3Button = refs.PixelScale3Button;
            pixelScale4Button = refs.PixelScale4Button;
            pixelScaleAutoButton = refs.PixelScaleAutoButton;
            resolutionButton = refs.ResolutionButton;
            quitConfirmPanel = refs.QuitConfirmPanel;
            quitConfirmOkButton = refs.QuitConfirmOkButton;
            quitConfirmCancelButton = refs.QuitConfirmCancelButton;
        }

        void Awake()
        {
            _motion = gameObject.AddComponent<UiMotion>();

            // 主菜单窗体可拖动（创始人 2026-09-25：按住标题带拖——Aseprite 窗口感）。
            Transform menuWindow = transform.parent != null ? transform.parent.Find("MenuWindow") : null;
            if (menuWindow is RectTransform menuRect)
                DebugUi.WindowDragger.Attach(menuRect);

            if (battleButton != null)
                battleButton.onClick.AddListener(OnBattleClicked);
            if (crewButton != null)
                crewButton.onClick.AddListener(OnCrewClicked);
            if (settingsButton != null)
                settingsButton.onClick.AddListener(OpenSettings);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (showcaseButton != null)
                showcaseButton.onClick.AddListener(OnShowcaseClicked);
#endif
            if (quitButton != null)
                quitButton.onClick.AddListener(OnQuitClicked);

            if (settingsBackButton != null)
                settingsBackButton.onClick.AddListener(CloseSettings);
            if (settingsCloseButton != null)
                settingsCloseButton.onClick.AddListener(CloseSettings);   // 标题带右上 ×：同返回
            if (settingsRestoreButton != null)
                settingsRestoreButton.onClick.AddListener(RestoreDefaults);

            WireVolumeSlider(masterVolumeSlider, AudioCategory.Master);
            WireVolumeSlider(sfxVolumeSlider, AudioCategory.Sfx);
            WireVolumeSlider(musicVolumeSlider, AudioCategory.Music);
            WireVolumeSlider(ambientVolumeSlider, AudioCategory.Ambient);

            if (qualityHighButton != null)
                qualityHighButton.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(qualityHighButton, true, _motion);
                    SetQuality(VideoSettingsStore.QualityHigh);
                });
            if (qualitySmoothButton != null)
                qualitySmoothButton.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(qualitySmoothButton, true, _motion);
                    SetQuality(VideoSettingsStore.QualitySmooth);
                });
            if (fullscreenOnButton != null)
                fullscreenOnButton.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(fullscreenOnButton, true, _motion);
                    SetFullscreen(true);
                });
            if (fullscreenOffButton != null)
                fullscreenOffButton.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(fullscreenOffButton, true, _motion);
                    SetFullscreen(false);
                });

            if (pixelScale2Button != null)
                pixelScale2Button.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(pixelScale2Button, true, _motion);
                    SetPixelScale(2);
                });
            if (pixelScale3Button != null)
                pixelScale3Button.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(pixelScale3Button, true, _motion);
                    SetPixelScale(3);
                });
            if (pixelScale4Button != null)
                pixelScale4Button.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(pixelScale4Button, true, _motion);
                    SetPixelScale(4);
                });
            if (pixelScaleAutoButton != null)
                pixelScaleAutoButton.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(pixelScaleAutoButton, true, _motion);
                    SetPixelScale(PixelScaleStore.ScaleAuto);
                });
            if (resolutionButton != null)
                resolutionButton.onClick.AddListener(() =>
                {
                    RuntimeUiBuilder.ButtonFeedback(resolutionButton, true, _motion);
                    CycleResolution();
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
            {
                // 版本号单一真源链：BuildVersion.Current →（构建期）bundleVersion →（运行时）
                // Application.version。文案只出前缀，不写死数字。
                versionText.text = UiStrings.MainVersionPrefix + " " + Application.version;
            }

            // M3 管理循环接线（订阅结算事件 + 首次读进度）已上移到组合根：
            // Core/GameEntryPoint → CampaignApi.Install()。这里只做一次"进菜单时从存档刷新进度"，
            // 因为本场景可能出现"打完一局回到菜单"的情形（无存档时静默返回 false）。
            CampaignApi.LoadProgress();
        }

        void OnDestroy()
        {
            if (battleButton != null)
                battleButton.onClick.RemoveListener(OnBattleClicked);
            if (crewButton != null)
                crewButton.onClick.RemoveListener(OnCrewClicked);
            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OpenSettings);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (showcaseButton != null)
                showcaseButton.onClick.RemoveListener(OnShowcaseClicked);
#endif
            if (quitButton != null)
                quitButton.onClick.RemoveListener(OnQuitClicked);
            if (settingsBackButton != null)
                settingsBackButton.onClick.RemoveListener(CloseSettings);
            if (settingsCloseButton != null)
                settingsCloseButton.onClick.RemoveListener(CloseSettings);
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
        /// 进入战斗：**必经选关面板**（创始人 2026-09-23：主菜单只留一个进游戏入口，选关不可绕过；
        /// 「直接出海第一张海图」的捷径与「单人战役」按钮一并退役）。
        /// </summary>
        void OnBattleClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(battleButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.LevelSelect);
        }

        void OnCrewClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(crewButton, true, _motion);
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.CrewManagement);
        }

        // 调试入口走编译符号而非运行时开关：发布构建里这段代码物理不存在，不给
        // 「配置误开 / 被调起」留任何可达路径；开发构建保留符号供 QA 进调试面板。
        // 发布包下按钮仍在场景里但未绑监听（判空照常），点击无效果、无空引用。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// 调试场景（创始人 2026-09-25）：弹出**可拖动调试菜单启动器**——里面的按钮再开
        /// 具体调试面板（组件实摆 / New Sprite 对话框复刻 / Aseprite 菜单栏 / 部件陈列廊）。
        void OnShowcaseClicked()
        {
            RuntimeUiBuilder.ButtonFeedback(showcaseButton, true, _motion);
            if (transform.parent != null)
                DebugUi.DebugMenuHost.Toggle(transform.parent);
        }
#endif

        // ------------------------------------------------------------------
        // 设置（真接线）
        // ------------------------------------------------------------------

        void OpenSettings()
        {
            if (settingsPanel == null)
                return;

            RefreshSettingsControls();
            RuntimeUiBuilder.OpenPanel(settingsPanel, _motion);
        }

        void CloseSettings()
        {
            RuntimeUiBuilder.ButtonFeedback(settingsBackButton, true, _motion);
            RuntimeUiBuilder.ButtonFeedback(settingsCloseButton, true, _motion);

            // 关面板统一落盘：拖动滑条的过程不写盘，避免一次拖动几十次 IO。
            AudioService.SaveVolumes();
            if (VideoSettingsService.Instance != null)
                VideoSettingsService.Instance.SaveSettings();
            PixelScaleService.Save();

            RuntimeUiBuilder.ClosePanel(settingsPanel, _motion);

            if (statusText != null)
                statusText.text = UiStrings.MainStatusSettingsSaved;
        }

        void RestoreDefaults()
        {
            RuntimeUiBuilder.ButtonFeedback(settingsRestoreButton, true, _motion);
            AudioService.SetVolume(AudioCategory.Master, AudioSettingsStore.DefaultMasterVolume);
            AudioService.SetVolume(AudioCategory.Sfx, AudioSettingsStore.DefaultSfxVolume);
            AudioService.SetVolume(AudioCategory.Music, AudioSettingsStore.DefaultMusicVolume);
            AudioService.SetVolume(AudioCategory.Ambient, AudioSettingsStore.DefaultAmbientVolume);

            if (VideoSettingsService.Instance != null)
            {
                VideoSettingsService.Instance.SetFullscreen(VideoSettingsStore.DefaultFullscreen);
                VideoSettingsService.Instance.SetQuality(VideoSettingsStore.DefaultQuality);
                VideoSettingsService.Instance.SetResolution(0, 0);   // 解除分辨率锁定
            }
            PixelScaleService.SetScale(PixelScaleStore.ScaleDefault);   // 2×（现行口径）

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

        /// <summary>改像素比例档（真源 <see cref="PixelScaleService"/>；UI 画布与渲染 rig 同档生效）。</summary>
        void SetPixelScale(int scaleIndex)
        {
            PixelScaleService.SetScale(scaleIndex);
            RefreshVideoChips();
        }

        /// <summary>当前分辨率候选（跟随系统 + 按宽高去重的可用清单，编辑器下通常只有当前档）。</summary>
        static List<(int Width, int Height)> ResolutionChoices()
        {
            var choices = new List<(int Width, int Height)> { (0, 0) };   // 0 = 跟随系统
            foreach (Resolution r in Screen.resolutions)
            {
                var size = (r.width, r.height);
                if (!choices.Contains(size))
                    choices.Add(size);
            }
            return choices;
        }

        (int Width, int Height) _currentResolutionChoice;

        /// <summary>循环切换分辨率：跟随系统 → 候选 1 → … → 候选 N → 回到跟随系统。</summary>
        void CycleResolution()
        {
            var choices = ResolutionChoices();
            int index = choices.IndexOf(_currentResolutionChoice);
            _currentResolutionChoice = choices[(index + 1) % choices.Count];

            if (VideoSettingsService.Instance != null)
                VideoSettingsService.Instance.SetResolution(
                    _currentResolutionChoice.Width, _currentResolutionChoice.Height);

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

            // 像素比例四档（真源 PixelScaleService；档位含义见 PixelScaleStore）。
            SetChipSelected(pixelScale2Button, PixelScaleService.ScaleIndex == 2);
            SetChipSelected(pixelScale3Button, PixelScaleService.ScaleIndex == 3);
            SetChipSelected(pixelScale4Button, PixelScaleService.ScaleIndex == 4);
            SetChipSelected(pixelScaleAutoButton, PixelScaleService.ScaleIndex == PixelScaleStore.ScaleAuto);

            // 分辨率循环钮：标签 = 当前选择（锁定值优先取服务真值，未走服务的刷新路径回落本地记录）。
            int width = VideoSettingsService.Instance != null
                ? VideoSettingsService.Instance.ResolutionWidth : _currentResolutionChoice.Width;
            int height = VideoSettingsService.Instance != null
                ? VideoSettingsService.Instance.ResolutionHeight : _currentResolutionChoice.Height;
            if (VideoSettingsService.Instance != null)
                _currentResolutionChoice = (width, height);
            if (resolutionButton != null && resolutionButton is Stick.SketchButton sketch
                && sketch.Label != null)
            {
                sketch.Label.text = width > 0 && height > 0
                    ? $"{width}×{height}"
                    : UiStrings.SettingsOptionResolutionFollow;
            }
        }

        /// <summary>编程改值走 SetValueWithoutNotify，避免把「刷新」误当成一次用户输入。</summary>
        static void SetSliderNoNotify(Slider slider, float value)
        {
            if (slider == null)
                return;

            slider.SetValueWithoutNotify(Mathf.Clamp01(value));
        }

        /// <summary>
        /// 选项块选中态。**theme buttonset_item**（<see cref="Stick.SketchButtonSet"/>）：当前值换 hot 件——
        /// 更亮面 + 底边下沉的**无彩色**件（theme <c>state="selected"</c> 的映射，语义见控件类注释）。
        /// 【旧 Plate 皮分支已随 tone 族退役】画质/窗口模式两组选项一律出 SketchButtonSet
        /// （MenuUiBuilder.BuildSettingsRow），不存在走 tone Plate 的选项钮。
        /// </summary>
        static void SetChipSelected(Button chip, bool selected)
        {
            if (chip == null)
                return;

            if (chip is Stick.SketchButtonSet set)
                set.Active = selected;   // theme buttonset_item：当前值换 hot 件（无彩色）
        }

        // ------------------------------------------------------------------
        // 退出（确认框）
        // ------------------------------------------------------------------

        void OnQuitClicked()
        {
            if (quitConfirmPanel != null)
            {
                if (settingsPanel != null)
                    RuntimeUiBuilder.ClosePanel(settingsPanel, _motion);
                RuntimeUiBuilder.OpenPanel(quitConfirmPanel, _motion);
                return;
            }

            // 没装配确认框时退回旧行为（构建后正常退出；编辑器里 Unity 会忽略）。
            // 刻意不引用 UnityEditor 命名空间：本文件在运行时程序集里。
            Application.Quit();
        }

        void ConfirmQuit()
        {
            RuntimeUiBuilder.ButtonFeedback(quitConfirmOkButton, true, _motion);
            Application.Quit();
        }

        void CancelQuit()
        {
            RuntimeUiBuilder.ButtonFeedback(quitConfirmCancelButton, true, _motion);
            RuntimeUiBuilder.ClosePanel(quitConfirmPanel, _motion);
        }
    }
}
