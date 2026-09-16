using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>
    /// 大海域海面（<see cref="OceanRig"/> + PirateOcean.shader）发布的全局 shader 变量名。
    /// 刻意与 <see cref="WaterSimulationDriver"/> 的全局（涟漪注入）分离：后者照旧由驱动发布，
    /// 新 shader 两套都读——驱动不存在时涟漪项自动跳过（<c>_WaterSimEnabled</c> 兜底），无需任何适配。
    /// </summary>
    public static class OceanGlobals
    {
        /// <summary>(竞技场中心X, 中心Z, 近岸保护半径, 全涌半径)——白帽/长涌包络用。</summary>
        public const string ArenaCenter = "_OceanArenaCenter";

        /// <summary>(网格中心X, 网格中心Z, 0, 0)——shader 据此算"距网格中心的半径"，镜像网格细分梯子。</summary>
        public const string GridCenter = "_OceanGridCenter";
    }

    /// <summary>
    /// 大海域海面的波表与观感规则（纯 C#，无头可测）。
    ///
    /// 【与 HLSL 的关系】PirateOcean.shader 内嵌**同一组公式**（HLSL 无法无头验证）；
    /// 本类是参考实现，改一边必须同步改另一边——沿袭 <see cref="WaterRules"/> 与旧 PirateWater 的双源纪律。
    ///
    /// 【M4 尺度契约】docs/M4-大海域世界化.md §1/§5：水面 y=-0.4（<see cref="LevelGeometry.WaterSurfaceY"/>），
    /// 岛顶最低 +0.5、湿沙带 -0.5~-0.2；海面域近场细分 + 远场裙边 ≥ 4000u。
    ///
    /// 【波峰硬约束（用户裁决，2026-09-17）】竞技场附近波峰最高点必须 &lt; -0.10（不穿岛基湿沙带）。
    /// 换算：-0.10 − 水面(-0.4) = 近岸可用振幅预算 0.30；现有 4 波 chop 振幅合计 0.278 →
    /// 近岸波峰 -0.122 &lt; -0.10 ✓。长涌（振幅 0.5~1.2，远超预算）**只允许出现在外海**——
    /// 由 <see cref="SwellEnvelope"/> 在竞技场近岸保护圈内把长涌振幅压到 0，
    /// 保护圈半径覆盖全部 M4 地图跨度（≤280u）后在外海平滑升到 1。
    /// 外海没有岛/角色，"茫茫大海"的全振幅涌浪在那里才是想要的。
    /// </summary>
    public static class OceanRules
    {
        // ------------------------------------------------------------------
        // 波峰高度契约（Tests/Water 断言的硬约束）
        // ------------------------------------------------------------------

        /// <summary>竞技场附近波峰允许的最高世界 Y（用户裁决：不穿岛基湿沙带）。</summary>
        public const float MaxCrestWorldY = -0.10f;

        /// <summary>近岸振幅预算 = 允许最高波峰 − 静水面 = -0.10 − (-0.4) = 0.30。</summary>
        public const float ShoreAmplitudeBudget = MaxCrestWorldY - LevelGeometry.WaterSurfaceY; // 0.30

        // ------------------------------------------------------------------
        // 长涌（swell）：两组大波长波，外海观感的主载体【AI 提案：波长/振幅在任务书 60-120u / 0.5-1.2 内取值】
        // ------------------------------------------------------------------

        /// <summary>
        /// 默认两条长涌（PirateOcean.shader 的 <c>_S1…_S2</c> Properties 默认值与此一一对应）。
        /// Σ Q·A·k ≈ 0.083（见 <see cref="WaterRules.SumHorizontalFactor"/> 对全浪表的合计），不自交。
        /// 波速按深水色散（与 chop 同口径）：120u 涌 ~3.5 u/s，72u 涌 ~4.7 u/s，慢而厚重。
        /// </summary>
        public static readonly WaterWave[] DefaultSwellWaves =
        {
            new WaterWave(new Vector2(1.00f, 0.15f), 120.0f, 1.10f, 0.75f, 1.00f),
            new WaterWave(new Vector2(0.55f, 1.00f), 72.0f, 0.65f, 0.70f, 1.05f),
        };

        /// <summary>
        /// 全浪表 = 长涌×2 + 现有 4 波 chop（<see cref="WaterRules.DefaultWaves"/>，振幅合计 0.278）。
        /// shader 顶点位移与解析法线吃这张表；近岸（包络=0）时实际位移只剩 chop。
        /// </summary>
        public static WaterWave[] DefaultOceanWaves()
        {
            WaterWave[] chop = WaterRules.DefaultWaves;
            var all = new WaterWave[DefaultSwellWaves.Length + chop.Length];
            for (int i = 0; i < DefaultSwellWaves.Length; i++)
                all[i] = DefaultSwellWaves[i];
            for (int i = 0; i < chop.Length; i++)
                all[DefaultSwellWaves.Length + i] = chop[i];
            return all;
        }

        /// <summary>长涌振幅合计（外海全涌时的额外波高）。</summary>
        public static float SwellAmplitudeSum()
        {
            return WaterRules.MaxAmplitude(DefaultSwellWaves);
        }

        // ------------------------------------------------------------------
        // 长涌近岸包络：保护圈 0 → 外海 1（smoothstep）
        // ------------------------------------------------------------------

        /// <summary>默认竞技场半径（半对角，世界单位）。覆盖 M4 最大跨度 280u（半对角 ≈198）+ 余量。</summary>
        public const float DefaultArenaRadius = 200f;

        /// <summary>保护圈外扩余量（世界单位）：岛缘/湿沙带外再留一圈缓冲。</summary>
        public const float ShorePadding = 24f;

        /// <summary>包络爬坡宽度（世界单位）：保护圈外从 0 升到 1 的距离。</summary>
        public const float SwellRampWidth = 260f;

        /// <summary>近岸保护半径：圈内长涌振幅 = 0（波峰契约在这里生效）。</summary>
        public static float ProtectRadius(float arenaRadius)
        {
            return Mathf.Max(arenaRadius, 0f) + ShorePadding;
        }

        /// <summary>全涌半径：圈外长涌振幅 = 1（茫茫大海）。</summary>
        public static float FullSwellRadius(float arenaRadius)
        {
            return ProtectRadius(arenaRadius) + SwellRampWidth;
        }

        /// <summary>HLSL smoothstep(e0,e1,x) 的 C# 镜像（两边公式必须一致）。</summary>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-5f));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 长涌包络 ∈ [0,1]：距竞技场中心 ≤ 保护半径时 0（近岸无长涌 → 波峰契约），
        /// ≥ 全涌半径时 1（外海全涌），中间 smoothstep 平滑爬坡。
        /// </summary>
        public static float SwellEnvelope(float distanceToArenaCenter, float protectRadius, float fullSwellRadius)
        {
            return SmoothStep(protectRadius, fullSwellRadius, distanceToArenaCenter);
        }

        // ------------------------------------------------------------------
        // 波的几何解析淡出（网格密度 LOD）：粗网格处淡出"解析不了"的短波几何位移
        // ------------------------------------------------------------------

        /// <summary>几何解析一条波所需的最少顶点数（每波长）。</summary>
        public const int VertsPerWaveMin = 3;

        /// <summary>
        /// 单条波的几何位移淡出 ∈ [0,1]：每波长 ≥ <see cref="VertsPerWaveMin"/> 顶点（旧
        /// <see cref="WaterMeshRules.IsSamplingAdequate"/> 的达标线）→ 完整位移；
        /// 跌破 2.1 顶点/波 → 0，防欠采样短波在远处" boiling "。
        /// 与 PirateOcean.shader 的 <c>OceanGeometricFade</c> 同式；**法线不受此淡出影响**
        /// （逐像素解析法线与网格无关，明暗始终是完整波场）。
        /// cellSize 来自 <see cref="OceanGridRules.RingWidthAtRadius"/>。
        /// </summary>
        public static float WaveGeometricFade(float wavelength, float cellSize)
        {
            float ratio = Mathf.Max(wavelength, WaterRules.MinWavelength) / (VertsPerWaveMin * Mathf.Max(cellSize, 1e-4f));
            return Mathf.Clamp01((ratio - 0.7f) / 0.3f);
        }

        // ------------------------------------------------------------------
        // 远场位移淡出（地平线要求：远场顶点位移衰减到 0，海天线干净不闪）
        // ------------------------------------------------------------------

        /// <summary>位移淡出起点（距相机，世界单位）。</summary>
        public const float DisplaceFadeStart = 2000f;

        /// <summary>位移淡出终点（距相机）：之外的水面是纯法线扰动的平片。</summary>
        public const float DisplaceFadeEnd = 3000f;

        /// <summary>顶点位移的远场淡出 ∈ [1→0]（乘在全部波的振幅上）。</summary>
        public static float DisplacementFarFade(float distanceToCamera)
        {
            return 1f - SmoothStep(DisplaceFadeStart, DisplaceFadeEnd, distanceToCamera);
        }

        // ------------------------------------------------------------------
        // 波峰白帽（陡度阈值触发 + 世界空间噪声碎化边缘）
        // ------------------------------------------------------------------

        /// <summary>白帽触发的雅可比阈值：J 低于它 = 波面水平压缩过陡（卷波），出白帽。</summary>
        public const float WhitecapJacobianThreshold = 0.85f;

        /// <summary>白帽软区宽度：J 在 [阈值-软区, 阈值] 内线性爬升。</summary>
        public const float WhitecapSoftness = 0.15f;

        /// <summary>包络=0（近岸，只剩 chop）时的白帽保留比例：外海白帽为主，近岸零星。</summary>
        public const float WhitecapShoreScale = 0.35f;

        /// <summary>
        /// 陡度项 ∈ [0,1]：<c>saturate((阈值 − J) / 软区)</c>。J ≥ 阈值 → 0；J ≤ 阈值−软区 → 1。
        /// 对 J 单调不增（越陡越白）。
        /// </summary>
        public static float WhitecapSteepnessTerm(float jacobian)
        {
            return Mathf.Clamp01((WhitecapJacobianThreshold - jacobian) / WhitecapSoftness);
        }

        /// <summary>波峰项 ∈ [0,1]：只在高处出白帽（heightNorm = 波高/振幅和 ∈ [-1,1]，上部 65% 线性爬升）。</summary>
        public static float WhitecapCrestTerm(float heightNorm)
        {
            return Mathf.Clamp01((heightNorm - 0.35f) / 0.4f);
        }

        /// <summary>噪声碎化增益 ∈ [0.55, 1.45]：世界空间噪声把白帽边缘打碎（对 noise 单调不减）。</summary>
        public static float WhitecapNoiseGain(float noise01)
        {
            return 0.55f + Mathf.Clamp01(noise01) * 0.9f;
        }

        /// <summary>
        /// 白帽合成掩码 ∈ [0,1] = 陡度 × 波峰 × 噪声碎化 × 近岸降权。
        /// 断言契约：J 达标（不陡）恒 0；波高不够恒 0；各因子单调；结果有界。
        /// </summary>
        public static float WhitecapMask(float jacobian, float heightNorm, float noise01, float swellEnvelope)
        {
            float steep = WhitecapSteepnessTerm(jacobian);
            if (steep <= 0f)
                return 0f;

            float crest = WhitecapCrestTerm(heightNorm);
            if (crest <= 0f)
                return 0f;

            float shore = Mathf.Lerp(WhitecapShoreScale, 1f, Mathf.Clamp01(swellEnvelope));
            float mask = steep * crest * WhitecapNoiseGain(noise01) * shore;
            return Mathf.Clamp01(mask);
        }

        // ------------------------------------------------------------------
        // 高频细节法线的远场淡出（远处 FBM 法线会闪成噪点，靠 Gerstner 法线 + 太阳光路即可）
        // ------------------------------------------------------------------

        /// <summary>细节法线淡出起点（距相机，世界单位）。</summary>
        public const float DetailFarFadeStart = 600f;

        /// <summary>细节法线淡出终点。</summary>
        public const float DetailFarFadeEnd = 1600f;

        /// <summary>细节法线（FBM 两层）的远场保留比例 ∈ [1→0]。</summary>
        public static float DetailFarFade(float distanceToCamera)
        {
            return 1f - SmoothStep(DetailFarFadeStart, DetailFarFadeEnd, distanceToCamera);
        }

        // ------------------------------------------------------------------
        // 地平线融合（雾色 #B0D4F1，见美术风格指南 §2.1/品控 Q-12）
        // ------------------------------------------------------------------

        /// <summary>地平线融合起点（距相机，世界单位）。</summary>
        public const float HorizonFadeStart = 2600f;

        /// <summary>地平线融合终点：之外水面完全等于雾色、alpha=1（海天线干净）。</summary>
        public const float HorizonFadeEnd = 3900f;

        /// <summary>地平线融合比例 ∈ [0→1]（颜色向雾色混合、alpha 向 1 混合）。</summary>
        public static float HorizonFade(float distanceToCamera)
        {
            return SmoothStep(HorizonFadeStart, HorizonFadeEnd, distanceToCamera);
        }
    }
}
