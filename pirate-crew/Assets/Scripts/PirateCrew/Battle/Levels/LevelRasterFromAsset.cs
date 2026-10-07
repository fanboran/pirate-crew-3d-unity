using System.Collections.Generic;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 关卡载荷的岛形地形 → <see cref="HeightfieldGrid"/>（单一栅格语义的落地处）。
    ///
    /// 【唯一形态】资产里只有岛形轮廓（形状即数据）；采样高度数组由
    /// <see cref="TerrainShapeRasterizer"/> 现算（采样粒度 = <see cref="LevelGeometry.TileWorldSize"/> 米），
    /// 再按 <see cref="LevelGeometry.BlockWorldHeight"/> 米/块折回整数块表示。
    /// 海图那条线不走这里（它的栅格由站面 box 派生，见 <c>WorldMapRuntime.BuildTerrainGrid</c>），
    /// 但两者产出的是同一个 <see cref="HeightfieldGrid"/> 列式形态，AI/站位/小地图共用一套查询。
    ///
    /// 【坏数据怎么办】地形不自洽时返回 <see cref="HeightfieldGrid.Flat"/> 并把问题交给
    /// `LevelAssetValidator` 报红——运行时静默兜底、门禁吵闹，是这里的取舍
    /// （关卡资产是构建期内容，坏数据不该在运行时才炸）。
    /// </summary>
    public static class LevelRasterFromAsset
    {
        /// <summary>岛形地形 → 地形网格；不自洽时返回全平网格。</summary>
        public static HeightfieldGrid Build(LevelAssetPayload payload)
        {
            if (payload == null || !payload.terrain.IsWellFormed)
                return HeightfieldGrid.Flat(
                    payload != null ? LevelGeometry.CellCount(payload.sizeX) : 1,
                    payload != null ? LevelGeometry.CellCount(payload.sizeZ) : 1);

            float blockHeight = payload.terrain.blockWorldHeight > 0f
                ? payload.terrain.blockWorldHeight
                : LevelGeometry.BlockWorldHeight;

            List<float> heights = TerrainShapeRasterizer.ToHeights(payload.terrain, payload.sizeX, payload.sizeZ);
            var blocks = new int[heights.Count];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = BlockCount(heights[i], blockHeight);

            return new HeightfieldGrid(
                LevelGeometry.CellCount(payload.sizeX), LevelGeometry.CellCount(payload.sizeZ),
                blocks, blockHeight);
        }

        /// <summary>实心采样格数（校验器用）。</summary>
        public static int SolidCellCount(LevelAssetPayload payload)
        {
            if (payload == null || !payload.terrain.IsWellFormed)
                return 0;

            List<float> heights = TerrainShapeRasterizer.ToHeights(payload.terrain, payload.sizeX, payload.sizeZ);
            int n = 0;
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
