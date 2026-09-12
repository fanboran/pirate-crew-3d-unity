using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileProfile"/> 参数推导测试（§5.2 武器总表 → PhysX 参数映射）。
    /// 逐值手算，算式写在每个断言旁。
    /// </summary>
    [TestFixture]
    public class ProjectileProfileTests
    {
        static WeaponStats Stats(WeaponId id) => WeaponCatalog.Get(id);

        // ------------------------------------------------------------------
        // AABB → Collider 半尺寸（1 单位 = 32px）
        // ------------------------------------------------------------------

        [Test]
        public void Cannonball_Aabb10px_BecomesHalfWidth0Point3125()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.Cannonball));
            // 10 / 32 = 0.3125；全宽 0.625
            Assert.AreEqual(0.3125f, p.HalfWidth, 1e-6f);
            Assert.AreEqual(0.3125f, p.HalfHeight, 1e-6f);
            Assert.AreEqual(0.625f, p.ColliderWidth, 1e-6f);
            Assert.AreEqual(0.625f, p.ColliderHeight, 1e-6f);
            Assert.AreEqual(ProjectileShape.Sphere, p.Shape);
        }

        [Test]
        public void Boulder_Aabb31px_BecomesHalfWidth0Point96875()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.Boulder));
            // 31 / 32 = 0.96875
            Assert.AreEqual(0.96875f, p.HalfWidth, 1e-6f);
        }

        [Test]
        public void GunpowderBarrel_IsBox_WithVerticalRadius15px()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.GunpowderBarrel));
            // 水平 16/32 = 0.5；垂直 15/32 = 0.46875；全宽 1.0、全高 0.9375
            Assert.AreEqual(0.5f, p.HalfWidth, 1e-6f);
            Assert.AreEqual(0.46875f, p.HalfHeight, 1e-6f);
            Assert.AreEqual(1.0f, p.ColliderWidth, 1e-6f);
            Assert.AreEqual(0.9375f, p.ColliderHeight, 1e-6f);
            Assert.AreEqual(ProjectileShape.Box, p.Shape);
        }

        [Test]
        public void WoodenCrate_IsBox_SameAabbAsBarrel()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.WoodenCrate));
            Assert.AreEqual(0.5f, p.HalfWidth, 1e-6f);
            Assert.AreEqual(0.46875f, p.HalfHeight, 1e-6f);
            Assert.AreEqual(ProjectileShape.Box, p.Shape);
        }

        // ------------------------------------------------------------------
        // Weight → 质量 / 重力
        // ------------------------------------------------------------------

        [Test]
        public void WeightlessWeapon_HasUnitMassAndNoGravity()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.Cannonball));
            // §5.2 cannonball weight=0：无重力；PhysX 不接受 0 质量，取 1。
            Assert.AreEqual(1f, p.Mass, 1e-6f);
            Assert.IsFalse(p.UsesGravity);
            Assert.AreEqual(0f, p.GravityScale, 1e-6f);
        }

        [Test]
        public void Boulder_Weight1Point5_ScalesMassAndGravity()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.Boulder));
            // §5.2 boulder 全表唯一非 1 重量 1.5
            Assert.AreEqual(1.5f, p.Mass, 1e-6f);
            Assert.IsTrue(p.UsesGravity);
            Assert.AreEqual(1.5f, p.GravityScale, 1e-6f);
        }

        [Test]
        public void NormalWeapon_Weight1_UsesUnitGravity()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb));
            Assert.AreEqual(1f, p.Mass, 1e-6f);
            Assert.IsTrue(p.UsesGravity);
            Assert.AreEqual(1f, p.GravityScale, 1e-6f);
        }

        // ------------------------------------------------------------------
        // Bounce / Friction → PhysicsMaterial
        // ------------------------------------------------------------------

        [Test]
        public void Banana_HasHighestBounce0Point8()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.Banana));
            Assert.AreEqual(0.8f, p.Bounciness, 1e-6f);
            Assert.AreEqual(0.5f, p.DynamicFriction, 1e-6f);
            Assert.AreEqual(0.5f, p.StaticFriction, 1e-6f);
        }

        [Test]
        public void Dynamite_Friction1Point7_IsCarriedIntoMaterial()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.Dynamite));
            Assert.AreEqual(1.7f, p.DynamicFriction, 1e-6f);
            Assert.AreEqual(0.2f, p.Bounciness, 1e-6f);
        }

        [Test]
        public void BoxWeapon_HasZeroFrictionAndZeroBounce()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.WoodenCrate));
            Assert.AreEqual(0f, p.Bounciness, 1e-6f);
            Assert.AreEqual(0f, p.DynamicFriction, 1e-6f);
        }

        // ------------------------------------------------------------------
        // 生命周期 / 复用 / 放置
        // ------------------------------------------------------------------

        [Test]
        public void LimitedToTurn_TrueWeapons_AreNotPersistent()
        {
            Assert.IsFalse(ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb)).IsPersistent);
            Assert.IsFalse(ProjectileProfile.FromStats(Stats(WeaponId.Cannonball)).IsPersistent);
            Assert.IsFalse(ProjectileProfile.FromStats(Stats(WeaponId.PiecesOfEight)).IsPersistent);
        }

        [Test]
        public void LimitedToTurn_FalseWeapons_ArePersistent()
        {
            // §5.2：mine / gunpowderBarrel / woodenCrate / cannon
            Assert.IsTrue(ProjectileProfile.FromStats(Stats(WeaponId.Mine)).IsPersistent);
            Assert.IsTrue(ProjectileProfile.FromStats(Stats(WeaponId.GunpowderBarrel)).IsPersistent);
            Assert.IsTrue(ProjectileProfile.FromStats(Stats(WeaponId.WoodenCrate)).IsPersistent);
            Assert.IsTrue(ProjectileProfile.FromStats(Stats(WeaponId.Cannon)).IsPersistent);
        }

        [Test]
        public void PlaceableCounts_MatchSection5_2()
        {
            Assert.AreEqual(2, ProjectileProfile.FromStats(Stats(WeaponId.GunpowderBarrel)).PlaceableCount);
            Assert.AreEqual(3, ProjectileProfile.FromStats(Stats(WeaponId.WoodenCrate)).PlaceableCount);
            Assert.IsTrue(ProjectileProfile.FromStats(Stats(WeaponId.GunpowderBarrel)).IsPlaceable);
            Assert.IsFalse(ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb)).IsPlaceable);
        }

        [Test]
        public void PiecesOfEight_ReuseCountIs8()
        {
            // §5.2 / §8.4「You get eight turns with this weapon」
            Assert.AreEqual(8, ProjectileProfile.FromStats(Stats(WeaponId.PiecesOfEight)).ReuseCount);
            Assert.AreEqual(1, ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb)).ReuseCount);
        }

        // ------------------------------------------------------------------
        // 通用弹体范围 / 弹弓发射资格 / 瓦片碰撞 / 落水处置
        // ------------------------------------------------------------------

        [Test]
        public void SupportsGenericProjectile_CoversElevenWeapons()
        {
            WeaponId[] supported =
            {
                WeaponId.Cannonball, WeaponId.CherryBomb, WeaponId.Dynamite, WeaponId.Boulder,
                WeaponId.Banana, WeaponId.Mine, WeaponId.ParachuteBomb, WeaponId.RumBottle,
                WeaponId.PiecesOfEight, WeaponId.GunpowderBarrel, WeaponId.WoodenCrate,
            };
            foreach (WeaponId id in supported)
                Assert.IsTrue(ProjectileProfile.SupportsGenericProjectile(Stats(id)), id.ToString());

            WeaponId[] todo =
            {
                WeaponId.Anchor, WeaponId.Seagull, WeaponId.TidalWave,
                WeaponId.VoodooDoll, WeaponId.Cannon, WeaponId.SweepingFlame,
            };
            foreach (WeaponId id in todo)
                Assert.IsFalse(ProjectileProfile.SupportsGenericProjectile(Stats(id)), id.ToString());
        }

        [Test]
        public void Cannonball_IsSlingLaunchable_DespiteTwangMaxZero()
        {
            // §5.2 cannonball twangMax 为「—」，但作为保底武器必须能抛（§3.2）。
            Assert.AreEqual(0f, Stats(WeaponId.Cannonball).TwangMax, 1e-6f);
            Assert.IsTrue(ProjectileProfile.CanBeSlingLaunched(Stats(WeaponId.Cannonball)));
            Assert.IsTrue(ProjectileProfile.CanBeSlingLaunched(Stats(WeaponId.Banana)));
            Assert.IsFalse(ProjectileProfile.CanBeSlingLaunched(Stats(WeaponId.WoodenCrate)));
        }

        [Test]
        public void HitsTiles_FalseOnlyForSeagullAndTidalWave()
        {
            Assert.IsFalse(ProjectileProfile.HitsTilesFor(WeaponId.Seagull));
            Assert.IsFalse(ProjectileProfile.HitsTilesFor(WeaponId.TidalWave));
            Assert.IsTrue(ProjectileProfile.HitsTilesFor(WeaponId.Cannonball));
            Assert.IsTrue(ProjectileProfile.HitsTilesFor(WeaponId.GunpowderBarrel));
        }

        [Test]
        public void WaterBehavior_MatchesSection5_2Notes()
        {
            // cannonball「落水即消失」；dynamite 落水不立即爆（M2 近似为消失）；
            // 其余带爆炸的武器落水即引爆。
            Assert.AreEqual(ProjectileWaterBehavior.Vanish,
                ProjectileProfile.WaterBehavior(Stats(WeaponId.Cannonball)));
            Assert.AreEqual(ProjectileWaterBehavior.Vanish,
                ProjectileProfile.WaterBehavior(Stats(WeaponId.Dynamite)));
            Assert.AreEqual(ProjectileWaterBehavior.Vanish,
                ProjectileProfile.WaterBehavior(Stats(WeaponId.WoodenCrate)));
            Assert.AreEqual(ProjectileWaterBehavior.Detonate,
                ProjectileProfile.WaterBehavior(Stats(WeaponId.CherryBomb)));
            Assert.AreEqual(ProjectileWaterBehavior.Detonate,
                ProjectileProfile.WaterBehavior(Stats(WeaponId.PiecesOfEight)));
            Assert.AreEqual(ProjectileWaterBehavior.Detonate,
                ProjectileProfile.WaterBehavior(Stats(WeaponId.Mine)));
        }

        [Test]
        public void ExplosionParams_AreCarriedThrough()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb));
            Assert.IsTrue(p.HasExplosion);
            Assert.AreEqual(80f, p.ExplosionSize, 1e-6f);
            Assert.AreEqual(40f, p.ExplosionMaxDamage, 1e-6f);

            ProjectileProfile crate = ProjectileProfile.FromStats(Stats(WeaponId.WoodenCrate));
            Assert.IsFalse(crate.HasExplosion);
        }
    }
}
