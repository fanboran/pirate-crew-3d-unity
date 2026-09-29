using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Campaign;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 海图进度（海图 id → 星级）的纯 C# 断言。一代的「顺序解锁链」随一代退场：
    /// 8 张海图全部可出战，进度只剩「星级记录」一个职责（键 = 海图 id，值域由
    /// <c>WorldMapCatalog</c> 校验）。
    ///
    /// 【零海图口径】八张世界海图已删除待重做、目录为空 ⇒ 一切 id 都在目录外，
    /// "需要合法海图 id 才能写入"的用例没有可写对象（写入被目录校验拒掉）。
    /// 这类用例以 <c>Assume</c> 前置跳成 Skipped（海图放回来自动生效）；
    /// "空 id / 目录外 id 被拒"的用例本来就该通过，原样保留。
    /// </summary>
    public class CampaignProgressTests
    {
        const string MapA = "wreck_hymn";   // 目录里的合法海图 id（零海图下不存在，见上面的口径）

        /// <summary>零海图时"需要可写 id"的用例统一以这条前置跳成 Skipped。</summary>
        const string NoMapSkipReason = "当前工程零海图（八张世界海图已删除待重做），用例待重做后自动生效";

        [Test]
        public void FreshProgress_IsEmpty()
        {
            var progress = new CampaignProgress();

            Assert.That(progress.GetStars(MapA), Is.EqualTo(0));
            Assert.That(progress.CompletedCount, Is.EqualTo(0));
            Assert.That(progress.TotalStars, Is.EqualTo(0));
        }

        [Test]
        public void CompleteLevel_KeepsBestStars()
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), NoMapSkipReason);

            var progress = new CampaignProgress();

            Assert.That(progress.CompleteLevel(MapA, 3), Is.True, "首次通关应记为改进");
            Assert.That(progress.CompleteLevel(MapA, 1), Is.False, "低星重打不降级");
            Assert.That(progress.GetStars(MapA), Is.EqualTo(3));
            Assert.That(progress.CompletedCount, Is.EqualTo(1));
            Assert.That(progress.TotalStars, Is.EqualTo(3));
        }

        [Test]
        public void CompleteLevel_IgnoresInvalidInput()
        {
            var progress = new CampaignProgress();

            Assert.That(progress.CompleteLevel(MapA, 0), Is.False, "0 星不算通关");
            Assert.That(progress.CompleteLevel("level_99", 3), Is.False, "不在海图目录的 id");
            Assert.That(progress.CompleteLevel("level_01", 3), Is.False, "一代旧档键不进新进度");
            Assert.That(progress.CompleteLevel(null, 3), Is.False);
            Assert.That(progress.CompletedCount, Is.EqualTo(0), "未通关不应计入通关数");
        }

        [Test]
        public void CompleteLevel_ClampsStarsToMax()
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), NoMapSkipReason);

            var progress = new CampaignProgress();

            progress.SetStars(MapA, 99);

            Assert.That(progress.GetStars(MapA), Is.EqualTo(StarRules.MaxStars));
        }

        [Test]
        public void SetStars_RejectsIdsOutsideMapCatalog()
        {
            var progress = new CampaignProgress();

            progress.SetStars("level_01", 3);

            Assert.That(progress.CompletedCount, Is.EqualTo(0), "一代旧档键（level_01）读档时静默丢弃");
        }

        [Test]
        public void CompletedMaps_CountOnceEvenIfReplayedBetter()
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), NoMapSkipReason);

            var progress = new CampaignProgress();
            progress.CompleteLevel(MapA, 1);
            progress.CompleteLevel(MapA, 3);

            Assert.That(progress.CompletedCount, Is.EqualTo(1));
            Assert.That(progress.TotalStars, Is.EqualTo(3));
        }

        [Test]
        public void TotalStars_SumsAcrossMaps()
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), NoMapSkipReason);

            var progress = new CampaignProgress();
            progress.CompleteLevel("wreck_hymn", 3);
            progress.CompleteLevel("atoll_ring", 2);

            Assert.That(progress.TotalStars, Is.EqualTo(5), "招募门槛（累计星数）的数据源");
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            Assume.That(WorldMapCatalog.Count, Is.GreaterThan(0), NoMapSkipReason);

            var progress = new CampaignProgress();
            progress.CompleteLevel(MapA, 3);

            progress.Reset();

            Assert.That(progress.CompletedCount, Is.EqualTo(0));
            Assert.That(progress.IsCompleted(MapA), Is.False);
        }
    }
}
