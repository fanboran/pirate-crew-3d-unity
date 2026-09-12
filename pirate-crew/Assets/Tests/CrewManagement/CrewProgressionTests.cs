using NUnit.Framework;
using PirateCrew.CrewManagement;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 船员经验/升级曲线的纯 C# 断言。
    /// ⚠ 曲线本身是**提案/待定**（Flash 与原版均无船员成长系统，见 <see cref="CrewProgressionRules"/> 类头），
    ///   这里锁的是「实现与提案数值一致、且边界不崩」，不代表玩法已验收。
    /// </summary>
    public class CrewProgressionTests
    {
        [Test]
        public void XpToNextLevel_StepsUpByHundred()
        {
            Assert.That(CrewProgressionRules.XpToNextLevel(1), Is.EqualTo(100));
            Assert.That(CrewProgressionRules.XpToNextLevel(2), Is.EqualTo(200));
            Assert.That(CrewProgressionRules.XpToNextLevel(9), Is.EqualTo(900));
            Assert.That(CrewProgressionRules.XpToNextLevel(CrewProgressionRules.MaxLevel), Is.EqualTo(0), "满级不再升级");
        }

        [Test]
        public void TotalXpForLevel_Accumulates()
        {
            Assert.That(CrewProgressionRules.TotalXpForLevel(1), Is.EqualTo(0));
            Assert.That(CrewProgressionRules.TotalXpForLevel(2), Is.EqualTo(100));
            Assert.That(CrewProgressionRules.TotalXpForLevel(3), Is.EqualTo(300));
        }

        [Test]
        public void LevelForXp_BoundariesAndCap()
        {
            Assert.That(CrewProgressionRules.LevelForXp(0), Is.EqualTo(1));
            Assert.That(CrewProgressionRules.LevelForXp(99), Is.EqualTo(1));
            Assert.That(CrewProgressionRules.LevelForXp(100), Is.EqualTo(2), "恰好 100 升级");
            Assert.That(CrewProgressionRules.LevelForXp(299), Is.EqualTo(2));
            Assert.That(CrewProgressionRules.LevelForXp(300), Is.EqualTo(3));
            Assert.That(CrewProgressionRules.LevelForXp(int.MaxValue), Is.EqualTo(CrewProgressionRules.MaxLevel),
                "经验再多也不超过等级上限");
        }

        [Test]
        public void XpAward_FollowsStarCount()
        {
            Assert.That(CrewProgressionRules.XpAward(0), Is.EqualTo(0), "未通关不发经验");
            Assert.That(CrewProgressionRules.XpAward(-1), Is.EqualTo(0));
            Assert.That(CrewProgressionRules.XpAward(1), Is.EqualTo(150));
            Assert.That(CrewProgressionRules.XpAward(2), Is.EqualTo(200));
            Assert.That(CrewProgressionRules.XpAward(3), Is.EqualTo(250));
        }

        [Test]
        public void GrantXp_ReturnsLevelsGained()
        {
            var progression = new CrewProgression();

            Assert.That(progression.GrantXp("sailor", 0), Is.EqualTo(0), "非正数忽略");
            Assert.That(progression.GetXp("sailor"), Is.EqualTo(0));

            Assert.That(progression.GrantXp("sailor", 100), Is.EqualTo(1));
            Assert.That(progression.GetLevel("sailor"), Is.EqualTo(2));
            Assert.That(progression.GetXp("sailor"), Is.EqualTo(100));

            Assert.That(progression.GrantXp("sailor", 100), Is.EqualTo(0), "还差 100 才到 3 级");
            Assert.That(progression.GrantXp("sailor", 100), Is.EqualTo(1));
            Assert.That(progression.GetLevel("sailor"), Is.EqualTo(3));
        }

        [Test]
        public void LevelProgress_IsZeroToOne()
        {
            var progression = new CrewProgression();

            Assert.That(progression.GetLevelProgress("sailor"), Is.EqualTo(0f));
            progression.GrantXp("sailor", 50);
            Assert.That(progression.GetLevelProgress("sailor"), Is.EqualTo(0.5f).Within(0.0001f));

            progression.SetXp("sailor", CrewProgressionRules.TotalXpForLevel(CrewProgressionRules.MaxLevel));
            Assert.That(progression.GetLevelProgress("sailor"), Is.EqualTo(1f), "满级进度归 1");
        }

        [Test]
        public void SetXp_ClampsNegativeAndIgnoresEmptyId()
        {
            var progression = new CrewProgression();

            progression.SetXp("sailor", -50);
            Assert.That(progression.GetXp("sailor"), Is.EqualTo(0));

            progression.SetXp(null, 500);
            progression.SetXp(string.Empty, 500);
            Assert.That(progression.CrewIds.Count, Is.EqualTo(1), "空 id 不应写入账本");
        }

        [Test]
        public void UntrackedCrew_ReportsLevelOne()
        {
            var progression = new CrewProgression();

            Assert.That(progression.GetXp("gunner"), Is.EqualTo(0));
            Assert.That(progression.GetLevel("gunner"), Is.EqualTo(1));
        }
    }
}
