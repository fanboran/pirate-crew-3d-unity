using NUnit.Framework;

namespace PirateCrew.Combat.Tests
{
    /// <summary>
    /// Ballistics 测试（米制重立后只覆盖撞地/撞墙积分——弹弓 twang 三件已随投掷米制重立退役，
    /// 初速口径见 PirateCrew.Battle.StandardThrowRules 与其测试）。
    /// </summary>
    [TestFixture]
    public class BallisticsTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void IntegrateGroundContact_BouncesUp_AndFrictionsHorizontal()
        {
            // vy=5、bounce=0.2 → -1.0（反弹向上）；vx=3、friction=1 → 2（不反向）。
            (float vx, float vy) = Ballistics.IntegrateGroundContact(3f, 5f, friction: 1f, bounce: 0.2f);
            Assert.AreEqual(2f, vx, Eps);
            Assert.AreEqual(-1.0f, vy, Eps);
        }

        [Test]
        public void IntegrateGroundContact_FrictionDoesNotReverse()
        {
            // |vx| < friction → 下限 0，不出现反向。
            (float vx, _) = Ballistics.IntegrateGroundContact(0.5f, 5f, friction: 2f, bounce: 0.2f);
            Assert.AreEqual(0f, vx, Eps);
            (float nvx, _) = Ballistics.IntegrateGroundContact(-0.5f, 5f, friction: 2f, bounce: 0.2f);
            Assert.AreEqual(0f, nvx, Eps);
        }

        [Test]
        public void IntegrateGroundContact_ZeroBounce_KillsVertical()
        {
            (float _, float vy) = Ballistics.IntegrateGroundContact(3f, 5f, friction: 0f, bounce: 0f);
            Assert.AreEqual(0f, vy, Eps);
        }

        [Test]
        public void IntegrateWallContact_ReversesHorizontal_KeepsVertical()
        {
            (float vx, float vy) = Ballistics.IntegrateWallContact(3f, 5f);
            Assert.AreEqual(3f * Ballistics.WallBounceScale, vx, Eps);
            Assert.AreEqual(5f, vy, Eps);
        }
    }
}
