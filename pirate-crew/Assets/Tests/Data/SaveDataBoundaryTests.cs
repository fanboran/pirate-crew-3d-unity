using NUnit.Framework;
using PirateCrew.Core;

namespace PirateCrew.Tests
{
    /// <summary>
    /// <see cref="SaveData"/> / <see cref="SlotMeta"/> / <see cref="SaveMetaData"/> 的边界契约
    /// （纯 C#，无 GameObject——无头验证台 Data 域真跑）。
    ///
    /// 【与 SaveManagerTests 的分工】根目录 SaveManagerTests.cs（EditMode，harness 排除项）覆盖的是
    /// SaveManager/SaveFileIO 胶水与文件层，其 KV 用例只走"干净容器"的往返；
    /// 本文件钉 KV 容器自身的防御性边界——这些边界正是损坏档（JsonUtility 反序列化出
    /// null 条目 / 缺字段）在读侧不炸的最后一道闸，出处 pirate-crew/Assets/Scripts/Core/SaveData.cs。
    ///
    /// 【为什么不走 JsonUtility】FromJson 走原生 ECall，脱离 Unity 运行时必抛
    /// SecurityException（见 external/harness/README.md）——损坏档形态用手工构造列表等价表达。
    /// </summary>
    public class SaveDataBoundaryTests
    {
        // ------------------------------------------------------------------
        // GetData：读侧防御
        // ------------------------------------------------------------------

        [Test]
        public void GetData_DataFieldNull_ReturnsDefault()
        {
            // Data 字段是 public List——反序列化或手工置 null 都可能发生（SaveData.cs:41），
            // 读侧必须返回 default 而不是 NullReferenceException。
            var data = new SaveData { Data = null };

            Assert.That(data.GetData("gold"), Is.Null);
            Assert.That(data.GetData("gold", "fallback"), Is.EqualTo("fallback"));
        }

        [Test]
        public void GetData_NullOrEmptyKey_ReturnsDefault()
        {
            var data = new SaveData();
            data.SetData("gold", "100");

            Assert.That(data.GetData(null), Is.Null);
            Assert.That(data.GetData(null, "fallback"), Is.EqualTo("fallback"));
            Assert.That(data.GetData(""), Is.Null);
            Assert.That(data.GetData("", "fallback"), Is.EqualTo("fallback"));
        }

        [Test]
        public void GetData_SkipsNullEntriesWithoutThrowing()
        {
            // 损坏档等价形态：条目列表里混进 null 元素（SaveData.cs:52 的 entry != null 守卫）。
            // 目标键排在 null 条目之后也必须读得到。
            var data = new SaveData();
            data.Data.Add(null);
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "100" });
            data.Data.Add(null);

            Assert.That(data.GetData("gold"), Is.EqualTo("100"));
        }

        [Test]
        public void GetData_KeyPresentWithNullValue_ReturnsNullNotDefault()
        {
            // 现状语义（SaveData.cs:52-53 直接返回 entry.Value）："键存在但值为 null"
            // 与"键不存在"不可区分地都返回 null——default 只对"没这个键"生效。
            // 固化这一行为：如果将来改成"null 值也回落 default"，这里应随实现一起改。
            var data = new SaveData();
            data.Data.Add(new StringKVEntry { Key = "gold", Value = null });

            Assert.That(data.GetData("gold"), Is.Null);
            Assert.That(data.GetData("gold", "fallback"), Is.Null, "键存在（哪怕值为 null）不回落 default");
            Assert.That(data.HasData("gold"), Is.True);
        }

        [Test]
        public void GetData_EntryWithNullKey_DoesNotMatch()
        {
            // Key 为 null 的条目在比较时被跳过（null == "gold" 为 false），不影响其他键的读取。
            var data = new SaveData();
            data.Data.Add(new StringKVEntry { Key = null, Value = "junk" });
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "100" });

            Assert.That(data.GetData("gold"), Is.EqualTo("100"));
            Assert.That(data.HasData("gold"), Is.True);
        }

        // ------------------------------------------------------------------
        // SetData：写侧防御
        // ------------------------------------------------------------------

        [Test]
        public void SetData_NullOrEmptyKey_IsIgnored()
        {
            // 空键静默拒绝（SaveData.cs:62-63）：既不写入也不惰性建表——
            // 否则会攒下一个谁也读不回来的条目。
            var data = new SaveData();
            data.SetData(null, "a");
            data.SetData("", "b");

            Assert.That(data.Data, Is.Empty);
            Assert.That(data.HasData(null), Is.False);
            Assert.That(data.HasData(""), Is.False);
        }

        [Test]
        public void SetData_DataFieldNull_LazilyCreatesList()
        {
            var data = new SaveData { Data = null };

            data.SetData("gold", "100");

            Assert.That(data.Data, Is.Not.Null, "Data 为 null 时写入应惰性建表（SaveData.cs:65-66）");
            Assert.That(data.GetData("gold"), Is.EqualTo("100"));
        }

        [Test]
        public void SetData_DuplicateKeysInList_OverwritesFirstMatchOnly()
        {
            // 手工构造的重复键（正常写路径不会产生，坏档可能出现）：
            // 覆盖命中第一个匹配即返回（SaveData.cs:68-76），不追加、不去重、不动后续同名条目。
            var data = new SaveData();
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "1" });
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "2" });

            data.SetData("gold", "9");

            Assert.That(data.Data.Count, Is.EqualTo(2), "覆盖不应追加条目");
            Assert.That(data.GetData("gold"), Is.EqualTo("9"), "读也命中第一个匹配");
        }

        [Test]
        public void SetData_NullValue_KeyStaysPresentUntilRemoved()
        {
            // 写 null 值是合法操作：键保留、读回 null；语义上"清值"与"删键"不同——
            // HasData 仍为 true，删除仍返回 true。
            var data = new SaveData();
            data.SetData("gold", "100");
            data.SetData("gold", null);

            Assert.That(data.HasData("gold"), Is.True);
            Assert.That(data.GetData("gold"), Is.Null);
            Assert.That(data.RemoveData("gold"), Is.True);
        }

        // ------------------------------------------------------------------
        // HasData / RemoveData：同套守卫
        // ------------------------------------------------------------------

        [Test]
        public void HasData_NullOrEmptyKeyOrData_ReturnsFalse()
        {
            Assert.That(new SaveData { Data = null }.HasData("k"), Is.False);

            var data = new SaveData();
            data.SetData("gold", "100");
            Assert.That(data.HasData(null), Is.False);
            Assert.That(data.HasData(""), Is.False);
        }

        [Test]
        public void HasData_SkipsNullEntries()
        {
            var data = new SaveData();
            data.Data.Add(null);
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "100" });

            Assert.That(data.HasData("gold"), Is.True);
            Assert.That(data.HasData("silver"), Is.False);
        }

        [Test]
        public void RemoveData_NullOrEmptyKeyOrData_ReturnsFalse()
        {
            Assert.That(new SaveData { Data = null }.RemoveData("k"), Is.False);

            var data = new SaveData();
            data.SetData("gold", "100");
            Assert.That(data.RemoveData(null), Is.False);
            Assert.That(data.RemoveData(""), Is.False);
            Assert.That(data.RemoveData("missing"), Is.False);
        }

        [Test]
        public void RemoveData_SkipsNullEntries()
        {
            // null 条目排在目标键之前也不能挡住删除（SaveData.cs:104 的 Data[i] != null 守卫）。
            var data = new SaveData();
            data.Data.Add(null);
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "100" });

            Assert.That(data.RemoveData("gold"), Is.True);
            Assert.That(data.HasData("gold"), Is.False);
        }

        [Test]
        public void RemoveData_DuplicateKeys_RemovesOnlyFirstMatch()
        {
            var data = new SaveData();
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "1" });
            data.Data.Add(new StringKVEntry { Key = "gold", Value = "2" });

            Assert.That(data.RemoveData("gold"), Is.True);
            Assert.That(data.Data.Count, Is.EqualTo(1), "只移除第一个匹配");
            Assert.That(data.GetData("gold"), Is.EqualTo("2"));
        }

        // ------------------------------------------------------------------
        // SlotMeta / SaveMetaData：字段缺省
        // ------------------------------------------------------------------

        [Test]
        public void SlotMeta_FieldsDefault_ToZeroAndNull()
        {
            // _meta.json 缺字段时 JsonUtility 的等价缺省形态（SaveData.cs:118-128）：
            // Slot=0、DisplayName=null、Timestamp=0——消费方（列表页兜底行）必须吃得下这组缺省。
            var meta = new SlotMeta();

            Assert.That(meta.Slot, Is.EqualTo(0));
            Assert.That(meta.DisplayName, Is.Null);
            Assert.That(meta.Timestamp, Is.EqualTo(0L));
        }

        [Test]
        public void SaveMetaData_Default_SlotsListEmptyNotNull()
        {
            // 字段初始化器保证空列表而非 null（SaveData.cs:138）——
            // ListSlots 之类直接 foreach 的消费方不需要空判。
            var metaData = new SaveMetaData();

            Assert.That(metaData.Slots, Is.Not.Null);
            Assert.That(metaData.Slots, Is.Empty);
        }
    }
}
