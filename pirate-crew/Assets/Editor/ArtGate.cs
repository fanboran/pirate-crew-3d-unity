using System;
using System.Collections.Generic;
using PirateCrew.PirateCrew.Ambient;
using PirateCrew.PirateCrew.Fx;
using PirateCrew.PirateCrew.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 一键资产管线：按依赖顺序串联十个波次留下的构建入口，产出"可直接评审"的完整工程。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/管线/一键构建全部资产与场景
    ///   无头: -batchmode -nographics -quit
    ///         -projectPath &lt;工程&gt;
    ///         -executeMethod PirateCrew.EditorTools.ArtGate.BuildAll
    ///         -logFile -
    ///
    /// 【为什么需要它】十个美术/玩法波次各自只提供 BuildAll 入口，且互相有顺序依赖
    /// （材质先于场景、字体先于 HUD、角色预制体先于战斗场景、M3 最后重写 Build Settings）。
    /// 手工按顺序点十个菜单既易漏也易错序；本类把顺序固化成代码。
    ///
    /// 【错误处理】每步独立 try/catch，**不中途静默吞异常**：先跑完全部步骤（能跑的都跑），
    /// 最后汇总全部失败步骤名 + 异常，统一 Debug.LogError 并抛出，
    /// 让 -executeMethod 以非零退出码收尾（CI / 协调者脚本据此判成败）。
    ///
    /// 【步骤顺序与理由】
    ///   ⓪ 运行时 shader 入构建保障         把只被运行时 Shader.Find 取用的 shader 写进
    ///                                    Always Included Shaders——**必须最先**，后续构建播放器才会带它们
    ///                                    （否则按名查找落空 → 洋红，见 EnsureRuntimeShadersIncluded）
    ///   ⓪.5 程序化材质噪声贴图            沙/草/岩三族 albedo+法线共 6 张 256²（MaterialNoiseBuilder）。
    ///                                    **必须在 ① 之前**：① 的 10 个环境材质要引用它们；
    ///                                    材质生成时贴图若不存在，会把细节强度置 0（画面退回纯色，
    ///                                    P-9/P-10 不达标）——所以这一步失败必须让管线整体失败。
    ///   ① BattleSceneLighting.BuildAll   环境材质 / 后处理 Volume / URP 设置——场景与材质引用的前置资产
    ///                                    （内部也会幂等地再调一次 MaterialNoiseBuilder，两者不冲突）
    ///   ①.5 SkyAssetBuilder.BuildAll     三档天空盒材质（视觉审计遗留 #6 的资产层；只出资产，
    ///                                    **不写 RenderSettings**——接线要等 A–F 实拍转正，理由见该脚本头）
    ///   ② FontAssetBuilder.BuildAll      TMP 中文字体——HUD/菜单文本的字形前置
    ///   ③ FxAssetBuilder.BuildAll        特效贴图 / 材质（+ Always Included Shaders）
    ///   ④ AudioAssetBuilder.BuildAll     wav 导入设置（落 Resources/PirateCrewAudio，运行时资产优先）
    ///   ⑤ AmbientAssetBuilder.BuildAll   活物网格 / 材质（AmbientDirector 运行时引用）
    ///   ⑥ CrewVisualPrefabBuilder.BuildAll 角色网格 / 材质 / 7 个职业预制体——M2 要按职业装载
    ///   ⑦ M2BattleSceneSetup.BuildAll    战斗场景（内部再调 BattleSceneLighting、BattleUiTheme.Apply，
    ///                                    并做 crewVisualPrefabs / 相机 battle /
    ///                                    AmbientDirector 三项接线）。**推倒重建**：烘进场景的手工件
    ///                                    （空岛 / 资产表接线）都会被洗掉，必须排在它后面
    ///   ⑦.5 空岛样板件摆入战斗场景       FloatingIslandShowcaseMenu.PlaceIntoBattleCenter——第 3 关地面；
    ///                                    必须在 ⑦ 之后（否则被洗掉，cb3b388 同类教训）
    ///   ⑧ HudMinimapSceneSetup.WireMinimap 小地图增量接线（需 ⑦ 的 Battle.unity 已存在；只接线不改样式）
    ///   ⑨ SceneSetup.BuildAll            M1 菜单 / 引导场景批量重建
    ///   ⑩ M3SceneSetup.BuildAll          M3 管理场景——**必须最后**，它会重写 Build Settings 场景列表
    ///   ⑩.5 WorldKit 资产表 + 海面材质   WorldMapAssetSetBuilder.BuildAll——打开 Battle 写入
    ///                                    worldMapAssetSet / worldOceanMaterial 两个场景引用（链尾收口）；
    ///                                    放 ⑩ 后因为它要改场景，而 ⑦ 会重烘场景、先写必被洗掉
    ///
    /// 【复核提示】`PirateSurface` / `PirateTerrain` 的 `_DebugMode` 档 5 = 湿掩码、档 6 = 沙纹、
    /// 档 7 = 细节贴图（r3 复验 N4 返工新增），编辑器里可把沙/地形材质临时切到这几档快速验证：
    /// 档 5 判据 = 水线 y≤-0.2 灰度 &gt;0.7、台顶 y≥+0.25 灰度 &lt;0.05；档 6 看沙纹是否平行水线且不过强；
    /// 档 7 判据 = 200×200 窗灰度 std &gt; 0.02（死平 = 强度为 0 或贴图未生成）。
    /// 复核完把 `_DebugMode` 切回 0（本管线每次重跑都会写回 0，不会残留调试档）。
    /// </summary>
    public static class ArtGate
    {
        const string MenuPath = "PirateCrew/管线/一键构建全部资产与场景";

        /// <summary>一个构建步骤：中文名（用于日志/错误汇总）+ 入口委托。</summary>
        struct Step
        {
            public string Name;
            public Action Action;

            public Step(string name, Action action)
            {
                Name = name;
                Action = action;
            }
        }

        /// <summary>按依赖顺序构建全部资产与场景；任一步失败不影响后续步骤，最后统一报错。</summary>
        [MenuItem(MenuPath, false, 0)]
        public static void BuildAll()
        {
            // 顺序即依赖：见类头注释的十步理由。改顺序前先想清楚 M3 为什么必须最后。
            var steps = new List<Step>
            {
                new Step("⓪ 运行时 shader 入构建保障", EnsureRuntimeShadersIncluded),
                new Step("⓪.5 程序化材质噪声贴图（沙/草/岩 albedo+法线）", BuildMaterialNoiseTextures),
                new Step("① 渲染基础（环境材质 / Volume / URP）", BattleSceneLighting.BuildAll),
                new Step("①.5 天空盒三档材质（遗留 #6 资产层，不接线）", SkyAssetBuilder.BuildAll),
                new Step("② TMP 中文字体", FontAssetBuilder.BuildAll),
                new Step("③ 特效贴图 / 材质", FxAssetBuilder.BuildAll),
                new Step("④ 音频 wav 导入设置", AudioAssetBuilder.BuildAll),
                new Step("⑤ 活物网格 / 材质", AmbientAssetBuilder.BuildAll),
                new Step("⑥ 角色网格 / 材质 / 7 职业预制体", CrewVisualPrefabBuilder.BuildAll),
                new Step("⑥.5 水面障碍图烘焙（⑦ 的 WaterSimulationDriver 要引用它）", WaterAssetBuilder.BakeObstacleMap),
                new Step("⑦ 战斗场景（含 1-3 项接线 + 水体组件）", M2BattleSceneSetup.BuildAll),
                new Step("⑦.5 空岛样板件摆入战斗场景（第 3 关地面）", FloatingIslandShowcaseMenu.PlaceIntoBattleCenter),
                new Step("⑧ 小地图增量接线", HudMinimapSceneSetup.WireMinimap),
                new Step("⑨ M1 菜单 / 引导场景", SceneSetup.BuildAll),
                new Step("⑩ M3 管理场景（必须最后，重写 Build Settings）", M3SceneSetup.BuildAll),
                new Step("⑩.5 WorldKit 资产表 + 海面材质接线（链尾收口）", WorldMapAssetSetBuilder.BuildAll),
            };

            var failures = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Debug.Log("[ArtGate] 开始一键构建，共 " + steps.Count + " 步。\n"
                + "  注意：需要图形界面渲染的评审出图**不在此管线内**"
                + "（-nographics 下无渲染路径），请另开编辑器会话跑菜单 PirateCrew/美术评审。");

            for (int i = 0; i < steps.Count; i++)
            {
                Step step = steps[i];
                double stepStart = sw.Elapsed.TotalSeconds;

                try
                {
                    EditorUtility.DisplayProgressBar("ArtGate 一键构建", step.Name, (float)i / steps.Count);
                    step.Action?.Invoke();

                    Debug.Log("[ArtGate] " + step.Name + " 完成（"
                        + (sw.Elapsed.TotalSeconds - stepStart).ToString("0.0") + "s）");
                }
                catch (Exception e)
                {
                    // 不中断：继续跑后续步骤，最后统一汇总（可能只是某一步的稀疏异常）。
                    failures.Add(step.Name + " → " + e.GetType().Name + ": " + e.Message);
                    Debug.LogError("[ArtGate] " + step.Name + " 失败（继续执行后续步骤）：\n" + e);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            sw.Stop();

            if (failures.Count > 0)
            {
                string summary = "[ArtGate] 一键构建结束：失败 " + failures.Count + "/" + steps.Count
                    + " 步，用时 " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s。\n  - "
                    + string.Join("\n  - ", failures.ToArray());
                Debug.LogError(summary);
                throw new Exception(summary);
            }

            Debug.Log("[ArtGate] 一键构建全部完成：" + steps.Count + " 步均成功，用时 "
                + sw.Elapsed.TotalSeconds.ToString("0.0") + "s。\n"
                + "  战斗场景: Assets/Scenes/Battle.unity（Build Settings 以 M3 重建后的列表为准）\n"
                + "  评审出图: 另开有图形界面的编辑器会话，跑菜单 PirateCrew/美术评审/采集评审图。");
        }

        // ------------------------------------------------------------------
        // ⓪ 运行时 shader 入构建保障
        // ------------------------------------------------------------------

        /// <summary>
        /// 只被运行时 <c>Shader.Find</c> 按名字取用的 shader 名（出处集中在各模块常量，不散落魔法字符串）。
        ///
        /// 【入榜判据】运行时 Find 的 shader 里，**没有被"可达"材质资产引用**的那些（可达 = 该 .mat 被
        /// 场景/Prefab/Renderer 资产引用）。实测（r2 播放器出图）：
        ///   · Ambient/Glow、Ambient/Wind —— <c>Assets/Art/Materials/Ambient/*.mat</c> 虽然引用了它们，
        ///     但这些 .mat 是**孤儿资产**（0 个场景/Prefab 引用），构包时随 shader 一起被剥离 →
        ///     Player.log 6 条「[Ambient] 找不到 shader」→ 回退链落空 → 洋红。**r2 洋红的根因**。
        ///   · Fx/Additive、Fx/Alpha —— 有 .mat 引用，且 FxAssetBuilder 早已登记进本列表（此处幂等跳过）。
        ///   · PirateOutlinePost —— 无 .mat 引用，被 URP Renderer 资产字段引用，理论上可达；仍登记兜底。
        /// 不入榜的运行时 Find 名及理由：PirateOutline / PirateSurface / URP-Unlit / URP-Lit 都有 .mat 引用；
        /// Standard / Sprites/Default / Unlit/Transparent 是引擎内置 shader（Sprites/Default 由
        /// GraphicsSettings.m_SpritesDefaultMaterial 常驻），只为 URP 工程里的最后兜底，不属于本项目产线。
        /// 【URP/Particles-Unlit 曾入榜后移除（r3 轮实证）】：Always Included 会强制编译该 shader 的
        /// 全变体，在无头构建环境连爆 20 条 d3d11 编译 OOM（BSDF/Common.hlsl 解析内存）；
        /// 而它只是 FxMaterials 的回落档——主 FX shader 已在本列表保证入包，回落永不触发。
        ///
        /// 【PirateCrew/Skybox/PirateGradientSky 为什么在榜】它是"孤儿材质"那一条的**现行实例**：
        /// 三张天空盒材质（Assets/Art/Materials/Sky/*.mat，由 SkyAssetBuilder 生成）目前 0 个场景引用
        /// （视觉审计遗留 #6 的接线轮才会写 RenderSettings.skybox），构包时会随 shader 一起被剥离。
        /// 那样本工程唯一的 shader 编译门禁（构建播放器 → grep "shader error"）会**因为根本没编译而报 0 条**
        /// ——"没编译 = 没错误"的假绿。登记进榜让门禁真正覆盖这个 shader；接线后它被场景材质可达引用，
        /// 留榜也无害（同 Fx/Additive 的先例）。
        /// </summary>
        static readonly string[] RuntimeFindShaderNames =
        {
            AmbientMaterialSet.GlowShaderName,
            AmbientMaterialSet.WindShaderName,
            FxMaterials.AdditiveShaderName,
            FxMaterials.AlphaShaderName,
            OutlineRendererFeature.OutlinePostShaderName,
            SkyAssetBuilder.SkyShaderName,
        };

        /// <summary>
        /// 把 <see cref="RuntimeFindShaderNames"/> 追加进 Graphics Settings 的 Always Included Shaders（幂等）。
        ///
        /// 【为什么是 Always Included 而不是 Preloaded Shaders】Graphics Settings 的 "Preloaded Shaders"
        /// 只接受 <c>ShaderVariantCollection</c> 资产（预热指定变体），**收不了单个 <c>Shader</c>**；
        /// 能让"按名字 Shader.Find 在成品播放器里成立"的字段是 "Always Included Shaders"
        /// （<c>m_AlwaysIncludedShaders</c>，类型 <c>Shader[]</c>；Unity 2022.3 手册 class-GraphicsSettings
        /// 定义为 "a list of shaders for which Unity includes all possible variants in every build"）。
        ///
        /// 【为什么放在管线最前】本步只改 ProjectSettings，成品播放器在管线之后才构建——
        /// 先登记后构包才生效（对应规程 G-6「无洋红」）。
        ///
        /// 【幂等】已在列表中的 shader 跳过，可反复重跑整条管线。
        /// 取不到资产/字段时抛异常，交回 BuildAll 的步骤 try/catch 汇总（不静默吞）。
        /// </summary>
        static void EnsureRuntimeShadersIncluded()
        {
            UnityEngine.Object settings = GraphicsSettings.GetGraphicsSettings();
            if (settings == null)
                throw new InvalidOperationException("取不到 GraphicsSettings 资产，无法登记 Always Included Shaders。");

            var so = new SerializedObject(settings);
            SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
                throw new InvalidOperationException("GraphicsSettings 上找不到 m_AlwaysIncludedShaders 数组。");

            int added = 0;
            var missing = new List<string>();
            for (int i = 0; i < RuntimeFindShaderNames.Length; i++)
            {
                string shaderName = RuntimeFindShaderNames[i];
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    // 编辑器里就找不到 = shader 编译失败/未导入；先修 shader，登记无从谈起。
                    missing.Add(shaderName);
                    continue;
                }

                if (IsShaderRegistered(list, shader))
                    continue;

                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                added++;
            }

            if (added > 0)
                so.ApplyModifiedProperties();

            if (missing.Count > 0)
            {
                Debug.LogWarning("[ArtGate] 运行时 shader 入构建保障：编辑器里找不到 "
                    + string.Join("、", missing.ToArray())
                    + "。请先在编辑器里 read_console 确认 shader 编译无错；未登记的 shader"
                    + "会让成品播放器出洋红（规程 G-6）。");
            }

            Debug.Log("[ArtGate] 运行时 shader 入构建保障：新增 " + added + " 个 Always Included Shader，"
                + "候选 " + RuntimeFindShaderNames.Length + " 个"
                + (missing.Count > 0 ? "，缺失 " + missing.Count + " 个" : "") + "。");
        }

        /// <summary>列表里是否已有该 shader（按引用比较，幂等判据）。</summary>
        static bool IsShaderRegistered(SerializedProperty list, Shader shader)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // ⓪.5 程序化材质噪声贴图
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成沙/草/岩三族程序化细节贴图（6 张 256²，幂等）。
        ///
        /// 【为什么是管线里的独立一步，而不是只塞进 ①】<see cref="BattleSceneLighting.BuildAll"/>
        /// 内部也会调一次（保证它作为独立无头入口时自给自足；那里对生成失败只记 Error 继续出材质），
        /// 但管线必须**显式**有这一步，才能把贴图生成的失败记成 ArtGate 的失败步骤 → 非零退出码。
        /// 否则贴图没生成时画面会静默退回"纯色 + 噪声"（P-9/P-10 悄悄不达标，只剩一条 Warning 容易被漏看）。
        ///
        /// 【自检】<see cref="MaterialNoiseBuilder.Build"/> 对单张失败只记 Warning（不中断 batchmode），
        /// 而这里承担门禁职责：跑完确认 6 张都能加载，缺一即抛（由 BuildAll 的步骤 try/catch 汇总）。
        ///
        /// 【顺序】必须早于 ①：① 生成的 10 个环境材质要引用这 6 张贴图。
        /// </summary>
        static void BuildMaterialNoiseTextures()
        {
            MaterialNoiseBuilder.Build(false);

            var missing = new List<string>();
            for (int i = 0; i < MaterialNoiseBuilder.AllKinds.Length; i++)
            {
                MaterialNoiseBuilder.NoiseKind kind = MaterialNoiseBuilder.AllKinds[i];
                if (MaterialNoiseBuilder.Load(kind) == null)
                    missing.Add(MaterialNoiseBuilder.PathOf(kind));
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException("程序化材质噪声贴图缺失 " + missing.Count + "/"
                    + MaterialNoiseBuilder.AllKinds.Length + " 张：\n  - "
                    + string.Join("\n  - ", missing.ToArray())
                    + "\n先在有渲染路径的编辑器里跑菜单 PirateCrew/渲染/生成程序化材质噪声贴图 并看 Console。");
            }
        }
    }
}
