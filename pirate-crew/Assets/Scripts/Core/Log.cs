using System.Diagnostics;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 运行时日志统一出口（发布收口）。
    ///
    /// 【语义】<see cref="Info"/> / <see cref="Warn"/> 只在**编辑器**或 **Development Build** 输出；
    /// 正式构建（非 Development）里调用点被编译器整体删除，零开销、零播放器日志噪音。
    /// <see cref="Error"/> 直通 <see cref="Debug.LogError"/>——真错误必须留在播放器日志里
    /// （发布版排障的唯一依据，见交接指南 §4.3 的 Player.log 排查惯例）。
    ///
    /// 【为什么用 [Conditional] 而不是 if 开关】条件编译在调用点直接删语句，不需要运行时判断，
    /// 也不会留下"空 if"；两个标记任一成立即保留（并集语义）。
    ///
    /// 【调用约定】跨命名空间调用统一写全限定 <c>global::PirateCrew.Core.Log.…</c>
    /// （部分模块处于 <c>PirateCrew.*</c> 命名空间，非限定名 <c>PirateCrew.Core</c>
    /// 会先命中子命名空间 <c>PirateCrew.PirateCrew</c> 导致解析失败）。
    /// </summary>
    public static class Log
    {
        const string EditorTag = "UNITY_EDITOR";
        const string DevBuildTag = "DEVELOPMENT_BUILD";

        [Conditional(EditorTag)]
        [Conditional(DevBuildTag)]
        public static void Info(object message)
        {
            UnityEngine.Debug.Log(message);
        }

        [Conditional(EditorTag)]
        [Conditional(DevBuildTag)]
        public static void Warn(object message)
        {
            UnityEngine.Debug.LogWarning(message);
        }

        public static void Error(object message)
        {
            UnityEngine.Debug.LogError(message);
        }
    }
}
