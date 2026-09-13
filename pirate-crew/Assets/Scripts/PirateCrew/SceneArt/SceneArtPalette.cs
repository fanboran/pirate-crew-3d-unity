using System.Globalization;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 场景美术调色板（纯 C#，无头可测）：把 <c>docs/美术风格指南.md</c> §2.1 与
    /// <c>docs/场景设计-战斗竞技场.md</c> §3.1/§4/§5 用到的十六进制色值集中到一处，
    /// 供场景陈设（编辑器构建 + 运行时壳体）共用，避免色值散落在多处。
    ///
    /// 【标注纪律】每项注明出处；GDD §10.4 调色板为【依据】，其余为【AI 提案】。
    ///   · 沙地/草地/岩石/海水/木材三档 = GDD §10.4（`gdd.md:815-820`），经美术风格指南 §2.1 转写；
    ///   · 阵营红/蓝 = 逆向文档小地图配色（`参考游戏逆向…静态.md:721`）；
    ///   · 危险色 #CC2222 / 描边 #2A2A2A = GDD §10.4（`gdd.md:831,833`）；
    ///   · 湿沙三档、天空三档、远景剪影 = 【AI 提案】（原版未导出该档色值）。
    ///
    /// 【色空间】本工程 ProjectSettings <c>m_ActiveColorSpace = 0</c>（Gamma），
    /// 故 sRGB 十六进制直接归一化即与色板一致（与 <c>BattleSceneLighting.Hex</c> 同口径，
    /// 切 Linear 时本文件必须一并改成 <c>.linear</c>）。
    /// </summary>
    public static class SceneArtPalette
    {
        // ---- 沙地 / 湿沙（GDD §10.4 沙地三档 + 湿沙档【AI 提案】）----
        /// <summary>干沙亮档 `#E8D5A3`（GDD §10.4 沙地亮面）。</summary>
        public const string SandDryLight = "#E8D5A3";
        /// <summary>沙地中档 `#C4A76A`（GDD §10.4 沙地中间调）。</summary>
        public const string SandMid = "#C4A76A";
        /// <summary>沙地暗档 `#8B7355`（GDD §10.4 沙地暗面）。</summary>
        public const string SandDark = "#8B7355";
        /// <summary>湿沙暗档 `#5C4A34`【AI 提案：GDD 无湿沙色，由沙地暗档压暗得到】。</summary>
        public const string SandWetDark = "#5C4A34";

        // ---- 岩石（GDD §10.4 岩石三档）----
        /// <summary>岩石亮档 `#B8A99A`。</summary>
        public const string RockLight = "#B8A99A";
        /// <summary>岩石中档 `#8C7B6A`。</summary>
        public const string RockMid = "#8C7B6A";
        /// <summary>岩石暗档 `#5C4F42`。</summary>
        public const string RockDark = "#5C4F42";

        // ---- 木材（GDD §10.4 木材三档）----
        /// <summary>木材亮档 `#D4A76A`（木板/桅杆/箱体亮面）。</summary>
        public const string WoodLight = "#D4A76A";
        /// <summary>木材中档 `#A67B42`。</summary>
        public const string WoodMid = "#A67B42";
        /// <summary>木材暗档 `#6B4C28`（水线以下木料）。</summary>
        public const string WoodDark = "#6B4C28";

        // ---- 金属（美术风格指南 §3.1【AI 提案】：GDD 无金属色值）----
        /// <summary>铁/链/箍 `#5C4F42`（美术风格指南 §3.1「铁」档偏冷，此处取岩石暗档同值以省材质）。</summary>
        public const string Iron = "#5C4F42";
        /// <summary>黄铜/金 `#C9A227`（美术风格指南 §3.1）。</summary>
        public const string Brass = "#C9A227";

        // ---- 植被（GDD §10.4 草地三档 + 树叶补充档）----
        /// <summary>草亮档 `#7BC67E`。</summary>
        public const string GrassLight = "#7BC67E";
        /// <summary>草中档/灌木 `#4A8C4A`。</summary>
        public const string GrassMid = "#4A8C4A";
        /// <summary>草暗档 `#2D5A2D`。</summary>
        public const string GrassDark = "#2D5A2D";

        // ---- 海水（GDD §10.4 海水三档；实际由 PirateWater shader 使用）----
        /// <summary>浅水 `#4DA6D9`。</summary>
        public const string WaterShallow = "#4DA6D9";
        /// <summary>中水 `#2B7AB8`。</summary>
        public const string WaterMid = "#2B7AB8";
        /// <summary>深水 `#1A4F7A`。</summary>
        public const string WaterDeep = "#1A4F7A";

        // ---- 阵营 / 语义色 ----
        /// <summary>红队 `#FF3A29`（逆向文档小地图配色 `静态:721`）。</summary>
        public const string TeamRed = "#FF3A29";
        /// <summary>蓝队 `#3366FF`（同上）。</summary>
        public const string TeamBlue = "#3366FF";
        /// <summary>落水危险色 `#CC2222`（GDD §10.4 `gdd.md:833`）。</summary>
        public const string Danger = "#CC2222";
        /// <summary>场景物描边色 `#2A2A2A`（GDD §10.4 `gdd.md:831`）。</summary>
        public const string Outline = "#2A2A2A";
        /// <summary>泡沫/浪花色 `#F0F7FF`（场景文档 §5.3【AI 提案】）。</summary>
        public const string Foam = "#F0F7FF";

        // ---- 远景（场景文档 §6.3【AI 提案】）----
        /// <summary>远景剪影岛近档 `#7E93A8`。</summary>
        public const string FarSilhouetteNear = "#7E93A8";
        /// <summary>远景剪影岛远档 `#AFC2D4`。</summary>
        public const string FarSilhouetteFar = "#AFC2D4";
        /// <summary>云 / 远帆白 `#FFFFFF`。</summary>
        public const string CloudWhite = "#FFFFFF";
        /// <summary>远帆帆布白 `#E8E8E0`。</summary>
        public const string SailFarWhite = "#E8E8E0";

        /// <summary>
        /// 解析 sRGB 十六进制（<c>#RRGGBB</c> 或 <c>#RRGGBBAA</c>；<c>#</c> 可省，大小写不限）。
        /// 解析失败返回品红（便于肉眼发现问题，与 <c>BattleSceneLighting.Hex</c> 同口径）。
        ///
        /// 【为什么不用 <c>ColorUtility.TryParseHtmlString</c>】它是原生 <c>ECall</c>，
        /// 脱离 Unity 运行时必抛 <c>SecurityException</c>（见 external/m2-harness/README.md）——
        /// 用了它整个调色板就无法在无头验证台上断言。这里自实现纯 C# 解析，
        /// 既保住"色值可无头测试"，也少一个 Unity 依赖。
        /// </summary>
        public static Color Hex(string hex)
        {
            return TryParseHex(hex, out Color color) ? color : Color.magenta;
        }

        /// <summary>解析 sRGB 十六进制并覆盖 alpha。</summary>
        public static Color Hex(string hex, float alpha)
        {
            Color color = Hex(hex);
            color.a = alpha;
            return color;
        }

        /// <summary>纯 C# 十六进制解析；失败返回 false（不抛异常）。</summary>
        public static bool TryParseHex(string hex, out Color color)
        {
            color = Color.magenta;
            if (string.IsNullOrEmpty(hex))
                return false;

            string text = hex.Trim();
            if (text.Length > 0 && text[0] == '#')
                text = text.Substring(1);

            if (text.Length != 6 && text.Length != 8)
                return false;

            if (!TryParseByte(text, 0, out int r)
                || !TryParseByte(text, 2, out int g)
                || !TryParseByte(text, 4, out int b))
                return false;

            int a = 255;
            if (text.Length == 8 && !TryParseByte(text, 6, out a))
                return false;

            color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            return true;
        }

        static bool TryParseByte(string text, int offset, out int value)
        {
            return int.TryParse(text.Substring(offset, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out value);
        }
    }
}
