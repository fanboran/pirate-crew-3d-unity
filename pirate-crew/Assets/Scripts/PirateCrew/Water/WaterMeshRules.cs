using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>
    /// 水面网格细分规则（纯 C#，无头可测）。
    ///
    /// 【为什么需要】工程里的水是 <c>PrimitiveType.Cube</c>（24 顶点），顶面只有 4 个角点，
    /// 顶点位移最多把整块面倾斜一下 —— Gerstner 的波峰几何**根本画不出来**。
    /// 所以另配 <see cref="WaterTessellator"/> 用本规则生成一张 XZ 细分布网格替换掉 Cube 网格。
    /// </summary>
    public static class WaterMeshRules
    {
        /// <summary>每轴最少格数。</summary>
        public const int MinCellsPerAxis = 1;

        /// <summary>默认单格世界尺寸：1.6 单位（格 1→2 单位 ×2；最短波长 4.8 → 每波长约 3 格，几何够顺）。</summary>
        public const float DefaultCellSize = 1.6f;

        /// <summary>默认每轴格数上限（水域 300×234 @1.6 → 188×147，约 28k 顶点；上限保持 192 足够覆盖）。</summary>
        public const int DefaultMaxCellsPerAxis = 192;

        /// <summary>按世界尺寸与目标格距求格数（向上取整，clamp 到 [1, maxCellsPerAxis]）。</summary>
        public static int CellCount(float worldSize, float targetCellSize, int maxCellsPerAxis)
        {
            float size = Mathf.Abs(worldSize);
            float target = Mathf.Max(targetCellSize, 1e-3f);
            int maxCells = Mathf.Max(MinCellsPerAxis, maxCellsPerAxis);
            int cells = Mathf.CeilToInt(size / target);
            return Mathf.Clamp(cells, MinCellsPerAxis, maxCells);
        }

        /// <summary>网格顶点数 <c>(cellsX+1)·(cellsZ+1)</c>。</summary>
        public static int VertexCount(int cellsX, int cellsZ)
        {
            int cx = Mathf.Max(cellsX, MinCellsPerAxis);
            int cz = Mathf.Max(cellsZ, MinCellsPerAxis);
            return (cx + 1) * (cz + 1);
        }

        /// <summary>三角面数 <c>cellsX·cellsZ·2</c>。</summary>
        public static int TriangleCount(int cellsX, int cellsZ)
        {
            int cx = Mathf.Max(cellsX, MinCellsPerAxis);
            int cz = Mathf.Max(cellsZ, MinCellsPerAxis);
            return cx * cz * 2;
        }

        /// <summary>索引数（= 三角面数 × 3）。</summary>
        public static int IndexCount(int cellsX, int cellsZ)
        {
            return TriangleCount(cellsX, cellsZ) * 3;
        }

        /// <summary>实际格距（世界单位）。</summary>
        public static float CellSize(float worldSize, int cells)
        {
            return Mathf.Abs(worldSize) / Mathf.Max(cells, MinCellsPerAxis);
        }

        /// <summary>
        /// 采样是否够细：格距 ≤ 最短波长 / 3（每个波长至少 3 个顶点，避免几何锯齿）。
        /// </summary>
        public static bool IsSamplingAdequate(float worldSizeX, float worldSizeZ, int cellsX, int cellsZ, float minWavelength)
        {
            float target = Mathf.Max(minWavelength, MinWavelength) / 3f;
            float cellX = CellSize(worldSizeX, cellsX);
            float cellZ = CellSize(worldSizeZ, cellsZ);
            return cellX <= target + 1e-4f && cellZ <= target + 1e-4f;
        }

        /// <summary>网格内部常量，仅供 <see cref="IsSamplingAdequate"/> 使用。</summary>
        const float MinWavelength = WaterRules.MinWavelength;
    }
}
