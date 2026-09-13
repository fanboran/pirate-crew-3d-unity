using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient
{
    /// <summary>
    /// 单只海鸥的运行时行为（MonoBehaviour 薄壳，**由 <see cref="AmbientDirector"/> 统一 <c>Tick</c>**，
    /// 不用自己的 <c>Update</c>——这样活物数量再多也只有一个 Update 入口，便于分帧与统一计时）。
    ///
    /// 【动画全部靠 Transform（无骨骼、无顶点动画）】
    ///   · 躯干：位置 = 李萨如轨迹（+ 俯冲贝塞尔 + 惊飞偏移）；
    ///   · 翅膀：左右翼两个子 Transform 绕局部 Z 轴拍动（<see cref="GullFlightRules.WingAngleDegrees"/>）；
    ///   · 朝向：由解析速度求 yaw（<see cref="GullFlightRules.HeadingDegrees"/>）。
    /// 一次 Tick 只算 3 条正弦 + 1 次 LookRotation，10 只海鸥的开销都远小于 1 个粒子系统。
    ///
    /// 【可读性红线】位置在写回 Transform 前必经
    /// <see cref="AmbientNoFlyZone.ClampOut"/>，保证永不进入投掷视线的中央区域
    /// （规则见 <see cref="AmbientRules"/>，测试见 <c>Assets/Tests/Ambient/</c>）。
    /// 本类**没有任何 Collider / Rigidbody**，不参与物理（场景文档 §9.4）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeagullAgent : MonoBehaviour
    {
        GullOrbit _orbit;
        AmbientNoFlyZone _noFly;
        Vector3 _diveTarget;
        float _diveIntervalMin;
        float _diveIntervalMax;

        Transform _leftWing;
        Transform _rightWing;

        GullState _state = GullState.Cruise;
        float _time;
        float _phaseOffset;
        float _diveT;
        float _diveCountdown;
        Vector3 _diveFrom;

        float _panic;
        Vector3 _blastPosition;
        bool _splashPending;
        bool _canDive = true;

        /// <summary>当前状态（报告/调试）。</summary>
        public GullState State => _state;

        /// <summary>当前是否处于惊飞（爆炸后）。</summary>
        public bool IsPanicking => _panic > 0f;

        /// <summary>绑定轨道与子部件。由 <see cref="AmbientDirector"/> 在生成后立即调用。</summary>
        public void Configure(GullOrbit orbit, Vector3 diveTarget, AmbientNoFlyZone noFly,
            float phaseOffset, float diveIntervalMin, float diveIntervalMax,
            Transform leftWing, Transform rightWing, bool canDive = true)
        {
            _orbit = orbit;
            _diveTarget = diveTarget;
            _noFly = noFly;
            _phaseOffset = phaseOffset;
            _diveIntervalMin = diveIntervalMin;
            _diveIntervalMax = diveIntervalMax;
            _leftWing = leftWing;
            _rightWing = rightWing;
            _canDive = canDive;

            // 首冲时间错开，避免所有海鸥同时俯冲。
            _diveCountdown = Mathf.Lerp(diveIntervalMin, diveIntervalMax, Mathf.Repeat(phaseOffset * 0.37f, 1f));
            _time = phaseOffset * 3.1f;
            ApplyPose(_orbit.Evaluate(_time), _orbit.Velocity(_time));
        }

        /// <summary>爆炸惊飞：记录爆心与强度（强度 1 = 全速逃离，随 <see cref="GullFlightRules.PanicDuration"/> 衰减）。</summary>
        public void Panic(Vector3 blastPosition, float strength)
        {
            _blastPosition = blastPosition;
            _panic = Mathf.Max(_panic, Mathf.Clamp01(strength));
        }

        /// <summary>推进一帧。</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f)
                return;

            _time += dt;
            _panic = GullFlightRules.DecayPanic(_panic, dt);

            Vector3 position;
            Vector3 velocity;

            if (_state == GullState.Cruise)
            {
                _diveCountdown -= dt;
                if (_canDive && _diveCountdown <= 0f && _panic <= 0f)
                {
                    _state = GullState.Dive;
                    _diveT = 0f;
                    _diveFrom = _orbit.Evaluate(_time);
                    _splashPending = true;
                }

                position = _orbit.Evaluate(_time);
                velocity = _orbit.Velocity(_time);
            }
            else
            {
                // 俯冲总进度 0..2（0..1 下冲、1..2 回升）。
                _diveT += dt * 2f / GullFlightRules.DiveDuration;
                position = GullFlightRules.DivePoint(_diveFrom, _diveTarget, _diveT);
                velocity = (GullFlightRules.DivePoint(_diveFrom, _diveTarget, _diveT + 0.02f) - position) / 0.02f;

                // 触水瞬间复用 Fx 模块的水花 + 涟漪（不重复造涟漪系统）。
                if (_splashPending && _diveT >= 1f)
                {
                    _splashPending = false;
                    Fx.FxApi.PlayWaterSplash(
                        GullFlightRules.SplashPoint(_diveTarget, _diveTarget.y), 6f);
                }

                if (_diveT >= 2f)
                {
                    _state = GullState.Cruise;
                    _diveCountdown = Random.Range(_diveIntervalMin, _diveIntervalMax);
                }
            }

            // ---- 惊飞偏移（随 panic 衰减平滑归位）----
            Vector3 panicOffset = GullFlightRules.PanicOffset(position, _blastPosition, _panic);
            if (panicOffset.sqrMagnitude > 0f)
            {
                position += panicOffset;
                velocity += panicOffset;   // 朝向也跟随惊飞方向（量纲不一致但只用于 yaw，无副作用）
            }

            position = GullFlightRules.ClampAltitude(position);

            // ---- 可读性红线：绝不进入投掷视线中央区域 ----
            if (_noFly.Contains(position))
                position = _noFly.ClampOut(position);

            ApplyPose(position, velocity);
        }

        void ApplyPose(Vector3 position, Vector3 velocity)
        {
            transform.position = position;

            float heading = GullFlightRules.HeadingDegrees(velocity, transform.eulerAngles.y);
            transform.rotation = Quaternion.Euler(0f, heading, 0f);

            float flapRate = _state == GullState.Dive
                ? GullFlightRules.DiveFlapRate
                : (_panic > 0.05f ? GullFlightRules.PanicFlapRate : GullFlightRules.CruiseFlapRate);

            float flapPhase = GullFlightRules.WingPhase(_time, flapRate, _phaseOffset * 6.28f);
            float angle = GullFlightRules.WingAngleDegrees(flapPhase, 34f);

            if (_leftWing != null)
                _leftWing.localRotation = Quaternion.Euler(0f, 0f, angle);

            if (_rightWing != null)
                _rightWing.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }
    }
}
