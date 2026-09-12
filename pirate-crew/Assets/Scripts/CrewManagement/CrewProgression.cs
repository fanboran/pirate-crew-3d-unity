using System;
using System.Collections.Generic;

namespace PirateCrew.CrewManagement
{
    /// <summary>
    /// 船员经验/升级曲线（纯 C# 静态规则，无头验证台可断言）。
    ///
    /// 【出处】⚠ <b>Flash 逆向文档里没有任何船员经验/等级规则</b>——
    ///   原版所有海盗属性完全相同、没有成长系统，进度只有「关卡得分 + 关卡解锁」
    ///   （<c>docs/参考游戏逆向-海盗军团抢宝藏-静态.md</c> §4.1 / §7.3）。
    ///   Godot 版的 <c>progression.gd</c> 也只记「关卡星级」，不含经验。
    ///   因此本曲线整体为 <b>提案/待定</b>：数值只求「跑得通、可测、可调」，不代表最终手感。
    ///
    /// 【曲线形状（提案/待定）】升级所需经验逐级 +100（L1→L2 需 100，L2→L3 需 200……L9→L10 需 900），
    ///   满级 <see cref="MaxLevel"/> = 10 级；通关按星级发经验（1★ 150 / 2★ 200 / 3★ 250）。
    /// </summary>
    public static class CrewProgressionRules
    {
        /// <summary>等级上限（提案/待定）。</summary>
        public const int MaxLevel = 10;

        /// <summary>L1→L2 所需经验（提案/待定）。</summary>
        public const int BaseXpPerLevel = 100;

        /// <summary>每升一级所需经验的递增量（提案/待定）。</summary>
        public const int XpStepPerLevel = 100;

        /// <summary>通关基础经验（提案/待定）。</summary>
        public const int ClearXpBase = 100;

        /// <summary>每颗星额外经验（提案/待定）。</summary>
        public const int XpPerStar = 50;

        /// <summary>
        /// 从 <paramref name="level"/> 升到下一级所需经验；已满级返回 0。
        /// </summary>
        public static int XpToNextLevel(int level)
        {
            if (level >= MaxLevel)
                return 0;

            int safeLevel = level < 1 ? 1 : level;
            return BaseXpPerLevel + (safeLevel - 1) * XpStepPerLevel;
        }

        /// <summary>达到指定等级所需的**累计**经验；等级 1（含以下）为 0，超过上限按上限算。</summary>
        public static int TotalXpForLevel(int level)
        {
            int target = level < 1 ? 1 : (level > MaxLevel ? MaxLevel : level);

            int total = 0;
            for (int current = 1; current < target; current++)
                total += XpToNextLevel(current);

            return total;
        }

        /// <summary>由累计经验求等级（夹在 1..<see cref="MaxLevel"/>）。</summary>
        public static int LevelForXp(int totalXp)
        {
            if (totalXp <= 0)
                return 1;

            int level = 1;
            int remaining = totalXp;
            while (level < MaxLevel)
            {
                int need = XpToNextLevel(level);
                if (need <= 0 || remaining < need)
                    break;

                remaining -= need;
                level++;
            }

            return level;
        }

        /// <summary>
        /// 关卡结算应发经验（提案/待定）：未通关不发；通关 = 基础 + 星级 × 每星。
        /// </summary>
        /// <param name="stars">关卡星级；&lt;= 0 视为未通关。</param>
        public static int XpAward(int stars)
        {
            if (stars <= 0)
                return 0;

            return ClearXpBase + stars * XpPerStar;
        }
    }

    /// <summary>
    /// 船员经验账本（每个船员 id → 累计经验）。纯 C#，可在无头验证台直接断言。
    ///
    /// 【与 Godot 的关系】Godot 版没有对应物（<c>progression.gd</c> 记的是关卡星级，
    /// 关卡进度在 <c>PirateCrew.Campaign.CampaignProgress</c> 里）；本类是 M3 新增，规则见
    /// <see cref="CrewProgressionRules"/>（提案/待定）。
    /// </summary>
    public sealed class CrewProgression
    {
        readonly Dictionary<string, int> _xp = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>该船员的累计经验；未知船员返回 0。</summary>
        public int GetXp(string crewId)
        {
            if (string.IsNullOrEmpty(crewId))
                return 0;

            return _xp.TryGetValue(crewId, out int xp) ? xp : 0;
        }

        /// <summary>该船员的当前等级（1..<see cref="CrewProgressionRules.MaxLevel"/>）。</summary>
        public int GetLevel(string crewId)
        {
            return CrewProgressionRules.LevelForXp(GetXp(crewId));
        }

        /// <summary>升到下一级还差多少经验；已满级返回 0。</summary>
        public int GetXpToNextLevel(string crewId)
        {
            int level = GetLevel(crewId);
            return CrewProgressionRules.XpToNextLevel(level);
        }

        /// <summary>当前等级内的经验进度（0..1）；已满级返回 1。</summary>
        public float GetLevelProgress(string crewId)
        {
            int level = GetLevel(crewId);
            int need = CrewProgressionRules.XpToNextLevel(level);
            if (need <= 0)
                return 1f;

            int intoLevel = GetXp(crewId) - CrewProgressionRules.TotalXpForLevel(level);
            return Math.Max(0, intoLevel) / (float)need;
        }

        /// <summary>
        /// 给船员加经验（非正数忽略，id 必须非空）。
        /// </summary>
        /// <returns>本次升了几级（0 = 未升级）。</returns>
        public int GrantXp(string crewId, int amount)
        {
            if (string.IsNullOrEmpty(crewId) || amount <= 0)
                return 0;

            int before = GetLevel(crewId);
            _xp[crewId] = GetXp(crewId) + amount;
            return GetLevel(crewId) - before;
        }

        /// <summary>直接写入累计经验（读档用）；负值夹到 0。</summary>
        public void SetXp(string crewId, int totalXp)
        {
            if (string.IsNullOrEmpty(crewId))
                return;

            _xp[crewId] = totalXp < 0 ? 0 : totalXp;
        }

        /// <summary>已记录经验的船员 id 列表（读档/存档与 UI 用）。</summary>
        public IReadOnlyCollection<string> CrewIds => _xp.Keys;

        /// <summary>清空全部经验。</summary>
        public void Reset()
        {
            _xp.Clear();
        }
    }
}
