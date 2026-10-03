using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 弹道轨迹预览（MonoBehaviour 薄壳，采样用纯逻辑 <see cref="ThrowTrajectory"/>）。
    ///
    /// 【预览重做（创始人裁决 2026-10-03，投掷行为契约 #9）】三条可判定行为：
    ///   1. **落点终止**：弧线积分到首次穿地（<see cref="ThrowTrajectory.TryPredictUntilImpact"/>），
    ///      不按飞行时长截断；
    ///   2. **不入地**：写入的全部珠点 y ≥ 地面（穿地点按 y 插值收在地面）；
    ///   3. **落点标记恒显**：兜底步数内穿地必显示标记（旧预览远射程时标记消失）。
    ///
    /// 【视觉形态】珠点弧线（池化面片 quad 串，像素语言一致）+ 青色落点环（沿用既有口径）。
    /// 读数（方向/仰角/力度）归操作 HUD，不在 3D 场景写字。
    ///
    /// 【预览 = 实弹】初速与重力由调用方（<see cref="BattleInteractionController"/>）从
    /// <see cref="StandardThrowRules"/> 取**与执行同源**的值传入；本类不自算任何弹道参数。
    /// </summary>
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        /// <summary>落点标记环的半径（世界单位，提案）：直径 0.9 ≈ 单位脚边，读作"落在这儿"。</summary>
        public const float ImpactMarkerRadius = 0.45f;

        /// <summary>落点标记环的段数（闭合圆）。低模纪律：24 段足够圆润。</summary>
        public const int ImpactMarkerSegments = 24;

        /// <summary>落点标记环的抬高（世界单位，防与地面 z-fight）。</summary>
        public const float ImpactMarkerLift = 0.06f;

        [Tooltip("珠点池大小（弧线最长显示的点数；更长弧线自动拉开点距）。")]
        [SerializeField] int dotPoolSize = 48;

        [Tooltip("珠点边长（世界单位，提案）：0.18 在基准档下约 3 px。")]
        [SerializeField] float dotSize = 0.18f;

        [Tooltip("是否显示预测落点标记。")]
        [SerializeField] bool showImpactMarker = true;

        [Tooltip("预制体上遗留的旧轨迹 LineRenderer（两态重构退役件）；Awake 时直接禁用。")]
        [SerializeField] LineRenderer legacyLine;

        Transform _dotsRoot;
        Transform[] _dots;
        LineRenderer _impactMarker;
        readonly Vector3[] _samples = new Vector3[StandardThrowRules.PreviewMaxSteps];

        void Awake()
        {
            if (legacyLine == null)
                legacyLine = GetComponent<LineRenderer>();
            if (legacyLine != null)
            {
                legacyLine.enabled = false;   // 旧连续细线随预览重做退役
                if (legacyLine.sharedMaterial == null)
                    legacyLine.sharedMaterial = FallbackDotMaterial();
            }

            EnsureDots();
        }

        /// <summary>
        /// 刷新轨迹。初速/重力与执行路径同源（<see cref="StandardThrowRules"/>，由调用方传入）——
        /// 「预览 = 实弹」的换算不在本类内发生第二次。
        /// </summary>
        /// <param name="originWorld">投掷起点（= 单位枢轴 + ThrowOriginHeight）。</param>
        /// <param name="velocity">完整初速向量（米制）。</param>
        public void Show(Vector3 originWorld, Vector3 velocity)
        {
            bool landed = ThrowTrajectory.TryPredictUntilImpact(
                originWorld, velocity, StandardThrowRules.LaunchGravityY,
                _samples, out int count, out Vector3 impact);

            if (!landed)
            {
                // 兜底步数内不穿地（理论不可达的极端平射）：整条隐藏，不出半截线。
                SetDotsActive(0);
                HideImpactMarker();
                return;
            }

            // 珠点沿弧线**均匀铺满**：点距 = 采样数 / 池大小（向上取整 ≥1），弧线长短都完整显示。
            int step = Mathf.Max(1, (count + dotPoolSize - 1) / dotPoolSize);
            Camera cam = Camera.current != null ? Camera.current : Camera.main;

            int shown = 0;
            for (int i = 0; i < count && shown < _dots.Length; i += step)
            {
                Transform dot = _dots[shown++];
                dot.position = _samples[i];
                if (cam != null)
                    dot.rotation = Quaternion.LookRotation(cam.transform.forward);
            }
            SetDotsActive(shown);

            if (showImpactMarker)
                PlaceImpactMarker(impact);
            else
                HideImpactMarker();
        }

        /// <summary>隐藏轨迹。</summary>
        public void Hide()
        {
            SetDotsActive(0);
            HideImpactMarker();
        }

        // ------------------------------------------------------------------
        // 珠点池
        // ------------------------------------------------------------------

        void SetDotsActive(int count)
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i].gameObject.activeSelf != i < count)
                    _dots[i].gameObject.SetActive(i < count);
            }
        }

        void EnsureDots()
        {
            if (_dots != null)
                return;

            var root = new GameObject("TrajectoryDots");
            root.transform.SetParent(transform, false);
            _dotsRoot = root.transform;

            Material material = legacyLine != null && legacyLine.sharedMaterial != null
                ? legacyLine.sharedMaterial
                : FallbackDotMaterial();
            MeshFilter quadMesh = CreateQuadTemplate();

            _dots = new Transform[dotPoolSize];
            for (int i = 0; i < dotPoolSize; i++)
            {
                var dot = new GameObject("Dot" + i);
                dot.transform.SetParent(_dotsRoot, false);
                dot.transform.localScale = new Vector3(dotSize, dotSize, dotSize);
                var meshFilter = dot.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = quadMesh.sharedMesh;
                var renderer = dot.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _dots[i] = dot.transform;
                dot.SetActive(false);
            }
        }

        /// <summary>四边形模板（Primitive Quad 去碰撞体；池内共享同一 Mesh）。</summary>
        MeshFilter CreateQuadTemplate()
        {
            var template = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Collider collider = template.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            template.SetActive(false);
            template.transform.SetParent(transform, false);
            return template.GetComponent<MeshFilter>();
        }

        static Material FallbackDotMaterial()
        {
            // 无序列化材质时的兜底（正常装配链都带 Trajectory.mat）；像素化管线会按色带重着色。
            var shader = Shader.Find("Sprites/Default");
            var material = shader != null ? new Material(shader) : new Material(Shader.Find("Unlit/Color"));
            material.color = new Color32(0xFF, 0xE6, 0x4D, 0xE6);   // 暖黄，旧轨迹线同族
            return material;
        }

        // ------------------------------------------------------------------
        // 落点标记
        // ------------------------------------------------------------------

        void PlaceImpactMarker(Vector3 impact)
        {
            EnsureImpactMarker();
            if (_impactMarker == null)
                return;

            for (int i = 0; i < ImpactMarkerSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / ImpactMarkerSegments;
                _impactMarker.SetPosition(i, impact + new Vector3(
                    Mathf.Cos(angle) * ImpactMarkerRadius,
                    ImpactMarkerLift,
                    Mathf.Sin(angle) * ImpactMarkerRadius));
            }

            _impactMarker.enabled = true;
        }

        void HideImpactMarker()
        {
            if (_impactMarker != null && _impactMarker.enabled)
                _impactMarker.enabled = false;
        }

        void EnsureImpactMarker()
        {
            if (_impactMarker != null)
                return;

            // 运行时动态创建（不改场景/Prefab）；材质复用珠点的（Sprites/Default 读顶点色），
            // 颜色 = 选中青 #49D9D6（Art Bible §2.2，与选中描边/光环同源）。
            var go = new GameObject("ImpactMarker");
            go.transform.SetParent(transform, false);
            _impactMarker = go.AddComponent<LineRenderer>();
            _impactMarker.useWorldSpace = true;
            _impactMarker.loop = true;
            _impactMarker.positionCount = ImpactMarkerSegments;
            _impactMarker.widthMultiplier = 0.05f;
            _impactMarker.numCapVertices = 0;
            Color32 teal = new Color32(0x49, 0xD9, 0xD6, 0xE6);
            _impactMarker.startColor = teal;
            _impactMarker.endColor = teal;
            _impactMarker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _impactMarker.receiveShadows = false;
            _impactMarker.sharedMaterial = legacyLine != null
                ? legacyLine.sharedMaterial
                : FallbackDotMaterial();
            _impactMarker.enabled = false;
        }
    }
}
