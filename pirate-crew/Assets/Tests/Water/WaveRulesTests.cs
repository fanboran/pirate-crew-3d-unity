using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Water;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="WaterRules"/> 测试：Gerstner 波几何与解析法线的一致性、默认参数的安全边界、
    /// 焦散/泡沫规则。数值依据写在断言旁。
    /// </summary>
    [TestFixture]
    public class WaveRulesTests
    {
        static WaterWave[] Defaults => WaterRules.DefaultWaves;

        // ------------------------------------------------------------------
        // 默认参数：观感下限 + 不自交 + 不穿出地面
        // ------------------------------------------------------------------

        [Test]
        public void Defaults_ShortestCrestSpacingAtLeastFourUnits()
        {
            // 协调者口径：波峰间距不宜小于 4 单位（格 1→2 单位后由 2 ×2，WaterRules.MinCrestSpacing），
            // 否则单位站上去像"踩碎浪"。默认最短波长 4.8（旧 2.4 ×2）。
            float min = WaterRules.MinWavelengthOf(Defaults);
            Assert.That(min, Is.GreaterThanOrEqualTo(WaterRules.MinCrestSpacing));
            Assert.AreEqual(4.8f, min, 1e-4f);
        }

        [Test]
        public void Defaults_FoldFreeBySufficientCondition()
        {
            // 充分条件 Σ Q·A·k < 1；实测 0.081（余量 > 10 倍）。
            float metric = WaterRules.SumHorizontalFactor(Defaults);
            Assert.That(metric, Is.LessThan(1f));
            Assert.That(metric, Is.LessThan(0.2f));
        }

        [Test]
        public void Defaults_CrestStaysBelowGroundLevel()
        {
            // 波峰硬约束（M4 用户裁决）：竞技场附近波峰最高点 < -0.10（不穿岛基湿沙带；
            // 岛顶最低 +0.5、静水面 -0.4）。换算：近岸振幅预算 = -0.10 − (-0.4) = 0.30
            // （OceanRules.ShoreAmplitudeBudget），旧口径是"振幅和 < 0.4（水面距地面）"→ 0.278 上限，
            // 新口径放宽到 0.30 → chop 振幅和 0.278 仍达标：波峰 -0.122 < -0.10 ✓。
            // 长涌（振幅 0.5~1.2）不参与近岸预算：OceanRules.SwellEnvelope 在竞技场保护圈内把它压 0
            //（见 OceanRulesTests）。
            float ampSum = WaterRules.MaxAmplitude(Defaults);
            Assert.That(LevelGeometry.WaterSurfaceY + ampSum,
                Is.LessThan(OceanRules.MaxCrestWorldY),
                $"chop 波峰 {LevelGeometry.WaterSurfaceY + ampSum} 必须低于 {OceanRules.MaxCrestWorldY}");
            Assert.That(ampSum, Is.LessThanOrEqualTo(OceanRules.ShoreAmplitudeBudget));
            Assert.That(LevelGeometry.WaterSurfaceY + ampSum, Is.LessThan(LevelGeometry.GroundTopY));
        }

        [Test]
        public void Defaults_FourWaves_WithDistinctWavelengths()
        {
            Assert.AreEqual(4, Defaults.Length);
            for (int i = 0; i < Defaults.Length; i++)
            {
                Assert.That(Defaults[i].Wavelength, Is.GreaterThan(0f));
                Assert.That(Defaults[i].Amplitude, Is.GreaterThan(0f));
                Assert.That(Defaults[i].Steepness, Is.InRange(0f, 1f));
            }
        }

        // ------------------------------------------------------------------
        // 解析法线：与"位移后参数曲面的数值微分"一致（Gerstner 的核心契约）
        // ------------------------------------------------------------------

        static Vector3 DisplacedPosition(WaterWave[] waves, float x, float z, float t)
        {
            Vector2 d = WaterRules.HorizontalDisplacement(waves, x, z, t);
            float h = WaterRules.Height(waves, x, z, t);
            return new Vector3(x + d.x, h, z + d.y);
        }

        [Test]
        public void AnalyticNormal_MatchesFiniteDifferenceOfDisplacedSurface()
        {
            const float e = 1e-3f;
            float[] times = { 0f, 0.37f, 1.9f, 5.2f };
            float[] xs = { 0f, 3.1f, 12.7f, 24.9f };
            float[] zs = { 0f, 2.2f, 8.5f, 16.4f };

            foreach (float t in times)
            {
                foreach (float x in xs)
                {
                    foreach (float z in zs)
                    {
                        Vector3 pXp = DisplacedPosition(Defaults, x + e, z, t);
                        Vector3 pXm = DisplacedPosition(Defaults, x - e, z, t);
                        Vector3 pZp = DisplacedPosition(Defaults, x, z + e, t);
                        Vector3 pZm = DisplacedPosition(Defaults, x, z - e, t);

                        Vector3 tx = (pXp - pXm) / (2f * e);
                        Vector3 tz = (pZp - pZm) / (2f * e);
                        Vector3 numeric = Vector3.Cross(tz, tx).normalized;
                        Vector3 analytic = WaterRules.AnalyticNormal(Defaults, x, z, t);

                        Assert.That(Vector3.Dot(numeric, analytic), Is.GreaterThan(0.999f),
                            $"法线不一致 @ t={t} x={x} z={z}: numeric={numeric} analytic={analytic}");
                    }
                }
            }
        }

        [Test]
        public void AnalyticNormal_IsUnitLengthAndUpward()
        {
            for (int i = 0; i < 50; i++)
            {
                float x = i * 1.13f;
                float z = i * 0.41f;
                float t = i * 0.17f;
                Vector3 n = WaterRules.AnalyticNormal(Defaults, x, z, t);
                Assert.That(n.magnitude, Is.EqualTo(1f).Within(1e-4f));
                Assert.That(n.y, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void FlatWater_NormalIsExactlyUp()
        {
            var flat = new[]
            {
                new WaterWave(new Vector2(1f, 0f), 8f, 0f, 1f, 1f),
                new WaterWave(new Vector2(0f, 1f), 5f, 0f, 1f, 1f),
            };
            Vector3 n = WaterRules.AnalyticNormal(flat, 3f, 7f, 2.5f);
            Assert.That(Vector3.Distance(n, Vector3.up), Is.LessThan(1e-5f));
            Assert.AreEqual(0f, WaterRules.Height(flat, 3f, 7f, 2.5f), 1e-6f);
        }

        [Test]
        public void Jacobian_PositiveAcrossArenaAndTime()
        {
            // 不自交的现场抽查：50×17 竞技场铺格 + 数个时刻。
            for (float x = 0f; x <= 50f; x += 2.5f)
            {
                for (float z = 0f; z <= 17f; z += 2.125f)
                {
                    for (float t = 0f; t <= 6f; t += 1.37f)
                    {
                        float j = WaterRules.Jacobian(Defaults, x, z, t);
                        Assert.That(j, Is.GreaterThan(0.9f), $"雅可比过低 @ ({x},{z},t={t}) J={j}");
                        Assert.IsTrue(WaterRules.IsFoldFree(Defaults, x, z, t));
                    }
                }
            }
        }

        [Test]
        public void Height_IsBoundedByAmplitudeSum()
        {
            float bound = WaterRules.MaxAmplitude(Defaults);
            for (int i = 0; i < 200; i++)
            {
                float h = WaterRules.Height(Defaults, i * 0.53f, i * 0.29f, i * 0.07f);
                Assert.That(Mathf.Abs(h), Is.LessThanOrEqualTo(bound + 1e-4f));
            }
        }

        // ------------------------------------------------------------------
        // 深水色散
        // ------------------------------------------------------------------

        [Test]
        public void WaveNumber_AndAngularFrequency_FollowDeepWaterDispersion()
        {
            var w = new WaterWave(new Vector2(1f, 0f), 10f, 0.1f, 0.5f, 1f);
            Assert.AreEqual(Mathf.PI * 2f / 10f, w.WaveNumber, 1e-5f);
            // ω² = g·k
            float expected = Mathf.Sqrt(WaterRules.Gravity * w.WaveNumber);
            Assert.AreEqual(expected, w.AngularFrequency, 1e-4f);
        }

        [Test]
        public void LongerWaves_TravelFaster()
        {
            var longWave = new WaterWave(new Vector2(1f, 0f), 16f, 0.05f, 0.5f, 1f);
            var shortWave = new WaterWave(new Vector2(1f, 0f), 2.5f, 0.02f, 0.5f, 1f);
            float cLong = longWave.AngularFrequency / longWave.WaveNumber;
            float cShort = shortWave.AngularFrequency / shortWave.WaveNumber;
            Assert.That(cLong, Is.GreaterThan(cShort));
        }

        // ------------------------------------------------------------------
        // 焦散 / 泡沫规则
        // ------------------------------------------------------------------

        [Test]
        public void CausticDepthFade_MonotonicDecreasing()
        {
            const float fade = 2.2f;
            Assert.AreEqual(1f, WaterRules.CausticDepthFade(0f, fade), 1e-5f);
            Assert.AreEqual(0f, WaterRules.CausticDepthFade(fade, fade), 1e-5f);
            Assert.AreEqual(0f, WaterRules.CausticDepthFade(fade + 5f, fade), 1e-5f);

            float prev = float.MaxValue;
            for (float d = 0f; d <= fade + 1f; d += 0.05f)
            {
                float v = WaterRules.CausticDepthFade(d, fade);
                Assert.That(v, Is.InRange(0f, 1f));
                Assert.That(v, Is.LessThanOrEqualTo(prev + 1e-6f), $"深度 {d} 处焦散不单调");
                prev = v;
            }
        }

        [Test]
        public void FoamPulse_StaysInUnitRange()
        {
            for (float d = 0f; d <= 4f; d += 0.1f)
            {
                for (float t = 0f; t <= 10f; t += 0.13f)
                {
                    float v = WaterRules.FoamPulse(d, t, 0.55f, 1.2f);
                    Assert.That(v, Is.InRange(0f, 1f));
                }
            }
        }

        [Test]
        public void FoamBreakup_StaysInUnitRange()
        {
            for (int i = 0; i < 50; i++)
            {
                float v = WaterRules.FoamBreakup(i * 0.031f, i * 0.017f, 0.45f);
                Assert.That(v, Is.InRange(0f, 1f));
            }
        }
    }
}
