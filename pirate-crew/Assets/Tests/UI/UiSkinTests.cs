using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;
using PirateCrew.UI;
using PirateCrew.UI.Stick;
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
            // P1 令牌接管后底色 = StickTokens.WINDOW_BG 系（"窗户"黑玻璃），断言仍走
            // InkDeep/InkSoft 语义名——换血只换值不改名，此处数值按 tokens RGB 重算。
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TextOnInk, UiSkin.InkDeep), 4.5f, "正文白(=TEXT)");
            // TEXT_DIM 是 55% 透明档：ContrastRatio 不吃 alpha，须先混到底色再断言
            // （渲染时透明度才生效），否则对比度虚高。
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(BlendedOver(UiSkin.TextDim, UiSkin.InkDeep), UiSkin.InkDeep), 4.5f,
                "次级文字(=TEXT_DIM)混底后有效对比");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.Gold, UiSkin.InkDeep), 4.5f, "强调琥珀(=ACCENT)");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TeamRedText, UiSkin.InkDeep), 4.5f, "红队文字");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TeamBlueText, UiSkin.InkDeep), 4.5f, "蓝队文字");
            Assert.GreaterOrEqual(UiSkin.ContrastRatio(UiSkin.TextOnInk, UiSkin.InkSoft), 4.5f, "正文压次级底(=WINDOW_BG_LIGHT)");
        }

        [Test]
        public void PanelBaseColors_AreStickTokensPipelined()
        {
            // P1 令牌接管回归锁：底色系必须继续取自 StickTokens（防手改回魔法值、
            // 与 stick-world ui_tokens.json 漂移）。武器/职业/队色是 pirate 玩法语义，不在本锁内。
            Assert.AreEqual(StickTokens.WINDOW_BG, UiSkin.InkDeep, "面板底");
            Assert.AreEqual(StickTokens.WINDOW_BG_LIGHT, UiSkin.InkSoft, "次级面板底");
            Assert.AreEqual(StickTokens.TEXT, UiSkin.TextOnInk, "正文");
            Assert.AreEqual(StickTokens.TEXT_DIM, UiSkin.TextDim, "次级文字");
            Assert.AreEqual(StickTokens.ACCENT, UiSkin.Gold, "强调");
            Assert.AreEqual(StickTokens.INK, UiSkin.InkOnGold, "强调底深字");
            Assert.AreEqual(StickTokens.GROOVE_BG, UiSkin.BarTrackInk, "凹槽底");
            Assert.AreEqual(StickTokens.INFO, UiSkin.Info, "语义·信息");
            Assert.AreEqual(StickTokens.WARN, UiSkin.Warn, "语义·警告");
            Assert.AreEqual(StickTokens.SUCCESS, UiSkin.Success, "语义·成功");
        }

        /// <summary>半透明前景按 alpha 混到不透明底上（sRGB 直混，与渲染合成同式）。</summary>
        static Color BlendedOver(Color foreground, Color background)
        {
            return new Color(
                foreground.r * foreground.a + background.r * (1f - foreground.a),
                foreground.g * foreground.a + background.g * (1f - foreground.a),
                foreground.b * foreground.a + background.b * (1f - foreground.a),
                1f);
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
            // §4.2 导出符号 → 职业短名（与 3D 外观侧 CrewVisualCatalog.ProfessionFromBattleSymbol
            // 同源；r9 出图事故回归锁：cabinBoy 曾落 unknown → 钢灰底+舵轮占位）。
            Assert.AreEqual("sniper", UiSkin.CrewKey("cabinBoy"));
            Assert.AreEqual("captain", UiSkin.CrewKey("cabinBoyCaptain"));
            Assert.AreEqual("gunner", UiSkin.CrewKey("soldier"));
            Assert.AreEqual("hooker", UiSkin.CrewKey("blindPirate"));
            Assert.AreEqual("arsonist", UiSkin.CrewKey("oldPirate"));
            Assert.AreEqual("arsonist", UiSkin.CrewKey("rainbowBeard"));
            Assert.AreEqual("skeleton", UiSkin.CrewKey("skeletonPirate"));
            Assert.AreEqual("captain", UiSkin.CrewKey("bossGuy"));
            Assert.AreEqual("sailor", UiSkin.CrewKey("tribe"));
            Assert.AreEqual("sailor", UiSkin.CrewKey("__unknown__"), "无法识别回落 sailor（与外观侧同口径）");

            // 已知符号（含 §4.2 全族）不得再出现 unknown 钢灰兜底色。
            Color steel = UiSkin.Rgb(0x8A, 0x97, 0xA8);
            string[] symbols = { "redPirate", "bluePirate", "gunner", "sniper", "hooker", "arsonist",
                "skeleton", "redPirateCaptain", "bluePirateCaptain", "cabinBoy", "soldier",
                "blindPirate", "oldPirate", "rainbowBeard", "skeletonPirate", "bossGuy" };
            foreach (string symbol in symbols)
                Assert.AreNotEqual(steel, UiSkin.CrewColor(symbol), "职业符号 {0} 落到了兜底色", symbol);
        }

        [Test]
        public void ContrastRatio_BlackOnWhite_IsTwentyOne()
        {
            Assert.AreEqual(21f, UiSkin.ContrastRatio(Color.black, Color.white), 0.01f);
        }

        [Test]
        public void FontScale_MatchesUserRuling_UpTwoSteps()
        {
            // 用户 2026-09-20 裁决"文字可读性很差"：八档整体上调（Tiny 不低于 15——
            // 手写体小字可读性下限）。字号跟控件走的纪律见 UiSkin.Font 注释。
            Assert.AreEqual(52, UiSkin.Font.Display);
            Assert.AreEqual(42, UiSkin.Font.Banner);
            Assert.AreEqual(30, UiSkin.Font.Title);
            Assert.AreEqual(24, UiSkin.Font.Section);
            Assert.AreEqual(22, UiSkin.Font.Hud);
            Assert.AreEqual(18, UiSkin.Font.Body);
            Assert.AreEqual(16, UiSkin.Font.Hint);
            Assert.AreEqual(15, UiSkin.Font.Tiny);
        }
    }
}
