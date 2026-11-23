using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.WaterLab
{
    // =========================================================================
    // ReplicaPlantDriver —— 植物 13 组系统的场景自驱件（I6b 装配端）。
    //
    // 【职责边界】装配（WaterLabSetup.EnsurePlants）只建挂点树 + 材质/凸包资产 +
    // 填下面三个序列化字段；系统的 new / 分布 / 每帧 Tick+Draw 全部本件自举——
    // 与 WaterLabSimulation 同一模式（TextAsset 引用随场景序列化，构建后重开
    // 场景仍有效），不依赖装配期的任何内存态。
    //
    // 【运行时专用口径（F1 静态定案后更新）】
    //  * DistributionSeed/SeedExtra：原作每局随机（链路见 ReplicaPlantSystem 字段注释，
    //    根 = UnixTimeSeconds → configuration.seed → InitState/SetGlobalInt("seed")）——
    //    无固定真值；逐株恒等对拍 = 运行时抓值注入（工具 rt/agent-plant.js 交协调者），
    //    默认 0 = 确定性一撒（统计同源）。
    //  * AmbientOverride：cb0[40] 环境色已定案（R2 抓帧实读 0.4452/0.2876/0.2789，
    //    与 CreateLighting 的 RenderSettings.ambientLight 同值单源）。
    //    I3 GI 落地后植物环境项换 giVoxels 采样，本覆写退役。
    //  * 水场（F1 已接实时）：LiveWaterFeed 订阅 _WaterLabHeightTex/_WaterLabGroundTex
    //    （AsyncGPUReadback，256² 双线性 + 0.2s 节流，依据见该类头）；未就绪回退静态
    //    快照水面——两源在水深门后一致。
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

        /// <summary>
        /// 原作肥力场（F3，可选）：1024² 字节 = groundCells 肥力 6bit 原值/格，
        /// 语义与 dump 配方见 <see cref="ReplicaPlantSystem.MenuFertilityField"/> 类头。
        /// 原作菜单的场是运行时状态（静态链推不出、≡0/≡1 均被菜单帧证伪），此资产
        /// 由协调者按 F3 报告 §5 配方灌出后拖入引用；缺省（null）回退恒 1 并告警。
        /// </summary>
        public TextAsset FertilitySnapshot;

        /// <summary>cb0[40] 环境色（R2 抓帧实读 (0.4452, 0.2876, 0.2789)，GI-audit §② 定案；
        /// 与 CreateLighting 的 RenderSettings.ambientLight 同值单源。旧值 (0.698,…) 是
        /// level0 序列化待机态，同 σ 案被第一手推翻）。</summary>
        public static readonly Vector4 MenuAmbient = new Vector4(0.4452f, 0.2876f, 0.2789f, 1f);

        ReplicaPlantSystem[] _systems;
        ReplicaPlantSystem.LiveWaterFeed _waterFeed;

        /// <summary>当前 13 组系统（装配期检查用；未自举时 null）。</summary>
        public IReadOnlyList<ReplicaPlantSystem> Systems => _systems;

        /// <summary>实时水场回读器（Update 泵；模拟未启动时内部保持快照回退）。</summary>
        public ReplicaPlantSystem.LiveWaterFeed WaterFeed => _waterFeed;

        void Awake()
        {
            if (!WaterLabReplicaSnapshot.HasData && GroundSnapshot != null && WaterSnapshot != null)
            {
                try { WaterLabReplicaSnapshot.Load(GroundSnapshot.bytes, WaterSnapshot.bytes); }
                catch (Exception e) { Debug.LogError($"[ReplicaPlantDriver] 快照加载失败（植物门控退化）：{e.Message}"); }
            }
            if (FertilitySnapshot != null)
            {
                try { ReplicaPlantSystem.MenuFertilityField.Load(FertilitySnapshot.bytes); }
                catch (Exception e) { Debug.LogError($"[ReplicaPlantDriver] 肥力场加载失败（回退恒 1）：{e.Message}"); }
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

            _waterFeed = new ReplicaPlantSystem.LiveWaterFeed();
            _systems = new ReplicaPlantSystem[table.Length];
            int total = 0;
            var perGroup = new string[table.Length];
            var fertilityBlind = new List<string>();
            for (int i = 0; i < table.Length; i++)
            {
                var p = table[i];
                if (Hulls[i] == null || Materials[i] == null)
                {
                    Debug.LogWarning($"[ReplicaPlantDriver] 第 {i} 组（{p.ClassName}）资产缺位，跳过。");
                    continue;
                }
                var sys = new ReplicaPlantSystem(table[i], Hulls[i], Materials[i],
                    null, new ReplicaPlantSystem.LiveWaterField(_waterFeed));
                // F3 修正口径：尝试数 = ceil(capacity·0.09/1024)·1024（fac 推导链见
                // ReplicaPlantSystem.DistributionFactor 注释）——32768 的两大草各 3072，
                // 其余组 1024。
                sys.Distribute(ReplicaPlantSystem.AttemptsForCapacity(p.Capacity));
                _systems[i] = sys;
                total += sys.AliveCount;
                perGroup[i] = $"{p.ClassName}={sys.AliveCount}";
                // 【F3 静态核查】分布门是严格开区间 (min, max)（反汇编 4×lt，L112-118）；
                // 肥力场数据文件缺位时回退恒 1——肥力上界 ≤1 的组被 1.0∉(min,max) 全拒绝。
                // 【F3 实证】原作菜单帧 12/13 组存活（仅 RedAlgae=0，R 抓帧 draws_index），
                // ≡0/≡1 均非原作行为——这是数据缺口退化，显式告警不上图骗人。
                if (!ReplicaPlantSystem.MenuFertilityField.HasData && p.FertilityRange.y <= 1f)
                    fertilityBlind.Add($"{p.ClassName}(fert∈({p.FertilityRange.x},{p.FertilityRange.y}))");
            }
            Debug.Log($"[ReplicaPlantDriver] 植物系统就绪：{_systems.Length} 组 / 存活 {total} 株 "
                + $"[{string.Join(" ", perGroup)}]；尝试数=ceil(cap·0.09/1024)·1024（F3 修正 fac=0.09）；"
                + $"seed 恒 0（原作每局随机，注入协议见系统注释）");
            if (fertilityBlind.Count > 0)
                Debug.LogWarning($"[ReplicaPlantDriver] 肥力场缺失退化：{fertilityBlind.Count} 组肥力门"
                    + $"不含 1.0 → 恒空 [{string.Join(" ", fertilityBlind)}]"
                    + "（原作菜单肥力=运行时状态，静态不可得——dump 配方与读法见 F3 报告 §5）");
        }

        void Update()
        {
            if (_systems == null)
                return;
            _waterFeed?.Pump();   // 实时水场回读泵（0.2s 节流在 feed 内部）
            float dt = Time.deltaTime;
            for (int i = 0; i < _systems.Length; i++)
            {
                var sys = _systems[i];
                if (sys == null) continue;
                sys.Tick(dt);   // 生根更新（实时水场，未就绪段自动回退快照）
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
            _waterFeed?.Dispose();
            _waterFeed = null;
        }
    }
}
