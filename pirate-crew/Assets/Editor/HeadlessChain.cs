using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 无头链式编排器：一次 Unity 启动按序串跑多件 Editor 装配方法
    /// （run.sh `chain` 档的 Unity 侧入口，设计见 docs/技术/无头验证与启动成本优化.md §5）。
    ///
    /// 【为什么存在】一次验收常要连跑多件装配（字体重建 → Battle 管线 → 菜单场景），
    /// 每件一次 -executeMethod 就多付一次 ≈2.5 分钟冷启动；本类一次启动按序串跑。
    ///
    /// 【铁律】本类自带 EditorApplication.Exit 退出码收口，命令行**不得**再加 -quit，
    /// 否则退出码丢失；装配方法会改场景/资产，结果不可回放，因此 chain 档不做缓存。
    /// </summary>
    public static class HeadlessChain
    {
        /// <summary>
        /// chain 档入口。读 -chainSteps "类全名.方法名;类全名.方法名"（分号分隔），
        /// 先对全部步骤做解析校验（有坏名字整体拒绝，不跑到一半才炸），再按序执行。
        /// 退出码：0 全部成功；2 参数缺失 / 步骤名解析失败；1 某步执行抛异常。
        /// </summary>
        public static void Run()
        {
            string raw = GetArgValue("-chainSteps");
            if (string.IsNullOrWhiteSpace(raw))
            {
                Debug.Log("[HeadlessChain] 缺少 -chainSteps，用法: " +
                          "-executeMethod PirateCrew.EditorTools.HeadlessChain.Run " +
                          "-chainSteps \"PirateCrew.EditorTools.FontAssetBuilder.BuildAll;...\"（分号分隔）");
                EditorApplication.Exit(2);
                return;
            }

            string[] stepNames = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var methods = new MethodInfo[stepNames.Length];
            var badSteps = new List<string>();
            for (int i = 0; i < stepNames.Length; i++)
            {
                stepNames[i] = stepNames[i].Trim();
                methods[i] = ResolveStep(stepNames[i]);
                if (methods[i] == null)
                    badSteps.Add(stepNames[i]);
            }
            if (badSteps.Count > 0)
            {
                Debug.Log("[HeadlessChain] 以下步骤解析失败（要求 public static、无参或全默认参）: " +
                          string.Join("; ", badSteps));
                EditorApplication.Exit(2);
                return;
            }

            for (int i = 0; i < methods.Length; i++)
            {
                Debug.Log($"== [chain] 步骤 {i + 1}/{methods.Length}: {stepNames[i]} ==");
                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    methods[i].Invoke(null, null);
                }
                catch (TargetInvocationException tie)
                {
                    // 反射会把装配方法自己的异常包一层，摘出真凶再打
                    Exception real = tie.InnerException ?? tie;
                    Debug.Log($"== [chain] 步骤 {i + 1} 失败 ==\n{real}");
                    EditorApplication.Exit(1);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.Log($"== [chain] 步骤 {i + 1} 失败 ==\n{ex}");
                    EditorApplication.Exit(1);
                    return;
                }
                sw.Stop();
                Debug.Log($"== [chain] 步骤 {i + 1}/{methods.Length}: {stepNames[i]} 完成，耗时 {sw.ElapsedMilliseconds} ms ==");
            }
            Debug.Log($"== [chain] 全部 {methods.Length} 步完成 ==");
            EditorApplication.Exit(0);
        }

        /// <summary>冒烟测试靶子：只打一行，不碰任何场景/资产。验证 chain 档通路时用它。</summary>
        public static void Noop()
        {
            Debug.Log("== [chain] noop OK ==");
        }

        // 步骤名 → public static 无参（或全默认参）方法；解析不了返回 null，按坏步骤处理
        static MethodInfo ResolveStep(string stepName)
        {
            // 最后一个 '.' 前是类型全名（类型全名自身就含点，如 PirateCrew.EditorTools.Xxx）
            int lastDot = stepName.LastIndexOf('.');
            if (lastDot <= 0)
                return null;
            Type type = FindType(stepName.Substring(0, lastDot));
            if (type == null)
                return null;
            try
            {
                MethodInfo method = type.GetMethod(stepName.Substring(lastDot + 1),
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return null;
                foreach (ParameterInfo p in method.GetParameters())
                    if (!p.HasDefaultValue)
                        return null;   // 统一零参调用，带必填参数的方法不收
                return method;
            }
            catch (AmbiguousMatchException)
            {
                return null;   // 重载无法唯一解析，按坏步骤处理
            }
        }

        // 别用 Type.GetType 单发——它只查当前执行程序集，跨程序集（Assembly-CSharp-Editor 等）会 null
        static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = asm.GetType(fullName, false);
                if (found != null)
                    return found;
            }
            return null;
        }

        static string GetArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }
    }
}
