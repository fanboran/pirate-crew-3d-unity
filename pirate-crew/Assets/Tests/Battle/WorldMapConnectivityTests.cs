using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// M4 大海域世界地图的硬约束校验（docs/大海域世界化.md §2.2/§4.2）：
    /// 对目录里全部海图断言——连通性（承载出生点的站面 box 同连通分量）、
    /// 出生点落位（在站面上、离边有余量、脚下无更高覆盖）、栅格化有效。
    /// 这是「地图各地方必须能来回跳过去」这一用户裁决的测试化。
    ///
    /// 【零海图口径】八张世界海图（101–108）已删除待重做、目录当前为空，
    /// 逐图用例（<c>[ValueSource]</c>）与下面的循环自动零用例/真空通过；
    /// 目录约束用例（关卡号唯一且在 101 段）保留结构，重做后自动恢复强度。
    /// </summary>
    public class WorldMapConnectivityTests
    {
        /// <summary>
        /// 目录里全部海图。**零海图（八张已删除待重做）时产出一个 null 哨兵**：NUnit 对**空**
        /// <c>[ValueSource]</c> 的处理两套 runner 不一致（无头验证台静默零用例，Unity EditMode
        /// 记成失败），给一个占位用例、由用例开头的 <c>Assume</c> 跳成 Skipped；
        /// 海图放回来后逐图用例自动恢复。遍历用 <c>WorldMapCatalog.All</c>（不走本方法，免踩哨兵）。
        /// </summary>
        static IEnumerable<WorldMapDefinition> AllMaps()
        {
            if (WorldMapCatalog.Count == 0)
            {
                yield return null;
                yield break;
            }

            foreach (WorldMapDefinition map in WorldMapCatalog.All)
                yield return map;
        }

        /// <summary>零海图下用例统一以这条前置跳成 Skipped（断言体一律不动）。</summary>
        const string EmptyCatalogSkipReason = "当前工程零海图（八张世界海图已删除待重做），门禁待重做后自动生效";

        [Test]
        public void Catalog_LevelNumbersAreUniqueAndInWorldMapSegment()
        {
            var seen = new HashSet<int>();
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                Assert.IsTrue(seen.Add(map.LevelNumber), "关卡号重复 " + map.LevelNumber);
                Assert.GreaterOrEqual(map.LevelNumber, WorldMapCatalog.FirstLevelNumber);
                Assert.Greater(map.SpanX, 0f);
                Assert.Greater(map.SpanZ, 0f);
            }
        }

        [Test]
        public void AllMaps_SpawnBoxesAreFullyConnected([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), EmptyCatalogSkipReason);

            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            Assert.Greater(boxes.Count, 0, map.Id + " 没有任何站面");

            List<int> unreachable = WorldMapRules.UnreachableSpawnBoxes(map, boxes);
            Assert.IsEmpty(unreachable,
                map.Id + " 有出生点站面不可达（连通性破产）: " + string.Join(",", unreachable));
        }

        [Test]
        public void AllMaps_SpawnsLandOnStandables([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), EmptyCatalogSkipReason);

            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            List<string> problems = WorldMapRules.ValidateSpawns(map, boxes);
            Assert.IsEmpty(problems, map.Id + " 出生点落位问题: " + string.Join("; ", problems));
        }

        [Test]
        public void AllMaps_SpawnCellsAreGroundedInRaster([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), EmptyCatalogSkipReason);

            Assert.IsTrue(WorldMapRules.TryRasterize(map, out int widthTiles, out int depthTiles, out int[] blocks),
                map.Id + " 栅格化失败");

            Assert.Greater(widthTiles, 0);
            Assert.Greater(depthTiles, 0);
            int groundCells = 0;
            for (int i = 0; i < blocks.Length; i++)
                if (blocks[i] > 0)
                    groundCells++;
            // 大世界图的水占比很高，但站面格必须占一席之地（≥3%）。
            Assert.GreaterOrEqual(groundCells, blocks.Length * 3 / 100,
                map.Id + " 站面格占比过低: " + groundCells + "/" + blocks.Length);

            foreach (WorldMapSpawn spawn in map.Spawns)
            {
                int gridX = Mathf.FloorToInt(spawn.X / WorldMapRules.RasterTileSize);
                int gridY = Mathf.FloorToInt(spawn.Z / WorldMapRules.RasterTileSize);
                Assert.GreaterOrEqual(gridX, 0, map.Id);
                Assert.GreaterOrEqual(gridY, 0, map.Id);
                Assert.Less(gridX, widthTiles, map.Id);
                Assert.Less(gridY, depthTiles, map.Id);
                Assert.Greater(blocks[gridX + gridY * widthTiles], 0,
                    map.Id + " 出生格 " + gridX + "," + gridY + " 落在水里");
            }
        }

        [Test]
        public void AllMaps_TeamSpawnCountsMatch([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), EmptyCatalogSkipReason);

            int team0 = 0, team1 = 0;
            foreach (WorldMapSpawn spawn in map.Spawns)
            {
                if (spawn.TeamIndex == 0)
                    team0++;
                else
                    team1++;
            }
            Assert.GreaterOrEqual(team0, 3, map.Id);
            Assert.AreEqual(team0, team1, map.Id + " 双方出生数不对等");
        }

        // ------------------------------------------------------------------
        // 几何原语
        // ------------------------------------------------------------------

        [Test]
        public void RectDistance_MeasuresGapBetweenAxisAlignedBoxes()
        {
            var a = new WorldMapRules.WorldBox(new Vector2(0f, 0f), new Vector2(10f, 10f), 0.5f, 0f);
            var b = new WorldMapRules.WorldBox(new Vector2(20f, 0f), new Vector2(10f, 10f), 0.5f, 0f);
            Assert.AreEqual(10f, WorldMapRules.RectDistance(a, b), 1e-3f);

            var c = new WorldMapRules.WorldBox(new Vector2(5.5f, 0f), new Vector2(2f, 2f), 1f, 0f);
            Assert.AreEqual(0f, WorldMapRules.RectDistance(a, c), 1e-3f); // 相交
        }

        [Test]
        public void CanHop_RespectsGapAndStepLimits()
        {
            var low = new WorldMapRules.WorldBox(new Vector2(0f, 0f), new Vector2(10f, 10f), 0.5f, 0f);
            var near = new WorldMapRules.WorldBox(new Vector2(18f, 0f), new Vector2(6f, 6f), 1f, 0f);
            var far = new WorldMapRules.WorldBox(new Vector2(30f, 0f), new Vector2(6f, 6f), 1f, 0f);
            var high = new WorldMapRules.WorldBox(new Vector2(18f, 20f), new Vector2(6f, 6f), 6f, 0f);

            Assert.IsTrue(WorldMapRules.CanHop(low, near), "5.5u 间隙应可跳");
            Assert.IsFalse(WorldMapRules.CanHop(low, far), "17u 间隙超出极限");
            Assert.IsFalse(WorldMapRules.CanHop(low, high), "5.5u 高差超出上行上限");
        }

        [Test]
        public void OverlapDetection_WorksWithRotatedRects()
        {
            var a = new WorldMapRules.WorldBox(new Vector2(0f, 0f), new Vector2(10f, 4f), 1f, 45f);
            var b = new WorldMapRules.WorldBox(new Vector2(5.5f, -1.5f), new Vector2(2f, 2f), 1f, 0f);
            Assert.IsTrue(WorldMapRules.RectsOverlap(a, b), "斜矩形应与轴对齐小块相交");

            var c = new WorldMapRules.WorldBox(new Vector2(12f, 0f), new Vector2(2f, 2f), 1f, 0f);
            Assert.IsFalse(WorldMapRules.RectsOverlap(a, c));
        }

        [Test]
        public void Standables_TopHeightsAreOnHalfMeterGrid()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                foreach (WorldMapRules.WorldBox box in WorldMapRules.AllStandBoxes(map))
                {
                    float step = box.TopY / 0.5f;
                    Assert.Less(Mathf.Abs(step - Mathf.Round(step)), 0.01f,
                        map.Id + " 站面顶 " + box.TopY + " 不在 0.5 档");
                    Assert.GreaterOrEqual(box.TopY, 0.5f, map.Id);
                }
            }
        }
    }
}
