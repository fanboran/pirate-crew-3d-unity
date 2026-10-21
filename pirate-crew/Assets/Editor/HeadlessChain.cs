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
    /// 【两种调用方共用同一套解析与执行】
    ///   · batchmode：run.sh chain → <see cref="Run"/>（自带 EditorApplication.Exit 退出码收口，
    ///     命令行**不得**再加 -quit，否则退出码丢失）；
    ///   · 常驻编辑器命令桥：CommandBridge 的 `chain` 命令 → <see cref="RunChainSpec"/>（不 Exit，
    ///     编辑器由创始人常开，桥绝不重启它）。
    ///
    /// 【链的两种写法】<see cref="RunChainSpec"/> 的 spec 既可以是注册表链名（如 "full"），
    /// 也可以是显式步骤列表（"类全名.方法名;类全名.方法名"）。装配方法会改场景/资产，
    /// 结果不可回放，因此 chain 档不做缓存。
    /// </summary>
    public static class HeadlessChain
    {
        /// <summary>
        /// 命名链注册表：名字 → 步骤序列（run.sh chain <名> 与命令桥 chain <名> 共用这一份）。
        /// 步骤全部要求 public static 无参（或全默认参）——与显式列表同规则。
        /// </summary>
        static readonly Dictionary<string, string[]> NamedChains =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // full：日常验收装配链——字体重建 → Battle 子链 → 菜单场景。
                //   ① 字体重建（删旧 TMP 资产再生成，图集落位）；
                //   ② Battle 八步子链（骨架→空岛摆件→场景件烘焙→资产清单→小地图→运行期接线→
                //      WorldKit 资产表→折叠 BattleRig.prefab），任一步失败能跑的跑完后汇总抛出；
                //   ③ M1 菜单场景（Bootstrapper/MainMenu 重建 + Build Settings 登记）。
                // 三步均为纯资产/场景操作（各入口自身注释均写明支持 -batchmode -nographics 无头跑），
                // 不含需要真 GfxDevice 的截图类步骤（如 BattleScenePipeline.CrewRebakeCapture）。
                ["full"] = new[]
                {
                    "PirateCrew.EditorTools.FontAssetBuilder.ForceRebuildAll",
                    "PirateCrew.EditorTools.BattleScenePipeline.Build",
                    "PirateCrew.EditorTools.SceneSetup.BuildAll",
                },

                // battle：只重跑 Battle 子链（改了 BattleSceneSetup / 摆件 / 接线后用，
                // 字体与 M1 场景不动——省掉两分钟的字体重建）。
                ["battle"] = new[]
                {
                    "PirateCrew.EditorTools.BattleScenePipeline.Build",
                },
            };

        /// <summary>已注册的链名清单（解析失败的报错提示与命令桥 list 用）。</summary>
        public static string[] ChainNames()
        {
            var names = new string[NamedChains.Count];
            NamedChains.Keys.CopyTo(names, 0);
            return names;
        }

        /// <summary>
        /// chain 档入口（batchmode 专用）：读 -chainSteps "链名" 或 "类全名.方法名;类全名.方法名"
        /// （分号分隔），交给 <see cref="RunChainSpec"/> 执行并以退出码收口。
        /// 退出码：0 全部成功；2 参数缺失 / 步骤名解析失败；1 某步执行抛异常。
        /// </summary>
        public static void Run()
        {
            string raw = GetArgValue("-chainSteps");
            if (string.IsNullOrWhiteSpace(raw))
            {
                Debug.Log("[HeadlessChain] 缺少 -chainSteps，用法: " +
                          "-executeMethod PirateCrew.EditorTools.HeadlessChain.Run " +
                          "-chainSteps \"full\" 或 \"PirateCrew.EditorTools.FontAssetBuilder.ForceRebuildAll;...\"（分号分隔；" +
                          "已注册链名: " + string.Join(", ", ChainNames()) + "）");
                EditorApplication.Exit(2);
                return;
            }
            EditorApplication.Exit(RunChainSpec(raw));
        }

        /// <summary>
        /// 执行一条链：spec 先查命名链注册表（如 "full"），未命中再按显式步骤列表
        /// （"类全名.方法名;…"）解析——先对全部步骤做解析校验（有坏名字整体拒绝，
        /// 不跑到一半才炸），再按序执行、逐步计时。
        /// 返回 0 全部成功；2 解析失败；1 某步执行抛异常。**不调 EditorApplication.Exit**——
        /// batchmode 入口与命令桥各自按此码收口。
        /// </summary>
        public static int RunChainSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
            {
                Debug.Log("[HeadlessChain] 链规格为空（应为注册链名或 \"类全名.方法名;…\"）。");
                return 2;
            }

            string trimmed = spec.Trim();
            string[] stepNames;
            if (NamedChains.TryGetValue(trimmed, out string[] named))
                stepNames = (string[])named.Clone();   // 克隆一份：后续逐步 Trim 是原地写，别污染注册表
            else
                stepNames = trimmed.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

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
                          string.Join("; ", badSteps) +
                          "；已注册链名: " + string.Join(", ", ChainNames()));
                return 2;
            }
            return ExecuteSteps(stepNames, methods);
        }

        /// <summary>冒烟测试靶子：只打一行，不碰任何场景/资产。验证 chain 档通路时用它。</summary>
        public static void Noop()
        {
            Debug.Log("== [chain] noop OK ==");
        }

        // 按序执行已解析的步骤；任一步失败即返回 1（不再往下跑，避免留下半截装配状态）。
        static int ExecuteSteps(string[] stepNames, MethodInfo[] methods)
        {
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
                    Debug.LogError($"== [chain] 步骤 {i + 1} 失败 ==\n{real}");
                    return 1;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"== [chain] 步骤 {i + 1} 失败 ==\n{ex}");
                    return 1;
                }
                sw.Stop();
                Debug.Log($"== [chain] 步骤 {i + 1}/{methods.Length}: {stepNames[i]} 完成，耗时 {sw.ElapsedMilliseconds} ms ==");
            }
            Debug.Log($"== [chain] 全部 {methods.Length} 步完成 ==");
            return 0;
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
