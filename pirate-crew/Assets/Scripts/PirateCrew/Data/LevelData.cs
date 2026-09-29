using System;
using System.Collections.Generic;

namespace PirateCrew.Data
{
    /// <summary>
    /// 单场战斗的数据快照（纯 C#）。世界海域图与样板关共用这一载体：
    /// 出战计划（<c>LevelGeometry.BuildBattlePlan</c>）只认它，不关心数据来自
    /// 海图资产还是关卡资产。
    ///
    /// 【坐标口径】一切数值**全米**：<see cref="SizeX"/> / <see cref="SizeZ"/> 是场地尺幅（米），
    /// <see cref="WaterWorldY"/> 是水面世界 Y（米，低于即落水），<see cref="Units"/> 里的
    /// <c>x/z</c> 是单位出生点的世界 X / 世界 Z（米，格心口径）。
    /// </summary>
    public readonly struct LevelData
    {
        /// <summary>本场战斗的序号（海图 101–108 / 手作样板关 1、3、4、5——关卡 2 已删除，号段有意不连续）。</summary>
        public readonly int LevelNumber;

        /// <summary>战斗名（选关页/结算展示用）。</summary>
        public readonly string Name;

        /// <summary>场地 X 尺幅（米）。</summary>
        public readonly float SizeX;

        /// <summary>场地 Z 尺幅（米）。</summary>
        public readonly float SizeZ;

        /// <summary>水面世界 Y（米；低于即落水，§4.4）。</summary>
        public readonly float WaterWorldY;

        /// <summary>空投武器池。</summary>
        public readonly IReadOnlyList<WeaponStack> PotentialWeapons;

        /// <summary>双方出战单位。</summary>
        public readonly IReadOnlyList<LevelUnit> Units;

        public LevelData(
            int levelNumber,
            string name,
            float sizeX,
            float sizeZ,
            float waterWorldY,
            IReadOnlyList<WeaponStack> potentialWeapons,
            IReadOnlyList<LevelUnit> units)
        {
            LevelNumber = levelNumber;
            Name = name;
            SizeX = sizeX;
            SizeZ = sizeZ;
            WaterWorldY = waterWorldY;
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

        /// <summary>出生点世界 X（米，格心口径）。</summary>
        public float x;

        /// <summary>出生点世界 Z（米，格心口径）。</summary>
        public float z;

        /// <summary>该单位的 luck（覆盖默认 5，AI 随机投掷次数基数）。</summary>
        public int luck;

        /// <summary>该单位的初始武器栈（count=10 表示无限）。</summary>
        public List<WeaponStack> initialWeapons;

        public LevelUnit(string typeName, int teamIndex, float x, float z, int luck, List<WeaponStack> initialWeapons)
        {
            this.typeName = typeName;
            this.teamIndex = teamIndex;
            this.x = x;
            this.z = z;
            this.luck = luck;
            this.initialWeapons = initialWeapons ?? new List<WeaponStack>();
        }
    }
}
