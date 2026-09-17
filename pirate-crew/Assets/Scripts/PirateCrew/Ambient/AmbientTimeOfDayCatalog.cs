using System.Globalization;
using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient
{
    /// <summary>昼夜/天气档位（默认 <see cref="AmbientTimeOfDay.Noon"/>，不改变默认可玩状态）。</summary>
    public enum AmbientTimeOfDay
    {
        /// <summary>正午碧海（关卡 1-5 / 16-21 档）。**默认档**。</summary>
        Noon = 0,

        /// <summary>黄昏暖调（关卡 6-10 / 22-27 档）。</summary>
        Dusk = 1,

        /// <summary>风暴/阴云（关卡 11-15 / 28-33 档）。</summary>
        Overcast = 2,
    }

    /// <summary>
    /// 一档光照预设（主光 / 雾 / 环境光 / 天空 tint）。纯 C# 值类型，无头可测。
    /// </summary>
    public readonly struct AmbientLightingPreset
    {
        /// <summary>档位。</summary>
        public readonly AmbientTimeOfDay TimeOfDay;

        /// <summary>主方向光颜色。</summary>
        public readonly Color SunColor;

        /// <summary>主方向光强度。</summary>
        public readonly float SunIntensity;

        /// <summary>主方向光欧拉角（x = 俯仰、y = 方位）。</summary>
        public readonly Vector3 SunEuler;

        /// <summary>线性雾颜色（= 该档天空地平线色，保证"大气透视"而非"盖灰"）。</summary>
        public readonly Color FogColor;

        /// <summary>雾起始距离。</summary>
        public readonly float FogStart;

        /// <summary>雾结束距离。</summary>
        public readonly float FogEnd;

        /// <summary>环境光颜色（冷色托底暗部）。</summary>
        public readonly Color AmbientColor;

        /// <summary>环境光强度。</summary>
        public readonly float AmbientIntensity;

        /// <summary>天顶 tint（可选写入天空盒材质的 <c>_SkyTint</c>；不写也不影响可玩性）。</summary>
        public readonly Color SkyTint;

        public AmbientLightingPreset(AmbientTimeOfDay timeOfDay, Color sunColor, float sunIntensity,
            Vector3 sunEuler, Color fogColor, float fogStart, float fogEnd,
            Color ambientColor, float ambientIntensity, Color skyTint)
        {
            TimeOfDay = timeOfDay;
            SunColor = sunColor;
            SunIntensity = sunIntensity;
            SunEuler = sunEuler;
            FogColor = fogColor;
            FogStart = fogStart;
            FogEnd = fogEnd;
            AmbientColor = ambientColor;
            AmbientIntensity = ambientIntensity;
            SkyTint = skyTint;
        }
    }

    /// <summary>
    /// 昼夜/天气档位目录（纯 C#）。
    ///
    /// 【正午档为什么必须与场景初始光照"三方逐值一致"】
    /// 任务书要求"默认正午，不要改变默认可玩状态"：<see cref="AmbientTimeOfDay.Noon"/> 的每个值
    /// 都必须与 <c>Assets/Editor/BattleSceneLighting.cs</c> 的写值、以及 Battle 场景 RenderSettings
    /// 的烘焙值**三方逐值一致**：主光 <c>#FFF4E0</c>/1.55、<c>Euler(48,140,0)</c>、
    /// 线性雾 <c>#B0D4F1</c> start 150 / end 1200、环境光强度 0.85。
    /// 这样 <c>applyPresetOnStart</c> 应用正午档时画面零变化，只有主动切档才改变氛围。
    /// 【雾距的口径】审计契约「可见海域预算」（docs/审计/视觉审计报告.md §三）【提案/待定】：
    /// 全景相机距离 ≤160u、55° 俯角下画面可见海面斜距 ≤~350u——正午雾 150→1200 保住主战区
    /// 不被雾洗白，远海由海洋侧地平线融合（1000→1400）在雾全饱和前收干净海天线。
    /// 主光口径的裁决出处：<c>docs/阳光感打光调研.md</c> §4 调法 2
    /// （直射:天光 ≈4:1 → 主光 1.55 / 环境光 0.85；姿态 <c>Euler(48,140,0)</c> 维持原裁决）。
    /// **改 BattleSceneLighting 的主光/雾距必须同步这里与场景烘焙值（三方一起改）**，
    /// 否则一开局 <c>applyPresetOnStart</c> 就把预设值写回去、编辑器与运行时画面不一致。
    ///
    /// 【黄昏/阴云的雾距怎么来的】同为契约表【提案/待定】：黄昏 130→1000、阴云 110→800，
    /// 维持"正午>黄昏>阴云"的距离单调性（阴云能见度最差），有测试钉住（AmbientTimeOfDayTests）。
    ///
    /// 【黄昏/阴云档的强度怎么来的】**相对正午的比例是提案/待定**。<c>docs/美术风格指南.md</c> §4.6
    /// 给了三档强度 1.9 / 1.6 / 1.4，按比例（黄昏/正午 = 1.6/1.9 = 0.842、
    /// 风暴/正午 = 1.4/1.9 = 0.737）乘到工程现状上得到本文件的取值。
    /// 【环境光强度 2026-09-13 按比例缩放】正午环境光 1.00→0.85 后，黄昏/阴云若不跟着缩，
    /// 会反超正午破坏"正午>黄昏>阴云"单调性（有测试钉住）。同乘 0.85：
    /// 黄昏 0.90→0.765、阴云 0.75→0.64。主光强度未动（0.93/0.81 保持历史提案值），
    /// 切档观感需人眼验收后再定，届时一并更新本注释。
    ///
    /// 【雾色/环境色出处】美术风格指南 §4.6 与场景设计 §6.1 的档位表（该表本身已标【AI 提案】）；
    /// 天顶 tint 取 <c>SceneArt.SkyTierCatalog</c> 档 1/2/3 的天顶色（同一套档位口径）。
    /// </summary>
    public static class AmbientTimeOfDayCatalog
    {
        /// <summary>默认档 = 正午（任务书硬要求）。</summary>
        public const AmbientTimeOfDay Default = AmbientTimeOfDay.Noon;

        /// <summary>档位总数（供 UI/轮换使用）。</summary>
        public const int Count = 3;

        // ---- 色值常量（出处见类头注释）----
        const string NoonSunHex = "#FFF4E0";
        const string NoonFogHex = "#B0D4F1";
        const string NoonAmbientHex = "#C8DDF0";
        const string NoonSkyHex = "#4DA6D9";

        const string DuskSunHex = "#FFD9A8";
        const string DuskFogHex = "#F2B27A";
        const string DuskAmbientHex = "#E8C9A0";
        const string DuskSkyHex = "#3E7FB5";

        const string OvercastSunHex = "#D9E2EC";
        const string OvercastFogHex = "#9A8E86";
        const string OvercastAmbientHex = "#A8B4C0";
        const string OvercastSkyHex = "#244A72";

        /// <summary>取档位预设。</summary>
        public static AmbientLightingPreset For(AmbientTimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case AmbientTimeOfDay.Dusk:
                    return new AmbientLightingPreset(
                        AmbientTimeOfDay.Dusk,
                        Hex(DuskSunHex), 0.93f, new Vector3(24f, -30f, 0f),
                        Hex(DuskFogHex), 130f, 1000f,
                        Hex(DuskAmbientHex), 0.765f,
                        Hex(DuskSkyHex));

                case AmbientTimeOfDay.Overcast:
                    return new AmbientLightingPreset(
                        AmbientTimeOfDay.Overcast,
                        Hex(OvercastSunHex), 0.81f, new Vector3(56f, -28f, 0f),
                        Hex(OvercastFogHex), 110f, 800f,
                        Hex(OvercastAmbientHex), 0.64f,
                        Hex(OvercastSkyHex));

                default:
                    return new AmbientLightingPreset(
                        AmbientTimeOfDay.Noon,
                        Hex(NoonSunHex), 1.55f, new Vector3(48f, 140f, 0f),
                        Hex(NoonFogHex), 150f, 1200f,
                        Hex(NoonAmbientHex), 0.85f,
                        Hex(NoonSkyHex));
            }
        }

        /// <summary>档位中文显示名（面向玩家的文本一律中文，美术风格指南 §7.1）。</summary>
        public static string DisplayName(AmbientTimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case AmbientTimeOfDay.Dusk: return "黄昏";
                case AmbientTimeOfDay.Overcast: return "阴云";
                default: return "正午";
            }
        }

        /// <summary>下一个档位（循环：正午 → 黄昏 → 阴云 → 正午）。</summary>
        public static AmbientTimeOfDay Next(AmbientTimeOfDay timeOfDay)
        {
            int next = ((int)timeOfDay + 1) % Count;
            return (AmbientTimeOfDay)next;
        }

        /// <summary>把整数夹进合法档位（存档/配置读入时用）。</summary>
        public static AmbientTimeOfDay FromInt(int value)
        {
            if (value < 0)
                return Default;
            if (value >= Count)
                return Default;

            return (AmbientTimeOfDay)value;
        }

        /// <summary>
        /// sRGB 十六进制解析（纯 C#）。**刻意不用 <c>ColorUtility.TryParseHtmlString</c>**——
        /// 它是原生 ECall，脱离 Unity 运行时必抛 <c>SecurityException</c>，
        /// 会让整个档位目录无法在无头验证台上断言。失败返回品红（便于肉眼发现）。
        /// </summary>
        public static Color Hex(string hex)
        {
            if (!TryParseHex(hex, out Color color))
                return Color.magenta;

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
