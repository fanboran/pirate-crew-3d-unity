using UnityEngine;
using PirateCrew.PirateCrew.SceneArt;

namespace PirateCrew.PirateCrew.Ambient
{
    /// <summary>
    /// 一档天空盒预设（纯 C# 值类型，无头可测）。
    ///
    /// 【与 <see cref="AmbientLightingPreset"/> 的分工】后者是"主光 / 雾 / 环境光"的运行时档位；
    /// 本结构是**天空盒本体**（渐变三色 + 太阳盘）的参数。两者同档同源，但用途不同：
    /// 前者写 <c>RenderSettings</c>，后者写天空盒材质属性。
    /// </summary>
    public readonly struct AmbientSkyboxPreset
    {
        /// <summary>档位。</summary>
        public readonly AmbientTimeOfDay TimeOfDay;

        /// <summary>对应 <see cref="SkyTierCatalog"/> 的档号（1/2/3，原版 <c>skyColour</c> 口径）。</summary>
        public readonly int SkyTierIndex;

        /// <summary>天顶色（上半球，<c>dir.y</c> 趋 +1）。</summary>
        public readonly Color ZenithColor;

        /// <summary>地平线色（<c>dir.y</c> ≈ 0）。**契约：逐值等于该档雾色**，见目录类头。</summary>
        public readonly Color HorizonColor;

        /// <summary>地面回照色（下半球，<c>dir.y</c> 趋 -1）。</summary>
        public readonly Color GroundColor;

        /// <summary>地平线过渡带宽（单位 <c>|dir.y|</c>：色带在 ±该值之间完成过渡）。</summary>
        public readonly float HorizonBlend;

        /// <summary>地面过渡带宽（单位 <c>|dir.y|</c>）。</summary>
        public readonly float GroundBlend;

        /// <summary>天顶过渡曲线指数（1 = 线性，越大天顶色越"贴顶"）。</summary>
        public readonly float GradientPower;

        /// <summary>曝光（整体亮度倍率）。</summary>
        public readonly float Exposure;

        /// <summary>太阳盘色。</summary>
        public readonly Color SunDiskColor;

        /// <summary>太阳盘角半径（弧度）。**0 = 关闭太阳盘**。</summary>
        public readonly float SunDiskSize;

        /// <summary>太阳盘边缘柔度（弧度，越大越"发光"而非"硬圆盘"）。</summary>
        public readonly float SunDiskSoftness;

        /// <summary>太阳盘强度（&gt; 1 的部分留给 Bloom 溢出，制造阳光感）。</summary>
        public readonly float SunDiskIntensity;

        /// <summary>本档是否画太阳盘。</summary>
        public bool HasSunDisk => SunDiskSize > 0f && SunDiskIntensity > 0f;

        public AmbientSkyboxPreset(AmbientTimeOfDay timeOfDay, int skyTierIndex,
            Color zenithColor, Color horizonColor, Color groundColor,
            float horizonBlend, float groundBlend, float gradientPower, float exposure,
            Color sunDiskColor, float sunDiskSize, float sunDiskSoftness, float sunDiskIntensity)
        {
            TimeOfDay = timeOfDay;
            SkyTierIndex = skyTierIndex;
            ZenithColor = zenithColor;
            HorizonColor = horizonColor;
            GroundColor = groundColor;
            HorizonBlend = horizonBlend;
            GroundBlend = groundBlend;
            GradientPower = gradientPower;
            Exposure = exposure;
            SunDiskColor = sunDiskColor;
            SunDiskSize = sunDiskSize;
            SunDiskSoftness = sunDiskSoftness;
            SunDiskIntensity = sunDiskIntensity;
        }
    }

    /// <summary>环境光来源（视觉遗留 #6 的开关）。</summary>
    public enum AmbientSkySource
    {
        /// <summary>三色 Trilight（**现役基准**：<c>BattleSceneLighting.ApplyThreePointAmbient</c> 的三灯分层）。</summary>
        Trilight = 0,

        /// <summary>天空盒驱动（环境光 SH 由天空盒卷积而来，有方向与色彩变化）。</summary>
        Skybox = 1,
    }

    /// <summary>
    /// 三档天空盒预设目录（纯 C#）。**数值全部为【提案/待定】**——本目录是视觉审计
    /// 遗留 #6「环境光从 Trilight 平铺升级为天空盒驱动」的参数方案，须实拍验收后转正。
    ///
    /// 【接线开关：<see cref="DefaultAmbientSource"/>】它是本功能唯一的开关，**默认 <see cref="AmbientSkySource.Trilight"/>**
    /// ——即默认不改变现役画面基准。理由：任务书的前置门要求"视觉批次 A–F 实拍转正后"才动全局参数
    /// （环境光一换全场景观感基准就变，两轮调参会互相覆盖）；把开关做成常量而不是场景里的序列化字段，
    /// 是为了让"翻转"成为**一处改动**（序列化字段会被旧值钉住，改代码默认值对已存场景无效——这是本项目
    /// 踩过的坑）。翻转步骤见 `docs/环境光天空盒化-预研与接线清单.md` §6。
    ///
    /// 【为什么色值不另起一套】三色直接取既有单一事实源，避免"同一档天空两处色值"：
    ///   · 天顶色   = <see cref="SkyTierCatalog"/> 该档的 <c>ZenithHex</c>
    ///     （与 <see cref="AmbientTimeOfDayCatalog"/> 的 <c>SkyTint</c> 本就同源）；
    ///   · 地面回照 = 同档的 <c>GroundBounceHex</c>；
    ///   · 太阳盘色 = 同档的 <c>SunHex</c>。
    /// 只有**地平线色**取自 <see cref="AmbientTimeOfDayCatalog"/> 的该档雾色，理由见下条。
    ///
    /// 【地平线色 = 雾色（本目录最硬的一条契约，有测试钉住）】
    /// <see cref="AmbientLightingPreset.FogColor"/> 的定义就是"该档天空地平线色，保证大气透视而非盖灰"
    /// （见 AmbientTimeOfDayCatalog 类头）。天空盒的地平线若与雾色不同，远景物体在雾里淡出的终点
    /// 会与它身后的天空对不上，出现"远岛贴在另一块天幕上"的接缝——故二者必须逐值相等。
    ///
    /// 【下半球为什么是暖的（换天空盒环境光时最容易搞错的一点）】
    /// 切 <c>RenderSettings.ambientMode = Skybox</c> 后，环境光的 SH **完全由天空盒卷积而来**
    /// （不再看 <c>ambientSky/Equator/GroundColor</c>），下半球约占环境光贡献的一半。
    /// 现状 Trilight 的下半球是暖沙色 <c>#C9A268</c>（<c>BattleSceneLighting.ApplyThreePointAmbient</c>
    /// 的"三灯分层"暖反弹）；若把天空盒下半球也画成天蓝，切档后整场会失去暖反弹、明显转冷。
    /// 故地面色取 <see cref="SkyTierCatalog"/> 的 <c>GroundBounceHex</c>（暖沙族），
    /// 用 <see cref="AmbientSkyboxPreset.GroundBlend"/> 控制暖色"下沉"多快：
    /// 过渡带内是接近地平线色的浅暖，越往下越暖。
    ///
    /// 【太阳盘与主光方位的一致性】太阳盘位置由 shader 读 URP 全局 <c>_MainLightPosition</c> 决定
    /// （与 <c>AmbientDirector</c> 写主光姿态、以及 <c>PirateOcean</c> 的太阳光路方位门同一来源），
    /// 因此**不存在"天上一轮日、海上两条光路"的错位**——不需要额外接线或校验参数。
    ///
    /// 【已知边界】阴云档不画太阳盘（乌云蔽日）；三档的曝光与太阳盘尺寸是【提案/待定】，
    /// 实拍后按"正午不过曝、黄昏日盘可辨、阴云不出现方向性亮斑"三条判据调。
    /// </summary>
    public static class AmbientSkyboxCatalog
    {
        // ------------------------------------------------------------------
        // 接线开关与 shader / 属性名（运行时与 Editor 生成器共用，禁止散落魔法字符串）
        // ------------------------------------------------------------------

        /// <summary>
        /// **环境光来源开关**（本功能唯一的开关）。默认 <see cref="AmbientSkySource.Trilight"/>：
        /// 不改变现役画面基准（A–F 实拍转正前不动全局参数，任务书前置门）。
        /// </summary>
        public const AmbientSkySource DefaultAmbientSource = AmbientSkySource.Trilight;

        /// <summary>是否启用天空盒驱动环境光（<c>AmbientDirector.ApplyPreset</c> 读它）。</summary>
        public static bool SkyboxAmbientEnabled => DefaultAmbientSource == AmbientSkySource.Skybox;

        /// <summary>渐变天空盒 shader 名（与 PirateGradientSky.shader 的 Shader 声明一致）。</summary>
        public const string SkyShaderName = "PirateCrew/Skybox/PirateGradientSky";

        public const string ZenithColorProperty      = "_SkyZenithColor";
        public const string HorizonColorProperty     = "_SkyHorizonColor";
        public const string GroundColorProperty      = "_SkyGroundColor";
        public const string HorizonBlendProperty     = "_SkyHorizonBlend";
        public const string GroundBlendProperty      = "_SkyGroundBlend";
        public const string GradientPowerProperty    = "_SkyGradientPower";
        public const string ExposureProperty         = "_SkyExposure";
        public const string SunDiskColorProperty     = "_SkySunDiskColor";
        public const string SunDiskSizeProperty      = "_SkySunDiskSize";
        public const string SunDiskSoftnessProperty  = "_SkySunDiskSoftness";
        public const string SunDiskIntensityProperty = "_SkySunDiskIntensity";

        /// <summary>三档档位（顺序即 <c>skyboxMaterials[]</c> 的下标顺序，与枚举值一致）。</summary>
        public static readonly AmbientTimeOfDay[] Tiers =
        {
            AmbientTimeOfDay.Noon,
            AmbientTimeOfDay.Dusk,
            AmbientTimeOfDay.Overcast,
        };

        /// <summary>
        /// 把一档预设写进天空盒材质（全部属性逐值覆盖），返回**实际写入的属性个数**。
        ///
        /// 【为什么写在运行时目录里】Editor 的生成器（<c>SkyAssetBuilder</c>）与运行时程序化建材质
        /// （<c>AmbientDirector.ResolveSkyboxMaterial</c> 的兜底路径）必须写同一组属性，
        /// 两处各写一份就是"改一处忘另一处"的温床；本方法 + 上面的属性名常量是它们共同的唯一出口。
        /// 写的是 sRGB 原值——Unity 对普通 Color 属性自动做 sRGB→Linear 转换（口径见
        /// <c>Assets/Editor/BattleSceneLighting.cs</c> 类头「色空间」段）。
        ///
        /// 【返回值是漂移探测器】<c>Material.HasProperty</c> 查不到就跳过（shader 属性改名时不会崩），
        /// 但"静默跳过"会让预设悄悄失效——调用方拿返回值与 <see cref="MaterialPropertyCount"/> 比对即可发现。
        /// </summary>
        public static int ApplyPreset(Material material, AmbientTimeOfDay timeOfDay)
        {
            return ApplyPreset(material, For(timeOfDay));
        }

        /// <summary>把一档预设写进材质（显式传预设，避免重复取目录）；返回实际写入的属性个数。</summary>
        public static int ApplyPreset(Material material, AmbientSkyboxPreset preset)
        {
            if (material == null)
                return 0;

            int written = 0;
            written += SetColor(material, ZenithColorProperty, preset.ZenithColor);
            written += SetColor(material, HorizonColorProperty, preset.HorizonColor);
            written += SetColor(material, GroundColorProperty, preset.GroundColor);
            written += SetFloat(material, HorizonBlendProperty, preset.HorizonBlend);
            written += SetFloat(material, GroundBlendProperty, preset.GroundBlend);
            written += SetFloat(material, GradientPowerProperty, preset.GradientPower);
            written += SetFloat(material, ExposureProperty, preset.Exposure);
            written += SetColor(material, SunDiskColorProperty, preset.SunDiskColor);
            written += SetFloat(material, SunDiskSizeProperty, preset.SunDiskSize);
            written += SetFloat(material, SunDiskSoftnessProperty, preset.SunDiskSoftness);
            written += SetFloat(material, SunDiskIntensityProperty, preset.SunDiskIntensity);
            return written;
        }

        /// <summary>预设涉及的材质属性个数（= <see cref="ApplyPreset(Material, AmbientSkyboxPreset)"/> 的正常返回值）。</summary>
        public const int MaterialPropertyCount = 11;

        static int SetColor(Material material, string property, Color value)
        {
            if (!material.HasProperty(property))
                return 0;

            material.SetColor(property, value);
            return 1;
        }

        static int SetFloat(Material material, string property, float value)
        {
            if (!material.HasProperty(property))
                return 0;

            material.SetFloat(property, value);
            return 1;
        }

        // ------------------------------------------------------------------
        // 档位 → SkyTierCatalog 档号（1/2/3）
        // ------------------------------------------------------------------

        /// <summary>
        /// 档位对应的原版天空档号（<see cref="SkyTierCatalog.All"/> 的索引 = 档号 - 1）。
        /// 映射理由：<see cref="AmbientTimeOfDay"/> 的三档本就是照原版 <c>skyColour</c> 1/2/3 分的
        /// （正午 = 1-5 关、黄昏 = 6-10 关、阴云 = 11-15 关）。
        /// </summary>
        public static int SkyTierIndex(AmbientTimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case AmbientTimeOfDay.Dusk: return 2;
                case AmbientTimeOfDay.Overcast: return 3;
                default: return 1;
            }
        }

        /// <summary>取该档在 <see cref="SkyTierCatalog"/> 里的色值表。</summary>
        public static SkyTierCatalog.SkyTier Tier(AmbientTimeOfDay timeOfDay)
        {
            return SkyTierCatalog.All[SkyTierIndex(timeOfDay) - 1];
        }

        /// <summary>
        /// 取档位预设。
        ///
        /// 【各档数值与理由（全部【提案/待定】）】
        ///   过渡带宽：正午 0.14 / 黄昏 0.20 / 阴云 0.30 —— 云层越厚，地平线越糊（单调递增，测试钉住）。
        ///   天顶曲线：正午 2.0 / 黄昏 1.8 / 阴云 1.3 —— 阴云接近均匀灰，天顶不该有干净蓝。
        ///   曝光：正午 1.10（= 现役 BattleSky 的 _Exposure，保证换盒后正午亮度不跳变）
        ///         / 黄昏 1.05 / 阴云 0.95（单调递减，测试钉住）。
        ///   太阳盘：正午 角半径 0.045 rad（≈2.6°，远大于真实太阳 0.27° —— 风格化"大日"，
        ///         与现有 Skybox/Procedural 的 _SunSize 0.065 同量级）；黄昏 0.065 rad 更大更暖
        ///         （低空散射的视觉惯例）；阴云关闭。
        /// </summary>
        public static AmbientSkyboxPreset For(AmbientTimeOfDay timeOfDay)
        {
            SkyTierCatalog.SkyTier tier = Tier(timeOfDay);

            switch (timeOfDay)
            {
                case AmbientTimeOfDay.Dusk:
                    return new AmbientSkyboxPreset(
                        AmbientTimeOfDay.Dusk, tier.Index,
                        AmbientTimeOfDayCatalog.Hex(tier.ZenithHex),
                        AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Dusk).FogColor,
                        AmbientTimeOfDayCatalog.Hex(tier.GroundBounceHex),
                        horizonBlend: 0.20f, groundBlend: 0.40f, gradientPower: 1.8f, exposure: 1.05f,
                        AmbientTimeOfDayCatalog.Hex(tier.SunHex),
                        sunDiskSize: 0.065f, sunDiskSoftness: 0.060f, sunDiskIntensity: 2.6f);

                case AmbientTimeOfDay.Overcast:
                    return new AmbientSkyboxPreset(
                        AmbientTimeOfDay.Overcast, tier.Index,
                        AmbientTimeOfDayCatalog.Hex(tier.ZenithHex),
                        AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Overcast).FogColor,
                        AmbientTimeOfDayCatalog.Hex(tier.GroundBounceHex),
                        horizonBlend: 0.30f, groundBlend: 0.50f, gradientPower: 1.3f, exposure: 0.95f,
                        AmbientTimeOfDayCatalog.Hex(tier.SunHex),
                        sunDiskSize: 0f, sunDiskSoftness: 0f, sunDiskIntensity: 0f);

                default:
                    return new AmbientSkyboxPreset(
                        AmbientTimeOfDay.Noon, tier.Index,
                        AmbientTimeOfDayCatalog.Hex(tier.ZenithHex),
                        AmbientTimeOfDayCatalog.For(AmbientTimeOfDay.Noon).FogColor,
                        AmbientTimeOfDayCatalog.Hex(tier.GroundBounceHex),
                        horizonBlend: 0.14f, groundBlend: 0.34f, gradientPower: 2.0f, exposure: 1.10f,
                        AmbientTimeOfDayCatalog.Hex(tier.SunHex),
                        sunDiskSize: 0.045f, sunDiskSoftness: 0.035f, sunDiskIntensity: 2.0f);
            }
        }
    }
}
