using System.Collections.Generic;

namespace PirateCrew.PirateCrew.Data
{
    /// <summary>
    /// 单个关卡的数据快照（纯 C#，供 <see cref="LevelCatalog"/> 与 Editor 资产生成器使用）。
    ///
    /// 【坐标字段说明】<see cref="Units"/> 里的 <c>gridX/gridY</c> 是<b>原版 XML 的瓦片格坐标</b>，
    /// 运行时像素坐标须按 §4.3 换算：<c>px = (xmlX+0.5)*32</c>、
    /// <c>py = (xmlY+0.5)*32 + 16 - bottomExtent</c>（见 <see cref="LevelCatalog.ToPixelX"/> /
    /// <see cref="LevelCatalog.ToPixelY"/>）。<see cref="WaterTileY"/> 是原版 water 对象的 y，
    /// 而 <see cref="WaterY"/> 是 §5.5 转换后的运行时值 <c>y*32</c>。
    /// </summary>
    public readonly struct LevelData
    {
        /// <summary>关卡序号（1–33）。</summary>
        public readonly int LevelNumber;

        /// <summary>关卡名（原版 XML name 属性；本作 XML 多为 "undefined"）。</summary>
        public readonly string Name;

        /// <summary>关卡宽度（瓦片）。</summary>
        public readonly int WidthTiles;

        /// <summary>关卡高度（瓦片）。</summary>
        public readonly int HeightTiles;

        /// <summary>原版 XML players 属性（1/2）。注意：本作 raw 恒为 undefined，实际模式由菜单按钮决定（§7.2）。</summary>
        public readonly int OriginalXmlPlayers;

        /// <summary>原版 water 对象的 y（瓦片格）。</summary>
        public readonly float WaterTileY;

        /// <summary>运行时水面 Y（px）= WaterTileY * 32（§5.5）。</summary>
        public readonly float WaterY;

        /// <summary>
        /// 宝箱同时存在上限。取 §5.5 硬编码的 3；原版 XML 的 potentialWeapons 另带 maxChests 属性
        /// （见 <see cref="SourceXmlMaxChests"/>），二者关系文档未明，此处按 §5.5 用 3。
        /// </summary>
        public readonly int MaxChests;

        /// <summary>原版 XML 里 potentialWeapons / water 的 maxChests 属性原值（文档未收录运行时用途，仅存档备查）。</summary>
        public readonly int SourceXmlMaxChests;

        /// <summary>空投武器池（§5.5 potentialWeaponList）。</summary>
        public readonly IReadOnlyList<WeaponStack> PotentialWeapons;

        /// <summary>双方出战单位。</summary>
        public readonly IReadOnlyList<LevelUnit> Units;

        public LevelData(
            int levelNumber,
            string name,
            int widthTiles,
            int heightTiles,
            int originalXmlPlayers,
            float waterTileY,
            int maxChests,
            int sourceXmlMaxChests,
            IReadOnlyList<WeaponStack> potentialWeapons,
            IReadOnlyList<LevelUnit> units)
        {
            LevelNumber = levelNumber;
            Name = name;
            WidthTiles = widthTiles;
            HeightTiles = heightTiles;
            OriginalXmlPlayers = originalXmlPlayers;
            WaterTileY = waterTileY;
            WaterY = waterTileY * 32f;               // §5.5: Controller.water.y = y * 32
            MaxChests = maxChests;
            SourceXmlMaxChests = sourceXmlMaxChests;
            PotentialWeapons = potentialWeapons;
            Units = units;
        }
    }

    /// <summary>
    /// 关卡目录的纯 C# 静态转写。
    ///
    /// 【出处】静态逆向文档 §7.2「关卡数量与配置」代表关表，以及 §4.3（坐标换算）、§5.5（水面/空投）。
    ///
    /// 【转写范围】只转写 3 个代表性关卡，覆盖三种形态：
    ///   1. level_1  —— 新手关（小规模 5v3，全员保底樱桃炸弹，空投炸药；水面 y=14）
    ///   2. level_4  —— 多对多混战关（6v6，空投含 seagull / piecesOfEight 等特殊武器；水面 y=17）
    ///   3. level_27 —— 1v1 对等决斗关（2P 面板，双船长单挑；水面 y=19）
    /// 其余 30 关在 <see cref="PendingLevelNumbers"/> 显式标注「待补」。
    ///
    /// 【布阵字段来源】§7.2 代表表只给出双方人数与武器池概览，**不含每个单位的坐标 / luck / 初始武器**；
    /// 这些字段只存在于原版关卡 XML（`<obj>` 的属性）里。为把"双方出战单位列表"的字段结构落全，
    /// 本表按原版 XML（`external/swf-decompile/levels_all.json`，即文档 §7.2 指明的数据源）逐单位转写；
    /// XML 有而文档 §7.2 未收录的字段（每单位坐标/luck/初始武器、potentialWeapons.maxChests、
    /// XML players 属性）均已在此与各武器装备的 Remark 中注明，未臆造任何数值。
    /// </summary>
    public static class LevelCatalog
    {
        /// <summary>原版关卡总数（§1 / §7.2：total_levels = 33）。</summary>
        public const int TotalLevels = 33;

        /// <summary>§5.5 硬编码的宝箱同时存在上限。</summary>
        public const int DefaultMaxChests = 3;

        // ------------------------------------------------------------------
        // 代表关 1：新手关（§7.2 level_1）
        // ------------------------------------------------------------------
        static readonly LevelData _level1 = new LevelData(
            levelNumber: 1,
            name: "level_1",
            widthTiles: 50,
            heightTiles: 17,
            originalXmlPlayers: 1,
            waterTileY: 14f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1,
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → 无限
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain，每人保底 cherryBomb×∞
                new LevelUnit("redPirate",        0, 17, 10, 5, new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) }),
                new LevelUnit("redPirate",        0, 12,  5, 5, new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) }),
                new LevelUnit("redPirate",        0,  7, 11, 5, new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) }),
                new LevelUnit("redPirate",        0, 29, 11, 5, new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) }),
                new LevelUnit("redPirateCaptain", 0, 34, 11, 5, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 5),   // XML dynamite="5" → 有限 5 件
                }),
                // 蓝队（team2，AI）：2 cabinBoy + 1 cabinBoyCaptain
                new LevelUnit("cabinBoy",         1, 48, 10, 2, new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) }),
                new LevelUnit("cabinBoy",         1, 43, 10, 2, new List<WeaponStack> { new WeaponStack(WeaponId.CherryBomb, 10) }),
                new LevelUnit("cabinBoyCaptain",  1, 46,  4, 2, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                }),
            });

        // ------------------------------------------------------------------
        // 代表关 2：多对多混战关（§7.2 level_4）
        // ------------------------------------------------------------------
        static readonly LevelData _level4 = new LevelData(
            levelNumber: 4,
            name: "level_4",
            widthTiles: 56,
            heightTiles: 18,
            originalXmlPlayers: 1,
            waterTileY: 17f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1,
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Dynamite, 5),
                new WeaponStack(WeaponId.GunpowderBarrel, 2),
                new WeaponStack(WeaponId.ParachuteBomb, 2),
                new WeaponStack(WeaponId.PiecesOfEight, 5),
                new WeaponStack(WeaponId.Seagull, 1),
                new WeaponStack(WeaponId.WoodenCrate, 2),
            },
            units: new List<LevelUnit>
            {
                // 红队 6：redPirateCaptain + 5 redPirate；统一 cherryBomb∞/cannon∞，部分带 special
                new LevelUnit("redPirateCaptain", 0,  7,  4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.PiecesOfEight, 10),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("redPirate", 0,  1, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("redPirate", 0, 21, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("redPirate", 0,  9, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("redPirate", 0,  8,  9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("redPirate", 0, 18,  9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                // 蓝队 6：soldierCaptain + 5 soldier，luck 多为 10
                new LevelUnit("soldierCaptain", 1, 48,  4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 5),
                    new WeaponStack(WeaponId.PiecesOfEight, 10),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("soldier", 1, 55, 13, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.PiecesOfEight, 10),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("soldier", 1, 34, 13, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.PiecesOfEight, 10),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("soldier", 1, 45, 14, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.PiecesOfEight, 10),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("soldier", 1, 50,  9, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.PiecesOfEight, 10),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
                new LevelUnit("soldier", 1, 38,  9, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.Dynamite, 1),
                    new WeaponStack(WeaponId.PiecesOfEight, 10),
                    new WeaponStack(WeaponId.ParachuteBomb, 1),
                    new WeaponStack(WeaponId.Cannon, 10),
                }),
            });

        // ------------------------------------------------------------------
        // 代表关 3：1v1 对等决斗关（§7.2 level_27，2P 面板）
        // ------------------------------------------------------------------
        static readonly LevelData _level27 = new LevelData(
            levelNumber: 27,
            name: "level_27",
            widthTiles: 21,
            heightTiles: 20,
            originalXmlPlayers: 2,
            waterTileY: 19f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1,
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Banana, 5),
                new WeaponStack(WeaponId.GunpowderBarrel, 5),
                new WeaponStack(WeaponId.Mine, 5),
                new WeaponStack(WeaponId.ParachuteBomb, 5),
                new WeaponStack(WeaponId.RumBottle, 5),
                new WeaponStack(WeaponId.WoodenCrate, 5),
            },
            units: new List<LevelUnit>
            {
                new LevelUnit("redPirateCaptain",  0,  3, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.WoodenCrate, 1),
                    new WeaponStack(WeaponId.GunpowderBarrel, 1),
                }),
                new LevelUnit("bluePirateCaptain", 1, 17, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10),
                    new WeaponStack(WeaponId.WoodenCrate, 1),
                    new WeaponStack(WeaponId.GunpowderBarrel, 1),
                }),
            });

        static readonly List<LevelData> _all = new List<LevelData> { _level1, _level4, _level27 };

        static readonly List<int> _pending = BuildPendingLevels();

        static List<int> BuildPendingLevels()
        {
            var pending = new List<int>();
            for (int n = 1; n <= TotalLevels; n++)
            {
                if (!IsTranscribed(n))
                    pending.Add(n);
            }
            return pending;
        }

        /// <summary>已转写的 3 个代表关。</summary>
        public static IReadOnlyList<LevelData> All => _all;

        /// <summary>已转写的关卡数量（应为 3）。</summary>
        public static int Count => _all.Count;

        /// <summary>已转写的关卡号集合。</summary>
        public static IReadOnlyList<int> TranscribedLevelNumbers
        {
            get
            {
                var numbers = new List<int>(_all.Count);
                for (int i = 0; i < _all.Count; i++)
                    numbers.Add(_all[i].LevelNumber);
                return numbers;
            }
        }

        /// <summary>其余关卡号（1–33 中未转写的 30 关），显式标注「待补」。</summary>
        public static IReadOnlyList<int> PendingLevelNumbers => _pending;

        /// <summary>该关卡号是否已转写。</summary>
        public static bool IsTranscribed(int levelNumber)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].LevelNumber == levelNumber)
                    return true;
            }
            return false;
        }

        /// <summary>按关卡号取数据；未转写时抛 <see cref="KeyNotFoundException"/>。</summary>
        public static LevelData Get(int levelNumber)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].LevelNumber == levelNumber)
                    return _all[i];
            }
            throw new KeyNotFoundException(
                "LevelCatalog 尚未转写关卡 " + levelNumber + "（见 PendingLevelNumbers，待补）。");
        }

        // ------------------------------------------------------------------
        // §4.3 坐标换算
        // ------------------------------------------------------------------

        /// <summary>§4.3：<c>px = (xmlX + 0.5) * 32</c>。</summary>
        public static float ToPixelX(float xmlX)
        {
            return (xmlX + 0.5f) * 32f;
        }

        /// <summary>§4.3：<c>py = (xmlY + 0.5) * 32 + 16 - bottomExtent</c>（bottomExtent 见 CrewCatalog.BottomExtent = 8）。</summary>
        public static float ToPixelY(float xmlY, float bottomExtent)
        {
            return (xmlY + 0.5f) * 32f + 16f - bottomExtent;
        }

        /// <summary>§5.5：<c>Controller.water.y = waterTileY * 32</c>。</summary>
        public static float ToWaterY(float waterTileY)
        {
            return waterTileY * 32f;
        }
    }
}
