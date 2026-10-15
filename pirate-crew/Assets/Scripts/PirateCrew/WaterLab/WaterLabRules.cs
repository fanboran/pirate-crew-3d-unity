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
    /// 动量方程省略平流项 v·∇v 只留 -g∇h 与阻尼——长波场景标准简化，破碎陡化由浅化承担
    /// （水深变小 → 波速 √(gh) 变慢 → 波峰追上波谷）。速度场的半拉格朗日平流只用于
    /// <b>泡沫场的输运</b>，不用于水深（平流不守恒，Sandcastle 采访自述的"数值误差累积"
    /// bug 正是这条路的坑）。
    /// </summary>
    public static class WaterLabRules
    {
        // ---- 域与分辨率（双分辨率：水密沙疏，Sandcastle 实证做法）----

        /// <summary>模拟域边长（世界单位，正方形，中心在原点）。</summary>
        public const float DomainSize = 64f;

        /// <summary>水域每轴格数 → 水格距 0.25 m。</summary>
        public const int WaterCells = 256;

        /// <summary>水域格距（m）。</summary>
        public const float WaterCellSize = DomainSize / WaterCells;

        /// <summary>地形每轴格数 → 地形格距 0.5 m（湿沙同分辨率挂在地形上）。</summary>
        public const int GroundCells = 128;

        /// <summary>地形格距（m）。</summary>
        public const float GroundCellSize = DomainSize / GroundCells;

        // ---- 物理与数值参数 ----

        /// <summary>
        /// 重力加速度（m/s²）。【逆向已知场景真值 16，暂不采纳】：本仓显式平均通量 +
        /// 中心差分动量格式在 g=16、弱阻尼下数值色散/负水深钳制超界（harness 实测：
        /// 质量漂移 0.655%、孤立波速比 0.625），要吃下 16 需先升通量格式（如 donor-cell
        /// 迎风），独立裁决后再对齐。
        /// </summary>
        public const float Gravity = 9.81f;

        /// <summary>
        /// 固定时间步（s）。CFL 验证：h_max≈1.6 m（深水台 1.3 + 波列 + 潮位）→
        /// c=√(9.81·1.6)≈4.0，C = c·dt/dx ≈ 0.26；保守上界 h=3 m 时 C≈0.36 ≤ <see cref="CflLimit"/>。
        /// </summary>
        public const float FixedStep = 1f / 60f;

        /// <summary>SWE 显式格式的 CFL 安全上限（理论 1.0，取保守 0.5）。</summary>
        public const float CflLimit = 0.5f;

        /// <summary>干湿阈值（m）：水深低于此值的格不产生流动（防干湿界振荡；Sandcastle 实测 0.05）。</summary>
        public const float DryDepth = 0.05f;

        /// <summary>
        /// 速度阻尼（1/s，线性阻力项）。原作涌浪能横穿 40 m+ 水面到岸（demo 实景），
        /// 反证其深水有效阻尼 ≲ c·t/λ ≈ 0.05/s；0.12/s 会让 λ40 涌浪一个域宽就衰减到
        /// 1/3，水底波影先天不可见（第九轮根因之一），压到 0.02。
        /// </summary>
        public const float VelocityDamping = 0.02f;

        /// <summary>速度限幅（m/s；超限等比缩回，防局部数值尖峰）。</summary>
        public const float MaxVelocity = 8f;

        /// <summary>海平面高程（m，世界 Y）。地形与水面都以它为基准。</summary>
        public const float SeaLevel = 0f;

        // ---- 波源（offshore 边行波注入：多向波列 + 潮位呼吸）----
        //
        // 【Sandcastle 实证】其环境浪 = UpdateAmount kernel 的「涌浪泵 + 潮位松弛」
        // （Water compute blob 6/17 反汇编，docs/技术/渲染/调研/Sandcastle水体逆向.md §6）：
        // spawnMap 注水带内 amount += rate·sin(空间相位 + 8ωt)·dt，全场向 seaLevel+amp·sin(ωt)
        // 松弛。WavesProperty 预设的 a/b/c/d 四列 Vector3 是代码构造常量，方法体在 Cpp2IL
        // v39 下不可恢复——本仓按同构「四列叠加」自推数值，结构（列数/色散/潮位）照抄。

        /// <summary>波源带宽度（m，从 -x 域边向内）。注入带内水面高程向目标松弛。</summary>
        public const float WaveBandWidth = 4f;

        /// <summary>
        /// Shapiro 网格滤波强度（**四阶**、x/y 两轴分离，每步
        /// `h -= ε/16×(h₊₂ − 4h₊₁ + 6h − 4h₋₁ + h₋₂)` 逐轴施加，带外全场）。
        /// 【为什么必须有】弱阻尼（0.02）+ 深度比例弱松弛下，2 格尺度的棋盘模无人压制
        /// （旧体系里速度阻尼 0.12 与均匀松弛 0.25 恰好兼职压着它）——η 场长出 ±0.5 m 级
        /// 逐格锯齿，把破碎判据 |η−邻均值|/h 全场误触发 → 泡沫饱和成白幕（Python 实验室与
        /// GPU 稳态转储双重实证）。
        /// 【为什么是四阶】二阶 `h−ε(h−4邻均值)` 对 λ4–8 格每步衰减 0.92–0.98，恰好把
        /// 涌浪浅化陡化出的 bore 陡 front 当噪声吃掉——r11 转储实证波到水线摆幅
        /// 0.50→0.21 m 腰斩。四阶传函 1−(ε/16)(2cos2k−8cos k+6) 单调不放大：
        /// λ2 保留 1−ε（与旧二阶 0.08 同杀 0.84/步，棋盘不复活）、λ4 保留 0.96、
        /// λ6 保留 0.99、λ40 m 长波每秒损 <0.1%；偏差全场求和为零，质量守恒不受影响。
        /// </summary>
        public const float GridFilterEps = 0.16f;

        /// <summary>松弛系数（1/s）：注入带内 η 与速度向目标值的逼近速率。大到两三步到位、小到不生硬反射。</summary>
        public const float WaveRelaxRate = 6f;

        /// <summary>潮位呼吸振幅（m）：全场水面目标 = SeaLevel + TideAmplitude·sin(ωt)，
        /// 让浪以「组」的节奏涌上来（swash 群性）。</summary>
        public const float TideAmplitude = 0.05f;

        /// <summary>潮位呼吸周期（s）。</summary>
        public const float TidePeriod = 9f;

        /// <summary>
        /// 潮位松弛深度系数（1/(s·m)）：带外全场松弛权重 = 此值 × 局部水深 × dt。
        /// 【逆向结构对齐】原作 UpdateAmount（Water_20632 反汇编）的潮位松弛权重比例于
        /// 局部水场值（`amount += 水场×(1/15)×间隙`），干处权重为 0 完全不松弛——
        /// 深水被压平向潮位、浅水架几乎不阻尼，行波得以活着冲上滩。本仓旧实现是全场
        /// 均匀 0.25/s，对行波等价 14 m 衰减长度（叠加速度阻尼 ~10 m），λ40 涌浪
        /// 没到岸就被吃光（近岸 η 振幅 ±0.008 m，水底波影调制 <1%，第九轮解析实锤）。
        /// 系数 0.02 按「WavesType 数值抄不到、按涌浪存活自推」口径取：深水 1.3 m 处
        /// 松弛 0.026/s，涌浪穿越半域衰减少于两成；浅水 0.2 m 处 0.004/s 近乎自由。
        /// 代价：潮位呼吸在浅水跟踪率仅 ~4%（近岸岸线呼吸变弱），swash 群性改由
        /// 涌浪本身承担。
        /// </summary>
        public const float TideRelaxDepthRate = 0.02f;

        /// <summary>一列入射波：方向、波长、振幅、初相。相速由深水色散关系 ω²=g·k 导出——
        /// 四列波长互质，波峰线永不重合，叠加天然不规律（这正是单正弦「太规律」的解药）。</summary>
        public struct WaveTrain
        {
            public float DirX;
            public float DirZ;
            public float Length;
            public float Amplitude;
            public float Phase;

            public WaveTrain(float dirX, float dirZ, float length, float amplitude, float phase)
            {
                float inv = 1f / Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                DirX = dirX * inv;
                DirZ = dirZ * inv;
                Length = length;
                Amplitude = amplitude;
                Phase = phase;
            }

            /// <summary>波数 k = 2π/L（rad/m）。</summary>
            public float K => 2f * Mathf.PI / Length;

            /// <summary>
            /// 浅水色散 ω = k·√(g·h带)（rad/s）。域内全是浅水长波（h≤1.3 m ≪ λ），
            /// SWE 特征速 √(gh) 不色散——用深水色散 ω=√(gk) 会把 λ40 m 波的相速
            /// 强迫到 7.9 m/s > 特征速 3.6 m/s（超临界），波源带激波化爆场（30× 快进实拍）。
            /// h带 取波源带处的静水深（深水平台高程到海平面）。
            /// </summary>
            public float Omega => K * WaveSpeed(SeaLevel - OffshoreDepth);
        }

        /// <summary>
        /// 入射波列（致敬 Sandcastle WavesProperty.Parameters 的 a/b/c/d 四列结构）。
        /// 【逆向对齐】原作渲染端无任何程序浪形（Lit_Water VS 已反汇编实证：顶点 Y 就是
        /// 高度场 4 邻均值），其泵源为单条 ~40 m 长涌浪（spawnMap 相位 0.0378 rad/格×0.25 m/格
        /// → λ≈41.6 m），且泵永不停——全场始终有一道行进的 η 图案（峰下水底暗、谷下亮，
        /// 水柱透射项 exp(−0.721Δ) 随之持续扫动 = 「水底波影」连绵不断的来源）。
        /// 【r11 裁决】四列互拍会产生 ~9 s 拍频平静窗，窗内 η 全平、水底静止（r10 三枪
        /// 差分实证：跨枪位移 ≈0，浅水带主导条纹是层理/沙纹静态斑）——削次级列幅对齐
        /// 单涌浪；主涌 0.16→0.30 m：0.16 m 下水柱透射调制仅 8–15% 不可见（r10 实测），
        /// ≥25% 可见门槛反推需峰谷摆幅 ~0.6 m，0.30 m 浅化后可达。幅值按 bore 观感自推
        /// （WavesProperty 数值不可恢复）；CFL 自检 h_max≈1.69 m → C≈0.27，余量足。
        /// </summary>
        public static readonly WaveTrain[] WaveTrains =
        {
            new WaveTrain(1f, 0.00f, 40.0f, 0.300f, 0.0f),
            new WaveTrain(1f, 0.30f, 21.0f, 0.020f, 1.7f),
            new WaveTrain(1f, -0.35f, 11.0f, 0.010f, 3.9f),
            new WaveTrain(1f, 0.15f, 5.9f, 0.006f, 5.1f),
        };

        /// <summary>波列总振幅（m）——CFL 与编码量程的自检上界（波峰同相的极端叠加）。</summary>
        public static float MaxWaveAmplitude
        {
            get
            {
                float sum = 0f;
                foreach (var train in WaveTrains)
                    sum += train.Amplitude;
                return sum;
            }
        }

        /// <summary>潮位目标高程（m）。</summary>
        public static float TideEta(float t)
        {
            return SeaLevel + TideAmplitude * Mathf.Sin(2f * Mathf.PI * t / TidePeriod);
        }

        /// <summary>波源目标水面高程（m）：潮位 + 四列行波（向 +x 传播）。</summary>
        public static float WaveEta(float worldX, float worldZ, float t)
        {
            float eta = TideEta(t);
            foreach (var train in WaveTrains)
            {
                float s = Mathf.Sin(train.K * (train.DirX * worldX + train.DirZ * worldZ)
                                    - train.Omega * t + train.Phase);
                eta += train.Amplitude * s;
            }
            return eta;
        }

        // ---- 破碎与泡沫 ----

        /// <summary>破碎判据：波陡 η'/h > 此值（Miche 准则常数 0.78 的工程版）判定破碎。</summary>
        public const float BreakingSteepness = 0.78f;

        /// <summary>泡沫生成增益（每步生成量 = 超出判据部分 × 此值）。</summary>
        public const float FoamGain = 2.4f;

        /// <summary>泡沫每步衰减系数（保留比）。</summary>
        public const float FoamDecay = 0.972f;

        /// <summary>泡沫邻域扩散保留比（每步向 4 邻均摊的量）。</summary>
        public const float FoamDiffuse = 0.16f;

        /// <summary>
        /// 破碎判据：局部波面陡度。陡度 = |η - 邻域均值| / max(水深, 干湿阈值)，
        /// 超过 <see cref="BreakingSteepness"/> 即认为该格处于破碎波峰。
        /// </summary>
        public static bool IsBreaking(float eta, float neighborMeanEta, float depth)
        {
            float d = Mathf.Max(depth, DryDepth);
            return Mathf.Abs(eta - neighborMeanEta) / d > BreakingSteepness;
        }

        // ---- 湿沙 ----

        /// <summary>湿沙饱和速率（1/s）：被水覆盖的格向 w=1 逼近。</summary>
        public const float WetnessSoakRate = 3f;

        /// <summary>湿沙蒸发速率（1/s）：无水覆盖后向 w=0 衰减（比浸润慢一个量级，退潮后湿痕留存）。</summary>
        public const float WetnessEvaporateRate = 0.15f;

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

        /// <summary>深水平台高程（m）。压在 1.3 m：吸收不至于饱和成死黑，浅水架更长。</summary>
        public const float OffshoreDepth = -1.3f;

        /// <summary>缓坡浅水架末端高程（m）： shelf 尽头留一段极浅，半透明绿松石带的主体。</summary>
        public const float ShelfEndHeight = -0.15f;

        /// <summary>缓坡浅水架起止（世界 X，域 [-32,32]，~4% 坡）。</summary>
        public const float ShelfStartX = -26f;
        public const float ShelfEndX = 6f;

        /// <summary>滩面段起止（世界 X，~8% 坡）：比浅水架陡，收窄浪的爬升带与湿沙带。</summary>
        public const float FaceStartX = 6f;
        public const float FaceEndX = 14f;

        /// <summary>干沙滩平台高程（m）。</summary>
        public const float BeachHeight = 0.5f;

        /// <summary>沙纹起伏幅度（m）——高频微起伏给折射一点可读的底纹。
        /// 0.03 时两组交叉正弦的波腹节点在滩面上显成规则暗点阵（实拍第五轮），压到 0.012。</summary>
        public const float SandRippleAmplitude = 0.012f;

        /// <summary>smoothstep（带 clamp）。</summary>
        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 程序化沙滩剖面（世界 X → 高程）：深水台 → 缓坡浅水架 → 较陡滩面 → 干沙台，
        /// 叠加沿 X/Z 的确定性沙纹。
        /// </summary>
        public static float TerrainHeight(float worldX, float worldZ)
        {
            float h = OffshoreDepth;
            h = Mathf.Lerp(h, ShelfEndHeight, Smooth01((worldX - ShelfStartX) / (ShelfEndX - ShelfStartX)));
            h = Mathf.Lerp(h, BeachHeight, Smooth01((worldX - FaceStartX) / (FaceEndX - FaceStartX)));
            // 确定性沙纹：单向为主的低幅正弦（两组等权交叉会出波腹节点格架，
            // 滩面上显成满场静态软点阵——实拍第六轮放大实证；次向留 0.25 打破平行单调）
            h += SandRippleAmplitude * (Mathf.Sin(worldX * 1.7f + worldZ * 0.6f) * 0.75f
                                        + Mathf.Sin(worldZ * 2.3f - worldX * 0.4f) * 0.25f);
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

                        // 4 邻（-1 = 出界＝反射墙）。墙界面上质量通量必须显式为 0：
                        // 「越界取自身」会让 F = h·v ≠ 0，边界格凭空流进流出，破坏守恒。
                        int l = cx > 0 ? GridIndex(cx - 1, cz, cellsX) : -1;
                        int r = cx < cellsX - 1 ? GridIndex(cx + 1, cz, cellsX) : -1;
                        int d = cz > 0 ? GridIndex(cx, cz - 1, cellsX) : -1;
                        int u = cz < cellsZ - 1 ? GridIndex(cx, cz + 1, cellsX) : -1;

                        // 平均通量（半格界面）：F = h̄·v̄。界面级干湿 gate——
                        // 「两侧水深都超过干湿阈值」该界面才流动。必须两侧格算出同一判定，
                        // 否则同一界面的进出通量不等，质量不守恒（这是界面格式守恒的关键）。
                        float fluxL = (l >= 0 && h[l] > DryDepth && hi > DryDepth) ? ((h[l] + hi) * 0.5f) * ((vx[l] + vx[i]) * 0.5f) : 0f;
                        float fluxR = (r >= 0 && hi > DryDepth && h[r] > DryDepth) ? ((hi + h[r]) * 0.5f) * ((vx[i] + vx[r]) * 0.5f) : 0f;
                        float fluxD = (d >= 0 && h[d] > DryDepth && hi > DryDepth) ? ((h[d] + hi) * 0.5f) * ((vz[d] + vz[i]) * 0.5f) : 0f;
                        float fluxU = (u >= 0 && hi > DryDepth && h[u] > DryDepth) ? ((hi + h[u]) * 0.5f) * ((vz[i] + vz[u]) * 0.5f) : 0f;

                        hNew[i] = hi - dt / dx * ((fluxR - fluxL) + (fluxU - fluxD));
                        if (hNew[i] < 0f) hNew[i] = 0f; // 数值保护：无负水深

                        // 动量：v += -dt·g·∇η（水面高程梯度，非水深梯度——干前滩这点很关键）。
                        // 出界侧 η 取自身（诺伊曼：法向梯度 0）。
                        float eta0 = ground[i] + hi;
                        if (hNew[i] > DryDepth)
                        {
                            float etaL = (l >= 0) ? ground[l] + h[l] : eta0;
                            float etaR = (r >= 0) ? ground[r] + h[r] : eta0;
                            float etaD = (d >= 0) ? ground[d] + h[d] : eta0;
                            float etaU = (u >= 0) ? ground[u] + h[u] : eta0;
                            float sx = (etaR - etaL) / (2f * dx);
                            float sz = (etaU - etaD) / (2f * dx);
                            vxNew[i] = vx[i] - dt * (Gravity * sx + VelocityDamping * vx[i]);
                            vzNew[i] = vz[i] - dt * (Gravity * sz + VelocityDamping * vz[i]);
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
