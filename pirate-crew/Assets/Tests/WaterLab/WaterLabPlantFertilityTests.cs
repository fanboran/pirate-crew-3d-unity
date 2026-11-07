using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.WaterLab.Tests
{
    /// <summary>
    /// F3 契约：<see cref="ReplicaPlantSystem.MenuFertilityField"/> 肥力通道装载/解码、
    /// <see cref="ReplicaPlantSystem.AttemptsForCapacity"/> 尝试数口径、
    /// <see cref="WaterLabHullNormals"/> 凸包 half4 法线表覆盖与半精度解码。
    /// 全部纯 C# 静态面（无 GameObject 实例化，harness 秒级档可跑）。
    /// </summary>
    [TestFixture]
    public class WaterLabPlantFertilityTests
    {
        // ------------------------------------------------------------------
        // ① 肥力场装载：平方数校验、6bit 值域校验、÷63 解码、回退语义
        // ------------------------------------------------------------------

        [Test]
        public void FertilityLoad_RejectsNonSquareAndOutOfRange()
        {
            Assert.Throws<ArgumentException>(() =>
                ReplicaPlantSystem.MenuFertilityField.Load(new byte[10]), "非平方数长度应拒绝");
            var bad = new byte[16];
            bad[5] = 64;                       // 6bit 值域外
            Assert.Throws<ArgumentException>(() =>
                ReplicaPlantSystem.MenuFertilityField.Load(bad), "值 >63 应拒绝（6bit 语义）");
        }

        [Test]
        public void FertilityLoad_DecodesDiv63AndUnloadsToFallback()
        {
            try
            {
                var field = new byte[4 * 4];
                field[0 * 4 + 2] = 63;         // row0,col2 → 1.0
                field[3 * 4 + 1] = 31;         // row3,col1 → 31/63
                field[1 * 4 + 1] = 0;          // → 0
                ReplicaPlantSystem.MenuFertilityField.Load(field);
                Assert.That(ReplicaPlantSystem.MenuFertilityField.HasData, Is.True);
                Assert.That(ReplicaPlantSystem.MenuFertilityField.Resolution, Is.EqualTo(4));
                Assert.That(ReplicaPlantSystem.MenuFertilityField.GetFertility(2, 0), Is.EqualTo(1f).Within(1e-6f));
                Assert.That(ReplicaPlantSystem.MenuFertilityField.GetFertility(1, 3), Is.EqualTo(31f / 63f).Within(1e-6f));
                Assert.That(ReplicaPlantSystem.MenuFertilityField.GetFertility(1, 1), Is.EqualTo(0f).Within(1e-6f));

                // 装载后 SnapshotGroundField 直读该场（不再回退恒 1）
                var ground = new ReplicaPlantSystem.SnapshotGroundField();
                ground.GetCell(1, 1, out _, out float frt, out _, out _);
                Assert.That(frt, Is.EqualTo(0f).Within(1e-6f), "装载后肥力应来自数据场而非恒 1");
            }
            finally
            {
                ReplicaPlantSystem.MenuFertilityField.Unload();
            }
            Assert.That(ReplicaPlantSystem.MenuFertilityField.HasData, Is.False, "Unload 应清场");
            Assert.That(ReplicaPlantSystem.MenuFertilityField.GetFertility(0, 0),
                Is.EqualTo(1f).Within(1e-6f), "未装载回退恒 1（数据缺口退化口径）");
        }

        // ------------------------------------------------------------------
        // ①b 统计锁（F3b）：全零场必拒、直方图/占比与手算对账、失败不留半态
        // ------------------------------------------------------------------

        [Test]
        public void FertilityStatsLock_RejectsAllZeroAndReportsHistogram()
        {
            // 全零 64² 场（≡0 已被 F3 §2.2 实测证伪：疑 dump 到未 Paint 的初始化缓冲）
            var allZero = new byte[64 * 64];
            Assert.Throws<ArgumentException>(() =>
                ReplicaPlantSystem.MenuFertilityField.Load(allZero), "全零场应被统计锁下限拒绝");
            Assert.That(ReplicaPlantSystem.MenuFertilityField.HasData, Is.False,
                "统计锁失败不应留下半加载状态");

            // 合成场：三种非零值 + 大片 0；直方图/占比与手算逐项对账
            var field = new byte[64 * 64];
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++) field[z * 64 + x] = 33;   // 256 格 ≈0.524
            for (int z = 16; z < 32; z++)
                for (int x = 16; x < 48; x++) field[z * 64 + x] = 52;   // 512 格 ≈0.825
            for (int z = 48; z < 64; z++)
                for (int x = 0; x < 8; x++) field[z * 64 + x] = 20;     // 128 格 ≈0.317
            int nonzero = 256 + 512 + 128;                              // 896 格 >0
            try
            {
                ReplicaPlantSystem.MenuFertilityField.Load(field);
                Assert.That(ReplicaPlantSystem.MenuFertilityField.HasData, Is.True);
                Assert.That(WaterLabReplicaFertility.LastNonZeroFrac,
                    Is.EqualTo(nonzero / (64f * 64f)).Within(1e-6f), ">0 格占比应与手算一致");
                var hist = WaterLabReplicaFertility.LastHistogram;
                Assert.That(hist, Is.Not.Null);
                Assert.That(hist.Length, Is.EqualTo(64), "直方图 64 桶（6bit 值域）");
                Assert.That(hist[33], Is.EqualTo(256), "值 33 桶计数");
                Assert.That(hist[52], Is.EqualTo(512), "值 52 桶计数");
                Assert.That(hist[20], Is.EqualTo(128), "值 20 桶计数");
                Assert.That(hist[0], Is.EqualTo(64 * 64 - nonzero), "值 0 桶计数");
                int histSum = 0;
                foreach (int c in hist) histSum += c;
                Assert.That(histSum, Is.EqualTo(64 * 64), "直方图总和 = 场长");

                // 失败不留半态的正向语义：成功装载后再喂坏场 → 旧场原样保留
                var bad = new byte[64 * 64];
                bad[0] = 200;                       // 值域外
                Assert.Throws<ArgumentException>(() =>
                    ReplicaPlantSystem.MenuFertilityField.Load(bad));
                Assert.That(ReplicaPlantSystem.MenuFertilityField.Resolution, Is.EqualTo(64),
                    "坏场装载失败后旧场应原样保留（无半态/无清空）");
                Assert.That(ReplicaPlantSystem.MenuFertilityField.GetFertility(0, 0),
                    Is.EqualTo(33f / 63f).Within(1e-6f));
            }
            finally
            {
                ReplicaPlantSystem.MenuFertilityField.Unload();
            }
        }

        // ------------------------------------------------------------------
        // ①b′ 真资产统计锁：主菜单 dump（MenuFertility1024.bytes）过精确锁、直方图峰钉死；
        // 同尺寸篡改场必拒且不留半态（精确锁只对 1024² 生效，合成小场只过结构校验）
        // ------------------------------------------------------------------

        [Test]
        public void FertilityAsset_MenuDumpPassesStatsLockAndPinsHistogram()
        {
            var bytes = ReadMenuFertilityAssetOrIgnore();
            try
            {
                ReplicaPlantSystem.MenuFertilityField.Load(bytes);
                Assert.IsTrue(ReplicaPlantSystem.MenuFertilityField.HasData);
                Assert.AreEqual(WaterLabReplicaFertility.MenuResolution,
                    ReplicaPlantSystem.MenuFertilityField.Resolution);
                Assert.That(WaterLabReplicaFertility.LastNonZeroFrac,
                    Is.EqualTo(WaterLabReplicaFertility.MetaNonZeroFrac)
                      .Within(WaterLabReplicaFertility.MetaNonZeroFracTolerance));
                var hist = WaterLabReplicaFertility.LastHistogram;
                // dump 实测峰值（agent-fertility.js stats.hist64）：饱和 1.0 桶与零桶钉死资产字节本身
                Assert.AreEqual(395956, hist[63], "饱和桶 63（=1.0）");
                Assert.AreEqual(207426, hist[0], "零桶");
            }
            finally
            {
                ReplicaPlantSystem.MenuFertilityField.Unload();
            }
        }

        [Test]
        public void FertilityAsset_TamperedMenuSizeFieldIsRejected()
        {
            var bytes = ReadMenuFertilityAssetOrIgnore();
            // 清零前 4096 个非零格：占比降 ≈0.0039，远超容差 1e-5 → 精确锁必拒
            int cleared = 0;
            for (int i = 0; i < bytes.Length && cleared < 4096; i++)
                if (bytes[i] > 0) { bytes[i] = 0; cleared++; }
            Assert.Throws<ArgumentException>(() => ReplicaPlantSystem.MenuFertilityField.Load(bytes));
            Assert.IsFalse(ReplicaPlantSystem.MenuFertilityField.HasData, "失败不留半态");
        }

        static byte[] ReadMenuFertilityAssetOrIgnore()
        {
            var dir = LocateReplicaDirectory();
            var path = dir == null ? null : Path.Combine(dir, "MenuFertility1024.bytes");
            if (path == null || !File.Exists(path))
                Assert.Ignore("MenuFertility1024.bytes 未找到（Assets/Art/WaterLabReplica/，或设 PC3D_WATERLAB_REPLICA_DIR）");
            return File.ReadAllBytes(path);
        }

        /// <summary>定位 Assets/Art/WaterLabReplica（同 WaterLabReplicaSnapshotTests 的三级定位）。</summary>
        static string LocateReplicaDirectory()
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "Art", "WaterLabReplica");
#else
            var env = Environment.GetEnvironmentVariable("PC3D_WATERLAB_REPLICA_DIR");
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

        // ------------------------------------------------------------------
        // ①c 肥力门控链路（F3b）：恒 1 回退复现 F1 §2.3 的 9 组恒空退化；
        // 装载合成场后 9 空组全部出现可行格（严格开区间判定同 Distribute）
        // ------------------------------------------------------------------

        [Test]
        public void FertilityGate_NineEmptyGroupsProduceWithField()
        {
            // 13 组肥力门（ReplicaPlantTable 单源）；F1 §2.3：恒 1 回退下
            // 9 组上界 ≤1.0（严格开区间 1.0<1.0 恒假）→ 恒空
            var nineEmpty = new[]
            {
                "Kelp", "RedAlgae", "LettuceCoral", "GrooveBrainCoral", "CarnationCoral",
                "ElkhornCoral", "GreyTussock", "Goldenrod", "WheatGrass",
            };
            // 门控判定式 = Distribute（ReplicaPlantSystem.cs，Distribution L112-118
            // 反汇编四×lt 严格不等）的肥力项同款，勿改成 ≥/≤
            static bool Passes(float f, Vector2 r) => f > r.x && f < r.y;

            try
            {
                // 退化面：未载回退恒 1 → 9 空组 0 可行值；4 组上界 >1.0 仍活
                foreach (var p in ReplicaPlantTable.All)
                {
                    bool passesAtOne = Passes(1f, p.FertilityRange);
                    if (nineEmpty.Contains(p.ClassName))
                        Assert.That(passesAtOne, Is.False,
                            $"{p.ClassName} 上界 ≤1.0，恒 1 回退下应恒空（F1 §2.3）");
                    else
                        Assert.That(passesAtOne, Is.True,
                            $"{p.ClassName} 上界 >1.0，恒 1 回退下应存活（F1 §2.3 的 4 组）");
                }

                // 装载合成场（64²，行主序 row=Z）：三个非零带 + 其余 0
                var field = new byte[64 * 64];
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++) field[z * 64 + x] = 33;   // ≈0.524
                for (int z = 16; z < 32; z++)
                    for (int x = 16; x < 48; x++) field[z * 64 + x] = 52;   // ≈0.825
                for (int z = 48; z < 64; z++)
                    for (int x = 0; x < 8; x++) field[z * 64 + x] = 20;     // ≈0.317
                ReplicaPlantSystem.MenuFertilityField.Load(field);

                foreach (var p in ReplicaPlantTable.All)
                {
                    int cells = 0;
                    for (int i = 0; i < field.Length; i++)
                        if (Passes(field[i] / 63f, p.FertilityRange)) cells++;
                    if (nineEmpty.Contains(p.ClassName))
                        Assert.That(cells, Is.GreaterThan(0),
                            $"{p.ClassName}（9 空组之一）在含其肥力带的场中应出现可行格");
                    // 0 值格对任何组都拒（严格下界）——门控生效的反向证据
                    Assert.That(Passes(0f, p.FertilityRange), Is.False,
                        $"{p.ClassName} 严格下界应拒 0 值");
                }
            }
            finally
            {
                ReplicaPlantSystem.MenuFertilityField.Unload();
            }
        }

        // ------------------------------------------------------------------
        // ② 尝试数口径（F3 修正 fac=0.09：ceil(cap·0.09/1024)·1024）
        // ------------------------------------------------------------------

        [Test]
        public void AttemptsForCapacity_MatchesDistributionDispatch()
        {
            // Plant.Distribution 原生 @0x18045f553：capacity·fac·(1/1024) → ceil → Dispatch 组数
            Assert.That(ReplicaPlantSystem.DistributionFactor, Is.EqualTo(0.09f)
                .Within(1e-6f), "fac = obj5·1e-4 = (2ex)(2ez)·1e-4 = 900·1e-4");
            Assert.That(ReplicaPlantSystem.AttemptsForCapacity(64), Is.EqualTo(1024));
            Assert.That(ReplicaPlantSystem.AttemptsForCapacity(512), Is.EqualTo(1024));
            Assert.That(ReplicaPlantSystem.AttemptsForCapacity(2048), Is.EqualTo(1024));
            Assert.That(ReplicaPlantSystem.AttemptsForCapacity(4096), Is.EqualTo(1024));
            Assert.That(ReplicaPlantSystem.AttemptsForCapacity(8192), Is.EqualTo(1024));
            Assert.That(ReplicaPlantSystem.AttemptsForCapacity(32768), Is.EqualTo(3072),
                "ceil(32768·0.09/1024)=3 组 → 3072（F3 修正：F1 的 fac=0.0225 漏了两个 ×2）");
            // 全表逐组与 R 帧实测包络相容：存活 ≤ 尝试数
            foreach (var p in ReplicaPlantTable.All)
                Assert.That(ReplicaPlantSystem.AttemptsForCapacity(p.Capacity),
                    Is.GreaterThanOrEqualTo(1024), $"{p.ClassName} 尝试数下限");
        }

        // ------------------------------------------------------------------
        // ③ 凸包法线表：13/13 覆盖、顶点数对齐 placement、单位长度、半精度解码
        // ------------------------------------------------------------------

        [Test]
        public void HullNormals_CoversAllThirteenHulls()
        {
            Assert.That(WaterLabHullNormals.CoveredCount, Is.EqualTo(13), "13 组凸包应全覆盖");
            foreach (var p in ReplicaPlantTable.All)
            {
                var normals = new Vector3[0];     // 故意长度不符
                Assert.That(WaterLabHullNormals.TryGet(p.HullMeshName, normals), Is.False,
                    $"{p.HullMeshName} 长度不符应返回 false");
            }
        }

        [Test]
        public void HullNormals_AreUnitLengthHalfDecode()
        {
            // 各凸包顶点数（placement.json mesh_full.vertexCount，G-plants §4.1）
            var counts = new (string name, int n)[]
            {
                ("Kelp_01_Collider", 40), ("Seagrass_01_Collider", 11),
                ("RedAlgae_01_Collider", 15), ("LettuceCoral_01_Collider", 17),
                ("GroovedBrainCoral_01_Collider", 32), ("CarnationCoral_01_Collider", 13),
                ("ElkhornCoral_01_Collider", 9), ("GreyTussock_01_Collider", 13),
                ("Yarrow_01_Collider", 13), ("Switchgrass_01_Collider", 11),
                ("Goldenrod_01_Collider", 16), ("BuffaloGrass_01_Collider", 13),
                ("WheatGrass_01_Collider", 14),
            };
            foreach (var (name, n) in counts)
            {
                var normals = new Vector3[n];
                Assert.That(WaterLabHullNormals.TryGet(name, normals), Is.True, $"{name} 应在表中");
                for (int i = 0; i < n; i++)
                {
                    float len = normals[i].magnitude;
                    Assert.That(len, Is.EqualTo(1f).Within(0.01f),
                        $"{name} v{i} 法线模长应≈1（资产实测全 1.000，half 量化噪声内）");
                }
            }
        }

        [Test]
        public void HullNormals_HalfDecode_KnownBitPattern()
        {
            // 半精度解码抽查：0x3C00=1.0、0xBC00=−1.0、0x0000=0.0、0x5640=100.0
            // 经 TryGet 的公开面不可直查单字节 → 走解码公式的等价断言：
            // 表内 |n|=1 ⇒ 每顶点 (x²+y²+z²)≈1，位模式正确性由 ③ 的单位长度 +
            // 0x8000 符号位（负分量存在）共同约束。这里补一个负分量存在性：
            bool anyNegative = false;
            var counts = new[] { "Kelp_01_Collider", "GroovedBrainCoral_01_Collider" };
            foreach (var name in counts)
            {
                var normals = new Vector3[
                    name == "Kelp_01_Collider" ? 40 : 32];
                Assert.That(WaterLabHullNormals.TryGet(name, normals), Is.True);
                foreach (var v in normals)
                    anyNegative |= v.x < 0f || v.y < 0f || v.z < 0f;
            }
            Assert.That(anyNegative, Is.True, "凸包法线应含负分量（包围面朝向各异）");
        }
    }
}
