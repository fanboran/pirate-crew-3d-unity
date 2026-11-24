namespace PirateCrew.Core
{
    /// <summary>
    /// 像素化渲染风格代号。本仓只保留 <see cref="PostLine"/>（屏后描边线：全分辨率着色 +
    /// 屏幕空间描边 + 锐利上采样），口径见 docs/技术/渲染/管线/渲染管线.md。
    ///
    /// <see cref="CellShade"/>（单元着色线）已裁决退役：枚举值只是因为相机装配器与材质换装器里
    /// 的分支尚未随代码删除而暂时保留，没有任何入口能再选中它。
    /// </summary>
    public enum RenderStyle
    {
        /// <summary>单元着色线（已退役，不生效）。</summary>
        CellShade = 0,

        /// <summary>屏后描边线（唯一生效的渲染风格）。</summary>
        PostLine = 1,
    }

    /// <summary>
    /// 渲染风格的运行期真值。
    ///
    /// 【放 Core 的原因】写入方在设置程序集、读取方在渲染程序集（<c>PixelartCameraRig</c>
    /// 与材质换装器），真值必须落在双方都引用的最低层。本类只存一个枚举，不含引擎调用。
    ///
    /// 【只读得到 PostLine】setter 把退役的 CellShade 挡在外面——旧存档、旧环境变量、
    /// 尚未删除的分支写入它都不会生效，画面不会静默退回退役风格。
    /// </summary>
    public static class RenderStyleState
    {
        static RenderStyle _current = RenderStyle.PostLine;

        /// <summary>当前风格（恒为 PostLine）。</summary>
        public static RenderStyle Current
        {
            get => _current;
            set
            {
                if (value == RenderStyle.PostLine)
                    _current = value;
            }
        }
    }
}
