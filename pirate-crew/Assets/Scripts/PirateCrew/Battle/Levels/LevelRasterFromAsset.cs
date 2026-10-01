using System.Collections.Generic;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 关卡载荷的逻辑高度场 → <see cref="HeightfieldGrid"/>（单一栅格语义的落地处）。
    ///
    /// 【唯一形态】载荷只存一种栅格：行主序**米高度** + 单块世界高度。海图那条线不走这里
    /// （它的栅格由站面 box 派生，见 <c>WorldMapRuntime.BuildTerrainGrid</c>），
    /// 但两者产出的是同一个 <see cref="HeightfieldGrid"/> 列式形态，AI/站位/小地图共用一套查询。
    ///
    /// 【全米 → 采样格】资产里的栅格尺幅与高度都是米；这里按运行时采样粒度
    /// （<see cref="LevelGeometry.TileWorldSize"/> 米/格、<see cref="LevelGeometry.BlockWorldHeight"/>
    /// 米/块）折回 <see cref="HeightfieldGrid"/> 的整数表示。两个换算都是"米 ÷ 常数"，
    /// 常数是 2 的幂，故往返逐值精确。
    ///
    /// 【坏数据怎么办】栅格尺寸与高度数不自洽时返回 <see cref="HeightfieldGrid.Flat"/>
    /// 并把问题交给 `LevelAssetValidator` 报红——运行时静默兜底、门禁吵闹，是这里的取舍
    /// （关卡资产是构建期内容，坏数据不该在运行时才炸）。
    /// </summary>
    public static class LevelRasterFromAsset
    {
        /// <summary>载荷栅格 → 地形网格；栅格不自洽时返回全平网格。</summary>
        public static HeightfieldGrid Build(LevelAssetPayload payload)
        {
            if (payload == null || !payload.terrain.IsWellFormed)
                return HeightfieldGrid.Flat(
                    payload != null ? LevelGeometry.CellCount(payload.sizeX) : 1,
                    payload != null ? LevelGeometry.CellCount(payload.sizeZ) : 1);

            TerrainRaster raster = payload.terrain;
            float blockHeight = raster.blockWorldHeight > 0f
                ? raster.blockWorldHeight
                : LevelGeometry.BlockWorldHeight;

            var blocks = new int[raster.heights.Count];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = BlockCount(raster.heights[i], blockHeight);

            return new HeightfieldGrid(
                LevelGeometry.CellCount(raster.sizeX), LevelGeometry.CellCount(raster.sizeZ),
                blocks, blockHeight);
        }

        /// <summary>栅格里的实心格数（校验器用）。</summary>
        public static int SolidCellCount(LevelAssetPayload payload)
        {
            if (payload == null || payload.terrain.heights == null)
                return 0;
            int n = 0;
            List<float> heights = payload.terrain.heights;
            for (int i = 0; i < heights.Count; i++)
            {
                if (heights[i] > 0f)
                    n++;
            }
            return n;
        }

        /// <summary>地面高度（米）→ 运行时整数块数（0 = 无地面）。</summary>
        static int BlockCount(float heightMeters, float blockWorldHeight)
        {
            if (heightMeters <= 0f || blockWorldHeight <= 0f)
                return 0;
            return Mathf.RoundToInt(heightMeters / blockWorldHeight);
        }
    }
}
