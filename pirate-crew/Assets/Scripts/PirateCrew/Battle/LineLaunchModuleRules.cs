using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 直线发射模组的参数状态（纯数据）：方向角 + 蓄力。
    /// </summary>
    public struct LineLaunchParams
    {
        /// <summary>方向角（度，世界方位，与标准投掷同约定：0 = +Z、90 = +X）。</summary>
        public float YawDegrees;

        /// <summary>蓄力（0–1 归一化；初速 = 蓄力 × <see cref="LineLaunchModuleRules.PlaceholderLaunchSpeed"/>）。</summary>
        public float Power;

        public LineLaunchParams(float yawDegrees, float power)
        {
            YawDegrees = yawDegrees;
            Power = power;
        }
    }

    /// <summary>
    /// 直线发射模组的纯规则骨架（无头可测）。
    ///
    /// 【骨架边界】参数推进复用标准投掷的键位口径（A/D 转向、Space/Shift 蓄力，
    /// 速率与夹取取同档常数）作为占位；正式交互（拖拽炮尾同时调角度与蓄力、
    /// 发射阈值 fireStrength）是加农炮格的逐格定案项，TODO 留桩。
    /// 发射方向恒水平（原作加农炮走直线）；是否加仰角属逐格定案。
    /// 常数为骨架期占位工程值（直接固定常数口径）。
    /// </summary>
    public static class LineLaunchModuleRules
    {
        /// <summary>方向角速率（度/秒，占位：沿用标准投掷档）。TODO(逐格定案)：拖拽炮尾手势映射。</summary>
        public const float YawRateDegreesPerSecond = 90f;

        /// <summary>蓄力速率（比例/秒，占位：沿用标准投掷档）。</summary>
        public const float PowerRatePerSecond = 0.45f;

        /// <summary>蓄力下限（误触保护，与标准投掷同语义）。</summary>
        public const float MinPower = 0.10f;

        /// <summary>蓄力上限（恒 1）。</summary>
        public const float MaxPower = 1f;

        /// <summary>默认蓄力（占位：延续旧炮台口径 0.6）。</summary>
        public const float DefaultPower = 0.60f;

        /// <summary>
        /// 占位直线初速（m/s，满蓄力）。TODO(逐格定案)：蓄力 → 初速映射与发射阈值
        /// （原作 fireStrength 达 30 才发射、≤4 不发射的语义）。
        /// </summary>
        public const float PlaceholderLaunchSpeed = 20f;

        /// <summary>
        /// 进入操作时的初始参数：方向角继承调用方给的世界方位（编排层传「玩家正对方向」，
        /// 与标准投掷起步规则同口径），蓄力取默认。
        /// </summary>
        public static LineLaunchParams Initial(float yawDegrees)
        {
            return new LineLaunchParams(yawDegrees, DefaultPower);
        }

        /// <summary>
        /// 一帧的参数推进（占位键位口径）：方向输入 -1/0/+1（A=-1/D=+1）、蓄力输入
        /// -1/0/+1（Shift=-1/Space=+1）。方向角不夹取（全周），蓄力夹到 [Min, Max]。
        /// </summary>
        public static LineLaunchParams Advance(LineLaunchParams p, int yawInput, int powerInput, float dt)
        {
            p.YawDegrees += yawInput * YawRateDegreesPerSecond * dt;
            p.Power = Mathf.Clamp(
                p.Power + powerInput * PowerRatePerSecond * dt, MinPower, MaxPower);
            return p;
        }

        /// <summary>
        /// 发射方向（单位向量，恒水平）：yaw 约定与
        /// <see cref="StandardThrowRules.LaunchDirection"/> 相同（仰角恒 0）。
        /// </summary>
        public static Vector3 LaunchDirection(float yawDegrees)
        {
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
        }

        /// <summary>发射初速大小 = 蓄力 × <see cref="PlaceholderLaunchSpeed"/>。</summary>
        public static float LaunchSpeed(LineLaunchParams p)
        {
            return Mathf.Clamp(p.Power, MinPower, MaxPower) * PlaceholderLaunchSpeed;
        }

        /// <summary>发射初速向量（占位口径；正式弹药走 cannonball 格的实弹链）。</summary>
        public static Vector3 LaunchVelocity(LineLaunchParams p)
        {
            return LaunchDirection(p.YawDegrees) * LaunchSpeed(p);
        }
    }
}
