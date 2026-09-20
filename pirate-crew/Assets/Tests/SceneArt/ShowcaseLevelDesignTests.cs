using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 样板三关**设计契约测试**（关卡制作管线阶段 2 门禁：R 规则的可测项落成用例）。
    /// 设计真源 = docs/设计/关卡/L0N-*.md；这里钉住「数据层 ↔ 设计文档」不无声分叉——
    /// 改设计先改文档，再改 ShowcaseLevels，然后让这里的断言跟着新口径走。
    /// 规则出处：docs/设计/关卡设计语言-参照游戏全场景分析.md §4（R1-R18）。
    /// </summary>
    public class ShowcaseLevelDesignTests
    {
        // ------------------------------------------------------------------
        // 硬门禁：站位 / 难度旋钮（三关全查）
        // ------------------------------------------------------------------

        [Test]
        public void AllLevels_EverySpawn_OnSolidGround()
        {
            // R2 引申：出生点必须落在实心格上（否则开局悬空/落水，"开局就是靶子"都不算）。
            for (int level = ShowcaseLevels.FirstLevel; level <= ShowcaseLevels.LastLevel; level++)
            {
                LevelData data = ShowcaseLevels.BuildLevelData(level).Value;
                TileTerrainGrid grid = ShowcaseLevels.BuildLogicGrid(level);

                Assert.IsNotEmpty(data.Units, "关 " + level + " 应有单位");
                for (int i = 0; i < data.Units.Count; i++)
                {
                    LevelUnit unit = data.Units[i];
                    Assert.Greater(grid.BlocksAt(unit.gridX, unit.gridY), 0,
                        "关 " + level + " 单位 " + unit.typeName + "(" + unit.gridX + "," + unit.gridY + ") 应站在实心格上");
                }
            }
        }

        [Test]
        public void EnemyLuck_LadderIs_One_Two_Five()
        {
            // 关间难度曲线（设计文档 README curve 块）：蓝方 luck 1→2→5；红方（玩家）恒 5。
            int[] expectedBlueLuck = { 1, 2, 5 };
            for (int level = ShowcaseLevels.FirstLevel; level <= ShowcaseLevels.LastLevel; level++)
            {
                LevelData data = ShowcaseLevels.BuildLevelData(level).Value;
                for (int i = 0; i < data.Units.Count; i++)
                {
                    LevelUnit unit = data.Units[i];
                    int expected = unit.teamIndex == 0 ? 5 : expectedBlueLuck[level - 1];
                    Assert.AreEqual(expected, unit.luck,
                        "关 " + level + " " + unit.typeName + "(team " + unit.teamIndex + ") luck 应为 " + expected);
                }
            }
        }

        // ------------------------------------------------------------------
        // L1 云端漫步：教学关尺度（R18 意译）
        // ------------------------------------------------------------------

        [Test]
        public void L01_CloudHeights_FourTiers_MaxFifteenBlocks()
        {
            // 云场高度档 {5,9,13,15} 块（y2.5/4.5/6.5/7.5）——高低错落的美术方向（用户裁决）
            // 偏离 R18 的 ≤2 层基准，此处在测试里钉住实际档位防漂移。
            TileTerrainGrid grid = ShowcaseLevels.BuildLogicGrid(1);
            int min = int.MaxValue, max = int.MinValue;
            var tiers = new HashSet<int>();
            ForEachSolid(grid, (blocks) =>
            {
                min = Mathf.Min(min, blocks);
                max = Mathf.Max(max, blocks);
                tiers.Add(blocks);
            });

            Assert.AreEqual(5, min, "最低云应为 5 块（y2.5）");
            Assert.AreEqual(15, max, "最高云应为 15 块（y7.5 北云）");
            Assert.AreEqual(4, tiers.Count, "云场应有 4 个高度档");
        }

        // ------------------------------------------------------------------
        // L2 碎岛雨：母题度量（R2/R3/R6/R9）
        // ------------------------------------------------------------------

        [Test]
        public void L02_IsletRain_SevenClusters_SixtyNineTiles()
        {
            // 设计文档 L02 §3：7 座独立岛、69 格平台面积（人均 8.6，R2 ≥4）。
            TileTerrainGrid grid = ShowcaseLevels.BuildLogicGrid(2);
            int solid = 0;
            ForEachSolid(grid, (blocks) => solid++);

            Assert.AreEqual(69, solid, "碎岛雨平台总格数应为 69");
            Assert.AreEqual(7, CountClusters(grid), "碎岛雨应有 7 座独立岛（4 连通域）");
        }

        [Test]
        public void L02_IsletRain_MaxWaterGapWithinRowOrColumn_IsFive()
        {
            // R3 常规水距 1-5：行/列方向「首末实心格之间」的最大空档 ≤5（红主岛↔C3 的 5 格
            // 大跨是设计文档「东北快线」的明确高风险选择，压在 R3 上限）。
            TileTerrainGrid grid = ShowcaseLevels.BuildLogicGrid(2);
            int maxGap = 0;

            for (int y = 0; y < ShowcaseLevels.DepthTiles; y++)
                maxGap = Mathf.Max(maxGap, MaxOpenRunBetweenSolids(grid, y, horizontal: true));
            for (int x = 0; x < ShowcaseLevels.WidthTiles; x++)
                maxGap = Mathf.Max(maxGap, MaxOpenRunBetweenSolids(grid, x, horizontal: false));

            Assert.AreEqual(5, maxGap, "碎岛雨最大水隙应为 5 格（R3 常规上限，设计文档东北快线）");
        }

        [Test]
        public void L02_IsletRain_AllTops_SingleTier()
        {
            // 原版 L2 全场 1 层（关卡设计语言 §1 表）：碎岛同高 → 抛物线计算不带高度修正。
            TileTerrainGrid grid = ShowcaseLevels.BuildLogicGrid(2);
            ForEachSolid(grid, (blocks) => Assert.AreEqual(1, blocks, "碎岛顶面应恒 1 块（y0.5）"));
        }

        // ------------------------------------------------------------------
        // L3 天空之岛：考核关尺度
        // ------------------------------------------------------------------

        [Test]
        public void L03_SkyIsland_FlatTop_TwentyEightBlocks()
        {
            // 岛面恒 28 块（y14），与场景空岛草皮面对齐（设计文档 L03 §3）。
            TileTerrainGrid grid = ShowcaseLevels.BuildLogicGrid(3);
            int solid = 0;
            ForEachSolid(grid, (blocks) =>
            {
                Assert.AreEqual(28, blocks, "大岛顶面应恒 28 块（y14）");
                solid++;
            });

            Assert.GreaterOrEqual(solid, 36, "大岛面积 ≥9 人 ×4 格（R2）");
        }

        [Test]
        public void L03_SkyIsland_FourVersusFive()
        {
            // 以少打多（设计文档 L03 §4）：红 4 vs 蓝 5。
            LevelData data = ShowcaseLevels.BuildLevelData(3).Value;
            int red = 0, blue = 0;
            for (int i = 0; i < data.Units.Count; i++)
            {
                if (data.Units[i].teamIndex == 0) red++;
                else blue++;
            }
            Assert.AreEqual(4, red);
            Assert.AreEqual(5, blue);
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        delegate void SolidVisitor(int blocks);

        static void ForEachSolid(TileTerrainGrid grid, SolidVisitor visit)
        {
            for (int y = 0; y < grid.DepthTiles; y++)
                for (int x = 0; x < grid.WidthTiles; x++)
                {
                    int blocks = grid.BlocksAt(x, y);
                    if (blocks > 0)
                        visit(blocks);
                }
        }

        /// <summary>行/列方向：首末实心格之间的最大连续空档（两端边距不计——那是场外留白）。</summary>
        static int MaxOpenRunBetweenSolids(TileTerrainGrid grid, int index, bool horizontal)
        {
            int length = horizontal ? grid.WidthTiles : grid.DepthTiles;
            int first = -1, last = -1;
            for (int i = 0; i < length; i++)
            {
                bool solid = horizontal ? grid.BlocksAt(i, index) > 0 : grid.BlocksAt(index, i) > 0;
                if (!solid)
                    continue;
                if (first < 0)
                    first = i;
                last = i;
            }

            if (first < 0)
                return 0;   // 整行/列无岛：场外留白，不参与水距统计

            int maxRun = 0, run = 0;
            for (int i = first; i <= last; i++)
            {
                bool solid = horizontal ? grid.BlocksAt(i, index) > 0 : grid.BlocksAt(index, i) > 0;
                if (solid)
                    run = 0;
                else
                    maxRun = Mathf.Max(maxRun, ++run);
            }
            return maxRun;
        }

        /// <summary>实心格 4 连通域计数（R9 的"独立平台簇数"口径）。</summary>
        static int CountClusters(TileTerrainGrid grid)
        {
            var seen = new bool[grid.WidthTiles * grid.DepthTiles];
            int clusters = 0;
            var queue = new Queue<int>();

            for (int start = 0; start < seen.Length; start++)
            {
                if (seen[start] || grid.BlocksAt(start % grid.WidthTiles, start / grid.WidthTiles) <= 0)
                    continue;

                clusters++;
                queue.Enqueue(start);
                seen[start] = true;
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    int cx = cell % grid.WidthTiles, cy = cell / grid.WidthTiles;
                    TryVisit(grid, seen, queue, cx + 1, cy);
                    TryVisit(grid, seen, queue, cx - 1, cy);
                    TryVisit(grid, seen, queue, cx, cy + 1);
                    TryVisit(grid, seen, queue, cx, cy - 1);
                }
            }
            return clusters;
        }

        static void TryVisit(TileTerrainGrid grid, bool[] seen, Queue<int> queue, int x, int y)
        {
            if (x < 0 || y < 0 || x >= grid.WidthTiles || y >= grid.DepthTiles)
                return;
            int cell = y * grid.WidthTiles + x;
            if (seen[cell] || grid.BlocksAt(x, y) <= 0)
                return;
            seen[cell] = true;
            queue.Enqueue(cell);
        }
    }
}
