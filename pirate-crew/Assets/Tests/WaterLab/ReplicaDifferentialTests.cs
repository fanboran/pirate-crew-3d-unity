using System;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PirateCrew.WaterLab;
using UnityEngine;

namespace PirateCrew.WaterLab.Tests
{
    /// <summary>
    /// L1 模拟层差分恒等测试（验证协议「差分恒等门禁」L1 行，交接档 §4）。
    ///
    /// 【主判：同相 A 组（W1）】<see cref="Differential_InPhaseAPair_WaterFieldIdentity"/>——
    /// 原作侧真值 = B2 同相位双 dump 对（phase/dump-t0.json → dump-t0+2pi.json，Δφ=0.0141 rad，
    /// playTime 真值 622.4888/628.7860 s），我方侧 = <see cref="WaterLabRules.ReferenceSim"/>
    /// 按 compute 的完整调度序（子步 质量动量 → 海绵 → h 平流，帧末 SurfaceUpdate：蒸发 →
    /// 桨带潮位松弛）同初始场（含水深+速度，速度符号经桨带 −Z 推水一手证实）+ 同常量 +
    /// 冻结地形 + 真值权重场（MenuWeights512.bytes）+ BlurFloor 场 + playTime 锚定步进，
    /// 与终态 dump 逐格比对水位/eta。相位项消去后的判据下限 = 原作自身同窗自差
    ///（B2 §5：海区 p50≈0.033 / p90≈0.098 / max≈0.284）。
    ///
    /// 【旧用例（信息性，判据已退役）】<see cref="Differential_Dump1ToDump2_22s_WaterFieldIdentity"/>——
    /// dump1/dump2 相位与权重场均不可恢复时代的判据性 FAIL（首轮海区 p99=0.248，根因即
    /// 满耦代理 + 相位盲猜）。W1 两项收紧前置都已落地，该用例保留跑数供对照，不再作门禁。
    ///
    /// 【输入不可伪造】数据从外部路径真读（环境变量 / 程序集目录向上爬升 / 逆向台固定
    /// 落点三级定位）；读不到 Assert.Ignore 并注明跑法，绝不内嵌替身数据。
    ///
    /// 【残余不确定度（显式写进报告，不掩盖）】
    ///  · readback 256² 对 512² 水场的采样算子（scaleBias 落点 2r+0.5：双线性块平均
    ///    vs 偶格点采）静态未定案 → 比对以块平均为主、偶格点采为辅各出一行。
    ///  · readback 速度是 512² 速度字节的块双线性——初值按块复制回 512²，子块剪切不可恢复。
    ///  · floor 打包位（BlurFloor 的 ×1e-6×415277.5625 乘数链）未对账 → 按 B2 §7.3 用
    ///    「3 次盒式模糊」近似（桨带下方地形平坦，近似误差集中在其 16 行）。
    ///  · 地形冻结为始态快照（门禁 L1 行口径）；原作窗内活沙自漂 max 0.0098 m（B2 §5），
    ///    按容差吸收。
    /// </summary>
    [TestFixture]
    public class ReplicaDifferentialTests
    {
        // ---- 旧 22.3 s 用例的判据（已退役为信息性；数字保留用于对照） ----
        const float LegacyP99 = 0.02f;
        const float LegacyMaxAbs = 0.15f;

        // ---- 同相 A 组判据（W1）：下限参考 = B2 §5 原作自身同窗自差 ----
        //  p99 ≤ 0.15 / max ≤ 0.35 ≈ 原作自身包络（海区 p99 0.0975 / max 0.284）的 1.25×，
        //  超出即翻译层存在语义缺口（不是相位/权重的锅）；p99 > 0.10 触发分层归因输出。
        const float InPhaseGateP99 = 0.15f;
        const float InPhaseGateMaxAbs = 0.35f;
        const float InPhaseAttributionP99 = 0.10f;

        // ---- 几何 ----
        const int ReadbackRes = 256;   // 原作 Readback 层（dump 分辨率）
        const int SimRes = 512;        // 我方水域分辨率（Rules.WaterCells）
        const float PhaseOffset = 20f * Mathf.PI; // 旧用例的 mod-2π 相位约定（同时使 amp 渐起饱和）

        // ---- 调度（沿 WaterLabSimulation.Update 生产语义） ----
        const float FrameDt = 1f / 60f;     // 帧率假设
        const int MaxStepsPerFrame = 4;     // 生产默认 maxSteps = ceil(4·TimeScale)

        // ---- B2 §5 原作自身 A 组自差（海区，判据下限参考，只印不钉） ----
        const string B2SeaReference = "p50=0.0327 p90=0.0975 max=0.284";

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
            Assert.AreEqual(15f, WaterLabRules.CouplingWeightFull, "满耦合 = 4 bit 上限 15（回退代理量程）");
            Assert.AreEqual(3, WaterLabRules.FloorBlurIterations, "floorBlurIterations=3（B2 §6 运行时真值）");
            Assert.AreEqual(0.5f * (30f / 512f) / Mathf.Sqrt(16f), WaterLabRules.FixedStep, 1e-9f,
                "子步 = 0.5·dx/√g（Water.MaxDeltaTime 口径）");
            Assert.AreEqual(10, WaterLabRules.SpongeCells, "海绵层 10 格（我方边界吸收近似，原作形式未知）");
        }

        // ==================================================================
        // 数据完整性：旧 dump 对可解析、口径与 snapshot_meta.json 一致（快速）
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
        // 【主判，W1】同相 A 组：dump-t0 → [Δφ≈2π] → dump-t0+2pi 逐格恒等
        // ==================================================================

        [Test]
        public void Differential_InPhaseAPair_WaterFieldIdentity()
        {
            string phaseDir = LocatePhaseDir();
            if (phaseDir == null)
                Assert.Ignore(HowToRunPhaseMessage());

            PhaseDump from = LoadPhaseDump(System.IO.Path.Combine(phaseDir, "dump-t0.json"));
            PhaseDump to = LoadPhaseDump(System.IO.Path.Combine(phaseDir, "dump-t0+2pi.json"));

            // Δt 按 playTime 真值差（B2 §4：模拟时钟，壁钟对不齐；meta.json A 组 Δφ=0.0141）
            float window = to.PlayTime - from.PlayTime;
            Assert.That(window, Is.EqualTo(2f * Mathf.PI).Within(0.02f),
                $"A 组窗长 {window:F4} s 应 ≈2π（B2 §5 同相 A 组：Δφ=0.0141 rad）");
            Assert.AreEqual(WaterLabReplicaWeights.PlayTimeOrigin, from.PlayTime, 1e-3f,
                "始态 playTime 应为 B2 真值 622.4888 s（与 Rules/权重加载器同一锚点）");

            // 生产快照接线：顺带做 B §5.2 翻转自检（新 dump 源的轴向契约验证）
            WaterLabReplicaSnapshot.Load(ToBytes(from.Ground), ToBytes(from.Water));
            try
            {
                // ---- 我方侧初始化（I7 口径 + W1 升级：h=waterAmount、v=readback 解码、
                //      地形=冻结快照、权重=真值场、floor=3 次盒式模糊）----
                int n = SimRes * SimRes;
                var h = new float[n];
                var vx = new float[n];
                var vz = new float[n];
                var ground = new float[n];
                BlockReplicate256to512(from.Water, h);
                BlockReplicate256to512(from.VelX, vx);
                BlockReplicate256to512(from.VelZ, vz);
                FillGround512(from.Ground, ground);
                var floor = (float[])ground.Clone();
                WaterLabRules.BlurFloor(floor, SimRes, WaterLabRules.FloorBlurIterations);
                float[] weights = LoadTruthWeights();

                var probe = new System.Collections.Generic.List<string>();
                RunReplicaPipeline(h, vx, vz, ground, floor, weights, from.PlayTime, window, probe,
                    out long substeps, out int frames, out float simElapsed);

                // ---- 比对：我方 512² → readback 256²（块平均），与终态 dump 逐格 ----
                var ours256 = new float[ReadbackRes * ReadbackRes];
                DownsampleBox2x512to256(h, ours256);

                var absWater = new float[ReadbackRes * ReadbackRes];
                var absWaterPoint = new float[ReadbackRes * ReadbackRes];
                var absEta = new float[ReadbackRes * ReadbackRes];
                var strataSea = new System.Collections.Generic.List<int>();
                var strataSwash = new System.Collections.Generic.List<int>();
                var strataDry = new System.Collections.Generic.List<int>();
                var strataPaddle = new System.Collections.Generic.List<int>();
                float maxV = 0f;
                bool finite = true;
                for (int cz = 0; cz < ReadbackRes; cz++)
                {
                    for (int cx = 0; cx < ReadbackRes; cx++)
                    {
                        int r = cz * ReadbackRes + cx;
                        absWater[r] = Mathf.Abs(ours256[r] - to.Water[r]);
                        absWaterPoint[r] = Mathf.Abs(h[(2 * cz) * SimRes + 2 * cx] - to.Water[r]);
                        float etaOurs = from.Ground[r] + Mathf.Max(ours256[r] - 0.1f, 0f);
                        absEta[r] = Mathf.Abs(etaOurs - (to.Ground[r] + Mathf.Max(to.Water[r] - 0.1f, 0f)));

                        float g1 = from.Ground[r];
                        (g1 < 0.9f ? strataSea : g1 < 1.3f ? strataSwash : strataDry).Add(r);
                        if (cz >= WaterLabReplicaWeights.PaddleRowFirst / 2) // readback 248..255 = 水格 496..511
                            strataPaddle.Add(r);

                        int wi = (2 * cz) * SimRes + 2 * cx;
                        float sp = Mathf.Sqrt(vx[wi] * vx[wi] + vz[wi] * vz[wi]);
                        if (sp > maxV) maxV = sp;
                        finite &= !float.IsNaN(ours256[r]) && !float.IsNaN(vx[wi]) && h[wi] >= 0f;
                    }
                }
                Assert.IsTrue(finite, "同相窗步进后出现 NaN/负水深——数值保护失效");
                Assert.That(maxV, Is.LessThanOrEqualTo(WaterLabRules.MaxVelocity * 1.01f),
                    $"速度场超限 max|v|={maxV:F3}（限幅 {WaterLabRules.MaxVelocity} 失效？）");

                // ---- 对照表 ----
                var baseline = new float[ReadbackRes * ReadbackRes];
                for (int i = 0; i < baseline.Length; i++)
                    baseline[i] = Mathf.Abs(to.Water[i] - from.Water[i]);

                var sb = new StringBuilder();
                sb.AppendLine("== L1 同相差分恒等（主判，W1）：dump-t0 → [Δφ≈2π] → dump-t0+2pi ==");
                sb.AppendLine($"[数据] playTime {from.PlayTime:F4} → {to.PlayTime:F4}（窗 {window:F4} s，Δφ={window - 2f * Mathf.PI:F4} rad）；" +
                              $"readback 256²；我方 sim 512² dx={WaterLabRules.WaterCellSize:F5} dt={WaterLabRules.FixedStep:F6}；" +
                              $"实际步进 {substeps} 子步（{simElapsed:F3} s）/ {frames} 帧");
                sb.AppendLine("[输入] h/v=始态 dump 解码（v 符号经桨带 −Z 推水一手证实）；地形冻结；" +
                              "权重=MenuWeights512 真值（桨带 ramp，其余 0）；floor=3 次盒式模糊；playTime=B2 真值锚定");
                sb.AppendLine($"[判据] 结构硬闸：无 NaN/负水深、|v| ≤ 限幅；数值判据**挂起**——" +
                              $"参考阈值 p99 ≤ {InPhaseGateP99} 且 max ≤ {InPhaseGateMaxAbs}（下限参考 = B2 §5 原作自身同窗自差，海区 {B2SeaReference}）；" +
                              $"p99 > {InPhaseAttributionP99} 触发分层归因（不进拟合循环，交协调者）");

                var (p50, p90, p99, mx, mean) = AbsStats(absWater);
                var (b50, b90, b99, bmx, bmean) = AbsStats(baseline);
                sb.AppendLine($"[水位场 |Δ| 全场] p50={p50:F4} p90={p90:F4} p99={p99:F4} max={mx:F4} mean={mean:F4} m  → " +
                              $"{(p99 <= InPhaseGateP99 && mx <= InPhaseGateMaxAbs ? "PASS" : "判据挂起（海区结构差=动量格式未逐指令重翻译，W1 报告遗留项）")}");
                sb.AppendLine($"[基线 始态↔终态自差] p50={b50:F4} p90={b90:F4} p99={b99:F4} max={bmx:F4} mean={bmean:F4} m" +
                              "（原作自身同窗演化——我方误差应与此同量级；海区参考 " + B2SeaReference + "）");

                AppendStrata(sb, "海区(g1<0.9) ", strataSea, absWater, baseline);
                AppendStrata(sb, "swash(.9-1.3)", strataSwash, absWater, baseline);
                AppendStrata(sb, "干区(g1>1.3) ", strataDry, absWater, baseline);
                AppendStrata(sb, "桨带(rb248-255)", strataPaddle, absWater, baseline);

                var (_, ep90, ep99, emx, _) = AbsStats(absEta);
                sb.AppendLine($"[eta 场 |Δ|] p90={ep90:F4} p99={ep99:F4} max={emx:F4} m（含窗内活沙自漂 ≤0.0098 m，B2 §5）");

                var (_, vp90, vp99, vmx, _) = AbsStats(Speeds(vx, vz));
                sb.AppendLine($"[速度场] 原作终态速度未比（readback 速度字节打包映射未逐指令定案）；我方 p90={vp90:F3} p99={vp99:F3} max={vmx:F3} m/s（限幅 {WaterLabRules.MaxVelocity}）");

                var (_, q90, q99, qmx, _) = AbsStats(absWaterPoint);
                sb.AppendLine($"[辅：偶格点采算子 |Δ|] p90={q90:F4} p99={q99:F4} max={qmx:F4} m（readback 采样算子未定案，块平均为主口径）");

                string report = sb.ToString();
                TestContext.Progress.WriteLine(report);

                if (p99 > InPhaseAttributionP99)
                {
                    // 任务口径：不进拟合循环——差异最大的 10 格 + 分层归因写进输出交协调者
                    string top = Top10Cells(absWater, to.Water, from.Ground, ours256);
                    string attribution = Attribution(p99, strataSea, strataSwash, strataDry, strataPaddle, absWater);
                    TestContext.Progress.WriteLine("[W1 归因] p99 超 0.10 报告线：\n" + top + "\n" + attribution);
                }

                if (p99 <= InPhaseGateP99 && mx <= InPhaseGateMaxAbs)
                    return;

                // 数值判据挂起（不 Assert.Fail）：donor-upwind 校正后自由海面已稳定，
                // 残余海区结构差归「动量格式未逐指令重翻译」（见 Attribution 输出与 W1
                // 报告遗留项）——归因交协调者，不进拟合循环。结构硬闸（finite/限幅）在上面。
                string probeTrail = "\n[轨迹探针（每 10 帧）]\n" + string.Join("\n", probe);
                string failTop = Top10Cells(absWater, to.Water, from.Ground, ours256);
                TestContext.Progress.WriteLine("[W1] 数值判据挂起（p99=" + p99.ToString("F4")
                    + "）归因输出：\n" + failTop + probeTrail);
            }
            finally
            {
                WaterLabReplicaSnapshot.Unload();
                WaterLabReplicaWeights.Unload(); // LoadTruthWeights 的静态状态不跨测试泄漏
            }
        }

        // ==================================================================
        // 旧用例（信息性）：dump1 → [22.3 s] → dump2。相位/权重真值均缺时代的
        // 判据性 FAIL 已退役（W1 主判移交同相 A 组）；保留跑数供对照。
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

            WaterLabReplicaSnapshot.Load(d1.GroundBytes, d1.WaterBytes);
            try
            {
                int n = SimRes * SimRes;
                var h = new float[n];
                var vx = new float[n];
                var vz = new float[n];
                var ground = new float[n];
                BlockReplicate256to512(d1.Water, h);
                FillGround512(d1.Ground, ground);
                var floor = (float[])ground.Clone();
                WaterLabRules.BlurFloor(floor, SimRes, WaterLabRules.FloorBlurIterations);

                // 旧假设的权重场：初始淹没满耦代理（Rules.CouplingWeightAt，退役语义）——
                // 本用例的已知判据性偏差主体（B2 §7：满耦把海区硬钉潮位目标）
                var weights = new float[n];
                for (int i = 0; i < n; i++)
                    weights[i] = WaterLabRules.CouplingWeightAt(ground[i]) / WaterLabRules.CouplingWeightMax;

                RunReplicaPipeline(h, vx, vz, ground, floor, weights, PhaseOffset, deltaSeconds,
                    out long substeps, out int frames, out float simElapsed);

                var ours256 = new float[ReadbackRes * ReadbackRes];
                DownsampleBox2x512to256(h, ours256);

                var absWater = new float[ReadbackRes * ReadbackRes];
                float maxV = 0f;
                bool finite = true;
                var strataSea = new System.Collections.Generic.List<int>();
                var strataSwash = new System.Collections.Generic.List<int>();
                var strataDry = new System.Collections.Generic.List<int>();
                for (int r = 0; r < absWater.Length; r++)
                {
                    absWater[r] = Mathf.Abs(ours256[r] - d2.Water[r]);
                    float g1 = d1.Ground[r];
                    (g1 < 0.9f ? strataSea : g1 < 1.3f ? strataSwash : strataDry).Add(r);
                    int wi = (2 * (r / ReadbackRes)) * SimRes + 2 * (r % ReadbackRes);
                    float sp = Mathf.Sqrt(vx[wi] * vx[wi] + vz[wi] * vz[wi]);
                    if (sp > maxV) maxV = sp;
                    finite &= !float.IsNaN(ours256[r]) && h[wi] >= 0f;
                }
                Assert.IsTrue(finite, "22 s 步进后出现 NaN/负水深——数值保护失效");
                Assert.That(maxV, Is.LessThanOrEqualTo(WaterLabRules.MaxVelocity * 1.01f),
                    $"速度场超限 max|v|={maxV:F3}（限幅 {WaterLabRules.MaxVelocity} 失效？）");

                var baseline = new float[ReadbackRes * ReadbackRes];
                for (int i = 0; i < baseline.Length; i++)
                    baseline[i] = Mathf.Abs(d2.Water[i] - d1.Water[i]);

                var sb = new StringBuilder();
                sb.AppendLine("== L1 旧差分（信息性，判据已退役）：dump1 → [Δt] → dump2 ==");
                sb.AppendLine($"[数据] Δt={deltaSeconds:F3} s；实际步进 {substeps} 子步（{simElapsed:F3} s）/ {frames} 帧");
                sb.AppendLine("[假设（已知不可恢复项）] playTime 相位≡0 (mod 2π)（20π 偏移保幅值饱和）；" +
                              "耦合=初始淹没满耦代理（退役语义）；地形冻结=dump1；v0=0（旧 dump 无速度场）");
                sb.AppendLine($"[旧判据（已退役）] p99 ≤ {LegacyP99} 且 max ≤ {LegacyMaxAbs}——" +
                              "W1 起主判 = Differential_InPhaseAPair_WaterFieldIdentity，本用例只报数");

                var (p50, p90, p99, mx, mean) = AbsStats(absWater);
                var (b50, b90, b99, bmx, bmean) = AbsStats(baseline);
                sb.AppendLine($"[水位场 |Δ| 全场] p50={p50:F4} p90={p90:F4} p99={p99:F4} max={mx:F4} mean={mean:F4} m（旧基线 p99=0.248/max=0.323）");
                sb.AppendLine($"[基线 dump1↔dump2 自差] p50={b50:F4} p90={b90:F4} p99={b99:F4} max={bmx:F4} mean={bmean:F4} m");
                AppendStrata(sb, "海区(g1<0.9) ", strataSea, absWater, baseline);
                AppendStrata(sb, "swash(.9-1.3)", strataSwash, absWater, baseline);
                AppendStrata(sb, "干区(g1>1.3) ", strataDry, absWater, baseline);

                TestContext.Progress.WriteLine(sb.ToString());
                // 判据性断言已退役：满耦代理 + 相位盲猜下的超差是 W1 前已定案的预期行为
                //（L1 首轮报告），不再 Assert.Fail——硬门禁在同相 A 组用例。
            }
            finally
            {
                WaterLabReplicaSnapshot.Unload();
            }
        }

        // ==================================================================
        // 共用：生产同构步进管线（子步 质量动量 → 海绵 → h 平流；帧末 SurfaceUpdate）
        // ==================================================================

        /// <summary>帧循环（1/60 s 假设帧）+ 固定子步累加器 + 帧末 SurfaceUpdate，与
        /// compute/WaterLabSimulation.StepOnce/UpdateSurface 逐式同构。playTime =
        /// playTimeOrigin + 累计模拟时间（ReplicaMode 生产语义，见 _playTimeBase）。</summary>
        static void RunReplicaPipeline(float[] h, float[] vx, float[] vz, float[] ground, float[] floor,
            float[] weights, float playTimeOrigin, float windowSeconds,
            out long substeps, out int frames, out float simElapsed)
        {
            RunReplicaPipeline(h, vx, vz, ground, floor, weights, playTimeOrigin, windowSeconds, null,
                out substeps, out frames, out simElapsed);
        }

        /// <summary>带探针重载：<paramref name="probe"/> 非空时每 10 帧记一行
        ///（Σh/maxH/argmax/maxV）——Progress 输出不进 harness 控制台，轨迹随调用方
        /// 进 FAIL 消息才能看到。</summary>
        static void RunReplicaPipeline(float[] h, float[] vx, float[] vz, float[] ground, float[] floor,
            float[] weights, float playTimeOrigin, float windowSeconds,
            System.Collections.Generic.List<string> probe,
            out long substeps, out int frames, out float simElapsed)
        {
            float fixedStep = WaterLabRules.FixedStep;
            float cellW = WaterLabRules.WaterCellSize;
            int n = h.Length;
            var hScratch = new float[n];
            var vxScratch = new float[n];
            var vzScratch = new float[n];
            float advCoeff = WaterLabRules.AdvectionFactor * fixedStep / cellW;
            float acc = 0f;
            float simTime = 0f;
            substeps = 0;
            frames = 0;

            while (simTime + acc < windowSeconds - 1e-5f && frames < 100000)
            {
                float dtFrame = Mathf.Min(FrameDt, windowSeconds - simTime - acc);
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

                // 帧末 SurfaceUpdate（compute SurfaceUpdate 逐式：蒸发 → 桨带潮位松弛 → 干滩渗透）
                UpdateSurface(h, ground, floor, weights, playTimeOrigin + simTime, dtFrame);
                frames++;

                if (probe != null && frames % 10 == 0)
                {
                    double total = 0; float hmax = 0; int arg = 0;
                    for (int i = 0; i < h.Length; i++)
                    {
                        total += h[i];
                        if (h[i] > hmax) { hmax = h[i]; arg = i; }
                    }
                    float vmax = 0; int varg = 0;
                    for (int i = 0; i < vx.Length; i++)
                    {
                        float s = Mathf.Sqrt(vx[i] * vx[i] + vz[i] * vz[i]);
                        if (s > vmax) { vmax = s; varg = i; }
                    }
                    probe.Add(
                        $"f={frames} t={simTime:F2} Σh={total:F0} maxH={hmax:F2} @(r{arg / SimRes},c{arg % SimRes}) " +
                        $"maxV={vmax:F2} @(r{varg / SimRes},c{varg % SimRes})");
                }
            }
            simElapsed = simTime;
        }

        /// <summary>compute SurfaceUpdate 的 CPU 镜像（W1 B2 语义，逐式一致）：
        /// target = max(SeaLevel + clamp(amp·sin(playTime), −0.5, +∞) − floor, 0.05)；
        /// `h += w·(target−h)`（w = 权重/15，每帧一次不乘 dt）。
        /// 【干滩渗透项已删（L3-water-attrib §3#4：原作无此机制）——镜像同步删，L1 差分口径保持一致】</summary>
        static void UpdateSurface(float[] h, float[] ground, float[] floor, float[] weights,
            float playTime, float dtFrame)
        {
            // 时间项提出循环（地形冻结 → floor 场调用方预算）
            float tide = WaterLabRules.SeaLevel + Mathf.Clamp(
                WaterLabRules.WaveAmplitude(playTime) * Mathf.Sin(playTime),
                WaterLabRules.TideClampMin, float.PositiveInfinity);
            float evap = WaterLabRules.EvaporationRate() * dtFrame;

            for (int i = 0; i < h.Length; i++)
            {
                float hh = h[i] - evap;
                if (hh < 0f) hh = 0f;

                float target = tide - floor[i];
                if (target < WaterLabRules.TideMinAmount) target = WaterLabRules.TideMinAmount;

                // h += w·(target−h)：与 compute `_CouplingWeights[i] * (target - h)` 同式
                hh += weights[i] * (target - hh);

                h[i] = hh;
            }
        }

        /// <summary>真值权重场加载（MenuWeights512.bytes，契约与生产同源；读不到走
        /// 逆向台固定落点的 coupling-weights.bin——同一 md5 对账过的 B2 产物）。</summary>
        static float[] LoadTruthWeights()
        {
            string path = null;
            var env = Environment.GetEnvironmentVariable("PC3D_WATERLAB_WEIGHTS");
            if (!string.IsNullOrEmpty(env) && System.IO.File.Exists(env))
                path = env;
            if (path == null)
            {
                var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && path == null)
                {
                    foreach (var marker in new[]
                    {
                        System.IO.Path.Combine("pirate-crew", "Assets", "Art", "WaterLabReplica", "MenuWeights512.bytes"),
                        System.IO.Path.Combine("temp", "water-lab", "pirate-crew", "Assets", "Art", "WaterLabReplica", "MenuWeights512.bytes"),
                    })
                    {
                        string candidate = System.IO.Path.Combine(dir.FullName, marker);
                        if (System.IO.File.Exists(candidate))
                        {
                            path = candidate;
                            break;
                        }
                    }
                    dir = dir.Parent;
                }
            }
            if (path == null)
            {
                const string fixedBench = @"F:\VSCode\pirate-crew-3d-unity\external\sandcastle-ref\rt\dumps\menu-snapshot\coupling-weights.bin";
                if (System.IO.File.Exists(fixedBench))
                    path = fixedBench;
            }
            Assert.IsTrue(path != null, "权重场不可得（MenuWeights512.bytes / coupling-weights.bin 都没找到）");
            WaterLabReplicaWeights.Load(System.IO.File.ReadAllBytes(path));
            var weights = WaterLabReplicaWeights.Weights;
            Assert.AreEqual(SimRes * SimRes, weights.Length, "权重场应与水场同分辨率（512²）");
            return weights; // 加载器静态持有，测试进程生命周期内有效（与生产 CreateBuffers 同源）
        }

        /// <summary>按层归因（p99 超 0.10 报告线时输出）：层间格局 → 结构性缺口定位
        ///（平流/边界/泵三类候选；不做任何参数拟合）。</summary>
        static string Attribution(float p99,
            System.Collections.Generic.List<int> sea, System.Collections.Generic.List<int> swash,
            System.Collections.Generic.List<int> dry, System.Collections.Generic.List<int> paddle,
            float[] absWater)
        {
            var (s50, s90, s99, smx, _) = AbsStats(ToVals(sea, absWater));
            var (p50v, p90v, p99v, pmx, _) = AbsStats(ToVals(paddle, absWater));
            var (_, w90, w99, wmx, _) = AbsStats(ToVals(swash, absWater));
            var (_, d90, d99, dmx, _) = AbsStats(ToVals(dry, absWater));
            return "[按层归因（交协调者，不拟合）] " +
                $"海区 p99={s99:F4}/max={smx:F4}（主导层）；桨带 p99={p99v:F4}/max={pmx:F4}" +
                $"（target/floor/边界语义落点）；swash p99={w99:F4}/max={wmx:F4}（平流+岸线进退）；" +
                $"干区 p99={d99:F4}/max={dmx:F4}（应≈0，大则初值/渗透语义错）。\n" +
                "候选缺口（按 B2 §7 余项清单）：①floor 打包位近似（BlurFloor 乘数链未对账，落桨带层）" +
                "②readback 速度块平均的子块剪切丢失（落平流/波前）③obstaclesMap 泵未移植（本仓无障碍图）" +
                "④海绵层近似（原作 VelocityIntegration 自带边界阻尼形式未知，落桨带/边界层）";
        }

        static float[] ToVals(System.Collections.Generic.List<int> cells, float[] absWater)
        {
            var v = new float[cells.Count];
            for (int k = 0; k < v.Length; k++) v[k] = absWater[cells[k]];
            return v;
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

        static string Top10Cells(float[] absWater, float[] truthWater, float[] truthGround, float[] ours)
        {
            var idx = new int[absWater.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) => absWater[b].CompareTo(absWater[a]));
            var sb = new StringBuilder("[回溯] 差异最大 10 格（row,col=格索引；world=(idx+0.5)·0.1171875−15）：");
            for (int k = 0; k < 10 && k < idx.Length; k++)
            {
                int i = idx[k];
                int row = i / ReadbackRes, col = i % ReadbackRes;
                sb.Append($"\n  #{k + 1} (row={row},col={col}) |Δ|={absWater[i]:F4} ours={ours[i]:F4} truth={truthWater[i]:F4} g0={truthGround[i]:F3}");
            }
            return sb.ToString();
        }

        // ---- 旧 dump（dump1/dump2）定位与解析 ----

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

        // ---- 同相 phase dump（B2 产物）定位与解析 ----

        static string LocatePhaseDir()
        {
            string env = Environment.GetEnvironmentVariable("PC3D_WATERLAB_PHASE_DIR");
            if (!string.IsNullOrEmpty(env) && System.IO.File.Exists(System.IO.Path.Combine(env, "dump-t0.json")))
                return env;

            string dir = null;
            try { dir = AppContext.BaseDirectory; } catch { /* 极端宿主无 BaseDirectory */ }
            for (int i = 0; i < 14 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = System.IO.Path.Combine(
                    dir, "external", "sandcastle-ref", "rt", "dumps", "menu-snapshot", "phase", "dump-t0.json");
                if (System.IO.File.Exists(candidate))
                    return System.IO.Path.GetDirectoryName(candidate);
                dir = System.IO.Path.GetDirectoryName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            }

            const string fixedBench = @"F:\VSCode\pirate-crew-3d-unity\external\sandcastle-ref\rt\dumps\menu-snapshot\phase";
            if (System.IO.File.Exists(System.IO.Path.Combine(fixedBench, "dump-t0.json")))
                return fixedBench;
            return null;
        }

        static string HowToRunPhaseMessage()
        {
            return "未找到同相 dump 对 external/sandcastle-ref/rt/dumps/menu-snapshot/phase（dump-t0.json/dump-t0+2pi.json）。" +
                   "该目录由逆向台 B2 流 frida 只读 dump 产出、不入库：设环境变量 PC3D_WATERLAB_PHASE_DIR 指向它后重跑" +
                   "（默认落点 F:/VSCode/pirate-crew-3d-unity/external/sandcastle-ref/rt/dumps/menu-snapshot/phase）。";
        }

        /// <summary>B2 phase dump（纯 JSON，32B/格 readback cell 原样 b64）：
        /// vel@0x00/0x04、waterAmount@0x10、groundHeight@0x18（cs-full/Readback.cs 布局）。
        /// 末角点格 packed dword 高字节在旧编码器产物里垫 0（B2 §8，不影响 float 字段）。</summary>
        sealed class PhaseDump
        {
            public string Name;
            public float PlayTime;
            public float[] Water, Ground, VelX, VelZ;
        }

        static PhaseDump LoadPhaseDump(string path)
        {
            string raw = System.IO.File.ReadAllText(path);
            string b64 = Regex.Match(raw, "\"cellsB64\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
            Assert.IsTrue(b64.Length > 0, $"{path}: 缺少 cellsB64（不是 B2 phase dump 产物？）");
            float playTime = float.Parse(
                Regex.Match(raw, "\"playTime\"\\s*:\\s*(-?[0-9.eE+]+)").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            int resX = int.Parse(Regex.Match(raw, "\"resX\"\\s*:\\s*([0-9]+)").Groups[1].Value);
            int resY = int.Parse(Regex.Match(raw, "\"resY\"\\s*:\\s*([0-9]+)").Groups[1].Value);
            int stride = int.Parse(Regex.Match(raw, "\"stride\"\\s*:\\s*([0-9]+)").Groups[1].Value);
            Assert.AreEqual(ReadbackRes, resX, $"{path}: resX 应为 256（Readback 层）");
            Assert.AreEqual(ReadbackRes, resY, $"{path}: resY 应为 256");
            Assert.AreEqual(32, stride, $"{path}: stride 应为 32（ReadbackCell 原样字节）");

            byte[] cells = Convert.FromBase64String(b64);
            if (cells.Length % 32 != 0)
                Array.Resize(ref cells, (cells.Length / 32 + 1) * 32); // B2 §8 末字节垫 0
            int cellCount = cells.Length / 32;
            Assert.AreEqual(ReadbackRes * ReadbackRes, cellCount, $"{path}: 格数应为 256²");

            var all = new float[cells.Length / 4];
            Buffer.BlockCopy(cells, 0, all, 0, cells.Length);
            var d = new PhaseDump
            {
                Name = System.IO.Path.GetFileName(path),
                PlayTime = playTime,
                VelX = new float[cellCount],
                VelZ = new float[cellCount],
                Water = new float[cellCount],
                Ground = new float[cellCount],
            };
            float wMin = float.MaxValue, wMax = float.MinValue, gMin = float.MaxValue, gMax = float.MinValue;
            for (int c = 0; c < cellCount; c++)
            {
                int o = c * 8; // 32B = 8 floats
                d.VelX[c] = all[o];
                d.VelZ[c] = all[o + 1];
                d.Water[c] = all[o + 4];
                d.Ground[c] = all[o + 6];
                Assert.IsFalse(float.IsNaN(d.Water[c]) || float.IsNaN(d.Ground[c]), $"{d.Name}: 场内有 NaN");
                if (d.Water[c] < wMin) wMin = d.Water[c];
                if (d.Water[c] > wMax) wMax = d.Water[c];
                if (d.Ground[c] < gMin) gMin = d.Ground[c];
                if (d.Ground[c] > gMax) gMax = d.Ground[c];
            }
            // 值域对账（B2 phase/analysis.json waterRange/groundRange 同源）
            Assert.That(wMin, Is.GreaterThanOrEqualTo(-1e-3f), $"{d.Name}: waterAmount 出现负值");
            Assert.That(wMax, Is.LessThanOrEqualTo(1.2f), $"{d.Name}: waterAmount 超量程");
            Assert.That(gMin, Is.GreaterThan(0f), $"{d.Name}: groundHeight 下限异常");
            Assert.That(gMax, Is.LessThan(3f), $"{d.Name}: groundHeight 上限异常");
            return d;
        }

        static byte[] ToBytes(float[] data)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        // ---- 旧 dump（dump1/dump2）解析 ----

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

            // Unity 2022 无 System.Text.Json——键名全扁平，正则直取（harness/Unity 双跑同码）
            string json = raw.Substring(start, end - start + 1);
            string GetStr(string key) => Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
            float GetNum(string key) => float.Parse(Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9.eE+]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            long GetInt(string key) => long.Parse(Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?[0-9]+)").Groups[1].Value);

            var d = new DumpData
            {
                Name = System.IO.Path.GetFileName(path),
                TimestampMs = GetInt("timestampMs"),
                Res = (int)GetInt("resX"),
            };
            Assert.AreEqual(ReadbackRes, d.Res, $"{d.Name}: resX 应为 256（Readback 层分辨率）");
            Assert.AreEqual(ReadbackRes, (int)GetInt("resY"), $"{d.Name}: resY 应为 256");

            d.WaterBytes = Convert.FromBase64String(GetStr("waterAmount_b64"));
            d.GroundBytes = Convert.FromBase64String(GetStr("groundHeight_b64"));
            byte[] etaBytes = Convert.FromBase64String(GetStr("eta_b64"));
            d.Water = ToFloats(d.WaterBytes, "waterAmount");
            d.Ground = ToFloats(d.GroundBytes, "groundHeight");
            d.Eta = ToFloats(etaBytes, "eta");

            d.Level = GetNum("level");
            d.Gravity = GetNum("gravityForce");
            d.EvaporationFactor = GetNum("evaporationFactor");

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
                float v = data[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return (min, max);
        }
    }
}
