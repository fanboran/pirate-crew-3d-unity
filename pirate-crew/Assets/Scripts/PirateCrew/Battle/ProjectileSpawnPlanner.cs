using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 一条弹体生成计划（纯 C#；由 <c>BattleController</c> 实例化）。
    /// </summary>
    public readonly struct ProjectileSpawn
    {
        /// <summary>生成点世界坐标。</summary>
        public readonly Vector3 WorldPosition;

        /// <summary>初始世界速度（放置类为 0）。</summary>
        public readonly Vector3 WorldVelocity;

        /// <summary>true = 放置类（kinematic，不受重力与推力，作为掩体/待触发物）。</summary>
        public readonly bool Kinematic;

        public ProjectileSpawn(Vector3 worldPosition, Vector3 worldVelocity, bool kinematic)
        {
            WorldPosition = worldPosition;
            WorldVelocity = worldVelocity;
            Kinematic = kinematic;
        }
    }

    /// <summary>
    /// 把一次「选武器 → 松手」翻译成若干条弹体生成计划（纯 C#，可无头测试）。
    ///
    /// 【规则】
    ///   · 通用弹弓武器（<see cref="ProjectileProfile.SupportsGenericProjectile"/> 且非放置类）：
    ///     1 条，生成点 = 投掷者位置，初速 = <see cref="LevelGeometry.FlashVelocityToWorld"/>。
    ///   · 放置类（PlaceableCount &gt; 0：woodenCrate 3 / gunpowderBarrel 2）：
    ///     N 条，生成点 = 瞄准落点沿 x 均匀铺开（间距 = 2×碰撞体半宽），初速 0，kinematic=true。
    ///   · 未实现的专用武器（anchor / seagull / tidalWave / voodooDoll / cannon / SweepingFlame）：
    ///     返回空列表——调用方据此走 TODO 分支，不生成错误弹体。
    ///
    /// 【出处】静态逆向文档 §5.1（初速公式）、§5.2（放置数量、AABB）、§3.4（抛自己/用武器二选一）。
    /// </summary>
    public static class ProjectileSpawnPlanner
    {
        static readonly ProjectileSpawn[] Empty = new ProjectileSpawn[0];

        /// <summary>
        /// 规划一次武器使用产生的弹体。
        /// </summary>
        /// <param name="stats">武器数值。</param>
        /// <param name="ownerWorldPosition">投掷者世界坐标（弹弓发射点）。</param>
        /// <param name="aimWorldPosition">瞄准落点世界坐标（放置类铺开中心）。</param>
        /// <param name="vxFlash">弹弓初速 vx（Flash px/帧，由 <c>Ballistics.TwangVelocity</c> 算好）。</param>
        /// <param name="vyFlash">弹弓初速 vy（Flash px/帧）。</param>
        public static IReadOnlyList<ProjectileSpawn> Plan(
            WeaponStats stats,
            Vector3 ownerWorldPosition,
            Vector3 aimWorldPosition,
            float vxFlash,
            float vyFlash)
        {
            if (!ProjectileProfile.SupportsGenericProjectile(stats))
                return Empty;   // TODO：专用武器机制，见 WeaponProjectile 与待办清单

            if (stats.PlaceableCount > 0)
                return PlanPlaceables(stats, aimWorldPosition);

            if (!ProjectileProfile.CanBeSlingLaunched(stats))
                return Empty;   // 双重保护：不该出现的组合

            Vector3 velocity = LevelGeometry.FlashVelocityToWorld(vxFlash, vyFlash);
            return new[] { new ProjectileSpawn(ownerWorldPosition, velocity, kinematic: false) };
        }

        /// <summary>放置类沿 x 均匀铺开（含中心），间距 = 2×水平半宽（世界单位）。</summary>
        static IReadOnlyList<ProjectileSpawn> PlanPlaceables(WeaponStats stats, Vector3 aimWorldPosition)
        {
            int count = stats.PlaceableCount;
            if (count <= 0)
                return Empty;

            float spacing = 2f * LevelGeometry.PixelsToUnits(stats.AabbRadius);
            var result = new ProjectileSpawn[count];
            float center = (count - 1) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Vector3 position = aimWorldPosition + new Vector3((i - center) * spacing, 0f, 0f);
                result[i] = new ProjectileSpawn(position, Vector3.zero, kinematic: true);
            }

            return result;
        }
    }
}
