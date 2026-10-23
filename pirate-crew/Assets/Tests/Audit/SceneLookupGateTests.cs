#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Tests.Audit
{
    /// <summary>
    /// 运行期查找清退闸门（EditMode）：反射调编辑器侧 <c>SceneLookupAudit.AuditOnce</c>，
    /// 断言「未登记命中 == 0 且白名单僵尸 == 0」——「存量可见、新增禁止」的自动化收口。
    ///
    /// 【为什么反射】<c>SceneLookupAudit</c> 在 <c>Assets/Editor/</c>（Assembly-CSharp-Editor），
    /// 本测试程序集不引用它，只能按类型名反射查找，惯例同 <c>LevelAssetGateTests</c> 调校验器；
    /// 反射拿到的是 <c>AuditOutcome</c>（只装 string/int 的机器可读结果），按字段名读。
    ///
    /// 【为什么 #if UNITY_EDITOR】无头验证台把 <c>Assets/Tests/**</c> 编进不引用 UnityEditor 的域，
    /// 不加条件会把主控的域门禁打成编译错误（同 LevelAssetGateTests 的处置）；
    /// 加条件后：Unity 侧 Test Runner 照常发现执行，无头侧本文件编译为空。
    ///
    /// 【假绿防护】断言 TotalHits &gt; 0：正则被改坏或扫描域指错导致零命中时，闸门必须响而不是静默通过。
    /// </summary>
    [TestFixture]
    public class SceneLookupGateTests
    {
        const string AuditTypeName = "PirateCrew.EditorTools.SceneLookupAudit, Assembly-CSharp-Editor";

        [Test]
        public void Scan_ReportsNoUnregisteredCalls()
        {
            object outcome = AuditOnce();

            int total = IntField(outcome, "TotalHits");
            Assert.That(total, Is.GreaterThan(0),
                "扫描零命中——LookupPattern 正则或扫描域（Assets/Scripts）被改坏了，闸门失明");

            System.Collections.IList unregistered = ListField(outcome, "Unregistered");
            Assert.That(unregistered.Count, Is.EqualTo(0),
                "有未登记的运行期查找（改成显式注入，或在 SceneLookupAudit.Whitelist 带理由登记）：\n"
                + JoinLines(unregistered));
        }

        [Test]
        public void Whitelist_HasNoZombieEntries()
        {
            object outcome = AuditOnce();

            System.Collections.IList zombies = ListField(outcome, "Zombies");
            Assert.That(zombies.Count, Is.EqualTo(0),
                "白名单有僵尸条目（登记的调用已扫不到，删掉登记）：\n" + JoinLines(zombies));
        }

        // ------------------------------------------------------------------
        // 反射胶水
        // ------------------------------------------------------------------

        static object AuditOnce()
        {
            System.Type type = System.Type.GetType(AuditTypeName, throwOnError: false);
            if (type == null)
            {
                // 单汇编工具链（无头验证台把全工程源码编进一个程序集）下兜底按类型名找。
                System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && type == null; i++)
                    type = assemblies[i].GetType("PirateCrew.EditorTools.SceneLookupAudit", false);
            }

            Assert.That(type, Is.Not.Null,
                "找不到 SceneLookupAudit（Assets/Editor/）——被改名或删了？");

            System.Reflection.MethodInfo auditOnce = type.GetMethod(
                "AuditOnce", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.That(auditOnce, Is.Not.Null, "SceneLookupAudit 缺少静态 AuditOnce() 入口");
            return auditOnce.Invoke(null, null);
        }

        static int IntField(object outcome, string fieldName)
        {
            var field = outcome.GetType().GetField(fieldName);
            Assert.That(field, Is.Not.Null, "AuditOutcome 缺少字段 " + fieldName);
            return (int)field.GetValue(outcome);
        }

        static System.Collections.IList ListField(object outcome, string fieldName)
        {
            var field = outcome.GetType().GetField(fieldName);
            Assert.That(field, Is.Not.Null, "AuditOutcome 缺少字段 " + fieldName);
            return (System.Collections.IList)field.GetValue(outcome);
        }

        static string JoinLines(System.Collections.IList items)
        {
            var lines = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
                lines[i] = "  - " + items[i];
            return string.Join("\n", lines);
        }
    }
}
#endif
