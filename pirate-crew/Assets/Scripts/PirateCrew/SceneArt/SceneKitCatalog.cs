using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 模块化构件（kit）的种类。行业 kit 做法的核心是**构件可复用、可换皮、可确定性重建**：
    /// 每类构件独立网格、独立材质，场景由"配方（recipe）+ 摆放表"拼出来，而不是一锅炖大合并网格。
    /// </summary>
    public enum SceneKitPiece
    {
        /// <summary>船艏构件（尖头侧板）。</summary>
        HullBow,

        /// <summary>船艏与船艉之间的船体段（平直侧板）。</summary>
        HullMid,

        /// <summary>船艉构件（方尾侧板）。</summary>
        HullStern,

        /// <summary>甲板木板（铺在平台顶面）。</summary>
        DeckPlank,

        /// <summary>舷墙 / 栏杆。</summary>
        Bulwark,

        /// <summary>桅杆（含横桁）。</summary>
        Mast,

        /// <summary>索具（缆绳）。</summary>
        Rigging,

        /// <summary>帆布。</summary>
        Sail,

        /// <summary>岛顶岩台（空岛 / 梯田顶的收边岩帽）。</summary>
        IslandTop,

        /// <summary>散落岩块（棱线 / 礁石）。</summary>
        RockChunk,

        /// <summary>通用陈设（箱 / 桶 / 锚 / 棕榈等，复用 <see cref="ScenePropGeometry"/>）。</summary>
        Prop,

        /// <summary>横桁：挂帆的横向木杆（沿船宽方向；帆挂在桁下）。</summary>
        Yard,

        /// <summary>桅顶瞭望巢（乌鸦巢）：桅顶的木板 + 一圈矮栏木斗。</summary>
        CrowNest,

        /// <summary>船艏装饰：艏柱横档 + 从艏端斜向前上方伸出的艏斜桁。</summary>
        BowDeco,

        /// <summary>放样船体（整艘，替代 HullBow/Mid/Stern 三件盒子套）：Scale=船宽、Length=总长、Height=吃深。
        /// 几何见 <see cref="ShipHullGeometry"/>（用户裁决 2026-09-14"真船建模"）。</summary>
        ShipHullLoft,
    }

    /// <summary>构件所属材质组（= 构建期合批目标）。</summary>
    public enum SceneKitMaterial
    {
        Wood,
        WoodDark,
        Rock,
        Metal,
        Cloth,
        Foliage,
    }

    /// <summary>一条构件摆放（纯 C#）：构件种类 + 材质组 + 世界 Transform + 尺寸。</summary>
    public readonly struct KitPart
    {
        public readonly SceneKitPiece Piece;
        public readonly SceneKitMaterial Material;

        /// <summary>世界位置（<c>y</c> 已贴合所在平台顶面）。</summary>
        public readonly Vector3 Position;

        /// <summary>绕 Y 朝向（度）。</summary>
        public readonly float YawDegrees;

        /// <summary>通用尺度（半径 / 高度倍率）。</summary>
        public readonly float Scale;

        /// <summary>长度（船体段 / 甲板板 / 桅高等条状量）。</summary>
        public readonly float Length;

        /// <summary>高度（舷墙高 / 桅高）。</summary>
        public readonly float Height;

        /// <summary><see cref="SceneKitPiece.Prop"/> 专用：复用哪一类道具几何。</summary>
        public readonly ScenePropKind PropKind;

        /// <summary>
        /// 该构件来自哪个**平台簇**（<c>-1</c> = 未标注，如直接调 <see cref="BuildShip"/> 的裸配方用法）。
        /// 有了它，"每簇的材质族纯净性"这类断言可以精确定位到簇，不必靠包围盒猜
        /// （船体段可能伸到包络之外、相邻簇的包围盒也可能互相覆盖）。
        /// </summary>
        public readonly int ClusterIndex;

        public KitPart(SceneKitPiece piece, SceneKitMaterial material, Vector3 position, float yawDegrees,
            float scale, float length, float height, ScenePropKind propKind = default, int clusterIndex = -1)
        {
            Piece = piece;
            Material = material;
            Position = position;
            YawDegrees = yawDegrees;
            Scale = scale;
            Length = length;
            Height = height;
            PropKind = propKind;
            ClusterIndex = clusterIndex;
        }

        /// <summary>标注该构件所属的平台簇（返回副本；本结构不可变）。</summary>
        public KitPart WithCluster(int clusterIndex)
        {
            return new KitPart(Piece, Material, Position, YawDegrees, Scale, Length, Height, PropKind, clusterIndex);
        }
    }

    /// <summary>一套构件摆放表（纯 C#，确定性）。</summary>
    public sealed class SceneKitLayout
    {
        readonly List<KitPart> _parts = new List<KitPart>();

        /// <summary>全部构件（顺序 = 生成顺序，确定）。</summary>
        public IReadOnlyList<KitPart> Parts => _parts;

        /// <summary>追加一条构件（供 catalog / 测试组装摆放表）。</summary>
        public void Add(KitPart part) => _parts.Add(part);

        /// <summary>某类构件数量。</summary>
        public int CountOf(SceneKitPiece piece)
        {
            int n = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Piece == piece)
                    n++;
            }
            return n;
        }

        /// <summary>某材质组的构件数量。</summary>
        public int CountOf(SceneKitMaterial material)
        {
            int n = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Material == material)
                    n++;
            }
            return n;
        }

        /// <summary>
        /// 打在该簇标上的某类构件数量。
        ///
        /// 【为什么需要】合并后一艘船（一组原版船区）的构件**全部**打在主船体簇的标上，
        /// 于是"这艘船有 1 个艏 / 1 个艉 / 每桅一个桅顶巢"这类"完整剪影"断言可以精确到船，
        /// 不必靠包围盒猜（合并船的构件会伸到包络之外）。
        /// </summary>
        public int CountInCluster(int clusterIndex, SceneKitPiece piece)
        {
            int n = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].ClusterIndex == clusterIndex && _parts[i].Piece == piece)
                    n++;
            }
            return n;
        }
    }

    /// <summary>船构件配方（纯数据）：尺寸 + 桅数 + 是否带帆。</summary>
    public readonly struct ShipRecipe
    {
        public readonly string Name;
        public readonly float HullLength;
        public readonly float HullBeam;
        public readonly float HullHeight;
        public readonly float BowLength;
        public readonly float SternLength;
        public readonly int MastCount;
        public readonly float MastHeight;
        public readonly bool HasSails;
        public readonly int CannonCount;

        public ShipRecipe(string name, float hullLength, float hullBeam, float hullHeight,
            float bowLength, float sternLength, int mastCount, float mastHeight, bool hasSails, int cannonCount)
        {
            Name = name;
            HullLength = hullLength;
            HullBeam = hullBeam;
            HullHeight = hullHeight;
            BowLength = bowLength;
            SternLength = sternLength;
            MastCount = mastCount;
            MastHeight = mastHeight;
            HasSails = hasSails;
            CannonCount = cannonCount;
        }
    }

    /// <summary>
    /// 一组"同一艘船 / 同一座岛"的原版平台簇（= kit 的**构件族**）。
    ///
    /// 【为什么需要它】原版 tile 是 2D 侧视图：一艘船被画成几块**互不相接**的瓦片区
    /// （船体区 + 桅 / 横桁区 + 桅盘区），<c>PlatformClusterLayout.BuildFromTileMap</c> 的 4 连通域
    /// 于是把它们切成 5 个簇。若逐簇各建一艘船，level_1 会读成"5 座独立船岛"而不是"2 艘大船"。
    /// 本结构把同一艘船的几个区归并成一组，<see cref="SceneKitCatalog.BuildFor"/> 便按
    /// **整船的完整剪影**只装配一次 —— 原版 tile 只决定"哪里有船、多长、桅在哪"，
    /// 剪影由 kit 按真船结构补全（想象力条款：把像素方块读成这座船本来该有的样子）。
    ///
    /// 【表现层限定】合并只影响 kit 的构件装配：平台簇、逐格块高、出生掩码、单位落位
    /// 一概不动（水距 / 高度 / 单位落位是玩法锚点，见 <c>docs/M2-3D空间模型对齐.md</c>）。
    /// </summary>
    public readonly struct SceneKitGroup
    {
        /// <summary>组类型（Ship / SkyIsland / TerraceIsland）。同组成员 Kind 一致。</summary>
        public readonly PlatformClusterKind Kind;

        /// <summary>
        /// 主簇下标：船 = 行带最低的成员（原版侧视图里船体画在桅/桅盘之下，是最贴近水线的那个区）；
        /// 岛 = 遍历到的最小下标。**该组全部构件都打在它上面**（材质族纯净性断言的定位键）。
        /// </summary>
        public readonly int PrimaryCluster;

        /// <summary>成员簇下标（升序）。</summary>
        public readonly IReadOnlyList<int> Members;

        /// <summary>被识别为"桅 / 桅盘"的成员簇（行带严格在船体之上者；岛组恒为空表）。</summary>
        public readonly IReadOnlyList<int> MastClusters;

        /// <summary>合并包络（含边界，格）。</summary>
        public readonly int X0, Z0, X1, Z1;

        public SceneKitGroup(PlatformClusterKind kind, int primaryCluster, IReadOnlyList<int> members,
            IReadOnlyList<int> mastClusters, int x0, int z0, int x1, int z1)
        {
            Kind = kind;
            PrimaryCluster = primaryCluster;
            Members = members;
            MastClusters = mastClusters;
            X0 = x0;
            Z0 = z0;
            X1 = x1;
            Z1 = z1;
        }

        /// <summary>合并包络宽度（格）；对船组即**船体长度**（1 格 = 1 世界单位）。</summary>
        public int WidthTiles => X1 - X0 + 1;

        /// <summary>合并包络纵深（格）。</summary>
        public int DepthTiles => Z1 - Z0 + 1;

        /// <summary>是否由多块原版区合并而成（报告"几个散件并成一艘船"用）。</summary>
        public bool IsMerged => Members != null && Members.Count > 1;
    }

    /// <summary>
    /// 场景**构件注册表**（纯 C# 数据 + 配方展开，无头可测）：
    ///   · <see cref="LargeShipRecipe"/> / <see cref="SmallBoatRecipe"/> 两套船配方（用户要求的"两艘不同尺寸"）；
    ///   · <see cref="BuildShip"/> 把配方展开成 <see cref="KitPart"/>（艏/舯/艉三种船体段 + 甲板 + 栏杆 + 桅 + 索具 + 帆）；
    ///   · <see cref="BuildGroups"/> 把平台簇归并成构件族（同一艘船 / 同一座岛），
    ///     <see cref="BuildFor"/> 按组装配**整船 / 整岛剪影**；
    ///   · <see cref="BuildLevel1"/> 把 level_1 的平台簇映射成构件摆放表。
    ///
    /// 【为什么是"注册表 + 配方"】<see cref="SceneKitGeometry"/> 只认构件种类，不认具体船只或岛；
    /// 换主题 = 换配方 / 换材质，构件几何完全复用（行业 kit 做法的核心收益）。
    ///
    /// 【提案/待定】尺寸与簇→配方映射是 AI 按用户诉求给出，未确认。
    /// </summary>
    public static class SceneKitCatalog
    {
        /// <summary>大船配方（战场主簇：长条形 15×8 格，甲板高 2 块起）。</summary>
        public static readonly ShipRecipe LargeShipRecipe = new ShipRecipe(
            "galleon", hullLength: 14f, hullBeam: 7f, hullHeight: 1.15f,
            bowLength: 4.2f, sternLength: 3.4f, mastCount: 2, mastHeight: 4.2f,
            hasSails: true, cannonCount: 4);

        /// <summary>小艇配方（侧翼簇 / 小空岛跳板，约为大船一半）。</summary>
        public static readonly ShipRecipe SmallBoatRecipe = new ShipRecipe(
            "longboat", hullLength: 7f, hullBeam: 4f, hullHeight: 0.8f,
            bowLength: 2.2f, sternLength: 1.8f, mastCount: 1, mastHeight: 2.5f,
            hasSails: true, cannonCount: 1);

        /// <summary>
        /// 从平台簇类型取配方（<c>null</c> = 不是船簇）。
        /// </summary>
        public static ShipRecipe? RecipeFor(PlatformClusterKind kind)
        {
            switch (kind)
            {
                case PlatformClusterKind.Ship: return LargeShipRecipe;
                default: return null;
            }
        }

        // ==================================================================
        // 构件族归并（同一艘船 / 同一座岛）
        // ==================================================================
        // 原版是 2D 侧视图：一艘船的"船体 / 桅（横桁）/ 桅盘"是几块互不相接的瓦片区，
        // 连通域会把它们切成多个簇。判据只用**格坐标**（X 间距 + 行带间距），不用瓦片名 ——
        // PlatformClusterKind 已经把"含船体语汇"译成 Ship（见 PlatformClusterLayout.BuildFromTileMap），
        // 所以表现层不必再回头认瓦片；换一张地图，规则照样成立。

        /// <summary>
        /// 同属一艘船的判定①：X 方向格间距上限（格）。
        /// 间距 ≤2 格（含重叠 / 相邻）即"在船的同一侧、同一段"——横跨竞技场的两块船区
        /// （如 level_1 的左船体 X[0,18] 与右桅盘 X[46,47]）间距 27 格，绝不会被并到一起。
        /// </summary>
        public const int ShipMergeMaxGapTiles = 2;

        /// <summary>
        /// 同属一艘船的判定②：行带（Z 带）间距上限（格）——"行带相近"。
        ///
        /// 【为什么是 6】原版侧视图里桅/桅盘画在船体**上方**，行号相差 4-5 行（level_1 实测：
        /// 桅盘行 5 / 桅行 7 / 船体行 11-13，取 Z 后 4 / 6 / 10-12）。上限定 6 格既容得下
        /// "船体↔桅↔桅盘"的整条链（链式吸收），又不会把远处另一座岛的船区（行差更大）拉进来。
        /// 【提案/待定】6 是本次按 level_1 实测行差给的 AI 提案值，未确认。
        /// </summary>
        public const int ShipMergeMaxBandGapRows = 6;

        /// <summary>台地 / 空岛轻合并的 X 间距上限（格）：相邻碎岛并成"一座岛"，只做一圈岛缘岩唇。</summary>
        public const int IslandMergeMaxGapTiles = 2;

        /// <summary>
        /// 原版桅区里**每根桅**占的 X 跨度（格）：14 格宽的横桁区（level_1 左侧那一块）→ 2 根桅，
        /// 6 格宽（如桅盘那种小图形）→ 1 根。原版 tile 只给"桅在哪段"，一根桅占多宽由本常量定。
        /// 【提案/待定】7 是本次按 level_1 的横桁区宽度（14 格 = 两个 7 格的帆图形）给的提案值。
        /// </summary>
        public const float MastSpacingTiles = 7f;

        /// <summary>
        /// 把平台簇归并成**构件族**（确定性，只用格坐标 + 升序遍历，与种子/关卡号无关）：
        ///   · 船组：从行带最低的船簇起（= 船体，原版侧视图里最贴近水线的那一块）逐块吸收
        ///     ——X 间距 ≤ <see cref="ShipMergeMaxGapTiles"/> 且行带间距 ≤
        ///     <see cref="ShipMergeMaxBandGapRows"/> 的未认领船簇（包络边吸收边长，故
        ///     "桅盘→桅→船体"这种链能整条并进来）；
        ///   · 岛组（空岛 / 梯田岛）：同 Kind、**同总块高**（顶面共面，才谈得上"一座岛的顶面"）、
        ///     X 间距 ≤ <see cref="IslandMergeMaxGapTiles"/>、且 Z 带**重叠**（同一纵深带里的邻岛；
        ///     纵深上分开的两级台地是两座岛，不并）。
        ///
        /// 【组序】先全部船组（按"主簇"确定序），再全部岛组（按最小下标序）。组与组之间不共享成员。
        /// </summary>
        public static IReadOnlyList<SceneKitGroup> BuildGroups(PlatformMap map)
        {
            var groups = new List<SceneKitGroup>();
            if (map == null || map.Clusters.Count == 0)
                return groups;

            int n = map.Clusters.Count;
            int[] areas = ClusterAreas(map);
            var claimed = new bool[n];

            // ---- ① 船组：主簇 = 行带最低（Z1 最大）；同高取面积大者（船体格数多），再取下标小者 ----
            while (true)
            {
                int best = -1;
                for (int c = 0; c < n; c++)
                {
                    if (claimed[c] || map.Clusters[c].Kind != PlatformClusterKind.Ship)
                        continue;
                    if (best < 0)
                    {
                        best = c;
                        continue;
                    }

                    PlatformClusterInfo a = map.Clusters[c];
                    PlatformClusterInfo b = map.Clusters[best];
                    bool better = a.Z1 > b.Z1
                        || (a.Z1 == b.Z1 && areas[c] > areas[best])
                        || (a.Z1 == b.Z1 && areas[c] == areas[best] && c < best);
                    if (better)
                        best = c;
                }

                if (best < 0)
                    break;

                groups.Add(GrowGroup(map, claimed, best, shipGroup: true));
            }

            // ---- ② 岛组：从最小未认领下标起（岛没有"船体"这种天然锚，用下标保证确定性）----
            for (int c = 0; c < n; c++)
            {
                PlatformClusterKind kind = map.Clusters[c].Kind;
                if (claimed[c] || (kind != PlatformClusterKind.SkyIsland && kind != PlatformClusterKind.TerraceIsland))
                    continue;

                groups.Add(GrowGroup(map, claimed, c, shipGroup: false));
            }

            return groups;
        }

        /// <summary>取某类型的组（供报告 / 断言直接读，如"level_1 有 2 艘船"）。</summary>
        public static int CountGroups(IReadOnlyList<SceneKitGroup> groups, PlatformClusterKind kind)
        {
            if (groups == null)
                return 0;

            int n = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Kind == kind)
                    n++;
            }
            return n;
        }

        /// <summary>以 <paramref name="primary"/> 为锚，吸收所有同族且满足间距判据的未认领簇（包络边吸收边长）。</summary>
        static SceneKitGroup GrowGroup(PlatformMap map, bool[] claimed, int primary, bool shipGroup)
        {
            PlatformClusterKind kind = map.Clusters[primary].Kind;
            var members = new List<int> { primary };
            claimed[primary] = true;

            int x0 = map.Clusters[primary].X0, x1 = map.Clusters[primary].X1;
            int z0 = map.Clusters[primary].Z0, z1 = map.Clusters[primary].Z1;

            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int c = 0; c < map.Clusters.Count; c++)
                {
                    if (claimed[c])
                        continue;

                    PlatformClusterInfo info = map.Clusters[c];
                    if (info.Kind != kind)
                        continue;

                    bool joins = shipGroup
                        ? HorizontalGapTiles(x0, x1, info.X0, info.X1) <= ShipMergeMaxGapTiles
                            && HorizontalGapTiles(z0, z1, info.Z0, info.Z1) <= ShipMergeMaxBandGapRows
                        : HorizontalGapTiles(x0, x1, info.X0, info.X1) <= IslandMergeMaxGapTiles
                            && HorizontalGapTiles(z0, z1, info.Z0, info.Z1) == 0   // Z 带必须重叠（同一纵深带）
                            && SameTotalHeight(map.Clusters[primary], info);

                    if (!joins)
                        continue;

                    members.Add(c);
                    claimed[c] = true;
                    if (info.X0 < x0) x0 = info.X0;
                    if (info.X1 > x1) x1 = info.X1;
                    if (info.Z0 < z0) z0 = info.Z0;
                    if (info.Z1 > z1) z1 = info.Z1;
                    grew = true;
                }
            }

            members.Sort();

            // 桅 / 桅盘簇：行带**严格在船体之上**（原版侧视图里画在船体上方的就是 mast_* / crows_nest_*）。
            var masts = new List<int>();
            if (shipGroup)
            {
                int hullZ0 = map.Clusters[primary].Z0;
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i] == primary)
                        continue;
                    if (map.Clusters[members[i]].Z1 < hullZ0)
                        masts.Add(members[i]);
                }
            }

            return new SceneKitGroup(kind, primary, members, masts, x0, z0, x1, z1);
        }

        /// <summary>两个闭区间（格）的间距：重叠/相邻 = 0，否则为中间的空格数。</summary>
        static int HorizontalGapTiles(int a0, int a1, int b0, int b1)
        {
            return Mathf.Max(0, Mathf.Max(a0, b0) - Mathf.Min(a1, b1) - 1);
        }

        /// <summary>两个簇的总块高区间完全相同（顶面与底面共面）——"同高差"，合并后岩唇才贴同一平面。</summary>
        static bool SameTotalHeight(in PlatformClusterInfo a, in PlatformClusterInfo b)
        {
            return a.MinTotalBlocks == b.MinTotalBlocks && a.MaxTotalBlocks == b.MaxTotalBlocks;
        }

        /// <summary>逐簇的实占地面格数（主簇判定用：船体格数远多于桅/桅盘）。</summary>
        static int[] ClusterAreas(PlatformMap map)
        {
            var areas = new int[map.Clusters.Count];
            for (int i = 0; i < map.CellCluster.Length; i++)
            {
                int c = map.CellCluster[i];
                if (c >= 0 && c < areas.Length)
                    areas[c]++;
            }
            return areas;
        }

        /// <summary>
        /// 一艘船的**桅位**（沿长轴相对船中的偏移，世界单位，升序）——由原版桅区（mast_* / crows_nest_*）给出。
        ///
        /// 【怎么读】先把 X 区间相接/重叠的桅簇并成一个"桅区"（桅 + 桅盘本来就是同一根桅的图形），
        /// 再按 <see cref="MastSpacingTiles"/> 把每个桅区切成若干根桅：
        /// 14 格宽的横桁区 → 2 根，6 格宽 → 1 根。偏移最后钳进船体范围（桅必须立在甲板上）。
        ///
        /// 【没有桅区时】退回配方默认桅数（长船 2 桅），保住"一艘船至少有桅"的剪影底线。
        /// </summary>
        public static IReadOnlyList<float> MastOffsetsFor(PlatformMap map, in SceneKitGroup group, float hullLength)
        {
            var offsets = new List<float>(4);
            float halfLen = hullLength * 0.5f;
            float limit = Mathf.Max(0f, halfLen - 1.2f);
            float centerX = (group.X0 + group.X1 + 1f) * 0.5f;

            // 桅区：桅簇按 **X 起止**升序并集（间距 ≤1 格视为同一根桅的图形）。
            // 【必须按 X 排序，不能按下标】并集只与"已生成的最后一个区间"比较，
            // 下标序不等于 X 序会让本可合并的相邻区间各起一根桅（level_4 实测：3 根变 3 段）。
            var regions = new List<Vector2Int>(4);
            var sorted = new List<int>(group.MastClusters ?? new int[0]);
            sorted.Sort(CompareClusterByX(map));
            for (int i = 0; i < sorted.Count; i++)
            {
                PlatformClusterInfo info = map.Clusters[sorted[i]];
                if (regions.Count > 0)
                {
                    Vector2Int last = regions[regions.Count - 1];
                    if (HorizontalGapTiles(last.x, last.y, info.X0, info.X1) <= 1)
                    {
                        regions[regions.Count - 1] = new Vector2Int(
                            Mathf.Min(last.x, info.X0), Mathf.Max(last.y, info.X1));
                        continue;
                    }
                }

                regions.Add(new Vector2Int(info.X0, info.X1));
            }

            for (int r = 0; r < regions.Count; r++)
            {
                int regionWidth = regions[r].y - regions[r].x + 1;
                int count = Mathf.Max(1, Mathf.RoundToInt(regionWidth / MastSpacingTiles));
                for (int m = 0; m < count; m++)
                {
                    float t = count == 1 ? 0.5f : (m + 0.5f) / count;
                    // 格 → 世界：第 gx 格占 [gx, gx+1]，故桅区右端取 y+1。
                    float worldX = Mathf.Lerp(regions[r].x, regions[r].y + 1, t);
                    offsets.Add(Mathf.Clamp(worldX - centerX, -limit, limit));
                }
            }

            if (offsets.Count == 0)
            {
                int count = hullLength >= 10f ? LargeShipRecipe.MastCount : 1;
                for (int m = 0; m < count; m++)
                {
                    offsets.Add(count == 1
                        ? 0f
                        : Mathf.Lerp(-halfLen * 0.45f, halfLen * 0.35f, m / (float)(count - 1)));
                }
            }

            offsets.Sort();
            return offsets;
        }

        /// <summary>按簇的 X 起止（再按下标）升序的确定性比较器（桅区并集用）。</summary>
        static System.Comparison<int> CompareClusterByX(PlatformMap map)
        {
            return (a, b) =>
            {
                PlatformClusterInfo ca = map.Clusters[a];
                PlatformClusterInfo cb = map.Clusters[b];
                if (ca.X0 != cb.X0)
                    return ca.X0 < cb.X0 ? -1 : 1;
                if (ca.X1 != cb.X1)
                    return ca.X1 < cb.X1 ? -1 : 1;
                return a == b ? 0 : (a < b ? -1 : 1);
            };
        }

        // ==================================================================
        // 语义纯净的材质族（用户裁决 4）
        // ==================================================================
        // 「大块可读剪影 + 大色块」的反面清单里点名了"木板 + 草方块混搭"，
        // 所以每个簇 Kind 只允许用一族材质（见 MaterialFamilyFor）：
        //   · Ship        = 木 / 暗木 / 铁 / 布索 ← 一条船只有木料、铁件、缆帆；
        //   · SkyIsland   = 岩 / 植被           ← 岩体 + 整片草顶（草是"顶面整片"，不是摆件）；
        //   · TerraceIsland = 岩 / 植被         ← 沙岩梯田 + 沙缘的棕榈（不摆木箱/木桶）。
        // 唯一的跨族例外是**出生簇的栏杆**（Wood，用户裁决 4 明写"出生岛=沙台地+栏杆"），
        // 所以纯净性断言要排除出生簇的那 1 条 Bulwark。

        /// <summary>该 Kind 允许的材质族白名单（出生簇栏杆的 Wood 是显式例外）。</summary>
        public static IReadOnlyList<SceneKitMaterial> MaterialFamilyFor(PlatformClusterKind kind)
        {
            switch (kind)
            {
                case PlatformClusterKind.Ship:
                    return new[] { SceneKitMaterial.Wood, SceneKitMaterial.WoodDark,
                        SceneKitMaterial.Metal, SceneKitMaterial.Cloth };

                case PlatformClusterKind.SkyIsland:
                case PlatformClusterKind.TerraceIsland:
                    return new[] { SceneKitMaterial.Rock, SceneKitMaterial.Foliage };

                default:
                    return new SceneKitMaterial[0];
            }
        }

        /// <summary>
        /// 把船配方展开成构件摆放表。船体按真船结构拼：沿长轴分 艏 / 舯（若干段）/ 艉，
        /// 甲板铺板、两舷舷墙、甲板上立桅（横桁 + 索具 + 帆），可选舰炮。
        /// </summary>
        /// <param name="recipe">配方。</param>
        /// <param name="deckCenter">甲板顶面中心世界坐标（y = 甲板面）。</param>
        /// <param name="yawDegrees">绕 Y 朝向。</param>
        /// <param name="seed">确定性抖动种子。</param>
        public static List<KitPart> BuildShip(in ShipRecipe recipe, Vector3 deckCenter, float yawDegrees, int seed)
        {
            // 配方自带的长度 / 艏艉段 / 船宽 / 桅位（既有调用逐值不变）。
            return BuildCompleteShip(recipe, deckCenter, yawDegrees, seed,
                recipe.HullLength, recipe.BowLength, recipe.SternLength, recipe.HullBeam, null);
        }

        /// <summary>
        /// 装配一艘**完整剪影**的船（纯数据）：船体长度 / 艏艉段长度 / 船宽 / 桅位全部显式给出。
        ///
        /// 【为什么把尺寸提成参数】合并后的船长度来自**原版船区的 X 跨度**（1 格 = 1 世界单位）、
        /// 桅位来自**原版桅区**，不再是配方里写死的 14 / 2 桅。剪影的其余部分
        /// （舷侧板、艏楼栏杆、船艏装饰、桅顶瞭望巢、横桁帆）仍由本函数按真船结构补全 ——
        /// 这正是"原版 tile 只决定哪里有船、多长、桅在哪，剪影由 kit 完整生成"。
        ///
        /// 每根桅一律配齐：桅杆（含 2 根横桁）→ 桅顶瞭望巢 → 横桁 + 帆 + 4 根索具。
        /// </summary>
        /// <param name="hullLength">船体总长（沿长轴，世界单位；原版船区 X 跨度）。</param>
        /// <param name="bowLength">艏段长。</param>
        /// <param name="sternLength">艉段长。</param>
        /// <param name="hullBeam">船宽（沿 Z；实测取该船区自身的格纵深，船体才与平台贴合）。</param>
        /// <param name="mastAlongOffsets">
        /// 各桅沿长轴相对 <paramref name="deckCenter"/> 的偏移（世界单位，升序）；
        /// <c>null</c> = 用配方的默认桅位（<see cref="ShipRecipe.MastCount"/> 根、按船长比例铺开）。
        /// </param>
        public static List<KitPart> BuildCompleteShip(in ShipRecipe recipe, Vector3 deckCenter, float yawDegrees,
            int seed, float hullLength, float bowLength, float sternLength, float hullBeam,
            IReadOnlyList<float> mastAlongOffsets)
        {
            var parts = new List<KitPart>(96);
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Cos(yawRad), 0f, Mathf.Sin(yawRad));   // 长轴
            Vector3 side = new Vector3(-axis.z, 0f, axis.x);                        // 船宽轴

            float halfLen = hullLength * 0.5f;
            float halfBeam = hullBeam * 0.5f;

            Vector3 P(float along, float lateral) => deckCenter + axis * along + side * lateral;

            // 【用户裁决 2026-09-14"真船建模，意译不要直译"】船体不再用 HullBow/Mid/Stern
            // 盒子拼剪影，改为**一整具放样船体**（ShipHullGeometry：横剖站渐变+舷弧+
            // 内倾舷墙+艉板+龙骨+艏斜桁，几何见该文件）。吃深按船宽的 55% 给真实比例。
            parts.Add(new KitPart(SceneKitPiece.ShipHullLoft, SceneKitMaterial.Wood,
                deckCenter, yawDegrees, hullBeam,
                hullLength, Mathf.Max(1.4f, hullBeam * 0.55f)));

            // ---- 甲板铺板（沿长轴一条条；可站面由地形格提供，铺板是贴面装饰） ----
            int planks = Mathf.Max(3, Mathf.RoundToInt(hullBeam / 0.9f));
            for (int i = 0; i < planks; i++)
            {
                float lateral = Mathf.Lerp(-halfBeam + 0.35f, halfBeam - 0.35f, (i + 0.5f) / planks);
                parts.Add(new KitPart(SceneKitPiece.DeckPlank, SceneKitMaterial.Wood,
                    P(0f, lateral) + Vector3.up * 0.03f, yawDegrees, 1f,
                    hullLength - 0.4f, 0.06f));
            }

            // （舷墙/艏饰已并入放样船体本体——栏杆帽/艉板/艏斜桁见 ShipHullGeometry。）

            // ---- 桅杆 + 索具 + 横桁 + 帆 + 桅顶瞭望巢 ----
            int mastCount = mastAlongOffsets != null ? mastAlongOffsets.Count : recipe.MastCount;
            for (int m = 0; m < mastCount; m++)
            {
                float along = mastAlongOffsets != null
                    ? mastAlongOffsets[m]
                    : (recipe.MastCount == 1
                        ? 0f
                        : Mathf.Lerp(-halfLen * 0.45f, halfLen * 0.35f, m / (float)(recipe.MastCount - 1)));
                Vector3 mastBase = P(along, 0f);

                parts.Add(new KitPart(SceneKitPiece.Mast, SceneKitMaterial.Wood,
                    mastBase, yawDegrees, 1f, 0.14f, recipe.MastHeight));

                // 索具：桅顶 → 艏、艉、两舷各一根。
                parts.Add(new KitPart(SceneKitPiece.Rigging, SceneKitMaterial.Cloth,
                    mastBase, yawDegrees, 1f, -halfLen, recipe.MastHeight));
                parts.Add(new KitPart(SceneKitPiece.Rigging, SceneKitMaterial.Cloth,
                    mastBase, yawDegrees + 180f, 1f, -halfLen, recipe.MastHeight));
                parts.Add(new KitPart(SceneKitPiece.Rigging, SceneKitMaterial.Cloth,
                    mastBase, yawDegrees + 90f, 1f, halfBeam, recipe.MastHeight));
                parts.Add(new KitPart(SceneKitPiece.Rigging, SceneKitMaterial.Cloth,
                    mastBase, yawDegrees - 90f, 1f, halfBeam, recipe.MastHeight));

                // 横桁：挂帆的横向木杆（沿船宽），摆在帆的顶缘之上 —— 帆"挂"在桁上。
                parts.Add(new KitPart(SceneKitPiece.Yard, SceneKitMaterial.Wood,
                    mastBase + Vector3.up * (recipe.MastHeight * 0.82f), yawDegrees + 90f, 1f,
                    hullBeam * 0.95f, 0.12f));

                if (recipe.HasSails)
                {
                    parts.Add(new KitPart(SceneKitPiece.Sail, SceneKitMaterial.Cloth,
                        mastBase + Vector3.up * (recipe.MastHeight * 0.55f), yawDegrees + 90f,
                        recipe.MastHeight * 0.5f, recipe.MastHeight * 0.55f, 0f));
                }

                // 桅顶瞭望巢（乌鸦巢）：每桅必有 —— 它是"这是船"的辨认特征之一。
                parts.Add(new KitPart(SceneKitPiece.CrowNest, SceneKitMaterial.Wood,
                    mastBase + Vector3.up * (recipe.MastHeight * 0.94f), yawDegrees, 0.3f,
                    0.36f, 0.26f));
            }

            // ---- 舰炮（沿两舷，简化：炮管 + 轮座） ----
            for (int c = 0; c < recipe.CannonCount; c++)
            {
                float along = Mathf.Lerp(-halfLen * 0.5f, halfLen * 0.5f,
                    recipe.CannonCount == 1 ? 0.5f : c / (float)(recipe.CannonCount - 1));
                int s = c % 2 == 0 ? -1 : 1;
                parts.Add(new KitPart(SceneKitPiece.Prop, SceneKitMaterial.Metal,
                    P(along, s * (halfBeam - 0.25f)) + Vector3.up * 0.35f,
                    yawDegrees + (s < 0 ? 90f : -90f), 1f, 0.7f, 0.35f));
            }

            return parts;
        }

        /// <summary>
        /// 把 level_1 的平台簇映射成整套 kit 摆放 = <c>BuildFor(1, BuildLevel1(), seed)</c> 的等价委托
        /// （既有测试与烘焙沿用它，行为逐值不变）。
        /// </summary>
        public static SceneKitLayout BuildLevel1(int seed)
        {
            return BuildFor(1, PlatformClusterLayout.BuildLevel1(), seed);
        }

        /// <summary>
        /// 把**任意关**的平台簇映射成整套 kit 摆放（确定性）。**按构件族装配**（<see cref="BuildGroups"/>）：
        ///   · 船族（同一艘船的几个原版区）→ **整船剪影只装配一次**：船体长度 = 合并包络的 X 跨度（含桅区）、
        ///     船宽 = 主船体区自身的格纵深（船体才与平台贴合）、桅位 = 原版桅区；
        ///     剪影（艏/舯/艉船体段、甲板、两舷舷墙、艏楼栏杆 + 艏柱/艏斜桁、每桅的桅顶瞭望巢 / 横桁 / 帆）
        ///     由 <see cref="BuildCompleteShip"/> 补全。**纯木/布索/铁**，无岩无草；
        ///   · 空岛族 → 岛顶岩唇 + 岛缘散岩 + **整片草顶**（非出生簇）；大岛再摆 2 棵棕榈作竖向剪影。
        ///     **不摆木器、不摆小艇**（岛就是岛，船就是船）；
        ///   · 梯田岛族 → 沿棱线的分级礁石（逐成员簇 = 逐格，与合并前一致）+ **每族一圈**顶面岩唇 +
        ///     沙缘棕榈（相邻同高差的小岛并成一座，取消"每座碎岛各带一圈重复岩唇"）；
        ///   · <c>IsSpawnCluster</c>（出生簇）→ 一圈收边栏杆（Wood，跨族唯一例外）+ 铁锚；
        ///     船簇出生岛另有木箱/木桶（甲板语汇）。**每个**出生簇都照旧生效，即使它已被并进某艘船
        ///     （例：level_1 的桅 / 桅盘区上站着红蓝各一名队长）。
        ///
        /// 【表现层限定】合并不动任何玩法锚点：平台簇、逐格块高、水距、出生掩码、单位落位全部来自
        /// <see cref="PlatformMap"/>，本函数**只读不写**那份 map；kit 几何**无碰撞**——
        /// <c>SceneKitComposer</c> 只写 <see cref="MeshBuffers"/>，<c>RuntimeSceneArt</c> 把它挂成
        /// MeshFilter / MeshRenderer，全程不生成 Collider（场景文档 §9.4）。
        ///
        /// 【簇 Kind 的来源（2026-09-14）】Kind 现在由**原版 tile 语义**决定，见
        /// <c>PlatformClusterLayout.BuildFromTileMap</c>：岛内含船体语汇
        /// （<c>ship_*</c> / <c>cannon_port_*</c> / <c>mast_*</c> / <c>crows_nest_*</c>）→
        /// <see cref="PlatformClusterKind.Ship"/>（"mast/ship 区域 → Ship 族"）；
        /// 其余（草地 / 土 / 沙洲瓦片）→ <see cref="PlatformClusterKind.TerraceIsland"/>
        /// （"grass/earth 区域 → TerraceIsland 族"）。本文件的材质族映射因此**不需要按瓦片名再分叉** ——
        /// 只要 Kind 对，<see cref="MaterialFamilyFor"/> 的白名单就对。
        ///
        /// 【基准高度】摆放高度一律走 <c>info.BaseBlocks</c> 折算的块高口径
        /// （原版行号给出的悬浮高度），与 <c>TileTerrainGrid</c> / 碰撞方块同源，不会错位。
        /// 合并后的船 / 岛取**主簇**的基准高度（同族成员共面，见 <see cref="BuildGroups"/> 的判据）。
        ///
        /// 【为什么按 Kind 而不是按关号】33 关的形状全部来自 <see cref="PlatformMap"/> 的簇 Kind
        /// （由原版 tile 地图推导），烘焙/运行时的配方展开只认 Kind → 换关卡 = 换一张 map，kit 逻辑零改动。
        /// </summary>
        /// <param name="levelNumber">关卡号（只用于调试日志，几何完全不依赖它）。</param>
        /// <param name="map">该关的平台簇地图（<see cref="PlatformClusterLayout.BuildFor"/> 的产物）。</param>
        /// <param name="seed">确定性种子。</param>
        public static SceneKitLayout BuildFor(int levelNumber, PlatformMap map, int seed)
        {
            _ = levelNumber;
            var layout = new SceneKitLayout();
            if (map == null)
                return layout;

            float block = TerrainCatalog.DefaultBlockWorldHeight;
            IReadOnlyList<SceneKitGroup> groups = BuildGroups(map);

            for (int g = 0; g < groups.Count; g++)
            {
                SceneKitGroup group = groups[g];
                int clusterIndex = group.PrimaryCluster;
                PlatformClusterInfo primary = map.Clusters[clusterIndex];

                // 本族的构件一律打上**主簇**下标（供"每族材质族纯净性 / 完整剪影"精确断言）。
                void AddPart(in KitPart part) => layout.Add(part.WithCluster(clusterIndex));

                // 【基准高度】用户裁决 2 给每个簇一个悬浮基准高度（PlatformClusterInfo.BaseHeight），
                // 它已折进 grid 的块高；kit 的摆放高度必须走同一口径，否则船/岛会"沉在岛底平面里"。
                float topY = LevelGeometry.GroundTopY + (primary.MaxBlocks + primary.BaseBlocks) * block;
                float deckY = LevelGeometry.GroundTopY + (primary.MinBlocks + primary.BaseBlocks) * block;
                float cx = (group.X0 + group.X1 + 1f) * 0.5f;                 // 族包络中心（合并后的船/岛中点）
                float cz = (primary.Z0 + primary.Z1 + 1f) * 0.5f;             // 纵深取主簇（船体自身的格纵深）
                int width = group.WidthTiles;                                // = 船体长度（1 格 = 1 世界单位）
                int depth = group.DepthTiles;

                if (group.Kind == PlatformClusterKind.Ship)
                {
                    // 整船剪影：长度取原版船区 X 跨度（含桅）、船宽取主船体区纵深、桅位取原版桅区。
                    float length = width;
                    float beam = Mathf.Clamp(primary.DepthTiles, 2.4f, LargeShipRecipe.HullBeam);
                    float bowLength = Mathf.Clamp(length * 0.28f, 0.9f, 5.4f);
                    float sternLength = Mathf.Clamp(length * 0.22f, 0.8f, 4.4f);
                    ShipRecipe recipe = length >= 10f ? LargeShipRecipe : SmallBoatRecipe;

                    List<KitPart> ship = BuildCompleteShip(recipe, new Vector3(cx, deckY, cz), 0f,
                        seed + clusterIndex, length, bowLength, sternLength, beam,
                        MastOffsetsFor(map, group, length));
                    for (int i = 0; i < ship.Count; i++)
                        AddPart(ship[i]);
                }
                else if (group.Kind == PlatformClusterKind.SkyIsland)
                {
                    // 空岛 = 岩体 + 岛顶岩唇 + **整片草顶**（用户裁决 4：草只在顶面整片，不是方块摆件）。
                    float rx = width * 0.5f - 0.05f;
                    float rz = depth * 0.5f - 0.05f;
                    AddPart(new KitPart(SceneKitPiece.IslandTop, SceneKitMaterial.Rock,
                        new Vector3(cx, topY, cz), 0f, 1f, rx, rz));

                    // 岩块沿岛缘（确定性取样，不抢中心）。
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i / 6f * Mathf.PI * 2f + SceneArtHash.SignedHash(seed, clusterIndex, i) * 0.4f;
                        AddPart(new KitPart(SceneKitPiece.RockChunk, SceneKitMaterial.Rock,
                            new Vector3(cx + Mathf.Cos(ang) * rx * 0.9f, topY, cz + Mathf.Sin(ang) * rz * 0.9f),
                            SceneArtHash.Hash01(seed, i, clusterIndex) * 360f,
                            0.28f + SceneArtHash.Hash01(seed, i, clusterIndex + 7) * 0.25f, 0f, 0f));
                    }

                    // 出生岛是"沙台地 + 栏杆"，不铺草（语义交给地形壳的沙/草/岩高度混合）。
                    if (!primary.IsSpawnCluster)
                        AddGrassCap(layout, clusterIndex, width, depth, cx, cz, topY);

                    // 棕榈只作大岛的竖向剪影（Foliage，与岩族同族）。
                    if (width > 5 && depth > 2)
                    {
                        AddProp(layout, clusterIndex, ScenePropKind.Palm,
                            new Vector3(cx - 1.5f, topY, cz - 1.0f), seed + 1, 4.5f);
                        AddProp(layout, clusterIndex, ScenePropKind.Palm,
                            new Vector3(cx + 1.2f, topY, cz + 1.5f), seed + 2, 5.2f);
                    }
                }
                else
                {
                    // 梯田小岛：沿每级台阶的棱线撒**不规则礁石**（3 种形/朝向/尺度，见
                    // SceneKitGeometry.AddRockChunk），而不是逐格叠同款 IslandTop 岩台——
                    // 旧写法对每个棱线格调一次 AddIslandTop（每次都铺一圈 10 个盒子），
                    // 一个台面就叠出上百个同形石块，r2 诊断读成"奶酪楔岩块层层码叠 / 撕碎的卡纸"。
                    // 逐**成员簇**遍历（未合并时 = 逐簇，与合并前逐格结果一致）。
                    for (int m = 0; m < group.Members.Count; m++)
                    {
                        PlatformClusterInfo member = map.Clusters[group.Members[m]];
                        int memberBaseBlocks = member.BaseBlocks;
                        for (int gz = member.Z0; gz <= member.Z1; gz++)
                        {
                            for (int gx = member.X0; gx <= member.X1; gx++)
                            {
                                int blocks = map.BlocksAt(gx, gz);
                                if (blocks <= 0)
                                    continue;
                                // 只在"比邻居高"的棱线上放，避免铺满整个顶面。
                                bool edge = blocks > map.BlocksAt(gx - 1, gz) || blocks > map.BlocksAt(gx + 1, gz)
                                    || blocks > map.BlocksAt(gx, gz - 1) || blocks > map.BlocksAt(gx, gz + 1);
                                if (!edge)
                                    continue;
                                // 抽样：只保留约 1/4 棱线格，礁石不连成一条石墙。
                                if (SceneArtHash.Hash01(gx, gz, 77) > 0.26f)
                                    continue;

                                float r = 0.16f + SceneArtHash.Hash01(gx, gz, 78) * 0.26f;
                                AddPart(new KitPart(SceneKitPiece.RockChunk, SceneKitMaterial.Rock,
                                    new Vector3(gx + 0.5f,
                                        LevelGeometry.GroundTopY + (blocks + memberBaseBlocks) * block, gz + 0.5f),
                                    SceneArtHash.Hash01(gx, gz, 79) * 360f, r, 0f, 0f));
                            }
                        }
                    }

                    // 梯田顶面一圈岩唇（沙岩梯田的收边）——**每族一圈**（相邻同高差的小岛并成一座岛，
                    // 只留一圈外缘；否则每座碎岛各带一圈，读成"一圈圈重复的岩唇"）。
                    AddPart(new KitPart(SceneKitPiece.IslandTop, SceneKitMaterial.Rock,
                        new Vector3(cx, topY, cz), 0f, 1f, width * 0.5f - 0.05f, depth * 0.5f - 0.05f));

                    // 梯田陈设（用户裁决 4：「草只长在沙边上，不叠木板上」）：
                    // 原实现摆 Palm + Crate + Barrel（木箱木桶叠在岩台上 = 被点名的"混搭"），
                    // 现只留沙缘植被（Foliage），木器交给船簇（Ship 的甲板语汇）。
                    AddProp(layout, clusterIndex, ScenePropKind.Palm,
                        new Vector3(group.X0 + 1.5f, topY, cz), seed + 11, 4.8f);
                    AddProp(layout, clusterIndex, ScenePropKind.Palm,
                        new Vector3(group.X1 - 1.5f, topY, cz - 1f), seed + 14, 5.5f);
                    if (depth >= 5)
                        AddProp(layout, clusterIndex, ScenePropKind.Palm,
                            new Vector3(cx, topY, group.Z1 - 1f), seed + 15, 4.2f);
                }

                // 出生簇标识：**每个**出生簇都要（含被并进本族的成员），保证"出生岛 = 沙台地 + 栏杆"
                // 不会因为合并而丢失。单成员族与合并前的调用逐值一致（seed + 簇下标、该簇自己的高度）。
                for (int m = 0; m < group.Members.Count; m++)
                {
                    int memberIndex = group.Members[m];
                    PlatformClusterInfo member = map.Clusters[memberIndex];
                    float memberTopY = LevelGeometry.GroundTopY
                        + (member.MaxBlocks + member.BaseBlocks) * block;
                    AddSpawnAccent(layout, member, memberIndex, memberTopY, member.WidthTiles, seed + memberIndex);
                }
            }

            return layout;
        }

        /// <summary>
        /// 空岛顶面的**整片草顶**：用 0.5 单位宽的平行草条铺满顶面（构件族包络）。
        ///
        /// 【为什么复用 <see cref="SceneKitPiece.DeckPlank"/> 而不是新枚举】DeckPlank 的几何就是
        /// "顶面贴地的一块薄板"，换 Foliage 材质即为草皮 —— 语义完全够用，也不必为一片草皮扩枚举。
        /// （新枚举值<b>必须</b>同步加进 <c>SceneKitComposer</c> 的 switch，否则会走进 default 分支
        /// **静默无几何** —— 看不见的构件比"名字不贴切"危险得多。）
        /// </summary>
        static void AddGrassCap(SceneKitLayout layout, int clusterIndex, int width, int depth,
            float cx, float cz, float topY)
        {
            const float stripWidth = 0.5f;   // 与 SceneKitComposer 里 AddDeckPlank 的硬编码板宽一致
            int strips = Mathf.Clamp(Mathf.RoundToInt(depth / stripWidth), 1, 24);

            for (int i = 0; i < strips; i++)
            {
                float t = (i + 0.5f) / strips;
                float z = cz + (t - 0.5f) * depth;
                layout.Add(new KitPart(SceneKitPiece.DeckPlank, SceneKitMaterial.Foliage,
                    new Vector3(cx, topY, z), 0f, 1f, Mathf.Max(0.6f, width - 0.6f), 0.06f)
                    .WithCluster(clusterIndex));
            }
        }

        /// <summary>
        /// 出生簇的"出生台地"标识（用户裁决 4：出生岛 = 沙台地 + 栏杆）：
        /// 一圈收边栏杆（Wood，跨族例外）恒定，陈设按 Kind 分族——
        /// 船簇（甲板语汇）摆木箱/木桶，岛簇（岩沙语汇）只摆铁锚，**不摆木器**（消灭"木板上摆草方块"式的混搭）。
        /// </summary>
        static void AddSpawnAccent(SceneKitLayout layout, in PlatformClusterInfo info, int clusterIndex,
            float topY, int width, int seed)
        {
            if (!info.IsSpawnCluster)
                return;

            float cx = (info.X0 + info.X1 + 1f) * 0.5f;
            float cz = (info.Z0 + info.Z1 + 1f) * 0.5f;

            layout.Add(new KitPart(SceneKitPiece.Bulwark, SceneKitMaterial.Wood,
                new Vector3(cx, topY + 0.22f, info.Z0 + 0.6f), 0f, 1f,
                Mathf.Max(1.5f, width - 0.6f), 0.4f).WithCluster(clusterIndex));

            if (info.Kind != PlatformClusterKind.Ship)
                return;   // 岛簇出生岛 = 沙台地 + 栏杆（不再叠加铁锚/木器，保持材质族纯净）

            AddProp(layout, clusterIndex, ScenePropKind.Anchor, new Vector3(info.X0 + 1.2f, topY, cz), seed + 31, 1f);
            AddProp(layout, clusterIndex, ScenePropKind.Crate, new Vector3(cx - 0.8f, topY, cz + 0.9f), seed + 32, 1f);
            AddProp(layout, clusterIndex, ScenePropKind.Barrel, new Vector3(cx + 0.9f, topY, cz - 0.9f), seed + 33, 1f);
        }

        static void AddProp(SceneKitLayout layout, int clusterIndex, ScenePropKind kind, Vector3 pos,
            int seed, float scale)
        {
            SceneKitMaterial material = SceneKitMaterial.Wood;
            if (kind == ScenePropKind.Palm)
                material = SceneKitMaterial.Foliage;
            else if (kind == ScenePropKind.Crate || kind == ScenePropKind.Barrel)
                material = SceneKitMaterial.WoodDark;
            else if (kind == ScenePropKind.Anchor)
                material = SceneKitMaterial.Metal;

            layout.Add(new KitPart(SceneKitPiece.Prop, material, pos,
                SceneArtHash.Hash01(seed, 1, 3) * 360f, scale, 0f, 0f, kind).WithCluster(clusterIndex));
        }
    }
}
