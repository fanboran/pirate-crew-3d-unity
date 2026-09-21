using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 控件陈列样张的 **Play 模式出图器**（P1 对账产物的权威路线）。
    ///
    /// 【为什么不用 -executeMethod 同步渲染】实测 executeMethod 同步调用栈里，
    /// 自定义 Graphic（SketchWobbleGraphic/SketchSliderGraphic 等自绘件）的
    /// CanvasRenderer 被 native 层即时回收（AddComponent 返回活引用、下一次访问即
    /// fake-null），UGUI 网格重建不完整——自绘件全军覆没。而内置 Image/TMP 正常。
    /// play 模式下 UGUI 生命周期原汁原味（Battle HUD 的手绘 UI 一直渲染正常），
    /// 且 ScreenCapture 出图是本项目历史成熟路线（OutlineDebugCapture / PlayerArtCapture）。
    ///
    /// 【流程】executeMethod → 进 play → 第 3 帧调 ControlsSampleBuilder.TryBuildSampleScene
    /// 在 play 场景搭样张（摆位表同源）→ Screen.SetResolution 1920×1080 → 40 帧图集暖场 →
    /// ScreenCapture.CaptureScreenshot → 校验产物 → 退 play → Exit(0/1)。
    /// 全程 try-catch + 帧数上限看门狗：异常落日志并退出，绝不悬死编辑器。
    /// </summary>
    public static class ControlsSamplePlayCapture
    {
        const string RelativeOut = "export/controls-sample/unity_1920x1080.png";
        const int BuildFrame = 3;       // play 稳定后搭样张
        const int ShootFrame = 40;      // 图集暖场后截图
        const int VerifyFrame = 46;     // ScreenCapture 异步落盘后校验
        const int WatchdogFrame = 300;  // 看门狗：600 帧（20s@30fps）必收口

        static int _frame;
        static bool _built;
        static bool _shot;
        static bool _exitWhenDone;

        [MenuItem("PirateCrew/控件样张/Play 模式出图（不退出编辑器）", priority = 22)]
        public static void CaptureViaPlayMenu() => CaptureViaPlay(false);

        /// <summary>-executeMethod 入口（GUI 模式，勿带 -batchmode/-nographics）。</summary>
        public static void CaptureViaPlay() => CaptureViaPlay(true);

        public static void CaptureViaPlay(bool exitWhenDone)
        {
            try
            {
                if (Application.isPlaying)
                {
                    Debug.LogError("[ControlsSamplePlayCapture] 已处于 play 模式，先退出再跑。");
                    if (exitWhenDone) EditorApplication.Exit(1);
                    return;
                }
                _frame = 0;
                _built = false;
                _shot = false;
                _exitWhenDone = exitWhenDone;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                EditorApplication.isPlaying = true;   // 下一 tick 进 play
                Debug.Log("[ControlsSamplePlayCapture] entering play mode ...");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ControlsSamplePlayCapture] enter play failed: " + e);
                if (exitWhenDone) EditorApplication.Exit(1);
            }
        }

        static void Tick()
        {
            try
            {
                _frame++;
                if (_frame > WatchdogFrame)
                    Fail("看门狗超时（" + WatchdogFrame + " 帧未完成出图），强制收口。");

                if (_frame == 2)
                    Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);

                if (_frame >= BuildFrame && !_built)
                {
                    _built = true;
                    // play 场景内搭样张（摆位表与 Godot 靶子同源；纯 GameObject API，play 下合法）
                    if (!ControlsSampleBuilder.TryBuildSampleScene())
                        Fail("样张场景搭建失败（字体/画布缺失），详见上方日志。");
                    else
                        Debug.Log("[ControlsSamplePlayCapture] sample scene built in play, frame " + _frame);
                }

                if (_frame == ShootFrame && !_shot)
                {
                    _shot = true;
                    // 相对路径基于工程根（编辑器口径）；play 帧尾异步落盘
                    ScreenCapture.CaptureScreenshot(RelativeOut);
                    Debug.Log("[ControlsSamplePlayCapture] CaptureScreenshot issued: " + RelativeOut);
                }

                if (_frame >= VerifyFrame && _shot)
                {
                    string abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                        RelativeOut.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(abs) || new FileInfo(abs).Length == 0)
                        return;   // 落盘可能晚一帧，下一 tick 再验
                    Debug.Log("[ControlsSamplePlayCapture] 样张已输出: " + abs + "（"
                        + new FileInfo(abs).Length + " 字节）");
                    Finish(0);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ControlsSamplePlayCapture] tick failed: " + e);
                Finish(1);
            }
        }

        static void Fail(string reason)
        {
            Debug.LogError("[ControlsSamplePlayCapture] " + reason);
            Finish(1);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            // play 退出延迟一 tick；Exit 在 play 退出后由 QuitWhenNotPlaying 收口
            EditorApplication.update -= QuitWhenNotPlaying;
            EditorApplication.update += QuitWhenNotPlaying;
            _pendingCode = code;
        }

        static int _pendingCode;

        static void QuitWhenNotPlaying()
        {
            if (Application.isPlaying)
                return;
            EditorApplication.update -= QuitWhenNotPlaying;
            Debug.Log("[ControlsSamplePlayCapture] exited play, quitting with code " + _pendingCode);
            EditorApplication.Exit(_pendingCode);
        }
    }
}
