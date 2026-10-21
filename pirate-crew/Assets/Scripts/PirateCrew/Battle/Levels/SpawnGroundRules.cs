using PirateCrew.Data;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 出生站面世界 Y 的**优先级仲裁**（纯 C#，无头可测）。「出生站位单源化」的运行时口径：
    ///
    ///   ① **数据优先**：关卡资产的岛带逐格高度（v4 类体素，穹顶已烘焙进数据）时，
    ///      光栅化后的 <see cref="HeightfieldGrid"/> 就是站面——单源在 LevelData，
    ///      blockWorldHeight（0.5 m）量化出 2-3 档台阶属预期语义（判定场从平顶变台阶，
    ///      创始人裁决口径：只动碰撞/逻辑高度场，视觉零改动）；
    ///   ② **穹顶兜底**：数据缺失（岛没带高度数组）而空岛根激活时，逐点贴
    ///      <see cref="FloatingIslandDome"/>（短期方案，保留为「场景有穹顶、资产没数据」窗口期的回退）；
    ///   ③ **平顶兜底**：其余关卡资产按逻辑高度场平顶；
    ///   ④ **基础地面**：无地形数据（海图路径在 BattleController 侧另走，正常不会到这里）。
    ///
    /// ①与②的等价性（数据高度 vs 穹顶公式逐点差 ≤ 采样格内的穹顶落差 + 半块量化）
    /// 由无头测试钉住；两条路径同时活着时**数据赢**——穹顶公式只是数据缺席时的影子。
    /// </summary>
    public static class SpawnGroundRules
    {
        /// <summary>
        /// 出生站面世界 Y（不含 <see cref="LevelGeometry.UnitPivotHeight"/>，由调用方叠加）。
        /// </summary>
        /// <param name="payload">关卡资产载荷（海图路径传 null：海图站位不走这套优先级）。</param>
        /// <param name="terrain">光栅化后的逻辑高度场（可为 null）。</param>
        /// <param name="worldX">出生点世界 X。</param>
        /// <param name="worldZ">出生点世界 Z。</param>
        /// <param name="cellX">出生点采样格 X（HeightfieldGrid 索引）。</param>
        /// <param name="cellY">出生点采样格 Z。</param>
        /// <param name="skyIslandActive">空岛根是否激活（本局站穹顶的运行时判据）。</param>
        /// <param name="islandRootWorld">空岛根世界位；<paramref name="skyIslandActive"/> 为 false 时忽略。</param>
        /// <param name="spec">空岛构图；null = <see cref="FloatingIslandSpec.Default"/>。</param>
        public static float GroundWorldY(
            LevelAssetPayload payload, HeightfieldGrid terrain,
            float worldX, float worldZ, int cellX, int cellY,
            bool skyIslandActive, Vector3 islandRootWorld, FloatingIslandSpec spec = null)
        {
            // ① 数据优先：岛带逐格高度且盖住这个点所在格 → 光栅化网格说了算。
            if (payload != null && payload.terrain.IsWellFormed
                && TerrainShapeRasterizer.HasPerCellHeight(payload.terrain, worldX, worldZ))
            {
                float dataY = terrain != null ? terrain.SurfaceWorldY(cellX, cellY) : 0f;
                if (dataY > LevelGeometry.GroundTopY)
                    return dataY;
                // 数据洞（该格块数为 0）：落到 ②/③ 兜底，别把单位钉在基础地面上。
            }

            // ② 穹顶兜底：数据缺失而空岛根激活 → 逐点贴穹顶。
            if (skyIslandActive)
                return FloatingIslandDome.SpawnGroundY(spec, islandRootWorld, worldX, worldZ);

            // ③ 平顶兜底：逻辑高度场（无穹顶的关卡资产路径）。
            if (terrain != null)
                return terrain.SurfaceWorldY(cellX, cellY);

            // ④ 基础地面。
            return LevelGeometry.GroundTopY;
        }
    }
}
