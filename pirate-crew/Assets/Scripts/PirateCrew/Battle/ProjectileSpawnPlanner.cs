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
    ///     1 条，生成点 = 投掷者位置，初速 = <see cref="LevelGeometry.FlashLaunchVelocityToWorld"/>。
    ///     <b>3D 语义</b>：该初速是三维向量——Flash 的平面初速 (vx, vy) 落到世界 (X, Z)，
    ///     再由固定抬升 <see cref="LevelGeometry.ThrowLift"/>（0.7）在 +Y 上抬出仰角；
    ///     抬升只改方向、不改速度大小，故 twangMax 的限速语义不被破坏。
    ///   · 放置类（PlaceableCount &gt; 0：woodenCrate 3 / gunpowderBarrel 2）：
    ///     N 条，生成点 = 瞄准落点沿 x 均匀铺开（间距 = 2×碰撞体半宽），初速 0，kinematic=true。
    ///   · 特殊武器（<see cref="ProjectileProfile.MechanicFor"/> 非 Generic）：
    ///     各自的生成位置/初速/数量由对应纯规则类（<c>AnchorRules</c> / <c>SeagullRules</c> /
    ///     <c>TidalWaveRules</c> / <c>VoodooDollRules</c> / <c>CannonRules</c> / <c>SweepingFlameRules</c>）
    ///     的常量推出，见 <see cref="PlanSpecial"/>。
    ///
    /// 【出处】静态逆向文档 §5.1（初速公式）、§5.2（17 武器总表：触发条件/位置/速度/放置数量/备注）、
    ///         §3.4（抛自己/用武器二选一）、docs/M2-3D空间模型对齐.md §3（3D 投掷与抬升）。
    ///
    /// 【单位】规则类里的常量都是 Flash px / px·帧⁻¹；本类用
    ///   <see cref="LevelGeometry.PixelsToUnits"/> 折位置、<see cref="LevelGeometry.FlashSpeedScale"/>
    ///   折速度，保证与角色投掷/轨迹预览同一套换算。
    /// </summary>
    public static class ProjectileSpawnPlanner
    {
        static readonly ProjectileSpawn[] Empty = new ProjectileSpawn[0];

        /// <summary>
        /// 规划一次武器使用产生的弹体。
        /// </summary>
        /// <param name="stats">武器数值。</param>
        /// <param name="ownerWorldPosition">投掷者世界坐标（弹弓发射点）。</param>
        /// <param name="aimWorldPosition">瞄准落点世界坐标（放置类铺开中心 / 锚的落点 / 加农炮炮位）。</param>
        /// <param name="vxFlash">弹弓初速 vx（Flash px/帧，由 <c>Ballistics.TwangVelocity</c> 算好）；
        /// 与 <paramref name="vyFlash"/> 一起构成 Flash 的<b>平面</b>初速，映射到世界 XZ，仰角由抬升给出。</param>
        /// <param name="vyFlash">弹弓初速 vy（Flash px/帧）。</param>
        public static IReadOnlyList<ProjectileSpawn> Plan(
            WeaponStats stats,
            Vector3 ownerWorldPosition,
            Vector3 aimWorldPosition,
            float vxFlash,
            float vyFlash)
        {
            ProjectileMechanic mechanic = ProjectileProfile.MechanicFor(stats.Id);
            if (mechanic != ProjectileMechanic.Generic)
                return PlanSpecial(stats, mechanic, ownerWorldPosition, aimWorldPosition, vxFlash, vyFlash);

            if (stats.PlaceableCount > 0)
                return PlanPlaceables(stats, aimWorldPosition);

            if (!ProjectileProfile.CanBeSlingLaunched(stats))
                return Empty;   // 双重保护：不该出现的组合

            // 与角色投掷、轨迹预览共用同一个换算入口（含固定仰角抬升），避免弹体自成一套弹道口径。
            Vector3 velocity = LevelGeometry.FlashLaunchVelocityToWorld(vxFlash, vyFlash);
            return new[] { new ProjectileSpawn(ownerWorldPosition, velocity, kinematic: false) };
        }

        /// <summary>放置类沿 x 均匀铺开（含中心），间距 = 2×水平半宽（世界单位）。</summary>
        static IReadOnlyList<ProjectileSpawn> PlanPlaceables(WeaponStats stats, Vector3 aimWorldPosition)
        {
            int count = stats.PlaceableCount;
            if (count <= 0)
                return Empty;

            float spacing = 2f * LevelGeometry.PixelsToUnits(ProjectileProfile.HorizontalHalfSizePixels(stats));
            var result = new ProjectileSpawn[count];
            float center = (count - 1) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Vector3 position = aimWorldPosition + new Vector3((i - center) * spacing, 0f, 0f);
                result[i] = new ProjectileSpawn(position, Vector3.zero, kinematic: true);
            }

            return result;
        }

        /// <summary>
        /// 特殊武器的生成计划。逐条对应 §5.2 备注列：
        ///   · AnchorDrop：从锚落点正上方 |y=-200|px 处、以 vy=40 等速直落（weight=0，无重力）。
        ///   · SeagullFlight：从 x=-300 的屏幕左侧外飞入（高度取瞄准点上方 100px 作默认，
        ///     玩家点选高度需 UI 交互，见报告）；以 vx=10 向右飞。
        ///   · TidalWaveSweep：从 x=-550 的左侧外、水位高度起扫（vx=20）。
        ///   · VoodooDollTransfer：与弹弓武器同一路径（twangMax=20），先锁定目标后抛出。
        ///   · CannonPlacement：炮位 = 瞄准点，kinematic 常驻，蓄力发射由运行时驱动。
        ///   · SweepingFlameSpread：由 rumBottle 生成 2 个（左右各一），也允许直接规划。
        /// </summary>
        static IReadOnlyList<ProjectileSpawn> PlanSpecial(
            WeaponStats stats,
            ProjectileMechanic mechanic,
            Vector3 ownerWorldPosition,
            Vector3 aimWorldPosition,
            float vxFlash,
            float vyFlash)
        {
            switch (mechanic)
            {
                case ProjectileMechanic.AnchorDrop:
                {
                    // §5.2 anchor：从 y=-200 以 vy=40 直落。FlashSpeedScale 把 px/帧折成世界单位/秒。
                    float spawnHeight = LevelGeometry.PixelsToUnits(-AnchorRules.SpawnFlashY);
                    float fallSpeed = AnchorRules.FallSpeed * LevelGeometry.FlashSpeedScale;
                    Vector3 position = new Vector3(
                        aimWorldPosition.x, aimWorldPosition.y + spawnHeight, aimWorldPosition.z);
                    return new[] { new ProjectileSpawn(position, Vector3.down * fallSpeed, kinematic: false) };
                }

                case ProjectileMechanic.SeagullFlight:
                {
                    // §5.2 seagull：从 x=-300 以 vx=10 向右飞。高度默认瞄准点上方 100px（AI 口径，§6.3）。
                    float spawnX = LevelGeometry.PixelsToUnits(SeagullRules.SpawnFlashX);
                    float height = LevelGeometry.PixelsToUnits(SeagullRules.AiHeightAboveTargetMin);
                    float speed = SeagullRules.FlightSpeed * LevelGeometry.FlashSpeedScale;
                    Vector3 position = new Vector3(spawnX, aimWorldPosition.y + height, aimWorldPosition.z);
                    return new[] { new ProjectileSpawn(position, Vector3.right * speed, kinematic: false) };
                }

                case ProjectileMechanic.TidalWaveSweep:
                {
                    // §5.2 tidalWave：x=-550, y=water.y, vx=20 横扫。
                    float spawnX = LevelGeometry.PixelsToUnits(TidalWaveRules.SpawnFlashX);
                    float speed = TidalWaveRules.SweepSpeed * LevelGeometry.FlashSpeedScale;
                    Vector3 position = new Vector3(
                        spawnX, LevelGeometry.WaterSurfaceY, aimWorldPosition.z);
                    return new[] { new ProjectileSpawn(position, Vector3.right * speed, kinematic: false) };
                }

                case ProjectileMechanic.VoodooDollTransfer:
                {
                    // 与弹弓武器同源（twangMax=20），保证"预览 = 实弹"。
                    Vector3 velocity = LevelGeometry.FlashLaunchVelocityToWorld(vxFlash, vyFlash);
                    return new[] { new ProjectileSpawn(ownerWorldPosition, velocity, kinematic: false) };
                }

                case ProjectileMechanic.CannonPlacement:
                {
                    // §5.2 cannon：placeableWeapon，摆位常驻（limitedToTurn=false）。
                    return new[] { new ProjectileSpawn(aimWorldPosition, Vector3.zero, kinematic: true) };
                }

                case ProjectileMechanic.SweepingFlameSpread:
                {
                    // §5.2 rumBottle 行：落地生成 2 个 SweepingFlame，向左右蔓延。
                    int[] directions = SweepingFlameRules.SpreadDirections;
                    float speed = SweepingFlameRules.SpreadStep * LevelGeometry.FlashSpeedScale;
                    var result = new ProjectileSpawn[directions.Length];
                    for (int i = 0; i < directions.Length; i++)
                    {
                        Vector3 velocity = new Vector3(directions[i] * speed, 0f, 0f);
                        result[i] = new ProjectileSpawn(aimWorldPosition, velocity, kinematic: false);
                    }

                    return result;
                }

                default:
                    return Empty;
            }
        }
    }
}
