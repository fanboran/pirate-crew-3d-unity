using System.Collections.Generic;

namespace PirateCrew.PirateCrew.Data
{
    /// <summary>
    /// <see cref="LevelCatalog"/> 的分部文件：level_16–level_33（level_27 见主文件 LevelCatalog.cs）
    ///
    /// 【转写范式】与 LevelCatalog.cs 的 level_1 / level_4 / level_27 完全一致：
    ///   1. <c>potentialWeapons</c> = XML <c>type="potentialWeapons"</c> 对象的属性展开
    ///      （排除 x/y/type/luck/maxChests；值 10 = 无限，§5.5）；同关若出现多个则取最后一个
    ///      （§5.5 是赋值语义，后写覆盖前写）；
    ///   2. <c>units</c> = XML 全部 <c>&lt;obj&gt;</c>（排除 potentialWeapons / water），
    ///      队伍按 §4.3 硬编码判定（redPirate / redPirateCaptain → 0，其余一律 → 1），
    ///      <b>输出顺序为红队在前、队内保持 XML 原序</b>（与既有三关的分组写法一致）；
    ///      没有武器属性的 obj 落成空 <c>List&lt;WeaponStack&gt;</c>（level_15 有两例）；
    ///   3. <c>sourceXmlMaxChests</c> = potentialWeapons 对象的 maxChests 属性原值；
    ///      <b>该属性缺省时记 1</b>，沿用 level_27 的缺省口径（该关 XML 同样没有 maxChests）；
    ///   4. <c>name</c> 取关卡键名（XML 的 name 属性恒为 "undefined"）；
    ///   5. <c>maxChests</c> 恒为 §5.5 硬编码的 3。
    ///
    /// 【未建模的 XML 字段】部分 <c>&lt;obj&gt;</c> 还带 <c>maxChests</c>（每单位宝箱上限）、
    ///   water 对象上还残留武器属性；§5.5 的 <c>setWeapons</c> 明确排除它们，故不落表。
    ///
    /// 【自证】与 <c>external/swf-decompile/levels_all.json</c> 的逐字段比对见
    ///   <c>PirateCrew.Tests.LevelCatalogJsonParityTests</c>（无头可跑）。
    /// </summary>
    public static partial class LevelCatalog
    {

        // ------------------------------------------------------------------
        // level_16：50×17 瓦片，XML players=1，水面 tile y=14，空投池 13 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_16（50×17，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：4 bluePirate + 1 bluePirateCaptain。
        /// 【模式】§7.2：本关 XML players="1"，但关卡 16–33 属 2P 面板（双人热座），实际模式以菜单按钮为准。
        /// </summary>
        static readonly LevelData _level16 = new LevelData(
            levelNumber: 16,
            name: "level_16",
            widthTiles: 50,
            heightTiles: 17,
            originalXmlPlayers: 1,
            waterTileY: 14f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Dynamite, 3), // XML dynamite="3" → 3 件
                new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 3), // XML mine="3" → 3 件
                new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 23, 3, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 1, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 12, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 46, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 27, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                // 蓝队（team2）：4 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 37, 3, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 46, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 16, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 3, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 35, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                    new WeaponStack(WeaponId.Mine, 4), // XML mine="4" → 4 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_17：47×28 瓦片，XML players=1，水面 tile y=27，空投池 13 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_17（47×28，与 §7.2 尺寸表一致）。
        /// 红队（team1）6 人：5 redPirate + 1 redPirateCaptain；蓝队（team2）6 人：5 bluePirate + 1 bluePirateCaptain。
        /// 【模式】§7.2：本关 XML players="1"，但关卡 16–33 属 2P 面板（双人热座），实际模式以菜单按钮为准。
        /// </summary>
        static readonly LevelData _level17 = new LevelData(
            levelNumber: 17,
            name: "level_17",
            widthTiles: 47,
            heightTiles: 28,
            originalXmlPlayers: 1,
            waterTileY: 27f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                new WeaponStack(WeaponId.ParachuteBomb, 6), // XML parachuteBomb="6" → 6 件
                new WeaponStack(WeaponId.WoodenCrate, 4), // XML woodenCrate="4" → 4 件
                new WeaponStack(WeaponId.GunpowderBarrel, 4), // XML gunpowderBarrel="4" → 4 件
                new WeaponStack(WeaponId.Seagull, 3), // XML seagull="3" → 3 件
                new WeaponStack(WeaponId.Mine, 7), // XML mine="7" → 7 件
                new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 3), // XML anchor="3" → 3 件
                new WeaponStack(WeaponId.VoodooDoll, 3), // XML voodooDoll="3" → 3 件
                new WeaponStack(WeaponId.TidalWave, 3), // XML tidalWave="3" → 3 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：5 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 5, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 23, 8, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 42, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 6, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 43, 19, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 24, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                // 蓝队（team2）：5 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 41, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 8, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 34, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 13, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 22, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 32, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_18：40×21 瓦片，XML players=1，水面 tile y=18，空投池 11 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_18（40×21，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：4 bluePirate + 1 bluePirateCaptain。
        /// 【模式】§7.2：本关 XML players="1"，但关卡 16–33 属 2P 面板（双人热座），实际模式以菜单按钮为准。
        /// </summary>
        static readonly LevelData _level18 = new LevelData(
            levelNumber: 18,
            name: "level_18",
            widthTiles: 40,
            heightTiles: 21,
            originalXmlPlayers: 1,
            waterTileY: 18f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                new WeaponStack(WeaponId.PiecesOfEight, 10), // XML piecesOfEight="10" → ∞
                new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                new WeaponStack(WeaponId.ParachuteBomb, 10), // XML parachuteBomb="10" → ∞
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                new WeaponStack(WeaponId.Anchor, 2), // XML anchor="2" → 2 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 11, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 6, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("redPirateCaptain", 0, 8, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 5, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 12, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                // 蓝队（team2）：4 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 29, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("bluePirate", 1, 34, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 32, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("bluePirate", 1, 35, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                }),
                new LevelUnit("bluePirate", 1, 28, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                })
            });

        // ------------------------------------------------------------------
        // level_19：56×18 瓦片，XML players=1，水面 tile y=17，空投池 11 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_19（56×18，与 §7.2 尺寸表一致）。
        /// 红队（team1）6 人：1 redPirateCaptain + 5 redPirate；蓝队（team2）6 人：1 bluePirateCaptain + 5 bluePirate。
        /// 【模式】§7.2：本关 XML players="1"，但关卡 16–33 属 2P 面板（双人热座），实际模式以菜单按钮为准。
        /// </summary>
        static readonly LevelData _level19 = new LevelData(
            levelNumber: 19,
            name: "level_19",
            widthTiles: 56,
            heightTiles: 18,
            originalXmlPlayers: 1,
            waterTileY: 17f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                new WeaponStack(WeaponId.PiecesOfEight, 10), // XML piecesOfEight="10" → ∞
                new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                new WeaponStack(WeaponId.ParachuteBomb, 10), // XML parachuteBomb="10" → ∞
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 2), // XML voodooDoll="2" → 2 件
                new WeaponStack(WeaponId.TidalWave, 2), // XML tidalWave="2" → 2 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：1 redPirateCaptain + 5 redPirate
                new LevelUnit("redPirateCaptain", 0, 7, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 0, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 21, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 11, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 5, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 17, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                // 蓝队（team2）：1 bluePirateCaptain + 5 bluePirate
                new LevelUnit("bluePirateCaptain", 1, 48, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 55, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 34, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 44, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 50, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 38, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_20：44×18 瓦片，XML players=1，水面 tile y=15，空投池 5 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_20（44×18，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：4 bluePirate + 1 bluePirateCaptain。
        /// 【模式】§7.2：本关 XML players="1"，但关卡 16–33 属 2P 面板（双人热座），实际模式以菜单按钮为准。
        /// </summary>
        static readonly LevelData _level20 = new LevelData(
            levelNumber: 20,
            name: "level_20",
            widthTiles: 44,
            heightTiles: 18,
            originalXmlPlayers: 1,
            waterTileY: 15f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.RumBottle, 4), // XML rumBottle="4" → 4 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 4), // XML anchor="4" → 4 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 4), // XML tidalWave="4" → 4 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 42, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 7, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 30, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("redPirateCaptain", 0, 9, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 28, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                // 蓝队（team2）：4 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 36, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 17, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("bluePirateCaptain", 1, 34, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 19, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 1, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_21：63×35 瓦片，XML players=2，水面 tile y=32，空投池 7 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_21（63×35，与 §7.2 尺寸表一致）。
        /// 红队（team1）6 人：1 redPirateCaptain + 5 redPirate；蓝队（team2）6 人：1 bluePirateCaptain + 5 bluePirate。
        /// </summary>
        static readonly LevelData _level21 = new LevelData(
            levelNumber: 21,
            name: "level_21",
            widthTiles: 63,
            heightTiles: 35,
            originalXmlPlayers: 2,
            waterTileY: 32f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                new WeaponStack(WeaponId.ParachuteBomb, 10), // XML parachuteBomb="10" → ∞
                new WeaponStack(WeaponId.Seagull, 10), // XML seagull="10" → ∞
                new WeaponStack(WeaponId.Anchor, 10), // XML anchor="10" → ∞
                new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：1 redPirateCaptain + 5 redPirate
                new LevelUnit("redPirateCaptain", 0, 38, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 41, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 43, 24, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 50, 29, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 58, 29, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 54, 29, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                // 蓝队（team2）：1 bluePirateCaptain + 5 bluePirate
                new LevelUnit("bluePirateCaptain", 1, 30, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 27, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 24, 24, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 11, 29, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 19, 29, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 15, 29, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_22：115×28 瓦片，XML players=2，水面 tile y=25，空投池 5 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_22（115×28，与 §7.2 尺寸表一致）。
        /// 红队（team1）14 人：13 redPirate + 1 redPirateCaptain；蓝队（team2）14 人：13 bluePirate + 1 bluePirateCaptain。
        /// </summary>
        static readonly LevelData _level22 = new LevelData(
            levelNumber: 22,
            name: "level_22",
            widthTiles: 115,
            heightTiles: 28,
            originalXmlPlayers: 2,
            waterTileY: 25f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：13 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 39, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 57, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 58, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 59, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 89, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 88, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 87, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 71, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 72, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 70, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 11, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 10, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 9, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 66, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                // 蓝队（team2）：13 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 65, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 64, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 63, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 107, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 109, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 108, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 48, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 47, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 49, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 28, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 29, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 30, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 78, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 55, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_23：24×16 瓦片，XML players=2，水面 tile y=13，空投池 6 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_23（24×16，与 §7.2 尺寸表一致）。
        /// 红队（team1）2 人：1 redPirate + 1 redPirateCaptain；蓝队（team2）2 人：1 bluePirateCaptain + 1 bluePirate。
        /// </summary>
        static readonly LevelData _level23 = new LevelData(
            levelNumber: 23,
            name: "level_23",
            widthTiles: 24,
            heightTiles: 16,
            originalXmlPlayers: 2,
            waterTileY: 13f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：1 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 14, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                }),
                new LevelUnit("redPirateCaptain", 0, 11, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                }),
                // 蓝队（team2）：1 bluePirateCaptain + 1 bluePirate
                new LevelUnit("bluePirateCaptain", 1, 13, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 10, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_24：23×34 瓦片，XML players=2，水面 tile y=30，空投池 9 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_24（23×34，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：4 bluePirate + 1 bluePirateCaptain。
        /// </summary>
        static readonly LevelData _level24 = new LevelData(
            levelNumber: 24,
            name: "level_24",
            widthTiles: 23,
            heightTiles: 34,
            originalXmlPlayers: 2,
            waterTileY: 30f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                new WeaponStack(WeaponId.ParachuteBomb, 10), // XML parachuteBomb="10" → ∞
                new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 13, 20, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 9, 16, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 13, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                }),
                new LevelUnit("redPirateCaptain", 0, 9, 8, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 9, 24, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                }),
                // 蓝队（team2）：4 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 9, 20, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 13, 16, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 9, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 13, 8, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 13, 24, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                })
            });

        // ------------------------------------------------------------------
        // level_25：59×27 瓦片，XML players=2，水面 tile y=26，空投池 15 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_25（59×27，与 §7.2 尺寸表一致）。
        /// 红队（team1）6 人：1 redPirateCaptain + 5 redPirate；蓝队（team2）6 人：1 bluePirateCaptain + 5 bluePirate。
        /// </summary>
        static readonly LevelData _level25 = new LevelData(
            levelNumber: 25,
            name: "level_25",
            widthTiles: 59,
            heightTiles: 27,
            originalXmlPlayers: 2,
            waterTileY: 26f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                new WeaponStack(WeaponId.PiecesOfEight, 10), // XML piecesOfEight="10" → ∞
                new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：1 redPirateCaptain + 5 redPirate
                new LevelUnit("redPirateCaptain", 0, 24, 3, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 57, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 29, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 39, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 8, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 0, 23, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                // 蓝队（team2）：1 bluePirateCaptain + 5 bluePirate
                new LevelUnit("bluePirateCaptain", 1, 25, 3, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 58, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 20, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 10, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 41, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 1, 23, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                })
            });

        // ------------------------------------------------------------------
        // level_26：77×32 瓦片，XML players=2，水面 tile y=29，空投池 11 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_26（77×32，与 §7.2 尺寸表一致）。
        /// 红队（team1）7 人：6 redPirate + 1 redPirateCaptain；蓝队（team2）7 人：6 bluePirate + 1 bluePirateCaptain。
        /// </summary>
        static readonly LevelData _level26 = new LevelData(
            levelNumber: 26,
            name: "level_26",
            widthTiles: 77,
            heightTiles: 32,
            originalXmlPlayers: 2,
            waterTileY: 29f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                new WeaponStack(WeaponId.PiecesOfEight, 2), // XML piecesOfEight="2" → 2 件
                new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                new WeaponStack(WeaponId.ParachuteBomb, 10), // XML parachuteBomb="10" → ∞
                new WeaponStack(WeaponId.Seagull, 3), // XML seagull="3" → 3 件
                new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                new WeaponStack(WeaponId.Anchor, 3), // XML anchor="3" → 3 件
                new WeaponStack(WeaponId.VoodooDoll, 3), // XML voodooDoll="3" → 3 件
                new WeaponStack(WeaponId.TidalWave, 3), // XML tidalWave="3" → 3 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：6 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 64, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 5, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 75, 25, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 60, 25, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 34, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("redPirateCaptain", 0, 10, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                    new WeaponStack(WeaponId.TidalWave, 2), // XML tidalWave="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 29, 24, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                // 蓝队（team2）：6 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 13, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 47, 25, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 2, 25, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 17, 25, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 67, 9, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                    new WeaponStack(WeaponId.TidalWave, 2), // XML tidalWave="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 72, 16, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("bluePirate", 1, 42, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 3), // XML banana="3" → 3 件
                    new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                })
            });

        // ------------------------------------------------------------------
        // level_28：44×20 瓦片，XML players=2，水面 tile y=17，空投池 11 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_28（44×20，与 §7.2 尺寸表一致）。
        /// 红队（team1）4 人：3 redPirate + 1 redPirateCaptain；蓝队（team2）4 人：3 bluePirate + 1 bluePirateCaptain。
        /// </summary>
        static readonly LevelData _level28 = new LevelData(
            levelNumber: 28,
            name: "level_28",
            widthTiles: 44,
            heightTiles: 20,
            originalXmlPlayers: 2,
            waterTileY: 17f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Boulder, 5), // XML boulder="5" → 5 件
                new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                new WeaponStack(WeaponId.PiecesOfEight, 2), // XML piecesOfEight="2" → 2 件
                new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 2), // XML anchor="2" → 2 件
                new WeaponStack(WeaponId.VoodooDoll, 2), // XML voodooDoll="2" → 2 件
                new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：3 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 32, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 38, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 23, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 11, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                // 蓝队（team2）：3 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 12, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 33, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 5, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 21, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_29：44×26 瓦片，XML players=2，水面 tile y=23，空投池 13 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_29（44×26，与 §7.2 尺寸表一致）。
        /// 红队（team1）6 人：5 redPirate + 1 redPirateCaptain；蓝队（team2）6 人：5 bluePirate + 1 bluePirateCaptain。
        /// </summary>
        static readonly LevelData _level29 = new LevelData(
            levelNumber: 29,
            name: "level_29",
            widthTiles: 44,
            heightTiles: 26,
            originalXmlPlayers: 2,
            waterTileY: 23f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 3), // XML cherryBomb="3" → 3 件
                new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：5 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 43, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 20, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 40, 8, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 21, 6, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 7, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 20, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                // 蓝队（team2）：5 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 0, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 24, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 24, 6, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 2, 6, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 36, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 24, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_30：90×12 瓦片，XML players=2，水面 tile y=9，空投池 11 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_30（90×12，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：1 redPirateCaptain + 4 redPirate；蓝队（team2）5 人：4 bluePirate + 1 bluePirateCaptain。
        /// </summary>
        static readonly LevelData _level30 = new LevelData(
            levelNumber: 30,
            name: "level_30",
            widthTiles: 90,
            heightTiles: 12,
            originalXmlPlayers: 2,
            waterTileY: 9f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 7), // XML cherryBomb="7" → 7 件
                new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                new WeaponStack(WeaponId.PiecesOfEight, 3), // XML piecesOfEight="3" → 3 件
                new WeaponStack(WeaponId.RumBottle, 7), // XML rumBottle="7" → 7 件
                new WeaponStack(WeaponId.Banana, 7), // XML banana="7" → 7 件
                new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                new WeaponStack(WeaponId.Seagull, 5), // XML seagull="5" → 5 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：1 redPirateCaptain + 4 redPirate
                new LevelUnit("redPirateCaptain", 0, 4, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 80, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 59, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 29, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 43, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                // 蓝队（team2）：4 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 49, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 15, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 87, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 68, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 37, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 5), // XML rumBottle="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_31：48×30 瓦片，XML players=2，水面 tile y=27，空投池 11 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_31（48×30，与 §7.2 尺寸表一致）。
        /// 红队（team1）4 人：3 redPirate + 1 redPirateCaptain；蓝队（team2）4 人：3 bluePirate + 1 bluePirateCaptain。
        /// </summary>
        static readonly LevelData _level31 = new LevelData(
            levelNumber: 31,
            name: "level_31",
            widthTiles: 48,
            heightTiles: 30,
            originalXmlPlayers: 2,
            waterTileY: 27f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 3), // XML mine="3" → 3 件
                new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：3 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 28, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 12, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("redPirateCaptain", 0, 9, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 14, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                // 蓝队（team2）：3 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 17, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("bluePirate", 1, 35, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 38, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 33, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 5), // XML cherryBomb="5" → 5 件
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                })
            });

        // ------------------------------------------------------------------
        // level_32：86×20 瓦片，XML players=2，水面 tile y=17，空投池 3 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_32（86×20，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：1 bluePirateCaptain + 4 bluePirate。
        /// 【特殊】XML 里本关有<b>两个</b> potentialWeapons 对象（y=5 x=43 的全武器池、y=10 x=54 的三件池）；
        /// 按 §5.5 后写覆盖前写，取<b>最后一个</b>（三件池）。
        /// </summary>
        static readonly LevelData _level32 = new LevelData(
            levelNumber: 32,
            name: "level_32",
            widthTiles: 86,
            heightTiles: 20,
            originalXmlPlayers: 2,
            waterTileY: 17f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 3, // XML potentialWeapons maxChests="3"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 47, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("redPirateCaptain", 0, 20, 8, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 70, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 52, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 27, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                // 蓝队（team2）：1 bluePirateCaptain + 4 bluePirate
                new LevelUnit("bluePirateCaptain", 1, 64, 8, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 13, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 35, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 59, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("bluePirate", 1, 40, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_33：41×33 瓦片，XML players=1，水面 tile y=30，空投池 15 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_33（41×33，与 §7.2 尺寸表一致）。
        /// 红队（team1）8 人：7 redPirate + 1 redPirateCaptain；蓝队（team2）8 人：7 bluePirate + 1 bluePirateCaptain。
        /// 【模式】§7.2：本关 XML players="1"，但关卡 16–33 属 2P 面板（双人热座），实际模式以菜单按钮为准。
        /// </summary>
        static readonly LevelData _level33 = new LevelData(
            levelNumber: 33,
            name: "level_33",
            widthTiles: 41,
            heightTiles: 33,
            originalXmlPlayers: 1,
            waterTileY: 30f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 3, // XML potentialWeapons maxChests="3"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：7 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 14, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 15, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 27, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 34, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 28, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 9, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 13, 27, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 19, 27, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                // 蓝队（team2）：7 bluePirate + 1 bluePirateCaptain
                new LevelUnit("bluePirate", 1, 26, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 13, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 25, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 6, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 14, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 32, 14, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("bluePirateCaptain", 1, 33, 27, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("bluePirate", 1, 27, 27, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                })
            });

    }
}
