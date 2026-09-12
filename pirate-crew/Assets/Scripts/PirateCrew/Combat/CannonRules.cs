using System;

namespace PirateCrew.PirateCrew.Combat
{
    /// <summary>
    /// 加农炮（cannon）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【出处】静态逆向文档（docs/参考游戏逆向-海盗军团抢宝藏-静态.md）：
    ///   · §5.2「武器总表」cannon 行（表格第 16 行）——
    ///     「放置 + 拖尾部 pin 调角度/蓄力，松手发射」；经 cannonball（100, 50）；
    ///     「placeableWeapon；fireStrength 达 30 才发射（≤4 不发射）；AI 走 aiFireTime = 25 延时后发射」；
    ///     limitedToTurn=false（摆位常驻）。
    ///   · §5.1 速度上限汇总——「cannon 发射速度 = fireStrength（≤30）」。
    ///   · §6.3 cannon 评分——`Cannon.randomThrows`：随机角度 0–360 + 随机拖拽距离决定炮位，
    ///     再模拟炮弹以**速度 30** 沿随机角度飞出；`aiPerform` 摆位、设角度、`aiFireTime = 25` 后发射。
    ///   · §8.4 官方文案——"Drag the cannon into position within the circle… drag the pin at the back to turn
    ///     the cannon. release it to fire"。
    ///
    /// 【数值映射决策】
    ///   · **蓄力 = 0.25 × 拖拽距离，上限 30**：与 §5.1 弹弓公式同系数（0.25），只是原表未给 cannon 的
    ///     twangMax，故上限取 §5.1「cannon 发射速度 = fireStrength（≤30）」的 30。满蓄力拖拽 = 30/0.25 = 120px。
    ///     （**提案/待定**：文档只说"拖尾部 pin 蓄力"，未给蓄力公式；此处复用弹弓系数。）
    ///   · **发射阈值**：文档「fireStrength 达 30 才发射（≤4 不发射）」存在 5–29 的空档，语义不完整。
    ///     本类取可证伪的最简口径：<see cref="ShouldFire"/> = 蓄力 ≥ 30 才发射；
    ///     <see cref="IsBelowNoFireThreshold"/> = 蓄力 ≤ 4 时明确不发射（覆盖文档后半句）。
    ///     5–29 视为"可蓄力但不发射"，**提案/待定**，若评审另有口径改这两个纯函数即可。
    ///   · **角度**：文档未给拖拽→角度的映射。取与 §5.1 弹弓一致的"后拉 → 反向发射"：
    ///     发射方向 = <c>atan2(-dy, -dx)</c>（**提案/待定**）。
    /// </summary>
    public static class CannonRules
    {
        /// <summary>最大蓄力 / 最大发射速度（Flash px/帧）。§5.1「fireStrength（≤30）」。</summary>
        public const float MaxFireStrength = 30f;

        /// <summary>明确不发射的蓄力上限（含）。§5.2「≤4 不发射」。</summary>
        public const float NoFireThreshold = 4f;

        /// <summary>发射所需的最低蓄力。§5.2「fireStrength 达 30 才发射」。</summary>
        public const float FireThreshold = MaxFireStrength;

        /// <summary>AI 摆位后的发射延时（帧）。§5.2 / §6.3「aiFireTime = 25」。</summary>
        public const int AiFireDelayFrames = 25;

        /// <summary>炮弹爆炸 size（§5.2 cannon 行「经 cannonball（100, 50）」）。</summary>
        public const float CannonballExplosionSize = 100f;

        /// <summary>炮弹爆炸最大伤害（§5.2）。</summary>
        public const float CannonballExplosionMaxDamage = 50f;

        /// <summary>蓄力系数：0.25 × 拖拽距离（复用 §5.1 弹弓系数）。</summary>
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

        /// <summary>是否落在文档明确的"不发射"区间（≤ 4）。</summary>
        public static bool IsBelowNoFireThreshold(float fireStrength)
        {
            return fireStrength <= NoFireThreshold;
        }

        /// <summary>
        /// 拖拽向量 → 发射角度（弧度）。方向取反，与 §5.1 弹弓"后拉 = 向前射"一致（**提案/待定**）。
        /// </summary>
        public static float AimAngleRadians(float dragDxPx, float dragDyPx)
        {
            // 加 0f 把 -0.0f 归一成 +0.0f：否则 dragDyPx == 0 且 dragDxPx < 0 时
            // atan2(-0.0, -1) 返回 -π，与 +π 等价但会让角度断言/表现抖动。
            return (float)Math.Atan2(-dragDyPx + 0f, -dragDxPx);
        }

        /// <summary>
        /// 发射初速（Flash px/帧）：沿 <paramref name="angleRadians"/> 以 fireStrength 为大小。
        /// 返回 (vx, vy)。§5.1「cannon 发射速度 = fireStrength」。
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

        /// <summary>AI 摆位后是否已到发射时刻（≥ 25 帧）。§6.3。</summary>
        public static bool ShouldAiFire(int elapsedFrames)
        {
            return elapsedFrames >= AiFireDelayFrames;
        }
    }
}
