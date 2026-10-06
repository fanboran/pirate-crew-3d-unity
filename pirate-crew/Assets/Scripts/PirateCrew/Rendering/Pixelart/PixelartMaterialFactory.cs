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
    /// 运行期没有按路径加载的入口，也没有 `Resources` 副本，本类 <c>Create</c> 挂不上它。
    /// 运行期要抖动靠**继承**：`PixelartContentConverter.InheritPerMaterialTuning` 派生时从源材质
    /// （编辑器装配、图案已挂）拷贝图案与档数——不继承则抖动项退化成硬边色带，战斗场景与
    /// 试点场景观感分裂（2026-10-06 修复）；编辑器侧直接装配的材质由装配路径自行挂上。
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

        /// <summary>
        /// 色带档数默认值 = **4**（创始人 2026-09-29 裁决，由调试场定档带入正式装配；前值 3）。
        /// 大平面的刻意降档（地面 2 档 / 海面 2 档）由装配器显式传参覆盖，不受本默认影响。
        /// </summary>
        public const float DefaultBandCount = 4f;

        /// <summary>
        /// 抖动默认 = **Bayer 4×4、幅度 0.21**（创始人 2026-09-30 定值：撕边质感保留、压住
        /// 「整个亮面棋盘格」的观感；同日曾定 0.5，但旧施加式下 0.5 实际超过量化边界间距的一半，
        /// 相邻翻转带互相重叠、全表面处处翻档——幅度单位已重定标为「强度 1 = ±0.5 个量化边界
        /// 间距」，见 `PixelartShading.shader` 的 DiffuseShading）。分离观感若再现，
        /// 逐关/逐材质走面板与装配器参数微调，不再动全仓默认。
        /// </summary>
        public const float DefaultDitherStrength = 0.21f;

        /// <summary>
        /// 法线边加成档（**负 = 压暗**）。口径照参考库的 `_EdgeLevel` 默认值与演示非金属件 = -1：
        /// 面转折处**压暗一档**成内墨线（v3 的 `DiffuseShading` 里这一项就是 `ndotl += singleLevel * 本值`，
        /// 负值即减一档）。
        /// ⚠ shader 属性必须声明成 `Range(-1, 1)`——`Range(0, 1)` 会把 -1 夹成 0，本项静默失效。
        /// </summary>
        public const float NormalEdgeLevel = -1f;

        /// <summary>
        /// 法线边阈值（连通域 `Result.b` = 单元内最大法线差 = 两个单位法线之差的模长，值域 0..2）。
        /// 口径照参考库默认值与演示非金属件 = 1.0：`diff > 1.0` ⇔ 面夹角 &gt; 60°，
        /// 于是内墨线只在**硬转折**上出现（参考库演示里这一项对多数材质等同关闭）。
        /// </summary>
        public const float NormalEdgeThreshold = 1f;

        /// <summary>
        /// 描边线宽默认值（**艺术像素**，1 = 1 个艺术像素 = 屏幕上的 `pixelScale` 像素）。
        /// 【为什么不是旧链的 2px】旧链"全分辨率渲染 → 3× 点降采"会把 1px 线欠采掉才需要 2；
        /// 本路径几何直接渲进低分辨率域，1 是实打实可见的（渲染篇 §5）。
        /// </summary>
        public const float DefaultOutlinePixels = 1f;

        /// <summary>
        /// 用本路径的物体 shader 造一个材质并套上配方。shader 摸不到时**报错返回 null**
        /// （调用方各自决定兜底，但绝不静默拿品红顶上）。shader 走 <see cref="PixelartShaders"/>
        /// 取用口：Resources 持有为主路径、按名回退兜底，丢失必有响亮报错（不静默）。
        /// </summary>
        public static Material Create(string name, Color albedo,
            float bandCount = DefaultBandCount, float outlinePixels = DefaultOutlinePixels,
            float aaScale = 1f, float priority = 1f)
        {
            Shader shader = PixelartShaders.Load(PixelartPath.ObjectShaderName);
            if (shader == null)
            {
                global::PirateCrew.Core.Log.Error("[PixelartMaterialFactory] 找不到 shader \""
                    + PixelartPath.ObjectShaderName + "\"（被剔除/编译失败？），材质 " + name + " 未创建。");
                return null;
            }

            var material = new Material(shader) { name = name };
            Configure(material, albedo, bandCount, outlinePixels, aaScale, priority);
            return material;
        }

        /// <summary>
        /// 剪影 sprite 版（t3ssel8r 草坪口径，参照 external/ref/unity-isometric-pixel-pipeline 的
        /// GrassBlade.shader："贴图只管形状、颜色全在调色板"——<paramref name="mask"/> 是**像素画剪影
        /// 遮罩**，物体 pass 只采样它的 alpha 裁切形状（<c>clip(a - _Cutoff)</c>），颜色仍是
        /// <paramref name="albedo"/> 纯色。开 `_SPRITE` 关键字后连通域/描边/着色对裁掉的部分
        /// 一无所知 ⇒ 等价于"这些像素不存在"。描边恒关（草叶不该有墨线，r17/r18 实拍教训）。
        /// 贴图导入口径：**Point / Clamp / 无 mip / 不压缩**（像素 mask 任何过滤都会糊边）。
        /// </summary>
        public static Material CreateSprite(string name, Color albedo, Texture2D mask,
            float bandCount = DefaultBandCount, float cutoff = 0.5f, float aaScale = 1f)
        {
            // 贴片（草叶等）是**遮挡物**：priority=0（Shape.r），描边不与它出接触线。
            Material material = Create(name, albedo, bandCount, outlinePixels: 0f, aaScale: aaScale,
                priority: 0f);
            if (material == null)
                return null;

            material.EnableKeyword("_SPRITE");
            material.SetTexture("_BaseMap", mask);
            material.SetFloat("_Cutoff", cutoff);
            return material;
        }

        /// <summary>
        /// 把配方写进一个已存在的材质（编辑器侧"就地更新资产"与运行期"新建后套参数"共用）。
        ///
        /// 【逐项为什么是这个值】
        /// <list type="bullet">
        ///   <item>`_MainLightLevel` = 色带档数（越高越细腻、越低越"平涂"）；</item>
        ///   <item>`_DitherMode` = 0（Bayer 4×4）/ `_DitherStrength` = <see cref="DefaultDitherStrength"/>：
        ///         抖动默认**开**（2026-09-29 创始人裁决，渐变态半幅）；出图脚本用
        ///         MaterialPropertyBlock 临时拨档对照；</item>
        ///   <item>`_NormalEdgeLevel` = **-1（压暗）** / `_NormalEdgeThreshold` = **1.0**：连通域判出
        ///         "单元内法线差超阈值"时给该像素**降**一档——面转折处压出一条内墨线。
        ///         口径照参考库：它的 `_EdgeLevel` 默认值与演示非金属件都是 -1，阈值默认值与演示
        ///         非金属件都是 1.0（两个单位法线之差 &gt; 1 ⇒ 面夹角 &gt; 60° 才触发，于是只在硬转折上出现）。
        ///         ⚠ **负值能表达的前提是 shader 属性声明成 `Range(-1, 1)`**：`Range(0, 1)` 会把 -1 夹成 0，
        ///         这一项就静默变成"永不触发"（表面看是"没效果"，实则参数根本没传进去）。</item>
        ///   <item>`_AAScale` = 1：连通域降档门控不缩放（v3 的 `_AAScale` 同义）；**0 = 整体关闭降档**
        ///         （草坪口径：降档的聚合窗格跨到物体剪影上时，物体旁的地面像素被误判成"面转折"
        ///         而压暗一档，观感是描边外圈忽有忽无的深色毛边——r25 逐像素定位。草地/大平面这类
        ///         "自身没有合法内线"的材质置 0，角色/道具保持 1）；</item>
        ///   <item>`_Smoothness` = **0（高光关）**：本路径高光趟对 `pow(NdotH, exp)` 做两档量化后乘
        ///         `_Smoothness`——地面/海面这类大平面上，相机方位一转 NdotH 就扫过量化的 floor 边界，
        ///         高光带整档翻面（创始人报的"地面反太阳光、旋转时颜色骤变"）。
        ///         **这不是本仓的偏离**：参考库 `SpecularShading`（`ShadingPass.hlsl:117-126`）逐行同式、
        ///         `level` 也硬编码 2.0 ⇒ 跳变是参考实现自带的行为。所以这是"要不要付跳变代价换回高光"
        ///         的裁决项，不是修 bug：**恢复 `_Smoothness` 之前必须先软化高光量化**（按艺术像素有序
        ///         抖动 / 提高档数），否则会原样重现那个跳变。`_Metallic` 0 = 无金属反射色（与参考库
        ///         非金属件一致）。</item>
        ///   <item>`_RimLightColor` = 黑：本物体不出边缘光（改画面要有理由，验证通路才拨亮）；</item>
        ///   <item>`_SnapToPixelGrid` = 1：物体级像素吸附（v3 CommonPass 的第二层）。</item>
        /// </list>
        /// </summary>
        public static void Configure(Material material, Color albedo,
            float bandCount = DefaultBandCount, float outlinePixels = DefaultOutlinePixels,
            float aaScale = 1f, float priority = 1f)
        {
            if (material == null)
                return;

            // 【Shape.r 优先级】1 = 可行走地面（描边接触线落在地面侧，见 PixelartOutline
            // 出线规则 ① 的 ground 分支）；0 = 遮挡物（草叶/贴片/道具——纯遮挡，不出接触线）。
            material.SetFloat("_Priority", priority);
            material.SetColor("_BaseColor", albedo);
            material.SetFloat("_MainLightLevel", bandCount);
            material.SetFloat("_DitherMode", 0f);
            material.SetFloat("_DitherStrength", DefaultDitherStrength);
            material.SetFloat("_NormalEdgeLevel", NormalEdgeLevel);
            material.SetFloat("_NormalEdgeThreshold", NormalEdgeThreshold);
            material.SetFloat("_AAScale", aaScale);
            material.SetFloat("_Smoothness", 0f);
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
            string raw = hex;
            if (!string.IsNullOrEmpty(hex) && hex[0] == '#')
                hex = hex.Substring(1);

            // 守卫：非法串在这里就带着原始串与期望格式报 ArgumentException，不漏进
            // Substring/int.Parse 变成深处的越界/FormatException——失败语义不变，失败原因说人话。
            if (hex == null || hex.Length != 6)
                throw new System.ArgumentException(
                    "sRGB hex 期望 \"RRGGBB\"（可选 # 前缀、6 位十六进制），收到 \"" + raw + "\"。", nameof(hex));
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    throw new System.ArgumentException(
                        "sRGB hex 期望 \"RRGGBB\"（可选 # 前缀、6 位十六进制），收到 \""
                        + raw + "\"（第 " + (i + 1) + " 位 \"" + c + "\" 不是十六进制位）。", nameof(hex));
            }

            return new Color(
                int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                1f);
        }
    }
}
