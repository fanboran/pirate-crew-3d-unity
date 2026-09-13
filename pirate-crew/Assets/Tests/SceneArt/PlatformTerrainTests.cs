using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 平台化地形语义的纯 C# 用例（无头可跑）：验证用户诉求「一堆高高低低的悬空平台浮在海面上、
    /// 平台间是水（掉落即死）」在数据层成立，且不破坏玩法契约。
    ///
    /// 【覆盖】
    ///   · 空列 = 水：<see cref="TileTerrainGrid"/> 的平台簇模式（无地面、无碰撞）；
    ///   · 出生位全部在平台格上、无初始落水；
    ///   · 簇间水距 ≥2 格、且小于角色最大投掷射程（投掷可跨、走路必落水）；
    ///   · 确定性重建（同定义同地图）；
    ///   · 爆炸把平台格炸空后变成水（SurfaceWorldYAtWorld 落到水面以下）；
    ///   · AI 落点语义：水格地表 = 虚空哨兵（< WaterSurfaceY），使投掷模拟判定落水；
    ///   · 旧列式关卡（level_27）语义不变（全图有基础地面）。
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

        [Test]
        public void Level1_IsPlatformMode_WithWaterAndGround()
        {
            TileTerrainGrid grid = Level1();

            Assert.AreEqual(4, grid.ClusterCount, "应为 4 个平台簇");
            Assert.Greater(grid.WaterCellCount, 0, "平台之间必须是水");
            Assert.Greater(grid.GroundCellCount, 0, "必须有可站的平台地面");
            Assert.AreEqual(Width, grid.WidthTiles);
            Assert.AreEqual(Depth, grid.DepthTiles);
        }

        [Test]
        public void AllSpawnCells_AreOnGround_NotWater()
        {
            TileTerrainGrid grid = Level1();
            LevelData level = LevelCatalog.Get(1);

            for (int i = 0; i < level.Units.Count; i++)
            {
                int gx = level.Units[i].gridX;
                int gy = level.Units[i].gridY;

                Assert.IsTrue(grid.IsGroundAt(gx, gy),
                    "出生格 (" + gx + "," + gy + ") 必须在平台上，否则开局落水");
                Assert.GreaterOrEqual(grid.BlocksAt(gx, gy), 1,
                    "出生格 (" + gx + "," + gy + ") 平台块高应 ≥1");
                Assert.IsFalse(LevelGeometry.IsBelowWater(grid.SurfaceWorldY(gx, gy), LevelGeometry.WaterSurfaceY),
                    "出生格地表不得在水面以下");
                Assert.IsTrue(TerrainCatalog.IsPlatformSpawnSafe(grid, gx, gy),
                    "IsPlatformSpawnSafe 应判出生格安全");
            }
        }

        [Test]
        public void ClusterWaterGaps_AreAtLeastTwoTiles()
        {
            TileTerrainGrid grid = Level1();

            for (int a = 0; a < grid.ClusterCount; a++)
            {
                for (int b = a + 1; b < grid.ClusterCount; b++)
                {
                    int gap = TerrainCatalog.WaterGapTiles(grid.ClusterAt(a), grid.ClusterAt(b));
                    Assert.GreaterOrEqual(gap, 2,
                        "簇 " + grid.ClusterAt(a).Name + " 与 " + grid.ClusterAt(b).Name
                        + " 之间的水距应 ≥2 格，实际 " + gap);
                }
            }
        }

        [Test]
        public void ClusterWaterGaps_MatchLevelDesignTargets()
        {
            // 依据 docs/关卡设计语言-参照游戏全场景分析.md §5.3 的校正值：
            // ①↔② 4 格、②↔③ 4 格（§5.4：由第一版 3 格拉齐到 4 格）、②↔④ 2 格。
            // 规则出处：R3（常规关簇间水距 1-5 格）/ R16（单次跨越 ≤ 满力射程 12.5 格）。
            TileTerrainGrid grid = Level1();

            PlatformClusterInfo west = grid.ClusterAt(0);
            PlatformClusterInfo ship = grid.ClusterAt(1);
            PlatformClusterInfo east = grid.ClusterAt(2);
            PlatformClusterInfo islet = grid.ClusterAt(3);

            Assert.AreEqual("terrace_island_west", west.Name);
            Assert.AreEqual("great_ship_center", ship.Name);
            Assert.AreEqual("sky_island_east", east.Name);
            Assert.AreEqual("sky_islet_north", islet.Name);

            Assert.AreEqual(4, TerrainCatalog.WaterGapTiles(west, ship), "①↔② 应为 4 格（§5.3）");
            Assert.AreEqual(4, TerrainCatalog.WaterGapTiles(ship, east), "②↔③ 应为 4 格（§5.4 校正 3→4）");
            Assert.AreEqual(2, TerrainCatalog.WaterGapTiles(ship, islet), "②↔④ 应为 2 格（§5.3）");

            // ④ 北小空岛终值：中央簇 ② 正北 x32-35 z0-1（移位后不切 ②↔③ 主水道）。
            Assert.AreEqual(32, islet.X0);
            Assert.AreEqual(35, islet.X1);
            Assert.AreEqual(0, islet.Z0);
            Assert.AreEqual(1, islet.Z1);
            Assert.AreEqual(3, islet.MaxBlocks, "④ 小空岛为 3 块（§5.3）");

            // ③ 东空岛西界 x43：与 ② 东界 x38 之间正好 4 格水。
            Assert.AreEqual(43, east.X0, "③ 西界应为 x43（§5.4）");
            Assert.AreEqual(38, ship.X1, "② 东界应为 x38");
        }

        [Test]
        public void Clusters_AreReachableByMaxThrow()
        {
            TileTerrainGrid grid = Level1();
            float reach = TerrainCatalog.MaxThrowRangeWorld;

            // 角色满力投掷射程应显著大于 1 格（否则地形设计无意义）。
            Assert.Greater(reach, 5f, "满力投掷射程应 >5 单位，实际 " + reach);

            // 以投掷射程为边建图，要求全图连通（不存在"投掷也到不了的孤岛"）。
            int n = grid.ClusterCount;
            var visited = new bool[n];
            var stack = new Stack<int>();
            visited[0] = true;
            stack.Push(0);

            while (stack.Count > 0)
            {
                int c = stack.Pop();
                for (int k = 0; k < n; k++)
                {
                    if (visited[k])
                        continue;

                    float dist = PlatformClusterLayout.MinEdgeDistanceWorld(grid.ClusterAt(c), grid.ClusterAt(k));
                    if (dist <= reach)
                    {
                        visited[k] = true;
                        stack.Push(k);
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                Assert.IsTrue(visited[i],
                    "簇 " + grid.ClusterAt(i).Name + " 与其余簇投掷不连通（最大射程 " + reach + "）");
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

        [Test]
        public void PlatformHeights_WithinKitRange()
        {
            TileTerrainGrid grid = Level1();

            for (int gy = 0; gy < Depth; gy++)
            {
                for (int gx = 0; gx < Width; gx++)
                {
                    if (!grid.IsGroundAt(gx, gy))
                    {
                        Assert.AreEqual(0, grid.BlocksAt(gx, gy), "水格块高应为 0");
                        continue;
                    }

                    Assert.GreaterOrEqual(grid.BlocksAt(gx, gy), 1);
                    Assert.LessOrEqual(grid.BlocksAt(gx, gy), PlatformClusterLayout.MaxBlocksPerCluster);
                }
            }
        }

        [Test]
        public void WaterCell_SurfaceIsVoidSentinel_BelowWaterSurface()
        {
            TileTerrainGrid grid = Level1();

            // (21,8) 在西侧梯田岛（X≤19）与中央大船（X≥24）之间的水道里。
            Assert.IsFalse(grid.IsGroundAt(21, 8), "(21,8) 应是水");
            float surface = grid.SurfaceWorldYAtWorld(21.5f, 8.5f);
            Assert.Less(surface, LevelGeometry.WaterSurfaceY,
                "水格地表应是水面以下的虚空哨兵，否则 AI 落水判定会失效");
            Assert.AreEqual(TileTerrainGrid.WaterVoidY, surface, 1e-6f);
        }

        [Test]
        public void AiTerrain_WaterCell_DrownsInsteadOfLanding()
        {
            TileTerrainGrid grid = Level1();
            var terrain = new AiTerrain(0f, Width * 32f, 0f, Depth * 32f, grid);

            // 世界 (21.5, 8.5) → 平面像素 (688, 272)。
            float surface = terrain.SurfaceWorldYAtPixel(21.5f * 32f, 8.5f * 32f);
            Assert.Less(surface, LevelGeometry.WaterSurfaceY,
                "AI 在水格上取到的地表必须低于水面，使投掷模拟走落水分支");
        }

        [Test]
        public void DestroyedPlatformCell_BecomesWater()
        {
            TileTerrainGrid grid = Level1();

            // (24,5) 是大船甲板西缘，块高 2。
            Assert.AreEqual(2, grid.BlocksAt(24, 5));
            Assert.IsTrue(grid.DestroyBlock(24, 5));
            Assert.IsTrue(grid.DestroyBlock(24, 5));
            Assert.AreEqual(0, grid.BlocksAt(24, 5));

            Assert.IsFalse(grid.IsGroundAt(24, 5), "平台被炸空后该格应变成水");
            Assert.Less(grid.SurfaceWorldYAtWorld(24.5f, 5.5f), LevelGeometry.WaterSurfaceY);
        }

        [Test]
        public void LegacyColumnLevel_KeepsBaseGroundEverywhere()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(27, 21, 20);

            Assert.IsNotNull(grid);
            Assert.AreEqual(0, grid.ClusterCount, "旧列式关卡没有平台簇");
            Assert.AreEqual(0, grid.WaterCellCount, "旧列式关卡全图有基础地面（不挖洞）");
            Assert.IsTrue(grid.IsGroundAt(5, 0), "0 块列在旧模式仍是地面");
            Assert.AreEqual(LevelGeometry.GroundTopY, grid.SurfaceWorldY(5, 0), 1e-6f);
        }
    }
}
