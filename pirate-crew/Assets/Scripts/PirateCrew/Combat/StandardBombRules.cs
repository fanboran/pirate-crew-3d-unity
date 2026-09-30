using PirateCrew.Data;

namespace PirateCrew.Combat
{
    /// <summary>
    /// 标准小炸弹的单一规则源（纯 C# 静态类）。
    ///
    /// 【定位】武器系统的行为口径收敛后，全部 <see cref="WeaponId"/> 都映射到同一份
    /// 标准炸弹：抛出 → 重力弹道 → 撞地/静止 → 按 <see cref="ExplosionResolver"/> 结算爆炸
    /// （击退、固定抬升、邪恶度、连锁引爆全部沿用该结算，不在此重复）。
    /// 「点哪把都是它」——武器面板/武器栈/WeaponId 只是选择外壳，入场的弹体行为由本类唯一规定。
    ///
    /// 【数值来源】一切可调数值单源引用 <see cref="BalanceConfig.Defaults"/>，
    /// 本类不另立第二份真值；引擎换算（px→世界单位、FlashSpeedScale）留在
    /// <c>Battle/ProjectileProfile</c>（Battle → Combat 单向依赖，本类不反向引用）。
    ///
    /// 【提案/待定】标准炸弹口径的占位空壳期数值见 Defaults 对应常量的标注；
    /// 重做批次裁决后逐值替换，改动只发生在 Defaults 一处。
    /// </summary>
    public static class StandardBombRules
    {
        /// <summary>
        /// 弹体碰撞半尺寸（Flash px）。AABB 全表普遍 7–16px，取 8px 档（沿用弹体兜底口径，
        /// <b>【提案/待定：标准炸弹口径，占位空壳期数值】</b>）。
        /// </summary>
        public const float HalfSizePixels = 8f;

        /// <summary>重量：与角色自重同档（有重力、PhysX 质量即此值）。</summary>
        public const float Weight = CrewCatalog.Weight;

        /// <summary>爆炸尺寸参数（<c>radius = size/2 + padding</c>，取保底档 100）。</summary>
        public static float ExplosionSize => BalanceConfig.Defaults.StandardBombExplosionSize;

        /// <summary>爆心最大伤害（取保底档 50）。</summary>
        public static float ExplosionMaxDamage => BalanceConfig.Defaults.StandardBombDamage;

        /// <summary>
        /// PhysicsMaterial 弹跳/摩擦取全局物理默认（<see cref="BalanceConfig.Defaults"/>）。
        /// </summary>
        public static float Bounciness => BalanceConfig.Defaults.DefaultBounce;
        public static float Friction => BalanceConfig.Defaults.DefaultFriction;

        /// <summary>弹弓最大初速：与角色自抛同限（<see cref="BalanceConfig.Defaults.DefaultTwangMax"/>）。</summary>
        public static float TwangMax => BalanceConfig.Defaults.DefaultTwangMax;

        /// <summary>
        /// 引爆方式：接触即爆 <b>或</b> 静止即爆（落地/静止爆炸，二 Trigger 并列、任一满足即引爆）。
        /// 判定走 <c>ProjectileTriggerRules.ShouldDetonateThisFrame</c> 既有桥，不另写判定。
        /// 全限定写法：本命名空间里有同名的旧 <c>Combat.WeaponTrigger</c> 枚举（非 Flags），
        /// 不限定名会命中它——触发语义必须锚定目录档 <c>Data.WeaponTrigger</c>。
        /// </summary>
        public static readonly global::PirateCrew.Data.WeaponTrigger Trigger =
            global::PirateCrew.Data.WeaponTrigger.OnContact | global::PirateCrew.Data.WeaponTrigger.OnRest;
    }
}
