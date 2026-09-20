using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileSpawnPlanner"/> 测试：把「用武器」翻译成弹体生成计划（位置/初速/数量）。
    /// 手算：1 单位 = 16px（格 1→2 单位后 PixelsPerUnit = 32/2 = 16）、FlashSpeedScale = 1/(16×0.04) = 1.5625；
    /// 放置间距 = 2×水平半宽。
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
            // vx=4, vy=10（Flash px/帧）→ 3D 初速（含 ThrowLift=0.7 仰角），FlashSpeedScale=1.5625：
            //   模长 s = √(4²+10²) = √116 ≈ 10.7703 px/帧 → 世界 10.7703×1.5625 ≈ 16.8286。
            //   平面方向 = normalize(4, 0, 10)（Flash 平面 → 世界 XZ）；抬升只在 +Y 上抬仰角、不改模长。
            //   dir = normalize(normalize(4,0,10) + up·0.7)：dir.x = 4/√(116×1.49) ≈ 0.30424、
            //   dir.y = 0.7/√1.49 ≈ 0.57346、dir.z ≈ 0.76064。
            //   合成后：x≈5.1200、y≈9.6506、z≈12.8001。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.CherryBomb, vx: 4f, vy: 10f);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(Owner, plan[0].WorldPosition);
            Assert.AreEqual(5.1200f, plan[0].WorldVelocity.x, 1e-3f);
            Assert.AreEqual(9.6506f, plan[0].WorldVelocity.y, 1e-3f);
            Assert.AreEqual(12.8001f, plan[0].WorldVelocity.z, 1e-3f);
            // 抬升不改变速度大小（twangMax 限速语义）：|v| = s × FlashSpeedScale。
            Assert.AreEqual(16.8286f, plan[0].WorldVelocity.magnitude, 1e-3f);
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
            // 半宽 16/16 = 1.0 → 间距 2.0；3 个以瞄准点 (4,0) 为中心 → x = 2, 4, 6。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.WoodenCrate);
            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(2f, plan[0].WorldPosition.x, 1e-5f);
            Assert.AreEqual(4f, plan[1].WorldPosition.x, 1e-5f);
            Assert.AreEqual(6f, plan[2].WorldPosition.x, 1e-5f);
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
            // 半宽 16/16 = 1.0 → 间距 2.0；2 个以瞄准点为中心 → x = 3, 5。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.GunpowderBarrel);
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(3f, plan[0].WorldPosition.x, 1e-5f);
            Assert.AreEqual(5f, plan[1].WorldPosition.x, 1e-5f);
            Assert.IsTrue(plan[0].Kinematic);
        }

        [Test]
        public void Anchor_DropsFromAboveAtConstant40PxPerFrame()
        {
            // §5.2 anchor：从 y=-200（= 200px 高）以 vy=40 等速直落。
            // 200/16 = 12.5；40×1.5625 = 62.5 世界单位/秒。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.Anchor);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(4f, plan[0].WorldPosition.x, 1e-4f);
            Assert.AreEqual(12.5f, plan[0].WorldPosition.y, 1e-4f);
            Assert.AreEqual(0f, plan[0].WorldVelocity.x, 1e-4f);
            Assert.AreEqual(-62.5f, plan[0].WorldVelocity.y, 1e-3f);
            Assert.IsFalse(plan[0].Kinematic);
        }

        [Test]
        public void Seagull_EntersFromLeftAt10PxPerFrame()
        {
            // §5.2 seagull：x=-300 → -18.75；高度取瞄准点上方 100px → 6.25；vx=10 → 15.625。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.Seagull);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(-18.75f, plan[0].WorldPosition.x, 1e-4f);
            Assert.AreEqual(6.25f, plan[0].WorldPosition.y, 1e-4f);
            Assert.AreEqual(15.625f, plan[0].WorldVelocity.x, 1e-3f);
            Assert.AreEqual(0f, plan[0].WorldVelocity.y, 1e-4f);
            Assert.IsFalse(plan[0].Kinematic);
        }

        [Test]
        public void TidalWave_StartsLeftAtWaterLevelAndSweepsRight()
        {
            // §5.2 tidalWave：x=-550 → -34.375；y=water.y → WaterSurfaceY；vx=20 → 31.25。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.TidalWave);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(-34.375f, plan[0].WorldPosition.x, 1e-4f);
            Assert.AreEqual(LevelGeometry.WaterSurfaceY, plan[0].WorldPosition.y, 1e-4f);
            Assert.AreEqual(31.25f, plan[0].WorldVelocity.x, 1e-3f);
            Assert.IsFalse(plan[0].Kinematic);
        }

        [Test]
        public void VoodooDoll_UsesSameSlingPathAsGenericWeapons()
        {
            // §5.2 voodooDoll：twangMax=20，走弹弓；与 CherryBomb 同源换算（预览=实弹）。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.VoodooDoll, vx: 4f, vy: 10f);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(Owner, plan[0].WorldPosition);
            Assert.AreEqual(5.1200f, plan[0].WorldVelocity.x, 1e-3f);
            Assert.AreEqual(9.6506f, plan[0].WorldVelocity.y, 1e-3f);
            Assert.AreEqual(12.8001f, plan[0].WorldVelocity.z, 1e-3f);
            Assert.IsFalse(plan[0].Kinematic);
        }

        [Test]
        public void Cannon_PlacesOnePersistentAtAim()
        {
            // §5.2 cannon：placeableWeapon，摆位常驻。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.Cannon);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(Aim, plan[0].WorldPosition);
            Assert.AreEqual(Vector3.zero, plan[0].WorldVelocity);
            Assert.IsTrue(plan[0].Kinematic);
        }

        [Test]
        public void SweepingFlame_SpreadsBothWaysAt8PxPerSegment()
        {
            // §5.2 rumBottle 行：落地生成 2 个 SweepingFlame，向左右蔓延；8×1.5625=12.5。
            IReadOnlyList<ProjectileSpawn> plan = Plan(WeaponId.SweepingFlame);
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(Aim, plan[0].WorldPosition);
            Assert.AreEqual(Aim, plan[1].WorldPosition);
            Assert.AreEqual(12.5f, plan[0].WorldVelocity.x, 1e-3f);
            Assert.AreEqual(-12.5f, plan[1].WorldVelocity.x, 1e-3f);
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
