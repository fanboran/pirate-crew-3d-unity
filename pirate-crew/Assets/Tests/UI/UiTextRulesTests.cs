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
    ///
    /// 【文案内容不进测试契约】具体文案字面不做逐条锁定——文案属设计迭代面，
    /// 改字不应触发测试红行（创始人裁决 2026-10-01）。规则性判据已够用：
    /// 非空 / 无不允许英文 / 参数往返（喂什么拼什么）、覆盖全枚举，
    /// 逐用例见各自注记；仅存的字面比较都在「旧英文串回归」与门禁哨兵（如回落值）上。
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
        public void WeaponName_MineCodexPairIsSingleSource()
        {
            // 「水雷」两表收敛为单源的裁决钉子（文案字面已按文件头裁决退役，防线改判据制）。
            // 来历：武器中文名曾有独立两张表——UiTextRules.WeaponName 与图鉴自持短名表
            //（UiGalleryPage.WeaponShortName）漂移成两套（「地雷」vs「水雷」），
            // 2026-09-23 文案审计按 UiSkin.WeaponColor(Mine) 行内注的裁决语义收敛为
            // UiTextRules 单源，图鉴侧改为直接调用本函数（短名表随后随旧画廊退役）。
            // 现存形态的单源防线：Mine 的名字与图鉴 hover 说明必须成对出自 UiTextRules、
            // 双双过「非空 + 无不允许英文」门禁——任一侧漏条目或再漂出英文即红。
            string name = UiTextRules.WeaponName(WeaponId.Mine);
            string codexDescription = UiTextRules.WeaponDescription(WeaponId.Mine);

            Assert.IsFalse(string.IsNullOrEmpty(name),
                "水雷中文名缺失——武器名单源（UiTextRules.WeaponName）漏条目");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(name),
                "水雷中文名混入英文——单源表被污染");
            Assert.IsFalse(string.IsNullOrEmpty(codexDescription),
                "水雷图鉴说明缺失——名字/说明应成对出自单源");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(codexDescription),
                "水雷图鉴说明混入英文");
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

            // 未知符号回落中文名，不得回显英文（回落值措辞按文件头裁决不锁）。
            string unknownFallback = UiTextRules.CrewNameByBattleSymbol("someUnknownSymbol");
            Assert.IsFalse(string.IsNullOrEmpty(unknownFallback), "未知符号应回落中文名而非空串");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(unknownFallback), "未知符号回落值混入英文");

            string nullFallback = UiTextRules.CrewNameByBattleSymbol(null);
            Assert.IsFalse(string.IsNullOrEmpty(nullFallback), "null 符号应回落中文名而非空串");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(nullFallback), "null 符号回落值混入英文");
        }

        // ------------------------------------------------------------------
        // 战斗 HUD 文案
        // ------------------------------------------------------------------

        // 【文案内容不进测试契约】TurnHint 的三条逐字断言退役（创始人裁决 2026-10-01）：
        // TurnHint 只是 UiStrings.BattleTurnAi / BattleTurnYouFormat 两常量的二选一拼接，
        // 措辞由 UiStringsTests.EveryUiString_* 反射门禁兜底，这里再锁字面即纯重复。

        // 【名册文案家族已随左下名册退役】TeamStatus / RosterRow / RosterTitle /
        // WeaponPanelTitle / TurnCounter / Alive 的断言一并删除（图标优先裁决）。

        [Test]
        public void Percent_Format()
        {
            Assert.AreEqual(79, UiTextRules.Percent(0.786f));
            Assert.AreEqual(0, UiTextRules.Percent(-1f));
            Assert.AreEqual(100, UiTextRules.Percent(2f));
            // StrengthPercent/StrengthLabel 的模板措辞按文件头裁决不锁——
            // 它们只是 UiStrings 模板 + Percent 数值的拼接，数值正确性由上面三条钉住。
        }

        // 【文案内容不进测试契约】ModeLabel 的两条逐字断言退役（创始人裁决 2026-10-01）：
        // ModeLabel 只是 UiStrings.BattleModeMove / BattleModeAction 的二选一，
        // 措辞由 UiStringsTests.EveryUiString_* 反射门禁兜底。

        // ------------------------------------------------------------------
        // 结算（§4.7）
        // ------------------------------------------------------------------

        [Test]
        public void OutcomeTitle_MapsAllOutcomesToChinese()
        {
            // 判据（文件头裁决：字面不锁）：全部结局枚举 × 1P/2P 两态，标题非空且无不允许英文；
            // 枚举再增新值时循环自动纳入覆盖。
            var values = (MatchOutcome[])Enum.GetValues(typeof(MatchOutcome));
            bool[] aiStates = { true, false };

            var offenders = new System.Collections.Generic.List<string>();
            for (int i = 0; i < values.Length; i++)
            {
                for (int j = 0; j < aiStates.Length; j++)
                {
                    string title = UiTextRules.OutcomeTitle(values[i], aiStates[j]);
                    if (string.IsNullOrEmpty(title) || UiTextRules.ContainsDisallowedEnglish(title))
                        offenders.Add(values[i] + "(ai=" + aiStates[j] + ") → \"" + title + "\"");
                }
            }

            Assert.IsNotEmpty(values, "MatchOutcome 枚举不应为空");
            Assert.IsEmpty(offenders, "结局标题缺失/含英文：\n" + string.Join("\n", offenders));
        }

        [Test]
        public void SettlementRows_FormatParamsAndStayChinese()
        {
            // 判据（文件头裁决：字面不锁）：结算行非空、无不允许英文、且吃进传入参数
            //（数字/名称原样出现——模板漏拼参数即红；具体模板措辞归 UiStrings 门禁）。
            string level = UiTextRules.SettlementLevel("第 3 关");
            Assert.IsFalse(string.IsNullOrEmpty(level), "结算「关卡」行为空");
            Assert.That(level, Does.Contain("第 3 关"), "结算「关卡」行未吃进关卡名");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(level), "结算「关卡」行混入英文");

            string score = UiTextRules.SettlementScore(1250);
            Assert.IsFalse(string.IsNullOrEmpty(score), "结算「评分」行为空");
            Assert.That(score, Does.Contain("1250"), "结算「评分」行未吃进分数");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(score), "结算「评分」行混入英文");

            string stars = UiTextRules.SettlementStars(2, 3);
            Assert.IsFalse(string.IsNullOrEmpty(stars), "结算「星级」行为空");
            Assert.That(stars, Does.Contain("2").And.Contains("3"), "结算「星级」行未吃进星数");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(stars), "结算「星级」行混入英文");

            string unlock = UiTextRules.SettlementUnlock("炮手");
            Assert.IsFalse(string.IsNullOrEmpty(unlock), "结算「新招募」行为空");
            Assert.That(unlock, Does.Contain("炮手"), "结算「新招募」行未吃进招募名");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(unlock), "结算「新招募」行混入英文");
        }

        // ------------------------------------------------------------------
        // 船员管理 / 选关（§4.3 / §4.4）
        // ------------------------------------------------------------------

        [Test]
        public void CrewRows_FormatParamsAndStayChinese()
        {
            string locked = UiTextRules.CrewRowLocked("狙击手", 5);
            Assert.IsFalse(string.IsNullOrEmpty(locked), "未解锁行为空");
            Assert.That(locked, Does.Contain("狙击手").And.Contains("5"),
                "未解锁行未吃进名字/解锁等级");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(locked), "未解锁行混入英文");
        }

        [Test]
        public void LevelAndChapterNames_FormatParamsAndStayChinese()
        {
            // 判据（文件头裁决：字面不锁）：关名/章名非空、无不允许英文；
            // 序号与越界回落都吃进章节数字（模板漏拼参数即红）。
            string level = UiTextRules.LevelName(3);
            Assert.IsFalse(string.IsNullOrEmpty(level), "关卡名为空");
            Assert.That(level, Does.Contain("3"), "关卡名未吃进序号");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(level), "关卡名混入英文");

            string chapter1 = UiTextRules.ChapterName(1);
            Assert.IsFalse(string.IsNullOrEmpty(chapter1), "第 1 章名为空");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(chapter1), "第 1 章名混入英文");

            string chapter3 = UiTextRules.ChapterName(3);
            Assert.IsFalse(string.IsNullOrEmpty(chapter3), "第 3 章名为空");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(chapter3), "第 3 章名混入英文");

            string chapterFallback = UiTextRules.ChapterName(9);
            Assert.IsFalse(string.IsNullOrEmpty(chapterFallback), "越界章名回落为空");
            Assert.That(chapterFallback, Does.Contain("9"), "越界章名回落未吃进章节数字");
            Assert.IsFalse(UiTextRules.ContainsDisallowedEnglish(chapterFallback), "越界章名回落混入英文");
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
