using NUnit.Framework;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Audio;

namespace PirateCrew.Tests.Audio
{
    /// <summary>
    /// 音量持久化的纯逻辑测试：用 <see cref="SaveData"/> 键值容器做往返，
    /// 不触碰 SaveManager（MonoBehaviour，无头验证台不可实例化）。
    /// </summary>
    public class AudioSettingsStoreTests
    {
        [Test]
        public void Store_KeyNamesAreStable()
        {
            // 键名是对外契约（存档兼容性），变更需同步考虑旧档迁移
            Assert.That(AudioSettingsStore.MasterKey, Is.EqualTo("audio.master"));
            Assert.That(AudioSettingsStore.SfxKey, Is.EqualTo("audio.sfx"));
            Assert.That(AudioSettingsStore.AmbientKey, Is.EqualTo("audio.ambient"));
            Assert.That(AudioSettingsStore.MusicKey, Is.EqualTo("audio.music"));
        }

        [Test]
        public void Store_RoundTripsAllFourVolumes()
        {
            var source = new VolumeMixer();
            source.SetAll(0.9f, 0.35f, 0.6f, 0.7f);

            var data = new SaveData();
            AudioSettingsStore.WriteTo(data, source);

            var restored = new VolumeMixer();
            Assert.That(AudioSettingsStore.TryApplyFrom(data, restored), Is.True);

            Assert.That(restored.GetVolume(AudioCategory.Master), Is.EqualTo(0.9f).Within(1e-4f));
            Assert.That(restored.GetVolume(AudioCategory.Sfx), Is.EqualTo(0.35f).Within(1e-4f));
            Assert.That(restored.GetVolume(AudioCategory.Ambient), Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(restored.GetVolume(AudioCategory.Music), Is.EqualTo(0.7f).Within(1e-4f));
        }

        [Test]
        public void Store_EmptyData_ReturnsFalseAndKeepsDefaults()
        {
            var mixer = new VolumeMixer();
            Assert.That(AudioSettingsStore.TryApplyFrom(new SaveData(), mixer), Is.False);
            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(1f), "未命中任何键时不应改动混音器");
        }

        [Test]
        public void Store_PartialKeys_OnlyApplyPresentOnes()
        {
            var data = new SaveData();
            data.SetData(AudioSettingsStore.MusicKey, "0.2");

            var mixer = new VolumeMixer();
            Assert.That(AudioSettingsStore.TryApplyFrom(data, mixer), Is.True);
            Assert.That(mixer.GetVolume(AudioCategory.Music), Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(1f), "缺失的键应保持原值");
        }

        [Test]
        public void Store_InvalidValue_IsIgnored()
        {
            var data = new SaveData();
            data.SetData(AudioSettingsStore.SfxKey, "not-a-number");

            var mixer = new VolumeMixer();
            Assert.That(AudioSettingsStore.TryApplyFrom(data, mixer), Is.False);
            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(1f));
        }

        [Test]
        public void Store_OutOfRangeValueIsClamped()
        {
            var data = new SaveData();
            data.SetData(AudioSettingsStore.SfxKey, "2.5");
            data.SetData(AudioSettingsStore.MusicKey, "-1");

            var mixer = new VolumeMixer();
            AudioSettingsStore.TryApplyFrom(data, mixer);

            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(1f));
            Assert.That(mixer.GetVolume(AudioCategory.Music), Is.EqualTo(0f));
        }

        [Test]
        public void Store_UsesInvariantCulture()
        {
            var data = new SaveData();
            AudioSettingsStore.WriteTo(data, new VolumeMixer());

            // 写出的文本必须是英文小数点（跨地区读回一致）
            string raw = data.GetData(AudioSettingsStore.SfxKey);
            Assert.That(raw, Does.Not.Contain(","));
            Assert.That(float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _), Is.True);
        }

        [Test]
        public void Store_ApplyDefaults_SetsFourCategories()
        {
            var mixer = new VolumeMixer();
            mixer.SetAll(1f, 1f, 1f, 1f);
            AudioSettingsStore.ApplyDefaults(mixer);

            // 【2026-09-16 起】出厂默认按类别拆分：用户反馈"背景音乐有点大"后，
            // 环境（浪/风/垫底 pad）0.8→0.5、音乐（胜负乐句）0.8→0.7，主/音效维持 0.8。
            Assert.That(mixer.GetVolume(AudioCategory.Master), Is.EqualTo(AudioSettingsStore.DefaultMasterVolume).Within(1e-6f));
            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(AudioSettingsStore.DefaultSfxVolume).Within(1e-6f));
            Assert.That(mixer.GetVolume(AudioCategory.Ambient), Is.EqualTo(AudioSettingsStore.DefaultAmbientVolume).Within(1e-6f));
            Assert.That(mixer.GetVolume(AudioCategory.Music), Is.EqualTo(AudioSettingsStore.DefaultMusicVolume).Within(1e-6f));
        }

        [Test]
        public void Store_SettingsSlotDoesNotCollideWithKnownSlots()
        {
            Assert.That(AudioSettingsStore.SettingsSlot, Is.Not.EqualTo(SaveManager.AutoSaveSlot), "槽位 0 是自动存档");
            Assert.That(AudioSettingsStore.SettingsSlot, Is.GreaterThan(1), "槽位 1 是战役进度");
        }
    }
}
