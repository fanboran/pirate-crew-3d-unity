using PirateCrew.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Settings
{
    /// <summary>
    /// 视频设置服务（全屏 / 画质档的应用出口）。
    ///
    /// 【挂在哪】Bootstrapper 场景的 <c>VideoSettings</c> 对象上（<c>SceneSetup.BuildBootstrapperScene</c>
    /// 装配并注入两份 URP Asset 引用）——序列化引用必须落在场景里，播放器构建才会把两份
    /// URP Asset 连同各自 Renderer 打进包，运行时切换才有的换。
    ///
    /// 【职责】
    ///   · Awake：从设置槽读档并应用（全屏 + 画质档），此后 UI 改设置也走本类；
    ///   · 画质档切换 = 换 <c>GraphicsSettings.renderPipelineAsset</c>
    ///     （QualitySettings 六档均为模板默认值、未挂管线资产，故不走 SetQualityLevel）；
    ///   · 全屏只在播放器构建生效（编辑器里改全屏会干扰开发环境，刻意跳过）。
    ///
    /// 【纯/脏分层】键值读写全部在 <see cref="VideoSettingsStore"/>（纯 C#）；本类只碰引擎 API。
    /// </summary>
    public sealed class VideoSettingsService : MonoBehaviour
    {
        /// <summary>画质档"流畅"用的 URP 资产（SceneSetup 注入；缺失时切流畅降级为无操作）。</summary>
        [SerializeField] RenderPipelineAsset performantPipeline;

        /// <summary>画质档"高画质"用的 URP 资产（SceneSetup 注入；缺失时切高画质降级为无操作）。</summary>
        [SerializeField] RenderPipelineAsset balancedPipeline;

        /// <summary>全局访问入口（UI 面板接线用；场景未装配时为 null，UI 侧各自降级）。</summary>
        public static VideoSettingsService Instance { get; private set; }

        /// <summary>当前画质档（<see cref="VideoSettingsStore.QualityHigh"/> / QualitySmooth）。</summary>
        public int QualityIndex { get; private set; } = VideoSettingsStore.DefaultQuality;

        /// <summary>当前全屏状态（编辑器里始终为 <see cref="VideoSettingsStore.DefaultFullscreen"/> 的存储值）。</summary>
        public bool Fullscreen { get; private set; } = VideoSettingsStore.DefaultFullscreen;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>
        /// 读档与应用放 Start 而非 Awake：本对象与 Services（SaveManager）是同级兄弟，
        /// Awake 顺序不保证——Start 时全部 Awake（含 SaveManager.Instance 的赋值）已跑完。
        /// </summary>
        void Start()
        {

            // 读档（无存档/无 SaveManager 时保持默认值）。
            bool fullscreen = Fullscreen;
            int quality = QualityIndex;
            SaveManager save = SaveManager.Instance;
            if (save != null && save.SlotExists(VideoSettingsStore.SettingsSlot))
            {
                SaveData data = save.LoadFromSlot(VideoSettingsStore.SettingsSlot);
                VideoSettingsStore.TryReadFrom(data, ref fullscreen, ref quality);
                Fullscreen = fullscreen;
                QualityIndex = quality;
            }

            ApplyFullscreen();
            ApplyQuality();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>设置全屏（立即生效；编辑器下只记录状态不真正切换）。</summary>
        public void SetFullscreen(bool fullscreen)
        {
            Fullscreen = fullscreen;
            ApplyFullscreen();
        }

        /// <summary>设置画质档（立即生效；未知档位忽略）。返回是否生效。</summary>
        public bool SetQuality(int qualityIndex)
        {
            if (qualityIndex != VideoSettingsStore.QualityHigh
                && qualityIndex != VideoSettingsStore.QualitySmooth)
            {
                return false;
            }

            QualityIndex = qualityIndex;
            ApplyQuality();
            return true;
        }

        /// <summary>把当前设置落盘（与音频设置同槽读改写，互不覆盖）。返回是否写入成功。</summary>
        public bool SaveSettings()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null)
                return false;

            // 读改写：保留同槽里音频设置的键（AudioSettingsStore 也已改为同一策略）。
            SaveData data = save.SlotExists(VideoSettingsStore.SettingsSlot)
                ? save.LoadFromSlot(VideoSettingsStore.SettingsSlot)
                : null;
            if (data == null)
                data = new SaveData();

            VideoSettingsStore.WriteTo(data, Fullscreen, QualityIndex);
            return save.SaveToSlot(VideoSettingsStore.SettingsSlot, data, VideoSettingsStore.SettingsDisplayName);
        }

        void ApplyFullscreen()
        {
            // 编辑器里切全屏会改变开发环境的 Game 视图，刻意只在播放器生效（存储值照常记录）。
            if (Application.isEditor)
                return;

            Screen.fullScreen = Fullscreen;
        }

        void ApplyQuality()
        {
            RenderPipelineAsset target = QualityIndex == VideoSettingsStore.QualitySmooth
                ? performantPipeline
                : balancedPipeline;

            // 同档幂等：切管线会重建渲染状态（一次可感知的卡顿），不要在重复调用时白切。
            if (target == null || GraphicsSettings.renderPipelineAsset == target)
                return;

            GraphicsSettings.renderPipelineAsset = target;
        }
    }
}
