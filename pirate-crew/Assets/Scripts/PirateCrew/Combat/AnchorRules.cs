using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 船锚（anchor）专用规则（纯 C#，不引用 MonoBehaviour / GameObject，便于无头测试）。
    ///
    /// 【出处】静态逆向文档（docs/参考游戏逆向-海盗军团抢宝藏-静态.md）：
    ///   · §5.2「武器总表」anchor 行（表格第 12 行）——
    ///     AABB「l/r 48, top 96, bottom 0」；触发「点击放置，从 y=-200 以 vy=40 直落」；
    ///     伤害「60 点固定伤害（非爆炸）」；命中条件「|x-anchorX| &lt; 48 且 anchorY-64 &lt; y &lt; anchorY」；
    ///     「落地 hold 30 帧后淡出 10 帧」。
    ///   · §5.1「投掷机制」速度上限汇总——「anchor 恒 vy=40 从 y=-200 下落」。
    ///
    /// 【坐标口径】本类内部一律用 **Flash 像素坐标**（x 向右、y 向下为正，25fps 逐帧）。
    ///   3D 胶水层（<c>WeaponProjectile</c>）负责把 Flash y 折算成世界 +Y：
    ///   1 瓦片 = 32px，世界高度与 Flash y **反向**。本类不引入任何 Unity 类型，保证可无头断言。
    ///
    /// 【落地判定】原文档未给出"地面 y"的显式条件（它依赖关卡瓦片）。
    ///   M2 取可定义的最简口径：锚心 y 触达地面格（<paramref name="groundFlashY"/>）即视为落地。
    /// </summary>
    public static class AnchorRules
    {
        /// <summary>投放起点 y（Flash px）；负值在地面上方。§5.2 anchor 行。</summary>
        public const float SpawnFlashY = -200f;

        /// <summary>恒定下落速度（Flash px/帧）。§5.1「anchor 恒 vy=40」。</summary>
        public const float FallSpeed = 40f;

        /// <summary>水平命中半宽（Flash px）。§5.2 AABB「l/r 48」。</summary>
        public const float HorizontalHalfExtent = 48f;

        /// <summary>AABB 上伸（Flash px）。§5.2「top 96」。</summary>
        public const float TopExtent = 96f;

        /// <summary>AABB 下伸（Flash px）。§5.2「bottom 0」。</summary>
        public const float BottomExtent = 0f;

        /// <summary>命中判据里锚心上方的那一段（Flash px）：<c>anchorY-64 &lt; y</c>。§5.2 anchor 行。</summary>
        public const float HitBandAboveAnchor = 64f;

        /// <summary>固定伤害（非爆炸）。§5.2 anchor 行「60 点固定伤害」。</summary>
        public const float FixedDamage = 60f;

        /// <summary>落地后保持帧数。§5.2「落地 hold 30 帧」。</summary>
        public const int LandHoldFrames = 30;

        /// <summary>淡出帧数。§5.2「淡出 10 帧」。</summary>
        public const int FadeFrames = 10;

        /// <summary>落地后到销毁的总帧数（hold 30 + fade 10）。</summary>
        public const int TotalFramesAfterLanding = LandHoldFrames + FadeFrames;

        /// <summary>下落一帧后的 y（Flash y 向下为正，故 +FallSpeed）。</summary>
        public static float StepY(float flashY)
        {
            return flashY + FallSpeed;
        }

        /// <summary>下落若干帧后的 y。</summary>
        public static float StepY(float flashY, int frames)
        {
            return flashY + FallSpeed * frames;
        }

        /// <summary>
        /// 命中判据（§5.2 原文逐字）：
        /// <c>|x-anchorX| &lt; 48</c> 且 <c>anchorY-64 &lt; y &lt; anchorY</c>。
        /// 注意 Flash y 向下为正：<c>anchorY-64</c> 在锚心**上方**，故目标须落在锚心上方 64px 内。
        /// </summary>
        public static bool ShouldHit(float anchorX, float anchorY, float targetX, float targetY)
        {
            return Math.Abs(targetX - anchorX) < HorizontalHalfExtent
                   && targetY > anchorY - HitBandAboveAnchor
                   && targetY < anchorY;
        }

        /// <summary>是否已落地：锚心 y 达到或越过地面格 y（Flash y 越大越低）。</summary>
        public static bool HasLanded(float flashY, float groundFlashY)
        {
            return flashY >= groundFlashY;
        }

        /// <summary>是否仍存活（落地计时未走完）。</summary>
        public static bool IsAlive(int landedFrames)
        {
            return landedFrames < TotalFramesAfterLanding;
        }

        /// <summary>是否已过 hold 阶段、进入淡出。</summary>
        public static bool IsFading(int landedFrames)
        {
            return landedFrames >= LandHoldFrames && landedFrames < TotalFramesAfterLanding;
        }

        /// <summary>是否应销毁（hold + fade 走完）。</summary>
        public static bool ShouldDestroy(int landedFrames)
        {
            return landedFrames >= TotalFramesAfterLanding;
        }

        /// <summary>
        /// 落地后的不透明度（1 = 不透明，0 = 全透明）。hold 期间恒 1，淡出 10 帧线性降到 0。
        /// 供表现层控制 <c>Renderer.material.color.a</c>（本类不碰渲染）。
        /// </summary>
        public static float AlphaAfterLanding(int landedFrames)
        {
            if (landedFrames <= LandHoldFrames)
                return 1f;
            if (landedFrames >= TotalFramesAfterLanding)
                return 0f;

            int faded = landedFrames - LandHoldFrames;
            return 1f - (float)faded / FadeFrames;
        }
    }
}
