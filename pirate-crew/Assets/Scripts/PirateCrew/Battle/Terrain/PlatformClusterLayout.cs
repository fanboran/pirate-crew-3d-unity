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

        /// <summary>簇内最低/最高平台高度（块）。</summary>
        public readonly int MinBlocks, MaxBlocks;

        /// <summary>
        /// 出生队列掩码：<c>bit0 = 红队(team0)</c>、<c>bit1 = 蓝队(team1)</c>、<c>0 = 中立</c>。
        /// 供 kit 配方区分「出生台地」与「中立场景簇」（见 <c>SceneArt/SceneKitCatalog.cs</c> 的 BuildFor）。
        /// </summary>
        public readonly int SpawnTeamMask;

        public PlatformClusterInfo(string name, PlatformClusterKind kind, int x0, int z0, int x1, int z1,
            int minBlocks, int maxBlocks, int spawnTeamMask = 0)
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
        }

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

        /// <summary>逐格块高（行主序）；0 = 水。</summary>
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

        /// <summary>该格的块高（水 / 越界 = 0）。</summary>
        public int BlocksAt(int gx, int gz)
        {
            if (gx < 0 || gz < 0 || gx >= WidthTiles || gz >= DepthTiles)
                return 0;
            return CellBlocks[IndexOf(gx, gz)];
        }

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
    /// 【两种入口】
    /// ==================================================================
    ///   · <see cref="BuildFor(LevelData)">BuildFor(level)</see> —— 通用推导：由关卡数据的
    ///     实际字段（尺寸 / 双方出生位 / 空投武器池）算出"一堆高高低低的悬空平台"，
    ///     **不是每关手写**。level_1 保留手写定义（见下），其余关一律走通用推导。
    ///   · <see cref="BuildLevel1()">BuildLevel1()</see> —— <c>BuildFor(level_1)</c> 的等价委托，
    ///     供既有测试 / 烘焙沿用（旧行为逐值不变）。
    ///
    /// ==================================================================
    /// 【通用推导规则表（每条的出处 = docs/关卡设计语言-参照游戏全场景分析.md §4 的 R 编号）】
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
    ///
    /// 【未建模（诚实声明）】R4（"主簇最底行贴水线"）依赖原版 2D 的行序语义，本项目把行序重投影为
    /// 世界 Z（见 <see cref="TileTerrainGrid"/> 类头），故不按"贴水线"摆放；R11（台阶进深 ≥2 行）
    /// 只在主簇台阶上近似满足（内缩 1 格 = 每边进深 1 格，比原版略窄）。
    ///
    /// ==================================================================
    /// 【level_1 手写定义（保留为 BuildFor(level_1) 的等价分支）】
    /// ==================================================================
    /// 【提案/待定】level_1 布局是 AI 按用户诉求（"一堆高高低低的悬空平台浮在海面上，平台间是水，
    /// 场景美术把平台伪装成大船 / 空岛 / 梯田小岛"）给出的，**未经用户确认**：
    ///   · 簇数量 4（3 主簇 + 1 小空岛）落在用户建议的 3-4 簇区间内；
    ///   · 水距 2-4 格，远小于角色最大投掷射程（约 11.7 单位，见 <see cref="MaxThrowRangeWorld"/>），
    ///     故任一簇都能被投掷跨越（不会出现"打不到的孤岛"）；
    ///   · 所有 8 个出生位（5 红 3 蓝）都落在各自簇的地面格上，高度 ≥1 块，绝不初始落水；
    ///   · ②↔③ 水距 4 格（③ 西界 x43）、①↔② 4 格、②↔④ 2 格（④ 在 ② 正北 x32-35 z0-1），
    ///     地面 411 / 850 格 = 48.4%（校正值见 §5.3；据此重算的断言在 PlatformTerrainTests）。
    ///
    /// 【与旧列的差别】旧 <see cref="TerrainCatalog"/> 的 level_1 是"整块地面 + 中脊"（0 块 = 基础地面，
    /// 永不挖洞）；本布局是**逐格水陆**，水格没有地面，单位走上去会掉到水面以下（落水即死）。
    /// </summary>
    public static class PlatformClusterLayout
    {
        /// <summary>单簇最高块数（平台竖直压缩上限；<see cref="TileTerrainGrid"/> 的块高口径）。</summary>
        public const int MaxBlocksPerCluster = 5;

        /// <summary>常规关允许的最大簇间水距（格）——R3「常规关 1–5 格」。</summary>
        public const int RegularMaxWaterGap = 5;

        /// <summary>配了越水武器时允许的最大簇间水距（格）——R3 例外「唯一允许 8–10 格」。</summary>
        public const int CrossingWeaponMaxWaterGap = 10;

        // ------------------------------------------------------------------
        // level_1 手写定义（50×17）。后列出的 Step 覆盖先列出的（用于叠台阶）。
        // ------------------------------------------------------------------

        sealed class Step
        {
            public readonly int X0, Z0, X1, Z1, Blocks;

            public Step(int x0, int z0, int x1, int z1, int blocks)
            {
                X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Blocks = blocks;
            }
        }

        sealed class ClusterDef
        {
            public readonly string Name;
            public readonly PlatformClusterKind Kind;
            public readonly int SpawnTeamMask;
            public readonly Step[] Steps;

            public ClusterDef(string name, PlatformClusterKind kind, Step[] steps, int spawnTeamMask = 0)
            {
                Name = name; Kind = kind; Steps = steps; SpawnTeamMask = spawnTeamMask;
            }
        }

        static readonly ClusterDef[] Level1Defs =
        {
            // ① 梯田小岛簇（红队出生区，西侧）：三级台阶递升，顶层放高台掩体。
            new ClusterDef("terrace_island_west", PlatformClusterKind.TerraceIsland, new[]
            {
                new Step( 4,  3, 19, 13, 1),   // 外环台阶
                new Step( 6,  5, 17, 11, 2),   // 中环台阶
                new Step( 9,  6, 13, 10, 3),   // 顶层
            }, spawnTeamMask: 1 << 0),

            // ② 大船簇（战场主簇 / 中立，中央）：外围浅礁裙 + 长条甲板 + 船头高台 + 桅盘。
            // z 包络取 4-13（比第一版 5-12 各外扩 1 行），使 ④ 北岛落到"水距 2 格"、
            // 且与 ③ 的主水道稳定为 4 格；依据 docs/关卡设计语言-参照游戏全场景分析.md §5.3/§5.4。
            new ClusterDef("great_ship_center", PlatformClusterKind.Ship, new[]
            {
                new Step(24,  4, 38, 13, 1),   // 外围浅礁裙（1 块；只决定簇包络，甲板叠在其上）
                new Step(24,  5, 38, 12, 2),   // 主甲板（长条形，高 2 块起）
                new Step(34,  5, 38, 12, 4),   // 船头高台
                new Step(28,  7, 31, 10, 5),   // 桅盘（掩体高台）
            }),

            // ③ 空岛簇（蓝队出生区，东侧）：基底 + 岩峰，高差 2 块。
            // 西界取 x43（第一版 x42 → x43）：②↔③ 水距由 3 格校正为 4 格（§5.4）。
            new ClusterDef("sky_island_east", PlatformClusterKind.SkyIsland, new[]
            {
                new Step(43,  2, 49, 12, 2),   // 岛基
                new Step(45,  4, 48, 10, 4),   // 岩峰
            }, spawnTeamMask: 1 << 1),

            // ④ 小空岛（中立跳板，中央簇正北）：第一版在 x36-39（压在 ②/③ 之间、会切主水道），
            // 现移到 ② 正北 x32-35 z0-1 —— 与 ② 水距 2 格，且不切断 ②↔③ 的 4 格主水道（§5.4）。
            new ClusterDef("sky_islet_north", PlatformClusterKind.SkyIsland, new[]
            {
                new Step(32,  0, 35,  1, 3),
            }),
        };

        /// <summary>level_1 的平台簇描述（供测试/报告直接读）。</summary>
        public static IReadOnlyList<PlatformClusterInfo> Level1Clusters => BuildLevel1().Clusters;

        /// <summary>
        /// level_1 的平台簇地图 = <see cref="BuildFor(LevelData)">BuildFor(level_1)</see> 的等价委托
        /// （既有测试与烘焙沿用它，行为逐值不变）。
        /// </summary>
        public static PlatformMap BuildLevel1()
        {
            return BuildFor(LevelCatalog.Get(1));
        }

        /// <summary>
        /// 关卡数据的**通用**平台簇推导（确定性：同一 LevelData 必得同一地图）。
        /// level_1 走手写定义（<see cref="Level1Defs"/>），其余关一律由出生位 + 尺寸 + 武器池推导。
        /// 尺寸非法（&lt;=0）时返回 <c>null</c>（调用方退回旧列式地形 / 平坦竞技场）。
        /// </summary>
        public static PlatformMap BuildFor(LevelData level)
        {
            if (level.LevelNumber == 1)
                return BuildFromDefs(Level1Defs, 50, 17);

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

        // ==================================================================
        // level_1 手写定义的展开
        // ==================================================================

        static PlatformMap BuildFromDefs(ClusterDef[] defs, int w, int d)
        {
            var blocks = new int[w * d];
            var cellCluster = new int[w * d];
            for (int i = 0; i < cellCluster.Length; i++)
                cellCluster[i] = -1;

            var infos = new List<PlatformClusterInfo>(defs.Length);

            for (int c = 0; c < defs.Length; c++)
            {
                ClusterDef def = defs[c];
                int minB = int.MaxValue, maxB = int.MinValue;
                int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;

                for (int s = 0; s < def.Steps.Length; s++)
                {
                    Step step = def.Steps[s];
                    for (int gz = step.Z0; gz <= step.Z1; gz++)
                    {
                        for (int gx = step.X0; gx <= step.X1; gx++)
                        {
                            if (gx < 0 || gz < 0 || gx >= w || gz >= d)
                                continue;

                            int idx = gx + gz * w;
                            blocks[idx] = step.Blocks;
                            cellCluster[idx] = c;

                            minB = Mathf.Min(minB, step.Blocks);
                            maxB = Mathf.Max(maxB, step.Blocks);
                            x0 = Mathf.Min(x0, gx); x1 = Mathf.Max(x1, gx);
                            z0 = Mathf.Min(z0, gz); z1 = Mathf.Max(z1, gz);
                        }
                    }
                }

                if (minB == int.MaxValue)
                    minB = 0;

                infos.Add(new PlatformClusterInfo(def.Name, def.Kind, x0, z0, x1, z1, minB, maxB,
                    def.SpawnTeamMask));
            }

            return new PlatformMap(w, d, blocks, cellCluster, infos);
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

            return writer.Finalize();
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
                int width = Mathf.Clamp(gap - 4, 3, 10);
                int depth = Mathf.Clamp(Mathf.Min(red.Depth, blue.Depth), 4, writer.D - 2);
                int cx = (red.X1 + blue.X0) / 2;
                int cz = (red.CenterZ + blue.CenterZ) / 2;

                RectI r = ClampRect(new RectI(cx - width / 2, cz - depth / 2,
                    cx - width / 2 + width - 1, cz - depth / 2 + depth - 1), writer.W, writer.D);
                if (r.Width >= 3 && r.Depth >= 3)
                {
                    int platform = writer.NewCluster("main_center", MainKindFor(chapter), 0);
                    writer.Stamp(platform, r.X0, r.Z0, r.X1, r.Z1, 1);

                    if (!teaching)
                    {
                        RectI inner = Shrink(r, 1);
                        if (inner.Width >= 2 && inner.Depth >= 2)
                            writer.Stamp(platform, inner.X0, inner.Z0, inner.X1, inner.Z1, 2);
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
            int width = Mathf.Clamp(GapX(red, blue) - 4, 3, 8);
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

            const int ring = 8;
            int placed = 0;
            for (int i = 0; i < ring; i++)
            {
                float ang = i / (float)ring * Mathf.PI * 2f + Hash01(level.LevelNumber, i, 5) * 0.25f;
                int ix = cx + Mathf.RoundToInt(Mathf.Cos(ang) * radiusX);
                int iz = cz + Mathf.RoundToInt(Mathf.Sin(ang) * radiusZ);

                int idx = writer.NewCluster("ring_islet_" + i, IsletKindFor(chapter), 0);
                RectI r = ClampRect(new RectI(ix - 1, iz - 1, ix + 1, iz + 1), writer.W, writer.D);
                if (r.Width <= 0 || r.Depth <= 0)
                    continue;

                writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, 2);
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
                int cx = 1 + (int)(Hash01(level.LevelNumber, i, 11) * Mathf.Max(1, writer.W - 4));
                int cz = 1 + (int)(Hash01(level.LevelNumber, i, 23) * Mathf.Max(1, writer.D - 4));
                int width = 2 + (Hash01(level.LevelNumber, i, 31) < 0.5f ? 0 : 1);
                int depth = 2;

                int idx = writer.NewCluster("scatter_islet_" + i, IsletKindFor(chapter), 0);
                RectI r = ClampRect(new RectI(cx, cz, cx + width - 1, cz + depth - 1), writer.W, writer.D);
                if (r.Width <= 0 || r.Depth <= 0)
                    continue;

                int blocks = Hash01(level.LevelNumber, i, 41) < 0.5f ? 1 : 2;
                writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, blocks);
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

            writer.Stamp(peak, r.X0, r.Z0, r.X1, r.Z1, 1);

            RectI step2 = Shrink(r, 1);
            if (step2.Width >= 3 && step2.Depth >= 3)
                writer.Stamp(peak, step2.X0, step2.Z0, step2.X1, step2.Z1, 2);

            RectI step3 = Shrink(step2, 1);
            if (!teaching && step3.Width >= 2 && step3.Depth >= 2)
                writer.Stamp(peak, step3.X0, step3.Z0, step3.X1, step3.Z1, 3);

            // 峰顶（≥9 层的原版"垂直大关"在本项目压缩为 5 块）——记忆点（R13）。
            RectI summit = Shrink(step3, 1);
            if (!teaching && summit.Width >= 2 && summit.Depth >= 2)
                writer.Stamp(peak, summit.X0, summit.Z0, summit.X1, summit.Z1, MaxBlocksPerCluster);

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

        /// <summary>出生平台：整块 1 块高（R2 保证每点 ≥4 格）；非教学关内缩一级抬高到 2 块（出生台地）。</summary>
        static void StampBirth(MapWriter writer, int idx, RectI r, bool teaching, bool shipLookout)
        {
            writer.Stamp(idx, r.X0, r.Z0, r.X1, r.Z1, 1);
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
