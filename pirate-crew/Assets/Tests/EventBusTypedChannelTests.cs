using System;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Core;

namespace PirateCrew.Tests
{
    /// <summary>
    /// EventBus 类型化频道（<see cref="Event"/> / <see cref="Event{T}"/>）单元测试。
    /// 与 <see cref="EventBusTests"/>（字符串键套件）锁定同一组不变量，外加类型化键特有的三条：
    ///
    /// 【类型化键特有】
    ///   1. 键即身份：同一载荷类型的两个频道实例互不相通（"static readonly 字段一个频道"语义）；
    ///   2. 迁移共存：字符串键频道与类型化频道互不相通；
    ///   3. 误用守卫：载荷频道被当成无载荷频道用（基类声明 + 无参 Action / 无载荷 Publish）时
    ///      记契约违规并拒绝建立条目，不留"订阅了但永远收不到"的静默条目。
    ///
    /// 【继承自字符串套件的不变量】快照遍历安全、去重、退订精确匹配、投递零分配、静态复位。
    /// EventBus 是静态类，测试间必须 ClearAll 隔离静态状态（频道实例本身无状态，可跨用例复用）。
    /// </summary>
    public class EventBusTypedChannelTests
    {
        // 频道定义模拟 XxxEvents 的标准形态：static readonly 字段，一个事件一个实例。
        static readonly Event<int> IntChannel = new();
        static readonly Event<int> OtherIntChannel = new();     // 与 IntChannel 同载荷类型、不同实例
        static readonly Event<string> StringChannel = new();
        static readonly Event<GoldPayload> GoldChannel = new();
        static readonly Event NoPayloadChannel = new();
        const string StringKeyChannel = "test_string_key_channel";

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            EventBus.ClearContractViolations();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
            EventBus.ClearContractViolations();
        }

        // ------------------------------------------------------------------
        // 基本投递
        // ------------------------------------------------------------------

        [Test]
        public void Publish_AfterSubscribe_DeliversPayload()
        {
            int received = 0;
            EventBus.Subscribe(IntChannel, (Action<int>)(payload => received = payload));

            EventBus.Publish(IntChannel, 42);

            Assert.That(received, Is.EqualTo(42));
        }

        [Test]
        public void Publish_StructPayload_ArrivesUnboxed()
        {
            var received = default(GoldPayload);
            EventBus.Subscribe(GoldChannel, (Action<GoldPayload>)(payload => received = payload));

            EventBus.Publish(GoldChannel, new GoldPayload(7));

            Assert.That(received.Amount, Is.EqualTo(7));
        }

        [Test]
        public void Publish_NoPayloadChannel_CallsHandler()
        {
            bool called = false;
            EventBus.Subscribe(NoPayloadChannel, () => called = true);

            EventBus.Publish(NoPayloadChannel);

            Assert.That(called, Is.True);
        }

        [Test]
        public void Publish_MethodGroup_TInferredFromChannel()
        {
            // 字符串键时代方法组无法推断 T（契约文档 §0），类型化频道把频道与处理器锁定到同一个 T
            // 后方法组可以直接推断——这是调用点清扫时"删掉 <T> 即完成迁移"的语义基础。
            EventBus.Subscribe(IntChannel, OnPayload);

            EventBus.Publish(IntChannel, 5);

            Assert.That(_methodGroupCalls, Is.EqualTo(1));
        }

        int _methodGroupCalls;

        void OnPayload(int payload)
        {
            _methodGroupCalls++;
        }

        [Test]
        public void Publish_MultipleSubscribers_AllReceiveInSubscriptionOrder()
        {
            var calls = new List<string>();
            EventBus.Subscribe(NoPayloadChannel, () => calls.Add("a"));
            EventBus.Subscribe(NoPayloadChannel, () => calls.Add("b"));
            EventBus.Subscribe(NoPayloadChannel, () => calls.Add("c"));

            EventBus.Publish(NoPayloadChannel);

            Assert.That(calls, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Publish_WithoutListeners_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBus.Publish(new Event<int>(), 1));
            Assert.DoesNotThrow(() => EventBus.Publish(new Event()));
        }

        // ------------------------------------------------------------------
        // 键即身份（类型化键特有的隔离语义）
        // ------------------------------------------------------------------

        [Test]
        public void Publish_SamePayloadType_DifferentChannelInstances_AreIsolated()
        {
            int first = 0;
            int second = 0;
            EventBus.Subscribe(IntChannel, (Action<int>)(payload => first = payload));
            EventBus.Subscribe(OtherIntChannel, (Action<int>)(payload => second = payload));

            EventBus.Publish(IntChannel, 1);

            Assert.That(first, Is.EqualTo(1), "IntChannel 的订阅者必须收到投递");
            Assert.That(second, Is.EqualTo(0), "同载荷类型的另一个频道实例不该收到投递（身份即实例）");
        }

        [Test]
        public void TypedChannel_And_StringKeyChannel_AreIsolated()
        {
            // 迁移期内两套键并存：绝不允许字符串频道收到类型化频道的投递，反之亦然。
            int typed = 0;
            int byString = 0;
            EventBus.Subscribe(IntChannel, (Action<int>)(payload => typed = payload));
            EventBus.Subscribe(StringKeyChannel, (Action<int>)(payload => byString = payload));

            EventBus.Publish(IntChannel, 1);
            Assert.That(typed, Is.EqualTo(1));
            Assert.That(byString, Is.EqualTo(0), "字符串键频道不该收到类型化频道的投递");

            EventBus.Publish(StringKeyChannel, 2);
            Assert.That(typed, Is.EqualTo(1), "类型化频道不该收到字符串键频道的投递");
            Assert.That(byString, Is.EqualTo(2));
        }

        // ------------------------------------------------------------------
        // 误用守卫（载荷频道被当无载荷频道用）
        // ------------------------------------------------------------------

        [Test]
        public void Subscribe_PayloadChannelViaBaseDeclaration_WithParameterlessAction_IsRejected()
        {
            // 变量声明成基类 Event 但运行期是 Event<T>：无参 Action 收不到任何载荷，必须留证拒绝。
            Event misdeclared = new Event<int>();
            EventBus.Subscribe(misdeclared, () => { });

            Assert.That(EventBus.HasListeners(misdeclared), Is.False, "误用订阅不该建立条目");
            Assert.That(EventBus.ContractViolations, Is.Not.Empty, "误用必须留下可断言的诊断");
        }

        [Test]
        public void Publish_PayloadChannelViaBaseDeclaration_WithoutPayload_IsRejected()
        {
            Event misdeclared = new Event<int>();
            EventBus.Publish(misdeclared);

            Assert.That(EventBus.ContractViolations, Is.Not.Empty);
        }

        [Test]
        public void SubscribeGenericWithNoPayloadSentinelChannel_IsRejectedWithDiagnostic()
        {
            // Event<NoPayload> 与无载荷频道是两种东西，误用时明确拒绝。
            var sentinelChannel = new Event<EventBus.NoPayload>();
            EventBus.Subscribe(sentinelChannel, _ => { });

            Assert.That(EventBus.HasListeners(sentinelChannel), Is.False);
            Assert.That(EventBus.ContractViolations, Is.Not.Empty);
        }

        // ------------------------------------------------------------------
        // 退订
        // ------------------------------------------------------------------

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            int count = 0;
            Action<int> handler = _ => count++;
            EventBus.Subscribe(IntChannel, handler);

            EventBus.Publish(IntChannel, 1);
            EventBus.Unsubscribe(IntChannel, handler);
            EventBus.Publish(IntChannel, 1);

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_TypedHandler_DoesNotRemoveDynamicSubscriber()
        {
            int typed = 0;
            int dynamic = 0;
            Action<int> typedHandler = _ => typed++;
            Action<object> dynamicHandler = _ => dynamic++;
            EventBus.Subscribe(IntChannel, typedHandler);
            EventBus.SubscribeDynamic(IntChannel, dynamicHandler);

            EventBus.Unsubscribe(IntChannel, typedHandler);
            EventBus.Publish(IntChannel, 1);

            Assert.That(typed, Is.EqualTo(0));
            Assert.That(dynamic, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_UnknownChannelOrCallback_DoesNotThrow()
        {
            Action<int> handler = _ => { };

            Assert.DoesNotThrow(() => EventBus.Unsubscribe(new Event<int>(), handler));
            Assert.DoesNotThrow(() => EventBus.Unsubscribe(IntChannel, handler));
            Assert.DoesNotThrow(() => EventBus.Unsubscribe(new Event(), () => { }));
            Assert.DoesNotThrow(() => EventBus.UnsubscribeDynamic(IntChannel, _ => { }));
        }

        // ------------------------------------------------------------------
        // 去重
        // ------------------------------------------------------------------

        [Test]
        public void Subscribe_SameCallbackTwice_IsInvokedOnce()
        {
            int count = 0;
            Action<int> handler = _ => count++;
            EventBus.Subscribe(IntChannel, handler);
            EventBus.Subscribe(IntChannel, handler);

            EventBus.Publish(IntChannel, 1);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(EventBus.ListenerCount(IntChannel), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // 遍历中订阅 / 退订（快照语义）
        // ------------------------------------------------------------------

        [Test]
        public void Publish_WhenCallbackUnsubscribesItself_SnapshotIsSafe()
        {
            var calls = new List<string>();
            Action<int> first = null;
            first = _ =>
            {
                calls.Add("first");
                EventBus.Unsubscribe(IntChannel, first);
            };
            Action<int> second = _ => calls.Add("second");

            EventBus.Subscribe(IntChannel, first);
            EventBus.Subscribe(IntChannel, second);

            EventBus.Publish(IntChannel, 1);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second" }), "本轮投递必须用快照，退订不影响本轮");

            EventBus.Publish(IntChannel, 1);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second", "second" }), "下一轮 first 已不在表中");
        }

        [Test]
        public void Publish_WhenCallbackSubscribesAnother_LateSubscriberWaitsForNextRound()
        {
            var calls = new List<string>();
            Action late = () => calls.Add("late");
            EventBus.Subscribe(NoPayloadChannel, () =>
            {
                calls.Add("early");
                EventBus.Subscribe(NoPayloadChannel, late);
            });

            EventBus.Publish(NoPayloadChannel);
            Assert.That(calls, Is.EqualTo(new[] { "early" }), "本轮快照里没有 late，本轮不该被调用");

            EventBus.Publish(NoPayloadChannel);
            Assert.That(calls, Is.EqualTo(new[] { "early", "early", "late" }));
        }

        // ------------------------------------------------------------------
        // 动态逃生口（迁移期内供订阅表驱动型系统过桥）
        // ------------------------------------------------------------------

        [Test]
        public void SubscribeDynamic_TypedChannel_ReceivesPayload()
        {
            object received = null;
            EventBus.SubscribeDynamic(IntChannel, payload => received = payload);

            EventBus.Publish(IntChannel, 9);

            Assert.That(received, Is.EqualTo(9), "动态订阅者收到装箱后的载荷");
        }

        [Test]
        public void SubscribeDynamic_NoPayloadChannel_ReceivesNull()
        {
            object received = "sentinel";
            EventBus.SubscribeDynamic(NoPayloadChannel, payload => received = payload);

            EventBus.Publish(NoPayloadChannel);

            Assert.That(received, Is.Null);
        }

        // ------------------------------------------------------------------
        // 查询 / 清理
        // ------------------------------------------------------------------

        [Test]
        public void HasListeners_TypedChannel_Semantics()
        {
            Assert.That(EventBus.HasListeners(IntChannel), Is.False, "无监听者时应为 false");
            Assert.That(EventBus.HasListeners((Event)null), Is.False);

            Action<int> handler = _ => { };
            EventBus.Subscribe(IntChannel, handler);
            Assert.That(EventBus.HasListeners(IntChannel), Is.True, "订阅后应为 true");

            EventBus.Unsubscribe(IntChannel, handler);
            Assert.That(EventBus.HasListeners(IntChannel), Is.False, "退订最后一个监听者后应为 false");
        }

        [Test]
        public void ClearEvent_TypedChannel_RemovesOnlyThatChannel()
        {
            int targetCount = 0;
            int otherCount = 0;
            EventBus.Subscribe(IntChannel, (Action<int>)(payload => targetCount = payload));
            EventBus.Subscribe(OtherIntChannel, (Action<int>)(payload => otherCount = payload));

            EventBus.ClearEvent(IntChannel);
            EventBus.Publish(IntChannel, 1);
            EventBus.Publish(OtherIntChannel, 2);

            Assert.That(targetCount, Is.EqualTo(0));
            Assert.That(otherCount, Is.EqualTo(2));
        }

        [Test]
        public void ResetForNewSession_ClearsTypedAndStringChannels()
        {
            EventBus.Subscribe(IntChannel, (Action<int>)(_ => { }));
            EventBus.Subscribe(NoPayloadChannel, () => { });
            EventBus.Subscribe(StringKeyChannel, () => { });

            EventBus.ResetForNewSession();

            Assert.That(EventBus.HasListeners(IntChannel), Is.False, "静态复位必须清掉类型化频道的订阅");
            Assert.That(EventBus.HasListeners(NoPayloadChannel), Is.False);
            Assert.That(EventBus.HasListeners(StringKeyChannel), Is.False, "静态复位必须一并清掉字符串键频道");
            Assert.That(EventBus.ChannelCount, Is.EqualTo(0));
        }

        [Test]
        public void Subscribe_NullChannelOrNullCallback_IsIgnored()
        {
            Assert.DoesNotThrow(() => EventBus.Subscribe((Event<int>)null, _ => { }));
            Assert.DoesNotThrow(() => EventBus.Subscribe(IntChannel, (Action<int>)null));
            Assert.DoesNotThrow(() => EventBus.Subscribe((Event)null, () => { }));
            Assert.DoesNotThrow(() => EventBus.SubscribeDynamic((Event)null, _ => { }));
            Assert.DoesNotThrow(() => EventBus.Publish((Event<int>)null, 1));
            Assert.DoesNotThrow(() => EventBus.Publish((Event)null));

            Assert.That(EventBus.ChannelCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // 零分配（快照机制原样保留的证明）
        // ------------------------------------------------------------------

        [Test]
        public void Publish_Repeatedly_DoesNotRebuildSnapshot()
        {
            EventBus.Subscribe(IntChannel, (Action<int>)(_ => { }));
            EventBus.SubscribeDynamic(IntChannel, _ => { });
            EventBus.Publish(IntChannel, 0);     // 建一次快照

            int before = EventBus.SnapshotRebuildCount;
            for (int i = 0; i < 1000; i++)
                EventBus.Publish(IntChannel, i);

            Assert.That(EventBus.SnapshotRebuildCount, Is.EqualTo(before),
                "订阅表没变时投递不得重建快照（类型化频道与字符串键共用同一套快照机制）");
        }

        [Test]
        public void Publish_Repeatedly_AllocatesNothing()
        {
            EventBus.Subscribe(IntChannel, OnAllocProbe);
            EventBus.Publish(IntChannel, 0);     // 预热：把快照建好

            long before;
            try
            {
                before = GC.GetAllocatedBytesForCurrentThread();
            }
            catch (Exception e) when (e is NotImplementedException || e is PlatformNotSupportedException)
            {
                // 运行时不提供该 API 时不假装通过；快照重建断言（上一条用例）已独立证明同一件事。
                Assert.Ignore("本运行时不支持 GC.GetAllocatedBytesForCurrentThread，零分配改用快照重建次数证明");
                return;
            }

            for (int i = 0; i < 1000; i++)
                EventBus.Publish(IntChannel, i);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0),
                "1000 次 Publish 的托管分配必须是 0（实测 " + allocated + " 字节）");
        }

        static void OnAllocProbe(int payload)
        {
        }

        // ------------------------------------------------------------------
        // 测试用载荷
        // ------------------------------------------------------------------

        readonly struct GoldPayload
        {
            public readonly int Amount;

            public GoldPayload(int amount)
            {
                Amount = amount;
            }
        }
    }
}
