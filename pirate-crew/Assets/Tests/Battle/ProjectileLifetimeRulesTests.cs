using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileLifetimeRules"/> + <see cref="WeaponInventory"/> 复用测试。
    /// 覆盖：跨回合常驻 vs 用完即销毁、piecesOfEight 8 次复用。
    /// </summary>
    [TestFixture]
    public class ProjectileLifetimeRulesTests
    {
        static WeaponStats Stats(WeaponId id) => WeaponCatalog.Get(id);

        [Test]
        public void PersistentWeapons_SurviveTurnEnd()
        {
            // §5.2 limitedToTurn=false：mine / gunpowderBarrel / woodenCrate / cannon
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
        public void PiecesOfEightInventory_SurvivesSevenUsesAndConsumesOnEighth()
        {
            // §5.2 / §8.4：8 次复用。每次 ConsumeEquipped 递减 UsesRemaining；
            // 前 7 次槽位保留（复位可再用），第 8 次移除。
            var inventory = new WeaponInventory(new[] { new WeaponStack(WeaponId.PiecesOfEight, 1) });
            Assert.AreEqual(1, inventory.Count);
            Assert.IsTrue(inventory.Equip(0));
            Assert.AreEqual(8, inventory.UsesRemainingAt(0));

            for (int i = 1; i <= 7; i++)
            {
                Assert.IsFalse(inventory.ConsumeEquipped(), "第 " + i + " 次不应移除槽位");
                Assert.AreEqual(1, inventory.Count);
                Assert.AreEqual(8 - i, inventory.UsesRemainingAt(0));
            }

            Assert.IsTrue(inventory.ConsumeEquipped(), "第 8 次应移除槽位");
            Assert.AreEqual(0, inventory.Count);
        }

        [Test]
        public void NormalWeapon_IsConsumedAfterSingleUse()
        {
            var inventory = new WeaponInventory(new[] { new WeaponStack(WeaponId.CherryBomb, 1) });
            Assert.IsTrue(inventory.Equip(0));
            Assert.IsTrue(inventory.ConsumeEquipped());
            Assert.AreEqual(0, inventory.Count);
        }

        [Test]
        public void InfiniteWeapon_IsNeverConsumed()
        {
            var inventory = new WeaponInventory(new[] { new WeaponStack(WeaponId.Cannonball, 10) });
            Assert.IsTrue(inventory.Equip(0));
            for (int i = 0; i < 12; i++)
                Assert.IsFalse(inventory.ConsumeEquipped());
            Assert.AreEqual(1, inventory.Count);
        }
    }
}
