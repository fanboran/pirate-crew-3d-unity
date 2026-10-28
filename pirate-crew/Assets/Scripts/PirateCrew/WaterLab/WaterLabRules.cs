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
        // ---- 域与分辨率（裁决①，I7）：采用原作世界坐标——主菜单沙盘域 30 m
        //（Beach.bounds size 30×10×30，XZ=[−15,+15]²，rt/reports/B-terrain.md §2），
        // 三层分辨率与原作逐层对齐（Ground 1024² / Water 512²），格距由此自动落回
        // FormatProperty 的 5.86 cm（水）与 2.93 cm（地）。快照烘焙与 ReplicaMode 见
        // WaterLabReplicaSnapshot。 ----

        /// <summary>模拟域边长（世界单位，正方形，中心在原点）。原作主菜单沙盘
        /// 30 m（Beach.bounds），快照只覆盖这个域。</summary>
        public const float DomainSize = 30f;

        /// <summary>
        /// 水域每轴格数：30 m / 512 = 5.86 cm，与原作 FormatProperty 的 5.86 cm 格距一致
        ///（原作 Normal 档 = 30 m/512²，reverse §8.1 与 B-terrain §2 layers.water）。
        /// </summary>
        public const int WaterCells = 512;

        /// <summary>水域格距（m）。</summary>
        public const float WaterCellSize = DomainSize / WaterCells;

        /// <summary>地形每轴格数：原作 groundResolution = 1024²（GPU 权威层，B-terrain §2）。</summary>
        public const int GroundCells = 1024;

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
        /// （Water.Run 每子步下发，见逆向 §8）。30 m/512² 下 ≈7.32 ms。
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

        /// <summary>海平面高程（m，世界 Y）。原作 Water.level=1.0（frida 运行时真值 +
        /// snapshot_meta.json layers.water.level）。地形与水面都以它为基准。</summary>
        public const float SeaLevel = 1f;

        // ---- 波源（原作 UpdateAmount：桨带潮位松弛 + 逐格 4bit 耦合权重）----
        //
        // 【原作实证，Water compute blob 6/17 反汇编 + B2 运行时双验证】UpdateAmount 每
        // **帧**派发一次（不在子步循环里），顺序：蒸发 → obstaclesMap 泵 → 两段潮位松弛。
        // 环境浪没有任何空间行波项：`waveAmplitude` 是标量，`WavesProperty` 四列预设只被
        // CPU 取 `max(第三分量)` 得到峰值 0.2 m（Water.Run：`saturate(playTime×0.05) ×
        // max(...)`，开盘 20 s 渐起，下限 0.01）。潮位目标
        // `max(level − floor + clamp(waveAmplitude·sin(playTime), −0.5, +∞), 0.05)`，
        // 每格按自身 4 bit 耦合权重 w/15 松弛：`h += (w/15)·(target−h)`（每帧一次的
        // 比例，不乘 dt）。
        // 【B2 定案（权重场/playTime）】权重 = 水格 dword3 bits28..31，仅 Paint/Fill 写、
        // 主菜单实测 **96.9% ≡ 0**：只背边缘 row 496..511（Z∈[+14.06,+15]）有
        // [1..8,8..15]/15 ramp = 造波桨带。海区是自由 SWE 场、不被潮位目标钉住；浪的
        // 不规律性来自地形浅化、岸线进退与泵，不来自多列正弦叠加——旧「四列行波」与本仓
        // 曾用的「初始淹没满耦代理」都已删除（代理仅保留作非复刻装配的权重数据源）。
        // `floor` = BlurFloor 场（floorBlurIterations=3，菜单真值），非原始地面。
        // playTime = Game.playTime（模拟时钟，Game.cs:855/1045/1504；与壁钟漂移 4–7%，
        // 对相必须按 playTime 值，见 WaterLabReplicaWeights.PlayTimeOrigin）。

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
        /// BlurFloor 迭代次数。原作 `Water.floorBlurIterations`，主菜单运行时真值 3
        ///（frida 直读 Game.Instance.configuration，B2 §6）；潮位目标的 floor 项按此平滑。
        /// </summary>
        public const int FloorBlurIterations = 3;

        /// <summary>
        /// 满耦合权重（4 bit 上限）。原作每格 word 高 4 bit 是静态耦合权重（B2 定案：
        /// 主菜单只桨带 row 496..511 有 1..15 ramp，真值场见
        /// <see cref="WaterLabReplicaWeights"/>）；本常量只服务
        /// <see cref="CouplingWeightAt"/> 回退代理场（权重资产缺失时的装配回退）。
        /// </summary>
        public const float CouplingWeightFull = CouplingWeightMax;

        /// <summary>水场蒸发系数（原作场景实例 `evaporationFactor=0.1`）。</summary>
        public const float EvaporationFactor = 0.1f;

        /// <summary>蒸发公式缩放（UpdateAmount 字面量 0.001）。</summary>
        public const float EvaporationScale = 0.001f;

        /// <summary>水汽衰减系数（UpdateAmount 字面量 0.721348）。</summary>
        public const float EvaporationHumidityDecay = 0.721348f;

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

        /// <summary>
        /// 潮位松弛的目标水量（m）：`max(level − floor + wave, 0.05)`。
        /// <paramref name="floor"/> 是 BlurFloor 平滑场（见 <see cref="BlurFloor"/>），
        /// 不是原始地面——反汇编 UpdateAmount 的减项来自 floor 打包位。
        /// </summary>
        public static float TideDepthTarget(float playTime, float floor)
        {
            float wave = Mathf.Clamp(WaveAmplitude(playTime) * Mathf.Sin(playTime),
                TideClampMin, float.PositiveInfinity);
            return Mathf.Max(SeaLevel - floor + wave, TideMinAmount);
        }

        /// <summary>
        /// 耦合权重（0..15）的**回退代理场**：初始淹没区满耦、初始干区零耦。
        /// 【已退役】B2 定案原作权重场是静态桨带 ramp（见 <see cref="WaterLabReplicaWeights"/>），
        /// 复刻路径逐格读真值场；本函数只作为权重资产缺失/加载失败时的回退数据源
        ///（WaterLabSimulation 装配 _CouplingWeights 缓冲时调用），不再是复刻语义。
        /// </summary>
        public static float CouplingWeightAt(float ground)
        {
            return ground < SeaLevel ? CouplingWeightFull : 0f;
        }

        /// <summary>
        /// 原作 BlurFloor 的 CPU 参考（近似式，B2 §7.3 裁定）：对 floor 场（水格分辨率）
        /// 做 <paramref name="iterations"/> 次 4 邻均值盒式模糊，clamp 边界（反汇编
        /// BlurFloor 的四邻 ±1、imax/imin clamp 同构，无中心项）；菜单
        /// floorBlurIterations=3（frida 运行时真值）。原作把 groundHeight 按水格格心
        /// 双线性采进水格（UpdateFloor）后跑本模糊——调用方先用
        /// <see cref="TerrainHeight"/> 采样出 floor 场再传入。
        /// 【打包尺度缺口（I1 TODO 同坑）】反汇编里 BlurFloor 按 Σ4邻×1e-6×415277.5625
        /// 解码/回编码，而 UpdateFloor 的打包是 sat(m×0.09901)×16777215——乘数链对不上
        /// Σ4/4 平均（应 250000），疑打包位两侧尺度不自洽；floor 不进 readback、L1 差分
        /// 不可直接验证，按 B2 §7.3 以「groundHeight 3 次盒式模糊」近似（桨带下方地形
        /// 平坦，模糊 ≈ 原值，近似误差对目标项影响集中在 16 行桨带）。
        /// </summary>
        public static void BlurFloor(float[] floor, int cells, int iterations)
        {
            if (iterations <= 0 || floor.Length < cells * cells)
                return;
            var scratch = new float[floor.Length];
            for (int it = 0; it < iterations; it++)
            {
                for (int z = 0; z < cells; z++)
                {
                    int zm = z > 0 ? z - 1 : 0;
                    int zp = z < cells - 1 ? z + 1 : cells - 1;
                    int row = z * cells;
                    int rowM = zm * cells;
                    int rowP = zp * cells;
                    for (int x = 0; x < cells; x++)
                    {
                        int xm = x > 0 ? x - 1 : 0;
                        int xp = x < cells - 1 ? x + 1 : cells - 1;
                        scratch[row + x] = (floor[rowM + x] + floor[rowP + x]
                                          + floor[row + xm] + floor[row + xp]) * 0.25f;
                    }
                }
                Array.Copy(scratch, floor, floor.Length);
            }
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

        // ---- 活沙 Ground（D 流件1：Sandcastle Ground compute 语义移植）----
        //
        // 【真值链】反编译 Ground.cs（5 kernel 名/字段名/子步式）+ Ground@*.hlslasm 逐段
        //（shaderblobs/render/，两组编译变体逐对相同，以 @8652=VelocityIntegration、
        // @600=HeightIntegration、@17600=UpdateTangent、@20296=UpdateObstacleHeight、
        // @148=Initialization 为准）+ 沉积参数七型预设真值（D-sand.md §4.2：Refs 裸字节
        // 恢复，主菜单=SandType.Carbonate type 0，七型共用 13 个模拟浮点）。
        //
        // 【移植口径】公式语义级（与水侧 W1 同路线）：公式/参数/门控逐条对齐反汇编；
        // 原作的 24bit/22bit/12bit 定点打包只是 GPU 存储格式，我方用明文 float4 场
        //（Ground@600 store 段的 sat(v×0.09901)×16777215 等编码式不入模拟语义）。
        // 反汇编中的死通道（第二平流场恒 −4：`mad r, r, l(0), l(-4)` 字面量乘 0 横贯
        // VelocityIntegration 的水速采样与 HeightIntegration 的邻居速度差分）按原样
        // 保留语义＝不产生平流/搬运——活沙的地形演化主体是**坡度滑移扩散**，不是速度场
        // 平流，这与 B2 实测（22 s 全场漂移 ≈0.018 m 的慢形态演化）同数量级自洽。
        //
        // 【GroundCell 打包已知位段（未完全复原，遗留 D 报告）】d0[0..7]=tangent.x、
        // d0[8..15]=tangent.y（8bit (n+1)/2×255）、d1[0..11]=obstacleHeight（12bit
        // sat(v×0.09901)×4095，UpdateObstacleHeight 写入）、高度/沙量 24bit
        // sat(v×0.09901)×16777215、sediment 22bit ×1e-6（d3[4..25]）、
        // sandVelocity 22bit ×1e-6 sat×0.2（量程 0.839 m/s）。

        /// <summary>活沙重力（m/s²）。Ground.cs 字段默认 0.1；主菜单场景实例值未在
        /// B2 dump 里（Ground 层只读了分辨率/格距），按类默认值落，登记 D 报告遗留。</summary>
        public const float GroundGravity = 0.1f;

        /// <summary>活沙子步（s）= 原作 Ground.MaxDeltaTime：`0.5·groundCellSize/√g`
        ///（Ground.Run 的 Step("simulation", …) 固定步，30 m/1024² 下 ≈46.3 ms——
        /// 比水子步粗 6.3 倍：Ground.Run 平均每 3 帧走一步）。</summary>
        public static readonly float GroundFixedStep =
            0.5f * GroundCellSize / Mathf.Sqrt(GroundGravity);

        /// <summary>活沙速度上限（m/s）= √GroundGravity（Ground.Run 的 maxVelocity 下发；
        /// 反汇编限幅 `movc` 分支把 v 归一到 ×maxVelocity/|v|，Ground@8652 末段）。</summary>
        public static readonly float GroundMaxVelocity = Mathf.Sqrt(GroundGravity);

        /// <summary>滑移/侵蚀噪声幅度（Ground.cs 字段默认 noiseFactor=0.0001；Wang hash
        /// ±1 × 该值 × dt，Ground@8652 噪声段）。</summary>
        public const float GroundNoiseFactor = 0.0001f;

        // ---- 沉积参数（SandProperty.Parameters 的 13 个模拟浮点；D-sand.md §4.2，
        //      七型预设共用真值，Ref s 裸字节恢复。Ground.Run 逐个 SetFloat 下发）----

        /// <summary>沙面速度阻尼（1/s）。Ground@8652：`v −= v×damping×dt`。</summary>
        public const float SandDamping = 7f;

        /// <summary>粘滞系数（Ground.cs 字段名单内；反汇编未见独立消费点，登记遗留）。</summary>
        public const float SandStickiness = 0.5f;

        /// <summary>高度松弛低通（Ground@600：`h=(h'×amountKappa+h)/(amountKappa+4)`，
        /// 与水侧 kappa 低通同式）。</summary>
        public const float SandAmountKappa = 200f;

        /// <summary>速度松弛低通（Ground@8652：`v=(v'×velocityKappa+v)/(velocityKappa+4)`）。</summary>
        public const float SandVelocityKappa = 200f;

        /// <summary>水侵蚀权重基值（Ground@8652：`wet = wEF×(sat(water×20)×19+1)`，
        /// 量程 [wEF, 20×wEF]——水深 5 cm 即饱和到 20 倍滑移）。</summary>
        public const float SandWaterErosionFactor = 0.04f;

        /// <summary>水侵蚀量指数（Ground@8652：`pow(sat(water−0.05), gamma)`，
        /// log/exp 对实现；=1 时恒等）。</summary>
        public const float SandWaterErosionAmountGamma = 1f;

        /// <summary>搬运容量系数（Ground@600 容量式乘子：capacity =
        /// sedimentCapacity × sat(water−0.05)^waterErosionAmountGamma × |v|^speedGamma）。</summary>
        public const float SandSedimentCapacity = 0.003f;

        /// <summary>容量式水速指数（Ground@600：`pow(|v|, gamma)`，log/exp 对）。</summary>
        public const float SandSedimentWaterSpeedGamma = 1f;

        /// <summary>容量式水量指数（Ground@600 同族 pow；=1 恒等）。</summary>
        public const float SandSedimentWaterAmountGamma = 1f;

        /// <summary>悬移率（Ground@600/8652 悬移段乘子：sediment &lt; capacity 时
        /// sand→sediment 的转移速率）。</summary>
        public const float SandSedimentSuspension = 0.015f;

        /// <summary>沉积率（Ground@600：sediment &gt; capacity 时 sediment→sand 的
        /// 转移速率）。</summary>
        public const float SandSedimentDeposition = 0.005f;

        /// <summary>悬沙平流系数（Ground@8652：sediment 场沿速度场的回溯采样比例；
        /// 原作速度通道是死通道 → 实际自采样，我方保留常数与公式结构）。</summary>
        public const float SandSedimentAdvection = 0.5f;

        /// <summary>悬沙松弛低通（Ground@600：sediment 的 kappa 族低通）。</summary>
        public const float SandSedimentKappa = 10f;

        /// <summary>
        /// 湿度侵蚀权重（Ground@8652 反汇编直译）：`wet = wEF×(sat(water×20)×19+1)`。
        /// water 是该格的可见水量（含结合水的整柱量，与水场 h 同语义）；
        /// water≥0.05 m 即权重饱和（20×wEF）。
        /// </summary>
        public static float SandWetnessWeight(float water)
        {
            return SandWaterErosionFactor * (Mathf.Clamp01(water * 20f) * 19f + 1f);
        }

        /// <summary>
        /// 泵速率（m/s，原作 Water.UpdateAmount 泵段，§9 逐式）：门控
        /// `sandSurface &lt; obstacleTop && obstacleTop ≤ η &lt; obstacleTop + 0.1`
        /// 满足时 `amount += 0.15·sin(8·playTime + 0.0378·uv.y)·dt`。
        /// uv.y 是水格归一化 v（跨全域相位只差 0.0378 rad——近均匀局部扰动）。
        /// 【场语义（D 定案）】obstacleTop = groundHeight 快照原值（含障碍绝对高程，
        /// 原作 obstaclesMap 由顶视渲染障碍 mesh 写世界 Y）；sandSurface = groundHeight −
        /// 障碍增量（=sandAmount 快照，纯沙面）；η = 沙面 + max(h−0.05,0)。
        /// </summary>
        public const float PumpAmplitude = 0.15f;
        public const float PumpTimeFrequency = 8f;
        public const float PumpPhasePerV = 0.0378f;
        public const float PumpEtaWindow = 0.1f;

        /// <summary>泵的正弦速率（带门控结果的完整式；uvY ∈ [0,1] 归一化）。</summary>
        public static float PumpRate(float playTime, float uvY)
        {
            return PumpAmplitude * Mathf.Sin(PumpTimeFrequency * playTime + PumpPhasePerV * uvY);
        }

        /// <summary>泵门控（见 <see cref="PumpRate"/> 注释的场语义）。</summary>
        public static bool PumpGate(float sandSurface, float obstacleTop, float eta)
        {
            return sandSurface < obstacleTop
                && obstacleTop <= eta && eta < obstacleTop + PumpEtaWindow;
        }

        // ---- Air 层（D 流件3）----
        //
        // 【原作实证（D 定案）】Air.Run 是空方法（cs-full/Air.cs：只读 Initialized 后
        // 直接返回，无任何 Dispatch）；Air 的 HeightIntegration/VelocityIntegration 是
        // 空 kernel（Air@492/712：dcl 后仅 ret）。主菜单里 Air 只在 Init 派发一次
        // Initialization（Air@144：`u0 = (0x00200000, 0x800, 0, 0)` 常量初值），此后
        // airCells 恒为初值——水汽场 ≡ 0。Water.UpdateAmount 从 airCells 双线性采水汽
        // （22bit @ byte4 bits12..33）喂蒸发式，采到的恒 0。
        //
        // 【X 流 exp2 语义修正】蒸发式的 0.721348 在 DXBC 是 exp2 指数（exp2(x)=2^x），
        // 语义 = e^(−0.721348·ln2·湿度) = e^(−0.5·湿度)——旧实现用自然 exp 直接乘
        // 0.721348（X-exp2.md 登记的潜伏雷）。湿度恒 0 时两者同为 1（数值免疫），
        // 仍按原作 exp2 语义修正。

        /// <summary>Air 场每轴格数：原作 Air.Setup 的 `resolution = formatProperty >> 4`
        ///（Normal 档水 512² → 大气 32²，格距 30/32 ≈ 0.94 m）。</summary>
        public const int AirCells = WaterCells / 16;

        /// <summary>Air 格距（m）。</summary>
        public const float AirCellSize = DomainSize / AirCells;

        /// <summary>
        /// 水汽场取值。原作 airCells 的 22bit 水汽双线性采样；Air 层在主菜单恒为
        /// Initialization 初值 0（见段注释），本常量即该场的全部取值——Air compute 的
        /// Initialization kernel 灌这个值，Water 侧逐格采样（值恒定、结构对齐原作）。
        /// </summary>
        public const float AirHumidity = 0f;

        /// <summary>
        /// 蒸发速率（m/s）：`exp2(−0.721348·湿度) × sun.r × evaporationFactor × 0.001`。
        /// exp2 语义 = e^(−0.5·湿度)（X 流修正，见段注释）；湿度恒 0 → 恒 1e-4 m/s 量级
        /// ×0.1 因子。原作在 UpdateAmount 里先减蒸发再泵再松弛。
        /// </summary>
        public static float EvaporationRate()
        {
            // exp2(−0.721348·h) = 2^(−0.721348·h) = e^(−0.721348·ln2·h)（0.721348·ln2=0.5）
            float extinction = -EvaporationHumidityDecay * 0.6931472f * AirHumidity;
            return Mathf.Exp(extinction)
                 * SunIrradiance * EvaporationFactor * EvaporationScale;
        }

        /// <summary>蒸发基率（湿度无关部分）= sun.r × evaporationFactor × 0.001。
        /// compute 侧的湿度因子 exp2(−0.721348·水汽采样) 在 kernel 内逐格算（Air 场驱动），
        /// CPU 参考与本函数配套（湿度恒 0 时 <see cref="EvaporationRate"/> 与本值相等）。</summary>
        public static float EvaporationBaseRate()
        {
            return SunIrradiance * EvaporationFactor * EvaporationScale;
        }

        // ---- 夜光浮游生物（D 流件4：Phytoplankton compute + Lit_Phytoplankton）----
        //
        // 【第一手依据】rt/out/Phytoplankton/（本流现反汇编，game path_id 643）+
        // cs-full/Phytoplankton.cs（Culling 基类 32768 池/双视锥/DrawProceduralIndirect）+
        // X-exp2.md §3（Lit_Phytoplankton VS @2450：位移 0.25×(1−e^(−3·life))，exp2 语义）。

        /// <summary>粒子池容量（Culling 基类默认 capacity=32768）。</summary>
        public const int PhytoCapacity = 32768;

        /// <summary>死亡标记/重生寿命（Update @0：life &lt; 0.01 死、重生 0.01）。</summary>
        public const float PhytoLifeThreshold = 0.01f;

        /// <summary>寿命衰减速率（1/s，Update @0 的 `life − dt×0.1`）。</summary>
        public const float PhytoDecayRate = 0.1f;

        /// <summary>剔除 alpha 门系数（Culling @0 第一手：`1 − exp(−4.328085·life) ≥ 0.08`，
        /// 自然 exp——与渲染 VS 的 exp2 语义 e^(−3·life) 不同效，两处各自直译）。</summary>
        public const float PhytoCullK = 4.328085f;

        /// <summary>剔除 alpha 门限（Culling @0 的 0.08）。</summary>
        public const float PhytoCullAlphaMin = 0.08f;

        /// <summary>重生位置散布半径系数（Update @0：哈希 ∈[−1.5,1.5]×域相关系数）。</summary>
        public const float PhytoSpawnSpread = 1.5f;

        /// <summary>漂移系数（Update @0：位移 += 场×该值×dt×0.5；原作 cb0[152].z=deltaTime
        /// 兼职乘子，本仓下发同名语义，数值取 1——场自身量纲已含 m/s）。</summary>
        public const float PhytoDrift = 1f;

        /// <summary>
        /// 昼夜相位（原作 dayCycler.Night）。主菜单固定 Morning（t=1/3、timelapse=0）
        /// → Night=0；Phytoplankton.Draw/Render 要求 Night ≥ 0.5 才绘制（且强度
        /// sat(Night−0.5)×2）——主菜单里系统在跑、画面恒无。实验室固定时刻同值。
        /// </summary>
        public const float PhytoNightAmount = 0f;

        /// <summary>浮游生物是否绘制（原作 Draw/Render 的门控）。</summary>
        public static bool PhytoVisible => PhytoNightAmount >= 0.5f;

        /// <summary>绘制强度（原作 `sat(Night−0.5)×2` 下发材质 intensity）。</summary>
        public static float PhytoIntensity => Mathf.Clamp01((PhytoNightAmount - 0.5f) * 2f);

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

        // ---- 地形剖面（程序化沙滩，ReplicaMode 关闭时的回退；开启时 TerrainHeight
        // 委托 WaterLabReplicaSnapshot 采样主菜单快照）----

        /// <summary>
        /// 旧 64 m 取景剖面 → 30 m 域的 X 缩放系数。域切 30 m 后把旧 C1 剖面的
        /// 岸线里程碑按同一比例压缩（shoreWarp/沙丘/起伏公式原样保留），
        /// 使回退剖面与 ReplicaMode 共用同一个 30 m 世界。
        /// </summary>
        public const float LegacyProfileScale = DomainSize / 64f;

        /// <summary>深水平台高程（m，世界 Y）。旧取景的观感定标是**深度**（海平面下
        /// 0.5 m），海平面切到 1.0 后按海平面相对量平移保形：水深分布与 r97 定标逐点一致。</summary>
        public const float OffshoreDepth = SeaLevel - 0.5f;

        /// <summary>缓坡浅水架末端（与滩面接缝）高程（m）：海平面下 0.15 m（同上保形）。</summary>
        public const float ShelfEndHeight = SeaLevel - 0.15f;

        /// <summary>缓坡浅水架起止（世界 X）：旧 64 m 取景设计值（−14/6）按域比例压缩，
        /// Fritsch–Carlson 单调条件随弦坡放大自动仍成立（3×弦坡 0.24 ≫ 接缝斜率 0.055）。</summary>
        public const float ShelfStartX = -14f * LegacyProfileScale;
        public const float ShelfEndX = 6f * LegacyProfileScale;

        /// <summary>滩面段起止（世界 X）：接缝斜率与 shelf 相同（C1、非零），到 FaceEndX
        /// 缓平到干沙台。与 ShelfEndX 共享同一比例缩放。</summary>
        public const float FaceStartX = 6f * LegacyProfileScale;
        public const float FaceEndX = 14f * LegacyProfileScale;

        /// <summary>干沙滩平台高程（m）：海平面上 0.5 m（同上保形）。</summary>
        public const float BeachHeight = SeaLevel + 0.5f;

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
        /// 地形高度（世界 XZ → 世界 Y）。ReplicaMode（主菜单快照已加载）下委托
        /// <see cref="WaterLabReplicaSnapshot.SampleGround"/> 双线性采样（groundHeight 世界
        /// Y 原值，含烘焙障碍物/海里礁石——裁决②原样导入）；未加载时回退下方程序剖面。
        /// </summary>
        public static float TerrainHeight(float worldX, float worldZ)
        {
            if (WaterLabReplicaSnapshot.HasGround)
                return WaterLabReplicaSnapshot.SampleGround(worldX, worldZ);
            return ProceduralTerrainHeight(worldX, worldZ);
        }

        /// <summary>
        /// 程序化沙滩剖面（世界 X → 高程）：深水台 → 缓坡浅水架 → 滩面 → 干沙台，
        /// 叠加沿 X/Z 的确定性沙纹。里程碑按 <see cref="LegacyProfileScale"/> 压缩、
        /// 高程按海平面相对量保形（见各常量注释）。
        /// shelf→face 接缝用同一非零斜率（C1）——旧实现两段 Smooth01 在接缝的斜率
        /// 都是 0，形成一个平台：涌浪在那里堆高后塌成油膜级薄水，水面高度断崖被
        /// 渲染成硬边（r68 高度剖面 x=7.0→7.5 eta 掉 0.37 m）。
        /// </summary>
        static float ProceduralTerrainHeight(float worldX, float worldZ)
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
                StepMassMomentum(h, vx, vz, ground, cellsX, cellsZ, dx, dt,
                    new float[n], new float[n], new float[n]);
            }

            /// <summary>
            /// 带调用方 scratch 的重载（L1 差分恒等测试用；public 因 Tests 是独立程序集，
            /// internal 跨程序集不可见会报 CS1501）：512² 场按 7.32 ms 子步步进
            /// 22 s 级要跑三千多子步，公共重载每步 new 三片 1 MB 大对象堆数组会把 GC 压力
            /// 抬到不可接受；此重载让调用方复用 scratch，数值行为与公共重载<b>逐位一致</b>
            /// （同一实现体）。scratch 长度必须 ≥ 各场长度。
            /// </summary>
            public static void StepMassMomentum(float[] h, float[] vx, float[] vz, float[] ground,
                int cellsX, int cellsZ, float dx, float dt, float[] hNew, float[] vxNew, float[] vzNew)
            {
                int n = h.Length;
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

                        // 平均界面速度（界面两侧共享）。施主选值（donor-upwind，W1 校正）：
                        // 通量水深取**施主侧**（按界面速度符号），不再是两侧平均——原作
                        // HeightIntegration 反汇编按速度符号 movc 在 dC/邻格 d 之间选值
                        //（Water_*.3.hlslasm；界面平均+门控的旧格式自由演化下边际不稳定，
                        // Σh 6 s ×64 指数爆破，被满耦代理的每帧钳制掩盖至今）。原作另有
                        // d_eq 限制器与格自速度变体，逐指令复核归核心重翻译（见 W1 报告）。
                        float fluxL = (l >= 0 && ((vL > 0f) ? dL : di) > 0f) ? ((vL > 0f) ? dL : di) * vL : 0f;
                        float fluxR = (r >= 0 && ((vR > 0f) ? di : dR) > 0f) ? ((vR > 0f) ? di : dR) * vR : 0f;
                        float fluxD = (d >= 0 && ((vD > 0f) ? dD : di) > 0f) ? ((vD > 0f) ? dD : di) * vD : 0f;
                        float fluxU = (u >= 0 && ((vU > 0f) ? di : dU) > 0f) ? ((vU > 0f) ? di : dU) * vU : 0f;

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

            /// <summary>
            /// 活沙 Ground 一步（D 流件1，CPU 参考；compute 的 Ground* kernel 同式）。
            ///
            /// 【语义重建口径】Ground@600/@8652 反汇编的搬运/平流通道被字面量 ×0 死通道
            /// 禁用（第二平流场恒 −4 族），地形演化的活项按反汇编可恢复的乘积链重建：
            /// <list type="bullet">
            /// <item>坡度滑移（守恒下坡重分配）：通量系数 k = <see cref="SandSedimentAdvection"/>
            /// ×0.04（Ground@8652 的 `slideW×slope×cb0[154].w×dt×l(0.04)` 字面量链），
            /// 被 <see cref="SandWetnessWeight"/>（湿度侵蚀权重）与水深硬门
            /// sat((w−0.05)×100)（Ground@600 `mul ×l(100)`/`×l(30)` 双门的水门）调制；</item>
            /// <item>高度松弛低通 `h=(h'×amountKappa+h)/(amountKappa+4)`（Ground@600）；
            /// 速度场同式低通 + `v−=v×damping×dt` + ±noiseFactor 噪声 + |v|≤√g 限幅
            ///（Ground@8652）；</item>
            /// <item>沉积交换：capacity = sedimentCapacity × sat(w−0.05)^wEAG ×
            /// |v|^speedGamma（Ground@600 log/exp 对）；sediment&gt;cap 沉积
            /// ((sed−cap)×deposition×dt)、反之悬移（×suspension×dt×水门）；</item>
            /// <item>sediment 平流 = 半拉格朗日回溯采样（Ground@8652；原作速度通道死 →
            /// 自采样，公式保留、位移为零）。</item>
            /// </list>
            /// 演化量级自洽：k=0.02/s × swash 带坡度 ~0.05 × 22 s ≈ 0.022 m，与 B2 活沙
            /// 实测（22 s ≈0.018 m）同数量级。
            /// </summary>
            public static void GroundStep(float[] height, float[] vx, float[] vz, float[] sediment,
                float[] water, int cells, float dx, float dt,
                float[] hScratch, float[] sedScratch)
            {
                int n = cells * cells;
                Array.Copy(height, hScratch, n);
                Array.Copy(sediment, sedScratch, n);

                float slideK = SandSedimentAdvection * 0.04f;

                for (int z = 0; z < cells; z++)
                {
                    int zm = z > 0 ? z - 1 : 0;
                    int zp = z < cells - 1 ? z + 1 : cells - 1;
                    for (int x = 0; x < cells; x++)
                    {
                        int xm = x > 0 ? x - 1 : 0;
                        int xp = x < cells - 1 ? x + 1 : cells - 1;
                        int i = x + z * cells;
                        float h = height[i];
                        float w = water[i];

                        int iXm = xm + z * cells;
                        int iXp = xp + z * cells;
                        int iZm = x + zm * cells;
                        int iZp = x + zp * cells;

                        // 速度场（Ground@8652）：坡度投影 → 阻尼 → 噪声 → 限幅 → 低通。
                        // 原作水速耦合项与基线 −4 均为字面量死通道，按原样置零不移植。
                        float sx = (height[iXp] - height[iXm]) * 0.5f / dx;
                        float sz = (height[iZp] - height[iZm]) * 0.5f / dx;
                        float wet = SandWetnessWeight(w);
                        float slideW = Mathf.Max(wet - Mathf.Sqrt(sx * sx + sz * sz), 0f);
                        float acc = SandSedimentSuspension * 0.04f * slideW;
                        float nvx = vx[i] + (acc * sx - vx[i] * SandDamping) * dt
                                  + (Hash01(x, z) * 2f - 1f) * GroundNoiseFactor * dt;
                        float nvz = vz[i] + (acc * sz - vz[i] * SandDamping) * dt
                                  + (Hash01(z, x) * 2f - 1f) * GroundNoiseFactor * dt;
                        float speed = Mathf.Sqrt(nvx * nvx + nvz * nvz);
                        if (speed > GroundMaxVelocity)
                        {
                            nvx *= GroundMaxVelocity / speed;
                            nvz *= GroundMaxVelocity / speed;
                        }
                        vx[i] = (nvx * SandVelocityKappa + vx[i]) / (SandVelocityKappa + 4f);
                        vz[i] = (nvz * SandVelocityKappa + vz[i]) / (SandVelocityKappa + 4f);

                        // 沉积交换（Ground@600 容量式）：sediment ⇄ height。
                        float visible = Mathf.Max(w - DryDepth, 0f);
                        float vSpeed = Mathf.Sqrt(vx[i] * vx[i] + vz[i] * vz[i]);
                        float cap = SandSedimentCapacity
                                  * Mathf.Pow(Mathf.Clamp01(visible), SandSedimentWaterAmountGamma)
                                  * Mathf.Pow(vSpeed, SandSedimentWaterSpeedGamma)
                                  * wet;
                        float sed = sediment[i];
                        float gate = Mathf.Clamp01((w - DryDepth) * 100f);
                        if (sed > cap)
                        {
                            float d = (sed - cap) * SandSedimentDeposition * dt;
                            sed -= d;
                            h += d;
                        }
                        else
                        {
                            float d = (cap - sed) * SandSedimentSuspension * dt * gate;
                            sed += d;
                            h -= d;
                        }

                        // 坡度滑移（守恒下坡重分配）：净通量记账（入量读原值数组），和恒零。
                        float k = slideK * wet * gate * dt * 0.25f;
                        float outSum = Mathf.Max(h - height[iXp], 0f)
                                     + Mathf.Max(h - height[iXm], 0f)
                                     + Mathf.Max(h - height[iZp], 0f)
                                     + Mathf.Max(h - height[iZm], 0f);
                        float inSum = Mathf.Max(height[iXp] - h, 0f)
                                    + Mathf.Max(height[iXm] - h, 0f)
                                    + Mathf.Max(height[iZp] - h, 0f)
                                    + Mathf.Max(height[iZm] - h, 0f);
                        hScratch[i] = h + (inSum - outSum) * k;
                        sedScratch[i] = sed;
                    }
                }

                // 高度松弛低通（Ground@600：分子 = h'×amountKappa + 4×邻均值——解包段把
                // 中心+3 邻 ×1e-6 累加成 4×均值，/(kappa+4) 后是「向 4 邻均值收缩」的
                // 守恒平滑；匀场恒等、总量一阶守恒）。低通 pass 读滑移后的 hScratch 求邻均值。
                for (int i = 0; i < n; i++)
                    sediment[i] = sedScratch[i];
                for (int z = 0; z < cells; z++)
                {
                    int zm = z > 0 ? z - 1 : 0;
                    int zp = z < cells - 1 ? z + 1 : cells - 1;
                    for (int x = 0; x < cells; x++)
                    {
                        int xm = x > 0 ? x - 1 : 0;
                        int xp = x < cells - 1 ? x + 1 : cells - 1;
                        int i = x + z * cells;
                        float neighMean = (hScratch[xp + z * cells] + hScratch[xm + z * cells]
                                         + hScratch[x + zp * cells] + hScratch[x + zm * cells]) * 0.25f;
                        height[i] = (hScratch[i] * SandAmountKappa + 4f * neighMean)
                                  / (SandAmountKappa + 4f);
                    }
                }
            }
        }
    }
}
