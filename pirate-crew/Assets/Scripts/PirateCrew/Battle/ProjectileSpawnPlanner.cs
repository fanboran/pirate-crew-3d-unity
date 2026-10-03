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
    /// 把一次「确认执行」翻译成弹体生成计划（纯 C#，可无头测试）。
    ///
    /// 【单一弹道】全部武器都走同一条通用抛掷：生成点 = 投掷起点（单位枢轴 + ThrowOriginHeight），
    /// 初速 = 调用方传入的**米制世界向量**（<see cref="StandardThrowRules.LaunchVelocity"/>，
    /// 与角色自抛、轨迹预览同一份值）。一次使用产 1 条弹。
    ///
    /// 【同源约束】本类不做任何弹道换算——「预览 = 实弹」由调用方传同一初速保证。
    /// </summary>
    public static class ProjectileSpawnPlanner
    {
        /// <summary>
        /// 规划一次武器使用产生的弹体。
        /// </summary>
        /// <param name="stats">武器数值（数据锚；物理口径取标准炸弹）。</param>
        /// <param name="originWorld">投掷起点世界坐标（ThrowOrigin 高度）。</param>
        /// <param name="velocity">米制初速向量（与预览/自抛同源）。</param>
        public static IReadOnlyList<ProjectileSpawn> Plan(
            WeaponStats stats,
            Vector3 originWorld,
            Vector3 velocity)
        {
            // stats 为数据锚入参：抛掷初速只由标准投掷模组参数决定，不按武器分叉。
            return new[] { new ProjectileSpawn(originWorld, velocity, kinematic: false) };
        }
    }
}
