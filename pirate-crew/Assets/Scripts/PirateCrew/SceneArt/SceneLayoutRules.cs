using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>纵深三段（场景文档 §2.4：远景带 / 中景战场 / 近侧带）。</summary>
    public enum SceneDepthBand
    {
        /// <summary>远景带 Z ≤ 4：可放高物（船骨/断桅/棕榈/旗杆）。</summary>
        Far = 0,

        /// <summary>中景战场 4 &lt; Z &lt; 13：可玩区，只放功能性掩体。</summary>
        Mid = 1,

        /// <summary>近侧带 Z ≥ 13：只放 ≤ 0.6 矮物。</summary>
        Near = 2,
    }

    /// <summary>
    /// 场景陈设的**布局规则**（纯 C# 静态类，无头可测；不含任何几何/GameObject）。
    ///
    /// 【这是什么的权威】把 <c>docs/场景设计-战斗竞技场.md</c> §2.3/§2.4/§7.3/§9 与
    /// §3.2/§3.4/§4.3 里那些「可判定验收判据」翻译成可断言的函数，让摆放规则只有一处实现：
    /// 编辑器构建（<c>Assets/Editor/SceneArtBuilder.cs</c>）与运行时（地形壳体）都调这里，
    /// NUnit 用例（<c>Assets/Tests/SceneArt/</c>）直接测这里。
    ///
    /// 【标注】纵深分段阈值与间距全部来自场景文档（该文档已把 §7.3 遮挡规则、§9 可玩性约束
    /// 列为【AI 提案】/【依据】混合），本类逐条注明来源；凡本文档未给数值的自定阈值都标【AI 提案】。
    /// </summary>
    public static class SceneLayoutRules
    {
        // ------------------------------------------------------------------
        // 纵深分层（场景文档 §2.4 / §7.3）
        // ------------------------------------------------------------------

        /// <summary>远景带的最大 Z（含）：Z ≤ 4 允许放高物。【依据场景文档 §7.3】</summary>
        public const float FarBandMaxZ = 4f;

        /// <summary>近侧带的起始 Z（含）：Z ≥ 13 只允许 ≤0.6 的矮物。【依据场景文档 §7.3】</summary>
        public const float NearBandMinZ = 13f;

        /// <summary>近侧带允许的最大物体高度（世界单位）。【依据场景文档 §7.3】</summary>
        public const float NearBandMaxHeight = 0.6f;

        /// <summary>「高物」阈值：高度 &gt; 此值只允许放远景带或竞技场外。【依据场景文档 §7.3】</summary>
        public const float TallHeightThreshold = 1.5f;

        /// <summary>中景带里中高物与其正后方单位的 Z 差下限（场景文档 §7.3 规则 2）。</summary>
        public const float MidBandBehindClearanceZ = 1.5f;

        /// <summary>装饰与任意出生格心的最小水平距离。【依据场景文档 §9.5「≥1.5」】</summary>
        public const float SpawnClearance = 1.5f;

        /// <summary>竞技场中点两侧的取景留白宽度（场景文档 §7.3：中点两侧各留 3-5 单位不做高物）。</summary>
        public const float CenterCorridorHalfWidth = 3f;

        // ------------------------------------------------------------------
        // 掩体密度（场景文档 §3.2「可玩区内可选放 6-10 个 0.8-1.2 单位中石」）
        // ------------------------------------------------------------------

        /// <summary>可玩区功能掩体中石数量下限。【依据场景文档 §3.2】</summary>
        public const int MinCoverRocks = 6;

        /// <summary>可玩区功能掩体中石数量上限。【依据场景文档 §3.2】</summary>
        public const int MaxCoverRocks = 10;

        // ------------------------------------------------------------------
        // 道具尺寸区间（场景文档 §3.2/§3.4/§4.3）
        // ------------------------------------------------------------------

        /// <summary>台地棱线小石块半径区间（世界单位）。【依据场景文档 §3.2「0.2-0.5」】</summary>
        public const float RidgeRockMinRadius = 0.2f;

        /// <summary>台地棱线小石块半径上限。</summary>
        public const float RidgeRockMaxRadius = 0.5f;

        /// <summary>潮间带石块半径区间。【依据场景文档 §3.2「0.2-0.6」】</summary>
        public const float IntertidalRockMinRadius = 0.2f;

        /// <summary>潮间带石块半径上限。</summary>
        public const float IntertidalRockMaxRadius = 0.6f;

        /// <summary>功能掩体中石半径区间。【依据场景文档 §3.2「0.8-1.2」】</summary>
        public const float CoverRockMinRadius = 0.8f;

        /// <summary>功能掩体中石半径上限。</summary>
        public const float CoverRockMaxRadius = 1.2f;

        /// <summary>棕榈树数量区间。【依据场景文档 §3.4/§10.1 M11「棕榈 ≥8」与 §3.4「8-12 棵」】</summary>
        public const int MinPalms = 8;

        /// <summary>棕榈树数量上限。</summary>
        public const int MaxPalms = 12;

        /// <summary>棕榈树在竞技场外的远景带额外株数（场外不占竞技场摆位，场景文档 §3.4「或场外」）。</summary>
        public const int MinOffshorePalms = 2;

        /// <summary>棕榈树干高度区间。【依据场景文档 §3.4「干高 4-6」】</summary>
        public const float PalmMinTrunkHeight = 4f;

        /// <summary>棕榈树干高度上限。</summary>
        public const float PalmMaxTrunkHeight = 6f;

        /// <summary>台地棱线小石块总数区间。【依据场景文档 §3.2「每关 40-80 个」】</summary>
        public const int MinRidgeRocks = 40;

        /// <summary>台地棱线小石块总数上限。</summary>
        public const int MaxRidgeRocks = 80;

        /// <summary>潮间带石块总数区间。【依据场景文档 §3.2「20-35 个」】</summary>
        public const int MinIntertidalRocks = 20;

        /// <summary>潮间带石块总数上限。</summary>
        public const int MaxIntertidalRocks = 35;

        /// <summary>草丛实例总数区间。【依据场景文档 §3.4「400-1500 实例」】</summary>
        public const int MinGrassTufts = 200;
        // 2026-09-14：船面不参与草散射（用户裁决船上不长草），下限按可撒面缩小。

        /// <summary>草丛实例总数上限。</summary>
        public const int MaxGrassTufts = 1500;

        /// <summary>灌木簇数区间。【依据场景文档 §3.4「15-30 组」】</summary>
        public const int MinBushClusters = 15;

        /// <summary>灌木簇数上限。</summary>
        public const int MaxBushClusters = 30;

        // ------------------------------------------------------------------
        // 查询
        // ------------------------------------------------------------------

        /// <summary>Z 落在哪一段（<see cref="SceneDepthBand"/>）。</summary>
        public static SceneDepthBand BandOf(float z)
        {
            if (z <= FarBandMaxZ)
                return SceneDepthBand.Far;
            if (z >= NearBandMinZ)
                return SceneDepthBand.Near;
            return SceneDepthBand.Mid;
        }

        /// <summary>该 Z 处允许的最大装饰高度（竞技场内）。</summary>
        public static float MaxAllowedHeightAt(float z)
        {
            switch (BandOf(z))
            {
                case SceneDepthBand.Near: return NearBandMaxHeight;
                case SceneDepthBand.Far: return float.PositiveInfinity;
                default: return TallHeightThreshold;
            }
        }

        /// <summary>
        /// 场景文档 §7.3 的三条遮挡规则，合并成一个判据。
        /// <paramref name="insideArena"/> = false 表示在竞技场外的水缘/远景（不受纵深带约束）。
        /// </summary>
        public static bool IsHeightAllowedAt(float height, float z, bool insideArena)
        {
            if (!insideArena)
                return true;   // 场外：远景带语义，允许高物（场景文档 §7.3 规则 3 的例外）

            return height <= MaxAllowedHeightAt(z);
        }

        /// <summary>该点是否与任意出生格心的水平距离 &lt; <see cref="SpawnClearance"/>。</summary>
        public static bool IsInsideSpawnClearance(float x, float z, IReadOnlyList<Vector2Int> spawnCells)
        {
            if (spawnCells == null)
                return false;

            for (int i = 0; i < spawnCells.Count; i++)
            {
                // 出生格心 = (gridX + 0.5, gridY + 0.5)（与 LevelGeometry.GridToArena 同口径）。
                float dx = x - (spawnCells[i].x + 0.5f);
                float dz = z - (spawnCells[i].y + 0.5f);
                if (dx * dx + dz * dz < SpawnClearance * SpawnClearance)
                    return true;
            }

            return false;
        }

        /// <summary>两个装饰之间的最小间距（防止道具互相穿插成"堆"）。【AI 提案】</summary>
        public const float PropMinSpacing = 0.55f;

        /// <summary>该点是否离竞技场矩形边界至少 <paramref name="margin"/>（用于判断"场内/场外"）。</summary>
        public static bool IsInsideArenaRect(float x, float z, float arenaWidth, float arenaDepth, float margin = 0f)
        {
            return x >= -margin && x <= arenaWidth + margin
                && z >= -margin && z <= arenaDepth + margin;
        }

        /// <summary>
        /// 装饰（直径 <paramref name="radius"/> 的近似圆）是否完全落在竞技场矩形内。
        /// </summary>
        public static bool FitsInsideArena(float x, float z, float radius, float arenaWidth, float arenaDepth)
        {
            return x - radius >= 0f && x + radius <= arenaWidth
                && z - radius >= 0f && z + radius <= arenaDepth;
        }

        /// <summary>
        /// 中脊（最高台）是否没超出竞技场矩形——场景文档 §1.1/§2.2 要求中脊落在 X23-37。
        /// 本函数只做「越界检查」，不判断具体 X 范围。
        /// </summary>
        public static bool RidgeInsideArena(float ridgeMinX, float ridgeMaxX, float arenaWidth)
        {
            return ridgeMinX >= 0f && ridgeMaxX <= arenaWidth && ridgeMaxX > ridgeMinX;
        }

        /// <summary>
        /// 功能掩体中石的数量是否在场景文档 §3.2 的区间内。
        /// </summary>
        public static bool IsCoverCountInRange(int coverCount)
        {
            return coverCount >= MinCoverRocks && coverCount <= MaxCoverRocks;
        }

        /// <summary>
        /// 出生格是否"不入水且不悬空"：该格必须有 ≥1 块抬升（场景文档 §9.5「滩头列均有 ≥1 块抬升」），
        /// 且地表高度严格高于水面（落水即死判据 <see cref="LevelGeometry.IsBelowWater"/> 的反面）。
        /// </summary>
        public static bool IsSpawnCellSafe(TileTerrainGrid grid, int gridX, int gridY)
        {
            if (grid == null)
                return false;

            if (grid.BlocksAt(gridX, gridY) <= 0)
                return false;

            float surfaceY = grid.SurfaceWorldY(gridX, gridY);
            return !LevelGeometry.IsBelowWater(surfaceY, LevelGeometry.WaterSurfaceY)
                && surfaceY >= LevelGeometry.GroundTopY;
        }

        /// <summary>
        /// 出生位的实际站位高度比"平坦计划"（<see cref="LevelGeometry.GridToArena"/> 的 y）高出多少：
        /// 恰为该格抬升块数 × 单块高度。
        ///
        /// 【为什么需要这个函数】<see cref="LevelGeometry.GridToArena"/> 是**平坦口径**
        /// （恒返回 <c>GroundTopY + UnitPivotHeight</c>）；运行时 <c>BattleController.cs:253-257</c>
        /// 会把出生 Y 覆写成 <c>SurfaceWorldY + UnitPivotHeight</c>。两者之差就是这个偏移。
        /// 地形加了抬升块后，"出生位脚底是否贴合地表"必须按**覆写后**的口径核对，
        /// 本函数把该差值显式化，测试里再与 <see cref="SurfaceWorldY"/> 对账。
        /// </summary>
        public static float SpawnHeightOffsetFromFlatPlan(TileTerrainGrid grid, int gridX, int gridY)
        {
            if (grid == null)
                return 0f;

            return grid.BlocksAt(gridX, gridY) * grid.BlockWorldHeight;
        }

        /// <summary>
        /// 出生位的**脚底**世界 Y（按运行时口径）：<c>SurfaceWorldY + UnitPivotHeight − UnitPivotHeight</c>，
        /// 即等于该格地表。若结果与 <see cref="SurfaceWorldY"/> 不等，说明有人改了产出高度的口径。
        /// </summary>
        public static float SpawnFootWorldY(TileTerrainGrid grid, int gridX, int gridY, float unitPivotHeight)
        {
            return LevelGeometry.GridToArena(gridX, gridY).y
                + SpawnHeightOffsetFromFlatPlan(grid, gridX, gridY)
                - unitPivotHeight;
        }

        /// <summary>取该格的顶面世界 Y（无网格时回落基础地面），供陈设贴地用。</summary>
        public static float SurfaceYAt(TileTerrainGrid grid, int gridX, int gridY)
        {
            return grid == null ? LevelGeometry.GroundTopY : grid.SurfaceWorldY(gridX, gridY);
        }

        /// <summary>世界 XZ → 该处地表 Y（贴地摆放用）。</summary>
        public static float SurfaceYAtWorld(TileTerrainGrid grid, float worldX, float worldZ)
        {
            return grid == null
                ? LevelGeometry.GroundTopY
                : grid.SurfaceWorldYAtWorld(worldX, worldZ);
        }
    }

    /// <summary>
    /// 确定性伪随机（纯 C#，跨 Unity/.NET 完全一致；**刻意不用 <c>System.Random</c>**——
    /// 它在 Mono 与 .NET 8 上的算法不同，会让"同一关卡在编辑器/运行时/测试里摆出不同布局"）。
    /// 采用 LCG（数值 = Numerical Recipes 常数），只用于"看起来随机"的装饰抖动，
    /// **不参与任何玩法数值**（弹道/伤害/AI 都不读它）。
    /// </summary>
    public sealed class SceneArtRandom
    {
        uint _state;

        /// <summary>以固定种子构造。</summary>
        public SceneArtRandom(int seed)
        {
            _state = (uint)(seed * 2654435761u) ^ 0x9E3779B9u;
            if (_state == 0u)
                _state = 0xA341316Cu;
        }

        /// <summary>下一个 uint。</summary>
        public uint NextUInt()
        {
            _state = _state * 1664525u + 1013904223u;
            return _state;
        }

        /// <summary>[0,1) 浮点。</summary>
        public float Next01()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>[min,max) 浮点。</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * Next01();
        }

        /// <summary>[min,max) 整数。</summary>
        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        /// <summary>概率为 <paramref name="probability"/> 的真。</summary>
        public bool Chance(float probability)
        {
            return Next01() < probability;
        }
    }

    /// <summary>由整数坐标派生的确定性哈希（给网格顶点抖动用，不依赖遍历顺序）。</summary>
    public static class SceneArtHash
    {
        /// <summary>把 (a, b, salt) 映射到 [0,1)。</summary>
        public static float Hash01(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>把 (a, b, salt) 映射到 [-1,1)。</summary>
        public static float SignedHash(int a, int b, int salt)
        {
            return Hash01(a, b, salt) * 2f - 1f;
        }
    }
}
