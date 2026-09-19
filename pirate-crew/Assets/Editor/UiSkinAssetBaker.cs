using System.IO;
using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 多彩卡通皮肤 / 符号 / 图标集的资产烘焙器（把程序化像素落成持久 PNG）。
    ///
    /// 【为什么需要落盘】编辑器装配的场景必须引用**持久资产**（内存 Sprite 存不进场景，
    /// 重开场景引用即丢——沿用 <see cref="MenuUiBuilder"/> 的既有结论）；运行时动态件
    /// （职业头像 pips 等）走 <c>Resources.Load</c>，图标落 <c>Assets/Resources/UIIcons/</c>。
    ///
    /// 【入口】菜单 <c>PirateCrew/UI/重烘焙卡通皮肤与图标集</c>；也可无头：
    /// <c>-executeMethod PirateCrew.EditorTools.UiSkinAssetBaker.BakeAll</c>（纯 CPU 像素，
    /// 不依赖 GPU 渲染路径——本机 batchmode 没有 GfxDevice，相机烘焙方案不可行）。
    /// </summary>
    public static class UiSkinAssetBaker
    {
        const string SkinFolder = "Assets/Art/Sprites/UI";
        const string IconFolder = "Assets/Resources/UIIcons";
        const string SketchFolder = "Assets/Resources/UI/Sketch";

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

        /// <summary>烘焙全部：手绘皮肤导入参数 + 平色九宫格 + 符号图标 + 武器/职业图标集。</summary>
        [MenuItem("PirateCrew/UI/重烘焙卡通皮肤与图标集")]
        public static void BakeAll()
        {
            int count = 0;

            // 0) 手绘涂鸦皮肤（tools/sketch_ui/gen_sketch_ui.py 产的 PNG 已在仓库里，
            //    这里只批设导入口径：sprite + 九宫格 border=10——缺这步 Resources.Load<Sprite> 为 null）。
            count += EnsureSketchImports();

            // 1) 平色九宫格皮肤（Cartoon_<Shape>.png；几何纯函数仍被 UiGlyphs/测试引用）。
            foreach (CartoonSpriteFactory.Shape shape in System.Enum.GetValues(typeof(CartoonSpriteFactory.Shape)))
            {
                Texture2D texture = CartoonSpriteFactory.CreateTexture(shape, out Vector4 border);
                WritePng(SkinFolder + "/Cartoon_" + shape + ".png", texture, border);
                count++;
            }

            // 2) 符号图标（Glyph_<Glyph>.png；切片边框 0）。
            foreach (UiGlyphs.Glyph glyph in System.Enum.GetValues(typeof(UiGlyphs.Glyph)))
            {
                Texture2D texture = UiGlyphs.CreateTexture(glyph);
                WritePng(SkinFolder + "/Glyph_" + glyph + ".png", texture, Vector4.zero);
                count++;
            }

            // 3) 武器 / 职业图标 → Resources（运行时 pips / 头像动态加载）。
            MenuUiBuilder.EnsureFolder("Assets/Resources");
            MenuUiBuilder.EnsureFolder(IconFolder);
            for (int i = 0; i <= 16; i++)
            {
                var id = (WeaponId)i;
                WritePng(IconFolder + "/Weapon_" + id + ".png", UiIconPainter.PaintWeapon(id), Vector4.zero);
                count++;
            }
            foreach (string key in new[] { "sailor", "gunner", "sniper", "hooker", "arsonist", "skeleton", "captain" })
            {
                WritePng(IconFolder + "/Crew_" + key + ".png", UiIconPainter.PaintCrew(key), Vector4.zero);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UiSkinAssetBaker] 卡通皮肤与图标烘焙完成，共 " + count + " 张（"
                + SkinFolder + " + " + IconFolder + "）。");
        }

        /// <summary>
        /// 批设手绘贴图导入口径（幂等：已是 sprite+border=10 的跳过不重导）。
        /// 独立菜单项供单跑；<see cref="BakeAll"/> 与场景装配链都会先走这里。
        /// </summary>
        [MenuItem("PirateCrew/UI/设定手绘皮肤导入参数")]
        public static int EnsureSketchImports()
        {
            if (!System.IO.Directory.Exists(SketchFolder))
            {
                Debug.LogWarning("[UiSkinAssetBaker] " + SketchFolder + " 不存在（先跑 tools/sketch_ui/gen_sketch_ui.py）");
                return 0;
            }

            int touched = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SketchFolder }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
                {
                    // 九砖子件走 Tiled 平铺，需要 Repeat（Clamp 会在砖边缘渗色）；
                    // 整图槽走 Sliced，Clamp 防 border 区渗色。
                    string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                    bool isBrick = System.Text.RegularExpressions.Regex.IsMatch(
                        name, @"_(tl|tc|tr|ml|mc|mr|bl|bc|br)_f\d$");
                    var wrap = isBrick ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                    if (importer.textureType != TextureImporterType.Sprite
                        || importer.spriteBorder != new Vector4(10f, 10f, 10f, 10f)
                        || importer.wrapMode != wrap)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.spritePixelsPerUnit = 100f;
                        importer.spriteBorder = new Vector4(10f, 10f, 10f, 10f);   // 烘焙 MARGIN=10
                        importer.mipmapEnabled = false;
                        importer.alphaIsTransparency = true;
                        importer.wrapMode = wrap;
                        importer.filterMode = FilterMode.Bilinear;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                        touched++;
                    }
                }
            }
            Debug.Log("[UiSkinAssetBaker] 手绘皮肤导入参数：新设 " + touched + " 张。");
            return touched;
        }
    }
}
