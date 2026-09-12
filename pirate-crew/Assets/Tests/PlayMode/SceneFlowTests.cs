using System.Collections;
using NUnit.Framework;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PirateCrew.Tests
{
    /// <summary>
    /// M1 端到端流转验证：Bootstrapper 启动 → MainMenu → Battle → go_back 回 MainMenu，
    /// 并验证全局服务（SceneLoader/SaveManager）在切场景后存活（DontDestroyOnLoad）。
    /// </summary>
    public class SceneFlowTests
    {
        const float TimeoutSeconds = 15f;

        static IEnumerator WaitForScene(string sceneName)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (SceneManager.GetActiveScene().name != sceneName)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail(
                        $"等待场景 '{sceneName}' 超时（当前 '{SceneManager.GetActiveScene().name}'）");
                }
                yield return null;
            }
        }

        static IEnumerator WaitForService<T>() where T : class
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (SceneLoader.Instance == null || SaveManager.Instance == null)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("等待全局服务超时");
                }
                yield return null;
            }
        }

        /// <summary>
        /// SceneLoader 有重入保护：过渡（含淡入淡出）期间的新请求会被忽略。
        /// 场景名切换在过渡中段就发生，因此必须再等 IsLoading 归零才能发下一个请求。
        /// </summary>
        static IEnumerator WaitForTransitionEnd()
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (SceneLoader.Instance != null && SceneLoader.Instance.IsLoading)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("等待场景过渡结束超时");
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Bootstrap_MainMenu_Battle_And_Back()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.Bootstrapper);
            yield return WaitForService<SceneLoader>();

            // Bootstrapper.Start 会请求切到 MainMenu
            yield return WaitForScene(SceneNames.MainMenu);
            yield return WaitForTransitionEnd();
            Assert.IsNotNull(SceneLoader.Instance, "SceneLoader 应被 Bootstrapper 创建");
            Assert.IsNotNull(SaveManager.Instance, "SaveManager 应被 Bootstrapper 创建");

            // 主菜单（或任意模块）通过 EventBus 请求进入战斗
            EventBus.Publish("change_scene", SceneNames.Battle);
            yield return WaitForScene(SceneNames.Battle);
            yield return WaitForTransitionEnd();
            Assert.IsNotNull(SceneLoader.Instance, "切场景后 SceneLoader 应存活");

            // Battle 占位场景的返回按钮走 go_back（事件名与 payload 与 BattlePlaceholder 一致）
            EventBus.Publish("go_back");
            yield return WaitForScene(SceneNames.MainMenu);
            yield return WaitForTransitionEnd();
            Assert.IsNotNull(SceneLoader.Instance, "返回后 SceneLoader 应存活");
            Assert.IsNotNull(SaveManager.Instance, "返回后 SaveManager 应存活");
        }
    }
}
