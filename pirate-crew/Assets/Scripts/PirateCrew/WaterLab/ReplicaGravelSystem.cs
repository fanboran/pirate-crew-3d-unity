using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.WaterLab
{
    // =========================================================================
    // ReplicaGravelSystem —— Sandcastle Gravel GPU 粒子层移植（C 流，§4b 批次）。
    //
    // 翻译源（全部第一手，出处见各成员注释）：
    //   * C# 结构   cs-full/Gravel.cs + Particle.cs（capacity 32768 /
    //     GravelParticle{Vector3 position; float age}=16B / Cleanup 每 5 s+Compaction /
    //     Draw=DrawMeshInstancedIndirect）。
    //   * Update 内核 shaderblobs/disasm/Gravel_*.bin.1.hlslasm（域外剔除 + 陡坡水推
    //     平流：门控 h>0.05 且 |tangent|>0.4、每粒随机 0.5–0.9、结果 clamp ±15 m）。
    //   * Cleanup 内核 Gravel_*.bin.2.hlslasm（域外粒子走 deadList/aliveList 回收）。
    //   * 渲染     extracted/shader-621/prog00-03（Lit/Gravel VS/PS 反汇编）：
    //     VS local×w+pos、每实例 rand=sqrt(PCG²) 定尺度与色 lerp；PS 双灯+环境、
    //     域外 discard、无水柱吸收。直译进 Assets/Art/Shaders/WaterLab/ReplicaGravel.shader。
    //   * 颜色     Carbonate 预设 gravelColor/gravelColor2（D-sand.md §4.2 七型表；
    //     与 Gravel 材质序列化值逐位交叉验证）。
    //
    // 【与原作的结构差】原作 Update/Paint/Spawn 是 compute kernel，本系统起步走 CPU
    // （ReplicaPlantSystem 同模式：CPU 数组 + GraphicsBuffer + Indirect 绘制）；
    // 布种由 Driver 在 Awake 按 §10.6 loose 群簇统计确定性生成（原作菜单砾石来自
    // LayoutMainMenu 落笔时的 brush 搅动，静态不可逐粒恢复——非恒等项，C-scatter.md 登记）。
    // =========================================================================
    public sealed class ReplicaGravelSystem : IDisposable
    {
        // ---------------------------------------------------------------------
        // 16B 粒子（Gravel.cs:9-14，Marshal.SizeOf=16 与 compute/渲染 buffer stride 一致）。
        // 原作第二字段名 age，但渲染端只把它当**尺寸**用（VS：local×w+pos）——改名
        // size 并在此注记，防误接时间语义。
        // ---------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        public struct GravelParticle
        {
            public Vector3 position;  // 偏移 0
            public float size;        // 偏移 12（原作字段 age；VS 用作网格缩放）
        }

        // ---------------------------------------------------------------------
        // 真值常量
        // ---------------------------------------------------------------------

        /// <summary>粒子容量（Particle.cs:14 字段默认值 32768）。</summary>
        public const int Capacity = 32768;

        /// <summary>沙滩世界 XZ 尺寸（Gravel Update 内核 cb0[116].xz 域界；B 流 bounds 30）。</summary>
        public const float BeachSize = WaterLabRules.DomainSize; // 30

        /// <summary>Cleanup 节拍（Gravel.cs:140 Step("Cleanup", 5f)）。</summary>
        public const float CleanupInterval = 5f;

        /// <summary>水推门控：水面高 > 0.05（Update 内核 lt 0.05；= WaterLabRules.DryDepth）。</summary>
        public const float WaterGate = 0.05f;

        /// <summary>水推门控：切向幅值 > 0.4（Update 内核 lt 0.400000）。</summary>
        public const float TangentGate = 0.4f;

        /// <summary>每粒平流随机下/上限（Update 内核 mad ×0.4 + 0.5 → [0.5,0.9)）。</summary>
        public const float FlowRandMin = 0.5f, FlowRandSpan = 0.4f;

        /// <summary>
        /// Carbonate 预设砾石双色（D-sand.md §4.2 表 type0，linear 序列化值；
        /// 与 Gravel 材质 baseColor1/2=(0.2390,0.2101,0.1811)/(0.4906,0.4417,0.3656) 交叉验证）。
        /// </summary>
        public static readonly Vector3 CarbonateColor1 = new Vector3(0.2392f, 0.2118f, 0.1804f);
        public static readonly Vector3 CarbonateColor2 = new Vector3(0.4902f, 0.4431f, 0.3656f);

        // shader buffer/属性名（ReplicaGravel.shader 同名声明）
        static readonly int IdGravel = Shader.PropertyToID("_Gravel");
        static readonly int IdAliveList = Shader.PropertyToID("_GravelAliveList");
        static readonly int IdDomainSize = Shader.PropertyToID("_WaterLabDomainSize");

        // ---------------------------------------------------------------------
        // 粒子态与 GPU 缓冲（Particle.Setup 生命周期）
        // ---------------------------------------------------------------------
        readonly GravelParticle[] _particles;
        readonly int[] _aliveList;
        readonly int[] _deadList;
        readonly uint[] _args = new uint[5];   // DrawIndirect 20B：indexCount, instanceCount, 0, 0, 0
        int _deadCount;                        // deadCounter 初值 = capacity（Particle.Setup）
        int _aliveCount;
        float _cleanupTimer;                   // Gravel.Run 的 Step("Cleanup",5f) 累计

        readonly Mesh _mesh;
        readonly Material _material;
        GraphicsBuffer _particleBuf;   // StructuredBuffer<float4>（16B）
        GraphicsBuffer _aliveBuf;      // StructuredBuffer<uint>
        GraphicsBuffer _argsBuf;       // IndirectArguments

        /// <summary>当前存活粒数（= instanceCount）。</summary>
        public int AliveCount => _aliveCount;

        /// <summary>DrawMeshInstancedIndirect 剔除包围盒（= B 流 Beach.bounds）。</summary>
        public Bounds DrawBounds = new Bounds(new Vector3(0f, 5f, 0f), new Vector3(BeachSize, 10f, BeachSize));

        public ReplicaGravelSystem(Mesh mesh, Material material)
        {
            _mesh = mesh;
            _material = material;
            _particles = new GravelParticle[Capacity];
            _aliveList = new int[Capacity];
            _deadList = new int[Capacity];
            for (int i = 0; i < Capacity; i++)
            {
                _aliveList[i] = -1;    // Initialization kernel：aliveList=−1
                _deadList[i] = i;      // deadList=identity
            }
            _deadCount = Capacity;     // deadCounter 初值 = capacity（Particle.Setup）
            _aliveCount = 0;

            _particleBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, 16);
            _aliveBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, 4);
            _argsBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint));

            // 材质缓冲绑定（Gravel.Init：material.SetBuffer("gravel", buffer)，kernel 绑定同族）
            _material.SetBuffer(IdGravel, _particleBuf);
            _material.SetBuffer(IdAliveList, _aliveBuf);
            // 域界全局（PS discard 用 cb0[116].xz 语义；结构上属于渲染端真值）
            Shader.SetGlobalFloat(IdDomainSize, BeachSize);
        }

        // =====================================================================
        // 布种（Spawn 路径的 CPU 形态：deadList 弹槽 + 写粒子 + 挂 aliveList；
        // 槽位语义 = Spawn kernel 的 atomic 弹出，本版单线程无需原子）
        // =====================================================================
        public bool TrySpawn(Vector3 position, float size)
        {
            if (_deadCount <= 0)
                return false;                          // 槽耗尽（原作 exch 守卫同语义）
            int slot = _deadList[--_deadCount];
            _particles[slot].position = position;
            _particles[slot].size = size;
            if (_aliveCount < Capacity)
                _aliveList[_aliveCount++] = slot;      // aliveList[atomicAdd(args[1])] = slot
            return true;
        }

        // =====================================================================
        // Update/Cleanup kernel 的 CPU 移植
        // Update（bin.1）：域外剔除在 Cleanup；本内核做陡坡水推平流——
        //   采地面切向/水面高（双线性），门控 waterH>0.05 且 |t|>0.4 →
        //   pos += t·rand(0.5..0.9)·brushRadius·dt 语义（brushRadius 静态=0 → 位移≈0），
        //   结果 clamp ±15。静态主菜单复刻下平流恒零，保留结构供动态验收。
        // Cleanup（bin.2 + Gravel.cs Run）：每 5 s 域外剔除 + Compaction。
        // =====================================================================
        public void Tick(float dt)
        {
            int limit = _aliveCount;
            float flowRadius = 0f;   // cb0[155].z：无笔刷时=0（静态菜单真值）
            for (int i = 0; i < limit; i++)
            {
                int idx = _aliveList[i];
                if (idx < 0) continue;
                GravelParticle p = _particles[idx];

                // 域界 clamp（Update 内核尾 max/min ±15；域外留给 Cleanup 杀）
                p.position.x = Mathf.Clamp(p.position.x, -BeachSize * 0.5f, BeachSize * 0.5f);
                p.position.z = Mathf.Clamp(p.position.z, -BeachSize * 0.5f, BeachSize * 0.5f);

                // 平流（门控内才动；rand 与原作同源 [0.5,0.9)）
                float waterH = WaterLabReplicaSnapshot.SampleWater(p.position.x, p.position.z);
                SampleGroundTangent(p.position.x, p.position.z, out float tx, out float tz);
                float tMag = Mathf.Sqrt(tx * tx + tz * tz);
                if (waterH > WaterGate && tMag > TangentGate)
                {
                    float rand = FlowRandMin + Hash01(PcgHash((uint)idx ^ 0xA5A5u)) * FlowRandSpan;
                    float inv = 1f / Mathf.Max(1e-6f, tMag);
                    p.position.x = Mathf.Clamp(p.position.x + tx * inv * rand * flowRadius, -BeachSize * 0.5f, BeachSize * 0.5f);
                    p.position.z = Mathf.Clamp(p.position.z + tz * inv * rand * flowRadius, -BeachSize * 0.5f, BeachSize * 0.5f);
                }
                _particles[idx] = p;
            }

            _cleanupTimer += dt;
            if (_cleanupTimer >= CleanupInterval)
            {
                _cleanupTimer = 0f;
                CleanupAndCompact();   // Gravel.Run：Dispatch(Cleanup) + Compaction()
            }
            Upload();
        }

        /// <summary>Cleanup kernel + Compaction：域外剔除，alive 保序压缩、deadList 重建。</summary>
        void CleanupAndCompact()
        {
            float half = BeachSize * 0.5f;
            int n = 0;
            for (int i = 0; i < _aliveCount; i++)
            {
                int idx = _aliveList[i];
                if (idx < 0) continue;
                Vector3 p = _particles[idx].position;
                if (p.x < -half || p.x > half || p.z < -half || p.z > half || p.y < 0f)
                    continue;                          // 出域 → 不入压缩表（= 回收 deadList）
                _aliveList[n++] = idx;
            }
            int killed = _aliveCount - n;
            _aliveCount = n;
            // deadList 重建 = identity 补齐（Compaction 后 deadCounter=capacity−alive）
            _deadCount = 0;
            bool[] alive = new bool[Capacity];
            for (int i = 0; i < _aliveCount; i++) alive[_aliveList[i]] = true;
            for (int i = 0; i < Capacity; i++)
                if (!alive[i]) _deadList[_deadCount++] = i;
        }

        void SampleGroundTangent(float wx, float wz, out float tx, out float tz)
        {
            // 地面切向 = 快照中央差分坡度（ReplicaPlantSystem.SnapshotGroundField 同式）；
            // 门控语义对齐原作 groundCells 切向字节（|t|>0.4 = 陡坡）。
            float e = WaterLabReplicaSnapshot.CellSize;
            float xl = WaterLabReplicaSnapshot.SampleGround(wx - e, wz);
            float xr = WaterLabReplicaSnapshot.SampleGround(wx + e, wz);
            float zl = WaterLabReplicaSnapshot.SampleGround(wx, wz - e);
            float zr = WaterLabReplicaSnapshot.SampleGround(wx, wz + e);
            tx = Mathf.Clamp((xr - xl) / (2f * e), -1f, 1f);
            tz = Mathf.Clamp((zr - zl) / (2f * e), -1f, 1f);
        }

        void Upload()
        {
            _particleBuf.SetData(_particles);
            _aliveBuf.SetData(_aliveList);
            _args[0] = _mesh ? _mesh.GetIndexCount(0) : 0u;
            _args[1] = (uint)_aliveCount;
            _args[2] = 0u;
            _args[3] = 0u;
            _args[4] = 0u;
            _argsBuf.SetData(_args);
        }

        // =====================================================================
        // 渲染挂接（Gravel.Draw：DrawMeshInstancedIndirect；castShadows 语义 = 原作
        // ShadowCastingMode=true 走 ShadowCaster pass，ReplicaGravel.shader 第二 pass）。
        // =====================================================================
        public void Draw(Camera camera = null)
        {
            if (_aliveCount == 0 || _mesh == null)
                return;
            Graphics.DrawMeshInstancedIndirect(_mesh, 0, _material, DrawBounds, _argsBuf,
                0, null, ShadowCastingMode.On, true, 0, camera);
        }

        // ---- PCG hash（与 ReplicaPlantSystem 同款：Lit/Plant Distribution L30-55；
        //      Lit/Gravel VS/PS 同链，extracted/shader-621/prog00 双哈希段）----
        static uint PcgHash(uint v)
        {
            uint t = (v ^ 61u) ^ (v >> 16);
            t *= 9u;
            t ^= t >> 4;
            t *= 0x27d4eb2du;
            t ^= t >> 15;
            return t;
        }

        // bfi(23,0) 进 0x3f800000 尾数再 −1 ∈ [0,1)（VS/Update 内核同式）
        static float Hash01(uint h)
        {
            return BitConverter.Int32BitsToSingle((int)(0x3f800000u | (h & 0x7fffffu))) - 1f;
        }

        public void Dispose()
        {
            _particleBuf?.Dispose();
            _aliveBuf?.Dispose();
            _argsBuf?.Dispose();
            _particleBuf = null;
            _aliveBuf = null;
            _argsBuf = null;
        }
    }

}
