using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 3D 投掷物抛物线采样（纯 C#，可在无头验证台断言）。
    ///
    /// 【为什么要有这个类】
    ///   3D 化之后，2D 的"平面内受重力"变成"XZ 水平面内**匀速** + Y 方向受重力"：
    ///   重力与水平面正交，所以水平分量不再有加速度，竖直分量才有。
    ///   若拿 2D 平面积分画预览就会与实弹分叉——这正是"预览 ≠ 实弹"的病根。
    ///
    /// 【与实弹严格同源】
    ///   实弹走 PhysX：<c>v += g·dt; p += v·dt</c>（半隐式欧拉，dt = <see cref="LevelGeometry.FrameSeconds"/>）。
    ///   本类用**同一套离散格式**；初速与重力由调用方传入（<see cref="StandardThrowRules"/> 单源，
    ///   米制重立后不再有 weight/Flash px 分流），故逐步严格一致：
    ///   预览线上第 i 个采样点 = 实弹第 i 个物理步的位置。
    ///
    /// 【谁在用】<see cref="TrajectoryPreview"/>（玩家预览，落点终止采样）；
    ///   实弹生成与预览共用同一初速向量（调用方保证），本类是唯一积分器。
    /// </summary>
    public static class ThrowTrajectory
    {
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

        /// <summary>
        /// **落点终止采样**（预览重做，投掷行为契约 #9）：半隐式欧拉积分到首次穿地——
        /// 跨越 <paramref name="groundY"/> 的那一步按 y 线性插值出恰好落在地面上的终点，
        /// 因此写入的全部采样点 **y ≥ groundY**（不入地），末点即预测落点。
        /// 步数上限 <paramref name="maxSteps"/> 兜底（默认
        /// <see cref="StandardThrowRules.PreviewMaxSteps"/> = 300 步 = 12 s）；
        /// 到上限仍未穿地（如极限平射出图）返回 <c>false</c> 且不写终点。
        /// </summary>
        /// <returns>写入 <paramref name="buffer"/> 的采样数；out 参数为预测落点（无则 zero）。</returns>
        public static bool TryPredictUntilImpact(
            Vector3 origin, Vector3 initialVelocity, float gravityY,
            Vector3[] buffer, out int count, out Vector3 impact,
            float groundY = LevelGeometry.GroundTopY,
            int maxSteps = StandardThrowRules.PreviewMaxSteps,
            float stepSeconds = LevelGeometry.FrameSeconds)
        {
            Vector3 v = initialVelocity;
            Vector3 p = origin;
            count = 0;
            impact = Vector3.zero;

            int limit = Mathf.Min(maxSteps, buffer.Length);
            for (int i = 0; i < limit; i++)
            {
                // 与 PhysX 相同的半隐式欧拉：先更新速度，再用新速度更新位置。
                v.y += gravityY * stepSeconds;
                Vector3 next = p + v * stepSeconds;

                if (p.y > groundY && next.y <= groundY)
                {
                    float span = p.y - next.y;
                    float t = span > 1e-6f ? (p.y - groundY) / span : 0f;
                    impact = Vector3.Lerp(p, next, t);
                    buffer[count++] = impact;
                    return true;
                }

                buffer[count++] = next;
                p = next;
            }

            return false;
        }
    }
}

