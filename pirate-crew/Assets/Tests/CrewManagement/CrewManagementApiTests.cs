using NUnit.Framework;
using PirateCrew.Core;
using PirateCrew.CrewManagement;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 船员管理跨模块出口（<see cref="CrewManagementApi"/>）的事件广播与存档往返（纯 C#，不碰 MonoBehaviour）。
    /// </summary>
    public class CrewManagementApiTests
    {
        int _rosterUpdatedCount;
        string _lastUnlockedId;
        CrewRewardPayload? _lastReward;

        [SetUp]
        public void SetUp()
        {
            // 静态状态隔离（与 EventBusTests / SaveManagerTests 同一约定）。
            EventBus.ClearAll();
            CrewManagementApi.Reset();

            _rosterUpdatedCount = 0;
            _lastUnlockedId = null;
            _lastReward = null;

            EventBus.Subscribe(CrewManagementEvents.RosterUpdated, OnRosterUpdated);
            EventBus.Subscribe(CrewManagementEvents.CrewUnlocked, OnUnlocked);
            EventBus.Subscribe(CrewManagementEvents.RewardGranted, OnReward);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
            CrewManagementApi.Reset();
        }

        void OnRosterUpdated(object payload) => _rosterUpdatedCount++;

        void OnUnlocked(object payload)
        {
            if (payload is CrewUnlockedPayload unlocked)
                _lastUnlockedId = unlocked.CrewId;
        }

        void OnReward(object payload)
        {
            if (payload is CrewRewardPayload reward)
                _lastReward = reward;
        }

        [Test]
        public void Recruit_PublishesUnlockedAndRosterUpdated()
        {
            Assert.That(CrewManagementApi.Recruit("gunner"), Is.True);

            Assert.That(_lastUnlockedId, Is.EqualTo("gunner"));
            Assert.That(_rosterUpdatedCount, Is.EqualTo(1));
            Assert.That(CrewManagementApi.UnlockedCrewIds, Does.Contain("gunner"));
        }

        [Test]
        public void Recruit_UnknownCrew_DoesNotPublish()
        {
            Assert.That(CrewManagementApi.Recruit("nobody"), Is.False);
            Assert.That(_rosterUpdatedCount, Is.EqualTo(0));
            Assert.That(_lastUnlockedId, Is.Null);
        }

        [Test]
        public void SetActiveRoster_PublishesUpdateOnlyWhenAccepted()
        {
            Assert.That(CrewManagementApi.SetActiveRoster(new[] { "sailor" }), Is.True);
            Assert.That(_rosterUpdatedCount, Is.EqualTo(1));

            Assert.That(CrewManagementApi.SetActiveRoster(new[] { "sniper" }), Is.False, "未拥有");
            Assert.That(_rosterUpdatedCount, Is.EqualTo(1), "被拒时不广播");
        }

        [Test]
        public void GrantLevelReward_GivesXpToActiveRosterOnly()
        {
            CrewManagementApi.Recruit("gunner");
            CrewManagementApi.SetActiveRoster(new[] { "gunner" });   // 水手下阵

            CrewRewardPayload reward = CrewManagementApi.GrantLevelReward("level_01", stars: 3, clearedLevelNumber: 1);

            Assert.That(reward.XpPerCrew, Is.EqualTo(CrewProgressionRules.XpAward(3)));
            Assert.That(reward.CrewIds, Is.EqualTo(new[] { "gunner" }));
            Assert.That(CrewManagementApi.Progression.GetXp("gunner"), Is.EqualTo(reward.XpPerCrew));
            Assert.That(CrewManagementApi.Progression.GetXp("sailor"), Is.EqualTo(0), "下阵船员不发经验");
            Assert.That(_lastReward?.LevelId, Is.EqualTo("level_01"));
        }

        [Test]
        public void GrantLevelReward_UnlocksCrewsAtThreshold()
        {
            // 通关到第 3 关：炮手（门槛 3）入列，狙击手（门槛 5）仍锁。
            CrewRewardPayload reward = CrewManagementApi.GrantLevelReward("level_03", stars: 2, clearedLevelNumber: 3);

            Assert.That(reward.UnlockedCrewIds, Is.EqualTo(new[] { "gunner" }));
            Assert.That(CrewManagementApi.IsUnlocked("gunner"), Is.True);
            Assert.That(CrewManagementApi.IsUnlocked("sniper"), Is.False);
            Assert.That(_lastUnlockedId, Is.EqualTo("gunner"));
        }

        [Test]
        public void GrantLevelReward_OnFailedLevel_GivesNothing()
        {
            CrewRewardPayload reward = CrewManagementApi.GrantLevelReward("level_03", stars: 0, clearedLevelNumber: 0);

            Assert.That(reward.XpPerCrew, Is.EqualTo(0));
            Assert.That(reward.UnlockedCrewIds, Is.Empty);
            Assert.That(CrewManagementApi.Progression.GetXp("sailor"), Is.EqualTo(0));
            Assert.That(CrewManagementApi.IsUnlocked("gunner"), Is.False, "失败不推进招募");
        }

        [Test]
        public void SaveRoundTrip_RestoresRosterAndXp()
        {
            CrewManagementApi.Recruit("gunner");
            CrewManagementApi.SetActiveRoster(new[] { "sailor", "gunner" });
            CrewManagementApi.Progression.GrantXp("sailor", 250);

            var data = new SaveData();
            CrewManagementApi.WriteTo(data);

            // 破坏内存状态后读回。
            CrewManagementApi.Reset();
            Assert.That(CrewManagementApi.Roster.Active.Count, Is.EqualTo(1));

            CrewManagementApi.ReadFrom(data);

            Assert.That(CrewManagementApi.Roster.UnlockedCount, Is.EqualTo(2));
            Assert.That(CrewManagementApi.Roster.Active, Is.EqualTo(new[] { "sailor", "gunner" }));
            Assert.That(CrewManagementApi.Progression.GetXp("sailor"), Is.EqualTo(250));
            Assert.That(CrewManagementApi.Progression.GetLevel("sailor"), Is.EqualTo(2), "250 XP：100 到 2 级，还差 50 到 3 级");
        }

        [Test]
        public void ReadFrom_WithoutKeys_KeepsCurrentState()
        {
            CrewManagementApi.Recruit("gunner");

            CrewManagementApi.ReadFrom(new SaveData());

            Assert.That(CrewManagementApi.IsUnlocked("gunner"), Is.True, "无键时不应把名册清回初始");
            Assert.That(CrewManagementApi.Roster.Active.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReadFrom_ToleratesCorruptedEntries()
        {
            var data = new SaveData();
            data.SetData(CrewManagementSaveCodec.UnlockedKey, "sailor|nobody||sailor");
            data.SetData(CrewManagementSaveCodec.ActiveKey, "sailor|nobody");
            data.SetData(CrewManagementSaveCodec.XpKey, "sailor:abc|gunner:120|:50|broken");

            CrewManagementApi.ReadFrom(data);

            Assert.That(CrewManagementApi.IsUnlocked("sailor"), Is.True);
            Assert.That(CrewManagementApi.IsUnlocked("nobody"), Is.False, "名录外 id 应被 Recruit 拒绝");
            Assert.That(CrewManagementApi.Roster.Active, Is.EqualTo(new[] { "sailor" }), "非法成员导致整组拒绝，保留默认");
            Assert.That(CrewManagementApi.Progression.GetXp("gunner"), Is.EqualTo(120));
            Assert.That(CrewManagementApi.Progression.CrewIds, Does.Not.Contain("sailor"), "非数字经验段应跳过");
        }

        [Test]
        public void Codec_JoinSplitIds_HandlesEmptyAndDuplicates()
        {
            Assert.That(CrewManagementSaveCodec.JoinIds(null), Is.EqualTo(string.Empty));
            Assert.That(CrewManagementSaveCodec.JoinIds(new string[0]), Is.EqualTo(string.Empty));
            Assert.That(CrewManagementSaveCodec.JoinIds(new[] { "sailor", null, "gunner" }), Is.EqualTo("sailor|gunner"));

            Assert.That(CrewManagementSaveCodec.SplitIds(null), Is.Empty);
            Assert.That(CrewManagementSaveCodec.SplitIds("a|a|b").Count, Is.EqualTo(2));
            Assert.That(CrewManagementSaveCodec.SplitIds(" sailor | gunner "), Is.EqualTo(new[] { "sailor", "gunner" }));
        }
    }
}
