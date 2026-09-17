using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>
    /// 水面模拟的纯公式层（纯 C#，无头可测）。
    ///
    /// 【定位】这是 <see cref="WaterWaveField2D"/>（二维波动方程高度场）里所有"单点可判定"的
    /// 数学规则：CFL 稳定域、海绵层阻尼、泡沫生成/衰减、域 UV 映射、格索引。
    /// 把它们抽出来是为了在 <c>external/m2-harness</c> 里无 Unity 运行时也能断言——
    /// 模拟数值一旦不稳（爆 / 不衰减 / 传播速度不对）是"画面慢慢变糊"，肉眼很难归因。
    ///
    /// 【参照思路来源，代码自写】HPWater 的水体数学（波动方程 + 吸收边界 + 泡沫场）。
    /// 本文件不包含任何第三方代码，公式为中心差分波动方程的标准写法。
    /// </summary>
    public static class WaterSimRules
    {
        /// <summary>
        /// 二维波动方程显式中心差分的 CFL 上限：<c>c·dt/dx ≤ 1/√2</c>。
        /// 本工程再乘安全系数 0.7 → ≤ <c>0.7/√2 ≈ 0.495</c>（协调者裁决口径）。
        /// </summary>
        public const float CflSafetyFactor = 0.7f;

        /// <summary>二维显式中心差分的理论稳定上限 <c>1/√2</c>。</summary>
        public const float CflTheoreticalLimit = 0.70710678f;

        /// <summary>本工程使用的 CFL 上限（含安全系数）。</summary>
        public const float CflLimit = CflSafetyFactor * CflTheoreticalLimit; // ≈ 0.495

        /// <summary>默认模拟域边长（世界单位）。128 覆盖 100×34 竞技场 + 两侧海床台阶
        /// （格 1→2 单位 ×2；格数不动 → 每格的格距在新口径下仍是 0.5 个*旧*格，即每格 0.5 世界单位 → 1.0）。</summary>
        public const float DefaultDomainSize = 128f;

        /// <summary>默认每轴格数；dx = 128/128 = 1.0 世界单位 = **半格**（与旧口径的"半格"一致，故格数不动）。</summary>
        public const int DefaultCellsPerAxis = 128;

        /// <summary>
        /// 世界地图模拟域相对地图跨度的收缩系数【提案/待定】：域边长 = 跨度 × 0.9。
        /// 取 0.9 而非 1.0 的依据：域边界是海绵吸收层 + 涌浪注入带（贴边约 10 格），
        /// 把边界收在图缘略内侧，让吸收/涌浪带落在玩家可见海域的边缘而非图外空白，
        /// 把固定的 128² 分辨率预算集中花在可见海面上；图缘之外是任务无关的远海，不需要涟漪细节。
        /// </summary>
        public const float WorldDomainSpanFactor = 0.9f;

        /// <summary>
        /// 世界地图模拟域边长下限【提案/待定】（= <see cref="DefaultDomainSize"/>）：
        /// 小跨度地图不缩域，保住旧竞技场调好的格距分辨率（128 格 / 128u = 每格 1u）
        /// 与波速行进观感——域再小，涟漪/涌浪会显得"局促"且编码对比度漂移。
        /// </summary>
        public const float MinWorldDomainSize = DefaultDomainSize;

        /// <summary>
        /// 世界地图模拟域边长上限【提案/待定】：格数固定 128 时 256u 对应 dx = 2.0u，
        /// 默认涟漪半径 7u 仍占约 3.5 格解析；域再大涟漪会糊成不可辨的钝斑。
        /// </summary>
        public const float MaxWorldDomainSize = 256f;

        /// <summary>
        /// 世界地图模式下的模拟域边长：随地图跨度等比伸缩（×<see cref="WorldDomainSpanFactor"/>），
        /// clamp 到 [<see cref="MinWorldDomainSize"/>, <see cref="MaxWorldDomainSize"/>]。
        /// 【稳定性】域扩大只会让格距 dx 变大 → 库朗数 C = c·dt/dx 变小，不会破坏 CFL 稳定域
        /// （驱动默认 c=18、dt=1/60 时 C ∈ [0.15, 0.30]，全部 ≤ <see cref="CflLimit"/>）。
        /// </summary>
        public static float WorldDomainSizeForSpan(float spanUnits)
        {
            return Mathf.Clamp(spanUnits * WorldDomainSpanFactor, MinWorldDomainSize, MaxWorldDomainSize);
        }

        /// <summary>库朗数 <c>C = c·dt/dx</c>。</summary>
        public static float CflNumber(float waveSpeed, float dt, float dx)
        {
            float denom = Mathf.Max(Mathf.Abs(dx), 1e-6f);
            return Mathf.Abs(waveSpeed) * Mathf.Max(dt, 0f) / denom;
        }

        /// <summary>该时间步是否落在稳定域内（<c>C ≤ _CflLimit</c>）。</summary>
        public static bool IsStable(float waveSpeed, float dt, float dx)
        {
            return CflNumber(waveSpeed, dt, dx) <= CflLimit;
        }

        /// <summary>给定波速与格距，允许的最大时间步长（含安全系数）。</summary>
        public static float MaxStableDt(float waveSpeed, float dx)
        {
            float c = Mathf.Max(Mathf.Abs(waveSpeed), 1e-6f);
            return CflLimit * Mathf.Max(Mathf.Abs(dx), 1e-6f) / c;
        }

        /// <summary>
        /// 海绵层（吸收边界）的每步速度阻尼系数 <c>σ ∈ [0, σ_max]</c>。
        /// 距边界 <paramref name="distanceToBorderCells"/> 格；&lt; spongeCells 才起作用，
        /// 且随距离平方增长（贴边最强、向内平滑趋 0，避免边界二次反射）。
        /// </summary>
        public static float SpongeSigma(int distanceToBorderCells, int spongeCells, float maxSigma)
        {
            if (spongeCells <= 0)
                return 0f;

            if (distanceToBorderCells >= spongeCells)
                return 0f;

            if (distanceToBorderCells < 0)
                distanceToBorderCells = 0;

            float t = 1f - (float)distanceToBorderCells / spongeCells; // 贴边 1 → 内缘 0
            float s = Mathf.Clamp01(maxSigma);
            return s * t * t;
        }

        /// <summary>
        /// 泡沫生成量：浪峰（曲率为正，即 <c>-∇²u</c> 越大）产生的泡沫越多；低于阈值不生成。
        /// 单调非减（曲率越大泡沫越多），且低于阈值恒为 0 —— 这是单元测试的判据。
        /// </summary>
        public static float FoamGeneration(float curvature, float threshold, float gain)
        {
            float over = curvature - Mathf.Max(threshold, 0f);
            if (over <= 0f)
                return 0f;
            return over * Mathf.Max(gain, 0f);
        }

        /// <summary>泡沫指数衰减一步：<c>f' = f · decay</c>（decay ∈ [0,1]）。</summary>
        public static float FoamDecayStep(float foam, float decayPerStep)
        {
            return Mathf.Max(foam, 0f) * Mathf.Clamp01(decayPerStep);
        }

        /// <summary>
        /// 世界 XZ → 模拟域 UV（[0,1]²）。域是以 <paramref name="centerXZ"/> 为中心、
        /// 边长 <paramref name="size"/> 的正方形。域外的点会超出 [0,1]（调用方自行 clamp/淡出）。
        /// </summary>
        public static Vector2 WorldToDomainUv(Vector2 worldXZ, Vector2 centerXZ, float size)
        {
            float s = Mathf.Max(size, 1e-4f);
            Vector2 rel = (worldXZ - centerXZ) / s;
            return new Vector2(rel.x + 0.5f, rel.y + 0.5f);
        }

        /// <summary>模拟域 UV → 世界 XZ（<see cref="WorldToDomainUv"/> 的逆）。</summary>
        public static Vector2 DomainUvToWorld(Vector2 uv, Vector2 centerXZ, float size)
        {
            return centerXZ + (uv - new Vector2(0.5f, 0.5f)) * Mathf.Max(size, 1e-4f);
        }

        /// <summary>域 UV → 连续格坐标（<c>cx ∈ [0, cellsX]</c>，可为小数；不含 clamp）。</summary>
        public static Vector2 UvToGrid(float u, float v, int cellsX, int cellsZ)
        {
            return new Vector2(u * cellsX, v * cellsZ);
        }

        /// <summary>格索引（行主序）；调用方保证坐标在界内。</summary>
        public static int GridIndex(int cx, int cz, int cellsX)
        {
            return cx + cz * cellsX;
        }

        /// <summary>格坐标是否在界内。</summary>
        public static bool InBounds(int cx, int cz, int cellsX, int cellsZ)
        {
            return cx >= 0 && cz >= 0 && cx < cellsX && cz < cellsZ;
        }
    }
}
