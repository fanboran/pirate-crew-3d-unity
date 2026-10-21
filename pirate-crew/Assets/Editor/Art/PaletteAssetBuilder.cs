using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 调色板镜像的生成 / 回写 / 对账三个入口（等距像素卡通·美术工业化轨道 D1）。
    ///
    /// 【一致性路径（本工具选定的唯一一条，别自己发明第二条）】真源是**分域两层板**：
    /// <code>
    ///   Assets/Data/Palette/pirate_palette.json     ← 共享核心板（跨关共用槽 + mergeOrder 合并序）
    ///   Assets/Data/Palette/palette_&lt;域&gt;.json      ← 域板（每关/每生物群系的地形风格档）
    ///              │  PaletteBoard.LoadMerged（共享核心 + 当前域 → 合并视图）
    ///              ▼
    ///   Assets/Data/Palette/PiratePalette.asset     ← Unity 侧派生镜像（Inspector 可读，勿当基准）
    /// </code>
    /// - 镜像的内容是**合并视图**：消费方只看这一份，不感知真源拆分；现役加勒比域的
    ///   合并视图与拆分前的单板逐位一致（零视觉漂移）。当前域见 <see cref="PaletteBoard"/>，
    ///   菜单「选择当前域…」可切，切换后重跑 BuildFromJson 重建镜像。
    /// - 真源是 **JSON**：Python 量化工具与 Blender 导出模板都只读它们，不读 .asset —— 离线链
    ///   因此完全不需要 Unity（这是能守住一致性的关键：把跨语言共享的那一份数据放在纯文本上）。
    /// - `.asset` 是镜像：由 <c>BuildFromJson</c> 生成，供 Unity 侧工具（材质工厂 / UI 派生）取色。
    ///   若有人在 Inspector 里改了色值想保留，先跑 <c>ExportToJson</c> 按槽位归属把改动
    ///   提升回所属层文件（核心板或当前域板），再重跑 <c>BuildFromJson</c>；两条命令互为逆运算，
    ///   重复执行收敛到同一字节（幂等）。
    /// - 漂移由两侧各自的对账命令发现：Unity 侧 <c>Verify</c>（镜像 vs 分层合并视图）、
    ///   离线侧 <c>python tools/palette/palette_tool.py verify</c>（逐层规范文本 + 合并视图行为）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Art/调色板/*
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.Art.PaletteAssetBuilder.BuildFromJson
    /// </summary>
    public static class PaletteAssetBuilder
    {
        /// <summary>共享核心板路径（工程内相对路径；域板路径见 PaletteBoard.DomainJsonPath）。</summary>
        public const string JsonPath = PaletteBoard.CoreJsonPath;

        /// <summary>Unity 侧镜像资产路径。</summary>
        public const string AssetPath = "Assets/Data/Palette/PiratePalette.asset";

        [MenuItem("PirateCrew/Art/调色板/JSON → PaletteAsset（生成镜像）", priority = 100)]
        public static void BuildFromJson()
        {
            PaletteJson.Root root;
            try
            {
                root = PaletteBoard.LoadMerged();
            }
            catch (Exception exc)
            {
                Debug.LogError("[PaletteAssetBuilder] 合并视图装载失败：" + exc.Message);
                return;
            }

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
                + "（共享核心 + 域 " + PaletteBoard.ActiveDomain + " 的合并视图，"
                + root.slots.Count + " 色 / v" + root.version
                + " / 纹素密度 " + root.texelDensityPxPerMeter + "px·m⁻¹）。真源 = " + JsonPath
                + " + " + PaletteBoard.DomainJsonPath(PaletteBoard.ActiveDomain));
        }

        [MenuItem("PirateCrew/Art/调色板/PaletteAsset → JSON（按层回写真源）", priority = 101)]
        public static void ExportToJson()
        {
            PaletteAsset asset = AssetDatabase.LoadAssetAtPath<PaletteAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("[PaletteAssetBuilder] 找不到镜像资产 " + AssetPath + "，先跑 JSON → PaletteAsset。");
                return;
            }

            var fromAsset = new PaletteJson.Root
            {
                name = asset.paletteName,
                version = asset.version,
                texelDensityPxPerMeter = asset.texelDensityPxPerMeter,
                note = asset.note,
                slots = asset.slots,
            };

            // 真源分域后镜像不再对应单个文件：按槽位归属把改动提升回所属层（核心板/当前域板）。
            // 镜像里不属于本层的槽位（其他域的）本层没有，天然跳过；镜像多余的槽位不动（保守）。
            string domain = PaletteBoard.ActiveDomain;
            int written = 0;
            if (PromoteLayer(PaletteBoard.CoreJsonPath, fromAsset, true))
                written++;
            if (File.Exists(PaletteBoard.AbsPath(PaletteBoard.DomainJsonPath(domain)))
                && PromoteLayer(PaletteBoard.DomainJsonPath(domain), fromAsset, false))
                written++;

            if (written == 0)
                Debug.Log("[PaletteAssetBuilder] 真源已是镜像的规范文本，无变化（核心板 + 域 " + domain + "）。");
        }

        /// <summary>把镜像里属于该层的槽位（核心板还含顶层字段）提升回层文件。有改动返回 true。</summary>
        static bool PromoteLayer(string projectPath, PaletteJson.Root fromAsset, bool isCore)
        {
            string abs = PaletteBoard.AbsPath(projectPath);
            if (!File.Exists(abs))
                return false;
            PaletteJson.Root layer = PaletteJson.Parse(PaletteBoard.ReadTextLf(abs));

            var fromAssetById = new Dictionary<string, PaletteSlot>();
            foreach (PaletteSlot slot in fromAsset.slots)
            {
                if (slot != null && !string.IsNullOrEmpty(slot.id))
                    fromAssetById[slot.id] = slot;
            }

            bool changed = false;
            for (int i = 0; i < layer.slots.Count; i++)
            {
                PaletteSlot mine = layer.slots[i];
                if (mine == null || !fromAssetById.TryGetValue(mine.id, out PaletteSlot theirs))
                    continue;
                if (!SameSlot(mine, theirs))
                {
                    layer.slots[i] = theirs;   // 槽位整体归属本层：六个字段一起提升
                    changed = true;
                }
            }

            if (isCore)
            {
                // 核心板顶层字段归镜像管；mergeOrder 是合并序结构声明，保持层文件原样（镜像不携带）
                if (layer.name != fromAsset.name)
                {
                    layer.name = fromAsset.name;
                    changed = true;
                }
                if (layer.version != fromAsset.version)
                {
                    layer.version = fromAsset.version;
                    changed = true;
                }
                if (layer.texelDensityPxPerMeter != fromAsset.texelDensityPxPerMeter)
                {
                    layer.texelDensityPxPerMeter = fromAsset.texelDensityPxPerMeter;
                    changed = true;
                }
                if (layer.note != fromAsset.note)
                {
                    layer.note = fromAsset.note;
                    changed = true;
                }
            }

            if (!changed)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllText(abs, PaletteJson.ToCanonicalText(layer), new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(projectPath);
            Debug.Log("[PaletteAssetBuilder] 已回写" + (isCore ? "共享核心板" : "域板") + "：" + projectPath);
            return true;
        }

        static bool SameSlot(PaletteSlot a, PaletteSlot b)
        {
            return a.id == b.id && a.group == b.group && a.hex == b.hex
                && a.usage == b.usage && a.status == b.status && a.source == b.source;
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
            string fromFiles;
            try
            {
                fromFiles = PaletteJson.ToCanonicalText(PaletteBoard.LoadMerged());
            }
            catch (Exception exc)
            {
                Debug.LogError("[PaletteAssetBuilder] 对账失败：" + exc.Message);
                return;
            }
            bool same = fromAsset == fromFiles;

            int assetColors = asset.slots == null ? 0 : asset.slots.Count;
            Debug.Log("[PaletteAssetBuilder] 对账（共享核心 + 域 " + PaletteBoard.ActiveDomain + "）："
                + (same ? "一致" : "**不一致**")
                + "（镜像 " + assetColors + " 色 vs 合并视图规范文本 " + fromFiles.Length + " 字节；"
                + "镜像规范文本 " + fromAsset.Length + " 字节）"
                + (same ? "" : "\n  处理：若 Inspector 的改动是要保留的 → 跑 ExportToJson；否则跑 BuildFromJson。"));

            if (!same)
            {
                int limit = Mathf.Min(400, Mathf.Min(fromAsset.Length, fromFiles.Length));
                for (int i = 0; i < limit; i++)
                {
                    if (fromAsset[i] != fromFiles[i])
                    {
                        Debug.LogWarning("[PaletteAssetBuilder] 首个差异在第 " + i + " 字节附近：\n  镜像="
                            + Excerpt(fromAsset, i) + "\n  真源=" + Excerpt(fromFiles, i));
                        break;
                    }
                }
            }

            if (asset.texelDensityPxPerMeter <= 0)
                Debug.LogWarning("[PaletteAssetBuilder] 纹素密度非正：" + asset.texelDensityPxPerMeter);
        }

        [MenuItem("PirateCrew/Art/调色板/选择当前域…", priority = 103)]
        public static void PickDomain()
        {
            var menu = new GenericMenu();
            string current = PaletteBoard.ActiveDomain;
            foreach (string domain in PaletteBoard.DiscoverDomains())
            {
                string d = domain;
                menu.AddItem(new GUIContent(d + (d == PaletteBoard.DefaultDomain ? "（默认）" : "")),
                    d == current,
                    () =>
                    {
                        PaletteBoard.SetActiveDomain(d);
                        Debug.Log("[PaletteAssetBuilder] 当前域已切为 " + d
                            + "——重跑「JSON → PaletteAsset」重建镜像（消费方看到的地形/海洋风格随之切换）。");
                    });
            }
            menu.ShowAsContext();
        }

        static string Excerpt(string text, int index)
        {
            int start = Mathf.Max(0, index - 40);
            int length = Mathf.Min(80, text.Length - start);
            return text.Substring(start, length).Replace("\n", "⏎");
        }

        // 文本读取的行尾归一（ReadTextLf）与工程内相对路径换算（AbsPath）移到 PaletteBoard——
        // 分域装载与镜像生成共用同一份实现，避免两侧漂移。

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
