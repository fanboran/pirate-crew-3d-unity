using NUnit.Framework;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Battle;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 出征注入载体本体（架构审计 P0-1 反向依赖修正的中立层）：
    /// 战斗侧只读它取「加载哪张竞技场 + 带哪套编成符号」，不再反向引用 Campaign / CrewManagement。
    /// 纯 C#，可无头跑。
    /// </summary>
    public class BattleLaunchContextTests
    {
        [SetUp]
        public void SetUp()
        {
            BattleLaunchContext.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            BattleLaunchContext.Clear();
        }

        [Test]
        public void NoInjection_LevelNumberFallsBack()
        {
            Assert.That(BattleLaunchContext.HasPending, Is.False);
            Assert.That(BattleLaunchContext.PendingLevelNumberOr(7), Is.EqualTo(7));
        }

        [Test]
        public void SetPending_ExposesLevelNumberAndSymbols()
        {
            BattleLaunchContext.SetPending(4, "level_04", new[] { "redPirate", "skeletonPirate" });

            Assert.That(BattleLaunchContext.HasPending, Is.True);
            Assert.That(BattleLaunchContext.Pending.LevelId, Is.EqualTo("level_04"));
            Assert.That(BattleLaunchContext.PendingLevelNumberOr(1), Is.EqualTo(4));
            Assert.That(BattleLaunchContext.Pending.ActiveBattleSymbols,
                Is.EqualTo(new[] { "redPirate", "skeletonPirate" }));
        }

        [Test]
        public void SetPending_LevelNumberZero_FallsBack()
        {
            // 关卡未转写（LevelCatalog.IsTranscribed 为假）时 Campaign 写 0 → 战斗侧回落场景自带的 fallback。
            BattleLaunchContext.SetPending(0, "level_99", new string[0]);

            Assert.That(BattleLaunchContext.HasPending, Is.True, "注入本身仍然有效（编成仍要注入）");
            Assert.That(BattleLaunchContext.PendingLevelNumberOr(3), Is.EqualTo(3));
        }

        [Test]
        public void SetPending_NullSymbols_BecomesEmptyArray()
        {
            BattleLaunchContext.SetPending(1, "level_01", null);

            Assert.That(BattleLaunchContext.Pending.ActiveBattleSymbols, Is.Not.Null);
            Assert.That(BattleLaunchContext.Pending.ActiveBattleSymbols.Length, Is.EqualTo(0));
        }

        [Test]
        public void SetPending_SnapshotsSymbols_MutatingSourceDoesNotLeak()
        {
            var source = new[] { "redPirate" };
            BattleLaunchContext.SetPending(1, "level_01", source);

            source[0] = "oldPirate";

            Assert.That(BattleLaunchContext.Pending.ActiveBattleSymbols[0], Is.EqualTo("redPirate"),
                "注入存的是快照，调用方之后改数组不应影响本局出征名单");
        }

        [Test]
        public void Clear_DropsInjection()
        {
            BattleLaunchContext.SetPending(2, "level_02", new[] { "redPirate" });

            BattleLaunchContext.Clear();

            Assert.That(BattleLaunchContext.HasPending, Is.False);
            Assert.That(BattleLaunchContext.PendingLevelNumberOr(9), Is.EqualTo(9));
        }
    }

    /// <summary>
    /// Campaign 侧的写入/清除接线：选关写注入（含「船员 id → 导出符号」映射）、
    /// 放弃待战关与「非战役入口那一局」清注入、Reset 清注入。
    /// 纯 C#（<see cref="CampaignApi.SelectLevel"/> 只发事件，不触 Unity API）。
    /// </summary>
    public class BattleLaunchInjectionTests
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

        [Test]
        public void SelectLevel_WritesInjection()
        {
            Assert.That(CampaignApi.SelectLevel("level_01"), Is.True);

            Assert.That(BattleLaunchContext.HasPending, Is.True);
            Assert.That(BattleLaunchContext.Pending.LevelId, Is.EqualTo("level_01"));
            Assert.That(BattleLaunchContext.PendingLevelNumberOr(99), Is.EqualTo(1));
            // 初始名册只有水手（sailor → redPirate）：映射在高层完成，战斗侧拿到的已是符号。
            Assert.That(BattleLaunchContext.Pending.ActiveBattleSymbols, Is.EqualTo(new[] { "redPirate" }));
        }

        [Test]
        public void SelectLevel_Rejected_KeepsNoInjection()
        {
            Assert.That(CampaignApi.SelectLevel("level_not_exist"), Is.False);

            Assert.That(BattleLaunchContext.HasPending, Is.False);
        }

        [Test]
        public void AbortPendingLevel_ClearsInjection()
        {
            CampaignApi.SelectLevel("level_01");

            CampaignApi.AbortPendingLevel();

            Assert.That(CampaignApi.PendingLevelId, Is.Null);
            Assert.That(BattleLaunchContext.HasPending, Is.False,
                "放弃待战关后注入必须一起清，否则下一局直进战斗会加载上一局选过的竞技场");
        }

        [Test]
        public void BattleStarted_WithoutCampaignRequest_ClearsStaleInjection()
        {
            // 模拟「上一局选过关但没打完就退出」留下的陈旧注入。
            BattleLaunchContext.SetPending(5, "level_05", new[] { "redPirate" });
            CampaignApi.EnsureBootstrapped();

            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(1, 2));

            Assert.That(BattleLaunchContext.HasPending, Is.False);
        }

        [Test]
        public void BattleStarted_FromCampaignRequest_KeepsInjection()
        {
            CampaignApi.EnsureBootstrapped();
            CampaignApi.SelectLevel("level_01");

            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(1, 2));

            Assert.That(BattleLaunchContext.HasPending, Is.True,
                "战役入口那一局的注入要活到整局打完（结算归属也依赖待战关卡）");
        }

        [Test]
        public void Reset_ClearsInjection()
        {
            BattleLaunchContext.SetPending(6, "level_06", new[] { "redPirate" });

            CampaignApi.Reset();

            Assert.That(BattleLaunchContext.HasPending, Is.False);
        }
    }
}
