using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 调色板 JSON 镜像的**规范文本**读写器（C# 侧实现，与 tools/palette/palette_tool.py
    /// 的 <c>canonical_text()</c> 逐字节等价）。
    ///
    /// 【它解决什么问题】板的三个消费者（Python 量化工具 / Unity 侧 PaletteAssetBuilder /
    /// 人眼 review）如果各自按自己的库 dumps，会产生大量与语义无关的格式差异，真正的改动
    /// 淹没在 diff 噪声里、且「两边是否一致」无法用比较字节来判定。本类把布局钉死：
    ///
    ///   顶层：<c>{</c> → 固定顺序的 <c>name/version/texelDensityPxPerMeter/note</c> 各占一行
    ///   （2 空格缩进、<c>": "</c> 分隔、行尾逗号）→ <c>"slots": [</c> → **一槽一行** →
    ///   <c>]</c> → <c>}</c>；UTF-8 无 BOM、LF 换行、末尾换行、中文不转义。
    ///
    ///   槽位字段顺序固定 <c>id, group, hex, usage, status, source</c>，行内 <c>", "</c> 分隔。
    ///
    /// 【契约】改本类的布局必须同步改 palette_tool.py —— python 侧 <c>verify</c> 的
    /// 「板文件是规范文本」用例是唯一能兜住两侧漂移的检查。
    /// 【为什么手写而不用 JsonUtility.ToJson】JsonUtility 的 prettyPrint 布局与 Python
    /// json.dumps 不同（括号、逗号、缩进都不同），拿不到逐字节一致；
    /// 反序列化仍用 JsonUtility（它足够稳，且省一个解析器）。
    /// </summary>
    public static class PaletteJson
    {
        /// <summary>槽位字段的固定写出顺序（与 palette_tool.py 的 SLOT_KEYS 一致）。</summary>
        static readonly string[] SlotKeys = { "id", "group", "hex", "usage", "status", "source" };

        /// <summary>顶层字段的固定写出顺序（与 palette_tool.py 的 ROOT_KEYS 一致）。</summary>
        static readonly string[] RootKeys = { "name", "version", "texelDensityPxPerMeter", "note" };

        /// <summary>JSON 根的反序列化外壳（JsonUtility 要求 [Serializable] 的普通类 + public 字段）。</summary>
        [Serializable]
        public class Root
        {
            public string name;
            public int version;
            public int texelDensityPxPerMeter;
            public string note;
            public List<PaletteSlot> slots = new List<PaletteSlot>();
        }

        /// <summary>解析 JSON 文本。解析失败抛 <see cref="ArgumentException"/>（不静默给空板）。</summary>
        public static Root Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("[PaletteJson] JSON 文本为空");
            Root root;
            try
            {
                root = JsonUtility.FromJson<Root>(json);
            }
            catch (Exception exc)
            {
                throw new ArgumentException("[PaletteJson] 解析失败：" + exc.Message);
            }
            if (root == null || root.slots == null || root.slots.Count == 0)
                throw new ArgumentException("[PaletteJson] 解析结果没有槽位（板文件损坏？）");
            return root;
        }

        /// <summary>把资产渲染成规范文本（资产 → JSON 方向的唯一写出点）。</summary>
        public static string ToCanonicalText(PaletteAsset asset)
        {
            var root = new Root
            {
                name = asset.paletteName,
                version = asset.version,
                texelDensityPxPerMeter = asset.texelDensityPxPerMeter,
                note = asset.note,
                slots = asset.slots,
            };
            return ToCanonicalText(root);
        }

        /// <summary>把 JSON 根渲染成规范文本。**与 palette_tool.py 的 canonical_text 逐字节一致**。</summary>
        public static string ToCanonicalText(Root root)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\n");
            for (int i = 0; i < RootKeys.Length; i++)
            {
                switch (RootKeys[i])
                {
                    case "name":
                        AppendString(sb, "name", root.name);
                        break;
                    case "version":
                        sb.Append("  \"version\": ").Append(root.version.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
                        break;
                    case "texelDensityPxPerMeter":
                        sb.Append("  \"texelDensityPxPerMeter\": ")
                          .Append(root.texelDensityPxPerMeter.ToString(System.Globalization.CultureInfo.InvariantCulture))
                          .Append(",\n");
                        break;
                    case "note":
                        AppendString(sb, "note", root.note);
                        break;
                }
            }

            sb.Append("  \"slots\": [\n");
            for (int i = 0; i < root.slots.Count; i++)
            {
                PaletteSlot slot = root.slots[i];
                sb.Append("    {");
                bool first = true;
                for (int k = 0; k < SlotKeys.Length; k++)
                {
                    string value = SlotField(slot, SlotKeys[k]);
                    if (value == null)
                        continue;
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    sb.Append('"').Append(SlotKeys[k]).Append("\": ").Append(PythonJsonString(value));
                }
                sb.Append('}');
                if (i != root.slots.Count - 1)
                    sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        static string SlotField(PaletteSlot slot, string key)
        {
            switch (key)
            {
                case "id": return slot.id;
                case "group": return slot.group;
                case "hex": return slot.hex;
                case "usage": return slot.usage;
                case "status": return slot.status;
                case "source": return slot.source;
                default: return null;
            }
        }

        static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append("  \"").Append(key).Append("\": ").Append(PythonJsonString(value)).Append(",\n");
        }

        /// <summary>
        /// 字符串字面量编码，口径与 <c>json.dumps(s, ensure_ascii=False)</c> 一致：
        /// 只转义 <c>" \ \b \f \n \r \t</c> 与其余 &lt;0x20 的控制字符（转 <c>\uXXXX</c>），
        /// 中文与全角符号原样输出（板里的注释都是中文）。
        /// </summary>
        public static string PythonJsonString(string value)
        {
            if (value == null)
                return "\"\"";
            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>结构化校验（与 palette_tool.py 的 validate_palette 同口径，供 Editor 侧即时反馈）。</summary>
        public static void Validate(Root root, List<string> errors, List<string> warnings)
        {
            if (root.slots.Count < 32 || root.slots.Count > 64)
                errors.Add("色数 " + root.slots.Count + " 不在 32~64 区间（美术风格指南 §3.2）");

            var seenIds = new HashSet<string>();
            var seenHex = new Dictionary<string, string>();
            for (int i = 0; i < root.slots.Count; i++)
            {
                PaletteSlot slot = root.slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.id))
                {
                    errors.Add("第 " + i + " 个槽位缺 id");
                    continue;
                }
                if (!seenIds.Add(slot.id))
                    errors.Add("槽位 id 重复：" + slot.id);
                if (!PaletteAsset.ParseHex(slot.hex, out _))
                    errors.Add("槽位 " + slot.id + " 的 hex 非法：" + slot.hex);
                else
                {
                    string key = slot.hex.TrimStart('#').ToUpperInvariant();
                    if (seenHex.TryGetValue(key, out string other))
                        errors.Add("色值重复：" + other + " 与 " + slot.id + " 同为 #" + key);
                    else
                        seenHex[key] = slot.id;
                }
                for (int f = 0; f < SlotKeys.Length; f++)
                {
                    if (string.IsNullOrEmpty(SlotField(slot, SlotKeys[f])))
                        warnings.Add("槽位 " + slot.id + " 缺 " + SlotKeys[f] + " 字段"
                            + "（项目规范要求标注出处与提案状态）");
                }
            }
        }
    }
}
