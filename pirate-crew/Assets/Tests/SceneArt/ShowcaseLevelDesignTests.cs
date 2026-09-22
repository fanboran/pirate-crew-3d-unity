using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 样板关**设计契约测试**（关卡制作管线阶段 2 门禁：R 规则的可测项落成用例）。
    /// 设计真源 = docs/设计/关卡/L0N-*.md；这里钉住「数据层 ↔ 设计文档」不无声分叉——
    /// 改设计先改文档，再改 ShowcaseLevels，然后让这里的断言跟着新口径走。
    /// 规则出处：docs/设计/关卡设计语言-参照游戏全场景分析.md §4（R1-R18）。
    /// </summary>
    public class ShowcaseLevelDesignTests
    {
        /// <summary>
        /// 现存样板关号。关卡 2「碎岛雨」已删除（2026-09-22），号段有意不连续——
        /// 所以这里写**显式清单**而不是 <c>FirstLevel..LastLevel</c> 的连续区间：
        /// 区间会在 2 上取到空数据（抛异常），而"遍历数据源里现存的关"会让数据源整片缺失时
        /// 循环体一次都不跑、门禁变成空跑绿灯。显式清单两者都避开：缺哪关就红在哪关。
        /// </summary>
        static readonly int[] ExistingLevels = { 1, 3 };

        // ------------------------------------------------------------------
        // 硬门禁：站位 / 难度旋钮（全关普查）
        // ------------------------------------------------------------------

        [Test]
        public void AllLevels_EverySpawn_OnSolidGround()
        {
            // R2 引申：出生点必须落在实心格上（否则开局悬空/落水，"开局就是靶子"都不算）。
            for (int l = 0; l < ExistingLevels.Length; l++)
            {
                int level = ExistingLevels[l];
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
            for (int l = 0; l < ExistingLevels.Length; l++)
            {
                int level = ExistingLevels[l];
                LevelData data = ShowcaseLevels.BuildLevelData(level).Value;
                for (int i = 0; i < data.Units.Count; i++)
                {
                    LevelUnit unit = data.Units[i];
                    int expected = unit.teamIndex == 0 ? 5 : ExpectedBlueLuck(level);
                    Assert.AreEqual(expected, unit.luck,
                        "关 " + level + " " + unit.typeName + "(team " + unit.teamIndex + ") luck 应为 " + expected);
                }
            }
        }

        /// <summary>
        /// 蓝方 luck 按**关卡号**取值（难度曲线 1/2/5）。关卡 2 已删除（2026-09-22）后号段不连续，
        /// 不能用「下标 = 关卡号 - 1」的连续假设（那会把关 3 读成 2）；这里按键取，curve 表本身照留。
        /// </summary>
        static int ExpectedBlueLuck(int level)
        {
            switch (level)
            {
                case 1: return 1;
                case 2: return 2;   // 关卡 2 已删除：此档只作难度曲线的完整刻度记录，不再被遍历到
                case 3: return 5;
                default: return 5;  // 新增关卡需同时登记 curve 档位
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
    }
}
