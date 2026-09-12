using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace PirateCrew.Tests
{
    /// <summary>
    /// SaveManager / SaveFileIO 单元测试（EditMode）。
    ///
    /// 【隔离方式】每个用例建独立临时目录：SaveFileIO 直接构造，SaveManager 通过
    /// <see cref="SaveManager.SaveRootPath"/> 注入；[TearDown] 删除临时目录。
    ///
    /// 【覆盖边界】自动存档协程需要真实协程调度（PlayMode），EditMode 下不启动；
    /// 这里覆盖其同步契约（TriggerAutoSave 未启用时返回 false）与文件层健壮性。
    /// </summary>
    public class SaveManagerTests
    {
        string _tempDir;
        GameObject _go;
        SaveManager _manager;
        SaveFileIO _io;

        [SetUp]
        public void SetUp()
        {
            EventBus.ClearAll();
            LogAssert.ignoreFailingMessages = false;

            _tempDir = Path.Combine(Path.GetTempPath(), "PirateCrewSaveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _io = new SaveFileIO(_tempDir);

            _go = new GameObject("SaveManagerUnderTest");
            _manager = _go.AddComponent<SaveManager>();
            _manager.SaveRootPath = _tempDir;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            EventBus.ClearAll();

            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);

            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch (Exception)
            {
                // 临时目录清理失败不影响测试结论
            }
        }

        static SaveData MakeData(string key, string value)
        {
            var data = new SaveData();
            data.SetData(key, value);
            return data;
        }

        // ------------------------------------------------------------------
        // SaveData：data_manager 合并进来的键值容器
        // ------------------------------------------------------------------

        [Test]
        public void SaveData_SetGetData_RoundTrips()
        {
            var data = new SaveData();

            data.SetData("gold", "100");

            Assert.That(data.GetData("gold"), Is.EqualTo("100"));
            Assert.That(data.HasData("gold"), Is.True);
        }

        [Test]
        public void SaveData_SetData_SameKeyOverwrites()
        {
            var data = new SaveData();
            data.SetData("gold", "100");
            data.SetData("gold", "250");

            Assert.That(data.GetData("gold"), Is.EqualTo("250"));
            Assert.That(data.Data.Count, Is.EqualTo(1), "同一键不应产生重复条目");
        }

        [Test]
        public void SaveData_GetData_MissingKey_ReturnsDefault()
        {
            var data = new SaveData();

            Assert.That(data.GetData("missing"), Is.Null);
            Assert.That(data.GetData("missing", "fallback"), Is.EqualTo("fallback"));
        }

        [Test]
        public void SaveData_RemoveData_RemovesEntry()
        {
            var data = MakeData("gold", "100");

            Assert.That(data.RemoveData("gold"), Is.True);
            Assert.That(data.HasData("gold"), Is.False);
            Assert.That(data.RemoveData("gold"), Is.False);
        }

        [Test]
        public void SaveData_KvSurvivesJsonRoundTrip()
        {
            var data = MakeData("captain", "jack");
            data.SetData("gold", "100");

            Assert.That(_io.SaveSlot(1, data), Is.True);

            SaveData loaded = _io.LoadSlot(1);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.GetData("captain"), Is.EqualTo("jack"));
            Assert.That(loaded.GetData("gold"), Is.EqualTo("100"));
        }

        // ------------------------------------------------------------------
        // SaveFileIO：健壮性契约（.tmp 校验 + .bak 回滚）
        // ------------------------------------------------------------------

        [Test]
        public void SaveFileIO_SaveThenLoad_RoundTrips()
        {
            Assert.That(_io.SaveSlot(1, MakeData("k", "v")), Is.True);
            Assert.That(_io.SlotExists(1), Is.True);

            SaveData loaded = _io.LoadSlot(1);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.GetData("k"), Is.EqualTo("v"));
        }

        [Test]
        public void SaveFileIO_LoadMissingSlot_ReturnsNull()
        {
            Assert.That(_io.LoadSlot(99), Is.Null);
        }

        [Test]
        public void SaveFileIO_Overwrite_CreatesBackupAndKeepsLatest()
        {
            Assert.That(_io.SaveSlot(1, MakeData("k", "old")), Is.True);
            Assert.That(_io.SaveSlot(1, MakeData("k", "new")), Is.True);

            SaveData loaded = _io.LoadSlot(1);
            Assert.That(loaded.GetData("k"), Is.EqualTo("new"), "正式文件应为最新版");

            string backupPath = _io.GetSlotPath(1) + SaveFileIO.BackupExtension;
            Assert.That(File.Exists(backupPath), Is.True, "覆盖时应生成 .bak");
            SaveData backup = JsonUtility.FromJson<SaveData>(File.ReadAllText(backupPath));
            Assert.That(backup.GetData("k"), Is.EqualTo("old"), ".bak 应为上一版");
        }

        [Test]
        public void SaveFileIO_LoadCorruptedSlot_RollsBackToBackup()
        {
            Assert.That(_io.SaveSlot(1, MakeData("k", "v1")), Is.True); // 首次：无 .bak
            Assert.That(_io.SaveSlot(1, MakeData("k", "v2")), Is.True); // 第二次：.bak = v1

            string slotPath = _io.GetSlotPath(1);
            Assert.That(File.Exists(slotPath + SaveFileIO.BackupExtension), Is.True);

            // 解析失败路径会打 warning（JsonUtility 也可能自行记日志），此处只关心回滚结果
            LogAssert.ignoreFailingMessages = true;
            File.WriteAllText(slotPath, "{ 这不是合法 JSON");

            SaveData loaded = _io.LoadSlot(1);

            Assert.That(loaded, Is.Not.Null, "损坏后应能从 .bak 回滚");
            Assert.That(loaded.GetData("k"), Is.EqualTo("v1"), "回滚内容应为上一版");
        }

        [Test]
        public void SaveFileIO_LoadCorruptedSlotWithoutBackup_ReturnsNull()
        {
            File.WriteAllText(_io.GetSlotPath(2), "{ 坏文件");
            // 无 .bak 时回滚会打一条 error，本用例正是要断言该失败路径
            LogAssert.ignoreFailingMessages = true;

            Assert.That(_io.LoadSlot(2), Is.Null);
        }

        [Test]
        public void SaveFileIO_DeleteSlot_RemovesFileAndBackup()
        {
            _io.SaveSlot(1, MakeData("k", "v1"));
            _io.SaveSlot(1, MakeData("k", "v2"));

            Assert.That(_io.DeleteSlot(1), Is.True);
            Assert.That(_io.SlotExists(1), Is.False);
            Assert.That(File.Exists(_io.GetSlotPath(1) + SaveFileIO.BackupExtension), Is.False);
            Assert.That(_io.DeleteSlot(1), Is.False, "重复删除应返回 false");
        }

        // ------------------------------------------------------------------
        // SaveManager：槽位 / 元数据 / 事件
        // ------------------------------------------------------------------

        [Test]
        public void SaveManager_SaveThenLoad_RoundTrips()
        {
            Assert.That(_manager.SaveToSlot(1, MakeData("gold", "100"), "第一档"), Is.True);
            Assert.That(_manager.SlotExists(1), Is.True);

            SaveData loaded = _manager.LoadFromSlot(1);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.GetData("gold"), Is.EqualTo("100"));
            Assert.That(loaded.DisplayName, Is.EqualTo("第一档"));
            Assert.That(loaded.Version, Is.Not.Empty);
            Assert.That(loaded.Timestamp, Is.GreaterThan(0));
        }

        [Test]
        public void SaveManager_LoadFromSlot_Missing_ReturnsNull()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.That(_manager.LoadFromSlot(42), Is.Null);
        }

        [Test]
        public void SaveManager_SaveToSlot_DefaultDisplayNameUsesSlotNumber()
        {
            Assert.That(_manager.SaveToSlot(3, MakeData("k", "v")), Is.True);

            Assert.That(_manager.LoadFromSlot(3).DisplayName, Is.EqualTo("槽位 3"));
        }

        [Test]
        public void SaveManager_SaveToSlot_NullData_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;

            Assert.That(_manager.SaveToSlot(1, null, "x"), Is.False);
        }

        [Test]
        public void SaveManager_SaveToSlot_FiresLocalEventAndEventBus()
        {
            int localSlot = -1;
            int busSlot = -1;
            _manager.SaveCompleted += slot => localSlot = slot;

            Action<object> handler = payload =>
            {
                if (payload is int slot)
                    busSlot = slot;
            };
            EventBus.Subscribe("save_completed", handler);

            try
            {
                Assert.That(_manager.SaveToSlot(5, MakeData("k", "v"), "五"), Is.True);
            }
            finally
            {
                EventBus.Unsubscribe("save_completed", handler);
            }

            Assert.That(localSlot, Is.EqualTo(5));
            Assert.That(busSlot, Is.EqualTo(5));
        }

        [Test]
        public void SaveManager_LoadFromSlot_FiresLocalEventAndEventBus()
        {
            _manager.SaveToSlot(6, MakeData("k", "v"), "六");

            int localSlot = -1;
            int busSlot = -1;
            _manager.LoadCompleted += slot => localSlot = slot;
            Action<object> handler = payload =>
            {
                if (payload is int slot)
                    busSlot = slot;
            };
            EventBus.Subscribe("load_completed", handler);

            try
            {
                Assert.That(_manager.LoadFromSlot(6), Is.Not.Null);
            }
            finally
            {
                EventBus.Unsubscribe("load_completed", handler);
            }

            Assert.That(localSlot, Is.EqualTo(6));
            Assert.That(busSlot, Is.EqualTo(6));
        }

        [Test]
        public void SaveManager_Meta_ReflectsSaveAndListSlots_SortedAscending()
        {
            _manager.SaveToSlot(2, MakeData("k", "v2"), "档二");
            _manager.SaveToSlot(1, MakeData("k", "v1"), "档一");

            SlotMeta meta = _manager.GetSlotMeta(1);
            Assert.That(meta, Is.Not.Null);
            Assert.That(meta.DisplayName, Is.EqualTo("档一"));
            Assert.That(meta.Timestamp, Is.GreaterThan(0));

            List<SlotMeta> slots = _manager.ListSlots();
            Assert.That(slots.Count, Is.EqualTo(2));
            Assert.That(slots[0].Slot, Is.EqualTo(1), "应按槽位号升序");
            Assert.That(slots[1].Slot, Is.EqualTo(2));
        }

        [Test]
        public void SaveManager_GetSlotMeta_MissingSlot_ReturnsNull()
        {
            Assert.That(_manager.GetSlotMeta(42), Is.Null);
        }

        [Test]
        public void SaveManager_ListSlots_ReadsMetaOnly_NotSlotFiles()
        {
            Assert.That(_manager.SaveToSlot(1, MakeData("k", "v"), "档一"), Is.True);

            // 手动删掉槽位文件：元数据仍在（Unity 版设计：列表以 _meta.json 为唯一来源）
            File.Delete(Path.Combine(_tempDir, "slot_1.json"));

            Assert.That(_manager.SlotExists(1), Is.False);
            Assert.That(_manager.ListSlots().Count, Is.EqualTo(1));
            Assert.That(_manager.GetSlotMeta(1), Is.Not.Null);
        }

        [Test]
        public void SaveManager_DeleteSlot_RemovesFileAndMeta()
        {
            Assert.That(_manager.SaveToSlot(3, MakeData("k", "v"), "三"), Is.True);

            Assert.That(_manager.DeleteSlot(3), Is.True);
            Assert.That(_manager.SlotExists(3), Is.False);
            Assert.That(_manager.GetSlotMeta(3), Is.Null);
            Assert.That(_manager.DeleteSlot(3), Is.False, "重复删除应返回 false");
        }

        [Test]
        public void SaveManager_SaveToSlot_OverwritesOldSave()
        {
            _manager.SaveToSlot(1, MakeData("k", "old"), "旧档");
            _manager.SaveToSlot(1, MakeData("k", "new"), "新档");

            SaveData loaded = _manager.LoadFromSlot(1);
            Assert.That(loaded.GetData("k"), Is.EqualTo("new"));

            SlotMeta meta = _manager.GetSlotMeta(1);
            Assert.That(meta.DisplayName, Is.EqualTo("新档"));
            Assert.That(_manager.ListSlots().Count, Is.EqualTo(1), "覆盖不应产生新槽位");
        }

        [Test]
        public void SaveManager_TriggerAutoSave_WhenDisabled_ReturnsFalse()
        {
            Assert.That(_manager.TriggerAutoSave(), Is.False);
        }

        [Test]
        public void SaveManager_SaveRootPath_Null_FallsBackToPersistentDataPath()
        {
            _manager.SaveRootPath = null;

            Assert.That(_manager.SaveRootPath, Does.Contain("saves"));
        }
    }
}
