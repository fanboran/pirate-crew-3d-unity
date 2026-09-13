using System;
using PirateCrew.PirateCrew.Audio.Synth;

namespace PirateCrew.PirateCrew.Audio
{
    /// <summary>
    /// 结果短乐句合成：胜利 / 失败（各 3–5 秒，小调式）。
    ///
    /// 【为什么用和弦工具而不是写死频率】和声进行用 <see cref="MusicTheory"/> 的
    /// 级数 → 三和弦 → 琶音链路生成：换一个 rootMidi 就能整体移调、换 ScaleType
    /// 就能换调式色彩，且「和弦性质（大/小/减）」由音阶自动推导，不会手写出错。
    ///
    /// 【调式选择依据】GDD 支柱 4「船歌小调」（F:\VSCode\game-3\docs\gdd.md:113）——
    /// 小调是海盗/航海题材的通用听觉符号：
    ///   · 胜利：A 自然小调 i-VI-III-VII（Am-F-C-G），明亮但有海盗的野味，
    ///     收在 Am 上保留小调身份；
    ///   · 失败：A 和声小调 i-VII-VI-V（Am-G-F-E），V 级用大三和弦是经典悲叹收束，
    ///     旋律整体下行、音区更低、低通更闷。
    ///
    /// 【「不电子味」手段】每层用 2–3 个失谐振荡器叠加 + 缓慢音高抖动
    /// （<see cref="Detune.AddToneStack"/>），并在末尾统一过混响。
    /// 所有音高/时值/力度为 **AI 提案/待定**，需人耳验收。
    /// </summary>
    public static class MusicSfx
    {
        /// <summary>A3 的 MIDI 序号（小调主音，乐句的根音）。</summary>
        const int RootMidi = 57;

        /// <summary>胜利短乐句（4.0 s，Am-F-C-G 后收在 Am）。</summary>
        public static AudioBuffer VictoryJingle(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(4.0d, sampleRate);

            // i - VI - III - VII：Am - F - C - G（自然小调）
            int[] degrees = { 0, 5, 2, 6 };
            int[][] chords = MusicTheory.Progression(RootMidi, ScaleType.NaturalMinor, degrees);
            double chordStep = 0.9d;

            // 旋律：级数 7/9/11/10 → A4 / C5 / E5 / D5
            int[] melodyDegrees = { 7, 9, 11, 10 };

            for (int i = 0; i < chords.Length; i++)
            {
                double start = i * chordStep;
                int[] chord = chords[i];

                // 低音：根音下八度，双层失谐
                var bassEnv = new Adsr(0.010d, 0.15d, 0.70d, 0.20d);
                Detune.AddToneStack(buffer, start, 0.88d, MusicTheory.MidiToFrequency(chord[0] - 12),
                    2, 5d, 0.50f, 4, 1.5d, bassEnv, (uint)(0x1C700000u + i), 4d, 0.5d);

                // 琶音：和弦音循环 pattern，高八度，四个八分音符
                int[] arp = MusicTheory.Arpeggio(chord, new[] { 0, 1, 2, 1 }, 4);
                var arpEnv = new Adsr(0.004d, 0.055d, 0.35d, 0.16d);
                for (int k = 0; k < arp.Length; k++)
                {
                    Detune.AddToneStack(buffer, start + k * 0.22d, 0.30d,
                        MusicTheory.MidiToFrequency(arp[k] + 12), 2, 6d, 0.24f,
                        3, 1.3d, arpEnv, (uint)(0x1C710000u + i * 8 + k), 4d, 0.9d);
                }

                // 旋律层
                var melodyEnv = new Adsr(0.020d, 0.12d, 0.60d, 0.25d);
                Detune.AddToneStack(buffer, start, 0.62d,
                    MusicTheory.MidiToFrequency(MusicTheory.DegreeToMidi(RootMidi, ScaleType.NaturalMinor, melodyDegrees[i])),
                    2, 5d, 0.34f, 4, 1.4d, melodyEnv, (uint)(0x1C720000u + i), 5d, 0.6d);
            }

            // 收束：最后一个和弦之后补一个渐弱的 Am 长音，让乐句落在主和弦上
            var finalEnv = new Adsr(0.020d, 0.18d, 0.55d, 0.30d);
            int[] tonic = MusicTheory.Triad(RootMidi, ChordQuality.Minor);
            for (int i = 0; i < tonic.Length; i++)
            {
                Detune.AddToneStack(buffer, 3.50d, 0.50d, MusicTheory.MidiToFrequency(tonic[i]),
                    2, 6d, 0.26f, 4, 1.45d, finalEnv, (uint)(0x1C730000u + i), 5d, 0.6d);
            }

            var reverb = new SimpleReverb(sampleRate, 0.68f, 0.42f);
            reverb.ProcessInPlace(buffer, 0.30d);
            buffer.ApplyFadeIn(0.006d);
            buffer.ApplyFadeOut(0.05d);
            return buffer;
        }

        /// <summary>失败短乐句（3.6 s，Am-G-F-E 下行收束）。</summary>
        public static AudioBuffer DefeatJingle(int sampleRate)
        {
            AudioBuffer buffer = SynthUtil.Create(3.6d, sampleRate);

            // i - VII - VI - V：Am - G - F - E
            // 前三个和弦取自然小调（VII 是下主音 G 大三和弦，听感是「下沉」而不是导音紧张），
            // 最后一个 V 级刻意改用和声小调，得到 E 大三和弦的经典悲叹收束。
            int[][] chords =
            {
                MusicTheory.TriadOnDegree(RootMidi, ScaleType.NaturalMinor, 0),
                MusicTheory.TriadOnDegree(RootMidi, ScaleType.NaturalMinor, 6),
                MusicTheory.TriadOnDegree(RootMidi, ScaleType.NaturalMinor, 5),
                MusicTheory.TriadOnDegree(RootMidi, ScaleType.HarmonicMinor, 4),
            };
            double chordStep = 0.9d;

            // 下行旋律：E4 / D4 / C4 / B3
            int[] melodyMidi = { 64, 62, 60, 59 };

            for (int i = 0; i < chords.Length; i++)
            {
                double start = i * chordStep;
                int[] chord = chords[i];

                // 弦垫：和弦三音各一层失谐，慢起慢收
                var padEnv = new Adsr(0.080d, 0.20d, 0.75d, 0.30d);
                for (int k = 0; k < chord.Length; k++)
                {
                    Detune.AddToneStack(buffer, start, 0.85d, MusicTheory.MidiToFrequency(chord[k]),
                        3, 9d, 0.16f, 5, 1.6d, padEnv, (uint)(0x2EA70000u + i * 8 + k), 5d, 0.4d);
                }

                // 低音
                var bassEnv = new Adsr(0.030d, 0.20d, 0.65d, 0.28d);
                Detune.AddToneStack(buffer, start, 0.86d, MusicTheory.MidiToFrequency(chord[0] - 12),
                    2, 6d, 0.48f, 4, 1.5d, bassEnv, (uint)(0x2EA71000u + i), 5d, 0.4d);

                // 下行旋律
                var melodyEnv = new Adsr(0.050d, 0.15d, 0.60d, 0.30d);
                Detune.AddToneStack(buffer, start + 0.05d, 0.70d, MusicTheory.MidiToFrequency(melodyMidi[i]),
                    2, 5d, 0.32f, 3, 1.5d, melodyEnv, (uint)(0x2EA72000u + i), 5d, 0.5d);
            }

            // 整体压暗：悲伤感来自高频缺失
            SynthUtil.ShapeSpectrum(buffer, NoiseFilterKind.LowPass, 2200d, 700d, 0.9d);
            var reverb = new SimpleReverb(sampleRate, 0.70f, 0.50f);
            reverb.ProcessInPlace(buffer, 0.32d);
            buffer.ApplyFadeIn(0.008d);
            buffer.ApplyFadeOut(0.06d);
            return buffer;
        }
    }
}
