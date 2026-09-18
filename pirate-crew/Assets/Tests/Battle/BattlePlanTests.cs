using NUnit.Framework;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 出战计划生成测试（§4.3 坐标/队伍、§4.1 luck、§5.5 初始武器、§4.4 全局水面常量）。
    /// 坐标按 3D 重投影语义（gridY→Z 纵深），数据源 = 样板第 1/2 关的手写
    /// <see cref="ShowcaseLevels"/> 数据（一代退场后 BuildBattlePlan 的纯 C# 入口），无头可跑。
    /// </summary>
    [TestFixture]
    public class BattlePlanTests
    {
        /// <summary>样板第 1 关（云端漫步）的出战数据。</summary>
        static LevelData Showcase1() => ShowcaseLevels.BuildLevelData(1).Value;

        [Test]
        public void Showcase1_Plan_HasExpectedTeamSplit()
        {
            // 云端漫步：3 redPirate + 1 redPirateCaptain（红4） vs 3 cabinBoy + 1 cabinBoyCaptain（蓝4）
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());

            Assert.AreEqual(8, plan.Entries.Count);
            Assert.AreEqual(4, plan.CountForTeam(0));
            Assert.AreEqual(4, plan.CountForTeam(1));
            Assert.AreEqual(1, plan.OriginalXmlPlayers);
            Assert.AreEqual(ShowcaseLevels.FirstLevel, plan.LevelNumber);
        }

        [Test]
        public void Showcase1_FirstEntry_HasExpectedCoordinatesLuckAndType()
        {
            // 云端漫步 红1：redPirate, teamIndex 0, gridX 8, gridY 6, luck 5
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());
            SpawnPlanEntry first = plan.Entries[0];

            Assert.AreEqual(0, first.TeamIndex);
            Assert.AreEqual("redPirate", first.TypeName);
            Assert.AreEqual(5, first.Luck);
            Assert.AreEqual(8, first.GridX);
            Assert.AreEqual(6, first.GridY);

            // 世界坐标 = GridToArena(8,6) = ((8+0.5)×2, UnitPivotHeight=0.5, (6+0.5)×2)
            // = (17, 0.5, 13)：3D 重投影后 gridY→纵深 Z、高度 Y 只有脚底贴地的 0.5 枢轴偏移
            //（格 1→2 单位后格心 = (格号+0.5)×TileWorldSize，见 LevelGeometry.cs:313）。
            Assert.AreEqual(17f, first.WorldPosition.x, 1e-5f);
            Assert.AreEqual(LevelGeometry.UnitPivotHeight, first.WorldPosition.y, 1e-5f);
            Assert.AreEqual(13f, first.WorldPosition.z, 1e-5f);
        }

        [Test]
        public void Showcase1_Plan_UsesDepthSemanticsForXZDepth()
        {
            // 样板场地 20×15：3D 重投影后 heightTiles 是竞技场**纵深**（Z），字段名为 DepthTiles / WorldDepth。
            // 世界尺寸 = 格数 × TileWorldSize(2)（LevelGeometry.TileToWorld，见 BattlePlan 构造）。
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());
            Assert.AreEqual(ShowcaseLevels.WidthTiles, plan.WidthTiles);
            Assert.AreEqual(ShowcaseLevels.DepthTiles, plan.DepthTiles);
            Assert.AreEqual(40f, plan.WorldWidth, 1e-5f);
            Assert.AreEqual(30f, plan.WorldDepth, 1e-5f);
        }

        [Test]
        public void Showcase1_WaterWorldY_IsGlobalWaterSurfaceConstant()
        {
            // §4.4 3D 化：水面改为全局常量 WaterSurfaceY = -0.4（LevelGeometry.cs），不再由 waterTileY 推出。
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());
            Assert.AreEqual(LevelGeometry.WaterSurfaceY, plan.WaterWorldY, 1e-5f);
            Assert.AreEqual(-0.4f, plan.WaterWorldY, 1e-5f);
        }

        [Test]
        public void AllEntries_TeamIndexMatchesHardcodedRule()
        {
            // §4.3: teamIndex = (type == redPirate || redPirateCaptain) ? 0 : 1
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());
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
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());
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
        public void Showcase1_Captain_HasFiniteSecondWeaponStack()
        {
            // redPirateCaptain：cherryBomb×10（无限）+ banana×6（有限）
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());
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

            Assert.IsTrue(found, "云端漫步应含 redPirateCaptain");
            Assert.IsTrue(captain.InitialWeapons[0].IsInfinite, "cherryBomb×10 应为无限");
            Assert.AreEqual(6, captain.InitialWeapons[1].count, "banana×6 应为有限 6");
            Assert.IsFalse(captain.InitialWeapons[1].IsInfinite);
        }

        [Test]
        public void Showcase2_Plan_BuildsThreeVsThree()
        {
            // 第二份数据集交叉验证（双雄并舷）：3 v 3，水面常量同源。
            LevelData data = ShowcaseLevels.BuildLevelData(2).Value;
            BattlePlan plan = LevelGeometry.BuildBattlePlan(data);

            Assert.AreEqual(6, plan.Entries.Count);
            Assert.AreEqual(3, plan.CountForTeam(0));
            Assert.AreEqual(3, plan.CountForTeam(1));
            Assert.AreEqual(LevelGeometry.WaterSurfaceY, plan.WaterWorldY, 1e-5f);
        }
    }
}
