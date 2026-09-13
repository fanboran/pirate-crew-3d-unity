using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient.Tests
{
    /// <summary>
    /// 程序化网格工厂的纯 C# 用例：每个网格非空、三角面在预算内、包围盒符合设计尺寸。
    ///
    /// 【为什么测包围盒】低模几何最容易犯的错是"某个零件被写到了 10 米外"或"整体缩放错了一个量级"
    /// ——画面上的表现是"活物小到看不见"或"一只鸟占满屏幕"，肉眼定位很慢。
    /// 断言包围盒把尺度钉死（尺度口径见 <see cref="AmbientMeshFactory"/> 类头注释：1 单位 ≈ 3.4 m）。
    /// </summary>
    [TestFixture]
    public class AmbientMeshFactoryTests
    {
        static void AssertNonEmpty(MeshBuffers buffers, string name)
        {
            Assert.IsNotNull(buffers, name + " 为 null");
            Assert.IsFalse(buffers.IsEmpty, name + " 没有三角面");
            Assert.Greater(buffers.TriangleCount, 0, name + " 三角面为 0");
        }

        static Bounds BoundsOf(MeshBuffers buffers)
        {
            Vector3[] vertices = buffers.ToVertices();
            Assert.Greater(vertices.Length, 0);
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
                bounds.Encapsulate(vertices[i]);

            return bounds;
        }

        [Test]
        public void GullBody_HasPartsAndFitsBodyLength()
        {
            MeshBuffers body = AmbientMeshFactory.BuildGullBody();
            AssertNonEmpty(body, "海鸥躯干");
            Assert.LessOrEqual(body.TriangleCount, AmbientBudget.MaxGullTriangles);

            Bounds bounds = BoundsOf(body);
            Assert.Less(bounds.size.z, 0.6f, "躯干（含尾）长度不应超过 0.6 单位");
            Assert.Less(bounds.size.x, 0.2f, "躯干宽度应明显小于翼展");
        }

        [Test]
        public void GullWing_SpansOneSideAndIsSingleSidedMesh()
        {
            MeshBuffers wing = AmbientMeshFactory.BuildGullWing();
            AssertNonEmpty(wing, "海鸥单翼");
            Assert.LessOrEqual(wing.TriangleCount, 16);

            Bounds bounds = BoundsOf(wing);
            Assert.Greater(bounds.max.x, AmbientMeshFactory.GullWingSpan * 0.9f, "翼尖应达到设计展长");
            Assert.GreaterOrEqual(bounds.min.x, -0.01f, "单翼只向 +X 展开（右翼靠镜像）");
        }

        [Test]
        public void CrabBody_IsWiderThanDeepAndLowProfile()
        {
            MeshBuffers crab = AmbientMeshFactory.BuildCrabBody();
            AssertNonEmpty(crab, "螃蟹甲壳");
            Assert.LessOrEqual(crab.TriangleCount, AmbientBudget.MaxCrabTriangles);

            Bounds bounds = BoundsOf(crab);
            Assert.Greater(bounds.size.x, 0.3f, "甲宽应达到设计值");
            Assert.Less(bounds.size.y, 0.2f, "螃蟹应是低矮轮廓");
        }

        [Test]
        public void CrabClaw_ExtendsForwardAndIsLowPoly()
        {
            MeshBuffers claw = AmbientMeshFactory.BuildCrabClaw();
            AssertNonEmpty(claw, "螃蟹钳子");
            Assert.LessOrEqual(claw.TriangleCount, 60, "单只钳子必须保持低模");
            Assert.Greater(BoundsOf(claw).max.x, 0.1f, "钳子应向 +X 伸出");
        }

        [Test]
        public void Fish_FitsDesignLength()
        {
            MeshBuffers fish = AmbientMeshFactory.BuildFish();
            AssertNonEmpty(fish, "鱼");
            Assert.LessOrEqual(fish.TriangleCount, AmbientBudget.MaxFishTriangles);

            Bounds bounds = BoundsOf(fish);
            Assert.Less(bounds.size.z, AmbientMeshFactory.FishLength * 1.6f, "鱼长应接近设计值");
            Assert.Less(bounds.size.y, 0.12f, "鱼应是细长轮廓");
        }

        [Test]
        public void LanternFrame_AndCore_FitLanternHeight()
        {
            MeshBuffers frame = AmbientMeshFactory.BuildLanternFrame();
            AssertNonEmpty(frame, "灯笼框架");
            Assert.LessOrEqual(frame.TriangleCount, AmbientBudget.MaxLanternTriangles);

            Bounds frameBounds = BoundsOf(frame);
            Assert.That(frameBounds.max.y, Is.InRange(AmbientMeshFactory.LanternHeight,
                AmbientMeshFactory.LanternHeight * 1.4f), "提梁到灯笼顶");

            MeshBuffers core = AmbientMeshFactory.BuildLanternCore();
            AssertNonEmpty(core, "灯笼灯芯");
            Bounds coreBounds = BoundsOf(core);
            Assert.Less(coreBounds.size.x, 0.15f);
            Assert.Less(coreBounds.size.y, 0.15f);
        }

        [Test]
        public void Pennant_HangsDownwardFromOrigin()
        {
            MeshBuffers pennant = AmbientMeshFactory.BuildPennant();
            AssertNonEmpty(pennant, "燕尾旗");

            Bounds bounds = BoundsOf(pennant);
            Assert.AreEqual(0f, bounds.max.y, 1e-4f, "上缘（挂点）必须在 y=0");
            Assert.Less(bounds.min.y, -0.1f, "旗面应向下垂");
            Assert.Greater(bounds.min.y, -0.3f, "垂长不应过大");
        }

        [Test]
        public void Post_HasDesignHeight()
        {
            MeshBuffers post = AmbientMeshFactory.BuildPost(AmbientMeshFactory.PostHeight, 0.05f);
            AssertNonEmpty(post, "立柱");

            Bounds bounds = BoundsOf(post);
            Assert.That(bounds.min.y, Is.InRange(-0.01f, 0.01f), "柱底在 y=0");
            Assert.Greater(bounds.max.y, AmbientMeshFactory.PostHeight, "柱顶应含顶帽，高于柱身");
        }

        [Test]
        public void GlowQuad_IsCenteredAndMatchesRequestedSize()
        {
            MeshBuffers quad = AmbientMeshFactory.BuildGlowQuad(2f);
            AssertNonEmpty(quad, "辉光片");

            Bounds bounds = BoundsOf(quad);
            Assert.AreEqual(0f, bounds.center.x, 1e-4f);
            Assert.AreEqual(0f, bounds.center.y, 1e-4f);
            Assert.AreEqual(2f, bounds.size.x, 1e-4f);
            Assert.AreEqual(2f, bounds.size.y, 1e-4f);
            Assert.AreEqual(0f, bounds.size.z, 1e-4f, "辉光片是零厚度贴片");
        }

        [Test]
        public void DistantBird_IsVeryLowPoly()
        {
            MeshBuffers bird = AmbientMeshFactory.BuildDistantBird();
            AssertNonEmpty(bird, "远景海鸟剪影");
            Assert.LessOrEqual(bird.TriangleCount, 12, "远景剪影必须极低成本");
            Assert.Greater(BoundsOf(bird).size.x, 0.5f, "翼展应可辨");
        }

        [Test]
        public void CorkFloat_IsSmallAndRounded()
        {
            MeshBuffers cork = AmbientMeshFactory.BuildCorkFloat();
            AssertNonEmpty(cork, "浮标");
            Bounds bounds = BoundsOf(cork);
            Assert.Less(bounds.size.x, 0.2f);
            Assert.Less(bounds.size.y, 0.3f);
        }

        [Test]
        public void AllMeshes_TotalTrianglesWithinAmbientBudget()
        {
            int total = 0;
            total += AmbientMeshFactory.BuildGullBody().TriangleCount;
            total += AmbientMeshFactory.BuildGullWing().TriangleCount * 2;   // 左右翼
            total += AmbientMeshFactory.BuildCrabBody().TriangleCount;
            total += AmbientMeshFactory.BuildCrabClaw().TriangleCount * 2;
            total += AmbientMeshFactory.BuildFish().TriangleCount * AmbientBudget.FishPerSchool;
            total += AmbientMeshFactory.BuildLanternFrame().TriangleCount * AmbientBudget.MaxLanterns;
            total += AmbientMeshFactory.BuildLanternCore().TriangleCount * AmbientBudget.MaxLanterns;
            total += AmbientMeshFactory.BuildGlowQuad(1f).TriangleCount * AmbientBudget.MaxLanterns;
            total += AmbientMeshFactory.BuildPennant().TriangleCount * 6;
            total += AmbientMeshFactory.BuildPost(AmbientMeshFactory.PostHeight, 0.05f).TriangleCount * 4;
            total += AmbientMeshFactory.BuildCorkFloat().TriangleCount * 3;
            total += AmbientMeshFactory.BuildDistantBird().TriangleCount * 3;

            Assert.LessOrEqual(total, AmbientBudget.MaxAmbientTriangles,
                "活物 + 动效道具的三角面合计必须留在预算内（实际 " + total + "）");
        }

        [Test]
        public void Meshes_UseIndependentVerticesForFlatShading()
        {
            // MeshBuffers 的约定：每面独立 3 顶点（平面着色），顶点数 = 3 × 三角面数。
            MeshBuffers fish = AmbientMeshFactory.BuildFish();
            Assert.AreEqual(fish.TriangleCount * 3, fish.VertexCount,
                "三角形与顶点数必须满足平面着色约定（否则低模会变圆滑）");
        }

        [Test]
        public void AppendRotated_WithMirrorScale_StillProducesTriangles()
        {
            // 右翼靠 localScale.x = -1 镜像，不额外生成网格；这里验证镜像不会让几何退化。
            // 用纯托管的 ComposeTrs（Quaternion.Euler / Matrix4x4.TRS 会触发 ECall，无头跑不了）。
            MeshBuffers wing = AmbientMeshFactory.BuildGullWing();
            var mirrored = new MeshBuffers();
            Matrix4x4 mirror = AmbientMeshFactory.ComposeTrs(
                Vector3.zero, Vector3.zero, new Vector3(-1f, 1f, 1f));
            mirrored.AppendTransformed(wing, mirror);

            Assert.AreEqual(wing.TriangleCount, mirrored.TriangleCount, "镜像不应丢面");
            Assert.Less(BoundsOf(mirrored).max.x, 0.01f, "镜像后应落在 -X 侧");
        }

        [Test]
        public void ComposeTrs_IsPureManagedAndMatchesKnownRotations()
        {
            // 纯 C# 自实现的 TRS：绕 X 轴 +90° 应把 +Y 映射到 +Z。
            Matrix4x4 rot = AmbientMeshFactory.ComposeTrs(Vector3.zero, new Vector3(90f, 0f, 0f), Vector3.one);
            Vector3 mapped = rot.MultiplyPoint3x4(Vector3.up);

            Assert.AreEqual(0f, mapped.x, 1e-4f);
            Assert.AreEqual(0f, mapped.y, 1e-4f);
            Assert.AreEqual(1f, mapped.z, 1e-4f, "绕 X +90° 应把 +Y 转到 +Z（与 Quaternion.Euler 同约定）");

            // 平移与缩放。
            Matrix4x4 trs = AmbientMeshFactory.ComposeTrs(
                new Vector3(1f, 2f, 3f), Vector3.zero, new Vector3(2f, 2f, 2f));
            Assert.AreEqual(new Vector3(3f, 2f, 3f), trs.MultiplyPoint3x4(Vector3.right));
        }

        [Test]
        public void MeshBuffers_AreReusableFromSceneArtModule()
        {
            // 记录一条只读依赖：Ambient 复用 SceneArt 的纯 C# MeshBuffers（不改其文件）。
            var buffers = new MeshBuffers();
            buffers.AddTriangle(Vector3.zero, Vector3.right, Vector3.up);
            Assert.AreEqual(1, buffers.TriangleCount);

            var list = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();
            buffers.CopyTo(list, normals, indices);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(3, indices.Count);
        }
    }
}
