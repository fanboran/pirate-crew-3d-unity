using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Visual.Tests
{
    /// <summary>
    /// <see cref="CrewVisualCatalog"/> 断言：符号/名册 id → 职业外观档映射、中文名、调色板。
    /// 映射出处 docs/角色造型规范.md §3.1「母题来源」列（§8.4 标【待定】：职业与原版外观的映射未拍板）。
    /// </summary>
    [TestFixture]
    public class CrewVisualCatalogTests
    {
        [Test]
        public void ProfessionFromBattleSymbol_MapsMotherThemes()
        {
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromBattleSymbol("redPirate"));
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromBattleSymbol("bluePirate"));
            Assert.AreEqual(CrewProfession.Bombardier, CrewVisualCatalog.ProfessionFromBattleSymbol("soldier"));
            Assert.AreEqual(CrewProfession.Sniper, CrewVisualCatalog.ProfessionFromBattleSymbol("femalePirate"));
            Assert.AreEqual(CrewProfession.Sniper, CrewVisualCatalog.ProfessionFromBattleSymbol("cabinBoy"));
            Assert.AreEqual(CrewProfession.Hook, CrewVisualCatalog.ProfessionFromBattleSymbol("blindPirate"));
            Assert.AreEqual(CrewProfession.Arsonist, CrewVisualCatalog.ProfessionFromBattleSymbol("oldPirate"));
            Assert.AreEqual(CrewProfession.Skeleton, CrewVisualCatalog.ProfessionFromBattleSymbol("skeletonPirate"));
        }

        [Test]
        public void ProfessionFromBattleSymbol_CaptainVariantsWin()
        {
            // 任何含 "Captain" 的符号都归船长档（redPirateCaptain / cabinBoyCaptain / skeletonPirateCaptain…）。
            Assert.AreEqual(CrewProfession.Captain, CrewVisualCatalog.ProfessionFromBattleSymbol("redPirateCaptain"));
            Assert.AreEqual(CrewProfession.Captain, CrewVisualCatalog.ProfessionFromBattleSymbol("bluePirateCaptain"));
            Assert.AreEqual(CrewProfession.Captain, CrewVisualCatalog.ProfessionFromBattleSymbol("soldierCaptain"));
            Assert.AreEqual(CrewProfession.Captain, CrewVisualCatalog.ProfessionFromBattleSymbol("cabinBoyCaptain"));
        }

        [Test]
        public void ProfessionFromBattleSymbol_UnknownFallsBackToSailor()
        {
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromBattleSymbol(null));
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromBattleSymbol(""));
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromBattleSymbol("parrot"));
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromBattleSymbol("monkey"));
        }

        [Test]
        public void ProfessionFromRosterId_MapsSixRecruits()
        {
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromRosterId("sailor"));
            Assert.AreEqual(CrewProfession.Bombardier, CrewVisualCatalog.ProfessionFromRosterId("gunner"));
            Assert.AreEqual(CrewProfession.Sniper, CrewVisualCatalog.ProfessionFromRosterId("sniper"));
            Assert.AreEqual(CrewProfession.Hook, CrewVisualCatalog.ProfessionFromRosterId("hooker"));
            Assert.AreEqual(CrewProfession.Arsonist, CrewVisualCatalog.ProfessionFromRosterId("arsonist"));
            Assert.AreEqual(CrewProfession.Skeleton, CrewVisualCatalog.ProfessionFromRosterId("skeleton"));
            Assert.AreEqual(CrewProfession.Sailor, CrewVisualCatalog.ProfessionFromRosterId("nobody"));
        }

        [Test]
        public void AllProfessions_AreSevenWithUniqueNames()
        {
            Assert.AreEqual(7, CrewVisualCatalog.AllProfessions.Length);
            Assert.AreEqual(7, CrewVisualCatalog.ProfessionCount);

            var names = new System.Collections.Generic.HashSet<string>();
            var files = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < CrewVisualCatalog.AllProfessions.Length; i++)
            {
                CrewProfession p = CrewVisualCatalog.AllProfessions[i];
                Assert.IsTrue(names.Add(CrewVisualCatalog.DisplayName(p)), "中文名重复: " + p);
                Assert.IsTrue(files.Add(CrewVisualCatalog.PrefabFileName(p)), "预制体文件名重复: " + p);
            }

            Assert.AreEqual("水手", CrewVisualCatalog.DisplayName(CrewProfession.Sailor));
            Assert.AreEqual("骷髅海盗", CrewVisualCatalog.DisplayName(CrewProfession.Skeleton));
            Assert.AreEqual("船长", CrewVisualCatalog.DisplayName(CrewProfession.Captain));
        }

        [Test]
        public void TeamColors_MatchStaticDoc()
        {
            // 静态文档:721 红 #FF3A29 / 蓝 #3366FF。
            Assert.That(CrewVisualCatalog.TeamRed.r, Is.EqualTo(1.000f).Within(0.01f));
            Assert.That(CrewVisualCatalog.TeamRed.g, Is.EqualTo(0.228f).Within(0.01f));
            Assert.That(CrewVisualCatalog.TeamRed.b, Is.EqualTo(0.161f).Within(0.01f));

            Assert.That(CrewVisualCatalog.TeamBlue.r, Is.EqualTo(0.200f).Within(0.01f));
            Assert.That(CrewVisualCatalog.TeamBlue.g, Is.EqualTo(0.400f).Within(0.01f));
            Assert.That(CrewVisualCatalog.TeamBlue.b, Is.EqualTo(1.000f).Within(0.01f));
        }

        [Test]
        public void RoleColors_AreDistinctEnoughForMaterialLayering()
        {
            // 造型规范 §2.1 纪律：阵营色不得污染肤色/铁/木/皮革 —— 至少保证它们不是同一个色。
            Color[] colors =
            {
                CrewVisualCatalog.Skin, CrewVisualCatalog.Iron, CrewVisualCatalog.Wood,
                CrewVisualCatalog.Leather, CrewVisualCatalog.Bone, CrewVisualCatalog.Brass,
            };
            for (int i = 0; i < colors.Length; i++)
            {
                for (int j = i + 1; j < colors.Length; j++)
                {
                    float distance = Mathf.Abs(colors[i].r - colors[j].r)
                                     + Mathf.Abs(colors[i].g - colors[j].g)
                                     + Mathf.Abs(colors[i].b - colors[j].b);
                    Assert.Greater(distance, 0.05f, "材质色过近，会读出'一团色'");
                }
            }

            // 粗糙度分区：布料（极哑）与铁/黄铜至少差 0.15（Art Bible §3.2 纪律 1）。
            Assert.Greater(CrewVisualCatalog.RoleSmoothness(CrewMaterialRole.Iron)
                           - CrewVisualCatalog.RoleSmoothness(CrewMaterialRole.TeamCloth), 0.15f);
        }

        [Test]
        public void RoleColor_IndexedByEnum()
        {
            Assert.AreEqual(CrewVisualCatalog.TeamRed, CrewVisualCatalog.RoleColor(CrewMaterialRole.TeamCloth));
            Assert.AreEqual(CrewVisualCatalog.Bone, CrewVisualCatalog.RoleColor(CrewMaterialRole.Bone));
            Assert.AreEqual("Crew" + CrewMaterialRole.Flame, CrewVisualCatalog.MaterialFileName(CrewMaterialRole.Flame));
        }
    }
}
