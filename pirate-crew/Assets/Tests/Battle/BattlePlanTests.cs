using NUnit.Framework;
using PirateCrew.Data;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 出战计划生成测试（§4.3 坐标/队伍、§4.1 luck、§5.5 初始武器、§4.4 全局水面常量）。
    /// 坐标按 3D 重投影语义（gridY→Z 纵深），数据源 = 样板第 1 关的
    /// <see cref="ShowcaseLevels"/> 数据（一代退场后 BuildBattlePlan 的纯 C# 入口），无头可跑。
    /// 关卡 2「碎岛雨」已删除（2026-09-22），本文件只覆盖关卡 1。
    /// </summary>
    [TestFixture]
    public class BattlePlanTests
    {
        /// <summary>样板第 1 关（云端漫步）的出战数据。</summary>
        static LevelData Showcase1() => ShowcaseLevels.BuildLevelData(1).Value;

        [Test]
        public void Showcase1_Plan_HasExpectedTeamSplit()
        {
            // 云端漫步（教学关，设计文档 L01 §4）：3 redPirate + 1 redPirateCaptain（红4，玩家优势）
            // vs 2 cabinBoy + 1 cabinBoyCaptain（蓝3）。
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());

            Assert.AreEqual(7, plan.Entries.Count);
            Assert.AreEqual(4, plan.CountForTeam(0));
            Assert.AreEqual(3, plan.CountForTeam(1));
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
        public void Showcase1_TeachingLoadout_CherryBombOnly()
        {
            // 教学关武器收敛（设计文档 L01 §5）：全员只有 cherryBomb×10（无限）一种初始武器；
            // 空投池 = {Dynamite}——不提前引入引爆时机/操控类机制。
            BattlePlan plan = LevelGeometry.BuildBattlePlan(Showcase1());

            for (int i = 0; i < plan.Entries.Count; i++)
            {
                SpawnPlanEntry e = plan.Entries[i];
                Assert.AreEqual(1, e.InitialWeapons.Count, e.TypeName + " 教学关应只有一种初始武器");
                Assert.AreEqual(WeaponId.CherryBomb, e.InitialWeapons[0].id, e.TypeName + " 初始武器应为 cherryBomb");
                Assert.IsTrue(e.InitialWeapons[0].IsInfinite, "cherryBomb×10 应为无限");
            }

            Assert.AreEqual(1, Showcase1().PotentialWeapons.Count, "空投池应恰 1 种");
            Assert.AreEqual(WeaponId.Dynamite, Showcase1().PotentialWeapons[0].id, "空投池应为 dynamite");
        }
    }
}
