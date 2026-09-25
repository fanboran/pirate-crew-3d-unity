using System;

namespace PirateCrew.Core
{
    /// <summary>
    /// ScreenCapture 的无头安全垫片：`-nographics` 的编译域里 ScreenCaptureModule
    /// 会被整体剔除（CS0103），出图钩子（ArtReview/SceneKitPilot）因此没法直接引用它。
    /// 本类用反射直呼——带图形设备（GUI 编辑器/正常播放器）时行为与
    /// <c>UnityEngine.ScreenCapture.CaptureScreenshot(path)</c> 完全一致；
    /// 无图形域下类型解析为 null，静默 no-op（出图钩子在那儿本来也出不了图）。
    /// </summary>
    public static class GraphicsCaptureCompat
    {
        /// <summary>等价 <c>ScreenCapture.CaptureScreenshot(filename)</c>；无图形域为 no-op。</summary>
        public static void CaptureScreenshot(string filename)
        {
            Type type = Type.GetType(
                "UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
            if (type == null)
                return;   // -nographics：模块不在编译/运行域，出图无从谈起
            type.GetMethod("CaptureScreenshot", new[] { typeof(string) })
                ?.Invoke(null, new object[] { filename });
        }
    }
}
