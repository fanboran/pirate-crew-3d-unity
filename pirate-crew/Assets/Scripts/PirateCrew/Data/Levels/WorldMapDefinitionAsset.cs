using UnityEngine;

namespace PirateCrew.Data
{
    /// <summary>
    /// 一张大海域海图的数据资产（跨图字段 / 氛围档 / terrain[] / horizon[] / props[] / spawns[] /
    /// 分层军火 / 远景特征）。
    ///
    /// 【与运行时类型的分工】资产 = <see cref="WorldMapAssetPayload"/>（可序列化载荷，本类宿主）；
    /// 运行时 = <c>PirateCrew.Battle.WorldMaps.WorldMapDefinition</c>（既有纯 C# 载体，语义不变，
    /// 由 <c>WorldMapFromAsset</c> 从载荷转出）。纯 C# 规则层（连通性/栅格化/远景环）继续只认后者，
    /// 于是"数据换载体"与"规则不变"彻底解耦。
    ///
    /// 【站面平直】可站立面不落本资产——它来自 kit 资产自己的 standable 表
    /// （<c>WorldMapStandables</c>，由 Blender 建模脚本产出）。本资产的 <c>terrain[]</c> 只是摆位。
    /// </summary>
    [CreateAssetMenu(menuName = "PirateCrew/Data/海图定义", fileName = "WorldMapDefinitionAsset")]
    public sealed class WorldMapDefinitionAsset : ScriptableObject
    {
        [SerializeField, Tooltip("海图数据载荷。由迁移器（PirateCrew/关卡/迁移关卡资产）从 golden JSON 写入。")]
        WorldMapAssetPayload data = new WorldMapAssetPayload();

        /// <summary>数据载荷（永不 null）。</summary>
        public WorldMapAssetPayload Data => data ?? (data = new WorldMapAssetPayload());

        /// <summary>海图 id（选关/战役结算键）。</summary>
        public string Id => Data.id;

        /// <summary>中文显示名。</summary>
        public string DisplayName => Data.displayName;

        /// <summary>关卡号（101–108 段）。</summary>
        public int LevelNumber => Data.levelNumber;

        /// <summary>迁移器 / 校验器写入入口（幂等覆盖）。</summary>
        public void Apply(WorldMapAssetPayload payload)
        {
            data = payload ?? new WorldMapAssetPayload();
        }
    }
}
