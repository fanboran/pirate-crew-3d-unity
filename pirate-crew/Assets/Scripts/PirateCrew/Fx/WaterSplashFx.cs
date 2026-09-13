using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.Fx
{
    /// <summary>
    /// 入水水花特效：飞沫水柱 + 水沫 + 两道扩散涟漪。
    ///
    /// 【触发来源】两条，都由 <see cref="FxRoot"/> 订阅：
    ///   1. <c>crew_died</c> + 实例的 <c>PirateBase.Drowned</c>（落水即死，§4.4 全局规则）→
    ///      <see cref="PlayDrown"/>：**这是"落水即死"最需要的强反馈**；
    ///   2. <c>battle_projectile_detonated</c> 的爆心 y 在水面附近（弹体落水引爆，§5.2 water 行为）→
    ///      按落速/默认速度播普通水花。
    ///
    /// 【水面坐标】一律吸附到 <c>LevelGeometry.WaterSurfaceY</c>（= -0.2），而不是用单位当前高度：
    /// 单位落水时已经沉到水面以下，直接拿它的 y 会让水花出现在水下（`docs/M2-3D空间模型对齐.md` §4）。
    ///
    /// 【预算】普通水花 2 个粒子系统 + 2 个涟漪 Quad ≈ 4 个 DrawCall；
    /// 落水款额外加一圈大涟漪与泡沫，≈ 5 个。
    /// </summary>
    public static class WaterSplashFx
    {
        /// <summary>落水默认冲击速度（世界单位/秒）。事件载荷不带速度，取一个"从岸上摔下来"的量级
        /// （【AI 提案】：角色被抛飞后落地竖向速度通常 5-10，取 6 作为对应"中等水花"）。</summary>
        public const float DrownFallSpeed = 6f;

        /// <summary>水花吸附水面时抬高一点点，避免与水面 z-fighting。</summary>
        const float SurfaceLift = 0.01f;

        /// <summary>普通入水水花。</summary>
        public static void Play(Vector3 position, float fallSpeed)
        {
            if (!Application.isPlaying)
                return;

            float speed = Mathf.Clamp(fallSpeed, 0f, 20f);
            Vector3 at = new Vector3(position.x, LevelGeometry.WaterSurfaceY + SurfaceLift, position.z);

            int droplets = FxRules.SplashDropletCount(speed);
            float life = FxRules.SplashLifetime(speed);
            float size = FxRules.SplashDropletSize(speed);

            // ---- 飞沫水柱（向上迸 + 受重力落回）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = at,
                Material = FxMaterial.WaterSplash,
                Count = droplets,
                Lifetime = life,
                LifetimeVariance = 0.35f,
                Speed = 2.20f + speed * 0.10f,
                SpeedVariance = 0.45f,
                StartSize = size,
                EndSize = 0.80f,
                Gravity = 1.10f,
                Radius = 0.14f,
                StartAlpha = 1.00f,
                EndAlpha = 0.60f,
                FadeStart = 0.45f,
                RiseSpeed = 1.80f + speed * 0.08f,   // 向上偏置，读作"溅起"
                RotationSpeed = 60f,
                SortingFudge = -1f,
            });

            // ---- 水沫（贴水面的白色泡沫，缓慢扩散）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = at,
                Material = FxMaterial.WaterFoam,
                Count = FxRules.SplashFoamCount(speed),
                Lifetime = life * 1.35f,
                LifetimeVariance = 0.30f,
                Speed = 0.80f,
                SpeedVariance = 0.50f,
                StartSize = size * 1.60f,
                EndSize = 1.80f,
                Gravity = 0f,
                Radius = 0.16f,
                StartAlpha = 0.80f,
                EndAlpha = 0.35f,
                FadeStart = 0.40f,
                RiseSpeed = 0.18f,
                SortingFudge = 2f,
            });

            // ---- 两道错开的涟漪（近处快、远处慢，读作"一圈圈荡开"）----
            PlayRipple(at, speed, 0.30f, 1.00f);
            PlayRipple(at, speed, 0.62f, 0.72f);
        }

        /// <summary>落水即死的强反馈：水花 + 额外一圈大涟漪 + 更长的泡沫。</summary>
        public static void PlayDrown(Vector3 position)
        {
            Play(position, DrownFallSpeed);

            Vector3 at = new Vector3(position.x, LevelGeometry.WaterSurfaceY + SurfaceLift, position.z);
            Color ripple = FxRules.RippleColor();
            FxSpriteFx big = FxPool.RentSprite(additive: true, billboard: false);
            big.SetTexture(FxTextures.Get(FxTextureKind.Ring));
            big.PlayOnce(
                at,
                new Vector2(0.40f, 0.40f),
                new Vector2(3.00f, 3.00f),
                new Color(ripple.r, ripple.g, ripple.b, 0.85f),
                new Color(ripple.r, ripple.g, ripple.b, 0f),
                0.95f);
        }

        /// <summary>单个涟漪环。</summary>
        static void PlayRipple(Vector3 at, float speed, float startScale, float alphaScale)
        {
            float diameter = FxRules.RippleDiameter(speed);
            Color ripple = FxRules.RippleColor();
            FxSpriteFx ring = FxPool.RentSprite(additive: true, billboard: false);
            ring.SetTexture(FxTextures.Get(FxTextureKind.Ring));
            ring.PlayOnce(
                at,
                new Vector2(diameter * startScale, diameter * startScale),
                new Vector2(diameter, diameter),
                new Color(ripple.r, ripple.g, ripple.b, 0.80f * alphaScale),
                new Color(ripple.r, ripple.g, ripple.b, 0f),
                FxRules.RippleLifetime(speed) * (0.75f + startScale * 0.35f));
        }
    }
}
