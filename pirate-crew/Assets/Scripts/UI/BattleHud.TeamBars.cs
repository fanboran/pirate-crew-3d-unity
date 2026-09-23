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
    /// 战斗 HUD 的**顶栏双队血条**分区（<see cref="BattleHud"/> 的 partial 之一）：
    /// 视图子结构（TeamBarView/UnitSegmentView/UnitPipView）、装配自检、运行时段/pip 刷新、
    /// 单位头顶血条挂接与 28 帧血量比例口径（原版 §4.1）。
    /// 拆分出处：docs/项目/待办事项.md「BattleHud 按分区拆三块」。
    /// </summary>
    public sealed partial class BattleHud
    {
        /// <summary>§4.1 血条总帧数（28 帧，长度 = 1 + ceil(27·hp/max)）。</summary>
        const int HealthBarFrames = 28;

        /// <summary>每队最多段数（当前关卡上限 6v6；装配侧按此建段）。</summary>
        const int MaxSegmentsPerTeam = 6;

        // ------------------------------------------------------------------
        // 视图子结构（装配脚本按字段名回写）
        // ------------------------------------------------------------------

        /// <summary>顶栏一队的合成血条（段容器 + pip 容器）。</summary>
        [Serializable]
        public sealed class TeamBarView
        {
            public GameObject root;
            public RectTransform segmentRoot;
            public RectTransform pipRoot;
            public UnitSegmentView[] segments = new UnitSegmentView[MaxSegmentsPerTeam];
            public UnitPipView[] pips = new UnitPipView[MaxSegmentsPerTeam];
        }

        /// <summary>一名单位的血条段（fill + 白色残影 ghost）。</summary>
        [Serializable]
        public sealed class UnitSegmentView
        {
            public GameObject root;
            public Image fill;
            public Image ghost;
        }

        /// <summary>一名单位的 pip（文字占位：存活空格 / 阵亡「×」+ 彩色格底）。</summary>
        [Serializable]
        public sealed class UnitPipView
        {
            public GameObject root;
            public TextMeshProUGUI label;
            public Image frame;
        }


        /// <summary>段索引 → 单位（两队各一份）。</summary>
        readonly Dictionary<int, PirateBase> _pirateBySegment = new Dictionary<int, PirateBase>();

        /// <summary>双队血条是否接好（段 + pips 全链）。</summary>
        public bool HasTeamBarWiring
        {
            get
            {
                return TeamBarWired(teamBarRed) && TeamBarWired(teamBarBlue);
            }
        }

        static bool TeamBarWired(TeamBarView bar)
        {
            if (bar == null || bar.root == null || bar.segmentRoot == null || bar.pipRoot == null)
                return false;
            if (bar.segments == null || bar.segments.Length != MaxSegmentsPerTeam)
                return false;
            if (bar.pips == null || bar.pips.Length != MaxSegmentsPerTeam)
                return false;

            for (int i = 0; i < MaxSegmentsPerTeam; i++)
            {
                UnitSegmentView segment = bar.segments[i];
                if (segment == null || segment.root == null || segment.fill == null || segment.ghost == null)
                    return false;
                UnitPipView pip = bar.pips[i];
                if (pip == null || pip.root == null || pip.label == null || pip.frame == null)
                    return false;
            }
            return true;
        }



    /// <summary>段间距 / 段区**两端**内边距（与 BattleHudBuilder 同源；3 = 1u——
    /// 两端各缩 1u 让 Track 的 1u 外环左右两缘都露出，右缘描边不被末段盖掉）。</summary>

        // ------------------------------------------------------------------
        // 顶栏双队血条（段 = 存活单位；pip = 职业头像）
        // ------------------------------------------------------------------

        void BuildTeamBars()
        {
            _pirateBySegment.Clear();

            if (battle == null)
                return;

            BuildOneTeamBar(teamBarRed, battle.GetTeam(0), 0);
            BuildOneTeamBar(teamBarBlue, battle.GetTeam(1), 1);
        }

        void BuildOneTeamBar(TeamBarView bar, BattleTeam team, int teamIndex)
        {
            if (bar == null)
                return;

            int count = 0;
            if (team != null)
            {
                var characters = team.Characters;
                // 分段是装配期按 MaxSegmentsPerTeam 预建的：超编时多出的单位没有段，必须吵醒装配侧。
                if (characters.Count > MaxSegmentsPerTeam)
                    Log.Warn("[BattleHud] 队伍人数 " + characters.Count + " 超过血条段数上限 "
                             + MaxSegmentsPerTeam + "，多出的单位不会出现在顶栏血条（检查关卡编队或扩段数）。");
                for (int i = 0; i < characters.Count && count < MaxSegmentsPerTeam; i++)
                {
                    PirateBase pirate = characters[i];
                    if (pirate == null)
                        continue;

                    int slot = count;
                    _pirateBySegment[SegmentKey(teamIndex, slot)] = pirate;

                    UnitSegmentView segment = bar.segments != null && slot < bar.segments.Length
                        ? bar.segments[slot]
                        : null;
                    if (segment != null && segment.root != null)
                    {
                        segment.root.SetActive(true);
                        // 段填充贴图（红/蓝队档）在装配期定死，运行时只推比例——
                        // 不再给 fill 乘队色（像素件禁令）。
                        if (segment.ghost != null)
                            _motion.SnapFillPair(segment.fill, segment.ghost,
                                pirate.Alive ? HealthRatio(pirate.Health, pirate.MaxHealth) : 0f);
                    }

                    UnitPipView pip = bar.pips != null && slot < bar.pips.Length ? bar.pips[slot] : null;
                    if (pip != null && pip.root != null)
                    {
                        pip.root.SetActive(true);
                        bool alive = pirate.Alive;
                        SetPipDimmed(pip.root, !alive);
                        if (pip.label != null)
                        {
                            // 文字占位（图标退役）：存活 = 空格，阵亡 = 「×」压暗。
                            pip.label.text = alive ? string.Empty : "×";
                            pip.label.color = alive
                                ? (Color)PixelSkin.PaperWhite
                                : UiSkin.WithAlpha(UiSkin.DeadGray, 0.9f);
                        }
                    }

                    count++;
                }
            }

            // 隐藏多余段 / pips。
            for (int i = count; i < MaxSegmentsPerTeam; i++)
            {
                if (bar.segments != null && i < bar.segments.Length && bar.segments[i] != null
                    && bar.segments[i].root != null)
                    bar.segments[i].root.SetActive(false);
                if (bar.pips != null && i < bar.pips.Length && bar.pips[i] != null
                    && bar.pips[i].root != null)
                    bar.pips[i].root.SetActive(false);
            }

            // 段按实际人数满格重排（4v4 时每段 1/4 宽，不留空槽——装配期按 6 人预建只是骨架）。
            // 公式与 BattleHudBuilder.BuildOneTeamBar 同式：两端各内缩 SegmentInset。
            if (count > 0 && bar.segments != null && bar.segmentRoot != null)
            {
                float trackWidth = bar.segmentRoot.sizeDelta.x;
                float segmentWidth = (trackWidth - 2f * SegmentInset - (count - 1) * SegmentGap) / count;
                for (int i = 0; i < count && i < bar.segments.Length; i++)
                {
                    var rect = bar.segments[i] != null
                        ? bar.segments[i].root.transform as RectTransform
                        : null;
                    if (rect == null)
                        continue;

                    rect.sizeDelta = new Vector2(segmentWidth, rect.sizeDelta.y);
                    rect.anchoredPosition = new Vector2(SegmentInset + i * (segmentWidth + SegmentGap), 0f);
                }
            }
        }

    /// <summary>段间距 / 段区**两端**内边距（与 BattleHudBuilder 同源；3 = 1u——
    /// 两端各缩 1u 让 Track 的 1u 外环左右两缘都露出，右缘描边不被末段盖掉）。</summary>
    const float SegmentGap = 3f;
    const float SegmentInset = 3f;

        /// <summary>阵亡 pip 压暗：CanvasGroup alpha（不烘黑图、不给像素件乘色）。</summary>
        static void SetPipDimmed(GameObject pipRoot, bool dimmed)
        {
            if (pipRoot == null)
                return;

            var group = pipRoot.GetComponent<CanvasGroup>();
            if (group != null)
                group.alpha = dimmed ? 0.55f : 1f;
        }

        static int SegmentKey(int teamIndex, int slot) => teamIndex * 100 + slot;

        /// <summary>全量刷两队（回合切换时）。</summary>
        void RefreshTeamBars()
        {
            if (battle == null)
                return;

            RefreshOneTeam(teamBarRed, 0);
            RefreshOneTeam(teamBarBlue, 1);
        }

        void RefreshOneTeam(TeamBarView bar, int teamIndex)
        {
            if (bar == null)
                return;

            for (int slot = 0; slot < MaxSegmentsPerTeam; slot++)
            {
                if (!_pirateBySegment.TryGetValue(SegmentKey(teamIndex, slot), out PirateBase pirate)
                    || pirate == null)
                    continue;

                ApplySegmentHealth(bar, slot, pirate.Alive ? pirate.Health : 0, pirate.MaxHealth);
                if (!pirate.Alive)
                    MarkPipDead(teamIndex, pirate.PirateId);   // 幂等（已死跳过）
            }
        }

        /// <summary>按事件更新单段（受击 / 死亡）。</summary>
        void UpdateUnitSegment(int teamIndex, int pirateId, int health, int maxHealth)
        {
            TeamBarView bar = teamIndex == 0 ? teamBarRed : teamBarBlue;
            if (bar == null)
                return;

            for (int slot = 0; slot < MaxSegmentsPerTeam; slot++)
            {
                if (!_pirateBySegment.TryGetValue(SegmentKey(teamIndex, slot), out PirateBase pirate)
                    || pirate == null || pirate.PirateId != pirateId)
                    continue;

                ApplySegmentHealth(bar, slot, health > 0 ? health : 0, maxHealth);
                return;
            }
        }

        void ApplySegmentHealth(TeamBarView bar, int slot, int health, int maxHealth)
        {
            UnitSegmentView segment = bar.segments != null && slot < bar.segments.Length
                ? bar.segments[slot]
                : null;
            if (segment == null)
                return;

            float ratio = HealthRatio(health, maxHealth);
            if (_motion != null)
                _motion.SetFillPairTarget(segment.fill, segment.ghost, ratio);
            else if (segment.fill != null)
                segment.fill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        }

        void MarkPipDead(int teamIndex, int pirateId)
        {
            TeamBarView bar = teamIndex == 0 ? teamBarRed : teamBarBlue;
            if (bar == null)
                return;

            for (int slot = 0; slot < MaxSegmentsPerTeam; slot++)
            {
                if (!_pirateBySegment.TryGetValue(SegmentKey(teamIndex, slot), out PirateBase pirate)
                    || pirate == null || pirate.PirateId != pirateId)
                    continue;

                UnitPipView pip = bar.pips != null && slot < bar.pips.Length ? bar.pips[slot] : null;
                if (pip == null)
                    return;

                bool alreadyDead = pip.label != null && pip.label.text == "×";
                if (alreadyDead)
                    return;

                SetPipDimmed(pip.root, true);
                if (pip.label != null)
                {
                    pip.label.text = "×";
                    pip.label.color = UiSkin.WithAlpha(UiSkin.DeadGray, 0.9f);
                }
                if (_motion != null && pip.frame != null)
                    _motion.Punch(pip.frame, UiMotionRules.PunchSeconds);
                return;
            }
        }

        // 文字占位版（2026-09-24）：职业头像加载（LoadPortrait / PortraitCache）随图标退役删除。
        // 单位头顶血条已根除（非像素世界空间件清退），血量读数只走顶栏合成血条。

        // ------------------------------------------------------------------
        // 血量比例（原版 §4.1 的 28 帧口径，保留）
        // ------------------------------------------------------------------

        /// <summary>长度 = 1 + ceil(27·hp/max)，折算 0-1。</summary>
        public static float HealthRatio(int health, int maxHealth)
        {
            if (maxHealth <= 0 || health <= 0)
                return 0f;

            int frames = 1 + Mathf.CeilToInt(27f * Mathf.Clamp(health, 0, maxHealth) / maxHealth);
            return Mathf.Clamp01((float)frames / HealthBarFrames);
        }
    }
}
