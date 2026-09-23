using System;
using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using PirateCrew.Visual;
using UnityEngine;

namespace PirateCrew.Battle
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
    ///   · 竞技场是 XZ 水平面（地面顶面 y=0，重力沿 -Y），位置**不**约束、只锁旋转保持直立
    ///     ——见 docs/3D空间模型对齐.md。
    ///   · 保底武器由 <see cref="ResetForTurnStart"/> 调用 <see cref="WeaponInventory.EnsureFallbackWeapon"/> 完成。
    ///   · 落地翻滚 / 落水死亡演出（M4 §3.1，忠实转写 Flash 逆向）：刚体旋转保持冻结，
    ///     翻滚与演出全部作用在运行时创建的视觉滚动 Pivot 上（<see cref="RollRules"/> 出换算，
    ///     本类只做接地检测与 Rigidbody 线速度 / 视觉 Transform 的胶水）。
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

        [Header("视觉（表现层，不参与玩法判定）")]
        [Tooltip("代码驱动动画组件；留空则 Awake 时从子节点抓。仅用于投掷/受击的表现通知。")]
        [SerializeField] CrewVisualAnimator visualAnimator;

        [Header("落地翻滚（M4 §3.1，忠实转写 Flash 逆向）")]
        [Tooltip("总开关：关闭后无翻滚、无落地弹跳/摩擦转写、无落水旋转演出。")]
        [SerializeField] bool enableRolling = true;

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

        // ---- 落地翻滚 / 落水演出（规则全在 RollRules，这里只存推进状态）----
        Transform _rollPivot;                // 视觉滚动枢轴（Awake 运行时包在 Visual 与 Body 之间）
        Quaternion _rollRotation = Quaternion.identity;
        float _rollAngle;                    // 标量滚动角（度；语义见 RollRules 类头）
        float _groundDampPendingSeconds;     // 接地阻尼的步进余数
        bool _groundContactQueued;           // OnCollision* 队列的"法线朝上接触"，下一物理步消费
        Vector3 _preStepVelocity;            // 本物理步步前速度（反弹用它近似碰撞前 vy）
        bool _drownPerforming;               // 落水死亡旋转下沉演出进行中
        float _drownSpinSpeed;               // 演出角速度（度/秒，落水瞬间定格）
        Vector3 _drownSpinAxis = Vector3.forward;
        /// <summary>接触点临时列表（grow-only）：OnCollision* 每物理帧都触发，<c>collision.contacts</c>
        /// 每次分配新 <c>ContactPoint[]</c>，改用预分配列表 + <see cref="Collision.GetContacts"/>。</summary>
        readonly List<ContactPoint> _contactScratch = new List<ContactPoint>(8);

        /// <summary>"法线朝上"的接触判定阈：normal.y ≥ 0.5（约 ≤60° 斜面按地面处理，提案）。</summary>
        const float GroundNormalMinUp = 0.5f;

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
            if (visualAnimator == null)
                visualAnimator = GetComponentInChildren<CrewVisualAnimator>(true);

            if (freezeRotation && body != null)
            {
                // 只锁旋转（保持直立），**不锁位置**：3D 化后竞技场是 XZ 水平面，角色要能沿 X 和 Z 两个
                // 方向被抛飞/滑行。原先 FreezePositionZ 是"2D 侧视"时代的遗留，会把深度方向焊死
                // （详见 docs/3D空间模型对齐.md）。
                body.constraints = RigidbodyConstraints.FreezeRotationX
                                   | RigidbodyConstraints.FreezeRotationY
                                   | RigidbodyConstraints.FreezeRotationZ;
            }

            _health = maxHealth;
            _alive = true;
            if (body != null)
                body.interpolation = RigidbodyInterpolation.Interpolate;

            // M4 §3.1：运行时在 Visual 与 Body 之间包一层滚动 Pivot（不改 CrewVisualPrefabBuilder 产物）。
            if (enableRolling)
                CreateRollPivot();
        }

        /// <summary>
        /// 在 <c>CrewVisualRig</c> 的 Visual 与 Body 之间插入滚动 Pivot。
        /// Pivot 的 localScale 取 Visual localScale 的逐分量倒数，恰好抵消 Visual 的非均匀缩放——
        /// Pivot 之下的数值矩阵 = 单位阵，任意刚体旋转都不产生剪切；静止时 Body 的世界矩阵与
        /// 插入前完全一致，<see cref="CrewVisualAnimator"/> 对 rig.Body 的读写无感知。
        /// </summary>
        void CreateRollPivot()
        {
            if (_rollPivot != null)
                return;

            CrewVisualRig rig = GetComponentInChildren<CrewVisualRig>(true);
            if (rig == null || rig.Body == null)
                return;

            Transform visual = rig.transform;
            Vector3 visualScale = visual.localScale;

            _rollPivot = new GameObject("RollPivot").transform;
            _rollPivot.SetParent(visual, false);
            _rollPivot.localPosition = Vector3.zero;
            _rollPivot.localRotation = Quaternion.identity;
            _rollPivot.localScale = new Vector3(
                visualScale.x > 1e-5f ? 1f / visualScale.x : 1f,
                visualScale.y > 1e-5f ? 1f / visualScale.y : 1f,
                visualScale.z > 1e-5f ? 1f / visualScale.z : 1f);

            rig.Body.SetParent(_rollPivot, false);
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
            Drowned = false;

            // 翻滚/演出状态复位（战斗重建时不得残留上一场的滚动角与落水演出）。
            _drownPerforming = false;
            SnapRollUpright();

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
            if (visualAnimator != null)
                visualAnimator.NotifyHit();
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
                Drowned = true;   // 表现层据此播"下沉"而不是"倒地"（docs/角色造型规范.md §4）
                BeginDrownPerformance();   // M4 §3.1：落水死亡旋转下沉演出（Character.as:164-180 转写）
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
            // M4 §3.1 瞄准保护：单位成为当前行动者时滚动角必须已归零。
            // 原版靠落地接触阻尼自然收敛；这里在回合边界主动补一个保证（空中被开回合的边角情况也归零）。
            SnapRollUpright();
        }

        /// <summary>§3.4 抛自己：thrown=true、canThrow=false（回合继续）。</summary>
        public void MarkThrowSelf()
        {
            _action = TurnRules.ApplyThrowSelf(_action);
            _thrown = true;
            if (visualAnimator != null)
                visualAnimator.NotifyThrow();
        }

        /// <summary>§3.4 用武器：结束回合。返回被消耗的武器 id。</summary>
        public bool MarkUseWeapon(out WeaponId usedWeapon)
        {
            _action = TurnRules.ApplyUseWeapon(_action);
            _weaponFired = true;
            if (visualAnimator != null)
                visualAnimator.NotifyThrow();

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

        // ------------------------------------------------------------------
        // 落地翻滚 / 落水演出（M4 §3.1；换算与推进规则全在 RollRules）
        // ------------------------------------------------------------------

        /// <summary>
        /// 翻滚推进（每个物理步）：
        ///   1. 恒转——空中与地面同源，转速 = 48°/s × 水平速度（Flash <c>rotation += vx*3</c>）；
        ///   2. 接地阻尼——接地期间每 0.04s 滚动角 ×0.5、&lt;1° 归零（Character.as:685-697）；
        ///   3. 地面线性摩擦——直接衰减刚体水平速度，78.125 u/s²（Solid.as:269 转写）。
        /// 刚体旋转保持冻结，全部旋转只写视觉滚动 Pivot。
        /// </summary>
        void FixedUpdate()
        {
            // 消费上一步 OnCollision* 队列的"法线朝上接触"（物理步之后触发，故滞后一步，物理语义足够）。
            bool grounded = _groundContactQueued;
            _groundContactQueued = false;

            if (body != null && !body.isKinematic)
                _preStepVelocity = body.velocity;   // 物理步步前速度；反弹用它近似碰撞前 vy

            if (!enableRolling || _rollPivot == null)
                return;

            if (_drownPerforming)
            {
                AdvanceDrownPerformance();
                return;
            }

            if (body == null || body.isKinematic)
                return;

            Vector3 velocity = body.velocity;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            float horizontalSpeed = flat.magnitude;
            float dt = Time.fixedDeltaTime;

            // 1) 恒转（前滚翻：轴 = up × 水平速度）。
            if (horizontalSpeed > 1e-4f)
            {
                _rollAngle = RollRules.AdvanceRollAngle(_rollAngle, horizontalSpeed, dt);
                Vector3 axis = RollRules.RollAxis(velocity);
                if (axis.sqrMagnitude > 0.5f)
                {
                    float deltaDegrees = RollRules.SpinDegreesPerSecond(horizontalSpeed) * dt;
                    _rollRotation = Quaternion.AngleAxis(deltaDegrees, axis) * _rollRotation;
                }
            }

            // 2) 接地阻尼（角减半时视觉旋转同步朝直立收敛一半）。
            if (grounded && _rollAngle > 0f)
            {
                float before = _rollAngle;
                _rollAngle = RollRules.DampGroundAngle(
                    _rollAngle, dt, ref _groundDampPendingSeconds);
                if (_rollAngle <= 0f)
                {
                    _rollRotation = Quaternion.identity;
                }
                else if (before > 1e-5f)
                {
                    float keep = Mathf.Clamp01(Mathf.Abs(_rollAngle / before));
                    _rollRotation = Quaternion.Slerp(Quaternion.identity, _rollRotation, keep);
                }
            }

            // 3) 地面线性摩擦：只衰减水平分量，方向不变（|vx| -= 2 px/接触帧 的连续等效）。
            if (grounded && horizontalSpeed > 1e-6f)
            {
                float after = RollRules.GroundSpeedAfterFriction(horizontalSpeed, dt);
                if (after < horizontalSpeed)
                    body.velocity = flat * (after / horizontalSpeed) + Vector3.up * velocity.y;
            }

            if (_rollPivot.localRotation != _rollRotation)
                _rollPivot.localRotation = _rollRotation;
        }

        void OnCollisionEnter(Collision collision)
        {
            HandleRollContact(collision, isNewContact: true);
        }

        void OnCollisionStay(Collision collision)
        {
            HandleRollContact(collision, isNewContact: false);
        }

        /// <summary>
        /// 接触结算：任一接触点法线朝上即视为"接地"（供阻尼/摩擦），并在<b>开始接触</b>时施加
        /// 落地反弹 <c>vy → -0.2·vy</c>（Solid.as:263-276 转写；用步前速度近似碰撞前 vy，
        /// 因为 OnCollision 回调时 PhysX 已把法向速度清零）。持续接触不重复反弹（提案取舍：原版
        /// 逐接触帧翻转的微震荡在 Unity 里表现为贴地稳定，观感一致且不会阻碍刚体入睡）。
        /// </summary>
        void HandleRollContact(Collision collision, bool isNewContact)
        {
            if (!enableRolling || body == null || body.isKinematic || _drownPerforming)
                return;

            // GetContacts 填入预分配列表（先手动清空保证内容恰为本次接触点，零 GC 分配），
            // 与 collision.contacts 是同一批接触点：判定语义不变——任一接触点法线朝上即接地。
            _contactScratch.Clear();
            collision.GetContacts(_contactScratch);

            float bestUp = 0f;
            for (int i = 0; i < _contactScratch.Count; i++)
                bestUp = Mathf.Max(bestUp, _contactScratch[i].normal.y);
            if (bestUp < GroundNormalMinUp)
                return;

            _groundContactQueued = true;

            if (isNewContact && _preStepVelocity.y < -1e-4f)
            {
                float upSpeed = RollRules.LandBounceUpSpeed(-_preStepVelocity.y);
                Vector3 v = body.velocity;
                body.velocity = new Vector3(v.x, upSpeed, v.z);
            }
        }

        /// <summary>
        /// 落水死亡旋转下沉演出（Character.as:164-180 转写）：转速 = 64°/s × 合速度（落水瞬间定格），
        /// 全速度每 0.04s ×0.8，且竖直速度钳到至少 2.34375 u/s（=1.5 px/帧）持续下沉。
        /// </summary>
        void BeginDrownPerformance()
        {
            if (!enableRolling)
                return;

            _drownPerforming = true;
            Vector3 velocity = body != null ? body.velocity : Vector3.zero;
            _drownSpinSpeed = RollRules.WaterSpinDegreesPerSecond(velocity.magnitude);
            _drownSpinAxis = RollRules.RollAxis(velocity);
            if (_drownSpinAxis.sqrMagnitude < 0.5f)
                _drownSpinAxis = Vector3.forward;   // 近垂直入水：兜底绕世界 Z 侧滚，演出保持稳定
        }

        void AdvanceDrownPerformance()
        {
            float dt = Time.fixedDeltaTime;

            if (body != null && !body.isKinematic)
            {
                Vector3 v = body.velocity * RollRules.WaterDampFactor(dt);
                v.y = Mathf.Min(v.y, -RollRules.WaterSinkSpeedUnitsPerSecond);
                body.velocity = v;
            }

            float deltaDegrees = _drownSpinSpeed * dt;
            if (deltaDegrees > 0f)
                _rollRotation = Quaternion.AngleAxis(deltaDegrees, _drownSpinAxis) * _rollRotation;

            if (_rollPivot != null)
                _rollPivot.localRotation = _rollRotation;
        }

        /// <summary>
        /// 瞄准保护（M4 §3.1）：把滚动角/滚动旋转立即归零。
        /// 由 <see cref="ResetForTurnStart"/> 在回合边界调用，保证"单位成为当前行动者且静止时滚动角已归零"。
        /// </summary>
        public void SnapRollUpright()
        {
            _rollAngle = 0f;
            _groundDampPendingSeconds = 0f;
            _rollRotation = Quaternion.identity;
            if (_rollPivot != null)
                _rollPivot.localRotation = Quaternion.identity;
        }

        // ------------------------------------------------------------------
        // 表现层通知（仅视觉，不改玩法状态；见 docs/角色造型规范.md §4）
        // ------------------------------------------------------------------

        /// <summary>是否因落水而死（表现层据此播"下沉"而不是"倒地"）。</summary>
        public bool Drowned { get; private set; }
    }
}
