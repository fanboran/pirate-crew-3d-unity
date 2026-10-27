using System;
using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// Sandcastle 主菜单 4bit 耦合权重场的运行时加载器（W1，B2 定案的数据件）。
    ///
    /// 【真值来源】frida 只读注入在 Game.Update 主线程内快照的水格 dword3 bits28..31
    ///（`h += (w/15)·(target−h)` 的逐格权重，UpdateAmount 反汇编 + 运行时双验证），
    /// 产物 <c>Assets/Art/WaterLabReplica/MenuWeights512.bytes</c>——512² float32 小端、
    /// 行主序 row=Z、值 = 权重/15，与 <see cref="WaterLabRules.WaterCells"/> 同分辨率直用。
    /// 证据链：<c>external/sandcastle-ref/rt/reports/B2-phase-weights.md</c>（§2 静态链 /
    /// §3 实测 / §9 产物）。
    ///
    /// 【B2 定案语义】权重场是**静态场**（运行时 4 dump 逐格一致率 1.000）：主菜单
    /// 全域 ≡0，仅背边缘 row 496..511（world Z∈[+14.06,+15]）有一条
    /// [1..8,8..15]/15 的 ramp——造波桨带。海区（≈49% 格）不被潮位目标钉住，是自由
    /// SWE 场；「初始淹没区满耦」的旧代理（<see cref="WaterLabRules.CouplingWeightAt"/>）
    /// 在复刻路径退役，仅保留作非回退装配的权重数据源。
    ///
    /// 【playTime 锚点】<see cref="PlayTimeOrigin"/> = B2 同相 dump 组 t0 的
    /// Game.playTime 运行时真值（phase/meta.json：622.4888 s，mod 2π → φ₀=0.4534 rad）。
    /// 用绝对值而非 mod 相位：潮位项 `amp·sin(playTime)` 只要相位（mod 2π），但振幅
    /// 渐起 `saturate(playTime×0.05)` 要 20 s 后才饱和——只有绝对 playTime 同时复现两者。
    ///
    /// 【纯 C#】只吃 byte[]、吐 float，不碰 Unity 原生对象——harness 无头测试直接
    /// Load 钉契约；Unity 侧由 <see cref="WaterLabSimulation"/> 的 TextAsset 接线。
    /// 加载校验按 B2 §3 实测锁：值域 [0,1]、桨带外全零、桨带 16 行逐行恒值且恰为
    /// k/15 序列——字段错位/错资产当场抛异常回退代理，不静默带病运行。
    /// </summary>
    public static class WaterLabReplicaWeights
    {
        /// <summary>权重场每轴格数（= 原作 Water 层 512²，行主序 row=Z）。</summary>
        public const int Resolution = WaterLabRules.WaterCells;

        /// <summary>造波桨带首行（含）：world Z∈[+14.06,+15] 的背边缘 16 行。</summary>
        public const int PaddleRowFirst = 496;

        /// <summary>造波桨带末行（含）。</summary>
        public const int PaddleRowLast = 511;

        /// <summary>
        /// ReplicaMode 的 playTime 起始偏移（s）：B2 同相 dump 组 t0 的 Game.playTime
        /// 运行时真值。原作潮位时钟 = playTime（Game.cs:855/1045/1504 + UpdateAmount
        /// 的 sincos cb0[113].z，B2 §4 实证：同相对桨带钉定行水量差 ≤0.0046 m，排除壁钟）。
        /// </summary>
        public const float PlayTimeOrigin = 622.48876953125f;

        /// <summary>原资产（coupling-weights.bin）的 md5——契约测试对账用（B2 §9 产物）。</summary>
        public const string TruthMd5 = "4a2c62397000f7ff4d92a7bdb09fcd4c";

        const int BytesPerCell = 4;
        const int ExpectedBytes = Resolution * Resolution * BytesPerCell;

        /// <summary>桨带 16 行的权重序列（×15），"8" 重复一次——15 级装 16 行（B2 §3
        /// readback 双线性 pair 和 [3,7,11,15,17,21,25,29] 唯一反解）。</summary>
        static readonly int[] PaddleRampOver15 = { 1, 2, 3, 4, 5, 6, 7, 8, 8, 9, 10, 11, 12, 13, 14, 15 };

        static float[] _weights;

        /// <summary>权重场已加载（复刻路径用真值场；未加载 = 回退代理场）。</summary>
        public static bool HasData => _weights != null;

        /// <summary>原始场（只读约定；行主序 row=Z、col=X，值 = 4bit 权重/15）。测试对账用。</summary>
        public static float[] Weights => _weights;

        /// <summary>
        /// 载入权重场。任何校验失败（长度/值域/桨带结构不符 B2 实测）都抛异常且
        /// <b>不留下半加载状态</b>——调用方捕获后回退 <see cref="WaterLabRules.CouplingWeightAt"/>
        /// 代理场并显式报错。
        /// </summary>
        public static void Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length != ExpectedBytes)
                throw new ArgumentException(
                    $"耦合权重场长度 {(bytes?.Length ?? 0)} != 期望 {ExpectedBytes}（512² float32）");
            var data = new float[Resolution * Resolution];
            Buffer.BlockCopy(bytes, 0, data, 0, ExpectedBytes);

            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i];
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f || v > 1f)
                    throw new ArgumentException($"耦合权重场第 {i} 格出现 {v}，值域应 [0,1]——解码疑似错位");
            }

            // B2 §3 实测锁：桨带外全域 ≡0（海区自由 SWE 场的直接证据）
            for (int row = 0; row < PaddleRowFirst; row++)
            {
                int offset = row * Resolution;
                for (int col = 0; col < Resolution; col++)
                {
                    if (data[offset + col] != 0f)
                        throw new ArgumentException(
                            $"耦合权重场 row {row} col {col} = {data[offset + col]}，桨带外应全零（B2 §3 实测）");
                }
            }

            // 桨带 16 行：逐行全列恒值，恰为 k/15 ramp（行 502/503 与 503/504 之间 "8" 重复）
            for (int row = PaddleRowFirst; row <= PaddleRowLast; row++)
            {
                float expected = PaddleRampOver15[row - PaddleRowFirst] / 15f;
                int offset = row * Resolution;
                for (int col = 0; col < Resolution; col++)
                {
                    if (Mathf.Abs(data[offset + col] - expected) > 1e-6f)
                        throw new ArgumentException(
                            $"耦合权重场 row {row} col {col} = {data[offset + col]}，应恒为 {expected}"
                            + "（B2 §3 桨带 ramp 实测不符）");
                }
            }

            _weights = data;
        }

        /// <summary>卸载权重场（回退代理场的开关）。</summary>
        public static void Unload()
        {
            _weights = null;
        }

        /// <summary>桨带行数（校验与契约测试用）。</summary>
        public static int PaddleRows => PaddleRowLast - PaddleRowFirst + 1;

        /// <summary>单行对账摘要（模拟 Awake 的 ReplicaMode 日志用）。</summary>
        public static string Describe()
        {
            return HasData
                ? $"weights[512² 桨带 row {PaddleRowFirst}..{PaddleRowLast} ramp 1..15/15，其余 0] playTime₀={PlayTimeOrigin:F4}s"
                : "weights[未加载（回退代理场）]";
        }
    }
}
