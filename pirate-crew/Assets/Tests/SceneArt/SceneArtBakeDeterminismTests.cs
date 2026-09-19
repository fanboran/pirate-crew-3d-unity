using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
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

        [Test]
        public void Islets_SameGridTwice_VertexIdentical()
        {
            // 碎岛壳直接从 L2 逻辑高度场烘出（SceneArtBaker.BakeIslets 同源路径）：
            // 两次独立建场 + 建壳应逐顶点一致——钉住"改布局必须连带重烘"的资产契约。
            MeshBuffers a = IslandShellGeometry.BuildSolidShell(
                ShowcaseLevels.BuildLogicGrid(2), IslandShellSettings.Default);
            MeshBuffers b = IslandShellGeometry.BuildSolidShell(
                ShowcaseLevels.BuildLogicGrid(2), IslandShellSettings.Default);

            Assert.Greater(a.VertexCount, 0, "碎岛礁群应有几何");
            AssertBuffersIdentical(a, b, "Islets_L02");
        }

        // ------------------------------------------------------------------
        // 用例：摆位表契约（数据层 ↔ 烘焙件的口径钉死）
        // ------------------------------------------------------------------

        [Test]
        public void PlacementTable_MatchesBakedGeometryOrigin()
        {
            // 碎岛：几何世界坐标直出（与逻辑高度场按构造对齐），实例必须恒在原点、不旋转。
            var placements2 = ShowcaseLevels.BakedPlacements(2);
            int islets = 0;
            for (int i = 0; i < placements2.Count; i++)
            {
                if (placements2[i].Piece == ShowcasePieceId.Islets)
                {
                    islets++;
                    Assert.AreEqual(Vector3.zero, placements2[i].Position, "碎岛实例应恒在原点（几何即布局）");
                    Assert.AreEqual(0f, placements2[i].YawDegrees, "碎岛实例不应旋转");
                }
            }
            Assert.AreEqual(1, islets, "碎岛雨应恰摆一组碎岛礁群");

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

            // 三关共用危险虚线于原点。
            for (int level = ShowcaseLevels.FirstLevel; level <= ShowcaseLevels.LastLevel; level++)
            {
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
