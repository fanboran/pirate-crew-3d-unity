using System;
using System.Collections.Generic;

namespace PirateCrew.Data
{
    /// <summary>
    /// 单场战斗的数据快照（纯 C#）。世界海域图与样板三关共用这一载体：
    /// 出战计划（<c>LevelGeometry.BuildBattlePlan</c>）只认它，不关心数据来自
    /// <c>WorldMapCatalog</c> 的手写海图还是 <c>ShowcaseLevels</c> 的手写样板。
    ///
    /// 【坐标字段说明】<see cref="Units"/> 里的 <c>gridX/gridY</c> 是**逻辑格坐标**
    ///（1 格 = <c>LevelGeometry.TileWorldSize</c> 世界单位），运行时世界坐标由
    /// <c>LevelGeometry.WorldPosition</c> 换算。<see cref="WaterTileY"/> 是逻辑水面行，
    /// <see cref="WaterY"/> 是换算后的运行时像素值（<c>WaterTileY * 32</c>）。
    /// </summary>
    public readonly struct LevelData
    {
        /// <summary>本场战斗的序号（世界图 101–108 / 手作样板关 1、3——关卡 2 已删除，号段有意不连续）。</summary>
        public readonly int LevelNumber;

        /// <summary>战斗名（选关页/结算展示用）。</summary>
        public readonly string Name;

        /// <summary>场地宽度（逻辑格）。</summary>
        public readonly int WidthTiles;

        /// <summary>场地高度（逻辑格）。</summary>
        public readonly int HeightTiles;

        /// <summary>逻辑水面行。</summary>
        public readonly float WaterTileY;

        /// <summary>运行时水面 Y（px）= WaterTileY * 32。</summary>
        public readonly float WaterY;

        /// <summary>空投武器池。</summary>
        public readonly IReadOnlyList<WeaponStack> PotentialWeapons;

        /// <summary>双方出战单位。</summary>
        public readonly IReadOnlyList<LevelUnit> Units;

        public LevelData(
            int levelNumber,
            string name,
            int widthTiles,
            int heightTiles,
            float waterTileY,
            IReadOnlyList<WeaponStack> potentialWeapons,
            IReadOnlyList<LevelUnit> units)
        {
            LevelNumber = levelNumber;
            Name = name;
            WidthTiles = widthTiles;
            HeightTiles = heightTiles;
            WaterTileY = waterTileY;
            WaterY = waterTileY * 32f;               // Controller.water.y = y * 32
            PotentialWeapons = potentialWeapons;
            Units = units;
        }
    }

    /// <summary>
    /// 场地内的单个出战单位（对应关卡资产里的一个出战单位条目）。
    ///
    /// 【口径】坐标、luck 与初始武器栈为本工程设计值。
    /// </summary>
    [Serializable]
    public struct LevelUnit
    {
        /// <summary>导出符号名（如 redPirate / bossGuy）。</summary>
        public string typeName;

        /// <summary>队伍索引：0=红队(team1)，1=蓝队(team2)。</summary>
        public int teamIndex;

        /// <summary>逻辑格 x（运行时 px = (gridX+0.5)*32）。</summary>
        public int gridX;

        /// <summary>逻辑格 y（运行时 py = (gridY+0.5)*32 + 16 - bottomExtent）。</summary>
        public int gridY;

        /// <summary>该单位的 luck（覆盖默认 5，AI 随机投掷次数基数）。</summary>
        public int luck;

        /// <summary>该单位的初始武器栈（count=10 表示无限）。</summary>
        public List<WeaponStack> initialWeapons;

        public LevelUnit(string typeName, int teamIndex, int gridX, int gridY, int luck, List<WeaponStack> initialWeapons)
        {
            this.typeName = typeName;
            this.teamIndex = teamIndex;
            this.gridX = gridX;
            this.gridY = gridY;
            this.luck = luck;
            this.initialWeapons = initialWeapons ?? new List<WeaponStack>();
        }
    }
}
