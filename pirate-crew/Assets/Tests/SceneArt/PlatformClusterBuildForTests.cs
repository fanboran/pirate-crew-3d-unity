using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 平台簇布局 / 场景 kit 的**通用化**冒烟用例（纯 C#，无头可跑）。
    ///
    /// 【覆盖】
    ///   · level_1 等价性：<c>BuildFor(level_1)</c> 与手写的 <c>BuildLevel1()</c> 逐簇一致
    ///     （簇数 / 名字 / Kind / 包络 / 块高）——保证既有测试与烘焙不被通用化改写破坏；
    ///   · 全部已转写关（level_1/4/27）都能推导出**有效布局**：出生位落在簇内且在地面上、
    ///     水距 ≥1 且不超过 R3 上限、块高在 <see cref="PlatformClusterLayout.MaxBlocksPerCluster"/> 内；
    ///   · 确定性：同一 LevelData 两次推导逐格相同；
    ///   · 合成 33 关（不依赖 Data 域的补全进度）全跑一遍，验证推导对任意尺寸/出生位都不崩。
    /// </summary>
    [TestFixture]
    public class PlatformClusterBuildForTests
    {
        // ------------------------------------------------------------------
        // level_1 等价性（既有行为不回归）
        // ------------------------------------------------------------------

        [Test]
        public void BuildFor_Level1_IsEquivalentToBuildLevel1()
        {
            PlatformMap authored = PlatformClusterLayout.BuildLevel1();
            PlatformMap generic = PlatformClusterLayout.BuildFor(LevelCatalog.Get(1));

            Assert.AreEqual(authored.WidthTiles, generic.WidthTiles);
            Assert.AreEqual(authored.DepthTiles, generic.DepthTiles);
            Assert.AreEqual(authored.Clusters.Count, generic.Clusters.Count, "簇数应一致");

            for (int c = 0; c < authored.Clusters.Count; c++)
            {
                PlatformClusterInfo a = authored.Clusters[c];
                PlatformClusterInfo b = generic.Clusters[c];

                Assert.AreEqual(a.Name, b.Name, "簇 " + c + " 名字");
                Assert.AreEqual(a.Kind, b.Kind, "簇 " + c + " 类型");
                Assert.AreEqual(a.X0, b.X0); Assert.AreEqual(a.Z0, b.Z0);
                Assert.AreEqual(a.X1, b.X1); Assert.AreEqual(a.Z1, b.Z1);
                Assert.AreEqual(a.MinBlocks, b.MinBlocks);
                Assert.AreEqual(a.MaxBlocks, b.MaxBlocks);
                Assert.AreEqual(a.SpawnTeamMask, b.SpawnTeamMask);
            }

            Assert.AreEqual(authored.GroundCellCount, generic.GroundCellCount, "地面格数应一致");
            Assert.AreEqual(authored.WaterCellCount, generic.WaterCellCount, "水格数应一致");
        }

        [Test]
        public void BuildFor_Level1_MarksSpawnClusters()
        {
            IReadOnlyList<PlatformClusterInfo> clusters = PlatformClusterLayout.BuildLevel1().Clusters;

            PlatformClusterInfo red = clusters[0];
            PlatformClusterInfo blue = clusters[2];

            Assert.IsTrue(red.IsSpawnCluster, "① 西梯田岛应是红队出生簇");
            Assert.IsTrue(red.SpawnsTeam(0));
            Assert.IsFalse(red.SpawnsTeam(1));

            Assert.IsTrue(blue.IsSpawnCluster, "③ 东空岛应是蓝队出生簇");
            Assert.IsTrue(blue.SpawnsTeam(1));
            Assert.IsFalse(blue.SpawnsTeam(0));

            Assert.IsFalse(clusters[1].IsSpawnCluster, "② 中央大船是中立簇");
        }

        [Test]
        public void SceneKit_BuildFor_Level1_EqualsBuildLevel1()
        {
            SceneKitLayout viaDelegate = SceneKitCatalog.BuildLevel1(7);
            SceneKitLayout viaGeneric = SceneKitCatalog.BuildFor(1, PlatformClusterLayout.BuildLevel1(), 7);

            Assert.AreEqual(viaDelegate.Parts.Count, viaGeneric.Parts.Count, "构件条数应一致");

            for (int i = 0; i < viaDelegate.Parts.Count; i++)
            {
                Assert.AreEqual(viaDelegate.Parts[i].Piece, viaGeneric.Parts[i].Piece);
                Assert.AreEqual(viaDelegate.Parts[i].Material, viaGeneric.Parts[i].Material);
                Assert.AreEqual(viaDelegate.Parts[i].Position.y, viaGeneric.Parts[i].Position.y, 1e-6f);
            }
        }

        // ------------------------------------------------------------------
        // 已转写关：有效布局
        // ------------------------------------------------------------------

        [Test]
        public void BuildFor_Level4_ProducesValidLayout()
        {
            AssertValidLayout(LevelCatalog.Get(4));
        }

        [Test]
        public void BuildFor_Level27_ProducesValidLayout()
        {
            AssertValidLayout(LevelCatalog.Get(27));
        }

        [Test]
        public void BuildFor_AllTranscribedLevels_SpawnsAreOnGroundInsideTheirCluster()
        {
            IReadOnlyList<int> levels = LevelCatalog.TranscribedLevelNumbers;
            Assert.Greater(levels.Count, 0);

            for (int i = 0; i < levels.Count; i++)
                AssertValidLayout(LevelCatalog.Get(levels[i]));
        }

        [Test]
        public void BuildFor_AllTranscribedLevels_WaterGapsRespectRule()
        {
            IReadOnlyList<int> levels = LevelCatalog.TranscribedLevelNumbers;
            for (int i = 0; i < levels.Count; i++)
            {
                LevelData level = LevelCatalog.Get(levels[i]);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                Assert.IsNotNull(map, "level_" + level.LevelNumber + " 应能推导出布局");

                // R3/R16 的可玩性判据：两个出生簇之间**存在一条每段水距 ≤ 上限的链**
                // （投掷可跨）。用并查集按"水距 ≤ 上限"连边，再断言出生簇同属一个连通分量。
                // 这比"逐对簇都 ≤ 上限"更贴近规则本意——手写 level_1 的 ①↔③ 相距 23 格，
                // 但中间隔着 ② 大船，不属于"要跨的水"。
                int cap = PlatformClusterLayout.MaxWaterGapFor(level);

                var spawnClusters = new List<int>();
                for (int u = 0; u < level.Units.Count; u++)
                {
                    PlatformClusterInfo c = map.ClusterAt(level.Units[u].gridX, level.Units[u].gridY);
                    int index = IndexOfCluster(map, c);
                    Assert.GreaterOrEqual(index, 0, "出生位应落在某个簇内");
                    if (!spawnClusters.Contains(index))
                        spawnClusters.Add(index);
                }

                var parent = new int[map.Clusters.Count];
                for (int c = 0; c < parent.Length; c++)
                    parent[c] = c;

                for (int a = 0; a < map.Clusters.Count; a++)
                {
                    for (int b = a + 1; b < map.Clusters.Count; b++)
                    {
                        if (PlatformClusterLayout.WaterGapTiles(map.Clusters[a], map.Clusters[b]) <= cap)
                            Union(parent, a, b);
                    }
                }

                int root = Find(parent, spawnClusters[0]);
                for (int s = 1; s < spawnClusters.Count; s++)
                {
                    Assert.AreEqual(root, Find(parent, spawnClusters[s]),
                        "level_" + level.LevelNumber + " 的出生簇不连通（水距上限 " + cap + "）："
                        + map.Clusters[spawnClusters[0]].Name + " 与 "
                        + map.Clusters[spawnClusters[s]].Name + " 之间没有可跨水链");
                }
            }
        }

        static int IndexOfCluster(PlatformMap map, in PlatformClusterInfo info)
        {
            for (int i = 0; i < map.Clusters.Count; i++)
            {
                PlatformClusterInfo c = map.Clusters[i];
                if (c.Name == info.Name && c.X0 == info.X0 && c.Z0 == info.Z0
                    && c.SpawnTeamMask == info.SpawnTeamMask)
                    return i;
            }
            return -1;
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
                parent[rb] = ra;
        }

        [Test]
        public void BuildFor_Level4_MarksRedAndBlueSpawnClusters()
        {
            LevelData level = LevelCatalog.Get(4);
            PlatformMap map = PlatformClusterLayout.BuildFor(level);
            AssertSpawnsOnGround(level, map, expectSpawnMask: true);
        }

        [Test]
        public void SceneKit_BuildFor_AllTranscribedLevels_ProducesPartsWithinGroups()
        {
            IReadOnlyList<int> levels = LevelCatalog.TranscribedLevelNumbers;
            for (int i = 0; i < levels.Count; i++)
            {
                LevelData level = LevelCatalog.Get(levels[i]);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                SceneKitLayout kit = SceneKitCatalog.BuildFor(level.LevelNumber, map, level.LevelNumber * 1013 + 7);

                Assert.Greater(kit.Parts.Count, 0, "level_" + level.LevelNumber + " 应展开出构件");

                var used = new HashSet<SceneKitMaterial>();
                for (int p = 0; p < kit.Parts.Count; p++)
                {
                    used.Add(kit.Parts[p].Material);
                    Assert.GreaterOrEqual(kit.Parts[p].Position.y, LevelGeometry.GroundTopY - 1e-4f,
                        "构件不得低于基础地面");
                }
                Assert.LessOrEqual(used.Count, 6, "构件材质组不得超过注册表的 6 组");
            }
        }

        [Test]
        public void SceneArt_Composition_GenericLevels_UnderSceneTriangleBudget()
        {
            // 场景文档 §8 的可见三角面预算：150k。这里对"小图 / 中图 / 大图"各跑一遍
            // 道具 + 构件 + 平台底部的完整合成（运行时 RuntimeSceneArt 的同一份调度）。
            int[] levels = { 1, 4, 27, 22 };

            for (int i = 0; i < levels.Length; i++)
            {
                LevelData level = LevelCatalog.Get(levels[i]);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                var grid = new TileTerrainGrid(level.WidthTiles, level.HeightTiles, null,
                    TerrainCatalog.DefaultBlockWorldHeight, map);

                var buffers = new ScenePropBuffers();
                int seed = level.LevelNumber * 1013 + 7;

                SceneLayout props = ScenePropLayout.Build(grid, null, seed);
                ScenePropComposer.Compose(buffers, props, seed, level.HeightTiles);
                SceneKitComposer.Compose(buffers, SceneKitCatalog.BuildFor(level.LevelNumber, map, seed + 500), seed + 500);
                IslandShellGeometry.AddPlatformUndersides(buffers, grid, IslandShellSettings.Default);
                IslandShellGeometry.AddDashedBorder(buffers.Danger, level.WidthTiles, level.HeightTiles,
                    3.15f, -0.15f + 0.012f, 0.9f, 0.55f, 0.12f);

                TestContext.Progress.WriteLine("[scene art] level_" + level.LevelNumber
                    + " 三角面 " + buffers.TotalTriangles + "（props " + props.Props.Count
                    + " / 簇 " + map.Clusters.Count + "）");

                Assert.Greater(buffers.TotalTriangles, 0);
                Assert.Less(buffers.TotalTriangles, 150000,
                    "level_" + level.LevelNumber + " 的静态陈设三角面应低于场景文档 §8 的 150k 预算");
            }
        }

        [Test]
        public void BuildFor_Deterministic()
        {
            LevelData level = LevelCatalog.Get(4);
            PlatformMap a = PlatformClusterLayout.BuildFor(level);
            PlatformMap b = PlatformClusterLayout.BuildFor(level);

            Assert.AreEqual(a.Clusters.Count, b.Clusters.Count);
            Assert.AreEqual(a.CellBlocks.Length, b.CellBlocks.Length);
            for (int i = 0; i < a.CellBlocks.Length; i++)
            {
                Assert.AreEqual(a.CellBlocks[i], b.CellBlocks[i], "格 " + i + " 块高应确定");
                Assert.AreEqual(a.CellCluster[i], b.CellCluster[i], "格 " + i + " 归属应确定");
            }
        }

        [Test]
        public void CrossingWeaponRule_DrivesWaterGapCap()
        {
            LevelData level1 = LevelCatalog.Get(1);
            LevelData level4 = LevelCatalog.Get(4);

            Assert.IsFalse(PlatformClusterLayout.HasCrossingWeapon(level1), "level_1 空投池无越水武器");
            Assert.IsTrue(PlatformClusterLayout.HasCrossingWeapon(level4), "level_4 空投池含越水武器（seagull）");

            // 前 5 关是教学期（R18）：水距上限收紧到 4，与武器池无关。
            Assert.AreEqual(4, PlatformClusterLayout.MaxWaterGapFor(level1));
            Assert.AreEqual(4, PlatformClusterLayout.MaxWaterGapFor(level4));
        }

        // ------------------------------------------------------------------
        // 合成 33 关：任意尺寸 / 出生位都不崩，且出生位总是在簇内的地面上
        // ------------------------------------------------------------------

        [Test]
        public void BuildFor_SyntheticAllLevelNumbers_IsRobust()
        {
            for (int n = 2; n <= LevelCatalog.TotalLevels; n++)
            {
                LevelData level = SyntheticLevel(n);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);

                Assert.IsNotNull(map, "合成 level_" + n + " 应能推导出布局");
                Assert.AreEqual(level.WidthTiles, map.WidthTiles);
                Assert.AreEqual(level.HeightTiles, map.DepthTiles);
                Assert.GreaterOrEqual(map.Clusters.Count, 1, "合成 level_" + n + " 至少 1 簇");
                Assert.Greater(map.GroundCellCount, 0, "合成 level_" + n + " 必须有可站地面");

                AssertSpawnsOnGround(level, map, expectSpawnMask: true);
                AssertBlocksWithinCap(map);
            }
        }

        [Test]
        public void BuildFor_SchemaVariants_DoNotThrow()
        {
            // 极小图 / 单队 / 双方贴边 / 双方重叠 —— 通用推导的边界形态。
            var variants = new List<LevelData>
            {
                SyntheticLevel(200, 8, 6, redX: 1, blueX: 6),
                SyntheticLevel(201, 20, 8, redX: 2, blueX: 3),      // 双方重叠 → 单簇
                SyntheticLevel(202, 40, 14, redX: 1, blueX: 1),     // 双方同列
                SingleTeamLevel(203, 30, 12),
            };

            for (int i = 0; i < variants.Count; i++)
            {
                LevelData level = variants[i];
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                Assert.IsNotNull(map, "变体 " + level.LevelNumber + " 应能推导出布局");
                Assert.Greater(map.GroundCellCount, 0, "变体 " + level.LevelNumber + " 必须有地面");
                AssertSpawnsOnGround(level, map, expectSpawnMask: true);
                AssertBlocksWithinCap(map);
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static void AssertValidLayout(LevelData level)
        {
            // level_1 是手写定义：红队的 2 个单位按原版设计站在中立大船簇上，故不断言"出生簇掩码"。
            AssertValidLayout(level, expectSpawnMask: level.LevelNumber != 1);
        }

        static void AssertValidLayout(LevelData level, bool expectSpawnMask)
        {
            PlatformMap map = PlatformClusterLayout.BuildFor(level);
            Assert.IsNotNull(map, "level_" + level.LevelNumber + " 应能推导出布局");
            Assert.AreEqual(level.WidthTiles, map.WidthTiles, "地图宽应 = 关卡宽");
            Assert.AreEqual(level.HeightTiles, map.DepthTiles, "地图深应 = 关卡深");
            Assert.GreaterOrEqual(map.Clusters.Count, 1, "至少 1 簇");
            Assert.Greater(map.WaterCellCount, 0, "必须有水（平台之间隔水）");
            Assert.Greater(map.GroundCellCount, 0, "必须有可站地面");

            AssertSpawnsOnGround(level, map, expectSpawnMask);
            AssertBlocksWithinCap(map);
        }

        static void AssertSpawnsOnGround(LevelData level, PlatformMap map, bool expectSpawnMask)
        {
            IReadOnlyList<LevelUnit> units = level.Units;
            for (int i = 0; i < units.Count; i++)
            {
                LevelUnit u = units[i];
                Assert.IsTrue(map.IsGround(u.gridX, u.gridY),
                    "level_" + level.LevelNumber + " 的出生位 (" + u.gridX + "," + u.gridY + ") 落水了");

                PlatformClusterInfo cluster = map.ClusterAt(u.gridX, u.gridY);
                Assert.IsFalse(string.IsNullOrEmpty(cluster.Name),
                    "level_" + level.LevelNumber + " 出生位不在任何簇内");
                Assert.IsTrue(cluster.Contains(u.gridX, u.gridY));
                if (expectSpawnMask)
                {
                    Assert.IsTrue(cluster.SpawnsTeam(u.teamIndex),
                        "level_" + level.LevelNumber + " 出生位所在的簇应标记为队 " + u.teamIndex + " 的出生簇");
                }
            }
        }

        static void AssertBlocksWithinCap(PlatformMap map)
        {
            for (int i = 0; i < map.CellBlocks.Length; i++)
            {
                if (map.CellCluster[i] < 0)
                {
                    Assert.AreEqual(0, map.CellBlocks[i], "水格块高应为 0");
                    continue;
                }

                Assert.GreaterOrEqual(map.CellBlocks[i], 1, "地面格块高应 ≥1");
                Assert.LessOrEqual(map.CellBlocks[i], PlatformClusterLayout.MaxBlocksPerCluster,
                    "块高不得超过 MaxBlocksPerCluster");
            }
        }

        /// <summary>合成一关（尺寸随关卡号确定性地变化，覆盖小/中/大图）。</summary>
        static LevelData SyntheticLevel(int levelNumber, int width = 0, int depth = 0,
            int redX = -1, int blueX = -1)
        {
            if (width <= 0)
                width = 20 + (levelNumber * 7) % 60;     // 20–79 宽
            if (depth <= 0)
                depth = 10 + (levelNumber * 5) % 18;     // 10–27 深

            if (redX < 0)
                redX = Mathf.Clamp(width / 5, 0, width - 1);
            if (blueX < 0)
                blueX = Mathf.Clamp(width - width / 5 - 1, 0, width - 1);

            int redZ = depth / 2;
            int blueZ = Mathf.Clamp(depth / 2 + 2, 0, depth - 1);

            var units = new List<LevelUnit>
            {
                new LevelUnit("redPirate", 0, redX, redZ, 5, null),
                new LevelUnit("redPirate", 0, Mathf.Min(width - 1, redX + 2), Mathf.Max(0, redZ - 2), 5, null),
                new LevelUnit("soldier", 1, blueX, blueZ, 5, null),
                new LevelUnit("soldier", 1, Mathf.Max(0, blueX - 2), Mathf.Min(depth - 1, blueZ + 2), 5, null),
            };

            var weapons = new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) };
            return new LevelData(levelNumber, "synthetic_" + levelNumber, width, depth, 1,
                depth - 1, LevelCatalog.DefaultMaxChests, 1, weapons, units);
        }

        static LevelData SingleTeamLevel(int levelNumber, int width, int depth)
        {
            var units = new List<LevelUnit>
            {
                new LevelUnit("redPirate", 0, width / 3, depth / 2, 5, null),
                new LevelUnit("redPirate", 0, width / 3 + 3, depth / 2 - 1, 5, null),
            };
            var weapons = new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) };
            return new LevelData(levelNumber, "single_team", width, depth, 1,
                depth - 1, LevelCatalog.DefaultMaxChests, 1, weapons, units);
        }
    }
}
