using PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.Fx
{
    /// <summary>
    /// 爆炸特效：**三层结构**（亮核 → 橙色中透体 → 暗烟低透）+ 火花 + 木屑/沙尘 + 地面冲击波环。
    ///
    /// 【三层（r6 重做）】r5 复验（size=160）读到"白雾 + 硬边半透明白卡片 + 绿白四角星"、没有火球：
    /// 34 颗火球（起始尺寸 1.56 世界单位）+ 11 颗核心在 additive 下大面积重叠 → 峰值远超 Bloom
    /// threshold 饱和成白。r6 起：
    ///   ① <b>亮核</b>（ExplosionCore 材质）小而实——半径×0.18、不膨胀、数量为火球的 1/4；
    ///   ② <b>橙色中透体</b>（ExplosionFire 材质 #FF7A1A）中等大小、半透明——半径×0.26、EndAlpha 0.25；
    ///   ③ <b>暗烟低透</b>（Smoke 材质）大而慢、低透——半径×0.42、EndAlpha 0.14。
    ///   星屑（Star4 绿白四角星）**已从本组合移除**（粒子系统 7 → 6）；FxMaterial.Star4 资产保留不动。
    ///   所有数量/尺寸/寿命仍来自 <see cref="FxRules"/>（可无头断言）。
    ///
    /// 【触发来源】<c>battle_projectile_detonated</c>（`BattleEvents.ProjectileDetonated`，
    /// 载荷 <c>ProjectileDetonatedPayload</c>：武器 id + 爆心世界坐标）——由 <see cref="FxRoot"/> 订阅后调用；
    /// 也可由其它系统直接调 <see cref="Play"/>。半径/威力从 `WeaponCatalog.Get(weapon)` 查得，
    /// 不需要事件载荷携带（所以这个事件**够用**，见交付报告的"缺触发信息"一节）。
    ///
    /// 【预算】一次爆炸 6 个粒子系统 + 1 个 Quad = **7 个 DrawCall**；
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

            // ---- 三层之①：亮核（小而实；不膨胀，只做炸点高光）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.ExplosionCore,
                Count = FxRules.ExplosionCoreParticles(explosionSize),
                Lifetime = FxRules.FireballLifetime(explosionSize) * 0.70f,
                LifetimeVariance = 0.20f,
                Speed = FxRules.FireballSpeed(explosionSize) * 0.50f,
                SpeedVariance = 0.25f,
                StartSize = FxRules.ExplosionCoreStartSize(explosionSize),
                EndSize = 1.00f,                       // r6：原 1.60（越膨胀越糊）
                Gravity = 0f,
                Radius = radius * 0.18f,
                StartAlpha = 1.00f,
                EndAlpha = 0.60f,
                FadeStart = 0.30f,
                RiseSpeed = 0.8f,
                SortingFudge = -3f,
            });

            // ---- 三层之②：橙色中透体（#FF7A1A，中等大小、半透明，读作"火球"）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.ExplosionFire,
                Count = FxRules.FireballParticles(explosionSize),
                Lifetime = FxRules.FireballLifetime(explosionSize),
                LifetimeVariance = 0.30f,
                Speed = FxRules.FireballSpeed(explosionSize),
                SpeedVariance = 0.35f,
                StartSize = FxRules.FireballStartSize(explosionSize),   // r6：半径×0.26（原 ×0.5）
                EndSize = 1.35f,
                Gravity = 0f,
                Radius = radius * 0.30f,
                StartAlpha = 0.80f,                    // r6：原 0.95
                EndAlpha = 0.25f,                      // r6：原 0.45（中透体，不再糊成一片白）
                FadeStart = 0.40f,
                RiseSpeed = 2.4f,
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
                StartAlpha = 0.85f,                    // r6：原 1.00（硬边木片不再读作实心卡片）
                EndAlpha = 0.70f,                      // r6：原 0.90
                FadeStart = 0.60f,
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
                StartSize = FxRules.DebrisSize(explosionSize) * 1.35f,   // r6：原 1.60
                EndSize = 1.70f,                                          // r6：原 1.90
                Gravity = 0.12f,
                Radius = radius * 0.38f,
                StartAlpha = 0.42f,                    // r6：原 0.70（半透明白卡片的本体之一）
                EndAlpha = 0.16f,                      // r6：原 0.30
                FadeStart = 0.45f,
                RiseSpeed = 0.6f,
                SortingFudge = 2f,
            });

            // ---- 三层之③：暗烟低透（大而慢、上浮、膨胀；Tint 已在 FxMaterials 改为烟灰）----
            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Smoke,
                Count = FxRules.SmokeParticles(explosionSize),
                Lifetime = FxRules.SmokeLifetime(explosionSize),
                LifetimeVariance = 0.30f,
                Speed = 1.6f,
                SpeedVariance = 0.50f,
                StartSize = FxRules.SmokeStartSize(explosionSize),
                EndSize = 2.20f,
                Gravity = 0f,
                Radius = radius * 0.34f,
                StartAlpha = 0.38f,                    // r6：原 0.55（白雾主因：高透白烟糊满画面）
                EndAlpha = 0.14f,                      // r6：原 0.30
                FadeStart = 0.40f,
                RiseSpeed = FxRules.SmokeRiseSpeed(explosionSize),
                RotationSpeed = 25f,
                SortingFudge = 3f,
            });

            // ---- 地面冲击波环（略高于地面，避免与地形 z-fighting）----
            // r6：整体透明度 0.90 → 0.62（暖白 additive 大环也是"硬边半透明白卡片"的候选之一）。
            float shockDiameter = FxRules.ShockwaveDiameter(explosionSize);
            Color shock = FxRules.ShockwaveColor();
            FxSpriteFx ring = FxPool.RentSprite(additive: true, billboard: false);
            ring.SetTexture(FxTextures.Get(FxTextureKind.Ring));
            ring.PlayOnce(
                new Vector3(center.x, LevelGeometry.GroundTopY + 0.02f, center.z),
                new Vector2(shockDiameter * 0.25f, shockDiameter * 0.25f),
                new Vector2(shockDiameter, shockDiameter),
                new Color(shock.r, shock.g, shock.b, 0.62f * Mathf.Clamp(scale, 0.7f, 1.3f)),
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
                Speed = 3.2f * s,
                SpeedVariance = 0.45f,
                StartSize = 0.18f * s,
                EndSize = 2.10f,
                Gravity = 0.20f,
                Radius = 0.32f * s,
                StartAlpha = 0.70f,
                EndAlpha = 0.30f,
                FadeStart = 0.45f,
                RiseSpeed = 0.7f,
                SortingFudge = 2f,
            });

            FxPool.RentParticles().Play(new FxBurstSpec
            {
                Position = center,
                Material = FxMaterial.Smoke,
                Count = Mathf.RoundToInt(5f * s),
                Lifetime = 0.80f,
                LifetimeVariance = 0.30f,
                Speed = 1.2f,
                SpeedVariance = 0.40f,
                StartSize = 0.32f * s,
                EndSize = 1.90f,
                Gravity = 0f,
                Radius = 0.28f * s,
                StartAlpha = 0.45f,
                EndAlpha = 0.25f,
                FadeStart = 0.40f,
                RiseSpeed = 1.1f,
                SortingFudge = 3f,
            });
        }
    }
}
