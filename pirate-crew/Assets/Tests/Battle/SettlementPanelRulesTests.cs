using System.Collections.Generic;
using NUnit.Framework;

namespace PirateCrew.UI.Tests
{
    /// <summary>
    /// <see cref="SettlementPanelRules"/> 纯规则测试（无头验证台可跑：不碰 MonoBehaviour/UI）。
    ///
    /// 【为什么值得测】"结算面板显示哪几行"原来是 <c>BattleHud.ShowSettlement</c> 里的一串 <c>if</c>，
    /// 只有跑起来、开一局战役、看面板才知道对不对。抽成纯规则后，**行集合与顺序**都被断言钉住——
    /// 顺序尤其重要（评分 → 关卡 / 星级 / 经验 / 招募 / 首通 这条顺序按旧实现逐字保持，
    /// 改动会改变玩家看到的信息排列）。
    /// </summary>
    [TestFixture]
    public class SettlementPanelRulesTests
    {
        static SettlementPanelRules.PanelInput Input(
            int score = 0,
            bool campaignBattle = false,
            bool hasSettlement = false,
            int stars = 0,
            bool firstClear = false,
            bool hasReward = false,
            int xpPerCrew = 0,
            int unlockedCrewCount = 0)
        {
            return new SettlementPanelRules.PanelInput(
                score, campaignBattle, hasSettlement, stars, firstClear, hasReward, xpPerCrew, unlockedCrewCount);
        }

        [Test]
        public void NonCampaign_WithScore_ShowsOnlyScoreRow()
        {
            List<SettlementPanelRules.RowKind> rows = SettlementPanelRules.RowsFor(Input(score: 120));

            CollectionAssert.AreEqual(new[] { SettlementPanelRules.RowKind.Score }, rows);
        }

        [Test]
        public void NonCampaign_WithoutScore_ShowsNothing()
        {
            Assert.AreEqual(0, SettlementPanelRules.RowsFor(Input(score: 0)).Count);
            Assert.AreEqual(0, SettlementPanelRules.RowsFor(Input(score: -5)).Count);
        }

        [Test]
        public void CampaignBattle_WithoutSettlementData_ShowsOnlyScore()
        {
            // 战役局但结算载荷还没到（CampaignApi.LastSettlement == null）：不能凭"是战役局"就显示明细，
            // 否则会渲染出空关卡名/0 颗星的半成品行。
            var input = Input(score: 80, campaignBattle: true, hasSettlement: false, stars: 3, firstClear: true);

            CollectionAssert.AreEqual(new[] { SettlementPanelRules.RowKind.Score }, SettlementPanelRules.RowsFor(input));
            Assert.AreEqual(0, SettlementPanelRules.LitStarsFor(input));
        }

        [Test]
        public void CampaignBattle_WithSettlement_ShowsLevelAndStars()
        {
            var input = Input(campaignBattle: true, hasSettlement: true, stars: 2);

            CollectionAssert.AreEqual(
                new[] { SettlementPanelRules.RowKind.Level, SettlementPanelRules.RowKind.Stars },
                SettlementPanelRules.RowsFor(input));
            Assert.AreEqual(2, SettlementPanelRules.LitStarsFor(input));
        }

        [Test]
        public void ScoreRow_ComesFirst_CampaignRowsFollow()
        {
            var input = Input(score: 300, campaignBattle: true, hasSettlement: true, stars: 3, firstClear: true,
                hasReward: true, xpPerCrew: 25, unlockedCrewCount: 2);

            CollectionAssert.AreEqual(
                new[]
                {
                    SettlementPanelRules.RowKind.Score,      // 旧实现把评分加在战役块之前 → 顺序不可变
                    SettlementPanelRules.RowKind.Level,
                    SettlementPanelRules.RowKind.Stars,
                    SettlementPanelRules.RowKind.Xp,
                    SettlementPanelRules.RowKind.Unlock,
                    SettlementPanelRules.RowKind.FirstClear,
                },
                SettlementPanelRules.RowsFor(input));
        }

        [TestCase(0, false, TestName = "XpRow_HiddenWhenZero")]
        [TestCase(-3, false, TestName = "XpRow_HiddenWhenNegative")]
        [TestCase(25, true, TestName = "XpRow_ShownWhenPositive")]
        public void XpRow_DependsOnRewardXp(int xpPerCrew, bool expected)
        {
            var input = Input(campaignBattle: true, hasSettlement: true, hasReward: true, xpPerCrew: xpPerCrew);

            Assert.AreEqual(expected, SettlementPanelRules.RowsFor(input).Contains(SettlementPanelRules.RowKind.Xp));
        }

        [TestCase(0, false, TestName = "UnlockRow_HiddenWhenNoCrewUnlocked")]
        [TestCase(3, true, TestName = "UnlockRow_ShownWhenCrewUnlocked")]
        public void UnlockRow_DependsOnUnlockedCount(int unlockedCrewCount, bool expected)
        {
            var input = Input(campaignBattle: true, hasSettlement: true,
                hasReward: true, unlockedCrewCount: unlockedCrewCount);

            Assert.AreEqual(expected, SettlementPanelRules.RowsFor(input).Contains(SettlementPanelRules.RowKind.Unlock));
        }

        [Test]
        public void RewardRows_HiddenWhenNoRewardPayload()
        {
            // 没有奖励载荷时，即便经验/解锁计数字段被填了也不显示（避免"数据串台"渲染出错误行）。
            var input = Input(campaignBattle: true, hasSettlement: true,
                hasReward: false, xpPerCrew: 40, unlockedCrewCount: 5);

            List<SettlementPanelRules.RowKind> rows = SettlementPanelRules.RowsFor(input);
            CollectionAssert.DoesNotContain(rows, SettlementPanelRules.RowKind.Xp);
            CollectionAssert.DoesNotContain(rows, SettlementPanelRules.RowKind.Unlock);
        }

        [Test]
        public void FirstClearRow_IsLast()
        {
            var input = Input(campaignBattle: true, hasSettlement: true, stars: 3, firstClear: true);

            List<SettlementPanelRules.RowKind> rows = SettlementPanelRules.RowsFor(input);
            Assert.AreEqual(SettlementPanelRules.RowKind.FirstClear, rows[rows.Count - 1]);
        }

        [Test]
        public void LitStars_ZeroForNonCampaign_EvenWhenStarsProvided()
        {
            Assert.AreEqual(0, SettlementPanelRules.LitStarsFor(Input(stars: 3)));
        }

        [Test]
        public void ShowCampaignRows_RequiresBothFlags()
        {
            Assert.IsFalse(SettlementPanelRules.ShowCampaignRows(Input(campaignBattle: true)));
            Assert.IsFalse(SettlementPanelRules.ShowCampaignRows(Input(hasSettlement: true)));
            Assert.IsTrue(SettlementPanelRules.ShowCampaignRows(Input(campaignBattle: true, hasSettlement: true)));
        }

        [Test]
        public void RowsFor_ReturnsNewListEachCall()
        {
            // HUD 每局都调它拼字符串；返回共享静态表会被上一次结算污染（元组化复用是常见事故）。
            var input = Input(score: 10);
            List<SettlementPanelRules.RowKind> first = SettlementPanelRules.RowsFor(input);

            first.Add(SettlementPanelRules.RowKind.FirstClear);

            Assert.AreEqual(1, SettlementPanelRules.RowsFor(input).Count);
        }
    }
}
