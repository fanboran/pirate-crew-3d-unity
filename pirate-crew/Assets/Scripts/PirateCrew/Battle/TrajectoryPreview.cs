using PirateCrew.PirateCrew.Combat;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 弹道轨迹预览（MonoBehaviour 薄壳，采样用纯逻辑 <see cref="Ballistics.PredictTrajectory"/>）。
    ///
    /// 【对应章节】§5.1 <c>drawTwangLine</c>（15 段虚线轨迹，逐段加重力）。
    ///
    /// 【3D 重写要点】
    ///   · Godot 版 30 个预实例化球体 + 自成一套 <c>speed_scale=18/gravity=-18</c>（预览 ≠ 实弹）
    ///     → 本实现用 <see cref="LineRenderer"/>，且<b>与实弹共用</b> <see cref="Ballistics"/> 与
    ///     <see cref="LevelGeometry"/> 的换算常量，从根上消除"预览≠实弹"。
    ///   · API 遵守风险清单 R3：用 <c>positionCount</c> + <c>SetPositions()</c>，
    ///     不用已过时的 <c>SetVertexCount</c>。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TrajectoryPreview : MonoBehaviour
    {
        [Tooltip("轨迹线组件；留空则 Awake 时取同对象上的 LineRenderer。")]
        [SerializeField] LineRenderer line;

        [Tooltip("预测采样段数（§5.1 原版 15 段）。")]
        [SerializeField] int sampleCount = Ballistics.DefaultPredictionSteps;

        [Tooltip("战斗平面世界 z（与 LevelGeometry.DefaultPlaneZ 一致）。")]
        [SerializeField] float planeZ = LevelGeometry.DefaultPlaneZ;

        [Tooltip("是否在轨迹最前面补一个起点。")]
        [SerializeField] bool includeOrigin = true;

        void Awake()
        {
            if (line == null)
                line = GetComponent<LineRenderer>();

            if (line != null)
            {
                line.useWorldSpace = true;
                line.positionCount = 0;
            }
        }

        /// <summary>
        /// 用 Flash 逻辑坐标与初速刷新轨迹。参数与 <see cref="AimThrowController"/> 传给
        /// <see cref="PirateBase.ApplyLaunchVelocity"/> 的完全同源。
        /// </summary>
        /// <param name="originPixelX">起点 Flash 像素 x。</param>
        /// <param name="originPixelY">起点 Flash 像素 y（y 向下）。</param>
        /// <param name="vxPixelsPerFrame">Flash 初速 x（px/帧）。</param>
        /// <param name="vyPixelsPerFrame">Flash 初速 y（px/帧）。</param>
        /// <param name="weight">重力权重（角色 = 1；武器见 §5.2）。</param>
        public void Show(
            float originPixelX, float originPixelY,
            float vxPixelsPerFrame, float vyPixelsPerFrame, float weight)
        {
            if (line == null)
                return;

            (float x, float y)[] points = Ballistics.PredictTrajectory(
                originPixelX, originPixelY, vxPixelsPerFrame, vyPixelsPerFrame, weight, sampleCount);

            int offset = includeOrigin ? 1 : 0;
            var positions = new Vector3[points.Length + offset];
            if (includeOrigin)
                positions[0] = LevelGeometry.PixelToWorld(originPixelX, originPixelY, planeZ);

            for (int i = 0; i < points.Length; i++)
                positions[i + offset] = LevelGeometry.PixelToWorld(points[i].x, points[i].y, planeZ);

            line.positionCount = positions.Length;
            line.SetPositions(positions);
        }

        /// <summary>以世界坐标起点刷新轨迹（内部转 Flash 像素，保证与实弹同一坐标系）。</summary>
        public void ShowFromWorld(
            Vector3 originWorld, float vxPixelsPerFrame, float vyPixelsPerFrame, float weight)
        {
            Vector2 px = LevelGeometry.WorldToPixel(originWorld);
            Show(px.x, px.y, vxPixelsPerFrame, vyPixelsPerFrame, weight);
        }

        /// <summary>隐藏轨迹。</summary>
        public void Hide()
        {
            if (line != null)
                line.positionCount = 0;
        }
    }
}
