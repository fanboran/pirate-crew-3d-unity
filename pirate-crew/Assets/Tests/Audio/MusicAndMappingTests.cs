using System;
using NUnit.Framework;
using PirateCrew.Audio;
using PirateCrew.Audio.Synth;
using PirateCrew.Combat;
using PirateCrew.Data;

namespace PirateCrew.Tests.Audio
{
    /// <summary>
    /// 乐理工具、失谐工具、事件映射、WAV 编解码的纯逻辑测试。
    /// </summary>
    public class MusicAndMappingTests
    {
        // ------------------------------------------------------------------
        // MusicTheory
        // ------------------------------------------------------------------

        [Test]
        public void MusicTheory_MidiToFrequency_UsesEqualTemperament()
        {
            Assert.That(MusicTheory.MidiToFrequency(69), Is.EqualTo(440d).Within(1e-9), "A4 = 440 Hz");
            Assert.That(MusicTheory.MidiToFrequency(81), Is.EqualTo(880d).Within(1e-6), "升八度频率翻倍");
            Assert.That(MusicTheory.MidiToFrequency(57), Is.EqualTo(220d).Within(1e-6), "A3 = 220 Hz");
            Assert.That(MusicTheory.MidiToFrequency(60), Is.EqualTo(261.6256d).Within(1e-3), "C4 ≈ 261.63 Hz");
        }

        [Test]
        public void MusicTheory_FrequencyToMidi_RoundTrips()
        {
            for (int midi = 40; midi <= 90; midi++)
            {
                double frequency = MusicTheory.MidiToFrequency(midi);
                Assert.That(MusicTheory.FrequencyToMidi(frequency), Is.EqualTo(midi).Within(1e-6));
            }
        }

        [Test]
        public void MusicTheory_ScaleSemitones_AreCorrect()
        {
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 5, 7, 8, 10 }, MusicTheory.ScaleSemitones(ScaleType.NaturalMinor));
            CollectionAssert.AreEqual(new[] { 0, 2, 4, 5, 7, 9, 11 }, MusicTheory.ScaleSemitones(ScaleType.Major));
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 5, 7, 8, 11 }, MusicTheory.ScaleSemitones(ScaleType.HarmonicMinor));
            CollectionAssert.AreEqual(new[] { 0, 2, 3, 5, 7, 9, 10 }, MusicTheory.ScaleSemitones(ScaleType.Dorian));
        }

        [Test]
        public void MusicTheory_DegreeToMidi_HandlesOctaveWrapAndNegatives()
        {
            Assert.That(MusicTheory.DegreeToMidi(57, ScaleType.NaturalMinor, 0), Is.EqualTo(57));
            Assert.That(MusicTheory.DegreeToMidi(57, ScaleType.NaturalMinor, 3), Is.EqualTo(62));
            Assert.That(MusicTheory.DegreeToMidi(57, ScaleType.NaturalMinor, 7), Is.EqualTo(69), "第 7 级 = 高八度主音");
            Assert.That(MusicTheory.DegreeToMidi(57, ScaleType.NaturalMinor, 9), Is.EqualTo(72), "第 9 级 = 小三度上行");
            Assert.That(MusicTheory.DegreeToMidi(57, ScaleType.NaturalMinor, -1), Is.EqualTo(55), "负级数向下跨八度");
        }

        [Test]
        public void MusicTheory_QualityFor_MatchesNaturalMinorDiatonicTriads()
        {
            Assert.That(MusicTheory.QualityFor(ScaleType.NaturalMinor, 0), Is.EqualTo(ChordQuality.Minor), "i 级");
            Assert.That(MusicTheory.QualityFor(ScaleType.NaturalMinor, 1), Is.EqualTo(ChordQuality.Diminished), "ii° 级");
            Assert.That(MusicTheory.QualityFor(ScaleType.NaturalMinor, 2), Is.EqualTo(ChordQuality.Major), "III 级");
            Assert.That(MusicTheory.QualityFor(ScaleType.NaturalMinor, 4), Is.EqualTo(ChordQuality.Minor), "v 级");
            Assert.That(MusicTheory.QualityFor(ScaleType.NaturalMinor, 5), Is.EqualTo(ChordQuality.Major), "VI 级");
            Assert.That(MusicTheory.QualityFor(ScaleType.NaturalMinor, 6), Is.EqualTo(ChordQuality.Major), "VII 级");
        }

        [Test]
        public void MusicTheory_HarmonicMinor_MakesDominantMajor()
        {
            Assert.That(MusicTheory.QualityFor(ScaleType.HarmonicMinor, 4), Is.EqualTo(ChordQuality.Major),
                "和声小调的 V 级必须是大三和弦（悲叹收束）");
        }

        [Test]
        public void MusicTheory_Triad_Intervals()
        {
            CollectionAssert.AreEqual(new[] { 60, 63, 67 }, MusicTheory.Triad(60, ChordQuality.Minor));
            CollectionAssert.AreEqual(new[] { 60, 64, 67 }, MusicTheory.Triad(60, ChordQuality.Major));
            CollectionAssert.AreEqual(new[] { 60, 63, 66 }, MusicTheory.Triad(60, ChordQuality.Diminished));
        }

        [Test]
        public void MusicTheory_Arpeggio_PatternWrapsOctave()
        {
            int[] chord = { 60, 64, 67 };
            int[] notes = MusicTheory.Arpeggio(chord, new[] { 0, 1, 2, 3 }, 4);
            CollectionAssert.AreEqual(new[] { 60, 64, 67, 72 }, notes);
        }

        [Test]
        public void MusicTheory_Arpeggio_EmptyInputsAreSafe()
        {
            Assert.That(MusicTheory.Arpeggio(null, null, 5).Length, Is.EqualTo(0));
            Assert.That(MusicTheory.Arpeggio(new int[0], new[] { 0 }, 5).Length, Is.EqualTo(0));
            Assert.That(MusicTheory.Arpeggio(new[] { 60 }, null, 0).Length, Is.EqualTo(0));
        }

        [Test]
        public void MusicTheory_Progression_UsesDiatonicQualities()
        {
            // i - VI - III - VII（A 自然小调）：Am - F - C - G
            int[][] chords = MusicTheory.Progression(57, ScaleType.NaturalMinor, new[] { 0, 5, 2, 6 });
            Assert.That(chords.Length, Is.EqualTo(4));
            CollectionAssert.AreEqual(new[] { 57, 60, 64 }, chords[0], "Am");
            CollectionAssert.AreEqual(new[] { 65, 69, 72 }, chords[1], "F");
            CollectionAssert.AreEqual(new[] { 60, 64, 67 }, chords[2], "C");
            CollectionAssert.AreEqual(new[] { 67, 71, 74 }, chords[3], "G");
        }

        [Test]
        public void MusicTheory_Transpose_ShiftsAllNotes()
        {
            CollectionAssert.AreEqual(new[] { 62, 65, 69 }, MusicTheory.Transpose(new[] { 60, 63, 67 }, 2));
            CollectionAssert.AreEqual(new int[0], MusicTheory.Transpose(null, 3));
        }

        // ------------------------------------------------------------------
        // Detune
        // ------------------------------------------------------------------

        [Test]
        public void Detune_CentsAndSemitoneRatios()
        {
            Assert.That(Detune.CentsToRatio(1200d), Is.EqualTo(2d).Within(1e-9));
            Assert.That(Detune.CentsToRatio(0d), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Detune.SemitonesToRatio(12d), Is.EqualTo(2d).Within(1e-9));
        }

        [Test]
        public void Detune_JitterCents_StaysWithinReasonableBounds()
        {
            const double amount = 40d;
            for (int i = 0; i < 400; i++)
            {
                double t = i * 0.017d;
                double cents = Detune.JitterCents(t, amount, 0.7d, 42);
                Assert.That(Math.Abs(cents), Is.LessThanOrEqualTo(amount * 1.3d));
            }
        }

        [Test]
        public void Detune_AddToneStack_WritesAudibleFiniteSignal()
        {
            var buffer = new AudioBuffer(4410, AudioBuffer.DefaultSampleRate, AudioBuffer.Mono);
            var adsr = new Adsr(0.01d, 0.05d, 0.4d, 0.1d);
            Detune.AddToneStack(buffer, 0d, 0.1d, 440d, 3, 8d, 0.5f, 3, 1.4d, adsr, 7u, 4d, 0.7d);

            Assert.That(buffer.Rms(), Is.GreaterThan(0.01f), "三层失谐叠加应有可测能量");
            float[] samples = buffer.Samples;
            for (int i = 0; i < samples.Length; i++)
            {
                Assert.That(float.IsNaN(samples[i]) || float.IsInfinity(samples[i]), Is.False);
                Assert.That(Math.Abs(samples[i]), Is.LessThan(1.5f), "三层叠加不应失控");
            }
        }

        [Test]
        public void Detune_Tremolo_ModulatesAmplitude()
        {
            var buffer = new AudioBuffer(4410, AudioBuffer.DefaultSampleRate, AudioBuffer.Mono);
            for (int i = 0; i < buffer.Samples.Length; i++)
                buffer.Samples[i] = 1f;

            Detune.ApplyTremolo(buffer, 5d, 1d);

            float min = 2f;
            float max = -2f;
            for (int i = 0; i < buffer.Samples.Length; i++)
            {
                min = Math.Min(min, buffer.Samples[i]);
                max = Math.Max(max, buffer.Samples[i]);
            }

            Assert.That(max, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(min, Is.LessThan(0.05f), "depth=1 时振幅应周期性到 0");
        }

        [Test]
        public void Detune_Breath_StaysInUnitRange()
        {
            for (int i = 0; i < 500; i++)
            {
                double value = Detune.Breath(i * 0.05d, 0.125d, 0.6d, 3);
                Assert.That(value, Is.InRange(0d, 1d));
            }
        }

        // ------------------------------------------------------------------
        // AudioEventMapper
        // ------------------------------------------------------------------

        [Test]
        public void Mapper_EveryWeaponMapsToARegisteredSfx()
        {
            foreach (WeaponId weapon in Enum.GetValues(typeof(WeaponId)))
            {
                SfxId id = AudioEventMapper.SfxForDetonation(weapon);
                Assert.That(SfxCatalog.Get(id).DurationSeconds, Is.GreaterThan(0d),
                    weapon + " 映射到了未登记的 SfxId");
            }
        }

        [Test]
        public void Mapper_WeaponSpecificMappings()
        {
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.WoodenCrate), Is.EqualTo(SfxId.WoodCrack));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.GunpowderBarrel), Is.EqualTo(SfxId.WoodCrack));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.Banana), Is.EqualTo(SfxId.Bounce));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.PiecesOfEight), Is.EqualTo(SfxId.Bounce));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.Seagull), Is.EqualTo(SfxId.WaterSplash));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.TidalWave), Is.EqualTo(SfxId.WaterSplash));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.Anchor), Is.EqualTo(SfxId.FleshHit));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.Cannonball), Is.EqualTo(SfxId.Explosion));
            Assert.That(AudioEventMapper.SfxForDetonation(WeaponId.Cannon), Is.EqualTo(SfxId.Explosion));
        }

        [Test]
        public void Mapper_OutcomeToMusicCue()
        {
            // 新口径：返回值 = 是否放乐句，out = 乐句（与 BattleHud.SettlementJingleFor 同一契约）。
            Assert.That(AudioEventMapper.MusicForMatchOutcome(MatchOutcome.Team0Win, true, out SfxId victory),
                Is.True);
            Assert.That(victory, Is.EqualTo(SfxId.VictoryJingle));

            Assert.That(AudioEventMapper.MusicForMatchOutcome(MatchOutcome.LevelFailed, true, out SfxId failed),
                Is.True);
            Assert.That(failed, Is.EqualTo(SfxId.DefeatJingle));

            Assert.That(AudioEventMapper.MusicForMatchOutcome(MatchOutcome.Draw, true, out SfxId draw),
                Is.True);
            Assert.That(draw, Is.EqualTo(SfxId.DefeatJingle));

            Assert.That(AudioEventMapper.MusicForMatchOutcome(MatchOutcome.Team1Win, true, out SfxId aiWin),
                Is.True, "1P 模式 AI 胜 = 玩家失败");
            Assert.That(aiWin, Is.EqualTo(SfxId.DefeatJingle));

            Assert.That(AudioEventMapper.MusicForMatchOutcome(MatchOutcome.Team1Win, false, out _),
                Is.False, "2P 热座蓝队获胜时不应误放胜利/失败乐句");
        }

        [Test]
        public void Mapper_SeagullVariantSelectsAllThree()
        {
            Assert.That(AudioEventMapper.SeagullVariantForRoll(0f), Is.EqualTo(SfxId.SeagullCry1));
            Assert.That(AudioEventMapper.SeagullVariantForRoll(0.33f), Is.EqualTo(SfxId.SeagullCry1));
            Assert.That(AudioEventMapper.SeagullVariantForRoll(0.34f), Is.EqualTo(SfxId.SeagullCry2));
            Assert.That(AudioEventMapper.SeagullVariantForRoll(0.66f), Is.EqualTo(SfxId.SeagullCry2));
            Assert.That(AudioEventMapper.SeagullVariantForRoll(0.67f), Is.EqualTo(SfxId.SeagullCry3));
            Assert.That(AudioEventMapper.SeagullVariantForRoll(1f), Is.EqualTo(SfxId.SeagullCry3));
            Assert.That(AudioEventMapper.SeagullVariantForRoll(-5f), Is.EqualTo(SfxId.SeagullCry1));
        }

        [Test]
        public void Mapper_WithinAudibleRange_UsesRecipeMaxDistance()
        {
            Assert.That(AudioEventMapper.WithinAudibleRange(WeaponId.Cannonball, 10f), Is.True);
            Assert.That(AudioEventMapper.WithinAudibleRange(WeaponId.Cannonball, 5000f), Is.False);
        }

        // ------------------------------------------------------------------
        // WavCodec
        // ------------------------------------------------------------------

        [Test]
        public void WavCodec_HeaderIsWellFormed()
        {
            var buffer = new AudioBuffer(1000, 44100, 1);
            byte[] wav = WavCodec.EncodePcm16(buffer);

            Assert.That(wav.Length, Is.EqualTo(WavCodec.HeaderBytes + 2000));
            Assert.That(WavCodec.TryReadHeader(wav, out int sampleRate, out int channels,
                out int bitsPerSample, out int dataBytes), Is.True);

            Assert.That(sampleRate, Is.EqualTo(44100));
            Assert.That(channels, Is.EqualTo(1));
            Assert.That(bitsPerSample, Is.EqualTo(16));
            Assert.That(dataBytes, Is.EqualTo(2000));
        }

        [Test]
        public void WavCodec_RoundTripsSampleValues()
        {
            var buffer = new AudioBuffer(5, 44100, 1);
            buffer.Samples[0] = 0f;
            buffer.Samples[1] = 0.5f;
            buffer.Samples[2] = -0.5f;
            buffer.Samples[3] = 1f;
            buffer.Samples[4] = -1f;

            byte[] wav = WavCodec.EncodePcm16(buffer);

            Assert.That(WavCodec.ReadPcm16Sample(wav, 0), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(WavCodec.ReadPcm16Sample(wav, 1), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(WavCodec.ReadPcm16Sample(wav, 2), Is.EqualTo(-0.5f).Within(1e-3f));
            Assert.That(WavCodec.ReadPcm16Sample(wav, 3), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(WavCodec.ReadPcm16Sample(wav, 4), Is.EqualTo(-1f).Within(1e-4f));
        }

        [Test]
        public void WavCodec_ClampsOutOfRangeAndGain()
        {
            var loud = new AudioBuffer(2, 44100, 1);
            loud.Samples[0] = 5f;
            loud.Samples[1] = -5f;

            byte[] wav = WavCodec.EncodePcm16(loud);
            Assert.That(WavCodec.ReadPcm16Sample(wav, 0), Is.EqualTo(1f).Within(1e-4f), "超过 1.0 应钳制");
            Assert.That(WavCodec.ReadPcm16Sample(wav, 1), Is.EqualTo(-1f).Within(1e-4f));

            var unit = new AudioBuffer(2, 44100, 1);
            unit.Samples[0] = 1f;
            unit.Samples[1] = -1f;

            byte[] half = WavCodec.EncodePcm16(unit, 0.5f);
            Assert.That(WavCodec.ReadPcm16Sample(half, 0), Is.EqualTo(0.5f).Within(1e-3f), "gain 应在钳制前缩放");
            Assert.That(WavCodec.ReadPcm16Sample(half, 1), Is.EqualTo(-0.5f).Within(1e-3f));
        }

        [Test]
        public void WavCodec_RejectsMalformedData()
        {
            Assert.That(WavCodec.TryReadHeader(null, out _, out _, out _, out _), Is.False);
            Assert.That(WavCodec.TryReadHeader(new byte[10], out _, out _, out _, out _), Is.False);
            Assert.That(WavCodec.TryReadHeader(new byte[64], out _, out _, out _, out _), Is.False,
                "全零缓冲不是合法 RIFF 头");
        }

        [Test]
        public void WavCodec_EncodesEveryCatalogEntryAtDeclaredLength()
        {
            // 抽查两条（不遍历全表，避免与 CatalogRenderTests 的渲染重复）
            foreach (SfxId id in new[] { SfxId.Explosion, SfxId.WavesLoop })
            {
                AudioBuffer buffer = SynthRenderer.Render(id);
                byte[] wav = WavCodec.EncodePcm16(buffer);

                Assert.That(WavCodec.TryReadHeader(wav, out int sampleRate, out int channels,
                    out int bitsPerSample, out int dataBytes), Is.True, id + " wav 头非法");
                Assert.That(sampleRate, Is.EqualTo(44100));
                Assert.That(channels, Is.EqualTo(1));
                Assert.That(bitsPerSample, Is.EqualTo(16));
                Assert.That(dataBytes, Is.EqualTo(buffer.FrameCount * 2));
                Assert.That(wav.Length, Is.EqualTo(WavCodec.HeaderBytes + dataBytes));
            }
        }
    }
}
