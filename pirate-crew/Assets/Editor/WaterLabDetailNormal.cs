using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 实验水体细节法线贴图的程序化生成（WaterNormal2D 的等价物）。
    ///
    /// 【逆向依据】Sandcastle Lit/Water PS：碎金闪点 = pow(NdotH, 1500) 的近镜面瓣，
    /// 高频法线全部来自 WaterNormal2D（1024²，世界 XZ×0.05 / ×0.2 双尺度采样）。
    ///
    /// 【生成法（第十三轮终版，第十四轮回退定案）】原作贴图本体已抽出实拍
    /// （UnityPy，external/sandcastle-ref/）：有机「鹅卵石」胞、胞界成脊、
    /// R/G std≈33/27。候选四版对照：平滑值噪声（孤立碎点）、平滑鼓包核（鹅卵石群）、
    /// 域扭曲+脊状 fbm（r17-r23 在役——长丝形态对但整片海面皱成「保鲜膜」，
    /// 创始人实证「R17 开始就不对」）、**F1−F2 Voronoi 有机胞（本版，r13-r16 在役）**——
    /// 主倍频泊松随机点集（一胞一点的抖动网格拓扑永远是变形方格）、细倍频抖动网格；
    /// 生成后按 R 通道 std 自标定到原作同统计。不拷原作资产，只对齐统计特征与形态。
    /// </summary>
    public static class WaterLabDetailNormal
    {
        const int Size = 512;

        /// <summary>R 通道编码 std 目标（原作 WaterNormal2D 实测 33/255）。</summary>
        const float TargetChannelStd = 33f / 255f;

        /// <summary>生成可平铺细节法线贴图（RG=法线 xy 编码，B=高度，A=1）。</summary>
        public static Texture2D Create()
        {
            var rand = new System.Random(20261015);
            var height = new float[Size * Size];
            AccumulateOrganic(height, 6, 0.30f, rand);   // 大胞起伏（成组感）
            AccumulateOrganic(height, 16, 1.0f, rand);   // 主胞（原作主导尺度 ~64px/1024）
            AccumulateGrid(height, 23, 0.35f, rand);     // 胞内褶皱（非谐波频率防与主格架成拍）
            AccumulateGrid(height, 38, 0.15f, rand);     // 细鳞

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

        /// <summary>叠加一层 F1−F2 Voronoi 胞脊高度场（points 为平铺坐标内的点集，
        /// 距离按 period 周期化取最短，保证可平铺；h=F1−F2 在胞界（F1≈F2）成脊）。</summary>
        static void AddCellLayer(float[] field, float[] px, float[] py, float period, float amplitude)
        {
            for (int y = 0; y < Size; y++)
            {
                float gy = y / (float)Size * period;
                for (int x = 0; x < Size; x++)
                {
                    float gx = x / (float)Size * period;
                    float f1 = float.MaxValue, f2 = float.MaxValue;
                    for (int i = 0; i < px.Length; i++)
                    {
                        float ddx = gx - px[i];
                        ddx -= Mathf.Round(ddx / period) * period;
                        float ddy = gy - py[i];
                        ddy -= Mathf.Round(ddy / period) * period;
                        float d = ddx * ddx + ddy * ddy;
                        if (d < f1) { f2 = f1; f1 = d; }
                        else if (d < f2) { f2 = d; }
                    }
                    field[y * Size + x] += (Mathf.Sqrt(f1) - Mathf.Sqrt(f2)) * amplitude;
                }
            }
        }

        /// <summary>泊松随机点集（cellsPerSide² 个均匀点）——有机胞与大小变化的来源。</summary>
        static void AccumulateOrganic(float[] field, int cellsPerSide, float amplitude, System.Random rand)
        {
            int n = cellsPerSide * cellsPerSide;
            var px = new float[n];
            var py = new float[n];
            for (int i = 0; i < n; i++)
            {
                px[i] = (float)rand.NextDouble() * cellsPerSide;
                py[i] = (float)rand.NextDouble() * cellsPerSide;
            }
            AddCellLayer(field, px, py, cellsPerSide, amplitude);
        }

        /// <summary>抖动网格点集（一胞一点，抖动可越胞界）——细尺度褶皱用，
        /// 小尺度上残余格架感不可见。</summary>
        static void AccumulateGrid(float[] field, int freq, float amplitude, System.Random rand)
        {
            int n = freq * freq;
            var px = new float[n];
            var py = new float[n];
            for (int i = 0; i < n; i++)
            {
                px[i] = (i % freq) + Mathf.Lerp(-0.05f, 1.05f, (float)rand.NextDouble());
                py[i] = (i / freq) + Mathf.Lerp(-0.05f, 1.05f, (float)rand.NextDouble());
            }
            AddCellLayer(field, px, py, freq, amplitude);
        }
    }
}
