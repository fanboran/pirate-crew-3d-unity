using PirateCrew.Core;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
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

        /// <summary>投掷/发射释放（载荷 float：释放时的拖拽距离 px）。</summary>
        public const string ShotReleased = "battle_shot_released";

        /// <summary>AI 队伍开始思考（载荷 <see cref="AiThinkingPayload"/>；§6.1，相机停止自动滚动）。</summary>
        public const string AiThinking = "ai_thinking";

        /// <summary>AI 选定动作（载荷 <see cref="AiDecidedPayload"/>；§6.1 <c>aiMoveDetails</c>）。</summary>
        public const string AiDecided = "ai_decided";

        /// <summary>武器弹体引爆（载荷 <see cref="ProjectileDetonatedPayload"/>；§5.2/§5.3，供表现层做爆炸特效）。</summary>
        public const string ProjectileDetonated = "battle_projectile_detonated";

        /// <summary>地雷引信蜂鸣（载荷 <see cref="MineBeepPayload"/>；§5.2 beepTimes，供音频层播放滴答）。</summary>
        public const string MineBeep = "battle_mine_beep";

        /// <summary>
        /// 把本类全部事件与**期望载荷类型**登记进 <see cref="EventCatalog"/>——契约的代码侧真源。
        ///
        /// 【为什么要这一份】事件名是字符串键，编译期查不出"键配错载荷"；登记之后
        /// <see cref="EventBus"/> 能在运行期对拍并告警，测试也能反射校验「常量都有登记 / 登记无僵尸」。
        /// 新增事件时：这里加一行 + 文档表格加一行（见 <see cref="EventCatalog"/> 类注释的三步）。
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.Contracts, order: 20)]
        public static void RegisterContracts()
        {
            EventCatalog.Add<BattleStartedPayload>(BattleStarted);
            EventCatalog.Add<TurnStartedPayload>(TurnStarted);
            EventCatalog.Add<int>(TurnEnded);
            EventCatalog.Add<ActionSelectedPayload>(ActionSelected);
            EventCatalog.Add<CrewDamagedPayload>(CrewDamaged);
            EventCatalog.Add<CrewDiedPayload>(CrewDied);
            EventCatalog.Add<MatchFinishedPayload>(MatchFinished);
            EventCatalog.Add<Transform>(CameraFocusRequested);
            EventCatalog.Add<float>(ShotReleased);
            EventCatalog.Add<AiThinkingPayload>(AiThinking);
            EventCatalog.Add<AiDecidedPayload>(AiDecided);
            EventCatalog.Add<ProjectileDetonatedPayload>(ProjectileDetonated);
            EventCatalog.Add<MineBeepPayload>(MineBeep);
        }
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

    /// <summary>ai_thinking 载荷（§6.1 AI 队伍开始分帧评估）。</summary>
    public readonly struct AiThinkingPayload
    {
        /// <summary>AI 队伍编号（1/2）。</summary>
        public readonly int TeamNumber;

        /// <summary>本次需要评估的存活角色数。</summary>
        public readonly int ActorCount;

        public AiThinkingPayload(int teamNumber, int actorCount)
        {
            TeamNumber = teamNumber;
            ActorCount = actorCount;
        }
    }

    /// <summary>ai_decided 载荷（§6.1 <c>aiMoveDetails</c> 的可观测投影）。</summary>
    public readonly struct AiDecidedPayload
    {
        /// <summary>行动角色 id。</summary>
        public readonly int PirateId;

        /// <summary>队伍索引（0/1）。</summary>
        public readonly int TeamIndex;

        /// <summary>动作种类（<see cref="AiActionKind"/> 的整数值）。</summary>
        public readonly int ActionKind;

        /// <summary>武器槽位索引；-1 = 抛自己。</summary>
        public readonly int WeaponSlotIndex;

        /// <summary>voodooDoll 锁定目标 id；无则 -1。</summary>
        public readonly int TargetUnitId;

        /// <summary>最终排序分（含 M2 伤害增强项）。</summary>
        public readonly float Success;

        /// <summary>§6.1 无正收益且允许放弃 → 跳过本回合。</summary>
        public readonly bool ShouldBailOut;

        public AiDecidedPayload(
            int pirateId, int teamIndex, int actionKind, int weaponSlotIndex,
            int targetUnitId, float success, bool shouldBailOut)
        {
            PirateId = pirateId;
            TeamIndex = teamIndex;
            ActionKind = actionKind;
            WeaponSlotIndex = weaponSlotIndex;
            TargetUnitId = targetUnitId;
            Success = success;
            ShouldBailOut = shouldBailOut;
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

    /// <summary>MineBeep 载荷（§5.2 beepTimes）。</summary>
    public readonly struct MineBeepPayload
    {
        /// <summary>武器 id（预期为 mine）。</summary>
        public readonly WeaponId Weapon;

        /// <summary>相对引信点燃的经过帧数（beepTimes 值）。</summary>
        public readonly int ElapsedFrames;

        /// <summary>地雷世界坐标。</summary>
        public readonly Vector3 Position;

        public MineBeepPayload(WeaponId weapon, int elapsedFrames, Vector3 position)
        {
            Weapon = weapon;
            ElapsedFrames = elapsedFrames;
            Position = position;
        }
    }
}
