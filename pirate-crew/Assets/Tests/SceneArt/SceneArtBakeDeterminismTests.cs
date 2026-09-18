using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 烘焙确定性断言（糖豆人式资产架构的阶段 E 判据）：同配方 + 同种子两次合成，
    /// 缓冲**逐顶点/逐三角一致**（bit 级）。运行时消费的是烘焙 prefab——几何源函数一旦
    /// 漂移，这里先红，资产库与代码才不会静默分叉（CI 可锁资产漂移）。
    ///
    /// 【为什么是 bit 级比较】几何层是纯 C# 确定性代码（无浮点环境差、无并行序），
    /// 同输入两次跑同一进程内应逐位一致；任何"看起来差不多"的容差都会放过抖动种子泄漏。
    /// </summary>
    public class SceneArtBakeDeterminismTests
    {
        const int ComposeSeed = 20;   // 与 SceneArtBaker/样板第 2 关同源种子

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

        static ScenePropBuffers ComposeShip(in ShipRecipe recipe)
        {
            var buffers = new ScenePropBuffers();
            var layout = new SceneKitLayout();
            System.Collections.Generic.List<KitPart> parts = SceneKitCatalog.BuildCompleteShip(
                recipe, Vector3.zero, 0f, ComposeSeed,
                recipe.HullLength, recipe.BowLength, recipe.SternLength, recipe.HullBeam,
                mastAlongOffsets: null);
            for (int i = 0; i < parts.Count; i++)
                layout.Add(parts[i]);
            SceneKitComposer.Compose(buffers, layout, ComposeSeed);
            return buffers;
        }

        static void AssertShipDeterministic(in ShipRecipe recipe, string label)
        {
            ScenePropBuffers first = ComposeShip(recipe);
            ScenePropBuffers second = ComposeShip(recipe);

            AssertBuffersIdentical(first.Wood, second.Wood, label + "/Wood");
            AssertBuffersIdentical(first.WoodDark, second.WoodDark, label + "/WoodDark");
            AssertBuffersIdentical(first.Metal, second.Metal, label + "/Metal");
            AssertBuffersIdentical(first.Cloth, second.Cloth, label + "/Cloth");

            // 非空自检：防止"两次都是空缓冲"的平凡通过——大帆船的放样船体/帆/炮是固定产出。
            Assert.Greater(first.Wood.VertexCount, 0, label + " 应有木料几何（非平凡比较）");
        }

        // ------------------------------------------------------------------
        // 用例
        // ------------------------------------------------------------------

        [Test]
        public void Galleon_SameRecipeTwice_VertexIdentical()
        {
            AssertShipDeterministic(SceneKitCatalog.LargeShipRecipe, "Ship_Galleon");
        }

        [Test]
        public void Longboat_SameRecipeTwice_VertexIdentical()
        {
            AssertShipDeterministic(SceneKitCatalog.SmallBoatRecipe, "Ship_Longboat");
        }

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
        public void Galleon_PlacementTable_MatchesBakedOrigin()
        {
            // 摆位表（数据层）与烘焙原点的契约：实例位置/yaw 变化不需要重烘；
            // 本断言钉住"样板第 2 关两艘都是 Galleon + 甲板面 y3"的口径不被无声改动。
            var placements = ShowcaseLevels.BakedPlacements(2);
            int galleons = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].Piece == ShowcasePieceId.Galleon)
                {
                    galleons++;
                    Assert.AreEqual(LevelGeometry.GroundTopY + LevelGeometry.BlockWorldHeight * 6f,
                        placements[i].Position.y, 1e-4f, "甲板面应固定在 y3（Blocks(3)=6 块）");
                }
            }
            Assert.AreEqual(2, galleons, "样板第 2 关应摆两艘大帆船（双雄并舷）");
        }
    }
}
