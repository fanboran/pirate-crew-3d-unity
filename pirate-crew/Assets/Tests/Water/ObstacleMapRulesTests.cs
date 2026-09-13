using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Water;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="ObstacleMapRules"/> 测试：烘焙障碍图与地形高度一致（抽格断言）、
    /// 域中心与开关的边界行为。用纯 C# 的 <see cref="TileTerrainGrid"/> 造地形，无需 Unity 运行时。
    /// </summary>
    [TestFixture]
    public class ObstacleMapRulesTests
    {
        [Test]
        public void IsObstacle_FollowsTerrainHeightRelativeToWater()
        {
            // 4×4 网格：只有 (1,1) 堆了 8 块（8×0.25 = 2.0 单位高）。
            var blocks = new int[16];
            blocks[1 + 1 * 4] = 8;
            var grid = new TileTerrainGrid(4, 4, blocks, 0.25f);

            // 把水面抬到 y=1.0：该柱（地表 2.0）是障碍，平地（地表 0）是开阔水。
            const float waterY = 1.0f;
            const float arenaW = 4f, arenaD = 4f;

            // 抽格：柱心 (1.5, 1.5) → 障碍；平地格心 (0.5, 0.5) → 非障碍。
            Assert.IsTrue(ObstacleMapRules.IsObstacleAt(grid, 1.5f, 1.5f, waterY, arenaW, arenaD));
            Assert.IsFalse(ObstacleMapRules.IsObstacleAt(grid, 0.5f, 0.5f, waterY, arenaW, arenaD));
        }

        [Test]
        public void IsObstacle_OutsideArenaIsOpenWater()
        {
            var grid = TileTerrainGrid.Flat(4, 4);
            // 默认水面 -0.2、地面 0 → 场内处处障碍（沙岛）。
            Assert.IsTrue(ObstacleMapRules.IsObstacleAt(grid, 2f, 2f, -0.2f, 4f, 4f));
            // 场外（岛外海床在水面之下）→ 开阔水。
            Assert.IsFalse(ObstacleMapRules.IsObstacleAt(grid, -1f, 2f, -0.2f, 4f, 4f));
            Assert.IsFalse(ObstacleMapRules.IsObstacleAt(grid, 5f, 2f, -0.2f, 4f, 4f));
        }

        [Test]
        public void Bake_FlatArena_MarksExactlyTheArenaCells()
        {
            // 域 64×64、128 格 → dx=0.5；竞技场 50×17 居中在 (25, 8.5)。
            var grid = TileTerrainGrid.Flat(50, 17);
            var center = new Vector2(25f, 8.5f);
            const float domainSize = 64f;
            const int cells = 128;
            const float waterY = -0.2f;

            bool[] mask = ObstacleMapRules.Bake(grid, center, domainSize, cells, waterY, 50f, 17f);

            Assert.AreEqual(cells * cells, mask.Length);

            // 抽格核对：逐个格的世界坐标是否落在竞技场内，应与掩码一致。
            int expectedObstacles = 0;
            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    float u = (cx + 0.5f) / cells;
                    float v = (cz + 0.5f) / cells;
                    Vector2 world = WaterSimRules.DomainUvToWorld(new Vector2(u, v), center, domainSize);
                    bool insideArena = world.x >= 0f && world.x <= 50f && world.y >= 0f && world.y <= 17f;

                    Assert.AreEqual(insideArena, mask[cz * cells + cx],
                        $"格 ({cx},{cz}) 世界 ({world.x:F2},{world.y:F2}) 掩码与竞技场范围不一致");

                    if (insideArena)
                        expectedObstacles++;
                }
            }

            Assert.AreEqual(expectedObstacles, ObstacleMapRules.CountObstacles(mask));
            // 50×17 竞技场在 64×64 域里约占 20% 面积。
            Assert.That((float)expectedObstacles / mask.Length, Is.InRange(0.18f, 0.24f));
        }

        [Test]
        public void Bake_NullGrid_TreatsArenaAsLand()
        {
            var center = new Vector2(25f, 8.5f);
            bool[] mask = ObstacleMapRules.Bake(null, center, 64f, 32, -0.2f, 50f, 17f);
            // 场内的格仍应是障碍（兜底按沙岛平地）。
            Assert.IsTrue(ObstacleMapRules.IsObstacleAt(null, 25f, 8.5f, -0.2f, 50f, 17f));
            Assert.IsFalse(ObstacleMapRules.IsObstacleAt(null, -10f, 8.5f, -0.2f, 50f, 17f));
        }

        [Test]
        public void Bake_IsDeterministic()
        {
            var grid = TerrainCatalog.Build(1, 50, 17) ?? TileTerrainGrid.Flat(50, 17);
            var center = new Vector2(25f, 8.5f);

            bool[] a = ObstacleMapRules.Bake(grid, center, 64f, 64, -0.2f, 50f, 17f);
            bool[] b = ObstacleMapRules.Bake(grid, center, 64f, 64, -0.2f, 50f, 17f);

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i], b[i]);
        }
    }
}
