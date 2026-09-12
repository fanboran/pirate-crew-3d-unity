using NUnit.Framework;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 瓦片地形系统与相关接线的纯 C# 测试（无头可跑；不碰 GameObject/MonoBehaviour）。
    ///
    /// 【覆盖】
    ///   · <see cref="TerrainCatalog"/>：已转写关卡的尺寸/块数/边界，未转写与尺寸不符时返回 null；
    ///   · <see cref="TileTerrainGrid"/>：地表高度查询、逐块递减、爆炸整格摧毁、平坦退化；
    ///   · <see cref="MinimapRules"/>：§8.1 实心/空两档 alpha（50/20 → 0.5/0.2）；
    ///   · <see cref="AimThrowController.IsDirectPlacementWeapon"/>：4 把非弹道武器的分类；
    ///   · <see cref="AiTerrain"/>：瓦片地形接入后的地表/遮挡/放置查询。
    /// </summary>
    [TestFixture]
    public class TileTerrainTests
    {
        // ------------------------------------------------------------------
        // TerrainCatalog
        // ------------------------------------------------------------------

        [Test]
        public void TerrainCatalog_Level1_HasExpectedDimensions()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);

            Assert.IsNotNull(grid, "level_1 应已转写地形");
            Assert.AreEqual(50, grid.WidthTiles);
            Assert.AreEqual(17, grid.DepthTiles);
            Assert.Greater(grid.SolidCellCount, 0, "level_1 应有抬升块");
            Assert.AreEqual(TerrainCatalog.MaxBlocksPerColumn, 8);
        }

        [Test]
        public void TerrainCatalog_Level1_ColumnBlocksMatchTranscription()
        {
            // level_1：第 23 列（原版桅杆/岛台，topRow 4）记满 8 块；第 19 列（原版水道）记 0；
            // 第 0 列（原版船体，topRow 11）记 1。数据由关卡 XML 瓦片行按类头推导得到。
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);

            Assert.AreEqual(8, grid.BlocksAt(23, 0), "第 23 列应为最高台（8 块）");
            Assert.AreEqual(0, grid.BlocksAt(19, 0), "第 19 列为原版水道，记 0（基础地面）");
            Assert.AreEqual(1, grid.BlocksAt(0, 0), "第 0 列记 1 块");

            // 格高度沿纵深（Z）不变（列一致的梯田），这也是本工程对「行序当高度」的取舍。
            Assert.AreEqual(grid.BlocksAt(23, 0), grid.BlocksAt(23, 16));
        }

        [Test]
        public void TerrainCatalog_Level1_SurfaceWorldY_UsesBlockHeight()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);

            // 单块 8px = 0.25 世界单位；8 块 = 2.0。
            Assert.AreEqual(0.25f, grid.BlockWorldHeight, 1e-6f);
            Assert.AreEqual(LevelGeometry.GroundTopY + 2f, grid.SurfaceWorldY(23, 5), 1e-5f);
            Assert.AreEqual(LevelGeometry.GroundTopY, grid.SurfaceWorldY(19, 5), 1e-5f);
        }

        [Test]
        public void TerrainCatalog_UntranscribedLevel_ReturnsNull()
        {
            Assert.IsNull(TerrainCatalog.Build(2, 47, 27), "未转写关卡应退回平坦地面");
            Assert.IsNull(TerrainCatalog.ColumnBlocksFor(2));
            Assert.IsFalse(TerrainCatalog.IsTranscribed(2));
        }

        [Test]
        public void TerrainCatalog_SizeMismatch_ReturnsNullInsteadOfMisplacedTerrain()
        {
            // 转写表按 widthTiles 索引；尺寸不符时必须退回平地而不是错位生成。
            Assert.IsNull(TerrainCatalog.Build(1, 49, 17));
        }

        [Test]
        public void TerrainCatalog_Level27_HasTwoTiers()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(27, 21, 20);

            Assert.IsNotNull(grid);
            Assert.AreEqual(8, grid.BlocksAt(0, 0), "两侧高台 8 块");
            Assert.AreEqual(0, grid.BlocksAt(5, 0), "中间缺口（第 4–7 列）0 块");
            Assert.AreEqual(8, grid.BlocksAt(10, 0), "中央高台 8 块");
        }

        // ------------------------------------------------------------------
        // TileTerrainGrid
        // ------------------------------------------------------------------

        [Test]
        public void TileTerrainGrid_Flat_HasNoBlocksAndGroundTop()
        {
            TileTerrainGrid grid = TileTerrainGrid.Flat(10, 8);

            Assert.AreEqual(0, grid.SolidCellCount);
            Assert.AreEqual(LevelGeometry.GroundTopY, grid.SurfaceWorldY(3, 4), 1e-6f);
            Assert.IsFalse(grid.IsSolidAt(3, 4));
        }

        [Test]
        public void TileTerrainGrid_DestroyBlock_DecrementsToOne()
        {
            var blocks = new int[4 * 4];
            blocks[1 + 1 * 4] = 3;
            var grid = new TileTerrainGrid(4, 4, blocks, 0.25f);

            Assert.AreEqual(3, grid.BlocksAt(1, 1));
            Assert.IsTrue(grid.DestroyBlock(1, 1));
            Assert.AreEqual(2, grid.BlocksAt(1, 1));
            Assert.AreEqual(LevelGeometry.GroundTopY + 0.5f, grid.SurfaceWorldY(1, 1), 1e-5f);

            Assert.IsTrue(grid.DestroyBlock(1, 1));
            Assert.IsTrue(grid.DestroyBlock(1, 1));
            Assert.IsFalse(grid.DestroyBlock(1, 1), "已空后再调用应返回 false");
            Assert.IsFalse(grid.DestroyBlock(0, 0), "无块格返回 false");
        }

        [Test]
        public void TileTerrainGrid_DestroyInRadius_ZeroesCellsWithin3DDistance()
        {
            var blocks = new int[5 * 5];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = 4;
            var grid = new TileTerrainGrid(5, 5, blocks, 0.25f);

            var destroyed = new System.Collections.Generic.List<int>();
            // 爆心在格 (2,2) 中心的地面高度；半径 1.2 → 命中正交 4 邻 + 自身，共 5 格。
            int count = grid.DestroyInRadius(new Vector3(2.5f, 0f, 2.5f), 1.2f, destroyed);

            Assert.AreEqual(5, count);
            Assert.AreEqual(5, destroyed.Count);
            Assert.AreEqual(0, grid.BlocksAt(2, 2));
            Assert.AreEqual(0, grid.BlocksAt(1, 2));
            Assert.AreEqual(0, grid.BlocksAt(2, 1));
            Assert.AreEqual(0, grid.BlocksAt(3, 2));
            Assert.AreEqual(0, grid.BlocksAt(2, 3));
            Assert.AreEqual(4, grid.BlocksAt(3, 3), "对角（3D 距离 1.5 > 1.2）不应被摧毁");
        }

        [Test]
        public void TileTerrainGrid_OutOfRange_IsBaseGround()
        {
            TileTerrainGrid grid = TileTerrainGrid.Flat(4, 4);

            Assert.AreEqual(0, grid.BlocksAt(-1, 0));
            Assert.AreEqual(0, grid.BlocksAt(0, 99));
            Assert.AreEqual(LevelGeometry.GroundTopY, grid.SurfaceWorldYAtWorld(-5f, 99f), 1e-6f);
        }

        // ------------------------------------------------------------------
        // MinimapRules 两档 alpha（§8.1）
        // ------------------------------------------------------------------

        [Test]
        public void MinimapRules_TileAlpha_TwoTiers()
        {
            Assert.AreEqual(0.5f, MinimapRules.TileAlpha(true), 1e-6f, "实心瓦片 alpha 50/100");
            Assert.AreEqual(0.2f, MinimapRules.TileAlpha(false), 1e-6f, "空瓦片 alpha 20/100");

            Assert.AreEqual(0.5f, MinimapRules.TileColor(true).a, 1e-6f);
            Assert.AreEqual(0.2f, MinimapRules.TileColor(false).a, 1e-6f);
        }

        // ------------------------------------------------------------------
        // 瞄准 UX：4 把非弹道武器
        // ------------------------------------------------------------------

        [Test]
        public void IsDirectPlacementWeapon_ClassifiesFourNonBallisticWeapons()
        {
            Assert.IsTrue(AimThrowController.IsDirectPlacementWeapon(WeaponId.Anchor));
            Assert.IsTrue(AimThrowController.IsDirectPlacementWeapon(WeaponId.Seagull));
            Assert.IsTrue(AimThrowController.IsDirectPlacementWeapon(WeaponId.TidalWave));
            Assert.IsTrue(AimThrowController.IsDirectPlacementWeapon(WeaponId.Cannon));

            // 弹道/弹弓武器保持拖拽预览。
            Assert.IsFalse(AimThrowController.IsDirectPlacementWeapon(WeaponId.CherryBomb));
            Assert.IsFalse(AimThrowController.IsDirectPlacementWeapon(WeaponId.VoodooDoll));
            Assert.IsFalse(AimThrowController.IsDirectPlacementWeapon(WeaponId.Dynamite));
        }

        // ------------------------------------------------------------------
        // AiTerrain 与瓦片地形打通
        // ------------------------------------------------------------------

        [Test]
        public void AiTerrain_WithGrid_ReportsSurfaceHeightAndBlocking()
        {
            var blocks = new int[4 * 4];
            blocks[1 + 1 * 4] = 4;   // 格 (1,1) 抬到 1.0 世界单位
            var grid = new TileTerrainGrid(4, 4, blocks, 0.25f);
            var terrain = new AiTerrain(0f, 128f, 0f, 128f, grid);

            // 平面像素 (48,48) = 世界 (1.5,1.5) = 格 (1,1)。
            Assert.AreEqual(1.0f, terrain.SurfaceWorldYAtPixel(48f, 48f), 1e-5f);
            Assert.IsTrue(terrain.IsBlocked(48f, 48f));
            Assert.IsFalse(terrain.IsBlocked(16f, 16f));
            Assert.IsFalse(terrain.CanPlace(48f, 48f, 8f, 8f), "地形块位置不可放置箱体");
            Assert.IsTrue(terrain.CanPlace(16f, 16f, 8f, 8f));
        }

        [Test]
        public void AiTerrain_WithoutGrid_IsFlatGround()
        {
            var terrain = new AiTerrain(0f, 100f, 0f, 100f);

            Assert.AreEqual(LevelGeometry.GroundTopY, terrain.SurfaceWorldYAtPixel(50f, 50f), 1e-6f);
            Assert.IsFalse(terrain.IsBlocked(50f, 50f));
            Assert.IsTrue(terrain.CanPlace(50f, 50f, 8f, 8f));
        }
    }
}
