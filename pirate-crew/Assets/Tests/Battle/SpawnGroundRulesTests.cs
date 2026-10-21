using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
using PirateCrew.Data;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="SpawnGroundRules"/>（出生站面优先级仲裁）的纯 C# 用例（无头可跑）。
    ///
    /// 【钉住的口径】①数据高度优先（岛带逐格高度时，光栅化网格说了算，穹顶只是影子）；
    /// ②数据缺失 + 空岛根激活 → 逐点贴穹顶（短期方案的回退位）；③无穹顶 → 平顶；④兜底地面。
    /// 数据高度 vs 穹顶公式的逐点等价性见 <c>PirateCrew.SceneArt.Tests.FloatingIslandShowcaseTests</c>
    /// （差 ≤ 采样格内穹顶落差 + 半块量化）。
    /// </summary>
    [TestFixture]
    public class SpawnGroundRulesTests
    {
        const float Cell = 2f;

        /// <summary>岛根位（Battle 场景装配器写死的场心居中位）。</summary>
        static readonly Vector3 Root = new Vector3(20f, 13.3f, 15f);

        static LevelAssetPayload PayloadWithHeights()
        {
            // 岛轮廓包住岛根周边 6×6 m，逐格高度 = 穹顶公式在该岛的采样（等价性另测，这里只造数）。
            var island = new TerrainIsland
            {
                topY = 14f,
                outline = new List<float> { 16f, 11f, 26f, 11f, 26f, 21f, 16f, 21f },
            };
            island = FloatingIslandHeightsBake.Bake(island, FloatingIslandSpec.Default, Root, 40f, 30f);

            return new LevelAssetPayload
            {
                levelNumber = 2,
                assetName = "spawn_rules_fixture",
                displayName = "fixture",
                sizeX = 40f,
                sizeZ = 30f,
                waterWorldY = LevelGeometry.WaterSurfaceY,
                terrain = new TerrainShape
                {
                    blockWorldHeight = LevelGeometry.BlockWorldHeight,
                    islands = new List<TerrainIsland> { island },
                },
            };
        }

        static LevelAssetPayload PayloadFlat()
        {
            return new LevelAssetPayload
            {
                levelNumber = 2,
                assetName = "spawn_rules_fixture_flat",
                displayName = "fixture",
                sizeX = 40f,
                sizeZ = 30f,
                waterWorldY = LevelGeometry.WaterSurfaceY,
                terrain = new TerrainShape
                {
                    blockWorldHeight = LevelGeometry.BlockWorldHeight,
                    islands = new List<TerrainIsland>
                    {
                        new TerrainIsland
                        {
                            topY = 14f,
                            outline = new List<float> { 16f, 11f, 26f, 11f, 26f, 21f, 16f, 21f },
                        },
                    },
                },
            };
        }

        [Test]
        public void DataHeights_Win_EvenWhenDomeActive()
        {
            // ①数据优先：穹顶也激活时仍取光栅化网格高度（块量化台阶），不是穹顶公式值。
            LevelAssetPayload payload = PayloadWithHeights();
            HeightfieldGrid grid = LevelRasterFromAsset.Build(payload);

            float x = Root.x + 1.3f, z = Root.z + 0.6f;
            int cellX = LevelGeometry.WorldToTileIndex(x), cellY = LevelGeometry.WorldToTileIndex(z);
            float gridY = grid.SurfaceWorldY(cellX, cellY);
            float domeY = FloatingIslandDome.SpawnGroundY(FloatingIslandSpec.Default, Root, x, z);
            Assert.That(gridY, Is.Not.EqualTo(domeY).Within(0.01f),
                "前置：两条路径在该点应取不同值（否则测不出优先级）");

            float actual = SpawnGroundRules.GroundWorldY(payload, grid, x, z, cellX, cellY,
                skyIslandActive: true, islandRootWorld: Root);

            Assert.AreEqual(gridY, actual, 1e-4f, "数据高度优先：穹顶激活时也必须吃光栅化网格的高度");
        }

        [Test]
        public void MissingHeights_FallBackToDome_WhenIslandActive()
        {
            // ②穹顶兜底：岛没带逐格高度（schema ≤ 3 数据）而空岛根激活 → 逐点贴穹顶。
            LevelAssetPayload payload = PayloadFlat();
            HeightfieldGrid grid = LevelRasterFromAsset.Build(payload);

            float x = Root.x + 1.3f, z = Root.z + 0.6f;
            int cellX = LevelGeometry.WorldToTileIndex(x), cellY = LevelGeometry.WorldToTileIndex(z);
            float expected = FloatingIslandDome.SpawnGroundY(FloatingIslandSpec.Default, Root, x, z);

            float actual = SpawnGroundRules.GroundWorldY(payload, grid, x, z, cellX, cellY,
                skyIslandActive: true, islandRootWorld: Root);
            Assert.AreEqual(expected, actual, 1e-5f, "数据缺失时必须逐点贴穹顶（短期方案的回退位）");
        }

        [Test]
        public void MissingHeights_FlatTop_WhenDomeInactive()
        {
            // ③平顶兜底：数据缺失且空岛根未激活（其余关卡/场景未烘焙）→ 逻辑高度场平顶。
            LevelAssetPayload payload = PayloadFlat();
            HeightfieldGrid grid = LevelRasterFromAsset.Build(payload);

            float x = 20f, z = 16f;
            int cellX = LevelGeometry.WorldToTileIndex(x), cellY = LevelGeometry.WorldToTileIndex(z);

            float actual = SpawnGroundRules.GroundWorldY(payload, grid, x, z, cellX, cellY,
                skyIslandActive: false, islandRootWorld: Vector3.zero);
            Assert.AreEqual(14f, actual, 1e-4f, "无穹顶时按逻辑高度场平顶 topY=14");
        }

        [Test]
        public void NoPayloadNoTerrain_GroundTopY()
        {
            // ④兜底地面：载荷与网格全空。
            float actual = SpawnGroundRules.GroundWorldY(null, null, 20f, 15f, 10, 7,
                skyIslandActive: false, islandRootWorld: Vector3.zero);
            Assert.AreEqual(LevelGeometry.GroundTopY, actual, 1e-6f);
        }

        [Test]
        public void DataHole_FallsThroughToDome()
        {
            // 数据洞（该格块数为 0）不落在 ①：即便网格有数据，0 块格要走到 ②/③ 兜底，
            // 别把单位钉在基础地面上。
            LevelAssetPayload payload = PayloadWithHeights();
            TerrainIsland island = payload.terrain.islands[0];
            // 挖一个洞：找格中心在轮廓内的一格，值清 0。
            int holeCol = -1, holeRow = -1;
            for (int row = 0; row < island.heightRows && holeCol < 0; row++)
            {
                for (int col = 0; col < island.heightCols; col++)
                {
                    float cx = (island.heightCol0 + col + 0.5f) * Cell;
                    float cz = (island.heightRow0 + row + 0.5f) * Cell;
                    if (TerrainShapeRasterizer.PointInIsland(cx, cz, island)
                        && island.heights[col + row * island.heightCols] > 0f)
                    {
                        island.heights[col + row * island.heightCols] = 0f;
                        holeCol = island.heightCol0 + col;
                        holeRow = island.heightRow0 + row;
                        break;
                    }
                }
            }
            Assert.GreaterOrEqual(holeCol, 0, "前置：fixture 里应能找到一个岛内格");

            HeightfieldGrid grid = LevelRasterFromAsset.Build(payload);
            float x = (holeCol + 0.5f) * Cell, z = (holeRow + 0.5f) * Cell;
            float expected = FloatingIslandDome.SpawnGroundY(FloatingIslandSpec.Default, Root, x, z);

            float actual = SpawnGroundRules.GroundWorldY(payload, grid, x, z, holeCol, holeRow,
                skyIslandActive: true, islandRootWorld: Root);
            Assert.AreEqual(expected, actual, 1e-5f, "数据洞必须落到穹顶兜底（该格无数据地面）");
        }

        [Test]
        public void OutOfGridPoint_FallsBackToDome()
        {
            // 数据网格没盖住的点（HasPerCellHeight = false）：站面走 ②穹顶兜底。
            LevelAssetPayload payload = PayloadWithHeights();
            HeightfieldGrid grid = LevelRasterFromAsset.Build(payload);

            float x = 30f, z = 20f;   // 场内但在岛外（网格覆盖外）
            int cellX = LevelGeometry.WorldToTileIndex(x), cellY = LevelGeometry.WorldToTileIndex(z);
            float expected = FloatingIslandDome.SpawnGroundY(FloatingIslandSpec.Default, Root, x, z);

            float actual = SpawnGroundRules.GroundWorldY(payload, grid, x, z, cellX, cellY,
                skyIslandActive: true, islandRootWorld: Root);
            Assert.AreEqual(expected, actual, 1e-5f, "网格覆盖外的点走穹顶兜底");
        }
    }
}
