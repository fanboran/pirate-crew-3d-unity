using System.Collections;
using System.IO;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.SceneKitPilot
{
    /// <summary>
    /// 场景资产样板（SceneKit Pilot）的运行时自动出图钩子：协调者给播放器传
    /// <c>-sceneKitOut &lt;绝对目录&gt;</c> 启动，本组件加载 SceneKitPilot showcase 场景，
    /// 就绪后绕拍 6 张 1280×720 PNG 落盘，完毕 <c>Application.Quit(0)</c>，失败 Quit(1)。
    ///
    /// 【零侵入】不带该参数启动（正常游玩/测试）时装配入口直接返回，零开销。
    /// 【零战斗耦合】手法仿 <c>ArtReview/PlayerArtCapture</c> 但独立实现：除
    /// <c>Core.CommandLineOptions</c>（命令行统一解析处）外不引用任何项目类型，
    /// 与战斗模块零编译耦合。
    /// 查找场景节点用根对象按名扫描（仅本调试组件允许；运行时代码仍禁 GameObject.Find）。
    ///
    /// 【已知坑（教训内化）】
    ///   · batchmode/播放器下 <c>File.Exists</c> 的相对路径按**进程工作目录**解析，不按工程根——
    ///     所以 outDir 一律取参数给的绝对路径，落盘轮询也用同一绝对路径；
    ///   · 场景是否登记进 Build Settings 不用文件系统判断（同上路径坑），在播放器里直接
    ///     <c>LoadScene</c> 后核对 activeScene.name，失败即 Quit(1)；
    ///   · <c>ScreenCapture.CaptureScreenshot</c> 是异步的：每张都要等帧 + 等文件真的落盘再拍下一张，
    ///     否则后一张会把前一张的采集请求顶掉（出图缺张）；
    ///   · 本机 batchmode 下 <c>EditorBuildSettings.scenes</c> API 改动不落盘，所以
    ///     <c>SceneKitPilotSetup.BuildAll</c> 不做登记——SceneKitPilot 场景已由协调者手工登记进
    ///     ProjectSettings/EditorBuildSettings.asset（YAML 直改），本组件依赖该登记。
    /// </summary>
    public sealed class SceneKitPilotCapture : MonoBehaviour
    {
        const string SceneName = "SceneKitPilot";
        const int Width = 1280;
        const int Height = 720;
        const float ReadyTimeoutSeconds = 30f;
        const float PerShotTimeoutSeconds = 10f;

        /// <summary>6 张机位：全景 / 船艏 3/4 / 船正侧 / 船艉 3/4 / 桥从岸看 / 桥贴水看。</summary>
        static readonly (string Name, Vector3 Position, Vector3 LookTarget, float Fov)[] Shots =
        {
            ("01-pano", new Vector3(15f, 8.5f, 13.5f), new Vector3(0f, 0.4f, -2.5f), 55f),
            ("02-ship-front34", new Vector3(5.8f, 3.4f, -0.8f), new Vector3(0f, 1.9f, -6.5f), 50f),
            ("03-ship-side", new Vector3(-9.2f, 2.6f, -5.9f), new Vector3(0f, 2.2f, -6.5f), 50f),
            ("04-ship-back34", new Vector3(-4.6f, 3.6f, -12.8f), new Vector3(0f, 2.0f, -6.2f), 50f),
            ("05-dock-from-shore", new Vector3(4.4f, 3.0f, 9.4f), new Vector3(0f, 0.4f, 2.0f), 55f),
            ("06-dock-waterline", new Vector3(4.8f, 0.5f, -2.2f), new Vector3(0f, 0.5f, 2.6f), 55f),
        };

        string _outDir;
        Camera _camera;

        /// <summary>
        /// 组合根接线入口（唯一入口 <c>Core/GameEntryPoint</c> 在进入播放前调用；不带开关时零开销）。
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.Initialize, order: 110)]
        internal static void Install()
        {
            string outDir = CommandLineOptions.GetValue(ToolFlags.SceneKitOut);
            if (string.IsNullOrEmpty(outDir))
                return;     // 正常启动：零开销，什么都不装。

            var go = new GameObject("[SceneKitPilotCapture]");
            go.AddComponent<SceneKitPilotCapture>()._outDir = outDir;
            DontDestroyOnLoad(go);
        }

        IEnumerator Start()
        {
            Screen.SetResolution(Width, Height, false);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            // showcase 场景在 Build Settings 里（协调者手工登记），单模式加载；失败即退。
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            if (SceneManager.GetActiveScene().name != SceneName)
            {
                Debug.LogError("[SceneKitPilotCapture] " + SceneName
                    + " 场景加载失败（未登记进 Build Settings？），当前场景："
                    + SceneManager.GetActiveScene().name + "——中止采集");
                Application.Quit(1);
                yield break;
            }

            // 等光照/阴影/水面稳定几帧再开拍。
            yield return new WaitForSeconds(1.5f);
            yield return new WaitForEndOfFrame();

            Directory.CreateDirectory(_outDir);
            SetupCamera();

            foreach (var shot in Shots)
            {
                _camera.transform.position = shot.Position;
                _camera.transform.rotation =
                    Quaternion.LookRotation(shot.LookTarget - shot.Position, Vector3.up);
                _camera.fieldOfView = shot.Fov;

                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();

                // CaptureScreenshot 异步落盘：绝对路径 + 轮询等文件出现，再切下一机位。
                string path = Path.Combine(_outDir, shot.Name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                float deadline = Time.unscaledTime + PerShotTimeoutSeconds;
                while (!File.Exists(path) && Time.unscaledTime < deadline)
                    yield return null;
                if (!File.Exists(path))
                {
                    Debug.LogError("[SceneKitPilotCapture] 截图未落盘：" + path + "——中止采集");
                    Application.Quit(1);
                    yield break;
                }
                yield return new WaitForSeconds(0.2f);
            }

            Debug.Log("[SceneKitPilotCapture] 采集完成（6 张 1280x720），退出。目录：" + _outDir);
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>自建采集相机并接管渲染：按根名扫描禁用场景里已有相机（不用 GameObject.Find）。</summary>
        void SetupCamera()
        {
            var go = new GameObject("[SceneKitPilotCaptureCamera]");
            _camera = go.AddComponent<Camera>();
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 400f;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Camera existing in root.GetComponentsInChildren<Camera>(true))
                {
                    if (existing != _camera)
                        existing.enabled = false;
                }
            }
        }
    }
}
