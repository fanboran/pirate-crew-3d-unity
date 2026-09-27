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
    /// （本测试只依赖 SceneNames 常量拼路径，不引 Editor 程序集类型。）
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
            var enabledPaths = new HashSet<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    enabledPaths.Add(scene.path);
            }

            foreach (string name in RequiredSceneNames)
            {
                string path = "Assets/Scenes/" + name + ".unity";
                Assert.IsTrue(enabledPaths.Contains(path),
                    "Build Settings 缺少场景 " + path + "——又有代码用自带清单覆盖了场景表"
                    + "（UIShowcase 历史上被这样摘过四次。修复：写表一律走"
                    + " BuildScenes.EditorRegistrationScenes 单一真源）");
            }
        }
    }
}
