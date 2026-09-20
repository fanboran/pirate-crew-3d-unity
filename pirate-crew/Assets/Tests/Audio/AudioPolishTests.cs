using System;
using NUnit.Framework;
using PirateCrew.Audio;

namespace PirateCrew.Tests.Audio
{
    /// <summary>
    /// 「听觉打磨」两条纯规则的测试（纯 C#，可无头跑）：
    ///   ① <see cref="AudioVariation"/>：一次性音效的 ±8% 音高 / ±10% 音量变奏——
    ///      幅度必须落在设计区间内、不跑调、不把音量抖成负数，且循环音/音乐不参与；
    ///   ② <see cref="AmbientBedRules"/> / <see cref="AmbientBedMix"/>：环境底床的分层混音、
    ///      镜头距离衰减（单调、不静音）与鸟鸣触发间隔。
    /// </summary>
    public class AudioPolishTests
    {
        // ------------------------------------------------------------------
        // 变奏（AudioVariation）
        // ------------------------------------------------------------------

        [Test]
        public void Variation_OffsetMapsRollToSymmetricRange()
        {
            Assert.That(AudioVariation.Offset(0f, 0.08f), Is.EqualTo(-0.08f).Within(1e-6f));
            Assert.That(AudioVariation.Offset(0.5f, 0.08f), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(AudioVariation.Offset(1f, 0.08f), Is.EqualTo(0.08f).Within(1e-6f));

            // 越界 roll 被钳制，不会放大成超出设计幅度的偏移
            Assert.That(AudioVariation.Offset(-3f, 0.08f), Is.EqualTo(-0.08f).Within(1e-6f));
            Assert.That(AudioVariation.Offset(9f, 0.08f), Is.EqualTo(0.08f).Within(1e-6f));
            Assert.That(AudioVariation.Offset(0.2f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void Variation_PitchStaysWithinEightPercentBand()
        {
            Assert.That(AudioVariation.PitchRange, Is.EqualTo(0.08f).Within(1e-6f), "设计值：±8%");

            for (int i = 0; i <= 100; i++)
            {
                float roll = i / 100f;
                float pitch = AudioVariation.JitterPitch(roll, 1f);
                Assert.That(pitch, Is.InRange(1f - AudioVariation.PitchRange, 1f + AudioVariation.PitchRange),
                    "roll=" + roll + " 抖出 ±8% 区间（会听出跑调）");
            }
        }

        [Test]
        public void Variation_VolumeStaysWithinTenPercentBandAndNeverNegative()
        {
            Assert.That(AudioVariation.VolumeRange, Is.EqualTo(0.10f).Within(1e-6f), "设计值：±10%");

            for (int i = 0; i <= 100; i++)
            {
                float roll = i / 100f;
                float volume = AudioVariation.JitterVolume(roll, 0.5f);
                Assert.That(volume, Is.InRange(0.5f * 0.9f, 0.5f * 1.1f));
                Assert.That(volume, Is.GreaterThanOrEqualTo(0f));
            }

            // 极小基准音量也不能被抖成负数
            Assert.That(AudioVariation.JitterVolume(0f, 0.01f), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void Variation_DebouncedPitchKeepsBasePitchWhenRangeIsZero()
        {
            float pitch = AudioVariation.JitterPitch(0.1f, 1.3f, 0f);
            Assert.That(pitch, Is.EqualTo(1.3f).Within(1e-6f), "range=0 时应原样返回（拖拽音高映射不被破坏）");
        }

        [Test]
        public void Variation_PitchIsClampedToSafeBand()
        {
            // 基准音高极端时也不能抖动到不可用音区
            float low = AudioVariation.JitterPitch(0f, 0.001f);
            float high = AudioVariation.JitterPitch(1f, 500f);
            Assert.That(low, Is.GreaterThanOrEqualTo(AudioVariation.MinPitch));
            Assert.That(high, Is.LessThanOrEqualTo(AudioVariation.MaxPitch));
        }

        [Test]
        public void Variation_DisabledIsPassthrough()
        {
            AudioVariation.Apply(false, 0.1f, 0.1f, 0.9f, 0.42f, out float pitch, out float volume);
            Assert.That(pitch, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(volume, Is.EqualTo(0.42f).Within(1e-6f));

            // 关闭时也要把非法基准值兜住（不把 0 音高丢给 AudioSource）
            AudioVariation.Apply(false, 0f, 0f, 0f, -1f, out float safePitch, out float safeVolume);
            Assert.That(safePitch, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(safeVolume, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Variation_AppliesToOneShotsOnly()
        {
            Assert.That(AudioVariation.Enabled, Is.True, "总开关默认开启");

            Assert.That(AudioVariation.AppliesTo(AudioCategory.Sfx, false), Is.True, "一次性音效要抖");
            Assert.That(AudioVariation.AppliesTo(AudioCategory.Ambient, false), Is.True, "鸟鸣这类一次性点缀也要抖");

            Assert.That(AudioVariation.AppliesTo(AudioCategory.Ambient, true), Is.False, "循环音不能抖（拼接点会跳变）");
            Assert.That(AudioVariation.AppliesTo(AudioCategory.Music, false), Is.False, "音乐不能抖（会走音）");
        }

        // ------------------------------------------------------------------
        // 环境底床（AmbientBedRules / AmbientBedMix）
        // ------------------------------------------------------------------

        [Test]
        public void Bed_LayersAreAllLoopAmbientRecipes()
        {
            Assert.That(AmbientBedRules.LayerCount, Is.EqualTo(3), "底床约定三层：海浪 + 海风 + 垫底");

            for (int i = 0; i < AmbientBedRules.LayerCount; i++)
            {
                AmbientBedLayer layer = AmbientBedRules.Layers[i];
                SfxRecipe recipe = SfxCatalog.Get(layer.Id);
                Assert.That(recipe.Loop, Is.True, layer.Label + " 必须是循环音");
                Assert.That(recipe.Category, Is.EqualTo(AudioCategory.Ambient), layer.Label + " 必须在环境总线");
                Assert.That(layer.DefaultWeight, Is.GreaterThan(0f), layer.Label + " 权重必须为正");

                Assert.That(AmbientBedRules.IndexOf(layer.Id), Is.EqualTo(i));
                Assert.That(AmbientBedRules.IsBedLayer(layer.Id), Is.True);
            }

            Assert.That(AmbientBedRules.IsBedLayer(SfxId.Explosion), Is.False, "一次性音效不是底床层");
            Assert.That(AmbientBedRules.IndexOf(SfxId.Explosion), Is.EqualTo(-1));
        }

        [Test]
        public void Bed_DistanceGainIsMonotonicAndNeverSilent()
        {
            float previous = float.MaxValue;
            for (float distance = 0f; distance <= 300f; distance += 5f)
            {
                float gain = AmbientBedRules.DistanceGain(distance);
                Assert.That(gain, Is.LessThanOrEqualTo(previous + 1e-6f), "距离衰减必须单调不增");
                Assert.That(gain, Is.GreaterThan(0f), "环境底床不能衰减到静音（场景会发空）");
                Assert.That(gain, Is.LessThanOrEqualTo(1f));
                previous = gain;
            }

            Assert.That(AmbientBedRules.DistanceGain(0f), Is.EqualTo(1f).Within(1e-6f), "场中心不衰减");
            Assert.That(AmbientBedRules.DistanceGain(AmbientBedRules.NearDistance), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AmbientBedRules.DistanceGain(AmbientBedRules.FarDistance),
                Is.EqualTo(AmbientBedRules.MinDistanceGain).Within(1e-6f));
            Assert.That(AmbientBedRules.DistanceGain(9999f),
                Is.EqualTo(AmbientBedRules.MinDistanceGain).Within(1e-6f), "超出远界保持下限，不继续掉");
        }

        [Test]
        public void Bed_DistanceGainHalfwayIsLinear()
        {
            float mid = (AmbientBedRules.NearDistance + AmbientBedRules.FarDistance) * 0.5f;
            float expected = (1f + AmbientBedRules.MinDistanceGain) * 0.5f;
            Assert.That(AmbientBedRules.DistanceGain(mid), Is.EqualTo(expected).Within(1e-4f));
        }

        [Test]
        public void Bed_DistanceGainDegenerateRangeIsSafe()
        {
            Assert.That(AmbientBedRules.DistanceGain(0f, 50f, 50f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(AmbientBedRules.DistanceGain(80f, 50f, 50f),
                Is.EqualTo(AmbientBedRules.MinDistanceGain).Within(1e-6f));
        }

        [Test]
        public void Bed_CameraDistanceIgnoresHeight()
        {
            // 只有 X/Z 参与：镜头抬高（俯视角变化）不应造成环境音误衰减
            Assert.That(AmbientBedRules.CameraDistance(30f, 40f, 0f, 0f), Is.EqualTo(50f).Within(1e-3f));
            Assert.That(AmbientBedRules.CameraDistance(0f, 0f, 0f, 0f), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Bed_LayerVolumeMultipliesAndClamps()
        {
            float volume = AmbientBedRules.LayerVolume(0.5f, 0.8f, 0.5f, 1f);
            Assert.That(volume, Is.EqualTo(0.2f).Within(1e-6f));

            Assert.That(AmbientBedRules.LayerVolume(1f, 1f, 1f, 1f), Is.EqualTo(1f).Within(1e-6f), "上限钳到 1");
            Assert.That(AmbientBedRules.LayerVolume(0.5f, 0f, 1f, 1f), Is.EqualTo(0f), "总线静音 → 0");
            Assert.That(AmbientBedRules.LayerVolume(-1f, -1f, -1f, -1f), Is.EqualTo(0f), "非法输入不产生负音量");
        }

        [Test]
        public void Bed_MixDefaultsMatchRulesAndWeightsAreAdjustable()
        {
            var mix = new AmbientBedMix();
            for (int i = 0; i < AmbientBedRules.LayerCount; i++)
            {
                Assert.That(mix.LayerId(i), Is.EqualTo(AmbientBedRules.Layers[i].Id));
                Assert.That(mix.GetWeightAt(i), Is.EqualTo(AmbientBedRules.Layers[i].DefaultWeight).Within(1e-6f));
            }

            SfxId waves = AmbientBedRules.Layers[0].Id;
            mix.SetWeight(waves, 0.25f);
            Assert.That(mix.GetWeight(waves), Is.EqualTo(0.25f).Within(1e-6f));

            mix.SetWeight(waves, -5f);
            Assert.That(mix.GetWeight(waves), Is.EqualTo(0f), "负权重钳到 0");

            // 非底床层：设置被忽略，读取返回 0
            mix.SetWeight(SfxId.Explosion, 0.9f);
            Assert.That(mix.GetWeight(SfxId.Explosion), Is.EqualTo(0f));

            mix.Reset();
            Assert.That(mix.GetWeight(waves), Is.EqualTo(AmbientBedRules.Layers[0].DefaultWeight).Within(1e-6f));
        }

        [Test]
        public void Bed_BirdIntervalIsBoundedAndNormalized()
        {
            var mix = new AmbientBedMix();
            Assert.That(mix.BirdMinIntervalSeconds, Is.EqualTo(AmbientBedRules.BirdMinIntervalSeconds).Within(1e-6));
            Assert.That(mix.BirdMaxIntervalSeconds, Is.EqualTo(AmbientBedRules.BirdMaxIntervalSeconds).Within(1e-6));

            for (int i = 0; i <= 20; i++)
            {
                double interval = mix.NextBirdInterval(i / 20d);
                Assert.That(interval, Is.InRange(AmbientBedRules.BirdMinIntervalSeconds,
                    AmbientBedRules.BirdMaxIntervalSeconds));
            }

            mix.SetBirdInterval(2d, 4d);
            Assert.That(mix.NextBirdInterval(0d), Is.EqualTo(2d).Within(1e-6));
            Assert.That(mix.NextBirdInterval(1d), Is.EqualTo(4d).Within(1e-6));

            // 传给音频线程的时间不能是 0/负（否则 WaitForSecondsRealtime 会每帧刷鸟叫）
            mix.SetBirdInterval(-3d, -1d);
            Assert.That(mix.BirdMinIntervalSeconds, Is.GreaterThan(0d));
            Assert.That(mix.BirdMaxIntervalSeconds, Is.GreaterThanOrEqualTo(mix.BirdMinIntervalSeconds));

            mix.SetBirdVolumeScale(-1f);
            Assert.That(mix.BirdVolumeScale, Is.EqualTo(0f));

            mix.Reset();
            Assert.That(mix.BirdVolumeScale, Is.EqualTo(AmbientBedRules.BirdVolumeScale).Within(1e-6f));
        }
    }
}
