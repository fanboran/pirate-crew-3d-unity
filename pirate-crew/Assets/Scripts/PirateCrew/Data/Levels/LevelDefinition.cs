using UnityEngine;

namespace PirateCrew.Data
{
    /// <summary>
    /// 单场战斗（非海图）的关卡资产：编成 / luck / 武器池 / 逻辑高度场 / 烘焙件摆位。
    ///
    /// 【唯一真源】本资产是这些数据的唯一真源；运行时（<c>SceneArt.ShowcaseLevels</c> 的外观）
    /// 与 golden JSON 都从它派生。数据内容只能由迁移器从 `_golden/*.json` 写入，
    /// 不允许手写 C# 常量（见 <c>docs/技术/架构/关卡数据资产.md</c>）。
    ///
    /// 【为什么只有一个 <see cref="data"/> 字段】字段真源放在纯 C# 载荷
    /// <see cref="LevelAssetPayload"/> 上，无头验证台才能在**不实例化 SO**（原生 ECall 必崩）
    /// 的前提下算「资产内容 == golden JSON」。SO 只做宿主与 Unity 引用入口。
    /// </summary>
    [CreateAssetMenu(menuName = "PirateCrew/Data/关卡定义", fileName = "LevelDefinition")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("关卡数据载荷。由迁移器（PirateCrew/关卡/迁移关卡资产）从 golden JSON 写入。")]
        LevelAssetPayload data = new LevelAssetPayload();

        /// <summary>数据载荷（永不 null）。</summary>
        public LevelAssetPayload Data => data ?? (data = new LevelAssetPayload());

        /// <summary>关卡序号。</summary>
        public int LevelNumber => Data.levelNumber;

        /// <summary>显示名。</summary>
        public string DisplayName => Data.displayName;

        /// <summary>迁移器 / 校验器写入入口（幂等覆盖，不产生第二份数值）。</summary>
        public void Apply(LevelAssetPayload payload)
        {
            data = payload ?? new LevelAssetPayload();
        }
    }
}
