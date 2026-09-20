using System.Collections.Generic;
using PirateCrew.Combat;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗队伍（普通 C# 类，非 MonoBehaviour）。
    ///
    /// 【对应章节】§3.2（<c>number</c> / <c>characters</c> / <c>selectedCharacter</c> /
    ///             <c>aiControlled</c> / <c>totalTurnsTaken</c> / <c>startTurn</c> / <c>isTurnComplete</c>）、
    ///             §3.3（<c>anyAlive</c> / <c>getAverageHealth</c>）、§4.3（每回合 1 角色）。
    ///
    /// 只持有 <see cref="PirateBase"/> 引用并做队伍级汇总，不驱动物理/输入。
    /// </summary>
    public sealed class BattleTeam
    {
        readonly List<PirateBase> _characters = new List<PirateBase>();

        /// <summary>队伍编号：1 = 红队（team1 / 玩家侧），2 = 蓝队（team2）。§3.2。</summary>
        public int Number { get; }

        /// <summary>队伍索引：0 = 红队，1 = 蓝队（§4.3）。</summary>
        public int TeamIndex => Number - 1;

        /// <summary>是否由 AI 控制（§3.2：1P 模式 team2 = true）。</summary>
        public bool AiControlled { get; set; }

        /// <summary>该队累计消耗的回合数（§3.3 得分公式）。</summary>
        public int TotalTurnsTaken { get; private set; }

        /// <summary>本回合选中角色的队伍内索引；-1 = 未选。</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>本回合选中角色（§3.2 <c>selectedCharacter</c>，单值 → 每回合只有 1 个角色行动）。</summary>
        public PirateBase SelectedCharacter { get; private set; }

        /// <summary>队伍成员（顺序 = 出战计划出现顺序）。</summary>
        public IReadOnlyList<PirateBase> Characters => _characters;

        public BattleTeam(int number, bool aiControlled)
        {
            Number = number;
            AiControlled = aiControlled;
        }

        /// <summary>加入成员（组装期调用）。</summary>
        public void Add(PirateBase pirate)
        {
            if (pirate != null && !_characters.Contains(pirate))
                _characters.Add(pirate);
        }

        /// <summary>§3.3 <c>anyAlive</c>。</summary>
        public bool AnyAlive
        {
            get
            {
                for (int i = 0; i < _characters.Count; i++)
                {
                    PirateBase c = _characters[i];
                    if (c != null && c.Alive)
                        return true;
                }
                return false;
            }
        }

        /// <summary>§3.3 <c>getAverageHealth</c>：分母含死亡角色（死亡按 0 计）。</summary>
        public float AverageHealth
        {
            get
            {
                if (_characters.Count == 0)
                    return 0f;

                int total = 0;
                for (int i = 0; i < _characters.Count; i++)
                {
                    PirateBase c = _characters[i];
                    total += (c != null && c.Alive) ? c.Health : 0;
                }
                return (float)total / _characters.Count;
            }
        }

        /// <summary>队伍内索引；不在队伍返回 -1。</summary>
        public int IndexOf(PirateBase pirate)
        {
            return _characters.IndexOf(pirate);
        }

        /// <summary>§3.2 <c>getCaptain</c> 近似：名字含 "Captain"（或 tribeChief）的成员，取第一个存活者。</summary>
        public PirateBase Captain()
        {
            for (int i = 0; i < _characters.Count; i++)
            {
                PirateBase c = _characters[i];
                if (c == null || !c.Alive || string.IsNullOrEmpty(c.CrewType))
                    continue;

                if (c.CrewType.IndexOf("Captain", System.StringComparison.Ordinal) >= 0
                    || c.CrewType.IndexOf("tribeChief", System.StringComparison.Ordinal) >= 0)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>距离给定世界坐标最近的存活成员（无则 null）。</summary>
        public PirateBase NearestTo(Vector3 worldPosition)
        {
            PirateBase best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < _characters.Count; i++)
            {
                PirateBase c = _characters[i];
                if (c == null || !c.Alive)
                    continue;

                float sqr = (c.transform.position - worldPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>第一个存活成员（无则 null）。</summary>
        public PirateBase FirstAlive()
        {
            for (int i = 0; i < _characters.Count; i++)
            {
                PirateBase c = _characters[i];
                if (c != null && c.Alive)
                    return c;
            }
            return null;
        }

        /// <summary>§3.2 <c>startTurn</c>：totalTurnsTaken++、清 selectedCharacter。</summary>
        public void StartTurn()
        {
            TotalTurnsTaken++;
            ClearSelection();
        }

        /// <summary>
        /// §3.2 <c>select</c>：选中本回合唯一行动角色；拒绝死亡角色；
        /// 本回合已选过角色时仅 <paramref name="again"/>（continueTurn）允许重复选中。
        /// </summary>
        public bool Select(PirateBase pirate, bool again = false)
        {
            if (pirate == null || !pirate.Alive)
                return false;
            if (SelectedCharacter != null && !again)
                return false;

            SelectedIndex = IndexOf(pirate);
            if (SelectedIndex < 0)
                return false;

            // 选中唯一性由本类保证，故"取消上一个、点亮这一个"也在这里做一次，
            // 让 SetSelected 状态位与 SelectedCharacter 永远同步（TurnManager 会直接调本方法，
            // 绕过 BattleController.SelectCharacter，只钩后者会漏）。
            if (SelectedCharacter != null && SelectedCharacter != pirate)
                SelectedCharacter.SetSelected(false);

            SelectedCharacter = pirate;
            pirate.SetSelected(true);
            return true;
        }

        /// <summary>§3.2 <c>finishTurn</c>：清 selectedCharacter。</summary>
        public void FinishTurn()
        {
            ClearSelection();
        }

        /// <summary>
        /// 清空本回合选中，并同步熄灭 <see cref="PirateBase.SetSelected"/> 状态位
        /// （描边表现由 <see cref="UnitOutlineBinder"/> 读该状态位）。
        /// </summary>
        void ClearSelection()
        {
            if (SelectedCharacter != null)
                SelectedCharacter.SetSelected(false);

            SelectedIndex = -1;
            SelectedCharacter = null;
        }

        /// <summary>§3.2 <c>isTurnComplete</c>：已选角色且行动经济耗尽（或选中角色死亡）。</summary>
        public bool IsTurnComplete
        {
            get
            {
                if (SelectedCharacter == null)
                    return false;
                return TurnRules.IsTurnComplete(SelectedCharacter.Alive, SelectedCharacter.CurrentAction);
            }
        }
    }
}
