using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Core;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 接线发现的钉子测试（<see cref="GameBootstrap"/>）：唯一入口能发现哪些模块接线入口、
    /// 顺序是不是声明里写的那套。
    ///
    /// 【为什么要有它】"初始化顺序是显式代码而非执行时机"这条判据，必须能被命令复现——
    /// 否则它只是文档里的一句话。本文件把**启动期会执行的全部接线入口与顺序**钉死：
    ///   · 某个模块的接线入口被删/改名/丢掉特性 → 失败（静默失效的接线是最难查的故障）；
    ///   · 有人把 Order 调乱（比如让工具出图早于服务创建）→ 失败。
    ///
    /// 【本文件不调用 Initialize / ResetStatics 阶段】那两个阶段会碰 <c>Time.timeScale</c>、
    /// <c>new GameObject</c> 等原生调用，无头验证台跑不了（ECall 边界，见 external/harness/README）。
    /// 它们由 Unity batchmode 侧的冒烟收口；这里只验证"发现与顺序"。
    /// </summary>
    public class GameBootstrapTests
    {
        [SetUp]
        public void SetUp()
        {
            GameBootstrap.ResetDiscovery();
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.ResetDiscovery();
        }

        [Test]
        public void DescribeAll_FindsEveryExpectedParticipant()
        {
            var descriptions = new HashSet<string>();
            foreach (GameBootstrap.Entry entry in GameBootstrap.DescribeAll())
                descriptions.Add(entry.Description);

            // 全仓接线入口清单（重构后 15 处；由 Core/GameEntryPoint 一处调度）。
            string[] expected =
            {
                // ResetStatics：进入播放前清静态残留
                "PirateCrew.Battle.WorldMaps.WorldMapRuntime.ResetStatics",
                "PirateCrew.Campaign.CampaignApi.ResetStatics",
                "PirateCrew.CrewManagement.CrewManagementApi.ResetStatics",
                "PirateCrew.Battle.BattlePause.ResetStatics",
                // Contracts：事件契约登记（必须早于任何 Publish/Subscribe）
                "PirateCrew.Core.SceneEvents.RegisterContracts",
                "PirateCrew.Core.SaveEvents.RegisterContracts",
                "PirateCrew.Battle.BattleEvents.RegisterContracts",
                "PirateCrew.Campaign.CampaignEvents.RegisterContracts",
                "PirateCrew.CrewManagement.CrewManagementEvents.RegisterContracts",
                // Initialize：服务创建 / 事件订阅 / 命令行工具装配
                "PirateCrew.Audio.AudioService.Install",
                "PirateCrew.Fx.FxBootstrap.Install",
                "PirateCrew.Campaign.CampaignApi.Install",
                "PirateCrew.ArtReview.PlayerArtCapture.Install",
                "PirateCrew.SceneKitPilot.SceneKitPilotCapture.Install",
                "PirateCrew.UI.UiGalleryCapture.Install",
            };

            var missing = new List<string>();
            for (int i = 0; i < expected.Length; i++)
            {
                if (!descriptions.Contains(expected[i]))
                    missing.Add(expected[i]);
            }

            Assert.That(missing, Is.Empty, "下列接线入口没被唯一入口发现：" + string.Join("、", missing));
        }

        [Test]
        public void DescribeAll_IsDeterministic()
        {
            var first = Snapshot();
            GameBootstrap.ResetDiscovery();
            var second = Snapshot();

            Assert.That(second, Is.EqualTo(first), "两次发现的顺序必须一致（顺序敏感 = 不可复现）");
        }

        [Test]
        public void DescribeAll_IsOrderedByPhaseThenOrder()
        {
            IReadOnlyList<GameBootstrap.Entry> entries = GameBootstrap.DescribeAll();

            for (int i = 1; i < entries.Count; i++)
            {
                GameBootstrap.Entry previous = entries[i - 1];
                GameBootstrap.Entry current = entries[i];

                Assert.That((int)current.Phase, Is.GreaterThanOrEqualTo((int)previous.Phase),
                    "阶段顺序被打破：" + previous.Description + " 之后是 " + current.Description);

                if (current.Phase == previous.Phase)
                {
                    Assert.That(current.Order, Is.GreaterThanOrEqualTo(previous.Order),
                        "同阶段内 Order 顺序被打破：" + previous.Description + " → " + current.Description);
                }
            }
        }

        [Test]
        public void DescribeAll_ServiceAndWiringOrderIsExplicit()
        {
            IReadOnlyList<GameBootstrap.Entry> entries = GameBootstrap.DescribeAll();

            int audio = IndexOf(entries, "PirateCrew.Audio.AudioService.Install");
            int fx = IndexOf(entries, "PirateCrew.Fx.FxBootstrap.Install");
            int campaign = IndexOf(entries, "PirateCrew.Campaign.CampaignApi.Install");
            int artCapture = IndexOf(entries, "PirateCrew.ArtReview.PlayerArtCapture.Install");

            Assert.That(audio, Is.GreaterThanOrEqualTo(0));
            Assert.That(fx, Is.GreaterThan(audio), "服务（音频）必须先于表现层（特效）建立");
            Assert.That(campaign, Is.GreaterThan(fx), "玩法接线晚于服务/表现层装配");
            Assert.That(artCapture, Is.GreaterThan(campaign),
                "命令行工具的最后装配位：工具要等服务与玩法接线都就位（否则出图链路取不到状态）");
        }

        [Test]
        public void ResetDiscovery_ThenScan_IsStable()
        {
            int before = GameBootstrap.DescribeAll().Count;

            GameBootstrap.ResetDiscovery();

            Assert.That(GameBootstrap.DescribeAll().Count, Is.EqualTo(before), "重新发现不该得到不同的集合");
        }

        [Test]
        public void EntryDescriptions_AreUnique()
        {
            var seen = new HashSet<string>();
            foreach (GameBootstrap.Entry entry in GameBootstrap.DescribeAll())
                Assert.That(seen.Add(entry.Description), Is.True, "接线入口重复：" + entry.Description);
        }

        static List<string> Snapshot()
        {
            var list = new List<string>();
            foreach (GameBootstrap.Entry entry in GameBootstrap.DescribeAll())
                list.Add((int)entry.Phase + ":" + entry.Order + ":" + entry.Description);
            return list;
        }

        static int IndexOf(IReadOnlyList<GameBootstrap.Entry> entries, string description)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Description == description)
                    return i;
            }

            return -1;
        }
    }
}
