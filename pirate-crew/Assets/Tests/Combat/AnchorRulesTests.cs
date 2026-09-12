using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// <see cref="AnchorRules"/> 测试（§5.2 anchor 行 / §5.1 速度汇总）。
    /// 坐标一律 Flash px（x 右、y 下），算式写在断言旁。
    /// </summary>
    [TestFixture]
    public class AnchorRulesTests
    {
        [Test]
        public void Constants_MatchSection5_2()
        {
            Assert.AreEqual(-200f, AnchorRules.SpawnFlashY);
            Assert.AreEqual(40f, AnchorRules.FallSpeed);
            Assert.AreEqual(48f, AnchorRules.HorizontalHalfExtent);
            Assert.AreEqual(96f, AnchorRules.TopExtent);
            Assert.AreEqual(0f, AnchorRules.BottomExtent);
            Assert.AreEqual(64f, AnchorRules.HitBandAboveAnchor);
            Assert.AreEqual(60f, AnchorRules.FixedDamage);
            Assert.AreEqual(30, AnchorRules.LandHoldFrames);
            Assert.AreEqual(10, AnchorRules.FadeFrames);
            Assert.AreEqual(40, AnchorRules.TotalFramesAfterLanding);
        }

        [Test]
        public void StepY_FallsAtConstant40PxPerFrame()
        {
            // §5.1「anchor 恒 vy=40 从 y=-200 下落」——等速、无重力。
            Assert.AreEqual(-160f, AnchorRules.StepY(-200f), 1e-4f);
            Assert.AreEqual(-80f, AnchorRules.StepY(-200f, 3), 1e-4f);
        }

        [Test]
        public void ShouldHit_TrueInsideHorizontalAndVerticalBand()
        {
            // anchor 在 (100, 200)；目标 (147, 199)：|147-100|=47 < 48，且 136 < 199 < 200 → 命中。
            Assert.IsTrue(AnchorRules.ShouldHit(100f, 200f, 147f, 199f));
            Assert.IsTrue(AnchorRules.ShouldHit(100f, 200f, 53f, 137f));
        }

        [Test]
        public void ShouldHit_FalseAtHorizontalBoundary_StrictLessThan48()
        {
            // |x-anchorX| == 48 不满足严格 < 48（§5.2 逐字）。
            Assert.IsFalse(AnchorRules.ShouldHit(100f, 200f, 148f, 199f));
            Assert.IsFalse(AnchorRules.ShouldHit(100f, 200f, 52f, 199f));
        }

        [Test]
        public void ShouldHit_FalseOutsideVerticalBand_StrictInequalities()
        {
            // anchorY-64 == 136 与 anchorY == 200 都是开区间端点。
            Assert.IsFalse(AnchorRules.ShouldHit(100f, 200f, 100f, 136f), "y == anchorY-64 不命中");
            Assert.IsFalse(AnchorRules.ShouldHit(100f, 200f, 100f, 200f), "y == anchorY 不命中");
            Assert.IsTrue(AnchorRules.ShouldHit(100f, 200f, 100f, 199f));
            Assert.IsTrue(AnchorRules.ShouldHit(100f, 200f, 100f, 136.001f));
            Assert.IsFalse(AnchorRules.ShouldHit(100f, 200f, 100f, 135.999f));
        }

        [Test]
        public void HasLanded_WhenFlashYReachesGround()
        {
            Assert.IsFalse(AnchorRules.HasLanded(499f, 500f));
            Assert.IsTrue(AnchorRules.HasLanded(500f, 500f));
            Assert.IsTrue(AnchorRules.HasLanded(520f, 500f));
        }

        [Test]
        public void LandingTimeline_Hold30ThenFade10()
        {
            Assert.IsTrue(AnchorRules.IsAlive(0));
            Assert.IsTrue(AnchorRules.IsAlive(39));
            Assert.IsFalse(AnchorRules.IsAlive(40));

            Assert.IsFalse(AnchorRules.IsFading(29));
            Assert.IsTrue(AnchorRules.IsFading(30));
            Assert.IsTrue(AnchorRules.IsFading(39));
            Assert.IsFalse(AnchorRules.IsFading(40));

            Assert.IsFalse(AnchorRules.ShouldDestroy(39));
            Assert.IsTrue(AnchorRules.ShouldDestroy(40));
        }

        [Test]
        public void AlphaAfterLanding_LinearOver10FadeFrames()
        {
            Assert.AreEqual(1f, AnchorRules.AlphaAfterLanding(0), 1e-4f);
            Assert.AreEqual(1f, AnchorRules.AlphaAfterLanding(30), 1e-4f);
            Assert.AreEqual(0.5f, AnchorRules.AlphaAfterLanding(35), 1e-4f);
            Assert.AreEqual(0.9f, AnchorRules.AlphaAfterLanding(31), 1e-4f);
            Assert.AreEqual(0f, AnchorRules.AlphaAfterLanding(40), 1e-4f);
            Assert.AreEqual(0f, AnchorRules.AlphaAfterLanding(99), 1e-4f);
        }
    }
}
