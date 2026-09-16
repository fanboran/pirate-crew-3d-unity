using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Fx
{
    /// <summary>特效材质档（每个档对应 `Assets/Art/Materials/Fx/` 下一个 .mat）。</summary>
    public enum FxMaterial
    {
        /// <summary>爆炸外焰（橙，加法）。</summary>
        ExplosionFire = 0,

        /// <summary>爆炸核心（暖白高亮，加法）。</summary>
        ExplosionCore = 1,

        /// <summary>火花（金，硬边，加法）。</summary>
        Sparks = 2,

        /// <summary>细小火星（暖白，加法）。</summary>
        FineSparks = 3,

        /// <summary>烟雾（低饱和灰，透明）。</summary>
        Smoke = 4,

        /// <summary>木屑（烘木色，透明）。</summary>
        WoodDebris = 5,

        /// <summary>沙尘（沙地中调，透明）。</summary>
        Sand = 6,

        /// <summary>水花飞沫（冷白，加法）。</summary>
        WaterSplash = 7,

        /// <summary>水沫（近白，透明）。</summary>
        WaterFoam = 8,

        /// <summary>涟漪环（浅蓝，加法）。</summary>
        Ripple = 9,

        /// <summary>冲击波环（暖白，加法）。</summary>
        Shockwave = 10,

        /// <summary>地面回合光环（白，颜色由 MPB 逐单位染，加法）。</summary>
        TurnMarker = 11,

        /// <summary>伤害数字（贴图由运行时逐串生成，透明）。</summary>
        DamageNumber = 12,

        /// <summary>爆炸类弹体拖尾（暖橙，加法）。</summary>
        TrailExplosive = 13,

        /// <summary>普通弹体拖尾（选中青，加法）。</summary>
        TrailDefault = 14,

        /// <summary>四芒星闪（星屑，加法）。</summary>
        Star4 = 15,
    }

    /// <summary>材质档的静态规格（编辑器生成 .mat 与运行时创建材质共用同一张表，保证两边一致）。</summary>
    public struct FxMaterialSpec
    {
        /// <summary>资产名（`Assets/Art/Materials/Fx/<Name>.mat`）。</summary>
        public string Name;

        /// <summary>true = 加法混合 shader；false = 透明混合 shader。</summary>
        public bool Additive;

        /// <summary>主贴图种类（伤害数字档为占位，运行时替换）。</summary>
        public FxTextureKind Texture;

        /// <summary>叠加色（sRGB，Art Bible 调色板出处见 FxRules）。</summary>
        public Color Tint;

        /// <summary>发光强度（加法档 >1 才会过曝出白心）。</summary>
        public float Intensity;
    }

    /// <summary>
    /// 特效材质缓存（运行时）。
    ///
    /// 【为什么运行时自己建材质】装配根（Battle.unity）不在本 agent 的白名单里，无法把
    /// `Assets/Art/Materials/Fx/*.mat` 序列化进场景；<c>Assets/Art/...</c> 也不是 Resources 目录，
    /// 运行时无法按路径加载。因此运行时用 <see cref="Shader.Find"/> 找本工程的 FX shader
    /// （资源目录里的 .mat 只作美术可调/资产可见性与"把 shader 带进构建"的载体），按同一张
    /// <see cref="Specs"/> 表建内存材质并缓存 —— 规格唯一来源仍是这张表，两边不会漂移。
    ///
    /// 【容错】找不到 FX shader 时回落到 URP 官方 Particles/Unlit，再退到 Sprites/Default、
    /// Unlit/Transparent；全找不到则返回 null，调用方（FxParticles/FxSpriteFx）静默不出特效不报错。
    ///
    /// 【实例化边界】本类触碰 <c>Shader</c>/<c>Material</c>（ECall），只能在 Unity 运行时/编辑器里用，
    /// 不进无头断言；规格表 <see cref="Specs"/> 本身是纯数据，可对表做无头断言。
    /// </summary>
    public static class FxMaterials
    {
        /// <summary>本工程 shader 名（与 Assets/Art/Shaders/Fx/*.shader 的 Shader "..." 一致）。</summary>
        public const string AdditiveShaderName = "PirateCrew/Fx/Additive";

        /// <summary>透明混合 shader 名。</summary>
        public const string AlphaShaderName = "PirateCrew/Fx/Alpha";

        static readonly FxMaterialSpec[] Specs =
        {
            // ---- 爆炸 ----
            // 亮度三轮压制（r4/r5 后续各一轮，r6 一轮，r7 再收一档）：
            //   r4/r5 已把 core/fire 的 Tint ×0.60 / ×0.70、Intensity 同步乘同系数；
            //   r6 在此基础上**再乘 0.60**（只乘 Intensity，不再压 Tint）；
            //   r7 再乘一档（core ×0.70 / fire ×0.80），最终值：
            //     core：1.70 × 0.60 × 0.60 × 0.70 = **0.4284 ≈ 0.43**
            //     fire：1.15 × 0.70 × 0.60 × 0.80 = **0.3864 ≈ 0.39**
            //   动机（r6 复验）：[Blocker] 爆炸过曝——火球框内 V>0.99 占 68%、全图 5.9%。
            //   评审定位火球框里的白大部分是**水面金色冲刷**（爆炸机位朝太阳方位，宽瓣+sheen 把整片
            //   水洗成亮金）——r7 把 PirateWater 的镜射四权重下调后这部分会大幅缓解（见 PirateWater.shader
            //   文件头【r7 收口】）；此外**再压爆炸粒子本体一档**做双保险。
            //   判据：全图 V>0.99（max 通道>252）≤2%、火球框内 ≤25%、
            //   橙壳（r−b>60 且 r>g>b）在火球盘内 ≥2000px（即压白而不灭火球橙）。
            // 系数与 FxAssetBuilder 的烘焙侧（Fx_ExplosionCore/Fire .mat）保持同源：那边读本表的
            // spec.Intensity 后再乘它自己的亮度系数，故本表改值会自动传导到烘焙资产。
            new FxMaterialSpec { Name = "Fx_ExplosionFire",  Additive = true,  Texture = FxTextureKind.SoftCircle, Tint = ScaleRgb(FxRules.ExplosionFireColor(), 0.70f), Intensity = 0.3864f },
            new FxMaterialSpec { Name = "Fx_ExplosionCore",  Additive = true,  Texture = FxTextureKind.SoftCircle, Tint = ScaleRgb(FxRules.ExplosionCoreColor(), 0.60f), Intensity = 0.4284f },
            new FxMaterialSpec { Name = "Fx_Sparks",         Additive = true,  Texture = FxTextureKind.Spark,      Tint = FxRules.SparkColor(),         Intensity = 1.80f },
            new FxMaterialSpec { Name = "Fx_FineSparks",     Additive = true,  Texture = FxTextureKind.FineSpark,  Tint = FxRules.ExplosionCoreColor(), Intensity = 1.90f },
            new FxMaterialSpec { Name = "Fx_Star4",          Additive = true,  Texture = FxTextureKind.Star4,      Tint = FxRules.ExplosionCoreColor(), Intensity = 1.60f },
            // ---- 碎屑 / 烟 ----
            // r6：烟 Tint 由 Color.white 改烟灰——贴图本身明度 ≈0.62，乘白后是"浅灰白团"，
            // 在提亮后的场景里就是复验看到的"白雾"；改乘 SmokeColor（#8C8A86）后 ≈0.34 明度，读作暗烟。
            new FxMaterialSpec { Name = "Fx_Smoke",          Additive = false, Texture = FxTextureKind.Smoke,      Tint = FxRules.SmokeColor(),         Intensity = 1.00f },
            new FxMaterialSpec { Name = "Fx_WoodDebris",     Additive = false, Texture = FxTextureKind.WoodShard,  Tint = Color.white,                  Intensity = 1.00f },
            new FxMaterialSpec { Name = "Fx_Sand",           Additive = false, Texture = FxTextureKind.SoftCircle, Tint = FxRules.SandColor(),          Intensity = 1.00f },
            // ---- 水 ----
            new FxMaterialSpec { Name = "Fx_WaterSplash",    Additive = true,  Texture = FxTextureKind.Droplet,    Tint = FxRules.SplashDropletColor(), Intensity = 1.30f },
            new FxMaterialSpec { Name = "Fx_WaterFoam",      Additive = false, Texture = FxTextureKind.SoftCircle, Tint = FxRules.SplashFoamColor(),    Intensity = 1.00f },
            new FxMaterialSpec { Name = "Fx_Ripple",         Additive = true,  Texture = FxTextureKind.Ring,       Tint = FxRules.RippleColor(),        Intensity = 1.00f },
            // ---- 环 / 标记 ----
            new FxMaterialSpec { Name = "Fx_Shockwave",      Additive = true,  Texture = FxTextureKind.Ring,       Tint = FxRules.ShockwaveColor(),     Intensity = 1.20f },
            new FxMaterialSpec { Name = "Fx_TurnMarker",     Additive = true,  Texture = FxTextureKind.Ring,       Tint = Color.white,                  Intensity = 1.00f },
            // ---- 数字 / 拖尾 ----
            new FxMaterialSpec { Name = "Fx_DamageNumber",   Additive = false, Texture = FxTextureKind.Spark,      Tint = Color.white,                  Intensity = 1.00f },
            new FxMaterialSpec { Name = "Fx_TrailExplosive", Additive = true,  Texture = FxTextureKind.SoftCircle, Tint = FxRules.TrailColor(WeaponId.CherryBomb), Intensity = 1.40f },
            new FxMaterialSpec { Name = "Fx_TrailDefault",   Additive = true,  Texture = FxTextureKind.SoftCircle, Tint = FxRules.TrailColor(WeaponId.Boulder),    Intensity = 1.40f },
        };

        /// <summary>材质档总数。</summary>
        public static int Count => Specs.Length;

        /// <summary>只缩放 RGB 保持 alpha=1（Color 的 * 运算符会连 alpha 一起乘，additive 材质不能动 alpha）。</summary>
        static Color ScaleRgb(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        /// <summary>取某档的静态规格。</summary>
        public static FxMaterialSpec SpecOf(FxMaterial material)
        {
            int i = (int)material;
            if (i < 0 || i >= Specs.Length)
                return default;
            return Specs[i];
        }

        // ---- 运行时材质缓存 ----

        static readonly Material[] Cache = new Material[Specs.Length];
        static Shader _additiveShader;
        static Shader _alphaShader;
        static bool _warnedShaderFallback;

        /// <summary>取（或创建）材质。无可用 shader 时返回 null。</summary>
        public static Material Get(FxMaterial material)
        {
            int i = (int)material;
            if (i < 0 || i >= Specs.Length)
                return null;
            if (Cache[i] != null)
                return Cache[i];

            FxMaterialSpec spec = Specs[i];
            Shader shader = GetShader(spec.Additive);
            if (shader == null)
                return null;

            var mat = new Material(shader) { name = spec.Name, hideFlags = HideFlags.DontSave };
            ApplyTexture(mat, FxTextures.Get(spec.Texture));
            ApplyTint(mat, spec.Tint);
            SetFloatIfPresent(mat, "_Intensity", spec.Intensity);

            Cache[i] = mat;
            return mat;
        }

        /// <summary>取 shader（优先本工程 FX shader，失败回落 URP 官方 / 内置，并告警一次）。</summary>
        public static Shader GetShader(bool additive)
        {
            if (additive && _additiveShader != null)
                return _additiveShader;
            if (!additive && _alphaShader != null)
                return _alphaShader;

            Shader primary = Shader.Find(additive ? AdditiveShaderName : AlphaShaderName);
            if (primary != null)
            {
                if (additive) _additiveShader = primary; else _alphaShader = primary;
                return primary;
            }

            // 回落链：URP 粒子 unlit → 内置 Sprites → 内置 Unlit。
            Shader fallback = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                              ?? Shader.Find("Sprites/Default")
                              ?? Shader.Find("Unlit/Transparent");
            if (!_warnedShaderFallback)
            {
                _warnedShaderFallback = true;
                global::PirateCrew.Core.Log.Warn("[FxMaterials] 未找到 " + (additive ? AdditiveShaderName : AlphaShaderName)
                    + "，回落为 " + (fallback != null ? fallback.name : "null")
                    + "。请在编辑器执行 PirateCrew/Fx/生成特效贴图与材质，并确认 FX shader 编译无错。");
            }

            if (additive) _additiveShader = fallback; else _alphaShader = fallback;
            return fallback;
        }

        /// <summary>把贴图写进材质（兼容本工程 shader 的 _BaseMap 与回落 shader 的 _MainTex）。</summary>
        public static void ApplyTexture(Material material, Texture texture)
        {
            if (material == null)
                return;
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
        }

        /// <summary>把颜色写进材质（兼容 _Color 与 _BaseColor）。</summary>
        public static void ApplyTint(Material material, Color tint)
        {
            if (material == null)
                return;
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tint);
        }

        static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property))
                material.SetFloat(property, value);
        }
    }
}
