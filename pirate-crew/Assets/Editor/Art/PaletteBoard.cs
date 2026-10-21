using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 分域色板的**合并视图**装载器（共享核心板 + 当前域板 → 单个 <see cref="PaletteJson.Root"/>）。
    ///
    /// 【真源结构】Assets/Data/Palette/ 下分域两层（分域色板体系）：
    ///   · pirate_palette.json   共享核心板：跨关共用槽（INK/UI 族/FACTION_*/SKY_*/WHITE_HOT 等）
    ///                           + 顶层 <c>mergeOrder</c>（合并视图的槽位顺序声明）；
    ///   · palette_&lt;域&gt;.json     域板：每关/每生物群系的地形四族（SAND/ROCK/WOOD/GRASS）与
    ///                           SEA 风格变体（现役 caribbean；结构示例 chemplant）。
    /// 资产实际可用的板 = 共享核心 + 当前域的**合并视图**。消费方（PiratePalette.asset、
    /// 直读 JSON 的编辑器工具）只看合并视图，不感知拆分；现役加勒比域的合并视图与
    /// 拆分前的单板**逐位一致**（零视觉漂移；板结构与合并口径见 docs/技术/资产管线/调色板.md）。
    ///
    /// 【合并序】核心板 mergeOrder 声明已知槽位的合并顺序；order 里只属于其他域的槽位
    /// （本次合并不在场）跳过，未列入 order 的槽位按「核心板在前、域板在后」的声明序
    /// 追加尾部（确定性，不依赖字典序）。与本仓库 <c>tools/palette/palette_tool.py</c> 的
    /// <c>merge_board_data</c> 同口径，两侧任一漂移都会被 palette_tool.py verify /
    /// PaletteAssetBuilder.Verify 当场抓住。
    /// </summary>
    public static class PaletteBoard
    {
        /// <summary>当前域的缺省值（生产现役 = 加勒比；菜单「选择当前域…」可切，落 EditorPrefs）。</summary>
        public const string DefaultDomain = "caribbean";

        /// <summary>当前域在 EditorPrefs 里的键。</summary>
        public const string DomainPrefsKey = "PirateCrew.Palette.ActiveDomain";

        /// <summary>调色板目录（工程内相对路径）。</summary>
        public const string PaletteDir = "Assets/Data/Palette";

        /// <summary>共享核心板路径（工程内相对路径）。</summary>
        public const string CoreJsonPath = PaletteDir + "/pirate_palette.json";

        /// <summary>当前域（EditorPrefs 覆盖，缺省 <see cref="DefaultDomain"/>）。</summary>
        public static string ActiveDomain => EditorPrefs.GetString(DomainPrefsKey, DefaultDomain);

        /// <summary>切换当前域（只落偏好；重跑 BuildFromJson 才会重建镜像）。</summary>
        public static void SetActiveDomain(string domain)
        {
            EditorPrefs.SetString(DomainPrefsKey, domain);
        }

        /// <summary>域板路径（工程内相对路径）。</summary>
        public static string DomainJsonPath(string domain)
        {
            return PaletteDir + "/palette_" + domain + ".json";
        }

        /// <summary>扫描调色板目录里的全部域板名（字典序；核心板不在列）。</summary>
        public static List<string> DiscoverDomains()
        {
            const string prefix = "palette_", suffix = ".json";
            var names = new List<string>();
            string dir = Path.GetDirectoryName(AbsPath(CoreJsonPath));
            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir, prefix + "*" + suffix))
                {
                    string name = Path.GetFileName(file);
                    names.Add(name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length));
                }
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>装载共享核心板。文件缺失/解析失败抛异常（不静默给空板）。</summary>
        public static PaletteJson.Root LoadCore()
        {
            return PaletteJson.Parse(ReadTextLf(AbsPath(CoreJsonPath)));
        }

        /// <summary>装载指定域板。文件缺失/解析失败抛异常。</summary>
        public static PaletteJson.Root LoadDomain(string domain)
        {
            string abs = AbsPath(DomainJsonPath(domain));
            if (!File.Exists(abs))
                throw new ArgumentException("[PaletteBoard] 找不到域板：" + DomainJsonPath(domain)
                    + "（现有域：" + string.Join(", ", DiscoverDomains().ToArray()) + "）");
            return PaletteJson.Parse(ReadTextLf(abs));
        }

        /// <summary>合并视图（共享核心 + 当前域）。结构问题当场抛异常。</summary>
        public static PaletteJson.Root LoadMerged()
        {
            return LoadMerged(ActiveDomain);
        }

        /// <summary>
        /// 合并视图（共享核心 + 指定域；域传 null 时不合域板，即单文件板兼容模式）。
        /// 顶层字段取核心板；mergeOrder 是真源结构声明，**不进合并视图**——
        /// 加勒比合并视图的规范文本因此与拆分前的单板逐位一致。
        /// </summary>
        public static PaletteJson.Root LoadMerged(string domain)
        {
            PaletteJson.Root core = LoadCore();
            var byId = new Dictionary<string, PaletteSlot>();
            var tail = new List<PaletteSlot>();
            AddLayer(core, byId, tail, "核心板");
            if (!string.IsNullOrEmpty(domain))
                AddLayer(LoadDomain(domain), byId, tail, "域 " + domain);

            var merged = new PaletteJson.Root
            {
                name = core.name,
                version = core.version,
                texelDensityPxPerMeter = core.texelDensityPxPerMeter,
                note = core.note,
                mergeOrder = null,
                slots = new List<PaletteSlot>(),
            };

            var seen = new HashSet<string>();
            string[] order = core.mergeOrder ?? Array.Empty<string>();
            foreach (string id in order)
            {
                if (!seen.Add(id))
                    throw new ArgumentException("[PaletteBoard] mergeOrder 重复声明槽位：" + id);
                if (byId.TryGetValue(id, out PaletteSlot slot))
                    merged.slots.Add(slot);   // 只属于其他域的槽位不在场：跳过（check 的跨层联合校验兜拼错）
            }
            foreach (PaletteSlot slot in tail)
            {
                if (!seen.Contains(slot.id))
                    merged.slots.Add(slot);   // 未列入 mergeOrder 的新槽：核心板在前、域板在后追加尾部
            }
            return merged;
        }

        static void AddLayer(PaletteJson.Root root, Dictionary<string, PaletteSlot> byId,
            List<PaletteSlot> tail, string label)
        {
            foreach (PaletteSlot slot in root.slots)
            {
                if (slot == null || string.IsNullOrEmpty(slot.id))
                    continue;
                if (byId.ContainsKey(slot.id))
                    throw new ArgumentException("[PaletteBoard] 槽位 id 重复（" + label + "）：" + slot.id
                        + "（一个组的槽位必须整体住在一层，域板只许新增/整体接管组）");
                byId[slot.id] = slot;
                tail.Add(slot);
            }
        }

        /// <summary>工程内相对路径 → 绝对路径（File IO 必须绝对路径；口径同 PaletteAssetBuilder 旧实现）。</summary>
        public static string AbsPath(string projectRelative)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelative));
        }

        /// <summary>
        /// 读文本并归一行尾到 LF。
        ///
        /// 【为什么要这一层】本仓 <c>core.autocrlf=true</c>，而 <c>.gitattributes</c> 的 <c>eol=lf</c>
        /// 白名单只覆盖 Unity 序列化扩展名（.meta/.unity/.prefab/.asset/.mat/.controller）——
        /// **.json 不在其中**，别的机器或新克隆签出板文件时可能拿到 CRLF。规范文本的定义是 LF，
        /// 但"读到的 CRLF"不该被报成"镜像与真源不一致"（那是 git 的行尾策略，不是内容差异）。
        /// 写出侧（<see cref="PaletteAssetBuilder.ExportToJson"/>）永远写 LF。
        /// 【根治】给 <c>.gitattributes</c> 补一行 <c>*.json text eol=lf</c> 可让这层容错变成冗余；
        /// 该文件属仓库根共享基建，改动登记在 docs/技术/资产管线/调色板.md 的遗留区。
        /// </summary>
        public static string ReadTextLf(string abs)
        {
            return File.ReadAllText(abs).Replace("\r\n", "\n");
        }
    }
}
