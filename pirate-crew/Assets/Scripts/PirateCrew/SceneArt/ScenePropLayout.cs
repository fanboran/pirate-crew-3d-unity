using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>场景装饰的种类（决定用哪个几何生成器与哪个材质组）。</summary>
    public enum ScenePropKind
    {
        /// <summary>搁浅断船（主角，场景文档 §4.1）。</summary>
        Wreck,

        /// <summary>木栈桥（§4.2，含桩）。</summary>
        Jetty,

        /// <summary>旗杆 + 旗帜（§4.4）。</summary>
        FlagPole,

        /// <summary>告示牌（§4.4）。</summary>
        SignPost,

        /// <summary>木箱（§4.3）。</summary>
        Crate,

        /// <summary>火药桶（§4.3）。</summary>
        Barrel,

        /// <summary>锚 + 链（§4.5）。</summary>
        Anchor,

        /// <summary>散落杂物（§4.6）。</summary>
        Debris,

        /// <summary>棕榈树（§3.4）。</summary>
        Palm,

        /// <summary>灌木簇（§3.4）。</summary>
        Bush,

        /// <summary>草丛（§3.4）。</summary>
        GrassTuft,

        /// <summary>台地棱线小石块（§3.2）。</summary>
        RidgeRock,

        /// <summary>潮间带石块（§3.2）。</summary>
        IntertidalRock,

        /// <summary>功能掩体中石（§3.2）。</summary>
        CoverRock,

        /// <summary>贝壳碎屑（§3.3）。</summary>
        Shell,

        /// <summary>水下海床坡（加分项 P2）。</summary>
        SeabedMound,

        /// <summary>低模积云（加分项 P1）。</summary>
        CloudPuff,

        /// <summary>远景剪影岛（M8）。</summary>
        FarIsland,

        /// <summary>远景帆船剪影（加分项 P5）。</summary>
        FarShip,
    }

    /// <summary>一条装饰摆位（纯 C#）。</summary>
    public readonly struct PropPlacement
    {
        /// <summary>种类。</summary>
        public readonly ScenePropKind Kind;

        /// <summary>世界位置（<c>y</c> 已按该处地表贴合）。</summary>
        public readonly Vector3 Position;

        /// <summary>绕 Y 的朝向（度）。</summary>
        public readonly float YawDegrees;

        /// <summary>绕 X 的侧倾（度；用于搁浅船的姿态）。</summary>
        public readonly float RollDegrees;

        /// <summary>通用尺度（半径 / 高度倍率 / 尺寸）。</summary>
        public readonly float Scale;

        /// <summary>长度（栈桥/船体等条状物）。</summary>
        public readonly float Length;

        /// <summary>物体名义高度（供场景文档 §7.3 遮挡规则判定）。</summary>
        public readonly float Height;

        /// <summary>是否在竞技场矩形内。</summary>
        public readonly bool InsideArena;

        public PropPlacement(ScenePropKind kind, Vector3 position, float yawDegrees, float scale,
            float length, float height, bool insideArena, float rollDegrees = 0f)
        {
            Kind = kind;
            Position = position;
            YawDegrees = yawDegrees;
            Scale = scale;
            Length = length;
            Height = height;
            InsideArena = insideArena;
            RollDegrees = rollDegrees;
        }
    }

    /// <summary>一次布局的产物（纯 C#）。</summary>
    public sealed class SceneLayout
    {
        readonly List<PropPlacement> _props = new List<PropPlacement>();

        /// <summary>全部摆位（顺序 = 生成顺序，确定）。</summary>
        public IReadOnlyList<PropPlacement> Props => _props;

        /// <summary>竞技场横向尺寸（世界单位）。</summary>
        public int ArenaWidth { get; internal set; }

        /// <summary>竞技场纵深尺寸（世界单位）。</summary>
        public int ArenaDepth { get; internal set; }

        internal void Add(PropPlacement placement)
        {
            _props.Add(placement);
        }

        /// <summary>某类装饰的数量。</summary>
        public int CountOf(ScenePropKind kind)
        {
            int n = 0;
            for (int i = 0; i < _props.Count; i++)
            {
                if (_props[i].Kind == kind)
                    n++;
            }

            return n;
        }

        /// <summary>竞技场内的功能掩体中石数量（场景文档 §3.2 的 6-10 个区间）。</summary>
        public int CoverRockCount => CountOf(ScenePropKind.CoverRock);

        /// <summary>取全部摆位的三角面无关摘要（调试用）。</summary>
        public override string ToString()
        {
            return "SceneLayout props=" + _props.Count + " wreck=" + CountOf(ScenePropKind.Wreck)
                + " cover=" + CoverRockCount + " palm=" + CountOf(ScenePropKind.Palm)
                + " grass=" + CountOf(ScenePropKind.GrassTuft);
        }
    }

    /// <summary>
    /// 装饰**摆位生成**（纯 C#，无头可测）：把场景文档 §2-§4 的摆位规则翻译成确定性布局。
    ///
    /// 【确定性】同一 (网格, 出生位, 种子) 必得同一布局——用 <see cref="SceneArtRandom"/>（自实现 LCG），
    /// 不用 <c>System.Random</c>（Mono / .NET 算法不同，会让编辑器与运行时摆出不同布局）。
    ///
    /// 【全部规则走 <see cref="SceneLayoutRules"/>】本类只负责"候选点采样"，是否允许摆放由规则类裁决
    /// （高度分层、出生位净空、中景走廊），因此规则类改了，本类自动跟随。
    ///
    /// 【不含几何】只输出 <see cref="PropPlacement"/>；几何由 <see cref="ScenePropGeometry"/> 生成。
    /// </summary>
    public static class ScenePropLayout
    {
        /// <summary>目标草丛数（在 <see cref="SceneLayoutRules.MinGrassTufts"/>/Max 区间内取中低值以省三角面）。【AI 提案】</summary>
        public const int TargetGrassTufts = 700;

        /// <summary>每格草丛簇的叶片数上限（3-4 簇/格，簇内 3-5 片叶）。【AI 提案】</summary>
        const int GrassTuftsPerCell = 3;

        /// <summary>草丛与出生位的最小距离（场景文档 §3.1「离出生点 ≥2 单位」）——比通用 1.5 更严。</summary>
        public const float GrassSpawnClearance = 2f;

        /// <summary>生成一整关的装饰布局。</summary>
        /// <param name="grid">地形网格（决定地表高度与台地边缘）。</param>
        /// <param name="spawnCells">出生格（gridX, gridY）。</param>
        /// <param name="seed">随机种子（关卡号即可）。</param>
        public static SceneLayout Build(TileTerrainGrid grid, IReadOnlyList<Vector2Int> spawnCells, int seed)
        {
            var layout = new SceneLayout();
            if (grid == null)
                return layout;

            layout.ArenaWidth = grid.WidthTiles;
            layout.ArenaDepth = grid.DepthTiles;

            float arenaW = grid.WidthTiles;
            float arenaD = grid.DepthTiles;

            var rng = new SceneArtRandom(seed);
            var placedPoints = new List<Vector4>();   // (x, z, minSpacing, isHero)

            // ------------------------------------------------------------------
            // 1. 主角：搁浅断船（X19-39 / Z1-4，绕 X 侧倾 14°，转向 12°）
            // ------------------------------------------------------------------
            // 位置出处：场景文档 §4.1「横卧中脊 X19-39、Z1-4」；侧倾 12-18° 取中值 14°。
            // 转向 12° 是【AI 提案】：让"横桁（沿船宽）"与相机视线（-Z）接近垂直，
            // 读起来是一根明确的横杆（文档只要求"大致垂直相机视线"）。
            {
                float wx = 29f, wz = 2.4f;
                // 船体基准 = 中脊顶面（X23-28 / X32-37 均为 8 块 = 2.0）+ 1.4：
                // 龙骨（局部 -2.0）因此落在 y≈1.4，低于脊顶 → "半埋进沙脊"，而舷顶（局部 0）
                // 露出脊顶约 1.4 单位，45° 俯视下能读出"一条侧倾的破船"而不是"一排箱子"（§4.1）。
                // 侧倾 14° 会让左右舷再差 ±2.3·sin14° ≈ ±0.56，低舷仍压在脊顶以下，不悬空。
                float ridgeTop = SceneLayoutRules.SurfaceYAtWorld(grid, 26f, wz);
                Vector3 pos = new Vector3(wx, ridgeTop + 1.4f, wz);

                // 注：船长 12.8、转向 12°、船宽 4.6 → 世界范围约 X22.7-35.3 / Z-1.0-6.2
                //（§4.1 记为"横卧中脊 X19-39、Z1-4"）。Z 上多出的部分只是船首/船尾的角，
                // 越过 Z>4 只会遮住它**身后**（更小 Z）的远景，不会遮挡中景战场（在它更近的一侧）；
                // 且船体无碰撞体，对可玩性零影响（§7.3 规则 3 已把"中脊船体本身"列为地标例外）。
                layout.Add(new PropPlacement(ScenePropKind.Wreck, pos, 12f, 1f, 12.8f, 7.4f, true, 14f));
                placedPoints.Add(new Vector4(wx, wz, 2.6f, 2.6f));
            }

            // ------------------------------------------------------------------
            // 2. 木栈桥（场外远侧 Z≈-1.5~-4：西 X4→14、东 X46→50）
            // ------------------------------------------------------------------
            AddJetty(layout, placedPoints, 4f, 14f, -2.6f, 0.6f, arenaW, arenaD);
            AddJetty(layout, placedPoints, 46f, 50f, -2.2f, 0.6f, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 3. 旗杆（红 X≈6/Z≈0.8、蓝 X≈47/Z≈2.5，都在各自出生区一侧）
            // ------------------------------------------------------------------
            AddFlag(layout, placedPoints, grid, spawnCells, 6.5f, 0.8f, true, arenaW, arenaD);
            AddFlag(layout, placedPoints, grid, spawnCells, 47.5f, 2.5f, false, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 4. 告示牌（西滩 X≈8/Z≈2；东滩 44/Z≈12.2）
            // ------------------------------------------------------------------
            // 冲突与取舍：场景文档 §4.4 写"东滩 X≈44/Z≈13"，但 §7.3 规则 2 规定
            // 高度 0.6-1.5 的物体不得放 Z≥13 近侧带；告示牌含柱 0.8+牌 → 约 1.0 高。
            // 本文按 §7.3（可玩性/遮挡优先）把东牌移到 Z=12.2（仍在"东滩"），属**提案**。
            AddSign(layout, placedPoints, grid, spawnCells, 8f, 2f, 0.9f, arenaW, arenaD);
            AddSign(layout, placedPoints, grid, spawnCells, 44f, 12.2f, 0.9f, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 5. 锚（西滩 / 东滩 / 船尾旁）
            // ------------------------------------------------------------------
            AddAnchorAt(layout, placedPoints, grid, spawnCells, 9f, 6f, arenaW, arenaD);
            AddAnchorAt(layout, placedPoints, grid, spawnCells, 45f, 12f, arenaW, arenaD);
            AddAnchorAt(layout, placedPoints, grid, spawnCells, 37f, 3f, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 6. 木箱与火药桶（船边 + 两岸出生台地边缘）
            // ------------------------------------------------------------------
            AddCrateCluster(layout, placedPoints, grid, spawnCells, rng, 21.5f, 30f, 1.5f, 3.4f, 5, 2, arenaW, arenaD);
            AddCrateCluster(layout, placedPoints, grid, spawnCells, rng, 4.5f, 8f, 4f, 8f, 4, 2, arenaW, arenaD);
            AddCrateCluster(layout, placedPoints, grid, spawnCells, rng, 44f, 48.5f, 3f, 8f, 4, 2, arenaW, arenaD);

            AddBarrelCluster(layout, placedPoints, grid, spawnCells, rng, 30.5f, 34f, 2f, 4f, 3, arenaW, arenaD);
            AddBarrelCluster(layout, placedPoints, grid, spawnCells, rng, 45f, 49.5f, 6f, 9f, 3, arenaW, arenaD);
            AddBarrelCluster(layout, placedPoints, grid, spawnCells, rng, 4f, 7f, 9f, 12f, 3, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 7. 功能掩体中石（6-10 个，贴台地高差边、避开中央走廊）
            // ------------------------------------------------------------------
            AddCoverRocks(layout, placedPoints, grid, spawnCells, rng, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 8. 礁石：台地棱线小石块（40-80）
            // ------------------------------------------------------------------
            AddRidgeRocks(layout, placedPoints, grid, spawnCells, rng, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 9. 潮间带石块（20-35）+ 贝壳碎屑（10-20）
            // ------------------------------------------------------------------
            AddIntertidal(layout, placedPoints, grid, rng, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 10. 植被：棕榈（8-12 场内 + 场外补充）、灌木（15-30 簇）、草丛（400-1500）
            // ------------------------------------------------------------------
            AddPalms(layout, placedPoints, grid, spawnCells, rng, arenaW, arenaD);
            AddBushes(layout, placedPoints, grid, spawnCells, rng, arenaW, arenaD);
            AddGrass(layout, placedPoints, grid, spawnCells, rng, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 11. 散落杂物
            // ------------------------------------------------------------------
            AddDebris(layout, placedPoints, grid, spawnCells, rng, arenaW, arenaD);

            // ------------------------------------------------------------------
            // 12. 远景：水下海床坡 / 积云 / 剪影岛 / 帆船剪影
            // ------------------------------------------------------------------
            AddSeabedMounds(layout, placedPoints, grid, rng, arenaW, arenaD);
            AddClouds(layout, rng, arenaW, arenaD);
            AddFarIslands(layout, rng, arenaW, arenaD);
            AddFarShips(layout, rng, arenaW, arenaD);

            return layout;
        }

        // ------------------------------------------------------------------
        // 通用：摆放闸门
        // ------------------------------------------------------------------

        /// <summary>
        /// 唯一的摆放闸门：纵深分层（§7.3）+ 出生位净空（§9.5）+ 相互间距（§9「不遮挡」）。
        /// 返回 true 表示已放置。
        /// </summary>
        static bool TryPlace(SceneLayout layout, List<Vector4> placed, ScenePropKind kind,
            Vector3 position, float yaw, float scale, float length, float height,
            bool insideArena, IReadOnlyList<Vector2Int> spawnCells, float minSpacing,
            bool requireSpawnClearance = true, float roll = 0f)
        {
            if (!SceneLayoutRules.IsHeightAllowedAt(height, position.z, insideArena))
                return false;

            if (requireSpawnClearance
                && SceneLayoutRules.IsInsideSpawnClearance(position.x, position.z, spawnCells))
                return false;

            for (int i = 0; i < placed.Count; i++)
            {
                Vector4 p = placed[i];
                // z 分量存的是"该点要求的最小间距"；只要任一方要求更大间距，就取更大者。
                float need = Mathf.Max(minSpacing, p.w);
                float dx = position.x - p.x;
                float dz = position.z - p.z;
                if (dx * dx + dz * dz < need * need)
                    return false;
            }

            layout.Add(new PropPlacement(kind, position, yaw, scale, length, height, insideArena, roll));
            placed.Add(new Vector4(position.x, position.z, 0f, minSpacing));
            return true;
        }

        // ------------------------------------------------------------------
        // 各类装饰的摆放
        // ------------------------------------------------------------------

        static void AddJetty(SceneLayout layout, List<Vector4> placed,
            float xFrom, float xTo, float centerZ, float deckY, float arenaW, float arenaD)
        {
            float midX = (xFrom + xTo) * 0.5f;
            var pos = new Vector3(midX, deckY, centerZ);
            // 栈桥在场外（Z<0），高度 2.0 > 1.5 → §7.3 规则 3 的"场外"例外，允许。
            TryPlace(layout, placed, ScenePropKind.Jetty, pos, 0f, 1f, Mathf.Abs(xTo - xFrom), 2f,
                false, null, 1.2f, false);
        }

        static void AddFlag(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, float x, float z, bool redTeam,
            float arenaW, float arenaD)
        {
            float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
            var pos = new Vector3(x, surface, z);
            // 旗杆高 3.2 > 1.5 → 只允许 Z≤4（§7.3 规则 3）；红 Z=0.8 / 蓝 Z=2.5 均满足。
            TryPlace(layout, placed, ScenePropKind.FlagPole, pos, redTeam ? 0f : 180f, 1f, 0f, 3.2f,
                true, spawnCells, 1.4f, true);
        }

        static void AddSign(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, float x, float z, float height,
            float arenaW, float arenaD)
        {
            float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
            var pos = new Vector3(x, surface, z);
            TryPlace(layout, placed, ScenePropKind.SignPost, pos, 155f, 1f, 0f, height,
                true, spawnCells, 1.2f, true);
        }

        static void AddAnchorAt(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, float x, float z, float arenaW, float arenaD)
        {
            float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
            var pos = new Vector3(x, surface, z);
            TryPlace(layout, placed, ScenePropKind.Anchor, pos, 37f, 1f, 0f, 1.2f,
                true, spawnCells, 1.1f, true);
        }

        static void AddCrateCluster(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng,
            float xFrom, float xTo, float zFrom, float zTo, int target, int maxPerStack,
            float arenaW, float arenaD)
        {
            // 木箱必须走 §7.3 规则 2（高 0.6-1.3 不得进 Z≥13）与出生位净空，
            // 故用"多试几次直到放满 target"而不是"试 target 次"。
            int guard = target * 12;
            for (int i = 0; i < guard; i++)
            {
                if (CountIn(layout, ScenePropKind.Crate, zFrom, zTo) >= target)
                    break;

                float x = rng.Range(xFrom, xTo);
                float z = rng.Range(zFrom, zTo);
                float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
                float stack = rng.RangeInt(1, maxPerStack + 1);
                float height = 0.6f * stack;

                TryPlace(layout, placed, ScenePropKind.Crate, new Vector3(x, surface, z),
                    rng.Range(0f, 90f), stack, 0f, height, true, spawnCells, 0.72f, true);
            }
        }

        /// <summary>统计落在 [zMin,zMax) 区间内某类装饰的数量（用于"每簇放满 N 个"的局部目标）。</summary>
        static int CountIn(SceneLayout layout, ScenePropKind kind, float zMin, float zMax)
        {
            int n = 0;
            IReadOnlyList<PropPlacement> props = layout.Props;
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].Kind == kind && props[i].Position.z >= zMin - 0.001f && props[i].Position.z < zMax + 0.001f)
                    n++;
            }

            return n;
        }

        static void AddBarrelCluster(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng,
            float xFrom, float xTo, float zFrom, float zTo, int target, float arenaW, float arenaD)
        {
            int guard = target * 12;
            for (int i = 0; i < guard; i++)
            {
                if (CountIn(layout, ScenePropKind.Barrel, zFrom, zTo) >= target)
                    break;

                float x = rng.Range(xFrom, xTo);
                float z = rng.Range(zFrom, zTo);
                float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
                TryPlace(layout, placed, ScenePropKind.Barrel, new Vector3(x, surface, z),
                    rng.Range(0f, 90f), 1f, 0f, 0.7f, true, spawnCells, 0.7f, true);
            }
        }

        /// <summary>
        /// 功能掩体中石：只落在"台地高差边"上（场景文档 §3.2 要求与既有台地高差成对出现，
        /// 不引入新战斗变量），避开中央走廊（§7.3：中点 X 两侧各留 3 单位）。
        /// </summary>
        static void AddCoverRocks(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng, float arenaW, float arenaD)
        {
            var candidates = new List<Vector2Int>();
            float centerX = arenaW * 0.5f;

            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    if (grid.BlocksAt(gx, gy) <= 0)
                        continue;

                    float z = gy + 0.5f;
                    // 只在中景/远景区，且不落在中央走廊。
                    if (z >= SceneLayoutRules.NearBandMinZ)
                        continue;

                    float x = gx + 0.5f;
                    if (Mathf.Abs(x - centerX) < SceneLayoutRules.CenterCorridorHalfWidth)
                        continue;

                    if (!HasLowerNeighbor(grid, gx, gy))
                        continue;

                    candidates.Add(new Vector2Int(gx, gy));
                }
            }

            if (candidates.Count == 0)
                return;

            // 确定性打乱，然后按 6-10 个取用。
            Shuffle(candidates, rng);

            int target = rng.RangeInt(SceneLayoutRules.MinCoverRocks, SceneLayoutRules.MaxCoverRocks + 1);
            int placedCount = 0;
            for (int i = 0; i < candidates.Count && placedCount < target; i++)
            {
                Vector2Int cell = candidates[i];
                float x = cell.x + 0.5f + rng.Range(-0.22f, 0.22f);
                float z = cell.y + 0.5f + rng.Range(-0.22f, 0.22f);
                float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
                float radius = rng.Range(SceneLayoutRules.CoverRockMinRadius, SceneLayoutRules.CoverRockMaxRadius);

                if (TryPlace(layout, placed, ScenePropKind.CoverRock, new Vector3(x, surface, z),
                        rng.Range(0f, 360f), radius, 0f, radius * 0.95f,
                        true, spawnCells, 1.6f))
                {
                    placedCount++;
                }
            }
        }

        static void AddRidgeRocks(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng, float arenaW, float arenaD)
        {
            var candidates = new List<Vector2Int>();
            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    if (grid.BlocksAt(gx, gy) <= 0)
                        continue;
                    if (!HasLowerNeighbor(grid, gx, gy))
                        continue;
                    candidates.Add(new Vector2Int(gx, gy));
                }
            }

            if (candidates.Count == 0)
                return;

            Shuffle(candidates, rng);
            int target = rng.RangeInt(SceneLayoutRules.MinRidgeRocks, SceneLayoutRules.MaxRidgeRocks + 1);
            int placedCount = 0;
            for (int i = 0; i < candidates.Count * 2 && placedCount < target; i++)
            {
                Vector2Int cell = candidates[i % candidates.Count];
                // 沿格边落石：贴到 4 边之一，打断台地棱线的直线。
                int edge = rng.RangeInt(0, 4);
                float ox = edge == 0 ? -0.36f : edge == 1 ? 0.36f : rng.Range(-0.3f, 0.3f);
                float oz = edge == 2 ? -0.36f : edge == 3 ? 0.36f : rng.Range(-0.3f, 0.3f);
                float x = Mathf.Clamp(cell.x + 0.5f + ox, 0.15f, arenaW - 0.15f);
                float z = Mathf.Clamp(cell.y + 0.5f + oz, 0.15f, arenaD - 0.15f);
                float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
                float radius = rng.Range(SceneLayoutRules.RidgeRockMinRadius, SceneLayoutRules.RidgeRockMaxRadius);

                // 小石块 ≤0.6 高 → 任意纵深可放（§7.3 规则 1）；但 §9.5 仍要求离出生格心 ≥1.5。
                if (TryPlace(layout, placed, ScenePropKind.RidgeRock, new Vector3(x, surface, z),
                        rng.Range(0f, 360f), radius, 0f, radius * 1.6f, true, spawnCells, 0.5f, true))
                {
                    placedCount++;
                }
            }
        }

        static void AddIntertidal(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            SceneArtRandom rng, float arenaW, float arenaD)
        {
            int rockTarget = rng.RangeInt(SceneLayoutRules.MinIntertidalRocks, SceneLayoutRules.MaxIntertidalRocks + 1);
            int shellTarget = rng.RangeInt(10, 21);

            for (int i = 0; i < rockTarget * 3 && layout.CountOf(ScenePropKind.IntertidalRock) < rockTarget; i++)
            {
                Vector3 p = RandomShorePoint(rng, arenaW, arenaD, 0.6f, 2.0f);
                float radius = rng.Range(SceneLayoutRules.IntertidalRockMinRadius, SceneLayoutRules.IntertidalRockMaxRadius);
                // 潮间带坡面高度：offset 0.6-2.0 对应 y≈-0.14~-0.48（坡从 y=0 到 y=-0.6、宽 2.5）。
                TryPlace(layout, placed, ScenePropKind.IntertidalRock, p,
                    rng.Range(0f, 360f), radius, 0f, radius * 1.6f, false, null, 0.5f, false);
            }

            for (int i = 0; i < shellTarget * 3 && layout.CountOf(ScenePropKind.Shell) < shellTarget; i++)
            {
                Vector3 p = RandomShorePoint(rng, arenaW, arenaD, 0.3f, 1.8f);
                p.y += 0.012f;
                TryPlace(layout, placed, ScenePropKind.Shell, p, 0f, rng.Range(0.05f, 0.12f), 0f, 0.05f,
                    false, null, 0.3f, false);
            }
        }

        static void AddPalms(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng, float arenaW, float arenaD)
        {
            // 场内只在远景带 Z≤3.2（§3.4「只放 Z≤3 或 Z≥15 远近带与场外」）。
            //
            // ⚠ 冲突与取舍（**提案**）：§3.4 允许 Z≥15 近侧带放棕榈，但 §7.3 规则 3 规定
            //   高度 >1.5 的物体只允许 Z≤4 或竞技场外——近侧带的高物会在 45° 相机下遮住它
            //   "身后"（即 -Z 方向的战场），正是 §7.3 要避免的。本文按 §7.3 执行：**棕榈不进近侧带**。
            int target = rng.RangeInt(SceneLayoutRules.MinPalms, SceneLayoutRules.MaxPalms + 1);
            var candidates = new List<Vector2Int>();

            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    float z = gy + 0.5f;
                    if (z > 3.4f)
                        continue;
                    if (grid.BlocksAt(gx, gy) < 2)
                        continue;
                    // 不抢主角：船体 X19-39 让开。
                    if (gx >= 18 && gx <= 39)
                        continue;
                    candidates.Add(new Vector2Int(gx, gy));
                }
            }

            Shuffle(candidates, rng);
            int placedCount = 0;
            for (int i = 0; i < candidates.Count && placedCount < target; i++)
            {
                Vector2Int cell = candidates[i];
                float x = cell.x + 0.5f + rng.Range(-0.28f, 0.28f);
                float z = cell.y + 0.5f + rng.Range(-0.28f, 0.28f);
                float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
                float trunk = rng.Range(SceneLayoutRules.PalmMinTrunkHeight, SceneLayoutRules.PalmMaxTrunkHeight);

                if (TryPlace(layout, placed, ScenePropKind.Palm, new Vector3(x, surface, z),
                        rng.Range(0f, 360f), trunk, 0f, trunk + 0.4f, true, spawnCells, 1.5f))
                {
                    placedCount++;
                }
            }

            // 场外补充（§7.3 规则 3 的"竞技场外"例外）：远侧、西端、东端各一批，
            // 既是"这里也有人烟"的线索，也保证 M11 的"棕榈 ≥8"在候选不足时仍能达标。
            int cap = SceneLayoutRules.MaxPalms + 4;
            int attempts = 0;
            while (layout.CountOf(ScenePropKind.Palm) < Mathf.Max(target, SceneLayoutRules.MinPalms)
                && attempts < 200)
            {
                attempts++;
                int side = attempts % 3;
                float x, z;
                switch (side)
                {
                    case 0:
                        x = rng.Range(2f, arenaW - 2f);
                        z = rng.Range(-6f, -2.2f);
                        break;
                    case 1:
                        x = rng.Range(-6f, -1.5f);
                        z = rng.Range(0.5f, 4f);
                        break;
                    default:
                        x = rng.Range(arenaW + 1.5f, arenaW + 6f);
                        z = rng.Range(0.5f, 4f);
                        break;
                }

                float trunk = rng.Range(SceneLayoutRules.PalmMinTrunkHeight, SceneLayoutRules.PalmMaxTrunkHeight);
                float y = z < 0f ? -0.55f : SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
                TryPlace(layout, placed, ScenePropKind.Palm, new Vector3(x, y, z),
                    rng.Range(0f, 360f), trunk, 0f, trunk + 0.4f, false, null, 1.5f, false);

                if (layout.CountOf(ScenePropKind.Palm) >= cap)
                    break;
            }
        }

        static void AddBushes(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng, float arenaW, float arenaD)
        {
            int target = rng.RangeInt(SceneLayoutRules.MinBushClusters, SceneLayoutRules.MaxBushClusters + 1);
            var candidates = new List<Vector2Int>();

            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    // 台顶与岩缝：需要有抬升块。
                    if (grid.BlocksAt(gx, gy) < 3)
                        continue;
                    candidates.Add(new Vector2Int(gx, gy));
                }
            }

            Shuffle(candidates, rng);
            int placedCount = 0;
            for (int i = 0; i < candidates.Count && placedCount < target; i++)
            {
                Vector2Int cell = candidates[i];
                float x = cell.x + 0.5f + rng.Range(-0.3f, 0.3f);
                float z = cell.y + 0.5f + rng.Range(-0.3f, 0.3f);
                float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);
                float scale = rng.Range(0.7f, 1.15f);
                bool inside = SceneLayoutRules.FitsInsideArena(x, z, 0.4f * scale, arenaW, arenaD);

                if (TryPlace(layout, placed, ScenePropKind.Bush, new Vector3(x, surface, z),
                        rng.Range(0f, 360f), scale, 0f, 0.9f * scale, inside, spawnCells, 1.0f))
                {
                    placedCount++;
                }
            }
        }

        static void AddGrass(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng, float arenaW, float arenaD)
        {
            var candidates = new List<Vector2Int>();
            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    // §3.1：草只铺在 y≥1.5 的平顶与岩石腰部（≥6 块 = 1.5 单位）。
                    if (grid.BlocksAt(gx, gy) < 6)
                        continue;
                    candidates.Add(new Vector2Int(gx, gy));
                }
            }

            Shuffle(candidates, rng);
            int target = Mathf.Clamp(TargetGrassTufts, SceneLayoutRules.MinGrassTufts, SceneLayoutRules.MaxGrassTufts);

            for (int i = 0; i < candidates.Count && layout.CountOf(ScenePropKind.GrassTuft) < target; i++)
            {
                Vector2Int cell = candidates[i];
                for (int k = 0; k < GrassTuftsPerCell; k++)
                {
                    float x = cell.x + rng.Range(0.18f, 0.82f);
                    float z = cell.y + rng.Range(0.18f, 0.82f);
                    float surface = SceneLayoutRules.SurfaceYAtWorld(grid, x, z);

                    // 草高 ≤0.4 → 任意纵深可放；但场景文档 §3.1 额外要求离出生点 ≥2 单位。
                    if (!IsBeyondGrassClearance(x, z, spawnCells))
                        continue;

                    if (TryPlace(layout, placed, ScenePropKind.GrassTuft, new Vector3(x, surface, z),
                            0f, rng.Range(0.8f, 1.2f), 0f, 0.4f, true, null, 0.22f, false))
                    {
                        // 到量即停。
                        if (layout.CountOf(ScenePropKind.GrassTuft) >= target)
                            break;
                    }
                }
            }
        }

        /// <summary>草丛专用的更宽出生净空（2 单位，比通用的 1.5 更严）。</summary>
        static bool IsBeyondGrassClearance(float x, float z, IReadOnlyList<Vector2Int> spawnCells)
        {
            if (spawnCells == null)
                return true;

            for (int i = 0; i < spawnCells.Count; i++)
            {
                float dx = x - (spawnCells[i].x + 0.5f);
                float dz = z - (spawnCells[i].y + 0.5f);
                if (dx * dx + dz * dz < GrassSpawnClearance * GrassSpawnClearance)
                    return false;
            }

            return true;
        }

        static void AddDebris(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            IReadOnlyList<Vector2Int> spawnCells, SceneArtRandom rng, float arenaW, float arenaD)
        {
            int target = rng.RangeInt(12, 28);
            for (int i = 0; i < target * 3 && layout.CountOf(ScenePropKind.Debris) < target; i++)
            {
                bool shore = rng.Chance(0.5f);
                Vector3 pos;
                bool inside;

                if (shore)
                {
                    // 水缘杂物：直接用潮间带坡面的高度（不能用网格地表——竞技场外没有台地）。
                    pos = RandomShorePoint(rng, arenaW, arenaD, 0.2f, 1.6f);
                    inside = false;
                }
                else
                {
                    float x = rng.Range(0.5f, arenaW - 0.5f);
                    float z = rng.Range(0.5f, arenaD - 0.5f);
                    pos = new Vector3(x, SceneLayoutRules.SurfaceYAtWorld(grid, x, z), z);
                    inside = SceneLayoutRules.FitsInsideArena(x, z, 0.3f, arenaW, arenaD);
                }

                TryPlace(layout, placed, ScenePropKind.Debris, pos,
                    rng.Range(0f, 360f), 1f, 0f, 0.08f, inside, spawnCells, 0.55f, true);
            }
        }

        static void AddSeabedMounds(SceneLayout layout, List<Vector4> placed, TileTerrainGrid grid,
            SceneArtRandom rng, float arenaW, float arenaD)
        {
            int target = rng.RangeInt(4, 9);
            for (int i = 0; i < target * 3 && layout.CountOf(ScenePropKind.SeabedMound) < target; i++)
            {
                Vector3 p = RandomShorePoint(rng, arenaW, arenaD, 3.2f, 9f);
                float radius = rng.Range(1.5f, 3f);
                TryPlace(layout, placed, ScenePropKind.SeabedMound, p, rng.Range(0f, 360f), radius, 0f, 0.3f,
                    false, null, 2.2f, false);
            }
        }

        static void AddClouds(SceneLayout layout, SceneArtRandom rng, float arenaW, float arenaD)
        {
            int target = rng.RangeInt(8, 21);
            for (int i = 0; i < target; i++)
            {
                float x = rng.Range(-30f, arenaW + 30f);
                float z = rng.Range(-58f, -12f);
                float y = rng.Range(12f, 20f);
                float radius = rng.Range(2.2f, 5.5f);
                TryPlace(layout, new List<Vector4>(), ScenePropKind.CloudPuff, new Vector3(x, y, z),
                        rng.Range(0f, 360f), radius, 0f, 0f, false, null, 0f, false);
            }
        }

        static void AddFarIslands(SceneLayout layout, SceneArtRandom rng, float arenaW, float arenaD)
        {
            int target = rng.RangeInt(3, 6);
            for (int i = 0; i < target; i++)
            {
                float x = rng.Range(-25f, arenaW + 25f);
                float z = rng.Range(-70f, -38f);
                float width = rng.Range(18f, 55f);
                float height = rng.Range(8f, 24f);
                TryPlace(layout, new List<Vector4>(), ScenePropKind.FarIsland, new Vector3(x, 0f, z),
                        rng.Range(0f, 360f), width, 0f, height, false, null, 0f, false);
            }
        }

        static void AddFarShips(SceneLayout layout, SceneArtRandom rng, float arenaW, float arenaD)
        {
            int target = rng.RangeInt(1, 3);
            for (int i = 0; i < target; i++)
            {
                float x = rng.Range(0f, arenaW);
                float z = rng.Range(-42f, -26f);
                float scale = rng.Range(0.8f, 1.3f);
                TryPlace(layout, new List<Vector4>(), ScenePropKind.FarShip, new Vector3(x, 0f, z),
                        rng.Range(-20f, 20f), scale, 0f, 6f * scale, false, null, 0f, false);
            }
        }

        // ------------------------------------------------------------------
        // 采样辅助
        // ------------------------------------------------------------------

        /// <summary>在竞技场矩形外 <paramref name="outer"/> 的环形带上取一个点。</summary>
        static Vector3 RandomShorePoint(SceneArtRandom rng, float arenaW, float arenaD,
            float innerOffset, float outerOffset)
        {
            float offset = rng.Range(innerOffset, outerOffset);
            float sign = rng.Chance(0.5f) ? -1f : 1f;
            bool alongX = rng.Chance(0.5f);

            float x, z;
            if (alongX)
            {
                x = rng.Range(-offset, arenaW + offset);
                z = sign < 0 ? -offset : arenaD + offset;
            }
            else
            {
                z = rng.Range(-offset, arenaD + offset);
                x = sign < 0 ? -offset : arenaW + offset;
            }

            // 潮间带坡面高度：offset 0→y=0；offset 2.5→y=-0.6（与 IslandShellGeometry 的湿沙坡一致）。
            float slopeY = -0.24f * offset;
            return new Vector3(x, Mathf.Max(-0.6f, slopeY), z);
        }

        /// <summary>该格是否有更低（或场外）的 4 邻 → 它是一条"台地棱线"。</summary>
        static bool HasLowerNeighbor(TileTerrainGrid grid, int gx, int gy)
        {
            int blocks = grid.BlocksAt(gx, gy);
            for (int d = 0; d < 4; d++)
            {
                int nx = gx + (d == 0 ? -1 : d == 1 ? 1 : 0);
                int ny = gy + (d == 2 ? -1 : d == 3 ? 1 : 0);

                if (nx < 0 || ny < 0 || nx >= grid.WidthTiles || ny >= grid.DepthTiles)
                    return true;   // 竞技场边界即棱线

                if (grid.BlocksAt(nx, ny) < blocks)
                    return true;
            }

            return false;
        }

        /// <summary>确定性 Fisher-Yates 洗牌（用自实现 LCG，保证编辑器/运行时/测试一致）。</summary>
        static void Shuffle<T>(List<T> list, SceneArtRandom rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.RangeInt(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
