using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步海水换装的**纯逻辑**（无 MonoBehaviour，无头可测）：shader 资源路径、
    /// 材质槽分流（浅水/深水/玻璃）、换装判据、颜色与 alpha 解析、水波幅度呼吸公式。
    /// <see cref="CloudSeaWater"/> 只做 Find/换材质/每帧推属性的胶水，决策全在本类。
    ///
    /// 【为什么拆出来】本仓铁律：核心逻辑刻意写成纯 C# 静态类以便无头测试
    /// （external/harness 边界：MonoBehaviour 胶水层走 batchmode 收口）。分流与取色是
    /// 字符串/数值契约——拼错即静默失效，值得独立成可断言的纯函数。
    /// 字面量（对象名/材质名/shader 名）只在 <see cref="CloudSeaPath"/> 一处。
    /// </summary>
    public static class CloudSeaWaterMaterial
    {
        /// <summary>
        /// shader 的 Resources 路径。【注意】文件名是 CloudSeaWater.shader、内部名
        /// （WaterShaderName）末段是 "Water"——与 PixelartShaders.Load 的
        /// "Resources 文件名 = 内部名末段"约定不同，组件自取不走该帮助类（走必落空
        /// 并响无谓的回退警告）。
        /// </summary>
        public const string ShaderResourcePath = "PixelartShaders/CloudSeaWater";

        /// <summary>材质槽换装后落哪一档（决定兜底色与兜底 alpha）。</summary>
        public enum SlotKind
        {
            /// <summary>与海水无关的槽（不换）。</summary>
            None,
            /// <summary>Cloud_Water：水面/水洼。</summary>
            Shallow,
            /// <summary>Cloud_WaterDeep：水壁/深水。</summary>
            Deep,
            /// <summary>Cloud_Glass：玻璃。</summary>
            Glass,
        }

        /// <summary>按源材质名分流（名字契约只在 CloudSeaPath）。</summary>
        public static SlotKind KindOf(string materialName)
        {
            switch (materialName)
            {
                case CloudSeaPath.WaterMaterialName:
                    return SlotKind.Shallow;
                case CloudSeaPath.WaterDeepMaterialName:
                    return SlotKind.Deep;
                case CloudSeaPath.GlassMaterialName:
                    return SlotKind.Glass;
                default:
                    return SlotKind.None;
            }
        }

        /// <summary>
        /// 槽位的**有效档**：非水材质槽只有在水面对象（按名命中 Cloud_Sea_Surface）上
        /// 才视为浅水——该对象整体就是水面，材质名契约断了（FBX 内嵌名漂移）时按对象名
        /// 兜住，不让整片海静默消失。
        /// </summary>
        public static SlotKind EffectiveKind(bool isSurfaceObject, SlotKind kind)
        {
            if (kind != SlotKind.None)
                return kind;
            return isSurfaceObject ? SlotKind.Shallow : SlotKind.None;
        }

        /// <summary>
        /// 该槽是否换装：水面对象恒换、玻璃恒换（玻璃在云台窗板上，不属水洼）；
        /// 其余浅/深槽（水壁/水洼/海床水）受 <paramref name="enablePuddles"/> 总闸。
        /// </summary>
        public static bool ShouldSwap(bool isSurfaceObject, SlotKind kind, bool enablePuddles)
        {
            SlotKind effective = EffectiveKind(isSurfaceObject, kind);
            if (effective == SlotKind.None)
                return false;
            if (isSurfaceObject || effective == SlotKind.Glass)
                return true;
            return enablePuddles;
        }

        /// <summary>
        /// alpha 兜底表（与 CloudSeaAssetBaker 的透明 alpha 表同源口径）。FBX 内嵌色
        /// alpha 恒 1：源 alpha 读不到、为 0 或实心（≥ <see cref="SolidAlphaThreshold"/>）
        /// 时按档取值——海水/水洼/玻璃永不定格成不透明或全透明。
        /// </summary>
        public static float AlphaFallback(SlotKind kind)
        {
            switch (kind)
            {
                case SlotKind.Shallow:
                    return 0.60f;
                case SlotKind.Deep:
                    return 0.60f;
                case SlotKind.Glass:
                    return 0.25f;
                default:
                    return 1f;
            }
        }

        /// <summary>源 alpha 视为"实心"的阈值（≥ 此值走兜底；0~1 之间的小数 alpha 一律尊重源）。</summary>
        public const float SolidAlphaThreshold = 0.999f;

        /// <summary>
        /// 解析换装色：rgb 优先源材质（缺则按档落 shallow/deep 字段色，玻璃缺色落浅水色）；
        /// alpha 在 (0, 实心阈值) 内尊重源，否则按档兜底。
        /// </summary>
        public static Color ResolveColor(SlotKind effectiveKind, bool hasSourceColor, Color sourceColor,
            Color shallowColor, Color deepColor)
        {
            Color resolved = hasSourceColor
                ? sourceColor
                : (effectiveKind == SlotKind.Deep ? deepColor : shallowColor);

            float alpha = resolved.a;
            if (alpha <= 0f || alpha >= SolidAlphaThreshold)
                alpha = AlphaFallback(effectiveKind);
            resolved.a = alpha;
            return resolved;
        }

        /// <summary>幅度呼吸深度（±20% → 0.8–1.2×）与速率（rad/s，周期 ≈7s）。</summary>
        public const float WaveBreathDepth = 0.2f;
        public const float WaveBreathRate = 0.9f;

        /// <summary>
        /// 水波幅度的慢速呼吸。水波相位走 shader 内 _Time.y × _WaveSpeed（零每帧属性写），
        /// 组件每帧只按本式推 _WaveStrength——幅度有起伏感，相位不占组件状态。
        /// </summary>
        public static float WaveStrengthModulated(float waveStrength, float time)
        {
            return waveStrength * (1f + WaveBreathDepth * Mathf.Sin(time * WaveBreathRate));
        }
    }
}
