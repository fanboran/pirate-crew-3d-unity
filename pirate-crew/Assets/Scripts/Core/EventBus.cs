using System;
using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 全局事件总线（翻译自 Godot <c>core/autoload/event_bus.gd</c>）。
    ///
    /// 【架构定位】
    ///   模块化架构的解耦核心。模块间通信一律走 EventBus，发布者不需要知道谁在监听，
    ///   监听者也不需要知道谁在发布。对应 Godot 版的 EventBus autoload。
    ///
    /// 【使用方式】
    ///   发布: EventBus.Publish("event_name", payload)
    ///   订阅: EventBus.Subscribe("event_name", OnEventHandler)
    ///   退订: EventBus.Unsubscribe("event_name", OnEventHandler)
    ///
    /// 【约定】
    ///   1. 事件名保留 Godot 版的 snake_case 字符串（跨模块兼容 Godot 版心智）
    ///   2. payload 类型为 object，建议用 Dictionary 以保持可扩展性
    ///   3. 订阅方在 OnDestroy 中退订，防止内存泄漏
    ///   4. Publish 在遍历前对监听者列表做快照，允许回调中安全地订阅/退订
    ///
    /// 【静态残留】
    ///   Unity 关闭 Domain Reload 后静态字段不会自动清空，故用
    ///   <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/> 在进入播放前强制清空。
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<string, List<Action<object>>> _listeners =
            new Dictionary<string, List<Action<object>>>();

        /// <summary>订阅事件（同一回调重复订阅只生效一次，对应 Godot 的 arr.has 去重）。</summary>
        public static void Subscribe(string eventName, Action<object> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null)
                return;

            if (!_listeners.TryGetValue(eventName, out var list))
            {
                list = new List<Action<object>>();
                _listeners[eventName] = list;
            }

            if (!list.Contains(callback))
                list.Add(callback);
        }

        /// <summary>取消订阅事件；某个事件无监听者时移除该键。</summary>
        public static void Unsubscribe(string eventName, Action<object> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null)
                return;

            if (!_listeners.TryGetValue(eventName, out var list))
                return;

            int index = list.IndexOf(callback);
            if (index != -1)
                list.RemoveAt(index);

            if (list.Count == 0)
                _listeners.Remove(eventName);
        }

        /// <summary>
        /// 发布事件。遍历前对列表做 <c>ToArray()</c> 快照，
        /// 因此回调中订阅/退订不会破坏本次遍历（Godot 版用 duplicate() 达成的同一语义）。
        /// </summary>
        public static void Publish(string eventName, object payload = null)
        {
            if (string.IsNullOrEmpty(eventName))
                return;

            if (!_listeners.TryGetValue(eventName, out var list) || list.Count == 0)
                return;

            var snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i]?.Invoke(payload);
        }

        /// <summary>检查某个事件是否有监听者。</summary>
        public static bool HasListeners(string eventName)
        {
            return !string.IsNullOrEmpty(eventName)
                   && _listeners.TryGetValue(eventName, out var list)
                   && list.Count > 0;
        }

        /// <summary>清除某个事件的所有监听者。</summary>
        public static void ClearEvent(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
                return;

            _listeners.Remove(eventName);
        }

        /// <summary>清除所有事件的所有监听者（慎用）。</summary>
        public static void ClearAll()
        {
            _listeners.Clear();
        }

        /// <summary>
        /// 关闭 Domain Reload 时（Enter Play Mode Options）静态字段不会重置，
        /// 进入播放前强制清空，避免上一次运行的监听者残留。
        /// 参照 adammyhre EventBusUtil 的 ClearAllBuses 思路。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode()
        {
            ClearAll();
        }
    }
}
