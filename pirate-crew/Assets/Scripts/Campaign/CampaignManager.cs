namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役推进器（纯 C#）。一代退场后不再持有自己的关卡目录——
    /// 「选哪张图」由 <c>WorldMapRuntime</c> 待战通道决定，本类只负责
    /// 「记下本局要结算哪张海图」与「把一次战斗结果折算成 <see cref="CampaignSettlement"/>」。
    /// 不含 UI 与事件（那是 <see cref="CampaignApi"/> 的事）。
    /// </summary>
    public sealed class CampaignManager
    {
        /// <summary>
        /// 通关的 <c>BattleEvents.MatchFinished</c> 结果码
        /// （= <c>PirateCrew.Combat.MatchOutcome</c> 的 <c>Team0Win = 0</c>，§3.3）。
        /// </summary>
        public const int PlayerWinOutcome = 0;

        /// <summary>海图星级与结算进度。</summary>
        public CampaignProgress Progress { get; } = new CampaignProgress();

        /// <summary>已选定、等待进战斗结算的海图 id；无则 null。</summary>
        public string PendingMapId { get; private set; }

        /// <summary>是否有待结算海图。</summary>
        public bool HasPendingMap => !string.IsNullOrEmpty(PendingMapId);

        /// <summary>
        /// 记录本局要结算的海图（id 合法性由 <c>WorldMapCatalog</c> 在待战入口校验过）。
        /// </summary>
        public void SelectMap(string mapId)
        {
            PendingMapId = string.IsNullOrEmpty(mapId) ? null : mapId;
        }

        /// <summary>
        /// 结算当前待挑战海图：评价星级、写入进度、清空待结算状态。
        /// </summary>
        /// <param name="result">战斗结果（胜负 / 阵亡 / 宝箱 / 得分）。</param>
        /// <param name="settlement">结算结果（失败时 <c>default</c>）。</param>
        /// <returns>有待结算海图并完成结算返回 true；没有待结算海图返回 false。</returns>
        public bool TrySettle(CampaignResult result, out CampaignSettlement settlement)
        {
            settlement = default;
            if (!HasPendingMap)
                return false;

            string mapId = PendingMapId;
            int stars = result.Stars;
            int previousStars = Progress.GetStars(mapId);

            bool firstClear = result.Cleared && previousStars <= 0;
            bool improved = Progress.CompleteLevel(mapId, stars);

            settlement = new CampaignSettlement(
                mapId: mapId,
                cleared: result.Cleared,
                stars: stars,
                firstClear: firstClear,
                improved: improved,
                score: result.Score);

            PendingMapId = null;
            return true;
        }

        /// <summary>放弃当前待结算海图（不计进度）。</summary>
        public void AbortLevel()
        {
            PendingMapId = null;
        }

        /// <summary>清空全部战役状态（重开档 / 测试用）。</summary>
        public void Reset()
        {
            Progress.Reset();
            PendingMapId = null;
        }
    }
}
