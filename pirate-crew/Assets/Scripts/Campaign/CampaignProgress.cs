using System;
using System.Collections.Generic;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 单人战役的关卡进度（星级 + 顺序解锁）。
    ///
    /// 【出处】语义翻译自 Godot <c>modules/crew_management/scripts/progression.gd</c>
    ///   （Godot 版把「关卡完成/星级/解锁」放在船员管理模块的 Progression 里；
    ///    Unity 版按职责把「关卡进度」归到 Campaign，「船员经验」归到 CrewManagement）。
    ///   对应关系：<c>_completed_levels</c> → <see cref="_stars"/>、<c>get_level_stars</c> → <see cref="GetStars"/>、
    ///   <c>complete_level</c> → <see cref="CompleteLevel"/>、<c>is_level_unlocked</c> → <see cref="IsUnlocked"/>。
    ///
    /// 【解锁链】与 Godot 同为「前一关已通关才解锁」；「前一关」经
    ///   <see cref="CampaignCatalog.PreviousImplementedLevel"/> 取「最近一个有转写数据的前一关」——
    ///   <c>LevelCatalog</c> 33 关已全部转写，现状即普通顺序解锁；该间接层保留的意义是
    ///   将来若个别关卡号缺数据，解锁链自动绕开缺口而非卡死。
    ///   规则见 <see cref="IsUnlocked"/>。
    ///
    /// 【纯 C#】不引用任何 UnityEngine 类型，可在无头验证台直接断言。
    /// </summary>
    public sealed class CampaignProgress
    {
        readonly Dictionary<string, int> _stars = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>已通关关卡数。</summary>
        public int CompletedCount => _stars.Count;

        /// <summary>累计星数（全 15 关满分 45 星）。</summary>
        public int TotalStars
        {
            get
            {
                int total = 0;
                foreach (int stars in _stars.Values)
                    total += stars;

                return total;
            }
        }

        /// <summary>关卡星级；未通关返回 0。</summary>
        public int GetStars(string levelId)
        {
            if (string.IsNullOrEmpty(levelId))
                return 0;

            return _stars.TryGetValue(levelId, out int stars) ? stars : 0;
        }

        /// <summary>该关卡是否已通关（星级 &gt; 0）。</summary>
        public bool IsCompleted(string levelId)
        {
            return GetStars(levelId) > 0;
        }

        /// <summary>
        /// 记录通关（对应 Godot <c>complete_level</c>）：**取历史最好成绩**，重打低星不会降级。
        /// 星级 &lt;= 0 视为未通关，不记录（Godot 会写入 0，本作显式忽略以免污染 <see cref="CompletedCount"/>）。
        /// </summary>
        /// <returns>本次是否刷新了记录（首次通关或星级提高）。</returns>
        public bool CompleteLevel(string levelId, int stars)
        {
            if (!CampaignCatalog.TryGet(levelId, out _) || stars <= 0)
                return false;

            int previous = GetStars(levelId);
            if (stars <= previous)
                return false;

            _stars[levelId] = Math.Min(stars, StarRules.MaxStars);
            return true;
        }

        /// <summary>
        /// 关卡是否已解锁。
        ///
        /// 【规则】第 1 关默认解锁；其余关卡要求「最近一个有转写数据的前一关」已通关
        /// （33 关全转写的现状下就是普通顺序解锁，见类头说明）。
        /// </summary>
        public bool IsUnlocked(string levelId)
        {
            if (!CampaignCatalog.TryGet(levelId, out CampaignLevel level))
                return false;

            CampaignLevel? previous = CampaignCatalog.PreviousImplementedLevel(level.LevelNumber);
            if (previous == null)
                return true;

            return IsCompleted(previous.Value.LevelId);
        }

        /// <summary>下一个待挑战的关卡 id（已解锁但未通关的最小序号关卡）；全部通关返回 null。</summary>
        public string NextPlayableLevelId()
        {
            IReadOnlyList<CampaignLevel> all = CampaignCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!IsCompleted(all[i].LevelId) && IsUnlocked(all[i].LevelId))
                    return all[i].LevelId;
            }

            return null;
        }

        /// <summary>
        /// 已通关关卡里的最大序号（没有通关记录返回 0）。船员招募门槛按这个值判定。
        /// </summary>
        public int MaxCompletedLevelNumber
        {
            get
            {
                int max = 0;
                foreach (string levelId in _stars.Keys)
                {
                    if (CampaignCatalog.TryGet(levelId, out CampaignLevel level) && level.LevelNumber > max)
                        max = level.LevelNumber;
                }

                return max;
            }
        }

        /// <summary>清空进度。</summary>
        public void Reset()
        {
            _stars.Clear();
        }

        /// <summary>已通关关卡及其星级的只读快照（存档 / UI 用）。</summary>
        public IReadOnlyDictionary<string, int> Snapshot()
        {
            return new Dictionary<string, int>(_stars, StringComparer.Ordinal);
        }

        /// <summary>直接写入星级（读档用）；≤0 或非法关卡忽略。</summary>
        public void SetStars(string levelId, int stars)
        {
            if (!CampaignCatalog.TryGet(levelId, out _) || stars <= 0)
                return;

            _stars[levelId] = Math.Min(stars, StarRules.MaxStars);
        }
    }
}
