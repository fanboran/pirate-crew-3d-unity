using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **Battle 场景装配子链的唯一入口**：把"只碰 Battle 场景"的八步收成一条有名字、有顺序、
    /// 可单独重跑的函数，链尾是折叠（场景 → `BattleRig.prefab` 的实例）。
    ///
    /// 【为什么把顺序写成代码】这八步原本散在 <see cref="ArtGate"/> 的步骤表与文档里，
    /// 顺序靠人记（审计报告 F-1：`①重建` 之后必须 `②接线`，而没有任何东西钉住它）。
    /// 顺序是**真实的依赖**——每一步都读写上一步落在场景里的东西——所以它应该是一条语句接一条语句，
    /// 而不是一张要背的表。收进本类之后：
    ///   · 违反顺序不再是"可能发生的事故"，而是"把两行代码写反"，改动会在 diff 里显形；
    ///   · 只重跑 Battle 子链变成一条命令，不必跑完整条资产管线（省两分钟的材质/音频/字体重建）；
    ///   · 折叠步骤的位置被代码固定为最后一步——它之后任何往场景写字段的动作都会变成
    ///     "Prefab 实例覆盖"，那是本工程明确要避免的状态（见 <see cref="ScenePrefabCollapse"/>）。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/重跑 Battle 装配链
    ///   无头: -batchmode -nographics -quit -projectPath &lt;P&gt;
    ///         -executeMethod PirateCrew.EditorTools.BattleScenePipeline.BuildFromCommandLine
    ///
    /// 【失败处理】与 <see cref="ArtGate"/> 同口径：每步独立 try/catch，能跑的都跑完，
    /// 最后汇总全部失败步骤名 + 异常再抛出（批处理据此拿到非零退出码）。
    /// </summary>
    public static class BattleScenePipeline
    {
        /// <summary>一个装配步骤：中文名（日志/错误汇总用）+ 入口委托。</summary>
        public sealed class Step
        {
            public string Name;

            public Action Action;

            public Step(string name, Action action)
            {
                Name = name;
                Action = action;
            }
        }

        /// <summary>
        /// 装配步骤表：**顺序即依赖**，逐条理由写在每一项上方。
        /// 契约测试（<c>Tests/Battle/SceneAssemblyContractTests</c>）把这份清单与顺序钉死——
        /// 调整顺序或增删步骤会红，必须是有意为之并同步测试。
        /// </summary>
        public static readonly Step[] Steps =
        {
            // ① 全量重建：NewScene(EmptyScene) → 造全部对象 → SaveScene。
            //    它能安全地扔掉旧场景，是因为后面每一步都会把该补的东西补齐；正因如此它必须排第一。
            new Step("① 重建 Battle 场景骨架（BattleSceneSetup.BuildAll）",
                BattleSceneSetup.BuildAll),

            // ② 空岛样板件摆入（第 3 关地面）。必须在 ① 之后：① 的 NewScene 会把它洗掉。
            new Step("② 空岛样板件摆入（FloatingIslandShowcaseMenu.PlaceIntoBattleCenter）",
                FloatingIslandShowcaseMenu.PlaceIntoBattleCenter),

            // ③ 样板场景件烘焙（云场 / 碎岛 / 危险线 → prefab + 接线）。同样依赖 ① 的场景。
            new Step("③ 样板场景件烘焙 + 接线（SceneArtBaker.BuildAll）",
                SceneArtBaker.BuildAll),

            // ④ 场景资产总清单：把 SceneKit / WorldKit 的上游资产登记进清单资产（供运行时按名取用）。
            new Step("④ 场景资产总清单（SceneAssetManifestBuilder.BuildAll）",
                SceneAssetManifestBuilder.BuildAll),

            // ⑤ 小地图增量接线：补 MinimapPanel 的 BattleMinimap 组件与 unitRoots/dotLayer/tileLayer/terrain。
            //    必须在 ① 之后（① 重建场景会洗掉这份接线，c78fdea 海图空白三轮的根因）。
            new Step("⑤ 小地图接线（HudMinimapSceneSetup.WireMinimap）",
                HudMinimapSceneSetup.WireMinimap),

            // ⑥ 运行期查找清退的三条显式接线（BattleCameraDriver.aimThrow /
            //    BattleHud.cameraController / WaterSimulationDriver.sunLight）。
            //    同样必须在 ① 之后。**注意**：本步原先不在 ArtGate 的步骤表里——
            //    整条资产管线重跑后场景会缺这三条线，只是运行时有一次性的 Find 兜底把它们盖住了，
            //    于是缺陷表现为"安静地多跑几次全场景查找"而不是报错。收进本链即修复。
            new Step("⑥ 运行期查找接线（BattleLookupWiring.WireScene）", WireLookupsOrThrow),

            // ⑦ WorldKit 资产表 + 海面材质接线（写 BattleController.worldMapAssetSet / worldOceanMaterial）。
            //    原先排在 ArtGate 末步（⑩.5），理由是"⑦ 会重烘场景、先写必被洗掉"——
            //    它的真实约束就是"在 ① 之后"，与 ⑨⑩（M1/M3 场景、Build Settings）无关，故收进本链。
            new Step("⑦ WorldKit 资产表 + 海面材质接线（WorldMapAssetSetBuilder.BuildAll）",
                WorldMapAssetSetBuilder.BuildAll),

            // ⑧ 折叠：全部根收进载体根 → 存 BattleRig.prefab → 场景只留一个实例。
            //    **必须是最后一步**：折叠之后任何往场景写字段的动作都会产生 Prefab 实例覆盖。
            new Step("⑧ 折叠为 BattleRig.prefab 实例（ScenePrefabCollapse.CollapseBattle）",
                ScenePrefabCollapse.CollapseBattle),
        };

        /// <summary>
        /// 第 ⑥ 步的包装：<see cref="BattleLookupWiring.WireScene"/> 用"返回问题清单"报告成败，
        /// 这里把它转成"失败即抛"，管线才不会在接线缺失的情况下继续往下折场景。
        /// </summary>
        static void WireLookupsOrThrow()
        {
            List<string> problems = BattleLookupWiring.WireScene();
            if (problems.Count == 0)
                return;

            throw new InvalidOperationException("运行期查找接线失败 " + problems.Count + " 项：\n  · "
                + string.Join("\n  · ", problems));
        }

        /// <summary>步骤名清单（契约测试按它钉顺序；也是 Describe 的输出源）。</summary>
        public static string[] StepNames()
        {
            var names = new string[Steps.Length];
            for (int i = 0; i < Steps.Length; i++)
                names[i] = Steps[i].Name;
            return names;
        }

        /// <summary>按 <see cref="Steps"/> 的顺序跑完整条子链；任一步失败不影响后续步骤，最后统一抛出。</summary>
        [UnityEditor.MenuItem("PirateCrew/Scenes/重跑 Battle 装配链", false, 20)]
        public static void Build()
        {
            var failures = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Debug.Log("[BattleScenePipeline] 开始装配 Battle 子链，共 " + Steps.Length + " 步。");

            for (int i = 0; i < Steps.Length; i++)
            {
                Step step = Steps[i];
                double t0 = sw.Elapsed.TotalSeconds;
                try
                {
                    step.Action();
                    Debug.Log("[BattleScenePipeline] ✓ " + step.Name
                              + "（" + (sw.Elapsed.TotalSeconds - t0).ToString("F1") + "s）");
                }
                catch (Exception e)
                {
                    failures.Add(step.Name + " → " + e.GetType().Name + ": " + e.Message);
                    Debug.LogError("[BattleScenePipeline] ✗ " + step.Name + "\n" + e);
                }
            }

            Debug.Log("[BattleScenePipeline] 子链结束，耗时 "
                      + sw.Elapsed.TotalSeconds.ToString("F1") + "s，失败 " + failures.Count + " 步。");

            if (failures.Count == 0)
                return;

            throw new InvalidOperationException(
                "[BattleScenePipeline] " + failures.Count + " 步失败：\n  · " + string.Join("\n  · ", failures));
        }

        /// <summary>无头入口：跑子链并以退出码报告成败（CI / 协调者脚本据此判成败）。</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
                if (Application.isBatchMode)
                    UnityEditor.EditorApplication.Exit(1);
                return;
            }

            if (Application.isBatchMode)
                UnityEditor.EditorApplication.Exit(0);
        }
    }
}
