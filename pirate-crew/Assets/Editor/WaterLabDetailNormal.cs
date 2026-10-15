using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 实验水体细节法线贴图的程序化生成（WaterNormal2D 的等价物）。
    ///
    /// 【逆向依据】Sandcastle Lit/Water PS：碎金闪点 = pow(NdotH, 1500) 的近镜面瓣，
    /// 高频法线全部来自 WaterNormal2D（1024²，世界 XZ×0.05 / ×0.2 双尺度采样）。没有
    /// 贴图就没有供瓣的高频法线——正弦格架出的点是网格状排列（实拍多轮实证）。
    /// 本件用可平铺值噪声（四倍频，晶格 wrap）生成高度场，中心差分转切线法线，
    /// RG 存切线空间 xy（shader 端 ×2−1 解包，z 由 sqrt 重建）。
    /// </summary>
    public static class WaterLabDetailNormal
    {
        const int Size = 256;
        const float Texel = 1f / Size;

        /// <summary>生成可平铺细节法线贴图（RG=法线 xy 编码，B=高度，A=1）。</summary>
        public static Texture2D Create()
        {
            var height = new float[Size * Size];
            AccumulateOctave(height, 4, 1.0f);
            AccumulateOctave(height, 8, 0.50f);
            AccumulateOctave(height, 16, 0.25f);
            AccumulateOctave(height, 32, 0.125f);
            AccumulateOctave(height, 64, 0.0625f);
            AccumulateOctave(height, 128, 0.03125f);

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) // mipChain=true：远处用高层级抑制摩尔纹
            {
                name = "WaterLabDetailNormal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[Size * Size];
            const float step = 1f; // 噪声幅度尺度下的差分步长
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float hl = height[y * Size + (x - 1 & Size - 1)];
                    float hr = height[y * Size + (x + 1 & Size - 1)];
                    float hd = height[(y - 1 & Size - 1) * Size + x];
                    float hu = height[(y + 1 & Size - 1) * Size + x];
                    // 切线空间法线 xy（强度系数控制波纹陡峭度）
                    float nx = (hl - hr) / step;
                    float ny = (hd - hu) / step;
                    const float strength = 3.2f;
                    nx *= strength;
                    ny *= strength;
                    float len = Mathf.Sqrt(nx * nx + ny * ny + 1f);
                    var color = new Color(
                        nx / len * 0.5f + 0.5f,
                        ny / len * 0.5f + 0.5f,
                        height[y * Size + x] * 0.5f + 0.5f,
                        1f);
                    pixels[y * Size + x] = color;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true, false); // 更新 mip 链；保持可读：CreateAsset 序列化需要像素数据
            return tex;
        }

        /// <summary>叠加一层可平铺值噪声（freq = 每贴图晶格数，必须整除 Size 语义上任意）。</summary>
        static void AccumulateOctave(float[] field, int freq, float amplitude)
        {
            var lattice = new float[freq * freq];
            var rand = new System.Random(freq * 7919 + Size);
            for (int i = 0; i < lattice.Length; i++)
                lattice[i] = (float)rand.NextDouble();

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // 贴图坐标 [0,1) → 晶格坐标 [0,freq)，wrap 平铺
                    float gx = x / (float)Size * freq;
                    float gy = y / (float)Size * freq;
                    int x0 = Mathf.FloorToInt(gx) % freq;
                    int y0 = Mathf.FloorToInt(gy) % freq;
                    int x1 = (x0 + 1) % freq;
                    int y1 = (y0 + 1) % freq;
                    float tx = Smooth(gx - Mathf.Floor(gx));
                    float ty = Smooth(gy - Mathf.Floor(gy));
                    float v = Mathf.Lerp(
                        Mathf.Lerp(lattice[y0 * freq + x0], lattice[y0 * freq + x1], tx),
                        Mathf.Lerp(lattice[y1 * freq + x0], lattice[y1 * freq + x1], tx),
                        ty);
                    field[y * Size + x] += v * amplitude;
                }
            }
        }

        static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
