using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
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
        ///   · 「簇内台阶差 ≤ <see cref="PlatformClusterLayout.MaxBlocksPerCluster"/>」这条既有契约
        ///     不被基准高度污染（基准是整簇平移，不是簇内加高）。
        ///
        /// 取值恒为整数块的倍数（阶梯 0/3/6/9 世界单位 = 0/12/24/36 块 @0.25）。
        /// </summary>
        public readonly float BaseHeight;

        /// <summary>
        /// 出生队列掩码：<c>bit0 = 红队(team0)</c>、<c>bit1 = 蓝队(team1)</c>、<c>0 = 中立</c>。
        /// 供 kit 配方区分「出生台地」与「中立场景簇」（见 <c>SceneArt/SceneKitCatalog.cs</c> 的 BuildFor）。
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

        /// <summary>基准高度换算成**块数**（单块高度取全工程单一来源 <see cref="TerrainCatalog.DefaultBlockWorldHeight"/>）。</summary>
        public int BaseBlocks =>
            Mathf.RoundToInt(BaseHeight / TerrainCatalog.DefaultBlockWorldHeight);

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
    /// 【为什么不用「每列一个高度」】旧版把地形写成 <c>columnBlocks[gridX]</c>（同一列沿 Z 同高），
    /// 那只能表达"一整块连续地面 + 中脊"，无法表达"一堆高高低低、彼此隔水的悬空平台"。
    /// 本结构改成**逐格**（行主序）的 <see cref="CellBlocks"/> + <see cref="CellCluster"/>，
    /// 水格 <c>CellCluster = -1</c>、<c>CellBlocks = 0</c>。
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

    /// <summary>
    /// 关卡 → **平台簇布局**的纯 C# 推导（无头可测，运行时与烘焙共用）。
    ///
    /// ==================================================================
    /// 【入口与优先级（2026-09-14 起）】
    /// ==================================================================
    ///   1. <b>原版 tile 地图（关卡地形的唯一权威）</b>：<see cref="BuildFor(LevelData)">BuildFor(level)</see>
    ///      先走 <see cref="TryBuildFromTileMap"/>，用 <c>Data/LevelTileMaps.cs</c> 里 33 关的原版
    ///      <c>&lt;row&gt;</c> 行串推出平台簇地图 —— 关卡地形按原版逐格翻译，
    ///      程序化模板不再是关卡形状的来源（用户批评"关卡地图翻译的很不好"的整改）。
    ///   2. <b>通用推导（兜底）</b>：原版行串缺失、或与关卡数据对不上（测试的合成关 / 未转写关）
    ///      时退回下面的 V4 模板推导（<see cref="BuildGeneric"/>），保"任何 LevelData 都能推出
    ///      一张能玩的地图"这条老契约不破。
    ///
    /// ==================================================================
    /// 【原版 tile → 平台簇 的推导（主路径）】
    /// ==================================================================
    /// 逐条见 <see cref="BuildFromTileMap"/> 的注释。一句话：
    ///   可站面 = 原版非空瓦片（除海面波纹）；岛高 =（该岛顶行离水线的行数）×
    ///   <see cref="TileBlocksPerRow"/>；纵深 = 岛自身的行跨度（<c>gridZ = rowY − 1</c>）；
    ///   岛型 = 含船体语汇者 <c>Ship</c>、其余 <c>TerraceIsland</c>。
    ///
    /// ==================================================================
    /// 【兜底路径：通用推导规则表（每条的出处 = docs/关卡设计语言-参照游戏全场景分析.md §4 的 R 编号）】
    /// ==================================================================
    /// | 步骤 | 规则 | 实现 |
    /// | --- | --- | --- |
    /// | 出生簇 | R1（主簇包络≥8×4 且必须容纳全部出生点）、R2（每出生点≥4 格平台） | 以该队 units 的 (gridX,gridY) 包络外扩 1 格、再按需扩张到面积 ≥ 4×人数，整块盖 1 块高 |
    /// | 双方过近 | R1/R6（一个主导形状） | 两个出生矩形相交 → 合成**单簇**（掩码双队），另加 1 个中立卫星簇 |
    /// | 出生台地 | R18（前 5 关不多层）、R10（瞭望台只在船簇） | 非教学关：内缩 1 格抬高到 2–3 块；教学关（2–5）保持单层 |
    /// | 主簇 | R6（一个主导形状 + 至多一个变奏） | 每关只有一个"主簇"（模板决定），形状取自 5 个模板 |
    /// | 水距 | R3（常规 1–5 格）、R16（≤ 满力射程 80%）、R3 例外（8–10 格必须配越水武器） | 生成后**统一过桥**：相邻簇水距 > 上限就在中点插"踏脚小岛"，直到全部 ≤ 上限 |
    /// | 越水武器 | R3（水距是武器池的"因"） | <see cref="HasCrossingWeapon"/> 检测锚/海鸥/加农/海啸；无越水武器时上限压到 5 格 |
    /// | 地形分档 | §2.1（档 1 沙滩+船 / 档 2 土草洞 / 档 3 远海礁） | <see cref="ChapterOf"/> 决定簇 Kind：档 1 主簇 Ship、档 2 TerraceIsland、档 3 SkyIsland |
    /// | 形状模板 | §2.2 的 8 个母题、R13（每关一个记忆点） | <see cref="TemplateFor"/> 由 levelNumber 确定性取模板（双岛对峙 / 沉船残骸 / 环形礁 / 碎岛雨 / 阶梯峰） |
    /// | 簇密度 | R9（教学 5–9、常规 7–12、高密 19–21） | 碎岛雨模板的簇数 = clamp(面积/90, 6, 21)；其余模板 2–5 簇 |
    /// | 垂直节奏 | R8（≥9 层的垂直大关后必须接 ≤3 层低平关） | 阶梯峰只在 <c>levelNumber % 5 == 0</c> 的关启用（峰值 5 块），其余关不出现 |
    /// | 掩体 | R17（掩体用高度差不用墙） | 全部地形都是实心平台 + 块高差；不生成任何墙/斜坡/单向平台 |
    /// | **岛形轮廓** | 用户裁决 1/4（禁止矩形瓦片拼盘） | 非出生簇的大矩形 stamp 走 `StampIsland`（角向噪声侵蚀/外凸，见该类头） |
    /// | **基准高度** | 用户裁决 2（错落悬浮高度） | `AssignBaseHeights` 按 `hash(levelNumber, 簇下标)` 定名次 → 0/3/6/9 世界单位阶梯（出生岛压最低档、阶梯连续） |
    ///
    /// 【已删除：level_1 手写四簇定义（2026-09-14）】旧实现给 level_1 手写了 4 个簇
    /// （<c>terrace_island_west</c> / <c>great_ship_center</c> / <c>sky_island_east</c> /
    /// <c>sky_islet_north</c>，411 地面格、基准高度恒 0、轮廓恒矩形）——那正是
    /// "程序化瞎编布局"的代表，与用户"关卡地图翻译的很不好"的批评直接对应。
    /// **现已删除**：level_1 与其余 32 关一样走原版 tile 地图（50×17，9 座岛、地面格数见
    /// <c>LevelTileMaps</c> 语义表）。相关旧断言已按原版地图重写，落在
    /// <c>Tests/Battle/TileTerrainTests.cs</c> / <c>Tests/SceneArt/PlatformTerrainTests.cs</c>
    /// （两处均注明"2026-09-14 起以原版 tile 地图为准"）。
    ///
    /// 【未建模（诚实声明，只针对兜底路径）】R4（"主簇最底行贴水线"）依赖原版 2D 的行序语义，
    /// 本项目把行号用于**高度**（见 <see cref="BuildFromTileMap"/>），故兜底路径不按"贴水线"摆放；
    /// R11（台阶进深 ≥2 行）只在主簇台阶上近似满足（内缩 1 格 = 每边进深 1 格，比原版略窄）。
    ///
    /// ==================================================================
    /// 【逐关"这一关的灵魂"（读原版 tile 后的一句话，供 kit / 场景美术对齐构图）】
    /// ==================================================================
    /// 每行 = 关卡号：岛数 / 船岛数 / 该关的记忆点（由 <c>LevelTileMaps</c> 的语义统计读出）。
    /// 布局围绕这句话做：单位姿势、岛高次序、水距语义都与它对齐，改布局前先读这一行。
    /// 表见 <c>Data/LevelTileMaps.cs</c> 类头的"逐关签名"注释（由生成脚本从行串统计得出，
    /// 是**可核对的事实**而非叙述）。
    ///
    /// 【原来这里写的是什么（留档一句话）】旧实现用 <c>Level1Defs</c> 手写 4 个簇并给 level_1
    /// 单独开了分支（<c>BuildFromDefs</c>）；那是"程序化瞎编布局"的产物，已删除，勿再引入。
    ///
    /// 【水陆语义】原版行串是**逐格水陆**：水格（<c>-</c> 与海面波纹）没有地面，单位走上去会掉到
    /// 水面以下（落水即死，§4.4）。这与旧列式地形（整块地面 + 中脊、永不挖洞）是两套语义，
    /// 后者现在只作为"未转写关"的兜底。
    /// </summary>
    public static class PlatformClusterLayout
    {
        /// <summary>单簇最高块数（平台竖直压缩上限；<see cref="TileTerrainGrid"/> 的块高口径）。</summary>
        public const int MaxBlocksPerCluster = 5;

        // ==================================================================
        // 用户裁决（docs/关卡设计语言-参照游戏全场景分析.md 头部总纲，2026-09-14）
        // ==================================================================
        // 「到处都是相对独立的空岛，每个处都应该是一个独立的大空岛」的四条落地：
        //   1. 每处 = 一整块岛体：**不规则岛形轮廓**（不是矩形瓦片拼盘）+ 厚重底部收形；
        //   2. 一张图 = 多个独立空岛，各自**不同的悬浮基准高度**（阶梯 0/3/6/9 世界单位）；
        //   3. 大块可读剪影 + 大色块（能站/会死一眼分清）；
        //   4. 禁止：贴水薄甲板 / 矩形瓦片拼盘 / 同层平铺 / 木板+草方块混搭。
        //
        // 【基准高度上限定为 9 世界单位（阶梯 0/3/6/9）的量化依据】
        //   · 相机可达性：BattleCameraController 全场档 = 距离 15 / 俯角 45°（FullFieldDistance/
        //     FullFieldPitchDegrees），相机高出焦点 15·sin45° ≈ 10.61 世界单位；滚轮最远 25 档
        //     则为 17.68。最高岛顶 = 9 + 5 块×0.25 = 10.25 < 10.61 → 全场档仍在画面内。
        //   · 瞄准/投掷可达性：满力投掷（twangMax=20 px/帧、抬升 0.7、g=19.53）的**竖直顶点**
        //     只有 v_v²/(2g) ≈ 2.06 世界单位（v_v = 20/1.28/√1.49·0.7 ≈ 8.96）。
        //     即单发投掷最多爬到起点上方约 2.06 单位；加上投手可站的最高局部台阶 5 块（1.25），
        //     一次跨越的**竖直档差上限 ≈ 3.3 单位**。故阶梯步长取 3（不是任意的 0/+3/+6/+9 里的 3
        //     以外的更细档），且**阶梯必须连续**（用了 9 就必须有 3/6 的中继岛）——
        //     见 AssignBaseHeights 的"补阶"步骤；没有中继岛时 9 档岛将成为打不到的孤岛。
        //   · 一个反例记录（诚实声明）：步长 3 已贴近 3.3 的上限，投手若站在最低台阶上，
        //     跨 3 档会落在射程边缘（需靠岛内高地或中继岛辅助），这是本方案的**已知手感代价**。

        /// <summary>
        /// 基准高度阶梯（世界单位）。用户裁决的 0/+3/+6/+9 档；每档 = 12 块 @0.25。
        /// 见类头「基准高度上限定为 9 世界单位」的量化依据。
        /// </summary>
        public static readonly float[] BaseHeightLadder = { 0f, 3f, 6f, 9f };

        /// <summary>
        /// 岛形掩码的**启用下限**：宽或深小于该值的 stamp（踏脚石 / 出生台地）保持实心矩形，
        /// 既避免 2×2 的跳板被侵蚀成碎渣，也保证出生台地形状可读。
        /// </summary>
        public const int IslandMaskMinSpan = 4;

        /// <summary>岛形掩码启用下限（面积，格）：小平台不做侵蚀。</summary>
        public const int IslandMaskMinArea = 16;

        /// <summary>岛形掩码最大侵蚀深度（相对归一化半径）：0.28 ≈ 在 14 宽的岛上蚀掉 2 格。</summary>
        public const float IslandErosion = 0.28f;

        /// <summary>岛形掩码最大外凸幅度（相对归一化半径）：0.16 ≈ 外凸 1 格。</summary>
        public const float IslandBulge = 0.16f;

        /// <summary>核心保底半径（归一化）：半径 ≤ 该值的格永不被侵蚀（岛心不成洞）。</summary>
        public const float IslandCoreRadius = 0.5f;

        /// <summary>侵蚀后保留率下限：低于它说明蚀过头（碎渣/成洞），退回实心矩形。</summary>
        public const float IslandMinKeepRatio = 0.62f;

        /// <summary>常规关允许的最大簇间水距（格）——R3「常规关 1–5 格」。</summary>
        public const int RegularMaxWaterGap = 5;

        /// <summary>配了越水武器时允许的最大簇间水距（格）——R3 例外「唯一允许 8–10 格」。</summary>
        public const int CrossingWeaponMaxWaterGap = 10;

        // ------------------------------------------------------------------
        // 原版 tile 地图路径（2026-09-14 起为关卡地形的唯一权威）
        // ------------------------------------------------------------------

        /// <summary>
        /// 行高换算系数（**块**/行）：一列最上面的地面格（草地顶 / 甲板）每比水线高 1 行，
        /// 该岛抬高 1.25 块（= 0.3125 世界单位）。
        ///
        /// 【为什么恰好是 1.25】用户口径是"以水面行为 0，向上每行 +N（N 取 0.5-1 格量级）"，
        /// 但两条硬约束把 N 夹在 0.286–0.321 世界单位/行（= 1.14–1.28 块/行）之间：
        ///   · 下限：level_1 各岛顶行离水线 3–10 行（7 行差）。N &lt; 0.286 单位/行时
        ///     同关最高-最低岛高差 &lt; 2 世界单位（用户裁决 ③ 的下限），关卡读不出高低结构；
        ///   · 上限：33 关里最大的岛顶行离水线 28 行（level_21 的一座高崖）。相机全场档的机位
        ///     只比聚焦点高 15·sin45° ≈ 10.61 世界单位（BattleCameraController.FullFieldDistance/Pitch），
        ///     故最高岛顶必须 ≤ 9 世界单位 → N ≤ 9 / 28 ≈ 0.321 单位/行。
        /// 取 1.25 块/行 = 0.3125 单位/行：最大 = round(28×1.25) = 35 块 = 8.75 单位 ✓；
        /// level_1 高差 = round(10×1.25) − round(3×1.25) = 12 − 4 = 8 块 = 2.0 单位 ✓（恰好达标）。
        /// 取整用 <see cref="Mathf.RoundToInt"/>（银行家舍入，跨运行时确定）。
        /// </summary>
        public const float TileBlocksPerRow = 1.25f;

        /// <summary>
        /// 单岛悬浮基准高度上限（块）：36 块 = 9 世界单位。上界推导见 <see cref="TileBlocksPerRow"/>
        /// （相机全场档可达性）；本常量只是防御性钳制，实际最大值是 35 块（level_21）。
        /// </summary>
        public const int TileMaxBaseBlocks = 36;

        /// <summary>
        /// 每个地面格的**局部**块高（不含簇基准）。原版行串只说"这格是陆地/海水"，
        /// 高度信息全部由行号给出（见 <see cref="TileBlocksPerRow"/>），故局部恒 1 块 ——
        /// 保证每格都有可站面（<c>IsGroundAt</c> 要求块高 &gt; 0），且地面绝对高度全部来自建筑基准。
        /// </summary>
        public const int TileLocalBlocks = 1;

        /// <summary>
        /// level_1 的平台簇地图 = <see cref="BuildFor(LevelData)">BuildFor(level_1)</see>。
        /// <b>2026-09-14 起以原版 tile 地图为准</b>：旧的手写四簇定义（4 簇 / 411 地面格 /
        /// 恒 0 基准高度）已删除，见类头"已删除"段落。
        /// </summary>
        public static PlatformMap BuildLevel1()
        {
            return BuildFor(LevelCatalog.Get(1));
        }

        /// <summary>level_1 的平台簇描述（供测试 / 报告直接读 = <see cref="BuildLevel1"/>）。</summary>
        public static IReadOnlyList<PlatformClusterInfo> Level1Clusters => BuildLevel1().Clusters;

        /// <summary>
        /// 关卡 → 平台簇地图。**优先原版 tile 地图**（<see cref="TryBuildFromTileMap"/>，33 关全有）；
        /// 原版数据缺失或与关卡数据不匹配（合成关 / 未转写关）时退回
        /// <see cref="BuildGeneric"/> 的通用推导（V4 模板，仅作兜底）。
        /// 尺寸非法（&lt;=0）时返回 <c>null</c>（调用方退回旧列式地形 / 平坦竞技场）。
        /// </summary>
        public static PlatformMap BuildFor(LevelData level)
        {
            if (TryBuildFromTileMap(level, out PlatformMap tileMap))
                return tileMap;

            return BuildGeneric(level);
        }

        /// <summary>
        /// 按关卡号推导平台簇地图；关卡数据未转写时返回 <c>false</c>（调用方退回旧路径）。
        /// </summary>
        public static bool TryBuildFor(int levelNumber, out PlatformMap map)
        {
            map = null;
            if (!LevelCatalog.IsTranscribed(levelNumber))
                return false;

            map = BuildFor(LevelCatalog.Get(levelNumber));
            return map != null;
        }

        // ------------------------------------------------------------------
        // 原版 tile 地图 → 平台簇地图
        // ------------------------------------------------------------------

        /// <summary>
        /// 用原版 tile 地图推导平台簇地图（<see cref="BuildFor(LevelData)"/> 的主路径）。
        ///
        /// 【为什么要有"数据对得上"这道闸】33 关都有原版行串，但测试会**合成** LevelData
        /// （尺寸 / 出生位自造）来压边界。三条全过才走本路径：
        ///   ① <see cref="LevelTileMaps"/> 收录了该关号；
        ///   ② 尺寸与行串一致（宽 = 行串展开格数、高 = 行串行数）；
        ///   ③ **每个出战单位的 (gridX, gridY) 都落在行串的地面格上** —— 既挡住合成数据，
        ///      也是翻译正确性的硬判据（单位必须站在原版地图的地面上，全 33 关 360 个单位实测通过）。
        /// 任一条不过就回退通用推导，绝不生成"单位悬空 / 落水"的地图。
        /// </summary>
        public static bool TryBuildFromTileMap(LevelData level, out PlatformMap map)
        {
            map = null;

            LevelTileMapData tiles;
            if (!LevelTileMaps.TryParse(level.LevelNumber, out tiles))
                return false;
            if (tiles.Width != level.WidthTiles || tiles.Height != level.HeightTiles)
                return false;

            IReadOnlyList<LevelUnit> units = level.Units;
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    LevelUnit u = units[i];
                    if (!tiles.IsSolidAtGrid(u.gridX, u.gridY))
                        return false;
                }
            }

            map = BuildFromTileMap(tiles, level);
            return map != null;
        }

        /// <summary>
        /// 原版 tile 地图 → <see cref="PlatformMap"/>（本文件的**地形权威**）。
        ///
        /// ==================================================================
        /// 【四条推导，改前必读】
        /// ==================================================================
        /// 一、<b>可站面 = 原版非空格（除海面波纹）</b>
        ///   行串里每个非 <c>-</c> 且非 <c>tile_ripple_*</c>/<c>boat_ripple_*</c> 的格 = 一块可站地面；
        ///   轮廓**完全来自原版**，不做任何侵蚀 / 外凸（V4 的 <c>StampIsland</c> 只为"程序化造岛"服务，
        ///   原版 tile 本身就是岛形）。海面波纹算水（踩上去落水即死，§4.4），不是可站面 ——
        ///   把它们当地面会让整片海变成一块可行走的"水地板"。
        ///
        /// 二、<b>行号 → 高度（不是纵深！）</b>
        ///   原版是 2D 侧视图，行号 <c>rowY</c> 表示"离水面多高"（0 = 最上一行 = 最高）。每座岛
        ///   （= 4 连通域）取**自己最上面一行地面格**（草地顶 / 甲板顶）到水线的行数差
        ///   <c>span = floor(waterTileY) − topRow</c>，换算 <c>baseBlocks = round(span × TileBlocksPerRow)</c>
        ///   作为该岛的悬浮基准高度。于是原版里离水线越远的岛在 3D 里浮得越高、贴水行的沙洲贴水 ——
        ///   这正是 V4"错落基准高度"的语义，只是把**分配来源从哈希改成原版行号**（原版自身的竖直结构
        ///   才是真相之源）。
        ///
        /// 三、<b>纵深 Z = 岛自身的行跨度（<c>gridZ = rowY − 1</c>）</b>
        ///   3D 需要第二个自由度，Z 就取该岛在原版里占的行范围：
        ///     · 语义上是"土 / 岩行 = 岛体厚度"的直接翻译（岛的土体有多厚，纵深就有多深）；
        ///     · 工程上让单位的 Z 落位天然正确 —— 原版对象的 y 是"脚底行 − 1"（全 33 关 360 个单位
        ///       实测 100% 站在 <c>(x, y+1)</c> 行上），减 1 之后单位的 <c>(gridX, gridY)</c>
        ///       恰好就是它脚下地面格，于是 <c>BattleController</c> 的 <c>z = gridY + 0.5</c> 与
        ///       <c>SurfaceWorldY(gridX, gridY)</c> 同时命中同一格。
        ///   【为什么**不**给岛另铺 2–5 格厚的自由 Z】那会把"单位的 (gridX, gridY)"与"岛的 Z 带"
        ///   解耦，而 <c>BattleController</c>（禁改文件）以 <c>gridY</c> 为 Z 索引查地表并把单位
        ///   投在 <c>z = gridY + 0.5</c> —— 自由 Z 会直接把单位放到地图之外（悬空 / 落水）。
        ///   纵深层次由原版自身给出：岛在 X 上错开、行跨度不同者在 Z 上也错开
        ///   （高的岛岛体自然比贴水沙洲厚），不会塌成"同一排平面"。
        ///
        /// 四、<b>每岛的伪装类型</b>（<see cref="PlatformClusterKind"/>，决定 kit 材质族）
        ///   岛内含船体语汇（<c>ship_*</c> / <c>cannon_port_*</c> / <c>mast_*</c> / <c>crows_nest_*</c>）
        ///   → <see cref="PlatformClusterKind.Ship"/>；其余（草地 / 土 / 沙洲）→
        ///   <see cref="PlatformClusterKind.TerraceIsland"/>。原版行串里没有岩石瓦片
        ///   （<c>rock_*</c> 只出现在背景层 <c>&lt;bgRow&gt;</c>），故本路径不产生 <c>SkyIsland</c>。
        /// </summary>
        static PlatformMap BuildFromTileMap(LevelTileMapData tiles, LevelData level)
        {
            int w = level.WidthTiles;
            int d = level.HeightTiles;
            if (w <= 0 || d <= 0 || w != tiles.Width || d != tiles.Height)
                return null;

            int rows = tiles.Height;

            // 1) 连通域（4 邻接）标号 —— 在**原版行空间** (x, rowY) 上做，一个域 = 一座岛。
            var comp = new int[w * rows];
            for (int i = 0; i < comp.Length; i++)
                comp[i] = -1;

            var islands = new List<List<int>>();
            var stack = new List<int>(256);

            for (int rowY = 0; rowY < rows; rowY++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!tiles.IsSolidAt(x, rowY) || comp[x + rowY * w] >= 0)
                        continue;

                    int island = islands.Count;
                    var cells = new List<int>(64);
                    islands.Add(cells);

                    comp[x + rowY * w] = island;
                    stack.Clear();
                    stack.Add(x + rowY * w);

                    while (stack.Count > 0)
                    {
                        int idx = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);

                        int cx = idx % w;
                        int cy = idx / w;
                        cells.Add(idx);

                        PushIslandCell(comp, tiles, w, cx - 1, cy, island, stack);
                        PushIslandCell(comp, tiles, w, cx + 1, cy, island, stack);
                        PushIslandCell(comp, tiles, w, cx, cy - 1, island, stack);
                        PushIslandCell(comp, tiles, w, cx, cy + 1, island, stack);
                    }
                }
            }

            // 2) 落位：gridZ = rowY − 1（行 0 恒为空 —— 由 LevelTileMapsTests 断言）。
            var blocks = new int[w * d];
            var cellCluster = new int[w * d];
            for (int i = 0; i < cellCluster.Length; i++)
                cellCluster[i] = -1;

            int waterRow = Mathf.FloorToInt(level.WaterTileY + 1e-4f);
            var infos = new List<PlatformClusterInfo>(islands.Count);

            for (int c = 0; c < islands.Count; c++)
            {
                List<int> cells = islands[c];

                int minRow = int.MaxValue, maxRow = int.MinValue;
                int x0 = int.MaxValue, x1 = int.MinValue;
                int z0 = int.MaxValue, z1 = int.MinValue;
                bool shipLook = false;
                bool placed = false;

                for (int i = 0; i < cells.Count; i++)
                {
                    int idx = cells[i];
                    int gx = idx % w;
                    int rowY = idx / w;

                    if (rowY < minRow) minRow = rowY;
                    if (rowY > maxRow) maxRow = rowY;
                    if (gx < x0) x0 = gx;
                    if (gx > x1) x1 = gx;
                    if (!shipLook && LevelTileMaps.IsShipTile(tiles.TileAt(gx, rowY)))
                        shipLook = true;

                    int gz = LevelTileMapData.RowToGridZ(rowY);
                    if (gz < 0 || gz >= d)
                        continue;

                    int cell = gx + gz * w;
                    blocks[cell] = TileLocalBlocks;
                    cellCluster[cell] = c;
                    placed = true;
                    if (gz < z0) z0 = gz;
                    if (gz > z1) z1 = gz;
                }

                if (!placed)
                {
                    z0 = 0;
                    z1 = 0;
                }

                // 行号 → 悬浮高度：以水面行为 0，向上每行 +TileBlocksPerRow 块。
                int spanRows = waterRow - minRow;
                if (spanRows < 0)
                    spanRows = 0;

                int baseBlocks = Mathf.Clamp(Mathf.RoundToInt(spanRows * TileBlocksPerRow),
                    0, TileMaxBaseBlocks);
                float baseHeight = baseBlocks * TerrainCatalog.DefaultBlockWorldHeight;

                PlatformClusterKind kind = shipLook
                    ? PlatformClusterKind.Ship
                    : PlatformClusterKind.TerraceIsland;

                infos.Add(new PlatformClusterInfo(
                    "tile_island_" + c, kind, x0, z0, x1, z1,
                    TileLocalBlocks, TileLocalBlocks, 0, baseHeight));
            }

            // 3) 出生簇掩码：单位的 (gridX, gridY) 落在哪座岛，就给那座岛打上该队的标记。
            var masks = new int[infos.Count];
            IReadOnlyList<LevelUnit> units = level.Units;
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    LevelUnit u = units[i];
                    if (u.gridX < 0 || u.gridY < 0 || u.gridX >= w || u.gridY >= d)
                        continue;

                    int cluster = cellCluster[u.gridX + u.gridY * w];
                    if (cluster >= 0 && u.teamIndex >= 0 && u.teamIndex < 31)
                        masks[cluster] |= 1 << u.teamIndex;
                }
            }

            for (int c = 0; c < infos.Count; c++)
            {
                if (masks[c] == 0)
                    continue;

                PlatformClusterInfo info = infos[c];
                infos[c] = new PlatformClusterInfo(info.Name, info.Kind, info.X0, info.Z0, info.X1, info.Z1,
                    info.MinBlocks, info.MaxBlocks, masks[c], info.BaseHeight);
            }

            return new PlatformMap(w, d, blocks, cellCluster, infos);
        }

        /// <summary>连通域扩散的入栈判据（越界 / 已标号 / 非地面都不入栈）。</summary>
        static void PushIslandCell(int[] comp, LevelTileMapData tiles, int w, int x, int rowY, int island,
            List<int> stack)
        {
            if (x < 0 || rowY < 0 || x >= w || rowY >= tiles.Height)
                return;

            int idx = x + rowY * w;
            if (comp[idx] >= 0 || !tiles.IsSolidAt(x, rowY))
                return;

            comp[idx] = island;
            stack.Add(idx);
        }

        // ==================================================================
        // 通用推导：出生位 → 模板 → 过桥 → 定稿
        // ==================================================================

        /// <summary>形状模板（§2.2 的母题归并，5 个）。</summary>
        enum LayoutTemplate
        {
            /// <summary>双岛对峙：双方各一块大平台，中间按水距需要补踏脚石（L1/L3 母题）。</summary>
            TwinIslands,

            /// <summary>沉船残骸簇：中央一艘断成两截的船（档 1 的船关母题，R10）。</summary>
            WreckField,

            /// <summary>环形礁：一圈小岛围着中央（L10/L26 母题）。</summary>
            RingReef,

            /// <summary>碎岛雨：大量独立小落点（R9 高密度，L2/L12/L29 母题）。</summary>
            ScatterIslets,

            /// <summary>阶梯峰：中央一个多级台阶的大盘（R8/R13 垂直大关，L6/L14 母题）。</summary>
            SteppedPeak,
        }

        /// <summary>天空/地形档（§2.1，与 SkyTierCatalog 同口径）：1 = L1-5/L16-21，2 = L6-10/L22-27，3 = 其余。</summary>
        public static int ChapterOf(int levelNumber)
        {
            if (levelNumber <= 5) return 1;
            if (levelNumber <= 10) return 2;
            if (levelNumber <= 15) return 3;
            if (levelNumber <= 21) return 1;
            if (levelNumber <= 27) return 2;
            return 3;
        }

        /// <summary>
        /// 该关的空投武器池里是否有"越水武器"（R3：anchor / seagull / cannon / tidalWave）。
        /// 有水距上限判定用它——水距与武器池必须成对设计。
        /// </summary>
        public static bool HasCrossingWeapon(LevelData level)
        {
            IReadOnlyList<WeaponStack> pool = level.PotentialWeapons;
            if (pool == null)
                return false;

            for (int i = 0; i < pool.Count; i++)
            {
                switch (pool[i].id)
                {
                    case WeaponId.Anchor:
                    case WeaponId.Seagull:
                    case WeaponId.Cannon:
                    case WeaponId.TidalWave:
                        return true;
                }
            }
            return false;
        }

        /// <summary>本关允许的最大簇间水距（格）：R3 常规 1–5；配越水武器放宽；教学期（≤5 关）收紧。</summary>
        public static int MaxWaterGapFor(LevelData level)
        {
            if (level.LevelNumber <= 5)
                return 4;   // R18：教学期不出现长水距
            return HasCrossingWeapon(level) ? CrossingWeaponMaxWaterGap : RegularMaxWaterGap;
        }

        static LayoutTemplate TemplateFor(LevelData level)
        {
            int n = level.LevelNumber;

            // R18：前 5 关教学式简单布置（双岛 + 必要的踏脚石），不出极端形状。
            if (n <= 5)
                return LayoutTemplate.TwinIslands;

            float h = Hash01(n, 7, 3);
            int chapter = ChapterOf(n);

            if (chapter == 2)
            {
                // R8：垂直大关（阶梯峰）每 5 关一次，其余为环形礁/沉船残骸。
                if (n % 5 == 0)
                    return LayoutTemplate.SteppedPeak;
                return h < 0.5f ? LayoutTemplate.RingReef : LayoutTemplate.WreckField;
            }

            if (chapter == 3)
            {
                if (h < 0.34f) return LayoutTemplate.RingReef;
                if (h < 0.67f) return LayoutTemplate.ScatterIslets;
                return LayoutTemplate.SteppedPeak;
            }

            // 档 1（L16-21）：船与沙洲语汇（R10 船关只在档 1）。
            if (h < 0.34f) return LayoutTemplate.TwinIslands;
            if (h < 0.67f) return LayoutTemplate.WreckField;
            return LayoutTemplate.ScatterIslets;
        }

        static PlatformClusterKind MainKindFor(int chapter)
        {
            switch (chapter)
            {
                case 1: return PlatformClusterKind.Ship;
                case 2: return PlatformClusterKind.TerraceIsland;
                default: return PlatformClusterKind.SkyIsland;
            }
        }

        static PlatformClusterKind BirthKindFor(int chapter)
        {
            return chapter == 3 ? PlatformClusterKind.SkyIsland : PlatformClusterKind.TerraceIsland;
        }

        static PlatformClusterKind IsletKindFor(int chapter)
        {
            return chapter == 3 ? PlatformClusterKind.SkyIsland : PlatformClusterKind.TerraceIsland;
        }

        static PlatformMap BuildGeneric(LevelData level)
        {
            int w = level.WidthTiles;
            int d = level.HeightTiles;
            if (w <= 0 || d <= 0)
                return null;

            int chapter = ChapterOf(level.LevelNumber);
            bool teaching = level.LevelNumber <= 5;
            LayoutTemplate template = TemplateFor(level);

            var writer = new MapWriter(w, d);

            RectI red = SpawnRect(level, 0, w, d);
            RectI blue = SpawnRect(level, 1, w, d);

            // 两个出生矩形相交 → 双方出生位交错（原版 2P 变体的常见做法），
            // 按 R1/R6 合成**一个主导形状**：单簇容纳全部出生点，再补一个中立变奏簇。
            bool merged = Overlaps(red, blue);

            if (merged)
            {
                // 出生位交错：BuildMerged 已把"并集"整块盖成出生簇（掩码双队），
                // 这里**不能再调 StampSpawns**（它会只盖 red 矩形，把并集切成两个同名簇）。
                BuildMerged(writer, red, blue, chapter, teaching);
            }
            else
            {
                switch (template)
                {
                    case LayoutTemplate.WreckField:
                        BuildWreckField(writer, red, blue, teaching);
                        break;

                    case LayoutTemplate.RingReef:
                        BuildRingReef(writer, level, red, blue, chapter);
                        break;

                    case LayoutTemplate.ScatterIslets:
                        BuildScatterIslets(writer, level, red, blue, chapter, teaching);
                        break;

                    case LayoutTemplate.SteppedPeak:
                        BuildSteppedPeak(writer, red, blue, chapter, teaching);
                        break;

                    default:
                        BuildTwinIslands(writer, red, blue, chapter, teaching);
                        break;
                }

                // 出生簇最后盖（后盖覆盖先盖）：**这是"出生位绝不落水"的结构性保证**——
                // 无论模板在中间画了什么，出生矩形上的格最终都归出生簇、块高 ≥1。
                StampSpawns(writer, red, blue, chapter, teaching);

                int cap = MaxWaterGapFor(level);

                // R3/R16：把跨水链上剩余的超限水距用"踏脚石"补齐（模板只负责形状，
                // 水距上限统一在这里收口）。它只补"跨水链"（与出生带 Z 重叠的簇），
                // 且遇到已占位置会让步——所以单独跑它不保证出生簇连通。
                BridgeExcessGaps(writer, red, blue, cap, chapter);

                // 连通性兜底：若两个出生簇在 cap 水距内仍不连通（如巨图 + 环形礁的稀疏外圈），
                // 沿两者中心连线补一串踏脚石。**只在真的不连通时才动手**——否则会在
                // 已经够近的中立平台上打出一串多余的小岛（模板形状会被破坏）。
                if (!SpawnClustersConnected(writer, red, blue, cap))
                    EnsureSpawnPath(writer, red, blue, cap, chapter);
            }

            // 用户裁决 2：给每个岛分配**错落**的悬浮基准高度（0/3/6/9 世界单位阶梯）。
            // 放在所有二维构造（过桥/连通性）之后：那两条规则只管"横向水距"，基准高度是整簇平移，
            // 不改变任何格的水陆归属与包络，故互不干扰（竖直可达性由阶梯连续性保证，见 AssignBaseHeights）。
            return AssignBaseHeights(level.LevelNumber, writer.Finalize());
        }

        // ==================================================================
        // 用户裁决 2：错落基准高度（0/3/6/9 世界单位阶梯）
        // ==================================================================

        /// <summary>
        /// 给整张地图的每个簇分配**悬浮基准高度**（确定性：由 levelNumber + 簇索引的哈希定名次）。
        ///
        /// 【三条硬约束（都由"可玩性"倒推，出处见类头的量化依据）】
        ///   · 出生岛贴水：anchor 出生簇（红队优先）强制落在阶梯最低档 0，另一支出生簇压到 ≤3，
        ///     保证开局双方站在离水面最近的岛上、画面可读（用户裁决："至少一个贴水档 0-3"）；
        ///   · 跨度 ≥ 1 档：至少两个簇拿到不同档（否则与"同层平铺"无异）；
        ///   · 阶梯连续：用到的档位必须是 0..M 的前缀（用了 9 就必须有 3/6 的中继岛）——
        ///     单发投掷的竖直顶点只有 ≈2.06 单位（见类头），跳档会让高岛变成打不到的孤岛。
        ///
        /// 【为什么用"名次 → 档位"而不是"直接取哈希档位"】直接取档位会出现"整关全是 0"或
        /// "跳档（0 与 9 无中继）"；按名次均匀铺到 0..M 天然覆盖全部档位（步长 ≤ 1 档），
        /// 再补一次"出生岛压档 + 补阶"就同时满足三条约束。
        /// </summary>
        static PlatformMap AssignBaseHeights(int levelNumber, PlatformMap map)
        {
            if (map == null || map.Clusters.Count < 2)
                return map;

            int n = map.Clusters.Count;
            int maxLadder = Mathf.Min(BaseHeightLadder.Length - 1, n - 1);

            // 1) 名次：按确定性哈希升序（稳定插入排序，避免 System.Random / LINQ 的跨运行时差异）。
            var keys = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = Hash01(levelNumber, i, 53);
                order[i] = i;
            }

            for (int i = 1; i < n; i++)
            {
                int k = order[i];
                float kv = keys[k];
                int j = i - 1;
                while (j >= 0 && keys[order[j]] > kv)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = k;
            }

            // 2) 名次 → 档位（p·maxLadder/(n-1) 四舍五入：步长 ≤ 1 档，天然覆盖 0..maxLadder）。
            var ladderIndex = new int[n];
            for (int p = 0; p < n; p++)
            {
                int li = Mathf.RoundToInt(p * maxLadder / (float)(n - 1));
                ladderIndex[order[p]] = Mathf.Clamp(li, 0, maxLadder);
            }

            // 3) 出生岛贴水：anchor（红队优先）压到最低档，另一支出生簇压到 ≤1 档。
            int anchor = FirstSpawnCluster(map.Clusters);
            if (anchor >= 0)
                ladderIndex[anchor] = 0;

            int other = -1;
            for (int i = 0; i < n; i++)
            {
                if (i != anchor && map.Clusters[i].IsSpawnCluster)
                {
                    other = i;
                    break;
                }
            }

            if (other >= 0)
                ladderIndex[other] = Mathf.Min(ladderIndex[other], 1);

            // 4) 跨度兜底：全压平（整关同高）时把一个非出生簇抬到 1 档。
            int maxUsed = 0;
            for (int i = 0; i < n; i++)
                maxUsed = Mathf.Max(maxUsed, ladderIndex[i]);

            if (maxUsed < 1)
            {
                for (int p = n - 1; p >= 0; p--)
                {
                    int i = order[p];
                    if (!map.Clusters[i].IsSpawnCluster)
                    {
                        ladderIndex[i] = 1;
                        maxUsed = 1;
                        break;
                    }
                }
            }

            // 5) 补阶：保证用到的档位是 0..M 的连续前缀（缺哪一档就把最高的一个簇降下来补它）。
            for (int v = 1; v <= maxUsed; v++)
            {
                if (UsedLadderIndex(ladderIndex, v))
                    continue;

                int victim = -1;
                for (int i = 0; i < n; i++)
                {
                    if (ladderIndex[i] > v)
                        victim = i;
                }

                if (victim < 0)
                    break;   // 没有更高的可降，接受当前跨度

                ladderIndex[victim] = v;
            }

            // 6) 阶梯档位 → 世界单位高度，重建簇列表（只换 BaseHeight，包络/Kind/掩码全部保留）。
            var rebuilt = new List<PlatformClusterInfo>(n);
            for (int i = 0; i < n; i++)
                rebuilt.Add(map.Clusters[i].WithBaseHeight(BaseHeightLadder[ladderIndex[i]]));

            return new PlatformMap(map.WidthTiles, map.DepthTiles, map.CellBlocks, map.CellCluster, rebuilt);
        }

        static bool UsedLadderIndex(int[] index, int v)
        {
            for (int i = 0; i < index.Length; i++)
            {
                if (index[i] == v)
                    return true;
            }
            return false;
        }

        /// <summary>第一支出生簇（红队优先）的下标；没有出生簇返回 -1。</summary>
        static int FirstSpawnCluster(IReadOnlyList<PlatformClusterInfo> clusters)
        {
            for (int i = 0; i < clusters.Count; i++)
            {
                if (clusters[i].SpawnsTeam(0))
                    return i;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                if (clusters[i].IsSpawnCluster)
                    return i;
            }

            return -1;
        }

        /// <summary>两个出生簇（各自中心所在的簇）在"水距 ≤ cap"的图里是否同一连通分量。</summary>
        static bool SpawnClustersConnected(MapWriter writer, RectI red, RectI blue, int cap)
        {
            List<PlatformClusterInfo> infos = writer.PeekClusterBounds();
            if (infos.Count == 0)
                return false;

            int a = ClusterIndexAt(infos, red.CenterX, red.CenterZ);
            int b = ClusterIndexAt(infos, blue.CenterX, blue.CenterZ);
            if (a < 0 || b < 0)
                return false;
            if (a == b)
                return true;

            var parent = new int[infos.Count];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            for (int i = 0; i < infos.Count; i++)
            {
                for (int j = i + 1; j < infos.Count; j++)
                {
                    if (WaterGapTiles(infos[i], infos[j]) > cap)
                        continue;

                    int ri = Find(parent, i), rj = Find(parent, j);
                    if (ri != rj)
                        parent[rj] = ri;
                }
            }

            return Find(parent, a) == Find(parent, b);
        }

        static int ClusterIndexAt(List<PlatformClusterInfo> infos, int gx, int gz)
        {
            for (int i = 0; i < infos.Count; i++)
            {
                if (infos[i].Contains(gx, gz))
                    return i;
            }
            return -1;
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        /// <summary>
        /// 沿两个出生矩形的中心连线铺一串 2×2 踏脚石，间距 ≤ <paramref name="maxGap"/> 格，
        /// 使"红出生簇 ↔ 蓝出生簇"在 R3/R16 的水距上限内**连通**（确定性，与模板无关）。
        ///
        /// 【为什么不直接判定连通性再补】玩家/ AI 只需要"打得过去"，而这由**每一段水距 ≤ 上限**
        /// 保证；沿直线布点是最简且必然满足的构造（每段切比雪夫距离 ≤ 上限 → 包络水距 ≤ 上限）。
        /// 【为什么跳过与出生矩形相交的位置】覆盖出生簇会把它的格子改属中立簇，
        /// 破坏"出生位所在簇带出生掩码"这一不变量（也会让出生簇包络变小）。
        /// </summary>
        static void EnsureSpawnPath(MapWriter writer, RectI red, RectI blue, int maxGap, int chapter)
        {
            if (maxGap <= 0)
                return;

            int rx = red.CenterX, rz = red.CenterZ;
            int bx = blue.CenterX, bz = blue.CenterZ;

            int cheb = Mathf.Max(Mathf.Abs(bx - rx), Mathf.Abs(bz - rz));
            if (cheb <= maxGap)
                return;   // 已经一步可跨

            int steps = Mathf.Max(1, Mathf.CeilToInt(cheb / (float)maxGap));
            for (int k = 1; k < steps; k++)
            {
                float t = k / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(rx, bx, t));
                int z = Mathf.RoundToInt(Mathf.Lerp(rz, bz, t));

                RectI r = ClampRect(new RectI(x, z, x + 1, z + 1), writer.W, writer.D);
                if (r.Width <= 0 || r.Depth <= 0)
                    continue;
                if (Intersects(r, red) || Intersects(r, blue))
                    continue;   // 落进出生簇的位置交给出生簇本身承担
                if (writer.AnyOwned(r))
                    continue;   // 位置已被模板簇占用：让位（原实现会覆盖它，切成碎片）

                int idx = writer.NewCluster("spawn_path_islet", IsletKindFor(chapter), 0);
                writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, 2);
            }
        }

        static bool Intersects(RectI a, RectI b)
        {
            if (a.Width <= 0 || b.Width <= 0)
                return false;
            return a.X0 <= b.X1 && b.X0 <= a.X1 && a.Z0 <= b.Z1 && b.Z0 <= a.Z1;
        }

        /// <summary>出生位交错（矩形相交）：单簇 + 一个中立卫星（R1/R6）。</summary>
        static void BuildMerged(MapWriter writer, RectI red, RectI blue, int chapter, bool teaching)
        {
            RectI u = new RectI(
                Mathf.Min(red.X0, blue.X0), Mathf.Min(red.Z0, blue.Z0),
                Mathf.Max(red.X1, blue.X1), Mathf.Max(red.Z1, blue.Z1));

            int shared = writer.NewCluster("spawn_shared", MainKindFor(chapter), 0x3);
            StampBirth(writer, shared, u, teaching, MainKindFor(chapter) == PlatformClusterKind.Ship);

            PlatformClusterKind satelliteKind = MainKindFor(chapter);
            AddSatellite(writer, u, satelliteKind, +1);
            AddSatellite(writer, u, satelliteKind, -1);
        }

        // ------------------------------------------------------------------
        // 模板 1：双岛对峙
        // ------------------------------------------------------------------

        static void BuildTwinIslands(MapWriter writer, RectI red, RectI blue, int chapter, bool teaching)
        {
            int gap = GapX(red, blue);

            if (gap >= 6)
            {
                // 用户裁决 3：「主岛 12-20 格宽量级的大岛（按关卡宽度适配）」——
                // 宽度吃到"两出生簇之间的可用水面 − 每侧 2 格水道"，上限 20；窄图自然退化。
                int width = Mathf.Clamp(gap - 4, 3, Mathf.Min(20, Mathf.Max(3, writer.W - 4)));
                int depth = Mathf.Clamp(Mathf.Min(red.Depth, blue.Depth) + 2, 5, writer.D - 2);
                int cx = (red.X1 + blue.X0) / 2;
                int cz = (red.CenterZ + blue.CenterZ) / 2;

                RectI r = ClampRect(new RectI(cx - width / 2, cz - depth / 2,
                    cx - width / 2 + width - 1, cz - depth / 2 + depth - 1), writer.W, writer.D);
                if (r.Width >= 3 && r.Depth >= 3)
                {
                    int platform = writer.NewCluster("main_center", MainKindFor(chapter), 0);
                    // 不规则岛形（Ship 簇除外——船体本来就是盒状船壳，见 StampIsland 的用途说明）。
                    if (MainKindFor(chapter) == PlatformClusterKind.Ship)
                        writer.Stamp(platform, r.X0, r.Z0, r.X1, r.Z1, 1);
                    else
                        writer.StampIsland(platform, r.X0, r.Z0, r.X1, r.Z1, 1, IslandSeed(platform));

                    if (!teaching)
                    {
                        RectI inner = Shrink(r, 1);
                        if (inner.Width >= 2 && inner.Depth >= 2)
                        {
                            if (MainKindFor(chapter) == PlatformClusterKind.Ship)
                                writer.Stamp(platform, inner.X0, inner.Z0, inner.X1, inner.Z1, 2);
                            else
                                writer.StampIsland(platform, inner.X0, inner.Z0, inner.X1, inner.Z1,
                                    2, IslandSeed(platform));
                        }
                    }
                }
            }
            else
            {
                // 水距已经够近（≤5）：不加中央平台，保持"两岛隔水对望"的干净导语；
                // 只在南北各补一块跳板（R9：簇数落在教学 5–9 / 常规 7–12 的档位）。
                AddSatellite(writer, red, IsletKindFor(chapter), +1);
                AddSatellite(writer, blue, IsletKindFor(chapter), -1);
            }
        }

        /// <summary>在锚点簇的北（zSign&gt;0）/ 南侧隔 3 格水放一块 2×2 中立跳板（R6 的"次要变奏"）。</summary>
        static void AddSatellite(MapWriter writer, RectI anchor, PlatformClusterKind kind, int zSign)
        {
            const int size = 2;
            int cx = anchor.CenterX;
            int cz = zSign > 0 ? anchor.Z1 + 3 : anchor.Z0 - 3 - (size - 1);
            int idx = writer.NewCluster("islet_satellite", kind, 0);
            RectI r = ClampRect(new RectI(cx - size / 2, cz, cx - size / 2 + size - 1, cz + size - 1),
                writer.W, writer.D);
            if (r.Width > 0 && r.Depth > 0)
                writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, 2);
        }

        // ------------------------------------------------------------------
        // 模板 2：沉船残骸簇（档 1 的船关母题）
        // ------------------------------------------------------------------

        /// <summary>岛形掩码的确定性种子（由簇下标派生；与关卡号无关，故模板函数无需透传 levelNumber）。</summary>
        static int IslandSeed(int clusterIndex) => 9001 + clusterIndex * 131;

        static void BuildWreckField(MapWriter writer, RectI red, RectI blue, bool teaching)
        {
            int gap = GapX(red, blue);
            if (gap < 10)
            {
                // 中间放不下"断成两截"的船 → 退回单块中立平台（仍是同一个"沉船"记忆点）。
                BuildWreckHulk(writer, red, blue, teaching);
                AddSatellite(writer, red, PlatformClusterKind.Ship, +1);
                AddSatellite(writer, blue, PlatformClusterKind.Ship, -1);
                return;
            }

            // 两截船体 + 中间 1 格水道（断口）。
            int depth = Mathf.Clamp(Mathf.Min(red.Depth, blue.Depth), 4, writer.D - 2);
            int cz = (red.CenterZ + blue.CenterZ) / 2;
            int mid = (red.X1 + blue.X0) / 2;

            RectI left = ClampRect(new RectI(red.X1 + 2, cz - depth / 2, mid - 1, cz - depth / 2 + depth - 1),
                writer.W, writer.D);
            RectI right = ClampRect(new RectI(mid + 1, cz - depth / 2, blue.X0 - 2, cz - depth / 2 + depth - 1),
                writer.W, writer.D);

            if (left.Width < 3 || right.Width < 3)
            {
                BuildWreckHulk(writer, red, blue, teaching);
                return;
            }

            int bow = writer.NewCluster("wreck_bow", PlatformClusterKind.Ship, 0);
            int stern = writer.NewCluster("wreck_stern", PlatformClusterKind.Ship, 0);
            StampHull(writer, bow, left, raised: true);      // 船首楼 = 制高点（R10/R13）
            StampHull(writer, stern, right, raised: true);

            AddSatellite(writer, red, PlatformClusterKind.Ship, +1);
            AddSatellite(writer, blue, PlatformClusterKind.Ship, -1);
        }

        static void BuildWreckHulk(MapWriter writer, RectI red, RectI blue, bool teaching)
        {
            int width = Mathf.Clamp(GapX(red, blue) - 4, 3, Mathf.Min(16, Mathf.Max(3, writer.W - 4)));
            int depth = Mathf.Clamp(Mathf.Min(red.Depth, blue.Depth), 4, writer.D - 2);
            RectI r = ClampRect(CenterBetween(red, blue, width, depth), writer.W, writer.D);
            int one = writer.NewCluster("wreck_hulk", PlatformClusterKind.Ship, 0);
            if (r.Width >= 3 && r.Depth >= 3)
                StampHull(writer, one, r, raised: !teaching);
        }

        /// <summary>船体剖面：外壳 1 块 → 甲板 2 块 → 楼 4 块（内缩两级，读得出"船头楼"）。</summary>
        static void StampHull(MapWriter writer, int idx, RectI r, bool raised)
        {
            writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, 1);
            RectI deck = Shrink(r, 1);
            if (deck.Width >= 2 && deck.Depth >= 2)
                writer.Stamp(idx, deck.X0, deck.Z0, deck.X1, deck.Z1, 2);
            if (!raised)
                return;

            RectI castle = Shrink(deck, 1);
            if (castle.Width >= 2 && castle.Depth >= 2)
                writer.Stamp(idx, castle.X0, castle.Z0, castle.X1, castle.Z1, 4);
        }

        // ------------------------------------------------------------------
        // 模板 3：环形礁
        // ------------------------------------------------------------------

        static void BuildRingReef(MapWriter writer, LevelData level, RectI red, RectI blue, int chapter)
        {
            int cx = (red.CenterX + blue.CenterX) / 2;
            int cz = writer.D / 2;

            int radiusX = Mathf.Clamp(Mathf.Abs(blue.CenterX - red.CenterX) / 2 - 2,
                4, Mathf.Max(4, writer.W / 2 - 2));
            int radiusZ = Mathf.Clamp(writer.D / 2 - 2, 3, Mathf.Max(3, writer.D / 2 - 1));

            // 用户裁决 3「每张图要有大块可读剪影」：半径够大时环形中心放一座大岛
            // （旧实现只在"环上一个都没放下"时才补中心，等于环形礁没有主岛、全是小砖）。
            if (radiusX >= 8 && radiusZ >= 6)
            {
                int cw = Mathf.Clamp(radiusX * 2 / 3, 6, 16);
                int cd = Mathf.Clamp(radiusZ, 5, writer.D - 2);
                RectI cr = ClampRect(new RectI(cx - cw / 2, cz - cd / 2,
                    cx - cw / 2 + cw - 1, cz - cd / 2 + cd - 1), writer.W, writer.D);

                if (cr.Width >= 4 && cr.Depth >= 4)
                {
                    int core = writer.NewCluster("ring_core", MainKindFor(chapter), 0);
                    int coreSeed = IslandSeed(core);
                    writer.StampIsland(core, cr.X0, cr.Z0, cr.X1, cr.Z1, 1, coreSeed);

                    RectI coreInner = Shrink(cr, 1);
                    if (coreInner.Width >= 2 && coreInner.Depth >= 2)
                        writer.StampIsland(core, coreInner.X0, coreInner.Z0,
                            coreInner.X1, coreInner.Z1, 2, coreSeed);
                }
            }

            // 环上小岛：半径小（环挤）时用 3×3（低于岛形掩码下限，挤在一起也不会互相啃），
            // 半径大时用 5×5（走岛形掩码 → 读作一圈不规则礁岛而不是一圈方砖）。
            int half = radiusX >= 7 ? 2 : 1;
            const int ring = 8;
            int placed = 0;
            for (int i = 0; i < ring; i++)
            {
                float ang = i / (float)ring * Mathf.PI * 2f + Hash01(level.LevelNumber, i, 5) * 0.25f;
                int ix = cx + Mathf.RoundToInt(Mathf.Cos(ang) * radiusX);
                int iz = cz + Mathf.RoundToInt(Mathf.Sin(ang) * radiusZ);

                int idx = writer.NewCluster("ring_islet_" + i, IsletKindFor(chapter), 0);
                RectI r = ClampRect(new RectI(ix - half, iz - half, ix + half, iz + half), writer.W, writer.D);
                if (r.Width <= 0 || r.Depth <= 0)
                    continue;
                if (writer.AnyOwned(r))
                    continue;   // 中央主岛已占位：让出这一段（环形构图不变）

                writer.StampIsland(idx, r.X0, r.Z0, r.X1, r.Z1, 2, IslandSeed(idx));
                placed++;
            }

            if (placed == 0)
                StampFallbackCenterCore(writer, red, blue, chapter);
        }

        static void StampFallbackCenterCore(MapWriter writer, RectI red, RectI blue, int chapter)
        {
            int idx = writer.NewCluster("main_center", MainKindFor(chapter), 0);
            RectI r = ClampRect(CenterBetween(red, blue, 4, 4), writer.W, writer.D);
            if (r.Width > 0 && r.Depth > 0)
                writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, 1);
        }

        // ------------------------------------------------------------------
        // 模板 4：碎岛雨（R9 高密度）
        // ------------------------------------------------------------------

        static void BuildScatterIslets(MapWriter writer, LevelData level, RectI red, RectI blue,
            int chapter, bool teaching)
        {
            int area = writer.W * writer.D;
            int count = Mathf.Clamp(area / 90, 6, 21);   // R9：高密关 19–21 簇
            if (teaching)
                count = Mathf.Min(count, 8);

            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                int cx = 1 + (int)(Hash01(level.LevelNumber, i, 11) * Mathf.Max(1, writer.W - 6));
                int cz = 1 + (int)(Hash01(level.LevelNumber, i, 23) * Mathf.Max(1, writer.D - 6));
                // 用户裁决 3：碎岛雨的每一块也要读成"一座小岛"（3-5 × 3-4），
                // 而不是 2×2 的砖（过小的矩形会被 StampIsland 的启用下限挡在掩码之外）。
                int width = 3 + (int)(Hash01(level.LevelNumber, i, 31) * 3f);
                int depth = 3 + (int)(Hash01(level.LevelNumber, i, 37) * 2f);

                int idx = writer.NewCluster("scatter_islet_" + i, IsletKindFor(chapter), 0);
                RectI r = ClampRect(new RectI(cx, cz, cx + width - 1, cz + depth - 1), writer.W, writer.D);
                if (r.Width <= 0 || r.Depth <= 0)
                    continue;
                if (writer.AnyOwned(r))
                    continue;   // 碎岛变大后可能互相压到：压到就让位（宁少一块，不叠成一坨）

                int blocks = Hash01(level.LevelNumber, i, 41) < 0.5f ? 1 : 2;
                writer.StampIsland(idx, r.X0, r.Z0, r.X1, r.Z1, blocks, IslandSeed(idx));
                placed++;
            }

            // 碎岛雨本身就是"高密掩体"模板（R9），不再额外补跳板。
            if (placed == 0)
                StampFallbackCenterCore(writer, red, blue, chapter);
        }

        // ------------------------------------------------------------------
        // 模板 5：阶梯峰（R8/R13）
        // ------------------------------------------------------------------

        static void BuildSteppedPeak(MapWriter writer, RectI red, RectI blue, int chapter, bool teaching)
        {
            int gap = GapX(red, blue);
            int width = Mathf.Clamp(gap - 4, 5, Mathf.Max(5, writer.W / 3));
            int depth = Mathf.Clamp(writer.D - 4, 6, writer.D - 2);

            RectI r = ClampRect(CenterBetween(red, blue, width, depth), writer.W, writer.D);
            if (r.Width < 4 || r.Depth < 4)
            {
                StampFallbackCenterCore(writer, red, blue, chapter);
                return;
            }

            int peak = writer.NewCluster("stepped_peak", MainKindFor(chapter), 0);

            // 阶梯峰的每一级都走岛形掩码 → 整座山是一座"岛"，而不是一摞矩形砖。
            int peakSeed = IslandSeed(peak);
            writer.StampIsland(peak, r.X0, r.Z0, r.X1, r.Z1, 1, peakSeed);

            RectI step2 = Shrink(r, 1);
            if (step2.Width >= 3 && step2.Depth >= 3)
                writer.StampIsland(peak, step2.X0, step2.Z0, step2.X1, step2.Z1, 2, peakSeed);

            RectI step3 = Shrink(step2, 1);
            if (!teaching && step3.Width >= 2 && step3.Depth >= 2)
                writer.StampIsland(peak, step3.X0, step3.Z0, step3.X1, step3.Z1, 3, peakSeed);

            // 峰顶（≥9 层的原版"垂直大关"在本项目压缩为 5 块）——记忆点（R13）。
            RectI summit = Shrink(step3, 1);
            if (!teaching && summit.Width >= 2 && summit.Depth >= 2)
                writer.StampIsland(peak, summit.X0, summit.Z0, summit.X1, summit.Z1,
                    MaxBlocksPerCluster, peakSeed);

            // 峰体两侧各一块跳板，簇数进入 R9 的常规档。
            AddSatellite(writer, red, IsletKindFor(chapter), +1);
            AddSatellite(writer, blue, IsletKindFor(chapter), -1);
        }

        // ------------------------------------------------------------------
        // 出生簇（R1/R2/R18）
        // ------------------------------------------------------------------

        /// <summary>出生簇（红/蓝各一；只在<b>非合并</b>分支调用——合并分支见 <see cref="BuildMerged"/>）。</summary>
        static void StampSpawns(MapWriter writer, RectI red, RectI blue, int chapter, bool teaching)
        {
            int r = writer.NewCluster("spawn_red", BirthKindFor(chapter), 1 << 0);
            StampBirth(writer, r, red, teaching, false);

            int b = writer.NewCluster("spawn_blue", BirthKindFor(chapter), 1 << 1);
            StampBirth(writer, b, blue, teaching, false);
        }

        /// <summary>
        /// 出生平台：整块 1 块高（R2 保证每点 ≥4 格）；非教学关内缩一级抬高到 2 块（出生台地）。
        ///
        /// 【2026-09-14 用户裁决 1：出生岛也要是"岛"】外轮廓走岛形掩码，但**出生矩形本身是受保护内核**
        /// （<see cref="MapWriter.StampIslandMasked"/> 的 protect 参数）——"出生位绝不落水"由结构保证，
        /// 不靠运气；受保护的只是内核，外壳那 1 格边缘仍会被侵蚀/外凸 → 出生岛不再是方砖。
        /// </summary>
        static void StampBirth(MapWriter writer, int idx, RectI r, bool teaching, bool shipLookout)
        {
            RectI shell = Grow(r, 1, writer.W, writer.D);

            // 外圈已被别的簇占了 → 不向外长（否则会把邻岛啃成碎片），退回实心矩形。
            if (shell.Width <= r.Width && shell.Depth <= r.Depth || writer.AnyOwnedOutside(shell, r))
                shell = r;

            writer.StampIslandMasked(idx, shell.X0, shell.Z0, shell.X1, shell.Z1,
                r.X0, r.Z0, r.X1, r.Z1, 1, IslandSeed(idx));

            if (teaching)
                return;   // R18：教学期不多层

            RectI inner = Shrink(r, 1);
            if (inner.Width >= 2 && inner.Depth >= 2)
                writer.Stamp(idx, inner.X0, inner.Z0, inner.X1, inner.Z1, 2);

            if (!shipLookout)
                return;

            RectI lookout = Shrink(inner, 1);
            if (lookout.Width >= 2 && lookout.Depth >= 2)
                writer.Stamp(idx, lookout.X0, lookout.Z0, lookout.X1, lookout.Z1, 3);
        }

        // ------------------------------------------------------------------
        // 过桥：把超过上限的水距用"踏脚小岛"补齐（R3/R16）
        // ------------------------------------------------------------------

        static void BridgeExcessGaps(MapWriter writer, RectI red, RectI blue, int maxGap, int chapter)
        {
            if (maxGap <= 0)
                return;

            int bandZ0 = Mathf.Min(red.Z0, blue.Z0);
            int bandZ1 = Mathf.Max(red.Z1, blue.Z1);

            for (int guard = 0; guard < 64; guard++)
            {
                List<PlatformClusterInfo> infos = writer.PeekClusterBounds();
                if (infos == null || infos.Count < 2)
                    return;

                // 只桥接"与出发带 Z 有重叠"的簇，避免把环形礁的南北岛串成一条链。
                var inBand = new List<int>();
                for (int i = 0; i < infos.Count; i++)
                {
                    if (infos[i].Z1 >= bandZ0 && infos[i].Z0 <= bandZ1)
                        inBand.Add(i);
                }
                if (inBand.Count < 2)
                    return;

                inBand.Sort((x, y) => infos[x].X0.CompareTo(infos[y].X0));

                bool inserted = false;
                for (int k = 0; k + 1 < inBand.Count; k++)
                {
                    PlatformClusterInfo a = infos[inBand[k]];
                    PlatformClusterInfo b = infos[inBand[k + 1]];
                    int gap = GapX(a, b);
                    if (gap <= maxGap)
                        continue;

                    int ix0 = a.X1 + maxGap + 1;
                    int iz = (a.Z0 + a.Z1) / 2;
                    int size = 2;
                    RectI r = ClampRect(new RectI(ix0, iz, ix0 + size - 1, iz + size - 1), writer.W, writer.D);
                    if (r.Width <= 0 || r.Depth <= 0 || r.X1 >= b.X0)
                        break;

                    if (writer.AnyOwned(r))
                        break;   // 位置被占：放弃这一对（宁可保留长水距，也不重叠）

                    int idx = writer.NewCluster("stepping_islet", IsletKindFor(chapter), 0);
                    writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, 2);
                    inserted = true;
                    break;
                }

                if (!inserted)
                    return;
            }
        }

        // ------------------------------------------------------------------
        // 出生位 → 矩形（R1/R2）
        // ------------------------------------------------------------------

        static RectI SpawnRect(LevelData level, int team, int w, int d)
        {
            int count = 0;
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;

            IReadOnlyList<LevelUnit> units = level.Units;
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    if (units[i].teamIndex != team)
                        continue;

                    count++;
                    x0 = Mathf.Min(x0, units[i].gridX); x1 = Mathf.Max(x1, units[i].gridX);
                    z0 = Mathf.Min(z0, units[i].gridY); z1 = Mathf.Max(z1, units[i].gridY);
                }
            }

            if (count == 0)
            {
                // 该队无出战单位（Boss 关 / 数据缺位）：给一条与对面镜像的默认平台。
                int margin = Mathf.Clamp(w / 6, 1, 5);
                int dz = Mathf.Clamp(d / 3, 1, 4);
                int cz = d / 2;
                if (team == 0)
                    return ClampRect(new RectI(1, cz - dz, 1 + margin + 2, cz + dz), w, d);
                return ClampRect(new RectI(w - 2 - margin - 2, cz - dz, w - 2, cz + dz), w, d);
            }

            var r = new RectI(x0, z0, x1, z1);
            r = Grow(r, 1, w, d);                       // 外扩 1 格（出生平台边缘）
            r = EnsureMin(r, 4, 3, w, d);               // R1：出生平台至少 4×3
            r = GrowForArea(r, 4 * count, w, d);        // R2：每出生点 ≥4 格平台
            return ClampRect(r, w, d);
        }

        // ------------------------------------------------------------------
        // 几何小工具
        // ------------------------------------------------------------------

        readonly struct RectI
        {
            public readonly int X0, Z0, X1, Z1;

            public RectI(int x0, int z0, int x1, int z1)
            {
                X0 = x0; Z0 = z0; X1 = x1; Z1 = z1;
            }

            public int Width => X1 - X0 + 1;
            public int Depth => Z1 - Z0 + 1;
            public int Area => Width * Depth;
            public int CenterX => (X0 + X1) / 2;
            public int CenterZ => (Z0 + Z1) / 2;
        }

        static RectI ClampRect(RectI r, int w, int d)
        {
            int x0 = Mathf.Max(0, r.X0), z0 = Mathf.Max(0, r.Z0);
            int x1 = Mathf.Min(w - 1, r.X1), z1 = Mathf.Min(d - 1, r.Z1);
            if (x0 > x1 || z0 > z1)
                return new RectI(0, 0, -1, -1);   // 空矩形
            return new RectI(x0, z0, x1, z1);
        }

        static RectI Grow(RectI r, int pad, int w, int d)
        {
            return ClampRect(new RectI(r.X0 - pad, r.Z0 - pad, r.X1 + pad, r.Z1 + pad), w, d);
        }

        static RectI Shrink(RectI r, int pad)
        {
            if (r.Width <= pad * 2 || r.Depth <= pad * 2)
                return new RectI(0, 0, -1, -1);
            return new RectI(r.X0 + pad, r.Z0 + pad, r.X1 - pad, r.Z1 - pad);
        }

        static RectI EnsureMin(RectI r, int minW, int minD, int w, int d)
        {
            if (r.Width <= 0 || r.Depth <= 0)
                return r;

            int x0 = r.X0, z0 = r.Z0, x1 = r.X1, z1 = r.Z1;
            while (x1 - x0 + 1 < minW)
            {
                int before = x1 - x0;
                if (x1 + 1 < w) x1++;
                else if (x0 - 1 >= 0) x0--;
                if (x1 - x0 == before)
                    break;
            }
            while (z1 - z0 + 1 < minD)
            {
                int before = z1 - z0;
                if (z1 + 1 < d) z1++;
                else if (z0 - 1 >= 0) z0--;
                if (z1 - z0 == before)
                    break;
            }
            return ClampRect(new RectI(x0, z0, x1, z1), w, d);
        }

        static RectI GrowForArea(RectI r, int neededCells, int w, int d)
        {
            if (r.Width <= 0 || r.Depth <= 0)
                return r;

            int x0 = r.X0, z0 = r.Z0, x1 = r.X1, z1 = r.Z1;
            int guard = 0;
            while ((x1 - x0 + 1) * (z1 - z0 + 1) < neededCells && guard++ < 512)
            {
                bool changed = false;
                if (x1 - x0 <= z1 - z0)
                {
                    if (x1 + 1 < w) { x1++; changed = true; }
                    else if (x0 - 1 >= 0) { x0--; changed = true; }
                }
                else
                {
                    if (z1 + 1 < d) { z1++; changed = true; }
                    else if (z0 - 1 >= 0) { z0--; changed = true; }
                }

                if (!changed)
                    break;   // 顶到地图边界，面积只能这么大
            }
            return ClampRect(new RectI(x0, z0, x1, z1), w, d);
        }

        static RectI CenterBetween(RectI red, RectI blue, int width, int depth)
        {
            int cx = ((red.CenterX + blue.CenterX) / 2);
            int cz = ((red.CenterZ + blue.CenterZ) / 2);
            width = Mathf.Max(1, width);
            depth = Mathf.Max(1, depth);
            return new RectI(cx - width / 2, cz - depth / 2, cx - width / 2 + width - 1, cz - depth / 2 + depth - 1);
        }

        static bool Overlaps(RectI a, RectI b)
        {
            if (a.Width <= 0 || b.Width <= 0)
                return false;
            return a.X0 <= b.X1 && b.X0 <= a.X1 && a.Z0 <= b.Z1 && b.Z0 <= a.Z1;
        }

        static int GapX(RectI a, RectI b) => GapX(a.X0, a.X1, b.X0, b.X1);

        static int GapX(PlatformClusterInfo a, PlatformClusterInfo b)
            => GapX(a.X0, a.X1, b.X0, b.X1);

        static int GapX(int a0, int a1, int b0, int b1)
        {
            if (b0 > a1)
                return b0 - a1 - 1;
            if (a0 > b1)
                return a0 - b1 - 1;
            return 0;   // X 上重叠
        }

        // ------------------------------------------------------------------
        // 确定性哈希（自实现 LCG 风格整数混合；不依赖 System.Random）
        // ------------------------------------------------------------------

        static int Hash3(int a, int b, int c)
        {
            unchecked
            {
                int h = a * 73856093 ^ b * 19349663 ^ c * 83492791;
                h ^= h >> 13;
                h *= 1274126177;
                h ^= h >> 16;
                return h & 0x7fffffff;
            }
        }

        static float Hash01(int a, int b, int c) => Hash3(a, b, c) / (float)int.MaxValue;

        // ==================================================================
        // 写入器：逐格盖簇 + 定稿成不重叠的簇包络
        // ==================================================================

        sealed class ClusterDraft
        {
            public readonly string Name;
            public readonly PlatformClusterKind Kind;
            public readonly int SpawnTeamMask;

            public ClusterDraft(string name, PlatformClusterKind kind, int spawnTeamMask)
            {
                Name = name; Kind = kind; SpawnTeamMask = spawnTeamMask;
            }
        }

        sealed class MapWriter
        {
            public readonly int W;
            public readonly int D;

            readonly int[] _owner;
            readonly int[] _blocks;
            readonly List<ClusterDraft> _drafts = new List<ClusterDraft>();

            public MapWriter(int w, int d)
            {
                W = w;
                D = d;
                _owner = new int[w * d];
                _blocks = new int[w * d];
                for (int i = 0; i < _owner.Length; i++)
                    _owner[i] = -1;
            }

            public int DraftCount => _drafts.Count;

            public int NewCluster(string name, PlatformClusterKind kind, int spawnTeamMask)
            {
                _drafts.Add(new ClusterDraft(name, kind, spawnTeamMask));
                return _drafts.Count - 1;
            }

            public void Stamp(int clusterIndex, int x0, int z0, int x1, int z1, int blocks)
            {
                if (blocks <= 0 || clusterIndex < 0)
                    return;

                if (x0 > x1) { int t = x0; x0 = x1; x1 = t; }
                if (z0 > z1) { int t = z0; z0 = z1; z1 = t; }
                x0 = Mathf.Max(0, x0); z0 = Mathf.Max(0, z0);
                x1 = Mathf.Min(W - 1, x1); z1 = Mathf.Min(D - 1, z1);

                for (int gz = z0; gz <= z1; gz++)
                {
                    int row = gz * W;
                    for (int gx = x0; gx <= x1; gx++)
                    {
                        int i = row + gx;
                        _owner[i] = clusterIndex;
                        _blocks[i] = blocks;
                    }
                }
            }

            /// <summary>
            /// **不规则岛形** stamp：在矩形掩码上做确定性的边缘"侵蚀 / 外凸"，得到一眼能读出
            /// "一整座岛"的轮廓（用户裁决 1/4：禁止矩形瓦片拼图）。
            ///
            /// 【算法】把矩形归一化成椭圆半径 <c>r = √((dx/rx)² + (dz/rz)²)</c>，
            /// 角向取 11 个桶的确定性噪声（平滑插值）<c>n(θ) ∈ [0,1)</c>，
            /// 边界限值 <c>limit = 1 − Erosion·(1−n) + Bulge·n</c>：
            ///   · <c>n→0</c> 的方位被**侵蚀**最多（内凹 ≤ <see cref="IslandErosion"/> 倍半径）；
            ///   · <c>n→1</c> 的方位**外凸**最多（≤ <see cref="IslandBulge"/> 倍半径，故外扩 1 格取格）。
            /// 半径 ≤ <see cref="IslandCoreRadius"/> 的**岛心保底**不被侵蚀（不会中间穿洞）。
            ///
            /// 【三条安全阀】① 宽/深 &lt; <see cref="IslandMaskMinSpan"/> 或面积 &lt;
            /// <see cref="IslandMaskMinArea"/> → 直接用实心矩形（踏脚石/小礁）；
            /// ② 侵蚀后矩形内保留率 &lt; <see cref="IslandMinKeepRatio"/> → 退回实心矩形（防碎渣）；
            /// ③ <paramref name="protectX0"/>.. 给出的**受保护内核**（出生矩形）永不被侵蚀，
            /// 保证"出生位绝不落水"这条不变量在岛形化之后仍然结构性成立。
            ///
            /// 【确定性】只用 <c>(seed, 桶号)</c> 的整数哈希，与遍历顺序无关 → 同 seed 同形状。
            /// </summary>
            public void StampIsland(int clusterIndex, int x0, int z0, int x1, int z1, int blocks, int seed)
            {
                StampIslandMasked(clusterIndex, x0, z0, x1, z1, 1, 1, -1, -1, blocks, seed);
            }

            /// <summary>
            /// 带**受保护内核**的岛形 stamp：<paramref name="protectX0"/>..<paramref name="protectX1"/>
            /// 为受保护矩形（传 <c>protectX0 &gt; protectX1</c> 表示无保护）。
            /// </summary>
            public void StampIslandMasked(int clusterIndex, int x0, int z0, int x1, int z1,
                int protectX0, int protectZ0, int protectX1, int protectZ1, int blocks, int seed)
            {
                if (blocks <= 0 || clusterIndex < 0)
                    return;

                if (x0 > x1) { int t = x0; x0 = x1; x1 = t; }
                if (z0 > z1) { int t = z0; z0 = z1; z1 = t; }
                x0 = Mathf.Max(0, x0); z0 = Mathf.Max(0, z0);
                x1 = Mathf.Min(W - 1, x1); z1 = Mathf.Min(D - 1, z1);

                int w = x1 - x0 + 1, d = z1 - z0 + 1;
                if (w < IslandMaskMinSpan || d < IslandMaskMinSpan || w * d < IslandMaskMinArea)
                {
                    Stamp(clusterIndex, x0, z0, x1, z1, blocks);
                    StampProtectedHoles(clusterIndex, x0, z0, x1, z1, blocks,
                        protectX0, protectZ0, protectX1, protectZ1);
                    return;
                }

                // 外扩 1 格容纳"外凸"；掩码半径仍按原矩形归一化，故外圈只有噪声高的方位被取到。
                int gx0 = Mathf.Max(0, x0 - 1), gx1 = Mathf.Min(W - 1, x1 + 1);
                int gz0 = Mathf.Max(0, z0 - 1), gz1 = Mathf.Min(D - 1, z1 + 1);

                float cx = x0 + w * 0.5f, cz = z0 + d * 0.5f;
                float rx = Mathf.Max(0.75f, w * 0.5f), rz = Mathf.Max(0.75f, d * 0.5f);

                int gw = gx1 - gx0 + 1, gd = gz1 - gz0 + 1;
                var keep = new bool[gw * gd];
                int keptInRect = 0;

                for (int gz = gz0; gz <= gz1; gz++)
                {
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        bool protectedCell = gx >= protectX0 && gx <= protectX1
                            && gz >= protectZ0 && gz <= protectZ1;

                        float nx = (gx + 0.5f - cx) / rx;
                        float nz = (gz + 0.5f - cz) / rz;
                        float r = Mathf.Sqrt(nx * nx + nz * nz);

                        float noise = AngularNoise01(Mathf.Atan2(nz, nx), seed);
                        float limit = 1f - IslandErosion * (1f - noise) + IslandBulge * noise;

                        bool inside = protectedCell || r <= limit || r <= IslandCoreRadius;
                        if (!inside)
                            continue;

                        keep[(gx - gx0) + (gz - gz0) * gw] = true;
                        if (gx >= x0 && gx <= x1 && gz >= z0 && gz <= z1)
                            keptInRect++;
                    }
                }

                // 安全阀 ②：蚀过头（保留率过低 = 碎渣/成洞）→ 退回实心矩形。
                // 【有受保护内核时不适用】内核保证该簇永远是一块完整的可站平台（不可能是碎片），
                // 此时保留率低只说明"外壳被蚀得很不规则"——那正是我们要的岛形，不该退回方砖。
                bool hasProtectedCore = protectX0 <= protectX1 && protectZ0 <= protectZ1;
                if (!hasProtectedCore && keptInRect < (int)(w * d * IslandMinKeepRatio))
                {
                    Stamp(clusterIndex, x0, z0, x1, z1, blocks);
                    StampProtectedHoles(clusterIndex, x0, z0, x1, z1, blocks,
                        protectX0, protectZ0, protectX1, protectZ1);
                    return;
                }

                for (int gz = gz0; gz <= gz1; gz++)
                {
                    int row = gz * W;
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        if (!keep[(gx - gx0) + (gz - gz0) * gw])
                            continue;

                        int i = row + gx;
                        _owner[i] = clusterIndex;
                        _blocks[i] = blocks;
                    }
                }
            }

            /// <summary>
            /// 受保护内核里"被侵蚀掉"的格补回（安全阀 ③ 的补写步骤）：
            /// 内核格若未被任何簇占用，就按 <paramref name="blocks"/> 补成该簇的地面。
            /// 只补**未被占用**的格，绝不覆盖别的簇（避免吞掉邻岛）。
            /// </summary>
            void StampProtectedHoles(int clusterIndex, int x0, int z0, int x1, int z1, int blocks,
                int protectX0, int protectZ0, int protectX1, int protectZ1)
            {
                if (protectX0 > protectX1 || protectZ0 > protectZ1)
                    return;

                int px0 = Mathf.Max(0, Mathf.Max(x0, protectX0));
                int pz0 = Mathf.Max(0, Mathf.Max(z0, protectZ0));
                int px1 = Mathf.Min(W - 1, Mathf.Min(x1, protectX1));
                int pz1 = Mathf.Min(D - 1, Mathf.Min(z1, protectZ1));

                for (int gz = pz0; gz <= pz1; gz++)
                {
                    int row = gz * W;
                    for (int gx = px0; gx <= px1; gx++)
                    {
                        int i = row + gx;
                        if (_owner[i] >= 0)
                            continue;

                        _owner[i] = clusterIndex;
                        _blocks[i] = blocks;
                    }
                }
            }

            /// <summary>角向噪声（11 桶 + 平滑插值），确定性；返回 [0,1)。</summary>
            static float AngularNoise01(float angle, int seed)
            {
                const int Buckets = 11;
                float t = (angle + Mathf.PI) / (Mathf.PI * 2f);
                if (t < 0f) t += 1f;
                if (t >= 1f) t -= 1f;

                float f = t * Buckets;
                int i0 = (int)f;
                if (i0 < 0) i0 = 0;
                if (i0 > Buckets - 1) i0 = Buckets - 1;

                float frac = f - i0;
                float n0 = Hash01(seed, i0, 991);
                float n1 = Hash01(seed, (i0 + 1) % Buckets, 991);
                float sm = frac * frac * (3f - 2f * frac);   // smoothstep
                return Mathf.Lerp(n0, n1, sm);
            }

            /// <summary>该矩形内是否已有归属（用于过桥时避免重叠）。</summary>
            public bool AnyOwned(RectI r)
            {
                if (r.Width <= 0 || r.Depth <= 0)
                    return false;
                RectI c = ClampRect(r, W, D);
                for (int gz = c.Z0; gz <= c.Z1; gz++)
                {
                    int row = gz * W;
                    for (int gx = c.X0; gx <= c.X1; gx++)
                    {
                        if (_owner[row + gx] >= 0)
                            return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// <paramref name="outer"/> 内、<paramref name="inner"/> 之外是否已有归属。
            /// 用于"出生岛要不要向外长一格做岛形"：外圈被别人占了就老实退回实心矩形，
            /// 免得把邻岛啃成碎片（碎片的包围盒只剩几格，会污染岛形统计与实际观感）。
            /// </summary>
            public bool AnyOwnedOutside(RectI outer, RectI inner)
            {
                RectI o = ClampRect(outer, W, D);
                for (int gz = o.Z0; gz <= o.Z1; gz++)
                {
                    int row = gz * W;
                    for (int gx = o.X0; gx <= o.X1; gx++)
                    {
                        if (gx >= inner.X0 && gx <= inner.X1 && gz >= inner.Z0 && gz <= inner.Z1)
                            continue;
                        if (_owner[row + gx] >= 0)
                            return true;
                    }
                }
                return false;
            }

            /// <summary>各簇当前的逐格归属统计（过桥时读包络用）。</summary>
            public List<PlatformClusterInfo> PeekClusterBounds()
            {
                return ComputeBounds(out _);
            }

            List<PlatformClusterInfo> ComputeBounds(out int[] draftToInfo)
            {
                int n = _drafts.Count;
                var count = new int[n];
                var minB = new int[n];
                var maxB = new int[n];
                var bx0 = new int[n]; var bz0 = new int[n];
                var bx1 = new int[n]; var bz1 = new int[n];

                for (int i = 0; i < n; i++)
                {
                    minB[i] = int.MaxValue; maxB[i] = int.MinValue;
                    bx0[i] = int.MaxValue; bz0[i] = int.MaxValue;
                    bx1[i] = int.MinValue; bz1[i] = int.MinValue;
                }

                for (int gz = 0; gz < D; gz++)
                {
                    int row = gz * W;
                    for (int gx = 0; gx < W; gx++)
                    {
                        int i = row + gx;
                        int o = _owner[i];
                        if (o < 0)
                            continue;

                        count[o]++;
                        int b = _blocks[i];
                        if (b < minB[o]) minB[o] = b;
                        if (b > maxB[o]) maxB[o] = b;
                        if (gx < bx0[o]) bx0[o] = gx;
                        if (gx > bx1[o]) bx1[o] = gx;
                        if (gz < bz0[o]) bz0[o] = gz;
                        if (gz > bz1[o]) bz1[o] = gz;
                    }
                }

                // infos 按 drafts 顺序追加；被完全覆盖（零格）的草稿被丢弃，
                // draftToInfo 记录"草稿下标 → infos 下标"（丢弃为 -1），供 Finalize 重映射。
                draftToInfo = new int[n];
                var infos = new List<PlatformClusterInfo>(n);
                for (int i = 0; i < n; i++)
                {
                    if (count[i] <= 0)
                    {
                        draftToInfo[i] = -1;
                        continue;
                    }

                    draftToInfo[i] = infos.Count;
                    infos.Add(new PlatformClusterInfo(_drafts[i].Name, _drafts[i].Kind,
                        bx0[i], bz0[i], bx1[i], bz1[i], minB[i], maxB[i], _drafts[i].SpawnTeamMask));
                }
                return infos;
            }

            public PlatformMap Finalize()
            {
                List<PlatformClusterInfo> infos = ComputeBounds(out int[] remap);

                var cellCluster = new int[W * D];
                for (int i = 0; i < cellCluster.Length; i++)
                {
                    int o = _owner[i];
                    cellCluster[i] = o < 0 ? -1 : remap[o];
                }

                return new PlatformMap(W, D, _blocks, cellCluster, infos);
            }
        }

        // ------------------------------------------------------------------
        // 簇几何查询（测试 / AI / 报告）
        // ------------------------------------------------------------------

        /// <summary>
        /// 角色最大投掷射程（世界单位）的解析估计：由满力 twangMax、固定抬升与重力算出平抛射程。
        /// <c>R = v_h · (2·v_v / g)</c>，其中 <c>v_h = v/√(1+lift²)</c>、<c>v_v = v_h·lift</c>。
        /// 用角色默认 weight=1、twangMax=20（<see cref="CrewCatalog"/>），得约 11.7 单位。
        /// 【AI 提案】仅用于"簇连通性"判定（水距远小于它即视为投掷可跨），不参与弹道计算。
        /// </summary>
        public static float MaxThrowRangeWorld
        {
            get
            {
                float speed = CrewCatalog.TwangMaxForce * LevelGeometry.FlashSpeedScale;
                float lift = LevelGeometry.ThrowLift;
                float horiz = speed / Mathf.Sqrt(1f + lift * lift);
                float vert = horiz * lift;
                float g = -LevelGeometry.WorldGravityY(CrewCatalog.Weight);
                if (g <= 1e-6f)
                    return 0f;
                return horiz * (2f * vert / g);
            }
        }

        /// <summary>
        /// 两簇**最近可站格**之间的世界距离（格宽 1）：沿 X/Z 各自算边到边的距离再取欧氏合成。
        /// 用于"投掷可跨"的连通性判定（比单纯水格数更贴近真实投掷距离）。
        /// </summary>
        public static float MinEdgeDistanceWorld(in PlatformClusterInfo a, in PlatformClusterInfo b)
        {
            int gapX = Mathf.Max(a.X0 - b.X1 - 1, b.X0 - a.X1 - 1);
            int gapZ = Mathf.Max(a.Z0 - b.Z1 - 1, b.Z0 - a.Z1 - 1);

            float dx = gapX >= 0 ? gapX + 1f : 0f;
            float dz = gapZ >= 0 ? gapZ + 1f : 0f;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 两簇之间的**水格数**（沿分离轴）。包络在 X/Z 上都重叠时返回 0（视为相接）。
        /// 用于测试"水距 ≥2 格"与"投掷可跨"。
        /// </summary>
        public static int WaterGapTiles(in PlatformClusterInfo a, in PlatformClusterInfo b)
        {
            int gapX = Mathf.Max(a.X0 - b.X1 - 1, b.X0 - a.X1 - 1);
            int gapZ = Mathf.Max(a.Z0 - b.Z1 - 1, b.Z0 - a.Z1 - 1);

            // 取"分离最明显"的轴：若某轴 gap>=0 则该轴已分离；否则重叠（gap<0）。
            int gap = Mathf.Max(gapX, gapZ);
            return gap < 0 ? 0 : gap;
        }
    }
}
