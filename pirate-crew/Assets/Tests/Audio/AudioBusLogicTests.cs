using NUnit.Framework;
using PirateCrew.Audio;

namespace PirateCrew.Tests.Audio
{
    /// <summary>
    /// 音频总线纯逻辑测试：分类音量混音、同帧去抖与并发上限、3D 衰减与映射曲线。
    /// 全部为纯 C#，不触碰 AudioSource / AudioClip。
    /// </summary>
    public class AudioBusLogicTests
    {
        // ------------------------------------------------------------------
        // VolumeMixer
        // ------------------------------------------------------------------

        [Test]
        public void Mixer_DefaultsToUnityGain()
        {
            var mixer = new VolumeMixer();
            Assert.That(mixer.EffectiveGain(AudioCategory.Master), Is.EqualTo(1f).Within(1e-6));
            Assert.That(mixer.EffectiveGain(AudioCategory.Sfx), Is.EqualTo(1f).Within(1e-6));
            Assert.That(mixer.EffectiveGain(AudioCategory.Ambient), Is.EqualTo(1f).Within(1e-6));
            Assert.That(mixer.EffectiveGain(AudioCategory.Music), Is.EqualTo(1f).Within(1e-6));
        }

        [Test]
        public void Mixer_MasterScalesOtherCategories()
        {
            var mixer = new VolumeMixer();
            mixer.SetVolume(AudioCategory.Master, 0.5f);
            mixer.SetVolume(AudioCategory.Sfx, 0.5f);

            Assert.That(mixer.EffectiveGain(AudioCategory.Sfx), Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(mixer.EffectiveGain(AudioCategory.Master), Is.EqualTo(0.5f).Within(1e-6),
                "Master 不应乘自己");
        }

        [Test]
        public void Mixer_VolumeIsClampedToUnitRange()
        {
            var mixer = new VolumeMixer();
            mixer.SetVolume(AudioCategory.Sfx, 5f);
            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(1f));

            mixer.SetVolume(AudioCategory.Sfx, -3f);
            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(0f));
        }

        [Test]
        public void Mixer_MuteZeroesOnlyThatCategory()
        {
            var mixer = new VolumeMixer();
            mixer.SetMuted(AudioCategory.Music, true);

            Assert.That(mixer.EffectiveGain(AudioCategory.Music), Is.EqualTo(0f));
            Assert.That(mixer.EffectiveGain(AudioCategory.Sfx), Is.EqualTo(1f));
            Assert.That(mixer.IsMuted(AudioCategory.Music), Is.True);
        }

        [Test]
        public void Mixer_MasterMuteSilencesEverything()
        {
            var mixer = new VolumeMixer();
            mixer.SetMuted(AudioCategory.Master, true);

            Assert.That(mixer.EffectiveGain(AudioCategory.Master), Is.EqualTo(0f));
            Assert.That(mixer.EffectiveGain(AudioCategory.Sfx), Is.EqualTo(0f));
            Assert.That(mixer.EffectiveGain(AudioCategory.Ambient), Is.EqualTo(0f));
            Assert.That(mixer.EffectiveGain(AudioCategory.Music), Is.EqualTo(0f));
        }

        [Test]
        public void Mixer_IsSilent_ReflectsEffectiveGain()
        {
            var mixer = new VolumeMixer();
            Assert.That(mixer.IsSilent(AudioCategory.Sfx), Is.False);

            mixer.SetVolume(AudioCategory.Sfx, 0f);
            Assert.That(mixer.IsSilent(AudioCategory.Sfx), Is.True);
        }

        [Test]
        public void Mixer_SetAll_WritesFourCategories()
        {
            var mixer = new VolumeMixer();
            mixer.SetAll(0.9f, 0.8f, 0.7f, 0.6f);

            Assert.That(mixer.GetVolume(AudioCategory.Master), Is.EqualTo(0.9f).Within(1e-6));
            Assert.That(mixer.GetVolume(AudioCategory.Sfx), Is.EqualTo(0.8f).Within(1e-6));
            Assert.That(mixer.GetVolume(AudioCategory.Ambient), Is.EqualTo(0.7f).Within(1e-6));
            Assert.That(mixer.GetVolume(AudioCategory.Music), Is.EqualTo(0.6f).Within(1e-6));
        }

        // ------------------------------------------------------------------
        // PlaybackGate：同帧去抖（key 为音效 id 整型值，与 AudioService 调用方式一致）
        // ------------------------------------------------------------------

        [Test]
        public void Gate_DebounceWindow_IsFiftyMilliseconds()
        {
            var gate = new PlaybackGate();
            int key = (int)SfxId.Explosion;

            Assert.That(gate.ShouldPlay(key, 0d), Is.True, "窗口内第一次必须放行");
            Assert.That(gate.ShouldPlay(key, 0.03d), Is.False, "30 ms 内应被去抖");
            Assert.That(gate.ShouldPlay(key, 0.049d), Is.False);
            Assert.That(gate.ShouldPlay(key, 0.05d), Is.True, "满 50 ms 后应放行");
        }

        [Test]
        public void Gate_DebounceIsPerKey_NotGlobal()
        {
            var gate = new PlaybackGate();
            int keyA = (int)SfxId.Explosion;
            int keyB = (int)SfxId.FleshHit;

            Assert.That(gate.ShouldPlay(keyA, 0d), Is.True);
            Assert.That(gate.ShouldPlay(keyB, 0.001d), Is.True, "不同音效互不影响");
            Assert.That(gate.ShouldPlay(keyA, 0.002d), Is.False);
        }

        [Test]
        public void Gate_BlockedCallDoesNotExtendWindow()
        {
            var gate = new PlaybackGate();
            int key = (int)SfxId.Bounce;

            Assert.That(gate.ShouldPlay(key, 0d), Is.True);
            Assert.That(gate.ShouldPlay(key, 0.03d), Is.False);
            Assert.That(gate.ShouldPlay(key, 0.051d), Is.True,
                "被拒绝的调用不应刷新时间戳（否则连点会永远播不出来）");
        }

        [Test]
        public void Gate_VoiceCapPerCategory_RejectsExcessAndRecovers()
        {
            var gate = new PlaybackGate(PlaybackGate.DefaultDebounceSeconds, maxVoicesPerCategory: 2);

            Assert.That(gate.TryAcquire((int)SfxId.Explosion, AudioCategory.Sfx, 0d, 1d), Is.True);
            Assert.That(gate.TryAcquire((int)SfxId.WoodCrack, AudioCategory.Sfx, 0d, 1d), Is.True);
            Assert.That(gate.TryAcquire((int)SfxId.FleshHit, AudioCategory.Sfx, 0d, 1d), Is.False, "超过并发上限应丢弃新请求");
            Assert.That(gate.VoiceCappedCount, Is.EqualTo(1));

            // 1 秒后旧占位过期，名额恢复
            Assert.That(gate.TryAcquire((int)SfxId.FleshHit, AudioCategory.Sfx, 1.01d, 1d), Is.True);
        }

        [Test]
        public void Gate_VoiceCapIsPerCategory()
        {
            var gate = new PlaybackGate(PlaybackGate.DefaultDebounceSeconds, maxVoicesPerCategory: 1);

            Assert.That(gate.TryAcquire((int)SfxId.Explosion, AudioCategory.Sfx, 0d, 1d), Is.True);
            Assert.That(gate.TryAcquire((int)SfxId.WoodCrack, AudioCategory.Sfx, 0d, 1d), Is.False);
            Assert.That(gate.TryAcquire((int)SfxId.WaterSplash, AudioCategory.Ambient, 0d, 1d), Is.True, "分类之间不共享名额");
        }

        [Test]
        public void Gate_ActiveCount_TrimsExpired()
        {
            var gate = new PlaybackGate();
            gate.TryAcquire((int)SfxId.Explosion, AudioCategory.Sfx, 0d, 0.5d);

            Assert.That(gate.ActiveCount(AudioCategory.Sfx, 0.1d), Is.EqualTo(1));
            Assert.That(gate.ActiveCount(AudioCategory.Sfx, 0.6d), Is.EqualTo(0));
        }

        [Test]
        public void Gate_Reset_ClearsAllState()
        {
            var gate = new PlaybackGate();
            gate.TryAcquire((int)SfxId.Explosion, AudioCategory.Sfx, 0d, 5d);
            gate.Reset();

            Assert.That(gate.ActiveCount(AudioCategory.Sfx, 0.1d), Is.EqualTo(0));
            Assert.That(gate.ShouldPlay((int)SfxId.Explosion, 0.1d), Is.True, "去抖记录也应清空");
            Assert.That(gate.VoiceCappedCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // SpatialAudioRules
        // ------------------------------------------------------------------

        [Test]
        public void Spatial_BlendMapsTwoDAndThreeD()
        {
            Assert.That(SpatialAudioRules.SpatialBlend(SpatialMode.TwoD), Is.EqualTo(0f));
            Assert.That(SpatialAudioRules.SpatialBlend(SpatialMode.ThreeD), Is.EqualTo(1f));
        }

        [Test]
        public void Spatial_VolumeAtDistance_IsMonotonicDecreasing()
        {
            const float min = 10f;
            const float max = 50f;

            Assert.That(SpatialAudioRules.VolumeAtDistance(0f, min, max), Is.EqualTo(1f));
            Assert.That(SpatialAudioRules.VolumeAtDistance(min, min, max), Is.EqualTo(1f));
            Assert.That(SpatialAudioRules.VolumeAtDistance(max, min, max), Is.EqualTo(0f));
            Assert.That(SpatialAudioRules.VolumeAtDistance(max * 3f, min, max), Is.EqualTo(0f));

            float previous = 1f;
            for (float d = min; d <= max; d += 1f)
            {
                float gain = SpatialAudioRules.VolumeAtDistance(d, min, max);
                Assert.That(gain, Is.LessThanOrEqualTo(previous + 1e-6f),
                    "衰减曲线在 d=" + d + " 处不单调");
                Assert.That(gain, Is.InRange(0f, 1f));
                previous = gain;
            }
        }

        [Test]
        public void Spatial_VolumeAtDistance_LinearHalfway()
        {
            // Unity Linear rolloff 口径：中点应为 0.5
            Assert.That(SpatialAudioRules.VolumeAtDistance(30f, 10f, 50f), Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void Spatial_VolumeAtDistance_DegenerateRangeIsSafe()
        {
            Assert.That(SpatialAudioRules.VolumeAtDistance(0f, 10f, 10f), Is.EqualTo(1f));
            Assert.That(SpatialAudioRules.VolumeAtDistance(20f, 10f, 10f), Is.EqualTo(0f));
        }

        [Test]
        public void Spatial_PitchForDrag_IsMonotonicAndBounded()
        {
            Assert.That(SpatialAudioRules.PitchForDrag(0f, 130f), Is.EqualTo(0.86f).Within(1e-4f));
            Assert.That(SpatialAudioRules.PitchForDrag(130f, 130f), Is.EqualTo(1.18f).Within(1e-4f));
            Assert.That(SpatialAudioRules.PitchForDrag(999f, 130f), Is.EqualTo(1.18f).Within(1e-4f), "超出行程应被钳制");
            Assert.That(SpatialAudioRules.PitchForDrag(50f, 0f), Is.EqualTo(1f), "无参考行程时不应改音高");

            float previous = 0f;
            for (float d = 0f; d <= 130f; d += 5f)
            {
                float pitch = SpatialAudioRules.PitchForDrag(d, 130f);
                Assert.That(pitch, Is.GreaterThan(previous));
                previous = pitch;
            }
        }

        [Test]
        public void Spatial_VolumeForDrag_IsMonotonic()
        {
            Assert.That(SpatialAudioRules.VolumeForDrag(0f, 130f), Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(SpatialAudioRules.VolumeForDrag(130f, 130f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Spatial_MineBeepGain_RampsUpAsFuseShortens()
        {
            Assert.That(SpatialAudioRules.MineBeepGain(0), Is.EqualTo(0.45f).Within(1e-4f));
            Assert.That(SpatialAudioRules.MineBeepGain(59), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(SpatialAudioRules.MineBeepGain(999), Is.EqualTo(1f), "越界应钳制");

            float previous = 0f;
            for (int f = 0; f <= 59; f++)
            {
                float gain = SpatialAudioRules.MineBeepGain(f);
                Assert.That(gain, Is.GreaterThanOrEqualTo(previous));
                previous = gain;
            }
        }

        [Test]
        public void Spatial_IsAudible_MatchesMaxDistance()
        {
            Assert.That(SpatialAudioRules.IsAudible(10f, 50f), Is.True);
            Assert.That(SpatialAudioRules.IsAudible(50f, 50f), Is.False);
            Assert.That(SpatialAudioRules.IsAudible(500f, 50f), Is.False);
        }
    }
}
