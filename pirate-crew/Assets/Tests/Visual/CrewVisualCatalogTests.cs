using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Visual.Tests
{
    /// <summary>
    /// <see cref="CrewVisualCatalog"/> 断言：调色板与材质角色。
    /// （2026-10-05 创始人裁决：职业外观塌缩为单一档，符号/名册 → 外观档映射测试随之根除；
    /// 战斗符号的 UI 短名映射由 <c>UiSkin.CrewKey</c> 独立承担，见 UiSkinTests。）
    /// </summary>
    [TestFixture]
    public class CrewVisualCatalogTests
    {
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
