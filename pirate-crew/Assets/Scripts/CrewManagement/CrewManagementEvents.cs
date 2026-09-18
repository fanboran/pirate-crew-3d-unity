using System;

namespace PirateCrew.CrewManagement
{
    /// <summary>
    /// 船员管理模块（CrewManagement）的 EventBus 事件契约集中登记。
    ///
    /// 【约定】跨模块通信只走 <c>PirateCrew.Core.EventBus</c> 的字符串事件；
    ///         事件名一律 snake_case，禁止在业务代码里散落魔法字符串。
    ///         本类是船员管理事件的唯一登记处，发布/订阅双方都从这里取常量。
    ///         事件名与载荷已登记在 <c>docs/EventBus事件契约.md</c>。
    ///
    /// 【方向】本模块只<b>发布</b>事件（通知名册/招募/奖励变化）；
    ///         结算命令由 <c>CampaignApi</c> 直接调用 <see cref="CrewManagementApi"/> 的公开方法
    ///         （跨模块调用的公共出口按架构原则放 XxxApi）。
    /// </summary>
    public static class CrewManagementEvents
    {
        /// <summary>名册或编成发生变化（载荷 <see cref="RosterUpdatedPayload"/>）。</summary>
        public const string RosterUpdated = "crew_roster_updated";

        /// <summary>新船员被招募（载荷 <see cref="CrewUnlockedPayload"/>）。</summary>
        public const string CrewUnlocked = "crew_unlocked";

        /// <summary>关卡结算给船员发经验/招募（载荷 <see cref="CrewRewardPayload"/>）。</summary>
        public const string RewardGranted = "crew_reward_granted";
    }

    /// <summary><see cref="CrewManagementEvents.RosterUpdated"/> 载荷。</summary>
    public readonly struct RosterUpdatedPayload
    {
        /// <summary>已拥有（已解锁）船员数。</summary>
        public readonly int UnlockedCount;

        /// <summary>当前编成阵容（船员 id 列表，拷贝）。</summary>
        public readonly string[] ActiveCrewIds;

        public RosterUpdatedPayload(int unlockedCount, string[] activeCrewIds)
        {
            UnlockedCount = unlockedCount;
            ActiveCrewIds = activeCrewIds ?? Array.Empty<string>();
        }
    }

    /// <summary><see cref="CrewManagementEvents.CrewUnlocked"/> 载荷（对应 Godot signal <c>crew_unlocked(crew_id)</c>）。</summary>
    public readonly struct CrewUnlockedPayload
    {
        /// <summary>新招募的船员 id。</summary>
        public readonly string CrewId;

        /// <summary>中文显示名（直接带出来，省得订阅方再查名录）。</summary>
        public readonly string DisplayName;

        public CrewUnlockedPayload(string crewId, string displayName)
        {
            CrewId = crewId;
            DisplayName = displayName;
        }
    }

    /// <summary><see cref="CrewManagementEvents.RewardGranted"/> 载荷。</summary>
    public readonly struct CrewRewardPayload
    {
        /// <summary>结算是哪张海图（<c>WorldMapCatalog</c> 的海图 id，如 <c>wreck_hymn</c>）。</summary>
        public readonly string LevelId;

        /// <summary>本局星级（0 = 未通关）。</summary>
        public readonly int Stars;

        /// <summary>每个出战船员获得的经验。</summary>
        public readonly int XpPerCrew;

        /// <summary>实际获得经验的船员 id（= 编成阵容）。</summary>
        public readonly string[] CrewIds;

        /// <summary>本次结算新招募的船员 id（无则空数组）。</summary>
        public readonly string[] UnlockedCrewIds;

        public CrewRewardPayload(string levelId, int stars, int xpPerCrew,
            string[] crewIds, string[] unlockedCrewIds)
        {
            LevelId = levelId;
            Stars = stars;
            XpPerCrew = xpPerCrew;
            CrewIds = crewIds ?? Array.Empty<string>();
            UnlockedCrewIds = unlockedCrewIds ?? Array.Empty<string>();
        }
    }
}
