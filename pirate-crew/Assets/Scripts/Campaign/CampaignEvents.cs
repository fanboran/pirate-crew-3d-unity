namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役模块（Campaign）的 EventBus 事件契约集中登记。
    ///
    /// 【约定】跨模块通信只走 <c>PirateCrew.Core.EventBus</c> 的字符串事件；
    ///         事件名一律 snake_case，禁止在业务代码里散落魔法字符串。
    ///         本类是战役事件的唯一登记处，事件名与载荷已登记在 <c>docs/EventBus事件契约.md</c>。
    ///
    /// 【一代退场后的契约】<c>campaign_level_selected</c> 随一代选关链删除；
    /// <c>campaign_level_completed</c> 更名 <see cref="MapCompleted"/>，载荷里的
    /// 关卡序号/章节字段删除（海图没有序号与章节），id 值域变为海图 id。
    /// </summary>
    public static class CampaignEvents
    {
        /// <summary>一场海图战结算完成（载荷 <see cref="CampaignMapCompletedPayload"/>）。</summary>
        public const string MapCompleted = "campaign_map_completed";
    }

    /// <summary><see cref="CampaignEvents.MapCompleted"/> 载荷。</summary>
    public readonly struct CampaignMapCompletedPayload
    {
        /// <summary>海图 id（<c>WorldMapCatalog</c> 收录，如 <c>wreck_hymn</c>）。</summary>
        public readonly string MapId;

        /// <summary>本局星级（0 = 挑战失败；1–3 见 <see cref="StarRules"/>）。</summary>
        public readonly int Stars;

        /// <summary>是否通关。</summary>
        public readonly bool Cleared;

        /// <summary>本次是否首次通关（星级此前为 0）。</summary>
        public readonly bool FirstClear;

        /// <summary>1P 得分（来自 <c>BattleEvents.MatchFinished</c> 载荷；2P 为 0）。</summary>
        public readonly int Score;

        public CampaignMapCompletedPayload(string mapId, int stars, bool cleared, bool firstClear, int score)
        {
            MapId = mapId;
            Stars = stars;
            Cleared = cleared;
            FirstClear = firstClear;
            Score = score;
        }
    }
}
