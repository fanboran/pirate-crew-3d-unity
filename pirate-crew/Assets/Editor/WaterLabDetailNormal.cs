using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 实验水体细节法线贴图的程序化生成（WaterNormal2D 的等价物）。
    ///
    /// 【逆向依据】Sandcastle Lit/Water PS：水面亮脊网 = 细节法线的棱线被**宽瓣**镜面
    /// 连续点亮（原作 GGX 宽瓣 + pow1500 尖瓣并存；尖瓣只出孤立碎点——同一贴图在
    /// pow400/p80 两种瓣下的形态对照实证，调研档案 §4.1）。本件只负责贴图；
    /// 宽瓣能量在 WaterLabSetup（_SpecularBroad=0.10，装配器所有权路径）。
    ///
    /// 【生成法（第十二轮终版）】原作贴图本体已抽出实拍（UnityPy，external/sandcastle-ref/）：
    /// 有机「鹅卵石」胞、胞界成脊、R/G std≈33/27。经四版对照（平滑值噪声→F1−F2
    /// Voronoi→平滑鼓包核→**域扭曲+脊状 fbm**），只有末版在宽瓣下出原作那种
    /// **长程蜿蜒的连通亮丝线**（8.2% vs 原作 11.3%，同口径 p80 镜面渲染对照）——
    /// 长连通脊线正是 ridged multifractal 的标志性形态，域扭曲（fbm 推弯采样坐标）
    /// 供给有机蜿蜒度。晶格 wrap 天然可平铺；生成后按 R 通道 std 自标定到原作同统计。
    /// 不拷原作资产，只对齐统计特征与形态。
    /// </summary>
    public static class WaterLabDetailNormal
    {
        const int Size = 512;

        /// <summary>R 通道编码 std 目标（原作 WaterNormal2D 实测 33/255）。</summary>
        const float TargetChannelStd = 33f / 255f;

        /// <summary>生成可平铺细节法线贴图（RG=法线 xy 编码，B=高度，A=1）。</summary>
        public static Texture2D Create()
        {
            var height = new float[Size * Size];

            // 域扭曲：采样坐标被低频 fbm 推弯 → 脊线长程蜿蜒（不做这一步脊线短促发闷）
            for (int y = 0; y < Size; y++)
            {
                float v = y / (float)Size;
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float wx = (Fbm(u, v, 3, 3, 11) - 0.5f) * 1.5f;
                    float wy = (Fbm(u, v, 3, 3, 29) - 0.5f) * 1.5f;
                    float r1 = Ridged(u + wx, v + wy, 4, 5, 53);
                    float r2 = Ridged(u + wx, v + wy, 9, 4, 97);
                    height[y * Size + x] = r1 + r2 * 0.3f;
                }
            }
            BlurWrap(height, 1.5f);

            // 归一化到零均值单位差
            float mean = 0f;
            for (int i = 0; i < height.Length; i++) mean += height[i];
            mean /= height.Length;
            float std = 0f;
            for (int i = 0; i < height.Length; i++) { float d = height[i] - mean; std += d * d; }
            std = Mathf.Sqrt(std / height.Length);
            for (int i = 0; i < height.Length; i++) height[i] = (height[i] - mean) / std;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) // mipChain=true：远处用高层级抑制摩尔纹
            {
                name = "WaterLabDetailNormal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[Size * Size];
            var nxArr = new float[Size * Size];
            var nyArr = new float[Size * Size];
            float gradStd = 0f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float hl = height[y * Size + (x - 1 & Size - 1)];
                    float hr = height[y * Size + (x + 1 & Size - 1)];
                    float hd = height[(y - 1 & Size - 1) * Size + x];
                    float hu = height[(y + 1 & Size - 1) * Size + x];
                    float nx = (hl - hr) * 0.5f;
                    float ny = (hd - hu) * 0.5f;
                    nxArr[y * Size + x] = nx;
                    nyArr[y * Size + x] = ny;
                    gradStd += nx * nx;
                }
            }
            // 自标定：缩放梯度使编码后 R 通道 std 与原作同统计
            gradStd = Mathf.Sqrt(gradStd / (Size * Size));
            float strength = TargetChannelStd * 2f / Mathf.Max(gradStd, 1e-6f); // 编码 0.5+0.5n，std 编码 = std(n)/2
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float nx = nxArr[i] * strength;
                    float ny = nyArr[i] * strength;
                    float len = Mathf.Sqrt(nx * nx + ny * ny + 1f);
                    pixels[i] = new Color(
                        nx / len * 0.5f + 0.5f,
                        ny / len * 0.5f + 0.5f,
                        height[i] * 0.5f + 0.5f,
                        1f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true, false); // 更新 mip 链；保持可读：CreateAsset 序列化需要像素数据
            return tex;
        }

        static readonly System.Collections.Generic.Dictionary<long, float[]> LatticeCache
            = new System.Collections.Generic.Dictionary<long, float[]>();

        /// <summary>晶格随机数表（按 freq+seed 缓存；同一 (freq,seed) 恒定 → 生成可复现）。</summary>
        static float[] Lattice(int freq, int seed)
        {
            long key = ((long)freq << 32) ^ (uint)seed;
            if (LatticeCache.TryGetValue(key, out var cached))
                return cached;
            var lat = new float[freq * freq];
            var rand = new System.Random(seed);
            for (int i = 0; i < lat.Length; i++)
                lat[i] = (float)rand.NextDouble();
            LatticeCache[key] = lat;
            return lat;
        }

        static int Mod(int a, int m) => (a % m + m) % m;

        /// <summary>可平铺值噪声（晶格 wrap，任意坐标合法——含域扭曲后的越界坐标）。</summary>
        static float ValueNoise(float gx, float gy, int freq, int seed)
        {
            var lat = Lattice(freq, seed);
            int x0 = Mod(Mathf.FloorToInt(gx), freq);
            int y0 = Mod(Mathf.FloorToInt(gy), freq);
            float tx = gx - Mathf.Floor(gx);
            float ty = gy - Mathf.Floor(gy);
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            int x1 = (x0 + 1) % freq;
            int y1 = (y0 + 1) % freq;
            return Mathf.Lerp(
                Mathf.Lerp(lat[y0 * freq + x0], lat[y0 * freq + x1], tx),
                Mathf.Lerp(lat[y1 * freq + x0], lat[y1 * freq + x1], tx), ty);
        }

        /// <summary>多倍频值噪声（幅度减半、频率加倍）。</summary>
        static float Fbm(float u, float v, int freq0, int octaves, int seed)
        {
            float h = 0f, amp = 1f, total = 0f;
            int freq = freq0;
            for (int o = 0; o < octaves; o++)
            {
                h += ValueNoise(u * freq, v * freq, freq, seed + o) * amp;
                total += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return h / total;
        }

        /// <summary>脊化：1−|2u−1| 把值噪声的等值带折成沿脊线走的长连通结构。</summary>
        static float Ridged(float u, float v, int freq0, int octaves, int seed)
        {
            return 1f - Mathf.Abs(Fbm(u, v, freq0, octaves, seed) * 2f - 1f);
        }

        /// <summary>可平铺高斯模糊（x/y 两轴分离，圆化脊线让宽瓣沿脊连续点亮）。</summary>
        static void BlurWrap(float[] field, float sigma)
        {
            int r = Mathf.CeilToInt(sigma * 2.5f);
            var kernel = new float[r * 2 + 1];
            float sum = 0f;
            for (int i = -r; i <= r; i++)
            {
                float w = Mathf.Exp(-i * i / (2f * sigma * sigma));
                kernel[i + r] = w;
                sum += w;
            }
            for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

            var tmp = new float[field.Length];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float acc = 0f;
                    for (int i = -r; i <= r; i++)
                        acc += field[y * Size + (x + i & Size - 1)] * kernel[i + r];
                    tmp[y * Size + x] = acc;
                }
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float acc = 0f;
                    for (int i = -r; i <= r; i++)
                        acc += tmp[(y + i & Size - 1) * Size + x] * kernel[i + r];
                    field[y * Size + x] = acc;
                }
        }
    }
}
