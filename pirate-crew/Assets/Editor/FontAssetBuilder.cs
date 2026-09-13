using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 从 <c>Assets/Art/Fonts/</c> 下的原始 ttf 生成 TMP（TextMeshPro）字体资产。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Fonts/生成 TMP 中文字体资产（幂等）
    ///   菜单: PirateCrew/Fonts/强制重建 TMP 中文字体资产
    ///   无头/批处理: -executeMethod PirateCrew.EditorTools.FontAssetBuilder.BuildAll
    ///
    /// 【产物】（路径给 UI 波次直接引用；分工来自 docs/UI-UX与中文本地化规范.md 定稿）
    ///   Assets/Art/Fonts/StickHand-Regular SDF.asset        ← 标题（手写体，fallback 到正文）
    ///   Assets/Art/Fonts/LXGWWenKaiLite-Medium SDF.asset    ← 正文主字体
    ///   Assets/Art/Fonts/LXGWWenKaiLite-Regular SDF.asset   ← 次级说明
    ///
    /// 【StickHand 的授权链】StickHand 是站酷快乐体（ZCOOL KuaiLe，OFL 1.1）的**修改版**，
    /// 由本项目作者用 tools/fonts/gen_hand_font.py 扰动字形生成；OFL 允许修改与再分发，
    /// 且站酷快乐体未声明 Reserved Font Name，故原样入库合规。原件与授权见
    /// Assets/Art/Fonts/ZCOOLKuaiLe-Regular.ttf 与 Licenses/StickHand-LICENSE.txt。
    ///
    /// 【注意：legacy UnityEngine.UI.Text 用的是另一套资产】
    ///   ttf 本身被 Unity 导入为 <see cref="Font"/>（Dynamic），legacy Text 直接引用
    ///   <c>Assets/Art/Fonts/LXGWWenKaiLite-Regular.ttf</c> 即可显示中文（无需 TMP）。
    ///   本脚本生成的 SDF 资产只对 TextMeshProUGUI / TextMeshPro 生效，两者不通用。
    ///
    /// 【参数选择理由】
    ///   · AtlasPopulationMode = Dynamic（动态）：中文常用字形上万，静态烘焙要么字符集残缺
    ///     要么图集巨大（多张 2048 仍不够）并导致导入极慢。动态模式按需把实际用到的字形
    ///     写入图集，导入快、包体小；enableMultiAtlasSupport = true 让图集写满后自动开新图集。
    ///   · samplingPointSize 正文 64 / 标题 72：SDF 清晰度与采样点数正相关。该值只决定
    ///     生成字形时的清晰度上限，不决定实际显示字号；64 足以支撑 16~48px 的正文，
    ///     标题更大留到 72。取值越大单字形占用的图集越大（动态模式按需付费）。
    ///   · atlasPadding 5/6（≈ 采样点数的 8~10%）：SDF 边缘需要留白，padding 太小会出现
    ///     相邻字形相互"渗色"（bleed）的亮边；TMP 官方实践取采样点数的约 10%。
    ///   · Atlas 初始 1024×1024：动态模式下这只是初始容量，写满会按需扩容，1024 让首次
    ///     导入保持轻量（2048 起会让首次生成/占位变重，收益有限）。
    ///   · GlyphRenderMode = SDFAA：抗锯齿的 SDF，通用质量与性能最平衡；HINTED 变体在
    ///     中文字形数量下收益不大。
    ///
    /// 【幂等性】
    ///   已存在目标资产时**直接跳过**，不重复创建、不产生 `... 1.asset` 之类的重复文件。
    ///   需要按新 ttf/参数重做时用"强制重建"菜单（会先删旧资产再生成）。
    /// </summary>
    public static class FontAssetBuilder
    {
        const string FontsFolder = "Assets/Art/Fonts";

        /// <summary>一个待生成字体的参数集合。</summary>
        sealed class FontSpec
        {
            public string SourceTtfPath;
            public string AssetFileName;   // 不含目录与 .asset 后缀
            public int SamplingPointSize;
            public int AtlasPadding;
            public int AtlasWidth;
            public int AtlasHeight;
            public GlyphRenderMode RenderMode;
            public string Purpose;         // 写入日志的中文用途说明

            public string AssetPath => FontsFolder + "/" + AssetFileName + ".asset";
        }

        static readonly FontSpec[] Specs =
        {
            new FontSpec
            {
                SourceTtfPath = FontsFolder + "/LXGWWenKaiLite-Medium.ttf",
                AssetFileName = "LXGWWenKaiLite-Medium SDF",
                SamplingPointSize = 64,
                AtlasPadding = 5,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.SDFAA,
                Purpose = "正文主字体（霞鹜文楷 Lite Medium，楷体；OFL 1.1）",
            },
            new FontSpec
            {
                SourceTtfPath = FontsFolder + "/LXGWWenKaiLite-Regular.ttf",
                AssetFileName = "LXGWWenKaiLite-Regular SDF",
                SamplingPointSize = 64,
                AtlasPadding = 5,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.SDFAA,
                Purpose = "次级说明（霞鹜文楷 Lite Regular；OFL 1.1）",
            },
            new FontSpec
            {
                SourceTtfPath = FontsFolder + "/StickHand-Regular.ttf",
                AssetFileName = "StickHand-Regular SDF",
                SamplingPointSize = 72,
                AtlasPadding = 6,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.SDFAA,
                Purpose = "标题（StickHand 手写体，站酷快乐体修改版；OFL 1.1）",
            },
        };

        /// <summary>幂等生成：已存在的资产跳过。批处理入口。</summary>
        [MenuItem("PirateCrew/Fonts/生成 TMP 中文字体资产（幂等）", priority = 20)]
        public static void BuildAll()
        {
            Build(false);
        }

        /// <summary>强制重建：先删除目标资产再重新生成（改了 ttf 或参数后用）。</summary>
        [MenuItem("PirateCrew/Fonts/强制重建 TMP 中文字体资产", priority = 21)]
        public static void ForceRebuildAll()
        {
            Build(true);
        }

        static void Build(bool force)
        {
            EnsureFolder("Assets/Art");
            EnsureFolder(FontsFolder);

            if (!AssetDatabase.IsValidFolder(FontsFolder))
            {
                Debug.LogError("[FontAssetBuilder] 字体目录不存在且创建失败: " + FontsFolder);
                return;
            }

            int created = 0;
            int skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < Specs.Length; i++)
                {
                    FontSpec spec = Specs[i];

                    Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(spec.SourceTtfPath);
                    if (sourceFont == null)
                    {
                        Debug.LogError("[FontAssetBuilder] 找不到源字体 ttf: " + spec.SourceTtfPath
                            + "\n  请把 OFL 授权允许再分发的 ttf 放到 " + FontsFolder + "/ 后重试；"
                            + "授权文件应放在 " + FontsFolder + "/Licenses/。");
                        continue;
                    }

                    TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(spec.AssetPath);
                    if (existing != null && !force)
                    {
                        skipped++;
                        Debug.Log("[FontAssetBuilder] 已存在，跳过（幂等）: " + spec.AssetPath);
                        continue;
                    }

                    if (existing != null && force)
                    {
                        // 强制重建：旧资产的贴图/材质是它的子资产，删除资产即可一并清除。
                        AssetDatabase.DeleteAsset(spec.AssetPath);
                    }

                    TMP_FontAsset fontAsset = CreateOne(spec, sourceFont);
                    if (fontAsset != null)
                    {
                        created++;
                        Debug.Log("[FontAssetBuilder] 已生成 " + spec.AssetPath
                            + "（" + spec.Purpose + "，Dynamic + 多图集，采样 "
                            + spec.SamplingPointSize + "，padding " + spec.AtlasPadding
                            + "，初始图集 " + spec.AtlasWidth + "×" + spec.AtlasHeight + "）");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            LinkFallbacks();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[FontAssetBuilder] 完成：新建/重建 " + created + " 个，跳过 " + skipped + " 个。\n"
                + "  产物路径（供 UI/场景引用）:\n"
                + "    标题  " + Specs[2].AssetPath + "（StickHand，fallback → 正文）\n"
                + "    正文  " + Specs[0].AssetPath + "\n"
                + "    次级  " + Specs[1].AssetPath + "\n"
                + "  说明: 这些是 TMP 字体资产，只对 TextMeshProUGUI/TextMeshPro 生效；\n"
                + "        legacy UnityEngine.UI.Text 请直接引用 .ttf（Unity 已导入为 Dynamic Font）。");
        }

        static TMP_FontAsset CreateOne(FontSpec spec, Font sourceFont)
        {
            // 显式重载：Dynamic + enableMultiAtlasSupport（见类头参数理由）。
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                spec.SamplingPointSize,
                spec.AtlasPadding,
                spec.RenderMode,
                spec.AtlasWidth,
                spec.AtlasHeight,
                AtlasPopulationMode.Dynamic,
                true);

            if (fontAsset == null)
            {
                Debug.LogError("[FontAssetBuilder] TMP_FontAsset.CreateFontAsset 返回 null: " + spec.SourceTtfPath
                    + "\n  常见原因：TMP 包未安装/未完成导入，或源字体无法被 TextCore 解析。");
                return null;
            }

            fontAsset.name = spec.AssetFileName;
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            AssetDatabase.CreateAsset(fontAsset, spec.AssetPath);
            PersistSubAssets(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            return fontAsset;
        }

        /// <summary>
        /// 把动态图集贴图与材质登记为字体资产的子资产。
        /// 不登记的话资产引用会指向内存对象，重开工程后丢失（贴图变粉/字体不显示）。
        /// </summary>
        static void PersistSubAssets(TMP_FontAsset fontAsset)
        {
            if (fontAsset.atlasTextures != null)
            {
                foreach (Texture2D texture in fontAsset.atlasTextures)
                {
                    if (texture == null || AssetDatabase.Contains(texture))
                        continue;

                    texture.name = fontAsset.name + " Atlas";
                    texture.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }
            }

            Material material = fontAsset.material;
            if (material != null && !AssetDatabase.Contains(material))
            {
                material.name = fontAsset.name + " Material";
                material.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(material, fontAsset);
            }
        }

        /// <summary>标题字体缺字时回退到正文，避免标题里出现缺字方块。</summary>
        static void LinkFallbacks()
        {
            TMP_FontAsset body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Specs[0].AssetPath);
            TMP_FontAsset title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Specs[2].AssetPath);
            if (body == null || title == null)
                return;

            if (title.fallbackFontAssetTable == null)
                title.fallbackFontAssetTable = new List<TMP_FontAsset>();

            if (!title.fallbackFontAssetTable.Contains(body))
            {
                title.fallbackFontAssetTable.Add(body);
                EditorUtility.SetDirty(title);
                Debug.Log("[FontAssetBuilder] 已把标题字体 fallback 指向正文字体: "
                    + Specs[2].AssetPath + " → " + Specs[0].AssetPath);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
