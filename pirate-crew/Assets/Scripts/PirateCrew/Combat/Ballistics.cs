using UnityEngine;

namespace PirateCrew.PirateCrew.Combat
{
    /// <summary>
    /// 投掷弹道纯逻辑（Flash 2D 平面口径）。
    /// 对应逆向文档 §5.1（弹弓 twang 公式、满力拖拽距离）与 §5.4（撞地/撞墙的速度积分）。
    /// 全部为静态纯函数，不依赖 MonoBehaviour / GameObject，可在无头验证台运行。
    /// 坐标约定与 Flash 原版一致（y 轴向下，重力每帧 +weight）。
    ///
    /// 【与 <c>Battle.ThrowTrajectory</c> 的分工】本类只负责「弹弓初速」与「撞地/撞墙后的速度积分」，
    /// 这些是与维度无关的 Flash 标量公式（拖拽距离、反弹/摩擦系数）。
    /// <b>3D 抛物线采样不在这里</b>：XZ 竞技场下的逐步积分由 <c>Battle.ThrowTrajectory.Predict</c> 承担
    /// （预览、实弹、AI 共用同一份半隐式欧拉）。原平面预测方法 <c>PredictTrajectory</c> 随 2D 模型废弃后
    /// 已无调用方（仅自身测试引用），按「死代码不保留证据链、Git 历史即存档」删除；
    /// 若需回看其 2D 口径实现，见本文件历史版本。
    /// </summary>
    public static class Ballistics
    {
        /// <summary>原版弹弓的固定力度系数：初速 = 0.25 × 拖拽距离。</summary>
        public const float DefaultForceScale = 0.25f;

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
