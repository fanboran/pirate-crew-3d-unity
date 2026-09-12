using System;

namespace PirateCrew.PirateCrew.Combat
{
    /// <summary>
    /// 海鸥（seagull）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【出处】静态逆向文档（docs/参考游戏逆向-海盗军团抢宝藏-静态.md）：
    ///   · §5.2「武器总表」seagull 行（表格第 13 行）——
    ///     「点击选高度，从 x=-300 以 vx=10 向右飞；再点击投弹」；每发「50, 50」；
    ///     「无重力、hitsTiles=false；可连投多颗；飞出 levelWidth*32+275 且无弹在飞即结束」。
    ///   · §5.1 速度上限汇总——weight=0（无重力）。
    ///   · §6.3 AI 专用评分——`Seagull.aiSimulation()`：高度 = 敌方最高 y − 100 − random*100；
    ///     命中敌人（&lt;40px）得 <c>1 - d/40</c>，误伤队友扣 <c>1.5 - d/40</c>；要求 shots.length &gt; 1。
    ///   · §8.4 官方文案——"Click on the screen to choose a path… Then click repeatedly to poop"。
    ///
    /// 【坐标口径】内部用 Flash 像素坐标（x 向右、y 向下），与 §5.2 原文一致；
    ///   3D 胶水层负责把 x → 世界 X、把高度折算到世界 +Y。
    ///
    /// 【玩家交互】"点击选高度 / 再点击投弹"依赖 <c>AimThrowController</c>（黑名单，只读）。
    ///   本类只提供规则本身；交互接线由协调者裁决后另派。
    /// </summary>
    public static class SeagullRules
    {
        /// <summary>入场 x（Flash px，屏幕左侧外）。§5.2 seagull 行。</summary>
        public const float SpawnFlashX = -300f;

        /// <summary>向右飞行速度（Flash px/帧）。§5.2「以 vx=10 向右飞」。</summary>
        public const float FlightSpeed = 10f;

        /// <summary>飞出地图右侧的额外余量（Flash px）。§5.2「levelWidth*32+275」。</summary>
        public const float ExitRightMargin = 275f;

        /// <summary>投弹爆炸 size（§5.2 每发「50, 50」的第一个数）。</summary>
        public const float BombExplosionSize = 50f;

        /// <summary>投弹爆炸最大伤害（§5.2 每发「50, 50」的第二个数）。</summary>
        public const float BombExplosionMaxDamage = 50f;

        /// <summary>AI 命中评分半径（Flash px）：命中敌人得 <c>1 - d/40</c>。§6.3 seagull 行。</summary>
        public const float AiHitScoreRadius = 40f;

        /// <summary>AI 选高相对敌方最高点的基准抬升（Flash px）。§6.3「高度 = 敌方最高 y − 100 − random*100」。</summary>
        public const float AiHeightAboveTargetMin = 100f;

        /// <summary>AI 选高的随机附加量（Flash px）。§6.3。</summary>
        public const float AiHeightAboveTargetRange = 100f;

        /// <summary>AI 要求的最少投弹数：<c>shots.length &gt; 1</c>，即 ≥ 2。§6.3。</summary>
        public const int AiMinimumShots = 2;

        /// <summary>向右飞一帧后的 x。</summary>
        public static float StepX(float flashX)
        {
            return flashX + FlightSpeed;
        }

        /// <summary>结束判定的右侧 x 阈值：<c>levelWidth*32 + 275</c>（Flash px）。</summary>
        public static float ExitFlashX(float levelWidthTiles)
        {
            return levelWidthTiles * 32f + ExitRightMargin;
        }

        /// <summary>是否已飞出右侧阈值。</summary>
        public static bool HasExitedRight(float flashX, float levelWidthTiles)
        {
            return flashX > ExitFlashX(levelWidthTiles);
        }

        /// <summary>
        /// 是否可以结束本次海鸥：已飞出右侧**且**没有投出的弹仍在飞（§5.2）。
        /// </summary>
        public static bool CanFinish(float flashX, float levelWidthTiles, bool anyBombInFlight)
        {
            return HasExitedRight(flashX, levelWidthTiles) && !anyBombInFlight;
        }

        /// <summary>
        /// AI 选高（§6.3）：<c>targetHighestFlashY - 100 - random01*100</c>。
        /// Flash y 向下为正，减去正数即在敌方最高点**上方**。
        /// </summary>
        public static float PickHeight(float targetHighestFlashY, float random01)
        {
            return targetHighestFlashY - AiHeightAboveTargetMin - Clamp01(random01) * AiHeightAboveTargetRange;
        }

        /// <summary>AI 命中敌人得分（§6.3）：<c>1 - d/40</c>；超出 40px 记 0。</summary>
        public static float HitScore(float distancePx)
        {
            if (distancePx >= AiHitScoreRadius)
                return 0f;
            return 1f - distancePx / AiHitScoreRadius;
        }

        /// <summary>AI 误伤队友扣分（§6.3）：<c>1.5 - d/40</c>；超出 40px 记 0。</summary>
        public static float FriendlyFirePenalty(float distancePx)
        {
            if (distancePx >= AiHitScoreRadius)
                return 0f;
            return 1.5f - distancePx / AiHitScoreRadius;
        }

        /// <summary>AI 只保留得分为正的落点（§6.3）。</summary>
        public static bool IsPositiveScore(float score)
        {
            return score > 0f;
        }

        /// <summary>AI 要求的最少投弹数是否满足（§6.3：<c>shots.length &gt; 1</c>）。</summary>
        public static bool HasEnoughShots(int shotCount)
        {
            return shotCount >= AiMinimumShots;
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
