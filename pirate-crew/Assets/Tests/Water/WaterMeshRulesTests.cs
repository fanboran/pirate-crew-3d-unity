using NUnit.Framework;
using PirateCrew.PirateCrew.Water;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="WaterMeshRules"/> 测试：水面细分网格的格数/顶点数/采样充分性。
    /// 背景：水是 Cube（24 顶点），Gerstner 顶点位移必须靠细分布网格才画得出来。
    /// </summary>
    [TestFixture]
    public class WaterMeshRulesTests
    {
        [Test]
        public void CellCount_UsesTargetCellSize()
        {
            // 90 世界单位 @ 0.8 → 113 格（向上取整）。
            int cells = WaterMeshRules.CellCount(90f, 0.8f, 256);
            Assert.AreEqual(113, cells);
            Assert.That(WaterMeshRules.CellSize(90f, cells), Is.LessThanOrEqualTo(0.8f));
        }

        [Test]
        public void CellCount_ClampsToMaxAndMin()
        {
            Assert.AreEqual(192, WaterMeshRules.CellCount(100000f, 0.8f, 192));
            Assert.AreEqual(1, WaterMeshRules.CellCount(0f, 0.8f, 192));
            Assert.AreEqual(1, WaterMeshRules.CellCount(0.1f, 0.8f, 192));
        }

        [Test]
        public void VertexAndTriangleCounts_MatchFormula()
        {
            // 水的实际尺寸：50×17 竞技场 + 40 边距 = 90×57。
            int cx = WaterMeshRules.CellCount(90f, 0.8f, 192);
            int cz = WaterMeshRules.CellCount(57f, 0.8f, 192);

            Assert.AreEqual((cx + 1) * (cz + 1), WaterMeshRules.VertexCount(cx, cz));
            Assert.AreEqual(cx * cz * 2, WaterMeshRules.TriangleCount(cx, cz));
            Assert.AreEqual(cx * cz * 6, WaterMeshRules.IndexCount(cx, cz));

            // 预算核对：90×57 @0.8 → 113×72 格 ≈ 8.3k 顶点 / 16.3k 三角面（单 DrawCall）。
            Assert.That(WaterMeshRules.VertexCount(cx, cz), Is.LessThan(20000));
            Assert.That(WaterMeshRules.TriangleCount(cx, cz), Is.LessThan(40000));
        }

        [Test]
        public void SamplingIsAdequate_ForShortestDefaultWavelength()
        {
            int cx = WaterMeshRules.CellCount(90f, WaterMeshRules.DefaultCellSize, 192);
            int cz = WaterMeshRules.CellCount(57f, WaterMeshRules.DefaultCellSize, 192);
            float minWavelength = WaterRules.MinWavelengthOf(WaterRules.DefaultWaves);

            Assert.IsTrue(
                WaterMeshRules.IsSamplingAdequate(90f, 57f, cx, cz, minWavelength),
                "默认格距 0.8 对最短波长 2.4 应至少每波长 3 个顶点");
        }

        [Test]
        public void SamplingIsInadequate_WhenCoarse()
        {
            // 每轴只分 2 格 → 格距 45/28.5，远大于 2.4/3 → 不达标。
            Assert.IsFalse(WaterMeshRules.IsSamplingAdequate(90f, 57f, 2, 2, 2.4f));
        }
    }
}
