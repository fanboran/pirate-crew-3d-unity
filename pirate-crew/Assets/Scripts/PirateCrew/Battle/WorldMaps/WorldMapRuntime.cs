using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界地图的运行时入口：待战状态、命令行解析、<see cref="BattlePlan"/> 构建。
    ///
    /// 【进图途径】① 程序调用 <see cref="SetPending"/>（选关 UI 的「大海域」页签走这条）；
    /// ② 播放器/批处理命令行 <c>-worldMap &lt;id&gt;</c>（无头捕图与试玩验证走这条，
    ///    与 ArtReview 的 <c>-artReviewLevel</c> 同风格，但优先级低于它）。
    ///
    /// 【与 BattleController 的契约】进图优先级：样板覆盖（-artReviewLevel 1–3，美术出图）&gt;
    /// 世界地图 &gt; 样板第 1 关兜底（直接 Play）；世界地图激活时 BuildTerrain 走栅格化块表、
    /// 陈设由 <see cref="WorldMapComposer"/> 负责表现层。一代退场后这是**唯一的玩法进图通道**，
    /// 战役结算归属也由它决定（<c>CampaignApi</c> 在 battle_started 时读取）。
    /// </summary>
    public static class WorldMapRuntime
    {
        const string CommandLineSwitch = "-worldMap";

        static string _pendingMapId;
        static bool _commandLineScanned;

        /// <summary>设定待战世界地图（id 不在目录时返回 false，不改动现状）。</summary>
        public static bool SetPending(string mapId)
        {
            if (!WorldMapCatalog.TryGet(mapId, out _))
                return false;
            _pendingMapId = mapId;
            return true;
        }

        public static void ClearPending() => _pendingMapId = null;

        /// <summary>
        /// 关闭 Domain Reload 时静态字段不会自动清空，进入播放前强制重置
        /// （与 <c>BattleLaunchContext.ResetOnEnterPlayMode</c> 同一手法）；
        /// 命令行扫描标志一并复位，让每次播放重新解析 <c>-worldMap</c>。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode()
        {
            ClearPending();
            _commandLineScanned = false;
        }

        /// <summary>
        /// 取当前待战地图（命令行 <c>-worldMap</c> 优先，其次 <see cref="SetPending"/>）。
        /// 返回 false 表示本局不走世界地图。
        /// </summary>
        public static bool TryGetPending(out WorldMapDefinition map)
        {
            ScanCommandLine();
            if (_pendingMapId != null && WorldMapCatalog.TryGet(_pendingMapId, out map))
                return true;
            map = null;
            return false;
        }

        static void ScanCommandLine()
        {
            if (_commandLineScanned)
                return;
            _commandLineScanned = true;
            string[] args = EnvironmentGetArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.CompareOrdinal(args[i], CommandLineSwitch) == 0)
                {
                    if (WorldMapCatalog.TryGet(args[i + 1], out _))
                        _pendingMapId = args[i + 1];
                    else
                        Debug.LogWarning(string.Format(
                            "[WorldMapRuntime] -worldMap {0} 不在目录中，忽略（共 {1} 张：wreck_hymn…sunken_gate）",
                            args[i + 1], WorldMapCatalog.Count));
                    return;
                }
            }
        }

        static string[] EnvironmentGetArgs() => System.Environment.GetCommandLineArgs();

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
