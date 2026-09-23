using System.IO;
using PirateCrew.UI;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 符号图标集的资产烘焙器（把 <see cref="UiGlyphs"/> 的程序化像素落成持久 PNG）。
    ///
    /// 【为什么需要落盘】编辑器装配的场景必须引用**持久资产**（内存 Sprite 存不进场景，
    /// 重开场景引用即丢）；<see cref="UiKit.GlyphSprite"/> 的 Editor 路径按
    /// <c>Assets/Art/Sprites/UI/Glyph_&lt;Glyph&gt;.png</c> 取这份资产，缺失时才退内存生成。
    ///
    /// 【历史包袱已清退】旧代际的 Cartoon 平涂九宫格（CartoonSpriteFactory）与武器/职业
    /// 静物图标（UiIconPainter → Resources/UIIcons）随 2026-09-24 UI 清退批次一并移除——
    /// HUD 已改文字占位，无消费方。本烘焙器只剩符号图标一族。
    ///
    /// 【入口】菜单 <c>PirateCrew/UI/重烘焙符号图标</c>；也可无头：
    /// <c>-executeMethod PirateCrew.EditorTools.UiSkinAssetBaker.BakeAll</c>（纯 CPU 像素，
    /// 不依赖 GPU 渲染路径——本机 batchmode 没有 GfxDevice，相机烘焙方案不可行）。
    /// </summary>
    public static class UiSkinAssetBaker
    {
        const string SkinFolder = "Assets/Art/Sprites/UI";

        /// <summary>九宫格贴图导入口径（渐变/细边禁不起压缩）。</summary>
        static void ImportSprite(string assetPath, Vector4 border)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = border;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            else
            {
                Debug.LogWarning("[UiSkinAssetBaker] 读不到 TextureImporter：" + assetPath);
            }
        }

        static void WritePng(string assetPath, Texture2D texture, Vector4 border)
        {
            try
            {
                MenuUiBuilder.EnsureFolder("Assets/Art");
                MenuUiBuilder.EnsureFolder("Assets/Art/Sprites");
                MenuUiBuilder.EnsureFolder(SkinFolder);

                byte[] png = texture.EncodeToPNG();
                string absolutePath = Path.Combine(Application.dataPath,
                    assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(absolutePath, png);
                ImportSprite(assetPath, border);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UiSkinAssetBaker] 烘焙失败（" + assetPath + "）：" + e.Message
                    + "\n  运行时仍可用内存 Sprite（外观一致，但不随场景持久化）。");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>烘焙全部：符号图标（Glyph_&lt;Glyph&gt;.png；切片边框 0）。</summary>
        [MenuItem("PirateCrew/UI/重烘焙符号图标")]
        public static void BakeAll()
        {
            int count = 0;
            foreach (UiGlyphs.Glyph glyph in System.Enum.GetValues(typeof(UiGlyphs.Glyph)))
            {
                Texture2D texture = UiGlyphs.CreateTexture(glyph);
                WritePng(SkinFolder + "/Glyph_" + glyph + ".png", texture, Vector4.zero);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UiSkinAssetBaker] 符号图标烘焙完成，共 " + count + " 张（" + SkinFolder + "）。");
        }
    }
}
