using System;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Audio;
using PirateCrew.Audio.Synth;

namespace PirateCrew.Tests.Audio
{
    /// <summary>
    /// 音效配方与渲染一致性测试（纯 C#）。
    ///
    /// 断言的是「配方声明的」与「实际渲染出的」逐条吻合：时长、采样率、声道、
    /// 峰值不削波、非循环音首尾淡出、循环音拼接处不跳变、渲染确定性。
    /// 渲染结果按 id 缓存在类内，避免同一音效重复合成拖慢测试。
    /// </summary>
    public class CatalogRenderTests
    {
        static readonly Dictionary<SfxId, AudioBuffer> Cache = new Dictionary<SfxId, AudioBuffer>();

        static AudioBuffer Rendered(SfxId id)
        {
            if (!Cache.TryGetValue(id, out AudioBuffer buffer))
            {
                buffer = SynthRenderer.Render(id);
                Cache[id] = buffer;
            }

            return buffer;
        }

        static IEnumerable<SfxId> AllIds()
        {
            // 只遍历「有程序化合成实现」的 id：纯外部搬运素材（如环境底床垫底 BedPad）
            // 本来就没有合成配方，对它做渲染断言等于把「没有合成」误判成「渲染失败」。
            // 判据见 SynthRenderer.CanRender / Game2AudioAssets。
            return SynthRenderer.RenderableIds();
        }

        // ------------------------------------------------------------------
        // 配方表自身完整性
        // ------------------------------------------------------------------

        [Test]
        public void Catalog_EveryEnumValueIsRegistered()
        {
            int max = 0;
            SfxRecipe[] all = SfxCatalog.All;
            for (int i = 0; i < all.Length; i++)
                max = Math.Max(max, (int)all[i].Id);

            Assert.That(all.Length, Is.EqualTo(max + 1), "SfxId 枚举有值未登记进 SfxCatalog");

            for (int i = 0; i <= max; i++)
            {
                SfxRecipe recipe = SfxCatalog.Get((SfxId)i);
                Assert.That(recipe.DurationSeconds, Is.GreaterThan(0d), recipe.Id + " 未登记或时长为 0");
                Assert.That(recipe.Recipe, Is.Not.EqualTo("(未登记)"));
            }
        }

        [Test]
        public void Catalog_NonRenderableIdsAreExactlyTheExternalOnlyOnes()
        {
            // 「不可合成」与「搬运来的纯外部素材」必须一一对应：
            // 不可合成却又不在搬运表里 = 漏登记（运行时会静默无声）。
            var nonRenderable = new List<SfxId>();
            SfxRecipe[] all = SfxCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (!SynthRenderer.CanRender(all[i].Id))
                    nonRenderable.Add(all[i].Id);
            }

            CollectionAssert.AreEquivalent(new[] { SfxId.BedPad }, nonRenderable);
            for (int i = 0; i < nonRenderable.Count; i++)
            {
                Assert.That(Game2AudioAssets.IsPorted(nonRenderable[i]), Is.True,
                    nonRenderable[i] + " 不可合成却又不在 Game-2 搬运表里（运行时会静默无声）");
            }
        }

        [Test]
        public void Catalog_MetadataIsCompleteAndValid()
        {
            SfxRecipe[] all = SfxCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                SfxRecipe recipe = all[i];
                Assert.That(recipe.Trigger, Is.Not.Null.And.Not.Empty, recipe.Id + " 缺触发来源");
                Assert.That(recipe.Recipe, Is.Not.Null.And.Not.Empty, recipe.Id + " 缺配方说明");
                Assert.That(recipe.DefaultVolume, Is.InRange(0f, 1f), recipe.Id + " 默认音量越界");

                if (recipe.Spatial == SpatialMode.ThreeD)
                {
                    Assert.That(recipe.MinDistance, Is.GreaterThan(0f), recipe.Id + " 3D 音必须有最小距离");
                    Assert.That(recipe.MaxDistance, Is.GreaterThan(recipe.MinDistance), recipe.Id + " 最大距离必须大于最小距离");
                }
            }
        }

        [Test]
        public void Catalog_AssetFileNamesAreUniqueAndPrefixed()
        {
            var seen = new HashSet<string>();
            SfxRecipe[] all = SfxCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                string name = SfxCatalog.AssetFileName(all[i].Id);
                Assert.That(seen.Add(name), Is.True, "资产文件名重复: " + name);
                Assert.That(name, Does.StartWith(SfxCatalog.CategoryPrefix(all[i].Category)));
                Assert.That(SfxCatalog.CategoryFolder(all[i].Category), Does.StartWith("Assets/Resources/PirateCrewAudio"));
            }
        }

        // ------------------------------------------------------------------
        // 渲染一致性
        // ------------------------------------------------------------------

        [Test]
        public void Render_UsesDeclaredSampleRateAndMonoChannels()
        {
            foreach (SfxId id in AllIds())
            {
                AudioBuffer buffer = Rendered(id);
                Assert.That(buffer.SampleRate, Is.EqualTo(SfxCatalog.SampleRate), id + " 采样率不一致");
                Assert.That(buffer.Channels, Is.EqualTo(SfxCatalog.Channels), id + " 声道数不一致（3D 音必须单声道）");
            }
        }

        [Test]
        public void Render_DurationMatchesRecipe()
        {
            foreach (SfxId id in AllIds())
            {
                SfxRecipe recipe = SfxCatalog.Get(id);
                AudioBuffer buffer = Rendered(id);
                double tolerance = 1.5d / SfxCatalog.SampleRate;
                Assert.That(buffer.Duration, Is.EqualTo(recipe.DurationSeconds).Within(tolerance),
                    id + " 渲染时长与配方声明不一致");
            }
        }

        [Test]
        public void Render_PeakIsNormalizedAndDoesNotClip()
        {
            foreach (SfxId id in AllIds())
            {
                float peak = Rendered(id).Peak();
                Assert.That(peak, Is.LessThanOrEqualTo(1f), id + " 削波");
                Assert.That(peak, Is.EqualTo(SfxCatalog.PeakTarget).Within(0.02f), id + " 峰值未归一化到目标");
            }
        }

        [Test]
        public void Render_AllSamplesAreFinite()
        {
            foreach (SfxId id in AllIds())
            {
                float[] samples = Rendered(id).Samples;
                for (int i = 0; i < samples.Length; i++)
                {
                    if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]))
                        Assert.Fail(id + " 第 " + i + " 个采样非有限值");
                }
            }
        }

        [Test]
        public void Render_NonLoopClipsFadeInAndOutToZero()
        {
            foreach (SfxId id in AllIds())
            {
                SfxRecipe recipe = SfxCatalog.Get(id);
                if (recipe.Loop)
                    continue;

                AudioBuffer buffer = Rendered(id);
                Assert.That(Math.Abs(buffer.Samples[0]), Is.LessThan(1e-6f), id + " 首帧未淡入到 0（会有爆音）");
                Assert.That(Math.Abs(buffer.Samples[buffer.Samples.Length - 1]), Is.LessThan(1e-6f),
                    id + " 末帧未淡出到 0（会有爆音）");
            }
        }

        [Test]
        public void Render_LoopSeamIsNotAnOutlierJump()
        {
            foreach (SfxId id in AllIds())
            {
                SfxRecipe recipe = SfxCatalog.Get(id);
                if (!recipe.Loop)
                    continue;

                AudioBuffer buffer = Rendered(id);
                float[] s = buffer.Samples;

                double sumDelta = 0d;
                for (int i = 1; i < s.Length; i++)
                    sumDelta += Math.Abs(s[i] - s[i - 1]);

                double meanDelta = sumDelta / (s.Length - 1);
                double seamDelta = Math.Abs(s[0] - s[s.Length - 1]);

                Assert.That(seamDelta, Is.LessThan(3d * meanDelta + 0.02d),
                    id + " 循环拼接处的跳变是孤立异常（会听到咔哒声）");
            }
        }

        [Test]
        public void Render_IsDeterministic()
        {
            AudioBuffer first = SynthRenderer.Render(SfxId.Explosion);
            AudioBuffer second = SynthRenderer.Render(SfxId.Explosion);

            Assert.That(second.Samples.Length, Is.EqualTo(first.Samples.Length));
            for (int i = 0; i < first.Samples.Length; i++)
            {
                if (!first.Samples[i].Equals(second.Samples[i]))
                    Assert.Fail("同一配方两次渲染结果不同（噪声源不确定）@ " + i);
            }
        }

        [Test]
        public void Render_CombatSfxAreAudible()
        {
            SfxId[] combat =
            {
                SfxId.Explosion, SfxId.WoodCrack, SfxId.FleshHit, SfxId.WaterSplash,
                SfxId.ThrowWhoosh, SfxId.Bounce, SfxId.StoneRoll, SfxId.MineBeep, SfxId.CrewDown,
            };

            foreach (SfxId id in combat)
            {
                float rms = Rendered(id).Rms();
                Assert.That(rms, Is.GreaterThan(0.005f), id + " 能量过低，几乎听不到");
            }
        }

        [Test]
        public void Render_AmbientLoopsHaveLowCrestFactor()
        {
            // 环境音是持续底噪：峰值应接近其有效值（不应有大起大落的瞬态）
            foreach (SfxId id in new[] { SfxId.WavesLoop, SfxId.WindLoop })
            {
                AudioBuffer buffer = Rendered(id);
                float rms = buffer.Rms();
                float peak = buffer.Peak();
                Assert.That(rms / peak, Is.GreaterThan(0.15f), id + " 动态过大，不像持续环境音");
            }
        }

        [Test]
        public void Render_MusicJinglesHaveExpectedLength()
        {
            Assert.That(Rendered(SfxId.VictoryJingle).Duration, Is.InRange(3.0d, 5.0d));
            Assert.That(Rendered(SfxId.DefeatJingle).Duration, Is.InRange(3.0d, 5.0d));
        }

        // ------------------------------------------------------------------
        // 频谱判据（程序化验收，不靠「听着像」）
        // ------------------------------------------------------------------

        [Test]
        public void Render_ExplosionIsLowFrequencyDominated()
        {
            AudioBuffer buffer = Rendered(SfxId.Explosion);
            float low = BandRms(buffer, NoiseFilterKind.LowPass, 200d);
            float high = BandRms(buffer, NoiseFilterKind.HighPass, 4000d);

            Assert.That(low, Is.GreaterThan(high * 2f),
                "爆炸的能量主体应在低频（体感），高频只是碎片点缀");
        }

        [Test]
        public void Render_WaterSplashIsHighFrequencyDominated()
        {
            AudioBuffer buffer = Rendered(SfxId.WaterSplash);
            float low = BandRms(buffer, NoiseFilterKind.LowPass, 200d);
            float high = BandRms(buffer, NoiseFilterKind.HighPass, 1500d);

            Assert.That(high, Is.GreaterThan(low),
                "水花应以中高频为主，低频过强会听成「落石」");
        }

        [Test]
        public void Render_MineBeepConcentratesEnergyAtItsTone()
        {
            AudioBuffer buffer = Rendered(SfxId.MineBeep);

            var atTone = Biquad.BandPass(2093d, 8d, buffer.SampleRate);
            var offTone = Biquad.BandPass(700d, 8d, buffer.SampleRate);

            double onSum = 0d;
            double offSum = 0d;
            for (int i = 0; i < buffer.FrameCount; i++)
            {
                float x = buffer.Samples[i];
                double on = atTone.Process(x);
                double off = offTone.Process(x);
                if (i >= buffer.FrameCount / 4)
                {
                    onSum += on * on;
                    offSum += off * off;
                }
            }

            Assert.That(onSum, Is.GreaterThan(offSum * 4d),
                "蜂鸣的主能量应集中在约 2093 Hz（对应 §5.2 引信提示音）");
        }

        [Test]
        public void Render_AmbientLoopsHaveBothLowAndHighContent()
        {
            foreach (SfxId id in new[] { SfxId.WavesLoop, SfxId.WindLoop })
            {
                AudioBuffer buffer = Rendered(id);
                float low = BandRms(buffer, NoiseFilterKind.LowPass, 250d);
                float high = BandRms(buffer, NoiseFilterKind.HighPass, 1200d);

                Assert.That(low, Is.GreaterThan(0.002f), id + " 缺低频底噪（会显得单薄）");
                Assert.That(high, Is.GreaterThan(0.001f), id + " 缺中高频细节（会显得像白噪声）");
            }
        }

        [Test]
        public void Render_VictoryIsBrighterThanDefeat()
        {
            // 胜利乐句高频更多，失败乐句被压暗——用高频带能量比作为程序化判据
            AudioBuffer victory = Rendered(SfxId.VictoryJingle);
            AudioBuffer defeat = Rendered(SfxId.DefeatJingle);

            float victoryHigh = BandRms(victory, NoiseFilterKind.HighPass, 1200d) / victory.Rms();
            float defeatHigh = BandRms(defeat, NoiseFilterKind.HighPass, 1200d) / defeat.Rms();

            Assert.That(victoryHigh, Is.GreaterThan(defeatHigh),
                "胜利乐句的高频占比应高于失败乐句（失败被低通压暗）");
        }

        /// <summary>用一阶低/高通测某频段的有效值（只统计后半段，避开起振瞬态）。</summary>
        static float BandRms(AudioBuffer buffer, NoiseFilterKind kind, double cutoffHz)
        {
            OnePoleLowPass low = OnePoleLowPass.Create(cutoffHz, buffer.SampleRate);
            OnePoleHighPass high = OnePoleHighPass.Create(cutoffHz, buffer.SampleRate);

            double sum = 0d;
            int counted = 0;
            for (int i = 0; i < buffer.FrameCount; i++)
            {
                float x = buffer.Samples[i];
                float y = kind == NoiseFilterKind.LowPass ? low.Process(x) : high.Process(x);
                if (i >= buffer.FrameCount / 4)
                {
                    sum += (double)y * y;
                    counted++;
                }
            }

            return counted == 0 ? 0f : (float)Math.Sqrt(sum / counted);
        }
    }
}
