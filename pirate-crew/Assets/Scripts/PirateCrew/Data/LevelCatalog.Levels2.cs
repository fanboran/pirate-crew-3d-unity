using System.Collections.Generic;

namespace PirateCrew.PirateCrew.Data
{
    /// <summary>
    /// <see cref="LevelCatalog"/> 的分部文件：level_2–level_15（level_4 见主文件 LevelCatalog.cs）
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
        // level_2：47×27 瓦片，XML players=1，水面 tile y=26，空投池 3 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_2（47×27，与 §7.2 尺寸表一致）。
        /// 红队（team1）7 人：6 redPirate + 1 redPirateCaptain；蓝队（team2）7 人：7 squid。
        /// </summary>
        static readonly LevelData _level2 = new LevelData(
            levelNumber: 2,
            name: "level_2",
            widthTiles: 47,
            heightTiles: 27,
            originalXmlPlayers: 1,
            waterTileY: 26f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                new WeaponStack(WeaponId.ParachuteBomb, 10), // XML parachuteBomb="10" → ∞
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：6 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 5, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 38, 8, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 21, 3, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("redPirateCaptain", 0, 6, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 2), // XML anchor="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 21, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 44, 18, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 24, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                }),
                // 蓝队（team2）：7 squid
                new LevelUnit("squid", 1, 10, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                }),
                new LevelUnit("squid", 1, 25, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                }),
                new LevelUnit("squid", 1, 42, 4, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                }),
                new LevelUnit("squid", 1, 6, 3, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                }),
                new LevelUnit("squid", 1, 34, 20, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                }),
                new LevelUnit("squid", 1, 13, 20, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                }),
                new LevelUnit("squid", 1, 32, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_3：40×23 瓦片，XML players=1，水面 tile y=20，空投池 1 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_3（40×23，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）4 人：3 blindPirate + 1 blindPirateCaptain。
        /// </summary>
        static readonly LevelData _level3 = new LevelData(
            levelNumber: 3,
            name: "level_3",
            widthTiles: 40,
            heightTiles: 23,
            originalXmlPlayers: 1,
            waterTileY: 20f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons maxChests="1"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 8, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 12, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 5, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 9, 6, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 12, 16, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                // 蓝队（team2）：3 blindPirate + 1 blindPirateCaptain
                new LevelUnit("blindPirate", 1, 32, 17, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("blindPirate", 1, 29, 11, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("blindPirate", 1, 34, 11, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("blindPirateCaptain", 1, 31, 6, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_5：44×20 瓦片，XML players=1，水面 tile y=17，空投池 1 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_5（44×20，与 §7.2 尺寸表一致）。
        /// 红队（team1）4 人：3 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：5 monkey。
        /// </summary>
        static readonly LevelData _level5 = new LevelData(
            levelNumber: 5,
            name: "level_5",
            widthTiles: 44,
            heightTiles: 20,
            originalXmlPlayers: 1,
            waterTileY: 17f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons maxChests="1"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：3 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 30, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                }),
                new LevelUnit("redPirateCaptain", 0, 23, 6, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 12, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 34, 13, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                }),
                // 蓝队（team2）：5 monkey
                new LevelUnit("monkey", 1, 40, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                }),
                new LevelUnit("monkey", 1, 2, 12, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                }),
                new LevelUnit("monkey", 1, 13, 2, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                }),
                new LevelUnit("monkey", 1, 7, 7, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                }),
                new LevelUnit("monkey", 1, 34, 6, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_6：115×28 瓦片，XML players=1，水面 tile y=25，空投池 5 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_6（115×28，与 §7.2 尺寸表一致）。
        /// 红队（team1）9 人：8 redPirate + 1 redPirateCaptain；蓝队（team2）9 人：8 oldPirate + 1 oldPirateCaptain。
        /// </summary>
        static readonly LevelData _level6 = new LevelData(
            levelNumber: 6,
            name: "level_6",
            widthTiles: 115,
            heightTiles: 28,
            originalXmlPlayers: 1,
            waterTileY: 25f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons maxChests="1"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：8 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 59, 17, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 84, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 83, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 71, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 72, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 70, 5, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 8, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 7, 22, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 55, 11, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                // 蓝队（team2）：8 oldPirate + 1 oldPirateCaptain
                new LevelUnit("oldPirate", 1, 51, 17, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirate", 1, 109, 21, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirate", 1, 108, 21, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirate", 1, 50, 5, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirate", 1, 51, 5, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirate", 1, 52, 5, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirateCaptain", 1, 21, 22, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirate", 1, 22, 22, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                }),
                new LevelUnit("oldPirate", 1, 61, 11, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_7：24×12 瓦片，XML players=1，水面 tile y=9，空投池 2 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_7（24×12，与 §7.2 尺寸表一致）。
        /// 红队（team1）2 人：1 redPirate + 1 redPirateCaptain；蓝队（team2）1 人：1 bossGuy。
        /// </summary>
        static readonly LevelData _level7 = new LevelData(
            levelNumber: 7,
            name: "level_7",
            widthTiles: 24,
            heightTiles: 12,
            originalXmlPlayers: 1,
            waterTileY: 9f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons maxChests="1"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 9), // XML cherryBomb="9" → 9 件
                new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：1 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 0, 6, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("redPirateCaptain", 0, 2, 6, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                // 蓝队（team2）：1 bossGuy
                new LevelUnit("bossGuy", 1, 19, 6, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 4), // XML cherryBomb="4" → 4 件
                    new WeaponStack(WeaponId.Boulder, 4), // XML boulder="4" → 4 件
                    new WeaponStack(WeaponId.Dynamite, 4), // XML dynamite="4" → 4 件
                    new WeaponStack(WeaponId.PiecesOfEight, 4), // XML piecesOfEight="4" → 4 件
                    new WeaponStack(WeaponId.RumBottle, 4), // XML rumBottle="4" → 4 件
                    new WeaponStack(WeaponId.Banana, 4), // XML banana="4" → 4 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                })
            });

        // ------------------------------------------------------------------
        // level_8：20×35 瓦片，XML players=1，水面 tile y=31，空投池 15 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_8（20×35，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：5 parrot。
        /// </summary>
        static readonly LevelData _level8 = new LevelData(
            levelNumber: 8,
            name: "level_8",
            widthTiles: 20,
            heightTiles: 35,
            originalXmlPlayers: 1,
            waterTileY: 31f,
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
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 11, 21, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 11, 17, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 11, 13, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 11, 9, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 11, 25, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 3), // XML rumBottle="3" → 3 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                // 蓝队（team2）：5 parrot
                new LevelUnit("parrot", 1, 7, 21, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 4), // XML cherryBomb="4" → 4 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("parrot", 1, 7, 17, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 4), // XML cherryBomb="4" → 4 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("parrot", 1, 7, 13, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 4), // XML cherryBomb="4" → 4 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("parrot", 1, 7, 9, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 4), // XML cherryBomb="4" → 4 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("parrot", 1, 7, 25, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 4), // XML cherryBomb="4" → 4 件
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 5), // XML banana="5" → 5 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 1), // XML gunpowderBarrel="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_9：59×25 瓦片，XML players=1，水面 tile y=24，空投池 11 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_9（59×25，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）6 人：5 rainbowBeard + 1 rainbowBeardCaptain。
        /// </summary>
        static readonly LevelData _level9 = new LevelData(
            levelNumber: 9,
            name: "level_9",
            widthTiles: 59,
            heightTiles: 25,
            originalXmlPlayers: 1,
            waterTileY: 24f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                new WeaponStack(WeaponId.PiecesOfEight, 2), // XML piecesOfEight="2" → 2 件
                new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 52, 20, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                }),
                new LevelUnit("redPirateCaptain", 0, 18, 15, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 40, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 8, 16, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                }),
                new LevelUnit("redPirate", 0, 41, 21, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                }),
                // 蓝队（team2）：5 rainbowBeard + 1 rainbowBeardCaptain
                new LevelUnit("rainbowBeard", 1, 25, 1, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 10), // XML piecesOfEight="10" → ∞
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("rainbowBeard", 1, 53, 20, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("rainbowBeard", 1, 30, 15, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("rainbowBeardCaptain", 1, 27, 10, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 2), // XML piecesOfEight="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                    new WeaponStack(WeaponId.Mine, 5), // XML mine="5" → 5 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 5), // XML tidalWave="5" → 5 件
                }),
                new LevelUnit("rainbowBeard", 1, 41, 16, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("rainbowBeard", 1, 9, 21, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 5), // XML woodenCrate="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_10：77×32 瓦片，XML players=1，水面 tile y=29，空投池 13 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_10（77×32，与 §7.2 尺寸表一致）。
        /// 红队（team1）7 人：6 redPirate + 1 redPirateCaptain；蓝队（team2）7 人：6 femalePirate + 1 femalePirateCaptain。
        /// </summary>
        static readonly LevelData _level10 = new LevelData(
            levelNumber: 10,
            name: "level_10",
            widthTiles: 77,
            heightTiles: 32,
            originalXmlPlayers: 1,
            waterTileY: 29f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons maxChests="1"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                new WeaponStack(WeaponId.PiecesOfEight, 5), // XML piecesOfEight="5" → 5 件
                new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                new WeaponStack(WeaponId.Anchor, 5), // XML anchor="5" → 5 件
                new WeaponStack(WeaponId.VoodooDoll, 5), // XML voodooDoll="5" → 5 件
                new WeaponStack(WeaponId.TidalWave, 5), // XML tidalWave="5" → 5 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：6 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 37, 5, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 15, 20, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 66, 11, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 29, 24, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 7, 14, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 74, 23, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 46, 23, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 10), // XML mine="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                }),
                // 蓝队（team2）：6 femalePirate + 1 femalePirateCaptain
                new LevelUnit("femalePirate", 1, 0, 26, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                }),
                new LevelUnit("femalePirateCaptain", 1, 40, 9, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                }),
                new LevelUnit("femalePirate", 1, 72, 16, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 2), // XML piecesOfEight="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("femalePirate", 1, 62, 20, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.PiecesOfEight, 2), // XML piecesOfEight="2" → 2 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("femalePirate", 1, 31, 20, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("femalePirate", 1, 45, 20, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("femalePirate", 1, 12, 14, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                })
            });

        // ------------------------------------------------------------------
        // level_11：44×16 瓦片，XML players=1，水面 tile y=13，空投池 6 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_11（44×16，与 §7.2 尺寸表一致）。
        /// 红队（team1）4 人：1 redPirateCaptain + 3 redPirate；蓝队（team2）4 人：4 crab。
        /// </summary>
        static readonly LevelData _level11 = new LevelData(
            levelNumber: 11,
            name: "level_11",
            widthTiles: 44,
            heightTiles: 16,
            originalXmlPlayers: 1,
            waterTileY: 13f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 3, // XML potentialWeapons maxChests="3"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                new WeaponStack(WeaponId.Dynamite, 8), // XML dynamite="8" → 8 件
                new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                new WeaponStack(WeaponId.Anchor, 2), // XML anchor="2" → 2 件
                new WeaponStack(WeaponId.TidalWave, 2), // XML tidalWave="2" → 2 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：1 redPirateCaptain + 3 redPirate
                new LevelUnit("redPirateCaptain", 0, 27, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 21, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 6, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 37, 10, 1, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                }),
                // 蓝队（team2）：4 crab
                new LevelUnit("crab", 1, 38, 5, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                }),
                new LevelUnit("crab", 1, 5, 5, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                }),
                new LevelUnit("crab", 1, 28, 1, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                }),
                new LevelUnit("crab", 1, 16, 1, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_12：44×26 瓦片，XML players=1，水面 tile y=23，空投池 7 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_12（44×26，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）11 人：10 skeletonPirate + 1 skeletonPirateCaptain。
        /// </summary>
        static readonly LevelData _level12 = new LevelData(
            levelNumber: 12,
            name: "level_12",
            widthTiles: 44,
            heightTiles: 26,
            originalXmlPlayers: 1,
            waterTileY: 23f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 3, // XML potentialWeapons maxChests="3"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Dynamite, 4), // XML dynamite="4" → 4 件
                new WeaponStack(WeaponId.RumBottle, 6), // XML rumBottle="6" → 6 件
                new WeaponStack(WeaponId.Banana, 4), // XML banana="4" → 4 件
                new WeaponStack(WeaponId.ParachuteBomb, 3), // XML parachuteBomb="3" → 3 件
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.Anchor, 2), // XML anchor="2" → 2 件
                new WeaponStack(WeaponId.VoodooDoll, 1), // XML voodooDoll="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 28, 17, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 2, 6, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 41, 3, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                }),
                new LevelUnit("redPirate", 0, 7, 15, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                }),
                new LevelUnit("redPirateCaptain", 0, 20, 15, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                }),
                // 蓝队（team2）：10 skeletonPirate + 1 skeletonPirateCaptain
                new LevelUnit("skeletonPirate", 1, 11, 5, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 34, 0, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 23, 21, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 39, 8, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 32, 10, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 12, 10, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 19, 6, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirateCaptain", 1, 23, 6, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 5), // XML dynamite="5" → 5 件
                    new WeaponStack(WeaponId.ParachuteBomb, 5), // XML parachuteBomb="5" → 5 件
                    new WeaponStack(WeaponId.GunpowderBarrel, 5), // XML gunpowderBarrel="5" → 5 件
                    new WeaponStack(WeaponId.Cannon, 5), // XML cannon="5" → 5 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                }),
                new LevelUnit("skeletonPirate", 1, 36, 15, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 5, 15, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                }),
                new LevelUnit("skeletonPirate", 1, 24, 15, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_13：90×7 瓦片，XML players=1，水面 tile y=4，空投池 15 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_13（90×7，与 §7.2 尺寸表一致）。
        /// 红队（team1）5 人：4 redPirate + 1 redPirateCaptain；蓝队（team2）5 人：5 shark。
        /// </summary>
        static readonly LevelData _level13 = new LevelData(
            levelNumber: 13,
            name: "level_13",
            widthTiles: 90,
            heightTiles: 7,
            originalXmlPlayers: 1,
            waterTileY: 4f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 3, // XML potentialWeapons maxChests="3"
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                new WeaponStack(WeaponId.Dynamite, 2), // XML dynamite="2" → 2 件
                new WeaponStack(WeaponId.PiecesOfEight, 2), // XML piecesOfEight="2" → 2 件
                new WeaponStack(WeaponId.RumBottle, 2), // XML rumBottle="2" → 2 件
                new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                new WeaponStack(WeaponId.WoodenCrate, 2), // XML woodenCrate="2" → 2 件
                new WeaponStack(WeaponId.GunpowderBarrel, 2), // XML gunpowderBarrel="2" → 2 件
                new WeaponStack(WeaponId.Seagull, 2), // XML seagull="2" → 2 件
                new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                new WeaponStack(WeaponId.Cannon, 2), // XML cannon="2" → 2 件
                new WeaponStack(WeaponId.Anchor, 2), // XML anchor="2" → 2 件
                new WeaponStack(WeaponId.VoodooDoll, 2), // XML voodooDoll="2" → 2 件
                new WeaponStack(WeaponId.TidalWave, 2), // XML tidalWave="2" → 2 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：4 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 67, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 41, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 3, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 28, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 50, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                // 蓝队（team2）：5 shark
                new LevelUnit("shark", 1, 86, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 4), // XML dynamite="4" → 4 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 4), // XML rumBottle="4" → 4 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("shark", 1, 57, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 4), // XML dynamite="4" → 4 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 4), // XML rumBottle="4" → 4 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("shark", 1, 14, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 4), // XML dynamite="4" → 4 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 4), // XML rumBottle="4" → 4 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("shark", 1, 43, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 4), // XML dynamite="4" → 4 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 4), // XML rumBottle="4" → 4 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                }),
                new LevelUnit("shark", 1, 36, 2, 10, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 2), // XML cherryBomb="2" → 2 件
                    new WeaponStack(WeaponId.Boulder, 2), // XML boulder="2" → 2 件
                    new WeaponStack(WeaponId.Dynamite, 4), // XML dynamite="4" → 4 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 4), // XML rumBottle="4" → 4 件
                    new WeaponStack(WeaponId.Banana, 2), // XML banana="2" → 2 件
                    new WeaponStack(WeaponId.ParachuteBomb, 2), // XML parachuteBomb="2" → 2 件
                    new WeaponStack(WeaponId.WoodenCrate, 1), // XML woodenCrate="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 2), // XML mine="2" → 2 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                    new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
                })
            });

        // ------------------------------------------------------------------
        // level_14：48×31 瓦片，XML players=1，水面 tile y=28，空投池 3 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_14（48×31，与 §7.2 尺寸表一致）。
        /// 红队（team1）4 人：3 redPirate + 1 redPirateCaptain；蓝队（team2）6 人：5 tribe + 1 tribeChief。
        /// </summary>
        static readonly LevelData _level14 = new LevelData(
            levelNumber: 14,
            name: "level_14",
            widthTiles: 48,
            heightTiles: 31,
            originalXmlPlayers: 1,
            waterTileY: 28f,
            maxChests: DefaultMaxChests,
            sourceXmlMaxChests: 1, // XML potentialWeapons 无 maxChests → 沿用 level_27 缺省口径 1
            potentialWeapons: new List<WeaponStack>
            {
                new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                new WeaponStack(WeaponId.TidalWave, 1), // XML tidalWave="1" → 1 件
            },
            units: new List<LevelUnit>
            {
                // 红队（team1，玩家）：3 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 9, 14, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("redPirateCaptain", 0, 12, 10, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 18, 17, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 16, 24, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 10), // XML cherryBomb="10" → ∞
                    new WeaponStack(WeaponId.Boulder, 1), // XML boulder="1" → 1 件
                    new WeaponStack(WeaponId.Dynamite, 1), // XML dynamite="1" → 1 件
                    new WeaponStack(WeaponId.PiecesOfEight, 1), // XML piecesOfEight="1" → 1 件
                    new WeaponStack(WeaponId.RumBottle, 1), // XML rumBottle="1" → 1 件
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.ParachuteBomb, 1), // XML parachuteBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                    new WeaponStack(WeaponId.GunpowderBarrel, 10), // XML gunpowderBarrel="10" → ∞
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.Cannon, 1), // XML cannon="1" → 1 件
                    new WeaponStack(WeaponId.Anchor, 1), // XML anchor="1" → 1 件
                }),
                // 蓝队（team2）：5 tribe + 1 tribeChief
                new LevelUnit("tribe", 1, 27, 19, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("tribe", 1, 18, 11, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("tribe", 1, 38, 14, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("tribeChief", 1, 34, 8, 25, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.Seagull, 1), // XML seagull="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("tribe", 1, 27, 12, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Banana, 1), // XML banana="1" → 1 件
                    new WeaponStack(WeaponId.Mine, 1), // XML mine="1" → 1 件
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                }),
                new LevelUnit("tribe", 1, 33, 24, 20, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.VoodooDoll, 10), // XML voodooDoll="10" → ∞
                })
            });

        // ------------------------------------------------------------------
        // level_15：86×20 瓦片，XML players=1，水面 tile y=17，空投池 3 项
        // ------------------------------------------------------------------
        /// <summary>
        /// level_15（86×20，与 §7.2 尺寸表一致）。
        /// 红队（team1）7 人：6 redPirate + 1 redPirateCaptain；蓝队（team2）1 人：1 bossGuyZombie。
        /// </summary>
        static readonly LevelData _level15 = new LevelData(
            levelNumber: 15,
            name: "level_15",
            widthTiles: 86,
            heightTiles: 20,
            originalXmlPlayers: 1,
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
                // 红队（team1，玩家）：6 redPirate + 1 redPirateCaptain
                new LevelUnit("redPirate", 0, 20, 8, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                }),
                new LevelUnit("redPirate", 0, 65, 8, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                }),
                // XML 该 obj 无武器属性 → 初始武器为空（§5.5）
                new LevelUnit("redPirate", 0, 13, 11, 30, new List<WeaponStack>()),
                // XML 该 obj 无武器属性 → 初始武器为空（§5.5）
                new LevelUnit("redPirate", 0, 61, 12, 30, new List<WeaponStack>()),
                new LevelUnit("redPirate", 0, 24, 12, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("redPirateCaptain", 0, 29, 14, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.CherryBomb, 1), // XML cherryBomb="1" → 1 件
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                new LevelUnit("redPirate", 0, 53, 14, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.WoodenCrate, 10), // XML woodenCrate="10" → ∞
                }),
                // 蓝队（team2）：1 bossGuyZombie
                new LevelUnit("bossGuyZombie", 1, 43, 8, 30, new List<WeaponStack>
                {
                    new WeaponStack(WeaponId.Boulder, 10), // XML boulder="10" → ∞
                    new WeaponStack(WeaponId.Dynamite, 10), // XML dynamite="10" → ∞
                    new WeaponStack(WeaponId.PiecesOfEight, 10), // XML piecesOfEight="10" → ∞
                    new WeaponStack(WeaponId.RumBottle, 10), // XML rumBottle="10" → ∞
                    new WeaponStack(WeaponId.Banana, 10), // XML banana="10" → ∞
                    new WeaponStack(WeaponId.ParachuteBomb, 10), // XML parachuteBomb="10" → ∞
                    new WeaponStack(WeaponId.Seagull, 10), // XML seagull="10" → ∞
                    new WeaponStack(WeaponId.Cannon, 10), // XML cannon="10" → ∞
                    new WeaponStack(WeaponId.Anchor, 10), // XML anchor="10" → ∞
                    new WeaponStack(WeaponId.TidalWave, 10), // XML tidalWave="10" → ∞
                })
            });

    }
}
