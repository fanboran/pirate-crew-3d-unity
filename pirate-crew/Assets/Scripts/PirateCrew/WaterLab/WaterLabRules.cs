using System;
using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// 实验水体（浅水方程海浪 + 湿沙 + 泡沫）的纯公式层（纯 C#，无头可测）。
    ///
    /// 【定位】与 <see cref="PirateCrew.Water.WaterSimRules"/> 同一层级：所有"单点可判定"的
    /// 数值规则与参数表在此单源，compute（<c>WaterLabSim.compute</c>）与 CPU 参考实现
    /// （<see cref="ReferenceSim"/>）都按这里的名目与公式落笔，<c>external/harness</c> 钉住
    /// CPU 参考的行为契约（质量守恒 / 孤立波速 / 无负水深），GPU 侧靠观感对齐。
    ///
    /// 【参照思路来源，代码自写】Sandcastle Demo 逆向（kernel 名与常量名见
    /// docs/技术/渲染/调研/Sandcastle水体逆向.md）：浅水方程格点求解、半拉格朗日回溯、
    /// 干湿阈值、双分辨率、湿沙渗透。本文件不含任何第三方代码。
    ///
    /// 【方程选择】质量方程用平均通量形式 ∂h/∂t = -∇·(h̄·v̄)（保留 h·v 非线性、天然守恒），
    /// 动量方程省略平流项 v·∇v 只留 -g∇h 与 kappa 时间低通——长波场景标准简化，破碎陡化
    /// 由浅化承担（水深变小 → 波速 √(gh) 变慢 → 波峰追上波谷）。原作每子步在
    /// HeightIntegration 之后另跑一个半拉格朗日 Advection（advectionFactor=0.2）对水量 h
    /// 做回溯平流——它是原作 surf zone 的主要数值耗散，本仓照抄（见 AdvectFoam）。
    /// 半拉格朗日不严格守恒，原作采访自述的「数值误差累积」即来自这条路；本仓照原作实现，
    /// harness 的质量守恒测试钉的是不含平流的纯通量步。
    /// </summary>
    public static class WaterLabRules
    {
        // ---- 域与分辨率（原作格距 5.86 cm；实验室域 64 m 为取景保留）----

        /// <summary>模拟域边长（世界单位，正方形，中心在原点）。实验室取景需要 64 m；
        /// 原作的 Normal/Huge 档是 30/60 m，但格距同为 5.86 cm（见 WaterCells）。</summary>
        public const float DomainSize = 64f;

        /// <summary>
        /// 水域每轴格数：64 m / 1092 = 5.861 cm，对齐原作 FormatProperty 的 5.86 cm 格距
        ///（60 m/1024²、30 m/512²、15 m/256² 三者同格距，reverse §8.1）。
        /// 1092 不是 8 的倍数，dispatch 必须向上取整（WaterLabSimulation.StepOnce）。
        /// </summary>
        public const int WaterCells = 1092;

        /// <summary>水域格距（m）。</summary>
        public const float WaterCellSize = DomainSize / WaterCells;

        /// <summary>地形每轴格数：原作 groundResolution 与 waterResolution 同档。</summary>
        public const int GroundCells = 1092;

        /// <summary>地形格距（m）。</summary>
        public const float GroundCellSize = DomainSize / GroundCells;

        // ---- 物理与数值参数 ----

        /// <summary>
        /// 重力加速度（m/s²）。原作场景实例真值 `gravityForce=16`（默认 10）。
        /// 原作的全部派生量都从这里来：`maxVelocity=0.5·√g`、子步 `dt=0.5·dx/√g`。
        /// </summary>
        public const float Gravity = 16f;

        /// <summary>
        /// 固定子步（s）= 原作 `MaxDeltaTime`：`0.5·cellSize/√gravityForce`
        /// （Water.Run 每子步下发，见逆向 §8）。60 m/1024² 下 ≈7.32 ms。
        /// </summary>
        public static readonly float FixedStep = 0.5f * WaterCellSize / Mathf.Sqrt(Gravity);

        /// <summary>SWE 显式格式的 CFL 安全上限（理论 1.0，取保守 0.5）。</summary>
        public const float CflLimit = 0.5f;

        /// <summary>结合水基准（m）：水面高度/通量/可见水深统一取 max(h − DryDepth, 0)；
        /// 前 5 cm 是贴沙结合水，不参与流动、也不显形（Sandcastle WATER_SATURATION_POINT 实测 0.05）。</summary>
        public const float DryDepth = 0.05f;

        /// <summary>
        /// 速度阻尼（1/s，线性阻力项）。原作 WaterProperty 四档预设全部 `damping=0`
        ///（Regular/Clear/Bahamas/Syrup，Refs 序列化真值），本仓照抄 0。
        /// </summary>
        public const float VelocityDamping = 0f;

        /// <summary>
        /// 速度限幅（m/s）。原作 `Water.MaxVelocity = 0.5·√gravityForce`（Water.cs 属性；
        /// 场景 g=16 → 2.0 m/s）。这不是防尖峰的工程上限，而是原作的真实输运上限：
        /// 涌潮速度被压在 2 m/s，配合 5.86 cm 格距，前置不会挤进一两格。
        /// </summary>
        public static readonly float MaxVelocity = 0.5f * Mathf.Sqrt(Gravity);

        /// <summary>
        /// 半拉格朗日水量平流系数（0..1）。原作 `WaterProperty.advectionFactor=0.2`
        ///（四档预设一致，Refs 真值）。每个子步按 `v·advectionFactor·dt` 回溯采样 h，
        /// 这是原作 surf zone 里把 bore 前缘摊开的主要数值耗散；本仓照抄。
        /// </summary>
        public const float AdvectionFactor = 0.2f;

        /// <summary>
        /// 动量时间平滑系数。原作 `WaterProperty.kappa=1000`（Regular/Clear/Bahamas；
        /// Syrup=2），VelocityIntegration 末尾按 `v=(v_new·kappa+v_old)/(kappa+4)` 轻度低通。
        /// </summary>
        public const float Kappa = 1000f;

        /// <summary>海平面高程（m，世界 Y）。地形与水面都以它为基准。</summary>
        public const float SeaLevel = 0f;

        // ---- 波源（原作 UpdateAmount：全场潮位松弛 + 每格耦合权重）----
        //
        // 【原作实证，Water compute blob 6/17 反汇编】UpdateAmount 每**帧**派发一次
        //（不在子步循环里），顺序：蒸发 → obstaclesMap 泵 → 两段潮位松弛。环境浪没有
        // 任何空间行波项：`waveAmplitude` 是标量，`WavesProperty` 四列预设只被 CPU 取
        // `max(第三分量)` 得到峰值 0.2 m（Water.Run：`saturate(playTime×0.05) × max(...)`，
        // 开盘 20 s 渐起，下限 0.01）。全场水面目标统一为
        // `level + clamp(waveAmplitude·sin(playTime), −0.5, +∞)`；每格按自身 4 bit 耦合
        // 权重 w/15 向目标松弛（每帧一次的比例，不乘 dt）。浪的不规律性来自地形浅化、
        // 岸线进退与泵，不来自多列正弦叠加——旧「四列行波」是本仓自推，已删除。

        /// <summary>潮位振荡振幅上限（m）。原作 Regular 档 WavesProperty 四列第三分量
        /// 0.1/0.15/0.2/0，`Water.Run` 取 max → 0.2 m。</summary>
        public const float WaveAmplitudeMax = 0.2f;

        /// <summary>振幅下限（m）：`Water.Run` 的 `max(0.01, …)`，开场也有 1 cm 微动。</summary>
        public const float WaveAmplitudeFloor = 0.01f;

        /// <summary>振幅渐起速率（1/s）：`saturate(playTime × 0.05)`，20 s 到满幅。</summary>
        public const float WaveAmplitudeRampPerSecond = 0.05f;

        /// <summary>潮位目标的下钳（m）：`clamp(waveAmplitude·sin(playTime), −0.5, +∞)`。</summary>
        public const float TideClampMin = -0.5f;

        /// <summary>潮位目标水量的下限（m）：`max(level − ground + wave, 0.05)`——
        /// 前 5 cm 结合水始终保留，与 <see cref="DryDepth"/> 同一语义。</summary>
        public const float TideMinAmount = 0.05f;

        /// <summary>4 bit 耦合权重的量程上限：原作松弛步长 = 权重/15。</summary>
        public const float CouplingWeightMax = 15f;

        /// <summary>
        /// 满耦合权重。原作每格 word 高 4 bit 是运行时耦合系数，由关卡 `water.Fill`
        /// 的初始水场写入；本仓拿不到该场的分布，改用**初始淹没区满耦合、初始干区零
        /// 耦合**的静态代理（C 级近似）：涨潮时外海严格跟随潮位，初始干滩不被直接钉住，
        /// 只能靠通量爬滩 → 保留 run-up/swash 的岸线进退。
        /// </summary>
        public const float CouplingWeightFull = CouplingWeightMax;

        /// <summary>水场蒸发系数（原作场景实例 `evaporationFactor=0.1`）。</summary>
        public const float EvaporationFactor = 0.1f;

        /// <summary>蒸发公式缩放（UpdateAmount 字面量 0.001）。</summary>
        public const float EvaporationScale = 0.001f;

        /// <summary>水汽衰减系数（UpdateAmount 字面量 0.721348）。</summary>
        public const float EvaporationHumidityDecay = 0.721348f;

        /// <summary>
        /// 水汽场取值。原作从 Air compute 的 22 bit 水汽场双线性采样；本仓不模拟气相，
        /// 取 Air Initialization 的初值 0（word1=0x800 → 采样字段 0），属 C 级近似。
        /// </summary>
        public const float AirHumidity = 0f;

        /// <summary>太阳辐照标量（原作 `sunColor.r`；实验室平行光强度 1）。</summary>
        public const float SunIrradiance = 1f;

        /// <summary>潮位振幅随时间渐起（m）。`max(0.01, saturate(t×0.05) × 0.2)`。</summary>
        public static float WaveAmplitude(float playTime)
        {
            float ramp = Mathf.Clamp01(playTime * WaveAmplitudeRampPerSecond);
            return Mathf.Max(WaveAmplitudeFloor, ramp * WaveAmplitudeMax);
        }

        /// <summary>
        /// 潮位目标的“水量面”（m）：`SeaLevel + clamp(amp·sin(playTime), −0.5, +∞)`。
        /// 它是**含 5 cm 结合水**的目标（amount 面），可见水面 = 该值 − <see cref="DryDepth"/>；
        /// 与 <see cref="TideDepthTarget"/> 配套使用，别把它当渲染出来的 η。
        /// </summary>
        public static float TideEta(float playTime)
        {
            float wave = WaveAmplitude(playTime) * Mathf.Sin(playTime);
            return SeaLevel + Mathf.Max(wave, TideClampMin);
        }

        /// <summary>潮位松弛的目标水量（m）：`max(level − ground + wave, 0.05)`。</summary>
        public static float TideDepthTarget(float playTime, float ground)
        {
            float wave = Mathf.Clamp(WaveAmplitude(playTime) * Mathf.Sin(playTime),
                TideClampMin, float.PositiveInfinity);
            return Mathf.Max(SeaLevel - ground + wave, TideMinAmount);
        }

        /// <summary>耦合权重（0..15）：初始淹没区满耦合，初始干区零耦合（C 级代理，见上）。</summary>
        public static float CouplingWeightAt(float ground)
        {
            return ground < SeaLevel ? CouplingWeightFull : 0f;
        }

        /// <summary>
        /// 蒸发速率（m/s）：`exp(−0.721348·湿度) × sun.r × evaporationFactor × 0.001`。
        /// 原作在 UpdateAmount 里先减蒸发再松弛；满耦合格随后被潮位拉回，故可见影响主要
        /// 落在未耦合的干/浅格。本仓按 Air 初值湿度 0、太阳辐照 1 计算。
        /// </summary>
        public static float EvaporationRate()
        {
            return Mathf.Exp(-EvaporationHumidityDecay * AirHumidity)
                 * SunIrradiance * EvaporationFactor * EvaporationScale;
        }

        // ---- 破碎与泡沫 ----

        /// <summary>
        /// 破碎判据（相对波高 H/h 的 Miche 准则常数 0.78 工程版，H = 2a）。
        /// 实测量取 <see cref="BreakingFroude"/>：线性浅水波质点最大速度 u = a·√(g/h)、
        /// 波速 c = √(g·h)，故 a/h = u/c = Fr，于是「H/h &gt; 0.78」等价于「Fr &gt; 0.39」。
        /// </summary>
        public const float BreakingSteepness = 0.78f;

        /// <summary>
        /// 破碎的弗劳德数阈值 = <see cref="BreakingSteepness"/>/2 = 0.39，即计划书 W1
        /// 「波高/水深 &gt; 0.78 **或** 质点速 &gt; 波速」两条的等价形式：线性浅水波
        /// u = a·√(g/h)、c = √(g·h) ⇒ a/h = Fr，而 H/h&gt;0.78（H = 2a）即 a/h &gt; 0.39。
        /// 【别再退回 η 的曲率判据】|η − 4邻均值|/h 是 2 格曲率量（λ=40 m 涌浪仅 ~1e-4，
        /// 够不着 0.78），且正是 Shapiro 网格滤波抑制的频带 → 泡沫恒 0、白水粒子恒 0
        ///（2026-10-18 实证：泡沫 max 0.082、drawList 全程 0）。
        /// 【也别取 1.0】本仓流态极少到超临界（实测 Fr&gt;1 时累计生成仅 417 颗、
        /// drawList 0–27 ≈ 空转）；破碎带的宽度由 0.39 + <see cref="FoamGain"/> 共同定。
        /// </summary>
        public const float BreakingFroude = BreakingSteepness * 0.5f;

        /// <summary>
        /// 泡沫生成增益（每步生成量 = 超出判据部分 × 此值 × dt）。
        /// 平衡值 ≈ 0.595·增益·(Fr − <see cref="BreakingFroude"/>)。
        /// 【一个场两个消费者，必须一起标】泡沫既画水面白度、又当白水粒子的生成源
        /// （阈值 <see cref="WhitewaterSpawnThreshold"/>）——只压增益会把粒子一起压没
        /// （2026-10-18 实测：增益 2.4→0.9 时累计生成从 49490 掉到 0）。
        /// 取 1.5：Fr≈1.5 处泡沫≈1.0（破碎核心纯白）、Fr≈1.0 处 ≈0.54（带内成梯度）。
        /// </summary>
        public const float FoamGain = 1.5f;

        /// <summary>泡沫每步衰减系数（保留比）。</summary>
        public const float FoamDecay = 0.972f;

        /// <summary>泡沫邻域扩散保留比（每步向 4 邻均摊的量）。</summary>
        public const float FoamDiffuse = 0.16f;

        // ---- 白水粒子（W4，对标原作 Whitewater：泡沫场实例化粒子，Update→Culling 写
        // drawList/args——本仓 Compact kernel 同构产出 draw list，End kernel 写 drawArgs）----

        /// <summary>粒子池容量（缓冲环：写游标回卷覆盖最老粒子）。</summary>
        public const int WhitewaterCapacity = 32768;

        /// <summary>粒子寿命（s）：白水生成后存续时长。</summary>
        public const float WhitewaterLifetime = 1.5f;

        /// <summary>
        /// 生成阈值：泡沫浓度超过此值的格才可能出粒。
        /// 【0.35 → 0.72 → 0.60】0.35 时整个浅水架都在吐粒子（泡沫场覆盖 56% 水面），
        /// r49 实测糊成密白点；0.72 只留破碎带中心、偏稀。0.55 在新取景里泡沫带偏厚，
        /// 0.60 留住了岸线白沫但收窄了带宽。
        /// </summary>
        public const float WhitewaterSpawnThreshold = 0.60f;

        /// <summary>生成产率系数：每格每步生成概率 = (泡沫−阈值) × 此值 × dt。</summary>
        public const float WhitewaterSpawnRate = 7f;

        /// <summary>粒子基准尺寸（m，个体在 ±40% 内随机）。原作 Lit_Whitewater VS
        /// 的尺寸式是 0.1×(1−exp(−5.77078×life))，故基准取 0.1。</summary>
        public const float WhitewaterSize = 0.1f;

        /// <summary>白水粒子的水面上抬量（m）：原作 VS 的 +0.02 字面量。
        /// 本仓粒子在世界空间，高度 = ground + max(h−DryDepth,0) + 该值。</summary>
        public const float WhitewaterSurfaceOffset = 0.02f;

        /// <summary>白水粒子最小可见水深（m）：低于此值粒子会搁在湿沙上，被读成残留
        /// 水沫；生成与存续都检查，与干滩渗透同一口径。</summary>
        public const float WhitewaterMinVisibleDepth = 0.02f;

        /// <summary>
        /// 视锥剔除外扩（m）：Compact kernel 对 6 平面做内侧判定时的容差，
        /// 防粒子在视口边缘突现突隐（原作 Whitewater.frustumOverscan = 0.1）。
        /// </summary>
        public const float WhitewaterFrustumOverscan = 0.1f;


        /// <summary>
        /// 破碎判据（弗劳德形式，与 AdvectFoam 同式）：Fr = |v| / √(g·max(h, 干湿阈值))
        /// 超过 <see cref="BreakingFroude"/> 即认为该格处于破碎（超临界涌潮/卷破）。
        /// </summary>
        public static bool IsBreaking(float speed, float depth)
        {
            float d = Mathf.Max(depth, DryDepth);
            return speed / Mathf.Sqrt(Gravity * d) > BreakingFroude;
        }

        // ---- 湿沙 ----

        /// <summary>湿沙饱和速率（1/s）：被水覆盖的格向 w=1 逼近。</summary>
        public const float WetnessSoakRate = 3f;

        /// <summary>湿沙蒸发速率（1/s）：无水覆盖后向 w=0 衰减（比浸润慢一个量级，退潮后湿痕留存）。</summary>
        public const float WetnessEvaporateRate = 0.15f;

        /// <summary>干滩薄水渗透阈值（m）：ground&gt;0 且可见水深低于此值时，水片按
        /// <see cref="BeachInfiltrationRate"/> 吸回 5 cm 结合水。原作沙层是孔隙介质，
        /// 本仓没有沙-水交换层，这是 C 级近似；深水/主水线（depth 远大于阈值）不受影响。</summary>
        public const float BeachInfiltrationDepth = 0.04f;

        /// <summary>干滩薄水渗透速率（1/s）：约 0.17 s 时间常数，退潮前沿的薄片在
        /// 半个潮周期内被吸走，同时保留水深 &gt;4 cm 的主水线。</summary>
        public const float BeachInfiltrationRate = 6f;

        /// <summary>湿沙邻域扩散保留比（湿痕向干沙摊开）。</summary>
        public const float WetnessDiffuse = 0.08f;

        // ---- 海绵层（域边吸收）----

        /// <summary>海绵层厚度（格，水域分辨率）。+x 岸侧不需要（地形自然吃波），三侧海边界用。</summary>
        public const int SpongeCells = 10;

        /// <summary>海绵层最大速度阻尼（1/s）。</summary>
        public const float SpongeMaxDamping = 8f;

        /// <summary>距边界格数 → 海绵阻尼（1/s），平方曲线沿 WaterSimRules.SpongeSigma 同式。</summary>
        public static float SpongeDamping(int distanceToBorderCells)
        {
            if (distanceToBorderCells >= SpongeCells)
                return 0f;
            float t = 1f - Mathf.Clamp(distanceToBorderCells, 0, SpongeCells) / (float)SpongeCells;
            return SpongeMaxDamping * t * t;
        }

        // ---- 地形剖面（程序化沙滩）----

        /// <summary>深水平台高程（m）。创始人观感裁决（2026-10-26，两轮）：先 −1.0 m「整片浅水」；对照原作主菜单实时画面
        /// （PrintWindow 抓帧）再放浅到 **−0.5 m**（原作水泽明显比 1.0 m 浅）。俯角 48°
        /// 光程 ≈1.4 m，落在参考中带 (74,153,161)～深绿松石 (46,126,146) 之间，保留深浅渐变。
        /// 【历史注】曾取 2.4 m 追参考头图的饱和宝蓝深带（1.3 m 台面在旧光/无兜底时代出图
        /// 只有 (62,136,92) 的绿、压不出宝蓝）；r92 起天光第二方向光 + GI 兜底就位后浅水
        /// 观感已足，创始人裁决浅水优先。C# 侧测试按符号取值，不钉具体数。</summary>
        public const float OffshoreDepth = -0.5f;

        /// <summary>缓坡浅水架末端（与滩面接缝）高程（m）：本场景地形设计值。
        /// 接缝两侧共用同一非零斜率（C1），不再用两段 Smooth01 在 x=6 造出零坡度平台。</summary>
        public const float ShelfEndHeight = -0.15f;

        /// <summary>缓坡浅水架起止（世界 X，域 [-30,30]）。</summary>
        /// <summary>缓坡浅水架起点（世界 X）。原值 -26 让深水带（光程 >2 m，超出 σ 定标
        /// 量程，出墨蓝）占画面水面约 65%，参考头图的构成是深水宝蓝**窄条**（约 1/3）+
        /// 大面积浅/中水绿松石（compare.py 实测浅+中带占水 30%）；收到 -14 后缓坡 20 m
        /// 爬 2.25 m（弦坡 11%），深水带压缩到取景顶部约 29%，offshore 的 -2.4 m 宝蓝
        /// 台面保留（当年 1.3 m 试过出不了宝蓝，见 OffshoreDepth 注释）。Fritsch–Carlson
        /// 单调条件 3×弦坡 = 0.34 仍覆盖接缝斜率 0.055。</summary>
        public const float ShelfStartX = -14f;
        public const float ShelfEndX = 6f;

        /// <summary>滩面段起止（世界 X）：接缝斜率与 shelf 相同（C1、非零），到 FaceEndX
        /// 缓平到干沙台。8 m 是本场景原取景的设计值，不随模拟参数改动。</summary>
        public const float FaceStartX = 6f;
        public const float FaceEndX = 14f;

        /// <summary>干沙滩平台高程（m）。</summary>
        public const float BeachHeight = 0.5f;

        /// <summary>剖面单调测试容差（m）。原作滩面来自 WorldHeightMap 高度场资产，
        /// 是光滑的大尺度地形；本仓不再叠加周期沙纹，容差只兜 value noise 的微小起伏。</summary>
        public const float TerrainMonotoneTolerance = 0.005f;

        /// <summary>smoothstep（带 clamp）。</summary>
        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>确定性整数哈希（0..1）。纯算术，harness 脱离 Unity 原生调用也能跑。</summary>
        static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        /// <summary>双线性平滑 value noise（0..1）。用噪声代替全场同相正弦，避免
        /// 沙面/岸线出现贯穿画面的等高条纹。</summary>
        static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float fx = x - xi;
            float fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash01(xi, yi);
            float b = Hash01(xi + 1, yi);
            float c = Hash01(xi, yi + 1);
            float d = Hash01(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>两倍频 value noise（互质缩放，压低格点感）。</summary>
        static float Fbm(float x, float y)
        {
            return ValueNoise(x, y) * 0.65f
                 + ValueNoise(x * 2.03f + 17.3f, y * 1.97f + 41.7f) * 0.35f;
        }

        /// <summary>
        /// 三次 Hermite 插值：h0/h1 端点高程，slope0/slope1 端点斜率（dh/dx，m/m），
        /// length 段长，u∈[0,1]。两端斜率非零才能让相邻两段的接缝保持 C1——
        /// smoothstep 两端斜率恒 0，两段各自用 smoothstep 会在接缝叠出一个平台。
        /// </summary>
        static float Hermite(float h0, float h1, float slope0, float slope1, float length, float u)
        {
            u = Mathf.Clamp01(u);
            float u2 = u * u;
            float u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * h0
                 + (u3 - 2f * u2 + u) * (slope0 * length)
                 + (-2f * u3 + 3f * u2) * h1
                 + (u3 - u2) * (slope1 * length);
        }

        /// <summary>
        /// 程序化沙滩剖面（世界 X → 高程）：深水台 → 缓坡浅水架 → 滩面 → 干沙台，
        /// 叠加沿 X/Z 的确定性沙纹。
        /// shelf→face 接缝用同一非零斜率（C1）——旧实现两段 Smooth01 在 x=6 的斜率
        /// 都是 0，形成一个平台：涌浪在那里堆高后塌成油膜级薄水，水面高度断崖被
        /// 渲染成硬边（r68 高度剖面 x=7.0→7.5 eta 掉 0.37 m）。
        /// </summary>
        public static float TerrainHeight(float worldX, float worldZ)
        {
            // 岸线沿 z 低频摆动：三组互质频率叠加（±2.0 m）。单频/双频摆动的波峰
            // 波谷仍会读成规则弧线，第三组短波把岸线打散成滩湾；摆幅小于浅水架宽度，
            // 不会把剖面结构打乱。
            float shoreWarp = Mathf.Sin(worldZ * 0.13f + 0.6f) * 1.15f
                            + Mathf.Sin(worldZ * 0.41f + 2.2f) * 0.55f
                            + Mathf.Sin(worldZ * 0.87f + 4.4f) * 0.32f;
            float x = worldX - shoreWarp;

            // 接缝斜率（m/m）：取在 shelf/face 两段平均坡度之下（Fritsch–Carlson 单调条件
            // 要求端点斜率 ≤ 3×弦坡），既保证单调爬升，又让涌浪过接缝时坡度不归零。
            const float junctionSlope = 0.055f;

            float h;
            if (x <= ShelfStartX)
            {
                h = OffshoreDepth;
            }
            else if (x <= ShelfEndX)
            {
                h = Hermite(OffshoreDepth, ShelfEndHeight, 0f, junctionSlope,
                            ShelfEndX - ShelfStartX,
                            (x - ShelfStartX) / (ShelfEndX - ShelfStartX));
            }
            else
            {
                h = Hermite(ShelfEndHeight, BeachHeight, junctionSlope, 0f,
                            FaceEndX - FaceStartX,
                            (x - FaceStartX) / (FaceEndX - FaceStartX));
            }

            // 后滨沙丘：脊线随 x 弯曲、脊高随 x/z 起伏。旧版只用 z 的正弦 → 沙丘是
            // 一条贯穿画面的横条；现在相位与幅值都带 x 项，外加一层二维噪声明暗。
            float duneMask = Smooth01((x - FaceEndX) / 8f);
            float dunePhaseA = worldZ * 0.31f + 1.1f + 0.85f * Mathf.Sin(worldX * 0.17f + 0.4f);
            float dunePhaseB = worldZ * 0.19f + 0.3f + 0.55f * Mathf.Sin(worldX * 0.11f + 2.1f);
            float dunes = (0.5f + 0.5f * Mathf.Sin(dunePhaseA)) * 0.24f
                        + (0.5f + 0.5f * Mathf.Sin(dunePhaseB)) * 0.16f
                        + (Fbm(worldX * 0.33f + 9.1f, worldZ * 0.29f + 3.7f) - 0.5f) * 0.14f;
            h += dunes * duneMask;

            // 湿滩到干滩的宽缓起伏：原作初始地形来自 WorldHeightMap 资产，岸线是
            // 分形曲线、滩面是光滑的大尺度地形，没有周期沙纹。这里只加 12–20 m
            // 波长的低幅起伏（±2.5 cm），让滩面不是一块斜板；不产生高频条纹。
            float broadMask = Smooth01((x - FaceStartX) / 4f)
                            * (1f - Smooth01((x - FaceEndX) / 3f));
            h += (Fbm(worldX * 0.055f + 21.7f, worldZ * 0.060f + 4.3f) - 0.5f) * 0.05f * broadMask;
            return h;
        }

        /// <summary>世界 X ∈ [-半域, +半域] → 地形格索引（含 clamp）。</summary>
        public static int GroundCellOf(float worldX)
        {
            float half = DomainSize * 0.5f;
            float u = Mathf.Clamp01((worldX + half) / DomainSize);
            return Mathf.Min((int)(u * GroundCells), GroundCells - 1);
        }

        /// <summary>地形格中心的世界坐标（单轴）。</summary>
        public static float GroundCellCenter(int cell)
        {
            return -DomainSize * 0.5f + (cell + 0.5f) * GroundCellSize;
        }

        // ---- 数值契约 ----

        /// <summary>SWE 库朗数 <c>C = √(g·h_max)·dt/dx</c>。</summary>
        public static float CflNumber(float maxDepth, float dt, float dx)
        {
            float c = Mathf.Sqrt(Mathf.Max(Gravity * Mathf.Max(maxDepth, 0f), 0f));
            return c * Mathf.Max(dt, 0f) / Mathf.Max(Mathf.Abs(dx), 1e-6f);
        }

        /// <summary>该参数组是否在稳定域内。</summary>
        public static bool IsStable(float maxDepth, float dt, float dx)
        {
            return CflNumber(maxDepth, dt, dx) <= CflLimit;
        }

        /// <summary>浅水波速 <c>√(g·h)</c>（h 为水深）。</summary>
        public static float WaveSpeed(float depth)
        {
            return Mathf.Sqrt(Gravity * Mathf.Max(depth, 0f));
        }

        /// <summary>格索引（行主序）。</summary>
        public static int GridIndex(int cx, int cz, int cellsX)
        {
            return cx + cz * cellsX;
        }

        /// <summary>格坐标是否在界内。</summary>
        public static bool InBounds(int cx, int cz, int cellsX, int cellsZ)
        {
            return cx >= 0 && cz >= 0 && cx < cellsX && cz < cellsZ;
        }

        /// <summary>
        /// 通量形式浅水一步的语义参考（CPU 版，harness 钉契约用；GPU compute 同公式）。
        /// 单步 = 质量通量 + 动量梯度，显式欧拉；边界为反射（诺伊曼），故封闭域质量守恒可测。
        /// 平流/波源/海绵不在本函数（它们不守恒或引入外部质量，测试单独绕开或单独断言界）。
        /// </summary>
        public static class ReferenceSim
        {
            /// <summary>一步质量+动量更新（在 h/vx/vz 上原地；foam/wet 由调用方推进）。</summary>
            public static void StepMassMomentum(float[] h, float[] vx, float[] vz, float[] ground,
                int cellsX, int cellsZ, float dx, float dt)
            {
                int n = h.Length;
                var hNew = new float[n];
                var vxNew = new float[n];
                var vzNew = new float[n];
                Array.Copy(h, hNew, n);
                Array.Copy(vx, vxNew, n);
                Array.Copy(vz, vzNew, n);

                for (int cz = 0; cz < cellsZ; cz++)
                {
                    for (int cx = 0; cx < cellsX; cx++)
                    {
                        int i = GridIndex(cx, cz, cellsX);
                        float hi = h[i];
                        // 结合水：前 5 cm 贴沙，不参与流动；η/通量都用 d = max(h−0.05,0)。
                        float di = Mathf.Max(hi - DryDepth, 0f);

                        // 4 邻（-1 = 出界＝反射墙）。墙界面上质量通量必须显式为 0：
                        // 「越界取自身」会让 F = d·v ≠ 0，边界格凭空流进流出，破坏守恒。
                        int l = cx > 0 ? GridIndex(cx - 1, cz, cellsX) : -1;
                        int r = cx < cellsX - 1 ? GridIndex(cx + 1, cz, cellsX) : -1;
                        int d = cz > 0 ? GridIndex(cx, cz - 1, cellsX) : -1;
                        int u = cz < cellsZ - 1 ? GridIndex(cx, cz + 1, cellsX) : -1;

                        float dL = l >= 0 ? Mathf.Max(h[l] - DryDepth, 0f) : 0f;
                        float dR = r >= 0 ? Mathf.Max(h[r] - DryDepth, 0f) : 0f;
                        float dD = d >= 0 ? Mathf.Max(h[d] - DryDepth, 0f) : 0f;
                        float dU = u >= 0 ? Mathf.Max(h[u] - DryDepth, 0f) : 0f;

                        float vL = l >= 0 ? (vx[l] + vx[i]) * 0.5f : 0f;
                        float vR = r >= 0 ? (vx[i] + vx[r]) * 0.5f : 0f;
                        float vD = d >= 0 ? (vz[d] + vz[i]) * 0.5f : 0f;
                        float vU = u >= 0 ? (vz[i] + vz[u]) * 0.5f : 0f;

                        // 平均通量（半格界面）：F = d̄·v̄。界面级迎风施主门——只有通量来向的
                        // 格子 d>0 才放行：湿格可把水推上干滩（run-up），干格不能凭平均速度
                        // 凭空吐水。两侧格对同一界面必须算出同一 F（界面格式守恒的关键）。
                        float fluxL = (l >= 0 && ((vL > 0f) ? dL : di) > 0f) ? (dL + di) * 0.5f * vL : 0f;
                        float fluxR = (r >= 0 && ((vR > 0f) ? di : dR) > 0f) ? (di + dR) * 0.5f * vR : 0f;
                        float fluxD = (d >= 0 && ((vD > 0f) ? dD : di) > 0f) ? (dD + di) * 0.5f * vD : 0f;
                        float fluxU = (u >= 0 && ((vU > 0f) ? di : dU) > 0f) ? (di + dU) * 0.5f * vU : 0f;

                        hNew[i] = hi - dt / dx * ((fluxR - fluxL) + (fluxU - fluxD));
                        if (hNew[i] < 0f) hNew[i] = 0f; // 数值保护：无负水深

                        // 动量：v += -dt·g·∇η（水面高程梯度，非水深梯度——干前滩这点很关键）。
                        // η = ground + max(h−DryDepth, 0)；出界侧 η 取自身（诺伊曼：法向梯度 0）。
                        float eta0 = ground[i] + di;
                        if (hNew[i] > DryDepth)
                        {
                            float etaL = (l >= 0) ? ground[l] + dL : eta0;
                            float etaR = (r >= 0) ? ground[r] + dR : eta0;
                            float etaD = (d >= 0) ? ground[d] + dD : eta0;
                            float etaU = (u >= 0) ? ground[u] + dU : eta0;
                            float sx = (etaR - etaL) / (2f * dx);
                            float sz = (etaU - etaD) / (2f * dx);
                            vxNew[i] = vx[i] - dt * (Gravity * sx + VelocityDamping * vx[i]);
                            vzNew[i] = vz[i] - dt * (Gravity * sz + VelocityDamping * vz[i]);
                            // 原作 kappa 时间低通（VelocityIntegration 末段，kappa=1000）：
                            // v = (v_new·kappa + v_old)/(kappa + 4)。
                            vxNew[i] = (vxNew[i] * Kappa + vx[i]) / (Kappa + 4f);
                            vzNew[i] = (vzNew[i] * Kappa + vz[i]) / (Kappa + 4f);
                            // 限幅
                            float sp = Mathf.Sqrt(vxNew[i] * vxNew[i] + vzNew[i] * vzNew[i]);
                            if (sp > MaxVelocity)
                            {
                                float k = MaxVelocity / sp;
                                vxNew[i] *= k;
                                vzNew[i] *= k;
                            }
                        }
                        else
                        {
                            vxNew[i] = 0f;
                            vzNew[i] = 0f;
                        }
                    }
                }

                Array.Copy(hNew, h, n);
                Array.Copy(vxNew, vx, n);
                Array.Copy(vzNew, vz, n);
            }
        }
    }
}
