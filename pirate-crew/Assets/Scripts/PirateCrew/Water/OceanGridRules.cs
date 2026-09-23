using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Water
{
    /// <summary>
    /// 大海域海面的分级网格规则（纯 C#，无头可测）。
    ///
    /// 【域分层（docs/大海域世界化.md §5.2）】替换"地图外扩 400u 的 Cube"：
    ///   · 近场：均匀细网格（格距 <see cref="CellSize"/>）到半径 <see cref="UniformRadius"/>——
    ///     解析最短 chop 波长 4.8u（每波 ≥3 顶点），跟随相机、按 <see cref="SnapStep"/> 步进对齐防泳动；
    ///   · 中场：环宽按 <see cref="RingGrowth"/> 几何增长的环带（远处的波本来就被
    ///     <see cref="OceanRules.WaveGeometricFade"/> 淡出几何位移，粗网格只承载法线/颜色）；
    ///   · 远场：裙边一直铺到 <see cref="HorizonRadius"/> ≥ 4000u 的地平线，位移衰减到 0、颜色融进雾色。
    /// 整片海是**一张径向圆盘网格**（中心顶点 + 同心环），无接缝、无重叠、单 DrawCall。
    ///
    /// 【防泳动的口径】网格平移按 <see cref="SnapStep"/>（=近场格距）对齐：
    /// 相机移动不足一步时网格原地不动，采样点不漂；位移场本身由世界坐标驱动，
    /// 逐像素解析法线与网格无关 → 明暗永不泳动，只有几何边缘存在亚格距级误差（近场被 1.6u 格距压住）。
    /// </summary>
    public static class OceanGridRules
    {
        /// <summary>近场格距（世界单位）。与 <see cref="WaterMeshRules.DefaultCellSize"/> 同口径：最短 chop 波长 4.8 → 每波约 3 格。</summary>
        public const float CellSize = 1.6f;

        /// <summary>均匀细网格半径（世界单位）。128 与 WaterSimRules.DefaultDomainSize 同尺度，覆盖竞技场核心区。</summary>
        public const float UniformRadius = 128f;

        /// <summary>
        /// 中场环带宽度增长率（每环比上一环宽 15%）。
        /// 【可见海域预算】增长率决定"波几何在多远还能被网格解析"：λ120 长涌全解析要求
        /// 环宽 ≤ λ/3 = 40u（<see cref="OceanRules.WaveGeometricFade"/> 达 1.0 的线性区）。
        /// 1.15 下全涌起点（最大世界地图 ≈306u）处环宽 ≈26u < 40 → 长涌一进画面就是完整位移；
        /// 旧值 1.25 时 300u 处环宽已 ≈45u、~500u 前几何位移全灭，长涌包络被网格密度枪毙。
        /// 代价核对：裙边到 4200u 总环数 ~123 环（均匀区浮点累加实际步进 81 格到 ≈129.6 + 增长 42 环）
        /// → 顶点 ≈3.16 万、三角面 ≈6.27 万，仍在 16 位索引内（<see cref="Needs32BitIndices"/> 为 false；
        /// OceanRig 的 32 位索引路径按需兜底）。
        /// </summary>
        public const float RingGrowth = 1.15f;

        /// <summary>地平线裙边半径（世界单位）。M4 §1/§5 硬要求 ≥ 4000。</summary>
        public const float HorizonRadius = 4200f;

        /// <summary>圆盘分段数（绕一圈的顶点数）。</summary>
        public const int Segments = 256;

        /// <summary>网格平移对齐步长（世界单位）= 近场格距。</summary>
        public static float SnapStep => CellSize;

        /// <summary>均匀区环数（半径 0→<see cref="UniformRadius"/>）。</summary>
        public static int UniformRingCount => Mathf.Max(1, Mathf.RoundToInt(UniformRadius / CellSize));

        /// <summary>
        /// 生成环半径梯子（升序，单位：世界单位）。
        /// 返回值 [0] = 0（圆盘中心），其后均匀区每环 +<see cref="CellSize"/>，
        /// 越过 <see cref="UniformRadius"/> 后每环宽度 ×<see cref="RingGrowth"/>，
        /// 最后一环半径 ≥ <see cref="HorizonRadius"/>（硬契约，由 <see cref="HorizonRadius"/> 断言测试）。
        /// </summary>
        public static float[] RingRadii()
        {
            var radii = new List<float>(UniformRingCount + 48) { 0f };
            float r = 0f;
            float width = CellSize;

            while (r < HorizonRadius)
            {
                r += width;
                radii.Add(r);
                // 越过均匀区后开始几何增长（增长的判定看"下一环起点是否已出均匀区"）。
                if (r >= UniformRadius)
                    width *= RingGrowth;
            }

            return radii.ToArray();
        }

        /// <summary>顶点数 = 中心 1 + 环数 ×（<see cref="Segments"/>+1）（每环重复一个缝合顶点，省去取模）。</summary>
        public static int VertexCount(int ringCount)
        {
            return 1 + Mathf.Max(ringCount, 1) * (Segments + 1);
        }

        /// <summary>三角面数 = 中心扇 <see cref="Segments"/> + 其余环带 ×2。</summary>
        public static int TriangleCount(int ringCount)
        {
            int rings = Mathf.Max(ringCount, 1);
            return Segments + (rings - 1) * Segments * 2;
        }

        /// <summary>索引数（= 三角面数 × 3）。</summary>
        public static int IndexCount(int ringCount)
        {
            return TriangleCount(ringCount) * 3;
        }

        /// <summary>顶点数是否超过 16 位索引上限（需要切 32 位索引）。</summary>
        public static bool Needs32BitIndices(int ringCount)
        {
            return VertexCount(ringCount) > 65535;
        }

        /// <summary>
        /// 半径 r 处的环宽（世界单位）——shader 里 <c>OceanLocalCellSize</c> 的 C# 镜像，
        /// 供 <see cref="OceanRules.WaveGeometricFade"/> 判定"当前网格能否解析这条波"。
        /// 均匀区内恒 <see cref="CellSize"/>；之外的闭式解与 <see cref="RingRadii"/> 的累加梯子一致
        /// （同调日志/同调 pow，环边界处的半环误差对淡出无感）。
        /// 【可见海域预算锚点】RingGrowth=1.15 时 300u 处环宽 ≈26u（旧 1.25 时 ≈45u）：
        /// λ120 长涌在该距离的几何淡出 = clamp01((120/(3×26) − 0.7)/0.3) = 1.0，位移完整。
        /// </summary>
        public static float RingWidthAtRadius(float radius)
        {
            if (radius <= UniformRadius)
                return CellSize;

            // 累加梯子的反解：r_k = U + cell·(g^k − 1)/(g − 1) ⇒ k = floor(log(1+(r−U)(g−1)/cell)/log g)。
            float x = 1f + (radius - UniformRadius) * (RingGrowth - 1f) / CellSize;
            float k = Mathf.FloorToInt(Mathf.Log(Mathf.Max(x, 1f)) / Mathf.Log(RingGrowth));
            return CellSize * Mathf.Pow(RingGrowth, Mathf.Max(k, 0));
        }
    }
}
