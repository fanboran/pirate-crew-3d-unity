using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 调色板镜像的生成 / 回写 / 对账三个入口（等距像素卡通·美术工业化轨道 D1）。
    ///
    /// 【一致性路径（本工具选定的唯一一条，别自己发明第二条）】
    /// <code>
    ///   Assets/Data/Palette/pirate_palette.json     ← 唯一真源（手改这里，或走 ExportToJson 提升）
    ///              │  BuildFromJson（本类）
    ///              ▼
    ///   Assets/Data/Palette/PiratePalette.asset     ← Unity 侧派生镜像（Inspector 可读，勿当基准）
    /// </code>
    /// - 真源是 **JSON**：Python 量化工具与 Blender 导出模板都只读它，不读 .asset —— 离线链
    ///   因此完全不需要 Unity（这是能守住一致性的关键：把跨语言共享的那一份数据放在纯文本上）。
    /// - `.asset` 是镜像：由 <c>BuildFromJson</c> 生成，供 Unity 侧工具（材质工厂 / UI 派生）取色。
    ///   若有人在 Inspector 里改了色值想保留，先跑 <c>ExportToJson</c> 把它提升回 JSON，再重跑
    ///   <c>BuildFromJson</c>；两条命令互为逆运算，重复执行收敛到同一字节（幂等）。
    /// - 漂移由两侧各自的对账命令发现：Unity 侧 <c>Verify</c>、离线侧
    ///   <c>python tools/palette/palette_tool.py verify</c>。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Art/调色板/*
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.Art.PaletteAssetBuilder.BuildFromJson
    /// </summary>
    public static class PaletteAssetBuilder
    {
        /// <summary>板的 JSON 真源路径（工程内相对路径）。</summary>
        public const string JsonPath = "Assets/Data/Palette/pirate_palette.json";

        /// <summary>Unity 侧镜像资产路径。</summary>
        public const string AssetPath = "Assets/Data/Palette/PiratePalette.asset";

        [MenuItem("PirateCrew/Art/调色板/JSON → PaletteAsset（生成镜像）", priority = 100)]
        public static void BuildFromJson()
        {
            PaletteJson.Root root = ReadJson();
            if (root == null)
                return;

            var errors = new List<string>();
            var warnings = new List<string>();
            PaletteJson.Validate(root, errors, warnings);
            for (int i = 0; i < warnings.Count; i++)
                Debug.LogWarning("[PaletteAssetBuilder] " + warnings[i]);
            if (errors.Count > 0)
            {
                for (int i = 0; i < errors.Count; i++)
                    Debug.LogError("[PaletteAssetBuilder] " + errors[i]);
                Debug.LogError("[PaletteAssetBuilder] 板结构校验未过，拒绝生成镜像（先修 JSON）。");
                return;
            }

            EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
            PaletteAsset asset = AssetDatabase.LoadAssetAtPath<PaletteAsset>(AssetPath);
            bool created = asset == null;
            if (created)
                asset = ScriptableObject.CreateInstance<PaletteAsset>();

            asset.paletteName = root.name;
            asset.version = root.version;
            asset.texelDensityPxPerMeter = root.texelDensityPxPerMeter;
            asset.note = root.note;
            asset.slots = root.slots;

            if (created)
                AssetDatabase.CreateAsset(asset, AssetPath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PaletteAssetBuilder] 镜像" + (created ? "新建" : "就地覆写") + "：" + AssetPath
                + "（" + root.slots.Count + " 色 / v" + root.version
                + " / 纹素密度 " + root.texelDensityPxPerMeter + "px·m⁻¹）。真源 = " + JsonPath);
        }

        [MenuItem("PirateCrew/Art/调色板/PaletteAsset → JSON（回写真源）", priority = 101)]
        public static void ExportToJson()
        {
            PaletteAsset asset = AssetDatabase.LoadAssetAtPath<PaletteAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("[PaletteAssetBuilder] 找不到镜像资产 " + AssetPath + "，先跑 JSON → PaletteAsset。");
                return;
            }

            string text = PaletteJson.ToCanonicalText(asset);
            string abs = AbsPath(JsonPath);
            string before = File.Exists(abs) ? ReadTextLf(abs) : null;
            if (before == text)
            {
                Debug.Log("[PaletteAssetBuilder] 真源已是镜像的规范文本，无变化：" + JsonPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllText(abs, text, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(JsonPath);
            Debug.Log("[PaletteAssetBuilder] 已回写真源：" + JsonPath
                + "（" + (before == null ? "新建" : "覆盖") + "，规范文本 " + text.Length + " 字节）。");
        }

        [MenuItem("PirateCrew/Art/调色板/对账 JSON 与 PaletteAsset", priority = 102)]
        public static void Verify()
        {
            PaletteAsset asset = AssetDatabase.LoadAssetAtPath<PaletteAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("[PaletteAssetBuilder] 对账失败：镜像资产不存在（" + AssetPath + "）。");
                return;
            }

            string fromAsset = PaletteJson.ToCanonicalText(asset);
            string abs = AbsPath(JsonPath);
            if (!File.Exists(abs))
            {
                Debug.LogError("[PaletteAssetBuilder] 对账失败：真源不存在（" + JsonPath + "）。");
                return;
            }
            string fromFile = ReadTextLf(abs);
            bool same = fromAsset == fromFile;

            int assetColors = asset.slots == null ? 0 : asset.slots.Count;
            Debug.Log("[PaletteAssetBuilder] 对账：" + (same ? "一致" : "**不一致**")
                + "（镜像 " + assetColors + " 色 vs 真源文本 " + fromFile.Length + " 字节；"
                + "镜像规范文本 " + fromAsset.Length + " 字节）"
                + (same ? "" : "\n  处理：若 Inspector 的改动是要保留的 → 跑 ExportToJson；否则跑 BuildFromJson。"));

            if (!same)
            {
                int limit = Mathf.Min(400, Mathf.Min(fromAsset.Length, fromFile.Length));
                for (int i = 0; i < limit; i++)
                {
                    if (fromAsset[i] != fromFile[i])
                    {
                        Debug.LogWarning("[PaletteAssetBuilder] 首个差异在第 " + i + " 字节附近：\n  镜像="
                            + Excerpt(fromAsset, i) + "\n  真源=" + Excerpt(fromFile, i));
                        break;
                    }
                }
            }

            if (asset.texelDensityPxPerMeter <= 0)
                Debug.LogWarning("[PaletteAssetBuilder] 纹素密度非正：" + asset.texelDensityPxPerMeter);
        }

        static string Excerpt(string text, int index)
        {
            int start = Mathf.Max(0, index - 40);
            int length = Mathf.Min(80, text.Length - start);
            return text.Substring(start, length).Replace("\n", "⏎");
        }

        /// <summary>
        /// 读文本并归一行尾到 LF。
        ///
        /// 【为什么要这一层】本仓 <c>core.autocrlf=true</c>，而 <c>.gitattributes</c> 的 <c>eol=lf</c>
        /// 白名单只覆盖 Unity 序列化扩展名（.meta/.unity/.prefab/.asset/.mat/.controller）——
        /// **.json 不在其中**，别的机器或新克隆签出这份板时可能拿到 CRLF。规范文本的定义是 LF，
        /// 但"读到的 CRLF"不该被报成"镜像与真源不一致"（那是 git 的行尾策略，不是内容差异）。
        /// 写出侧（<see cref="ExportToJson"/>）永远写 LF。
        /// 【根治】给 <c>.gitattributes</c> 补一行 <c>*.json text eol=lf</c> 可让这层容错变成冗余；
        /// 该文件属仓库根共享基建，改动登记在 docs/技术/资产管线/调色板与量化手册.md 的遗留区。
        /// </summary>
        static string ReadTextLf(string path)
        {
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        static PaletteJson.Root ReadJson()
        {
            string abs = AbsPath(JsonPath);
            if (!File.Exists(abs))
            {
                Debug.LogError("[PaletteAssetBuilder] 找不到板的真源 JSON：" + abs
                    + "（板与量化链的手册见 docs/技术/资产管线/调色板与量化手册.md）");
                return null;
            }
            try
            {
                return PaletteJson.Parse(File.ReadAllText(abs));
            }
            catch (System.Exception exc)
            {
                Debug.LogError("[PaletteAssetBuilder] " + exc.Message);
                return null;
            }
        }

        /// <summary>工程内相对路径 → 绝对路径（File IO 必须绝对路径：相对路径按进程 CWD 解析，batchmode 下踩过）。</summary>
        static string AbsPath(string projectRelative)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelative));
        }

        static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
