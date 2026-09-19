using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 场景资产**总清单生成器**（糖豆人式资产架构，任务书阶段 C「命名统一」）：
    /// 扫描 <c>Assets/Art/Models/</c> 全树（SceneKit 烘焙件 + WorldKit 八图件），
    /// 按「程序化 = 配方 + 种子 / 手作 = Blender 脚本路径」登记每件资产的上游，
    /// 落一份 <c>scene-assets.json</c>——一份 manifest 覆盖全部场景资产。
    ///
    /// 【为什么需要】烘焙 prefab 与 Blender FBX 从此同目录共存，"这件东西哪来的、
    /// 改哪个源头能再生"必须有一个可核对的登记处，否则改配方不知道要重烘、改脚本
    /// 不知道产物在哪。上游表是**本文件内的登记表**（代码即真源）；manifest 本体是
    /// 生成产物——手工编辑会被下次生成覆盖。
    ///
    /// 【幂等】输出按路径排序、无时间戳——同输入必得逐字节一致的 JSON。
    ///
    /// 【格式】【提案/待定】manifestVersion 1：assets[] 条目 = path / kind / origin / source。
    ///
    /// 【入口】菜单 PirateCrew/烘焙/场景资产总清单；无头 -executeMethod
    /// PirateCrew.EditorTools.SceneAssetManifestBuilder.BuildAll。ArtGate ⑦.7 在烘焙后调用。
    /// </summary>
    public static class SceneAssetManifestBuilder
    {
        const string ModelsRoot = "Assets/Art/Models";
        const string ManifestPath = ModelsRoot + "/scene-assets.json";

        /// <summary>一条上游登记：该目录/文件下的场景资产从哪来（kind 分流）。</summary>
        class UpstreamEntry
        {
            public string PathInModels;   // 相对 Assets/Art/Models 的目录或文件
            public string Origin;         // programmatic（C# 烘焙器）| handmade（Blender）
            public string Source;         // 配方/种子或 Blender 脚本路径
        }

        /// <summary>
        /// 上游登记表（**代码即真源**）。前缀匹配最长者优先；新增资产目录先来这里登记，
        /// 未登记的条目按 unknown 落盘并在日志告警。
        /// </summary>
        static readonly UpstreamEntry[] Upstreams =
        {
            // ---- SceneKit：C# 烘焙件（程序化，输入 = 固定输出）----
            new UpstreamEntry { PathInModels = "SceneKit/CloudField.prefab", Origin = "programmatic",
                Source = "SceneArtBaker.BakeCloudField: CloudFieldSpec.Default, Seed=26091401" },
            new UpstreamEntry { PathInModels = "SceneKit/Islets_L02.prefab", Origin = "programmatic",
                Source = "SceneArtBaker.BakeIslets: ShowcaseLevels.BuildLogicGrid(2) + IslandShellGeometry.BuildSolidShell" },
            new UpstreamEntry { PathInModels = "SceneKit/ShowcaseDangerBorder.prefab", Origin = "programmatic",
                Source = "SceneArtBaker.BakeDangerBorder: IslandShellGeometry.AddDashedBorder 20x15" },
            new UpstreamEntry { PathInModels = "SceneKit/Baked", Origin = "programmatic",
                Source = "SceneArtBaker 烘焙网格资产（被同目录 prefab 引用，不单独摆放）" },

            // ---- SceneKit：Blender 手作主件（§19 样板）----
            new UpstreamEntry { PathInModels = "SceneKit/Flagship.fbx", Origin = "handmade",
                Source = "tools/blender/scene/build_scene_kit.py（Blender 无头）" },
            new UpstreamEntry { PathInModels = "SceneKit/Dock.fbx", Origin = "handmade",
                Source = "tools/blender/scene/build_scene_kit.py（Blender 无头）" },

            // ---- WorldKit：四组 Blender 无头批量建模（M4，风格参数 style_tokens.py）----
            new UpstreamEntry { PathInModels = "WorldKit/Archipelago", Origin = "handmade",
                Source = "tools/blender/scene/archipelago/（Blender 无头批量，style_tokens.py 统一风格）" },
            new UpstreamEntry { PathInModels = "WorldKit/Props", Origin = "handmade",
                Source = "tools/blender/scene/props/（Blender 无头批量，style_tokens.py 统一风格）" },
            new UpstreamEntry { PathInModels = "WorldKit/Marine", Origin = "handmade",
                Source = "tools/blender/scene/marine/（Blender 无头批量，style_tokens.py 统一风格）" },
            new UpstreamEntry { PathInModels = "WorldKit/Horizon", Origin = "handmade",
                Source = "tools/blender/scene/horizon/（Blender 无头批量，style_tokens.py 统一风格）" },
        };

        /// <summary>standable 站面 manifest（GENERATED 文件，独立于 FBX 本体登记）。</summary>
        const string StandableSource = "tools/blender/scene/sync_standables.py（GENERATED，勿手改）";

        [MenuItem("PirateCrew/烘焙/场景资产总清单")]
        public static void BuildAll()
        {
            var assets = new List<string>();
            Collect(ModelsRoot, assets);
            assets.Sort();

            var json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"manifestVersion\": 1,\n");
            json.Append("  \"note\": \"场景资产总清单（生成器 SceneAssetManifestBuilder.BuildAll；手工编辑会被覆盖）。"
                + "格式为提案/待定。\",\n");
            json.AppendFormat("  \"assetCount\": {0},\n", assets.Count);
            json.Append("  \"assets\": [\n");

            int unknown = 0;
            for (int i = 0; i < assets.Count; i++)
            {
                string path = assets[i];
                string relative = path.Substring(ModelsRoot.Length + 1).Replace('\\', '/');
                string origin, source;
                ResolveUpstream(relative, out origin, out source);
                if (origin == "unknown")
                {
                    unknown++;
                    Debug.LogWarning("[SceneAssetManifestBuilder] 未登记上游的场景资产: " + relative
                        + "——请把它的来源补进 Upstreams 登记表。");
                }

                json.AppendFormat("    {{ \"path\": \"{0}\", \"kind\": \"{1}\", \"origin\": \"{2}\", \"source\": \"{3}\" }}{4}\n",
                    Escape(relative), KindOf(relative), origin, Escape(source),
                    i < assets.Count - 1 ? "," : "");
            }

            json.Append("  ]\n}\n");

            File.WriteAllText(ManifestPath, json.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ManifestPath);

            Debug.Log("[SceneAssetManifestBuilder] 场景资产总清单已生成: " + ManifestPath
                + "（" + assets.Count + " 件，未登记 " + unknown + " 件）。");
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        /// <summary>递归收集场景资产（fbx / prefab / Baked 网格 .asset / standable.json；跳过 .meta）。</summary>
        static void Collect(string folder, List<string> assets)
        {
            foreach (string file in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".meta", System.StringComparison.Ordinal))
                    continue;
                bool relevant = name.EndsWith(".fbx", System.StringComparison.Ordinal)
                    || name.EndsWith(".prefab", System.StringComparison.Ordinal)
                    || name.EndsWith(".standable.json", System.StringComparison.Ordinal)
                    || (name.EndsWith(".asset", System.StringComparison.Ordinal)
                        && file.Replace('\\', '/').Contains(ModelsRoot + "/SceneKit/Baked"));
                if (relevant)
                    assets.Add(file.Replace('\\', '/'));
            }

            foreach (string sub in Directory.GetDirectories(folder))
                Collect(sub.Replace('\\', '/'), assets);
        }

        /// <summary>最长前缀匹配上游登记表；standable.json 优先走 GENERATED 上游。</summary>
        static void ResolveUpstream(string relative, out string origin, out string source)
        {
            if (relative.EndsWith(".standable.json", System.StringComparison.Ordinal))
            {
                origin = "generated";
                source = StandableSource;
                return;
            }

            UpstreamEntry best = null;
            for (int i = 0; i < Upstreams.Length; i++)
            {
                string prefix = Upstreams[i].PathInModels;
                bool match = relative == prefix
                    || relative.StartsWith(prefix + "/", System.StringComparison.Ordinal);
                if (match && (best == null || prefix.Length > best.PathInModels.Length))
                    best = Upstreams[i];
            }

            if (best == null)
            {
                origin = "unknown";
                source = "未登记";
                return;
            }

            origin = best.Origin;
            source = best.Source;
        }

        static string KindOf(string relative)
        {
            if (relative.EndsWith(".prefab", System.StringComparison.Ordinal))
                return "prefab";
            if (relative.EndsWith(".fbx", System.StringComparison.Ordinal))
                return "fbx";
            if (relative.EndsWith(".standable.json", System.StringComparison.Ordinal))
                return "standable";
            return "mesh";
        }

        static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
