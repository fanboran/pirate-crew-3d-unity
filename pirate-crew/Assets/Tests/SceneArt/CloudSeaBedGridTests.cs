using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.SceneArt.CloudSea;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 云端漫步海床高度格的纯 C# 用例（无头可跑，不 new 任何 Unity 对象）。
    ///
    /// 【覆盖】
    ///   · 平面可复原：格点落在平面高度上时，双线性查高逐点等于平面公式（鱼群贴沙的精度基线）；
    ///   · 取高不取均：同格多个顶点取最高（活物不能穿的那条线）；
    ///   · 没铺到的角落由邻格填满、不残留无穷大；一个顶点都没有时全 0 且不死循环；
    ///   · 方格外钳到边缘；方格外的顶点不入格。
    /// </summary>
    public class CloudSeaBedGridTests
    {
        const int Cells = 16;
        const float Half = 4f;

        /// <summary>平面的格点采样：y = 0.1x − 0.2z，顶点正好落在每个格点上。</summary>
        static List<Vector3> PlaneNodes()
        {
            var points = new List<Vector3>();
            for (int iz = 0; iz <= Cells; iz++)
            {
                for (int ix = 0; ix <= Cells; ix++)
                {
                    float x = -Half + 2f * Half * ix / Cells;
                    float z = -Half + 2f * Half * iz / Cells;
                    points.Add(new Vector3(x, 0.1f * x - 0.2f * z, z));
                }
            }
            return points;
        }

        [Test]
        public void Build_PlaneNodes_SampleReproducesPlane()
        {
            float[] grid = CloudSeaBedGrid.Build(PlaneNodes(), Cells, Half);

            Assert.AreEqual((Cells + 1) * (Cells + 1), grid.Length, "格点数应为 (cells + 1)²");
            Assert.AreEqual(0.13f, CloudSeaBedGrid.Sample(grid, Cells, Half, 1.3f, 0f), 1e-4f);
            Assert.AreEqual(0.42f, CloudSeaBedGrid.Sample(grid, Cells, Half, 0f, -2.1f), 1e-4f);
            Assert.AreEqual(0.55f, CloudSeaBedGrid.Sample(grid, Cells, Half, 1.3f, -2.1f), 1e-4f);
        }

        [Test]
        public void Build_SeveralVerticesInOneCell_TakesHighest()
        {
            var points = new List<Vector3>
            {
                new Vector3(0.10f, -2.5f, 0.10f),
                new Vector3(0.20f, -1.2f, 0.20f),
            };
            float[] grid = CloudSeaBedGrid.Build(points, Cells, Half);

            Assert.AreEqual(-1.2f, CloudSeaBedGrid.Sample(grid, Cells, Half, 0.05f, 0.05f), 1e-4f,
                "同一格里应以最高的顶点为准——取平均会让高度线落到沙面以下");
        }

        [Test]
        public void Build_PartialCoverage_FillsEmptyFromNeighbours_WithoutInfinity()
        {
            // 只铺左半边（x ≤ 0）的平底：右半边没有顶点，应由左半边的高度填满。
            var points = new List<Vector3>();
            for (int iz = 0; iz <= Cells; iz++)
                for (int ix = 0; ix <= Cells / 2; ix++)
                    points.Add(new Vector3(-Half + 2f * Half * ix / Cells, -3f, -Half + 2f * Half * iz / Cells));

            float[] grid = CloudSeaBedGrid.Build(points, Cells, Half);

            foreach (float h in grid)
                Assert.IsFalse(float.IsInfinity(h) || float.IsNaN(h), "高度格里不应残留无穷大");
            Assert.AreEqual(-3f, CloudSeaBedGrid.Sample(grid, Cells, Half, 3.5f, 0f), 1e-4f);
        }

        [Test]
        public void Build_NoPoints_IsAllZero()
        {
            float[] grid = CloudSeaBedGrid.Build(new List<Vector3>(), Cells, Half);

            Assert.AreEqual((Cells + 1) * (Cells + 1), grid.Length);
            foreach (float h in grid)
                Assert.AreEqual(0f, h, 1e-6f, "没有海床时高度按 0（不算海床，也不能死循环）");
        }

        [Test]
        public void Sample_OutsideRange_ClampsToEdge()
        {
            // 沿 X 的斜坡：y = x，格点在 [-2, 2]。
            const int cells = 8;
            const float half = 2f;
            var points = new List<Vector3>();
            for (int iz = 0; iz <= cells; iz++)
                for (int ix = 0; ix <= cells; ix++)
                    points.Add(new Vector3(-half + 2f * half * ix / cells, -half + 2f * half * ix / cells, 0f));

            float[] grid = CloudSeaBedGrid.Build(points, cells, half);

            Assert.AreEqual(2f, CloudSeaBedGrid.Sample(grid, cells, half, 9f, 0f), 1e-4f, "右边界之外钳到右边缘");
            Assert.AreEqual(-2f, CloudSeaBedGrid.Sample(grid, cells, half, -9f, 0f), 1e-4f, "左边界之外钳到左边缘");
            Assert.AreEqual(0.5f, CloudSeaBedGrid.Sample(grid, cells, half, 0.5f, 0f), 1e-4f);
        }

        [Test]
        public void Build_PointsOutsideRange_AreIgnored()
        {
            var points = new List<Vector3> { new Vector3(0f, -3f, 0f), new Vector3(500f, 900f, 500f) };
            float[] grid = CloudSeaBedGrid.Build(points, Cells, Half);

            Assert.AreEqual(-3f, CloudSeaBedGrid.Sample(grid, Cells, Half, 0f, 0f), 1e-4f,
                "方格覆盖范围外的顶点（远景海床）不应影响格内高度");
        }
    }
}
