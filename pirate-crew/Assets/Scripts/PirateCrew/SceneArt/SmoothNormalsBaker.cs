using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.SceneArt
{
    /// <summary>
    /// 反向壳描边的**平滑法线**烘焙器（等距像素卡通渲染篇 §5；调研-反向壳 §3：
    /// 角度加权平均 + 位置容差合并，优于等权平均）。
    ///
    /// 【它解决什么】本项目程序化几何（<see cref="MeshBuffers"/>）是**每面独立顶点、面法线硬边**
    /// 的低模——反壳描边直接沿硬边法线外扩会在折缝处裂线（风险 R1）。烘焙把「空间上重合的
    /// 顶点」按共享三角形的**内角加权**平均出面法线，得到跨面连续的平滑法线，编码进顶点色。
    ///
    /// 【顶点色编码（裁决 #8 提案）】R = 色带阈值偏移（0.5 = 无偏移，Xrd 工作流）；
    /// GBA = 平滑法线 <c>×0.5+0.5</c>（8bit 编码；shader 侧 <c>×2-1</c> 解码）。
    /// 未烘焙网格顶点色 alpha≈0，shader 回退 normalOS（PirateToon 的 ToonInk Pass 已处理）。
    ///
    /// 【位置容差的边界】量化桶按 <c>round(pos / quantize)</c> 归并。程序化几何的重合顶点
    /// 来自相同计算的 bit 级相同坐标，跨桶不发生；外部导入网格若存在「近而不等」的裂缝顶点，
    /// 需要更大的 quantize 或先 weld——本烘焙器的调用方目前只有程序化烘焙链，不处理该情况。
    ///
    /// 【纯 C# 纪律】不触碰 Mesh/Texture 等 Unity 对象实例化（ECall 限制），可在无头验证台跑；
    /// Unity 侧薄壳（Mesh.SetColors）由 Editor 装配脚本承担。
    /// </summary>
    public static class SmoothNormalsBaker
    {
        /// <summary>
        /// 烘焙平滑法线。
        /// </summary>
        /// <param name="vertices">顶点位置（长度 = 顶点数）。</param>
        /// <param name="triangles">三角形索引（长度 = 3 × 面数；顶点可复用可不复用）。</param>
        /// <param name="normals">逐顶点法线（回退用：位置桶的法线加权和退化为近零向量时用它）。</param>
        /// <param name="quantize">位置合并量化粒度（世界单位；默认 1e-4 = 0.1mm）。</param>
        /// <returns>逐顶点颜色：R=0.5（阈值偏移中性），GBA=平滑法线 ×0.5+0.5。</returns>
        public static Color[] Bake(Vector3[] vertices, int[] triangles, Vector3[] normals, float quantize = 1e-4f)
        {
            int vertexCount = vertices.Length;
            var colors = new Color[vertexCount];

            // ---- 1. 位置桶：量化坐标 → 该位置的三角形贡献列表（triangleId, 内角）----
            // 同一三角形对每个顶点位置各贡献一次内角；同桶重复三角形（退化）丢弃。
            var buckets = new Dictionary<(long, long, long), List<(int tri, float angle)>>();
            float inv = 1f / quantize;

            for (int t = 0; t * 3 + 2 < triangles.Length; t++)
            {
                int i0 = triangles[t * 3], i1 = triangles[t * 3 + 1], i2 = triangles[t * 3 + 2];
                Vector3 a = vertices[i0], b = vertices[i1], c = vertices[i2];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-16f)
                    continue; // 退化三角形：无面积无面法线，不参与平均

                // 三个顶点处的内角（内角和 = π，权重与面积无关——正是「角度加权」）。
                float angA = Vector3.Angle(b - a, c - a);
                float angB = Vector3.Angle(a - b, c - b);
                float angC = 180f - angA - angB; // 减法收回舍入误差

                AddContribution(buckets, a, t, angA, inv);
                AddContribution(buckets, b, t, angB, inv);
                AddContribution(buckets, c, t, angC, inv);
            }

            // ---- 2. 桶 → 平滑法线（角度加权和归一化；近零回退标记 NaN）----
            var bucketNormals = new Dictionary<(long, long, long), Vector3>(buckets.Count);
            foreach (var kv in buckets)
            {
                Vector3 sum = Vector3.zero;
                foreach ((int tri, float angle) in kv.Value)
                {
                    int i0 = triangles[tri * 3], i1 = triangles[tri * 3 + 1], i2 = triangles[tri * 3 + 2];
                    Vector3 fn = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]).normalized;
                    sum += fn * angle;
                }

                // 近零（正反双面法线相消等）标记 NaN → 逐顶点回退自身法线。
                bucketNormals[kv.Key] = sum.sqrMagnitude > 0.01f ? sum.normalized : new Vector3(float.NaN, 0f, 0f);
            }

            // ---- 3. 逐顶点写色 ----
            for (int i = 0; i < vertexCount; i++)
            {
                var key = Quantize(vertices[i], inv);
                Vector3 n = bucketNormals.TryGetValue(key, out Vector3 smooth) && !float.IsNaN(smooth.x)
                    ? smooth
                    : normals[i];

                colors[i] = new Color(0.5f, n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
            }

            return colors;
        }

        static void AddContribution(Dictionary<(long, long, long), List<(int tri, float angle)>> buckets,
            Vector3 pos, int tri, float angle, float inv)
        {
            var key = Quantize(pos, inv);
            if (!buckets.TryGetValue(key, out List<(int, float)> list))
            {
                list = new List<(int, float)>();
                buckets[key] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Item1 == tri)
                    return; // 同一三角形已在该桶（两个顶点量化重合的退化情形）
            }

            list.Add((tri, angle));
        }

        static (long, long, long) Quantize(Vector3 p, float inv)
        {
            return ((long)Mathf.Round(p.x * inv), (long)Mathf.Round(p.y * inv), (long)Mathf.Round(p.z * inv));
        }
    }
}
