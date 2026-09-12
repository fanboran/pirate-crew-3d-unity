namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役推进器（纯 C#，翻译并扩展 Godot <c>modules/campaign/scripts/campaign_manager.gd</c>）。
    ///
    /// 【对应关系】Godot <c>get_chapter_levels</c> → <see cref="CampaignCatalog.GetChapter"/>、
    ///   <c>get_current_chapter</c> → <see cref="CurrentChapter"/>。
    ///   Godot 的 <c>get_level_config_path</c>（<c>res://config/levels/*.json</c>）在 Unity 版无对应物——
    ///   关卡数值走 <c>PirateCrew.PirateCrew.Data.LevelCatalog</c> / <c>LevelDefinition</c> 资产。
    ///
    /// 【职责】持有 <see cref="Progress"/>（星级与解锁）、记录「已选但未结算」的关卡、
    ///   把一次战斗结果折算成 <see cref="CampaignSettlement"/>。不含 UI 与事件（那是 <see cref="CampaignApi"/> 的事）。
    /// </summary>
    public sealed class CampaignManager
    {
        /// <summary>
        /// 通关的 <c>BattleEvents.MatchFinished</c> 结果码
        /// （= <c>PirateCrew.PirateCrew.Combat.MatchOutcome</c> 的 <c>Team0Win = 0</c>，§3.3）。
        /// </summary>
        public const int PlayerWinOutcome = 0;

        /// <summary>关卡星级与解锁进度。</summary>
        public CampaignProgress Progress { get; } = new CampaignProgress();

        /// <summary>当前章节（1–3，对应 Godot <c>get_current_chapter</c>；选关时同步）。</summary>
        public int CurrentChapter { get; set; } = 1;

        /// <summary>已选定、等待进战斗结算的关卡 id；无则 null。</summary>
        public string PendingLevelId { get; private set; }

        /// <summary>是否有待结算关卡。</summary>
        public bool HasPendingLevel => !string.IsNullOrEmpty(PendingLevelId);

        /// <summary>
        /// 选定关卡（校验存在 + 已解锁），并记录为待结算。
        /// </summary>
        /// <returns>成功返回 true；关卡 id 非法或未解锁返回 false（不改变当前待结算状态）。</returns>
        public bool TrySelectLevel(string levelId)
        {
            if (!CampaignCatalog.TryGet(levelId, out CampaignLevel level))
                return false;

            if (!Progress.IsUnlocked(levelId))
                return false;

            PendingLevelId = levelId;
            CurrentChapter = level.Chapter;
            return true;
        }

        /// <summary>
        /// 结算当前待挑战关卡：评价星级、写入进度、清空待结算状态。
        /// </summary>
        /// <param name="result">战斗结果（胜负 / 阵亡 / 宝箱 / 得分）。</param>
        /// <param name="settlement">结算结果（失败时 <c>default</c>）。</param>
        /// <returns>有待结算关卡并完成结算返回 true；没有待结算关卡返回 false。</returns>
        public bool TrySettle(CampaignResult result, out CampaignSettlement settlement)
        {
            settlement = default;
            if (!HasPendingLevel)
                return false;

            CampaignLevel level = CampaignCatalog.Get(PendingLevelId);
            int stars = result.Stars;
            int previousStars = Progress.GetStars(level.LevelId);

            bool firstClear = result.Cleared && previousStars <= 0;
            bool improved = Progress.CompleteLevel(level.LevelId, stars);

            settlement = new CampaignSettlement(
                levelId: level.LevelId,
                levelNumber: level.LevelNumber,
                chapter: level.Chapter,
                cleared: result.Cleared,
                stars: stars,
                firstClear: firstClear,
                improved: improved,
                score: result.Score);

            PendingLevelId = null;
            return true;
        }

        /// <summary>放弃当前待挑战关卡（返回主菜单等场景用），不计进度。</summary>
        public void AbortLevel()
        {
            PendingLevelId = null;
        }

        /// <summary>清空全部战役状态（重开档 / 测试用）。</summary>
        public void Reset()
        {
            Progress.Reset();
            CurrentChapter = 1;
            PendingLevelId = null;
        }
    }
}
