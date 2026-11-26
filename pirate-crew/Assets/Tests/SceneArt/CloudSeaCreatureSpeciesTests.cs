using NUnit.Framework;
using PirateCrew.SceneArt.CloudSea;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 云端漫步水下生物物种表的纯 C# 用例（无头可跑）。
    ///
    /// 【覆盖】
    ///   · 对象名 → 物种：只认「前缀 + 物种 + 下划线 + 纯数字序号」；
    ///   · 物种 → 行为族：Blender 侧摆放表里的每个物种都在表里，且落在预期的行为族
    ///     （两边对不上时那只生物会停在原位，运行时只报错不中断——所以把清单钉在这里）；
    ///   · 朝向换算：让本地 +X 指向给定水平方向；
    ///   · 贴底生物的活动范围：对象不动、整只在骨架里挪，范围要给够（渲染器包围盒按它放大）。
    /// </summary>
    public class CloudSeaCreatureSpeciesTests
    {
        // ------------------------------------------------------------------
        // 对象名 → 物种
        // ------------------------------------------------------------------

        [Test]
        public void TryParse_AcceptsPrefixSpeciesAndIndex()
        {
            string species;
            Assert.IsTrue(CloudSeaCreatureSpecies.TryParse("Cloud_Creature_Sword_0", out species));
            Assert.AreEqual("Sword", species);
            Assert.IsTrue(CloudSeaCreatureSpecies.TryParse("Cloud_Creature_CrabWalk_12", out species));
            Assert.AreEqual("CrabWalk", species);
            Assert.IsTrue(CloudSeaCreatureSpecies.TryParse("Cloud_Creature_StarGold_3", out species));
            Assert.AreEqual("StarGold", species);
        }

        [Test]
        public void TryParse_RejectsMalformedNames()
        {
            string species;
            // 没有前缀 / 前缀不在开头
            Assert.IsFalse(CloudSeaCreatureSpecies.TryParse("Cloud_FishSchool_0", out species));
            Assert.IsFalse(CloudSeaCreatureSpecies.TryParse("Gull_Cloud_Creature_Sword_0", out species));
            // 缺序号或序号不是纯数字
            Assert.IsFalse(CloudSeaCreatureSpecies.TryParse("Cloud_Creature_Sword", out species));
            Assert.IsFalse(CloudSeaCreatureSpecies.TryParse("Cloud_Creature_Sword_", out species));
            Assert.IsFalse(CloudSeaCreatureSpecies.TryParse("Cloud_Creature_Sword_big", out species));
            Assert.IsFalse(CloudSeaCreatureSpecies.TryParse("Cloud_Creature_Sword_3a", out species));
            Assert.IsNull(species);
            Assert.IsFalse(CloudSeaCreatureSpecies.TryParse(null, out species));
        }

        // ------------------------------------------------------------------
        // 物种 → 行为族
        // ------------------------------------------------------------------

        [Test]
        public void KindOf_EverySpeciesOfTheBlenderPlacement_IsKnown()
        {
            // 清单 = tools/blender/scene/cloudsea/mod_creatures.py 的 SCHOOLS / SINGLES / BOTTOM。
            var expected = new System.Collections.Generic.Dictionary<string, CloudSeaCreatureKind>
            {
                { "Clownfish", CloudSeaCreatureKind.School },
                { "Tang", CloudSeaCreatureKind.School },
                { "YellowTang", CloudSeaCreatureKind.School },
                { "Bait", CloudSeaCreatureKind.School },
                { "Sword", CloudSeaCreatureKind.Swimmer },
                { "Manta", CloudSeaCreatureKind.Swimmer },
                { "Turtle", CloudSeaCreatureKind.Swimmer },
                { "Puffer", CloudSeaCreatureKind.Swimmer },
                { "Jelly", CloudSeaCreatureKind.Jelly },
                { "CrabWalk", CloudSeaCreatureKind.Bottom },
                { "Star", CloudSeaCreatureKind.Bottom },
                { "CrabWave", CloudSeaCreatureKind.Bottom },
                { "Octopus", CloudSeaCreatureKind.Bottom },
                { "Seahorse", CloudSeaCreatureKind.Bottom },
                { "Anemone", CloudSeaCreatureKind.Bottom },
                { "StarGold", CloudSeaCreatureKind.Bottom },
            };
            foreach (var pair in expected)
            {
                Assert.AreEqual(pair.Value, CloudSeaCreatureSpecies.KindOf(pair.Key), pair.Key);
                CloudSeaCreatureSpecies.Profile profile;
                Assert.IsTrue(CloudSeaCreatureSpecies.TryGet(pair.Key, out profile), pair.Key);
            }
        }

        [Test]
        public void KindOf_UnknownSpecies_IsUnknown()
        {
            Assert.AreEqual(CloudSeaCreatureKind.Unknown, CloudSeaCreatureSpecies.KindOf("Shark"));
            Assert.AreEqual(CloudSeaCreatureKind.Unknown, CloudSeaCreatureSpecies.KindOf(null));
            CloudSeaCreatureSpecies.Profile profile;
            Assert.IsFalse(CloudSeaCreatureSpecies.TryGet("Shark", out profile));
        }

        [Test]
        public void Profiles_SwimmersStayInsideTheWaterColumn()
        {
            // 岩架：脊顶沙面 -2.25、水面 -0.4。剑鱼 / 魔鬼鱼 / 海龟 / 河豚的「上伸 + 下伸」
            // 加上两侧余量后必须小于这 1.85 m 的水柱，否则它们在沙脊上会被上下沿夹死、原地抖。
            const float waterColumn = 1.85f;
            const float surfaceClearance = 0.08f;
            const float bedClearance = 0.12f;
            string[] swimmers = { "Sword", "Manta", "Turtle", "Puffer", "Jelly" };
            foreach (string species in swimmers)
            {
                CloudSeaCreatureSpecies.Profile profile;
                Assert.IsTrue(CloudSeaCreatureSpecies.TryGet(species, out profile), species);
                float needed = profile.Top + profile.Bottom + surfaceClearance + bedClearance;
                Assert.Less(needed, waterColumn, species + " 上下伸出 {0:F2} m 超过脊顶处的水柱", needed);
            }
        }

        [Test]
        public void Profiles_BottomCreatures_HaveRoomToRoam()
        {
            // 贴底生物的对象不动，整只在骨架里挪；范围（半径）盖不住控制器里的巡逻 / 慢逛常数时，
            // 它会走出被放大的渲染器包围盒、被剔除掉。蟹整只缩到一半，巡逻半径 0.45，加壳与腿的伸出。
            Assert.GreaterOrEqual(CloudSeaCreatureSpecies.TryGet("CrabWalk", out var crab) ? crab.Radius : 0f, 0.55f);
            Assert.GreaterOrEqual(CloudSeaCreatureSpecies.TryGet("Octopus", out var octopus) ? octopus.Radius : 0f, 1.5f);
            Assert.GreaterOrEqual(CloudSeaCreatureSpecies.TryGet("Star", out var star) ? star.Radius : 0f, 0.8f);
        }

        // ------------------------------------------------------------------
        // 朝向换算
        // ------------------------------------------------------------------

        [Test]
        public void HeadingOf_TurnsLocalForwardOntoTheGivenDirection()
        {
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, CloudSeaCreatureSpecies.HeadingOf(1f, 0f)), 1e-4f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(180f, CloudSeaCreatureSpecies.HeadingOf(-1f, 0f)), 1e-4f);
            // Unity 里绕 Y 转 +90° 把 +X 转到 -Z：朝 -Z 是 +90°，朝 +Z 是 -90°。
            Assert.AreEqual(0f, Mathf.DeltaAngle(90f, CloudSeaCreatureSpecies.HeadingOf(0f, -1f)), 1e-4f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(-90f, CloudSeaCreatureSpecies.HeadingOf(0f, 1f)), 1e-4f);
        }

    }
}
