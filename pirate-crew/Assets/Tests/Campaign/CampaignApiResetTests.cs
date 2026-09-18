using NUnit.Framework;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Battle.WorldMaps;

namespace PirateCrew.Tests
{
    /// <summary>
    /// <see cref="CampaignApi"/> 静态生命周期的订阅纪律：<see cref="CampaignApi.Reset"/> 必须
    /// 对称退订 <see cref="CampaignApi.EnsureBootstrapped"/> 订下的三个战斗事件——
    /// 方法组转换每次生成新委托实例，Reset 不退订会让 EventBus 里残留监听者，
    /// Reset → EnsureBootstrapped 往返后结算路径就站在双份订阅的悬崖边（架构违规：
    /// 订阅方必须退订，见 <c>Core/EventBus</c> 约定 3）。
    /// 纯 C#：只碰 EventBus / CampaignApi / CrewManagementApi 静态态，无 Unity 对象，无头可跑。
    /// </summary>
    public class CampaignApiResetTests
    {
        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            CampaignApi.Reset();
            CrewManagementApi.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
            CampaignApi.Reset();
            CrewManagementApi.Reset();
        }

        /// <summary>Reset 前已订阅、Reset 后三个事件都必须无监听者。</summary>
        [Test]
        public void Reset_UnsubscribesAllBattleEvents()
        {
            CampaignApi.EnsureBootstrapped();
            Assert.That(EventBus.HasListeners(BattleEvents.BattleStarted), Is.True);
            Assert.That(EventBus.HasListeners(BattleEvents.CrewDied), Is.True);
            Assert.That(EventBus.HasListeners(BattleEvents.MatchFinished), Is.True);

            CampaignApi.Reset();

            Assert.That(EventBus.HasListeners(BattleEvents.BattleStarted), Is.False,
                "Reset 必须退订 battle_started");
            Assert.That(EventBus.HasListeners(BattleEvents.CrewDied), Is.False,
                "Reset 必须退订 crew_died");
            Assert.That(EventBus.HasListeners(BattleEvents.MatchFinished), Is.False,
                "Reset 必须退订 match_finished");
        }

        /// <summary>
        /// Reset → EnsureBootstrapped 幂等：发布一次 crew_died 只允许计一次阵亡。
        /// 若残留 + 重订形成双份订阅，1 人阵亡会被计成 2 → 星级从 2★ 掉到 1★（结算翻倍的直接症状）。
        /// </summary>
        [Test]
        public void ResetThenEnsureBootstrapped_PublishOnce_TriggersOnce()
        {
            CampaignApi.EnsureBootstrapped();
            CampaignApi.Reset();
            CampaignApi.EnsureBootstrapped();

            WorldMapRuntime.SetPending("wreck_hymn");
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(101, 2));
            EventBus.Publish(BattleEvents.CrewDied, new CrewDiedPayload(0, 0, "redPirate"));
            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(
                CampaignManager.PlayerWinOutcome, 1200, true));

            Assert.That(CampaignApi.LastSettlement, Is.Not.Null, "一局恰好结算一次");
            Assert.That(CampaignApi.LastSettlement?.Stars, Is.EqualTo(2),
                "发布一次 crew_died 只允许计一次阵亡（双订阅会把 1 死计成 2 → 1★）");
            Assert.That(CampaignApi.Progress.GetStars("wreck_hymn"), Is.EqualTo(2));
        }

        /// <summary>重复 EnsureBootstrapped 不叠加订阅（EventBus 去重 + 幂等标志的既有保证，回归保护）。</summary>
        [Test]
        public void EnsureBootstrapped_RepeatedCalls_StaySingleSubscriber()
        {
            CampaignApi.EnsureBootstrapped();
            CampaignApi.EnsureBootstrapped();
            CampaignApi.EnsureBootstrapped();

            WorldMapRuntime.SetPending("wreck_hymn");
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(101, 2));
            EventBus.Publish(BattleEvents.CrewDied, new CrewDiedPayload(0, 0, "redPirate"));
            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(
                CampaignManager.PlayerWinOutcome, 1200, true));

            Assert.That(CampaignApi.LastSettlement?.Stars, Is.EqualTo(2),
                "重复 EnsureBootstrapped 不得叠加订阅");
        }

        /// <summary>Reset 后事件照常可用：再 EnsureBootstrapped 能重新订阅并完成一次完整结算。</summary>
        [Test]
        public void Reset_AllowsFreshBootstrap()
        {
            CampaignApi.EnsureBootstrapped();
            CampaignApi.Reset();
            Assert.That(EventBus.HasListeners(BattleEvents.MatchFinished), Is.False);

            CampaignApi.EnsureBootstrapped();
            Assert.That(EventBus.HasListeners(BattleEvents.MatchFinished), Is.True,
                "Reset 后再 EnsureBootstrapped 应恢复订阅");

            WorldMapRuntime.SetPending("wreck_hymn");
            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(101, 2));
            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(
                CampaignManager.PlayerWinOutcome, 900, true));

            Assert.That(CampaignApi.LastSettlement?.Cleared, Is.True, "重订阅后结算链路完好");
        }
    }
}
