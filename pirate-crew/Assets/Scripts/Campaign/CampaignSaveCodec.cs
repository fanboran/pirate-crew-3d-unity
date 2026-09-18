using System;
using System.Collections.Generic;
using System.Text;
using PirateCrew.Core;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役进度的存档编解码（纯 C#，走 <see cref="SaveData"/> 现有的字符串键值 API）。
    ///
    /// 【格式】<c>campaign_level_stars = "wreck_hymn:3|atoll_ring:2"</c>（海图 id : 星级）。
///   一代旧档的 level_01 形式键会被 SetStars 静默丢弃（值域已变为海图 id 集合）。
    ///   与 <c>CrewManagementSaveCodec</c> 同一思路：数据量小、JsonUtility 不支持 Dictionary，
    ///   用分隔符文本表达，纯 C# 可无头断言，且不需要改 Core。
    ///   解码对空段/非法段一律跳过（坏档不影响启动）。
    /// </summary>
    public static class CampaignSaveCodec
    {
        /// <summary>关卡星级的存档键。</summary>
        public const string StarsKey = "campaign_level_stars";

        /// <summary>条目分隔符。</summary>
        public const char EntrySeparator = '|';

        /// <summary>「关卡 id : 星级」的分隔符。</summary>
        public const char StarSeparator = ':';

        /// <summary>把进度写进存档数据。</summary>
        public static void Write(SaveData data, CampaignProgress progress)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (progress == null)
                throw new ArgumentNullException(nameof(progress));

            data.SetData(StarsKey, Join(progress.Snapshot()));
        }

        /// <summary>从存档数据恢复进度（键缺失时保持现状）。</summary>
        public static void Read(SaveData data, CampaignProgress progress)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (progress == null)
                throw new ArgumentNullException(nameof(progress));

            string raw = data.GetData(StarsKey);
            if (raw == null)
                return;

            progress.Reset();
            ReadInto(raw, progress);
        }

        /// <summary>把「关卡 id → 星级」字典编码成存档字符串；空 → 空串。</summary>
        public static string Join(IReadOnlyDictionary<string, int> stars)
        {
            if (stars == null || stars.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in stars)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value <= 0)
                    continue;

                if (builder.Length > 0)
                    builder.Append(EntrySeparator);
                builder.Append(pair.Key).Append(StarSeparator).Append(pair.Value);
            }

            return builder.ToString();
        }

        /// <summary>解码存档字符串并写入进度；空串 / null 为 no-op。</summary>
        public static void ReadInto(string raw, CampaignProgress progress)
        {
            if (progress == null || string.IsNullOrEmpty(raw))
                return;

            string[] parts = raw.Split(EntrySeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                    continue;

                int split = part.LastIndexOf(StarSeparator);
                if (split <= 0)
                    continue;

                string levelId = part.Substring(0, split).Trim();
                string starText = part.Substring(split + 1).Trim();
                if (levelId.Length == 0 || !int.TryParse(starText, out int stars))
                    continue;

                progress.SetStars(levelId, stars);
            }
        }
    }
}
