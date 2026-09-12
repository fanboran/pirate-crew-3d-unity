using System;
using System.Collections.Generic;

namespace PirateCrew.CrewManagement
{
    /// <summary>
    /// 一名可招募船员的名录条目（纯 C# 结构）。
    /// </summary>
    public readonly struct CrewRosterEntry
    {
        /// <summary>船员 id（= Godot <c>roster.gd</c> 里 <c>_unlocked_crews</c> 使用的字符串 id，如 <c>sailor</c>）。</summary>
        public readonly string Id;

        /// <summary>中文显示名（UI 用）。</summary>
        public readonly string DisplayName;

        /// <summary>
        /// 战役关卡序号达到该值时招募开放；<c>0</c> 表示初始船员。
        /// <b>提案/待定</b>：数值取自 Godot 项目设计文档 <c>../game-3/docs/gdd.md</c> §5.2 的「解锁」列，
        /// 非逆向文档结论——<b>原版 Flash 没有船员招募系统</b>
        /// （见 <c>docs/参考游戏逆向-海盗军团抢宝藏-静态.md</c> §7.3 末「无金币/商店系统，只有关卡得分 + 解锁进度」），
        /// 因此本表整体属于本项目自创设计，需人工确认。
        /// </summary>
        public readonly int UnlockLevelNumber;

        /// <summary>
        /// 对应战斗数据层的海盗导出符号（<c>PirateCrew.PirateCrew.Data.CrewCatalog.ExportSymbols</c>，§4.2）。
        /// <b>提案/待定</b>：M3 的编成还不会注入战斗（见 <c>CampaignApi</c> 类头「本轮边界」），
        /// 该映射仅供后续打通时参考，未经确认。
        /// </summary>
        public readonly string BattleSymbol;

        public CrewRosterEntry(string id, string displayName, int unlockLevelNumber, string battleSymbol)
        {
            Id = id;
            DisplayName = displayName;
            UnlockLevelNumber = unlockLevelNumber;
            BattleSymbol = battleSymbol;
        }

        /// <summary>是否为初始船员（无需通关任何关卡）。</summary>
        public bool IsInitial => UnlockLevelNumber <= 0;
    }

    /// <summary>
    /// 可招募船员名录（真值来源，纯 C# 静态类，无头验证台可断言）。
    ///
    /// 【出处与性质】
    ///   · 结构取自 Godot <c>modules/crew_management/scripts/roster.gd:12</c>
    ///     （初始 <c>["sailor"]</c>、<c>max_roster_size = 4</c>）。
    ///   · 船员种类与解锁关卡取自 Godot 设计文档 <c>../game-3/docs/gdd.md</c> §5.2 的「3D 重制船员职业设计」表。
    ///   · ⚠ <b>原版 Flash 没有船员系统</b>（所有海盗属性相同，只有美术/初始武器差异，
    ///     见 <c>docs/参考游戏逆向-海盗军团抢宝藏-静态.md</c> §4.1/§4.2），
    ///     因此本表全部为<b>提案/待定</b>，实现按「最小可玩闭环」取，不当作已确认设定。
    /// </summary>
    public static class CrewRosterCatalog
    {
        /// <summary>
        /// 初始船员 id（Godot <c>roster.gd:12</c> <c>_unlocked_crews = ["sailor"]</c>）。
        /// </summary>
        public const string InitialCrewId = "sailor";

        /// <summary>
        /// 编成上限（Godot <c>roster.gd:10</c> <c>@export max_roster_size = 4</c>）。
        /// </summary>
        public const int MaxRosterSize = 4;

        static readonly CrewRosterEntry[] _all =
        {
            // 解锁关卡列 = gdd.md §5.2「解锁」列（提案/待定）。
            new CrewRosterEntry("sailor",        "水手",   0,  "redPirate"),
            new CrewRosterEntry("gunner",        "炮手",   3,  "redPirateCaptain"),
            new CrewRosterEntry("sniper",        "狙击手", 5,  "bluePirate"),
            new CrewRosterEntry("hooker",        "钩子手", 7,  "bluePirateCaptain"),
            new CrewRosterEntry("arsonist",      "纵火狂", 10, "oldPirate"),
            new CrewRosterEntry("skeleton",      "骷髅海盗", 13, "skeletonPirate"),
        };

        /// <summary>全部可招募船员（顺序即展示顺序）。</summary>
        public static IReadOnlyList<CrewRosterEntry> All => _all;

        /// <summary>名录条目数。</summary>
        public static int Count => _all.Length;

        /// <summary>id 是否在名录内（<see cref="StringComparison.Ordinal"/> 精确匹配）。</summary>
        public static bool Contains(string crewId)
        {
            return TryGet(crewId, out _);
        }

        /// <summary>按 id 取条目；不存在返回 false。</summary>
        public static bool TryGet(string crewId, out CrewRosterEntry entry)
        {
            if (!string.IsNullOrEmpty(crewId))
            {
                for (int i = 0; i < _all.Length; i++)
                {
                    if (string.Equals(_all[i].Id, crewId, StringComparison.Ordinal))
                    {
                        entry = _all[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        /// <summary>初始即开放的船员 id 列表（当前只有 <see cref="InitialCrewId"/>）。</summary>
        public static IReadOnlyList<string> InitialCrewIds
        {
            get
            {
                var ids = new List<string>();
                for (int i = 0; i < _all.Length; i++)
                {
                    if (_all[i].IsInitial)
                        ids.Add(_all[i].Id);
                }

                return ids;
            }
        }
    }
}
