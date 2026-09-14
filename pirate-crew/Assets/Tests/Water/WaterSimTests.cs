using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="WaterSimRules"/> + <see cref="WaterWaveField2D"/> 测试：
    /// CFL 稳定域、海绵吸收有效（能量不爆）、波传播速度与 c 一致、障碍反射（阴影区）、
    /// 泡沫生成/衰减单调、同序列确定性、128² 帧预算实测。
    /// </summary>
    [TestFixture]
    public class WaterSimTests
    {
        const float Eps = 1e-6f;

        // ------------------------------------------------------------------
        // 纯规则
        // ------------------------------------------------------------------

        [Test]
        public void Cfl_DefaultConfig_IsStableAt60Hz()
        {
            WaterFieldConfig cfg = WaterFieldConfig.Default;
            float dt = 1f / 60f;
            float cfl = WaterSimRules.CflNumber(cfg.WaveSpeed, dt, cfg.Dx);

            // 格 1→2 单位后默认域格距 Dx = 128/128 = 1.0 世界单位（旧 0.5），
            // 波速 c=9 不变 → C = 9×(1/60)/1.0 = 0.15，仍远低于 CflLimit ≈ 0.495（数值验证稳定）。
            Assert.AreEqual(0.15f, cfl, 0.005f);
            Assert.IsTrue(WaterSimRules.IsStable(cfg.WaveSpeed, dt, cfg.Dx));
            Assert.IsTrue(cfl <= WaterSimRules.CflLimit);
        }

        [Test]
        public void Cfl_RejectsUnstableStep()
        {
            // c=9, dx=0.5, dt=0.06 → C=1.08 ≫ 0.495。
            Assert.IsFalse(WaterSimRules.IsStable(9f, 0.06f, 0.5f));
        }

        [Test]
        public void MaxStableDt_IsTightBoundary()
        {
            float dtMax = WaterSimRules.MaxStableDt(9f, 0.5f);
            Assert.AreEqual(WaterSimRules.CflLimit * 0.5f / 9f, dtMax, 1e-6f);
            Assert.IsTrue(WaterSimRules.IsStable(9f, dtMax, 0.5f));
            Assert.IsFalse(WaterSimRules.IsStable(9f, dtMax * 1.05f, 0.5f));
        }

        [Test]
        public void SpongeSigma_StrongestAtBorderAndMonotonic()
        {
            Assert.AreEqual(0.06f, WaterSimRules.SpongeSigma(0, 10, 0.06f), 1e-6f);
            Assert.AreEqual(0f, WaterSimRules.SpongeSigma(10, 10, 0.06f), 1e-6f);
            Assert.AreEqual(0f, WaterSimRules.SpongeSigma(50, 10, 0.06f), 1e-6f);

            float prev = float.MaxValue;
            for (int d = 0; d <= 12; d++)
            {
                float s = WaterSimRules.SpongeSigma(d, 10, 0.06f);
                Assert.That(s, Is.InRange(0f, 0.06f));
                Assert.That(s, Is.LessThanOrEqualTo(prev + Eps));
                prev = s;
            }
        }

        [Test]
        public void FoamGeneration_ThresholdAndMonotonic()
        {
            Assert.AreEqual(0f, WaterSimRules.FoamGeneration(0.01f, 0.02f, 4f), Eps);
            Assert.AreEqual(0f, WaterSimRules.FoamGeneration(0.02f, 0.02f, 4f), Eps);
            // (0.5 - 0.02) * 4 = 1.92
            Assert.AreEqual(1.92f, WaterSimRules.FoamGeneration(0.5f, 0.02f, 4f), 1e-4f);

            float prev = -1f;
            for (float c = 0f; c <= 1f; c += 0.05f)
            {
                float g = WaterSimRules.FoamGeneration(c, 0.02f, 4f);
                Assert.That(g, Is.GreaterThanOrEqualTo(prev - Eps));
                prev = g;
            }
        }

        [Test]
        public void FoamDecayStep_IsExponentialAndMonotonic()
        {
            Assert.AreEqual(0.99f, WaterSimRules.FoamDecayStep(1f, 0.99f), 1e-5f);
            Assert.AreEqual(0.9801f, WaterSimRules.FoamDecayStep(0.99f, 0.99f), 1e-5f);
            Assert.That(WaterSimRules.FoamDecayStep(1f, 1f), Is.EqualTo(1f).Within(Eps));
        }

        [Test]
        public void DomainUv_RoundTrips()
        {
            var center = new Vector2(25f, 8.5f);
            const float size = 64f;

            Vector2 uv = WaterSimRules.WorldToDomainUv(center, center, size);
            Assert.AreEqual(0.5f, uv.x, 1e-5f);
            Assert.AreEqual(0.5f, uv.y, 1e-5f);

            var world = new Vector2(25f + 16f, 8.5f - 16f);
            uv = WaterSimRules.WorldToDomainUv(world, center, size);
            Assert.AreEqual(0.75f, uv.x, 1e-5f);
            Assert.AreEqual(0.25f, uv.y, 1e-5f);

            Vector2 back = WaterSimRules.DomainUvToWorld(uv, center, size);
            Assert.That(Vector2.Distance(back, world), Is.LessThan(1e-3f));
        }

        // ------------------------------------------------------------------
        // 场：稳定 / 能量 / 传播 / 反射 / 泡沫 / 确定性
        // ------------------------------------------------------------------

        static WaterFieldConfig SmallFreeField(int cells, float c, int sponge)
        {
            var cfg = WaterFieldConfig.Default;
            cfg.CellsX = cells;
            cfg.CellsZ = cells;
            cfg.Dx = 1f;
            cfg.WaveSpeed = c;
            cfg.BaseDamping = 0f;
            cfg.SpongeCells = sponge;
            cfg.SpongeSigmaMax = 0.06f;
            return cfg;
        }

        [Test]
        public void Field_ThrowsWhenStepViolatesCfl()
        {
            var field = new WaterWaveField2D(WaterFieldConfig.Default);
            Assert.Throws<InvalidOperationException>(() => field.Step(1f));
        }

        [Test]
        public void Field_EnergyDoesNotGrow()
        {
            var cfg = SmallFreeField(64, 8f, 6);
            var field = new WaterWaveField2D(cfg);
            const float dt = 1f / 60f;

            field.InjectGaussian(0.5f, 0.5f, 3f / 64f, 1f);
            float e0 = field.ComputeEnergy(dt);
            Assert.That(e0, Is.GreaterThan(0f));

            for (int i = 0; i < 600; i++)
                field.Step(dt);

            float e1 = field.ComputeEnergy(dt);
            Assert.That(e1, Is.LessThanOrEqualTo(e0 * 1.02f),
                $"能量增长（{e0} → {e1}）：海绵吸收或离散格式失真");
        }

        [Test]
        public void Field_SpongeAbsorbsInsteadOfReflecting()
        {
            var cfg = SmallFreeField(64, 8f, 10);
            cfg.BaseDamping = 0f;
            cfg.SpongeSigmaMax = 0.10f;
            var field = new WaterWaveField2D(cfg);
            const float dt = 1f / 60f;

            field.InjectGaussian(0.5f, 0.5f, 2f / 64f, 1f);
            float e0 = field.ComputeEnergy(dt);
            Assert.That(e0, Is.GreaterThan(0f));

            for (int i = 0; i < 900; i++)
                field.Step(dt); // 15s，足够走完全场并被海绵吃掉

            float e = field.ComputeEnergy(dt);
            Assert.That(e, Is.LessThan(e0 * 0.10f),
                $"海绵层吸收不足：残余 {e} vs 初始 {e0}（比值 {e / Mathf.Max(e0, 1e-9f):P1}）");
        }

        static float CellCenterUv(int cell, int cells) => (cell + 0.5f) / cells;

        [Test]
        public void Field_WavefrontTravelsAtConfiguredSpeed()
        {
            const int cells = 121;
            const float c = 10f;
            const float dt = 0.04f; // C = 0.4 ≤ 0.495
            var cfg = SmallFreeField(cells, c, 0);
            var field = new WaterWaveField2D(cfg);

            int center = cells / 2;
            field.InjectGaussian(CellCenterUv(center, cells), CellCenterUv(center, cells), 3f / cells, 1f);

            const int nearOffset = 15;
            const int farOffset = 35;
            float uNear = CellCenterUv(center + nearOffset, cells);
            float uFar = CellCenterUv(center + farOffset, cells);
            const float threshold = 1e-3f;

            float arrivalNear = -1f, arrivalFar = -1f;
            for (int step = 0; step < 400; step++)
            {
                field.Step(dt);
                float t = (step + 1) * dt;

                if (arrivalNear < 0f && Mathf.Abs(field.HeightAt(uNear, 0.5f)) > threshold)
                    arrivalNear = t;
                if (arrivalFar < 0f && Mathf.Abs(field.HeightAt(uFar, 0.5f)) > threshold)
                    arrivalFar = t;

                if (arrivalNear > 0f && arrivalFar > 0f)
                    break;
            }

            Assert.That(arrivalNear, Is.GreaterThan(0f), "近传感器未收到波");
            Assert.That(arrivalFar, Is.GreaterThan(0f), "远传感器未收到波");

            float measured = arrivalFar - arrivalNear;
            float expected = (farOffset - nearOffset) / c; // = 2.0s
            Assert.That(measured, Is.EqualTo(expected).Within(expected * 0.30f),
                $"波前速度偏离：实测 Δt={measured:F3}s，期望 {expected:F3}s（c={c}）");
        }

        [Test]
        public void Field_ObstacleCreatesShadowBehindWall()
        {
            const int cells = 81;
            const float dt = 0.04f;
            var cfg = SmallFreeField(cells, 10f, 4);
            var field = new WaterWaveField2D(cfg);

            const int wallCx = 60;
            for (int cz = 0; cz < cells; cz++)
                field.SetObstacle(wallCx, cz, true);

            field.InjectGaussian(CellCenterUv(20, cells), CellCenterUv(40, cells), 3f / cells, 1f);

            for (int i = 0; i < 300; i++)
                field.Step(dt);

            float incident = MaxAbsOverColumns(field, 40, 55, cells);
            float shadow = MaxAbsOverColumns(field, 66, 76, cells);

            Assert.That(incident, Is.GreaterThan(0.01f), "入射侧能量异常低");
            Assert.That(shadow, Is.LessThan(incident * 0.10f),
                $"障碍墙后出现透射（阴影 {shadow} vs 入射 {incident}），诺伊曼反射未生效");
        }

        static float MaxAbsOverColumns(WaterWaveField2D field, int cx0, int cx1, int cells)
        {
            float max = 0f;
            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                    max = Mathf.Max(max, Mathf.Abs(field.HeightAtCell(cx, cz)));
            }
            return max;
        }

        [Test]
        public void Field_FoamDecaysMonotonicallyWithoutGeneration()
        {
            var cfg = SmallFreeField(48, 1f, 0);
            cfg.FoamSpread = 0f;
            cfg.FoamDecay = 0.99f;
            cfg.FoamThreshold = 1e9f; // 关掉曲率生成，只测衰减
            var field = new WaterWaveField2D(cfg);

            field.AddFoam(24, 24, 1f);
            float prev = field.FoamAtCell(24, 24);
            Assert.That(prev, Is.GreaterThan(0.9f));

            for (int i = 0; i < 60; i++)
            {
                field.Step(1f / 60f);
                float f = field.FoamAtCell(24, 24);
                Assert.That(f, Is.LessThan(prev + Eps), "泡沫未单调衰减");
                prev = f;
            }

            Assert.That(prev, Is.LessThan(0.6f), "60 步后泡沫衰减过慢");
        }

        [Test]
        public void Field_CrestCurvatureGeneratesFoam()
        {
            const int cells = 64;
            var cfg = SmallFreeField(cells, 8f, 4);
            cfg.FoamThreshold = 0.001f;
            cfg.FoamGain = 4f;
            var field = new WaterWaveField2D(cfg);

            field.InjectGaussian(0.5f, 0.5f, 3f / cells, 0.2f);
            for (int i = 0; i < 30; i++)
                field.Step(1f / 60f);

            float maxFoam = 0f;
            for (int i = 0; i < field.RawFoam.Length; i++)
                maxFoam = Mathf.Max(maxFoam, field.RawFoam[i]);

            Assert.That(maxFoam, Is.GreaterThan(0f), "浪峰未生成泡沫");
        }

        [Test]
        public void Field_SameInputsAreDeterministic()
        {
            var cfg = SmallFreeField(64, 9f, 5);
            var a = new WaterWaveField2D(cfg);
            var b = new WaterWaveField2D(cfg);

            const float dt = 1f / 60f;
            a.InjectGaussian(0.3f, 0.6f, 0.05f, 0.5f);
            b.InjectGaussian(0.3f, 0.6f, 0.05f, 0.5f);

            for (int i = 0; i < 120; i++)
            {
                a.Step(dt);
                b.Step(dt);
            }

            for (int i = 0; i < a.RawHeights.Length; i++)
            {
                Assert.AreEqual(a.RawHeights[i], b.RawHeights[i],
                    $"第 {i} 格高度不同：非确定性（可能与并行/浮点顺序有关）");
            }
        }

        [Test]
        public void Field_EdgeSwellRadiatesInward()
        {
            var cfg = SmallFreeField(64, 8f, 4);
            var field = new WaterWaveField2D(cfg);
            const float dt = 1f / 60f;
            const float amplitude = 0.02f;

            for (int i = 0; i < 200; i++)
            {
                float phase = Mathf.PI * 2f * (i * dt) / 6f;
                field.InjectEdgeSwell(0, amplitude, phase);
                field.Step(dt);
            }

            // 边缘内侧附近应有非零起伏（波已向内辐射）。
            float nearEdge = 0f;
            for (int cx = 0; cx < 64; cx++)
                nearEdge = Mathf.Max(nearEdge, Mathf.Abs(field.HeightAtCell(cx, 12)));
            Assert.That(nearEdge, Is.GreaterThan(1e-4f), "边缘涌浪未向内辐射");
        }

        // ------------------------------------------------------------------
        // 帧预算（实测，写进报告）
        // ------------------------------------------------------------------

        [Test]
        public void Field_PerformanceBudget128()
        {
            var field = new WaterWaveField2D(WaterFieldConfig.Default);
            const float dt = 1f / 60f;

            // 造一点扰动，别让更新是"全零空转"。
            field.InjectGaussian(0.5f, 0.5f, 0.05f, 0.1f);
            field.InjectGaussian(0.3f, 0.7f, 0.05f, 0.1f);

            for (int i = 0; i < 20; i++)
                field.Step(dt); // 预热

            const int steps = 300;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < steps; i++)
                field.Step(dt);
            sw.Stop();

            double msPerStep = sw.Elapsed.TotalMilliseconds / steps;
            Console.WriteLine($"[WaterSim] 128×128 每步 {msPerStep:F3} ms（含泡沫），" +
                              $"60Hz 占帧 {msPerStep / (1000.0 / 60.0) * 100.0:F2}%");

            Assert.That(msPerStep, Is.LessThan(8.0),
                $"128² 每步 {msPerStep:F3} ms，超过 8ms 预算");
        }
    }
}
