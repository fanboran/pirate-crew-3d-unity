using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Audio;
using PirateCrew.PirateCrew.Audio.Synth;

namespace PirateCrew.Tests.Audio
{
    /// <summary>
    /// 合成原语测试（全部纯 C#，不实例化 AudioClip / MonoBehaviour，符合无头验证台边界）。
    ///
    /// 覆盖：波形映射、确定性随机、ADSR 分段时长、滤波器对高频的衰减单调性、
    /// 混响尾音、缓冲的采样数与首尾淡入淡出、无缝循环折叠。
    /// </summary>
    public class SynthPrimitivesTests
    {
        const int Sr = AudioBuffer.DefaultSampleRate;

        // ------------------------------------------------------------------
        // 波形
        // ------------------------------------------------------------------

        [Test]
        public void Waveforms_Sine_MatchesUnitCircle()
        {
            Assert.That(Waveforms.Sine(0d), Is.EqualTo(0d).Within(1e-9));
            Assert.That(Waveforms.Sine(0.25d), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Waveforms.Sine(0.5d), Is.EqualTo(0d).Within(1e-9));
            Assert.That(Waveforms.Sine(0.75d), Is.EqualTo(-1d).Within(1e-9));
        }

        [Test]
        public void Waveforms_Triangle_PeaksAndZeroCrossings()
        {
            Assert.That(Waveforms.Triangle(0d), Is.EqualTo(0d).Within(1e-9));
            Assert.That(Waveforms.Triangle(0.25d), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Waveforms.Triangle(0.5d), Is.EqualTo(0d).Within(1e-9));
            Assert.That(Waveforms.Triangle(0.75d), Is.EqualTo(-1d).Within(1e-9));
            Assert.That(Waveforms.Triangle(1.25d), Is.EqualTo(1d).Within(1e-9), "相位应自动按周期回绕");
        }

        [Test]
        public void Waveforms_Saw_RisingRamp()
        {
            Assert.That(Waveforms.Saw(0.25d), Is.EqualTo(0.5d).Within(1e-9));
            Assert.That(Waveforms.Saw(0.4999d), Is.GreaterThan(0.99d));
            Assert.That(Waveforms.Saw(0.75d), Is.EqualTo(-0.5d).Within(1e-9));
        }

        [Test]
        public void Waveforms_Square_RespectsDutyCycle()
        {
            Assert.That(Waveforms.Square(0.25d, 0.5d), Is.EqualTo(1d));
            Assert.That(Waveforms.Square(0.75d, 0.5d), Is.EqualTo(-1d));
            Assert.That(Waveforms.Square(0.30d, 0.34d), Is.EqualTo(1d));
            Assert.That(Waveforms.Square(0.40d, 0.34d), Is.EqualTo(-1d));
        }

        [Test]
        public void Waveforms_Harmonics_NormalizedWithinUnitRange()
        {
            for (int i = 0; i < 200; i++)
            {
                double value = Waveforms.Harmonics(i / 200d, 5, 1.4d);
                Assert.That(value, Is.InRange(-1.0001d, 1.0001d));
            }
        }

        [Test]
        public void Waveforms_PhaseIncrement_IsFrequencyOverSampleRate()
        {
            Assert.That(Waveforms.PhaseIncrement(441d, Sr), Is.EqualTo(0.01d).Within(1e-12));
            Assert.That(Waveforms.PhaseIncrement(441d, 0), Is.EqualTo(0d));
        }

        [Test]
        public void Waveforms_ExpGlide_ApproachesTarget()
        {
            Assert.That(Waveforms.Glide(95d, 32d, 0d, 0.12d), Is.EqualTo(95d).Within(1e-9));
            Assert.That(Waveforms.Glide(95d, 32d, 10d, 0.12d), Is.EqualTo(32d).Within(0.001d));
        }

        // ------------------------------------------------------------------
        // 确定性随机
        // ------------------------------------------------------------------

        [Test]
        public void SynthRandom_SameSeed_ProducesIdenticalSequence()
        {
            var a = new SynthRandom(12345u);
            var b = new SynthRandom(12345u);
            for (int i = 0; i < 32; i++)
                Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()));
        }

        [Test]
        public void SynthRandom_DifferentSeed_ProducesDifferentSequence()
        {
            var a = new SynthRandom(1u);
            var b = new SynthRandom(2u);
            bool anyDifferent = false;
            for (int i = 0; i < 16; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                    anyDifferent = true;
            }

            Assert.That(anyDifferent, Is.True);
        }

        [Test]
        public void SynthRandom_BipolarStaysInRange()
        {
            var rng = new SynthRandom(777u);
            for (int i = 0; i < 5000; i++)
            {
                float value = rng.NextBipolar();
                Assert.That(value, Is.GreaterThanOrEqualTo(-1f));
                Assert.That(value, Is.LessThan(1f));
            }
        }

        [Test]
        public void SynthRandom_ZeroSeed_IsReplacedNotStuck()
        {
            var rng = new SynthRandom(0u);
            uint first = rng.NextUInt();
            uint second = rng.NextUInt();
            Assert.That(first, Is.Not.EqualTo(0u));
            Assert.That(first, Is.Not.EqualTo(second));
        }

        // ------------------------------------------------------------------
        // AudioBuffer
        // ------------------------------------------------------------------

        [Test]
        public void AudioBuffer_FrameCountAndDuration_AreConsistent()
        {
            var buffer = new AudioBuffer(Sr, Sr, AudioBuffer.Mono);
            Assert.That(buffer.FrameCount, Is.EqualTo(Sr));
            Assert.That(buffer.Duration, Is.EqualTo(1d).Within(1e-9));
            Assert.That(buffer.Samples.Length, Is.EqualTo(Sr));
        }

        [Test]
        public void AudioBuffer_FramesForSeconds_RoundsAwayFromZero()
        {
            Assert.That(AudioBuffer.FramesForSeconds(1d, Sr), Is.EqualTo(Sr));
            Assert.That(AudioBuffer.FramesForSeconds(0.5d, Sr), Is.EqualTo(Sr / 2));
            Assert.That(AudioBuffer.FramesForSeconds(6.0d, Sr), Is.EqualTo(6 * Sr));
            Assert.That(AudioBuffer.FramesForSeconds(0d, Sr), Is.EqualTo(0));
        }

        [Test]
        public void AudioBuffer_PeakAndRms_AreCorrect()
        {
            var buffer = new AudioBuffer(4, Sr, AudioBuffer.Mono);
            buffer.Samples[0] = 0.5f;
            buffer.Samples[1] = -2f;
            buffer.Samples[2] = 1f;
            buffer.Samples[3] = 0f;

            Assert.That(buffer.Peak(), Is.EqualTo(2f).Within(1e-6));
            float expectedRms = (float)System.Math.Sqrt((0.25 + 4 + 1 + 0) / 4d);
            Assert.That(buffer.Rms(), Is.EqualTo(expectedRms).Within(1e-5));
        }

        [Test]
        public void AudioBuffer_NormalizeTo_HitsTargetPeak()
        {
            var buffer = new AudioBuffer(100, Sr, AudioBuffer.Mono);
            for (int i = 0; i < buffer.Samples.Length; i++)
                buffer.Samples[i] = (i % 2 == 0 ? 1f : -3f);

            buffer.NormalizeTo(SfxCatalog.PeakTarget);

            Assert.That(buffer.Peak(), Is.EqualTo(SfxCatalog.PeakTarget).Within(1e-5));
        }

        [Test]
        public void AudioBuffer_FadeIn_StartsAtZeroAndRamps()
        {
            var buffer = new AudioBuffer(1000, 1000, AudioBuffer.Mono);
            for (int i = 0; i < buffer.Samples.Length; i++)
                buffer.Samples[i] = 1f;

            buffer.ApplyFadeIn(0.5d); // 500 帧

            Assert.That(buffer.Samples[0], Is.EqualTo(0f).Within(1e-6));
            Assert.That(buffer.Samples[250], Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(buffer.Samples[600], Is.EqualTo(1f).Within(1e-6), "淡入区之后应保持原值");
        }

        [Test]
        public void AudioBuffer_FadeOut_EndsAtZero()
        {
            var buffer = new AudioBuffer(1000, 1000, AudioBuffer.Mono);
            for (int i = 0; i < buffer.Samples.Length; i++)
                buffer.Samples[i] = 1f;

            buffer.ApplyFadeOut(0.5d);

            Assert.That(buffer.Samples[999], Is.EqualTo(0f).Within(1e-6));
            Assert.That(buffer.Samples[0], Is.EqualTo(1f).Within(1e-6));
        }

        [Test]
        public void AudioBuffer_Declick_BothEndsZero()
        {
            var buffer = new AudioBuffer(4410, Sr, AudioBuffer.Mono);
            for (int i = 0; i < buffer.Samples.Length; i++)
                buffer.Samples[i] = 0.8f;

            buffer.ApplyDeclick(SynthUtil.DeclickSeconds);

            Assert.That(buffer.Samples[0], Is.EqualTo(0f).Within(1e-6));
            Assert.That(buffer.Samples[buffer.Samples.Length - 1], Is.EqualTo(0f).Within(1e-6));
        }

        [Test]
        public void AudioBuffer_MixIn_OffsetsAndGains()
        {
            var target = new AudioBuffer(100, Sr, AudioBuffer.Mono);
            var source = new AudioBuffer(10, Sr, AudioBuffer.Mono);
            for (int i = 0; i < source.Samples.Length; i++)
                source.Samples[i] = 1f;

            target.MixIn(source, 20, 0.5f);

            Assert.That(target.Samples[19], Is.EqualTo(0f));
            Assert.That(target.Samples[20], Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(target.Samples[29], Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(target.Samples[30], Is.EqualTo(0f));
        }

        [Test]
        public void AudioBuffer_FoldSeamlessLoop_TrimsLengthAndKeepsSeamContinuous()
        {
            // 用一段连续信号（非整周期正弦）验证折叠后首帧等于原第 N 帧、末帧等于原第 N-1 帧
            const int body = 2000;
            const int tail = 200;
            var raw = new AudioBuffer(body + tail, Sr, AudioBuffer.Mono);
            for (int i = 0; i < raw.Samples.Length; i++)
                raw.Samples[i] = (float)System.Math.Sin(2d * System.Math.PI * 0.013d * i);

            AudioBuffer loop = raw.FoldSeamlessLoop(tail);

            Assert.That(loop.FrameCount, Is.EqualTo(body));
            Assert.That(loop.Samples[0], Is.EqualTo(raw.Samples[body]).Within(1e-6));
            Assert.That(loop.Samples[body - 1], Is.EqualTo(raw.Samples[body - 1]).Within(1e-6));
        }

        // ------------------------------------------------------------------
        // ADSR
        // ------------------------------------------------------------------

        [Test]
        public void Adsr_LevelAt_FollowsSegments()
        {
            var adsr = new Adsr(0.1d, 0.2d, 0.5d, 0.3d);
            const double gate = 0.5d;

            Assert.That(adsr.LevelAt(0d, gate), Is.EqualTo(0d).Within(1e-9));
            Assert.That(adsr.LevelAt(0.05d, gate), Is.EqualTo(0.5d).Within(1e-9), "起音段线性");
            Assert.That(adsr.LevelAt(0.1d, gate), Is.EqualTo(1d).Within(1e-9), "起音终点 = 1");
            Assert.That(adsr.LevelAt(0.2d, gate), Is.EqualTo(0.75d).Within(1e-9), "衰减段");
            Assert.That(adsr.LevelAt(0.3d, gate), Is.EqualTo(0.5d).Within(1e-9), "延音起点 = Sustain");
            Assert.That(adsr.LevelAt(0.5d, gate), Is.EqualTo(0.5d).Within(1e-9), "gate 时刻仍在延音");
            Assert.That(adsr.LevelAt(0.65d, gate), Is.EqualTo(0.25d).Within(1e-9), "释音段线性");
            Assert.That(adsr.LevelAt(0.8d, gate), Is.EqualTo(0d).Within(1e-9), "释音结束归零");
            Assert.That(adsr.LevelAt(5d, gate), Is.EqualTo(0d).Within(1e-9));
        }

        [Test]
        public void Adsr_TotalDuration_IsGatePlusRelease()
        {
            var adsr = new Adsr(0.01d, 0.1d, 0.5d, 0.3d);
            Assert.That(adsr.TotalDuration(0.5d), Is.EqualTo(0.8d).Within(1e-12));
        }

        [Test]
        public void Adsr_ShortGate_ReleaseStartsFromCurrentLevelWithoutJump()
        {
            var adsr = new Adsr(0.1d, 0.2d, 0.5d, 0.3d);
            const double gate = 0.1d;

            double atGate = adsr.LevelAt(gate, gate);
            double justAfter = adsr.LevelAt(gate + 0.001d, gate);

            Assert.That(atGate, Is.EqualTo(1d).Within(1e-9));
            Assert.That(justAfter, Is.LessThanOrEqualTo(atGate));
            Assert.That(justAfter, Is.GreaterThan(0.95d), "短音符的释音应从上一点的电平连续下降，而不是跳到 Sustain");
        }

        [Test]
        public void Adsr_Apply_ScalesBuffer()
        {
            var buffer = new AudioBuffer(1000, 1000, AudioBuffer.Mono);
            for (int i = 0; i < buffer.Samples.Length; i++)
                buffer.Samples[i] = 1f;

            var adsr = new Adsr(0.1d, 0.1d, 0.5d, 0.1d);
            adsr.Apply(buffer, 0.9d); // gate 0.9 s，buffer 1 s → 最后 0.1 s 是释音

            Assert.That(buffer.Samples[0], Is.EqualTo(0f).Within(1e-6));
            Assert.That(buffer.Samples[500], Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(buffer.Samples[999], Is.LessThan(0.02f), "释音段末端应接近 0");
            Assert.That(buffer.Samples[900], Is.EqualTo(0.5f).Within(0.02f), "gate 结束前仍在延音");
        }

        // ------------------------------------------------------------------
        // 滤波器
        // ------------------------------------------------------------------

        [Test]
        public void OnePoleLowPass_AttenuatesHighFrequencyMore()
        {
            float low = RmsThroughOnePole(cutoffHz: 500d, toneHz: 1000d, highPass: false);
            float high = RmsThroughOnePole(cutoffHz: 500d, toneHz: 8000d, highPass: false);

            Assert.That(low, Is.GreaterThan(high * 3f), "一阶低通对高频的衰减应显著大于低频");
        }

        [Test]
        public void OnePoleHighPass_AttenuatesLowFrequencyMore()
        {
            float low = RmsThroughOnePole(cutoffHz: 2000d, toneHz: 100d, highPass: true);
            float high = RmsThroughOnePole(cutoffHz: 2000d, toneHz: 6000d, highPass: true);

            Assert.That(high, Is.GreaterThan(low * 3f), "一阶高通应挡住低频");
        }

        [Test]
        public void Biquad_LowPass_AttenuationIsMonotonicWithFrequency()
        {
            double[] freqs = { 200d, 800d, 3000d, 9000d };
            var rms = new float[freqs.Length];
            for (int i = 0; i < freqs.Length; i++)
                rms[i] = RmsThroughBiquadLowPass(cutoffHz: 1000d, toneHz: freqs[i]);

            for (int i = 1; i < rms.Length; i++)
            {
                Assert.That(rms[i], Is.LessThan(rms[i - 1]),
                    "低通衰减应随频率单调（" + freqs[i - 1] + " Hz → " + freqs[i] + " Hz）");
            }
        }

        [Test]
        public void Biquad_BandPass_PeaksNearCenterFrequency()
        {
            float atCenter = RmsThroughBiquadBandPass(1000d, 1000d);
            float below = RmsThroughBiquadBandPass(1000d, 150d);
            float above = RmsThroughBiquadBandPass(1000d, 6000d);

            Assert.That(atCenter, Is.GreaterThan(below));
            Assert.That(atCenter, Is.GreaterThan(above));
        }

        [Test]
        public void OnePoleLowPass_Reset_ClearsState()
        {
            OnePoleLowPass filter = OnePoleLowPass.Create(500d, Sr);
            filter.Process(1f);
            filter.Reset();
            Assert.That(filter.Process(0f), Is.EqualTo(0f).Within(1e-9));
        }

        // ------------------------------------------------------------------
        // 混响
        // ------------------------------------------------------------------

        [Test]
        public void SimpleReverb_ImpulseProducesDecayingTail()
        {
            var reverb = new SimpleReverb(Sr, 0.75f, 0.4f);
            int frames = Sr; // 1 秒
            var tail = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                float input = i == 0 ? 1f : 0f;
                tail[i] = reverb.Process(input);
            }

            float early = EnergyBetween(tail, 0, Sr / 20);       // 前 50 ms
            float mid = EnergyBetween(tail, Sr / 10, Sr / 5);    // 100–200 ms
            float late = EnergyBetween(tail, Sr * 7 / 10, Sr * 9 / 10); // 700–900 ms

            Assert.That(early, Is.GreaterThan(0f), "混响应有可听的早期反射");
            Assert.That(mid, Is.GreaterThan(0f), "混响应有中期尾巴");
            Assert.That(mid, Is.LessThan(early), "尾巴应随时间是衰减的");
            Assert.That(late, Is.LessThan(mid));
        }

        [Test]
        public void SimpleReverb_WetZero_LeavesBufferUntouched()
        {
            var buffer = new AudioBuffer(2000, Sr, AudioBuffer.Mono);
            for (int i = 0; i < buffer.Samples.Length; i++)
                buffer.Samples[i] = 0.5f;

            var reverb = new SimpleReverb(Sr);
            reverb.ProcessInPlace(buffer, 0d);

            Assert.That(buffer.Samples[0], Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(buffer.Samples[1999], Is.EqualTo(0.5f).Within(1e-6));
        }

        [Test]
        public void SimpleReverb_Clear_ResetsTail()
        {
            var reverb = new SimpleReverb(Sr);
            reverb.Process(1f);
            reverb.Clear();

            float sum = 0f;
            for (int i = 0; i < 64; i++)
                sum += System.Math.Abs(reverb.Process(0f));

            Assert.That(sum, Is.EqualTo(0f).Within(1e-9));
        }

        // ------------------------------------------------------------------
        // 辅助
        // ------------------------------------------------------------------

        static float RmsThroughOnePole(double cutoffHz, double toneHz, bool highPass)
        {
            int frames = Sr / 5;
            OnePoleLowPass lp = OnePoleLowPass.Create(cutoffHz, Sr);
            OnePoleHighPass hp = OnePoleHighPass.Create(cutoffHz, Sr);
            double sum = 0d;
            int counted = 0;

            for (int i = 0; i < frames; i++)
            {
                float value = (float)System.Math.Sin(2d * System.Math.PI * toneHz * i / Sr);
                float processed = highPass ? hp.Process(value) : lp.Process(value);
                if (i >= frames / 2)
                {
                    sum += (double)processed * processed;
                    counted++;
                }
            }

            return counted == 0 ? 0f : (float)System.Math.Sqrt(sum / counted);
        }

        static float RmsThroughBiquadLowPass(double cutoffHz, double toneHz)
        {
            int frames = Sr / 5;
            Biquad filter = Biquad.LowPass(cutoffHz, 0.707d, Sr);
            double sum = 0d;
            int counted = 0;

            for (int i = 0; i < frames; i++)
            {
                float value = (float)System.Math.Sin(2d * System.Math.PI * toneHz * i / Sr);
                float processed = filter.Process(value);
                if (i >= frames / 2)
                {
                    sum += (double)processed * processed;
                    counted++;
                }
            }

            return counted == 0 ? 0f : (float)System.Math.Sqrt(sum / counted);
        }

        static float RmsThroughBiquadBandPass(double centerHz, double toneHz)
        {
            int frames = Sr / 5;
            Biquad filter = Biquad.BandPass(centerHz, 1.0d, Sr);
            double sum = 0d;
            int counted = 0;

            for (int i = 0; i < frames; i++)
            {
                float value = (float)System.Math.Sin(2d * System.Math.PI * toneHz * i / Sr);
                float processed = filter.Process(value);
                if (i >= frames / 2)
                {
                    sum += (double)processed * processed;
                    counted++;
                }
            }

            return counted == 0 ? 0f : (float)System.Math.Sqrt(sum / counted);
        }

        static float EnergyBetween(float[] data, int from, int to)
        {
            double sum = 0d;
            int start = from < 0 ? 0 : from;
            int end = to > data.Length ? data.Length : to;
            for (int i = start; i < end; i++)
                sum += (double)data[i] * data[i];
            return (float)(sum / System.Math.Max(1, end - start));
        }
    }
}
