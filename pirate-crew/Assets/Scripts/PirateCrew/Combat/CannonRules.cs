using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 加农炮（cannon）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【口径】最大蓄力/发射速度、发射阈值、AI 发射延时、炮弹爆炸 size/伤害、蓄力系数
    ///   均为本工程设计值（<b>【提案/待定】</b>：当前无已裁决文档为其取值背书）。
    ///
    /// 【数值映射决策（提案/待定）】
    ///   · **蓄力 = 0.25 × 拖拽距离，上限 30**：蓄力系数复用弹弓的 0.25，上限取 30。
    ///     满蓄力拖拽 = 30/0.25 = 120px。
    ///   · **发射阈值**：取可证伪的最简口径：<see cref="ShouldFire"/> = 蓄力 ≥ 30 才发射；
    ///     <see cref="IsBelowNoFireThreshold"/> = 蓄力 ≤ 4 时明确不发射。
    ///     5–29 视为"可蓄力但不发射"，**提案/待定**，若评审另有口径改这两个纯函数即可。
    ///   · **角度**：取"后拉 → 反向发射"：发射方向 = <c>atan2(-dy, -dx)</c>（**提案/待定**）。
    /// </summary>
    public static class CannonRules
    {
        /// <summary>最大蓄力 / 最大发射速度（Flash px/帧）。</summary>
        public const float MaxFireStrength = 30f;

        /// <summary>明确不发射的蓄力上限（含）。</summary>
        public const float NoFireThreshold = 4f;

        /// <summary>发射所需的最低蓄力。</summary>
        public const float FireThreshold = MaxFireStrength;

        /// <summary>AI 摆位后的发射延时（帧）。</summary>
        public const int AiFireDelayFrames = 25;

        /// <summary>炮弹爆炸 size。</summary>
        public const float CannonballExplosionSize = 100f;

        /// <summary>炮弹爆炸最大伤害。</summary>
        public const float CannonballExplosionMaxDamage = 50f;

        /// <summary>蓄力系数：0.25 × 拖拽距离（复用弹弓力度系数）。</summary>
        public const float ChargePerDragPx = 0.25f;

        /// <summary>满蓄力所需拖拽距离（Flash px）：30 / 0.25 = 120。</summary>
        public static float FullChargeDragPx => FireThreshold / ChargePerDragPx;

        /// <summary>拖拽距离 → 蓄力（夹在 [0, 30]）。</summary>
        public static float ChargeFromDrag(float dragPx)
        {
            if (dragPx <= 0f)
                return 0f;
            float charge = dragPx * ChargePerDragPx;
            return charge > MaxFireStrength ? MaxFireStrength : charge;
        }

        /// <summary>蓄力是否达到发射线（≥ 30）。</summary>
        public static bool ShouldFire(float fireStrength)
        {
            return fireStrength >= FireThreshold;
        }

        /// <summary>是否落在明确的"不发射"区间（≤ 4）。</summary>
        public static bool IsBelowNoFireThreshold(float fireStrength)
        {
            return fireStrength <= NoFireThreshold;
        }

        /// <summary>
        /// 拖拽向量 → 发射角度（弧度）。方向取反（后拉 = 向前射）（**提案/待定**）。
        /// </summary>
        public static float AimAngleRadians(float dragDxPx, float dragDyPx)
        {
            // 加 0f 把 -0.0f 归一成 +0.0f：否则 dragDyPx == 0 且 dragDxPx < 0 时
            // atan2(-0.0, -1) 返回 -π，与 +π 等价但会让角度断言/表现抖动。
            return (float)Math.Atan2(-dragDyPx + 0f, -dragDxPx);
        }

        /// <summary>
        /// 发射初速（Flash px/帧）：沿 <paramref name="angleRadians"/> 以 fireStrength 为大小。
        /// 返回 (vx, vy)。
        /// </summary>
        public static void LaunchVelocity(float fireStrength, float angleRadians, out float vx, out float vy)
        {
            float strength = fireStrength;
            if (strength < 0f)
                strength = 0f;
            if (strength > MaxFireStrength)
                strength = MaxFireStrength;

            vx = (float)Math.Cos(angleRadians) * strength;
            vy = (float)Math.Sin(angleRadians) * strength;
        }

        /// <summary>AI 摆位后是否已到发射时刻（≥ 25 帧）。</summary>
        public static bool ShouldAiFire(int elapsedFrames)
        {
            return elapsedFrames >= AiFireDelayFrames;
        }
    }
}
