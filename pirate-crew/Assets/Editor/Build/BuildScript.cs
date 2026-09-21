using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PirateCrew.EditorTools.BuildSystem
{
    /// <summary>
    /// 一键出包（轨道 E 的主入口）：把「场景集 + PlayerSettings + 版本号 + 构建报告」收成一次调用。
    ///
    /// <para><b>两个入口，同一份逻辑</b></para>
    /// <list type="bullet">
    ///   <item><b>编辑器菜单</b>：<c>PirateCrew/Build/…</c>（下面每个 MenuItem 都调到同一套
    ///         <see cref="Execute"/>，不存在「菜单能跑、CLI 不能跑」的第二条路径）。
    ///         菜单入口吃默认参数（Release 档 / win64 / <see cref="BuildVersion.Current"/>）。</item>
    ///   <item><b>命令行</b>：<c>-executeMethod PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs</c>
    ///         + 一组 <c>-build*</c> 参数（参数表见 <see cref="BuildConfig.Args"/>，CLI 也接受
    ///         <c>-buildCommit</c> 之类 CI 注入值）。CI 与本地发布用这条。</item>
    /// </list>
    ///
    /// <para><b>退出码</b>（batchmode 下由 <c>EditorApplication.Exit</c> 传出，命令行可直接判定）：
    /// <c>0</c> 成功；<c>1</c> 构建失败（Unity 报错，报告里是 <c>Failed</c>）；
    /// <c>2</c> 构建过程抛异常；<c>3</c> 预检失败（版本/场景/后端/图标不合规，构建根本没开始）；
    /// <c>4</c> 参数不合法。</para>
    ///
    /// <para><b>三条刻意的设计选择</b></para>
    /// <list type="number">
    ///   <item><b>不改写 Build Settings</b>：场景列表经 <c>BuildPlayerOptions.scenes</c> 显式传入，
    ///         所以出包既不受 <c>EditorBuildSettings</c> 当前内容影响、也不会把它改掉
    ///         （本机 batchmode 下那个 API 写入不落盘，且 Build Settings 要留着 ToonPilot 供出图）。
    ///         想让两者一致时用菜单里的「同步 Build Settings」。见 <see cref="BuildScenes"/> 的说明。</item>
    ///   <item><b>失败也写报告</b>：报告是排查的起点，只在成功时写等于在最需要它的时候没有它。</item>
    ///   <item><b>构建前读回校验 PlayerSettings</b>：「脚本写了」不等于「引擎接受了」，
    ///         所以写完再读一遍，不一致就当作预检失败——避免产出一个配置与声明不符的包。</item>
    /// </list>
    /// </summary>
    public static class BuildScript
    {
        /// <summary>成功。</summary>
        public const int ExitOk = 0;

        /// <summary>构建失败（Unity 侧有 error，报告 result != Succeeded）。</summary>
        public const int ExitBuildFailed = 1;

        /// <summary>构建过程抛异常。</summary>
        public const int ExitException = 2;

        /// <summary>预检失败（配置/版本/场景/后端/图标不合规，构建未开始）。</summary>
        public const int ExitPreflight = 3;

        /// <summary>命令行参数不合法。</summary>
        public const int ExitBadArgs = 4;

        // ==================================================================
        // 命令行入口（-executeMethod 要求 public static 且无参）
        // ==================================================================

        /// <summary>
        /// 【CI / 本地发布主入口】读命令行参数出 Release 包。
        /// <code>
        /// "F:/Unity/2022.3.62f1c1/Editor/Unity.exe" -batchmode -nographics -quit ^
        ///   -projectPath "F:/VSCode/pirate-crew-3d-unity/pirate-crew" ^
        ///   -executeMethod PirateCrew.EditorTools.BuildSystem.BuildScript.BuildFromCommandLineArgs ^
        ///   -logFile - -buildFlavor release -buildCommit %GIT_SHA%
        /// </code>
        /// </summary>
        public static void BuildFromCommandLineArgs()
        {
            List<string> problems;
            BuildConfig config = BuildConfig.FromCommandLine(out problems);

            if (problems != null && problems.Count > 0)
            {
                LogProblems(problems, "命令行参数不合法");
                Debug.Log(BuildConfig.UsageText());
                Terminate(ExitBadArgs);
                return;
            }

            Terminate(Execute(config, "命令行"));
        }

        /// <summary>无参 CLI 入口：Release 默认档（等价于菜单「一键出包 Windows64（Release）」）。</summary>
        public static void BuildWindows64Release()
        {
            Terminate(Execute(BuildConfig.CreateDefault(BuildFlavor.Release), "CLI/Release 默认档"));
        }

        /// <summary>无参 CLI 入口：Development 默认档。</summary>
        public static void BuildWindows64Development()
        {
            Terminate(Execute(BuildConfig.CreateDefault(BuildFlavor.Development), "CLI/Development 默认档"));
        }

        /// <summary>
        /// 只校验发行场景集与配置，不出包（CI 的快速前置 job 用）。
        /// 退出码 0 = 通过，3 = 有问题。
        /// </summary>
        public static void ValidateReleaseScenes()
        {
            BuildConfig config = BuildConfig.CreateDefault(BuildFlavor.Release);
            List<string> problems = config.Validate();

            if (problems.Count > 0)
            {
                LogProblems(problems, "发行场景集校验未通过");
                Terminate(ExitPreflight);
                return;
            }

            Debug.Log("[Build] 发行场景集校验通过：" + config.Scenes.Length + " 个场景，"
                + "顺序 " + string.Join(" → ", config.Scenes) + "；版本 " + config.Version + "。");
            Terminate(ExitOk);
        }

        /// <summary>只写 PlayerSettings 并读回校验（不出包）；菜单与 CI 都可用。</summary>
        public static void ApplyPlayerSettingsOnly()
        {
            BuildConfig config = BuildConfig.CreateDefault(BuildFlavor.Release);
            List<string> problems = config.Validate();
            if (problems.Count > 0)
            {
                LogProblems(problems, "配置预检未通过");
                Terminate(ExitPreflight);
                return;
            }

            List<string> applied = PlayerPreset.Apply(config, problems);
            if (problems.Count > 0)
            {
                LogProblems(problems, "PlayerSettings 写入失败");
                Terminate(ExitPreflight);
                return;
            }

            Debug.Log("[Build] PlayerSettings 已写入并读回校验通过：\n  " + string.Join("\n  ", applied.ToArray()));
            Terminate(ExitOk);
        }

        // ==================================================================
        // 编辑器菜单入口（与上面同一套逻辑，只是参数取默认值）
        // ==================================================================

        /// <summary>一键出 Release 包（默认参数：win64 / 5 场景发行集 / 当前版本号）。</summary>
        [MenuItem("PirateCrew/Build/一键出包 Windows64（Release）", priority = 100)]
        public static void MenuBuildRelease()
        {
            Execute(BuildConfig.CreateDefault(BuildFlavor.Release), "菜单/Release");
        }

        /// <summary>一键出 Development 包（含 6 场景开发集与调试符号）。</summary>
        [MenuItem("PirateCrew/Build/一键出包 Windows64（Development）", priority = 101)]
        public static void MenuBuildDevelopment()
        {
            Execute(BuildConfig.CreateDefault(BuildFlavor.Development), "菜单/Development");
        }

        /// <summary>打印全部出包参数与用法（照抄进命令行即可）。</summary>
        [MenuItem("PirateCrew/Build/打印出包参数用法", priority = 102)]
        public static void MenuPrintUsage()
        {
            Debug.Log(BuildConfig.UsageText());
            Debug.Log("[Build] 当前默认档: 目标 " + BuildConfig.TargetWin64
                + " · 版本 " + BuildVersion.Current
                + " · 输出 " + BuildConfig.CreateDefault(BuildFlavor.Release).OutputDir);
        }

        /// <summary>校验发行场景集（不发包，秒级）。</summary>
        [MenuItem("PirateCrew/Build/校验发行场景集", priority = 200)]
        public static void MenuValidateScenes()
        {
            ValidateReleaseScenes();
        }

        /// <summary>只写 PlayerSettings 并读回校验。</summary>
        [MenuItem("PirateCrew/Build/写入 PlayerSettings（工业化预设）", priority = 201)]
        public static void MenuApplyPlayerSettings()
        {
            ApplyPlayerSettingsOnly();
        }

        /// <summary>
        /// 把 Build Settings 同步成**开发集**（= 现有 6 场景，含 ToonPilot）。
        /// 这是给「编辑器里按 Play / 播放器出图」用的列表，保持与装配脚本文档一致。
        /// </summary>
        [MenuItem("PirateCrew/Build/同步 Build Settings（开发集 6 场景）", priority = 210)]
        public static void MenuSyncBuildSettingsDevelopment()
        {
            SyncBuildSettings(BuildScenes.DevelopmentSet(), "开发集");
        }

        /// <summary>
        /// 把 Build Settings 同步成**发行集**（5 场景，去掉 ToonPilot）。
        /// 提交发行前的准备动作；之后要跑 ToonPilot 出图请再同步回开发集。
        /// </summary>
        [MenuItem("PirateCrew/Build/同步 Build Settings（发行集 5 场景）", priority = 211)]
        public static void MenuSyncBuildSettingsRelease()
        {
            SyncBuildSettings(BuildScenes.ReleaseSet(), "发行集");
        }

        /// <summary>把场景集写进 <c>EditorBuildSettings</c>。</summary>
        public static void SyncBuildSettings(string[] scenes, string label)
        {
            List<string> problems = BuildScenes.Validate(scenes, false);
            if (problems.Count > 0)
            {
                LogProblems(problems, label + "校验未通过，未改动 Build Settings");
                return;
            }

            var settings = new EditorBuildSettingsScene[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
                settings[i] = new EditorBuildSettingsScene(BuildScenes.PathOf(scenes[i]), true);

            EditorBuildSettings.scenes = settings;
            Debug.Log("[Build] Build Settings 已同步为" + label + "（" + scenes.Length + " 场景）："
                + string.Join(" → ", scenes)
                + "。注意：本机 batchmode 下该 API 写入可能不落盘，请用 git diff 复核 ProjectSettings/EditorBuildSettings.asset。");
        }

        // ==================================================================
        // 核心流程
        // ==================================================================

        /// <summary>
        /// 执行一次构建。返回退出码（菜单路径忽略返回值，batchmode 路径由 <see cref="Terminate"/> 传出）。
        /// </summary>
        public static int Execute(BuildConfig config, string origin)
        {
            Debug.Log("[Build] ===== 出包开始（" + origin + "）=====\n" + Describe(config));

            // ---- 1. 预检：所有「会静默出错」的输入在这里拦下 ----
            var problems = config.Validate();

            string backendDetail;
            if (!PlayerPreset.CheckScriptingBackend(config.ScriptingBackend, out backendDetail))
                problems.Add(backendDetail);

            if (problems.Count > 0)
            {
                LogProblems(problems, "预检未通过，构建未开始");
                return ExitPreflight;
            }

            // ---- 2. 切目标平台（写平台相关 PlayerSettings 的前提） ----
            if (!PlayerPreset.EnsureActiveTarget(problems))
            {
                LogProblems(problems, "切换构建目标失败");
                return ExitPreflight;
            }

            // ---- 3. PlayerSettings 工业化（写 + 读回校验） ----
            var applied = new List<string>();
            if (config.ApplyPlayerSettings)
            {
                applied = PlayerPreset.Apply(config, problems);
                if (problems.Count > 0)
                {
                    LogProblems(problems, "PlayerSettings 写入或读回校验失败");
                    return ExitPreflight;
                }
            }
            else
            {
                applied.Add("（本次用 -buildNoPlayerSettings 跳过了 PlayerSettings 写入）");
            }

            // ---- 4. 构建 ----
            Directory.CreateDirectory(config.OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = config.SceneAssetPaths(),
                locationPathName = config.ExecutablePath,
                target = PlayerPreset.Target,
                targetGroup = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildFlags(config),
            };

            DateTime startedAt = DateTime.UtcNow;
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[Build] 构建过程抛异常，退出码 " + ExitException + "。");
                return ExitException;
            }

            if (report == null)
            {
                Debug.LogError("[Build] BuildPipeline.BuildPlayer 返回 null（没有可用报告）。");
                return ExitException;
            }

            // ---- 5. 打包（仅成功时） ----
            string zipPath = string.Empty;
            long zipBytes = 0;
            if (report.summary.result == BuildResult.Succeeded && config.Zip)
            {
                zipPath = BuildZip(config, out zipBytes);
            }

            // ---- 6. 报告（无论成败都写） ----
            var data = Compose(config, report, applied, zipPath, zipBytes, startedAt);
            BuildReportWriter.Write(data, config.ReportPath, MarkdownPath(config.ReportPath));

            int code = report.summary.result == BuildResult.Succeeded ? ExitOk : ExitBuildFailed;
            if (code == ExitOk)
            {
                Debug.Log("[Build] ===== 出包成功 =====\n"
                    + "  产物: " + config.ExecutablePath + "\n"
                    + "  体积: " + BuildReportWriter.FormatBytes(data.outputSizeBytes)
                    + "（" + data.outputFileCount + " 个文件）· 耗时 "
                    + BuildReportWriter.FormatSeconds(data.buildSeconds) + "\n"
                    + "  报告: " + config.ReportPath);
            }
            else
            {
                Debug.LogError("[Build] ===== 出包失败（result=" + report.summary.result
                    + "，error " + report.summary.totalErrors + " 条）=====\n"
                    + "  报告: " + config.ReportPath + "\n"
                    + report.SummarizeErrors());
            }

            return code;
        }

        /// <summary><see cref="BuildOptions"/> 组合：始终要详细报告，档位与开关在此基础上叠加。</summary>
        static BuildOptions BuildFlags(BuildConfig config)
        {
            BuildOptions flags = BuildOptions.DetailedBuildReport;

            if (config.Flavor == BuildFlavor.Development)
            {
                flags |= BuildOptions.Development;
                flags |= BuildOptions.AllowDebugging;
            }

            if (config.Clean)
                flags |= BuildOptions.CleanBuildCache;

            // StrictMode：构建期只要有 error 就不许成功（Unity 文档原文 "Do not allow the build to
            // succeed if any errors are reporting during it."）。这是「不许出坏包」的兜底——
            // 即使 Unity 想带着 error 收尾，也让它以失败结束，避免退出码 0 却给不出能跑的包。
            flags |= BuildOptions.StrictMode;

            return flags;
        }

        /// <summary>
        /// 打 zip：落在输出目录的**上一级**（<c>external/build/&lt;版本&gt;/PirateCrew3D_v&lt;版本&gt;_win64.zip</c>），
        /// 这样「解压即可玩」的目录结构与压缩包本身不会互相嵌套。
        /// </summary>
        static string BuildZip(BuildConfig config, out long zipBytes)
        {
            zipBytes = 0;
            string root = Path.GetDirectoryName(config.OutputDir.TrimEnd('/'));
            string zipPath = (root + "/" + BuildVersion.ZipName(config.Version)).Replace('\\', '/');

            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                // 全限定：CompressionLevel 在 System.IO.Compression 与 UnityEngine 里同名（CS0104）。
                ZipFile.CreateFromDirectory(config.OutputDir, zipPath,
                    System.IO.Compression.CompressionLevel.Optimal, false);
                zipBytes = new FileInfo(zipPath).Length;
                Debug.Log("[Build] 压缩包: " + zipPath + "（" + BuildReportWriter.FormatBytes(zipBytes) + "）");
                return zipPath;
            }
            catch (Exception e)
            {
                // 压缩失败不影响「包已经出来了」这个事实，只记警告并如实写进报告。
                Debug.LogWarning("[Build] 打 zip 失败（产物本身有效）: " + e.Message);
                return string.Empty;
            }
        }

        /// <summary>组装构建报告数据。</summary>
        static BuildReportData Compose(BuildConfig config, BuildReport report, List<string> applied,
            string zipPath, long zipBytes, DateTime startedAt)
        {
            BuildSummary summary = report.summary;

            var data = new BuildReportData
            {
                schema = BuildVersion.ReportSchema,
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                version = config.Version,
                numericVersion = BuildVersion.NumericVersion(config.Version),
                flavor = config.Flavor.ToString(),
                target = BuildConfig.TargetWin64,
                sceneSet = config.SceneSet,
                scenes = config.Scenes,
                outputPath = config.OutputDir,
                executablePath = config.ExecutablePath,
                zipPath = zipPath,
                zipBytes = zipBytes,
                unityVersion = Application.unityVersion,
                gitCommit = config.Commit,
                gitBranch = config.Branch,
                commitSource = config.CommitSource,
                scriptingBackend = config.ScriptingBackend == "il2cpp" ? "IL2CPP" : "Mono2x",
                appId = config.AppId,
                playerSettings = applied.ToArray(),
                result = summary.result.ToString(),
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                buildSeconds = summary.totalTime.TotalSeconds,
                reportSizeBytes = CastSize(summary.totalSize),
                errorMessages = CollectErrors(report),
            };

            Debug.Log("[Build] BuildReport: result=" + summary.result
                + " platform=" + summary.platform
                + " startedAt=" + summary.buildStartedAt.ToString("o", CultureInfo.InvariantCulture)
                + " 抓取起点=" + startedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

            BuildReportWriter.MeasureOutput(data, config.OutputDir,
                new[] { config.ReportPath.Replace('\\', '/'), MarkdownPath(config.ReportPath) });

            return data;
        }

        /// <summary><c>BuildSummary.totalSize</c> 是 ulong；报告用 long 以匹配 BCL 的字节口径。</summary>
        static long CastSize(ulong bytes)
        {
            return bytes > long.MaxValue ? long.MaxValue : (long)bytes;
        }

        /// <summary>从 BuildReport 的各步骤里收集 error/exception 消息（截断，避免报告被日志淹没）。</summary>
        static string[] CollectErrors(BuildReport report)
        {
            var messages = new List<string>();
            BuildStep[] steps = report.steps;
            if (steps == null)
                return new string[0];

            for (int s = 0; s < steps.Length; s++)
            {
                BuildStepMessage[] stepMessages = steps[s].messages;
                if (stepMessages == null)
                    continue;

                for (int m = 0; m < stepMessages.Length; m++)
                {
                    LogType type = stepMessages[m].type;
                    if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                        continue;

                    messages.Add("[" + steps[s].name + "] " + stepMessages[m].content);
                    if (messages.Count >= 20)
                        return messages.ToArray();
                }
            }

            return messages.ToArray();
        }

        /// <summary>报告 Markdown 的路径（与 JSON 同目录同名，换扩展名）。</summary>
        static string MarkdownPath(string jsonPath)
        {
            string dir = Path.GetDirectoryName(jsonPath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(jsonPath);
            string path = Path.Combine(dir, name + ".md");
            return path.Replace('\\', '/');
        }

        /// <summary>打印配置摘要（出包日志的第一段，出问题时先看这里）。</summary>
        static string Describe(BuildConfig config)
        {
            return "  版本: " + config.Version + "（文件版本 " + BuildVersion.NumericVersion(config.Version) + "）\n"
                + "  构建档: " + config.Flavor + " · 后端: " + config.ScriptingBackend + " · zip: " + (config.Zip ? "是" : "否") + "\n"
                + "  场景集: " + config.SceneSet + "（" + config.Scenes.Length + " 个）: "
                    + string.Join(", ", config.Scenes) + "\n"
                + "  输出: " + config.OutputDir + "\n"
                + "  可执行: " + config.ExecutablePath + "\n"
                + "  报告: " + config.ReportPath + "\n"
                + "  git: " + config.Commit + " @ " + config.Branch + "（来源 " + config.CommitSource + "）\n"
                + "  PlayerSettings: " + (config.ApplyPlayerSettings ? "写入" : "跳过（-buildNoPlayerSettings）");
        }

        static void LogProblems(List<string> problems, string headline)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[Build] ").Append(headline).Append("（").Append(problems.Count).AppendLine(" 项）：");
            for (int i = 0; i < problems.Count; i++)
                sb.Append("  - ").AppendLine(problems[i]);
            Debug.LogError(sb.ToString());
        }

        /// <summary>
        /// 收尾：batchmode 下用 <c>EditorApplication.Exit</c> 把退出码真正传给调用方；
        /// 编辑器 GUI 里只记日志（在 GUI 里 Exit 会把编辑器关掉——那是灾难）。
        /// </summary>
        static void Terminate(int code)
        {
            if (!Application.isBatchMode)
                return;

            EditorApplication.Exit(code);
        }
    }
}
