using System;
using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// Sandcastle 主菜单障碍场（obstaclesMap 泵的数据件，D 流件2）的运行时加载器。
    ///
    /// 【真值来源与推导】B2 快照（rt/dumps/menu-snapshot/）的 groundHeight 含烘焙障碍、
    /// sandAmount 是纯沙（snapshot_meta.json obstacles.note）；障碍增量 =
    /// max(groundHeight − sandAmount, 0) 阈值化（deltaThreshold=0.02）。推导场与 B2 meta
    /// 四项统计逐位对账：超阈格占比 0.0059356689（meta cellFrac 同值）、maxDelta 1.332135
    /// @ row/col (197,233)、row 范围 [11,206]、col 范围 [35,244]。产物
    /// <c>Assets/Art/WaterLabReplica/MenuSand256.bytes</c>（sandAmount 快照，256² float32
    /// 小端）与已入库的 MenuGround256.bytes 成对；加载时重算 delta 并钉 B2 统计。
    ///
    /// 【泵场语义（D 定案）】原作 obstaclesMap 是顶视渲染障碍 mesh 的 RGHalf RT（绝对
    /// 高程）；泵门控 `sandSurface &lt; obstacleTop ≤ η &lt; obstacleTop+0.1`（§9）。我方
    /// obstacleTop = 活沙场 height（t0=groundHeight 含障碍）、sandSurface = height −
    /// <see cref="Delta"/>——与本加载器的推导场等价。
    ///
    /// 【纯 C#】只吃 byte[]、吐 float，harness 无头可测；Unity 侧由
    /// <see cref="WaterLabSimulation"/> 的 TextAsset 接线。
    /// </summary>
    public static class WaterLabReplicaObstacles
    {
        /// <summary>障碍场每轴格数（= 快照 Readback 层 256²，行主序 row=Z）。</summary>
        public const int Resolution = WaterLabReplicaSnapshot.Resolution;

        /// <summary>障碍判定阈值（m）：低于此值的 groundHeight−sandAmount 差不算障碍
        ///（B2 dump 的 deltaThreshold）。</summary>
        public const float DeltaThreshold = 0.02f;

        const int BytesPerCell = 4;
        const int ExpectedBytes = Resolution * Resolution * BytesPerCell;

        // B2 meta obstacles 统计锁（加载校验：字段错位/错资产当场抛异常）
        public const float MetaCellFrac  // Tests 独立程序集可见 = 0.0059356689453125f;
        public const float MetaMaxDelta  // Tests 独立程序集可见 = 1.332135f;
        public const float CellFracTolerance  // Tests 独立程序集可见 = 1e-4f;
        public const float MaxDeltaTolerance  // Tests 独立程序集可见 = 1e-3f;

        static float[] _delta;

        /// <summary>障碍场已加载。</summary>
        public static bool HasData => _delta != null;

        /// <summary>障碍增量场（只读约定；行主序 row=Z、col=X，m，阈下为 0）。</summary>
        public static float[] Delta => _delta;

        /// <summary>
        /// 载入障碍场：从 ground/sand 两个 256² float32 快照重算 delta 并对账 B2 统计。
        /// 任何校验失败都抛异常且<b>不留下半加载状态</b>——调用方捕获后禁用泵并显式报错。
        /// </summary>
        public static void Load(byte[] groundBytes, byte[] sandBytes)
        {
            var ground = Decode(groundBytes, "groundHeight");
            var sand = Decode(sandBytes, "sandAmount");

            var delta = new float[Resolution * Resolution];
            int over = 0;
            float maxDelta = 0f;
            for (int i = 0; i < delta.Length; i++)
            {
                float d = ground[i] - sand[i];
                if (d > DeltaThreshold)
                {
                    delta[i] = d;
                    over++;
                    if (d > maxDelta) maxDelta = d;
                }
            }

            float frac = over / (float)delta.Length;
            if (Mathf.Abs(frac - MetaCellFrac) > CellFracTolerance)
                throw new ArgumentException(
                    $"障碍场超阈格占比 {frac:G7} != B2 实测 {MetaCellFrac:G7}——推导或资产错位");
            if (Mathf.Abs(maxDelta - MetaMaxDelta) > MaxDeltaTolerance)
                throw new ArgumentException(
                    $"障碍场最大增量 {maxDelta:F6} != B2 实测 {MetaMaxDelta:F6}——推导或资产错位");

            _delta = delta;
        }

        /// <summary>卸载障碍场（泵禁用开关）。</summary>
        public static void Unload()
        {
            _delta = null;
        }

        /// <summary>单行对账摘要（模拟 Awake 的日志用）。</summary>
        public static string Describe()
        {
            if (!HasData)
                return "obstacles[未加载（泵禁用）]";
            float sum = 0f;
            for (int i = 0; i < _delta.Length; i++)
                sum += _delta[i];
            return $"obstacles[256² 阈值 {DeltaThreshold}，max {MetaMaxDelta:F3}]";
        }

        static float[] Decode(byte[] bytes, string field)
        {
            if (bytes == null || bytes.Length != ExpectedBytes)
                throw new ArgumentException(
                    $"{field} 快照长度 {(bytes?.Length ?? 0)} != 期望 {ExpectedBytes}（256² float32）");
            var data = new float[Resolution * Resolution];
            Buffer.BlockCopy(bytes, 0, data, 0, ExpectedBytes);
            for (int i = 0; i < data.Length; i++)
            {
                if (float.IsNaN(data[i]) || float.IsInfinity(data[i]))
                    throw new ArgumentException($"{field} 快照第 {i} 格出现 NaN/Inf");
            }
            return data;
        }
    }
}
