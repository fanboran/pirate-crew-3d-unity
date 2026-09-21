using System;
using System.Collections.Generic;
using PirateCrew.Data;

namespace PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 大海域世界地图目录——**数据来自关卡资产**，不在代码里。
    ///
    /// 【本类现在只做两件事】① 把 <see cref="LevelAssetLibrary"/> 加载到的海图载荷
    /// （<see cref="WorldMapAssetPayload"/>，来源 = Unity 资产清单或 golden JSON）转成运行时
    /// <see cref="WorldMapDefinition"/>；② 维持按 id / 关卡号的查表接口（UI、战役、待战入口都只认它）。
    ///
    /// 【数据在哪】`Assets/Data/WorldMaps/*.asset`（唯一真源）+ `Assets/Data/WorldMaps/_golden/*.json`
    /// （文本锚点，逐字段同源）。改一张图请改 golden JSON 后跑迁移器，
    /// 见 <c>docs/技术/架构/关卡数据资产.md</c>。
    ///
    /// 【坐标/连通性契约（未变）】地图占据 [0..SpanX]×[0..SpanZ]，世界系（Y-up，米）；
    /// 承载出生点的站面 box 必须同连通分量（<see cref="WorldMapRules"/> BFS，
    /// 由 WorldMapConnectivityTests 断言）；关卡号占用 101–108，与原版转写 1–33 不冲突。
    /// </summary>
    public static class WorldMapCatalog
    {
        /// <summary>海图关卡号的起始值（101–108 段）。</summary>
        public const int FirstLevelNumber = 101;

        static List<WorldMapDefinition> _all;
        static Dictionary<string, WorldMapDefinition> _byId;
        static Dictionary<int, WorldMapDefinition> _byLevelNumber;

        /// <summary>全部海图（按关卡号升序）。</summary>
        public static IReadOnlyList<WorldMapDefinition> All
        {
            get
            {
                EnsureBuilt();
                return _all;
            }
        }

        /// <summary>海图张数。</summary>
        public static int Count
        {
            get
            {
                EnsureBuilt();
                return _all.Count;
            }
        }

        /// <summary>按 id 取海图（id 为空返回 false，不抛）。</summary>
        public static bool TryGet(string id, out WorldMapDefinition map)
        {
            EnsureBuilt();
            if (string.IsNullOrEmpty(id))
            {
                map = null;
                return false;
            }
            return _byId.TryGetValue(id, out map);
        }

        /// <summary>按关卡号取海图。</summary>
        public static bool TryGetByLevelNumber(int levelNumber, out WorldMapDefinition map)
        {
            EnsureBuilt();
            return _byLevelNumber.TryGetValue(levelNumber, out map);
        }

        /// <summary>
        /// 丢弃缓存（唯一入口的 ResetStatics 阶段经 <c>WorldMapRuntime.ResetStatics</c> 调用）。
        /// 需要在同一播放里重新读资产时也可显式调。
        /// </summary>
        public static void ResetCache()
        {
            _all = null;
            _byId = null;
            _byLevelNumber = null;
        }

        static void EnsureBuilt()
        {
            if (_all != null)
                return;

            IReadOnlyList<WorldMapAssetPayload> payloads = LevelAssetLibrary.WorldMaps;
            _all = new List<WorldMapDefinition>(payloads.Count);
            _byId = new Dictionary<string, WorldMapDefinition>(payloads.Count, StringComparer.Ordinal);
            _byLevelNumber = new Dictionary<int, WorldMapDefinition>(payloads.Count);

            for (int i = 0; i < payloads.Count; i++)
            {
                WorldMapDefinition map = WorldMapFromAsset.ToRuntime(payloads[i]);
                if (map == null)
                    continue;
                _all.Add(map);
                if (!_byId.ContainsKey(map.Id))
                    _byId.Add(map.Id, map);
                if (!_byLevelNumber.ContainsKey(map.LevelNumber))
                    _byLevelNumber.Add(map.LevelNumber, map);
            }
        }
    }
}
