using System;
using System.Collections.Generic;
using System.Text;
using PirateCrew.Core;

namespace PirateCrew.CrewManagement
{
    /// <summary>
    /// 船员管理模块的存档编解码（纯 C#，走 <see cref="SaveData"/> 现有的字符串键值 API）。
    ///
    /// 【为什么这么写】Core 的 <see cref="SaveData"/> 只提供 <c>SetData/GetData</c>（字符串键值）与
    ///   <c>List&lt;StringKVEntry&gt;</c>；JsonUtility 不支持 Dictionary，而 M3 的数据量很小，
    ///   故用**分隔符文本**表达：id 列表 <c>"sailor|gunner"</c>、经验 <c>"sailor:250|gunner:100"</c>。
    ///   好处是编解码全部是纯 C#（无头验证台可断言），且不需要改动 Core 一行。
    ///
    /// 【安全性】键值两侧只允许出现名录里的 id（<c>[a-zA-Z]</c>）与整数，
    ///   分隔符 <c>|</c> / <c>:</c> 不会出现在 id 里，故无需转义；
    ///   解码时对空段、非法段一律跳过（不抛异常），保证坏档不会让游戏起不来。
    /// </summary>
    public static class CrewManagementSaveCodec
    {
        /// <summary>已拥有船员列表的存档键。</summary>
        public const string UnlockedKey = "crew_roster_unlocked";

        /// <summary>编成阵容的存档键。</summary>
        public const string ActiveKey = "crew_roster_active";

        /// <summary>船员经验的存档键。</summary>
        public const string XpKey = "crew_xp";

        /// <summary>id 列表分隔符。</summary>
        public const char IdSeparator = '|';

        /// <summary>经验条目的「id:经验」分隔符。</summary>
        public const char XpSeparator = ':';

        /// <summary>
        /// 把名册与经验写进存档数据（三个键整体覆盖）。
        /// </summary>
        public static void Write(SaveData data, Roster roster, CrewProgression progression)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (roster == null)
                throw new ArgumentNullException(nameof(roster));

            data.SetData(UnlockedKey, JoinIds(roster.UnlockedCopy()));
            data.SetData(ActiveKey, JoinIds(roster.ActiveCopy()));
            data.SetData(XpKey, JoinXp(progression));
        }

        /// <summary>
        /// 从存档数据恢复名册与经验。键缺失时保持传入对象的现状（首次开档不乱动默认值）。
        ///
        /// 【恢复顺序】先重放「已拥有」，再重放「编成」，最后经验——
        ///   <see cref="Roster.SetActive"/> 要求阵容成员已拥有，顺序颠倒会把阵容读丢。
        /// </summary>
        public static void Read(SaveData data, Roster roster, CrewProgression progression)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (roster == null)
                throw new ArgumentNullException(nameof(roster));

            string unlockedRaw = data.GetData(UnlockedKey);
            // 空串视为「没有可用数据」（正常档至少会有初始船员），保持现状而不是把名册清空。
            if (!string.IsNullOrEmpty(unlockedRaw))
            {
                roster.Reset();
                List<string> unlocked = SplitIds(unlockedRaw);
                for (int i = 0; i < unlocked.Count; i++)
                    roster.Recruit(unlocked[i]);

                // Recruit 会在空阵容时自动补人；这里按存档覆盖回去。
                string activeRaw = data.GetData(ActiveKey);
                if (activeRaw != null)
                    roster.SetActive(SplitIds(activeRaw));
            }

            string xpRaw = data.GetData(XpKey);
            if (xpRaw != null && progression != null)
            {
                progression.Reset();
                ReadXp(xpRaw, progression);
            }
        }

        /// <summary>把 id 列表编码成存档字符串（空列表 → 空串）。</summary>
        public static string JoinIds(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.IsNullOrEmpty(ids[i]))
                    continue;

                if (builder.Length > 0)
                    builder.Append(IdSeparator);
                builder.Append(ids[i]);
            }

            return builder.ToString();
        }

        /// <summary>把存档字符串解码成 id 列表（空串 / null → 空列表）。</summary>
        public static List<string> SplitIds(string raw)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(raw))
                return ids;

            string[] parts = raw.Split(IdSeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                string id = parts[i].Trim();
                if (id.Length > 0 && !ids.Contains(id))
                    ids.Add(id);
            }

            return ids;
        }

        /// <summary>把经验账本编码成 <c>"sailor:250|gunner:100"</c>；无数据 → 空串。</summary>
        public static string JoinXp(CrewProgression progression)
        {
            if (progression == null)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (string crewId in progression.CrewIds)
            {
                if (string.IsNullOrEmpty(crewId))
                    continue;

                if (builder.Length > 0)
                    builder.Append(IdSeparator);
                builder.Append(crewId).Append(XpSeparator).Append(progression.GetXp(crewId));
            }

            return builder.ToString();
        }

        /// <summary>解码经验字符串并写进账本；空串 / null 为 no-op。</summary>
        public static void ReadXp(string raw, CrewProgression progression)
        {
            if (progression == null || string.IsNullOrEmpty(raw))
                return;

            string[] parts = raw.Split(IdSeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                    continue;

                int split = part.LastIndexOf(XpSeparator);
                if (split <= 0)
                    continue;

                string crewId = part.Substring(0, split).Trim();
                string xpText = part.Substring(split + 1).Trim();
                if (crewId.Length == 0 || !int.TryParse(xpText, out int xp))
                    continue;

                progression.SetXp(crewId, xp);
            }
        }
    }
}
