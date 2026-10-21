using System.Collections.Generic;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 直击作用的「命中带」几何（纯数据）：水平半宽 × 高度区间，Flash px 口径
    /// （与 <see cref="ExplosionResolver"/> 的距离口径同域）。
    /// 平面 X 为带的横轴（潮汐横扫带 / 船锚落点带皆此形），<see cref="HeightMin"/>/<see cref="HeightMax"/>
    /// 界定第三分量（世界高度换算后的 px）。平面纵深（Y）不参与判定——直击带默认横跨纵深，
    /// 纵深折叠口径属逐格定案项。
    /// </summary>
    public readonly struct DirectHitBand
    {
        /// <summary>带的水平半宽（px）：|targetX - bandX| &lt;= HalfWidth 命中。</summary>
        public readonly float HalfWidth;

        /// <summary>高度下限（px，含）。</summary>
        public readonly float HeightMin;

        /// <summary>高度上限（px，含）。</summary>
        public readonly float HeightMax;

        public DirectHitBand(float halfWidth, float heightMin, float heightMax)
        {
            HalfWidth = halfWidth;
            HeightMin = heightMin;
            HeightMax = heightMax;
        }
    }

    /// <summary>单个目标受到的直击结算结果（对应 <see cref="ExplosionHit"/> 的直击版）。</summary>
    public readonly struct DirectHit
    {
        /// <summary>该目标在传入列表中的索引，便于调用方回写。</summary>
        public readonly int Index;

        /// <summary>本次伤害。</summary>
        public readonly float Damage;

        public DirectHit(int index, float damage)
        {
            Index = index;
            Damage = damage;
        }
    }

    /// <summary>
    /// 直击（非爆炸）结算通道（纯 C# 静态，与 <see cref="ExplosionResolver"/> 并列成对）。
    ///
    /// 【定位】武器系统重做 §3.2 的地基两条——爆炸走球半径+线性衰减（ExplosionResolver），
    /// 直击<b>不走路由衰减</b>：
    ///   · 固定伤害型（<see cref="ResolveFixed"/>）：命中带内一律同一伤害、无衰减
    ///     （船锚类「命中带内恒定伤害」的将来宿主）；
    ///   · 速度比例型（<see cref="ResolveVelocityProportional"/>）：伤害与弹体速度成正比
    ///     （巨石类「碾压伤害与速度成正比」的将来宿主）。
    /// 击退/推开/速度继承等伴随效果属武器格个性机制，不进本通道。
    ///
    /// 【口径】坐标/高度与 <see cref="ExplosionTarget"/> 同域（Flash 平面 px + 世界高度换算 px），
    /// 候选目标结构直接复用它。速度比例型的速度入参由调用方负责换算到与伤害系数匹配的域
    /// （旧表口径为 Flash px/帧，逐格定案时裁决米制迁移）。
    /// </summary>
    public static class DirectHitResolver
    {
        /// <summary>
        /// 目标是否在命中带内（Alive 与否不在本判定内，由 Resolve 统一跳过）。
        /// 带以 bandX 为横轴中心：|targetX - bandX| &lt;= <see cref="DirectHitBand.HalfWidth"/>
        /// 且高度落在 [HeightMin, HeightMax]（闭区间，边界命中）。
        /// </summary>
        public static bool Contains(in DirectHitBand band, float bandX, float targetX, float targetHeight)
        {
            if (Mathf.Abs(targetX - bandX) > band.HalfWidth)
                return false;
            return targetHeight >= band.HeightMin && targetHeight <= band.HeightMax;
        }

        /// <summary>
        /// 固定伤害直击：带内全部存活目标受同一伤害，无衰减、无击退。
        /// </summary>
        /// <param name="band">命中带（Flash px）。</param>
        /// <param name="bandX">带的横轴中心（Flash 平面 x）。</param>
        /// <param name="fixedDamage">固定伤害值（逐格定案的调用方传入）。</param>
        /// <param name="targets">候选目标（Alive=false 跳过）。</param>
        public static DirectHit[] ResolveFixed(
            in DirectHitBand band, float bandX, float fixedDamage, IReadOnlyList<ExplosionTarget> targets)
        {
            if (targets == null)
                return new DirectHit[0];

            var hits = new List<DirectHit>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i].Alive || !Contains(band, bandX, targets[i].X, targets[i].Height))
                    continue;
                hits.Add(new DirectHit(i, fixedDamage));
            }

            return hits.ToArray();
        }

        /// <summary>
        /// 速度比例直击：带内全部存活目标受 damage = speed × <paramref name="damagePerSpeed"/>
        /// （伤害与弹体当时速度成正比；speed 为非负标量，调用方负责取模）。
        /// </summary>
        /// <param name="band">命中带（Flash px）。</param>
        /// <param name="bandX">带的横轴中心（Flash 平面 x）。</param>
        /// <param name="speed">弹体速度模长（与 damagePerSpeed 同域；&lt; 0 按 0 处理）。</param>
        /// <param name="damagePerSpeed">每单位速度的伤害系数（逐格定案的调用方传入）。</param>
        /// <param name="targets">候选目标（Alive=false 跳过）。</param>
        public static DirectHit[] ResolveVelocityProportional(
            in DirectHitBand band, float bandX, float speed, float damagePerSpeed,
            IReadOnlyList<ExplosionTarget> targets)
        {
            if (targets == null)
                return new DirectHit[0];

            float damage = Mathf.Max(0f, speed) * damagePerSpeed;
            var hits = new List<DirectHit>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i].Alive || !Contains(band, bandX, targets[i].X, targets[i].Height))
                    continue;
                hits.Add(new DirectHit(i, damage));
            }

            return hits.ToArray();
        }
    }
}
