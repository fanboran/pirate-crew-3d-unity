using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 标准投掷模组的参数状态（纯数据）。方向角 / 仰角 / 力度三参数，
    /// 服务跳跃（抛自己）与绝大多数抛掷类武器。
    /// </summary>
    public struct ThrowParams
    {
        /// <summary>方向角（度，世界方位：0 = +Z，90 = +X——俯视顺时针，Unity yaw 同约定）。</summary>
        public float YawDegrees;

        /// <summary>仰角（度，地平面向上为正）。</summary>
        public float ElevationDegrees;

        /// <summary>力度（0–1 归一化；初速 = 力度 × <see cref="StandardThrowRules.MaxLaunchSpeed"/>）。</summary>
        public float Power;

        public ThrowParams(float yawDegrees, float elevationDegrees, float power)
        {
            YawDegrees = yawDegrees;
            ElevationDegrees = elevationDegrees;
            Power = power;
        }
    }

    /// <summary>
    /// 标准投掷模组的纯规则层（无头可测）。
    ///
    /// 【米制重立（创始人裁决 2026-10-03）】投掷初速与重力为**米制常量**，本类是唯一真源：
    /// <see cref="MaxLaunchSpeed"/> / <see cref="LaunchGravityY"/> / <see cref="ThrowOriginHeight"/>
    /// 全工程禁止第二处字面量（Physics.gravity、弹体重力、预览积分全部引用这里）。
    /// 旧 Flash 换算口径（0.25 系数 / twangMax / FlashSpeedScale / 固定 ThrowLift）已退役，
    /// 爆炸域的 px 换算（LevelGeometry.PixelsToUnits 等）属武器数值域，保留至武器系统重做。
    ///
    /// 【数值推导（全部【提案/待定】，实机手感验收后定案）】
    ///   · 旧口径全力度初速 = 20 px/帧 × 1.5625 = 31.25 m/s、重力 −39.06 m/s²（v²/g ≈ 25 m 射程包络）；
    ///   · 新口径取整 MaxLaunchSpeed = 30、LaunchGravityY = −30（v²/g = 30 m，包络 +20%），
    ///     全力 35° 仰角射程 ≈ 28 m > 世界图连通判据 MaxJumpGap = 13 m，跳跃间隙覆盖安全；
    ///   · 默认仰角 35° 延续旧 ThrowLift=0.7 的等效仰角（atan(0.7) ≈ 35°），开局手感连续；
    ///   · 物理步率 25 Hz（LevelGeometry.FrameSeconds = 0.04）保留——回合/像素节奏，非 Flash 专属。
    ///
    /// 【规格出处】docs/技术/投掷行为契约.md #3/#8/#9（改数值先改契约）；键位见交互操作契约 §B。
    /// </summary>
    public static class StandardThrowRules
    {
        // ------------------------------------------------------------------
        // 参数步进（速率与夹取，投掷行为契约 #3）
        // ------------------------------------------------------------------

        /// <summary>方向角速率（度/秒，提案）：A 逆时针 / D 顺时针，全周无界。</summary>
        public const float YawRateDegreesPerSecond = 90f;

        /// <summary>仰角速率（度/秒，提案）：W 升 / S 降。</summary>
        public const float ElevationRateDegreesPerSecond = 45f;

        /// <summary>仰角下限（度，提案）。</summary>
        public const float ElevationMinDegrees = 10f;

        /// <summary>仰角上限（度，提案）。</summary>
        public const float ElevationMaxDegrees = 85f;

        /// <summary>默认仰角（度，提案）：延续旧口径 atan(0.7) ≈ 35° 的等效仰角。</summary>
        public const float DefaultElevationDegrees = 35f;

        /// <summary>力度速率（比例/秒，提案）：Space 升 / Shift 降。</summary>
        public const float PowerRatePerSecond = 0.45f;

        /// <summary>力度下限（提案）：误触保护——最小初速 = 0.10 × 30 = 3 m/s，不产生零向量发射。</summary>
        public const float MinPower = 0.10f;

        /// <summary>力度上限（恒 1）。</summary>
        public const float MaxPower = 1f;

        /// <summary>默认力度（提案）：延续旧炮台口径 0.6。</summary>
        public const float DefaultPower = 0.60f;

        // ------------------------------------------------------------------
        // 米制常量（全工程唯一真源，投掷行为契约不变量 4）
        // ------------------------------------------------------------------

        /// <summary>最大初速（m/s，提案）：满力度初速。</summary>
        public const float MaxLaunchSpeed = 30f;

        /// <summary>投掷重力 Y（m/s²，提案，向下为负）。角色/弹体/预览/Physics.gravity 同值。</summary>
        public const float LaunchGravityY = -30f;

        /// <summary>投掷重力向量 (0, g, 0)。</summary>
        public static Vector3 LaunchGravity => new Vector3(0f, LaunchGravityY, 0f);

        /// <summary>投掷起点离单位枢轴的抬升（m，提案，约投掷手高度）。预览与实弹同用。</summary>
        public const float ThrowOriginHeight = 1.0f;

        /// <summary>预览积分的兜底步数上限（300 步 × 0.04 s = 12 s 飞行；见 ThrowTrajectory）。</summary>
        public const int PreviewMaxSteps = 300;

        // ------------------------------------------------------------------
        // 参数推进
        // ------------------------------------------------------------------

        /// <summary>
        /// 一帧的参数推进。方向输入取 -1/0/+1（A=-1 逆时针、D=+1 顺时针；W=+1 升仰角；
        /// Space=+1 升力度），dt 为帧时长。方向角不夹取（全周），仰角/力度夹到契约范围。
        /// </summary>
        public static ThrowParams Advance(ThrowParams p, int yawInput, int elevationInput, int powerInput, float dt)
        {
            p.YawDegrees += yawInput * YawRateDegreesPerSecond * dt;
            p.ElevationDegrees = Mathf.Clamp(
                p.ElevationDegrees + elevationInput * ElevationRateDegreesPerSecond * dt,
                ElevationMinDegrees, ElevationMaxDegrees);
            p.Power = Mathf.Clamp(
                p.Power + powerInput * PowerRatePerSecond * dt,
                MinPower, MaxPower);
            return p;
        }

        /// <summary>
        /// 进入操作时的初始参数：仰角/力度取默认值，方向角继承调用方给的世界方位
        /// （编排层传"玩家正对方向"——交互操作契约 §B10 的起步规则）。
        /// </summary>
        public static ThrowParams Initial(float yawDegrees)
        {
            return new ThrowParams(yawDegrees, DefaultElevationDegrees, DefaultPower);
        }

        // ------------------------------------------------------------------
        // 初速合成
        // ------------------------------------------------------------------

        /// <summary>初速大小 = 力度 × <see cref="MaxLaunchSpeed"/>（与方向无关）。</summary>
        public static float LaunchSpeed(ThrowParams p)
        {
            return Mathf.Clamp(p.Power, MinPower, MaxPower) * MaxLaunchSpeed;
        }

        /// <summary>
        /// 初速方向（单位向量）：方位角绕 +Y（俯视顺时针为正，Unity yaw 同约定）、
        /// 仰角向地平面上方。yaw=0/elev=0 → +Z；yaw=90/elev=0 → +X；elev=90 → +Y。
        /// 【无头可测】用纯三角构造，不走 <c>Quaternion.Euler</c>（原生 ECall，
        /// 脱离 Unity 运行时必抛 SecurityException，见 external/harness 边界）。
        /// </summary>
        public static Vector3 LaunchDirection(float yawDegrees, float elevationDegrees)
        {
            float yawRad = yawDegrees * Mathf.Deg2Rad;
            float elevRad = elevationDegrees * Mathf.Deg2Rad;
            float cosE = Mathf.Cos(elevRad);
            // 俯视（沿 -Y 看下去，+X 朝右、+Z 朝屏幕上方）：+Z 顺时针转 yaw=90° 到 +X，
            // 故水平分量 x = sin(yaw)、z = cos(yaw)；仰角抬竖直分量。
            return new Vector3(Mathf.Sin(yawRad) * cosE, Mathf.Sin(elevRad), Mathf.Cos(yawRad) * cosE);
        }

        /// <summary>完整初速向量（投掷行为契约 #8；预览与实弹的唯一入口）。</summary>
        public static Vector3 LaunchVelocity(ThrowParams p)
        {
            return LaunchDirection(p.YawDegrees, p.ElevationDegrees) * LaunchSpeed(p);
        }

        /// <summary>投掷起点世界坐标：单位枢轴 + <see cref="ThrowOriginHeight"/>×UP。</summary>
        public static Vector3 ThrowOrigin(Vector3 unitPivotPosition)
        {
            return unitPivotPosition + Vector3.up * ThrowOriginHeight;
        }
    }
}
