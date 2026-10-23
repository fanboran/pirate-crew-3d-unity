using System.Collections.Generic;
using System.IO;
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
    ///         PirateCrew/关卡/烘焙逐格高度入 golden（schema 3→4，第 2 关空岛）
    ///   无头: -executeMethod PirateCrew.EditorTools.LevelDataMigrator.MigrateAll
    ///         -executeMethod PirateCrew.EditorTools.LevelDataMigrator.WriteGoldenFromAssets
    ///         -executeMethod PirateCrew.EditorTools.LevelDataMigrator.BakeVoxelHeights
    /// </summary>
    public static class LevelDataMigrator
    {
        const string LevelsFolder = "Assets/Data/Levels";
        const string LevelsGolden = LevelsFolder + "/_golden";
        const string CatalogPath = LevelsFolder + "/Resources/LevelCatalog.asset";

        const string LevelScriptPath = "Assets/Scripts/PirateCrew/Data/Levels/LevelDefinition.cs";
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

            var levelAssets = new List<LevelDefinition>();
            var problems = new List<string>();

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

            LevelCatalog catalog = EnsureAsset<LevelCatalog>(CatalogPath);
            catalog.Apply(levelAssets);
            WriteAssetText(CatalogPath, LevelAssetYaml.WriteCatalog(
                "LevelCatalog", LevelAssetYaml.GuidFor(CatalogScriptPath),
                AssetGuids(LevelsFolder, LevelAssetNames(levelAssets))));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (problems.Count == 0)
                MigrateAll();

            Report("迁移关卡资产", "写 " + levelAssets.Count + " 张关卡 + 1 份清单",
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
        // ③ 烘焙逐格高度（schema 3→4，第 2 关空岛穹顶 → LevelData 类体素）
        // ==================================================================

        /// <summary>
        /// 把第 2 关空岛的草皮穹顶按 2 m 采样格烘焙进 golden JSON（幂等，batchmode 可调：
        /// <c>-executeMethod PirateCrew.EditorTools.LevelDataMigrator.BakeVoxelHeights</c>）。
        ///
        /// 【它做三件事】
        ///   ① schema 升版：golden JSON 的 <c>"schema": 3</c> 行升到 4（一次性文本替换——
        ///      读侧按版本拒读，升版必须先于解析）；没烘高度的文件**只动这一行、逐字节保留原文**
        ///      （存量 golden 有旧排版文件，整写会把排版一起规范化，评审面就没了）；
        ///   ② 第 2 关采样：<see cref="PirateCrew.SceneArt.FloatingIslandHeightsBake"/> 按
        ///      Battle 场景里空岛根的**实际世界位**采样 <c>PlateauY</c> 写进岛的逐格高度字段，
        ///      该文件走确定性写出器整写（新字段要插进岛对象）；
        ///   ③ 落盘后重跑 <see cref="MigrateAll"/> 把 `.asset` 与清单对齐（门禁逐字节对拍）。
        /// </summary>
        [MenuItem("PirateCrew/关卡/烘焙逐格高度入 golden（schema 3→4，第 2 关）")]
        public static void BakeVoxelHeights()
        {
            var problems = new List<string>();
            int schemaBumped = 0, rewritten = 0, bakedIslands = 0;

            // 第 2 关采样要贴的是「场景里实际摆的穹顶」——根位取 Battle 场景的实例，不抄常量。
            Vector3? islandRoot = null;
            bool rootMissing = false;

            string[] levelJsons = GoldenFiles(LevelsGolden);
            for (int i = 0; i < levelJsons.Length; i++)
            {
                string text = File.ReadAllText(levelJsons[i]);
                string parseable = BumpSchema3To4(text);
                if (!ReferenceEquals(text, parseable))
                    schemaBumped++;

                LevelAssetPayload payload = LevelAssetJson.ReadLevel(parseable);
                if (payload == null || string.IsNullOrEmpty(payload.assetName))
                {
                    problems.Add("golden JSON 不是合法关卡载荷：" + levelJsons[i]);
                    continue;
                }

                bool bakedThis = false;
                if (payload.levelNumber == 2 && payload.terrain.islands != null && !rootMissing)
                {
                    if (islandRoot == null)
                    {
                        islandRoot = FindSkyIslandRootWorld(problems);
                        if (islandRoot == null)
                            rootMissing = true;   // 根没取到：跳过采样，其余关卡照常升版重写。
                    }
                    if (islandRoot != null)
                    {
                        for (int k = 0; k < payload.terrain.islands.Count; k++)
                        {
                            payload.terrain.islands[k] = PirateCrew.SceneArt.FloatingIslandHeightsBake.Bake(
                                payload.terrain.islands[k], PirateCrew.SceneArt.FloatingIslandSpec.Default,
                                islandRoot.Value, payload.sizeX, payload.sizeZ);
                            bakedIslands++;
                        }
                        bakedThis = true;
                    }
                }

                if (bakedThis)
                {
                    // 烘过高度的文件必须整写（新字段要插进岛对象）——走确定性写出器。
                    WriteText(levelJsons[i], LevelAssetJson.Write(payload));
                }
                else
                {
                    // 没烘高度的文件**只升 schema 行、逐字节保留原文**——存量 golden 里还有
                    // 旧排版的文件（如 1 空格缩进的 cloud_walk），整写会把排版一起规范化，
                    // "除 schema 行外零改动"的评审面就没了。可读性已由上面的解析证明。
                    WriteText(levelJsons[i], parseable);
                }
                rewritten++;
            }

            AssetDatabase.Refresh();

            Report("烘焙逐格高度入 golden",
                "schema 升版 " + schemaBumped + " 份 / 重写 " + rewritten + " 份 / 烘焙岛 " + bakedIslands
                + " 座（根位 " + (islandRoot != null ? islandRoot.Value.ToString("0.##") : "未取到") + "）",
                problems);
        }

        /// <summary>
        /// schema 3→4 的一次性升版替换：文本已是当前版本时原样返回（同一实例）；
        /// 不是 3 也不是当前版本时原样返回，交给解析侧按坏数据拒绝。
        /// 写侧数字格式固定（<c>"schema": 3</c>），整文件只有这一处该字面量。
        /// </summary>
        static string BumpSchema3To4(string text)
        {
            const string oldLine = "\"schema\": 3";
            const string newLine = "\"schema\": 4";
            if (!text.Contains(oldLine))
                return text;
            return text.Replace(oldLine, newLine);
        }

        /// <summary>
        /// 取 Battle 场景里空岛根的世界位（additive 打开、读完即关，不保存不弄脏）。
        /// 取不到（场景未接线 SkyIslandRoot）返回 null 并记问题——烘焙贴的是场景实况，
        /// 宁可失败也不拿常量猜。
        /// </summary>
        static Vector3? FindSkyIslandRootWorld(List<string> problems)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BuildSystem.BuildScenes.PathOf("Battle"), UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                PirateCrew.Battle.RuntimeSceneArt sceneArt =
                    Object.FindObjectOfType<PirateCrew.Battle.RuntimeSceneArt>();
                GameObject root = sceneArt != null ? sceneArt.SkyIslandRoot : null;
                if (root == null)
                {
                    problems.Add("Battle 场景未接线空岛根（SkyIslandRoot）——先跑 "
                        + "PirateCrew/Showcase/摆进战斗场景居中（第2关地面）再烘焙");
                    return null;
                }
                return root.transform.position;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
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
