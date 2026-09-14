using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient
{
    /// <summary>
    /// 环境与活物（Ambient）模块的**纯 C# 规则层**：竞技场边界、禁飞区、风相位、预算与确定性随机。
    ///
    /// 【为什么全写成纯 C# 静态类】<c>GameObject</c> / <c>MonoBehaviour</c> 的实例化走原生 ECall，
    /// 脱离 Unity 运行时必抛 <c>SecurityException</c>（见 <c>external/m2-harness/README.md</c>）。
    /// 把"路径是否越界 / 是否闯入禁飞区 / 风相位是否连续 / 档位参数是否单调"这些
    /// **最容易出边界错误**的规则抽到这里，就能在无头验证台上秒级断言（<c>Assets/Tests/Ambient/</c>）。
    ///
    /// 【坐标口径】全部沿用 <c>docs/M2-3D空间模型对齐.md</c>：XZ 水平竞技场、重力 -Y、
    /// 地面顶面 y=0、水面 y=-0.2、1 单位 = 32px。本文件不新增任何坐标口径。
    ///
    /// 【标注】凡本模块自定的数值（数量上限、禁飞区尺寸、风力速度等）一律标【提案】，
    /// 因为 <c>docs/美术风格指南.md</c> / <c>docs/场景设计-战斗竞技场.md</c> 都未给活物数值。
    /// </summary>
    public static class AmbientBudget
    {
        // ------------------------------------------------------------------
        // 活物数量上限（【提案】：原版 Flash 与 Godot 版都没有"装饰活物"这一层，
        // 数值按时髦低模场景的常规密度取，并留出可调区间）
        // ------------------------------------------------------------------

        /// <summary>海鸥数量下限【提案】。</summary>
        public const int MinGulls = 2;

        /// <summary>海鸥数量上限【提案】。</summary>
        public const int MaxGulls = 4;

        /// <summary>海鸥默认数量【提案】（取上限与下限之间，兼顾"有生气"与"不抢戏"）。</summary>
        public const int DefaultGulls = 3;

        /// <summary>螃蟹数量下限【提案】。</summary>
        public const int MinCrabs = 2;

        /// <summary>螃蟹数量上限【提案】。</summary>
        public const int MaxCrabs = 3;

        /// <summary>螃蟹默认数量【提案】。</summary>
        public const int DefaultCrabs = 3;

        /// <summary>单群鱼的数量【提案】。</summary>
        public const int FishPerSchool = 14;

        /// <summary>单群鱼数量上限【提案】（boids 是 O(n²)，上限由此保证开销可控）。</summary>
        public const int MaxFishPerSchool = 20;

        /// <summary>灯笼数量上限【提案】。点光数量与 <c>美术风格指南</c> §8「方向光 ×1 + 无额外实时光」冲突，
        /// 见本模块交付报告的提案清单。</summary>
        public const int MaxLanterns = 3;

        // ------------------------------------------------------------------
        // 三角面 / DrawCall 预算（【提案】：按 <c>docs/场景设计-战斗竞技场.md</c> §8 的
        // ≤150k 三角面 / ≤250 DrawCall 总量反推，活物只占其中很小一份）
        // ------------------------------------------------------------------

        /// <summary>单只海鸥（躯干 + 双翼）三角面上限【提案】。</summary>
        public const int MaxGullTriangles = 160;

        /// <summary>单只螃蟹三角面上限【提案】（实测甲壳 + 8 步足 + 双眼柄 = 212）。</summary>
        public const int MaxCrabTriangles = 260;

        /// <summary>单条鱼三角面上限【提案】。</summary>
        public const int MaxFishTriangles = 40;

        /// <summary>单个灯笼（金属件）三角面上限【提案】（实测框架 + 顶盖 + 底座 + 提梁 = 84）。</summary>
        public const int MaxLanternTriangles = 100;

        /// <summary>全部活物 + 环境动效新增的三角面总上限【提案】（远低于 §8 的 150k）。</summary>
        public const int MaxAmbientTriangles = 4000;

        /// <summary>
        /// 全部活物 + 环境动效新增的 DrawCall 总上限【提案】（远低于 §8 的 ≤250）。
        /// 实际新增渲染器约 59：海鸥 3×3、螃蟹 3×3、鱼 14、灯笼 3×4、旗绳 2+1+6、
        /// 浮标 3、远景鸟 3；同类共享网格/材质 → 会被 SRP Batcher / 动态批处理压得更低。
        /// </summary>
        public const int MaxAmbientDrawCalls = 64;
    }

    /// <summary>
    /// 竞技场边界（纯 C# 值类型）。尺寸来自 <c>Data/LevelCatalog</c> 的关卡 `widthTiles × heightTiles`，
    /// 高度口径来自 <c>Battle/LevelGeometry</c>（地面顶面 <c>GroundTopY</c>、水面 <c>WaterSurfaceY</c>）。
    /// 本类型**只读**这些量，绝不改写地形语义或出生位。
    /// </summary>
    public readonly struct AmbientArena
    {
        /// <summary>竞技场宽度（世界 X，单位）。</summary>
        public readonly float Width;

        /// <summary>竞技场纵深（世界 Z，单位）。</summary>
        public readonly float Depth;

        /// <summary>水面高度（世界 Y，恒 -0.4；格 1→2 单位后由 -0.2 乘 2）。</summary>
        public readonly float WaterY;

        /// <summary>地面顶面高度（世界 Y，恒 0）。</summary>
        public readonly float GroundY;

        public AmbientArena(float width, float depth, float waterY, float groundY)
        {
            Width = width;
            Depth = depth;
            WaterY = waterY;
            GroundY = groundY;
        }

        /// <summary>按本工程固定高度口径构造（地面 y=0、水面 y=-0.4）。</summary>
        public static AmbientArena FromTiles(float widthTiles, float depthTiles)
        {
            return new AmbientArena(widthTiles, depthTiles, Battle.LevelGeometry.WaterSurfaceY, Battle.LevelGeometry.GroundTopY);
        }

        /// <summary>竞技场中心 X。</summary>
        public float CenterX => Width * 0.5f;

        /// <summary>竞技场中心 Z。</summary>
        public float CenterZ => Depth * 0.5f;

        /// <summary>XZ 是否落在矩形内（<paramref name="margin"/> 为正则外扩、为负则内缩）。</summary>
        public bool ContainsXZ(float x, float z, float margin)
        {
            return x >= -margin && x <= Width + margin
                && z >= -margin && z <= Depth + margin;
        }

        /// <summary>把 XZ 夹回矩形内（带外扩边距）。用于把活物的巡逻路径钉在合法范围内。</summary>
        public Vector3 ClampXZ(Vector3 position, float margin)
        {
            return new Vector3(
                Mathf.Clamp(position.x, -margin, Width + margin),
                position.y,
                Mathf.Clamp(position.z, -margin, Depth + margin));
        }

        /// <summary>把 XZ 夹到矩形**外**侧（用于"只在竞技场外水域活动"的活物，如螃蟹/鱼群）。</summary>
        public Vector3 PushOutsideXZ(Vector3 position, float margin)
        {
            if (!ContainsXZ(position.x, position.z, margin))
                return position;

            // 矩形内 → 找四条边里最近的一条推出去。
            float left = Mathf.Abs(position.x - (-margin));
            float right = Mathf.Abs(Width + margin - position.x);
            float back = Mathf.Abs(position.z - (-margin));
            float front = Mathf.Abs(Depth + margin - position.z);

            float best = left;
            float x = -margin - 1e-4f;
            float z = position.z;
            if (right < best) { best = right; x = Width + margin + 1e-4f; z = position.z; }
            if (back < best) { best = back; x = position.x; z = -margin - 1e-4f; }
            if (front < best) { x = position.x; z = Depth + margin + 1e-4f; }

            return new Vector3(x, position.y, z);
        }
    }

    /// <summary>
    /// 「投掷视线中央区域」禁飞区（可读性红线，任务书 §1 + <c>docs/场景设计-战斗竞技场.md</c> §9.3）。
    ///
    /// 【定义】一个轴对齐长方体：屏幕中央那条竖直柱体 —— X ∈ [中心 − 半宽, 中心 + 半宽]、
    /// Z ∈ [竞技场两端]、Y ≤ 天花板。低空活物（海鸥俯冲、鱼跃）不得进入；
    /// 高于天花板的盘旋不受限（它已完全离开玩家瞄准的屏幕中带）。
    ///
    /// 【为什么用世界空间盒而不是屏幕空间判定】屏幕空间判定依赖相机（可环绕/缩放），
    /// 会让"禁飞"随相机变化而失效，且无法在无头测试里断言；世界空间盒是相机无关的保守代理。
    /// </summary>
    public readonly struct AmbientNoFlyZone
    {
        /// <summary>禁飞区 X 下界。</summary>
        public readonly float MinX;

        /// <summary>禁飞区 X 上界。</summary>
        public readonly float MaxX;

        /// <summary>禁飞区 Z 下界。</summary>
        public readonly float MinZ;

        /// <summary>禁飞区 Z 上界。</summary>
        public readonly float MaxZ;

        /// <summary>禁飞区高度天花板：Y ≤ 此值且在 XZ 盒内即视为闯入。</summary>
        public readonly float CeilingY;

        public AmbientNoFlyZone(float minX, float maxX, float minZ, float maxZ, float ceilingY)
        {
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
            CeilingY = ceilingY;
        }

        /// <summary>中央走廊半宽【提案】：相机 45°/距离 18/FOV60 下屏幕宽约 37 单位，
        /// 中央约 1/3 屏宽 ≈ 12 单位，取半宽 6 更保守（宁可鸟飞偏一点，不许进瞄准带）。</summary>
        public const float DefaultCorridorHalfWidth = 6f;

        /// <summary>禁飞天花板【提案】：投掷抛物线顶点通常 < 6 单位（Flash 满力抛射高度量级），
        /// 取 9 留出余量，鸟要么在 9 以上、要么在中央走廊之外。</summary>
        public const float DefaultCeilingY = 9f;

        /// <summary>按竞技场构造：X 取中心 ± 半宽，Z 覆盖整个竞技场纵深。</summary>
        public static AmbientNoFlyZone ForArena(AmbientArena arena,
            float corridorHalfWidth = DefaultCorridorHalfWidth, float ceilingY = DefaultCeilingY)
        {
            return new AmbientNoFlyZone(
                arena.CenterX - corridorHalfWidth,
                arena.CenterX + corridorHalfWidth,
                -1f,
                arena.Depth + 1f,
                ceilingY);
        }

        /// <summary>点是否闯入禁飞区（Y ≤ 天花板且 XZ 在盒内）。</summary>
        public bool Contains(float x, float y, float z)
        {
            return y <= CeilingY
                && x >= MinX && x <= MaxX
                && z >= MinZ && z <= MaxZ;
        }

        /// <summary>点是否闯入禁飞区。</summary>
        public bool Contains(Vector3 p)
        {
            return Contains(p.x, p.y, p.z);
        }

        /// <summary>
        /// 把点推离禁飞区：在三种逃逸方式（抬到天花板上方 / 向左推出 / 向右推出）里取位移最小的一种。
        /// 结果**保证**不在禁飞区内（<see cref="Contains(Vector3)"/> 为 false）——测试逐点断言这条不变量。
        /// </summary>
        public Vector3 ClampOut(Vector3 p, float clearance = 0.05f)
        {
            if (!Contains(p))
                return p;

            float upDy = (CeilingY + clearance) - p.y;

            float leftX = MinX - clearance;
            float leftDx = p.x - leftX;

            float rightX = MaxX + clearance;
            float rightDx = rightX - p.x;

            Vector3 best = new Vector3(p.x, CeilingY + clearance, p.z);
            float bestCost = upDy * upDy;

            if (leftDx * leftDx < bestCost)
            {
                bestCost = leftDx * leftDx;
                best = new Vector3(leftX, p.y, p.z);
            }

            if (rightDx * rightDx < bestCost)
            {
                best = new Vector3(rightX, p.y, p.z);
            }

            return best;
        }
    }

    /// <summary>
    /// 风相位规则（纯 C#）：植被/旗帜顶点摆动的相位、阵风包络与"离根越远摆幅越大"的权重。
    ///
    /// 【为什么和 shader 各写一份】顶点位移必须在 GPU 上算（否则整片合并网格要每帧 CPU 重写顶点）。
    /// 本类是 <c>Assets/Art/Shaders/Ambient/PirateAmbientWind.shader</c> 里 HLSL 公式的**等价 C# 副本**，
    /// 逐行对应（含常数），用途有两个：
    ///   ① 无头测试可以直接断言"位移有界、相位连续、根部权重为下限而不是 0"；
    ///   ② 将来 shader 调参时，这里的公式是回归基线（改一处必须同步另一处，注释已互指）。
    /// </summary>
    public static class WindRules
    {
        /// <summary>基准角速度（弧度/秒）【提案】：0.55 → 主摆周期约 11.4 秒，读作"海风缓推"。</summary>
        public const float BaseSpeed = 0.55f;

        /// <summary>阵风包络下限（对应 shader 的 <c>0.70 - 0.30</c>）【提案】。</summary>
        public const float GustMin = 0.40f;

        /// <summary>阵风包络上限（对应 shader 的 <c>0.70 + 0.30</c>）【提案】。</summary>
        public const float GustMax = 1.00f;

        /// <summary>默认摆幅下限（矮植被也保留的摆动比例）【提案】：0.35 让草丛在合并网格里仍可见地摆。</summary>
        public const float DefaultSwayFloor = 0.35f;

        /// <summary>
        /// 相位 = 时间 × 角速度 + 空间偏移。空间偏移让相邻植株不同步（同坐标相位差为 0）。
        /// 【连续性】对任意 dt→0，相位增量 → 0，不存在跳变。
        /// </summary>
        public static float Phase(float time, float angularSpeed, float offset)
        {
            return offset + time * angularSpeed;
        }

        /// <summary>由世界坐标派生相位偏移（与 shader 的 <c>(x + z*0.73) * density</c> 同式）。</summary>
        public static float SpatialPhase(float worldX, float worldZ, float density)
        {
            return (worldX + worldZ * 0.73f) * density;
        }

        /// <summary>
        /// 阵风包络（与 shader 同式）：<c>0.70 + 0.30·sin(phase·0.31)</c>，范围
        /// [<see cref="GustMin"/>, <see cref="GustMax"/>]，**恒为正**（不会被反向吹）。
        /// 两个不同频率正弦叠加只是为了让包络不呈标准周期，仍是连续可导函数。
        /// </summary>
        public static float GustEnvelope(float phase)
        {
            return 0.70f + 0.30f * Mathf.Sin(phase * 0.31f);
        }

        /// <summary>
        /// 顶点摆幅权重：
        /// <paramref name="weightDirection"/> &gt; 0 → 以 <paramref name="anchorY"/> 为根、向上权重从 0 增到 1（树/草）；
        /// &lt; 0 → 以 <paramref name="anchorY"/> 为悬挂点、向下权重从 0 增到 1（旗/帆/垂下的缆绳）。
        /// 权重取平方，让根部比末梢安静得多（否则整株平移像"滑步"）。
        /// </summary>
        public static float SwayWeight(float worldY, float anchorY, float scale, float weightDirection)
        {
            if (scale <= 1e-5f)
                return 0f;

            float t = weightDirection >= 0f
                ? (worldY - anchorY) / scale
                : (anchorY - worldY) / scale;

            t = Mathf.Clamp01(t);
            return t * t;
        }

        /// <summary>带下限的摆幅权重（与 shader 的 <c>_WindFloor</c> 同式）。</summary>
        public static float SwayWeightWithFloor(float worldY, float anchorY, float scale,
            float weightDirection, float floor)
        {
            float f = Mathf.Clamp01(floor);
            return f + (1f - f) * SwayWeight(worldY, anchorY, scale, weightDirection);
        }

        /// <summary>
        /// 一维摆动量：低频主摆 + 次摆 + 高频抖动（与 shader 同式）。
        /// </summary>
        public static float Sway(float phase, float flutter)
        {
            float main = Mathf.Sin(phase);
            float secondary = 0.35f * Mathf.Sin(phase * 2.7f + 1.3f);
            float high = flutter * Mathf.Sin(phase * 5.3f + 2.1f);
            return main + secondary + high;
        }

        /// <summary>
        /// 完整顶点位移（shader 的等价 C# 实现）。返回世界 XZ 偏移量。
        /// 位移**有界**：|offset| ≤ strength × (1 + 0.35 + flutter) —— 测试断言这条，防止改参数把植被吹飞。
        /// </summary>
        public static Vector3 Displacement(Vector3 worldPosition, float anchorY, float weightScale,
            float weightDirection, float swayFloor, float strength, float flutter,
            float angularSpeed, float density, float time, Vector2 windDirectionXZ)
        {
            float phase = Phase(time, angularSpeed, SpatialPhase(worldPosition.x, worldPosition.z, density));
            float gust = GustEnvelope(phase);
            float sway = Sway(phase, flutter);
            float weight = SwayWeightWithFloor(worldPosition.y, anchorY, weightScale, weightDirection, swayFloor);

            float magnitude = sway * strength * gust * weight;

            Vector2 dir = windDirectionXZ;
            float len = dir.magnitude;
            dir = len > 1e-4f ? dir / len : Vector2.right;

            return new Vector3(dir.x * magnitude, 0f, dir.y * magnitude);
        }

        /// <summary>位移的理论上界（供测试与调参用）：strength × (1 + 0.35 + flutter) × GustMax。</summary>
        public static float MaxDisplacement(float strength, float flutter)
        {
            return strength * (1f + 0.35f + Mathf.Max(0f, flutter)) * GustMax;
        }
    }

    /// <summary>
    /// 环境模块用到的确定性伪随机（纯 C#，跨 Mono/.NET 完全一致）。
    ///
    /// 【为什么不复用 <c>SceneArt.SceneArtRandom</c>】两个模块各有一份同构的小 LCG 是本工程
    /// 刻意的取舍：跨模块引用一个"只有 5 个方法"的工具会把两个模块的生命周期绑在一起
    /// （SceneArt 正在并行开发，其 API 变动不应波及 Ambient）。算法与常数与 SceneArtRandom 完全一致，
    /// 便于将来合并。**不参与任何玩法数值**（弹道/伤害/AI 都不读它）。
    /// </summary>
    public sealed class AmbientRandom
    {
        uint _state;

        /// <summary>以固定种子构造。</summary>
        public AmbientRandom(int seed)
        {
            _state = (uint)(seed * 2654435761u) ^ 0x9E3779B9u;
            if (_state == 0u)
                _state = 0xA341316Cu;
        }

        /// <summary>下一个 uint。</summary>
        public uint NextUInt()
        {
            _state = _state * 1664525u + 1013904223u;
            return _state;
        }

        /// <summary>[0,1) 浮点。</summary>
        public float Next01()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>[min,max) 浮点。</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * Next01();
        }

        /// <summary>[min,max) 整数。</summary>
        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }
    }
}
