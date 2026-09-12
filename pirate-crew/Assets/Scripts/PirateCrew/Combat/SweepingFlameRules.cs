using System;

namespace PirateCrew.PirateCrew.Combat
{
    /// <summary>
    /// 蔓延火焰（SweepingFlame）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【出处】静态逆向文档（docs/参考游戏逆向-海盗军团抢宝藏-静态.md）：
    ///   · §5.2「武器总表」rumBottle 行（表格第 8 行）——「落在 FLOOR 时额外生成 **2 个**
    ///     `SweepingFlame`（向左右蔓延的火）」。
    ///   · §5.2 表格末行「（火）SweepingFlame」——「命中条件 dist &lt; 8px，附加随机击退；
    ///     沿地面每 8px 蔓延一段（下方有瓦片且上方无瓦片）」，每段 **30 点**。
    ///   · §6.1 非 AI 随机（原文 line 725）与 §6.3（原文 line 636）——
    ///     击退 <c>(rand-0.5)*8</c>（水平）/ <c>-(rand*2+6)</c>（垂直，向上为负 = 上抛）。
    ///
    /// 【坐标口径】内部用 Flash 像素（x 横向、y 向下为正）。火焰沿地面 X 向左右蔓延，
    ///   3D 胶水层把 x → 世界 X、把 y → 由地面高度决定。
    /// </summary>
    public static class SweepingFlameRules
    {
        /// <summary>rumBottle 落地生成的火焰数量。§5.2「生成 2 个」。</summary>
        public const int SpawnCount = 2;

        /// <summary>每段蔓延的位移（Flash px）。§5.2「沿地面每 8px 蔓延一段」。</summary>
        public const float SpreadStep = 8f;

        /// <summary>命中距离阈值（Flash px）。§5.2「命中条件 dist &lt; 8px」。</summary>
        public const float HitDistance = 8f;

        /// <summary>每段伤害。§5.2「每段 30 点」。</summary>
        public const float DamagePerSegment = 30f;

        /// <summary>水平击退系数：<c>(rand-0.5)*8</c>。§6.1/§6.3。</summary>
        public const float KnockbackHorizontalScale = 8f;

        /// <summary>垂直击退基准：<c>-(rand*2+6)</c> 的常数项 6（Flash y 向上为负 = 上抛）。§6.3。</summary>
        public const float KnockbackVerticalBase = 6f;

        /// <summary>垂直击退随机系数：<c>rand*2</c> 的系数 2。§6.3。</summary>
        public const float KnockbackVerticalRandomScale = 2f;

        /// <summary>蔓延方向：+1（右）/ -1（左），对应 §5.2「向左右蔓延」的两个火焰。</summary>
        public static readonly int[] SpreadDirections = { 1, -1 };

        /// <summary>下一段的 x（沿 <paramref name="direction"/> 每次 8px）。</summary>
        public static float NextSegmentX(float flashX, int direction)
        {
            return flashX + (direction >= 0 ? SpreadStep : -SpreadStep);
        }

        /// <summary>
        /// 是否可以继续往该方向蔓延：落地判据「下方有瓦片**且**上方无瓦片」（§5.2）。
        /// </summary>
        public static bool CanSpread(bool hasTileBelow, bool hasTileAbove)
        {
            return hasTileBelow && !hasTileAbove;
        }

        /// <summary>是否命中（dist &lt; 8px，严格小于）。§5.2。</summary>
        public static bool ShouldHit(float distancePx)
        {
            return distancePx < HitDistance;
        }

        /// <summary>
        /// 随机击退：<c>vx = (rand-0.5)*8</c>、<c>vy = -(rand*2+6)</c>（Flash y 向上为负，即上抛）。
        /// <paramref name="random01"/> 取 [0,1) 的随机数。§6.1/§6.3。
        /// </summary>
        public static void Knockback(float random01, out float vx, out float vy)
        {
            float r = Clamp01(random01);
            vx = (r - 0.5f) * KnockbackHorizontalScale;
            vy = -(r * KnockbackVerticalRandomScale + KnockbackVerticalBase);
        }

        /// <summary>在最大蔓延距离内能铺出的段数（含起点）。</summary>
        public static int SegmentCount(float maxSpreadPx)
        {
            if (maxSpreadPx <= 0f)
                return 1;
            return 1 + (int)Math.Floor(maxSpreadPx / SpreadStep);
        }

        static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }
}
