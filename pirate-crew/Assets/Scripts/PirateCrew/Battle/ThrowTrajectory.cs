using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 3D 投掷物抛物线采样（纯 C#，可在无头验证台断言）。
    ///
    /// 【为什么要有这个类】
    ///   3D 化之后，Flash 的"2D 平面内受重力"变成"XZ 水平面内**匀速** + Y 方向受重力"：
    ///   重力与水平面正交，所以水平分量不再有加速度，竖直分量才有。
    ///   旧 2D 实现（平面内 vy += weight 的逐步积分）因此不再适用于预览，
    ///   若继续拿它画线就会与实弹分叉——正是 Godot 版"预览 ≠ 实弹"的病根。
    ///
    /// 【与实弹严格同源】
    ///   实弹走 PhysX：<c>v += g·dt; p += v·dt</c>（半隐式欧拉，dt = <see cref="LevelGeometry.FrameSeconds"/>）。
    ///   本类用**同一套离散格式**、同一份初速（<see cref="LevelGeometry.ThrowVelocity"/>）
    ///   与同一份重力（<see cref="LevelGeometry.WorldGravity"/>），故逐步严格一致：
    ///   预览线上第 i 个采样点 = 实弹第 i 个物理步的位置。
    ///
    /// 【谁在用】<see cref="TrajectoryPreview"/>（玩家预览）与 <c>AiEvaluation</c>（AI 落点评估）
    ///   共用本类，避免两套弹道口径。
    /// </summary>
    public static class ThrowTrajectory
    {
        /// <summary>默认采样步数（§5.1 原版预览 15 段）。</summary>
        public const int DefaultSteps = 15;

        /// <summary>
        /// 半隐式欧拉逐步积分，写入 <paramref name="buffer"/>（长度需 &gt;= <paramref name="steps"/>）。
        /// 采样点**不含**起点：第 i 个元素 = 第 i+1 步结束时的位置。
        /// </summary>
        public static void Predict(
            Vector3 origin, Vector3 initialVelocity, float gravityY,
            Vector3[] buffer, int steps, float stepSeconds = LevelGeometry.FrameSeconds)
        {
            Vector3 v = initialVelocity;
            Vector3 p = origin;

            for (int i = 0; i < steps && i < buffer.Length; i++)
            {
                // 与 PhysX 相同的半隐式欧拉：先更新速度，再用新速度更新位置。
                v.y += gravityY * stepSeconds;
                p += v * stepSeconds;
                buffer[i] = p;
            }
        }

        /// <summary>按角色的 weight 采样（weight 决定重力大小，§4.1/§5.2）。</summary>
        public static void PredictForWeight(
            Vector3 origin, Vector3 initialVelocity, float weight,
            Vector3[] buffer, int steps, float stepSeconds = LevelGeometry.FrameSeconds)
        {
            Predict(origin, initialVelocity, LevelGeometry.WorldGravityY(weight), buffer, steps, stepSeconds);
        }

        /// <summary>
        /// 直接由"Flash 平面初速 + 抬升"采样 —— 参数与 <see cref="LevelGeometry.ThrowVelocity"/>
        /// 的输入完全一致，供调用方少写一步换算。
        /// </summary>
        public static void PredictFromFlashSpeed(
            Vector3 origin, Vector3 horizontalDirection, float speedPixelsPerFrame, float weight,
            Vector3[] buffer, int steps, float stepSeconds = LevelGeometry.FrameSeconds)
        {
            Vector3 v0 = LevelGeometry.ThrowVelocity(horizontalDirection, speedPixelsPerFrame);
            PredictForWeight(origin, v0, weight, buffer, steps, stepSeconds);
        }
    }
}
