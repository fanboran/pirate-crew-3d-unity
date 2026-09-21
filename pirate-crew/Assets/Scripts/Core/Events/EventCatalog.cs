using System;
using System.Collections.Generic;

namespace PirateCrew.Core
{
    /// <summary>
    /// 事件契约登记表：**事件名 → 期望载荷类型**（单一事实源，运行期可查）。
    ///
    /// 【解决什么问题】事件名是字符串键，字符串键没有编译期检查——拼错一个字母、或者
    /// 键与载荷张冠李戴，都表现为"事件发了但没人收到"的静默故障（本项目最贵的一类 bug）。
    /// 登记表把每个键**应该配什么载荷**变成可查询的数据，于是：
    ///   · <see cref="EventBus.Publish{T}"/>/<see cref="EventBus.Subscribe{T}"/> 能在运行期
    ///     拿实际载荷类型与登记值对拍，不符即报到 <see cref="EventBus.ContractViolations"/>
    ///     与 Console（编辑器/开发构建下，见 <see cref="EventBus"/> 的契约诊断）；
    ///   · 测试可以反射扫出全仓 <c>*Events</c> 常量类，断言「代码里用的键都已登记」
    ///     且「登记表没有僵尸项」，见 <c>Tests/Core/EventCatalogTests</c>。
    ///
    /// 【登记谁】只登记**跨模块**事件（模块内部状态变化不走 EventBus，见架构原则）；
    /// 每个 <c>XxxEvents</c> 常量类提供 <c>RegisterContracts()</c>（用
    /// <see cref="GameBootstrapAttribute"/> 标注，由唯一入口 <see cref="GameEntryPoint"/> 调用），
    /// 事件名常量与载荷类型写在同一个文件里，杜绝两处漂移。
    ///
    /// 【无载荷事件】登记为 <see cref="EventBus.NoPayload"/>（哨兵类型），查询用
    /// <see cref="IsNoPayloadEvent"/>——不要用 <c>null</c> 类型或 <c>object</c> 搪塞：
    /// "约定无载荷"与"没登记"必须能区分。
    ///
    /// 【新增一个事件的标准动作】
    ///   1. 在所属模块的 <c>XxxEvents</c> 类里加 <c>public const string</c> 常量；
    ///   2. 同一个类的 <c>RegisterContracts()</c> 里加一行 <c>Add&lt;TPayload&gt;(常量)</c>
    ///      （无载荷事件用 <c>AddNoPayload(常量)</c>）；
    ///   3. 在 <c>docs/技术/架构/EventBus事件契约.md</c> 的表格里补一行（发布方/订阅方/载荷）。
    ///   漏了第 2 步会被登记表测试直接判失败（那道测试是这次重构加上的安全网）。
    /// </summary>
    public static class EventCatalog
    {
        static readonly Dictionary<string, Type> _expectedPayloads =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>已登记的事件数量。</summary>
        public static int Count => _expectedPayloads.Count;

        /// <summary>已登记的全部事件名（诊断/测试用，顺序不保证）。</summary>
        public static IReadOnlyCollection<string> Keys => _expectedPayloads.Keys;

        /// <summary>
        /// 登记一个带载荷事件。事件名重复登记为**不同**类型属于契约冲突：
        /// 保留先登记者并打错误日志（重复登记同一个类型则静默忽略，便于幂等重入）。
        /// </summary>
        public static void Add(string eventName, Type payloadType)
        {
            if (string.IsNullOrEmpty(eventName) || payloadType == null)
                return;

            if (_expectedPayloads.TryGetValue(eventName, out Type existing))
            {
                if (!ReferenceEquals(existing, payloadType))
                {
                    Log.Error("[EventCatalog] 事件 \"" + eventName + "\" 被重复登记为不同载荷类型："
                              + existing.Name + " vs " + payloadType.Name + "（保留先登记者 " + existing.Name + "）");
                }

                return;
            }

            _expectedPayloads[eventName] = payloadType;
        }

        /// <summary>登记一个带载荷事件（泛型便利形式）。</summary>
        public static void Add<T>(string eventName)
        {
            Add(eventName, typeof(T));
        }

        /// <summary>登记一个无载荷事件（载荷类型 = <see cref="EventBus.NoPayload"/> 哨兵）。</summary>
        public static void AddNoPayload(string eventName)
        {
            Add(eventName, typeof(EventBus.NoPayload));
        }

        /// <summary>该事件名是否已登记。</summary>
        public static bool Contains(string eventName)
        {
            return !string.IsNullOrEmpty(eventName) && _expectedPayloads.ContainsKey(eventName);
        }

        /// <summary>取某事件登记的期望载荷类型；未登记返回 false（未登记**不**等于违约）。</summary>
        public static bool TryGetExpectedPayload(string eventName, out Type payloadType)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                payloadType = null;
                return false;
            }

            return _expectedPayloads.TryGetValue(eventName, out payloadType);
        }

        /// <summary>该事件是否登记为"无载荷"。</summary>
        public static bool IsNoPayloadEvent(string eventName)
        {
            return TryGetExpectedPayload(eventName, out Type payload)
                   && ReferenceEquals(payload, typeof(EventBus.NoPayload));
        }

        /// <summary>清空登记表（测试 / 重新登记用；运行期由 <see cref="GameEntryPoint"/> 统一重入登记）。</summary>
        public static void Clear()
        {
            _expectedPayloads.Clear();
        }
    }
}
