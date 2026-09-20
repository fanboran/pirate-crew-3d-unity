using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 爆炸命中目标的最小数据结构（不引用 MonoBehaviour，便于纯逻辑测试）。
    ///
    /// 【3D 泛化】X / Y 是 Flash 平面坐标（px，原侧视的 (x, y) → 3D 的 (X, Z)）；
    /// <see cref="Height"/> 是平面之外的第三分量 = 世界高度 Y（向上为正）。默认 0，
    /// 因此只传 (x, y) 的既有 2D 用例（高度差 = 0）与 3D 泛化前的结果逐值相同。
    /// </summary>
    public readonly struct ExplosionTarget
    {
        public readonly float X;
        public readonly float Y;

        /// <summary>第三分量：世界高度 Y（向上为正）。默认 0 时退化为原 2D 平面。</summary>
        public readonly float Height;

        public readonly bool Alive;

        public ExplosionTarget(float x, float y, bool alive)
        {
            X = x;
            Y = y;
            Height = 0f;
            Alive = alive;
        }

        public ExplosionTarget(float x, float y, float height, bool alive)
        {
            X = x;
            Y = y;
            Height = height;
            Alive = alive;
        }
    }

    /// <summary>
    /// 单个目标受到的爆炸结算结果。
    /// </summary>
    public readonly struct ExplosionHit
    {
        /// <summary>该目标在传入列表中的索引，便于调用方回写。</summary>
        public readonly int Index;

        /// <summary>本次伤害（maxDamage * falloff）。</summary>
        public readonly float Damage;

        /// <summary>水平速度增量（Flash 平面 X 分量，已含方向与 5k 系数）。</summary>
        public readonly float DeltaVx;

        /// <summary>平面纵深速度增量（Flash 平面 Y 分量，已含方向与 5k 系数）。</summary>
        public readonly float DeltaVy;

        /// <summary>
        /// 竖直速度增量，方向为世界 <b>+Y（向上为正）</b>。
        /// = 3D 径向单位向量的竖直分量 × 5k + 固定 6k 抬升项（原版"总是额外上抛"）。
        /// 3D 泛化前该 6k 混在 <see cref="DeltaVy"/> 里（原版约定 -y = 向上），这里拆成独立字段，
        /// 供 Unity 侧直接映射到世界 +Y。平面高度差为 0 时本值 = 6k。
        /// </summary>
        public readonly float DeltaVUp;

        /// <summary>线性衰减系数（1 在爆心，0 在半径边缘）。</summary>
        public readonly float Falloff;

        public ExplosionHit(int index, float damage, float deltaVx, float deltaVy, float falloff, float deltaVUp)
        {
            Index = index;
            Damage = damage;
            DeltaVx = deltaVx;
            DeltaVy = deltaVy;
            Falloff = falloff;
            DeltaVUp = deltaVUp;
        }
    }

    /// <summary>
    /// 一次爆炸的整体结算结果。
    /// </summary>
    public readonly struct ExplosionResult
    {
        /// <summary>命中目标（d &lt;= radius）的结算列表，按传入顺序。</summary>
        public readonly ExplosionHit[] Hits;

        /// <summary>施暴者邪恶度增量 = Σ falloff（AI 后续优先攻击邪恶度高的敌人）。</summary>
        public readonly float EvilnessGain;

        public ExplosionResult(ExplosionHit[] hits, float evilnessGain)
        {
            Hits = hits;
            EvilnessGain = evilnessGain;
        }

        public int HitCount => Hits == null ? 0 : Hits.Length;
    }

    /// <summary>
    /// 爆炸范围伤害/击退结算（<b>3D 球</b>）。
    /// 对应逆向文档 §5.3（Explosion.as）与 <c>docs/M2-3D空间模型对齐.md</c> §5：
    ///   radius = size/2 + 20
    ///   d = 目标到爆心的 <b>3D 距离</b>（Flash 平面 (x,y) + 世界高度 Height）；仅 d &lt;= radius 命中
    ///   falloff = 1 - d/radius
    ///   damage = maxDamage * falloff（无最小伤害保底，边缘趋 0）
    ///   k = 0.06 * falloff * maxDamage
    ///   deltaVx/deltaVy = 3D 径向单位向量的平面分量 * 5k（映射到世界 X/Z）
    ///   deltaVUp        = 3D 径向单位向量的竖直分量 * 5k + 6k（映射到世界 +Y；原版"总是额外上抛"）
    ///   caster.evilness += Σ falloff
    ///
    /// 【与 2D 旧值的关系】高度差（含爆心高度）为 0 时：d 退化为原平面距离，
    /// falloff / damage / deltaVx 与原实现逐值相同；原 <c>deltaVy = ny*5k - 6k</c> 里的 -6k
    /// 被拆到 <see cref="ExplosionHit.DeltaVUp"/>（改为世界 +Y 的 +6k），故 <c>deltaVy</c> 净增 +6k。
    /// 公式与常数全部沿用 Flash，**不**采纳 Godot 版的 blast_radius/DamageCalculator 占位值。
    /// 纯静态逻辑，不依赖 MonoBehaviour / GameObject。
    /// </summary>
    public static class ExplosionResolver
    {
        /// <summary>半径在 size/2 基础上的额外膨胀（原版固定 +20px）。</summary>
        public const float RadiusPadding = 20f;

        /// <summary>击退系数基数（原版 0.06）。</summary>
        public const float KnockbackCoefficient = 0.06f;

        /// <summary>击退速度倍率（原版 5）。</summary>
        public const float KnockbackMultiplier = 5f;

        /// <summary>额外上抛系数（原版 6；在原版里是 velocityY -= 6k，即向上）。</summary>
        public const float ExtraLiftCoefficient = 6f;

        /// <summary>爆炸半径：size / 2 + 20（px）。</summary>
        public static float Radius(float size)
        {
            return size / 2f + RadiusPadding;
        }

        /// <summary>
        /// 结算一次爆炸（2D 兼容重载：爆心高度默认 0，退化为原平面口径）。
        /// </summary>
        /// <param name="size">爆炸尺寸参数（radius = size/2 + 20）</param>
        /// <param name="maxDamage">爆心处最大伤害</param>
        /// <param name="centerX">爆心 Flash 平面 x</param>
        /// <param name="centerY">爆心 Flash 平面 y</param>
        /// <param name="targets">候选目标（Alive=false 的会被跳过）</param>
        public static ExplosionResult Resolve(
            float size, float maxDamage, float centerX, float centerY,
            IReadOnlyList<ExplosionTarget> targets)
        {
            return Resolve(size, maxDamage, centerX, centerY, 0f, targets);
        }

        /// <summary>
        /// 结算一次爆炸（3D 球：距离含平面 (x,y) 与高度差）。
        /// </summary>
        /// <param name="size">爆炸尺寸参数（radius = size/2 + 20）</param>
        /// <param name="maxDamage">爆心处最大伤害</param>
        /// <param name="centerX">爆心 Flash 平面 x</param>
        /// <param name="centerY">爆心 Flash 平面 y</param>
        /// <param name="centerHeight">爆心世界高度 Y（向上为正）</param>
        /// <param name="targets">候选目标（Alive=false 的会被跳过）</param>
        public static ExplosionResult Resolve(
            float size, float maxDamage, float centerX, float centerY, float centerHeight,
            IReadOnlyList<ExplosionTarget> targets)
        {
            float radius = Radius(size);
            var hits = new List<ExplosionHit>();
            float evilnessGain = 0f;

            if (targets == null)
            {
                return new ExplosionResult(new ExplosionHit[0], 0f);
            }

            for (int i = 0; i < targets.Count; i++)
            {
                ExplosionTarget target = targets[i];
                if (!target.Alive)
                {
                    continue;   // 死亡目标不受爆炸影响
                }

                float dx = target.X - centerX;
                float dy = target.Y - centerY;
                float dh = target.Height - centerHeight;
                // 3D 距离：平面 (dx, dy) 之外再计入高度差 dh（height=0 时与原 2D 完全一致）。
                float distance = Mathf.Sqrt(dx * dx + dy * dy + dh * dh);

                if (distance > radius)
                {
                    continue;   // 未命中
                }

                float falloff;
                float nx;
                float ny;
                float nUp;

                if (distance == 0f)
                {
                    // ★ d == 0 显式处置（原版 dx/d 会除零得 NaN，导致速度变 NaN 后物理失控）：
                    //   falloff = 1 —— 目标正处爆心，理应受满额伤害，取衰减公式在 d→0 时的极限；
                    //   方向取零向量 (0,0,0) —— 爆心处没有可定义的"向外"方向，不臆造推力方向；
                    //   于是 deltaVx = deltaVy = 0，deltaVUp = 6k（仅保留公式中"总是额外上抛"的分量）。
                    //   理由：可用定义（定向、无 NaN、可测试）优先于隐式 NaN 传播。
                    falloff = 1f;
                    nx = 0f;
                    ny = 0f;
                    nUp = 0f;
                }
                else
                {
                    falloff = 1f - distance / radius;
                    nx = dx / distance;
                    ny = dy / distance;
                    nUp = dh / distance;
                }

                float damage = maxDamage * falloff;
                float k = KnockbackCoefficient * falloff * maxDamage;

                // 3D 径向单位向量 × 5k：平面分量 → 世界 (X, Z)，竖直分量并入 DeltaVUp。
                float deltaVx = nx * KnockbackMultiplier * k;
                float deltaVy = ny * KnockbackMultiplier * k;
                // 沿世界 +Y 的固定抬升（原版 velocityY -= 6k，Flash -y 即向上）+ 径向竖直分量。
                float deltaVUp = nUp * KnockbackMultiplier * k + ExtraLiftCoefficient * k;

                hits.Add(new ExplosionHit(i, damage, deltaVx, deltaVy, falloff, deltaVUp));
                evilnessGain += falloff;
            }

            return new ExplosionResult(hits.ToArray(), evilnessGain);
        }
    }
}
