namespace PirateCrew.Core
{
    /// <summary>
    /// 像素化渲染风格代号（见 docs/技术/渲染/管线/双风格切换.md）。
    /// CellShade = 单元着色线（G-Buffer + 连通域 + 低分辨率域着色，现役默认）；
    /// PostLine = 屏后描边线（全分辨率着色 + 屏幕空间描边/锐利上采样）。
    /// </summary>
    public enum RenderStyle
    {
        /// <summary>单元着色线（Sloane 参照库，现役默认）。</summary>
        CellShade = 0,

        /// <summary>屏后描边线（Bababuyyy 参照库）。</summary>
        PostLine = 1,
    }

    /// <summary>
    /// 渲染风格的运行期真值（仿 <see cref="PixelScaleState"/>）。
    ///
    /// 【放 Core 的原因】写入方在设置/UI 程序集、读取方在渲染程序集（<c>PixelartCameraRig</c>
    /// 与两套 Renderer 装配），真值必须落在双方都引用的最低层。本类只存一个枚举，不含引擎调用。
    ///
    /// 【生效边界】切换在场景重载时生效（两套链的相机结构与 RT 生命周期不同，不做逐帧热切）——
    /// 设置页写入本值并落盘，随后重载当前场景；rig 初始化时读本值决定走哪条链。
    /// </summary>
    public static class RenderStyleState
    {
        /// <summary>当前风格（出厂 = CellShade，与现役观感一致）。</summary>
        public static RenderStyle Current = RenderStyle.CellShade;
    }
}
