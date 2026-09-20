using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// M4 大海域世界地图的硬约束校验（docs/M4-大海域世界化.md §2.2/§4.2）：
    /// 对目录里全部 8 张图断言——连通性（承载出生点的站面 box 同连通分量）、
    /// 出生点落位（在站面上、离边有余量、脚下无更高覆盖）、栅格化有效。
    /// 这是「地图各地方必须能来回跳过去」这一用户裁决的测试化。
    /// </summary>
    public class WorldMapConnectivityTests
    {
        static IEnumerable<WorldMapDefinition> AllMaps()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
                yield return map;
        }

        [Test]
        public void Catalog_HasEightMaps_WithUniqueLevelNumbers()
        {
            Assert.AreEqual(8, WorldMapCatalog.Count);
            var seen = new HashSet<int>();
            foreach (WorldMapDefinition map in AllMaps())
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
            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            Assert.Greater(boxes.Count, 0, map.Id + " 没有任何站面");

            List<int> unreachable = WorldMapRules.UnreachableSpawnBoxes(map, boxes);
            Assert.IsEmpty(unreachable,
                map.Id + " 有出生点站面不可达（连通性破产）: " + string.Join(",", unreachable));
        }

        [Test]
        public void AllMaps_SpawnsLandOnStandables([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            List<string> problems = WorldMapRules.ValidateSpawns(map, boxes);
            Assert.IsEmpty(problems, map.Id + " 出生点落位问题: " + string.Join("; ", problems));
        }

        [Test]
        public void AllMaps_SpawnCellsAreGroundedInRaster([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
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
            foreach (WorldMapDefinition map in AllMaps())
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
