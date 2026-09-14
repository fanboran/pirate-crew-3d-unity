using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 原版瓦片行串的数据层用例（纯 C#，无头可跑）。
    ///
    /// 【数据链】<c>external/swf-decompile/levels_all.json</c>（33 关关卡 XML，文档 §7.2 指明的数据源）
    ///   → <see cref="LevelTileMaps"/>（行串原文 + 语义表 + 稀疏表解析器）
    ///   → <see cref="PlatformClusterLayout.BuildFor(LevelData)"/>（岛簇地图）。
    /// 与源数据的逐行串比对在 <c>LevelTileMapsJsonParityTests</c>（读不到 external/ 时 Skip）。
    ///
    /// 【2026-09-14】本文件是"关卡地形 1:1 翻译"的验收：33 关逐一断言
    ///   · 语义表穷举（Unknown 计数 = 0）；
    ///   · 行串自洽（宽度 = 关卡宽、行数 = 关卡高）；
    ///   · 每个出战单位的 (gridX, gridY) 落在原版地面格上（翻译正确性硬判据）；
    ///   · 岛高次序与原版行号一致（原版里高的岛，3D 里也高）；
    ///   · 岛与岛的水距语义与原版一致（独立实现交叉核对）；
    ///   · level_1 的逐值口径（旧手写布局断言已按原版地图重写）。
    /// </summary>
    [TestFixture]
    public class LevelTileMapsTests
    {
        const int LevelCount = 33;

        /// <summary>33 关合计：单元格 / 非空格 / 地面格（回归哨兵，数据一改就会响）。</summary>
        const int TotalCells = 42251;
        const int TotalNonDash = 11815;
        const int TotalSolid = 8150;
        const int TotalUnits = 360;
        const int TotalIslands = 286;

        // ------------------------------------------------------------------
        // 行串自洽 + 语义表穷举
        // ------------------------------------------------------------------

        [Test]
        public void AllLevels_HaveRowsMatchingCatalogDimensions()
        {
            for (int n = 1; n <= LevelCount; n++)
            {
                LevelTileMapData map = LevelTileMaps.Parse(n);
                Assert.IsNotNull(map, "level_" + n + " 应有原版行串");

                LevelData level = LevelCatalog.Get(n);
                Assert.AreEqual(level.WidthTiles, map.Width, "level_" + n + " 宽应 = LevelCatalog 的宽");
                Assert.AreEqual(level.HeightTiles, map.Height, "level_" + n + " 高应 = LevelCatalog 的高");
                Assert.AreEqual(level.HeightTiles, LevelTileMaps.HeightOf(n));

                for (int rowY = 0; rowY < map.Height; rowY++)
                {
                    int cells = LevelTileMaps.CountCells(LevelTileMaps.RawRows(n)[rowY]);
                    Assert.AreEqual(map.Width, cells,
                        "level_" + n + " 第 " + rowY + " 行展开格数应 = 宽度");
                }
            }
        }

        [Test]
        public void TileSemanticTable_IsExhaustive_NoUnknownFamilies()
        {
            var names = new HashSet<string>();

            for (int n = 1; n <= LevelCount; n++)
            {
                LevelTileMapData map = LevelTileMaps.Parse(n);
                for (int i = 0; i < map.Cells.Count; i++)
                    names.Add(map.Cells[i].TileName);
            }

            // 类头语义表登记了 76 个名字（75 个瓦片名 + 空格 "-"）；出现过的名字必须全被登记。
            Assert.AreEqual(75, names.Count, "全 33 关出现过的瓦片名应为 75 个（不含 \"-\"）");

            foreach (string name in names)
            {
                LevelTileFamily family = LevelTileMaps.FamilyOf(name);
                Assert.AreNotEqual(LevelTileFamily.Unknown, family,
                    "瓦片 " + name + " 未登记进语义表（Unknown = 待定，会让地形口径失控）");
            }
        }

        [Test]
        public void SemanticTable_ClassifiesByOriginalTileVocabulary()
        {
            // 水：空 + 海面波纹（波纹不是可站面）。
            Assert.AreEqual(LevelTileFamily.Water, LevelTileMaps.FamilyOf("-"));
            Assert.AreEqual(LevelTileFamily.Water, LevelTileMaps.FamilyOf("tile_ripple_middle"));
            Assert.AreEqual(LevelTileFamily.Water, LevelTileMaps.FamilyOf("boat_ripple_3"));

            // 陆地：草地 / 土 / 沙洲 / 船体 / 桅 / 桅盘。
            Assert.AreEqual(LevelTileFamily.Grass, LevelTileMaps.FamilyOf("grass_top_left"));
            Assert.AreEqual(LevelTileFamily.Grass, LevelTileMaps.FamilyOf("single_grass_2"));
            Assert.AreEqual(LevelTileFamily.Earth, LevelTileMaps.FamilyOf("earth_edge_middle_1"));
            Assert.AreEqual(LevelTileFamily.Earth, LevelTileMaps.FamilyOf("eartyh_bottom_middle"),
                "原版拼写错误 eartyh_bottom_middle 必须照原样登记为土体");
            Assert.AreEqual(LevelTileFamily.Sand, LevelTileMaps.FamilyOf("blank_sand_middle_1"));
            Assert.AreEqual(LevelTileFamily.Sand, LevelTileMaps.FamilyOf("shell_sand_top_left"));
            Assert.AreEqual(LevelTileFamily.Ship, LevelTileMaps.FamilyOf("ship_top_middle"));
            Assert.AreEqual(LevelTileFamily.Ship, LevelTileMaps.FamilyOf("cannon_port_2"));
            Assert.AreEqual(LevelTileFamily.Mast, LevelTileMaps.FamilyOf("mast_tile"));
            Assert.AreEqual(LevelTileFamily.CrowNest, LevelTileMaps.FamilyOf("crows_nest_1"));

            // 船体语汇判据（决定岛簇 Kind）。
            Assert.IsTrue(LevelTileMaps.IsShipTile("mast_end_left"));
            Assert.IsTrue(LevelTileMaps.IsShipTile("crows_nest_2"));
            Assert.IsFalse(LevelTileMaps.IsShipTile("grass_top_right"));
            Assert.IsFalse(LevelTileMaps.IsShipTile("tile_ripple_left"));
        }

        [Test]
        public void WaterAndGroundCounts_AreConsistent()
        {
            int totalSolid = 0;
            int totalNonDash = 0;
            int totalCells = 0;

            for (int n = 1; n <= LevelCount; n++)
            {
                LevelTileMapData map = LevelTileMaps.Parse(n);

                Assert.AreEqual(map.Cells.Count, map.NonDashCount, "非空格数应 = 稀疏表条目数");
                Assert.AreEqual(map.SolidCells.Count, map.SolidCount);
                Assert.AreEqual(map.RippleCells.Count, map.RippleCount);
                Assert.AreEqual(map.NonDashCount, map.SolidCount + map.RippleCount,
                    "非空格 = 地面格 + 波纹格");
                Assert.Greater(map.SolidCount, 0, "level_" + n + " 必须有可站地面");

                totalSolid += map.SolidCount;
                totalNonDash += map.NonDashCount;
                totalCells += map.Width * map.Height;
            }

            // 事实记录（改数据时会显著变化，作为回归哨兵）。
            Assert.AreEqual(TotalSolid, totalSolid, "33 关地面格合计");
            Assert.AreEqual(TotalNonDash, totalNonDash, "33 关非空格合计");
            Assert.AreEqual(TotalCells, totalCells, "33 关单元格合计");
            Assert.AreEqual(TotalCells - TotalSolid, totalCells - totalSolid, "33 关水格合计（含波纹）");
        }

        [Test]
        public void TopRow_IsNeverSolid_BecauseGridZShiftsByOne()
        {
            // gridZ = rowY − 1 的前提：原版第 0 行（最上一行）永远没有地面格，
            // 否则减 1 会把该行的地形挤出地图。33 关逐一确认。
            for (int n = 1; n <= LevelCount; n++)
            {
                LevelTileMapData map = LevelTileMaps.Parse(n);
                for (int x = 0; x < map.Width; x++)
                {
                    Assert.IsFalse(map.IsSolidAt(x, 0),
                        "level_" + n + " 第 0 行 x=" + x + " 出现了地面格 —— gridZ 位移不再安全");
                }
            }
        }

        [Test]
        public void EveryUnit_StandsOnAnOriginalGroundTile()
        {
            // 翻译正确性的**硬判据**：原版对象 y 是"脚底行 − 1"，故单位站在 (x, y+1) 行上。
            // 33 关 360 个单位逐一核对；任何一处落空都说明行号口径或数据转写出错。
            int checkedUnits = 0;

            for (int n = 1; n <= LevelCount; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                LevelTileMapData map = LevelTileMaps.Parse(n);

                for (int i = 0; i < level.Units.Count; i++)
                {
                    LevelUnit u = level.Units[i];
                    checkedUnits++;

                    Assert.IsTrue(map.IsSolidAt(u.gridX, u.gridY + 1),
                        "level_" + n + " 的 " + u.typeName + " (" + u.gridX + "," + u.gridY
                        + ") 脚下应是原版地面格（实测行 " + (u.gridY + 1) + " = "
                        + map.TileAt(u.gridX, u.gridY + 1) + "）");

                    Assert.IsTrue(map.IsSolidAtGrid(u.gridX, u.gridY),
                        "换成 3D 网格口径（gridZ = rowY − 1）后同样必须命中地面格");
                }
            }

            Assert.AreEqual(TotalUnits, checkedUnits, "33 关出战单位总数");
        }

        [Test]
        public void SignatureStats_AreStable()
        {
            // 逐关签名（LevelTileMaps 类头的表）里的关键数字：岛数 / 船岛数 / 地面格数。
            Assert.AreEqual(9, IslandCount(tiles: 1));
            Assert.AreEqual(5, ShipIslandCount(1));
            Assert.AreEqual(163, LevelTileMaps.Parse(1).SolidCount);

            Assert.AreEqual(19, IslandCount(2));
            Assert.AreEqual(0, ShipIslandCount(2));

            // 单岛关：整关是一块陆地（原版就是一座大岩体）。
            Assert.AreEqual(1, IslandCount(7));
            Assert.AreEqual(1, IslandCount(21));
            Assert.AreEqual(1, IslandCount(32));

            // 碎岛关：15 座小岛。
            Assert.AreEqual(15, IslandCount(13));
            Assert.AreEqual(15, IslandCount(30));

            int islands = 0;
            for (int n = 1; n <= LevelCount; n++)
                islands += IslandCount(n);

            Assert.AreEqual(TotalIslands, islands, "33 关岛（连通域）总数");
        }

        // ------------------------------------------------------------------
        // 岛簇地图：单位落位 / 高度次序 / 水距语义
        // ------------------------------------------------------------------

        [Test]
        public void TileIslandMap_ForEveryLevel_KeepsSpawnsOnTheirIsland()
        {
            for (int n = 1; n <= LevelCount; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                Assert.IsNotNull(map, "level_" + n + " 应能推出平台簇地图");

                Assert.IsTrue(PlatformClusterLayout.TryBuildFromTileMap(level, out PlatformMap _),
                    "level_" + n + " 应走原版 tile 路径（而不是退回程序化兜底）");

                Assert.AreEqual(level.WidthTiles, map.WidthTiles);
                Assert.AreEqual(level.HeightTiles, map.DepthTiles);
                Assert.Greater(map.WaterCellCount, 0, "岛与岛之间必须有水");
                Assert.AreEqual(IslandCount(n), map.Clusters.Count,
                    "level_" + n + " 岛数应 = 原版连通域数（独立实现核对）");
                Assert.AreEqual(LevelTileMaps.Parse(n).SolidCount, map.GroundCellCount,
                    "level_" + n + " 地面格数应 = 原版非空非波纹格数");

                for (int i = 0; i < level.Units.Count; i++)
                {
                    LevelUnit u = level.Units[i];

                    Assert.IsTrue(map.IsGround(u.gridX, u.gridY),
                        "level_" + n + " 的 " + u.typeName + " (" + u.gridX + "," + u.gridY + ") 落水了");

                    PlatformClusterInfo cluster = map.ClusterAt(u.gridX, u.gridY);
                    Assert.IsFalse(string.IsNullOrEmpty(cluster.Name),
                        "level_" + n + " 出生位不在任何岛簇内");
                    Assert.IsTrue(cluster.SpawnsTeam(u.teamIndex),
                        "level_" + n + " 出生位所在岛应带 team " + u.teamIndex + " 的出生标记");

                    // 单位站的高度 = 所在岛的基准高度 + 该格局部块高（局部恒 1）。
                    int expected = cluster.BaseBlocks + PlatformClusterLayout.TileLocalBlocks;
                    Assert.AreEqual(expected, map.TotalBlocksAt(u.gridX, u.gridY),
                        "level_" + n + " 单位世界高度应 = 其所在岛高度");
                }
            }
        }

        [Test]
        public void IslandHeightOrder_FollowsOriginalRowNumbers()
        {
            // 硬锚点 2：原版里越高的岛（顶行离水线越远），3D 里浮得越高。逐关逐岛核对单调性。
            for (int n = 1; n <= LevelCount; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                int waterRow = (int)level.WaterTileY;

                for (int a = 0; a < map.Clusters.Count; a++)
                {
                    for (int b = a + 1; b < map.Clusters.Count; b++)
                    {
                        int spanA = waterRow - TopRowOfCluster(map, a);
                        int spanB = waterRow - TopRowOfCluster(map, b);

                        if (spanA == spanB)
                        {
                            Assert.AreEqual(map.Clusters[a].BaseBlocks, map.Clusters[b].BaseBlocks,
                                "level_" + n + " 同高的两座岛应取同一悬浮高度");
                            continue;
                        }

                        PlatformClusterInfo taller = spanA > spanB ? map.Clusters[a] : map.Clusters[b];
                        PlatformClusterInfo lower = spanA > spanB ? map.Clusters[b] : map.Clusters[a];
                        Assert.Greater(taller.BaseBlocks, lower.BaseBlocks,
                            "level_" + n + " 岛高次序应跟随原版行号（"
                            + map.Clusters[a].Name + " span=" + spanA + " vs "
                            + map.Clusters[b].Name + " span=" + spanB + "）");
                    }
                }
            }
        }

        [Test]
        public void IslandHeights_AreWithinCameraReachAndShowVerticalStructure()
        {
            int flatLevels = 0;

            for (int n = 1; n <= LevelCount; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);

                int min = int.MaxValue, max = int.MinValue;
                for (int c = 0; c < map.Clusters.Count; c++)
                {
                    int h = map.Clusters[c].BaseBlocks;
                    if (h < min) min = h;
                    if (h > max) max = h;

                    Assert.LessOrEqual(map.Clusters[c].MaxTotalBlocks,
                        PlatformClusterLayout.TileMaxBaseBlocks + PlatformClusterLayout.TileLocalBlocks,
                        "level_" + n + " 岛顶不得超过相机可达上限");
                }

                Assert.GreaterOrEqual(min, 0, "最低岛不得低于水面基准");
                // 【2026-09-14 修复：相机机位/块高双变更后的天花板】全场档相机（距离 30、俯角 45°，
                // BattleCameraController.FullFieldDistance/FullFieldPitchDegrees）只比聚焦点高
                // 30·sin45° ≈ 21.2 世界单位，最高岛顶天花板由 PlatformClusterLayout.TileMaxBaseBlocks
                // 的推导给出 = 36 块基准 + 1 块局部 = 18.5 世界单位（单块 0.5，见
                // TerrainCatalog.DefaultBlockWorldHeight）。旧断言 9 与 +0.25 都是块高 0.25 时代的口径。
                Assert.LessOrEqual(
                    (max + PlatformClusterLayout.TileLocalBlocks) * TerrainCatalog.DefaultBlockWorldHeight,
                    (PlatformClusterLayout.TileMaxBaseBlocks + PlatformClusterLayout.TileLocalBlocks)
                        * TerrainCatalog.DefaultBlockWorldHeight,
                    "level_" + n + " 最高岛顶（含 1 块局部）不得超过相机全场档可达天花板 18.5 世界单位");

                if (max == min)
                {
                    flatLevels++;
                    continue;   // 原版真的全贴水：允许全平，写进统计不硬造
                }

                Assert.GreaterOrEqual(max - min, 8,
                    "level_" + n + " 有竖直结构（原版行号给出），最高-最低岛高差应 ≥ 8 块 = 4 世界单位");
            }

            // 33 关里"原版本身全贴水"的关（其余都有竖直结构，做成错落悬浮）。
            Assert.AreEqual(7, flatLevels,
                "全贴水关应为 7 关（level_7/13/15/21/23/30/32）—— 统计记录，不是硬造");
        }

        [Test]
        public void WaterGaps_BetweenIslands_MatchOriginalTileMap()
        {
            // 硬锚点 3：哪些岛隔水、隔多远，必须与原版一致。
            // 参考值由**测试内独立实现**的连通域包络算出（不复用被测代码）。
            for (int n = 1; n <= LevelCount; n++)
            {
                LevelData level = LevelCatalog.Get(n);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                List<int[]> reference = ReferenceIslands(LevelTileMaps.Parse(n));

                Assert.AreEqual(reference.Count, map.Clusters.Count,
                    "level_" + n + " 岛数应 = 独立实现算出的连通域数");

                for (int c = 0; c < reference.Count; c++)
                {
                    Assert.AreEqual(reference[c][0], map.Clusters[c].X0, "level_" + n + " 岛 " + c + " X0");
                    Assert.AreEqual(reference[c][1], map.Clusters[c].X1, "level_" + n + " 岛 " + c + " X1");
                    Assert.AreEqual(reference[c][2], map.Clusters[c].Z0, "level_" + n + " 岛 " + c + " Z0");
                    Assert.AreEqual(reference[c][3], map.Clusters[c].Z1, "level_" + n + " 岛 " + c + " Z1");
                }

                for (int a = 0; a < reference.Count; a++)
                {
                    for (int b = a + 1; b < reference.Count; b++)
                    {
                        int expected = Max(Gap(reference[a][0], reference[a][1], reference[b][0], reference[b][1]),
                            Gap(reference[a][2], reference[a][3], reference[b][2], reference[b][3]));
                        int actual = PlatformClusterLayout.WaterGapTiles(map.Clusters[a], map.Clusters[b]);

                        Assert.AreEqual(expected, actual,
                            "level_" + n + " 的 " + map.Clusters[a].Name + " 与 " + map.Clusters[b].Name
                            + " 水距应与原版一致");
                    }
                }
            }
        }

        [Test]
        public void SignatureVocabulary_MatchesTileVocabulary()
        {
            // 每关的"记忆点语汇"必须来自原版行串：原版有船体/桅/桅盘瓦片 →
            // 该关必有 Ship 岛簇；反之整关没有船体瓦片 → 不应凭空出现船簇。
            for (int n = 1; n <= LevelCount; n++)
            {
                LevelTileMapData tiles = LevelTileMaps.Parse(n);
                bool hasShipTiles = false;
                for (int i = 0; i < tiles.SolidCells.Count; i++)
                {
                    if (LevelTileMaps.IsShipTile(tiles.SolidCells[i].TileName))
                    {
                        hasShipTiles = true;
                        break;
                    }
                }

                PlatformMap map = PlatformClusterLayout.BuildFor(LevelCatalog.Get(n));
                bool hasShipCluster = false;
                for (int c = 0; c < map.Clusters.Count; c++)
                {
                    if (map.Clusters[c].Kind == PlatformClusterKind.Ship)
                    {
                        hasShipCluster = true;
                        break;
                    }
                }

                Assert.AreEqual(hasShipTiles, hasShipCluster,
                    "level_" + n + " 的船簇应与原版船体瓦片一致（记忆点不能凭空出现/消失）");
            }

            // 有船的关（原版 tile 里读得出的"那一艘船"）—— 统计记录。
            int shipLevels = 0;
            for (int n = 1; n <= LevelCount; n++)
            {
                if (ShipIslandCount(n) > 0)
                    shipLevels++;
            }

            Assert.AreEqual(8, shipLevels, "33 关里有船语汇的关应为 8 关");
        }

        [Test]
        public void TileMap_IsDeterministic()
        {
            for (int n = 1; n <= 3; n++)
            {
                PlatformMap a = PlatformClusterLayout.BuildFor(LevelCatalog.Get(n));
                PlatformMap b = PlatformClusterLayout.BuildFor(LevelCatalog.Get(n));

                Assert.AreEqual(a.Clusters.Count, b.Clusters.Count);
                for (int i = 0; i < a.CellCluster.Length; i++)
                {
                    Assert.AreEqual(a.CellCluster[i], b.CellCluster[i], "格 " + i + " 归属应确定");
                    Assert.AreEqual(a.CellBlocks[i], b.CellBlocks[i], "格 " + i + " 块高应确定");
                }
            }
        }

        // ------------------------------------------------------------------
        // level_1 的逐值口径（旧手写布局断言已按原版地图重写）
        // ------------------------------------------------------------------

        [Test]
        public void Level1_MatchesOriginalTileMap()
        {
            LevelTileMapData tiles = LevelTileMaps.Parse(1);
            PlatformMap map = PlatformClusterLayout.BuildFor(LevelCatalog.Get(1));

            Assert.AreEqual(50, map.WidthTiles);
            Assert.AreEqual(17, map.DepthTiles);

            // 原版行串给出 9 座岛、163 格地面。
            // 【2026-09-14 起以原版 tile 地图为准】旧手写布局是 4 簇 / 411 格地面（瞎编的，已删除）。
            Assert.AreEqual(9, map.Clusters.Count, "level_1 原版含 9 座岛");
            Assert.AreEqual(163, map.GroundCellCount, "level_1 原版地面格 = 163");
            Assert.AreEqual(50 * 17, map.GroundCellCount + map.WaterCellCount);
            Assert.AreEqual(tiles.SolidCount, map.GroundCellCount,
                "地图地面格数应 = 原版非空非波纹格数");

            // 5 座船岛（左船体 / 右船体 / 左船横桁 / 右船横桁 / 桅盘），其余 4 座是岛。
            int ships = 0;
            for (int c = 0; c < map.Clusters.Count; c++)
            {
                if (map.Clusters[c].Kind == PlatformClusterKind.Ship)
                    ships++;
            }

            Assert.AreEqual(5, ships, "level_1 原版含 5 座船岛");
            Assert.AreEqual(4, map.Clusters.Count - ships, "其余 4 座是岛");

            // 逐岛口径（原版行串读出的 9 座岛，按扫描顺序；改数据即响的回归锚点）。
            //   0/1 两座草岛（x23–28 / x32–37，顶行 4 → 12 块基准 = 3 世界单位）
            //   2   桅盘（x46–47，顶行 5 → 11 块）
            //   3   左船横桁（x2–13，顶行 6 → 10 块）
            //   4   右船横桁（x44–49，顶行 7 → 9 块）
            //   5   沙洲小岛（x29–31，顶行 8 → 8 块）
            //   6   中央沙洲（x20–39，顶行 9 → 6 块）
            //   7/8 左右船体甲板（x0–18 / x41–49，顶行 11 → 4 块 = 1 世界单位）
            int[][] expected = {
                new[] { 23, 28, 12 }, new[] { 32, 37, 12 }, new[] { 46, 47, 11 },
                new[] { 2, 13, 10 }, new[] { 44, 49, 9 }, new[] { 29, 31, 8 },
                new[] { 20, 39, 6 }, new[] { 0, 18, 4 }, new[] { 41, 49, 4 },
            };

            for (int c = 0; c < expected.Length; c++)
            {
                Assert.AreEqual(expected[c][0], map.Clusters[c].X0, "岛 " + c + " 西界");
                Assert.AreEqual(expected[c][1], map.Clusters[c].X1, "岛 " + c + " 东界");
                Assert.AreEqual(expected[c][2], map.Clusters[c].BaseBlocks, "岛 " + c + " 悬浮基准块数");
                Assert.AreEqual(expected[c][2] + PlatformClusterLayout.TileLocalBlocks,
                    map.Clusters[c].MaxTotalBlocks, "岛 " + c + " 总块高");
            }

            // 旧断言对照：旧手写地图在 (21,8) 造了水道、(24,5) 造过"大船甲板 2 块高"。
            // 原版行串给的是：(21,8) 是海面；(24,5) 是西侧草岛的土体（属岛 0，基准 12 块）。
            Assert.IsFalse(map.IsGround(21, 8), "(21,8) 原版是海面");
            Assert.IsTrue(map.IsGround(24, 5), "(24,5) 原版是岛 0 的土体（eartyh_bottom_middle）");
            Assert.AreEqual(12 + PlatformClusterLayout.TileLocalBlocks, map.TotalBlocksAt(24, 5),
                "(24,5) 属岛 0 → 高度 = 12 块基准 + 1 块局部");
        }

        [Test]
        public void Level27_OriginalTileMap_IsATwoTierDuel()
        {
            // level_27（1v1 决斗）：原版行串给的是"两座高台 + 中间低地"。
            // 【2026-09-14 起以原版 tile 地图为准】旧断言用的是手抄列高表（两侧 8 块 / 中间 0 块）。
            LevelData level = LevelCatalog.Get(27);
            PlatformMap map = PlatformClusterLayout.BuildFor(level);

            Assert.AreEqual(21, map.WidthTiles);
            Assert.AreEqual(20, map.DepthTiles);
            Assert.IsTrue(PlatformClusterLayout.TryBuildFromTileMap(level, out PlatformMap _),
                "level_27 现在也走原版 tile 路径（旧列高表已删除）");

            for (int i = 0; i < level.Units.Count; i++)
            {
                LevelUnit u = level.Units[i];
                Assert.IsTrue(map.IsGround(u.gridX, u.gridY),
                    "level_27 的 " + u.typeName + " 应站在原版地面上");
            }

            Assert.AreEqual(IslandCount(27), map.Clusters.Count);
        }

        // ------------------------------------------------------------------
        // 参考实现（仅测试用，独立于被测代码）
        // ------------------------------------------------------------------

        /// <summary>原版连通域（岛）数：扫描顺序与 <c>BuildFromTileMap</c> 一致（先行后列）。</summary>
        static int IslandCount(int tiles)
        {
            return ReferenceIslands(LevelTileMaps.Parse(tiles)).Count;
        }

        /// <summary>原版连通域（岛）的包络列表 [x0, x1, gridZ0, gridZ1]，按扫描顺序。</summary>
        static List<int[]> ReferenceIslands(LevelTileMapData tiles)
        {
            var seen = new bool[tiles.Width * tiles.Height];
            var result = new List<int[]>();

            for (int rowY = 0; rowY < tiles.Height; rowY++)
            {
                for (int x = 0; x < tiles.Width; x++)
                {
                    if (seen[x + rowY * tiles.Width] || !tiles.IsSolidAt(x, rowY))
                        continue;

                    int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                    var stack = new Stack<int>();
                    stack.Push(x + rowY * tiles.Width);
                    seen[x + rowY * tiles.Width] = true;

                    while (stack.Count > 0)
                    {
                        int idx = stack.Pop();
                        int cx = idx % tiles.Width;
                        int cy = idx / tiles.Width;
                        int cz = LevelTileMapData.RowToGridZ(cy);

                        if (cx < x0) x0 = cx;
                        if (cx > x1) x1 = cx;
                        if (cz < z0) z0 = cz;
                        if (cz > z1) z1 = cz;

                        PushCell(tiles, seen, stack, cx - 1, cy);
                        PushCell(tiles, seen, stack, cx + 1, cy);
                        PushCell(tiles, seen, stack, cx, cy - 1);
                        PushCell(tiles, seen, stack, cx, cy + 1);
                    }

                    result.Add(new[] { x0, x1, z0, z1 });
                }
            }

            return result;
        }

        /// <summary>船岛数（含船体 / 桅 / 桅盘语汇的连通域）。</summary>
        static int ShipIslandCount(int levelNumber)
        {
            LevelTileMapData tiles = LevelTileMaps.Parse(levelNumber);
            var seen = new bool[tiles.Width * tiles.Height];
            int ships = 0;

            for (int rowY = 0; rowY < tiles.Height; rowY++)
            {
                for (int x = 0; x < tiles.Width; x++)
                {
                    if (seen[x + rowY * tiles.Width] || !tiles.IsSolidAt(x, rowY))
                        continue;

                    bool ship = false;
                    var stack = new Stack<int>();
                    stack.Push(x + rowY * tiles.Width);
                    seen[x + rowY * tiles.Width] = true;

                    while (stack.Count > 0)
                    {
                        int idx = stack.Pop();
                        int cx = idx % tiles.Width;
                        int cy = idx / tiles.Width;
                        if (!ship && LevelTileMaps.IsShipTile(tiles.TileAt(cx, cy)))
                            ship = true;

                        PushCell(tiles, seen, stack, cx - 1, cy);
                        PushCell(tiles, seen, stack, cx + 1, cy);
                        PushCell(tiles, seen, stack, cx, cy - 1);
                        PushCell(tiles, seen, stack, cx, cy + 1);
                    }

                    if (ship)
                        ships++;
                }
            }

            return ships;
        }

        static void PushCell(LevelTileMapData tiles, bool[] seen, Stack<int> stack, int x, int rowY)
        {
            if (x < 0 || rowY < 0 || x >= tiles.Width || rowY >= tiles.Height)
                return;

            int idx = x + rowY * tiles.Width;
            if (seen[idx] || !tiles.IsSolidAt(x, rowY))
                return;

            seen[idx] = true;
            stack.Push(idx);
        }

        /// <summary>某岛簇自己格子里最靠上的原版行号（= gridZ 最小的格子 + 1）。</summary>
        static int TopRowOfCluster(PlatformMap map, int clusterIndex)
        {
            int minZ = int.MaxValue;
            for (int i = 0; i < map.CellCluster.Length; i++)
            {
                if (map.CellCluster[i] != clusterIndex)
                    continue;

                int gz = i / map.WidthTiles;
                if (gz < minZ)
                    minZ = gz;
            }

            return minZ == int.MaxValue ? 0 : LevelTileMapData.GridZToRow(minZ);
        }

        static int Gap(int a0, int a1, int b0, int b1)
        {
            int gap = Max(a0 - b1 - 1, b0 - a1 - 1);
            return gap < 0 ? 0 : gap;
        }

        static int Max(int a, int b) => a > b ? a : b;
    }
}
