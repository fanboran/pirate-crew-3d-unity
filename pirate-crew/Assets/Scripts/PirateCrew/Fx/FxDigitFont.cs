using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 伤害数字用的**程序化点阵字模**（3×5 像素字形，只覆盖数字与几个符号）。
    ///
    /// 【为什么要自己画字】AGENTS.md 的版权纪律禁止引入来源不明素材；而伤害数字若走
    /// TMP / legacy TextMesh，就要依赖 `Assets/Scripts/UI` 波次的字体资产或内置字体
    /// （2022.3 内置 <c>Arial.ttf</c> 已不可靠）。本类用纯几何点阵生成，**与中文字体完全无关**
    /// （伤害数字只有 0-9 与 '-'/'+'/'.'），且算法是纯 C#，可在无头验证台断言。
    ///
    /// 【同一算法两处用】运行时 <c>FxTextures</c> 生成内存贴图；编辑器
    /// `FxAssetBuilder` 只生成形状类贴图（数字组合太多，不逐张落盘，运行时按需缓存）。
    /// </summary>
    public static class FxDigitFont
    {
        /// <summary>字形宽（字体像素）。</summary>
        public const int GlyphWidth = 3;

        /// <summary>字形高（字体像素）。</summary>
        public const int GlyphHeight = 5;

        /// <summary>字距（字体像素，1px 让 3px 宽的字能分开）。</summary>
        public const int GlyphSpacing = 1;

        // 每行 3 bit：bit0 = 最左列。行序自上而下（row 0 = 顶行）。
        static readonly int[] Zero = { 0b111, 0b101, 0b101, 0b101, 0b111 };
        static readonly int[] One = { 0b010, 0b110, 0b010, 0b010, 0b111 };
        static readonly int[] Two = { 0b111, 0b001, 0b111, 0b100, 0b111 };
        static readonly int[] Three = { 0b111, 0b001, 0b111, 0b001, 0b111 };
        static readonly int[] Four = { 0b101, 0b101, 0b111, 0b001, 0b001 };
        static readonly int[] Five = { 0b111, 0b100, 0b111, 0b001, 0b111 };
        static readonly int[] Six = { 0b111, 0b100, 0b111, 0b101, 0b111 };
        static readonly int[] Seven = { 0b111, 0b001, 0b010, 0b010, 0b010 };
        static readonly int[] Eight = { 0b111, 0b101, 0b111, 0b101, 0b111 };
        static readonly int[] Nine = { 0b111, 0b101, 0b111, 0b001, 0b111 };
        static readonly int[] Minus = { 0b000, 0b000, 0b111, 0b000, 0b000 };
        static readonly int[] Plus = { 0b000, 0b010, 0b111, 0b010, 0b000 };
        static readonly int[] Dot = { 0b000, 0b000, 0b000, 0b000, 0b010 };
        static readonly int[] Exclaim = { 0b010, 0b010, 0b010, 0b000, 0b010 };
        static readonly int[] Unknown = { 0b111, 0b001, 0b011, 0b000, 0b010 };

        /// <summary>取字形（返回 5 行、每行 3 bit；未知字符回落到 '?'）。</summary>
        public static int[] GetGlyph(char c)
        {
            switch (c)
            {
                case '0': return Zero;
                case '1': return One;
                case '2': return Two;
                case '3': return Three;
                case '4': return Four;
                case '5': return Five;
                case '6': return Six;
                case '7': return Seven;
                case '8': return Eight;
                case '9': return Nine;
                case '-': return Minus;
                case '+': return Plus;
                case '.': return Dot;
                case '!': return Exclaim;
                default: return Unknown;
            }
        }

        /// <summary>字形是否为实心像素（col 0..2 从左到右，row 0..4 从上到下）。</summary>
        public static bool IsLit(char c, int col, int row)
        {
            if (col < 0 || col >= GlyphWidth || row < 0 || row >= GlyphHeight)
                return false;
            int[] glyph = GetGlyph(c);
            return (glyph[row] & (1 << col)) != 0;
        }

        /// <summary>内容像素尺寸（不含描边）：宽 = n×3 + (n−1)×1；高 = 5。</summary>
        public static void Measure(string text, out int width, out int height)
        {
            int n = string.IsNullOrEmpty(text) ? 0 : text.Length;
            width = n <= 0 ? GlyphWidth : n * GlyphWidth + (n - 1) * GlyphSpacing;
            height = GlyphHeight;
        }

        /// <summary>
        /// 生成整串文本的像素（含描边）。输出像素序为 <c>Texture2D.SetPixels32</c> 约定
        /// （索引 0 = 左下角，逐行向上）。
        /// </summary>
        /// <param name="text">待画文本（通常只有数字）。</param>
        /// <param name="scale">每个字体像素放大成多少屏幕像素（≥1）。</param>
        /// <param name="outline">描边厚度（字体像素；0 = 无描边）。</param>
        /// <param name="fill">字形颜色。</param>
        /// <param name="outlineColor">描边颜色（建议 #2A2A2A，Art Bible §2.5 场景叠加文字要求）。</param>
        public static Color32[] CreatePixels(
            string text, int scale, int outline, Color32 fill, Color32 outlineColor,
            out int width, out int height)
        {
            if (scale < 1)
                scale = 1;
            if (outline < 0)
                outline = 0;

            Measure(text, out int contentW, out int contentH);

            int totalW = (contentW + outline * 2) * scale;
            int totalH = (contentH + outline * 2) * scale;
            var pixels = new Color32[totalW * totalH];

            for (int y = 0; y < totalH; y++)
            {
                for (int x = 0; x < totalW; x++)
                {
                    // 屏幕像素 → 字体像素（含描边留边）。注意"屏幕 y 向上、字形行号向下"，
                    // 故要把从下往上的纹素行号翻成自上而下的字形行号，否则数字上下颠倒。
                    int fx = x / scale - outline;
                    int fyBottom = y / scale - outline;
                    int fy = contentH - 1 - fyBottom;

                    Color32 c;
                    if (IsLitAt(text, fx, fy))
                        c = fill;
                    else if (outline > 0 && IsLitAtOutline(text, fx, fy, outline))
                        c = outlineColor;
                    else
                        c = new Color32(0, 0, 0, 0);

                    pixels[y * totalW + x] = c;
                }
            }

            width = totalW;
            height = totalH;
            return pixels;
        }

        /// <summary>字体像素坐标 (fx, fy) 是否落在字形实心处（fy 以顶部为 0）。</summary>
        static bool IsLitAt(string text, int fx, int fy)
        {
            if (string.IsNullOrEmpty(text) || fy < 0 || fy >= GlyphHeight)
                return false;

            int advance = GlyphWidth + GlyphSpacing;
            int index = fx / advance;
            if (index < 0 || index >= text.Length)
                return false;

            int col = fx - index * advance;
            if (col < 0 || col >= GlyphWidth)
                return false;

            return IsLit(text[index], col, fy);
        }

        /// <summary>字体像素坐标是否在任一实心像素的切比雪夫距离 &lt;= thickness 内（描边扩张）。</summary>
        static bool IsLitAtOutline(string text, int fx, int fy, int thickness)
        {
            for (int dy = -thickness; dy <= thickness; dy++)
            {
                for (int dx = -thickness; dx <= thickness; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    if (IsLitAt(text, fx + dx, fy + dy))
                        return true;
                }
            }
            return false;
        }
    }
}
