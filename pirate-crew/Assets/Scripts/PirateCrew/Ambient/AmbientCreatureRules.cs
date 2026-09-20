using UnityEngine;

namespace PirateCrew.Ambient
{
    /// <summary>
    /// 海鸥飞行规则（纯 C#）。
    ///
    /// 【路径形态】李萨如（Lissajous）轨迹：X/Z 用不同频率的正弦，得到"不闭合的椭圆盘旋"，
    /// 比正圆更像真鸟；高度用第三条正弦做缓升缓降。轨迹是**解析式**，因此可在无头测试里
    /// 采样任意时长并断言"永远不越界、永远不进禁飞区"。
    ///
    /// 【可读性红线】所有轨道中心与半径都必须让轨迹落在 <see cref="AmbientNoFlyZone"/> 之外
    /// （低空轨道放到竞技场外水域；唯一飞越竞技场的轨道抬到天花板以上）。
    /// 测试 <c>GullFlight_StaysOutOfNoFlyZone</c> 逐帧断言这条。
    /// </summary>
    public readonly struct GullOrbit
    {
        /// <summary>轨道中心（y = 基准高度）。</summary>
        public readonly Vector3 Center;

        /// <summary>X 向半径。</summary>
        public readonly float RadiusX;

        /// <summary>Z 向半径。</summary>
        public readonly float RadiusZ;

        /// <summary>X 向角频率（弧度/秒）。</summary>
        public readonly float FrequencyX;

        /// <summary>Z 向角频率（弧度/秒）。</summary>
        public readonly float FrequencyZ;

        /// <summary>高度角频率（弧度/秒）。</summary>
        public readonly float FrequencyY;

        /// <summary>X 相位偏移。</summary>
        public readonly float PhaseX;

        /// <summary>Z 相位偏移。</summary>
        public readonly float PhaseZ;

        /// <summary>高度相位偏移。</summary>
        public readonly float PhaseY;

        /// <summary>高度振幅。</summary>
        public readonly float AltitudeAmplitude;

        public GullOrbit(Vector3 center, float radiusX, float radiusZ,
            float frequencyX, float frequencyZ, float frequencyY,
            float phaseX, float phaseZ, float phaseY, float altitudeAmplitude)
        {
            Center = center;
            RadiusX = radiusX;
            RadiusZ = radiusZ;
            FrequencyX = frequencyX;
            FrequencyZ = frequencyZ;
            FrequencyY = frequencyY;
            PhaseX = phaseX;
            PhaseZ = phaseZ;
            PhaseY = phaseY;
            AltitudeAmplitude = altitudeAmplitude;
        }

        /// <summary>t 时刻的轨迹点。</summary>
        public Vector3 Evaluate(float time)
        {
            return new Vector3(
                Center.x + RadiusX * Mathf.Sin(FrequencyX * time + PhaseX),
                Center.y + AltitudeAmplitude * Mathf.Sin(FrequencyY * time + PhaseY),
                Center.z + RadiusZ * Mathf.Sin(FrequencyZ * time + PhaseZ));
        }

        /// <summary>t 时刻的解析速度（对 t 求导，用于朝向）。</summary>
        public Vector3 Velocity(float time)
        {
            return new Vector3(
                RadiusX * FrequencyX * Mathf.Cos(FrequencyX * time + PhaseX),
                AltitudeAmplitude * FrequencyY * Mathf.Cos(FrequencyY * time + PhaseY),
                RadiusZ * FrequencyZ * Mathf.Cos(FrequencyZ * time + PhaseZ));
        }
    }

    /// <summary>海鸥行为状态。</summary>
    public enum GullState
    {
        /// <summary>正常盘旋。</summary>
        Cruise = 0,

        /// <summary>俯冲入水（只在竞技场外水域发生）。</summary>
        Dive = 1,
    }

    /// <summary>一只海鸥的完整运动计划：盘旋轨道 + 俯冲目标点。</summary>
    public readonly struct GullPlan
    {
        /// <summary>盘旋轨道。</summary>
        public readonly GullOrbit Orbit;

        /// <summary>俯冲入水点（必须落在禁飞区之外）。</summary>
        public readonly Vector3 DiveTarget;

        /// <summary>是否允许俯冲。飞越竞技场上方的那只设为 false ——
        /// 它一旦俯冲就要穿过中央走廊（可读性红线），所以只盘旋不俯冲。</summary>
        public readonly bool Dives;

        public GullPlan(GullOrbit orbit, Vector3 diveTarget, bool dives)
        {
            Orbit = orbit;
            DiveTarget = diveTarget;
            Dives = dives;
        }
    }

    /// <summary>
    /// 海鸥飞行/俯冲/惊飞的纯规则。
    /// </summary>
    public static class GullFlightRules
    {
        /// <summary>巡航高度下限（世界 Y，水面之上）【提案】：低于此值会读作"贴地飞"，
        /// 且靠近投掷视线，故设在水面上方约 6 单位。</summary>
        public const float MinCruiseAltitude = 6f;

        /// <summary>巡航高度上限（世界 Y）【提案】。</summary>
        public const float MaxCruiseAltitude = 16f;

        /// <summary>巡航拍翅频率（次/秒）【提案】：慢拍，读出"滑翔为主"。</summary>
        public const float CruiseFlapRate = 1.35f;

        /// <summary>俯冲拍翅频率（次/秒）【提案】：急拍。</summary>
        public const float DiveFlapRate = 3.4f;

        /// <summary>惊飞拍翅频率（次/秒）【提案】：更快。</summary>
        public const float PanicFlapRate = 4.6f;

        /// <summary>俯冲总时长（秒）【提案】（下行 + 回升）。</summary>
        public const float DiveDuration = 3.2f;

        /// <summary>两次俯冲之间的最短间隔（秒）【提案】。</summary>
        public const float MinDiveInterval = 9f;

        /// <summary>两次俯冲之间的最长间隔（秒）【提案】。</summary>
        public const float MaxDiveInterval = 18f;

        /// <summary>爆炸惊飞的持续时长（秒）【提案】。</summary>
        public const float PanicDuration = 2.6f;

        /// <summary>爆炸惊飞的抬升速度（世界单位/秒）【提案】。</summary>
        public const float PanicRiseSpeed = 5.5f;

        /// <summary>拍翅相位（连续）。<paramref name="flapRate"/> 由状态决定。</summary>
        public static float WingPhase(float time, float flapRate, float offset)
        {
            return offset + time * flapRate * Mathf.PI * 2f;
        }

        /// <summary>翅尖相对肩部的俯仰角（度）。正值上扬、负值下压。
        /// 用 |sin| 的不对称映射模拟"下拍更有力"。</summary>
        public static float WingAngleDegrees(float wingPhase, float amplitudeDegrees)
        {
            float s = Mathf.Sin(wingPhase);
            // 下拍（s<0）压缩一半，上扬（s>0）保持全幅 —— 鸟类上拍与下拍的幅度不对称。
            float shaped = s >= 0f ? s : s * 0.55f;
            return shaped * amplitudeDegrees;
        }

        /// <summary>
        /// 俯冲轨迹：<paramref name="t"/> ∈ [0, 2)，t&lt;1 为下冲段、t≥1 为回升段。
        /// 下冲段用二次贝塞尔（控制点抬高，得到"先加速俯冲再拉平"的弧线）。
        /// **凸组合性质**保证了路径 X 始终落在两端点之间 —— 只要两端点 X 同在禁飞区外，
        /// 整条路径就在禁飞区外（测试断言）。
        /// </summary>
        public static Vector3 DivePoint(Vector3 from, Vector3 target, float t)
        {
            if (t < 1f)
            {
                float u = Mathf.Clamp01(t);
                Vector3 control = new Vector3(
                    Mathf.Lerp(from.x, target.x, 0.4f),
                    Mathf.Max(from.y, target.y) + 1.2f,
                    Mathf.Lerp(from.z, target.z, 0.4f));
                return QuadraticBezier(from, control, target, u);
            }

            float v = Mathf.Clamp01(t - 1f);
            // 回升：从 target 抬回 from（用平滑步，末端稳定收束到盘旋高度）。
            float ease = v * v * (3f - 2f * v);
            return Vector3.Lerp(target, from, ease);
        }

        /// <summary>二次贝塞尔。</summary>
        public static Vector3 QuadraticBezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float u = 1f - t;
            return a * (u * u) + control * (2f * u * t) + b * (t * t);
        }

        /// <summary>俯冲入水点（水面高度）—— 水花与涟漪在 y = 水面处触发。</summary>
        public static Vector3 SplashPoint(Vector3 target, float waterY)
        {
            return new Vector3(target.x, waterY, target.z);
        }

        /// <summary>由速度求朝向角（度，绕 Y）。速度过小时保持 <paramref name="fallback"/>。</summary>
        public static float HeadingDegrees(Vector3 velocity, float fallback = 0f)
        {
            float sqr = velocity.x * velocity.x + velocity.z * velocity.z;
            if (sqr < 1e-8f)
                return fallback;

            return Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
        }

        /// <summary>惊飞强度衰减（指数，dt 为帧时长）。返回 [0, 1]。</summary>
        public static float DecayPanic(float panic, float dt)
        {
            float p = Mathf.Clamp01(panic);
            if (p <= 0f)
                return 0f;

            float next = p - dt / PanicDuration;
            return next <= 0f ? 0f : next;
        }

        /// <summary>惊飞位移：向斜上方 + 远离爆心的水平反向抬升。</summary>
        public static Vector3 PanicOffset(Vector3 gullPosition, Vector3 blastPosition, float panic)
        {
            float p = Mathf.Clamp01(panic);
            if (p <= 0f)
                return Vector3.zero;

            Vector3 away = gullPosition - blastPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f)
                away = Vector3.back;
            away.Normalize();

            Vector3 lift = Vector3.up * (PanicRiseSpeed * p * p);
            Vector3 push = away * (2.2f * p);
            return lift + push;
        }

        /// <summary>把高度夹进巡航带（防止俯冲/惊飞把鸟带到不合理高度）。</summary>
        public static Vector3 ClampAltitude(Vector3 position)
        {
            return new Vector3(position.x, Mathf.Clamp(position.y, MinCruiseAltitude, MaxCruiseAltitude), position.z);
        }

        /// <summary>
        /// 生成 <paramref name="count"/> 只海鸥的**规范轨道布局**（【提案】）：
        ///   0 号 = 西侧远海、1 号 = 东侧远海、2 号 = 高空飞越竞技场（在禁飞天花板之上）、3 号 = 西侧近场；
        ///   以 4 为周期循环（超过 4 只会重复轨道并靠随机相位错开）。
        ///
        /// 【布局不变量（测试逐帧断言）】
        ///   · 低空轨道全部落在竞技场**外**水域（X 远离中央走廊，或 Z 在禁区之外）；
        ///   · 飞越竞技场的轨道抬到 <see cref="AmbientNoFlyZone.DefaultCeilingY"/> 之上；
        ///   · 俯冲目标同样在禁飞区之外。
        /// 这是把"可读性红线"从"记得别放中间"变成"可断言的几何约束"。
        /// </summary>
        public static GullPlan[] BuildPlans(AmbientArena arena, AmbientRandom rng, int count)
        {
            if (count < 0)
                count = 0;

            var plans = new GullPlan[count];
            for (int i = 0; i < count; i++)
            {
                // 每次调用都取一组随机相位：同一种子给同一布局，不同种子错开。
                float phaseX = rng.Range(0f, 6.283f);
                float phaseZ = rng.Range(0f, 6.283f);
                float phaseY = rng.Range(0f, 6.283f);

                plans[i] = BuildPlan(i, arena, phaseX, phaseZ, phaseY);
            }

            return plans;
        }

        /// <summary>单只海鸥的轨道（供 <see cref="BuildPlans"/> 与测试复用）。</summary>
        public static GullPlan BuildPlan(int index, AmbientArena arena,
            float phaseX, float phaseZ, float phaseY)
        {
            float cx = arena.CenterX;
            float cz = arena.CenterZ;
            float w = arena.Width;
            float waterY = arena.WaterY;

            switch (((index % 4) + 4) % 4)
            {
                case 1:   // 东侧远海
                    return new GullPlan(
                        new GullOrbit(new Vector3(w + 6f, 8.6f, -7f), 7f, 3.6f,
                            0.19f, 0.27f, 0.14f, phaseX, phaseZ, phaseY, 1.3f),
                        new Vector3(w + 7f, waterY, -7.6f), dives: true);

                case 2:   // 高空飞越竞技场（禁飞天花板之上）—— 只盘旋，不俯冲
                    // 俯冲目标仍给一个**合法**点（禁飞区外），即便 Dives=false 也不留违规数据。
                    return new GullPlan(
                        new GullOrbit(new Vector3(cx, 13.6f, cz), 9f, 4f,
                            0.16f, 0.23f, 0.11f, phaseX, phaseZ, phaseY, 1.5f),
                        new Vector3(cx, waterY, -8f), dives: false);

                case 3:   // 西侧近场
                    return new GullPlan(
                        new GullOrbit(new Vector3(-4f, 7.6f, cz), 5f, 2.5f,
                            0.25f, 0.35f, 0.19f, phaseX, phaseZ, phaseY, 1.1f),
                        new Vector3(-5f, waterY, cz), dives: true);

                default:  // 西侧远海
                    return new GullPlan(
                        new GullOrbit(new Vector3(-6f, 8.6f, -7f), 6f, 3.5f,
                            0.22f, 0.31f, 0.15f, phaseX, phaseZ, phaseY, 1.4f),
                        new Vector3(-7f, waterY, -7f), dives: true);
            }
        }
    }

    /// <summary>螃蟹行为状态。</summary>
    public enum CrabState
    {
        /// <summary>岸边横爬巡逻。</summary>
        Patrol = 0,

        /// <summary>遇单位靠近 → 缩进沙里（不可见）。</summary>
        Buried = 1,
    }

    /// <summary>
    /// 螃蟹横爬/躲沙的纯规则。
    ///
    /// 【行为】沿一条预先给定的岸边线段做 ping-pong 往返，钳子摆动；
    /// 最近单位水平距离 &lt; <see cref="BuryTriggerDistance"/> → 缩沙（0.25 秒内沉到沙下）；
    /// 距离恢复 &gt; <see cref="ReleaseDistance"/> → 出沙复位。滞回区间避免"探头-缩回"抖动。
    /// </summary>
    public static class CrabBehaviorRules
    {
        /// <summary>触发缩沙的距离【提案】（单位半径约 0.2 单位，2.2 单位约等于"三尺外有动静"）。</summary>
        public const float BuryTriggerDistance = 2.2f;

        /// <summary>解除缩沙的距离【提案】（&gt; 触发距离，形成滞回）。</summary>
        public const float ReleaseDistance = 3.2f;

        /// <summary>爆炸惊扰半径【提案】：爆心在此半径内时，即使最近单位很远，螃蟹也会缩沙。</summary>
        public const float AlarmRadius = 8f;

        /// <summary>缩沙时下沉的深度（世界单位）【提案】：0.28 让它完全没入湿沙/浅水。</summary>
        public const float BuryDepth = 0.28f;

        /// <summary>缩沙/出沙的过渡时长（秒）【提案】。</summary>
        public const float BuryLerpDuration = 0.25f;

        /// <summary>横爬速度（世界单位/秒）【提案】。</summary>
        public const float PatrolSpeed = 0.45f;

        /// <summary>钳子摆动频率（次/秒）【提案】。</summary>
        public const float ClawRate = 1.6f;

        /// <summary>状态转移（纯函数）：距离 → 状态。</summary>
        public static CrabState Next(CrabState current, float distanceToNearestUnit)
        {
            switch (current)
            {
                case CrabState.Patrol:
                    return distanceToNearestUnit < BuryTriggerDistance ? CrabState.Buried : CrabState.Patrol;
                case CrabState.Buried:
                    return distanceToNearestUnit > ReleaseDistance ? CrabState.Patrol : CrabState.Buried;
                default:
                    return CrabState.Patrol;
            }
        }

        /// <summary>ping-pong 参数：返回 [0,1] 的三角波（周期 2×<paramref name="halfPeriod"/> 秒）。
        /// 【连续性】对 dt→0 增量→0，但折返点处有速度反向（不是相位跳变）。</summary>
        public static float PingPong(float time, float halfPeriod)
        {
            if (halfPeriod <= 1e-5f)
                return 0f;

            float cycle = time / halfPeriod;          // 每 2.0 一个完整来回
            float m = cycle - Mathf.Floor(cycle * 0.5f) * 2f;   // [0,2)
            return m <= 1f ? m : 2f - m;
        }

        /// <summary>线段上的往返点。</summary>
        public static Vector3 PatrolPoint(Vector3 a, Vector3 b, float pingPong01)
        {
            return Vector3.Lerp(a, b, Mathf.Clamp01(pingPong01));
        }

        /// <summary>钳子摆动角（度），左右钳相差 π 相位（错开摆动）。</summary>
        public static float ClawAngleDegrees(float phase, float amplitudeDegrees)
        {
            return Mathf.Sin(phase) * amplitudeDegrees;
        }

        /// <summary>缩沙过渡进度 → 当前可见度（1 = 完全露出，0 = 完全埋没）。</summary>
        public static float BuriedVisibility(float buryProgress01)
        {
            return 1f - Mathf.Clamp01(buryProgress01);
        }
    }

    /// <summary>
    /// 鱼群 boids 参数与水体边界（纯 C#）。
    ///
    /// 【水体盒的来历】竞技场是浮在海上的沙岛，岛外先有一层"浅海床台阶"
    /// （<c>M2BattleSceneSetup.CreateSeabedShelves</c>：浅台顶面 = 水面 − 0.4 = −0.6，
    /// 外扩 6 单位；中台顶面 −1.6，外扩 16）。鱼必须待在**浅台之上、水面之下**，
    /// 否则会穿海床或跃出水面被人看见。默认盒因此取 y ∈ [−0.55, −0.28]。
    /// </summary>
    public readonly struct BoidsSettings
    {
        /// <summary>水体盒中心。</summary>
        public readonly Vector3 Center;

        /// <summary>水体盒半径（各轴半长）。</summary>
        public readonly Vector3 Extents;

        /// <summary>分离半径（小于它开始互相排斥）。</summary>
        public readonly float SeparationRadius;

        /// <summary>分离权重。</summary>
        public readonly float SeparationWeight;

        /// <summary>对齐权重。</summary>
        public readonly float AlignmentWeight;

        /// <summary>聚集（向邻居质心）权重。</summary>
        public readonly float CohesionWeight;

        /// <summary>回归鱼群锚点（整群缓慢巡游的目标点）权重。</summary>
        public readonly float AnchorWeight;

        /// <summary>边界推回权重。</summary>
        public readonly float BoundsWeight;

        /// <summary>最大速度（世界单位/秒）。</summary>
        public readonly float MaxSpeed;

        /// <summary>最小速度（防止停下）。</summary>
        public readonly float MinSpeed;

        /// <summary>单帧最大转向（力）大小。</summary>
        public readonly float MaxForce;

        public BoidsSettings(Vector3 center, Vector3 extents, float separationRadius,
            float separationWeight, float alignmentWeight, float cohesionWeight,
            float anchorWeight, float boundsWeight, float maxSpeed, float minSpeed, float maxForce)
        {
            Center = center;
            Extents = extents;
            SeparationRadius = separationRadius;
            SeparationWeight = separationWeight;
            AlignmentWeight = alignmentWeight;
            CohesionWeight = cohesionWeight;
            AnchorWeight = anchorWeight;
            BoundsWeight = boundsWeight;
            MaxSpeed = maxSpeed;
            MinSpeed = minSpeed;
            MaxForce = maxForce;
        }

        /// <summary>
        /// 默认参数【提案】：由 <paramref name="arena"/> 的**近侧（相机侧）**水域推导水体盒，
        /// 让鱼群出现在前景水面下（最容易被看到）。
        /// </summary>
        public static BoidsSettings Default(AmbientArena arena)
        {
            float waterY = arena.WaterY;
            // 浅海床顶面 = 水面 - 0.4（与 M2BattleSceneSetup.CreateSeabedShelves 一致）。
            float seabedY = waterY - 0.4f;
            float midY = Mathf.Lerp(seabedY, waterY, 0.45f);

            Vector3 center = new Vector3(arena.CenterX - 6f, midY, arena.Depth + 3.2f);
            Vector3 extents = new Vector3(5.5f, Mathf.Min(0.11f, (waterY - 0.02f - seabedY) * 0.35f), 2.4f);

            return new BoidsSettings(center, extents, 0.28f,
                separationWeight: 1.55f,
                alignmentWeight: 0.65f,
                cohesionWeight: 0.55f,
                anchorWeight: 0.9f,
                boundsWeight: 2.4f,
                maxSpeed: 1.15f,
                minSpeed: 0.35f,
                maxForce: 2.6f);
        }
    }

    /// <summary>
    /// 简化版 boids（分离 / 对齐 / 聚集 + 锚点回归 + 水体盒约束）。纯 C#，O(n²)，n ≤ 20。
    ///
    /// 【为什么不用 Unity 的 NavMesh/物理】鱼是纯装饰，任何刚体/碰撞都会引入
    /// "不该挡的东西"并可能触碰弹道（场景文档 §9.4 红线）。这里只在数组里算位置。
    /// </summary>
    public static class BoidsRules
    {
        /// <summary>
        /// 推进一帧。数组长度必须 ≥ <paramref name="count"/>；
        /// 位置/速度都按世界单位与 世界单位/秒。**不分配**堆内存。
        /// </summary>
        public static void Step(Vector3[] positions, Vector3[] velocities, int count,
            BoidsSettings settings, Vector3 anchor, float dt)
        {
            if (positions == null || velocities == null || count <= 1 || dt <= 0f)
                return;

            if (count > positions.Length)
                count = positions.Length;
            if (count > velocities.Length)
                count = velocities.Length;

            float sepR2 = settings.SeparationRadius * settings.SeparationRadius;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = positions[i];
                Vector3 vel = velocities[i];

                Vector3 separation = Vector3.zero;
                Vector3 alignment = Vector3.zero;
                Vector3 cohesion = Vector3.zero;
                int neighbors = 0;

                for (int j = 0; j < count; j++)
                {
                    if (j == i)
                        continue;

                    Vector3 diff = pos - positions[j];
                    float d2 = diff.sqrMagnitude;
                    if (d2 > 4f)
                        continue;   // 超出"感知范围"直接忽略（2 单位）

                    neighbors++;

                    // 分离：越近排斥越强（d² 归一，d→0 时力度封顶）。
                    if (d2 < sepR2 && d2 > 1e-8f)
                        separation += diff / d2;

                    alignment += velocities[j];
                    cohesion += positions[j];
                }

                Vector3 force = Vector3.zero;

                if (separation.sqrMagnitude > 1e-8f)
                    force += separation.normalized * settings.SeparationWeight;

                if (neighbors > 0)
                {
                    Vector3 avgVel = alignment / neighbors;
                    if (avgVel.sqrMagnitude > 1e-8f)
                        force += avgVel.normalized * settings.AlignmentWeight;

                    Vector3 center = cohesion / neighbors;
                    Vector3 toCenter = center - pos;
                    if (toCenter.sqrMagnitude > 1e-8f)
                        force += toCenter.normalized * settings.CohesionWeight;
                }

                // 锚点回归：整群跟着一个缓慢移动的目标点走（否则会各自游散）。
                Vector3 toAnchor = anchor - pos;
                if (toAnchor.sqrMagnitude > 1e-8f)
                    force += toAnchor.normalized * settings.AnchorWeight;

                // 边界推回：靠近水体盒边界时给一个向内的力。
                force += BoundsForce(pos, settings) * settings.BoundsWeight;

                if (force.sqrMagnitude > settings.MaxForce * settings.MaxForce)
                    force = force.normalized * settings.MaxForce;

                vel += force * dt;

                float speed = vel.magnitude;
                if (speed > settings.MaxSpeed)
                    vel = vel * (settings.MaxSpeed / speed);
                else if (speed < settings.MinSpeed && speed > 1e-6f)
                    vel = vel * (settings.MinSpeed / speed);
                else if (speed <= 1e-6f)
                    vel = Vector3.forward * settings.MinSpeed;

                pos += vel * dt;
                pos = ClampToVolume(pos, settings);

                positions[i] = pos;
                velocities[i] = vel;
            }
        }

        /// <summary>水体盒边界的向内推力（各轴独立，越界则指向盒心）。</summary>
        public static Vector3 BoundsForce(Vector3 position, BoidsSettings settings)
        {
            Vector3 force = Vector3.zero;
            Vector3 d = position - settings.Center;
            Vector3 e = settings.Extents;

            force.x = AxisForce(d.x, e.x);
            force.y = AxisForce(d.y, e.y);
            force.z = AxisForce(d.z, e.z);
            return force;
        }

        static float AxisForce(float delta, float extent)
        {
            if (extent <= 1e-5f)
                return 0f;

            // 出界越多推力越大；只用很薄的一层软化带（0.75~1.0 倍半径）。
            float t = Mathf.Abs(delta) / extent;
            if (t < 0.75f)
                return 0f;

            float strength = (t - 0.75f) / 0.25f;
            return delta > 0f ? -strength : strength;
        }

        /// <summary>把点夹回水体盒（硬约束，保证鱼永远在水面以下、海床以上）。</summary>
        public static Vector3 ClampToVolume(Vector3 position, BoidsSettings settings)
        {
            Vector3 c = settings.Center;
            Vector3 e = settings.Extents;
            return new Vector3(
                Mathf.Clamp(position.x, c.x - e.x, c.x + e.x),
                Mathf.Clamp(position.y, c.y - e.y, c.y + e.y),
                Mathf.Clamp(position.z, c.z - e.z, c.z + e.z));
        }

        /// <summary>任意两两最小距离（单调测试用：不塌缩）。</summary>
        public static float MinPairwiseDistance(Vector3[] positions, int count)
        {
            if (positions == null || count < 2)
                return float.PositiveInfinity;

            if (count > positions.Length)
                count = positions.Length;

            float min = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    float d = Vector3.Distance(positions[i], positions[j]);
                    if (d < min)
                        min = d;
                }
            }

            return min;
        }

        /// <summary>质心。</summary>
        public static Vector3 Centroid(Vector3[] positions, int count)
        {
            if (positions == null || count <= 0)
                return Vector3.zero;

            if (count > positions.Length)
                count = positions.Length;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < count; i++)
                sum += positions[i];

            return sum / count;
        }

        /// <summary>
        /// 确定性初始化：用 <paramref name="rng"/> 在盒内散布位置与初速（测试与运行时共用，
        /// 保证"同一 seed 同一初始形态"）。
        /// </summary>
        public static void Seed(AmbientRandom rng, Vector3[] positions, Vector3[] velocities, int count,
            BoidsSettings settings, Vector3 anchor)
        {
            if (positions == null || velocities == null)
                return;

            if (count > positions.Length)
                count = positions.Length;

            Vector3 c = settings.Center;
            Vector3 e = settings.Extents;

            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector3(
                    c.x + rng.Range(-e.x * 0.7f, e.x * 0.7f),
                    c.y + rng.Range(-e.y * 0.6f, e.y * 0.6f),
                    c.z + rng.Range(-e.z * 0.7f, e.z * 0.7f));

                Vector3 toAnchor = anchor - positions[i];
                toAnchor.y = 0f;
                if (toAnchor.sqrMagnitude < 1e-6f)
                    toAnchor = Vector3.forward;

                velocities[i] = toAnchor.normalized * rng.Range(settings.MinSpeed, settings.MaxSpeed);
            }
        }
    }

    /// <summary>
    /// 岸边几何口径（纯 C#）：潮间带湿沙坡与"螃蟹/浮标该待在哪条水线上"。
    ///
    /// 【出处】湿沙坡由 <c>Assets/Editor/SceneArtBuilder.cs</c> 生成：竞技场矩形边界外
    /// 0 → 2.5 单位宽，高度从 <c>GroundTopY</c>(0) 线性降到 **-0.6**
    /// （<c>SceneArtBuilder.TideSlopeWidth</c> / 调用处的 <c>-0.6f</c>）。
    /// 本类只做同一口径的几何查询，供活物贴地/入水使用，**不改任何地形语义**。
    /// </summary>
    public static class AmbientShore
    {
        /// <summary>潮间带湿沙坡宽度（世界单位）。【依据 SceneArtBuilder.TideSlopeWidth】</summary>
        public const float TideSlopeWidth = 2.5f;

        /// <summary>潮间带外缘高度（世界 Y）。【依据 SceneArtBuilder 调用处的 -0.6f】</summary>
        public const float TideSlopeBottomY = -0.6f;

        /// <summary>距竞技场边界 <paramref name="offsetOutside"/> 处的湿沙坡地表高度（线性）。</summary>
        public static float TideSlopeY(float offsetOutside, float groundY = 0f,
            float width = TideSlopeWidth, float bottomY = TideSlopeBottomY)
        {
            if (width <= 1e-5f)
                return bottomY;

            float t = Mathf.Clamp01(offsetOutside / width);
            return Mathf.Lerp(groundY, bottomY, t);
        }

        /// <summary>
        /// 螃蟹默认待的水线高度：湿沙坡上"刚没过脚"的位置（离边界 <see cref="CrabShoreOffset"/>）。
        /// 【提案】0.8 单位 → 地表约 -0.19，正好在水面（-0.2）上下，读作"浅水区横爬"。
        /// </summary>
        public const float CrabShoreOffset = 0.8f;

        /// <summary>螃蟹身体中心的默认世界 Y（【提案】）。</summary>
        public static float CrabBodyY(float waterY, float groundY = 0f)
        {
            float surface = TideSlopeY(CrabShoreOffset, groundY);
            // 甲壳中心略高于地表（腿撑起来），且保证不低于水面 0.05（半身入水）。
            return Mathf.Max(surface + 0.055f, waterY + 0.05f);
        }

        /// <summary>浮标随波起伏的高度（水面附近 ±amplitude）。</summary>
        public static float CorkFloatY(float waterY, float phase, float amplitude)
        {
            return waterY + 0.02f + Mathf.Sin(phase) * amplitude;
        }
    }
}
