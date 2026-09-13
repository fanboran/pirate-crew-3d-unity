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

        public KitPart(SceneKitPiece piece, SceneKitMaterial material, Vector3 position, float yawDegrees,
            float scale, float length, float height, ScenePropKind propKind = default)
        {
            Piece = piece;
            Material = material;
            Position = position;
            YawDegrees = yawDegrees;
            Scale = scale;
            Length = length;
            Height = height;
            PropKind = propKind;
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
    /// 场景**构件注册表**（纯 C# 数据 + 配方展开，无头可测）：
    ///   · <see cref="LargeShipRecipe"/> / <see cref="SmallBoatRecipe"/> 两套船配方（用户要求的"两艘不同尺寸"）；
    ///   · <see cref="BuildShip"/> 把配方展开成 <see cref="KitPart"/>（艏/舯/艉三种船体段 + 甲板 + 栏杆 + 桅 + 索具 + 帆）；
    ///   · <see cref="BuildLevel1"/> 把 level_1 的平台簇映射成构件摆放表（大船簇→大船配方、空岛→岛顶、
    ///     梯田岛→分级岩台、小空岛→侧翼小艇配方）。
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

        /// <summary>从平台簇类型取配方（<c>null</c> = 不是船簇）。</summary>
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
        /// <param name="seed">确定性抖动种子。</param>
        public static List<KitPart> BuildShip(in ShipRecipe recipe, Vector3 deckCenter, float yawDegrees, int seed)
        {
            var parts = new List<KitPart>(64);
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Cos(yawRad), 0f, Mathf.Sin(yawRad));   // 长轴
            Vector3 side = new Vector3(-axis.z, 0f, axis.x);                        // 船宽轴

            float halfLen = recipe.HullLength * 0.5f;
            float halfBeam = recipe.HullBeam * 0.5f;

            // ---- 船体段：艏 / 舯（按 2.4 单位切段）/ 艉 ----
            float bowStart = halfLen - recipe.BowLength;
            float sternStart = -halfLen + recipe.SternLength;

            Vector3 P(float along, float lateral) => deckCenter + axis * along + side * lateral;

            parts.Add(new KitPart(SceneKitPiece.HullBow, SceneKitMaterial.Wood,
                P(-halfLen + recipe.BowLength * 0.5f, 0f), yawDegrees, recipe.HullBeam,
                recipe.BowLength, recipe.HullHeight));

            float midLen = bowStart - sternStart;
            int midSegments = Mathf.Max(1, Mathf.RoundToInt(midLen / 2.4f));
            for (int i = 0; i < midSegments; i++)
            {
                float t = (i + 0.5f) / midSegments;
                parts.Add(new KitPart(SceneKitPiece.HullMid, SceneKitMaterial.Wood,
                    P(Mathf.Lerp(sternStart, bowStart, t), 0f), yawDegrees, recipe.HullBeam,
                    midLen / midSegments, recipe.HullHeight));
            }

            parts.Add(new KitPart(SceneKitPiece.HullStern, SceneKitMaterial.Wood,
                P(halfLen - recipe.SternLength * 0.5f, 0f), yawDegrees, recipe.HullBeam,
                recipe.SternLength, recipe.HullHeight));

            // ---- 甲板铺板（沿长轴一条条） ----
            int planks = Mathf.Max(3, Mathf.RoundToInt(recipe.HullBeam / 0.9f));
            for (int i = 0; i < planks; i++)
            {
                float lateral = Mathf.Lerp(-halfBeam + 0.35f, halfBeam - 0.35f, (i + 0.5f) / planks);
                parts.Add(new KitPart(SceneKitPiece.DeckPlank, SceneKitMaterial.Wood,
                    P(0f, lateral) + Vector3.up * 0.03f, yawDegrees, 1f,
                    recipe.HullLength - 0.4f, 0.06f));
            }

            // ---- 两舷舷墙 / 栏杆 ----
            for (int s = -1; s <= 1; s += 2)
            {
                parts.Add(new KitPart(SceneKitPiece.Bulwark, SceneKitMaterial.Wood,
                    P(0f, s * (halfBeam - 0.08f)) + Vector3.up * 0.22f, yawDegrees, 1f,
                    recipe.HullLength - 0.3f, 0.42f));
            }

            // ---- 桅杆 + 索具 + 帆 ----
            for (int m = 0; m < recipe.MastCount; m++)
            {
                float along = recipe.MastCount == 1
                    ? 0f
                    : Mathf.Lerp(-halfLen * 0.45f, halfLen * 0.35f, m / (float)(recipe.MastCount - 1));
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

                if (recipe.HasSails)
                {
                    parts.Add(new KitPart(SceneKitPiece.Sail, SceneKitMaterial.Cloth,
                        mastBase + Vector3.up * (recipe.MastHeight * 0.55f), yawDegrees + 90f,
                        recipe.MastHeight * 0.5f, recipe.MastHeight * 0.55f, 0f));
                }
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
        /// 把 level_1 的平台簇映射成整套 kit 摆放（确定性）。
        /// 大船簇 → <see cref="LargeShipRecipe"/>；空岛簇 → 岛顶岩台 + 岩块 + 陈设；
        /// 梯田岛簇 → 分级岩台 + 棱线岩块 + 陈设；小空岛 → 侧翼小艇配方。
        /// </summary>
        public static SceneKitLayout BuildLevel1(int seed)
        {
            var layout = new SceneKitLayout();
            PlatformMap map = PlatformClusterLayout.BuildLevel1();
            float block = TerrainCatalog.DefaultBlockWorldHeight;

            for (int c = 0; c < map.Clusters.Count; c++)
            {
                PlatformClusterInfo info = map.Clusters[c];
                float topY = LevelGeometry.GroundTopY + info.MaxBlocks * block;
                float deckY = LevelGeometry.GroundTopY + info.MinBlocks * block;
                float cx = (info.X0 + info.X1 + 1f) * 0.5f;
                float cz = (info.Z0 + info.Z1 + 1f) * 0.5f;

                if (info.Kind == PlatformClusterKind.Ship)
                {
                    // 大船：主簇用 galleon 配方，铺在甲板面上（甲板 = 簇内最低台阶顶面）。
                    List<KitPart> ship = BuildShip(LargeShipRecipe, new Vector3(cx, deckY, cz), 0f, seed + c);
                    for (int i = 0; i < ship.Count; i++)
                        layout.Add(Offset(ship[i], 0f));

                    // 船头高台加一圈栏杆，读得出"艏楼"。
                    layout.Add(new KitPart(SceneKitPiece.Bulwark, SceneKitMaterial.Wood,
                        new Vector3(info.X1 - 1f, topY + 0.22f, cz), 90f, 1f,
                        (info.Z1 - info.Z0 + 1f) - 0.3f, 0.4f));
                    continue;
                }

                if (info.Kind == PlatformClusterKind.SkyIsland)
                {
                    float rx = (info.X1 - info.X0 + 1f) * 0.5f - 0.05f;
                    float rz = (info.Z1 - info.Z0 + 1f) * 0.5f - 0.05f;
                    layout.Add(new KitPart(SceneKitPiece.IslandTop, SceneKitMaterial.Rock,
                        new Vector3(cx, topY, cz), 0f, 1f, rx, rz));

                    // 岩块沿岛缘（确定性取样，不抢中心）。
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i / 6f * Mathf.PI * 2f + SceneArtHash.SignedHash(seed, c, i) * 0.4f;
                        layout.Add(new KitPart(SceneKitPiece.RockChunk, SceneKitMaterial.Rock,
                            new Vector3(cx + Mathf.Cos(ang) * rx * 0.9f, topY, cz + Mathf.Sin(ang) * rz * 0.9f),
                            SceneArtHash.Hash01(seed, i, c) * 360f,
                            0.28f + SceneArtHash.Hash01(seed, i, c + 7) * 0.25f, 0f, 0f));
                    }

                    // 岛上陈设：东空岛放棕榈 + 蓝旗，小空岛放一艘小艇。
                    if (info.X0 >= 42)
                    {
                        AddProp(layout, ScenePropKind.Palm, new Vector3(cx - 1.5f, topY, cz - 1.0f), seed + 1, 4.5f);
                        AddProp(layout, ScenePropKind.Palm, new Vector3(cx + 1.2f, topY, cz + 1.5f), seed + 2, 5.2f);
                        AddProp(layout, ScenePropKind.Anchor, new Vector3(cx + 2.0f, topY, cz - 2.0f), seed + 3, 1f);
                    }
                    else
                    {
                        List<KitPart> boat = BuildShip(SmallBoatRecipe,
                            new Vector3(cx, LevelGeometry.GroundTopY + info.MinBlocks * block, cz), 15f, seed + c);
                        for (int i = 0; i < boat.Count; i++)
                            layout.Add(boat[i]);
                    }
                    continue;
                }

                // 梯田小岛：沿每级台阶的棱线撒**不规则礁石**（3 种形/朝向/尺度，见
                // SceneKitGeometry.AddRockChunk），而不是逐格叠同款 IslandTop 岩台——
                // 旧写法对每个棱线格调一次 AddIslandTop（每次都铺一圈 10 个盒子），
                // 一个台面就叠出上百个同形石块，r2 诊断读成"奶酪楔岩块层层码叠 / 撕碎的卡纸"。
                for (int gz = info.Z0; gz <= info.Z1; gz++)
                {
                    for (int gx = info.X0; gx <= info.X1; gx++)
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
                        layout.Add(new KitPart(SceneKitPiece.RockChunk, SceneKitMaterial.Rock,
                            new Vector3(gx + 0.5f, LevelGeometry.GroundTopY + blocks * block, gz + 0.5f),
                            SceneArtHash.Hash01(gx, gz, 79) * 360f, r, 0f, 0f));
                    }
                }

                // 梯田陈设：棕榈 + 桶箱（顶层）。
                float terraceTopY = topY;
                AddProp(layout, ScenePropKind.Palm, new Vector3(info.X0 + 3f, terraceTopY, cz), seed + 11, 4.8f);
                AddProp(layout, ScenePropKind.Crate, new Vector3(cx, terraceTopY, cz + 1.5f), seed + 12, 1f);
                AddProp(layout, ScenePropKind.Barrel, new Vector3(cx + 1.5f, terraceTopY, cz - 1.5f), seed + 13, 1f);
                AddProp(layout, ScenePropKind.Palm, new Vector3(info.X1 - 2f, terraceTopY, cz - 2f), seed + 14, 5.5f);
            }

            return layout;
        }

        static KitPart Offset(in KitPart p, float dy)
        {
            return new KitPart(p.Piece, p.Material, p.Position + Vector3.up * dy, p.YawDegrees,
                p.Scale, p.Length, p.Height, p.PropKind);
        }

        static void AddProp(SceneKitLayout layout, ScenePropKind kind, Vector3 pos, int seed, float scale)
        {
            SceneKitMaterial material = SceneKitMaterial.Wood;
            if (kind == ScenePropKind.Palm)
                material = SceneKitMaterial.Foliage;
            else if (kind == ScenePropKind.Crate || kind == ScenePropKind.Barrel)
                material = SceneKitMaterial.WoodDark;
            else if (kind == ScenePropKind.Anchor)
                material = SceneKitMaterial.Metal;

            layout.Add(new KitPart(SceneKitPiece.Prop, material, pos,
                SceneArtHash.Hash01(seed, 1, 3) * 360f, scale, 0f, 0f, kind));
        }
    }
}
