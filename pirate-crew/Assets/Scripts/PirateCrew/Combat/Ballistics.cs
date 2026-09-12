using UnityEngine;

namespace PirateCrew.PirateCrew.Combat
{
    /// <summary>
    /// 投掷弹道纯逻辑。
    /// 对应逆向文档 §5.1（弹弓 twang 公式、满力拖拽距离、15 段虚线预测）与 §5.4（物理积分：撞地/撞墙）。
    /// 全部为静态纯函数，不依赖 MonoBehaviour / GameObject，可在无头验证台运行。
    /// 坐标约定与 Flash 原版一致（y 轴向下，重力每帧 +weight）。
    /// </summary>
    public static class Ballistics
    {
        /// <summary>原版弹弓的固定力度系数：初速 = 0.25 × 拖拽距离。</summary>
        public const float DefaultForceScale = 0.25f;

        /// <summary>原版轨迹预测的虚线段数（§5.1）。</summary>
        public const int DefaultPredictionSteps = 15;

        /// <summary>撞墙时的水平速度反弹系数（§5.4）：vx *= -0.4。</summary>
        public const float WallBounceScale = -0.4f;

        /// <summary>
        /// 弹弓松开瞬间的初速（§5.1）。
        /// vx = dx * -forceScale、vy = dy * -forceScale（方向与光标偏移相反：向后拉 = 向前射）；
        /// 若 vx²+vy² > twangMax²，则按同比例缩放使模长恰为 twangMax（限速）。
        /// </summary>
        /// <param name="dx">光标相对物体的水平偏移（px）</param>
        /// <param name="dy">光标相对物体的垂直偏移（px）</param>
        /// <param name="twangMax">该物体的最大初速（原版 twangMaxForce；角色 20，香蕉/跳伞炸弹等 30）</param>
        /// <param name="forceScale">力度系数，原版固定 0.25</param>
        public static (float vx, float vy) TwangVelocity(
            float dx, float dy, float twangMax, float forceScale = DefaultForceScale)
        {
            float vx = dx * -forceScale;
            float vy = dy * -forceScale;

            float speedSq = vx * vx + vy * vy;
            if (speedSq > twangMax * twangMax)
            {
                float speed = Mathf.Sqrt(speedSq);
                // 拖拽距离 > 满力距离时才可能进这里；speed 必 > 0，但仍防御性判断。
                if (speed > 0f)
                {
                    float scale = twangMax / speed;
                    vx *= scale;
                    vy *= scale;
                }
            }

            return (vx, vy);
        }

        /// <summary>
        /// 达到最大初速所需的拖拽距离（§5.1）：twangMax / forceScale。
        /// 默认 20 / 0.25 = 80px；twangMax=30 的武器为 120px。
        /// </summary>
        public static float FullForceDragDistance(float twangMax, float forceScale = DefaultForceScale)
        {
            return twangMax / forceScale;
        }

        /// <summary>
        /// 弹弓虚线轨迹预测（§5.1 drawTwangLine）。
        /// 从 (startX,startY) 以 (vx,vy) 出发，每步先 vy += weight（重力）再积分位置，
        /// 返回 steps 个采样点（默认 15，即原版 15 段虚线）。
        /// </summary>
        public static (float x, float y)[] PredictTrajectory(
            float startX, float startY, float vx, float vy,
            float weight = 1f, int steps = DefaultPredictionSteps)
        {
            if (steps < 0)
            {
                steps = 0;
            }

            var points = new (float x, float y)[steps];

            float x = startX;
            float y = startY;
            float vyRunning = vy;

            for (int i = 0; i < steps; i++)
            {
                vyRunning += weight;   // 先加重力
                x += vx;
                y += vyRunning;
                points[i] = (x, y);
            }

            return points;
        }

        /// <summary>
        /// 撞地后的速度积分（§5.4）。
        /// 垂直：vy *= -bounce（默认 bounce=0.2 → 反弹向上）；
        /// 水平：|vx| 减去 friction，且下限为 0（不会因摩擦反向）。
        /// </summary>
        public static (float vx, float vy) IntegrateGroundContact(
            float vx, float vy, float friction, float bounce)
        {
            float newVy = vy * -bounce;

            float magnitude = Mathf.Abs(vx) - friction;
            if (magnitude < 0f)
            {
                magnitude = 0f;
            }

            float newVx = vx >= 0f ? magnitude : -magnitude;
            return (newVx, newVy);
        }

        /// <summary>
        /// 撞墙后的速度积分（§5.4）：vx *= -0.4，垂直速度不变。
        /// </summary>
        public static (float vx, float vy) IntegrateWallContact(float vx, float vy)
        {
            return (vx * WallBounceScale, vy);
        }
    }
}
