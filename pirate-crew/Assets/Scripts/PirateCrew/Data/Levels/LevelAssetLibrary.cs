using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PirateCrew.Data
{
    /// <summary>
    /// 关卡资产库——运行时/工具读取关卡数据的**唯一入口**。两条来源，一个视图：
    ///
    ///   ① **Unity 资产**（编辑器与播放器）：<see cref="Resources.Load{T}(string)"/>
    ///      取 <see cref="LevelCatalog"/> 清单，资产是加载路径上的唯一真源。
    ///   ② **golden JSON**（无头验证台 / 批处理工具）：读
    ///      <c>Assets/Data/**/_golden/*.json</c>——与资产逐字段同源（由迁移器同一次写出），
    ///      是"数据外化"的文本锚点，也是 diff 可读的评审面。
    ///
    /// 【为什么两条来源不是双真源】① 是加载通道，② 是同一份数据的文本渲染。门禁
    /// <c>LevelAssetTests.Assets_MatchGoldenJson</c> 逐字节钉住"清单里的资产 == golden JSON"，
    /// 所以两条通道不可能分叉。无头环境没有 AssetDatabase，② 是唯一能让纯 C# 测试
    /// 断言关卡语义的路径。
    ///
    /// 【静态缓存与复位】惰性加载一次并缓存（关卡数据是只读常量语义）。关闭 Domain Reload 时
    /// 静态字段跨播放存活，故由 <c>WorldMapRuntime.ResetStatics</c>（唯一入口的 ResetStatics 阶段）
    /// 调 <see cref="Reset"/> 清空。
    /// </summary>
    public static class LevelAssetLibrary
    {
        /// <summary>加载来源（诊断/测试用）。</summary>
        public enum Source
        {
            /// <summary>还没加载。</summary>
            None = 0,

            /// <summary>Unity 资产清单。</summary>
            UnityAssets = 1,

            /// <summary>golden JSON。</summary>
            GoldenJson = 2,

            /// <summary>两条都没取到（构建配置错误）。</summary>
            Empty = 3,
        }

        static bool _loaded;
        static bool _skipUnityAssets;
        static Source _source = Source.None;
        static string _diagnostic = string.Empty;

        static readonly List<WorldMapAssetPayload> _maps = new List<WorldMapAssetPayload>();
        static readonly Dictionary<string, WorldMapAssetPayload> _mapById =
            new Dictionary<string, WorldMapAssetPayload>(StringComparer.Ordinal);
        static readonly Dictionary<int, WorldMapAssetPayload> _mapByLevel =
            new Dictionary<int, WorldMapAssetPayload>();

        static readonly List<LevelAssetPayload> _levels = new List<LevelAssetPayload>();
        static readonly Dictionary<int, LevelAssetPayload> _levelByNumber = new Dictionary<int, LevelAssetPayload>();

        /// <summary>已加载数据的来源。</summary>
        public static Source LoadedFrom => _loaded ? _source : Source.None;

        /// <summary>最近一次加载的诊断文本（缺清单 / 读到几个文件 / JSON 语法错误）。</summary>
        public static string Diagnostic => _diagnostic;

        /// <summary>
        /// 工程内的 <c>Assets/Data</c> 绝对路径（无头工具与测试定位 golden JSON 用）；
        /// 找不到返回 null（不抛）。
        /// </summary>
        public static string DataRootPath => FindDataRoot();

        /// <summary>全部海图载荷（按关卡号升序，与旧目录的声明顺序一致）。</summary>
        public static IReadOnlyList<WorldMapAssetPayload> WorldMaps
        {
            get
            {
                EnsureLoaded();
                return _maps;
            }
        }

        /// <summary>全部非海图关卡载荷（按关卡号升序）。</summary>
        public static IReadOnlyList<LevelAssetPayload> Levels
        {
            get
            {
                EnsureLoaded();
                return _levels;
            }
        }

        /// <summary>按 id 取海图载荷。</summary>
        public static bool TryGetWorldMap(string id, out WorldMapAssetPayload payload)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id))
            {
                payload = null;
                return false;
            }
            return _mapById.TryGetValue(id, out payload);
        }

        /// <summary>按关卡号取海图载荷。</summary>
        public static bool TryGetWorldMapByLevel(int levelNumber, out WorldMapAssetPayload payload)
        {
            EnsureLoaded();
            return _mapByLevel.TryGetValue(levelNumber, out payload);
        }

        /// <summary>按关卡号取非海图关卡载荷。</summary>
        public static bool TryGetLevel(int levelNumber, out LevelAssetPayload payload)
        {
            EnsureLoaded();
            return _levelByNumber.TryGetValue(levelNumber, out payload);
        }

        /// <summary>清空缓存（唯一入口的 ResetStatics 阶段调用）。</summary>
        public static void Reset()
        {
            _loaded = false;
            _source = Source.None;
            _diagnostic = string.Empty;
            _maps.Clear();
            _mapById.Clear();
            _mapByLevel.Clear();
            _levels.Clear();
            _levelByNumber.Clear();
        }

        /// <summary>
        /// 强制走 golden JSON（跳过 Unity 资产通道）。给"要验证兜底通道本身"的工具与测试用；
        /// 传 <c>false</c> 恢复默认（先试资产清单）。
        /// </summary>
        public static void ForceGoldenJsonOnly(bool goldenJsonOnly)
        {
            _skipUnityAssets = goldenJsonOnly;
            Reset();
        }

        // ------------------------------------------------------------------
        // 加载
        // ------------------------------------------------------------------

        static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;

            LevelCatalog catalog = _skipUnityAssets ? null : TryLoadCatalog();
            if (catalog != null)
            {
                InstallFromCatalog(catalog);
                _source = Source.UnityAssets;
                _diagnostic = string.Format("Unity 资产清单：{0} 张海图 / {1} 张关卡",
                    _maps.Count, _levels.Count);
                return;
            }

            int files = LoadFromGoldenJson();
            if (files > 0)
            {
                _source = Source.GoldenJson;
                _diagnostic = string.Format("golden JSON：{0} 张海图 / {1} 张关卡",
                    _maps.Count, _levels.Count);
                return;
            }

            _source = Source.Empty;
            _diagnostic = "未取到任何关卡数据（Unity 资产清单缺失，且 golden JSON 不可读）";
        }

        /// <summary>
        /// 取 Unity 资产清单。<c>Resources.Load</c> 是原生 ECall：无头验证台没有 Unity 运行时，
        /// 调用必抛 <c>SecurityException</c>——这不是错误路径，正是"无头环境下退回 golden JSON"
        /// 的判据（见 harness README「能力边界」）。
        /// </summary>
        static LevelCatalog TryLoadCatalog()
        {
            try
            {
                return Resources.Load<LevelCatalog>(LevelAssetSchema.CatalogResourcePath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void InstallFromCatalog(LevelCatalog catalog)
        {
            IReadOnlyList<LevelDefinition> levels = catalog.Levels;
            for (int i = 0; i < levels.Count; i++)
            {
                LevelDefinition asset = levels[i];
                if (asset != null)
                    AddLevel(asset.Data);
            }

            IReadOnlyList<WorldMapDefinitionAsset> maps = catalog.WorldMaps;
            for (int i = 0; i < maps.Count; i++)
            {
                WorldMapDefinitionAsset asset = maps[i];
                if (asset != null)
                    AddMap(asset.Data);
            }

            SortByLevelNumber();
        }

        static int LoadFromGoldenJson()
        {
            string dataRoot = FindDataRoot();
            if (dataRoot == null)
                return 0;

            int files = LoadGoldenDir(Path.Combine(dataRoot, "WorldMaps", "_golden"), isWorldMap: true);
            files += LoadGoldenDir(Path.Combine(dataRoot, "Levels", "_golden"), isWorldMap: false);
            if (files > 0)
                SortByLevelNumber();
            return files;
        }

        static int LoadGoldenDir(string dir, bool isWorldMap)
        {
            if (!Directory.Exists(dir))
                return 0;

            string[] paths = Directory.GetFiles(dir, "*.json");
            Array.Sort(paths, StringComparer.Ordinal);

            int loaded = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                string json;
                try
                {
                    json = File.ReadAllText(paths[i]);
                }
                catch (IOException)
                {
                    continue;
                }

                if (isWorldMap)
                {
                    WorldMapAssetPayload payload = LevelAssetJson.ReadWorldMap(json);
                    if (payload == null || string.IsNullOrEmpty(payload.id))
                    {
                        _diagnostic = "golden JSON 解析失败：" + paths[i];
                        continue;
                    }
                    AddMap(payload);
                }
                else
                {
                    LevelAssetPayload payload = LevelAssetJson.ReadLevel(json);
                    if (payload == null)
                    {
                        _diagnostic = "golden JSON 解析失败：" + paths[i];
                        continue;
                    }
                    AddLevel(payload);
                }
                loaded++;
            }
            return loaded;
        }

        static void AddMap(WorldMapAssetPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.id))
                return;
            _maps.Add(payload);
            _mapById[payload.id] = payload;
            _mapByLevel[payload.levelNumber] = payload;
        }

        static void AddLevel(LevelAssetPayload payload)
        {
            if (payload == null)
                return;
            _levels.Add(payload);
            _levelByNumber[payload.levelNumber] = payload;
        }

        static void SortByLevelNumber()
        {
            _maps.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));
            _levels.Sort((a, b) => a.levelNumber.CompareTo(b.levelNumber));
        }

        // ------------------------------------------------------------------
        // 路径发现
        // ------------------------------------------------------------------

        /// <summary>
        /// 找工程内的 <c>Assets/Data</c> 目录。编辑器/播放器里取当前工作目录，
        /// 无头验证台里从程序集所在目录向上走到仓库根（两种部署都能命中）。
        /// 找不到返回 null（不抛——数据缺失由 <see cref="Diagnostic"/> 与校验器报告）。
        /// </summary>
        static string FindDataRoot()
        {
            string[] roots =
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
            };

            for (int i = 0; i < roots.Length; i++)
            {
                DirectoryInfo dir = TryDirectory(roots[i]);
                while (dir != null)
                {
                    string direct = Path.Combine(dir.FullName, "Assets", "Data");
                    if (Directory.Exists(direct))
                        return direct;

                    string nested = Path.Combine(dir.FullName, "pirate-crew", "Assets", "Data");
                    if (Directory.Exists(nested))
                        return nested;

                    dir = dir.Parent;
                }
            }
            return null;
        }

        static DirectoryInfo TryDirectory(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? null : new DirectoryInfo(path);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
