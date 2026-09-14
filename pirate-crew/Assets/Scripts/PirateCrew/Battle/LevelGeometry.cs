using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
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

        /// <summary>原版 XML 瓦片格 x（§4.3）→ 竞技场横向。</summary>
        public readonly int GridX;

        /// <summary>原版 XML 瓦片格 y（§4.3）→ 竞技场**纵深**（3D 重投影，见 LevelGeometry 类头）。</summary>
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

        /// <summary>原版 XML players 属性（§7.2；运行时模式由菜单/序列化开关决定）。</summary>
        public readonly int OriginalXmlPlayers;

        /// <summary>水面世界 Y（Unity 约定，y 向上；低于该值即落水，§4.4）。全局常量，见 LevelGeometry。</summary>
        public readonly float WaterWorldY;

        /// <summary>竞技场世界宽度（X 方向，单位）。</summary>
        public readonly float WorldWidth;

        /// <summary>竞技场世界纵深（Z 方向，单位）。</summary>
        public readonly float WorldDepth;

        readonly IReadOnlyList<SpawnPlanEntry> _entries;

        public BattlePlan(
            int levelNumber, int widthTiles, int depthTiles, int originalXmlPlayers,
            float waterWorldY, IReadOnlyList<SpawnPlanEntry> entries)
        {
            LevelNumber = levelNumber;
            WidthTiles = widthTiles;
            DepthTiles = depthTiles;
            OriginalXmlPlayers = originalXmlPlayers;
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
    /// 【本文件是空间模型的唯一权威】完整契约见 <c>docs/M2-3D空间模型对齐.md</c>，改前必读。
    ///
    /// 【坐标模型（3D 重投影，2026-09-13 修正）】
    ///   原第一版把 Flash 的 2D 侧视坐标 1:1 搬进 Unity 的 XY **竖直**平面、并冻结 Z，
    ///   结果是"披着 3D 引擎的 2D 游戏"，与 Godot 基准（真 3D）不符。现行模型：
    ///     · 竞技场 = <b>XZ 水平面</b>；重力沿 <b>-Y</b>；地面顶面 y = 0，水面 y = <see cref="WaterSurfaceY"/>。
    ///     · Flash 的 px（横向）→ 世界 X；Flash 的 py（原侧视的"竖直"轴）→ 世界 <b>Z（纵深）</b>。
    ///       即把原版 2D 关卡当作**平面图**重新投影，`gridY` 从"平台高度"变成"纵深位置"。
    ///     · **y 不取负**：侧视图里"越往下"= 越靠近观众；平面图里"越靠近观众"= +Z（相机在 +Z 侧），
    ///       方向天然一致，故 <c>PixelToArena</c> 直接映射而不翻转。
    ///   这一改动使 3D 空间结构对齐 Godot 基准（其 battle.tscn 为 XZ 地面 + 45° 相机 + 单位沿 Z 分路），
    ///   而**数值仍全部取自 Flash 逆向文档**（Godot 版弹道/伤害层是自相矛盾的占位实现，见上述文档）。
    ///
    /// 【3D 化决策 1：px→单位换算】原版 1 瓦片 = 32px。本工程定 <b>1 瓦片 = 2 Unity 单位</b>
    ///   （用户裁决 2026-09-14：格子世界尺寸 1→2 单位，岛 4-8 格 = 8-16 单位才够厚重；
    ///   **角色自身尺寸 1.85 不随格变化**），即
    ///   常量集中在 <see cref="TilePixels"/> / <see cref="TileWorldSize"/>，
    ///   <see cref="PixelsPerUnit"/>（= 16，1 单位 = 16px）由两者相除得到。
    ///   <b>凡是"由 Flash px / 格语义换算成世界单位"的量都必须走这里的换算</b>：
    ///   · 距离类（选中半径、爆炸半径、投掷射程、水距）→ <see cref="PixelsToUnits"/> / <see cref="TileToWorld"/>；
    ///   · 速度/重力类 → <see cref="FlashSpeedScale"/> / <see cref="WorldGravityY"/>（随 PixelsPerUnit 缩放）；
    ///   · **格号 → 世界坐标** → <see cref="GridToArena"/> / <see cref="TileToWorld"/> / <see cref="TileCenterWorld"/>；
    ///   · 反向（世界 → 格号）→ <see cref="WorldToTiles"/> / <see cref="PixelsToTiles"/>（**不是** PixelsToUnits）。
    ///   格语义（射程多少格、水距多少格）在本次扫荡里逐项不变——只有"一格有几个世界单位"变了。
    ///
    /// 【3D 化决策 2：重力与力度换算】Flash 每帧 vy += weight（25fps，1px/帧²）。
    ///   本工程让 Unity 物理与 Ballistics 同源：
    ///   1) <see cref="Time.fixedDeltaTime"/> = <see cref="FrameSeconds"/>（0.04s，25Hz），
    ///      Rigidbody 用 <c>useGravity</c> + <c>Physics.gravity = WorldGravity(1)</c>；
    ///   2) 初速换算 <see cref="FlashVelocityToArena"/>：v_world = v_flash / (16 * 0.04)；
    ///   3) 重力换算 <see cref="WorldGravityY"/>：g_world = -weight / (16 * 0.04²) = -39.0625·weight。
    ///   由于 Flash 的离散积分是 vy += w; y += vy、PhysX 的半隐式欧拉是 v += g·dt; p += v·dt，
    ///   在 dt 相同时二者逐步完全一致 → <b>预览 = 实弹</b>（见测试
    ///   <c>LevelGeometryTests.Ballistics_And_WorldSemiImplicit_Match_Exactly</c>）。
    ///   【格 1→2 单位后弹道为什么逐点不变】Flash 侧 (vx, vy, weight) 是**像素口径**，一个字都没改；
    ///   变的只是 px→世界的除数（32→16），于是整条抛物线在**格空间里逐点相同**、在世界里整体乘 2。
    ///   这条不变量是刻意保留的：Godot 版恰好坏在这里（预览与实弹速度差 18 倍、重力 -18 vs -9.8），
    ///   对齐空间结构时**不要**把它的数值一起搬进来。
    ///
    /// 【3D 化决策 3：投掷抬升】Flash 的拖拽竖直分量直接给 vy（2D 里就是"抛多高"）。
    ///   3D 里拖拽只表达"水平往哪扔"，仰角由固定抬升 <see cref="ThrowLift"/> 提供
    ///   （Godot 预览用 0.7、实弹用 0.8，本工程统一取 0.7 单一常量），
    ///   速度**大小**仍由 Flash 的 twangMax 限速决定 —— 限速语义不被抬升破坏。
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
        /// 水面世界 Y。落水即死（§4.4）的判据基准；对齐 Godot 基准的水位（其水面在地面下方一点）。
        /// 比地面低 <b>0.4 单位</b>（= 6.4px；格 1→2 单位后 px 语义不变，故由 0.2 乘 2），
        /// 角色掉出地面后下落约 0.9 单位即判定落水。
        /// </summary>
        public const float WaterSurfaceY = -0.4f;

        /// <summary>
        /// 投掷的固定抬升系数：<c>throwDir = normalize(水平方向 + UP * ThrowLift)</c>。
        /// 对齐 Godot 的 0.7（其预览用 0.7、实弹用 0.8，本工程统一为单一常量以保"预览 = 实弹"）。
        /// </summary>
        public const float ThrowLift = 0.7f;

        /// <summary>
        /// Flash 速度（px/帧）→ 世界速度（单位/秒）的比例：
        /// <c>1 / (PixelsPerUnit * FrameSeconds) = 1 / 0.64 = 1.5625</c>（格 1→2 单位后 PixelsPerUnit=16）。
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

        /// <summary>格号（含小数）→ 像素；<see cref="PixelsToTiles"/> 的逆。</summary>
        public static float TilesToPixels(float tiles)
        {
            return tiles * TilePixels;
        }

        /// <summary>格号（含小数）→ 世界单位（× <see cref="TileWorldSize"/>）。</summary>
        public static float TileToWorld(float tiles)
        {
            return tiles * TileWorldSize;
        }

        /// <summary>世界单位 → 格号（含小数）；<see cref="TileToWorld"/> 的逆。</summary>
        public static float WorldToTiles(float units)
        {
            return units / TileWorldSize;
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
        /// 关卡 XML 瓦片格坐标 → 单位站位世界坐标（§4.3）。
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
        /// Flash 水平初速（px/帧，平面内 (vx, vy)）→ Unity 世界速度（单位/秒）：
        /// 平面分量落到 (X, Z)，高度分量恒为 0（仰角由 <see cref="ApplyThrowLift"/> 提供）。
        /// </summary>
        public static Vector3 FlashVelocityToArena(float vxPixelsPerFrame, float vyPixelsPerFrame)
        {
            return new Vector3(
                vxPixelsPerFrame * FlashSpeedScale,
                0f,
                vyPixelsPerFrame * FlashSpeedScale);
        }

        /// <summary>
        /// Unity 世界速度（单位/秒）→ Flash 平面速度（px/帧）；<see cref="FlashVelocityToArena"/> 的逆变换。
        /// 供弹体运行时判静止（§5.2 dynamite 的 <c>vx==0 &amp;&amp; |vy|&lt;0.2</c>）等回读场景。
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

        /// <summary>
        /// 平面方向 + 固定抬升 → 3D 投掷方向（已归一化）。零向量返回 <see cref="Vector3.zero"/>。
        /// </summary>
        public static Vector3 ApplyThrowLift(Vector3 horizontalDirection)
        {
            Vector3 flat = new Vector3(horizontalDirection.x, 0f, horizontalDirection.z);
            if (flat.sqrMagnitude < 1e-12f)
                return Vector3.zero;

            return (flat.normalized + Vector3.up * ThrowLift).normalized;
        }

        /// <summary>
        /// 投掷初速：平面方向由 <see cref="ApplyThrowLift"/> 定仰角，**大小**由 Flash 的
        /// twang 结果（px/帧）决定 —— 抬升只改方向、不改速度大小，从而不破坏 twangMax 限速语义。
        /// </summary>
        public static Vector3 ThrowVelocity(Vector3 horizontalDirection, float speedPixelsPerFrame)
        {
            Vector3 dir = ApplyThrowLift(horizontalDirection);
            if (dir == Vector3.zero)
                return Vector3.zero;

            return dir * (speedPixelsPerFrame * FlashSpeedScale);
        }

        /// <summary>
        /// 由相机基向量把屏幕拖拽 (dx, dy) 映射成世界水平方向（已归一化，y = 0）。
        /// <c>horiz = camRight.xz * dx + camForward.xz * dy</c>（直接瞄准：拖向哪扔向哪）。
        /// 相机 yaw 变化时映射随基向量旋转，预览与实弹永远同口径。
        /// </summary>
        public static Vector3 ScreenDragToArenaDirection(Vector3 cameraRight, Vector3 cameraForward, float dragX, float dragY)
        {
            Vector3 right = new Vector3(cameraRight.x, 0f, cameraRight.z);
            Vector3 forward = new Vector3(cameraForward.x, 0f, cameraForward.z);

            // 【r12 用户裁决"拖向哪扔向哪"】玩家输入采用**直接瞄准**语义（拖拽方向=投掷方向），
            // 不再做 Flash 弹弓取反（旧口径实测第一次上手就反，直觉性优先于原版输入习惯）；
            // 预览与实弹共用本函数，口径仍唯一。
            Vector3 horiz = right * dragX + forward * dragY;
            if (horiz.sqrMagnitude < 1e-12f)
                return Vector3.zero;

            return horiz.normalized;
        }

        /// <summary>
        /// <b>投掷/发射的统一入口</b>：Flash 平面初速 (vx, vy)（px/帧）→ 3D 世界初速（含抬升）。
        /// 速度**大小**仍由 Flash 的 twang 结果（已含 twangMax 限速）决定，抬升只改仰角。
        /// 弹体（<c>ProjectileSpawnPlanner</c>）与角色（<c>PirateBase</c>）都走这里，
        /// 保证两者弹道口径与预览完全一致。
        /// </summary>
        public static Vector3 FlashLaunchVelocityToWorld(float vxPixelsPerFrame, float vyPixelsPerFrame)
        {
            float speed = Mathf.Sqrt(
                vxPixelsPerFrame * vxPixelsPerFrame + vyPixelsPerFrame * vyPixelsPerFrame);
            if (speed <= 1e-6f)
                return Vector3.zero;

            return ThrowVelocity(new Vector3(vxPixelsPerFrame, 0f, vyPixelsPerFrame), speed);
        }

        /// <summary>
        /// Flash 重力加速度（weight px/帧²）→ Unity 世界重力 Y（单位/秒²，向下为负）：
        /// <c>-weight / (16 * 0.04²) = -39.0625 * weight</c>（格 1→2 单位后由 -19.53125 乘 2）。
        /// 重力沿 -Y（垂直向下），与水平面正交。
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

        /// <summary>由纯 C# 关卡数据 <see cref="LevelData"/> 生成出战计划（无头可测路径）。</summary>
        public static BattlePlan BuildBattlePlan(LevelData data)
        {
            return BuildPlan(
                data.LevelNumber, data.WidthTiles, data.HeightTiles, data.OriginalXmlPlayers,
                data.Units);
        }

        /// <summary>由 Unity 关卡资产 <see cref="LevelDefinition"/> 生成出战计划（运行时路径）。</summary>
        public static BattlePlan BuildBattlePlan(LevelDefinition definition)
        {
            return BuildPlan(
                definition.LevelNumber, definition.WidthTiles, definition.HeightTiles,
                definition.OriginalXmlPlayers, definition.Units);
        }

        static BattlePlan BuildPlan(
            int levelNumber, int widthTiles, int depthTiles, int originalXmlPlayers,
            IReadOnlyList<LevelUnit> units)
        {
            units = units ?? new List<LevelUnit>();
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
                    GridToArena(unit.gridX, unit.gridY),
                    unit.initialWeapons));
            }

            return new BattlePlan(
                levelNumber, widthTiles, depthTiles, originalXmlPlayers,
                WaterSurfaceY, entries);
        }

        /// <summary>空武器列表（供 WeaponInventory / BattlePlan 复用，避免分配）。</summary>
        public static IReadOnlyList<WeaponStack> NoWeapons => EmptyWeapons;
    }
}
