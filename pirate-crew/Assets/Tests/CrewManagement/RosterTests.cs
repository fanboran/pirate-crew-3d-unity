using NUnit.Framework;
using PirateCrew.CrewManagement;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 船员名册/编成的纯 C# 断言。
    /// 【基准】Godot <c>modules/crew_management/scripts/roster.gd</c>：
    ///   初始 <c>["sailor"]</c>、<c>max_roster_size = 4</c>、<c>set_active_roster</c> 校验「数量 ≤ 上限 + 必须已解锁」。
    /// </summary>
    public class RosterTests
    {
        [Test]
        public void NewRoster_StartsWithInitialCrewUnlockedAndActive()
        {
            var roster = new Roster();

            Assert.That(roster.MaxSize, Is.EqualTo(4), "roster.gd:10 max_roster_size = 4");
            Assert.That(roster.IsUnlocked(CrewRosterCatalog.InitialCrewId), Is.True, "roster.gd:12 默认解锁水手");
            Assert.That(roster.UnlockedCount, Is.EqualTo(CrewRosterCatalog.InitialCrewIds.Count));
            Assert.That(roster.Active, Is.EquivalentTo(CrewRosterCatalog.InitialCrewIds));
        }

        [Test]
        public void Recruit_UnknownId_Fails()
        {
            var roster = new Roster();

            Assert.That(roster.Recruit("notACrew"), Is.False);
            Assert.That(roster.Recruit(null), Is.False);
            Assert.That(roster.Recruit(string.Empty), Is.False);
            Assert.That(roster.UnlockedCount, Is.EqualTo(1));
        }

        [Test]
        public void Recruit_AddsCrewOnce()
        {
            var roster = new Roster();

            Assert.That(roster.Recruit("gunner"), Is.True);
            Assert.That(roster.Recruit("gunner"), Is.False, "重复招募应无效（Godot unlock_crew 的 has 去重）");
            Assert.That(roster.UnlockedCount, Is.EqualTo(2));
            Assert.That(roster.IsActive("gunner"), Is.False, "新招募不应挤占已有编成");
        }

        [Test]
        public void SetActive_AcceptsSubsetOfUnlocked()
        {
            var roster = new Roster();
            roster.Recruit("gunner");
            roster.Recruit("sniper");

            Assert.That(roster.SetActive(new[] { "gunner", "sniper" }), Is.True);
            Assert.That(roster.Active, Is.EqualTo(new[] { "gunner", "sniper" }));
        }

        [Test]
        public void SetActive_RejectsOverCapacity()
        {
            var roster = new Roster(maxSize: 2);
            roster.Recruit("gunner");

            Assert.That(roster.SetActive(new[] { "sailor", "gunner", "sniper" }), Is.False,
                "超过编成上限必须拒绝（roster.gd:26）");
            Assert.That(roster.Active, Is.EqualTo(new[] { "sailor" }), "被拒绝时不得改动原阵容");
        }

        [Test]
        public void SetActive_RejectsLockedCrew()
        {
            var roster = new Roster();

            Assert.That(roster.SetActive(new[] { "sailor", "sniper" }), Is.False, "未拥有 → 拒绝（roster.gd:29）");
        }

        [Test]
        public void SetActive_RejectsDuplicatesAndEmptyIds()
        {
            var roster = new Roster();
            roster.Recruit("gunner");

            Assert.That(roster.SetActive(new[] { "sailor", "sailor" }), Is.False, "重复 id（本作补强校验）");
            Assert.That(roster.SetActive(new[] { "sailor", string.Empty }), Is.False, "空 id（本作补强校验）");
            Assert.That(roster.SetActive(null), Is.False);
        }

        [Test]
        public void SetActive_AllowsEmptyRoster()
        {
            var roster = new Roster();

            Assert.That(roster.SetActive(new string[0]), Is.True, "Godot 版允许空阵容（minimum 是 UI 的规则）");
            Assert.That(roster.Active, Is.Empty);
        }

        [Test]
        public void AddToActive_StopsAtCapacity()
        {
            var roster = new Roster(maxSize: 2);
            roster.Recruit("gunner");
            roster.Recruit("sniper");

            Assert.That(roster.AddToActive("gunner"), Is.True);
            Assert.That(roster.AddToActive("sniper"), Is.False, "已达上限");
            Assert.That(roster.AddToActive("gunner"), Is.False, "已在阵容里");
            Assert.That(roster.AddToActive("hooker"), Is.False, "未拥有");

            Assert.That(roster.RemoveFromActive("gunner"), Is.True);
            Assert.That(roster.AddToActive("sniper"), Is.True);
            Assert.That(roster.RemoveFromActive("sniper"), Is.True);
            Assert.That(roster.RemoveFromActive("sniper"), Is.False, "重复移除应为 false");
        }

        [Test]
        public void Reset_RestoresInitialState()
        {
            var roster = new Roster();
            roster.Recruit("gunner");
            roster.SetActive(new[] { "gunner" });

            roster.Reset();

            Assert.That(roster.UnlockedCount, Is.EqualTo(1));
            Assert.That(roster.Active, Is.EqualTo(new[] { "sailor" }));
        }

        [Test]
        public void Copies_DoNotExposeInternalLists()
        {
            var roster = new Roster();

            string[] active = roster.ActiveCopy();
            active[0] = "mutated";

            Assert.That(roster.Active[0], Is.EqualTo("sailor"), "外部改拷贝不应影响内部状态");
        }
    }
}
