using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Combat
{
    /// <summary>
    /// 爆炸命中目标的最小数据结构（不引用 MonoBehaviour，便于纯逻辑测试）。
    /// </summary>
    public readonly struct ExplosionTarget
    {
        public readonly float X;
        public readonly float Y;
        public readonly bool Alive;

        public ExplosionTarget(float x, float y, bool alive)
        {
            X = x;
            Y = y;
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

        /// <summary>水平速度增量（已含方向与 5k 系数）。</summary>
        public readonly float DeltaVx;

        /// <summary>垂直速度增量（已含 5k 方向分量与 -6k 额外上抛）。</summary>
        public readonly float DeltaVy;

        /// <summary>线性衰减系数（1 在爆心，0 在半径边缘）。</summary>
        public readonly float Falloff;

        public ExplosionHit(int index, float damage, float deltaVx, float deltaVy, float falloff)
        {
            Index = index;
            Damage = damage;
            DeltaVx = deltaVx;
            DeltaVy = deltaVy;
            Falloff = falloff;
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
    /// 爆炸范围伤害/击退结算。
    /// 对应逆向文档 §5.3（Explosion.as）：
    ///   radius = size/2 + 20
    ///   d = 目标到爆心距离；仅 d &lt;= radius 命中
    ///   falloff = 1 - d/radius
    ///   damage = maxDamage * falloff（无最小伤害保底，边缘趋 0）
    ///   k = 0.06 * falloff * maxDamage
    ///   deltaVx = nx * 5k, deltaVy = ny * 5k - 6k（总是额外上抛）
    ///   caster.evilness += Σ falloff
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
        /// 结算一次爆炸。
        /// </summary>
        /// <param name="size">爆炸尺寸参数（radius = size/2 + 20）</param>
        /// <param name="maxDamage">爆心处最大伤害</param>
        /// <param name="centerX">爆心 x</param>
        /// <param name="centerY">爆心 y</param>
        /// <param name="targets">候选目标（Alive=false 的会被跳过）</param>
        public static ExplosionResult Resolve(
            float size, float maxDamage, float centerX, float centerY,
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
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance > radius)
                {
                    continue;   // 未命中
                }

                float falloff;
                float nx;
                float ny;

                if (distance == 0f)
                {
                    // ★ d == 0 显式处置（原版 dx/d 会除零得 NaN，导致速度变 NaN 后物理失控）：
                    //   falloff = 1 —— 目标正处爆心，理应受满额伤害，取衰减公式在 d→0 时的极限；
                    //   方向取零向量 (0,0) —— 爆心处没有可定义的"向外"方向，不臆造推力方向；
                    //   于是 deltaVx = 0，deltaVy = -6k（仅保留公式中"总是额外上抛"的分量）。
                    //   理由：可用定义（定向、无 NaN、可测试）优先于隐式 NaN 传播。
                    falloff = 1f;
                    nx = 0f;
                    ny = 0f;
                }
                else
                {
                    falloff = 1f - distance / radius;
                    nx = dx / distance;
                    ny = dy / distance;
                }

                float damage = maxDamage * falloff;
                float k = KnockbackCoefficient * falloff * maxDamage;

                float deltaVx = nx * KnockbackMultiplier * k;
                float deltaVy = ny * KnockbackMultiplier * k - ExtraLiftCoefficient * k;

                hits.Add(new ExplosionHit(i, damage, deltaVx, deltaVy, falloff));
                evilnessGain += falloff;
            }

            return new ExplosionResult(hits.ToArray(), evilnessGain);
        }
    }
}
