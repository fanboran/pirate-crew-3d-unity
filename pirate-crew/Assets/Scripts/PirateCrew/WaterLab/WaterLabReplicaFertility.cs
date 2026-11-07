using System;
using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// Sandcastle 主菜单肥力场（groundCells 6bit 通道，F3/F3b）的运行时加载器。
    ///
    /// 【真值来源（第一手，静态证据链）】肥力在 groundCells 每格 16B 的 dword3[26..32)
    /// 6bit：打包 = Ground Paint kernel `sat·63+0.5 <<26`（bin.5 L305-308），解码 =
    /// Plant Distribution `ushr l(16,8,12,26)` 后 `×0.015873(=1/63)`（v0_Distribution_0
    /// L67/L72）；C# 侧 Ground.GroundCell.fertility（Ground.cs:23）只承载 32B 笔刷载荷，
    /// GPU 权威在 <c>Ground.input</c> ComputeBuffer。场值 = 运行时 Paint 累积状态
    /// （F3 §0.2：菜单布局代码传给 Paint 的肥力字恒 0，非零场是运行时注入——静态不可推），
    /// 由协调者按 rt/reports/F3-fertility.md §5 配方用 rt/agent-fertility.js 一次性
    /// dump（<c>byte[i*16+15]>>2</c>，1024² 行主序 row=Z、row0=z−15）产出
    /// <c>MenuFertility1024.bytes</c> 后入库 Assets/Art/WaterLabReplica/。
    ///
    /// 【统计锁（加载校验，仿 WaterLabReplicaObstacles 的 meta 对账模式）】装载时重算
    /// dump 侧同款统计（64 桶直方图、>0 格占比）并钉常量：直方图结构与占比下限是
    /// 结构性约束（全零场必拒——F3 §2.1/§2.2 已证菜单运行时场必含非零：12/13 组存活）；
    /// 占比精确锁 <see cref="MetaNonZeroFrac"/> 取自主菜单 dump 实测（资产错位/错轴当场抛）。
    /// 任何校验失败都抛异常且<b>不留下半加载状态</b>（_cells 只在校验全过后赋值）。
    ///
    /// 【纯 C#】只吃 byte[]、吐 float（÷63 还原 [0,1]），harness 无头可测；Unity 侧由
    /// <see cref="ReplicaPlantSystem.MenuFertilityField"/> 薄壳转发接线
    /// （ReplicaPlantDriver.FertilitySnapshot TextAsset → Load）。
    /// </summary>
    public static class WaterLabReplicaFertility
    {
        /// <summary>直方图桶数 = 6bit 值域（0..63 每值一桶，dump 侧同款统计）。</summary>
        public const int HistogramBuckets = 64;

        // ---------------- 统计锁常量 ----------------
        /// <summary>
        /// >0 格占比的实测值（agent-fertility.js 主菜单 dump：nonzeroCount 841150 / 1048576；
        /// 直方图峰 63:395956、0:207426——38% 格饱和在 1.0、20% 为 0，(0.8,1.0) 带稀疏，
        /// 与 F3 §2.2 从 12/13 组存活反推的场形状相洽）。
        /// </summary>
        public const float MetaNonZeroFrac = 0.80218315f;

        /// <summary>占比精确锁容差（占比由同一资产重算，确定性；只留 float 舍入余量）。</summary>
        public const float MetaNonZeroFracTolerance = 1e-5f;

        /// <summary>占比下限：全零场必拒（≡0 已被 F3 §2.2 实测证伪——非结构约束不随 dump 变）。</summary>
        public const float MinNonZeroFrac = 1e-4f;

        /// <summary>主菜单肥力资产的每轴格数（Ground 层 1024²）；精确锁只对该尺寸生效，
        /// 合成小场（契约测试）只过结构校验。</summary>
        public const int MenuResolution = 1024;

        const int MaxCells = 4 * 1024 * 1024;   // 防错读：菜单 1024²=1,048,576

        static byte[] _cells;
        static int _res;
        static int[] _lastHistogram;
        static float _lastNonZeroFrac;

        /// <summary>是否已装载（未载时 <see cref="GetFertility"/> 回退恒 1）。</summary>
        public static bool HasData => _cells != null;

        /// <summary>场每轴格数（N² 字节的 N；未载时 0）。</summary>
        public static int Resolution => _res;

        /// <summary>最近一次装载的 64 桶直方图（副本；未载时 null）。</summary>
        public static int[] LastHistogram => _lastHistogram;

        /// <summary>最近一次装载的 >0 格占比（未载时 NaN）。</summary>
        public static float LastNonZeroFrac => _lastNonZeroFrac;

        /// <summary>
        /// 装载肥力场（幂等覆盖）。校验序：平方数长度 → 6bit 值域 → 统计锁
        /// （直方图重算 + >0 占比）。任何失败抛 <see cref="ArgumentException"/>，
        /// 不触碰 <see cref="_cells"/>（无半态）。
        /// </summary>
        public static void Load(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            int n = (int)Mathf.Sqrt(bytes.Length);
            if (n * n != bytes.Length)
                throw new ArgumentException(
                    $"肥力场长度 {bytes.Length} 不是平方数（应为 N² 字节；dump 产物 1024²={1024 * 1024}）");
            if (n * n > MaxCells)
                throw new ArgumentException($"肥力场 {n}² 超出合理量程（≤{MaxCells} 格）——疑似错资产");

            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] > 63)
                    throw new ArgumentException($"肥力场 byte[{i}]={bytes[i]} 超出 6bit 值域 [0,63]");
            }

            // 统计（dump 侧 agent-fertility.js 同款：64 桶直方图 + >0 格占比）
            var hist = new int[HistogramBuckets];
            int nonzero = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                hist[bytes[i]]++;
                if (bytes[i] > 0) nonzero++;
            }
            float frac = nonzero / (float)bytes.Length;
            if (frac < MinNonZeroFrac)
                throw new ArgumentException(
                    $"肥力场 >0 格占比 {frac:G4} 低于下限 {MinNonZeroFrac:G4}——全零场（≡0 已被" +
                    "F3 §2.2 实测证伪，疑 dump 到未 Paint 的初始化缓冲或错位读取）");
            if (n == MenuResolution && Mathf.Abs(frac - MetaNonZeroFrac) > MetaNonZeroFracTolerance)
                throw new ArgumentException(
                    $"肥力场 >0 格占比 {frac:G7} != dump 实测 {MetaNonZeroFrac:G7}（±{MetaNonZeroFracTolerance}）——资产或解码错位");

            // 校验全过才落状态（不留半态）
            _cells = bytes;
            _res = n;
            _lastHistogram = hist;
            _lastNonZeroFrac = frac;
        }

        /// <summary>卸载（回退恒 1；测试隔离用）。</summary>
        public static void Unload()
        {
            _cells = null;
            _res = 0;
            _lastHistogram = null;
            _lastNonZeroFrac = float.NaN;
        }

        /// <summary>取一格肥力（行主序 row=Z、col=X；÷63 还原 [0,1]；未载回退恒 1）。</summary>
        public static float GetFertility(int cx, int cz)
        {
            if (_cells == null)
                return 1f;
            return _cells[cz * _res + cx] / 63f;
        }

        /// <summary>单行对账摘要（模拟 Awake 日志用）。</summary>
        public static string Describe()
        {
            if (!HasData)
                return "fertility[未加载（门控回退恒 1，数据缺口退化）]";
            string lockState = _res == MenuResolution
                ? $"占比精确锁 {MetaNonZeroFrac:G7}±{MetaNonZeroFracTolerance}"
                : "非菜单尺寸（仅结构校验）";
            return $"fertility[{_res}² 行主序 row=Z，>0 占比 {LastNonZeroFrac:G7}，{lockState}]";
        }
    }
}
