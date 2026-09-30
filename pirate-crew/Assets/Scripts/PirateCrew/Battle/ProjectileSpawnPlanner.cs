using System.Collections.Generic;
using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 一条弹体生成计划（纯 C#；由 <c>BattleController</c> 实例化）。
    /// </summary>
    public readonly struct ProjectileSpawn
    {
        /// <summary>生成点世界坐标。</summary>
        public readonly Vector3 WorldPosition;

        /// <summary>初始世界速度。</summary>
        public readonly Vector3 WorldVelocity;

        /// <summary>true = 常驻静置弹体（kinematic，不受重力与推力）。单一弹体路径恒 false。</summary>
        public readonly bool Kinematic;

        public ProjectileSpawn(Vector3 worldPosition, Vector3 worldVelocity, bool kinematic)
        {
            WorldPosition = worldPosition;
            WorldVelocity = worldVelocity;
            Kinematic = kinematic;
        }
    }

    /// <summary>
    /// 把一次「选武器 → 松手」翻译成弹体生成计划（纯 C#，可无头测试）。
    ///
    /// 【单一弹道】全部武器都走同一条通用抛掷：生成点 = 投掷者位置，初速 =
    /// <see cref="LevelGeometry.FlashLaunchVelocityToWorld(float, float, float)"/>（重量分流既有通道，
    /// 标准炸弹 weight &gt; 0 → 固定抬升仰角）。一次使用产 1 条弹。
    ///
    /// 【同源约束】与角色自抛、轨迹预览共用同一换算入口
    /// （<c>LevelGeometry.ThrowVelocityForWeight</c> 的按重量分流），保证预览 = 实弹。
    /// </summary>
    public static class ProjectileSpawnPlanner
    {
        /// <summary>
        /// 规划一次武器使用产生的弹体。
        /// </summary>
        /// <param name="stats">武器数值（数据锚；物理口径取标准炸弹）。</param>
        /// <param name="ownerWorldPosition">投掷者世界坐标（弹弓发射点）。</param>
        /// <param name="aimWorldPosition">瞄准落点世界坐标（抛掷路径不消费，保留入参以稳定调用方签名）。</param>
        /// <param name="vxFlash">弹弓初速 vx（Flash px/帧，由 <c>Ballistics.TwangVelocity</c> 算好）。</param>
        /// <param name="vyFlash">弹弓初速 vy（Flash px/帧）。</param>
        public static IReadOnlyList<ProjectileSpawn> Plan(
            WeaponStats stats,
            Vector3 ownerWorldPosition,
            Vector3 aimWorldPosition,
            float vxFlash,
            float vyFlash)
        {
            // stats 为数据锚入参：抛掷初速与落点只由标准口径与弹弓结果决定，不按武器分叉
            //（aimWorldPosition 同理保留，稳定调用方签名）。

            // 与角色投掷、轨迹预览共用同一个换算入口（按 weight 分流的抬升，
            // 见 LevelGeometry.ThrowVelocityForWeight）：标准炸弹 weight>0，
            // 走固定仰角抬升的抛物线弹道。
            Vector3 velocity = LevelGeometry.FlashLaunchVelocityToWorld(
                vxFlash, vyFlash, StandardBombRules.Weight);
            return new[] { new ProjectileSpawn(ownerWorldPosition, velocity, kinematic: false) };
        }
    }
}
