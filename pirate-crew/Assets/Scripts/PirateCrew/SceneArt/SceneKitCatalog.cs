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
        /// 把 level_1 的平台簇映射成整套 kit 摆放 = <c>BuildFor(1, BuildLevel1(), seed)</c> 的等价委托
        /// （既有测试与烘焙沿用它，行为逐值不变）。
        /// </summary>
        public static SceneKitLayout BuildLevel1(int seed)
        {
            return BuildFor(1, PlatformClusterLayout.BuildLevel1(), seed);
        }

        /// <summary>
        /// 把**任意关**的平台簇映射成整套 kit 摆放（确定性）。簇类型 → 配方的映射：
        ///   · <see cref="PlatformClusterKind.Ship"/> → 船配方（包络宽 ≥10 用 <see cref="LargeShipRecipe"/>，
        ///     否则用 <see cref="SmallBoatRecipe"/>——宽 ≥10 才有 galleon 的体量可言）；**纯木/布索/铁**，无岩无草；
        ///   · <see cref="PlatformClusterKind.SkyIsland"/> → 岛顶岩唇 + 岛缘散岩 + **整片草顶**（非出生簇）；
        ///     大岛再摆 2 棵棕榈作竖向剪影。**不摆木器、不摆小艇**（岛就是岛，船就是船）；
        ///   · <see cref="PlatformClusterKind.TerraceIsland"/> → 沿棱线的分级礁石 + 顶面岩唇 + 沙缘棕榈
        ///     （**不摆木箱木桶**——用户裁决 4 点名禁止"木板上摆草方块"式的混搭）；
        ///   · <c>info.IsSpawnCluster</c>（出生簇）→ 额外一圈收边栏杆（Wood，跨族唯一例外）+ 铁锚；
        ///     船簇出生岛另有木箱/木桶（甲板语汇）。
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

            for (int c = 0; c < map.Clusters.Count; c++)
            {
                PlatformClusterInfo info = map.Clusters[c];
                int clusterIndex = c;

                // 本簇的构件一律打上簇下标（供"每簇材质族纯净性"精确断言）。
                void AddPart(in KitPart part) => layout.Add(part.WithCluster(clusterIndex));

                // 【基准高度】用户裁决 2 给每个簇一个悬浮基准高度（PlatformClusterInfo.BaseHeight），
                // 它已折进 grid 的块高；kit 的摆放高度必须走同一口径，否则船/岛会"沉在岛底平面里"。
                int baseBlocks = info.BaseBlocks;
                float topY = LevelGeometry.GroundTopY + (info.MaxBlocks + baseBlocks) * block;
                float deckY = LevelGeometry.GroundTopY + (info.MinBlocks + baseBlocks) * block;
                float cx = (info.X0 + info.X1 + 1f) * 0.5f;
                float cz = (info.Z0 + info.Z1 + 1f) * 0.5f;
                int width = info.X1 - info.X0 + 1;
                int depth = info.Z1 - info.Z0 + 1;

                if (info.Kind == PlatformClusterKind.Ship)
                {
                    // 大船：主簇用 galleon 配方，铺在甲板面上（甲板 = 簇内最低台阶顶面）。
                    ShipRecipe recipe = width >= 10 ? LargeShipRecipe : SmallBoatRecipe;
                    List<KitPart> ship = BuildShip(recipe, new Vector3(cx, deckY, cz), 0f, seed + c);
                    for (int i = 0; i < ship.Count; i++)
                        AddPart(ship[i]);

                    // 船头高台加一圈栏杆，读得出"艏楼"。
                    AddPart(new KitPart(SceneKitPiece.Bulwark, SceneKitMaterial.Wood,
                        new Vector3(info.X1 - 1f, topY + 0.22f, cz), 90f, 1f,
                        depth - 0.3f, 0.4f));

                    AddSpawnAccent(layout, info, clusterIndex, topY, width, seed + c);
                    continue;
                }

                if (info.Kind == PlatformClusterKind.SkyIsland)
                {
                    // 空岛 = 岩体 + 岛顶岩唇 + **整片草顶**（用户裁决 4：草只在顶面整片，不是方块摆件）。
                    float rx = width * 0.5f - 0.05f;
                    float rz = depth * 0.5f - 0.05f;
                    AddPart(new KitPart(SceneKitPiece.IslandTop, SceneKitMaterial.Rock,
                        new Vector3(cx, topY, cz), 0f, 1f, rx, rz));

                    // 岩块沿岛缘（确定性取样，不抢中心）。
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i / 6f * Mathf.PI * 2f + SceneArtHash.SignedHash(seed, c, i) * 0.4f;
                        AddPart(new KitPart(SceneKitPiece.RockChunk, SceneKitMaterial.Rock,
                            new Vector3(cx + Mathf.Cos(ang) * rx * 0.9f, topY, cz + Mathf.Sin(ang) * rz * 0.9f),
                            SceneArtHash.Hash01(seed, i, c) * 360f,
                            0.28f + SceneArtHash.Hash01(seed, i, c + 7) * 0.25f, 0f, 0f));
                    }

                    // 出生岛是"沙台地 + 栏杆"，不铺草（语义交给地形壳的沙/草/岩高度混合）。
                    if (!info.IsSpawnCluster)
                        AddGrassCap(layout, clusterIndex, width, depth, cx, cz, topY);

                    // 棕榈只作大岛的竖向剪影（Foliage，与岩族同族）。
                    if (width > 5 && depth > 2)
                    {
                        AddProp(layout, clusterIndex, ScenePropKind.Palm,
                            new Vector3(cx - 1.5f, topY, cz - 1.0f), seed + 1, 4.5f);
                        AddProp(layout, clusterIndex, ScenePropKind.Palm,
                            new Vector3(cx + 1.2f, topY, cz + 1.5f), seed + 2, 5.2f);
                    }

                    AddSpawnAccent(layout, info, clusterIndex, topY, width, seed + c);
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
                        AddPart(new KitPart(SceneKitPiece.RockChunk, SceneKitMaterial.Rock,
                            new Vector3(gx + 0.5f,
                                LevelGeometry.GroundTopY + (blocks + baseBlocks) * block, gz + 0.5f),
                            SceneArtHash.Hash01(gx, gz, 79) * 360f, r, 0f, 0f));
                    }
                }

                // 梯田顶面一圈岩唇（沙岩梯田的收边）。
                AddPart(new KitPart(SceneKitPiece.IslandTop, SceneKitMaterial.Rock,
                    new Vector3(cx, topY, cz), 0f, 1f, width * 0.5f - 0.05f, depth * 0.5f - 0.05f));

                // 梯田陈设（用户裁决 4：「草只长在沙边上，不叠木板上」）：
                // 原实现摆 Palm + Crate + Barrel（木箱木桶叠在岩台上 = 被点名的"混搭"），
                // 现只留沙缘植被（Foliage），木器交给船簇（Ship 的甲板语汇）。
                AddProp(layout, clusterIndex, ScenePropKind.Palm,
                    new Vector3(info.X0 + 1.5f, topY, cz), seed + 11, 4.8f);
                AddProp(layout, clusterIndex, ScenePropKind.Palm,
                    new Vector3(info.X1 - 1.5f, topY, cz - 1f), seed + 14, 5.5f);
                if (depth >= 5)
                    AddProp(layout, clusterIndex, ScenePropKind.Palm,
                        new Vector3(cx, topY, info.Z1 - 1f), seed + 15, 4.2f);

                AddSpawnAccent(layout, info, clusterIndex, topY, width, seed + c);
            }

            return layout;
        }

        /// <summary>
        /// 空岛顶面的**整片草顶**：用 0.5 单位宽的平行草条铺满顶面。
        ///
        /// 【为什么复用 <see cref="SceneKitPiece.DeckPlank"/>】<c>SceneKitComposer</c> 是本轮禁改文件，
        /// 新增 <see cref="SceneKitPiece"/> 枚举值会走进 switch 的 default 分支而**静默无几何**
        /// （看不见的构件比"名字不贴切"危险得多）。DeckPlank 的几何就是"顶面贴地的一块薄板"，
        /// 语义完全够用：换 Foliage 材质即为草皮。
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
