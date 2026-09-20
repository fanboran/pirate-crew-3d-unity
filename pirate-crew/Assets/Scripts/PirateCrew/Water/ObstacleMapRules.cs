using PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.Water
{
    /// <summary>
    /// 障碍图烘焙规则（纯 C#，无头可测）。
    ///
    /// 【判据】模拟域内某世界点是不是"水面反射墙"：
    ///   · 竞技场范围内：地形地表（<see cref="TileTerrainGrid.SurfaceWorldYAtWorld"/>）**高于水面** → 障碍
    ///     （沙岛本体、被抬升的台子、礁石）；地形低于水面 → 开阔水；
    ///   · 竞技场范围外：没有地形（只有水面之下的海床台阶）→ 开阔水。
    ///   本工程地面顶面 y=0、水面 y=-0.4（格 ×2 后；旧口径 -0.2）→ 整个沙岛都是障碍，海浪在岛缘反射——这正是要的"浪拍岸"。
    ///
    /// 【为什么单独成类】<c>Assets/Editor/WaterAssetBuilder.cs</c> 只负责把这里的输出编码成 PNG；
    /// 判据留在纯 C# 里，就能在无头验证台用 <see cref="TileTerrainGrid"/>（也是纯 C#）抽格断言
    /// "烘焙障碍图与地形高度一致"。
    /// </summary>
    public static class ObstacleMapRules
    {
        /// <summary>地表高于水面这么多（世界单位）才算障碍（容差，避免贴水面误判）。</summary>
        public const float SurfaceEpsilon = 0.01f;

        /// <summary>单个世界点是否为障碍（反射墙）。</summary>
        public static bool IsObstacleAt(TileTerrainGrid grid, float worldX, float worldZ,
            float waterWorldY, float arenaWidth, float arenaDepth)
        {
            bool insideArena = worldX >= 0f && worldX <= arenaWidth
                            && worldZ >= 0f && worldZ <= arenaDepth;
            if (!insideArena)
                return false; // 岛外是开阔水（海床在水面之下，不构成表面障碍）

            if (grid == null)
                return true; // 兜底：没有地形数据时按沙岛平地处理

            return grid.SurfaceWorldYAtWorld(worldX, worldZ) >= waterWorldY - SurfaceEpsilon;
        }

        /// <summary>
        /// 烘焙整张障碍掩码（行主序，长度 = cells×cells）。域是以 <paramref name="domainCenterXZ"/> 为中心、
        /// 边长 <paramref name="domainSize"/> 的正方形，与 <see cref="WaterSimulationDriver"/> 的域参数一致。
        /// </summary>
        public static bool[] Bake(TileTerrainGrid grid, Vector2 domainCenterXZ, float domainSize, int cells,
            float waterWorldY, float arenaWidth, float arenaDepth)
        {
            cells = Mathf.Max(1, cells);
            var mask = new bool[cells * cells];

            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    float u = (cx + 0.5f) / cells;
                    float v = (cz + 0.5f) / cells;
                    Vector2 world = WaterSimRules.DomainUvToWorld(new Vector2(u, v), domainCenterXZ, domainSize);
                    mask[cz * cells + cx] = IsObstacleAt(grid, world.x, world.y, waterWorldY, arenaWidth, arenaDepth);
                }
            }

            return mask;
        }

        /// <summary>掩码里障碍格数量（日志/报告用）。</summary>
        public static int CountObstacles(bool[] mask)
        {
            if (mask == null)
                return 0;
            int n = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i])
                    n++;
            }
            return n;
        }
    }
}
