using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;

namespace PirateCrew.Battle.Levels
{
    /// <summary>本局关卡的内容来源。</summary>
    public enum LevelSourceKind
    {
        /// <summary>大海域海图（<c>-worldMap</c> / 选关页 SetPending）。</summary>
        WorldMap = 0,

        /// <summary>非海图战斗场（关卡资产；-artReviewLevel 覆盖或直接 Play 的兜底关）。</summary>
        Showcase = 1,
    }

    /// <summary>
    /// 本局关卡来源的**解析结果**：出战计划、地形栅格、水位、图幅、氛围档全部已算好。
    /// 消费方（<c>BattleController</c>）不再判断"这局是不是海图"，只读这里的数据。
    /// </summary>
    public sealed class LevelSource
    {
        /// <summary>内容来源种类。</summary>
        public readonly LevelSourceKind Kind;

        /// <summary>海图定义（<see cref="Kind"/> == WorldMap 时非 null）。</summary>
        public readonly WorldMapDefinition WorldMap;

        /// <summary>关卡资产载荷（<see cref="Kind"/> == Showcase 时非 null）。</summary>
        public readonly LevelAssetPayload ShowcaseAsset;

        /// <summary>出战计划（单位 / 格子尺寸 / 水位）。</summary>
        public readonly BattlePlan Plan;

        /// <summary>逻辑高度场（AI / 站位 / 小地图共用）。</summary>
        public readonly TileTerrainGrid Terrain;

        /// <summary>水面世界 Y（低于即落水，§4.4）。</summary>
        public readonly float WaterWorldY;

        /// <summary>水模拟域 / 海面外扩用的图幅 X（海图 = 定义跨度；样板关 = 竞技场世界宽）。</summary>
        public readonly float SpanX;

        /// <summary>水模拟域 / 海面外扩用的图幅 Z。</summary>
        public readonly float SpanZ;

        /// <summary>竞技场世界宽（= <see cref="Plan"/> 的 <c>WorldWidth</c>）。</summary>
        public readonly float ArenaWorldWidth;

        /// <summary>竞技场世界深。</summary>
        public readonly float ArenaWorldDepth;

        /// <summary>氛围档名（"Noon"/"Dusk"/"Storm"）；null = 保持场景烘焙档（样板关）。</summary>
        public readonly string AmbientTier;

        /// <summary>相机全景档的图幅（0 = 不设置，样板关沿用场景烘焙边界）。</summary>
        public readonly float CameraWorldSpan;

        /// <summary>解析过程中值得让用户知道的事（回落告警等）；null = 无。</summary>
        public readonly string Notice;

        public LevelSource(
            LevelSourceKind kind, WorldMapDefinition worldMap, LevelAssetPayload showcaseAsset,
            BattlePlan plan, TileTerrainGrid terrain, float waterWorldY,
            float spanX, float spanZ, string ambientTier, float cameraWorldSpan, string notice)
        {
            Kind = kind;
            WorldMap = worldMap;
            ShowcaseAsset = showcaseAsset;
            Plan = plan;
            Terrain = terrain;
            WaterWorldY = waterWorldY;
            SpanX = spanX;
            SpanZ = spanZ;
            ArenaWorldWidth = plan != null ? plan.WorldWidth : 0f;
            ArenaWorldDepth = plan != null ? plan.WorldDepth : 0f;
            AmbientTier = ambientTier;
            CameraWorldSpan = cameraWorldSpan;
            Notice = notice;
        }

        /// <summary>本局是否为海图模式。</summary>
        public bool IsWorldMapActive => Kind == LevelSourceKind.WorldMap;

        /// <summary>本局关卡号（海图 101–108 / 关卡资产 1–3）。</summary>
        public int LevelNumber => Plan != null ? Plan.LevelNumber : 0;
    }

    /// <summary>
    /// 关卡来源解析器——**全仓唯一**决定"这一局打哪张内容"的地方。
    ///
    /// 【为什么要有它】重构前这段分叉写死在 <c>BattleController</c> 的四处
    /// （BuildPlan / BuildTerrain / RebuildSceneArt / SetupBattleEnvironment），
    /// 战斗根类同时是内容路由器。现在分叉只在这里出现一次，且是可注入参数的纯函数
    /// （无头可测：<see cref="Resolve(int, WorldMapDefinition)"/> 不碰命令行与静态状态）。
    ///
    /// 【优先级（语义与重构前逐条一致）】
    ///   ① <c>-artReviewLevel N</c>（美术出图覆盖，N 有对应关卡资产）→ 该关卡资产；
    ///   ② 否则若没有出图覆盖且有待战海图（<c>-worldMap &lt;id&gt;</c> 或选关页 SetPending）→ 海图；
    ///   ③ 否则 → 第 1 张关卡资产兜底（直接 Play 战斗场景的开发路径）。
    /// </summary>
    public static class LevelSourceResolver
    {
        /// <summary>无待战内容时的兜底关卡号（原「样板第 1 关」口径）。</summary>
        public const int FallbackLevelNumber = 1;

        /// <summary>
        /// 按当前进程状态解析（命令行开关名与语义未变：<c>-worldMap</c> / <c>-artReviewLevel</c>）。
        /// 取不到任何内容时返回 null（构建配置错误，由调用方吵闹）。
        /// </summary>
        public static LevelSource Resolve()
        {
            int overrideLevel = ArtReview.ArtReviewCaptureOverride.LevelNumber;
            WorldMapDefinition pending = WorldMapRuntime.TryGetPending(out WorldMapDefinition map) ? map : null;
            return Resolve(overrideLevel, pending);
        }

        /// <summary>
        /// 注入式重载（无头可测）：不读命令行、不读静态状态。
        /// </summary>
        /// <param name="showcaseOverrideLevel">出图覆盖的关卡号；&lt;= 0 = 未启用。</param>
        /// <param name="pendingWorldMap">待战海图；null = 无。</param>
        public static LevelSource Resolve(int showcaseOverrideLevel, WorldMapDefinition pendingWorldMap)
        {
            bool overridden = showcaseOverrideLevel > 0;
            if (overridden && LevelAssetLibrary.TryGetLevel(showcaseOverrideLevel, out LevelAssetPayload asset))
                return FromShowcase(showcaseOverrideLevel, asset, null);

            if (!overridden && pendingWorldMap != null)
                return FromWorldMap(pendingWorldMap);

            if (!LevelAssetLibrary.TryGetLevel(FallbackLevelNumber, out LevelAssetPayload fallback))
                return null;

            string notice = "[LevelSourceResolver] 无待战海图，回落关卡资产 "
                + FallbackLevelNumber + "（正常出海走选关页或播放器 -worldMap <地图id>）。";
            return FromShowcase(FallbackLevelNumber, fallback, notice);
        }

        /// <summary>由海图定义构出本局来源（出战计划 / 栅格 / 水位 / 图幅 / 氛围档）。</summary>
        public static LevelSource FromWorldMap(WorldMapDefinition map)
        {
            if (map == null)
                return null;

            BattlePlan plan = WorldMapRuntime.BuildBattlePlan(map);
            TileTerrainGrid terrain = WorldMapRuntime.BuildTerrainGrid(map)
                                      ?? TileTerrainGrid.Flat(plan.WidthTiles, plan.DepthTiles);

            return new LevelSource(
                LevelSourceKind.WorldMap, map, null, plan, terrain, plan.WaterWorldY,
                map.SpanX, map.SpanZ,
                ambientTier: map.AmbientTier,
                cameraWorldSpan: (map.SpanX > map.SpanZ ? map.SpanX : map.SpanZ),
                notice: null);
        }

        /// <summary>由关卡资产构出本局来源。</summary>
        public static LevelSource FromShowcase(int levelNumber, LevelAssetPayload asset, string notice)
        {
            if (asset == null)
                return null;

            BattlePlan plan = LevelGeometry.BuildBattlePlan(asset.ToLevelData());
            TileTerrainGrid terrain = LevelRasterFromAsset.Build(asset);

            return new LevelSource(
                LevelSourceKind.Showcase, null, asset, plan, terrain, plan.WaterWorldY,
                plan.WorldWidth, plan.WorldDepth,
                ambientTier: null,
                cameraWorldSpan: 0f,
                notice: notice);
        }
    }
}
