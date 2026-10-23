using System.Collections.Generic;
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
        // 单元素计划缓存：ProjectileSpawn 是 readonly struct、消费方只经 IReadOnlyList 遍历，
        // 复用同一实例消除「一次使用一个数组」的稳态分配（B14）。
        static readonly ProjectileSpawn[] SinglePlan =
            { new ProjectileSpawn(Vector3.zero, Vector3.zero, kinematic: false) };

        /// <summary>
        /// 规划一次武器使用产生的弹体（单一弹体：一次使用恒产 1 条）。
        /// </summary>
        /// <param name="originWorld">投掷起点世界坐标（ThrowOrigin 高度）。</param>
        /// <param name="velocity">米制初速向量（与预览/自抛同源，原样透传）。</param>
        public static IReadOnlyList<ProjectileSpawn> Plan(Vector3 originWorld, Vector3 velocity)
        {
            // 不做任何弹道换算：「预览 = 实弹」由调用方传同一初速保证（同源约束）。
            SinglePlan[0] = new ProjectileSpawn(originWorld, velocity, kinematic: false);
            return SinglePlan;
        }

        /// <summary>
        /// 兼容旧调用形的转发重载。<paramref name="stats"/> 无消费者（抛掷不按武器分叉）——
        /// 迁移调用点后删除（B14 转交：现役调用点 <c>BattleController.SpawnWeaponProjectiles</c>）。
        /// </summary>
        public static IReadOnlyList<ProjectileSpawn> Plan(
            WeaponStats stats, Vector3 originWorld, Vector3 velocity)
        {
            return Plan(originWorld, velocity);
        }
    }
}
