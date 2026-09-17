using System;
using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Campaign
{
    /// <summary>
    /// 战役中的一个关卡（纯 C# 结构）。
    /// </summary>
    public readonly struct CampaignLevel
    {
        /// <summary>关卡 id（Godot <c>campaign_manager.gd</c> 的 <c>level_01</c> 形式）。</summary>
        public readonly string LevelId;

        /// <summary>章节号（1–3）。</summary>
        public readonly int Chapter;

        /// <summary>章节内序号（1–5）。</summary>
        public readonly int IndexInChapter;

        /// <summary>
        /// 战役内全局序号（1–15）。与 1P 原版关卡序号（§7.2：1P 战役 1–15）一致，
        /// 因此可直接作为 <c>LevelCatalog.Get(levelNumber)</c> 的入参。
        /// </summary>
        public readonly int LevelNumber;

        /// <summary>UI 显示名（如「第 1 关」）。</summary>
        public readonly string DisplayName;

        public CampaignLevel(string levelId, int chapter, int indexInChapter, int levelNumber, string displayName)
        {
            LevelId = levelId;
            Chapter = chapter;
            IndexInChapter = indexInChapter;
            LevelNumber = levelNumber;
            DisplayName = displayName;
        }

        /// <summary>
        /// 该关卡的数据是否已转写进 <c>LevelCatalog</c>（33 关已全部转写，1–15 恒为 true）。
        /// <b>注意</b>：这只表示「数值数据存在」；Battle 场景按
        /// <c>CampaignApi.PendingBattleLevelNumberOr</c> 注入的所选关卡号取数据，
        /// 无待战关卡的非战役局回落第 1 关（<c>BattleController.fallbackLevelNumber</c>）。
        /// </summary>
        public bool HasData => LevelCatalog.IsTranscribed(LevelNumber);
    }

    /// <summary>
    /// 战役关卡目录（真值来源，纯 C# 静态类）。
    ///
    /// 【出处】结构照抄 Godot <c>modules/campaign/scripts/campaign_manager.gd:13-17</c> 的
    ///   <c>CHAPTERS</c>：3 章节 × 5 关 = <c>level_01</c>…<c>level_15</c>。
    ///   与 Flash 逆向文档 §7.2 的「1P 战役 1–15 关」吻合（16–33 是 2P 面板，不在战役内）。
    ///
    /// 【为什么不列出 16–33】原版 2P 的 16–33 关在引导时直接全解锁、不写关卡进度
    ///   （§7.2 notes：`for i=16..33: ng.setLevelUnlocked(i)`），不属于战役推进链。
    /// </summary>
    public static class CampaignCatalog
    {
        /// <summary>章节数（Godot CHAPTERS 的键数）。</summary>
        public const int ChapterCount = 3;

        /// <summary>每章关卡数（Godot CHAPTERS 每项 5 个）。</summary>
        public const int LevelsPerChapter = 5;

        /// <summary>战役关卡总数（3 × 5 = 15）。</summary>
        public const int TotalLevels = ChapterCount * LevelsPerChapter;

        static readonly CampaignLevel[] _all = BuildAll();

        /// <summary>全部关卡（按序号升序）。</summary>
        public static IReadOnlyList<CampaignLevel> All => _all;

        /// <summary>关卡 id 是否合法。</summary>
        public static bool TryGet(string levelId, out CampaignLevel level)
        {
            if (!string.IsNullOrEmpty(levelId))
            {
                for (int i = 0; i < _all.Length; i++)
                {
                    if (string.Equals(_all[i].LevelId, levelId, StringComparison.Ordinal))
                    {
                        level = _all[i];
                        return true;
                    }
                }
            }

            level = default;
            return false;
        }

        /// <summary>按 id 取关卡；不存在抛 <see cref="KeyNotFoundException"/>（与 LevelCatalog.Get 同风格）。</summary>
        public static CampaignLevel Get(string levelId)
        {
            if (TryGet(levelId, out CampaignLevel level))
                return level;

            throw new KeyNotFoundException("CampaignCatalog 不存在关卡: " + levelId);
        }

        /// <summary>章节内的关卡列表；章节号越界返回空列表（对应 Godot <c>get_chapter_levels</c> 的 <c>.get(chapter, [])</c>）。</summary>
        public static IReadOnlyList<CampaignLevel> GetChapter(int chapter)
        {
            var levels = new List<CampaignLevel>();
            for (int i = 0; i < _all.Length; i++)
            {
                if (_all[i].Chapter == chapter)
                    levels.Add(_all[i]);
            }

            return levels;
        }

        /// <summary>序号 → 关卡 id（对应 Godot 的 <c>"level_%02d" % n</c>）。序号越界返回 null。</summary>
        public static string LevelIdFor(int levelNumber)
        {
            if (levelNumber < 1 || levelNumber > TotalLevels)
                return null;

            return _all[levelNumber - 1].LevelId;
        }

        /// <summary>
        /// 序号小于 <paramref name="levelNumber"/> 的最后一个「已转写数据」的关卡；没有则返回 null。
        ///
        /// 【用途】顺序解锁的「前一关」判定（<c>CampaignProgress.IsUnlocked</c>）。
        /// 33 关已全部转写（<c>LevelCatalog.IsTranscribed</c> 对 1–33 恒真），
        /// 现状下本方法就是「前一序号关」；保留「跳过缺数据关卡」的扫描写法，
        /// 将来若个别关卡号缺数据，解锁链会自动绕开缺口而不是断链。
        /// </summary>
        public static CampaignLevel? PreviousImplementedLevel(int levelNumber)
        {
            for (int i = levelNumber - 2; i >= 0; i--)
            {
                if (_all[i].HasData)
                    return _all[i];
            }

            return null;
        }

        static CampaignLevel[] BuildAll()
        {
            var levels = new CampaignLevel[TotalLevels];
            for (int index = 0; index < TotalLevels; index++)
            {
                int levelNumber = index + 1;
                int chapter = index / LevelsPerChapter + 1;
                int indexInChapter = index % LevelsPerChapter + 1;

                levels[index] = new CampaignLevel(
                    levelId: FormatLevelId(levelNumber),
                    chapter: chapter,
                    indexInChapter: indexInChapter,
                    levelNumber: levelNumber,
                    displayName: "第 " + levelNumber + " 关");
            }

            return levels;
        }

        static string FormatLevelId(int levelNumber)
        {
            return "level_" + levelNumber.ToString("00");
        }
    }
}
