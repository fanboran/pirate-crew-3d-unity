using NUnit.Framework;
using PirateCrew.PirateCrew.Water;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="WaterSurfaceSampler"/> 测试（批次 F）：与 PirateOcean.shader 的 OceanGerstner
    /// 同口径的高度合成契约——近岸包络压 0 长涌、|高度| ≤ 有效振幅和、同输入确定性、
    /// 远场几何淡出把短 chop 归零。波参单一事实源 = DefaultSwellWaves/DefaultWaves，
    /// 本文件不抄数值副本。
    /// </summary>
    [TestFixture]
    public class WaterSurfaceSamplerTests
    {
        static readonly Vector2 Center = Vector2.zero;
        static readonly float ArenaRadius = OceanRules.DefaultArenaRadius; // 200 → 保护圈 212

        static WaterWave[] Swell => OceanRules.DefaultSwellWaves;
        static WaterWave[] Chop => WaterRules.DefaultWaves;
        static WaterWave[] NoWaves => new WaterWave[0];

        // ------------------------------------------------------------------
        // 近岸（包络=0）：长涌贡献为 0
        // ------------------------------------------------------------------

        [Test]
        public void NearArenaCenter_SwellContributionIsZero()
        {
            // 保护圈内 smoothstep 包络恒 0 → 带长涌与不带长涌的结果必须完全一致。
            // 采样点覆盖圈心/近圈缘（≤ ProtectRadius=212），时间扫过多个相位。
            for (int i = 0; i < 12; i++)
            {
                var xz = new Vector2(i * 17.3f, i * -5.1f);
                float t = i * 0.731f;
                float withSwell = WaterSurfaceSampler.HeightFromWaves(
                    Swell, Chop, xz, Center, Center, ArenaRadius, t);
                float chopOnly = WaterSurfaceSampler.HeightFromWaves(
                    NoWaves, Chop, xz, Center, Center, ArenaRadius, t);
                Assert.AreEqual(chopOnly, withSwell, 1e-6f,
                    $"保护圈内长涌贡献必须为 0（点 {xz}，t={t}）");
            }
        }

        [Test]
        public void TaskSignatureOverload_MatchesExplicitArenaCenter()
        {
            // 任务书 4 参签名 = 5 参精确口径在"竞技场中心≈网格中心"近似下的快捷方式。
            var xz = new Vector2(31.4f, -15.9f);
            float a = WaterSurfaceSampler.HeightAt(xz, Center, ArenaRadius, 2.718f);
            float b = WaterSurfaceSampler.HeightAt(xz, Center, Center, ArenaRadius, 2.718f);
            Assert.AreEqual(b, a, 0f);
        }

        // ------------------------------------------------------------------
        // 有界性：|高度| ≤ 有效振幅和
        // ------------------------------------------------------------------

        [Test]
        public void Height_BoundedByTotalAmplitudeSum()
        {
            // 全衰减因子 ∈ [0,1]，故 |h| ≤ 全浪表振幅和（swell 1.75 + chop 0.278 = 2.028）。
            float bound = WaterRules.MaxAmplitude(OceanRules.DefaultOceanWaves());
            for (int i = 0; i < 200; i++)
            {
                var xz = new Vector2(i * 13.7f - 900f, i * 7.9f - 600f);
                float h = WaterSurfaceSampler.HeightAt(xz, Center, Center, ArenaRadius, i * 0.37f);
                Assert.That(Mathf.Abs(h), Is.LessThanOrEqualTo(bound + 1e-4f),
                    $"高度越界（点 {xz}）：{h} > {bound}");
            }
        }

        [Test]
        public void NearShore_HeightBoundedByChopAmplitudeSum()
        {
            // 近岸 tighter 界：包络=0 时只剩 chop → |h| ≤ 0.278（波峰契约的采样器版）。
            float chopSum = WaterRules.MaxAmplitude(Chop);
            for (int i = 0; i < 200; i++)
            {
                var xz = new Vector2(i * 2.11f, i * 1.03f); // 最远 ≈ 473 > 212 → 只断言圈内点
                if (Vector2.Distance(xz, Center) > OceanRules.ProtectRadius(ArenaRadius))
                    continue;
                float h = WaterSurfaceSampler.HeightAt(xz, Center, Center, ArenaRadius, i * 0.53f);
                Assert.That(Mathf.Abs(h), Is.LessThanOrEqualTo(chopSum + 1e-4f),
                    $"近岸高度越界（点 {xz}）：{h} > chop 和 {chopSum}");
            }
        }

        // ------------------------------------------------------------------
        // 确定性
        // ------------------------------------------------------------------

        [Test]
        public void SameInput_IsDeterministic()
        {
            var xz = new Vector2(88.8f, 44.4f);
            for (int i = 0; i < 8; i++)
            {
                float t = i * 1.117f;
                float a = WaterSurfaceSampler.HeightAt(xz, Center, Center, ArenaRadius, t);
                float b = WaterSurfaceSampler.HeightAt(xz, Center, Center, ArenaRadius, t);
                Assert.AreEqual(a, b, 0f, "同输入必须逐位一致（无隐藏状态/随机源）");
            }
        }

        // ------------------------------------------------------------------
        // 几何淡出：距网格中心足够远时短 chop（λ4.8）贡献趋 0
        // ------------------------------------------------------------------

        [Test]
        public void FarFromGrid_ShortestChopFadesToZero()
        {
            // r=134.5 → 环宽 1.6·1.15³ ≈ 2.43：λ4.8 每波长 ≈2 顶点 < 3 → 淡出恰为 0；
            // 而 W1-W3（λ26/15/8.4）在此环宽仍是完整位移（ratio ≥ 1.15 → fade=1）。
            float cell = OceanGridRules.RingWidthAtRadius(134.5f);
            Assert.AreEqual(0f, OceanRules.WaveGeometricFade(Chop[3].Wavelength, cell), 1e-6f,
                "前提：λ4.8 在 r=134.5 的几何淡出应为 0");

            var xz = new Vector2(134.5f, 0f);
            float withAllChop = WaterSurfaceSampler.HeightFromWaves(
                Swell, Chop, xz, Center, Center, ArenaRadius, 4.2f);
            float withoutW4 = WaterSurfaceSampler.HeightFromWaves(
                Swell, new[] { Chop[0], Chop[1], Chop[2] }, xz, Center, Center, ArenaRadius, 4.2f);
            Assert.AreEqual(withoutW4, withAllChop, 1e-6f,
                "r=134.5 处 W4（λ4.8）贡献必须为 0（几何淡出）");
        }

        [Test]
        public void FarFromGrid_AllChopFadesOut_SwellSurvivesEnvelopePermitting()
        {
            // r=600 → 环宽 ≈70：全部 chop 波长（≤26）都跌破每波 3 顶点 → 贡献全 0。
            float cell = OceanGridRules.RingWidthAtRadius(600f);
            Assert.AreEqual(0f, OceanRules.WaveGeometricFade(WaterRules.DefaultWaves[0].Wavelength, cell), 1e-6f,
                "前提：最长的 chop（W1）在 r=600 也应被淡出");

            var xz = new Vector2(600f, 0f);
            float withChop = WaterSurfaceSampler.HeightFromWaves(
                Swell, Chop, xz, Center, Center, ArenaRadius, 1.3f);
            float noChop = WaterSurfaceSampler.HeightFromWaves(
                Swell, NoWaves, xz, Center, Center, ArenaRadius, 1.3f);
            Assert.AreEqual(noChop, withChop, 1e-6f, "r=600 处全部 chop 贡献必须为 0");
        }
    }
}
