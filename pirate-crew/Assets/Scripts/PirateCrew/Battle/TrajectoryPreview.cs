using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 弹道轨迹预览（MonoBehaviour 薄壳，采样用纯逻辑 <see cref="ThrowTrajectory"/>）。
    ///
    /// 【对应章节】§5.1 <c>drawTwangLine</c>（15 段虚线轨迹，逐段加重力）。
    ///
    /// 【3D 重写要点】
    ///   · Godot 版 30 个预实例化球体 + 自成一套 <c>speed_scale=18 / gravity=-18</c>（预览 ≠ 实弹，
    ///     实弹是 <c>velocity = dir * power</c>、<c>gravity = -9.8</c>）→ 本实现用
    ///     <see cref="LineRenderer"/>，且<b>与实弹共用</b> <see cref="LevelGeometry.ThrowVelocity"/> 的初速、
    ///     <see cref="LevelGeometry.WorldGravity"/> 的重力、<see cref="ThrowTrajectory"/> 的半隐式欧拉，
    ///     从根上消除"预览≠实弹"。
    ///   · 重力沿 -Y，水平面（XZ）内**匀速**——3D 化后重力与水平面正交（见 ThrowTrajectory 类头）。
    ///   · API 遵守风险清单 R3：用 <c>positionCount</c> + <c>SetPositions()</c>，不用已过时的 <c>SetVertexCount</c>。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        [Tooltip("轨迹线组件；留空则 Awake 时取同对象上的 LineRenderer。")]
        [SerializeField] LineRenderer line;

        [Tooltip("预测采样段数（§5.1 原版 15 段）。")]
        [SerializeField] int sampleCount = ThrowTrajectory.DefaultSteps;

        [Tooltip("是否在轨迹最前面补一个起点。")]
        [SerializeField] bool includeOrigin = true;

        Vector3[] _buffer;

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
        public void Show(Vector3 originWorld, Vector3 horizontalDirection, float speedPixelsPerFrame, float weight)
        {
            if (line == null)
                return;

            if (_buffer == null || _buffer.Length < sampleCount)
                _buffer = new Vector3[Mathf.Max(1, sampleCount)];

            ThrowTrajectory.PredictFromFlashSpeed(
                originWorld, horizontalDirection, speedPixelsPerFrame, weight, _buffer, sampleCount);

            int offset = includeOrigin ? 1 : 0;
            var positions = new Vector3[sampleCount + offset];
            if (includeOrigin)
                positions[0] = originWorld;

            for (int i = 0; i < sampleCount; i++)
                positions[i + offset] = _buffer[i];

            line.positionCount = positions.Length;
            line.SetPositions(positions);
        }

        /// <summary>隐藏轨迹。</summary>
        public void Hide()
        {
            if (line != null)
                line.positionCount = 0;
        }
    }
}
