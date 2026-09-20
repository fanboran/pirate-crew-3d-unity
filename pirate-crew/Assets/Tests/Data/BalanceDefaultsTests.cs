using NUnit.Framework;
using PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// BalanceConfig.Defaults 的纯 C# 断言（对应静态逆向文档 §5.1 / §5.3 / §3.1 / §1 / §7.3 / §4.1）。
    /// 直接断言 <see cref="BalanceConfig.Defaults"/>，不实例化 ScriptableObject。
    /// </summary>
    public class BalanceDefaultsTests
    {
        [Test]
        public void TwangConstants_MatchDocument()
        {
            Assert.That(BalanceConfig.Defaults.TwangForceScale, Is.EqualTo(0.25f), "§5.1");
            Assert.That(BalanceConfig.Defaults.DefaultTwangMax, Is.EqualTo(20f), "§5.1");
            Assert.That(BalanceConfig.Defaults.HighTwangMax, Is.EqualTo(30f), "§5.1");
        }

        [Test]
        public void ExplosionAndKnockback_MatchDocument()
        {
            Assert.That(BalanceConfig.Defaults.ExplosionRadiusPadding, Is.EqualTo(20f), "§5.3");
            Assert.That(BalanceConfig.Defaults.KnockbackCoefficient, Is.EqualTo(0.06f), "§5.3");
            Assert.That(BalanceConfig.Defaults.KnockbackHorizontal, Is.EqualTo(5f), "§5.3");
            Assert.That(BalanceConfig.Defaults.KnockbackVertical, Is.EqualTo(6f), "§5.3");
        }

        [Test]
        public void PhysicsDefaults_MatchDocument()
        {
            Assert.That(BalanceConfig.Defaults.DefaultFriction, Is.EqualTo(2f), "§4.1 / §5.2");
            Assert.That(BalanceConfig.Defaults.DefaultBounce, Is.EqualTo(0.2f), "§4.1 / §5.2");
        }

        [Test]
        public void TurnAndFrameConstants_MatchDocument()
        {
            Assert.That(BalanceConfig.Defaults.InactivityFramesToAdvance, Is.EqualTo(10), "§3.1");
            Assert.That(BalanceConfig.Defaults.OriginalFps, Is.EqualTo(25), "§1");
        }

        [Test]
        public void ScoreConstants_MatchDocument()
        {
            Assert.That(BalanceConfig.Defaults.ScoreHealthWeight, Is.EqualTo(20f), "§7.3");
            Assert.That(BalanceConfig.Defaults.ScoreTurnPenalty, Is.EqualTo(25f), "§7.3");
            Assert.That(BalanceConfig.Defaults.ScoreFloorPerLevel, Is.EqualTo(10f), "§7.3");
        }

        [Test]
        public void CharacterAabb_MatchDocument()
        {
            Assert.That(BalanceConfig.Defaults.CharHalfWidth, Is.EqualTo(6f), "§4.1");
            Assert.That(BalanceConfig.Defaults.CharHalfHeight, Is.EqualTo(8f), "§4.1");
        }

        /// <summary>
        /// 交叉一致性：Balance 的角色尺寸 / 弹弓值应与 CrewCatalog 的 §4.1 值一致，
        /// 防止两处各自维护后漂移。
        /// </summary>
        [Test]
        public void CrossCheck_WithCrewCatalog_AndWeaponCatalog()
        {
            Assert.That(BalanceConfig.Defaults.CharHalfWidth, Is.EqualTo(CrewCatalog.LeftExtent));
            Assert.That(BalanceConfig.Defaults.CharHalfHeight, Is.EqualTo(CrewCatalog.TopExtent));
            Assert.That(BalanceConfig.Defaults.DefaultTwangMax, Is.EqualTo(CrewCatalog.TwangMaxForce));
            Assert.That(BalanceConfig.Defaults.DefaultFriction, Is.EqualTo(CrewCatalog.Friction));
            Assert.That(BalanceConfig.Defaults.DefaultBounce, Is.EqualTo(CrewCatalog.Bounce));

            Assert.That(BalanceConfig.Defaults.TwangForceScale, Is.EqualTo(0.25f));
            Assert.That(BalanceConfig.Defaults.ExplosionRadiusPadding, Is.EqualTo(20f));
            Assert.That(BalanceConfig.Defaults.KnockbackCoefficient, Is.EqualTo(0.06f));
        }
    }
}
