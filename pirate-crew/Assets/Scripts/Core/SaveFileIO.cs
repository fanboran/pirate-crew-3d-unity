using System;
using System.IO;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 存档文件 IO 纯 C# 层（无 MonoBehaviour，可脱离 Unity 生命周期单独测试）。
    ///
    /// 【健壮性契约】源自 external/core-reference/savesystem-shapedbyrain 的 FileDataHandler，
    ///   转正一步升级为同卷原子操作（Replace/Move 取代非原子的 Copy）：
    ///   写入：写 .tmp → 回读校验 → 转正（目标已存在则 Replace、旧档自动进 .bak；
    ///   不存在则 Move 同卷重命名）；
    ///   读取：解析失败时从 .bak 回滚一次（allowRestoreFromBackup 防无限递归）。
    ///   路径一律 Path.Combine，跨平台。
    ///
    /// 【文件布局】&lt;root&gt;/slot_{n}.json、&lt;root&gt;/_meta.json（另有 .bak / .tmp 同名伴随文件）。
    /// </summary>
    public sealed class SaveFileIO
    {
        /// <summary>元数据文件名。</summary>
        public const string MetaFileName = "_meta.json";

        /// <summary>备份文件后缀。</summary>
        public const string BackupExtension = ".bak";

        /// <summary>临时文件后缀（写入中转，写完即删）。</summary>
        public const string TempExtension = ".tmp";

        readonly string _rootPath;

        /// <summary>
        /// 槽位号合法域下界（合法域 = [0, +∞)，无上限）。
        /// 依据：本工程实际使用的槽位号全部非负——0 = 自动存档（<see cref="SaveManager.AutoSaveSlot"/>），
        /// 1 起手动档（如 <c>CampaignApi.ProgressSlot = 1</c>），9 = 设置档
        /// （<c>AudioSettingsStore.SettingsSlot</c> / <c>VideoSettingsStore.SettingsSlot</c>）；
        /// 负号拼出的 <c>slot_-1.json</c> 是没有任何读写方会碰的孤儿文件名。
        /// </summary>
        public const int MinSlot = 0;

        public SaveFileIO(string rootPath)
        {
            // null / 空串拒绝在构造期：Path.Combine(null/空串, …) 会静默回落进程当前目录（CWD），
            // 编辑器、批处理、无头验证台的 CWD 各不相同——存档会散落到随机工作区，
            // 且只有玩家发现丢档时才现形。宁可构造即抛，不留"看似可用"的实例。
            if (rootPath == null)
                throw new ArgumentNullException(nameof(rootPath), "存档根目录不能为 null。");
            if (rootPath.Length == 0)
                throw new ArgumentException("存档根目录不能为空串（会静默落进程当前目录）。", nameof(rootPath));

            _rootPath = rootPath;
        }

        /// <summary>槽位文件路径：slot_{n}.json。槽位号界外（负数）即抛，不做静默兜底。</summary>
        public string GetSlotPath(int slot)
        {
            if (slot < MinSlot)
                throw new ArgumentOutOfRangeException(nameof(slot), slot,
                    "槽位号必须 ≥ " + MinSlot + "（0 = 自动存档，1 起手动/设置档）；"
                    + "负槽位号只会拼出无人读写的孤儿文件名，宁抛不静默。");

            return Path.Combine(_rootPath, "slot_" + slot + ".json");
        }

        /// <summary>元数据文件路径：_meta.json。</summary>
        public string GetMetaPath()
        {
            return Path.Combine(_rootPath, MetaFileName);
        }

        /// <summary>确保根目录存在（幂等）。</summary>
        public void EnsureDirectory()
        {
            if (string.IsNullOrEmpty(_rootPath))
                return;

            Directory.CreateDirectory(_rootPath);
        }

        /// <summary>槽位文件是否存在。</summary>
        public bool SlotExists(int slot)
        {
            return File.Exists(GetSlotPath(slot));
        }

        /// <summary>删除槽位文件（连带 .bak / .tmp）；文件不存在返回 false。</summary>
        public bool DeleteSlot(int slot)
        {
            string path = GetSlotPath(slot);
            if (!File.Exists(path))
                return false;

            try
            {
                File.Delete(path);
                SafeDelete(path + BackupExtension);
                SafeDelete(path + TempExtension);
                return true;
            }
            catch (Exception e)
            {
                global::PirateCrew.Core.Log.Error("[SaveFileIO] 删除存档失败: " + path + "\n" + e);
                return false;
            }
        }

        /// <summary>读取槽位（失败时允许从 .bak 回滚一次）；不存在或彻底失败返回 null。</summary>
        public SaveData LoadSlot(int slot)
        {
            return LoadSlot(slot, true);
        }

        /// <summary>
        /// 读取槽位。
        /// <paramref name="allowRestoreFromBackup"/> 为 true 时，解析失败会尝试从 .bak 回滚；
        /// 回滚后以 false 递归重读，避免 .bak 同样损坏导致无限递归（FileDataHandler 同款防递归）。
        /// </summary>
        public SaveData LoadSlot(int slot, bool allowRestoreFromBackup)
        {
            string path = GetSlotPath(slot);
            if (!File.Exists(path))
                return null;

            try
            {
                string text = File.ReadAllText(path);
                SaveData data = Deserialize<SaveData>(text);
                if (data == null)
                    throw new InvalidDataException("存档解析结果为空");

                return data;
            }
            catch (Exception e)
            {
                if (!allowRestoreFromBackup)
                {
                    global::PirateCrew.Core.Log.Error("[SaveFileIO] 存档损坏且无法从 .bak 恢复: " + path + "\n" + e);
                    return null;
                }

                global::PirateCrew.Core.Log.Warn("[SaveFileIO] 存档损坏，尝试从 .bak 回滚: " + path + "\n" + e);
                if (AttemptRollback(path))
                    return LoadSlot(slot, false);

                return null;
            }
        }

        /// <summary>写入槽位（.tmp → 回读校验 → 存在则 Replace 旧档进 .bak / 不存在则 Move 转正）；成功返回 true。</summary>
        public bool SaveSlot(int slot, SaveData data)
        {
            if (data == null)
            {
                global::PirateCrew.Core.Log.Error("[SaveFileIO] SaveSlot 收到空数据，slot=" + slot);
                return false;
            }

            return WriteJsonAtomic(GetSlotPath(slot), data);
        }

        /// <summary>读取 _meta.json；不存在或彻底损坏（含 .bak 回滚失败）返回 null（调用方按空处理）。</summary>
        public SaveMetaData LoadMeta()
        {
            return LoadMeta(true);
        }

        /// <summary>
        /// 读取 _meta.json，与 <see cref="LoadSlot(int,bool)"/> 同款健壮性：
        /// <paramref name="allowRestoreFromBackup"/> 为 true 时解析失败先从 .bak 回滚一次，
        /// 回滚后以 false 重读（.bak 同样损坏则不递归）。
        ///
        /// 【为什么元数据也要回滚】_meta.json 是**全部槽位**元数据的唯一载体（列表页/槽位名的
        /// 数据源，见 SaveManager 的约定），一次损坏不该等于所有槽位元数据清零——
        /// 能从上一版恢复就恢复，回滚也失败才按空处理。
        /// </summary>
        public SaveMetaData LoadMeta(bool allowRestoreFromBackup)
        {
            string path = GetMetaPath();
            if (!File.Exists(path))
                return null;

            try
            {
                SaveMetaData meta = Deserialize<SaveMetaData>(File.ReadAllText(path));
                if (meta == null)
                    throw new InvalidDataException("元数据解析结果为空");

                return meta;
            }
            catch (Exception e)
            {
                if (!allowRestoreFromBackup)
                {
                    global::PirateCrew.Core.Log.Error("[SaveFileIO] 元数据损坏且无法从 .bak 恢复: " + path + "\n" + e);
                    return null;
                }

                global::PirateCrew.Core.Log.Warn("[SaveFileIO] 元数据损坏，尝试从 .bak 回滚: " + path + "\n" + e);
                if (AttemptRollback(path))
                    return LoadMeta(false);

                return null;
            }
        }

        /// <summary>写入 _meta.json（与槽位同款 .tmp/.bak 健壮性）。</summary>
        public bool SaveMeta(SaveMetaData meta)
        {
            if (meta == null)
                return false;

            return WriteJsonAtomic(GetMetaPath(), meta);
        }

        // ------------------------------------------------------------------
        // 内部实现
        // ------------------------------------------------------------------

        static T Deserialize<T>(string text) where T : class
        {
            if (string.IsNullOrEmpty(text))
                return null;

            return JsonUtility.FromJson<T>(text);
        }

        /// <summary>
        /// 原子写入：写 .tmp → 回读校验 → 转正。
        /// 转正按目标是否存在二选一：已存在走 File.Replace（原子替换，旧档自动转 .bak），
        /// 不存在（首写）走 File.Move（同卷重命名）——两者都是原子操作，崩溃也绝不留下半截正式文件。
        /// 任一步失败删除 .tmp 并返回 false。
        /// </summary>
        bool WriteJsonAtomic<T>(string path, T data) where T : class
        {
            string tempPath = path + TempExtension;
            string backupPath = path + BackupExtension;

            try
            {
                EnsureDirectory();

                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(tempPath, json);

                // 回读校验：写出去的必须能读回来
                if (Deserialize<T>(File.ReadAllText(tempPath)) == null)
                {
                    SafeDelete(tempPath);
                    global::PirateCrew.Core.Log.Error("[SaveFileIO] 回读校验失败，放弃写入: " + path);
                    return false;
                }

                // 转正不用 File.Copy：拷贝中途崩溃会留半截正式档，这里走同卷原子操作——
                // 目标已存在用 File.Replace 原子换入，旧档自动转 .bak；
                // 首次写入（无目标）用 File.Move 同卷重命名，没有内容拷贝窗口。
                if (File.Exists(path))
                {
                    // Unix 实现的 Replace 遇 .bak 已存在会抛异常（Windows 则直接覆盖），
                    // 先清掉旧 .bak 保证跨平台行为一致。
                    SafeDelete(backupPath);
                    File.Replace(tempPath, path, backupPath);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                return true;
            }
            catch (Exception e)
            {
                global::PirateCrew.Core.Log.Error("[SaveFileIO] 写入存档失败: " + path + "\n" + e);
                SafeDelete(tempPath);
                return false;
            }
        }

        /// <summary>从 .bak 回滚覆盖正式文件；无 .bak 或拷贝失败返回 false。</summary>
        bool AttemptRollback(string path)
        {
            string backupPath = path + BackupExtension;
            try
            {
                if (!File.Exists(backupPath))
                {
                    global::PirateCrew.Core.Log.Error("[SaveFileIO] 没有可回滚的 .bak 文件: " + backupPath);
                    return false;
                }

                File.Copy(backupPath, path, true);
                global::PirateCrew.Core.Log.Warn("[SaveFileIO] 已从 .bak 回滚: " + backupPath);
                return true;
            }
            catch (Exception e)
            {
                global::PirateCrew.Core.Log.Error("[SaveFileIO] 回滚失败: " + backupPath + "\n" + e);
                return false;
            }
        }

        static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                global::PirateCrew.Core.Log.Warn("[SaveFileIO] 删除临时文件失败: " + path + "\n" + e);
            }
        }
    }
}
