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
    /// 【产物】（原生档纪律：字号只取像素字体原生设计档，有什么字号做什么字号，不放大）
    ///   Assets/Art/Fonts/ZhengGeDianHei16.asset ← 16 原生档（标题族——正格点黑16，简体全过）
    ///   Assets/Art/Fonts/FusionPixel12.asset    ← 12 原生档（区块/HUD/正文——缝合像素 zh_hans）
    ///   Assets/Art/Fonts/FusionPixel10.asset    ← 10 原生档（辅助提示——缝合像素 zh_hans）
    ///   Assets/Art/Fonts/FusionPixel8.asset     ← 8 原生档（角标/快捷键——缝合像素 zh_hans）
    ///   同名副本同步到 Assets/Resources/Fonts/（运行时 UiKit 经 Resources 加载）。
    ///   覆盖实测（cmap 对工程全部 UI 字符）：16 缺 0；12 缺 1（毂←10 兜）；10 缺 2（胫舭←12 兜）；
    ///   8 全过。回退链按「本档 → 更小档」逐级兜底，单字缺字不落方块。
    ///   已淘汰：方舟 16px（缺 1413 字含巨火扫）、寒蝉 16px（简体约 40%）、
    ///   旧 px30/px36 放大档（翻倍被否决）。24px+ 开源全简体原生档现不存在，不硬凑。
    ///
    /// 【位图口径】samplingPointSize = 字体原生设计尺寸（12/10/8，绝不放大采样）、
    ///   GlyphRenderMode.RASTER_HINTED（位图栅格化，不走 SDFAA——SDF 会给像素字形糊灰边）、
    ///   atlasPadding = 4、图集 filterMode = Point、material 换 TextMeshPro/Bitmap shader。
    ///   1 字体像素 = 1 屏幕像素；显示字号必须等于原生档，错档渲染 = 非整数缩放 = 糊栅格。
    ///
    /// 【注意：legacy UnityEngine.UI.Text 用的是另一套资产】
    ///   ttf 本身被 Unity 导入为 <see cref="Font"/>（Dynamic），legacy Text 直接引用 ttf
    ///   即可显示中文（无需 TMP）；本脚本产物只对 TextMeshProUGUI / TextMeshPro 生效。
    ///
    /// 【参数选择理由】
    ///   · AtlasPopulationMode = Dynamic（动态）：中文常用字形上万，静态烘焙要么字符集残缺
    ///     要么图集巨大并导致导入极慢。动态模式按需把实际用到的字形写入图集，导入快、
    ///     包体小；enableMultiAtlasSupport = true 让图集写满后自动开新图集。
    ///   · Atlas 初始 1024×1024：动态模式下这只是初始容量，写满会按需扩容，1024 让首次
    ///     导入保持轻量。
    ///   · RASTER_HINTED 而非 SDFAA：位图档要的是平涂字形，任何距离场解算都会毁像素边。
    ///   · atlasPadding = 4（采样缓冲，位图档同样需要）：padding 0 时相邻字形格贴死，
    ///     字形边缘 UV 的采样窗口没有余量，落位带小数时 Point 过滤照样读进隔壁字形——
    ///     实机症状「文字上盖一层莫名其妙的像素点」。留白在字形位图外围，
    ///     不参与字形本体采样，不会虚化字形边缘（padding 0 渗色在 SDF 档已复发过一次，
    ///     同一根因在位图档二次实锤：渗色是采样窗口问题，不是 SDF 特有问题）。
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
                SourceTtfPath = FontsFolder + "/ZhengGeDianHei16.ttf",
                AssetFileName = "ZhengGeDianHei16",
                SamplingPointSize = 16,
                AtlasPadding = 4,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.RASTER_HINTED,
                Purpose = "16 原生档（标题族）——正格点黑16，简体全过",
            },
            new FontSpec
            {
                SourceTtfPath = FontsFolder + "/FusionPixel12-zh_hans.ttf",
                AssetFileName = "FusionPixel12",
                SamplingPointSize = 12,
                AtlasPadding = 4,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.RASTER_HINTED,
                Purpose = "12 原生档（区块/HUD/正文）",
            },
            new FontSpec
            {
                SourceTtfPath = FontsFolder + "/FusionPixel10-zh_hans.ttf",
                AssetFileName = "FusionPixel10",
                SamplingPointSize = 10,
                AtlasPadding = 4,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.RASTER_HINTED,
                Purpose = "10 原生档（辅助提示）",
            },
            new FontSpec
            {
                SourceTtfPath = FontsFolder + "/FusionPixel8-zh_hans.ttf",
                AssetFileName = "FusionPixel8",
                SamplingPointSize = 8,
                AtlasPadding = 4,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.RASTER_HINTED,
                Purpose = "8 原生档（角标/快捷键）",
            },
        };

        /// <summary>已退役资产名（构建时清理 Art 与 Resources 两侧，防 GUID 悬空引用复活）。</summary>
        static readonly string[] RetiredAssetNames =
        {
            "FusionPixel12-px30",   // 旧放大档（12px 采样 ×2.5）：翻倍被否决
            "FusionPixel12-px36",   // 旧放大档（12px 采样 ×3）：翻倍被否决
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
            // TMP 的 shader（TextMeshPro/Distance Field 等）默认不在工程资产里——
            // 它们打包在 PackageCache 的 unitypackage 中，需"Import TMP Essential Resources"
            // 导入一次。不导入时 TMP_FontAsset.CreateFontAsset 内部 Shader.Find 返回 null，
            // 抛 ArgumentNullException。判定用 Shader.Find（不要用 TMP_Settings.instance——
            // batchmode 下它的 getter 会尝试弹出导入窗口并刷屏"No graphic device"）。
            // 路径用 PackageCache 的物理路径：虚拟路径 "Packages/..." 传给 ImportPackage 会静默不导入。
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                string pkg = null;
                if (System.IO.Directory.Exists("Library/PackageCache"))
                {
                    string[] hits = System.IO.Directory.GetFiles(
                        "Library/PackageCache", "TMP Essential Resources.unitypackage",
                        System.IO.SearchOption.AllDirectories);
                    if (hits.Length > 0)
                        pkg = hits[0];
                }
                if (pkg == null)
                {
                    Debug.LogError("[FontAssetBuilder] PackageCache 中找不到 TMP Essential Resources.unitypackage");
                    return;
                }
                Debug.Log("[FontAssetBuilder] 首次构建：导入 TMP Essential Resources（shader/TMP Settings）：" + pkg);
                AssetDatabase.ImportPackage(pkg, interactive: false);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (Shader.Find("TextMeshPro/Distance Field") == null)
                {
                    Debug.LogError("[FontAssetBuilder] 导入后仍找不到 TextMeshPro/Distance Field——TMP 资源导入异常");
                    return;
                }
            }

            EnsureFolder("Assets/Art");
            EnsureFolder(FontsFolder);

            if (!AssetDatabase.IsValidFolder(FontsFolder))
            {
                Debug.LogError("[FontAssetBuilder] 字体目录不存在且创建失败: " + FontsFolder);
                return;
            }

            int created = 0;
            int skipped = 0;

            // 退役资产清理（Art 与 Resources 两侧）：旧放大档已被原生档纪律否决，
            // 残留会以悬空 GUID 复活白块链，每次构建顺手清零。
            foreach (string retired in RetiredAssetNames)
            {
                AssetDatabase.DeleteAsset(FontsFolder + "/" + retired + ".asset");
                AssetDatabase.DeleteAsset("Assets/Resources/Fonts/" + retired + ".asset");
            }

            // 【不用 StartAssetEditing 批处理块】块内 DeleteAsset 是延迟执行的：
            // ForceRebuild 先删后建同名资产时，CreateAsset 撞上未落盘的删除 → 
            // UnityException: Creating asset failed（batchmode 实测，2026-09-24）。
            // 5 个资产逐个即时导入，慢不了多少。
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
                    // 【GUID 政策（UI 重构 W4 裁决）】删除+重建会换 GUID——这是**有意行为**：
                    // 字体资产的消费契约是**按 Resources 路径加载**（UiKit.FontTiers /
                    // MenuUiBuilder 三属性），任何资产/场景**不许持字体 GUID 引用**；
                    // 需要换字体观感就走 ForceRebuildAll（全链 GUID 一起换，消费方按路径无感）。
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

            LinkFallbacks();
            CopyToResources();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var lines = new System.Text.StringBuilder();
            foreach (FontSpec spec in Specs)
                lines.Append("\n    ").Append(spec.AssetPath).Append("（").Append(spec.Purpose).Append("）");
            Debug.Log("[FontAssetBuilder] 完成：新建/重建 " + created + " 个，跳过 " + skipped + " 个。"
                + "  产物路径（供 UI/场景引用）:" + lines + "\n"
                + "  说明: TMP 位图字体资产（RASTER_HINTED + Point + Bitmap shader），"
                + "只对 TextMeshProUGUI/TextMeshPro 生效；legacy Text 请直接引用 .ttf。");
        }

        /// <summary>
        /// 把三份 SDF 资产复制一份进 <c>Assets/Resources/Fonts/</c>：播放器运行时
        /// （<see cref="PirateCrew.UI.UiKit.RuntimeFont"/>，Gallery / 运行时动态文本）
        /// 只能经 Resources 加载，Art 路径在包内不可寻址。子资产（图集/材质）随
        /// CopyAsset 一并复制；源 ttf 引用保留，Dynamic 模式在包内仍可按需光栅化新字形。
        /// 幂等：副本存在则先删再拷，保证与 Art 版同步。
        /// </summary>
        static void CopyToResources()
        {
            const string targetFolder = "Assets/Resources/Fonts";
            EnsureFolder("Assets/Resources");
            EnsureFolder(targetFolder);

            foreach (FontSpec spec in Specs)
            {
                string targetPath = targetFolder + "/" + spec.AssetFileName + ".asset";
                if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(targetPath) != null)
                    AssetDatabase.DeleteAsset(targetPath);
                if (!AssetDatabase.CopyAsset(spec.AssetPath, targetPath))
                    Debug.LogError("[FontAssetBuilder] 复制字体到 Resources 失败: " + spec.AssetPath
                        + " → " + targetPath);
            }
            Debug.Log("[FontAssetBuilder] 已同步字体副本到 " + targetFolder
                + "（运行时 UiKit.RuntimeFont 取这批）");
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

            if (spec.RenderMode == GlyphRenderMode.RASTER_HINTED)
            {
                // 位图口径（像素字体专用）：图集 Point 过滤（整数倍缩放最近邻、不出灰边），
                // 材质换 Bitmap shader（SDF shader 会把平涂字形当距离场解，边缘发脏）。
                if (fontAsset.atlasTextures != null)
                {
                    foreach (Texture2D texture in fontAsset.atlasTextures)
                        texture.filterMode = FilterMode.Point;
                }
                Shader bitmap = Shader.Find("TextMeshPro/Bitmap");
                if (bitmap != null && fontAsset.material != null)
                    fontAsset.material.shader = bitmap;
                else
                    Debug.LogWarning("[FontAssetBuilder] 找不到 TextMeshPro/Bitmap shader，"
                        + spec.AssetFileName + " 暂用 SDF 材质显示（可能有灰边）——"
                        + "确认 TMP Essential Resources 已导入。");
            }

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

        /// <summary>确保工程内文件夹存在（幂等；逐级创建）。</summary>
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>缺字回退链：本档缺字时按「更小档」逐级兜底（Specs 按字号降序排列，
        /// 每档 fallback = 其后全部更小档）。覆盖实测：16 与 8 全过；12 缺 1（毂←10 兜）、
        /// 10 缺 2（胫舭←8 兜）——链只是保险，不承担常规渲染。</summary>
        static void LinkFallbacks()
        {
            TMP_FontAsset[] assets = new TMP_FontAsset[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
                assets[i] = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Specs[i].AssetPath);

            for (int i = 0; i < assets.Length - 1; i++)
            {
                if (assets[i] == null)
                    continue;
                assets[i].fallbackFontAssetTable = new List<TMP_FontAsset>();
                for (int j = i + 1; j < assets.Length; j++)
                    if (assets[j] != null)
                        assets[i].fallbackFontAssetTable.Add(assets[j]);
                EditorUtility.SetDirty(assets[i]);
            }
        }
    }
}
