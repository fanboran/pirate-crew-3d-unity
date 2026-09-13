using System.Collections.Generic;
using System.Globalization;

namespace PirateCrew.PirateCrew.Data
{
    /// <summary>
    /// 原版瓦片语义族（<c>&lt;row&gt;</c> 行串里每个瓦片名的归类）。
    /// 全 33 关出现过的瓦片名**穷举**见 <see cref="LevelTileMaps"/> 类头的语义表；
    /// 未登记的名字落到 <see cref="Unknown"/>（<see cref="LevelTileMaps"/> 按"地面格"保守处理，
    /// 并由 <c>LevelTileMapsTests</c> 断言全 33 关里 Unknown 计数为 0 —— 即语义表必须穷举）。
    /// </summary>
    public enum LevelTileFamily
    {
        /// <summary>空（<c>-</c>）与水面波纹（<c>tile_ripple_*</c> / <c>boat_ripple_*</c>）——不可站、掉落即死。</summary>
        Water = 0,

        /// <summary>草地（<c>grass_*</c> / <c>single_grass_*</c>）——岛顶可站面。</summary>
        Grass = 1,

        /// <summary>土体（<c>earth_*</c>，含原版拼写错误 <c>eartyh_bottom_middle</c>）——岛体可站面。</summary>
        Earth = 2,

        /// <summary>沙洲（<c>blank_sand_*</c> / <c>shell_sand_*</c> / <c>sand_overlap_*</c>）——沙洲可站面。</summary>
        Sand = 3,

        /// <summary>船体（<c>ship_*</c> / <c>cannon_port_*</c>）——甲板 / 舷侧可站面。</summary>
        Ship = 4,

        /// <summary>桅与横桁（<c>mast_*</c>）——船的一部分，作可站面。</summary>
        Mast = 5,

        /// <summary>桅盘（<c>crows_nest_*</c>）——船的一部分，作可站面。</summary>
        CrowNest = 6,

        /// <summary>待定（语义表未登记的名字）——保守按地面格处理，并由测试断言其数量为 0。</summary>
        Unknown = 7,
    }

    /// <summary>
    /// 原版瓦片地图的**稀疏表**条目：一个非空格的 (gridX, 原版行号, 瓦片名, 语义族)。
    ///
    /// 【行号口径】<c>RowY</c> 是原版 <c>&lt;row&gt;</c> 的序号（0 = 关卡最上面一行 = 原版里最高的位置；
    /// 行号越大越靠下、越接近水面）。详见 <see cref="LevelTileMaps"/> 类头。
    /// </summary>
    public readonly struct LevelTileCell
    {
        /// <summary>原版瓦片格 x（= 世界 X 格号）。</summary>
        public readonly int X;

        /// <summary>原版行号（0 = 最上一行）。</summary>
        public readonly int RowY;

        /// <summary>原版瓦片名（如 <c>grass_top_left</c> / <c>tile_ripple_middle</c>）。</summary>
        public readonly string TileName;

        /// <summary>语义族。</summary>
        public readonly LevelTileFamily Family;

        public LevelTileCell(int x, int rowY, string tileName, LevelTileFamily family)
        {
            X = x;
            RowY = rowY;
            TileName = tileName;
            Family = family;
        }

        public override string ToString() => TileName + "@" + X + "," + RowY;
    }

    /// <summary>
    /// 一关的原版瓦片地图（由 <see cref="LevelTileMaps"/> 解析行串得到，纯 C#、无头可跑）。
    ///
    /// 【两套行号口径，务必分清】
    ///   · <b>原版行号 <c>rowY</c></b>：<c>&lt;row&gt;</c> 的序号，0 = 最上一行。所有 <c>*At(x, rowY)</c> 查询用它。
    ///   · <b>3D 纵深格 <c>gridZ</c></b>：<c>gridZ = rowY − 1</c>（见 <see cref="RowToGridZ"/>）。
    ///     之所以减 1：原版对象的 y 是"脚底所在行 − 1"，即单位真正站在 <c>(x, y+1)</c> 这一行上
    ///     （全 33 关 360 个单位实测 100% 如此）。减 1 之后单位的 <c>gridY</c> 恰好就是它脚下
    ///     地面格的 <c>gridZ</c>，于是 <c>TileTerrainGrid.SurfaceWorldY(unit.gridX, unit.gridY)</c>
    ///     与 <c>BattleController</c> 的落位口径（投影 <c>z = gridY + 0.5</c>）**同时**命中地面格。
    /// </summary>
    public sealed class LevelTileMapData
    {
        /// <summary>关卡号（1–33）。</summary>
        public readonly int LevelNumber;

        /// <summary>关卡宽度（瓦片数，= 原版 <c>width</c> 属性）。</summary>
        public readonly int Width;

        /// <summary>关卡高度（行数，= 原版 <c>height</c> 属性）。</summary>
        public readonly int Height;

        readonly string[] _tiles;   // 行主序 [x + rowY * Width]；"-" = 空

        /// <summary>稀疏表：全部非 <c>-</c> 格（含水面波纹）。</summary>
        public readonly IReadOnlyList<LevelTileCell> Cells;

        /// <summary>稀疏表：地面格（非 <c>-</c> 且非波纹）——即 3D 里的可站面。</summary>
        public readonly IReadOnlyList<LevelTileCell> SolidCells;

        /// <summary>稀疏表：水面格（<c>tile_ripple_*</c> / <c>boat_ripple_*</c>）。</summary>
        public readonly IReadOnlyList<LevelTileCell> RippleCells;

        /// <summary>非 <c>-</c> 格数（地面 + 波纹）。</summary>
        public readonly int NonDashCount;

        /// <summary>波纹格数（水）。</summary>
        public readonly int RippleCount;

        /// <summary>地面格数 = <see cref="NonDashCount"/> − <see cref="RippleCount"/>。</summary>
        public readonly int SolidCount;

        internal LevelTileMapData(int levelNumber, int width, int height, string[] tiles,
            List<LevelTileCell> cells, List<LevelTileCell> solid, List<LevelTileCell> ripple)
        {
            LevelNumber = levelNumber;
            Width = width;
            Height = height;
            _tiles = tiles;
            Cells = cells;
            SolidCells = solid;
            RippleCells = ripple;
            NonDashCount = cells.Count;
            RippleCount = ripple.Count;
            SolidCount = solid.Count;
        }

        /// <summary>该格瓦片名（按原版行号）；越界返回 <c>-</c>。</summary>
        public string TileAt(int x, int rowY)
        {
            if (x < 0 || rowY < 0 || x >= Width || rowY >= Height)
                return LevelTileMaps.EmptyTile;
            return _tiles[x + rowY * Width];
        }

        /// <summary>该格的语义族（按原版行号）；越界 = <see cref="LevelTileFamily.Water"/>。</summary>
        public LevelTileFamily FamilyAt(int x, int rowY)
        {
            return LevelTileMaps.FamilyOf(TileAt(x, rowY));
        }

        /// <summary>该格是否是地面（可站）；越界 = false。按**原版行号**。</summary>
        public bool IsSolidAt(int x, int rowY)
        {
            if (x < 0 || rowY < 0 || x >= Width || rowY >= Height)
                return false;
            return LevelTileMaps.IsSolidTile(_tiles[x + rowY * Width]);
        }

        /// <summary>原版行号 → 3D 纵深格号（<c>rowY − 1</c>，口径见类头）。</summary>
        public static int RowToGridZ(int rowY) => rowY - 1;

        /// <summary>3D 纵深格号 → 原版行号（<see cref="RowToGridZ"/> 的逆）。</summary>
        public static int GridZToRow(int gridZ) => gridZ + 1;

        /// <summary>
        /// 3D 网格格 (gx, gridZ) 是否是地面：等价于按原版行号查 <c>(gx, gridZ + 1)</c>。
        /// <c>BattleController</c> 的落位口径（投影 <c>z = gridY + 0.5</c>）与
        /// <c>TileTerrainGrid</c> 的地面查询都用这一套网格坐标。
        /// </summary>
        public bool IsSolidAtGrid(int gx, int gridZ) => IsSolidAt(gx, GridZToRow(gridZ));

        /// <summary>该列（原版 x）最上面一行地面格的行号；整列为空返回 -1。</summary>
        public int ColumnTopSolidRow(int x)
        {
            for (int rowY = 0; rowY < Height; rowY++)
            {
                if (IsSolidAt(x, rowY))
                    return rowY;
            }
            return -1;
        }
    }

    /// <summary>
    /// 全 33 关的**原版瓦片行串**（关卡 XML 的 <c>&lt;row&gt;</c> 内容）与解析器（纯 C#，无头可跑）。
    ///
    /// ==================================================================
    /// 【为什么把行串搬进 C#】
    /// ==================================================================
    /// 数据唯一来源是 <c>external/swf-decompile/levels_all.json</c>（33 关关卡 XML 全量逆向导出）。
    /// 但 <c>external/</c> 被 gitignore、不随包发布、运行时也读不到 —— 故把每关 <c>&lt;row&gt;</c> 原文
    /// **逐行原样**转写成 C# 常量（未做任何手工改数），解析器在运行时/无头验证台里复算。
    /// 与源数据的一致性由两道测试兜底：
    ///   · <c>Tests/Data/LevelTileMapsTests.cs</c> —— 语义表穷举、行串自洽、33 关落位断言；
    ///   · <c>Tests/Data/LevelTileMapsJsonParityTests.cs</c> —— 与 levels_all.json / levels_raw.txt
    ///     逐行串比对（读不到 <c>external/</c> 时 Skip，与 <c>LevelCatalogJsonParityTests</c> 同规矩）。
    ///
    /// ==================================================================
    /// 【行号口径（本项目最关键的约定，改前必读）】
    /// ==================================================================
    ///   · <c>&lt;row&gt;</c> 按**从上到下**排列：第 0 行是关卡最上面一行（原版里最高的位置），
    ///     行号越大越靠下、越接近水线；水面对象 <c>&lt;obj type="water" y="W"&gt;</c> 的 y 就是水线行
    ///     （level_1 水线行 = 14，而最后三行 14/15/16 正是 <c>tile_ripple_*</c> 海面波纹）。
    ///     反向读（把首行当海底）会让海面跑到关卡顶部，与 33 关的水面对象 y 全部对不上。
    ///   · 单位落位：<c>&lt;obj y="k"&gt;</c> 的单位站在 <c>(x, k+1)</c> 这一行上（脚底贴该行顶面）。
    ///     全 33 关 360 个单位按此口径实测 100% 命中非空格（见 <c>LevelTileMapsTests</c>）。
    ///   · 3D 纵深格 <c>gridZ = rowY − 1</c>（见 <see cref="LevelTileMapData.RowToGridZ"/>），
    ///     于是单位 (gridX, gridY) 的脚下地面格 = 3D 网格的 (gridX, gridY)。
    ///
    /// ==================================================================
    /// 【瓦片语义表（全 33 关穷举：76 个名字）】
    /// ==================================================================
    /// 按 **<c>&lt;row&gt;</c> 里的出现总次数** 列全。只在 <c>&lt;bgRow&gt;</c> 背景层出现的名字
    /// （<c>mast_top/mast_middle/mast_bottom/rock_*/cave_*/clam/antichest/...</c>）是背景装饰，
    /// **不作为可站面**，故不入表：
    ///
    ///   · <b>Water</b>（34101 格 / 8 个名字）：
    ///     <c>-</c>（30436）
    ///     <c>boat_ripple_1</c>（14）
    ///     <c>boat_ripple_2</c>（86）
    ///     <c>boat_ripple_3</c>（124）
    ///     <c>boat_ripple_4</c>（14）
    ///     <c>tile_ripple_left</c>（260）
    ///     <c>tile_ripple_middle</c>（2907）
    ///     <c>tile_ripple_right</c>（260）
    ///
    ///   · <b>Grass</b>（1399 格 / 15 个名字）：
    ///     <c>grass_overlap_both</c>（6）
    ///     <c>grass_overlap_left</c>（151）
    ///     <c>grass_overlap_left_edge</c>（4）
    ///     <c>grass_overlap_right</c>（149）
    ///     <c>grass_overlap_right_edge</c>（3）
    ///     <c>grass_row_left</c>（26）
    ///     <c>grass_row_middle</c>（38）
    ///     <c>grass_row_right</c>（26）
    ///     <c>grass_top_left</c>（269）
    ///     <c>grass_top_middle_1</c>（357）
    ///     <c>grass_top_middle_2</c>（77）
    ///     <c>grass_top_right</c>（270）
    ///     <c>grass_top_single</c>（13）
    ///     <c>single_grass_1</c>（2）
    ///     <c>single_grass_2</c>（8）
    ///
    ///   · <b>Earth</b>（4826 格 / 11 个名字）：
    ///     <c>earth_bottom_left</c>（139）
    ///     <c>earth_bottom_middle_2</c>（29）
    ///     <c>earth_bottom_middle_3</c>（25）
    ///     <c>earth_bottom_right</c>（159）
    ///     <c>earth_bottom_single</c>（13）
    ///     <c>earth_edge_left</c>（491）
    ///     <c>earth_edge_middle_1</c>（1543）
    ///     <c>earth_edge_middle_2</c>（1563）
    ///     <c>earth_edge_right</c>（511）
    ///     <c>earth_single_collum</c>（19）
    ///     <c>eartyh_bottom_middle</c>（334）
    ///
    ///   · <b>Sand</b>（951 格 / 10 个名字）：
    ///     <c>blank_sand_middle_1</c>（177）
    ///     <c>blank_sand_middle_2</c>（210）
    ///     <c>blank_sand_top_left</c>（76）
    ///     <c>blank_sand_top_right</c>（68）
    ///     <c>sand_overlap_left</c>（76）
    ///     <c>sand_overlap_right</c>（76）
    ///     <c>shell_sand_middle_1</c>（90）
    ///     <c>shell_sand_middle_2</c>（100）
    ///     <c>shell_sand_top_left</c>（36）
    ///     <c>shell_sand_top_right</c>（42）
    ///
    ///   · <b>Ship</b>（598 格 / 25 个名字）：
    ///     <c>cannon_port_1</c>（6）
    ///     <c>cannon_port_2</c>（140）
    ///     <c>cannon_port_3</c>（8）
    ///     <c>ship_anchor_1</c>（6）
    ///     <c>ship_anchor_2</c>（6）
    ///     <c>ship_anchor_3</c>（8）
    ///     <c>ship_anchor_4</c>（8）
    ///     <c>ship_end_1</c>（8）
    ///     <c>ship_end_2</c>（8）
    ///     <c>ship_end_3</c>（50）
    ///     <c>ship_left_facing_1</c>（6）
    ///     <c>ship_left_facing_2</c>（4）
    ///     <c>ship_left_facing_3</c>（6）
    ///     <c>ship_left_facing_5</c>（6）
    ///     <c>ship_left_side_shaded_4</c>（12）
    ///     <c>ship_right_facing_1</c>（8）
    ///     <c>ship_right_facing_2</c>（8）
    ///     <c>ship_right_facing_3</c>（8）
    ///     <c>ship_right_facing_3_1</c>（2）
    ///     <c>ship_right_facing_4</c>（8）
    ///     <c>ship_right_facing_4_1</c>（10）
    ///     <c>ship_right_side_shaded_2</c>（6）
    ///     <c>ship_tile_1</c>（106）
    ///     <c>ship_tile_2</c>（104）
    ///     <c>ship_top_middle</c>（56）
    ///
    ///   · <b>Mast</b>（332 格 / 5 个名字）：
    ///     <c>mast_end_left</c>（42）
    ///     <c>mast_end_right</c>（42）
    ///     <c>mast_middle_1</c>（36）
    ///     <c>mast_middle_2</c>（36）
    ///     <c>mast_tile</c>（176）
    ///
    ///   · <b>CrowNest</b>（44 格 / 2 个名字）：
    ///     <c>crows_nest_1</c>（22）
    ///     <c>crows_nest_2</c>（22）
    ///
    ///   · <b>Unknown</b>（0 格 / 0 个名字）：
    ///
    /// 语义表**穷举**由 <c>LevelTileMapsTests</c> 断言：33 关里 <see cref="LevelTileFamily.Unknown"/>
    /// 的格数必须为 0；拿不准的名字按"地面格"保守处理（宁可多一块陆地，不可凭空挖水）。
    ///
    /// ==================================================================
    /// 【关键结论（供地形构建使用）】
    /// ==================================================================
    ///   · 可站面 = 除 <c>-</c> 与波纹以外的全部瓦片（草地 / 土 / 沙洲 / 船体 / 桅 / 桅盘）；
    ///   · 水 = <c>-</c> 与 <c>tile_ripple_*</c> / <c>boat_ripple_*</c>。**波纹是海面不是可站面**：
    ///     原版踩上去就是落水；把它们当地面会让整片海变成一块可行走的"水地板"，
    ///     落水即死（§4.4）这条全局规则随之失效。
    ///   · 行号 → 高度（不是纵深！）：一列最上面的地面格（草地顶 / 甲板）离水线越高，
    ///     该岛在 3D 里浮得越高；纵深 Z 由连通域自身的行跨度给出 ——
    ///     见 <c>PlatformClusterLayout.BuildFromTileMap</c> 的推导注释。
    ///
    /// ==================================================================
    /// 【逐关签名（读原版 tile 后的"这一关的灵魂"，布局围绕它做）】
    /// ==================================================================
    /// 由生成脚本从行串统计得出，是**可核对的事实**：
    ///   岛数 / 船岛数 / 地面格数 / 岛顶行区间（水线行）→ 悬浮高度区块 / 出现的语汇。
    /// "悬浮高度" = round((水线行 − 顶行) × 1.25) 块（单块 0.25 世界单位），
    /// 只列区间端点，逐岛值见 <c>PlatformClusterLayout.BuildFromTileMap</c>。
    ///
    ///   · level_1（50×17，水线行 14）：9 岛 / 其中船岛 5 / 地面 163 格 / 岛顶行 4–11 → 悬浮高度 1–3 单位 / 语汇：船体、桅、桅盘、沙洲、草地、土体
    ///   · level_2（47×27，水线行 26）：19 岛 / 其中船岛 0 / 地面 111 格 / 岛顶行 4–23 → 悬浮高度 1–7 单位 / 语汇：草地、土体
    ///   · level_3（40×23，水线行 20）：9 岛 / 其中船岛 8 / 地面 165 格 / 岛顶行 7–17 → 悬浮高度 1–4 单位 / 语汇：船体、桅、桅盘、草地、土体
    ///   · level_4（56×18，水线行 17）：8 岛 / 其中船岛 8 / 地面 134 格 / 岛顶行 5–14 → 悬浮高度 1–3.75 单位 / 语汇：船体、桅、桅盘
    ///   · level_5（44×20，水线行 17）：5 岛 / 其中船岛 0 / 地面 186 格 / 岛顶行 3–12 → 悬浮高度 1.5–4.5 单位 / 语汇：沙洲、土体
    ///   · level_6（115×28，水线行 25）：11 岛 / 其中船岛 0 / 地面 475 格 / 岛顶行 6–23 → 悬浮高度 0.5–6 单位 / 语汇：沙洲、草地、土体
    ///   · level_7（24×12，水线行 9）：1 岛 / 其中船岛 0 / 地面 91 格 / 岛顶行 2–2 → 悬浮高度 2.25–2.25 单位 / 语汇：沙洲、土体
    ///   · level_8（20×35，水线行 31）：7 岛 / 其中船岛 0 / 地面 212 格 / 岛顶行 4–26 → 悬浮高度 1.5–8.5 单位 / 语汇：草地、土体
    ///   · level_9（59×25，水线行 24）：12 岛 / 其中船岛 12 / 地面 192 格 / 岛顶行 2–21 → 悬浮高度 1–7 单位 / 语汇：船体、桅、桅盘
    ///   · level_10（77×32，水线行 29）：3 岛 / 其中船岛 0 / 地面 590 格 / 岛顶行 6–12 → 悬浮高度 5.25–7.25 单位 / 语汇：沙洲、草地、土体
    ///   · level_11（44×16，水线行 13）：8 岛 / 其中船岛 0 / 地面 142 格 / 岛顶行 2–11 → 悬浮高度 0.5–3.5 单位 / 语汇：沙洲、草地、土体
    ///   · level_12（44×26，水线行 23）：21 岛 / 其中船岛 0 / 地面 199 格 / 岛顶行 1–22 → 悬浮高度 0.25–7 单位 / 语汇：沙洲、草地、土体
    ///   · level_13（90×7，水线行 4）：15 岛 / 其中船岛 0 / 地面 49 格 / 岛顶行 3–3 → 悬浮高度 0.25–0.25 单位 / 语汇：沙洲、草地
    ///   · level_14（48×31，水线行 28）：8 岛 / 其中船岛 0 / 地面 448 格 / 岛顶行 5–19 → 悬浮高度 2.75–7.25 单位 / 语汇：沙洲、草地、土体
    ///   · level_15（86×20，水线行 17）：1 岛 / 其中船岛 0 / 地面 433 格 / 岛顶行 2–2 → 悬浮高度 4.75–4.75 单位 / 语汇：沙洲、草地、土体
    ///   · level_16（50×17，水线行 14）：9 岛 / 其中船岛 5 / 地面 163 格 / 岛顶行 4–11 → 悬浮高度 1–3 单位 / 语汇：船体、桅、桅盘、沙洲、草地、土体
    ///   · level_17（47×28，水线行 27）：19 岛 / 其中船岛 0 / 地面 111 格 / 岛顶行 5–24 → 悬浮高度 1–7 单位 / 语汇：草地、土体
    ///   · level_18（40×21，水线行 18）：9 岛 / 其中船岛 8 / 地面 165 格 / 岛顶行 5–15 → 悬浮高度 1–4 单位 / 语汇：船体、桅、桅盘、草地、土体
    ///   · level_19（56×18，水线行 17）：8 岛 / 其中船岛 8 / 地面 134 格 / 岛顶行 5–14 → 悬浮高度 1–3.75 单位 / 语汇：船体、桅、桅盘
    ///   · level_20（44×18，水线行 15）：5 岛 / 其中船岛 0 / 地面 186 格 / 岛顶行 1–10 → 悬浮高度 1.5–4.5 单位 / 语汇：沙洲、土体
    ///   · level_21（63×35，水线行 32）：1 岛 / 其中船岛 0 / 地面 385 格 / 岛顶行 4–4 → 悬浮高度 8.75–8.75 单位 / 语汇：沙洲、草地、土体
    ///   · level_22（115×28，水线行 25）：11 岛 / 其中船岛 0 / 地面 475 格 / 岛顶行 6–23 → 悬浮高度 0.5–6 单位 / 语汇：沙洲、草地、土体
    ///   · level_23（24×16，水线行 13）：1 岛 / 其中船岛 0 / 地面 91 格 / 岛顶行 6–6 → 悬浮高度 2.25–2.25 单位 / 语汇：沙洲、土体
    ///   · level_24（23×34，水线行 30）：7 岛 / 其中船岛 0 / 地面 232 格 / 岛顶行 3–25 → 悬浮高度 1.5–8.5 单位 / 语汇：草地、土体
    ///   · level_25（59×27，水线行 26）：12 岛 / 其中船岛 12 / 地面 192 格 / 岛顶行 4–23 → 悬浮高度 1–7 单位 / 语汇：船体、桅、桅盘
    ///   · level_26（77×32，水线行 29）：3 岛 / 其中船岛 0 / 地面 590 格 / 岛顶行 6–12 → 悬浮高度 5.25–7.25 单位 / 语汇：沙洲、草地、土体
    ///   · level_27（21×20，水线行 19）：8 岛 / 其中船岛 0 / 地面 96 格 / 岛顶行 4–13 → 悬浮高度 2–4.75 单位 / 语汇：草地、土体
    ///   · level_28（44×20，水线行 17）：8 岛 / 其中船岛 0 / 地面 142 格 / 岛顶行 6–15 → 悬浮高度 0.5–3.5 单位 / 语汇：沙洲、草地、土体
    ///   · level_29（44×26，水线行 23）：21 岛 / 其中船岛 0 / 地面 199 格 / 岛顶行 1–22 → 悬浮高度 0.25–7 单位 / 语汇：沙洲、草地、土体
    ///   · level_30（90×12，水线行 9）：15 岛 / 其中船岛 0 / 地面 49 格 / 岛顶行 8–8 → 悬浮高度 0.25–0.25 单位 / 语汇：沙洲、草地
    ///   · level_31（48×30，水线行 27）：8 岛 / 其中船岛 0 / 地面 448 格 / 岛顶行 4–18 → 悬浮高度 2.75–7.25 单位 / 语汇：沙洲、草地、土体
    ///   · level_32（86×20，水线行 17）：1 岛 / 其中船岛 0 / 地面 433 格 / 岛顶行 2–2 → 悬浮高度 4.75–4.75 单位 / 语汇：沙洲、草地、土体
    ///   · level_33（41×33，水线行 30）：3 岛 / 其中船岛 0 / 地面 469 格 / 岛顶行 5–22 → 悬浮高度 2.5–7.75 单位 / 语汇：沙洲、草地、土体
    ///
    /// </summary>
    public static class LevelTileMaps
    {
        /// <summary>关卡总数（1–33，与 <see cref="LevelCatalog.TotalLevels"/> 同源）。</summary>
        public const int LevelCount = LevelCatalog.TotalLevels;

        /// <summary>空格瓦片名（原版行串里的 <c>-</c>）。</summary>
        public const string EmptyTile = "-";

        // ------------------------------------------------------------------
        // 33 关 <row> 行串原文（逐关一维，按原版行号顺序：0 = 最上一行）
        // ------------------------------------------------------------------
        static readonly string[][] RowsByLevel =
        {
            // level_1（50×17，水线行 14）
            new[]
            {
                "-:50",
                "-:50",
                "-:50",
                "-:50",
                "-:23,grass_top_left,grass_top_middle_1:4,grass_top_right,-:3,grass_top_left,grass_top_middle_2:4,grass_top_right,-:12",
                "-:23,earth_edge_left,earth_edge_middle_1:2,eartyh_bottom_middle:2,earth_bottom_left,-:3,earth_bottom_right,eartyh_bottom_middle:2,earth_edge_middle_1:2,earth_edge_right,-:8,crows_nest_2,crows_nest_1,-:2",
                "-:2,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:9,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:12",
                "-:35,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:6,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right",
                "-:29,shell_sand_top_left,shell_sand_middle_2,blank_sand_top_right,-:18",
                "-:23,grass_top_left,grass_top_middle_2,grass_top_right,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:18",
                "-:23,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:24",
                "ship_right_facing_1,ship_right_facing_4_1,-:12,ship_left_side_shaded_4,ship_right_facing_3,ship_end_3:2,ship_end_2,-:4,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:15,ship_end_1,ship_end_3,ship_left_facing_1,ship_right_facing_4_1,-:3,ship_left_side_shaded_4,ship_left_facing_5",
                "ship_right_facing_2,cannon_port_3,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_anchor_3,-:5,blank_sand_top_left,blank_sand_middle_2:2,sand_overlap_left,earth_edge_middle_2,sand_overlap_right,blank_sand_middle_1:2,shell_sand_middle_1,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1:2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1:2,shell_sand_middle_2,shell_sand_top_right,-:4,ship_anchor_1,cannon_port_2:3,cannon_port_1,ship_right_side_shaded_2",
                "ship_right_facing_4,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_anchor_4,-:5,earth_edge_left,earth_edge_middle_2:4,earth_edge_middle_1,earth_edge_middle_2:4,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2:5,earth_edge_right,-:4,ship_anchor_2,ship_tile_1,ship_tile_2:2,ship_tile_1,ship_left_facing_3",
                "boat_ripple_1,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_4,-:5,tile_ripple_left,tile_ripple_middle:18,tile_ripple_right,-:4,boat_ripple_1,boat_ripple_3:2,boat_ripple_2,boat_ripple_3,boat_ripple_4",
                "-:20,tile_ripple_left,tile_ripple_middle:18,tile_ripple_right,-:10",
                "-:20,tile_ripple_left,tile_ripple_middle:18,tile_ripple_right,-:10",
            },
            // level_2（47×27，水线行 26）
            new[]
            {
                "-:47",
                "-:47",
                "-:47",
                "-:47",
                "-:5,grass_row_left,grass_row_middle:2,grass_row_right,-:12,grass_row_left,grass_row_middle,grass_row_right,-:8,grass_top_left,grass_top_middle_1,grass_top_right,-:12",
                "-:32,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:6,grass_row_left,grass_row_middle,grass_row_right,-:3",
                "-:47",
                "-:13,grass_top_left,grass_top_middle_1:2,grass_top_right,-:30",
                "-:13,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:6,grass_row_left,grass_row_middle:3,grass_row_right,-:19",
                "-:37,grass_top_left,grass_top_middle_2:3,grass_top_right,-:5",
                "-:37,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:5",
                "-:4,grass_top_left,grass_top_middle_2:2,grass_top_right,-:39",
                "-:4,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:39",
                "-:47",
                "-:20,grass_row_left,grass_row_middle:2,grass_row_right,-:6,grass_top_left,grass_top_middle_1:2,grass_top_right,-:13",
                "-:30,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:13",
                "-:9,grass_row_left,grass_row_middle:2,grass_row_right,-:25,grass_top_left,grass_top_middle_1,grass_top_right,-:6",
                "-:38,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:6",
                "-:23,grass_top_left,grass_top_middle_1,grass_top_right,-:21",
                "-:23,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:17,grass_top_left,grass_top_middle_1:2,grass_top_right",
                "-:43,earth_edge_left,earth_edge_middle_1:2,earth_edge_right",
                "-:11,grass_top_left,grass_top_middle_1:2,grass_top_right,-:3,grass_row_left,grass_row_middle,grass_row_right,-:12,grass_top_left,grass_top_middle_1,grass_top_right,-:7,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left",
                "-:3,grass_row_left,grass_row_middle:2,grass_row_right,-:4,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:18,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:11",
                "-:27,grass_row_left,grass_row_middle,grass_row_right,-:17",
                "-:47",
                "-:47",
                "-:47",
            },
            // level_3（40×23，水线行 20）
            new[]
            {
                "-:40",
                "-:40",
                "-:40",
                "-:40",
                "-:40",
                "-:40",
                "-:40",
                "-:8,crows_nest_2,crows_nest_1,-:10,grass_top_single,-:10,crows_nest_2,crows_nest_1,-:7",
                "-:20,earth_single_collum,-:19",
                "-:6,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:6,grass_top_left,grass_top_middle_1,grass_overlap_both,grass_top_middle_1,grass_top_right,-:6,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:5",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:4,mast_end_left,mast_tile:3,mast_middle_2,mast_middle_1,mast_tile:3,mast_end_right,-:4,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:4,mast_end_left,mast_tile:3,mast_middle_2,mast_middle_1,mast_tile:3,mast_end_right,-:3",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:16,grass_top_left,grass_top_middle_1,grass_overlap_left,earth_edge_middle_1:3,grass_overlap_right,grass_top_middle_1,grass_top_right,-:15",
                "-:16,earth_edge_left,earth_edge_middle_1:6,earth_edge_middle_2,earth_edge_right,-:15",
                "-,ship_end_1,ship_end_3:2,ship_left_facing_1,ship_left_facing_2,-:6,ship_left_side_shaded_4,ship_left_facing_5,-:2,earth_edge_left,earth_edge_middle_1:7,earth_edge_right,-:2,ship_right_facing_1,ship_right_facing_4_1,-:6,ship_right_facing_3_1,ship_right_facing_3,ship_end_3:2,ship_end_2",
                "-:5,ship_anchor_1,cannon_port_2:3,ship_top_middle,cannon_port_2:2,cannon_port_1,ship_right_side_shaded_2,-:2,earth_edge_left,earth_edge_middle_1:7,earth_edge_right,-:2,ship_right_facing_2,cannon_port_3,cannon_port_2:2,ship_top_middle,cannon_port_2:3,ship_anchor_3,-:4",
                "-:5,ship_anchor_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_left_facing_3,-,grass_top_left,grass_overlap_left,earth_edge_middle_1:7,grass_overlap_right,grass_top_right,-,ship_right_facing_4,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_anchor_4,-:4",
                "-:5,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_4,-,tile_ripple_left,tile_ripple_middle:9,tile_ripple_right,-,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_4,-:4",
                "-:15,tile_ripple_left,tile_ripple_middle:9,tile_ripple_right,-:14",
                "-:15,tile_ripple_left,tile_ripple_middle:9,tile_ripple_right,-:14",
            },
            // level_4（56×18，水线行 17）
            new[]
            {
                "-:56",
                "-:56",
                "-:56",
                "-:56",
                "-:56",
                "-:6,crows_nest_2,crows_nest_1,-:40,crows_nest_2,crows_nest_1,-:6",
                "-:5,mast_end_left,mast_middle_2,mast_middle_1,mast_end_right,-:38,mast_end_left,mast_tile:2,mast_end_right,-:5",
                "-:56",
                "-:56",
                "-:15,crows_nest_2,crows_nest_1,-:22,crows_nest_2,crows_nest_1,-:15",
                "-:3,mast_end_left,mast_tile:2,mast_middle_2,mast_middle_1,mast_tile:2,mast_end_right,-:2,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:18,mast_end_left,mast_tile:4,mast_end_right,-:2,mast_end_left,mast_tile:6,mast_end_right,-:3",
                "-:56",
                "-:56",
                "-:56",
                "ship_right_facing_1,ship_right_facing_4_1,-:16,ship_left_side_shaded_4,ship_right_facing_3,ship_end_3:2,ship_end_2,-:10,ship_end_1,ship_end_3:2,ship_left_facing_1,ship_left_facing_2,-:16,ship_left_side_shaded_4,ship_left_facing_5",
                "ship_right_facing_2,cannon_port_3,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,ship_anchor_3,-:18,ship_anchor_1,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,cannon_port_1,ship_right_side_shaded_2",
                "ship_right_facing_4,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_anchor_4,-:18,ship_anchor_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_left_facing_3",
                "boat_ripple_1,boat_ripple_3:11,boat_ripple_2:2,boat_ripple_3:3,boat_ripple_2,boat_ripple_4,-:18,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3:3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3:6,boat_ripple_4",
            },
            // level_5（44×20，水线行 17）
            new[]
            {
                "-:44",
                "-:44",
                "-:44",
                "-:12,blank_sand_top_left,blank_sand_middle_2:3,blank_sand_top_right,-:27",
                "-:12,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1:2,earth_bottom_left,-:27",
                "-:14,earth_bottom_right,earth_bottom_left,-:28",
                "-:44",
                "-:21,blank_sand_top_left,blank_sand_middle_2:3,blank_sand_top_right,-:5,blank_sand_top_left,blank_sand_middle_2:4,blank_sand_top_right,-:7",
                "-:6,blank_sand_top_left,blank_sand_middle_2:4,blank_sand_top_right,-:9,earth_bottom_right,earth_edge_middle_1:3,earth_bottom_left,-:5,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1:2,earth_bottom_middle_3,earth_bottom_left,-:7",
                "-:6,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1:2,earth_bottom_middle_3,earth_bottom_left,-:10,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:8,earth_bottom_right,earth_bottom_left,-:9",
                "-:8,earth_bottom_right,earth_bottom_left,-:34",
                "-:44",
                "-:29,shell_sand_top_left,blank_sand_top_right,-:8,blank_sand_top_left,blank_sand_top_right,-:3",
                "-:2,blank_sand_top_left,blank_sand_middle_2,blank_sand_top_right,-:10,blank_sand_top_left,blank_sand_middle_2:2,blank_sand_top_right,-:10,earth_edge_left,earth_edge_right,-:8,earth_edge_left,earth_edge_right,-:3",
                "-,blank_sand_top_left,sand_overlap_left,earth_edge_middle_1,sand_overlap_right,blank_sand_top_right,-:4,blank_sand_top_left,blank_sand_middle_2:4,sand_overlap_left,earth_edge_middle_1:2,sand_overlap_right,blank_sand_middle_2:3,blank_sand_top_right,-:3,blank_sand_top_left,blank_sand_middle_2:2,sand_overlap_left,sand_overlap_right,blank_sand_middle_2:3,blank_sand_top_right,-:3,blank_sand_top_left,sand_overlap_left,earth_edge_right,-:3",
                "blank_sand_top_left,sand_overlap_left,earth_edge_middle_1:3,sand_overlap_right,blank_sand_middle_2:4,sand_overlap_left,earth_edge_middle_1:11,sand_overlap_right,blank_sand_middle_2:3,sand_overlap_left,earth_edge_middle_1:7,sand_overlap_right,blank_sand_middle_2:3,sand_overlap_left,earth_edge_middle_1,sand_overlap_right,blank_sand_middle_2:2,blank_sand_top_right",
                "earth_edge_left,earth_edge_middle_1:42,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
            },
            // level_6（115×28，水线行 25）
            new[]
            {
                "-:115",
                "-:115",
                "-:115",
                "-:115",
                "-:115",
                "-:115",
                "-:47,grass_top_left,grass_top_middle_1:24,grass_top_right,-:42",
                "-:46,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:2,earth_bottom_middle_3,eartyh_bottom_middle:18,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:41",
                "-:45,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_left,-:19,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:40",
                "-:44,grass_top_left,grass_overlap_left,earth_edge_middle_2:3,earth_edge_right,-:21,earth_bottom_right,eartyh_bottom_middle,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:39",
                "-:43,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_edge_middle_2:3,earth_bottom_left,-:23,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:38",
                "-:42,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_middle_2:2,earth_edge_right,-:25,earth_bottom_right,earth_edge_middle_2:2,grass_top_right,-:37",
                "-:41,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2:2,earth_edge_right,-:6,grass_top_left,grass_top_middle_1:10,grass_top_right,-:8,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:36",
                "-:40,grass_top_left,grass_overlap_left,earth_edge_middle_1:4,earth_edge_middle_2:2,earth_edge_right,-:6,earth_bottom_right,eartyh_bottom_middle:10,earth_bottom_left,-:9,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:35",
                "-:39,grass_top_left,grass_overlap_left,earth_edge_middle_1:6,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:27,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:34",
                "-:38,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,eartyh_bottom_middle:4,earth_bottom_middle_2,earth_edge_middle_2:2,earth_edge_right,-:28,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:33",
                "-:37,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_bottom_left,-:5,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:29,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:32",
                "-:36,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:6,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:30,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:31",
                "-:35,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_right,-:6,earth_edge_left,earth_edge_middle_2:2,grass_top_middle_1:9,grass_top_right,-:3,grass_top_left,grass_top_middle_1:6,grass_top_right,-:10,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:31",
                "-:34,grass_top_left,grass_overlap_left,earth_edge_middle_1:4,earth_edge_right,-:4,grass_top_left,grass_top_middle_1,grass_overlap_left,earth_edge_middle_2:7,earth_edge_middle_1:4,earth_edge_right,-:3,earth_edge_left,earth_edge_middle_1:6,grass_overlap_right,grass_top_right,-:43",
                "-:34,earth_bottom_right,eartyh_bottom_middle:5,earth_bottom_left,-:4,earth_bottom_right,eartyh_bottom_middle:13,earth_bottom_left,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_middle_2,earth_edge_middle_2:2,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-:39,grass_top_left,grass_top_middle_1,grass_top_right",
                "-:2,grass_top_left,grass_top_middle_1,grass_top_right,-:61,earth_edge_left,earth_edge_middle_2:5,grass_overlap_right,grass_top_right,-:38,earth_edge_left,earth_edge_middle_2,earth_edge_right",
                "-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:61,earth_edge_left,earth_edge_middle_2:6,sand_overlap_right,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1,shell_sand_top_right,-:2,blank_sand_top_left,shell_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1:2,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2:2,shell_sand_middle_2,blank_sand_top_right,-:2,blank_sand_top_left,shell_sand_middle_2:2,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_2,earth_edge_right",
                "-:2,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-,blank_sand_top_left,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,shell_sand_top_right,-,shell_sand_top_left,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2,shell_sand_top_right,-,shell_sand_top_left,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,shell_sand_top_right,-:2,blank_sand_top_left,shell_sand_top_right,-:3,blank_sand_top_left,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_2,blank_sand_top_right,-:20,earth_edge_left,earth_edge_middle_2:13,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1:5,earth_edge_middle_2:11,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:7,earth_edge_right",
                "-:2,earth_edge_left,tile_ripple_middle:2,earth_edge_right,-,earth_edge_left,tile_ripple_middle:3,earth_edge_right,-,earth_edge_left,tile_ripple_middle:3,earth_edge_right,-,earth_edge_left,earth_edge_middle_2:12,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:3,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:20,earth_edge_left,tile_ripple_middle:13,earth_edge_right,-:2,earth_edge_left,tile_ripple_middle:19,earth_edge_right,-:2,earth_edge_left,tile_ripple_middle:7,earth_edge_right",
                "-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:12,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:20,tile_ripple_left,tile_ripple_middle:13,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:19,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:7,tile_ripple_right",
                "-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:12,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:20,tile_ripple_left,tile_ripple_middle:13,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:19,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:7,tile_ripple_right",
                "-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:12,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:20,tile_ripple_left,tile_ripple_middle:13,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:19,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:7,tile_ripple_right",
            },
            // level_7（24×12，水线行 9）
            new[]
            {
                "-:24",
                "-:24",
                "-:10,blank_sand_top_left,shell_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_top_right,-:9",
                "-:10,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:9",
                "-:7,blank_sand_top_left,shell_sand_middle_1,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_2:3,sand_overlap_right,shell_sand_middle_2,blank_sand_middle_1,shell_sand_top_right,-:6",
                "-:7,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1:5,earth_edge_right,-:6",
                "-:7,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right,-:6",
                "blank_sand_top_left,shell_sand_middle_1,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,sand_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2:3,earth_edge_middle_1:3,sand_overlap_right,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2,blank_sand_top_right",
                "earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1:4,earth_edge_middle_2:2,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:22,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:22,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:22,tile_ripple_right",
            },
            // level_8（20×35，水线行 31）
            new[]
            {
                "-:20",
                "-:20",
                "-:20",
                "-:20",
                "-:3,grass_top_single,-:11,grass_top_single,-:4",
                "-:3,earth_single_collum,-:11,earth_single_collum,-:4",
                "-:3,earth_single_collum,-:11,earth_single_collum,-:4",
                "-:3,earth_single_collum,-:11,earth_single_collum,-:4",
                "-:2,grass_top_left,grass_overlap_left_edge,-:11,earth_single_collum,-:4",
                "-:2,earth_edge_left,earth_edge_right,-:11,earth_single_collum,-:4",
                "-:2,earth_edge_left,earth_edge_right,-:3,grass_row_left,grass_top_middle_1:3,grass_row_right,-:3,grass_overlap_right_edge,grass_top_right,-:3",
                "-:2,earth_edge_left,earth_edge_right,-:4,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4,earth_edge_left,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_right,-:11,earth_edge_left,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_right,-:11,earth_edge_left,earth_edge_right,-:3",
                "-:2,earth_edge_left,grass_overlap_right,grass_top_right,-:2,grass_row_left,grass_top_middle_1:3,grass_row_right,-:3,earth_edge_left,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4,earth_edge_left,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_middle_1,earth_bottom_left,-:10,earth_edge_left,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_right,-:11,earth_edge_left,earth_edge_right,-:3",
                "-,grass_top_left,grass_overlap_left,earth_edge_right,-:3,grass_row_left,grass_top_middle_1:3,grass_row_right,-:2,grass_top_left,grass_overlap_left,earth_edge_right,-:3",
                "-,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:4,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:3,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:3",
                "-,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:10,earth_bottom_right,earth_edge_middle_2,earth_edge_right,-:3",
                "-,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:11,earth_edge_left,grass_overlap_right,grass_top_right,-:2",
                "-,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:2,grass_row_left,grass_top_middle_1:3,grass_row_right,-:3,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2",
                "-,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2",
                "-,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2",
                "grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-",
                "earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:2,grass_top_left,grass_top_middle_1:3,grass_top_right,-:2,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-",
                "earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-",
                "earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-",
                "earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,grass_overlap_right,grass_top_right",
                "earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right",
            },
            // level_9（59×25，水线行 24）
            new[]
            {
                "-:59",
                "-:59",
                "-:24,crows_nest_2,crows_nest_1,-:33",
                "-:59",
                "-:59",
                "-:24,crows_nest_2,crows_nest_1,-:33",
                "-:59",
                "-:22,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:31",
                "-:59",
                "-:59",
                "-:59",
                "-:9,crows_nest_2,crows_nest_1,-:8,mast_end_left,mast_tile:4,mast_middle_2,mast_middle_1,mast_tile:4,mast_end_right,-:8,crows_nest_2,crows_nest_1,-:18",
                "-:59",
                "-:7,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:24,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:16",
                "-:59",
                "-:59",
                "-:17,mast_end_left,mast_tile:6,mast_middle_2,mast_middle_1,mast_tile:6,mast_end_right,-:26",
                "-:6,mast_end_left,mast_tile:2,mast_middle_2,mast_middle_1,mast_tile:2,mast_end_right,-:2,mast_end_left,mast_tile:7,mast_middle_2,mast_middle_1,mast_tile:7,mast_end_right,-:2,mast_end_left,mast_tile:2,mast_middle_2,mast_middle_1,mast_tile:2,mast_end_right,-:15",
                "-:59",
                "-:59",
                "-:59",
                "-:4,ship_right_facing_1,ship_right_facing_4_1,-:39,ship_left_side_shaded_4,ship_right_facing_3,ship_end_3:11,ship_end_2",
                "ship_end_1,ship_end_3:3,ship_right_facing_2,cannon_port_3,cannon_port_2:3,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_anchor_3,-:13",
                "-:4,ship_right_facing_4,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2:2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_anchor_4,-:13",
                "-:4,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3:2,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_4,-:13",
            },
            // level_10（77×32，水线行 29）
            new[]
            {
                "-:77",
                "-:77",
                "-:77",
                "-:77",
                "-:77",
                "-:77",
                "-:37,grass_top_left,grass_top_right,-:38",
                "-:37,earth_edge_left,earth_edge_right,-:38",
                "-:36,grass_top_left,grass_overlap_left,earth_edge_right,-:38",
                "-:35,grass_top_left,grass_overlap_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:37",
                "-:35,earth_edge_left,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:26,grass_top_left,grass_top_right,-:8",
                "-:35,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:26,earth_edge_left,earth_edge_right,-:8",
                "-:9,grass_top_left,grass_top_middle_1,grass_top_right,-:22,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_1,grass_top_right,-:23,grass_top_left,grass_overlap_left,grass_overlap_right,grass_top_right,-:7",
                "-:9,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:22,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:5,earth_edge_right,-:23,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:7",
                "-:8,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:22,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:5,grass_overlap_right,grass_top_right,-:21,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_2,grass_top_right,-:5",
                "-:7,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:20,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2:3,earth_edge_right,-:21,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:5",
                "-:7,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:20,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:20,grass_top_left,grass_overlap_left,earth_edge_middle_2:5,earth_edge_right,-:5",
                "-:6,grass_top_left,grass_overlap_left,earth_edge_middle_2:4,earth_edge_right,-:20,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:20,earth_edge_left,earth_edge_middle_2:2,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:4",
                "-:6,earth_edge_left,earth_edge_middle_2:2,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2,grass_overlap_right,grass_top_middle_1,grass_top_right,-:17,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1,grass_overlap_right,grass_top_middle_2:2,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:19,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4",
                "-:5,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:17,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:5,earth_edge_middle_1:2,earth_edge_right,-:18,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:17,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1:4,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:18,earth_edge_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-:4",
                "-:4,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:14,grass_top_left,grass_top_middle_1,grass_overlap_left,earth_edge_middle_2:5,eartyh_bottom_middle:2,earth_edge_middle_2,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-:16,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:4,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:3",
                "-:4,earth_edge_left,earth_edge_middle_2:10,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_2:6,earth_bottom_left,-:2,earth_bottom_right,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:16,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1:4,earth_edge_middle_2:3,earth_edge_right,-:3",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2:4,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:16,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1:2,eartyh_bottom_middle:2,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_right,-:3",
                "-:3,grass_top_left,grass_overlap_left,earth_edge_middle_2:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:13,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-:14,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_middle_1,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:2",
                "-:3,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:11,shell_sand_top_left,shell_sand_middle_1,sand_overlap_left,earth_edge_middle_2:5,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:3,earth_edge_middle_2,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1:2,earth_edge_right,-:2",
                "-,grass_top_left,grass_top_middle_2,grass_overlap_left,earth_edge_middle_2:4,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:4,sand_overlap_right,blank_sand_middle_2,shell_sand_top_right,-:9,earth_edge_left,earth_edge_middle_2:7,grass_overlap_right,grass_top_middle_1,grass_top_middle_2:2,grass_top_middle_1,grass_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2,sand_overlap_right,shell_sand_middle_1,shell_sand_top_right,-:10,shell_sand_top_left,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_2:3,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:2,grass_overlap_right,grass_top_middle_1,grass_top_right",
                "shell_sand_top_left,sand_overlap_left,earth_edge_middle_2:16,earth_edge_right,-:7,shell_sand_top_left,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_2:9,earth_edge_middle_1:7,earth_edge_middle_2:3,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_2:5,earth_edge_middle_1:5,earth_edge_middle_2:3,earth_edge_middle_1:3,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,earth_edge_left,earth_edge_middle_2:21,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_2:16,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:10,tile_ripple_left,tile_ripple_middle:16,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:10,tile_ripple_left,tile_ripple_middle:16,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:10,tile_ripple_left,tile_ripple_middle:16,tile_ripple_right",
            },
            // level_11（44×16，水线行 13）
            new[]
            {
                "-:44",
                "-:44",
                "-:10,grass_top_left,grass_top_middle_1,grass_top_right,-:2,grass_top_left,grass_top_middle_1,grass_top_right,-:9,grass_top_left,grass_top_middle_1,grass_top_right,-:2,grass_top_left,grass_top_middle_2,grass_top_right,-:9",
                "-:10,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:9,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:9",
                "-:10,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:2,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:2,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9",
                "-:44",
                "-:4,grass_top_left,grass_top_middle_1,grass_top_right,-:14,grass_top_left,grass_top_middle_1,grass_top_right,-:13,grass_top_left,grass_top_middle_2,grass_top_right,-:4",
                "-:4,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:14,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:13,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4",
                "-:44",
                "-:44",
                "-:44",
                "blank_sand_top_left,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1:6,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1:2,blank_sand_middle_2:3,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2:2,blank_sand_middle_1,shell_sand_middle_2:2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1:2,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1:5,blank_sand_middle_2,shell_sand_top_right",
                "earth_edge_left,earth_edge_middle_1:42,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
            },
            // level_12（44×26，水线行 23）
            new[]
            {
                "-:44",
                "-:34,grass_top_left,grass_top_middle_1:2,grass_top_right,-:6",
                "-:18,grass_top_left,grass_top_middle_1,grass_top_right,-:13,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:6",
                "-:18,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:23",
                "-:5,single_grass_2,-:24,single_grass_2,-:10,grass_top_left,grass_top_right,-",
                "-:41,earth_bottom_right,earth_bottom_left,-",
                "-:11,single_grass_1,-:32",
                "grass_top_left,grass_top_middle_1:2,grass_top_right,-:13,grass_top_left,grass_top_middle_2,grass_top_middle_1:2,grass_top_middle_2:2,grass_top_middle_1,grass_top_middle_2,grass_top_middle_1:2,grass_top_right,-:16",
                "earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:12,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:15",
                "-:16,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_1:3,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_right,-:9,grass_top_left,grass_top_middle_2:2,grass_top_right,-:2",
                "-:16,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:9,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:2",
                "-:11,grass_top_left,grass_top_middle_1,grass_top_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,grass_top_left,grass_top_right,-:11",
                "-:11,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:2,earth_bottom_right,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,earth_bottom_left,-:2,earth_edge_left,grass_overlap_right,grass_top_right,-:10",
                "-:17,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1,eartyh_bottom_middle,earth_bottom_middle_2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_left,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:10",
                "-:19,earth_bottom_single,-:2,earth_bottom_single,-:2,earth_bottom_single,-:18",
                "-:6,grass_top_single,-:30,grass_top_single,-:5,single_grass_2",
                "-:5,grass_top_left,grass_overlap_both,grass_top_middle_1:2,grass_top_right,-:9,grass_top_left,grass_top_middle_2,grass_top_middle_1:2,grass_top_middle_2,grass_top_middle_1,grass_top_right,-:8,grass_top_left,grass_top_middle_1:2,grass_overlap_both,grass_top_right,-:5",
                "-:5,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:9,earth_bottom_right,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_bottom_left,-:8,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:5",
                "-:15,grass_top_left,grass_top_middle_1,grass_top_right,-:2,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:2,grass_top_left,grass_top_middle_2,grass_top_right,-:14",
                "single_grass_2,-:14,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:14",
                "-:44",
                "-:44",
                "-:10,shell_sand_top_left,shell_sand_top_right,-:7,shell_sand_top_left,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,shell_sand_top_right,-:8,shell_sand_top_left,blank_sand_top_right,-:8",
                "-:10,tile_ripple_left,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:8,tile_ripple_left,tile_ripple_right,-:8",
                "-:10,tile_ripple_left,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:8,tile_ripple_left,tile_ripple_right,-:8",
                "-:10,tile_ripple_left,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:8,tile_ripple_left,tile_ripple_right,-:8",
            },
            // level_13（90×7，水线行 4）
            new[]
            {
                "-:90",
                "-:90",
                "-:90",
                "grass_top_left,grass_top_middle_1:4,grass_top_right,-:3,grass_top_left,grass_top_right,-:2,grass_top_left,grass_top_middle_1:2,grass_top_right,-:5,grass_top_left,grass_top_right,-:4,shell_sand_top_left,blank_sand_middle_1,blank_sand_top_right,-,grass_top_left,grass_top_right,-:2,grass_top_left,grass_top_right,-:3,blank_sand_top_left,blank_sand_middle_1,shell_sand_top_right,-:5,grass_top_left,grass_top_middle_1,grass_top_right,-:2,shell_sand_top_left,shell_sand_middle_2:2,blank_sand_top_right,-,blank_sand_top_left,blank_sand_top_right,-:5,grass_top_left,grass_top_middle_1:2,grass_top_right,-:3,grass_top_left,grass_top_middle_1:2,grass_top_right,-:2,shell_sand_top_left,blank_sand_middle_2,shell_sand_middle_1,shell_sand_top_right,-:3,grass_top_left,grass_top_middle_2:2,grass_top_right",
                "tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_right,-:4,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_right,-:4,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_right,-:4,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right",
            },
            // level_14（48×31，水线行 28）
            new[]
            {
                "-:48",
                "-:48",
                "-:48",
                "-:48",
                "-:48",
                "-:5,grass_top_left,grass_top_middle_2:2,grass_top_right,-:30,grass_top_left,grass_top_middle_2:2,grass_top_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:30,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:12,grass_top_left,grass_top_middle_1:2,grass_top_right,-:14,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:11,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4,grass_top_left,grass_top_middle_1,grass_top_right,-:4,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:7,grass_top_left,grass_top_middle_1,grass_top_right,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:14,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:3,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-:3,earth_bottom_right,eartyh_bottom_middle:2,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1,grass_top_right,-:12,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:6,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:7,grass_top_left,grass_top_right,-:11,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:14,grass_top_left,grass_top_middle_1:2,grass_overlap_left,earth_edge_right,-:11,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:13,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:10,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:28,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:6,grass_top_left,grass_top_right,-:20,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:5,blank_sand_top_left,sand_overlap_left,sand_overlap_right,shell_sand_middle_2,blank_sand_top_right,-:18,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,grass_overlap_right,grass_top_right,-:4",
                "-:3,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2:4,earth_edge_right,-:5,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:8,grass_top_left,grass_top_right,-:8,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:4",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:4,sand_overlap_right,blank_sand_top_right,-:16,blank_sand_top_left,sand_overlap_left,sand_overlap_right,shell_sand_top_right,-:6,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:4",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:5,earth_edge_right,-:16,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:6,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1:3,earth_edge_right,-:4",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:5,sand_overlap_right,blank_sand_top_right,-:24,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:3,sand_overlap_right,blank_sand_top_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:6,earth_edge_right,-:24,earth_edge_left,earth_edge_middle_2:4,earth_edge_middle_1:3,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:6,sand_overlap_right,blank_sand_top_right,-:22,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2:4,earth_edge_middle_1:3,earth_edge_right,-:3",
                "-,blank_sand_top_left,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_1,earth_edge_middle_2:7,sand_overlap_right,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_1,shell_sand_top_right,-:4,blank_sand_top_left,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_2:5,earth_edge_middle_1:3,sand_overlap_right,blank_sand_middle_2,shell_sand_middle_2,blank_sand_top_right",
                "shell_sand_top_left,sand_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2:16,sand_overlap_right,blank_sand_top_right,-:2,shell_sand_top_left,sand_overlap_left,earth_edge_middle_2:14,earth_edge_middle_1:6,earth_edge_right",
                "earth_edge_left,earth_edge_middle_1:4,earth_edge_middle_2:17,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:14,earth_edge_middle_1:7,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right",
            },
            // level_15（86×20，水线行 17）
            new[]
            {
                "-:86",
                "-:86",
                "-:72,grass_top_left,grass_top_middle_1,grass_top_right,-:11",
                "-:8,grass_top_left,grass_top_middle_1,grass_top_right,-:60,grass_top_left,grass_overlap_left,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:10",
                "-:7,grass_top_left,grass_overlap_left,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:59,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:10",
                "-:7,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:59,earth_edge_left,earth_edge_middle_1,eartyh_bottom_middle:2,grass_overlap_right,grass_top_right,-:9",
                "-:6,grass_top_left,grass_overlap_left,eartyh_bottom_middle:2,earth_edge_middle_1,earth_edge_right,-:59,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:9",
                "-:6,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:59,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:9",
                "-:6,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:59,earth_edge_left,grass_overlap_right,grass_row_middle:2,grass_overlap_left,earth_edge_right,-:9",
                "-:6,earth_edge_left,grass_overlap_right,grass_row_middle:2,grass_overlap_left,earth_edge_right,-:8,grass_top_left,grass_top_middle_1,grass_top_right,-:19,grass_top_left,grass_top_middle_1,grass_top_right,-:19,grass_top_left,grass_top_right,-:5,earth_edge_left,earth_edge_right,-:2,earth_edge_left,grass_overlap_right,grass_top_right,-:8",
                "-:6,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:8,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:19,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:19,earth_edge_left,earth_edge_right,-:5,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:8",
                "-:5,grass_top_left,grass_overlap_left,earth_edge_right,-:2,earth_edge_left,sand_overlap_right,blank_sand_top_right,-:7,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:19,earth_edge_left,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1,grass_top_right,-:17,earth_edge_left,grass_overlap_right,grass_top_middle_2,grass_top_right,-:2,grass_top_left,grass_overlap_left,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,earth_edge_right,-:8",
                "-:5,earth_edge_left,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,sand_overlap_right,shell_sand_top_right,-:6,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:18,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_right,-:17,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:6,sand_overlap_right,blank_sand_top_right,-:7",
                "-:5,earth_edge_left,earth_edge_middle_1:7,earth_edge_right,-:4,blank_sand_top_left,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_1,sand_overlap_right,blank_sand_middle_2,blank_sand_top_right,-:7,blank_sand_top_left,blank_sand_middle_1,shell_sand_middle_2:2,blank_sand_middle_1,-:4,earth_edge_left,earth_edge_middle_1:2,tile_ripple_middle,earth_edge_middle_1,earth_edge_right,-:12,blank_sand_top_left,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2:2,sand_overlap_left,earth_edge_middle_1:2,sand_overlap_right,shell_sand_middle_2,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_1:8,-:7",
                "-:5,earth_edge_left,earth_edge_middle_1:7,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_1:5,earth_edge_right,-:6,shell_sand_top_left,sand_overlap_left,earth_edge_middle_1:3,sand_overlap_right,shell_sand_top_right,-:3,earth_edge_left,earth_edge_middle_1:4,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1:2,shell_sand_top_right,-:4,blank_sand_top_left,blank_sand_middle_2:2,sand_overlap_left,earth_edge_middle_1:18,sand_overlap_right,shell_sand_top_right,-:6",
                "shell_sand_top_left,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_1:18,sand_overlap_right,blank_sand_middle_1:2,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_1:2,sand_overlap_left,earth_edge_middle_1:5,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_1:9,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_1:22,sand_overlap_right,blank_sand_middle_1:2,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_1,blank_sand_top_right",
                "earth_edge_left,earth_edge_middle_1:84,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:84,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:84,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:84,tile_ripple_right",
            },
            // level_16（50×17，水线行 14）
            new[]
            {
                "-:50",
                "-:50",
                "-:50",
                "-:50",
                "-:23,grass_top_left,grass_top_middle_1:4,grass_top_right,-:3,grass_top_left,grass_top_middle_2:4,grass_top_right,-:12",
                "-:23,earth_edge_left,earth_edge_middle_1:2,eartyh_bottom_middle:2,earth_bottom_left,-:3,earth_bottom_right,eartyh_bottom_middle:2,earth_edge_middle_1:2,earth_edge_right,-:8,crows_nest_2,crows_nest_1,-:2",
                "-:2,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:9,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:12",
                "-:35,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:6,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right",
                "-:29,shell_sand_top_left,shell_sand_middle_2,blank_sand_top_right,-:18",
                "-:23,grass_top_left,grass_top_middle_2,grass_top_right,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:18",
                "-:23,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:24",
                "ship_right_facing_1,ship_right_facing_4_1,-:12,ship_left_side_shaded_4,ship_right_facing_3,ship_end_3:2,ship_end_2,-:4,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:15,ship_end_1,ship_end_3,ship_left_facing_1,ship_right_facing_4_1,-:3,ship_left_side_shaded_4,ship_left_facing_5",
                "ship_right_facing_2,cannon_port_3,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_top_middle,cannon_port_2,ship_anchor_3,-:5,blank_sand_top_left,blank_sand_middle_2:2,sand_overlap_left,earth_edge_middle_2,sand_overlap_right,blank_sand_middle_1:2,shell_sand_middle_1,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1:2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1:2,shell_sand_middle_2,shell_sand_top_right,-:4,ship_anchor_1,cannon_port_2:3,cannon_port_1,ship_right_side_shaded_2",
                "ship_right_facing_4,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_anchor_4,-:5,earth_edge_left,earth_edge_middle_2:4,earth_edge_middle_1,earth_edge_middle_2:4,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2:5,earth_edge_right,-:4,ship_anchor_2,ship_tile_1,ship_tile_2:2,ship_tile_1,ship_left_facing_3",
                "boat_ripple_1,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_4,-:5,tile_ripple_left,tile_ripple_middle:18,tile_ripple_right,-:4,boat_ripple_1,boat_ripple_3:2,boat_ripple_2,boat_ripple_3,boat_ripple_4",
                "-:20,tile_ripple_left,tile_ripple_middle:18,tile_ripple_right,-:10",
                "-:20,tile_ripple_left,tile_ripple_middle:18,tile_ripple_right,-:10",
            },
            // level_17（47×28，水线行 27）
            new[]
            {
                "-:47",
                "-:47",
                "-:47",
                "-:47",
                "-:47",
                "-:5,grass_row_left,grass_row_middle:2,grass_row_right,-:12,grass_row_left,grass_row_middle,grass_row_right,-:8,grass_top_left,grass_top_middle_1,grass_top_right,-:12",
                "-:32,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:6,grass_row_left,grass_row_middle,grass_row_right,-:3",
                "-:47",
                "-:13,grass_top_left,grass_top_middle_1:2,grass_top_right,-:30",
                "-:13,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:6,grass_row_left,grass_row_middle:3,grass_row_right,-:19",
                "-:37,grass_top_left,grass_top_middle_2:3,grass_top_right,-:5",
                "-:37,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:5",
                "-:4,grass_top_left,grass_top_middle_2:2,grass_top_right,-:39",
                "-:4,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:39",
                "-:47",
                "-:20,grass_row_left,grass_row_middle:2,grass_row_right,-:6,grass_top_left,grass_top_middle_1:2,grass_top_right,-:13",
                "-:30,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:13",
                "-:9,grass_row_left,grass_row_middle:2,grass_row_right,-:25,grass_top_left,grass_top_middle_1,grass_top_right,-:6",
                "-:38,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:6",
                "-:23,grass_top_left,grass_top_middle_1,grass_top_right,-:21",
                "-:23,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:17,grass_top_left,grass_top_middle_1:2,grass_top_right",
                "-:43,earth_edge_left,earth_edge_middle_1:2,earth_edge_right",
                "-:11,grass_top_left,grass_top_middle_1:2,grass_top_right,-:3,grass_row_left,grass_row_middle,grass_row_right,-:12,grass_top_left,grass_top_middle_1,grass_top_right,-:7,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left",
                "-:3,grass_row_left,grass_row_middle:2,grass_row_right,-:4,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:18,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:11",
                "-:27,grass_row_left,grass_row_middle,grass_row_right,-:17",
                "-:47",
                "-:47",
                "-:47",
            },
            // level_18（40×21，水线行 18）
            new[]
            {
                "-:40",
                "-:40",
                "-:40",
                "-:40",
                "-:40",
                "-:8,crows_nest_2,crows_nest_1,-:10,grass_top_single,-:10,crows_nest_2,crows_nest_1,-:7",
                "-:20,earth_single_collum,-:19",
                "-:6,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:6,grass_top_left,grass_top_middle_1,grass_overlap_both,grass_top_middle_1,grass_top_right,-:6,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:5",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:4,mast_end_left,mast_tile:3,mast_middle_2,mast_middle_1,mast_tile:3,mast_end_right,-:4,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:4,mast_end_left,mast_tile:3,mast_middle_2,mast_middle_1,mast_tile:3,mast_end_right,-:3",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:18,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:17",
                "-:16,grass_top_left,grass_top_middle_1,grass_overlap_left,earth_edge_middle_1:3,grass_overlap_right,grass_top_middle_1,grass_top_right,-:15",
                "-:16,earth_edge_left,earth_edge_middle_1:6,earth_edge_middle_2,earth_edge_right,-:15",
                "-,ship_end_1,ship_end_3:2,ship_left_facing_1,ship_left_facing_2,-:6,ship_left_side_shaded_4,ship_left_facing_5,-:2,earth_edge_left,earth_edge_middle_1:7,earth_edge_right,-:2,ship_right_facing_1,ship_right_facing_4_1,-:6,ship_right_facing_3_1,ship_right_facing_3,ship_end_3:2,ship_end_2",
                "-:5,ship_anchor_1,cannon_port_2:3,ship_top_middle,cannon_port_2:2,cannon_port_1,ship_right_side_shaded_2,-:2,earth_edge_left,earth_edge_middle_1:7,earth_edge_right,-:2,ship_right_facing_2,cannon_port_3,cannon_port_2:2,ship_top_middle,cannon_port_2:3,ship_anchor_3,-:4",
                "-:5,ship_anchor_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_left_facing_3,-,grass_top_left,grass_overlap_left,earth_edge_middle_1:7,grass_overlap_right,grass_top_right,-,ship_right_facing_4,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_anchor_4,-:4",
                "-:5,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_4,-,tile_ripple_left,tile_ripple_middle:9,tile_ripple_right,-,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_4,-:4",
                "-:15,tile_ripple_left,tile_ripple_middle:9,tile_ripple_right,-:14",
                "-:15,tile_ripple_left,tile_ripple_middle:9,tile_ripple_right,-:14",
            },
            // level_19（56×18，水线行 17）
            new[]
            {
                "-:56",
                "-:56",
                "-:56",
                "-:56",
                "-:56",
                "-:6,crows_nest_2,crows_nest_1,-:40,crows_nest_2,crows_nest_1,-:6",
                "-:5,mast_end_left,mast_middle_2,mast_middle_1,mast_end_right,-:38,mast_end_left,mast_tile:2,mast_end_right,-:5",
                "-:56",
                "-:56",
                "-:15,crows_nest_2,crows_nest_1,-:22,crows_nest_2,crows_nest_1,-:15",
                "-:3,mast_end_left,mast_tile:2,mast_middle_2,mast_middle_1,mast_tile:2,mast_end_right,-:2,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:18,mast_end_left,mast_tile:4,mast_end_right,-:2,mast_end_left,mast_tile:6,mast_end_right,-:3",
                "-:56",
                "-:56",
                "-:56",
                "ship_right_facing_1,ship_right_facing_4_1,-:16,ship_left_side_shaded_4,ship_right_facing_3,ship_end_3:2,ship_end_2,-:10,ship_end_1,ship_end_3:2,ship_left_facing_1,ship_left_facing_2,-:16,ship_left_side_shaded_4,ship_left_facing_5",
                "ship_right_facing_2,cannon_port_3,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,ship_anchor_3,-:18,ship_anchor_1,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,ship_top_middle,cannon_port_2:3,cannon_port_1,ship_right_side_shaded_2",
                "ship_right_facing_4,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_anchor_4,-:18,ship_anchor_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_left_facing_3",
                "boat_ripple_1,boat_ripple_3:11,boat_ripple_2:2,boat_ripple_3:3,boat_ripple_2,boat_ripple_4,-:18,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3:3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3:6,boat_ripple_4",
            },
            // level_20（44×18，水线行 15）
            new[]
            {
                "-:44",
                "-:12,blank_sand_top_left,blank_sand_middle_2:3,blank_sand_top_right,-:27",
                "-:12,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1:2,earth_bottom_left,-:27",
                "-:14,earth_bottom_right,earth_bottom_left,-:28",
                "-:44",
                "-:21,blank_sand_top_left,blank_sand_middle_2:3,blank_sand_top_right,-:5,blank_sand_top_left,blank_sand_middle_2:4,blank_sand_top_right,-:7",
                "-:6,blank_sand_top_left,blank_sand_middle_2:4,blank_sand_top_right,-:9,earth_bottom_right,earth_edge_middle_1:3,earth_bottom_left,-:5,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1:2,earth_bottom_middle_3,earth_bottom_left,-:7",
                "-:6,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1:2,earth_bottom_middle_3,earth_bottom_left,-:10,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:8,earth_bottom_right,earth_bottom_left,-:9",
                "-:8,earth_bottom_right,earth_bottom_left,-:34",
                "-:44",
                "-:29,shell_sand_top_left,blank_sand_top_right,-:8,blank_sand_top_left,blank_sand_top_right,-:3",
                "-:2,blank_sand_top_left,blank_sand_middle_2,blank_sand_top_right,-:10,blank_sand_top_left,blank_sand_middle_2:2,blank_sand_top_right,-:10,earth_edge_left,earth_edge_right,-:8,earth_edge_left,earth_edge_right,-:3",
                "-,blank_sand_top_left,sand_overlap_left,earth_edge_middle_1,sand_overlap_right,blank_sand_top_right,-:4,blank_sand_top_left,blank_sand_middle_2:4,sand_overlap_left,earth_edge_middle_1:2,sand_overlap_right,blank_sand_middle_2:3,blank_sand_top_right,-:3,blank_sand_top_left,blank_sand_middle_2:2,sand_overlap_left,sand_overlap_right,blank_sand_middle_2:3,blank_sand_top_right,-:3,blank_sand_top_left,sand_overlap_left,earth_edge_right,-:3",
                "blank_sand_top_left,sand_overlap_left,earth_edge_middle_1:3,sand_overlap_right,blank_sand_middle_2:4,sand_overlap_left,earth_edge_middle_1:11,sand_overlap_right,blank_sand_middle_2:3,sand_overlap_left,earth_edge_middle_1:7,sand_overlap_right,blank_sand_middle_2:3,sand_overlap_left,earth_edge_middle_1,sand_overlap_right,blank_sand_middle_2:2,blank_sand_top_right",
                "earth_edge_left,earth_edge_middle_1:42,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
            },
            // level_21（63×35，水线行 32）
            new[]
            {
                "-:63",
                "-:63",
                "-:63",
                "-:63",
                "-:34,grass_top_single,-:28",
                "-:33,grass_top_left,grass_overlap_left_edge,-:28",
                "-:33,earth_edge_left,earth_edge_right,-:28",
                "-:33,earth_edge_left,grass_overlap_right,grass_top_right,-:27",
                "-:32,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:27",
                "-:32,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right,-:27",
                "-:31,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:26",
                "-:31,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-:25",
                "-:31,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:25",
                "-:30,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:24",
                "-:29,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:5,earth_edge_right,-:24",
                "-:29,earth_edge_left,earth_edge_middle_1:2,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:23",
                "-:29,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_right,-:23",
                "-:29,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2:4,earth_edge_middle_1:2,grass_overlap_right,grass_top_right,-:22",
                "-:28,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_middle_1:7,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right,-:22",
                "-:27,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,eartyh_bottom_middle:3,earth_edge_middle_1:2,eartyh_bottom_middle:3,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:21",
                "-:27,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right,-:3,earth_edge_left,earth_edge_right,-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:21",
                "-:27,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right,-:3,earth_edge_left,earth_edge_right,-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:21",
                "-:26,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:3,earth_edge_left,earth_edge_right,-:3,earth_edge_left,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:20",
                "-:26,earth_edge_left,earth_edge_middle_1:3,grass_overlap_right,grass_top_middle_1:3,grass_overlap_left,grass_overlap_right,grass_top_middle_1:3,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:20",
                "-:25,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,eartyh_bottom_middle:2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:4,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_1,earth_edge_right,-:20",
                "-:24,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:2,earth_bottom_right,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_middle_2,earth_edge_middle_1,earth_bottom_left,-:2,earth_edge_left,grass_overlap_right,grass_top_middle_2,grass_top_right,-:18",
                "-:24,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right,-:3,earth_bottom_right,eartyh_bottom_middle:4,earth_bottom_left,-:3,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:18",
                "-:22,grass_top_left,grass_top_middle_2,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:10,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:17",
                "-:22,earth_edge_left,earth_edge_middle_2:6,grass_overlap_right,grass_top_right,-:8,grass_top_left,grass_overlap_left,earth_edge_middle_2:4,earth_edge_right,-:17",
                "-:22,earth_edge_left,earth_edge_middle_2:7,grass_overlap_right,grass_top_middle_1:8,grass_overlap_left,earth_edge_middle_2:5,grass_overlap_right,grass_top_middle_1,grass_top_right,-:15",
                "-:5,blank_sand_top_left,shell_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,shell_sand_middle_2:2,blank_sand_middle_2:2,shell_sand_middle_1,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:4,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:3,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_1,shell_sand_middle_2:2,blank_sand_middle_1,shell_sand_top_right",
                "-:5,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2:4,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:4,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right",
                "-:5,tile_ripple_left,tile_ripple_middle:56,tile_ripple_right",
                "-:5,tile_ripple_left,tile_ripple_middle:56,tile_ripple_right",
                "-:5,tile_ripple_left,tile_ripple_middle:56,tile_ripple_right",
            },
            // level_22（115×28，水线行 25）
            new[]
            {
                "-:115",
                "-:115",
                "-:115",
                "-:115",
                "-:115",
                "-:115",
                "-:47,grass_top_left,grass_top_middle_1:24,grass_top_right,-:42",
                "-:46,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:2,earth_bottom_middle_3,eartyh_bottom_middle:18,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:41",
                "-:45,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_left,-:19,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:40",
                "-:44,grass_top_left,grass_overlap_left,earth_edge_middle_2:3,earth_edge_right,-:21,earth_bottom_right,eartyh_bottom_middle,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:39",
                "-:43,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_edge_middle_2:3,earth_bottom_left,-:23,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:38",
                "-:42,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_middle_2:2,earth_edge_right,-:25,earth_bottom_right,earth_edge_middle_2:2,grass_top_right,-:37",
                "-:41,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2:2,earth_edge_right,-:6,grass_top_left,grass_top_middle_1:10,grass_top_right,-:8,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:36",
                "-:40,grass_top_left,grass_overlap_left,earth_edge_middle_1:4,earth_edge_middle_2:2,earth_edge_right,-:6,earth_bottom_right,eartyh_bottom_middle:10,earth_bottom_left,-:9,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:35",
                "-:39,grass_top_left,grass_overlap_left,earth_edge_middle_1:6,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:27,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:34",
                "-:38,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,eartyh_bottom_middle:4,earth_bottom_middle_2,earth_edge_middle_2:2,earth_edge_right,-:28,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:33",
                "-:37,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_bottom_left,-:5,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:29,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:32",
                "-:36,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:6,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:30,earth_bottom_right,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:31",
                "-:35,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_right,-:6,earth_edge_left,earth_edge_middle_2:2,grass_top_middle_1:9,grass_top_right,-:3,grass_top_left,grass_top_middle_1:6,grass_top_right,-:10,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:31",
                "-:34,grass_top_left,grass_overlap_left,earth_edge_middle_1:4,earth_edge_right,-:4,grass_top_left,grass_top_middle_1,grass_overlap_left,earth_edge_middle_2:7,earth_edge_middle_1:4,earth_edge_right,-:3,earth_edge_left,earth_edge_middle_1:6,grass_overlap_right,grass_top_right,-:43",
                "-:34,earth_bottom_right,eartyh_bottom_middle:5,earth_bottom_left,-:4,earth_bottom_right,eartyh_bottom_middle:13,earth_bottom_left,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_middle_2,earth_edge_middle_2:2,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-:39,grass_top_left,grass_top_middle_1,grass_top_right",
                "-:2,grass_top_left,grass_top_middle_1,grass_top_right,-:61,earth_edge_left,earth_edge_middle_2:5,grass_overlap_right,grass_top_right,-:38,earth_edge_left,earth_edge_middle_2,earth_edge_right",
                "-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:61,earth_edge_left,earth_edge_middle_2:6,sand_overlap_right,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1,shell_sand_top_right,-:2,blank_sand_top_left,shell_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1:2,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2:2,shell_sand_middle_2,blank_sand_top_right,-:2,blank_sand_top_left,shell_sand_middle_2:2,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_2,earth_edge_right",
                "-:2,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-,blank_sand_top_left,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,shell_sand_top_right,-,shell_sand_top_left,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2,shell_sand_top_right,-,shell_sand_top_left,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_2,shell_sand_top_right,-:2,blank_sand_top_left,shell_sand_top_right,-:3,blank_sand_top_left,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_2,blank_sand_top_right,-:20,earth_edge_left,earth_edge_middle_2:13,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1:5,earth_edge_middle_2:11,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:7,earth_edge_right",
                "-:2,earth_edge_left,tile_ripple_middle:2,earth_edge_right,-,earth_edge_left,tile_ripple_middle:3,earth_edge_right,-,earth_edge_left,tile_ripple_middle:3,earth_edge_right,-,earth_edge_left,earth_edge_middle_2:12,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:3,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:20,earth_edge_left,tile_ripple_middle:13,earth_edge_right,-:2,earth_edge_left,tile_ripple_middle:19,earth_edge_right,-:2,earth_edge_left,tile_ripple_middle:7,earth_edge_right",
                "-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:12,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:20,tile_ripple_left,tile_ripple_middle:13,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:19,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:7,tile_ripple_right",
                "-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:12,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:20,tile_ripple_left,tile_ripple_middle:13,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:19,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:7,tile_ripple_right",
                "-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-,tile_ripple_left,tile_ripple_middle:12,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:20,tile_ripple_left,tile_ripple_middle:13,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:19,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:7,tile_ripple_right",
            },
            // level_23（24×16，水线行 13）
            new[]
            {
                "-:24",
                "-:24",
                "-:24",
                "-:24",
                "-:24",
                "-:24",
                "-:10,blank_sand_top_left,shell_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_top_right,-:9",
                "-:10,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:9",
                "-:7,blank_sand_top_left,shell_sand_middle_1,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_2:3,sand_overlap_right,shell_sand_middle_2,blank_sand_middle_1,shell_sand_top_right,-:6",
                "-:7,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1:5,earth_edge_right,-:6",
                "-:7,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right,-:6",
                "blank_sand_top_left,shell_sand_middle_1,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,sand_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2:3,earth_edge_middle_1:3,sand_overlap_right,blank_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2,blank_sand_top_right",
                "earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_middle_1:4,earth_edge_middle_2:2,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:22,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:22,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:22,tile_ripple_right",
            },
            // level_24（23×34，水线行 30）
            new[]
            {
                "-:23",
                "-:23",
                "-:23",
                "-:5,grass_top_single,-:11,grass_top_single,-:5",
                "-:5,earth_single_collum,-:11,earth_single_collum,-:5",
                "-:5,earth_single_collum,-:11,earth_single_collum,-:5",
                "-:5,earth_single_collum,-:11,earth_single_collum,-:5",
                "-:5,earth_single_collum,-:11,grass_overlap_right_edge,grass_top_right,-:4",
                "-:4,grass_top_left,grass_overlap_left_edge,-:11,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:3,grass_row_left,grass_top_middle_1:3,grass_row_right,-:3,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:4,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:11,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:11,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,grass_overlap_right,grass_top_right,-:2,grass_row_left,grass_top_middle_1:3,grass_row_right,-:3,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4,earth_edge_left,earth_edge_right,-:4",
                "-:3,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_bottom_left,-:10,earth_edge_left,earth_edge_right,-:4",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:11,earth_edge_left,grass_overlap_right,grass_top_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3,grass_row_left,grass_top_middle_1:3,grass_row_right,-:2,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:4,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:3,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:10,earth_bottom_right,earth_edge_middle_1:2,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:11,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:2,grass_top_left,grass_overlap_left,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:2,grass_row_left,grass_top_middle_1:3,grass_row_right,-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:2",
                "-,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_right,-:2,grass_top_left,grass_top_middle_1:3,grass_top_right,-:2,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:2",
                "-,earth_edge_left,earth_edge_middle_1:4,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-",
                "-,earth_edge_left,earth_edge_middle_1:4,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:4,earth_edge_right,-",
                "grass_top_left,grass_overlap_left,earth_edge_middle_1:4,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:4,earth_edge_right,-",
                "earth_edge_left,earth_edge_middle_1:5,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:4,grass_overlap_right,grass_top_right",
                "tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:3,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right",
            },
            // level_25（59×27，水线行 26）
            new[]
            {
                "-:59",
                "-:59",
                "-:59",
                "-:59",
                "-:24,crows_nest_2,crows_nest_1,-:33",
                "-:59",
                "-:59",
                "-:24,crows_nest_2,crows_nest_1,-:33",
                "-:59",
                "-:22,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:31",
                "-:59",
                "-:59",
                "-:59",
                "-:9,crows_nest_2,crows_nest_1,-:8,mast_end_left,mast_tile:4,mast_middle_2,mast_middle_1,mast_tile:4,mast_end_right,-:8,crows_nest_2,crows_nest_1,-:18",
                "-:59",
                "-:7,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:24,mast_end_left,mast_tile,mast_middle_2,mast_middle_1,mast_tile,mast_end_right,-:16",
                "-:59",
                "-:59",
                "-:17,mast_end_left,mast_tile:6,mast_middle_2,mast_middle_1,mast_tile:6,mast_end_right,-:26",
                "-:6,mast_end_left,mast_tile:2,mast_middle_2,mast_middle_1,mast_tile:2,mast_end_right,-:2,mast_end_left,mast_tile:7,mast_middle_2,mast_middle_1,mast_tile:7,mast_end_right,-:2,mast_end_left,mast_tile:2,mast_middle_2,mast_middle_1,mast_tile:2,mast_end_right,-:15",
                "-:59",
                "-:59",
                "-:59",
                "-:4,ship_right_facing_1,ship_right_facing_4_1,-:39,ship_left_side_shaded_4,ship_right_facing_3,ship_end_3:11,ship_end_2",
                "ship_end_1,ship_end_3:3,ship_right_facing_2,cannon_port_3,cannon_port_2:3,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_top_middle:2,cannon_port_2:4,ship_anchor_3,-:13",
                "-:4,ship_right_facing_4,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2:2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_tile_2,ship_tile_1,ship_anchor_4,-:13",
                "-:4,boat_ripple_1,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3:2,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_3,boat_ripple_2,boat_ripple_4,-:13",
            },
            // level_26（77×32，水线行 29）
            new[]
            {
                "-:77",
                "-:77",
                "-:77",
                "-:77",
                "-:77",
                "-:77",
                "-:37,grass_top_left,grass_top_right,-:38",
                "-:37,earth_edge_left,earth_edge_right,-:38",
                "-:36,grass_top_left,grass_overlap_left,earth_edge_right,-:38",
                "-:35,grass_top_left,grass_overlap_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:37",
                "-:35,earth_edge_left,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:26,grass_top_left,grass_top_right,-:8",
                "-:35,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:26,earth_edge_left,earth_edge_right,-:8",
                "-:9,grass_top_left,grass_top_middle_1,grass_top_right,-:22,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_1,grass_top_right,-:23,grass_top_left,grass_overlap_left,grass_overlap_right,grass_top_right,-:7",
                "-:9,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:22,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:5,earth_edge_right,-:23,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:7",
                "-:8,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:22,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:5,grass_overlap_right,grass_top_right,-:21,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_2,grass_top_right,-:5",
                "-:7,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:20,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2:3,earth_edge_right,-:21,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:5",
                "-:7,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:20,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:20,grass_top_left,grass_overlap_left,earth_edge_middle_2:5,earth_edge_right,-:5",
                "-:6,grass_top_left,grass_overlap_left,earth_edge_middle_2:4,earth_edge_right,-:20,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:20,earth_edge_left,earth_edge_middle_2:2,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:4",
                "-:6,earth_edge_left,earth_edge_middle_2:2,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2,grass_overlap_right,grass_top_middle_1,grass_top_right,-:17,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1,grass_overlap_right,grass_top_middle_2:2,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:19,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4",
                "-:5,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:17,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:5,earth_edge_middle_1:2,earth_edge_right,-:18,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:17,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1:4,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:18,earth_edge_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-:4",
                "-:4,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:14,grass_top_left,grass_top_middle_1,grass_overlap_left,earth_edge_middle_2:5,eartyh_bottom_middle:2,earth_edge_middle_2,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-:16,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:4,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-:3",
                "-:4,earth_edge_left,earth_edge_middle_2:10,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_2:6,earth_bottom_left,-:2,earth_bottom_right,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:16,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1:4,earth_edge_middle_2:3,earth_edge_right,-:3",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_2:4,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:16,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1:2,eartyh_bottom_middle:2,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_right,-:3",
                "-:3,grass_top_left,grass_overlap_left,earth_edge_middle_2:3,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:13,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:3,grass_overlap_right,grass_top_right,-:14,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_middle_1,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:2",
                "-:3,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:11,shell_sand_top_left,shell_sand_middle_1,sand_overlap_left,earth_edge_middle_2:5,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:3,earth_edge_middle_2,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1:2,earth_edge_right,-:2",
                "-,grass_top_left,grass_top_middle_2,grass_overlap_left,earth_edge_middle_2:4,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:4,sand_overlap_right,blank_sand_middle_2,shell_sand_top_right,-:9,earth_edge_left,earth_edge_middle_2:7,grass_overlap_right,grass_top_middle_1,grass_top_middle_2:2,grass_top_middle_1,grass_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2,sand_overlap_right,shell_sand_middle_1,shell_sand_top_right,-:10,shell_sand_top_left,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_2:3,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:2,grass_overlap_right,grass_top_middle_1,grass_top_right",
                "shell_sand_top_left,sand_overlap_left,earth_edge_middle_2:16,earth_edge_right,-:7,shell_sand_top_left,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_2:9,earth_edge_middle_1:7,earth_edge_middle_2:3,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_2:5,earth_edge_middle_1:5,earth_edge_middle_2:3,earth_edge_middle_1:3,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,earth_edge_left,earth_edge_middle_2:21,earth_edge_right,-:10,earth_edge_left,earth_edge_middle_2:16,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:10,tile_ripple_left,tile_ripple_middle:16,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:10,tile_ripple_left,tile_ripple_middle:16,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:17,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:10,tile_ripple_left,tile_ripple_middle:16,tile_ripple_right",
            },
            // level_27（21×20，水线行 19）
            new[]
            {
                "-:21",
                "-:21",
                "-:21",
                "-:21",
                "grass_top_left,grass_top_middle_1:2,grass_top_right,-:4,grass_top_left,grass_top_middle_1:3,grass_top_right,-:4,grass_top_left,grass_top_middle_1:2,grass_top_right",
                "earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4,earth_bottom_right,eartyh_bottom_middle,earth_edge_middle_2,eartyh_bottom_middle,earth_bottom_left,-:4,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_edge_right",
                "earth_edge_left,eartyh_bottom_middle:2,earth_bottom_left,-:6,earth_single_collum,-:6,earth_bottom_right,eartyh_bottom_middle:2,earth_edge_right",
                "earth_bottom_single,-:9,earth_single_collum,-:9,earth_bottom_single",
                "-:10,earth_bottom_single,-:10",
                "-:5,grass_top_left,grass_top_middle_1,grass_top_right,-:5,grass_top_left,grass_top_middle_1,grass_top_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:5,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:5",
                "-:5,earth_bottom_right,earth_edge_middle_2,earth_bottom_left,-:5,earth_bottom_right,earth_edge_middle_2,earth_bottom_left,-:5",
                "grass_top_single,-:5,earth_bottom_single,-:7,earth_bottom_single,-:5,grass_top_single",
                "grass_overlap_right_edge,grass_top_middle_2:2,grass_top_right,-:5,grass_top_left,grass_top_middle_1,grass_top_right,-:5,grass_top_left,grass_top_middle_1:2,grass_overlap_left_edge",
                "earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:5,earth_edge_left,earth_edge_middle_1:2,earth_edge_right",
                "earth_bottom_right,eartyh_bottom_middle,earth_edge_middle_1,earth_bottom_left,-:5,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:5,earth_bottom_right,eartyh_bottom_middle,earth_edge_middle_1,earth_bottom_left",
                "-:2,earth_bottom_single,-:16,earth_bottom_single,-",
                "-:21",
                "-:21",
                "-:21",
            },
            // level_28（44×20，水线行 17）
            new[]
            {
                "-:44",
                "-:44",
                "-:44",
                "-:44",
                "-:44",
                "-:44",
                "-:10,grass_top_left,grass_top_middle_1,grass_top_right,-:2,grass_top_left,grass_top_middle_1,grass_top_right,-:9,grass_top_left,grass_top_middle_1,grass_top_right,-:2,grass_top_left,grass_top_middle_2,grass_top_right,-:9",
                "-:10,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:9,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:9",
                "-:10,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:2,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:2,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9",
                "-:44",
                "-:4,grass_top_left,grass_top_middle_1,grass_top_right,-:14,grass_top_left,grass_top_middle_1,grass_top_right,-:13,grass_top_left,grass_top_middle_2,grass_top_right,-:4",
                "-:4,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:14,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:13,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:4",
                "-:44",
                "-:44",
                "-:44",
                "blank_sand_top_left,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1:6,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1:2,blank_sand_middle_2:3,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_2:2,blank_sand_middle_1,shell_sand_middle_2:2,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1:2,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1:5,blank_sand_middle_2,shell_sand_top_right",
                "earth_edge_left,earth_edge_middle_1:42,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:42,tile_ripple_right",
            },
            // level_29（44×26，水线行 23）
            new[]
            {
                "-:44",
                "-:34,grass_top_left,grass_top_middle_1:2,grass_top_right,-:6",
                "-:18,grass_top_left,grass_top_middle_1,grass_top_right,-:13,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:6",
                "-:18,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:23",
                "-:5,single_grass_2,-:24,single_grass_2,-:10,grass_top_left,grass_top_right,-",
                "-:41,earth_bottom_right,earth_bottom_left,-",
                "-:11,single_grass_1,-:32",
                "grass_top_left,grass_top_middle_1:2,grass_top_right,-:13,grass_top_left,grass_top_middle_2,grass_top_middle_1:2,grass_top_middle_2:2,grass_top_middle_1,grass_top_middle_2,grass_top_middle_1:2,grass_top_right,-:16",
                "earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:12,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:15",
                "-:16,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_1:3,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_right,-:9,grass_top_left,grass_top_middle_2:2,grass_top_right,-:2",
                "-:16,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:9,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:2",
                "-:11,grass_top_left,grass_top_middle_1,grass_top_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:2,grass_top_left,grass_top_right,-:11",
                "-:11,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:2,earth_bottom_right,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,earth_bottom_left,-:2,earth_edge_left,grass_overlap_right,grass_top_right,-:10",
                "-:17,earth_bottom_right,earth_bottom_middle_2,earth_edge_middle_1,eartyh_bottom_middle,earth_bottom_middle_2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_middle_2,earth_edge_middle_1,earth_bottom_middle_3,earth_bottom_left,-:3,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:10",
                "-:19,earth_bottom_single,-:2,earth_bottom_single,-:2,earth_bottom_single,-:18",
                "-:6,grass_top_single,-:30,grass_top_single,-:5,single_grass_2",
                "-:5,grass_top_left,grass_overlap_both,grass_top_middle_1:2,grass_top_right,-:9,grass_top_left,grass_top_middle_2,grass_top_middle_1:2,grass_top_middle_2,grass_top_middle_1,grass_top_right,-:8,grass_top_left,grass_top_middle_1:2,grass_overlap_both,grass_top_right,-:5",
                "-:5,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:9,earth_bottom_right,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2,earth_bottom_left,-:8,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:5",
                "-:15,grass_top_left,grass_top_middle_1,grass_top_right,-:2,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:2,grass_top_left,grass_top_middle_2,grass_top_right,-:14",
                "single_grass_2,-:14,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:9,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:14",
                "-:44",
                "-:44",
                "-:10,shell_sand_top_left,shell_sand_top_right,-:7,shell_sand_top_left,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,shell_sand_top_right,-:8,shell_sand_top_left,blank_sand_top_right,-:8",
                "-:10,tile_ripple_left,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:8,tile_ripple_left,tile_ripple_right,-:8",
                "-:10,tile_ripple_left,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:8,tile_ripple_left,tile_ripple_right,-:8",
                "-:10,tile_ripple_left,tile_ripple_right,-:7,tile_ripple_left,tile_ripple_middle:5,tile_ripple_right,-:8,tile_ripple_left,tile_ripple_right,-:8",
            },
            // level_30（90×12，水线行 9）
            new[]
            {
                "-:90",
                "-:90",
                "-:90",
                "-:90",
                "-:90",
                "-:90",
                "-:90",
                "-:90",
                "grass_top_left,grass_top_middle_1:4,grass_top_right,-:3,grass_top_left,grass_top_right,-:2,grass_top_left,grass_top_middle_1:2,grass_top_right,-:5,grass_top_left,grass_top_right,-:4,shell_sand_top_left,blank_sand_middle_1,blank_sand_top_right,-,grass_top_left,grass_top_right,-:2,grass_top_left,grass_top_right,-:3,blank_sand_top_left,blank_sand_middle_1,shell_sand_top_right,-:5,grass_top_left,grass_top_middle_1,grass_top_right,-:2,shell_sand_top_left,shell_sand_middle_2:2,blank_sand_top_right,-,blank_sand_top_left,blank_sand_top_right,-:5,grass_top_left,grass_top_middle_1:2,grass_top_right,-:3,grass_top_left,grass_top_middle_1:2,grass_top_right,-:2,shell_sand_top_left,blank_sand_middle_2,shell_sand_middle_1,shell_sand_top_right,-:3,grass_top_left,grass_top_middle_2:2,grass_top_right",
                "tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_right,-:4,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_right,-:4,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:4,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_right,-:4,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-,tile_ripple_left,tile_ripple_right,-:5,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right,-:3,tile_ripple_left,tile_ripple_middle:2,tile_ripple_right",
            },
            // level_31（48×30，水线行 27）
            new[]
            {
                "-:48",
                "-:48",
                "-:48",
                "-:48",
                "-:5,grass_top_left,grass_top_middle_2:2,grass_top_right,-:30,grass_top_left,grass_top_middle_2:2,grass_top_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:30,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:12,grass_top_left,grass_top_middle_1:2,grass_top_right,-:14,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:11,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4,grass_top_left,grass_top_middle_1,grass_top_right,-:4,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:7,grass_top_left,grass_top_middle_1,grass_top_right,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:14,grass_top_left,grass_overlap_left,earth_edge_middle_2,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:5,earth_edge_left,earth_edge_middle_2:2,earth_edge_right,-:3,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:14,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,grass_top_left,grass_overlap_left,earth_edge_middle_2:2,earth_edge_right,-:3,earth_bottom_right,eartyh_bottom_middle:2,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1,grass_top_right,-:12,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:4,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:6,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:7,grass_top_left,grass_top_right,-:11,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:3,earth_edge_right,-:14,grass_top_left,grass_top_middle_1:2,grass_overlap_left,earth_edge_right,-:11,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:3,grass_overlap_right,grass_top_right,-:13,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:10,grass_top_left,grass_overlap_left,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:28,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:6,grass_top_left,grass_top_right,-:20,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,earth_edge_right,-:5",
                "-:4,earth_edge_left,earth_edge_middle_2:4,earth_edge_right,-:5,blank_sand_top_left,sand_overlap_left,sand_overlap_right,shell_sand_middle_2,blank_sand_top_right,-:18,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:2,grass_overlap_right,grass_top_right,-:4",
                "-:3,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2:4,earth_edge_right,-:5,earth_bottom_right,eartyh_bottom_middle:3,earth_bottom_left,-:8,grass_top_left,grass_top_right,-:8,earth_edge_left,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:4",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:4,sand_overlap_right,blank_sand_top_right,-:16,blank_sand_top_left,sand_overlap_left,sand_overlap_right,shell_sand_top_right,-:6,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2,earth_edge_middle_1:3,earth_edge_right,-:4",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:5,earth_edge_right,-:16,earth_bottom_right,eartyh_bottom_middle:2,earth_bottom_left,-:6,earth_edge_left,earth_edge_middle_2:2,earth_edge_middle_1:3,earth_edge_right,-:4",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:5,sand_overlap_right,blank_sand_top_right,-:24,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2:2,earth_edge_middle_1:3,sand_overlap_right,blank_sand_top_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:6,earth_edge_right,-:24,earth_edge_left,earth_edge_middle_2:4,earth_edge_middle_1:3,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:6,sand_overlap_right,blank_sand_top_right,-:22,blank_sand_top_left,sand_overlap_left,earth_edge_middle_2:4,earth_edge_middle_1:3,earth_edge_right,-:3",
                "-,blank_sand_top_left,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_1,earth_edge_middle_2:7,sand_overlap_right,blank_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_1,shell_sand_top_right,-:4,blank_sand_top_left,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_2:5,earth_edge_middle_1:3,sand_overlap_right,blank_sand_middle_2,shell_sand_middle_2,blank_sand_top_right",
                "shell_sand_top_left,sand_overlap_left,earth_edge_middle_1:3,earth_edge_middle_2:16,sand_overlap_right,blank_sand_top_right,-:2,shell_sand_top_left,sand_overlap_left,earth_edge_middle_2:14,earth_edge_middle_1:6,earth_edge_right",
                "earth_edge_left,earth_edge_middle_1:4,earth_edge_middle_2:17,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_2:14,earth_edge_middle_1:7,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:21,tile_ripple_right,-:2,tile_ripple_left,tile_ripple_middle:21,tile_ripple_right",
            },
            // level_32（86×20，水线行 17）
            new[]
            {
                "-:86",
                "-:86",
                "-:72,grass_top_left,grass_top_middle_1,grass_top_right,-:11",
                "-:8,grass_top_left,grass_top_middle_1,grass_top_right,-:60,grass_top_left,grass_overlap_left,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:10",
                "-:7,grass_top_left,grass_overlap_left,earth_edge_middle_1,grass_overlap_right,grass_top_right,-:59,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:10",
                "-:7,earth_edge_left,earth_edge_middle_1:3,earth_edge_right,-:59,earth_edge_left,earth_edge_middle_1,eartyh_bottom_middle:2,grass_overlap_right,grass_top_right,-:9",
                "-:6,grass_top_left,grass_overlap_left,eartyh_bottom_middle:2,earth_edge_middle_1,earth_edge_right,-:59,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:9",
                "-:6,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:59,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:9",
                "-:6,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:59,earth_edge_left,grass_overlap_right,grass_row_middle:2,grass_overlap_left,earth_edge_right,-:9",
                "-:6,earth_edge_left,grass_overlap_right,grass_row_middle:2,grass_overlap_left,earth_edge_right,-:8,grass_top_left,grass_top_middle_1,grass_top_right,-:19,grass_top_left,grass_top_middle_1,grass_top_right,-:19,grass_top_left,grass_top_right,-:5,earth_edge_left,earth_edge_right,-:2,earth_edge_left,grass_overlap_right,grass_top_right,-:8",
                "-:6,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_right,-:8,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:19,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:19,earth_edge_left,earth_edge_right,-:5,earth_edge_left,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:8",
                "-:5,grass_top_left,grass_overlap_left,earth_edge_right,-:2,earth_edge_left,sand_overlap_right,blank_sand_top_right,-:7,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:19,earth_edge_left,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1,grass_top_right,-:17,earth_edge_left,grass_overlap_right,grass_top_middle_2,grass_top_right,-:2,grass_top_left,grass_overlap_left,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,earth_edge_right,-:8",
                "-:5,earth_edge_left,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1:2,grass_overlap_left,earth_edge_middle_1,sand_overlap_right,shell_sand_top_right,-:6,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:18,grass_top_left,grass_overlap_left,earth_edge_middle_1:3,earth_edge_right,-:17,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:2,earth_edge_left,earth_edge_middle_1:6,sand_overlap_right,blank_sand_top_right,-:7",
                "-:5,earth_edge_left,earth_edge_middle_1:7,earth_edge_right,-:4,blank_sand_top_left,shell_sand_middle_2,sand_overlap_left,earth_edge_middle_1,sand_overlap_right,blank_sand_middle_2,blank_sand_top_right,-:7,blank_sand_top_left,blank_sand_middle_1,shell_sand_middle_2:2,blank_sand_middle_1,-:4,earth_edge_left,earth_edge_middle_1:2,tile_ripple_middle,earth_edge_middle_1,earth_edge_right,-:12,blank_sand_top_left,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_2:2,sand_overlap_left,earth_edge_middle_1:2,sand_overlap_right,shell_sand_middle_2,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_1:8,-:7",
                "-:5,earth_edge_left,earth_edge_middle_1:7,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_1:5,earth_edge_right,-:6,shell_sand_top_left,sand_overlap_left,earth_edge_middle_1:3,sand_overlap_right,shell_sand_top_right,-:3,earth_edge_left,earth_edge_middle_1:4,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1:2,shell_sand_top_right,-:4,blank_sand_top_left,blank_sand_middle_2:2,sand_overlap_left,earth_edge_middle_1:18,sand_overlap_right,shell_sand_top_right,-:6",
                "shell_sand_top_left,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,sand_overlap_left,earth_edge_middle_1:18,sand_overlap_right,blank_sand_middle_1:2,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_1:2,sand_overlap_left,earth_edge_middle_1:5,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_1,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_1:9,sand_overlap_right,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_1,sand_overlap_left,earth_edge_middle_1:22,sand_overlap_right,blank_sand_middle_1:2,shell_sand_middle_1,shell_sand_middle_2,blank_sand_middle_1,blank_sand_top_right",
                "earth_edge_left,earth_edge_middle_1:84,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:84,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:84,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:84,tile_ripple_right",
            },
            // level_33（41×33，水线行 30）
            new[]
            {
                "-:41",
                "-:41",
                "-:41",
                "-:41",
                "-:41",
                "-:4,grass_top_left,grass_top_right,-:29,grass_top_left,grass_top_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:29,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:29,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:29,earth_edge_left,earth_edge_right,-:4",
                "-:4,earth_edge_left,earth_edge_right,-:29,earth_edge_left,grass_overlap_right,grass_top_right,-:3",
                "-:3,grass_top_left,grass_overlap_left,earth_edge_right,-:29,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:29,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,grass_overlap_right,grass_top_middle_1,grass_top_right,-:4,grass_top_left,grass_top_middle_1:15,grass_top_right,-:5,grass_top_left,grass_overlap_left,earth_edge_middle_1,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1:2,earth_edge_middle_2,earth_edge_right,-:4,earth_bottom_right,eartyh_bottom_middle:6,earth_edge_middle_2:3,eartyh_bottom_middle:6,earth_bottom_left,-:5,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-:11,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:12,earth_edge_left,earth_edge_middle_1:2,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_2:3,grass_overlap_right,grass_top_middle_2:2,grass_top_right,-:8,earth_edge_left,earth_edge_middle_2,earth_edge_right,-:8,grass_top_left,grass_top_middle_2:3,grass_overlap_left,earth_edge_middle_1,earth_edge_middle_2,earth_edge_right,-:3",
                "-:3,earth_edge_left,earth_edge_middle_2:6,earth_edge_right,-:8,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:8,earth_edge_left,earth_edge_middle_2:4,earth_edge_middle_1,earth_edge_middle_2,earth_edge_right,-:3",
                "-:2,grass_top_left,grass_overlap_left,earth_edge_middle_2:6,sand_overlap_right,blank_sand_middle_1:2,blank_sand_top_right,-:5,earth_edge_left,earth_edge_middle_1,earth_edge_right,-:5,blank_sand_top_left,blank_sand_middle_1:2,sand_overlap_left,earth_edge_middle_2:4,earth_edge_middle_1,earth_edge_middle_2,grass_overlap_right,grass_top_right,-:2",
                "-:2,earth_edge_left,earth_edge_middle_2:10,sand_overlap_right,blank_sand_middle_2:2,blank_sand_top_right,-:2,earth_bottom_right,eartyh_bottom_middle,earth_bottom_left,-:2,blank_sand_top_left,blank_sand_middle_2:2,sand_overlap_left,earth_edge_middle_1:4,earth_edge_middle_2:3,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-:2",
                "-:2,earth_edge_left,earth_edge_middle_2:6,eartyh_bottom_middle:7,earth_bottom_left,-:7,earth_bottom_right,eartyh_bottom_middle:7,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-:2",
                "-:2,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:23,earth_bottom_right,eartyh_bottom_middle,earth_edge_middle_2,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-:2",
                "-:2,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:4,earth_edge_right,-:25,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-:2",
                "-:2,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:4,earth_edge_right,-:4,blank_sand_top_left,blank_sand_middle_1:2,blank_sand_middle_2,blank_sand_middle_1:8,blank_sand_middle_2:2,blank_sand_middle_1,blank_sand_middle_2,blank_sand_top_right,-:4,earth_bottom_right,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-:2",
                "-:2,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:4,earth_bottom_right,eartyh_bottom_middle:5,earth_edge_middle_2:10,earth_edge_right,-:5,earth_edge_left,earth_edge_middle_2:2,grass_overlap_right,grass_top_right,-",
                "-:2,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:10,earth_bottom_right,eartyh_bottom_middle:3,earth_edge_middle_2:6,sand_overlap_right,blank_sand_middle_1,blank_sand_top_right,-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-",
                "-:2,earth_edge_left,earth_edge_middle_2:5,earth_edge_right,-:14,earth_bottom_right,eartyh_bottom_middle:7,earth_bottom_left,-:3,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-",
                "-,grass_top_left,grass_overlap_left,earth_edge_middle_2:5,sand_overlap_right,blank_sand_middle_2,blank_sand_middle_1,shell_sand_top_right,-:23,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,earth_edge_right,-",
                "-,earth_edge_left,earth_edge_middle_2:9,earth_edge_right,-:23,earth_edge_left,earth_edge_middle_1,earth_edge_middle_2:2,grass_overlap_right,grass_top_right",
                "-,earth_edge_left,earth_edge_middle_2:9,sand_overlap_right,blank_sand_middle_1:2,blank_sand_middle_2,blank_sand_middle_1,blank_sand_middle_2,shell_sand_middle_2,blank_sand_middle_1:5,blank_sand_middle_2,blank_sand_middle_1,shell_sand_middle_2,shell_sand_middle_1,blank_sand_middle_2,blank_sand_middle_1:2,blank_sand_middle_2,blank_sand_middle_1:4,sand_overlap_left,earth_edge_middle_1,earth_edge_middle_2:3,earth_edge_right",
                "grass_top_left,grass_overlap_left,earth_edge_middle_2:4,earth_edge_middle_1:31,earth_edge_middle_2:3,earth_edge_right",
                "tile_ripple_left,tile_ripple_middle:39,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:39,tile_ripple_right",
                "tile_ripple_left,tile_ripple_middle:39,tile_ripple_right",
            },
        };

        static readonly LevelTileMapData[] Cache = new LevelTileMapData[LevelCount + 1];

        /// <summary>该关号是否有原版行串（1–33 全部有）。</summary>
        public static bool Has(int levelNumber)
        {
            return levelNumber >= 1 && levelNumber <= LevelCount;
        }

        /// <summary>原版行数（= 关卡 height）；未收录返回 0。</summary>
        public static int HeightOf(int levelNumber)
        {
            return Has(levelNumber) ? RowsByLevel[levelNumber - 1].Length : 0;
        }

        /// <summary>原版宽度（= 关卡 width，由首行展开校验）；未收录返回 0。</summary>
        public static int WidthOf(int levelNumber)
        {
            if (!Has(levelNumber))
                return 0;

            string[] rows = RowsByLevel[levelNumber - 1];
            return rows.Length == 0 ? 0 : CountCells(rows[0]);
        }

        /// <summary>该关行串原文（返回内部数组，只读用途，请勿修改）。</summary>
        public static string[] RawRows(int levelNumber)
        {
            return Has(levelNumber) ? RowsByLevel[levelNumber - 1] : null;
        }

        /// <summary>解析该关行串（首次调用后缓存）。未收录返回 <c>null</c>。</summary>
        public static LevelTileMapData Parse(int levelNumber)
        {
            if (!Has(levelNumber))
                return null;

            if (Cache[levelNumber] != null)
                return Cache[levelNumber];

            string[] rows = RowsByLevel[levelNumber - 1];
            int height = rows.Length;
            int width = height == 0 ? 0 : CountCells(rows[0]);

            var tiles = new string[width * height];
            var cells = new List<LevelTileCell>();
            var solid = new List<LevelTileCell>();
            var ripple = new List<LevelTileCell>();

            for (int rowY = 0; rowY < height; rowY++)
            {
                List<string> expanded = ExpandRow(rows[rowY]);
                while (expanded.Count < width)
                    expanded.Add(EmptyTile);   // 兜底：行串自洽性由测试断言，这里只保证不越界

                for (int x = 0; x < width; x++)
                {
                    string name = expanded[x];
                    tiles[x + rowY * width] = name;

                    if (name == EmptyTile)
                        continue;

                    LevelTileFamily family = FamilyOf(name);
                    var cell = new LevelTileCell(x, rowY, name, family);
                    cells.Add(cell);
                    if (family == LevelTileFamily.Water)
                        ripple.Add(cell);
                    else
                        solid.Add(cell);
                }
            }

            var data = new LevelTileMapData(levelNumber, width, height, tiles, cells, solid, ripple);
            Cache[levelNumber] = data;
            return data;
        }

        /// <summary>解析该关行串；未收录返回 <c>false</c>。</summary>
        public static bool TryParse(int levelNumber, out LevelTileMapData map)
        {
            map = Parse(levelNumber);
            return map != null;
        }

        // ------------------------------------------------------------------
        // 行串解析（运行长度编码：<c>name:count</c>，count 省略 = 1）
        // ------------------------------------------------------------------

        /// <summary>把一个 <c>&lt;row&gt;</c> 行串展开成逐格瓦片名。</summary>
        public static List<string> ExpandRow(string row)
        {
            var list = new List<string>(64);
            if (string.IsNullOrEmpty(row))
                return list;

            int i = 0;
            while (i < row.Length)
            {
                if (row[i] == ',')
                {
                    i++;
                    continue;
                }

                int j = i;
                while (j < row.Length && row[j] != ',')
                    j++;

                string part = row.Substring(i, j - i);
                i = j;

                if (part.Length == 0)
                    continue;

                string name = part;
                int count = 1;

                int colon = part.LastIndexOf(':');
                if (colon > 0)
                {
                    int parsed;
                    if (int.TryParse(part.Substring(colon + 1), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out parsed))
                    {
                        name = part.Substring(0, colon);
                        count = parsed;
                    }
                }

                for (int k = 0; k < count; k++)
                    list.Add(name);
            }

            return list;
        }

        /// <summary>行串展开后的格数（不分配 List 的快速版，供宽度校验用）。</summary>
        public static int CountCells(string row)
        {
            if (string.IsNullOrEmpty(row))
                return 0;

            int total = 0;
            int i = 0;
            while (i < row.Length)
            {
                if (row[i] == ',')
                {
                    i++;
                    continue;
                }

                int j = i;
                while (j < row.Length && row[j] != ',')
                    j++;

                string part = row.Substring(i, j - i);
                i = j;

                if (part.Length == 0)
                    continue;

                int count = 1;
                int colon = part.LastIndexOf(':');
                if (colon > 0)
                {
                    int parsed;
                    if (int.TryParse(part.Substring(colon + 1), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out parsed))
                        count = parsed;
                }

                total += count;
            }

            return total;
        }

        // ------------------------------------------------------------------
        // 瓦片语义（穷举表见类头）
        // ------------------------------------------------------------------

        /// <summary>瓦片名 → 语义族（未登记 → <see cref="LevelTileFamily.Unknown"/>）。</summary>
        public static LevelTileFamily FamilyOf(string tileName)
        {
            if (string.IsNullOrEmpty(tileName) || tileName == EmptyTile)
                return LevelTileFamily.Water;

            if (tileName.StartsWith("tile_ripple") || tileName.StartsWith("boat_ripple"))
                return LevelTileFamily.Water;

            if (tileName.StartsWith("grass") || tileName.StartsWith("single_grass"))
                return LevelTileFamily.Grass;

            // 原版 XML 里的拼写错误：eartyh_bottom_middle（earth 写成 eartyh），照原样收录。
            if (tileName.StartsWith("earth") || tileName.StartsWith("eartyh"))
                return LevelTileFamily.Earth;

            if (tileName.StartsWith("blank_sand") || tileName.StartsWith("shell_sand")
                || tileName.StartsWith("sand_overlap"))
                return LevelTileFamily.Sand;

            if (tileName.StartsWith("ship") || tileName.StartsWith("cannon_port"))
                return LevelTileFamily.Ship;

            if (tileName.StartsWith("mast"))
                return LevelTileFamily.Mast;

            if (tileName.StartsWith("crows_nest"))
                return LevelTileFamily.CrowNest;

            return LevelTileFamily.Unknown;
        }

        /// <summary>是否是可站面瓦片（非空、非波纹）。</summary>
        public static bool IsSolidTile(string tileName)
        {
            return FamilyOf(tileName) != LevelTileFamily.Water;
        }

        /// <summary>是否是水（空或波纹）。</summary>
        public static bool IsWaterTile(string tileName)
        {
            return FamilyOf(tileName) == LevelTileFamily.Water;
        }

        /// <summary>是否是船体语汇（船体 / 桅 / 桅盘）——决定岛簇的伪装类型（Ship 族）。</summary>
        public static bool IsShipTile(string tileName)
        {
            LevelTileFamily f = FamilyOf(tileName);
            return f == LevelTileFamily.Ship || f == LevelTileFamily.Mast || f == LevelTileFamily.CrowNest;
        }

        /// <summary>语义族的中文名（报告 / 失败信息用）。</summary>
        public static string FamilyName(LevelTileFamily family)
        {
            switch (family)
            {
                case LevelTileFamily.Water: return "水";
                case LevelTileFamily.Grass: return "草地";
                case LevelTileFamily.Earth: return "土体";
                case LevelTileFamily.Sand: return "沙洲";
                case LevelTileFamily.Ship: return "船体";
                case LevelTileFamily.Mast: return "桅";
                case LevelTileFamily.CrowNest: return "桅盘";
                default: return "待定";
            }
        }
    }
}
