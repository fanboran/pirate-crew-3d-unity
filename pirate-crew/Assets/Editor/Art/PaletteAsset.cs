using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 调色板的一个命名槽位（板的「令牌」）。
    ///
    /// 【为什么字段都是 string 而不是 Color】板的真源是
    /// <c>Assets/Data/Palette/pirate_palette.json</c>，槽位的 hex 字符串才是能逐字节 diff、
    /// 能被 Python 与 C# 双方一致读写的形态；转 <see cref="Color"/> 只发生在消费点
    /// （材质生成、UI 派生）。板是**制作管线令牌**，不是运行时数据。
    /// </summary>
    [Serializable]
    public class PaletteSlot
    {
        /// <summary>槽位 id（全大写 A-Z/0-9/_）。这是代码里唯一该引用的键。</summary>
        public string id;

        /// <summary>所属阶梯族（同一族内按 OkLCH 明度升序声明，见调色板手册 §2）。</summary>
        public string group;

        /// <summary>sRGB 十六进制（6 位 RRGGBB，不带 #）。</summary>
        public string hex;

        /// <summary>用途说明（写给用板的人看：这个色该用在哪）。</summary>
        public string usage;

        /// <summary>状态：【裁定】（创始人/正式文档已定）或【提案】（AI 提案，待试产校准）。</summary>
        public string status;

        /// <summary>出处（带文件/小节的可核对引用，项目文档规范的硬要求）。</summary>
        public string source;
    }

    /// <summary>
    /// 全局调色板资产（等距像素卡通的「风格令牌表」；美术风格指南 §3.2、
    /// 像素纹理资产管线 §5）。
    ///
    /// 【唯一真源】<c>Assets/Data/Palette/pirate_palette.json</c>。本资产是它的一份
    /// **派生镜像**，由 <see cref="PaletteAssetBuilder"/> 生成，供 Unity 侧工具
    /// （<see cref="ToonMaterialFactory"/>、UI 派生）在 Inspector 里直接读色值。
    /// 反向路径（Inspector 手改 → 回写 JSON）由 PaletteAssetBuilder.ExportToJson 提供，
    /// 两条命令互为镜像且收敛到同一字节，所以任何一方手改都能被另一方对账出来。
    ///
    /// 【为什么本类型放在 Assets/Editor 下（editor-only）】它是**制作期令牌**，
    /// 消费点全在 Editor 工具与离线烘焙链；运行时材质把颜色烘进材质资产，播放器不需要读板。
    /// 放在 Editor 程序集换来的好处是：无头验证台（<c>-p:HarnessScope=DataEditor</c>）能
    /// 编译它 —— 美术管线代码的编译门禁因此不需要开 Unity。
    ///
    /// 【色空间】本工程 <c>m_ActiveColorSpace = 0</c>（Gamma），hex 归一化即与屏幕一致
    /// （口径同 <c>SceneArtPalette.Hex</c> / <c>BattleSceneLighting.Hex</c>）；
    /// 切 Linear 时本类与调色板手册必须一并改。
    /// </summary>
    public class PaletteAsset : ScriptableObject
    {
        /// <summary>板名（与 JSON 的 <c>name</c> 一致）。</summary>
        public string paletteName = "PiratePalette";

        /// <summary>板版本（吸收试产反馈时 +1；M1 v0 → M2g v1 的演进锚点）。</summary>
        public int version = 1;

        /// <summary>全项目纹素密度（px/米，资产篇 §1；随板携带，供密度校验工具取目标值）。</summary>
        public int texelDensityPxPerMeter = 32;

        /// <summary>板的使用说明与一致性契约（与 JSON 的 <c>note</c> 一致）。</summary>
        public string note = "";

        /// <summary>全部槽位，**声明顺序即语义**（组内按明度升序；见调色板手册 §2）。</summary>
        public List<PaletteSlot> slots = new List<PaletteSlot>();

        /// <summary>按 id 取槽位；缺失返回 false（调用方决定是报错还是兜底）。</summary>
        public bool TryGetSlot(string id, out PaletteSlot slot)
        {
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != null && slots[i].id == id)
                    {
                        slot = slots[i];
                        return true;
                    }
                }
            }
            slot = null;
            return false;
        }

        /// <summary>按 id 取 sRGB 十六进制；缺失返回 null。</summary>
        public string HexOf(string id)
        {
            return TryGetSlot(id, out PaletteSlot slot) ? slot.hex : null;
        }

        /// <summary>
        /// 按 id 取 Color（Gamma 空间直存，口径同 SceneArtPalette）。
        /// 缺失或 hex 非法返回**品红**——便于肉眼一眼发现问题，不静默给黑
        /// （这是本仓 <c>SceneArtPalette.Hex</c> 既有口径，保持一致）。
        /// </summary>
        public Color ColorOf(string id)
        {
            string hex = HexOf(id);
            return ParseHex(hex, out Color color) ? color : Color.magenta;
        }

        /// <summary>取某阶梯族的全部槽位（声明顺序）。</summary>
        public List<PaletteSlot> SlotsInGroup(string group)
        {
            var result = new List<PaletteSlot>();
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != null && slots[i].group == group)
                        result.Add(slots[i]);
                }
            }
            return result;
        }

        /// <summary>全部槽位 id（去重前的声明序）；PaletteAssetBuilder 用它查重。</summary>
        public string[] Ids()
        {
            var ids = new List<string>(slots == null ? 0 : slots.Count);
            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != null)
                        ids.Add(slots[i].id);
                }
            }
            return ids.ToArray();
        }

        /// <summary>
        /// 解析 <c>RRGGBB</c> / <c>#RRGGBB</c> / <c>RRGGBBAA</c>（大小写不限）。
        ///
        /// 【为什么不用 ColorUtility.TryParseHtmlString】它是原生 <c>ECall</c>，脱离 Unity
        /// 运行时必抛 <c>SecurityException</c>（external/harness/README.md）——用了它，
        /// 板解析就无法在无头环境下断言。自实现纯 C# 解析与 SceneArtPalette.Hex 同口径。
        /// </summary>
        public static bool ParseHex(string hex, out Color color)
        {
            color = Color.magenta;
            if (string.IsNullOrEmpty(hex))
                return false;
            string h = hex[0] == '#' ? hex.Substring(1) : hex;
            if (h.Length != 6 && h.Length != 8)
                return false;
            if (!TryByte(h, 0, out int r) || !TryByte(h, 2, out int g) || !TryByte(h, 4, out int b))
                return false;
            int a = 255;
            if (h.Length == 8 && !TryByte(h, 6, out a))
                return false;
            color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            return true;
        }

        static bool TryByte(string text, int offset, out int value)
        {
            return int.TryParse(text.Substring(offset, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out value);
        }
    }
}
