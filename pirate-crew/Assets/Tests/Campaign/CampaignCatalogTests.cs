using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Campaign;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 战役关卡目录的纯 C# 断言。
    /// 【基准】Godot <c>modules/campaign/scripts/campaign_manager.gd:13-17</c> 的 <c>CHAPTERS</c>：
    ///   3 章 × 5 关 = <c>level_01</c>…<c>level_15</c>，与 Flash 逆向 §7.2「1P 战役 1–15 关」一致。
    /// </summary>
    public class CampaignCatalogTests
    {
        [Test]
        public void Catalog_HasThreeChaptersOfFive()
        {
            Assert.That(CampaignCatalog.ChapterCount, Is.EqualTo(3));
            Assert.That(CampaignCatalog.LevelsPerChapter, Is.EqualTo(5));
            Assert.That(CampaignCatalog.TotalLevels, Is.EqualTo(15));
            Assert.That(CampaignCatalog.All.Count, Is.EqualTo(15));
        }

        [Test]
        public void Catalog_LevelIdsFollowGodotFormat()
        {
            Assert.That(CampaignCatalog.All[0].LevelId, Is.EqualTo("level_01"));
            Assert.That(CampaignCatalog.All[14].LevelId, Is.EqualTo("level_15"));
            Assert.That(CampaignCatalog.LevelIdFor(7), Is.EqualTo("level_07"));
            Assert.That(CampaignCatalog.LevelIdFor(0), Is.Null);
            Assert.That(CampaignCatalog.LevelIdFor(16), Is.Null);
        }

        [Test]
        public void Catalog_ChapterAndIndexAreConsistent()
        {
            foreach (CampaignLevel level in CampaignCatalog.All)
            {
                Assert.That(level.Chapter, Is.InRange(1, CampaignCatalog.ChapterCount));
                Assert.That(level.IndexInChapter, Is.InRange(1, CampaignCatalog.LevelsPerChapter));
                Assert.That((level.Chapter - 1) * CampaignCatalog.LevelsPerChapter + level.IndexInChapter,
                    Is.EqualTo(level.LevelNumber), "章节/序号换算应与全局序号自洽");
            }
        }

        [Test]
        public void GetChapter_ReturnsThatChaptersLevels()
        {
            IReadOnlyList<CampaignLevel> chapter2 = CampaignCatalog.GetChapter(2);

            Assert.That(chapter2.Count, Is.EqualTo(5));
            Assert.That(chapter2[0].LevelId, Is.EqualTo("level_06"));
            Assert.That(chapter2[4].LevelId, Is.EqualTo("level_10"));
            Assert.That(CampaignCatalog.GetChapter(4), Is.Empty, "越界章节返回空（Godot .get(chapter, [])）");
        }

        [Test]
        public void Get_ThrowsOnUnknownId()
        {
            Assert.Throws<KeyNotFoundException>(() => CampaignCatalog.Get("level_99"));
        }

        [Test]
        public void HasData_TracksLevelCatalogTranscriptions()
        {
            // 33 关已全量转写（2026-09-14，levels_all.json 逐字段对账）。
            Assert.That(CampaignCatalog.Get("level_01").HasData, Is.True);
            Assert.That(CampaignCatalog.Get("level_04").HasData, Is.True);
            Assert.That(CampaignCatalog.Get("level_02").HasData, Is.True);

            Assert.That(LevelCatalog.IsTranscribed(1), Is.True);
            Assert.That(LevelCatalog.IsTranscribed(4), Is.True);
        }

        [Test]
        public void PreviousImplementedLevel_SkipsUntranscribedLevels()
        {
            // 33 关全量转写后解锁链为严格顺序：N 的前一关就是 N-1。
            Assert.That(CampaignCatalog.PreviousImplementedLevel(1), Is.Null, "第 1 关没有前一关");
            Assert.That(CampaignCatalog.PreviousImplementedLevel(2).Value.LevelId, Is.EqualTo("level_01"));
            Assert.That(CampaignCatalog.PreviousImplementedLevel(4).Value.LevelId, Is.EqualTo("level_03"));
            Assert.That(CampaignCatalog.PreviousImplementedLevel(6).Value.LevelId, Is.EqualTo("level_05"));
        }
    }
}
