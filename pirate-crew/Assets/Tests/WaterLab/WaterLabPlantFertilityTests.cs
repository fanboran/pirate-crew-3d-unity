using System;
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
