using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 存档/读档服务（含键值持久化职责）。
    ///
    /// 【架构定位】
    ///   全局服务，由 Bootstrapper 创建并随 Services 对象 DontDestroyOnLoad（同 SceneLoader）。
    ///   Core 层不依赖玩法模块：玩法数据（战役进度、设置）由玩法模块持有并传入槽位 API 落盘。
    ///
    /// 【核心职责】
    ///   1. 多槽位存档：SaveToSlot / LoadFromSlot / DeleteSlot / ListSlots / SlotExists
    ///   2. 槽位元数据：独立 _meta.json；GetSlotMeta / ListSlots 以元数据为唯一来源、不碰槽位文件
    ///      （本工程定案：以元数据为唯一来源，不扫描/读取槽位文件。唯一例外见 ListSlots 的
    ///      「有档无 meta」兜底——只扫文件名、不读内容）
    ///   3. 事件通知：本地 event + 转发 EventBus 频道（SaveEvents.SaveCompleted / LoadCompleted）
    ///   4. 版本门卫：读档时校验档内 Version，未来版本的档拒绝读取；旧档经
    ///      <see cref="MigrationSteps"/> 迁移管线（当前为空表，见该成员注释）
    ///
    /// 【存档写入路径】只有两条：手动存档（SaveToSlot）与战役结算落盘（CampaignApi 战后写进度，
    ///   内部同样走 SaveToSlot）；本类不含定时快照。
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
        [SerializeField] string savesFolder = "saves";

        string _saveRootPath;
        SaveFileIO _io;

        /// <summary>
        /// 全局访问入口（由 Bootstrapper 创建本组件后可用）。
        /// 【定位】与 <see cref="Services"/> 注册表双轨并存：本属性是**便捷过渡入口**，
        /// 权威定位器是 <see cref="Services"/>（Bootstrapper 已把本组件 Register 进注册表）——
        /// 新代码优先经 Services 解析，本属性仅为存量调用点保留。
        /// </summary>
        public static SaveManager Instance { get; private set; }

        /// <summary>存档成功。</summary>
        public event Action<int> SaveCompleted;

        /// <summary>读档成功。</summary>
        public event Action<int> LoadCompleted;

        /// <summary>
        /// 读档迁移注册表（版本迁移钩子）。
        ///
        /// 【用法】读档成功（含版本校验通过）后，数据会按注册顺序依次经过每个
        /// <c>Func&lt;SaveData, SaveData&gt;</c>：函数对传入档做升级变换并返回同一（或新）实例；
        /// 返回 null 视为迁移失败，读档整体拒绝（Log.Error + 返回 null，不吐半截档）。
        ///
        /// 【定位——预演设施】当前**无野外旧档**（存档系统上线即无条件写 Version，且尚未发过
        /// 任何带旧版本存档的正式版），本表恒为空、管线是 no-op。它先行立桩的理由：等真出现
        /// 旧档时只需往表里挂函数，读侧结构不用再动。未来挂真实迁移函数时的约定：
        /// ① 按版本从旧到新的顺序注册；② 函数自己读 <see cref="SaveData.Version"/> 判断
        /// 本步迁移是否适用于该档（注册表只保证顺序执行，不做版本区间过滤）。
        ///
        /// 【为什么当前版本档也过管线】管线收口在版本判别之后、对所有成功读取统一生效——
        /// 这样"管线是否通"用当前版本档即可测试，不必造旧档；no-op 函数对当前档零影响。
        /// </summary>
        public static readonly List<Func<SaveData, SaveData>> MigrationSteps =
            new List<Func<SaveData, SaveData>>();

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
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------
        // 槽位 API
        // ------------------------------------------------------------------

        /// <summary>
        /// 保存数据到指定槽位（对应 <c>save_to_slot</c>）。
        /// 会覆盖 data 的 Version / Timestamp / DisplayName，并更新 _meta.json。
        /// </summary>
        /// <param name="slot">槽位号（非负；当前占用：1 = 战役进度、9 = 设置档，0 无占用方）。</param>
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

        /// <summary>
        /// LoadFromSlot / TryLoadSlotQuiet 共用的读取体（不含任何事件通知）：
        /// 文件层读取（含 .bak 回滚）→ 版本校验 → <see cref="MigrationSteps"/> 迁移管线。
        /// 版本不受支持或迁移失败一律 Log.Error 留痕并返回 null——绝不把不认识的档
        /// 当旧档静默错读。
        /// </summary>
        SaveData LoadSlotCore(int slot)
        {
            SaveData data = Io.LoadSlot(slot);
            if (data == null)
            {
                global::PirateCrew.Core.Log.Warn("[SaveManager] 读档失败或槽位不存在: " + slot);
                return null;
            }

            string rejection = VersionIncompatibilityReason(data.Version);
            if (rejection != null)
            {
                global::PirateCrew.Core.Log.Error(
                    "[SaveManager] 槽位 " + slot + " 的存档版本不受支持，拒绝读取: " + rejection);
                return null;
            }

            for (int i = 0; i < MigrationSteps.Count; i++)
            {
                data = MigrationSteps[i](data);
                if (data == null)
                {
                    global::PirateCrew.Core.Log.Error(
                        "[SaveManager] 槽位 " + slot + " 读档迁移失败（迁移步骤 " + i + " 返回 null），拒绝读取");
                    return null;
                }
            }

            return data;
        }

        /// <summary>
        /// 版本兼容性判别：档内 Version 与当前版本比较。
        /// 相同（字符串相等即短路，同一程序写的档必然命中，不依赖解析）或**更旧** → 兼容
        /// （旧档交 <see cref="MigrationSteps"/> 迁移）；**更高**（未来版本的档）或无法比较
        /// → 返回拒绝原因。空 Version 同样拒绝：写侧（SaveToSlot）无条件写 Version，
        /// 空 Version 只能来自非本系统产出的文件。
        /// </summary>
        static string VersionIncompatibilityReason(string fileVersion)
        {
            string current = GameVersion();

            if (string.IsNullOrEmpty(fileVersion))
                return "档内无版本号（Version 为空）——不是本存档系统产出的文件";

            if (fileVersion == current)
                return null;

            int comparison = CompareVersions(fileVersion, current, out bool parsed);
            if (!parsed)
                return "档版本 \"" + fileVersion + "\" 无法与当前版本 \"" + current + "\" 比较版本号";

            return comparison > 0
                ? "档版本 " + fileVersion + " 高于当前版本 " + current + "（来自更新版本的存档）"
                : null; // 旧档：认识，交迁移管线
        }

        /// <summary>
        /// 版本号比较：按 '.' 分段取整数逐段比，段数不足补 0（"1.0" == "1.0.0"）。
        /// 任一段不是纯数字即视为不可解析（parsed = false）——解析不动的版本宁可拒绝，
        /// 不做"大概差不多"的猜测。
        /// </summary>
        static int CompareVersions(string a, string b, out bool parsed)
        {
            parsed = true;
            string[] partsA = a.Split('.');
            string[] partsB = b.Split('.');
            int count = Math.Max(partsA.Length, partsB.Length);

            for (int i = 0; i < count; i++)
            {
                int valueA = 0;
                int valueB = 0;
                if ((i < partsA.Length && !int.TryParse(partsA[i], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out valueA))
                    || (i < partsB.Length && !int.TryParse(partsB[i], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out valueB)))
                {
                    parsed = false;
                    return 0;
                }

                if (valueA != valueB)
                    return valueA.CompareTo(valueB);
            }

            return 0;
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
        /// 列出全部槽位元数据；按槽位号升序。
        /// 以 _meta.json 为唯一来源，外加「有档无 meta」兜底（见 <see cref="AppendOrphanSlotFallbacks"/>）。
        /// </summary>
        public List<SlotMeta> ListSlots()
        {
            List<SlotMeta> slots = LoadMetaSlots();
            AppendOrphanSlotFallbacks(slots);
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
        // _meta.json 读写
        // ------------------------------------------------------------------

        List<SlotMeta> LoadMetaSlots()
        {
            SaveMetaData meta = Io.LoadMeta();
            if (meta == null || meta.Slots == null)
                return new List<SlotMeta>();

            return meta.Slots.FindAll(m => m != null);
        }

        /// <summary>
        /// 「有档无 meta」兜底：槽位文件存在、_meta.json 里却没有对应行的槽位，
        /// 合成一个兜底行补进列表（显示名「槽位 N」= 写侧默认名，时间戳取文件最后写入时间）。
        ///
        /// 【兜底语义】_meta.json 是列表页唯一来源（本类定案），但它是**单点**：
        /// 一次损坏/误删就会静默隐藏全部槽位——列表页空掉比丢档更糟，玩家会误以为存档没了。
        /// 本兜底只扫目录里的文件名、不读取槽位内容，不改变元数据的读写路径；它只保证
        /// 孤儿档在列表页可见，随后由既有的读档 / 删除操作自然接管。
        /// </summary>
        void AppendOrphanSlotFallbacks(List<SlotMeta> slots)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(SaveRootPath, "slot_*.json");
            }
            catch (Exception)
            {
                return; // 目录不存在 / 不可达：按无兜底处理，读档路径自会报错
            }

            for (int i = 0; i < files.Length; i++)
            {
                int slot = ParseSlotFileName(files[i]);
                if (slot < 0 || slots.Exists(m => m.Slot == slot))
                    continue;

                long timestamp = 0;
                try
                {
                    timestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(files[i])).ToUnixTimeSeconds();
                }
                catch (Exception)
                {
                    // 取不到文件时间就留 0：兜底行仍可见，只是列表排序里排最前
                }

                slots.Add(new SlotMeta
                {
                    Slot = slot,
                    DisplayName = "槽位 " + slot,
                    Timestamp = timestamp,
                });
            }
        }

        /// <summary>从文件名解析槽位号（仅接受 slot_{非负整数}.json）；其余形态返回 -1。</summary>
        static int ParseSlotFileName(string path)
        {
            string name = Path.GetFileName(path);
            const string prefix = "slot_";
            const string suffix = ".json";
            if (!name.StartsWith(prefix, StringComparison.Ordinal)
                || !name.EndsWith(suffix, StringComparison.Ordinal))
                return -1;

            string digits = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            // NumberStyles.None：纯数字，连符号都不收——负槽位号本就是非法域
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int slot)
                ? slot
                : -1;
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
                // 元数据写失败不影响存档本体，但必须按错误留痕：定案是「元数据是列表页唯一
                // 来源」——写失败意味着这档从列表页消失（玩家视角 = 存档丢了），属于用户真实
                // 受损，Warn 级别在发布版会被编译剔除，受害者看不到任何痕迹。
                global::PirateCrew.Core.Log.Error("[SaveManager] 更新存档元数据失败: " + e);
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
