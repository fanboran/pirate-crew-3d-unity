using UnityEngine;

namespace PirateCrew.PirateCrew.Fx
{
    /// <summary>
    /// 对象池化的「单片四边形」特效体：地面光环、冲击波环、涟漪、伤害数字都用它。
    ///
    /// 【为什么用 Quad 而不是粒子】环/数字需要"整体缩放 + 整体淡出"的确定性动画
    /// （ParticleSystem 的 sizeOverLifetime/colorOverLifetime 做环的扩张会糊边，且数字需要
    /// 精确的字形贴图）。一个 Quad + 每实例材质 = 1 个 DrawCall，量级完全够。
    ///
    /// 【朝向】两种模式：
    ///   · <c>billboard</c>：每帧贴相机朝向（伤害数字、面向玩家的提示）；
    ///   · <c>faceUp</c>：绕 X 转 90° 躺在 XZ 地面（地面光环、涟漪、冲击波）。
    /// 内置 Quad 的正面法线是 -Z、UV 与之匹配，故 billboard 用 <c>cam.rotation</c>、
    /// faceUp 用 <c>Euler(90,0,0)</c> 时贴图都不会镜像（本工程相机在 +Z 高处朝 -Z——见
    /// `docs/M2-3D空间模型对齐.md:48`）。
    ///
    /// 【尺寸是二维的】环用等比（宽=高=直径），伤害数字用非等比（宽随位数增长），
    /// 故统一以 <see cref="Vector2"/>（宽, 高，世界单位）表达尺寸。
    ///
    /// 【材质】每实例一份材质（从 FxMaterials 的两个 FX shader 建），池化复用 → 实例数受池上限
    /// 约束，不会随特效次数无限增长。池按「加法/透明 × billboard/faceUp」四类分开缓存。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FxSpriteFx : MonoBehaviour
    {
        MeshRenderer _renderer;
        Material _material;
        bool _billboard;
        bool _faceUp;
        bool _additive;

        bool _playing;
        float _age;
        float _life;
        Vector2 _startSize = Vector2.one;
        Vector2 _endSize = Vector2.one;
        Color _startColor = Color.white;
        Color _endColor = Color.white;
        float _rise;
        Vector3 _origin;

        /// <summary>本实例材质（可读，便于调试）。</summary>
        public Material Material => _material;

        /// <summary>是否面向相机（对象池按朝向分栈用）。</summary>
        internal bool IsBillboard => _billboard;

        /// <summary>是否加法混合（对象池按混合模式分栈用）。</summary>
        internal bool IsAdditive => _additive;

        /// <summary>创建一个四边形特效体（未激活到池，直接可用）。</summary>
        public static FxSpriteFx Create(string name, bool additive, bool billboard)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;

            // 视觉特效不需要碰撞体；图元自带的 MeshCollider 必须移除（否则会挡住瞄准射线）。
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            var fx = go.AddComponent<FxSpriteFx>();
            fx._billboard = billboard;
            fx._faceUp = !billboard;
            fx._additive = additive;
            fx._renderer = go.GetComponent<MeshRenderer>();
            if (fx._renderer != null)
            {
                fx._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fx._renderer.receiveShadows = false;
                fx._renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                fx._renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            Shader shader = FxMaterials.GetShader(additive);
            if (shader != null)
                fx._material = new Material(shader) { name = name + "_Mat", hideFlags = HideFlags.DontSave };

            if (fx._renderer != null)
                fx._renderer.enabled = fx._material != null;

            fx.ApplyOrientation();
            return fx;
        }

        /// <summary>更换贴图（伤害数字每次弹字都要换）。</summary>
        public void SetTexture(Texture2D texture)
        {
            FxMaterials.ApplyTexture(_material, texture);
        }

        /// <summary>设置整体颜色（含 alpha）。</summary>
        public void SetColor(Color color)
        {
            FxMaterials.ApplyTint(_material, color);
        }

        /// <summary>设置尺寸（宽, 高；世界单位）。Quad 本身 1×1，故 scale 即尺寸。</summary>
        public void SetSize(Vector2 size)
        {
            transform.localScale = new Vector3(
                Mathf.Max(0.001f, size.x), Mathf.Max(0.001f, size.y), 1f);
        }

        /// <summary>设置等比直径（环类用）。</summary>
        public void SetDiameter(float diameter)
        {
            float d = Mathf.Max(0.001f, diameter);
            SetSize(new Vector2(d, d));
        }

        /// <summary>摆放位置。</summary>
        public void SetPosition(Vector3 position)
        {
            transform.position = position;
        }

        /// <summary>
        /// 播一次「从 startSize 到 endSize、颜色从 startColor 淡到 endColor、期间上浮 rise」的动画，
        /// 结束后自动归还对象池。
        /// </summary>
        public void PlayOnce(
            Vector3 position, Vector2 startSize, Vector2 endSize,
            Color startColor, Color endColor, float life, float rise = 0f)
        {
            _playing = true;
            _age = 0f;
            _life = Mathf.Max(0.02f, life);
            _startSize = startSize;
            _endSize = endSize;
            _startColor = startColor;
            _endColor = endColor;
            _rise = rise;
            _origin = position;

            transform.position = position;
            SetSize(startSize);
            SetColor(startColor);
            ApplyOrientation();
            gameObject.SetActive(true);
        }

        /// <summary>立即结束并归还对象池。</summary>
        public void Despawn()
        {
            if (!_playing)
                return;
            _playing = false;
            FxPool.Return(this);
        }

        /// <summary>归还前由对象池调用。</summary>
        internal void PrepareForPool()
        {
            _playing = false;
        }

        void Update()
        {
            if (_billboard)
            {
                Camera cam = FxCamera.Main;
                if (cam != null)
                    transform.rotation = cam.transform.rotation;
            }

            if (!_playing)
                return;

            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);

            SetSize(Vector2.Lerp(_startSize, _endSize, t));
            SetColor(Color.Lerp(_startColor, _endColor, t));
            if (!Mathf.Approximately(_rise, 0f))
                transform.position = _origin + Vector3.up * (_rise * t);

            if (t >= 1f)
                Despawn();
        }

        void ApplyOrientation()
        {
            if (_faceUp)
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            else
            {
                Camera cam = FxCamera.Main;
                if (cam != null)
                    transform.rotation = cam.transform.rotation;
            }
        }
    }

    /// <summary>相机查询缓存（<c>Camera.main</c> 每帧查找有开销，缓存并容错）。</summary>
    internal static class FxCamera
    {
        static Camera _cached;

        public static Camera Main
        {
            get
            {
                if (_cached != null)
                    return _cached;
                _cached = Camera.main;
                return _cached;
            }
        }
    }
}
