using NUnit.Framework;
using PirateCrew.Campaign;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 关卡进度（星级 + 顺序解锁）的纯 C# 断言。
    /// 【基准】Godot <c>progression.gd</c>：星级取历史最好、第一关默认解锁、其余需前一关完成；
    ///   「跳过未转写关卡」是本作提案（见 <see cref="CampaignProgress"/> 类头）。
    /// </summary>
    public class CampaignProgressTests
    {
        [Test]
        public void FreshProgress_OnlyFirstLevelUnlocked()
        {
            var progress = new CampaignProgress();

            Assert.That(progress.IsUnlocked("level_01"), Is.True, "progression.gd:25 第一关默认解锁");
            Assert.That(progress.GetStars("level_01"), Is.EqualTo(0));
            Assert.That(progress.CompletedCount, Is.EqualTo(0));
            Assert.That(progress.TotalStars, Is.EqualTo(0));
            Assert.That(progress.MaxCompletedLevelNumber, Is.EqualTo(0));
        }

        [Test]
        public void UnlockChain_SkipsUntranscribedLevels()
        {
            var progress = new CampaignProgress();

            // level_02..level_04 的前一「已转写」关卡都是 level_01（见 Catalog 测试）。
            Assert.That(progress.IsUnlocked("level_02"), Is.False);
            Assert.That(progress.IsUnlocked("level_04"), Is.False);

            progress.CompleteLevel("level_01", 2);

            Assert.That(progress.IsUnlocked("level_02"), Is.True);
            Assert.That(progress.IsUnlocked("level_03"), Is.True);
            Assert.That(progress.IsUnlocked("level_04"), Is.True);
            Assert.That(progress.IsUnlocked("level_05"), Is.False, "level_05 需要 level_04 通关");
            Assert.That(progress.IsUnlocked("level_15"), Is.False);
        }

        [Test]
        public void UnlockChain_FullRun()
        {
            var progress = new CampaignProgress();

            // 只打通「已转写」的两关即可推进整条链（未转写关卡不阻塞，提案/待定）。
            progress.CompleteLevel("level_01", 1);
            progress.CompleteLevel("level_04", 1);

            Assert.That(progress.IsUnlocked("level_05"), Is.True);
            Assert.That(progress.IsUnlocked("level_15"), Is.True, "链条上再没有已转写关卡拦截");
        }

        [Test]
        public void CompleteLevel_KeepsBestStars()
        {
            var progress = new CampaignProgress();

            Assert.That(progress.CompleteLevel("level_01", 3), Is.True, "首次通关应记为改进");
            Assert.That(progress.CompleteLevel("level_01", 1), Is.False, "低星重打不降级");
            Assert.That(progress.GetStars("level_01"), Is.EqualTo(3));
            Assert.That(progress.CompletedCount, Is.EqualTo(1));
            Assert.That(progress.TotalStars, Is.EqualTo(3));
        }

        [Test]
        public void CompleteLevel_IgnoresInvalidInput()
        {
            var progress = new CampaignProgress();

            Assert.That(progress.CompleteLevel("level_01", 0), Is.False, "0 星不算通关");
            Assert.That(progress.CompleteLevel("level_99", 3), Is.False, "未知关卡");
            Assert.That(progress.CompleteLevel(null, 3), Is.False);
            Assert.That(progress.CompletedCount, Is.EqualTo(0), "未通关不应计入通关数");
        }

        [Test]
        public void CompleteLevel_ClampsStarsToMax()
        {
            var progress = new CampaignProgress();

            progress.SetStars("level_01", 99);

            Assert.That(progress.GetStars("level_01"), Is.EqualTo(StarRules.MaxStars));
        }

        [Test]
        public void CompletedLevels_CountOnceEvenIfReplayedBetter()
        {
            var progress = new CampaignProgress();
            progress.CompleteLevel("level_01", 1);
            progress.CompleteLevel("level_01", 3);

            Assert.That(progress.CompletedCount, Is.EqualTo(1));
            Assert.That(progress.TotalStars, Is.EqualTo(3));
        }

        [Test]
        public void NextPlayableLevel_SkipsLockedAndCompleted()
        {
            var progress = new CampaignProgress();
            Assert.That(progress.NextPlayableLevelId(), Is.EqualTo("level_01"));

            progress.CompleteLevel("level_01", 1);
            Assert.That(progress.NextPlayableLevelId(), Is.EqualTo("level_02"),
                "level_02 已解锁（前一已转写关卡完成），优先推荐");
        }

        [Test]
        public void MaxCompletedLevelNumber_TracksHighestCleared()
        {
            var progress = new CampaignProgress();
            progress.CompleteLevel("level_04", 2);
            progress.CompleteLevel("level_01", 3);

            Assert.That(progress.MaxCompletedLevelNumber, Is.EqualTo(4));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var progress = new CampaignProgress();
            progress.CompleteLevel("level_01", 3);

            progress.Reset();

            Assert.That(progress.CompletedCount, Is.EqualTo(0));
            Assert.That(progress.IsCompleted("level_01"), Is.False);
        }
    }
}
