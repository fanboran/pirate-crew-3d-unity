using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 跨回合常驻物容器测试：登记/查询/显式移除 + 爆炸波及的 AABB 最近点判定
    /// + 回合边界不销毁（容器没有回合清理入口，条目只能显式离场）。
    /// </summary>
    [TestFixture]
    public class PersistentObjectRegistryTests
    {
        static PersistentObjectEntry Entry(WeaponId weapon, Vector3 position, Vector3 halfExtents)
        {
            return new PersistentObjectEntry(0, weapon, position, halfExtents, teamIndex: -1);
        }

        // ------------------------------------------------------------------
        // 登记 / 查询 / 显式移除
        // ------------------------------------------------------------------

        [Test]
        public void Register_AssignsSequentialIds_AndSnapshotsEntry()
        {
            var registry = new PersistentObjectRegistry();
            int idA = registry.Register(Entry(WeaponId.GunpowderBarrel, new Vector3(2f, 0f, 3f), Vector3.one * 0.5f));
            int idB = registry.Register(Entry(WeaponId.WoodenCrate, new Vector3(5f, 0f, 1f), Vector3.one));

            Assert.AreEqual(2, registry.Count);
            Assert.AreNotEqual(idA, idB);

            Assert.IsTrue(registry.TryGet(idA, out PersistentObjectEntry a));
            Assert.AreEqual(WeaponId.GunpowderBarrel, a.Weapon);
            Assert.AreEqual(new Vector3(2f, 0f, 3f), a.Position);
            Assert.AreEqual(Vector3.one * 0.5f, a.HalfExtents);

            Assert.IsFalse(registry.TryGet(999, out _), "未登记的句柄应查询失败");
        }

        [Test]
        public void EntryId_IsRewrittenByRegistry_NotTakenFromCaller()
        {
            // 调用方塞进 entry.Id 的值被注册表重写（句柄始终由注册表分配，防外部伪造）。
            var registry = new PersistentObjectRegistry();
            var fake = new PersistentObjectEntry(777, WeaponId.Mine, Vector3.zero, Vector3.one, -1);
            int id = registry.Register(fake);
            Assert.AreNotEqual(777, id);
            Assert.IsTrue(registry.TryGet(id, out PersistentObjectEntry stored));
            Assert.AreEqual(id, stored.Id);
        }

        [Test]
        public void Remove_IsExplicitOnly_AndKeepsOthers()
        {
            var registry = new PersistentObjectRegistry();
            int idA = registry.Register(Entry(WeaponId.GunpowderBarrel, Vector3.zero, Vector3.one));
            int idB = registry.Register(Entry(WeaponId.WoodenCrate, Vector3.one * 3f, Vector3.one));

            Assert.IsTrue(registry.Remove(idA));
            Assert.AreEqual(1, registry.Count);
            Assert.IsFalse(registry.TryGet(idA, out _));
            Assert.IsTrue(registry.TryGet(idB, out _));
            Assert.IsFalse(registry.Remove(idA), "重复移除返回 false");
        }

        [Test]
        public void Clear_EmptiesEverything()
        {
            var registry = new PersistentObjectRegistry();
            registry.Register(Entry(WeaponId.Mine, Vector3.zero, Vector3.one));
            registry.Register(Entry(WeaponId.Cannon, Vector3.one, Vector3.one));

            registry.Clear();
            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(0, registry.All.Count);
        }

        // ------------------------------------------------------------------
        // 回合边界不销毁：容器没有回合清理入口，条目只能显式离场
        // ------------------------------------------------------------------

        [Test]
        public void Entries_SurviveAcrossTurnBoundaries_NoTurnApiExists()
        {
            // 模拟多回合推进：注册表侧不存在任何「回合结束清理」可调用面
            // （PersistentObjectRegistry 公开成员里没有 TurnEnd/AdvanceTurn 之类入口，
            // 唯一的批量离场是整场 Clear）——条目跨回合恒存活是容器的存在意义。
            var registry = new PersistentObjectRegistry();
            int id = registry.Register(Entry(WeaponId.Mine, Vector3.zero, Vector3.one));

            // 若干「回合」过去（没有任何调用发生）：
            for (int turn = 0; turn < 5; turn++)
            {
                Assert.AreEqual(1, registry.Count, "回合 " + turn);
                Assert.IsTrue(registry.TryGet(id, out PersistentObjectEntry entry), "回合 " + turn);
                Assert.AreEqual(WeaponId.Mine, entry.Weapon);
            }
        }

        // ------------------------------------------------------------------
        // 爆炸波及询问：AABB 最近点距离
        // ------------------------------------------------------------------

        [Test]
        public void CollectBlastAffected_HitsWhenBoxEdgeWithinRadius()
        {
            var registry = new PersistentObjectRegistry();
            // 木箱：中心 (10,0,0)、半尺寸 1（AABB x∈[9,11]）。
            int crateId = registry.Register(Entry(WeaponId.WoodenCrate, new Vector3(10f, 0f, 0f), Vector3.one));
            // 火药桶：中心 (30,0,0)、半尺寸 0.5。
            int barrelId = registry.Register(Entry(WeaponId.GunpowderBarrel, new Vector3(30f, 0f, 0f), Vector3.one * 0.5f));

            var results = new List<PersistentObjectEntry>();

            // 爆心 (0,0,0)、半径 9.5：木箱最近点 x=9 距 9 <= 9.5 命中（中心距 10 > 9.5，
            // 中心点口径会漏——AABB 最近点口径不漏）；火药桶不命中。
            registry.CollectBlastAffected(new Vector3(0f, 0f, 0f), 9.5f, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(crateId, results[0].Id);

            // 半径 0.5 贴边命中火药桶（最近点距 29.5 <= 29.5）。
            registry.CollectBlastAffected(Vector3.zero, 29.5f, results);
            Assert.AreEqual(2, results.Count);

            // 不改变登记。
            Assert.AreEqual(2, registry.Count);
            Assert.IsTrue(registry.TryGet(barrelId, out _));
        }

        [Test]
        public void CollectBlastAffected_BoxSurroundingCenter_AlwaysHit()
        {
            // AABB 完全包住爆心（大箱子里爆炸）：最近点 = 爆心自身，距离 0 命中。
            var registry = new PersistentObjectRegistry();
            registry.Register(Entry(WeaponId.WoodenCrate, Vector3.zero, Vector3.one * 5f));

            var results = new List<PersistentObjectEntry>();
            registry.CollectBlastAffected(Vector3.zero, 0.01f, results);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void CollectBlastAffected_EmptyRegistry_ReturnsEmpty()
        {
            var registry = new PersistentObjectRegistry();
            var results = new List<PersistentObjectEntry>();
            registry.CollectBlastAffected(Vector3.zero, 10f, results);
            Assert.AreEqual(0, results.Count);
        }
    }
}
