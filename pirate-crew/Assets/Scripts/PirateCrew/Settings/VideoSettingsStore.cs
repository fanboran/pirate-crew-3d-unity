using System.Globalization;
using PirateCrew.Core;

namespace PirateCrew.Settings
{
    /// <summary>
    /// 视频设置（全屏 / 画质档）的持久化与档位定义（纯 C#，可无头测试）。
    ///
    /// 【存储路径】与 <c>AudioSettingsStore</c> 同一存档槽位（9，"设置"槽）的同一份
    /// <see cref="SaveData"/>，各写各的键前缀（<c>video.*</c> / <c>audio.*</c>）。
    /// 读写都走「先读槽 → 改键 → 写回」，两个设置域互不覆盖（旧版音频存储是整槽替换，
    /// 会把别人写的键抹掉——本次一并修正为读改写）。
    ///
    /// 【画质档】两档，对应 <c>Assets/Settings/URP/</c> 的两份 URP Asset：
    /// 0 = 高画质（PC_Balanced，默认）、1 = 流畅（PC_Performant，阴影距离 100→25）。
    /// 档位切换由 <see cref="VideoSettingsService"/> 落到 <c>GraphicsSettings.renderPipelineAsset</c>。
    /// </summary>
    public static class VideoSettingsStore
    {
        /// <summary>与音频设置共用的设置槽位（见 AudioSettingsStore.SettingsSlot）。</summary>
        public const int SettingsSlot = 9;

        /// <summary>槽位显示名。</summary>
        public const string SettingsDisplayName = "视频设置";

        /// <summary>全屏键（值 "1"/"0"）。</summary>
        public const string FullscreenKey = "video.fullscreen";

        /// <summary>画质档键（值 "0"/"1"）。</summary>
        public const string QualityKey = "video.quality";

        /// <summary>画质档：高画质（PC_Balanced，默认）。</summary>
        public const int QualityHigh = 0;

        /// <summary>画质档：流畅（PC_Performant）。</summary>
        public const int QualitySmooth = 1;

        /// <summary>默认全屏（提案/待定：全屏是桌面端惯例默认）。</summary>
        public const bool DefaultFullscreen = true;

        /// <summary>默认画质档（高画质；低配玩家可手动切流畅）。</summary>
        public const int DefaultQuality = QualityHigh;

        /// <summary>把当前设置写入存档数据（纯函数，不落盘）。</summary>
        public static void WriteTo(SaveData data, bool fullscreen, int qualityIndex)
        {
            if (data == null)
                return;

            data.SetData(FullscreenKey, fullscreen ? "1" : "0");
            data.SetData(QualityKey, qualityIndex.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 从存档数据读取设置（纯函数）。两个键都缺失时返回 false（旧档/首启保持默认值）；
        /// 单键缺失或非法时该维度保持传入的原值。
        /// </summary>
        public static bool TryReadFrom(SaveData data, ref bool fullscreen, ref int qualityIndex)
        {
            if (data == null)
                return false;

            bool any = false;

            string rawFullscreen = data.GetData(FullscreenKey);
            if (rawFullscreen == "1") { fullscreen = true; any = true; }
            else if (rawFullscreen == "0") { fullscreen = false; any = true; }

            if (int.TryParse(data.GetData(QualityKey), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int parsedQuality)
                && (parsedQuality == QualityHigh || parsedQuality == QualitySmooth))
            {
                qualityIndex = parsedQuality;
                any = true;
            }

            return any;
        }
    }
}
