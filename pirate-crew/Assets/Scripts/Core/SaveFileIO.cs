using System;
using System.IO;
using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 存档文件 IO 纯 C# 层（无 MonoBehaviour，可脱离 Unity 生命周期单独测试）。
    ///
    /// 【健壮性契约】照搬 external/core-reference/savesystem-shapedbyrain 的 FileDataHandler：
    ///   写入：写 .tmp → 回读校验 → 旧正式文件转 .bak → .tmp 转正；
    ///   读取：解析失败时从 .bak 回滚一次（allowRestoreFromBackup 防无限递归）。
    ///   路径一律 Path.Combine，跨平台。
    ///
    /// 【文件布局】&lt;root&gt;/slot_{n}.json、&lt;root&gt;/_meta.json（另有 .bak / .tmp 同名伴随文件）。
    /// </summary>
    public sealed class SaveFileIO
    {
        /// <summary>元数据文件名（对应 Godot META_FILE）。</summary>
        public const string MetaFileName = "_meta.json";

        /// <summary>备份文件后缀。</summary>
        public const string BackupExtension = ".bak";

        /// <summary>临时文件后缀（写入中转，写完即删）。</summary>
        public const string TempExtension = ".tmp";

        readonly string _rootPath;

        public SaveFileIO(string rootPath)
        {
            _rootPath = rootPath ?? string.Empty;
        }

        /// <summary>存档根目录。</summary>
        public string RootPath => _rootPath;

        /// <summary>槽位文件路径：slot_{n}.json。</summary>
        public string GetSlotPath(int slot)
        {
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

        /// <summary>槽位文件是否存在（对应 Godot slot_exists）。</summary>
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
                Debug.LogError("[SaveFileIO] 删除存档失败: " + path + "\n" + e);
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
                    Debug.LogError("[SaveFileIO] 存档损坏且无法从 .bak 恢复: " + path + "\n" + e);
                    return null;
                }

                global::PirateCrew.Core.Log.Warn("[SaveFileIO] 存档损坏，尝试从 .bak 回滚: " + path + "\n" + e);
                if (AttemptRollback(path))
                    return LoadSlot(slot, false);

                return null;
            }
        }

        /// <summary>写入槽位（.tmp → 回读校验 → 旧文件转 .bak → 转正）；成功返回 true。</summary>
        public bool SaveSlot(int slot, SaveData data)
        {
            if (data == null)
            {
                Debug.LogError("[SaveFileIO] SaveSlot 收到空数据，slot=" + slot);
                return false;
            }

            return WriteJsonAtomic(GetSlotPath(slot), data);
        }

        /// <summary>读取 _meta.json；不存在或损坏返回 null（调用方按空处理）。</summary>
        public SaveMetaData LoadMeta()
        {
            string path = GetMetaPath();
            if (!File.Exists(path))
                return null;

            try
            {
                return Deserialize<SaveMetaData>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                global::PirateCrew.Core.Log.Warn("[SaveFileIO] 元数据读取失败，按空处理: " + path + "\n" + e);
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
        /// 原子写入：写 .tmp → 回读校验 → 旧正式文件转 .bak → .tmp 转正。
        /// 任一步失败都删除 .tmp 并返回 false，绝不留下半截正式文件。
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
                    Debug.LogError("[SaveFileIO] 回读校验失败，放弃写入: " + path);
                    return false;
                }

                // 旧正式文件转 .bak（首次写入无旧文件则跳过）
                if (File.Exists(path))
                    File.Copy(path, backupPath, true);

                // .tmp 转正
                File.Copy(tempPath, path, true);
                SafeDelete(tempPath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveFileIO] 写入存档失败: " + path + "\n" + e);
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
                    Debug.LogError("[SaveFileIO] 没有可回滚的 .bak 文件: " + backupPath);
                    return false;
                }

                File.Copy(backupPath, path, true);
                global::PirateCrew.Core.Log.Warn("[SaveFileIO] 已从 .bak 回滚: " + backupPath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveFileIO] 回滚失败: " + backupPath + "\n" + e);
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
