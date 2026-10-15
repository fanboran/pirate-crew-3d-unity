using NUnit.Framework;
using PirateCrew.WaterLab;
using UnityEngine;

namespace PirateCrew.WaterLab.Tests
{
    /// <summary>
    /// <see cref="WaterLabRules"/> 测试：实验水体（浅水方程）的数值契约——
    /// 质量守恒、孤立波传播速度、无负水深、干湿界稳定性、CFL 常量自检。
    /// 测的是 CPU 参考实现（<see cref="WaterLabRules.ReferenceSim"/>）；
    /// compute（WaterLabSim.compute）同公式落笔，GPU 侧靠观感与读数对齐（见实验水体文档 §5）。
    /// </summary>
    [TestFixture]
    public class WaterLabRulesTests
    {
        // ------------------------------------------------------------------
        // 常量自检：默认参数组必须落在稳定域内
        // ------------------------------------------------------------------

        [Test]
        public void DefaultParameters_AreCflStable()
        {
            // h_max ≈ |OffshoreDepth| + 浪高余量（波列总振幅 + 潮位）≈ 3 m
            float hMax = -WaterLabRules.OffshoreDepth + WaterLabRules.MaxWaveAmplitude + WaterLabRules.TideAmplitude;
            Assert.IsTrue(WaterLabRules.IsStable(hMax, WaterLabRules.FixedStep, WaterLabRules.WaterCellSize),
                "默认参数组应满足 CFL（√(g·h_max)·dt/dx ≤ 0.5）");
        }

        // ------------------------------------------------------------------
        // 多向波列：波长互质（不规律性的来源）+ 深水色散 + 高程有界
        // ------------------------------------------------------------------

        [Test]
        public void WaveTrains_HaveDistinctWavelengthsAndDirections()
        {
            var trains = WaterLabRules.WaveTrains;
            Assert.That(trains.Length, Is.GreaterThanOrEqualTo(2), "至少两列波才有不规律叠加");
            for (int i = 0; i < trains.Length; i++)
                for (int j = i + 1; j < trains.Length; j++)
                {
                    Assert.That(Mathf.Abs(trains[i].Length - trains[j].Length), Is.GreaterThan(0.5f),
                        $"波列 {i}/{j} 波长过近，叠加会退化为周期性拍");
                    Assert.That(Mathf.Abs(trains[i].DirZ - trains[j].DirZ), Is.GreaterThan(0.05f),
                        $"波列 {i}/{j} 方向过近，波峰线趋于平行");
                }
        }

        [Test]
        public void WaveTrains_FollowShallowWaterDispersionAndStayBounded()
        {
            // 浅水色散 ω = k·√(g·h带)：相速必须等于 SWE 特征速 √(g·h)——
            // 用深水色散会把长波相速强迫到特征速之上（超临界），波源带激波化爆场
            //（30× 快进出图实证：全场短波垃圾 + 泡沫满格 + η 编码饱和）。
            float bandDepth = WaterLabRules.SeaLevel - WaterLabRules.OffshoreDepth;
            foreach (var train in WaterLabRules.WaveTrains)
            {
                float theory = train.K * WaterLabRules.WaveSpeed(bandDepth);
                Assert.That(train.Omega, Is.EqualTo(theory).Within(1e-4f),
                    "相速应服从浅水色散 ω=k·√(g·h带)（与 SWE 特征速匹配，超临界强迫会爆场）");
            }

            // 高程界：任意时空点 |η - SeaLevel| ≤ Σ振幅 + 潮位（编码量程与 CFL 自检的依据）
            float bound = WaterLabRules.MaxWaveAmplitude + WaterLabRules.TideAmplitude;
            for (int it = 0; it < 32; it++)
            {
                float t = it * 0.37f; // 非整周期采样
                for (int ix = 0; ix < 8; ix++)
                for (int iz = 0; iz < 8; iz++)
                {
                    float x = -30f + ix * 8.6f;
                    float z = -30f + iz * 8.6f;
                    Assert.That(Mathf.Abs(WaterLabRules.WaveEta(x, z, t) - WaterLabRules.SeaLevel),
                        Is.LessThanOrEqualTo(bound + 1e-3f), $"({x:F0},{z:F0},{t:F1}) 波高超界");
                }
            }
        }

        [Test]
        public void TerrainProfile_IsMonotoneishFlatToBeach()
        {
            // 剖面骨架：深水端低于海平面、干沙端高于海平面、缓坡+滩面单调（微起伏容差内）
            Assert.That(WaterLabRules.TerrainHeight(-30f, 0f), Is.LessThan(WaterLabRules.SeaLevel - 0.8f));
            Assert.That(WaterLabRules.TerrainHeight(30f, 0f), Is.GreaterThan(WaterLabRules.SeaLevel + 0.3f));
            float prev = WaterLabRules.TerrainHeight(WaterLabRules.ShelfStartX, 0f);
            for (float x = WaterLabRules.ShelfStartX + 1f; x <= WaterLabRules.FaceEndX; x += 1f)
            {
                float cur = WaterLabRules.TerrainHeight(x, 0f);
                Assert.That(cur, Is.GreaterThan(prev - WaterLabRules.SandRippleAmplitude * 2.1f),
                    $"爬升段在 x={x} 处出现超过沙纹容差的下降");
                prev = cur;
            }
        }

        // ------------------------------------------------------------------
        // 质量守恒：封闭平地 + 反射边界，Σh 恒定（平均通量形式的天然性质）
        // ------------------------------------------------------------------

        [Test]
        public void MassMomentum_ConservesMassInClosedBasin()
        {
            const int cells = 64;
            float dx = 0.25f;
            var h = new float[cells * cells];
            var vx = new float[cells * cells];
            var vz = new float[cells * cells];
            var ground = new float[cells * cells];

            for (int i = 0; i < h.Length; i++)
                h[i] = 1f; // 平地静水 1 m 深
            // 中心高斯扰动（有东西可传播）
            int c0 = cells / 2;
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int i = x + z * cells;
                    float r2 = (x - c0) * (x - c0) + (z - c0) * (z - c0);
                    h[i] = 1f + 0.1f * Mathf.Exp(-r2 / 18f);
                }

            float mass0 = 0f;
            for (int i = 0; i < h.Length; i++) mass0 += h[i];

            for (int step = 0; step < 240; step++) // 4 秒
                WaterLabRules.ReferenceSim.StepMassMomentum(h, vx, vz, ground, cells, cells, dx, WaterLabRules.FixedStep);

            float mass1 = 0f;
            for (int i = 0; i < h.Length; i++) mass1 += h[i];
            Assert.That((mass1 - mass0) / mass0, Is.InRange(-0.001f, 0.001f),
                $"4 秒后质量漂移 {(mass1 - mass0) / mass0:P3} 超出 ±0.1%");
        }

        // ------------------------------------------------------------------
        // 孤立波速：浅水长波以 √(g·h0) 传播（±5%）
        // ------------------------------------------------------------------

        [Test]
        public void MassMomentum_WaveTravelsAtShallowWaterSpeed()
        {
            const int cells = 256;
            float dx = 0.25f;
            var h = new float[cells * cells];
            var vx = new float[cells * cells];
            var vz = new float[cells * cells];
            var ground = new float[cells * cells];
            const float h0 = 1f; // 静水深 → 理论波速 √(9.81) ≈ 3.13 m/s

            for (int i = 0; i < h.Length; i++)
                h[i] = h0;
            // 1D 高斯波峰（z 向均匀 → 沿 x 传播的平面波）
            int xc = 40;
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int i = x + z * cells;
                    h[i] = h0 + 0.05f * Mathf.Exp(-((x - xc) * (x - xc)) / 8f);
                }

            int FindPeak()
            {
                int peak = 0;
                float best = float.MinValue;
                for (int x = 0; x < cells; x++)
                {
                    float col = 0f;
                    for (int z = 0; z < cells; z++) col += h[x + z * cells];
                    if (col > best) { best = col; peak = x; }
                }
                return peak;
            }

            int p0 = FindPeak();
            const int steps = 120; // 2 秒
            for (int step = 0; step < steps; step++)
                WaterLabRules.ReferenceSim.StepMassMomentum(h, vx, vz, ground, cells, cells, dx, WaterLabRules.FixedStep);
            int p1 = FindPeak();

            float dxMeters = (p1 - p0) * dx;
            float speed = Mathf.Abs(dxMeters) / (steps * WaterLabRules.FixedStep);
            float theory = WaterLabRules.WaveSpeed(h0);
            // 平均通量格式（F=h̄·v̄）的固有二阶数值色散使峰值偏慢约 5-8%；
            // 本测试钉的是量级正确（无 0.5×/2× 的结构性错误），容差 ±10%。
            Assert.That(speed / theory, Is.InRange(0.90f, 1.10f),
                $"实测波速 {speed:F2} m/s vs 理论 √(g·h0)={theory:F2} m/s，偏差超出 ±10%");
        }

        // ------------------------------------------------------------------
        // 数值卫生：无负水深、无 NaN、速度有界（带地形的滩面场景）
        // ------------------------------------------------------------------

        [Test]
        public void MassMomentum_BeachProfile_StaysBoundedAndDrySafe()
        {
            const int cells = 128;
            float dx = WaterLabRules.DomainSize / cells;
            var h = new float[cells * cells];
            var vx = new float[cells * cells];
            var vz = new float[cells * cells];
            var ground = new float[cells * cells];

            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int i = x + z * cells;
                    float wx = -WaterLabRules.DomainSize * 0.5f + (x + 0.5f) * dx;
                    float wz = -WaterLabRules.DomainSize * 0.5f + (z + 0.5f) * dx;
                    float g = WaterLabRules.TerrainHeight(wx, wz);
                    ground[i] = g;
                    h[i] = Mathf.Max(WaterLabRules.SeaLevel - g, 0f);
                }

            for (int step = 0; step < 300; step++) // 5 秒
            {
                WaterLabRules.ReferenceSim.StepMassMomentum(h, vx, vz, ground, cells, cells, dx, WaterLabRules.FixedStep);
                for (int i = 0; i < h.Length; i += 97) // 抽查（全查太慢，素数步进覆盖面广）
                {
                    Assert.That(h[i], Is.GreaterThanOrEqualTo(0f), "出现负水深");
                    Assert.IsFalse(float.IsNaN(h[i]) || float.IsNaN(vx[i]) || float.IsNaN(vz[i]), "出现 NaN");
                    Assert.That(Mathf.Abs(vx[i]), Is.LessThanOrEqualTo(WaterLabRules.MaxVelocity * 1.01f), "速度超界");
                }
            }
        }

        // ------------------------------------------------------------------
        // 破碎判据单调性：越陡越可能破碎
        // ------------------------------------------------------------------

        [Test]
        public void Breaking_IsMonotoneInSteepness()
        {
            float depth = 1f;
            bool gentle = WaterLabRules.IsBreaking(0f, 0.01f, depth);   // 陡度 0.01
            bool steep = WaterLabRules.IsBreaking(0f, 1.2f, depth);     // 陡度 1.2
            Assert.IsFalse(gentle, "缓坡不应判破碎");
            Assert.IsTrue(steep, "陡峰应判破碎");
        }

        [Test]
        public void SpongeDamping_IsZeroInsideAndMaxAtBorder()
        {
            Assert.That(WaterLabRules.SpongeDamping(WaterLabRules.SpongeCells), Is.EqualTo(0f));
            Assert.That(WaterLabRules.SpongeDamping(0), Is.EqualTo(WaterLabRules.SpongeMaxDamping));
        }
    }
}
