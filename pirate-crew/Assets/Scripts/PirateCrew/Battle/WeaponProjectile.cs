using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 武器弹体运行时（MonoBehaviour 薄壳）。
    ///
    /// 【对应章节】静态逆向文档 §5.2（17 武器总表的触发/物理/爆炸/专用行为列）、§5.3（爆炸结算，委派
    ///             <see cref="BattleController.ResolveExplosion"/>）、§5.4（落水/出界处置）。
    ///
    /// 【分层】所有可测规则在纯 C# 里：
    ///   <see cref="ProjectileProfile"/>（参数推导 / 机制分类）、<see cref="ProjectileTriggerRules"/>（引爆判定，
    ///   复用 <see cref="WeaponTriggerRules.ShouldDetonate"/>）、<see cref="ProjectileLifetimeRules"/>、
    ///   <see cref="ProjectileSpawnPlanner"/>；6 把特殊武器的机制分别在
    ///   <see cref="AnchorRules"/> / <see cref="SeagullRules"/> / <see cref="TidalWaveRules"/> /
    ///   <see cref="VoodooDollRules"/> / <see cref="CannonRules"/> / <see cref="SweepingFlameRules"/>。
    ///   本类只做：PhysX 配置、碰撞回调、逐帧组装上下文、按 <see cref="ProjectileMechanic"/> 驱动专用行为、
    ///   引爆时调用 <see cref="BattleController.ResolveExplosion"/>。
    ///
    /// 【程序化兜底】弹体可由 <c>BattleController.projectilePrefab</c> 提供；为空时由
    ///   <see cref="BattleController"/> 用图元 + 颜色程序化构建，因此<b>既有场景无需重新装配</b>。
    ///
    /// 【3D 运动语义（见 docs/M2-3D空间模型对齐.md）】
    ///   · 竞技场是 <b>XZ 水平面</b>、重力沿 <b>-Y</b>：弹体在 X/Z 上惯性飞行、在 Y 上受重力。
    ///   · 刚体<b>只锁旋转、不锁位置</b>。
    ///
    /// 【Flash 帧口径（审计 代码审计报告 §一.2）】「每帧 N」语义——tidalWave 每帧伤害、
    ///   地雷引信 tick、anchor hold/fade、voodoo 10/20 帧、海鸥投弹/炮 AI 间隔、火焰存活——
    ///   全部在 <c>FixedUpdate</c>（fixedDeltaTime=0.04 = Flash 25fps 帧）里推进，与刷新率解耦；
    ///   Update 只留输入轮询（GetMouseButtonDown 仅按下帧为真）与出界/落水清退。
    ///
    /// 【6 把特殊武器的接线范围（重要，评审必读）】
    ///   · anchor：从落点正上方等速下砸（<see cref="AnchorRules"/>）、命中 60 固定伤害、落地 hold+fade。
    ///   · tidalWave：从左侧横扫、每帧对范围内角色 5 点伤害（<see cref="TidalWaveRules"/>）。
    ///   · sweepingFlame：rumBottle 引爆时生成 2 道、向左右蔓延、命中 30 点 + 随机击退
    ///     （<see cref="SweepingFlameRules"/>）。
    ///   · voodooDoll：弹弓抛出，落地 10 帧切镜头、20 帧把投掷速度赋给锁定的最近敌人
    ///     （<see cref="VoodooDollRules"/>）。
    ///   · seagull：从左侧飞入、按固定间隔自动投弹、飞出右侧结束（<see cref="SeagullRules"/>）。
    ///   · cannon：摆位常驻；AI 队 25 帧后自动发射，玩家队经 <see cref="FireCannonToward"/> 发射
    ///     （<see cref="CannonRules"/>）。
    ///   ⚠ 玩家交互（点击放置锚 / 点选海鸥高度 / 拖尾部 pin 蓄力）需要改 <c>AimThrowController</c>，
    ///     而它是本任务黑名单（只读）——本期以「瞄准点 + 自动/公开 API 驱动」近似，
    ///     交互层接线由协调者裁决后另派。
    ///
    /// 【已知 TODO】
    ///   · dynamite 落水变 unlit 状态、boulder 碾压的「推到 x±32 并继承 vx」、
    ///     Flash 的「每帧 |vx|-=friction」精确摩擦语义均留 TODO。
    ///   · parachuteBomb 的「空中减速」与「按住鼠标当扇子」、banana 的 AI 近距引爆条件未实现。
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

        /// <summary>特殊武器允许的更大出界边距（世界单位）：它们要从地图外飞入/从高处落下。</summary>
        const float SpecialBoundsMargin = 64f;

        /// <summary>海鸥自动投弹间隔（帧）。§5.2 只说"可连投多颗"，间隔文档未给 —— <b>提案/待定</b>。</summary>
        const int SeagullBombIntervalFrames = 25;

        [SerializeField] Rigidbody body;
        [SerializeField] Collider hitCollider;

        WeaponStats _stats;
        ProjectileProfile _profile;
        ProjectileMechanic _mechanic;
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

        // ---------------- 特殊机制状态 ----------------
        /// <summary>抛出瞬间的世界速度（voodooDoll 转移给目标用；亦用于调试）。</summary>
        Vector3 _launchVelocity;
        /// <summary>voodooDoll 锁定的目标（可能为 null）。</summary>
        PirateBase _voodooTarget;
        /// <summary>落地后的经过帧数；-1 = 未落地。</summary>
        int _landedFrames = -1;
        /// <summary>锚已命中的角色（同一落点不重复结算 60 伤害）。</summary>
        readonly HashSet<int> _anchorHitIds = new HashSet<int>();
        /// <summary>火焰已命中的角色（每道火对同一角色只结算一次 30 伤害）。</summary>
        readonly HashSet<int> _flameHitIds = new HashSet<int>();
        Renderer _renderer;
        /// <summary>special 计时（海鸥投弹 / 炮 AI 等）。</summary>
        int _specialFrames;
        /// <summary>加农炮 AI 发射倒计时；-1 = 不自动发射。</summary>
        int _cannonAiFrames = -1;
        /// <summary>火焰存活帧数（按蔓延段数换算）。</summary>
        int _flameFramesRemaining;
        readonly List<PirateBase> _pirateBuffer = new List<PirateBase>(16);

        /// <summary>武器 id。</summary>
        public WeaponId WeaponId => _stats.Id;

        /// <summary>运行机制分类（§5.2）。</summary>
        public ProjectileMechanic Mechanic => _mechanic;

        /// <summary>是否仍在本回合物理运动（用于 <see cref="BattleController.IsAnythingActive"/>）。</summary>
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

        /// <summary>
        /// 火焰击退的世界增量（纯函数，供无头测试钉口径；审计 代码审计报告 §一.5 的修复）：
        /// 原版 <c>Knockback</c> 的 vx 是「随机 ±水平量」、vy 负值上抛——2D 侧视里水平轴就是世界 X。
        /// 3D 竞技场目标可能在火焰的任意方位，这里把同一份随机水平量投到
        /// <paramref name="awayBearingXZ"/>（目标 − 火焰 的水平向量）的方位轴上，竖直上抛不变；
        /// 方位退化为零向量（目标与火焰几乎重合）时回退世界 +X。
        /// </summary>
        public static Vector3 FlameKnockbackWorldDelta(Vector2 awayBearingXZ, float random01)
        {
            SweepingFlameRules.Knockback(random01, out float vxFlash, out float vyFlash);
            Vector2 dir = awayBearingXZ.sqrMagnitude > 1e-4f
                ? awayBearingXZ.normalized
                : Vector2.right;
            float horizontal = vxFlash * LevelGeometry.FlashSpeedScale;
            return new Vector3(
                dir.x * horizontal,
                -vyFlash * LevelGeometry.FlashSpeedScale,
                dir.y * horizontal);
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
            _mechanic = ProjectileProfile.MechanicFor(stats.Id);
            _battle = battle;
            _owner = owner;
            _launchVelocity = worldVelocity;
            _detonated = false;
            _initialized = true;
            _armedFrame = Time.frameCount + SpawnGraceFrames;
            _landedFrames = -1;
            _specialFrames = 0;
            _flameHitIds.Clear();
            _anchorHitIds.Clear();

            if (body == null)
                body = GetComponent<Rigidbody>();
            if (hitCollider == null)
                hitCollider = GetComponent<Collider>();

            if (body != null)
            {
                body.mass = _profile.Mass;
                // 2022.3 的 Rigidbody 没有 gravityScale（Unity 6 才加入），
                // 故关闭 useGravity，改由 FixedUpdate 按 Weight 手动施加加速度。
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

            SetupSpecial(stats, owner);

            gameObject.name = "WeaponProjectile_" + stats.DisplayName + (placed ? "_placed" : "");
        }

        /// <summary>特殊机制的一次性初始化（§5.2 各专用行）。</summary>
        void SetupSpecial(WeaponStats stats, PirateBase owner)
        {
            // anchor 手动等速下砸（§5.1「恒 vy=40」），不走 PhysX 自由落体，也不需要碰撞体阻挡。
            if (_mechanic == ProjectileMechanic.AnchorDrop)
            {
                if (body != null)
                {
                    body.isKinematic = true;
                    body.velocity = Vector3.zero;
                }

                if (hitCollider != null)
                    hitCollider.isTrigger = true;
                _renderer = GetComponentInChildren<Renderer>();
            }

            // 海鸥 / 潮汐 / 火焰：用 OverlapSphere 自行结算伤害，碰撞体设 trigger 防止挡住角色。
            if (_mechanic == ProjectileMechanic.SeagullFlight
                || _mechanic == ProjectileMechanic.TidalWaveSweep
                || _mechanic == ProjectileMechanic.SweepingFlameSpread)
            {
                if (hitCollider != null)
                    hitCollider.isTrigger = true;
            }

            if (_mechanic == ProjectileMechanic.VoodooDollTransfer)
                LockVoodooTarget(owner);

            if (_mechanic == ProjectileMechanic.CannonPlacement && owner != null
                && _battle != null && _battle.IsTeamAi(owner.TeamIndex))
            {
                _cannonAiFrames = 0;   // 0 = 已摆位，开始计 AI 发射延时
            }

            if (_mechanic == ProjectileMechanic.SweepingFlameSpread)
                _flameFramesRemaining = FlameLifetimeFrames();
        }

        void FixedUpdate()
        {
            if (!_initialized || _detonated || body == null)
                return;

            // 锚：等速下砸（§5.1「恒 vy=40」），手动推进位置、无重力。
            if (_mechanic == ProjectileMechanic.AnchorDrop)
            {
                AdvanceAnchor();
            }
            else if (!body.isKinematic && _profile.UsesGravity)
            {
                // 手动按 Weight 放大全局重力（weight=1.5 的 boulder；weight=1 与全局一致）。
                // §5.2 weight=0（cannonball / 海鸥 / 潮汐 / 火焰）与已摆位件不施加重力。
                body.AddForce(Physics.gravity * _profile.GravityScale, ForceMode.Acceleration);
            }

            if (Time.frameCount < _armedFrame)
                return;

            // 【Flash 帧口径】逐帧计数与逐帧伤害在物理步推进（类头「Flash 帧口径」注记）：
            // 原先在渲染帧 Update 里，60/144Hz 屏上 tidalWave 每秒伤害（60Hz=300HP/s、
            // 144Hz=720HP/s，原版 25fps=125HP/s）、引信时长、anchor/voodoo 时间线、
            // 海鸥/炮 AI 间隔全部随刷新率漂移。
            if (_mechanic != ProjectileMechanic.Generic)
                UpdateSpecial();
            else
                UpdateMineFuse();
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

            if (_mechanic != ProjectileMechanic.Generic)
            {
                // 特殊机制的逐帧行为已移至 FixedUpdate（Flash 帧口径）；这里只做出界/落水清退。
                HandleSpecialBounds();
                _contact = false;
                _clicked = false;
                _blastHit = false;
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

            // 输入轮询必须留在渲染帧（GetMouseButtonDown 只在按下的那一帧为真）。
            UpdateClickTrigger();

            FlashRestComponents(body.velocity, out float vx, out float vy);
            if (body.velocity.sqrMagnitude < RestSnapSpeed * RestSnapSpeed)
            {
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

            // 特殊机制自行用 OverlapSphere 判定命中，不走通用接触引爆。
            if (_mechanic != ProjectileMechanic.Generic)
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
            if (_mechanic != ProjectileMechanic.Generic)
                return;
            if (_stats.Id == WeaponId.Boulder)
                return;
            _contact = true;
        }

        void OnDestroy()
        {
            _battle?.UnregisterProjectile(this);
        }

        // ==================================================================
        // 特殊机制（§5.2）
        // ==================================================================

        /// <summary>
        /// 特殊武器的出界/落水处置。它们允许从地图外飞入或从高处落下，
        /// 故用比通用弹体宽松得多的 <see cref="SpecialBoundsMargin"/>；落水仍按全局水位消失。
        /// </summary>
        void HandleSpecialBounds()
        {
            if (_battle == null)
                return;

            if (LevelGeometry.IsBelowWater(transform.position.y, _battle.WaterWorldY))
            {
                Vanish();
                return;
            }

            BattlePlan plan = _battle.Plan;
            if (plan == null)
                return;

            Vector3 p = transform.position;
            bool outOfMap = p.x < -SpecialBoundsMargin
                            || p.x > plan.WorldWidth + SpecialBoundsMargin
                            || p.z < -SpecialBoundsMargin
                            || p.z > plan.WorldDepth + SpecialBoundsMargin
                            || p.y < LevelGeometry.WaterSurfaceY - SpecialBoundsMargin
                            || p.y > SpecialBoundsMargin;

            if (outOfMap)
                Vanish();
        }

        void UpdateSpecial()
        {
            switch (_mechanic)
            {
                case ProjectileMechanic.AnchorDrop:
                    UpdateAnchor();
                    break;
                case ProjectileMechanic.SeagullFlight:
                    UpdateSeagull();
                    break;
                case ProjectileMechanic.TidalWaveSweep:
                    UpdateTidalWave();
                    break;
                case ProjectileMechanic.VoodooDollTransfer:
                    UpdateVoodooDoll();
                    break;
                case ProjectileMechanic.CannonPlacement:
                    UpdateCannon();
                    break;
                case ProjectileMechanic.SweepingFlameSpread:
                    UpdateSweepingFlame();
                    break;
            }
        }

        // ---------------- anchor（§5.2 anchor 行） ----------------

        /// <summary>等速下砸一帧：位置下移 FallSpeed（px/帧 → 世界单位/秒），触地即落地。</summary>
        void AdvanceAnchor()
        {
            if (_landedFrames >= 0)
                return;

            float speed = AnchorRules.FallSpeed * LevelGeometry.FlashSpeedScale;
            transform.position += Vector3.down * (speed * Time.fixedDeltaTime);

            float groundY = LevelGeometry.GroundTopY + _profile.HalfHeight;
            if (transform.position.y <= groundY)
            {
                Vector3 p = transform.position;
                p.y = groundY;
                transform.position = p;
                _landedFrames = 0;
            }
        }

        /// <summary>下落中逐帧按 §5.2 命中条件结算 60 固定伤害，落地后 hold 30 + fade 10。</summary>
        void UpdateAnchor()
        {
            if (_landedFrames < 0)
            {
                ApplyAnchorDamage();
                return;
            }

            _landedFrames++;

            if (_renderer != null)
            {
                Color c = _renderer.material.color;
                c.a = AnchorRules.AlphaAfterLanding(_landedFrames);
                _renderer.material.color = c;
            }

            if (AnchorRules.ShouldDestroy(_landedFrames))
                Vanish();
        }

        /// <summary>
        /// 命中判定（§5.2「|x-anchorX| &lt; 48 且 anchorY-64 &lt; y &lt; anchorY」）。
        /// 世界坐标 → Flash 坐标：横向量取 XZ 平面距离的 px；竖直量以「锚底端」为原点、
        /// 用 <c>LevelGeometry.WaterWorldY - worldY</c> 折成"向下为正"的 Flash y 口径
        /// （规则类只用到差值，绝对基准可任取）。
        /// 同一角色在同一落点只结算一次 60 伤害。
        /// </summary>
        void ApplyAnchorDamage()
        {
            if (_battle == null || _owner == null)
                return;

            Vector3 anchorPos = transform.position;
            float anchorBottomY = anchorPos.y - _profile.HalfHeight;
            float queryRadius = _profile.HalfHeight
                                + LevelGeometry.PixelsToUnits(AnchorRules.HorizontalHalfExtent);

            _battle.CollectPiratesInRadius(anchorPos, queryRadius, _pirateBuffer);
            for (int i = 0; i < _pirateBuffer.Count; i++)
            {
                PirateBase target = _pirateBuffer[i];
                if (target == null || !target.Alive || _anchorHitIds.Contains(target.PirateId))
                    continue;

                float dxzPx = LevelGeometry.UnitsToPixels(
                    Vector2.Distance(
                        new Vector2(anchorPos.x, anchorPos.z),
                        new Vector2(target.transform.position.x, target.transform.position.z)));
                float anchorFlashY = LevelGeometry.UnitsToPixels(
                    _battle.WaterWorldY - anchorBottomY);
                float targetFlashY = LevelGeometry.UnitsToPixels(
                    _battle.WaterWorldY - target.transform.position.y);

                if (!AnchorRules.ShouldHit(0f, anchorFlashY, dxzPx, targetFlashY))
                    continue;

                _anchorHitIds.Add(target.PirateId);
                target.SubtractHealth(AnchorRules.FixedDamage);
            }
        }

        // ---------------- seagull（§5.2 seagull 行） ----------------

        /// <summary>
        /// 海鸥飞行：向右以 vx=10 飞（初速由生成计划给出），到间隔即投弹，飞出右侧阈值结束。
        /// ⚠ 原文"再次点击投弹"需交互层；本期自动按间隔投弹（<b>提案/待定</b>）。
        /// </summary>
        void UpdateSeagull()
        {
            BattlePlan plan = _battle != null ? _battle.Plan : null;
            if (plan == null)
                return;

            _specialFrames++;
            if (_specialFrames % SeagullBombIntervalFrames == 0)
                DropSeagullBomb();

            float flashX = LevelGeometry.UnitsToPixels(transform.position.x);
            if (SeagullRules.CanFinish(flashX, plan.WorldWidth, anyBombInFlight: false))
                Vanish();
        }

        /// <summary>投下一颗弹（§5.2 每发 50/50）：按 <see cref="BattleController.ResolveExplosion"/> 结算。</summary>
        void DropSeagullBomb()
        {
            if (_battle == null || !_stats.HasExplosion)
                return;

            PirateBase caster = _owner != null && _owner.Alive ? _owner : null;
            _battle.ResolveExplosion(
                transform.position, _stats.ExplosionSize, _stats.ExplosionMaxDamage, caster, this);
        }

        // ---------------- tidalWave（§5.2 tidalWave 行） ----------------

        /// <summary>
        /// 横扫（初速由生成计划给出 vx=20）：每帧对 ±150px 内且 y ≥ waterY-300 的**所有**角色
        /// 造成 5 点伤害（对敌我一视同仁、无 evilness）。扫出右边界即消失。
        /// </summary>
        void UpdateTidalWave()
        {
            if (_battle == null)
                return;

            BattlePlan plan = _battle.Plan;
            Vector3 wave = transform.position;
            float waterY = _battle.WaterWorldY;

            _battle.CollectPiratesInRadius(
                wave, LevelGeometry.PixelsToUnits(TidalWaveRules.HitRadius), _pirateBuffer);

            for (int i = 0; i < _pirateBuffer.Count; i++)
            {
                PirateBase target = _pirateBuffer[i];
                if (target == null || !target.Alive)
                    continue;

                // 3D 映射：dx 取世界 X 差（横扫轴），dy 取世界 Y 差；Z 折叠（浪横跨纵深）。
                float dxPx = LevelGeometry.UnitsToPixels(target.transform.position.x - wave.x);
                float targetFlashY = LevelGeometry.UnitsToPixels(waterY - target.transform.position.y);
                float waveFlashY = LevelGeometry.UnitsToPixels(waterY - wave.y);

                if (!TidalWaveRules.ShouldDamage(0f, waveFlashY, dxPx, targetFlashY, 0f))
                    continue;

                target.SubtractHealth(TidalWaveRules.DamagePerFrame);
            }

            if (plan != null && TidalWaveRules.IsPastRightEdge(
                    LevelGeometry.UnitsToPixels(wave.x), plan.WorldWidth))
            {
                Vanish();
            }
        }

        // ---------------- voodooDoll（§5.2 voodooDoll 行） ----------------

        /// <summary>
        /// 落地（静止）后走 10+10 帧时间线：第 10 帧镜头切到目标、第 20 帧把**抛出瞬间的速度**
        /// 赋给目标（<see cref="PirateBase.ApplyImpulseDelta"/>，可把目标抛入水中）。
        /// </summary>
        void UpdateVoodooDoll()
        {
            bool landed = body != null
                          && body.velocity.sqrMagnitude < RestSnapSpeed * RestSnapSpeed;

            if (_landedFrames < 0)
            {
                if (!landed)
                    return;

                _landedFrames = 0;
                if (body != null)
                {
                    body.isKinematic = true;
                    body.velocity = Vector3.zero;
                }
            }

            _landedFrames++;

            if (_voodooTarget != null && !_voodooTarget.Alive)
                return;

            if (VoodooDollRules.ShouldSwitchCamera(_landedFrames) && _voodooTarget != null)
            {
                EventBus.Publish(BattleEvents.CameraFocusRequested, _voodooTarget.transform);
            }

            if (VoodooDollRules.ShouldTransferVelocity(_landedFrames))
            {
                if (_voodooTarget != null
                    && VoodooDollRules.TryTransferVelocity(
                        _landedFrames, _launchVelocity.x, _launchVelocity.y, out _, out _))
                {
                    // 3D 转移：抛出速度原样赋给目标（XZ 水平 + Y 抬升一起给，保持"同方向抛飞"）。
                    _voodooTarget.ApplyImpulseDelta(_launchVelocity);
                }

                Vanish();
            }
        }

        /// <summary>锁定最近的在 30px 内的敌方存活角色（§4.2 <c>pickNearestEnemy(30px)</c>）。</summary>
        void LockVoodooTarget(PirateBase owner)
        {
            _voodooTarget = null;
            if (_battle == null || owner == null)
                return;

            _battle.CollectPiratesInRadius(
                transform.position,
                LevelGeometry.PixelsToUnits(VoodooDollRules.TargetPickRadius),
                _pirateBuffer);

            float best = float.MaxValue;
            for (int i = 0; i < _pirateBuffer.Count; i++)
            {
                PirateBase candidate = _pirateBuffer[i];
                if (candidate == null || !candidate.Alive
                    || candidate == owner || candidate.TeamIndex == owner.TeamIndex)
                {
                    continue;
                }

                float d = Vector3.Distance(transform.position, candidate.transform.position);
                if (d < best)
                {
                    best = d;
                    _voodooTarget = candidate;
                }
            }
        }

        // ---------------- cannon（§5.2 cannon 行） ----------------

        /// <summary>
        /// 加农炮：AI 队摆位后 25 帧自动朝最近敌人满蓄力发射（§6.3 <c>aiFireTime=25</c>）；
        /// 玩家队由交互层调用 <see cref="FireCannonToward"/>。
        /// </summary>
        void UpdateCannon()
        {
            if (_cannonAiFrames < 0)
                return;

            if (!CannonRules.ShouldAiFire(_cannonAiFrames))
            {
                _cannonAiFrames++;
                return;
            }

            _cannonAiFrames = -1;
            PirateBase target = FindNearestEnemy();
            if (target != null)
                FireCannonToward(target.transform.position);
        }

        /// <summary>
        /// 朝世界坐标 <paramref name="targetWorld"/> 发射一颗 cannonball（§5.2「经 cannonball（100, 50）」）。
        /// 蓄力恒取满值 30（<see cref="CannonRules.MaxFireStrength"/>）。
        /// 方向：3D 下由 XZ 平面方向给出（弹弓换算会再加 <c>ThrowLift</c> 仰角）。
        /// </summary>
        public bool FireCannonToward(Vector3 targetWorld)
        {
            if (_detonated || _battle == null)
                return false;

            float charge = CannonRules.MaxFireStrength;
            if (!CannonRules.ShouldFire(charge))
                return false;

            Vector3 origin = transform.position;
            Vector3 flat = new Vector3(targetWorld.x - origin.x, 0f, targetWorld.z - origin.z);
            if (flat.sqrMagnitude < 1e-6f)
                return false;

            flat.Normalize();
            // FlashLaunchVelocityToWorld 把 Flash 平面 (vx, vy) 落到世界 (X, Z) 再抬仰角，
            // 故这里把 XZ 方向直接映射到 (vx, vy)，大小 = 蓄力。
            float vxFlash = flat.x * charge;
            float vyFlash = flat.z * charge;
            Vector3 muzzle = origin + flat * 0.5f;

            _battle.SpawnWeaponProjectiles(
                WeaponCatalog.Get(WeaponId.Cannonball), _owner, muzzle, muzzle, vxFlash, vyFlash);
            _cannonAiFrames = -1;
            return true;
        }

        PirateBase FindNearestEnemy()
        {
            if (_battle == null || _owner == null)
                return null;

            _battle.CollectPiratesInRadius(
                transform.position, SpecialBoundsMargin, _pirateBuffer);

            PirateBase best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _pirateBuffer.Count; i++)
            {
                PirateBase candidate = _pirateBuffer[i];
                if (candidate == null || !candidate.Alive
                    || candidate.TeamIndex == _owner.TeamIndex)
                {
                    continue;
                }

                float d = Vector3.Distance(transform.position, candidate.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }

            return best;
        }

        // ---------------- sweepingFlame（§5.2 表格末行） ----------------

        /// <summary>
        /// 沿地面蔓延：命中 8px 内角色给 30 点 + 随机击退（每道火对同一角色只结算一次）；
        /// 存活帧数由蔓延段数换算，走完即消失。
        /// </summary>
        void UpdateSweepingFlame()
        {
            if (_flameFramesRemaining > 0)
                _flameFramesRemaining--;
            if (_flameFramesRemaining <= 0)
            {
                Vanish();
                return;
            }

            if (_battle == null)
                return;

            float hitRadius = LevelGeometry.PixelsToUnits(SweepingFlameRules.HitDistance);
            _battle.CollectPiratesInRadius(transform.position, hitRadius, _pirateBuffer);

            for (int i = 0; i < _pirateBuffer.Count; i++)
            {
                PirateBase target = _pirateBuffer[i];
                if (target == null || !target.Alive || _flameHitIds.Contains(target.PirateId))
                    continue;

                float distancePx = LevelGeometry.UnitsToPixels(
                    Vector3.Distance(transform.position, target.transform.position));
                if (!SweepingFlameRules.ShouldHit(distancePx))
                    continue;

                _flameHitIds.Add(target.PirateId);
                target.SubtractHealth(SweepingFlameRules.DamagePerSegment);

                // 击退方向按「目标相对火焰的方位」给出（审计 代码审计报告 §一.5）：
                // 原版 vx 是随机 ±水平量，2D 侧视里水平就是世界 X；3D 下目标可能在火焰
                // 任意方位，把同一份随机量投到远离火焰的方位轴上，竖直上抛不变。
                Vector2 away = new Vector2(
                    target.transform.position.x - transform.position.x,
                    target.transform.position.z - transform.position.z);
                target.ApplyImpulseDelta(FlameKnockbackWorldDelta(away, Random.value));
            }
        }

        /// <summary>火焰存活帧数 = 铺到地图右边界所需的段数。</summary>
        int FlameLifetimeFrames()
        {
            float widthPx = _battle != null && _battle.Plan != null
                ? LevelGeometry.UnitsToPixels(_battle.Plan.WorldWidth)
                : 0f;
            return Mathf.Max(1, SweepingFlameRules.SegmentCount(widthPx));
        }

        // ==================================================================
        // 通用弹体辅助
        // ==================================================================

        void HandleWaterAndBounds()
        {
            if (_battle == null)
                return;

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
            bool outOfMap = p.x < -OutOfMapMargin
                            || p.x > plan.WorldWidth + OutOfMapMargin
                            || p.z < -OutOfMapMargin
                            || p.z > plan.WorldDepth + OutOfMapMargin
                            || p.y < LevelGeometry.WaterSurfaceY - OutOfMapMargin;

            if (!outOfMap && !_profile.UsesGravity && p.y > OutOfMapMargin)
                outOfMap = true;

            if (outOfMap)
                Vanish();
        }

        /// <summary>离开场上（落水/出界且按表应消失）：标记已消费并销毁，避免同帧再走引爆逻辑。</summary>
        void Vanish()
        {
            _detonated = true;
            Destroy(gameObject);
        }

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

        float NearestPirateDistance()
        {
            _nearestMoving = false;
            if (_battle == null)
                return -1f;

            _battle.CollectPiratesInRadius(
                transform.position,
                LevelGeometry.PixelsToUnits(WeaponTriggerRules.ProximityRadius),
                _pirateBuffer);

            float best = -1f;
            for (int i = 0; i < _pirateBuffer.Count; i++)
            {
                PirateBase pirate = _pirateBuffer[i];
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
        /// TODO：把敌人推到 x±32 并继承 vx。</summary>
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

        /// <summary>引爆：走 <see cref="BattleController.ResolveExplosion"/>，然后移除自身。</summary>
        void Detonate()
        {
            if (_detonated)
                return;
            _detonated = true;

            if (_stats.HasExplosion && _battle != null)
            {
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
        /// 引爆后的扩展点：rumBottle 落在 FLOOR 时额外生成 2 个 SweepingFlame（§5.2 rumBottle 行）。
        /// M2 近似：doc 只要求"落在 FLOOR"，而通用弹体的引爆条件无法稳定区分地面/角色；
        /// 这里在 rumBottle 引爆时一律生成 2 道火（**提案/待定**）。
        /// </summary>
        void OnPostDetonate()
        {
            if (_stats.Id == WeaponId.RumBottle && _battle != null)
                _battle.SpawnSweepingFlames(transform.position);
        }
    }
}
