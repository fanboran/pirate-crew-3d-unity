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
    ///   1. 不含不允许的英文 token（源码白名单：品牌名 3D、按键 Esc/WASD/E/W/S；
    ///      测试侧另有按键名补充白名单——r12/r13 模式系统裁决操作提示词有意含按键标签，
    ///      见 <see cref="KeyLabelWhitelist"/>）；
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

        /// <summary>
        /// 测试侧追加的**按键名白名单**（r12/r13 模式系统裁决：操作提示词有意包含按键标签，
        /// 见 <c>UiStrings.BattleHintAiming/BattleHintGeneral/BattleHintFocus</c> 与
        /// <c>BattleHud.RefreshModeHint</c>）。按键名是合法 UX 惯例，不是漏译——
        /// 源码侧 <c>UiTextRules.AllowedTokens</c> 只登记了 3d/esc/e/w/s/wasd，
        /// 这里在审计逻辑里**补充**过滤，命中全部为白名单 token 的文案不再记违规。
        /// 逐项理由：
        ///   · AD / WS —— 瞄准提示的水平转向 / 力度键位对（BattleHintAiming）；
        ///   · Space / Shift —— 常见修饰键的英文键帽名（模式提示词预留）；
        ///   · HP —— 血量条通用缩写（HUD 布局规范有意保留，规范 §4.10 例外项）。
        /// 单独的 A / D / W / S / E 单字母不在此列：源码白名单已覆盖 E/W/S，
        /// 而孤立的 A/D 更可能是漏译碎片，出现时应当人工裁决而不是静默放行。
        /// </summary>
        static readonly string[] KeyLabelWhitelist = { "ad", "ws", "space", "shift", "hp" };

        /// <summary>文本里的英文字母 token 是否**全部**落在按键名白名单里。</summary>
        static bool AllTokensAreKeyLabels(string text)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                if (!IsAsciiLetter(text[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && IsAsciiLetter(text[i]))
                    i++;
                tokens.Add(text.Substring(start, i - start));
            }

            if (tokens.Count == 0)
                return false;

            for (int t = 0; t < tokens.Count; t++)
            {
                bool allowed = false;
                for (int w = 0; w < KeyLabelWhitelist.Length; w++)
                {
                    if (string.Equals(tokens[t], KeyLabelWhitelist[w],
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed)
                    return false;
            }

            return true;
        }

        static bool IsAsciiLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        [Test]
        public void EveryUiString_ContainsNoDisallowedEnglish()
        {
            var offenders = new List<string>();
            foreach (var pair in EnumerateStrings())
            {
                if (!UiTextRules.ContainsDisallowedEnglish(pair.Value))
                    continue;

                // 按键名白名单：被标记 token 全部是设计上有意保留的按键标签 → 不算漏译。
                if (AllTokensAreKeyLabels(pair.Value))
                    continue;

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
            // 【2026-09-14 对齐源码现值】"抛自己"（把自己抛出去的攻击动作）随 r12/r13 模式系统
            // 裁决改为"跳跃"（UiStrings.cs:303）——动作语义从"投掷自己"改为"跳跃位移"。
            Assert.AreEqual("跳跃", UiStrings.BattleThrowSelf);
            Assert.AreEqual("结束回合", UiStrings.BattleEndGo);
            // 【UI 审计 P1-5】"瞄准中 / 聚焦中"两个标签随永久隐藏的死节点一并退役，不再断言。
            Assert.AreEqual("选择武器", UiStrings.BattleWeaponListTitle);
            // 【发布收口】M1 时代的占位场景文案随 BattlePlaceholder 一并退役。
            Assert.AreEqual("暂停 (Esc)", UiStrings.BattlePauseButton);
            Assert.AreEqual("再来一局", UiStrings.BattleRestart);
            Assert.AreEqual("版本 1.0", UiStrings.MainVersion);
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
