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
    /// 【对 Godot 版的唯一语义改动（提案/待定）】解锁链**跳过未转写数据的关卡**：
    ///   Godot 是「前一序号关卡已完成」，但本工程 <c>LevelCatalog</c> 只转写了 3 关，
    ///   严格按前一号判定会让演示链卡在 <c>level_02</c>。
    ///   现规则见 <see cref="IsUnlocked"/>；实现见
    ///   <see cref="CampaignCatalog.PreviousImplementedLevel"/>。取消该提案只需把那里换成「前一序号」。
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
        /// 【规则】序号最小的已转写关卡默认解锁；其余关卡要求
        /// 「序号更小的、最近的一个已转写关卡」已通关（跳过未转写关卡，见类头说明）。
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
