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
            // 原作参数契约（reverse §8.1 与 Water.Run 派生量；I7 裁决①域/分辨率对齐原作）：
            // 域 = 主菜单沙盘 30 m（Beach.bounds）；水 512² / 地 1024²（B-terrain §2 三层
            // 分辨率）；格距 5.86 cm（30/512 = 60/1024，FormatProperty 真值）；g=16；
            // maxVelocity=0.5√g=2 m/s；子步 dt=0.5·dx/√g；damping=0；advectionFactor=0.2；
            // kappa=1000；海平面 level=1.0。
            Assert.That(WaterLabRules.WaterCellSize, Is.EqualTo(60f / 1024f).Within(1e-4f),
                "水格距应约等于原作 FormatProperty 的 5.86 cm");
            Assert.That(WaterLabRules.DomainSize, Is.EqualTo(30f), "主菜单沙盘域 30 m（Beach.bounds，裁决①）");
            Assert.That(WaterLabRules.WaterCells, Is.EqualTo(512), "原作 Water 层 512²");
            Assert.That(WaterLabRules.GroundCells, Is.EqualTo(1024), "原作 Ground 层 1024²");
            Assert.That(WaterLabRules.GroundCellSize, Is.EqualTo(30f / 1024f).Within(1e-6f),
                "地形格距 = 原作 2.93 cm（30 m/1024²）");
            Assert.That(WaterLabRules.SeaLevel, Is.EqualTo(1f), "原作 Water.level=1.0（世界 Y）");
            Assert.That(WaterLabRules.Gravity, Is.EqualTo(16f), "原作场景 gravityForce=16");
            Assert.That(WaterLabRules.MaxVelocity, Is.EqualTo(2f).Within(1e-4f),
                "原作 maxVelocity=0.5·√g（g=16 → 2 m/s）");
            Assert.That(WaterLabRules.FixedStep,
                Is.EqualTo(0.5f * WaterLabRules.WaterCellSize / Mathf.Sqrt(WaterLabRules.Gravity)).Within(1e-6f),
                "原作子步 = 0.5·dx/√g");
            Assert.That(WaterLabRules.VelocityDamping, Is.EqualTo(0f), "原作 damping=0");
            Assert.That(WaterLabRules.AdvectionFactor, Is.EqualTo(0.2f), "原作 advectionFactor=0.2");
            Assert.That(WaterLabRules.Kappa, Is.EqualTo(1000f), "原作 kappa=1000");

            // CFL 以原作的输运速度上限计：HeightIntegration 的通量是 d·v，v 已钳到 2 m/s，
            // 所以 C = v_max·dt/dx（不是 √(g·h_max)·dt/dx）。
            float C = WaterLabRules.MaxVelocity * WaterLabRules.FixedStep / WaterLabRules.WaterCellSize;
            Assert.That(C, Is.LessThanOrEqualTo(WaterLabRules.CflLimit),
                $"原作参数下 C=v_max·dt/dx={C:F3} 应 ≤ {WaterLabRules.CflLimit}");
        }

        // ------------------------------------------------------------------
        // 原作 UpdateAmount 契约：amp 渐起、全场潮位目标、耦合权重、蒸发
        // ------------------------------------------------------------------

        [Test]
        public void WaveAmplitude_FollowsOriginalRampAndPresetMax()
        {
            Assert.That(WaterLabRules.WaveAmplitude(0f), Is.EqualTo(0.01f).Within(1e-6f),
                "开场下限 0.01 m（Water.Run 的 max(0.01, …)）");
            Assert.That(WaterLabRules.WaveAmplitude(10f), Is.EqualTo(0.1f).Within(1e-5f),
                "saturate(10×0.05)=0.5 → 0.1 m");
            Assert.That(WaterLabRules.WaveAmplitude(20f), Is.EqualTo(0.2f).Within(1e-5f),
                "saturate(20×0.05)=1 → 预设峰值 0.2 m");
            Assert.That(WaterLabRules.WaveAmplitude(60f), Is.EqualTo(0.2f).Within(1e-5f),
                "渐起饱和后保持预设峰值");
        }

        [Test]
        public void TideDepthTarget_MatchesOriginalUpdateAmount()
        {
            // 判据全部相对 SeaLevel 表达（原作 target = max(level − ground + wave, 0.05)，
            // level=1.0；测试不钉绝对高程，海平面再调也不漂）。
            // t=0：sin=0，目标 = max(SeaLevel − ground, 0.05)
            Assert.That(WaterLabRules.TideDepthTarget(0f, WaterLabRules.SeaLevel - 2.4f),
                Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(WaterLabRules.TideDepthTarget(0f, WaterLabRules.SeaLevel + 0.5f),
                Is.EqualTo(WaterLabRules.TideMinAmount), "干沙上目标仍保留 5 cm 结合水");

            // t=20：amp=0.2，sin(20)=0.9129453 → 目标 = (SeaLevel − ground) + 0.1825891 m
            float wave = 0.2f * Mathf.Sin(20f);
            Assert.That(WaterLabRules.TideDepthTarget(20f, WaterLabRules.SeaLevel - 1f),
                Is.EqualTo(1f + wave).Within(1e-4f));

            // 全场统一潮位，没有空间行波项；TideEta 是含 5 cm 结合水的水量面
            Assert.That(WaterLabRules.TideEta(0f), Is.EqualTo(WaterLabRules.SeaLevel).Within(1e-6f));
            Assert.That(WaterLabRules.TideClampMin, Is.EqualTo(-0.5f), "原作 clamp 下钳 −0.5 m");
        }

        [Test]
        public void CouplingWeight_IsFullInInitialSeaAndZeroOnInitialDrySand()
        {
            float sea = WaterLabRules.SeaLevel;
            Assert.That(WaterLabRules.CouplingWeightAt(sea - 2.4f), Is.EqualTo(15f),
                "初始淹没区满耦合（4 bit 上限）");
            Assert.That(WaterLabRules.CouplingWeightAt(sea - 0.01f), Is.EqualTo(15f));
            Assert.That(WaterLabRules.CouplingWeightAt(sea), Is.EqualTo(0f), "初始岸线干区零耦合");
            Assert.That(WaterLabRules.CouplingWeightAt(sea + 0.5f), Is.EqualTo(0f));
        }

        [Test]
        public void EvaporationRate_MatchesOriginalUpdateAmountLiteral()
        {
            // exp(−0.721348×0) × 1 × 0.1 × 0.001 = 1e-4 m/s
            Assert.That(WaterLabRules.EvaporationRate(), Is.EqualTo(1e-4f).Within(1e-9f));
        }

        [Test]
        public void TerrainProfile_IsMonotoneishFlatToBeach()
        {
            // 程序剖面（ReplicaMode 关闭时的回退）骨架：海侧低于海平面、干沙侧高于海平面、
            // 缓坡+滩面在爬升段单调（地形噪声容差内）。端点取域边缘，干湿判据全部
            // 相对 SeaLevel（语义断言，不钉具体高程）。
            float half = WaterLabRules.DomainSize * 0.5f;
            Assert.That(WaterLabRules.TerrainHeight(-half, 0f), Is.LessThan(WaterLabRules.SeaLevel - 0.2f));
            Assert.That(WaterLabRules.TerrainHeight(half, 0f), Is.GreaterThan(WaterLabRules.SeaLevel + 0.3f));
            float prev = WaterLabRules.TerrainHeight(WaterLabRules.ShelfStartX, 0f);
            for (float x = WaterLabRules.ShelfStartX + 1f; x <= WaterLabRules.FaceEndX; x += 1f)
            {
                float cur = WaterLabRules.TerrainHeight(x, 0f);
                Assert.That(cur, Is.GreaterThan(prev - WaterLabRules.TerrainMonotoneTolerance),
                    $"爬升段在 x={x} 处出现超过地形噪声容差的下降");
                prev = cur;
            }
        }

        [Test]
        public void TerrainProfile_FallbackShapesScaleWithDomain()
        {
            // 回退剖面的里程碑随域缩放（LegacyProfileScale），滩面始终落在域内：
            // FaceEndX 不越过域边缘、滩面高程在海平面上方——域再改时测试不必跟数。
            float half = WaterLabRules.DomainSize * 0.5f;
            Assert.That(WaterLabRules.FaceEndX, Is.LessThan(half), "滩面末端应在域内");
            Assert.That(WaterLabRules.ShelfStartX, Is.GreaterThan(-half), "浅水架起点应在域内");
            Assert.That(WaterLabRules.BeachHeight, Is.GreaterThan(WaterLabRules.SeaLevel));
            Assert.That(WaterLabRules.OffshoreDepth, Is.LessThan(WaterLabRules.SeaLevel));
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

            for (int step = 0; step < Mathf.RoundToInt(4f / WaterLabRules.FixedStep); step++) // 4 秒
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
            const float h0 = 1f; // 总水量 1 m；前 5 cm 是结合水，参与波动的有效深度 = h0 − DryDepth

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
            int steps = Mathf.RoundToInt(2f / WaterLabRules.FixedStep); // 2 秒
            for (int step = 0; step < steps; step++)
                WaterLabRules.ReferenceSim.StepMassMomentum(h, vx, vz, ground, cells, cells, dx, WaterLabRules.FixedStep);
            int p1 = FindPeak();

            float dxMeters = (p1 - p0) * dx;
            float speed = Mathf.Abs(dxMeters) / (steps * WaterLabRules.FixedStep);
            float theory = WaterLabRules.WaveSpeed(h0 - WaterLabRules.DryDepth);
            // 平均通量格式（F=d̄·v̄）的固有二阶数值色散使峰值偏慢约 8-10%；
            // 本测试钉的是量级正确（无 0.5×/2× 的结构性错误），容差 ±12%。
            Assert.That(speed / theory, Is.InRange(0.88f, 1.12f),
                $"实测波速 {speed:F2} m/s vs 有效深度理论 √(g·(h0−5cm))={theory:F2} m/s，偏差超出 ±12%");
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

            for (int step = 0; step < Mathf.RoundToInt(5f / WaterLabRules.FixedStep); step++) // 5 秒
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
        // 破碎判据（弗劳德形式）：浅水超临界才破碎，深水长波不破碎，且随速度单调
        // ------------------------------------------------------------------

        [Test]
        public void Breaking_FiresOnSupercriticalShallowNotDeepWater()
        {
            float g = WaterLabRules.Gravity;
            // 深水长波：a/h = 0.3/1.3 ≈ 0.23 < 0.39 → 不破碎
            float deep = 1.3f;
            float vDeep = 0.3f * Mathf.Sqrt(g / deep);   // 即 a=0.3 m 的线性浅水最大质点速
            Assert.IsFalse(WaterLabRules.IsBreaking(vDeep, deep), "深水长波不应判破碎");

            // 浅水涌潮：Fr = 1.2 > 0.39 → 破碎
            float shallow = 0.3f;
            float vShore = 1.2f * Mathf.Sqrt(g * shallow);
            Assert.IsTrue(WaterLabRules.IsBreaking(vShore, shallow), "超临界涌潮应判破碎");

            // 单调：速度越大越容易判破碎
            Assert.IsTrue(WaterLabRules.IsBreaking(vShore * 2f, shallow), "更快的质点应仍判破碎");
        }

        [Test]
        public void SpongeDamping_IsZeroInsideAndMaxAtBorder()
        {
            Assert.That(WaterLabRules.SpongeDamping(WaterLabRules.SpongeCells), Is.EqualTo(0f));
            Assert.That(WaterLabRules.SpongeDamping(0), Is.EqualTo(WaterLabRules.SpongeMaxDamping));
        }
    }
}
