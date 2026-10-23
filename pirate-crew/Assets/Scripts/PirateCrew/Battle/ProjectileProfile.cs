using PirateCrew.Combat;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>弹体外观图元（程序化兜底构建用；仅表现，不参与物理）。</summary>
    public enum ProjectileShape
    {
        /// <summary>球体（标准炸弹的外观图元）。</summary>
        Sphere = 0,

        /// <summary>立方体（保留的图元档位，当前单一弹体路径不产出）。</summary>
        Box = 1,
    }

    /// <summary>
    /// 弹体运行参数（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【单一弹体】全部 <see cref="WeaponId"/> 都映射到同一份<b>标准小炸弹</b>参数
    /// （<see cref="StandardBombRules"/>：抛出 → 重力弹道 → 撞地/静止 → 爆炸结算）。
    /// <see cref="FromStats"/> 的入参 <paramref name="stats"/> 只是数据锚
    /// （寿命投影仍消费 <c>LimitedToTurn</c> 等目录字段），物理/尺寸/爆炸数值一律取标准口径，
    /// 不再按武器分叉——「点哪把都是它」由此在参数层成立。
    ///
    /// 【投影字段（无运行时消费者，保留待武器重做）】
    ///   弹跳/摩擦投影族（PhysicsMaterial 三件 + 撞地积分两件）已按创始人 2026-10-23 裁决删除，
    ///   与武器重做一起重立（覆写表 ProjectileContactOverrides / ProjectileContactRules 保留）。
    ///   TwangMax、IsPersistent / IsPlaceable / PlaceableCount / ReuseCount、HitsTiles、
    ///   HasExplosion / ExplosionSize / ExplosionMaxDamage 目前没有运行时读点
    ///   （物理材质未接线、弹跳摩擦走 <see cref="ProjectileContactRules"/> 按武器 id 直查、
    ///   爆炸数值走 <see cref="StandardBombRules"/>）——它们是武器重做 schema 的承载位
    ///   （每武器覆写/常驻/放置的接线面，见 docs/项目/待办/武器系统重做.md），<b>不删</b>；
    ///   运行时现役消费的字段只有 Mass / GravityScale / UsesGravity 与 ColliderWidth/Height/Depth、Shape。
    ///
    /// 【职责边界】本结构只把标准口径翻译成「PhysX 需要什么」：
    ///   半尺寸 → Collider 半尺寸；Weight → 质量 / 重力倍率；
    ///   Bounce/Friction → PhysicsMaterial；以及寿命投影。
    ///   实例化、碰撞回调与物理积分由 <c>WeaponProjectile</c>（MonoBehaviour）负责。
    ///
    /// 【3D 轴映射】HalfWidth → 世界 X（横向）、HalfHeight → 世界 Y（竖直）、
    ///   HalfDepth → 世界 Z（纵深，取与半宽同值以避免侧向穿模）。重力作用在世界 <b>-Y</b>。
    /// </summary>
    public readonly struct ProjectileProfile
    {
        /// <summary>武器 id（数据锚原样透传，供日志/调试辨识"哪把武器扔出了这颗标准炸弹"）。</summary>
        public readonly WeaponId Id;

        /// <summary>碰撞体半宽（世界单位）。</summary>
        public readonly float HalfWidth;

        /// <summary>碰撞体半高（世界单位）。</summary>
        public readonly float HalfHeight;

        /// <summary>碰撞体半深（世界单位）；战斗平面深度，取与半宽同值以避免侧向穿模。</summary>
        public readonly float HalfDepth;

        /// <summary>Rigidbody 质量（标准口径 = 标准炸弹重量）。</summary>
        public readonly float Mass;

        /// <summary>是否吃全局重力（标准炸弹恒 true）。</summary>
        public readonly bool UsesGravity;

        /// <summary>重力倍率（相对全局 Physics.gravity 的口径）。</summary>
        public readonly float GravityScale;

        /// <summary>弹弓最大初速（标准口径 = 角色自抛同限）。</summary>
        public readonly float TwangMax;

        /// <summary>true = 跨回合常驻（寿命投影：目录 <c>LimitedToTurn=false</c> 的武器；标准炸弹路径不产常驻弹）。</summary>
        public readonly bool IsPersistent;

        /// <summary>是否放置类（单一弹体路径恒 false：放置机制已收敛为通用抛掷）。</summary>
        public readonly bool IsPlaceable;

        /// <summary>一次放置数量（单一弹体路径恒 0）。</summary>
        public readonly int PlaceableCount;

        /// <summary>库存侧复用次数（寿命投影：目录 <c>MaxReuses</c>，0 视为 1）。</summary>
        public readonly int ReuseCount;

        /// <summary>是否参与地形碰撞（标准炸弹恒 true）。</summary>
        public readonly bool HitsTiles;

        /// <summary>是否带爆炸（标准炸弹恒 true）。</summary>
        public readonly bool HasExplosion;

        /// <summary>爆炸 size（radius = size/2 + padding，标准口径）。</summary>
        public readonly float ExplosionSize;

        /// <summary>爆炸中心最大伤害（标准口径）。</summary>
        public readonly float ExplosionMaxDamage;

        /// <summary>外观图元（标准炸弹恒球体）。</summary>
        public readonly ProjectileShape Shape;

        public ProjectileProfile(
            WeaponId id, float halfWidth, float halfHeight, float halfDepth,
            float mass, bool usesGravity, float gravityScale,
            float twangMax, bool isPersistent, bool isPlaceable, int placeableCount,
            int reuseCount, bool hitsTiles, bool hasExplosion,
            float explosionSize, float explosionMaxDamage, ProjectileShape shape)
        {
            Id = id;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            HalfDepth = halfDepth;
            Mass = mass;
            UsesGravity = usesGravity;
            GravityScale = gravityScale;
            TwangMax = twangMax;
            IsPersistent = isPersistent;
            IsPlaceable = isPlaceable;
            PlaceableCount = placeableCount;
            ReuseCount = reuseCount;
            HitsTiles = hitsTiles;
            HasExplosion = hasExplosion;
            ExplosionSize = explosionSize;
            ExplosionMaxDamage = explosionMaxDamage;
            Shape = shape;
        }

        /// <summary>碰撞体全宽（世界单位）；BoxCollider.size.x 用。</summary>
        public float ColliderWidth => HalfWidth * 2f;

        /// <summary>碰撞体全高（世界单位）；BoxCollider.size.y 用。</summary>
        public float ColliderHeight => HalfHeight * 2f;

        /// <summary>碰撞体全深（世界单位）；BoxCollider.size.z 用。</summary>
        public float ColliderDepth => HalfDepth * 2f;

        /// <summary>
        /// 由 <see cref="WeaponStats"/> 推导弹体运行参数（纯函数，全工程唯一入口）。
        /// 物理/尺寸/爆炸全部取 <see cref="StandardBombRules"/> 标准口径；
        /// <paramref name="stats"/> 只贡献寿命投影（<c>LimitedToTurn</c>/<c>MaxReuses</c> 数据锚）。
        /// </summary>
        public static ProjectileProfile FromStats(WeaponStats stats)
        {
            float half = LevelGeometry.PixelsToUnits(StandardBombRules.HalfSizePixels);
            float mass = StandardBombRules.Weight;

            return new ProjectileProfile(
                stats.Id,
                half,
                half,
                half,
                mass,
                usesGravity: true,
                StandardBombRules.Weight,
                StandardBombRules.TwangMax,
                !stats.LimitedToTurn,
                isPlaceable: false,
                0,
                stats.MaxReuses > 0 ? stats.MaxReuses : 1,
                hitsTiles: true,
                hasExplosion: true,
                StandardBombRules.ExplosionSize,
                StandardBombRules.ExplosionMaxDamage,
                ProjectileShape.Sphere);
        }
    }
}
