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

    // =========================================================================
    // ReplicaGravelDriver —— 砾石系统场景自驱件（C 流装配端，ReplicaPlantDriver 同模式）。
    //
    // 【职责边界】装配（WaterLabScatterSetup.EnsureScatter）只建挂点 + 网格/材质资产 +
    // 填三个序列化字段；系统 new / 布种 / 每帧 Tick+Draw 本件自举，重开场景仍有效。
    //
    // 【分布定标（C-scatter.md §3 登记，非逐粒恒等）】原作菜单砾石来自布局落笔时
    // brush 搅动（Paint kernel），逐粒不可静态恢复。本分布按 §10.6 观感真值确定性生成：
    // loose 群簇（簇径 0.3–0.8 m、簇距 1–3 m），带 = 沙面带 ground∈[0.5,1.5]（快照域
    // 面积 476.3 m²），粒尺寸 = VS 同源 sqrt(PCG²) ∈ [0,1)×Gravel_01 网格（4 cm 级暗点）。
    // =========================================================================
    public sealed class ReplicaGravelDriver : MonoBehaviour
    {
        /// <summary>Gravel_01 网格资产（原作 DrawMeshInstancedIndirect 的 mesh 字段）。</summary>
        public Mesh GravelMesh;

        /// <summary>ReplicaGravel 材质（装配期落盘，双色 = Carbonate 预设真值）。</summary>
        public Material GravelMaterial;

        /// <summary>主菜单快照（与 WaterLabSimulation/ReplicaPlantDriver 引用同对资产，Load 幂等）。</summary>
        public TextAsset GroundSnapshot;
        public TextAsset WaterSnapshot;

        // ---- 分布定标常量（出处：§10.6 loose 群簇参数 + C-scatter.md §3 面积统计）----
        /// <summary>散布种子（确定性；每次装配/重启同一分布——非逐粒恒等的登记口径）。</summary>
        public const uint Seed = 0x5CA77u;

        /// <summary>砾石带（沙面带）：ground ∈ [0.5, 1.5]（快照实测面积 476.3 m²）。</summary>
        public const float BandGroundMin = 0.5f, BandGroundMax = 1.5f;

        /// <summary>簇距 2 m（§10.6 间距 1–3 m 中值）±0.5 m 抖动（保底 1 m 间距）。</summary>
        public const float ClusterSpacing = 2f, ClusterJitter = 0.5f;

        /// <summary>簇半径 0.15–0.4 m（§10.6 单簇 0.3–0.8 m 直径）。</summary>
        public const float ClusterRadiusMin = 0.15f, ClusterRadiusSpan = 0.25f;

        /// <summary>每簇粒数 8–20（暗点疏密观感口径）。</summary>
        public const int SpecksMin = 8, SpecksSpan = 12;

        /// <summary>带内簇保留率（45 簇目标 / 带内约 118 格 ≈ 0.38，量级定标）。</summary>
        public const float ClusterKeepRatio = 0.38f;

        ReplicaGravelSystem _system;

        /// <summary>当前系统（装配期检查用；未自举时 null）。</summary>
        public ReplicaGravelSystem System => _system;

        void Awake()
        {
            if (!WaterLabReplicaSnapshot.HasData && GroundSnapshot != null && WaterSnapshot != null)
            {
                try { WaterLabReplicaSnapshot.Load(GroundSnapshot.bytes, WaterSnapshot.bytes); }
                catch (Exception e) { Debug.LogError($"[ReplicaGravelDriver] 快照加载失败（砾石跳过）：{e.Message}"); }
            }
            EnsureSystem();
        }

        /// <summary>建系统并按 §10.6 群簇统计布种（幂等；Awake 自举/装配期手动触发）。</summary>
        public void EnsureSystem()
        {
            if (_system != null)
                return;
            if (GravelMesh == null || GravelMaterial == null)
            {
                Debug.LogError("[ReplicaGravelDriver] GravelMesh/GravelMaterial 未注入（装配缺位），砾石跳过。");
                return;
            }
            _system = new ReplicaGravelSystem(GravelMesh, GravelMaterial);
            int spawned = SeedDeterministic();
            Debug.Log($"[ReplicaGravelDriver] 砾石系统就绪：存活 {spawned} 粒" +
                "（确定性种子，分布非逐粒恒等——原作=布局笔刷搅动，静态不可恢复，见 C-scatter.md §3）");
        }

        /// <summary>
        /// 确定性布种：2 m 抖动网格 × 带过滤 × PCG 群簇。哈希链与渲染端 PCG 同族。
        /// </summary>
        int SeedDeterministic()
        {
            int half = (int)(ReplicaGravelSystem.BeachSize / ClusterSpacing / 2f); // 15/2 → 7 → −7..7
            int spawned = 0;
            uint cell = 0;
            for (int gz = -half; gz <= half; gz++)
            {
                for (int gx = -half; gx <= half; gx++, cell++)
                {
                    // 每格独立哈希流：seed ⊕ 格序数 × 黄金比
                    uint state = Seed ^ (cell * 0x9E3779B1u);
                    float cx = gx * ClusterSpacing + (Hash01(PcgHash(ref state)) * 2f - 1f) * ClusterJitter;
                    float cz = gz * ClusterSpacing + (Hash01(PcgHash(ref state)) * 2f - 1f) * ClusterJitter;

                    float ground = WaterLabReplicaSnapshot.SampleGround(cx, cz);
                    if (ground < BandGroundMin || ground >= BandGroundMax)
                        continue;
                    if (Hash01(PcgHash(ref state)) > ClusterKeepRatio)
                        continue;

                    float radius = ClusterRadiusMin + Hash01(PcgHash(ref state)) * ClusterRadiusSpan;
                    int count = SpecksMin + (int)(Hash01(PcgHash(ref state)) * SpecksSpan);
                    for (int k = 0; k < count; k++)
                    {
                        float ang = Hash01(PcgHash(ref state)) * Mathf.PI * 2f;
                        float dist = Mathf.Sqrt(Hash01(PcgHash(ref state))) * radius; // 盘内均匀
                        float sx = cx + Mathf.Cos(ang) * dist;
                        float sz = cz + Mathf.Sin(ang) * dist;
                        float sg = WaterLabReplicaSnapshot.SampleGround(sx, sz);
                        if (sg < BandGroundMin || sg >= BandGroundMax)
                            continue;
                        // 粒尺寸 = VS 同源 rand=sqrt(PCG²) ∈ [0,1)（尺度域与渲染端一致）
                        float size = Mathf.Sqrt(Hash01(PcgHash(PcgHash((uint)(cell * 31 + (uint)k) ^ Seed))));
                        if (_system.TrySpawn(new Vector3(sx, sg, sz), size))
                            spawned++;
                    }
                }
            }
            return spawned;
        }

        void Update()
        {
            if (_system == null)
                return;
            _system.Tick(Time.deltaTime);
            _system.Draw();
        }

        void OnDestroy()
        {
            _system?.Dispose();
            _system = null;
        }

        // ---- PCG hash（系统内私有实现同款；此处再备一份供布种流使用）----
        static uint PcgHash(ref uint state)
        {
            state = state * 747796405u + 2891336453u;   // 流推进（PCG 计数器步）
            uint t = (state ^ 61u) ^ (state >> 16);
            t *= 9u;
            t ^= t >> 4;
            t *= 0x27d4eb2du;
            t ^= t >> 15;
            return t;
        }

        static uint PcgHash(uint v)
        {
            uint t = (v ^ 61u) ^ (v >> 16);
            t *= 9u;
            t ^= t >> 4;
            t *= 0x27d4eb2du;
            t ^= t >> 15;
            return t;
        }

        static float Hash01(uint h)
        {
            return BitConverter.Int32BitsToSingle((int)(0x3f800000u | (h & 0x7fffffu))) - 1f;
        }
    }
}
