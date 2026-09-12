using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 一个出战单位的战场计划条目（纯 C#，不引用 MonoBehaviour）。
    /// 由 <see cref="LevelGeometry.BuildBattlePlan(LevelData, float)"/> 从关卡数据生成，
    /// 供 <c>BattleController</c> 实例化 <c>PirateBase</c>。
    /// </summary>
    public readonly struct SpawnPlanEntry
    {
        /// <summary>队伍索引：0 = 红队（team1 / 玩家侧），1 = 蓝队（team2）。对应 §4.3。</summary>
        public readonly int TeamIndex;

        /// <summary>原版导出符号名（§4.2，如 redPirate / cabinBoyCaptain）。</summary>
        public readonly string TypeName;

        /// <summary>该单位的 luck（§4.1，AI 随机投掷次数基数）。</summary>
        public readonly int Luck;

        /// <summary>原版 XML 瓦片格 x（§4.3）。</summary>
        public readonly int GridX;

        /// <summary>原版 XML 瓦片格 y（§4.3）。</summary>
        public readonly int GridY;

        /// <summary>Unity 世界坐标（见 <see cref="LevelGeometry.GridToWorld"/>；xy 为战斗平面，z 为深度）。</summary>
        public readonly Vector3 WorldPosition;

        /// <summary>初始武器栈（§5.5；count == 10 表示无限）。可能为空列表，绝不 null。</summary>
        public readonly IReadOnlyList<WeaponStack> InitialWeapons;

        public SpawnPlanEntry(
            int teamIndex, string typeName, int luck, int gridX, int gridY,
            Vector3 worldPosition, IReadOnlyList<WeaponStack> initialWeapons)
        {
            TeamIndex = teamIndex;
            TypeName = typeName;
            Luck = luck;
            GridX = gridX;
            GridY = gridY;
            WorldPosition = worldPosition;
            InitialWeapons = initialWeapons ?? EmptyWeapons;
        }

        static readonly WeaponStack[] EmptyWeapons = new WeaponStack[0];
    }

    /// <summary>
    /// 一场战斗的组装计划（纯 C#）：关卡尺寸、水位与全部出战单位。
    /// </summary>
    public sealed class BattlePlan
    {
        /// <summary>关卡序号（1–33）。</summary>
        public readonly int LevelNumber;

        /// <summary>关卡宽度（瓦片；本工程 1 瓦片 = 1 世界单位）。</summary>
        public readonly int WidthTiles;

        /// <summary>关卡高度（瓦片）。</summary>
        public readonly int HeightTiles;

        /// <summary>原版 XML players 属性（§7.2；运行时模式由菜单/序列化开关决定）。</summary>
        public readonly int OriginalXmlPlayers;

        /// <summary>水面世界 Y（Unity 约定，y 向上；低于该值即落水，§4.4）。</summary>
        public readonly float WaterWorldY;

        /// <summary>关卡世界宽度（单位）。</summary>
        public readonly float WorldWidth;

        /// <summary>关卡世界高度（单位）。</summary>
        public readonly float WorldHeight;

        readonly IReadOnlyList<SpawnPlanEntry> _entries;

        public BattlePlan(
            int levelNumber, int widthTiles, int heightTiles, int originalXmlPlayers,
            float waterWorldY, IReadOnlyList<SpawnPlanEntry> entries)
        {
            LevelNumber = levelNumber;
            WidthTiles = widthTiles;
            HeightTiles = heightTiles;
            OriginalXmlPlayers = originalXmlPlayers;
            WaterWorldY = waterWorldY;
            // 1 瓦片 = 1 世界单位（见类头的 px→单位换算决策）。
            WorldWidth = widthTiles;
            WorldHeight = heightTiles;
            _entries = entries ?? new List<SpawnPlanEntry>();
        }

        /// <summary>全部出战单位（顺序 = 关卡数据里的出现顺序）。</summary>
        public IReadOnlyList<SpawnPlanEntry> Entries => _entries;

        /// <summary>指定队伍（teamIndex 0/1）的出战单位数量。</summary>
        public int CountForTeam(int teamIndex)
        {
            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].TeamIndex == teamIndex)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// 战斗坐标/尺度换算与出战计划生成（纯 C# 静态工具）。
    ///
    /// 【对应章节】§4.3（坐标换算 px = (xmlX+0.5)*32、py = (xmlY+0.5)*32+16-bottomExtent）、
    ///             §5.5（water.y = waterTileY*32）、§5.1/§5.4（速度与重力口径）、§8.1（相机范围）。
    ///
    /// 【3D 化决策 1：px→单位换算】
    ///   原版 1 瓦片 = 32px。本工程定 <b>1 瓦片 = 1 Unity 单位</b>（即 1 单位 = 32px），
    ///   常量集中在 <see cref="PixelsPerUnit"/>，全工程单一来源。
    ///
    /// 【3D 化决策 2：y 轴方向】
    ///   Flash 的 y 轴向下（重力每帧 +weight）；Unity 的 y 轴向上。
    ///   映射取 <c>world = (px / 32, -py / 32, z)</c>：显式翻转 y，且战斗平面为世界 XY 平面（z = 深度）。
    ///   这样 Flash 里"越大越靠下"变成 Unity 里"越小越靠下"，符合 y 向上约定。
    ///
    /// 【3D 化决策 3：重力与力度换算】
    ///   Flash 每帧 vy += weight（25fps，1px/帧²）。本工程让 Unity 物理与 Ballistics 同源：
    ///   1) 把 <see cref="Time.fixedDeltaTime"/> 设为 <see cref="FrameSeconds"/>（0.04s，25Hz），
    ///      Rigidbody 用 <c>useGravity</c> + <c>Physics.gravity = WorldGravity(1)</c>；
    ///   2) 初速换算 <see cref="FlashVelocityToWorld"/>：v_world = v_flash / (32 * 0.04)；
    ///   3) 重力换算 <see cref="WorldGravityY"/>：g_world = -weight / (32 * 0.04²)。
    ///   由于 Flash 的离散积分是 vy += w; y += vy、PhysX 的半隐式欧拉是 v += g·dt; p += v·dt，
    ///   在 dt 相同时二者逐步完全一致 → <b>预览 = 实弹</b>（详见测试
    ///   <c>LevelGeometryTests.Ballistics_And_WorldSemiImplicit_Match_Exactly</c>）。
    ///   weight != 1 的武器（§5.2 仅 boulder = 1.5、cannonball = 0）由实弹脚本自行处理
    ///   （boulder 关 useGravity 后自定义加速度；cannonball 直接 useGravity = false）。
    ///   预览与实弹共用 <see cref="Ballistics.TwangVelocity"/> 的输出，不存在第二份速度口径。
    ///
    /// 【3D 化决策 4：落水即死】
    ///   §4.1/§4.3：原版每关都有 water 对象，落水即死是<b>全局规则</b>。
    ///   Unity 取全局：<see cref="IsBelowWater"/>，由 BattleController 每帧对所有存活角色判定，
    ///   不设"仅某关开启"的开关（Godot 版只在 shipwreck_cove 开启属漂移）。
    /// </summary>
    public static class LevelGeometry
    {
        /// <summary>1 瓦片 = 32px（原版瓦片尺寸，§5.4 的 <c>&gt;&gt;5</c>）。</summary>
        public const float PixelsPerUnit = 32f;

        /// <summary>原版帧率 25fps（§1）。</summary>
        public const float FrameRate = 25f;

        /// <summary>原版一帧的秒数（1/25 = 0.04s）。</summary>
        public const float FrameSeconds = 1f / FrameRate;

        /// <summary>战斗平面的世界 z（本工程 x/y 为战斗平面，z 仅用于表现深度）。</summary>
        public const float DefaultPlaneZ = 0f;

        /// <summary>
        /// Flash 速度（px/帧）→ 世界速度（单位/秒）的比例：
        /// <c>1 / (PixelsPerUnit * FrameSeconds) = 1 / 1.28 = 0.78125</c>。
        /// </summary>
        public const float FlashSpeedScale = 1f / (PixelsPerUnit * FrameSeconds);

        /// <summary>§3.4 选中/拖拽的隐式阈值：30px（<c>minD2 = 900</c>）。</summary>
        public const float SelectionRadiusPixels = 30f;

        /// <summary>30px 对应的世界距离（30 / 32 = 0.9375 单位）。</summary>
        public const float SelectionRadiusWorld = SelectionRadiusPixels / PixelsPerUnit;

        // ------------------------------------------------------------------
        // px ↔ 单位
        // ------------------------------------------------------------------

        /// <summary>像素 → 世界单位（1 单位 = 32px）。</summary>
        public static float PixelsToUnits(float px)
        {
            return px / PixelsPerUnit;
        }

        /// <summary>世界单位 → 像素。</summary>
        public static float UnitsToPixels(float units)
        {
            return units * PixelsPerUnit;
        }

        // ------------------------------------------------------------------
        // 逻辑坐标 ↔ 世界坐标（y 轴翻转）
        // ------------------------------------------------------------------

        /// <summary>
        /// Flash 逻辑像素坐标 (px, py) → Unity 世界坐标 (px/32, -py/32, planeZ)。
        /// y 取负实现"Flash y 向下 → Unity y 向上"的翻转。
        /// </summary>
        public static Vector3 PixelToWorld(float pixelX, float pixelY, float planeZ = DefaultPlaneZ)
        {
            return new Vector3(pixelX / PixelsPerUnit, -pixelY / PixelsPerUnit, planeZ);
        }

        /// <summary>
        /// 关卡 XML 瓦片格坐标 → 世界坐标（§4.3 完整换算，bottomExtent 用 §4.1 的 8）。
        /// px = (gridX+0.5)*32；py = (gridY+0.5)*32 + 16 - bottomExtent。
        /// </summary>
        public static Vector3 GridToWorld(int gridX, int gridY, float planeZ = DefaultPlaneZ)
        {
            float px = LevelCatalog.ToPixelX(gridX);
            float py = LevelCatalog.ToPixelY(gridY, CrewCatalog.BottomExtent);
            return PixelToWorld(px, py, planeZ);
        }

        /// <summary>世界坐标 → Flash 逻辑像素坐标 (px, -世界y*32)。</summary>
        public static Vector2 WorldToPixel(Vector3 world)
        {
            return new Vector2(world.x * PixelsPerUnit, -world.y * PixelsPerUnit);
        }

        // ------------------------------------------------------------------
        // 水面（§5.5 / §4.4）
        // ------------------------------------------------------------------

        /// <summary>水面像素 y → 世界 Y（取负，见 y 轴翻转决策）。</summary>
        public static float WaterWorldY(float waterPixelY)
        {
            return -waterPixelY / PixelsPerUnit;
        }

        /// <summary>世界坐标是否已在水平面之下（§4.4 落水即死；全局规则）。</summary>
        public static bool IsBelowWater(float worldY, float waterWorldY)
        {
            return worldY < waterWorldY;
        }

        // ------------------------------------------------------------------
        // 速度 / 重力换算（§5.1 / §5.4）
        // ------------------------------------------------------------------

        /// <summary>
        /// Flash 初速（px/帧）→ Unity 世界速度（单位/秒）。
        /// vx 同向、vy 取负（y 翻转）。不含重力的离散补偿项——
        /// 因为本工程令 Unity 物理帧率 = 原版帧率，半隐式欧拉逐步等价，无需补偿（见类头说明）。
        /// </summary>
        public static Vector3 FlashVelocityToWorld(float vxPixelsPerFrame, float vyPixelsPerFrame)
        {
            return new Vector3(
                vxPixelsPerFrame * FlashSpeedScale,
                -vyPixelsPerFrame * FlashSpeedScale,
                0f);
        }

        /// <summary>
        /// Unity 世界速度（单位/秒）→ Flash 速度（px/帧）；是
        /// <see cref="FlashVelocityToWorld"/> 的逆变换（x 同向、y 取负、除以 <see cref="FlashSpeedScale"/>）。
        /// 供弹体运行时判静止（§5.2 dynamite 的 <c>vx==0 &amp;&amp; |vy|&lt;0.2</c>）等回读场景。
        /// </summary>
        public static Vector2 WorldVelocityToFlash(Vector3 worldVelocity)
        {
            return new Vector2(
                worldVelocity.x / FlashSpeedScale,
                -worldVelocity.y / FlashSpeedScale);
        }

        /// <summary>
        /// 速度"增量"（爆炸击退等，§5.3）从 Flash 约定翻到 Unity：
        /// 与 <see cref="FlashVelocityToWorld"/> 相同的缩放 + y 取负。
        ///
        /// ★ 关键：<see cref="ExplosionResolver"/> 的 <c>deltaVy = ny*5k - 6k</c> 是在 Flash
        ///   y 向下约定下算出的，其中 <c>-6k</c> 表示<b>向上</b>。经本函数 y 取负后，
        ///   Unity 里的值变为正（+Y 向上），语义正确衔接。
        /// </summary>
        public static Vector3 FlashVelocityDeltaToWorld(float deltaVxPixelsPerFrame, float deltaVyPixelsPerFrame)
        {
            return new Vector3(
                deltaVxPixelsPerFrame * FlashSpeedScale,
                -deltaVyPixelsPerFrame * FlashSpeedScale,
                0f);
        }

        /// <summary>
        /// Flash 重力加速度（weight px/帧²）→ Unity 世界重力 Y（单位/秒²，向下为负）：
        /// <c>-weight / (32 * 0.04²) = -19.53125 * weight</c>。
        /// </summary>
        public static float WorldGravityY(float weight)
        {
            return -weight / (PixelsPerUnit * FrameSeconds * FrameSeconds);
        }

        /// <summary>Flash 重力加速度 → Unity 世界重力向量 (0, g, 0)。</summary>
        public static Vector3 WorldGravity(float weight)
        {
            return new Vector3(0f, WorldGravityY(weight), 0f);
        }

        // ------------------------------------------------------------------
        // 出战计划
        // ------------------------------------------------------------------

        static readonly IReadOnlyList<WeaponStack> EmptyWeapons = new WeaponStack[0];

        /// <summary>由纯 C# 关卡数据 <see cref="LevelData"/> 生成出战计划（无头可测路径）。</summary>
        public static BattlePlan BuildBattlePlan(LevelData data, float planeZ = DefaultPlaneZ)
        {
            IReadOnlyList<LevelUnit> units = data.Units ?? new List<LevelUnit>();
            var entries = new List<SpawnPlanEntry>(units.Count);

            for (int i = 0; i < units.Count; i++)
            {
                LevelUnit unit = units[i];
                entries.Add(new SpawnPlanEntry(
                    unit.teamIndex,
                    unit.typeName,
                    unit.luck,
                    unit.gridX,
                    unit.gridY,
                    GridToWorld(unit.gridX, unit.gridY, planeZ),
                    unit.initialWeapons));
            }

            return new BattlePlan(
                data.LevelNumber,
                data.WidthTiles,
                data.HeightTiles,
                data.OriginalXmlPlayers,
                WaterWorldY(data.WaterY),
                entries);
        }

        /// <summary>由 Unity 关卡资产 <see cref="LevelDefinition"/> 生成出战计划（运行时路径）。</summary>
        public static BattlePlan BuildBattlePlan(LevelDefinition definition, float planeZ = DefaultPlaneZ)
        {
            IReadOnlyList<LevelUnit> units = definition.Units ?? new List<LevelUnit>();
            var entries = new List<SpawnPlanEntry>(units.Count);

            for (int i = 0; i < units.Count; i++)
            {
                LevelUnit unit = units[i];
                entries.Add(new SpawnPlanEntry(
                    unit.teamIndex,
                    unit.typeName,
                    unit.luck,
                    unit.gridX,
                    unit.gridY,
                    GridToWorld(unit.gridX, unit.gridY, planeZ),
                    unit.initialWeapons));
            }

            return new BattlePlan(
                definition.LevelNumber,
                definition.WidthTiles,
                definition.HeightTiles,
                definition.OriginalXmlPlayers,
                WaterWorldY(definition.WaterY),
                entries);
        }

        /// <summary>空武器列表（供 WeaponInventory / BattlePlan 复用，避免分配）。</summary>
        public static IReadOnlyList<WeaponStack> NoWeapons => EmptyWeapons;
    }
}
