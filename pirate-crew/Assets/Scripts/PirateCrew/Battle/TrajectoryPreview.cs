using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 弹道轨迹预览（MonoBehaviour 薄壳，采样用纯逻辑 <see cref="ThrowTrajectory"/>）。
    ///
    /// 【对应章节】§5.1 <c>drawTwangLine</c>（15 段虚线轨迹，逐段加重力）；
    ///             M4 §3.2：步数随射程延长 + 预测落点标记。
    ///
    /// 【3D 重写要点】
    ///   · Godot 版 30 个预实例化球体 + 自成一套 <c>speed_scale=18 / gravity=-18</c>（预览 ≠ 实弹，
    ///     实弹是 <c>velocity = dir * power</c>、<c>gravity = -9.8</c>）→ 本实现用
    ///     <see cref="LineRenderer"/>，且<b>与实弹共用</b> <see cref="LevelGeometry.ThrowVelocity"/> 的初速、
    ///     <see cref="LevelGeometry.WorldGravity"/> 的重力、<see cref="ThrowTrajectory"/> 的半隐式欧拉，
    ///     从根上消除"预览≠实弹"。
    ///   · 重力沿 -Y，水平面（XZ）内**匀速**——3D 化后重力与水平面正交（见 ThrowTrajectory 类头）。
    ///   · API 遵守风险清单 R3：用 <c>positionCount</c> + <c>SetPositions()</c>，不用已过时的 <c>SetVertexCount</c>。
    ///
    /// 【M4 落点标记（提案/待定）】小环（LineRenderer 闭合圆，复用预览线的 Sprites/Default 材质），
    ///   选中青 #49D9D6（与选中描边/选中光环同源，Art Bible §2.2）。落点由
    ///   <see cref="ThrowTrajectory.TryGetImpactPoint"/> 对采样序列做穿地插值（纯数学，无额外射线）。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        /// <summary>落点标记环的半径（世界单位，提案）：直径 0.9 ≈ 单位脚边，读作"落在这儿"。</summary>
        public const float ImpactMarkerRadius = 0.45f;

        /// <summary>落点标记环的段数（闭合圆）。低模纪律：24 段足够圆润。</summary>
        public const int ImpactMarkerSegments = 24;

        /// <summary>落点标记环的抬高（世界单位，防与地面 z-fight）。</summary>
        public const float ImpactMarkerLift = 0.06f;

        [Tooltip("轨迹线组件；留空则 Awake 时取同对象上的 LineRenderer。")]
        [SerializeField] LineRenderer line;

        [Tooltip("默认预测采样段数（§5.1 原版 15 段）；Show 可按力度传延长步数（M4 §3.2）。")]
        [SerializeField] int sampleCount = ThrowTrajectory.DefaultSteps;

        [Tooltip("是否在轨迹最前面补一个起点。")]
        [SerializeField] bool includeOrigin = true;

        [Tooltip("是否显示预测落点标记（M4 §3.2，提案）。")]
        [SerializeField] bool showImpactMarker = true;

        Vector3[] _buffer;
        LineRenderer _impactMarker;

        void Awake()
        {
            if (line == null)
                line = GetComponent<LineRenderer>();

            if (line != null)
            {
                line.useWorldSpace = true;
                line.positionCount = 0;
            }

            _buffer = new Vector3[Mathf.Max(1, sampleCount)];
        }

        /// <summary>
        /// 刷新轨迹。参数与 <see cref="AimThrowController"/> 真正发射时传给
        /// <see cref="PirateBase.ApplyLaunchVelocity(Vector3)"/> 的完全同源
        /// （同一个 <see cref="LevelGeometry.ThrowVelocity"/> 调用点语义）。
        /// </summary>
        /// <param name="originWorld">起点世界坐标。</param>
        /// <param name="horizontalDirection">XZ 平面上的投掷方向（未归一化亦可，内部会归一化并加抬升）。</param>
        /// <param name="speedPixelsPerFrame">Flash 口径的初速大小（px/帧，由 twang 的 twangMax 限速决定）。</param>
        /// <param name="weight">重力权重（角色 = 1；武器见 §5.2）。</param>
        /// <param name="steps">采样步数（M4 §3.2：随力度延长，见 <see cref="ThrowTrajectory.StepsForSpeed"/>）；
        /// 缺省保持原版 15 段。</param>
        public void Show(Vector3 originWorld, Vector3 horizontalDirection, float speedPixelsPerFrame, float weight,
            int steps = ThrowTrajectory.DefaultSteps)
        {
            if (line == null)
                return;

            steps = Mathf.Clamp(steps, 1, ThrowTrajectory.MaxSteps);
            if (_buffer == null || _buffer.Length < steps)
                _buffer = new Vector3[steps];

            ThrowTrajectory.PredictFromFlashSpeed(
                originWorld, horizontalDirection, speedPixelsPerFrame, weight, _buffer, steps);

            int offset = includeOrigin ? 1 : 0;
            var positions = new Vector3[steps + offset];
            if (includeOrigin)
                positions[0] = originWorld;

            for (int i = 0; i < steps; i++)
                positions[i + offset] = _buffer[i];

            line.positionCount = positions.Length;
            line.SetPositions(positions);

            if (showImpactMarker
                && ThrowTrajectory.TryGetImpactPoint(
                    originWorld, _buffer, steps, LevelGeometry.GroundTopY, out Vector3 impact))
            {
                PlaceImpactMarker(impact);
            }
            else
            {
                HideImpactMarker();
            }
        }

        /// <summary>隐藏轨迹。</summary>
        public void Hide()
        {
            if (line != null)
                line.positionCount = 0;
            HideImpactMarker();
        }

        // ------------------------------------------------------------------
        // 落点标记（M4 §3.2，提案/待定）
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

            // 运行时动态创建（不改场景/Prefab）；材质复用预览线的（Sprites/Default 读顶点色），
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
            if (line != null && line.sharedMaterial != null)
                _impactMarker.sharedMaterial = line.sharedMaterial;
            _impactMarker.enabled = false;
        }
    }
}
