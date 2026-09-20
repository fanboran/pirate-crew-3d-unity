using UnityEngine;

namespace PirateCrew.Ambient
{
    /// <summary>
    /// 一群水下鱼（简化 boids：分离 / 对齐 / 聚集 + 锚点回归 + 水体盒约束）。
    /// MonoBehaviour 薄壳，由 <see cref="AmbientDirector"/> 统一 <c>Tick</c>；
    /// 规则与数值全部在 <see cref="BoidsRules"/>/<see cref="BoidsSettings"/>（纯 C#，无头可测）。
    ///
    /// 【为什么用"每条鱼一个 GameObject + 共享网格/材质"而不是每帧重建合并网格】
    /// 共享材质 + 相同网格的多个 Renderer 会被 SRP Batcher / GPU Instancing 批掉
    /// （14 条鱼 ≈ 1-2 个实际提交）；而每帧重建网格要 CPU 写 2000+ 顶点并产生 GC，得不偿失。
    ///
    /// 【为什么鱼不会被水挡住】水是半透明（Transparent 队列）且不写深度，
    /// 鱼是不透明（Geometry 队列）→ 先画鱼、再画水，透过水面能看到鱼。
    /// 鱼的**世界 Y 被硬夹在水体盒内**（水面以下、海床以上），因此不存在"跃出水面被人看见"的情况
    /// （任务书要求"仅在水面以下可见"）。
    ///
    /// 【无碰撞】不加 Collider —— 否则会碰弹道（场景文档 §9.4）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FishSchoolAgent : MonoBehaviour
    {
        BoidsSettings _settings;
        Vector3[] _positions;
        Vector3[] _velocities;
        Transform[] _fish;

        Vector3 _anchorCenter;
        Vector3 _anchorRadius;
        float _anchorSpeed;
        float _time;

        int _count;

        /// <summary>鱼条数。</summary>
        public int Count => _count;

        /// <summary>当前鱼群质心（报告/调试用）。</summary>
        public Vector3 Centroid => BoidsRules.Centroid(_positions, _count);

        /// <summary>
        /// 绑定鱼群。位置/速度数组由调用方（导演）创建并复用，避免每帧分配。
        /// </summary>
        public void Configure(BoidsSettings settings, Vector3[] positions, Vector3[] velocities,
            Transform[] fish, Vector3 anchorCenter, Vector3 anchorRadius, float anchorSpeed,
            AmbientRandom rng, float seedPhase)
        {
            _settings = settings;
            _positions = positions;
            _velocities = velocities;
            _fish = fish;
            _count = Mathf.Min(fish != null ? fish.Length : 0, positions != null ? positions.Length : 0);
            _anchorCenter = anchorCenter;
            _anchorRadius = anchorRadius;
            _anchorSpeed = anchorSpeed;
            _time = seedPhase;

            BoidsRules.Seed(rng, _positions, _velocities, _count, settings, anchorCenter);
            WriteTransforms();
        }

        /// <summary>爆炸惊散：给每条鱼一个远离爆心的冲量 + 短暂提速。</summary>
        public void Scatter(Vector3 blastPosition, float strength)
        {
            if (_positions == null || _velocities == null)
                return;

            float s = Mathf.Clamp01(strength);
            if (s <= 0f)
                return;

            for (int i = 0; i < _count; i++)
            {
                Vector3 away = _positions[i] - blastPosition;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-6f)
                    away = Vector3.forward;

                Vector3 push = away.normalized * (s * _settings.MaxSpeed * 2.2f);
                _velocities[i] += push;

                // 速度上限的硬夹（boids 每帧也会夹一次；这里先夹一次避免冲量把鱼顶出水体盒）。
                float speed = _velocities[i].magnitude;
                float cap = _settings.MaxSpeed * 2.4f;
                if (speed > cap)
                    _velocities[i] = _velocities[i] * (cap / speed);
            }
        }

        /// <summary>推进一帧。</summary>
        public void Tick(float dt)
        {
            if (_positions == null || _velocities == null || _count <= 0 || dt <= 0f)
                return;

            _time += dt;

            // 锚点做缓慢李萨如移动 → 整群沿近岸水域巡游（不会钉死在一个点）。
            Vector3 anchor = new Vector3(
                _anchorCenter.x + _anchorRadius.x * Mathf.Sin(_anchorSpeed * _time),
                _anchorCenter.y,
                _anchorCenter.z + _anchorRadius.z * Mathf.Sin(_anchorSpeed * 0.63f * _time + 1.2f));

            BoidsRules.Step(_positions, _velocities, _count, _settings, anchor, dt);
            WriteTransforms();
        }

        void WriteTransforms()
        {
            if (_fish == null)
                return;

            float wobble = _time * 6.2f;

            for (int i = 0; i < _count; i++)
            {
                Transform t = _fish[i];
                if (t == null)
                    continue;

                Vector3 pos = _positions[i];
                // 硬保证：鱼永远在水面以下（水体盒上界已在 BoidsSettings 里设为水面 − 0.08 以下）。
                t.position = pos;

                Vector3 vel = _velocities[i];
                if (vel.sqrMagnitude > 1e-6f)
                    t.rotation = Quaternion.LookRotation(vel.normalized, Vector3.up);

                // 摆尾：小幅侧向摇摆（低模鱼没有骨骼，用整体 roll 近似）。
                t.localRotation *= Quaternion.Euler(0f, 0f, Mathf.Sin(wobble + i * 1.7f) * 7f);
            }
        }
    }
}
