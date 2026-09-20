using UnityEngine;

namespace PirateCrew.Data
{
    /// <summary>
    /// 全局平衡常数的 ScriptableObject 汇总。
    ///
    /// 【出处】静态逆向文档：§5.1（投掷）、§5.3（爆炸伤害/击退）、§5.2（摩擦/弹跳默认）、
    ///         §3.1（10 帧无活动推进）、§1（25 fps）、§7.3（分数公式）、§4.1（角色 AABB 半宽高）。
    ///
    /// 【单一来源】默认值只写在嵌套的 <see cref="Defaults"/> 里；
    /// SO 字段的初值引用它，生成器调用 <see cref="ApplyDefaults"/> 做幂等覆盖，
    /// 从而避免在资产 / 生成器里再出现第二份硬编码副本。
    /// </summary>
    [CreateAssetMenu(menuName = "PirateCrew/Data/平衡常数", fileName = "BalanceConfig")]
    public class BalanceConfig : ScriptableObject
    {
        /// <summary>平衡常数的真值来源（纯 C#，无头验证台可断言）。</summary>
        public static class Defaults
        {
            /// <summary>§5.1：初速 = 0.25 × 拖拽距离；方向与光标偏移相反。</summary>
            public const float TwangForceScale = 0.25f;

            /// <summary>§5.1：默认 twangMaxForce = 20（满力拖拽距离 20/0.25 = 80px）。</summary>
            public const float DefaultTwangMax = 20f;

            /// <summary>§5.1：高弹弓上限 30（香蕉 / 跳伞炸弹 / 朗姆酒瓶；满力距离 120px）。</summary>
            public const float HighTwangMax = 30f;

            /// <summary>§5.3：爆炸半径 <c>radius = size/2 + 20</c>。</summary>
            public const float ExplosionRadiusPadding = 20f;

            /// <summary>§5.3：击退系数 <c>k = 0.06 * falloff * maxDamage</c>。</summary>
            public const float KnockbackCoefficient = 0.06f;

            /// <summary>§5.3：击退水平分量倍率（<c>dir * 5k</c>）。</summary>
            public const float KnockbackHorizontal = 5f;

            /// <summary>§5.3：击退垂直分量倍率（<c>dir * 5k - 6k</c>，总是额外上抛）。</summary>
            public const float KnockbackVertical = 6f;

            /// <summary>§4.1 / §5.2：默认摩擦 2。</summary>
            public const float DefaultFriction = 2f;

            /// <summary>§4.1 / §5.2：默认弹跳 0.2。</summary>
            public const float DefaultBounce = 0.2f;

            /// <summary>§3.1：全局 inactivity 超过 10 帧（≈0.4s 无活动）推进回合。</summary>
            public const int InactivityFramesToAdvance = 10;

            /// <summary>§1：原版帧率 25 fps（所有"每帧"量纲都基于它）。</summary>
            public const int OriginalFps = 25;

            /// <summary>§7.3：关卡得分 <c>floor(平均血量*20 - 回合数*25)</c> 的血量权重。</summary>
            public const float ScoreHealthWeight = 20f;

            /// <summary>§7.3：回合数惩罚系数（每回合 -25）。</summary>
            public const float ScoreTurnPenalty = 25f;

            /// <summary>§7.3：得分下限系数（下限 = 关卡序号 × 10）。</summary>
            public const float ScoreFloorPerLevel = 10f;

            /// <summary>§4.1：角色 AABB 半宽 6。</summary>
            public const float CharHalfWidth = 6f;

            /// <summary>§4.1：角色 AABB 半高 8。</summary>
            public const float CharHalfHeight = 8f;
        }

        [Header("投掷（§5.1）")]
        [SerializeField, Tooltip("初速 = 系数 × 拖拽距离。§5.1：0.25。")]
        float twangForceScale = Defaults.TwangForceScale;

        [SerializeField, Tooltip("默认弹弓最大初速。§5.1：20（满力拖拽距离 80px）。")]
        float defaultTwangMax = Defaults.DefaultTwangMax;

        [SerializeField, Tooltip("高弹弓上限（香蕉/跳伞炸弹/朗姆酒瓶）。§5.1：30（满力距离 120px）。")]
        float highTwangMax = Defaults.HighTwangMax;

        [Header("爆炸（§5.3）")]
        [SerializeField, Tooltip("爆炸半径附加量：radius = size/2 + 本值。§5.3：20。")]
        float explosionRadiusPadding = Defaults.ExplosionRadiusPadding;

        [SerializeField, Tooltip("击退系数 k = 系数 × falloff × maxDamage。§5.3：0.06。")]
        float knockbackCoefficient = Defaults.KnockbackCoefficient;

        [SerializeField, Tooltip("击退水平分量倍率（dir × 5k）。§5.3：5。")]
        float knockbackHorizontal = Defaults.KnockbackHorizontal;

        [SerializeField, Tooltip("击退垂直分量倍率（dir × 5k - 6k，总是额外上抛）。§5.3：6。")]
        float knockbackVertical = Defaults.KnockbackVertical;

        [Header("物理默认（§4.1 / §5.2）")]
        [SerializeField, Tooltip("默认摩擦。§4.1 / §5.2：2。")]
        float defaultFriction = Defaults.DefaultFriction;

        [SerializeField, Tooltip("默认弹跳。§4.1 / §5.2：0.2。")]
        float defaultBounce = Defaults.DefaultBounce;

        [Header("回合与帧率（§3.1 / §1）")]
        [SerializeField, Tooltip("全局 inactivity 达 N 帧（≈0.4s 无活动）后推进回合。§3.1：10。")]
        int inactivityFramesToAdvance = Defaults.InactivityFramesToAdvance;

        [SerializeField, Tooltip("原版帧率。§1：25 fps。")]
        int originalFps = Defaults.OriginalFps;

        [Header("分数（§7.3）")]
        [SerializeField, Tooltip("关卡得分血量权重：floor(平均血量 × 本值 - 回合数 × 惩罚)。§7.3：20。")]
        float scoreHealthWeight = Defaults.ScoreHealthWeight;

        [SerializeField, Tooltip("关卡得分回合惩罚系数（每回合 -25）。§7.3：25。")]
        float scoreTurnPenalty = Defaults.ScoreTurnPenalty;

        [SerializeField, Tooltip("得分下限系数（下限 = 关卡序号 × 本值）。§7.3：10。")]
        float scoreFloorPerLevel = Defaults.ScoreFloorPerLevel;

        [Header("角色 AABB（§4.1）")]
        [SerializeField, Tooltip("角色 AABB 半宽。§4.1：6。")]
        float charHalfWidth = Defaults.CharHalfWidth;

        [SerializeField, Tooltip("角色 AABB 半高。§4.1：8。")]
        float charHalfHeight = Defaults.CharHalfHeight;

        // 只读访问器
        public float TwangForceScale => twangForceScale;
        public float DefaultTwangMax => defaultTwangMax;
        public float HighTwangMax => highTwangMax;
        public float ExplosionRadiusPadding => explosionRadiusPadding;
        public float KnockbackCoefficient => knockbackCoefficient;
        public float KnockbackHorizontal => knockbackHorizontal;
        public float KnockbackVertical => knockbackVertical;
        public float DefaultFriction => defaultFriction;
        public float DefaultBounce => defaultBounce;
        public int InactivityFramesToAdvance => inactivityFramesToAdvance;
        public int OriginalFps => originalFps;
        public float ScoreHealthWeight => scoreHealthWeight;
        public float ScoreTurnPenalty => scoreTurnPenalty;
        public float ScoreFloorPerLevel => scoreFloorPerLevel;
        public float CharHalfWidth => charHalfWidth;
        public float CharHalfHeight => charHalfHeight;

        /// <summary>把全部字段重置为 <see cref="Defaults"/> 的真值（生成器幂等覆盖用）。</summary>
        public void ApplyDefaults()
        {
            twangForceScale = Defaults.TwangForceScale;
            defaultTwangMax = Defaults.DefaultTwangMax;
            highTwangMax = Defaults.HighTwangMax;
            explosionRadiusPadding = Defaults.ExplosionRadiusPadding;
            knockbackCoefficient = Defaults.KnockbackCoefficient;
            knockbackHorizontal = Defaults.KnockbackHorizontal;
            knockbackVertical = Defaults.KnockbackVertical;
            defaultFriction = Defaults.DefaultFriction;
            defaultBounce = Defaults.DefaultBounce;
            inactivityFramesToAdvance = Defaults.InactivityFramesToAdvance;
            originalFps = Defaults.OriginalFps;
            scoreHealthWeight = Defaults.ScoreHealthWeight;
            scoreTurnPenalty = Defaults.ScoreTurnPenalty;
            scoreFloorPerLevel = Defaults.ScoreFloorPerLevel;
            charHalfWidth = Defaults.CharHalfWidth;
            charHalfHeight = Defaults.CharHalfHeight;
        }
    }
}
