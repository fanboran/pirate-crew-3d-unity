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
    /// （部分模块处于 <c>PirateCrew.PirateCrew.*</c> 命名空间，非限定名 <c>PirateCrew.Core</c>
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

        /// <summary>带上下文的重载（双击日志可定位到场景对象；清扫前的 Debug.LogWarning(msg, this) 落这里）。</summary>
        [Conditional(EditorTag)]
        [Conditional(DevBuildTag)]
        public static void Warn(object message, Object context)
        {
            UnityEngine.Debug.LogWarning(message, context);
        }

        /// <summary>带上下文的信息日志。</summary>
        [Conditional(EditorTag)]
        [Conditional(DevBuildTag)]
        public static void Info(object message, Object context)
        {
            UnityEngine.Debug.Log(message, context);
        }

        public static void Error(object message)
        {
            UnityEngine.Debug.LogError(message);
        }

        /// <summary>带上下文的错误日志（直通，发布版保留）。</summary>
        public static void Error(object message, Object context)
        {
            UnityEngine.Debug.LogError(message, context);
        }
    }
}
