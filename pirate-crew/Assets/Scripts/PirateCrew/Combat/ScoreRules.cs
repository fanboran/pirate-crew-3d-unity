using UnityEngine;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 关卡得分规则。
    /// 对应逆向文档 §3.3 / §7.3：<c>get1PLevelScore = floor(team0.getAverageHealth()*20 - team0.totalTurnsTaken*25)</c>，
    /// 下限为 <c>selected_level * 10</c>。
    /// 纯静态逻辑，不依赖 MonoBehaviour / GameObject。
    /// </summary>
    public static class ScoreRules
    {
        /// <summary>每点平均生命值折算的分数（原版 20）。</summary>
        public const float HealthScorePerPoint = 20f;

        /// <summary>每消耗一个回合扣除的分数（原版 25）。</summary>
        public const int TurnScorePenalty = 25;

        /// <summary>每关索引对应的分数下限系数（原版下限 = selected_level * 10）。</summary>
        public const int LevelFloorPerIndex = 10;

        /// <summary>
        /// 计算 1P 关卡得分。
        /// 取整语义：先算 <c>avgHealth*20 - totalTurnsTaken*25</c>（float），
        /// 对其<b>向下取整</b>（floor，对负数同样向 −∞ 取整），再与下限取较大值。
        /// 下限：<c>levelIndex * 10</c>，保证任何差表现都不会低于该值（也不会因原始分为负而倒扣总分）。
        /// </summary>
        /// <param name="avgHealth">team0 平均生命值；分母含死亡角色（§7.3 getAverageHealth）</param>
        /// <param name="totalTurnsTaken">team0 已消耗的回合数</param>
        /// <param name="levelIndex">关卡序号（原版 selected_level，1–33）</param>
        public static int LevelScore(float avgHealth, int totalTurnsTaken, int levelIndex)
        {
            float raw = avgHealth * HealthScorePerPoint - totalTurnsTaken * TurnScorePenalty;
            int floored = Mathf.FloorToInt(raw);
            int floorLimit = levelIndex * LevelFloorPerIndex;
            return Mathf.Max(floored, floorLimit);
        }
    }
}
