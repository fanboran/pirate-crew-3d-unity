using System;
using UnityEngine;

namespace PirateCrew.WaterLab
{
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
