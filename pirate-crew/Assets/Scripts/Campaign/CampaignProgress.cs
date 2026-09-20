using System;
using System.Collections.Generic;
using PirateCrew.Battle.WorldMaps;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 大海域的结算进度（海图 id → 星级）。一代的「顺序解锁链」随一代退场：
    /// 8 张海图全部可出战（见选关页），进度只剩「星级记录」一个职责。
    ///
    /// 【出处】语义源自 Godot <c>modules/crew_management/scripts/progression.gd</c>
    ///   （<c>_completed_levels</c> → <see cref="_stars"/>、<c>complete_level</c> → <see cref="CompleteLevel"/>）。
    ///
    /// 【键值域】键 = 海图 id（<c>WorldMapCatalog</c> 收录的 <c>wreck_hymn</c> 等）；
    /// 旧存档里的 <c>level_01</c> 形式键会被 <see cref="SetStars"/> 静默丢弃（旧档不迁移，弃档）。
    ///
    /// 【纯 C#】不引用任何 UnityEngine 类型，可在无头验证台直接断言。
    /// </summary>
    public sealed class CampaignProgress
    {
        readonly Dictionary<string, int> _stars = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>已通关海图数。</summary>
        public int CompletedCount => _stars.Count;

        /// <summary>累计星数（8 图满分 24 星；招募门槛按本值判定）。</summary>
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

        /// <summary>海图星级；未通关返回 0。</summary>
        public int GetStars(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return 0;

            return _stars.TryGetValue(mapId, out int stars) ? stars : 0;
        }

        /// <summary>该海图是否已通关（星级 &gt; 0）。</summary>
        public bool IsCompleted(string mapId)
        {
            return GetStars(mapId) > 0;
        }

        /// <summary>
        /// 记录通关：**取历史最好成绩**，重打低星不会降级。
        /// 星级 &lt;= 0 视为未通关，不记录（以免污染 <see cref="CompletedCount"/>）。
        /// </summary>
        /// <returns>本次是否刷新了记录（首次通关或星级提高）。</returns>
        public bool CompleteLevel(string mapId, int stars)
        {
            if (string.IsNullOrEmpty(mapId) || !WorldMapCatalog.TryGet(mapId, out _) || stars <= 0)
                return false;

            int previous = GetStars(mapId);
            if (stars <= previous)
                return false;

            _stars[mapId] = Math.Min(stars, StarRules.MaxStars);
            return true;
        }

        /// <summary>清空进度。</summary>
        public void Reset()
        {
            _stars.Clear();
        }

        /// <summary>已通关海图及其星级的只读快照（存档 / UI 用）。</summary>
        public IReadOnlyDictionary<string, int> Snapshot()
        {
            return new Dictionary<string, int>(_stars, StringComparer.Ordinal);
        }

        /// <summary>直接写入星级（读档用）；id 不在目录 / ≤0 星忽略。</summary>
        public void SetStars(string mapId, int stars)
        {
            if (string.IsNullOrEmpty(mapId) || !WorldMapCatalog.TryGet(mapId, out _) || stars <= 0)
                return;

            _stars[mapId] = Math.Min(stars, StarRules.MaxStars);
        }
    }
}
