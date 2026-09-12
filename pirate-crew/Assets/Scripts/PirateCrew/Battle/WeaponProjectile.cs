using PirateCrew.Core;
using PirateCrew.PirateCrew.Combat;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 武器弹体运行时（MonoBehaviour 薄壳）。
    ///
    /// 【对应章节】静态逆向文档 §5.2（17 武器总表的触发/物理/爆炸列）、§5.3（爆炸结算，委派
    ///             <see cref="BattleController.ResolveExplosion"/>）、§5.4（落水/出界处置）。
    ///
    /// 【分层】所有可测规则在纯 C# 里：
    ///   <see cref="ProjectileProfile"/>（参数推导）、<see cref="ProjectileTriggerRules"/>（引爆判定，
    ///   复用 <see cref="WeaponTriggerRules.ShouldDetonate"/>）、<see cref="ProjectileLifetimeRules"/>、
    ///   <see cref="ProjectileSpawnPlanner"/>。本类只做：PhysX 配置、碰撞回调、逐帧组装
    ///   <see cref="TriggerContext"/>、引爆时调用 <see cref="BattleController.ResolveExplosion"/>。
    ///
    /// 【程序化兜底】弹体可由 <c>BattleController.projectilePrefab</c> 提供；为空时由
    ///   <see cref="BattleController"/> 用图元 + 颜色程序化构建，因此<b>既有场景无需重新装配</b>。
    ///
    /// 【3D 运动语义（见 docs/M2-3D空间模型对齐.md）】
    ///   · 竞技场是 <b>XZ 水平面</b>、重力沿 <b>-Y</b>：弹体在 X/Z 上惯性飞行、在 Y 上受重力
    ///     （<see cref="ProjectileProfile.UsesGravity"/> 时，见 <see cref="FixedUpdate"/>）。
    ///   · 刚体<b>只锁旋转、不锁位置</b>：FreezePositionZ 是"2D 侧视"时代的遗留，会把弹体钉死在
    ///     出生时的 Z 平面，永远打不到纵深方向的目标。
    ///   · 「静止」判据（dynamite / banana 的 OnRest）不按 XY 平面读速度，口径见
    ///     <see cref="FlashRestComponents"/>。
    ///   · 地面碰撞/弹跳/落水在 XZ 地面上成立：碰撞与弹跳交给 PhysX（重力 -Y + PhysicsMaterial），
    ///     落水用全局水面常量 <see cref="LevelGeometry.WaterSurfaceY"/>。
    ///
    /// 【已知 TODO】
    ///   · anchor / seagull / tidalWave / voodooDoll / cannon / SweepingFlame 的专用机制未实现
    ///     （见 <see cref="ProjectileProfile.SupportsGenericProjectile"/>），选到它们时不生成弹体。
    ///   · rumBottle 落地生成 2 个 SweepingFlame：待专用火焰脚本，TODO 在可扩展点
    ///     <see cref="OnPostDetonate"/> 补（本次不做，成本高于本任务边界）。
    ///   · dynamite 落水变 unlit 状态、boulder 碾压的「推到 x±32 并继承 vx」、
    ///     Flash 的「每帧 |vx|-=friction」精确摩擦语义均留 TODO。
    ///   · parachuteBomb 的「空中减速（Flash vy&gt;1 → vy-=2；vx*=0.95）」与「按住鼠标当扇子
    ///     （沿光标反方向 ±0.2）」、banana 的 AI 近距引爆条件均未实现（纯玩法增强，非空间语义）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class WeaponProjectile : MonoBehaviour
    {
        /// <summary>生成后前若干帧不判接触，避免与投掷者/地面初始化重叠误爆。</summary>
        const int SpawnGraceFrames = 2;

        /// <summary>
        /// 视为「静止」的世界总速度阈值（Flash 0.2px/帧 ≈ 0.15625 世界单位/秒，这里取 0.2 略宽）。
        /// 小于该值时把 <see cref="FlashRestComponents"/> 输出的 vx、vy 一起归零，
        /// 以喂给 <see cref="WeaponTriggerRules.IsAtRest"/> 的
        /// <c>vx==0 &amp;&amp; |vy|&lt;0.2</c> 严格判定（PhysX 静置速度不会精确为 0）。
        /// </summary>
        const float RestSnapSpeed = 0.2f;

        /// <summary>出界判定的宽松边距（世界单位）。</summary>
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
        bool _clicked;
        bool _blastHit;

        bool _fuseArmed;
        int _fuseRemaining;
        int _fuseElapsed;

        /// <summary>武器 id。</summary>
        public WeaponId WeaponId => _stats.Id;

        /// <summary>是否仍在本回合物理运动（用于 <see cref="BattleController.IsAnythingActive"/>）。
        /// 3D 下取总速度（XZ 惯性 + Y 重力），任一轴非零即算在飞。</summary>
        public bool IsInFlight
        {
            get
            {
                if (_detonated || body == null || body.isKinematic)
                    return false;
                return body.velocity.sqrMagnitude > 0.01f;
            }
        }

        /// <summary>是否会在被爆炸命中时连锁引爆（§5.2 火药桶）。</summary>
        public bool TriggersOnBlast => ProjectileTriggerRules.CanChainFromBlast(_stats.Trigger);

        /// <summary>
        /// 把 3D 世界速度折算成 Flash「静止」判据 <see cref="WeaponTriggerRules.IsAtRest"/> 需要的
        /// (vx, vy)（px/帧）。3D 口径（见 docs/M2-3D空间模型对齐.md §1/§5.2）：
        ///   · <paramref name="vx"/> = <b>XZ 平面速度模长</b>。Flash 的 vx 是"横向是否在动"的标量；
        ///     3D 的水平面有 X/Z 两个轴，任一轴有速度都算"在动"，故取平面合成模长
        ///     （先用 <see cref="LevelGeometry.ArenaVelocityToFlash"/> 拿到平面分量，再取模长）。
        ///   · <paramref name="vy"/> = <b>世界 Y 速度 / FlashSpeedScale</b>。Flash 的 vy 是重力轴分量；
        ///     3D 里重力沿 -Y，所以"下落"必须落在 vy 上。
        ///
        /// 为什么不能直接把平面重投影的 Z 分量当作 vy：竖直下落的弹体（X=Z=0、Y 很大）会被读成
        /// vx=0、vy=0 → 判定静止并在半空中引爆；而竖直下落时 vy 应显著非零——文档对 dynamite 落水的
        /// 备注正是"水里下沉不静止"。反之，只读 world.x 作 vx 会漏判沿 Z 的滑行。故取上式。
        ///
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
                // 故关闭 useGravity，改由 FixedUpdate 按 Weight 手动施加加速度（见 ApplyWeightGravity）。
                body.useGravity = false;
                // 只锁旋转（保持朝向稳定），**不锁位置**：3D 竞技场是 XZ 水平面，弹体必须能沿 X 和 Z
                // 同时位移。原先的 FreezePositionZ 是"2D 侧视"时代的遗留——它把弹体钉死在出生时的 Z
                // 平面上，使其永远打不到纵深方向的目标（见 docs/M2-3D空间模型对齐.md §1/§6）。
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
            if (!_initialized || _detonated || body == null || body.isKinematic)
                return;
            if (!_profile.UsesGravity)
                return;   // §5.2 weight=0（cannonball 等）无重力

            // 手动按 Weight 放大全局重力（weight=1.5 的 boulder；weight=1 与全局一致）。
            // 用 ForceMode.Acceleration 使加速度与质量无关。
            // 3D 下 Physics.gravity = (0, -19.53125, 0)：水平面 (X,Z) 不受影响（惯性匀速），
            // 弹体在 Y 上匀加速下落——重力竖直向下，与竞技场平面正交。
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

            // boulder 的碾压是接触时的直接伤害（§5.2），不参与通用引爆/销毁路径。
            if (_stats.Id == WeaponId.Boulder)
            {
                _contact = false;
                return;
            }

            UpdateMineFuse();
            UpdateClickTrigger();

            // 静止判据的 3D 口径：vx = XZ 平面速度模长、vy = 世界 Y（重力轴）速度，见 FlashRestComponents。
            FlashRestComponents(body.velocity, out float vx, out float vy);
            if (body.velocity.sqrMagnitude < RestSnapSpeed * RestSnapSpeed)
            {
                // PhysX 静置速度不会精确为 0，而 §5.2 的 dynamite 判据要求 vx 严格 == 0；
                // 总速度低于阈值时一并归零，把"实际已停住"喂成"判据意义上的静止"。
                vx = 0f;
                vy = 0f;
            }

            var context = new TriggerContext(
                _contact, _clicked, _blastHit, vx, vy, _fuseArmed, _fuseRemaining);

            if (ProjectileTriggerRules.ShouldDetonateThisFrame(_stats.Trigger, context))
            {
                Detonate();
                return;
            }

            _contact = false;
            _clicked = false;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!_initialized || _detonated || collision == null || collision.collider == null)
                return;

            // boulder：接触敌人 → 伤害 = |vx| * 1.5（§5.2），不引爆、不销毁。
            if (_stats.Id == WeaponId.Boulder)
            {
                Crush(collision.collider);
                return;
            }

            _contact = true;
        }

        void OnCollisionStay(Collision collision)
        {
            if (!_initialized || _detonated || collision == null || collision.collider == null)
                return;
            if (_stats.Id == WeaponId.Boulder)
                return;
            _contact = true;
        }

        void OnDestroy()
        {
            _battle?.UnregisterProjectile(this);
        }

        // ------------------------------------------------------------------
        // 触发辅助
        // ------------------------------------------------------------------

        void HandleWaterAndBounds()
        {
            if (_battle == null)
                return;

            // §4.4：水面是世界 Y 阈值（LevelGeometry.WaterSurfaceY = -0.2），方向无关——
            // 从 X 或 Z 任一侧掉出地面都会落到水面以下。弹体枢轴低于水面即按 §5.2 处置。
            if (LevelGeometry.IsBelowWater(transform.position.y, _battle.WaterWorldY))
            {
                if (ProjectileProfile.WaterBehavior(_stats) == ProjectileWaterBehavior.Detonate)
                    Detonate();
                else
                    Vanish();
                return;
            }

            BattlePlan plan = _battle.Plan;
            if (plan == null)
                return;

            Vector3 p = transform.position;
            // 3D 化后的出界判据：竞技场是 XZ 平面（X 0..WorldWidth、Z 0..WorldDepth），
            // 水平方向出界即消失；高度只对**无重力**弹体设上界——
            // 有重力的弹体必然回落，抛物线顶点本来就该允许高过竞技场
            // （实测满力 banana/parachuteBomb = 30px/帧 + ThrowLift 0.7 时顶点约 4.62 单位，
            //   若沿用旧的 y > 4 上界，它们会在上升段被误判出界、半空消失）。
            bool outOfMap = p.x < -OutOfMapMargin
                            || p.x > plan.WorldWidth + OutOfMapMargin
                            || p.z < -OutOfMapMargin
                            || p.z > plan.WorldDepth + OutOfMapMargin
                            || p.y < LevelGeometry.WaterSurfaceY - OutOfMapMargin;

            if (!outOfMap && !_profile.UsesGravity && p.y > OutOfMapMargin)
                outOfMap = true;   // 无重力弹体（如 cannonball）会一直直线飞，需要高度上界兜住

            if (outOfMap)
                Vanish();
        }

        /// <summary>离开场上（落水/出界且按表应消失）：标记已消费并销毁，避免同帧再走引爆逻辑。</summary>
        void Vanish()
        {
            _detonated = true;
            Destroy(gameObject);
        }

        /// <summary>
        /// mine 引信（§5.2）：60px 内有角色且移动 → 点燃；随后每帧推进，到点引爆。
        /// 引信点燃与蜂鸣帧序均由 <see cref="WeaponTriggerRules"/> 判定。
        /// </summary>
        void UpdateMineFuse()
        {
            if (!ProjectileTriggerRules.HasProximityFuse(_stats.Trigger) || _battle == null)
                return;

            if (!_fuseArmed)
            {
                float nearest = NearestPirateDistance();
                bool moving = nearest >= 0f && _nearestMoving;
                if (WeaponTriggerRules.ShouldStartFuse(
                        Combat.WeaponTrigger.ProximityFuse, nearest, moving))
                {
                    _fuseArmed = true;
                    _fuseRemaining = WeaponTriggerRules.MineFuseFrames;
                    _fuseElapsed = 0;
                }

                return;
            }

            if (WeaponTriggerRules.ShouldBeep(_fuseElapsed))
            {
                EventBus.Publish(BattleEvents.MineBeep, new MineBeepPayload(
                    _stats.Id, _fuseElapsed, transform.position));
            }

            _fuseRemaining = WeaponTriggerRules.TickFuse(_fuseRemaining);
            _fuseElapsed++;
        }

        bool _nearestMoving;

        /// <summary>最近存活角色的距离（Flash px；无则 -1），并记录其是否在移动。
        /// 3D 语义：<see cref="Physics.OverlapSphere"/> 与 <see cref="Vector3.Distance"/> 本就是三维判定
        /// （球半径 60px → 1.875 世界单位，距离含高度差），无需按平面改写；单位都贴地，
        /// 高度差在正常战斗中可忽略，但判据本身已是 3D。</summary>
        float NearestPirateDistance()
        {
            _nearestMoving = false;
            if (_battle == null)
                return -1f;

            float radiusWorld = LevelGeometry.PixelsToUnits(WeaponTriggerRules.ProximityRadius);
            Collider[] overlaps = Physics.OverlapSphere(
                transform.position, radiusWorld, _battle.PirateLayerMask, QueryTriggerInteraction.Ignore);

            float best = -1f;
            for (int i = 0; i < overlaps.Length; i++)
            {
                PirateBase pirate = overlaps[i] != null
                    ? overlaps[i].GetComponentInParent<PirateBase>()
                    : null;
                if (pirate == null || pirate == _owner || !pirate.Alive)
                    continue;

                float distanceWorld = Vector3.Distance(transform.position, pirate.transform.position);
                float distancePixels = LevelGeometry.UnitsToPixels(distanceWorld);
                if (best < 0f || distancePixels < best)
                {
                    best = distancePixels;
                    _nearestMoving = pirate.IsMoving();
                }
            }

            return best;
        }

        /// <summary>banana 的玩家点击引爆（§5.2）：鼠标按下且射线命中自身即标记。
        /// 3D 语义：<c>Camera.ScreenPointToRay</c> 本就是从透视/正交相机出发的三维射线，
        /// 拾取与竞技场维度无关，无需按平面改写（对照 LevelGeometry 里的选中半径说明）。</summary>
        void UpdateClickTrigger()
        {
            if (!ProjectileTriggerRules.HasClickTrigger(_stats.Trigger))
                return;
            if (!Input.GetMouseButtonDown(0))
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider == hitCollider)
            {
                _clicked = true;
            }
        }

        /// <summary>boulder 碾压伤害（§5.2：伤害 = |vx| * 1.5）。
        /// 3D 口径：vx 是 Flash 的**水平**速度（2D 侧视里唯一的水平轴）；竞技场是 XZ 平面，
        /// 故取世界 (X,Z) 平面速度模长——只看 world.x 会让沿 Z 冲来的 boulder 碾压伤害恒为 0。
        /// 重力方向（Y）不参与，与 Flash 的 |vx| 口径一致（垂直砸下不计碾压伤害）。
        /// TODO：把敌人推到 x±32 并继承 vx（文档同条），本次只做伤害。</summary>
        void Crush(Collider other)
        {
            PirateBase target = other != null ? other.GetComponentInParent<PirateBase>() : null;
            if (target == null || target == _owner || !target.Alive || body == null)
                return;

            float flashVx = LevelGeometry.ArenaVelocityToFlash(body.velocity).magnitude;
            float damage = flashVx * 1.5f;
            if (damage > 0f)
                target.SubtractHealth(damage);
        }

        // ------------------------------------------------------------------
        // 引爆
        // ------------------------------------------------------------------

        /// <summary>被爆炸命中触发连锁引爆（§5.2 火药桶；由 <see cref="BattleController.ResolveExplosion"/> 调用）。</summary>
        public void DetonateFromBlast()
        {
            if (_detonated || !TriggersOnBlast)
                return;
            _blastHit = true;
            Detonate();
        }

        /// <summary>引爆：走 <see cref="BattleController.ResolveExplosion"/>（伤害/击退/evilness/死亡全链），然后移除自身。</summary>
        void Detonate()
        {
            if (_detonated)
                return;
            _detonated = true;

            if (_stats.HasExplosion && _battle != null)
            {
                // §5.2：火药桶由 Explosion 触发时 caster=null（不累加施暴者 evilness）。
                PirateBase caster = _stats.Id == WeaponId.GunpowderBarrel ? null : _owner;
                if (caster != null && !caster.Alive)
                    caster = null;

                _battle.ResolveExplosion(
                    transform.position, _stats.ExplosionSize, _stats.ExplosionMaxDamage, caster, this);
            }

            OnPostDetonate();

            EventBus.Publish(BattleEvents.ProjectileDetonated, new ProjectileDetonatedPayload(
                _stats.Id, transform.position));

            Destroy(gameObject);
        }

        /// <summary>
        /// 引爆后的扩展点。
        /// TODO（rumBottle → 2 个 SweepingFlame）：此处生成火焰专用弹体；
        /// 需要先实现 SweepingFlame 脚本（沿地面每 8px 蔓延、每段 30 伤害、随机击退，§5.2），
        /// 成本超出本任务边界，故留空。
        /// </summary>
        void OnPostDetonate()
        {
        }
    }
}
