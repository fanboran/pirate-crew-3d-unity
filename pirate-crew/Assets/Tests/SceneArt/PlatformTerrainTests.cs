using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 平台化地形语义的纯 C# 用例（无头可跑）：水陆逐格、掉落即死、单位落位与地形高度一致。
    ///
    /// 【2026-09-14 起以原版 tile 地图为准】level_1 的旧手写四簇布局（4 簇 / 411 地面格）已删除，
    /// 现在 33 关全部由 <c>Data/LevelTileMaps</c> 的原版 <c>&lt;row&gt;</c> 行串推出：
    /// level_1 = 9 座岛 / 163 格地面（50×17 里大片是海面）。
    /// 本文件的逐值断言（格数 / 块高 / 水格）已按原版读数重写。
    ///
    /// 【覆盖】
    ///   · 全 33 关都能生成平台簇地形，且**地面格数 = 原版非空非波纹格数**；
    ///   · 出生位全部站在原版地面上、地表高于水面（不初始落水）；
    ///   · 水格没有地面（块高 0）且游戏性查询返回虚空哨兵（AI 判定落水）；
    ///   · 爆炸把平台格炸空后变成水；
    ///   · 尺寸不符 / 未转写时不生成错位地形。
    /// </summary>
    [TestFixture]
    public class PlatformTerrainTests
    {
        const int Width = 50, Depth = 17;

        static TileTerrainGrid Level1()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, Width, Depth);
            Assert.IsNotNull(grid, "level_1 应为平台簇地形");
            return grid;
        }

        // ------------------------------------------------------------------
        // 全 33 关：平台模式 + 地面格数 = 原版读数
        // ------------------------------------------------------------------

        [Test]
        public void AllLevels_BuildPlatformGridMatchingOriginalTileMap()
        {
            for (int n = 1; n <= LevelCatalog.TotalLevels; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                TileTerrainGrid grid = TerrainCatalog.Build(n, level.WidthTiles, level.HeightTiles);

                Assert.IsNotNull(grid, "level_" + n + " 应能生成地形网格");
                Assert.IsTrue(grid.IsPlatformMode, "level_" + n + " 应是平台簇模式（逐格水陆）");
                Assert.AreEqual(level.WidthTiles, grid.WidthTiles);
                Assert.AreEqual(level.HeightTiles, grid.DepthTiles);
                Assert.Greater(grid.GroundCellCount, 0, "level_" + n + " 必须有可站地面");
                Assert.Greater(grid.WaterCellCount, 0, "level_" + n + " 必须有水（岛与岛之间）");

                Assert.AreEqual(LevelTileMaps.Parse(n).SolidCount, grid.GroundCellCount,
                    "level_" + n + " 地面格数应 = 原版非空非波纹格数");
                Assert.AreEqual(LevelTileMaps.Parse(n).RippleCount,
                    CountWaterRipples(grid, n),
                    "level_" + n + " 的海面波纹格必须落在地图的水格里");

                // 出生位：站在原版地面上、地表高于水面。
                for (int i = 0; i < level.Units.Count; i++)
                {
                    LevelUnit u = level.Units[i];
                    Assert.IsTrue(grid.IsGroundAt(u.gridX, u.gridY),
                        "level_" + n + " 的 " + u.typeName + " (" + u.gridX + "," + u.gridY + ") 落水了");
                    Assert.GreaterOrEqual(grid.BlocksAt(u.gridX, u.gridY), 1,
                        "level_" + n + " 出生格块高应 ≥1");
                    Assert.IsFalse(LevelGeometry.IsBelowWater(
                            grid.SurfaceWorldY(u.gridX, u.gridY), LevelGeometry.WaterSurfaceY),
                        "level_" + n + " 出生格地表不得在水面以下");
                    Assert.IsTrue(TerrainCatalog.IsPlatformSpawnSafe(grid, u.gridX, u.gridY),
                        "level_" + n + " 出生格应判安全");
                }
            }
        }

        static int CountWaterRipples(TileTerrainGrid grid, int levelNumber)
        {
            LevelTileMapData tiles = LevelTileMaps.Parse(levelNumber);
            int n = 0;
            for (int i = 0; i < tiles.RippleCells.Count; i++)
            {
                LevelTileCell cell = tiles.RippleCells[i];
                int gz = LevelTileMapData.RowToGridZ(cell.RowY);
                if (gz >= 0 && gz < grid.DepthTiles && !grid.IsGroundAt(cell.X, gz))
                    n++;
            }

            return n;
        }

        [Test]
        public void SizeMismatch_OrUnknownLevel_ReturnsNullInsteadOfMisplacedTerrain()
        {
            // 行串按关卡自带的尺寸解析；尺寸不符必须退回平地而不是错位生成。
            Assert.IsNull(TerrainCatalog.Build(1, 49, 17), "宽度不符 → null");
            Assert.IsNull(TerrainCatalog.Build(1, 50, 16), "纵深不符 → null");
            Assert.IsNull(TerrainCatalog.Build(0, 50, 17), "非法关号 → null");
            Assert.IsNull(TerrainCatalog.Build(34, 50, 17), "超出 33 关 → null");
        }

        // ------------------------------------------------------------------
        // level_1 的逐值口径（原版 50×17：9 岛 / 163 地面 / 687 水）
        // ------------------------------------------------------------------

        [Test]
        public void Level1_IsPlatformMode_WithWaterAndGround()
        {
            TileTerrainGrid grid = Level1();

            Assert.AreEqual(9, grid.ClusterCount, "level_1 原版含 9 座岛");
            Assert.AreEqual(163, grid.GroundCellCount, "level_1 原版地面格 = 163");
            Assert.AreEqual(850 - 163, grid.WaterCellCount, "其余 687 格是水（含海面波纹）");
            Assert.AreEqual(Width, grid.WidthTiles);
            Assert.AreEqual(Depth, grid.DepthTiles);
        }

        [Test]
        public void Level1_PlatformHeights_FollowOriginalRowNumbers()
        {
            TileTerrainGrid grid = Level1();

            for (int gz = 0; gz < Depth; gz++)
            {
                for (int gx = 0; gx < Width; gx++)
                {
                    if (!grid.IsGroundAt(gx, gz))
                    {
                        Assert.AreEqual(0, grid.BlocksAt(gx, gz), "水格块高应为 0");
                        Assert.AreEqual(0, grid.BaseBlocksAt(gx, gz), "水格无基准");
                        continue;
                    }

                    PlatformClusterInfo info = grid.ClusterAt(grid.ClusterIndexOf(gx, gz));
                    Assert.AreEqual(info.BaseBlocks + PlatformClusterLayout.TileLocalBlocks,
                        grid.BlocksAt(gx, gz), "地面格总块高 = 岛基准 + 1 块局部");
                    Assert.LessOrEqual(grid.BlocksAt(gx, gz),
                        PlatformClusterLayout.TileMaxBaseBlocks + PlatformClusterLayout.TileLocalBlocks,
                        "块高不得超过相机可达上限");
                }
            }

            // 原版读数：西侧草岛（x23–28）顶行 4 → 12 块基准 → 13 块总高；
            // 左船体甲板（x0–18）顶行 11 → 4 块基准 → 5 块总高。
            // 【2026-09-14 修复】单块世界高度 = 0.5 单位（TerrainCatalog.DefaultBlockWorldHeight =
            // PixelsToUnits(8)，格 1→2 世界单位后随格放大），地表 = 块数 × 0.5；
            // 旧期望 1.25/3.25 是 0.25 时代的口径。两座岛的高差 = (13−5)×0.5 = 4 世界单位。
            Assert.AreEqual(13, grid.BlocksAt(24, 5), "(24,5) 属西侧草岛 → 13 块");
            Assert.AreEqual(5, grid.BlocksAt(17, 10), "(17,10) 属左船体 → 5 块");
            Assert.AreEqual(2.5f, grid.SurfaceWorldY(17, 10), 1e-5f, "左船体地表 = 2.5 世界单位");
            Assert.AreEqual(6.5f, grid.SurfaceWorldY(24, 5), 1e-5f, "西侧草岛地表 = 6.5 世界单位");
        }

        [Test]
        public void Level1_WaterIsVoid_AndDeckIsPlaceable()
        {
            TileTerrainGrid grid = Level1();

            // (21,8) 在原版行串里是海面（旧手写布局在这造过一条水道 —— 结论相同，来源不同）。
            Assert.IsFalse(grid.IsGroundAt(21, 8), "(21,8) 应是水");

            float surface = grid.SurfaceWorldYAtWorld(21.5f, 8.5f);
            Assert.Less(surface, LevelGeometry.WaterSurfaceY,
                "水格地表须是水面以下的虚空哨兵，否则 AI 落水判定失效");
            Assert.AreEqual(TileTerrainGrid.WaterVoidY, surface, 1e-6f);

            var terrain = new AiTerrain(0f, Width * 32f, 0f, Depth * 32f, grid);
            Assert.IsTrue(terrain.IsBlocked(21.5f * 32f, 8.5f * 32f), "水格不可放置箱体");
            Assert.IsFalse(terrain.CanPlace(21.5f * 32f, 8.5f * 32f, 8f, 8f));

            // 船体甲板（(17,10) 5 块）：合法放置面（平台模式下水格才是不可放置的）。
            Assert.IsTrue(grid.IsGroundAt(17, 10));
            Assert.IsFalse(terrain.IsBlocked(17.5f * 32f, 10.5f * 32f), "船甲板是合法放置面");
            Assert.IsTrue(terrain.CanPlace(17.5f * 32f, 10.5f * 32f, 8f, 8f));
        }

        [Test]
        public void DestroyedPlatformCell_BecomesWater()
        {
            TileTerrainGrid grid = Level1();

            // (17,10) 属左船体：4 块基准 + 1 块局部 = 5 块。
            Assert.AreEqual(5, grid.BlocksAt(17, 10));
            for (int i = 0; i < 5; i++)
                Assert.IsTrue(grid.DestroyBlock(17, 10), "第 " + (i + 1) + " 次逐块摧毁应生效");

            Assert.AreEqual(0, grid.BlocksAt(17, 10));
            Assert.IsFalse(grid.IsGroundAt(17, 10), "平台被炸空后该格应变成水");
            // 【2026-09-14 修复：格→世界换算】SurfaceWorldYAtWorld 收世界坐标；
            // (17,10) 格中心的世界坐标 = TileToWorld(格号+0.5) = (35, 21)
            // （1 格 = 2 世界单位，旧断言把格号当世界坐标会查到 (8,5) 那格）。
            Assert.Less(
                grid.SurfaceWorldYAtWorld(LevelGeometry.TileToWorld(17.5f), LevelGeometry.TileToWorld(10.5f)),
                LevelGeometry.WaterSurfaceY);
        }

        [Test]
        public void DestroyInRadius_ZeroesCellsAndKeepsOthers()
        {
            TileTerrainGrid grid = Level1();

            // 中央沙洲（岛 6：包络 x[20,39] / z[8,12]，但包络只是包围盒——实际地面集中在
            // gridZ=11/12 两行，gridZ=10 在岛中部是水）内部炸一发：命中范围内整格清零。
            // 【2026-09-14 修复：格→世界换算】DestroyInRadius 收世界坐标；爆心取沙洲地面格 (30,11)
            // 的格中心世界坐标（TileToWorld(30.5), TileToWorld(11.5) = (61, 23)），高度取该格
            // 地表（DestroyInRadius 的格心高度 = 簇基准 + 局部堆高一半，距地表 0.25 < 半径 1.2，
            // 见 TileTerrainGrid.cs:423-428）；旧断言把格号当世界坐标，实际炸在了 (15,5) 附近的水/桅区。
            Assert.IsTrue(grid.IsGroundAt(30, 11), "(30,11) 应是沙洲地面格（gridZ=10 中部是水）");
            float centerY = grid.SurfaceWorldY(30, 11);
            var destroyed = new List<int>();
            int count = grid.DestroyInRadius(
                new Vector3(LevelGeometry.TileToWorld(30.5f), centerY, LevelGeometry.TileToWorld(11.5f)),
                1.2f, destroyed);

            Assert.Greater(count, 0, "沙洲上炸一发应至少摧毁 1 格");
            Assert.AreEqual(count, destroyed.Count);

            for (int i = 0; i < destroyed.Count; i++)
            {
                int index = destroyed[i];
                Assert.AreEqual(0, grid.BlocksAt(index % Width, index / Width), "被摧毁格应清零");
            }
        }

        [Test]
        public void PlatformLayout_IsDeterministic()
        {
            TileTerrainGrid a = Level1();
            TileTerrainGrid b = Level1();

            Assert.AreEqual(a.ClusterCount, b.ClusterCount);
            for (int gy = 0; gy < Depth; gy++)
            {
                for (int gx = 0; gx < Width; gx++)
                {
                    Assert.AreEqual(a.IsGroundAt(gx, gy), b.IsGroundAt(gx, gy));
                    Assert.AreEqual(a.BlocksAt(gx, gy), b.BlocksAt(gx, gy));
                    Assert.AreEqual(a.ClusterIndexOf(gx, gy), b.ClusterIndexOf(gx, gy));
                }
            }
        }

        // ------------------------------------------------------------------
        // 列式旧地形（兜底路径的 TileTerrainGrid 语义）
        // ------------------------------------------------------------------

        [Test]
        public void LegacyColumnGrid_KeepsBaseGroundEverywhere()
        {
            // 列式旧地形（无平台地图）仍被支持：全图有基础地面、0 块格也是地面。
            // 【2026-09-14】TerrainCatalog 不再为任何关卡生成它（33 关都走原版行串），
            // 这里直接用构造函数覆盖该模式的语义，保证兜底路径不退化成"水格"。
            var blocks = new int[4 * 4];
            blocks[1 + 1 * 4] = 4;
            var grid = new TileTerrainGrid(4, 4, blocks, 0.25f);

            Assert.IsFalse(grid.IsPlatformMode);
            Assert.AreEqual(0, grid.ClusterCount);
            Assert.AreEqual(16, grid.GroundCellCount, "旧列模式全图有基础地面（不挖洞）");
            Assert.AreEqual(0, grid.WaterCellCount);
            Assert.IsTrue(grid.IsGroundAt(0, 0), "0 块格在旧模式仍是地面");
            Assert.AreEqual(LevelGeometry.GroundTopY, grid.SurfaceWorldY(0, 0), 1e-6f);
        }
    }
}
