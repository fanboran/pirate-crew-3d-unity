using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 落地翻滚 / 落水死亡演出的纯 C# 规则层（忠实转写 Flash 逆向，docs/M4-大海域世界化.md §3.1）。
    ///
    /// 【是什么】原版没有翻滚动画状态机，翻滚是三段涌现逻辑：空中与地面同源的恒转、
    ///   落地弹跳、接地时滚动角逐接触帧减半；落水死亡另有一段旋转下沉演出。
    ///   本类把它们的 <b>全部数值换算</b> 与 <b>状态推进</b> 收进一个无 Unity 依赖（仅用 Mathf 的
    ///   托管数学）的静态类，供 <see cref="PirateBase"/> 的胶水层逐步驱动，并可在无头验证台断言。
    ///
    /// 【换算口径】Flash 速度以 px/帧 计，Unity 以 u/s 计，比例
    ///   <see cref="LevelGeometry.FlashSpeedScale"/> = 1.5625 u/s per px/帧；一帧 = 0.04s。
    ///
    /// 【角度语义】滚动角是非负标量（累计翻滚量，度）；"往哪个方向滚"完全由旋转轴承载
    ///   （<see cref="RollAxis"/>：up × 水平速度 → 前滚翻）。原版 <c>rotation += vx*3</c> 的符号语义
    ///   在 3D 中等价于轴的反向（vx 取负 = 轴取反），因此规则层只维护大小、不带符号。
    ///
    /// 【实现口径（M4 §3.1 提案）】刚体旋转保持冻结，翻滚只作用在视觉子变换上——
    ///   不破坏胶囊对位 / AI / 瞄准 / 描边链路。
    /// </summary>
    public static class RollRules
    {
        // ------------------------------------------------------------------
        // 换算表（每条都与 Flash 出处一一核对，见各成员注释）
        // ------------------------------------------------------------------

        /// <summary>
        /// 空中/滚动转速系数（度/秒 per 世界单位/秒）。
        /// 【出处】每帧 <c>rotation += vx*3</c>（Character.as:151-154，空中+地面同源）→
        /// vx = 1 px/帧 时 3°/帧 × 25fps = 75°/s；÷ FlashSpeedScale(1.5625 u/s per px/帧) = <b>48</b>。
        /// </summary>
        public const float SpinDegreesPerSecondPerUnitSpeed = 48f;

        /// <summary>
        /// 地面线性减速（u/s²）。
        /// 【出处】落地接触 <c>|vx| -= 2</c> 逐接触帧（Solid.as:269，friction=2，Character.as:50）→
        /// 2 px/帧 ÷ 0.04s × 1.5625 = <b>78.125</b>。
        /// </summary>
        public const float GroundDecelerationUnitsPerSecond2 = 78.125f;

        /// <summary>接地阻尼的步长（秒）= 原版一个接触帧（LevelGeometry.FrameSeconds = 0.04）。</summary>
        public const float GroundDampStepSeconds = LevelGeometry.FrameSeconds;

        /// <summary>
        /// 接地阻尼系数：每接触步滚动角 ×0.5。
        /// 【出处】落地接触 <c>rotation *= 0.5</c>（Character.as:685-697）。
        /// </summary>
        public const float GroundAngleDampFactor = 0.5f;

        /// <summary>滚动角归零阈值（度）：归一后绝对值小于该值直接归零（Character.as:685-697 的 &lt;1° 归零）。</summary>
        public const float AngleSnapDegrees = 1f;

        /// <summary>
        /// 落地弹跳系数：接触法线朝上且 vy&lt;0 时，反弹向上速度 = |vy| × 该值。
        /// 【出处】<c>vy *= -0.2</c>（Solid.as:263-276；bounce=0.2，Solid.as:13）。
        /// </summary>
        public const float LandBounceFactor = 0.2f;

        /// <summary>
        /// 最低反弹回跳速度（u/s，<b>提案/待定</b>）。低于它的反弹直接归零——Flash 数值空间里
        /// ×0.2 的几何衰减几帧内自然贴地，Unity 物理里保留一个下限可避免数值噪声让刚体永远无法入睡。
        /// </summary>
        public const float MinBounceUpSpeed = 0.05f;

        /// <summary>
        /// 落水死亡演出转速系数（度/秒 per 世界单位/秒）。
        /// 【出处】落水 <c>rotation += (vx+vy)*4</c>（Character.as:164-180）→ 4°/帧 per px/帧
        /// × 25fps = 100°/s；÷ 1.5625 = <b>64</b>。
        /// </summary>
        public const float WaterSpinDegreesPerSecondPerUnitSpeed = 64f;

        /// <summary>
        /// 落水演出速度衰减系数：每 0.04s 全速度 ×0.8。
        /// 【出处】落水 <c>v×0.8</c>（Character.as:164-180）。
        /// </summary>
        public const float WaterVelocityDampPerStep = 0.8f;

        /// <summary>
        /// 落水下沉的最低速度（Flash 口径 px/帧）。
        /// 【出处】落水 vy 钳 ≥1.5 下沉（Character.as:164-180）。
        /// </summary>
        public const float WaterSinkPixelsPerFrame = 1.5f;

        /// <summary>落水下沉的最低速度（u/s，向下）= 1.5 × 1.5625 = 2.34375。</summary>
        public static float WaterSinkSpeedUnitsPerSecond => WaterSinkPixelsPerFrame * LevelGeometry.FlashSpeedScale;

        // ------------------------------------------------------------------
        // 角度工具
        // ------------------------------------------------------------------

        /// <summary>
        /// 把任意角度归一到 <c>(-180, 180]</c>（等价原版落地阻尼前的归一，Character.as:685-697）。
        /// 多圈累计角先取模，再折进半开区间；恰为 ±180 时归到 +180。
        /// </summary>
        public static float NormalizeAngle180(float degrees)
        {
            float a = Mathf.Repeat(degrees + 180f, 360f) - 180f;
            if (a <= -180f)
                a = 180f;
            return a;
        }

        // ------------------------------------------------------------------
        // 空中 / 滚动恒转
        // ------------------------------------------------------------------

        /// <summary>转速（度/秒）= 48 × 水平速度（u/s）。</summary>
        public static float SpinDegreesPerSecond(float horizontalSpeedUnitsPerSecond)
        {
            return SpinDegreesPerSecondPerUnitSpeed * Mathf.Abs(horizontalSpeedUnitsPerSecond);
        }

        /// <summary>推进一步（dt 秒）后的滚动角。空中与接地同源（原版恒转不分空中地面）。</summary>
        public static float AdvanceRollAngle(float angleDegrees, float horizontalSpeedUnitsPerSecond, float dt)
        {
            if (dt <= 0f)
                return angleDegrees;
            return angleDegrees + SpinDegreesPerSecond(horizontalSpeedUnitsPerSecond) * dt;
        }

        /// <summary>
        /// 前滚翻的旋转轴 = up × 水平速度方向（归一）。速度退化（近零）返回 <see cref="Vector3.zero"/>。
        /// 验证：向 +X 移动时轴为 -Z，正角绕 -Z 即"头顶向 +X 倒"= 前滚。
        /// </summary>
        public static Vector3 RollAxis(Vector3 velocity)
        {
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude < 1e-12f)
                return Vector3.zero;
            return Vector3.Cross(Vector3.up, flat).normalized;
        }

        // ------------------------------------------------------------------
        // 接地：滚动角阻尼 + 线性摩擦 + 落地弹跳
        // ------------------------------------------------------------------

        /// <summary>
        /// 接地阻尼：每累计 <see cref="GroundDampStepSeconds"/> 秒，滚动角 ×<see cref="GroundAngleDampFactor"/>
        /// （阻尼前先归一到 (-180,180]），绝对值 &lt; <see cref="AngleSnapDegrees"/> 直接归零。
        ///
        /// <paramref name="pendingStepSeconds"/> 是跨帧的步进余数（调用方持久保存的累计接地时间），
        /// 按 ref 累进消费——与原版"逐接触帧 ×0.5"逐步等价，且允许胶水层用任意 dt 步进。
        /// </summary>
        /// <returns>阻尼后的滚动角。</returns>
        public static float DampGroundAngle(
            float angleDegrees, float groundedSeconds, ref float pendingStepSeconds)
        {
            if (groundedSeconds <= 0f)
                return angleDegrees;

            pendingStepSeconds += groundedSeconds;
            float angle = NormalizeAngle180(angleDegrees);

            while (pendingStepSeconds >= GroundDampStepSeconds)
            {
                pendingStepSeconds -= GroundDampStepSeconds;
                angle *= GroundAngleDampFactor;
                if (Mathf.Abs(angle) < AngleSnapDegrees)
                {
                    angle = 0f;
                    break;
                }
            }

            return angle;
        }

        /// <summary>
        /// 地面线性摩擦后的水平速度大小：<c>max(0, speed − 78.125 × dt)</c>。
        /// 方向保持不变（胶水层按大小比例缩放水平速度向量）。
        /// </summary>
        public static float GroundSpeedAfterFriction(float horizontalSpeedUnitsPerSecond, float dt)
        {
            if (dt <= 0f || horizontalSpeedUnitsPerSecond <= 0f)
                return Mathf.Max(0f, horizontalSpeedUnitsPerSecond);
            return Mathf.Max(0f, horizontalSpeedUnitsPerSecond - GroundDecelerationUnitsPerSecond2 * dt);
        }

        /// <summary>
        /// 落地反弹的向上速度（u/s）：|下落速度| × 0.2；低于 <see cref="MinBounceUpSpeed"/> 归零。
        /// 传入的 <paramref name="downwardSpeed"/> 是下落速度的<b>大小</b>（≥0；负值按 0 处理）。
        /// </summary>
        public static float LandBounceUpSpeed(float downwardSpeed)
        {
            float up = Mathf.Max(0f, downwardSpeed) * LandBounceFactor;
            return up >= MinBounceUpSpeed ? up : 0f;
        }

        // ------------------------------------------------------------------
        // 落水死亡演出
        // ------------------------------------------------------------------

        /// <summary>落水演出转速（度/秒）= 64 × 合速度（u/s）。</summary>
        public static float WaterSpinDegreesPerSecond(float totalSpeedUnitsPerSecond)
        {
            return WaterSpinDegreesPerSecondPerUnitSpeed * Mathf.Abs(totalSpeedUnitsPerSecond);
        }

        /// <summary>
        /// 落水演出的速度衰减系数（本帧等效值）：每 0.04s ×0.8 → <c>0.8^(dt/0.04)</c>。
        /// 允许胶水层以任意 dt 连续步进而保持每 0.04s 衰减 20% 的速率。
        /// </summary>
        public static float WaterDampFactor(float dt)
        {
            if (dt <= 0f)
                return 1f;
            return Mathf.Pow(WaterVelocityDampPerStep, dt / GroundDampStepSeconds);
        }
    }
}
