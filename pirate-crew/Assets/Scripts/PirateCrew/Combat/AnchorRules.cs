using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 船锚（anchor）专用规则（纯 C#，不引用 MonoBehaviour / GameObject，便于无头测试）。
    ///
    /// 【口径】投放点、恒定下落速度、AABB 半宽/上伸/下伸、命中带宽、固定伤害、
    ///   落地 hold / fade 帧数均为本工程设计值
    ///   （<b>【提案/待定】</b>：当前无已裁决文档为其取值背书）。
    ///
    /// 【坐标口径】本类内部一律用 **Flash 像素坐标**（x 向右、y 向下为正，25fps 逐帧）。
    ///   3D 胶水层（<c>WeaponProjectile</c>）负责把 Flash y 折算成世界 +Y：
    ///   1 瓦片 = 32px，世界高度与 Flash y **反向**。本类不引入任何 Unity 类型，保证可无头断言。
    ///
    /// 【落地判定】取可定义的最简口径：锚心 y 触达地面格（<paramref name="groundFlashY"/>）即视为落地。
    /// </summary>
    public static class AnchorRules
    {
        /// <summary>投放起点 y（Flash px）；负值在地面上方。</summary>
        public const float SpawnFlashY = -200f;

        /// <summary>恒定下落速度（Flash px/帧）。</summary>
        public const float FallSpeed = 40f;

        /// <summary>水平命中半宽（Flash px）。</summary>
        public const float HorizontalHalfExtent = 48f;

        /// <summary>AABB 上伸（Flash px）。</summary>
        public const float TopExtent = 96f;

        /// <summary>AABB 下伸（Flash px）。</summary>
        public const float BottomExtent = 0f;

        /// <summary>命中判据里锚心上方的那一段（Flash px）：<c>anchorY-64 &lt; y</c>。</summary>
        public const float HitBandAboveAnchor = 64f;

        /// <summary>固定伤害（非爆炸）。</summary>
        public const float FixedDamage = 60f;

        /// <summary>落地后保持帧数。</summary>
        public const int LandHoldFrames = 30;

        /// <summary>淡出帧数。</summary>
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
        /// 命中判据：
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

        /// <summary>
        /// 是否已过 hold 阶段、进入淡出。判据统一为 <c>landedFrames &gt; LandHoldFrames</c>：
        /// hold 满帧数才开始淡，第 30 帧仍是不透明满帧
        /// （与 <see cref="AlphaAfterLanding"/> 的 <c>≤ LandHoldFrames → 1</c> 同界）——
        /// 旧判据 <c>≥</c> 让 IsFading(30)==true 而 AlphaAfterLanding(30)==1，两函数边界自相矛盾，
        /// 且淡出只走过 0.9~0.1、末帧 0 永不可见即被销毁。行为微变属修边界 bug。
        /// </summary>
        public static bool IsFading(int landedFrames)
        {
            return landedFrames > LandHoldFrames && landedFrames < TotalFramesAfterLanding;
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
