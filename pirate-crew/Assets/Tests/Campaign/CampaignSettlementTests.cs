using NUnit.Framework;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Combat;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 星级评价、海图结算与 M3 闭环接线（<c>battle_started</c> / <c>crew_died</c> / <c>match_finished</c> → 进度 + 奖励）。
    /// 一代退场后结算键 = 海图 id：待战海图由 <see cref="WorldMapRuntime.SetPending"/> 设置，
    /// <c>CampaignApi</c> 在 battle_started 时收养它为待结算归属。
    /// 纯 C#：发事件不触发 Unity API（无 SaveManager / 无 SceneLoader 时安全）。
    /// </summary>
    public class CampaignSettlementTests
    {
        const string MapA = "wreck_hymn";   // WorldMapCatalog 首张海图（id 以目录为准，SetUp 里兜底校验）

        CampaignMapCompletedPayload? _lastCompleted;

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            CampaignApi.Reset();
            CrewManagementApi.Reset();
            CampaignApi.EnsureBootstrapped();
            _lastCompleted = null;
            Assert.That(WorldMapCatalog.TryGet(MapA, out _), Is.True,
                "测试依赖目录里存在海图 " + MapA);
            EventBus.Subscribe(CampaignEvents.MapCompleted, OnMapCompleted);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
            CampaignApi.Reset();
            CrewManagementApi.Reset();
        }

        void OnMapCompleted(object payload)
        {
            if (payload is CampaignMapCompletedPayload completed)
                _lastCompleted = completed;
        }

        /// <summary>把一局海图战的事件序列打完（可选阵亡数）；battle_started 前须已 SetPending。</summary>
        static void PlayBattle(int outcome, int score, int playerDeaths)
        {
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(101, 2));
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
        public void SelectMap_AdoptsPendingAndSettlesByMapId()
        {
            var manager = new CampaignManager();
            manager.SelectMap(MapA);

            Assert.That(manager.PendingMapId, Is.EqualTo(MapA));
            manager.SelectMap(null);
            Assert.That(manager.HasPendingMap, Is.False, "空 id 归一化为 null，不产生待结算");
            Assert.That(manager.HasPendingMap, Is.False);

            manager.SelectMap(MapA);
            bool settled = manager.TrySettle(new CampaignResult(true, 0, 0, 0, 1200), out CampaignSettlement settlement);

            Assert.That(settled, Is.True);
            Assert.That(settlement.MapId, Is.EqualTo(MapA));
            Assert.That(settlement.Stars, Is.EqualTo(3));
            Assert.That(settlement.FirstClear, Is.True);
            Assert.That(settlement.Improved, Is.True);
            Assert.That(settlement.Score, Is.EqualTo(1200));
            Assert.That(manager.Progress.GetStars(MapA), Is.EqualTo(3));
            Assert.That(manager.HasPendingMap, Is.False, "结算后应清空待结算海图");

            Assert.That(manager.TrySettle(new CampaignResult(true, 0, 0, 0, 1200), out _), Is.False,
                "没有待结算海图时不应重复结算");
        }

        [Test]
        public void TrySettle_FailedRun_RecordsNothing()
        {
            var manager = new CampaignManager();
            manager.SelectMap(MapA);

            manager.TrySettle(new CampaignResult(false, 3, 0, 0, 10), out CampaignSettlement settlement);

            Assert.That(settlement.Cleared, Is.False);
            Assert.That(settlement.Stars, Is.EqualTo(0));
            Assert.That(manager.Progress.CompletedCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // M3 闭环接线（CampaignApi 订阅战斗事件，收养待战海图）
        // ------------------------------------------------------------------

        [Test]
        public void MatchFinished_ClearedMap_WritesProgressAndGrantsXp()
        {
            Assert.That(WorldMapRuntime.SetPending(MapA), Is.True);

            PlayBattle(outcome: CampaignManager.PlayerWinOutcome, score: 1500, playerDeaths: 1);

            Assert.That(CampaignApi.Progress.GetStars(MapA), Is.EqualTo(2), "阵亡 1 人 → 2★");
            Assert.That(_lastCompleted?.MapId, Is.EqualTo(MapA));
            Assert.That(_lastCompleted?.Stars, Is.EqualTo(2));

            // 奖励发给编成阵容（初始只有水手）；累计 2 星还不到炮手的 3 星门槛。
            Assert.That(CrewManagementApi.Progression.GetXp(CrewRosterCatalog.InitialCrewId),
                Is.EqualTo(CrewProgressionRules.XpAward(2)));
            Assert.That(CrewManagementApi.Roster.UnlockedCount, Is.EqualTo(1));
            Assert.That(CampaignApi.LastReward?.XpPerCrew, Is.EqualTo(CrewProgressionRules.XpAward(2)));
            Assert.That(CampaignApi.HasPendingMap, Is.False, "结算后待结算海图应清空");
        }

        [Test]
        public void MatchFinished_PerfectRun_GivesThreeStars()
        {
            WorldMapRuntime.SetPending(MapA);

            PlayBattle(CampaignManager.PlayerWinOutcome, 2000, playerDeaths: 0);

            Assert.That(CampaignApi.Progress.GetStars(MapA), Is.EqualTo(3));
        }

        [Test]
        public void MatchFinished_TotalStars_GatesRecruitment()
        {
            // 门槛口径（一代退场执行决策）：炮手 3 星 / 狙击手 5 星（数值沿用，语义 = 累计星数）。
            WorldMapRuntime.SetPending(MapA);
            PlayBattle(CampaignManager.PlayerWinOutcome, 1000, 0);   // +3 星

            Assert.That(CrewManagementApi.IsUnlocked("gunner"), Is.True, "3 星达标 → 炮手入列（gdd §5.2，提案）");
            Assert.That(CrewManagementApi.IsUnlocked("sniper"), Is.False, "狙击手要 5 星");
            Assert.That(CampaignApi.LastReward?.UnlockedCrewIds, Is.EqualTo(new[] { "gunner" }));

            // 第二张图 2★（阵亡 1 人）→ 累计 5 星 → 狙击手入列。
            const string mapB = "atoll_ring";
            Assert.That(WorldMapCatalog.TryGet(mapB, out _), Is.True, "测试依赖目录里存在海图 " + mapB);
            WorldMapRuntime.SetPending(mapB);
            PlayBattle(CampaignManager.PlayerWinOutcome, 1000, 1);

            Assert.That(CrewManagementApi.IsUnlocked("sniper"), Is.True);
            Assert.That(CampaignApi.Progress.TotalStars, Is.EqualTo(5));
        }

        [Test]
        public void MatchFinished_FailedRun_KeepsProgressAndGivesNoXp()
        {
            WorldMapRuntime.SetPending(MapA);

            PlayBattle((int)MatchOutcome.LevelFailed, 0, playerDeaths: 3);

            Assert.That(CampaignApi.Progress.CompletedCount, Is.EqualTo(0));
            Assert.That(CrewManagementApi.Progression.GetXp(CrewRosterCatalog.InitialCrewId), Is.EqualTo(0));
            Assert.That(_lastCompleted?.Cleared, Is.False);
            Assert.That(CampaignApi.LastSettlement?.Stars, Is.EqualTo(0));
        }

        [Test]
        public void MatchFinished_WithoutPendingMap_DoesNotSettle()
        {
            // 样板三关 / 主菜单直进等「没有待战海图」的局：不应写任何进度。
            PlayBattle(CampaignManager.PlayerWinOutcome, 900, 0);

            Assert.That(CampaignApi.Progress.CompletedCount, Is.EqualTo(0));
            Assert.That(CampaignApi.LastSettlement, Is.Null);
            Assert.That(_lastCompleted, Is.Null);
        }

        [Test]
        public void DeathsCounter_ResetsBetweenBattles()
        {
            WorldMapRuntime.SetPending(MapA);
            PlayBattle(CampaignManager.PlayerWinOutcome, 900, playerDeaths: 2);
            Assert.That(CampaignApi.Progress.GetStars(MapA), Is.EqualTo(1));

            // 第二局：事件序列重新从 battle_started 开始，阵亡数必须归零 → 3★ 并刷新记录。
            WorldMapRuntime.SetPending(MapA);
            PlayBattle(CampaignManager.PlayerWinOutcome, 900, playerDeaths: 0);

            Assert.That(CampaignApi.Progress.GetStars(MapA), Is.EqualTo(3));
        }

        [Test]
        public void StalePendingMap_IsCancelledByMaplessBattle()
        {
            // 场景：海图战没打完就退 → 下一局没有任何待战海图（直接 Play）。
            WorldMapRuntime.SetPending(MapA);

            // 第一局：battle_started 收养待战海图为待结算归属。
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(101, 2));
            Assert.That(CampaignApi.HasPendingMap, Is.True);

            // 第二局（无待战海图）：陈旧待结算必须被清掉。
            WorldMapRuntime.ClearPending();
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(101, 2));
            Assert.That(CampaignApi.HasPendingMap, Is.False, "无待战海图的新一局应丢弃陈旧待结算");

            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(
                CampaignManager.PlayerWinOutcome, 800, true));

            Assert.That(CampaignApi.Progress.CompletedCount, Is.EqualTo(0), "无归属一局的结果不得记成战役进度");
            Assert.That(CampaignApi.LastSettlement, Is.Null);
            Assert.That(_lastCompleted, Is.Null);
        }

        // ------------------------------------------------------------------
        // 存档
        // ------------------------------------------------------------------

        [Test]
        public void SaveRoundTrip_RestoresCampaignProgressAndRoster()
        {
            WorldMapRuntime.SetPending(MapA);
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

            Assert.That(CampaignApi.Progress.GetStars(MapA), Is.EqualTo(3));
            Assert.That(CampaignApi.Progress.TotalStars, Is.EqualTo(3));
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
        public void CampaignSaveCodec_ToleratesCorruptedAndLegacyEntries()
        {
            var progress = new CampaignProgress();
            // level_01 是一代旧档键：不在海图目录 → 必须被静默丢弃（旧档不迁移）。
            CampaignSaveCodec.ReadInto("wreck_hymn:3||level_99:2|broken|wreck_hymn:abc", progress);

            Assert.That(progress.GetStars("wreck_hymn"), Is.EqualTo(3));
            Assert.That(progress.GetStars("level_01"), Is.EqualTo(0), "一代旧键不应进新进度");
            Assert.That(progress.CompletedCount, Is.EqualTo(1), "未知 id / 非数字星级 / 残缺段都应跳过");
        }

        [Test]
        public void CampaignSaveCodec_JoinSkipsZeroStars()
        {
            var progress = new CampaignProgress();
            progress.CompleteLevel(MapA, 2);

            string raw = CampaignSaveCodec.Join(progress.Snapshot());

            Assert.That(raw, Is.EqualTo(MapA + ":2"));
            Assert.That(CampaignSaveCodec.Join(null), Is.EqualTo(string.Empty));
        }
    }
}
