using System.Text;
using PirateCrew.CrewManagement;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;

namespace PirateCrew.UI
{
    /// <summary>
    /// UI 文案格式化规则（**纯 C# 静态类**，不引用 UnityEngine / MonoBehaviour）。
    ///
    /// 【职责】
    ///   · 把 <see cref="UiStrings"/> 的模板与运行时数据拼成最终文案（回合提示 / 存活 / 名册行 / 结算…）；
    ///   · 提供 17 件武器的中文名与中文说明（数据层的 <c>WeaponCatalog.DisplayName</c> 是英文导出名，
    ///     属只读黑名单，故中文名在 UI 层单独建表）；
    ///   · 提供 <c>CrewType</c> 英文导出符号 → 中文职业名的反查（规范 §4.10 第 9 条）。
    ///
    /// 【为什么纯 C#】可在无头验证台直接跑 NUnit（不实例化 GameObject），是「无英文残留」验收的判据载体。
    /// </summary>
    public static class UiTextRules
    {
        // ------------------------------------------------------------------
        // 武器中文名 / 说明（规范 §4.6 全 17 行）
        // ------------------------------------------------------------------

        /// <summary>武器中文名（规范 §4.6）。</summary>
        public static string WeaponName(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Cannonball: return "炮弹";        // 【AI 提案】名
                case WeaponId.CherryBomb: return "樱桃炸弹";
                case WeaponId.Dynamite: return "炸药";
                case WeaponId.Boulder: return "巨石";
                case WeaponId.Banana: return "香蕉";
                case WeaponId.Mine: return "地雷";
                case WeaponId.ParachuteBomb: return "降落伞炸弹";
                case WeaponId.RumBottle: return "朗姆酒瓶";
                case WeaponId.PiecesOfEight: return "八枚金币";  // 【AI 提案】名
                case WeaponId.GunpowderBarrel: return "火药桶";
                case WeaponId.WoodenCrate: return "木箱";
                case WeaponId.Anchor: return "船锚";
                case WeaponId.Seagull: return "海鸥";            // 【AI 提案】名
                case WeaponId.TidalWave: return "潮汐巨浪";      // 【AI 提案】名
                case WeaponId.VoodooDoll: return "巫毒娃娃";
                case WeaponId.Cannon: return "大炮";
                case WeaponId.SweepingFlame: return "扫射火焰";
                default: return "未知武器";
            }
        }

        /// <summary>武器中文说明（HUD / 图鉴 hover 用；规范 §4.6）。</summary>
        public static string WeaponDescription(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Cannonball:
                    return "保底武器：每回合没武器时自动补发；撞到瓦片或敌人即爆。";
                case WeaponId.CherryBomb:
                    return "接触即爆，威力最小，最适合练手的入门武器。";
                case WeaponId.Dynamite:
                    return "落地静止后引爆，标准投掷爆炸物。";
                case WeaponId.Boulder:
                    return "越滚越快，碾到谁就把谁推开，伤害看滚速。";
                case WeaponId.Banana:
                    return "弹跳力极强，静下来后点一下鼠标立刻引爆。";
                case WeaponId.Mine:
                    return "放好就藏起来，敌人一走近移动就会引爆。";
                case WeaponId.ParachuteBomb:
                    return "缓慢下降，从正上方砸掩体后的敌人；按住鼠标当扇子左右吹。";
                case WeaponId.RumBottle:
                    return "碎裂溅射，落在地上还会烧出一片左右蔓延的火。";
                case WeaponId.PiecesOfEight:
                    return "一枚金币就是一次投掷，这件武器一共能用 8 次。";
                case WeaponId.GunpowderBarrel:
                    return "一次放 2 个，被任何爆炸打中就炸，还能连锁引爆别人。";
                case WeaponId.WoodenCrate:
                    return "一次放 3 个，搭成一道墙给船员当掩体。";
                case WeaponId.Anchor:
                    return "点哪砸哪，从天而降，命中重创，但投掷距离很短。";
                case WeaponId.Seagull:
                    return "点击选高度，海鸥横穿战场，再点点它投弹轰炸。";
                case WeaponId.TidalWave:
                    return "从场地一侧横扫到另一侧，敌我通吃，谁也躲不开。";
                case WeaponId.VoodooDoll:
                    return "先点一个敌人锁定，再抛出木偶，那个敌人会跟着被甩飞。";
                case WeaponId.Cannon:
                    return "拖到圈里摆好，拉尾部销钉调角度蓄力，松手开炮。";
                case WeaponId.SweepingFlame:
                    return "朗姆酒瓶落地后烧出的火焰，一路蔓延持续灼伤。";
                default:
                    return string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // 职业名反查（规范 §4.10 第 9 条：CrewType 英文导出符号 → 中文）
        // ------------------------------------------------------------------

        /// <summary>
        /// 战斗数据层的 <c>CrewType</c>（如 <c>redPirate</c>）→ 中文职业名。
        /// 通过 <see cref="CrewRosterCatalog.BattleSymbol"/> 反查；查不到回落「海盗」而不是回显英文。
        /// </summary>
        public static string CrewNameByBattleSymbol(string battleSymbol)
        {
            if (!string.IsNullOrEmpty(battleSymbol))
            {
                var all = CrewRosterCatalog.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (string.Equals(all[i].BattleSymbol, battleSymbol, System.StringComparison.Ordinal))
                        return all[i].DisplayName;
                }
            }

            return "海盗";
        }

        /// <summary>职业 id（sailor 等）→ 中文职业名；查不到回显 id。</summary>
        public static string CrewNameById(string crewId)
        {
            return CrewRosterCatalog.TryGet(crewId, out CrewRosterEntry entry) ? entry.DisplayName : crewId;
        }

        /// <summary>职业 id → 职业描述（GDD §5.2）。未知返回空串。</summary>
        public static string CrewDescriptionById(string crewId)
        {
            switch (crewId)
            {
                case "sailor": return UiStrings.CrewSailorDesc;
                case "gunner": return UiStrings.CrewGunnerDesc;
                case "sniper": return UiStrings.CrewSniperDesc;
                case "hooker": return UiStrings.CrewHookerDesc;
                case "arsonist": return UiStrings.CrewArsonistDesc;
                case "skeleton": return UiStrings.CrewSkeletonDesc;
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // 战斗 HUD 文案
        // ------------------------------------------------------------------

        /// <summary>回合提示：AI 控制 → 电脑回合；否则「玩家 N，该你了」。</summary>
        public static string TurnHint(bool aiControlled, int teamNumber)
        {
            return aiControlled ? UiStrings.BattleTurnAi : string.Format(UiStrings.BattleTurnYouFormat, teamNumber);
        }

        /// <summary>双方存活：「红队 存活 a/b　蓝队 存活 c/d」（修正只统计当前队的缺陷）。</summary>
        public static string TeamStatus(int redAlive, int redTotal, int blueAlive, int blueTotal)
        {
            return string.Format(UiStrings.BattleTeamStatusFormat, redAlive, redTotal, blueAlive, blueTotal);
        }

        /// <summary>名册标题。</summary>
        public static string RosterTitle(int levelNumber)
        {
            return string.Format(UiStrings.BattleRosterTitleFormat, levelNumber);
        }

        /// <summary>名册行：队伍中文 + 职业中文。</summary>
        public static string RosterRow(int teamNumber, string battleSymbol)
        {
            return string.Format(UiStrings.BattleRosterRowFormat,
                UiTheme.TeamName(teamNumber), CrewNameByBattleSymbol(battleSymbol));
        }

        /// <summary>生命数字。</summary>
        public static string Hp(int health, int maxHealth)
        {
            return string.Format(UiStrings.BattleHpFormat, health, maxHealth);
        }

        /// <summary>武器面板标题。</summary>
        public static string WeaponPanelTitle(string battleSymbol)
        {
            return string.Format(UiStrings.BattleWeaponPanelFormat, CrewNameByBattleSymbol(battleSymbol));
        }

        /// <summary>存活计数。</summary>
        public static string Alive(int alive, int total)
        {
            return string.Format(UiStrings.BattleAliveFormat, alive, total);
        }

        /// <summary>
        /// 回合计数（对局第 N 手，不带上限——原版 §3 无回合上限，
        /// 旧「回合 N/20」的假上限随 UI 审计 P1-4 退役）。
        /// </summary>
        public static string TurnCounter(int turn)
        {
            return string.Format(UiStrings.BattleTurnFormat, turn);
        }

        /// <summary>力度数字（瞄准态，不标「力度」）。</summary>
        public static string StrengthPercent(float normalized01)
        {
            return string.Format(UiStrings.BattleStrengthFormat, Percent(normalized01));
        }

        /// <summary>力度完整写法（非瞄准态）。</summary>
        public static string StrengthLabel(float normalized01)
        {
            return string.Format(UiStrings.BattleStrengthLabelFormat, Percent(normalized01));
        }

        /// <summary>0–1 → 0–100 整数百分比（夹取）。</summary>
        public static int Percent(float normalized01)
        {
            float clamped = normalized01 < 0f ? 0f : (normalized01 > 1f ? 1f : normalized01);
            return (int)(clamped * 100f + 0.5f);
        }

        /// <summary>模式开关文案。</summary>
        public static string ModeLabel(bool moveMode)
        {
            return moveMode ? UiStrings.BattleModeMove : UiStrings.BattleModeAction;
        }

        // ------------------------------------------------------------------
        // 结算文案
        // ------------------------------------------------------------------

        /// <summary>对局结果标题（1P 模式玩家胜显示「胜利」，2P 显示「玩家 N 获胜」）。</summary>
        public static string OutcomeTitle(MatchOutcome outcome, bool team1IsAi)
        {
            switch (outcome)
            {
                case MatchOutcome.Team0Win:
                    return team1IsAi ? UiStrings.SettlementWin : UiStrings.SettlementWinP1;
                case MatchOutcome.Team1Win:
                    return UiStrings.SettlementWinP2;
                case MatchOutcome.Draw:
                    return UiStrings.SettlementDraw;
                default:
                    return UiStrings.SettlementFail;
            }
        }

        /// <summary>结算行：关卡。</summary>
        public static string SettlementLevel(string levelDisplayName)
        {
            return string.Format(UiStrings.SettlementRowLevelFormat, levelDisplayName);
        }

        /// <summary>结算行：评分。</summary>
        public static string SettlementScore(int score)
        {
            return string.Format(UiStrings.SettlementRowScoreFormat, score);
        }

        /// <summary>结算行：星级。</summary>
        public static string SettlementStars(int stars, int maxStars)
        {
            return string.Format(UiStrings.SettlementRowStarsFormat, stars, maxStars);
        }

        /// <summary>结算行：每人经验。</summary>
        public static string SettlementXp(int xpPerCrew)
        {
            return string.Format(UiStrings.SettlementRowXpFormat, xpPerCrew);
        }

        /// <summary>结算行：新招募。</summary>
        public static string SettlementUnlock(string displayNames)
        {
            return string.Format(UiStrings.SettlementRowUnlockFormat, displayNames);
        }

        // ------------------------------------------------------------------
        // 船员管理 / 选关文案
        // ------------------------------------------------------------------

        /// <summary>船员管理行文本。</summary>
        public static string CrewRow(string displayName, int level, int xp)
        {
            return string.Format(UiStrings.CrewRowFormat, displayName, level, xp);
        }

        /// <summary>船员管理行文本（未解锁）。</summary>
        public static string CrewRowLocked(string displayName, int unlockLevelNumber)
        {
            return string.Format(UiStrings.CrewRowLockedFormat, displayName, unlockLevelNumber);
        }

        /// <summary>选关顶部信息。</summary>
        public static string LevelHeader(string title, int chapter, int chapterCount, int stars, int maxStars)
        {
            return string.Format(UiStrings.LevelHeaderFormat, title, chapter, chapterCount, stars, maxStars);
        }

        /// <summary>关卡显示名（「第 N 关」）。</summary>
        public static string LevelName(int levelNumber)
        {
            return string.Format(UiStrings.LevelNameFormat, levelNumber);
        }

        /// <summary>章节显示名（1–3；越界回落「第 N 章」）。</summary>
        public static string ChapterName(int chapter)
        {
            switch (chapter)
            {
                case 1: return UiStrings.Chapter1;
                case 2: return UiStrings.Chapter2;
                case 3: return UiStrings.Chapter3;
                default: return string.Format(UiStrings.LevelChapterFormat, chapter);
            }
        }

        // ------------------------------------------------------------------
        // 「无英文残留」判据（验收 V1 的可程序化部分）
        // ------------------------------------------------------------------

        /// <summary>规范明确允许保留半角的例外 token（品牌名 / 按键字母）。</summary>
        static readonly string[] AllowedTokens = { "3d", "esc", "e", "w", "s", "wasd" };

        /// <summary>
        /// 文案里是否含**不允许**的英文单词/字母串。
        /// 数字串（0.1 之类）不算；允许 token 见 <see cref="AllowedTokens"/>；
        /// 「死亡」「存活」等中文不受影响。
        /// </summary>
        public static bool ContainsDisallowedEnglish(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            int i = 0;
            while (i < text.Length)
            {
                if (!IsAsciiAlnum(text[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && IsAsciiAlnum(text[i]))
                    i++;

                string token = text.Substring(start, i - start);
                if (ContainsAsciiLetter(token) && !IsAllowedToken(token))
                    return true;
            }

            return false;
        }

        /// <summary>非 ASCII 字符占比（中文文案应显著高于半角；用于粗筛未翻译串）。</summary>
        public static float NonAsciiRatio(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            int nonAscii = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 0x7F)
                    nonAscii++;
            }

            return (float)nonAscii / text.Length;
        }

        /// <summary>把文本里所有不允许的英文 token 列出来（诊断用）。</summary>
        public static string DescribeDisallowedEnglish(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (!IsAsciiAlnum(text[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && IsAsciiAlnum(text[i]))
                    i++;

                string token = text.Substring(start, i - start);
                if (ContainsAsciiLetter(token) && !IsAllowedToken(token))
                {
                    if (sb.Length > 0)
                        sb.Append('、');
                    sb.Append(token);
                }
            }

            return sb.ToString();
        }

        static bool IsAsciiAlnum(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        static bool ContainsAsciiLetter(string token)
        {
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    return true;
            }

            return false;
        }

        static bool IsAllowedToken(string token)
        {
            for (int i = 0; i < AllowedTokens.Length; i++)
            {
                if (string.Equals(token, AllowedTokens[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
