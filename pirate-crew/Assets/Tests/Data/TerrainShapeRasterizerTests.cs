using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// <see cref="TerrainShapeRasterizer"/> 的解析解断言（无头可跑）：
    /// 矩形/带洞/重叠岛的格中心判定、行主序索引、even-odd 轮廓。
    /// 迁移产物的「岛形 == 旧采样数组」逐格等价由迁移器自检与冻结签名（摘要逐字不变）双面钉住。
    /// </summary>
    [TestFixture]
    public class TerrainShapeRasterizerTests
    {
        static TerrainShape Shape(params TerrainIsland[] islands)
        {
            return new TerrainShape { blockWorldHeight = 0.5f, islands = new List<TerrainIsland>(islands) };
        }

        static TerrainIsland Rect(float x0, float z0, float x1, float z1, float topY)
        {
            return new TerrainIsland
            {
                topY = topY,
                outline = new List<float> { x0, z0, x1, z0, x1, z1, x0, z1 },
            };
        }

        [Test]
        public void RectIsland_FillsExactCells()
        {
            // 4×3 顶点米制矩形（0..8, 0..6）→ 2m 采样 = 4 列 × 3 行全实心。
            TerrainShape shape = Shape(Rect(0f, 0f, 8f, 6f, 4.5f));

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 6f);

            Assert.That(heights.Count, Is.EqualTo(12));
            Assert.That(heights, Is.All.EqualTo(4.5f));
        }

        [Test]
        public void RowMajor_IndexIsColPlusRowTimesCols()
        {
            // 只有最右列（x 6..8）实心：col=3 的三个格 → 索引 3 / 7 / 11。
            TerrainShape shape = Shape(Rect(6f, 0f, 8f, 6f, 2f));

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 6f);

            Assert.That(heights[3], Is.EqualTo(2f));
            Assert.That(heights[7], Is.EqualTo(2f));
            Assert.That(heights[11], Is.EqualTo(2f));
            Assert.That(heights[0], Is.EqualTo(0f));
        }

        [Test]
        public void Hole_RingIsland_IsEmptyInside()
        {
            // 外环 0..8 见方，洞 2..6 见方：中带实心、洞内为空。
            var island = Rect(0f, 0f, 8f, 8f, 3f);
            island.holes = new List<List<float>>
            {
                new List<float> { 2f, 2f, 6f, 2f, 6f, 6f, 2f, 6f },
            };
            TerrainShape shape = Shape(island);

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 8f);

            Assert.That(heights[0], Is.EqualTo(3f), "角格实心");
            Assert.That(heights[1 + 1 * 4], Is.EqualTo(0f), "洞内格为空");
            Assert.That(heights[0 + 1 * 4], Is.EqualTo(3f), "西中带实心");
        }

        [Test]
        public void Overlap_HighestTopWins()
        {
            TerrainShape shape = Shape(
                Rect(0f, 0f, 8f, 6f, 2.5f),
                Rect(2f, 0f, 6f, 6f, 4.5f));

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 6f);

            Assert.That(heights[0], Is.EqualTo(2.5f), "只被低岛覆盖");
            Assert.That(heights[1], Is.EqualTo(4.5f), "重叠格取高顶");
        }

        [Test]
        public void PointOnGround_MatchesPolygon()
        {
            TerrainShape shape = Shape(Rect(2f, 2f, 6f, 6f, 1f));

            Assert.That(TerrainShapeRasterizer.PointOnGround(shape, 4f, 4f), Is.True);
            Assert.That(TerrainShapeRasterizer.PointOnGround(shape, 1f, 4f), Is.False);
            Assert.That(TerrainShapeRasterizer.PointOnGround(shape, 4f, 4f - 10f), Is.False);
        }

        [Test]
        public void PointInPolygon_ConcaveOutline()
        {
            // L 形（凹多边形）：拐角内的缺口不在多边形内。
            var ring = new List<float>
            {
                0f, 0f, 8f, 0f, 8f, 4f, 4f, 4f, 4f, 8f, 0f, 8f,
            };

            Assert.That(TerrainShapeRasterizer.PointInPolygon(2f, 6f, ring), Is.True, "竖臂内");
            Assert.That(TerrainShapeRasterizer.PointInPolygon(6f, 6f, ring), Is.False, "缺口外");
            Assert.That(TerrainShapeRasterizer.PointInPolygon(6f, 2f, ring), Is.True, "横臂内");
        }

        [Test]
        public void Malformed_IsWellFormedFalse()
        {
            var bad = new TerrainShape
            {
                blockWorldHeight = 0.5f,
                islands = new List<TerrainIsland> { new TerrainIsland { topY = 0f } },
            };
            Assert.That(bad.IsWellFormed, Is.False);

            var good = Shape(Rect(0f, 0f, 4f, 4f, 1f));
            Assert.That(good.IsWellFormed, Is.True);
        }
    }
}
