using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 道具几何与**性能预算**的纯 C# 用例（无头可跑）。
    ///
    /// 【覆盖】场景文档 §8 的性能预算里可以在无头环境量到的部分：
    ///   · 场景总三角面 ≤ 150k（本用例把"地形壳 + 道具"合并计算）；
    ///   · 材质组数（= 构建期静态合并后的 DrawCall 数）在预算内；
    ///   · 远景剪影的基座高度落在"水远缘视线"之下（否则岛会浮在天上）。
    /// 帧率/实际 DrawCall 数必须靠编辑器 Frame Debugger / Profiler 实测，不在本文件范围内。
    /// </summary>
    [TestFixture]
    public class ScenePropComposerTests
    {
        const int LevelNumber = 1;

        static SceneLayout Level1Layout()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(LevelNumber, 50, 17);
            LevelData level = LevelCatalog.Get(LevelNumber);
            var spawns = new List<Vector2Int>();
            for (int i = 0; i < level.Units.Count; i++)
                spawns.Add(new Vector2Int(level.Units[i].gridX, level.Units[i].gridY));

            return ScenePropLayout.Build(grid, spawns, LevelNumber * 1013 + 7);
        }

        /// <summary>构建完整的场景美术三角面集合（地形壳 + 潮间带 + 泡沫 + 危险带 + 全部道具）。</summary>
        static ScenePropBuffers ComposeFullLevel1()
        {
            const int arenaW = 50, arenaD = 17;
            var buffers = new ScenePropBuffers();

            TileTerrainGrid grid = TerrainCatalog.Build(LevelNumber, arenaW, arenaD);

            // 地形壳（视觉层）另算，但它也占可见三角面预算，这里一并计入。
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);
            MeshBuffers low = IslandShellGeometry.BuildLowZone(grid, IslandShellSettings.Default.LowPlateYOffset);
            IslandShellGeometry.AddOffsetBand(buffers.SandWet, arenaW, arenaD, 0f, 2.5f, 0f, -0.6f, 1f);
            IslandShellGeometry.AddFlatRingBand(buffers.Foam, arenaW, arenaD, 0.75f, 1.75f, -0.14f, 1f);
            IslandShellGeometry.AddFlatRingBand(buffers.WaterDark, arenaW, arenaD, 2.6f, 3.8f, -0.148f, 1f);
            IslandShellGeometry.AddDashedBorder(buffers.Danger, arenaW, arenaD, 3.15f, -0.138f, 0.9f, 0.55f, 0.12f);

            ScenePropComposer.Compose(buffers, Level1Layout(), 1013 + 7, arenaD);

            TestContext.Progress.WriteLine(
                "[场景美术三角面] 地形壳 " + shell.TriangleCount
                + " | 潮沟贴片 " + low.TriangleCount
                + " | 道具合计 " + buffers.TotalTriangles
                + "（木 " + buffers.Wood.TriangleCount
                + " / 暗木 " + buffers.WoodDark.TriangleCount
                + " / 岩 " + buffers.Rock.TriangleCount
                + " / 铁 " + buffers.Metal.TriangleCount
                + " / 植被 " + buffers.Foliage.TriangleCount
                + " / 布 " + buffers.Cloth.TriangleCount
                + " / 旗 " + (buffers.FlagRed.TriangleCount + buffers.FlagBlue.TriangleCount)
                + " / 湿沙 " + buffers.SandWet.TriangleCount
                + " / 泡沫 " + buffers.Foam.TriangleCount
                + " / 危险线 " + buffers.Danger.TriangleCount
                + " / 暗水带 " + buffers.WaterDark.TriangleCount
                + " / 剪影 " + buffers.Silhouette.TriangleCount
                + " / 云 " + buffers.Cloud.TriangleCount + "）");

            return buffers;
        }

        static MeshBuffers Level1Shell()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(LevelNumber, 50, 17);
            return IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);
        }

        [Test]
        public void TotalVisibleTriangles_StaysUnderSceneDocBudget()
        {
            MeshBuffers shell = Level1Shell();
            ScenePropBuffers props = ComposeFullLevel1();

            int total = shell.TriangleCount + props.TotalTriangles;

            TestContext.Progress.WriteLine("[可见三角面合计] " + total
                + "（预算 150000，场景文档 §8；不含单位与海床台阶）");

            Assert.Less(total, 150000,
                "地形壳 + 场景道具的可见三角面 " + total + " 超出场景文档 §8 的 150k 预算");
        }

        [Test]
        public void EveryMaterialGroup_IsNonEmpty_SoNoMaterialIsWasted()
        {
            ScenePropBuffers b = ComposeFullLevel1();

            // 每个材质组都要真的有东西，否则就是在场景里白挂一个渲染器（也白占材质预算）。
            Assert.Greater(b.Wood.TriangleCount, 0, "木（船舷/箱/栈桥/桅）");
            Assert.Greater(b.WoodDark.TriangleCount, 0, "暗木（水线下/桶/桩）");
            Assert.Greater(b.Rock.TriangleCount, 0, "岩（礁石/掩体石）");
            Assert.Greater(b.Metal.TriangleCount, 0, "铁（锚/链/箍）");
            Assert.Greater(b.Foliage.TriangleCount, 0, "植被（棕榈叶/灌木/草）");
            Assert.Greater(b.Cloth.TriangleCount, 0, "布/索（帆/缆/贝壳）");
            Assert.Greater(b.FlagRed.TriangleCount, 0, "红队旗");
            Assert.Greater(b.FlagBlue.TriangleCount, 0, "蓝队旗");
            Assert.Greater(b.SandWet.TriangleCount, 0, "湿沙（潮间带/海床坡）");
            Assert.Greater(b.Foam.TriangleCount, 0, "泡沫线");
            Assert.Greater(b.Danger.TriangleCount, 0, "危险虚线");
            Assert.Greater(b.WaterDark.TriangleCount, 0, "暗水带");
            Assert.Greater(b.Silhouette.TriangleCount, 0, "远景剪影");
            Assert.Greater(b.Cloud.TriangleCount, 0, "云/远帆");
        }

        [Test]
        public void GrassDominatesTriangleCount_ButStaysReasonablePerTuft()
        {
            ScenePropBuffers b = ComposeFullLevel1();
            SceneLayout layout = Level1Layout();

            int grass = layout.CountOf(ScenePropKind.GrassTuft);
            Assert.Greater(grass, 0);

            float perTuft = b.Foliage.TriangleCount / (float)grass;
            TestContext.Progress.WriteLine("[草丛] " + grass + " 簇，植被组三角面 " + b.Foliage.TriangleCount
                + "，均摊 " + perTuft.ToString("0.0") + " 面/簇（含棕榈叶与灌木）");

            Assert.Less(perTuft, 90f, "单簇草丛相关三角面应远低于 90（否则 1500 簇会吃光预算）");
        }

        [Test]
        public void Wreck_HasEnoughDetailToReadAsBrokenShip_ButNotMore()
        {
            // 场景文档 §4.1：三角面预算 3000-5000。
            var buffers = new ScenePropBuffers();
            ScenePropGeometry.AddWreck(buffers, Vector3.zero, 12f, 14f, 4.6f, 2.0f, 6.4f, 3);

            int wreck = buffers.Wood.TriangleCount + buffers.WoodDark.TriangleCount
                + buffers.Cloth.TriangleCount + buffers.Metal.TriangleCount;

            TestContext.Progress.WriteLine("[搁浅船] 三角面 " + wreck
                + "（木 " + buffers.Wood.TriangleCount + " / 暗木 " + buffers.WoodDark.TriangleCount
                + " / 布 " + buffers.Cloth.TriangleCount + " / 铁 " + buffers.Metal.TriangleCount + "）");

            Assert.Greater(wreck, 800, "船体细节过少会读成盒子（§4.1 的核心论点）");
            Assert.Less(wreck, 6000, "船体三角面超出 §4.1 预算上限 5000（含缆绳/帆/铁件后放宽到 6000）");
        }

        [Test]
        public void SingleProps_RespectTheirSceneDocSizeRanges()
        {
            // 单件尺寸用"几何包围盒"核对，防止手改常量时把 0.6 的箱子改成 6。
            var crate = new ScenePropBuffers();
            ScenePropGeometry.AddCrate(crate, Vector3.zero, 0f, 0.6f);
            Bounds crateBounds = BoundsOf(crate.Wood);
            Assert.AreEqual(0.6f, crateBounds.size.x, 0.06f, "木箱 0.6 立方（§4.3）");
            Assert.AreEqual(0.6f, crateBounds.size.y, 0.06f);

            var barrel = new ScenePropBuffers();
            ScenePropGeometry.AddBarrel(barrel, Vector3.zero, 0f, 0.25f, 0.7f);
            Bounds barrelBounds = BoundsOf(barrel.WoodDark);
            Assert.AreEqual(0.7f, barrelBounds.size.y, 0.05f, "火药桶 0.5 径 × 0.7 高（§4.3）");
            Assert.AreEqual(0.5f, barrelBounds.size.x, 0.06f);

            var flag = new ScenePropBuffers();
            ScenePropGeometry.AddFlagPole(flag, Vector3.zero, 0f, true, 3.2f, 0);
            Bounds flagBounds = BoundsOf(flag.Wood, flag.FlagRed);
            Assert.That(flagBounds.max.y, Is.InRange(3.0f, 3.5f), "旗杆高 3.0-3.5（§4.4）");
        }

        [Test]
        public void HorizonBaseY_PutsSilhouettesBelowTheWaterFarEdgeSightLine()
        {
            const int arenaDepth = 17;

            // 水远缘（Z = 8.5 - 40 = -31.5）处的视线高度。
            float atFarEdge = ScenePropComposer.HorizonBaseY(-31.5f, arenaDepth);
            float deeper = ScenePropComposer.HorizonBaseY(-55f, arenaDepth);
            float shallower = ScenePropComposer.HorizonBaseY(-20f, arenaDepth);

            TestContext.Progress.WriteLine("[远景基座] Z=-20 → y=" + shallower.ToString("0.00")
                + "；Z=-31.5 → y=" + atFarEdge.ToString("0.00")
                + "；Z=-55 → y=" + deeper.ToString("0.00"));

            // 越远的剪影基座必须越低（否则远岛会浮在近岛上方）。
            Assert.Less(deeper, atFarEdge);
            Assert.Less(atFarEdge, shallower);

            // 量级核对：Z=-55 时约 -11.1（手算：camY 12.73 − 0.313 × 76.2）。
            Assert.AreEqual(-11.1f, deeper, 0.6f);
        }

        static Bounds BoundsOf(params MeshBuffers[] groups)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            for (int g = 0; g < groups.Length; g++)
            {
                Vector3[] v = groups[g].ToVertices();
                for (int i = 0; i < v.Length; i++)
                {
                    if (first)
                    {
                        bounds = new Bounds(v[i], Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(v[i]);
                    }
                }
            }

            return bounds;
        }
    }
}
