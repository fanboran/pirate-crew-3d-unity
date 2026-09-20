using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Fx;
using UnityEngine;

namespace PirateCrew.Fx.Tests
{
    /// <summary>
    /// <see cref="FxTextureRules"/> 的纯像素用例（无头可跑：本类只返回 <c>Color32[]</c>，不建 Texture2D）。
    ///
    /// 【测法的意义】程序化贴图最容易"跑起来不报错、但生成出来是一整块"（哈希写错/坐标恒定）。
    /// 这些用例直接按像素采样断言形状特征（中心实/边缘透、环带峰值位置、条状长宽比、
    /// 水滴纵向拉长），把"算法退化成色块"挡住。判据全部是"关系"而非精确像素值。
    /// </summary>
    [TestFixture]
    public class FxTextureRulesTests
    {
        static Color32 At(Color32[] pixels, int size, int x, int y)
        {
            return pixels[y * size + x];
        }

        static float AlphaAt(Color32[] pixels, int size, int x, int y)
        {
            return At(pixels, size, x, y).a / 255f;
        }

        // ------------------------------------------------------------------
        // 通用性质
        // ------------------------------------------------------------------

        [Test]
        public void AllKinds_ProduceDeterministicPixels()
        {
            for (int k = 0; k < FxTextureRules.All.Length; k++)
            {
                FxTextureKind kind = FxTextureRules.All[k];
                int size = FxTextureRules.DefaultSize(kind);
                Color32[] a = FxTextureRules.CreatePixels(kind, size);
                Color32[] b = FxTextureRules.CreatePixels(kind, size);

                Assert.AreEqual(size * size, a.Length, kind + " 像素数应为 size²");
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a)
                        Assert.Fail(kind + " 在像素 " + i + " 上不满足确定性（同参数两次结果不同）");
                }
            }
        }

        [Test]
        public void AllKinds_HaveSomeVisiblePixels()
        {
            for (int k = 0; k < FxTextureRules.All.Length; k++)
            {
                FxTextureKind kind = FxTextureRules.All[k];
                int size = FxTextureRules.DefaultSize(kind);
                Color32[] pixels = FxTextureRules.CreatePixels(kind, size);

                int visible = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 8)
                        visible++;
                }

                // FineSpark 是 2×2 硬点（4 像素），其余种类的可见像素都应多于边长。
                Assert.GreaterOrEqual(visible, 4, kind + " 几乎全透明，贴图等于没生成");
                if (kind != FxTextureKind.FineSpark)
                    Assert.Greater(visible, size, kind + " 可见像素过少，形状算法可能退化成点");
            }
        }

        [Test]
        public void DefaultSizesAndFilters_AreSane()
        {
            for (int k = 0; k < FxTextureRules.All.Length; k++)
            {
                FxTextureKind kind = FxTextureRules.All[k];
                Assert.GreaterOrEqual(FxTextureRules.DefaultSize(kind), 8, kind + " 边长过小");
                Assert.LessOrEqual(FxTextureRules.DefaultSize(kind), 128, kind + " 边长超过预算");
            }

            // 硬边类必须点采样，否则边缘会被插值糊掉（火花读起来像雾）。
            Assert.AreEqual(FilterMode.Point, FxTextureRules.DefaultFilter(FxTextureKind.Spark));
            Assert.AreEqual(FilterMode.Point, FxTextureRules.DefaultFilter(FxTextureKind.FineSpark));
            Assert.AreEqual(FilterMode.Bilinear, FxTextureRules.DefaultFilter(FxTextureKind.Smoke));
        }

        // ------------------------------------------------------------------
        // SoftCircle：中心实、边缘透、径向单调
        // ------------------------------------------------------------------

        [Test]
        public void SoftCircle_IsOpaqueAtCenter_TransparentAtCorner_AndMonotonic()
        {
            const int size = 64;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.SoftCircle, size);
            int c = (size - 1) / 2;

            Assert.AreEqual(1f, AlphaAt(pixels, size, c, c), 1e-3f, "中心应完全不透明");
            Assert.AreEqual(0f, AlphaAt(pixels, size, 0, 0), 1e-3f, "角落应完全透明");

            float previous = 2f;
            for (int x = c; x < size; x++)
            {
                float a = AlphaAt(pixels, size, x, c);
                Assert.LessOrEqual(a, previous + 1e-4f, "沿半径方向 alpha 必须单调不增（x=" + x + "）");
                previous = a;
            }
        }

        // ------------------------------------------------------------------
        // Spark / FineSpark：硬边
        // ------------------------------------------------------------------

        [Test]
        public void Spark_IsHardEdged()
        {
            const int size = 16;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.Spark, size);
            int c = (size - 1) / 2;

            Assert.AreEqual(255, At(pixels, size, c, c).a, "火花中心必须全实");
            Assert.AreEqual(0, At(pixels, size, 0, 0).a, "远离十字线的角落必须全透（硬边）");
            Assert.Greater(At(pixels, size, c, 1).a, 0, "十字亮线应到达边缘附近");

            // 硬边：不存在中间 alpha（除十字线的渐隐），至少应同时有 0 与 255 两类。
            bool hasZero = false;
            bool hasFull = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 0) hasZero = true;
                if (pixels[i].a == 255) hasFull = true;
            }
            Assert.IsTrue(hasZero && hasFull, "火花应同时存在全透与全实像素");
        }

        [Test]
        public void FineSpark_IsTinyAndHard()
        {
            const int size = 8;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.FineSpark, size);

            Assert.AreEqual(255, At(pixels, size, 3, 3).a);
            Assert.AreEqual(255, At(pixels, size, 4, 4).a);
            Assert.AreEqual(0, At(pixels, size, 0, 0).a);
            Assert.AreEqual(0, At(pixels, size, 7, 7).a);
        }

        // ------------------------------------------------------------------
        // Star4
        // ------------------------------------------------------------------

        [Test]
        public void Star4_HasPointsOnAxes_AndTransparentDiagonals()
        {
            const int size = 32;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.Star4, size);
            int c = (size - 1) / 2;
            const int r = 8;   // 距星心 8px：轴向应亮、对角应暗（凹边四角星的特征）

            Assert.Greater(AlphaAt(pixels, size, c, c), 0.9f, "星心应实");
            Assert.Greater(AlphaAt(pixels, size, c, c - r), 0.8f, "上尖方向应亮");
            Assert.Greater(AlphaAt(pixels, size, c - r, c), 0.8f, "左尖方向应亮");
            Assert.Greater(AlphaAt(pixels, size, c + r, c), 0.8f, "右尖方向应亮");
            Assert.Greater(AlphaAt(pixels, size, c, c + r), 0.8f, "下尖方向应亮");

            // 对角方向必须是暗的，否则就退化成圆/方块了。
            Assert.Less(AlphaAt(pixels, size, c - r, c - r), 0.1f, "对角（凹边处）应透");
            Assert.Less(AlphaAt(pixels, size, c + r, c + r), 0.1f, "对角（凹边处）应透");
        }

        // ------------------------------------------------------------------
        // Smoke：中心有、边缘无、噪声非均匀
        // ------------------------------------------------------------------

        [Test]
        public void Smoke_HasNoiseAndRadialFalloff()
        {
            const int size = 64;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.Smoke, size);
            int c = (size - 1) / 2;

            Assert.Greater(AlphaAt(pixels, size, c, c), 0.2f, "烟团中心应有实体");
            Assert.Less(AlphaAt(pixels, size, 0, 0), 0.05f, "角落应被径向衰减清掉");

            var alphas = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 0)
                    alphas.Add(pixels[i].a);
            }
            Assert.Greater(alphas.Count, 8, "烟雾应是噪声云而非纯色圆（不同 alpha 档过少）");

            Color32 center = At(pixels, size, c, c);
            Assert.GreaterOrEqual(center.r, center.b, "烟雾应为低饱和灰（R 不应低于 B）");
        }

        // ------------------------------------------------------------------
        // Droplet：纵向拉长
        // ------------------------------------------------------------------

        [Test]
        public void Droplet_IsTallerThanWide()
        {
            const int size = 24;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.Droplet, size);

            int minX = size, maxX = -1, minY = size, maxY = -1;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (pixels[y * size + x].a <= 8)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            Assert.Greater(height, width, "水滴应纵向拉长（height=" + height + " width=" + width + "）");
        }

        // ------------------------------------------------------------------
        // WoodShard：横向木条 + 木色
        // ------------------------------------------------------------------

        [Test]
        public void WoodShard_IsHorizontalBar_WithWoodColor()
        {
            const int size = 32;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.WoodShard, size);

            int minX = size, maxX = -1, minY = size, maxY = -1;
            long sumR = 0, sumG = 0, sumB = 0;
            int count = 0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color32 p = pixels[y * size + x];
                    if (p.a <= 8)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    sumR += p.r;
                    sumG += p.g;
                    sumB += p.b;
                    count++;
                }
            }

            Assert.Greater(count, 0, "木屑不应全透");
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            Assert.Greater(width, height * 2, "木屑应为横向长条（width=" + width + " height=" + height + "）");

            // 木色 #A67B42（Art Bible §2.1 木材中调）→ R > G > B。
            Assert.Greater(sumR / count, sumG / count, "木屑平均 R 应大于 G");
            Assert.Greater(sumG / count, sumB / count, "木屑平均 G 应大于 B");
        }

        // ------------------------------------------------------------------
        // Ring：中空、环带峰值靠外
        // ------------------------------------------------------------------

        [Test]
        public void Ring_IsHollow_WithPeakNearOuterEdge()
        {
            const int size = 96;
            Color32[] pixels = FxTextureRules.CreatePixels(FxTextureKind.Ring, size);
            int c = (size - 1) / 2;

            Assert.AreEqual(0, At(pixels, size, c, c).a, "环中心必须空");
            Assert.AreEqual(0, At(pixels, size, 0, 0).a, "环外角必须空");

            // 峰值应落在 r ≈ 0.78 半宽处
            int peakX = c + Mathf.RoundToInt(0.78f * size * 0.5f);
            Assert.Greater(At(pixels, size, peakX, c).a, 150,
                "环带峰值应在 r≈0.78 半宽处（x=" + peakX + "）");

            float inner = AlphaAt(pixels, size, c + Mathf.RoundToInt(0.20f * size * 0.5f), c);
            float outer = AlphaAt(pixels, size, c + Mathf.RoundToInt(0.95f * size * 0.5f), c);
            Assert.Less(inner, 0.05f, "环内侧（r=0.20）应接近透明");
            Assert.Less(outer, 0.6f, "环外缘（r=0.95）应已开始收口");
        }
    }
}
