using System;
using System.Collections.Generic;
using System.Reflection;

namespace PirateCrew.Core
{
    /// <summary>
    /// 模块接线的显式声明：一个 <c>static void</c> 无参方法标注它，即被唯一入口
    /// <see cref="GameEntryPoint"/> 在进入播放时调用一次。
    ///
    /// 【为什么需要它（架构约束）】<c>PirateCrew.Core</c> 是零游戏依赖的底座程序集，
    /// **不能**引用 <c>PirateCrew.Gameplay</c> / <c>Campaign</c> / <c>CrewManagement</c> / <c>UI</c>
    /// （依赖方向铁律，见工业级重构总纲 §3.1）。所以"组合根调用模块的 Initialize()"
    /// 不能写成直接的静态调用——本类提供的是**受控的发现机制**：模块自己声明入口，
    /// 唯一入口按 <see cref="GameBootstrapPhase"/> 与 <see cref="Order"/> 的显式顺序调用。
    ///
    /// 【顺序仍然是显式代码】阶段枚举定大顺序，<see cref="Order"/> 定同阶段内的小顺序，
    /// 都在被调用方的属性上写得明明白白；<see cref="DescribeAll"/> 能列出全套入口供文档与测试核对。
    ///
    /// 【纪律】只允许在**组装/接线**层使用：声明"服务创建、事件订阅、命令行工具装配"这类
    /// 进程级一次性动作。业务逻辑挂在 MonoBehaviour 生命周期或事件回调里，不要塞进 bootstrap。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class GameBootstrapAttribute : Attribute
    {
        /// <param name="phase">所属阶段（见 <see cref="GameBootstrapPhase"/>）。</param>
        /// <param name="order">同阶段内的调用顺序，小的先跑；同值按类型全名排序（保证确定性）。</param>
        public GameBootstrapAttribute(GameBootstrapPhase phase = GameBootstrapPhase.Initialize, int order = 0)
        {
            Phase = phase;
            Order = order;
        }

        /// <summary>所属阶段。</summary>
        public GameBootstrapPhase Phase { get; }

        /// <summary>同阶段内的调用顺序。</summary>
        public int Order { get; }
    }

    /// <summary>
    /// 接线阶段。三个阶段由 <see cref="GameEntryPoint"/> 在**同一次** <c>BeforeSceneLoad</c> 回调里
    /// 按枚举顺序依次跑完（阶段名说的是"这类工作"，不是"三个引擎时机"）。
    /// </summary>
    public enum GameBootstrapPhase
    {
        /// <summary>清空静态残留（关闭 Domain Reload 时静态字段跨播放存活）。最先跑。</summary>
        ResetStatics = 0,

        /// <summary>登记事件契约（<see cref="EventCatalog.Add{T}"/>）。必须早于任何 Publish/Subscribe。</summary>
        Contracts = 1,

        /// <summary>创建服务、订阅事件、装配命令行工具模式。最后跑。</summary>
        Initialize = 2,
    }

    /// <summary>
    /// 接线入口的发现与调用（<see cref="GameBootstrapAttribute"/> 的执行侧）。
    ///
    /// 【扫描范围】只扫"项目自己的程序集"：含 <see cref="GameBootstrap"/> 的那个程序集
    /// （Core，同时也覆盖无头验证台把全工程源码编进同一个程序集的情形）、
    /// 名字以 <c>PirateCrew</c> 开头的程序集、以及 <c>Assembly-CSharp*</c>。
    /// 不扫 UnityEngine / 包 / 测试框架，避免启动期把时间花在无关程序集上。
    /// </summary>
    public static class GameBootstrap
    {
        /// <summary>发现到的入口（诊断与文档核对用）。</summary>
        public readonly struct Entry
        {
            public Entry(GameBootstrapPhase phase, int order, string description)
            {
                Phase = phase;
                Order = order;
                Description = description;
            }

            public GameBootstrapPhase Phase { get; }

            public int Order { get; }

            /// <summary>"类型全名.方法名"。</summary>
            public string Description { get; }
        }

        static readonly List<Entry> _discovered = new List<Entry>(16);
        static readonly List<MethodInfo> _methods = new List<MethodInfo>(16);
        static bool _scanned;

        /// <summary>已发现的接线入口（按调用顺序）。</summary>
        public static IReadOnlyList<Entry> DescribeAll()
        {
            Scan();
            return _discovered;
        }

        /// <summary>
        /// 按阶段顺序跑完所有阶段。返回成功调用的入口数。
        /// 单个入口抛异常只记错误日志、不中断后续入口（一个模块接不起来不该让整个游戏起不来）。
        /// </summary>
        public static int RunAll()
        {
            Scan();

            var phases = (GameBootstrapPhase[])Enum.GetValues(typeof(GameBootstrapPhase));
            Array.Sort(phases);
            int invoked = 0;
            for (int i = 0; i < phases.Length; i++)
                invoked += RunPhase(phases[i]);

            return invoked;
        }

        /// <summary>只跑某个阶段（测试与诊断用；正常启动走 <see cref="RunAll"/>）。</summary>
        public static int RunPhase(GameBootstrapPhase phase)
        {
            Scan();

            int invoked = 0;
            var args = Array.Empty<object>();
            for (int i = 0; i < _methods.Count; i++)
            {
                if (_discovered[i].Phase != phase)
                    continue;

                try
                {
                    _methods[i].Invoke(null, args);
                    invoked++;
                }
                catch (TargetInvocationException e)
                {
                    Log.Error("[GameBootstrap] 接线入口 " + _discovered[i].Description
                              + "（阶段 " + phase + "）抛异常：" + (e.InnerException ?? e));
                }
                catch (Exception e)
                {
                    Log.Error("[GameBootstrap] 接线入口 " + _discovered[i].Description
                              + "（阶段 " + phase + "）调用失败：" + e);
                }
            }

            return invoked;
        }

        /// <summary>清空发现缓存（测试隔离用；正常启动不需要）。</summary>
        public static void ResetDiscovery()
        {
            _discovered.Clear();
            _methods.Clear();
            _scanned = false;
        }

        static void Scan()
        {
            if (_scanned)
                return;

            _scanned = true;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Assembly assembly = assemblies[a];
                if (!IsProjectAssembly(assembly))
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    // 有类型加载不出来时仍然处理能加载的那些（Unity 编辑器下偶发）。
                    types = e.Types;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null)
                        continue;

                    CollectFromType(type);
                }
            }

            // 确定性顺序：阶段 → Order → 描述（同 Order 时不受程序集/反射顺序影响）。
            var paired = new List<KeyValuePair<Entry, MethodInfo>>(_discovered.Count);
            for (int i = 0; i < _discovered.Count; i++)
                paired.Add(new KeyValuePair<Entry, MethodInfo>(_discovered[i], _methods[i]));

            paired.Sort((x, y) =>
            {
                int byPhase = x.Key.Phase.CompareTo(y.Key.Phase);
                if (byPhase != 0)
                    return byPhase;
                int byOrder = x.Key.Order.CompareTo(y.Key.Order);
                return byOrder != 0
                    ? byOrder
                    : string.CompareOrdinal(x.Key.Description, y.Key.Description);
            });

            _discovered.Clear();
            _methods.Clear();
            for (int i = 0; i < paired.Count; i++)
            {
                _discovered.Add(paired[i].Key);
                _methods.Add(paired[i].Value);
            }
        }

        static void CollectFromType(Type type)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.DeclaredOnly);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.IsGenericMethod || method.GetParameters().Length != 0 || method.ReturnType != typeof(void))
                    continue;

                GameBootstrapAttribute attribute;
                try
                {
                    attribute = method.GetCustomAttribute<GameBootstrapAttribute>();
                }
                catch (Exception)
                {
                    continue;
                }

                if (attribute == null)
                    continue;

                _discovered.Add(new Entry(attribute.Phase, attribute.Order,
                    type.FullName + "." + method.Name));
                _methods.Add(method);
            }
        }

        static bool IsProjectAssembly(Assembly assembly)
        {
            if (assembly == null)
                return false;

            if (assembly == typeof(GameBootstrap).Assembly)
                return true;

            string name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name))
                return false;

            return name.StartsWith("PirateCrew", StringComparison.Ordinal)
                   || name.StartsWith("Assembly-CSharp", StringComparison.Ordinal);
        }
    }
}
