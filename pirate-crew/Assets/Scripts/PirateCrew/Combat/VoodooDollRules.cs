using System;
using System.Collections.Generic;

namespace PirateCrew.PirateCrew.Combat
{
    /// <summary>
    /// 巫毒娃娃（voodooDoll）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【出处】静态逆向文档（docs/参考游戏逆向-海盗军团抢宝藏-静态.md）：
    ///   · §5.2「武器总表」voodooDoll 行（表格第 15 行）——
    ///     「先点敌人锁定目标 → 弹弓抛出木偶」；「无爆炸」；
    ///     「落地 10 帧后镜头切到目标，再 10 帧后把木偶的投掷速度赋给目标角色（目标被同方向抛飞，可落水）」；
    ///     weight=1、twangMax=20。
    ///   · §4.2 目标选择（原文 line 262-263）——
    ///     <c>if voodooDoll equipped and !fired: pickNearestEnemy(30px) -&gt; setTargetCharacter(); return</c>；
    ///     与其余选中共用屏幕空间 30px 阈值。
    ///   · §3.4 <c>target</c> 字段——装备 voodooDoll 时标记目标敌人（原文 line 381）。
    ///   · §6.3 voodooDoll 评分——对每个存活敌人模拟 <c>randomThrows(2)</c>，落水则最高收益。
    ///
    /// 【时间线】落地帧记为 0：
    ///   第 10 帧 → 镜头切到目标；第 20 帧 → 把木偶的投掷速度赋给目标。
    /// </summary>
    public static class VoodooDollRules
    {
        /// <summary>锁定目标的选择半径（Flash px）。§4.2「pickNearestEnemy(30px)」。</summary>
        public const float TargetPickRadius = 30f;

        /// <summary>落地后镜头切到目标的帧数。§5.2「落地 10 帧后镜头切到目标」。</summary>
        public const int CameraSwitchFrames = 10;

        /// <summary>镜头切换后再等待的帧数。§5.2「再 10 帧后把木偶的投掷速度赋给目标角色」。</summary>
        public const int VelocityTransferDelayFrames = 10;

        /// <summary>落地到速度转移完成的总帧数。</summary>
        public const int TotalTimelineFrames = CameraSwitchFrames + VelocityTransferDelayFrames;

        /// <summary>
        /// 从候选距离（Flash px，元素顺序对应候选角色）里选最近且在 30px 内的敌人。
        /// 返回索引；无满足者返回 -1。对应 §4.2 <c>pickNearestEnemy(30px)</c>。
        /// </summary>
        public static int PickTargetIndex(IReadOnlyList<float> distancesPx)
        {
            if (distancesPx == null)
                return -1;

            int bestIndex = -1;
            float best = float.MaxValue;
            for (int i = 0; i < distancesPx.Count; i++)
            {
                float d = distancesPx[i];
                if (d < 0f || d > TargetPickRadius)
                    continue;
                if (d < best)
                {
                    best = d;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>是否应切换镜头（落地满 10 帧）。</summary>
        public static bool ShouldSwitchCamera(int landedFrames)
        {
            return landedFrames >= CameraSwitchFrames;
        }

        /// <summary>是否应把速度赋给目标（落地满 20 帧）。</summary>
        public static bool ShouldTransferVelocity(int landedFrames)
        {
            return landedFrames >= TotalTimelineFrames;
        }

        /// <summary>时间线是否走完（可销毁木偶/结束专用行为）。</summary>
        public static bool IsTimelineComplete(int landedFrames)
        {
            return landedFrames >= TotalTimelineFrames;
        }

        /// <summary>
        /// 转移给目标的速度 = 木偶被抛出时的投掷速度**原样**（§5.2「把木偶的投掷速度赋给目标角色」）。
        /// 返回 true 表示已转移。
        /// </summary>
        public static bool TryTransferVelocity(int landedFrames, float dollVx, float dollVy,
            out float targetVx, out float targetVy)
        {
            targetVx = dollVx;
            targetVy = dollVy;
            return ShouldTransferVelocity(landedFrames);
        }
    }
}
