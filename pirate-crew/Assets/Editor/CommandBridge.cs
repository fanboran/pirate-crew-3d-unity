using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
// UnityEditor.Compilation 与 System.Reflection 都有 Assembly，这里全程指后者
using Assembly = System.Reflection.Assembly;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **命令桥**（常开编辑器的秒级执行通道）：轮询 <c>&lt;仓库根&gt;/export/unity-command.txt</c>，
    /// 每行一条命令，读到即执行、执行完清空命令文件（消费式）。结果追加到
    /// <c>export/unity-command-result.txt</c>，协议：每条命令以 <c>&gt;&gt; 命令</c> 开场、
    /// <c>&lt;&lt; OK 命令</c> / <c>&lt;&lt; FAILED 命令</c> 收尾，细节行夹在中间——
    /// CLI 侧（tools/headless/run.sh 的 bridge / menu 档）靠收尾行判成败，等不到收尾即报
    /// "编辑器不在跑"类错误，绝不静默。
    ///
    /// 命令表（动词封闭白名单）：
    /// <list type="bullet">
    /// <item><c>ping</c> → 探活应答（CLI 先发它，快速区分"编辑器不在跑"与"命令执行得久"）</item>
    /// <item><c>state</c> → 回写编辑器状态（isPlaying / 是否编译中）</item>
    /// <item><c>refresh</c> → AssetDatabase.Refresh()（让新写的脚本/资产进编辑器）</item>
    /// <item><c>recompile</c> → 强制全量重编（编译期 error/exception 转发进结果文件）</item>
    /// <item><c>call 类型全名.方法名</c> → 反射执行 public static void()（无参）</item>
    /// <item><c>menu 菜单路径</c> → EditorApplication.ExecuteMenuItem（如 menu PirateCrew/Scenes/重跑 Battle 装配链）</item>
    /// <item><c>chain 链名或步骤列表</c> → 复用 HeadlessChain.RunChainSpec（与 run.sh chain 同一张注册表，如 chain full）</item>
    /// <item><c>afterplay 类型全名.方法名</c> → 进 Play 后第 95 帧执行（截图流程的诊断窗口）</item>
    /// <item><c>list 类型全名</c> → 列出该类型的 public static 方法</item>
    /// <item><c>probe</c> / <c>play</c> / <c>stopplay</c> → 程序集排查 / 进出 Play</item>
    /// </list>
    ///
    /// 【安全边界】不监听任何网络端口——只消费本仓库 export/ 下的文件（本机文件系统，
    /// 比 localhost TCP 还窄）；命令动词是封闭白名单；反射调用限定 public static 无参方法。
    /// 【装载】[InitializeOnLoad] 随域重载自动装载；batchmode 下整桥禁用（无头进程是一次性的，
    /// 不该替常驻编辑器消费命令）。命令在主线程同步执行——长装配（chain full）期间编辑器
    /// 无响应属预期。
    /// 【绝不调用 EditorApplication.Exit】编辑器由创始人常开，本桥的职责就是不再重启它
    /// （2026-09-23 约定）。
    /// </summary>
    [InitializeOnLoad]
    public static class CommandBridge
    {
        const string CommandFile = "export/unity-command.txt";
        const string ResultFile = "export/unity-command-result.txt";
        const double PollInterval = 0.4;   // 秒

        static double _nextPoll;
        static readonly List<string> _afterplay = new List<string>();
        static int _playFrame;

        // 仓库根 = 工程根的上一级（dataPath 恒为 <工程>/Assets；不依赖进程 CWD，worktree 下同样成立）
        static string RepoDir => Directory.GetParent(Application.dataPath).Parent.FullName;
        static string CommandPath => Path.Combine(RepoDir, CommandFile);
        static string ResultPath => Path.Combine(RepoDir, ResultFile);

        static CommandBridge()
        {
            if (Application.isBatchMode)
                return;   // 无头进程是一次性的，不替常驻编辑器消费命令（与 RemotePlayControl 同口径）
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayStateChanged;
            AppendResult("bridge hooked（命令文件 " + CommandPath + "）");
        }

        static void OnPlayStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && _afterplay.Count > 0)
                _playFrame = 1;   // 开始数帧，到 95 帧执行排队的诊断
        }

        static void Poll()
        {
            if (_playFrame > 0 && EditorApplication.isPlaying && _afterplay.Count > 0)
            {
                _playFrame++;
                if (_playFrame == 95)
                {
                    foreach (string methodName in _afterplay.ToArray())
                        Invoke("call " + methodName);
                    _afterplay.Clear();
                    _playFrame = 0;
                }
            }
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + PollInterval;

            string[] commands;
            try
            {
                if (!File.Exists(CommandPath))
                    return;
                commands = File.ReadAllLines(CommandPath);
            }
            catch (IOException)
            {
                return;   // 写方还在写，下一拍再看
            }

            bool any = false;
            foreach (string raw in commands)
            {
                string command = raw.Trim();
                if (command.Length == 0)
                    continue;
                any = true;
                AppendResult(">> " + command);
                bool ok;
                try
                {
                    ok = Invoke(command);
                }
                catch (Exception ex)
                {
                    // 内建命令自身炸了也必须给收尾行，否则 CLI 会一直等到超时
                    AppendResult("FAILED（命令抛异常）: " + ex.Message + "\n" + ex.StackTrace);
                    ok = false;
                }
                AppendResult("<< " + (ok ? "OK " : "FAILED ") + command);
            }
            if (any)
            {
                // 消费式：执行完清空队列。防重复执行由"读后即清"保证，**不做内容去重**——
                // 同一条命令连跑第二遍（改码 → call 验证 → 再改 → 再 call）是日常迭代常态。
                File.WriteAllText(CommandPath, "");
            }
        }

        /// <summary>分发一条命令，返回执行成败（收尾行由 Poll 统一打）。</summary>
        static bool Invoke(string command)
        {
            if (command == "ping")
            {
                AppendResult("pong（编辑器在跑，命令桥已装载）");
                return true;
            }
            if (command == "state")
            {
                AppendResult("isPlaying=" + EditorApplication.isPlaying
                             + " compiling=" + EditorApplication.isCompiling);
                return true;
            }
            if (command == "refresh")
            {
                AssetDatabase.Refresh();
                AppendResult("refresh ok");
                return true;
            }
            if (command == "recompile")
            {
                // 强制全量重编：怀疑 Bee 缓存陈旧（编辑器里明明有新源码，程序集却是旧的）时用。
                Application.logMessageReceived -= OnCompileLog;
                Application.logMessageReceived += OnCompileLog;
                CompilationPipeline.RequestScriptCompilation();
                AppendResult("recompile requested（编译异步进行，error 会转发到本文件）");
                return true;
            }
            if (command == "probe")
            {
                // 编辑器域内程序集真源排查：到底加载了哪些 Assembly-CSharp-Editor、
                // 里面有没有 UiShowcaseSceneSetup / FontProbeDumper。
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name.Contains("Assembly-CSharp")).ToArray();
                AppendResult("probe: Assembly-CSharp* 加载 " + loaded.Length + " 个");
                foreach (var a in loaded)
                {
                    AppendResult("  " + a.GetName().Name + " @ " + a.Location);
                    var t1 = a.GetType("PirateCrew.EditorTools.UiShowcaseSceneSetup");
                    var t2 = a.GetType("PirateCrew.EditorTools.FontProbeDumper");
                    AppendResult("    UiShowcaseSceneSetup=" + (t1 != null) + " FontProbeDumper=" + (t2 != null));
                }
                return true;
            }
            if (command == "play")
            {
                EditorApplication.isPlaying = true;   // 进 Play 并保持（配合 afterplay 诊断）
                AppendResult("play requested");
                return true;
            }
            if (command == "stopplay")
            {
                EditorApplication.isPlaying = false;
                AppendResult("stop-play requested");
                return true;
            }
            if (command.StartsWith("call ", StringComparison.Ordinal))
                return InvokeStatic(command.Substring(5).Trim());
            if (command.StartsWith("menu ", StringComparison.Ordinal))
                return ExecuteMenu(command.Substring(5).Trim());
            if (command.StartsWith("chain ", StringComparison.Ordinal))
                return RunChain(command.Substring(6).Trim());
            if (command.StartsWith("afterplay ", StringComparison.Ordinal))
            {
                // 进 Play 后第 95 帧执行（截图流程在 frame>90 截图、~110 帧退 Play——
                // 排在这中间，正好把"页面已建、图集已填"的实机状态抓到手）
                _afterplay.Add(command.Substring(10).Trim());
                AppendResult("afterplay queued: " + _afterplay[_afterplay.Count - 1]);
                return true;
            }
            if (command.StartsWith("list ", StringComparison.Ordinal))
                return ListMethods(command.Substring(5).Trim());

            AppendResult("unknown command: " + command);
            return false;
        }

        // 反射执行 public static 无参方法（要求 public static、跨程序集查找）
        static bool InvokeStatic(string typeName)
        {
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot <= 0)
            {
                AppendResult("bad command（应为 call 命名空间.类型.方法）: " + typeName);
                return false;
            }
            string typeOnly = typeName.Substring(0, lastDot);
            string methodName = typeName.Substring(lastDot + 1);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var found = assembly.GetType(typeOnly);
                if (found == null)
                    continue;
                var m = found.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (m == null)
                {
                    AppendResult("方法不存在: " + typeName);
                    return false;
                }
                try
                {
                    m.Invoke(null, null);
                    AppendResult("called " + typeName);
                    return true;
                }
                catch (Exception e)
                {
                    var inner = e.InnerException ?? e;
                    AppendResult("FAILED " + typeName + ": " + inner.GetType().Name + " " + inner.Message
                                 + "\n" + inner.StackTrace);
                    return false;
                }
            }
            AppendResult("类型不存在: " + typeOnly);
            return false;
        }

        // 执行编辑器菜单项（路径须与 MenuItem 声明完全一致，含中文）
        static bool ExecuteMenu(string menuPath)
        {
            bool executed = EditorApplication.ExecuteMenuItem(menuPath);
            AppendResult((executed ? "menu ok: " : "menu FAILED（路径不存在或不可执行）: ") + menuPath);
            return executed;
        }

        // 复用 HeadlessChain 的注册表跑链（batchmode 的 run.sh chain 与这里是同一套解析/执行）
        static bool RunChain(string spec)
        {
            Application.logMessageReceived -= OnChainLog;
            Application.logMessageReceived += OnChainLog;   // 执行期间的 error/warning 转发给 CLI
            int rc;
            try
            {
                rc = HeadlessChain.RunChainSpec(spec);
            }
            finally
            {
                Application.logMessageReceived -= OnChainLog;
            }
            AppendResult("chain rc=" + rc + "（0=全部成功 1=某步失败 2=解析失败；逐步日志见编辑器 Console）"
                         + "；已注册链名: " + string.Join(", ", HeadlessChain.ChainNames()));
            return rc == 0;
        }

        static void OnChainLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                AppendResult("[chain] " + type + ": " + condition.Substring(0, Math.Min(300, condition.Length)));
        }

        // 列出某类型的 public static 方法（辅助发现可 call 的靶子）
        static bool ListMethods(string typeName)
        {
            var found = typeof(CommandBridge).Assembly.GetType(typeName);
            if (found == null)
            {
                AppendResult("list: 类型不存在 " + typeName);
                return false;
            }
            foreach (var m in found.GetMethods(BindingFlags.Public | BindingFlags.Static))
                AppendResult("  " + typeName + "." + m.Name + "()");
            return true;
        }

        static void OnCompileLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || condition.Contains("error CS"))
                AppendResult("[compile] " + type + ": " + condition.Substring(0, Math.Min(300, condition.Length)));
        }

        static void AppendResult(string line)
        {
            try
            {
                File.AppendAllText(ResultPath,
                    DateTime.Now.ToString("HH:mm:ss.fff ") + line + Environment.NewLine);
            }
            catch (IOException)
            {
                // 结果文件被读方占用时丢弃这一条（下一条命令还会再写）
            }
        }
    }
}
