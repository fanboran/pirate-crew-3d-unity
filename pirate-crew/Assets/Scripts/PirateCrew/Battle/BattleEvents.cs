using PirateCrew.Core;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 战斗模块的 EventBus 事件频道集中声明（对应逆向文档 §3.1/§3.2/§3.3/§8.1）。
    ///
    /// 【约定】跨模块通信只走 <c>PirateCrew.Core.EventBus</c> 的类型化频道；
    ///         本类是战斗事件的唯一声明处，发布/订阅双方都从这里取频道字段，
    ///         禁止在调用点内联 new Event。
    ///
    /// 【载荷】统一用本文件里定义的只读结构体（值类型，跨模块可安全传递）；
    ///         只有 <c>TurnStarted</c> / <c>CameraFocusRequested</c> 带 <see cref="Transform"/>
    ///         （UnityEngine 类型，符合参照库调研「不跨模块传自定义业务类型」的约定）。
    ///
    /// 对应交付约束 D：BattleStarted / TurnStarted / TurnEnded / ActionSelected /
    ///                 CrewDamaged / CrewDied / MatchFinished 为必需事件。
    /// </summary>
    public static class BattleEvents
    {
        /// <summary>战斗开始（载荷 <see cref="BattleStartedPayload"/>）。</summary>
        public static readonly Event<BattleStartedPayload> BattleStarted = new();

        /// <summary>回合开始（载荷 <see cref="TurnStartedPayload"/>）。</summary>
        public static readonly Event<TurnStartedPayload> TurnStarted = new();

        /// <summary>回合结束（载荷 int：队伍编号 1/2）。</summary>
        public static readonly Event<int> TurnEnded = new();

        /// <summary>玩家/AI 选定一次动作（载荷 <see cref="ActionSelectedPayload"/>）。</summary>
        public static readonly Event<ActionSelectedPayload> ActionSelected = new();

        /// <summary>角色受伤（载荷 <see cref="CrewDamagedPayload"/>）。</summary>
        public static readonly Event<CrewDamagedPayload> CrewDamaged = new();

        /// <summary>角色死亡（载荷 <see cref="CrewDiedPayload"/>）。</summary>
        public static readonly Event<CrewDiedPayload> CrewDied = new();

        /// <summary>对局结束（载荷 <see cref="MatchFinishedPayload"/>）。</summary>
        public static readonly Event<MatchFinishedPayload> MatchFinished = new();

        /// <summary>请求战斗相机聚焦某目标（载荷 <see cref="Transform"/>，§3.2 panToCharacter）。</summary>
        public static readonly Event<Transform> CameraFocusRequested = new();

        /// <summary>投掷/发射释放（载荷 float：释放时的拖拽距离 px）。</summary>
        public static readonly Event<float> ShotReleased = new();

        /// <summary>武器弹体引爆（载荷 <see cref="ProjectileDetonatedPayload"/>，供表现层做爆炸特效）。</summary>
        public static readonly Event<ProjectileDetonatedPayload> ProjectileDetonated = new();
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
        /// <summary>对局结果（<c>PirateCrew.Combat.MatchOutcome</c> 的整数值）。</summary>
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

    /// <summary>WeaponProjectileDetonated 载荷。</summary>
    public readonly struct ProjectileDetonatedPayload
    {
        /// <summary>引爆的武器 id。</summary>
        public readonly WeaponId Weapon;

        /// <summary>爆心世界坐标（Unity，y 向上）。</summary>
        public readonly Vector3 Position;

        public ProjectileDetonatedPayload(WeaponId weapon, Vector3 position)
        {
            Weapon = weapon;
            Position = position;
        }
    }
}
