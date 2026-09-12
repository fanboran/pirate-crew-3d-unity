using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// <see cref="SeagullRules"/> 测试（§5.2 seagull 行 / §6.3 AI 评分）。
    /// </summary>
    [TestFixture]
    public class SeagullRulesTests
    {
        [Test]
        public void Constants_MatchSection5_2_And6_3()
        {
            Assert.AreEqual(-300f, SeagullRules.SpawnFlashX);
            Assert.AreEqual(10f, SeagullRules.FlightSpeed);
            Assert.AreEqual(275f, SeagullRules.ExitRightMargin);
            Assert.AreEqual(50f, SeagullRules.BombExplosionSize);
            Assert.AreEqual(50f, SeagullRules.BombExplosionMaxDamage);
            Assert.AreEqual(40f, SeagullRules.AiHitScoreRadius);
            Assert.AreEqual(100f, SeagullRules.AiHeightAboveTargetMin);
            Assert.AreEqual(100f, SeagullRules.AiHeightAboveTargetRange);
            Assert.AreEqual(2, SeagullRules.AiMinimumShots);
        }

        [Test]
        public void StepX_FliesRightAt10PxPerFrame()
        {
            Assert.AreEqual(-290f, SeagullRules.StepX(-300f), 1e-4f);
        }

        [Test]
        public void ExitFlashX_IsLevelWidthTimes32Plus275()
        {
            // 17 瓦片 → 17*32 + 275 = 544 + 275 = 819。
            Assert.AreEqual(819f, SeagullRules.ExitFlashX(17f), 1e-3f);
            Assert.IsFalse(SeagullRules.HasExitedRight(819f, 17f), "等于阈值不算飞出（严格 >）");
            Assert.IsTrue(SeagullRules.HasExitedRight(819.001f, 17f));
        }

        [Test]
        public void CanFinish_RequiresExitedAndNoBombInFlight()
        {
            Assert.IsFalse(SeagullRules.CanFinish(800f, 17f, false), "未飞出不能结束");
            Assert.IsFalse(SeagullRules.CanFinish(900f, 17f, true), "仍有弹在飞不能结束");
            Assert.IsTrue(SeagullRules.CanFinish(900f, 17f, false));
        }

        [Test]
        public void PickHeight_Is100To200AboveTargetHighestY()
        {
            // §6.3：高度 = 敌方最高 y − 100 − random*100。random=0 → 高 100；random=1 → 高 200。
            Assert.AreEqual(400f, SeagullRules.PickHeight(500f, 0f), 1e-3f);
            Assert.AreEqual(300f, SeagullRules.PickHeight(500f, 1f), 1e-3f);
            Assert.AreEqual(350f, SeagullRules.PickHeight(500f, 0.5f), 1e-3f);
        }

        [Test]
        public void HitScore_OneMinusDistanceOver40()
        {
            Assert.AreEqual(1f, SeagullRules.HitScore(0f), 1e-4f);
            Assert.AreEqual(0.5f, SeagullRules.HitScore(20f), 1e-4f);
            Assert.AreEqual(0f, SeagullRules.HitScore(40f), 1e-4f);
            Assert.AreEqual(0f, SeagullRules.HitScore(41f), 1e-4f);
        }

        [Test]
        public void FriendlyFirePenalty_Is1Point5MinusDistanceOver40()
        {
            Assert.AreEqual(1.5f, SeagullRules.FriendlyFirePenalty(0f), 1e-4f);
            Assert.AreEqual(1.0f, SeagullRules.FriendlyFirePenalty(20f), 1e-4f);
            Assert.AreEqual(0f, SeagullRules.FriendlyFirePenalty(40f), 1e-4f);
        }

        [Test]
        public void IsPositiveScore_And_HasEnoughShots()
        {
            Assert.IsTrue(SeagullRules.IsPositiveScore(0.01f));
            Assert.IsFalse(SeagullRules.IsPositiveScore(0f));
            Assert.IsFalse(SeagullRules.IsPositiveScore(-1f));

            Assert.IsFalse(SeagullRules.HasEnoughShots(0));
            Assert.IsFalse(SeagullRules.HasEnoughShots(1));
            Assert.IsTrue(SeagullRules.HasEnoughShots(2), "§6.3 要求 shots.length > 1");
        }
    }
}
