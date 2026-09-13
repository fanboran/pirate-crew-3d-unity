using System;
using PirateCrew.PirateCrew.Audio.Synth;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>
    /// 战斗音效合成（爆炸、木质碎裂、命中、入水、投掷、弹跳、滚动、地雷蜂鸣、阵亡）。
    ///
    /// 【音色设计原则】全部原创合成，不采样任何第三方素材（版权纪律）。
    /// 爆炸/撞击这类冲击音的通用配方骨架是三层：
    ///   ① 低频冲击（正弦/次低音指数滑落）负责「体感」，人耳对 30–100 Hz 的瞬态最敏感；
    ///   ② 中频噪声爆（滤波白噪 + 快衰减）负责「材质」，决定听上去是炸药还是木头；
    ///   ③ 尾音/混响负责「空间」，把声音从「贴脸」推到场景里。
    /// 三层比例与参数均为 **AI 提案/待定**，需人耳验收（见交付报告第 ⑦ 节）。
    /// </summary>
    public static class CombatSfx
    {
        /// <summary>爆炸（1.6 s）。</summary>
        public static AudioBuffer Explosion(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(1.60d, sampleRate);
            var rng = new SynthRandom(0x0B1E5A11u);

            // ① 低频冲击：95 → 32 Hz 指数滑落，负责胸腔感
            SynthUtil.AddGlideTone(buffer, 0d, 0.70d, 95d, 32d, 0.12d, 0.22d, 0.95f, true, 0.25d);
            // ② 次低音 punch：稍晚、更短，制造「轰」的双层体感
            SynthUtil.AddGlideTone(buffer, 0d, 0.35d, 62d, 28d, 0.08d, 0.15d, 0.70f, true, 0.60d);
            // ③ 噪声爆：低通 1500 → 600 Hz，快衰减
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.80d, 1.00f, 0.002d, 0.09d,
                NoiseFilterKind.LowPass, 1500d, 600d, 0.9d);
            // ④ 高频碎片（火药/碎屑的「嗤」）
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.25d, 0.45f, 0.001d, 0.05d,
                NoiseFilterKind.HighPass, 2500d, 1200d, 0.7d);
            // ⑤ 隆隆尾（烟尘与回响）
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0.05d, 1.50d, 0.42f, 0.02d, 0.55d,
                NoiseFilterKind.LowPass, 260d, 140d, 1.0d);

            var reverb = new SimpleReverb(sampleRate, 0.72f, 0.45f);
            reverb.ProcessInPlace(buffer, 0.25d);
            return buffer;
        }

        /// <summary>木桶/木箱碎裂（0.6 s）。</summary>
        public static AudioBuffer WoodCrack(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.60d, sampleRate);
            var rng = new SynthRandom(0x5EEDC0DEu);

            // ① 木质腔体：低阶分音衰减，决定「是木头不是石头」
            SynthUtil.AddDecayingPartials(buffer, 0d, 0.32d,
                new[] { 190d, 320d, 540d, 870d },
                new[] { 0.90f, 0.52f, 0.34f, 0.18f },
                new[] { 0.09d, 0.12d, 0.16d, 0.19d }, 0.31d);
            // ② 低频砰
            SynthUtil.AddGlideTone(buffer, 0d, 0.20d, 220d, 90d, 0.06d, 0.09d, 0.62f, true, 0.30d);

            // ③ 4–6 段木片断裂噪声（时间随机错位，避免像合成器）
            int pieces = 5 + (int)(rng.NextFloat() * 2f);
            for (int i = 0; i < pieces; i++)
            {
                double start = rng.NextRange(0.004f, 0.27f);
                double dur = rng.NextRange(0.008f, 0.030f);
                float gain = rng.NextRange(0.35f, 0.72f);
                double cutoff = rng.NextRange(900f, 2400f);
                double sweep = cutoff * rng.NextRange(1.4f, 1.9f);
                double q = rng.NextRange(1.1f, 1.8f);

                SynthUtil.AddFilteredNoise(buffer, ref rng, start, dur, gain, 0.0008d, 0.015d,
                    NoiseFilterKind.BandPass, cutoff, sweep, q);
            }

            var reverb = new SimpleReverb(sampleRate, 0.60f, 0.50f);
            reverb.ProcessInPlace(buffer, 0.15d);
            return buffer;
        }

        /// <summary>命中肉体（0.24 s）。</summary>
        public static AudioBuffer FleshHit(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.24d, sampleRate);
            var rng = new SynthRandom(0xF1E5B00Bu);

            // 闷响：150 → 65 Hz，短
            SynthUtil.AddGlideTone(buffer, 0d, 0.20d, 150d, 65d, 0.04d, 0.05d, 0.95f, true, 0.20d);
            // 拍打：低通噪声，极快衰减
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.12d, 0.55f, 0.001d, 0.025d,
                NoiseFilterKind.LowPass, 1400d, 700d, 0.8d);
            return buffer;
        }

        /// <summary>弹体入水（0.7 s）。</summary>
        public static AudioBuffer WaterSplash(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.70d, sampleRate);
            var rng = new SynthRandom(0x5A1A5A1Au);

            // ① 水花：高通噪声，快起快落
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.40d, 0.90f, 0.004d, 0.08d,
                NoiseFilterKind.HighPass, 700d, 1300d, 0.7d);
            // ② 音高下坠（「噗通」的调性成分）
            SynthUtil.AddGlideTone(buffer, 0d, 0.14d, 700d, 250d, 0.05d, 0.08d, 0.35f, true, 0.10d);

            // ③ 气泡尾：低通噪声 + 11 Hz 调幅（单独缓冲再混入，避免调制影响到水花本体）
            AudioBuffer bubbles = SynthUtil.Create(0.60d, sampleRate);
            SynthUtil.AddFilteredNoise(bubbles, ref rng, 0d, 0.60d, 0.40f, 0.01d, 0.25d,
                NoiseFilterKind.LowPass, 520d, 320d, 1.2d);
            Detune.ApplyTremolo(bubbles, 11d, 0.55d);
            buffer.MixIn(bubbles, AudioBuffer.FramesForSeconds(0.06d, sampleRate), 1f);

            return buffer;
        }

        /// <summary>投掷出手 whoosh（0.42 s）。</summary>
        public static AudioBuffer ThrowWhoosh(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.42d, sampleRate);
            var rng = new SynthRandom(0x0FF5A17Du);

            // 中心频率先升后降：两段带通扫，模拟物体掠过耳边
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.21d, 0.70f, 0.025d, 0d,
                NoiseFilterKind.BandPass, 220d, 2400d, 1.2d);
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0.19d, 0.23d, 0.70f, 0.005d, 0.065d,
                NoiseFilterKind.BandPass, 2400d, 420d, 1.2d);
            // 低频气流垫，避免只有「嘶」没有「甩」
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.40d, 0.22f, 0.06d, 0.10d,
                NoiseFilterKind.LowPass, 500d, 260d, 0.8d);

            buffer.ApplyFadeIn(0.004d);
            buffer.ApplyFadeOut(0.02d);
            return buffer;
        }

        /// <summary>落地弹跳（0.16 s）。</summary>
        public static AudioBuffer Bounce(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.16d, sampleRate);
            var rng = new SynthRandom(0xB0A2CE01u);

            SynthUtil.AddGlideTone(buffer, 0d, 0.14d, 420d, 130d, 0.05d, 0.05d, 0.90f, true, 0.10d);
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.006d, 0.45f, 0d, 0.002d,
                NoiseFilterKind.HighPass, 2000d, 3200d, 0.7d);
            return buffer;
        }

        /// <summary>石块滚动（1.4 s）。</summary>
        public static AudioBuffer StoneRoll(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(1.40d, sampleRate);
            var rng = new SynthRandom(0x5709E0A1u);

            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 1.40d, 0.65f, 0.06d, 0d,
                NoiseFilterKind.LowPass, 420d, 300d, 1.0d);
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 1.40d, 0.30f, 0.08d, 0d,
                NoiseFilterKind.BandPass, 900d, 700d, 1.5d);
            // 70 Hz 次低音：给石头「重量」
            SynthUtil.AddGlideTone(buffer, 0d, 1.40d, 72d, 64d, 0.70d, 0d, 0.16f, true, 0.40d);

            // 摩擦调制：4 Hz 与 7 Hz 叠加（互质，避免明显周期）
            int frames = buffer.FrameCount;
            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / sampleRate;
                double mod = 0.70d
                             + 0.18d * Math.Sin(2d * Math.PI * 4d * t)
                             + 0.12d * Math.Sin(2d * Math.PI * 7d * t + 1.1d);
                float m = (float)mod;
                for (int c = 0; c < buffer.Channels; c++)
                    buffer.Samples[f * buffer.Channels + c] *= m;
            }

            buffer.ApplyFadeIn(0.06d);
            buffer.ApplyFadeOut(0.28d);
            return buffer;
        }

        /// <summary>地雷引信蜂鸣（0.09 s）。</summary>
        public static AudioBuffer MineBeep(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.09d, sampleRate);
            var adsr = new Adsr(0.005d, 0.010d, 0.60d, 0.020d);
            double gate = 0.06d;
            double phase = 0d;
            int frames = buffer.FrameCount;

            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / sampleRate;
                double freq = 2093d * (1d - 0.02d * Math.Min(1d, t / gate)); // 轻微下坠，更像机械蜂鸣
                phase += freq / sampleRate;
                if (phase >= 1d)
                    phase -= Math.Floor(phase);

                float level = (float)adsr.LevelAt(t, gate);
                float value = (float)(Waveforms.Square(phase, 0.42d) * level * 0.55d);
                buffer.SetSample(f, value);
            }

            return buffer;
        }

        /// <summary>船员阵亡（0.7 s，2D）。</summary>
        public static AudioBuffer CrewDown(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.70d, sampleRate);
            var adsr = new Adsr(0.02d, 0.18d, 0.55d, 0.30d);
            double gate = 0.40d;

            // 下行低音号角：锯齿谐波叠加（关押感）+ 指数下滑
            double phase = 0d;
            int frames = buffer.FrameCount;
            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / sampleRate;
                double freq = Waveforms.Glide(330d, 165d, t, 0.25d);
                phase += freq / sampleRate;
                if (phase >= 1d)
                    phase -= Math.Floor(phase);

                double tone = Waveforms.Harmonics(phase, 5, 1.35d, 0.35d);
                float level = (float)adsr.LevelAt(t, gate);
                buffer.SetSample(f, (float)(tone * level * 0.70d));
            }

            SynthUtil.ShapeSpectrum(buffer, NoiseFilterKind.LowPass, 1200d, 400d, 0.9d);
            var reverb = new SimpleReverb(sampleRate, 0.66f, 0.50f);
            reverb.ProcessInPlace(buffer, 0.20d);
            return buffer;
        }
    }
}
