using System.Globalization;
using PirateCrew.Core;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>
    /// 音量设置的持久化（走 Core 的现有键值 API，**不修改 Core 任何文件**）。
    ///
    /// 【走哪条存储路径】<c>Core/SaveManager</c> 的槽位读写 + <c>SaveData.GetData/SetData</c>
    /// 键值容器（<c>SaveData.cs:38-96</c>）。没有另开 PlayerPrefs：
    /// 一来 AGENTS.md 要求「跨模块通信/存储走 Core 现有服务」，二来 PlayerPrefs 在
    /// 本项目里没有任何既有用法，引入第二套存储会分散口径。
    ///
    /// 【槽位选择】<see cref="SettingsSlot"/> = 9。
    /// 已知占用：<c>SaveManager.AutoSaveSlot</c> = 0（自动存档）、
    /// <c>CampaignApi.ProgressSlot</c> = 1（战役进度）。9 与测试里出现过的 2/3/5/6 均不冲突。
    /// 这是 **提案/待定**：若协调者要统一规划槽位，改此一处常量即可。
    ///
    /// 【纯/脏分层】<see cref="WriteTo"/> / <see cref="TryApplyFrom"/> 只操作
    /// <see cref="SaveData"/>，是纯 C#（可无头测试）；<see cref="TryLoadInto"/> /
    /// <see cref="TrySaveFrom"/> 才接触 <c>SaveManager.Instance</c>，在无头验证台
    /// 因无 Unity 运行时自然返回 false（不抛异常）。
    /// </summary>
    public static class AudioSettingsStore
    {
        /// <summary>音频设置槽位（提案/待定；见类头占用说明）。</summary>
        public const int SettingsSlot = 9;

        /// <summary>槽位显示名。</summary>
        public const string SettingsDisplayName = "音频设置";

        /// <summary>总音量键。</summary>
        public const string MasterKey = "audio.master";

        /// <summary>音效音量键。</summary>
        public const string SfxKey = "audio.sfx";

        /// <summary>环境音音量键。</summary>
        public const string AmbientKey = "audio.ambient";

        /// <summary>音乐音量键。</summary>
        public const string MusicKey = "audio.music";

        /// <summary>默认音量（提案/待定）。</summary>
        public const float DefaultVolume = 0.8f;

        /// <summary>把混音器当前四类音量写入存档数据（纯函数，不落盘）。</summary>
        public static void WriteTo(SaveData data, VolumeMixer mixer)
        {
            if (data == null || mixer == null)
                return;

            data.SetData(MasterKey, ToText(mixer.GetVolume(AudioCategory.Master)));
            data.SetData(SfxKey, ToText(mixer.GetVolume(AudioCategory.Sfx)));
            data.SetData(AmbientKey, ToText(mixer.GetVolume(AudioCategory.Ambient)));
            data.SetData(MusicKey, ToText(mixer.GetVolume(AudioCategory.Music)));
        }

        /// <summary>
        /// 从存档数据恢复音量（纯函数）。只要四个键里存在任意一个就返回 true，
        /// 缺失的键保持混音器原值（向前兼容：旧档没有音频键时不会被清零）。
        /// </summary>
        public static bool TryApplyFrom(SaveData data, VolumeMixer mixer)
        {
            if (data == null || mixer == null)
                return false;

            bool any = false;
            any |= Apply(data, mixer, MasterKey, AudioCategory.Master);
            any |= Apply(data, mixer, SfxKey, AudioCategory.Sfx);
            any |= Apply(data, mixer, AmbientKey, AudioCategory.Ambient);
            any |= Apply(data, mixer, MusicKey, AudioCategory.Music);
            return any;
        }

        /// <summary>从 <see cref="SaveManager"/> 的音频槽位读档并应用；失败返回 false（不抛异常）。</summary>
        public static bool TryLoadInto(VolumeMixer mixer)
        {
            SaveManager save = SaveManager.Instance;
            if (save == null || mixer == null)
                return false;

            if (!save.SlotExists(SettingsSlot))
                return false;

            SaveData data = save.LoadFromSlot(SettingsSlot);
            if (data == null)
                return false;

            return TryApplyFrom(data, mixer);
        }

        /// <summary>把混音器当前音量落盘到音频槽位；无 SaveManager 实例或写失败返回 false。</summary>
        public static bool TrySaveFrom(VolumeMixer mixer)
        {
            SaveManager save = SaveManager.Instance;
            if (save == null || mixer == null)
                return false;

            var data = new SaveData();
            WriteTo(data, mixer);
            return save.SaveToSlot(SettingsSlot, data, SettingsDisplayName);
        }

        /// <summary>四类默认音量的便捷写入（首次启动/重置设置用）。</summary>
        public static void ApplyDefaults(VolumeMixer mixer)
        {
            if (mixer == null)
                return;

            mixer.SetAll(DefaultVolume, DefaultVolume, DefaultVolume, DefaultVolume);
        }

        static bool Apply(SaveData data, VolumeMixer mixer, string key, AudioCategory category)
        {
            string raw = data.GetData(key);
            if (string.IsNullOrEmpty(raw))
                return false;

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return false;

            mixer.SetVolume(category, value);
            return true;
        }

        static string ToText(float volume)
        {
            // 不变文化：避免某些地区用逗号做小数点导致跨机器读不回来
            return volume.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
