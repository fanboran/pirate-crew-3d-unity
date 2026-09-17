using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.Visual
{
    /// <summary>
    /// 角色视觉动画驱动（代码驱动，**不依赖 Animator 资产/AnimatorController**）。
    ///
    /// 【职责】读 <see cref="PirateBase"/> 的状态位（存活 / 移动 / 落水标记）与外部显式通知
    /// （投掷、受击），按 <see cref="CrewAnimationRules"/> 的时长/曲线写 <see cref="CrewVisualRig"/>
    /// 的部件 Transform。所有位移/旋转都作用在 <c>Visual</c> 子层级上，**不改 Rigidbody/根 Transform**，
    /// 因此不会影响战斗/AI/弹道判定（docs/角色造型规范.md §4 纪律）。
    ///
    /// 【为什么不新增 EventBus 事件】投掷/受击由 <see cref="PirateBase"/> 直接方法调用通知本组件
    /// （同一 GameObject 内的强关系，不该伪装成松耦合；见 AGENTS.md 事件契约节）。
    ///
    /// 【淡出说明】描边 shader 的本体 Pass 是不透明（ZWrite On、无 Blend），没有 alpha 通道
    /// （shader 冻结不改），因此"淡出"用**整体收拢缩放**近似，不是真正的 alpha 渐隐。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrewVisualAnimator : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("留空则在 Awake 时从子节点抓 CrewVisualRig。")]
        [SerializeField] CrewVisualRig rig;
        [Tooltip("留空则取同物体的 PirateBase。")]
        [SerializeField] PirateBase pirate;

        [Header("整身表现")]
        [Tooltip("死亡倒地方向：+1 后倒 / −1 前扑。")]
        [SerializeField] float deathFallSign = 1f;

        // 状态
        CrewVisualState _baseState = CrewVisualState.Idle;
        CrewVisualState _throwPhase = CrewVisualState.Idle;
        bool _throwActive;
        float _throwElapsed;
        bool _hitActive;
        float _hitElapsed;
        bool _dead;
        bool _drown;
        float _deadElapsed;
        float _time;

        // 部件基准（Awake 记录，动画在此之上叠加）
        Vector3 _headBasePosition;
        Vector3 _headBaseScale;
        Vector3 _torsoBaseScale;
        Vector3 _bodyBasePosition;
        Vector3 _bodyBaseScale;

        /// <summary>受击白闪写入器（Awake 缓存 + 首次使用惰性兜底，避免 Update 链路每帧
        /// <c>GetComponent</c> 扫描；binder 与本组件同物体，见 <see cref="UnitOutlineBinder"/>）。</summary>
        UnitOutlineBinder _outlineBinder;

        /// <summary>当前表现状态（调试/测试可见）。</summary>
        public CrewVisualState CurrentState => ResolveState();

        /// <summary>绑定装配根（编辑器/测试注入用）。</summary>
        public void Bind(CrewVisualRig visualRig, PirateBase owner)
        {
            rig = visualRig;
            pirate = owner;
            CaptureBasePose();
        }

        void Awake()
        {
            if (rig == null)
                rig = GetComponentInChildren<CrewVisualRig>(true);
            if (pirate == null)
                pirate = GetComponent<PirateBase>();
            // 白闪 binder 由 prefab 静态挂载（RequireComponent(PirateBase)），Awake 即可拿到；
            // 抓不到也不报警——ApplyHitFlash 里会惰性兜底（覆盖手工组装/时序边角）。
            if (_outlineBinder == null)
                _outlineBinder = GetComponent<UnitOutlineBinder>();

            CaptureBasePose();
        }

        void CaptureBasePose()
        {
            if (rig == null)
                return;

            if (rig.HeadPivot != null)
            {
                _headBasePosition = rig.HeadPivot.localPosition;
                _headBaseScale = rig.HeadPivot.localScale;
            }
            if (rig.TorsoPivot != null)
                _torsoBaseScale = rig.TorsoPivot.localScale;
            if (rig.Body != null)
            {
                _bodyBasePosition = rig.Body.localPosition;
                _bodyBaseScale = rig.Body.localScale;
            }
        }

        // ------------------------------------------------------------------
        // 外部通知（由 PirateBase 直接调用）
        // ------------------------------------------------------------------

        /// <summary>开始一次投掷表现（蓄力 → 释放 → 恢复）。</summary>
        public void NotifyThrow()
        {
            if (_dead)
                return;
            _throwActive = true;
            _throwElapsed = 0f;
            _throwPhase = CrewVisualState.ThrowCharge;
        }

        /// <summary>受击表现（后仰 + 白闪）。</summary>
        public void NotifyHit()
        {
            if (_dead)
                return;
            _hitActive = true;
            _hitElapsed = 0f;
        }

        /// <summary>立即进入死亡/落水表现（PirateBase.Kill 后由 Died 事件或轮询触发）。</summary>
        public void NotifyDeath(bool drowned)
        {
            _dead = true;
            _drown = drowned;
            _deadElapsed = 0f;
            _throwActive = false;
            _hitActive = false;
        }

        // ------------------------------------------------------------------
        // 每帧驱动
        // ------------------------------------------------------------------

        void Update()
        {
            if (rig == null)
                return;

            float dt = Time.deltaTime;
            _time += dt;

            // 死亡/落水检测（轮询 PirateBase，无需事件订阅）。
            if (!_dead && pirate != null && !pirate.Alive)
                NotifyDeath(pirate.Drowned);
            if (_dead)
                _deadElapsed += dt;

            AdvanceOverlays(dt);

            CrewVisualState state = ResolveState();
            _baseState = state;

            ApplyPose(state);
        }

        void AdvanceOverlays(float dt)
        {
            if (_hitActive)
            {
                _hitElapsed += dt;
                if (_hitElapsed >= CrewAnimationRules.HitTotalSeconds)
                    _hitActive = false;
            }

            if (_throwActive)
            {
                _throwElapsed += dt;
                float charge = CrewAnimationRules.ThrowChargeSeconds;
                float release = charge + CrewAnimationRules.ThrowReleaseSeconds;
                float total = CrewAnimationRules.ThrowTotalSeconds;

                if (_throwElapsed < charge)
                    _throwPhase = CrewVisualState.ThrowCharge;
                else if (_throwElapsed < release)
                    _throwPhase = CrewVisualState.ThrowRelease;
                else if (_throwElapsed < total)
                    _throwPhase = CrewVisualState.ThrowRecover;
                else
                    _throwActive = false;
            }
        }

        CrewVisualState ResolveState()
        {
            if (_dead)
                return _drown ? CrewVisualState.Drown : CrewVisualState.Death;
            if (_hitActive)
                return CrewVisualState.Hit;
            if (_throwActive)
                return _throwPhase;
            if (pirate != null && pirate.IsMoving())
                return CrewVisualState.Move;
            return CrewVisualState.Idle;
        }

        void ApplyPose(CrewVisualState state)
        {
            int professionIndex = (int)rig.Profession;

            // 复位到基准姿势，再按状态叠加（状态间不会残留）。
            ResetPose();

            switch (state)
            {
                case CrewVisualState.Idle:
                    ApplyIdle(professionIndex);
                    break;
                case CrewVisualState.Move:
                    ApplyMove();
                    break;
                case CrewVisualState.ThrowCharge:
                case CrewVisualState.ThrowRelease:
                case CrewVisualState.ThrowRecover:
                    ApplyThrow(state);
                    break;
                case CrewVisualState.Hit:
                    ApplyHit();
                    break;
                case CrewVisualState.Death:
                    ApplyDeath();
                    break;
                case CrewVisualState.Drown:
                    ApplyDrown();
                    break;
            }

            // 受击白闪（可叠加在待机/移动上；走 binder 的 MPB，不抢材质资产）。
            ApplyHitFlash();
        }

        void ResetPose()
        {
            if (rig.HeadPivot != null)
            {
                rig.HeadPivot.localPosition = _headBasePosition;
                rig.HeadPivot.localScale = _headBaseScale;
                rig.HeadPivot.localRotation = Quaternion.identity;
            }
            if (rig.TorsoPivot != null)
            {
                rig.TorsoPivot.localScale = _torsoBaseScale;
                rig.TorsoPivot.localRotation = Quaternion.identity;
            }
            if (rig.Body != null)
            {
                rig.Body.localPosition = _bodyBasePosition;
                rig.Body.localScale = _bodyBaseScale;
                rig.Body.localRotation = Quaternion.identity;
            }
            SetArmRotation(rig.ArmLPivot, 0f);
            SetArmRotation(rig.ArmRPivot, 0f);
            SetArmRotation(rig.LegLPivot, 0f);
            SetArmRotation(rig.LegRPivot, 0f);

            if (rig.HeldR != null && !rig.HeldR.gameObject.activeSelf)
                rig.HeldR.gameObject.SetActive(true);
        }

        void ApplyIdle(int professionIndex)
        {
            if (rig.TorsoPivot != null)
            {
                Vector3 scale = _torsoBaseScale;
                scale.y *= CrewAnimationRules.BreathTorsoScaleY(_time, professionIndex);
                rig.TorsoPivot.localScale = scale;
            }
            if (rig.HeadPivot != null)
            {
                Vector3 p = _headBasePosition;
                p.y += CrewAnimationRules.BreathHeadOffsetY(_time, professionIndex);
                rig.HeadPivot.localPosition = p;
            }

            float swing = CrewAnimationRules.BreathArmSwing(_time, professionIndex);
            SetArmRotation(rig.ArmLPivot, swing);
            SetArmRotation(rig.ArmRPivot, -swing);
        }

        void ApplyMove()
        {
            if (rig.Body != null)
            {
                Vector3 p = _bodyBasePosition;
                p.y += CrewAnimationRules.MoveBob(_time);
                rig.Body.localPosition = p;
            }
            if (rig.TorsoPivot != null)
            {
                // 前倾（约定：正角 = 前倾，视觉上用 −X 旋转实现，见 CrewAnimationRules）。
                rig.TorsoPivot.localRotation = Quaternion.Euler(-CrewAnimationRules.MoveLeanDegrees, 0f, 0f);
            }

            SetArmRotation(rig.ArmLPivot, CrewAnimationRules.WalkArmSwing(_time, 1f));
            SetArmRotation(rig.ArmRPivot, CrewAnimationRules.WalkArmSwing(_time, -1f));
            SetArmRotation(rig.LegLPivot, CrewAnimationRules.WalkLegSwing(_time, 1f));
            SetArmRotation(rig.LegRPivot, CrewAnimationRules.WalkLegSwing(_time, -1f));
        }

        void ApplyThrow(CrewVisualState phase)
        {
            if (rig.TorsoPivot != null)
            {
                float lean = CrewAnimationRules.ThrowLeanDegrees(phase, PhaseElapsed(phase));
                rig.TorsoPivot.localRotation = Quaternion.Euler(-lean, 0f, 0f);
            }

            float arm = CrewAnimationRules.ThrowArmDegrees(phase, PhaseElapsed(phase));
            SetArmRotation(rig.ArmRPivot, arm);

            // 释放帧后手持物脱手（隐藏；实际弹体由战斗层出膛，见规格 §4/R-7）。
            if (rig.HeldR != null)
            {
                bool released = CrewAnimationRules.IsHeldItemReleased(phase, PhaseElapsed(phase));
                if (rig.HeldR.gameObject.activeSelf == released)
                    rig.HeldR.gameObject.SetActive(!released);
            }
        }

        float PhaseElapsed(CrewVisualState phase)
        {
            float charge = CrewAnimationRules.ThrowChargeSeconds;
            switch (phase)
            {
                case CrewVisualState.ThrowCharge: return _throwElapsed;
                case CrewVisualState.ThrowRelease: return _throwElapsed - charge;
                case CrewVisualState.ThrowRecover: return _throwElapsed - charge - CrewAnimationRules.ThrowReleaseSeconds;
                default: return 0f;
            }
        }

        void ApplyHit()
        {
            if (rig.TorsoPivot != null)
            {
                // 后仰 = 负的"前倾"，故取 +X 旋转。
                float recoil = CrewAnimationRules.HitRecoilDegreesAt(_hitElapsed);
                rig.TorsoPivot.localRotation = Quaternion.Euler(recoil, 0f, 0f);
            }
        }

        void ApplyDeath()
        {
            if (rig.Body != null)
            {
                float fall = CrewAnimationRules.DeathFallAngle(_deadElapsed);
                rig.Body.localRotation = Quaternion.Euler(deathFallSign * fall, 0f, 0f);

                if (_deadElapsed > CrewAnimationRules.DeathFallSeconds)
                {
                    float fadeElapsed = _deadElapsed - CrewAnimationRules.DeathFallSeconds;
                    float scale = CrewAnimationRules.FadeScale(fadeElapsed, CrewAnimationRules.DeathFadeSeconds);
                    rig.Body.localScale = _bodyBaseScale * scale;
                }
            }
        }

        void ApplyDrown()
        {
            if (rig.Body != null)
            {
                Vector3 p = _bodyBasePosition;
                // 下沉位移：Body 在 Visual 下，Y 方向 net scale = 1（见 rig 类头矩阵口径），故 1:1。
                p.y += CrewAnimationRules.DrownSinkOffset(_deadElapsed);
                rig.Body.localPosition = p;
                rig.Body.localRotation = Quaternion.Euler(0f, 0f,
                    CrewAnimationRules.DrownSwayDegreesAt(_deadElapsed));

                float total = Mathf.Lerp(
                    CrewAnimationRules.DrownSecondsMin,
                    CrewAnimationRules.DrownSecondsMax,
                    0.5f);
                float fadeElapsed = Mathf.Max(0f, _deadElapsed - total * 0.5f);
                float scale = CrewAnimationRules.FadeScale(fadeElapsed, total * 0.5f);
                rig.Body.localScale = _bodyBaseScale * scale;
            }
        }

        void ApplyHitFlash()
        {
            // 已在 Awake 缓存；此兜底只在缓存未命中时扫一次，命中后不再逐帧 GetComponent。
            if (_outlineBinder == null)
                _outlineBinder = GetComponent<UnitOutlineBinder>();
            if (_outlineBinder == null)
                return;

            // 受击期间按规则给闪白强度，其余时刻复位为 0；
            // binder.SetColorFlash 内部做"值未变则不置脏"，每帧调用无额外开销。
            float strength = _hitActive ? CrewAnimationRules.HitFlashStrength(_hitElapsed) : 0f;
            _outlineBinder.SetColorFlash(strength);
        }

        static void SetArmRotation(Transform pivot, float degrees)
        {
            if (pivot != null)
                pivot.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        }
    }
}
