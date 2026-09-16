using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界套件（WorldKit）可站立面转写表 —— GENERATED 文件，勿手改。
    ///
    /// 由 <c>tools/blender/scene/sync_standables.py</c> 从
    /// <c>Assets/Art/Models/WorldKit/&lt;Kit&gt;/&lt;Asset&gt;.standable.json</c> 生成
    /// （manifest 是建模脚本的直接产物，本表与它逐字同步）。
    ///
    /// 【box 语义】资产本地系（Y-up，米）：矩形中心 = <see cref="WorldStandBox.Center"/>（资产系），
    /// 矩形自身绕其中心转 <see cref="WorldStandBox.YawDeg"/>；顶面高度 = <see cref="WorldStandBox.TopY"/>
    /// （0.5 档）。摆放进地图时：世界矩形中心 = placement.Position + R(placement.Yaw)·Center，
    /// 总转角 = placement.Yaw + box.Yaw。
    /// 【坐标转换】Unity (X,Z) = (Blender X, −Blender Y)，TopY = Blender Z，Yaw 同值。
    /// </summary>
    public static class WorldMapStandables
    {
        /// <summary>该资产是否有站面数据（false = 纯视觉件）。</summary>
        public static bool Has(string asset)
        {
            return Table.ContainsKey(asset);
        }

        /// <summary>取资产站面 box 表；未收录返回 null（调用方按纯视觉处理）。</summary>
        public static IReadOnlyList<WorldStandBox> BoxesOf(string asset)
        {
            return Table.TryGetValue(asset, out var boxes) ? boxes : null;
        }

        // ------------------------------------------------------------------
        // 各资产站面（按 kit 目录分组，组内按资产名排序）
        // ------------------------------------------------------------------
        // archipelago（tools/blender/scene/archipelago/）
        static readonly WorldStandBox[] _AtollArcA =
        {
            new WorldStandBox(-19f, 32.91f, 18.64f, 11.5f, 0.5f, -120f),
            new WorldStandBox(0f, 38f, 18.64f, 11.5f, 0.5f, -90f),
            new WorldStandBox(19f, 32.91f, 18.64f, 11.5f, 0.5f, -60f),
            new WorldStandBox(-17.84f, 33.55f, 8f, 5f, 1f, -28f),
            new WorldStandBox(17.84f, 33.55f, 8f, 5f, 1f, 28f),
        };

        static readonly WorldStandBox[] _AtollCore =
        {
            new WorldStandBox(0f, 0f, 13.6f, 9.6f, 0.5f),
            new WorldStandBox(4.4f, -3.4f, 4.8f, 4f, 1f),
            new WorldStandBox(-4.6f, 2.8f, 4.8f, 4f, 1f),
        };

        static readonly WorldStandBox[] _MangroveHummock =
        {
            new WorldStandBox(0f, 0f, 17.6f, 13.6f, 1f),
        };

        static readonly WorldStandBox[] _ReefStepsA =
        {
            new WorldStandBox(-8f, 0f, 5.7f, 4.7f, 0.5f),
            new WorldStandBox(0f, 0f, 5.7f, 4.7f, 1f),
            new WorldStandBox(8f, 0f, 5.7f, 4.7f, 1.5f),
        };

        static readonly WorldStandBox[] _SandBarL =
        {
            new WorldStandBox(0f, 0f, 29.6f, 7.6f, 0.5f),
        };

        static readonly WorldStandBox[] _SeaStackShort =
        {
            new WorldStandBox(0f, 0f, 6.6f, 6.6f, 2.5f),
        };

        static readonly WorldStandBox[] _SeaStackTall =
        {
            new WorldStandBox(0f, 0f, 7.6f, 7.6f, 6f),
        };

        static readonly WorldStandBox[] _SunkenPlaza =
        {
            new WorldStandBox(0f, 9.2f, 27.6f, 9.2f, 0.5f),
            new WorldStandBox(0f, -9.2f, 27.6f, 9.2f, 0.5f),
        };

        static readonly WorldStandBox[] _TerraceIslandL =
        {
            new WorldStandBox(0f, 0f, 47.6f, 35.6f, 0.5f),
            new WorldStandBox(-5f, -3f, 31.6f, 23.6f, 1.5f),
            new WorldStandBox(3f, 3.5f, 19.6f, 13.6f, 2.5f),
            new WorldStandBox(-1.5f, -2f, 10.6f, 7.2f, 3.5f),
        };

        static readonly WorldStandBox[] _TerraceIslandM =
        {
            new WorldStandBox(0f, 0f, 23.6f, 17.6f, 0.5f),
            new WorldStandBox(-2.8f, -1.8f, 14.6f, 10f, 1.5f),
            new WorldStandBox(1.6f, 1.2f, 7.6f, 5.2f, 2.5f),
        };

        static readonly WorldStandBox[] _TurtleShellIsle =
        {
            new WorldStandBox(0f, 0f, 39.6f, 31.6f, 1f),
            new WorldStandBox(0f, 0.6f, 27.6f, 20.6f, 2f),
            new WorldStandBox(0f, 0.8f, 15.6f, 11.6f, 3f),
            new WorldStandBox(18.5f, -8.5f, 5f, 4f, 1f, 45f),
            new WorldStandBox(-18.5f, -8.5f, 5f, 4f, 1f, 135f),
            new WorldStandBox(-18.5f, 8f, 5f, 4f, 1f, 225f),
            new WorldStandBox(18.5f, 8f, 5f, 4f, 1f, 315f),
            new WorldStandBox(0f, 19.5f, 6f, 6f, 2f),
            new WorldStandBox(0f, -18f, 4f, 4f, 1f),
            new WorldStandBox(0f, 0.8f, 12f, 4.4f, 3.5f),
        };

        static readonly WorldStandBox[] _VolcanoRimA =
        {
            new WorldStandBox(-9.46f, -24.22f, 9.6f, 11.5f, 4.5f, 111.33f),
            new WorldStandBox(-18.06f, -18.7f, 9.6f, 11.5f, 4.5f, 134f),
            new WorldStandBox(-23.87f, -10.3f, 9.6f, 11.5f, 4.5f, 156.67f),
            new WorldStandBox(-23.5f, 11.12f, 7.81f, 11.5f, 4.5f, 205.33f),
            new WorldStandBox(-18.7f, 18.06f, 7.81f, 11.5f, 4.5f, 224f),
            new WorldStandBox(-11.94f, 23.1f, 7.81f, 11.5f, 4.5f, 242.67f),
            new WorldStandBox(13.52f, 22.21f, 7.81f, 11.5f, 4.5f, 301.33f),
            new WorldStandBox(19.92f, 16.71f, 7.81f, 11.5f, 4.5f, 320f),
            new WorldStandBox(24.22f, 9.46f, 7.81f, 11.5f, 4.5f, 338.67f),
            new WorldStandBox(25.8f, -3.19f, 11.74f, 11.5f, 3.5f, 7.05f),
            new WorldStandBox(21.41f, -14.74f, 11.74f, 11.5f, 3.5f, 34.55f),
            new WorldStandBox(12.19f, -22.97f, 11.74f, 11.5f, 3.5f, 62.05f),
            new WorldStandBox(0.2f, -26f, 11.74f, 11.5f, 3.5f, 89.55f),
            new WorldStandBox(-25.73f, 3.75f, 12.84f, 11.5f, 3.5f, 188.3f),
            new WorldStandBox(0f, 0f, 27.6f, 27.6f, 0.5f),
        };


        // marine（tools/blender/scene/marine/）
        static readonly WorldStandBox[] _LighthouseTower =
        {
            new WorldStandBox(0f, 0f, 11.4f, 11.4f, 0.5f),
            new WorldStandBox(0f, 0f, 4f, 4f, 4f),
            new WorldStandBox(0f, 0f, 3.5f, 3.5f, 7.5f),
        };

        static readonly WorldStandBox[] _MastBridge =
        {
            new WorldStandBox(0f, 4.4f, 1.6f, 7.1f, 1.5f),
            new WorldStandBox(0f, -4.4f, 1.6f, 7.1f, 1.5f),
            new WorldStandBox(1.35f, -2.2f, 3f, 3f, 4.5f),
        };

        static readonly WorldStandBox[] _PierHead =
        {
            new WorldStandBox(0f, 0f, 7.7f, 7.7f, 1f),
        };

        static readonly WorldStandBox[] _PierLong =
        {
            new WorldStandBox(0f, 0f, 4f, 17.6f, 1f),
        };

        static readonly WorldStandBox[] _WreckBowHalf =
        {
            new WorldStandBox(0f, -4.25f, 5.02f, 4f, 0.5f),
            new WorldStandBox(0f, 0.1f, 5.03f, 4.5f, 1f),
            new WorldStandBox(0f, 4.04f, 3.12f, 3.18f, 1.5f),
        };

        static readonly WorldStandBox[] _WreckSternHalf =
        {
            new WorldStandBox(0f, 1.9f, 4.4f, 6.8f, 0.5f),
            new WorldStandBox(0f, -3.82f, 4f, 4f, 1f),
        };


        static readonly Dictionary<string, WorldStandBox[]> Table =
            new Dictionary<string, WorldStandBox[]>
            {
                { "AtollArcA", _AtollArcA },
                { "AtollCore", _AtollCore },
                { "MangroveHummock", _MangroveHummock },
                { "ReefStepsA", _ReefStepsA },
                { "SandBarL", _SandBarL },
                { "SeaStackShort", _SeaStackShort },
                { "SeaStackTall", _SeaStackTall },
                { "SunkenPlaza", _SunkenPlaza },
                { "TerraceIslandL", _TerraceIslandL },
                { "TerraceIslandM", _TerraceIslandM },
                { "TurtleShellIsle", _TurtleShellIsle },
                { "VolcanoRimA", _VolcanoRimA },
                { "LighthouseTower", _LighthouseTower },
                { "MastBridge", _MastBridge },
                { "PierHead", _PierHead },
                { "PierLong", _PierLong },
                { "WreckBowHalf", _WreckBowHalf },
                { "WreckSternHalf", _WreckSternHalf },
            };

    }
}
