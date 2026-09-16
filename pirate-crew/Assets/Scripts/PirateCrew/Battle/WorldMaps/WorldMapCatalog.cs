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
                // 主战线连成一体（M 岛与断船/桅桥重叠），南线沙洲环做侧翼
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 55f, 75f, 0f),
                new WorldKitPlacement("Archipelago", "TerraceIslandM", 95f, 75f, 0f),
                new WorldKitPlacement("Marine", "WreckBowHalf", 40f, 75f, 90f),
                new WorldKitPlacement("Marine", "WreckSternHalf", 110f, 75f, 90f),
                new WorldKitPlacement("Marine", "MastBridge", 75f, 75f, 90f),
                new WorldKitPlacement("Archipelago", "SandBarL", 55f, 62f, 315f),
                new WorldKitPlacement("Archipelago", "SandBarL", 95f, 62f, 45f),
                new WorldKitPlacement("Archipelago", "AtollCore", 75f, 45f, 0f),
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
                new WorldPropPlacement("Campfire", 55f, 0.5f, 78f, 0f),
                new WorldPropPlacement("Campfire", 95f, 0.5f, 72f, 0f),
                new WorldPropPlacement("TreasureMound", 77.2f, 4.5f, 73.7f, 0f),
                new WorldPropPlacement("CannonEmplacement", 48f, 0.5f, 71f, 200f),
                new WorldPropPlacement("CannonEmplacement", 102f, 0.5f, 79f, 160f),
                new WorldPropPlacement("PalmTall", 70f, 1f, 42f, 15f),
                new WorldPropPlacement("Driftwood", 60f, 0.5f, 52f, 60f),
                new WorldPropPlacement("GrassTuft", 78f, 0.5f, 47f, 0f),
            },
            spawns: new[]
            {
                new WorldMapSpawn(0, "redPirate", 52f, 73f, 5),
                new WorldMapSpawn(0, "redPirate", 58f, 77f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 55f, 79f, 5),
                new WorldMapSpawn(1, "bluePirate", 92f, 73f, 2),
                new WorldMapSpawn(1, "bluePirate", 98f, 77f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 95f, 79f, 2),
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
                new WorldPropPlacement("Campfire", 27f, 0.5f, 92f, 0f),
                new WorldPropPlacement("CannonEmplacement", 33f, 0.5f, 99f, 250f),
                new WorldPropPlacement("CannonEmplacement", 151f, 1.0f, 92f, 90f),
                new WorldPropPlacement("ShipWheelPost", 154f, 1.0f, 98f, 0f),
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
                // 中央长码头三段 + 沉没广场 + 灯塔岛高地
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
                new WorldPropPlacement("Campfire", 96f, 0.5f, 58f, 0f),
                new WorldPropPlacement("CannonEmplacement", 112f, 0.5f, 52f, 120f),
                new WorldPropPlacement("RuinColumnBroken", 98f, 0.5f, 64f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 110f, 0.5f, 68f, 40f),
                new WorldPropPlacement("RuinArch", 105f, 0.5f, 47f, 0f),
                new WorldPropPlacement("ShipWheelPost", 105f, 1.0f, 112f, 0f),
                new WorldPropPlacement("CrateStack", 88f, 1.0f, 111f, 0f),
                new WorldPropPlacement("CrateStack", 126f, 1.0f, 109f, 90f),
                new WorldPropPlacement("BarrelWood", 96f, 1.0f, 109f, 0f),
                new WorldPropPlacement("BarrelWood", 114f, 1.0f, 112f, 0f),
                new WorldPropPlacement("Campfire", 102f, 0.5f, 152f, 0f),
                new WorldPropPlacement("TreasureMound", 108f, 0.5f, 148f, 0f),
                new WorldPropPlacement("PalmDead", 96f, 0.5f, 70f, 0f),
                new WorldPropPlacement("FernClump", 148f, 0.5f, 100f, 0f),
                new WorldPropPlacement("GrassTuft", 60f, 0.5f, 98f, 0f),
                new WorldPropPlacement("RockM", 160f, 0.5f, 94f, 0f),
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
                new WorldPropPlacement("Campfire", 42f, 0.5f, 17f, 0f),
                new WorldPropPlacement("CannonEmplacement", 55f, 0.5f, 24f, 240f),
                new WorldPropPlacement("Campfire", 138f, 0.5f, 151f, 0f),
                new WorldPropPlacement("CannonEmplacement", 126f, 0.5f, 144f, 60f),
                new WorldPropPlacement("TreasureMound", 104f, 1.0f, 97f, 0f),
                new WorldPropPlacement("FernClump", 46f, 1.0f, 46f, 0f),
                new WorldPropPlacement("FernClump", 78f, 1.0f, 44f, 90f),
                new WorldPropPlacement("FernClump", 92f, 1.0f, 72f, 0f),
                new WorldPropPlacement("FernClump", 120f, 1.0f, 98f, 180f),
                new WorldPropPlacement("GrassTuft", 64f, 1.0f, 124f, 0f),
                new WorldPropPlacement("GrassTuft", 106f, 1.0f, 124f, 90f),
                new WorldPropPlacement("GrassTuft", 148f, 1.0f, 122f, 0f),
                new WorldPropPlacement("Driftwood", 50f, 1.0f, 96f, 20f),
                new WorldPropPlacement("Driftwood", 134f, 1.0f, 72f, 110f),
                new WorldPropPlacement("BarrelWood", 74f, 1.0f, 98f, 0f),
                new WorldPropPlacement("BarrelWood", 116f, 1.0f, 46f, 0f),
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
                new WorldKitPlacement("Archipelago", "VolcanoRimA", 150f, 40f, 90f),
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
                new WorldPropPlacement("Campfire", 42f, 0.5f, 196f, 0f),
                new WorldPropPlacement("CannonEmplacement", 60f, 0.5f, 206f, 210f),
                new WorldPropPlacement("Campfire", 208f, 0.5f, 42f, 0f),
                new WorldPropPlacement("CannonEmplacement", 226f, 0.5f, 52f, 150f),
                new WorldPropPlacement("TreasureMound", 215f, 3.5f, 111f, 0f),
                new WorldPropPlacement("ShipWheelPost", 213f, 2.0f, 106f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 185f, 1.5f, 140f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 177f, 1.0f, 140f, 60f),
                new WorldPropPlacement("PalmTall", 34f, 0.5f, 210f, 0f),
                new WorldPropPlacement("PalmLean", 70f, 0.5f, 190f, 0f),
                new WorldPropPlacement("PalmTall", 222f, 0.5f, 34f, 0f),
                new WorldPropPlacement("PalmDead", 193f, 0.5f, 58f, 0f),
                new WorldPropPlacement("CannonEmplacement", 220f, 0.5f, 52f, 90f),
                new WorldPropPlacement("RockL", 156f, 0.5f, 34f, 0f),
                new WorldPropPlacement("RockM", 140f, 0.5f, 48f, 0f),
                new WorldPropPlacement("GrassTuft", 96f, 0.5f, 200f, 0f),
                new WorldPropPlacement("FernClump", 165f, 0.5f, 162f, 0f),
                new WorldPropPlacement("BarrelWood", 128f, 0.5f, 188f, 0f),
                new WorldPropPlacement("Driftwood", 158f, 0.5f, 115f, 30f),
            },
            spawns: new[]
            {
                new WorldMapSpawn(0, "redPirate", 115f, 191f, 5),
                new WorldMapSpawn(0, "redPirate", 121f, 195f, 5),
                new WorldMapSpawn(0, "redPirate", 115f, 194.5f, 5),
                new WorldMapSpawn(0, "redPirateCaptain", 121f, 190f, 5),
                new WorldMapSpawn(1, "bluePirate", 225f, 56f, 2),
                new WorldMapSpawn(1, "bluePirate", 231f, 60f, 2),
                new WorldMapSpawn(1, "bluePirate", 225f, 59.5f, 2),
                new WorldMapSpawn(1, "bluePirateCaptain", 231f, 55f, 2),
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
                new WorldPropPlacement("Campfire", 172f, 0.5f, 102f, 0f),
                new WorldPropPlacement("CannonEmplacement", 184f, 0.5f, 94f, 130f),
                new WorldPropPlacement("ShipWheelPost", 92f, 1.0f, 102f, 0f),
                new WorldPropPlacement("CrateStack", 98f, 1.0f, 98f, 0f),
                new WorldPropPlacement("BarrelWood", 162f, 1.0f, 101f, 0f),
                new WorldPropPlacement("RockL", 106f, 0.5f, 118f, 0f),
                new WorldPropPlacement("RockM", 94f, 0.5f, 126f, 0f),
                new WorldPropPlacement("RockS", 140f, 2.5f, 101f, 0f),
                new WorldPropPlacement("GrassTuft", 40f, 0.5f, 94f, 0f),
                new WorldPropPlacement("GrassTuft", 174f, 0.5f, 108f, 0f),
                new WorldPropPlacement("TreasureMound", 130f, 6.0f, 100f, 0f),
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
                new WorldPropPlacement("CannonEmplacement", 62f, 0.5f, 148f, 210f),
                new WorldPropPlacement("Campfire", 238f, 0.5f, 186f, 0f),
                new WorldPropPlacement("CannonEmplacement", 220f, 0.5f, 198f, 140f),
                new WorldPropPlacement("RuinArch", 155f, 0.5f, 127f, 0f),
                new WorldPropPlacement("RuinArch", 155f, 0.5f, 153f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 148f, 0.5f, 138f, 0f),
                new WorldPropPlacement("RuinColumnBroken", 162f, 0.5f, 146f, 40f),
                new WorldPropPlacement("RuinColumnBroken", 150f, 0.5f, 150f, 80f),
                new WorldPropPlacement("RuinColumnBroken", 200f, 0.5f, 136f, 0f),
                new WorldPropPlacement("AnchorMonument", 208f, 0.5f, 128f, 0f),
                new WorldPropPlacement("ShipWheelPost", 240f, 0.5f, 138f, 0f),
                new WorldPropPlacement("TreasureMound", 225f, 1.0f, 142f, 0f),
                new WorldPropPlacement("CrateStack", 176f, 1.0f, 138f, 0f),
                new WorldPropPlacement("BarrelWood", 186f, 1.0f, 141f, 0f),
                new WorldPropPlacement("BarrelWood", 190f, 0.5f, 146f, 0f),
                new WorldPropPlacement("PalmDead", 60f, 0.5f, 130f, 0f),
                new WorldPropPlacement("PalmDead", 236f, 0.5f, 194f, 0f),
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
