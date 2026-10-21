using System.Collections.Generic;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.SceneArt
{
    /// <summary>
    /// 空岛草皮穹顶 → 关卡资产**逐格高度**（类体素）的烘焙采样器（纯 C#，无头可测）。
    ///
    /// 【它解决什么】第 2 关的可见可站面是 <see cref="FloatingIslandDome"/> 的曲面穹顶，
    /// 而逻辑高度场（<see cref="TerrainShape"/>）此前只有平顶 topY——判定与可见面两套口径。
    /// 本采样器在**烘焙期**把穹顶按 2 m 采样格固化进资产（编辑器入口
    /// <c>LevelDataMigrator.BakeVoxelHeights</c>），运行时栅格化后与穹顶同源，
    /// 出生站位从此单源（数据优先、穹顶公式仅作数据缺失时的兜底）。
    ///
    /// 【格子口径（与 <see cref="TerrainShapeRasterizer"/> 严格同一套）】
    /// 全场栅格：格 <c>col</c> 覆盖世界 X <c>[col·cell, (col+1)·cell)</c>（cell =
    /// <see cref="LevelAssetSchema.RasterCellSize"/> = 2 m，row 同理沿 Z）；
    /// 格值在**格中心** <c>((col+0.5)·cell, (row+0.5)·cell)</c> 采样（与 ToHeights 的
    /// even-odd 点包含判定同一点）；数组行主序 <c>heights[col - col0 + (row - row0)·cols]</c>。
    /// 两侧若各自漂移，等价性测试（数据高度 vs 穹顶公式逐点差 ≤ 格内穹顶落差）会红。
    ///
    /// 【值口径】格值 = 岛根世界 Y + <see cref="FloatingIslandDome.PlateauY"/>(局部)（= 出生链
    /// <see cref="FloatingIslandDome.SpawnGroundY"/> 同式）；轮廓外的包围盒角落写 0（那里不是岛）。
    /// 折回块表示的量化（÷ blockWorldHeight 四舍五入）发生在运行时消费侧，不在采样侧。
    /// </summary>
    public static class FloatingIslandHeightsBake
    {
        /// <summary>
        /// 给一座岛采样逐格高度（幂等：同岛同根必得同一数组）。
        /// 网格只覆盖岛轮廓包围盒（外扩到整格），并夹在全场栅格内。
        /// </summary>
        /// <param name="island">岛（轮廓/内洞原样保留，只补高度字段）。</param>
        /// <param name="spec">空岛构图（穹顶参数唯一真源）。</param>
        /// <param name="islandRootWorld">岛根世界位（装配契约：只平移、不旋转不缩放）。</param>
        /// <param name="sizeX">场地 X 尺幅（米；夹取网格边界用）。</param>
        /// <param name="sizeZ">场地 Z 尺幅。</param>
        public static TerrainIsland Bake(TerrainIsland island, FloatingIslandSpec spec,
            Vector3 islandRootWorld, float sizeX, float sizeZ)
        {
            float cell = LevelAssetSchema.RasterCellSize;
            int arenaCols = LevelAssetSchema.RasterCells(sizeX);
            int arenaRows = LevelAssetSchema.RasterCells(sizeZ);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int k = 0; k + 1 < island.outline.Count; k += 2)
            {
                minX = Mathf.Min(minX, island.outline[k]);
                maxX = Mathf.Max(maxX, island.outline[k]);
                minZ = Mathf.Min(minZ, island.outline[k + 1]);
                maxZ = Mathf.Max(maxZ, island.outline[k + 1]);
            }

            int col0 = ClampIndex(Mathf.FloorToInt(minX / cell), arenaCols);
            int col1 = ClampIndex(Mathf.CeilToInt(maxX / cell), arenaCols);
            int row0 = ClampIndex(Mathf.FloorToInt(minZ / cell), arenaRows);
            int row1 = ClampIndex(Mathf.CeilToInt(maxZ / cell), arenaRows);
            int cols = Mathf.Max(0, col1 - col0);
            int rows = Mathf.Max(0, row1 - row0);

            var heights = new List<float>(cols * rows);
            for (int row = 0; row < rows; row++)
            {
                float cz = (row0 + row + 0.5f) * cell;
                for (int col = 0; col < cols; col++)
                {
                    float cx = (col0 + col + 0.5f) * cell;
                    float value = TerrainShapeRasterizer.PointInIsland(cx, cz, island)
                        ? islandRootWorld.y
                          + FloatingIslandDome.PlateauY(spec, cx - islandRootWorld.x, cz - islandRootWorld.z)
                        : 0f;
                    heights.Add(value);
                }
            }

            island.heightCol0 = col0;
            island.heightRow0 = row0;
            island.heightCols = cols;
            island.heightRows = rows;
            island.heights = heights;
            return island;
        }

        /// <summary>采样格索引夹在 [0, arenaCells] 内（轮廓顶点本该在场内，这里是防手编数据越界）。</summary>
        static int ClampIndex(int value, int arenaCells)
        {
            return Mathf.Clamp(value, 0, arenaCells);
        }
    }
}
