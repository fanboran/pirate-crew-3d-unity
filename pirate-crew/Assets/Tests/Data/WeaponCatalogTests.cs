using System;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// WeaponCatalog 的纯 C# 断言（对应静态逆向文档 §5.2 武器总表 17 行）。
    /// 不实例化任何 ScriptableObject / GameObject。
    /// </summary>
    public class WeaponCatalogTests
    {
        [Test]
        public void Count_Is17()
        {
            Assert.That(WeaponCatalog.Count, Is.EqualTo(17));
            Assert.That(WeaponCatalog.All.Count, Is.EqualTo(17));
        }

        [Test]
        public void All_Ids_CoverEveryEnumValueExactlyOnce()
        {
            var seen = new HashSet<WeaponId>();
            foreach (WeaponStats stats in WeaponCatalog.All)
                Assert.That(seen.Add(stats.Id), Is.True, "武器 id 重复: " + stats.Id);

            foreach (WeaponId id in Enum.GetValues(typeof(WeaponId)))
                Assert.That(seen.Contains(id), Is.True, "目录缺少武器 id: " + id);

            Assert.That(seen.Count, Is.EqualTo(Enum.GetValues(typeof(WeaponId)).Length));
        }

        // ------------------------------------------------------------------
        // §5.2 逐值抽查
        // ------------------------------------------------------------------

        [Test]
        public void Cannonball_Explosion_100_50()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.Cannonball);
            Assert.That(s.ExplosionSize, Is.EqualTo(100f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(50f));
            Assert.That(s.AabbRadius, Is.EqualTo(10f));
            Assert.That(s.Weight, Is.EqualTo(0f), "cannonball 无重力");
        }

        [Test]
        public void CherryBomb_Explosion_80_40()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.CherryBomb);
            Assert.That(s.ExplosionSize, Is.EqualTo(80f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(40f));
            Assert.That(s.TwangMax, Is.EqualTo(20f));
        }

        [Test]
        public void Dynamite_Explosion_250_70_And_TriggerOnRest()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.Dynamite);
            Assert.That(s.ExplosionSize, Is.EqualTo(250f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(70f));
            Assert.That(s.Friction, Is.EqualTo(1.7f));
            Assert.That(s.Trigger, Is.EqualTo(WeaponTrigger.OnRest));
        }

        [Test]
        public void Banana_Explosion_160_80_And_Bounce08_And_Twang30()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.Banana);
            Assert.That(s.ExplosionSize, Is.EqualTo(160f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(80f));
            Assert.That(s.Bounce, Is.EqualTo(0.8f));
            Assert.That(s.TwangMax, Is.EqualTo(30f));
            // banana 同时具备"静止"与"点击"两种触发（§5.2）
            Assert.That((s.Trigger & WeaponTrigger.OnRest) != 0, Is.True);
            Assert.That((s.Trigger & WeaponTrigger.OnClick) != 0, Is.True);
        }

        [Test]
        public void Boulder_Weight15_And_NoExplosion()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.Boulder);
            Assert.That(s.Weight, Is.EqualTo(1.5f));
            Assert.That(s.HasExplosion, Is.False);
            Assert.That(s.ExplosionSize, Is.EqualTo(0f));
        }

        [Test]
        public void Mine_Explosion_250_70_And_CrossTurnPersistent()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.Mine);
            Assert.That(s.ExplosionSize, Is.EqualTo(250f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(70f));
            Assert.That(s.Trigger, Is.EqualTo(WeaponTrigger.ProximityFuse));
            Assert.That(s.LimitedToTurn, Is.False, "地雷跨回合常驻（§5.2 limitedToTurn=false）");
            Assert.That(s.DragRange, Is.EqualTo(180f));
        }

        [Test]
        public void RumBottle_Explosion_80_25_Twang30()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.RumBottle);
            Assert.That(s.ExplosionSize, Is.EqualTo(80f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(25f));
            Assert.That(s.TwangMax, Is.EqualTo(30f));
        }

        [Test]
        public void PiecesOfEight_Explosion_50_25_And_Reusable8()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.PiecesOfEight);
            Assert.That(s.ExplosionSize, Is.EqualTo(50f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(25f));
            Assert.That(s.MaxReuses, Is.EqualTo(8), "§8.4：八枚金币可复用 8 个回合");
        }

        [Test]
        public void GunpowderBarrel_Explosion_150_30_Place2_ChainTrigger()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.GunpowderBarrel);
            Assert.That(s.ExplosionSize, Is.EqualTo(150f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(30f));
            Assert.That(s.PlaceableCount, Is.EqualTo(2), "§5.2/§8.4：放置 2 个");
            Assert.That(s.LimitedToTurn, Is.False, "火药桶跨回合常驻");
            Assert.That((s.Trigger & WeaponTrigger.OnExplosionHit) != 0, Is.True, "可被爆炸连锁引爆");
            Assert.That(s.AabbRadius, Is.EqualTo(16f));
            Assert.That(s.AabbVerticalRadius, Is.EqualTo(15f), "§5.2 箱体 AABB 16/16/15/15");
        }

        [Test]
        public void WoodenCrate_HasNoExplosion_Place3()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.WoodenCrate);
            Assert.That(s.HasExplosion, Is.False);
            Assert.That(s.ExplosionSize, Is.EqualTo(0f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(0f));
            Assert.That(s.PlaceableCount, Is.EqualTo(3), "§5.2/§8.4：放置 3 个");
        }

        // ------------------------------------------------------------------
        // 可证伪断言：twangMax == 30 的恰好是 Banana / ParachuteBomb / RumBottle
        // ------------------------------------------------------------------

        [Test]
        public void TwangMax30_IsExactly_BananaParachuteBombRumBottle()
        {
            var high = new List<WeaponId>();
            foreach (WeaponStats s in WeaponCatalog.All)
            {
                if (s.TwangMax == 30f)
                    high.Add(s.Id);
            }

            Assert.That(high.Count, Is.EqualTo(3));
            Assert.That(high, Is.EquivalentTo(new[]
            {
                WeaponId.Banana,
                WeaponId.ParachuteBomb,
                WeaponId.RumBottle,
            }));
        }

        [Test]
        public void ParachuteBomb_Explosion_160_50_And_Twang30()
        {
            WeaponStats s = WeaponCatalog.Get(WeaponId.ParachuteBomb);
            Assert.That(s.ExplosionSize, Is.EqualTo(160f));
            Assert.That(s.ExplosionMaxDamage, Is.EqualTo(50f));
            Assert.That(s.TwangMax, Is.EqualTo(30f));
        }

        // ------------------------------------------------------------------
        // 特殊行为备注可被消费方读到（不是空字符串）
        // ------------------------------------------------------------------

        [Test]
        public void SpecialBehavior_Values_AreTranscribed()
        {
            Assert.That(WeaponCatalog.Get(WeaponId.Anchor).DirectDamage, Is.EqualTo(60f), "锚 60 点固定伤害");
            Assert.That(WeaponCatalog.Get(WeaponId.Seagull).ExplosionMaxDamage, Is.EqualTo(50f), "海鸥每发 50");
            Assert.That(WeaponCatalog.Get(WeaponId.TidalWave).DirectDamage, Is.EqualTo(5f), "潮汐每帧 5 点");
            Assert.That(WeaponCatalog.Get(WeaponId.SweepingFlame).DirectDamage, Is.EqualTo(30f), "蔓延火焰每段 30 点");
        }

        [Test]
        public void Get_UnknownId_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => WeaponCatalog.Get((WeaponId)999));
        }
    }
}
