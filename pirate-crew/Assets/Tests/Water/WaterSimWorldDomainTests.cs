using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water.Tests
{
    /// <summary>
    /// <see cref="WaterSimRules.WorldDomainSizeForSpan"/> 测试：世界地图模拟域边长的等比伸缩
    /// （×0.9【提案/待定】）与 clamp 边界（下限 = 旧竞技场域 128、上限 256），以及
    /// "域扩大不破坏 CFL 稳定域"的性质——域边长只影响格距 dx，dx 变大 → 库朗数变小。
    /// 覆盖世界地图目录的典型跨度 150/190/220/260（M4 八图的 150–260u 跨度带）。
    /// </summary>
    [TestFixture]
    public class WaterSimWorldDomainTests
    {
        static readonly float[] TypicalMapSpans = { 150f, 190f, 220f, 260f };

        [Test]
        public void TypicalMapSpans_ScaleLinearly()
        {
            Assert.AreEqual(135f, WaterSimRules.WorldDomainSizeForSpan(150f), 1e-4f);
            Assert.AreEqual(171f, WaterSimRules.WorldDomainSizeForSpan(190f), 1e-4f);
            Assert.AreEqual(198f, WaterSimRules.WorldDomainSizeForSpan(220f), 1e-4f);
            Assert.AreEqual(234f, WaterSimRules.WorldDomainSizeForSpan(260f), 1e-4f);
        }

        [Test]
        public void SmallSpans_ClampToMinDomain()
        {
            // 128 × 0.9 = 115.2 → 顶回下限（下限 = DefaultDomainSize，分辨率/观感口径不变）。
            Assert.AreEqual(WaterSimRules.MinWorldDomainSize,
                WaterSimRules.WorldDomainSizeForSpan(128f), 1e-4f);
            Assert.AreEqual(WaterSimRules.MinWorldDomainSize,
                WaterSimRules.WorldDomainSizeForSpan(100f), 1e-4f);

            // 退化输入不抛异常，回到下限。
            Assert.AreEqual(WaterSimRules.MinWorldDomainSize,
                WaterSimRules.WorldDomainSizeForSpan(0f), 1e-4f);
            Assert.AreEqual(WaterSimRules.MinWorldDomainSize,
                WaterSimRules.WorldDomainSizeForSpan(-20f), 1e-4f);
        }

        [Test]
        public void HugeSpans_ClampToMaxDomain()
        {
            Assert.AreEqual(WaterSimRules.MaxWorldDomainSize,
                WaterSimRules.WorldDomainSizeForSpan(300f), 1e-4f);
            Assert.AreEqual(WaterSimRules.MaxWorldDomainSize,
                WaterSimRules.WorldDomainSizeForSpan(1000f), 1e-4f);

            // 上限内侧仍线性：284 × 0.9 = 255.6 < 256，不触发 clamp。
            Assert.AreEqual(255.6f, WaterSimRules.WorldDomainSizeForSpan(284f), 1e-4f);
        }

        [Test]
        public void ExpandedDomain_StaysInsideCflStableRegion()
        {
            // 驱动默认波速 18（场景序列化值）、固定步长 1/60；
            // 格数固定 128，域边长只改 dx。逐档断言 C ≤ CflLimit（0.495）。
            const float waveSpeed = 18f;
            const float dt = 1f / 60f;
            const int cells = WaterSimRules.DefaultCellsPerAxis;

            foreach (float span in TypicalMapSpans)
            {
                float size = WaterSimRules.WorldDomainSizeForSpan(span);
                float dx = size / cells;
                float cfl = WaterSimRules.CflNumber(waveSpeed, dt, dx);

                Assert.IsTrue(WaterSimRules.IsStable(waveSpeed, dt, dx),
                    $"跨度 {span} → 域 {size}（dx={dx:F3}）CFL={cfl:F3} 超出稳定域");
                Assert.That(cfl, Is.LessThanOrEqualTo(WaterSimRules.CflLimit));

                // 域扩大只会放宽 CFL 上限：dx ≥ 默认域的 dx（1.0）。
                Assert.That(dx, Is.GreaterThanOrEqualTo(
                    WaterSimRules.DefaultDomainSize / cells - 1e-5f));
            }
        }

        [Test]
        public void WorldDomainMapping_CoversMapCenterAtDomainCenter()
        {
            // 装配契约：域心 = 图心时，图心必然映射到域 UV (0.5, 0.5)（InjectSplash 的域内判定基准）。
            foreach (float span in TypicalMapSpans)
            {
                float size = WaterSimRules.WorldDomainSizeForSpan(span);
                var center = new Vector2(span * 0.5f, span * 0.5f);
                Vector2 uv = WaterSimRules.WorldToDomainUv(center, center, size);
                Assert.AreEqual(0.5f, uv.x, 1e-5f);
                Assert.AreEqual(0.5f, uv.y, 1e-5f);
            }
        }
    }
}
