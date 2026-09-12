using System;
using System.Collections.Generic;

namespace PirateCrew.CrewManagement
{
    /// <summary>
    /// 船员名册与编成（翻译自 Godot <c>modules/crew_management/scripts/roster.gd</c>，纯 C# 可无头测试）。
    ///
    /// 【对应关系】<c>_unlocked_crews</c> → <see cref="Unlocked"/>、<c>_active_roster</c> → <see cref="Active"/>、
    ///   <c>max_roster_size</c> → <see cref="MaxSize"/>、
    ///   <c>unlock_crew</c> → <see cref="Recruit"/>、<c>set_active_roster</c> → <see cref="SetActive"/>。
    ///
    /// 【对 Godot 版的补强（已在此显式标注，便于走查）】
    ///   1. <see cref="SetActive"/> 额外拒绝**重复 id**（Godot 未校验；重复会让结算重复发经验）。
    ///   2. <see cref="SetActive"/> 额外拒绝 null / 空串 id（Godot 会把它塞进列表）。
    ///   3. <see cref="Recruit"/> 额外校验 id 必须存在于 <see cref="CrewRosterCatalog"/>
    ///      （Godot 的 <c>unlock_crew</c> 接受任意字符串）。
    ///   其余语义（上限校验、必须已解锁、允许空阵容）与 Godot 版一致。
    /// </summary>
    public sealed class Roster
    {
        readonly List<string> _unlocked = new List<string>();
        readonly List<string> _active = new List<string>();

        /// <summary>编成上限。</summary>
        public int MaxSize { get; }

        /// <summary>
        /// 构造名册。
        /// </summary>
        /// <param name="maxSize">
        /// 编成上限；非正数回退 <see cref="CrewRosterCatalog.MaxRosterSize"/>。
        /// </param>
        public Roster(int maxSize = CrewRosterCatalog.MaxRosterSize)
        {
            MaxSize = maxSize > 0 ? maxSize : CrewRosterCatalog.MaxRosterSize;
            Reset();
        }

        /// <summary>已拥有（已解锁）船员 id 列表（只读视图，勿改）。</summary>
        public IReadOnlyList<string> Unlocked => _unlocked;

        /// <summary>当前编成阵容（只读视图，勿改）。</summary>
        public IReadOnlyList<string> Active => _active;

        /// <summary>已拥有船员数。</summary>
        public int UnlockedCount => _unlocked.Count;

        /// <summary>是否已拥有该船员。</summary>
        public bool IsUnlocked(string crewId)
        {
            return IndexOf(_unlocked, crewId) >= 0;
        }

        /// <summary>该船员是否在编成阵容里。</summary>
        public bool IsActive(string crewId)
        {
            return IndexOf(_active, crewId) >= 0;
        }

        /// <summary>
        /// 招募（解锁）一名船员。对应 Godot <c>unlock_crew</c>，但要求 id 在
        /// <see cref="CrewRosterCatalog"/> 名录内。
        /// </summary>
        /// <returns>本次确实新增了船员返回 true；id 未知或已拥有返回 false。</returns>
        public bool Recruit(string crewId)
        {
            if (!CrewRosterCatalog.Contains(crewId) || IsUnlocked(crewId))
                return false;

            _unlocked.Add(crewId);

            // 对应 Godot 的默认体验：初始船员自动上阵，招募到的船员不自动挤占阵容。
            if (_active.Count == 0)
                _active.Add(crewId);

            return true;
        }

        /// <summary>
        /// 设置编成阵容（对应 Godot <c>set_active_roster</c>）。
        /// 校验：非 null、数量 ≤ <see cref="MaxSize"/>、全部已拥有、无重复、无空 id。
        /// 允许空阵容（与 Godot 一致）；「至少要 1 人才能出战」是 UI 层的规则。
        /// </summary>
        /// <returns>校验通过并写入返回 true。</returns>
        public bool SetActive(IReadOnlyList<string> crewIds)
        {
            if (crewIds == null || crewIds.Count > MaxSize)
                return false;

            var candidate = new List<string>(crewIds.Count);
            for (int i = 0; i < crewIds.Count; i++)
            {
                string crewId = crewIds[i];
                if (string.IsNullOrEmpty(crewId) || IndexOf(candidate, crewId) >= 0 || !IsUnlocked(crewId))
                    return false;

                candidate.Add(crewId);
            }

            _active.Clear();
            _active.AddRange(candidate);
            return true;
        }

        /// <summary>
        /// 编成阵容中追加一名船员（UI 勾选「上阵」用）。
        /// </summary>
        /// <returns>加入成功返回 true；未拥有 / 已在阵容 / 已达上限返回 false。</returns>
        public bool AddToActive(string crewId)
        {
            if (string.IsNullOrEmpty(crewId) || !IsUnlocked(crewId)
                || IsActive(crewId) || _active.Count >= MaxSize)
            {
                return false;
            }

            _active.Add(crewId);
            return true;
        }

        /// <summary>
        /// 编成阵容中移除一名船员（UI 取消勾选用）。允许移除到空阵容（与 <see cref="SetActive"/> 一致）。
        /// </summary>
        /// <returns>移除成功返回 true；不在阵容里返回 false。</returns>
        public bool RemoveFromActive(string crewId)
        {
            int index = IndexOf(_active, crewId);
            if (index < 0)
                return false;

            _active.RemoveAt(index);
            return true;
        }

        /// <summary>清空并回到初始状态（初始船员 + 初始编成）。存档读档与测试用。</summary>
        public void Reset()
        {
            _unlocked.Clear();
            _active.Clear();

            IReadOnlyList<string> initial = CrewRosterCatalog.InitialCrewIds;
            for (int i = 0; i < initial.Count; i++)
                _unlocked.Add(initial[i]);

            if (_unlocked.Count > 0)
                _active.Add(_unlocked[0]);
        }

        /// <summary>当前编成阵容的拷贝（发布事件 / 存档用；避免把内部列表泄出去）。</summary>
        public string[] ActiveCopy()
        {
            return _active.ToArray();
        }

        /// <summary>已拥有船员 id 的拷贝。</summary>
        public string[] UnlockedCopy()
        {
            return _unlocked.ToArray();
        }

        static int IndexOf(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value))
                return -1;

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }
    }
}
