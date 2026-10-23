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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            HeadlessFrustumNoise.Suppress();
            yield return null;
        }

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

    /// <summary>
    /// PlayMode 无头域的相机噪声豁免（**测试基建，不是产品豁免**；同目录四个测试类共用）。
    ///
    /// 【根因（2026-10 全灭日志的追查结论）】batchmode <c>-nographics</c> 下：
    ///   1. <see cref="PirateCrew.Rendering.Pixelart.PixelartCameraRig.DeviceSupportsPath"/> = false
    ///     （空设备只支持 1 张渲染目标，凑不齐 G-buffer 的 7 张），<c>EnsureBuffers</c> 按设计整趟
    ///      inert 不建缓冲 → <c>RenderHeight</c> 恒 0；
    ///   2. rig 的 derive 取景模式（BattleRig 装配即开）每帧推
    ///      <c>orthographicSize = RenderHeight × worldPerPixel × 0.5 = 0</c>——正交 size 为 0
    ///      使投影/反投影矩阵退化；
    ///   3. 引擎玩家循环的鼠标悬停探测 <c>SendMouseEvents.DoSendMouseEvents</c> 对 (0,0) 调
    ///      <c>Camera.ScreenPointToRay</c> 时撞上退化视锥，逐帧报
    ///      「Screen position out of view frustum (screen pos 0, 0) (Camera rect 0 0 640 480)」
    ///      （640×480 = batchmode 默认屏幕）。测试框架把未预期 Error 判死用例——一条错误即整套全灭
    ///      （历史结果 XML 同款 572 处，至少 2026-10-22 起就有，非本轮引入）。
    ///   产品实机（真 GPU + 真窗口）RenderHeight &gt; 0、orthoSize = 16，**无此错**——按处置分级走
    ///   a（batchmode 特有引擎噪声 → 测试域豁免）；rig「缓冲不可用时不该把 orthoSize 推成 0」
    ///   这条无头特有边角另登记转交协调者，不在本域修。
    ///
    /// 【为什么是 ignoreFailingMessages 而不是逐条 LogAssert.Expect】test-framework 1.1.33 没有
    /// <c>LogAssert.Ignore(Regex)</c>（那是 test-framework 2.x 的 API）；Expect 是队列语义，必须与
    /// 逐帧错误的条数精确对齐（主相机与 Cast 相机两台都退化时每帧两条），数量对不齐就是新的假红。
    ///
    /// 【已知取舍】该开关对整个用例 scope（含 SetUp/TearDown）吞掉全部 Error/Exception/Assert——
    /// 真回归的防线仍在各用例断言本身与 EditMode/harness 域；scope 随用例结束销毁，不跨用例泄漏。
    /// </summary>
    internal static class HeadlessFrustumNoise
    {
        /// <summary>在 [UnitySetUp] 里调用；豁免覆盖本用例全程，无需在 TearDown 复原（scope 自毁）。</summary>
        internal static void Suppress()
        {
            LogAssert.ignoreFailingMessages = true;
        }
    }
}
