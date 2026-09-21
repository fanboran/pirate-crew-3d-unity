using System.Collections.Generic;
using PirateCrew.Data;

namespace PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 海图资产载荷 → 运行时 <see cref="WorldMapDefinition"/> 的适配层。
    ///
    /// 【为什么在 Battle 侧而不是 Data 侧】依赖方向：Data 是低层，不认识玩法层类型。
    /// 载荷（<see cref="WorldMapAssetPayload"/>）只描述"有什么数据"，玩法层决定"怎么用"——
    /// 转换在这里，Data 保持零玩法依赖。
    ///
    /// 【转换纪律】逐字段直搬，不做任何"顺手修一下"（重复的 WeaponStack、超 360° 的 yaw、
    /// y 非 0 的摆位都原样保留）——语义零漂移是本次重构的硬判据。数据健康度由
    /// <c>LevelAssetValidator</c> 另外报告，不在转换里静默改写。
    /// </summary>
    public static class WorldMapFromAsset
    {
        /// <summary>载荷 → 运行时定义。id 为空返回 null。</summary>
        public static WorldMapDefinition ToRuntime(WorldMapAssetPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.id))
                return null;

            return new WorldMapDefinition(
                id: payload.id,
                displayName: payload.displayName,
                levelNumber: payload.levelNumber,
                spanX: payload.spanX,
                spanZ: payload.spanZ,
                ambientTier: payload.ambientTier,
                crewWeapons: payload.crewWeapons,
                captainWeapons: payload.captainWeapons,
                terrain: KitPlacements(payload.terrain),
                horizon: KitPlacements(payload.horizon),
                props: PropPlacements(payload.props),
                spawns: Spawns(payload.spawns),
                airdropPool: payload.airdropPool,
                horizonSeed: payload.horizonSeed,
                horizonFeatures: payload.horizonFeatures);
        }

        static List<WorldKitPlacement> KitPlacements(List<KitPlacementEntry> entries)
        {
            var list = new List<WorldKitPlacement>(entries == null ? 0 : entries.Count);
            if (entries == null)
                return list;
            for (int i = 0; i < entries.Count; i++)
            {
                KitPlacementEntry e = entries[i];
                list.Add(new WorldKitPlacement(e.kit, e.asset, e.x, e.z, e.yawDeg, e.y));
            }
            return list;
        }

        static List<WorldPropPlacement> PropPlacements(List<PropPlacementEntry> entries)
        {
            var list = new List<WorldPropPlacement>(entries == null ? 0 : entries.Count);
            if (entries == null)
                return list;
            for (int i = 0; i < entries.Count; i++)
            {
                PropPlacementEntry e = entries[i];
                list.Add(new WorldPropPlacement(e.asset, e.x, e.y, e.z, e.yawDeg));
            }
            return list;
        }

        static List<WorldMapSpawn> Spawns(List<SpawnEntry> entries)
        {
            var list = new List<WorldMapSpawn>(entries == null ? 0 : entries.Count);
            if (entries == null)
                return list;
            for (int i = 0; i < entries.Count; i++)
            {
                SpawnEntry e = entries[i];
                list.Add(new WorldMapSpawn(e.teamIndex, e.archetype, e.x, e.z, e.luck));
            }
            return list;
        }
    }
}
