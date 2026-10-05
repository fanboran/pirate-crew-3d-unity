using System.Collections.Generic;
using PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 一个出战单位的战场计划条目（纯 C#，不引用 MonoBehaviour）。
    /// 由 <see cref="LevelGeometry.BuildBattlePlan(LevelData)"/> 从关卡数据生成，
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

        /// <summary>地形采样格 X（由 <see cref="WorldPosition"/> 的 X 反推；只供 <c>HeightfieldGrid</c> 查询）。</summary>
        public readonly int GridX;

        /// <summary>地形采样格 Z（由 <see cref="WorldPosition"/> 的 Z 反推；只供 <c>HeightfieldGrid</c> 查询）。</summary>
        public readonly int GridY;

        /// <summary>
        /// Unity 世界坐标：脚底贴地、枢轴抬高 <see cref="LevelGeometry.UnitPivotHeight"/>。
        /// x = 横向、y = 高度、z = 纵深。
        /// </summary>
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
    /// 一场战斗的组装计划（纯 C#）：竞技场尺寸、水面高度与全部出战单位。
    /// </summary>
    public sealed class BattlePlan
    {
        /// <summary>关卡序号（1–33）。</summary>
        public readonly int LevelNumber;

        /// <summary>竞技场横向尺寸（瓦片）。世界尺寸 = 本值 × <see cref="TileWorldSize"/>。</summary>
        public readonly int WidthTiles;

        /// <summary>竞技场纵深尺寸（瓦片；来自原版关卡的 heightTiles）。</summary>
        public readonly int DepthTiles;

        /// <summary>水面世界 Y（Unity 约定，y 向上；低于该值即落水，§4.4）。全局常量，见 LevelGeometry。</summary>
        public readonly float WaterWorldY;

        /// <summary>竞技场世界宽度（X 方向，单位）。</summary>
        public readonly float WorldWidth;

        /// <summary>竞技场世界纵深（Z 方向，单位）。</summary>
        public readonly float WorldDepth;

        readonly IReadOnlyList<SpawnPlanEntry> _entries;

        public BattlePlan(
            int levelNumber, int widthTiles, int depthTiles,
            float waterWorldY, IReadOnlyList<SpawnPlanEntry> entries)
        {
            LevelNumber = levelNumber;
            WidthTiles = widthTiles;
            DepthTiles = depthTiles;
            WaterWorldY = waterWorldY;
            // 1 瓦片 = TileWorldSize 世界单位（见 LevelGeometry 类头的 px→单位换算决策）。
            WorldWidth = LevelGeometry.TileToWorld(widthTiles);
            WorldDepth = LevelGeometry.TileToWorld(depthTiles);
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
    /// 【本文件是空间模型的唯一权威】完整契约见 <c>docs/设计/3D空间模型对齐.md</c>，改前必读。
    ///
    /// 【坐标模型（3D 重投影，2026-09-13 修正）】
    ///   原第一版把 Flash 的 2D 侧视坐标 1:1 搬进 Unity 的 XY **竖直**平面、并冻结 Z，
    ///   结果是"披着 3D 引擎的 2D 游戏"。现行模型：
    ///     · 竞技场 = <b>XZ 水平面</b>；重力沿 <b>-Y</b>；地面顶面 y = 0，水面 y = <see cref="WaterSurfaceY"/>。
    ///     · Flash 的 px（横向）→ 世界 X；Flash 的 py（原侧视的"竖直"轴）→ 世界 <b>Z（纵深）</b>。
    ///       即把原版 2D 关卡当作**平面图**重新投影，`gridY` 从"平台高度"变成"纵深位置"。
    ///     · **y 不取负**：侧视图里"越往下"= 越靠近观众；平面图里"越靠近观众"= +Z（相机在 +Z 侧），
    ///       方向天然一致，故 <c>PixelToArena</c> 直接映射而不翻转。
    ///   这一改动使空间结构成为真 3D（XZ 地面 + 45° 相机 + 单位沿 Z 分路），
    ///   而**数值仍全部取自 Flash 逆向文档**（弹道/伤害层为自相矛盾的占位实现，不可作数值依据）。
    ///
    /// 【3D 化决策 1：px→单位换算】原版 1 瓦片 = 32px。本工程定 <b>1 瓦片 = 2 Unity 单位</b>
    ///   （用户裁决 2026-09-14：格子世界尺寸 1→2 单位，岛 4-8 格 = 8-16 单位才够厚重；
    ///   **角色自身尺寸 1.85 不随格变化**），即
    ///   常量集中在 <see cref="TilePixels"/> / <see cref="TileWorldSize"/>，
    ///   <see cref="PixelsPerUnit"/>（= 16，1 单位 = 16px）由两者相除得到。
    ///   <b>凡是"由 Flash px / 格语义换算成世界单位"的量都必须走这里的换算</b>：
    ///   · 距离类（选中半径、爆炸半径、水距）→ <see cref="PixelsToUnits"/> / <see cref="TileToWorld"/>；
    ///   · 速度增量类（击退/翻滚/下沉，武器数值域）→ <see cref="FlashSpeedScale"/>（随 PixelsPerUnit 缩放）；
    ///   · **格号 → 世界坐标** → <see cref="GridToArena"/> / <see cref="TileToWorld"/> / <see cref="TileCenterWorld"/>；
        ///   · 反向（世界 → 格号）→ <see cref="PixelsToTiles"/> / <see cref="WorldToTileIndex"/>（**不是** PixelsToUnits）。
    ///   格语义（射程多少格、水距多少格）在本次扫荡里逐项不变——只有"一格有几个世界单位"变了。
    ///
    /// 【投掷口径（米制重立，2026-10-03 创始人裁决）】投掷链的初速/重力/仰角不再走 Flash px 换算：
    ///   单源 <see cref="StandardThrowRules"/>（MaxLaunchSpeed=30 m/s、LaunchGravity=−30 m/s²、
    ///   三参数=方向角/仰角/力度）。本类只保留**武器/爆炸/翻滚数值域**的 px 换算
    ///   （FlashVelocityDeltaToArena / ArenaVelocityToFlash / FlashSpeedScale），随武器系统重做再迁米制。
    ///   物理步率 25 Hz（<see cref="FrameSeconds"/>）保留——回合/像素节奏，非 Flash 专属；
    ///   PhysX 半隐式欧拉与预览积分器同 dt 同重力 → <b>预览 = 实弹</b>（不变量沿用）。
    ///
    /// 【3D 化决策 4：落水即死】§4.1/§4.3：原版每关都有 water 对象，落水即死是<b>全局规则</b>。
    ///   Unity 取全局：<see cref="IsBelowWater"/>，由 BattleController 每帧对所有存活角色判定。
    ///   水面高度改为常量（不再由关卡 waterTileY 推出）——地图已平坦化为一块 XZ 地面，
    ///   从 X 或 Z 任一侧掉出地面都会落到水面以下，方向无关。
    /// </summary>
    public static class LevelGeometry
    {
        /// <summary>原版瓦片边长（px）——一个 Flash 关卡格恒为 32px（§5.4 的 <c>&gt;&gt;5</c>）。</summary>
        public const float TilePixels = 32f;

        /// <summary>
        /// 1 格的**世界尺寸**（Unity 单位）。用户裁决 2026-09-14：格子世界尺寸 1→2 单位
        /// （"岛 4-8 格 = 8-16 单位才够厚重"；角色自身尺寸 1.85 不乘）。
        /// <b>改这一个数即等于给全工程所有"格语义 → 世界单位"的量乘同一个系数</b>。
        /// </summary>
        public const float TileWorldSize = 2f;

        /// <summary>
        /// 1 世界单位对应多少 Flash 像素 = <see cref="TilePixels"/> / <see cref="TileWorldSize"/>
        /// = 32 / 2 = <b>16</b>（旧口径是 32）。所有 px ↔ 世界单位的换算的唯一来源。
        /// </summary>
        public const float PixelsPerUnit = TilePixels / TileWorldSize;

        /// <summary>原版帧率 25fps（§1）。</summary>
        public const float FrameRate = 25f;

        /// <summary>原版一帧的秒数（1/25 = 0.04s）。</summary>
        public const float FrameSeconds = 1f / FrameRate;

        /// <summary>地面顶面的世界 Y（竞技场平面）。</summary>
        public const float GroundTopY = 0f;

        /// <summary>
        /// 单个地形块的世界高度 = 8px = <b>0.5 单位</b>（可玩性优先的竖直压缩）。
        /// 全工程块高语义的单一来源：站位高度场（样板三关/世界图栅格）与视图层的方块尺寸都取它。
        /// </summary>
        public static float BlockWorldHeight => PixelsToUnits(8f);

        /// <summary>
        /// 水面世界 Y。落水即死（§4.4）的判据基准；低于地面顶面一点。
        /// 比地面低 <b>0.4 单位</b>（= 6.4px；格 1→2 单位后 px 语义不变，故由 0.2 乘 2），
        /// 角色掉出地面后下落约 0.9 单位即判定落水。
        /// </summary>
        public const float WaterSurfaceY = -0.4f;

        /// <summary>
        /// Flash 速度（px/帧）→ 世界速度（单位/秒）的比例：
        /// <c>1 / (PixelsPerUnit * FrameSeconds) = 1 / 0.64 = 1.5625</c>。
        /// 【口径归属】投掷链已随米制重立退役本比例（初速/重力单源 <see cref="StandardThrowRules"/>）；
        /// 现役消费方是**武器/爆炸/翻滚数值域**（击退增量、翻滚角速度、下沉速度——数值仍以 Flash px/帧
        /// 表达，随武器系统重做再迁米制），故保留。
        /// </summary>
        public const float FlashSpeedScale = 1f / (PixelsPerUnit * FrameSeconds);

        /// <summary>§3.4 选中/拖拽的隐式阈值：30px（<c>minD2 = 900</c>）。屏幕空间量，与维度、格大小无关。</summary>
        public const float SelectionRadiusPixels = 30f;

        /// <summary>30px 对应的世界距离（30 / 16 = 1.875 单位；格 1→2 单位后由 0.9375 乘 2）。</summary>
        public const float SelectionRadiusWorld = SelectionRadiusPixels / PixelsPerUnit;

        /// <summary>
        /// 单位枢轴离地高度：Flash 的 <c>py = (gridY+0.5)*32 + 16 - bottomExtent</c> 里那半格偏移
        /// （§4.3）。它是**格内偏移**（半格 = 16px），故随格世界尺寸一起放大：16px / 16px每单位
        /// = 0.5 单位（旧口径 0.25）。角色碰撞箱半高（RootScale 0.5→1.0 × BoxCollider 1）同为 0.5，
        /// 脚底因此贴地。
        /// </summary>
        public static float UnitPivotHeight => PixelsToUnits(16f - CrewCatalog.BottomExtent);

        // ------------------------------------------------------------------
        // px / 格 ↔ 单位
        // ------------------------------------------------------------------

        /// <summary>像素 → 世界单位（1 单位 = 16px）。</summary>
        public static float PixelsToUnits(float px)
        {
            return px / PixelsPerUnit;
        }

        /// <summary>世界单位 → 像素。</summary>
        public static float UnitsToPixels(float units)
        {
            return units * PixelsPerUnit;
        }

        /// <summary>
        /// 像素 → **格号**（栅格索引，**与格世界尺寸无关**；恒为 px / 32）。
        /// 与 <see cref="PixelsToUnits"/> 的区别是本轮扫荡的核心：格号语义不随"1 格几个单位"变，
        /// 凡是拿 px 去查 (gridX, gridZ) 的地方（AI 放置判定等）都必须用本函数。
        /// </summary>
        public static float PixelsToTiles(float px)
        {
            return px / TilePixels;
        }

        /// <summary>格号（含小数）→ 世界单位（× <see cref="TileWorldSize"/>）。</summary>
        public static float TileToWorld(float tiles)
        {
            return tiles * TileWorldSize;
        }

        /// <summary>
        /// 世界坐标 → **格号**（向下取整的栅格索引）。供地形查询（地表高度、水陆、簇归属）使用；
        /// 越界不报错，由调用方用 <c>grid.IndexOf</c> 判空。
        /// </summary>
        public static int WorldToTileIndex(float units)
        {
            return Mathf.FloorToInt(units / TileWorldSize);
        }

        /// <summary>格号 → 该格**中心**的世界坐标（XZ 平面；不含高度）。</summary>
        public static Vector2 TileCenterWorld(int gridX, int gridZ)
        {
            return new Vector2(TileToWorld(gridX + 0.5f), TileToWorld(gridZ + 0.5f));
        }

        // ------------------------------------------------------------------
        // 逻辑坐标 ↔ 世界坐标（XZ 竞技场平面）
        // ------------------------------------------------------------------

        /// <summary>
        /// Flash 逻辑像素 (px, py) → **地面平面**上的世界点 (px/16, <see cref="GroundTopY"/>, py/16)。
        /// 供瞄准落点、爆心等"平面位置"使用；**不**用于角色站位（那要加 <see cref="UnitPivotHeight"/>，
        /// 见 <see cref="GridToArena"/>）。
        /// </summary>
        public static Vector3 PixelToArena(float pixelX, float pixelY)
        {
            return new Vector3(pixelX / PixelsPerUnit, GroundTopY, pixelY / PixelsPerUnit);
        }

        /// <summary>
        /// 世界坐标 → Flash 平面像素 (x*16, z*16)。
        /// 注意：<b>忽略高度 y</b>——平面分量之外的高度由爆炸结算单独按 3D 距离处理
        /// （见 <c>ExplosionResolver</c> 的三维泛化）。
        /// </summary>
        public static Vector2 ArenaToPixel(Vector3 world)
        {
            return new Vector2(world.x * PixelsPerUnit, world.z * PixelsPerUnit);
        }

        /// <summary>
        /// 关卡栅格格坐标 → 单位站位世界坐标（§4.3）。
        /// 横向 X = (gridX + 0.5) × <see cref="TileWorldSize"/>；纵深 Z 同理；
        /// 高度 Y = 地面 + <see cref="UnitPivotHeight"/>（脚底贴地）。
        /// </summary>
        public static Vector3 GridToArena(int gridX, int gridY)
        {
            return new Vector3(
                TileToWorld(gridX + 0.5f),
                GroundTopY + UnitPivotHeight,
                TileToWorld(gridY + 0.5f));
        }

        // ------------------------------------------------------------------
        // 速度 / 重力换算（§5.1 / §5.4）
        // ------------------------------------------------------------------

        /// <summary>
        /// Unity 世界速度（单位/秒）→ Flash 平面速度（px/帧）。
        /// 供弹体运行时判静止（§5.2 dynamite 的 <c>vx==0 &amp;&amp; |vy|&lt;0.2</c>）等回读场景
        /// （武器数值域；投掷链已米制化，不走本函数）。
        /// </summary>
        public static Vector2 ArenaVelocityToFlash(Vector3 worldVelocity)
        {
            return new Vector2(
                worldVelocity.x / FlashSpeedScale,
                worldVelocity.z / FlashSpeedScale);
        }

        /// <summary>
        /// 速度"增量"（爆炸击退等，§5.3）从 Flash 平面约定翻到 Unity 的平面分量。
        /// 竖直抬升项由 <see cref="ExplosionResolver"/> 的三维泛化直接给出，不经本函数。
        /// </summary>
        public static Vector3 FlashVelocityDeltaToArena(float deltaVxPixelsPerFrame, float deltaVyPixelsPerFrame)
        {
            return new Vector3(
                deltaVxPixelsPerFrame * FlashSpeedScale,
                0f,
                deltaVyPixelsPerFrame * FlashSpeedScale);
        }

        // ------------------------------------------------------------------
        // 水面（§4.4）
        // ------------------------------------------------------------------

        /// <summary>世界坐标是否已在水平面之下（§4.4 落水即死；全局规则，方向无关）。</summary>
        public static bool IsBelowWater(float worldY, float waterWorldY)
        {
            return worldY < waterWorldY;
        }

        // ------------------------------------------------------------------
        // 出战计划
        // ------------------------------------------------------------------

        static readonly IReadOnlyList<WeaponStack> EmptyWeapons = new WeaponStack[0];

        /// <summary>
        /// 米尺寸 → 运行时地形采样格数（四舍五入）。
        /// 采样格只是运行时地形数据的粒度（<see cref="TileWorldSize"/> 米/格），不是对外概念；
        /// 资产与玩法数值一律用米。
        /// </summary>
        public static int CellCount(float meters)
        {
            return Mathf.RoundToInt(meters / TileWorldSize);
        }

        /// <summary>
        /// 由纯 C# 关卡数据 <see cref="LevelData"/> 生成出战计划（无头可测路径）。
        /// 数据已是全米口径：出生点直接用 <c>x/z</c>（世界 X / Z），不再做格→米换算；
        /// 采样格号（<see cref="SpawnPlanEntry.GridX"/>/<see cref="SpawnPlanEntry.GridY"/>）由米反推，
        /// 仅供运行时地形查询（<c>HeightfieldGrid.SurfaceWorldY</c>）使用。
        /// </summary>
        public static BattlePlan BuildBattlePlan(LevelData data)
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
                    WorldToTileIndex(unit.x),
                    WorldToTileIndex(unit.z),
                    new Vector3(unit.x, GroundTopY + UnitPivotHeight, unit.z),
                    unit.initialWeapons));
            }

            return new BattlePlan(
                data.LevelNumber, CellCount(data.SizeX), CellCount(data.SizeZ),
                data.WaterWorldY, entries);
        }

        /// <summary>空武器列表（供 WeaponInventory / BattlePlan 复用，避免分配）。</summary>
        public static IReadOnlyList<WeaponStack> NoWeapons => EmptyWeapons;
    }
}
