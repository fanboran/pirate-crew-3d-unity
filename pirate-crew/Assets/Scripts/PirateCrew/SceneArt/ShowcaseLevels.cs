using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>烘焙陈设件种类（= SceneArtBaker 的产物；一个种类一个 prefab 资产）。</summary>
    public enum ShowcasePieceId
    {
        /// <summary>落水危险虚线（样板三关共用一圈）。</summary>
        DangerBorder = 0,

        /// <summary>低模云场（第 1 关「云端漫步」主景）。</summary>
        CloudField = 1,

        /// <summary>碎岛礁群（第 2 关「碎岛雨」主景：岛壳烘焙，与逻辑高度场按构造对齐）。</summary>
        Islets = 2,
    }

    /// <summary>一件烘焙陈设的摆位（纯数据）。prefab 原点 = 几何烘焙原点（云场=构图中心，碎岛=竞技场原点）。</summary>
    public readonly struct ShowcasePiecePlacement
    {
        public readonly ShowcasePieceId Piece;

        /// <summary>实例世界位置。</summary>
        public readonly Vector3 Position;

        /// <summary>绕 Y 朝向（度）。</summary>
        public readonly float YawDegrees;

        /// <summary>实例名（层级可读性，非逻辑键）。</summary>
        public readonly string InstanceName;

        public ShowcasePiecePlacement(ShowcasePieceId piece, Vector3 position, float yawDegrees, string instanceName)
        {
            Piece = piece;
            Position = position;
            YawDegrees = yawDegrees;
            InstanceName = instanceName;
        }
    }

    /// <summary>
    /// 样板三关的**纯数据层**（关卡制作管线阶段 3 的落点，设计文档见 docs/设计/关卡/）：
    ///   L1 云端漫步（教学：投掷手感 / 回合流转 / 小心坠落）
    ///   L2 碎岛雨（进阶：落水威胁 / 跨岛精度 / 阵地武器）——2026-09-19 替换退役的「双雄并舷」
    ///   L3 天空之岛（考核：以少打多 4v5 / 越水控场武器）
    /// 编成 / luck / 武器池的数值依据逐条引用在各设计文档（AI 提案，待用户终审）。
    ///
    /// 【架构口径】格子在此**降级为不可见的逻辑高度场**，只承担两件玩家看不见的事：
    ///   1) 单位站位高度——<c>BattleController.SpawnTeams</c> 用 <c>TileTerrainGrid.SurfaceWorldY</c>；
    ///   2) AI 落点评估——<c>AiTerrain</c> 按格判实心/落水。
    /// 渲染层由烘焙 prefab 按摆位表实例化（RuntimeSceneArt）；L2 的碎岛壳直接从本类的高度场
    /// 烘出（IslandShellGeometry.BuildSolidShell），逻辑-视觉按构造对齐。
    ///
    /// 【尺度】1 格 = 2 单位（LevelGeometry.TileWorldSize）；块高 0.5 单位
    /// （LevelGeometry.BlockWorldHeight = PixelsToUnits(8)）；水面 y=-0.4。
    /// 关卡场地统一 20×15 格（40×30 单位）。
    /// </summary>
    public static class ShowcaseLevels
    {
        public const int FirstLevel = 1;
        public const int LastLevel = 3;
        public const int WidthTiles = 20;
        public const int DepthTiles = 15;

        public static bool IsShowcase(int levelNumber) =>
            levelNumber >= FirstLevel && levelNumber <= LastLevel;

        // ------------------------------------------------------------------
        // 关卡数据（单位布阵 / 武器 / luck）——数值出处见 docs/设计/关卡/L0N-*.md §4-§5
        // ------------------------------------------------------------------

        /// <summary>样板关的出战数据；非样板关返回 null（调用方回落世界图）。</summary>
        public static LevelData? BuildLevelData(int levelNumber)
        {
            switch (levelNumber)
            {
                case 1: return CloudWalk();
                case 2: return IsletRain();
                case 3: return SkyIsland();
                default: return null;
            }
        }

        /// <summary>L1 云端漫步：教学关，4v3，蓝方 luck 1，全员 cherryBomb，空投 Dynamite。</summary>
        static LevelData CloudWalk()
        {
            var units = new List<LevelUnit>
            {
                // 红队（玩家，luck 5=默认）：主力在主角云，船长占北云制高点
                new LevelUnit("redPirate",        0, 8,  6,  5, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("redPirate",        0, 12, 9,  5, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("redPirate",        0, 10, 4,  5, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("redPirateCaptain", 0, 10, 11, 5, W(WeaponId.CherryBomb, 10)),
                // 蓝队（luck 1=最笨档）：两人在主角云、船长在东云
                new LevelUnit("cabinBoy",         1, 12, 6,  1, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("cabinBoy",         1, 8,  9,  1, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("cabinBoyCaptain",  1, 13, 7,  1, W(WeaponId.CherryBomb, 10)),
            };
            return NewLevel(1, "云端漫步", units, WeaponId.Dynamite);
        }

        /// <summary>L2 碎岛雨：进阶关，4v4，蓝方 luck 2，cannonball+船长 gunpowderBarrel，空投 Mine/RumBottle/Banana。</summary>
        static LevelData IsletRain()
        {
            var units = new List<LevelUnit>
            {
                // 红队：西北主岛（岛 2-6 × 2-5）
                new LevelUnit("redPirate",        0, 3,  3,  5, W(WeaponId.Cannonball, 10)),
                new LevelUnit("redPirate",        0, 6,  3,  5, W(WeaponId.Cannonball, 10)),
                new LevelUnit("redPirate",        0, 3,  5,  5, W(WeaponId.Cannonball, 10)),
                new LevelUnit("redPirateCaptain", 0, 6,  5,  5, W(WeaponId.Cannonball, 10, WeaponId.GunpowderBarrel, 2)),
                // 蓝队（luck 2）：东南主岛（岛 13-17 × 9-12），与红队中心对称
                new LevelUnit("cabinBoy",         1, 16, 11, 2, W(WeaponId.Cannonball, 10)),
                new LevelUnit("cabinBoy",         1, 13, 11, 2, W(WeaponId.Cannonball, 10)),
                new LevelUnit("cabinBoy",         1, 16, 9,  2, W(WeaponId.Cannonball, 10)),
                new LevelUnit("cabinBoyCaptain",  1, 13, 9,  2, W(WeaponId.Cannonball, 10, WeaponId.GunpowderBarrel, 2)),
            };
            return NewLevel(2, "碎岛雨", units, WeaponId.Mine, WeaponId.RumBottle, WeaponId.Banana);
        }

        /// <summary>L3 天空之岛：考核关，红 4 vs 蓝 5（以少打多），全员 luck 5，空投越水控场组。</summary>
        static LevelData SkyIsland()
        {
            var units = new List<LevelUnit>
            {
                // 红队（玩家）：大岛东半（列 11-13，背靠东岛缘）
                new LevelUnit("redPirate",        0, 11, 5, 5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("redPirate",        0, 13, 6, 5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("redPirate",        0, 11, 8, 5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("redPirateCaptain", 0, 12, 6, 5, W(WeaponId.Cannonball, 10, WeaponId.VoodooDoll, 2)),
                // 蓝队（luck 5 拉满 + 人数 +1）：大岛西半（列 5-8），东西强镜像
                new LevelUnit("cabinBoy",         1, 6, 5, 5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("cabinBoy",         1, 8, 5, 5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("cabinBoy",         1, 5, 7, 5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("cabinBoy",         1, 8, 8, 5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("cabinBoyCaptain",  1, 6, 8, 5, W(WeaponId.Cannonball, 10, WeaponId.Mine, 2)),
            };
            return NewLevel(3, "天空之岛", units, WeaponId.TidalWave, WeaponId.Anchor, WeaponId.Seagull);
        }

        static LevelData NewLevel(int number, string name, List<LevelUnit> units, params WeaponId[] drops)
        {
            var potential = new List<WeaponStack>();
            for (int i = 0; i < drops.Length; i++)
                potential.Add(new WeaponStack(drops[i], 10));
            return new LevelData(number, name, WidthTiles, DepthTiles,
                originalXmlPlayers: 1, waterTileY: 14f,
                maxChests: 3, sourceXmlMaxChests: 3, potential, units);
        }

        static List<WeaponStack> W(WeaponId id, int count)
        {
            var list = new List<WeaponStack> { new WeaponStack(id, count) };
            return list;
        }

        static List<WeaponStack> W(WeaponId id, int count, WeaponId id2, int count2)
        {
            var list = new List<WeaponStack> { new WeaponStack(id, count), new WeaponStack(id2, count2) };
            return list;
        }

        // ------------------------------------------------------------------
        // 逻辑高度场（隐形；决定站位 Y 与 AI 落点，不参与渲染）
        // 布局真源 = docs/设计/关卡/L0N-*.md 的 ASCII 布局块（改布局先改文档再改这里）。
        // ------------------------------------------------------------------

        /// <summary>样板关的逻辑地形；调用法与 TerrainCatalog.Build 同构。</summary>
        public static TileTerrainGrid BuildLogicGrid(int levelNumber)
        {
            int[] blocks = levelNumber == 1 ? CloudFieldBlocks()
                : levelNumber == 2 ? IsletRainBlocks()
                : HillBlocks();
            return new TileTerrainGrid(WidthTiles, DepthTiles, blocks, LevelGeometry.BlockWorldHeight);
        }

        /// <summary>目标世界高度 → 块数（块高 0.5 单位）。</summary>
        static int Blocks(float worldY) =>
            Mathf.Max(1, Mathf.RoundToInt(worldY / LevelGeometry.BlockWorldHeight));

        /// <summary>
        /// L1 云场：与 Lowpoly.CloudFieldGeometry 的真实布局对齐（云场平移到 (20,15) 后的格子）：
        /// 主角云 12×12（格 7-12 × 5-10）y4.5；第一环四朵 y 7.5/2.5/6.5/2.5（避开主角云行）。
        /// </summary>
        static int[] CloudFieldBlocks()
        {
            var b = new int[WidthTiles * DepthTiles];
            Fill(b, 7, 12, 5, 10, Blocks(4.5f));   // 主角云
            Fill(b, 9, 11, 11, 11, Blocks(7.5f));  // 北云 Cloud1
            Fill(b, 6, 8, 7, 8, Blocks(2.5f));     // 西云 Cloud2（覆写主角云西缘两格 → 台阶读数）
            Fill(b, 9, 11, 4, 4, Blocks(6.5f));    // 南云 Cloud3
            Fill(b, 13, 14, 7, 8, Blocks(2.5f));   // 东云 Cloud4
            return b;
        }

        /// <summary>
        /// L2 碎岛雨：7 座低礁岛（全部 1 块 = 顶面 y0.5），中心对称——
        /// 双方主岛 5×4 于西北/东南，中枢 C1 居场心，C2/C3 侧翼、C4/C5 哨岛（布局见设计文档 ASCII 块）。
        /// </summary>
        static int[] IsletRainBlocks()
        {
            var b = new int[WidthTiles * DepthTiles];
            int low = Blocks(0.5f);
            Fill(b, 2, 6, 2, 5, low);      // 红方主岛（西北）
            Fill(b, 13, 17, 9, 12, low);   // 蓝方主岛（东南，与红岛 180° 对称）
            Fill(b, 9, 11, 6, 8, low);     // C1 中枢（场心十字路口）
            Fill(b, 5, 7, 8, 9, low);      // C2 西南侧翼
            Fill(b, 12, 14, 4, 5, low);    // C3 东北侧翼
            Fill(b, 9, 10, 2, 3, low);     // C4 北哨岛
            Fill(b, 9, 10, 11, 12, low);   // C5 南哨岛
            return b;
        }

        /// <summary>
        /// L3 大岛：岛面椭圆（中心格 (10, 7.5)，半径 X 14.5 / Z 12.6 单位 = 7.25 / 6.3 格），
        /// 只铺草皮平缓带（椭圆比 ≤ 0.8），高度 28 块 = y14，与空岛根 y13.3 + 草皮面 0.6-0.9 对齐。
        /// </summary>
        static int[] HillBlocks()
        {
            var b = new int[WidthTiles * DepthTiles];
            float rx = 7.25f * 0.8f, rz = 6.3f * 0.8f;
            for (int gy = 0; gy < DepthTiles; gy++)
            {
                for (int gx = 0; gx < WidthTiles; gx++)
                {
                    float dx = gx + 0.5f - 10f;
                    float dz = gy + 0.5f - 7.5f;
                    if ((dx * dx) / (rx * rx) + (dz * dz) / (rz * rz) <= 1f)
                        b[gy * WidthTiles + gx] = Blocks(14f);
                }
            }
            return b;
        }

        static void Fill(int[] blocks, int x0, int x1, int y0, int y1, int value)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    blocks[y * WidthTiles + x] = value;
        }

        // ------------------------------------------------------------------
        // 烘焙件摆位表（糖豆人式资产架构：数据层只记「件 id + 摆位」，几何已烘焙进 prefab）
        // ------------------------------------------------------------------

        /// <summary>
        /// 某样板关的烘焙件摆位表（<see cref="RuntimeSceneArt"/> 实例化消费）。
        /// 换这里的数 = 换摆位，不需要重新烘焙；碎岛壳例外——它与逻辑高度场按构造对齐，
        /// 改 L2 布局必须重跑 SceneArtBaker（IsletRainBlocks → Islets prefab）。
        /// </summary>
        public static List<ShowcasePiecePlacement> BakedPlacements(int levelNumber)
        {
            var list = new List<ShowcasePiecePlacement>
            {
                // 危险虚线绕 20×15 格竞技场一圈，几何按场景原点烘焙，实例恒在原点。
                new ShowcasePiecePlacement(ShowcasePieceId.DangerBorder, Vector3.zero, 0f, "DangerBorder"),
            };

            switch (levelNumber)
            {
                case 1:
                    // 云场平移到 20×15 格场心。
                    list.Add(new ShowcasePiecePlacement(ShowcasePieceId.CloudField,
                        new Vector3(LevelGeometry.TileToWorld(10f), 0f, LevelGeometry.TileToWorld(7.5f)),
                        0f, "CloudField"));
                    break;

                case 2:
                    // 碎岛礁群：几何从 L2 逻辑高度场烘出（世界坐标直出），实例恒在原点。
                    list.Add(new ShowcasePiecePlacement(ShowcasePieceId.Islets, Vector3.zero, 0f, "Islets_L02"));
                    break;
            }

            return list;
        }

    }
}
