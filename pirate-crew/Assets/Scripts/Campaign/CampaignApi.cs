using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役模块的跨模块公共出口 + M3 管理循环的接线点（纯静态外观）。
    ///
    /// 【M3 闭环接线（一代退场后）】战役不再拥有自己的关卡序列——出海目标就是
    /// 世界海域图（<see cref="WorldMapRuntime"/> 待战通道，选关页/播放器 -worldMap 设置）。
    /// 本类把三个**已有**战斗事件接到结算流程上：
    ///   <c>battle_started</c>（读取待战海图 → 记为待结算归属；清掉无主陈旧待结算）
    ///   → <c>crew_died</c>（累计玩家方阵亡，用于 §9.3 星级）
    ///   → <c>match_finished</c>（评价星级 → 按海图 id 写进度 → 给编成阵容发经验/招募 → 广播 → 落盘）。
    ///
    /// 【结算键】星级存档的键 = 海图 id（<c>wreck_hymn</c> 等）。存档格式不变、值域变化
    /// （一代的 <c>level_01..15</c> 键读档时被静默丢弃——项目未发布，旧档不迁移）。
    ///
    /// 【为什么读 WorldMapRuntime 而不是 UI 转告】Campaign → PirateCrew.Battle 是高层依赖低层，
    /// 方向合法；战斗开局（battle_started）时海图待战必然在位，无需 UI 在中间转发。
    ///
    /// 【编成与战斗的关系】海图战用地图自带布阵（<c>WorldMapDefinition.Spawns</c>），
    /// 编成阵容不再注入/过滤出战名单（一代的 BattleLaunchContext 注入通道已随一代退场删除）；
    /// 名册语义保留在「结算发经验给谁 / 招募进度」上。
    ///
    /// 【存档】本类是管理循环存档的所有者：槽位 <see cref="ProgressSlot"/>（1），
    ///   把「海图星级」与「船员名册/经验」写进同一个 <see cref="SaveData"/>。
    /// </summary>
    public static class CampaignApi
    {
        /// <summary>管理循环存档槽位（0 被 <c>SaveManager.AutoSaveSlot</c> 占用，手动存档从 1 起）。</summary>
        public const int ProgressSlot = 1;

        /// <summary>存档显示名。</summary>
        const string ProgressDisplayName = "海盗军团进度";

        static CampaignManager _manager;
        static bool _bootstrapped;
        static int _playerDeaths;

        /// <summary>战役推进器（懒初始化）。</summary>
        public static CampaignManager Manager
        {
            get
            {
                if (_manager == null)
                    _manager = new CampaignManager();

                return _manager;
            }
        }

        /// <summary>海图星级与结算进度。</summary>
        public static CampaignProgress Progress => Manager.Progress;

        /// <summary>是否有待结算的海图（<c>battle_started</c> 起算、<c>match_finished</c> 消费）。</summary>
        public static bool HasPendingMap => Manager.HasPendingMap;

        /// <summary>最近一次结算结果（供结算界面展示）；无则 null。</summary>
        public static CampaignSettlement? LastSettlement { get; private set; }

        /// <summary>最近一次结算的船员奖励（供结算界面展示）；无则 null。</summary>
        public static CrewRewardPayload? LastReward { get; private set; }

        // ------------------------------------------------------------------
        // 接线
        // ------------------------------------------------------------------

        /// <summary>
        /// 订阅战斗事件、接上结算流程。幂等；由 UI 层（主菜单/选关页）在 Awake 调用。
        /// </summary>
        public static void EnsureBootstrapped()
        {
            if (_bootstrapped)
                return;

            _bootstrapped = true;
            EventBus.Subscribe(BattleEvents.BattleStarted, OnBattleStarted);
            EventBus.Subscribe(BattleEvents.CrewDied, OnCrewDied);
            EventBus.Subscribe(BattleEvents.MatchFinished, OnMatchFinished);
        }

        // ------------------------------------------------------------------
        // 查询（UI 用）
        // ------------------------------------------------------------------

        /// <summary>海图星级；未通关返回 0。</summary>
        public static int GetStars(string mapId) => Progress.GetStars(mapId);

        /// <summary>清空「最近一次结算」展示数据（结算界面展示完调用）。</summary>
        public static void ClearLastResult()
        {
            LastSettlement = null;
            LastReward = null;
        }

        // ------------------------------------------------------------------
        // 战斗事件处理（静态订阅；EventBus 在进入播放时会清空，靠 EnsureBootstrapped 重订阅）
        // ------------------------------------------------------------------

        static void OnBattleStarted(object payload)
        {
            // 每一局开打都从 0 计阵亡（不区分是哪个入口进的战斗）。
            _playerDeaths = 0;

            // 结算归属 = 本局实际加载的世界海图。没有待战海图（样板三关 / 直接 Play）时
            // 丢掉陈旧待结算——没打完就退出的那一局不能被误结算成战役进度。
            if (WorldMapRuntime.TryGetPending(out WorldMapDefinition map))
                Manager.SelectMap(map.Id);
            else if (Manager.HasPendingMap)
                Manager.AbortLevel();
        }

        static void OnCrewDied(object payload)
        {
            if (payload is CrewDiedPayload died && died.TeamIndex == CrewCatalog.RedTeamIndex)
                _playerDeaths++;
        }

        static void OnMatchFinished(object payload)
        {
            if (!(payload is MatchFinishedPayload finished))
                return;

            // 非海图入口（样板三关 / 直接 Play 战斗场景）没有待结算海图 → 不结算。
            if (!Manager.HasPendingMap)
                return;

            bool cleared = finished.Outcome == CampaignManager.PlayerWinOutcome;
            var result = new CampaignResult(
                cleared: cleared,
                playerDeaths: _playerDeaths,
                chestsCollected: 0,   // 宝箱未实装
                chestsTotal: 0,
                score: finished.Score);

            if (!Manager.TrySettle(result, out CampaignSettlement settlement))
                return;

            // 船员奖励：给编成阵容发经验，并按「累计星数」过招募门槛。
            // 门槛口径（一代退场执行决策）：原「已通关的最大关卡序号（1–15）」改为
            // 「累计星数（0–24）」，CrewRosterCatalog 的门槛数值 0/3/5/7/10/13 直接沿用。
            CrewRewardPayload reward = CrewManagementApi.GrantMapReward(
                settlement.MapId, settlement.Stars, Progress.TotalStars);

            LastSettlement = settlement;
            LastReward = reward;

            EventBus.Publish(CampaignEvents.MapCompleted, new CampaignMapCompletedPayload(
                settlement.MapId, settlement.Stars, settlement.Cleared,
                settlement.FirstClear, settlement.Score));

            SaveProgress();
        }

        // ------------------------------------------------------------------
        // 存档
        // ------------------------------------------------------------------

        /// <summary>
        /// 把「海图星级 + 船员名册/经验」写进同一个存档数据对象（纯 C#，可无头断言）。
        /// </summary>
        public static void WriteTo(SaveData data)
        {
            CampaignSaveCodec.Write(data, Progress);
            CrewManagementApi.WriteTo(data);
        }

        /// <summary>从存档数据恢复「海图星级 + 船员名册/经验」。</summary>
        public static void ReadFrom(SaveData data)
        {
            CampaignSaveCodec.Read(data, Progress);
            CrewManagementApi.ReadFrom(data);
        }

        /// <summary>
        /// 落盘到 <see cref="ProgressSlot"/>。没有 SaveManager 实例（无头验证台 / 未走 Bootstrapper）时静默返回 false。
        /// </summary>
        public static bool SaveProgress()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null)
                return false;

            // 先 SlotExists 再 LoadFromSlot：槽位不存在时 LoadFromSlot 会打一条 no-op 的告警日志。
            SaveData data = save.SlotExists(ProgressSlot) ? save.LoadFromSlot(ProgressSlot) : null;
            if (data == null)
                data = new SaveData();

            WriteTo(data);
            return save.SaveToSlot(ProgressSlot, data, ProgressDisplayName);
        }

        /// <summary>
        /// 从存档数据读档。没有实例或槽位不存在时返回 false（保持现状，不报错）。
        /// </summary>
        public static bool LoadProgress()
        {
            SaveManager save = SaveManager.Instance;
            if (save == null || !save.SlotExists(ProgressSlot))
                return false;

            SaveData data = save.LoadFromSlot(ProgressSlot);
            if (data == null)
                return false;

            ReadFrom(data);
            return true;
        }

        // ------------------------------------------------------------------
        // 静态残留
        // ------------------------------------------------------------------

        /// <summary>清空全部静态状态（测试 / 重开档用）。</summary>
        public static void Reset()
        {
            // 【先退订再置标志】EnsureBootstrapped 订阅的三个事件必须在这里对称退订：
            // 方法组转换每次生成新委托实例，靠 EventBus 的 Contains 去重兜底属于实现细节；
            // Reset 不退订会让 EventBus 里残留监听者（架构违规：订阅方必须退订，见 EventBus 约定 3）。
            if (_bootstrapped)
            {
                EventBus.Unsubscribe(BattleEvents.BattleStarted, OnBattleStarted);
                EventBus.Unsubscribe(BattleEvents.CrewDied, OnCrewDied);
                EventBus.Unsubscribe(BattleEvents.MatchFinished, OnMatchFinished);
            }

            _manager = new CampaignManager();
            _bootstrapped = false;
            _playerDeaths = 0;
            LastSettlement = null;
            LastReward = null;
            // 海图待战同批清（测试域隔离也需要）。
            WorldMapRuntime.ClearPending();
        }

        /// <summary>
        /// 关闭 Domain Reload 时静态字段不会自动清空，进入播放前强制重置
        /// （与 <c>Core/EventBus</c> 的 <c>ResetOnEnterPlayMode</c> 同一手法）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnEnterPlayMode()
        {
            Reset();
        }
    }
}
