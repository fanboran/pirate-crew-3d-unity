using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>弹体外观图元（程序化兜底构建用；仅表现，不参与物理）。</summary>
    public enum ProjectileShape
    {
        /// <summary>球体（多数圆形武器）。</summary>
        Sphere = 0,

        /// <summary>立方体（§5.2 的 BoxWeapon：gunpowderBarrel / woodenCrate）。</summary>
        Box = 1,
    }

    /// <summary>弹体落水后的处置（§5.2「飞出地图或落水即消失 / 接触·落水引爆」）。</summary>
    public enum ProjectileWaterBehavior
    {
        /// <summary>落水即消失（不引爆）。</summary>
        Vanish = 0,

        /// <summary>落水即引爆。</summary>
        Detonate = 1,
    }

    /// <summary>
    /// 弹体运行参数（纯 C#，不引用 MonoBehaviour / GameObject）。
    ///
    /// 【出处】静态逆向文档 §5.2「武器总表」逐列（AABB 半径 / 摩擦 / 重量 / 弹跳 / 爆炸 size·maxDamage /
    ///         limitedToTurn / 放置数量 / 复用次数），换算口径来自 §5.1（px→世界单位）与
    ///         <see cref="LevelGeometry"/>(1 瓦片 = 32px = 1 世界单位)。
    ///
    /// 【职责边界】本结构只把 <see cref="WeaponStats"/> 翻译成「PhysX 需要什么」：
    ///   AABB 半径 → Collider 半尺寸；  Weight → 质量 / 是否吃重力 / 重力倍率；
    ///   Bounce → PhysicsMaterial.bounciness；Friction → PhysicsMaterial 摩擦系数；
    ///   以及生命周期（是否跨回合常驻 / 放置数量 / 复用次数 / 是否参与瓦片碰撞 / 落水处置）。
    ///   实例化、碰撞回调与物理积分由 <c>WeaponProjectile</c>（MonoBehaviour）负责。
    ///
    /// 【3D 轴映射】HalfWidth → 世界 X（横向）、HalfHeight → 世界 Y（竖直）、
    ///   HalfDepth → 世界 Z（纵深）。UsesGravity / GravityScale 作用在世界 <b>-Y</b>
    ///   （<c>Physics.gravity</c>）；水平面 (X,Z) 不受重力，故 Bounce/Friction 只在
    ///   弹体与 XZ 地面接触时起弹跳/减速作用（重力竖直、与地面正交）。
    ///
    /// 【映射决策（数值未在文档中给出时，取可定义的最简口径并注明）】
    ///   · 质量：Weight &gt; 0 时取 Weight（boulder=1.5）；Weight==0 的武器（cannonball 等）
    ///     PhysX 不接受 0 质量，取 1 并令 <see cref="UsesGravity"/> = false（文档明确「无重力」）。
    ///   · 摩擦：Flash 的 friction 是「着地时每帧 |vx| 递减量」（§5.4），并非摩擦系数。
    ///     M2 近似：直接作为 PhysicsMaterial.dynamicFriction / staticFriction（同值），
    ///     精确的每帧递减语义留待运行时手感调参（见 <c>WeaponProjectile</c> 类头 TODO）。
    ///     3D 下该摩擦作用于弹体与 XZ 地面的接触，减速的是水平 (X,Z) 速度分量。
    /// </summary>
    public readonly struct ProjectileProfile
    {
        /// <summary>武器 id。</summary>
        public readonly WeaponId Id;

        /// <summary>碰撞体半宽（世界单位）。</summary>
        public readonly float HalfWidth;

        /// <summary>碰撞体半高（世界单位）。</summary>
        public readonly float HalfHeight;

        /// <summary>碰撞体半深（世界单位）；3D 化的战斗平面深度，取与半宽同值以避免侧向穿模。</summary>
        public readonly float HalfDepth;

        /// <summary>Rigidbody 质量（见类头映射决策）。</summary>
        public readonly float Mass;

        /// <summary>是否吃全局重力（Weight &gt; 0）。</summary>
        public readonly bool UsesGravity;

        /// <summary>重力倍率（相对全局 Physics.gravity 的 weight=1 口径）。</summary>
        public readonly float GravityScale;

        /// <summary>PhysicsMaterial 弹性系数（= §5.2 Bounce）。</summary>
        public readonly float Bounciness;

        /// <summary>PhysicsMaterial 动摩擦系数（= §5.2 Friction 近似，见类头）。</summary>
        public readonly float DynamicFriction;

        /// <summary>PhysicsMaterial 静摩擦系数（与动摩擦同值）。</summary>
        public readonly float StaticFriction;

        /// <summary>弹弓最大初速（0 = 非弹弓发射；cannonball 由保底规则特殊处理）。</summary>
        public readonly float TwangMax;

        /// <summary>true = 跨回合常驻（§5.2 limitedToTurn=false：mine / gunpowderBarrel / woodenCrate / cannon）。</summary>
        public readonly bool IsPersistent;

        /// <summary>是否放置类（PlaceableCount &gt; 0）。</summary>
        public readonly bool IsPlaceable;

        /// <summary>一次放置数量（§5.2：woodenCrate 3、gunpowderBarrel 2）。</summary>
        public readonly int PlaceableCount;

        /// <summary>库存侧复用次数（§5.2 MaxReuses；0 视为 1）。</summary>
        public readonly int ReuseCount;

        /// <summary>是否参与瓦片碰撞（§5.2：仅 seagull / tidalWave 明确 hitsTiles=false，其余默认 true）。</summary>
        public readonly bool HitsTiles;

        /// <summary>是否带爆炸。</summary>
        public readonly bool HasExplosion;

        /// <summary>爆炸 size（§5.3 radius = size/2 + 20）。</summary>
        public readonly float ExplosionSize;

        /// <summary>爆炸中心最大伤害（§5.3）。</summary>
        public readonly float ExplosionMaxDamage;

        /// <summary>外观图元。</summary>
        public readonly ProjectileShape Shape;

        public ProjectileProfile(
            WeaponId id, float halfWidth, float halfHeight, float halfDepth,
            float mass, bool usesGravity, float gravityScale,
            float bounciness, float dynamicFriction, float staticFriction,
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
            Bounciness = bounciness;
            DynamicFriction = dynamicFriction;
            StaticFriction = staticFriction;
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
        /// </summary>
        public static ProjectileProfile FromStats(WeaponStats stats)
        {
            float halfWidth = LevelGeometry.PixelsToUnits(stats.AabbRadius);
            float halfHeight = LevelGeometry.PixelsToUnits(stats.AabbVerticalRadius > 0f
                ? stats.AabbVerticalRadius
                : stats.AabbRadius);   // 原表为「—」时垂直半径取水平同值

            bool usesGravity = stats.Weight > 0f;
            float mass = stats.Weight > 0f ? stats.Weight : 1f;

            int reuse = stats.MaxReuses > 0 ? stats.MaxReuses : 1;

            return new ProjectileProfile(
                stats.Id,
                halfWidth,
                halfHeight,
                halfWidth,
                mass,
                usesGravity,
                usesGravity ? stats.Weight : 0f,
                Mathf.Clamp(stats.Bounce, 0f, 1f),
                Mathf.Max(0f, stats.Friction),
                Mathf.Max(0f, stats.Friction),
                stats.TwangMax,
                !stats.LimitedToTurn,
                stats.PlaceableCount > 0,
                stats.PlaceableCount,
                reuse,
                HitsTilesFor(stats.Id),
                stats.HasExplosion,
                stats.ExplosionSize,
                stats.ExplosionMaxDamage,
                ShapeFor(stats.Id));
        }

        /// <summary>
        /// 是否由本任务的「通用弹体」路径实现（§5.2 大多数武器）。
        ///
        /// 未纳入的 6 种走专用机制，本次不做（TODO 见各自备注）：
        ///   anchor（点击放置 + 固定伤害）、seagull（点击选高 + 投弹）、tidalWave（点击横扫持续伤害）、
        ///   voodooDoll（锁定目标 + 速度转移）、cannon（放置 + 蓄力发射）、SweepingFlame（落地生成火）。
        /// </summary>
        public static bool SupportsGenericProjectile(WeaponStats stats)
        {
            switch (stats.Id)
            {
                case WeaponId.Cannonball:
                case WeaponId.CherryBomb:
                case WeaponId.Dynamite:
                case WeaponId.Boulder:
                case WeaponId.Banana:
                case WeaponId.Mine:
                case WeaponId.ParachuteBomb:
                case WeaponId.RumBottle:
                case WeaponId.PiecesOfEight:
                case WeaponId.GunpowderBarrel:
                case WeaponId.WoodenCrate:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 是否可由弹弓发射。TwangMax &gt; 0 的武器直接满足；
        /// cannonball 原表 twangMax 为「—」，但作为保底武器必须能被弹弓抛出（§3.2/§5.5），故特殊放行。
        /// 放置类（gunpowderBarrel / woodenCrate）虽 TwangMax=0，走「放置」而非弹弓发射。
        /// </summary>
        public static bool CanBeSlingLaunched(WeaponStats stats)
        {
            return stats.TwangMax > 0f || stats.Id == WeaponId.Cannonball;
        }

        /// <summary>落水处置（§5.2 备注逐条）。</summary>
        public static ProjectileWaterBehavior WaterBehavior(WeaponStats stats)
        {
            // cannonball：文档明确「落水即消失」（不引爆）。
            if (stats.Id == WeaponId.Cannonball)
                return ProjectileWaterBehavior.Vanish;

            // dynamite：文档「落水后变 unlit，仍按静止判定爆炸（水中下沉不静止）」——
            // 即落水并不立即爆。M2 近似为消失；精确的 unlit 状态留 TODO（见 WeaponProjectile）。
            if (stats.Id == WeaponId.Dynamite)
                return ProjectileWaterBehavior.Vanish;

            return stats.HasExplosion
                ? ProjectileWaterBehavior.Detonate
                : ProjectileWaterBehavior.Vanish;
        }

        /// <summary>是否参与瓦片碰撞（§5.2 仅 seagull / tidalWave 记 hitsTiles=false）。</summary>
        public static bool HitsTilesFor(WeaponId id)
        {
            return id != WeaponId.Seagull && id != WeaponId.TidalWave;
        }

        /// <summary>外观图元：BoxWeapon（箱体）用立方体，其余用球体。</summary>
        public static ProjectileShape ShapeFor(WeaponId id)
        {
            return id == WeaponId.GunpowderBarrel || id == WeaponId.WoodenCrate
                ? ProjectileShape.Box
                : ProjectileShape.Sphere;
        }

        /// <summary>是否为箱体类（AABB 非正圆，垂直半径小于水平）。</summary>
        public static bool IsBoxWeapon(WeaponId id)
        {
            return id == WeaponId.GunpowderBarrel || id == WeaponId.WoodenCrate;
        }
    }
}
