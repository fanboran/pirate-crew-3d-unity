using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 弹道轨迹预览（MonoBehaviour 薄壳，采样用纯逻辑 <see cref="ThrowTrajectory"/>）。
    ///
    /// 【预览规格（投掷行为契约 #9）】可判定行为：
    ///   1. **落点终止**：弧线积分到首次穿地（<see cref="ThrowTrajectory.TryPredictUntilImpact"/>），
    ///      不按飞行时长截断；
    ///   2. **不入地**：写入的全部珠点 y ≥ 地面（穿地点按 y 插值收在地面）；
    ///   3. **落点标记恒显**：兜底步数内穿地必显示标记（旧预览远射程时标记消失）；
    ///   4. **方向可读 + 三维实体**（2026-10-05 走查反馈）：珠点沿弧线尺寸/色深递减
    ///      （大→小 = 起点→落点），落点组带随水平初速方向的指向箭头；全部件都是带深度
    ///      测试的世界物体，被单位/地形正常遮挡，禁止覆盖式渲染（旧预览线浮在角色上层的病灶）。
    ///
    /// 【视觉形态】锥形珠点弧线（暖黄→深橙顶点色渐变）+ 青色落点组（贴地环 + 中心点 +
    /// 指向箭头，环带微脉动）——暖色是"这条路"，青组是"落在这"；两族语义分开。
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

        /// <summary>珠点尺寸锥形的起点/落点倍率（相对 <see cref="dotSize"/>；提案）。</summary>
        public const float DotScaleStart = 1.15f;
        public const float DotScaleEnd = 0.5f;

        /// <summary>落点指向箭头（贴地三角）的长度与半宽（世界单位，提案；整体收在落点环内）。</summary>
        public const float ImpactArrowLength = 0.4f;
        public const float ImpactArrowHalfWidth = 0.18f;

        [Tooltip("珠点池大小（弧线最长显示的点数；更长弧线自动拉开点距）。")]
        [SerializeField] int dotPoolSize = 48;

        [Tooltip("珠点基准边长（世界单位，提案）：0.18 在基准档下约 3 px，随弧线锥形缩放。")]
        [SerializeField] float dotSize = 0.18f;

        [Tooltip("是否显示预测落点标记（贴地环 + 中心点 + 指向箭头）。")]
        [SerializeField] bool showImpactMarker = true;

        [Tooltip("预制体上遗留的旧轨迹 LineRenderer（两态重构退役件）；Awake 时直接禁用。")]
        [SerializeField] LineRenderer legacyLine;

        Transform _dotsRoot;
        Transform[] _dots;
        LineRenderer _impactMarker;
        Transform _impactArrow;
        Transform _impactCenter;
        readonly Vector3[] _samples = new Vector3[StandardThrowRules.PreviewMaxSteps];

        void Awake()
        {
            if (legacyLine == null)
                legacyLine = GetComponent<LineRenderer>();
            if (legacyLine != null)
            {
                legacyLine.enabled = false;   // 旧连续细线随预览重做退役
                if (legacyLine.sharedMaterial == null)
                    legacyLine.sharedMaterial = FallbackPreviewMaterial();
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
                HideImpactGroup();
                return;
            }

            // 珠点沿弧线**均匀铺满**：点距 = 采样数 / 池大小（向上取整 ≥1），弧线长短都完整显示；
            // 尺寸沿弧线锥形收小（大→小 = 起点→落点，方向可读）。
            int step = Mathf.Max(1, (count + dotPoolSize - 1) / dotPoolSize);
            Camera cam = Camera.current != null ? Camera.current : Camera.main;

            int shown = 0;
            for (int i = 0; i < count && shown < _dots.Length; i += step)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0f;
                float scale = dotSize * Mathf.Lerp(DotScaleStart, DotScaleEnd, t);
                Transform dot = _dots[shown++];
                dot.position = _samples[i];
                dot.localScale = new Vector3(scale, scale, scale);
                if (cam != null)
                    dot.rotation = Quaternion.LookRotation(cam.transform.forward);
            }
            SetDotsActive(shown);

            if (showImpactMarker)
                PlaceImpactGroup(impact, velocity);
            else
                HideImpactGroup();
        }

        /// <summary>隐藏轨迹。</summary>
        public void Hide()
        {
            SetDotsActive(0);
            HideImpactGroup();
        }

        // ------------------------------------------------------------------
        // 珠点池（顶点色锥形：颜色按池位渐变烘进各自 Mesh，共享一份白材质）
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
                : FallbackPreviewMaterial();

            _dots = new Transform[dotPoolSize];
            for (int i = 0; i < dotPoolSize; i++)
            {
                var dot = new GameObject("Dot" + i);
                dot.transform.SetParent(_dotsRoot, false);
                dot.transform.localScale = new Vector3(dotSize, dotSize, dotSize);
                var meshFilter = dot.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = CreateTintedQuad(DotColor(i, dotPoolSize));
                var renderer = dot.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _dots[i] = dot.transform;
                dot.SetActive(false);
            }
        }

        /// <summary>珠点锥形色：暖黄（起点）→ 深橙（落点），按池位渐变（提案）。</summary>
        static Color32 DotColor(int poolIndex, int poolSize)
        {
            float t = poolSize > 1 ? (float)poolIndex / (poolSize - 1) : 0f;
            return Color32.Lerp(new Color32(0xFF, 0xEC, 0x78, 0xE6), new Color32(0xFF, 0x78, 0x28, 0xE6), t);
        }

        /// <summary>带顶点色的单位面片（-0.5..0.5）；Sprites/Default 乘顶点色，材质保持白色。</summary>
        static Mesh CreateTintedQuad(Color32 tint)
        {
            var mesh = new Mesh { name = "PreviewDot" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.triangles = new[] { 0, 3, 1, 0, 2, 3 };
            mesh.colors32 = new[] { tint, tint, tint, tint };
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material FallbackPreviewMaterial()
        {
            // 无序列化材质时的兜底（正常装配链都带 Trajectory.mat）；着色全靠顶点色，材质保白。
            var shader = Shader.Find("Sprites/Default");
            var material = shader != null ? new Material(shader) : new Material(Shader.Find("Unlit/Color"));
            material.color = Color.white;
            return material;
        }

        // ------------------------------------------------------------------
        // 落点组：贴地环（微脉动）+ 中心点 + 指向箭头（随水平初速方向）
        // ------------------------------------------------------------------

        void PlaceImpactGroup(Vector3 impact, Vector3 velocity)
        {
            EnsureImpactGroup();
            if (_impactMarker == null)
                return;

            // 环微脉动（呼吸感提示"这是活的落点标记"，幅度收敛在环内不扩出）。
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 5f);
            float radius = ImpactMarkerRadius * pulse;
            for (int i = 0; i < ImpactMarkerSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / ImpactMarkerSegments;
                _impactMarker.SetPosition(i, impact + new Vector3(
                    Mathf.Cos(angle) * radius,
                    ImpactMarkerLift,
                    Mathf.Sin(angle) * radius));
            }
            _impactMarker.enabled = true;

            // 指向箭头：贴地三角尖朝水平初速方向（速度近垂直时的兜底 = 保持上次朝向）。
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            if (horizontal.sqrMagnitude > 1e-6f && _impactArrow != null)
            {
                _impactArrow.position = impact + Vector3.up * ImpactMarkerLift;
                _impactArrow.rotation = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
                _impactArrow.gameObject.SetActive(true);
            }

            // 中心点：贴地小方片，压在环心（略高于环线避免 z-fight）。
            if (_impactCenter != null)
            {
                _impactCenter.position = impact + Vector3.up * (ImpactMarkerLift + 0.01f);
                _impactCenter.gameObject.SetActive(true);
            }
        }

        void HideImpactGroup()
        {
            if (_impactMarker != null && _impactMarker.enabled)
                _impactMarker.enabled = false;
            if (_impactArrow != null && _impactArrow.gameObject.activeSelf)
                _impactArrow.gameObject.SetActive(false);
            if (_impactCenter != null && _impactCenter.gameObject.activeSelf)
                _impactCenter.gameObject.SetActive(false);
        }

        void EnsureImpactGroup()
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
                : FallbackPreviewMaterial();
            _impactMarker.enabled = false;

            Material material = _impactMarker.sharedMaterial;

            // 指向箭头：XZ 平面内朝 +Z 的三角（顶点色青），旋转交给 PlaceImpactGroup。
            var arrow = new GameObject("ImpactArrow");
            arrow.transform.SetParent(transform, false);
            var arrowFilter = arrow.AddComponent<MeshFilter>();
            var arrowMesh = new Mesh { name = "ImpactArrow" };
            arrowMesh.vertices = new[]
            {
                new Vector3(0f, 0f, ImpactArrowLength * 0.6f),
                new Vector3(-ImpactArrowHalfWidth, 0f, -ImpactArrowLength * 0.4f),
                new Vector3(ImpactArrowHalfWidth, 0f, -ImpactArrowLength * 0.4f),
            };
            arrowMesh.triangles = new[] { 0, 1, 2 };
            arrowMesh.colors32 = new[] { teal, teal, teal };
            arrowMesh.RecalculateBounds();
            arrowFilter.sharedMesh = arrowMesh;
            var arrowRenderer = arrow.AddComponent<MeshRenderer>();
            arrowRenderer.sharedMaterial = material;
            arrowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrowRenderer.receiveShadows = false;
            arrow.SetActive(false);
            _impactArrow = arrow.transform;

            // 中心点：贴地小方片（旋转放平由 PlaceImpactGroup 设定）。
            var center = new GameObject("ImpactCenter");
            center.transform.SetParent(transform, false);
            var centerFilter = center.AddComponent<MeshFilter>();
            centerFilter.sharedMesh = CreateTintedQuad(teal);
            center.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            center.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var centerRenderer = center.AddComponent<MeshRenderer>();
            centerRenderer.sharedMaterial = material;
            centerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            centerRenderer.receiveShadows = false;
            center.SetActive(false);
            _impactCenter = center.transform;
        }
    }
}
