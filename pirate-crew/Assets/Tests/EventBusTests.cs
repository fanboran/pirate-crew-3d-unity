using System;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Core;

namespace PirateCrew.Tests
{
    /// <summary>
    /// EventBus 单元测试（行为契约：[强类型版] 泛型订阅/发布 + 载荷类型隔离 + 去重 + 遍历快照 +
    /// 快照零分配 + 动态逃生口 + 静态复位）。EventBus 是静态类，测试间必须 ClearAll 隔离静态状态。
    ///
    /// 【本文件锁定的四条关键不变量】
    ///   1. 载荷类型隔离：订阅 <c>int</c> 的收不到 <c>string</c>，也收不到无载荷发布；
    ///   2. 去重：同一回调重复订阅只生效一次（Godot 版 <c>arr.has</c> 语义）；
    ///   3. 遍历安全：回调中订阅/退订不破坏本轮投递（快照语义）；
    ///   4. 投递零分配：订阅表不变时连续 Publish 不重建快照、不产生 GC 分配。
    /// </summary>
    public class EventBusTests
    {
        const string EventName = "test_event";
        const string OtherEventName = "test_other_event";

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
            EventBus.Subscribe<int>(EventName, payload => received = payload);

            EventBus.Publish(EventName, 42);

            Assert.That(received, Is.EqualTo(42));
        }

        [Test]
        public void Publish_WithoutPayload_CallsHandlerWithoutPayload()
        {
            bool called = false;
            EventBus.Subscribe(EventName, () => called = true);

            EventBus.Publish(EventName);

            Assert.That(called, Is.True);
        }

        [Test]
        public void Publish_WithoutListeners_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBus.Publish("nobody_listens", 1));
            Assert.DoesNotThrow(() => EventBus.Publish("nobody_listens"));
        }

        [Test]
        public void Publish_MultipleSubscribers_AllReceiveInSubscriptionOrder()
        {
            var calls = new List<string>();
            EventBus.Subscribe(EventName, () => calls.Add("a"));
            EventBus.Subscribe(EventName, () => calls.Add("b"));
            EventBus.Subscribe(EventName, () => calls.Add("c"));

            EventBus.Publish(EventName);

            Assert.That(calls, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Publish_StructPayload_ArrivesUnboxed()
        {
            // 值类型载荷（工程里绝大多数事件都是 readonly struct）必须原样到达，不能被装箱/降级成 object。
            var received = default(CrewPayload);
            EventBus.Subscribe<CrewPayload>(EventName, payload => received = payload);

            EventBus.Publish(EventName, new CrewPayload(7, "gunner"));

            Assert.That(received.Id, Is.EqualTo(7));
            Assert.That(received.Name, Is.EqualTo("gunner"));
        }

        // ------------------------------------------------------------------
        // 载荷类型隔离（本次重构的核心：类型不匹配不再"静默无效果"）
        // ------------------------------------------------------------------

        [Test]
        public void Publish_OtherPayloadType_IsNotDelivered()
        {
            int intCount = 0;
            int stringCount = 0;
            EventBus.Subscribe<int>(EventName, _ => intCount++);
            EventBus.Subscribe<string>(EventName, _ => stringCount++);

            EventBus.Publish(EventName, 42);

            Assert.That(intCount, Is.EqualTo(1), "int 订阅者必须收到 int 载荷");
            Assert.That(stringCount, Is.EqualTo(0), "string 订阅者不该收到 int 载荷");
        }

        [Test]
        public void Publish_PayloadToNoPayloadSubscriber_IsNotDelivered()
        {
            int noPayloadCount = 0;
            EventBus.Subscribe(EventName, () => noPayloadCount++);

            EventBus.Publish(EventName, 42);

            Assert.That(noPayloadCount, Is.EqualTo(0), "无载荷订阅者不该收到带载荷发布");
        }

        [Test]
        public void Publish_NoPayload_DoesNotReachTypedSubscriber()
        {
            int typed = 0;
            EventBus.Subscribe<int>(EventName, _ => typed++);

            EventBus.Publish(EventName);

            Assert.That(typed, Is.EqualTo(0));
        }

        [Test]
        public void Subscribe_TwoPayloadTypesOnSameKey_EachGetsItsOwn()
        {
            // 同一键可以承载多种载荷（工程里 change_scene 是唯一一例：string 场景名）。
            int intCount = 0;
            int stringCount = 0;
            EventBus.Subscribe<int>(EventName, _ => intCount++);
            EventBus.Subscribe<string>(EventName, _ => stringCount++);

            EventBus.Publish(EventName, 1);
            EventBus.Publish(EventName, "x");

            Assert.That(intCount, Is.EqualTo(1));
            Assert.That(stringCount, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // 退订
        // ------------------------------------------------------------------

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            int count = 0;
            Action<int> handler = _ => count++;
            EventBus.Subscribe(EventName, handler);

            EventBus.Publish(EventName, 1);
            EventBus.Unsubscribe(EventName, handler);
            EventBus.Publish(EventName, 1);

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_ReferenceEquality_RemovesOnlyThatDelegate()
        {
            int first = 0;
            int second = 0;
            Action<int> handlerA = _ => first++;
            Action<int> handlerB = _ => second++;
            EventBus.Subscribe(EventName, handlerA);
            EventBus.Subscribe(EventName, handlerB);

            EventBus.Unsubscribe(EventName, handlerA);
            EventBus.Publish(EventName, 1);

            Assert.That(first, Is.EqualTo(0));
            Assert.That(second, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_TypedHandler_DoesNotRemoveDynamicSubscriber()
        {
            // 载荷类型也是条目标识的一部分：退订 int 订阅不该把动态订阅一起摘掉。
            int typed = 0;
            int dynamic = 0;
            Action<int> typedHandler = _ => typed++;
            Action<object> dynamicHandler = _ => dynamic++;
            EventBus.Subscribe(EventName, typedHandler);
            EventBus.SubscribeDynamic(EventName, dynamicHandler);

            EventBus.Unsubscribe(EventName, typedHandler);
            EventBus.Publish(EventName, 1);

            Assert.That(typed, Is.EqualTo(0));
            Assert.That(dynamic, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_UnknownEventOrCallback_DoesNotThrow()
        {
            Action<int> handler = _ => { };

            Assert.DoesNotThrow(() => EventBus.Unsubscribe("missing_event", handler));
            Assert.DoesNotThrow(() => EventBus.Unsubscribe(EventName, handler));
            Assert.DoesNotThrow(() => EventBus.Unsubscribe("missing_event", () => { }));
            Assert.DoesNotThrow(() => EventBus.UnsubscribeDynamic(EventName, _ => { }));
        }

        // ------------------------------------------------------------------
        // 去重
        // ------------------------------------------------------------------

        [Test]
        public void Subscribe_SameCallbackTwice_IsInvokedOnce()
        {
            int count = 0;
            Action<int> handler = _ => count++;
            EventBus.Subscribe(EventName, handler);
            EventBus.Subscribe(EventName, handler);

            EventBus.Publish(EventName, 1);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(EventBus.ListenerCount(EventName), Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_SameMethodGroupTwice_IsInvokedOnce()
        {
            // 方法组每次转换都生成**新的委托实例**，但 Delegate.Equals 按"目标 + 方法"比较，
            // 所以 OnPayload 订两次仍然只投递一次（旧版靠 List.Contains 得到同一结果）。
            EventBus.Subscribe<int>(EventName, OnPayload);
            EventBus.Subscribe<int>(EventName, OnPayload);

            EventBus.Publish(EventName, 1);

            Assert.That(_methodGroupCalls, Is.EqualTo(1));
        }

        int _methodGroupCalls;

        void OnPayload(int payload)
        {
            _methodGroupCalls++;
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
                EventBus.Unsubscribe(EventName, first);
            };
            Action<int> second = _ => calls.Add("second");

            EventBus.Subscribe(EventName, first);
            EventBus.Subscribe(EventName, second);

            EventBus.Publish(EventName, 1);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second" }), "本轮投递必须用快照，退订不影响本轮");

            EventBus.Publish(EventName, 1);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second", "second" }), "下一轮 first 已不在表中");
        }

        [Test]
        public void Publish_WhenCallbackSubscribesAnother_LateSubscriberWaitsForNextRound()
        {
            var calls = new List<string>();
            Action late = () => calls.Add("late");
            EventBus.Subscribe(EventName, () =>
            {
                calls.Add("early");
                EventBus.Subscribe(EventName, late);
            });

            EventBus.Publish(EventName);
            Assert.That(calls, Is.EqualTo(new[] { "early" }), "本轮快照里没有 late，本轮不该被调用");

            EventBus.Publish(EventName);
            Assert.That(calls, Is.EqualTo(new[] { "early", "early", "late" }));
        }

        [Test]
        public void Publish_WhenCallbackClearsTheEvent_SnapshotIsSafe()
        {
            var calls = new List<string>();
            EventBus.Subscribe(EventName, () =>
            {
                calls.Add("first");
                EventBus.ClearEvent(EventName);
            });
            EventBus.Subscribe(EventName, () => calls.Add("second"));

            Assert.DoesNotThrow(() => EventBus.Publish(EventName));
            Assert.That(calls, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(EventBus.HasListeners(EventName), Is.False);
        }

        // ------------------------------------------------------------------
        // 清理
        // ------------------------------------------------------------------

        [Test]
        public void ClearEvent_RemovesOnlyThatEvent()
        {
            int targetCount = 0;
            int otherCount = 0;
            EventBus.Subscribe(EventName, () => targetCount++);
            EventBus.Subscribe(OtherEventName, () => otherCount++);

            EventBus.ClearEvent(EventName);
            EventBus.Publish(EventName);
            EventBus.Publish(OtherEventName);

            Assert.That(targetCount, Is.EqualTo(0));
            Assert.That(otherCount, Is.EqualTo(1));
        }

        [Test]
        public void ClearAll_RemovesEveryListener()
        {
            int count = 0;
            EventBus.Subscribe(EventName, () => count++);
            EventBus.Subscribe(OtherEventName, () => count++);

            EventBus.ClearAll();
            EventBus.Publish(EventName);
            EventBus.Publish(OtherEventName);

            Assert.That(count, Is.EqualTo(0));
            Assert.That(EventBus.HasListeners(EventName), Is.False);
        }

        [Test]
        public void HasListeners_Semantics()
        {
            Assert.That(EventBus.HasListeners(EventName), Is.False, "无监听者时应为 false");
            Assert.That(EventBus.HasListeners((string)null), Is.False);
            Assert.That(EventBus.HasListeners(string.Empty), Is.False);

            Action<int> handler = _ => { };
            EventBus.Subscribe(EventName, handler);
            Assert.That(EventBus.HasListeners(EventName), Is.True, "订阅后应为 true");

            EventBus.Unsubscribe(EventName, handler);
            Assert.That(EventBus.HasListeners(EventName), Is.False, "退订最后一个监听者后应为 false");

            EventBus.Subscribe(EventName, handler);
            EventBus.ClearEvent(EventName);
            Assert.That(EventBus.HasListeners(EventName), Is.False, "ClearEvent 后应为 false");
        }

        [Test]
        public void Subscribe_NullCallback_IsIgnored()
        {
            Assert.DoesNotThrow(() => EventBus.Subscribe(EventName, (Action<int>)null));
            Assert.DoesNotThrow(() => EventBus.Subscribe(EventName, (Action)null));
            Assert.DoesNotThrow(() => EventBus.SubscribeDynamic(EventName, null));
            Assert.That(EventBus.HasListeners(EventName), Is.False);
        }

        [Test]
        public void Subscribe_EmptyEventName_IsIgnored()
        {
            EventBus.Subscribe<string>((string)null, _ => { });
            EventBus.Subscribe(string.Empty, () => { });
            EventBus.Publish<string>((string)null, "x");

            Assert.That(EventBus.ChannelCount, Is.EqualTo(0));
        }

        [Test]
        public void ResetForNewSession_ClearsSubscriptionsAndViolations()
        {
            EventBus.Subscribe(EventName, () => { });
            EventCatalog.Add<int>("test_contract_event");
            EventBus.Publish("test_contract_event", "wrong-type");

            Assert.That(EventBus.HasListeners(EventName), Is.True);
            Assert.That(EventBus.ContractViolations, Is.Not.Empty);

            try
            {
                EventBus.ResetForNewSession();

                Assert.That(EventBus.HasListeners(EventName), Is.False, "静态复位必须清掉订阅表");
                Assert.That(EventBus.ChannelCount, Is.EqualTo(0));
                Assert.That(EventBus.ContractViolations, Is.Empty, "静态复位必须清掉上局累积的契约违规");
            }
            finally
            {
                EventCatalog.Clear();
            }
        }

        // ------------------------------------------------------------------
        // 无载荷哨兵
        // ------------------------------------------------------------------

        [Test]
        public void SubscribeGenericWithNoPayloadSentinel_IsRejectedWithDiagnostic()
        {
            // NoPayload 是内部哨兵，误用它订阅会记住一个投递侧按裸 Action 调用的条目 → 明确拒绝并留证。
            EventBus.Subscribe<EventBus.NoPayload>(EventName, _ => { });

            Assert.That(EventBus.HasListeners(EventName), Is.False, "哨兵泛型订阅不该建立条目");
            Assert.That(EventBus.ContractViolations, Is.Not.Empty, "误用必须留下可断言的诊断");
        }

        // ------------------------------------------------------------------
        // 动态逃生口（AudioService 用的那一条）
        // ------------------------------------------------------------------

        [Test]
        public void SubscribeDynamic_ReceivesAnyPayloadType()
        {
            var received = new List<object>();
            EventBus.SubscribeDynamic(EventName, received.Add);

            EventBus.Publish(EventName, 1);
            EventBus.Publish(EventName, "text");

            Assert.That(received, Is.EqualTo(new object[] { 1, "text" }));
        }

        [Test]
        public void SubscribeDynamic_NoPayloadPublish_ReceivesNull()
        {
            object received = "sentinel";
            EventBus.SubscribeDynamic(EventName, payload => received = payload);

            EventBus.Publish(EventName);

            Assert.That(received, Is.Null);
        }

        [Test]
        public void UnsubscribeDynamic_StopsDelivery()
        {
            int count = 0;
            Action<object> handler = _ => count++;
            EventBus.SubscribeDynamic(EventName, handler);

            EventBus.Publish(EventName, 1);
            EventBus.UnsubscribeDynamic(EventName, handler);
            EventBus.Publish(EventName, 1);

            Assert.That(count, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // 零分配（重构目标：热路径不再 ToArray()）
        // ------------------------------------------------------------------

        [Test]
        public void Publish_Repeatedly_DoesNotRebuildSnapshot()
        {
            EventBus.Subscribe(EventName, () => { });
            EventBus.Subscribe<int>(EventName, _ => { });
            EventBus.Publish(EventName, 1);      // 建一次快照

            int before = EventBus.SnapshotRebuildCount;
            for (int i = 0; i < 1000; i++)
                EventBus.Publish(EventName, i);

            Assert.That(EventBus.SnapshotRebuildCount, Is.EqualTo(before),
                "订阅表没变时投递不得重建快照（重建 = 每次 ToArray 分配，即本次重构要消灭的行为）");
        }

        [Test]
        public void SnapshotRebuild_AfterSubscribeChange_HappensOnce()
        {
            EventBus.Subscribe<int>(EventName, _ => { });
            EventBus.Publish(EventName, 1);

            int before = EventBus.SnapshotRebuildCount;
            EventBus.Subscribe<int>(EventName, _ => { });   // 表变了 → 下一轮投递重建一次
            EventBus.Publish(EventName, 1);
            EventBus.Publish(EventName, 1);

            Assert.That(EventBus.SnapshotRebuildCount, Is.EqualTo(before + 1),
                "订阅变更后只该重建一次快照，而不是每次投递都重建");
        }

        [Test]
        public void Publish_Repeatedly_AllocatesNothing()
        {
            EventBus.Subscribe<int>(EventName, OnAllocProbe);
            EventBus.Publish(EventName, 0);     // 预热：把快照建好

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
                EventBus.Publish(EventName, i);

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

        readonly struct CrewPayload
        {
            public readonly int Id;
            public readonly string Name;

            public CrewPayload(int id, string name)
            {
                Id = id;
                Name = name;
            }
        }
    }
}
