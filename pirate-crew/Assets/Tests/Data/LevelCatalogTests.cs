using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// LevelCatalog 的纯 C# 断言（对应静态逆向文档 §7.2 / §4.3 / §5.5）。
    /// 覆盖全部 33 关的通用不变量；与原始关卡 XML 的逐字段比对见
    /// <see cref="LevelCatalogJsonParityTests"/>。
    /// </summary>
    public class LevelCatalogTests
    {
        [Test]
        public void Count_Is33_AndLevelNumbersAre1To33()
        {
            Assert.That(LevelCatalog.TotalLevels, Is.EqualTo(33));
            Assert.That(LevelCatalog.Count, Is.EqualTo(33));

            var seen = new HashSet<int>();
            foreach (LevelData level in LevelCatalog.All)
                Assert.That(seen.Add(level.LevelNumber), Is.True, "关卡号重复: " + level.LevelNumber);

            var expected = new List<int>();
            for (int n = 1; n <= 33; n++) expected.Add(n);
            Assert.That(seen, Is.EquivalentTo(expected));

            // All 按关卡号升序（分发表 1..33 顺序），便于外部按下标取用
            for (int i = 0; i < LevelCatalog.All.Count; i++)
                Assert.That(LevelCatalog.All[i].LevelNumber, Is.EqualTo(i + 1));
        }

        [Test]
        public void EachLevel_HasUnitsOnBothTeams()
        {
            foreach (LevelData level in LevelCatalog.All)
            {
                int red = 0;
                int blue = 0;
                foreach (LevelUnit unit in level.Units)
                {
                    if (unit.teamIndex == CrewCatalog.RedTeamIndex) red++;
                    else if (unit.teamIndex == CrewCatalog.BlueTeamIndex) blue++;
                }

                Assert.That(red, Is.GreaterThanOrEqualTo(1), "关卡 " + level.LevelNumber + " 缺少红队单位");
                Assert.That(blue, Is.GreaterThanOrEqualTo(1), "关卡 " + level.LevelNumber + " 缺少蓝队单位");
            }
        }

        [Test]
        public void EachUnit_TeamIndex_MatchesHardcodedRule()
        {
            foreach (LevelData level in LevelCatalog.All)
            {
                foreach (LevelUnit unit in level.Units)
                {
                    Assert.That(unit.teamIndex, Is.EqualTo(CrewCatalog.TeamIndexOf(unit.typeName)),
                        "关卡 " + level.LevelNumber + " 单位 " + unit.typeName + " 队伍归属与 §4.3 不一致");
                }
            }
        }

        [Test]
        public void EachUnit_GridCoordinates_WithinLevelBounds()
        {
            foreach (LevelData level in LevelCatalog.All)
            {
                foreach (LevelUnit unit in level.Units)
                {
                    Assert.That(unit.gridX, Is.InRange(0, level.WidthTiles - 1),
                        "关卡 " + level.LevelNumber + " 单位 " + unit.typeName + " gridX 越界");
                    Assert.That(unit.gridY, Is.InRange(0, level.HeightTiles - 1),
                        "关卡 " + level.LevelNumber + " 单位 " + unit.typeName + " gridY 越界");
                }
            }
        }

        // ------------------------------------------------------------------
        // §5.5 水面 Y 语义
        // ------------------------------------------------------------------

        [Test]
        public void WaterY_EqualsWaterTileYTimes32()
        {
            foreach (LevelData level in LevelCatalog.All)
            {
                Assert.That(level.WaterY, Is.EqualTo(level.WaterTileY * 32f),
                    "关卡 " + level.LevelNumber + " 水面 Y 不符合 §5.5（water.y = y*32）");
            }

            Assert.That(LevelCatalog.Get(1).WaterY, Is.EqualTo(448f), "level_1 water y=14 → 448");
            Assert.That(LevelCatalog.Get(4).WaterY, Is.EqualTo(544f), "level_4 water y=17 → 544");
            Assert.That(LevelCatalog.Get(27).WaterY, Is.EqualTo(608f), "level_27 water y=19 → 608");
        }

        // ------------------------------------------------------------------
        // 武器引用完整性
        // ------------------------------------------------------------------

        [Test]
        public void AllReferencedWeapons_ExistInWeaponCatalog()
        {
            foreach (LevelData level in LevelCatalog.All)
            {
                AssertWeaponsExist(level.PotentialWeapons, "关卡 " + level.LevelNumber + " 空投池");
                foreach (LevelUnit unit in level.Units)
                    AssertWeaponsExist(unit.initialWeapons, "关卡 " + level.LevelNumber + " 单位 " + unit.typeName);
            }
        }

        static void AssertWeaponsExist(IReadOnlyList<WeaponStack> stacks, string context)
        {
            foreach (WeaponStack stack in stacks)
            {
                Assert.That(WeaponCatalog.TryGet(stack.id, out _), Is.True,
                    context + " 引用了 WeaponCatalog 中不存在的武器: " + stack.id);
            }
        }

        // ------------------------------------------------------------------
        // 宝箱上限（§5.5 硬编码 3）
        // ------------------------------------------------------------------

        [Test]
        public void MaxChests_IsThree()
        {
            foreach (LevelData level in LevelCatalog.All)
                Assert.That(level.MaxChests, Is.EqualTo(3), "关卡 " + level.LevelNumber + " maxChests 应为 §5.5 的 3");
        }

        // ------------------------------------------------------------------
        // §4.3 坐标换算
        // ------------------------------------------------------------------

        [Test]
        public void CoordinateConversion_MatchesPseudoCode()
        {
            // px = (xmlX + 0.5) * 32
            Assert.That(LevelCatalog.ToPixelX(0), Is.EqualTo(16f));
            Assert.That(LevelCatalog.ToPixelX(1), Is.EqualTo(48f));

            // py = (xmlY + 0.5) * 32 + 16 - bottomExtent，bottomExtent = 8
            Assert.That(LevelCatalog.ToPixelY(0, CrewCatalog.BottomExtent), Is.EqualTo(16f + 16f - 8f));
            Assert.That(LevelCatalog.ToPixelY(10, CrewCatalog.BottomExtent), Is.EqualTo(10.5f * 32f + 8f));

            // water.y = y * 32
            Assert.That(LevelCatalog.ToWaterY(14), Is.EqualTo(448f));

            // LevelUnit 上的便捷访问器与静态换算一致
            LevelData level1 = LevelCatalog.Get(1);
            foreach (LevelUnit unit in level1.Units)
            {
                Assert.That(unit.PixelX, Is.EqualTo(LevelCatalog.ToPixelX(unit.gridX)));
                Assert.That(unit.PixelY, Is.EqualTo(LevelCatalog.ToPixelY(unit.gridY, CrewCatalog.BottomExtent)));
            }
        }

        // ------------------------------------------------------------------
        // 待补关卡标注：33 关全部补齐后应无待补
        // ------------------------------------------------------------------

        [Test]
        public void PendingLevels_AreEmpty_All33Transcribed()
        {
            Assert.That(LevelCatalog.PendingLevelNumbers, Is.Empty, "33 关应已全部转写，无「待补」关卡");

            for (int n = 1; n <= 33; n++)
                Assert.That(LevelCatalog.IsTranscribed(n), Is.True, "关卡 " + n + " 应已转写");

            Assert.That(LevelCatalog.TranscribedLevelNumbers.Count, Is.EqualTo(33));
            Assert.That(LevelCatalog.IsTranscribed(0), Is.False, "0 号不是合法关卡号");
            Assert.That(LevelCatalog.IsTranscribed(34), Is.False, "34 号超出 TotalLevels");

            // 越界关卡号在取数时立刻抛错，而不是返回默认 struct
            Assert.Throws<KeyNotFoundException>(() => LevelCatalog.Get(0));
            Assert.Throws<KeyNotFoundException>(() => LevelCatalog.Get(34));
            Assert.DoesNotThrow(() => LevelCatalog.Get(2));
            Assert.DoesNotThrow(() => LevelCatalog.Get(33));
        }

        // ------------------------------------------------------------------
        // 关卡尺寸与模式（§7.2 尺寸表）
        // ------------------------------------------------------------------

        [Test]
        public void LevelMetadata_MatchesDoc72SizeTable()
        {
            int[,] expected =
            {
                { 1, 50, 17, 1 },  { 2, 47, 27, 1 },  { 3, 40, 23, 1 },  { 4, 56, 18, 1 },
                { 5, 44, 20, 1 },  { 6, 115, 28, 1 }, { 7, 24, 12, 1 },  { 8, 20, 35, 1 },
                { 9, 59, 25, 1 },  { 10, 77, 32, 1 },{ 11, 44, 16, 1 }, { 12, 44, 26, 1 },
                { 13, 90, 7, 1 },  { 14, 48, 31, 1 },{ 15, 86, 20, 1 }, { 16, 50, 17, 1 },
                { 17, 47, 28, 1 }, { 18, 40, 21, 1 },{ 19, 56, 18, 1 }, { 20, 44, 18, 1 },
                { 21, 63, 35, 2 }, { 22, 115, 28, 2 },{ 23, 24, 16, 2 }, { 24, 23, 34, 2 },
                { 25, 59, 27, 2 }, { 26, 77, 32, 2 },{ 27, 21, 20, 2 }, { 28, 44, 20, 2 },
                { 29, 44, 26, 2 }, { 30, 90, 12, 2 },{ 31, 48, 30, 2 }, { 32, 86, 20, 2 },
                { 33, 41, 33, 1 },
            };

            for (int i = 0; i < expected.GetLength(0); i++)
            {
                LevelData level = LevelCatalog.Get(expected[i, 0]);
                Assert.That(level.WidthTiles, Is.EqualTo(expected[i, 1]),
                    "关卡 " + level.LevelNumber + " 宽度与 §7.2 尺寸表不符");
                Assert.That(level.HeightTiles, Is.EqualTo(expected[i, 2]),
                    "关卡 " + level.LevelNumber + " 高度与 §7.2 尺寸表不符");
                Assert.That(level.Name, Is.EqualTo("level_" + level.LevelNumber), "关卡名应为键名");
                Assert.That(level.OriginalXmlPlayers, Is.EqualTo(expected[i, 3]),
                    "关卡 " + level.LevelNumber + " XML players 属性与原始关卡 XML 不符");
            }
        }
    }
}
