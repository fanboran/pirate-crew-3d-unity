using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界地图的运行时入口：待战状态、命令行解析、<see cref="BattlePlan"/> 构建。
    ///
    /// 【待战状态 = 两个互斥槽位】同一次只可能有一个有效，写入口彼此清空对方：
    ///   · <see cref="SetPending"/> —— 世界海图（选关页海图行 / 主菜单「进入战斗」/ 结算「再战」）；
    ///   · <see cref="SetPendingShowcase"/> —— 手作样板关（选关页样板行）。
    /// 读侧也守住这条互斥：<see cref="TryGetPending"/> 在样板待战期间恒返回 false。
    ///
    /// 【进图途径】① 程序调用上面两个入口（选关 UI 走这条）；
    /// ② 播放器/批处理命令行 <c>-worldMap &lt;id&gt;</c>（无头捕图与试玩验证走这条，
    ///    与 ArtReview 的 <c>-artReviewLevel</c> 同风格，但优先级低于它）。
    ///
    /// 【与 BattleController 的契约】进图优先级由 <c>LevelSourceResolver</c> **一处**决定：
    /// 出图覆盖（-artReviewLevel，美术出图）&gt; 选关页点选的样板关 &gt; 世界地图 &gt;
    /// 样板第 1 关兜底（直接 Play）；世界地图激活时 BuildTerrain 走栅格化块表、
    /// 陈设由 <see cref="WorldMapComposer"/> 负责表现层。一代退场后这是**唯一的玩法进图通道**，
    /// 战役结算归属也由它决定（<c>CampaignApi</c> 在 battle_started 时读取；样板关不记星，
    /// 见 <see cref="SetPendingShowcase"/>）。
    /// </summary>
    public static class WorldMapRuntime
    {
        const string CommandLineSwitch = "-worldMap";

        static string _pendingMapId;
        static int _pendingShowcaseLevel;
        static bool _commandLineScanned;

        /// <summary>设定待战世界地图（id 不在目录时返回 false，不改动现状；
        /// 成功即清掉待战样板关——两个槽位互斥，避免上一局的待战内容泄漏到本局）。</summary>
        public static bool SetPending(string mapId)
        {
            if (!WorldMapCatalog.TryGet(mapId, out _))
                return false;
            _pendingMapId = mapId;
            _pendingShowcaseLevel = 0;
            return true;
        }

        public static void ClearPending() => _pendingMapId = null;

        /// <summary>
        /// 设定待战的手作样板关（选关页样板行；关卡号必须已加载出关卡资产，否则返回 false 且不改动现状）。
        /// 成功即清掉待战海图——两个槽位互斥。
        ///
        /// 【为什么不记星】星级进度的键是**海图 id**（<c>CampaignProgress</c> 只认它），
        /// 样板关没有这个键，所以选关页不给样板行画星级；本状态只决定"这一局加载哪份内容"。
        /// </summary>
        public static bool SetPendingShowcase(int levelNumber)
        {
            if (!LevelAssetLibrary.TryGetLevel(levelNumber, out _))
                return false;
            _pendingShowcaseLevel = levelNumber;
            _pendingMapId = null;
            return true;
        }

        /// <summary>取待战样板关的关卡号；返回 false 表示本局不是选关页点进来的样板关。</summary>
        public static bool TryGetPendingShowcase(out int levelNumber)
        {
            levelNumber = _pendingShowcaseLevel;
            return levelNumber > 0;
        }

        /// <summary>清掉待战样板关（<see cref="SetPendingShowcase"/> 的反操作；静态复位与测试域隔离用）。</summary>
        public static void ClearPendingShowcase() => _pendingShowcaseLevel = 0;

        /// <summary>
        /// 关闭 Domain Reload 时静态字段不会自动清空，进入播放前强制重置
        /// （由唯一入口 <c>Core/GameEntryPoint</c> 调用）；
        /// 命令行扫描标志一并复位，让每次播放重新取 <c>-worldMap</c>。
        /// 关卡资产缓存（<c>LevelAssetLibrary</c> → 本目录）同批清空——同一播放里改了资产要能重读。
        /// **两个待战槽位都要清**：漏一个就会让上一局的待战内容串到下一局（静态残留）。
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.ResetStatics, order: 20)]
        internal static void ResetStatics()
        {
            ClearPending();
            ClearPendingShowcase();
            _commandLineScanned = false;
            LevelAssetLibrary.Reset();
            WorldMapCatalog.ResetCache();
        }

        /// <summary>
        /// 取当前待战地图（命令行 <c>-worldMap</c> 优先，其次 <see cref="SetPending"/>）。
        /// 返回 false 表示本局不走世界地图——**待战样板关期间恒为 false**：互斥由读侧一起守，
        /// 因为命令行开关可能在 <see cref="SetPendingShowcase"/> 之后才被首次扫描到，
        /// 不挡住它，样板局的海图 HUD 与结算归属就会被那张图抢走。
        /// </summary>
        public static bool TryGetPending(out WorldMapDefinition map)
        {
            ScanCommandLine();
            if (_pendingShowcaseLevel > 0)
            {
                map = null;
                return false;
            }

            if (_pendingMapId != null && WorldMapCatalog.TryGet(_pendingMapId, out map))
                return true;
            map = null;
            return false;
        }

        /// <summary>
        /// 取命令行指定的海图（<c>-worldMap &lt;id&gt;</c>）。argv 由
        /// <see cref="CommandLineOptions"/> 统一解析（唯一入口解析一次，本类不再自扫命令行）；
        /// 只扫描一次并缓存结果（id 不在目录里时告警并忽略）。
        /// </summary>
        static void ScanCommandLine()
        {
            if (_commandLineScanned)
                return;
            _commandLineScanned = true;

            // `-bootBattle <关卡号|海图 id>`：启动即进该关（Bootstrapper 已跳过主菜单）。
            // 关卡号 → 手作样板关通道；海图 id → 海图通道。与 `-worldMap` 等价但更直白，
            // 两个都给时以 `-bootBattle` 为准（它是"我要看这一关"的显式表达）。
            string boot = CommandLineOptions.GetValue(ToolFlags.BootBattle);
            if (!string.IsNullOrEmpty(boot))
            {
                if (int.TryParse(boot, out int bootLevel) && bootLevel > 0)
                {
                    if (SetPendingShowcase(bootLevel))
                    {
                        Debug.Log("[WorldMapRuntime] -bootBattle " + bootLevel
                            + "：启动即进手作样板关 " + bootLevel + "。");
                        return;
                    }

                    Debug.LogWarning("[WorldMapRuntime] -bootBattle " + bootLevel
                        + " 不是已登记的样板关（关卡表里没有它），忽略。");
                }
                else if (WorldMapCatalog.TryGet(boot, out _))
                {
                    _pendingMapId = boot;
                    Debug.Log("[WorldMapRuntime] -bootBattle " + boot + "：启动即进海图。");
                    return;
                }
                else
                {
                    Debug.LogWarning("[WorldMapRuntime] -bootBattle " + boot
                        + " 既不是关卡号也不是海图 id，忽略。");
                }
            }

            string requested = CommandLineOptions.GetValue(ToolFlags.WorldMap);
            if (requested == null)
                return;

            if (WorldMapCatalog.TryGet(requested, out _))
                _pendingMapId = requested;
            else
                Debug.LogWarning(string.Format(
                    "[WorldMapRuntime] -worldMap {0} 不在目录中，忽略（共 {1} 张：wreck_hymn…sunken_gate）",
                    requested, WorldMapCatalog.Count));
        }

        // ------------------------------------------------------------------
        // 战斗计划
        // ------------------------------------------------------------------

        /// <summary>由世界地图构建出战计划（含出生格块高修正，见 <see cref="WorldMapRules.TryRasterize"/>）。</summary>
        public static BattlePlan BuildBattlePlan(WorldMapDefinition map)
        {
            var entries = new List<SpawnPlanEntry>(map.Spawns.Count);
            // AllStandBoxes 每次调用都重建整张 box 表（返回新 List），只读用途——
            // 必须提到 spawn 循环外算一次复用，不能逐出生点重建。
            var boxes = WorldMapRules.AllStandBoxes(map);
            for (int i = 0; i < map.Spawns.Count; i++)
            {
                WorldMapSpawn spawn = map.Spawns[i];
                float surfaceY = WorldMapRules.HeightAtWorld(boxes, new Vector2(spawn.X, spawn.Z));
                int gridX = Mathf.FloorToInt(spawn.X / WorldMapRules.RasterTileSize);
                int gridY = Mathf.FloorToInt(spawn.Z / WorldMapRules.RasterTileSize);
                entries.Add(new SpawnPlanEntry(
                    spawn.TeamIndex, spawn.Archetype, spawn.Luck, gridX, gridY,
                    new Vector3(spawn.X, surfaceY + LevelGeometry.UnitPivotHeight, spawn.Z),
                    // 方案 D 分层军火：初配真值在目录（近程基线 / 船长含全图级旗舰）；
                    // 未配（null）时回落战役惯例，外部构造的 definition 不破。
                    spawn.Archetype.EndsWith("Captain")
                        ? map.CaptainWeapons ?? CaptainWeapons()
                        : map.CrewWeapons ?? CherryBombOnly()));
            }

            WorldMapRules.TryRasterize(map, out int widthTiles, out int depthTiles, out _);
            return new BattlePlan(
                map.LevelNumber, widthTiles, depthTiles, originalXmlPlayers: 1,
                LevelGeometry.WaterSurfaceY, entries);
        }

        static List<WeaponStack> CherryBombOnly() => new List<WeaponStack>
        {
            new WeaponStack(WeaponId.CherryBomb, 10),
        };

        static List<WeaponStack> CaptainWeapons() => new List<WeaponStack>
        {
            new WeaponStack(WeaponId.CherryBomb, 10),
            new WeaponStack(WeaponId.Dynamite, 5),
        };

        // ------------------------------------------------------------------
        // 地形栅格
        // ------------------------------------------------------------------

        /// <summary>
        /// 世界地图 → 逻辑格（块表）。出生点所在格强制为其脚下站面顶高（防格心与出生点跨 box 的高度差）。
        /// 返回 null 表示地图无站面（无效）。
        /// </summary>
        public static TileTerrainGrid BuildTerrainGrid(WorldMapDefinition map)
        {
            if (!WorldMapRules.TryRasterize(map, out int widthTiles, out int depthTiles, out int[] blocks))
                return null;

            var boxes = WorldMapRules.AllStandBoxes(map);
            for (int i = 0; i < map.Spawns.Count; i++)
            {
                WorldMapSpawn spawn = map.Spawns[i];
                var p = new Vector2(spawn.X, spawn.Z);
                int gridX = Mathf.FloorToInt(p.x / WorldMapRules.RasterTileSize);
                int gridY = Mathf.FloorToInt(p.y / WorldMapRules.RasterTileSize);
                if (gridX < 0 || gridY < 0 || gridX >= widthTiles || gridY >= depthTiles)
                    continue;
                float surfaceY = WorldMapRules.HeightAtWorld(boxes, p);
                blocks[gridX + gridY * widthTiles] = Mathf.Max(1, Mathf.RoundToInt(surfaceY / 0.5f));
            }

            return new TileTerrainGrid(widthTiles, depthTiles, blocks, 0.5f);
        }
    }
}
