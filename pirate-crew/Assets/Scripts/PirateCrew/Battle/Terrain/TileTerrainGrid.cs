using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 瓦片地形网格（纯 C#，不引用 MonoBehaviour / GameObject，可在无头验证台断言）。
    ///
    /// ==================================================================
    /// 【3D 化语义推导（两份基准各出了什么）】
    /// ==================================================================
    /// 这份地形是「Flash 出内容 + Godot 出空间结构」的合成，两份基准各自贡献如下：
    ///
    /// 一、<b>Flash（逆向文档 §5.4 + 关卡 XML）出什么</b>
    ///   · 瓦片网格与「实心 / 空」判据：原版关卡 XML 的 <c>&lt;row&gt;</c> 行压缩串
    ///     （<c>tile:count</c>）给出每个 (gridX, gridY) 的瓦片名，<c>-</c> = 空、
    ///     <c>tile_ripple_*</c> / <c>boat_ripple_*</c> = 水（非实心），其余为陆地。
    ///   · 1 瓦片 = 32px（§5.4 的 <c>&gt;&gt;5</c>）；本工程沿用 1 瓦片 = 1 世界单位
    ///     （<see cref="LevelGeometry.PixelsPerUnit"/>）。
    ///   · 每列最上方的实心瓦片 = 该列**地表**（2D 里角色站的就是它）。本类用
    ///     <c>altitude = heightTiles − topSolidRow</c> 表示该地表在关卡里的高度。
    ///   · 空列（整列无水陆瓦片，如 level_1 的第 19/40 列）= 原版的水道。
    ///
    /// 二、<b>Godot 基准出什么</b>
    ///   · 竞技场是 <b>XZ 水平面</b>、重力沿 <b>-Y</b>、单位沿 Z 分路（见
    ///     <c>docs/M2-3D空间模型对齐.md</c> §1/§2）。故瓦片格 (gridX, gridY) 占世界 XZ 格
    ///     <c>(gridX, gridY)</c>，堆叠方向为 <b>+Y</b>。
    ///   · （Godot 版本身没有地形实现——<c>island_generator.gd</c> 是 20 行空桩，
    ///     <c>battle.tscn</c> 只有一块 50×50 的平地。它只贡献「XZ + 向上为正」这套空间约定。）
    ///
    /// 三、<b>冲突与取舍（本类最关键的推导，标注为「提案/待定」）</b>
    ///   · 冲突：原版 2D 里 <c>gridY</c> 是**高度轴**（越大越靠下），但本项目已按 Godot 基准把
    ///     <c>gridY</c> 重投影成**纵深轴 Z**（<c>docs/M2-3D空间模型对齐.md</c> §1）。
    ///     同一根轴不能既表示 Z 又表示高度，而文档要求「瓦片在 XZ 竞技场上构成墙体/高台」，
    ///     即必须有第二个高度自由度。因此我们**只把行序用于高度、不用于格位高度**：
    ///       – 格位 Z 仍 = gridY（平面图重投影，未改）；
    ///       – 格的**堆叠高度**由「该列的相对地表海拔」压缩得到（见下）。
    ///   · 高度归一化（<b>提案/待定</b>）：令 <c>altitude = heightTiles − topSolidRow</c>，
    ///     全图最低地表 <c>minAlt</c> 记 0，最高 <c>maxAlt</c> 映射到
    ///     <see cref="TerrainCatalog.MaxBlocksPerColumn"/> 块。于是「与最低地面同高」的列 = 平坦地面
    ///     （0 块），原版里越高的台子堆得越高，相邻列的高差即**墙体**立面。
    ///   · 竖直压缩（<b>提案/待定，可玩性优先</b>）：1 块 = 8px = 0.25 世界单位
    ///     （<see cref="TerrainCatalog.DefaultBlockWorldHeight"/>），最多 8 块 = 2.0 单位。
    ///     理由：Flash 的弹弓满速 20px/帧、抬升 0.7 时理论最高点 ≈ 2.05 单位（
    ///     <c>v=20/1.28=15.625</c>，竖直分量 <c>v·0.573</c>，<c>h=vy²/(2·19.53)≈2.05</c>），
    ///     若按原版 1 瓦片 = 1 单位直接堆到 8 单位，任何高台都**超出投掷上限**、AI/玩家都打不到，
    ///     关卡不可玩。压缩到 2.0 单位后高台仍构成遮挡/借墙弹的障碍，但可被抛越。
    ///   · 基础地面永远存在（本类只**叠加**抬升块，不挖洞）：空列/最低列都是平坦地面。
    ///     理由同样是可玩性——原版 level_1 有 2 条水道、level_4 有 10 列缺口，
    ///     若在 XZ 平面挖洞，作者写在这些列的出生点会直接落水（落水即死，§4.4），
    ///     与「旧场景直接 Play 不坏」冲突。
    ///
    /// 四、<b>破坏</b>：爆炸命中范围内每个实心格被整格摧毁（<see cref="DestroyInRadius"/>）；
    ///   另提供逐块递减的 <see cref="DestroyBlock"/> 供细粒度/测试使用。地形格被摧毁后回到
    ///   基础地面高度（0 块），小地图点阵随之由实心档降到空档（§8.1 两档 alpha）。
    /// ==================================================================
    /// </summary>
    public sealed class TileTerrainGrid
    {
        readonly int[] _blocks;   // 行主序 [gx + gy * WidthTiles]；0 = 基础地面（无抬升块）

        /// <summary>竞技场横向格数（= 关卡 widthTiles）。</summary>
        public int WidthTiles { get; }

        /// <summary>竞技场纵深格数（= 关卡 heightTiles）。</summary>
        public int DepthTiles { get; }

        /// <summary>单块世界高度（提案/待定，见类头；0.25 = 8px）。</summary>
        public float BlockWorldHeight { get; }

        /// <summary>
        /// 构造地形网格。
        /// </summary>
        /// <param name="widthTiles">横向格数（&gt; 0）。</param>
        /// <param name="depthTiles">纵深格数（&gt; 0）。</param>
        /// <param name="blocks">每格堆叠块数（长度须 = widthTiles × depthTiles，行主序）。
        /// 传 null 视为全 0（平坦地面）。</param>
        /// <param name="blockWorldHeight">单块世界高度（&lt;= 0 时用 <see cref="TerrainCatalog.DefaultBlockWorldHeight"/>）。</param>
        public TileTerrainGrid(int widthTiles, int depthTiles, int[] blocks, float blockWorldHeight)
        {
            WidthTiles = Mathf.Max(1, widthTiles);
            DepthTiles = Mathf.Max(1, depthTiles);
            BlockWorldHeight = blockWorldHeight > 0f ? blockWorldHeight : TerrainCatalog.DefaultBlockWorldHeight;

            int expected = WidthTiles * DepthTiles;
            _blocks = new int[expected];
            if (blocks != null)
            {
                int count = Mathf.Min(expected, blocks.Length);
                for (int i = 0; i < count; i++)
                    _blocks[i] = blocks[i] > 0 ? blocks[i] : 0;
            }
        }

        /// <summary>全平坦地面（无抬升块）的网格。</summary>
        public static TileTerrainGrid Flat(int widthTiles, int depthTiles)
        {
            return new TileTerrainGrid(widthTiles, depthTiles, null, 0f);
        }

        /// <summary>格索引（行主序）→ 横向格号。</summary>
        public int CellXOf(int index) => WidthTiles > 0 ? index % WidthTiles : 0;

        /// <summary>格索引（行主序）→ 纵深格号。</summary>
        public int CellYOf(int index) => WidthTiles > 0 ? index / WidthTiles : 0;

        /// <summary>格号 → 行主序索引；越界返回 -1。</summary>
        public int IndexOf(int gridX, int gridY)
        {
            if (gridX < 0 || gridY < 0 || gridX >= WidthTiles || gridY >= DepthTiles)
                return -1;
            return gridX + gridY * WidthTiles;
        }

        /// <summary>该格的堆叠块数；越界返回 0。</summary>
        public int BlocksAt(int gridX, int gridY)
        {
            int index = IndexOf(gridX, gridY);
            return index < 0 ? 0 : _blocks[index];
        }

        /// <summary>该格是否堆了抬升块（小地图「实心」判据）。</summary>
        public bool IsSolidAt(int gridX, int gridY) => BlocksAt(gridX, gridY) > 0;

        /// <summary>该格地表世界 Y（基础地面 + 堆叠高度）。</summary>
        public float SurfaceWorldY(int gridX, int gridY)
        {
            return LevelGeometry.GroundTopY + BlocksAt(gridX, gridY) * BlockWorldHeight;
        }

        /// <summary>世界 XZ 处的地表世界 Y；越出竞技场返回基础地面 <see cref="LevelGeometry.GroundTopY"/>。</summary>
        public float SurfaceWorldYAtWorld(float worldX, float worldZ)
        {
            int gx = Mathf.FloorToInt(worldX);
            int gy = Mathf.FloorToInt(worldZ);
            return SurfaceWorldY(gx, gy);
        }

        /// <summary>实心（有抬升块）格数量（调试/测试用）。</summary>
        public int SolidCellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _blocks.Length; i++)
                {
                    if (_blocks[i] > 0)
                        n++;
                }
                return n;
            }
        }

        /// <summary>堆叠块总数（调试/测试用）。</summary>
        public int TotalBlocks
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _blocks.Length; i++)
                    n += _blocks[i];
                return n;
            }
        }

        /// <summary>
        /// 逐块摧毁：该格减 1 块（到 0 为止）。
        /// </summary>
        /// <returns>本格本次确实减了 1 块返回 true；已是 0 或越界返回 false。</returns>
        public bool DestroyBlock(int gridX, int gridY)
        {
            int index = IndexOf(gridX, gridY);
            if (index < 0 || _blocks[index] <= 0)
                return false;

            _blocks[index]--;
            return true;
        }

        /// <summary>
        /// 爆炸破坏：整格摧毁半径内所有实心格（堆叠块清零），并把被摧毁格的索引写入
        /// <paramref name="destroyedCells"/>（供视图/小地图刷新）。
        ///
        /// 【判据】格心到爆心的 3D 距离 &lt;= <paramref name="radiusWorld"/>（与
        /// <see cref="Combat.ExplosionResolver"/> 的 3D 球口径一致；格心高度取当前堆叠高度的一半）。
        /// </summary>
        /// <returns>被整格摧毁的数量。</returns>
        public int DestroyInRadius(Vector3 centerWorld, float radiusWorld, List<int> destroyedCells)
        {
            if (radiusWorld <= 0f)
                return 0;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centerWorld.x - radiusWorld));
            int maxX = Mathf.Min(WidthTiles - 1, Mathf.FloorToInt(centerWorld.x + radiusWorld));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centerWorld.z - radiusWorld));
            int maxY = Mathf.Min(DepthTiles - 1, Mathf.FloorToInt(centerWorld.z + radiusWorld));

            float radiusSqr = radiusWorld * radiusWorld;
            int destroyed = 0;

            for (int gy = minY; gy <= maxY; gy++)
            {
                for (int gx = minX; gx <= maxX; gx++)
                {
                    int index = gx + gy * WidthTiles;
                    if (_blocks[index] <= 0)
                        continue;

                    var cellCenter = new Vector3(
                        gx + 0.5f,
                        LevelGeometry.GroundTopY + _blocks[index] * BlockWorldHeight * 0.5f,
                        gy + 0.5f);

                    if ((cellCenter - centerWorld).sqrMagnitude > radiusSqr)
                        continue;

                    _blocks[index] = 0;
                    destroyedCells?.Add(index);
                    destroyed++;
                }
            }

            return destroyed;
        }
    }
}
