using NUnit.Framework;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 星级评价、战役结算与 M3 闭环接线（<c>battle_started</c> / <c>crew_died</c> / <c>match_finished</c> → 进度 + 奖励）。
    /// 纯 C#：<see cref="CampaignApi.SelectLevel"/> 只发事件（无 SaveManager / 无 SceneLoader 时不会触发 Unity API）。
    /// </summary>
    public class CampaignSettlementTests
    {
        CampaignLevelCompletedPayload? _lastCompleted;

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            CampaignApi.Reset();
            CrewManagementApi.Reset();
            CampaignApi.EnsureBootstrapped();
            _lastCompleted = null;
            EventBus.Subscribe(CampaignEvents.LevelCompleted, OnLevelCompleted);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
            CampaignApi.Reset();
            CrewManagementApi.Reset();
        }

        void OnLevelCompleted(object payload)
        {
            if (payload is CampaignLevelCompletedPayload completed)
                _lastCompleted = completed;
        }

        /// <summary>把一局战斗的事件序列打完（可选阵亡数）。</summary>
        static void PlayBattle(int outcome, int score, int playerDeaths)
        {
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(1, 2));
            for (int i = 0; i < playerDeaths; i++)
                EventBus.Publish(BattleEvents.CrewDied, new CrewDiedPayload(i, 0, "redPirate"));

            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(outcome, score, true));
        }

        // ------------------------------------------------------------------
        // 星级（GDD §9.3，提案/待定）
        // ------------------------------------------------------------------

        [Test]
        public void StarRules_FollowGddNineThree()
        {
            Assert.That(StarRules.Evaluate(cleared: false, playerDeaths: 0, chestsCollected: 0, chestsTotal: 0),
                Is.EqualTo(0), "未通关 0 星");

            Assert.That(StarRules.Evaluate(true, 0, 0, 0), Is.EqualTo(3), "全员存活 + 宝箱全收（未实装 → 0/0 视为满足）");
            Assert.That(StarRules.Evaluate(true, 1, 0, 0), Is.EqualTo(2), "阵亡 ≤ 1");
            Assert.That(StarRules.Evaluate(true, 2, 0, 0), Is.EqualTo(1), "阵亡 > 1");

            Assert.That(StarRules.Evaluate(true, 0, 1, 2), Is.EqualTo(2), "全员存活但没拾完宝箱 → 只到 2★");
            Assert.That(StarRules.Evaluate(true, 0, 2, 2), Is.EqualTo(3));

            Assert.That(StarRules.Evaluate(true, -5, -1, -1), Is.EqualTo(3), "负数输入夹到 0，不应抛");
        }

        [Test]
        public void PlayerWinOutcome_MatchesCombatEnum()
        {
            Assert.That(CampaignManager.PlayerWinOutcome, Is.EqualTo((int)MatchOutcome.Team0Win),
                "Campaign 侧的通关结果码必须与 Combat 的 MatchOutcome 对齐（§3.3）");
        }

        // ------------------------------------------------------------------
        // CampaignManager
        // ------------------------------------------------------------------

        [Test]
        public void TrySelectLevel_RejectsLockedAndUnknown()
        {
            var manager = new CampaignManager();

            Assert.That(manager.TrySelectLevel("level_01"), Is.True);
            Assert.That(manager.PendingLevelId, Is.EqualTo("level_01"));
            Assert.That(manager.CurrentChapter, Is.EqualTo(1));

            Assert.That(manager.TrySelectLevel("level_05"), Is.False, "未解锁");
            Assert.That(manager.PendingLevelId, Is.EqualTo("level_01"), "拒绝时保留原待结算关卡");

            Assert.That(manager.TrySelectLevel("level_99"), Is.False);
        }

        [Test]
        public void TrySettle_WritesProgressAndClearsPending()
        {
            var manager = new CampaignManager();
            manager.TrySelectLevel("level_01");

            bool settled = manager.TrySettle(new CampaignResult(true, 0, 0, 0, 1200), out CampaignSettlement settlement);

            Assert.That(settled, Is.True);
            Assert.That(settlement.Stars, Is.EqualTo(3));
            Assert.That(settlement.FirstClear, Is.True);
            Assert.That(settlement.Improved, Is.True);
            Assert.That(settlement.Score, Is.EqualTo(1200));
            Assert.That(manager.Progress.GetStars("level_01"), Is.EqualTo(3));
            Assert.That(manager.HasPendingLevel, Is.False, "结算后应清空待结算关卡");

            Assert.That(manager.TrySettle(new CampaignResult(true, 0, 0, 0, 1200), out _), Is.False,
                "没有待结算关卡时不应重复结算");
        }

        [Test]
        public void TrySettle_FailedRun_RecordsNothing()
        {
            var manager = new CampaignManager();
            manager.TrySelectLevel("level_01");

            manager.TrySettle(new CampaignResult(false, 3, 0, 0, 10), out CampaignSettlement settlement);

            Assert.That(settlement.Cleared, Is.False);
            Assert.That(settlement.Stars, Is.EqualTo(0));
            Assert.That(manager.Progress.CompletedCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // M3 闭环接线（CampaignApi 订阅战斗事件）
        // ------------------------------------------------------------------

        [Test]
        public void MatchFinished_ClearedLevel_WritesProgressAndGrantsXp()
        {
            Assert.That(CampaignApi.SelectLevel("level_01"), Is.True);
            Assert.That(CampaignApi.PendingLevelId, Is.EqualTo("level_01"));

            PlayBattle(outcome: CampaignManager.PlayerWinOutcome, score: 1500, playerDeaths: 1);

            Assert.That(CampaignApi.Progress.GetStars("level_01"), Is.EqualTo(2), "阵亡 1 人 → 2★");
            Assert.That(_lastCompleted?.LevelId, Is.EqualTo("level_01"));
            Assert.That(_lastCompleted?.Stars, Is.EqualTo(2));

            // 奖励发给编成阵容（初始只有水手），此时还没到招募门槛（第 3 关）。
            Assert.That(CrewManagementApi.Progression.GetXp(CrewRosterCatalog.InitialCrewId),
                Is.EqualTo(CrewProgressionRules.XpAward(2)));
            Assert.That(CrewManagementApi.Roster.UnlockedCount, Is.EqualTo(1));
            Assert.That(CampaignApi.LastReward?.XpPerCrew, Is.EqualTo(CrewProgressionRules.XpAward(2)));
            Assert.That(CampaignApi.PendingLevelId, Is.Null);
        }

        [Test]
        public void MatchFinished_PerfectRun_GivesThreeStars()
        {
            CampaignApi.SelectLevel("level_01");

            PlayBattle(CampaignManager.PlayerWinOutcome, 2000, playerDeaths: 0);

            Assert.That(CampaignApi.Progress.GetStars("level_01"), Is.EqualTo(3));
        }

        [Test]
        public void MatchFinished_UnlocksCrewAtThirdClear()
        {
            // 顺序打通 level_01 → level_02 → level_03（未转写关卡不阻塞，提案/待定）。
            CampaignApi.SelectLevel("level_01");
            PlayBattle(CampaignManager.PlayerWinOutcome, 1000, 0);

            Assert.That(CampaignApi.SelectLevel("level_02"), Is.True, "level_01 通关后 level_02 解锁");
            PlayBattle(CampaignManager.PlayerWinOutcome, 1000, 0);

            Assert.That(CampaignApi.SelectLevel("level_03"), Is.True);
            PlayBattle(CampaignManager.PlayerWinOutcome, 1000, 0);

            Assert.That(CrewManagementApi.IsUnlocked("gunner"), Is.True, "第 3 关通关后炮手入列（gdd §5.2，提案）");
            Assert.That(CrewManagementApi.IsUnlocked("sniper"), Is.False, "狙击手要第 5 关");
            Assert.That(CampaignApi.LastReward?.UnlockedCrewIds, Is.EqualTo(new[] { "gunner" }));
            Assert.That(CampaignApi.Progress.MaxCompletedLevelNumber, Is.EqualTo(3));
        }

        [Test]
        public void MatchFinished_FailedRun_KeepsProgressAndGivesNoXp()
        {
            CampaignApi.SelectLevel("level_01");

            PlayBattle((int)MatchOutcome.LevelFailed, 0, playerDeaths: 3);

            Assert.That(CampaignApi.Progress.CompletedCount, Is.EqualTo(0));
            Assert.That(CrewManagementApi.Progression.GetXp(CrewRosterCatalog.InitialCrewId), Is.EqualTo(0));
            Assert.That(_lastCompleted?.Cleared, Is.False);
            Assert.That(CampaignApi.LastSettlement?.Stars, Is.EqualTo(0));
        }

        [Test]
        public void MatchFinished_WithoutPendingLevel_DoesNotSettle()
        {
            // 主菜单「进入战斗」直接进战场的情况：没有待结算关卡，不应写任何进度。
            PlayBattle(CampaignManager.PlayerWinOutcome, 900, 0);

            Assert.That(CampaignApi.Progress.CompletedCount, Is.EqualTo(0));
            Assert.That(CampaignApi.LastSettlement, Is.Null);
            Assert.That(_lastCompleted, Is.Null);
        }

        [Test]
        public void DeathsCounter_ResetsBetweenBattles()
        {
            CampaignApi.SelectLevel("level_01");
            PlayBattle(CampaignManager.PlayerWinOutcome, 900, playerDeaths: 2);
            Assert.That(CampaignApi.Progress.GetStars("level_01"), Is.EqualTo(1));

            // 第二局：事件序列重新从 battle_started 开始，阵亡数必须归零 → 3★ 并刷新记录。
            CampaignApi.SelectLevel("level_01");
            PlayBattle(CampaignManager.PlayerWinOutcome, 900, playerDeaths: 0);

            Assert.That(CampaignApi.Progress.GetStars("level_01"), Is.EqualTo(3));
        }

        [Test]
        public void StalePendingLevel_IsCancelledByNonCampaignBattle()
        {
            // 场景：选关进了 level_01 的战斗 → 玩家没打完就退回 → 又从主菜单直接「进入战斗」。
            CampaignApi.SelectLevel("level_01");

            // 第一局（战役入口）：battle_started 消费掉「本次是战役局」标记，待结算关卡保留。
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(1, 2));
            Assert.That(CampaignApi.PendingLevelId, Is.EqualTo("level_01"));

            // 第二局（主菜单直进）：没有 SelectLevel，待结算关卡必须被清掉。
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(1, 2));
            Assert.That(CampaignApi.PendingLevelId, Is.Null, "非战役入口的新一局应丢弃陈旧待结算关卡");

            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(
                CampaignManager.PlayerWinOutcome, 800, true));

            Assert.That(CampaignApi.Progress.CompletedCount, Is.EqualTo(0), "主菜单直进战斗的结果不得记成战役进度");
            Assert.That(CampaignApi.LastSettlement, Is.Null);
            Assert.That(_lastCompleted, Is.Null);
        }

        // ------------------------------------------------------------------
        // Battle 侧关卡注入的衔接点
        // ------------------------------------------------------------------

        [Test]
        public void PendingBattleLevelNumber_FallsBackWhenNoSelection()
        {
            Assert.That(CampaignApi.PendingBattleLevelNumberOr(7), Is.EqualTo(7), "未选关 → 回退");
        }

        [Test]
        public void PendingBattleLevelNumber_UsesSelectedLevelWhenTranscribed()
        {
            CampaignApi.Progress.CompleteLevel("level_01", 1);   // level_04 的前置已转写关卡
            Assert.That(CampaignApi.SelectLevel("level_04"), Is.True);

            Assert.That(CampaignApi.PendingBattleLevelNumberOr(1), Is.EqualTo(4),
                "level_04 在 LevelCatalog 里有数据 → 用所选关卡号");
        }

        [Test]
        public void PendingBattleLevelNumber_FallsBackWhenLevelHasNoData()
        {
            // level_02 必须先解锁才能选中（前置已转写关卡 level_01 通关）。
            CampaignApi.Progress.CompleteLevel("level_01", 1);
            Assert.That(CampaignApi.SelectLevel("level_02"), Is.True);
            Assert.That(CampaignApi.PendingLevelId, Is.EqualTo("level_02"));

            Assert.That(CampaignApi.PendingBattleLevelNumberOr(1), Is.EqualTo(1),
                "level_02 的数据还没转写 → 回退，接入方不会抛 KeyNotFoundException");
        }

        // ------------------------------------------------------------------
        // 存档
        // ------------------------------------------------------------------

        [Test]
        public void SaveRoundTrip_RestoresCampaignProgressAndRoster()
        {
            CampaignApi.SelectLevel("level_01");
            PlayBattle(CampaignManager.PlayerWinOutcome, 1500, 0);
            CrewManagementApi.Recruit("gunner");
            CrewManagementApi.SetActiveRoster(new[] { "sailor", "gunner" });

            var data = new SaveData();
            CampaignApi.WriteTo(data);

            CampaignApi.Reset();
            CrewManagementApi.Reset();
            Assert.That(CampaignApi.Progress.CompletedCount, Is.EqualTo(0));
            Assert.That(CrewManagementApi.Roster.UnlockedCount, Is.EqualTo(1));

            CampaignApi.ReadFrom(data);

            Assert.That(CampaignApi.Progress.GetStars("level_01"), Is.EqualTo(3));
            Assert.That(CampaignApi.Progress.TotalStars, Is.EqualTo(3));
            Assert.That(CampaignApi.IsLevelUnlocked("level_02"), Is.True, "读档后解锁链应恢复");
            Assert.That(CrewManagementApi.Roster.Active, Is.EqualTo(new[] { "sailor", "gunner" }));
            Assert.That(CrewManagementApi.Progression.GetXp("sailor"),
                Is.EqualTo(CrewProgressionRules.XpAward(3)));
        }

        [Test]
        public void SaveProgress_WithoutSaveManagerInstance_ReturnsFalse()
        {
            // 无头验证台 / 未经过 Bootstrapper 时，SaveManager.Instance 为 null → 静默失败不抛异常。
            Assert.That(SaveManager.Instance, Is.Null, "本用例要求没有 SaveManager 实例");
            Assert.That(CampaignApi.SaveProgress(), Is.False);
            Assert.That(CampaignApi.LoadProgress(), Is.False);
        }

        [Test]
        public void CampaignSaveCodec_ToleratesCorruptedEntries()
        {
            var progress = new CampaignProgress();
            CampaignSaveCodec.ReadInto("level_01:3||level_99:2|broken|level_04:abc", progress);

            Assert.That(progress.GetStars("level_01"), Is.EqualTo(3));
            Assert.That(progress.CompletedCount, Is.EqualTo(1), "未知关卡 / 非数字星级 / 残缺段都应跳过");
        }

        [Test]
        public void CampaignSaveCodec_JoinSkipsZeroStars()
        {
            var progress = new CampaignProgress();
            progress.CompleteLevel("level_01", 2);

            string raw = CampaignSaveCodec.Join(progress.Snapshot());

            Assert.That(raw, Is.EqualTo("level_01:2"));
            Assert.That(CampaignSaveCodec.Join(null), Is.EqualTo(string.Empty));
        }
    }
}
