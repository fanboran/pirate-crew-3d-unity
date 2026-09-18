using System;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 关卡星级评价（纯 C# 静态规则）。
    ///
    /// 【出处】Godot 设计文档 <c>../game-3/docs/gdd.md</c> §9.3「星级评价」：
    ///   ⭐ 通关 / ⭐⭐ 通关 + 阵亡 ≤ 1 人 / ⭐⭐⭐ 通关 + 全员存活 + 拾取所有宝箱。
    ///   ⚠ 该表是 **3D 重制设计文档**，不是 Flash 逆向结论——原版只有得分与关卡解锁，没有星级
    ///   （<c>docs/参考游戏逆向-海盗军团抢宝藏-静态.md</c> §7.3）。故本规则整体标注为**提案/待定**。
    ///
    /// 【宝箱项的处理】M2 没有实现宝箱/空投拾取（只有 <c>LevelDefinition.MaxChests</c> 数据字段），
    ///   因此调用方目前传 <c>chestsTotal = 0</c>；此时「拾取所有宝箱」恒真，
    ///   3★ 退化为「通关 + 全员存活」。等宝箱系统实装后只需把真实值传进来，规则本身不用改。
    /// </summary>
    public static class StarRules
    {
        /// <summary>最高星级。</summary>
        public const int MaxStars = 3;

        /// <summary>2★ 允许的阵亡上限（GDD §9.3：「阵亡 ≤ 1 人」）。</summary>
        public const int TwoStarMaxDeaths = 1;

        /// <summary>
        /// 评价本关星级。
        /// </summary>
        /// <param name="cleared">是否通关（未通关返回 0 星）。</param>
        /// <param name="playerDeaths">玩家方（红队）阵亡人数。</param>
        /// <param name="chestsCollected">已拾取宝箱数。</param>
        /// <param name="chestsTotal">本关宝箱总数（未实装时传 0）。</param>
        public static int Evaluate(bool cleared, int playerDeaths, int chestsCollected, int chestsTotal)
        {
            if (!cleared)
                return 0;

            int deaths = Math.Max(0, playerDeaths);
            int collected = Math.Max(0, chestsCollected);
            int total = Math.Max(0, chestsTotal);

            if (deaths == 0 && collected >= total)
                return 3;

            if (deaths <= TwoStarMaxDeaths)
                return 2;

            return 1;
        }
    }

    /// <summary>
    /// 一次关卡结算的输入（来自 <c>BattleEvents.MatchFinished</c> 与战斗期间累计的阵亡数）。
    /// </summary>
    public readonly struct CampaignResult
    {
        /// <summary>是否通关。</summary>
        public readonly bool Cleared;

        /// <summary>玩家方阵亡人数（订阅 <c>crew_died</c> 累计，TeamIndex == 0）。</summary>
        public readonly int PlayerDeaths;

        /// <summary>已拾取宝箱数（宝箱未实装，当前恒 0）。</summary>
        public readonly int ChestsCollected;

        /// <summary>本关宝箱总数（未实装，当前恒 0）。</summary>
        public readonly int ChestsTotal;

        /// <summary>1P 关卡得分（2P 为 0）。</summary>
        public readonly int Score;

        public CampaignResult(bool cleared, int playerDeaths, int chestsCollected, int chestsTotal, int score)
        {
            Cleared = cleared;
            PlayerDeaths = playerDeaths;
            ChestsCollected = chestsCollected;
            ChestsTotal = chestsTotal;
            Score = score;
        }

        /// <summary>按 GDD §9.3 评价星级。</summary>
        public int Stars => StarRules.Evaluate(Cleared, PlayerDeaths, ChestsCollected, ChestsTotal);
    }

    /// <summary>
    /// 一场海图战结算的结果（战役侧；船员奖励见 <c>CrewManagement.CrewRewardPayload</c>）。
    /// </summary>
    public readonly struct CampaignSettlement
    {
        /// <summary>海图 id（<c>WorldMapCatalog</c> 收录，如 <c>wreck_hymn</c>）。</summary>
        public readonly string MapId;

        /// <summary>是否通关。</summary>
        public readonly bool Cleared;

        /// <summary>星级（0–3）。</summary>
        public readonly int Stars;

        /// <summary>是否首次通关。</summary>
        public readonly bool FirstClear;

        /// <summary>是否刷新了星级记录（首次通关或星级提高）。</summary>
        public readonly bool Improved;

        /// <summary>1P 得分。</summary>
        public readonly int Score;

        public CampaignSettlement(string mapId,
            bool cleared, int stars, bool firstClear, bool improved, int score)
        {
            MapId = mapId;
            Cleared = cleared;
            Stars = stars;
            FirstClear = firstClear;
            Improved = improved;
            Score = score;
        }
    }
}
