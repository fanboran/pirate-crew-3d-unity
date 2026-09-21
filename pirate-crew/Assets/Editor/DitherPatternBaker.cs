using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 烘焙 **1-bit 密度抖动图案**（v3 口径）为工程资产：4×4/6×6/8×8/16×16 共 9 张，
    /// 供 <c>PirateCrew/PirateToon</c> 的 <c>_DitherMode=1/2</c> 采样
    /// （对照依据见 docs/技术/渲染/蓝图-新渲染管线-v3蓝本.md §6 P0-2）。
    ///
    /// 【为什么是"密度图案"而不是有序抖动矩阵】v3（`SL0ANE/SloanePixelartURP`，MIT）的抖动不是
    ///   Bayer 矩阵，而是一张**二值图案**：着色时 <c>ndotl += singleLevel · ±strength</c>，
    ///   图案的"亮纹素"整体偏移 +、暗纹素偏移 −，于是在色带边界上按图案的**固定密度**撕出锯齿
    ///   （v3 生产材质用的是 ~50% 密度的三张：行条 / 棋盘 / 8×8 小圆点，strength ≈ 0.2）。
    ///   与 Bayer 的区别：Bayer 有 16 级渐变态（边界平滑过渡），密度图案只有两态（边界是"撕边"）。
    ///
    /// 【图案出处与"不入库"纪律】矩阵按 v3 资产
    ///   （`external/sloane-urp/.../Assets/Textures/DitherGrayScale/dither_0..8.png`，
    ///   参照库整目录 gitignore、不入库）**逐像素量出后在本文件以字符串重制**——
    ///   等价于按源码级翻译改编，不 vendor 原始 PNG。行字符串**自上而下**（同 PNG 行序），
    ///   写入时翻成 Unity 的自下而上 y。
    ///
    /// 【导入三条】Point + mipmap off + 不压缩（像素纹理口径，资产管线 §3）；wrap 用 **Repeat**
    ///   （采样 UV = 屏幕像素 ÷ 图案边长，必然超出 [0,1)，必须可平铺）。shader 侧用 inline 采样器
    ///   <c>sampler_point_repeat</c> 双保险，导入设置被改坏也不至于糊成灰。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/ToonPilot/烘焙 1-bit 密度抖动图案（9 张）
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.DitherPatternBaker.Bake
    /// </summary>
    public static class DitherPatternBaker
    {
        const string OutputFolder = "Assets/Art/Textures/Fx/Dither";

        /// <summary>
        /// 九张图案的位图（'1' = 亮纹素 → 纹理值 255；'0' = 暗纹素 → 0）。
        /// 注释里的"参照用途"是按 v3 的生产材质反查出来的：`DitherPattern_0/1/2.mat` 分别引用
        /// dither_7 / dither_0 / dither_4（本表序号同 v3 文件名后缀）。
        /// </summary>
        static readonly string[][] Patterns =
        {
            // 0：4×4 棋盘（50%）——v3 生产材质 DitherPattern_1 用
            new[]
            {
                "0101",
                "1010",
                "0101",
                "1010",
            },
            // 1：4×4 稀疏点（25%）
            new[]
            {
                "0101",
                "0000",
                "0101",
                "0000",
            },
            // 2：4×4 密点（62.5%）
            new[]
            {
                "1100",
                "1110",
                "1110",
                "0110",
            },
            // 3：6×6 方块阵（44.4%）
            new[]
            {
                "000000",
                "011011",
                "011011",
                "000000",
                "110110",
                "110110",
            },
            // 4：8×8 圆点阵（50%）——v3 生产材质 DitherPattern_2 用
            new[]
            {
                "01100000",
                "11110110",
                "11110110",
                "01100000",
                "00000110",
                "01101111",
                "01101111",
                "00000110",
            },
            // 5：8×8 实心圆角块（93.8%）——**不是抖动图案**（是遮罩），一并烘出仅供比照
            new[]
            {
                "01111110",
                "11111111",
                "11111111",
                "11111111",
                "11111111",
                "11111111",
                "11111111",
                "01111110",
            },
            // 6：16×16 斜带（50%）
            new[]
            {
                "0000001111111100",
                "0011111111000000",
                "1111110000000011",
                "1100000000111111",
                "0000001111111100",
                "0011111111000000",
                "1111110000000011",
                "1100000000111111",
                "0000001111111100",
                "0011111111000000",
                "1111110000000011",
                "1100000000111111",
                "0000001111111100",
                "0011111111000000",
                "1111110000000011",
                "1100000000111111",
            },
            // 7：4×4 横条（50%）——v3 生产材质 DitherPattern_0 用
            new[]
            {
                "0000",
                "1111",
                "0000",
                "1111",
            },
            // 8：8×8 竖条阵（65.6%）
            new[]
            {
                "01110111",
                "01110111",
                "01110111",
                "00000111",
                "01110111",
                "01110111",
                "01110111",
                "01110000",
            },
        };

        [MenuItem("PirateCrew/ToonPilot/烘焙 1-bit 密度抖动图案（9 张）")]
        public static void Bake()
        {
            EnsureFolder("Assets/Art/Textures");
            EnsureFolder("Assets/Art/Textures/Fx");
            EnsureFolder(OutputFolder);

            for (int i = 0; i < Patterns.Length; i++)
                BakeOne(i, Patterns[i]);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DitherPatternBaker] 1-bit 密度抖动图案烘焙完成 " + Patterns.Length
                + " 张 → " + OutputFolder + "（Point / mip off / 不压缩 / Repeat；"
                + "shader 的 _DitherMode=1 块对齐、=2 屏幕像素率）。");
        }

        static void BakeOne(int index, string[] rows)
        {
            int n = rows.Length;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);

            for (int row = 0; row < n; row++)
            {
                // 字符串自上而下；Unity 纹理 y=0 在下 → 翻转。
                int y = n - 1 - row;
                string line = rows[row];
                if (line.Length != n)
                {
                    Debug.LogError("[DitherPatternBaker] 图案 " + index + " 第 " + row
                        + " 行长度 " + line.Length + " ≠ " + n + "，跳过。");
                    Object.DestroyImmediate(tex);
                    return;
                }

                for (int x = 0; x < n; x++)
                {
                    // 二值：纹理值 0/1（sRGB 导入后 shader 采到的仍是 0..1 两态；
                    // 偏移量由 _DitherStrength 定，不靠纹理的亮度差）。
                    float v = line[x] == '1' ? 1f : 0f;
                    tex.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }
            tex.Apply();

            string path = OutputFolder + "/ToonDither_" + index + ".png";
            // File IO 必须绝对路径（batchmode 下相对路径按进程 CWD 解析——ToonPilotSetup 同款教训）。
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            File.WriteAllBytes(abs, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[DitherPatternBaker] 导入器缺失：" + path);
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.npotScale = TextureImporterNPOTScale.None;   // 6×6 图案不是 2 的幂，禁自动缩放
            importer.SaveAndReimport();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
