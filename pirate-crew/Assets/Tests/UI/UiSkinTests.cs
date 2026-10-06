using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Data;
using PirateCrew.UI;
using UnityEngine;

namespace PirateCrew.Tests.UI
{
    /// <summary>
    /// <see cref="UiSkin"/> 的无头断言：内容语义色（武器/职业）覆盖率 + 字号/几何档位锁。
    /// （纯 Color 数学，不触碰 GameObject，可在 harness 无头跑。旧底色系 WCAG 门禁随
    /// StickTokens 遗留层根除退役——现役观感取色走 theme 对拍。）
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
        public void CrewColor_KnownBattleSymbols_NonFallback()
        {
            // §4.2 导出符号 → 职业短名（UI 独立映射；3D 外观侧已塌缩为单一档，
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
        public void FontScale_FreeSized_ReadabilityTable()
        {
            // 【裁决 2026-09-24】原生档纪律：字号只取像素字体原生设计档——有什么字号
            // 做什么字号，没有的档不硬凑、绝不放大（12px 烘 24/36 = 翻倍，被否决）。
            // 现役四档：16 正格点黑16 / 12、10、8 缝合像素（cmap 实测全过，见 FontAssetBuilder）。
            // 层级靠颜色与留白表达，不靠字号。
            Assert.AreEqual(16, UiSkin.Font.Display);
            Assert.AreEqual(16, UiSkin.Font.Banner);
            Assert.AreEqual(16, UiSkin.Font.Title);
            Assert.AreEqual(12, UiSkin.Font.Section);
            Assert.AreEqual(12, UiSkin.Font.Hud);
            Assert.AreEqual(12, UiSkin.Font.Body);
            Assert.AreEqual(10, UiSkin.Font.Hint);
            Assert.AreEqual(8, UiSkin.Font.Tiny);
        }

        [Test]
        public void Px_TokenGeometry_IsOnArtPixelGrid()
        {
            // 像素几何令牌（×1 终局：1 设计格 = 1 贴图像素 = 1 画布像素）。
            // 条 10 = 凹槽描边 1+1 + 槽底 8；按钮渲染高 24 = 正文档 12 + theme button
            // 上下切片 10 + 2（参考库 OK 钮实测）；头像 16、小地图 64（2026-09-24 收档）。
            Assert.AreEqual(24, UiSkin.Px.Button);
            Assert.AreEqual(10, UiSkin.Px.Bar);
            Assert.AreEqual(8, UiSkin.Px.ButtonPadX);
            Assert.AreEqual(4, UiSkin.Px.PanelPadding);
            Assert.AreEqual(6, UiSkin.Px.Pip);
            Assert.AreEqual(12, UiSkin.Px.Ring);
            Assert.AreEqual(16, UiSkin.Px.Avatar);
            Assert.AreEqual(64, UiSkin.Px.Minimap);

            // 按钮宽 = 标签宽（按 CJK 逐字 × 正文档 12）+ 8，两字起步。
            Assert.AreEqual(32, UiSkin.Px.ButtonWidth("确定"));
            Assert.AreEqual(68, UiSkin.Px.ButtonWidth("返回主菜单"));
        }
    }
}
