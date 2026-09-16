using PirateCrew.Core;
using PirateCrew.PirateCrew.Audio;
using PirateCrew.PirateCrew.Settings;
using NUnit.Framework;

namespace PirateCrew.Tests.Settings
{
    /// <summary>
    /// <see cref="VideoSettingsStore"/>（视频设置持久化纯逻辑）与音频设置**同槽共存**的契约测试。
    ///
    /// 【背景】视频与音频共用设置槽 9 的同一份 <see cref="SaveData"/>（各写各的键前缀）。
    /// 旧版音频存储是「整槽替换」，会把同槽其他域的键抹掉——本测试把「两个域写在同一容器上
    /// 互不覆盖」钉成契约，防止将来有人把读改写改回替换。
    /// </summary>
    public sealed class VideoSettingsStoreTests
    {
        [Test]
        public void SettingsSlot_SharedWithAudioStore_AtSlot9()
        {
            Assert.AreEqual(AudioSettingsStore.SettingsSlot, VideoSettingsStore.SettingsSlot,
                "视频与音频必须共用同一设置槽，否则'关面板统一落盘'会写散两份档。");
            Assert.AreEqual(9, VideoSettingsStore.SettingsSlot);
        }

        [Test]
        public void WriteThenRead_RoundTrips()
        {
            var data = new SaveData();

            VideoSettingsStore.WriteTo(data, fullscreen: false, qualityIndex: VideoSettingsStore.QualitySmooth);

            bool fullscreen = VideoSettingsStore.DefaultFullscreen;
            int quality = VideoSettingsStore.DefaultQuality;
            bool any = VideoSettingsStore.TryReadFrom(data, ref fullscreen, ref quality);

            Assert.IsTrue(any);
            Assert.IsFalse(fullscreen);
            Assert.AreEqual(VideoSettingsStore.QualitySmooth, quality);
        }

        [Test]
        public void Read_EmptyData_ReturnsFalseAndKeepsDefaults()
        {
            var data = new SaveData();

            bool fullscreen = VideoSettingsStore.DefaultFullscreen;
            int quality = VideoSettingsStore.DefaultQuality;
            bool any = VideoSettingsStore.TryReadFrom(data, ref fullscreen, ref quality);

            Assert.IsFalse(any, "旧档/首启没有视频键，应保持默认值而不是误报已读。");
            Assert.IsTrue(fullscreen);
            Assert.AreEqual(VideoSettingsStore.DefaultQuality, quality);
        }

        [Test]
        public void Read_InvalidQualityValue_KeepsOriginal()
        {
            var data = new SaveData();
            data.SetData(VideoSettingsStore.QualityKey, "7");

            bool fullscreen = true;
            int quality = VideoSettingsStore.QualityHigh;
            VideoSettingsStore.TryReadFrom(data, ref fullscreen, ref quality);

            Assert.AreEqual(VideoSettingsStore.QualityHigh, quality, "非法档位必须被忽略（防手改档把切换打崩）。");
        }

        [Test]
        public void Read_PartialKeys_MissingDimensionKeepsOriginal()
        {
            var data = new SaveData();
            data.SetData(VideoSettingsStore.FullscreenKey, "0");
            // 故意不写 quality 键。

            bool fullscreen = true;
            int quality = VideoSettingsStore.QualitySmooth;
            bool any = VideoSettingsStore.TryReadFrom(data, ref fullscreen, ref quality);

            Assert.IsTrue(any, "任一键存在即视为有档（向前兼容缺键）。");
            Assert.IsFalse(fullscreen);
            Assert.AreEqual(VideoSettingsStore.QualitySmooth, quality, "缺的那个维度保持原值。");
        }

        [Test]
        public void AudioAndVideo_OnSameSaveData_DoNotOverwriteEachOther()
        {
            // 模拟「先保存音频、再保存视频」与反向的共存契约（读改写的可见效果）。
            var mixer = new VolumeMixer();
            mixer.SetAll(0.5f, 0.6f, 0.7f, 0.8f);

            var shared = new SaveData();
            AudioSettingsStore.WriteTo(shared, mixer);
            VideoSettingsStore.WriteTo(shared, fullscreen: false, qualityIndex: VideoSettingsStore.QualitySmooth);

            // 音频键仍在。
            var reloadedMixer = new VolumeMixer();
            Assert.IsTrue(AudioSettingsStore.TryApplyFrom(shared, reloadedMixer));
            Assert.AreEqual(0.5f, reloadedMixer.GetVolume(AudioCategory.Master), 1e-4f);
            Assert.AreEqual(0.8f, reloadedMixer.GetVolume(AudioCategory.Music), 1e-4f);

            // 视频键也在。
            bool fullscreen = true;
            int quality = VideoSettingsStore.QualityHigh;
            Assert.IsTrue(VideoSettingsStore.TryReadFrom(shared, ref fullscreen, ref quality));
            Assert.IsFalse(fullscreen);
            Assert.AreEqual(VideoSettingsStore.QualitySmooth, quality);
        }
    }
}
