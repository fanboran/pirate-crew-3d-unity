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
        public void TerrainCatalog_Level1_UsesPlatformClusterLayout()
        {
            // level_1 已平台化：逐格水陆、4 个平台簇（3 主簇 + 1 北侧小空岛），
            // 地面 411 / 850 格 = 48.4%（docs/关卡设计语言-参照游戏全场景分析.md §5.3 实算）。
            // 规则出处：R1（主簇包络 ≥8×4 且容纳全部出生点）、R9（平台簇数控制掩体密度）。
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);

            Assert.IsNotNull(grid);
            Assert.AreEqual(4, grid.ClusterCount, "level_1 应为 4 个平台簇（§5.3）");
            Assert.AreEqual(411, grid.GroundCellCount, "平台地面格数应为 411（§5.3 实算）");
            Assert.AreEqual(850 - 411, grid.WaterCellCount, "其余 439 格是水（掉落即死）");
            Assert.Greater(grid.SolidCellCount, 0, "平台格都有 ≥1 块抬升");

            // 中央大船簇：长条甲板（R1 主簇）与 5 块桅盘制高点（R8/R13「抢高台」记忆点）。
            Assert.IsTrue(grid.IsGroundAt(24, 5), "大船甲板西缘应是平台地面");
            Assert.AreEqual(2, grid.BlocksAt(24, 5), "大船甲板块高 2（§5.3）");
            Assert.AreEqual(5, grid.BlocksAt(29, 8), "桅盘是全场制高点 5 块（§5.3）");

            // 两侧出生簇的顶层高低差（§5.3：①梯田 3 层、③空岛岩峰 4 层）。
            Assert.IsTrue(grid.IsGroundAt(10, 8));
            Assert.AreEqual(3, grid.BlocksAt(10, 8), "西梯田岛顶层块高 3");
            Assert.AreEqual(4, grid.BlocksAt(46, 6), "东空岛岩峰块高 4");

            // 平台之间是水：西岛与中央大船之间的水道（②↔③ 主水道同样由水距断言覆盖）。
            Assert.IsFalse(grid.IsGroundAt(21, 8), "西岛与中央大船之间应是水");
            Assert.AreEqual(0, grid.BlocksAt(21, 8), "水格无抬升块");
        }

        [Test]
        public void TerrainCatalog_Level1_SurfaceWorldY_PlatformAndWater()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);

            // 单块 8px = 0.25 世界单位（TerrainCatalog.DefaultBlockWorldHeight）。
            Assert.AreEqual(0.25f, grid.BlockWorldHeight, 1e-6f);

            // 平台地面：地表 = 基础地面 + 块高 × 0.25（甲板 2 块 → +0.5；桅盘 5 块 → +1.25）。
            Assert.AreEqual(LevelGeometry.GroundTopY + 0.5f, grid.SurfaceWorldY(24, 5), 1e-5f);
            Assert.AreEqual(LevelGeometry.GroundTopY + 1.25f, grid.SurfaceWorldY(29, 8), 1e-5f);

            // 水格（(21,8) 水道）：游戏性查询返回虚空哨兵（低于水面），使 AI 投掷模拟走落水分支。
            Assert.Less(grid.SurfaceWorldYAtWorld(21.5f, 8.5f), LevelGeometry.WaterSurfaceY,
                "水格地表必须低于水面，否则 AI 落水判定失效");
            Assert.AreEqual(TileTerrainGrid.WaterVoidY, grid.SurfaceWorldYAtWorld(21.5f, 8.5f), 1e-6f);

            // 出生位安全（R2：每个出生点 ≥4 格平台面积；水格不是安全出生位）。
            Assert.IsTrue(TerrainCatalog.IsPlatformSpawnSafe(grid, 29, 11), "红队 (29,11) 在甲板上");
            Assert.IsFalse(TerrainCatalog.IsPlatformSpawnSafe(grid, 21, 8), "水格不是安全出生位");
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
        public void AiTerrain_LegacyRaisedColumn_IsWall_ButLowBumpIsNot()
        {
            // 列式旧地形：3 块及以上是墙体立面（RaisedWallBlocks），1-2 块是低矮沙埂仍可放置。
            var blocks = new int[4 * 4];
            blocks[1 + 1 * 4] = 4;   // 格 (1,1) 高台 → 墙
            blocks[2 + 1 * 4] = 1;   // 格 (2,1) 低埂 → 可放置
            var grid = new TileTerrainGrid(4, 4, blocks, 0.25f);
            var terrain = new AiTerrain(0f, 128f, 0f, 128f, grid);

            Assert.IsTrue(terrain.IsBlocked(48f, 48f), "4 块高台应视为墙，不可放置");
            Assert.IsTrue(terrain.IsRaisedWallAt(48f, 48f));
            Assert.IsFalse(terrain.IsBlocked(80f, 48f), "1 块沙埂仍可承受箱体");
            Assert.IsFalse(terrain.IsRaisedWallAt(80f, 48f));
        }

        [Test]
        public void AiTerrain_PlatformLevel_WaterBlocked_DeckPlaceable()
        {
            // 【平台化修正】旧判据（块高>0）会把整片甲板误判为占用；水格才是不可放置的格。
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);
            var terrain = new AiTerrain(0f, 50f * 32f, 0f, 17f * 32f, grid);

            // 水格（(21,8) 水道中心 → 世界 (21.5,8.5) → 像素 (688,272)）：不可放置。
            Assert.IsFalse(grid.IsGroundAt(21, 8));
            Assert.IsTrue(terrain.IsBlocked(21.5f * 32f, 8.5f * 32f), "水格不可放置箱体");
            Assert.IsFalse(terrain.CanPlace(21.5f * 32f, 8.5f * 32f, 8f, 8f));

            // 平台甲板（(24,5)，块高 2）：合法放置面——修复「甲板全被判占用」。
            Assert.IsTrue(grid.IsGroundAt(24, 5));
            Assert.IsFalse(terrain.IsBlocked(24.5f * 32f, 5.5f * 32f), "平台甲板是合法放置面");
            Assert.IsTrue(terrain.CanPlace(24.5f * 32f, 5.5f * 32f, 8f, 8f));

            // 桅盘 5 块：块高 ≠ 墙，仍是可放置面；但 IsRaisedWallAt 能识别它是高台。
            Assert.AreEqual(5, grid.BlocksAt(29, 8));
            Assert.IsFalse(terrain.IsBlocked(29.5f * 32f, 8.5f * 32f), "高楼顶也是平台地面，可放置");
            Assert.IsTrue(terrain.IsRaisedWallAt(29.5f * 32f, 8.5f * 32f), "块高≥阈值应识别为高墙/高台");
            Assert.IsFalse(terrain.IsRaisedWallAt(24.5f * 32f, 5.5f * 32f), "2 块甲板不是高墙");
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
