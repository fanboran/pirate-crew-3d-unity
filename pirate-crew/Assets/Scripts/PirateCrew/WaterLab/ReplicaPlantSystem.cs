using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.WaterLab
{
    // =========================================================================
    // ReplicaPlantSystem —— Sandcastle 植物 imposter 粒子系统移植（I6a）。
    //
    // 翻译源（全部第一手，行号引用见各成员注释）：
    //   * 摆放参数  external/sandcastle-ref/rt/plants-placement.json（13 组实例字段
    //     + 材质 pivotOffset/shadowDepthBiais/atlasMap_ST 裸字节直读）
    //     → <see cref="ReplicaPlantTable"/> 静态表（单源，契约测试对拍口径）。
    //   * 分布     rt/out/Plant/v0_Distribution_0.hlslasm（PCG 拒绝采样，逐指令核对）。
    //   * 生根更新 rt/out/Plant/v0_Update_0.hlslasm（水场推力/重锚定/回正，语义移植；
    //     姿态动力学系数 G-plants.md §6 自标未决，逐指令二次核对 TODO）。
    //   * 渲染     Assets/Art/Shaders/WaterLab/ReplicaPlant.shader（Lit/Plant 直译），
    //     挂接 = Graphics.DrawMeshInstancedIndirect（原作 Particle.Draw 同机制）。
    //
    // 【I6a 边界】只做资产+shader+运行时系统本体；场景装配（13 挂点/材质/凸包网格
    // 生成、fac 真值）由 I6b 接（G-plants.md §5 规格第 3/4 条）。
    // =========================================================================
    public sealed class ReplicaPlantSystem : IDisposable
    {
        // ---------------------------------------------------------------------
        // 48B 粒子（Plant.cs : Particle.cs，G-plants.md §3.1：PlantParticle 48B
        // = rotation4 / position3+size / velocity3+age；sizeof=48 与 compute stride 一致）
        // ---------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        public struct PlantParticle
        {
            public Vector4 rotation;   // 偏移 0
            public Vector3 position;   // 偏移 16（y 在 20，Update 只写回这一格）
            public float size;         // 偏移 28
            public Vector3 velocity;   // 偏移 32
            public float age;          // 偏移 44
        }

        // ---------------------------------------------------------------------
        // 真值常量（反汇编字面量，出处标注）
        // ---------------------------------------------------------------------

        /// <summary>沙滩世界 XZ 尺寸（Distribution L121 cb0[116].xz；B 流 Beach.bounds 30×10×30）。</summary>
        public const float BeachSize = WaterLabRules.DomainSize; // 30

        /// <summary>水面深度门（Update L231 add −0.05；= WaterLabRules.DryDepth）。</summary>
        public const float WaterDepthOffset = 0.05f;

        /// <summary>brush 径向衰减（Update L238 字面量 2.164042）。</summary>
        public const float BrushFalloff = 2.164042f;

        /// <summary>速度每帧阻尼（Update L246 字面量 0.97）。</summary>
        public const float VelocityDamping = 0.97f;

        // shader 全局名（ReplicaPlant.shader 声明同名 uniform）
        static readonly int IdBoundsSize = Shader.PropertyToID("_WaterLabPlantBoundsSize");
        static readonly int IdAmbient = Shader.PropertyToID("_WaterLabPlantAmbient");
        static readonly int IdPlants = Shader.PropertyToID("_Plants");
        static readonly int IdAliveList = Shader.PropertyToID("_AliveList");

        // ---------------------------------------------------------------------
        // 注入依赖（I6b 装配时给；给 null 时用 WaterLabReplicaSnapshot 默认实现）
        // ---------------------------------------------------------------------

        /// <summary>
        /// 地面场（原作 groundCells 1024² 结构缓冲：格心整数索引、p=u·res−0.5 双线性；
        /// 通道：24bit 高度 ×1e-6 / 12bit 沙 ×0.002466 / 切线 2 字节 (b/255)·2−1 / 肥力 6bit ÷63，
        /// 解码见 v0_Distribution_0.hlslasm L67-99）。
        /// </summary>
        public interface IGroundField
        {
            int Resolution { get; }
            /// <summary>取一格（格心整数索引；越界 clamp 由内核结构负责）。</summary>
            void GetCell(int cx, int cz, out float height, out float fertility, out float tangentX, out float tangentZ);
        }

        /// <summary>水场（原作 waterCells 512²：偏移 8 的 24bit 水面高 ×1e-6；平流 16bit 域本版禁用）。</summary>
        public interface IWaterField
        {
            int Resolution { get; }
            float GetHeight(int cx, int cz);
        }

        readonly IGroundField _ground;
        readonly IWaterField _water;

        // ---------------------------------------------------------------------
        // 参数与渲染句柄（I6b 装配注入）
        // ---------------------------------------------------------------------
        readonly ReplicaPlantParams _p;
        readonly Mesh _hull;
        readonly Material _material;

        /// <summary>DrawMeshInstancedIndirect 剔除包围盒（默认 = B 流 Beach.bounds）。</summary>
        public Bounds DrawBounds = new Bounds(new Vector3(0f, 5f, 0f), new Vector3(30f, 10f, 30f));

        // ---------------------------------------------------------------------
        // 粒子态与 GPU 缓冲（Plant.Setup 生命周期，G §3.1）
        // ---------------------------------------------------------------------
        readonly PlantParticle[] _plants;
        readonly int[] _aliveList;
        readonly int[] _deadList;
        readonly uint[] _args = new uint[5];   // DrawIndirect 20B：indexCount, instanceCount, 0, 0, 0
        int _deadCount;                        // deadCounter 初值 = capacity（Plant.Setup）
        int _aliveCount;

        GraphicsBuffer _plantBuf;    // StructuredBuffer<float4> ×3/株
        GraphicsBuffer _aliveBuf;    // StructuredBuffer<uint>
        GraphicsBuffer _argsBuf;     // IndirectArguments

        /// <summary>当前存活株数（= instanceCount）。</summary>
        public int AliveCount => _aliveCount;

        /// <summary>
        /// 分布种子（Distribution L29 cb0[157].z）。运行时专用 TODO：原作由引擎每实例
        /// SetFloat 上传，Cpp2IL 丢失（G-plants.md §6）——恒等化走 frida 补读；当前 0。
        /// </summary>
        public uint DistributionSeed;

        /// <summary>
        /// 哈希种子第二分量（Distribution L28 cb0[113].x）。运行时专用 TODO（同上）——当前 0。
        /// </summary>
        public uint SeedExtra;

        /// <summary>
        /// brush 交互力（Update L235-241 cb0[119] 圆心 / cb0[120] 力矢量）。运行时专用 TODO：
        /// 引擎每帧上传，主菜单静态帧疑恒零（G §6）——默认零。
        /// </summary>
        public Vector3 BrushCenter;
        public Vector3 BrushForce;

        /// <summary>
        /// cb0[40] 环境色（shader _WaterLabPlantAmbient）。运行时专用 TODO：引擎上传，
        /// seg05-07 的逐格环境色依赖 Gi/地形色调图（E/I3 域）——主菜单值抓帧定（G §6），
        /// 解不出不造值；未设置时 shader 侧为 0（黑），装配验收前必须补。
        /// </summary>
        public static Vector4? AmbientOverride;

        public ReplicaPlantSystem(ReplicaPlantParams p, Mesh hull, Material material,
                                  IGroundField ground = null, IWaterField water = null)
        {
            _p = p;
            _hull = hull;
            _material = material;
            _ground = ground ?? new SnapshotGroundField();
            _water = water ?? new SnapshotWaterField();

            _plants = new PlantParticle[p.Capacity];
            _aliveList = new int[p.Capacity];
            _deadList = new int[p.Capacity];
            for (int i = 0; i < p.Capacity; i++)
            {
                _aliveList[i] = -1;    // Initialization kernel（336B）：aliveList=−1
                _deadList[i] = i;      // deadList=identity
            }
            _deadCount = p.Capacity;   // deadCounter 初值 = capacity

            _plantBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, p.Capacity, 48);
            _aliveBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, p.Capacity, 4);
            _argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint));

            // 材质缓冲绑定（Particle.Init：material.SetBuffer("plants"/"aliveList")，G §1/§3.1）
            _material.SetBuffer(IdPlants, _plantBuf);
            _material.SetBuffer(IdAliveList, _aliveBuf);
            _material.SetVector("pivotOffset", p.PivotOffset);
            _material.SetFloat("shadowDepthBiais", p.ShadowDepthBiais);
            PublishGlobals();
        }

        /// <summary>
        /// 幂等下发全局（Beach 尺寸真值 = B 流 bounds 30×10×30；环境色见 AmbientOverride TODO）。
        /// </summary>
        public static void PublishGlobals()
        {
            Shader.SetGlobalVector(IdBoundsSize, new Vector4(BeachSize, 10f, BeachSize, 0f));
            if (AmbientOverride.HasValue)
                Shader.SetGlobalVector(IdAmbient, AmbientOverride.Value);
        }

        // =====================================================================
        // 分布（v0_Distribution_0.hlslasm 逐指令移植）
        // 原作按线程组 Dispatch：groups = ceil(capacity·fac/1024)（Plant.Distribution，
        // G §3.1），每线程一次尝试。fac = obj5·1e-4 运行时专用（Cpp2IL 丢失，G §6）——
        // TODO(I6b/对拍)：以实测存活株数定 fac/尝试数；当前全容量尝试。
        // =====================================================================
        public int Distribute(int attempts)
        {
            int res = _ground.Resolution;
            for (int tid = 0; tid < attempts; tid++)
            {
                // L28-29：seed = tid + cb0[113].x + cb0[157].z
                uint h1 = PcgHash((uint)tid + SeedExtra + DistributionSeed);

                // L30-48：u1 = decode(PcgHash(PcgHash(seed)))；L57：bitcast([1,2))−1
                float u1 = Hash01(PcgHash(h1));
                // L49-56：u2 = decode(PcgTail(h1 ^ 0x005b5c68))——此路无前缀步（编译器折叠）
                float u2 = Hash01(PcgTail(h1 ^ 0x005b5c68u));

                // 第一次双线性（L58-111）：p = u·res − 0.5 → 格心 floor/frac → 高度/肥力
                BilinearGround(u1 * res - 0.5f, u2 * res - 0.5f, res,
                               out float hgt, out float frt, out _, out _);

                // 门控 [min,max)（L112-118：lt min<h; lt h<max；肥力同）
                if (!(hgt >= _p.GroundHeightRange.x && hgt < _p.GroundHeightRange.y &&
                      frt >= _p.FertilityRange.x && frt < _p.FertilityRange.y))
                    continue;

                // 世界位（L120-121）：(bitcast−1.5)·size = (u−0.5)·BeachSize，中心对齐
                float wx = (u1 - 0.5f) * BeachSize;
                float wz = (u2 - 0.5f) * BeachSize;

                // 死环槽位（L214-228）：slot = deadList[−−deadCounter]，下溢守卫 exch 0
                if (_deadCount <= 0)
                    continue;
                int slot = _deadList[--_deadCount];

                // yaw/size 同源 u（L229-250）：decode(PcgHash(PcgHash(slot)))——**与偏航
                // 同一个 u**（L251 sincos 与 L265 lerp 共用 r0.z，反汇编核对），非独立两路
                float uSlot = Hash01(PcgHash(PcgHash((uint)slot)));

                // 第二次双线性（L122-207）：u' = sat(world/size + 0.5) 重走 cell↔world，
                // 取该处的切线/高度/肥力（存储值用第二次的，门控用第一次的）
                BilinearGround(
                    Mathf.Clamp01(wx / BeachSize + 0.5f) * res - 0.5f,
                    Mathf.Clamp01(wz / BeachSize + 0.5f) * res - 0.5f,
                    res, out float hgt2, out float frt2, out float tx, out float tz);

                // 切线对齐四元数（L202-213 + L254-258 逐寄存器核对）：
                //   ny = √(1−|t|²)；qA = normalize(Z, 0, −(T+Z), 1+Z+ny)
                //   （注意**不是** FromToRotation(up,n)——原作公式带 T+Z 交叉项，照抄。
                //   Min 钳制只在 |t|²>1 的越域数据防 NaN；原作直接 sqrt，数据域保证不触）
                float t2 = Mathf.Min(1f, tx * tx + tz * tz);
                float ny = Mathf.Sqrt(1f - t2);
                Vector4 qA = new Vector4(tz, 0f, -(tx + tz), 1f + tz + ny);
                qA /= Mathf.Sqrt(qA.x * qA.x + qA.y * qA.y + qA.z * qA.z + qA.w * qA.w);

                // 偏航四元数（L251-253）：(0, sin(u·π), 0, cos(u·π))——u·π 作四元数半角，
                // 实际旋转角 2π·u ∈ [0,2π)；复合顺序 = q_align∘yaw（L259-263 寄存器核对）
                float ang = uSlot * Mathf.PI;
                var qYaw = new Quaternion(0f, Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                var qAlign = new Quaternion(qA.x, qA.y, qA.z, qA.w);
                Quaternion q = qAlign * qYaw;

                // size（L264-272）：lerp(sizeRange, uSlot)·(肥力归一)^0.2（log·0.2·exp=五次根）
                float size = Mathf.Lerp(_p.SizeRange.x, _p.SizeRange.y, uSlot);
                float fn = Mathf.Clamp01((frt2 - _p.FertilityRange.x) /
                                         Mathf.Max(1e-8f, _p.FertilityRange.y - _p.FertilityRange.x));
                size *= Mathf.Exp(Mathf.Log(fn) * 0.2f);   // fn=0 → 0（与原作 exp(−inf) 一致）

                // alive 挂接（L222-228）：aliveList[atomicAdd(args[1])] = slot，越界 clamp
                if (_aliveCount < _p.Capacity)
                    _aliveList[_aliveCount++] = slot;
                else
                    _aliveCount = _p.Capacity;

                // 写粒子（L276-279）：{q, (wx, hgt2, wz), size, 0, 0}——y 取第二次双线性高
                _plants[slot].rotation = new Vector4(q.x, q.y, q.z, q.w);
                _plants[slot].position = new Vector3(wx, hgt2, wz);
                _plants[slot].size = size;
                _plants[slot].velocity = Vector3.zero;
                _plants[slot].age = 0f;
            }
            Upload();
            return _aliveCount;
        }

        /// <summary>全容量尝试的便捷重载（fac 真值落地前的口径，见 Distribute 注释）。</summary>
        public int Distribute() => Distribute(_p.Capacity);

        // ---- PCG hash（v0_Distribution_0.hlslasm L30-55，逐指令）----
        // hashA(v)：前缀 (^61, ^>>16) + 尾 (·9, ^>>4, ·0x27d4eb2d, ^>>15)
        static uint PcgHash(uint v)
        {
            uint t = (v ^ 61u) ^ (v >> 16);
            t *= 9u;
            t ^= t >> 4;
            t *= 0x27d4eb2du;
            t ^= t >> 15;
            return t;
        }

        // 尾段独立用（u2 路 L49-55：盐 0x005b5c68 直接进尾段，无前缀）
        static uint PcgTail(uint t)
        {
            t *= 9u;
            t ^= t >> 4;
            t *= 0x27d4eb2du;
            t ^= t >> 15;
            return t;
        }

        // L48/L56 bfi(23,0) + L57：bitcast(0x3f800000 | (h & 0x7fffff)) − 1 ∈ [0,1)
        static float Hash01(uint h)
        {
            return BitConverter.Int32BitsToSingle((int)(0x3f800000u | (h & 0x7fffffu))) - 1f;
        }

        /// <summary>格心双线性（L58-111 结构）：floor/frac → 四角 clamp → 通道各自 lerp。</summary>
        void BilinearGround(float px, float pz, int res,
                            out float height, out float fertility, out float tx, out float tz)
        {
            int x0 = Mathf.FloorToInt(px), z0 = Mathf.FloorToInt(pz);
            float fx = px - x0, fz = pz - z0;
            int x1 = Mathf.Clamp(x0 + 1, 0, res - 1), z1 = Mathf.Clamp(z0 + 1, 0, res - 1);
            x0 = Mathf.Clamp(x0, 0, res - 1);
            z0 = Mathf.Clamp(z0, 0, res - 1);

            _ground.GetCell(x0, z0, out float h00, out float f00, out float tx00, out float tz00);
            _ground.GetCell(x1, z0, out float h10, out float f10, out float tx10, out float tz10);
            _ground.GetCell(x0, z1, out float h01, out float f01, out float tx01, out float tz01);
            _ground.GetCell(x1, z1, out float h11, out float f11, out float tx11, out float tz11);

            height = Bilerp(h00, h10, h01, h11, fx, fz);
            fertility = Bilerp(f00, f10, f01, f11, fx, fz);
            tx = Bilerp(tx00, tx10, tx01, tx11, fx, fz);
            tz = Bilerp(tz00, tz10, tz01, tz11, fx, fz);
        }

        static float Bilerp(float a, float b, float c, float d, float fx, float fz)
        {
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        // =====================================================================
        // 生根更新（v0_Update_0.hlslasm 语义移植；行号为该文件行序）
        // 每帧：if (tid >= args[1]) return; idx = aliveList[tid]; if (idx==−1) return（L27-36）。
        // 【根系不动 xz】全程只写 rotation(+0)/position.y(+20)/velocity+age(+32)（L274-276）。
        // =====================================================================
        public void Tick(float dt)
        {
            int res = _ground.Resolution;
            int wres = _water.Resolution;
            for (int i = 0; i < _aliveCount; i++)
            {
                int idx = _aliveList[i];
                if (idx < 0) continue;
                PlantParticle p = _plants[idx];

                // ---- 地面重采（L40-131）：切线 T/Z + 高度24bit + 沙12bit（×0.002466 通道，
                //      我方场无此通道——默认实现并入 height，对账 TODO）----
                BilinearGround(
                    Mathf.Clamp01(p.position.x / BeachSize + 0.5f) * res - 0.5f,
                    Mathf.Clamp01(p.position.z / BeachSize + 0.5f) * res - 0.5f,
                    res, out float groundH, out float _, out float tx, out float tz);

                // ---- 水场重采（L132-201）：水面高；平流场 16bit 域本版 ×0 字面量禁用 →
                //      解码恒 (−4,−4)（L142-148 mad …, l(0.0), l(−4.0)），照抄 ----
                float waterH = SampleWaterBilinear(p.position.x, p.position.z, wres);

                // ---- 水深门（L231-232）：max(waterH − 0.05, 0)。
                //      【对账 TODO】任务书/G §3.3 记作 max(waterH−0.05−groundH, 0)，反汇编
                //      无 −groundH 项——语义差源于原作 waterCells 存水面高还是水深，L2 对拍闭 ----
                float depth = Mathf.Max(waterH - WaterDepthOffset, 0f);

                // ---- 平流推力（L233）：depth·flow，flow 恒 (−4,−4)（本版真值）----
                float thrustX = depth * -4f;
                float thrustZ = depth * -4f;

                // ---- brush 径向脉冲（L235-241）：exp(−2.164·d)·force·dt ----
                Vector3 d = p.position - BrushCenter;
                float imp = Mathf.Exp(-BrushFalloff * d.magnitude) * dt;

                // ---- 切线回正（L202-234）：目标四元数公式与 Distribution 相同
                //      qUp = normalize(Z, 0, −(T+Z), 1+Z+ny)；姿态误差进速度（L234/245）----
                float t2 = Mathf.Min(1f, tx * tx + tz * tz);
                float ny2 = Mathf.Sqrt(1f - t2);
                Vector4 qUp = new Vector4(tz, 0f, -(tx + tz), 1f + tz + ny2);
                qUp /= Mathf.Sqrt(qUp.x * qUp.x + qUp.y * qUp.y + qUp.z * qUp.z + qUp.w * qUp.w);

                // 旧旋转的“立轴”向量（L208-215 逐寄存器：rotated-up 的 (z,x) 分量 + 1）
                Quaternion q = new Quaternion(p.rotation.x, p.rotation.y, p.rotation.z, p.rotation.w);
                float qx = q.x, qy = q.y, qz = q.z, qw = q.w;
                Vector3 axis = new Vector3(
                    2f * (qy * qz + qw * qx),
                    2f * (qx * qy - qw * qz),
                    1f - 2f * qx * qx - 2f * qx * qz);

                // 速度更新（L242-246）：vel = 0.97·(vel + 2·brushImp·force + (thrust,0,thrust) + attErr)
                Vector3 attErr = new Vector3(qUp.x - axis.y, qUp.y - axis.z, qUp.z - axis.x); // L234
                Vector3 vel = p.velocity;
                vel += 2f * imp * BrushForce;                                  // L242（×2 字面量）
                vel += new Vector3(thrustX, 0f, thrustZ);                      // L243-244
                vel += attErr;                                                 // L245
                vel *= VelocityDamping;                                        // L246

                // ---- 姿态回正（L247-270）：由阻尼速度+立轴构造最短弧四元数右乘。
                //      TODO(逐指令二次核对，G §6 自标未决)：swizzle 序已核，混合权重语义
                //      （为何进速度通道）动态验收前由 L2 抓帧闭 ----
                Vector3 disp = vel * dt + new Vector3(axis.y, axis.z, axis.x); // L247（r6.yzx）
                float mag = disp.magnitude;
                if (mag > 1e-12f)
                {
                    Vector3 n = disp / mag;                                    // L248-250
                    Vector3 rc = new Vector3(                                  // L251-252
                        axis.z * n.z - n.y * axis.x,
                        axis.x * n.x - n.z * axis.y,
                        axis.y * n.y - n.x * axis.z);
                    float rw = 1f + (axis.y * n.x + axis.z * n.y + axis.x * n.z); // L253-254
                    Quaternion delta;
                    if (rw < 1e-6f)                                            // L255 退化
                    {
                        Vector3 ax = Mathf.Abs(axis.y) < 0.9f                  // L256-257 轴回退
                            ? new Vector3(0f, 0f, 1f)
                            : new Vector3(1f, 0f, 0f);
                        Vector3 c = new Vector3(                               // L258-259
                            axis.z * ax.y - ax.x * axis.x,
                            axis.x * ax.z - ax.y * axis.y,
                            axis.y * ax.x - ax.z * axis.z);
                        delta = new Quaternion(c.x, c.y, c.z, 0f);
                    }
                    else
                    {
                        float inv = 1f / Mathf.Sqrt(
                            rc.x * rc.x + rc.y * rc.y + rc.z * rc.z + rw * rw); // L260-262
                        delta = new Quaternion(rc.x * inv, rc.y * inv, rc.z * inv, rw * inv);
                    }
                    q = q * delta;                                             // L264-270 q_old∘delta
                }

                // ---- 写回（L271-276）：age += dt；y 重锚定 = min(max(沙12bit, 地24bit), 旧y)
                //      ——【对账 TODO】单调下沉（沙/地抬面不超旧 y）；任务书/G §3.3 的
                //      “max(ground,water) 压抬”在反汇编中不成立（水不进 y 通道）----
                p.rotation = new Vector4(q.x, q.y, q.z, q.w);
                p.age += dt;                                                   // L271
                // y 重锚定：原作 = min(max(沙12bit·0.002466, 地24bit), 旧y)（L272-273）；
                // 我方场暂无沙12bit 通道 → 并入 height（TODO：I7 产全通道场后补第二通道）
                float yNew = Mathf.Min(groundH, p.position.y);
                p.position = new Vector3(p.position.x, yNew, p.position.z);    // L274-275（xz 不动）
                p.velocity = vel;                                              // L276
                _plants[idx] = p;
            }
            Upload();
        }

        float SampleWaterBilinear(float wx, float wz, int res)
        {
            float px = Mathf.Clamp01(wx / BeachSize + 0.5f) * res - 0.5f;
            float pz = Mathf.Clamp01(wz / BeachSize + 0.5f) * res - 0.5f;
            int x0 = Mathf.FloorToInt(px), z0 = Mathf.FloorToInt(pz);
            float fx = px - x0, fz = pz - z0;
            int x1 = Mathf.Clamp(x0 + 1, 0, res - 1), z1 = Mathf.Clamp(z0 + 1, 0, res - 1);
            x0 = Mathf.Clamp(x0, 0, res - 1);
            z0 = Mathf.Clamp(z0, 0, res - 1);
            return Bilerp(
                _water.GetHeight(x0, z0), _water.GetHeight(x1, z0),
                _water.GetHeight(x0, z1), _water.GetHeight(x1, z1), fx, fz);
        }

        void Upload()
        {
            _plantBuf.SetData(_plants);
            _aliveBuf.SetData(_aliveList);
            _args[0] = _hull ? _hull.GetIndexCount(0) : 0u;
            _args[1] = (uint)_aliveCount;
            _args[2] = 0u;
            _args[3] = 0u;
            _args[4] = 0u;
            _argsBuf.SetData(_args);
        }

        // =====================================================================
        // 渲染挂接（Plant.Draw，G §3.1：DrawMeshInstancedIndirect；I6b 装配每帧调用）。
        // 原作 castShadows=true 再以 LightProbeUsage(3) 画一遍进阴影——Unity 原生管线
        // 由 ShadowCastingMode.On 一并走 ShadowCaster pass（ReplicaPlant.shader 第二
        // pass），无需双画（差异注记，L2 抓帧对账）。
        // =====================================================================
        public void Draw(Camera camera = null)
        {
            if (_aliveCount == 0 || _hull == null)
                return;
            PublishGlobals();
            Graphics.DrawMeshInstancedIndirect(_hull, 0, _material, DrawBounds, _argsBuf,
                0, null, ShadowCastingMode.On, true, 0, camera);
        }

        // ---------------------------------------------------------------------
        // 默认场实现：WaterLabReplicaSnapshot（B 流 256² 快照）。
        // 【分辨率/通道差异 TODO（G §5.5）】原作 groundCells=1024²/waterCells=512² 且
        // 带肥力 6bit 与切线字节通道；快照只有高度/水量 256²。I7 烘地形产出同编码
        // 肥力场后替换本实现，否则 §4 表的 fertilityRange 过滤退化为全通过。
        // ---------------------------------------------------------------------
        public sealed class SnapshotGroundField : IGroundField
        {
            public int Resolution => WaterLabReplicaSnapshot.Resolution;

            public void GetCell(int cx, int cz, out float height, out float fertility,
                                out float tangentX, out float tangentZ)
            {
                float[] g = WaterLabReplicaSnapshot.GroundData;
                height = g != null ? g[cz * WaterLabReplicaSnapshot.Resolution + cx] : WaterLabRules.SeaLevel;
                fertility = 1f;   // TODO(I7)：原作 6bit 肥力通道
                // 切线 = 中央差分坡度（clamp 到字节域 [−1,1] 语义）。
                // TODO(I7)：原作 groundCells 偏移 0 的 2 字节切线直读后替换。
                int r = WaterLabReplicaSnapshot.Resolution;
                float xl = g != null ? g[cz * r + Mathf.Max(cx - 1, 0)] : height;
                float xr = g != null ? g[cz * r + Mathf.Min(cx + 1, r - 1)] : height;
                float zl = g != null ? g[Mathf.Max(cz - 1, 0) * r + cx] : height;
                float zr = g != null ? g[Mathf.Min(cz + 1, r - 1) * r + cx] : height;
                float dxs = (xr - xl) / (2f * WaterLabReplicaSnapshot.CellSize);
                float dzs = (zr - zl) / (2f * WaterLabReplicaSnapshot.CellSize);
                tangentX = Mathf.Clamp(dxs, -1f, 1f);
                tangentZ = Mathf.Clamp(dzs, -1f, 1f);
            }
        }

        public sealed class SnapshotWaterField : IWaterField
        {
            public int Resolution => WaterLabReplicaSnapshot.Resolution;

            public float GetHeight(int cx, int cz)
            {
                float[] w = WaterLabReplicaSnapshot.WaterData;
                return w != null ? w[cz * WaterLabReplicaSnapshot.Resolution + cx] : 0f;
            }
        }

        public void Dispose()
        {
            _plantBuf?.Dispose();
            _aliveBuf?.Dispose();
            _argsBuf?.Dispose();
            _plantBuf = null;
            _aliveBuf = null;
            _argsBuf = null;
        }
    }

    // =========================================================================
    // 13 组摆放参数（单源：rt/plants-placement.json，G-plants.md §4.1 表逐列核对）。
    // 顺序 = Beach.plants[] = GetComponentsInChildren 场景子序（level0 m_Children 序）。
    // 来源标注：capacity/sizeRange/groundHeightRange/fertilityRange = Plant 组件实例
    // 字段裸字节；GridN = 材质 atlasMap texenv m_Scale（cb1[0] 真身）；PivotOffset =
    // 材质 m_Colors.pivotOffset（cb1[1]）；ShadowDepthBiais = 材质 m_Floats（cb1[2].x）；
    // HullPathId = sharedassets0 凸包 Mesh path_id（I6b 由 placement JSON mesh_full 生成）。
    // =========================================================================
    public struct ReplicaPlantParams
    {
        public string ClassName;
        public string Group;
        public string AtlasName;      // Assets/Art/Textures/WaterLab/Plants/<AtlasName>.png
        public int Capacity;
        public Vector2 SizeRange;
        public Vector2 GroundHeightRange;
        public Vector2 FertilityRange;
        public float GridN;
        public Vector4 PivotOffset;   // xyz=imposter 立方中心局部偏移，w=立方世界尺寸
        public float ShadowDepthBiais;
        public string HullMeshName;
        public int HullPathId;
    }

    public static class ReplicaPlantTable
    {
        public static readonly ReplicaPlantParams[] All =
        {
            new ReplicaPlantParams {
                ClassName = "Kelp", Group = "Coastal", AtlasName = "Kelp_01_ImposterAtlas",
                Capacity = 2048, SizeRange = new Vector2(0.5f, 1.0f),
                GroundHeightRange = new Vector2(0.0f, 0.40000001f),
                FertilityRange = new Vector2(0.20000000f, 1.0f), GridN = 16,
                PivotOffset = new Vector4(0.02563637f, 0.37724593f, 0.02181767f, 1.22661448f),
                ShadowDepthBiais = 0.161f, HullMeshName = "Kelp_01_Collider", HullPathId = 422 },
            new ReplicaPlantParams {
                ClassName = "SeaGrass", Group = "Coastal", AtlasName = "Seagrass_01_ImposterAtlas",
                Capacity = 4096, SizeRange = new Vector2(0.5f, 1.0f),
                GroundHeightRange = new Vector2(0.20000000f, 0.60000002f),
                FertilityRange = new Vector2(0.10000000f, 1.5f), GridN = 12,
                PivotOffset = new Vector4(0.03536578f, 0.32605842f, -0.00081841f, 1.56171095f),
                ShadowDepthBiais = 0.147f, HullMeshName = "Seagrass_01_Collider", HullPathId = 461 },
            new ReplicaPlantParams {
                ClassName = "RedAlgae", Group = "Coastal", AtlasName = "RedAlgae_01_ImposterAtlas",
                Capacity = 1024, SizeRange = new Vector2(0.5f, 1.0f),
                GroundHeightRange = new Vector2(0.0f, 0.5f),
                FertilityRange = new Vector2(0.80000001f, 1.0f), GridN = 14,
                PivotOffset = new Vector4(0.02540170f, 0.41553858f, -0.01209591f, 1.17514479f),
                ShadowDepthBiais = 0.156f, HullMeshName = "RedAlgae_01_Collider", HullPathId = 428 },
            new ReplicaPlantParams {
                ClassName = "LettuceCoral", Group = "CoralReef", AtlasName = "LettuceCoral_01_ImposterAtlas",
                Capacity = 8192, SizeRange = new Vector2(0.5f, 1.0f),
                GroundHeightRange = new Vector2(0.60000002f, 0.89999998f),
                FertilityRange = new Vector2(0.20000000f, 1.0f), GridN = 10,
                PivotOffset = new Vector4(-0.00317361f, 0.13387443f, 0.04550306f, 1.44605219f),
                ShadowDepthBiais = 0.132f, HullMeshName = "LettuceCoral_01_Collider", HullPathId = 415 },
            new ReplicaPlantParams {
                ClassName = "GrooveBrainCoral", Group = "CoralReef", AtlasName = "GroovedBrainCoral_01_ImposterAtlas",
                Capacity = 4096, SizeRange = new Vector2(0.5f, 1.0f),
                GroundHeightRange = new Vector2(0.5f, 0.85000002f),
                FertilityRange = new Vector2(0.30000001f, 1.0f), GridN = 8,
                PivotOffset = new Vector4(0.00287246f, 0.21075085f, 0.00246722f, 1.79527318f),
                ShadowDepthBiais = 0.132f, HullMeshName = "GroovedBrainCoral_01_Collider", HullPathId = 488 },
            new ReplicaPlantParams {
                ClassName = "CarnationCoral", Group = "CoralReef", AtlasName = "CarnationCoral_01_ImposterAtlas",
                Capacity = 4096, SizeRange = new Vector2(0.5f, 1.0f),
                GroundHeightRange = new Vector2(0.5f, 0.89999998f),
                FertilityRange = new Vector2(0.34999999f, 1.0f), GridN = 14,
                PivotOffset = new Vector4(0.06017601f, 0.33328342f, 0.03168675f, 1.14912426f),
                ShadowDepthBiais = 0.154f, HullMeshName = "CarnationCoral_01_Collider", HullPathId = 439 },
            new ReplicaPlantParams {
                ClassName = "ElkhornCoral", Group = "CoralReef", AtlasName = "ElkhornCoral_01_ImposterAtlas",
                Capacity = 4096, SizeRange = new Vector2(0.5f, 1.0f),
                GroundHeightRange = new Vector2(0.5f, 0.80000001f),
                FertilityRange = new Vector2(0.30000001f, 1.0f), GridN = 16,
                PivotOffset = new Vector4(0.00385825f, 0.29852793f, -0.01051866f, 1.14408243f),
                ShadowDepthBiais = 0.177f, HullMeshName = "ElkhornCoral_01_Collider", HullPathId = 396 },
            new ReplicaPlantParams {
                ClassName = "GreyTussock", Group = "Grassland", AtlasName = "GreyTussock_01_ImposterAtlas",
                Capacity = 8192, SizeRange = new Vector2(0.75f, 1.5f),
                GroundHeightRange = new Vector2(1.10000002f, 4.0f),
                FertilityRange = new Vector2(0.30000001f, 0.60000002f), GridN = 14,
                PivotOffset = new Vector4(-0.00751367f, 0.19552810f, -0.01545218f, 1.35503685f),
                ShadowDepthBiais = 0.174f, HullMeshName = "GreyTussock_01_Collider", HullPathId = 419 },
            new ReplicaPlantParams {
                ClassName = "Yarrow", Group = "Grassland", AtlasName = "Yarrow_01_ImposterAtlas",
                Capacity = 64, SizeRange = new Vector2(1.0f, 2.0f),
                GroundHeightRange = new Vector2(1.04999995f, 4.0f),
                FertilityRange = new Vector2(0.80000001f, 2.0f), GridN = 12,
                PivotOffset = new Vector4(0.00541825f, 0.23779866f, 0.00485707f, 1.36139023f),
                ShadowDepthBiais = 0.142f, HullMeshName = "Yarrow_01_Collider", HullPathId = 498 },
            new ReplicaPlantParams {
                ClassName = "SwitchGrass", Group = "Grassland", AtlasName = "Switchgrass_01_ImposterAtlas",
                Capacity = 32768, SizeRange = new Vector2(0.75f, 1.25f),
                GroundHeightRange = new Vector2(1.04999995f, 4.0f),
                FertilityRange = new Vector2(0.69999999f, 2.0f), GridN = 12,
                PivotOffset = new Vector4(0.09519492f, 0.35754853f, 0.02511655f, 1.42879069f),
                ShadowDepthBiais = 0.155f, HullMeshName = "Switchgrass_01_Collider", HullPathId = 358 },
            new ReplicaPlantParams {
                ClassName = "Goldenrod", Group = "Grassland", AtlasName = "Goldenrod_01_ImposterAtlas",
                Capacity = 256, SizeRange = new Vector2(0.75f, 1.25f),
                GroundHeightRange = new Vector2(1.04999995f, 4.0f),
                FertilityRange = new Vector2(0.80000001f, 1.0f), GridN = 12,
                PivotOffset = new Vector4(-0.02337214f, 0.40560830f, -0.00310306f, 1.40326428f),
                ShadowDepthBiais = 0.150f, HullMeshName = "Goldenrod_01_Collider", HullPathId = 471 },
            new ReplicaPlantParams {
                ClassName = "BuffaloGrass", Group = "Grassland", AtlasName = "BuffaloGrass_01_ImposterAtlas",
                Capacity = 32768, SizeRange = new Vector2(0.60000002f, 1.5f),
                GroundHeightRange = new Vector2(1.20000005f, 4.0f),
                FertilityRange = new Vector2(0.60000002f, 2.0f), GridN = 14,
                PivotOffset = new Vector4(-0.05520138f, 0.13268846f, 0.06704574f, 1.29398549f),
                ShadowDepthBiais = 0.147f, HullMeshName = "BuffaloGrass_01_Collider", HullPathId = 379 },
            new ReplicaPlantParams {
                ClassName = "WheatGrass", Group = "Grassland", AtlasName = "WheatGrass_01_ImposterAtlas",
                Capacity = 512, SizeRange = new Vector2(0.75f, 1.0f),
                GroundHeightRange = new Vector2(1.04999995f, 4.0f),
                FertilityRange = new Vector2(0.69999999f, 0.89999998f), GridN = 14,
                PivotOffset = new Vector4(0.00579458f, 0.35813925f, 0.01307179f, 1.17702806f),
                ShadowDepthBiais = 0.166f, HullMeshName = "WheatGrass_01_Collider", HullPathId = 479 },
        };

        /// <summary>运行时建材质（I6b 装配用）：shader 真值逐字填入（G-plants.md §5.1）。</summary>
        public static Material CreateMaterial(ReplicaPlantParams p, Texture2D atlas, Shader shader)
        {
            var mat = new Material(shader) { name = p.ClassName + "_01_Imposter" };
            mat.SetTexture("_atlasMap", atlas);
            mat.SetTextureScale("_atlasMap", Vector2.one * p.GridN);  // cb1[0] 真身：ST=(N,N,0,0)
            mat.SetTextureOffset("_atlasMap", Vector2.zero);
            mat.SetVector("pivotOffset", p.PivotOffset);              // cb1[1]
            mat.SetFloat("shadowDepthBiais", p.ShadowDepthBiais);     // cb1[2].x
            mat.SetFloat("_Cutoff", 0.5f);                            // 原作材质 _Cutoff=0.5
            return mat;
        }
    }

    // =========================================================================
    // ReplicaPlantDriver —— 植物 13 组系统的场景自驱件（I6b 装配端）。
    //
    // 【职责边界】装配（WaterLabSetup.EnsurePlants）只建挂点树 + 材质/凸包资产 +
    // 填下面三个序列化字段；系统的 new / 分布 / 每帧 Tick+Draw 全部本件自举——
    // 与 WaterLabSimulation 同一模式（TextAsset 引用随场景序列化，构建后重开
    // 场景仍有效），不依赖装配期的任何内存态。
    //
    // 【运行时专用 TODO（G §6 遗留，静态不可恢复）】
    //  * DistributionSeed/SeedExtra：原作引擎每实例 SetFloat，Cpp2IL 丢失——恒 0，
    //    分布结果与原作不逐株恒等（统计分布同源）；恒等化走 frida 补读。
    //  * AmbientOverride：cb0[40] 环境色真值待抓帧；暂置主菜单 t=1/3
    //    ambientSkyColor（CreateLighting/EnsureSkybox 同值三处一字不改）。
    //    I3 GI 落地后植物环境项换 giVoxels 采样，本覆写退役。
    //  * 水场：Tick 用静态快照水面（初始 level≈1.0 场），非实时水场——原作
    //    waterCells 每帧变。接实时场需 async readback _WaterLabHeightTex（I9 域），
    //    L2 动态验收时再评估；静态画面（主菜单复刻目标）两者在 t0 一致。
    // =========================================================================
    public sealed class ReplicaPlantDriver : MonoBehaviour
    {
        /// <summary>凸包网格（与 <see cref="ReplicaPlantTable.All"/> 同序；Editor 烘焙资产
        /// WaterLabPlantBake.EnsureHullMeshes 产物，场景序列化引用）。</summary>
        public Mesh[] Hulls;

        /// <summary>13 组 imposter 材质（同序；EnsurePlants 按 CreateMaterial 真值落盘）。</summary>
        public Material[] Materials;

        /// <summary>主菜单快照（自举时灌 WaterLabReplicaSnapshot 静态采样器；
        /// 与 WaterLabSimulation 引用同一对资产，Load 幂等，先后无害）。</summary>
        public TextAsset GroundSnapshot;
        public TextAsset WaterSnapshot;

        /// <summary>cb0[40] 环境色暂置值：t=1/3 Morning ambientSkyColor（menu-state.json，
        /// 与 CreateLighting 的 RenderSettings.ambientSkyColor 同值）。运行时专用 TODO 见类头。</summary>
        public static readonly Vector4 MenuAmbient = new Vector4(0.698039f, 0.564706f, 0.569923f, 1f);

        ReplicaPlantSystem[] _systems;

        /// <summary>当前 13 组系统（装配期检查用；未自举时 null）。</summary>
        public IReadOnlyList<ReplicaPlantSystem> Systems => _systems;

        void Awake()
        {
            if (!WaterLabReplicaSnapshot.HasData && GroundSnapshot != null && WaterSnapshot != null)
            {
                try { WaterLabReplicaSnapshot.Load(GroundSnapshot.bytes, WaterSnapshot.bytes); }
                catch (Exception e) { Debug.LogError($"[ReplicaPlantDriver] 快照加载失败（植物门控退化）：{e.Message}"); }
            }
            EnsureSystems();
        }

        /// <summary>按表序建 13 组系统并完成初始分布（幂等；Awake 自举/装配期手动触发）。</summary>
        public void EnsureSystems()
        {
            if (_systems != null)
                return;
            var table = ReplicaPlantTable.All;
            if (Hulls == null || Hulls.Length != table.Length ||
                Materials == null || Materials.Length != table.Length)
            {
                Debug.LogError($"[ReplicaPlantDriver] Hulls/Materials 未注入或长度≠{table.Length}（装配缺位），植物跳过。");
                return;
            }

            // 环境色先于构造（构造链上的 PublishGlobals 会读取）。
            if (!ReplicaPlantSystem.AmbientOverride.HasValue)
                ReplicaPlantSystem.AmbientOverride = MenuAmbient;

            _systems = new ReplicaPlantSystem[table.Length];
            int total = 0;
            for (int i = 0; i < table.Length; i++)
            {
                if (Hulls[i] == null || Materials[i] == null)
                {
                    Debug.LogWarning($"[ReplicaPlantDriver] 第 {i} 组（{table[i].ClassName}）资产缺位，跳过。");
                    continue;
                }
                var sys = new ReplicaPlantSystem(table[i], Hulls[i], Materials[i]);
                sys.Distribute();   // fac 真值落地前 = 全容量尝试（系统注释 TODO）
                _systems[i] = sys;
                total += sys.AliveCount;
            }
            Debug.Log($"[ReplicaPlantDriver] 植物系统就绪：{_systems.Length} 组 / 存活 {total} 株" +
                "（seed 恒 0=运行时专用 TODO，分布与原作非逐株恒等）");
        }

        void Update()
        {
            if (_systems == null)
                return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _systems.Length; i++)
            {
                var sys = _systems[i];
                if (sys == null) continue;
                sys.Tick(dt);   // 生根更新（静态快照水场，见类头 TODO）
                sys.Draw();     // DrawMeshInstancedIndirect（castShadows 语义在系统内）
            }
        }

        void OnDestroy()
        {
            if (_systems == null)
                return;
            for (int i = 0; i < _systems.Length; i++)
                _systems[i]?.Dispose();
            _systems = null;
        }
    }
}
