using System;

namespace PirateCrew.Core
{
    /// <summary>
    /// 无载荷事件的类型化频道（<see cref="EventBus"/> 建议A内核：事件键从裸 <c>string</c> 升级为类型实例）。
    ///
    /// 【键即身份】每个频道实例自身就是事件键：订阅/发布用同一个 <c>static readonly</c> 字段即天然对齐，
    ///   键名拼错、键与载荷张冠李戴这类字符串时代的静默故障在编译期即不可能写出
    ///   （<c>Event&lt;int&gt;</c> 频道订阅 <c>Action&lt;string&gt;</c> 是编译错误 CS1503）。
    ///
    /// 【定义位置】事件频道一律定义在所属模块的 <c>XxxEvents</c> 类里，形如
    ///   <c>public static readonly Event GoBack = new();</c>（无载荷）或
    ///   <c>public static readonly Event&lt;CrewDamagedPayload&gt; CrewDamaged = new();</c>（带载荷）。
    ///   **禁止**在调用点内联 <c>new Event()</c>——两个各自 new 出来的实例是两个不同频道，
    ///   发布与订阅对不上就是"事件发了没人收到"，比字符串键拼错更隐蔽。
    ///
    /// 【与字符串键的关系】迁移期内 EventBus 同时支持字符串键（旧 API）与类型化频道（本类）；
    ///   两类键互不相通（字符串频道收不到类型化频道的投递，反之亦然）。
    /// </summary>
    public class Event
    {
        /// <summary>
        /// 本频道的载荷类型（诊断/守卫用）：无载荷频道即 <see cref="EventBus.NoPayload"/> 哨兵；
        /// <see cref="Event{T}"/> 覆写为实际载荷类型。EventBus 用它拦截"把载荷频道当无载荷频道用"的误用。
        /// </summary>
        internal virtual Type PayloadType => typeof(EventBus.NoPayload);
    }

    /// <summary>
    /// 带载荷事件的类型化频道。载荷类型由泛型参数携带：
    ///   · <c>Event&lt;int&gt;</c> 与 <c>Event&lt;string&gt;</c> 是**不同**频道（载荷不同）；
    ///   · 两个 <c>Event&lt;int&gt;</c> **实例**也是不同频道（身份即实例，见 <see cref="Event"/>）。
    /// 无载荷事件不要写成 <c>Event&lt;NoPayload&gt;</c>——直接用非泛型 <see cref="Event"/>；
    /// 误用会在订阅/发布时被 EventBus 记为契约违规并拒绝投递。
    /// </summary>
    /// <typeparam name="T">载荷类型；工程惯例为 readonly struct（值类型载荷零装箱投递）。</typeparam>
    public class Event<T> : Event
    {
        internal override Type PayloadType => typeof(T);
    }
}
