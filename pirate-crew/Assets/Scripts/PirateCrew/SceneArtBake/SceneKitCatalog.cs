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
    ///   · <see cref="BuildCompleteShip"/> 把配方展开成 <see cref="KitPart"/>（放样船体 + 甲板 + 桅 +
    ///     索具 + 帆 + 瞭望巢 + 舰炮），供编辑器烘焙器（<c>SceneArtBaker</c>）展开成 prefab。
    ///
    /// 【为什么是"注册表 + 配方"】<see cref="SceneKitComposer"/> 只认构件种类，不认具体船只；
    /// 换主题 = 换配方 / 换材质，构件几何完全复用（行业 kit 做法的核心收益）。
    ///
    /// 【一代退场（2026-09-18）+ 管线合并（2026-09-19）】按平台簇归并装配（BuildFor/BuildGroups）、
    /// 桅位提取（MastOffsetsFor）等一代管线随原版 tile 地图退役——运行时消费的是烘焙 prefab，
    /// 配方展开只发生在编辑器烘焙期。
    ///
    /// 【提案/待定】尺寸与簇→配方映射是 AI 按用户诉求给出，未确认。
    /// </summary>
    public static class SceneKitCatalog
    {
        /// <summary>大船配方（战场主簇：长条形 15×8 格，甲板高 2 块起）。
        /// 【格 1→2 单位】全部世界尺寸 ×2（桅数/炮数等无量纲量不动）。</summary>
        public static readonly ShipRecipe LargeShipRecipe = new ShipRecipe(
            "galleon", hullLength: 28f, hullBeam: 14f, hullHeight: 2.3f,
            bowLength: 8.4f, sternLength: 6.8f, mastCount: 2, mastHeight: 8.4f,
            hasSails: true, cannonCount: 4);

        /// <summary>小艇配方（侧翼簇 / 小空岛跳板，约为大船一半）。世界尺寸 ×2。</summary>
        public static readonly ShipRecipe SmallBoatRecipe = new ShipRecipe(
            "longboat", hullLength: 14f, hullBeam: 8f, hullHeight: 1.6f,
            bowLength: 4.4f, sternLength: 3.6f, mastCount: 1, mastHeight: 5f,
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

        /// <summary>
        /// 把船配方展开成构件摆放表。船体按真船结构拼：沿长轴分 艏 / 舯（若干段）/ 艉，
        /// 甲板铺板、两舷舷墙、甲板上立桅（横桁 + 索具 + 帆），可选舰炮。
        /// </summary>
        /// <param name="recipe">配方。</param>
        /// <param name="deckCenter">甲板顶面中心世界坐标（y = 甲板面）。</param>
        /// <param name="yawDegrees">绕 Y 朝向。</param>
        /// <param name="seed">确定性抖动种子（经 <see cref="SceneKitComposer"/> 的逐件派生生效）。</param>
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
                hullLength, Mathf.Max(2.8f, hullBeam * 0.55f)));   // 吃深下限 ×2

            // ---- 甲板铺板（沿长轴一条条；可站面由地形格提供，铺板是贴面装饰） ----
            // 板宽 0.9→1.8、舷边留白 0.35→0.7、抬升 0.03→0.06、板长端距 0.4→0.8、板厚 0.06→0.12（世界值 ×2）
            int planks = Mathf.Max(3, Mathf.RoundToInt(hullBeam / 1.2f));   // 1.8→1.2：r11 实测铺板露缝太宽（梯子感），加密到缝宽 < 板宽 1/3
            for (int i = 0; i < planks; i++)
            {
                float lateral = Mathf.Lerp(-halfBeam + 0.7f, halfBeam - 0.7f, (i + 0.5f) / planks);
                parts.Add(new KitPart(SceneKitPiece.DeckPlank, SceneKitMaterial.Wood,
                    P(0f, lateral) + Vector3.up * 0.06f, yawDegrees, 1f,
                    hullLength - 0.8f, 0.12f));
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
                    mastBase, yawDegrees, 1f, 0.28f, recipe.MastHeight));   // 桅径 ×2

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
                    hullBeam * 0.95f, 0.24f));   // 桁径 ×2

                if (recipe.HasSails)
                {
                    parts.Add(new KitPart(SceneKitPiece.Sail, SceneKitMaterial.Cloth,
                        mastBase + Vector3.up * (recipe.MastHeight * 0.55f), yawDegrees + 90f,
                        recipe.MastHeight * 0.5f, recipe.MastHeight * 0.55f, 0f));
                }

                // 桅顶瞭望巢（乌鸦巢）：每桅必有 —— 它是"这是船"的辨认特征之一。
                parts.Add(new KitPart(SceneKitPiece.CrowNest, SceneKitMaterial.Wood,
                    mastBase + Vector3.up * (recipe.MastHeight * 0.94f), yawDegrees, 0.3f,
                    0.72f, 0.52f));   // 鸦巢半径/高 ×2（scale 倍率 0.3 不动）
            }

            // ---- 舰炮（沿两舷，简化：炮管 + 轮座） ----
            for (int c = 0; c < recipe.CannonCount; c++)
            {
                float along = Mathf.Lerp(-halfLen * 0.5f, halfLen * 0.5f,
                    recipe.CannonCount == 1 ? 0.5f : c / (float)(recipe.CannonCount - 1));
                int s = c % 2 == 0 ? -1 : 1;
                parts.Add(new KitPart(SceneKitPiece.Prop, SceneKitMaterial.Metal,
                    P(along, s * (halfBeam - 0.5f)) + Vector3.up * 0.7f,
                    yawDegrees + (s < 0 ? 90f : -90f), 1f, 1.4f, 0.7f));   // 炮管内缩/抬高/尺寸 ×2
            }

            return parts;
        }

    }
}
