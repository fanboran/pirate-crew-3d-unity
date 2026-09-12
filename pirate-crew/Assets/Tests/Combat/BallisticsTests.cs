using NUnit.Framework;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// Ballistics 测试。期望值全部可由逆向文档 §5.1 / §5.4 的公式手算复核。
    /// </summary>
    [TestFixture]
    public class BallisticsTests
    {
        private const float Eps = 1e-4f;

        // ------------------------------------------------------------------
        // TwangVelocity（§5.1）
        // ------------------------------------------------------------------

        [Test]
        public void TwangVelocity_DirectionIsOppositeToCursorOffset()
        {
            // 公式：vx = dx * -0.25；dx=40 → vx = 40 * -0.25 = -10
            var (vx, vy) = Ballistics.TwangVelocity(40f, 0f, 20f);

            Assert.AreEqual(-10f, vx, Eps);
            Assert.AreEqual(0f, vy, Eps);
        }

        [Test]
        public void TwangVelocity_ZeroOffset_IsZero()
        {
            var (vx, vy) = Ballistics.TwangVelocity(0f, 0f, 20f);

            Assert.AreEqual(0f, vx, Eps);
            Assert.AreEqual(0f, vy, Eps);
        }

        [Test]
        public void TwangVelocity_BelowMaxSpeed_NotClamped()
        {
            // 拖拽 40px → 速度 = 40*0.25 = 10 < 20，不触发限速
            var (vx, vy) = Ballistics.TwangVelocity(-40f, 0f, 20f);

            Assert.AreEqual(10f, vx, Eps);
            Assert.AreEqual(0f, vy, Eps);
        }

        [Test]
        public void TwangVelocity_ExactlyAtMaxSpeed_NotScaled()
        {
            // 拖拽 80px = 满力距离 → 速度恰为 20；条件 vx²+vy² > 20² 不成立，保持原值
            var (vx, vy) = Ballistics.TwangVelocity(-80f, 0f, 20f);

            Assert.AreEqual(20f, vx, Eps);
            Assert.AreEqual(0f, vy, Eps);
        }

        [Test]
        public void TwangVelocity_OverMaxSpeed_ScalesToExactlyMax()
        {
            // 拖拽 400px（光标在左侧）→ 原始 vx = -400 * -0.25 = +100，模长 100 > 20
            // 缩放 20/100 = 0.2 → vx = +20（向右射，方向与偏移相反）
            var (vx, vy) = Ballistics.TwangVelocity(-400f, 0f, 20f);

            Assert.AreEqual(20f, vx, Eps);
            Assert.AreEqual(0f, vy, Eps);
        }

        [Test]
        public void TwangVelocity_JustOverFullForceDistance_ClampsToMax()
        {
            // 拖拽 81px → 原始速度 81*0.25 = 20.25 > 20 → 缩放到模长恰好 20
            var (vx, vy) = Ballistics.TwangVelocity(-81f, 0f, 20f);
            float magnitude = (float)System.Math.Sqrt(vx * vx + vy * vy);

            Assert.AreEqual(20f, magnitude, Eps);
            Assert.Greater(vx, 0f);   // 方向仍与偏移相反（光标在左 → 射向右）
        }

        [Test]
        public void TwangVelocity_Diagonal_ScalesBothComponents()
        {
            // dx=300, dy=400 → 原始 (-75, -100)，模长 125 > 20
            // 缩放 20/125 = 0.16 → (-12, -16)，模长 = sqrt(144+256) = 20
            var (vx, vy) = Ballistics.TwangVelocity(300f, 400f, 20f);
            float magnitude = (float)System.Math.Sqrt(vx * vx + vy * vy);

            Assert.AreEqual(-12f, vx, Eps);
            Assert.AreEqual(-16f, vy, Eps);
            Assert.AreEqual(20f, magnitude, Eps);
        }

        [Test]
        public void TwangVelocity_TwangMax30_UsesSameFormula()
        {
            // twangMax=30 时满力距离 120px；拖拽 240px（光标在左）→ 原始速度 +60 → 缩放 30/60 = 0.5 → +30
            var (vx, _) = Ballistics.TwangVelocity(-240f, 0f, 30f);

            Assert.AreEqual(30f, vx, Eps);
        }

        // ------------------------------------------------------------------
        // FullForceDragDistance（§5.1）
        // ------------------------------------------------------------------

        [Test]
        public void FullForceDragDistance_Default()
        {
            // 20 / 0.25 = 80px
            Assert.AreEqual(80f, Ballistics.FullForceDragDistance(20f), Eps);
        }

        [Test]
        public void FullForceDragDistance_TwangMax30_Is120()
        {
            // 30 / 0.25 = 120px
            Assert.AreEqual(120f, Ballistics.FullForceDragDistance(30f), Eps);
        }

        [Test]
        public void FullForceDragDistance_MatchesTwangLimit()
        {
            // 一致性：在满力距离处拖拽，速度模长应恰好等于 twangMax
            const float twangMax = 20f;
            float drag = Ballistics.FullForceDragDistance(twangMax);
            var (vx, vy) = Ballistics.TwangVelocity(-drag, 0f, twangMax);
            float magnitude = (float)System.Math.Sqrt(vx * vx + vy * vy);

            Assert.AreEqual(80f, drag, Eps);
            Assert.AreEqual(twangMax, magnitude, Eps);
        }

        // ------------------------------------------------------------------
        // PredictTrajectory（§5.1：每步先 vy += weight）
        // ------------------------------------------------------------------

        [Test]
        public void PredictTrajectory_AppliesGravityBeforeEachStep()
        {
            // start=(0,0), vx=5, vy=0, weight=1, steps=3
            // 步1: vy=1, x=5,  y=1   → (5,1)
            // 步2: vy=2, x=10, y=3   → (10,3)
            // 步3: vy=3, x=15, y=6   → (15,6)
            var pts = Ballistics.PredictTrajectory(0f, 0f, 5f, 0f, 1f, 3);

            Assert.AreEqual(3, pts.Length);
            Assert.AreEqual(5f, pts[0].x, Eps);
            Assert.AreEqual(1f, pts[0].y, Eps);
            Assert.AreEqual(10f, pts[1].x, Eps);
            Assert.AreEqual(3f, pts[1].y, Eps);
            Assert.AreEqual(15f, pts[2].x, Eps);
            Assert.AreEqual(6f, pts[2].y, Eps);
        }

        [Test]
        public void PredictTrajectory_DefaultsToFifteenPoints()
        {
            var pts = Ballistics.PredictTrajectory(0f, 0f, 0f, 0f);

            Assert.AreEqual(15, pts.Length);
        }

        [Test]
        public void PredictTrajectory_OneStep()
        {
            // start=(10,20), vx=-3, vy=5, weight=2, steps=1
            // 步1: vy=7, x=7, y=27 → (7,27)
            var pts = Ballistics.PredictTrajectory(10f, 20f, -3f, 5f, 2f, 1);

            Assert.AreEqual(1, pts.Length);
            Assert.AreEqual(7f, pts[0].x, Eps);
            Assert.AreEqual(27f, pts[0].y, Eps);
        }

        [Test]
        public void PredictTrajectory_StartPositionNotIncluded()
        {
            // 返回的是采样点（不含起点）：vx=0,vy=0,weight=1 时第一点应为 (0,1) 而非 (0,0)
            var pts = Ballistics.PredictTrajectory(100f, 200f, 0f, 0f, 1f, 1);

            Assert.AreEqual(100f, pts[0].x, Eps);
            Assert.AreEqual(201f, pts[0].y, Eps);
        }

        // ------------------------------------------------------------------
        // IntegrateGroundContact（§5.4）
        // ------------------------------------------------------------------

        [Test]
        public void IntegrateGroundContact_BouncesAndAppliesFriction()
        {
            // vy = 3 * -0.2 = -0.6；|vx| = |5| - 2 = 3
            var (vx, vy) = Ballistics.IntegrateGroundContact(5f, 3f, 2f, 0.2f);

            Assert.AreEqual(3f, vx, Eps);
            Assert.AreEqual(-0.6f, vy, Eps);
        }

        [Test]
        public void IntegrateGroundContact_FrictionDoesNotGoNegative()
        {
            // |vx|=1 - 2 = -1 → 下限 0
            var (vx, _) = Ballistics.IntegrateGroundContact(1f, 0f, 2f, 0.2f);

            Assert.AreEqual(0f, vx, Eps);
        }

        [Test]
        public void IntegrateGroundContact_FrictionExactlyConsumesSpeed()
        {
            // |vx|=2 - 2 = 0
            var (vx, _) = Ballistics.IntegrateGroundContact(2f, 0f, 2f, 0.2f);

            Assert.AreEqual(0f, vx, Eps);
        }

        [Test]
        public void IntegrateGroundContact_PreservesNegativeDirection()
        {
            // vx=-5，摩擦 2 → -(|-5|-2) = -3（方向不变）
            var (vx, _) = Ballistics.IntegrateGroundContact(-5f, 0f, 2f, 0.2f);

            Assert.AreEqual(-3f, vx, Eps);
        }

        [Test]
        public void IntegrateGroundContact_ZeroVelocityStaysZero()
        {
            var (vx, vy) = Ballistics.IntegrateGroundContact(0f, 0f, 2f, 0.2f);

            Assert.AreEqual(0f, vx, Eps);
            Assert.AreEqual(0f, vy, Eps);   // 0 * -0.2 = 0
        }

        // ------------------------------------------------------------------
        // IntegrateWallContact（§5.4）
        // ------------------------------------------------------------------

        [Test]
        public void IntegrateWallContact_ReversesHorizontalOnly()
        {
            // vx = 5 * -0.4 = -2；vy 不变
            var (vx, vy) = Ballistics.IntegrateWallContact(5f, 3f);

            Assert.AreEqual(-2f, vx, Eps);
            Assert.AreEqual(3f, vy, Eps);
        }

        [Test]
        public void IntegrateWallContact_NegativeVxBecomesPositive()
        {
            // vx = -5 * -0.4 = 2
            var (vx, _) = Ballistics.IntegrateWallContact(-5f, 0f);

            Assert.AreEqual(2f, vx, Eps);
        }
    }
}
