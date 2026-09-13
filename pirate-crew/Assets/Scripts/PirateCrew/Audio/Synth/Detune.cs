using System;

namespace PirateCrew.PirateCrew.Audio.Synth
{
    /// <summary>
    /// 抖动 / 失谐 / 颤音工具（纯函数）——「反电子味」的核心手段。
    ///
    /// 【为什么需要】单一正弦或理想锯齿听起来像测试音、不像乐器；真实乐器天然存在：
    ///   ① 多个发声体（弦/管/人声）的微小音高差异 → 失谐叠加（chorus 感）；
    ///   ② 音高随时间的缓慢漂移 → 抖动（把「机械精确」打散）；
    ///   ③ 振幅的周期性起伏 → 颤音/揉弦。
    /// 三者叠加后，程序化合成才不会被耳朵立刻判为「电子音」。
    ///
    /// 【与 Unity 内置效果的区别】AudioSource 的 pitch 是全局不变值，做不到逐样本的
    /// 缓慢漂移；离线合成可以在生成阶段就把这些细节写进波形，且零运行时开销。
    ///
    /// 各项默认量（cents / rate / depth）为 **AI 提案/待定**，需人耳验收。
    /// </summary>
    public static class Detune
    {
        /// <summary>半音 = 100 音分。音分 → 频率倍率。</summary>
        public static double CentsToRatio(double cents)
        {
            return Math.Pow(2d, cents / 1200d);
        }

        /// <summary>半音数 → 频率倍率。</summary>
        public static double SemitonesToRatio(double semitones)
        {
            return Math.Pow(2d, semitones / MusicTheory.SemitonesPerOctave);
        }

        /// <summary>
        /// 缓慢随机游走的音高偏移（音分）。用两个不同速率的正弦叠加确定性哈希噪声，
        /// 得到「不重复但连续」的漂移，比纯白噪声更接近演奏中的音准起伏。
        /// </summary>
        public static double JitterCents(double t, double amountCents, double rateHz, int seed)
        {
            if (amountCents <= 0d)
                return 0d;

            // 用多个互质速率叠加，避免周期感
            double a = Math.Sin(2d * Math.PI * rateHz * t);
            double b = Math.Sin(2d * Math.PI * rateHz * 0.618d * t + 1.7d);
            double c = Math.Sin(2d * Math.PI * rateHz * 1.414d * t + 3.1d);
            double wander = (a * 0.55d + b * 0.30d + c * 0.15d);

            // 叠加一个由 seed 决定的缓变偏置，保证同种子可复现、不同种子不雷同
            double bias = (SynthRandom.HashBipolar(seed, 0x51ED2701u)) * 0.25d;
            return amountCents * (wander + bias);
        }

        /// <summary>
        /// 在缓冲的 [<paramref name="startSeconds"/>, +<paramref name="durationSeconds"/>)
        /// 区间叠加一组合唱式失谐音（<paramref name="voices"/> 个振荡器，均匀分布在
        /// ±<paramref name="spreadCents"/> 音分范围内）。
        ///
        /// <paramref name="harmonics"/> 为每音振荡器叠加的谐波数（1 = 纯正弦；
        /// 3–4 配合 <paramref name="harmonicRolloff"/> ≈ 1.2–1.5 得到柔和的号角/弦垫音色）。
        /// 每个音的包络由 <paramref name="envelope"/> 决定（gate = duration）。
        ///
        /// 【相位偏移设计】各音的初始相位按黄金比例散开，避免所有振荡器在 t=0 同相叠加
        /// 产生一记「爆音式起振」。
        /// </summary>
        public static void AddToneStack(
            AudioBuffer target,
            double startSeconds,
            double durationSeconds,
            double frequencyHz,
            int voices,
            double spreadCents,
            float gain,
            int harmonics,
            double harmonicRolloff,
            Adsr envelope,
            uint seed,
            double jitterCents = 0d,
            double jitterRateHz = 0.7d)
        {
            if (target == null || durationSeconds <= 0d || frequencyHz <= 0d)
                return;
            if (voices < 1)
                voices = 1;

            int startFrame = (int)Math.Round(startSeconds * target.SampleRate);
            int frames = AudioBuffer.FramesForSeconds(durationSeconds, target.SampleRate);
            int sampleRate = target.SampleRate;
            double gate = durationSeconds;

            for (int v = 0; v < voices; v++)
            {
                // voices=1 时偏移为 0；否则在 [-1,1] 上均匀铺开
                double spread = voices <= 1
                    ? 0d
                    : (v / (double)(voices - 1)) * 2d - 1d;
                double cents = spread * spreadCents;
                double phase = SynthRandom.Hash01(v + 1, seed) ; // [0,1) 初始相位
                double voiceGain = gain / (float)Math.Sqrt(voices);

                for (int f = 0; f < frames; f++)
                {
                    int dst = startFrame + f;
                    if (dst < 0 || dst >= target.FrameCount)
                        break;

                    double t = (double)f / sampleRate;
                    double level = envelope.LevelAt(t, gate);
                    if (level <= 0d)
                        continue;

                    double drift = jitterCents > 0d
                        ? JitterCents(t, jitterCents, jitterRateHz, (int)(seed + (uint)v))
                        : 0d;

                    double freq = frequencyHz * CentsToRatio(cents + drift);
                    phase += freq / sampleRate;
                    if (phase >= 1d)
                        phase -= Math.Floor(phase);

                    double sample = harmonics <= 1
                        ? Waveforms.Sine(phase)
                        : Waveforms.Harmonics(phase, harmonics, harmonicRolloff);

                    // 每个 voice 独立声道叠加（单声道）
                    for (int c = 0; c < target.Channels; c++)
                        target.Samples[dst * target.Channels + c] += (float)(sample * level * voiceGain);
                }
            }
        }

        /// <summary>
        /// 对缓冲施加颤音（振幅周期性起伏）。
        /// <paramref name="depth"/> = 0 无效果、1 振幅最低到 0。
        /// </summary>
        public static void ApplyTremolo(AudioBuffer buffer, double rateHz, double depth)
        {
            if (buffer == null || depth <= 0d)
                return;
            if (depth > 1d)
                depth = 1d;

            int sampleRate = buffer.SampleRate;
            int frames = buffer.FrameCount;
            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / sampleRate;
                double mod = 1d - depth * 0.5d * (1d - Math.Cos(2d * Math.PI * rateHz * t));
                for (int c = 0; c < buffer.Channels; c++)
                    buffer.Samples[f * buffer.Channels + c] *= (float)mod;
            }
        }

        /// <summary>
        /// 简单的「呼吸式」振幅起伏（多个互质低频相乘），用于环境音的长周期强弱变化。
        /// 返回 [0,1] 的调制系数，不做状态，可直接按 t 求值。
        /// </summary>
        public static double Breath(double t, double baseRateHz, double depth, int seed)
        {
            if (depth <= 0d || baseRateHz <= 0d)
                return 1d;

            double sunit = SynthRandom.Hash01(seed, 0x2F1B3C5Du);
            double a = Math.Sin(2d * Math.PI * baseRateHz * t + sunit * 6.2831853d);
            double b = Math.Sin(2d * Math.PI * baseRateHz * 0.379d * t + 1.1d);
            double mod = 0.5d + 0.5d * (a * 0.7d + b * 0.3d);
            return 1d - depth + depth * mod;
        }
    }
}
