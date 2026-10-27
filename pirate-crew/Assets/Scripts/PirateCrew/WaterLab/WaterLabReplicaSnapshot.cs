using System;
using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// Sandcastle 主菜单地形/水场快照的运行时采样器（I7，ReplicaMode 的数据底座）。
    ///
    /// 【真值来源】frida 运行时只读 dump（Beach.Readback CPU 副本，256² Readback 层，
    /// dump3），产物 <c>Assets/Art/WaterLabReplica/MenuGround256.bytes</c>（groundHeight）与
    /// <c>MenuWater256.bytes</c>（waterAmount），各 256×256 float32 小端。规格与轴向判定见
    /// <c>external/sandcastle-ref/rt/reports/B-terrain.md</c>（§2 映射 / §4 轴向 / §5 导入）。
    ///
    /// 【坐标契约】世界域 XZ=[−15,+15]²（Beach.bounds size 30×10×30）；
    /// <c>idx_f = world·(1/dx) + (res/2 − 0.5)</c>（dx = 30/256 = 0.1171875，与原作
    /// scaleBias 逐项一致）；行主序 <c>i = row·256 + col</c>，<b>row = 世界 Z 且 row 0 =
    /// z −15（前景干滩）</b>、col = 世界 X 且 col 0 = x −15（B §4 四角投影 + 干沙质心 +
    /// 相机侧三重证据）。加载时做 B §5.2 的翻转自检：对角四角采样必须呈现
    /// 「(−,−) 干滩 / (+,+) 海底」+ 穿岸轴行均值差，失败即抛异常回退程序剖面。
    ///
    /// 【纯 C#】只吃 byte[]、吐 float，不碰 Unity 原生对象——harness 无头测试直接
    /// Load(.bytes 字节) 钉契约；Unity 侧由 <see cref="WaterLabSimulation"/> 的
    /// TextAsset 字段接线（装配器注入），ReplicaMode 判定 = 快照资产可加载。
    /// </summary>
    public static class WaterLabReplicaSnapshot
    {
        /// <summary>快照每轴格数（Readback 层分辨率）。</summary>
        public const int Resolution = 256;

        /// <summary>快照格距（m）= 30/256 = 0.1171875（原作 readback cellSize）。</summary>
        public const float CellSize = WaterLabRules.DomainSize / Resolution;

        /// <summary>world→浮点格的偏置（原作 scaleBias 的后两维 res/2−0.5）。</summary>
        public const float IndexBias = Resolution * 0.5f - 0.5f;

        const int BytesPerCell = 4;               // float32
        const int ExpectedBytes = Resolution * Resolution * BytesPerCell;

        // 干湿特征自检阈值（B §5.2：row0 角 ≈1.95 干滩 / row255 角 ≈0.03 海底；
        // 穿岸轴行均值差 ≈1.95，实测 rowMean 1.987→0.041）。均留足裕量只防"写反"。
        const float DryCornerMin = 1.2f;
        const float WetCornerMax = 0.6f;
        const float CrossShoreRowMeanDeltaMin = 1.0f;

        static float[] _ground;
        static float[] _water;

        // 加载时顺带算好的统计（对账 snapshot_meta.json 用）
        public static float GroundMin { get; private set; }
        public static float GroundMax { get; private set; }
        public static float GroundMean { get; private set; }
        public static float WaterMin { get; private set; }
        public static float WaterMax { get; private set; }
        public static float WaterMean { get; private set; }

        /// <summary>地形快照已加载。</summary>
        public static bool HasGround => _ground != null;

        /// <summary>水场快照已加载。</summary>
        public static bool HasWater => _water != null;

        /// <summary>ReplicaMode 判定：两个快照场都可用（水场+地形成对，缺一即回退）。</summary>
        public static bool HasData => HasGround && HasWater;

        /// <summary>原始场（只读约定；行主序 row=Z、col=X）。测试对账用。</summary>
        public static float[] GroundData => _ground;
        public static float[] WaterData => _water;

        /// <summary>
        /// 载入快照（两场成对）。字节序 float32 小端、长度必须恰为 256²×4；任何校验
        /// 失败（长度/四角翻转/NaN/水量为负）都抛异常且<b>不留下半加载状态</b>——
        /// 调用方（模拟/装配器）捕获后走程序剖面回退。
        /// </summary>
        public static void Load(byte[] groundBytes, byte[] waterBytes)
        {
            var ground = Decode(groundBytes, "groundHeight");
            var water = Decode(waterBytes, "waterAmount");

            // 水量语义：≥0 的水柱量（含结合水）。负值说明字节解码错位，宁可回退。
            (float wMin, float wMax, float wMean) = Stats(water);
            if (wMin < -1e-3f)
                throw new ArgumentException($"waterAmount 出现负值 min={wMin}，解码疑似错位");

            ValidateAxisOrientation(ground);

            _ground = ground;
            _water = water;
            (GroundMin, GroundMax, GroundMean) = Stats(ground);
            (WaterMin, WaterMax, WaterMean) = (wMin, wMax, wMean);
        }

        /// <summary>卸载快照（回退程序剖面的开关）。</summary>
        public static void Unload()
        {
            _ground = null;
            _water = null;
        }

        /// <summary>地形高程（世界 Y 原值，0.03–2.62 m）双线性采样，域外 clamp 常值延伸。</summary>
        public static float SampleGround(float worldX, float worldZ)
        {
            return SampleFromArray(_ground, worldX, worldZ);
        }

        /// <summary>水量（含结合水的水柱量，与 h 同语义）双线性采样，域外 clamp 常值延伸。</summary>
        public static float SampleWater(float worldX, float worldZ)
        {
            return SampleFromArray(_water, worldX, worldZ);
        }

        /// <summary>单行对账摘要（模拟 Awake 的 ReplicaMode 日志用）。</summary>
        public static string Describe()
        {
            return $"ground[{GroundMin:F3}..{GroundMax:F3} mean {GroundMean:F3}] "
                 + $"water[{WaterMin:F3}..{WaterMax:F3} mean {WaterMean:F3}]";
        }

        // ---- 内部 ----

        static float[] Decode(byte[] bytes, string field)
        {
            if (bytes == null || bytes.Length != ExpectedBytes)
                throw new ArgumentException(
                    $"{field} 快照长度 {(bytes?.Length ?? 0)} != 期望 {ExpectedBytes}（256² float32）");
            var data = new float[Resolution * Resolution];
            Buffer.BlockCopy(bytes, 0, data, 0, ExpectedBytes);
            for (int i = 0; i < data.Length; i++)
            {
                if (float.IsNaN(data[i]) || float.IsInfinity(data[i]))
                    throw new ArgumentException($"{field} 快照第 {i} 格出现 NaN/Inf");
            }
            return data;
        }

        /// <summary>
        /// B §5.2/§7-3 的翻转自检：四角干湿特征 + 穿岸轴守卫。row/col 写反时
        /// 「行均值」实际沿 col（沿岸轴，均值恒 ≈1.0）走，差值塌到 ~0.06，必被
        /// CrossShoreRowMeanDeltaMin 抓住；row 符号写反时对角干湿互换，被四角抓住。
        /// </summary>
        static void ValidateAxisOrientation(float[] ground)
        {
            float dryCorner = SampleFromArray(ground, -14.9f, -14.9f);
            float wetCorner = SampleFromArray(ground, +14.9f, +14.9f);
            if (dryCorner < DryCornerMin || wetCorner > WetCornerMax)
                throw new ArgumentException(
                    $"快照四角干湿特征不符（B §5.2）：TerrainHeight(−14.9,−14.9)={dryCorner:F3} "
                    + $"应 ≥{DryCornerMin}（干滩）、(+14.9,+14.9)={wetCorner:F3} 应 ≤{WetCornerMax}（海底）"
                    + "——疑似 row/col 或符号写反");

            float rowTop = BandMean(ground, 0, 4);
            float rowBottom = BandMean(ground, Resolution - 4, 4);
            if (rowTop - rowBottom < CrossShoreRowMeanDeltaMin)
                throw new ArgumentException(
                    $"快照穿岸轴守卫失败（B §4）：前 4 行均值 {rowTop:F3} − 末 4 行均值 {rowBottom:F3} "
                    + $"应 ≥{CrossShoreRowMeanDeltaMin}（row=世界 Z，row0=干滩）——疑似 row/col 写反");
        }

        /// <summary>若干整行的均值（守卫与统计用）。</summary>
        static float BandMean(float[] data, int rowStart, int rowCount)
        {
            float sum = 0f;
            for (int z = rowStart; z < rowStart + rowCount; z++)
            {
                int offset = z * Resolution;
                for (int x = 0; x < Resolution; x++)
                    sum += data[offset + x];
            }
            return sum / (rowCount * Resolution);
        }

        static (float min, float max, float mean) Stats(float[] data)
        {
            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i];
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
            }
            return (min, max, sum / data.Length);
        }

        /// <summary>
        /// 双线性采样（B §5.2 伪码逐式）：idx_f = world·(1/dx) + (res/2−0.5)，格心对齐 −0.5；
        /// 行主序 index = z·256 + x（<b>row 吃世界 Z</b>，不是 Unity Texture2D 的 x 行主序习惯）。
        /// dx=15/128 是二进制小数，格心处 world→idx 无舍入误差，采样恒等于原始格值。
        /// </summary>
        static float SampleFromArray(float[] data, float worldX, float worldZ)
        {
            float fx = worldX / CellSize + IndexBias;
            float fz = worldZ / CellSize + IndexBias;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, Resolution - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, Resolution - 2);
            float tx = Mathf.Clamp01(fx - x0);
            float tz = Mathf.Clamp01(fz - z0);
            int i = z0 * Resolution + x0;
            float g00 = data[i], g10 = data[i + 1];
            float g01 = data[i + Resolution], g11 = data[i + Resolution + 1];
            return Mathf.Lerp(Mathf.Lerp(g00, g10, tx), Mathf.Lerp(g01, g11, tx), tz);
        }
    }
}
