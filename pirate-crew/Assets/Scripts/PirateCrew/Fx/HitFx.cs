using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 命中反馈：火花 + 尘土 + 伤害数字。
    ///
    /// 【与既有白闪的分工】受击"闪白"已经由 `UnitOutlineBinder.SetColorFlash` +
    /// `CrewVisualAnimator.NotifyHit` 实现（描边链路的 MPB 通道），本类**不重复实现**，
    /// 只补三样它没有的东西：迸射火花、脚下尘土、上浮伤害数字（Art Bible §7.2）。
    ///
    /// 【触发来源】<c>crew_damaged</c>（载荷 <c>CrewDamagedPayload</c>：角色 id / 队伍 / 本次伤害 /
    /// 剩余生命 / 最大生命）。载荷**没有世界坐标**，由 <see cref="FxRoot"/> 用
    /// 「PirateId → PirateBase」注册表把 id 还原成位置（该注册表在 battle_started 时建立，
    /// 见交付报告"缺触发信息"一节：更干净的做法是给载荷加一个 Position/Transform 字段）。
    ///
    /// 【预算】2 个粒子系统 + 1 个数字 Quad ≈ 3 个 DrawCall；粒子上限 18+8=26。
    /// </summary>
    public static class HitFx
    {
        /// <summary>播放一次命中反馈。</summary>
        /// <param name="worldPosition">被命中单位的位置（枢轴点，y = 脚底 + 0.25）。</param>
        /// <param name="damage">本次伤害。</param>
        /// <param name="maxHealth">最大生命（分档与数量用）。</param>
        public static void Play(Vector3 worldPosition, float damage, int maxHealth)
        {
            if (!Application.isPlaying)
                return;

            // 命中点在躯干高度（枢轴上方一点），不是脚底。
            Vector3 hitPoint = worldPosition + Vector3.up * 0.12f;   // 抬高量 ×2（格 1→2 单位）

            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = hitPoint,
                Material = FxMaterial.Sparks,
                Count = FxRules.HitSparkCount(damage, maxHealth),
                Lifetime = FxRules.HitSparkLifetime(damage, maxHealth),
                LifetimeVariance = 0.35f,
                Speed = 9f,
                SpeedVariance = 0.40f,
                StartSize = 0.14f,
                EndSize = 0.55f,
                Gravity = 0.50f,
                Radius = 0.2f,
                StartAlpha = 1.00f,
                EndAlpha = 0.55f,
                FadeStart = 0.50f,
                RotationSpeed = 120f,
                SortingFudge = -1.5f,
            });

            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = hitPoint,
                Material = FxMaterial.Sand,
                Count = FxRules.HitDustCount(damage, maxHealth),
                Lifetime = 0.45f,
                LifetimeVariance = 0.30f,
                Speed = 2.8f,
                SpeedVariance = 0.40f,
                StartSize = 0.18f,
                EndSize = 1.90f,
                Gravity = 0.15f,
                Radius = 0.24f,
                StartAlpha = 0.70f,
                EndAlpha = 0.30f,
                FadeStart = 0.40f,
                RiseSpeed = 0.5f,
                SortingFudge = 2f,
            });

            DamageNumberFx.Play(
                worldPosition + Vector3.up * FxRules.DamageNumberBaseHeight,
                damage, maxHealth);
        }

        /// <summary>死亡时的一小股尘土（不落水的场合；落水由 <see cref="WaterSplashFx.PlayDrown"/> 接管）。</summary>
        public static void PlayDeathPuff(Vector3 worldPosition)
        {
            if (!Application.isPlaying)
                return;

            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = worldPosition,
                Material = FxMaterial.Smoke,
                Count = 8,
                Lifetime = 0.70f,
                LifetimeVariance = 0.30f,
                Speed = 2.2f,
                SpeedVariance = 0.40f,
                StartSize = 0.32f,
                EndSize = 1.80f,
                Gravity = 0f,
                Radius = 0.32f,
                StartAlpha = 0.50f,
                EndAlpha = 0.25f,
                FadeStart = 0.40f,
                RiseSpeed = 0.9f,
                SortingFudge = 3f,
            });
        }
    }
}
