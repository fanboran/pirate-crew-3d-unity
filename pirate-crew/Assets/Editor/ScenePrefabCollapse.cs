using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **场景 → Prefab 折叠器**：把场景里的全部根对象收进一个载体根，存成 Prefab 资产，
    /// 场景只留该 Prefab 的一个实例。
    ///
    /// 【为什么需要它】Battle / MainMenu / LevelSelect / CrewManagement 四个场景都是装配脚本的产物，
    /// 内容全以"裸对象"烤在 .unity 文件里（Battle 折叠前 199 对象 / 18,772 行）：
    ///   · 改一处改不了全部——同一个结构在场景里只有一份，无复用单元；
    ///   · 场景 diff 无法复核——一次改动等于看天书，等价性只能靠转储比对；
    ///   · PlayMode 测试要加载整份场景才能拿到战场结构。
    /// 折叠后场景文件退化成"一个实例"（几十行），内容落在可按模块定位、可在 Prefab 模式里
    /// 单独打开、可被别的场景复用的 Prefab 资产里。
    ///
    /// 【定位：Prefab 仍是**生成产物**】装配脚本（<see cref="ArtGate.BuildAll"/> 链）才是真源，
    /// 本步骤是链尾的收口变换，每次重跑链都会重新生成 Prefab。
    /// 手改 Prefab 会被下一次重跑覆盖——这是"生成式资产"的固有代价，与场景折叠前一致
    /// （折叠前手改场景同样会被 <c>M2BattleSceneSetup.BuildAll</c> 的 NewScene 洗掉）。
    /// 要持久化手工调整，改生成器，不改产物。
    ///
    /// 【幂等】已折叠的场景再次折叠 = 先完全解包回裸对象、再重新折叠；结果与一次折叠一致
    /// （不会出现"实例里套实例"）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/折叠为 Prefab/…
    ///   无头折叠: -batchmode -nographics -quit -projectPath &lt;P&gt;
    ///             -executeMethod PirateCrew.EditorTools.ScenePrefabCollapse.FromCommandLine
    ///             -collapseMode collapse -collapseTargets "Battle;MainMenu"
    ///   无头校验（只读，漂移时退出码 1）: 同上，-collapseMode verify（-collapseTargets 省略 = 全部）
    ///
    /// 【顺序】必须排在被折叠场景的**全部**装配步骤之后：折叠把根对象收进 Prefab，
    /// 之后再往场景里写字段就等于写"Prefab 实例覆盖"（覆盖是本工程明确要避免的状态）。
    /// 链内位置见 <see cref="BattleScenePipeline"/>。
    /// </summary>
    public static class ScenePrefabCollapse
    {
        /// <summary>一个"场景 → Prefab"的折叠目标。</summary>
        public sealed class Entry
        {
            /// <summary>命令行 / 日志用的短名。</summary>
            public string Key;

            public string ScenePath;

            public string PrefabPath;

            /// <summary>载体根名：Prefab 资产的根对象名，也是折叠后场景里唯一的根对象名。</summary>
            public string RootName;
        }

        /// <summary>
        /// 全部折叠目标。Battle 与三个 UI 场景都是装配脚本产物（见各自 Setup 脚本），
        /// 一律折叠；Bootstrapper 只有 2 个对象（服务宿主 + 视频设置）且是入口场景，
        /// 折它只增加一层间接，故**有意不折**。
        /// </summary>
        public static readonly Entry[] Targets =
        {
            new Entry
            {
                Key = "Battle",
                ScenePath = "Assets/Scenes/Battle.unity",
                PrefabPath = "Assets/Prefabs/PirateCrew/Battle/BattleRig.prefab",
                RootName = "BattleRig",
            },
            new Entry
            {
                Key = "MainMenu",
                ScenePath = "Assets/Scenes/MainMenu.unity",
                PrefabPath = "Assets/Prefabs/UI/MainMenuScreen.prefab",
                RootName = "MainMenuScreen",
            },
            new Entry
            {
                Key = "LevelSelect",
                ScenePath = "Assets/Scenes/LevelSelect.unity",
                PrefabPath = "Assets/Prefabs/UI/LevelSelectScreen.prefab",
                RootName = "LevelSelectScreen",
            },
            new Entry
            {
                Key = "CrewManagement",
                ScenePath = "Assets/Scenes/CrewManagement.unity",
                PrefabPath = "Assets/Prefabs/UI/CrewManagementScreen.prefab",
                RootName = "CrewManagementScreen",
            },
        };

        // ------------------------------------------------------------------
        // 菜单 / 命令行入口
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/Scenes/折叠为 Prefab/① Battle 战斗场景", false, 40)]
        public static void CollapseBattle()
        {
            RunBatch(new[] { "Battle" }, true);
        }

        [MenuItem("PirateCrew/Scenes/折叠为 Prefab/② 三个 UI 场景", false, 41)]
        public static void CollapseUiScenes()
        {
            RunBatch(new[] { "MainMenu", "LevelSelect", "CrewManagement" }, true);
        }

        [MenuItem("PirateCrew/Scenes/折叠为 Prefab/③ 全部目标", false, 42)]
        public static void CollapseAll()
        {
            RunBatch(AllKeys(), true);
        }

        [MenuItem("PirateCrew/Scenes/折叠为 Prefab/校验折叠态（只读）", false, 43)]
        public static void VerifyAll()
        {
            RunBatch(AllKeys(), false);
        }

        /// <summary>
        /// 无头总入口。
        /// <c>-collapseMode</c>：collapse（默认）| verify；
        /// <c>-collapseTargets</c>：分号分隔的 Key（省略 = 全部）。
        /// </summary>
        public static void FromCommandLine()
        {
            string mode = ArgValue("-collapseMode") ?? "collapse";
            string targets = ArgValue("-collapseTargets");
            string[] keys = string.IsNullOrEmpty(targets) ? AllKeys() : targets.Split(';');
            RunBatch(keys, !string.Equals(mode, "verify", System.StringComparison.OrdinalIgnoreCase));
        }

        internal static string[] AllKeys()
        {
            var keys = new string[Targets.Length];
            for (int i = 0; i < Targets.Length; i++)
                keys[i] = Targets[i].Key;
            return keys;
        }

        /// <summary>按 Key 找目标；找不到返回 null 并记错误。</summary>
        internal static Entry Find(string key)
        {
            for (int i = 0; i < Targets.Length; i++)
                if (Targets[i].Key == key)
                    return Targets[i];

            Debug.LogError("[ScenePrefabCollapse] 未知折叠目标: " + key
                           + "（可用: " + string.Join("、", AllKeys()) + "）");
            return null;
        }

        static void RunBatch(string[] keys, bool collapse)
        {
            var problems = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                Entry entry = Find(keys[i]);
                if (entry == null)
                {
                    problems.Add("未知目标 " + keys[i]);
                    continue;
                }

                List<string> result = collapse ? Collapse(entry) : Verify(entry);
                for (int j = 0; j < result.Count; j++)
                    problems.Add(keys[i] + "：" + result[j]);
            }

            if (problems.Count == 0)
            {
                Debug.Log("[ScenePrefabCollapse] " + (collapse ? "折叠" : "校验") + "通过："
                          + string.Join("、", keys));
                Finish(0);
                return;
            }

            for (int i = 0; i < problems.Count; i++)
                Debug.LogError("[ScenePrefabCollapse] " + problems[i]);
            Finish(1);
        }

        // ------------------------------------------------------------------
        // 折叠
        // ------------------------------------------------------------------

        /// <summary>
        /// 折叠一个场景。返回问题清单（空 = 成功）。
        /// 步骤：开场景 → 解包（若已是折叠态）→ 全部根挂到载体根下 → 存 Prefab →
        /// 销毁载体、实例化 Prefab → 存场景。
        /// </summary>
        public static List<string> Collapse(Entry entry)
        {
            var problems = new List<string>();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.ScenePath) == null)
            {
                problems.Add("场景不存在: " + entry.ScenePath);
                return problems;
            }

            Scene scene = EditorSceneManager.OpenScene(entry.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                problems.Add("打开场景失败: " + entry.ScenePath);
                return problems;
            }

            GameObject[] roots = CollectRoots(scene, entry, problems);
            if (roots.Length == 0)
            {
                problems.Add("场景里没有可折叠的根对象（" + entry.ScenePath + "）");
                return problems;
            }

            var container = new GameObject(entry.RootName);
            for (int i = 0; i < roots.Length; i++)
            {
                // worldPositionStays=true：载体根在原点/单位旋转，故 localPosition 不变，
                // 转储的 localPosition 列在折叠前后应完全一致（等价性判据之一）。
                roots[i].transform.SetParent(container.transform, true);
            }

            EnsureFolder(Path.GetDirectoryName(entry.PrefabPath).Replace('\\', '/'));
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(container, entry.PrefabPath, out bool ok);
            if (!ok || asset == null)
            {
                // 存盘失败时**不销毁载体**——场景内存里仍是完整内容，可人工抢救。
                problems.Add("保存 Prefab 失败: " + entry.PrefabPath + "（场景内容仍在内存未销毁）");
                return problems;
            }

            Object.DestroyImmediate(container);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            if (instance == null)
            {
                problems.Add("实例化 Prefab 失败: " + entry.PrefabPath);
                return problems;
            }

            instance.name = entry.RootName;

            if (!EditorSceneManager.MarkSceneDirty(scene) && scene.isDirty == false)
                EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, entry.ScenePath))
            {
                problems.Add("保存场景失败: " + entry.ScenePath);
                return problems;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ScenePrefabCollapse] " + entry.Key + " 折叠完成："
                      + roots.Length + " 个根 → " + entry.PrefabPath
                      + "；场景只剩一个实例（" + entry.ScenePath + "）");
            return problems;
        }

        /// <summary>
        /// 取"待折叠的根集合"。
        /// 正常情况 = 场景全部根；若场景**已经是折叠态**，先完全解包回裸对象再取，
        /// 保证"折叠 → 再折叠"幂等（见类头）。
        /// </summary>
        static GameObject[] CollectRoots(Scene scene, Entry entry, List<string> problems)
        {
            GameObject[] roots = scene.GetRootGameObjects();

            if (roots.Length == 1 && PrefabUtility.IsPartOfPrefabInstance(roots[0]))
            {
                PrefabUtility.UnpackPrefabInstance(roots[0], PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                roots = scene.GetRootGameObjects();
            }

            // 解包后可能剩下一个同名空载体（只有 Transform）——把它的子对象提为根，避免载体套载体。
            if (roots.Length == 1 && roots[0].name == entry.RootName
                && roots[0].GetComponents<Component>().Length == 1)
            {
                Transform shell = roots[0].transform;
                var promoted = new List<GameObject>();
                while (shell.childCount > 0)
                {
                    Transform child = shell.GetChild(0);
                    child.SetParent(null, true);
                    promoted.Add(child.gameObject);
                }

                Object.DestroyImmediate(shell.gameObject);
                return promoted.ToArray();
            }

            return roots;
        }

        // ------------------------------------------------------------------
        // 校验（只读）
        // ------------------------------------------------------------------

        /// <summary>
        /// 校验一个场景处于"纯折叠态"：恰好一个根、是目标 Prefab 的实例、且**没有任何实例覆盖**。
        /// 返回问题清单（空 = 通过）。只读，不保存。
        /// </summary>
        public static List<string> Verify(Entry entry)
        {
            var problems = new List<string>();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.ScenePath) == null)
            {
                problems.Add("场景不存在: " + entry.ScenePath);
                return problems;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
            if (prefab == null)
            {
                problems.Add("Prefab 不存在: " + entry.PrefabPath);
                return problems;
            }
            if (prefab.name != entry.RootName)
                problems.Add("Prefab 根名应为 " + entry.RootName + "，实为 " + prefab.name);

            Scene scene = EditorSceneManager.OpenScene(entry.ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();

            if (roots.Length != 1)
            {
                problems.Add("场景根对象数应为 1（载体根 " + entry.RootName + "），实为 " + roots.Length
                             + "：折叠态被破坏（有人往场景里手摆对象，或装配链尾没跑折叠步骤）。");
                return problems;
            }

            GameObject root = roots[0];
            if (root.name != entry.RootName)
                problems.Add("场景唯一根名应为 " + entry.RootName + "，实为 " + root.name);

            if (!PrefabUtility.IsPartOfPrefabInstance(root))
            {
                problems.Add("场景唯一根不是 Prefab 实例（折叠态被解包了）。");
                return problems;
            }

            Object source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            string sourcePath = source == null ? null : AssetDatabase.GetAssetPath(source);
            if (sourcePath != entry.PrefabPath)
                problems.Add("场景实例的源 Prefab 应为 " + entry.PrefabPath + "，实为 "
                             + (sourcePath ?? "<null>"));

            if (PrefabUtility.HasPrefabInstanceAnyOverrides(root, false))
            {
                problems.Add("场景实例带有 Prefab 覆盖——折叠态应是纯实例（覆盖会散落接线知识，"
                             + "是本工程明确要避免的状态）。请把改动落到生成器 "
                             + "或 Prefab 资产上，再重跑装配链。");
            }

            return problems;
        }

        // ------------------------------------------------------------------
        // 辅助
        // ------------------------------------------------------------------

        static string ArgValue(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag)
                    return args[i + 1];
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void Finish(int exitCode)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }
}
