using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 符号图标库（代码平涂绘制，tintable：白形 + 0.16 深灰细边，<c>Image.color</c> 染色）。
    ///
    /// 【为什么代码画而不导入图片】① 与 <see cref="CartoonSpriteFactory"/> 同一套平涂语言，
    /// 染色 / 改风格零资产成本；② 几何判定是纯函数（<see cref="InsideGlyph"/>），可无头断言；
    /// ③ 项目纪律：禁止来源不明的图片素材（同 <see cref="UiSprites"/>）。
    ///
    /// 【图标优先原则（用户裁决）】能用图像表示的全用图像：模式开关 = 移动/准星/眼睛三图标、
    /// 动作按钮 = 投掷弧 / 旗、暂停 = 双竖条、确认 = 勾 / 叉……文字只留横幅与提示。
    ///
    /// 坐标约定：<paramref name="fx"/>/<paramref name="fy"/> 以图标中心为原点（y 向上），
    /// <paramref name="half"/> 为半边长；贴图内实际按 2×half 边长逐像素判定。
    /// </summary>
    public static class UiGlyphs
    {
        /// <summary>符号种类。</summary>
        public enum Glyph
        {
            /// <summary>十字准星（操作模式）。</summary>
            Crosshair,
            /// <summary>眼睛（观察模式）。</summary>
            Eye,
            /// <summary>四向箭头（移动模式）。</summary>
            MovePad,
            /// <summary>双竖条（暂停）。</summary>
            Pause,
            /// <summary>实心三角（继续 / 播放）。</summary>
            Play,
            /// <summary>对勾（确认）。</summary>
            Check,
            /// <summary>叉（取消）。</summary>
            Cross,
            /// <summary>骷髅（阵亡 pip / 失败）。</summary>
            Skull,
            /// <summary>五角星（星级 / 胜利）。</summary>
            Star,
            /// <summary>循环箭头（再来一局）。</summary>
            Retry,
            /// <summary>舵轮（返回主菜单 = 回船）。</summary>
            Helm,
            /// <summary>旗帜（结束回合 / 回合徽章）。</summary>
            Flag,
            /// <summary>抛物弧 + 弹体（投掷）。</summary>
            ThrowArc,
        }

        /// <summary>绘制边长（像素；符号按此分辨率的系数画，切片边框 0）。</summary>
        public const int Size = 48;

        static readonly Dictionary<Glyph, Sprite> Cache = new Dictionary<Glyph, Sprite>();

        /// <summary>取内存 Sprite（运行时用；颜色由 Image.color 染）。</summary>
        public static Sprite Get(Glyph glyph)
        {
            if (Cache.TryGetValue(glyph, out Sprite cached) && cached != null)
                return cached;

            Texture2D texture = CreateTexture(glyph);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                Vector4.zero,
                false);
            sprite.name = "Glyph_" + glyph;
            Cache[glyph] = sprite;
            return sprite;
        }

        /// <summary>画一张符号贴图（tintable：白形 + 细边；洞 = 透明）。</summary>
        public static Texture2D CreateTexture(Glyph glyph)
        {
            float half = Size * 0.5f;
            var pixels = new Color32[Size * Size];
            var fill = new Color32(255, 255, 255, 255);
            var edge = new Color(
                UiSkin.StrokeThin > 0f ? 0.16f : 1f,
                UiSkin.StrokeThin > 0f ? 0.16f : 1f,
                UiSkin.StrokeThin > 0f ? 0.16f : 1f, 1f);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // 贴图 y=0 在底部 → 翻转成"y 向上"的符号坐标。
                    float fx = x + 0.5f - half;
                    float fy = (Size - 1 - y) + 0.5f - half;

                    // 细边判定：形状内 1px 圈用深灰（近似——对形状做轻微外扩再比较）。
                    bool inside = InsideGlyph(glyph, fx, fy, half);
                    if (!inside)
                    {
                        pixels[y * Size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    bool onEdge = !InsideGlyph(glyph, fx * 0.90f, fy * 0.90f, half * 0.90f);
                    Color c = onEdge ? edge : fill;
                    pixels[y * Size + x] = new Color32(
                        (byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.name = "GlyphTex_" + glyph;
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, false);
            return texture;
        }

        // ------------------------------------------------------------------
        // 几何判定（纯函数，可无头断言：中心语义 / 远点必外）
        // ------------------------------------------------------------------

        /// <summary>符号形状判定（fx/fy 以中心为原点、y 向上，half = 半边长）。</summary>
        public static bool InsideGlyph(Glyph glyph, float fx, float fy, float half)
        {
            // 归一到 half=24 的设计坐标系（各符号的系数按 48px 设计）。
            const float design = 24f;
            float x = fx * (design / half);
            float y = fy * (design / half);

            switch (glyph)
            {
                case Glyph.Crosshair:
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    bool ring = r <= 10f && r >= 6.5f;
                    bool spokes = (Mathf.Abs(x) <= 1.8f && Mathf.Abs(y) <= 15f)
                                  || (Mathf.Abs(y) <= 1.8f && Mathf.Abs(x) <= 15f);
                    bool hub = r <= 2.5f;
                    return ring || spokes || hub;
                }

                case Glyph.Eye:
                {
                    float ellipse = Mathf.Sqrt(x * x / (196f) + y * y / (81f)); // a=14, b=9
                    bool outline = ellipse <= 1f && ellipse >= 0.72f;
                    bool pupil = x * x + y * y <= 20.25f;                        // r=4.5 实心瞳
                    return outline || pupil;
                }

                case Glyph.MovePad:
                {
                    if (x * x + y * y <= 9f)
                        return true;                                            // 中心圆 r=3
                    // 四个三角箭头（尖向外 r=15，根 r=6，半宽 4.5）。
                    float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                    float r = Mathf.Max(ax, ay);
                    if (r > 15f || r < 6f)
                        return false;
                    return Mathf.Min(ax, ay) <= 4.5f * (15f - r) / 9f + 1.2f;
                }

                case Glyph.Pause:
                    return CartoonSpriteFactory.SdRoundRect(x - 5f, y, 2.6f, 1.5f) <= 0f
                        || CartoonSpriteFactory.SdRoundRect(x + 5f, y, 2.6f, 1.5f) <= 0f;

                case Glyph.Play:
                    return x >= -7f && x <= 11f && Mathf.Abs(y) <= (x + 7f) / 18f * 12f + 0.5f;

                case Glyph.Check:
                    return DistToSegment(x, y, -11f, 0f, -3f, 8f) <= 2.6f
                        || DistToSegment(x, y, -3f, 8f, 12f, -9f) <= 2.6f;

                case Glyph.Cross:
                    return DistToSegment(x, y, -9f, -9f, 9f, 9f) <= 2.8f
                        || DistToSegment(x, y, -9f, 9f, 9f, -9f) <= 2.8f;

                case Glyph.Skull:
                {
                    // 头圆 + 下颚，眼洞 / 鼻洞挖空。
                    bool head = x * x + (y + 1.5f) * (y + 1.5f) <= 100f;         // r=10，中心 (0,-1.5)
                    bool jaw = Mathf.Abs(x) <= 5.5f && y >= 6f && y <= 12f;
                    bool eyeL = (x + 3.9f) * (x + 3.9f) + (y - 0.5f) * (y - 0.5f) <= 7.3f;
                    bool eyeR = (x - 3.9f) * (x - 3.9f) + (y - 0.5f) * (y - 0.5f) <= 7.3f;
                    bool nose = Mathf.Abs(x) <= 1.6f && y >= 2.2f && y <= 5.2f;
                    bool toothGap = Mathf.Abs(x) <= 0.9f && y >= 6f && y <= 9f;
                    return (head || jaw) && !eyeL && !eyeR && !nose && !toothGap;
                }

                case Glyph.Star:
                    return InsideStar(x, y, 11f, 4.9f);

                case Glyph.Retry:
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    bool ring = r <= 10f && r >= 6.5f;
                    // 顶部开口（约 -55°..55° 以竖直向上为 0 时挖空）。
                    float angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;             // 90 = 上
                    bool gap = Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < 42f;
                    // 箭头：开口右端的小三角（指向顺时针 / 向右下）。
                    bool arrow = x >= 2f && x <= 11.5f && y >= 4f
                                 && Mathf.Abs(y - (10f - (x - 2f) * 0.62f)) <= 2.6f;
                    return (ring && !gap) || arrow;
                }

                case Glyph.Helm:
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    bool ring = r <= 9.5f && r >= 7f;
                    bool hub = r <= 4f;
                    // 8 根辐条（45° 一根，从 r=5 伸到 r=15）。
                    float a = Mathf.Atan2(y, x);
                    float sector = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 0f)) % 45f;
                    sector = Mathf.Min(sector, 45f - sector);
                    bool spoke = r <= 15f && r >= 4.5f && sector <= 4.2f;
                    return ring || hub || spoke;
                }

                case Glyph.Flag:
                {
                    bool pole = Mathf.Abs(x + 8f) <= 1.8f && y >= -12f && y <= 13f;
                    // 三角旗：旗杆顶向右展开，随 x 收窄。
                    bool flag = x >= -6.2f && x <= 11f && y <= 12f && y >= 0f
                                && (y - 6f) <= 5.5f * (1f - (x + 6.2f) / 17.2f) + 0.6f
                                && (6f - y) <= 5.5f * (1f - (x + 6.2f) / 17.2f) + 0.6f;
                    return pole || flag;
                }

                case Glyph.ThrowArc:
                {
                    // 抛物弧 y = 9 - x²/14（x ∈ [-11, 11]），线宽 2.4；弹体圆在左端。
                    if (x >= -11f && x <= 11f)
                    {
                        float curve = 9f - x * x / 14f;
                        if (Mathf.Abs(y - curve) <= 2.4f)
                            return true;
                    }
                    bool ball = (x + 11f) * (x + 11f) + (y - 8f) * (y - 8f) <= 12.25f;  // r=3.5
                    // 落点星尘：右端三粒小点。
                    bool sparks = (x - 12f) * (x - 12f) + (y + 5f) * (y + 5f) <= 2.2f
                                  || (x - 9.5f) * (x - 9.5f) + (y + 9f) * (y + 9f) <= 1.6f
                                  || (x - 14f) * (x - 14f) + (y - 0.5f) * (y - 0.5f) <= 1.6f;
                    return ball || sparks;
                }

                default:
                    return false;
            }
        }

        /// <summary>点到线段距离。</summary>
        public static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax, aby = by - ay;
            float apx = px - ax, apy = py - ay;
            float lengthSq = abx * abx + aby * aby;
            float t = lengthSq <= 0f ? 0f : Mathf.Clamp01((apx * abx + apy * aby) / lengthSq);
            float cx = ax + abx * t - px;
            float cy = ay + aby * t - py;
            return Mathf.Sqrt(cx * cx + cy * cy);
        }

        /// <summary>标准五角星判定（角度法，与 UiSprites 同式；tintable 版供染色星级）。</summary>
        public static bool InsideStar(float x, float y, float outerRadius, float innerRadius)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > outerRadius)
                return false;

            float angle = Mathf.Atan2(y, x);
            const float step = Mathf.PI / 5f;
            float phase = angle - Mathf.PI * 0.5f;
            float t = Mathf.Repeat(phase, step) / step;
            float wave = Mathf.Abs(2f * t - 1f);
            float limit = Mathf.Lerp(innerRadius, outerRadius, wave);
            return r <= limit;
        }
    }
}
