using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 武器触发/引爆方式（§5.2 触发条件列）。
    /// </summary>
    public enum WeaponTrigger
    {
        /// <summary>无触发条件（如 woodenCrate 不爆炸）。</summary>
        None = 0,

        /// <summary>接触即爆（cannonball / cherryBomb / parachuteBomb / rumBottle / piecesOfEight）。</summary>
        Contact = 1,

        /// <summary>静止时引爆（dynamite：vx==0 且 |vy|&lt;0.2）。</summary>
        AtRest = 2,

        /// <summary>玩家点击引爆（banana / tidalWave）。</summary>
        Click = 3,

        /// <summary>有人进入 60px 且移动 → 起 60 帧引信（mine）。</summary>
        ProximityFuse = 4,

        /// <summary>被任意 Explosion 命中触发（gunpowderBarrel，可连锁）。</summary>
        BlastContact = 5
    }

    /// <summary>
    /// 某一帧的触发判定输入（不引用 MonoBehaviour，便于纯逻辑测试）。
    /// </summary>
    public readonly struct TriggerContext
    {
        /// <summary>本帧是否与瓦片/箱体/敌人接触。</summary>
        public readonly bool Contact;

        /// <summary>本帧玩家是否点击（banana 二次点击引爆等）。</summary>
        public readonly bool Clicked;

        /// <summary>本帧是否被爆炸命中。</summary>
        public readonly bool BlastHit;

        public readonly float VelocityX;
        public readonly float VelocityY;

        /// <summary>引信是否已点燃（mine 进入 60px 触发后置 true）。</summary>
        public readonly bool FuseArmed;

        /// <summary>引信剩余帧数；&lt;=0 表示到点。</summary>
        public readonly int FuseFramesRemaining;

        public TriggerContext(
            bool contact, bool clicked, bool blastHit,
            float velocityX, float velocityY,
            bool fuseArmed = false, int fuseFramesRemaining = 0)
        {
            Contact = contact;
            Clicked = clicked;
            BlastHit = blastHit;
            VelocityX = velocityX;
            VelocityY = velocityY;
            FuseArmed = fuseArmed;
            FuseFramesRemaining = fuseFramesRemaining;
        }
    }

    /// <summary>
    /// 武器触发判定与地雷引信。
    /// 对应逆向文档 §5.2 的「触发/引爆条件」列，以及 mine 的
    /// 「有角色在 60px 内且在移动 → 引信 60 帧」「beepTimes [0,15,30,38,45,49,53,55,57,59]」。
    /// 全部为静态纯函数，不依赖 MonoBehaviour / GameObject。
    /// </summary>
    public static class WeaponTriggerRules
    {
        /// <summary>静止判定里垂直速度的阈值：|vy| &lt; 0.2（原版 dynamite）。</summary>
        public const float AtRestVyEpsilon = 0.2f;

        /// <summary>地雷感应半径（px）。</summary>
        public const float ProximityRadius = 60f;

        /// <summary>地雷引信长度（帧，25fps ≈ 2.4s）。</summary>
        public const int MineFuseFrames = 60;

        /// <summary>
        /// 地雷引信期间的蜂鸣帧序号（相对点燃时刻的经过帧数），原版 Mine.beepTimes。
        /// 最后一响 59，第 60 帧爆炸。
        /// </summary>
        public static readonly int[] MineBeepTimes = { 0, 15, 30, 38, 45, 49, 53, 55, 57, 59 };

        /// <summary>
        /// 是否处于静止（原版 dynamite：<c>vx == 0 &amp;&amp; |vy| &lt; 0.2</c>）。
        /// 水平要求严格为 0；垂直用阈值。
        /// </summary>
        public static bool IsAtRest(float vx, float vy, float restVyEpsilon = AtRestVyEpsilon)
        {
            return vx == 0f && Math.Abs(vy) < restVyEpsilon;
        }

        /// <summary>
        /// 本帧是否应引爆。各触发方式对应 §5.2：
        ///   Contact      → 接触即爆
        ///   AtRest       → 静止即爆
        ///   Click        → 点击引爆
        ///   BlastContact → 被爆炸命中触发
        ///   ProximityFuse→ 引信点燃且剩余帧数到点（起爆瞬间由 ShouldStartFuse 判定）
        ///   None         → 永不
        /// </summary>
        public static bool ShouldDetonate(WeaponTrigger trigger, TriggerContext context)
        {
            switch (trigger)
            {
                case WeaponTrigger.Contact:
                    return context.Contact;
                case WeaponTrigger.AtRest:
                    return IsAtRest(context.VelocityX, context.VelocityY);
                case WeaponTrigger.Click:
                    return context.Clicked;
                case WeaponTrigger.BlastContact:
                    return context.BlastHit;
                case WeaponTrigger.ProximityFuse:
                    return context.FuseArmed && context.FuseFramesRemaining <= 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 是否应点燃引信（mine）：触发方式为 ProximityFuse，且有角色在 60px 内<b>且</b>在移动。
        /// 距离为负（无效输入）视为不在范围内。
        /// </summary>
        public static bool ShouldStartFuse(
            WeaponTrigger trigger, float distanceToNearestCharacter, bool characterMoving)
        {
            if (trigger != WeaponTrigger.ProximityFuse)
            {
                return false;
            }

            if (!characterMoving)
            {
                return false;
            }

            return distanceToNearestCharacter >= 0f && distanceToNearestCharacter <= ProximityRadius;
        }

        /// <summary>引信推进一帧，返回新的剩余帧数（下限 0）。</summary>
        public static int TickFuse(int framesRemaining)
        {
            return framesRemaining > 0 ? framesRemaining - 1 : 0;
        }

        /// <summary>引信是否已到点（可以引爆）。</summary>
        public static bool IsFuseExpired(int framesRemaining)
        {
            return framesRemaining <= 0;
        }

        /// <summary>该经过帧是否应播放蜂鸣（严格命中 beepTimes 序列）。</summary>
        public static bool ShouldBeep(int elapsedFrames)
        {
            for (int i = 0; i < MineBeepTimes.Length; i++)
            {
                if (MineBeepTimes[i] == elapsedFrames)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>从点燃到 <paramref name="elapsedFrames"/>（含）为止已播放的蜂鸣次数。</summary>
        public static int BeepCountUpTo(int elapsedFrames)
        {
            int count = 0;
            for (int i = 0; i < MineBeepTimes.Length; i++)
            {
                if (MineBeepTimes[i] <= elapsedFrames)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
