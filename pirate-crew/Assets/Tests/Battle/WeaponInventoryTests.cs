using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="WeaponInventory"/> 测试。
    /// 覆盖：count==10 无限（§5.5）、有限武器消耗、保底 cannonball（§3.2）、
    /// piecesOfEight 8 次复用（§5.2）、装备/卸下与索引稳定性。
    /// </summary>
    [TestFixture]
    public class WeaponInventoryTests
    {
        static WeaponInventory Make(params WeaponStack[] stacks)
        {
            return new WeaponInventory(new List<WeaponStack>(stacks));
        }

        // ------------------------------------------------------------------
        // 展开
        // ------------------------------------------------------------------

        [Test]
        public void InfiniteStack_ExpandsToOneSlot()
        {
            // count=10 表示无限（§5.5：if n == 10 → infiniteWeapons.push）。
            WeaponInventory inv = Make(new WeaponStack(WeaponId.CherryBomb, 10));
            Assert.AreEqual(1, inv.Count);
            Assert.IsTrue(inv.IsInfiniteAt(0));
            Assert.AreEqual(int.MaxValue, inv.UsesRemainingAt(0));
        }

        [Test]
        public void FiniteStack_ExpandsToNCountSlots()
        {
            // dynamite×5 → 5 个槽位
            WeaponInventory inv = Make(new WeaponStack(WeaponId.Dynamite, 5));
            Assert.AreEqual(5, inv.Count);
            Assert.IsFalse(inv.IsInfiniteAt(0));
            Assert.AreEqual(1, inv.UsesRemainingAt(0), "普通武器复用次数为 1");
            Assert.AreEqual(0, inv.FirstIndexOf(WeaponId.Dynamite));
        }

        [Test]
        public void ZeroCountStack_IsNotAdded()
        {
            WeaponInventory inv = Make(new WeaponStack(WeaponId.CherryBomb, 0));
            Assert.IsFalse(inv.HasAny);
            Assert.AreEqual(0, inv.Count);
        }

        // ------------------------------------------------------------------
        // 消耗 / 无限
        // ------------------------------------------------------------------

        [Test]
        public void ConsumeInfinite_ReturnsFalseAndKeepsCount()
        {
            WeaponInventory inv = Make(new WeaponStack(WeaponId.CherryBomb, 10));
            Assert.IsFalse(inv.ConsumeAt(0), "无限武器不消耗（返回 false 表示未移除）");
            Assert.AreEqual(1, inv.Count);
        }

        [Test]
        public void ConsumeFinite_RemovesOneSlotPerUse()
        {
            // dynamite×2 → 2 个槽位，每次 ConsumeAt 移除 1 个（普通武器每槽 1 次）。
            WeaponInventory inv = Make(new WeaponStack(WeaponId.Dynamite, 2));
            Assert.IsTrue(inv.ConsumeAt(0));
            Assert.AreEqual(1, inv.Count);
            Assert.IsTrue(inv.ConsumeAt(0));
            Assert.AreEqual(0, inv.Count);
        }

        [Test]
        public void EquippedIndex_IsFixedAfterRemovalOfLowerSlot()
        {
            // 槽位：[dynamite, cherryBomb(∞)]，装备 index 1；消耗 index 0 后装备索引应变为 0。
            WeaponInventory inv = Make(
                new WeaponStack(WeaponId.Dynamite, 1),
                new WeaponStack(WeaponId.CherryBomb, 10));

            Assert.IsTrue(inv.Equip(1));
            Assert.IsTrue(inv.ConsumeAt(0));
            Assert.AreEqual(0, inv.EquippedIndex);
            Assert.IsTrue(inv.TryGetEquipped(out WeaponId id));
            Assert.AreEqual(WeaponId.CherryBomb, id);
        }

        [Test]
        public void FirstIndexOf_ReturnsMinusOneWhenMissing()
        {
            WeaponInventory inv = Make(new WeaponStack(WeaponId.CherryBomb, 10));
            Assert.AreEqual(-1, inv.FirstIndexOf(WeaponId.Mine));
            Assert.IsFalse(inv.Contains(WeaponId.Mine));
        }

        // ------------------------------------------------------------------
        // 保底武器（§3.2 / §5.5）
        // ------------------------------------------------------------------

        [Test]
        public void EnsureFallbackWeapon_EmptyInventory_AddsCannonball()
        {
            var inv = new WeaponInventory();
            Assert.IsFalse(inv.HasAny);

            Assert.IsTrue(inv.EnsureFallbackWeapon());
            Assert.AreEqual(1, inv.Count);
            Assert.IsTrue(inv.TryGetWeaponAt(0, out WeaponId id));
            Assert.AreEqual(WeaponId.Cannonball, id);
            Assert.IsFalse(inv.IsInfiniteAt(0), "保底 cannonball 是有限的（用掉即消耗）");
        }

        [Test]
        public void EnsureFallbackWeapon_NonEmpty_DoesNothing()
        {
            WeaponInventory inv = Make(new WeaponStack(WeaponId.CherryBomb, 1));
            Assert.IsFalse(inv.EnsureFallbackWeapon());
            Assert.AreEqual(1, inv.Count);
        }

        // ------------------------------------------------------------------
        // piecesOfEight 8 次复用（§5.2）
        // ------------------------------------------------------------------

        [Test]
        public void PiecesOfEight_ReusesEightTimes()
        {
            // §5.2：可重复使用 8 次——爆后 owner.equip(同 index) 复位并 weaponLocked=true。
            // 期望：前 7 次 ConsumeAt 返回 false（槽位保留），第 8 次返回 true（移除）。
            WeaponInventory inv = Make(new WeaponStack(WeaponId.PiecesOfEight, 1));

            Assert.AreEqual(1, inv.Count);
            Assert.AreEqual(8, inv.UsesRemainingAt(0));

            for (int use = 1; use <= 7; use++)
            {
                Assert.IsFalse(inv.ConsumeAt(0), "第 " + use + " 次使用后仍应保留槽位");
                Assert.AreEqual(1, inv.Count);
                Assert.AreEqual(8 - use, inv.UsesRemainingAt(0));
            }

            Assert.IsTrue(inv.ConsumeAt(0), "第 8 次使用后槽位应被移除");
            Assert.AreEqual(0, inv.Count);
        }

        [Test]
        public void PiecesOfEight_FiniteCountThree_YieldsThreeSlots()
        {
            // 有限 piecesOfEight×3 → 3 个槽位，每个 8 次复用。
            WeaponInventory inv = Make(new WeaponStack(WeaponId.PiecesOfEight, 3));
            Assert.AreEqual(3, inv.Count);
            Assert.AreEqual(8, inv.UsesRemainingAt(0));
            Assert.AreEqual(8, inv.UsesRemainingAt(2));
        }

        // ------------------------------------------------------------------
        // 装备
        // ------------------------------------------------------------------

        [Test]
        public void EquipUnequip_Works()
        {
            WeaponInventory inv = Make(new WeaponStack(WeaponId.CherryBomb, 10));
            Assert.IsFalse(inv.HasEquipped);

            Assert.IsTrue(inv.EquipFirst(WeaponId.CherryBomb));
            Assert.IsTrue(inv.HasEquipped);
            Assert.AreEqual(WeaponId.CherryBomb, inv.EquippedWeapon);

            inv.Unequip();
            Assert.IsFalse(inv.HasEquipped);
        }

        [Test]
        public void Equip_InvalidIndex_ReturnsFalse()
        {
            WeaponInventory inv = Make(new WeaponStack(WeaponId.CherryBomb, 1));
            Assert.IsFalse(inv.Equip(5));
            Assert.IsFalse(inv.Equip(-1));
        }
    }
}
