using System;
using PirateCrew.Audio.Synth;

namespace PirateCrew.Audio
{
    /// <summary>
    /// UI 音合成：按钮点击、面板展开、错误/禁用。
    ///
    /// 【为什么单独一组】UI 音必须在任何音量下都「短、干净、不抢戏」：
    ///   · 时长全部 &lt; 0.4 s——玩家可能一秒点五次，长音会糊成一片（配合 AudioService
    ///     的同帧去抖与 50 ms 去重）；
    ///   · 高频为主（1 kHz 以上或明确的上扫），与战斗低频爆炸在频谱上错开，
    ///     不会被爆炸声掩蔽；
    ///   · 错误音刻意用低频方波制造「不舒服」，这是刻意的负反馈设计。
    /// 参数均为 **AI 提案/待定**，需人耳验收。
    /// </summary>
    public static class UiSfx
    {
        /// <summary>按钮点击（0.09 s）。</summary>
        public static AudioBuffer UiClick(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.09d, sampleRate);
            var rng = new SynthRandom(0xC11C4E01u);

            SynthUtil.AddGlideTone(buffer, 0d, 0.05d, 1200d, 900d, 0.012d, 0.013d, 0.55f, true, 0.13d);
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.004d, 0.35f, 0d, 0.0015d,
                NoiseFilterKind.HighPass, 1500d, 2600d, 0.7d);

            buffer.ApplyFadeOut(0.008d);
            return buffer;
        }

        /// <summary>面板展开（0.36 s）：柔和上扫 + 轻微混响。</summary>
        public static AudioBuffer UiPanelOpen(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.36d, sampleRate);
            var rng = new SynthRandom(0x9A1E1001u);

            // 主体：带通噪声从 380 → 2600 Hz 上扫，起音 40 ms（柔和，不「啪」）
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.34d, 0.60f, 0.04d, 0.16d,
                NoiseFilterKind.BandPass, 380d, 2600d, 0.9d);
            // 叠加上行三角，给一个明确的「打开」音高方向
            SynthUtil.AddGlideTone(buffer, 0d, 0.30d, 300d, 620d, 0.18d, 0.14d, 0.22f, true, 0.22d);

            buffer.ApplyFadeIn(0.01d);
            buffer.ApplyFadeOut(0.06d);

            var reverb = new SimpleReverb(sampleRate, 0.50f, 0.55f);
            reverb.ProcessInPlace(buffer, 0.12d);
            return buffer;
        }

        /// <summary>错误/禁用（0.32 s）：低频方波双音，刻意刺耳。</summary>
        public static AudioBuffer UiError(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.32d, sampleRate);
            var rng = new SynthRandom(0xE4401001u);

            AddHarshTone(buffer, 0d, 0.14d, 160d, 0.42f);
            AddHarshTone(buffer, 0.15d, 0.16d, 120d, 0.45f);

            // 5 Hz 调幅 + 噪声粗糙化
            Detune.ApplyTremolo(buffer, 5d, 0.30d);
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.30d, 0.12f, 0.005d, 0.10d,
                NoiseFilterKind.BandPass, 700d, 500d, 1.0d);

            buffer.ApplyFadeIn(0.004d);
            buffer.ApplyFadeOut(0.03d);
            return buffer;
        }

        /// <summary>低沉刺耳的方波短音（错误音的基本单元）。</summary>
        static void AddHarshTone(
            AudioBuffer buffer, double startSeconds, double durationSeconds,
            double frequencyHz, float gain)
        {
            int sampleRate = buffer.SampleRate;
            int startFrame = (int)Math.Round(startSeconds * sampleRate);
            int frames = AudioBuffer.FramesForSeconds(durationSeconds, sampleRate);

            var adsr = new Adsr(0.004d, 0.03d, 0.60d, 0.04d);
            double gate = durationSeconds * 0.75d;
            double phase = 0d;

            for (int f = 0; f < frames; f++)
            {
                int dst = startFrame + f;
                if (dst >= buffer.FrameCount)
                    break;

                double t = (double)f / sampleRate;
                phase += frequencyHz / sampleRate;
                if (phase >= 1d)
                    phase -= Math.Floor(phase);

                float level = (float)(adsr.LevelAt(t, gate) * gain);
                buffer.Samples[dst] += (float)Waveforms.Square(phase, 0.34d) * level;
            }
        }
    }
}
