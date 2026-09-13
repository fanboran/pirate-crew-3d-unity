using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 模块化构件管线（kit）的纯 C# 用例（无头可跑）。
    ///
    /// 【覆盖】
    ///   · 两艘不同尺寸的船配方都能展开出 艏/舯/艉 船体段 + 甲板 + 桅 + 索具 + 帆；
    ///   · level_1 的簇 → 配方映射（大船主簇 + 岛簇的岩唇/散岩/草顶），以及
    ///     **每簇的材质族纯净性**（用户裁决 4：船=木/布/铁，岛=岩/草，出生簇栏杆除外）；
    ///   · 确定性重建（同 seed 同摆放）；
    ///   · 构件预算（三角面 / 材质组数）在场景文档 §8 之内；
    ///   · 构件几何不抬高地表（所有构件 y ≥ 基础地面）。
    /// </summary>
    [TestFixture]
    public class SceneKitTests
    {
        [Test]
        public void TwoShipRecipes_DifferInSize()
        {
            ShipRecipe large = SceneKitCatalog.LargeShipRecipe;
            ShipRecipe small = SceneKitCatalog.SmallBoatRecipe;

            Assert.Greater(large.HullLength, small.HullLength, "大船应比小艇长");
            Assert.Greater(large.HullBeam, small.HullBeam, "大船应比小艇宽");
            Assert.Greater(large.MastHeight, small.MastHeight, "大船桅更高");
            Assert.Greater(large.MastCount, small.MastCount, "大船桅更多");
            Assert.Greater(large.CannonCount, small.CannonCount, "大船炮更多");
        }

        [Test]
        public void BuildShip_HasBowMidStern_Deck_Mast_Rigging_Sail()
        {
            List<KitPart> ship = SceneKitCatalog.BuildShip(
                SceneKitCatalog.LargeShipRecipe, Vector3.zero, 0f, 7);

            int bows = 0, mids = 0, sterns = 0, decks = 0, masts = 0, rigs = 0, sails = 0, hulls = 0;
            for (int i = 0; i < ship.Count; i++)
            {
                switch (ship[i].Piece)
                {
                    case SceneKitPiece.HullBow: bows++; break;
                    case SceneKitPiece.HullMid: mids++; break;
                    case SceneKitPiece.HullStern: sterns++; break;
                    case SceneKitPiece.DeckPlank: decks++; break;
                    case SceneKitPiece.Mast: masts++; break;
                    case SceneKitPiece.Rigging: rigs++; break;
                    case SceneKitPiece.Sail: sails++; break;
                    case SceneKitPiece.Bulwark: hulls++; break;
                }
            }

            Assert.AreEqual(1, bows, "艏构件 1 段");
            Assert.GreaterOrEqual(mids, 1, "舯构件至少 1 段");
            Assert.AreEqual(1, sterns, "艉构件 1 段");
            Assert.GreaterOrEqual(decks, 3, "甲板铺板应有多条");
            Assert.AreEqual(2, hulls, "两舷舷墙");
            Assert.AreEqual(SceneKitCatalog.LargeShipRecipe.MastCount, masts, "桅数 = 配方");
            Assert.GreaterOrEqual(rigs, 4, "每桅至少 4 根索具");
            Assert.GreaterOrEqual(sails, 1, "带帆配方应至少 1 面帆");
        }

        [Test]
        public void BuildLevel1_Deterministic()
        {
            SceneKitLayout a = SceneKitCatalog.BuildLevel1(1234);
            SceneKitLayout b = SceneKitCatalog.BuildLevel1(1234);

            Assert.AreEqual(a.Parts.Count, b.Parts.Count, "同 seed 应得同一条数");
            Assert.Greater(a.Parts.Count, 0);

            for (int i = 0; i < a.Parts.Count; i++)
            {
                Assert.AreEqual(a.Parts[i].Piece, b.Parts[i].Piece);
                Assert.AreEqual(a.Parts[i].Material, b.Parts[i].Material);
                Assert.AreEqual(a.Parts[i].Position.x, b.Parts[i].Position.x, 1e-6f);
                Assert.AreEqual(a.Parts[i].Position.y, b.Parts[i].Position.y, 1e-6f);
                Assert.AreEqual(a.Parts[i].Position.z, b.Parts[i].Position.z, 1e-6f);
            }
        }

        // ------------------------------------------------------------------
        // 构件族归并：同一艘船 / 同一座岛
        // ------------------------------------------------------------------

        /// <summary>
        /// level_1 的**船**判据（合并后）：
        /// 原版 tile 把一艘船画成互不相接的几块（左船体 / 左横桁 / 右船体 / 右横桁 / 桅盘）→ 5 个
        /// <see cref="PlatformClusterKind.Ship"/> 簇。合并后必须读成 **2 艘完整船**（不是 5 座散件船岛）：
        ///   · 每艘：1 个艏 + 1 个艉 + ≥1 个舯 + ≥1 桅 + 每桅 1 个桅顶巢 / 1 根横桁 / 1 面帆 + 1 个船艏装饰；
        ///   · 船体长度 = 原版船区的 X 跨度（构件沿长轴的外沿必须落在合并包络上）；
        ///   · 桅位来自原版桅区（左船 = 14 格宽的横桁区 → 2 桅，右船 = 6 格宽 → 1 桅）。
        /// </summary>
        /// <summary>
        /// 合成地图（不依赖关卡数据）验归并判据本身：
        /// 船体（最低行带）+ 横桁区（上方、X 覆盖船体）+ 桅盘（上方、X 相邻）→ **1 艘完整船**；
        /// 10 格外的另一段船体（X 间距 &gt; 2）绝不并进来。
        /// </summary>
        [Test]
        public void ShipGroups_SyntheticHullMastNest_MergeToOneCompleteShip()
        {
            const int w = 30, d = 5;
            var blocks = new int[w * d];
            var cluster = new int[w * d];
            for (int i = 0; i < cluster.Length; i++)
                cluster[i] = -1;

            // 船体 X0-9 / Z2-3、横桁区 X3-6 / Z0、桅盘 X7-8 / Z0、远处船体 X20-27 / Z2-3。
            StampRect(blocks, cluster, w, 0, 2, 9, 3, 0, 1);
            StampRect(blocks, cluster, w, 3, 0, 6, 0, 1, 1);
            StampRect(blocks, cluster, w, 7, 0, 8, 0, 2, 1);
            StampRect(blocks, cluster, w, 20, 2, 27, 3, 3, 1);

            var clusters = new List<PlatformClusterInfo>
            {
                new PlatformClusterInfo("hull", PlatformClusterKind.Ship, 0, 2, 9, 3, 1, 1),
                new PlatformClusterInfo("yard", PlatformClusterKind.Ship, 3, 0, 6, 0, 1, 1),
                new PlatformClusterInfo("nest", PlatformClusterKind.Ship, 7, 0, 8, 0, 1, 1),
                new PlatformClusterInfo("far_hull", PlatformClusterKind.Ship, 20, 2, 27, 3, 1, 1),
            };
            var map = new PlatformMap(w, d, blocks, cluster, clusters);

            IReadOnlyList<SceneKitGroup> groups = SceneKitCatalog.BuildGroups(map);
            Assert.AreEqual(2, groups.Count, "船体+横桁+桅盘并成 1 艘；远处船体自成 1 艘");
            Assert.AreEqual(2, SceneKitCatalog.CountGroups(groups, PlatformClusterKind.Ship));
            Assert.AreEqual(3, groups[0].Members.Count, "主船 3 个原版区");
            Assert.AreEqual(0, groups[0].X0);
            Assert.AreEqual(9, groups[0].X1, "合并包络 = 原版船区 X 跨度（含桅盘）");
            Assert.AreEqual(2, groups[0].MastClusters.Count, "横桁区 + 桅盘都识别为桅簇");

            SceneKitLayout kit = SceneKitCatalog.BuildFor(99, map, 5);
            Assert.AreEqual(10, groups[0].WidthTiles, "船体长度 = 原版船区 X 跨度");
            AssertShipHasCompleteSilhouette(kit, groups[0]);
            Assert.AreEqual(1, kit.CountInCluster(0, SceneKitPiece.Mast), "横桁区+桅盘共 6 格宽 → 1 桅");
            Assert.AreEqual(1, kit.CountInCluster(3, SceneKitPiece.Mast), "远处船体（无桅区）走配方默认桅数");
            Assert.AreEqual(0, kit.CountOf(SceneKitPiece.IslandTop), "没有岛簇 → 没有岩唇");
        }

        /// <summary>
        /// 合成地图验**岛轻合并**：两座相邻、同高差、Z 带重叠的空岛 → 并成 1 座岛（1 圈岩唇）；
        /// 高差不同 → 各留一圈（顶面不共面，谈不上"一座岛的顶面"）。
        /// </summary>
        [Test]
        public void IslandGroups_SyntheticAdjacentSameHeight_MergeToOneRim()
        {
            const int w = 4, d = 3;
            var blocks = new int[w * d];
            var cluster = new int[w * d];
            for (int gz = 0; gz < d; gz++)
            {
                for (int gx = 0; gx < w; gx++)
                {
                    blocks[gx + gz * w] = 2;
                    cluster[gx + gz * w] = gx < 2 ? 0 : 1;
                }
            }

            var sameHeight = new List<PlatformClusterInfo>
            {
                new PlatformClusterInfo("islet_west", PlatformClusterKind.SkyIsland, 0, 0, 1, d - 1, 2, 2),
                new PlatformClusterInfo("islet_east", PlatformClusterKind.SkyIsland, 2, 0, 3, d - 1, 2, 2),
            };
            var merged = new PlatformMap(w, d, blocks, cluster, sameHeight);

            IReadOnlyList<SceneKitGroup> mergedGroups = SceneKitCatalog.BuildGroups(merged);
            Assert.AreEqual(1, mergedGroups.Count, "相邻 + 同高差 → 一座岛");
            Assert.IsTrue(mergedGroups[0].IsMerged);
            Assert.AreEqual(0, mergedGroups[0].X0);
            Assert.AreEqual(3, mergedGroups[0].X1);

            SceneKitLayout mergedKit = SceneKitCatalog.BuildFor(98, merged, 5);
            Assert.AreEqual(1, mergedKit.CountOf(SceneKitPiece.IslandTop), "一座岛只做一圈岩唇（不重复）");
            Assert.AreEqual(6, mergedKit.CountOf(SceneKitPiece.RockChunk), "岛缘 6 块散岩");
            Assert.Greater(mergedKit.CountOf(SceneKitPiece.DeckPlank), 0, "空岛顶面铺草（DeckPlank + Foliage）");

            var differentHeight = new List<PlatformClusterInfo>
            {
                new PlatformClusterInfo("islet_west", PlatformClusterKind.SkyIsland, 0, 0, 1, d - 1, 2, 2),
                new PlatformClusterInfo("islet_east", PlatformClusterKind.SkyIsland, 2, 0, 3, d - 1, 1, 1),
            };
            var separate = new PlatformMap(w, d, blocks, cluster, differentHeight);

            Assert.AreEqual(2, SceneKitCatalog.BuildGroups(separate).Count, "高差不同 → 两座岛（各一圈岩唇）");
            Assert.AreEqual(2, SceneKitCatalog.BuildFor(97, separate, 5).CountOf(SceneKitPiece.IslandTop));
        }

        /// <summary>把 [x0,x1]×[z0,z1] 的矩形写进合成地图的块高 / 簇归属。</summary>
        static void StampRect(int[] blocks, int[] cluster, int width, int x0, int z0, int x1, int z1,
            int clusterIndex, int blockHeight)
        {
            for (int gz = z0; gz <= z1; gz++)
            {
                for (int gx = x0; gx <= x1; gx++)
                {
                    blocks[gx + gz * width] = blockHeight;
                    cluster[gx + gz * width] = clusterIndex;
                }
            }
        }

        [Test]
        public void BuildLevel1_MergesFiveShipIslandsIntoTwoCompleteShips()
        {
            PlatformMap map = PlatformClusterLayout.BuildLevel1();
            SceneKitLayout kit = SceneKitCatalog.BuildFor(1, map, 7);

            int shipClusters = 0;
            for (int c = 0; c < map.Clusters.Count; c++)
            {
                if (map.Clusters[c].Kind == PlatformClusterKind.Ship)
                    shipClusters++;
            }

            IReadOnlyList<SceneKitGroup> groups = SceneKitCatalog.BuildGroups(map);
            int shipGroups = SceneKitCatalog.CountGroups(groups, PlatformClusterKind.Ship);

            Assert.AreEqual(5, shipClusters, "原版 level_1 的行串给出 5 个船区（左船体/左横桁/右船体/右横桁/桅盘）");
            Assert.AreEqual(2, shipGroups, "5 个原版船区应并成 2 艘完整船（而非 5 座散件船岛）");
            Assert.Less(shipGroups, shipClusters, "必须真的发生了合并");

            int minWidth = int.MaxValue, maxWidth = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g].Kind != PlatformClusterKind.Ship)
                    continue;

                SceneKitGroup group = groups[g];
                Assert.IsTrue(group.IsMerged, "level_1 的每艘船都应由多个原版船区合成");

                AssertShipHasCompleteSilhouette(kit, group);

                float hullMinX, hullMaxX;
                HullSpanX(kit, group.PrimaryCluster, out hullMinX, out hullMaxX);
                Assert.AreEqual(group.X0, hullMinX, 0.05f,
                    "船体长度 = 原版船区 X 跨度（左端应贴住合并包络的左端）");
                Assert.AreEqual(group.X1 + 1, hullMaxX, 0.05f,
                    "船体长度 = 原版船区 X 跨度（右端应贴住合并包络的右端）");

                minWidth = Mathf.Min(minWidth, group.WidthTiles);
                maxWidth = Mathf.Max(maxWidth, group.WidthTiles);

                // 桅位落在甲板范围内（桅必须立在船上，不能悬在船外）。
                IReadOnlyList<float> masts = SceneKitCatalog.MastOffsetsFor(map, group, group.WidthTiles);
                Assert.GreaterOrEqual(masts.Count, 1, "每艘船至少 1 桅");
                for (int m = 0; m < masts.Count; m++)
                    Assert.LessOrEqual(Mathf.Abs(masts[m]), group.WidthTiles * 0.5f, "桅位必须在船体内");
            }

            Assert.GreaterOrEqual(minWidth, 8, "每艘都应是可读的整船（≥8 单位长）");
            Assert.GreaterOrEqual(maxWidth, 14, "至少一艘是 galleon 尺度（原版左船体 X 跨度 19）");
        }

        /// <summary>
        /// 全部已转写关的**整船剪影不变量**：每一艘合并船都必须有 艏/艉/舯 + 每桅配齐
        /// 桅顶瞭望巢 / 横桁 / 帆 + 一个船艏装饰 —— "一艘船该有的样子"由 kit 完整生成，
        /// 原版 tile 只决定哪里有船 / 多长 / 桅在哪。
        /// </summary>
        [Test]
        public void ShipGroups_AllTranscribedLevels_EveryShipHasCompleteSilhouette()
        {
            IReadOnlyList<int> levels = LevelCatalog.TranscribedLevelNumbers;
            int shipTotal = 0, mastTotal = 0;

            for (int i = 0; i < levels.Count; i++)
            {
                LevelData level = LevelCatalog.Get(levels[i]);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                if (map == null)
                    continue;

                SceneKitLayout kit = SceneKitCatalog.BuildFor(level.LevelNumber, map, level.LevelNumber * 1013 + 7);
                IReadOnlyList<SceneKitGroup> groups = SceneKitCatalog.BuildGroups(map);

                for (int g = 0; g < groups.Count; g++)
                {
                    if (groups[g].Kind != PlatformClusterKind.Ship)
                        continue;

                    AssertShipHasCompleteSilhouette(kit, groups[g]);
                    shipTotal++;

                    float hullMinX, hullMaxX;
                    HullSpanX(kit, groups[g].PrimaryCluster, out hullMinX, out hullMaxX);
                    Assert.AreEqual(groups[g].X0, hullMinX, 0.05f, "level_" + level.LevelNumber + " 船体左端");
                    Assert.AreEqual(groups[g].X1 + 1, hullMaxX, 0.05f, "level_" + level.LevelNumber + " 船体右端");

                    mastTotal += kit.CountInCluster(groups[g].PrimaryCluster, SceneKitPiece.Mast);
                }
            }

            TestContext.Progress.WriteLine("[合并船] 已转写关共 " + shipTotal + " 艘 / 桅 " + mastTotal + " 根");
            Assert.Greater(shipTotal, 0, "33 关里有若干关含船");
        }

        /// <summary>
        /// 台地 / 空岛轻合并：**每座岛只做一圈岛顶岩唇**（相邻同高差的小岛并成一座），
        /// 取消"每座碎岛各带一圈重复岩唇"。判据：岩唇条数 == 岛族数（全 33 关）。
        /// </summary>
        [Test]
        public void IslandTop_AllTranscribedLevels_OneRimPerIslandGroup()
        {
            IReadOnlyList<int> levels = LevelCatalog.TranscribedLevelNumbers;
            int mergedLevels = 0;

            for (int i = 0; i < levels.Count; i++)
            {
                LevelData level = LevelCatalog.Get(levels[i]);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                if (map == null)
                    continue;

                int islandClusters = 0;
                for (int c = 0; c < map.Clusters.Count; c++)
                {
                    if (map.Clusters[c].Kind != PlatformClusterKind.Ship)
                        islandClusters++;
                }

                IReadOnlyList<SceneKitGroup> groups = SceneKitCatalog.BuildGroups(map);
                int islandGroups = groups.Count - SceneKitCatalog.CountGroups(groups, PlatformClusterKind.Ship);

                SceneKitLayout kit = SceneKitCatalog.BuildFor(level.LevelNumber, map, level.LevelNumber * 1013 + 7);
                Assert.AreEqual(islandGroups, kit.CountOf(SceneKitPiece.IslandTop),
                    "level_" + level.LevelNumber + " 每座岛（族）只应有 1 圈岛顶岩唇");

                if (islandGroups < islandClusters)
                    mergedLevels++;
            }

            TestContext.Progress.WriteLine("[岛轻合并] 33 关里 " + mergedLevels + " 关的相邻同高差碎岛被并成整岛");
            Assert.Greater(mergedLevels, 0, "应有若干关真的发生了岛合并（否则这条判据没有约束力）");
        }

        /// <summary>
        /// 【锚点铁律】kit 是**纯表现层**：<see cref="SceneKitCatalog.BuildFor"/> 只读 <see cref="PlatformMap"/>，
        /// 不移动任何玩法锚点 ——
        ///   · map 逐格块高 / 簇归属 / 簇包络 / Kind / 基准高度在装配前后完全一致；
        ///   · 全 33 关的单位出生格仍是"有地面、区块高 &gt; 0"（水距 / 高度 / 单位落位不受合并影响）；
        ///   · 调度产出有限（无 NaN / 无 Inf），且只写网格缓冲（无 Collider：见 SceneKitComposer 类头）。
        /// </summary>
        [Test]
        public void BuildFor_IsPresentationOnly_AnchorsStayUntouched()
        {
            IReadOnlyList<int> levels = LevelCatalog.TranscribedLevelNumbers;

            for (int i = 0; i < levels.Count; i++)
            {
                LevelData level = LevelCatalog.Get(levels[i]);
                PlatformMap map = PlatformClusterLayout.BuildFor(level);
                if (map == null)
                    continue;

                // 快照
                int[] blocksBefore = (int[])map.CellBlocks.Clone();
                int[] clusterBefore = (int[])map.CellCluster.Clone();
                var boundsBefore = new int[map.Clusters.Count * 6];
                for (int c = 0; c < map.Clusters.Count; c++)
                {
                    PlatformClusterInfo info = map.Clusters[c];
                    boundsBefore[c * 6 + 0] = info.X0;
                    boundsBefore[c * 6 + 1] = info.Z0;
                    boundsBefore[c * 6 + 2] = info.X1;
                    boundsBefore[c * 6 + 3] = info.Z1;
                    boundsBefore[c * 6 + 4] = info.MaxTotalBlocks;
                    boundsBefore[c * 6 + 5] = (int)info.Kind;
                }

                SceneKitLayout kit = SceneKitCatalog.BuildFor(level.LevelNumber, map, level.LevelNumber * 1013 + 7);
                Assert.Greater(kit.Parts.Count, 0, "level_" + level.LevelNumber + " 应展开出构件");

                for (int k = 0; k < blocksBefore.Length; k++)
                {
                    Assert.AreEqual(blocksBefore[k], map.CellBlocks[k], "逐格块高被 kit 改动（违规）");
                    Assert.AreEqual(clusterBefore[k], map.CellCluster[k], "逐簇归属被 kit 改动（违规）");
                }

                for (int c = 0; c < map.Clusters.Count; c++)
                {
                    PlatformClusterInfo info = map.Clusters[c];
                    Assert.AreEqual(boundsBefore[c * 6 + 0], info.X0);
                    Assert.AreEqual(boundsBefore[c * 6 + 1], info.Z0);
                    Assert.AreEqual(boundsBefore[c * 6 + 2], info.X1);
                    Assert.AreEqual(boundsBefore[c * 6 + 3], info.Z1);
                    Assert.AreEqual(boundsBefore[c * 6 + 4], info.MaxTotalBlocks, "簇高度被 kit 改动（违规）");
                    Assert.AreEqual(boundsBefore[c * 6 + 5], (int)info.Kind, "簇 Kind 被 kit 改动（违规）");
                }

                // 水距锚点：单位仍站在原版地面格上（合并是否发生都不影响这一条）。
                IReadOnlyList<LevelUnit> units = level.Units;
                if (units != null)
                {
                    for (int u = 0; u < units.Count; u++)
                    {
                        LevelUnit unit = units[u];
                        Assert.Greater(map.TotalBlocksAt(unit.gridX, unit.gridY), 0,
                            "level_" + level.LevelNumber + " 单位 (" + unit.gridX + "," + unit.gridY
                            + ") 落到了水上（锚点被破坏）");
                    }
                }
            }

            // 调度产出有限（合并船的尺寸推导若写出 NaN/Inf，这里会抓到）。
            var buffers = new ScenePropBuffers();
            SceneKitComposer.Compose(buffers, SceneKitCatalog.BuildFor(1, PlatformClusterLayout.BuildLevel1(), 7), 7);
            Vector3[] vertices = buffers.Wood.ToVertices();
            Assert.Greater(vertices.Length, 0);
            for (int v = 0; v < vertices.Length; v++)
            {
                Assert.IsFalse(float.IsNaN(vertices[v].x) || float.IsNaN(vertices[v].y) || float.IsNaN(vertices[v].z),
                    "木组顶点出现 NaN（尺寸推导出错）");
                Assert.IsFalse(float.IsInfinity(vertices[v].x) || float.IsInfinity(vertices[v].y)
                    || float.IsInfinity(vertices[v].z), "木组顶点出现 Inf（尺寸推导出错）");
            }
        }

        /// <summary>一艘船的"完整剪影"判据（艏/艉/舯 + 每桅配齐桅顶巢 / 横桁 / 帆 + 船艏装饰 + 两舷舷墙）。</summary>
        static void AssertShipHasCompleteSilhouette(SceneKitLayout kit, in SceneKitGroup group)
        {
            int ci = group.PrimaryCluster;

            Assert.AreEqual(1, kit.CountInCluster(ci, SceneKitPiece.HullBow), "整船应有 1 个艏构件");
            Assert.AreEqual(1, kit.CountInCluster(ci, SceneKitPiece.HullStern), "整船应有 1 个艉构件");
            Assert.GreaterOrEqual(kit.CountInCluster(ci, SceneKitPiece.HullMid), 1, "整船应有舯段");

            int masts = kit.CountInCluster(ci, SceneKitPiece.Mast);
            Assert.GreaterOrEqual(masts, 1, "整船至少 1 桅");
            Assert.AreEqual(masts, kit.CountInCluster(ci, SceneKitPiece.CrowNest), "每桅一个桅顶瞭望巢");
            Assert.AreEqual(masts, kit.CountInCluster(ci, SceneKitPiece.Yard), "每桅一根横桁");
            Assert.AreEqual(masts, kit.CountInCluster(ci, SceneKitPiece.Sail), "每桅一面帆（两套配方都带帆）");
            Assert.AreEqual(1, kit.CountInCluster(ci, SceneKitPiece.BowDeco), "整船应有 1 个船艏装饰");
            Assert.GreaterOrEqual(kit.CountInCluster(ci, SceneKitPiece.Bulwark), 2,
                "两舷舷墙（出生簇另有 1 条出生栏杆）");
            Assert.GreaterOrEqual(kit.CountInCluster(ci, SceneKitPiece.Rigging), masts * 4, "每桅 4 根索具");

            // 船体段必须**连贯**（艏/舯/艉首尾相接，不能有缝）—— "一艘完整船"的最低要求。
            var segments = new List<KitPart>();
            for (int i = 0; i < kit.Parts.Count; i++)
            {
                KitPart part = kit.Parts[i];
                if (part.ClusterIndex != ci)
                    continue;
                if (part.Piece == SceneKitPiece.HullBow || part.Piece == SceneKitPiece.HullMid
                    || part.Piece == SceneKitPiece.HullStern)
                    segments.Add(part);
            }

            segments.Sort((a, b) => a.Position.x.CompareTo(b.Position.x));
            for (int i = 1; i < segments.Count; i++)
            {
                float previousRight = segments[i - 1].Position.x + segments[i - 1].Length * 0.5f;
                float currentLeft = segments[i].Position.x - segments[i].Length * 0.5f;
                Assert.LessOrEqual(Mathf.Abs(currentLeft - previousRight), 0.01f,
                    "船体段之间不应有缝（艏/舯/艉必须首尾相接）");
            }
        }

        /// <summary>船体构件沿长轴（yaw = 0 → X 轴）的外沿范围——应与合并包络逐值贴合。</summary>
        static void HullSpanX(SceneKitLayout kit, int clusterIndex, out float minX, out float maxX)
        {
            minX = float.PositiveInfinity;
            maxX = float.NegativeInfinity;

            for (int i = 0; i < kit.Parts.Count; i++)
            {
                KitPart part = kit.Parts[i];
                if (part.ClusterIndex != clusterIndex)
                    continue;
                if (part.Piece != SceneKitPiece.HullBow && part.Piece != SceneKitPiece.HullMid
                    && part.Piece != SceneKitPiece.HullStern)
                    continue;

                minX = Mathf.Min(minX, part.Position.x - part.Length * 0.5f);
                maxX = Mathf.Max(maxX, part.Position.x + part.Length * 0.5f);
            }
        }

        [Test]
        public void BuildLevel1_UsesLargeShip_AndKeepsIslandsWoodFree()
        {
            // 【2026-09-14 起以原版 tile 地图为准】原版 level_1 的行串给出 5 座船岛
            // （左船体 / 右船体 / 左横桁 / 右横桁 / 桅盘）+ 4 座岛；旧手写布局的"1 艘大船 + 3 岛"
            // 已随手写定义一起删除。判据改为**按 Kind 的不变量**（而不是拍死的构件条数）：
            //   · 桅 / 船体构件只出现在 Ship 簇上；Terrace 簇一个木构件都没有；
            //   · 5 个船区经构件族合并成 2 艘完整船（见 BuildLevel1_MergesFiveShipIslandsIntoTwoCompleteShips）。
            PlatformMap map = PlatformClusterLayout.BuildLevel1();
            SceneKitLayout kit = SceneKitCatalog.BuildFor(1, map, 7);

            int masts = 0;
            int terraceMasts = 0;
            int bowOnTerrace = 0;

            for (int i = 0; i < kit.Parts.Count; i++)
            {
                KitPart part = kit.Parts[i];
                if (part.ClusterIndex < 0 || part.ClusterIndex >= map.Clusters.Count)
                    continue;

                PlatformClusterKind kind = map.Clusters[part.ClusterIndex].Kind;

                if (part.Piece == SceneKitPiece.Mast)
                {
                    masts++;
                    if (kind != PlatformClusterKind.Ship)
                        terraceMasts++;
                }

                if (part.Piece == SceneKitPiece.HullBow && kind != PlatformClusterKind.Ship)
                    bowOnTerrace++;
            }

            Assert.AreEqual(0, terraceMasts, "桅只能出现在 Ship 簇上（岛簇不摆船）");
            Assert.AreEqual(0, bowOnTerrace, "船体构件只能出现在 Ship 簇上");
            Assert.AreEqual(3, masts, "左船 2 桅（14 格宽的横桁区）+ 右船 1 桅 = 3 桅");

            // 岛族：level_1 的 4 座岛彼此不同高差 / X 间距 >2，保持 4 座（各一圈岩唇）。
            IReadOnlyList<SceneKitGroup> level1Groups = SceneKitCatalog.BuildGroups(map);
            Assert.AreEqual(4, level1Groups.Count - SceneKitCatalog.CountGroups(level1Groups, PlatformClusterKind.Ship),
                "level_1 的 4 座岛不合并（不同高差 + X 间距 >2）");

            // 岛簇要有岛顶岩唇与散岩。
            Assert.GreaterOrEqual(kit.CountOf(SceneKitPiece.IslandTop), 4, "4 座岛簇各有岛顶岩唇");
            Assert.GreaterOrEqual(kit.CountOf(SceneKitPiece.RockChunk), 6, "岛缘应有散落岩块");
            Assert.GreaterOrEqual(kit.CountOf(SceneKitPiece.Prop), 1, "应有陈设构件");
        }

        /// <summary>
        /// 用户裁决 4「按簇 Kind 严格选材质族」的程序化判据：
        /// 每个簇包络内的构件材质必须落在 <see cref="SceneKitCatalog.MaterialFamilyFor"/> 白名单里；
        /// 唯一的跨族例外是**出生簇的栏杆**（Bulwark + Wood，用户裁决明写"出生岛 = 沙台地 + 栏杆"）。
        /// </summary>
        [Test]
        public void BuildFor_EveryCluster_KeepsItsMaterialFamily()
        {
            var maps = new List<PlatformMap>
            {
                PlatformClusterLayout.BuildLevel1(),
                PlatformClusterLayout.BuildFor(LevelCatalog.Get(4)),
                PlatformClusterLayout.BuildFor(LevelCatalog.Get(27)),
            };

            for (int m = 0; m < maps.Count; m++)
            {
                PlatformMap map = maps[m];
                SceneKitLayout kit = SceneKitCatalog.BuildFor(m + 1, map, 1013 + m);

                for (int p = 0; p < kit.Parts.Count; p++)
                {
                    KitPart part = kit.Parts[p];
                    if (part.ClusterIndex < 0 || part.ClusterIndex >= map.Clusters.Count)
                        continue;   // 未标注簇（裸配方用法）：不参与纯净性断言

                    PlatformClusterInfo info = map.Clusters[part.ClusterIndex];
                    IReadOnlyList<SceneKitMaterial> allowed = SceneKitCatalog.MaterialFamilyFor(info.Kind);
                    if (ContainsMaterial(allowed, part.Material))
                        continue;

                    bool railingException = part.Piece == SceneKitPiece.Bulwark
                        && part.Material == SceneKitMaterial.Wood && info.IsSpawnCluster;

                    Assert.IsTrue(railingException,
                        info.Name + "（" + info.Kind + "）出现跨族材质 " + part.Material
                        + " @ " + part.Piece + "：" + part.Position);
                }
            }
        }

        static bool ContainsMaterial(IReadOnlyList<SceneKitMaterial> list, SceneKitMaterial material)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == material)
                    return true;
            }
            return false;
        }

        [Test]
        public void KitComposition_TotalTriangles_UnderSceneBudget()
        {
            var buffers = new ScenePropBuffers();
            SceneKitComposer.Compose(buffers, SceneKitCatalog.BuildLevel1(7), 7);

            TestContext.Progress.WriteLine("[kit 三角面] 合计 " + buffers.TotalTriangles
                + "（木 " + buffers.Wood.TriangleCount + " / 暗木 " + buffers.WoodDark.TriangleCount
                + " / 岩 " + buffers.Rock.TriangleCount + " / 铁 " + buffers.Metal.TriangleCount
                + " / 布 " + buffers.Cloth.TriangleCount + " / 植被 " + buffers.Foliage.TriangleCount + "）");

            Assert.Greater(buffers.TotalTriangles, 0);
            Assert.Less(buffers.TotalTriangles, 60000, "kit 可见三角面应远低于场景 §8 的 150k 预算");
        }

        [Test]
        public void KitComposition_IsDeterministic()
        {
            var a = new ScenePropBuffers();
            var b = new ScenePropBuffers();
            SceneKitComposer.Compose(a, SceneKitCatalog.BuildLevel1(7), 7);
            SceneKitComposer.Compose(b, SceneKitCatalog.BuildLevel1(7), 7);

            Assert.AreEqual(a.TotalTriangles, b.TotalTriangles, "同 seed 应得同三角面数");
            Assert.AreEqual(a.Wood.VertexCount, b.Wood.VertexCount);
            Assert.AreEqual(a.Rock.VertexCount, b.Rock.VertexCount);
            Assert.AreEqual(a.WoodDark.VertexCount, b.WoodDark.VertexCount);
        }

        [Test]
        public void KitMaterialGroups_AreBounded()
        {
            SceneKitLayout kit = SceneKitCatalog.BuildLevel1(7);
            var used = new HashSet<SceneKitMaterial>();
            for (int i = 0; i < kit.Parts.Count; i++)
                used.Add(kit.Parts[i].Material);

            Assert.LessOrEqual(used.Count, 6, "构件材质组不得超过注册表定义的 6 组");
            Assert.GreaterOrEqual(used.Count, 3, "至少用到木/岩/植被三类");
        }

        [Test]
        public void KitParts_NeverSinkBelowBaseGround()
        {
            SceneKitLayout kit = SceneKitCatalog.BuildLevel1(7);

            for (int i = 0; i < kit.Parts.Count; i++)
            {
                KitPart p = kit.Parts[i];
                Assert.GreaterOrEqual(p.Position.y, LevelGeometry.GroundTopY - 1e-4f,
                    p.Piece + " 的摆放点不得低于基础地面");
                // 桅/帆/索具会高于平台顶面（那是正常的高物），但仍应在合理高度内。
                Assert.LessOrEqual(p.Position.y, LevelGeometry.GroundTopY + 8f,
                    p.Piece + " 的摆放点高度异常");
            }
        }

        [Test]
        public void ShipGeometry_RespectsSceneDocTriangleBudget()
        {
            var buffers = new ScenePropBuffers();
            var ship = SceneKitCatalog.BuildShip(SceneKitCatalog.LargeShipRecipe, Vector3.zero, 0f, 3);
            var layout = new SceneKitLayout();
            // 用最小调度壳把单船构件展开。
            for (int i = 0; i < ship.Count; i++)
                layout.Add(ship[i]);
            SceneKitComposer.Compose(buffers, layout, 3);

            int shipTris = buffers.Wood.TriangleCount + buffers.WoodDark.TriangleCount
                + buffers.Cloth.TriangleCount + buffers.Metal.TriangleCount;

            TestContext.Progress.WriteLine("[大船 kit 三角面] " + shipTris);
            Assert.Greater(shipTris, 300, "船体细节过少会读成盒子");
            Assert.Less(shipTris, 8000, "单船三角面应受控（§4.1 预算 3000-5000，含构件叠加放宽到 8000）");
        }
    }
}
