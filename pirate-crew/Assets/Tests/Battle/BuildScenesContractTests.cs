using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Core;
using UnityEditor;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 编辑器 Build Settings 场景表回归（机制性堵漏，2026-09-28 UI 重构 W1C）：
    /// EditorBuildSettings.scenes 历史上被砍四次（UIShowcase 三度被摘 + 早期截断），
    /// 根因是三个场景装配器各持硬编码清单互写。现已收口到
    /// <c>PirateCrew.EditorTools.BuildScenes.EditorRegistrationScenes()</c> 单一真源；
    /// 本测试钉住底线——**任何**写场景表的代码跑完后，六个「运行时按名载入」的场景
    /// 必须都在表内且 enabled。缺一个当场红，而不是等某天发现组件展示又白了。
    /// （要读 <c>EditorBuildSettings.scenes</c>——UnityEditor 域专属，harness 纯 dotnet 域自动 Ignore。）
    /// </summary>
    public class BuildScenesContractTests
    {
        static readonly string[] RequiredSceneNames =
        {
            SceneNames.Bootstrapper,
            SceneNames.MainMenu,
            SceneNames.Battle,
            SceneNames.CrewManagement,
            SceneNames.LevelSelect,
            SceneNames.UIShowcase,
        };

        [Test]
        public void EditorBuildSettings_ContainsAllRuntimeLoadableScenes_Enabled()
        {
            if (!IsUnityRuntimeDomain())
                Assert.Ignore("非 Unity 运行时（无头验证台纯 dotnet）：EditorBuildSettings 要 "
                    + "UnityEditor——本断言由 EditMode 收口。");

            HashSet<string> enabledPaths = CollectEnabledScenePaths();

            foreach (string name in RequiredSceneNames)
            {
                string path = SceneNames.PathOf(name);
                Assert.IsTrue(enabledPaths.Contains(path),
                    "Build Settings 缺少场景 " + path + "——又有代码用自带清单覆盖了场景表"
                    + "（UIShowcase 历史上被这样摘过四次。修复：写表一律走"
                    + " BuildScenes.EditorRegistrationScenes 单一真源）");
            }
        }

        /// <summary>读 Build Settings 场景表。<c>EditorBuildSettingsScene</c> 在 UnityEditor 程序集里，
        /// 必须独立成方法：缺失程序集的类型让**整个方法体** JIT 失败，守卫语句放在同一个方法里
        /// 会连跑的机会都没有（实测）。</summary>
        static HashSet<string> CollectEnabledScenePaths()
        {
            var enabledPaths = new HashSet<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    enabledPaths.Add(scene.path);
            }
            return enabledPaths;
        }

        /// <summary>反射探测是否在 Unity 运行时域（纯 dotnet 域拿不到 Application.dataPath）。</summary>
        static bool IsUnityRuntimeDomain()
        {
            try
            {
                System.Type appType = System.Type.GetType("UnityEngine.Application, UnityEngine.CoreModule");
                System.Reflection.PropertyInfo dataPath = appType?.GetProperty("dataPath",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return !string.IsNullOrEmpty(dataPath?.GetValue(null) as string);
            }
            catch
            {
                return false;
            }
        }
    }
}
