using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PirateCrew.EditorTools.BuildSystem
{
    /// <summary>构建报告里的一条产物文件记录。</summary>
    [Serializable]
    public sealed class BuildReportFile
    {
        /// <summary>相对输出目录的路径。</summary>
        public string path;

        /// <summary>字节数。</summary>
        public long bytes;
    }

    /// <summary>
    /// 构建报告的数据体。字段名直接进 JSON（<c>JsonUtility</c> 用字段名做键），
    /// 所以字段名也是对外契约——解析方（CI、发布脚本）依赖它们。
    ///
    /// 【为什么要报告】「出了个包」这件事需要可复现的证据：包多大、多久、哪个 commit、什么版本、
    /// 哪些场景、哪套 PlayerSettings、有没有 error/warning。没有报告就只能靠人回忆，
    /// 而人回忆的「上次那个包」在排查「这次为什么不一样」时没有用。
    /// </summary>
    [Serializable]
    public sealed class BuildReportData
    {
        /// <summary>报告 schema 版本（<see cref="BuildVersion.ReportSchema"/>）。</summary>
        public int schema;

        /// <summary>报告生成时刻（UTC，ISO 8601）。</summary>
        public string generatedAtUtc;

        /// <summary>版本号（<see cref="BuildVersion.Current"/> 或 <c>-buildVersion</c> 覆盖值）。</summary>
        public string version;

        /// <summary>四段数值版号（Windows 文件版本属性口径）。</summary>
        public string numericVersion;

        /// <summary>构建档：<c>Release</c> / <c>Development</c>。</summary>
        public string flavor;

        /// <summary>目标平台（<c>win64</c>）。</summary>
        public string target;

        /// <summary>场景集来源（<c>-buildScenes</c> 的原始值）。</summary>
        public string sceneSet;

        /// <summary>打进包的场景名（顺序即包内 index）。</summary>
        public string[] scenes;

        /// <summary>产物输出目录（绝对路径）。</summary>
        public string outputPath;

        /// <summary>可执行文件绝对路径。</summary>
        public string executablePath;

        /// <summary>zip 路径；未打包为空串。</summary>
        public string zipPath;

        /// <summary>zip 字节数；未打包为 0。</summary>
        public long zipBytes;

        /// <summary>用的 Unity 版本（<c>Application.unityVersion</c>）。</summary>
        public string unityVersion;

        /// <summary>git commit（CLI 注入或直读 .git）。</summary>
        public string gitCommit;

        /// <summary>git 分支名。</summary>
        public string gitBranch;

        /// <summary>commit 来源：<c>cli</c> / <c>git</c> / <c>unknown</c>。</summary>
        public string commitSource;

        /// <summary>脚本后端（<c>Mono2x</c> / <c>IL2CPP</c>）。</summary>
        public string scriptingBackend;

        /// <summary>应用标识。</summary>
        public string appId;

        /// <summary>本次强制写入的 PlayerSettings 条目（已读回校验）。</summary>
        public string[] playerSettings;

        /// <summary>构建结果（<c>Succeeded</c> / <c>Failed</c> / ...）。</summary>
        public string result;

        /// <summary>构建期 error 与异常总数。</summary>
        public int totalErrors;

        /// <summary>构建期 warning 总数。</summary>
        public int totalWarnings;

        /// <summary>构建耗时（秒，来自 <c>BuildSummary.totalTime</c>）。</summary>
        public double buildSeconds;

        /// <summary>Unity 统计的构建产物字节数（<c>BuildSummary.totalSize</c>）。</summary>
        public long reportSizeBytes;

        /// <summary>输出目录实测总字节数（不含本报告自身）。</summary>
        public long outputSizeBytes;

        /// <summary>输出目录实测文件数（不含本报告自身）。</summary>
        public int outputFileCount;

        /// <summary>最大的若干产物文件（便于一眼看出体积花在哪）。</summary>
        public BuildReportFile[] topFiles;

        /// <summary>构建期的 error 消息（截断到前 20 条，避免报告被日志淹没）。</summary>
        public string[] errorMessages;
    }

    /// <summary>
    /// 构建报告落盘：JSON（机器读，CI 与发布脚本）+ Markdown（人读，贴进 issue / PR / 手册）。
    /// 两者同源，避免「报告里的数字和 README 里写的不一样」。
    /// </summary>
    public static class BuildReportWriter
    {
        /// <summary>体积统计时最多列出的大文件条数。</summary>
        const int TopFileCount = 10;

        /// <summary>error 消息最多保留条数。</summary>
        const int MaxErrorMessages = 20;

        /// <summary>
        /// 把报告写成 <paramref name="jsonPath"/> 与同目录同名 .md。目录不存在会自动创建。
        /// 写报告**不抛异常**：报告写不出来不该让一次成功的构建变成失败。
        /// </summary>
        public static void Write(BuildReportData data, string jsonPath, string markdownPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(jsonPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(jsonPath, JsonUtility.ToJson(data, true), new UTF8EncodingNoBom());
                File.WriteAllText(markdownPath, ToMarkdown(data), new UTF8EncodingNoBom());

                Debug.Log("[Build] 构建报告: " + jsonPath + " / " + markdownPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Build] 构建报告写入失败（构建结果不受影响）: " + e.Message);
            }
        }

        /// <summary>测量输出目录的体积与文件数，填进已跳过报告文件本身的统计。</summary>
        public static void MeasureOutput(BuildReportData data, string outputDir, string[] excludePaths)
        {
            var top = new List<BuildReportFile>();
            long total = 0;
            int count = 0;

            string[] files;
            try
            {
                files = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Build] 统计产物体积失败: " + e.Message);
                data.topFiles = new BuildReportFile[0];
                return;
            }

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i].Replace('\\', '/');
                if (IsExcluded(file, excludePaths))
                    continue;

                long bytes = 0;
                try
                {
                    bytes = new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    continue;
                }

                total += bytes;
                count++;

                string relative = file.StartsWith(outputDir, StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(outputDir.Length).TrimStart('/')
                    : file;
                top.Add(new BuildReportFile { path = relative, bytes = bytes });
            }

            top.Sort((a, b) => b.bytes.CompareTo(a.bytes));
            if (top.Count > TopFileCount)
                top.RemoveRange(TopFileCount, top.Count - TopFileCount);

            data.outputSizeBytes = total;
            data.outputFileCount = count;
            data.topFiles = top.ToArray();
        }

        static bool IsExcluded(string absolutePath, string[] excludePaths)
        {
            if (excludePaths == null)
                return false;

            for (int i = 0; i < excludePaths.Length; i++)
            {
                if (string.IsNullOrEmpty(excludePaths[i]))
                    continue;

                if (string.Equals(absolutePath, excludePaths[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>生成人读的 Markdown 报告。</summary>
        public static string ToMarkdown(BuildReportData data)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("# 构建报告 · Pirate Crew 3D v" + data.version);
            sb.AppendLine();
            sb.Append("> 生成于 ").Append(data.generatedAtUtc).Append(" · schema ").Append(data.schema)
                .Append(" · 由 `PirateCrew/Assets/Editor/Build/BuildScript.cs` 自动生成（不要手改）").AppendLine();
            sb.AppendLine();

            sb.AppendLine("## 结论");
            sb.AppendLine();
            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("| --- | --- |");
            Row(sb, "结果", data.result);
            Row(sb, "构建档", data.flavor);
            Row(sb, "目标平台", data.target);
            Row(sb, "版本号", data.version + "（文件版本 " + data.numericVersion + "）");
            Row(sb, "Unity", data.unityVersion);
            Row(sb, "git", data.gitCommit + " @ " + data.gitBranch + "（来源 " + data.commitSource + "）");
            Row(sb, "耗时", FormatSeconds(data.buildSeconds));
            Row(sb, "error / warning", data.totalErrors + " / " + data.totalWarnings);
            sb.AppendLine();

            sb.AppendLine("## 产物");
            sb.AppendLine();
            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("| --- | --- |");
            Row(sb, "输出目录", "`" + data.outputPath + "`");
            Row(sb, "可执行文件", "`" + data.executablePath + "`");
            if (!string.IsNullOrEmpty(data.zipPath))
                Row(sb, "压缩包", "`" + data.zipPath + "`（" + FormatBytes(data.zipBytes) + "）");
            Row(sb, "目录实测体积", FormatBytes(data.outputSizeBytes) + "（" + data.outputFileCount + " 个文件）");
            Row(sb, "Unity 统计体积", FormatBytes(data.reportSizeBytes));
            sb.AppendLine();

            sb.AppendLine("## 场景清单（包内顺序 = index）");
            sb.AppendLine();
            sb.Append("场景集来源: `").Append(data.sceneSet).AppendLine("`");
            sb.AppendLine();
            for (int i = 0; i < data.scenes.Length; i++)
                sb.Append(i).Append(". `").Append(data.scenes[i]).AppendLine(".unity`");
            sb.AppendLine();

            if (data.topFiles != null && data.topFiles.Length > 0)
            {
                sb.AppendLine("## 体积 TOP（前 " + data.topFiles.Length + "）");
                sb.AppendLine();
                sb.AppendLine("| 文件 | 体积 |");
                sb.AppendLine("| --- | --- |");
                for (int i = 0; i < data.topFiles.Length; i++)
                {
                    sb.Append("| `").Append(data.topFiles[i].path).Append("` | ")
                        .Append(FormatBytes(data.topFiles[i].bytes)).AppendLine(" |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## 本次强制写入的 PlayerSettings");
            sb.AppendLine();
            sb.AppendLine("写入后已读回校验（不一致会在这份报告的 error 里出现）。");
            sb.AppendLine();
            for (int i = 0; i < data.playerSettings.Length; i++)
                sb.Append("- `").Append(data.playerSettings[i]).AppendLine("`");
            sb.AppendLine();

            if (data.errorMessages != null && data.errorMessages.Length > 0)
            {
                sb.AppendLine("## error 摘要");
                sb.AppendLine();
                sb.AppendLine("```");
                for (int i = 0; i < data.errorMessages.Length; i++)
                    sb.AppendLine(data.errorMessages[i]);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        static void Row(System.Text.StringBuilder sb, string key, string value)
        {
            sb.Append("| ").Append(key).Append(" | ").Append(value ?? string.Empty).AppendLine(" |");
        }

        /// <summary>人类可读的体积（1024 进制，与资源管理器口径一致）。</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";

            string[] units = { "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = -1;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        /// <summary>人类可读的耗时。</summary>
        public static string FormatSeconds(double seconds)
        {
            if (seconds < 60)
                return seconds.ToString("0.#", CultureInfo.InvariantCulture) + " 秒";

            int minutes = (int)(seconds / 60);
            double rest = seconds - minutes * 60;
            return minutes + " 分 " + rest.ToString("0.#", CultureInfo.InvariantCulture) + " 秒";
        }

        /// <summary>不带 BOM 的 UTF-8——报告会被 CI 与脚本读取，BOM 只会制造麻烦。</summary>
        sealed class UTF8EncodingNoBom : System.Text.UTF8Encoding
        {
            public UTF8EncodingNoBom() : base(false)
            {
            }
        }
    }
}
