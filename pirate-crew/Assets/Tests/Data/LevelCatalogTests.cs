using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// LevelCatalog 的纯 C# 断言（对应静态逆向文档 §7.2 / §4.3 / §5.5）。
    /// 只覆盖 3 个已转写代表关的不变量；其余 30 关标注「待补」。
    /// </summary>
    public class LevelCatalogTests
    {
        [Test]
        public void Count_Is3_AndLevelNumbersUnique()
        {
            Assert.That(LevelCatalog.Count, Is.EqualTo(3));
            Assert.That(LevelCatalog.TotalLevels, Is.EqualTo(33));

            var seen = new HashSet<int>();
            foreach (LevelData level in LevelCatalog.All)
                Assert.That(seen.Add(level.LevelNumber), Is.True, "关卡号重复: " + level.LevelNumber);

            Assert.That(seen, Is.EquivalentTo(new[] { 1, 4, 27 }));
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
        // 待补关卡标注
        // ------------------------------------------------------------------

        [Test]
        public void PendingLevels_AreExplicitlyMarked()
        {
            Assert.That(LevelCatalog.PendingLevelNumbers.Count, Is.EqualTo(30));
            foreach (int n in new[] { 1, 4, 27 })
                Assert.That(LevelCatalog.IsTranscribed(n), Is.True, "关卡 " + n + " 应已转写");

            var pending = new HashSet<int>(LevelCatalog.PendingLevelNumbers);
            Assert.That(LevelCatalog.IsTranscribed(2), Is.False);
            Assert.That(pending.Contains(2), Is.True);
            Assert.That(pending.Contains(33), Is.True);
            Assert.That(pending.Contains(4), Is.False);

            Assert.Throws<KeyNotFoundException>(() => LevelCatalog.Get(2));
        }
    }
}
