using NUnit.Framework;
using PirateCrew.Data;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileLifetimeRules"/> 复用/寿命规则测试（B9 审计回填，49dc3288 误删后恢复）。
    /// 覆盖：跨回合常驻 vs 限本回合、引爆即离场、piecesOfEight 8 次复用的规则侧口径。
    /// 库存槽位的消耗行为（ConsumeAt 语义）由 <see cref="WeaponInventoryTests"/> 钉住，此处不重复。
    /// </summary>
    [TestFixture]
    public class ProjectileLifetimeRulesTests
    {
        static WeaponStats Stats(WeaponId id) => WeaponCatalog.Get(id);

        [Test]
        public void PersistentWeapons_SurviveTurnEnd()
        {
            // 目录 limitedToTurn=false：mine / gunpowderBarrel / woodenCrate / cannon 跨回合常驻
            //（地雷埋伏到对手回合、箱体掩体长期使用）——回合结束不销毁。
            Assert.IsFalse(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.Mine)));
            Assert.IsFalse(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.GunpowderBarrel)));
            Assert.IsFalse(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.WoodenCrate)));
            Assert.IsFalse(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.Cannon)));
        }

        [Test]
        public void TurnScopedWeapons_AreDestroyedAtTurnEnd()
        {
            Assert.IsTrue(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.CherryBomb)));
            Assert.IsTrue(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.Cannonball)));
            Assert.IsTrue(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.PiecesOfEight)));
            Assert.IsTrue(ProjectileLifetimeRules.ShouldDestroyAtTurnEnd(Stats(WeaponId.Dynamite)));
        }

        [Test]
        public void Detonation_AlwaysRemovesProjectileFromField()
        {
            // 复用是「库存槽位」语义，不是留弹体：引爆即从场上移除。
            foreach (WeaponStats stats in WeaponCatalog.All)
                Assert.IsTrue(ProjectileLifetimeRules.ShouldDestroyAfterDetonation(stats), stats.DisplayName);
        }

        [Test]
        public void ReuseCount_IsEightForPiecesOfEight_ElseOne()
        {
            Assert.AreEqual(8, ProjectileLifetimeRules.ReuseCount(Stats(WeaponId.PiecesOfEight)));
            Assert.IsTrue(ProjectileLifetimeRules.IsReusable(Stats(WeaponId.PiecesOfEight)));
            Assert.AreEqual(1, ProjectileLifetimeRules.ReuseCount(Stats(WeaponId.CherryBomb)));
            Assert.IsFalse(ProjectileLifetimeRules.IsReusable(Stats(WeaponId.CherryBomb)));
        }

        [Test]
        public void ReuseCount_ZeroFallsBackToOne()
        {
            // 目录 MaxReuses=0（非复用型）→ 每件用掉即没（1 次）。
            Assert.AreEqual(1, ProjectileLifetimeRules.ReuseCount(Stats(WeaponId.Cannonball)));
        }
    }
}
