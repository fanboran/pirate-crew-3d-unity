using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 关卡资产迁移器：**golden JSON ⇄ `.asset`** 的双向工具。
    ///
    /// 【它是什么】本次「数据外化」的交付工具。关卡数据从 C# 硬编码表搬出后，
    /// 唯一真源是 `Assets/Data/**/*.asset`；`Assets/Data/**/_golden/*.json` 是同一份数据的
    /// 文本锚点（逐字节对拍用，见 <c>Assets/Tests/Battle/LevelAssetTests.cs</c>）。
    ///
    /// 【为什么不是"C# → 资产"的单向脚本】那一步在迁移时只跑过一次，输入（旧 C# 表）已按判据 5
    /// 从源码里删除，只留在 git 历史。留下来的、必须能重复跑的是**改数据之后的重新落盘**：
    ///   ① 策划/设计改 golden JSON（或直接改资产的 Inspector）→ 跑迁移器 → 资产重写 + 清单重写；
    ///   ② 反向：资产在 Inspector 里被调过 → 跑「资产 → golden JSON」把文本锚点同步回去。
    /// 两条方向都走同一个 YAML 写出器（<see cref="LevelAssetYaml"/>），所以无论从哪边改，
    /// "资产文本 == golden JSON" 这条门禁都成立。
    ///
    /// 【确定性】写出内容只由载荷决定（字段顺序、缩进、数字格式都固定），重复跑逐字节一致；
    /// 清单里的引用 guid 由资产路径确定性派生（<see cref="LevelAssetYaml.GuidFor"/>），
    /// 不依赖 Unity 的 guid 分配，故一次生成即可入库。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/关卡/迁移关卡资产（golden JSON → 资产）
    ///         PirateCrew/关卡/回写 golden JSON（资产 → 文本锚点）
    ///   无头: -executeMethod PirateCrew.EditorTools.LevelDataMigrator.MigrateAll
    ///         -executeMethod PirateCrew.EditorTools.LevelDataMigrator.WriteGoldenFromAssets
    /// </summary>
    public static class LevelDataMigrator
    {
        const string WorldMapsFolder = "Assets/Data/WorldMaps";
        const string WorldMapsGolden = WorldMapsFolder + "/_golden";
        const string LevelsFolder = "Assets/Data/Levels";
        const string LevelsGolden = LevelsFolder + "/_golden";
        const string CatalogPath = LevelsFolder + "/Resources/LevelCatalog.asset";

        const string LevelScriptPath = "Assets/Scripts/PirateCrew/Data/Levels/LevelDefinition.cs";
        const string WorldMapScriptPath = "Assets/Scripts/PirateCrew/Data/Levels/WorldMapDefinitionAsset.cs";
        const string CatalogScriptPath = "Assets/Scripts/PirateCrew/Data/Levels/LevelCatalog.cs";

        // ==================================================================
        // ① golden JSON → 资产
        // ==================================================================

        [MenuItem("PirateCrew/关卡/迁移关卡资产（golden JSON → 资产）")]
        public static void MigrateAll()
        {
            EnsureFolder("Assets/Data");
            EnsureFolder(LevelsFolder);
            EnsureFolder(LevelsGolden);
            EnsureFolder(LevelsFolder + "/Resources");
            EnsureFolder(WorldMapsFolder);
            EnsureFolder(WorldMapsGolden);

            var levelAssets = new List<LevelDefinition>();
            var mapAssets = new List<WorldMapDefinitionAsset>();
            var problems = new List<string>();

            string[] mapJsons = GoldenFiles(WorldMapsGolden);
            for (int i = 0; i < mapJsons.Length; i++)
            {
                WorldMapAssetPayload payload = LevelAssetJson.ReadWorldMap(File.ReadAllText(mapJsons[i]));
                if (payload == null || string.IsNullOrEmpty(payload.id))
                {
                    problems.Add("golden JSON 不是合法海图载荷：" + mapJsons[i]);
                    continue;
                }

                string assetPath = WorldMapsFolder + "/" + payload.id + ".asset";
                WorldMapDefinitionAsset asset = EnsureAsset<WorldMapDefinitionAsset>(assetPath);
                asset.Apply(payload);
                WriteAssetText(assetPath, LevelAssetYaml.WriteWorldMap(
                    payload, payload.id, LevelAssetYaml.GuidFor(WorldMapScriptPath)));
                mapAssets.Add(asset);
            }

            string[] levelJsons = GoldenFiles(LevelsGolden);
            for (int i = 0; i < levelJsons.Length; i++)
            {
                LevelAssetPayload payload = LevelAssetJson.ReadLevel(File.ReadAllText(levelJsons[i]));
                if (payload == null || string.IsNullOrEmpty(payload.assetName))
                {
                    problems.Add("golden JSON 不是合法关卡载荷：" + levelJsons[i]);
                    continue;
                }

                string assetPath = LevelsFolder + "/" + payload.assetName + ".asset";
                LevelDefinition asset = EnsureAsset<LevelDefinition>(assetPath);
                asset.Apply(payload);
                WriteAssetText(assetPath, LevelAssetYaml.WriteLevel(
                    payload, payload.assetName, LevelAssetYaml.GuidFor(LevelScriptPath)));
                levelAssets.Add(asset);
            }

            SortByLevelNumber(levelAssets);
            SortByLevelNumber(mapAssets);

            LevelCatalog catalog = EnsureAsset<LevelCatalog>(CatalogPath);
            catalog.Apply(levelAssets, mapAssets);
            WriteAssetText(CatalogPath, LevelAssetYaml.WriteCatalog(
                "LevelCatalog", LevelAssetYaml.GuidFor(CatalogScriptPath),
                AssetGuids(LevelsFolder, LevelAssetNames(levelAssets)),
                AssetGuids(WorldMapsFolder, MapNames(mapAssets))));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Report("迁移关卡资产", "写 " + mapAssets.Count + " 张海图 / " + levelAssets.Count + " 张关卡 + 1 份清单",
                problems);
        }

        // ==================================================================
        // ② 资产 → golden JSON
        // ==================================================================

        [MenuItem("PirateCrew/关卡/回写 golden JSON（资产 → 文本锚点）")]
        public static void WriteGoldenFromAssets()
        {
            var problems = new List<string>();
            int written = 0;

            string[] mapGuids = AssetDatabase.FindAssets("t:WorldMapDefinitionAsset", new[] { WorldMapsFolder });
            for (int i = 0; i < mapGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(mapGuids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<WorldMapDefinitionAsset>(path);
                if (asset == null)
                    continue;
                WriteText(WorldMapsGolden + "/" + asset.Id + ".json", LevelAssetJson.Write(asset.Data));
                written++;
            }

            string[] levelGuids = AssetDatabase.FindAssets("t:LevelDefinition", new[] { LevelsFolder });
            for (int i = 0; i < levelGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(levelGuids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                if (asset == null)
                    continue;
                WriteText(LevelsGolden + "/" + asset.Data.assetName + ".json", LevelAssetJson.Write(asset.Data));
                written++;
            }

            AssetDatabase.Refresh();
            Report("回写 golden JSON", "写 " + written + " 份 JSON", problems);
        }

        // ==================================================================
        // 辅助
        // ==================================================================

        /// <summary>
        /// 资产正文由本类自己写盘（而不是交给 Unity 序列化）：Unity 的 YAML 写法随版本浮动，
        /// 而"提交进仓库的资产 == golden JSON 的确定性渲染"这条门禁要的是逐字节稳定。
        /// 写完立刻 ImportAsset 让 Unity 认账。
        /// </summary>
        static void WriteAssetText(string assetPath, string text)
        {
            File.WriteAllText(assetPath, text, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        static void WriteText(string assetPath, string text)
        {
            string dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(assetPath, text, new System.Text.UTF8Encoding(false));
        }

        static string[] GoldenFiles(string folder)
        {
            if (!Directory.Exists(folder))
                return new string[0];
            string[] files = Directory.GetFiles(folder, "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            return files;
        }

        static List<string> LevelAssetNames(List<LevelDefinition> assets)
        {
            var names = new List<string>(assets.Count);
            for (int i = 0; i < assets.Count; i++)
                names.Add(assets[i].Data.assetName);
            return names;
        }

        static List<string> MapNames(List<WorldMapDefinitionAsset> assets)
        {
            var names = new List<string>(assets.Count);
            for (int i = 0; i < assets.Count; i++)
                names.Add(assets[i].Id);
            return names;
        }

        static List<string> AssetGuids(string folder, List<string> names)
        {
            var guids = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++)
                guids.Add(LevelAssetYaml.GuidFor(folder + "/" + names[i] + ".asset"));
            return guids;
        }

        static void SortByLevelNumber(List<LevelDefinition> assets)
        {
            assets.Sort((a, b) => a.LevelNumber.CompareTo(b.LevelNumber));
        }

        static void SortByLevelNumber(List<WorldMapDefinitionAsset> assets)
        {
            assets.Sort((a, b) => a.LevelNumber.CompareTo(b.LevelNumber));
        }

        static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void Report(string what, string summary, List<string> problems)
        {
            if (problems.Count == 0)
            {
                Debug.Log("[" + what + "] 完成：" + summary);
                return;
            }
            Debug.LogError("[" + what + "] 完成但有问题 " + problems.Count + " 条：" + summary + "\n"
                + string.Join("\n", problems));
        }
    }
}
