using System;
using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Core;

namespace PirateCrew.Tests
{
    /// <summary>
    /// EventBus 单元测试（翻译自 event_bus.gd 的行为契约）。
    /// EventBus 是静态类，测试间必须 ClearAll 隔离静态状态。
    /// </summary>
    public class EventBusTests
    {
        const string EventName = "test_event";

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();
        }

        [Test]
        public void Publish_AfterSubscribe_DeliversPayload()
        {
            object received = null;
            EventBus.Subscribe(EventName, payload => received = payload);

            EventBus.Publish(EventName, 42);

            Assert.That(received, Is.EqualTo(42));
        }

        [Test]
        public void Publish_WithoutPayload_DeliversNull()
        {
            bool called = false;
            object received = "sentinel";
            EventBus.Subscribe(EventName, payload =>
            {
                called = true;
                received = payload;
            });

            EventBus.Publish(EventName);

            Assert.That(called, Is.True);
            Assert.That(received, Is.Null);
        }

        [Test]
        public void Publish_WithoutListeners_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBus.Publish("nobody_listens", 1));
        }

        [Test]
        public void Publish_MultipleSubscribers_AllReceive()
        {
            var calls = new List<string>();
            EventBus.Subscribe(EventName, _ => calls.Add("a"));
            EventBus.Subscribe(EventName, _ => calls.Add("b"));
            EventBus.Subscribe(EventName, _ => calls.Add("c"));

            EventBus.Publish(EventName);

            Assert.That(calls, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            int count = 0;
            Action<object> handler = _ => count++;
            EventBus.Subscribe(EventName, handler);

            EventBus.Publish(EventName);
            EventBus.Unsubscribe(EventName, handler);
            EventBus.Publish(EventName);

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_UnknownEventOrCallback_DoesNotThrow()
        {
            Action<object> handler = _ => { };

            Assert.DoesNotThrow(() => EventBus.Unsubscribe("missing_event", handler));
            Assert.DoesNotThrow(() => EventBus.Unsubscribe(EventName, handler));
        }

        [Test]
        public void Subscribe_SameCallbackTwice_IsInvokedOnce()
        {
            int count = 0;
            Action<object> handler = _ => count++;
            EventBus.Subscribe(EventName, handler);
            EventBus.Subscribe(EventName, handler);

            EventBus.Publish(EventName);

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void Publish_WhenCallbackUnsubscribesItself_SnapshotIsSafe()
        {
            var calls = new List<string>();
            Action<object> first = null;
            first = _ =>
            {
                calls.Add("first");
                EventBus.Unsubscribe(EventName, first);
            };
            Action<object> second = _ => calls.Add("second");

            EventBus.Subscribe(EventName, first);
            EventBus.Subscribe(EventName, second);

            // 快照语义：first 在回调中退订自己，不影响本轮对 second 的投递
            EventBus.Publish(EventName);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second" }));

            // 下一轮 first 已不在列表中
            EventBus.Publish(EventName);
            Assert.That(calls, Is.EqualTo(new[] { "first", "second", "second" }));
        }

        [Test]
        public void ClearEvent_RemovesOnlyThatEvent()
        {
            int targetCount = 0;
            int otherCount = 0;
            EventBus.Subscribe(EventName, _ => targetCount++);
            EventBus.Subscribe("other_event", _ => otherCount++);

            EventBus.ClearEvent(EventName);
            EventBus.Publish(EventName);
            EventBus.Publish("other_event");

            Assert.That(targetCount, Is.EqualTo(0));
            Assert.That(otherCount, Is.EqualTo(1));
        }

        [Test]
        public void ClearAll_RemovesEveryListener()
        {
            int count = 0;
            EventBus.Subscribe(EventName, _ => count++);
            EventBus.Subscribe("other_event", _ => count++);

            EventBus.ClearAll();
            EventBus.Publish(EventName);
            EventBus.Publish("other_event");

            Assert.That(count, Is.EqualTo(0));
            Assert.That(EventBus.HasListeners(EventName), Is.False);
        }

        [Test]
        public void HasListeners_Semantics()
        {
            Assert.That(EventBus.HasListeners(EventName), Is.False, "无监听者时应为 false");

            Action<object> handler = _ => { };
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
            Assert.DoesNotThrow(() => EventBus.Subscribe(EventName, null));
            Assert.That(EventBus.HasListeners(EventName), Is.False);
        }
    }
}
