using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 已转写关卡的瓦片地形目录（纯 C#，无头可跑）。
    ///
    /// ==================================================================
    /// 【两种地形模式（2026-09-13 平台化）】
    /// ==================================================================
    /// 一、<b>平台簇模式（level_1，新）</b>：由 <see cref="PlatformClusterLayout"/> 提供逐格水陆与簇归属，
    ///   地面格有显式块高，格与格之间是水（掉落即死）。视觉上伪装成大船 / 空岛 / 梯田小岛
    ///   （见 <c>SceneArt/SceneKitCatalog.cs</c> 与 <c>IslandShellGeometry.BuildPlatformUndersides</c>）。
    ///   即用户诉求的「一对高高低低的悬空平台浮在海面上」。
    ///
    /// 二、<b>列式旧模式（level_4 / level_27）</b>：沿用原版 XML 的列高转写
    ///   （每列一个块高、全图有基础地面、永不挖洞，见 <see cref="TileTerrainGrid"/> 类头）。
    ///   未重写的关卡仍退回平坦竞技场（<see cref="Build"/> 返回 <c>null</c>）。
    ///
    /// 【与 <see cref="Data.LevelCatalog"/> 的关系】关卡尺寸（widthTiles/heightTiles）仍以
    /// <see cref="Data.LevelCatalog"/> / <c>LevelDefinition</c> 为权威；本目录只补它没有的地形数据。
    ///
    /// 【数据来源】旧列模式来自 <c>external/swf-decompile/levels_all.json</c>（逆向文档 §7.2）；
    /// 平台簇模式是 AI 依据用户诉求给出的【提案/待定】布局（详见 <see cref="PlatformClusterLayout"/>）。
    /// ==================================================================
    /// </summary>
    public static class TerrainCatalog
    {
        /// <summary>每列最大抬升块数（列式旧模式的竖直压缩上限，提案/待定）。</summary>
        public const int MaxBlocksPerColumn = 8;

        /// <summary>单块世界高度 = 8px = 0.25 单位（可玩性优先的竖直压缩）。</summary>
        public static float DefaultBlockWorldHeight => LevelGeometry.PixelsToUnits(8f);

        // ------------------------------------------------------------------
        // level_4（56×18）：minAlt=3 / maxAlt=13；第 23–32 列原版为水道记 0。
        // ------------------------------------------------------------------
        static readonly int[] Level4ColumnBlocks =
        {
            1, 1, 0, 4, 4, 7, 8, 8, 7, 4, 4, 0, 0, 4, 4, 5, 5, 4, 4, 1,
            1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 4, 4, 5,
            5, 4, 4, 0, 0, 4, 4, 7, 8, 8, 7, 4, 4, 0, 1, 1,
        };

        // ------------------------------------------------------------------
        // level_27（21×20）：minAlt=11 / maxAlt=16；两侧高台（topRow 4）记 8，
        // 中间地面（topRow 9）记 0；第 4 / 16 列原版为缺口记 0。
        // ------------------------------------------------------------------
        static readonly int[] Level27ColumnBlocks =
        {
            8, 8, 8, 8, 0, 0, 0, 0, 8, 8, 8, 8, 8, 0, 0, 0, 0, 8, 8, 8, 8,
        };

        /// <summary>该关卡是否已转写地形数据。</summary>
        public static bool IsTranscribed(int levelNumber)
        {
            return levelNumber == 1 || levelNumber == 4 || levelNumber == 27;
        }

        /// <summary>该关卡是否使用平台簇模式（逐格水陆）。</summary>
        public static bool IsPlatformLevel(int levelNumber) => levelNumber == 1;

        /// <summary>取该关的平台簇地图；非平台关返回 null。</summary>
        public static PlatformMap PlatformMapFor(int levelNumber)
        {
            return levelNumber == 1 ? PlatformClusterLayout.BuildLevel1() : null;
        }

        /// <summary>
        /// 生成某关的瓦片地形网格。
        /// </summary>
        /// <param name="levelNumber">关卡序号。</param>
        /// <param name="widthTiles">关卡横向格数（权威来自 <see cref="Data.LevelCatalog"/>）。</param>
        /// <param name="depthTiles">关卡纵深格数。</param>
        /// <returns>已转写且尺寸吻合时返回网格；否则返回 <c>null</c>（运行时退回平坦竞技场）。</returns>
        public static TileTerrainGrid Build(int levelNumber, int widthTiles, int depthTiles)
        {
            if (widthTiles <= 0 || depthTiles <= 0)
                return null;

            if (levelNumber == 1)
            {
                if (widthTiles != 50 || depthTiles != 17)
                    return null;   // 平台布局按 50×17 定义，尺寸不符宁可退回平地

                PlatformMap map = PlatformClusterLayout.BuildLevel1();
                return new TileTerrainGrid(widthTiles, depthTiles, null, DefaultBlockWorldHeight, map);
            }

            int[] columns = ColumnBlocksFor(levelNumber);
            if (columns == null)
                return null;

            // 尺寸必须与转写表吻合，否则数据/关卡对不上——宁可退回平地也不生成错位地形。
            if (columns.Length != widthTiles)
                return null;

            var blocks = new int[widthTiles * depthTiles];
            for (int gy = 0; gy < depthTiles; gy++)
            {
                for (int gx = 0; gx < widthTiles; gx++)
                    blocks[gx + gy * widthTiles] = columns[gx];
            }

            return new TileTerrainGrid(widthTiles, depthTiles, blocks, DefaultBlockWorldHeight);
        }

        /// <summary>
        /// 取某关的"每列块数"数组（兼容旧接口）：平台关返回各列沿 Z 的最大块高，未转写返回 null。
        /// </summary>
        public static int[] ColumnBlocksFor(int levelNumber)
        {
            switch (levelNumber)
            {
                case 1:
                    {
                        PlatformMap map = PlatformClusterLayout.BuildLevel1();
                        var columns = new int[map.WidthTiles];
                        for (int gx = 0; gx < map.WidthTiles; gx++)
                        {
                            int max = 0;
                            for (int gz = 0; gz < map.DepthTiles; gz++)
                                max = Mathf.Max(max, map.BlocksAt(gx, gz));
                            columns[gx] = max;
                        }
                        return columns;
                    }
                case 4: return Level4ColumnBlocks;
                case 27: return Level27ColumnBlocks;
                default: return null;
            }
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
