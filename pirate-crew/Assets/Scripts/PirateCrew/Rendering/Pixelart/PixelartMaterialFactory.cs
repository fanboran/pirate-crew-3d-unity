using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化物体材质的唯一配方**（运行时版）：本路径所有"该长什么样"的逐物体参数都在这里，
    /// 编辑器侧的装配器（<c>PixelartStageKit.EnsureMaterial</c>）与运行期创建材质的各处
    /// （站面 / 海面 / 弹体兜底 / 内容转换器）**都调它**——参数各写一份的后果本仓踩过：
    /// 场景里是一套色带档数、运行期造的是另一套，出图与实机观感对不上还查不出来。
    ///
    /// 【为什么放在运行时程序集】游戏本体在**运行期**造材质（`WorldMapComposer` 的站面、
    /// `OceanRig` 的海面、弹体兜底），编辑器的 SDK 在那里用不了；编辑器侧反过来可以引用运行时，
    /// 于是配方落在这一侧，两边共用。
    ///
    /// 【着色数学不在这里】色带/描边/调色板全在走 shader 全局的那几趟里（见 <see cref="PixelartCameraRig"/>
    /// 每帧下发的光照与尺寸全局）；本类只写"这个物体该长什么样"。
    ///
    /// 【抖动图案为什么要外部传】`_DitherPattern` 是工程内的 png（`Assets/Pixelart/Textures/Dither`），
    /// 运行期没有按路径加载的入口，也没有 `Resources` 副本。默认 `_DitherStrength = 0` ⇒ 图案不参与
    /// 着色，所以运行期材质不挂它也是正确的；要做出图那种抖动对照，走编辑器侧的装配器（它会挂上）。
    /// </summary>
    public static class PixelartMaterialFactory
    {
        // ------------------------------------------------------------------
        // 共用配色（游戏本体与试点场景同一份）
        // ------------------------------------------------------------------

        /// <summary>
        /// 海图站面三档的顶面主色（= `WorldMapComposer` 旧 Terrain 材质的 `_SandColor/_GrassColor/_RockColor`，
        /// 逐值来自 GDD §10.4 中档）。运行期站面材质与试点场景的显式映射表共用这一组，
        /// 站面在"游戏本体"与"观感图"里才是同一个色。
        /// </summary>
        public static readonly Color StandSand = Hex("C4A76A");
        /// <summary>站面中档（草）。</summary>
        public static readonly Color StandGrass = Hex("4A8C4A");
        /// <summary>站面高档（岩）。</summary>
        public static readonly Color StandRock = Hex("8C7B6A");

        /// <summary>
        /// 海面色（2 档、不出描边）。**与海图试点场景的替身海面同值**——本路径的 G-buffer 没有混合，
        /// 半透明活水面（`PirateCrew/Ocean`）进不来，海面一律是"平色 + 色带"的这一档；
        /// 3D 波浪观感留给"像素海面方案"那条待定项（色带波纹 + 量化泡沫 + 岸线带）。
        /// </summary>
        public static readonly Color Sea = Hex("2E5F84");

        /// <summary>海面的色带档数（与试点替身一致：2 档足够读出水天分界，不会把平色打成条纹）。</summary>
        public const float SeaBandCount = 2f;

        // ------------------------------------------------------------------
        // 配方
        // ------------------------------------------------------------------

        /// <summary>色带档数默认值（3 档：亮/中/暗——渲染篇 §4 的起步档，试点场景全部用它）。</summary>
        public const float DefaultBandCount = 3f;

        /// <summary>
        /// 描边线宽默认值（**艺术像素**，1 = 1 个艺术像素 = 屏幕上的 `pixelScale` 像素）。
        /// 【为什么不是旧链的 2px】旧链"全分辨率渲染 → 3× 点降采"会把 1px 线欠采掉才需要 2；
        /// 本路径几何直接渲进低分辨率域，1 是实打实可见的（渲染篇 §5）。
        /// </summary>
        public const float DefaultOutlinePixels = 1f;

        /// <summary>
        /// 用本路径的物体 shader 造一个材质并套上配方。shader 被剔除/没编译时**报错返回 null**
        /// （调用方各自决定兜底，但绝不静默拿品红顶上）。
        /// </summary>
        public static Material Create(string name, Color albedo,
            float bandCount = DefaultBandCount, float outlinePixels = DefaultOutlinePixels)
        {
            Shader shader = Shader.Find(PixelartPath.ObjectShaderName);
            if (shader == null)
            {
                global::PirateCrew.Core.Log.Error("[PixelartMaterialFactory] 找不到 shader \""
                    + PixelartPath.ObjectShaderName + "\"（被剔除/编译失败？），材质 " + name + " 未创建。");
                return null;
            }

            var material = new Material(shader) { name = name };
            Configure(material, albedo, bandCount, outlinePixels);
            return material;
        }

        /// <summary>
        /// 把配方写进一个已存在的材质（编辑器侧"就地更新资产"与运行期"新建后套参数"共用）。
        ///
        /// 【逐项为什么是这个值】
        /// <list type="bullet">
        ///   <item>`_MainLightLevel` = 色带档数（越高越细腻、越低越"平涂"）；</item>
        ///   <item>`_DitherMode` / `_DitherStrength` = 0：抖动是**出图对照**用的开关，默认关，
        ///         出图脚本用 MaterialPropertyBlock 临时拨开；</item>
        ///   <item>`_NormalEdgeLevel/Threshold` = 0.5：连通域判出"单元内法线差超阈值"时给该像素加半档
        ///         （内部转折提亮，靠的是连通域那份数据）；</item>
        ///   <item>`_AAScale` = 1：连通域降档门控不缩放（v3 的 `_AAScale` 同义）；</item>
        ///   <item>`_Smoothness` 0.25 / `_Metallic` 0：高光那趟的输入，取值保守（高光一重就压过色带）；</item>
        ///   <item>`_RimLightColor` = 黑：本物体不出边缘光（改画面要有理由，验证通路才拨亮）；</item>
        ///   <item>`_SnapToPixelGrid` = 1：物体级像素吸附（v3 CommonPass 的第二层）。</item>
        /// </list>
        /// </summary>
        public static void Configure(Material material, Color albedo,
            float bandCount = DefaultBandCount, float outlinePixels = DefaultOutlinePixels)
        {
            if (material == null)
                return;

            material.SetColor("_BaseColor", albedo);
            material.SetFloat("_MainLightLevel", bandCount);
            material.SetFloat("_DitherMode", 0f);
            material.SetFloat("_DitherStrength", 0f);
            material.SetFloat("_NormalEdgeLevel", 0.5f);
            material.SetFloat("_NormalEdgeThreshold", 0.5f);
            material.SetFloat("_AAScale", 1f);
            material.SetFloat("_Smoothness", 0.25f);
            material.SetFloat("_Metallic", 0f);
            material.SetColor("_RimLightColor", Color.black);
            material.SetFloat("_OutlinePixels", outlinePixels);
            material.SetFloat("_SnapToPixelGrid", 1f);

            // 队列：几何只靠深度测试分前后（描边已改成艺术画布上的屏幕空间膨胀，与队列无关）。
            material.renderQueue = 2000;
        }

        /// <summary>
        /// 从源材质里取一个"当 albedo 用"的颜色。优先 `_BaseColor`（URP Lit / PirateSurface / 本路径），
        /// 其次 `_Color`（内置着色器），最后 `_BaseColorA`——本仓的 `PirateCrew/PirateSurface` 把三档色
        /// 写在 `_BaseColorA/B/C` 上，只看第一个会整类落空（这是实测踩过的坑）。
        /// </summary>
        public static bool TryGetAlbedo(Material source, out Color albedo)
        {
            albedo = Color.white;
            if (source == null)
                return false;

            if (source.HasProperty("_BaseColor"))
                albedo = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color"))
                albedo = source.GetColor("_Color");
            else if (source.HasProperty("_BaseColorA"))
                albedo = source.GetColor("_BaseColorA");
            else
                return false;

            return true;
        }

        /// <summary>
        /// 本路径的材质名（派生材质的命名口径）：`PixelartDerived_<源材质名>`。
        /// 【为什么要统一命名】运行期"同一源材质只派生一次"的缓存键、以及出问题时肉眼查资产，
        /// 都靠这个名字。
        /// </summary>
        public static string DerivedName(string sourceMaterialName)
        {
            return "PixelartDerived_" + sourceMaterialName;
        }

        /// <summary>sRGB hex → Color（口径同 `SceneArtPalette` / 编辑器侧的 `PixelartStageKit.Hex`）。</summary>
        public static Color Hex(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && hex[0] == '#')
                hex = hex.Substring(1);

            return new Color(
                int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                1f);
        }
    }
}
