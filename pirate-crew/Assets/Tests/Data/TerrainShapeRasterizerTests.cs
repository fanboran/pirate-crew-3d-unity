using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// <see cref="TerrainShapeRasterizer"/> 的解析解断言（无头可跑）：
    /// 矩形/带洞/重叠岛的格中心判定、行主序索引、even-odd 轮廓、逐格高度（v4 类体素）优先与回退。
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

        /// <summary>矩形岛 + 全覆盖逐格高度（0..cols·cell × 0..rows·cell，col0=row0=0）。</summary>
        static TerrainIsland RectWithHeights(float x0, float z0, float x1, float z1, float topY,
            int col0, int row0, int cols, int rows, params float[] values)
        {
            TerrainIsland island = Rect(x0, z0, x1, z1, topY);
            island.heightCol0 = col0;
            island.heightRow0 = row0;
            island.heightCols = cols;
            island.heightRows = rows;
            island.heights = new List<float>(values);
            return island;
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

        // ------------------------------------------------------------------
        // 逐格高度（v4 类体素）：优先、回退、数据洞、覆盖判定
        // ------------------------------------------------------------------

        [Test]
        public void PerCellHeights_OverrideTopY_PerCell()
        {
            // 0..8 × 0..6 岛（4 列 × 3 行），逐格高度各不相同：每格取数组值而非 topY。
            TerrainShape shape = Shape(RectWithHeights(
                0f, 0f, 8f, 6f, 14f, 0, 0, 4, 3,
                13.5f, 14f, 14.5f, 13.5f,
                14f, 14.5f, 13.5f, 14f,
                14.5f, 13.5f, 14f, 14.5f));

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 6f);

            Assert.That(heights.Count, Is.EqualTo(12));
            Assert.That(heights[0], Is.EqualTo(13.5f), "行主序 [col0,row0]");
            Assert.That(heights[1], Is.EqualTo(14f), "行主序 [col1,row0]");
            Assert.That(heights[2], Is.EqualTo(14.5f), "行主序 [col2,row0]");
            Assert.That(heights[4], Is.EqualTo(14f), "行主序 [col0,row1]");
            Assert.That(heights[7], Is.EqualTo(14f), "行主序 [col3,row1]");
            Assert.That(heights[11], Is.EqualTo(14.5f), "行主序 [col3,row2]");
            CollectionAssert.AreEqual(
                new List<float> { 13.5f, 14f, 14.5f, 13.5f, 14f, 14.5f, 13.5f, 14f, 14.5f, 13.5f, 14f, 14.5f },
                heights, "逐格高度必须逐格生效（不是整岛一个顶）");
        }

        [Test]
        public void PerCellHeights_ZeroValueInsideIsland_IsNoGround()
        {
            // 岛内一格高度 0 = 数据洞：该格无地面（数据权威，不回退 topY）。
            TerrainShape shape = Shape(RectWithHeights(
                0f, 0f, 8f, 6f, 14f, 0, 0, 4, 3,
                13.5f, 13.5f, 13.5f, 13.5f,
                13.5f, 0f, 13.5f, 13.5f,
                13.5f, 13.5f, 13.5f, 13.5f));

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 6f);

            Assert.That(heights[5], Is.EqualTo(0f), "岛内数据洞 = 无地面");
            Assert.That(heights[4], Is.EqualTo(13.5f), "洞的邻格不受影响");
        }

        [Test]
        public void PerCellHeights_GridGap_FallsBackToTopY()
        {
            // 网格只盖左半（col 0..1），岛轮廓更大：右半格在轮廓内但网格外 → 回退平顶 topY。
            TerrainShape shape = Shape(RectWithHeights(
                0f, 0f, 8f, 6f, 14f, 0, 0, 2, 3,
                13.5f, 14f, 13.5f, 14f, 13.5f, 14f));

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 6f);

            Assert.That(heights[0], Is.EqualTo(13.5f), "网格内取数组值");
            Assert.That(heights[2], Is.EqualTo(14f), "网格外的轮廓内格回退 topY（校验器会报「未盖住包围盒」）");
        }

        [Test]
        public void PerCellHeights_Overlap_HighestCellWins()
        {
            // 高岛带逐格高度、低岛平顶：重叠格取逐格值与平顶中的较高者。
            TerrainShape shape = Shape(
                RectWithHeights(2f, 0f, 6f, 6f, 12f, 1, 0, 2, 3,
                    13.5f, 14f, 13.5f, 14f, 13.5f, 14f),
                Rect(0f, 0f, 8f, 6f, 13.75f));

            List<float> heights = TerrainShapeRasterizer.ToHeights(shape, 8f, 6f);

            Assert.That(heights[0], Is.EqualTo(13.75f), "只被平顶岛覆盖：取 topY");
            Assert.That(heights[1], Is.EqualTo(13.75f), "重叠格：数组值 13.5 < 平顶 13.75，取高者");
            Assert.That(heights[2], Is.EqualTo(14f), "重叠格：数组值 14 > 平顶 13.75，取高者");
        }

        [Test]
        public void WithoutHeights_FlatTop_SemanticsUnchanged()
        {
            // 不带数组的岛保持平顶（schema ≤ 3 资产语义不变）——空数组当缺席。
            TerrainIsland island = Rect(0f, 0f, 8f, 6f, 14f);
            island.heights = new List<float>();
            Assert.That(island.HasHeights, Is.False, "空数组 = 无逐格高度");

            List<float> heights = TerrainShapeRasterizer.ToHeights(Shape(island), 8f, 6f);
            Assert.That(heights, Is.All.EqualTo(14f), "无逐格高度 = 整岛平顶");
        }

        [Test]
        public void HeightFieldsMalformed_IsWellFormedFalse()
        {
            var island = Rect(0f, 0f, 8f, 6f, 14f);
            island.heightCol0 = 0;
            island.heightRow0 = 0;
            island.heightCols = 4;
            island.heightRows = 3;
            island.heights = new List<float> { 1f, 2f, 3f };   // 长度 ≠ 4×3
            Assert.That(island.IsWellFormed, Is.False, "长度与包围盒不符 = 结构不自洽");

            island.heights.Add(4f);
            island.heights.Add(5f);   // 补到 5 仍 ≠ 12
            Assert.That(island.IsWellFormed, Is.False);
        }

        [Test]
        public void HasPerCellHeight_MatchesGridCoverage()
        {
            TerrainShape shape = Shape(RectWithHeights(
                0f, 0f, 8f, 6f, 14f, 1, 1, 2, 2,
                13.5f, 14f, 13.5f, 14f));

            // 格口径与 ToHeights 同一套：格 col 覆盖 [col·2, (col+1)·2)。
            Assert.That(TerrainShapeRasterizer.HasPerCellHeight(shape, 3f, 3f), Is.True,
                "点 (3,3) 落在格 (1,1)——网格起点，覆盖内");
            Assert.That(TerrainShapeRasterizer.HasPerCellHeight(shape, 5.9f, 5.9f), Is.True,
                "点 (5.9,5.9) 落在格 (2,2)——网格最后一格");
            Assert.That(TerrainShapeRasterizer.HasPerCellHeight(shape, 1.9f, 4f), Is.False,
                "点 (1.9,4) 落在格 (0,2)——col 在网格外");
            Assert.That(TerrainShapeRasterizer.HasPerCellHeight(shape, 6.5f, 4f), Is.False,
                "点 (6.5,4) 落在格 (3,2)——col 出网格");

            // 不带数组的岛：任何点都报 false（数据缺失 → 出生链走兜底）。
            TerrainShape flat = Shape(Rect(0f, 0f, 8f, 6f, 14f));
            Assert.That(TerrainShapeRasterizer.HasPerCellHeight(flat, 4f, 3f), Is.False);
        }
    }
}
