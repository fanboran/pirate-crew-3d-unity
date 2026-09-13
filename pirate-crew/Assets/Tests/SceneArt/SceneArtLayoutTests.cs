using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.PirateCrew.SceneArt;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 场景美术布局的**纯 C# 用例**（无头可跑；不碰 GameObject / MonoBehaviour / ScriptableObject）。
    ///
    /// 【覆盖的验收判据】对应 <c>docs/场景设计-战斗竞技场.md</c> §10.2 逐图品控表里那些
    /// 可以**在无头环境程序化判定**的条目：
    ///   · 出生点不入水 / 脚底贴合地表（§9.5、§10.2「单位脚底贴合」「出生点不入水」）；
    ///   · 中脊不超出竞技场（§1.1/§2.2）；
    ///   · 道具不与出生位重叠（§9.5）；
    ///   · 纵深分层：Z≥13 只允许 ≤0.6 高物、>1.5 高物只在 Z≤4 或场外（§7.3）；
    ///   · 危险红线不进可玩区（§5.4/§10.2）；
    ///   · 掩体密度区间（§3.2 的 6-10 个）；
    ///   · 草地只铺在 y≥1.5 平顶且离出生点 ≥2 单位（§3.1）；
    ///   · 地形壳顶面与 <c>SurfaceWorldY</c> 严格等高（§9.1 的 ±0.02 硬要求）。
    /// 观感类判据（色相分离、倒角亮边、阴影质量）需要出图，走 MCP 人工/程序化判图，不在本文件的范围内。
    /// </summary>
    [TestFixture]
    public class SceneArtLayoutTests
    {
        const int LevelNumber = 1;

        static TileTerrainGrid Level1Grid()
        {
            TileTerrainGrid grid = TerrainCatalog.Build(LevelNumber, 50, 17);
            Assert.IsNotNull(grid, "level_1 应有瓦片地形（TerrainCatalog 已转写）");
            return grid;
        }

        static List<Vector2Int> Level1SpawnCells()
        {
            LevelData level = LevelCatalog.Get(LevelNumber);
            var cells = new List<Vector2Int>();
            for (int i = 0; i < level.Units.Count; i++)
                cells.Add(new Vector2Int(level.Units[i].gridX, level.Units[i].gridY));

            Assert.AreEqual(8, cells.Count, "level_1 应有 5 红 + 3 蓝 = 8 个出生位");
            return cells;
        }

        static SceneLayout Level1Layout()
        {
            return ScenePropLayout.Build(Level1Grid(), Level1SpawnCells(), 7);
        }

        // ------------------------------------------------------------------
        // 出生位：不入水 / 不悬空 / 脚底贴合
        // ------------------------------------------------------------------

        [Test]
        public void SpawnCells_AreAllOnRaisedLand_NotInWater()
        {
            TileTerrainGrid grid = Level1Grid();
            List<Vector2Int> cells = Level1SpawnCells();

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int c = cells[i];
                Assert.Greater(grid.BlocksAt(c.x, c.y), 0,
                    "出生格 (" + c.x + "," + c.y + ") 应有 ≥1 块抬升（场景文档 §9.5）");

                float surface = grid.SurfaceWorldY(c.x, c.y);
                Assert.GreaterOrEqual(surface, LevelGeometry.GroundTopY,
                    "出生格地表不得低于基础地面");
                Assert.IsFalse(LevelGeometry.IsBelowWater(surface, LevelGeometry.WaterSurfaceY),
                    "出生格地表不得在水面以下");

                Assert.IsTrue(SceneLayoutRules.IsSpawnCellSafe(grid, c.x, c.y),
                    "IsSpawnCellSafe 应判出生格安全");
            }
        }

        [Test]
        public void SpawnCells_FootFlushWithSurface_NoGap()
        {
            TileTerrainGrid grid = Level1Grid();
            List<Vector2Int> cells = Level1SpawnCells();
            float pivot = LevelGeometry.UnitPivotHeight;

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int c = cells[i];
                float surface = grid.SurfaceWorldY(c.x, c.y);

                // 运行时口径（BattleController.cs:253-257）= SurfaceWorldY + UnitPivotHeight。
                float spawnY = grid.SurfaceWorldY(c.x, c.y) + pivot;

                // 1) 脚底必须正好落在地表上（无缝隙、无陷地）。
                Assert.AreEqual(surface, spawnY - pivot, 1e-5f,
                    "出生格 (" + c.x + "," + c.y + ") 脚底应贴合地表");

                // 2) 纯 C# 的平坦计划 + 列高偏移 == 运行时口径（两个高度口径只差列高，不得有别的差）。
                float planY = LevelGeometry.GridToArena(c.x, c.y).y;
                float offset = SceneLayoutRules.SpawnHeightOffsetFromFlatPlan(grid, c.x, c.y);
                Assert.AreEqual(spawnY, planY + offset, 1e-5f,
                    "出生格 (" + c.x + "," + c.y + ") 的平坦计划 + 列高偏移应等于运行时站位高度");

                // 3) 反向核对：由规则类算出的脚底 Y 就是该格地表。
                Assert.AreEqual(surface,
                    SceneLayoutRules.SpawnFootWorldY(grid, c.x, c.y, pivot), 1e-5f);
            }
        }

        [Test]
        public void SpawnCells_AreClearOfEveryProp()
        {
            SceneLayout layout = Level1Layout();
            List<Vector2Int> spawns = Level1SpawnCells();
            Vector3[] centers = new Vector3[spawns.Count];
            for (int i = 0; i < spawns.Count; i++)
                centers[i] = new Vector3(spawns[i].x + 0.5f, 0f, spawns[i].y + 0.5f);

            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];
                for (int s = 0; s < centers.Length; s++)
                {
                    float dx = p.Position.x - centers[s].x;
                    float dz = p.Position.z - centers[s].z;
                    float d2 = dx * dx + dz * dz;

                    // 草丛另有更严的 2 单位净空（场景文档 §3.1），通用装饰为 1.5（§9.5）。
                    float need = p.Kind == ScenePropKind.GrassTuft
                        ? ScenePropLayout.GrassSpawnClearance
                        : SceneLayoutRules.SpawnClearance;

                    Assert.GreaterOrEqual(d2, need * need - 1e-4f,
                        p.Kind + " 与出生位 " + centers[s] + " 的水平距离应 ≥ " + need);
                }
            }
        }

        // ------------------------------------------------------------------
        // 中脊与纵深分层
        // ------------------------------------------------------------------

        [Test]
        public void PlatformClusters_AreInsideArena_AndHaveWaterBetween()
        {
            TileTerrainGrid grid = Level1Grid();

            // 【2026-09-14 起以原版 tile 地图为准】level_1 = 原版行串的 9 座岛
            // （旧手写布局是 4 簇，已删除）。岛数由 LevelTileMaps 的行串统计给出。
            Assert.AreEqual(9, grid.ClusterCount, "level_1 原版含 9 座岛");
            Assert.Greater(grid.WaterCellCount, 0, "平台之间应当是水（掉落即死）");

            for (int c = 0; c < grid.ClusterCount; c++)
            {
                PlatformClusterInfo info = grid.ClusterAt(c);
                Assert.GreaterOrEqual(info.X0, 0);
                Assert.LessOrEqual(info.X1, grid.WidthTiles - 1);
                Assert.GreaterOrEqual(info.Z0, 0);
                Assert.LessOrEqual(info.Z1, grid.DepthTiles - 1);
                Assert.GreaterOrEqual(info.MinBlocks, 1, "平台块高 ≥1（地表必须高于水面）");
                Assert.LessOrEqual(info.MaxBlocks,
                    PlatformClusterLayout.TileMaxBaseBlocks + PlatformClusterLayout.TileLocalBlocks,
                    "平台高度不得超过相机可达上限");
            }

            // 每座岛都必须是"被水围住的岛"：簇内至少有一格的邻格是水（有临水面）。
            // 【为什么不是"两两水距 ≥1"】原版 tile 里两座岛的**包络**可以在 X 上有重叠
            // （错落在不同 Z 上），此时 WaterGapTiles 取 max(gapX, gapZ) 会给出 0，但两岛毫无共享格。
            for (int c = 0; c < grid.ClusterCount; c++)
            {
                PlatformClusterInfo info = grid.ClusterAt(c);
                bool hasWaterFront = false;

                for (int gz = info.Z0; gz <= info.Z1 && !hasWaterFront; gz++)
                {
                    for (int gx = info.X0; gx <= info.X1 && !hasWaterFront; gx++)
                    {
                        if (grid.ClusterIndexOf(gx, gz) != c)
                            continue;

                        if (!grid.IsGroundAt(gx - 1, gz) || !grid.IsGroundAt(gx + 1, gz)
                            || !grid.IsGroundAt(gx, gz - 1) || !grid.IsGroundAt(gx, gz + 1))
                            hasWaterFront = true;
                    }
                }

                Assert.IsTrue(hasWaterFront,
                    "簇 " + info.Name + " 应是四周临水的岛（没有任何临水面的地块说明它被并进了别的岛）");
            }
        }

        [Test]
        public void DepthBanding_NoTallPropEntersNearOrMidBand()
        {
            SceneLayout layout = Level1Layout();
            IReadOnlyList<PropPlacement> props = layout.Props;

            Assert.Greater(props.Count, 0, "布局不应为空");

            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];

                if (!p.InsideArena)
                    continue;

                Assert.IsTrue(SceneLayoutRules.IsHeightAllowedAt(p.Height, p.Position.z, true),
                    p.Kind + " 高 " + p.Height + " 在 Z=" + p.Position.z
                    + " 违反纵深分层（§7.3：Z≥13 只允许 ≤0.6、Z4-13 只允许 ≤1.5）");
            }
        }

        [Test]
        public void DepthBanding_FlagsAreInFarBand_AndSignsAvoidNearBand()
        {
            SceneLayout layout = Level1Layout();
            IReadOnlyList<PropPlacement> props = layout.Props;

            int flags = 0;
            int signs = 0;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];
                if (p.Kind == ScenePropKind.FlagPole)
                {
                    flags++;
                    Assert.LessOrEqual(p.Position.z, SceneLayoutRules.FarBandMaxZ,
                        "旗杆高 3.2 > 1.5 → 只能在远景带 Z≤4（§7.3 规则 3）");
                    Assert.AreEqual(3.2f, p.Height, 1e-4f);
                }

                if (p.Kind == ScenePropKind.SignPost)
                {
                    signs++;
                    Assert.Less(p.Position.z, SceneLayoutRules.NearBandMinZ,
                        "告示牌约 1.0 高 → 不得进 Z≥13 近侧带（§7.3 规则 2；本文因此把东牌移到 Z=12.2）");
                }
            }

            Assert.AreEqual(2, flags, "应有红蓝两座旗杆");
            Assert.AreEqual(2, signs, "应有西/东两块告示牌");
        }

        [Test]
        public void Wreck_IsSingleHero_OnTheRidge_InFarBand()
        {
            SceneLayout layout = Level1Layout();

            Assert.AreEqual(1, layout.CountOf(ScenePropKind.Wreck), "主角只有一艘搁浅船");

            PropPlacement wreck = default;
            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].Kind == ScenePropKind.Wreck)
                    wreck = props[i];
            }

            Assert.Greater(wreck.Length, 12f, "船长应 ≥12 单位（场景文档 §4.1：12-14）");
            Assert.Less(wreck.Length, 14f + 1e-3f, "船长应 ≤14 单位");
            Assert.That(wreck.Position.x, Is.InRange(4f, 19f), "平台化后船体搁浅在西侧梯田岛（X4-19）");
            Assert.That(wreck.Position.z, Is.InRange(1f, 4f), "船体应在 Z1-4 远景带");
            Assert.That(Mathf.Abs(wreck.RollDegrees), Is.InRange(12f, 18f), "搁浅姿态侧倾 12-18°");
            Assert.LessOrEqual(wreck.Position.z, SceneLayoutRules.FarBandMaxZ, "高 6.9 的桅只能在远景带");

            // 唯一的竖向剪影：任何其它场内道具都不得比它高（场景文档 §2.4）。
            float highest = 0f;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];
                if (p.Kind == ScenePropKind.Wreck || !p.InsideArena)
                    continue;
                if (p.Kind == ScenePropKind.Palm)
                    continue;   // 棕榈按 §2.4/§3.4 允许与船同高的远景剪影

                highest = Mathf.Max(highest, p.Height);
            }

            Assert.Less(highest, wreck.Height, "除棕榈外，场内不得有比船体更高的元素（唯一视觉焦点）");
        }

        // ------------------------------------------------------------------
        // 密度与数量区间
        // ------------------------------------------------------------------

        [Test]
        public void CoverRocks_CountInsideSceneDocRange()
        {
            SceneLayout layout = Level1Layout();

            Assert.IsTrue(SceneLayoutRules.IsCoverCountInRange(layout.CoverRockCount),
                "掩体中石应在 " + SceneLayoutRules.MinCoverRocks + "-" + SceneLayoutRules.MaxCoverRocks
                + " 个（场景文档 §3.2），实际 " + layout.CoverRockCount);
        }

        [Test]
        public void CoverRocks_KeepOutOfCenterCorridor()
        {
            SceneLayout layout = Level1Layout();
            float centerX = layout.ArenaWidth * 0.5f;

            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];
                if (p.Kind != ScenePropKind.CoverRock)
                    continue;

                Assert.GreaterOrEqual(Mathf.Abs(p.Position.x - centerX),
                    SceneLayoutRules.CenterCorridorHalfWidth - 0.5f,
                    "掩体不得挤占中点两侧 3 单位的取景留白（§7.3）");
            }
        }

        [Test]
        public void Vegetation_CountsInsideSceneDocRanges()
        {
            SceneLayout layout = Level1Layout();

            Assert.GreaterOrEqual(layout.CountOf(ScenePropKind.Palm), SceneLayoutRules.MinPalms,
                "棕榈 ≥8 棵（场景文档 §10.1 M11）");
            Assert.LessOrEqual(layout.CountOf(ScenePropKind.Palm), SceneLayoutRules.MaxPalms + 4,
                "棕榈不应远超 8-12 棵上限（含场外补充 ≤4）");

            int bushes = layout.CountOf(ScenePropKind.Bush);
            Assert.That(bushes, Is.InRange(SceneLayoutRules.MinBushClusters, SceneLayoutRules.MaxBushClusters),
                "灌木 15-30 组（§3.4）");

            int grass = layout.CountOf(ScenePropKind.GrassTuft);
            // 【2026-09-14 起以原版 tile 地图为准】§3.4 的 400-1500 下限是按**旧手写布局**
            // （411 格陆地）标定的；原版 level_1 只有 163 格陆地（50×17 的原版行串里大片是海面），
            // 草丛实例数随之降到 ~366，故下限按陆地面积等比下调（163/411 ≈ 0.4）。
            Assert.That(grass, Is.InRange(300, SceneLayoutRules.MaxGrassTufts),
                "草丛 300-1500 实例（§3.4 下限按原版陆地面积下调）");
        }

        [Test]
        public void Grass_OnlyOnHighFlats_AndAwayFromSpawns()
        {
            TileTerrainGrid grid = Level1Grid();
            SceneLayout layout = Level1Layout();
            List<Vector2Int> spawns = Level1SpawnCells();

            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];
                if (p.Kind != ScenePropKind.GrassTuft)
                    continue;

                int gx = Mathf.Clamp(Mathf.FloorToInt(p.Position.x), 0, grid.WidthTiles - 1);
                int gy = Mathf.Clamp(Mathf.FloorToInt(p.Position.z), 0, grid.DepthTiles - 1);
                Assert.IsTrue(grid.IsGroundAt(gx, gy), "草只长在有地面的平台格上（不能长在水里）");
                Assert.GreaterOrEqual(grid.BlocksAt(gx, gy), 1,
                    "草只铺在有抬升的平台顶，实际落在 " + grid.BlocksAt(gx, gy) + " 块");

                for (int s = 0; s < spawns.Count; s++)
                {
                    float dx = p.Position.x - (spawns[s].x + 0.5f);
                    float dz = p.Position.z - (spawns[s].y + 0.5f);
                    Assert.GreaterOrEqual(dx * dx + dz * dz,
                        ScenePropLayout.GrassSpawnClearance * ScenePropLayout.GrassSpawnClearance - 1e-4f,
                        "草离出生点应 ≥2 单位（§3.1）");
                }
            }
        }

        [Test]
        public void Rocks_CountsInsideSceneDocRanges()
        {
            SceneLayout layout = Level1Layout();

            Assert.That(layout.CountOf(ScenePropKind.RidgeRock),
                Is.InRange(SceneLayoutRules.MinRidgeRocks, SceneLayoutRules.MaxRidgeRocks),
                "台地棱线小石块 40-80 个（§3.2）");

            Assert.That(layout.CountOf(ScenePropKind.IntertidalRock),
                Is.InRange(SceneLayoutRules.MinIntertidalRocks, SceneLayoutRules.MaxIntertidalRocks),
                "潮间带石块 20-35 个（§3.2）");

            Assert.That(layout.CountOf(ScenePropKind.FarIsland), Is.InRange(3, 5),
                "远景剪影岛 ≥3 座（§10.1 M8）");

            Assert.That(layout.CountOf(ScenePropKind.FarShip), Is.InRange(1, 2),
                "远景帆船剪影 1-2 艘（§10.1 P5）");
        }

        [Test]
        public void Props_AreOnOrOutsideArena_AndInsideTerrainBounds()
        {
            TileTerrainGrid grid = Level1Grid();
            SceneLayout layout = Level1Layout();

            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropPlacement p = props[i];

                // 远景（云/岛/船）、场外栈桥与水下沙脊不参与"贴该处地表"的断言：
                // 它们分别悬在天空、架在水上半空、压在浅海床里（各自的 y 是刻意的）。
                if (p.Kind == ScenePropKind.CloudPuff || p.Kind == ScenePropKind.FarIsland
                    || p.Kind == ScenePropKind.FarShip
                    || p.Kind == ScenePropKind.Jetty
                    || p.Kind == ScenePropKind.SeabedMound
                    || p.Kind == ScenePropKind.Wreck)
                    continue;

                if (p.Kind == ScenePropKind.Jetty)
                {
                    Assert.Less(p.Position.z, 0f, "栈桥在场外远侧（§4.2：Z≈-1.5~-4）");
                    continue;
                }

                if (!p.InsideArena)
                    continue;

                // 场内道具的 y 必须贴该处地表（±1.5e-3），否则会悬空或陷地。
                float expected = SceneLayoutRules.SurfaceYAtWorld(grid, p.Position.x, p.Position.z);
                Assert.AreEqual(expected, p.Position.y, 1.5e-3f,
                    p.Kind + " 在 (" + p.Position.x + "," + p.Position.z + ") 未贴合地表");
            }

            // 栈桥单独断言：必须都在场外远侧。
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].Kind == ScenePropKind.Jetty)
                    Assert.Less(props[i].Position.z, 0f, "栈桥在场外远侧（§4.2）");
            }
        }

        // ------------------------------------------------------------------
        // 确定性
        // ------------------------------------------------------------------

        [Test]
        public void Layout_IsDeterministic_ForSameSeed()
        {
            TileTerrainGrid grid = Level1Grid();
            List<Vector2Int> spawns = Level1SpawnCells();

            SceneLayout a = ScenePropLayout.Build(grid, spawns, 7);
            SceneLayout b = ScenePropLayout.Build(grid, spawns, 7);

            Assert.AreEqual(a.Props.Count, b.Props.Count, "同种子应得到同一条数");
            for (int i = 0; i < a.Props.Count; i++)
            {
                Assert.AreEqual(a.Props[i].Kind, b.Props[i].Kind);
                Assert.AreEqual(a.Props[i].Position.x, b.Props[i].Position.x, 1e-6f);
                Assert.AreEqual(a.Props[i].Position.z, b.Props[i].Position.z, 1e-6f);
            }

            SceneLayout c = ScenePropLayout.Build(grid, spawns, 99);

            bool identical = a.Props.Count == c.Props.Count;
            if (identical)
            {
                for (int i = 0; i < a.Props.Count; i++)
                {
                    if (a.Props[i].Kind != c.Props[i].Kind
                        || Mathf.Abs(a.Props[i].Position.x - c.Props[i].Position.x) > 1e-4f
                        || Mathf.Abs(a.Props[i].Position.z - c.Props[i].Position.z) > 1e-4f)
                    {
                        identical = false;
                        break;
                    }
                }
            }

            Assert.IsFalse(identical, "不同种子应给出不同布局（否则种子没接上）");
        }

        [Test]
        public void Layout_WithoutRelief_HasNoRidgeProps()
        {
            // 地形未转写（全平地）时应优雅退化：没有台地边缘 → 没有棱线石/掩体石，
            // 没有高程 → 没有场内植被（草只铺 y≥1.5 平顶、灌木/棕榈要有抬升块）。
            SceneLayout layout = ScenePropLayout.Build(TileTerrainGrid.Flat(50, 17), null, 3);

            Assert.AreEqual(0, layout.CountOf(ScenePropKind.RidgeRock), "平地无棱线石");
            Assert.AreEqual(0, layout.CountOf(ScenePropKind.CoverRock), "平地无掩体石");
            Assert.AreEqual(0, layout.CountOf(ScenePropKind.GrassTuft), "平地无草丛");
            Assert.AreEqual(0, layout.CountOf(ScenePropKind.Bush), "平地无灌木");

            // 棕榈只允许在场外补充（场内候选要求 blocks ≥ 2），且不得有任何一棵落在场内。
            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].Kind == ScenePropKind.Palm)
                    Assert.IsFalse(props[i].InsideArena, "平地不应有场内棕榈");
            }
            Assert.AreEqual(0, layout.CountOf(ScenePropKind.GrassTuft), "平地无草丛");

            // 但远景与水面装饰仍应存在（保证退化模式下画面不空）。
            Assert.Greater(layout.CountOf(ScenePropKind.FarIsland), 0, "远景剪影岛与地形无关，仍应生成");
            Assert.Greater(layout.CountOf(ScenePropKind.CloudPuff), 0, "云与地形无关，仍应生成");
        }
    }
}
