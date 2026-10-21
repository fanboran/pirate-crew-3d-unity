using System.Collections.Generic;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 单武器的撞地积分参数覆写（<see cref="Ballistics"/> 域口径）：
    /// 摩擦 = 每次撞地对水平速度模长的递减量；弹跳 = 撞地垂直速度反向系数。
    /// </summary>
    public readonly struct ProjectileContactOverride
    {
        /// <summary>撞地摩擦（水平速度模长递减量，&gt;= 0）。</summary>
        public readonly float Friction;

        /// <summary>撞地弹跳系数（vy *= -bounce）。</summary>
        public readonly float Bounce;

        public ProjectileContactOverride(float friction, float bounce)
        {
            Friction = friction;
            Bounce = bounce;
        }
    }

    /// <summary>
    /// 每武器撞地/撞墙积分参数的覆写登记表（纯 C#，武器系统地基 S2「弹跳/摩擦参数化」的数据面）。
    ///
    /// 【为什么不直接读 WeaponStats.Friction/Bounce】目录里这两列是旧表【提案/待定】遗留值，
    /// 直接消费会让各武器弹道立刻分叉、破坏「单一弹体」现役口径（点哪把都是标准小炸弹）。
    /// 逐格定案后的个性参数在<b>本表</b>登记（<see cref="Register"/>）；未登记武器回退全局默认
    /// （<see cref="BalanceConfig.Defaults.DefaultFriction"/> / <see cref="BalanceConfig.Defaults.DefaultBounce"/>），
    /// 行为与登记前逐位一致（回归测试钉住）。
    ///
    /// 【登记即启用】有覆写登记的武器，弹体碰撞改走 Ballistics 手动积分
    /// （<see cref="ProjectileContactRules.HasManualIntegration"/>；接线在 WeaponProjectile 的碰撞回调）；
    /// 未登记武器维持 PhysX 现状碰撞。两个域的切换随逐格定案发生，地基只提供通道。
    /// </summary>
    public static class ProjectileContactOverrides
    {
        // 现役空表：17 武器全部未定案个性弹道参数 → 全部回退全局默认。
        static readonly Dictionary<WeaponId, ProjectileContactOverride> _byId =
            new Dictionary<WeaponId, ProjectileContactOverride>();

        /// <summary>已登记的覆写数（0 = 全武器走全局默认与 PhysX 现状）。</summary>
        public static int Count => _byId.Count;

        /// <summary>登记一件武器的撞地积分覆写（逐格定案时调用；同 id 重复登记后写覆盖）。</summary>
        public static void Register(WeaponId id, ProjectileContactOverride overwrite)
        {
            _byId[id] = overwrite;
        }

        /// <summary>查覆写（未登记返回 false）。</summary>
        public static bool TryGet(WeaponId id, out ProjectileContactOverride overwrite)
        {
            return _byId.TryGetValue(id, out overwrite);
        }

        /// <summary>清空全部登记（测试隔离用；运行期无调用方）。</summary>
        public static void Clear()
        {
            _byId.Clear();
        }
    }

    /// <summary>
    /// 撞地/撞墙速度积分的 3D 封装（纯函数，无头可测）：把 <see cref="Ballistics"/> 的
    /// 平面标量积分接到世界速度向量上——水平 XZ 共用摩擦/墙反弹口径，竖直分量单独走弹跳口径。
    /// 积分数学单源在 Ballistics，本类只做世界向量的拆装，不另写一套公式。
    /// 参数来源：<see cref="ProjectileContactOverrides"/>（无覆写回退全局默认）。
    /// </summary>
    public static class ProjectileContactRules
    {
        /// <summary>无覆写时的摩擦回退（单源 = <see cref="BalanceConfig.Defaults.DefaultFriction"/>）。</summary>
        public static float DefaultFriction => BalanceConfig.Defaults.DefaultFriction;

        /// <summary>无覆写时的弹跳回退（单源 = <see cref="BalanceConfig.Defaults.DefaultBounce"/>）。</summary>
        public static float DefaultBounce => BalanceConfig.Defaults.DefaultBounce;

        /// <summary>该武器是否启用 Ballistics 手动积分（= 覆写表里有登记；未登记走 PhysX 现状）。</summary>
        public static bool HasManualIntegration(WeaponId id)
        {
            return ProjectileContactOverrides.TryGet(id, out _);
        }

        /// <summary>取该武器的撞地积分参数（无覆写回退全局默认）。</summary>
        public static void Resolve(WeaponId id, out float friction, out float bounce)
        {
            if (ProjectileContactOverrides.TryGet(id, out ProjectileContactOverride overwrite))
            {
                friction = overwrite.Friction;
                bounce = overwrite.Bounce;
            }
            else
            {
                friction = DefaultFriction;
                bounce = DefaultBounce;
            }
        }

        /// <summary>
        /// 撞地积分（3D）：竖直分量按弹跳反向（vy *= -bounce，含负 bounce 时向下），水平 XZ
        /// 模长按摩擦递减（下限 0、方向保持）。水平模长恒为非负标量，正好落进
        /// <see cref="Ballistics.IntegrateGroundContact"/> 的 vx 槽位（其摩擦只减不反向的口径因此保持）。
        /// </summary>
        public static Vector3 IntegrateGroundVelocity(Vector3 worldVelocity, float friction, float bounce)
        {
            Vector2 flat = new Vector2(worldVelocity.x, worldVelocity.z);
            float horizontalMagnitude = flat.magnitude;
            (float reduced, float bounced) = Ballistics.IntegrateGroundContact(
                horizontalMagnitude, worldVelocity.y, friction, bounce);
            Vector2 newFlat = horizontalMagnitude > 0f
                ? flat * (reduced / horizontalMagnitude)
                : Vector2.zero;
            return new Vector3(newFlat.x, bounced, newFlat.y);
        }

        /// <summary>
        /// 撞墙积分（3D）：水平速度整体按墙反弹系数反向（× <see cref="Ballistics.WallBounceScale"/>
        /// = -0.4，即沿原水平方向弹回），竖直分量不变。X 分量与竖直分量经
        /// <see cref="Ballistics.IntegrateWallContact"/> 单源处理；Z 是 Flash 平面没有的
        /// 第二水平轴，按同一系数反向。
        /// </summary>
        public static Vector3 IntegrateWallVelocity(Vector3 worldVelocity)
        {
            (float reversedX, float keptY) = Ballistics.IntegrateWallContact(
                worldVelocity.x, worldVelocity.y);
            float scale = worldVelocity.x != 0f
                ? reversedX / worldVelocity.x
                : Ballistics.WallBounceScale;
            return new Vector3(reversedX, keptY, worldVelocity.z * scale);
        }
    }
}
