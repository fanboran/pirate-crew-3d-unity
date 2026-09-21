using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.Tests
{
    /// <summary>
    /// **场景装配产物的契约测试**：把"场景处于折叠态"和"装配链的顺序"两件事从文档里的约定
    /// 变成会自己报警的断言。
    ///
    /// 【它在防什么】
    ///   ① 审计报告 F-1：装配链是"重建 → 接线 → … → 折叠"，顺序是**真实依赖**，但原先只写在文档里。
    ///      只跑 <c>M2BattleSceneSetup.BuildAll</c> 而没跑后面几步，结果是"场景能打开、能跑、
    ///      但少了小地图接线/运行期查找接线，并且没折叠"——是静默的，不报错。
    ///      折叠态断言把这种"半跑的链"变成一条红测试。
    ///   ② 折叠态被破坏：有人往场景里手摆对象、或对实例做了覆盖（覆盖会把接线知识散回场景文件，
    ///      正是折叠要消灭的状态）。
    ///   ③ 装配链顺序被改动：<see cref="BattleScenePipeline"/> 的步骤表是**冻结的**，
    ///      增删或换序必须是有意为之并同步改这条测试。
    ///
    /// 【怎么判】加性打开场景，交回**引擎自己**回答四个问题：
    /// 根对象是不是恰好一个、名字对不对、是不是目标 Prefab 的实例、
    /// <c>HasPrefabInstanceAnyOverrides(..., includeDefaultOverrides: false)</c> 是不是假。
    /// 覆盖那一项刻意不用"扫 YAML 列白名单"实现：引擎写实例时总会带几项与源 Prefab **等值的**
    /// Transform / 材质默认项，文本白名单会把它们误报成覆盖（实测 `m_sharedMaterial` 就是这么冒出来的），
    /// 而"算不算覆盖"的定义在引擎手里，不在文本里。
    /// 查完立刻 <c>CloseScene</c>，不打扰测试运行器自己的场景。
    /// </summary>
    [TestFixture]
    public class SceneAssemblyContractTests
    {
        /// <summary>
        /// 折叠目标（从 ScenePrefabCollapse.Targets 反射读入，避免两处各写一份清单）。
        /// 必须是 public：NUnit 的用例参数类型要能被测试方法公开签名引用（CS0051）。
        /// </summary>
        public sealed class CollapseTarget
        {
            public string Key;
            public string ScenePath;
            public string PrefabPath;
            public string RootName;

            public override string ToString()
            {
                return Key;
            }
        }

        static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        static List<CollapseTarget> ReadTargets()
        {
            Type type = FindEditorType("PirateCrew.EditorTools.ScenePrefabCollapse");
            Assert.IsNotNull(type,
                "找不到 PirateCrew.EditorTools.ScenePrefabCollapse —— 编辑器程序集没加载？"
                + "本测试必须在编辑器里跑（EditMode）。");

            FieldInfo field = type.GetField("Targets", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, "ScenePrefabCollapse.Targets 字段不存在（被改名？本测试按名反射读它）。");

            var raw = field.GetValue(null) as Array;
            Assert.IsNotNull(raw, "ScenePrefabCollapse.Targets 不是数组。");
            Assert.Greater(raw.Length, 0, "ScenePrefabCollapse.Targets 为空——没有任何折叠目标就没有契约可守。");

            var list = new List<CollapseTarget>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                object entry = raw.GetValue(i);
                Type et = entry.GetType();
                list.Add(new CollapseTarget
                {
                    Key = (string)et.GetField("Key").GetValue(entry),
                    ScenePath = (string)et.GetField("ScenePath").GetValue(entry),
                    PrefabPath = (string)et.GetField("PrefabPath").GetValue(entry),
                    RootName = (string)et.GetField("RootName").GetValue(entry),
                });
            }

            return list;
        }

        static Type FindEditorType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i].GetType(fullName, false);
                if (found != null)
                    return found;
            }

            return null;
        }

        public static IEnumerable<TestCaseData> CollapseTargetCases()
        {
            List<CollapseTarget> targets = ReadTargets();
            for (int i = 0; i < targets.Count; i++)
                yield return new TestCaseData(targets[i]).SetName("折叠态_" + targets[i].Key);
        }

        // ------------------------------------------------------------------
        // ① 折叠态：场景 = 恰好一个 Prefab 实例，零裸对象，零真覆盖
        // ------------------------------------------------------------------

        [TestCaseSource(nameof(CollapseTargetCases))]
        public void Scene_IsExactlyOnePurePrefabInstance(CollapseTarget target)
        {
            Assert.IsTrue(File.Exists(Path.Combine(ProjectRoot, target.ScenePath)),
                "场景不存在: " + target.ScenePath);

            // 加性打开：不打扰测试运行器自己的场景，查完立刻关。
            Scene scene = EditorSceneManager.OpenScene(target.ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();

                Assert.AreEqual(1, roots.Length,
                    target.ScenePath + " 的根对象数应为 1（载体根 " + target.RootName + "），实为 " + roots.Length
                    + "。裸对象说明折叠步骤没跑，或有人往场景里手摆东西了——"
                    + "手摆的内容会在下一次装配链重跑时被洗掉，等于没保存；要持久化请改生成器或改 "
                    + target.PrefabPath + "。");

                GameObject root = roots[0];
                Assert.AreEqual(target.RootName, root.name,
                    target.ScenePath + " 的唯一根名应为 " + target.RootName + "。");

                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(root),
                    target.ScenePath + " 的唯一根不是 Prefab 实例——折叠态被解包了。");

                UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                Assert.IsNotNull(source, target.ScenePath + " 的实例取不到源 Prefab（源丢失？）。");
                Assert.AreEqual(target.PrefabPath, AssetDatabase.GetAssetPath(source),
                    target.ScenePath + " 的实例源 Prefab 漂移。");

                // "有没有覆盖"由引擎自己判定（includeDefaultOverrides: false =
                // 与源 Prefab 等值的默认项不算覆盖）。这比在 YAML 里列白名单可靠：
                // 引擎写实例时总会带几项等值的 Transform/材质默认项，文本白名单会把它们误报成覆盖。
                Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(root, false),
                    target.ScenePath + " 的实例带 Prefab 覆盖。折叠态要求**纯实例**——覆盖会把接线/参数"
                    + "散回场景文件，正是 Prefab 化要消灭的状态。改动的正确落点是把值写进 "
                    + target.PrefabPath + " 或它的生成器，再重跑装配链。");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ------------------------------------------------------------------
        // ② 折叠目标本身健全：Prefab 存在、根名对、无缺失脚本
        // ------------------------------------------------------------------

        [TestCaseSource(nameof(CollapseTargetCases))]
        public void Prefab_ExistsWithExpectedRoot_AndNoMissingScripts(CollapseTarget target)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target.PrefabPath);
            Assert.IsNotNull(prefab, "Prefab 不存在: " + target.PrefabPath
                + "（跑 ScenePrefabCollapse.CollapseAll 或整条装配链重建）。");

            Assert.AreEqual(target.RootName, prefab.name,
                "Prefab 根名应为载体根名 " + target.RootName + "（折叠器与校验/测试都按这个名字找它）。");

            Assert.Greater(prefab.transform.childCount, 0,
                target.PrefabPath + " 是个空壳——折叠时没把场景的根对象挂进来。");

            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            var missing = new System.Text.StringBuilder();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                    missing.Append("（第 ").Append(i).Append(" 个组件）");
            }

            Assert.AreEqual(0, missing.Length,
                target.PrefabPath + " 里有**缺失脚本**（MonoBehaviour 的类被删/改名，序列化引用断掉）。"
                + "缺失脚本的组件不会是 null 判断能拦住的静默故障，必须在装配链里修掉。位置：" + missing);
        }

        // ------------------------------------------------------------------
        // ③ 装配链顺序被冻结
        // ------------------------------------------------------------------

        /// <summary>
        /// 冻结的 Battle 装配子链步骤顺序。改这条清单 = 改装配链顺序，
        /// 必须是想清楚的（顺序是真实依赖，逐条理由见 BattleScenePipeline.Steps 的注释）。
        /// </summary>
        static readonly string[] FrozenBattlePipelineSteps =
        {
            "① 重建 Battle 场景骨架（M2BattleSceneSetup.BuildAll）",
            "② 空岛样板件摆入（FloatingIslandShowcaseMenu.PlaceIntoBattleCenter）",
            "③ 样板场景件烘焙 + 接线（SceneArtBaker.BuildAll）",
            "④ 场景资产总清单（SceneAssetManifestBuilder.BuildAll）",
            "⑤ 小地图接线（HudMinimapSceneSetup.WireMinimap）",
            "⑥ 运行期查找接线（BattleLookupWiring.WireScene）",
            "⑦ WorldKit 资产表 + 海面材质接线（WorldMapAssetSetBuilder.BuildAll）",
            "⑧ 折叠为 BattleRig.prefab 实例（ScenePrefabCollapse.CollapseBattle）",
        };

        static string[] ReadPipelineSteps()
        {
            Type type = FindEditorType("PirateCrew.EditorTools.BattleScenePipeline");
            Assert.IsNotNull(type, "找不到 PirateCrew.EditorTools.BattleScenePipeline（编辑器程序集没加载？）。");

            MethodInfo method = type.GetMethod("StepNames", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "BattleScenePipeline.StepNames() 不存在（被改名？本测试按名反射读它）。");

            var names = method.Invoke(null, null) as string[];
            Assert.IsNotNull(names, "BattleScenePipeline.StepNames() 没返回 string[]。");
            return names;
        }

        [Test]
        public void BattlePipeline_StepOrder_IsFrozen()
        {
            string[] actual = ReadPipelineSteps();
            Assert.AreEqual(FrozenBattlePipelineSteps.Length, actual.Length,
                "Battle 装配子链的步骤数变了：\n  实为:\n    " + string.Join("\n    ", actual)
                + "\n  契约为:\n    " + string.Join("\n    ", FrozenBattlePipelineSteps));

            for (int i = 0; i < FrozenBattlePipelineSteps.Length; i++)
            {
                Assert.AreEqual(FrozenBattlePipelineSteps[i], actual[i],
                    "Battle 装配子链第 " + (i + 1) + " 步不符。顺序是真实依赖（每步读写上一步落在场景里的东西），"
                    + "改顺序必须同步改这条契约并复核依赖。");
            }
        }

        [Test]
        public void BattlePipeline_LastStep_IsTheCollapse()
        {
            string[] steps = ReadPipelineSteps();
            StringAssert.Contains("ScenePrefabCollapse", steps[steps.Length - 1],
                "折叠必须是 Battle 装配子链的**最后一步**：折叠之后任何往场景写字段的动作都会产生 "
                + "Prefab 实例覆盖（正是折叠要消灭的状态）。");
        }
    }
}
