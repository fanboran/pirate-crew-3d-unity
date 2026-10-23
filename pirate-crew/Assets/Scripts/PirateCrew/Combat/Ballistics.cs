using UnityEngine;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 弹体撞地/撞墙的速度积分纯逻辑（标量积分器）。
    /// 投掷初速已随米制重立迁 <c>Battle/StandardThrowRules</c>（弹弓 twang 公式随之退役，
    /// 见 docs/技术/投掷行为契约.md）；本类只保留**弹体落地后的速度积分**
    /// （撞地反弹/摩擦、撞墙反弹），数值为本工程设计值（<b>【提案/待定】</b>）。
    /// 全部为静态纯函数，不依赖 MonoBehaviour / GameObject，可在无头验证台运行。
    ///
    /// 【坐标约定（3D 世界口径）】竞技场为 XZ 水平面、世界 Y 向上、米制。本类自身无坐标
    /// 语义，只有标量槽位：撞地积分的 vx 槽位 = XZ 水平速度模长、vy 槽位 = 世界竖直分量
    /// （反弹 <c>vy *= -bounce</c>：落地 vy&lt;0，乘负后向上）；槽位到世界轴的映射由
    /// <see cref="ProjectileContactRules.IntegrateGroundVelocity"/> /
    /// <see cref="ProjectileContactRules.IntegrateWallVelocity"/> 唯一定义。
    /// 重力不在本类积分（由 <c>WeaponProjectile.FixedUpdate</c> 按标准口径施加）。
    ///
    /// 【与 <c>Battle.ThrowTrajectory</c> 的分工】本类负责「撞地/撞墙后的标量积分」，
    /// 与维度无关（反弹/摩擦系数）。3D 抛物线采样不在这里（<c>ThrowTrajectory.Predict</c> 唯一积分器）。
    /// </summary>
    public static class Ballistics
    {
        /// <summary>撞墙时的水平速度反弹系数：vx *= -0.4。</summary>
        public const float WallBounceScale = -0.4f;

        /// <summary>
        /// 撞地后的速度积分。
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
        /// 撞墙后的速度积分：vx *= -0.4，垂直速度不变。
        /// 【存件·随武器重做接线（原 S2 计划项的预留实现）】产线暂无调用方；
        /// 弹体弹跳/摩擦口径接线时把 Integrate* 接进 WeaponProjectile、bounce/friction 入
        /// ProjectileProfile（见 docs/项目/待办/武器系统重做.md）。接走前勿删。
        /// </summary>
        public static (float vx, float vy) IntegrateWallContact(float vx, float vy)
        {
            return (vx * WallBounceScale, vy);
        }
    }
}
