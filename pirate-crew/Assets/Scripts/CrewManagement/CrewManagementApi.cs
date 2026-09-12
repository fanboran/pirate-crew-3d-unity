using System.Collections.Generic;
using PirateCrew.Core;
using UnityEngine;

namespace PirateCrew.CrewManagement
{
    /// <summary>
    /// 船员管理模块的跨模块公共出口（纯静态外观，对应 Godot <c>modules/crew_management/api.gd</c>）。
    ///
    /// 【架构定位】
    ///   模块状态（名册 + 经验）是**纯 C# 静态持有**的，不需要 MonoBehaviour、不需要摆进场景：
    ///   · 战斗场景会卸载管理场景，状态必须跨场景存活；
    ///   · MonoBehaviour 实例化在无头验证台不可用，纯 C# 才能被 NUnit 直接断言。
    ///   进入播放时用 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 清空静态残留（与 Core/EventBus 同一手法）。
    ///
    /// 【跨模块用法】其他模块只调本类公开方法（架构原则：跨模块调用的出口放 XxxApi），
    ///   状态变化通过 <see cref="CrewManagementEvents"/> 的事件广播给 UI。
    ///
    /// 【M3 边界（重要，别高估）】编成阵容目前**不会注入战斗**——
    ///   <c>PirateCrew.Battle.BattleController</c> 的出战名单来自关卡资产（§7.2），
    ///   本轮（M3）不允许改 PirateCrew/，故名册/编成只影响「结算发经验给谁」。
    ///   真正把编成喂给战斗需要 BattleController 增一个读取入口，已写进交付报告由协调者裁决。
    /// </summary>
    public static class CrewManagementApi
    {
        static Roster _roster;
        static CrewProgression _progression;

        /// <summary>名册状态（懒初始化）。</summary>
        public static Roster Roster
        {
            get
            {
                EnsureState();
                return _roster;
            }
        }

        /// <summary>经验账本（懒初始化）。</summary>
        public static CrewProgression Progression
        {
            get
            {
                EnsureState();
                return _progression;
            }
        }

        /// <summary>可招募船员名录（转发 <see cref="CrewRosterCatalog"/>，方便 UI 单点取用）。</summary>
        public static IReadOnlyList<CrewRosterEntry> AllCrews => CrewRosterCatalog.All;

        /// <summary>已拥有船员 id 列表。</summary>
        public static IReadOnlyList<string> UnlockedCrewIds => Roster.Unlocked;

        /// <summary>某船员是否已拥有。</summary>
        public static bool IsUnlocked(string crewId) => Roster.IsUnlocked(crewId);

        /// <summary>某船员是否在编成阵容里。</summary>
        public static bool IsActive(string crewId) => Roster.IsActive(crewId);

        /// <summary>招募一名船员并广播事件。</summary>
        /// <returns>本次确实新招募返回 true。</returns>
        public static bool Recruit(string crewId)
        {
            if (!Roster.Recruit(crewId))
                return false;

            PublishUnlocked(crewId);
            PublishRosterUpdated();
            return true;
        }

        /// <summary>设置编成阵容并广播事件（校验见 <see cref="Roster.SetActive"/>）。</summary>
        public static bool SetActiveRoster(IReadOnlyList<string> crewIds)
        {
            if (!Roster.SetActive(crewIds))
                return false;

            PublishRosterUpdated();
            return true;
        }

        /// <summary>编成阵容追加一人并广播事件。</summary>
        public static bool AddToActive(string crewId)
        {
            if (!Roster.AddToActive(crewId))
                return false;

            PublishRosterUpdated();
            return true;
        }

        /// <summary>编成阵容移除一人并广播事件。</summary>
        public static bool RemoveFromActive(string crewId)
        {
            if (!Roster.RemoveFromActive(crewId))
                return false;

            PublishRosterUpdated();
            return true;
        }

        /// <summary>
        /// 关卡结算：给本关编成阵容发经验 + 按通关关卡序号招募新船员，并广播事件。
        ///
        /// 【调用方】<c>CampaignApi</c>（跨模块命令走本方法；通知走事件）。
        /// </summary>
        /// <param name="levelId">关卡 id（<c>CampaignCatalog</c> 的 <c>level_01</c> 形式）。</param>
        /// <param name="stars">本关星级（0 = 未通关）。</param>
        /// <param name="clearedLevelNumber">
        /// 已通关的战役关卡序号（用于招募判定）；未通关传 0。
        /// </param>
        /// <returns>本次结算的奖励载荷（供调用方记录/展示）。</returns>
        public static CrewRewardPayload GrantLevelReward(string levelId, int stars, int clearedLevelNumber)
        {
            string[] activeIds = Roster.ActiveCopy();
            int xpPerCrew = CrewProgressionRules.XpAward(stars);

            if (xpPerCrew > 0)
            {
                for (int i = 0; i < activeIds.Length; i++)
                    Progression.GrantXp(activeIds[i], xpPerCrew);
            }

            List<string> unlocked = UnlockCrewsForLevel(clearedLevelNumber);

            var payload = new CrewRewardPayload(levelId, stars, xpPerCrew, activeIds, unlocked.ToArray());
            EventBus.Publish(CrewManagementEvents.RewardGranted, payload);
            PublishRosterUpdated();
            return payload;
        }

        /// <summary>
        /// 招募所有解锁条件 ≤ <paramref name="clearedLevelNumber"/> 且尚未拥有的船员。
        /// </summary>
        /// <returns>本次新招募的船员 id 列表。</returns>
        public static List<string> UnlockCrewsForLevel(int clearedLevelNumber)
        {
            var unlocked = new List<string>();
            if (clearedLevelNumber <= 0)
                return unlocked;

            IReadOnlyList<CrewRosterEntry> all = CrewRosterCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                CrewRosterEntry entry = all[i];
                if (!entry.IsInitial && entry.UnlockLevelNumber <= clearedLevelNumber
                    && Roster.Recruit(entry.Id))
                {
                    unlocked.Add(entry.Id);
                    PublishUnlocked(entry.Id);
                }
            }

            return unlocked;
        }

        // ------------------------------------------------------------------
        // 存档（走 Core.SaveData 的字符串键值 API，不改 Core）
        // ------------------------------------------------------------------

        /// <summary>把名册与经验写进存档数据。</summary>
        public static void WriteTo(SaveData data)
        {
            CrewManagementSaveCodec.Write(data, Roster, Progression);
        }

        /// <summary>从存档数据恢复名册与经验。</summary>
        public static void ReadFrom(SaveData data)
        {
            CrewManagementSaveCodec.Read(data, Roster, Progression);
        }

        /// <summary>
        /// 清空静态状态并回到初始名册（测试 / 重新开档用）。
        /// </summary>
        public static void Reset()
        {
            _roster = new Roster();
            _progression = new CrewProgression();
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态字段不会自动清空，进入播放前强制重置
        /// （与 <c>Core/EventBus</c> 的 <c>ResetOnEnterPlayMode</c> 同一手法）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode()
        {
            _roster = null;
            _progression = null;
        }

        static void EnsureState()
        {
            if (_roster == null)
                _roster = new Roster();
            if (_progression == null)
                _progression = new CrewProgression();
        }

        static void PublishRosterUpdated()
        {
            EventBus.Publish(CrewManagementEvents.RosterUpdated,
                new RosterUpdatedPayload(Roster.UnlockedCount, Roster.ActiveCopy()));
        }

        static void PublishUnlocked(string crewId)
        {
            string displayName = crewId;
            if (CrewRosterCatalog.TryGet(crewId, out CrewRosterEntry entry))
                displayName = entry.DisplayName;

            EventBus.Publish(CrewManagementEvents.CrewUnlocked, new CrewUnlockedPayload(crewId, displayName));
        }
    }
}
