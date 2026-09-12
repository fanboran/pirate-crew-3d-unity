using NUnit.Framework;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 出战计划生成测试（§4.3 坐标/队伍、§4.1 luck、§5.5 初始武器、§4.4 全局水面常量）。
    /// 坐标按 3D 重投影语义（gridY→Z 纵深），用 LevelCatalog 的纯 C# 关卡数据，无头可跑。
    /// </summary>
    [TestFixture]
    public class BattlePlanTests
    {
        [Test]
        public void Level1_Plan_HasExpectedTeamSplit()
        {
            // §7.2 level_1：4 redPirate + 1 redPirateCaptain（红5） vs 2 cabinBoy + 1 cabinBoyCaptain（蓝3）
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(1));

            Assert.AreEqual(8, plan.Entries.Count);
            Assert.AreEqual(5, plan.CountForTeam(0));
            Assert.AreEqual(3, plan.CountForTeam(1));
            Assert.AreEqual(1, plan.OriginalXmlPlayers);
        }

        [Test]
        public void Level1_FirstEntry_HasExpectedCoordinatesLuckAndType()
        {
            // level_1 红1：redPirate, teamIndex 0, gridX 17, gridY 10, luck 5
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(1));
            SpawnPlanEntry first = plan.Entries[0];

            Assert.AreEqual(0, first.TeamIndex);
            Assert.AreEqual("redPirate", first.TypeName);
            Assert.AreEqual(5, first.Luck);
            Assert.AreEqual(17, first.GridX);
            Assert.AreEqual(10, first.GridY);

            // 世界坐标 = GridToArena(17,10) = (17.5, UnitPivotHeight=0.25, 10.5)：
            // 3D 重投影后 gridY→纵深 Z、高度 Y 只有脚底贴地的 0.25 枢轴偏移。
            Assert.AreEqual(17.5f, first.WorldPosition.x, 1e-5f);
            Assert.AreEqual(LevelGeometry.UnitPivotHeight, first.WorldPosition.y, 1e-5f);
            Assert.AreEqual(10.5f, first.WorldPosition.z, 1e-5f);
        }

        [Test]
        public void Level1_Plan_UsesDepthSemanticsForXZDepth()
        {
            // level_1 heightTiles=17：3D 重投影后它是竞技场**纵深**（Z），字段名为 DepthTiles / WorldDepth。
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(1));
            Assert.AreEqual(50, plan.WidthTiles);
            Assert.AreEqual(17, plan.DepthTiles);
            Assert.AreEqual(50f, plan.WorldWidth, 1e-5f);
            Assert.AreEqual(17f, plan.WorldDepth, 1e-5f);
        }

        [Test]
        public void Level1_WaterWorldY_IsGlobalWaterSurfaceConstant()
        {
            // §4.4 3D 化：水面改为全局常量 WaterSurfaceY = -0.2，不再由关卡 waterTileY(=14) 推出。
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(1));
            Assert.AreEqual(LevelGeometry.WaterSurfaceY, plan.WaterWorldY, 1e-5f);
            Assert.AreEqual(-0.2f, plan.WaterWorldY, 1e-5f);
        }

        [Test]
        public void AllEntries_TeamIndexMatchesHardcodedRule()
        {
            // §4.3: teamIndex = (type == redPirate || redPirateCaptain) ? 0 : 1
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(1));
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                SpawnPlanEntry e = plan.Entries[i];
                Assert.AreEqual(CrewCatalog.TeamIndexOf(e.TypeName), e.TeamIndex,
                    "条目 " + e.TypeName + " 的队伍索引应与 §4.3 硬编码规则一致");
            }
        }

        [Test]
        public void AllEntries_HaveValidInitialWeaponReferences()
        {
            // 初始武器引用必须有效（WeaponCatalog 能查到），且列表非 null。
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(1));
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                SpawnPlanEntry e = plan.Entries[i];
                Assert.IsNotNull(e.InitialWeapons, "InitialWeapons 不允许为 null");
                Assert.IsNotEmpty(e.InitialWeapons, e.TypeName + " 应有初始武器");

                for (int w = 0; w < e.InitialWeapons.Count; w++)
                {
                    WeaponStack stack = e.InitialWeapons[w];
                    Assert.IsTrue(WeaponCatalog.TryGet(stack.id, out WeaponStats stats),
                        "武器 id 未登记到 WeaponCatalog: " + stack.id);
                    Assert.AreEqual(stack.id, stats.Id);
                    Assert.Greater(stack.count, 0);
                }
            }
        }

        [Test]
        public void Level1_Captain_HasFiniteDynamiteStack()
        {
            // redPirateCaptain：cherryBomb×10（无限）+ dynamite×5（有限）
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(1));
            SpawnPlanEntry captain = default;
            bool found = false;
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                if (plan.Entries[i].TypeName == "redPirateCaptain")
                {
                    captain = plan.Entries[i];
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "level_1 应含 redPirateCaptain");
            Assert.IsTrue(captain.InitialWeapons[0].IsInfinite, "cherryBomb×10 应为无限");
            Assert.AreEqual(5, captain.InitialWeapons[1].count, "dynamite×5 应为有限 5");
            Assert.IsFalse(captain.InitialWeapons[1].IsInfinite);
        }

        [Test]
        public void Level27_IsTwoPlayerMode()
        {
            // §7.2 level_27：2P 面板，redPirateCaptain vs bluePirateCaptain，各 1 人
            BattlePlan plan = LevelGeometry.BuildBattlePlan(LevelCatalog.Get(27));
            Assert.AreEqual(2, plan.OriginalXmlPlayers);
            Assert.AreEqual(1, plan.CountForTeam(0));
            Assert.AreEqual(1, plan.CountForTeam(1));
        }
    }
}
