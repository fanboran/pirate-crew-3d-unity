using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 存档/读档服务（翻译自 Godot <c>core/autoload/save_manager.gd</c>，
    /// 并合并 <c>data_manager.gd</c> 的键值持久化职责——Godot 版两者职责重叠）。
    ///
    /// 【架构定位】
    ///   全局服务，由 Bootstrapper 创建并随 Services 对象 DontDestroyOnLoad（同 SceneLoader）。
    ///   Core 层不依赖玩法模块：自动存档的数据由玩法模块通过 <see cref="AutoSaveDataProvider"/> 注入。
    ///
    /// 【核心职责】
    ///   1. 多槽位存档：SaveToSlot / LoadFromSlot / DeleteSlot / ListSlots / SlotExists
    ///   2. 槽位元数据：独立 _meta.json；GetSlotMeta / ListSlots 以元数据为唯一来源、不碰槽位文件
    ///      （Unity 版定案设计；Godot 原版这两个方法是扫描/读取槽位文件的）
    ///   3. 自动存档：EnableAutoSave 协程定时 + TriggerAutoSave 手动触发 + 退出兜底
    ///   4. 事件通知：本地 event + 转发 EventBus（save_completed / load_completed / auto_save_triggered）
    ///
    /// 【文件布局】&lt;SaveRootPath&gt;/slot_{n}.json、&lt;SaveRootPath&gt;/_meta.json（+ .bak / .tmp）
    ///   默认 SaveRootPath = Application.persistentDataPath/saves；测试或特殊平台可注入覆盖。
    ///
    /// 【与 data_manager 的关系】Godot 的 data_get/data_set/save_data/load_data 被合并为
    ///   <see cref="SaveData.GetData"/> / <see cref="SaveData.SetData"/>：键值随档写入，不再单开全局文件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        /// <summary>
        /// 自动存档占用的槽位号（对应 Godot 的 AUTOSAVE_SLOT = "autosave"）。
        /// 手动存档请从 1 起，避免覆盖自动存档。
        /// </summary>
        public const int AutoSaveSlot = 0;

        /// <summary>自动存档默认间隔（秒），对应 Godot _auto_save_interval 默认 60。</summary>
        public const float DefaultAutoSaveInterval = 60f;

        /// <summary>自动存档最短间隔，防止传入 0/负值时协程每帧空转。</summary>
        const float MinAutoSaveInterval = 1f;

        [SerializeField] string savesFolder = "saves";

        string _saveRootPath;
        SaveFileIO _io;
        Coroutine _autoSaveRoutine;
        bool _autoSaveEnabled;
        float _autoSaveInterval = DefaultAutoSaveInterval;

        /// <summary>全局访问入口（由 Bootstrapper 创建本组件后可用）。</summary>
        public static SaveManager Instance { get; private set; }

        /// <summary>存档成功（等价 Godot signal save_completed）。</summary>
        public event Action<int> SaveCompleted;

        /// <summary>读档成功（等价 Godot signal load_completed）。</summary>
        public event Action<int> LoadCompleted;

        /// <summary>自动存档触发（等价 Godot signal auto_save_triggered）。</summary>
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
                Debug.LogWarning("[SaveManager] 已存在实例，销毁重复对象: " + name);
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
                Debug.LogError("[SaveManager] 创建存档目录失败: " + e);
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
        // 槽位 API（对应 Godot save_manager.gd 的公开方法）
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
                Debug.LogError("[SaveManager] SaveToSlot 收到空数据，slot=" + slot);
                return false;
            }

            data.Version = GameVersion();
            data.Timestamp = NowUnixSeconds();
            data.DisplayName = string.IsNullOrEmpty(displayName) ? ("槽位 " + slot) : displayName;

            if (!Io.SaveSlot(slot, data))
                return false;

            UpsertMetaSlot(slot, data.DisplayName, data.Timestamp);

            SaveCompleted?.Invoke(slot);
            EventBus.Publish("save_completed", slot);
            return true;
        }

        /// <summary>
        /// 从指定槽位读取（对应 <c>load_from_slot</c>）。
        /// 失败或不存在返回 null（Godot 版返回空字典，C# 侧用 null 表达同一语义）。
        /// </summary>
        public SaveData LoadFromSlot(int slot)
        {
            SaveData data = Io.LoadSlot(slot);
            if (data == null)
            {
                Debug.LogWarning("[SaveManager] 读档失败或槽位不存在: " + slot);
                return null;
            }

            LoadCompleted?.Invoke(slot);
            EventBus.Publish("load_completed", slot);
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
        /// 返回是否真的删掉了文件（对应 Godot 的 bool 返回值）。
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
        // 自动存档（对应 Godot 的 Timer 定时器）
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
                Debug.LogWarning("[SaveManager] 未注册 AutoSaveDataProvider，跳过自动存档");
                return false;
            }

            SaveData data;
            try
            {
                data = AutoSaveDataProvider();
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveManager] 自动存档数据源异常: " + e);
                return false;
            }

            if (data == null)
            {
                Debug.LogWarning("[SaveManager] 自动存档数据源返回 null，跳过本次");
                return false;
            }

            // 对应 Godot：先发信号，再写盘
            AutoSaveTriggered?.Invoke(AutoSaveSlot);
            EventBus.Publish("auto_save_triggered", AutoSaveSlot);

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

                // 单次失败不能弄死协程（对应 Godot 版没做、这里补上的健壮性）
                try
                {
                    TriggerAutoSave();
                }
                catch (Exception e)
                {
                    Debug.LogError("[SaveManager] 自动存档异常: " + e);
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
                Debug.LogError("[SaveManager] 更新存档元数据失败: " + e);
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
                Debug.LogError("[SaveManager] 删除存档元数据失败: " + e);
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
