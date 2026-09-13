using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 模块化构件管线（kit）的纯 C# 用例（无头可跑）。
    ///
    /// 【覆盖】
    ///   · 两艘不同尺寸的船配方都能展开出 艏/舯/艉 船体段 + 甲板 + 桅 + 索具 + 帆；
    ///   · level_1 的簇 → 配方映射（大船主簇 + 小空岛小艇 + 空岛/梯田岩台）；
    ///   · 确定性重建（同 seed 同摆放）；
    ///   · 构件预算（三角面 / 材质组数）在场景文档 §8 之内；
    ///   · 构件几何不抬高地表（所有构件 y ≥ 基础地面）。
    /// </summary>
    [TestFixture]
    public class SceneKitTests
    {
        [Test]
        public void TwoShipRecipes_DifferInSize()
        {
            ShipRecipe large = SceneKitCatalog.LargeShipRecipe;
            ShipRecipe small = SceneKitCatalog.SmallBoatRecipe;

            Assert.Greater(large.HullLength, small.HullLength, "大船应比小艇长");
            Assert.Greater(large.HullBeam, small.HullBeam, "大船应比小艇宽");
            Assert.Greater(large.MastHeight, small.MastHeight, "大船桅更高");
            Assert.Greater(large.MastCount, small.MastCount, "大船桅更多");
            Assert.Greater(large.CannonCount, small.CannonCount, "大船炮更多");
        }

        [Test]
        public void BuildShip_HasBowMidStern_Deck_Mast_Rigging_Sail()
        {
            List<KitPart> ship = SceneKitCatalog.BuildShip(
                SceneKitCatalog.LargeShipRecipe, Vector3.zero, 0f, 7);

            int bows = 0, mids = 0, sterns = 0, decks = 0, masts = 0, rigs = 0, sails = 0, hulls = 0;
            for (int i = 0; i < ship.Count; i++)
            {
                switch (ship[i].Piece)
                {
                    case SceneKitPiece.HullBow: bows++; break;
                    case SceneKitPiece.HullMid: mids++; break;
                    case SceneKitPiece.HullStern: sterns++; break;
                    case SceneKitPiece.DeckPlank: decks++; break;
                    case SceneKitPiece.Mast: masts++; break;
                    case SceneKitPiece.Rigging: rigs++; break;
                    case SceneKitPiece.Sail: sails++; break;
                    case SceneKitPiece.Bulwark: hulls++; break;
                }
            }

            Assert.AreEqual(1, bows, "艏构件 1 段");
            Assert.GreaterOrEqual(mids, 1, "舯构件至少 1 段");
            Assert.AreEqual(1, sterns, "艉构件 1 段");
            Assert.GreaterOrEqual(decks, 3, "甲板铺板应有多条");
            Assert.AreEqual(2, hulls, "两舷舷墙");
            Assert.AreEqual(SceneKitCatalog.LargeShipRecipe.MastCount, masts, "桅数 = 配方");
            Assert.GreaterOrEqual(rigs, 4, "每桅至少 4 根索具");
            Assert.GreaterOrEqual(sails, 1, "带帆配方应至少 1 面帆");
        }

        [Test]
        public void BuildLevel1_Deterministic()
        {
            SceneKitLayout a = SceneKitCatalog.BuildLevel1(1234);
            SceneKitLayout b = SceneKitCatalog.BuildLevel1(1234);

            Assert.AreEqual(a.Parts.Count, b.Parts.Count, "同 seed 应得同一条数");
            Assert.Greater(a.Parts.Count, 0);

            for (int i = 0; i < a.Parts.Count; i++)
            {
                Assert.AreEqual(a.Parts[i].Piece, b.Parts[i].Piece);
                Assert.AreEqual(a.Parts[i].Material, b.Parts[i].Material);
                Assert.AreEqual(a.Parts[i].Position.x, b.Parts[i].Position.x, 1e-6f);
                Assert.AreEqual(a.Parts[i].Position.y, b.Parts[i].Position.y, 1e-6f);
                Assert.AreEqual(a.Parts[i].Position.z, b.Parts[i].Position.z, 1e-6f);
            }
        }

        [Test]
        public void BuildLevel1_UsesLargeShipAndSmallBoat()
        {
            SceneKitLayout kit = SceneKitCatalog.BuildLevel1(7);

            // 大船主簇 2 桅 + 小空岛小艇 1 桅 = 3 桅；两艘船各 1 个艏构件。
            Assert.AreEqual(3, kit.CountOf(SceneKitPiece.Mast),
                "大船(2桅) + 小艇(1桅) = 3 桅，说明两套配方都被用了");
            Assert.AreEqual(2, kit.CountOf(SceneKitPiece.HullBow), "两艘船各 1 个艏构件");
            Assert.GreaterOrEqual(kit.CountOf(SceneKitPiece.IslandTop), 1, "空岛/梯田应有岛顶岩台");
            Assert.GreaterOrEqual(kit.CountOf(SceneKitPiece.RockChunk), 6, "岛缘应有散落岩块");
            Assert.GreaterOrEqual(kit.CountOf(SceneKitPiece.Prop), 1, "应有陈设构件");
        }

        [Test]
        public void KitComposition_TotalTriangles_UnderSceneBudget()
        {
            var buffers = new ScenePropBuffers();
            SceneKitComposer.Compose(buffers, SceneKitCatalog.BuildLevel1(7), 7);

            TestContext.Progress.WriteLine("[kit 三角面] 合计 " + buffers.TotalTriangles
                + "（木 " + buffers.Wood.TriangleCount + " / 暗木 " + buffers.WoodDark.TriangleCount
                + " / 岩 " + buffers.Rock.TriangleCount + " / 铁 " + buffers.Metal.TriangleCount
                + " / 布 " + buffers.Cloth.TriangleCount + " / 植被 " + buffers.Foliage.TriangleCount + "）");

            Assert.Greater(buffers.TotalTriangles, 0);
            Assert.Less(buffers.TotalTriangles, 60000, "kit 可见三角面应远低于场景 §8 的 150k 预算");
        }

        [Test]
        public void KitComposition_IsDeterministic()
        {
            var a = new ScenePropBuffers();
            var b = new ScenePropBuffers();
            SceneKitComposer.Compose(a, SceneKitCatalog.BuildLevel1(7), 7);
            SceneKitComposer.Compose(b, SceneKitCatalog.BuildLevel1(7), 7);

            Assert.AreEqual(a.TotalTriangles, b.TotalTriangles, "同 seed 应得同三角面数");
            Assert.AreEqual(a.Wood.VertexCount, b.Wood.VertexCount);
            Assert.AreEqual(a.Rock.VertexCount, b.Rock.VertexCount);
            Assert.AreEqual(a.WoodDark.VertexCount, b.WoodDark.VertexCount);
        }

        [Test]
        public void KitMaterialGroups_AreBounded()
        {
            SceneKitLayout kit = SceneKitCatalog.BuildLevel1(7);
            var used = new HashSet<SceneKitMaterial>();
            for (int i = 0; i < kit.Parts.Count; i++)
                used.Add(kit.Parts[i].Material);

            Assert.LessOrEqual(used.Count, 6, "构件材质组不得超过注册表定义的 6 组");
            Assert.GreaterOrEqual(used.Count, 3, "至少用到木/岩/植被三类");
        }

        [Test]
        public void KitParts_NeverSinkBelowBaseGround()
        {
            SceneKitLayout kit = SceneKitCatalog.BuildLevel1(7);

            for (int i = 0; i < kit.Parts.Count; i++)
            {
                KitPart p = kit.Parts[i];
                Assert.GreaterOrEqual(p.Position.y, LevelGeometry.GroundTopY - 1e-4f,
                    p.Piece + " 的摆放点不得低于基础地面");
                // 桅/帆/索具会高于平台顶面（那是正常的高物），但仍应在合理高度内。
                Assert.LessOrEqual(p.Position.y, LevelGeometry.GroundTopY + 8f,
                    p.Piece + " 的摆放点高度异常");
            }
        }

        [Test]
        public void ShipGeometry_RespectsSceneDocTriangleBudget()
        {
            var buffers = new ScenePropBuffers();
            var ship = SceneKitCatalog.BuildShip(SceneKitCatalog.LargeShipRecipe, Vector3.zero, 0f, 3);
            var layout = new SceneKitLayout();
            // 用最小调度壳把单船构件展开。
            for (int i = 0; i < ship.Count; i++)
                layout.Add(ship[i]);
            SceneKitComposer.Compose(buffers, layout, 3);

            int shipTris = buffers.Wood.TriangleCount + buffers.WoodDark.TriangleCount
                + buffers.Cloth.TriangleCount + buffers.Metal.TriangleCount;

            TestContext.Progress.WriteLine("[大船 kit 三角面] " + shipTris);
            Assert.Greater(shipTris, 300, "船体细节过少会读成盒子");
            Assert.Less(shipTris, 8000, "单船三角面应受控（§4.1 预算 3000-5000，含构件叠加放宽到 8000）");
        }
    }
}
