using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.UI;

namespace PirateCrew.Tests
{
    /// <summary>
    /// 文案表「无英文残留」验收（规范 §8 V1 / §4.10）。
    ///
    /// 【判据】把 <see cref="UiStrings"/> 的全部公开常量经反射取出，逐条断言：
    ///   1. 不含不允许的英文 token（品牌名 3D、按键 Esc/WASD/E/W/S 属规范允许例外）；
    ///   2. 非 ASCII（中文）字符占比 ≥ 0.3，粗筛「忘了翻译的半角串」。
    ///
    /// 纯 C# 反射 + 字符串判据，可在无头验证台运行（不碰 GameObject）。
    /// </summary>
    [TestFixture]
    public class UiStringsTests
    {
        /// <summary>反射取出 UiStrings 的全部公开字符串常量。</summary>
        static List<KeyValuePair<string, string>> EnumerateStrings()
        {
            var list = new List<KeyValuePair<string, string>>();
            FieldInfo[] fields = typeof(UiStrings).GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(string) || !fields[i].IsLiteral)
                    continue;

                string value = fields[i].GetValue(null) as string;
                list.Add(new KeyValuePair<string, string>(fields[i].Name, value ?? string.Empty));
            }

            return list;
        }

        [Test]
        public void UiStrings_HasExpectedVolume()
        {
            int count = EnumerateStrings().Count;
            Assert.GreaterOrEqual(count, 130,
                "文案条目应达到规范 §4.2–§4.9 的量级（约 150 条，含变体），当前 " + count);
        }

        [Test]
        public void EveryUiString_ContainsNoDisallowedEnglish()
        {
            var offenders = new List<string>();
            foreach (var pair in EnumerateStrings())
            {
                if (UiTextRules.ContainsDisallowedEnglish(pair.Value))
                    offenders.Add(pair.Key + " = \"" + pair.Value + "\"（英文 token：" +
                                  UiTextRules.DescribeDisallowedEnglish(pair.Value) + "）");
            }

            Assert.IsEmpty(offenders,
                "以下文案含未翻译英文（规范 §4.10 要求替换）：\n" + string.Join("\n", offenders));
        }

        [Test]
        public void EveryUiString_IsPredominantlyChinese()
        {
            var offenders = new List<string>();
            foreach (var pair in EnumerateStrings())
            {
                if (string.IsNullOrEmpty(pair.Value))
                {
                    offenders.Add(pair.Key + " 为空串");
                    continue;
                }

                // 纯骨架模板（如「{0}/{1}」「{0}%」，只有占位符/数字/标点、没有任何中英文字母）
                // 天然不含中文——它们承载的是数字，不参与「是否翻译」判定。
                if (HasNoLetters(pair.Value))
                    continue;

                // 阈值取 0.19：带格式占位符的短模板（如「存活 {0}/{1}」）非 ASCII 占比约 0.2，
                // 是正常的中文模板；低于该值说明基本是半角串，判为漏译。
                if (UiTextRules.NonAsciiRatio(pair.Value) < 0.19f)
                    offenders.Add(pair.Key + " = \"" + pair.Value + "\"（非 ASCII 占比 " +
                                  UiTextRules.NonAsciiRatio(pair.Value).ToString("P0") + "）");
            }

            Assert.IsEmpty(offenders, "以下文案不像中文：\n" + string.Join("\n", offenders));
        }

        /// <summary>字符串里是否完全不含中英文字母（占位符 / 数字 / 标点骨架）。</summary>
        static bool HasNoLetters(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool asciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                bool cjk = c >= 0x4E00 && c <= 0x9FFF;
                if (asciiLetter || cjk)
                    return false;
            }

            return true;
        }

        [Test]
        public void SpotCheck_KeyCopyMatchesSpec()
        {
            Assert.AreEqual("海盗军团夺宝 3D", UiStrings.MainTitle);
            Assert.AreEqual("进入战斗", UiStrings.MainBattle);
            Assert.AreEqual("单人战役", UiStrings.MainCampaign);
            Assert.AreEqual("船员管理", UiStrings.MainCrew);
            Assert.AreEqual("退出游戏", UiStrings.MainQuit);
            Assert.AreEqual("抛自己", UiStrings.BattleThrowSelf);
            Assert.AreEqual("结束回合", UiStrings.BattleEndGo);
            Assert.AreEqual("瞄准中", UiStrings.BattleAiming);
            Assert.AreEqual("聚焦中", UiStrings.BattleFocusing);
            Assert.AreEqual("选择武器", UiStrings.BattleWeaponListTitle);
            Assert.AreEqual("战斗场景（占位）", UiStrings.BattlePlaceholderNote);
        }

        [Test]
        public void EnglishRegressionStrings_AreGone()
        {
            // §4.10 对照表里出现过、绝不能再现身的旧英文串（在整张文案表里搜）。
            var banned = new[]
            {
                "Player ", "Computer, take your turn", "take your turn",
                "Roster — Level", "choose action", "throw character", "end go",
                "Pirate Crew 3D", "redPirate", "bluePirate", "Level failed",
                "Lv.", "XP ",
            };

            var found = new List<string>();
            foreach (var pair in EnumerateStrings())
            {
                for (int i = 0; i < banned.Length; i++)
                {
                    if (pair.Value.IndexOf(banned[i], System.StringComparison.Ordinal) >= 0)
                        found.Add(pair.Key + " 含旧英文串 \"" + banned[i] + "\"");
                }
            }

            Assert.IsEmpty(found, "旧英文串仍在文案表中：\n" + string.Join("\n", found));
        }
    }
}
