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
    /// 【出处】静态逆向文档 §7.2「关卡数量与配置」代表关表与尺寸表，以及 §4.3（坐标换算/队伍归属）、
    ///         §5.5（水面 / 空投 / 初始武器）与 §7.1（1P 战役 1–15、2P 面板 16–33）。
    ///
    /// 【转写范围】<b>全部 33 关</b>（§7.2：total_levels = 33），无「待补」关卡。
    ///   本文件保留三个代表关作为范式样板（覆盖三种形态，也是 §7.2 代表表的原班人马）：
    ///     1. level_1  —— 新手关（小规模 5v3，全员保底樱桃炸弹，空投炸药；水面 y=14）
    ///     2. level_4  —— 多对多混战关（6v6，空投含 seagull / piecesOfEight 等特殊武器；水面 y=17）
    ///     3. level_27 —— 1v1 对等决斗关（2P 面板，双船长单挑；水面 y=19）
    ///   其余 30 关按同一范式拆到分部文件，规模上限与可读性考虑：
    ///     - `LevelCatalog.Levels2.cs` —— level_2–level_15（level_4 除外）
    ///     - `LevelCatalog.Levels3.cs` —— level_16–level_33（level_27 除外）
    ///   三个文件的静态字段由本文件的静态构造函数统一汇总成 <see cref="All"/>，顺序即关卡号升序，
    ///   避免分部类跨文件静态字段初始化顺序不确定带来的隐患。
    ///
    /// 【布阵字段来源】§7.2 代表表只给出双方人数与武器池概览，**不含每个单位的坐标 / luck / 初始武器**；
    /// 这些字段只存在于原版关卡 XML（`<obj>` 的属性）里。为把"双方出战单位列表"的字段结构落全，
    /// 本表按原版 XML（`external/swf-decompile/levels_all.json`，即文档 §7.2 指明的数据源）逐单位转写；
    /// XML 有而文档 §7.2 未收录的字段（每单位坐标/luck/初始武器、potentialWeapons.maxChests、
    /// XML players 属性）均已在此与各武器装备的 Remark 中注明，未臆造任何数值。
    /// </summary>
    public static partial class LevelCatalog
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

        // ------------------------------------------------------------------
        // 汇总：静态构造函数保证「先初始化 33 个关卡字段，再建索引与列表」
        //
        // 分部类（本文件 + Levels2 + Levels3）的静态字段初始化顺序在跨文件时是未定义的，
        // 若把汇总写在字段初始化式里（如 _all = new List<LevelData> { _level1, ... }），
        // 就可能读到尚未初始化的 struct 字段（全 0）而静默得到空关卡。
        // 显式静态构造函数可保证：所有静态字段初始化式执行完毕后，才执行函数体。
        // ------------------------------------------------------------------
        static readonly Dictionary<int, LevelData> _byNumber;
        static readonly List<LevelData> _all;
        static readonly List<int> _pending;

        static LevelCatalog()
        {
            _byNumber = BuildIndex();
            _all = BuildAllLevels();
            _pending = BuildPendingLevels();
        }

        /// <summary>逐关登记（新增关卡时在本表补一行即可，顺序不敏感）。</summary>
        static Dictionary<int, LevelData> BuildIndex()
        {
            var map = new Dictionary<int, LevelData>(TotalLevels);
            map[_level1.LevelNumber] = _level1;
            map[_level2.LevelNumber] = _level2;
            map[_level3.LevelNumber] = _level3;
            map[_level4.LevelNumber] = _level4;
            map[_level5.LevelNumber] = _level5;
            map[_level6.LevelNumber] = _level6;
            map[_level7.LevelNumber] = _level7;
            map[_level8.LevelNumber] = _level8;
            map[_level9.LevelNumber] = _level9;
            map[_level10.LevelNumber] = _level10;
            map[_level11.LevelNumber] = _level11;
            map[_level12.LevelNumber] = _level12;
            map[_level13.LevelNumber] = _level13;
            map[_level14.LevelNumber] = _level14;
            map[_level15.LevelNumber] = _level15;
            map[_level16.LevelNumber] = _level16;
            map[_level17.LevelNumber] = _level17;
            map[_level18.LevelNumber] = _level18;
            map[_level19.LevelNumber] = _level19;
            map[_level20.LevelNumber] = _level20;
            map[_level21.LevelNumber] = _level21;
            map[_level22.LevelNumber] = _level22;
            map[_level23.LevelNumber] = _level23;
            map[_level24.LevelNumber] = _level24;
            map[_level25.LevelNumber] = _level25;
            map[_level26.LevelNumber] = _level26;
            map[_level27.LevelNumber] = _level27;
            map[_level28.LevelNumber] = _level28;
            map[_level29.LevelNumber] = _level29;
            map[_level30.LevelNumber] = _level30;
            map[_level31.LevelNumber] = _level31;
            map[_level32.LevelNumber] = _level32;
            map[_level33.LevelNumber] = _level33;
            return map;
        }

        static List<LevelData> BuildAllLevels()
        {
            var all = new List<LevelData>(TotalLevels);
            for (int n = 1; n <= TotalLevels; n++)
                all.Add(Get(n));
            return all;
        }

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

        /// <summary>已转写的关卡（= 全部 33 关，按关卡号升序）。</summary>
        public static IReadOnlyList<LevelData> All => _all;

        /// <summary>已转写的关卡数量（应为 33 = <see cref="TotalLevels"/>）。</summary>
        public static int Count => _all.Count;

        /// <summary>已转写的关卡号集合（1..33，升序）。</summary>
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

        /// <summary>
        /// 「待补」关卡号（1–33 中未转写者）。33 关已于本次全部补齐，恒为空列表；
        /// 保留属性是为了不打断 <c>M2DataAssetGenerator</c> 等既有调用方的编译。
        /// </summary>
        public static IReadOnlyList<int> PendingLevelNumbers => _pending;

        /// <summary>该关卡号是否已转写。33 关全部转写后 = 关卡号是否落在 1–<see cref="TotalLevels"/>。</summary>
        public static bool IsTranscribed(int levelNumber)
        {
            return levelNumber >= 1 && levelNumber <= TotalLevels;
        }

        /// <summary>按关卡号取数据；关卡号落在 1–33 之外时抛 <see cref="KeyNotFoundException"/>。</summary>
        public static LevelData Get(int levelNumber)
        {
            if (_byNumber.TryGetValue(levelNumber, out LevelData level))
                return level;

            throw new KeyNotFoundException(
                "LevelCatalog 中不存在关卡 " + levelNumber + "（合法范围 1–" + TotalLevels + "）。");
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
