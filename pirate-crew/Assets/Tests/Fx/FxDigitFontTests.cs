using NUnit.Framework;
using PirateCrew.Fx;
using UnityEngine;

namespace PirateCrew.Fx.Tests
{
    /// <summary>
    /// <see cref="FxDigitFont"/> 的纯点阵用例（无头可跑）。
    ///
    /// 【测什么】伤害数字不依赖任何字体资产，字形是手写点阵；这里钉死三件事：
    ///   1. 尺寸公式（宽 = n×3 + (n−1)，高 = 5，乘 scale、加 outline 留边）；
    ///   2. 字形可区分（'8' 的实心像素远多于 '1'）；
    ///   3. **朝向正确**（贴图 y 向上、字形行号向下，不翻转）——这条最容易写错，
    ///      写错的表现是"伤害数字上下颠倒"，且不会有任何报错。
    /// </summary>
    [TestFixture]
    public class FxDigitFontTests
    {
        static readonly Color32 Fill = new Color32(255, 194, 75, 255);      // #FFC24B
        static readonly Color32 Outline = new Color32(42, 42, 42, 255);     // #2A2A2A

        static int LitCount(Color32[] pixels, Color32 color)
        {
            int n = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r == color.r && pixels[i].g == color.g
                    && pixels[i].b == color.b && pixels[i].a == color.a)
                    n++;
            }
            return n;
        }

        // ------------------------------------------------------------------
        // 字形数据
        // ------------------------------------------------------------------

        [Test]
        public void Measure_ComputesWidthFromDigitCount()
        {
            FxDigitFont.Measure("1", out int oneW, out int oneH);
            Assert.AreEqual(FxDigitFont.GlyphWidth, oneW);
            Assert.AreEqual(FxDigitFont.GlyphHeight, oneH);

            FxDigitFont.Measure("123", out int threeW, out int threeH);
            Assert.AreEqual(3 * FxDigitFont.GlyphWidth + 2 * FxDigitFont.GlyphSpacing, threeW);
            Assert.AreEqual(FxDigitFont.GlyphHeight, threeH);

            FxDigitFont.Measure("", out int emptyW, out int emptyH);
            Assert.AreEqual(FxDigitFont.GlyphWidth, emptyW, "空串给一个字形宽，避免 0 尺寸贴图");
            Assert.AreEqual(FxDigitFont.GlyphHeight, emptyH);
        }

        [Test]
        public void GlyphEight_HasMoreLitPixelsThanOne()
        {
            Assert.AreEqual(13, CountLit("8"), "'8' 的 3×5 点阵应有 13 个实心像素");
            Assert.AreEqual(8, CountLit("1"), "'1' 的 3×5 点阵应有 8 个实心像素");
            Assert.Greater(CountLit("8"), CountLit("1"));
        }

        [Test]
        public void GlyphZero_HasHoleInTheMiddle()
        {
            Assert.IsTrue(FxDigitFont.IsLit('0', 0, 0), "'0' 左上角应实");
            Assert.IsTrue(FxDigitFont.IsLit('0', 2, 0), "'0' 右上角应实");
            Assert.IsFalse(FxDigitFont.IsLit('0', 1, 1), "'0' 中间（第 2 行第 2 列）应是空心");
            Assert.IsFalse(FxDigitFont.IsLit('0', 1, 2), "'0' 中间应是空心");
        }

        [Test]
        public void DotGlyph_HasSinglePixel()
        {
            Assert.AreEqual(1, CountLit("."));
        }

        [Test]
        public void UnknownChar_FallsBackToQuestionLikeGlyph()
        {
            Assert.AreEqual(CountLit("?"), CountLit("@"), "未知字符应回落到同一个兜底字形");
            Assert.Greater(CountLit("@"), 0, "兜底字形不能全空（否则会静默不显示）");
        }

        static int CountLit(string text)
        {
            int n = 0;
            for (int row = 0; row < FxDigitFont.GlyphHeight; row++)
            {
                for (int col = 0; col < FxDigitFont.GlyphWidth; col++)
                {
                    if (FxDigitFont.IsLit(text[0], col, row))
                        n++;
                }
            }
            return n;
        }

        // ------------------------------------------------------------------
        // 像素生成
        // ------------------------------------------------------------------

        [Test]
        public void CreatePixels_SizeIncludesScaleAndOutlinePadding()
        {
            Color32[] pixels = FxDigitFont.CreatePixels("7", 2, 1, Fill, Outline,
                out int width, out int height);

            Assert.AreEqual((FxDigitFont.GlyphWidth + 2) * 2, width);
            Assert.AreEqual((FxDigitFont.GlyphHeight + 2) * 2, height);
            Assert.AreEqual(width * height, pixels.Length);
        }

        [Test]
        public void CreatePixels_WithoutOutline_HasOnlyFillAndTransparent()
        {
            Color32[] pixels = FxDigitFont.CreatePixels("8", 1, 0, Fill, Outline,
                out int width, out int height);

            Assert.AreEqual(FxDigitFont.GlyphWidth, width);
            Assert.AreEqual(FxDigitFont.GlyphHeight, height);
            Assert.AreEqual(13, LitCount(pixels, Fill));
            Assert.AreEqual(0, LitCount(pixels, Outline), "outline=0 时不应出现描边色");
        }

        [Test]
        public void CreatePixels_WithOutline_ProducesOutlinePixelsAroundGlyph()
        {
            Color32[] pixels = FxDigitFont.CreatePixels("8", 1, 1, Fill, Outline,
                out int width, out int height);

            Assert.AreEqual(FxDigitFont.GlyphWidth + 2, width);
            Assert.Greater(LitCount(pixels, Outline), 0, "应生成描边像素");
            Assert.AreEqual(13, LitCount(pixels, Fill), "放大/描边不改变字形实心像素数");
            // 贴图四角都应被描边覆盖（'8' 的上下行都是满行 111），且必须是描边色而不是填充色。
            Assert.AreEqual(Outline, pixels[0], "左下角应为描边色");
            Assert.AreEqual(Outline, pixels[pixels.Length - 1], "右上角应为描边色");
        }

        [Test]
        public void CreatePixels_IsDeterministic()
        {
            Color32[] a = FxDigitFont.CreatePixels("123", 3, 1, Fill, Outline, out _, out _);
            Color32[] b = FxDigitFont.CreatePixels("123", 3, 1, Fill, Outline, out _, out _);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a)
                    Assert.Fail("同一文本两次生成的像素应完全一致（像素 " + i + " 不同）");
            }
        }

        [Test]
        public void CreatePixels_TextIsNotVerticallyFlipped()
        {
            // '1' 的字形：顶行窄（010）、底行宽（111）。
            // 贴图索引 0 = 左下角，故"宽的那行"应出现在贴图底部（y 小），"窄的那行"在顶部（y 大）。
            Color32[] pixels = FxDigitFont.CreatePixels("1", 1, 0, Fill, Outline,
                out int width, out int height);

            int bottomLit = 0;
            int topLit = 0;
            for (int x = 0; x < width; x++)
            {
                if (pixels[0 * width + x].a > 0)
                    bottomLit++;
                if (pixels[(height - 1) * width + x].a > 0)
                    topLit++;
            }

            Assert.Greater(bottomLit, topLit,
                "'1' 的底行应是宽的 111（" + bottomLit + " vs " + topLit + "）—— 数字上下颠倒说明行序翻错了");
        }
    }
}
