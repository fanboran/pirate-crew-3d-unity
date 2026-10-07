using System;
using System.Collections.Generic;

namespace PirateCrew.Data
{
    /// <summary>
    /// 岛形地形 → 采样高度数组的**唯一栅格化器**（纯 C#，无头验证台可断言）。
    ///
    /// 【它在哪里】资产里只存岛形（<see cref="TerrainShape"/>）；采样数组是运行时
    /// <c>HeightfieldGrid</c> 的内部表示，由本类从岛形现算——「矩形采样网格」从此只存在于
    /// 运行时内部，不进资产、不进文档概念。
    ///
    /// 【判定语义】逐采样格取**格中心**（<c>((col+0.5)·cell, (row+0.5)·cell)</c>）做
    /// even-odd 点包含判定；多岛重叠取顶面最高者（迁移产物互不重叠，重叠语义供手编数据用）。
    ///
    /// 【行主序约定】heights[col + row · cols]，row = Z 方向（gridY → +Z），与
    /// <c>HeightfieldGrid</c> 的索引口径一致。
    /// </summary>
    public static class TerrainShapeRasterizer
    {
        /// <summary>
        /// 岛形 → 行主序高度数组（米；0 = 该处无地面）。尺寸由场地尺幅与
        /// <see cref="LevelAssetSchema.RasterCellSize"/> 推出。
        /// </summary>
        public static List<float> ToHeights(TerrainShape shape, float sizeX, float sizeZ)
        {
            int cols = LevelAssetSchema.RasterCells(sizeX);
            int rows = LevelAssetSchema.RasterCells(sizeZ);
            float cell = LevelAssetSchema.RasterCellSize;

            var heights = new List<float>(cols * rows);
            for (int row = 0; row < rows; row++)
            {
                float cz = (row + 0.5f) * cell;
                for (int col = 0; col < cols; col++)
                {
                    float cx = (col + 0.5f) * cell;

                    float best = 0f;
                    if (shape.islands != null)
                    {
                        for (int i = 0; i < shape.islands.Count; i++)
                        {
                            TerrainIsland island = shape.islands[i];
                            if (island.topY <= best)
                                continue;
                            if (PointInIsland(cx, cz, island))
                                best = island.topY;
                        }
                    }
                    heights.Add(best);
                }
            }
            return heights;
        }

        /// <summary>世界坐标点是否踩在实心地面上（顶面最高覆盖岛的顶面高度 &gt; 0）。</summary>
        public static bool PointOnGround(TerrainShape shape, float x, float z)
        {
            if (shape.islands == null)
                return false;

            for (int i = 0; i < shape.islands.Count; i++)
            {
                if (shape.islands[i].topY > 0f && PointInIsland(x, z, shape.islands[i]))
                    return true;
            }
            return false;
        }

        /// <summary>点是否在岛内（外轮廓内且不在任何内洞内）。</summary>
        public static bool PointInIsland(float x, float z, TerrainIsland island)
        {
            if (island.outline == null || !PointInPolygon(x, z, island.outline))
                return false;

            if (island.holes != null)
            {
                for (int i = 0; i < island.holes.Count; i++)
                {
                    if (island.holes[i] != null && PointInPolygon(x, z, island.holes[i]))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// even-odd 射线法点包含判定（环 = 扁平 <c>[x0, z0, x1, z1, ...]</c>，方向不限）。
        /// </summary>
        public static bool PointInPolygon(float x, float z, List<float> ring)
        {
            if (ring == null || ring.Count < 6)
                return false;

            bool inside = false;
            int vertexCount = ring.Count / 2;
            for (int i = 0, j = vertexCount - 1; i < vertexCount; j = i++)
            {
                float xi = ring[i * 2], zi = ring[i * 2 + 1];
                float xj = ring[j * 2], zj = ring[j * 2 + 1];
                bool crosses = (zi > z) != (zj > z)
                    && x < (xj - xi) * (z - zi) / (zj - zi) + xi;
                if (crosses)
                    inside = !inside;
            }
            return inside;
        }
    }
}
