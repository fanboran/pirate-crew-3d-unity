using UnityEngine;

namespace PirateCrew.PirateCrew.Ambient
{
    /// <summary>
    /// 岸边螃蟹：沿一条岸边线段横爬往返 + 双钳摆动 + 遇单位靠近缩进沙里。
    /// MonoBehaviour 薄壳，由 <see cref="AmbientDirector"/> 统一 <c>Tick</c>。
    ///
    /// 【行为规则全部在 <see cref="CrabBehaviorRules"/>（纯 C#，无头可测）】，本类只负责：
    ///   · 把 ping-pong 参数翻译成世界坐标；
    ///   · 把缩沙进度翻译成下沉位移与可见性；
    ///   · 驱动两个钳子子 Transform 的摆角。
    ///
    /// 【无碰撞】不加任何 Collider（场景文档 §9.4：装饰不得改变弹道）。
    /// 单位靠近判定来自 <see cref="AmbientDirector"/> 传入的"最近单位距离"（它持有单位根引用），
    /// **本类不做 <c>GameObject.Find</c> / <c>FindObjectsOfType</c>**。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrabAgent : MonoBehaviour
    {
        Vector3 _patrolA;
        Vector3 _patrolB;
        float _halfPeriod;
        float _phaseOffset;

        Transform _leftClaw;
        Transform _rightClaw;

        CrabState _state = CrabState.Patrol;
        float _time;
        float _buryProgress;   // 0 = 露出、1 = 完全埋没
        float _buriedDepth;

        int _alarmFrames;

        /// <summary>当前状态。</summary>
        public CrabState State => _state;

        /// <summary>当前缩沙进度（0..1）。</summary>
        public float BuryProgress => _buryProgress;

        /// <summary>绑定巡逻段与钳子引用。由 <see cref="AmbientDirector"/> 调用。</summary>
        public void Configure(Vector3 patrolA, Vector3 patrolB, float halfPeriod, float phaseOffset,
            float buriedDepth, Transform leftClaw, Transform rightClaw)
        {
            _patrolA = patrolA;
            _patrolB = patrolB;
            _halfPeriod = halfPeriod;
            _phaseOffset = phaseOffset;
            _buriedDepth = buriedDepth;
            _leftClaw = leftClaw;
            _rightClaw = rightClaw;

            _time = phaseOffset * 5.3f;
            transform.position = CrabBehaviorRules.PatrolPoint(_patrolA, _patrolB,
                CrabBehaviorRules.PingPong(_time, _halfPeriod));
        }

        /// <summary>爆炸惊扰：短时间内强制缩沙（<paramref name="frames"/> 由导演按爆炸事件给出）。</summary>
        public void Alarm(int frames)
        {
            if (frames > _alarmFrames)
                _alarmFrames = frames;
        }

        /// <summary>推进一帧。<paramref name="distanceToNearestUnit"/> 由导演提供（无单位引用时传正无穷）。</summary>
        public void Tick(float dt, float distanceToNearestUnit)
        {
            if (dt <= 0f)
                return;

            _time += dt;

            if (_alarmFrames > 0)
                _alarmFrames--;

            // 警报期间等价于"有单位贴脸"。
            float effectiveDistance = _alarmFrames > 0 ? 0f : distanceToNearestUnit;
            _state = CrabBehaviorRules.Next(_state, effectiveDistance);

            float target = _state == CrabState.Buried ? 1f : 0f;
            float step = dt / CrabBehaviorRules.BuryLerpDuration;
            _buryProgress = Mathf.MoveTowards(_buryProgress, target, step);

            // ---- 位置：往返路径 + 下沉 ----
            Vector3 surface = CrabBehaviorRules.PatrolPoint(_patrolA, _patrolB,
                CrabBehaviorRules.PingPong(_time, _halfPeriod));

            float sink = _buryProgress * _buriedDepth;
            transform.position = new Vector3(surface.x, surface.y - sink, surface.z);

            // 埋没后整只隐藏（避免"沙丘里露出钳子"的穿帮）。
            float visibility = CrabBehaviorRules.BuriedVisibility(_buryProgress);
            SetVisible(visibility > 0.15f);

            // ---- 朝向：沿移动方向（横爬 = 侧身，视觉上朝行进方向即可）----
            Vector3 forward = (_patrolB - _patrolA);
            forward.y = 0f;
            if (forward.sqrMagnitude > 1e-6f)
            {
                // ping-pong 上行时朝 B、下行时朝 A。
                float pp = CrabBehaviorRules.PingPong(_time + dt * 0.5f, _halfPeriod);
                float ppNow = CrabBehaviorRules.PingPong(_time, _halfPeriod);
                Vector3 dir = pp >= ppNow ? forward : -forward;
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }

            // ---- 钳子摆动（左右反相）----
            float clawPhase = CrabBehaviorRules.ClawRate * _time * Mathf.PI * 2f + _phaseOffset * 6.28f;
            float clawAngle = CrabBehaviorRules.ClawAngleDegrees(clawPhase, 22f);

            // 缩沙时钳子收拢（幅度减半）。
            float clamp = Mathf.Lerp(1f, 0.35f, _buryProgress);
            if (_leftClaw != null)
                _leftClaw.localRotation = Quaternion.Euler(0f, clawAngle * clamp, 0f);
            if (_rightClaw != null)
                _rightClaw.localRotation = Quaternion.Euler(0f, -clawAngle * clamp, 0f);
        }

        Renderer[] _renderers;
        bool _visible = true;

        /// <summary>隐藏用 <c>Renderer.enabled</c> 而不是 <c>SetActive</c>（避免反复触发层级重算）。
        /// Renderer 数组在第一次需要切换时缓存一次。</summary>
        void SetVisible(bool visible)
        {
            if (visible == _visible)
                return;

            _visible = visible;

            if (_renderers == null)
                _renderers = GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = visible;
            }
        }
    }
}
