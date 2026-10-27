using System;
using System.IO;
using NUnit.Framework;
using PirateCrew.WaterLab;
using UnityEngine;

namespace PirateCrew.WaterLab.Tests
{
    /// <summary>
    /// <see cref="WaterLabReplicaSnapshot"/> 契约：主菜单快照（I7 数据底座）的加载
    /// 四角自检、采样语义（格心恒等/值域包络/穿岸单调）、水量量程与统计对账。
    /// 统计基准 = frida 运行时只读 dump3 的元数据
    /// <c>external/sandcastle-ref/rt/dumps/menu-snapshot/snapshot_meta.json</c>
    ///（B-terrain.md §2 真值表），容差 ±0.01（验收口径）。
    /// </summary>
    [TestFixture]
    public class WaterLabReplicaSnapshotTests
    {
        // snapshot_meta.json stats.groundHeight / stats.waterAmount（dump3，float32 原值）
        const float MetaGroundMin = 0.032356821f;
        const float MetaGroundMax = 2.616257906f;
        const float MetaGroundMean = 0.996679544f;
        const float MetaWaterMin = 0f;
        const float MetaWaterMax = 1.038169026f;
        const float MetaWaterMean = 0.263206363f;
        const float MetaTolerance = 0.01f;

        string _dir;

        [SetUp]
        public void LoadSnapshot()
        {
            if (WaterLabReplicaSnapshot.HasData)
                return;
            _dir = LocateSnapshotDirectory();
            if (_dir == null)
                Assert.Ignore("快照资产未找到（Assets/Art/WaterLabReplica/Menu*.bytes 缺失，或设 PC3D_WATERLAB_REPLICA_DIR 指向该目录）");
            WaterLabReplicaSnapshot.Load(
                File.ReadAllBytes(Path.Combine(_dir, "MenuGround256.bytes")),
                File.ReadAllBytes(Path.Combine(_dir, "MenuWater256.bytes")));
        }

        [TearDown]
        public void UnloadSnapshot()
        {
            WaterLabReplicaSnapshot.Unload(); // 静态状态不跨测试泄漏（TerrainHeight 委托是全局的）
        }

        // ------------------------------------------------------------------
        // ① 加载与四角自检（B-terrain §5.2：row0 侧干滩、row255 侧海底）
        // ------------------------------------------------------------------

        [Test]
        public void Load_PassesCornerOrientationCheck()
        {
            Assert.That(WaterLabReplicaSnapshot.HasData, Is.True, "快照应加载成功（含四角自检）");
            // 前景（z−15，row0）两侧角都是干滩，海侧（z+15，row255）两侧角都是海底——
            // 只查对角会漏掉 row/col 整体写反（对角两格数值巧合相同），四角全查。
            Assert.That(WaterLabReplicaSnapshot.SampleGround(-14.9f, -14.9f), Is.GreaterThan(1.2f),
                "(−,−) 角应为干滩（meta：g[0,0]≈1.95）");
            Assert.That(WaterLabReplicaSnapshot.SampleGround(+14.9f, -14.9f), Is.GreaterThan(1.2f),
                "(+,−) 角应为干滩（row0 整行干沙，rowMean≈1.99）");
            Assert.That(WaterLabReplicaSnapshot.SampleGround(-14.9f, +14.9f), Is.LessThan(0.6f),
                "(−,+) 角应为海底（row255 整行海床，rowMean≈0.04）");
            Assert.That(WaterLabReplicaSnapshot.SampleGround(+14.9f, +14.9f), Is.LessThan(0.6f),
                "(+,+) 角应为海底（meta：g[255,255]≈0.03）");
        }

        [Test]
        public void Load_RejectsBadLengthAndFlippedAxis()
        {
            WaterLabReplicaSnapshot.Unload(); // 从空态出发验证"失败不留半加载状态"
            var water = new byte[256 * 256 * 4];
            Assert.Throws<ArgumentException>(() =>
                WaterLabReplicaSnapshot.Load(new byte[10], water), "长度不符应拒绝");
            Assert.That(WaterLabReplicaSnapshot.HasData, Is.False, "失败的加载不得留下半加载状态");

            // 常值场没有穿岸轴（四角干湿特征全同）——翻转自检必须拒绝
            var flat = new float[256 * 256];
            var flatBytes = new byte[flat.Length * 4];
            Buffer.BlockCopy(flat, 0, flatBytes, 0, flatBytes.Length);
            Assert.Throws<ArgumentException>(() =>
                WaterLabReplicaSnapshot.Load(flatBytes, water), "无干湿特征的场应被四角自检拒绝");
            Assert.That(WaterLabReplicaSnapshot.HasData, Is.False);
        }

        // ------------------------------------------------------------------
        // ② 采样语义：格心恒等（映射公式）、64 随机点值域包络、沿岸轴单调带
        // ------------------------------------------------------------------

        [Test]
        public void Sampling_IsExactAtCellCenters()
        {
            // idx_f = world·(1/dx) + (res/2−0.5)：格心 world=(i+0.5)·dx−15 → idx 恰为 i
            //（dx=15/128 是二进制小数，全程无舍入），采样必须逐位返回原格值。
            var rnd = new System.Random(20261027);
            for (int n = 0; n < 64; n++)
            {
                int col = rnd.Next(WaterLabReplicaSnapshot.Resolution);
                int row = rnd.Next(WaterLabReplicaSnapshot.Resolution);
                float wx = -15f + (col + 0.5f) * WaterLabReplicaSnapshot.CellSize;
                float wz = -15f + (row + 0.5f) * WaterLabReplicaSnapshot.CellSize;
                int idx = row * WaterLabReplicaSnapshot.Resolution + col; // row=Z！
                Assert.That(WaterLabReplicaSnapshot.SampleGround(wx, wz),
                    Is.EqualTo(WaterLabReplicaSnapshot.GroundData[idx]).Within(1e-4f),
                    $"格心 ({col},{row}) 采样应恒等于 g[{idx}]");
                Assert.That(WaterLabReplicaSnapshot.SampleWater(wx, wz),
                    Is.EqualTo(WaterLabReplicaSnapshot.WaterData[idx]).Within(1e-4f),
                    $"格心 ({col},{row}) 采样应恒等于 w[{idx}]");
            }
        }

        [Test]
        public void RandomSamples_StayInsideDataEnvelope()
        {
            // 双线性是凸组合：任何世界点（含域外 clamp 延伸）的采样值都在数据包络内。
            var rnd = new System.Random(20261027);
            for (int n = 0; n < 64; n++)
            {
                float wx = (float)(rnd.NextDouble() * 2.0 - 1.0) * 15f;
                float wz = (float)(rnd.NextDouble() * 2.0 - 1.0) * 15f;
                float g = WaterLabReplicaSnapshot.SampleGround(wx, wz);
                Assert.IsFalse(float.IsNaN(g) || float.IsInfinity(g), $"({wx:F2},{wz:F2}) 采样非有限");
                Assert.That(g, Is.InRange(WaterLabReplicaSnapshot.GroundMin - 1e-3f,
                    WaterLabReplicaSnapshot.GroundMax + 1e-3f), $"({wx:F2},{wz:F2}) 地形采样越出数据包络");
                float w = WaterLabReplicaSnapshot.SampleWater(wx, wz);
                Assert.That(w, Is.InRange(WaterLabReplicaSnapshot.WaterMin - 1e-3f,
                    WaterLabReplicaSnapshot.WaterMax + 1e-3f), $"({wx:F2},{wz:F2}) 水量采样越出数据包络");
            }
        }

        [Test]
        public void CrossShoreDirection_DecreasesFromDryToSea()
        {
            // 穿岸单调（带均值口径）：row0 侧（干滩）的地形带均值显著高于 row255 侧（海底）。
            // 逐点不单调（滩肩/沙波/礁石都是合法起伏），只在穿岸方向上对比两端带。
            float dryBand = 0f, seaBand = 0f;
            int samples = 16;
            for (int i = 0; i < samples; i++)
            {
                float x = Mathf.Lerp(-13f, 13f, i / (float)(samples - 1));
                dryBand += WaterLabReplicaSnapshot.SampleGround(x, -13f);
                seaBand += WaterLabReplicaSnapshot.SampleGround(x, +13f);
            }
            dryBand /= samples;
            seaBand /= samples;
            Assert.That(dryBand - seaBand, Is.GreaterThan(1.0f),
                $"干滩带均值 {dryBand:F2} − 海底带均值 {seaBand:F2} 应 >1.0（row0=z−15 干滩的轴向契约）");
        }

        // ------------------------------------------------------------------
        // ③ 水量量程：物理上非负、上限在快照包络内（meta max 1.038）
        // ------------------------------------------------------------------

        [Test]
        public void WaterAmount_StaysInPhysicalRange()
        {
            Assert.That(WaterLabReplicaSnapshot.WaterMin, Is.GreaterThanOrEqualTo(0f), "水量出现负值");
            Assert.That(WaterLabReplicaSnapshot.WaterMax, Is.LessThanOrEqualTo(1.05f),
                "水量上限超出快照包络（meta max 1.038）");
            var rnd = new System.Random(20261027);
            for (int n = 0; n < 64; n++)
            {
                float wx = (float)(rnd.NextDouble() * 2.0 - 1.0) * 15f;
                float wz = (float)(rnd.NextDouble() * 2.0 - 1.0) * 15f;
                Assert.That(WaterLabReplicaSnapshot.SampleWater(wx, wz), Is.InRange(0f, 1.05f));
            }
        }

        // ------------------------------------------------------------------
        // ④ 统计对账：min/max/mean 与 frida meta ±0.01
        // ------------------------------------------------------------------

        [Test]
        public void SnapshotStats_MatchFridaMeta()
        {
            Assert.That(WaterLabReplicaSnapshot.GroundMin, Is.EqualTo(MetaGroundMin).Within(MetaTolerance));
            Assert.That(WaterLabReplicaSnapshot.GroundMax, Is.EqualTo(MetaGroundMax).Within(MetaTolerance));
            Assert.That(WaterLabReplicaSnapshot.GroundMean, Is.EqualTo(MetaGroundMean).Within(MetaTolerance));
            Assert.That(WaterLabReplicaSnapshot.WaterMin, Is.EqualTo(MetaWaterMin).Within(MetaTolerance));
            Assert.That(WaterLabReplicaSnapshot.WaterMax, Is.EqualTo(MetaWaterMax).Within(MetaTolerance));
            Assert.That(WaterLabReplicaSnapshot.WaterMean, Is.EqualTo(MetaWaterMean).Within(MetaTolerance));
        }

        // ------------------------------------------------------------------
        // 委托：TerrainHeight 在快照加载时走采样器、卸载后回退程序剖面
        // ------------------------------------------------------------------

        [Test]
        public void TerrainHeight_DelegatesWhileLoadedAndFallsBackAfterUnload()
        {
            float wx = 3.7f, wz = 10.3f; // 海侧（row≈216，海床），程序剖面这里是干沙台
            float snapshotValue = WaterLabReplicaSnapshot.SampleGround(wx, wz);
            Assert.That(WaterLabRules.TerrainHeight(wx, wz), Is.EqualTo(snapshotValue).Within(1e-5f),
                "快照加载时 TerrainHeight 应逐点委托采样器");

            WaterLabReplicaSnapshot.Unload();
            Assert.That(WaterLabReplicaSnapshot.HasData, Is.False);
            Assert.That(Mathf.Abs(WaterLabRules.TerrainHeight(wx, wz) - snapshotValue), Is.GreaterThan(0.3f),
                "卸载后 TerrainHeight 应回到程序剖面（该点两套地形差显著）");
            // 重新加载，恢复本 fixture 其余测试的公共前置（TearDown 会再清一次）
            WaterLabReplicaSnapshot.Load(
                File.ReadAllBytes(Path.Combine(_dir, "MenuGround256.bytes")),
                File.ReadAllBytes(Path.Combine(_dir, "MenuWater256.bytes")));
        }

        // ---- 快照目录定位 ----

        /// <summary>
        /// Unity 编辑器直接走工程相对路径；harness（dotnet）从程序集目录逐级上溯找
        /// 仓库内的工作树副本（主仓与 temp/water-lab worktree 两种布局都覆盖），
        /// 环境变量 PC3D_WATERLAB_REPLICA_DIR 可显式指路。
        /// </summary>
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
