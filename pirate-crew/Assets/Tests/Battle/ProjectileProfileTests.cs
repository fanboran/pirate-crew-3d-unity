using NUnit.Framework;
using PirateCrew.Combat;
using PirateCrew.Data;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="ProjectileProfile"/> 参数推导测试。
    ///
    /// 【单一弹体口径】全部 <see cref="WeaponId"/> 都映射到同一份标准小炸弹 profile
    /// （<see cref="StandardBombRules"/> 单一规则源，数值单源 <see cref="BalanceConfig.Defaults"/>）。
    /// 本文件逐条断言「任意武器 → 同一份标准参数」，以及 px→世界单位换算（1 单位 = 16px）。
    /// </summary>
    [TestFixture]
    public class ProjectileProfileTests
    {
        static WeaponStats Stats(WeaponId id) => WeaponCatalog.Get(id);

        static readonly WeaponId[] AllWeaponIds =
        {
            WeaponId.Cannonball, WeaponId.CherryBomb, WeaponId.Dynamite, WeaponId.Boulder,
            WeaponId.Banana, WeaponId.Mine, WeaponId.ParachuteBomb, WeaponId.RumBottle,
            WeaponId.PiecesOfEight, WeaponId.GunpowderBarrel, WeaponId.WoodenCrate,
            WeaponId.Anchor, WeaponId.Seagull, WeaponId.TidalWave,
            WeaponId.VoodooDoll, WeaponId.Cannon, WeaponId.SweepingFlame,
        };

        // ------------------------------------------------------------------
        // 任意武器 → 同一份标准炸弹参数
        // ------------------------------------------------------------------

        [Test]
        public void AnyWeapon_YieldsIdenticalStandardProfile()
        {
            ProjectileProfile reference = ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb));

            foreach (WeaponId id in AllWeaponIds)
            {
                ProjectileProfile p = ProjectileProfile.FromStats(Stats(id));
                Assert.AreEqual(reference.HalfWidth, p.HalfWidth, 1e-6f, id.ToString());
                Assert.AreEqual(reference.HalfHeight, p.HalfHeight, 1e-6f, id.ToString());
                Assert.AreEqual(reference.Mass, p.Mass, 1e-6f, id.ToString());
                Assert.AreEqual(reference.UsesGravity, p.UsesGravity, id.ToString());
                Assert.AreEqual(reference.GravityScale, p.GravityScale, 1e-6f, id.ToString());
                Assert.AreEqual(reference.Bounciness, p.Bounciness, 1e-6f, id.ToString());
                Assert.AreEqual(reference.DynamicFriction, p.DynamicFriction, 1e-6f, id.ToString());
                Assert.AreEqual(reference.TwangMax, p.TwangMax, 1e-6f, id.ToString());
                Assert.AreEqual(reference.HasExplosion, p.HasExplosion, id.ToString());
                Assert.AreEqual(reference.ExplosionSize, p.ExplosionSize, 1e-6f, id.ToString());
                Assert.AreEqual(reference.ExplosionMaxDamage, p.ExplosionMaxDamage, 1e-6f, id.ToString());
                Assert.AreEqual(ProjectileShape.Sphere, p.Shape, id.ToString());
            }
        }

        [Test]
        public void StandardProfile_MatchesStandardBombRules()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.Cannonball));

            float half = LevelGeometry.PixelsToUnits(StandardBombRules.HalfSizePixels);
            // 8 / 16 = 0.5 世界单位；全宽 1.0
            Assert.AreEqual(half, p.HalfWidth, 1e-6f);
            Assert.AreEqual(half, p.HalfHeight, 1e-6f);
            Assert.AreEqual(half, p.HalfDepth, 1e-6f);
            Assert.AreEqual(1.0f, p.ColliderWidth, 1e-6f);
            Assert.AreEqual(1.0f, p.ColliderHeight, 1e-6f);
            Assert.AreEqual(1.0f, p.ColliderDepth, 1e-6f);

            // 重量/重力：标准口径 = 角色自重同档，恒吃重力
            Assert.AreEqual(StandardBombRules.Weight, p.Mass, 1e-6f);
            Assert.IsTrue(p.UsesGravity);
            Assert.AreEqual(StandardBombRules.Weight, p.GravityScale, 1e-6f);

            // 物理材质取全局物理默认
            Assert.AreEqual(StandardBombRules.Bounciness, p.Bounciness, 1e-6f);
            Assert.AreEqual(StandardBombRules.Friction, p.DynamicFriction, 1e-6f);
            Assert.AreEqual(StandardBombRules.Friction, p.StaticFriction, 1e-6f);

            // 弹弓上限 = 角色自抛同限
            Assert.AreEqual(StandardBombRules.TwangMax, p.TwangMax, 1e-6f);
        }

        // ------------------------------------------------------------------
        // 爆炸口径单源（StandardBombRules → BalanceConfig.Defaults）
        // ------------------------------------------------------------------

        [Test]
        public void StandardExplosion_ValuesComeFromBalanceDefaults()
        {
            ProjectileProfile p = ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb));

            Assert.IsTrue(p.HasExplosion);
            Assert.AreEqual(BalanceConfig.Defaults.StandardBombExplosionSize, p.ExplosionSize, 1e-6f);
            Assert.AreEqual(BalanceConfig.Defaults.StandardBombDamage, p.ExplosionMaxDamage, 1e-6f);
            Assert.AreEqual(StandardBombRules.ExplosionSize, p.ExplosionSize, 1e-6f);
            Assert.AreEqual(StandardBombRules.ExplosionMaxDamage, p.ExplosionMaxDamage, 1e-6f);
        }

        // ------------------------------------------------------------------
        // 寿命投影仍消费目录数据锚（LimitedToTurn / MaxReuses）
        // ------------------------------------------------------------------

        [Test]
        public void LifetimeProjection_StillReadsCatalogAnchors()
        {
            // LimitedToTurn=true 的武器（如 cherryBomb）→ 非常驻；false（如 mine）→ 常驻投影。
            Assert.IsFalse(ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb)).IsPersistent);
            Assert.IsTrue(ProjectileProfile.FromStats(Stats(WeaponId.Mine)).IsPersistent);

            // 复用次数投影：piecesOfEight 目录记 8 → 8；普通武器 → 1。
            Assert.AreEqual(8, ProjectileProfile.FromStats(Stats(WeaponId.PiecesOfEight)).ReuseCount);
            Assert.AreEqual(1, ProjectileProfile.FromStats(Stats(WeaponId.CherryBomb)).ReuseCount);
        }

        // ------------------------------------------------------------------
        // 放置/掩体路径已收敛为通用抛掷
        // ------------------------------------------------------------------

        [Test]
        public void NoWeapon_IsPlaceable_Anymore()
        {
            foreach (WeaponId id in AllWeaponIds)
            {
                ProjectileProfile p = ProjectileProfile.FromStats(Stats(id));
                Assert.IsFalse(p.IsPlaceable, id.ToString());
                Assert.AreEqual(0, p.PlaceableCount, id.ToString());
                Assert.IsTrue(p.HitsTiles, id.ToString());
            }
        }
    }
}
