using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// <see cref="VoodooDollRules"/> 测试（§5.2 voodooDoll 行 / §4.2 目标选择 30px）。
    /// </summary>
    [TestFixture]
    public class VoodooDollRulesTests
    {
        [Test]
        public void Constants_MatchSection5_2_And4_2()
        {
            Assert.AreEqual(30f, VoodooDollRules.TargetPickRadius);
            Assert.AreEqual(10, VoodooDollRules.CameraSwitchFrames);
            Assert.AreEqual(10, VoodooDollRules.VelocityTransferDelayFrames);
            Assert.AreEqual(20, VoodooDollRules.TotalTimelineFrames);
        }

        [Test]
        public void PickTargetIndex_ReturnsNearestWithin30Px()
        {
            // §4.2 pickNearestEnemy(30px)：100 太远，20 最近 → 索引 1。
            var distances = new List<float> { 100f, 20f, 40f };
            Assert.AreEqual(1, VoodooDollRules.PickTargetIndex(distances));
        }

        [Test]
        public void PickTargetIndex_ReturnsMinusOneWhenNoneInRange()
        {
            Assert.AreEqual(-1, VoodooDollRules.PickTargetIndex(new List<float> { 40f, 50f, 31f }));
            Assert.AreEqual(-1, VoodooDollRules.PickTargetIndex(new List<float>()));
            Assert.AreEqual(-1, VoodooDollRules.PickTargetIndex(null));
        }

        [Test]
        public void PickTargetIndex_IgnoresNegativeDistances()
        {
            Assert.AreEqual(1, VoodooDollRules.PickTargetIndex(new List<float> { -1f, 29f }));
        }

        [Test]
        public void PickTargetIndex_Boundary30_IsInclusive()
        {
            Assert.AreEqual(0, VoodooDollRules.PickTargetIndex(new List<float> { 30f }));
            Assert.AreEqual(-1, VoodooDollRules.PickTargetIndex(new List<float> { 30.001f }));
        }

        [Test]
        public void Timeline_SwitchAt10_TransferAt20()
        {
            Assert.IsFalse(VoodooDollRules.ShouldSwitchCamera(9));
            Assert.IsTrue(VoodooDollRules.ShouldSwitchCamera(10));
            Assert.IsTrue(VoodooDollRules.ShouldSwitchCamera(19));

            Assert.IsFalse(VoodooDollRules.ShouldTransferVelocity(19));
            Assert.IsTrue(VoodooDollRules.ShouldTransferVelocity(20));

            Assert.IsFalse(VoodooDollRules.IsTimelineComplete(19));
            Assert.IsTrue(VoodooDollRules.IsTimelineComplete(20));
        }

        [Test]
        public void TryTransferVelocity_CopiesDollVelocityOnceTimelineComplete()
        {
            Assert.IsFalse(VoodooDollRules.TryTransferVelocity(19, 12f, -8f, out _, out _));
            Assert.IsTrue(VoodooDollRules.TryTransferVelocity(20, 12f, -8f, out float vx, out float vy));
            Assert.AreEqual(12f, vx, 1e-4f);
            Assert.AreEqual(-8f, vy, 1e-4f);
        }
    }
}
