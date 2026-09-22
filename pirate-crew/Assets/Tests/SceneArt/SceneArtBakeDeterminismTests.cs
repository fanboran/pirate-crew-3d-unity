using NUnit.Framework;
using PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 烘焙确定性断言（糖豆人式资产架构的阶段 E 判据）：同输入两次合成，
    /// 缓冲**逐顶点/逐三角一致**（bit 级）。运行时消费的是烘焙 prefab——几何源函数一旦
    /// 漂移，这里先红，资产库与代码才不会静默分叉（CI 可锁资产漂移）。
    ///
    /// 【为什么是 bit 级比较】几何层是纯 C# 确定性代码（无浮点环境差、无并行序，
    /// 剪影扰动走 SceneArtHash 确定性哈希），同输入两次跑同一进程内应逐位一致；
    /// 任何"看起来差不多"的容差都会放过抖动种子泄漏。
    /// </summary>
    public class SceneArtBakeDeterminismTests
    {
        /// <summary>
        /// 现存样板关号。关卡 2「碎岛雨」已删除（2026-09-22），号段有意不连续——
        /// 显式清单而非 <c>FirstLevel..LastLevel</c> 连续区间：区间会在关卡 2 上取到空摆位表
        /// 而误判，显式清单则让"哪一关的烘焙件丢了"直接红在那一关。
        /// </summary>
        static readonly int[] ExistingLevels = { 1, 3 };

        // ------------------------------------------------------------------
        // 断言工具
        // ------------------------------------------------------------------

        static void AssertBuffersIdentical(MeshBuffers a, MeshBuffers b, string label)
        {
            Assert.AreEqual(a.VertexCount, b.VertexCount, label + " 顶点数应一致");
            Assert.AreEqual(a.IndexCount, b.IndexCount, label + " 索引数应一致");

            Vector3[] va = a.ToVertices();
            Vector3[] vb = b.ToVertices();
            for (int i = 0; i < va.Length; i++)
            {
                Assert.AreEqual(va[i].x, vb[i].x, label + " 顶点[" + i + "].x");
                Assert.AreEqual(va[i].y, vb[i].y, label + " 顶点[" + i + "].y");
                Assert.AreEqual(va[i].z, vb[i].z, label + " 顶点[" + i + "].z");
            }

            int[] ta = a.ToTriangles();
            int[] tb = b.ToTriangles();
            for (int i = 0; i < ta.Length; i++)
                Assert.AreEqual(ta[i], tb[i], label + " 索引[" + i + "]");
        }

        // ------------------------------------------------------------------
        // 用例：确定性（同输入两次，逐顶点一致）
        // ------------------------------------------------------------------

        [Test]
        public void DangerBorder_SameParamsTwice_VertexIdentical()
        {
            var a = new ScenePropBuffers();
            var b = new ScenePropBuffers();
            IslandShellGeometry.AddDashedBorder(a.Danger, ShowcaseLevels.WidthTiles, ShowcaseLevels.DepthTiles,
                3.15f, LevelGeometry.WaterSurfaceY + 0.012f, 0.9f, 0.55f, 0.18f);
            IslandShellGeometry.AddDashedBorder(b.Danger, ShowcaseLevels.WidthTiles, ShowcaseLevels.DepthTiles,
                3.15f, LevelGeometry.WaterSurfaceY + 0.012f, 0.9f, 0.55f, 0.18f);

            Assert.Greater(a.Danger.VertexCount, 0, "危险虚线应有几何");
            AssertBuffersIdentical(a.Danger, b.Danger, "DangerBorder");
        }

        [Test]
        public void CloudField_SameSpecTwice_VertexIdentical()
        {
            var a = new Lowpoly.LowpolyBuffers();
            var b = new Lowpoly.LowpolyBuffers();
            Lowpoly.CloudFieldGeometry.Compose(a, Lowpoly.CloudFieldSpec.Default);
            Lowpoly.CloudFieldGeometry.Compose(b, Lowpoly.CloudFieldSpec.Default);

            Assert.Greater(a.CloudWarmWhite.VertexCount + a.CloudPaleGold.VertexCount, 0,
                "云场应有几何");
            AssertBuffersIdentical(a.CloudWarmWhite, b.CloudWarmWhite, "CloudWarmWhite");
            AssertBuffersIdentical(a.CloudPaleGold, b.CloudPaleGold, "CloudPaleGold");
        }

        // ------------------------------------------------------------------
        // 用例：摆位表契约（数据层 ↔ 烘焙件的口径钉死）
        // ------------------------------------------------------------------

        [Test]
        public void PlacementTable_MatchesBakedGeometryOrigin()
        {
            // 云场：实例在场心（格 10, 7.5）。
            var placements1 = ShowcaseLevels.BakedPlacements(1);
            int clouds = 0;
            for (int i = 0; i < placements1.Count; i++)
            {
                if (placements1[i].Piece == ShowcasePieceId.CloudField)
                {
                    clouds++;
                    Assert.AreEqual(
                        new Vector3(LevelGeometry.TileToWorld(10f), 0f, LevelGeometry.TileToWorld(7.5f)),
                        placements1[i].Position, "云场实例应在 20×15 格场心");
                }
            }
            Assert.AreEqual(1, clouds, "云端漫步应恰摆一片云场");

            // 现存关卡共用危险虚线于原点。
            for (int l = 0; l < ExistingLevels.Length; l++)
            {
                int level = ExistingLevels[l];
                var list = ShowcaseLevels.BakedPlacements(level);
                bool hasBorder = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Piece != ShowcasePieceId.DangerBorder)
                        continue;
                    hasBorder = true;
                    Assert.AreEqual(Vector3.zero, list[i].Position, "关 " + level + " 危险虚线应恒在原点");
                }
                Assert.IsTrue(hasBorder, "关 " + level + " 应有危险虚线");
            }
        }
    }
}
