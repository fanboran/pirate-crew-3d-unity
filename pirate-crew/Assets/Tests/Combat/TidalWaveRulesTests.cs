using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;

namespace PirateCrew.PirateCrew.Combat.Tests
{
    /// <summary>
    /// <see cref="TidalWaveRules"/> 测试（§5.2 tidalWave 行）。
    /// 距离判定用「dx = 浪与目标的 X 差、dy = 世界 Y 差」（Z 折叠，见规则类头）。
    /// </summary>
    [TestFixture]
    public class TidalWaveRulesTests
    {
        [Test]
        public void Constants_MatchSection5_2()
        {
            Assert.AreEqual(-550f, TidalWaveRules.SpawnFlashX);
            Assert.AreEqual(20f, TidalWaveRules.SweepSpeed);
            Assert.AreEqual(5f, TidalWaveRules.DamagePerFrame);
            Assert.AreEqual(150f, TidalWaveRules.HitRadius);
            Assert.AreEqual(300f, TidalWaveRules.VerticalReach);
        }

        [Test]
        public void StepX_SweepsRightAt20PxPerFrame()
        {
            Assert.AreEqual(-530f, TidalWaveRules.StepX(-550f), 1e-4f);
            Assert.AreEqual(-490f, TidalWaveRules.StepX(-550f, 3), 1e-4f);
        }

        [Test]
        public void IsWithinBlast_Radius150()
        {
            Assert.IsTrue(TidalWaveRules.IsWithinBlast(0f, 0f));
            Assert.IsTrue(TidalWaveRules.IsWithinBlast(150f, 0f), "边界 150 命中（<=）");
            Assert.IsFalse(TidalWaveRules.IsWithinBlast(151f, 0f));
            Assert.IsTrue(TidalWaveRules.IsWithinBlast(0f, 150f));
            // 106² + 106² = 22472 <= 22500 → 命中；107² + 107² = 22898 > 22500 → 不命中。
            Assert.IsTrue(TidalWaveRules.IsWithinBlast(106f, 106f));
            Assert.IsFalse(TidalWaveRules.IsWithinBlast(107f, 107f));
        }

        [Test]
        public void IsAboveVerticalReach_RequiresTargetNotBelowWaterMinus300()
        {
            // Flash y 向下为正：waterY=500，阈值为 200；y >= 200 才算在有效高度。
            Assert.IsTrue(TidalWaveRules.IsAboveVerticalReach(500f, 500f));
            Assert.IsTrue(TidalWaveRules.IsAboveVerticalReach(200f, 500f), "恰好等于阈值算有效（>=）");
            Assert.IsFalse(TidalWaveRules.IsAboveVerticalReach(199f, 500f));
        }

        [Test]
        public void ShouldDamage_RequiresBothRadiusAndVerticalReach()
        {
            const float waterY = 500f;
            // 浪在 (0, 500)；目标 (100, 480)：dx=100, dy=-20 → 100²+20²=10400 <22500，且 480>=200。
            Assert.IsTrue(TidalWaveRules.ShouldDamage(0f, 500f, 100f, 480f, waterY));
            // 目标太远（dx=200）。
            Assert.IsFalse(TidalWaveRules.ShouldDamage(0f, 500f, 200f, 500f, waterY));
            // 目标在浪附近但低于 waterY-300。
            Assert.IsFalse(TidalWaveRules.ShouldDamage(0f, 500f, 0f, 199f, waterY));
        }

        [Test]
        public void IsPastRightEdge_UsesLevelWidth()
        {
            // 17 瓦片 → 右边界 544。
            Assert.IsFalse(TidalWaveRules.IsPastRightEdge(544f, 17f));
            Assert.IsTrue(TidalWaveRules.IsPastRightEdge(544.001f, 17f));
        }
    }
}
