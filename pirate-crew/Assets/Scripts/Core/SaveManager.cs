using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 存档/读档服务（含键值持久化职责）。
    ///
    /// 【架构定位】
    ///   全局服务，由 Bootstrapper 创建并随 Services 对象 DontDestroyOnLoad（同 SceneLoader）。
    ///   Core 层不依赖玩法模块：自动存档的数据由玩法模块通过 <see cref="AutoSaveDataProvider"/> 注入。
    ///
    /// 【核心职责】
    ///   1. 多槽位存档：SaveToSlot / LoadFromSlot / DeleteSlot / ListSlots / SlotExists
    ///   2. 槽位元数据：独立 _meta.json；GetSlotMeta / ListSlots 以元数据为唯一来源、不碰槽位文件
    ///      （本工程定案：以元数据为唯一来源，不扫描/读取槽位文件）
    ///   3. 自动存档：EnableAutoSave 协程定时 + TriggerAutoSave 手动触发 + 退出兜底
    ///   4. 事件通知：本地 event + 转发 EventBus 频道（SaveEvents.SaveCompleted / LoadCompleted / AutoSaveTriggered）
    ///
    /// 【文件布局】&lt;SaveRootPath&gt;/slot_{n}.json、&lt;SaveRootPath&gt;/_meta.json（+ .bak / .tmp）
    ///   默认 SaveRootPath = Application.persistentDataPath/saves；测试或特殊平台可注入覆盖。
    ///
    /// 【键值持久化】键值随档写入（<see cref="SaveData.GetData"/> / <see cref="SaveData.SetData"/>），
    ///   不再单开全局文件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        /// <summary>
        /// 自动存档占用的槽位号。
        /// 手动存档请从 1 起，避免覆盖自动存档。
        /// </summary>
        public const int AutoSaveSlot = 0;

        /// <summary>自动存档默认间隔（秒）。</summary>
        public const float DefaultAutoSaveInterval = 60f;

        /// <summary>自动存档最短间隔，防止传入 0/负值时协程每帧空转。</summary>
        const float MinAutoSaveInterval = 1f;

        [SerializeField] string savesFolder = "saves";

        string _saveRootPath;
        SaveFileIO _io;
        Coroutine _autoSaveRoutine;
        bool _autoSaveEnabled;
        float _autoSaveInterval = DefaultAutoSaveInterval;

        /// <summary>
        /// 全局访问入口（由 Bootstrapper 创建本组件后可用）。
        /// 【定位】与 <see cref="Services"/> 注册表双轨并存：本属性是**便捷过渡入口**，
        /// 权威定位器是 <see cref="Services"/>（Bootstrapper 已把本组件 Register 进注册表）——
        /// 新代码优先经 Services 解析，本属性仅为存量调用点保留。
        /// </summary>
        public static SaveManager Instance { get; private set; }

        // 【存件·自动存档链（代码审计登记）】EnableAutoSave → AutoSaveRoutine → TriggerAutoSave
        // → AutoSaveTriggered 整条链当前**零调用方**：没有任何代码调 EnableAutoSave，故
        // _autoSaveEnabled 恒为 false，OnApplicationQuit 的 TriggerAutoSave 每次都在开关处短路；
        // AutoSaveDataProvider 也从未被赋值。保留原因：自动存档是完整实现的标准功能，启用路径 =
        // Bootstrapper 调 EnableAutoSave 并注入 AutoSaveDataProvider。删除或接线二选一，别让链上成员各自零散演化。

        /// <summary>存档成功。</summary>
        public event Action<int> SaveCompleted;

        /// <summary>读档成功。</summary>
        public event Action<int> LoadCompleted;

        /// <summary>自动存档触发。</summary>
        public event Action<int> AutoSaveTriggered;

        /// <summary>
        /// 自动存档数据来源（由玩法模块注册；Core 不依赖玩法，故用委托注入）。
        /// 返回 null 时本次自动存档跳过；抛异常会被捕获，不影响其他系统。
        /// </summary>
        public Func<SaveData> AutoSaveDataProvider { get; set; }

        /// <summary>
        /// 存档根目录。默认 <c>Application.persistentDataPath/saves</c>；
        /// 设置后立即切换到新目录（测试注入临时目录的关键入口）；赋空则回退默认值。
        /// </summary>
        public string SaveRootPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_saveRootPath))
                    return _saveRootPath;

                string folder = string.IsNullOrEmpty(savesFolder) ? "saves" : savesFolder;
                return Path.Combine(Application.persistentDataPath, folder);
            }
            set
            {
                _saveRootPath = value;
                _io = null; // 惰性重建，指向新目录
            }
        }

        /// <summary>自动存档是否已启用。</summary>
        public bool AutoSaveEnabled => _autoSaveEnabled;

        /// <summary>当前自动存档间隔（秒）。</summary>
        public float AutoSaveInterval => _autoSaveInterval;

        SaveFileIO Io => _io ?? (_io = new SaveFileIO(SaveRootPath));

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                global::PirateCrew.Core.Log.Warn("[SaveManager] 已存在实例，销毁重复对象: " + name);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // DontDestroyOnLoad 由 Bootstrapper 统一处理（同 SceneLoader 约定）。
            try
            {
                Io.EnsureDirectory();
            }
            catch (Exception e)
            {
                global::PirateCrew.Core.Log.Error("[SaveManager] 创建存档目录失败: " + e);
            }
        }

        void OnDestroy()
        {
            DisableAutoSave();

            if (Instance == this)
                Instance = null;
        }

        void OnApplicationQuit()
        {
            // 兜底：退出前落盘一次（未启用自动存档或无数据源时为 no-op）
            TriggerAutoSave();
        }

        // ------------------------------------------------------------------
        // 槽位 API
        // ------------------------------------------------------------------

        /// <summary>
        /// 保存数据到指定槽位（对应 <c>save_to_slot</c>）。
        /// 会覆盖 data 的 Version / Timestamp / DisplayName，并更新 _meta.json。
        /// </summary>
        /// <param name="slot">槽位号（0 预留给自动存档，手动存档建议从 1 起）。</param>
        /// <param name="data">存档数据；为 null 直接失败。</param>
        /// <param name="displayName">显示名称；空则用「槽位 n」。</param>
        public bool SaveToSlot(int slot, SaveData data, string displayName = "")
        {
            if (data == null)
            {
                global::PirateCrew.Core.Log.Error("[SaveManager] SaveToSlot 收到空数据，slot=" + slot);
                return false;
            }

            data.Version = GameVersion();
            data.Timestamp = NowUnixSeconds();
            data.DisplayName = string.IsNullOrEmpty(displayName) ? ("槽位 " + slot) : displayName;

            if (!Io.SaveSlot(slot, data))
                return false;

            UpsertMetaSlot(slot, data.DisplayName, data.Timestamp);

            SaveCompleted?.Invoke(slot);
            EventBus.Publish(SaveEvents.SaveCompleted, slot);
            return true;
        }

        /// <summary>
        /// 从指定槽位读取（对应 <c>load_from_slot</c>）。
        /// 失败或不存在返回 null。
        /// </summary>
        public SaveData LoadFromSlot(int slot)
        {
            SaveData data = LoadSlotCore(slot);
            if (data == null)
                return null;

            LoadCompleted?.Invoke(slot);
            EventBus.Publish(SaveEvents.LoadCompleted, slot);
            return data;
        }

        /// <summary>
        /// 静默读档：与 <see cref="LoadFromSlot"/> 同一套读取（含 .bak 回滚），但**不广播**
        /// 本地 <see cref="LoadCompleted"/> 与 <c>SaveEvents.LoadCompleted</c>。
        /// 给"读档只是拿数据的中间步骤"的调用方用（如保存前读回旧档做合并）——那种读档
        /// 一旦广播出去，监听方会把它当成"用户执行了读档"，触发不该有的界面刷新/状态重载。
        /// </summary>
        public SaveData TryLoadSlotQuiet(int slot)
        {
            return LoadSlotCore(slot);
        }

        /// <summary>LoadFromSlot / TryLoadSlotQuiet 共用的读取体（不含任何事件通知）。</summary>
        SaveData LoadSlotCore(int slot)
        {
            SaveData data = Io.LoadSlot(slot);
            if (data == null)
                global::PirateCrew.Core.Log.Warn("[SaveManager] 读档失败或槽位不存在: " + slot);
            return data;
        }

        /// <summary>读取槽位元数据（只读 _meta.json，不碰槽位文件）；不存在返回 null。</summary>
        public SlotMeta GetSlotMeta(int slot)
        {
            List<SlotMeta> slots = LoadMetaSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].Slot == slot)
                    return slots[i];
            }

            return null;
        }

        /// <summary>
        /// 列出全部槽位元数据（只读 _meta.json，不扫描/读取槽位文件）；按槽位号升序。
        /// </summary>
        public List<SlotMeta> ListSlots()
        {
            List<SlotMeta> slots = LoadMetaSlots();
            slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return slots;
        }

        /// <summary>
        /// 删除槽位（对应 <c>delete_slot</c>）：文件存在才删除并清元数据，
        /// 返回是否真的删掉了文件。
        /// </summary>
        public bool DeleteSlot(int slot)
        {
            bool deleted = Io.DeleteSlot(slot);
            if (deleted)
                RemoveMetaSlot(slot);

            return deleted;
        }

        /// <summary>槽位文件是否存在（对应 <c>slot_exists</c>）。</summary>
        public bool SlotExists(int slot)
        {
            return Io.SlotExists(slot);
        }

        // ------------------------------------------------------------------
        // 自动存档
        // ------------------------------------------------------------------

        /// <summary>启用自动存档（对应 <c>enable_auto_save</c>）；重复调用会按新间隔重置计时。</summary>
        public void EnableAutoSave(float intervalSeconds = DefaultAutoSaveInterval)
        {
            _autoSaveInterval = Mathf.Max(intervalSeconds, MinAutoSaveInterval);
            _autoSaveEnabled = true;

            if (_autoSaveRoutine != null)
                StopCoroutine(_autoSaveRoutine);

            _autoSaveRoutine = StartCoroutine(AutoSaveRoutine(_autoSaveInterval));
        }

        /// <summary>禁用自动存档（对应 <c>disable_auto_save</c>）。</summary>
        public void DisableAutoSave()
        {
            _autoSaveEnabled = false;

            if (_autoSaveRoutine != null)
            {
                StopCoroutine(_autoSaveRoutine);
                _autoSaveRoutine = null;
            }
        }

        /// <summary>
        /// 立刻触发一次自动存档（对应 <c>trigger_auto_save</c>）。
        /// 未启用自动存档、未注册数据源、数据源抛异常或返回 null 时返回 false，
        /// 且任何失败都不会让定时协程中断。
        /// </summary>
        public bool TriggerAutoSave()
        {
            if (!_autoSaveEnabled)
                return false;

            if (AutoSaveDataProvider == null)
            {
                global::PirateCrew.Core.Log.Warn("[SaveManager] 未注册 AutoSaveDataProvider，跳过自动存档");
                return false;
            }

            SaveData data;
            try
            {
                data = AutoSaveDataProvider();
            }
            catch (Exception e)
            {
                // 数据源异常只废掉本次自动存档，可恢复，收口为 Warn（发布版不再留日志）
                global::PirateCrew.Core.Log.Warn("[SaveManager] 自动存档数据源异常: " + e);
                return false;
            }

            if (data == null)
            {
                global::PirateCrew.Core.Log.Warn("[SaveManager] 自动存档数据源返回 null，跳过本次");
                return false;
            }

            // 先发事件，再写盘
            AutoSaveTriggered?.Invoke(AutoSaveSlot);
            EventBus.Publish(SaveEvents.AutoSaveTriggered, AutoSaveSlot);

            return SaveToSlot(AutoSaveSlot, data, "自动存档");
        }

        IEnumerator AutoSaveRoutine(float interval)
        {
            // WaitForSecondsRealtime：游戏暂停（timeScale = 0）时也照常计时
            while (_autoSaveEnabled)
            {
                yield return new WaitForSecondsRealtime(interval);

                if (!_autoSaveEnabled)
                    yield break;

                // 单次失败不能弄死协程（健壮性保障）
                try
                {
                    TriggerAutoSave();
                }
                catch (Exception e)
                {
                    global::PirateCrew.Core.Log.Warn("[SaveManager] 自动存档异常: " + e);
                }
            }
        }

        // ------------------------------------------------------------------
        // _meta.json 读写
        // ------------------------------------------------------------------

        List<SlotMeta> LoadMetaSlots()
        {
            SaveMetaData meta = Io.LoadMeta();
            if (meta == null || meta.Slots == null)
                return new List<SlotMeta>();

            return meta.Slots.FindAll(m => m != null);
        }

        void UpsertMetaSlot(int slot, string displayName, long timestamp)
        {
            try
            {
                SaveMetaData meta = Io.LoadMeta() ?? new SaveMetaData();
                if (meta.Slots == null)
                    meta.Slots = new List<SlotMeta>();

                SlotMeta existing = meta.Slots.Find(m => m != null && m.Slot == slot);
                if (existing == null)
                {
                    meta.Slots.Add(new SlotMeta
                    {
                        Slot = slot,
                        DisplayName = displayName,
                        Timestamp = timestamp
                    });
                }
                else
                {
                    existing.DisplayName = displayName;
                    existing.Timestamp = timestamp;
                }

                Io.SaveMeta(meta);
            }
            catch (Exception e)
            {
                // 元数据写失败不影响存档本体，只记录
                global::PirateCrew.Core.Log.Warn("[SaveManager] 更新存档元数据失败: " + e);
            }
        }

        void RemoveMetaSlot(int slot)
        {
            try
            {
                SaveMetaData meta = Io.LoadMeta();
                if (meta == null || meta.Slots == null)
                    return;

                int removed = meta.Slots.RemoveAll(m => m != null && m.Slot == slot);
                if (removed > 0)
                    Io.SaveMeta(meta);
            }
            catch (Exception e)
            {
                global::PirateCrew.Core.Log.Warn("[SaveManager] 删除存档元数据失败: " + e);
            }
        }

        static long NowUnixSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        static string GameVersion()
        {
            string version = Application.version;
            return string.IsNullOrEmpty(version) ? "0.0.0" : version;
        }
    }
}
