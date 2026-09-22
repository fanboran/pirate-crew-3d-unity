using System;
using System.Collections.Generic;

namespace PirateCrew.Core
{
    /// <summary>
    /// 运行期命令行开关的**唯一解析处**（进程级只读快照）。
    ///
    /// 【为什么要集中】工具模式（无头出图、样板关覆盖、指定海图试玩…）原先各自在
    /// <c>Boot</c> 里扫 <c>Environment.GetCommandLineArgs()</c>：同一份 argv 被解析七八遍、
    /// 解析规则各写各的（有的认 <c>-flag value</c>、有的漏判缺参），而且"命令行解析"
    /// 这条启动路径藏在各个工具类里，读代码时根本没法定论"启动时到底看了哪些开关"。
    /// 现在由 <see cref="GameEntryPoint"/> 进入播放前调一次 <see cref="Parse()"/>，
    /// 其它人只读 <see cref="Has"/> / <see cref="GetValue"/> / 类型化取值。
    ///
    /// 【解析规则】<c>-flag value</c> 形式的裸参数对（Unity 自己传的 <c>-logFile</c>、
    /// <c>-projectPath</c> 同样按此形式落在表里，无害）；<c>-flag</c> 后面没有值（或下一个
    /// token 也是 <c>-</c> 开关）时记为"存在但无值"。**同一开关重复出现取最后一次**。
    ///
    /// 【可见的开关名单】见 <see cref="ToolFlags"/>。模块自己私有的调试档（如
    /// <c>AmbientTimeOfDayCatalog.CommandLineTierSwitch</c>、<c>OceanRig</c> 的 <c>-oceanDebug</c>）
    /// 仍由模块自己解释取值，但输入一律取 <see cref="RawArgs"/>，不再各自调
    /// <c>Environment.GetCommandLineArgs()</c>——argv 只有这一处读。
    ///
    /// 【跨播放残留】关闭 Domain Reload 时静态字典跨播放存活，由 <see cref="GameEntryPoint"/>
    /// 在进入播放前 <see cref="Reset"/>。
    /// </summary>
    public static class CommandLineOptions
    {
        static readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly HashSet<string> _present = new HashSet<string>(StringComparer.Ordinal);
        static string[] _rawArgs = Array.Empty<string>();
        static bool _parsed;

        /// <summary>解析用的原始 argv（模块自有开关的取值来源；未解析时返回空数组）。</summary>
        public static string[] RawArgs
        {
            get
            {
                EnsureParsed();
                return _rawArgs;
            }
        }

        /// <summary>已出现的开关数量（诊断用）。</summary>
        public static int Count
        {
            get
            {
                EnsureParsed();
                return _present.Count;
            }
        }

        /// <summary>命令行是否出现某开关（不带值也算出现）。</summary>
        public static bool Has(string flag)
        {
            if (string.IsNullOrEmpty(flag))
                return false;

            EnsureParsed();
            return _present.Contains(flag);
        }

        /// <summary>取开关的值；未出现或"出现但无值"返回 null。</summary>
        public static string GetValue(string flag)
        {
            if (string.IsNullOrEmpty(flag))
                return null;

            EnsureParsed();
            return _values.TryGetValue(flag, out string value) ? value : null;
        }

        /// <summary>取整数开关值（缺参/格式不符返回 false）。</summary>
        public static bool TryGetInt(string flag, out int value)
        {
            value = 0;
            string raw = GetValue(flag);
            return raw != null && int.TryParse(raw, out value);
        }

        /// <summary>取浮点开关值（缺参/格式不符返回 false）。</summary>
        public static bool TryGetFloat(string flag, out float value)
        {
            value = 0f;
            string raw = GetValue(flag);
            return raw != null
                   && float.TryParse(raw, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>解析进程 argv（由 <see cref="GameEntryPoint"/> 调用；重复调用只解析一次）。</summary>
        public static void Parse()
        {
            if (_parsed)
                return;

            Parse(Environment.GetCommandLineArgs());
        }

        /// <summary>
        /// 用给定 argv 解析（测试用：不依赖真实进程命令行，断言结果确定）。
        /// 会覆盖上一次的解析结果。
        /// </summary>
        public static void Parse(string[] args)
        {
            _parsed = true;
            _values.Clear();
            _present.Clear();
            _rawArgs = args ?? Array.Empty<string>();

            if (args == null)
                return;

            for (int i = 0; i < args.Length; i++)
            {
                string token = args[i];
                if (string.IsNullOrEmpty(token) || token[0] != '-')
                    continue;

                _present.Add(token);

                bool hasValue = i + 1 < args.Length
                                && !string.IsNullOrEmpty(args[i + 1])
                                && args[i + 1][0] != '-';
                if (!hasValue)
                    continue;

                _values[token] = args[i + 1];
                i++;    // 值不参与下一轮开关识别
            }
        }

        /// <summary>清空解析结果（进入播放前由 <see cref="GameEntryPoint"/> 调用）。</summary>
        public static void Reset()
        {
            _values.Clear();
            _present.Clear();
            _rawArgs = Array.Empty<string>();
            _parsed = false;
        }

        static void EnsureParsed()
        {
            if (!_parsed)
                Parse();
        }
    }

    /// <summary>
    /// 运行期开关名的集中登记（单一事实源：不许在业务代码里散落 <c>"-flag"</c> 字面量）。
    /// 只有**进程级工具模式**在这里；模块私有的调试档常量留在各自模块（见
    /// <see cref="CommandLineOptions"/> 类注释的说明）。
    /// </summary>
    public static class ToolFlags
    {
        /// <summary>指定待战世界海图：<c>-worldMap &lt;id&gt;</c>（无头试玩/捕图）。</summary>
        public const string WorldMap = "-worldMap";

        /// <summary>自动评审出图输出目录：<c>-artReviewOut &lt;绝对目录&gt;</c>（内置播放器）。</summary>
        public const string ArtReviewOut = "-artReviewOut";

        /// <summary>出图用样板关序号：<c>-artReviewLevel &lt;1..ShowcaseLevels.LastLevel&gt;</c>。</summary>
        public const string ArtReviewLevel = "-artReviewLevel";

        /// <summary>等距像素卡通试点出图目录：<c>-toonPilotOut &lt;绝对目录&gt;</c>。</summary>
        public const string ToonPilotOut = "-toonPilotOut";

        /// <summary>
        /// 像素化着色路径（v3 蓝本重写线）试点出图目录：<c>-pixelartOut &lt;绝对目录&gt;</c>。
        /// 进 <c>PixelartPilot</c> 场景，机位与抖动档见 <c>PlayerArtCapture.RunPixelartCapture</c>。
        /// </summary>
        public const string PixelartOut = "-pixelartOut";

        /// <summary>
        /// `-pixelartOut` 的场景切换：<c>-pixelartCloud</c>（无值，出现即生效）⇒ 改拍
        /// <c>PixelartCloud</c>（云彩关 L01 真实内容：云场 + 落水危险线 + 按关卡出生表摆的 7 个船员）。
        /// 不带它时拍 <c>PixelartPilot</c>（图元几何，验机制）。两个档共用同一条采集流程与判据脚本。
        /// </summary>
        public const string PixelartCloud = "-pixelartCloud";

        /// <summary>场景资产样板出图目录：<c>-sceneKitOut &lt;绝对目录&gt;</c>。</summary>
        public const string SceneKitOut = "-sceneKitOut";

        /// <summary>UI 陈列页出图目录：<c>-uiGalleryOut &lt;绝对目录&gt;</c>。</summary>
        public const string UiGalleryOut = "-uiGalleryOut";

        /// <summary>水面 shader 调试档：<c>-oceanDebug &lt;0-13&gt;</c>（模块自有开关，见 OceanRig）。</summary>
        public const string OceanDebug = "-oceanDebug";
    }
}
