using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// 像素纹理的导入后处理器：把 <see cref="PixelArtTextureRules.PixelRoot"/> 目录下的每一张贴图
    /// 强制成像素规格（Point / mip off / Uncompressed / sRGB / 不膨胀 alpha）。
    ///
    /// 【为什么必须是 AssetPostprocessor 而不是一次性脚本】导入设置属于 .meta，而 .meta 会被
    /// 三条路径反复重写：资产被重新导入、meta 被误删后重建、Blender 重新导出后覆盖 PNG。
    /// 一次性脚本改完的设置在下一次重导就会丢——只有挂在导入管线上才是**收敛的**。
    ///
    /// 【范围纪律（这条最关键）】只在 <c>OnPreprocessTexture</c> 里判断路径前缀，目录外**完全不碰**：
    /// 存量 55 张贴图（写实期产物，多数开 mip + 双线性）不在约定目录内，因此本处理器对它们
    /// 零影响 —— 现有画面不会被回溯改动（重构总纲 §7 的缓解措施）。
    ///
    /// 【触发时机与幂等】OnPreprocessTexture 在导入写盘**之前**触发，改 importer 的值即改最终 .meta；
    /// 重复导入同一张图结果相同（设置是绝对赋值，不是增量）。
    ///
    /// 【入口】无（导入时自动跑）。手动补跑：把资产 Reimport，或走
    /// 菜单 PirateCrew/Art/像素纹理/校验约定目录。
    /// </summary>
    public class PixelArtTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!PixelArtTextureRules.IsPixelAsset(assetPath))
                return;

            var importer = assetImporter as TextureImporter;
            if (importer == null)
                return;

            PixelArtTextureRules.Apply(importer);

            // 判定用 Debug.Log（不是 LogWarning）：导入期每次都会跑，Warning 会刷屏。
            // 想核对结果跑 PixelArtTextureValidator.ValidateAll，它按资产逐个断言并汇总。
            if (assetPath.EndsWith(".px.png", System.StringComparison.OrdinalIgnoreCase))
            {
                // 命名后缀不是约定（约定只有目录一条），但撞上了说明有人按旧提案命名——
                // 提一句，避免"两条规则并存"的误解。
                Debug.Log("[PixelArtTexturePostprocessor] 提示：" + assetPath
                    + " 命中了已废弃的 .px.png 后缀命名；现行约定只有目录一条（" 
                    + PixelArtTextureRules.PixelRoot + "/**），后缀不参与判定。");
            }
        }
    }
}
