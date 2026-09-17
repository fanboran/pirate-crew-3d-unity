using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 大海域世界地图的数据载体（纯 C#，无头可测）。契约：docs/M4-大海域世界化.md §1/§2/§4.2。
    ///
    /// 【坐标系】Unity 世界系（Y-up，米）。地图占据 [0..SpanX]×[0..SpanZ]，
    /// 与 <see cref="LevelGeometry.GridToArena"/> 的「格 (gx,gy) → 世界 ((gx+0.5)×2, (gy+0.5)×2)」同源。
    /// 地形/船坞/远景件的摆放根在海平面 y=0；道具的摆放根在其所站站面顶高。
    ///
    /// 【站面平直机制】可站立面只来自 <see cref="WorldMapStandables"/> 的 box 表
    /// （Blender 侧 &lt;Asset&gt;.standable.json 的入库转写版）：Unity 侧按 box 生成 BoxCollider、
    /// 并栅格化进 <c>TileTerrainGrid</c> 供 AI/小地图/出生高度使用——视觉网格不参与碰撞。
    /// </summary>

    /// <summary>一个可站立矩形：资产本地系（Y-up）。Center/Size 定义在<b>先绕资产原点转 YawDeg 后</b>的坐标系内；TopY 为顶面高度（0.5 档）。</summary>
    public struct WorldStandBox
    {
        public readonly Vector2 Center;
        public readonly Vector2 Size;
        public readonly float TopY;
        public readonly float YawDeg;

        public WorldStandBox(float centerX, float centerZ, float sizeX, float sizeZ, float topY, float yawDeg = 0f)
        {
            Center = new Vector2(centerX, centerZ);
            Size = new Vector2(sizeX, sizeZ);
            TopY = topY;
            YawDeg = yawDeg;
        }
    }

    /// <summary>地形/船坞/远景件摆放（根默认 = 海平面 y=0；Yaw 绕 Y 轴，度，逆时针俯视）。
    /// positionY 供需要脱离海平面的摆放显式给值（远景特征环：鲸半浸下沉 / 云带抬空，
    /// 见 <see cref="HorizonFeatureRules"/>）；目录手摆件不传即维持 y=0 旧口径。</summary>
    public struct WorldKitPlacement
    {
        public readonly string Kit;
        public readonly string Asset;
        public readonly Vector3 Position;
        public readonly float YawDeg;

        public WorldKitPlacement(string kit, string asset, float x, float z, float yawDeg = 0f, float positionY = 0f)
        {
            Kit = kit;
            Asset = asset;
            Position = new Vector3(x, positionY, z);
            YawDeg = yawDeg;
        }
    }

    /// <summary>道具摆放（根 = 落地接触面中心，Position.y = 所站站面顶高）。</summary>
    public struct WorldPropPlacement
    {
        public readonly string Asset;
        public readonly Vector3 Position;
        public readonly float YawDeg;

        public WorldPropPlacement(string asset, float x, float y, float z, float yawDeg = 0f)
        {
            Asset = asset;
            Position = new Vector3(x, y, z);
            YawDeg = yawDeg;
        }
    }

    /// <summary>出生点（世界坐标）。初始武器由 Battle 接线层按「人手樱桃炸弹×∞、船长另配炸药」的战役惯例合成。</summary>
    public struct WorldMapSpawn
    {
        public readonly int TeamIndex;
        public readonly string Archetype;
        public readonly float X;
        public readonly float Z;
        public readonly int Luck;

        public WorldMapSpawn(int teamIndex, string archetype, float x, float z, int luck)
        {
            TeamIndex = teamIndex;
            Archetype = archetype;
            X = x;
            Z = z;
            Luck = luck;
        }
    }

    /// <summary>一张大海域地图的完整定义。</summary>
    public sealed class WorldMapDefinition
    {
        public readonly string Id;
        public readonly string DisplayName;
        /// <summary>关卡号（世界地图占用 101–108 段，与原版转写 1–33 不冲突）。</summary>
        public readonly int LevelNumber;
        public readonly float SpanX;
        public readonly float SpanZ;
        /// <summary>氛围档（"Noon"/"Dusk"/"Storm"，对接 AmbientTimeOfDayCatalog 预设名）。</summary>
        public readonly string AmbientTier;
        public readonly IReadOnlyList<WorldKitPlacement> Terrain;
        public readonly IReadOnlyList<WorldKitPlacement> Horizon;
        public readonly IReadOnlyList<WorldPropPlacement> Props;
        public readonly IReadOnlyList<WorldMapSpawn> Spawns;
        /// <summary>空投武器池（count=10 为无限，沿袭 §5.5 惯例）。</summary>
        public readonly IReadOnlyList<WeaponStack> AirdropPool;
        /// <summary>
        /// 普通船员初配（方案 D 分层军火，2026-09-17 裁决）：全近程档（twangMax 20，
        /// 逆向 §5.1/§5.2）；null 时 <see cref="WorldMapRuntime"/> 回落「樱桃×∞」战役惯例。
        /// </summary>
        public readonly IReadOnlyList<WeaponStack> CrewWeapons;
        /// <summary>
        /// 船长初配：含 ≥1 件全图级旗舰武器（cannon/seagull/tidalWave/anchor/voodooDoll，
        /// 逆向 §5.2——这五件射程 = 地图本身）；null 时回落「樱桃×∞+炸药×5」惯例。
        /// </summary>
        public readonly IReadOnlyList<WeaponStack> CaptainWeapons;
        /// <summary>远景环随机种子（确定性）。</summary>
        public readonly int HorizonSeed;
        /// <summary>本图专属远景特征件（如 LeviathanTentacle / GiantRibs）。</summary>
        public readonly IReadOnlyList<string> HorizonFeatures;

        public WorldMapDefinition(
            string id, string displayName, int levelNumber, float spanX, float spanZ, string ambientTier,
            IReadOnlyList<WeaponStack> crewWeapons, IReadOnlyList<WeaponStack> captainWeapons,
            IReadOnlyList<WorldKitPlacement> terrain, IReadOnlyList<WorldKitPlacement> horizon,
            IReadOnlyList<WorldPropPlacement> props, IReadOnlyList<WorldMapSpawn> spawns,
            IReadOnlyList<WeaponStack> airdropPool, int horizonSeed, IReadOnlyList<string> horizonFeatures)
        {
            Id = id;
            DisplayName = displayName;
            LevelNumber = levelNumber;
            SpanX = spanX;
            SpanZ = spanZ;
            AmbientTier = ambientTier;
            CrewWeapons = crewWeapons;
            CaptainWeapons = captainWeapons;
            Terrain = terrain;
            Horizon = horizon;
            Props = props;
            Spawns = spawns;
            AirdropPool = airdropPool;
            HorizonSeed = horizonSeed;
            HorizonFeatures = horizonFeatures;
        }
    }
}
