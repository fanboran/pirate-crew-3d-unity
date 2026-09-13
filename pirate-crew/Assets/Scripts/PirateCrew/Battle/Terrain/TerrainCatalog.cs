using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 已转写关卡的瓦片地形目录（纯 C#，无头可跑）。
    ///
    /// ==================================================================
    /// 【两种地形模式（2026-09-14 更新：全 33 关都走原版 tile 地图）】
    /// ==================================================================
    /// 一、<b>平台簇模式（全 33 关）</b>：由 <see cref="PlatformClusterLayout"/> 从原版
    ///   <c>&lt;row&gt;</c> 行串（<see cref="Data.LevelTileMaps"/>）逐格推出水陆与岛簇归属，
    ///   地面格有显式块高（岛高来自行号），格与格之间是水（掉落即死）。视觉上伪装成
    ///   大船 / 梯田岛（见 <c>SceneArt/SceneKitCatalog.cs</c> 与
    ///   <c>IslandShellGeometry.BuildPlatformUndersides</c>）。
    ///
    /// 二、<b>列式旧模式（仅作兜底）</b>：调用方给不出与关卡匹配的转写数据时，
    ///   <see cref="Build"/> 返回 <c>null</c>，运行时退回平坦竞技场
    ///   （<c>TileTerrainGrid.Flat</c>）。旧的 level_4 / level_27 手工列高表
    ///   **已删除** —— 那两关现在与原版行串一致，没必要再维护一张手抄列高。
    ///
    /// 【与 <see cref="Data.LevelCatalog"/> 的关系】关卡尺寸（widthTiles/heightTiles）仍以
    /// <see cref="Data.LevelCatalog"/> / <c>LevelDefinition</c> 为权威；本目录只补它没有的地形数据。
    ///
    /// 【数据来源】<c>external/swf-decompile/levels_all.json</c>（逆向文档 §7.2）→
    /// 转写进 <see cref="Data.LevelTileMaps"/>（external/ 不入库，故行串随包入库）。
    /// ==================================================================
    /// </summary>
    public static class TerrainCatalog
    {
        /// <summary>每列最大抬升块数（列式旧模式的竖直压缩上限，仅兜底路径使用）。</summary>
        public const int MaxBlocksPerColumn = 8;

        /// <summary>单块世界高度 = 8px = 0.5 单位（可玩性优先的竖直压缩；格 1→2 单位后随格放大）。</summary>
        public static float DefaultBlockWorldHeight => LevelGeometry.PixelsToUnits(8f);

        /// <summary>该关卡是否已转写地形数据（= <see cref="Data.LevelTileMaps"/> 收录了 1–33 全部关卡）。</summary>
        public static bool IsTranscribed(int levelNumber)
        {
            return LevelTileMaps.Has(levelNumber);
        }

        /// <summary>该关卡是否使用平台簇模式（逐格水陆）。全 33 关都是。</summary>
        public static bool IsPlatformLevel(int levelNumber) => LevelTileMaps.Has(levelNumber);

        /// <summary>取该关的平台簇地图；未转写返回 null。</summary>
        public static PlatformMap PlatformMapFor(int levelNumber)
        {
            if (!LevelCatalog.IsTranscribed(levelNumber))
                return null;

            return PlatformClusterLayout.BuildFor(LevelCatalog.Get(levelNumber));
        }

        /// <summary>
        /// 生成某关的瓦片地形网格（平台簇模式）。
        /// </summary>
        /// <param name="levelNumber">关卡序号。</param>
        /// <param name="widthTiles">关卡横向格数（权威来自 <see cref="Data.LevelCatalog"/>）。</param>
        /// <param name="depthTiles">关卡纵深格数（= 关卡 height，与 <see cref="LevelGeometry"/> 的出战场一致）。</param>
        /// <returns>已转写且尺寸吻合时返回网格；否则返回 <c>null</c>（运行时退回平坦竞技场）。</returns>
        public static TileTerrainGrid Build(int levelNumber, int widthTiles, int depthTiles)
        {
            if (widthTiles <= 0 || depthTiles <= 0)
                return null;

            PlatformMap map = PlatformMapFor(levelNumber);
            if (map == null)
                return null;

            // 尺寸对不上宁可退回平地，也不生成错位地形（关卡数据与行串版本不一致的信号）。
            if (map.WidthTiles != widthTiles || map.DepthTiles != depthTiles)
                return null;

            return new TileTerrainGrid(widthTiles, depthTiles, null, DefaultBlockWorldHeight, map);
        }

        /// <summary>
        /// 取某关的"每列最高块数"数组（兼容旧接口）：由平台地图逐列取最大总块高得到。
        /// 未转写返回 <c>null</c>。
        /// </summary>
        public static int[] ColumnBlocksFor(int levelNumber)
        {
            PlatformMap map = PlatformMapFor(levelNumber);
            if (map == null)
                return null;

            var columns = new int[map.WidthTiles];
            for (int gx = 0; gx < map.WidthTiles; gx++)
            {
                int max = 0;
                for (int gz = 0; gz < map.DepthTiles; gz++)
                    max = Mathf.Max(max, map.TotalBlocksAt(gx, gz));
                columns[gx] = max;
            }

            return columns;
        }

        // ------------------------------------------------------------------
        // 平台布局合法性查询（供测试 / 报告）
        // ------------------------------------------------------------------

        /// <summary>出生格是否在平台地面且不落水（块 ≥1、地表高于水面）。</summary>
        public static bool IsPlatformSpawnSafe(TileTerrainGrid grid, int gridX, int gridY)
        {
            if (grid == null || !grid.IsGroundAt(gridX, gridY))
                return false;

            float surface = grid.SurfaceWorldY(gridX, gridY);
            return surface > LevelGeometry.WaterSurfaceY;
        }

        /// <summary>两平台簇之间的水格数（沿分离轴）；见 <see cref="PlatformClusterLayout.WaterGapTiles"/>。</summary>
        public static int WaterGapTiles(in PlatformClusterInfo a, in PlatformClusterInfo b)
        {
            return PlatformClusterLayout.WaterGapTiles(a, b);
        }

        /// <summary>角色最大投掷射程（世界单位）；簇间水距换算成世界距离后须 ≤ 它才算"投掷可跨"。</summary>
        public static float MaxThrowRangeWorld => PlatformClusterLayout.MaxThrowRangeWorld;
    }
}
