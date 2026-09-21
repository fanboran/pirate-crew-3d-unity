using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle.Levels;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 关卡资产校验器：遍历工程里的全部关卡资产，逐条判「能不能进包」。
    ///
    /// 【判什么】（失败的判据一律报错；标【告警】的只报数）
    ///   ① 文件层：golden JSON 是否存在、**资产正文是否等于 golden JSON 的确定性渲染**
    ///      （这条保证"资产与文本锚点永不漂移"）；
    ///   ② 清单层：全部资产都已登记进 <see cref="LevelCatalog"/>、无空引用、无重复 id / 关卡号；
    ///   ③ 语义层：连通性 / 出生点落位 / 地形高度范围 / 武器 id 是否在 WeaponCatalog /
    ///      站位是否在实心格 / 编成双方非空——规则体在 <see cref="LevelAssetRules"/>，
    ///      与内容门禁测试（<c>Assets/Tests/Battle/LevelAssetTests.cs</c>）**共用同一份实现**；
    ///   ④ 尺度：【告警】双方出生质心间距超 120u（提案/待定的平衡口径，不阻断）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/关卡/校验关卡资产
    ///   无头: -executeMethod PirateCrew.EditorTools.LevelAssetValidator.ValidateAll
    ///         （有错时写 Console Error；硬门禁是 EditMode 的内容门禁用例）
    /// </summary>
    public static class LevelAssetValidator
    {
        const string WorldMapsFolder = "Assets/Data/WorldMaps";
        const string WorldMapsGolden = WorldMapsFolder + "/_golden";
        const string LevelsFolder = "Assets/Data/Levels";
        const string LevelsGolden = LevelsFolder + "/_golden";
        const string CatalogPath = LevelsFolder + "/Resources/LevelCatalog.asset";
        const string LevelScriptPath = "Assets/Scripts/PirateCrew/Data/Levels/LevelDefinition.cs";
        const string WorldMapScriptPath = "Assets/Scripts/PirateCrew/Data/Levels/WorldMapDefinitionAsset.cs";

        [MenuItem("PirateCrew/关卡/校验关卡资产")]
        public static void ValidateAll()
        {
            Report report = Validate();
            if (report.Errors.Count == 0)
            {
                Debug.Log("[LevelAssetValidator] 全部通过。" + report.Summary
                    + (report.Warnings.Count > 0 ? "（告警 " + report.Warnings.Count + " 条）" : string.Empty)
                    + (report.Warnings.Count > 0 ? "\n" + string.Join("\n", report.Warnings) : string.Empty));
                return;
            }

            Debug.LogError("[LevelAssetValidator] " + report.Summary + " —— 失败 " + report.Errors.Count
                + " 条：\n" + string.Join("\n", report.Errors)
                + (report.Warnings.Count > 0
                    ? "\n告警 " + report.Warnings.Count + " 条：\n" + string.Join("\n", report.Warnings)
                    : string.Empty));
        }

        /// <summary>校验结果（无副作用，供 EditMode 内容门禁用例直接断言）。</summary>
        public sealed class Report
        {
            /// <summary>硬失败项（必须修，否则不允许进包）。</summary>
            public readonly List<string> Errors = new List<string>();

            /// <summary>【告警】项（提案/待定的平衡口径，不阻断）。</summary>
            public readonly List<string> Warnings = new List<string>();

            /// <summary>一句话摘要（资产张数）。</summary>
            public string Summary;
        }

        /// <summary>
        /// 遍历全部关卡资产做完整校验。用例与菜单走同一条实现——工具里绿、测试里红这种事不该发生。
        /// </summary>
        public static Report Validate()
        {
            var report = new Report();
            List<string> errors = report.Errors;
            List<string> warnings = report.Warnings;

            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null)
            {
                errors.Add("缺资源清单 " + CatalogPath + "（跑迁移器生成）");
            }

            // ---- 海图 ----
            var registeredIds = new HashSet<string>();
            var seenIds = new HashSet<string>();
            var seenLevels = new HashSet<int>();
            string[] mapGuids = AssetDatabase.FindAssets("t:WorldMapDefinitionAsset", new[] { WorldMapsFolder });
            for (int i = 0; i < mapGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(mapGuids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<WorldMapDefinitionAsset>(path);
                if (asset == null)
                    continue;

                WorldMapAssetPayload payload = asset.Data;
                string id = payload.id;
                if (string.IsNullOrEmpty(id))
                {
                    errors.Add(path + " 的载荷缺 id");
                    continue;
                }
                if (!seenIds.Add(id))
                    errors.Add(path + " 的 id 与另一张海图重复：" + id);
                if (payload.levelNumber != 0 && !seenLevels.Add(payload.levelNumber))
                    errors.Add(path + " 的关卡号与另一张海图重复：" + payload.levelNumber);

                GoldenAndTextChecks(errors,
                    WorldMapsGolden + "/" + id + ".json", path,
                    LevelAssetYaml.WriteWorldMap(payload, id, LevelAssetYaml.GuidFor(WorldMapScriptPath)));

                WorldMapDefinition runtime = WorldMapFromAsset.ToRuntime(payload);
                Collect(errors, warnings, path, LevelAssetRules.Problems(runtime));

                if (catalog != null && !Contains(catalog.WorldMaps, asset))
                    errors.Add(path + " 没有登记进 " + CatalogPath);
                registeredIds.Add(id);
            }

            // ---- 关卡 ----
            var registeredAssetNames = new HashSet<string>();
            var seenNames = new HashSet<string>();
            string[] levelGuids = AssetDatabase.FindAssets("t:LevelDefinition", new[] { LevelsFolder });
            for (int i = 0; i < levelGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(levelGuids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                if (asset == null)
                    continue;

                LevelAssetPayload payload = asset.Data;
                string name = payload.assetName;
                if (string.IsNullOrEmpty(name))
                {
                    errors.Add(path + " 的载荷缺 assetName");
                    continue;
                }
                if (!seenNames.Add(name))
                    errors.Add(path + " 的 assetName 与另一张关卡重复：" + name);

                GoldenAndTextChecks(errors,
                    LevelsGolden + "/" + name + ".json", path,
                    LevelAssetYaml.WriteLevel(payload, name, LevelAssetYaml.GuidFor(LevelScriptPath)));

                Collect(errors, warnings, path, LevelAssetRules.Problems(payload));

                if (catalog != null && !Contains(catalog.Levels, asset))
                    errors.Add(path + " 没有登记进 " + CatalogPath);
                registeredAssetNames.Add(name);
            }

            // ---- 清单里的空引用与漏项 ----
            if (catalog != null)
            {
                for (int i = 0; i < catalog.WorldMaps.Count; i++)
                {
                    WorldMapDefinitionAsset asset = catalog.WorldMaps[i];
                    if (asset == null)
                        errors.Add(CatalogPath + " 的 worldMaps[" + i + "] 是空引用");
                    else if (!seenIds.Contains(asset.Id))
                        errors.Add(CatalogPath + " 登记了不存在的海图 " + asset.Id);
                }
                for (int i = 0; i < catalog.Levels.Count; i++)
                {
                    LevelDefinition asset = catalog.Levels[i];
                    if (asset == null)
                        errors.Add(CatalogPath + " 的 levels[" + i + "] 是空引用");
                    else if (!seenNames.Contains(asset.Data.assetName))
                        errors.Add(CatalogPath + " 登记了不存在的关卡 " + asset.Data.assetName);
                }
            }

            report.Summary = string.Format(
                "海图 {0} 张 / 关卡 {1} 张；登记 {2} 张海图 + {3} 张关卡",
                seenIds.Count, seenNames.Count, registeredIds.Count, registeredAssetNames.Count);
            return report;
        }

        /// <summary>golden JSON 存在 + 资产正文 == golden JSON 的确定性渲染。</summary>
        static void GoldenAndTextChecks(List<string> errors, string goldenPath, string assetPath, string expectedText)
        {
            if (!File.Exists(goldenPath))
            {
                errors.Add(assetPath + " 缺 golden JSON（" + goldenPath + "）");
                return;
            }

            string actual = File.ReadAllText(assetPath);
            // 行尾归一化：仓库对 .asset 钉了 eol=lf，但有人手工保存成 CRLF 时不该误报"内容不同"。
            if (Normalize(actual) != Normalize(expectedText))
            {
                errors.Add(assetPath + " 正文与 golden JSON 的确定性渲染不一致"
                    + "（在 Inspector 里手改过资产？改完请跑「回写 golden JSON」）");
            }
        }

        static string Normalize(string text) => text.Replace("\r\n", "\n");

        static void Collect(List<string> errors, List<string> warnings, string path, List<string> problems)
        {
            for (int i = 0; i < problems.Count; i++)
            {
                string message = path + " " + problems[i];
                if (problems[i].StartsWith("【告警】", System.StringComparison.Ordinal))
                    warnings.Add(message);
                else
                    errors.Add(message);
            }
        }

        static bool Contains<T>(IReadOnlyList<T> list, T value) where T : Object
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                    return true;
            }
            return false;
        }
    }
}
