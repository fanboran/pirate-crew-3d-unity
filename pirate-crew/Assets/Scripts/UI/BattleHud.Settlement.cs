using System;
using System.Collections;
using System.Collections.Generic;
using PirateCrew.Campaign;
using PirateCrew.Core;
using PirateCrew.CrewManagement;
using PirateCrew.Audio;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Combat;
using PirateCrew.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗 HUD 的**结算面板**分区（<see cref="BattleHud"/> 的 partial 之一）：
    /// 结算乐句决策口、结算行渲染（SettlementPanelRules 规则消费）、三星逐颗 pop。
    /// </summary>
    public sealed partial class BattleHud
    {
        /// <summary>
        /// 结算乐句的唯一决策口（纯函数；音频层裁决对 HUD 生效，含「2P 热座蓝队胜不放乐句」口径）。
        /// </summary>
        public static bool SettlementJingleFor(int outcome, bool team1IsAi, out SfxId jingle)
        {
            bool hasMusic = AudioEventMapper.MusicForMatchOutcome(
                (MatchOutcome)outcome, team1IsAi, out jingle);
            return hasMusic;
        }

        /// <summary>结算面板：胜负大字 + 三星逐颗 pop + 得分 / 战役明细行。</summary>
        void ShowSettlement(MatchFinishedPayload finished)
        {
            if (settlementPanelRoot == null)
                return;

            // 「显示哪几行 / 亮几颗星」是纯规则（SettlementPanelRules，可无头测）；
            // 本方法只负责：读数据源 → 交给规则 → 把行种类渲染成文本 → 摆 UI。
            CampaignSettlement settlement = CampaignApi.LastSettlement ?? default;
            bool hasCampaignSettlement = CampaignApi.LastSettlement != null;
            CrewRewardPayload? reward = CampaignApi.LastReward;

            var input = new SettlementPanelRules.PanelInput(
                finished.Score,
                _campaignBattle,
                hasCampaignSettlement,
                settlement.Stars,
                settlement.FirstClear,
                reward != null,
                reward?.XpPerCrew ?? 0,
                reward?.UnlockedCrewIds?.Length ?? 0);

            if (settlementTitleText != null)
            {
                UiTextUtil.SetText(settlementTitleText,
                    UiTextRules.OutcomeTitle((MatchOutcome)finished.Outcome, finished.Team1IsAi));
            }

            List<SettlementPanelRules.RowKind> rows = SettlementPanelRules.RowsFor(input);
            var lines = new List<string>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                string line = SettlementRowText(rows[i], input, settlement, reward);
                if (line != null)
                    lines.Add(line);
            }

            if (settlementLinesText != null)
                UiTextUtil.SetText(settlementLinesText, string.Join("\n", lines));

            SetSettlementStars(SettlementPanelRules.LitStarsFor(input));
            OpenModal(settlementPanelRoot, settlementCard);

            bool played = SettlementJingleFor(finished.Outcome, finished.Team1IsAi, out SfxId jingle)
                          && AudioService.PlayMusic(jingle);
            if (!played)
                AudioService.PlayUi(SfxId.UiPanelOpen);
        }

        /// <summary>把一行行种类渲染成文本（文案全部来自 <see cref="UiTextRules"/>，本方法只做映射）。</summary>
        static string SettlementRowText(SettlementPanelRules.RowKind kind,
            in SettlementPanelRules.PanelInput input, in CampaignSettlement settlement, CrewRewardPayload? reward)
        {
            switch (kind)
            {
                case SettlementPanelRules.RowKind.Score:
                    return UiTextRules.SettlementScore(input.Score);
                case SettlementPanelRules.RowKind.Level:
                    return UiTextRules.SettlementLevel(UiTextRules.MapDisplayName(settlement.MapId));
                case SettlementPanelRules.RowKind.Stars:
                    return UiTextRules.SettlementStars(settlement.Stars, StarRules.MaxStars);
                case SettlementPanelRules.RowKind.Xp:
                    return UiTextRules.SettlementXp(input.XpPerCrew);
                case SettlementPanelRules.RowKind.Unlock:
                    // 规则只在 HasReward 时才会给出 Unlock 行；这里再守一道，避免数据源中途变了就 NRE。
                    return reward.HasValue
                        ? UiTextRules.SettlementUnlock(string.Join("、", UiTextRules.CrewDisplayNames(reward.Value.UnlockedCrewIds)))
                        : null;
                case SettlementPanelRules.RowKind.FirstClear:
                    return UiStrings.SettlementRowFirstClear;
                default:
                    return null;
            }
        }

        /// <summary>三星逐颗点亮：颜色分层 + 逐颗延迟 pop（juice）。</summary>
        void SetSettlementStars(int stars)
        {
            if (settlementStars == null)
                return;

            for (int i = 0; i < settlementStars.Length; i++)
            {
                if (settlementStars[i] == null)
                    continue;

                bool lit = i < stars;
                // 星级是 icon 族（可染色），但取色仍走像素皮调色板：亮 = 黄铜强调档，暗 = 暖白压 alpha。
                settlementStars[i].color = lit
                    ? PixelSkin.LightOf(PixelTone.Primary)
                    : UiSkin.WithAlpha(PixelSkin.PaperWhite, 0.28f);
                if (lit && isActiveAndEnabled)
                    StartCoroutine(PopStarDelayed(settlementStars[i], 0.12f * i));
            }
        }

        IEnumerator PopStarDelayed(Image star, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (_motion != null)
                _motion.Pop(star);
        }

        // 海图名 / 船员显示名的本地拷贝已收编进 UiTextRules（MapDisplayName / CrewDisplayNames，
        // 与选关、船员管理同源），本类只做调用。
    }
}
