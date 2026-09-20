using System;
using NUnit.Framework;
using PirateCrew.CrewManagement;
using PirateCrew.Combat;
using PirateCrew.Data;
using PirateCrew.UI;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 文案格式化规则与武器/职业中文名的纯逻辑测试（规范 §4.5–§4.7、§4.10）。
    /// 全部不碰 GameObject，无头验证台可跑。
    /// </summary>
    [TestFixture]
    public class UiTextRulesTests
    {
        // ------------------------------------------------------------------
        // 武器中文名（规范 §4.6）
        // ------------------------------------------------------------------

        [Test]
        public void WeaponName_CoversAll17AndIsChinese()
        {
            var values = (WeaponId[])Enum.GetValues(typeof(WeaponId));
            Assert.AreEqual(17, values.Length, "WeaponId 应为 17 件武器");

            var offenders = new System.Collections.Generic.List<string>();
            for (int i = 0; i < values.Length; i++)
            {
                string name = UiTextRules.WeaponName(values[i]);
                if (string.IsNullOrEmpty(name)
                    || name == "未知武器"
                    || UiTextRules.ContainsDisallowedEnglish(name))
                {
                    offenders.Add(values[i] + " → \"" + name + "\"");
                }
            }

            Assert.IsEmpty(offenders, "武器中文名缺失/含英文：\n" + string.Join("\n", offenders));
        }

        [Test]
        public void WeaponName_SpotCheckSpec()
        {
            Assert.AreEqual("炮弹", UiTextRules.WeaponName(WeaponId.Cannonball));
            Assert.AreEqual("樱桃炸弹", UiTextRules.WeaponName(WeaponId.CherryBomb));
            Assert.AreEqual("降落伞炸弹", UiTextRules.WeaponName(WeaponId.ParachuteBomb));
            Assert.AreEqual("八枚金币", UiTextRules.WeaponName(WeaponId.PiecesOfEight));
            Assert.AreEqual("潮汐巨浪", UiTextRules.WeaponName(WeaponId.TidalWave));
            Assert.AreEqual("扫射火焰", UiTextRules.WeaponName(WeaponId.SweepingFlame));
        }

        [Test]
        public void WeaponDescription_CoversAll17AndIsChinese()
        {
            var values = (WeaponId[])Enum.GetValues(typeof(WeaponId));
            var offenders = new System.Collections.Generic.List<string>();
            for (int i = 0; i < values.Length; i++)
            {
                string desc = UiTextRules.WeaponDescription(values[i]);
                if (string.IsNullOrEmpty(desc) || UiTextRules.ContainsDisallowedEnglish(desc))
                    offenders.Add(values[i] + " → \"" + desc + "\"");
            }

            Assert.IsEmpty(offenders, "武器中文说明缺失/含英文：\n" + string.Join("\n", offenders));
        }

        // ------------------------------------------------------------------
        // 职业名反查（§4.10 第 9 条）
        // ------------------------------------------------------------------

        [Test]
        public void CrewNameByBattleSymbol_MapsExportSymbolsToChinese()
        {
            var all = CrewRosterCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                string name = UiTextRules.CrewNameByBattleSymbol(all[i].BattleSymbol);
                Assert.AreEqual(all[i].DisplayName, name,
                    all[i].BattleSymbol + " 应反查为 " + all[i].DisplayName);
                Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(name));
            }

            // 未知符号不得回显英文。
            Assert.AreEqual("海盗", UiTextRules.CrewNameByBattleSymbol("someUnknownSymbol"));
            Assert.AreEqual("海盗", UiTextRules.CrewNameByBattleSymbol(null));
        }

        // ------------------------------------------------------------------
        // 战斗 HUD 文案
        // ------------------------------------------------------------------

        [Test]
        public void TurnHint_IsChineseForBothSides()
        {
            Assert.AreEqual("玩家 1，该你了", UiTextRules.TurnHint(false, 1));
            Assert.AreEqual("玩家 2，该你了", UiTextRules.TurnHint(false, 2));
            Assert.AreEqual("电脑回合，行动中……", UiTextRules.TurnHint(true, 2));
        }

        // 【名册文案家族已随左下名册退役】TeamStatus / RosterRow / RosterTitle /
        // WeaponPanelTitle / TurnCounter / Alive 的断言一并删除（图标优先裁决）。

        [Test]
        public void Percent_Format()
        {
            Assert.AreEqual(79, UiTextRules.Percent(0.786f));
            Assert.AreEqual(0, UiTextRules.Percent(-1f));
            Assert.AreEqual(100, UiTextRules.Percent(2f));
            Assert.AreEqual("79%", UiTextRules.StrengthPercent(0.786f));
            Assert.AreEqual("力度 79%", UiTextRules.StrengthLabel(0.786f));
        }

        [Test]
        public void ModeLabels()
        {
            Assert.AreEqual("移动", UiTextRules.ModeLabel(true));
            Assert.AreEqual("操作", UiTextRules.ModeLabel(false));
        }

        // ------------------------------------------------------------------
        // 结算（§4.7）
        // ------------------------------------------------------------------

        [Test]
        public void OutcomeTitle_MapsAllOutcomesToChinese()
        {
            Assert.AreEqual("胜 利", UiTextRules.OutcomeTitle(MatchOutcome.Team0Win, true));
            Assert.AreEqual("玩家 1 获胜", UiTextRules.OutcomeTitle(MatchOutcome.Team0Win, false));
            Assert.AreEqual("玩家 2 获胜", UiTextRules.OutcomeTitle(MatchOutcome.Team1Win, true));
            Assert.AreEqual("平 局", UiTextRules.OutcomeTitle(MatchOutcome.Draw, true));
            Assert.AreEqual("挑战失败", UiTextRules.OutcomeTitle(MatchOutcome.LevelFailed, true));
        }

        [Test]
        public void SettlementRows_AreChinese()
        {
            Assert.AreEqual("关卡　第 3 关", UiTextRules.SettlementLevel("第 3 关"));
            Assert.AreEqual("评分　1250", UiTextRules.SettlementScore(1250));
            Assert.AreEqual("星级　2/3", UiTextRules.SettlementStars(2, 3));
            Assert.AreEqual("每人经验　+40", UiTextRules.SettlementXp(40));
            Assert.AreEqual("新招募　炮手", UiTextRules.SettlementUnlock("炮手"));
        }

        // ------------------------------------------------------------------
        // 船员管理 / 选关（§4.3 / §4.4）
        // ------------------------------------------------------------------

        [Test]
        public void CrewRows_AreChinese()
        {
            Assert.AreEqual("水手　等级 3　经验 120", UiTextRules.CrewRow("水手", 3, 120));
            Assert.AreEqual("狙击手　（累计 5 星后招募）", UiTextRules.CrewRowLocked("狙击手", 5));
        }

        [Test]
        public void LevelAndChapterNames_AreChinese()
        {
            Assert.AreEqual("第 3 关", UiTextRules.LevelName(3));
            Assert.AreEqual("第一章 · 加勒比新手海域", UiTextRules.ChapterName(1));
            Assert.AreEqual("第三章 · 传奇宝藏", UiTextRules.ChapterName(3));
            Assert.AreEqual("第 9 章", UiTextRules.ChapterName(9));
        }

        // ------------------------------------------------------------------
        // 英文扫描器自身（判据可信度）
        // ------------------------------------------------------------------

        [Test]
        public void EnglishScanner_FlagsOldStringsButAllowsExemptions()
        {
            Assert.IsTrue(UiTextRules.ContainsDisallowedEnglish("Player 1 wins!"));
            Assert.IsTrue(UiTextRules.ContainsDisallowedEnglish("Roster — Level 3"));
            Assert.IsTrue(UiTextRules.ContainsDisallowedEnglish("Lv.3 XP 120"));

            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish("海盗军团夺宝 3D"));
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish("空格 瞄准　E 聚焦　Esc 取消"));
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish("版本 0.1"));
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish("已经没有了 100%"));
        }

        [Test]
        public void NonAsciiRatio_Behaves()
        {
            Assert.AreEqual(0f, UiTextRules.NonAsciiRatio("ABC123"));
            Assert.AreEqual(1f, UiTextRules.NonAsciiRatio("海盗军团"), 1e-4f);
        }
    }
}
