using System;
using System.Collections.Generic;

namespace PirateCrew.Core
{
    /// <summary>
    /// 通用键值条目（本工程存档的键值容器）。
    ///
    /// JsonUtility 不支持 Dictionary，故用 [Serializable] 条目列表承载任意键值数据；
    /// 读写统一走 <see cref="SaveData.GetData"/> / <see cref="SaveData.SetData"/>。
    /// </summary>
    [Serializable]
    public class StringKVEntry
    {
        /// <summary>键。</summary>
        public string Key;

        /// <summary>值。统一存字符串，由调用方按需解析。</summary>
        public string Value;
    }

    /// <summary>
    /// 单个存档槽位的数据容器。
    ///
    /// 【字段即 JSON 键】JsonUtility 不支持字段重命名特性，字段名直接作为 JSON 键，
    /// 因此本类字段沿用 C# PascalCase（存档格式自成一套）。
    /// </summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>写入时的游戏版本（取 Application.version）。</summary>
        public string Version;

        /// <summary>写入时的 Unix 时间戳（秒）。JsonUtility 不支持 DateTime，故存 long。</summary>
        public long Timestamp;

        /// <summary>存档显示名称。</summary>
        public string DisplayName;

        /// <summary>通用数据容器。</summary>
        public List<StringKVEntry> Data = new List<StringKVEntry>();

        /// <summary>读取键值；键不存在返回 defaultValue。</summary>
        public string GetData(string key, string defaultValue = null)
        {
            if (Data == null || string.IsNullOrEmpty(key))
                return defaultValue;

            for (int i = 0; i < Data.Count; i++)
            {
                StringKVEntry entry = Data[i];
                if (entry != null && entry.Key == key)
                    return entry.Value;
            }

            return defaultValue;
        }

        /// <summary>写入键值；键已存在则覆盖。</summary>
        public void SetData(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (Data == null)
                Data = new List<StringKVEntry>();

            for (int i = 0; i < Data.Count; i++)
            {
                StringKVEntry entry = Data[i];
                if (entry != null && entry.Key == key)
                {
                    entry.Value = value;
                    return;
                }
            }

            Data.Add(new StringKVEntry { Key = key, Value = value });
        }

        /// <summary>是否包含指定键。</summary>
        public bool HasData(string key)
        {
            if (Data == null || string.IsNullOrEmpty(key))
                return false;

            for (int i = 0; i < Data.Count; i++)
            {
                if (Data[i] != null && Data[i].Key == key)
                    return true;
            }

            return false;
        }

        /// <summary>移除指定键；存在并移除成功返回 true。</summary>
        public bool RemoveData(string key)
        {
            if (Data == null || string.IsNullOrEmpty(key))
                return false;

            for (int i = 0; i < Data.Count; i++)
            {
                if (Data[i] != null && Data[i].Key == key)
                {
                    Data.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 存档槽位元数据（_meta.json 中每个槽位一条：display_name、timestamp）。
    /// </summary>
    [Serializable]
    public class SlotMeta
    {
        /// <summary>槽位号。</summary>
        public int Slot;

        /// <summary>存档显示名称。</summary>
        public string DisplayName;

        /// <summary>最后写入的 Unix 时间戳（秒）。</summary>
        public long Timestamp;
    }

    /// <summary>
    /// _meta.json 的根对象。JsonUtility 不支持字典，故退化为列表。
    /// </summary>
    [Serializable]
    public class SaveMetaData
    {
        /// <summary>全部槽位的元数据条目。</summary>
        public List<SlotMeta> Slots = new List<SlotMeta>();
    }
}
