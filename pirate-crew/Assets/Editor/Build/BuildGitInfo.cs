using System;
using System.IO;

namespace PirateCrew.EditorTools.BuildSystem
{
    /// <summary>
    /// 构建报告里的 git 信息（commit / 分支）。
    ///
    /// 【为什么直读 <c>.git</c> 而不起 <c>git</c> 子进程】Unity 编辑器里 <c>System.Diagnostics.Process</c>
    /// 启动外部进程在 batchmode 下有卡住的风险（等不到子进程退出就是一次挂死的 CI），
    /// 而我们要的东西就在两个纯文本文件里：<c>.git/HEAD</c> 与 <c>.git/refs/heads/&lt;分支&gt;</c>。
    /// 直读是同步、无副作用、零依赖的。
    ///
    /// 【CI 用 <c>-buildCommit</c> 注入】GitHub Actions 的 checkout 在分离 HEAD 上跑，读到的是 tag 的 commit；
    /// 想标记「构建自哪个 PR/分支」就必须由 CI 显式传（<c>-buildCommit ${{ github.sha }}</c>）。
    /// 注入值优先于直读值，且报告里用 <c>commitSource</c> 如实标注来源是 <c>cli</c> 还是 <c>git</c>。
    /// </summary>
    public static class BuildGitInfo
    {
        /// <summary>把 git 信息填进配置（已有 CLI 注入值时不覆盖）。</summary>
        public static void Fill(BuildConfig config)
        {
            if (config == null)
                return;

            string gitDir = FindGitDir(BuildConfig.ProjectRoot());
            if (string.IsNullOrEmpty(gitDir))
            {
                if (string.IsNullOrEmpty(config.Commit))
                    config.CommitSource = "unknown";
                if (string.IsNullOrEmpty(config.Branch))
                    config.Branch = "unknown";
                return;
            }

            if (string.IsNullOrEmpty(config.Commit))
            {
                string commit = ReadHeadCommit(gitDir);
                if (!string.IsNullOrEmpty(commit))
                {
                    config.Commit = commit;
                    config.CommitSource = "git";
                }
                else
                {
                    config.CommitSource = "unknown";
                }
            }

            if (string.IsNullOrEmpty(config.Branch))
                config.Branch = ReadHeadBranch(gitDir) ?? "unknown";

            if (string.IsNullOrEmpty(config.Commit))
                config.Commit = "unknown";
        }

        /// <summary>从 <paramref name="startDir"/> 向上找 <c>.git</c>，返回其目录路径（处理 gitdir 指针文件）。</summary>
        public static string FindGitDir(string startDir)
        {
            if (string.IsNullOrEmpty(startDir))
                return null;

            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(candidate))
                    return candidate.Replace('\\', '/');

                if (File.Exists(candidate))
                {
                    // worktree / submodule：.git 是指向真实 gitdir 的文件
                    string gitDir = ReadGitDirPointer(candidate, dir.FullName);
                    if (!string.IsNullOrEmpty(gitDir))
                        return gitDir;
                }

                dir = dir.Parent;
            }

            return null;
        }

        static string ReadGitDirPointer(string pointerFile, string relativeTo)
        {
            try
            {
                string[] lines = File.ReadAllLines(pointerFile);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    const string prefix = "gitdir:";
                    if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string path = line.Substring(prefix.Length).Trim();
                    if (path.Length == 0)
                        return null;

                    if (!Path.IsPathRooted(path))
                        path = Path.Combine(relativeTo, path);

                    return path.Replace('\\', '/');
                }
            }
            catch (IOException)
            {
            }

            return null;
        }

        /// <summary>读 HEAD 指向的 commit（分离 HEAD 时 HEAD 本身就是 sha）。</summary>
        public static string ReadHeadCommit(string gitDir)
        {
            string head = ReadFirstLine(Path.Combine(gitDir, "HEAD"));
            if (string.IsNullOrEmpty(head))
                return null;

            const string refPrefix = "ref:";
            if (head.StartsWith(refPrefix, StringComparison.Ordinal))
            {
                string refName = head.Substring(refPrefix.Length).Trim();

                string loose = ReadFirstLine(Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar)));
                if (!string.IsNullOrEmpty(loose) && loose.Length >= 7)
                    return loose;

                return ReadPackedRef(gitDir, refName);
            }

            return head.Length >= 7 ? head : null;
        }

        /// <summary>读 HEAD 的分支名；分离 HEAD 返回 null（如实表示「不知道在哪个分支」）。</summary>
        public static string ReadHeadBranch(string gitDir)
        {
            string head = ReadFirstLine(Path.Combine(gitDir, "HEAD"));
            if (string.IsNullOrEmpty(head))
                return null;

            const string refPrefix = "ref:";
            if (!head.StartsWith(refPrefix, StringComparison.Ordinal))
                return null; // 分离 HEAD

            string refName = head.Substring(refPrefix.Length).Trim();
            const string headPrefix = "refs/heads/";
            return refName.StartsWith(headPrefix, StringComparison.Ordinal)
                ? refName.Substring(headPrefix.Length)
                : refName;
        }

        static string ReadPackedRef(string gitDir, string refName)
        {
            try
            {
                string path = Path.Combine(gitDir, "packed-refs");
                if (!File.Exists(path))
                    return null;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == '^')
                        continue;

                    int space = line.IndexOf(' ');
                    if (space <= 0)
                        continue;

                    if (string.Equals(line.Substring(space + 1).Trim(), refName, StringComparison.Ordinal))
                        return line.Substring(0, space);
                }
            }
            catch (IOException)
            {
            }

            return null;
        }

        static string ReadFirstLine(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                using (var reader = new StreamReader(path))
                {
                    string line = reader.ReadLine();
                    return line == null ? null : line.Trim();
                }
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
