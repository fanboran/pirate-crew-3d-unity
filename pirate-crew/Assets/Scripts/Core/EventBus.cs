using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 全局事件总线（类型化频道版；语义翻译自 Godot <c>core/autoload/event_bus.gd</c>）。
    ///
    /// 【架构定位】
    ///   模块化架构的解耦核心。模块间通信一律走 EventBus，发布者不需要知道谁在监听，
    ///   监听者也不需要知道谁在发布。对应 Godot 版的 EventBus autoload。
    ///
    /// 【使用方式（事件键 = 类型化频道，出处：docs/技术/架构/调研-模块间通信.md §4 建议A）】
    ///   每个事件在所属模块的 <c>XxxEvents</c> 类里声明一个 <c>static readonly</c> 频道字段：
    ///     <c>public static readonly Event&lt;CrewDamagedPayload&gt; CrewDamaged = new();</c>（带载荷）
    ///     <c>public static readonly Event GoBack = new();</c>（无载荷）
    ///   发布: <c>EventBus.Publish(BattleEvents.CrewDamaged, new CrewDamagedPayload(...))</c>
    ///   订阅: <c>EventBus.Subscribe(BattleEvents.CrewDamaged, OnCrewDamaged)</c>（T 由频道推断）
    ///   退订: <c>EventBus.Unsubscribe(BattleEvents.CrewDamaged, OnCrewDamaged)</c>
    ///   无载荷: <c>EventBus.Publish(SceneEvents.GoBack)</c> / <c>Subscribe(SceneEvents.GoBack, OnGoBack)</c>
    ///   动态分派（逃生口，见下）: <c>SubscribeDynamic</c> / <c>UnsubscribeDynamic</c>
    ///
    /// 【为什么事件键要进类型系统】
    ///   字符串键时代：键拼错、键与载荷张冠李戴都是"事件发了没人收到"的静默故障，
    ///   只能靠登记表运行期对拍兜底。类型化频道把键变成类型实例——拼错/张冠李戴直接是
    ///   **编译错误**（CS1503），运行期零契约对拍开销。
    ///
    /// 【内部结构】频道（<see cref="Event"/> 实例，引用即身份）→ <see cref="Channel"/>；
    ///   频道内每条订阅记录「载荷类型 + 委托」。一个频道只承载一种载荷类型
    ///   （由 <c>Event&lt;T&gt;</c> 的泛型参数锁定），投递时按类型匹配。
    ///
    /// 【热路径零分配】频道内维护**缓存的快照数组**，只有订阅/退订改变列表时才置脏重建，
    ///   <c>Publish</c> 本身不再 <c>ToArray()</c>（"每帧发事件吃 GC"的旧病由此消除，
    ///   见 <c>EventBusTypedChannelTests.Publish_Repeatedly_AllocatesNothing</c> 的分配断言）。
    ///
    /// 【空载荷哨兵】无载荷事件用非泛型 <see cref="Event"/> 频道 + 无参 <c>Action</c> 订阅；
    ///   <see cref="NoPayload"/> 哨兵类型仅用于内部统一表示"无载荷"。
    ///
    /// 【遍历中订阅/退订】回调里改订阅表不会破坏本轮遍历（快照语义，Godot 版 duplicate() 的等价物）。
    ///
    /// 【静态残留】Unity 关闭 Domain Reload 后静态字段不会自动清空；清空动作由唯一入口
    ///   <see cref="GameEntryPoint"/> 调用 <see cref="ResetForNewSession"/> 完成（见该类注释）。
    ///
    /// 【线程】非线程安全：只允许主线程发布/订阅（Unity 的运行期语义）。
    /// </summary>
    public static class EventBus
    {
        /// <summary>
        /// 空载荷事件的哨兵载荷类型。
        ///
        /// 【用途】内部用它表示"这个频道承载的是无载荷事件"（无载荷频道 = 非泛型
        /// <see cref="Event"/>）。<b>不要</b>用它声明 <c>Event&lt;NoPayload&gt;</c> 频道或写
        /// <c>Subscribe&lt;NoPayload&gt;</c>——误用会被误用守卫拦截并留诊断。
        /// </summary>
        public readonly struct NoPayload
        {
        }

        /// <summary>
        /// 动态订阅的载荷哨兵：带它的条目接收该频道的**一切**载荷（见 <see cref="SubscribeDynamic"/>）。
        /// 私有类型——外部无法构造，只能经 <see cref="SubscribeDynamic"/> 落到这个哨兵上。
        /// </summary>
        sealed class AnyPayload
        {
            AnyPayload()
            {
            }
        }

        static readonly Type NoPayloadType = typeof(NoPayload);
        static readonly Type AnyPayloadType = typeof(AnyPayload);

        /// <summary>误用记录的条数上限（防某个坏调用点在循环里刷爆内存）。</summary>
        const int MaxViolationRecords = 64;

        /// <summary>一条订阅：载荷类型 + 与该类型匹配的委托（<c>Action&lt;T&gt;</c> / 无载荷的 <c>Action</c> / 动态的 <c>Action&lt;object&gt;</c>）。</summary>
        struct Subscription
        {
            /// <summary><see cref="NoPayloadType"/>（无载荷）｜<see cref="AnyPayloadType"/>（动态）｜具体载荷类型。</summary>
            public Type PayloadType;

            /// <summary>与 <see cref="PayloadType"/> 对应的委托实例。</summary>
            public Delegate Callback;
        }

        /// <summary>一个频道的订阅集合 + 它自己的快照缓存。</summary>
        sealed class Channel
        {
            public readonly List<Subscription> Items = new List<Subscription>(4);

            /// <summary>投递用的快照（<see cref="Dirty"/> 为真时在下次投递前重建）。</summary>
            public Subscription[] Snapshot = Array.Empty<Subscription>();

            /// <summary>订阅表自上次重建快照后是否变过。</summary>
            public bool Dirty = true;

            public void MarkDirty() => Dirty = true;

            public void RebuildSnapshot()
            {
                Snapshot = Items.ToArray();
                Dirty = false;
                _snapshotRebuilds++;
            }
        }

        /// <summary>
        /// 频道表：键 = <see cref="Event"/> 实例。<see cref="Event"/> 未覆写 Equals，
        /// 字典按引用相等取键——"每个 static readonly 字段一个频道"的语义由此成立。
        /// </summary>
        static readonly Dictionary<Event, Channel> _channels = new Dictionary<Event, Channel>(64);

        /// <summary>快照重建次数（诊断/测试用）——用来证明"订阅不变时 Publish 不重建快照"。</summary>
        static int _snapshotRebuilds;

        /// <summary>误用记录（频道/期望类型/实际类型），供诊断与测试断言；只在真正误用时增长。</summary>
        static readonly List<string> _violations = new List<string>(4);

        /// <summary>已建立的频道数（诊断/测试用）。</summary>
        public static int ChannelCount => _channels.Count;

        /// <summary>
        /// 快照重建次数（诊断/测试用）：订阅表**没变**时反复 Publish 不应让它增长
        /// （增长 = 每次投递都重新分配快照，即旧版 <c>ToArray()</c> 的退化）。
        /// </summary>
        public static int SnapshotRebuildCount => _snapshotRebuilds;

        // ==================================================================
        // 订阅
        // ==================================================================

        /// <summary>订阅带载荷频道。T 由频道与处理器共同锁定——键与载荷张冠李戴是编译错误（CS1503）。</summary>
        public static void Subscribe<T>(Event<T> channel, Action<T> handler)
        {
            if (channel == null || handler == null)
                return;

            if (ReferenceEquals(typeof(T), NoPayloadType))
            {
                // 无载荷频道请用非泛型 Event + Subscribe(channel, Action)：Event<NoPayload> 与
                // 无载荷频道是两种东西，明确报错优于静默建立一条永远收不到投递的条目。
                ReportMisuse(DescribeChannel(channel), NoPayloadType, NoPayloadType,
                    "无载荷事件请用 Event 频道 + Subscribe(channel, Action) 重载，不要 Event<NoPayload>");
                return;
            }

            Add(channel, typeof(T), handler);
        }

        /// <summary>订阅无载荷频道。同一回调重复订阅只生效一次（Godot 版 <c>arr.has</c> 去重语义）。</summary>
        public static void Subscribe(Event channel, Action handler)
        {
            if (channel == null || handler == null)
                return;

            if (!ReferenceEquals(channel.PayloadType, NoPayloadType))
            {
                // 变量声明成基类 Event 但运行期是 Event<T> 时，无参 lambda 会落到本重载——
                // 它收不到任何载荷投递，明确报错优于静默无效。
                ReportMisuse(DescribeChannel(channel), channel.PayloadType, NoPayloadType,
                    "订阅方（载荷频道误用无载荷重载——请用 Subscribe(channel, Action<T>)）");
                return;
            }

            Add(channel, NoPayloadType, handler);
        }

        /// <summary>
        /// <b>低层动态订阅逃生口（非泛型）</b>——只给"按频道表循环注册、无法在编译期固定载荷类型"
        /// 的动态分派用（现仅 <c>PirateCrew.Audio.AudioService</c>：它的订阅表是
        /// <c>Event[] Channels</c>，处理器按频道查表取得，见该文件 <c>SubscribeEvents</c>）。
        ///
        /// 【语义】handler 收到该频道的**任何**载荷（无载荷频道收到 <c>null</c>），
        ///   不受频道泛型参数的类型锁定保护——能不用就不用。
        ///   业务代码一律用 <see cref="Subscribe{T}(Event{T}, Action{T})"/>，让载荷类型进编译期检查。
        /// </summary>
        public static void SubscribeDynamic(Event channel, Action<object> handler)
        {
            if (channel == null || handler == null)
                return;

            Add(channel, AnyPayloadType, handler);
        }

        static void Add(Event key, Type payloadType, Delegate callback)
        {
            if (!_channels.TryGetValue(key, out Channel channel))
            {
                channel = new Channel();
                _channels[key] = channel;
            }

            List<Subscription> items = channel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Subscription existing = items[i];
                if (ReferenceEquals(existing.PayloadType, payloadType) && existing.Callback.Equals(callback))
                    return;     // 去重：同一回调重复订阅只生效一次
            }

            items.Add(new Subscription { PayloadType = payloadType, Callback = callback });
            channel.MarkDirty();
        }

        // ==================================================================
        // 退订
        // ==================================================================

        /// <summary>取消订阅带载荷频道（按委托相等性匹配，与 <see cref="Subscribe{T}(Event{T}, Action{T})"/> 成对使用）。</summary>
        public static void Unsubscribe<T>(Event<T> channel, Action<T> handler)
        {
            if (channel == null || handler == null)
                return;

            Remove(channel, typeof(T), handler);
        }

        /// <summary>取消订阅无载荷频道。</summary>
        public static void Unsubscribe(Event channel, Action handler)
        {
            if (channel == null || handler == null)
                return;

            Remove(channel, NoPayloadType, handler);
        }

        /// <summary>取消动态订阅（与 <see cref="SubscribeDynamic"/> 成对使用）。</summary>
        public static void UnsubscribeDynamic(Event channel, Action<object> handler)
        {
            if (channel == null || handler == null)
                return;

            Remove(channel, AnyPayloadType, handler);
        }

        static void Remove(Event key, Type payloadType, Delegate callback)
        {
            if (!_channels.TryGetValue(key, out Channel channel))
                return;

            List<Subscription> items = channel.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Subscription existing = items[i];
                if (!ReferenceEquals(existing.PayloadType, payloadType) || !existing.Callback.Equals(callback))
                    continue;

                items.RemoveAt(i);
                channel.MarkDirty();

                if (items.Count == 0)
                    _channels.Remove(key);   // 无监听者即移除键（与旧版一致）
                return;
            }
        }

        // ==================================================================
        // 发布
        // ==================================================================

        /// <summary>发布带载荷事件。载荷类型由频道锁定，写错即编译错误。</summary>
        public static void Publish<T>(Event<T> channel, T payload)
        {
            if (channel == null)
                return;

            if (ReferenceEquals(typeof(T), NoPayloadType))
            {
                ReportMisuse(DescribeChannel(channel), NoPayloadType, NoPayloadType,
                    "无载荷事件请用 Event 频道 + Publish(channel) 重载，不要 Event<NoPayload>");
                return;
            }

            Dispatch(channel, payload);
        }

        /// <summary>发布无载荷事件。动态订阅者收到 <c>null</c>。</summary>
        public static void Publish(Event channel)
        {
            if (channel == null)
                return;

            if (!ReferenceEquals(channel.PayloadType, NoPayloadType))
            {
                ReportMisuse(DescribeChannel(channel), channel.PayloadType, NoPayloadType,
                    "发布方（载荷频道误用无载荷重载——请用 Publish(channel, payload)）");
                return;
            }

            DispatchNoPayload(channel);
        }

        /// <summary>
        /// 投递共用体：按快照遍历，载荷类型匹配的条目强转调用，动态条目走 <c>Action&lt;object&gt;</c> 逃生口。
        /// 快照/零分配机制在 <see cref="Channel"/> 内，这里只消费——订阅表不变时重复 Publish 零分配。
        /// </summary>
        static void Dispatch<T>(Event key, T payload)
        {
            if (!_channels.TryGetValue(key, out Channel channel))
                return;

            if (channel.Dirty)
                channel.RebuildSnapshot();

            Subscription[] snapshot = channel.Snapshot;
            Type publishedType = typeof(T);
            for (int i = 0; i < snapshot.Length; i++)
            {
                Subscription entry = snapshot[i];
                if (ReferenceEquals(entry.PayloadType, publishedType))
                    ((Action<T>)entry.Callback).Invoke(payload);
                else if (ReferenceEquals(entry.PayloadType, AnyPayloadType))
                    ((Action<object>)entry.Callback).Invoke(payload);   // 逃生口：装箱
            }
        }

        /// <summary>无载荷投递共用体（动态订阅者收到 <c>null</c>）。</summary>
        static void DispatchNoPayload(Event key)
        {
            if (!_channels.TryGetValue(key, out Channel channel))
                return;

            if (channel.Dirty)
                channel.RebuildSnapshot();

            Subscription[] snapshot = channel.Snapshot;
            for (int i = 0; i < snapshot.Length; i++)
            {
                Subscription entry = snapshot[i];
                if (ReferenceEquals(entry.PayloadType, NoPayloadType))
                    ((Action)entry.Callback).Invoke();
                else if (ReferenceEquals(entry.PayloadType, AnyPayloadType))
                    ((Action<object>)entry.Callback).Invoke(null);
            }
        }

        // ==================================================================
        // 查询 / 清理
        // ==================================================================

        /// <summary>某个频道是否有监听者。</summary>
        public static bool HasListeners(Event channel)
        {
            return channel != null
                   && _channels.TryGetValue(channel, out Channel ch)
                   && ch.Items.Count > 0;
        }

        /// <summary>某频道当前的监听者数量（诊断/测试用）。</summary>
        public static int ListenerCount(Event channel)
        {
            return channel != null && _channels.TryGetValue(channel, out Channel ch)
                ? ch.Items.Count
                : 0;
        }

        /// <summary>清除某个频道的所有监听者。</summary>
        public static void ClearEvent(Event channel)
        {
            if (channel == null)
                return;

            _channels.Remove(channel);
        }

        /// <summary>清除所有频道的所有监听者（慎用）。</summary>
        public static void ClearAll()
        {
            _channels.Clear();
        }

        /// <summary>
        /// 进入播放前清空全部静态残留（订阅表 + 误用记录）。由唯一入口
        /// <see cref="GameEntryPoint"/> 调用；测试也可用来隔离静态状态。
        /// </summary>
        public static void ResetForNewSession()
        {
            ClearAll();
            _violations.Clear();
        }

        // ==================================================================
        // 误用诊断（编译期管不住的结构性误用：基类声明逃过重载选择的那些）
        // ==================================================================

        /// <summary>已记录的误用（频道/期望类型/实际类型）；诊断与测试用，最多 <see cref="MaxViolationRecords"/> 条。</summary>
        public static IReadOnlyList<string> ContractViolations => _violations;

        /// <summary>清空误用记录（测试隔离用）。</summary>
        public static void ClearContractViolations()
        {
            _violations.Clear();
        }

        /// <summary>
        /// 记账 + 告警：编辑器与开发构建下经 <see cref="Log"/> 打出频道 / 期望类型 / 实际类型
        /// （<see cref="Log.Warn"/> 带 [Conditional]，正式构建里整条日志调用被删掉，静默）。
        /// </summary>
        static void ReportMisuse(string channelDescription, Type expected, Type actual, string side)
        {
            string message = "[EventBus] 频道用法与声明不符（" + side + "）：" + channelDescription
                             + "，按 " + Describe(expected) + " 使用，实际 " + Describe(actual)
                             + "；该订阅者/发布将不会被投递（静默无效果的典型症状）。";

            if (_violations.Count < MaxViolationRecords)
                _violations.Add(message);

            Log.Warn(message);
        }

        static string Describe(Type type)
        {
            if (type == null)
                return "<null>";
            if (ReferenceEquals(type, NoPayloadType))
                return "无载荷（NoPayload）";
            return type.Name;
        }

        static string DescribeChannel(Event channel)
        {
            return channel == null ? "<null频道>" : "频道 " + channel.GetType().FullName;
        }
    }
}
