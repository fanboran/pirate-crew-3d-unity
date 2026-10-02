namespace PirateCrew.Core
{
    /// <summary>
    /// 像素比例档的运行期真值（「1 艺术像素 = N 屏幕像素」）。
    ///
    /// 【放 Core 的原因】这一档有两个跨程序集消费者——UI 侧（<c>PixelScaleService</c> 写入并应用到
    /// CanvasScaler）与渲染侧（<c>PixelartCameraRig</c> 读取并重推 RT 尺寸），而 Rendering 程序集
    /// 不引用 UI：真值必须落在双方都引用的最低层。本类只存一个 int，不含任何引擎调用。
    ///
    /// 【写入方】设置页（PixelScaleService）与战斗滚轮步进（TryStepPixelScale）——两个入口同源，
    /// 后写的生效；读取方每帧自适应（rig 在 Update 里对齐，画布由服务直接写 scaleFactor）。
    /// </summary>
    public static class PixelScaleState
    {
        /// <summary>当前档（整数倍；自动档在服务侧解析成整数后写入这里）。烘焙期出厂值 = 2。</summary>
        public static int Unit = 2;
    }
}
