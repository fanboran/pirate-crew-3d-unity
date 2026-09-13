using UnityEngine;

namespace PirateCrew.PirateCrew.Fx
{
    /// <summary>
    /// 一次粒子爆发的参数包（纯数据）。由各特效脚本按 <see cref="FxRules"/> 的映射填好后交给
    /// <see cref="FxParticles.Play"/>。用 struct 是为了避免每次爆发分配堆对象（GC 纪律）。
    /// </summary>
    public struct FxBurstSpec
    {
        /// <summary>爆发世界坐标（发射点）。**必须设置**：System 用 World 模拟空间，
        /// 对象池里的系统停在池根原点，不设位置会把所有粒子发到世界原点。</summary>
        public Vector3 Position;

        /// <summary>材质档。</summary>
        public FxMaterial Material;

        /// <summary>粒子数（burst 一次性发射，不持续发射）。</summary>
        public int Count;

        /// <summary>基准寿命（秒）。</summary>
        public float Lifetime;

        /// <summary>寿命随机浮动比例（0-1，0.3 = ±30%）。</summary>
        public float LifetimeVariance;

        /// <summary>基准初速（世界单位/秒）。</summary>
        public float Speed;

        /// <summary>初速随机浮动比例（0-1）。</summary>
        public float SpeedVariance;

        /// <summary>起始尺寸（世界单位，= 贴图直径）。</summary>
        public float StartSize;

        /// <summary>结束尺寸倍率（1 = 不变；烟雾 &gt;1 膨胀）。</summary>
        public float EndSize;

        /// <summary>重力倍率（1 = 跟随世界重力，见 LevelGeometry.WorldGravity）；0 = 无重力。</summary>
        public float Gravity;

        /// <summary>发射球半径（世界单位）；0.02 近似点发射。</summary>
        public float Radius;

        /// <summary>起始不透明度。</summary>
        public float StartAlpha;

        /// <summary>淡出中途点的不透明度（配合 FadeStart）。</summary>
        public float EndAlpha;

        /// <summary>淡出中途点（归一化寿命，0-1）。</summary>
        public float FadeStart;

        /// <summary>竖直漂移速度（世界单位/秒，烟雾上浮用）。</summary>
        public float RiseSpeed;

        /// <summary>自旋速度（度/秒，木屑翻滚用）；0 = 不自旋。</summary>
        public float RotationSpeed;

        /// <summary>渲染排序微调（同一位置叠加时决定谁在前）。</summary>
        public float SortingFudge;
    }

    /// <summary>
    /// 对象池化的粒子爆发体（`[FxPool]` 子物体上的一个 ParticleSystem 薄壳）。
    ///
    /// 【为什么一个爆发体一个 ParticleSystem】逐特效（火球/火花/木屑/烟）各用一个系统是
    /// 最省的画法：不同材质必然分 DrawCall，拆开只是让同一材质的粒子能跟别处的粒子合批，
    /// 且各系统的 shape/重力/寿命互不干扰。一次爆炸 7 个系统 + 1 个冲击波 Quad = 8 个 DrawCall
    /// （见 `ExplosionFx` 头注释与交付报告的性能一节）。
    ///
    /// 【burst 而非持续发射】`emission.rateOverTime = 0` + `SetBursts`，一次发完即停，
    /// 系统在最后一颗粒子死亡后经 <c>stopAction = Callback</c> 回调归还对象池 —— 不会出现
    /// "特效早该结束却还在慢慢滴粒子"的持续发射浪费。
    ///
    /// 【Simulation Space = World 的必要性】爆炸/水花都发生在固定世界坐标上，用 World 让粒子
    /// 脱离"父物体变换"独立存在：对象池复用同一个小物体时，若用 Local，上一次残留的粒子会被
    /// 下一次复用时的位移/缩放整体拖动。World 空间 + `Clear(true)` 双保险。
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class FxParticles : MonoBehaviour
    {
        ParticleSystem _system;
        ParticleSystemRenderer _renderer;
        ParticleSystem.Burst[] _burstCache;
        float _deadline;
        bool _active;

        void Awake()
        {
            _system = GetComponent<ParticleSystem>();
            _renderer = GetComponent<ParticleSystemRenderer>();
            _burstCache = new ParticleSystem.Burst[1];

            ParticleSystem.MainModule main = _system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Callback;
            if (_renderer != null)
            {
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                // 先定 renderMode 再设 alignment：alignment 只在 Billboard/StretchedBillboard 下有意义。
                _renderer.renderMode = ParticleSystemRenderMode.Billboard;
                _renderer.alignment = ParticleSystemRenderSpace.View;
            }
        }

        /// <summary>按参数包发射一次。</summary>
        public void Play(in FxBurstSpec spec)
        {
            if (_system == null)
                return;

            int count = Mathf.Clamp(spec.Count, 1, 512);
            float life = Mathf.Max(0.05f, spec.Lifetime);
            float speed = Mathf.Max(0f, spec.Speed);
            float size = Mathf.Max(0.005f, spec.StartSize);

            // World 模拟空间 + 池中系统停在原点 → 必须先摆到爆发点，否则粒子全在世界原点出现。
            transform.position = spec.Position;
            transform.localScale = Vector3.one;

            ParticleSystem.MainModule main = _system.main;
            main.duration = life * 1.25f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                life * (1f - spec.LifetimeVariance), life * (1f + spec.LifetimeVariance));
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                speed * (1f - spec.SpeedVariance), speed * (1f + spec.SpeedVariance));
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.75f, size * 1.25f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, spec.StartAlpha * 0.75f),
                new Color(1f, 1f, 1f, spec.StartAlpha));
            main.gravityModifier = spec.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            main.stopAction = ParticleSystemStopAction.Callback;

            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            _burstCache[0] = new ParticleSystem.Burst(0f, (short)count);
            emission.SetBursts(_burstCache);

            ParticleSystem.ShapeModule shape = _system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.01f, spec.Radius);
            shape.radiusThickness = 1f;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;

            // 颜色只做 alpha 淡出（色相由材质 _Color 决定），避免"材质色 × 生命曲线色"二次相乘发暗。
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = _system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(Mathf.Clamp01(spec.EndAlpha), Mathf.Clamp01(spec.FadeStart)),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _system.sizeOverLifetime;
            sizeOverLifetime.enabled = !Mathf.Approximately(spec.EndSize, 1f);
            if (sizeOverLifetime.enabled)
            {
                var curve = new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1f, Mathf.Max(0.01f, spec.EndSize)));
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }

            ParticleSystem.VelocityOverLifetimeModule velocity = _system.velocityOverLifetime;
            velocity.enabled = !Mathf.Approximately(spec.RiseSpeed, 0f);
            if (velocity.enabled)
            {
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.y = new ParticleSystem.MinMaxCurve(spec.RiseSpeed * 0.6f, spec.RiseSpeed);
            }

            ParticleSystem.RotationOverLifetimeModule rotation = _system.rotationOverLifetime;
            rotation.enabled = !Mathf.Approximately(spec.RotationSpeed, 0f);
            if (rotation.enabled)
                rotation.z = new ParticleSystem.MinMaxCurve(-spec.RotationSpeed, spec.RotationSpeed);

            if (_renderer != null)
            {
                _renderer.renderMode = ParticleSystemRenderMode.Billboard;
                _renderer.alignment = ParticleSystemRenderSpace.View;
                _renderer.sharedMaterial = FxMaterials.Get(spec.Material);
                _renderer.sortingFudge = spec.SortingFudge;
                _renderer.enabled = _renderer.sharedMaterial != null;
            }

            _system.Clear(true);
            _system.Play(true);
            _active = true;
            // 兜底回收：万一 stopAction 回调因对象被禁用等原因丢失，也不会漏在池外。
            _deadline = Time.time + main.duration + life * (1f + spec.LifetimeVariance) + 0.75f;
        }

        void Update()
        {
            if (_active && Time.time >= _deadline)
                Despawn();
        }

        /// <summary>ParticleSystem 停止回调（`stopAction = Callback`）。</summary>
        void OnParticleSystemStopped()
        {
            Despawn();
        }

        /// <summary>立即结束并归还对象池。</summary>
        public void Despawn()
        {
            if (!_active)
                return;
            _active = false;
            if (_system != null)
                _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            FxPool.Return(this);
        }

        /// <summary>归还前由对象池调用（清残留、挂起对象）。</summary>
        internal void PrepareForPool()
        {
            _active = false;
            if (_system != null)
                _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
