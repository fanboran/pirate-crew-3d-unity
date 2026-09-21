using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 像素纹理的**约定域**与导入设置的唯一真源（像素纹理资产管线 §3 的执行体）。
    ///
    /// 【约定规则（只有一条，别再发明第二条）】
    /// <code>Assets/Art/Textures/Pixel/** （含任意深度子目录）= 像素纹理</code>
    /// 该目录下的每一张贴图一律按下表导入，目录外一律不碰。
    ///
    /// 【为什么用目录而不是文件名后缀】三条理由，按重要性排序：
    ///   ① **范围一眼可判**：想知道哪些资产受管，看目录树即可，不必逐个文件名字符串匹配；
    ///   ② **不会在改名/导出链路上被丢掉**：后缀约定在 Blender 导出、批量重命名、格式转换里
    ///      极易丢失，一旦丢了就静默退回写实导入设置（这是"改了看不出问题"型事故）；
    ///   ③ **迁移是显式动作**：把一张贴图搬进该目录 = 宣布它是像素纹理，git diff 里看得见。
    ///
    /// 【为什么只对约定目录生效（不回溯存量）】存量 55 张贴图是写实期产物（多数开了 mip、
    /// 用双线性过滤），像素化后处理已经把全局观感兜住了（资产篇 §7）。批量改成 Point/mip off
    /// 会改动现役画面且**无法人眼验收**（主控串行出图成本高），所以规则显式只对新增/约定目录生效
    /// ——这是重构总纲 §7 风险表「美术管线改动影响现有画面」的缓解措施。
    ///
    /// 【与 Postprocessor 的分工】本类是"规矩"，<see cref="PixelArtTexturePostprocessor"/> 是"执行"，
    /// <see cref="PixelArtTextureValidator"/> 是"检查"。三者共用本类的常量与断言逻辑，
    /// 因此规矩改动只有一处。
    /// </summary>
    public static class PixelArtTextureRules
    {
        /// <summary>像素纹理的约定根目录（工程内相对路径，无尾斜杠）。</summary>
        public const string PixelRoot = "Assets/Art/Textures/Pixel";

        /// <summary>亚像素密度说明用的档位名（资产篇 §2 的尺寸表在文档里，这里只留目标密度）。</summary>
        public const int TexelDensityPxPerMeter = 32;

        /// <summary>
        /// 该资产路径是否属于像素纹理约定域。
        /// 只认目录前缀（区分大小写，与 Unity 资产路径一致），不接受"恰好同名"的兄弟目录。
        /// </summary>
        public static bool IsPixelAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            string p = assetPath.Replace('\\', '/');
            return p == PixelRoot
                || p.StartsWith(PixelRoot + "/", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 把三条关键设置（+ 一条隐藏坑）写进 importer。**只在 OnPreprocessTexture 里调用**。
        ///
        /// | 项 | 值 | 理由（资产篇 §3） |
        /// |---|---|---|
        /// | Filter Mode | Point | 像素硬边；双线性会把板内色插成板外色 |
        /// | Generate Mip Maps | Off | 正交固定机位没有"远处"；mip 的平均色同样是板外色 |
        /// | Compression | Uncompressed（RGBA32） | BC1/BC3 压脏 4px 色块；BC7 端点插值必然破板（调研 §5） |
        /// | sRGB | 勾 | 板色按 sRGB 十六进制定义，编码必须一致 |
        /// | Alpha Is Transparency | **关** | 见下方长注释 |
        ///
        /// 【AlphaIsTransparency 为什么必须关】开启后 Unity 会把 RGB **膨胀**到透明区域
        /// （dilate），透明像素的 RGB 变成从邻近色插出来的中间值——那些值不在板上，
        /// 会直接破坏「贴图唯一色数 ≤ 板色数」的验收判据与逐字节回归 diff；而 Point + 无 mip 下
        /// 根本没有需要膨胀来修的黑边（黑边是双线性/mip 时代的产物）。
        /// 透明是画出来的（halo / 光池 / 泡沫），不是被推导出来的。
        ///
        /// 【明确不改的项】wrapMode 保持导入默认（Repeat）——像素纹理要被平铺到岛台/地面大面上，
        /// 强制 Clamp 会在接缝处把结构线截断；每个资产需要 Clamp 时在 .meta 上单独覆写，
        /// 属于"逐资产例外"而非全局口径。maxTextureSize / npotScale / isReadable 同样不动
        /// （尺寸规范由尺寸表约束，不由导入器裁剪）。
        /// </summary>
        public static void Apply(TextureImporter importer)
        {
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;                 // ① 像素硬边
            importer.mipmapEnabled = false;                         // ② 无 mip
            importer.textureCompression = TextureImporterCompression.Uncompressed; // ③ 固定不压缩（BC7 除名）
            importer.sRGBTexture = true;                            // ④ 与板色编码一致
            importer.alphaIsTransparency = false;                   // ⑤ 隐藏坑：不膨胀 RGB，保住锁板
            importer.crunchedCompression = false;                   // 不压缩时无意义，显式关掉免得误导
        }

        /// <summary>
        /// 断言 importer 符合约定；返回违反项清单（空 = 合规）。
        /// <see cref="PixelArtTextureValidator"/> 与 EditMode 测试共用这一份判据。
        /// </summary>
        public static System.Collections.Generic.List<string> Check(TextureImporter importer)
        {
            var problems = new System.Collections.Generic.List<string>();
            if (importer == null)
            {
                problems.Add("importer 为空（AssetImporter.GetAtPath 拿不到 TextureImporter？）");
                return problems;
            }

            if (importer.filterMode != FilterMode.Point)
                problems.Add("filterMode=" + importer.filterMode + "（应为 Point）");
            if (importer.mipmapEnabled)
                problems.Add("mipmapEnabled=true（应为 false）");
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                problems.Add("textureCompression=" + importer.textureCompression + "（应为 Uncompressed）");
            if (!importer.sRGBTexture)
                problems.Add("sRGBTexture=false（应为 true）");
            if (importer.alphaIsTransparency)
                problems.Add("alphaIsTransparency=true（应为 false —— 会膨胀 RGB 破坏锁板）");
            return problems;
        }

        /// <summary>验收判据的书面描述（校验器的报告头与文档同源）。</summary>
        public static string Expectation()
        {
            return "Point / mip off / Uncompressed(RGBA32) / sRGB on / alphaIsTransparency off";
        }
    }
}
