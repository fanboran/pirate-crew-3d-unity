using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools.Art
{
    /// <summary>
    /// Toon 材质工厂：把「131 个材质从写实栈换到 Toon 栈」变成一条命令（任务书 §3 表 M2e 行）。
    ///
    /// 【为什么不直接全量换血】换血会立刻改变现役画面，而本轮**没有出图验收环节**
    /// （出图由协调者串行驱动，见任务书 §8 门禁链）。所以本工厂做成**可重复、可预览、可分批**：
    /// <list type="number">
    ///   <item>扫描：列出所有"写实栈"材质（按 shader 名判定）与建议分批；</item>
    ///   <item>预览：为某一批算出「材质 → 目标 shader + 从板上取哪个基色/暗部色」的完整清单并打印，
    ///         不写盘；</item>
    ///   <item>应用：只动被选中的那一批，幂等（重跑收敛到同一结果），并打印改动对账。</item>
    /// </list>
    ///
    /// 【参数从哪里来（数据与代码分离）】
    ///   · 基色 / 暗部色 / 墨色：全部从 <see cref="PaletteAsset"/>（JSON 真源派生）取，
    ///     代码里**不写色值**；
    ///   · 归色规则：材质现有 `_BaseColor`（或 `_Color`）经 OkLab 最近邻落到板上某个槽位，
    ///     再按族查"暗部槽位表"得到替换式暗部色（渲染篇 §4.2：暗部是**替换**而非乘暗）；
    ///   · 着色参数（阈值/羽化/抖动/线宽）：本文件的常量表，是 M1 裁决点 #2/#3 的落点。
    ///
    /// 【幂等与可回退】换血前用 <c>Undo.RegisterCompleteObjectUndo</c> 记一笔（编辑器内可 Ctrl+Z）；
    /// 已经是 Toon shader 的材质被显式跳过（所以分批可以随便重跑、也可以只跑某一段而不影响其它批）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Art/材质工厂/*
    ///   无头: -executeMethod PirateCrew.EditorTools.Art.ToonMaterialFactory.Scan
    ///         -executeMethod PirateCrew.EditorTools.Art.ToonMaterialFactory.ApplyBatch -batchIndex 1
    /// </summary>
    public static class ToonMaterialFactory
    {
        /// <summary>目标 shader（赛璐璐本体 + 常驻 INK 反壳描边，渲染篇 §4/§5）。</summary>
        public const string ToonShaderName = "PirateCrew/PirateToon";

        /// <summary>写实栈 shader 名单（退役对象，任务书 §3 处置表）。按名判定，不依赖 GUID。</summary>
        static readonly string[] RealisticShaders =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "PirateCrew/PirateSurface",
            "PirateCrew/PirateOutline",
            "PirateCrew/PirateTerrain",
            "PirateCrew/PirateOutlinePost",
            "PirateCrew/PirateWater",
            "PirateCrew/Ocean/PirateOcean",
            "PirateCrew/Ambient/Wind",
            "PirateCrew/Ambient/Glow",
        };

        /// <summary>材质扫描根（Prefab/场景里的引用不在此列 —— 换 shader 是改材质资产，引用自动跟随）。</summary>
        const string MaterialRoot = "Assets/Art/Materials";

        /// <summary>
        /// 分批方案（顺序 = 视觉焦点优先级，资产篇 §7 的翻新顺序同源）。
        /// 一个材质只属于一批：按目录判定，先命中先归属。
        /// </summary>
        static readonly string[][] Batches =
        {
            new[] { "Toon" },                                   // 0：已是 Toon 的试点材质（只做校验，不出改动）
            new[] { "Environment" },                            // 1：视线焦点（岛台/船体/地面/水）
            new[] { "Scene" },                                  // 2：场景陈设
            new[] { "Crew" },                                   // 3：单位（模型不改铁律，只换材质）
            new[] { "Lowpoly" },                                // 4：低模配件
            new[] { "Fx", "Sky", "UI", "Ambient" },             // 5：特效/天空/UI/氛围（多为加色/透明，风险与画风耦合最高）
        };

        /// <summary>
        /// 族 → 替换式暗部槽位（渲染篇 §4.2：暗部是**板上替换色**，不是把亮部乘暗）。
        /// 表里没有的族统一回落到 SHADOW_COOL（偏紫冷，与"左上暖光"对位）。
        /// </summary>
        static readonly Dictionary<string, string> ShadowSlotByFamily = new Dictionary<string, string>
        {
            { "SAND", "SHADOW_COOL" },
            { "ROCK", "SHADOW_COOL" },
            { "WOOD", "SHADOW_WARM" },
            { "GRASS", "GRASS_DARK" },
            { "SEA", "SEA_DEEP" },
            { "METAL", "IRON_DARK" },
            { "CLOTH", "SHADOW_WARM" },
            { "PROP", "SHADOW_WARM" },
            { "FACTION_RED", "HERO_RED_DEEP" },
            { "FACTION_BLUE", "HERO_BLUE_DEEP" },
            { "EMISSIVE", "SHADOW_WARM" },
            { "ACCENT", "SHADOW_WARM" },
            { "FARFIELD", "SHADOW_COOL" },
            { "INK", "SHADOW_DEEP" },
            { "UI", "UI_BEVEL_LO" },
            { "NIGHT", "SHADOW_DEEP" },
        };

        // --- 着色参数（M1 裁决点 #2/#3 的落点；改动即改试点观感，必须出对照图）---
        const float ShadowThreshold = 0.5f;   // 色带切分点（渲染篇 §4.1）
        const float ShadowFeather = 0f;       // 硬切主路径（裁决 #2 默认硬切）
        const float DitherStrength = 0f;      // 默认关（参照图无有序抖动；资产篇 §4）
        const float OutlinePixels = 2f;       // 试点实测 1px 经 3× 点降采不可见（裁决 #3）
        const float EmissiveStrength = 0f;

        // ------------------------------------------------------------------
        // ① 扫描
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/Art/材质工厂/① 扫描：写实栈材质清单与分批", priority = 140)]
        public static void Scan()
        {
            var realistic = new List<string>();
            var alreadyToon = new List<string>();
            var unknown = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null)
                    continue;
                string shader = mat.shader.name;
                if (shader == ToonShaderName)
                    alreadyToon.Add(path);
                else if (System.Array.IndexOf(RealisticShaders, shader) >= 0)
                    realistic.Add(path + "  [" + shader + "]");
                else
                    unknown.Add(path + "  [" + shader + "]");
            }

            var sb = new StringBuilder();
            sb.Append("[ToonMaterialFactory] 扫描 ").Append(MaterialRoot).Append("：")
              .Append("写实栈 ").Append(realistic.Count)
              .Append(" / 已是 Toon ").Append(alreadyToon.Count)
              .Append(" / 名单外 ").Append(unknown.Count).Append(" 个材质\n");

            for (int b = 0; b < Batches.Length; b++)
            {
                var plan = BuildPlan(b, null);
                sb.Append("  第 ").Append(b).Append(" 批 [").Append(string.Join("+", Batches[b]))
                  .Append("]：").Append(plan.Count).Append(" 个待换\n");
            }

            sb.Append("  写实栈明细（前 20）：\n");
            for (int i = 0; i < realistic.Count && i < 20; i++)
                sb.Append("    ").Append(realistic[i]).Append('\n');
            if (unknown.Count > 0)
            {
                sb.Append("  名单外 shader（需人工归类，不在本工厂的判定内）：\n");
                for (int i = 0; i < unknown.Count && i < 20; i++)
                    sb.Append("    ").Append(unknown[i]).Append('\n');
            }

            Debug.Log(sb.ToString());
        }

        [MenuItem("PirateCrew/Art/调色板/自检 OkLab 转换", priority = 103)]
        public static void SelfCheckOklab()
        {
            float worst = ArtPaletteMath.SelfCheckOklab();
            if (worst > 1e-3f)
                Debug.LogError("[ArtPaletteMath] OkLab 官方测试表回归未过：最大偏差 " + worst
                    + " > 0.001（转换写错了，先修 ArtPaletteMath 再用材质工厂）");
            else
                Debug.Log("[ArtPaletteMath] OkLab 官方测试表回归通过：4 组 XYZ 最大偏差 " + worst + " ≤ 0.001");
        }

        // ------------------------------------------------------------------
        // ② 预览 / ③ 应用
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/Art/材质工厂/② 预览：打印分批换血清单（不写盘）", priority = 141)]
        public static void PreviewAll()
        {
            var sb = new StringBuilder("[ToonMaterialFactory] 预览（不写盘）\n");
            for (int b = 0; b < Batches.Length; b++)
            {
                sb.Append("== 第 ").Append(b).Append(" 批 [").Append(string.Join("+", Batches[b]))
                  .Append("] ==\n");
                sb.Append(DescribePlan(b));
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>无头入口：应用第 N 批。<c>-executeMethod ... -batchIndex 1</c></summary>
        public static void ApplyBatch()
        {
            int index = ReadBatchIndexArg();
            if (index < 0)
            {
                Debug.LogError("[ToonMaterialFactory] 无头调用必须带 -batchIndex <0.." + (Batches.Length - 1)
                    + ">（本工厂刻意不提供「全量换血」入口 —— 换血会改现役画面，必须分批出图验收）");
                return;
            }
            Apply(index, true);
        }

        /// <summary>应用某一批（幂等）。<paramref name="report"/> = 打印逐材质对账。</summary>
        public static int Apply(int batchIndex, bool report)
        {
            if (batchIndex < 0 || batchIndex >= Batches.Length)
            {
                Debug.LogError("[ToonMaterialFactory] 批次越界：" + batchIndex);
                return 0;
            }

            PaletteAsset palette = LoadPalette();
            if (palette == null)
                return 0;

            var plan = BuildPlan(batchIndex, palette);
            if (plan.Count == 0)
            {
                Debug.Log("[ToonMaterialFactory] 第 " + batchIndex + " 批没有待换材质（已是 Toon 或目录为空）。");
                return 0;
            }

            if (!Application.isBatchMode)
            {
                bool ok = EditorUtility.DisplayDialog("Toon 材质工厂",
                    "第 " + batchIndex + " 批 [" + string.Join("+", Batches[batchIndex]) + "] 共 "
                    + plan.Count + " 个材质将被换成 " + ToonShaderName + " 并重取板色。\n\n"
                    + "本批不含出图验收：换完请用 read_console 确认 0 shader error，再出图判据。\n\n继续？",
                    "应用", "取消");
                if (!ok)
                {
                    Debug.Log("[ToonMaterialFactory] 用户取消，未改动任何材质。");
                    return 0;
                }
            }

            var sb = new StringBuilder();
            sb.Append("[ToonMaterialFactory] 应用第 ").Append(batchIndex).Append(" 批，")
              .Append(plan.Count).Append(" 个材质：\n");

            int applied = 0;
            foreach (MaterialChange change in plan)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(change.path);
                if (mat == null)
                    continue;

                Undo.RegisterCompleteObjectUndo(mat, "Toon 材质换血（第 " + batchIndex + " 批）");
                ApplyToMaterial(mat, change, palette);
                EditorUtility.SetDirty(mat);
                applied++;
                if (report)
                    sb.Append("  ").Append(change.path)
                      .Append("：").Append(change.fromShader)
                      .Append(" → Toon；基色 ").Append(change.fromHex)
                      .Append(" → ").Append(change.baseSlotId).Append(' ').Append(change.baseHex)
                      .Append("；暗部 → ").Append(change.shadowSlotId).Append(' ').Append(change.shadowHex)
                      .Append('\n');
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            sb.Append("  完成 ").Append(applied).Append('/').Append(plan.Count)
              .Append(" 个。下一步：read_console 查 shader error；出图判据按任务书 §9 三律。");
            Debug.Log(sb.ToString());
            return applied;
        }

        [MenuItem("PirateCrew/Art/材质工厂/③ 应用第 1 批（Environment）", priority = 160)]
        static void ApplyBatch1()
        {
            Apply(1, true);
        }

        [MenuItem("PirateCrew/Art/材质工厂/③ 应用第 2 批（Scene）", priority = 161)]
        static void ApplyBatch2()
        {
            Apply(2, true);
        }

        [MenuItem("PirateCrew/Art/材质工厂/③ 应用第 3 批（Crew）", priority = 162)]
        static void ApplyBatch3()
        {
            Apply(3, true);
        }

        // ------------------------------------------------------------------
        // 内部：计划与执行
        // ------------------------------------------------------------------

        /// <summary>一个材质的换血计划（预览与执行共用同一条数据，所以预览即承诺）。</summary>
        class MaterialChange
        {
            public string path;
            public string fromShader;
            public string fromHex;
            public string baseSlotId;
            public string baseHex;
            public string shadowSlotId;
            public string shadowHex;
        }

        /// <summary>算出某批的换血清单。<paramref name="palette"/> 为 null 时只统计数量（扫描用）。</summary>
        static List<MaterialChange> BuildPlan(int batchIndex, PaletteAsset palette)
        {
            var plan = new List<MaterialChange>();
            if (batchIndex <= 0)
                return plan; // 第 0 批 = 已是 Toon 的试点材质，无需换血

            foreach (string folder in Batches[batchIndex])
            {
                string root = MaterialRoot + "/" + folder;
                if (!AssetDatabase.IsValidFolder(root))
                    continue;

                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null || mat.shader == null)
                        continue;
                    if (mat.shader.name == ToonShaderName)
                        continue;                                     // 幂等：已是 Toon 就跳过
                    if (System.Array.IndexOf(RealisticShaders, mat.shader.name) < 0)
                        continue;                                     // 名单外的 shader 交人工归类

                    if (palette == null)
                    {
                        plan.Add(new MaterialChange { path = path, fromShader = mat.shader.name });
                        continue;
                    }

                    Color current = ReadBaseColor(mat);
                    PaletteSlot baseSlot = ArtPaletteMath.NearestSlot(palette, current);
                    if (baseSlot == null)
                        continue;
                    string shadowId = ResolveShadowSlot(baseSlot.group);
                    PaletteSlot shadowSlot;
                    if (!palette.TryGetSlot(shadowId, out shadowSlot))
                        palette.TryGetSlot("SHADOW_COOL", out shadowSlot);

                    plan.Add(new MaterialChange
                    {
                        path = path,
                        fromShader = mat.shader.name,
                        fromHex = ArtPaletteMath.ToHex(current),
                        baseSlotId = baseSlot.id,
                        baseHex = "#" + baseSlot.hex.ToUpperInvariant(),
                        shadowSlotId = shadowSlot == null ? "(缺)" : shadowSlot.id,
                        shadowHex = shadowSlot == null ? "(缺)" : "#" + shadowSlot.hex.ToUpperInvariant(),
                    });
                }
            }
            return plan;
        }

        static string DescribePlan(int batchIndex)
        {
            var palette = batchIndex <= 0 ? null : LoadPalette();
            var plan = BuildPlan(batchIndex, palette);
            if (plan.Count == 0)
                return "  （无待换材质）\n";

            var sb = new StringBuilder();
            foreach (MaterialChange change in plan)
            {
                if (palette == null)
                {
                    sb.Append("  ").Append(change.path).Append("  [").Append(change.fromShader).Append("]\n");
                    continue;
                }
                sb.Append("  ").Append(change.path)
                  .Append("  ").Append(change.fromHex).Append(" → ")
                  .Append(change.baseSlotId).Append(' ').Append(change.baseHex)
                  .Append("  暗部 ").Append(change.shadowSlotId).Append(' ').Append(change.shadowHex)
                  .Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>把一个材质改成 Toon 栈（渲染篇 §4/§5 的参数口径；只动本工厂负责的键）。</summary>
        static void ApplyToMaterial(Material mat, MaterialChange change, PaletteAsset palette)
        {
            Shader toon = Shader.Find(ToonShaderName);
            if (toon == null)
            {
                Debug.LogError("[ToonMaterialFactory] 找不到 " + ToonShaderName + "（shader 未导入？）");
                return;
            }

            mat.shader = toon;
            mat.SetColor("_BaseColor", palette.ColorOf(change.baseSlotId));
            mat.SetColor("_ShadowColor", palette.ColorOf(change.shadowSlotId));
            mat.SetColor("_InkColor", palette.ColorOf("INK"));   // 3D 描边与 UI 令牌同色（渲染篇 §5）
            mat.SetColor("_EmissiveColor", Color.black);
            mat.SetFloat("_EmissiveStrength", EmissiveStrength);
            mat.SetFloat("_ShadowThreshold", ShadowThreshold);
            mat.SetFloat("_ShadowFeather", ShadowFeather);
            mat.SetFloat("_DitherStrength", DitherStrength);
            mat.SetFloat("_OutlinePixels", OutlinePixels);
            // 描边物 SubShader 是 Geometry+50：队列必须一起覆写，否则会被透明物盖掉（六轮实测结论）
            mat.renderQueue = 2050;
        }

        /// <summary>取材质的基色：Toon 用 _BaseColor，URP/Lit 与自写写实栈用 _BaseColor 或 _Color。</summary>
        static Color ReadBaseColor(Material mat)
        {
            if (mat.HasProperty("_BaseColor"))
                return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color"))
                return mat.GetColor("_Color");
            if (mat.HasProperty("_BaseMap"))
            {
                Texture2D tex = mat.GetTexture("_BaseMap") as Texture2D;
                if (tex != null)
                    return Color.white; // 有贴图时基色无意义（资产篇 §3 时期望贴图承载颜色）
            }
            return Color.white;
        }

        static string ResolveShadowSlot(string family)
        {
            string slot;
            if (family != null && ShadowSlotByFamily.TryGetValue(family, out slot))
                return slot;
            return "SHADOW_COOL";
        }

        static PaletteAsset LoadPalette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<PaletteAsset>(PaletteAssetBuilder.AssetPath);
            if (palette == null || palette.slots == null || palette.slots.Count == 0)
            {
                Debug.LogError("[ToonMaterialFactory] 读不到调色板镜像 " + PaletteAssetBuilder.AssetPath
                    + "；先跑菜单 PirateCrew/Art/调色板/JSON → PaletteAsset（生成镜像）。");
                return null;
            }
            return palette;
        }

        static int ReadBatchIndexArg()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-batchIndex" && int.TryParse(args[i + 1], out int index))
                    return index;
            }
            return -1;
        }
    }
}
