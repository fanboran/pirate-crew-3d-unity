using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>平台簇的伪装类型（决定视觉底部与 kit 配方）。</summary>
    public enum PlatformClusterKind
    {
        /// <summary>无簇（水格 / 旧版列式地形）。</summary>
        None = 0,

        /// <summary>大船簇：底部 = 船体侧板 + 龙骨，甲板可走。</summary>
        Ship = 1,

        /// <summary>空岛簇：底部 = 岩锥收尖下垂（可挂垂藤/钟乳）。</summary>
        SkyIsland = 2,

        /// <summary>梯田小岛簇：底部 = 岩层，顶部多级台阶。</summary>
        TerraceIsland = 3,
    }

    /// <summary>单格平台表面的语义档（供小地图 / 视图选形）。</summary>
    public enum PlatformSurface : byte
    {
        /// <summary>旧版列式地形（有基础地面、无平台语义）。</summary>
        LegacyGround = 0,

        /// <summary>水（无地面，掉落即死）。</summary>
        Water = 1,

        Ship = 2,
        SkyIsland = 3,
        TerraceIsland = 4,
    }

    /// <summary>一个平台簇的只读描述（纯 C#）。</summary>
    public readonly struct PlatformClusterInfo
    {
        /// <summary>簇名（报告/测试可读）。</summary>
        public readonly string Name;

        /// <summary>伪装类型。</summary>
        public readonly PlatformClusterKind Kind;

        /// <summary>地面格包络（含边界，俯视矩形）。</summary>
        public readonly int X0, Z0, X1, Z1;

        /// <summary>簇内最低/最高平台高度（块，**局部**台阶高，不含 <see cref="BaseHeight"/>）。</summary>
        public readonly int MinBlocks, MaxBlocks;

        /// <summary>
        /// 岛基准高度（世界单位）：整簇地基相对基础地面 <see cref="LevelGeometry.GroundTopY"/> 的抬高量。
        ///
        /// 【数据口径】<see cref="PlatformMap.CellBlocks"/> 仍只记**局部**块高（簇内台阶差），
        /// 由 <see cref="TileTerrainGrid"/> 构造时换算成「局部 + 基准」的总块高。这样：
        ///   · 碰撞（<c>BattleTerrainView</c> 按 <c>BlocksAt</c> 摆方块）与视觉壳（按
        ///     <c>SurfaceWorldY</c>）**无需任何改动**就跟随基准高度；
        ///   · 「簇内台阶差」这条既有契约不被基准高度污染（基准是整簇平移，不是簇内加高）。
        /// </summary>
        public readonly float BaseHeight;

        /// <summary>
        /// 出生队列掩码：<c>bit0 = 红队(team0)</c>、<c>bit1 = 蓝队(team1)</c>、<c>0 = 中立</c>。
        /// 供 kit 配方区分「出生台地」与「中立场景簇」（见 <c>SceneArt/SceneKitCatalog.cs</c> 的配方）。
        /// </summary>
        public readonly int SpawnTeamMask;

        public PlatformClusterInfo(string name, PlatformClusterKind kind, int x0, int z0, int x1, int z1,
            int minBlocks, int maxBlocks, int spawnTeamMask = 0, float baseHeight = 0f)
        {
            Name = name;
            Kind = kind;
            X0 = x0;
            Z0 = z0;
            X1 = x1;
            Z1 = z1;
            MinBlocks = minBlocks;
            MaxBlocks = maxBlocks;
            SpawnTeamMask = spawnTeamMask;
            BaseHeight = baseHeight;
        }

        /// <summary>基准高度换算成**块数**（单块高度取全工程单一来源 <see cref="LevelGeometry.BlockWorldHeight"/>）。</summary>
        public int BaseBlocks =>
            Mathf.RoundToInt(BaseHeight / LevelGeometry.BlockWorldHeight);

        /// <summary>含基准高度的最低总块高。</summary>
        public int MinTotalBlocks => MinBlocks + BaseBlocks;

        /// <summary>含基准高度的最高总块高。</summary>
        public int MaxTotalBlocks => MaxBlocks + BaseBlocks;

        /// <summary>整簇平移基准高度（返回新值，本结构不可变）。</summary>
        public PlatformClusterInfo WithBaseHeight(float baseHeight)
        {
            return new PlatformClusterInfo(Name, Kind, X0, Z0, X1, Z1, MinBlocks, MaxBlocks,
                SpawnTeamMask, baseHeight);
        }

        /// <summary>整簇包络的宽度（格）。</summary>
        public int WidthTiles => X1 - X0 + 1;

        /// <summary>整簇包络的纵深（格）。</summary>
        public int DepthTiles => Z1 - Z0 + 1;

        /// <summary>格是否落在本簇包络内（含边界）。</summary>
        public bool Contains(int gx, int gz) => gx >= X0 && gx <= X1 && gz >= Z0 && gz <= Z1;

        /// <summary>是否出生簇（任一队在此出生）。</summary>
        public bool IsSpawnCluster => SpawnTeamMask != 0;

        /// <summary>该队是否在本簇出生（teamIndex 0/1）。</summary>
        public bool SpawnsTeam(int teamIndex)
        {
            if (teamIndex < 0 || teamIndex > 30)
                return false;
            return (SpawnTeamMask & (1 << teamIndex)) != 0;
        }
    }

    /// <summary>
    /// 平台簇地图（纯 C# 数据）：每格是「水」还是「某簇的地面」，地面格带显式块高与簇归属。
    ///
    /// 【为什么不用「每列一个高度」】列式只能表达"一整块连续地面 + 中脊"，无法表达
    /// "一堆高高低低、彼此隔水的悬空平台"。本结构是**逐格**（行主序）的
    /// <see cref="CellBlocks"/> + <see cref="CellCluster"/>，水格 <c>CellCluster = -1</c>、
    /// <c>CellBlocks = 0</c>。一代瓦片竞技场退场后，世界海域图的站面栅格
    ///（<c>WorldMapRuntime.BuildTerrainGrid</c>）与样板三关的逻辑高度场仍以块高语义进
    /// <see cref="TileTerrainGrid"/>；本结构保留供 kit 配方与视图层消费。
    /// </summary>
    public sealed class PlatformMap
    {
        /// <summary>横向格数。</summary>
        public readonly int WidthTiles;

        /// <summary>纵深格数。</summary>
        public readonly int DepthTiles;

        /// <summary>逐格块高（行主序）；0 = 水。**局部**块高——不含所属簇的 <see cref="PlatformClusterInfo.BaseHeight"/>。</summary>
        public readonly int[] CellBlocks;

        /// <summary>逐簇归属（行主序）；-1 = 水。</summary>
        public readonly int[] CellCluster;

        /// <summary>全部平台簇。</summary>
        public readonly IReadOnlyList<PlatformClusterInfo> Clusters;

        public PlatformMap(int widthTiles, int depthTiles, int[] cellBlocks, int[] cellCluster,
            IReadOnlyList<PlatformClusterInfo> clusters)
        {
            WidthTiles = widthTiles;
            DepthTiles = depthTiles;
            CellBlocks = cellBlocks;
            CellCluster = cellCluster;
            Clusters = clusters;
        }

        /// <summary>行主序索引。</summary>
        public int IndexOf(int gx, int gz) => gx + gz * WidthTiles;

        /// <summary>该格是否是地面（非水）。</summary>
        public bool IsGround(int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= WidthTiles || gz >= DepthTiles)
                return false;
            return CellCluster[IndexOf(gx, gz)] >= 0;
        }

        /// <summary>该格的块高（水 / 越界 = 0）；**局部**块高（不含簇基准高度）。</summary>
        public int BlocksAt(int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= WidthTiles || gz >= DepthTiles)
                return 0;
            return CellBlocks[IndexOf(gx, gz)];
        }

        /// <summary>该格所属簇的基准块数（水 / 越界 = 0）。</summary>
        public int BaseBlocksAt(int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= WidthTiles || gz >= DepthTiles)
                return 0;

            int c = CellCluster[IndexOf(gx, gz)];
            return c >= 0 && c < Clusters.Count ? Clusters[c].BaseBlocks : 0;
        }

        /// <summary>该格的**总**块高（局部 + 簇基准）；水 / 越界 = 0。</summary>
        public int TotalBlocksAt(int gx, int gz) => BlocksAt(gx, gz) + BaseBlocksAt(gx, gz);

        /// <summary>地面格数量。</summary>
        public int GroundCellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < CellCluster.Length; i++)
                {
                    if (CellCluster[i] >= 0)
                        n++;
                }
                return n;
            }
        }

        /// <summary>水格数量。</summary>
        public int WaterCellCount => CellBlocks.Length - GroundCellCount;

        /// <summary>取包含该格的簇；水格返回 default（<c>Name == null</c>）。</summary>
        public PlatformClusterInfo ClusterAt(int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= WidthTiles || gz >= DepthTiles)
                return default;
            int c = CellCluster[IndexOf(gx, gz)];
            return c >= 0 && c < Clusters.Count ? Clusters[c] : default;
        }
    }
}
