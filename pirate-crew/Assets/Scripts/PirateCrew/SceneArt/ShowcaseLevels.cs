using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 样板三关（DEMO v0.1，用户 2026-09-14 裁决）：关卡号 1/2/3 覆盖为
    /// 「云朵场（低模，高低错落）」「双大帆船并列（结构画全）」「山包大岛 + 超美空岛」。
    ///
    /// 【架构口径】格子在此**降级为不可见的逻辑高度场**，只承担两件玩家看不见的事：
    ///   1) 单位站位高度——<c>BattleController.SpawnTeams</c> 用 <c>TileTerrainGrid.SurfaceWorldY</c>；
    ///   2) AI 落点评估——<c>AiTerrain</c> 按格判实心/落水。
    /// 渲染层完全不碰格子（RuntimeSceneArt 走自由几何装配，BattleTerrainView 只建碰撞层不建渲染层），
    /// 弹体/站立碰撞由 BattleTerrainView 的隐形 BoxCollider 碰撞层 + 各几何体自带 Collider 共同承担。
    ///
    /// 【尺度】1 格 = 2 单位（LevelGeometry.TileWorldSize）；块高 0.5 单位
    /// （TerrainCatalog.DefaultBlockWorldHeight = PixelsToUnits(8)）；水面 y=-0.2。
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
        // 关卡数据（单位布阵 / 武器）
        // ------------------------------------------------------------------

        /// <summary>样板关的出战数据；非样板关返回 null（调用方回落 LevelCatalog）。</summary>
        public static LevelData? BuildLevelData(int levelNumber)
        {
            switch (levelNumber)
            {
                case 1: return CloudField();
                case 2: return TwinShips();
                case 3: return HillAndSkyIsland();
                default: return null;
            }
        }

        static LevelData CloudField()
        {
            var units = new List<LevelUnit>
            {
                // 红队：主角云对角 2 点 + 北云/南云各 1（格子由 Lowpoly 出生点平移场心后换算，1 格 = 2 单位）
                new LevelUnit("redPirate",        0, 8,  6,  5, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("redPirate",        0, 12, 9,  5, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("redPirateCaptain", 0, 10, 11, 5, W(WeaponId.CherryBomb, 10, WeaponId.Banana, 6)),
                new LevelUnit("redPirate",        0, 10, 4,  5, W(WeaponId.CherryBomb, 10)),
                // 蓝队：主角云另一对角 + 西云/东云
                new LevelUnit("cabinBoy",         1, 12, 6,  2, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("cabinBoy",         1, 8,  9,  2, W(WeaponId.CherryBomb, 10)),
                new LevelUnit("cabinBoyCaptain",  1, 7,  7,  2, W(WeaponId.CherryBomb, 10, WeaponId.Banana, 6)),
                new LevelUnit("cabinBoy",         1, 13, 7,  2, W(WeaponId.CherryBomb, 10)),
            };
            return NewLevel(1, "云端漫步", units, WeaponId.Dynamite, WeaponId.ParachuteBomb);
        }

        static LevelData TwinShips()
        {
            var units = new List<LevelUnit>
            {
                // 红队：北船（gy0-6）
                new LevelUnit("redPirate",        0, 6,  3,  5, W(WeaponId.Cannonball, 10)),
                new LevelUnit("redPirate",        0, 10, 3,  5, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("redPirateCaptain", 0, 13, 5,  5, W(WeaponId.Cannonball, 10, WeaponId.GunpowderBarrel, 2)),
                // 蓝队：南船（gy8-14）
                new LevelUnit("cabinBoy",         1, 6,  11, 2, W(WeaponId.Cannonball, 10)),
                new LevelUnit("cabinBoy",         1, 10, 11, 2, W(WeaponId.Cannonball, 10, WeaponId.Dynamite, 5)),
                new LevelUnit("cabinBoyCaptain",  1, 13, 9,  2, W(WeaponId.Cannonball, 10, WeaponId.GunpowderBarrel, 2)),
            };
            return NewLevel(2, "双雄并舷", units, WeaponId.Mine, WeaponId.PiecesOfEight, WeaponId.Seagull);
        }

        static LevelData HillAndSkyIsland()
        {
            var units = new List<LevelUnit>
            {
                // 红队（玩家）：空岛东侧（遗迹侧；用户点名"把你这个角色放在上面"）——格子由
                // FloatingIslandSpawnTable 出生点局部坐标换算（局部 (x,z) + 场心 (10, 7.5)，1 格 = 2 单位）。
                new LevelUnit("redPirate",        0, 10, 3, 5, W(WeaponId.Cannonball, 10)),
                new LevelUnit("redPirate",        0, 12, 3, 5, W(WeaponId.Dynamite, 5)),
                new LevelUnit("redPirate",        0, 10, 5, 5, W(WeaponId.Boulder, 4, WeaponId.RumBottle, 4)),
                new LevelUnit("redPirateCaptain", 0, 12, 5, 5, W(WeaponId.PiecesOfEight, 2, WeaponId.VoodooDoll, 2)),
                // 蓝队：空岛西侧（瞭望台侧）
                new LevelUnit("cabinBoy",         1, 6, 4, 2, W(WeaponId.Cannonball, 10)),
                new LevelUnit("cabinBoy",         1, 8, 4, 2, W(WeaponId.Dynamite, 5)),
                new LevelUnit("cabinBoy",         1, 6, 6, 2, W(WeaponId.Boulder, 4, WeaponId.RumBottle, 4)),
                new LevelUnit("cabinBoyCaptain",  1, 8, 6, 2, W(WeaponId.PiecesOfEight, 2, WeaponId.VoodooDoll, 2)),
            };
            return NewLevel(3, "天空之岛", units, WeaponId.TidalWave, WeaponId.Anchor, WeaponId.ParachuteBomb);
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
        // ------------------------------------------------------------------

        /// <summary>样板关的逻辑地形；调用法与 TerrainCatalog.Build 同构。</summary>
        public static TileTerrainGrid BuildLogicGrid(int levelNumber)
        {
            int[] blocks = levelNumber == 1 ? CloudFieldBlocks()
                : levelNumber == 2 ? TwinShipBlocks()
                : HillBlocks();
            return new TileTerrainGrid(WidthTiles, DepthTiles, blocks, TerrainCatalog.DefaultBlockWorldHeight);
        }

        /// <summary>目标世界高度 → 块数（块高 0.5 单位）。</summary>
        static int Blocks(float worldY) =>
            Mathf.Max(1, Mathf.RoundToInt(worldY / TerrainCatalog.DefaultBlockWorldHeight));

        static int[] CloudFieldBlocks()
        {
            var b = new int[WidthTiles * DepthTiles];
            // 与 Lowpoly.CloudFieldGeometry 的真实布局对齐（云场平移到 (20,15) 后的格子）：
            // 主角云 12×12（格 7-12 × 5-10）y4.5；第一环四朵 y 7.5/2.5/6.5/2.5（避开主角云行）。
            Fill(b, 7, 12, 5, 10, Blocks(4.5f));   // 主角云
            Fill(b, 9, 11, 11, 11, Blocks(7.5f));  // 北云 Cloud1
            Fill(b, 6, 8, 7, 8, Blocks(2.5f));     // 西云 Cloud2
            Fill(b, 9, 11, 4, 4, Blocks(6.5f));    // 南云 Cloud3
            Fill(b, 13, 14, 7, 8, Blocks(2.5f));   // 东云 Cloud4
            return b;
        }

        static int[] TwinShipBlocks()
        {
            var b = new int[WidthTiles * DepthTiles];
            // 双大船并列：北船 gx3-16×gy0-6，南船 gx3-16×gy8-14，中间 gy7 一格水道；甲板 y3（r11 实测 y5 船底悬空 2 单位，降 2 龙骨贴水）。
            Fill(b, 3, 16, 0, 6, Blocks(3f));
            Fill(b, 3, 16, 8, 14, Blocks(3f));
            return b;
        }

        static int[] HillBlocks()
        {
            var b = new int[WidthTiles * DepthTiles];
            // 天空之岛：岛面椭圆（中心格 (10, 7.5)，半径 X 14.5 / Z 12.6 单位 = 7.25 / 6.3 格），
            // 只铺草皮平缓带（椭圆比 ≤ 0.8），高度 28 块 = y14，与空岛根 y13.3 + 草皮面 0.6-0.9 对齐。
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
        // 场景几何（自由几何装配；渲染层与格子完全无关）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把样板关的自由几何装进 buffers（RuntimeSceneArt 的 showcase 分支调用，
        /// 材质分组/网格落盘走它的 EmitGroups）。关 1/3 的低模与空岛几何由
        /// Lowpoly / Showcase 模块供给（合并点见内联注释）。
        /// </summary>
        public static void ComposeInto(ScenePropBuffers buffers, int levelNumber, Transform root)
        {
            // 落水危险虚线（绕竞技场一圈，旧管线的 UX 资产：告诉玩家边界在哪；参数抄 RuntimeSceneArt）。
            IslandShellGeometry.AddDashedBorder(buffers.Danger, WidthTiles, DepthTiles,
                3.15f, LevelGeometry.WaterSurfaceY + 0.012f, 0.9f, 0.55f, 0.18f);

            switch (levelNumber)
            {
                case 1:
                {
                    // 云朵场：Lowpoly 几何以原点为中心生成，平移到 20×15 格场的心 (20, 15)。
                    // BuildCloudField 幂等（同 parent 同名旧根先删后建，旧根的运行时网格一并回收），
                    // 多次 Rebuild 不堆积 GameObject 也不泄漏 Mesh。
                    Lowpoly.LowpolyStageReport cloudStage =
                        Lowpoly.LowpolyStageBuilder.BuildCloudField(root, null);
                    cloudStage.Root.transform.position = new Vector3(
                        LevelGeometry.TileToWorld(10f), 0f, LevelGeometry.TileToWorld(7.5f));
                    break;
                }

                case 2:
                {
                    // 双大船并列：北船中心 (格 10.0, 3.5)，南船中心 (格 10.0, 11.5)；
                    // LargeShipRecipe = 28 长 × 14 宽 × 2 桅 × 桅高 8.4，放样船体含
                    // 舭部渐变/舷弧/艉板/龙骨/艏斜桁，桅配横桁+帆+瞭望巢+索具（结构画全）。
                    var layout = new SceneKitLayout();
                    ComposeShipAt(layout, 10.0f, 3.5f, yaw: 180f, seed: 21);
                    ComposeShipAt(layout, 10.0f, 11.5f, yaw: 0f, seed: 22);
                    SceneKitComposer.Compose(buffers, layout, seed: 20);
                    break;
                }

                case 3:
                    // 天空之岛：空岛是场景内静态物（FloatingIslandShowcaseMenu.PlaceIntoBattleCenter
                    // 烘进 Battle.unity，RuntimeSceneArt.skyIslandRoot 按关卡号开关），此处无需再加几何。
                    break;
            }
        }

        static void ComposeShipAt(SceneKitLayout layout, float centerTileX, float centerTileZ, float yaw, int seed)
        {
            Vector3 deckCenter = new Vector3(
                LevelGeometry.TileToWorld(centerTileX),
                LevelGeometry.GroundTopY + TerrainCatalog.DefaultBlockWorldHeight * Blocks(3f),
                LevelGeometry.TileToWorld(centerTileZ));
            var parts = SceneKitCatalog.BuildCompleteShip(
                SceneKitCatalog.LargeShipRecipe, deckCenter, yaw, seed,
                hullLength: SceneKitCatalog.LargeShipRecipe.HullLength,
                bowLength: SceneKitCatalog.LargeShipRecipe.BowLength,
                sternLength: SceneKitCatalog.LargeShipRecipe.SternLength,
                hullBeam: SceneKitCatalog.LargeShipRecipe.HullBeam,
                mastAlongOffsets: null);
            for (int i = 0; i < parts.Count; i++)
                layout.Add(parts[i]);
        }
    }
}
