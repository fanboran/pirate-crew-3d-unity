using System.Collections.Generic;

namespace PirateCrew.UI
{
    /// <summary>
    /// 结算面板的**纯规则**：由「本局是否战役局 + 战役结算数据是否就绪 + 得分」推出
    /// 「面板显示哪几行、按什么顺序、亮几颗星」。
    ///
    /// 【为什么单独一个类】这些判断原来是 <c>BattleHud.ShowSettlement</c> 里的一串
    /// <c>if</c>，与读 <c>CampaignApi</c> / 拼字符串 / 开模态框混在一起——于是"哪几行该出现"
    /// 这条规则只能靠读 UI 代码推断，且无法在无头验证台里跑。抽出来之后：
    ///   · 规则是纯函数（输入 = 值类型快照，输出 = 行种类列表 / 星数），可 NUnit 直接断言；
    ///   · HUD 只剩"把行种类渲染成文本 + 摆 UI"，文案仍归 <see cref="UiTextRules"/>（本类不碰文案）。
    ///
    /// 【本类不做的事】不读 <c>CampaignApi</c>（由 HUD 读好填进 <see cref="PanelInput"/>）、
    /// 不碰 Text/Image/协程、不含任何 UnityEngine 类型——纯 C#，无头可测。
    /// 结算乐句的决策口仍在 <c>BattleHud.SettlementJingleFor</c>（早已是纯函数且有专测，
    /// 与音频层真值对拍，改动它会牵动那条一致性测试，故不动）。
    /// </summary>
    public static class SettlementPanelRules
    {
        /// <summary>结算面板的一行（枚举顺序无关，**显示顺序由 <see cref="RowsFor"/> 决定**）。</summary>
        public enum RowKind
        {
            /// <summary>评分行（<c>UiTextRules.SettlementScore</c>）。</summary>
            Score,

            /// <summary>关卡名行。</summary>
            Level,

            /// <summary>星级行（<c>n/max</c>）。</summary>
            Stars,

            /// <summary>每人经验行。</summary>
            Xp,

            /// <summary>新招募行。</summary>
            Unlock,

            /// <summary>首次通关行。</summary>
            FirstClear,
        }

        /// <summary>
        /// 决策输入（值快照，全部由 HUD 从 <c>MatchFinishedPayload</c> + <c>CampaignApi</c> 读出后填入）。
        /// </summary>
        public readonly struct PanelInput
        {
            /// <summary>本局得分（&lt;=0 表示不带评分，不显示评分行）。</summary>
            public readonly int Score;

            /// <summary>本局是否战役局（从选关进来）。</summary>
            public readonly bool CampaignBattle;

            /// <summary>战役结算数据是否就绪（<c>CampaignApi.LastSettlement != null</c>）。</summary>
            public readonly bool HasSettlement;

            /// <summary>本关星级（仅战役局有意义）。</summary>
            public readonly int Stars;

            /// <summary>是否首次通关。</summary>
            public readonly bool FirstClear;

            /// <summary>是否带奖励载荷（<c>CampaignApi.LastReward != null</c>）。</summary>
            public readonly bool HasReward;

            /// <summary>每人经验（&lt;=0 不显示经验行）。</summary>
            public readonly int XpPerCrew;

            /// <summary>本次解锁的船员数（0 不显示新招募行）。</summary>
            public readonly int UnlockedCrewCount;

            public PanelInput(int score, bool campaignBattle, bool hasSettlement, int stars, bool firstClear,
                bool hasReward, int xpPerCrew, int unlockedCrewCount)
            {
                Score = score;
                CampaignBattle = campaignBattle;
                HasSettlement = hasSettlement;
                Stars = stars;
                FirstClear = firstClear;
                HasReward = hasReward;
                XpPerCrew = xpPerCrew;
                UnlockedCrewCount = unlockedCrewCount;
            }
        }

        /// <summary>是否显示战役明细行：两个条件都成立才算（战役局 + 结算数据就绪）。</summary>
        public static bool ShowCampaignRows(in PanelInput input)
        {
            return input.CampaignBattle && input.HasSettlement;
        }

        /// <summary>
        /// 结算面板要显示的行，**顺序即显示顺序**（与抽类前的 HUD 实现逐行等价）：
        /// 评分 →（战役行：关卡 / 星级 / 经验 / 新招募 / 首通）。
        /// </summary>
        public static List<RowKind> RowsFor(in PanelInput input)
        {
            var rows = new List<RowKind>(6);

            // 评分行在前：非战役局也可能有分（旧实现把 Score 加在战役块之前，勿改顺序）。
            if (input.Score > 0)
                rows.Add(RowKind.Score);

            if (!ShowCampaignRows(input))
                return rows;

            rows.Add(RowKind.Level);
            rows.Add(RowKind.Stars);

            // 经验 / 新招募都是奖励载荷里的可选项，各自按"有没有内容"决定（0 / 空数组不显示）。
            if (input.HasReward)
            {
                if (input.XpPerCrew > 0)
                    rows.Add(RowKind.Xp);
                if (input.UnlockedCrewCount > 0)
                    rows.Add(RowKind.Unlock);
            }

            if (input.FirstClear)
                rows.Add(RowKind.FirstClear);

            return rows;
        }

        /// <summary>
        /// 要点亮的星数：非战役局恒 0（不显示星，即便载荷里带了星级）。
        /// 与 <see cref="RowsFor"/> 同一判据，避免"显示星级行却不亮星"的不一致。
        /// </summary>
        public static int LitStarsFor(in PanelInput input)
        {
            return ShowCampaignRows(input) ? input.Stars : 0;
        }
    }
}
