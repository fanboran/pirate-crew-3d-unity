using System.Collections;
using System.IO;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 组件陈列页的独立播放器采集器：命令行 <c>-uiGalleryOut &lt;绝对目录&gt;</c>
    /// 启动播放器后，把 <see cref="UiGalleryPage"/> 两页（控件档位 / 图标集）逐页
    /// 截成 PNG。
    ///
    /// 【为什么不挂在 PlayerArtCapture 上】出图链路解耦：PlayerArtCapture 在
    /// Runtime 程序集且重度依赖战斗场景；gallery 是纯 UI 资产展示，不进战斗、
    /// 不等水体热身，独立跑更快。程序集方向上 Runtime 也不能反向引用本程序集
    /// （UI → Runtime 已是单向依赖）。
    ///
    /// 【背景】页根压一张全屏 <c>PixelSkin.DarkOf(PixelTone.Frame)</c>（Frame tone 暗档）底——陈列页就是设计
    /// 系统的"活文档"，控件在它们真实所属的深底上展示（见 docs/设计/UI设计语言.md §九）。
    /// </summary>
    public class UiGalleryCapture : MonoBehaviour
    {
        const int Width = 1920;
        const int Height = 1080;

        string _outDir;

        /// <summary>
        /// 组合根接线入口（唯一入口 <c>Core/GameEntryPoint</c> 在进入播放前调用；不带开关时零开销）。
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.Initialize, order: 120)]
        internal static void Install()
        {
            string outDir = global::PirateCrew.Core.CommandLineOptions.GetValue(
                global::PirateCrew.Core.ToolFlags.UiGalleryOut);
            if (string.IsNullOrEmpty(outDir))
                return;   // 正常启动零开销。

            var go = new GameObject("[UiGalleryCapture]");
            go.AddComponent<UiGalleryCapture>()._outDir = outDir;
            DontDestroyOnLoad(go);
        }

        IEnumerator Start()
        {
            Screen.SetResolution(Width, Height, false);
            Application.targetFrameRate = 60;

            // 引导流程与字体 Resources 加载让路（不依赖具体场景，主菜单底会被全屏 InkDeep 盖住）。
            yield return new WaitForSeconds(1f);
            Directory.CreateDirectory(_outDir);

            yield return StartCoroutine(CaptureRoutine(UiGalleryPage.ControlsPage, false));
            yield return StartCoroutine(CaptureRoutine(UiGalleryPage.IconsPage, true));

            global::PirateCrew.Core.Log.Info("[UiGalleryCapture] 陈列页采集完成，退出。目录：" + _outDir);
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        IEnumerator CaptureRoutine(string pageName, bool iconsPage)
        {
            GameObject canvasRoot = BuildCanvas(pageName, iconsPage);

            // 多等几帧：UGUI 布局收口 + TMP 动态图集把本页新字形光栅化进 atlas。
            for (int i = 0; i < 6; i++)
                yield return null;
            yield return new WaitForEndOfFrame();

            string path = Path.Combine(_outDir, pageName + ".png");
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.unscaledTime + 10f;
            while (!File.Exists(path) && Time.unscaledTime < deadline)
                yield return null;
            yield return new WaitForSeconds(0.3f);

            Destroy(canvasRoot);
            yield return null;
        }

    /// <summary>overlay Canvas（与战斗 HUD 同缩放口径）+ 全屏 InkDeep 底 + 陈列页内容。
    /// 采集器与实机调试窗（<see cref="UiShowcaseBoot"/>）共用这条建法。</summary>
    public static GameObject BuildCanvas(string pageName, bool iconsPage)
        {
            var go = new GameObject("UiGalleryCanvas_" + pageName, typeof(Canvas));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;   // 压过一切常规 UI

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height);
            // 对齐 Godot canvas_items+expand 口径：Expand(1) 外扩参考分辨率（陈列画布与各构建器统一）。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // 全屏深底：像素皮最暗档（DarkOf(Frame)）——陈列页控件在它们真实所属的深底上展示。
            RectTransform backdrop = UiKit.CreateRect("Backdrop", go.transform);
            UiKit.Stretch(backdrop);
            var backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = PixelSkin.DarkOf(PixelTone.Frame);
            backdropImage.raycastTarget = false;

            UiGalleryPage.Build(go.transform, iconsPage);
            return go;
        }
    }
}
