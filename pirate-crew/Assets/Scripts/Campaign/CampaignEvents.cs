using PirateCrew.Core;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役模块（Campaign）的 EventBus 事件频道集中声明。
    ///
    /// 【约定】跨模块通信只走 <c>PirateCrew.Core.EventBus</c> 的类型化频道；
    ///         本类是战役事件的唯一声明处，人读登记表在 <c>docs/技术/架构/EventBus事件契约.md</c>。
    ///
    /// 【一代退场后的契约】一代选关链事件随一代选关链删除；海图结算事件
    /// <see cref="MapCompleted"/> 载荷里的关卡序号/章节字段已删除（海图没有序号与章节），
    /// id 值域为海图 id。
    /// </summary>
    public static class CampaignEvents
    {
        /// <summary>一场海图战结算完成（载荷 <see cref="CampaignMapCompletedPayload"/>）。</summary>
        public static readonly Event<CampaignMapCompletedPayload> MapCompleted = new();
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
