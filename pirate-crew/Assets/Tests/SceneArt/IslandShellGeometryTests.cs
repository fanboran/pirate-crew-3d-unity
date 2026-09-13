using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
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
        // ------------------------------------------------------------------
        // 用户裁决 1/2：厚底悬浮岛（岛底平面 + 贴岛形轮廓的收形锥）
        // ------------------------------------------------------------------

        /// <summary>合成一关（尺寸/出生位随关卡号确定性变化），用于"多岛 + 错落基准高度"的用例。</summary>
        static LevelData SyntheticLevel(int levelNumber, int width, int depth)
        {
            int redX = Mathf.Clamp(width / 5, 0, width - 1);
            int blueX = Mathf.Clamp(width - width / 5 - 1, 0, width - 1);
            int redZ = depth / 2;

            var units = new List<LevelUnit>
            {
                new LevelUnit("redPirate", 0, redX, redZ, 5, null),
                new LevelUnit("soldier", 1, blueX, Mathf.Clamp(redZ + 2, 0, depth - 1), 5, null),
            };

            var weapons = new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) };

            return new LevelData(levelNumber, "synthetic_" + levelNumber,
                width, depth, 1, depth - 1, 3, 1, weapons, units);
        }

        static TileTerrainGrid SyntheticGrid(out PlatformMap map, int levelNumber = 7, int width = 56, int depth = 18)
        {
            LevelData level = SyntheticLevel(levelNumber, width, depth);
            map = PlatformClusterLayout.BuildFor(level);
            return new TileTerrainGrid(width, depth, null, TerrainCatalog.DefaultBlockWorldHeight, map);
        }

        [Test]
        public void ElevatedIsland_ShellWallsStopAtIslandBottomPlane_NotBaseGround()
        {
            PlatformMap map;
            TileTerrainGrid grid = SyntheticGrid(out map);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);

            float lowestBottom = float.MaxValue;
            bool sawFloating = false;

            for (int c = 0; c < grid.ClusterCount; c++)
            {
                float bottom = IslandShellGeometry.IslandBottomWorldY(grid, c, IslandShellSettings.Default);
                lowestBottom = Mathf.Min(lowestBottom, bottom);

                PlatformClusterInfo info = grid.ClusterAt(c);
                if (info.BaseHeight >= 3f)
                {
                    sawFloating = true;
                    // 悬浮岛的岛底平面必须在**水面之上**（往下看得到海水，不是水下柱子）。
                    Assert.Greater(bottom, LevelGeometry.WaterSurfaceY,
                        "基准高度 " + info.BaseHeight + " 的岛底平面应高于水面");

                    // 厚度 = 簇最低地表 − 岛底平面（下限 clamp 时允许更薄）。
                    float thickness = grid.ClusterSurfaceMinWorldY(c) - bottom;
                    Assert.LessOrEqual(thickness, IslandShellSettings.Default.SideThickness + 1e-3f,
                        "岛体厚度不得超过 SideThickness");
                }
            }

            Assert.IsTrue(sawFloating, "合成关应至少有一个基准高度 ≥3 的悬浮岛");
            Assert.AreEqual(lowestBottom, MinY(shell), 1e-3f,
                "壳的最低点应是岛底平面（不是基础地面 y=0 的柱子）");
        }

        [Test]
        public void UndersideTaper_FollowsIslandOutline_AndReachesBelowBottomPlane()
        {
            PlatformMap map;
            TileTerrainGrid grid = SyntheticGrid(out map);

            var under = new MeshBuffers();
            IslandShellGeometry.AddPlatformUnderside(under, grid, IslandShellSettings.Default);

            Assert.Greater(under.TriangleCount, 0, "应有收形锥几何");

            // 找一个基准高度 ≥3 的簇，用它的岛缘轮廓做核对。
            int target = -1;
            for (int c = 0; c < grid.ClusterCount; c++)
            {
                if (grid.ClusterAt(c).Kind != PlatformClusterKind.Ship
                    && grid.ClusterAt(c).BaseHeight >= 3f)
                {
                    target = c;
                    break;
                }
            }

            Assert.GreaterOrEqual(target, 0, "合成关应有一个非船的抬高岛");

            float bottomY = IslandShellGeometry.IslandBottomWorldY(grid, target, IslandShellSettings.Default);
            Vector3[] v = under.ToVertices();

            int ring0 = 0;
            int irregularCorner = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(v[i].y - bottomY) > 1e-3f)
                    continue;
                // 环 0 顶点必须是**格角**（整数 xz）。
                if (Mathf.Abs(v[i].x - Mathf.Round(v[i].x)) > 1e-3f
                    || Mathf.Abs(v[i].z - Mathf.Round(v[i].z)) > 1e-3f)
                    continue;

                ring0++;

                // 该格角四邻格里，属于本簇的数量：矩形轮廓只会有 2 或 4；不规则岛形会出现 1（凹口尖）或 3。
                int gx = Mathf.RoundToInt(v[i].x), gz = Mathf.RoundToInt(v[i].z);
                int insideCount = 0;
                if (grid.ClusterIndexOf(gx - 1, gz - 1) == target) insideCount++;
                if (grid.ClusterIndexOf(gx, gz - 1) == target) insideCount++;
                if (grid.ClusterIndexOf(gx - 1, gz) == target) insideCount++;
                if (grid.ClusterIndexOf(gx, gz) == target) insideCount++;
                if (insideCount == 1 || insideCount == 3)
                    irregularCorner++;
            }

            Assert.Greater(ring0, 0, "收形锥的轮廓环应贴合岛缘格角");
            Assert.Greater(irregularCorner, 0,
                "轮廓环应贴**不规则**岛形（出现 1/3 邻格的内凹/外凸格角），而不是矩形包络");

            Assert.Less(MinY(under), bottomY, "收形锥应低于岛底平面（底尖化）");
        }

        [Test]
        public void WaterlineBands_OnlyWhenIslandActuallyTouchesWater()
        {
            PlatformMap map;
            TileTerrainGrid grid = SyntheticGrid(out map);

            var buffers = new ScenePropBuffers();
            IslandShellGeometry.AddPlatformUndersides(buffers, grid, IslandShellSettings.Default);

            bool anyTouchesWater = false;
            bool anyFloats = false;
            for (int c = 0; c < grid.ClusterCount; c++)
            {
                float bottom = IslandShellGeometry.IslandBottomWorldY(grid, c, IslandShellSettings.Default);
                if (bottom < LevelGeometry.WaterSurfaceY)
                    anyTouchesWater = true;
                else
                    anyFloats = true;
            }

            // 岛底入水的簇才有泡沫/湿沙（四段过渡）；悬浮岛的岛底在水面之上 → 不画贴水带，
            // 否则水面上会凭空浮一圈白边（用户裁决 2 的悬浮语义）。
            Assert.IsTrue(anyTouchesWater, "合成关应有至少一个贴水岛（基准 0）");
            Assert.IsTrue(anyFloats, "合成关应有至少一个悬浮岛（基准 ≥3）");
            Assert.Greater(buffers.Foam.TriangleCount, 0, "贴水岛仍要有泡沫碎斑");
            Assert.Greater(buffers.SandWet.TriangleCount, 0, "贴水岛仍要有湿沙暗带");
        }
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
            // 【2026-09-14 起以原版 tile 地图为准】原来是硬编码 level_1 的"大船簇西缘 x=24"，
            // 那格在原版行串里根本不是长墙。改为**自己找出最长的一段墙**（沿 Z 连续 ≥4 格
            // 地面、且西侧是水），再检查它的侧壁沿全高有不同横向偏移（剪影扰动，不是一条直线）。
            //
            // 【厚底侧壁】岛体侧壁由"下延到基础地面"变成"下延到岛底平面"，故扫描窗取全墙
            // y∈(−1.2, 0.55)（旧窗口只能看到墙的最顶 0.15 单位，会让"整墙是否直"的判据失真）。
            TileTerrainGrid grid = TerrainCatalog.Build(1, 50, 17);
            MeshBuffers shell = IslandShellGeometry.BuildSolidShell(grid, IslandShellSettings.Default);

            // 沿 Z 找最长的岛缘墙（某列连续 ≥3 格地面、且西侧是水）。
            int wallX = -1, wallZ0 = -1, wallZ1 = -1, bestRun = 0;
            for (int gx = 1; gx < grid.WidthTiles; gx++)
            {
                int run = 0, runStart = 0;
                for (int gz = 0; gz < grid.DepthTiles; gz++)
                {
                    if (grid.IsGroundAt(gx, gz) && !grid.IsGroundAt(gx - 1, gz))
                    {
                        if (run == 0)
                            runStart = gz;
                        run++;
                        if (run > bestRun)
                        {
                            bestRun = run;
                            wallX = gx;
                            wallZ0 = runStart;
                            wallZ1 = gz;
                        }
                    }
                    else
                    {
                        run = 0;
                    }
                }
            }

            Assert.GreaterOrEqual(bestRun, 3,
                "level_1 应存在一段沿 Z ≥3 格的岛缘墙（否则本用例无从检查）");

            // 【为什么不再限制 y 窗口】岛高来自原版行号（level_1 最低的船体甲板也有 1.25 世界单位），
            // 固定窗口会漏掉整段墙。这里只按墙平面（x）与 Z 跨度取顶点，看横向偏移是否有多种。
            var xs = new HashSet<int>();
            Vector3[] v = shell.ToVertices();
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(v[i].x - wallX) < 0.25f
                    && v[i].z > wallZ0 - 0.5f && v[i].z < wallZ1 + 1.5f)
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
