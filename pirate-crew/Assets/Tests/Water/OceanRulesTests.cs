using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Water;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="OceanRules"/> 测试：M4 大海域的波表契约（长涌 60-120u/0.5-1.2）、波峰硬约束
    /// （近岸 &lt; -0.10）、长涌近岸包络、白帽规则、远场/地平线淡出、几何解析淡出。
    /// 数值依据写在断言旁；shader（PirateOcean.shader）内嵌同式，改一边必须改另一边。
    /// </summary>
    [TestFixture]
    public class OceanRulesTests
    {
        static WaterWave[] Chop => WaterRules.DefaultWaves;
        static WaterWave[] Swells => OceanRules.DefaultSwellWaves;

        // ------------------------------------------------------------------
        // 长涌波表：任务书契约（波长 60-120u、振幅 0.5-1.2，两组）
        // ------------------------------------------------------------------

        [Test]
        public void SwellWaves_TwoWaves_WithinMandate()
        {
            Assert.AreEqual(2, Swells.Length);
            foreach (WaterWave wave in Swells)
            {
                Assert.That(wave.Wavelength, Is.InRange(60f, 120f),
                    $"长涌波长 {wave.Wavelength} 应在任务书 60-120u 内");
                Assert.That(wave.Amplitude, Is.InRange(0.5f, 1.2f),
                    $"长涌振幅 {wave.Amplitude} 应在任务书 0.5-1.2 内");
                Assert.That(wave.Steepness, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void SwellWaves_LongerThanEveryChopWave()
        {
            // 分层语义：长涌是"大涌"，chop 是"碎浪"，两档波长不交叠。
            float maxChop = 0f;
            foreach (WaterWave wave in Chop)
                maxChop = Mathf.Max(maxChop, wave.Wavelength);
            foreach (WaterWave wave in Swells)
                Assert.That(wave.Wavelength, Is.GreaterThan(maxChop));
        }

        [Test]
        public void OceanWaves_FoldFreeBySufficientCondition()
        {
            // 全浪表（swell×2 + chop×4）的 Σ Q·A·k 充分条件：swell ≈ 0.083 + chop 0.081 ≈ 0.164 ≪ 1。
            float metric = WaterRules.SumHorizontalFactor(OceanRules.DefaultOceanWaves());
            Assert.That(metric, Is.LessThan(1f));
            Assert.That(metric, Is.LessThan(0.3f));
        }

        [Test]
        public void OceanWaves_HeightBoundedByTotalAmplitudeSum()
        {
            float bound = WaterRules.MaxAmplitude(OceanRules.DefaultOceanWaves());
            for (int i = 0; i < 300; i++)
            {
                float h = WaterRules.Height(OceanRules.DefaultOceanWaves(), i * 1.37f, i * 0.71f, i * 0.19f);
                Assert.That(Mathf.Abs(h), Is.LessThanOrEqualTo(bound + 1e-4f));
            }
        }

        // ------------------------------------------------------------------
        // 波峰硬约束（近岸 < -0.10）：包络把长涌压 0 后只剩 chop
        // ------------------------------------------------------------------

        [Test]
        public void NearShore_CrestStaysBelowContract()
        {
            // 近岸（包络=0）：有效振幅 = chop 振幅和 0.278 → 波峰 -0.4 + 0.278 = -0.122 < -0.10 ✓。
            float chopSum = WaterRules.MaxAmplitude(Chop);
            float crestY = LevelGeometry.WaterSurfaceY + chopSum;
            Assert.That(crestY, Is.LessThan(OceanRules.MaxCrestWorldY),
                $"近岸波峰 {crestY} 必须低于契约 {OceanRules.MaxCrestWorldY}（不穿岛基湿沙带）");
            // chop 本身就要吃满近岸预算的 90% 以上（否则长涌没必要包络）。
            Assert.That(chopSum, Is.LessThanOrEqualTo(OceanRules.ShoreAmplitudeBudget));
        }

        [Test]
        public void Offshore_FullSwellIsChopPlusSwell()
        {
            // 外海（包络=1）：全振幅 = chop + swell（无岛，无约束）。
            float full = WaterRules.MaxAmplitude(OceanRules.DefaultOceanWaves());
            Assert.That(full, Is.EqualTo(WaterRules.MaxAmplitude(Chop) + OceanRules.SwellAmplitudeSum()).Within(1e-4f));
            // 外海涌高可观（"茫茫大海"）：全振幅至少是 chop 的 5 倍。
            Assert.That(OceanRules.SwellAmplitudeSum(), Is.GreaterThan(WaterRules.MaxAmplitude(Chop) * 5f));
        }

        // ------------------------------------------------------------------
        // 长涌近岸包络
        // ------------------------------------------------------------------

        [Test]
        public void SwellEnvelope_ZeroInsideProtect_OneOutsideFull()
        {
            float protect = OceanRules.ProtectRadius(OceanRules.DefaultArenaRadius);
            float full = OceanRules.FullSwellRadius(OceanRules.DefaultArenaRadius);

            Assert.AreEqual(0f, OceanRules.SwellEnvelope(0f, protect, full), 1e-5f);
            Assert.AreEqual(0f, OceanRules.SwellEnvelope(protect * 0.5f, protect, full), 1e-5f);
            Assert.AreEqual(0f, OceanRules.SwellEnvelope(protect, protect, full), 1e-5f);
            Assert.AreEqual(1f, OceanRules.SwellEnvelope(full, protect, full), 1e-5f);
            Assert.AreEqual(1f, OceanRules.SwellEnvelope(full + 500f, protect, full), 1e-5f);
        }

        [Test]
        public void SwellEnvelope_MonotonicNonDecreasing()
        {
            float protect = OceanRules.ProtectRadius(OceanRules.DefaultArenaRadius);
            float full = OceanRules.FullSwellRadius(OceanRules.DefaultArenaRadius);
            float prev = 0f;
            for (float d = 0f; d <= full + 100f; d += 5f)
            {
                float v = OceanRules.SwellEnvelope(d, protect, full);
                Assert.That(v, Is.InRange(0f, 1f));
                Assert.That(v, Is.GreaterThanOrEqualTo(prev - 1e-6f), $"包络在 d={d} 处回落");
                prev = v;
            }
        }

        [Test]
        public void ProtectRadius_CoversLargestM4Map()
        {
            // M4 §2.3 最大跨度 280u：半对角 = 280/√2 ≈ 198。默认竞技场半径必须罩住它。
            float halfDiagonal = 280f * Mathf.Sqrt(0.5f);
            Assert.That(OceanRules.DefaultArenaRadius, Is.GreaterThanOrEqualTo(halfDiagonal));

            // ForArena 工厂：矩形外扩 → 半对角。
            var cfg = OceanConfig.ForArena(new Vector2(140f, 140f), 140f, 140f);
            Assert.That(cfg.ArenaRadius, Is.EqualTo(halfDiagonal).Within(1e-3f));
        }

        [Test]
        public void FullSwellRadius_IsBeyondProtect()
        {
            float protect = OceanRules.ProtectRadius(OceanRules.DefaultArenaRadius);
            float full = OceanRules.FullSwellRadius(OceanRules.DefaultArenaRadius);
            Assert.That(full, Is.GreaterThan(protect));
            Assert.That(full - protect, Is.EqualTo(OceanRules.SwellRampWidth).Within(1e-4f));
        }

        // ------------------------------------------------------------------
        // 远场淡出与地平线
        // ------------------------------------------------------------------

        [Test]
        public void DisplacementFarFade_OneNear_ZeroFar_Monotonic()
        {
            Assert.AreEqual(1f, OceanRules.DisplacementFarFade(0f), 1e-5f);
            Assert.AreEqual(1f, OceanRules.DisplacementFarFade(OceanRules.DisplaceFadeStart), 1e-5f);
            Assert.AreEqual(0f, OceanRules.DisplacementFarFade(OceanRules.DisplaceFadeEnd), 1e-5f);
            Assert.AreEqual(0f, OceanRules.DisplacementFarFade(6000f), 1e-5f);

            float prev = 1f;
            for (float d = 0f; d <= OceanRules.DisplaceFadeEnd + 100f; d += 25f)
            {
                float v = OceanRules.DisplacementFarFade(d);
                Assert.That(v, Is.InRange(0f, 1f));
                Assert.That(v, Is.LessThanOrEqualTo(prev + 1e-6f), $"位移远淡在 d={d} 处回升");
                prev = v;
            }
        }

        [Test]
        public void HorizonFade_CompletesBeforeSkirtEdge()
        {
            // 地平线融合必须在裙边半径之前完成，否则 4200u 处的网格硬边会露出来。
            Assert.AreEqual(0f, OceanRules.HorizonFade(OceanRules.HorizonFadeStart), 1e-5f);
            Assert.AreEqual(1f, OceanRules.HorizonFade(OceanRules.HorizonFadeEnd), 1e-5f);
            Assert.That(OceanRules.HorizonFadeEnd, Is.LessThan(OceanGridRules.HorizonRadius));
        }

        [Test]
        public void DetailFarFade_BoundedAndMonotonic()
        {
            Assert.AreEqual(1f, OceanRules.DetailFarFade(0f), 1e-5f);
            Assert.AreEqual(0f, OceanRules.DetailFarFade(OceanRules.DetailFarFadeEnd + 50f), 1e-5f);
            float prev = 1f;
            for (float d = 0f; d <= OceanRules.DetailFarFadeEnd + 50f; d += 20f)
            {
                float v = OceanRules.DetailFarFade(d);
                Assert.That(v, Is.InRange(0f, 1f));
                Assert.That(v, Is.LessThanOrEqualTo(prev + 1e-6f));
                prev = v;
            }
        }

        // ------------------------------------------------------------------
        // 白帽规则
        // ------------------------------------------------------------------

        [Test]
        public void Whitecap_SteepnessTerm_ZeroWhenFlat()
        {
            // J 达标（不陡）→ 无白帽；J 越低（越陡）越白。
            Assert.AreEqual(0f, OceanRules.WhitecapSteepnessTerm(1f), 1e-5f);
            Assert.AreEqual(0f, OceanRules.WhitecapSteepnessTerm(OceanRules.WhitecapJacobianThreshold), 1e-5f);
            Assert.AreEqual(1f, OceanRules.WhitecapSteepnessTerm(OceanRules.WhitecapJacobianThreshold
                - OceanRules.WhitecapSoftness), 1e-5f);

            float prev = 0f;
            for (float j = 1f; j >= 0.3f; j -= 0.02f)
            {
                float v = OceanRules.WhitecapSteepnessTerm(j);
                Assert.That(v, Is.InRange(0f, 1f));
                // J 递减（越陡）→ 陡度项单调不减；J ≥ 阈值的前缀是 0 平台，之后才爬升。
                Assert.That(v, Is.GreaterThanOrEqualTo(prev - 1e-6f), $"陡度项在 J={j} 处回落");
                prev = v;
            }
        }

        [Test]
        public void Whitecap_MaskZeroWithoutSteepnessOrCrest()
        {
            // 平水（J=1）：再高的波也不白帽。
            Assert.AreEqual(0f, OceanRules.WhitecapMask(1f, 1f, 1f, 1f), 1e-5f);
            // 波谷（heightNorm=-1）：再陡也不白帽。
            Assert.AreEqual(0f, OceanRules.WhitecapMask(0.5f, -1f, 1f, 1f), 1e-5f);
        }

        [Test]
        public void Whitecap_MaskBoundedAndGrowsWithEnvelopeAndNoise()
        {
            for (float j = 0.5f; j <= 1f; j += 0.05f)
            {
                for (float h = -1f; h <= 1f; h += 0.25f)
                {
                    for (float n = 0f; n <= 1f; n += 0.25f)
                    {
                        for (float e = 0f; e <= 1f; e += 0.5f)
                        {
                            float m = OceanRules.WhitecapMask(j, h, n, e);
                            Assert.That(m, Is.InRange(0f, 1f), $"J={j} h={h} n={n} e={e}");
                        }
                    }
                }
            }

            // 包络↑（外海）与噪声↑（碎化增益）都只增不减。
            Assert.That(OceanRules.WhitecapMask(0.7f, 1f, 0.5f, 1f),
                Is.GreaterThanOrEqualTo(OceanRules.WhitecapMask(0.7f, 1f, 0.5f, 0f) - 1e-6f));
            Assert.That(OceanRules.WhitecapMask(0.7f, 1f, 1f, 1f),
                Is.GreaterThanOrEqualTo(OceanRules.WhitecapMask(0.7f, 1f, 0f, 1f) - 1e-6f));
        }

        // ------------------------------------------------------------------
        // 几何解析淡出（网格密度 LOD）
        // ------------------------------------------------------------------

        [Test]
        public void WaveGeometricFade_FullAtSamplingAdequacy_ZeroWhenUnderresolved()
        {
            const float cell = OceanGridRules.CellSize;
            // 最短 chop 4.8u 恰好 = 3×1.6（旧 WaterMeshRules 的达标线）→ 必须全额位移（不砍半）。
            Assert.AreEqual(1f, OceanRules.WaveGeometricFade(4.8f, cell), 1e-5f);
            Assert.AreEqual(1f, OceanRules.WaveGeometricFade(120f, cell), 1e-5f);
            // 跌破 2.1 顶点/波 → 淡到 0。
            Assert.AreEqual(0f, OceanRules.WaveGeometricFade(4.8f, 2.4f), 1e-5f);
            Assert.AreEqual(0f, OceanRules.WaveGeometricFade(2.4f, cell), 1e-5f);
        }

        [Test]
        public void WaveGeometricFade_AllDefaultWavesResolvedInUniformZone()
        {
            const float cell = OceanGridRules.CellSize;
            foreach (WaterWave wave in OceanRules.DefaultOceanWaves())
                Assert.AreEqual(1f, OceanRules.WaveGeometricFade(wave.Wavelength, cell), 1e-4f,
                    $"波长 {wave.Wavelength} 在近场均匀区（格 {cell}）必须被完整解析");
        }
    }

    /// <summary><see cref="OceanGridRules"/> 测试：分级网格梯子、覆盖率、闭式环宽一致性。</summary>
    [TestFixture]
    public class OceanGridRulesTests
    {
        [Test]
        public void RingRadii_ReachHorizonRadius()
        {
            // M4 §1/§5 硬契约：裙边 ≥ 4000u（本工程取 4200）。
            float[] radii = OceanGridRules.RingRadii();
            Assert.That(radii[radii.Length - 1], Is.GreaterThanOrEqualTo(OceanGridRules.HorizonRadius));
            Assert.That(OceanGridRules.HorizonRadius, Is.GreaterThanOrEqualTo(4000f));
        }

        [Test]
        public void RingRadii_UniformZoneIsCellStepped()
        {
            float[] radii = OceanGridRules.RingRadii();
            int uniformRings = OceanGridRules.UniformRingCount;
            Assert.That(uniformRings * OceanGridRules.CellSize,
                Is.EqualTo(OceanGridRules.UniformRadius).Within(OceanGridRules.CellSize));

            for (int i = 1; i <= uniformRings && i < radii.Length; i++)
                Assert.That(radii[i], Is.EqualTo(i * OceanGridRules.CellSize).Within(1e-3f),
                    $"均匀区第 {i} 环应严格按格距步进");
        }

        [Test]
        public void RingRadii_StartAtZeroAndStrictlyIncreasing()
        {
            float[] radii = OceanGridRules.RingRadii();
            Assert.AreEqual(0f, radii[0], 1e-6f);
            for (int i = 1; i < radii.Length; i++)
                Assert.That(radii[i], Is.GreaterThan(radii[i - 1]), $"第 {i} 环半径未递增");
        }

        [Test]
        public void RingWidths_NonDecreasing_Outward()
        {
            float[] radii = OceanGridRules.RingRadii();
            float prevWidth = 0f;
            for (int i = 1; i < radii.Length; i++)
            {
                float width = radii[i] - radii[i - 1];
                Assert.That(width, Is.GreaterThanOrEqualTo(prevWidth - 1e-4f), $"第 {i} 环宽度回落");
                prevWidth = width;
            }
        }

        [Test]
        public void RingWidthAtRadius_MatchesAccumulatedLadder()
        {
            // 闭式解必须与实际累加梯子一致（shader 里的 LOD 淡出靠它对齐）。
            float[] radii = OceanGridRules.RingRadii();
            for (int i = 1; i < radii.Length; i++)
            {
                float width = radii[i] - radii[i - 1];
                float mid = (radii[i] + radii[i - 1]) * 0.5f;
                float closed = OceanGridRules.RingWidthAtRadius(mid);
                Assert.That(closed, Is.EqualTo(width).Within(Mathf.Max(width * 0.05f, 0.4f)),
                    $"半径 {mid} 处闭式环宽 {closed} ≠ 梯子环宽 {width}");
            }
        }

        [Test]
        public void Counts_MatchFormulas_AndStaySane()
        {
            float[] radii = OceanGridRules.RingRadii();
            int rings = radii.Length - 1;

            Assert.AreEqual(1 + rings * (OceanGridRules.Segments + 1), OceanGridRules.VertexCount(rings));
            Assert.AreEqual(OceanGridRules.Segments + (rings - 1) * OceanGridRules.Segments * 2,
                OceanGridRules.TriangleCount(rings));
            Assert.AreEqual(OceanGridRules.TriangleCount(rings) * 3, OceanGridRules.IndexCount(rings));

            // 规模核对（不计性能，但也不能失控）：默认梯子 ~110 环 → 顶点 < 40k、面 < 80k。
            Assert.That(rings, Is.InRange(90, 200));
            Assert.That(OceanGridRules.VertexCount(rings), Is.LessThan(40000));
            Assert.That(OceanGridRules.TriangleCount(rings), Is.LessThan(80000));
            Assert.IsFalse(OceanGridRules.Needs32BitIndices(rings), "默认规模应落在 16 位索引内");
        }

        [Test]
        public void SnapStep_EqualsNearCellSize()
        {
            // 步进对齐的口径：相机移动不足一格时网格原地不动。
            Assert.AreEqual(OceanGridRules.CellSize, OceanGridRules.SnapStep, 1e-6f);
        }
    }
}
