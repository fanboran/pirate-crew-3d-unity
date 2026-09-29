using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 海鸥（seagull）专用规则（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【口径】入场点、飞行速度、飞出余量、投弹爆炸 size/伤害、AI 评分半径与选高参数
    ///   均为本工程设计值（<b>【提案/待定】</b>：当前无已裁决文档为其取值背书）。
    ///
    /// 【坐标口径】内部用 Flash 像素坐标（x 向右、y 向下为正）。
    ///   3D 胶水层负责把 x → 世界 X、把高度折算到世界 +Y。
    ///
    /// 【玩家交互】"点击选高度 / 再点击投弹"依赖 <c>AimThrowController</c>（黑名单，只读）。
    ///   本类只提供规则本身；交互接线由协调者裁决后另派。
    /// </summary>
    public static class SeagullRules
    {
        /// <summary>入场 x（Flash px，屏幕左侧外）。</summary>
        public const float SpawnFlashX = -300f;

        /// <summary>向右飞行速度（Flash px/帧）。</summary>
        public const float FlightSpeed = 10f;

        /// <summary>飞出地图右侧的额外余量（Flash px）：<c>levelWidth*32 + 275</c>。</summary>
        public const float ExitRightMargin = 275f;

        /// <summary>投弹爆炸 size。</summary>
        public const float BombExplosionSize = 50f;

        /// <summary>投弹爆炸最大伤害。</summary>
        public const float BombExplosionMaxDamage = 50f;

        /// <summary>AI 命中评分半径（Flash px）：命中敌人得 <c>1 - d/40</c>。</summary>
        public const float AiHitScoreRadius = 40f;

        /// <summary>AI 选高相对敌方最高点的基准抬升（Flash px）：<c>目标最高 y − 100 − random*100</c>。</summary>
        public const float AiHeightAboveTargetMin = 100f;

        /// <summary>AI 选高的随机附加量（Flash px）。</summary>
        public const float AiHeightAboveTargetRange = 100f;

        /// <summary>AI 要求的最少投弹数：<c>shots.length &gt; 1</c>，即 ≥ 2。</summary>
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
        /// 是否可以结束本次海鸥：已飞出右侧**且**没有投出的弹仍在飞。
        /// </summary>
        public static bool CanFinish(float flashX, float levelWidthTiles, bool anyBombInFlight)
        {
            return HasExitedRight(flashX, levelWidthTiles) && !anyBombInFlight;
        }

        /// <summary>
        /// AI 选高：<c>targetHighestFlashY - 100 - random01*100</c>。
        /// Flash y 向下为正，减去正数即在敌方最高点**上方**。
        /// </summary>
        public static float PickHeight(float targetHighestFlashY, float random01)
        {
            return targetHighestFlashY - AiHeightAboveTargetMin - Clamp01(random01) * AiHeightAboveTargetRange;
        }

        /// <summary>AI 命中敌人得分：<c>1 - d/40</c>；超出 40px 记 0。</summary>
        public static float HitScore(float distancePx)
        {
            if (distancePx >= AiHitScoreRadius)
                return 0f;
            return 1f - distancePx / AiHitScoreRadius;
        }

        /// <summary>AI 误伤队友扣分：<c>1.5 - d/40</c>；超出 40px 记 0。</summary>
        public static float FriendlyFirePenalty(float distancePx)
        {
            if (distancePx >= AiHitScoreRadius)
                return 0f;
            return 1.5f - distancePx / AiHitScoreRadius;
        }

        /// <summary>AI 只保留得分为正的落点。</summary>
        public static bool IsPositiveScore(float score)
        {
            return score > 0f;
        }

        /// <summary>AI 要求的最少投弹数是否满足（<c>shots.length &gt; 1</c>）。</summary>
        public static bool HasEnoughShots(int shotCount)
        {
            return shotCount >= AiMinimumShots;
        }

        /// <summary>
        /// [0,1] 截断（Combat 域的唯一单源，<see cref="SweepingFlameRules"/> 亦引用此份——
        /// 逐字双份实现收敛一处，同程序集不新建文件）。
        /// </summary>
        internal static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }
}
