using System.Collections.Generic;
using PirateCrew.Data;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 关卡载荷的逻辑高度场 → <see cref="TileTerrainGrid"/>（单一栅格语义的落地处）。
    ///
    /// 【唯一形态】载荷只存一种栅格：行主序块数 + 单块世界高度。海图那条线不走这里
    /// （它的栅格由站面 box 派生，见 <c>WorldMapRuntime.BuildTerrainGrid</c>），
    /// 但两者产出的是同一个 <see cref="TileTerrainGrid"/> 列式形态，AI/站位/小地图共用一套查询。
    ///
    /// 【坏数据怎么办】栅格尺寸与格子数不自洽时返回 <see cref="TileTerrainGrid.Flat"/>
    /// 并把问题交给 `LevelAssetValidator` 报红——运行时静默兜底、门禁吵闹，是这里的取舍
    /// （关卡资产是构建期内容，坏数据不该在运行时才炸）。
    /// </summary>
    public static class LevelRasterFromAsset
    {
        /// <summary>载荷栅格 → 地形网格；栅格不自洽时返回全平网格。</summary>
        public static TileTerrainGrid Build(LevelAssetPayload payload)
        {
            if (payload == null || !payload.terrain.IsWellFormed)
                return TileTerrainGrid.Flat(payload != null ? payload.widthTiles : 1,
                                            payload != null ? payload.depthTiles : 1);

            var blocks = new int[payload.terrain.blocks.Count];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = payload.terrain.blocks[i];

            return new TileTerrainGrid(
                payload.terrain.widthTiles, payload.terrain.depthTiles,
                blocks, payload.terrain.blockWorldHeight);
        }

        /// <summary>栅格是否可用（校验器与测试共用同一判据，避免两处各写一套）。</summary>
        public static bool IsUsable(LevelAssetPayload payload)
        {
            return payload != null
                && payload.terrain.IsWellFormed
                && payload.terrain.widthTiles == payload.widthTiles
                && payload.terrain.depthTiles == payload.depthTiles
                && payload.terrain.blockWorldHeight > 0f;
        }

        /// <summary>栅格里的实心格数（校验器用）。</summary>
        public static int SolidCellCount(LevelAssetPayload payload)
        {
            if (payload == null || payload.terrain.blocks == null)
                return 0;
            int n = 0;
            List<int> blocks = payload.terrain.blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] > 0)
                    n++;
            }
            return n;
        }
    }
}
