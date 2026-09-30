using PirateCrew.Combat;
using PirateCrew.Core;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 标准炸弹弹体运行时（MonoBehaviour 薄壳）。
    ///
    /// 【单一弹体】全部 <see cref="WeaponId"/> 都收敛到同一条路径：
    /// 抛出 → 重力弹道（<see cref="ProjectileProfile"/> 驱动的 PhysX 配置）→
    /// 撞地/静止引爆（<see cref="ProjectileTriggerRules"/> 桥接 <see cref="WeaponTriggerRules"/>）→
    /// <see cref="BattleController.ResolveExplosion"/> 结算爆炸（击退/固定抬升/邪恶度/连锁全在其内）。
    /// 「点哪把都是它」——武器 id 只随弹体透传供日志与表现辨识。
    ///
    /// 【分层】可测规则全在纯 C#：<see cref="ProjectileProfile"/>（标准口径 → PhysX 参数）、
    /// <see cref="ProjectileTriggerRules"/>（引爆判定桥）、<see cref="ProjectileLifetimeRules"/>（寿命）、
    /// <see cref="ProjectileSpawnPlanner"/>（生成计划）、<see cref="StandardBombRules"/>（数值单一规则源）。
    /// 本类只做：PhysX 配置、碰撞回调、逐帧组装触发上下文、引爆时调结算入口。
    ///
    /// 【程序化兜底】弹体可由 <c>BattleController.projectilePrefab</c> 提供；为空时由
    /// <see cref="BattleController"/> 用图元 + 颜色程序化构建，因此<b>既有场景无需重新装配</b>。
    ///
    /// 【3D 运动语义（见 docs/设计/3D空间模型对齐.md）】
    ///   · 竞技场是 <b>XZ 水平面</b>、重力沿 <b>-Y</b>：弹体在 X/Z 上惯性飞行、在 Y 上受重力。
    ///   · 刚体<b>只锁旋转、不锁位置</b>。
    ///
    /// 【Flash 帧口径】引爆判定输入（接触/静止）在物理步与渲染帧各取所长：
    ///   重力在 <c>FixedUpdate</c>（fixedDeltaTime=0.04 = Flash 25fps 帧）施加；
    ///   输入轮询（无点击引爆路径后已无）与出界/落水清退在 <c>Update</c>。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class WeaponProjectile : MonoBehaviour
    {
        /// <summary>生成后前若干帧不判接触，避免与投掷者/地面初始化重叠误爆。</summary>
        const int SpawnGraceFrames = 2;

        /// <summary>
        /// 视为「静止」的世界总速度阈值（Flash 0.2px/帧 ≈ 0.15625 世界单位/秒，这里取 0.2 略宽）。
        /// </summary>
        const float RestSnapSpeed = 0.2f;

        /// <summary>通用弹体的出界判定的宽松边距（世界单位）。</summary>
        const float OutOfMapMargin = 4f;

        [SerializeField] Rigidbody body;
        [SerializeField] Collider hitCollider;

        WeaponStats _stats;
        ProjectileProfile _profile;
        BattleController _battle;
        PirateBase _owner;

        bool _detonated;
        bool _initialized;
        int _armedFrame;
        bool _contact;

        /// <summary>武器 id。</summary>
        public WeaponId WeaponId => _stats.Id;

        /// <summary>是否仍在本回合物理运动（用于 <see cref="BattleController.IsAnythingActive"/>）。
        /// 阈值同 <see cref="WeaponTriggerRules.AtRestSqrMagnitudeEpsilon"/>（3D 侧静止判据，与 PirateBase.IsMoving 共用）。</summary>
        public bool IsInFlight
        {
            get
            {
                if (_detonated || body == null || body.isKinematic)
                    return false;
                return body.velocity.sqrMagnitude > WeaponTriggerRules.AtRestSqrMagnitudeEpsilon;
            }
        }

        /// <summary>是否会在被爆炸命中时连锁引爆（数据锚 <c>Trigger</c> 含 OnExplosionHit 的武器保留此语义）。</summary>
        public bool TriggersOnBlast => ProjectileTriggerRules.CanChainFromBlast(_stats.Trigger);

        /// <summary>
        /// 把 3D 世界速度折算成 Flash「静止」判据需要的 (vx, vy)（px/帧）。
        ///   · vx = XZ 平面速度模长；vy = 世界 Y 速度 / FlashSpeedScale。
        /// 纯函数（不触实例状态），供无头测试直接验证口径。
        /// </summary>
        public static void FlashRestComponents(Vector3 worldVelocity, out float vx, out float vy)
        {
            Vector2 flat = LevelGeometry.ArenaVelocityToFlash(worldVelocity);
            vx = flat.magnitude;
            vy = worldVelocity.y / LevelGeometry.FlashSpeedScale;
        }

        void Awake()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (hitCollider == null)
                hitCollider = GetComponent<Collider>();
        }

        /// <summary>由 <see cref="BattleController"/> 生成后初始化。</summary>
        public void Initialize(
            WeaponStats stats, BattleController battle, PirateBase owner, bool placed, Vector3 worldVelocity)
        {
            _stats = stats;
            _profile = ProjectileProfile.FromStats(stats);
            _battle = battle;
            _owner = owner;
            _detonated = false;
            _initialized = true;
            _armedFrame = Time.frameCount + SpawnGraceFrames;

            if (body == null)
                body = GetComponent<Rigidbody>();
            if (hitCollider == null)
                hitCollider = GetComponent<Collider>();

            if (body != null)
            {
                body.mass = _profile.Mass;
                // 2022.3 的 Rigidbody 没有 gravityScale（Unity 6 才加入），
                // 故关闭 useGravity，改由 FixedUpdate 按标准口径手动施加加速度。
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeRotationX
                                   | RigidbodyConstraints.FreezeRotationY
                                   | RigidbodyConstraints.FreezeRotationZ;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = placed
                    ? CollisionDetectionMode.Discrete
                    : CollisionDetectionMode.ContinuousDynamic;
                body.isKinematic = placed;
                body.velocity = placed ? Vector3.zero : worldVelocity;
                body.angularVelocity = Vector3.zero;
            }

            // 不与投掷者自身碰撞，避免生成瞬间自爆 / 被自己挡下。
            if (hitCollider != null && owner != null && owner.BodyCollider != null)
                Physics.IgnoreCollision(hitCollider, owner.BodyCollider, true);

            gameObject.name = "WeaponProjectile_" + stats.DisplayName + (placed ? "_placed" : "");
        }

        void FixedUpdate()
        {
            if (!_initialized || _detonated || body == null)
                return;

            // 标准炸弹恒吃重力：按 profile 的重力倍率（= 标准重量口径）施加全局重力。
            if (!body.isKinematic && _profile.UsesGravity)
                body.AddForce(Physics.gravity * _profile.GravityScale, ForceMode.Acceleration);
        }

        void Update()
        {
            if (!_initialized || _detonated || body == null)
                return;

            if (Time.frameCount < _armedFrame)
            {
                _contact = false;
                return;
            }

            HandleWaterAndBounds();
            if (_detonated)
                return;

            // 静止引爆判据在 Flash 像素速度域（vx==0 且 |vy|<0.2）；世界总速度低于
            // 静止阈值时直接按零速处理，避免低速爬行永远到不了"严格为 0"的判据。
            FlashRestComponents(body.velocity, out float vx, out float vy);
            if (body.velocity.sqrMagnitude < RestSnapSpeed * RestSnapSpeed)
            {
                vx = 0f;
                vy = 0f;
            }

            // BlastHit 恒传 false：被爆炸命中的弹体走 DetonateFromBlast 直接引爆（同帧销毁），
            // 不会再回到 Update 的判定路径——规则层的 BlastContact 判据只由那条直调路径消费。
            var context = new TriggerContext(_contact, clicked: false, blastHit: false, vx, vy);

            if (ProjectileTriggerRules.ShouldDetonateThisFrame(StandardBombRules.Trigger, context))
            {
                Detonate();
                return;
            }

            _contact = false;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!_initialized || _detonated || collision == null || collision.collider == null)
                return;

            _contact = true;
        }

        void OnCollisionStay(Collision collision)
        {
            if (!_initialized || _detonated || collision == null || collision.collider == null)
                return;
            _contact = true;
        }

        void OnDestroy()
        {
            _battle?.UnregisterProjectile(this);
        }

        // ==================================================================
        // 出界 / 落水
        // ==================================================================

        void HandleWaterAndBounds()
        {
            if (_battle == null)
                return;

            // 落水即熄火消失（不引爆）：水面阈值方向无关（3D 空间模型对齐 §4）。
            if (LevelGeometry.IsBelowWater(transform.position.y, _battle.WaterWorldY))
            {
                Vanish();
                return;
            }

            BattlePlan plan = _battle.Plan;
            if (plan == null)
                return;

            Vector3 p = transform.position;
            bool outOfMap = p.x < -OutOfMapMargin
                            || p.x > plan.WorldWidth + OutOfMapMargin
                            || p.z < -OutOfMapMargin
                            || p.z > plan.WorldDepth + OutOfMapMargin
                            || p.y < LevelGeometry.WaterSurfaceY - OutOfMapMargin;

            if (outOfMap)
                Vanish();
        }

        /// <summary>离开场上（落水/出界）：标记已消费并销毁，避免同帧再走引爆逻辑。</summary>
        void Vanish()
        {
            _detonated = true;
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------
        // 引爆
        // ------------------------------------------------------------------

        /// <summary>被爆炸命中触发连锁引爆（数据锚触发里带 OnExplosionHit 的武器；由 <see cref="BattleController.ResolveExplosion"/> 调用）。</summary>
        public void DetonateFromBlast()
        {
            if (_detonated || !TriggersOnBlast)
                return;
            // 直接引爆而非置标志等 Update 判定：爆炸回调发生在别处的事件栈里，
            // 本帧内弹体随即销毁，Update 不会再读到任何"被炸"标志。
            Detonate();
        }

        /// <summary>引爆：走 <see cref="BattleController.ResolveExplosion"/>，然后移除自身。</summary>
        void Detonate()
        {
            if (_detonated)
                return;
            _detonated = true;

            if (_battle != null)
            {
                PirateBase caster = _owner != null && _owner.Alive ? _owner : null;
                _battle.ResolveExplosion(
                    transform.position,
                    StandardBombRules.ExplosionSize,
                    StandardBombRules.ExplosionMaxDamage,
                    caster,
                    this);
            }

            EventBus.Publish(BattleEvents.ProjectileDetonated, new ProjectileDetonatedPayload(
                _stats.Id, transform.position));

            Destroy(gameObject);
        }
    }
}
