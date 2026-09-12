using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// CrewCatalog 的纯 C# 断言（对应静态逆向文档 §4.1 / §4.2 / §4.3 / §3.2）。
    /// </summary>
    public class CrewCatalogTests
    {
        // ------------------------------------------------------------------
        // §4.1 共享属性逐值
        // ------------------------------------------------------------------

        [Test]
        public void SharedAttributes_MatchDocument()
        {
            Assert.That(CrewCatalog.MaxHealth, Is.EqualTo(100));
            Assert.That(CrewCatalog.LeftExtent, Is.EqualTo(6f));
            Assert.That(CrewCatalog.RightExtent, Is.EqualTo(6f));
            Assert.That(CrewCatalog.TopExtent, Is.EqualTo(8f));
            Assert.That(CrewCatalog.BottomExtent, Is.EqualTo(8f));
            Assert.That(CrewCatalog.Weight, Is.EqualTo(1f));
            Assert.That(CrewCatalog.Friction, Is.EqualTo(2f));
            Assert.That(CrewCatalog.Bounce, Is.EqualTo(0.2f));
            Assert.That(CrewCatalog.TwangMaxForce, Is.EqualTo(20f));
            Assert.That(CrewCatalog.DragRange, Is.EqualTo(130f));
            Assert.That(CrewCatalog.DragOffset, Is.EqualTo(-100f));
            Assert.That(CrewCatalog.DefaultLuck, Is.EqualTo(5));
        }

        [Test]
        public void SharedStats_MatchesConstants()
        {
            CrewStats s = CrewCatalog.SharedStats;
            Assert.That(s.MaxHealth, Is.EqualTo(100));
            Assert.That(s.Weight, Is.EqualTo(1f));
            Assert.That(s.Friction, Is.EqualTo(2f));
            Assert.That(s.Bounce, Is.EqualTo(0.2f));
            Assert.That(s.TwangMaxForce, Is.EqualTo(20f));
            Assert.That(s.DragRange, Is.EqualTo(130f));
            Assert.That(s.DragOffset, Is.EqualTo(-100f));
            Assert.That(s.Luck, Is.EqualTo(5));
        }

        // ------------------------------------------------------------------
        // §4.3 队伍归属
        // ------------------------------------------------------------------

        [Test]
        public void TeamIndexOf_RedPirates_IsTeam0()
        {
            Assert.That(CrewCatalog.TeamIndexOf("redPirate"), Is.EqualTo(0));
            Assert.That(CrewCatalog.TeamIndexOf("redPirateCaptain"), Is.EqualTo(0));
        }

        [Test]
        public void TeamIndexOf_BlueAndOthers_IsTeam1()
        {
            Assert.That(CrewCatalog.TeamIndexOf("bluePirate"), Is.EqualTo(1));
            Assert.That(CrewCatalog.TeamIndexOf("bluePirateCaptain"), Is.EqualTo(1));
            Assert.That(CrewCatalog.TeamIndexOf("bossGuy"), Is.EqualTo(1));
            Assert.That(CrewCatalog.TeamIndexOf("skeletonPirate"), Is.EqualTo(1));
        }

        [Test]
        public void TeamIndexOf_UnknownOrNull_FallsBackToTeam1()
        {
            // §4.3 的 else 分支兜底为 team2：未知 / 拼错 / null / 空串都返回 1，不在数据层抛错。
            Assert.That(CrewCatalog.TeamIndexOf("notARealPirate"), Is.EqualTo(1));
            Assert.That(CrewCatalog.TeamIndexOf("RedPirate"), Is.EqualTo(1), "比较大小写敏感（Ordinal）");
            Assert.That(CrewCatalog.TeamIndexOf(""), Is.EqualTo(1));
            Assert.That(CrewCatalog.TeamIndexOf(null), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // §4.2 导出符号名单
        // ------------------------------------------------------------------

        [Test]
        public void ExportSymbols_CountAndUniqueness()
        {
            // 见 CrewCatalog.ExportSymbols 的说明：§4.2 展开 Captain 变体后共 27 个，
            // 与任务书写的「21 个」不一致，以文档为准。
            Assert.That(CrewCatalog.ExportSymbolCount, Is.EqualTo(27));

            var seen = new HashSet<string>();
            foreach (string symbol in CrewCatalog.ExportSymbols)
                Assert.That(seen.Add(symbol), Is.True, "导出符号重复: " + symbol);
        }

        [Test]
        public void ExportSymbols_ContainKnownNames()
        {
            var set = new HashSet<string>(CrewCatalog.ExportSymbols);
            foreach (string expected in new[]
            {
                "redPirate", "redPirateCaptain", "bluePirate", "bluePirateCaptain",
                "cabinBoy", "cabinBoyCaptain", "soldierCaptain", "skeletonPirateCaptain",
                "tribeChief", "monkey", "crab", "shark", "squid", "parrot",
                "bossGuy", "bossGuyZombie",
            })
            {
                Assert.That(set.Contains(expected), Is.True, "导出符号名单缺少: " + expected);
            }
        }

        // ------------------------------------------------------------------
        // §3.2 保底武器
        // ------------------------------------------------------------------

        [Test]
        public void FallbackWeapon_IsCannonball()
        {
            Assert.That(CrewCatalog.FallbackWeapon, Is.EqualTo(WeaponId.Cannonball));
            Assert.That(CrewCatalog.InfiniteWeaponCount, Is.EqualTo(10));
        }

        [Test]
        public void EnsureFallbackWeapon_EmptyStack_AddsCannonball()
        {
            var hasWeapons = new List<WeaponId>();
            bool added = CrewCatalog.EnsureFallbackWeapon(hasWeapons);

            Assert.That(added, Is.True);
            Assert.That(hasWeapons, Is.EqualTo(new[] { WeaponId.Cannonball }));
        }

        [Test]
        public void EnsureFallbackWeapon_NonEmptyStack_LeavesUnchanged()
        {
            var hasWeapons = new List<WeaponId> { WeaponId.Banana };
            bool added = CrewCatalog.EnsureFallbackWeapon(hasWeapons);

            Assert.That(added, Is.False);
            Assert.That(hasWeapons, Is.EqualTo(new[] { WeaponId.Banana }));
        }
    }
}
