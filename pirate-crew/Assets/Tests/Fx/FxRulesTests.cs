using NUnit.Framework;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.Fx;
using UnityEngine;

namespace PirateCrew.PirateCrew.Fx.Tests
{
    /// <summary>
    /// <see cref="FxRules"/> 的纯 C# 用例（无头可跑，不碰任何 ECall）。
    ///
    /// 【测什么】把"爆炸半径 → 粒子数量/尺寸/速度/时长/冲击波直径""伤害 → 数字分档与颜色"
    /// 这些映射钉死：它们是"爆炸规模随半径变化"与"伤害数字四档配色"的唯一口径，
    /// 改坏了会以"大炸弹和小炸弹看着一样"或"重伤数字还是白的"的形式暴露。
    ///
    /// 【数值性质】被测常量多为【AI 提案】（原版 AS2 未导出参数），因此断言测的是
    /// **关系与边界**（单调性、钳制区间、预算上限、分档边界），不是"某个具体数值等于某值"——
    /// 这样调参不影响用例语义，但把"乱改到越界/不单调"挡住。
    /// </summary>
    [TestFixture]
    public class FxRulesTests
    {
        // ------------------------------------------------------------------
        // 爆炸半径 / 缩放
        // ------------------------------------------------------------------

        [Test]
        public void ExplosionRadiusWorld_Cannonball_MatchesSizeOverTwoPlusPadding()
        {
            // cannonball size=100（WeaponCatalog.cs:130）→ radius=(50+20)px=70px → 70/16=4.375u
            //（1 单位 = 16px，格 1→2 单位后由 70/32=2.1875 ×2）。
            float radius = FxRules.ExplosionRadiusWorld(100f);
            Assert.AreEqual(70f / 16f, radius, 1e-4f);
            Assert.AreEqual(1f, FxRules.ExplosionVisualScale(100f), 1e-4f, "cannonball 是基准，缩放应为 1");
        }

        [Test]
        public void ExplosionVisualScale_IsMonotonic_AndClamped()
        {
            float small = FxRules.ExplosionVisualScale(50f);    // piecesOfEight
            float mid = FxRules.ExplosionVisualScale(100f);     // cannonball
            float big = FxRules.ExplosionVisualScale(250f);     // dynamite / mine

            Assert.Less(small, mid);
            Assert.Less(mid, big);

            Assert.AreEqual(0.65f, FxRules.ExplosionVisualScale(0f), 1e-4f, "极小值被钳到下限");
            Assert.AreEqual(2.20f, FxRules.ExplosionVisualScale(100000f), 1e-4f, "极大值被钳到上限");
        }

        [Test]
        public void ExplosionParticleCounts_IncreaseWithSize_AndStayInBudget()
        {
            Assert.Less(FxRules.FireballParticles(80f), FxRules.FireballParticles(250f));
            Assert.Less(FxRules.SparkParticles(80f), FxRules.SparkParticles(250f));
            Assert.Less(FxRules.DebrisParticles(80f), FxRules.DebrisParticles(250f));
            Assert.Less(FxRules.SmokeParticles(80f), FxRules.SmokeParticles(250f));

            // 全表武器（含放置类 size=150/160）都必须落在单次爆炸预算内。
            float[] sizes = { 50f, 80f, 100f, 150f, 160f, 250f };
            for (int i = 0; i < sizes.Length; i++)
            {
                int total = FxRules.ExplosionTotalParticles(sizes[i]);
                Assert.LessOrEqual(total, FxRules.MaxExplosionParticles,
                    "size=" + sizes[i] + " 的爆炸粒子总数超预算");
                Assert.Greater(total, 0);
            }
        }

        [Test]
        public void ExplosionDurationsAndSpeeds_ArePositive_AndGrowWithSize()
        {
            Assert.Greater(FxRules.SparkSpeed(250f), FxRules.SparkSpeed(50f));
            Assert.Greater(FxRules.FireballLifetime(250f), FxRules.FireballLifetime(50f));
            Assert.Greater(FxRules.SmokeLifetime(250f), FxRules.SmokeLifetime(50f));
            Assert.Greater(FxRules.ShockwaveDiameter(250f), FxRules.ShockwaveDiameter(50f));

            float[] sizes = { 0f, 10f, 50f, 100f, 250f, 9999f };
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.Greater(FxRules.FireballStartSize(sizes[i]), 0f);
                Assert.Greater(FxRules.SparkSize(sizes[i]), 0f);
                Assert.Greater(FxRules.DebrisSize(sizes[i]), 0f);
                Assert.Greater(FxRules.SmokeStartSize(sizes[i]), 0f);
                Assert.Greater(FxRules.ShockwaveLifetime(sizes[i]), 0f);
            }
        }

        // ------------------------------------------------------------------
        // 水花
        // ------------------------------------------------------------------

        [Test]
        public void SplashCounts_IncreaseWithFallSpeed_AndClamp()
        {
            Assert.AreEqual(10, FxRules.SplashDropletCount(0f), "零速仍给最小水花（落水即死必须看得见）");
            Assert.Greater(FxRules.SplashDropletCount(12f), FxRules.SplashDropletCount(2f));
            Assert.AreEqual(40, FxRules.SplashDropletCount(1000f), "上限钳制");

            Assert.GreaterOrEqual(FxRules.SplashFoamCount(0f), 6);
            Assert.LessOrEqual(FxRules.SplashFoamCount(1000f), 14);
        }

        [Test]
        public void SplashSizesAndRipples_GrowWithFallSpeed_AndStayPositive()
        {
            Assert.Greater(FxRules.SplashDropletSize(10f), FxRules.SplashDropletSize(1f));
            Assert.Greater(FxRules.RippleDiameter(10f), FxRules.RippleDiameter(1f));
            Assert.Greater(FxRules.RippleLifetime(10f), FxRules.RippleLifetime(1f));

            Assert.GreaterOrEqual(FxRules.RippleDiameter(-5f), 1.2f, "负速度被钳到下限");
            Assert.GreaterOrEqual(FxRules.SplashLifetime(-5f), 0.35f);
            Assert.Greater(FxRules.SplashDropletSize(-5f), 0f);
        }

        // ------------------------------------------------------------------
        // 伤害分档
        // ------------------------------------------------------------------

        [Test]
        public void TierFor_UsesDamageOverMaxHealthRatio()
        {
            // 100 血：<15 → 轻；15-34 → 普通；35-59 → 重；>=60 → 致命
            Assert.AreEqual(DamageTier.Light, FxRules.TierFor(14f, 100));
            Assert.AreEqual(DamageTier.Normal, FxRules.TierFor(15f, 100), "边界含在下一档");
            Assert.AreEqual(DamageTier.Normal, FxRules.TierFor(34f, 100));
            Assert.AreEqual(DamageTier.Heavy, FxRules.TierFor(35f, 100));
            Assert.AreEqual(DamageTier.Heavy, FxRules.TierFor(59f, 100));
            Assert.AreEqual(DamageTier.Critical, FxRules.TierFor(60f, 100));
            Assert.AreEqual(DamageTier.Critical, FxRules.TierFor(100f, 100));
        }

        [Test]
        public void TierFor_ZeroMaxHealth_FallsBackToLight()
        {
            Assert.AreEqual(DamageTier.Light, FxRules.TierFor(50f, 0));
        }

        [Test]
        public void TierColors_AreDistinct_AndMatchArtBiblePalette()
        {
            Color32 light = FxRules.TierColor(DamageTier.Light);
            Color32 normal = FxRules.TierColor(DamageTier.Normal);
            Color32 heavy = FxRules.TierColor(DamageTier.Heavy);
            Color32 critical = FxRules.TierColor(DamageTier.Critical);

            Assert.AreNotEqual(light, normal);
            Assert.AreNotEqual(normal, heavy);
            Assert.AreNotEqual(heavy, critical);
            Assert.AreNotEqual(light, critical);

            // 出处：Art Bible §2.3（#F3E9D2）/ §2.2（#FFC24B、#CC2222）/ §2.1（#FF7A1A）
            Assert.AreEqual(FxRules.FromHex(0xF3E9D2), light);
            Assert.AreEqual(FxRules.FromHex(0xFFC24B), normal);
            Assert.AreEqual(FxRules.FromHex(0xFF7A1A), heavy);
            Assert.AreEqual(FxRules.FromHex(0xCC2222), critical);
        }

        [Test]
        public void TierScale_IncreasesWithTier_AndAllAlphaOpaque()
        {
            Assert.Less(FxRules.TierScale(DamageTier.Light), FxRules.TierScale(DamageTier.Normal));
            Assert.Less(FxRules.TierScale(DamageTier.Normal), FxRules.TierScale(DamageTier.Heavy));
            Assert.Less(FxRules.TierScale(DamageTier.Heavy), FxRules.TierScale(DamageTier.Critical));

            for (int i = 0; i <= 3; i++)
                Assert.AreEqual(255, FxRules.TierColor((DamageTier)i).a, "伤害数字本身必须不透明");
        }

        // ------------------------------------------------------------------
        // 命中
        // ------------------------------------------------------------------

        [Test]
        public void HitCounts_ScaleWithDamage_AndClamp()
        {
            Assert.AreEqual(6, FxRules.HitSparkCount(0f, 100));
            Assert.Greater(FxRules.HitSparkCount(60f, 100), FxRules.HitSparkCount(10f, 100));
            Assert.AreEqual(18, FxRules.HitSparkCount(500f, 100));

            Assert.GreaterOrEqual(FxRules.HitDustCount(0f, 100), 3);
            Assert.LessOrEqual(FxRules.HitDustCount(500f, 100), 8);

            Assert.Greater(FxRules.HitSparkLifetime(60f, 100), FxRules.HitSparkLifetime(5f, 100));
        }

        // ------------------------------------------------------------------
        // 拖尾
        // ------------------------------------------------------------------

        [Test]
        public void TrailWidth_ExplosiveWeaponsAreWider()
        {
            Assert.AreEqual(FxRules.TrailBaseWidth, FxRules.TrailWidth(WeaponId.Boulder), 1e-5f,
                "巨石无爆炸 → 基础宽度");
            Assert.AreEqual(FxRules.TrailBaseWidth + FxRules.TrailExplosiveWidthBonus,
                FxRules.TrailWidth(WeaponId.CherryBomb), 1e-5f, "爆炸类更宽，读作危险物");
            Assert.Greater(FxRules.TrailWidth(WeaponId.CherryBomb), FxRules.TrailWidth(WeaponId.Boulder));
        }

        [Test]
        public void TrailColor_DiffersByExplosive()
        {
            Assert.AreNotEqual(FxRules.TrailColor(WeaponId.CherryBomb), FxRules.TrailColor(WeaponId.Boulder));
        }

        // ------------------------------------------------------------------
        // 标记色
        // ------------------------------------------------------------------

        [Test]
        public void MarkerColors_MatchArtBibleTeamAndSelectionColors()
        {
            // 出处：Art Bible §2.2（阵营红 #FF3A29 / 阵营蓝 #3366FF / 选中青 #49D9D6）
            Assert.AreEqual(FxRules.FromHex(0xFF3A29), FxRules.TeamMarkerColor(1));
            Assert.AreEqual(FxRules.FromHex(0x3366FF), FxRules.TeamMarkerColor(2));
            Assert.AreEqual(FxRules.FromHex(0x49D9D6), FxRules.SelectedMarkerColor());
            Assert.AreNotEqual(FxRules.TeamMarkerColor(1), FxRules.TeamMarkerColor(2));
        }

        // ------------------------------------------------------------------
        // 工具函数
        // ------------------------------------------------------------------

        [Test]
        public void FromHex_ConvertsRgbWithOpaqueAlpha()
        {
            Color32 c = FxRules.FromHex(0xFF7A1A);
            Assert.AreEqual(255, c.r);
            Assert.AreEqual(122, c.g);
            Assert.AreEqual(26, c.b);
            Assert.AreEqual(255, c.a);

            Color32 half = FxRules.FromHex(0x010203, 128);
            Assert.AreEqual(1, half.r);
            Assert.AreEqual(2, half.g);
            Assert.AreEqual(3, half.b);
            Assert.AreEqual(128, half.a);
        }

        [Test]
        public void Hash01_IsDeterministic_AndInRange()
        {
            for (int x = -5; x <= 5; x++)
            {
                for (int y = -5; y <= 5; y++)
                {
                    float v = FxRules.Hash01(x, y, 7);
                    Assert.GreaterOrEqual(v, 0f);
                    Assert.Less(v, 1f);
                    Assert.AreEqual(v, FxRules.Hash01(x, y, 7), "同参数必须同结果（确定性）");
                }
            }
        }

        // ------------------------------------------------------------------
        // 材质规格表（纯数据；不触碰 Shader/Material）
        // ------------------------------------------------------------------

        [Test]
        public void MaterialTable_IsConsistentWithTextureKinds()
        {
            Assert.Greater(FxMaterials.Count, 0);

            for (int i = 0; i < FxMaterials.Count; i++)
            {
                FxMaterialSpec spec = FxMaterials.SpecOf((FxMaterial)i);
                Assert.IsFalse(string.IsNullOrEmpty(spec.Name), "材质档 " + i + " 缺资产名");
                Assert.Contains(spec.Texture, FxTextureRules.All,
                    "材质档 " + spec.Name + " 引用了未登记的贴图种类");
                Assert.Greater(spec.Intensity, 0f, "材质档 " + spec.Name + " 强度必须为正");

                Color tint = spec.Tint;
                Assert.Greater(tint.a, 0f, "材质档 " + spec.Name + " 的色必须可见");
            }
        }
    }
}
