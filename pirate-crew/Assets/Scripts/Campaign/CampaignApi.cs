using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役模块的跨模块公共出口 + M3 管理循环的接线点（纯静态外观，对应 Godot <c>modules/campaign/api.gd</c>）。
    ///
    /// 【为什么用静态外观】与 <see cref="CrewManagementApi"/> 同理：状态须跨场景存活（战斗场景会卸载管理场景），
    ///   且纯 C# 才能在无头验证台断言。
    ///
    /// 【M3 闭环接线】<see cref="EnsureBootstrapped"/> 把三个**已有**战斗事件接到结算流程上：
    ///   <c>battle_started</c>（重置本局阵亡计数）→ <c>crew_died</c>（累计玩家方阵亡，用于 §9.3 星级）
    ///   → <c>match_finished</c>（评价星级 → 写战役进度 → 给编成阵容发经验/招募 → 广播 → 落盘）。
    ///   全程不改 PirateCrew/（黑名单），只订阅它已有的事件。
    ///
    /// 【本轮边界（重要）】
    ///   1. 选关同时决定**结算归属的关卡与加载的竞技场**：Battle 场景未指定 level 资产时，
    ///      <c>BattleController.BuildPlan()</c> 经 <see cref="PendingBattleLevelNumberOr"/> 取
    ///      「已选、等待结算」的关卡号注入；无待战关卡时回落 <c>BattleController.fallbackLevelNumber</c>。
    ///   2. 编成阵容注入出战名单：战役入口那一局（<see cref="PendingLevelId"/> 非空）红队按
    ///      <c>CrewManagementApi.Roster.Active</c> 过滤出征名单（<c>BattleController.ResolveActiveRosterSymbols</c>）；
    ///      主菜单直进战斗 / 2P 不过滤，保持关卡作者写好的布阵。
    ///   3. 宝箱未实装，星级用 <c>chestsTotal = 0</c>（3★ 退化为「通关 + 全员存活」，见 <see cref="StarRules"/>）。
    ///
    /// 【存档】本类是管理循环存档的所有者：槽位 <see cref="ProgressSlot"/>（1），
    ///   把「战役关卡星级」与「船员名册/经验」写进同一个 <see cref="SaveData"/>
    ///   （各模块只负责自己那几个字符串键，见各自的 SaveCodec）。
    /// </summary>
    public static class CampaignApi
    {
        /// <summary>管理循环存档槽位（0 被 <c>SaveManager.AutoSaveSlot</c> 占用，手动存档从 1 起）。</summary>
        public const int ProgressSlot = 1;

        /// <summary>EventBus 场景切换事件名（与 <c>Core/SceneLoader</c> 约定一致）。</summary>
        const string ChangeSceneEvent = "change_scene";

        /// <summary>存档显示名。</summary>
        const string ProgressDisplayName = "海盗军团进度";

        static CampaignManager _manager;
        static bool _bootstrapped;
        static int _playerDeaths;

        /// <summary>
        /// 「这一局战斗是从关卡选择进来的」标记：<see cref="SelectLevel"/> 置位、
        /// <see cref="OnBattleStarted"/> 消费。用于识别「陈旧待结算关卡」——见 <see cref="OnBattleStarted"/>。
        /// </summary>
        static bool _campaignBattleRequested;

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

        /// <summary>关卡星级与解锁进度。</summary>
        public static CampaignProgress Progress => Manager.Progress;

        /// <summary>当前章节（读写；选关时同步）。</summary>
        public static int CurrentChapter
        {
            get => Manager.CurrentChapter;
            set => Manager.CurrentChapter = value;
        }

        /// <summary>当前已选、等待结算的关卡 id；无则 null。</summary>
        public static string PendingLevelId => Manager.PendingLevelId;

        /// <summary>最近一次结算结果（供结算界面展示）；无则 null。</summary>
        public static CampaignSettlement? LastSettlement { get; private set; }

        /// <summary>最近一次结算的船员奖励（供结算界面展示）；无则 null。</summary>
        public static CrewRewardPayload? LastReward { get; private set; }

        // ------------------------------------------------------------------
        // 接线
        // ------------------------------------------------------------------

        /// <summary>
        /// 订阅战斗事件、接上结算流程。幂等；由 UI 层（主菜单/管理界面）在 Awake 调用。
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
        // 选关 → 进战斗
        // ------------------------------------------------------------------

        /// <summary>
        /// 选定关卡（校验已解锁）→ 广播 <see cref="CampaignEvents.LevelSelected"/> → 请求切到 Battle 场景。
        /// 同场景重载（「再来一局」）由 SceneLoader 按同名目标自动不压栈，无需调用方区分
        /// （原 replaceTopScene 补丁退役，见 代码审计报告 §一.1）。
        /// </summary>
        /// <param name="levelId">关卡 id。</param>
        /// <returns>关卡非法或未解锁返回 false，且不发起场景切换。</returns>
        public static bool SelectLevel(string levelId)
        {
            if (!Manager.TrySelectLevel(levelId))
                return false;

            _campaignBattleRequested = true;

            CampaignLevel level = CampaignCatalog.Get(levelId);
            EventBus.Publish(CampaignEvents.LevelSelected,
                new CampaignLevelSelectedPayload(level.LevelId, level.LevelNumber, level.Chapter));

            // 走 EventBus 请求场景切换（UI 不直接持有 SceneLoader，见 Core/SceneLoader 约定）。
            EventBus.Publish(ChangeSceneEvent, SceneNames.Battle);
            return true;
        }

        /// <summary>
        /// 放弃当前待结算关卡（不计进度 / 不发事件）。
        /// 一般不需要手工调用：<see cref="OnBattleStarted"/> 会自动清掉「非战役入口那一局」的陈旧待结算关卡。
        /// </summary>
        public static void AbortPendingLevel()
        {
            Manager.AbortLevel();
            _campaignBattleRequested = false;
        }

        /// <summary>清空「最近一次结算」展示数据（结算界面展示完调用）。</summary>
        public static void ClearLastResult()
        {
            LastSettlement = null;
            LastReward = null;
        }

        // ------------------------------------------------------------------
        // 查询（UI 用）
        // ------------------------------------------------------------------

        /// <summary>关卡星级；未通关返回 0。</summary>
        public static int GetStars(string levelId) => Progress.GetStars(levelId);

        /// <summary>关卡是否已解锁。</summary>
        public static bool IsLevelUnlocked(string levelId) => Progress.IsUnlocked(levelId);

        /// <summary>下一关（已解锁未通关的最小序号关卡）；全部通关返回 null。</summary>
        public static string NextLevelId() => Progress.NextPlayableLevelId();

        /// <summary>
        /// 待战关卡在 <c>LevelCatalog</c> 里的关卡序号；无法确定时返回 <paramref name="fallback"/>。
        ///
        /// 【用途 / 现状】这是 **Battle 侧关卡注入** 的衔接点：
        ///   <c>BattleController.BuildPlan()</c> 在场景未指定 level 资产时用它取所选关卡号，
        ///   让「选关」决定加载哪张竞技场（见 <c>BattleController.cs</c> 的「关卡注入」注释）。
        ///   <c>LevelCatalog</c> 已 33 关全量转写（<c>IsTranscribed</c> 对 1–33 恒真），
        ///   故正常选关后恒返回所选关卡号；仅关卡号越界 / 未选关时回落 <paramref name="fallback"/>。
        /// </summary>
        public static int PendingBattleLevelNumberOr(int fallback)
        {
            string pending = PendingLevelId;
            if (string.IsNullOrEmpty(pending) || !CampaignCatalog.TryGet(pending, out CampaignLevel level))
                return fallback;

            return LevelCatalog.IsTranscribed(level.LevelNumber) ? level.LevelNumber : fallback;
        }

        /// <summary>章节关卡列表。</summary>
        public static IReadOnlyList<CampaignLevel> GetChapterLevels(int chapter)
            => CampaignCatalog.GetChapter(chapter);

        // ------------------------------------------------------------------
        // 战斗事件处理（静态订阅；EventBus 在进入播放时会清空，靠 EnsureBootstrapped 重订阅）
        // ------------------------------------------------------------------

        static void OnBattleStarted(object payload)
        {
            // 每一局开打都从 0 计阵亡（不区分是哪个入口进的战斗）。
            _playerDeaths = 0;

            // 这一局不是从关卡选择进来的（主菜单「进入战斗」/ 2P）→ 丢掉上一局留下的待结算关卡，
            // 否则没打完就退出的那一局会把「陈旧关卡」带到这里，被误结算成战役进度。
            if (!_campaignBattleRequested && Manager.HasPendingLevel)
                Manager.AbortLevel();

            _campaignBattleRequested = false;
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

            // 非战役入口（主菜单直接进战斗 / 2P）没有待结算关卡 → 不结算。
            if (!Manager.HasPendingLevel)
                return;

            bool cleared = finished.Outcome == CampaignManager.PlayerWinOutcome;
            var result = new CampaignResult(
                cleared: cleared,
                playerDeaths: _playerDeaths,
                chestsCollected: 0,   // 宝箱未实装（见类头边界 3）
                chestsTotal: 0,
                score: finished.Score);

            if (!Manager.TrySettle(result, out CampaignSettlement settlement))
                return;

            // 船员奖励：给本关编成阵容发经验，并按「已通关的最大关卡序号」招募新船员。
            // 用最大序号而非本关序号，是为了重打旧关时也能补上漏掉的招募门槛。
            CrewRewardPayload reward = CrewManagementApi.GrantLevelReward(
                settlement.LevelId, settlement.Stars, Progress.MaxCompletedLevelNumber);

            LastSettlement = settlement;
            LastReward = reward;

            EventBus.Publish(CampaignEvents.LevelCompleted, new CampaignLevelCompletedPayload(
                settlement.LevelId, settlement.LevelNumber, settlement.Chapter,
                settlement.Stars, settlement.Cleared, settlement.FirstClear, settlement.Score));

            SaveProgress();
        }

        // ------------------------------------------------------------------
        // 存档
        // ------------------------------------------------------------------

        /// <summary>
        /// 把「战役进度 + 船员名册/经验」写进同一个存档数据对象（纯 C#，可无头断言）。
        /// </summary>
        public static void WriteTo(SaveData data)
        {
            CampaignSaveCodec.Write(data, Progress);
            CrewManagementApi.WriteTo(data);
        }

        /// <summary>从存档数据恢复「战役进度 + 船员名册/经验」。</summary>
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
        /// 从 <see cref="ProgressSlot"/> 读档。没有实例或槽位不存在时返回 false（保持现状，不报错）。
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
            _campaignBattleRequested = false;
            LastSettlement = null;
            LastReward = null;
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
