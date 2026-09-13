using System;
using PirateCrew.PirateCrew.Audio.Synth;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>
    /// 环境音合成：海浪循环、风声循环、海鸥鸣叫（3 个变体）。
    ///
    /// 【氛围基调】docs/美术风格指南.md:12-16 定的是「加勒比正午海岛」——
    /// 明亮、温暖、有风但不阴沉。因此：
    ///   · 海浪以中高频泡沫为主、低频隆隆为辅，不做风暴感的重低音；
    ///   · 风声用 250–900 Hz 带通，避开刺耳的 2–4 kHz 啸叫；
    ///   · 海鸥基频取 900–1700 Hz，接近真实 Laughing Gull 的叫声区间。
    ///
    /// 【循环无缝是硬要求】海浪/风声是持续循环播放的底噪，接缝处任何跳变都会被听出来。
    /// 做法：① 所有调制频率取循环长度的整数分频（如 6 秒内正好 3 个涌浪周期）；
    /// ② 多生成一段尾巴并用 <see cref="AudioBuffer.FoldSeamlessLoop"/> 与头部交叉淡化，
    /// 使「末帧→首帧」等价于连续信号里的相邻两帧。参数均为 **AI 提案/待定**，需人耳验收。
    /// </summary>
    public static class AmbientSfx
    {
        /// <summary>海浪循环体（6.0 s，无缝）。</summary>
        public static AudioBuffer WavesLoop(int sampleRate)
        {
            const double loopSeconds = 6.0d;
            const double tailSeconds = 0.5d;
            const double swellPeriod = 2.0d;   // 6 秒内 3 个涌浪周期
            const double rumblePeriod = 6.0d;  // 1 个长周期

            int frames = AudioBuffer.FramesForSeconds(loopSeconds + tailSeconds, sampleRate);
            var raw = new AudioBuffer(frames, sampleRate, AudioBuffer.Mono);
            var rng = new SynthRandom(0xB1ACB1ACu);

            var rumbleLow = OnePoleLowPass.Create(260d, sampleRate);
            var swellLow = OnePoleLowPass.Create(1800d, sampleRate);
            var swellLow2 = OnePoleLowPass.Create(1400d, sampleRate);
            var foamHigh = OnePoleHighPass.Create(3000d, sampleRate);

            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / sampleRate;
                float noise = rng.NextBipolar();

                // 低频隆隆（长周期呼吸）
                float rumble = rumbleLow.Process(noise)
                               * 0.34f
                               * (float)(0.85d + 0.15d * Math.Sin(2d * Math.PI * t / rumblePeriod));

                // 涌浪 1：每 2 秒一次，sin^1.6 包络（缓起快落，像浪推上来）
                double swellPhase = Wrap01(t / swellPeriod);
                double swellEnv = Math.Pow(Math.Max(0d, Math.Sin(Math.PI * swellPhase)), 1.6d);
                float swell = swellLow.Process(noise) * (float)(0.55d * swellEnv);

                // 浪花泡沫：与涌浪同相但更尖（sin^4），只在浪峰出现
                double foamEnv = Math.Pow(Math.Max(0d, Math.Sin(Math.PI * swellPhase)), 4.0d);
                float foam = foamHigh.Process(noise) * (float)(0.30d * foamEnv);

                // 涌浪 2：错开 1 秒，层次更厚
                double swellPhase2 = Wrap01((t + 1.0d) / swellPeriod);
                double swellEnv2 = Math.Pow(Math.Max(0d, Math.Sin(Math.PI * swellPhase2)), 2.2d);
                float swell2 = swellLow2.Process(noise) * (float)(0.30d * swellEnv2);

                raw.Samples[f] = rumble + swell + foam + swell2;
            }

            return raw.FoldSeamlessLoop(AudioBuffer.FramesForSeconds(tailSeconds, sampleRate));
        }

        /// <summary>风声循环体（8.0 s，无缝）。</summary>
        public static AudioBuffer WindLoop(int sampleRate)
        {
            const double loopSeconds = 8.0d;
            const double tailSeconds = 0.6d;

            int frames = AudioBuffer.FramesForSeconds(loopSeconds + tailSeconds, sampleRate);
            var raw = new AudioBuffer(frames, sampleRate, AudioBuffer.Mono);
            var rng = new SynthRandom(0x71D071D0u);

            var bandLow = OnePoleLowPass.Create(900d, sampleRate);
            var bandHigh = OnePoleHighPass.Create(250d, sampleRate);
            var subLow = OnePoleLowPass.Create(180d, sampleRate);

            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / sampleRate;
                float noise = rng.NextBipolar();

                // 阵风：0.125 Hz（8 秒 1 周期）与 0.25 Hz（8 秒 2 周期）叠加
                double gust = 0.60d
                              + 0.28d * Math.Sin(2d * Math.PI * t / loopSeconds)
                              + 0.12d * Math.Sin(2d * Math.PI * 2d * t / loopSeconds + 1.3d);

                // 带通 250–900 Hz：先低通再高通
                float band = bandHigh.Process(bandLow.Process(noise)) * (float)(gust * 0.60d);
                // 低频底噪
                float sub = subLow.Process(noise)
                            * 0.24f
                            * (float)(0.80d + 0.20d * Math.Sin(2d * Math.PI * t / loopSeconds + 0.7d));

                raw.Samples[f] = band + sub;
            }

            return raw.FoldSeamlessLoop(AudioBuffer.FramesForSeconds(tailSeconds, sampleRate));
        }

        /// <summary>海鸥鸣叫变体 1（0.90 s，两声）。</summary>
        public static AudioBuffer SeagullCry1(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.90d, sampleRate);
            var rng = new SynthRandom(0x600D0001u);

            AddSquawk(buffer, ref rng, 0.02d, 0.36d, 1150d, 1600d, 900d, 0.62f);
            AddSquawk(buffer, ref rng, 0.44d, 0.34d, 1080d, 1520d, 860d, 0.58f);

            var reverb = new SimpleReverb(sampleRate, 0.55f, 0.55f);
            reverb.ProcessInPlace(buffer, 0.14d);
            return buffer;
        }

        /// <summary>海鸥鸣叫变体 2（1.10 s，三声、基频更低、间距更长）。</summary>
        public static AudioBuffer SeagullCry2(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(1.10d, sampleRate);
            var rng = new SynthRandom(0x600D0002u);

            AddSquawk(buffer, ref rng, 0.02d, 0.30d, 980d, 1380d, 820d, 0.60f);
            AddSquawk(buffer, ref rng, 0.36d, 0.30d, 940d, 1320d, 800d, 0.56f);
            AddSquawk(buffer, ref rng, 0.70d, 0.36d, 1020d, 1440d, 880d, 0.58f);

            var reverb = new SimpleReverb(sampleRate, 0.55f, 0.55f);
            reverb.ProcessInPlace(buffer, 0.14d);
            return buffer;
        }

        /// <summary>海鸥鸣叫变体 3（0.75 s，单声短促、尾音上扬）。</summary>
        public static AudioBuffer SeagullCry3(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.75d, sampleRate);
            var rng = new SynthRandom(0x600D0003u);

            AddSquawk(buffer, ref rng, 0.02d, 0.55d, 1250d, 1700d, 1180d, 0.64f);

            var reverb = new SimpleReverb(sampleRate, 0.55f, 0.55f);
            reverb.ProcessInPlace(buffer, 0.14d);
            return buffer;
        }

        /// <summary>
        /// 一声「嘎」：基频按 起→峰→尾 三段线性包络滑变，叠加 4 个谐波（1/h^1.5）与
        /// 少量带通噪声制造嘶哑；包络快起、短延音、快速释音。
        /// </summary>
        static void AddSquawk(
            AudioBuffer buffer,
            ref SynthRandom rng,
            double startSeconds,
            double durationSeconds,
            double fromHz,
            double peakHz,
            double toHz,
            float gain)
        {
            int sampleRate = buffer.SampleRate;
            int startFrame = (int)Math.Round(startSeconds * sampleRate);
            int frames = AudioBuffer.FramesForSeconds(durationSeconds, sampleRate);

            var adsr = new Adsr(0.008d, 0.06d, 0.35d, 0.09d);
            double gate = durationSeconds * 0.75d;
            double phase = 0d;

            for (int f = 0; f < frames; f++)
            {
                int dst = startFrame + f;
                if (dst >= buffer.FrameCount)
                    break;

                double t = (double)f / sampleRate;
                double freq;
                if (t < durationSeconds * 0.35d)
                    freq = Waveforms.Lerp(fromHz, peakHz, t, durationSeconds * 0.35d);
                else
                    freq = Waveforms.Lerp(peakHz, toHz, t - durationSeconds * 0.35d, durationSeconds * 0.65d);

                phase += freq / sampleRate;
                if (phase >= 1d)
                    phase -= Math.Floor(phase);

                double tone = Waveforms.Harmonics(phase, 4, 1.5d, 0.25d);
                float level = (float)(adsr.LevelAt(t, gate) * gain);
                buffer.Samples[dst] += (float)tone * level;
            }

            // 嘶哑噪声：与音头同起，短促
            float rasp = gain * 0.22f;
            SynthUtil.AddFilteredNoise(buffer, ref rng, startSeconds, durationSeconds * 0.55d, rasp,
                0.006d, 0.05d, NoiseFilterKind.BandPass, 3000d, 2600d, 1.2d);
        }

        static double Wrap01(double value)
        {
            return value - Math.Floor(value);
        }
    }
}
