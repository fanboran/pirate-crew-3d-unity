using System;

namespace PirateCrew.EditorTools.BuildSystem
{
    /// <summary>
    /// 版本号与产物命名的**单一真源**（轨道 E）。
    ///
    /// 【为什么要有它】重构前版本号散在三处且互相矛盾（2026-09-21 实测快照）：
    /// <list type="bullet">
    ///   <item><c>ProjectSettings/ProjectSettings.asset</c> 的 <c>bundleVersion</c> = 1.1.0</item>
    ///   <item><c>Assets/Editor/ReleaseGate.cs</c> 常量 = "1.0.0"（重跑该菜单会把 bundleVersion 回写成旧值）</item>
    ///   <item><c>README.md</c> 声明 v0.2.0（对应已发行的 GitHub release tag）</item>
    /// </list>
    /// 用户裁决（2026-09-21 设立单一真源；2026-09-23 起为 0.2.0）：以 <see cref="Current"/> 为准，与 README 及已发行 tag 对齐。
    ///
    /// 【谁读它】构建脚本（写 PlayerSettings.bundleVersion、算产物名、写构建报告）、CI 的 release job、
    /// 文档中的版本号。**任何地方都不许再写字面版本号**——新增一处派生点就多一点漂移。
    ///
    /// 【依赖方向】本类只依赖 BCL，不碰 Unity API，所以能被无头验证台编译与调用（见 docs/项目/开发者指南.md）。
    /// </summary>
    public static class BuildVersion
    {
        /// <summary>
        /// 当前版本号（唯一真源）。改版本只改这一行；构建脚本会把它写进
        /// <c>PlayerSettings.bundleVersion</c>、产物文件名与构建报告。
        /// 格式：<c>major.minor.patch</c>（三段数字，见 <see cref="Validate"/>）。
        /// </summary>
        public const string Current = "0.2.2";

        /// <summary>
        /// 产物基名（不含扩展名与平台后缀）。与 README「从 Releases 下载」一节声明的文件名一致：
        /// <c>PirateCrew3D.exe</c> / <c>PirateCrew3D_v0.2.2_win64.zip</c>。
        /// </summary>
        public const string ProductBaseName = "PirateCrew3D";

        /// <summary>可执行文件名（写入 <c>BuildPlayerOptions.locationPathName</c> 的末段）。</summary>
        public const string ExecutableName = ProductBaseName + ".exe";

        /// <summary>构建报告的 schema 版本（报告字段增删时递增，便于脚本解析方判代）。</summary>
        public const int ReportSchema = 1;

        /// <summary>走包用的压缩包名（与 README 的 release 资产命名一致）。</summary>
        public static string ZipName(string version)
        {
            return ProductBaseName + "_v" + version + "_win64.zip";
        }

        /// <summary>
        /// 四段数值版号（Windows 文件版本属性用）。三段 <c>0.2.2</c> → <c>0.2.2.0</c>。
        /// </summary>
        public static string NumericVersion(string version)
        {
            string[] parts = (version ?? string.Empty).Split('.');
            string major = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : "0";
            string minor = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "0";
            string patch = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : "0";
            return major + "." + minor + "." + patch + ".0";
        }

        /// <summary>
        /// 校验版本号是否合法（三段纯数字 <c>major.minor.patch</c>，每段 ≤ 65535——这是 Windows
        /// 文件版本字段的上限，超了 Unity 构建期会自行截断进而与文档不一致）。
        /// 返回 false 时 <paramref name="error"/> 给出可诊断的原因。
        /// </summary>
        public static bool Validate(string version, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(version))
            {
                error = "版本号为空。写法: -buildVersion 0.1.0";
                return false;
            }

            string[] parts = version.Split('.');
            if (parts.Length != 3)
            {
                error = "版本号必须是三段 major.minor.patch，实际收到 \"" + version + "\"（" + parts.Length + " 段）";
                return false;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0)
                {
                    error = "版本号第 " + (i + 1) + " 段为空: \"" + version + "\"";
                    return false;
                }

                for (int c = 0; c < part.Length; c++)
                {
                    if (part[c] < '0' || part[c] > '9')
                    {
                        error = "版本号第 " + (i + 1) + " 段含非数字字符: \"" + version + "\"";
                        return false;
                    }
                }

                int value;
                if (!int.TryParse(part, out value) || value > 65535)
                {
                    error = "版本号第 " + (i + 1) + " 段超出 0–65535: \"" + version + "\"";
                    return false;
                }
            }

            return true;
        }
    }
}
