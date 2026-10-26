using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 实验水体细节法线贴图的程序化生成（WaterNormal2D 的等价物）。
    ///
    /// 【逆向依据】Sandcastle Lit/Water PS：碎金闪点 = pow(NdotH, 1500) 的近镜面瓣，
    /// 高频法线全部来自 WaterNormal2D（1024²）。采样机制见
    /// WaterLabOcean.shader 细节法线段（2026-10-26 反汇编复核）。
    ///
    /// 【生成法（2026-10-26 按同口径自相关剖面重定谱）】原作贴图本体抽出实拍后
    /// 量测 lag 剖面（[−1,1] 域 R 通道，1024²）：lag1/4/8/16/32/64/128 =
    /// 0.983/0.845/0.722/0.595/0.456/0.226/−0.063——宽谱低频主导的长相关场。
    /// 旧 F1−F2 Voronoi 胞脊谱（lag4 即崩到 0.30、lag8 转负）与原作差一个量级，
    /// 碎斑高频被 pow1500 放大成「整面碎金」，是碎金过密的贴图侧根源。
    /// 新谱 = F1 平滑距离锥 ×2 层（长程相关）+ Gauss 鼓包 ×4 层（中程结构），
    /// Python 复现拟合 rms=0.121：0.994/0.951/0.889/0.765/0.512/0.089/−0.171。
    /// 幅度标定（编码域 R std 33/255）与原作解包域 0.256/2 同口径，不变。
    /// </summary>
    public static class WaterLabDetailNormal
    {
        const int Size = 512;

        /// <summary>R 通道编码 std 目标（原作 WaterNormal2D 实测 33/255 编码域）。</summary>
        const float TargetChannelStd = 33f / 255f;

        /// <summary>生成可平铺细节法线贴图（RG=法线 xy 编码，B=高度，A=1）。</summary>
        public static Texture2D Create()
        {
            var rand = new System.Random(20261015);
            var height = new float[Size * Size];
            AccumulateCellLayer(height, 2, 0.90f, CellShape.Cone, rand);    // F1 锥：长程相关主波
            AccumulateCellLayer(height, 3, 0.30f, CellShape.Cone, rand);
            AccumulateCellLayer(height, 6, 0.50f, CellShape.Bump, rand);    // Gauss 鼓包：中程结构
            AccumulateCellLayer(height, 8, 0.45f, CellShape.Bump, rand);
            AccumulateCellLayer(height, 11, 0.35f, CellShape.Bump, rand);
            AccumulateCellLayer(height, 15, 0.20f, CellShape.Bump, rand);

            // 归一化到零均值单位差，中心差分转切线法线
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

        /// <summary>胞形状：Cone = 最近点距离 √f1（平滑锥，梯度朝点汇聚——宽谱长程相关，
        /// 拟合原作 lag32+ 的缓慢单调衰减）；Bump = exp(−4d²) 鼓包（中程高斯胞结构）。</summary>
        enum CellShape { Cone, Bump }

        /// <summary>叠加一层随机点集胞场（距离按 period 周期化取最短，保证可平铺）。</summary>
        static void AddCellLayer(float[] field, float[] px, float[] py, float period,
            float amplitude, CellShape shape)
        {
            for (int y = 0; y < Size; y++)
            {
                float gy = y / (float)Size * period;
                for (int x = 0; x < Size; x++)
                {
                    float gx = x / (float)Size * period;
                    float best = 0f, f1 = float.MaxValue;
                    if (shape == CellShape.Cone)
                    {
                        for (int i = 0; i < px.Length; i++)
                        {
                            float ddx = gx - px[i];
                            ddx -= Mathf.Round(ddx / period) * period;
                            float ddy = gy - py[i];
                            ddy -= Mathf.Round(ddy / period) * period;
                            float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                            if (d < f1) f1 = d;
                        }
                        field[y * Size + x] += f1 * amplitude;
                    }
                    else
                    {
                        for (int i = 0; i < px.Length; i++)
                        {
                            float ddx = gx - px[i];
                            ddx -= Mathf.Round(ddx / period) * period;
                            float ddy = gy - py[i];
                            ddy -= Mathf.Round(ddy / period) * period;
                            float g = Mathf.Exp(-(ddx * ddx + ddy * ddy) * 4f);
                            if (g > best) best = g;
                        }
                        field[y * Size + x] += best * amplitude;
                    }
                }
            }
        }

        /// <summary>均匀随机点集（cellsPerSide² 个点）+ 指定形状叠加。</summary>
        static void AccumulateCellLayer(float[] field, int cellsPerSide, float amplitude,
            CellShape shape, System.Random rand)
        {
            int n = cellsPerSide * cellsPerSide;
            var px = new float[n];
            var py = new float[n];
            for (int i = 0; i < n; i++)
            {
                px[i] = (float)rand.NextDouble() * cellsPerSide;
                py[i] = (float)rand.NextDouble() * cellsPerSide;
            }
            AddCellLayer(field, px, py, cellsPerSide, amplitude, shape);
        }
    }
}
