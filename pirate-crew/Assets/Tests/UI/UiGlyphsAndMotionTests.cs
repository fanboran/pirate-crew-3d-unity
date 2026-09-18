using NUnit.Framework;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests.UI
{
    /// <summary>
    /// <see cref="UiGlyphs"/> 几何判定与 <see cref="UiMotionRules"/> 新曲线（ghost / pop）的无头断言。
    /// </summary>
    public sealed class UiGlyphsAndMotionTests
    {
        const float Half = 24f;

        [Test]
        public void Glyphs_CenterInside_FarOutside()
        {
            // 中心语义：这些符号中心是实心的。
            Assert.IsTrue(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.Crosshair, 0f, 0f, Half), "准星中心有轴点");
            Assert.IsTrue(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.MovePad, 0f, 0f, Half), "移动盘中心有圆");
            Assert.IsTrue(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.Skull, 0f, -4f, Half), "骷髅额头是实的");
            Assert.IsTrue(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.Helm, 0f, 0f, Half), "舵轮中心有毂");
            Assert.IsTrue(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.Eye, 0f, 0f, Half), "眼睛中心是瞳孔");
            Assert.IsTrue(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.Star, 0f, 10f, Half), "星有朝上的尖");

            // 远点必外（所有符号）。
            foreach (UiGlyphs.Glyph glyph in System.Enum.GetValues(typeof(UiGlyphs.Glyph)))
            {
                Assert.IsFalse(UiGlyphs.InsideGlyph(glyph, Half - 0.5f, Half - 0.5f, Half),
                    "{0} 的画布角落应是透明的", glyph);
            }
        }

        [Test]
        public void Skull_EyesAreHollow()
        {
            Assert.IsFalse(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.Skull, -3.9f, 0.5f, Half), "左眼洞");
            Assert.IsFalse(UiGlyphs.InsideGlyph(UiGlyphs.Glyph.Skull, 3.9f, 0.5f, Half), "右眼洞");
        }

        [Test]
        public void SdRoundRect_StraightEdgeDistanceMatchesRadius()
        {
            // 直边中点（远离圆角）：像素中心 y=half-0.5 贴边时 SDF ≈ 0，向内为负（形状内）。
            float sdEdge = CartoonSpriteFactory.SdRoundRect(0f, 23.5f, 23.5f, 8f);
            Assert.LessOrEqual(Mathf.Abs(sdEdge), 0.1f, "直边 SDF ≈ 0");
            float sdInside = CartoonSpriteFactory.SdRoundRect(0f, 20f, 23.5f, 8f);
            Assert.Less(sdInside, -2f, "向内 3.5px 的 SDF < -2");
            float sdOutside = CartoonSpriteFactory.SdRoundRect(0f, 26f, 23.5f, 8f);
            Assert.Greater(sdOutside, 1f, "向外 2px 的 SDF > 1");
        }

        // ------------------------------------------------------------------
        // damage ghost：只降不升
        // ------------------------------------------------------------------

        [Test]
        public void StepGhost_TracksDownSlowly_SnapsUpImmediately()
        {
            // 掉血：白条慢速追（走一大步后仍高于主填充 = 残影存在）。
            float ghost = UiMotionRules.StepGhost(1f, 0.5f, 1f / 60f);
            Assert.Greater(ghost, 0.5f, "掉血后残影应还悬在上方");
            Assert.Less(ghost, 1f, "残影也应在往下追");

            // 回血：白条立即钉到新值，不留白。
            Assert.AreEqual(0.9f, UiMotionRules.StepGhost(0.5f, 0.9f, 1f / 60f), "回血时残影立即让位");
        }

        [Test]
        public void StepGhost_EventuallySettlesAtFill()
        {
            float ghost = 1f;
            for (int i = 0; i < 600; i++)                       // 模拟 10 秒
                ghost = UiMotionRules.StepGhost(ghost, 0.3f, 1f / 60f);
            Assert.AreEqual(0.3f, ghost, "10 秒后残影应追平主填充");
        }

        // ------------------------------------------------------------------
        // pop / back-out
        // ------------------------------------------------------------------

        [Test]
        public void EaseOutBack_OvershootsThenSettles()
        {
            Assert.AreEqual(0f, UiMotionRules.EaseOutBack(0f), 0.001f);
            Assert.AreEqual(1f, UiMotionRules.EaseOutBack(1f), 0.001f);

            float max = 0f;
            for (int i = 0; i <= 100; i++)
                max = Mathf.Max(max, UiMotionRules.EaseOutBack(i / 100f));
            Assert.Greater(max, 1.05f, "back-out 中段应过冲超过 1");
        }

        [Test]
        public void PopScale_StartsBelowOne_EndsAtOne()
        {
            Assert.Less(UiMotionRules.PopScale(0f), 1f, "pop 起手应小于 1");
            Assert.AreEqual(1f, UiMotionRules.PopScale(1f), 0.001f, "pop 终点应回到 1");

            float max = 0f;
            for (int i = 0; i <= 100; i++)
                max = Mathf.Max(max, UiMotionRules.PopScale(i / 100f));
            Assert.Greater(max, 1.05f, "pop 中段应过冲（弹一下）");
        }
    }
}
