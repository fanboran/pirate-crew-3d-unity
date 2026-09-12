namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役模块（Campaign）的 EventBus 事件契约集中登记。
    ///
    /// 【约定】跨模块通信只走 <c>PirateCrew.Core.EventBus</c> 的字符串事件；
    ///         事件名一律 snake_case，禁止在业务代码里散落魔法字符串。
    ///         本类是战役事件的唯一登记处，事件名与载荷已登记在 <c>docs/EventBus事件契约.md</c>。
    ///
    /// 【与 Godot 的对应】Godot <c>modules/campaign/api.gd</c> 的信号
    ///   <c>level_started(level_id)</c> → <see cref="LevelSelected"/>（本作把「已选关」与「进战场」的时机合一：
    ///   发布后紧接着请求切到 Battle 场景）；信号 <c>level_completed(level_id, stars)</c> → <see cref="LevelCompleted"/>。
    /// </summary>
    public static class CampaignEvents
    {
        /// <summary>玩家选定关卡、即将进入战斗（载荷 <see cref="CampaignLevelSelectedPayload"/>）。</summary>
        public const string LevelSelected = "campaign_level_selected";

        /// <summary>关卡结算完成（载荷 <see cref="CampaignLevelCompletedPayload"/>）。</summary>
        public const string LevelCompleted = "campaign_level_completed";
    }

    /// <summary><see cref="CampaignEvents.LevelSelected"/> 载荷。</summary>
    public readonly struct CampaignLevelSelectedPayload
    {
        /// <summary>关卡 id（<c>level_01</c> 形式）。</summary>
        public readonly string LevelId;

        /// <summary>战役内全局序号（1–15）。</summary>
        public readonly int LevelNumber;

        /// <summary>章节号（1–3）。</summary>
        public readonly int Chapter;

        public CampaignLevelSelectedPayload(string levelId, int levelNumber, int chapter)
        {
            LevelId = levelId;
            LevelNumber = levelNumber;
            Chapter = chapter;
        }
    }

    /// <summary><see cref="CampaignEvents.LevelCompleted"/> 载荷（对应 Godot signal <c>level_completed(level_id, stars)</c>）。</summary>
    public readonly struct CampaignLevelCompletedPayload
    {
        /// <summary>关卡 id。</summary>
        public readonly string LevelId;

        /// <summary>战役内全局序号（1–15）。</summary>
        public readonly int LevelNumber;

        /// <summary>章节号（1–3）。</summary>
        public readonly int Chapter;

        /// <summary>本关星级（0 = 挑战失败；1–3 见 <see cref="StarRules"/>）。</summary>
        public readonly int Stars;

        /// <summary>是否通关。</summary>
        public readonly bool Cleared;

        /// <summary>本次是否首次通关（星级此前为 0）。</summary>
        public readonly bool FirstClear;

        /// <summary>1P 关卡得分（来自 <c>BattleEvents.MatchFinished</c> 载荷；2P 为 0）。</summary>
        public readonly int Score;

        public CampaignLevelCompletedPayload(string levelId, int levelNumber, int chapter,
            int stars, bool cleared, bool firstClear, int score)
        {
            LevelId = levelId;
            LevelNumber = levelNumber;
            Chapter = chapter;
            Stars = stars;
            Cleared = cleared;
            FirstClear = firstClear;
            Score = score;
        }
    }
}
