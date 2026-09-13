using System;

namespace PirateCrew.PirateCrew.Audio.Synth
{
    /// <summary>
    /// ADSR 分段包络（纯值类型 + 纯函数，可逐点断言）。
    ///
    /// 分段定义（t 为自音头发声起的秒数，gate 为「音符按住」时长）：
    ///   [0, A)            : 线性上升 0 → 1
    ///   [A, A+D)          : 线性下降 1 → Sustain
    ///   [A+D, gate)       : 恒定 Sustain
    ///   [gate, gate+R)    : 从 gate 时刻的包络值线性下降到 0
    ///   ≥ gate+R          : 0
    ///
    /// 【与 Gate 的关系】Release 从「gate 时刻的实际包络值」起算，而不是固定从 Sustain 起算——
    /// 否则当 gate < A+D（音符短于起音+衰减）时会出现包络值跳变（咔哒声）。
    ///
    /// 【数值】下列默认值均为 **AI 提案/待定**，需人耳验收；它们只影响听感，不影响机制。
    /// </summary>
    [Serializable]
    public struct Adsr
    {
        /// <summary>起音时长（秒）。</summary>
        public double Attack;

        /// <summary>衰减时长（秒）。</summary>
        public double Decay;

        /// <summary>延音电平（0–1）。</summary>
        public double Sustain;

        /// <summary>释音时长（秒）。</summary>
        public double Release;

        public Adsr(double attack, double decay, double sustain, double release)
        {
            Attack = attack < 0d ? 0d : attack;
            Decay = decay < 0d ? 0d : decay;
            Sustain = sustain < 0d ? 0d : (sustain > 1d ? 1d : sustain);
            Release = release < 0d ? 0d : release;
        }

        /// <summary>打击乐式：极短起音 + 短衰减 + 零延音（爆炸/撞击的包络骨架）。</summary>
        public static Adsr Percussive(double attack = 0.002d, double decay = 0.15d)
        {
            return new Adsr(attack, decay, 0d, 0.02d);
        }

        /// <summary>拨弦/木质感：短起音、中衰减、低延音。</summary>
        public static Adsr Pluck(double attack = 0.003d, double decay = 0.09d, double sustain = 0.18d, double release = 0.08d)
        {
            return new Adsr(attack, decay, sustain, release);
        }

        /// <summary>包络在第 <paramref name="t"/> 秒、gate 时长 <paramref name="gate"/> 秒时的电平。</summary>
        public double LevelAt(double t, double gate)
        {
            if (t < 0d)
                return 0d;

            double attackEnd = Attack;
            double decayEnd = Attack + Decay;

            if (t < attackEnd)
                return Attack <= 0d ? 1d : t / Attack;

            if (t < decayEnd)
                return Decay <= 0d ? Sustain : 1d + (Sustain - 1d) * ((t - attackEnd) / Decay);

            if (t < gate)
                return Sustain;

            // release 从 gate 时刻的实际电平起算，避免短音符的跳变
            double releaseStart = Sustain;
            if (gate < decayEnd)
                releaseStart = LevelAt(gate, gate);
            if (t >= gate + Release)
                return 0d;
            if (Release <= 0d)
                return 0d;
            return releaseStart * (1d - (t - gate) / Release);
        }

        /// <summary>包络彻底归零所需的总时长（gate + release）。</summary>
        public double TotalDuration(double gate)
        {
            return (gate < 0d ? 0d : gate) + Release;
        }

        /// <summary>把包络施加到整段缓冲（逐帧按 t = frame / SampleRate 求值）。</summary>
        public void Apply(AudioBuffer buffer, double gate, float gain = 1f)
        {
            if (buffer == null)
                return;

            int frames = buffer.FrameCount;
            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / buffer.SampleRate;
                float level = (float)(LevelAt(t, gate) * gain);
                for (int c = 0; c < buffer.Channels; c++)
                    buffer.Samples[f * buffer.Channels + c] *= level;
            }
        }
    }
}
