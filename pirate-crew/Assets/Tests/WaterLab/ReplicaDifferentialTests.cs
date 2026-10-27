using System;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using PirateCrew.WaterLab;
using UnityEngine;

namespace PirateCrew.WaterLab.Tests
{
    /// <summary>
    /// L1 模拟层差分恒等测试（验证协议「差分恒等门禁」L1 行，交接档 §4）：
    /// 原作侧真值 = frida 只读 dump 的主菜单 readbackCells 全场对
    ///（<c>external/sandcastle-ref/rt/dumps/menu-snapshot/dump1.json + dump2.json</c>，
    /// 间隔 22.29 s 的两次全场 readback）；我方侧 = <see cref="WaterLabRules.ReferenceSim"/>
    /// 按 compute 的完整调度序（子步 质量动量 → 海绵 → h 平流，帧末 SurfaceUpdate：
    /// 蒸发 → 潮位松弛 → 干滩渗透）同初始场 + 同常量 + 冻结地形快照步进同一时间窗，
    /// 与 dump2 逐格比对水位/eta，输出 p50/p90/p99/max 对照表。
    ///
    /// 【判据】p99(|ΔwaterAmount|) ≤ 0.02 m 且 max ≤ 0.15 m 记 PASS——「活沙漂移 +
    /// 潮位相位不可恢复」下的工程容差，理想恒等在 L2 抓帧后收紧；超出记 FAIL 并输出
    /// 差异最大的 10 个格坐标与双侧值（供回溯）。
    ///
    /// 【输入不可伪造】数据从外部路径真读（环境变量 PC3D_WATERLAB_DUMP_DIR / 程序集
    /// 目录向上爬升 / 逆向台固定落点三级定位）；读不到 Assert.Ignore 并注明跑法，
    /// 绝不内嵌替身数据。
    ///
    /// 【已知不确定度（显式写进报告，不掩盖）】
    ///  · 原作 4 bit 耦合权重场由关卡 water.Fill 初始水场写入、运行时才存在，静态不可
    ///    恢复（逆向档案 C 项）——本仓用「初始淹没满耦 / 干区零耦」静态代理（Rules 现值）。
    ///    满耦合格每帧 lerp 因子 1.0 = 硬钉潮位目标 → 海区波动结构无法复现。L1 预期在
    ///    海区暴露该语义缺口：这不是 SWE 核的错误，而是权重场缺真值，逐格恒等要等
    ///    L2 抓帧或 frida dump 权重场后重建。
    ///  · playTime 绝对相位不可恢复（dump 无该字段）→ 假设 dump1 相位 ≡ 0 (mod 2π)：
    ///    以 20π 相位偏移代入（sin 相位不变、WaveAmplitude 渐起已饱和取满幅 0.2）。
    ///  · dump 不含速度场 → v0 = 0（沿 I7 初值口径），速度无真值侧可比。
    ///  · readback 256² 对 512² 水场的采样算子（scaleBias 落点 2r+0.5：双线性块平均
    ///    vs 偶格点采）静态未定案 → 比对以块平均为主、偶格点采为辅各出一行。
    ///  · 地形冻结为 dump1 快照（门禁 L1 行口径）；原作 22 s 内活沙自漂 max 0.018 m
    ///    （snapshot_meta stability），按容差吸收。
    /// </summary>
    [TestFixture]
    public class ReplicaDifferentialTests
    {
        // ---- 判据（任务书口径：活沙+相位不确定性下的工程容差） ----
        const float PassP99 = 0.02f;
        const float PassMaxAbs = 0.15f;

        // ---- 几何 ----
        const int ReadbackRes = 256;   // 原作 Readback 层（dump 分辨率）
        const int SimRes = 512;        // 我方水域分辨率（Rules.WaterCells）
        const float PhaseOffset = 20f * Mathf.PI; // mod-2π 相位约定（见类注释），同时使 amp 渐起饱和

        // ---- 调度（沿 WaterLabSimulation.Update 生产语义） ----
        const float FrameDt = 1f / 60f;     // 帧率假设：潮位松弛对满耦合格一步收敛、对帧率不敏感（报告注明）
        const int MaxStepsPerFrame = 4;     // 生产默认 maxSteps = ceil(4·TimeScale)

        // ==================================================================
        // 常量防回归：差分步进前的静态前提（台账 §3 + 实验水体.md §2.4 现值）
        // ==================================================================

        [Test]
        public void ReplicaConstants_MatchLedgerBeforeDifferential()
        {
            Assert.AreEqual(30f, WaterLabRules.DomainSize, "域 = 主菜单沙盘 30 m（B-terrain）");
            Assert.AreEqual(512, WaterLabRules.WaterCells, "水层 512²（原作 FormatProperty）");
            Assert.AreEqual(16f, WaterLabRules.Gravity, "gravityForce=16（frida 运行时真值）");
            Assert.AreEqual(2f, WaterLabRules.MaxVelocity, 1e-4f, "maxVelocity = 0.5·√g = 2 m/s");
            Assert.AreEqual(0f, WaterLabRules.VelocityDamping, "damping=0（WaterProperty 四档真值）");
            Assert.AreEqual(0.2f, WaterLabRules.AdvectionFactor, "advectionFactor=0.2");
            Assert.AreEqual(1000f, WaterLabRules.Kappa, "kappa=1000（Regular 档）");
            Assert.AreEqual(1f, WaterLabRules.SeaLevel, "Water.level=1.0");
            Assert.AreEqual(0.05f, WaterLabRules.DryDepth, "结合水 5 cm（WATER_SATURATION_POINT）");
            Assert.AreEqual(0.1f, WaterLabRules.EvaporationFactor, "evaporationFactor=0.1（场景实例）");
            Assert.AreEqual(0.001f, WaterLabRules.EvaporationScale, "UpdateAmount 字面量 0.001");
            Assert.AreEqual(1e-4f, WaterLabRules.EvaporationRate(), 1e-9f, "蒸发速率 exp(0)·1·0.1·0.001");
            Assert.AreEqual(0.2f, WaterLabRules.WaveAmplitudeMax, "Regular 档潮幅峰值 0.2 m");
            Assert.AreEqual(-0.5f, WaterLabRules.TideClampMin, "潮位下钳 −0.5 m");
            Assert.AreEqual(0.05f, WaterLabRules.TideMinAmount, "目标水量下限 = 结合水");
            Assert.AreEqual(15f, WaterLabRules.CouplingWeightFull, "满耦合 = 4 bit 上限 15");
            Assert.AreEqual(0.5f * (30f / 512f) / Mathf.Sqrt(16f), WaterLabRules.FixedStep, 1e-9f,
                "子步 = 0.5·dx/√g（Water.MaxDeltaTime 口径）");
            Assert.AreEqual(10, WaterLabRules.SpongeCells, "海绵层 10 格（我方边界吸收近似，原作形式未知）");
        }

        // ==================================================================
        // 数据完整性：dump 对可解析、口径与 snapshot_meta.json 一致（快速）
        // ==================================================================

        [Test]
        public void DumpPair_LoadsAndMatchesSnapshotMeta()
        {
            string dir = LocateDumpDir();
            if (dir == null)
                Assert.Ignore(HowToRunMessage());

            DumpData d1 = LoadDump(System.IO.Path.Combine(dir, "dump1.json"));
            DumpData d2 = LoadDump(System.IO.Path.Combine(dir, "dump2.json"));

            float deltaSeconds = (d2.TimestampMs - d1.TimestampMs) / 1000f;
            Assert.That(deltaSeconds, Is.InRange(20f, 25f),
                $"dump1→dump2 时间差 {deltaSeconds:F3} s 应 ≈22.3 s（snapshot_meta timestampCaveat 口径）");
            TestContext.Progress.WriteLine($"[L1] Δt(dump1→dump2) = {deltaSeconds:F3} s");

            foreach (DumpData d in new[] { d1, d2 })
            {
                int n = ReadbackRes * ReadbackRes;
                Assert.AreEqual(ReadbackRes, d.Res, $"{d.Name}: resX 应为 256（Readback 层）");
                Assert.AreEqual(n, d.Water.Length, $"{d.Name}: waterAmount 场长度");
                Assert.AreEqual(n, d.Ground.Length, $"{d.Name}: groundHeight 场长度");
                Assert.AreEqual(n, d.Eta.Length, $"{d.Name}: eta 场长度");

                // eta 公式自检（Readback.cs:74 口径 eta = ground + max(water − 0.1, 0)）
                float etaErr = 0f;
                for (int i = 0; i < n; i++)
                {
                    float expect = d.Ground[i] + Mathf.Max(d.Water[i] - 0.1f, 0f);
                    etaErr = Mathf.Max(etaErr, Mathf.Abs(d.Eta[i] - expect));
                    Assert.IsFalse(float.IsNaN(d.Water[i]) || float.IsNaN(d.Ground[i]), $"{d.Name}: 场内有 NaN");
                }
                Assert.That(etaErr, Is.LessThanOrEqualTo(2e-5f),
                    $"{d.Name}: eta 公式残差 {etaErr:E2} 超限——解码或版本错位");

                Assert.That(d.WaterMin, Is.GreaterThanOrEqualTo(-1e-3f), $"{d.Name}: waterAmount 出现负值");
                Assert.That(d.WaterMax, Is.LessThanOrEqualTo(1.2f), $"{d.Name}: waterAmount 超量程");
                Assert.That(d.GroundMin, Is.GreaterThan(0f), $"{d.Name}: groundHeight 下限异常");
                Assert.That(d.GroundMax, Is.LessThan(3f), $"{d.Name}: groundHeight 上限异常");

                // 场内实例参数与 Rules 对账（防两个源漂移）
                Assert.AreEqual(WaterLabRules.SeaLevel, d.Level, "dump Water.level 与 Rules.SeaLevel 不一致");
                Assert.AreEqual(WaterLabRules.Gravity, d.Gravity, "dump gravityForce 与 Rules.Gravity 不一致");
                Assert.AreEqual(WaterLabRules.EvaporationFactor, d.EvaporationFactor, "dump evaporationFactor 不一致");
            }

            TestContext.Progress.WriteLine(
                $"[L1] dump1 water[{d1.WaterMin:F3}..{d1.WaterMax:F3}] ground[{d1.GroundMin:F3}..{d1.GroundMax:F3}]；" +
                $"dump2 water[{d2.WaterMin:F3}..{d2.WaterMax:F3}] ground[{d2.GroundMin:F3}..{d2.GroundMax:F3}]");
        }

        // ==================================================================
        // L1 主测试：dump1 初始化 → 22.29 s 原作调度序步进 → 与 dump2 逐格比对
        // ==================================================================

        [Test]
        public void Differential_Dump1ToDump2_22s_WaterFieldIdentity()
        {
            string dir = LocateDumpDir();
            if (dir == null)
                Assert.Ignore(HowToRunMessage());

            DumpData d1 = LoadDump(System.IO.Path.Combine(dir, "dump1.json"));
            DumpData d2 = LoadDump(System.IO.Path.Combine(dir, "dump2.json"));
            float deltaSeconds = (d2.TimestampMs - d1.TimestampMs) / 1000f;
            Assert.That(deltaSeconds, Is.InRange(20f, 25f), "dump 对时间差应 ≈22.3 s");

            // 生产快照接线：Load 顺带做 B §5.2 翻转自检（轴写反当场抛），用完卸载防跨 fixture 残留
            WaterLabReplicaSnapshot.Load(d1.GroundBytes, d1.WaterBytes);
            float[] ground256;
            try
            {
                ground256 = WaterLabReplicaSnapshot.GroundData;
                Assert.IsNotNull(WaterLabReplicaSnapshot.WaterData);

                // ---- 我方侧初始化（I7 口径：h=waterAmount、v=0、地形=冻结快照）----
                int n = SimRes * SimRes;
                var h = new float[n];
                var vx = new float[n];
                var vz = new float[n];
                var ground = new float[n];
                BlockReplicate256to512(d1.Water, h);
                FillGround512(ground256, ground);

                // ---- 步进：帧循环（1/60 s 假设帧）+ 固定子步累加器 + 帧末 SurfaceUpdate ----
                // 子步序与 compute/WaterLabSimulation.StepOnce 同构：
                //   质量动量（ReferenceSim 核）→ 海绵（VelocityIntegration 尾段）→ h 平流（AdvectFoam h 通道）
                float fixedStep = WaterLabRules.FixedStep;
                float cellW = WaterLabRules.WaterCellSize;
                var hScratch = new float[n];  // 质量步 scratch，用完即平流读副本（内容 == 步进后 h，省一次拷贝）
                var vxScratch = new float[n];
                var vzScratch = new float[n];
                float advCoeff = WaterLabRules.AdvectionFactor * fixedStep / cellW;
                float acc = 0f;
                float simTime = 0f;
                long substeps = 0;
                int frames = 0;

                while (simTime + acc < deltaSeconds - 1e-5f && frames < 100000)
                {
                    float dtFrame = Mathf.Min(FrameDt, deltaSeconds - simTime - acc);
                    acc += dtFrame;

                    int steps = 0;
                    while (acc >= fixedStep && steps < MaxStepsPerFrame)
                    {
                        // (1) 质量+动量（ReferenceSim 核，scratch 复用防 LOH 分配）
                        WaterLabRules.ReferenceSim.StepMassMomentum(
                            h, vx, vz, ground, SimRes, SimRes, cellW, fixedStep, hScratch, vxScratch, vzScratch);

                        // (2) 海绵层（z 两侧，compute VelocityIntegration 尾段语义；x 两侧原作形式未知）
                        for (int cz = 0; cz < SimRes; cz++)
                        {
                            int bd = Mathf.Min(cz, SimRes - 1 - cz);
                            if (bd >= WaterLabRules.SpongeCells) continue;
                            float damp = Mathf.Exp(-WaterLabRules.SpongeDamping(bd) * fixedStep);
                            int row = cz * SimRes;
                            for (int cx = 0; cx < SimRes; cx++)
                            {
                                vx[row + cx] *= damp;
                                vz[row + cx] *= damp;
                            }
                        }

                        // (3) 半拉格朗日 h 平流（AdvectFoam h 通道）：back = c − v·advFactor·dt/dx，
                        //     双线性读 hScratch（== 步进后 h 的副本，同 compute 读 A 写 B），整场替换
                        for (int cz = 0; cz < SimRes; cz++)
                        {
                            int row = cz * SimRes;
                            for (int cx = 0; cx < SimRes; cx++)
                            {
                                float bx = Mathf.Clamp(cx - vx[row + cx] * advCoeff, 0f, SimRes - 1);
                                float bz = Mathf.Clamp(cz - vz[row + cx] * advCoeff, 0f, SimRes - 1);
                                int x0 = (int)bx, z0 = (int)bz; // bx,bz ≥ 0，cast 即 floor
                                int x1 = Mathf.Min(x0 + 1, SimRes - 1);
                                int z1 = Mathf.Min(z0 + 1, SimRes - 1);
                                float tx = bx - x0, tz = bz - z0;
                                int i00 = z0 * SimRes + x0;
                                int i01 = z0 * SimRes + x1;   // x0 钳到 SimRes−1 时 x1 == x0，不许越界 +1（同 compute min(h0+1, Cells−1)）
                                int i10 = z1 * SimRes + x0;
                                int i11 = z1 * SimRes + x1;
                                h[row + cx] = Mathf.Lerp(
                                    Mathf.Lerp(hScratch[i00], hScratch[i01], tx),
                                    Mathf.Lerp(hScratch[i10], hScratch[i11], tx), tz);
                            }
                        }

                        simTime += fixedStep;
                        acc -= fixedStep;
                        substeps++;
                        steps++;
                    }
                    if (steps == MaxStepsPerFrame)
                        acc = 0f; // 追帧雪崩保护（生产同款；1/60 帧不会触顶）

                    UpdateSurface(h, ground, simTime, dtFrame);
                    frames++;
                }

                // ---- 比对：我方 512² → readback 256²（块平均），与 dump2 逐格 ----
                var ours256 = new float[ReadbackRes * ReadbackRes];
                DownsampleBox2x512to256(h, ours256);

                var absWater = new float[ReadbackRes * ReadbackRes];
                var absWaterPoint = new float[ReadbackRes * ReadbackRes];
                var absEta = new float[ReadbackRes * ReadbackRes];
                var strataSea = new System.Collections.Generic.List<int>();
                var strataSwash = new System.Collections.Generic.List<int>();
                var strataDry = new System.Collections.Generic.List<int>();
                float maxV = 0f;
                bool finite = true;
                for (int cz = 0; cz < ReadbackRes; cz++)
                {
                    for (int cx = 0; cx < ReadbackRes; cx++)
                    {
                        int r = cz * ReadbackRes + cx;
                        absWater[r] = Mathf.Abs(ours256[r] - d2.Water[r]);
                        absWaterPoint[r] = Mathf.Abs(h[(2 * cz) * SimRes + 2 * cx] - d2.Water[r]);
                        float etaOurs = d1.Ground[r] + Mathf.Max(ours256[r] - 0.1f, 0f);
                        absEta[r] = Mathf.Abs(etaOurs - d2.Eta[r]);

                        float g1 = d1.Ground[r];
                        (g1 < 0.9f ? strataSea : g1 < 1.3f ? strataSwash : strataDry).Add(r);

                        int wi = (2 * cz) * SimRes + 2 * cx;
                        float sp = Mathf.Sqrt(vx[wi] * vx[wi] + vz[wi] * vz[wi]);
                        if (sp > maxV) maxV = sp;
                        finite &= !float.IsNaN(ours256[r]) && !float.IsNaN(vx[wi]) && h[wi] >= 0f;
                    }
                }
                Assert.IsTrue(finite, "22 s 步进后出现 NaN/负水深——数值保护失效");
                Assert.That(maxV, Is.LessThanOrEqualTo(WaterLabRules.MaxVelocity * 1.01f),
                    $"速度场超限 max|v|={maxV:F3}（限幅 {WaterLabRules.MaxVelocity} 失效？）");

                // ---- 对照表 ----
                var baseline = new float[ReadbackRes * ReadbackRes];
                for (int i = 0; i < baseline.Length; i++)
                    baseline[i] = Mathf.Abs(d2.Water[i] - d1.Water[i]);

                var sb = new StringBuilder();
                sb.AppendLine("== L1 模拟层差分恒等：dump1 → [Δt] → dump2 ==");
                sb.AppendLine($"[数据] Δt={deltaSeconds:F3} s；readback 256²；我方 sim 512² dx={cellW:F5} dt={fixedStep:F6}；" +
                              $"实际步进 {substeps} 子步（{simTime:F3} s）/ {frames} 帧");
                sb.AppendLine("[假设] playTime 相位≡0 (mod 2π)（20π 偏移保幅值饱和）；耦合=初始淹没满耦代理（Rules 现值）；" +
                              "地形冻结=dump1；v0=0（dump 无速度场）；帧率 1/60 s（松弛对满耦合格不敏感）");
                sb.AppendLine($"[判据] p99(|Δh|) ≤ {PassP99} m 且 max ≤ {PassMaxAbs} m（活沙+相位不确定性工程容差）");

                var (p50, p90, p99, mx, mean) = AbsStats(absWater);
                var (b50, b90, b99, bmx, bmean) = AbsStats(baseline);
                sb.AppendLine($"[水位场 |Δ| 全场] p50={p50:F4} p90={p90:F4} p99={p99:F4} max={mx:F4} mean={mean:F4} m  → " +
                              $"{(p99 <= PassP99 && mx <= PassMaxAbs ? "PASS" : "FAIL")}");
                sb.AppendLine($"[基线 dump1↔dump2 自差] p50={b50:F4} p90={b90:F4} p99={b99:F4} max={bmx:F4} mean={bmean:F4} m" +
                              "（同窗原作场自身变化量——我方误差应与此同量级才算跟住了原作动力学）");

                AppendStrata(sb, "海区(g1<0.9) ", strataSea, absWater, baseline);
                AppendStrata(sb, "swash(.9-1.3)", strataSwash, absWater, baseline);
                AppendStrata(sb, "干区(g1>1.3) ", strataDry, absWater, baseline);

                var (_, ep90, ep99, emx, _) = AbsStats(absEta);
                sb.AppendLine($"[eta 场 |Δ|] p90={ep90:F4} p99={ep99:F4} max={emx:F4} m（我方 η 用冻结 g1、原作用活沙 g2，含 ≤0.018 m 地形自漂）");

                var (_, vp90, vp99, vmx, _) = AbsStats(Speeds(vx, vz));
                sb.AppendLine($"[速度场] 原作 dump 无速度通道 → 无真值侧；我方 p90={vp90:F3} p99={vp99:F3} max={vmx:F3} m/s（限幅 {WaterLabRules.MaxVelocity}）");

                var (_, q90, q99, qmx, _) = AbsStats(absWaterPoint);
                sb.AppendLine($"[辅：偶格点采算子 |Δ|] p90={q90:F4} p99={q99:F4} max={qmx:F4} m（readback 采样算子未定案，块平均为主口径）");

                string report = sb.ToString();
                TestContext.Progress.WriteLine(report);

                // ---- 门禁（任务书判据，FAIL 输出 top-10 供回溯）----
                if (p99 <= PassP99 && mx <= PassMaxAbs)
                    return;

                string top = Top10Cells(absWater, d2, ours256);
                TestContext.Progress.WriteLine(top);
                Assert.Fail($"L1 差分门禁 FAIL：p99(|Δh|)={p99:F4} m（≤{PassP99}?）、max={mx:F4} m（≤{PassMaxAbs}?）\n{report}\n{top}");
            }
            finally
            {
                WaterLabReplicaSnapshot.Unload();
            }
        }

        // ---- 帧末 SurfaceUpdate（compute SurfaceUpdate 逐式：蒸发 → 潮位松弛 → 干滩渗透）----

        static void UpdateSurface(float[] h, float[] ground, float simTime, float dtFrame)
        {
            float playTime = PhaseOffset + simTime;
            // target = max(SeaLevel + clamp(amp·sin(t), −0.5, +∞) − g, 0.05)：与 Rules.TideDepthTarget
            // 逐式一致，仅把每帧不变的时间项提出循环（地形冻结 → 目标的 g 项也可预算）
            float tide = WaterLabRules.SeaLevel + Mathf.Clamp(
                WaterLabRules.WaveAmplitude(playTime) * Mathf.Sin(playTime),
                WaterLabRules.TideClampMin, float.PositiveInfinity);
            float evap = WaterLabRules.EvaporationRate() * dtFrame;
            float infilDecay = Mathf.Exp(-WaterLabRules.BeachInfiltrationRate * dtFrame);

            for (int i = 0; i < h.Length; i++)
            {
                float g = ground[i];
                float hh = h[i] - evap;
                if (hh < 0f) hh = 0f;

                float target = tide - g;
                if (target < WaterLabRules.TideMinAmount) target = WaterLabRules.TideMinAmount;

                // 耦合代理（Rules.CouplingWeightAt(g)/15 ∈ {0,1}）：lerp 退化为选择——
                // 满耦合格每帧直接钉到目标（海区波动结构无法复现的根源，见类注释）
                float coupling = WaterLabRules.CouplingWeightAt(g) / WaterLabRules.CouplingWeightMax;
                hh = Mathf.Lerp(hh, target, coupling);

                float visible = hh - WaterLabRules.DryDepth;
                if (g > WaterLabRules.SeaLevel && visible > 0f && visible < WaterLabRules.BeachInfiltrationDepth)
                    hh = WaterLabRules.DryDepth + (hh - WaterLabRules.DryDepth) * infilDecay;

                h[i] = hh;
            }
        }

        // ---- 几何/统计工具 ----

        /// <summary>256² readback → 512² 水场：readback 格 = 其 2×2 水格块均值
        ///（scaleBias 落点 2r+0.5），块复制是保块质量的逆算子（子块结构信息不可恢复）。</summary>
        static void BlockReplicate256to512(float[] src, float[] dst)
        {
            for (int r = 0; r < ReadbackRes; r++)
            {
                int row0 = (2 * r) * SimRes;
                int row1 = (2 * r + 1) * SimRes;
                for (int c = 0; c < ReadbackRes; c++)
                {
                    float v = src[r * ReadbackRes + c];
                    dst[row0 + 2 * c] = v;
                    dst[row0 + 2 * c + 1] = v;
                    dst[row1 + 2 * c] = v;
                    dst[row1 + 2 * c + 1] = v;
                }
            }
        }

        static void DownsampleBox2x512to256(float[] src, float[] dst)
        {
            for (int r = 0; r < ReadbackRes; r++)
                for (int c = 0; c < ReadbackRes; c++)
                    dst[r * ReadbackRes + c] = 0.25f * (
                        src[(2 * r) * SimRes + 2 * c] + src[(2 * r) * SimRes + 2 * c + 1] +
                        src[(2 * r + 1) * SimRes + 2 * c] + src[(2 * r + 1) * SimRes + 2 * c + 1]);
        }

        /// <summary>冻结快照 → 水格分辨率地形：water 格心世界坐标处对 256² 快照双线性
        ///（与 WaterLabReplicaSnapshot.SampleFromArray 同式；水格距是 readback 格距一半，
        /// 格心落在 rb 连续坐标 0.5·cx−0.25，真双线性插值）。ReplicaMode 生产链是
        /// 256→1024² 缓冲再由 compute 采样，同口径的两次双线性在此处一步到位。</summary>
        static void FillGround512(float[] ground256, float[] dst)
        {
            float half = WaterLabRules.DomainSize * 0.5f;
            float cellW = WaterLabRules.WaterCellSize;
            for (int cz = 0; cz < SimRes; cz++)
            {
                float wz = -half + (cz + 0.5f) * cellW;
                for (int cx = 0; cx < SimRes; cx++)
                {
                    float wx = -half + (cx + 0.5f) * cellW;
                    dst[cx + cz * SimRes] = SampleBilinear256(ground256, wx, wz);
                }
            }
        }

        static float SampleBilinear256(float[] data, float worldX, float worldZ)
        {
            float fx = worldX / WaterLabReplicaSnapshot.CellSize + WaterLabReplicaSnapshot.IndexBias;
            float fz = worldZ / WaterLabReplicaSnapshot.CellSize + WaterLabReplicaSnapshot.IndexBias;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, ReadbackRes - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, ReadbackRes - 2);
            float tx = Mathf.Clamp01(fx - x0);
            float tz = Mathf.Clamp01(fz - z0);
            int i = z0 * ReadbackRes + x0;
            return Mathf.Lerp(Mathf.Lerp(data[i], data[i + 1], tx),
                              Mathf.Lerp(data[i + ReadbackRes], data[i + ReadbackRes + 1], tx), tz);
        }

        static (float p50, float p90, float p99, float max, float mean) AbsStats(float[] absDiff)
        {
            var sorted = (float[])absDiff.Clone();
            Array.Sort(sorted);
            double sum = 0f;
            for (int i = 0; i < absDiff.Length; i++) sum += absDiff[i];
            float Q(float q) => sorted[Mathf.Clamp((int)(q * sorted.Length), 0, sorted.Length - 1)];
            return (Q(0.50f), Q(0.90f), Q(0.99f), sorted[sorted.Length - 1], (float)(sum / absDiff.Length));
        }

        static float[] Speeds(float[] vx, float[] vz)
        {
            var s = new float[vx.Length];
            for (int i = 0; i < s.Length; i++)
                s[i] = Mathf.Sqrt(vx[i] * vx[i] + vz[i] * vz[i]);
            return s;
        }

        static void AppendStrata(StringBuilder sb, string name,
            System.Collections.Generic.List<int> cells, float[] absWater, float[] baseline)
        {
            var ours = new float[cells.Count];
            var self = new float[cells.Count];
            for (int k = 0; k < cells.Count; k++)
            {
                ours[k] = absWater[cells[k]];
                self[k] = baseline[cells[k]];
            }
            var o = AbsStats(ours);
            var b = AbsStats(self);
            sb.AppendLine($"[分层 {name}] n={cells.Count,5}  我方 p50={o.p50:F4} p90={o.p90:F4} p99={o.p99:F4} max={o.max:F4}" +
                          $"  |  基线自差 p50={b.p50:F4} p99={b.p99:F4} max={b.max:F4} m");
        }

        static string Top10Cells(float[] absWater, DumpData truth, float[] ours)
        {
            var idx = new int[absWater.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) => absWater[b].CompareTo(absWater[a]));
            var sb = new StringBuilder("[FAIL 回溯] 差异最大 10 格（row,col=格索引；world=(idx+0.5)·0.1171875−15）：");
            for (int k = 0; k < 10 && k < idx.Length; k++)
            {
                int i = idx[k];
                int row = i / ReadbackRes, col = i % ReadbackRes;
                sb.Append($"\n  #{k + 1} (row={row},col={col}) |Δ|={absWater[i]:F4} ours={ours[i]:F4} truth={truth.Water[i]:F4} g1={truth.Ground[i]:F3}");
            }
            return sb.ToString();
        }

        // ---- dump 定位与解析 ----

        static string LocateDumpDir()
        {
            string env = Environment.GetEnvironmentVariable("PC3D_WATERLAB_DUMP_DIR");
            if (!string.IsNullOrEmpty(env) && System.IO.File.Exists(System.IO.Path.Combine(env, "dump1.json")))
                return env;

            string dir = null;
            try { dir = AppContext.BaseDirectory; } catch { /* 极端宿主无 BaseDirectory */ }
            for (int i = 0; i < 14 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = System.IO.Path.Combine(
                    dir, "external", "sandcastle-ref", "rt", "dumps", "menu-snapshot", "dump1.json");
                if (System.IO.File.Exists(candidate))
                    return System.IO.Path.GetDirectoryName(candidate);
                dir = System.IO.Path.GetDirectoryName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            }

            // 逆向台固定落点（交接档 §2；harness 临时副本被挪出仓库时兜底）
            const string fixedBench = @"F:\VSCode\pirate-crew-3d-unity\external\sandcastle-ref\rt\dumps\menu-snapshot";
            if (System.IO.File.Exists(System.IO.Path.Combine(fixedBench, "dump1.json")))
                return fixedBench;
            return null;
        }

        static string HowToRunMessage()
        {
            return "未找到差分数据目录 external/sandcastle-ref/rt/dumps/menu-snapshot（dump1.json/dump2.json）。" +
                   "该目录由逆向台 frida 只读 dump 产出、不入库：设环境变量 PC3D_WATERLAB_DUMP_DIR 指向它后重跑" +
                   "（默认落点 F:/VSCode/pirate-crew-3d-unity/external/sandcastle-ref/rt/dumps/menu-snapshot）。";
        }

        sealed class DumpData
        {
            public string Name;
            public long TimestampMs;
            public int Res;
            public float[] Water, Ground, Eta;
            public byte[] WaterBytes, GroundBytes; // 生产快照接线（WaterLabReplicaSnapshot.Load）用原始字节
            public float Level, Gravity, EvaporationFactor;
            public float WaterMin, WaterMax, GroundMin, GroundMax;
        }

        static DumpData LoadDump(string path)
        {
            string raw = System.IO.File.ReadAllText(path);
            int start = raw.IndexOf("===RTJSON===", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"{path}: 缺少 ===RTJSON=== 标记（不是 frida dump 产物？）");
            start = raw.IndexOf('{', start);
            int endMark = raw.IndexOf("===END===", start, StringComparison.Ordinal);
            Assert.IsTrue(start > 0 && endMark > start, $"{path}: JSON 区段定位失败");
            int end = raw.LastIndexOf('}', endMark);
            Assert.IsTrue(end > start, $"{path}: JSON 区段为空");

            using JsonDocument doc = JsonDocument.Parse(raw.Substring(start, end - start + 1));
            JsonElement root = doc.RootElement;
            JsonElement full = root.GetProperty("fullDump");

            var d = new DumpData
            {
                Name = System.IO.Path.GetFileName(path),
                TimestampMs = root.GetProperty("timestampMs").GetInt64(),
                Res = full.GetProperty("resX").GetInt32(),
            };
            Assert.AreEqual(ReadbackRes, d.Res, $"{d.Name}: resX 应为 256（Readback 层分辨率）");
            Assert.AreEqual(ReadbackRes, full.GetProperty("resY").GetInt32(), $"{d.Name}: resY 应为 256");

            d.WaterBytes = Convert.FromBase64String(full.GetProperty("waterAmount_b64").GetString());
            d.GroundBytes = Convert.FromBase64String(full.GetProperty("groundHeight_b64").GetString());
            byte[] etaBytes = Convert.FromBase64String(full.GetProperty("eta_b64").GetString());
            d.Water = ToFloats(d.WaterBytes, "waterAmount");
            d.Ground = ToFloats(d.GroundBytes, "groundHeight");
            d.Eta = ToFloats(etaBytes, "eta");

            JsonElement water = root.GetProperty("water");
            d.Level = water.GetProperty("level").GetSingle();
            d.Gravity = water.GetProperty("gravityForce").GetSingle();
            d.EvaporationFactor = water.GetProperty("evaporationFactor").GetSingle();

            (d.WaterMin, d.WaterMax) = MinMax(d.Water);
            (d.GroundMin, d.GroundMax) = MinMax(d.Ground);
            return d;
        }

        static float[] ToFloats(byte[] bytes, string name)
        {
            Assert.AreEqual(0, bytes.Length % 4, $"{name}: b64 解码长度非 4 的倍数");
            var data = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
            return data;
        }

        static (float min, float max) MinMax(float[] data)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] < min) min = data[i];
                if (data[i] > max) max = data[i];
            }
            return (min, max);
        }
    }
}
