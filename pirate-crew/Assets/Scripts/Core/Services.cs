using System;
using System.Collections.Generic;

namespace PirateCrew.Core
{
    /// <summary>
    /// 全局服务注册表——**组合根专用的显式服务定位器**，不是全局单例垃圾桶。
    ///
    /// 【它是什么】一张"服务类型 → 实例"的表。<see cref="Bootstrapper"/>（唯一组合根）
    /// 在进入播放时把自己创建的服务逐个 <see cref="Register{T}"/> 进来；需要服务的地方用
    /// <see cref="TryGet{T}"/> 取——"谁依赖谁"因此是**代码里的一行注册**，而不是
    /// "某个 Awake 恰好在某个时机跑过"。
    ///
    /// 【它不是什么（纪律）】
    ///   · 不是查找工具：业务代码不许用 <c>Services.Get&lt;XxxController&gt;()</c> 去捞场景对象、
    ///     组件或模块内部状态；跨模块取数据走各模块 <c>XxxApi</c> 静态外观，跨模块通知走 <see cref="EventBus"/>。
    ///   · 不是自动单例容器：本类**不创建**任何实例，只登记已创建的服务（没有反射注入、没有生命周期推断）。
    ///   · 不是服务定位器的万能钥匙：能在组合根里传引用就不要查表（同模块内部直接方法调用即可）。
    ///   只有"跨模块、进程级、有明确生命周期（由组合根拥有）"的服务才登记在这里。
    ///
    /// 【生命周期】与播放期一致：组合根登记 → 全程可查 → 由 <see cref="GameEntryPoint"/>
    /// 在进入播放前 <see cref="Clear"/>。关闭 Domain Reload 时静态表跨播放存活，所以清空是必须的。
    ///
    /// 【与 <see cref="EventBus"/> 的分工】注册表管"谁负责这件事"（服务定位），事件总线管"发生了什么"（通知）。
    /// 需要"要不到服务也能跑"的地方用 <see cref="TryGet{T}"/>，需要"必须要有，没有就是配置错误"的地方用 <see cref="Get{T}"/>。
    /// </summary>
    public static class Services
    {
        static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>(8);

        /// <summary>已登记的服务数量。</summary>
        public static int Count => _services.Count;

        /// <summary>已登记的服务类型（诊断用）。</summary>
        public static IReadOnlyCollection<Type> RegisteredTypes => _services.Keys;

        /// <summary>
        /// 登记服务（键 = <typeparamref name="T"/>）。重复登记会覆盖：组合根是幂等的，
        /// 同一进程里第二次装配（例如从任意场景直接按 Play 后又加载了 Bootstrapper 场景）
        /// 不该抛异常，但覆盖不同实例会打一条警告，便于发现"两个宿主"。
        /// </summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service), "登记 null 服务没有意义（要清空请用 Clear）");

            Type key = typeof(T);
            if (_services.TryGetValue(key, out object existing) && !ReferenceEquals(existing, service))
            {
                Log.Warn("[Services] " + key.Name + " 被重复登记（旧实例将不再可查）——"
                          + "若不该有两个服务宿主，请检查组合根与场景里是否都摆了实例。");
            }

            _services[key] = service;
        }

        /// <summary>
        /// 取服务；未登记时记错误日志并返回 null（"必须存在"的场合：失败可见，但不抛异常中断玩法）。
        /// </summary>
        public static T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out object service))
                return service as T;

            Log.Error("[Services] 未登记的服务：" + typeof(T).Name
                      + "（组合根漏注册，或调用点早于 GameEntryPoint 的装配时机）");
            return null;
        }

        /// <summary>尝试取服务；未登记返回 false 且 <paramref name="service"/> 为 null。</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object found))
            {
                service = found as T;
                return service != null;
            }

            service = null;
            return false;
        }

        /// <summary>注销某个服务类型。</summary>
        public static void Unregister<T>() where T : class
        {
            _services.Remove(typeof(T));
        }

        /// <summary>清空注册表（进入播放前由组合根调用；测试也用它隔离静态状态）。</summary>
        public static void Clear()
        {
            _services.Clear();
        }
    }
}
