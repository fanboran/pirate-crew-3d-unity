using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 全局事件总线（强类型版；语义翻译自 Godot <c>core/autoload/event_bus.gd</c>）。
    ///
    /// 【架构定位】
    ///   模块化架构的解耦核心。模块间通信一律走 EventBus，发布者不需要知道谁在监听，
    ///   监听者也不需要知道谁在发布。对应 Godot 版的 EventBus autoload。
    ///
    /// 【使用方式（泛型，载荷类型编译期受检）】
    ///   发布: <c>EventBus.Publish(BattleEvents.CrewDamaged, new CrewDamagedPayload(...))</c>（T 由实参推断）
    ///   订阅: <c>EventBus.Subscribe&lt;CrewDamagedPayload&gt;(BattleEvents.CrewDamaged, OnCrewDamaged)</c>
    ///   退订: <c>EventBus.Unsubscribe&lt;CrewDamagedPayload&gt;(BattleEvents.CrewDamaged, OnCrewDamaged)</c>
    ///   无载荷: <c>EventBus.Publish(SceneEvents.GoBack)</c> / <c>Subscribe(SceneEvents.GoBack, OnGoBack)</c>
    ///   动态分派（逃生口，见下）: <c>SubscribeDynamic</c> / <c>UnsubscribeDynamic</c>
    ///
    /// 【为什么载荷类型要进类型系统】
    ///   旧版载荷是 <c>object</c>：订阅侧靠 <c>is</c> 模式匹配，**类型写错就是"事件发了没人收到"的静默故障**，
    ///   编译期与运行期都不报错。泛型化后，订阅/发布的载荷不匹配直接是编译错误（CS1503），
    ///   剩下的漏网之鱼（字符串键拼错、键与载荷张冠李戴）由 <see cref="EventCatalog"/> 的运行期断言兜底。
    ///
    /// 【内部结构】事件名 → 频道（<see cref="Channel"/>）；频道内每条订阅记录
    ///   「载荷类型 + 委托」。<c>Publish&lt;T&gt;</c> 只投递给载荷类型匹配的条目，
    ///   因此**同一个键可以同时承载多种载荷类型**（当前仅 <c>change_scene</c> 的 <see cref="string"/> 一种）。
    ///
    /// 【热路径零分配】频道内维护**缓存的快照数组**，只有订阅/退订改变列表时才置脏重建，
    ///   <c>Publish</c> 本身不再 <c>ToArray()</c>（"每帧发事件吃 GC"的旧病由此消除，
    ///   见 <c>EventBusTests.Publish_Repeatedly_AllocatesNothing</c> 的分配断言）。
    ///
    /// 【空载荷哨兵】无载荷事件用 <see cref="NoPayload"/> 哨兵类型表示——**不用 object 混过去**：
    ///   它让"无载荷订阅"与"任意类型载荷订阅"在频道里可区分，也让契约表能登记"本事件无载荷"。
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
        /// 【用途】内部用它表示"这个频道承载的是无载荷事件"；契约表（<see cref="EventCatalog"/>）
        /// 也用它登记无载荷事件。<b>不要</b>用它写 <c>Subscribe&lt;NoPayload&gt;</c>——
        /// 无载荷事件请用 <c>Subscribe(key, Action)</c>（无参重载）。
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

        /// <summary>契约违规记录的条数上限（防某个坏事件在循环里刷爆内存）。</summary>
        const int MaxViolationRecords = 64;

        /// <summary>一条订阅：载荷类型 + 与该类型匹配的委托（<c>Action&lt;T&gt;</c> / 无载荷的 <c>Action</c> / 动态的 <c>Action&lt;object&gt;</c>）。</summary>
        struct Subscription
        {
            /// <summary><see cref="NoPayloadType"/>（无载荷）｜<see cref="AnyPayloadType"/>（动态）｜具体载荷类型。</summary>
            public Type PayloadType;

            /// <summary>与 <see cref="PayloadType"/> 对应的委托实例。</summary>
            public Delegate Callback;
        }

        /// <summary>一个事件名的订阅集合 + 它自己的快照缓存。</summary>
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

        static readonly Dictionary<string, Channel> _channels = new Dictionary<string, Channel>(64);

        /// <summary>快照重建次数（诊断/测试用）——用来证明"订阅不变时 Publish 不重建快照"。</summary>
        static int _snapshotRebuilds;

        /// <summary>契约违规记录（事件名/期望类型/实际类型），供诊断与测试断言；只在真正违规时增长。</summary>
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

        /// <summary>
        /// 订阅某事件的某种载荷。同一回调重复订阅只生效一次（Godot 版 <c>arr.has</c> 去重语义）。
        /// </summary>
        /// <typeparam name="T">载荷类型；必须与该键在 <see cref="EventCatalog"/> 登记的类型一致。</typeparam>
        public static void Subscribe<T>(string eventName, Action<T> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return;

            if (ReferenceEquals(typeof(T), NoPayloadType))
            {
                // 无载荷事件请用 Subscribe(key, Action)：走泛型会把 Action<NoPayload> 存进通道，
                // 而投递侧按裸 Action 调用 —— 明确报错比运行期 InvalidCastException 好定位。
                ReportContractViolation(eventName, NoPayloadType, NoPayloadType,
                    "无载荷事件请用 Subscribe(key, Action) 重载，不要 Subscribe<NoPayload>");
                return;
            }

            VerifyContract(eventName, typeof(T), subscriber: true);
            Add(eventName, typeof(T), handler);
        }

        /// <summary>订阅无载荷事件（如 <c>go_back</c>）。</summary>
        public static void Subscribe(string eventName, Action handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return;

            VerifyContract(eventName, NoPayloadType, subscriber: true);
            Add(eventName, NoPayloadType, handler);
        }

        /// <summary>
        /// <b>低层动态订阅逃生口（非泛型）</b>——只给"按事件名表循环注册、无法在编译期固定载荷类型"
        /// 的动态分派用（现仅 <c>PirateCrew.Audio.AudioService</c>：它的订阅表是
        /// <c>string[] EventNames</c>，处理器按名字查表取得，见该文件 <c>SubscribeEvents</c>）。
        ///
        /// 【语义】handler 收到该事件的**任何**载荷（无载荷事件收到 <c>null</c>），
        ///   所以**不受契约表保护、也不会触发类型不匹配告警**——能不用就不用。
        ///   业务代码一律用泛型 <see cref="Subscribe{T}"/>，让载荷类型进编译期检查。
        /// </summary>
        public static void SubscribeDynamic(string eventName, Action<object> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return;

            Add(eventName, AnyPayloadType, handler);
        }

        static void Add(string eventName, Type payloadType, Delegate callback)
        {
            if (!_channels.TryGetValue(eventName, out Channel channel))
            {
                channel = new Channel();
                _channels[eventName] = channel;
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

        /// <summary>取消订阅（按委托相等性匹配，与 <see cref="Subscribe{T}"/> 成对使用）。</summary>
        public static void Unsubscribe<T>(string eventName, Action<T> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return;

            Remove(eventName, typeof(T), handler);
        }

        /// <summary>取消订阅无载荷事件。</summary>
        public static void Unsubscribe(string eventName, Action handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return;

            Remove(eventName, NoPayloadType, handler);
        }

        /// <summary>取消动态订阅（与 <see cref="SubscribeDynamic"/> 成对使用）。</summary>
        public static void UnsubscribeDynamic(string eventName, Action<object> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return;

            Remove(eventName, AnyPayloadType, handler);
        }

        static void Remove(string eventName, Type payloadType, Delegate callback)
        {
            if (!_channels.TryGetValue(eventName, out Channel channel))
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
                    _channels.Remove(eventName);   // 无监听者即移除键（与旧版一致）
                return;
            }
        }

        // ==================================================================
        // 发布
        // ==================================================================

        /// <summary>
        /// 发布带载荷事件。载荷类型由实参推断，只投递给载荷类型匹配的订阅者。
        /// </summary>
        public static void Publish<T>(string eventName, T payload)
        {
            if (string.IsNullOrEmpty(eventName))
                return;

            // 契约对拍放在"有没有订阅者"之前：**没有订阅者时用错载荷类型才是最典型的静默故障**
            // （事件发了、没人收到、也没有任何告警），所以无监听者也照样检查。
            VerifyContract(eventName, typeof(T), subscriber: false);

            if (!_channels.TryGetValue(eventName, out Channel channel))
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

        /// <summary>发布无载荷事件（如 <c>go_back</c>）。动态订阅者收到 <c>null</c>。</summary>
        public static void Publish(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
                return;

            VerifyContract(eventName, NoPayloadType, subscriber: false);

            if (!_channels.TryGetValue(eventName, out Channel channel))
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

        /// <summary>某个事件是否有监听者。</summary>
        public static bool HasListeners(string eventName)
        {
            return !string.IsNullOrEmpty(eventName)
                   && _channels.TryGetValue(eventName, out Channel channel)
                   && channel.Items.Count > 0;
        }

        /// <summary>某事件名当前的监听者数量（诊断/测试用）。</summary>
        public static int ListenerCount(string eventName)
        {
            return !string.IsNullOrEmpty(eventName) && _channels.TryGetValue(eventName, out Channel channel)
                ? channel.Items.Count
                : 0;
        }

        /// <summary>清除某个事件的所有监听者。</summary>
        public static void ClearEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
                return;

            _channels.Remove(eventName);
        }

        /// <summary>清除所有事件的所有监听者（慎用）。</summary>
        public static void ClearAll()
        {
            _channels.Clear();
        }

        /// <summary>
        /// 进入播放前清空全部静态残留（订阅表 + 契约违规记录）。由唯一入口
        /// <see cref="GameEntryPoint"/> 调用；测试也可用来隔离静态状态。
        /// </summary>
        public static void ResetForNewSession()
        {
            ClearAll();
            _violations.Clear();
        }

        // ==================================================================
        // 契约诊断（键与载荷类型的一致性）
        // ==================================================================

        /// <summary>已记录的契约违规（事件名/期望类型/实际类型）；诊断与测试用，最多 <see cref="MaxViolationRecords"/> 条。</summary>
        public static IReadOnlyList<string> ContractViolations => _violations;

        /// <summary>清空契约违规记录（测试隔离用）。</summary>
        public static void ClearContractViolations()
        {
            _violations.Clear();
        }

        /// <summary>
        /// 校验「这个键 + 这个载荷类型」是否与 <see cref="EventCatalog"/> 的登记一致。
        /// 未登记的键**不报**（大量内部/测试事件不登记）；登记了却用错类型才是故障。
        /// </summary>
        static void VerifyContract(string eventName, Type payloadType, bool subscriber)
        {
            if (!EventCatalog.TryGetExpectedPayload(eventName, out Type expected))
                return;

            if (ReferenceEquals(expected, payloadType))
                return;

            ReportContractViolation(eventName, expected, payloadType,
                subscriber ? "订阅方" : "发布方");
        }

        /// <summary>
        /// 记账 + 告警：编辑器与开发构建下经 <see cref="Log"/> 打出事件名 / 期望类型 / 实际类型
        /// （<see cref="Log.Warn"/> 带 [Conditional]，正式构建里整条日志调用被删掉，静默）。
        /// </summary>
        static void ReportContractViolation(string eventName, Type expected, Type actual, string side)
        {
            string message = "[EventBus] 事件载荷类型与契约不符（" + side + "）：事件 \""
                             + eventName + "\"，契约登记 " + Describe(expected)
                             + "，实际 " + Describe(actual)
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
    }
}
