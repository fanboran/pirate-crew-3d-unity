using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.Fx
{
    /// <summary>
    /// 爆炸特效：核心闪光 + 外焰 + 火花 + 星屑 + 木屑 + 沙尘 + 烟雾余留 + 地面冲击波环。
    ///
    /// 【触发来源】<c>battle_projectile_detonated</c>（`BattleEvents.ProjectileDetonated`，
    /// 载荷 <c>ProjectileDetonatedPayload</c>：武器 id + 爆心世界坐标）——由 <see cref="FxRoot"/> 订阅后调用；
    /// 也可由其它系统直接调 <see cref="Play"/>。半径/威力从 `WeaponCatalog.Get(weapon)` 查得，
    /// 不需要事件载荷携带（所以这个事件**够用**，见交付报告的"缺触发信息"一节）。
    ///
    /// 【规模随半径】所有粒子数/尺寸/速度/时长/冲击波直径都由 <see cref="FxRules"/> 按
    /// 爆炸半径（= size/2 + 20 px → 世界单位）映射，cannonball 与 dynamite 的观感差异明显。
    ///
    /// 【预算】一次爆炸 7 个粒子系统 + 1 个 Quad = **8 个 DrawCall**；
    /// 粒子总数 ≤ <see cref="FxRules.MaxExplosionParticles"/>（各系统数量由 FxRules 钳制）。
    ///
    /// 【3D 球形】爆心即事件给的世界坐标；粒子用 World 空间球形爆发
    /// （`docs/M2-3D空间模型对齐.md` §5：爆炸是 3D 球）。
    /// </summary>
    public static class ExplosionFx
    {
        /// <summary>播放一次爆炸。</summary>
        /// <param name="center">爆心世界坐标（y 向上）。</param>
        /// <param name="explosionSize">爆炸 size 参数（Flash px；radius = size/2 + 20）。</param>
        public static void Play(Vector3 center, float explosionSize)
        {
            if (!Application.isPlaying || explosionSize <= 0f)
                return;

            float radius = FxRules.ExplosionRadiusWorld(explosionSize);
            float scale = FxRules.ExplosionVisualScale(explosionSize);

            // ---- 核心闪光（短、亮、外层压住其它粒子的排序）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.ExplosionCore,
                Count = FxRules.ExplosionCoreParticles(explosionSize),
                Lifetime = FxRules.FireballLifetime(explosionSize) * 0.70f,
                LifetimeVariance = 0.20f,
                Speed = FxRules.FireballSpeed(explosionSize) * 0.50f,
                SpeedVariance = 0.25f,
                StartSize = FxRules.FireballStartSize(explosionSize) * 1.10f,
                EndSize = 1.60f,
                Gravity = 0f,
                Radius = radius * 0.18f,
                StartAlpha = 1.00f,
                EndAlpha = 0.50f,
                FadeStart = 0.35f,
                RiseSpeed = 0.40f,
                SortingFudge = -3f,
            });

            // ---- 外焰 ----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.ExplosionFire,
                Count = FxRules.FireballParticles(explosionSize),
                Lifetime = FxRules.FireballLifetime(explosionSize),
                LifetimeVariance = 0.30f,
                Speed = FxRules.FireballSpeed(explosionSize),
                SpeedVariance = 0.35f,
                StartSize = FxRules.FireballStartSize(explosionSize),
                EndSize = 1.35f,
                Gravity = 0f,
                Radius = radius * 0.30f,
                StartAlpha = 0.95f,
                EndAlpha = 0.45f,
                FadeStart = 0.40f,
                RiseSpeed = 1.20f,
                SortingFudge = -2f,
            });

            // ---- 火花（最快、最亮、最"迸射"）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Sparks,
                Count = FxRules.SparkParticles(explosionSize),
                Lifetime = FxRules.SparkLifetime(explosionSize),
                LifetimeVariance = 0.40f,
                Speed = FxRules.SparkSpeed(explosionSize),
                SpeedVariance = 0.40f,
                StartSize = FxRules.SparkSize(explosionSize),
                EndSize = 0.55f,
                Gravity = 0.35f,
                Radius = radius * 0.25f,
                StartAlpha = 1.00f,
                EndAlpha = 0.60f,
                FadeStart = 0.55f,
                RotationSpeed = 90f,
                SortingFudge = -4f,
            });

            // ---- 星屑（四芒星，少量点亮画面）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Star4,
                Count = FxRules.ExplosionStarParticles(explosionSize),
                Lifetime = FxRules.SparkLifetime(explosionSize) * 0.8f,
                LifetimeVariance = 0.35f,
                Speed = FxRules.SparkSpeed(explosionSize) * 0.55f,
                SpeedVariance = 0.45f,
                StartSize = FxRules.SparkSize(explosionSize) * 1.8f,
                EndSize = 1.50f,
                Gravity = 0.20f,
                Radius = radius * 0.20f,
                StartAlpha = 0.95f,
                EndAlpha = 0.40f,
                FadeStart = 0.45f,
                RotationSpeed = 45f,
                SortingFudge = -3.5f,
            });

            // ---- 木屑（翻滚、受重力；木色烘在贴图里）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.WoodDebris,
                Count = Mathf.Max(3, FxRules.DebrisParticles(explosionSize) / 2),
                Lifetime = FxRules.DebrisLifetime(explosionSize),
                LifetimeVariance = 0.35f,
                Speed = FxRules.DebrisSpeed(explosionSize),
                SpeedVariance = 0.40f,
                StartSize = FxRules.DebrisSize(explosionSize),
                EndSize = 1.00f,
                Gravity = 1.00f,
                Radius = radius * 0.30f,
                StartAlpha = 1.00f,
                EndAlpha = 0.90f,
                FadeStart = 0.70f,
                RotationSpeed = 220f,
                SortingFudge = 1f,
            });

            // ---- 沙尘（扩散、慢落；沙地中调色）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Sand,
                Count = Mathf.Max(3, FxRules.DebrisParticles(explosionSize) / 2),
                Lifetime = FxRules.DebrisLifetime(explosionSize) * 1.30f,
                LifetimeVariance = 0.35f,
                Speed = FxRules.DebrisSpeed(explosionSize) * 0.65f,
                SpeedVariance = 0.40f,
                StartSize = FxRules.DebrisSize(explosionSize) * 1.60f,
                EndSize = 1.90f,
                Gravity = 0.12f,
                Radius = radius * 0.38f,
                StartAlpha = 0.70f,
                EndAlpha = 0.30f,
                FadeStart = 0.45f,
                RiseSpeed = 0.30f,
                SortingFudge = 2f,
            });

            // ---- 烟雾余留（低饱和、上浮、膨胀）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Smoke,
                Count = FxRules.SmokeParticles(explosionSize),
                Lifetime = FxRules.SmokeLifetime(explosionSize),
                LifetimeVariance = 0.30f,
                Speed = 0.80f,
                SpeedVariance = 0.50f,
                StartSize = FxRules.SmokeStartSize(explosionSize),
                EndSize = 2.20f,
                Gravity = 0f,
                Radius = radius * 0.34f,
                StartAlpha = 0.55f,
                EndAlpha = 0.30f,
                FadeStart = 0.40f,
                RiseSpeed = FxRules.SmokeRiseSpeed(explosionSize),
                RotationSpeed = 25f,
                SortingFudge = 3f,
            });

            // ---- 地面冲击波环（略高于地面，避免与地形 z-fighting）----
            float shockDiameter = FxRules.ShockwaveDiameter(explosionSize);
            Color shock = FxRules.ShockwaveColor();
            FxSpriteFx ring = FxPool.RentSprite(additive: true, billboard: false);
            ring.SetTexture(FxTextures.Get(FxTextureKind.Ring));
            ring.PlayOnce(
                new Vector3(center.x, LevelGeometry.GroundTopY + 0.02f, center.z),
                new Vector2(shockDiameter * 0.25f, shockDiameter * 0.25f),
                new Vector2(shockDiameter, shockDiameter),
                new Color(shock.r, shock.g, shock.b, 0.90f * Mathf.Clamp(scale, 0.7f, 1.3f)),
                new Color(shock.r, shock.g, shock.b, 0f),
                FxRules.ShockwaveLifetime(explosionSize));
        }

        /// <summary>
        /// 小型命中/落地尘爆（用于无爆炸的弹体引爆，如巨石落地——`battle_projectile_detonated`
        /// 对非爆炸武器也会发布）。规模固定小一圈，读作"砸起一片土"而非"炸开"。
        /// </summary>
        public static void PlayImpactPuff(Vector3 center, float scale = 1f)
        {
            if (!Application.isPlaying)
                return;

            float s = Mathf.Clamp(scale, 0.5f, 2f);

            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Sand,
                Count = Mathf.RoundToInt(10f * s),
                Lifetime = 0.55f,
                LifetimeVariance = 0.35f,
                Speed = 1.60f * s,
                SpeedVariance = 0.45f,
                StartSize = 0.09f * s,
                EndSize = 2.10f,
                Gravity = 0.20f,
                Radius = 0.16f * s,
                StartAlpha = 0.70f,
                EndAlpha = 0.30f,
                FadeStart = 0.45f,
                RiseSpeed = 0.35f,
                SortingFudge = 2f,
            });

            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Smoke,
                Count = Mathf.RoundToInt(5f * s),
                Lifetime = 0.80f,
                LifetimeVariance = 0.30f,
                Speed = 0.60f,
                SpeedVariance = 0.40f,
                StartSize = 0.16f * s,
                EndSize = 1.90f,
                Gravity = 0f,
                Radius = 0.14f * s,
                StartAlpha = 0.45f,
                EndAlpha = 0.25f,
                FadeStart = 0.40f,
                RiseSpeed = 0.55f,
                SortingFudge = 3f,
            });
        }
    }
}
