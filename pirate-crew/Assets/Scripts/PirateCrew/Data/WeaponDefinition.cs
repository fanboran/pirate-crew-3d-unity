using UnityEngine;

namespace PirateCrew.Data
{
    /// <summary>
    /// 单件武器的 ScriptableObject 定义（Unity 侧可调参载体）。
    ///
    /// 【出处】静态逆向文档 §5.2「武器总表」。
    ///
    /// 【三层数值架构】
    ///   真值来源 = 纯 C# 的 <see cref="WeaponCatalog"/>（可无头测试）；
    ///   本类只是它在 Unity 里的序列化投影，供策划调参与 <c>[SerializeField]</c> 引用；
    ///   Editor 侧的 <c>DataAssetGenerator</c> 负责把 Catalog 写成 <c>.asset</c>。
    ///   因此<b>不要在本类里另写一份数值默认值</b>——默认值只在 Catalog 里维护，
    ///   生成器通过 <see cref="Apply"/> 把 Catalog 的值灌进来。
    /// </summary>
    [CreateAssetMenu(menuName = "PirateCrew/Data/武器定义", fileName = "WeaponDefinition")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("标识")]
        [SerializeField]
        [Tooltip("武器 id（§5.2 表里的英文 id）。用于代码索引，应与 WeaponCatalog 的键一致。")]
        WeaponId id;

        [SerializeField]
        [Tooltip("显示名；沿用原版英文类名 / 关卡 XML 属性键（如 cherryBomb）。")]
        string displayName;

        [Header("物理（§5.2 / §4.1）")]
        [SerializeField]
        [Tooltip("水平 AABB 半径（px）。§5.2：cannonball=10、cherryBomb=9、dynamite=11、boulder=31、banana=7、mine=14、parachuteBomb=11、rumBottle=14、piecesOfEight=7、箱体=16、anchor 左右=48。")]
        float aabbRadius;

        [SerializeField]
        [Tooltip("垂直 AABB 半径（px）。§5.2：gunpowderBarrel / woodenCrate 为 15（原表 16/16/15/15）；anchor 上=96、下=0；其余与水平同值或原表为「—」。")]
        float aabbVerticalRadius;

        [SerializeField]
        [Tooltip("摩擦力。§5.2：cannonball/cherryBomb/parachuteBomb/rumBottle/piecesOfEight=0.3、dynamite=1.7、boulder=0.25、banana=0.5、mine=1.5；箱体/锚/海鸥等原表为「—」。")]
        float friction;

        [SerializeField]
        [Tooltip("重量（重力增量）。§5.2：cannonball=0、boulder=1.5、seagull/tidalWave/cannon=0、其余多为 1。")]
        float weight;

        [SerializeField]
        [Tooltip("弹跳系数（撞地 vy *= -bounce）。§5.2：banana=0.8（最弹），其余多为 0.2。")]
        float bounce;

        [SerializeField]
        [Tooltip("弹弓最大初速。§5.2：20 或 30；原表为「—」的武器记 0（如 cannonball 由 cannon 决定速度）。")]
        float twangMax;

        [Header("触发与爆炸（§5.2 / §5.3）")]
        [SerializeField]
        [Tooltip("引爆 / 触发条件（可组合）。§5.2「触发/引爆条件」列。")]
        WeaponTrigger trigger;

        [SerializeField, Tooltip("爆炸范围 size。§5.2 爆炸列；0 表示无爆炸（原表为「—」）。")]
        float explosionSize;

        [SerializeField, Tooltip("爆炸中心最大伤害 maxDamage。§5.2；0 表示无爆炸。伤害公式见 §5.3：damage = maxDamage * (1 - d/(size/2+20))。")]
        float explosionMaxDamage;

        [Header("回合与拖拽")]
        [SerializeField]
        [Tooltip("true = 仅限本回合（发射/使用后销毁）；false = 跨回合常驻（§5.2：mine / gunpowderBarrel / woodenCrate / cannon 为 false）。")]
        bool limitedToTurn;

        [SerializeField]
        [Tooltip("拖拽半径（仅范围圈显示与拖拽判定，与力度无关）。§5.1：通用 130、地雷 180；原表未单列者记 0。")]
        float dragRange;

        [Header("备注")]
        [SerializeField]
        [Tooltip("特殊行为与出处追注（如 piecesOfEight 可复用 8 次、anchor 60 固定伤害、seagull 每发 50/50、tidalWave 每帧 5 点、SweepingFlame 每段 30 点）。")]
        [TextArea(2, 6)]
        string remark;

        // 只读访问器：供玩法层读取（避免直接暴露可写字段给外部脚本）。
        public WeaponId Id => id;
        public string DisplayName => displayName;
        public float AabbRadius => aabbRadius;
        public float AabbVerticalRadius => aabbVerticalRadius;
        public float Friction => friction;
        public float Weight => weight;
        public float Bounce => bounce;
        public float TwangMax => twangMax;
        public WeaponTrigger Trigger => trigger;
        public float ExplosionSize => explosionSize;
        public float ExplosionMaxDamage => explosionMaxDamage;
        public bool LimitedToTurn => limitedToTurn;
        public float DragRange => dragRange;
        public string Remark => remark;

        /// <summary>
        /// 把 <see cref="WeaponCatalog"/> 的数值灌入本资产（生成器幂等覆盖用）。
        /// 数值只此一处来源，避免生成器里再硬编码第二份副本。
        /// </summary>
        public void Apply(WeaponStats stats)
        {
            id = stats.Id;
            displayName = stats.DisplayName;
            aabbRadius = stats.AabbRadius;
            aabbVerticalRadius = stats.AabbVerticalRadius;
            friction = stats.Friction;
            weight = stats.Weight;
            bounce = stats.Bounce;
            twangMax = stats.TwangMax;
            trigger = stats.Trigger;
            explosionSize = stats.ExplosionSize;
            explosionMaxDamage = stats.ExplosionMaxDamage;
            limitedToTurn = stats.LimitedToTurn;
            dragRange = stats.DragRange;
            remark = stats.Remark;
        }
    }
}
