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
    ///
    /// 等待原语（internal）供同目录其余 PlayMode 测试复用（如 MainChainE2ETests），不各写一份。
    /// </summary>
    public class SceneFlowTests
    {
        const float TimeoutSeconds = 15f;

        internal static IEnumerator WaitForScene(string sceneName)
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

        /// <summary>
        /// 等待服务 <typeparamref name="T"/> 在 Services 注册表就绪（Bootstrapper.EnsureInstalled 登记）。
        /// 【签名语义修正】原实现泛型参数未参与解析，方法体只盯 SceneLoader/SaveManager 两个
        /// 具体单例——现在真正按 T 解析（走 TryGet，静默轮询不刷错误日志），调用点需要
        /// 哪个服务就等哪个（本文件用例依赖两个服务，因此依次等 SceneLoader 与 SaveManager）。
        /// </summary>
        internal static IEnumerator WaitForService<T>() where T : class
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!Services.TryGet<T>(out _))
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("等待全局服务 " + typeof(T).Name + " 超时");
                }
                yield return null;
            }
        }

        /// <summary>
        /// SceneLoader 有重入保护：过渡（含淡入淡出）期间的新请求会被忽略。
        /// 场景名切换在过渡中段就发生，因此必须再等 IsLoading 归零才能发下一个请求。
        /// </summary>
        internal static IEnumerator WaitForTransitionEnd()
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

        /// <summary>
        /// 装载 Bootstrapper 场景并保证本实例会执行 <c>Start → ChangeScene(StartScene)</c> 的自动转场。
        /// 【为什么要先清残留】Bootstrapper 是 DontDestroyOnLoad 单例且带「重复实例自毁」守卫
        /// （Awake 见 Instance 即销毁自己）——同一次测试会话里**第二次**装载该场景时，新实例会在
        /// Awake 里自毁、Start 永不执行，自动转场就此哑火（跨用例静态/实例存活导致，先跑过任何
        /// 装载过 Bootstrapper 的用例后，后续依赖自动转场的用例都会 15s 超时）。先显式销毁旧实例
        /// 再装载，让每次装载都走真实冷启动链。
        /// </summary>
        internal static IEnumerator LoadBootstrapperFresh()
        {
            if (Bootstrapper.Instance != null)
                Object.Destroy(Bootstrapper.Instance.gameObject);

            yield return SceneManager.LoadSceneAsync(SceneNames.Bootstrapper);
        }

        [UnityTest]
        public IEnumerator Bootstrap_MainMenu_Battle_And_Back()
        {
            yield return LoadBootstrapperFresh();
            yield return WaitForService<SceneLoader>();
            yield return WaitForService<SaveManager>();

            // Bootstrapper.Start 会请求切到 MainMenu
            yield return WaitForScene(SceneNames.MainMenu);
            yield return WaitForTransitionEnd();
            Assert.IsNotNull(SceneLoader.Instance, "SceneLoader 应被 Bootstrapper 创建");
            Assert.IsNotNull(SaveManager.Instance, "SaveManager 应被 Bootstrapper 创建");

            // 主菜单（或任意模块）通过 EventBus 频道请求进入战斗
            EventBus.Publish(SceneEvents.ChangeScene, SceneNames.Battle);
            yield return WaitForScene(SceneNames.Battle);
            yield return WaitForTransitionEnd();
            Assert.IsNotNull(SceneLoader.Instance, "切场景后 SceneLoader 应存活");

            // Battle 占位场景的返回按钮走 SceneEvents.GoBack（频道与 payload 与 BattlePlaceholder 一致）
            EventBus.Publish(SceneEvents.GoBack);
            yield return WaitForScene(SceneNames.MainMenu);
            yield return WaitForTransitionEnd();
            Assert.IsNotNull(SceneLoader.Instance, "返回后 SceneLoader 应存活");
            Assert.IsNotNull(SaveManager.Instance, "返回后 SaveManager 应存活");
        }
    }
}
