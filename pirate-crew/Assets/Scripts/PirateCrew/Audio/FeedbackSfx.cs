using System;
using PirateCrew.Audio.Synth;

namespace PirateCrew.Audio
{
    /// <summary>
    /// 反馈音合成：单位选中、武器切换、回合开始/结束、危险提示。
    ///
    /// 【与 UI 音的分工】这里都是「游戏状态变化」的反馈（回合流转、选中、危险），
    /// 音色偏木质/号角，落在游戏世界里；<see cref="UiSfx"/> 是「界面操作」的反馈，
    /// 音色偏电子短促，落在界面层。两者都用 Sfx 总线，但音色语言不同。
    ///
    /// 【为什么用音程而不是单音】短促的双音/三音上行/下行能让玩家不靠文字就听出
    /// 「这是开始还是结束」「这是好事还是警告」——音程方向是最廉价的状态编码。
    /// 具体音高/时长为 **AI 提案/待定**，需人耳验收。
    /// </summary>
    public static class FeedbackSfx
    {
        /// <summary>单位选中（0.16 s）：C6→G6 两音上行。</summary>
        public static AudioBuffer UnitSelect(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.16d, sampleRate);
            var adsr = new Adsr(0.004d, 0.014d, 0.50d, 0.030d);

            // C6 = MIDI 84 = 1046.5 Hz；G6 = MIDI 91 = 1568.0 Hz
            Detune.AddToneStack(buffer, 0d, 0.07d, MusicTheory.MidiToFrequency(84), 2, 6d, 0.50f,
                3, 1.4d, adsr, 0x501EC1u, 3d, 0.9d);
            Detune.AddToneStack(buffer, 0.055d, 0.09d, MusicTheory.MidiToFrequency(91), 2, 6d, 0.46f,
                3, 1.4d, adsr, 0x501EC2u, 3d, 0.9d);

            buffer.ApplyFadeOut(0.012d);
            return buffer;
        }

        /// <summary>武器切换（0.22 s）：机械点击 + 两下木扣。</summary>
        public static AudioBuffer WeaponSwitch(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.22d, sampleRate);
            var rng = new SynthRandom(0x5A17C401u);

            // 第一下：点击 + 木扣（高频 520 Hz）
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.008d, 0.45f, 0d, 0.002d,
                NoiseFilterKind.HighPass, 2500d, 3500d, 0.7d);
            SynthUtil.AddDecayingPartials(buffer, 0d, 0.10d,
                new[] { 520d, 830d }, new[] { 0.55f, 0.25f }, new[] { 0.028d, 0.030d }, 0.17d);

            // 第二下：70 ms 后更低更闷的确认声
            var rng2 = new SynthRandom(0x5A17C402u);
            SynthUtil.AddFilteredNoise(buffer, ref rng2, 0.07d, 0.010d, 0.32f, 0d, 0.003d,
                NoiseFilterKind.HighPass, 1800d, 2600d, 0.7d);
            SynthUtil.AddDecayingPartials(buffer, 0.07d, 0.12d,
                new[] { 390d, 620d }, new[] { 0.45f, 0.20f }, new[] { 0.030d, 0.034d }, 0.23d);

            return buffer;
        }

        /// <summary>回合开始（0.7 s）：C5-E5-G5-C6 上行琶音。</summary>
        public static AudioBuffer TurnStart(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.70d, sampleRate);
            var adsr = new Adsr(0.006d, 0.05d, 0.40d, 0.22d);
            int[] midi = { 72, 76, 79, 84 };
            double[] starts = { 0d, 0.075d, 0.15d, 0.235d };

            for (int i = 0; i < midi.Length; i++)
            {
                Detune.AddToneStack(buffer, starts[i], 0.45d, MusicTheory.MidiToFrequency(midi[i]),
                    2, 5d, 0.40f, 3, 1.35d, adsr, (uint)(0x7C4E1000u + i), 3d, 0.8d);
            }

            var reverb = new SimpleReverb(sampleRate, 0.60f, 0.50f);
            reverb.ProcessInPlace(buffer, 0.20d);
            return buffer;
        }

        /// <summary>回合结束（0.55 s）：G4→C4 两音下行。</summary>
        public static AudioBuffer TurnEnd(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.55d, sampleRate);
            var adsr = new Adsr(0.012d, 0.06d, 0.35d, 0.24d);

            Detune.AddToneStack(buffer, 0d, 0.35d, MusicTheory.MidiToFrequency(67), 2, 5d, 0.42f,
                3, 1.4d, adsr, 0x7C4E2001u, 3d, 0.8d);
            Detune.AddToneStack(buffer, 0.16d, 0.39d, MusicTheory.MidiToFrequency(60), 2, 5d, 0.38f,
                3, 1.4d, adsr, 0x7C4E2002u, 3d, 0.8d);

            var reverb = new SimpleReverb(sampleRate, 0.60f, 0.50f);
            reverb.ProcessInPlace(buffer, 0.20d);
            return buffer;
        }

        /// <summary>危险提示（0.6 s）：440/660/880 Hz 三级上行方波警笛。</summary>
        public static AudioBuffer DangerWarning(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(0.60d, sampleRate);
            var rng = new SynthRandom(0xDA0E6001u);

            AddBuzzer(buffer, 0d, 0.13d, 440d, 0.42f, 25d);
            AddBuzzer(buffer, 0.20d, 0.13d, 660d, 0.46f, 25d);
            AddBuzzer(buffer, 0.40d, 0.19d, 880d, 0.50f, 28d);

            // 粗糙化噪声（少量，避免纯方波的「玩具感」）
            SynthUtil.AddFilteredNoise(buffer, ref rng, 0d, 0.55d, 0.10f, 0.01d, 0.18d,
                NoiseFilterKind.BandPass, 1800d, 2400d, 1.0d);

            buffer.ApplyFadeOut(0.03d);
            return buffer;
        }

        /// <summary>方波蜂鸣（含 5 Hz 级调幅的粗糙感与 ADSR）。</summary>
        static void AddBuzzer(
            AudioBuffer buffer, double startSeconds, double durationSeconds,
            double frequencyHz, float gain, double tremoloHz)
        {
            int sampleRate = buffer.SampleRate;
            int startFrame = (int)Math.Round(startSeconds * sampleRate);
            int frames = AudioBuffer.FramesForSeconds(durationSeconds, sampleRate);

            var adsr = new Adsr(0.004d, 0.02d, 0.70d, 0.03d);
            double gate = durationSeconds * 0.8d;
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

                double tremolo = 0.80d + 0.20d * Math.Sin(2d * Math.PI * tremoloHz * t);
                double level = adsr.LevelAt(t, gate) * tremolo;
                buffer.Samples[dst] += (float)(Waveforms.Square(phase, 0.5d) * level * gain);
            }
        }
    }
}
