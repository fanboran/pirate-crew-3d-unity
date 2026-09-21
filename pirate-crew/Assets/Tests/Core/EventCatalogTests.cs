using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 事件契约登记表（<see cref="EventCatalog"/>）的钉子测试：用反射扫全仓 <c>*Events</c> 常量类，
    /// 断言「代码里用到的每个事件名都登记了」且「登记表里没有僵尸项」。
    ///
    /// 【为什么必须有这道测试】事件名是字符串键，编译器查不出"新增了事件但忘了登记"，
    /// 而漏登记的事件在运行期**不会**触发契约告警（未登记的键被视为"内部/测试事件"），
    /// 于是契约会静默漂移。这道测试就是防漂移的唯一防线：
    ///   · 常量类里加了 <c>public const string</c> 而没在 <c>RegisterContracts()</c> 里登记 → 失败；
    ///   · 登记了常量类里不存在的键（改名/删事件的残留）→ 失败；
    ///   · 同一个键在两个常量类里各声明一次（载荷漂移的温床）→ 失败。
    ///
    /// 【③"载荷类型与调用点一致"的取舍（诚实说明）】"调用点实际用的载荷类型"无法静态获得
    /// （C# 没有编译期反射，本工程也不引 Roslyn）。因此采用**运行期断言 + 集成测试**两条：
    ///   ① 运行期：<see cref="EventBus"/> 在发布/订阅时拿实际载荷类型与登记值对拍，
    ///      不符即记入 <see cref="EventBus.ContractViolations"/> 并告警（编辑器/开发构建）；
    ///   ② 测试：<see cref="Integration_SettlementSequenceMatchesDeclaredPayloadTypes"/> 按
    ///      **生产侧真实发布用的载荷类型**打一遍结算序列，断言"投递成功 + 零违规"——
    ///      若有人把登记类型改错，这条会立刻红。
    /// 静态方案（Roslyn 分析器）留给后续工业化步骤，不塞进本次重构。
    /// </summary>
    public class EventCatalogTests
    {
        static readonly Assembly[] ProjectAssemblies =
        {
            typeof(EventBus).Assembly,
            typeof(BattleEvents).Assembly,
            typeof(CampaignEvents).Assembly,
            typeof(CrewManagementEvents).Assembly,
        };

        [SetUp]
        public void SetUp()
        {
            EventCatalog.Clear();
            EventBus.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            EventCatalog.Clear();
            EventBus.ResetForNewSession();
        }

        // ------------------------------------------------------------------
        // ① 常量类 → 登记表
        // ------------------------------------------------------------------

        [Test]
        public void EveryEventsConstant_IsRegisteredInCatalog()
        {
            RegisterAllContracts();

            var missing = new List<string>();
            foreach (KeyValuePair<Type, string> pair in AllDeclaredKeys())
            {
                if (!EventCatalog.Contains(pair.Value))
                    missing.Add(pair.Key.Name + "." + pair.Value);
            }

            Assert.That(missing, Is.Empty,
                "下列事件常量没在 EventCatalog 里登记（在该类的 RegisterContracts() 里加一行）："
                + string.Join("、", missing));
        }

        [Test]
        public void EveryEventsClass_HasDiscoveredRegisterContracts()
        {
            RegisterAllContracts();

            var discovered = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameBootstrap.Entry entry in GameBootstrap.DescribeAll())
            {
                if (entry.Phase == GameBootstrapPhase.Contracts)
                    discovered.Add(entry.Description);
            }

            var missing = new List<string>();
            foreach (Type type in EventsClasses())
            {
                if (!discovered.Contains(type.FullName + ".RegisterContracts"))
                    missing.Add(type.FullName);
            }

            Assert.That(missing, Is.Empty,
                "下列 *Events 常量类没有可被唯一入口发现的 RegisterContracts()"
                + "（需 public static void + [GameBootstrap(GameBootstrapPhase.Contracts)]）："
                + string.Join("、", missing));
        }

        // ------------------------------------------------------------------
        // ② 登记表 → 常量类（无僵尸项）
        // ------------------------------------------------------------------

        [Test]
        public void Catalog_HasNoZombieEntries()
        {
            RegisterAllContracts();

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<Type, string> pair in AllDeclaredKeys())
                declared.Add(pair.Value);

            var zombies = new List<string>();
            foreach (string key in EventCatalog.Keys)
            {
                if (!declared.Contains(key))
                    zombies.Add(key);
            }

            Assert.That(zombies, Is.Empty,
                "下列登记项在 *Events 常量类里已不存在（事件被改名/删除后的残留）："
                + string.Join("、", zombies));
        }

        [Test]
        public void NoEventKeyIsDeclaredInTwoClasses()
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new List<string>();
            foreach (KeyValuePair<Type, string> pair in AllDeclaredKeys())
            {
                if (owners.TryGetValue(pair.Value, out string owner))
                    duplicates.Add(pair.Value + "（" + owner + " / " + pair.Key.Name + "）");
                else
                    owners[pair.Value] = pair.Key.Name;
            }

            Assert.That(duplicates, Is.Empty,
                "同一个事件键被两个常量类声明（载荷漂移的温床）：" + string.Join("、", duplicates));
        }

        // ------------------------------------------------------------------
        // 登记内容本身的质量
        // ------------------------------------------------------------------

        [Test]
        public void RegisteredPayloads_AreConcreteTypes()
        {
            RegisterAllContracts();

            var offenders = new List<string>();
            foreach (string key in EventCatalog.Keys)
            {
                EventCatalog.TryGetExpectedPayload(key, out Type payload);
                if (payload == null || payload == typeof(object) || payload == typeof(Delegate))
                    offenders.Add(key + " → " + (payload == null ? "<null>" : payload.Name));
            }

            Assert.That(offenders, Is.Empty,
                "登记的载荷类型不许是 object/null（那等于放弃类型检查）；无载荷事件请用 AddNoPayload："
                + string.Join("、", offenders));
        }

        [Test]
        public void NoPayloadEvents_AreRegisteredWithSentinel()
        {
            RegisterAllContracts();

            Assert.That(EventCatalog.IsNoPayloadEvent(SceneEvents.GoBack), Is.True,
                "go_back 是无载荷事件，必须登记成 NoPayload 哨兵而不是 string/object");

            EventCatalog.TryGetExpectedPayload(SceneEvents.ChangeScene, out Type changeScenePayload);
            Assert.That(changeScenePayload, Is.EqualTo(typeof(string)),
                "change_scene 的载荷是 string 场景名（字典形式已随强类型化退役）");
        }

        [Test]
        public void Registration_IsIdempotent()
        {
            RegisterAllContracts();
            int first = EventCatalog.Count;

            RegisterAllContracts();     // 幂等重入（例如两处都调了发现）：不得重复计数、不得报冲突

            Assert.That(EventCatalog.Count, Is.EqualTo(first));
            Assert.That(EventBus.ContractViolations, Is.Empty);
        }

        // ------------------------------------------------------------------
        // 运行期断言（③的机制验证）
        // ------------------------------------------------------------------

        [Test]
        public void PublishWithUndeclaredPayloadType_RecordsViolationAndDoesNotDeliver()
        {
            EventCatalog.Add<int>(BattleEvents.TurnEnded);
            int received = 0;
            EventBus.Subscribe<int>(BattleEvents.TurnEnded, payload => received = payload);

            EventBus.Publish(BattleEvents.TurnEnded, "42");     // 契约定的是 int，实际发了 string

            Assert.That(received, Is.EqualTo(0), "张冠李戴的载荷不该被投递（订阅方等的是 int）");
            Assert.That(EventBus.ContractViolations, Is.Not.Empty,
                "发布方用错载荷类型必须留证（这是防『静默无效果』的关键）");
        }

        [Test]
        public void PublishWithDeclaredPayloadType_RecordsNoViolation()
        {
            EventCatalog.Add<int>(BattleEvents.TurnEnded);
            int received = 0;
            EventBus.Subscribe<int>(BattleEvents.TurnEnded, payload => received = payload);

            EventBus.Publish(BattleEvents.TurnEnded, 7);

            Assert.That(received, Is.EqualTo(7));
            Assert.That(EventBus.ContractViolations, Is.Empty);
        }

        [Test]
        public void SubscribeWithUndeclaredPayloadType_RecordsViolation()
        {
            EventCatalog.Add<int>(BattleEvents.TurnEnded);

            EventBus.Subscribe<string>(BattleEvents.TurnEnded, _ => { });

            Assert.That(EventBus.ContractViolations, Is.Not.Empty,
                "订阅方用错载荷类型（键选对了、类型写错了）同样必须留证");
        }

        [Test]
        public void UnregisteredKey_IsNotTreatedAsViolation()
        {
            // 未登记的键 = 内部/测试事件，不参与契约检查（否则每个测试键都会污染违规记录）。
            EventBus.Publish("internal_only_event", 42);
            EventBus.Subscribe<string>("internal_only_event", _ => { });

            Assert.That(EventBus.ContractViolations, Is.Empty);
        }

        [Test]
        public void NoPayloadPublishOnNoPayloadKey_RecordsNoViolation()
        {
            EventCatalog.AddNoPayload(SceneEvents.GoBack);
            int calls = 0;
            EventBus.Subscribe(SceneEvents.GoBack, () => calls++);

            EventBus.Publish(SceneEvents.GoBack);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(EventBus.ContractViolations, Is.Empty);
        }

        // ------------------------------------------------------------------
        // ③ 集成：按生产侧真实载荷类型打一遍主要事件
        // ------------------------------------------------------------------

        [Test]
        public void Integration_SettlementSequenceMatchesDeclaredPayloadTypes()
        {
            RegisterAllContracts();

            // 订阅方用**契约登记的类型**；发布方用**生产代码真实发布的载荷构造**（见
            // BattleController.cs / PirateBase.cs / CampaignApi.cs 的 EventBus.Publish 调用点）。
            int battleStarted = 0;
            int crewDied = 0;
            int matchFinished = 0;
            int mapCompleted = 0;

            EventBus.Subscribe<BattleStartedPayload>(BattleEvents.BattleStarted, _ => battleStarted++);
            EventBus.Subscribe<CrewDiedPayload>(BattleEvents.CrewDied, _ => crewDied++);
            EventBus.Subscribe<MatchFinishedPayload>(BattleEvents.MatchFinished, _ => matchFinished++);
            EventBus.Subscribe<CampaignMapCompletedPayload>(CampaignEvents.MapCompleted, _ => mapCompleted++);

            EventBus.Publish(BattleEvents.BattleStarted, new BattleStartedPayload(101, 2));
            EventBus.Publish(BattleEvents.CrewDied, new CrewDiedPayload(0, 0, "redPirate"));
            EventBus.Publish(BattleEvents.MatchFinished, new MatchFinishedPayload(0, 1200, true));
            EventBus.Publish(CampaignEvents.MapCompleted,
                new CampaignMapCompletedPayload("wreck_hymn", 3, true, true, 1200));

            Assert.That(new[] { battleStarted, crewDied, matchFinished, mapCompleted },
                Is.EqualTo(new[] { 1, 1, 1, 1 }), "主要事件必须按登记类型投递到订阅方");
            Assert.That(EventBus.ContractViolations, Is.Empty,
                "主要事件的发布/订阅类型与登记不符： " + string.Join(" / ", EventBus.ContractViolations));
        }

        // ------------------------------------------------------------------
        // 反射工具
        // ------------------------------------------------------------------

        static void RegisterAllContracts()
        {
            GameBootstrap.RunPhase(GameBootstrapPhase.Contracts);
        }

        /// <summary>全仓 <c>*Events</c> 常量类里的 (所属类型, 事件名) 清单。</summary>
        static IEnumerable<KeyValuePair<Type, string>> AllDeclaredKeys()
        {
            foreach (Type type in EventsClasses())
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (!field.IsLiteral || field.FieldType != typeof(string))
                        continue;

                    yield return new KeyValuePair<Type, string>(type, (string)field.GetRawConstantValue());
                }
            }
        }

        static IEnumerable<Type> EventsClasses()
        {
            var seen = new HashSet<Type>();
            for (int a = 0; a < ProjectAssemblies.Length; a++)
            {
                Type[] types;
                try
                {
                    types = ProjectAssemblies[a].GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type == null || !type.IsClass || !type.IsAbstract || !type.IsSealed)
                        continue;   // 只要静态类
                    if (!type.Name.EndsWith("Events", StringComparison.Ordinal))
                        continue;
                    if (!HasEventKeyConstant(type))
                        continue;
                    if (seen.Add(type))
                        yield return type;
                }
            }
        }

        static bool HasEventKeyConstant(Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsLiteral && fields[i].FieldType == typeof(string))
                    return true;
            }

            return false;
        }
    }
}
