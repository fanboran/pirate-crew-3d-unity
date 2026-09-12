using System;
using PirateCrew.Core;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 战斗中的海盗角色运行时（翻译自 Godot <c>scripts/characters/pirate_base.gd</c>，3D 语义重写）。
    ///
    /// 【对应章节】§4.1（maxHealth 100 / weight 1 / evilness / canThrow / canShoot）、
    ///             §4.4（SubtractHealth 四舍五入、死亡、<b>落水即死全局规则</b>）、
    ///             §3.4（单回合两阶段行动经济）、§3.2（回合开始 evilness/action 复位、保底武器）。
    ///
    /// 【与 Godot 版的差异（3D 重写）】
    ///   · <c>CharacterBody3D</c> + 手搓 <c>_THROWN_GRAVITY</c> → Unity <see cref="Rigidbody"/>，
    ///     重力交给 PhysX（<c>useGravity</c> + <c>Physics.gravity</c>），不逐帧改 velocityY。
    ///   · 速度换算统一走 <see cref="LevelGeometry"/>（预览与实弹同源）。
    ///   · 角色被约束在战斗平面（XY，z 固定），保留 z 仅作表现深度——对应原版纯 2D 物理。
    ///   · 保底武器由 <see cref="ResetForTurnStart"/> 调用 <see cref="WeaponInventory.EnsureFallbackWeapon"/> 完成。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PirateBase : MonoBehaviour
    {
        [Header("身份（运行时由 BattleController.Initialize 覆盖）")]
        [SerializeField] string crewType = "redPirate";
        [SerializeField] int teamIndex;
        [SerializeField] int luck = CrewCatalog.DefaultLuck;
        [SerializeField] int maxHealth = CrewCatalog.MaxHealth;

        [Header("物理")]
        [SerializeField] float weight = CrewCatalog.Weight;
        [SerializeField] Rigidbody body;
        [SerializeField] Collider bodyCollider;
        [Tooltip("是否约束旋转（保持直立）。位置**不**约束——3D 化后 XZ 水平面都能动，见 Awake。")]
        [SerializeField] bool freezeRotation = true;

        int _pirateId = -1;
        int _health;
        bool _alive;
        int _evilness;
        ActionState _action = ActionState.Start;
        WeaponInventory _inventory = new WeaponInventory();
        bool _selected;
        bool _hovered;
        bool _thrown;
        bool _weaponFired;

        /// <summary>角色运行时唯一 id（由 BattleController 分配）。</summary>
        public int PirateId => _pirateId;

        /// <summary>队伍索引：0 = 红队（team1），1 = 蓝队（team2）。</summary>
        public int TeamIndex => teamIndex;

        /// <summary>队伍编号：1 / 2（teamIndex + 1）。</summary>
        public int TeamNumber => teamIndex + 1;

        /// <summary>原版导出符号名（§4.2）。</summary>
        public string CrewType => crewType;

        /// <summary>AI 随机投掷次数基数（§4.1）。</summary>
        public int Luck => luck;

        /// <summary>重量（§4.1 = 1；击退不乘体重）。</summary>
        public float Weight => weight;

        /// <summary>当前生命值。</summary>
        public int Health => _health;

        /// <summary>最大生命值（§4.1 = 100）。</summary>
        public int MaxHealth => maxHealth;

        /// <summary>是否存活。</summary>
        public bool Alive => _alive;

        /// <summary>邪恶度（§3.2 每回合清零；§5.3 被爆炸命中累加 falloff）。</summary>
        public int Evilness => _evilness;

        /// <summary>本回合行动经济状态（§3.4）。</summary>
        public ActionState CurrentAction => _action;

        /// <summary>是否已被抛出（表现层标记）。</summary>
        public bool Thrown => _thrown;

        /// <summary>是否已使用武器（表现层标记）。</summary>
        public bool WeaponFired => _weaponFired;

        /// <summary>武器背包（§5.5）。</summary>
        public WeaponInventory Inventory => _inventory;

        /// <summary>是否被玩家选中。</summary>
        public bool Selected => _selected;

        /// <summary>是否处于鼠标悬停。</summary>
        public bool Hovered => _hovered;

        /// <summary>刚体引用。</summary>
        public Rigidbody Body => body;

        /// <summary>碰撞体引用。</summary>
        public Collider BodyCollider => bodyCollider;

        /// <summary>生命值变化（本地事件，仅血条/UI 关心）。</summary>
        public event Action<PirateBase, int, int> HealthChanged;

        /// <summary>死亡（本地事件；同时通过 EventBus 广播 <see cref="BattleEvents.CrewDied"/>）。</summary>
        public event Action<PirateBase> Died;

        void Awake()
        {
            // 同对象自动绑定，避免 Inspector 漏拖；不是跨模块 GetComponent。
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<Collider>();

            if (freezeRotation && body != null)
            {
                // 只锁旋转（保持直立），**不锁位置**：3D 化后竞技场是 XZ 水平面，角色要能沿 X 和 Z 两个
                // 方向被抛飞/滑行。原先 FreezePositionZ 是"2D 侧视"时代的遗留，会把深度方向焊死
                // （详见 docs/M2-3D空间模型对齐.md）。
                body.constraints = RigidbodyConstraints.FreezeRotationX
                                   | RigidbodyConstraints.FreezeRotationY
                                   | RigidbodyConstraints.FreezeRotationZ;
            }

            _health = maxHealth;
            _alive = true;
            if (body != null)
                body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>
        /// 由 <see cref="BattleController"/> 按出战计划初始化（§4.1 共享属性 + §5.5 初始武器）。
        /// </summary>
        public void Initialize(int pirateId, in SpawnPlanEntry entry)
        {
            _pirateId = pirateId;
            crewType = entry.TypeName;
            teamIndex = entry.TeamIndex;
            luck = entry.Luck;

            maxHealth = CrewCatalog.MaxHealth;   // §4.1 所有海盗统一 100
            weight = CrewCatalog.Weight;         // §4.1 统一 1
            _health = maxHealth;
            _alive = true;
            _evilness = 0;
            _action = ActionState.Start;
            _inventory = new WeaponInventory(entry.InitialWeapons);

            transform.position = entry.WorldPosition;
            gameObject.name = entry.TypeName + "_T" + entry.TeamIndex + "_" + pirateId;

            if (body != null)
            {
                body.useGravity = true;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        // ------------------------------------------------------------------
        // §4.4 生命 / 死亡 / 落水
        // ------------------------------------------------------------------

        /// <summary>
        /// §4.4 <c>subtractHealth</c>：<c>health -= round(amount)</c>；≤0 则置 0 并死亡。
        /// AS2 <c>Math.round</c> 是四舍五入（.5 向上），这里用 <c>floor(x+0.5)</c> 精确对齐。
        /// </summary>
        /// <returns>本次是否导致死亡。</returns>
        public bool SubtractHealth(float amount)
        {
            if (!_alive)
                return false;

            int rounded = Mathf.FloorToInt(amount + 0.5f);
            _health -= rounded;
            if (_health <= 0)
            {
                _health = 0;
                HealthChanged?.Invoke(this, _health, maxHealth);
                EventBus.Publish(BattleEvents.CrewDamaged, new CrewDamagedPayload(
                    _pirateId, teamIndex, rounded, _health, maxHealth));
                Kill();
                return true;
            }

            HealthChanged?.Invoke(this, _health, maxHealth);
            EventBus.Publish(BattleEvents.CrewDamaged, new CrewDamagedPayload(
                _pirateId, teamIndex, rounded, _health, maxHealth));
            return false;
        }

        /// <summary>直接死亡（§4.4；也用于落水）。幂等。</summary>
        public void Kill()
        {
            if (!_alive)
                return;

            _alive = false;
            _selected = false;
            _hovered = false;

            Died?.Invoke(this);
            EventBus.Publish(BattleEvents.CrewDied, new CrewDiedPayload(_pirateId, teamIndex, crewType));
        }

        /// <summary>
        /// §4.4 落水即死（<b>全局规则</b>）：世界 y 低于水面世界 y 即死亡。
        /// </summary>
        /// <returns>本次调用是否触发落水死亡。</returns>
        public bool CheckWaterDeath(float waterWorldY)
        {
            if (!_alive)
                return false;

            if (LevelGeometry.IsBelowWater(transform.position.y, waterWorldY))
            {
                Kill();
                return true;
            }

            return false;
        }

        /// <summary>§5.3 累加邪恶度（施暴者）；§3.2 每回合开始时清零。</summary>
        public void AddEvilness(float amount)
        {
            if (amount <= 0f)
                return;
            _evilness += Mathf.RoundToInt(amount);
        }

        // ------------------------------------------------------------------
        // §3.2 / §3.4 回合与行动
        // ------------------------------------------------------------------

        /// <summary>
        /// §3.2 startTurn 对单个角色的重置：行动经济复位、evilness = 0、补保底武器、取消选中/装备。
        /// </summary>
        public void ResetForTurnStart()
        {
            _action = ActionState.Start;
            _evilness = 0;
            _thrown = false;
            _weaponFired = false;
            _inventory.Unequip();
            _inventory.EnsureFallbackWeapon();
        }

        /// <summary>§3.4 抛自己：thrown=true、canThrow=false（回合继续）。</summary>
        public void MarkThrowSelf()
        {
            _action = TurnRules.ApplyThrowSelf(_action);
            _thrown = true;
        }

        /// <summary>§3.4 用武器：结束回合。返回被消耗的武器 id。</summary>
        public bool MarkUseWeapon(out WeaponId usedWeapon)
        {
            _action = TurnRules.ApplyUseWeapon(_action);
            _weaponFired = true;

            if (_inventory.TryGetEquipped(out usedWeapon))
            {
                _inventory.ConsumeEquipped();
                return true;
            }

            usedWeapon = default;
            return false;
        }

        /// <summary>§3.4 点 end go：立即结束回合。</summary>
        public void MarkEndGo()
        {
            _action = TurnRules.ApplyEndGo(_action);
        }

        // ------------------------------------------------------------------
        // 选中 / 悬停（§4.5）
        // ------------------------------------------------------------------

        /// <summary>设置选中状态（描边表现由外部渲染层负责，这里只维护状态位）。</summary>
        public void SetSelected(bool value)
        {
            _selected = value;
        }

        /// <summary>设置悬停状态。</summary>
        public void SetHover(bool value)
        {
            _hovered = value;
        }

        // ------------------------------------------------------------------
        // 物理（§5.1 / §5.3）
        // ------------------------------------------------------------------

        /// <summary>
        /// §5.1 施加初速。传 **Flash 平面初速 (vx, vy)（px/帧）**，由
        /// <see cref="LevelGeometry.FlashLaunchVelocityToWorld"/> 统一换算成 3D 世界初速
        /// （含仰角抬升）——与弹体、预览共用同一个入口，保证三者弹道口径一致。
        /// 使用 <see cref="ForceMode.Impulse"/>：Δv = impulse / mass，与质量无关。
        /// </summary>
        public void ApplyLaunchVelocity(float vxPixelsPerFrame, float vyPixelsPerFrame)
        {
            if (body == null || body.isKinematic)
                return;

            Vector3 deltaV = LevelGeometry.FlashLaunchVelocityToWorld(vxPixelsPerFrame, vyPixelsPerFrame);
            if (deltaV == Vector3.zero)
                return;

            body.AddForce(deltaV * body.mass, ForceMode.Impulse);
        }

        /// <summary>
        /// §5.3 施加击退速度增量。传入的已是 Unity 世界向量
        /// （由 BattleController 用 <see cref="LevelGeometry.FlashVelocityDeltaToArena"/> 换算而来）。
        /// </summary>
        public void ApplyImpulseDelta(Vector3 worldDeltaVelocity)
        {
            if (body == null || body.isKinematic)
                return;

            body.AddForce(worldDeltaVelocity * body.mass, ForceMode.Impulse);
        }

        /// <summary>是否处于可见运动（供 TurnManager 的 inactivity 判定）。</summary>
        public bool IsMoving(float thresholdSqr = 0.01f)
        {
            if (body == null)
                return false;
            if (body.IsSleeping())
                return false;
            return body.velocity.sqrMagnitude > thresholdSqr;
        }
    }
}
