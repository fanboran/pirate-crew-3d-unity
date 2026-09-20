using NUnit.Framework;
using PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="WeaponProjectile"/> 纯函数口径测试。
    /// 覆盖：火焰击退的世界增量（审计 代码审计报告 §一.5——水平击退不得恒落世界 X 轴）。
    /// 逐帧计数的 FixedUpdate 化属结构性修复（Update/FixedUpdate 的迁移无法在 EditMode 断言），
    /// 其数值口径（tidalWave 每帧 5 点、引信 60 帧、anchor hold30+fade10 等）由各 *Rules 测试钉住。
    /// </summary>
    [TestFixture]
    public class WeaponProjectileFlashFrameTests
    {
        [Test]
        public void FlameKnockback_TargetNorthOfFlame_PushesAlongZ()
        {
            // 目标在火焰正北（+Z 方位）：正随机量的水平击退应落在 Z 轴上，X 分量为 0。
            Vector3 delta = WeaponProjectile.FlameKnockbackWorldDelta(
                new Vector2(0f, 5f), random01: 0.75f);

            Assert.Greater(delta.z, 0f);
            Assert.AreEqual(0f, delta.x, 1e-4f);
            // 上抛恒为正（原版 vy = -(rand*2+6)，取负后向上）。
            Assert.Greater(delta.y, 0f);
        }

        [Test]
        public void FlameKnockback_NegativeRoll_PushesTowardFlame_SemanticsPreserved()
        {
            // 原版 vx = (rand-0.5)*8 带符号（一半概率把人推回火里）；方位轴投影不得丢掉这个符号。
            Vector3 delta = WeaponProjectile.FlameKnockbackWorldDelta(
                new Vector2(0f, 5f), random01: 0.25f);

            Assert.Less(delta.z, 0f);
        }

        [Test]
        public void FlameKnockback_EastBearing_MatchesLegacyWorldX()
        {
            // 目标在火焰正东（+X 方位）：数值与旧公式（恒世界 X）完全一致——修复只改方位轴。
            Vector3 delta = WeaponProjectile.FlameKnockbackWorldDelta(
                new Vector2(5f, 0f), random01: 0.75f);

            Assert.Greater(delta.x, 0f);
            Assert.AreEqual(0f, delta.z, 1e-4f);
        }

        [Test]
        public void FlameKnockback_DegenerateBearing_FallsBackToWorldX()
        {
            // 目标与火焰几乎重合（方位零向量）：回退 +X，不产生 NaN。
            Vector3 delta = WeaponProjectile.FlameKnockbackWorldDelta(
                Vector2.zero, random01: 0.9f);

            Assert.IsFalse(float.IsNaN(delta.x) || float.IsNaN(delta.z));
            Assert.GreaterOrEqual(delta.x, 0f);
            Assert.AreEqual(0f, delta.z, 1e-4f);
        }

        [Test]
        public void FlameKnockback_HorizontalMagnitude_InvariantToBearing()
        {
            // 同一 roll 下水平合速度模长与方位无关（只改方向不改量值）。
            Vector3 a = WeaponProjectile.FlameKnockbackWorldDelta(new Vector2(0f, 5f), 0.75f);
            Vector3 b = WeaponProjectile.FlameKnockbackWorldDelta(new Vector2(3f, -4f), 0.75f);

            float magA = Mathf.Sqrt(a.x * a.x + a.z * a.z);
            float magB = Mathf.Sqrt(b.x * b.x + b.z * b.z);
            Assert.AreEqual(magA, magB, 1e-4f);
            Assert.AreEqual(a.y, b.y, 1e-6f);
        }
    }
}
