using System.Collections.Generic;
using System.IO;
using PirateCrew.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 组件展示**实机调试窗口**的场景构建（<see cref="ScenePath"/>）：
    /// 场景内容刻意最小——一台纯色清屏的相机 + 一个 <see cref="UiShowcaseBoot"/>，
    /// 页面全部由运行时建（与采集链路同一份 <see cref="UiGalleryPage"/>），场景里没有
    /// 要手维护的 UI 层级。菜单：
    ///   · 构建：生成/刷新场景 + 登记 Build Settings；
    ///   · 构建并播放：构建完直接进 Play（= 创始人要的"专门开一个实机调试窗口"）；
    ///   · 采集实机截图：命令行出口（带图形的编辑器进程，非 batchmode——UGUI 截图
    ///     需要真渲染），落 <c>export/ui-pixel-4a/showcase-live.png</c> 后退出进程。
    /// </summary>
    public static class UiShowcaseSceneSetup
    {
        const string ScenePath = "Assets/Scenes/UIShowcase.unity";
        const string CaptureDir = "export/ui-pixel-4a";
        const string CaptureFile = "showcase-live.png";

        /// <summary>构建（或刷新）组件展示场景：最小内容 + 自足接线，登记进 Build Settings。</summary>
        [MenuItem("PirateCrew/UI/构建组件展示场景（实机调试窗口）")]
        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.AddComponent<Camera>();
            // 底色与陈列页全屏底同源（Frame tone 暗档）：窗口在它真实所属的深底上展示。
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PixelSkin.DarkOf(PixelTone.Frame);
            camera.orthographic = true;
            camera.nearClipPlane = -10f;

            new GameObject("ShowcaseBoot").AddComponent<UiShowcaseBoot>();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            RegisterInBuildSettings();
            Debug.Log("[UiShowcaseSceneSetup] 组件展示场景已构建：" + ScenePath);
        }

        /// <summary>构建并直接进 Play——实机调试窗口一步到位。</summary>
        [MenuItem("PirateCrew/UI/打开组件展示（构建并播放）")]
        public static void BuildAndPlay()
        {
            Build();
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// 命令行出口：<c>-executeMethod ...CaptureFromCommandLine -uiShowcaseOut &lt;绝对目录&gt;</c>。
        /// **必须带图形跑**（不给 -batchmode/-nographics）：开场景 → 进 Play → 等布局与
        /// TMP 动态图集收口 → ScreenCapture → 退 Play → 退出编辑器进程。退出码 0 = 出图成功。
        /// </summary>
        [MenuItem("PirateCrew/UI/采集组件展示截图（命令行出口）")]
        public static void CaptureFromCommandLine()
        {
            string dir = GetArg("-uiShowcaseOut");
            if (string.IsNullOrEmpty(dir))
                dir = Path.GetFullPath(CaptureDir);
            SessionState.SetString(CaptureDirKey, dir);
            SessionState.SetInt(StateKey, 0);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.update += CaptureStep;
            Debug.Log("[UiShowcaseSceneSetup] 实机截图流程启动 → " + dir);
        }

        // 进 Play 会触发 domain reload，update 订阅与闭包状态都会被清掉——
        // 状态机全部放 SessionState（本会话内跨 reload 存活），并用 InitializeOnLoad 重新挂钩。
        const string PendingKey = "UiShowcaseCapture.Pending";
        const string StateKey = "UiShowcaseCapture.State";
        const string WaitKey = "UiShowcaseCapture.Wait";
        const string CaptureDirKey = "UiShowcaseCapture.Dir";

        [InitializeOnLoadMethod]
        static void RehookCaptureAfterReload()
        {
            if (SessionState.GetBool(PendingKey, false))
                EditorApplication.update += CaptureStep;
        }

        static void CaptureStep()
        {
            string dir = SessionState.GetString(CaptureDirKey, CaptureDir);
            string path = Path.Combine(dir, CaptureFile);
            switch (SessionState.GetInt(StateKey, 0))
            {
                case 0:
                    Build();
                    Screen.SetResolution(1920, 1080, false);
                    SessionState.SetInt(StateKey, 1);
                    break;
                case 1:
                    EditorApplication.isPlaying = true;
                    SessionState.SetInt(StateKey, 2);
                    break;
                case 2:
                    // Play 早期帧等三件事：UGUI 布局收口、TMP 动态图集把本页新字形光栅化、
                    // Screen.SetResolution 落地。90 帧在装配机上是秒级，够稳。
                    if (EditorApplication.isPlaying && Time.frameCount > 90)
                    {
                        Directory.CreateDirectory(dir);
                        ScreenCapture.CaptureScreenshot(path);
                        SessionState.SetInt(StateKey, 3);
                        SessionState.SetInt(WaitKey, 0);
                    }
                    break;
                case 3:
                    if (SessionState.GetInt(WaitKey, 0) > 20)
                    {
                        Debug.Log("[UiShowcaseSceneSetup] 实机截图完成：" + path);
                        EditorApplication.isPlaying = false;
                        SessionState.SetInt(StateKey, 4);
                        SessionState.SetInt(WaitKey, 0);
                    }
                    else
                    {
                        SessionState.SetInt(WaitKey, SessionState.GetInt(WaitKey, 0) + 1);
                    }
                    break;
                case 4:
                    if (!EditorApplication.isPlaying)
                    {
                        if (SessionState.GetInt(WaitKey, 0) > 5)
                        {
                            SessionState.SetBool(PendingKey, false);
                            EditorApplication.update -= CaptureStep;
                            EditorApplication.Exit(File.Exists(path) ? 0 : 1);
                        }
                        else
                        {
                            SessionState.SetInt(WaitKey, SessionState.GetInt(WaitKey, 0) + 1);
                        }
                    }
                    break;
            }
        }

        /// <summary>登记进 Build Settings（插在 Bootstrapper 之后；重跑幂等）。</summary>
        static void RegisterInBuildSettings()
        {
            List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == ScenePath))
                return;
            int insert = list.FindIndex(s => s.path.EndsWith("Bootstrapper.unity"));
            list.Insert(insert < 0 ? list.Count : insert + 1, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }
            return null;
        }
    }
}
