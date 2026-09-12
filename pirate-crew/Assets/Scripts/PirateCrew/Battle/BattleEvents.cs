using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 战斗模块的 EventBus 事件契约集中登记（对应逆向文档 §3.1/§3.2/§3.3/§8.1）。
    ///
    /// 【约定】跨模块通信只走 <c>PirateCrew.Core.EventBus</c> 的字符串事件；
    ///         事件名一律 snake_case（与 Godot 版命名兼容），禁止在业务代码里散落魔法字符串。
    ///         本类是战斗事件的唯一登记处，发布/订阅双方都从这里取常量。
    ///
    /// 【载荷】统一用本文件里定义的只读结构体（值类型，跨模块可安全传递）；
    ///         只有 <c>TurnStarted</c> / <c>CameraFocusRequested</c> 带 <see cref="Transform"/>
    ///         （UnityEngine 类型，符合参照库调研「不跨模块传自定义业务类型」的约定）。
    ///
    /// 对应交付约束 D：battle_started / turn_started / turn_ended / action_selected /
    ///                 crew_damaged / crew_died / match_finished 为必需事件。
    /// </summary>
    public static class BattleEvents
    {
        /// <summary>战斗开始（载荷 <see cref="BattleStartedPayload"/>）。</summary>
        public const string BattleStarted = "battle_started";

        /// <summary>回合开始（载荷 <see cref="TurnStartedPayload"/>）。</summary>
        public const string TurnStarted = "turn_started";

        /// <summary>回合结束（载荷 int：队伍编号 1/2）。</summary>
        public const string TurnEnded = "turn_ended";

        /// <summary>玩家/AI 选定一次动作（载荷 <see cref="ActionSelectedPayload"/>）。</summary>
        public const string ActionSelected = "action_selected";

        /// <summary>角色受伤（载荷 <see cref="CrewDamagedPayload"/>）。</summary>
        public const string CrewDamaged = "crew_damaged";

        /// <summary>角色死亡（载荷 <see cref="CrewDiedPayload"/>）。</summary>
        public const string CrewDied = "crew_died";

        /// <summary>对局结束（载荷 <see cref="MatchFinishedPayload"/>）。</summary>
        public const string MatchFinished = "match_finished";

        /// <summary>请求战斗相机聚焦某目标（载荷 <see cref="Transform"/>，§3.2 panToCharacter）。</summary>
        public const string CameraFocusRequested = "camera_focus_requested";

        /// <summary>瞄准姿态更新（载荷 float：当前拖拽距离 px）。</summary>
        public const string AimUpdated = "battle_aim_updated";

        /// <summary>投掷/发射释放（载荷 float：释放时的拖拽距离 px）。</summary>
        public const string ShotReleased = "battle_shot_released";
    }

    /// <summary>动作种类（<see cref="BattleEvents.ActionSelected"/> 载荷用，对应 §3.4 三路径）。</summary>
    public enum BattleActionKind
    {
        /// <summary>抛自己（§3.4 Character.twang）。</summary>
        ThrowSelf = 0,

        /// <summary>使用武器（§3.4 Weapon.twang；用武器即结束回合）。</summary>
        UseWeapon = 1,

        /// <summary>点 end go 直接结束回合（§3.4 button_endTurn）。</summary>
        EndGo = 2,
    }

    /// <summary>battle_started 载荷。</summary>
    public readonly struct BattleStartedPayload
    {
        /// <summary>关卡序号（1–33）。</summary>
        public readonly int LevelNumber;

        /// <summary>队伍数量（恒为 2）。</summary>
        public readonly int TeamCount;

        public BattleStartedPayload(int levelNumber, int teamCount)
        {
            LevelNumber = levelNumber;
            TeamCount = teamCount;
        }
    }

    /// <summary>turn_started 载荷。</summary>
    public readonly struct TurnStartedPayload
    {
        /// <summary>本回合行动方队伍编号（1 = 红队，2 = 蓝队）。</summary>
        public readonly int TeamNumber;

        /// <summary>该队累计消耗的回合数（§3.3 得分公式用）。</summary>
        public readonly int TotalTurnsTaken;

        /// <summary>默认镜头目标角色 id（对应 §3.2 panToCharacter；无有效目标时为 -1）。</summary>
        public readonly int PirateId;

        /// <summary>默认镜头目标 Transform（对应 §3.2 panToCharacter；可为 null）。</summary>
        public readonly Transform PanTarget;

        public TurnStartedPayload(int teamNumber, int totalTurnsTaken, int pirateId, Transform panTarget)
        {
            TeamNumber = teamNumber;
            TotalTurnsTaken = totalTurnsTaken;
            PirateId = pirateId;
            PanTarget = panTarget;
        }
    }

    /// <summary>action_selected 载荷。</summary>
    public readonly struct ActionSelectedPayload
    {
        /// <summary>行动角色 id。</summary>
        public readonly int PirateId;

        /// <summary>角色所属队伍索引（0 = 红队，1 = 蓝队）。</summary>
        public readonly int TeamIndex;

        /// <summary>动作种类（<see cref="BattleActionKind"/>）。</summary>
        public readonly BattleActionKind Kind;

        public ActionSelectedPayload(int pirateId, int teamIndex, BattleActionKind kind)
        {
            PirateId = pirateId;
            TeamIndex = teamIndex;
            Kind = kind;
        }
    }

    /// <summary>crew_damaged 载荷。</summary>
    public readonly struct CrewDamagedPayload
    {
        /// <summary>角色 id。</summary>
        public readonly int PirateId;

        /// <summary>角色所属队伍索引（0/1）。</summary>
        public readonly int TeamIndex;

        /// <summary>本次实际造成的伤害（已按 §4.4 round）。</summary>
        public readonly float Damage;

        /// <summary>结算后生命值。</summary>
        public readonly int Health;

        /// <summary>最大生命值。</summary>
        public readonly int MaxHealth;

        public CrewDamagedPayload(int pirateId, int teamIndex, float damage, int health, int maxHealth)
        {
            PirateId = pirateId;
            TeamIndex = teamIndex;
            Damage = damage;
            Health = health;
            MaxHealth = maxHealth;
        }
    }

    /// <summary>crew_died 载荷。</summary>
    public readonly struct CrewDiedPayload
    {
        /// <summary>角色 id。</summary>
        public readonly int PirateId;

        /// <summary>角色所属队伍索引（0/1）。</summary>
        public readonly int TeamIndex;

        /// <summary>角色种类导出符号名（§4.2）。</summary>
        public readonly string CrewType;

        public CrewDiedPayload(int pirateId, int teamIndex, string crewType)
        {
            PirateId = pirateId;
            TeamIndex = teamIndex;
            CrewType = crewType;
        }
    }

    /// <summary>match_finished 载荷。</summary>
    public readonly struct MatchFinishedPayload
    {
        /// <summary>对局结果（<c>PirateCrew.PirateCrew.Combat.MatchOutcome</c> 的整数值）。</summary>
        public readonly int Outcome;

        /// <summary>1P 关卡得分（§3.3 / §7.3；2P 模式该值为 0）。</summary>
        public readonly int Score;

        /// <summary>team2 是否由 AI 控制（1P 模式 true）。</summary>
        public readonly bool Team1IsAi;

        public MatchFinishedPayload(int outcome, int score, bool team1IsAi)
        {
            Outcome = outcome;
            Score = score;
            Team1IsAi = team1IsAi;
        }
    }
}
