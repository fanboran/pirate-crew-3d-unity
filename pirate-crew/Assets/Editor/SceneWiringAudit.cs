using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 场景接线转储器：把某个场景的**完整层级 + 组件 + 序列化引用**导出成文本/TSV，
    /// 供人工审计与「装配脚本重跑前后是否等价」的机械比对。
    ///
    /// 【为什么需要它】Battle.unity 是 Editor 装配脚本的生成产物（3.4 万行），
    /// 接线知识活在脚本里而不在 Prefab 连线上（架构审计 P0-2）。要把它拆成
    /// 「稳定结构 Prefab 化 + 生成式内容」就必须先有**可核对的现状快照**，
    /// 之后每次改装配脚本都要能证明"行为等价"，而不是靠肉眼看场景。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Audit/场景接线转储（Battle）
    ///   无头: -batchmode -nographics -quit -projectPath &lt;P&gt; \
    ///         -executeMethod PirateCrew.EditorTools.SceneWiringAudit.DumpBattle \
    ///         [-sceneAuditOut &lt;绝对目录&gt;] [-sceneAuditScene Assets/Scenes/xxx.unity]
    ///   比对: 菜单 PirateCrew/Audit/比对两次转储（选两份 tsv）；或 -executeMethod
    ///         SceneWiringAudit.DiffFromCommandLine -sceneAuditDiff "a.tsv;b.tsv"
    ///
    /// 【产物】&lt;out&gt;/hierarchy-&lt;场景名&gt;.txt（人读树）、hierarchy-&lt;场景名&gt;.tsv（机读）、
    /// summary-&lt;场景名&gt;.txt（统计：对象数/深度/组件直方图/空引用直方图/跨根引用数）。
    ///
    /// 【只读保证】本工具只打开场景读取，**从不保存**场景或资产。
    /// </summary>
    public static class SceneWiringAudit
    {
        const string DefaultScene = "Assets/Scenes/Battle.unity";

        /// <summary>默认输出目录（仓库根的 external/ 下，该目录不入库）。</summary>
        static string DefaultOutDir
        {
            get
            {
                string project = Directory.GetParent(Application.dataPath).FullName;   // .../pirate-crew
                string repo = Directory.GetParent(project).FullName;                    // 仓库根
                return Path.Combine(repo, "external", "scene-audit");
            }
        }

        [MenuItem("PirateCrew/Audit/场景接线转储（Battle）")]
        public static void DumpBattle()
        {
            Dump(DefaultScene, DefaultOutDir);
        }

        [MenuItem("PirateCrew/Audit/场景接线转储（当前打开场景）")]
        public static void DumpActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[SceneWiringAudit] 当前没有已保存的场景可转储。");
                return;
            }
            Dump(scene.path, DefaultOutDir);
        }

        /// <summary>无头入口：读命令行可选参数后转储。</summary>
        public static void DumpBattleFromCommandLine()
        {
            string scene = ArgValue("-sceneAuditScene") ?? DefaultScene;
            string outDir = ArgValue("-sceneAuditOut") ?? DefaultOutDir;
            Dump(scene, outDir);
        }

        // ------------------------------------------------------------------
        // 转储
        // ------------------------------------------------------------------

        public static void Dump(string scenePath, string outDir)
        {
            if (!File.Exists(scenePath))
            {
                Debug.LogError("[SceneWiringAudit] 场景不存在: " + scenePath);
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(outDir);

            var rows = new List<NodeRow>();
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots)
                Walk(root, null, 0, rows);

            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string txtPath = Path.Combine(outDir, "hierarchy-" + sceneName + ".txt");
            string tsvPath = Path.Combine(outDir, "hierarchy-" + sceneName + ".tsv");
            string sumPath = Path.Combine(outDir, "summary-" + sceneName + ".txt");

            WriteTree(txtPath, rows);
            WriteTsv(tsvPath, rows);
            WriteSummary(sumPath, sceneName, scenePath, roots.Length, rows);

            Debug.Log("[SceneWiringAudit] 转储完成：" + rows.Count + " 个对象 → " + tsvPath);
        }

        sealed class NodeRow
        {
            public int Depth;
            public string Path;
            public bool ActiveSelf;
            public bool ActiveInHierarchy;
            public int Layer;
            public string Tag;
            public string PrefabStatus;      // None / Instance / Missing
            public string PrefabSource;      // 源 Prefab 资产路径（实例才有）
            public string Components;        // 逗号分隔的类型名（缺失脚本记为 !MissingScript）
            public Vector3 LocalPosition;
            public List<string> Refs = new List<string>();   // "组件.字段 = 目标路径" 或 "= <null>"
            public List<string> Disabled = new List<string>();
        }

        static void Walk(GameObject go, string parentPath, int depth, List<NodeRow> rows)
        {
            string path = parentPath == null ? go.name : parentPath + "/" + go.name;
            var row = new NodeRow
            {
                Depth = depth,
                Path = path,
                ActiveSelf = go.activeSelf,
                ActiveInHierarchy = go.activeInHierarchy,
                Layer = go.layer,
                Tag = go.tag,
                LocalPosition = go.transform.localPosition,
            };

            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(go);
            row.PrefabStatus = status.ToString();
            if (status != PrefabInstanceStatus.NotAPrefab)
            {
                UnityEngine.Object src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                row.PrefabSource = src == null ? "?" : AssetDatabase.GetAssetPath(src);
            }

            var names = new List<string>();
            Component[] comps = go.GetComponents<Component>();
            foreach (Component c in comps)
            {
                if (c == null)
                {
                    names.Add("!MissingScript");
                    continue;
                }

                names.Add(c.GetType().Name);

                var behaviour = c as Behaviour;
                if (behaviour != null && !behaviour.enabled)
                    row.Disabled.Add(c.GetType().Name);

                CollectRefs(c, row);
            }
            row.Components = string.Join(",", names.ToArray());
            rows.Add(row);

            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i).gameObject, path, depth + 1, rows);
        }

        /// <summary>收集一个组件上所有 ObjectReference 序列化字段的指向（含数组元素）。</summary>
        static void CollectRefs(Component c, NodeRow row)
        {
            if (c is Transform)   // m_Children / m_Father 是层级自身，属噪音
                return;

            SerializedObject so;
            try
            {
                so = new SerializedObject(c);
            }
            catch (Exception)
            {
                return;   // 个别组件（如内置不可序列化类型）取不到，跳过
            }

            SerializedProperty it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                if (it.propertyPath == "m_Script")
                    continue;

                UnityEngine.Object target = it.objectReferenceValue;
                string label = c.GetType().Name + "." + it.propertyPath;
                if (target == null)
                {
                    row.Refs.Add(label + " = <null>");
                    continue;
                }

                string targetPath = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(targetPath))
                    targetPath = DescribeSceneObject(target);
                row.Refs.Add(label + " = " + targetPath);
            }
        }

        static string DescribeSceneObject(UnityEngine.Object obj)
        {
            var comp = obj as Component;
            if (comp != null)
                return "<场景内> " + HierarchyPath(comp.transform);
            var go = obj as GameObject;
            if (go != null)
                return "<场景内> " + HierarchyPath(go.transform);
            return "<内存对象> " + obj.name + " (" + obj.GetType().Name + ")";
        }

        static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 输出
        // ------------------------------------------------------------------

        static void WriteTree(string path, List<NodeRow> rows)
        {
            var sb = new StringBuilder();
            foreach (NodeRow r in rows)
            {
                sb.Append(new string(' ', r.Depth * 2));
                sb.Append(r.Path.Substring(r.Path.LastIndexOf('/') + 1));
                sb.Append("  [").Append(r.Components).Append(']');
                if (!r.ActiveSelf)
                    sb.Append("  (inactive)");
                if (r.PrefabStatus == "Instance")
                    sb.Append("  {prefab: ").Append(r.PrefabSource).Append('}');
                else if (r.PrefabStatus != "NotAPrefab")
                    sb.Append("  {prefab: ").Append(r.PrefabStatus).Append('}');
                if (r.Disabled.Count > 0)
                    sb.Append("  (disabled: ").Append(string.Join(",", r.Disabled.ToArray())).Append(')');
                sb.AppendLine();

                if (r.Refs.Count > 0)
                {
                    string indent = new string(' ', r.Depth * 2 + 2);
                    foreach (string s in r.Refs)
                        sb.Append(indent).Append("- ").AppendLine(s);
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static void WriteTsv(string path, List<NodeRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("path\tdepth\tactiveSelf\tactiveInHierarchy\tlayer\ttag\tprefabStatus\tprefabSource\t"
                + "components\tdisabled\tlocalPosition\trefs");
            foreach (NodeRow r in rows)
            {
                sb.Append(Cell(r.Path)).Append('\t')
                  .Append(r.Depth).Append('\t')
                  .Append(r.ActiveSelf ? "1" : "0").Append('\t')
                  .Append(r.ActiveInHierarchy ? "1" : "0").Append('\t')
                  .Append(r.Layer).Append('\t')
                  .Append(Cell(r.Tag)).Append('\t')
                  .Append(Cell(r.PrefabStatus)).Append('\t')
                  .Append(Cell(r.PrefabSource)).Append('\t')
                  .Append(Cell(r.Components)).Append('\t')
                  .Append(Cell(string.Join(",", r.Disabled.ToArray()))).Append('\t')
                  .Append(Cell(r.LocalPosition.ToString("F3"))).Append('\t')
                  .Append(Cell(string.Join("; ", r.Refs.ToArray())))
                  .AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static string Cell(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }

        static void WriteSummary(string path, string sceneName, string scenePath, int rootCount, List<NodeRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 场景接线转储摘要");
            sb.AppendLine("场景: " + scenePath);
            sb.AppendLine("对象总数: " + rows.Count);
            sb.AppendLine("根对象数: " + rootCount);

            int maxDepth = 0, inactive = 0, prefabInstances = 0, missingScripts = 0, crossRootRefs = 0;
            int totalRefs = 0, nullRefs = 0;
            var compHist = new SortedDictionary<string, int>();
            var rootHist = new SortedDictionary<string, int>();
            var nullHist = new SortedDictionary<string, int>();
            var refTargets = new SortedDictionary<string, int>();

            foreach (NodeRow r in rows)
            {
                if (r.Depth > maxDepth)
                    maxDepth = r.Depth;
                if (!r.ActiveSelf)
                    inactive++;
                if (r.PrefabStatus == "Instance")
                    prefabInstances++;
                if (r.Components.Contains("!MissingScript"))
                    missingScripts++;

                foreach (string c in r.Components.Split(','))
                {
                    if (c.Length == 0)
                        continue;
                    Bump(compHist, c);
                }

                string rootName = r.Path.Contains("/") ? r.Path.Substring(0, r.Path.IndexOf('/')) : r.Path;
                string rootKey = NormalizeRootName(rootName);
                Bump(rootHist, rootKey);

                foreach (string s in r.Refs)
                {
                    totalRefs++;
                    int eq = s.IndexOf(" = ", StringComparison.Ordinal);
                    string label = eq < 0 ? s : s.Substring(0, eq);
                    string target = eq < 0 ? "" : s.Substring(eq + 3);
                    if (target == "<null>")
                    {
                        nullRefs++;
                        Bump(nullHist, label);
                        continue;
                    }
                    if (target.StartsWith("<场景内> ", StringComparison.Ordinal))
                    {
                        string tp = target.Substring("<场景内> ".Length);
                        string tRoot = tp.Contains("/") ? tp.Substring(0, tp.IndexOf('/')) : tp;
                        Bump(refTargets, NormalizeRootName(tRoot));
                        if (NormalizeRootName(tRoot) != rootKey)
                            crossRootRefs++;
                    }
                }
            }

            sb.AppendLine("最大层级深度: " + maxDepth);
            sb.AppendLine("未激活(inactiveSelf=0)对象: " + inactive);
            sb.AppendLine("Prefab 实例对象: " + prefabInstances);
            sb.AppendLine("含缺失脚本的对象: " + missingScripts);
            sb.AppendLine("序列化引用总数: " + totalRefs + "（空引用 " + nullRefs + "，跨根引用 " + crossRootRefs + "）");
            sb.AppendLine();

            sb.AppendLine("## 组件直方图");
            foreach (var kv in compHist)
                sb.AppendLine(kv.Value.ToString().PadLeft(6) + "  " + kv.Key);
            sb.AppendLine();

            sb.AppendLine("## 根对象（按名称归一后）");
            foreach (var kv in rootHist)
                sb.AppendLine(kv.Value.ToString().PadLeft(6) + "  " + kv.Key);
            sb.AppendLine();

            sb.AppendLine("## 空引用最多的字段（前 40）");
            AppendTop(sb, nullHist, 40);
            sb.AppendLine();

            sb.AppendLine("## 被引用的根（引用来自哪个根 → 指向哪个根，跨根即耦合）");
            foreach (var kv in refTargets)
                sb.AppendLine(kv.Value.ToString().PadLeft(6) + "  " + kv.Key);

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>把 "IslandTile_12" 这类带编号的名字归一成 "IslandTile_#"，避免直方图被实例淹没。</summary>
        static string NormalizeRootName(string name)
        {
            var sb = new StringBuilder(name.Length);
            bool lastDigit = false;
            foreach (char ch in name)
            {
                bool digit = ch >= '0' && ch <= '9';
                if (digit)
                {
                    if (!lastDigit)
                        sb.Append('#');
                }
                else
                {
                    sb.Append(ch);
                }
                lastDigit = digit;
            }
            return sb.ToString();
        }

        static void Bump(SortedDictionary<string, int> d, string key)
        {
            int v;
            d.TryGetValue(key, out v);
            d[key] = v + 1;
        }

        static void AppendTop(StringBuilder sb, SortedDictionary<string, int> d, int n)
        {
            var list = new List<KeyValuePair<string, int>>(d);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < list.Count && i < n; i++)
                sb.AppendLine(list[i].Value.ToString().PadLeft(6) + "  " + list[i].Key);
        }

        // ------------------------------------------------------------------
        // 两次转储的比对（阶段 3：装配脚本重跑前后等价性）
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/Audit/比对两次转储（tsv）")]
        public static void DiffFromMenu()
        {
            string a = EditorUtility.OpenFilePanel("选旧转储 tsv", DefaultOutDir, "tsv");
            if (string.IsNullOrEmpty(a))
                return;
            string b = EditorUtility.OpenFilePanel("选新转储 tsv", DefaultOutDir, "tsv");
            if (string.IsNullOrEmpty(b))
                return;
            Diff(a, b);
        }

        public static void DiffFromCommandLine()
        {
            string spec = ArgValue("-sceneAuditDiff");
            if (string.IsNullOrEmpty(spec))
            {
                Debug.LogError("[SceneWiringAudit] 需要 -sceneAuditDiff \"a.tsv;b.tsv\"");
                return;
            }
            string[] parts = spec.Split(';');
            if (parts.Length != 2)
            {
                Debug.LogError("[SceneWiringAudit] -sceneAuditDiff 需要恰好两个路径（分号分隔）");
                return;
            }
            Diff(parts[0], parts[1]);
        }

        public static void Diff(string oldTsv, string newTsv)
        {
            Dictionary<string, string> a = ReadTsv(oldTsv);
            Dictionary<string, string> b = ReadTsv(newTsv);

            var added = new List<string>();
            var removed = new List<string>();
            var changed = new List<string>();

            foreach (var kv in b)
                if (!a.ContainsKey(kv.Key))
                    added.Add(kv.Key);
            foreach (var kv in a)
            {
                if (!b.ContainsKey(kv.Key))
                {
                    removed.Add(kv.Key);
                    continue;
                }
                if (a[kv.Key] != b[kv.Key])
                    changed.Add(kv.Key);
            }

            added.Sort();
            removed.Sort();
            changed.Sort();

            var sb = new StringBuilder();
            sb.AppendLine("# 转储比对");
            sb.AppendLine("旧: " + oldTsv + "（" + a.Count + " 对象）");
            sb.AppendLine("新: " + newTsv + "（" + b.Count + " 对象）");
            sb.AppendLine("新增 " + added.Count + " / 删除 " + removed.Count + " / 变化 " + changed.Count);
            sb.AppendLine();
            AppendList(sb, "## 新增对象", added);
            AppendList(sb, "## 删除对象", removed);
            AppendList(sb, "## 变化对象", changed);

            string outPath = Path.Combine(Path.GetDirectoryName(newTsv) ?? ".", "diff.txt");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[SceneWiringAudit] 比对完成：新增 " + added.Count + " / 删除 " + removed.Count
                + " / 变化 " + changed.Count + " → " + outPath);
        }

        static void AppendList(StringBuilder sb, string title, List<string> items)
        {
            sb.AppendLine(title);
            foreach (string s in items)
                sb.AppendLine("  " + s);
            sb.AppendLine();
        }

        static Dictionary<string, string> ReadTsv(string path)
        {
            var map = new Dictionary<string, string>();
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)   // 跳过表头
            {
                string line = lines[i];
                if (line.Length == 0)
                    continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0)
                    continue;
                map[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
            return map;
        }

        static string ArgValue(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag)
                    return args[i + 1];
            return null;
        }
    }
}
