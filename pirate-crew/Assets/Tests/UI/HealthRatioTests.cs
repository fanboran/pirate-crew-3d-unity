using NUnit.Framework;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests
{
    /// <summary>
    /// <see cref="BattleHud.HealthRatio"/>（血量 → 血条长度比例，public static 纯函数）的边界钉子。
    ///
    /// 【出处】原版逆向 <c>docs/技术/参考逆向/参考游戏逆向-海盗军团抢宝藏-静态.md</c> §4.1
    /// 的 28 帧口径：血条长度（帧数）= 1 + ceil(27 · hp / max)，折算比例 = 帧数 / 28。
    /// hp<=0 与 max<=0 一律 0（空条），hp>max 夹到满。
    /// 全部不碰 GameObject，无头验证台可跑。
    /// </summary>
    public sealed class HealthRatioTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void ZeroHealth_IsEmptyBar()
        {
            Assert.AreEqual(0f, BattleHud.HealthRatio(0, 100), Eps, "hp=0 应为空条");
            Assert.AreEqual(0f, BattleHud.HealthRatio(-5, 100), Eps, "负 hp 应为空条");
        }

        [Test]
        public void NonPositiveMaxHealth_IsEmptyBar()
        {
            Assert.AreEqual(0f, BattleHud.HealthRatio(10, 0), Eps, "max=0 应为空条");
            Assert.AreEqual(0f, BattleHud.HealthRatio(10, -3), Eps, "负 max 应为空条");
        }

        [Test]
        public void FullHealth_IsExactlyOne()
        {
            Assert.AreEqual(1f, BattleHud.HealthRatio(100, 100), Eps, "满血应恰为 1（28/28 帧）");
            Assert.AreEqual(1f, BattleHud.HealthRatio(1, 1), Eps, "1/1 满血同样 28 帧 = 1");
        }

        [Test]
        public void OneHealth_IsTwoFrames()
        {
            // hp=1（max 充分大）：ceil(27/100)=1 → 帧数 = 2 → 2/28（口径：至少亮 2 帧而不是 0）
            Assert.AreEqual(2f / 28f, BattleHud.HealthRatio(1, 100), Eps, "1 血应亮 2 帧");
        }

        [Test]
        public void OverMaxHealth_ClampsToFull()
        {
            Assert.AreEqual(1f, BattleHud.HealthRatio(200, 100), Eps, "hp>max 应夹到满条");
            Assert.AreEqual(1f, BattleHud.HealthRatio(int.MaxValue, 1), Eps, "极端越界同样夹满");
        }

        [Test]
        public void HalfHealth_LandsOnDiscreteFrame()
        {
            // 27 · 0.5 = 13.5 → ceil = 14 → 帧数 15
            Assert.AreEqual(15f / 28f, BattleHud.HealthRatio(50, 100), Eps,
                "半血应落在离散帧 15/28（ceil 取整，不四舍五入）");
        }

        [Test]
        public void Ratio_IsAlwaysAnIntegerFrameCount()
        {
            // 28 帧口径的整数性：任意存活比例都应落在 {1..28}/28 的离散帧集合上
            int max = 60;
            for (int hp = 1; hp <= max; hp++)
            {
                float ratio = BattleHud.HealthRatio(hp, max);
                float frames = ratio * 28f;
                Assert.That(frames, Is.EqualTo(Mathf.Round(frames)).Within(Eps),
                    $"hp={hp}/{max} 应落在整数帧上");
                Assert.That(ratio, Is.InRange(0f, 1f), "比例必须有界");
                Assert.Greater(ratio, 0f, $"hp={hp} 存活时不得为空条");
            }
        }

        [Test]
        public void Ratio_IsMonotonicInHealth()
        {
            int max = 100;
            float prev = 0f;
            for (int hp = 0; hp <= max; hp++)
            {
                float ratio = BattleHud.HealthRatio(hp, max);
                Assert.GreaterOrEqual(ratio, prev - Eps, $"hp={hp} 时比例不得回退（ceil 单调）");
                prev = ratio;
            }
        }
    }
}
