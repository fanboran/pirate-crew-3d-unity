using NUnit.Framework;
using PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 平滑法线烘焙器的几何契约（等距像素卡通渲染篇 §5；角度加权 + 位置容差合并）。
    /// 断言对象是纯 C# 输出（Color[] 编码），可在无头验证台跑。
    /// </summary>
    public class SmoothNormalsBakerTests
    {
        static (Vector3[] v, int[] t, Vector3[] n) FromBuffers(MeshBuffers buffers)
        {
            return (buffers.ToVertices(), buffers.ToTriangles(), buffers.ToNormals());
        }

        static Color[] Bake(MeshBuffers buffers)
        {
            var (v, t, n) = FromBuffers(buffers);
            return SmoothNormalsBaker.Bake(v, t, n);
        }

        /// <summary>解码顶点色的 GBA 通道回法线（shader ToonInk Pass 同式：×2-1）。</summary>
        static Vector3 Decode(Color c)
        {
            return new Vector3(c.g * 2f - 1f, c.b * 2f - 1f, c.a * 2f - 1f);
        }

        // ------------------------------------------------------------------
        // 立方体角点：3 个正方形面各贡献 90° 内角 → 均匀平均 = 体对角方向
        // ------------------------------------------------------------------
        [Test]
        public void CubeCorner_BlendsThreeFaces_ToBodyDiagonal()
        {
            var buffers = new MeshBuffers();
            buffers.AddBox(Vector3.zero, Vector3.one, 0f);
            var (v, t, n) = FromBuffers(buffers);
            Color[] colors = SmoothNormalsBaker.Bake(v, t, n);

            // 角点 (0.5,0.5,0.5) 上的所有顶点（MeshBuffers 每面独立顶点 → 多个顶点同位置）
            // 平滑法线都应指向 (+1,+1,+1)/√3。
            Vector3 expected = new Vector3(1f, 1f, 1f).normalized;
            int checkedCount = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (Vector3.Distance(v[i], new Vector3(0.5f, 0.5f, 0.5f)) < 1e-4f)
                {
                    Assert.That(Decode(colors[i]), Is.EqualTo(expected).Using(Vector3Within(1e-3f)),
                        $"角点顶点 {i} 的平滑法线应为体对角方向");
                    checkedCount++;
                }
            }

            Assert.That(checkedCount, Is.GreaterThanOrEqualTo(3), "立方体角点应至少被 3 个面共享");
        }

        // ------------------------------------------------------------------
        // 同面三角形：位置桶内法线相同 → 平滑法线 = 面法线（无畸变）
        // ------------------------------------------------------------------
        [Test]
        public void FlatQuad_AllVerticesKeepFaceNormal()
        {
            var buffers = new MeshBuffers();
            buffers.AddQuad(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, 1f), new Vector3(0f, 0f, 1f), Vector3.up);
            var (v, t, n) = FromBuffers(buffers);
            Color[] colors = SmoothNormalsBaker.Bake(v, t, n);

            for (int i = 0; i < v.Length; i++)
                Assert.That(Decode(colors[i]), Is.EqualTo(Vector3.up).Using(Vector3Within(1e-3f)),
                    $"平面上顶点 {i} 的平滑法线应保持 +Y");
        }

        // ------------------------------------------------------------------
        // 双面贴片：正反法线相消 → 回退顶点自身法线（不产出 NaN / 零向量）
        // ------------------------------------------------------------------
        [Test]
        public void DoubleSidedSheet_FallsBackToOwnNormal()
        {
            var buffers = new MeshBuffers();
            buffers.AddTrianglesDoubleSided(
                new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
                new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 0f), Vector3.back);
            var (v, t, n) = FromBuffers(buffers);
            Color[] colors = SmoothNormalsBaker.Bake(v, t, n);

            for (int i = 0; i < v.Length; i++)
            {
                Vector3 decoded = Decode(colors[i]);
                Assert.IsFalse(float.IsNaN(decoded.x) || float.IsNaN(decoded.y) || float.IsNaN(decoded.z),
                    $"顶点 {i} 解码出 NaN");
                // 回退语义：解码结果 = 该顶点自身的面法线（正面顶点 -Z、反面顶点 +Z）。
                Assert.That(decoded, Is.EqualTo(n[i]).Using(Vector3Within(1e-3f)),
                    $"双面贴片顶点 {i} 应回退自身法线");
            }
        }

        // ------------------------------------------------------------------
        // 编码契约：R 恒 0.5（阈值偏移中性），GBA 有界
        // ------------------------------------------------------------------
        [Test]
        public void Encoding_RIsNeutralHalf_GbaBounded()
        {
            var buffers = new MeshBuffers();
            buffers.AddBox(Vector3.zero, Vector3.one, 0f);
            buffers.AddRock(new Vector3(3f, 0f, 0f), 0.5f, Vector3.one, 7);
            var (v, t, n) = FromBuffers(buffers);
            Color[] colors = SmoothNormalsBaker.Bake(v, t, n);

            Assert.That(colors.Length, Is.EqualTo(v.Length));
            for (int i = 0; i < colors.Length; i++)
            {
                Assert.That(colors[i].r, Is.EqualTo(0.5f).Within(1e-6f), $"顶点 {i} R 通道应为中性 0.5");
                Assert.That(colors[i].g, Is.InRange(0f, 1f), $"顶点 {i} G 编码越界");
                Assert.That(colors[i].b, Is.InRange(0f, 1f), $"顶点 {i} B 编码越界");
                Assert.That(colors[i].a, Is.InRange(0f, 1f), $"顶点 {i} A 编码越界");
                // 解码后必为单位向量（描边外扩方向）。
                Assert.That(Decode(colors[i]).magnitude, Is.EqualTo(1f).Within(2e-2f),
                    $"顶点 {i} 解码法线长度应接近 1");
            }
        }

        // ------------------------------------------------------------------
        // 共享棱：正面(+Z) 与顶面(+Y) 相折 → 棱上平滑法线 = 两面法线的 45° 角平分
        // （两面在棱上各贡献 90° 总内角，权重相等 → 严格角平分）
        // ------------------------------------------------------------------
        [Test]
        public void FoldedEdge_BlendsToFortyFiveDegrees()
        {
            var buffers = new MeshBuffers();
            // 正面（法线 +Z）：竖直面 z=0，y∈[0,1]。
            buffers.AddQuad(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f), Vector3.forward);
            // 顶面（法线 +Y）：水平面 y=1，向 -Z 延伸；前棱与正面上边重合。
            buffers.AddQuad(new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
                new Vector3(1f, 1f, -1f), new Vector3(0f, 1f, -1f), Vector3.up);
            var (v, t, n) = FromBuffers(buffers);
            Color[] colors = SmoothNormalsBaker.Bake(v, t, n);

            // 共享棱 y=1, z=0, x∈[0,1] 上的顶点：两面各贡献 90° 内角 → (0,1,1)/√2。
            Vector3 expected = new Vector3(0f, 1f, 1f).normalized;
            int checkedCount = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(v[i].y - 1f) < 1e-4f && Mathf.Abs(v[i].z) < 1e-4f
                    && v[i].x >= -1e-4f && v[i].x <= 1f + 1e-4f)
                {
                    Assert.That(Decode(colors[i]), Is.EqualTo(expected).Using(Vector3Within(5e-3f)),
                        $"折棱顶点 {i} 的平滑法线应为两面角平分方向");
                    checkedCount++;
                }
            }

            Assert.That(checkedCount, Is.GreaterThanOrEqualTo(2), "折棱应被两个面共享");
        }

        static System.Collections.Generic.IEqualityComparer<Vector3> Vector3Within(float tolerance)
        {
            return new Vector3Tolerance(tolerance);
        }

        class Vector3Tolerance : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            readonly float _tolerance;

            public Vector3Tolerance(float tolerance) => _tolerance = tolerance;

            public bool Equals(Vector3 x, Vector3 y) => (x - y).magnitude <= _tolerance;

            public int GetHashCode(Vector3 obj) => obj.GetHashCode();
        }
    }
}
