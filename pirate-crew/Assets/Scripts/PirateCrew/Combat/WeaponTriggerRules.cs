using System;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 武器触发/引爆方式。
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
    /// 武器触发判定（纯静态函数，不依赖 MonoBehaviour / GameObject）。
    /// </summary>
    public static class WeaponTriggerRules
    {
        /// <summary>静止判定里垂直速度的阈值：|vy| &lt; 0.2。</summary>
        public const float AtRestVyEpsilon = 0.2f;

        /// <summary>
        /// 3D 侧「静止 / 在动」判定的速度平方阈值（sqrMagnitude）：
        /// <c>PirateBase.IsMoving</c> 的默认参数与 <c>WeaponProjectile.IsInFlight</c> 共用本常量。
        /// 【无逆向出处】原版静止判定为 <c>vx == 0 且 |vy| &lt; 0.2</c>（§5.2 dynamite 行，
        /// Flash 像素速度域，见 <see cref="AtRestVyEpsilon"/>）；0.01 是 3D 侧 sqrMagnitude
        /// 阈值的近似档（标定取值，数值沿用初版），不是原版数值的换算。
        /// </summary>
        public const float AtRestSqrMagnitudeEpsilon = 0.01f;

        /// <summary>
        /// 是否处于静止（<c>vx == 0 &amp;&amp; |vy| &lt; 0.2</c>）。
        /// 水平要求严格为 0；垂直用阈值。
        /// </summary>
        public static bool IsAtRest(float vx, float vy, float restVyEpsilon = AtRestVyEpsilon)
        {
            return vx == 0f && Math.Abs(vy) < restVyEpsilon;
        }

        /// <summary>
        /// 本帧是否应引爆。各触发方式：
        ///   Contact      → 接触即爆
        ///   AtRest       → 静止即爆
        ///   Click        → 点击引爆
        ///   BlastContact → 被爆炸命中触发
        ///   ProximityFuse→ 引信点燃且剩余帧数到点
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
    }
}
