using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 已转写关卡的瓦片地形目录（纯 C#，无头可跑）。
    ///
    /// ==================================================================
    /// 【数据来源与推导】
    /// ==================================================================
    /// 每关存一列「每列的抬升块数」<c>columnBlocks[gridX]</c>，由原版关卡 XML 的
    /// <c>&lt;row&gt;</c> 瓦片行算出（数据源：<c>external/swf-decompile/levels_all.json</c>，
    /// 即逆向文档 §7.2 指明的数据源）：
    ///   1. 逐格判实心：瓦片名 <c>-</c> = 空，<c>tile_ripple_*</c> / <c>boat_ripple_*</c> = 水（非实心），
    ///      其余（grass/earth/sand/ship/mast/…）= 实心（§5.4 的瓦片 AABB 碰撞）。
    ///   2. 每列取最上方实心瓦片的行号 <c>topSolidRow</c>（= 该列地表）。
    ///   3. <c>altitude = heightTiles − topSolidRow</c>；<c>relative = altitude − minAltitude</c>。
    ///   4. <c>blocks = round(relative / (maxAltitude − minAltitude) × MaxBlocksPerColumn)</c>；
    ///      无实心列（原版水道）记 0（与最低地表同 → 平坦地面，见 <see cref="TileTerrainGrid"/> 类头取舍）。
    ///
    /// ⚠ <b>高度语义是提案/待定</b>：原版 2D 的 <c>gridY</c> 在本工程已重投影为纵深 Z，
    ///   把行序再当高度属于**本工程的 3D 化推导**（唯一能同时满足「XZ 重投影」与
    ///   「瓦片构成墙体/高台」的做法）。完整推导与归一化/压缩理由见 <see cref="TileTerrainGrid"/> 类头。
    ///
    /// 【转写范围】只转写了 <see cref="Data.LevelCatalog"/> 已转写的 3 关（1 / 4 / 27）——
    /// 其余 30 关的瓦片行未转写，<see cref="Build"/> 返回 <c>null</c>（运行时退回平坦竞技场，
    /// 保持既有行为）。这也与「关卡注入只在 LevelCatalog.IsTranscribed 的关卡上生效」一致。
    ///
    /// 【与 <see cref="Data.LevelCatalog"/> 的关系】关卡尺寸（widthTiles/heightTiles）仍以
    /// <see cref="Data.LevelCatalog"/> / <c>LevelDefinition</c> 为权威；本目录只补它没有的瓦片列数据。
    /// </summary>
    public static class TerrainCatalog
    {
        /// <summary>每列最大抬升块数（竖直压缩上限，提案/待定；见 <see cref="TileTerrainGrid"/> 类头）。</summary>
        public const int MaxBlocksPerColumn = 8;

        /// <summary>单块世界高度 = 8px = 0.25 单位（提案/待定：可玩性优先的竖直压缩）。</summary>
        public static float DefaultBlockWorldHeight => LevelGeometry.PixelsToUnits(8f);

        // ------------------------------------------------------------------
        // level_1（50×17）：minAlt=5 / maxAlt=13；地面带（topRow 12）记 0，
        // 桅杆/岛台（topRow 4）记 8；第 19 / 40 列原版为水道（无实心）记 0。
        // ------------------------------------------------------------------
        static readonly int[] Level1ColumnBlocks =
        {
            1, 1, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 1, 1, 1, 1, 1, 0,
            0, 0, 0, 8, 8, 8, 8, 8, 8, 4, 4, 4, 8, 8, 8, 8, 8, 8, 0, 0,
            0, 1, 1, 1, 5, 5, 7, 7, 5, 5,
        };

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

        /// <summary>该关卡是否已转写地形列数据。</summary>
        public static bool IsTranscribed(int levelNumber)
        {
            return levelNumber == 1 || levelNumber == 4 || levelNumber == 27;
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
            int[] columns = ColumnBlocksFor(levelNumber);
            if (columns == null || widthTiles <= 0 || depthTiles <= 0)
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

        /// <summary>取某关的每列块数数组；未转写返回 null。</summary>
        public static int[] ColumnBlocksFor(int levelNumber)
        {
            switch (levelNumber)
            {
                case 1: return Level1ColumnBlocks;
                case 4: return Level4ColumnBlocks;
                case 27: return Level27ColumnBlocks;
                default: return null;
            }
        }
    }
}
