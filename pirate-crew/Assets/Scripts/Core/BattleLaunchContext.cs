using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 战役入口 → 战斗场景的**中立注入载体**：把「选了哪一关、带哪套编成」这类由高层
    /// （Campaign / CrewManagement）掌握的状态，以**数据**形式交给战斗场景读取，
    /// 使战斗模块不必反向引用高层模块（架构审计 P0-1：修前 <c>BattleController</c> 直接
    /// <c>using PirateCrew.Campaign</c> / <c>PirateCrew.CrewManagement</c> 并读
    /// <c>CampaignApi.PendingLevelId</c> 与 <c>CrewManagementApi.Roster.Active</c>，
    /// 违反「高层可依赖低层，反向禁止」）。
    ///
    /// 【为什么放 Core】Core 是零模块依赖的最低层：Campaign 写、Battle 读，两侧都只依赖 Core，
    /// 方向就正过来了。与 <see cref="EventBus"/> 同层同理——跨模块的**数据**放中立层，
    /// 而不是让低层伸手去拉高层状态。
    ///
    /// 【写入方 / 读取方】
    ///   · 写：<c>CampaignApi.SelectLevel</c>（选关时快照编成阵容）、
    ///     <c>CampaignApi.AbortPendingLevel</c> 与 <c>CampaignApi.OnBattleStarted</c>
    ///     （非战役入口那一局丢弃陈旧注入）；
    ///   · 读：<c>BattleController.BuildPlan</c>（关卡序号）与
    ///     <c>BattleController.ResolveActiveRosterSymbols</c>（出战名单）。
    ///
    /// 【快照语义】写入的是**选关那一刻**的编成快照，不是活引用：编成只在船员管理场景改，
    /// 选关到开打之间不会变，故快照与「开打时现读」等价，且不会因名册后续变动而漂移。
    /// </summary>
    public static class BattleLaunchContext
    {
        /// <summary>一次战役出征的注入载荷（纯数据，不含任何模块类型）。</summary>
        public struct Payload
        {
            /// <summary>已映射到 <c>LevelCatalog</c> 的关卡序号；0 = 无可用转写序号（战斗侧回落 fallback）。</summary>
            public int LevelNumber;

            /// <summary>战役关卡 id（诊断用；战斗侧不依赖它做判定）。</summary>
            public string LevelId;

            /// <summary>
            /// 编成对应的**战斗导出符号**快照（如 <c>redPirate</c>）；空数组 = 无可用符号（战斗侧不做红队过滤）。
            /// 【为什么注入符号而不是船员 id】「船员 id → 导出符号」是名册语义（<c>CrewRosterCatalog</c>），
            /// 留在高层做映射，战斗侧就不必引用名册类型——它只消费自己世界里的符号。
            /// </summary>
            public string[] ActiveBattleSymbols;
        }

        static readonly string[] NoCrew = new string[0];

        static bool _hasPending;
        static Payload _pending;

        /// <summary>是否有待生效的战役出征注入。</summary>
        public static bool HasPending => _hasPending;

        /// <summary>当前注入载荷（<see cref="HasPending"/> 为 false 时内容无意义）。</summary>
        public static Payload Pending => _pending;

        /// <summary>
        /// 写入一次出征注入（Campaign 侧调用；<paramref name="activeCrewIds"/> 可为 null）。
        /// 编成按**快照**存：拷贝一份，调用方之后改动传入数组不会影响本局出征名单。
        /// </summary>
        public static void SetPending(int levelNumber, string levelId, string[] activeCrewIds)
        {
            string[] crew;
            if (activeCrewIds == null || activeCrewIds.Length == 0)
            {
                crew = NoCrew;
            }
            else
            {
                crew = new string[activeCrewIds.Length];
                System.Array.Copy(activeCrewIds, crew, activeCrewIds.Length);
            }

            _pending = new Payload
            {
                LevelNumber = levelNumber,
                LevelId = levelId,
                ActiveBattleSymbols = crew,
            };
            _hasPending = true;
        }

        /// <summary>取关卡序号；无注入或序号不可用时返回 <paramref name="fallback"/>。</summary>
        public static int PendingLevelNumberOr(int fallback)
            => _hasPending && _pending.LevelNumber > 0 ? _pending.LevelNumber : fallback;

        /// <summary>清空注入（非战役入口那一局、放弃待战关卡、进入播放时调用）。</summary>
        public static void Clear()
        {
            _hasPending = false;
            _pending = default;
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态字段不会自动清空，进入播放前强制重置
        /// （与 <c>Core/EventBus</c> 的 <c>ResetOnEnterPlayMode</c> 同一手法）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode()
        {
            Clear();
        }
    }
}
