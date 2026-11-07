using System;
using System.IO;
using NUnit.Framework;
using PirateCrew.WaterLab;
using UnityEngine;

namespace PirateCrew.WaterLab.Tests
{
    /// <summary>
    /// 活沙 Ground（D 流件1）+ obstaclesMap 泵（D 流件2）+ Air 蒸发（D 流件3）的行为契约。
    ///
    /// 【稳定性门槛（任务验收口径）】域 30 m 下 46 s 模拟不发散、swash 带形态演化量级与
    /// B2 活沙实测（22 s ≈0.018 m，snapshot_meta.json stability[0] 的 groundHeight
    /// maxAbs 0.018010616）同数量级。CPU 参考走 <see cref="WaterLabRules.ReferenceSim.GroundStep"/>
    /// （与 compute 的 Ground* kernel 公式同源）；网格取 128² 保格距（2.93 cm）粗化域——
    /// 数值稳定性由「子步长 0.5·dx/√g + 滑移系数 0.02/s」的公式结构决定，与域尺寸无关，
    /// 边界效应在断言裕量内。
    /// </summary>
    [TestFixture]
    public class WaterLabGroundTests
    {
        // B2 实测活沙形态漂移（dump1→dump2 = 22.3 s 的 groundHeight maxAbs）
        const float B2SwashDrift22s = 0.018f;

        const int Cells = 128;
        const float Dx = WaterLabRules.GroundCellSize;

        float[] _height, _vx, _vz, _sed, _water, _hScratch, _sedScratch;

        /// <summary>合成 swash 剖面：X 向 1:20 斜坡（海平面 1.0 附近穿零）、沿 Z 恒定；
        /// 水位 = max(海平面 − 地形, 0) + 0.1 的薄水层——活沙演化的驱动场。</summary>
        void BuildSwashScene()
        {
            int n = Cells * Cells;
            _height = new float[n];
            _vx = new float[n];
            _vz = new float[n];
            _sed = new float[n];
            _water = new float[n];
            _hScratch = new float[n];
            _sedScratch = new float[n];
            for (int z = 0; z < Cells; z++)
            {
                for (int x = 0; x < Cells; x++)
                {
                    int i = x + z * Cells;
                    float worldX = (x + 0.5f) * Dx;
                    float ground = 1.0f + (worldX - Cells * Dx * 0.5f) * 0.05f; // 1:20 坡
                    _height[i] = ground;
                    _water[i] = Mathf.Max(WaterLabRules.SeaLevel - ground, 0f) + 0.1f;
                }
            }
        }

        void Step(float seconds)
        {
            float dt = WaterLabRules.GroundFixedStep;
            int steps = Mathf.CeilToInt(seconds / dt);
            for (int s = 0; s < steps; s++)
            {
                WaterLabRules.ReferenceSim.GroundStep(_height, _vx, _vz, _sed, _water,
                    Cells, Dx, dt, _hScratch, _sedScratch);
            }
        }

        [Test]
        public void GroundFixedStep_IsCflDerived()
        {
            // 原作 Ground.MaxDeltaTime = 0.5·cellSize/√gravityForce（Ground.Run 子步式）
            float expected = 0.5f * WaterLabRules.GroundCellSize / Mathf.Sqrt(WaterLabRules.GroundGravity);
            Assert.That(WaterLabRules.GroundFixedStep, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(WaterLabRules.GroundMaxVelocity,
                Is.EqualTo(Mathf.Sqrt(WaterLabRules.GroundGravity)).Within(1e-6f));
        }

        [Test]
        public void GroundStep_StaysStableAndConservative46s()
        {
            BuildSwashScene();
            double sumBefore = 0.0;
            for (int i = 0; i < _height.Length; i++)
                sumBefore += (double)_height[i] + _sed[i];

            Step(46f);

            double sumAfter = 0.0;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < _height.Length; i++)
            {
                if (float.IsNaN(_height[i]) || float.IsInfinity(_height[i]))
                    Assert.Fail($"第 {i} 格 46 s 后高度非有限——模拟发散");
                sumAfter += (double)_height[i] + _sed[i];
                if (_height[i] < min) min = _height[i];
                if (_height[i] > max) max = _height[i];
            }
            // 沙量守恒（滑移重分配 + 沉积交换只转移；kappa 低通的回漂是唯一泄漏源，应远小于总量）
            double drift = System.Math.Abs(sumAfter - sumBefore) / sumBefore;
            Assert.That(drift, Is.LessThan(0.001),
                $"46 s 总沙量漂移 {drift:P4} 超限——守恒被破坏");
            // 包络不发散：演化不越过初值 ±0.5 m（B2 实测 3.5 min 才漂 0.3 m）
            float baseMin = float.MaxValue, baseMax = float.MinValue;
            BuildSwashScene();
            for (int i = 0; i < _height.Length; i++)
            {
                if (_height[i] < baseMin) baseMin = _height[i];
                if (_height[i] > baseMax) baseMax = _height[i];
            }
            Assert.That(min, Is.GreaterThan(baseMin - 0.5f), "地形被过度侵蚀");
            Assert.That(max, Is.LessThan(baseMax + 0.5f), "地形被过度堆积");
        }

        [Test]
        public void GroundStep_SwashEvolution_MatchesB2OrderOfMagnitude()
        {
            BuildSwashScene();
            var before = (float[])_height.Clone();

            Step(22f);

            float maxAbs = 0f;
            for (int i = 0; i < _height.Length; i++)
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(_height[i] - before[i]));
            // B2 实测 22 s ≈0.018 m：数量级窗 [1 mm, 0.2 m]——「活沙在动」且「不是雪崩」
            Assert.That(maxAbs, Is.GreaterThan(0.001f),
                $"22 s 形态演化 max|Δh|={maxAbs:F6} 低于 1 mm——活沙未演化");
            Assert.That(maxAbs, Is.LessThan(0.2f),
                $"22 s 形态演化 max|Δh|={maxAbs:F6} 超出 B2 数量级——演化过猛");
        }

        [Test]
        public void ObstacleDelta_MatchesB2StatisticsAndPumpGate()
        {
            var dir = LocateSnapshotDirectory();
            if (dir == null)
                Assert.Ignore("快照资产未找到（Assets/Art/WaterLabReplica/Menu*.bytes 缺失）");
            WaterLabReplicaObstacles.Load(
                File.ReadAllBytes(Path.Combine(dir, "MenuGround256.bytes")),
                File.ReadAllBytes(Path.Combine(dir, "MenuSand256.bytes")));
            Assert.That(WaterLabReplicaObstacles.HasData, Is.True);

            // B2 meta obstacles 四项统计锁（WaterLabReplicaObstacles.Load 内部已钉，
            // 这里再显式断言两处主锚）
            float maxDelta = 0f;
            foreach (float d in WaterLabReplicaObstacles.Delta)
                maxDelta = Mathf.Max(maxDelta, d);
            Assert.That(maxDelta, Is.EqualTo(WaterLabReplicaObstacles.MetaMaxDelta)
                .Within(WaterLabReplicaObstacles.MaxDeltaTolerance));

            // 泵门控行为：障碍格（delta>阈值）内，η 在顶 +0.1 窗内才过闸
            float top = 2.0f, sand = top - maxDelta;
            Assert.That(WaterLabRules.PumpGate(sand, top, top), Is.True, "η=顶 应过闸");
            Assert.That(WaterLabRules.PumpGate(sand, top, top + 0.1f), Is.False, "η=顶+0.1 应拒");
            Assert.That(WaterLabRules.PumpGate(sand, top, top - 0.01f), Is.False, "η<顶 应拒");
            Assert.That(WaterLabRules.PumpGate(sand, top, top + 0.05f), Is.True, "窗内应过闸");
            // 泵速率的相位式：uv.y 全域只贡献 0.0378 rad（§9 近均匀局部扰动）
            Assert.That(Mathf.Abs(WaterLabRules.PumpPhasePerV), Is.LessThan(0.05f));
            WaterLabReplicaObstacles.Unload();
            Assert.That(WaterLabReplicaObstacles.HasData, Is.False);
        }

        [Test]
        public void EvaporationRate_UsesExp2Semantics()
        {
            // X 流修正：exp2(−0.721348·湿度) = e^(−0.5·湿度)；主菜单水汽恒 0 → 基率恒等
            Assert.That(WaterLabRules.EvaporationRate(),
                Is.EqualTo(WaterLabRules.EvaporationBaseRate()).Within(1e-12f));
            Assert.That(WaterLabRules.EvaporationBaseRate(),
                Is.EqualTo(WaterLabRules.SunIrradiance * WaterLabRules.EvaporationFactor
                           * WaterLabRules.EvaporationScale).Within(1e-12f));
            // 衰减字面量的 exp2 折算：0.721348·ln2 ≈ 0.5
            Assert.That(WaterLabRules.EvaporationHumidityDecay * 0.6931472f,
                Is.EqualTo(0.5f).Within(1e-4f));
        }

        // ---- 快照目录定位（同 WaterLabReplicaSnapshotTests 的 harness 上溯策略）----

        static string LocateSnapshotDirectory()
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "Art", "WaterLabReplica");
#else
            var env = System.Environment.GetEnvironmentVariable("PC3D_WATERLAB_REPLICA_DIR");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "MenuGround256.bytes")))
                return env;
            var markers = new[]
            {
                Path.Combine("pirate-crew", "Assets", "Art", "WaterLabReplica"),
                Path.Combine("temp", "water-lab", "pirate-crew", "Assets", "Art", "WaterLabReplica"),
            };
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                foreach (var marker in markers)
                {
                    var candidate = Path.Combine(dir.FullName, marker);
                    if (File.Exists(Path.Combine(candidate, "MenuGround256.bytes")))
                        return candidate;
                }
                dir = dir.Parent;
            }
            return null;
#endif
        }
    }
}
