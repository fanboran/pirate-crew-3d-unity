using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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
    ///   ① BattleSceneLighting.BuildAll   环境材质 / 后处理 Volume / URP 设置——场景与材质引用的前置资产
    ///   ② FontAssetBuilder.BuildAll      TMP 中文字体——HUD/菜单文本的字形前置
    ///   ③ FxAssetBuilder.BuildAll        特效贴图 / 材质（+ Always Included Shaders）
    ///   ④ AudioAssetBuilder.BuildAll     wav 导入设置（落 Resources/PirateCrewAudio，运行时资产优先）
    ///   ⑤ AmbientAssetBuilder.BuildAll   活物网格 / 材质（AmbientDirector 运行时引用）
    ///   ⑥ CrewVisualPrefabBuilder.BuildAll 角色网格 / 材质 / 7 个职业预制体——M2 要按职业装载
    ///   ⑦ M2BattleSceneSetup.BuildAll    战斗场景（内部再调 BattleSceneLighting、SceneArtBuilder.Apply、
    ///                                    BattleUiTheme.Apply，并做 crewVisualPrefabs / 相机 battle /
    ///                                    AmbientDirector 三项接线）
    ///   ⑧ HudMinimapSceneSetup.WireMinimap 小地图增量接线（需 ⑦ 的 Battle.unity 已存在；只接线不改样式）
    ///   ⑨ SceneSetup.BuildAll            M1 菜单 / 引导场景批量重建
    ///   ⑩ M3SceneSetup.BuildAll          M3 管理场景——**必须最后**，它会重写 Build Settings 场景列表
    ///
    /// 【复核提示】`PirateSurface` / `PirateTerrain` 的 `_DebugMode` 档 5 = 湿掩码、档 6 = 沙纹，
    /// 编辑器里可把沙/地形材质临时切到这两档快速验证湿带正确性：
    /// 档 5 判据 = 水线 y≤-0.2 灰度 &gt;0.7、台顶 y≥+0.25 灰度 &lt;0.05；档 6 看沙纹是否平行水线且不过强。
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
                new Step("① 渲染基础（环境材质 / Volume / URP）", BattleSceneLighting.BuildAll),
                new Step("② TMP 中文字体", FontAssetBuilder.BuildAll),
                new Step("③ 特效贴图 / 材质", FxAssetBuilder.BuildAll),
                new Step("④ 音频 wav 导入设置", AudioAssetBuilder.BuildAll),
                new Step("⑤ 活物网格 / 材质", AmbientAssetBuilder.BuildAll),
                new Step("⑥ 角色网格 / 材质 / 7 职业预制体", CrewVisualPrefabBuilder.BuildAll),
                new Step("⑥.5 水面障碍图烘焙（⑦ 的 WaterSimulationDriver 要引用它）", WaterAssetBuilder.BakeObstacleMap),
                new Step("⑦ 战斗场景（含 1-3 项接线 + 水体组件）", M2BattleSceneSetup.BuildAll),
                new Step("⑧ 小地图增量接线", HudMinimapSceneSetup.WireMinimap),
                new Step("⑨ M1 菜单 / 引导场景", SceneSetup.BuildAll),
                new Step("⑩ M3 管理场景（必须最后，重写 Build Settings）", M3SceneSetup.BuildAll),
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
    }
}
