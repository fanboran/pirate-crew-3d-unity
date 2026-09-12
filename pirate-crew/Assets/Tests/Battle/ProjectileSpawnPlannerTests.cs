using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileSpawnPlanner"/> 测试：把「用武器」翻译成弹体生成计划（位置/初速/数量）。
    /// 手算：1 单位 = 32px；放置间距 = 2×水平半宽。
    /// </summary>
    [TestFixture]
    public class ProjectileSpawnPlannerTests
    {
        static readonly Vector3 Owner = new Vector3(1f, 2f, 0f);
        static readonly Vector3 Aim = new Vector3(4f, 0f, 0f);

        static IReadOnlyList<ProjectileSpawn> Plan(WeaponId id, float vx = 0f, float vy = 0f)
        {
            return ProjectileSpawnPlanner.Plan(WeaponCatalog.Get(id), Owner, Aim, vx, vy);
        }

        [Test]
        public void SlingWeapon_SpawnsOneAtOwner_WithFlashVelocityConverted()
        {
            // vx=4, vy=10（Flash px/帧）→ 3D 初速（含 ThrowLift=0.7 仰角）：
            //   模长 s = √(4²+10²) = √116 ≈ 10.7703 px/帧 → 世界 10.7703×0.78125 ≈ 8.4143。
            //   平面方向 = normalize(4, 0, 10)（Flash 平面 → 世界 XZ）；抬升只在 +Y 上抬仰角、不改模长。
            //   合成后：x≈2.5601、y≈4.8252、z≈6.4003。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.CherryBomb, vx: 4f, vy: 10f);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(Owner, plan[0].WorldPosition);
            Assert.AreEqual(2.5601f, plan[0].WorldVelocity.x, 1e-3f);
            Assert.AreEqual(4.8252f, plan[0].WorldVelocity.y, 1e-3f);
            Assert.AreEqual(6.4003f, plan[0].WorldVelocity.z, 1e-3f);
            // 抬升不改变速度大小（twangMax 限速语义）：|v| = s × FlashSpeedScale。
            Assert.AreEqual(8.4143f, plan[0].WorldVelocity.magnitude, 1e-3f);
            // 平面方向仍是 Flash (vx, vy) 的方向：x/z = 4/10。
            Assert.AreEqual(0.4f, plan[0].WorldVelocity.x / plan[0].WorldVelocity.z, 1e-4f);
            Assert.IsFalse(plan[0].Kinematic);
        }

        [Test]
        public void Cannonball_SpawnsOneAtOwner_DespiteTwangMaxZero()
        {
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.Cannonball, vx: 0f, vy: 0f);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(Owner, plan[0].WorldPosition);
        }

        [Test]
        public void WoodenCrate_PlacesThree_SpacedByColliderWidth()
        {
            // 半宽 16/32 = 0.5 → 间距 1.0；3 个以瞄准点 (4,0) 为中心 → x = 3, 4, 5。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.WoodenCrate);
            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(3f, plan[0].WorldPosition.x, 1e-5f);
            Assert.AreEqual(4f, plan[1].WorldPosition.x, 1e-5f);
            Assert.AreEqual(5f, plan[2].WorldPosition.x, 1e-5f);
            Assert.AreEqual(0f, plan[0].WorldPosition.y, 1e-5f);

            for (int i = 0; i < plan.Count; i++)
            {
                Assert.IsTrue(plan[i].Kinematic, "放置类应为 kinematic");
                Assert.AreEqual(Vector3.zero, plan[i].WorldVelocity);
            }
        }

        [Test]
        public void GunpowderBarrel_PlacesTwo_SpacedByColliderWidth()
        {
            // 半宽 0.5 → 间距 1.0；2 个以瞄准点为中心 → x = 3.5, 4.5。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.GunpowderBarrel);
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(3.5f, plan[0].WorldPosition.x, 1e-5f);
            Assert.AreEqual(4.5f, plan[1].WorldPosition.x, 1e-5f);
            Assert.IsTrue(plan[0].Kinematic);
        }

        [Test]
        public void UnimplementedSpecialWeapons_ProduceNoSpawn()
        {
            // anchor / seagull / tidalWave / voodooDoll / cannon / SweepingFlame：本次未实现，返回空。
            Assert.AreEqual(0, Plan(WeaponId.Anchor).Count);
            Assert.AreEqual(0, Plan(WeaponId.Seagull).Count);
            Assert.AreEqual(0, Plan(WeaponId.TidalWave).Count);
            Assert.AreEqual(0, Plan(WeaponId.VoodooDoll).Count);
            Assert.AreEqual(0, Plan(WeaponId.Cannon).Count);
            Assert.AreEqual(0, Plan(WeaponId.SweepingFlame).Count);
        }

        [Test]
        public void Mine_SpawnsOneAtOwner()
        {
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.Mine, vx: 1f, vy: 1f);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(Owner, plan[0].WorldPosition);
            Assert.IsFalse(plan[0].Kinematic);
        }
    }
}
