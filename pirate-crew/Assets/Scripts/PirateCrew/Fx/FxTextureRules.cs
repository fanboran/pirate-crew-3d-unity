using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>程序化特效贴图种类（与 `Assets/Art/Textures/Fx/*.png` 一一对应）。</summary>
    public enum FxTextureKind
    {
        /// <summary>径向渐变软圆（火球/光晕/泡沫底）。</summary>
        SoftCircle = 0,

        /// <summary>硬边火花点（中心实心 + 十字亮线）。</summary>
        Spark = 1,

        /// <summary>四芒星闪（凹边四角星）。</summary>
        Star4 = 2,

        /// <summary>烟团噪声（FBM 噪声 × 径向衰减，低饱和灰）。</summary>
        Smoke = 3,

        /// <summary>水花飞沫（纵向拉长水滴 + 高光）。</summary>
        Droplet = 4,

        /// <summary>木屑条（横向木条 + 深色端面/纹理）。</summary>
        WoodShard = 5,

        /// <summary>圆环（边缘附近的环带，用于冲击波/涟漪/地面光环）。</summary>
        Ring = 6,

        /// <summary>细小硬点（微型火星）。</summary>
        FineSpark = 7,
    }

    /// <summary>
    /// 特效贴图的**纯像素算法**：输入尺寸，输出 <see cref="Color32"/> 数组。
    ///
    /// 【为什么是纯 C#】本类刻意不碰 <c>Texture2D</c>（ECall 边界，无头验证台会抛
    /// <c>SecurityException</c>，见 `external/m2-harness/README.md`），只算像素。
    /// 同一份算法供两端使用：
    ///   · 运行时：<c>FxTextures</c> 上传到 <c>Texture2D</c>（内存，不落盘）；
    ///   · 编辑器：`Assets/Editor/FxAssetBuilder.cs` 烘成 `Assets/Art/Textures/Fx/*.png` 持久资产。
    /// 这样"运行时看到的样子"与"落盘资产的样子"永远一致，不会出现两套图对不上。
    ///
    /// 【版权纪律】全部像素由确定性哈希/几何公式生成，无任何外部素材（AGENTS.md 参照库/版权纪律）。
    ///
    /// 【配色】形状类（软圆/火花/星/环/飞沫）烘**白色**，颜色由材质 <c>_Color</c> 或粒子
    /// <c>startColor</c> 乘上去——同一张图可复用成火焰/沙尘/泡沫多色。噪声/木屑类烘固有色
    /// （烟灰 / 木色），因为它们自带明暗层次，再乘色会脏。
    /// </summary>
    public static class FxTextureRules
    {
        /// <summary>全部种类（编辑器批量生成用）。</summary>
        public static readonly FxTextureKind[] All =
        {
            FxTextureKind.SoftCircle,
            FxTextureKind.Spark,
            FxTextureKind.Star4,
            FxTextureKind.Smoke,
            FxTextureKind.Droplet,
            FxTextureKind.WoodShard,
            FxTextureKind.Ring,
            FxTextureKind.FineSpark,
        };

        /// <summary>每种的默认边长（【AI 提案】：软圆/烟团 64、环 96、其余小图）。
        /// 原则是"屏幕占位最大者分辨率最高"，且总显存 &lt; 200KB。</summary>
        public static int DefaultSize(FxTextureKind kind)
        {
            switch (kind)
            {
                case FxTextureKind.SoftCircle: return 64;
                case FxTextureKind.Smoke: return 64;
                case FxTextureKind.Ring: return 96;
                case FxTextureKind.Star4: return 32;
                case FxTextureKind.WoodShard: return 32;
                case FxTextureKind.Droplet: return 24;
                case FxTextureKind.Spark: return 16;
                default: return 8;   // FineSpark
            }
        }

        /// <summary>该种类建议的过滤方式：软边用双线性，硬边/点用点采样（避免糊成一团）。</summary>
        public static FilterMode DefaultFilter(FxTextureKind kind)
        {
            switch (kind)
            {
                case FxTextureKind.Spark:
                case FxTextureKind.FineSpark:
                case FxTextureKind.WoodShard:
                    return FilterMode.Point;
                default:
                    return FilterMode.Bilinear;
            }
        }

        /// <summary>生成像素（RGBA32 直通 alpha，非预乘）。</summary>
        public static Color32[] CreatePixels(FxTextureKind kind, int size)
        {
            if (size < 4)
                size = 4;

            var pixels = new Color32[size * size];
            switch (kind)
            {
                case FxTextureKind.SoftCircle: FillSoftCircle(size, pixels); break;
                case FxTextureKind.Spark: FillSpark(size, pixels); break;
                case FxTextureKind.Star4: FillStar4(size, pixels); break;
                case FxTextureKind.Smoke: FillSmoke(size, pixels); break;
                case FxTextureKind.Droplet: FillDroplet(size, pixels); break;
                case FxTextureKind.WoodShard: FillWoodShard(size, pixels); break;
                case FxTextureKind.Ring: FillRing(size, pixels); break;
                default: FillFineSpark(size, pixels); break;
            }
            return pixels;
        }

        /// <summary>按默认边长生成（编辑器/运行时都走这个入口）。</summary>
        public static Color32[] CreatePixels(FxTextureKind kind)
        {
            return CreatePixels(kind, DefaultSize(kind));
        }

        // ------------------------------------------------------------------
        // 各种形状
        // ------------------------------------------------------------------

        /// <summary>软圆：中心 alpha=1，向边缘用 smoothstep 衰减到 0（内部 15% 保持实心）。</summary>
        static void FillSoftCircle(int size, Color32[] pixels)
        {
            float c = (size - 1) * 0.5f;
            float inv = 1f / (size * 0.5f);
            const float core = 0.15f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) * inv;
                    float dy = (y - c) * inv;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 1f - Smoothstep(core, 1f, r);
                    pixels[y * size + x] = White(a);
                }
            }
        }

        /// <summary>硬边火花点：中心 1.5px 实心 + 十字亮线，边缘硬（点采样不糊）。</summary>
        static void FillSpark(int size, Color32[] pixels)
        {
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - c);
                    float dy = Mathf.Abs(y - c);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float a;
                    if (r <= 1.2f)
                        a = 1f;                                   // 实心核心
                    else if (dx <= 0.6f || dy <= 0.6f)
                        a = Mathf.Clamp01(1f - r / c) * 0.95f;    // 十字亮线
                    else
                        a = 0f;                                   // 硬边：之外全透明
                    pixels[y * size + x] = White(a);
                }
            }
        }

        /// <summary>四芒星：astroid 曲线 <c>u^(2/3)+v^(2/3) ≤ 1</c>（凹边四角星），软边收口。</summary>
        static void FillStar4(int size, Color32[] pixels)
        {
            float c = (size - 1) * 0.5f;
            float inv = 1f / (size * 0.5f);
            const float p = 2f / 3f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = Mathf.Abs((x - c) * inv);
                    float v = Mathf.Abs((y - c) * inv);
                    float s = Mathf.Pow(u, p) + Mathf.Pow(v, p);
                    // s=0 中心、s=1 星尖；0.9→1 平滑收边
                    float a = 1f - Smoothstep(0.90f, 1.0f, s);
                    pixels[y * size + x] = White(a);
                }
            }
        }

        /// <summary>烟团：FBM 噪声 × 径向衰减，RGB 低饱和灰（去饱和的岩石中间调）。</summary>
        static void FillSmoke(int size, Color32[] pixels)
        {
            float c = (size - 1) * 0.5f;
            float inv = 1f / (size * 0.5f);
            const int seed = 1337;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) * inv;
                    float dy = (y - c) * inv;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = 1f - Smoothstep(0.15f, 1.05f, r);
                    if (falloff <= 0.001f)
                    {
                        pixels[y * size + x] = White(0f);
                        continue;
                    }

                    // 噪声坐标放慢，出现大团块而不是砂纸感。
                    float n = Fbm(x * 0.13f, y * 0.13f, seed);
                    float a = falloff * (0.45f + 0.55f * n);
                    // 明度随噪声起伏，形成烟团的体积感（0.62 基准 ± 0.16）。
                    float v = Mathf.Clamp01(0.62f + (n - 0.5f) * 0.32f);
                    var col = new Color32(
                        (byte)Mathf.RoundToInt(v * 255f),
                        (byte)Mathf.RoundToInt((v * 0.99f) * 255f),
                        (byte)Mathf.RoundToInt((v * 0.96f) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                    pixels[y * size + x] = col;
                }
            }
        }

        /// <summary>水滴：纵向拉长的椭圆 + 顶部收窄成尖 + 左上高光（冷白）。</summary>
        static void FillDroplet(int size, Color32[] pixels)
        {
            float c = (size - 1) * 0.5f;
            float inv = 1f / (size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - c) * inv;      // -1..1
                    float ny = (y - c) * inv;

                    // 宽度随高度收窄：底部宽（ny=-1 → 0.72）、顶部尖（ny=+1 → 0.06）。
                    float width = Mathf.Lerp(0.72f, 0.06f, Saturate((ny + 1f) * 0.5f));
                    float halfHeight = 0.95f;

                    float rx = Mathf.Abs(nx) / width;
                    float ry = Mathf.Abs(ny) / halfHeight;
                    float r = Mathf.Sqrt(rx * rx + ry * ry);
                    float a = 1f - Smoothstep(0.70f, 1.0f, r);
                    if (a <= 0.001f)
                    {
                        pixels[y * size + x] = White(0f);
                        continue;
                    }

                    // 高光在左上方（光从左上入射，与 Art Bible §4.1 主光方位角一致）。
                    float hl = Saturate(1f - Mathf.Sqrt(
                        (nx + 0.28f) * (nx + 0.28f) + (ny - 0.35f) * (ny - 0.35f)) / 0.42f);
                    float v = Mathf.Clamp01(0.80f + hl * 0.20f);
                    var col = new Color32(
                        (byte)Mathf.RoundToInt(v * 0.86f * 255f),
                        (byte)Mathf.RoundToInt(v * 0.94f * 255f),
                        (byte)Mathf.RoundToInt(v * 1.00f * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                    pixels[y * size + x] = col;
                }
            }
        }

        /// <summary>木屑条：横向木条（长宽比约 4:1）+ 深色边缘 + 2 条木纹 + 一端深色端面。</summary>
        static void FillWoodShard(int size, Color32[] pixels)
        {
            float cy = (size - 1) * 0.5f;
            float halfLen = size * 0.46f;
            float halfThick = Mathf.Max(1f, size * 0.11f);
            const int seed = 4242;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - (size - 1) * 0.5f);
                    float dy = Mathf.Abs(y - cy);

                    if (dx > halfLen || dy > halfThick)
                    {
                        pixels[y * size + x] = White(0f);
                        continue;
                    }

                    // 端部斜切：左右两端沿厚度收一点，读作"断裂的木片"。
                    float endT = Saturate((dx - halfLen * 0.75f) / (halfLen * 0.25f));
                    float allowed = halfThick * (1f - endT * 0.55f);
                    if (dy > allowed)
                    {
                        pixels[y * size + x] = White(0f);
                        continue;
                    }

                    float edge = 1f - Saturate(dy / halfThick);           // 0 边缘 → 1 中心
                    float grain = Hash01(x / 2, y, seed) * 0.10f - 0.05f; // 木纹微扰
                    float v = Mathf.Clamp01(0.52f + edge * 0.30f + grain);

                    // 木色 #A67B42（Art Bible §2.1 木材中间调）按明度缩放。
                    var col = new Color32(
                        (byte)Mathf.RoundToInt(v * 0.650f * 255f),
                        (byte)Mathf.RoundToInt(v * 0.482f * 255f),
                        (byte)Mathf.RoundToInt(v * 0.259f * 255f),
                        255);
                    pixels[y * size + x] = col;
                }
            }
        }

        /// <summary>圆环：环带峰值在 rNorm≈0.78，内外都平滑收口；环内透明。</summary>
        static void FillRing(int size, Color32[] pixels)
        {
            float c = (size - 1) * 0.5f;
            float inv = 1f / (size * 0.5f);
            const float peak = 0.78f;
            const float inner = 0.52f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) * inv;
                    float dy = (y - c) * inv;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    float a;
                    if (r < inner || r > 1.0f)
                        a = 0f;
                    else if (r < peak)
                        a = Smoothstep(inner, peak, r);
                    else
                        a = 1f - Smoothstep(peak, 1.0f, r);
                    pixels[y * size + x] = White(a);
                }
            }
        }

        /// <summary>细小硬点：1.2px 实心 + 极细十字，纯硬边。</summary>
        static void FillFineSpark(int size, Color32[] pixels)
        {
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - c);
                    float dy = Mathf.Abs(y - c);
                    bool on = dx <= 0.6f && dy <= 0.6f;      // 1-2 px 实心
                    pixels[y * size + x] = White(on ? 1f : 0f);
                }
            }
        }

        // ------------------------------------------------------------------
        // 纯工具（确定性，无 UnityEngine.Random）
        // ------------------------------------------------------------------

        static Color32 White(float alpha)
        {
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
        }

        static float Saturate(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>标准 smoothstep：edge0 处 0、edge1 处 1，中间三次缓和。</summary>
        static float Smoothstep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1))
                return x < edge0 ? 0f : 1f;
            float t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>确定性哈希 → [0,1)。</summary>
        static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                h = h ^ (h >> 16);
                return (h & 0x7FFFFFFF) / 2147483648f;
            }
        }

        /// <summary>双线性插值的值噪声。</summary>
        static float ValueNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            float a = Hash01(xi, yi, seed);
            float b = Hash01(xi + 1, yi, seed);
            float cc = Hash01(xi, yi + 1, seed);
            float d = Hash01(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(cc, d, u), v);
        }

        /// <summary>4 阶 FBM，返回 [0,1]。</summary>
        static float Fbm(float x, float y, int seed)
        {
            float sum = 0f;
            float amp = 0.5f;
            float norm = 0f;
            for (int i = 0; i < 4; i++)
            {
                sum += ValueNoise(x, y, seed + i * 131) * amp;
                norm += amp;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f + 9.7f;
                amp *= 0.5f;
            }
            return sum / Mathf.Max(norm, 1e-5f);
        }
    }
}
