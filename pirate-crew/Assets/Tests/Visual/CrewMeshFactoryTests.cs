using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.PirateCrew.Visual.Tests
{
    /// <summary>
    /// <see cref="CrewMeshFactory"/> / <see cref="CrewMeshLibrary"/> 的纯 C# 断言：
    /// 顶点数、包围盒、法线单位化、无退化三角、绕向朝外、UV 范围、逐职业三角面预算。
    ///
    /// 【为什么可无头】全部走 <see cref="MeshData"/> 数据，不实例化 <see cref="Mesh"/> / GameObject
    /// （后者在本环境会抛 SecurityException，见 AGENTS.md 无头验证台边界）。
    /// </summary>
    [TestFixture]
    public class CrewMeshFactoryTests
    {
        const float Epsilon = 1e-3f;

        // ------------------------------------------------------------------
        // 通用健全性
        // ------------------------------------------------------------------

        [Test]
        public void LowPolySphere_CountsAndBounds()
        {
            MeshData m = CrewMeshFactory.LowPolySphere(0.0775f, 10, 6);
            Assert.AreEqual(77, m.VertexCount, "顶点数 = (segments+1)×(rings+1) = 11×7");
            Assert.AreEqual(100, m.TriangleCount, "三角面 = 2×segments×(rings−1) = 2×10×5");
            AssertWellFormed(m, "低模球");

            float maxRadius = 0f;
            for (int i = 0; i < m.VertexCount; i++)
                maxRadius = Mathf.Max(maxRadius, m.Vertices[i].magnitude);
            Assert.That(maxRadius, Is.EqualTo(0.0775f).Within(Epsilon), "球面半径 = 输入半径");
        }

        [Test]
        public void Frustum_IsTapered_TopNarrowerThanBottom()
        {
            MeshData m = CrewMeshFactory.Frustum(0.052f, 0.095f, 0.200f, 10);
            Assert.AreEqual(40, m.TriangleCount, "侧面 20 + 上下盖 20");
            AssertWellFormed(m, "躯干圆台");

            float bottomMax = 0f;
            float topMax = 0f;
            for (int i = 0; i < m.VertexCount; i++)
            {
                Vector3 v = m.Vertices[i];
                float r = new Vector2(v.x, v.z).magnitude;
                if (v.y < 0f)
                    bottomMax = Mathf.Max(bottomMax, r);
                else if (v.y > 0f)
                    topMax = Mathf.Max(topMax, r);
            }

            Assert.That(bottomMax, Is.EqualTo(0.095f).Within(Epsilon), "下半径 0.095");
            Assert.That(topMax, Is.EqualTo(0.052f).Within(Epsilon), "上半径 0.052");
            Assert.Less(topMax, bottomMax, "必须上窄下宽（Art Bible §5.1 禁止上下等径）");
        }

        [Test]
        public void Cylinder_HasEqualRadii()
        {
            MeshData m = CrewMeshFactory.Cylinder(0.022f, 0.090f, 10);
            Assert.AreEqual(40, m.TriangleCount);
            AssertWellFormed(m, "圆柱");

            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < m.VertexCount; i++)
            {
                minY = Mathf.Min(minY, m.Vertices[i].y);
                maxY = Mathf.Max(maxY, m.Vertices[i].y);
            }
            Assert.That(maxY - minY, Is.EqualTo(0.090f).Within(Epsilon), "高度沿 Y 居中");
            Assert.That(minY, Is.EqualTo(-0.045f).Within(Epsilon));
        }

        [Test]
        public void Capsule_TotalHeightIsCylinderPlusTwoRadii()
        {
            MeshData m = CrewMeshFactory.Capsule(0.5f, 1f, 8, 3);
            Assert.AreEqual(96, m.TriangleCount, "7 波段 × 8 段 × 2 − 两极各省一段");
            AssertWellFormed(m, "胶囊");

            float minY = float.MaxValue;
            float maxY = float.MinValue;
            float maxRadius = 0f;
            for (int i = 0; i < m.VertexCount; i++)
            {
                Vector3 v = m.Vertices[i];
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
                maxRadius = Mathf.Max(maxRadius, new Vector2(v.x, v.z).magnitude);
            }
            Assert.That(maxY - minY, Is.EqualTo(2f).Within(Epsilon), "总高 = 圆柱段 1 + 2×半径 0.5");
            Assert.That(maxRadius, Is.EqualTo(0.5f).Within(Epsilon));
        }

        [Test]
        public void Box_MatchesSizeAndHasTwelveTriangles()
        {
            var size = new Vector3(0.1f, 0.2f, 0.3f);
            MeshData m = CrewMeshFactory.Box(size);
            Assert.AreEqual(24, m.VertexCount, "硬边 6 面 × 4 顶点");
            Assert.AreEqual(12, m.TriangleCount);
            AssertWellFormed(m, "盒");

            var extents = new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
            for (int i = 0; i < m.VertexCount; i++)
            {
                Assert.That(Mathf.Abs(m.Vertices[i].x), Is.EqualTo(extents.x).Within(Epsilon));
                Assert.That(Mathf.Abs(m.Vertices[i].y), Is.EqualTo(extents.y).Within(Epsilon));
                Assert.That(Mathf.Abs(m.Vertices[i].z), Is.EqualTo(extents.z).Within(Epsilon));
            }
        }

        [Test]
        public void ArcRib_HookTriangleCountMatchesSpec()
        {
            // 规格 §3.5：钩的弯管约 96 tri；这里 270°/12 段/6 边 = 144（更圆滑，仍在预算内）。
            MeshData m = CrewMeshFactory.ArcRib(0.0375f, 0.012f, 270f, 12, 6);
            Assert.AreEqual(144, m.TriangleCount, "segments × tubeSides × 2 = 12×6×2");
            AssertWellFormed(m, "铁钩弯管");

            MeshData rib = CrewMeshFactory.ArcRib(0.055f, 0.008f, 180f, 8, 6);
            Assert.AreEqual(96, rib.TriangleCount, "肋骨半环 8×6×2");
            AssertWellFormed(rib, "肋骨半环");
        }

        [Test]
        public void Tricorn_IsClosedSolid()
        {
            MeshData m = CrewMeshFactory.Tricorn(0.065f, 0.048f, 0.070f, 28f, 0.012f, 10);
            Assert.AreEqual(66, m.TriangleCount, "帽冠 20+10 + 三片翻檐 3×12");
            AssertWellFormed(m, "三角帽");

            // 三片 120° 均布的翻檐应把 XZ 范围撑到约 ±brimRadius×1.5。
            float maxXZ = 0f;
            for (int i = 0; i < m.VertexCount; i++)
                maxXZ = Mathf.Max(maxXZ, new Vector2(m.Vertices[i].x, m.Vertices[i].z).magnitude);
            Assert.Greater(maxXZ, 0.065f, "帽檐应超出帽冠半径");
        }

        [Test]
        public void BlobCluster_MergesSpheresIntoSingleMesh()
        {
            var blobs = new List<CrewMeshFactory.Blob>
            {
                new CrewMeshFactory.Blob(Vector3.zero, new Vector3(0.05f, 0.03f, 0.04f)),
                new CrewMeshFactory.Blob(new Vector3(0.03f, 0f, 0f), new Vector3(0.02f, 0.02f, 0.02f)),
            };
            MeshData m = CrewMeshFactory.BlobCluster(blobs, 8, 5);
            Assert.AreEqual(2 * 64, m.TriangleCount, "2 个球 × 2×8×4");
            AssertWellFormed(m, "球簇");
        }

        // ------------------------------------------------------------------
        // 全部零件：法线 / 退化 / 绕向 / UV
        // ------------------------------------------------------------------

        [Test]
        public void LibraryMeshes_AllWellFormed()
        {
            Dictionary<string, MeshData> meshes = CrewMeshLibrary.BuildAll();
            Assert.AreEqual(CrewMeshLibrary.Keys.Length, meshes.Count, "零件表与键列表一致");

            foreach (KeyValuePair<string, MeshData> pair in meshes)
            {
                Assert.False(pair.Value.IsEmpty, pair.Key + " 不应为空网格");
                AssertWellFormed(pair.Value, pair.Key);
            }
        }

        // ------------------------------------------------------------------
        // 三角面预算（docs/角色造型规范.md §1.4 / R-3）
        // ------------------------------------------------------------------

        [Test]
        public void EachProfession_IsWithinTriangleBudget()
        {
            for (int i = 0; i < CrewVisualCatalog.AllProfessions.Length; i++)
            {
                CrewProfession profession = CrewVisualCatalog.AllProfessions[i];
                int triangles = CrewMeshLibrary.TotalTriangles(profession);
                Assert.Greater(triangles, 0, profession + " 应有零件");
                Assert.LessOrEqual(triangles, CrewMeshFactory.MaxTrianglesPerUnit,
                    profession + " 三角面 " + triangles + " 超过预算 " + CrewMeshFactory.MaxTrianglesPerUnit);
            }
        }

        [Test]
        public void EachProfession_PlanUsesSharedPartsOnly()
        {
            Dictionary<string, MeshData> meshes = CrewMeshLibrary.BuildAll();
            for (int i = 0; i < CrewVisualCatalog.AllProfessions.Length; i++)
            {
                Dictionary<string, int> counts = CrewMeshLibrary.CountPartInstances(CrewVisualCatalog.AllProfessions[i]);
                Assert.Greater(counts.Count, 0);
                foreach (KeyValuePair<string, int> pair in counts)
                {
                    Assert.IsTrue(meshes.ContainsKey(pair.Key), "零件 " + pair.Key + " 必须在零件表内");
                    Assert.Greater(pair.Value, 0);
                }
            }
        }

        // ------------------------------------------------------------------
        // 断言工具
        // ------------------------------------------------------------------

        static void AssertWellFormed(MeshData m, string label)
        {
            Assert.Greater(m.VertexCount, 0, label + ": 顶点数");
            Assert.AreEqual(m.VertexCount, m.Normals.Length, label + ": 法线数与顶点数一致");
            Assert.AreEqual(m.VertexCount, m.Uvs.Length, label + ": UV 数与顶点数一致");
            Assert.AreEqual(0, m.Triangles.Length % 3, label + ": 索引数是 3 的倍数");

            for (int i = 0; i < m.Triangles.Length; i++)
            {
                Assert.That(m.Triangles[i], Is.InRange(0, m.VertexCount - 1), label + ": 索引越界");
            }

            for (int i = 0; i < m.VertexCount; i++)
            {
                Vector3 n = m.Normals[i];
                Assert.That(n.magnitude, Is.EqualTo(1f).Within(Epsilon),
                    label + ": 法线未单位化 @" + i + " = " + n);
                Assert.IsFalse(float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z),
                    label + ": 法线含 NaN @" + i);

                Vector2 uv = m.Uvs[i];
                Assert.That(uv.x, Is.InRange(-Epsilon, 1f + Epsilon), label + ": UV.x 越界 @" + i);
                Assert.That(uv.y, Is.InRange(-Epsilon, 1f + Epsilon), label + ": UV.y 越界 @" + i);
            }

            for (int t = 0; t < m.Triangles.Length; t += 3)
            {
                int i0 = m.Triangles[t];
                int i1 = m.Triangles[t + 1];
                int i2 = m.Triangles[t + 2];
                Assert.IsTrue(i0 != i1 && i1 != i2 && i0 != i2, label + ": 退化三角（索引重复）@" + t);

                Vector3 a = m.Vertices[i0];
                Vector3 b = m.Vertices[i1];
                Vector3 c = m.Vertices[i2];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                Assert.Greater(cross.magnitude, 1e-9f, label + ": 退化三角（零面积）@" + t);

                // 绕向朝外：几何法线与三个顶点法线平均方向同向。
                Vector3 avgNormal = (m.Normals[i0] + m.Normals[i1] + m.Normals[i2]) / 3f;
                if (avgNormal.sqrMagnitude > 1e-9f)
                {
                    float dot = Vector3.Dot(cross.normalized, avgNormal.normalized);
                    Assert.Greater(dot, 0f, label + ": 三角面绕向朝内 @" + t + "（dot=" + dot + "）");
                }
            }
        }
    }
}
