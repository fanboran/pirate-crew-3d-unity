using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 大海域世界地图目录（8 张，语义设计，M4）。
    /// 设计契约与逐图语义：docs/M4-大海域世界化.md §2（【提案】名单与布局；【裁决】语义化/连通性/尺度）。
    ///
    /// 【坐标】世界系（Y-up，米）：地图占据 [0..SpanX]×[0..SpanZ]，布局围绕图心对称展开。
    /// 【连通性】所有承载出生点的站面 box 必须同连通分量（<see cref="WorldMapRules"/> BFS，
    /// 由 WorldMapConnectivityTests 断言）；图内有意留了少量不承载出生点的孤礁作氛围。
    /// 【关卡号】占用 101–108，与原版转写 1–33 不冲突。
    /// </summary>
    public static class WorldMapCatalog
    {
        public const int FirstLevelNumber = 101;

        // 【初始化顺序】_all/_byId/_byLevel 必须声明在地图字段之前：地图字段的初始化器会调 Register
        // 入表（C# 按声明顺序执行静态字段初始化器；跨文件顺序不确定的坑见 LevelCatalog 同款注释）。
        static readonly List<WorldMapDefinition> _all = new List<WorldMapDefinition>(8);
        static readonly Dictionary<string, WorldMapDefinition> _byId = new Dictionary<string, WorldMapDefinition>(8);
        static readonly Dictionary<int, WorldMapDefinition> _byLevelNumber = new Dictionary<int, WorldMapDefinition>(8);

        public static IReadOnlyList<WorldMapDefinition> All => _all;
        public static int Count => _all.Count;

        public static bool TryGet(string id, out WorldMapDefinition map) => _byId.TryGetValue(id, out map);

        public static bool TryGetByLevelNumber(int levelNumber, out WorldMapDefinition map)
            => _byLevelNumber.TryGetValue(levelNumber, out map);

        static WorldMapDefinition Register(WorldMapDefinition map)
        {
            _all.Add(map);
            _byId.Add(map.Id, map);
            _byLevelNumber.Add(map.LevelNumber, map);
            return map;
        }

        // ------------------------------------------------------------------
        // 通用件
        // ------------------------------------------------------------------

        /// <summary>战役惯例武器：全员樱桃炸弹×∞。</summary>
        static List<WeaponStack> CherryBombOnly() => new List<WeaponStack>
        {
            new WeaponStack(WeaponId.CherryBomb, 10),
        };

        /// <summary>船长加炸药×5（沿袭 level_1 转写惯例）。</summary>
        static List<WeaponStack> CaptainWeapons() => new List<WeaponStack>
        {
            new WeaponStack(WeaponId.CherryBomb, 10),
            new WeaponStack(WeaponId.Dynamite, 5),
        };

        // ------------------------------------------------------------------
        // 1. wreck_hymn 搁浅圣母号（150×150，3v3，正午）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition WreckHymn = Register(new WorldMapDefinition(
            id: "wreck_hymn", displayName: "搁浅圣母号", levelNumber: 101,
            spanX: 150f, spanZ: 150f, ambientTier: "Noon",
            terrain: new[]
            {
                // 【语义】圣母号斜着撞上礁滩后断成两截，倒下的主桅横跨断口；两队的本阵分踞
                // 西/东两块梯田岛，四周散着搁浅时甩出去的沙洲、礁阶与海蚀柱。
                // 【为什么两岛同款却不算镜像】底座同资产是为**资源对等**（可用面积/高度/掩体量），
                // 破镜像交给三处：两岛反向微转（+10° / −14°）、断船两截的长度与朝向不同、
                // 各自的外围件完全不同（西侧沙洲链 vs 东侧礁盘 + 大岛）。形状不镜像、资源对等。
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 54f, 74f, 10f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 96f, 76f, -14f),
                // 断船：艏段（西，艏朝西北）/ 艉段（东，艉朝东南）/ 倒桅横跨断口
                new WorldKitPlacement("Marine", "WreckBowHalf", 40f, 79f, 96f),
                new WorldKitPlacement("Marine", "WreckSternHalf", 112f, 70f, 78f),
                new WorldKitPlacement("Marine", "MastBridge", 76f, 74f, 88f),
                // 红队外围：南沙洲 + 西北沙洲 + 北礁阶（三件不同资产，与蓝队侧不构成镜像）
                new WorldKitPlacement("Archipelago", "SandBarL", 44f, 50f, 25f),
                new WorldKitPlacement("Archipelago", "SandBarL", 30f, 96f, 340f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 58f, 106f, 270f),
                // 蓝队外围：东礁盘 + 南礁阶 + 东北大岛（搁浅时被甩出去的礁台）
                new WorldKitPlacement("Archipelago", "AtollCore", 118f, 62f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 96f, 44f, 90f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 110f, 118f, 0f),
                // 北孤岛（钟塔残基）+ 西南/东北两座海蚀柱（把内容铺到图的四角）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 76f, 128f, 12f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 22f, 40f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 132f, 112f, 0f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleM", 75f, -60f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleS", -50f, 190f, 30f),
                new WorldKitPlacement("Horizon", "CloudBankL", 150f, 30f, 0f),
                new WorldKitPlacement("Horizon", "FarFleet", 200f, 120f, 0f),
            },
            horizonFeatures: new[] { "WhaleSurfacing" },
            props: new[]
            {
                // 红队本阵（西岛）：营火 + 炮位朝东南（面向战场）
                new WorldPropPlacement("Campfire", 50f, 0.5f, 72f, 0f),
                new WorldPropPlacement("CannonEmplacement", 58f, 0.5f, 71f, 200f),
                new WorldPropPlacement("CrateStack", 51f, 0.5f, 78f, 0f),
                // 蓝队本阵（东岛）：营火 + 炮位朝西 + 船轮柱（本阵挂在艉段旁）
                new WorldPropPlacement("Campfire", 100f, 0.5f, 80f, 0f),
                new WorldPropPlacement("CannonEmplacement", 92f, 0.5f, 71f, 160f),
                new WorldPropPlacement("ShipWheelPost", 101f, 0.5f, 72f, 0f),
                // 断船上的宝箱堆（争夺目标）+ 倒桅上的瞭望残件
                new WorldPropPlacement("TreasureMound", 80f, 1.5f, 74f, 0f),
                new WorldPropPlacement("BarrelWood", 72f, 1.5f, 74f, 0f),
                // 北孤岛（钟塔残基）：宝箱 + 棕榈
                new WorldPropPlacement("TreasureMound", 76f, 0.5f, 126f, 0f),
                new WorldPropPlacement("PalmTall", 70f, 0.5f, 132f, 15f),
                new WorldPropPlacement("RuinColumnBroken", 82f, 0.5f, 130f, 30f),
                // 东北大岛：搁浅幸存者的营地遗迹
                new WorldPropPlacement("Campfire", 104f, 0.5f, 116f, 0f),
                new WorldPropPlacement("RuinArch", 118f, 0.5f, 124f, 0f),
                new WorldPropPlacement("PalmLean", 116f, 0.5f, 110f, 0f),
                new WorldPropPlacement("GrassTuft", 100f, 0.5f, 128f, 0f),
                new WorldPropPlacement("RockM", 124f, 0.5f, 116f, 0f),
                // 外围：沙洲上的浮木与碎箱（搁浅甩出来的船货）
                new WorldPropPlacement("Driftwood", 42f, 0.5f, 48f, 60f),
                new WorldPropPlacement("BarrelWood", 34f, 0.5f, 96f, 0f),
                new WorldPropPlacement("CrateStack", 46f, 0.5f, 52f, 20f),
                new WorldPropPlacement("PalmDead", 30f, 0.5f, 100f, 0f),
                new WorldPropPlacement("RockS", 96f, 0.5f, 42f, 0f),
                new WorldPropPlacement("RockM", 118f, 0.5f, 60f, 0f),
                new WorldPropPlacement("AnchorMonument", 58f, 0.5f, 106f, 0f),
            },
            spawns: new[]
            {
                // 红队 ×3：西岛，三角形站位（船长居中靠后）
                new WorldMapSpawn(0, "redPirate", 50f, 72f, 5),
                new WorldMapSpawn(0, "redPirate", 56.6f, 76.2f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 54f, 79f, 5),
                // 蓝队 ×3：东岛，一字排开（与红队的三点站位刻意不同）
                new WorldMapSpawn(1, "bluePirate", 92f, 74f, 2),
                new WorldMapSpawn(1, "bluePirate", 98f, 76f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 104f, 78f, 2),
            },
            airdropPool: new[]
            {
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.Dynamite, 10),
            },
            horizonSeed: 101));

        // ------------------------------------------------------------------
        // 2. atoll_ring 环礁（190×190，4v4，正午）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition AtollRing = Register(new WorldMapDefinition(
            id: "atoll_ring", displayName: "环礁", levelNumber: 102,
            spanX: 190f, spanZ: 190f, ambientTier: "Noon",
            terrain: new[]
            {
                // 【语义】外环礁三面抱水、东北留一道口子（船能开进来的那条道）；泻湖里散着礁盘、
                // 红树林滩与两座本阵梯田岛，环外四向再挂一圈外礁——把 190×190 的图面铺满，
                // 不让内容缩在图心一小块（原布局 51% 跨度、8.9% 覆盖，实测见审计 §一.2）。
                // 本阵 M 岛贴环礁带（西压弧带/东压门口栈道），接敌 1-2 跳
                new WorldKitPlacement("Archipelago", "AtollArcA", 95f, 95f, 270f),
                new WorldKitPlacement("Archipelago", "AtollArcA", 95f, 95f, 0f),
                new WorldKitPlacement("Archipelago", "AtollArcA", 95f, 95f, 180f),
                new WorldKitPlacement("Archipelago", "AtollCore", 95f, 95f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 78f, 108f, 20f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 112f, 82f, 200f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 135f, 70f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 135f, 120f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 133f, 95f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 60f, 95f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 130f, 95f, 0f),
                // 泻湖内的两片礁盘 + 南北礁阶：把环内水域变成"有落脚点的浅湖"
                new WorldKitPlacement("Archipelago", "AtollCore", 60f, 118f, 0f),
                new WorldKitPlacement("Archipelago", "AtollCore", 130f, 74f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 95f, 130f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 95f, 62f, 180f),
                // 环外的外礁四向铺开（西南/东北两座梯田岛 + 北礁台 + 南沙洲）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 34f, 60f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 156f, 130f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 95f, 160f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 95f, 30f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 48f, 148f, 30f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 146f, 44f, 200f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 24f, 95f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 166f, 95f, 0f),
                // 环内外的踏脚礁阶（把四向的外礁接进环礁带，跳距一律 ≤13u）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 95f, 44f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 58f, 138f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 146f, 58f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 38f, 95f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 152f, 95f, 0f),
                // 北端再挂一座梯田岛（把环礁带往北推满，覆盖补到 15% 线以上）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 95f, 178f, 0f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleL", 95f, -80f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleS", 280f, 150f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 40f, 230f, 0f),
                new WorldKitPlacement("Horizon", "FarFleet", 260f, 95f, 0f),
            },
            horizonFeatures: new[] { "LeviathanTentacle", "WhaleSurfacing" },
            props: new[]
            {
                // 红队外围（西南梯田岛）：前哨营火 + 炮位朝东北（面向环内战场）
                new WorldPropPlacement("Campfire", 32f, 0.5f, 58f, 0f),
                new WorldPropPlacement("CannonEmplacement", 40f, 0.5f, 63f, 250f),
                new WorldPropPlacement("PalmDead", 28f, 0.5f, 66f, 0f),
                // 蓝队外围（东北梯田岛）：前哨营火 + 炮位朝西南 + 船轮柱
                new WorldPropPlacement("CannonEmplacement", 152f, 0.5f, 126f, 90f),
                new WorldPropPlacement("ShipWheelPost", 160f, 0.5f, 134f, 0f),
                new WorldPropPlacement("Campfire", 150f, 0.5f, 136f, 0f),
                new WorldPropPlacement("PalmTall", 92f, 0.5f, 93f, 30f),
                new WorldPropPlacement("PalmDead", 99f, 0.5f, 98f, 180f),
                new WorldPropPlacement("FernClump", 74f, 1.0f, 106f, 0f),
                new WorldPropPlacement("FernClump", 116f, 1.0f, 84f, 90f),
                new WorldPropPlacement("TreasureMound", 95f, 1.0f, 96f, 0f),
                new WorldPropPlacement("RockM", 130f, 0.5f, 90f, 0f),
                new WorldPropPlacement("RockS", 137f, 1.0f, 98f, 0f),
                new WorldPropPlacement("BarrelWood", 24f, 0.5f, 98f, 0f),
            },
            spawns: new[]
            {
                new WorldMapSpawn(0, "redPirate", 57f, 93f, 5),
                new WorldMapSpawn(0, "redPirate", 62f, 97f, 5),
                new WorldMapSpawn(0, "redPirate", 59.5f, 98f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 62f, 92f, 5),
                new WorldMapSpawn(1, "bluePirate", 127f, 93f, 2),
                new WorldMapSpawn(1, "bluePirate", 132f, 97f, 2),
                new WorldMapSpawn(1, "bluePirate", 129.5f, 98f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 132f, 92f, 2),
            },
            airdropPool: new[]
            {
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.ParachuteBomb, 10),
                new WeaponStack(WeaponId.Anchor, 1),
                new WeaponStack(WeaponId.Seagull, 1),
            },
            horizonSeed: 102));

        // ------------------------------------------------------------------
        // 3. ghost_harbor 鬼火港（220×220，4v4，黄昏）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition GhostHarbor = Register(new WorldMapDefinition(
            "ghost_harbor", displayName: "鬼火港", levelNumber: 103,
            spanX: 220f, spanZ: 220f, ambientTier: "Dusk",
            terrain: new[]
            {
                // 【语义】鬼火港是一座沉了一半的港口城：中央长码头（栈桥三段 + 沉没广场 + 灯塔岛）
                // 是主战线，港湾东西两侧各搁着半条沉船；港外的岛链（西北/东南两座大岛、北岛、
                // 东西外礁）是当年进出港的锚地，如今只剩礁台与断柱。
                // （栈桥长轴沿本地 Z，yaw 90 转成东西向；沉没广场中带 z 55.4..64.6 是死亡水池）
                new WorldKitPlacement("Marine", "PierLong", 85f, 110f, 90f),
                new WorldKitPlacement("Marine", "PierHead", 105f, 110f, 0f),
                new WorldKitPlacement("Marine", "PierLong", 125f, 110f, 90f),
                new WorldKitPlacement("Archipelago", "SunkenPlaza", 105f, 60f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 105f, 85f, 90f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 105f, 128f, 0f),
                new WorldKitPlacement("Marine", "LighthouseTower", 105f, 150f, 0f),
                // 港湾两侧沉船（断口朝图心）
                new WorldKitPlacement("Marine", "WreckBowHalf", 40f, 90f, 90f),
                new WorldKitPlacement("Marine", "WreckSternHalf", 170f, 90f, 90f),
                new WorldKitPlacement("Archipelago", "SandBarL", 58f, 100f, 20f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 152f, 98f, 0f),
                // 港外岛链：西北大岛（老锚地）+ 东南大岛（新城址）+ 北岛（灯塔的备用台地）
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 60f, 165f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 170f, 60f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 110f, 192f, 0f),
                // 东西两座外礁岛（封住港口的出入口，把 220×220 的图面撑满）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 32f, 105f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 192f, 150f, 0f),
                // 港口南口的沙洲与礁台（进港水道两侧的浅滩）
                new WorldKitPlacement("Archipelago", "SandBarL", 60f, 40f, 20f),
                new WorldKitPlacement("Archipelago", "SandBarL", 165f, 190f, 340f),
                new WorldKitPlacement("Archipelago", "AtollCore", 105f, 25f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 150f, 30f, 30f),
                // 灯塔台地两侧的礁阶（从码头爬上灯塔岛的踏脚）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 95f, 132f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 152f, 132f, 0f),
                // 港外两座海蚀柱（远看是航标，近看是废墟）
                new WorldKitPlacement("Archipelago", "SeaStackShort", 25f, 60f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 200f, 105f, 0f),
                // 港外岛链之间的进港水道踏脚（每条水道两到三级礁阶，跳距 ≤13u）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 110f, 174f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 194f, 112f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 196f, 128f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 105f, 42f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 128f, 130f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 85f, 52f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 165f, 172f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 40f, 62f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 185f, 105f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 110f, 165f, 0f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleM", 110f, -70f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleS", -60f, 260f, 20f),
                new WorldKitPlacement("Horizon", "CloudBankL", 40f, 40f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 200f, 180f, 30f),
                new WorldKitPlacement("Horizon", "FarFleet", 330f, 110f, 0f),
            },
            horizonFeatures: new[] { "GiantRibs", "WhaleSurfacing" },
            props: new[]
            {
                new WorldPropPlacement("Campfire", 100f, 0.5f, 69f, 0f),
                new WorldPropPlacement("CannonEmplacement", 112f, 0.5f, 52f, 120f),
                new WorldPropPlacement("RuinColumnBroken", 98f, 0.5f, 64f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 110f, 0.5f, 68f, 40f),
                new WorldPropPlacement("RuinArch", 105f, 0.5f, 47f, 0f),
                new WorldPropPlacement("ShipWheelPost", 105f, 1.0f, 112f, 0f),
                new WorldPropPlacement("CrateStack", 88f, 1.0f, 111f, 0f),
                new WorldPropPlacement("CrateStack", 126f, 1.0f, 109f, 90f),
                new WorldPropPlacement("BarrelWood", 82f, 1.0f, 110f, 0f),
                new WorldPropPlacement("BarrelWood", 122f, 1.0f, 110f, 0f),
                new WorldPropPlacement("Campfire", 102f, 0.5f, 152f, 0f),
                new WorldPropPlacement("TreasureMound", 108f, 0.5f, 148f, 0f),
                new WorldPropPlacement("PalmDead", 96f, 0.5f, 70f, 0f),
                new WorldPropPlacement("FernClump", 148f, 0.5f, 100f, 0f),
                new WorldPropPlacement("GrassTuft", 60f, 0.5f, 98f, 0f),
                new WorldPropPlacement("RockM", 160f, 0.5f, 94f, 0f),
                // 港外岛链：西北老锚地（幸存者营地）+ 东南新城址（废墟街区）+ 北岛台地
                new WorldPropPlacement("Campfire", 56f, 0.5f, 162f, 0f),
                new WorldPropPlacement("CannonEmplacement", 66f, 0.5f, 168f, 210f),
                new WorldPropPlacement("RuinArch", 44f, 0.5f, 170f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 62f, 0.5f, 156f, 30f),
                new WorldPropPlacement("PalmDead", 38f, 0.5f, 158f, 0f),
                new WorldPropPlacement("Campfire", 174f, 0.5f, 56f, 0f),
                new WorldPropPlacement("CannonEmplacement", 164f, 0.5f, 50f, 150f),
                new WorldPropPlacement("RuinArch", 186f, 0.5f, 50f, 0f),
                new WorldPropPlacement("CrateStack", 178f, 0.5f, 66f, 0f),
                new WorldPropPlacement("AnchorMonument", 110f, 0.5f, 190f, 0f),
                new WorldPropPlacement("PalmDead", 102f, 0.5f, 196f, 0f),
                new WorldPropPlacement("Campfire", 30f, 0.5f, 102f, 0f),
                new WorldPropPlacement("RockM", 194f, 0.5f, 152f, 0f),
                new WorldPropPlacement("Driftwood", 62f, 0.5f, 42f, 20f),
                new WorldPropPlacement("BarrelWood", 150f, 0.5f, 32f, 0f),
            },
            spawns: new[]
            {
                // 红队 ×4：沉没广场（北台 z 64.6..73.8 / 南台 z 46.2..55.4，避开中央水池）
                new WorldMapSpawn(0, "redPirate", 95f, 66f, 5),
                new WorldMapSpawn(0, "redPirate", 97f, 71f, 5),
                new WorldMapSpawn(0, "redPirate", 101f, 52f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 108f, 66f, 5),
                // 蓝队 ×4：东侧岛台 ×3 + 艉楼（yaw 90 后艉楼 x 171.8..175.8）
                new WorldMapSpawn(1, "bluePirate", 144f, 94f, 2),
                new WorldMapSpawn(1, "bluePirate", 148f, 103f, 2),
                new WorldMapSpawn(1, "bluePirate", 158f, 93f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 173.5f, 90f, 2),
            },
            airdropPool: new[]
            {
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.RumBottle, 10),
                new WeaponStack(WeaponId.VoodooDoll, 1),
                new WeaponStack(WeaponId.ParachuteBomb, 2),
            },
            horizonSeed: 103));

        // ------------------------------------------------------------------
        // 4. turtle_back 巨龟环脊（240×240，5v5，正午）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition TurtleBack = Register(new WorldMapDefinition(
            id: "turtle_back", displayName: "巨龟环脊", levelNumber: 104,
            spanX: 240f, spanZ: 240f, ambientTier: "Noon",
            terrain: new[]
            {
                // 【语义】巨龟环脊：龟背甲是图心战场，东西两翼各一座大岛是本阵（形状不同：
                // 西翼双岛串成"肩胛"，东翼单岛带礁阶），南北两级礁阶是上背甲的路。
                // 环脊之外再挂四座外岛与两串踏脚礁——原布局内容只占 z 方向 53%，
                // 北/南两片海是无人可到的空水（审计 §一.2）。
                // 中央巨龟背甲 + 东西两翼本阵
                new WorldKitPlacement("Archipelago", "TurtleShellIsle", 120f, 120f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 55f, 120f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 88f, 120f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 190f, 120f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 156f, 120f, 0f),
                // 南北两级礁阶 + 短海蚀柱卫哨（礁阶 ±8 板距实测后内移：保 shell 缘跳距 ≤13）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 120f, 84f, 90f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 120f, 60f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 120f, 156f, 90f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 120f, 180f, 0f),
                // 北/南两端的外岛（龟首与龟尾方向的礁台）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 120f, 35f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 120f, 200f, 0f),
                // 西南大岛（另一座龟背残台）+ 从西翼爬上去的两级踏脚礁
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 35f, 195f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 40f, 80f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 48f, 96f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 72f, 190f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 95f, 185f, 0f),
                // 西侧孤岛（龟群里的另一只小龟）+ 东侧外礁链（把东翼撑到图边）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 25f, 60f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 215f, 165f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 215f, 100f, 90f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 215f, 140f, 90f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 215f, 125f, 90f),
                new WorldKitPlacement("Archipelago", "AtollCore", 200f, 30f, 0f),
                // 东南角外礁的踏脚链（礁阶 → 礁阶 → 海蚀柱 → 东侧沙洲）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 205f, 50f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 210f, 66f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 215f, 78f, 0f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleM", 120f, -60f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleM", 120f, 320f, 180f),
                new WorldKitPlacement("Horizon", "DistantIsleS", -40f, 200f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 30f, 30f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 220f, 210f, 20f),
                new WorldKitPlacement("Horizon", "FarFleet", 340f, 300f, 0f),
            },
            horizonFeatures: new[] { "WhaleSurfacing", "LeviathanTentacle" },
            props: new[]
            {
                new WorldPropPlacement("Campfire", 44f, 0.5f, 118f, 0f),
                new WorldPropPlacement("CannonEmplacement", 62f, 0.5f, 128f, 200f),
                new WorldPropPlacement("Campfire", 196f, 0.5f, 122f, 0f),
                new WorldPropPlacement("CannonEmplacement", 178f, 0.5f, 112f, 160f),
                new WorldPropPlacement("TreasureMound", 115f, 3.5f, 123f, 0f),
                new WorldPropPlacement("PalmTall", 38f, 0.5f, 132f, 0f),
                new WorldPropPlacement("PalmTall", 66f, 0.5f, 108f, 180f),
                new WorldPropPlacement("PalmLean", 178f, 0.5f, 130f, 0f),
                new WorldPropPlacement("PalmDead", 206f, 0.5f, 110f, 0f),
                new WorldPropPlacement("RockL", 126f, 1.0f, 112f, 0f),
                new WorldPropPlacement("RockM", 112f, 2.0f, 126f, 0f),
                new WorldPropPlacement("RockFlat", 128f, 1.0f, 132f, 30f),
                new WorldPropPlacement("GrassTuft", 118f, 2.0f, 116f, 0f),
                new WorldPropPlacement("FernClump", 124f, 3.0f, 118f, 0f),
                new WorldPropPlacement("Driftwood", 88f, 0.5f, 114f, 90f),
                new WorldPropPlacement("BarrelWood", 190f, 0.5f, 126f, 0f),
                // 外岛陈设（北端龟首礁台 / 南端龟尾礁台 / 西南残台 / 东侧外礁）
                new WorldPropPlacement("Campfire", 118f, 0.5f, 38f, 0f),
                new WorldPropPlacement("RockM", 125f, 0.5f, 32f, 0f),
                new WorldPropPlacement("PalmDead", 120f, 0.5f, 203f, 0f),
                new WorldPropPlacement("RockFlat", 113f, 0.5f, 197f, 20f),
                new WorldPropPlacement("Campfire", 40f, 0.5f, 190f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 30f, 0.5f, 200f, 0f),
                new WorldPropPlacement("PalmTall", 46f, 0.5f, 202f, 0f),
                new WorldPropPlacement("GrassTuft", 28f, 0.5f, 188f, 0f),
                new WorldPropPlacement("CannonEmplacement", 52f, 0.5f, 182f, 200f),
                new WorldPropPlacement("Campfire", 214f, 0.5f, 162f, 0f),
                new WorldPropPlacement("CrateStack", 210f, 0.5f, 170f, 0f),
                new WorldPropPlacement("RockM", 25f, 0.5f, 62f, 0f),
                new WorldPropPlacement("FernClump", 30f, 0.5f, 55f, 0f),
                new WorldPropPlacement("PalmLean", 202f, 0.5f, 32f, 0f),
            },
            spawns: new[]
            {
                new WorldMapSpawn(0, "redPirate", 85f, 117f, 5),
                new WorldMapSpawn(0, "redPirate", 91f, 123f, 5),
                new WorldMapSpawn(0, "redPirate", 85f, 121.5f, 5),
                new WorldMapSpawn(0, "redPirate", 91f, 117f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 88f, 120f, 5),
                new WorldMapSpawn(1, "bluePirate", 153f, 117f, 2),
                new WorldMapSpawn(1, "bluePirate", 159f, 123f, 2),
                new WorldMapSpawn(1, "bluePirate", 153f, 121.5f, 2),
                new WorldMapSpawn(1, "bluePirate", 159f, 117f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 156f, 120f, 2),
            },
            airdropPool: new[]
            {
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.Banana, 10),
                new WeaponStack(WeaponId.Dynamite, 10),
                new WeaponStack(WeaponId.Boulder, 2),
                new WeaponStack(WeaponId.Seagull, 2),
                new WeaponStack(WeaponId.TidalWave, 1),
            },
            horizonSeed: 104));

        // ------------------------------------------------------------------
        // 5. mangrove_veil 红树帷幔（180×180，4v4，黄昏）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition MangroveVeil = Register(new WorldMapDefinition(
            id: "mangrove_veil", displayName: "红树帷幔", levelNumber: 105,
            spanX: 180f, spanZ: 180f, ambientTier: "Dusk",
            terrain: new[]
            {
                // 【语义】红树帷幔：一片退潮后露出的红树林泥滩，4×4 的树丛岛交错成棋盘迷宫
                // （隔行错开半格），南北两端各再挂两丛——把迷宫从"图心一块"铺成"整片滩"。
                // 本阵贴迷宫西/东中段，横向接敌
                new WorldKitPlacement("Archipelago", "MangroveHummock", 48f, 45f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 76f, 45f, 25f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 104f, 45f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 132f, 45f, 70f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 62f, 71f, 40f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 90f, 71f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 118f, 71f, 210f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 146f, 71f, 15f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 48f, 97f, 80f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 76f, 97f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 104f, 97f, 300f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 132f, 97f, 45f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 62f, 123f, 10f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 90f, 123f, 190f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 118f, 123f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 146f, 123f, 60f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 36f, 84f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 144f, 84f, 0f),
                // 南北两端的延伸树丛（补 z 方向跨度：原布局内容只占 z 51%）
                new WorldKitPlacement("Archipelago", "MangroveHummock", 48f, 19f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 104f, 19f, 30f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 76f, 149f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 132f, 149f, 45f),
                // 泥滩上的两座干台（退潮时露出的高地，也是迷宫两端唯一的制高点）
                new WorldKitPlacement("Archipelago", "AtollCore", 22f, 34f, 0f),
                new WorldKitPlacement("Archipelago", "AtollCore", 158f, 134f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 40f, 137f, 90f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 140f, 31f, 90f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleS", 90f, -50f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleS", -40f, 160f, 30f),
                new WorldKitPlacement("Horizon", "DistantIsleS", 240f, 120f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 30f, 220f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 160f, -30f, 15f),
                new WorldKitPlacement("Horizon", "FarFleet", 250f, 250f, 0f),
            },
            horizonFeatures: new[] { "LeviathanTentacle" },
            props: new[]
            {
                // 两端延伸树丛上的陈设（红队北滩哨位 / 蓝队南滩哨位）
                new WorldPropPlacement("Campfire", 46f, 0.5f, 20f, 0f),
                new WorldPropPlacement("CannonEmplacement", 100f, 0.5f, 21f, 240f),
                new WorldPropPlacement("Campfire", 130f, 0.5f, 150f, 0f),
                new WorldPropPlacement("CannonEmplacement", 136f, 0.5f, 147f, 60f),
                // 两座干台（退潮露出的高地）+ 迷宫四角的踏脚礁
                new WorldPropPlacement("Campfire", 22f, 0.5f, 34f, 0f),
                new WorldPropPlacement("RockM", 158f, 0.5f, 134f, 0f),
                new WorldPropPlacement("BarrelWood", 40f, 0.5f, 137f, 0f),
                new WorldPropPlacement("CrateStack", 140f, 0.5f, 31f, 0f),
                new WorldPropPlacement("TreasureMound", 104f, 1.0f, 97f, 0f),
                new WorldPropPlacement("FernClump", 46f, 1.0f, 46f, 0f),
                new WorldPropPlacement("FernClump", 78f, 1.0f, 44f, 90f),
                new WorldPropPlacement("FernClump", 92f, 1.0f, 72f, 0f),
                new WorldPropPlacement("FernClump", 110f, 1.0f, 98f, 180f),
                new WorldPropPlacement("GrassTuft", 64f, 1.0f, 124f, 0f),
                new WorldPropPlacement("GrassTuft", 95f, 1.0f, 124f, 90f),
                new WorldPropPlacement("GrassTuft", 148f, 1.0f, 122f, 0f),
                new WorldPropPlacement("Driftwood", 50f, 1.0f, 96f, 20f),
                new WorldPropPlacement("Driftwood", 134f, 1.0f, 72f, 110f),
                new WorldPropPlacement("BarrelWood", 74f, 1.0f, 98f, 0f),
                new WorldPropPlacement("BarrelWood", 108f, 1.0f, 46f, 0f),
                new WorldPropPlacement("RockS", 88f, 1.0f, 46f, 0f),
                new WorldPropPlacement("RockS", 62f, 1.0f, 98f, 0f),
            },
            spawns: new[]
            {
                new WorldMapSpawn(0, "redPirate", 33f, 82f, 5),
                new WorldMapSpawn(0, "redPirate", 39f, 87f, 5),
                new WorldMapSpawn(0, "redPirate", 33f, 85.5f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 39f, 81f, 5),
                new WorldMapSpawn(1, "bluePirate", 141f, 82f, 2),
                new WorldMapSpawn(1, "bluePirate", 147f, 87f, 2),
                new WorldMapSpawn(1, "bluePirate", 141f, 85.5f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 147f, 81f, 2),
            },
            airdropPool: new[]
            {
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.Banana, 10),
                new WeaponStack(WeaponId.Mine, 2),
                new WeaponStack(WeaponId.RumBottle, 2),
            },
            horizonSeed: 105));

        // ------------------------------------------------------------------
        // 6. spiral_throne 螺旋王座（260×260，5v5，正午）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition SpiralThrone = Register(new WorldMapDefinition(
            id: "spiral_throne", displayName: "螺旋王座", levelNumber: 106,
            spanX: 260f, spanZ: 260f, ambientTier: "Noon",
            terrain: new[]
            {
                // 【语义】螺旋王座：一条由外向内收的岛链绕着图心转一圈，尽头是中央的火山口
                // （VolcanoRimA 的环状岩壁 + 口内的平顶礁台）。本阵分踞螺旋中段的龟背要塞（蓝）
                // 与对角的高台岛（红）——原布局两阵相隔 174u（跑满 7 个回合才能接火），
                // 已按"接敌 ≤120u"收拢到 75u 级。
                // 本阵沿螺旋内收一格（远征图，接敌距离减半）
                new WorldKitPlacement("Archipelago", "SandBarL", 95f, 200f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 118f, 193f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 133f, 190f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 163f, 164f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 185f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 185f, 125f, 0f),
                new WorldKitPlacement("Archipelago", "TurtleShellIsle", 215f, 110f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 250f, 82f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 228f, 58f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 160f, 115f, 0f),
                // 中央火山口：环状岩壁 + 口内平顶礁台，两侧接一条冷却的熔岩堤（可跳过去）
                new WorldKitPlacement("Archipelago", "VolcanoRimA", 150f, 55f, 90f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 155f, 82f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 158f, 96f, 0f),
                // 螺旋外侧的三座大岛（远征路线上的补给台）+ 西南高台岛
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 60f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 180f, 220f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 60f, 220f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 30f, 150f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 215f, 215f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 120f, 120f, 45f),
                new WorldKitPlacement("Archipelago", "AtollCore", 120f, 145f, 0f),
                // 螺旋外侧两座大岛的踏脚链（西南大岛 → 西南高台岛）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 95f, 108f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 34f, 132f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 112f, 104f, 0f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleL", 130f, -80f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleM", 380f, 130f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 60f, 300f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 300f, 240f, 25f),
                new WorldKitPlacement("Horizon", "FarFleet", 320f, 40f, 0f),
            },
            horizonFeatures: new[] { "GiantRibs", "LeviathanTentacle" },
            props: new[]
            {
                // 红队本阵（螺旋中段高台岛）+ 后方补给台
                new WorldPropPlacement("Campfire", 159f, 0.5f, 160f, 0f),
                new WorldPropPlacement("CannonEmplacement", 168f, 0.5f, 168f, 210f),
                new WorldPropPlacement("CrateStack", 158f, 0.5f, 169f, 0f),
                new WorldPropPlacement("PalmLean", 112f, 0.5f, 190f, 0f),
                new WorldPropPlacement("BarrelWood", 128f, 0.5f, 188f, 0f),
                new WorldPropPlacement("GrassTuft", 96f, 0.5f, 200f, 0f),
                new WorldPropPlacement("FernClump", 165f, 0.5f, 162f, 0f),
                new WorldPropPlacement("Campfire", 58f, 0.5f, 216f, 0f),
                new WorldPropPlacement("CannonEmplacement", 66f, 0.5f, 224f, 0f),
                // 蓝队本阵（龟背要塞）+ 后方补给台
                new WorldPropPlacement("TreasureMound", 215f, 3.5f, 111f, 0f),
                new WorldPropPlacement("ShipWheelPost", 213f, 2.0f, 106f, 0f),
                new WorldPropPlacement("CannonEmplacement", 222f, 0.5f, 118f, 150f),
                new WorldPropPlacement("Campfire", 226f, 0.5f, 60f, 0f),
                new WorldPropPlacement("CannonEmplacement", 234f, 0.5f, 64f, 90f),
                new WorldPropPlacement("PalmTall", 230f, 0.5f, 52f, 0f),
                new WorldPropPlacement("PalmDead", 222f, 0.5f, 64f, 0f),
                // 螺旋途中的两处遗迹踏脚 + 火山口内的岩台
                new WorldPropPlacement("RuinColumnBroken", 185f, 1.5f, 140f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 177f, 1.0f, 140f, 60f),
                new WorldPropPlacement("RockL", 146f, 0.5f, 29f, 0f),
                new WorldPropPlacement("RockM", 161f, 0.5f, 78f, 0f),
                new WorldPropPlacement("Driftwood", 158f, 0.5f, 115f, 30f),
                // 螺旋外侧三座大岛与西南高台岛的陈设
                new WorldPropPlacement("Campfire", 60f, 0.5f, 100f, 0f),
                new WorldPropPlacement("RockM", 30f, 0.5f, 150f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 180f, 0.5f, 220f, 0f),
                new WorldPropPlacement("PalmDead", 215f, 0.5f, 215f, 0f),
                new WorldPropPlacement("RockFlat", 120f, 0.5f, 145f, 0f),
                new WorldPropPlacement("GrassTuft", 120f, 0.5f, 120f, 0f),
            },
            spawns: new[]
            {
                // 红队 ×5：螺旋中段高台岛（梯田三层，船长占顶层）
                new WorldMapSpawn(0, "redPirate", 159f, 160f, 5),
                new WorldMapSpawn(0, "redPirate", 167f, 168f, 5),
                new WorldMapSpawn(0, "redPirate", 159f, 168f, 5),
                new WorldMapSpawn(0, "redPirate", 167f, 160f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 163f, 170f, 5),
                // 蓝队 ×5：螺旋中段的低平梯田岛（**不放龟背**——龟壳穹顶比站面高 4-5 倍，
                // 出生群若落在穹顶正中，出生机位从后方看过去整幅画面都是壳壁，
                // 实测遮挡比 77.9% 且角色只剩几个像素。龟背留作图心的要塞地标）
                new WorldMapSpawn(1, "bluePirate", 181f, 122f, 2),
                new WorldMapSpawn(1, "bluePirate", 189f, 128f, 2),
                new WorldMapSpawn(1, "bluePirate", 181f, 127f, 2),
                new WorldMapSpawn(1, "bluePirate", 189f, 122f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 185f, 130f, 2),
            },
            airdropPool: new[]
            {
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.Dynamite, 10),
                new WeaponStack(WeaponId.Cannon, 3),
                new WeaponStack(WeaponId.Anchor, 2),
                new WeaponStack(WeaponId.ParachuteBomb, 2),
                new WeaponStack(WeaponId.TidalWave, 1),
            },
            horizonSeed: 106));

        // ------------------------------------------------------------------
        // 7. storm_cape 雷暴岬（200×200，4v4，风暴）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition StormCape = Register(new WorldMapDefinition(
            id: "storm_cape", displayName: "雷暴岬", levelNumber: 107,
            spanX: 200f, spanZ: 200f, ambientTier: "Storm",
            terrain: new[]
            {
                // 【语义】雷暴岬是一道横贯东西的海蚀柱链：柱与柱之间靠栈桥与礁阶勉强连成一条
                // "顶着风浪的窄路"，两端各有一座梯田岛做本阵。柱链之外是四片被浪打散的礁滩
                // （西北/东南两座大岛 + 南北礁台），把 200×200 的图面从"一条线"铺成"一片海岬"。
                // 本阵内移贴柱链两端（103u 接敌）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 55f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 60f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 75f, 100f, 0f),
                new WorldKitPlacement("Marine", "PierLong", 95f, 100f, 90f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 110f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackTall", 130f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 140f, 100f, 0f),
                new WorldKitPlacement("Marine", "PierLong", 158f, 100f, 90f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 158f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 100f, 122f, 0f),
                new WorldKitPlacement("Marine", "LighthouseTower", 100f, 145f, 0f),
                // 柱链两端的红树林滩（封住东西口，红队西口 / 蓝队东口）
                new WorldKitPlacement("Archipelago", "MangroveHummock", 35f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 170f, 110f, 0f),
                // 西北大岛（红队后方锚地）+ 东南大岛（蓝队后方锚地）
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 50f, 45f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 150f, 160f, 0f),
                // 从本阵爬向后方的踏脚礁（各两段：岛 → 柱 → 礁阶 → 礁阶 → 后方大岛）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 52f, 75f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 55f, 88f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 48f, 125f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 44f, 138f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 170f, 72f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 168f, 84f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 168f, 62f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 115f, 155f, 0f),
                // 南北两端的礁台（把内容撑到图的南北边缘）
                new WorldKitPlacement("Archipelago", "SandBarL", 95f, 25f, 0f),
                new WorldKitPlacement("Archipelago", "AtollCore", 100f, 178f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 100f, 160f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 40f, 155f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 165f, 45f, 0f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleM", 100f, -70f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 30f, 30f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 170f, 60f, 40f),
                new WorldKitPlacement("Horizon", "CloudBankL", 90f, 250f, 0f),
                new WorldKitPlacement("Horizon", "FarFleet", -40f, 240f, 0f),
            },
            horizonFeatures: new[] { "LeviathanTentacle", "GiantRibs" },
            props: new[]
            {
                new WorldPropPlacement("Campfire", 30f, 0.5f, 98f, 0f),
                new WorldPropPlacement("CannonEmplacement", 42f, 0.5f, 106f, 230f),
                new WorldPropPlacement("Campfire", 172f, 0.5f, 108f, 0f),
                new WorldPropPlacement("CannonEmplacement", 166f, 0.5f, 112f, 130f),
                new WorldPropPlacement("ShipWheelPost", 92f, 1.0f, 102f, 0f),
                new WorldPropPlacement("CrateStack", 98f, 1.0f, 98f, 0f),
                new WorldPropPlacement("BarrelWood", 162f, 1.0f, 101f, 0f),
                new WorldPropPlacement("RockL", 106f, 0.5f, 118f, 0f),
                new WorldPropPlacement("RockM", 94f, 0.5f, 126f, 0f),
                new WorldPropPlacement("RockS", 140f, 2.5f, 101f, 0f),
                new WorldPropPlacement("GrassTuft", 40f, 0.5f, 94f, 0f),
                new WorldPropPlacement("GrassTuft", 174f, 0.5f, 108f, 0f),
                new WorldPropPlacement("TreasureMound", 130f, 6.0f, 100f, 0f),
                // 西北大岛（红队后方锚地）+ 东南大岛（蓝队后方锚地）
                new WorldPropPlacement("Campfire", 50f, 0.5f, 48f, 0f),
                new WorldPropPlacement("RockM", 30f, 0.5f, 40f, 0f),
                new WorldPropPlacement("PalmDead", 68f, 0.5f, 42f, 0f),
                new WorldPropPlacement("CrateStack", 60f, 0.5f, 56f, 0f),
                new WorldPropPlacement("Campfire", 150f, 0.5f, 158f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 168f, 0.5f, 168f, 0f),
                new WorldPropPlacement("PalmLean", 136f, 0.5f, 166f, 0f),
                // 南北两端礁台 + 两座后方梯田岛 + 踏脚礁上的漂流物
                new WorldPropPlacement("BarrelWood", 95f, 0.5f, 26f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 100f, 0.5f, 178f, 0f),
                new WorldPropPlacement("Campfire", 40f, 0.5f, 158f, 0f),
                new WorldPropPlacement("RockS", 34f, 0.5f, 152f, 0f),
                new WorldPropPlacement("CrateStack", 165f, 0.5f, 42f, 0f),
                new WorldPropPlacement("RockM", 172f, 0.5f, 48f, 0f),
                new WorldPropPlacement("Driftwood", 52f, 0.5f, 76f, 30f),
                new WorldPropPlacement("GrassTuft", 115f, 2.5f, 155f, 0f),
            },
            spawns: new[]
            {
                new WorldMapSpawn(0, "redPirate", 50f, 96f, 5),
                new WorldMapSpawn(0, "redPirate", 55f, 103f, 5),
                new WorldMapSpawn(0, "redPirate", 60f, 96f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 58f, 104f, 5),
                new WorldMapSpawn(1, "bluePirate", 153f, 96f, 2),
                new WorldMapSpawn(1, "bluePirate", 158f, 103f, 2),
                new WorldMapSpawn(1, "bluePirate", 163f, 96f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 161f, 104f, 2),
            },
            airdropPool: new[]
            {
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.TidalWave, 2),
                new WeaponStack(WeaponId.Anchor, 2),
                new WeaponStack(WeaponId.Seagull, 2),
                new WeaponStack(WeaponId.Dynamite, 2),
            },
            horizonSeed: 107));

        // ------------------------------------------------------------------
        // 8. sunken_gate 沉都之门（280×280，6v6，黄昏）
        // ------------------------------------------------------------------

        static readonly WorldMapDefinition SunkenGate = Register(new WorldMapDefinition(
            id: "sunken_gate", displayName: "沉都之门", levelNumber: 108,
            spanX: 280f, spanZ: 280f, ambientTier: "Dusk",
            terrain: new[]
            {
                // 【语义】沉都之门是一座沉进海里的城：东西主轴（西岛 → 沙洲 → 礁阶 → 沉没广场 →
                // 栈桥 → 东岛 → 门楼 → 心岛）是当年进城的主街，南北两侧各有一圈"被淹的街区"
                // （红树丛 + 沙洲 + 礁阶），四角再挂四座外城台——把 280×280 的图面从
                // "一条主街"铺成"一座沉城"。
                // 本阵贴主轴两端（西岛压沙洲/礁阶/广场；东岛压心岛链）
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 100f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 105f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 135f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "SunkenPlaza", 155f, 140f, 0f),
                new WorldKitPlacement("Marine", "PierLong", 182f, 140f, 90f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 200f, 140f, 0f),
                new WorldKitPlacement("Marine", "PierHead", 225f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "AtollCore", 240f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 230f, 158f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 205f, 175f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 155f, 85f, 20f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 155f, 108f, 90f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 200f, 75f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 180f, 80f, 90f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 155f, 195f, 200f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 155f, 172f, 90f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 200f, 205f, 40f),
                new WorldKitPlacement("Archipelago", "SandBarL", 180f, 200f, 90f),
                new WorldKitPlacement("Archipelago", "SeaStackTall", 200f, 105f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackTall", 200f, 175f, 0f),
                // 西南/东北两座外城台（主街两头的老城门台地）+ 西北/东南两片被淹街区
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 60f, 60f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandL", 220f, 220f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 95f, 100f, 0f),
                new WorldKitPlacement("Archipelago", "MangroveHummock", 205f, 195f, 0f),
                // 主轴两端外侧的礁台（把主街延伸到图的南北边缘）
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 140f, 245f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 140f, 35f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 105f, 240f, 0f),
                new WorldKitPlacement("Archipelago", "SandBarL", 175f, 40f, 0f),
                // 东西两端的外礁台（西门外 / 东门外）
                new WorldKitPlacement("Archipelago", "AtollCore", 40f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "AtollCore", 255f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 60f, 215f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 235f, 60f, 0f),
                // 海蚀柱前的一级踏脚（补 0.5→2.5→6.0 的高度阶梯，否则柱顶跳不上去）
                new WorldKitPlacement("Archipelago", "SeaStackShort", 200f, 122f, 0f),
                // 四角外城台与主轴两端礁台的踏脚链（每条 2-3 级礁阶，跳距一律 ≤13u）
                new WorldKitPlacement("Archipelago", "ReefStepsA", 72f, 92f, 0f),
                new WorldKitPlacement("Archipelago", "SeaStackShort", 76f, 110f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 98f, 116f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 152f, 228f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 168f, 220f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 145f, 58f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 150f, 70f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 62f, 140f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 72f, 192f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 80f, 180f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 85f, 165f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 216f, 76f, 0f),
                new WorldKitPlacement("Archipelago", "ReefStepsA", 235f, 78f, 0f),
            },
            horizon: new[]
            {
                new WorldKitPlacement("Horizon", "DistantIsleL", 140f, -80f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleM", 420f, 140f, 0f),
                new WorldKitPlacement("Horizon", "DistantIsleS", -80f, 320f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 40f, 60f, 0f),
                new WorldKitPlacement("Horizon", "CloudBankL", 260f, 260f, 30f),
                new WorldKitPlacement("Horizon", "FarFleet", 440f, 80f, 0f),
                new WorldKitPlacement("Horizon", "FarFleet", -60f, 180f, 0f),
            },
            horizonFeatures: new[] { "GiantRibs", "LeviathanTentacle", "WhaleSurfacing" },
            props: new[]
            {
                new WorldPropPlacement("Campfire", 44f, 0.5f, 136f, 0f),
                new WorldPropPlacement("PalmDead", 36f, 0.5f, 142f, 0f),
                // 西南/东北两座外城台（主街两头的城门台地）
                new WorldPropPlacement("Campfire", 56f, 0.5f, 58f, 0f),
                new WorldPropPlacement("CannonEmplacement", 66f, 0.5f, 64f, 210f),
                new WorldPropPlacement("RuinArch", 70f, 0.5f, 68f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 46f, 0.5f, 52f, 0f),
                new WorldPropPlacement("PalmDead", 76f, 0.5f, 46f, 0f),
                new WorldPropPlacement("Campfire", 216f, 0.5f, 216f, 0f),
                new WorldPropPlacement("CannonEmplacement", 228f, 0.5f, 226f, 140f),
                new WorldPropPlacement("RuinColumnBroken", 206f, 0.5f, 228f, 0f),
                new WorldPropPlacement("PalmDead", 232f, 0.5f, 212f, 0f),
                // 主轴两端外侧的礁台与沙洲
                new WorldPropPlacement("Campfire", 140f, 0.5f, 244f, 0f),
                new WorldPropPlacement("RockM", 146f, 0.5f, 248f, 0f),
                new WorldPropPlacement("BarrelWood", 140f, 0.5f, 36f, 0f),
                new WorldPropPlacement("RockS", 134f, 0.5f, 32f, 0f),
                new WorldPropPlacement("CrateStack", 105f, 0.5f, 240f, 0f),
                new WorldPropPlacement("Driftwood", 175f, 0.5f, 40f, 20f),
                // 东西门外的小礁台与被淹街区
                new WorldPropPlacement("RuinColumnBroken", 60f, 0.5f, 216f, 0f),
                new WorldPropPlacement("RockM", 235f, 0.5f, 60f, 0f),
                new WorldPropPlacement("RockS", 200f, 2.5f, 122f, 0f),
                new WorldPropPlacement("FernClump", 95f, 0.5f, 100f, 0f),
                new WorldPropPlacement("FernClump", 205f, 0.5f, 195f, 0f),
                new WorldPropPlacement("RuinArch", 155f, 0.5f, 127f, 0f),
                new WorldPropPlacement("RuinArch", 155f, 0.5f, 153f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 148f, 0.5f, 138f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 162f, 0.5f, 146f, 40f),
                new WorldPropPlacement("RuinColumnBroken", 150f, 0.5f, 150f, 80f),
                new WorldPropPlacement("RuinColumnBroken", 200f, 0.5f, 136f, 0f),
                new WorldPropPlacement("AnchorMonument", 206f, 0.5f, 134f, 0f),
                new WorldPropPlacement("ShipWheelPost", 240f, 0.5f, 138f, 0f),
                new WorldPropPlacement("TreasureMound", 225f, 1.0f, 142f, 0f),
                new WorldPropPlacement("CrateStack", 176f, 1.0f, 140f, 0f),
                new WorldPropPlacement("BarrelWood", 186f, 1.0f, 141f, 0f),
                new WorldPropPlacement("BarrelWood", 190f, 0.5f, 146f, 0f),
                new WorldPropPlacement("FernClump", 150f, 1.0f, 84f, 0f),
                new WorldPropPlacement("FernClump", 206f, 1.0f, 204f, 0f),
                new WorldPropPlacement("GrassTuft", 100f, 0.5f, 138f, 0f),
                new WorldPropPlacement("RockFlat", 92f, 0.5f, 142f, 20f),
                new WorldPropPlacement("RowboatBeached", 148f, 0.5f, 134f, 60f),
                new WorldPropPlacement("BuoyRing", 210f, 0.0f, 118f, 0f),
            },
            spawns: new[]
            {
                new WorldMapSpawn(0, "redPirate", 96f, 134f, 5),
                new WorldMapSpawn(0, "redPirate", 103f, 142f, 5),
                new WorldMapSpawn(0, "redPirate", 96f, 144f, 5),
                new WorldMapSpawn(0, "redPirate", 104f, 133f, 5),
                new WorldMapSpawn(0, "redPirate", 97f, 148.5f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 104f, 148f, 5),
                new WorldMapSpawn(1, "bluePirate", 201f, 171f, 2),
                new WorldMapSpawn(1, "bluePirate", 209f, 179f, 2),
                new WorldMapSpawn(1, "bluePirate", 217f, 171f, 2),
                new WorldMapSpawn(1, "bluePirate", 203f, 187f, 2),
                new WorldMapSpawn(1, "bluePirate", 213f, 188f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 221f, 180f, 2),
            },
            airdropPool: new[]
            {
                // 终章规格：全 15 种（保底樱桃炸弹 ∞，其余各 1）
                new WeaponStack(WeaponId.CherryBomb, 10),
                new WeaponStack(WeaponId.Dynamite, 1),
                new WeaponStack(WeaponId.Boulder, 1),
                new WeaponStack(WeaponId.Banana, 1),
                new WeaponStack(WeaponId.Mine, 1),
                new WeaponStack(WeaponId.ParachuteBomb, 1),
                new WeaponStack(WeaponId.RumBottle, 1),
                new WeaponStack(WeaponId.PiecesOfEight, 1),
                new WeaponStack(WeaponId.GunpowderBarrel, 1),
                new WeaponStack(WeaponId.WoodenCrate, 1),
                new WeaponStack(WeaponId.Anchor, 1),
                new WeaponStack(WeaponId.Seagull, 1),
                new WeaponStack(WeaponId.TidalWave, 1),
                new WeaponStack(WeaponId.VoodooDoll, 1),
                new WeaponStack(WeaponId.Cannon, 1),
            },
            horizonSeed: 108));
    }
}
