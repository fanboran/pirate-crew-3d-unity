using System;
using System.Collections.Generic;
using System.Linq;

namespace PirateCrew.Data
{
    /// <summary>
    /// 关卡数据资产的**唯一形状**（纯 C# 可序列化载荷）。三种渲染共用同一份字段名：
    ///   ① ScriptableObject 资产的 <c>data</c> 字段（Unity YAML，`.asset`）；
    ///   ② golden JSON 快照（`Assets/Data/**/_golden/*.json`，无头可读、diff 可读）；
    ///   ③ 运行时视图（<see cref="LevelData"/> / WorldMaps 的 <c>WorldMapDefinition</c>）。
    ///
    /// 【为什么载荷与 SO 分家】SO 实例化在无头验证台走原生 ECall 必崩，而"资产内容 == golden JSON"
    /// 这条判据必须在无头环境也能算；把字段放在纯 C# 载荷上，SO 只是它的宿主，
    /// 于是 YAML/JSON 读写器、校验器、对拍测试全部不需要 Unity 运行时。
    ///
    /// 【坐标口径】**一切数值全米**（Unity 世界单位）：尺幅、出生点、水位、高度场都是米，
    /// 与 <c>LevelGeometry</c> 的尺度口径同源，不再有"格"这一对外概念。
    /// </summary>
    public static class LevelAssetSchema
    {
        /// <summary>资产 schema 版本；字段语义变化时递增，迁移器按它判断要不要重写。</summary>
        public const int Version = 3;

        /// <summary>
        /// 逻辑高度场的**采样间隔**（米/采样格，= <c>LevelGeometry.TileWorldSize</c>）。
        /// 它只是运行时地形的数据采样粒度（不对玩家与资产暴露）；资产里的栅格尺幅用米写，
        /// 采样格数由本常量推出，故读写两侧不可能各自漂移。
        /// </summary>
        public const float RasterCellSize = 2f;

        /// <summary>米尺寸 → 采样格数（四舍五入）。</summary>
        public static int RasterCells(float meters)
        {
            return (int)Math.Round(meters / RasterCellSize, MidpointRounding.AwayFromZero);
        }

        /// <summary>golden JSON 的 kind 标记（玩家关卡快照）。</summary>
        public const string KindLevel = "level";

        /// <summary>golden JSON 的 kind 标记（大海域海图）。</summary>
        public const string KindWorldMap = "world_map";

        /// <summary>关卡资产目录（工程内相对路径，无头读取 golden JSON 的根）。</summary>
        public const string LevelGoldenDir = "Assets/Data/Levels/_golden";

        /// <summary>
        /// 资源清单资产的 <c>Resources.Load</c> 路径（Unity 侧唯一加载入口；
        /// 资产落 <c>Assets/Data/Levels/Resources/LevelCatalog.asset</c>，Resources 路径不含目录）。
        /// </summary>
        public const string CatalogResourcePath = "LevelCatalog";
    }

    /// <summary>
    /// 地形——**岛形轮廓**（形状即数据）。一块地形 = 一组悬浮岛，每岛 = 顶面高度 +
    /// 俯视外轮廓多边形（+ 可选内洞）；顶点全部是米制世界坐标。
    ///
    /// 【为什么是形状不是网格】场地设计是环形范围内的有机岛群，矩形采样数组是历史载体的
    /// 遗留概念；资产直接记录岛形，运行时按需要栅格化（见 <c>TerrainShapeRasterizer</c>），
    /// 采样数组从此只存在于运行时内部（<c>HeightfieldGrid</c>），不进资产、不进文档概念。
    ///
    /// 【全米】顶点、高度全是米（Unity 世界单位）；<see cref="blockWorldHeight"/> =
    /// 运行时地形的单块世界高度（现行 0.5 = <c>LevelGeometry.BlockWorldHeight</c>），
    /// 只用于把米高度折回运行时的整数块表示，存进资产是为了让资产自解释。
    /// </summary>
    [Serializable]
    public struct TerrainShape
    {
        /// <summary>单块世界高度（米）。</summary>
        public float blockWorldHeight;

        /// <summary>岛列表（每岛一个顶面高度 + 外轮廓 + 可选内洞）。</summary>
        public List<TerrainIsland> islands;

        /// <summary>结构是否自洽（读入校验用）。</summary>
        public bool IsWellFormed =>
            blockWorldHeight > 0f
            && islands != null && islands.Count > 0
            && islands.All(i => i.IsWellFormed);
    }

    /// <summary>
    /// 一座岛的形状：顶面高度 + 外轮廓（+ 可选内洞）。顶点是俯视平面的米制世界坐标
    /// （<c>[x0, z0, x1, z1, ...]</c> 扁平列表）；轮廓方向不限（点包含判定用 even-odd）。
    /// </summary>
    [Serializable]
    public struct TerrainIsland
    {
        /// <summary>顶面世界高度（米，&gt; 0；0 或负 = 无地面）。</summary>
        public float topY;

        /// <summary>外轮廓顶点（扁平 <c>[x0, z0, x1, z1, ...]</c>，至少 3 个顶点）。</summary>
        public List<float> outline;

        /// <summary>内洞轮廓列表（环岛用；null 或空 = 无洞）。</summary>
        public List<List<float>> holes;

        /// <summary>岛是否自洽（读入校验用）。</summary>
        public bool IsWellFormed =>
            topY > 0f
            && outline != null && outline.Count >= 6 && outline.Count % 2 == 0
            && (holes == null || holes.All(h => h != null && h.Count >= 6 && h.Count % 2 == 0));
    }

    /// <summary>
    /// 一件烘焙陈设的摆位（样板关：云场 / 岛壳 / 落水危险虚线）。
    /// <see cref="pieceId"/> 是 <c>SceneArt.ShowcasePieceId</c> 的整数值——Data 层不认识 SceneArt 枚举，
    /// 由 SceneArt 侧做一次显式转换（避免 Data → SceneArt 的反向依赖）。
    /// </summary>
    [Serializable]
    public struct BakedPieceEntry
    {
        /// <summary>件种类（= <c>(int)ShowcasePieceId</c>）。</summary>
        public int pieceId;

        /// <summary>实例名（层级可读性，非逻辑键）。</summary>
        public string instanceName;

        /// <summary>实例世界 X。</summary>
        public float x;

        /// <summary>实例世界 Y。</summary>
        public float y;

        /// <summary>实例世界 Z。</summary>
        public float z;

        /// <summary>绕 Y 朝向（度）。</summary>
        public float yawDeg;
    }

    /// <summary>
    /// 单场战斗的数据快照资产载荷（样板关 / 任何"非海图"战斗场）：编成、武器、luck、
    /// 逻辑高度场、烘焙件摆位。字段与旧 <c>ShowcaseLevels</c> 的手写数据逐条对应。
    /// </summary>
    [Serializable]
    public class LevelAssetPayload
    {
        /// <summary>关卡序号（选关/结算/得分口径用；手作样板关 1、3——关卡 2 已删除，号段有意不连续）。</summary>
        public int levelNumber;

        /// <summary>
        /// 资产的 ASCII 名（= `.asset` 文件名 / `m_Name` / golden JSON 文件名）。
        /// 显示名是中文（<see cref="displayName"/>），文件名必须是安全 ASCII，故单列一个字段，
        /// 免得迁移器靠外部约定猜文件名。
        /// </summary>
        public string assetName;

        /// <summary>显示名（结算与选关页展示，如「云端漫步」）。</summary>
        public string displayName;

        /// <summary>场地 X 尺幅（米）。</summary>
        public float sizeX;

        /// <summary>场地 Z 尺幅（米）。</summary>
        public float sizeZ;

        /// <summary>水面世界 Y（米；低于即落水，§4.4）。</summary>
        public float waterWorldY;

        /// <summary>空投武器池。</summary>
        public List<WeaponStack> airdropPool = new List<WeaponStack>();

        /// <summary>双方出战单位（顺序 = 关卡数据出现顺序，运行时按此实例化）。</summary>
        public List<LevelUnit> units = new List<LevelUnit>();

        /// <summary>逻辑高度场（唯一栅格语义）。</summary>
        public TerrainShape terrain;

        /// <summary>烘焙陈设摆位表（几何在 prefab 里，本表只记"件 id + 摆位"）。</summary>
        public List<BakedPieceEntry> bakedPieces = new List<BakedPieceEntry>();

        /// <summary>转运行时快照（逐字段直搬，不做任何"顺手修一下"）。</summary>
        public LevelData ToLevelData()
        {
            return new LevelData(
                levelNumber, displayName, sizeX, sizeZ,
                waterWorldY, airdropPool, units);
        }
    }

    /// <summary>地形/船坞/远景件摆位（资产侧扁平字段版；<c>y</c> = 摆放根世界 Y，手摆件恒 0）。</summary>
    [Serializable]
    public struct KitPlacementEntry
    {
        /// <summary>套件名（Archipelago / Marine / Horizon）。</summary>
        public string kit;

        /// <summary>资产名（kit FBX 名，如 TerraceIslandM）。</summary>
        public string asset;

        /// <summary>世界 X。</summary>
        public float x;

        /// <summary>世界 Y（摆放根；需要脱离海平面的件才非 0）。</summary>
        public float y;

        /// <summary>世界 Z。</summary>
        public float z;

        /// <summary>绕 Y 朝向（度）。</summary>
        public float yawDeg;
    }

    /// <summary>道具摆位（资产侧扁平字段版；<c>y</c> = 所站站面顶高）。</summary>
    [Serializable]
    public struct PropPlacementEntry
    {
        /// <summary>资产名。</summary>
        public string asset;

        /// <summary>世界 X。</summary>
        public float x;

        /// <summary>世界 Y（落地接触面）。</summary>
        public float y;

        /// <summary>世界 Z。</summary>
        public float z;

        /// <summary>绕 Y 朝向（度）。</summary>
        public float yawDeg;
    }

    /// <summary>出生点（资产侧扁平字段版，世界坐标）。</summary>
    [Serializable]
    public struct SpawnEntry
    {
        /// <summary>队伍索引：0 = 红队，1 = 蓝队。</summary>
        public int teamIndex;

        /// <summary>战斗导出符号名（如 redPirateCaptain）。</summary>
        public string archetype;

        /// <summary>世界 X。</summary>
        public float x;

        /// <summary>世界 Z。</summary>
        public float z;

        /// <summary>该单位 luck（AI 随机投掷次数基数）。</summary>
        public int luck;
    }

    /// <summary>
    /// 一张大海域海图的数据载荷。字段与旧 <c>WorldMapCatalog</c> 的手写表逐条对应，
    /// 语义（坐标口径 / 连通性约束 / 关卡号段）见 <c>docs/技术/架构/关卡数据资产.md</c>。
    /// </summary>
    [Serializable]
    public class WorldMapAssetPayload
    {
        /// <summary>地图 id（选关/战役结算键，如 wreck_hymn）。</summary>
        public string id;

        /// <summary>显示名（中文名，选关页与结算展示）。</summary>
        public string displayName;

        /// <summary>关卡号（海图占 101–108 段，与样板关号段不冲突）。</summary>
        public int levelNumber;

        /// <summary>图幅 X（米）。</summary>
        public float spanX;

        /// <summary>图幅 Z（米）。</summary>
        public float spanZ;

        /// <summary>氛围档（"Noon"/"Dusk"/"Storm"，对接 AmbientTimeOfDayCatalog 预设名）。</summary>
        public string ambientTier;

        /// <summary>普通船员初配（方案 D 分层军火；null 时运行时回落「樱桃×∞」战役惯例）。</summary>
        public List<WeaponStack> crewWeapons;

        /// <summary>船长初配（含 ≥1 件全图级旗舰武器）。</summary>
        public List<WeaponStack> captainWeapons;

        /// <summary>地形/船坞件摆位（站面 box 由 kit 资产的 standable 表展开）。</summary>
        public List<KitPlacementEntry> terrain = new List<KitPlacementEntry>();

        /// <summary>远景件摆位（海平面外环）。</summary>
        public List<KitPlacementEntry> horizon = new List<KitPlacementEntry>();

        /// <summary>道具摆位（不承载站面，纯陈设）。</summary>
        public List<PropPlacementEntry> props = new List<PropPlacementEntry>();

        /// <summary>出生点（世界坐标）。</summary>
        public List<SpawnEntry> spawns = new List<SpawnEntry>();

        /// <summary>空投武器池。</summary>
        public List<WeaponStack> airdropPool = new List<WeaponStack>();

        /// <summary>远景环随机种子（确定性）。</summary>
        public int horizonSeed;

        /// <summary>本图专属远景特征件（如 LeviathanTentacle / GiantRibs）。</summary>
        public List<string> horizonFeatures = new List<string>();
    }
}
