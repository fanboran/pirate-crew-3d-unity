using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 地形视觉壳与"水陆过渡/危险带"几何的纯 C# 用例（无头可跑）。
    ///
    /// 【为什么这些必须测】场景文档 §9.1 把"壳顶面与碰撞顶面偏差 ≤ ±0.02 单位"列为硬约束，
    /// 一旦越界单位就悬空/陷地；§5.4 又要求红线**只在边界带、不进可玩区**。
    /// 这两条都能在无头环境用顶点坐标直接断言，不必等出图。
    /// </summary>
    [TestFixture]
    public class IslandShellGeometryTests
    {
        static MeshBuffers BuildSingleCell(int blocks /* 放在 (1,1) */, out TileTerrainGrid grid)
        {
            var cells = new int[3 * 3];
            cells[1 + 1 * 3] = blocks;
            grid = new TileTerrainGrid(3, 3, cells, 0.25f);
            return IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);
        }

        static void AssertNormalsAreUnit(MeshBuffers b)
        {
            Vector3[] normals = b.ToNormals();
            for (int i = 0; i < normals.Length; i++)
            {
                Assert.AreEqual(1f, normals[i].magnitude, 1e-3f, "法线应为单位向量，索引 " + i);
            }
        }

        static float MinY(MeshBuffers b)
        {
            Vector3[] v = b.ToVertices();
            float min = float.MaxValue;
            for (int i = 0; i < v.Length; i++)
                min = Mathf.Min(min, v[i].y);
            return min;
        }

        static float MaxY(MeshBuffers b)
        {
            Vector3[] v = b.ToVertices();
            float max = float.MinValue;
            for (int i = 0; i < v.Length; i++)
                max = Mathf.Max(max, v[i].y);
            return max;
        }

        // ------------------------------------------------------------------
        // 顶面与地表等高（§9.1 的 ±0.02 硬要求）
        // ------------------------------------------------------------------

        [Test]
        public void ShellTopPlate_IsExactlyAtSurfaceWorldY()
        {
            TileTerrainGrid grid;
            MeshBuffers shell = BuildSingleCell(4, out grid);

            float surface = grid.SurfaceWorldY(1, 1);
            Assert.AreEqual(LevelGeometry.GroundTopY + 1.0f, surface, 1e-5f, "4 块 = 1.0 单位");
            Assert.AreEqual(surface, MaxY(shell), 1e-5f,
                "壳的顶面必须与该格地表严格等高（偏差 0，远优于 ±0.02 的验收线）");
        }

        [Test]
        public void ShellNeverRisesAboveAnyColumnSurface_OnRealLevel()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);

            // 平台化后不能只看 z=0 那一行（那一行多半是水）；要扫全部**有地面**的格。
            float tallest = LevelGeometry.GroundTopY;
            for (int gz = 0; gz < grid.DepthTiles; gz++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    if (grid.IsGroundAt(gx, gz))
                        tallest = Mathf.Max(tallest, grid.SurfaceWorldY(gx, gz));
                }
            }

            Assert.AreEqual(tallest, MaxY(shell), 1e-5f,
                "任何顶点都不得高于最高台地顶面（装饰不得抬高碰撞语义）");
        }

        // ------------------------------------------------------------------
        // 倒角 / 岩层 / 裙边 / 剪影扰动
        // ------------------------------------------------------------------

        [Test]
        public void ShellHasChamferBand_BelowTopPlate()
        {
            TileTerrainGrid grid;
            MeshBuffers shell = BuildSingleCell(4, out grid);

            float surface = grid.SurfaceWorldY(1, 1);
            float chamferBottom = surface - IslandShellSettings.Default.ChamferHeight;

            Vector3[] v = shell.ToVertices();
            bool hasChamferEdge = false;
            bool hasWall = false;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(v[i].y - chamferBottom) < 1e-4f)
                    hasChamferEdge = true;
                if (v[i].y < chamferBottom - 1e-3f)
                    hasWall = true;
            }

            Assert.IsTrue(hasChamferEdge, "应有 0.15 单位高的倒角带");
            Assert.IsTrue(hasWall, "倒角之下应有侧壁（该格 4 邻皆 0 块，侧壁必须下延到基础地面）");
            Assert.AreEqual(0f, MinY(shell), 1e-4f, "内部格的侧壁应下延到基础地面 y=0");
        }

        [Test]
        public void BorderCell_GetsSkirtDownToMinusZeroPointSix()
        {
            // 边界格（0,0）的外侧必须有裙边下延过水面到 y=-0.6（§3.1 ④）。
            var cells = new int[3 * 3];
            cells[0] = 2;
            var grid = new TileTerrainGrid(3, 3, cells, 0.25f);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);

            Assert.AreEqual(IslandShellSettings.Default.SkirtBottomY, MinY(shell), 1e-4f,
                "边界台地外侧面必须下延到 y=-0.6（否则能看见方块底面的悬空边）");
        }

        [Test]
        public void SideWalls_AreJittered_SoLongWallsAreNotStraight()
        {
            // 平台化：大船簇西缘 x=24（Z=5..12 共 8 格连续地面）外侧（x=23）是水，
            // 其西侧墙最下一层应有不同的横向进/出偏移（剪影扰动，避免"一条直线墙"）。
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);

            var xs = new HashSet<int>();
            Vector3[] v = shell.ToVertices();
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(v[i].x - 24f) < 0.25f && v[i].y > 0.001f && v[i].y < 0.55f
                    && v[i].z > 4f && v[i].z < 13f)
                {
                    xs.Add(Mathf.RoundToInt(v[i].x * 1000f));
                }
            }

            Assert.Greater(xs.Count, 1, "同列沿 Z 的侧壁必须有多种横向偏移（否则长墙是直线）");
        }

        // ------------------------------------------------------------------
        // 网格完整性
        // ------------------------------------------------------------------

        [Test]
        public void ShellMeshBuffers_AreWellFormed()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);

            Assert.Greater(shell.TriangleCount, 0);
            Assert.AreEqual(0, shell.TriangleCount % 1);
            Assert.AreEqual(shell.TriangleCount * 3, shell.VertexCount,
                "平面着色实现每面独立输出 3 个顶点");
            Assert.AreEqual(shell.TriangleCount * 3, shell.IndexCount);
            AssertNormalsAreUnit(shell);
        }

        [Test]
        public void ShellTriangleBudget_StaysUnderSceneDocLimit()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);

            // 场景文档 §8：可见三角面 ≤150k（含全部道具）。地形壳单独留在 60k 以内，
            // 给道具/单位/水体留出余量。
            Assert.Less(shell.TriangleCount, 60000,
                "地形壳三角面 " + shell.TriangleCount + " 超出 60k 预算");
        }

        [Test]
        public void LowZonePlates_OnlyCoverZeroBlockGroundCells_NotWater()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);
            MeshBuffers low = IslandShellGeometry.BuildLowZone(grid, 0.006f);

            // 平台化后 level_1 是逐格水陆：水格（无地面）**不铺**潮沟贴片（那是真海水，不是沙洼）；
            // 只有"有地面且 0 块"的格才铺——平台顶面块高 ≥1，故贴片数应为 0。
            int plateCells = 0;
            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    if (grid.IsGroundAt(gx, gy) && grid.BlocksAt(gx, gy) <= 0)
                        plateCells++;
                }
            }

            Assert.Greater(grid.WaterCellCount, 0, "level_1 平台间应有水（掉落即死）");
            Assert.AreEqual(0, plateCells, "平台化关卡没有 0 块地面格");
            Assert.AreEqual(plateCells * 2, low.TriangleCount,
                "只有 0 块地面格才铺潮沟贴片；水格不铺");
        }

        // ------------------------------------------------------------------
        // 潮间带 / 泡沫 / 危险带
        // ------------------------------------------------------------------

        [Test]
        public void TideSlope_RunsFromGroundToMinusZeroPointSix()
        {
            var band = new MeshBuffers();
            IslandShellGeometry.AddOffsetBand(band, 50f, 17f, 0f, 2.5f, 0f, -0.6f, 1f);

            Assert.Greater(band.TriangleCount, 0);
            Assert.AreEqual(0f, MaxY(band), 1e-4f, "湿沙坡内缘贴地面 y=0");
            Assert.AreEqual(-0.6f, MinY(band), 1e-4f, "湿沙坡外缘降到 y=-0.6");
        }

        [Test]
        public void FoamBand_IsFlatAndOutsideThePlayableRect()
        {
            const float y = -0.14f;
            var foam = new MeshBuffers();
            IslandShellGeometry.AddFlatRingBand(foam, 50f, 17f, 0.75f, 1.75f, y, 1f);

            Vector3[] v = foam.ToVertices();
            Assert.Greater(v.Length, 0);
            for (int i = 0; i < v.Length; i++)
            {
                Assert.AreEqual(y, v[i].y, 1e-5f, "泡沫环带是水平带");
                Assert.IsTrue(v[i].x < 0f || v[i].x > 50f || v[i].z < 0f || v[i].z > 17f,
                    "泡沫线必须落在竞技场矩形之外（不进可玩区）");
            }
        }

        [Test]
        public void DangerDashedLine_StaysOutsidePlayableArea_AndDashesAreShort()
        {
            var danger = new MeshBuffers();
            int dashes = IslandShellGeometry.AddDashedBorder(danger, 50f, 17f, 3.15f, -0.138f, 0.9f, 0.55f, 0.12f);

            Assert.Greater(dashes, 0, "应生成若干短划线");
            Assert.AreEqual(dashes * 6, danger.VertexCount, "每段虚线 = 1 个四边形 = 6 个顶点（平面着色）");

            Vector3[] v = danger.ToVertices();
            for (int i = 0; i < v.Length; i++)
            {
                // 红色危险线不得进可玩区（场景文档 §5.4/§10.2：可玩区内无红线）。
                Assert.IsTrue(v[i].x < 0f || v[i].x > 50f || v[i].z < 0f || v[i].z > 17f,
                    "危险虚线越界进了可玩区，顶点 " + v[i]);

                // 线宽受控：同一段虚线沿"外法线方向"的跨度 ≤ 0.12（场景文档 §5.4 要求 ≤1 单位）。
                Assert.LessOrEqual(Mathf.Min(v[i].x + 3.15f, 53.15f - v[i].x, v[i].z + 3.15f, 20.15f - v[i].z), 0.07f,
                    "线宽应为 0.12（半宽 0.06），顶点 " + v[i]);
            }
        }

        [Test]
        public void MeshBuffers_DropsDegenerateQuads()
        {
            var b = new MeshBuffers();
            b.AddQuad(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.up);
            Assert.AreEqual(0, b.TriangleCount, "零面积面应被丢弃而不是污染法线");
            Assert.IsTrue(b.IsEmpty);
        }
    }
}
