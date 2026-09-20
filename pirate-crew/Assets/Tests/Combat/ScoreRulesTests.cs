using NUnit.Framework;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// ScoreRules 测试。公式出自逆向文档 §3.3 / §7.3：
    /// floor(avgHealth*20 - totalTurnsTaken*25)，下限 levelIndex*10。
    /// </summary>
    [TestFixture]
    public class ScoreRulesTests
    {
        [Test]
        public void LevelScore_FullHealthNoTurns_HighScore()
        {
            // floor(100*20 - 0*25) = 2000；max(2000, 1*10=10) = 2000（下限未触发）
            Assert.AreEqual(2000, ScoreRules.LevelScore(100f, 0, 1));
        }

        [Test]
        public void LevelScore_LowerBoundNotTriggered()
        {
            // 关 4：floor(100*20 - 78*25) = floor(2000-1950) = 50；max(50, 40) = 50 → 不触发
            Assert.AreEqual(50, ScoreRules.LevelScore(100f, 78, 4));
        }

        [Test]
        public void LevelScore_LowerBoundTriggered()
        {
            // 关 4：floor(100*20 - 79*25) = floor(2000-1975) = 25；max(25, 40) = 40 → 触发下限
            Assert.AreEqual(40, ScoreRules.LevelScore(100f, 79, 4));
        }

        [Test]
        public void LevelScore_NegativeRawScore_ClampedToLevelFloor()
        {
            // 关 3：floor(100*20 - 100*25) = floor(-500) = -500；max(-500, 30) = 30
            Assert.AreEqual(30, ScoreRules.LevelScore(100f, 100, 3));
        }

        [Test]
        public void LevelScore_FloorIsAppliedNotTruncation()
        {
            // floor(1.02*20) = floor(20.4) = 20（截断也是 20，但语义是 floor）；max(20, 10) = 20
            Assert.AreEqual(20, ScoreRules.LevelScore(1.02f, 0, 1));
        }

        [Test]
        public void LevelScore_FractionalHealth_TruncatesDown()
        {
            // floor(66.5*20 - 2*25) = floor(1330 - 50) = 1280
            Assert.AreEqual(1280, ScoreRules.LevelScore(66.5f, 2, 1));
        }

        [Test]
        public void LevelScore_ZeroHealthNoTurns_UsesLevelFloor()
        {
            // floor(0*20 - 0*25) = 0；max(0, 2*10=20) = 20
            Assert.AreEqual(20, ScoreRules.LevelScore(0f, 0, 2));
        }

        [Test]
        public void LevelScore_LowerBoundScalesWithLevelIndex()
        {
            // 差表现统一落到下限：levelIndex 越大下限越高
            Assert.AreEqual(10, ScoreRules.LevelScore(0f, 50, 1));
            Assert.AreEqual(150, ScoreRules.LevelScore(0f, 50, 15));
            Assert.AreEqual(330, ScoreRules.LevelScore(0f, 50, 33));
        }

        [Test]
        public void LevelScore_ExactFloorBoundary_KeepsRawScore()
        {
            // 关 1：floor(50*20 - 0) = 1000；max(1000, 10) = 1000
            Assert.AreEqual(1000, ScoreRules.LevelScore(50f, 0, 1));
        }
    }
}
