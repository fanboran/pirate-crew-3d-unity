using PirateCrew.Data;

namespace PirateCrew.Battle.Levels
{
    /// <summary>
    /// 本局关卡来源的**解析结果**：出战计划、地形栅格、水位、图幅全部已算好。
    /// 消费方（<c>BattleController</c>）只读这里的数据，不再判断"这局是什么内容种类"——
    /// 关卡=唯一概念（创始人 2026-10-23 裁决），本局来源永远是某个关卡资产。
    /// </summary>
    public sealed class LevelSource
    {
        /// <summary>关卡资产载荷。</summary>
        public readonly LevelAssetPayload Asset;

        /// <summary>出战计划（单位 / 格子尺寸 / 水位）。</summary>
        public readonly BattlePlan Plan;

        /// <summary>逻辑高度场（AI / 站位 / 小地图共用）。</summary>
        public readonly HeightfieldGrid Terrain;

        /// <summary>水面世界 Y（低于即落水，§4.4）。</summary>
        public readonly float WaterWorldY;

        /// <summary>水模拟域 / 海面外扩用的图幅 X（= <see cref="Plan"/> 的 <c>WorldWidth</c>）。</summary>
        public readonly float SpanX;

        /// <summary>水模拟域 / 海面外扩用的图幅 Z。</summary>
        public readonly float SpanZ;

        /// <summary>解析过程中值得让用户知道的事（回落告警等）；null = 无。</summary>
        public readonly string Notice;

        public LevelSource(
            LevelAssetPayload asset, BattlePlan plan, HeightfieldGrid terrain, float waterWorldY,
            float spanX, float spanZ, string notice)
        {
            Asset = asset;
            Plan = plan;
            Terrain = terrain;
            WaterWorldY = waterWorldY;
            SpanX = spanX;
            SpanZ = spanZ;
            Notice = notice;
        }

        /// <summary>本局关卡号。</summary>
        public int LevelNumber => Plan != null ? Plan.LevelNumber : 0;
    }

    /// <summary>
    /// 关卡来源解析器——**全仓唯一**决定"这一局打哪张内容"的地方。
    ///
    /// 【为什么要有它】重构前这段分叉写死在 <c>BattleController</c> 的四处
    /// （BuildPlan / BuildTerrain / RebuildSceneArt / SetupBattleEnvironment），
    /// 战斗根类同时是内容路由器。现在路由只在这里出现一次，且是可注入参数的纯函数
    /// （无头可测：<see cref="Resolve(int, int)"/> 不碰命令行与静态状态）。
    ///
    /// 【优先级（自上而下，命中即返回）】
    ///   ① <c>-artReviewLevel N</c>（美术出图覆盖，N 有对应关卡资产）→ 该关卡资产；
    ///      出图覆盖是工具专用通道，**有值时其它通道一律让位**（覆盖到不存在的关号时
    ///      也不改走待战内容，落兜底关 1 并留告警，与原语义一致）；
    ///   ② 待战关卡（选关页点选 <see cref="PendingLevel.SetPending"/> 或 <c>-bootBattle N</c>）；
    ///   ③ 都没有 → 第 1 张关卡资产兜底（直接 Play 战斗场景的开发路径）。
    /// </summary>
    public static class LevelSourceResolver
    {
        /// <summary>无待战内容时的兜底关卡号。</summary>
        public const int FallbackLevelNumber = 1;

        /// <summary>
        /// 按当前进程状态解析（命令行开关名与语义未变：<c>-artReviewLevel</c> / <c>-bootBattle</c>）。
        /// 取不到任何内容时返回 null（构建配置错误，由调用方吵闹）。
        /// </summary>
        public static LevelSource Resolve()
        {
            int overrideLevel = ArtReview.ArtReviewCaptureOverride.LevelNumber;
            int pendingLevel = PendingLevel.TryGetPending(out int levelNumber) ? levelNumber : 0;
            return Resolve(overrideLevel, pendingLevel);
        }

        /// <summary>
        /// 注入式重载（无头可测）：不读命令行、不读静态状态。
        /// </summary>
        /// <param name="overrideLevel">出图覆盖的关卡号；&lt;= 0 = 未启用。</param>
        /// <param name="pendingLevel">待战关卡号；&lt;= 0 = 无。</param>
        public static LevelSource Resolve(int overrideLevel, int pendingLevel)
        {
            if (overrideLevel > 0)
            {
                // ① 出图覆盖：命中即用；命中不了（关号已不存在）也**不**改走 ②，直接落 ③。
                if (LevelAssetLibrary.TryGetLevel(overrideLevel, out LevelAssetPayload asset))
                    return FromLevel(overrideLevel, asset, null);
            }
            else
            {
                // ② 待战关卡（选关页点选 / -bootBattle）。
                if (pendingLevel > 0 && LevelAssetLibrary.TryGetLevel(pendingLevel, out LevelAssetPayload pending))
                    return FromLevel(pendingLevel, pending, null);
            }

            // ③ 兜底：第 1 关（并留告警）。
            if (!LevelAssetLibrary.TryGetLevel(FallbackLevelNumber, out LevelAssetPayload fallback))
                return null;

            string notice = "[LevelSourceResolver] 无待战关卡，回落关卡资产 "
                + FallbackLevelNumber + "（正常出海走选关页或播放器 -bootBattle <关卡号>）。";
            return FromLevel(FallbackLevelNumber, fallback, notice);
        }

        /// <summary>由关卡资产构出本局来源（出战计划 / 栅格 / 水位 / 图幅）。</summary>
        public static LevelSource FromLevel(int levelNumber, LevelAssetPayload asset, string notice)
        {
            if (asset == null)
                return null;

            BattlePlan plan = LevelGeometry.BuildBattlePlan(asset.ToLevelData());
            HeightfieldGrid terrain = LevelRasterFromAsset.Build(asset);

            return new LevelSource(
                asset, plan, terrain, plan.WaterWorldY,
                plan.WorldWidth, plan.WorldDepth,
                notice);
        }
    }
}
