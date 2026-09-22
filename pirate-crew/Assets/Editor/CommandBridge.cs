using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **命令桥**（常开编辑器的秒级执行通道）：轮询 <c>&lt;仓库根&gt;/export/unity-command.txt</c>，
    /// 支持三行内命令——
    ///   · <c>refresh</c>            → AssetDatabase.Refresh()（让新写的脚本/资产进编辑器）
    ///   · <c>call 类型全名.方法名</c> → 反射执行 public static void()（无参）
    ///   · <c>state</c>              → 回写当前编辑器状态（isPlaying / 是否编译中）
    /// 结果追加到 <c>export/unity-command-result.txt</c>。**绝不调用 EditorApplication.Exit**
    /// （编辑器由创始人常开，本桥的职责就是不再重启它——2026-09-23 约定）。
    /// 命令按"内容哈希"去重：写同样的命令不会重复执行。
    /// </summary>
    [InitializeOnLoad]
    public static class CommandBridge
    {
        const string CommandFile = "export/unity-command.txt";
        const string ResultFile = "export/unity-command-result.txt";
        const double PollInterval = 0.4;   // 秒

        static string _lastCommand;
        static double _nextPoll;
        static readonly System.Collections.Generic.List<string> _afterplay = new System.Collections.Generic.List<string>();
        static PlayModeStateChange _lastPlayState;
        static int _playFrame;

        static string ProjectDir => Path.GetFullPath(".");
        static string RepoDir => Path.GetFullPath("..");
        static string CommandPath => Path.Combine(RepoDir, CommandFile);
        static string ResultPath => Path.Combine(RepoDir, ResultFile);

        static CommandBridge()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayStateChanged;
            AppendResult("bridge hooked");
        }

        static void OnPlayStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && _afterplay.Count > 0)
                _playFrame = 1;   // 开始数帧，到 95 帧执行排队的诊断
        }

        static void Poll()
        {
            if (_playFrame > 0 && EditorApplication.isPlaying && !_afterplay.Count.Equals(0))
            {
                _playFrame++;
                if (_playFrame == 95)
                {
                    foreach (string methodName in _afterplay.ToArray())
                        Invoke(methodName);
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
                if (command.Length == 0 || command == _lastCommand)
                    continue;
                any = true;
                AppendResult(">> " + command);
                Invoke(command);
                _lastCommand = command;
            }
            if (any)
            {
                // 消费式：执行完清空队列（域重载后 _lastCommand 归零也不会重复执行）
                File.WriteAllText(CommandPath, "");
            }
        }

        static void Invoke(string command)
        {
            if (command == "refresh" || command == "state" || command == "recompile"
                || command.StartsWith("afterplay ", System.StringComparison.Ordinal)
                || command.StartsWith("list ", System.StringComparison.Ordinal))
            {
                Execute(command);
                return;
            }
            if (command.StartsWith("call ", System.StringComparison.Ordinal))
            {
                InvokeStatic(command.Substring(5).Trim());
                return;
            }
            AppendResult("unknown command: " + command);
        }

        static void InvokeStatic(string typeName)
        {
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot <= 0)
            {
                AppendResult("bad command（应为 call 命名空间.类型.方法）: " + typeName);
                return;
            }
            string typeOnly = typeName.Substring(0, lastDot);
            string methodName = typeName.Substring(lastDot + 1);
            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var found = assembly.GetType(typeOnly);
                if (found == null)
                    continue;
                var m = found.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m == null)
                {
                    AppendResult("方法不存在: " + typeName);
                    return;
                }
                try
                {
                    m.Invoke(null, null);
                    AppendResult("called " + typeName);
                }
                catch (System.Exception e)
                {
                    var inner = e.InnerException ?? e;
                    AppendResult("FAILED " + typeName + ": " + inner.GetType().Name + " " + inner.Message + "\n" + inner.StackTrace);
                }
                return;
            }
            AppendResult("类型不存在: " + typeName);
        }

        static string methodOf(string typeName)
        {
            int dot = typeName.LastIndexOf('.');
            return dot <= 0 ? typeName : typeName.Substring(dot + 1);
        }

        static void Execute(string command)
        {
            if (command == "refresh")
            {
                AssetDatabase.Refresh();
                AppendResult("refresh ok");
                return;
            }
            if (command.StartsWith("list ", System.StringComparison.Ordinal))
            {
                string typeName = command.Substring(5).Trim();
                foreach (System.Reflection.Assembly assembly in new[] { typeof(CommandBridge).Assembly })
                {
                    var found = assembly.GetType(typeName);
                    if (found == null)
                    {
                        AppendResult("list: 类型不存在 " + typeName);
                        return;
                    }
                    foreach (var m in found.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        AppendResult("  " + typeName + "." + m.Name + "()");
                }
                return;
            }
            if (command == "recompile")
            {
                // 强制全量重编：怀疑 Bee 缓存陈旧（编辑器里明明有新源码，程序集却是旧的）时用。
                Application.logMessageReceived -= OnCompileLog;
                Application.logMessageReceived += OnCompileLog;
                CompilationPipeline.RequestScriptCompilation();
                AppendResult("recompile requested");
                return;
            }
            if (command.StartsWith("afterplay ", System.StringComparison.Ordinal))
            {
                // 进 Play 后第 95 帧执行（截图流程在 frame>90 截图、~110 帧退 Play——
                // 排在这中间，正好把"页面已建、图集已填"的实机状态抓到手）
                _afterplay.Add(command.Substring(10).Trim());
                AppendResult("afterplay queued: " + _afterplay[_afterplay.Count - 1]);
                return;
            }
            if (command == "probe")
            {
                // 编辑器域内程序集真源排查：到底加载了哪些 Assembly-CSharp-Editor、
                // 里面有没有 UiShowcaseSceneSetup / FontProbeDumper。
                var loaded = System.AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name.Contains("Assembly-CSharp")).ToArray();
                AppendResult("probe: Assembly-CSharp* 加载 " + loaded.Length + " 个");
                foreach (var a in loaded)
                {
                    AppendResult("  " + a.GetName().Name + " @ " + a.Location);
                    var t1 = a.GetType("PirateCrew.EditorTools.UiShowcaseSceneSetup");
                    var t2 = a.GetType("PirateCrew.EditorTools.FontProbeDumper");
                    AppendResult("    UiShowcaseSceneSetup=" + (t1 != null) + " FontProbeDumper=" + (t2 != null));
                }
                return;
            }
            if (command == "play")
            {
                EditorApplication.isPlaying = true;   // 进 Play 并保持（配合 afterplay 诊断）
                AppendResult("play requested");
                return;
            }
            if (command == "stopplay")
            {
                EditorApplication.isPlaying = false;
                AppendResult("stop-play requested");
                return;
            }
            if (command == "state")
            {
                AppendResult("isPlaying=" + EditorApplication.isPlaying
                    + " compiling=" + EditorApplication.isCompiling);
                return;
            }
            if (command.StartsWith("call ", System.StringComparison.Ordinal))
            {
                string typeName = command.Substring(5).Trim();
                int dot = typeName.LastIndexOf('.');
                if (dot <= 0)
                {
                    AppendResult("bad command（应为 call 命名空间.类型.方法）: " + typeName);
                    return;
                }
                string type = typeName.Substring(0, dot);
                string method = typeName.Substring(dot + 1);
                foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var found = assembly.GetType(type);
                    if (found == null)
                        continue;
                    var m = found.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
                    if (m == null)
                    {
                        AppendResult("方法不存在: " + typeName);
                        return;
                    }
                    try
                    {
                        m.Invoke(null, null);
                        AppendResult("called " + typeName);
                    }
                    catch (System.Exception e)
                    {
                        AppendResult("FAILED " + typeName + ": " + e.Message + "\n" + e.StackTrace);
                    }
                    return;
                }
                AppendResult("类型不存在: " + type);
                return;
            }
            AppendResult("unknown command: " + command);
        }

        static void OnCompileLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || condition.Contains("error CS"))
                AppendResult("[compile] " + type + ": " + condition.Substring(0, System.Math.Min(300, condition.Length)));
        }

        static void AppendResult(string line)
        {
            try
            {
                File.AppendAllText(ResultPath,
                    System.DateTime.Now.ToString("HH:mm:ss.fff ") + line + System.Environment.NewLine);
            }
            catch (IOException)
            {
                // 结果文件被读方占用时丢弃这一条（下一条命令还会再写）
            }
        }
    }
}
