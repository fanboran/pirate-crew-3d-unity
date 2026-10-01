using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
    /// 【覆盖边界】这里覆盖槽位 / 元数据 / 事件通知的同步契约与文件层健壮性
    /// （协程类行为本就需要 PlayMode，EditMode 不覆盖）。
    ///
    /// 【已知残余风险】转正走 File.Replace（SaveFileIO.WriteJsonAtomic）：为跨平台一致
    /// 会先删旧 .bak 再 Replace——若 Replace 中途失败，旧档已随 .bak 先删。构造该写失败
    /// 需要文件系统注入点，超出本测试范围，故只在此登记、不做硬测。
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
            // 迁移注册表是 static 状态：上个用例（尤其失败没走到 finally 的）不许泄漏进来
            SaveManager.MigrationSteps.Clear();

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
            SaveManager.MigrationSteps.Clear();

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
        public void SaveFileIO_NegativeSlot_Throws()
        {
            // 槽位合法域 = [0, ∞)（SaveFileIO.MinSlot）；
            // 负号只会拼出无人读写的 slot_-1.json 孤儿文件，界外必须抛而不是静默拼路径。
            Assert.Throws<ArgumentOutOfRangeException>(() => _io.GetSlotPath(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _io.SaveSlot(-1, MakeData("k", "v")));
            Assert.Throws<ArgumentOutOfRangeException>(() => _io.LoadSlot(-1));
        }

        [Test]
        public void SaveFileIO_NullOrEmptyRootPath_Throws()
        {
            // null / 空串根目录会让 Path.Combine 静默落进程当前目录，存档散落到随机工作区——
            // 构造期就抛，不留"看似可用"的实例。
            Assert.Throws<ArgumentNullException>(() => new SaveFileIO(null));
            Assert.Throws<ArgumentException>(() => new SaveFileIO(string.Empty));
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
        public void SaveFileIO_Write_LeavesNoTempFileBehind()
        {
            // 转正语义（SaveFileIO.WriteJsonAtomic：.tmp 写入 → 回读校验 → 转正）的关键终态：
            // .tmp 只是写入中转，首写走 File.Move、覆盖走 File.Replace，两条路径转正后
            // .tmp 都必须消失——残留 .tmp 意味着写入中途断链，不能假装成功。
            Assert.That(_io.SaveSlot(1, MakeData("k", "v1")), Is.True);
            Assert.That(File.Exists(_io.GetSlotPath(1) + SaveFileIO.TempExtension), Is.False,
                "首写（Move 转正）后 .tmp 不应残留");

            Assert.That(_io.SaveSlot(1, MakeData("k", "v2")), Is.True);
            Assert.That(File.Exists(_io.GetSlotPath(1) + SaveFileIO.TempExtension), Is.False,
                "覆盖写（Replace 转正）后 .tmp 不应残留");
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

            // 语义判据：默认显示名由槽位号生成（措辞按文案裁决 2026-10-01 不锁）。
            Assert.That(_manager.LoadFromSlot(3).DisplayName, Does.Contain("3"));
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

            Action<int> handler = slot => busSlot = slot;
            EventBus.Subscribe(SaveEvents.SaveCompleted, handler);

            try
            {
                Assert.That(_manager.SaveToSlot(5, MakeData("k", "v"), "五"), Is.True);
            }
            finally
            {
                EventBus.Unsubscribe(SaveEvents.SaveCompleted, handler);
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
            Action<int> handler = slot => busSlot = slot;
            EventBus.Subscribe(SaveEvents.LoadCompleted, handler);

            try
            {
                Assert.That(_manager.LoadFromSlot(6), Is.Not.Null);
            }
            finally
            {
                EventBus.Unsubscribe(SaveEvents.LoadCompleted, handler);
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
        public void SaveManager_SaveRootPath_Null_FallsBackToPersistentDataPath()
        {
            _manager.SaveRootPath = null;

            Assert.That(_manager.SaveRootPath, Does.Contain("saves"));
        }

        // ------------------------------------------------------------------
        // 版本校验与迁移钩子（读侧门卫）
        // ------------------------------------------------------------------

        [Test]
        public void SaveManager_LoadFromSlot_FutureVersion_ReturnsNullAndLogs()
        {
            // 手写一个"来自未来版本"的档（Version 高于当前 GameVersion）——
            // 模拟用新版游戏存的档被旧版程序读到，必须拒绝而不是当旧档静默错读
            File.WriteAllText(_io.GetSlotPath(1),
                "{\"Version\":\"999.0.0\",\"Timestamp\":1,\"DisplayName\":\"未来档\",\"Data\":[]}");

            LogAssert.Expect(LogType.Error, new Regex("\\[SaveManager\\].*版本不受支持"));

            Assert.That(_manager.LoadFromSlot(1), Is.Null, "未来版本的档必须拒绝读取");
            LogAssert.Expect(LogType.Error, new Regex("\\[SaveManager\\].*版本不受支持"));
            Assert.That(_manager.TryLoadSlotQuiet(1), Is.Null, "静默读档走同一套版本校验");
        }

        [Test]
        public void SaveManager_LoadFromSlot_UnparsableVersion_ReturnsNull()
        {
            // 段比较解析不动的版本号同样拒绝：解析不动就宁可拒绝，不做"大概差不多"的猜测
            File.WriteAllText(_io.GetSlotPath(1),
                "{\"Version\":\"beta-7\",\"Timestamp\":1,\"DisplayName\":\"怪档\",\"Data\":[]}");
            LogAssert.Expect(LogType.Error, new Regex("\\[SaveManager\\].*版本不受支持"));

            Assert.That(_manager.LoadFromSlot(1), Is.Null);
        }

        [Test]
        public void SaveManager_LoadFromSlot_OlderVersion_PassesThroughMigrationPipeline()
        {
            File.WriteAllText(_io.GetSlotPath(1),
                "{\"Version\":\"0.0.1\",\"Timestamp\":1,\"DisplayName\":\"旧档\",\"Data\":[]}");
            SaveManager.MigrationSteps.Add(data =>
            {
                data.SetData("migrated", "yes");
                return data;
            });

            SaveData loaded = _manager.LoadFromSlot(1);

            Assert.That(loaded, Is.Not.Null, "低于当前版本的旧档应放行");
            Assert.That(loaded.GetData("migrated"), Is.EqualTo("yes"), "旧档应经过迁移管线");
        }

        [Test]
        public void SaveManager_MigrationSteps_TransformLoadedData()
        {
            // 管线连通性证明：当前版本档也过注册表，挂一个变换函数就能在读档结果里观测到
            SaveManager.MigrationSteps.Add(data =>
            {
                data.SetData("migrated", "yes");
                return data;
            });

            Assert.That(_manager.SaveToSlot(1, MakeData("k", "v"), "档"), Is.True);

            SaveData loaded = _manager.LoadFromSlot(1);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.GetData("migrated"), Is.EqualTo("yes"), "读档结果应经过迁移注册表");
            Assert.That(loaded.GetData("k"), Is.EqualTo("v"), "迁移不丢既有键值");
        }

        [Test]
        public void SaveManager_MigrationSteps_ReturnNull_RejectsLoad()
        {
            SaveManager.MigrationSteps.Add(_ => null);
            Assert.That(_manager.SaveToSlot(1, MakeData("k", "v"), "档"), Is.True);

            LogAssert.Expect(LogType.Error, new Regex("\\[SaveManager\\].*迁移失败"));

            Assert.That(_manager.LoadFromSlot(1), Is.Null, "迁移步骤返回 null 视为失败，不吐半截档");
        }

        // ------------------------------------------------------------------
        // ListSlots「有档无 meta」兜底
        // ------------------------------------------------------------------

        [Test]
        public void SaveManager_ListSlots_SlotFileWithoutMeta_GetsFallbackRow()
        {
            // 手写槽位文件、不写 _meta.json：模拟元数据损坏/丢失后的"有档无名"——
            // 列表页空掉会让人误以为存档没了，兜底行必须让孤儿档可见
            File.WriteAllText(_io.GetSlotPath(7), "{\"Version\":\"0.3.0\",\"Data\":[]}");
            File.WriteAllText(_io.GetSlotPath(2), "{\"Version\":\"0.3.0\",\"Data\":[]}");

            List<SlotMeta> slots = _manager.ListSlots();

            Assert.That(slots.Count, Is.EqualTo(2), "两个孤儿档都应以兜底行出现在列表");
            Assert.That(slots[0].Slot, Is.EqualTo(2), "兜底行同样按槽位号升序");
            Assert.That(slots[0].DisplayName, Is.EqualTo("槽位 2"), "兜底显示名 = 写侧默认名");
            Assert.That(slots[0].Timestamp, Is.GreaterThan(0), "兜底时间戳取文件最后写入时间");
            Assert.That(slots[1].Slot, Is.EqualTo(7));
            Assert.That(slots[1].DisplayName, Is.EqualTo("槽位 7"));
        }

        [Test]
        public void SaveManager_ListSlots_MetaWins_NoDuplicateFallbackRow()
        {
            // meta 已登记的槽位不应因文件也存在而多出一行兜底（兜底只补"有档无 meta"）
            Assert.That(_manager.SaveToSlot(1, MakeData("k", "v"), "档一"), Is.True);

            List<SlotMeta> slots = _manager.ListSlots();

            Assert.That(slots.Count, Is.EqualTo(1));
            Assert.That(slots[0].DisplayName, Is.EqualTo("档一"));
        }
    }
}
