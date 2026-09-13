using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>平台簇的伪装类型（决定视觉底部与 kit 配方）。</summary>
    public enum PlatformClusterKind
    {
        /// <summary>无簇（水格 / 旧版列式地形）。</summary>
        None = 0,

        /// <summary>大船簇：底部 = 船体侧板 + 龙骨，甲板可走。</summary>
        Ship = 1,

        /// <summary>空岛簇：底部 = 岩锥收尖下垂（可挂垂藤/钟乳）。</summary>
        SkyIsland = 2,

        /// <summary>梯田小岛簇：底部 = 岩层，顶部多级台阶。</summary>
        TerraceIsland = 3,
    }

    /// <summary>单格平台表面的语义档（供小地图 / 视图选形）。</summary>
    public enum PlatformSurface : byte
    {
        /// <summary>旧版列式地形（有基础地面、无平台语义）。</summary>
        LegacyGround = 0,

        /// <summary>水（无地面，掉落即死）。</summary>
        Water = 1,

        Ship = 2,
        SkyIsland = 3,
        TerraceIsland = 4,
    }

    /// <summary>一个平台簇的只读描述（纯 C#）。</summary>
    public readonly struct PlatformClusterInfo
    {
        /// <summary>簇名（报告/测试可读）。</summary>
        public readonly string Name;

        /// <summary>伪装类型。</summary>
        public readonly PlatformClusterKind Kind;

        /// <summary>地面格包络（含边界，俯视矩形）。</summary>
        public readonly int X0, Z0, X1, Z1;

        /// <summary>簇内最低/最高平台高度（块）。</summary>
        public readonly int MinBlocks, MaxBlocks;

        public PlatformClusterInfo(string name, PlatformClusterKind kind, int x0, int z0, int x1, int z1,
            int minBlocks, int maxBlocks)
        {
            Name = name;
            Kind = kind;
            X0 = x0;
            Z0 = z0;
            X1 = x1;
            Z1 = z1;
            MinBlocks = minBlocks;
            MaxBlocks = maxBlocks;
        }

        /// <summary>格是否落在本簇包络内（含边界）。</summary>
        public bool Contains(int gx, int gz) => gx >= X0 && gx <= X1 && gz >= Z0 && gz <= Z1;
    }

    /// <summary>
    /// 平台簇地图（纯 C# 数据）：每格是「水」还是「某簇的地面」，地面格带显式块高与簇归属。
    ///
    /// 【为什么不用「每列一个高度」】旧版把地形写成 <c>columnBlocks[gridX]</c>（同一列沿 Z 同高），
    /// 那只能表达"一整块连续地面 + 中脊"，无法表达"一堆高高低低、彼此隔水的悬空平台"。
    /// 本结构改成**逐格**（行主序）的 <see cref="CellBlocks"/> + <see cref="CellCluster"/>，
    /// 水格 <c>CellCluster = -1</c>、<c>CellBlocks = 0</c>。
    /// </summary>
    public sealed class PlatformMap
    {
        /// <summary>横向格数。</summary>
        public readonly int WidthTiles;

        /// <summary>纵深格数。</summary>
        public readonly int DepthTiles;

        /// <summary>逐格块高（行主序）；0 = 水。</summary>
        public readonly int[] CellBlocks;

        /// <summary>逐簇归属（行主序）；-1 = 水。</summary>
        public readonly int[] CellCluster;

        /// <summary>全部平台簇。</summary>
        public readonly IReadOnlyList<PlatformClusterInfo> Clusters;

        public PlatformMap(int widthTiles, int depthTiles, int[] cellBlocks, int[] cellCluster,
            IReadOnlyList<PlatformClusterInfo> clusters)
        {
            WidthTiles = widthTiles;
            DepthTiles = depthTiles;
            CellBlocks = cellBlocks;
            CellCluster = cellCluster;
            Clusters = clusters;
        }

        /// <summary>行主序索引。</summary>
        public int IndexOf(int gx, int gz) => gx + gz * WidthTiles;

        /// <summary>该格是否是地面（非水）。</summary>
        public bool IsGround(int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= WidthTiles || gz >= DepthTiles)
                return false;
            return CellCluster[IndexOf(gx, gz)] >= 0;
        }

        /// <summary>该格的块高（水 / 越界 = 0）。</summary>
        public int BlocksAt(int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= WidthTiles || gz >= DepthTiles)
                return 0;
            return CellBlocks[IndexOf(gx, gz)];
        }

        /// <summary>地面格数量。</summary>
        public int GroundCellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < CellCluster.Length; i++)
                {
                    if (CellCluster[i] >= 0)
                        n++;
                }
                return n;
            }
        }

        /// <summary>水格数量。</summary>
        public int WaterCellCount => CellBlocks.Length - GroundCellCount;
    }

    /// <summary>
    /// level_1 的**平台簇布局**（纯 C# 数据 + 生成，无头可测）。
    ///
    /// 【提案/待定】本布局是 AI 按用户诉求（"一堆高高低低的悬空平台浮在海面上，平台间是水，
    /// 场景美术把平台伪装成大船 / 空岛 / 梯田小岛"）给出的第一版，**未经用户确认**：
    ///   · 簇数量 4（3 主簇 + 1 小空岛）落在用户建议的 3-4 簇区间内；
    ///   · 水距 2-4 格，远小于角色最大投掷射程（约 11.7 单位，见 <see cref="MaxThrowRangeWorld"/>），
    ///     故任一簇都能被投掷跨越（不会出现"打不到的孤岛"）；
    ///   · 所有 8 个出生位（5 红 3 蓝）都落在各自簇的地面格上，高度 ≥1 块，绝不初始落水；
    ///   · ②↔③ 水距 4 格（③ 西界 x43）、①↔② 4 格、②↔④ 2 格（④ 在 ② 正北 x32-35 z0-1），
    ///     地面 411 / 850 格 = 48.4%（校正值见 §5.3；据此重算的断言在 PlatformTerrainTests）。
    ///
    /// 【与旧列的差别】旧 <see cref="TerrainCatalog"/> 的 level_1 是"整块地面 + 中脊"（0 块 = 基础地面，
    /// 永不挖洞）；本布局是**逐格水陆**，水格没有地面，单位走上去会掉到水面以下（落水即死）。
    ///
    /// 【簇布局图（X 横 0-49，Z 纵 0-16，数字 = 块高，. = 水）】
    /// <code>
    /// Z\X  0    5    10   15   20   25   30   35   40   45   49
    ///  0   ....  .................... 3333 .........  ......
    ///  2   .... ...11111...........  .......  .2222222  ← 空岛 C（蓝出生）
    ///  4   .... ..1111111111... ....... ..22222...
    ///  6   .... ..1112222111.. .22222.. ..22222...
    ///  8   .... ..1113333111.. .22555.. ..2244222...
    /// 10   .... ..1112222111.. .22222.. ..22222...
    /// 12   .... ..1111111111.. .22222.. ..22222...
    /// 14   .... ....................  .........  ......
    /// </code>
    /// （上图为示意，精确数据见 <see cref="Level1Clusters"/> 的 Step 定义。）
    /// </summary>
    public static class PlatformClusterLayout
    {
        /// <summary>单块的竖直步进（= 8px = 0.25 单位，沿用 <see cref="TerrainCatalog.DefaultBlockWorldHeight"/>）。</summary>
        public const int MaxBlocksPerCluster = 5;

        sealed class Step
        {
            public readonly int X0, Z0, X1, Z1, Blocks;

            public Step(int x0, int z0, int x1, int z1, int blocks)
            {
                X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Blocks = blocks;
            }
        }

        sealed class ClusterDef
        {
            public readonly string Name;
            public readonly PlatformClusterKind Kind;
            public readonly Step[] Steps;

            public ClusterDef(string name, PlatformClusterKind kind, Step[] steps)
            {
                Name = name; Kind = kind; Steps = steps;
            }
        }

        // ------------------------------------------------------------------
        // 平台簇定义（level_1，50×17）。后列出的 Step 覆盖先列出的（用于叠台阶）。
        // ------------------------------------------------------------------
        static readonly ClusterDef[] Level1Defs =
        {
            // ① 梯田小岛簇（红队出生区，西侧）：三级台阶递升，顶层放高台掩体。
            new ClusterDef("terrace_island_west", PlatformClusterKind.TerraceIsland, new[]
            {
                new Step( 4,  3, 19, 13, 1),   // 外环台阶
                new Step( 6,  5, 17, 11, 2),   // 中环台阶
                new Step( 9,  6, 13, 10, 3),   // 顶层
            }),

            // ② 大船簇（战场主簇 / 中立，中央）：外围浅礁裙 + 长条甲板 + 船头高台 + 桅盘。
            // z 包络取 4-13（比第一版 5-12 各外扩 1 行），使 ④ 北岛落到"水距 2 格"、
            // 且与 ③ 的主水道稳定为 4 格；依据 docs/关卡设计语言-参照游戏全场景分析.md §5.3/§5.4。
            new ClusterDef("great_ship_center", PlatformClusterKind.Ship, new[]
            {
                new Step(24,  4, 38, 13, 1),   // 外围浅礁裙（1 块；只决定簇包络，甲板叠在其上）
                new Step(24,  5, 38, 12, 2),   // 主甲板（长条形，高 2 块起）
                new Step(34,  5, 38, 12, 4),   // 船头高台
                new Step(28,  7, 31, 10, 5),   // 桅盘（掩体高台）
            }),

            // ③ 空岛簇（蓝队出生区，东侧）：基底 + 岩峰，高差 2 块。
            // 西界取 x43（第一版 x42 → x43）：②↔③ 水距由 3 格校正为 4 格（§5.4）。
            new ClusterDef("sky_island_east", PlatformClusterKind.SkyIsland, new[]
            {
                new Step(43,  2, 49, 12, 2),   // 岛基
                new Step(45,  4, 48, 10, 4),   // 岩峰
            }),

            // ④ 小空岛（中立跳板，中央簇正北）：第一版在 x36-39（压在 ②/③ 之间、会切主水道），
            // 现移到 ② 正北 x32-35 z0-1 —— 与 ② 水距 2 格，且不切断 ②↔③ 的 4 格主水道（§5.4）。
            new ClusterDef("sky_islet_north", PlatformClusterKind.SkyIsland, new[]
            {
                new Step(32,  0, 35,  1, 3),
            }),
        };

        /// <summary>level_1 的平台簇描述（供测试/报告直接读）。</summary>
        public static IReadOnlyList<PlatformClusterInfo> Level1Clusters => BuildLevel1().Clusters;

        /// <summary>
        /// 生成 level_1 的平台簇地图（确定性：同一定义必得同一地图）。
        /// </summary>
        public static PlatformMap BuildLevel1()
        {
            int w = 50, d = 17;
            var blocks = new int[w * d];
            var cellCluster = new int[w * d];
            for (int i = 0; i < cellCluster.Length; i++)
                cellCluster[i] = -1;

            var infos = new List<PlatformClusterInfo>(Level1Defs.Length);

            for (int c = 0; c < Level1Defs.Length; c++)
            {
                ClusterDef def = Level1Defs[c];
                int minB = int.MaxValue, maxB = int.MinValue;
                int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;

                for (int s = 0; s < def.Steps.Length; s++)
                {
                    Step step = def.Steps[s];
                    for (int gz = step.Z0; gz <= step.Z1; gz++)
                    {
                        for (int gx = step.X0; gx <= step.X1; gx++)
                        {
                            if (gx < 0 || gz < 0 || gx >= w || gz >= d)
                                continue;

                            int idx = gx + gz * w;
                            blocks[idx] = step.Blocks;
                            cellCluster[idx] = c;

                            minB = Mathf.Min(minB, step.Blocks);
                            maxB = Mathf.Max(maxB, step.Blocks);
                            x0 = Mathf.Min(x0, gx); x1 = Mathf.Max(x1, gx);
                            z0 = Mathf.Min(z0, gz); z1 = Mathf.Max(z1, gz);
                        }
                    }
                }

                if (minB == int.MaxValue)
                    minB = 0;

                infos.Add(new PlatformClusterInfo(def.Name, def.Kind, x0, z0, x1, z1, minB, maxB));
            }

            return new PlatformMap(w, d, blocks, cellCluster, infos);
        }

        /// <summary>
        /// 角色最大投掷射程（世界单位）的解析估计：由满力 twangMax、固定抬升与重力算出平抛射程。
        /// <c>R = v_h · (2·v_v / g)</c>，其中 <c>v_h = v/√(1+lift²)</c>、<c>v_v = v_h·lift</c>。
        /// 用角色默认 weight=1、twangMax=20（<see cref="CrewCatalog"/>），得约 11.7 单位。
        /// 【AI 提案】仅用于"簇连通性"判定（水距远小于它即视为投掷可跨），不参与弹道计算。
        /// </summary>
        public static float MaxThrowRangeWorld
        {
            get
            {
                float speed = CrewCatalog.TwangMaxForce * LevelGeometry.FlashSpeedScale;
                float lift = LevelGeometry.ThrowLift;
                float horiz = speed / Mathf.Sqrt(1f + lift * lift);
                float vert = horiz * lift;
                float g = -LevelGeometry.WorldGravityY(CrewCatalog.Weight);
                if (g <= 1e-6f)
                    return 0f;
                return horiz * (2f * vert / g);
            }
        }

        /// <summary>
        /// 两簇**最近可站格**之间的世界距离（格宽 1）：沿 X/Z 各自算边到边的距离再取欧氏合成。
        /// 用于"投掷可跨"的连通性判定（比单纯水格数更贴近真实投掷距离）。
        /// </summary>
        public static float MinEdgeDistanceWorld(in PlatformClusterInfo a, in PlatformClusterInfo b)
        {
            int gapX = Mathf.Max(a.X0 - b.X1 - 1, b.X0 - a.X1 - 1);
            int gapZ = Mathf.Max(a.Z0 - b.Z1 - 1, b.Z0 - a.Z1 - 1);

            float dx = gapX >= 0 ? gapX + 1f : 0f;
            float dz = gapZ >= 0 ? gapZ + 1f : 0f;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 两簇之间的**水格数**（沿分离轴）。包络在 X/Z 上都重叠时返回 0（视为相接）。
        /// 用于测试"水距 ≥2 格"与"投掷可跨"。
        /// </summary>
        public static int WaterGapTiles(in PlatformClusterInfo a, in PlatformClusterInfo b)
        {
            int gapX = Mathf.Max(a.X0 - b.X1 - 1, b.X0 - a.X1 - 1);
            int gapZ = Mathf.Max(a.Z0 - b.Z1 - 1, b.Z0 - a.Z1 - 1);

            // 取"分离最明显"的轴：若某轴 gap>=0 则该轴已分离；否则重叠（gap<0）。
            int gap = Mathf.Max(gapX, gapZ);
            return gap < 0 ? 0 : gap;
        }
    }
}
