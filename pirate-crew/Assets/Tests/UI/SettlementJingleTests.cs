using NUnit.Framework;
using PirateCrew.PirateCrew.Audio;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;

namespace PirateCrew.PirateCrew.UI.Tests
{
    /// <summary>
    /// 结算乐句口径一致性（审计 代码审计报告 §一.4 + 测试缺口 1）：
    /// HUD 的乐句决策（<see cref="BattleHud.SettlementJingleFor"/>）必须与音频层唯一真值
    /// <see cref="AudioEventMapper.MusicForMatchOutcome"/> 对全部 (outcome, team1IsAi) 组合完全一致——
    /// 2P 热座蓝队（玩家 2）获胜时<b>不放任何乐句</b>，不得回落「非红队胜即失败」的旧旁路。
    /// </summary>
    [TestFixture]
    public class SettlementJingleTests
    {
        [Test]
        public void AllOutcomeCombinations_MatchAudioMapperVerdict()
        {
            foreach (MatchOutcome outcome in System.Enum.GetValues(typeof(MatchOutcome)))
            {
                foreach (bool team1IsAi in new[] { false, true })
                {
                    bool hudPlays = BattleHud.SettlementJingleFor((int)outcome, team1IsAi, out SfxId hudJingle);
                    bool mapperPlays = AudioEventMapper.MusicForMatchOutcome(outcome, team1IsAi, out SfxId mapperJingle);

                    Assert.AreEqual(mapperPlays, hudPlays,
                        $"({outcome}, team1IsAi={team1IsAi}) 是否播乐句不一致");
                    if (mapperPlays)
                        Assert.AreEqual(mapperJingle, hudJingle,
                            $"({outcome}, team1IsAi={team1IsAi}) 乐句选择不一致");
                }
            }
        }

        [Test]
        public void HotseatBlueWin_PlaysNothing()
        {
            // 回归钉：2P 热座蓝队胜（Team1Win 且非 AI）——任何乐句都错，静默 + 面板开合音。
            bool plays = BattleHud.SettlementJingleFor((int)MatchOutcome.Team1Win, team1IsAi: false, out _);
            Assert.IsFalse(plays);
        }

        [Test]
        public void SoloPlayerWin_PlaysVictory()
        {
            bool plays = BattleHud.SettlementJingleFor((int)MatchOutcome.Team0Win, team1IsAi: true, out SfxId jingle);
            Assert.IsTrue(plays);
            Assert.AreEqual(SfxId.VictoryJingle, jingle);
        }

        [Test]
        public void SoloAiWin_PlaysDefeat()
        {
            bool plays = BattleHud.SettlementJingleFor((int)MatchOutcome.Team1Win, team1IsAi: true, out SfxId jingle);
            Assert.IsTrue(plays);
            Assert.AreEqual(SfxId.DefeatJingle, jingle);
        }
    }
}
