using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PirateCrew.EditorTools.BuildSystem
{
    /// <summary>构建档：决定 <c>BuildOptions</c> 与 PlayerSettings 的调试/剥离取向。</summary>
    public enum BuildFlavor
    {
        /// <summary>发行档：无调试符号、无玩家日志以外的诊断开销、托管剥离开 Low。</summary>
        Release = 0,

        /// <summary>开发档：<c>BuildOptions.Development</c> + 允许脚本调试，托管剥离关闭（栈帧完整）。</summary>
        Development = 1,
    }

    /// <summary>
    /// 一条命令行参数的定义。既驱动解析，也驱动 <see cref="BuildConfig.UsageText"/>——
    /// 参数表与文档同源，避免「文档里写的参数代码不认」。
    /// </summary>
    public sealed class BuildArg
    {
        /// <summary>规范名（含前导短横线，例 <c>-buildVersion</c>）。</summary>
        public readonly string Name;

        /// <summary>别名（含前导短横线）；无别名为 null。别名解析后归一到 <see cref="Name"/>。</summary>
        public readonly string Alias;

        /// <summary>true = 开关（出现即为真，不吃下一个 token）；false = 需要值。</summary>
        public readonly bool IsSwitch;

        /// <summary>值的占位说明（开关为 null）。</summary>
        public readonly string ValueHint;

        /// <summary>缺省值描述（开关为 null）。</summary>
        public readonly string Default;

        /// <summary>一句话说明。</summary>
        public readonly string Description;

        public BuildArg(string name, string alias, bool isSwitch, string valueHint, string @default, string description)
        {
            Name = name;
            Alias = alias;
            IsSwitch = isSwitch;
            ValueHint = valueHint;
            Default = @default;
            Description = description;
        }
    }

    /// <summary>
    /// 一次出包的全部输入：目标平台、构建档、版本、输出目录、场景集、CI 注入的 git 信息、
    /// 以及「要不要顺带改 PlayerSettings / Build Settings / 打 zip」这类副作用开关。
    ///
    /// 【为什么参数表写在 C# 里而不是 YAML/JSON】出包参数与构建逻辑必须同版本演进：
    /// 表在代码里，参数改名时编译器会同时逼着改 <see cref="BuildConfig"/> 的读取点与这里的说明；
    /// 表在外部配置文件里，改名就是一次静默的「参数被忽略」。
    /// </summary>
    public sealed class BuildConfig
    {
        /// <summary>本平台唯一支持的目标（轨道 E 只做 Windows 64 位，其余平台未验证不假装支持）。</summary>
        public const string TargetWin64 = "win64";

        /// <summary>参数总表（解析 + 用法文本的唯一来源）。</summary>
        public static readonly BuildArg[] Args =
        {
            new BuildArg("-buildTarget", null, false, "win64", "win64",
                "目标平台。当前仅 win64（Windows 64 位 Standalone）。"),
            new BuildArg("-buildFlavor", null, false, "release|development", "release",
                "构建档。development 会开 BuildOptions.Development + AllowDebugging、关托管剥离。"),
            new BuildArg("-buildVersion", "-pcVersion", false, "0.1.0", BuildVersion.Current,
                "覆盖版本号（三段 major.minor.patch）。缺省用 BuildVersion.Current。"),
            new BuildArg("-buildOutput", null, false, "<dir>", "<仓库根>/external/build/<版本>/win64",
                "产物输出目录。相对路径按仓库根解析。"),
            new BuildArg("-buildScenes", null, false, "release|development|a,b,c", "release",
                "场景集：release（5 场景，不含 ToonPilot）/ development（6 场景）/ 逗号分隔的场景名。"),
            new BuildArg("-buildScriptingBackend", null, false, "mono|il2cpp", "mono",
                "脚本后端。本机 Unity 只装了 Mono 变体，il2cpp 会在预检阶段明确报错而不是构建到一半失败。"),
            new BuildArg("-buildCommit", null, false, "<sha>", "读 .git/HEAD",
                "写进构建报告的 commit（CI 注入；缺省回退读 .git）。"),
            new BuildArg("-buildBranch", null, false, "<name>", "读 .git/HEAD",
                "写进构建报告的分支名（CI 注入；缺省回退读 .git）。"),
            new BuildArg("-buildAppId", null, false, "<com.company.product>", "com.boranfan.piratecrew3d",
                "应用标识（PlayerSettings.applicationIdentifier，Standalone 组）。"),
            new BuildArg("-buildClean", null, true, null, "关",
                "附加 BuildOptions.CleanBuildCache：丢弃增量构建缓存，用于排除脏缓存造成的怪问题。"),
            new BuildArg("-buildZip", null, true, null, "release 档默认开，development 档默认关",
                "构建成功后把产物目录压成 <仓库根>/external/build/<版本>/PirateCrew3D_v<版本>_win64.zip。"),
            new BuildArg("-buildNoPlayerSettings", null, true, null, "关",
                "跳过 PlayerSettings 写入。给「只想验证构建、不许工程被改写」的场合用。"),
            new BuildArg("-buildReport", null, false, "<path>", "<输出目录>/build-report.json",
                "构建报告 JSON 路径（同目录会另写一份 .md）。"),
        };

        /// <summary>构建档。</summary>
        public BuildFlavor Flavor = BuildFlavor.Release;

        /// <summary>版本号（<see cref="BuildVersion.Validate"/> 已通过）。</summary>
        public string Version = BuildVersion.Current;

        /// <summary>产物输出目录（绝对路径）。</summary>
        public string OutputDir;

        /// <summary>可执行文件绝对路径。</summary>
        public string ExecutablePath;

        /// <summary>解析后的场景名数组（顺序即包内 index）。</summary>
        public string[] Scenes;

        /// <summary><c>-buildScenes</c> 的原始值，写进构建报告便于追溯。</summary>
        public string SceneSet = "release";

        /// <summary>git commit（CI 注入优先，否则读 .git）。</summary>
        public string Commit;

        /// <summary>git 分支名。</summary>
        public string Branch;

        /// <summary>commit 的来源（<c>cli</c> / <c>git</c> / <c>unknown</c>），报告里如实交代。</summary>
        public string CommitSource = "unknown";

        /// <summary>脚本后端。</summary>
        public string ScriptingBackend = "mono";

        /// <summary>应用标识。</summary>
        public string AppId = "com.boranfan.piratecrew3d";

        /// <summary>是否附加 <c>BuildOptions.CleanBuildCache</c>。</summary>
        public bool Clean;

        /// <summary>是否在构建成功后打 zip。</summary>
        public bool Zip;

        /// <summary>是否写入 PlayerSettings（false = <c>-buildNoPlayerSettings</c>）。</summary>
        public bool ApplyPlayerSettings = true;

        /// <summary>构建报告 JSON 路径。</summary>
        public string ReportPath;

        /// <summary>仓库根目录（Unity 工程目录的上一级）。</summary>
        public static string RepoRoot()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
                return Application.dataPath;

            string parent = Path.GetDirectoryName(projectRoot);
            return (string.IsNullOrEmpty(parent) ? projectRoot : parent).Replace('\\', '/');
        }

        /// <summary>Unity 工程目录（<c>pirate-crew/</c>）。</summary>
        public static string ProjectRoot()
        {
            return (Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath).Replace('\\', '/');
        }

        /// <summary>提交给 <c>BuildPlayerOptions</c> 的场景资产路径（带扩展名）。</summary>
        public string[] SceneAssetPaths()
        {
            var paths = new string[Scenes.Length];
            for (int i = 0; i < Scenes.Length; i++)
                paths[i] = BuildScenes.PathOf(Scenes[i]);
            return paths;
        }

        /// <summary>菜单/无参 CLI 入口用的默认配置。</summary>
        public static BuildConfig CreateDefault(BuildFlavor flavor)
        {
            var config = new BuildConfig { Flavor = flavor };
            Finalize(config);
            return config;
        }

        /// <summary>
        /// 从 <see cref="Environment.GetCommandLineArgs"/> 解析。
        /// 返回 null 表示参数本身不合法（<paramref name="problems"/> 已列明原因，调用方直接失败退出）。
        /// </summary>
        public static BuildConfig FromCommandLine(out List<string> problems)
        {
            return Parse(Environment.GetCommandLineArgs(), out problems);
        }

        /// <summary>
        /// 解析参数数组。支持 <c>-name value</c> 与 <c>-name=value</c> 两种写法；
        /// 未出现在 <see cref="Args"/> 里的 token 一律忽略（Unity 自带参数与未知参数都在此列）。
        /// </summary>
        public static BuildConfig Parse(string[] argv, out List<string> problems)
        {
            problems = new List<string>();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 规范名 / 别名 → 规范名
            var lookup = new Dictionary<string, BuildArg>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Args.Length; i++)
            {
                lookup[Args[i].Name] = Args[i];
                if (!string.IsNullOrEmpty(Args[i].Alias))
                    lookup[Args[i].Alias] = Args[i];
            }

            if (argv != null)
            {
                for (int i = 0; i < argv.Length; i++)
                {
                    string token = argv[i];
                    if (string.IsNullOrEmpty(token) || token[0] != '-')
                        continue;

                    string name = token;
                    string inlineValue = null;
                    int eq = token.IndexOf('=');
                    if (eq > 0)
                    {
                        name = token.Substring(0, eq);
                        inlineValue = token.Substring(eq + 1);
                    }

                    BuildArg spec;
                    if (!lookup.TryGetValue(name, out spec))
                        continue; // Unity 自身参数或本表未定义：静默忽略

                    if (spec.IsSwitch)
                    {
                        values[spec.Name] = "true";
                        continue;
                    }

                    if (inlineValue != null)
                    {
                        values[spec.Name] = inlineValue;
                        continue;
                    }

                    if (i + 1 >= argv.Length)
                    {
                        problems.Add("参数 " + name + " 缺值（需要 " + spec.ValueHint + "）。");
                        continue;
                    }

                    values[spec.Name] = argv[++i];
                }
            }

            var config = new BuildConfig();

            string raw;
            if (values.TryGetValue("-buildTarget", out raw) && !string.IsNullOrEmpty(raw))
            {
                if (!string.Equals(raw, TargetWin64, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(raw, "windows64", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(raw, "win", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add("不支持的目标平台 \"" + raw + "\"；当前只有 " + TargetWin64
                        + "（其余平台未做验证，不在此假装支持）。");
                }
            }

            if (values.TryGetValue("-buildFlavor", out raw) && !string.IsNullOrEmpty(raw))
            {
                if (string.Equals(raw, "release", StringComparison.OrdinalIgnoreCase))
                    config.Flavor = BuildFlavor.Release;
                else if (string.Equals(raw, "development", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(raw, "dev", StringComparison.OrdinalIgnoreCase))
                    config.Flavor = BuildFlavor.Development;
                else
                    problems.Add("未知构建档 \"" + raw + "\"；可用 release | development。");
            }

            if (values.TryGetValue("-buildVersion", out raw) && !string.IsNullOrEmpty(raw))
                config.Version = raw;

            if (values.TryGetValue("-buildOutput", out raw) && !string.IsNullOrEmpty(raw))
                config.OutputDir = raw;

            if (values.TryGetValue("-buildScenes", out raw) && !string.IsNullOrEmpty(raw))
                config.SceneSet = raw;

            if (values.TryGetValue("-buildScriptingBackend", out raw) && !string.IsNullOrEmpty(raw))
                config.ScriptingBackend = raw.ToLowerInvariant();

            if (values.TryGetValue("-buildAppId", out raw) && !string.IsNullOrEmpty(raw))
                config.AppId = raw;

            if (values.TryGetValue("-buildCommit", out raw) && !string.IsNullOrEmpty(raw))
            {
                config.Commit = raw;
                config.CommitSource = "cli";
            }

            if (values.TryGetValue("-buildBranch", out raw) && !string.IsNullOrEmpty(raw))
                config.Branch = raw;

            if (values.TryGetValue("-buildReport", out raw) && !string.IsNullOrEmpty(raw))
                config.ReportPath = raw;

            config.Clean = values.ContainsKey("-buildClean");
            config.ApplyPlayerSettings = !values.ContainsKey("-buildNoPlayerSettings");

            // zip：只有显式给了 -buildZip 才开（菜单/默认档的开关在 Finalize 里按构建档定）
            config.Zip = values.ContainsKey("-buildZip");
            config.ZipExplicit = values.ContainsKey("-buildZip");

            Finalize(config);
            Validate(config, problems);
            return config;
        }

        /// <summary><c>-buildZip</c> 是否被显式指定过（否则由构建档决定默认）。</summary>
        public bool ZipExplicit;

        /// <summary>把相对路径补成全路径、解析场景集、回填 git 信息与派生的产物名。</summary>
        static void Finalize(BuildConfig config)
        {
            if (string.IsNullOrEmpty(config.OutputDir))
            {
                config.OutputDir = RepoRoot() + "/external/build/" + config.Version + "/win64";
            }
            else if (!Path.IsPathRooted(config.OutputDir))
            {
                config.OutputDir = RepoRoot() + "/" + config.OutputDir.TrimStart('/', '\\');
            }

            config.OutputDir = config.OutputDir.Replace('\\', '/').TrimEnd('/');
            config.ExecutablePath = config.OutputDir + "/" + BuildVersion.ExecutableName;

            if (!config.ZipExplicit)
                config.Zip = config.Flavor == BuildFlavor.Release;

            string[] scenes;
            string error;
            if (BuildScenes.TryResolveSet(config.SceneSet, out scenes, out error))
                config.Scenes = scenes;
            else
                config.Scenes = new string[0];

            if (string.IsNullOrEmpty(config.ReportPath))
                config.ReportPath = config.OutputDir + "/build-report.json";
            else if (!Path.IsPathRooted(config.ReportPath))
                config.ReportPath = RepoRoot() + "/" + config.ReportPath.TrimStart('/', '\\');

            BuildGitInfo.Fill(config);
        }

        /// <summary>配置合法性校验：把「会静默出错」的输入拦在构建之前。</summary>
        static void Validate(BuildConfig config, List<string> problems)
        {
            string error;
            if (!BuildVersion.Validate(config.Version, out error))
                problems.Add(error);

            if (config.Scenes == null || config.Scenes.Length == 0)
                problems.Add("场景集解析失败: \"" + config.SceneSet + "\"");
            else
                problems.AddRange(BuildScenes.Validate(config.Scenes, config.Flavor == BuildFlavor.Release));

            if (config.ScriptingBackend != "mono" && config.ScriptingBackend != "il2cpp")
                problems.Add("未知脚本后端 \"" + config.ScriptingBackend + "\"；可用 mono | il2cpp。");

            if (string.IsNullOrWhiteSpace(config.AppId) || config.AppId.IndexOf('.') < 0)
                problems.Add("应用标识 \"" + config.AppId + "\" 不合法；应形如 com.boranfan.piratecrew3d。");

            if (config.AppId != "com.boranfan.piratecrew3d")
            {
                Debug.LogWarning("[Build] 应用标识非默认值（" + config.AppId
                    + "）。默认值 com.boranfan.piratecrew3d 是【提案/待定】，见 docs/项目/构建与发布手册.md。");
            }
        }

        /// <summary>
        /// 实例校验：菜单入口与默认档走的是 <see cref="CreateDefault"/>，没经过 <see cref="Parse"/>，
        /// 所以校验必须能独立调用——<c>Execute</c> 两条路径共用这一份。
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            Validate(this, problems);
            return problems;
        }

        /// <summary>参数用法文本（由 <see cref="Args"/> 生成，与解析逻辑同源）。</summary>
        public static string UsageText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("PirateCrew 出包参数（未知参数忽略，可写 -name value 或 -name=value）：");
            for (int i = 0; i < Args.Length; i++)
            {
                BuildArg arg = Args[i];
                string head = arg.IsSwitch
                    ? "  " + arg.Name
                    : "  " + arg.Name + (arg.ValueHint == null ? string.Empty : " " + arg.ValueHint);
                sb.Append(head.PadRight(42)).Append("缺省: ").Append(arg.Default).AppendLine();
                sb.Append("      ").AppendLine(arg.Description);
                if (!string.IsNullOrEmpty(arg.Alias))
                    sb.Append("      别名: ").AppendLine(arg.Alias);
            }
            return sb.ToString();
        }
    }
}
