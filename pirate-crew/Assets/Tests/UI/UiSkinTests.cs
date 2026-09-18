using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.PirateCrew.Data;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests.UI
{
    /// <summary>
    /// <see cref="UiSkin"/> 设计 Token 的无头断言：语义色槽覆盖率 + WCAG 对比度硬门禁。
    /// （纯 Color 数学，不触碰 GameObject，可在 harness 无头跑。）
    /// </summary>
    public sealed class UiSkinTests
    {
        [Test]
        public void WeaponColor_CoversAllSeventeenWeapons_NonFallback()
        {
            var seen = new HashSet<Color>();
            for (int i = 0; i <= 16; i++)
            {
                Color color = UiSkin.WeaponColor((WeaponId)i);
                Color fallback = UiSkin.WeaponColor((WeaponId)(-1));
                Assert.AreNotEqual(fallback, color, "武器 {0} 落到了兜底钢灰色", (WeaponId)i);
                Assert.IsTrue(seen.Add(color), "武器 {0} 的色相与其他武器重复", (WeaponId)i);
            }
        }

        [Test]
        public void WeaponColor_AllSaturatedEnough_ToReadAsColorful()
        {
            // 多彩观感的量化下限：HSV 饱和度 ≥0.28（亮金类最低也在 0.7 附近，余量充足）。
            for (int i = 0; i <= 16; i++)
            {
                Color c = UiSkin.WeaponColor((WeaponId)i);
                Color.RGBToHSV(c, out _, out float s, out _);
                Assert.GreaterOrEqual(s, 0.28f, "武器 {0} 饱和度过低（{1:F2}），多彩风格要求每格都有自己的色相",
                    (WeaponId)i, s);
            }
        }

        [Test]
        public void TextColors_OnInkDeepPanel_MeetWcagAA()
        {
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TextOnInk, UiSkin.InkDeep), 4.5f, "正文暖白");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TextDim, UiSkin.InkDeep), 4.5f, "次级柔米");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.Gold, UiSkin.InkDeep), 4.5f, "强调金");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TeamRedText, UiSkin.InkDeep), 4.5f, "红队文字");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TeamBlueText, UiSkin.InkDeep), 4.5f, "蓝队文字");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TextOnInk, UiSkin.InkSoft), 4.5f, "暖白压亮档底");
        }

        [Test]
        public void TeamFill_GraphicsOnlyColors_StayAtLeastThreeToOne()
        {
            // 队色本体是图形色（血条段 / 徽章环 / 点位），WCAG 1.4.11 非文字下限 3:1。
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TeamRed, UiSkin.InkDeep), 3f, "红队图形色");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TeamBlue, UiSkin.InkDeep), 3f, "蓝队图形色");
        }

        [Test]
        public void InkOnGold_OnGoldChip_MeetsWcagAA()
        {
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.InkOnGold, UiSkin.Gold), 4.5f, "金底深字");
        }

        [Test]
        public void CrewColor_KnownBattleSymbols_NonFallback()
        {
            string[] symbols = { "redPirate", "bluePirate", "gunner", "sniper", "hooker", "arsonist", "skeleton",
                "redPirateCaptain", "bluePirateCaptain" };
            Color fallback = UiSkin.CrewColor("__unknown__");
            foreach (string symbol in symbols)
                Assert.AreNotEqual(fallback, UiSkin.CrewColor(symbol), "职业符号 {0} 落到了兜底色", symbol);
        }

        [Test]
        public void ContrastRatio_BlackOnWhite_IsTwentyOne()
        {
            Assert.AreEqual(21f, UiSkin.ContrastRatio(Color.black, Color.white), 0.01f);
        }

        [Test]
        public void FontScale_MatchesUserRuling_DownOneStep()
        {
            // 用户 2026-09-14 裁决的渲染档（原 UiTheme 旧档整体降一档）。
            Assert.AreEqual(48, UiSkin.Font.Display);
            Assert.AreEqual(36, UiSkin.Font.Banner);
            Assert.AreEqual(26, UiSkin.Font.Title);
            Assert.AreEqual(20, UiSkin.Font.Section);
            Assert.AreEqual(18, UiSkin.Font.Hud);
            Assert.AreEqual(15, UiSkin.Font.Body);
            Assert.AreEqual(14, UiSkin.Font.Hint);
            Assert.AreEqual(13, UiSkin.Font.Tiny);
        }
    }
}
