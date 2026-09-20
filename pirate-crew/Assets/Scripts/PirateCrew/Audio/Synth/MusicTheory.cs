using System;

namespace PirateCrew.Audio.Synth
{
    /// <summary>和弦性质（三和弦）。</summary>
    public enum ChordQuality
    {
        /// <summary>小三和弦（0,3,7）。</summary>
        Minor = 0,

        /// <summary>大三和弦（0,4,7）。</summary>
        Major = 1,

        /// <summary>减三和弦（0,3,6）。</summary>
        Diminished = 2,
    }

    /// <summary>音阶类型（用于按级数取和弦/旋律）。</summary>
    public enum ScaleType
    {
        /// <summary>自然小调：0,2,3,5,7,8,10（海盗味主调，GDD §1.4 支柱 4「海盗味」）。</summary>
        NaturalMinor = 0,

        /// <summary>多利亚：0,2,3,5,7,9,10（比自然小调明亮一点，适合胜利乐句）。</summary>
        Dorian = 1,

        /// <summary>和声小调：0,2,3,5,7,8,11（含大三度属和弦，失败乐句的收束用）。</summary>
        HarmonicMinor = 2,

        /// <summary>大调：0,2,4,5,7,9,11。</summary>
        Major = 3,
    }

    /// <summary>
    /// 乐理工具（纯函数）：MIDI 音高 ↔ 频率换算、音阶、三和弦、和弦进行与琶音序列生成。
    ///
    /// 【为什么自己算而不写死频率表】结果乐句（胜利/失败）要用「和弦 + 琶音」生成，
    /// 写死频率表无法移调、也读不出乐理结构。MIDI 序号是最紧凑的音高表示：
    /// 频率 = 440 × 2^((midi-69)/12)，十二平均律，任何音高都有确定频率。
    ///
    /// 【出处】十二平均律换算与音阶半音集为通用乐理常识；
    /// 具体调性/进行的选取（A 小调、i-VI-III-VII 等）为 **AI 提案/待定**，
    /// 依据是 GDD 支柱 4「船歌小调」（F:\VSCode\game-3\docs\gdd.md:113）——
    /// 小调式是船歌/海盗题材的通用听觉符号。
    /// </summary>
    public static class MusicTheory
    {
        /// <summary>A4 标准音高（Hz）。</summary>
        public const double A4Frequency = 440d;

        /// <summary>A4 的 MIDI 序号。</summary>
        public const int A4Midi = 69;

        /// <summary>半音数（一个八度）。</summary>
        public const int SemitonesPerOctave = 12;

        /// <summary>MIDI 序号 → 频率（Hz）。</summary>
        public static double MidiToFrequency(double midiNote)
        {
            return A4Frequency * Math.Pow(2d, (midiNote - A4Midi) / SemitonesPerOctave);
        }

        /// <summary>频率 → 最近的 MIDI 序号（可为零点几的浮点，测试/校验用）。</summary>
        public static double FrequencyToMidi(double frequencyHz)
        {
            if (frequencyHz <= 0d)
                return 0d;
            return A4Midi + SemitonesPerOctave * Math.Log(frequencyHz / A4Frequency, 2d);
        }

        /// <summary>音阶的半音集合（相对于主音）。</summary>
        public static int[] ScaleSemitones(ScaleType scale)
        {
            switch (scale)
            {
                case ScaleType.Dorian:
                    return new[] { 0, 2, 3, 5, 7, 9, 10 };
                case ScaleType.HarmonicMinor:
                    return new[] { 0, 2, 3, 5, 7, 8, 11 };
                case ScaleType.Major:
                    return new[] { 0, 2, 4, 5, 7, 9, 11 };
                default:
                    return new[] { 0, 2, 3, 5, 7, 8, 10 };
            }
        }

        /// <summary>
        /// 音阶级数 → MIDI 序号。<paramref name="degree"/> 可超出 0..6
        /// （负数向下、≥7 向上跨八度），便于写旋律时自然地上下行。
        /// </summary>
        public static int DegreeToMidi(int rootMidi, ScaleType scale, int degree)
        {
            int[] semis = ScaleSemitones(scale);
            int octave = (int)Math.Floor(degree / (double)semis.Length);
            int index = degree - octave * semis.Length;
            return rootMidi + octave * SemitonesPerOctave + semis[index];
        }

        /// <summary>
        /// 某音级上三和弦的性质（大/小/减）。
        ///
        /// 【判定必须看五度，不能只看三度】自然小调的 ii 级（B-D-F）根到三是小三度，
        /// 但五度是减五度，所以它是减三和弦而不是小三和弦——只看三度会误判。
        /// 因此先判五度（6 半音 = 减），再按三度定大/小。
        /// </summary>
        public static ChordQuality QualityFor(ScaleType scale, int degree)
        {
            int[] semis = ScaleSemitones(scale);
            int size = semis.Length;
            int idx = ((degree % size) + size) % size;

            int root = semis[idx];

            int thirdIdx = idx + 2;
            int third = semis[thirdIdx % size] + (thirdIdx >= size ? SemitonesPerOctave : 0);

            int fifthIdx = idx + 4;
            int fifth = semis[fifthIdx % size] + (fifthIdx >= size ? SemitonesPerOctave : 0);

            int thirdInterval = third - root;
            int fifthInterval = fifth - root;

            if (fifthInterval == 6)
                return ChordQuality.Diminished;
            if (thirdInterval == 4)
                return ChordQuality.Major;
            if (thirdInterval == 3)
                return ChordQuality.Minor;

            // 兜底（音阶含增四度等非常规集合时）：按五度性质归类
            return fifthInterval >= 7 ? ChordQuality.Major : ChordQuality.Diminished;
        }

        /// <summary>由根音 MIDI 与性质构成三和弦（含根、三、五，升序）。</summary>
        public static int[] Triad(int rootMidi, ChordQuality quality)
        {
            switch (quality)
            {
                case ChordQuality.Major:
                    return new[] { rootMidi, rootMidi + 4, rootMidi + 7 };
                case ChordQuality.Diminished:
                    return new[] { rootMidi, rootMidi + 3, rootMidi + 6 };
                default:
                    return new[] { rootMidi, rootMidi + 3, rootMidi + 7 };
            }
        }

        /// <summary>按音阶级数取三和弦（自动选大/小/减）。</summary>
        public static int[] TriadOnDegree(int rootMidi, ScaleType scale, int degree)
        {
            return Triad(DegreeToMidi(rootMidi, scale, degree), QualityFor(scale, degree));
        }

        /// <summary>和弦音列表 → 琶音序列（按 <paramref name="pattern"/> 的和弦内索引循环取音）。</summary>
        public static int[] Arpeggio(int[] chordTones, int[] pattern, int noteCount)
        {
            if (chordTones == null || chordTones.Length == 0 || noteCount <= 0)
                return new int[0];

            if (pattern == null || pattern.Length == 0)
                pattern = new[] { 0, 1, 2, 1 };

            var notes = new int[noteCount];
            for (int i = 0; i < noteCount; i++)
            {
                int chordIndex = pattern[i % pattern.Length];
                // 越界的和弦索引向上/向下跨八度，而不是钳制（否则琶音会卡在最高音）
                int octaveShift = FloorDiv(chordIndex, chordTones.Length);
                int local = chordIndex - octaveShift * chordTones.Length;
                notes[i] = chordTones[local] + octaveShift * SemitonesPerOctave;
            }

            return notes;
        }

        /// <summary>
        /// 按级数生成和弦序列。<paramref name="degrees"/> = 每小节的和弦级数
        /// （0 = i 级，5 = VI 级……），返回每小节的和弦音数组。
        /// </summary>
        public static int[][] Progression(int rootMidi, ScaleType scale, int[] degrees)
        {
            if (degrees == null || degrees.Length == 0)
                return new int[0][];

            var chords = new int[degrees.Length][];
            for (int i = 0; i < degrees.Length; i++)
                chords[i] = TriadOnDegree(rootMidi, scale, degrees[i]);
            return chords;
        }

        /// <summary>把一组 MIDI 音整体移调（半音数，可负）。</summary>
        public static int[] Transpose(int[] midiNotes, int semitones)
        {
            if (midiNotes == null)
                return new int[0];

            var result = new int[midiNotes.Length];
            for (int i = 0; i < midiNotes.Length; i++)
                result[i] = midiNotes[i] + semitones;
            return result;
        }

        static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0)))
                q--;
            return q;
        }
    }
}
