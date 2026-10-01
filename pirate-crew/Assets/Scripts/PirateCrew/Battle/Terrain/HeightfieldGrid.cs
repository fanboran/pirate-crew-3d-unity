using UnityEngine;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 静态高度场网格（纯 C#，不引用 MonoBehaviour / GameObject，可在无头验证台断言）。
    ///
    /// ==================================================================
    /// 【3D 化语义（地形数据模型 / 空间约定）】
    /// ==================================================================
    /// 这份地形由**静态高度场**（内容：每格一个整数块数，给出该列地表抬升）与 **XZ 空间约定**
    /// （几何：平铺成世界 XZ 格、重力沿 -Y）两部分合成，各自贡献如下。
    /// 名字里的「格」只是**存储与换算单位**（1 格 = 2 世界单位），不是玩法：
    /// 一代「瓦片竞技场」已删除，本类不存瓦片名、不表达任何逐格玩法语义，
    /// 只回答「这一列的地表在哪」。
    ///
    /// 一、<b>高度场（内容）</b>
    ///   · 行主序整数块阵列：每格 (gridX, gridY) 存一个块数，0 = 基础地面，
    ///     正值 = 该列地表相对基础地面的抬升；「无地面」（水）由地面标记表达（见构造形态）。
    ///   · 1 格 = 2 世界单位
    ///     （<see cref="LevelGeometry.TileWorldSize"/>，故 1 单位 = 16px = <see cref="LevelGeometry.PixelsPerUnit"/>）。
    ///     格号 ↔ 世界坐标的换算**只走** <see cref="LevelGeometry.WorldToTileIndex"/> /
    ///     <see cref="LevelGeometry.TileCenterWorld"/>，不要在这里手搓 <c>+0.5f</c>。
    ///   · 每格块数 = 该列**地表**（单位站的就是它）。现役生产者是关卡资产高度场
    ///     （<c>LevelRasterFromAsset</c> 把米高度折成整数块）；海图站面走
    ///     <c>WorldMapRuntime.BuildTerrainGrid</c>，产出同一种列式形态。
    ///   · 空列（整列无地面）= 水道。
    ///
    /// 二、<b>XZ 空间约定（几何）</b>
    ///   · 竞技场是 <b>XZ 水平面</b>、重力沿 <b>-Y</b>、单位沿 Z 分路（见
    ///     <c>docs/设计/3D空间模型对齐.md</c> §1/§2）。格 (gx, gz) 占世界 XZ 格，堆叠方向为 <b>+Y</b>。
    ///
    /// 三、<b>行号 → 高度 / 纵深</b>
    ///   · <b>行号 <c>rowY</c> = 离水面的竖直高度</b>。每座岛（栅格里的 4 连通域）取自己最
    ///     上面一行地面格到水线的行数差，换算成该岛的悬浮基准高度——越高的岛，3D 里浮得越高。
    ///   · <b>纵深 <c>gridZ</c> = <c>rowY − 1</c></b>：岛在 3D 里占的 Z 范围就是它在栅格里占的行范围
    ///     （"土/岩行 = 岛体厚度"）。减 1 是为了让单位的 <c>(gridX, gridY)</c> 正好落在它脚下的
    ///     地面格上（栅格里对象 y 是"脚底行 − 1"），使 <c>BattleController</c> 的
    ///     <c>z = (gridY + 0.5) × TileWorldSize</c>
    ///     与 <see cref="SurfaceWorldY(int,int)"/> 同时命中同一格。
    ///   · <b>每格块高</b> = 簇基准（行号给出）+ 局部 1 块（栅格只说"这格是陆地"）。
        ///   · <b>{ 有地面 / 是水 } 的两种构造形态</b>：① <b>列式栅格</b>（<c>blocks</c> 全图铺底、
        ///     0 = 基础地面）——**关卡资产走的就是这一种**，海图的站面栅格与关卡资产的高度场在
        ///     资产里都只有这一份形态（见 <c>docs/技术/架构/关卡数据资产.md</c>）；
        ///     ② <b>平台簇模式</b>（构造函数收 <see cref="PlatformMap"/>，逐格带簇归属、
        ///     <c>Cluster &lt; 0</c> 的水格无地面无碰撞）——早期"悬空平台群浮在海面上"的形态，
        ///     现役内容已无生产者，保留只为合成/兜底地形与既有 AI 用例逐值不变。
        ///     为兼容旧查询，<see cref="SurfaceWorldY(int,int)"/> 对**场内水格**仍报基础地面高度，
        ///     但游戏性查询 <see cref="SurfaceWorldYAtWorld(float,float)"/> 对水格返回
        ///     <see cref="WaterVoidY"/>（水面以下的虚空哨兵），AI 投掷模拟据此判定「落水」。
        ///
        /// ==================================================================
        /// </summary>
        public sealed class HeightfieldGrid
        {
            readonly int[] _blocks;   // 行主序 [gx + gy * WidthTiles]；0 = 基础地面（无抬升块）
            readonly bool[] _ground;  // 该格是否有地面（false = 水）
            readonly int[] _cluster;  // 平台簇索引；-1 = 水 / 列式旧地形
            readonly int[] _baseBlocks; // 该格所属簇的基准块数（整簇抬高的"地板"）；0 = 无基准
            readonly bool _platformMode;   // true = 平台簇模式（水格无地面；块清零即水）
            readonly PlatformMap _platforms;

        /// <summary>竞技场横向格数（= 关卡 widthTiles）。</summary>
        public int WidthTiles { get; }

        /// <summary>竞技场纵深格数（= 关卡 heightTiles）。</summary>
        public int DepthTiles { get; }

        /// <summary>单块世界高度（提案/待定，见类头；0.5 = 8px，格 1→2 单位后随格放大）。</summary>
        public float BlockWorldHeight { get; }

        /// <summary>全平坦地面（无抬升块）的网格。</summary>
        public static HeightfieldGrid Flat(int widthTiles, int depthTiles)
        {
            return new HeightfieldGrid(widthTiles, depthTiles, null, 0f);
        }

        /// <summary>行主序索引 → 横向格号。</summary>
        public int CellXOf(int index) => WidthTiles > 0 ? index % WidthTiles : 0;

        /// <summary>行主序索引 → 纵深格号。</summary>
        public int CellYOf(int index) => WidthTiles > 0 ? index / WidthTiles : 0;

        /// <summary>
        /// 构造地形网格（列式旧地形模式）。
        /// </summary>
        /// <param name="widthTiles">横向格数（&gt; 0）。</param>
        /// <param name="depthTiles">纵深格数（&gt; 0）。</param>
        /// <param name="blocks">每格堆叠块数（长度须 = widthTiles × depthTiles，行主序）。
        /// 传 null 视为全 0（平坦地面）。</param>
        /// <param name="blockWorldHeight">单块世界高度（&lt;= 0 时用 <see cref="LevelGeometry.BlockWorldHeight"/>）。</param>
        public HeightfieldGrid(int widthTiles, int depthTiles, int[] blocks, float blockWorldHeight)
            : this(widthTiles, depthTiles, blocks, blockWorldHeight, null)
        {
        }

        /// <summary>
        /// 构造地形网格（平台簇模式）：地面/水由 <paramref name="platforms"/> 决定。
        /// 水格块高强制归零；列式旧地形传 <c>null</c>。
        /// </summary>
        public HeightfieldGrid(int widthTiles, int depthTiles, int[] blocks, float blockWorldHeight,
            PlatformMap platforms)
        {
            WidthTiles = Mathf.Max(1, widthTiles);
            DepthTiles = Mathf.Max(1, depthTiles);
            BlockWorldHeight = blockWorldHeight > 0f ? blockWorldHeight : LevelGeometry.BlockWorldHeight;

            int expected = WidthTiles * DepthTiles;
            _blocks = new int[expected];
            _ground = new bool[expected];
            _cluster = new int[expected];
            _baseBlocks = new int[expected];

            bool platformMode = platforms != null
                && platforms.WidthTiles == WidthTiles && platforms.DepthTiles == DepthTiles;
            _platformMode = platformMode;
            _platforms = platformMode ? platforms : null;

            for (int i = 0; i < expected; i++)
            {
                _cluster[i] = -1;

                if (platformMode)
                {
                    int cluster = platforms.CellCluster[i];
                    _cluster[i] = cluster;
                    if (cluster < 0)
                    {
                        // 水格：没有地面，块高强制 0。
                        _ground[i] = false;
                        _blocks[i] = 0;
                        continue;
                    }

                    _ground[i] = true;

                    // 【基准高度换算】CellBlocks 是**簇内局部**台阶高；这里加上该簇的整簇基准块数，
                    // 得到"从基础地面起算"的总块高。这样所有按 BlocksAt 工作的下游
                    // （BattleTerrainView 的碰撞方块、SurfaceWorldY、AI 地表查询、小地图）
                    // 都自动跟随悬浮基准高度，无需各自改口径。
                    int baseBlocks = platforms.Clusters[cluster].BaseBlocks;
                    _baseBlocks[i] = baseBlocks;
                    int local = platforms.CellBlocks[i] > 0 ? platforms.CellBlocks[i] : 0;
                    _blocks[i] = local + baseBlocks;
                    continue;
                }

                // 列式旧地形：全图有基础地面，blocks = 抬升块数。
                _ground[i] = true;
                _blocks[i] = blocks != null && i < blocks.Length && blocks[i] > 0 ? blocks[i] : 0;
            }
        }

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

        /// <summary>该格是否堆了抬升块（小地图「实心」判据）。水格恒为 false。</summary>
        public bool IsSolidAt(int gridX, int gridY) => BlocksAt(gridX, gridY) > 0;

        /// <summary>
        /// 该格是否有地面（可站、有碰撞）。平台簇模式下 = 「属于某簇且块高 &gt; 0」，
        /// 故平台被炸空（块归零）后该格变为水；列式旧地形恒为 true。
        /// </summary>
        public bool IsGroundAt(int gridX, int gridY)
        {
            int index = IndexOf(gridX, gridY);
            if (index < 0)
                return false;
            return _platformMode ? _cluster[index] >= 0 && _blocks[index] > 0 : _ground[index];
        }

        /// <summary>该格所属平台簇索引；水格 / 列式旧地形返回 -1。</summary>
        public int ClusterIndexOf(int gridX, int gridY)
        {
            int index = IndexOf(gridX, gridY);
            if (index < 0 || !_platformMode || !IsGroundAt(gridX, gridY))
                return -1;
            return _cluster[index];
        }

        /// <summary>平台簇数量（列式旧地形为 0）。</summary>
        public int ClusterCount => _platforms != null ? _platforms.Clusters.Count : 0;

        /// <summary>取平台簇描述；越界返回 default。</summary>
        public PlatformClusterInfo ClusterAt(int clusterIndex)
        {
            if (_platforms == null || clusterIndex < 0 || clusterIndex >= _platforms.Clusters.Count)
                return default;
            return _platforms.Clusters[clusterIndex];
        }

        /// <summary>是否平台簇模式（逐格水陆、簇基准高度生效）。列式旧地形为 false。</summary>
        public bool IsPlatformMode => _platformMode;

        /// <summary>
        /// 该格所属簇的基准块数（整簇"地板"）。水格 / 列式旧地形 = 0。
        /// 视觉壳与底部收形靠它区分"岛底平面"与"局部台阶"。
        /// </summary>
        public int BaseBlocksAt(int gridX, int gridY)
        {
            int index = IndexOf(gridX, gridY);
            return index < 0 ? 0 : _baseBlocks[index];
        }

        /// <summary>该格所属簇的基准高度（世界单位）；水格 / 列式旧地形 = 0。</summary>
        public float BaseWorldYAt(int gridX, int gridY)
        {
            return BaseBlocksAt(gridX, gridY) * BlockWorldHeight;
        }

        /// <summary>该簇的**最低**地表世界 Y（含基准高度）——岛底收形从这里往下走。</summary>
        public float ClusterSurfaceMinWorldY(int clusterIndex)
        {
            PlatformClusterInfo info = ClusterAt(clusterIndex);
            if (string.IsNullOrEmpty(info.Name))
                return LevelGeometry.GroundTopY;
            return LevelGeometry.GroundTopY + info.MinTotalBlocks * BlockWorldHeight;
        }

        /// <summary>
        /// 该格地表世界 Y（基础地面 + **总**堆叠高度，总高 = 簇基准块数 + 簇内局部台阶块数）。
        /// 【基准高度】用户裁决「每个空岛有不同悬浮基准高度」→ <see cref="PlatformClusterInfo.BaseHeight"/>
        /// 在构造时被折进块高（见构造函数的换算注释），故本函数、<see cref="BlocksAt"/>、
        /// 碰撞方块与视觉壳**共用同一个口径**，不存在"视觉浮起来、碰撞还在地面"的错位。
        /// 【兼容口径】平台模式的水格（块 0）这里返回基础地面 <see cref="LevelGeometry.GroundTopY"/>；
        /// 需要"水格无地表"的**游戏性**查询请用 <see cref="SurfaceWorldYAtWorld"/>。
        /// </summary>
        public float SurfaceWorldY(int gridX, int gridY)
        {
            return LevelGeometry.GroundTopY + BlocksAt(gridX, gridY) * BlockWorldHeight;
        }

        /// <summary>
        /// 水面以下的**虚空哨兵**：平台模式的水格没有地表，游戏性查询返回此值，
        /// 使落点/放置类查询能走到 <c>p.y &lt;= WaterSurfaceY</c> 的「落水」分支
        /// （不能返回 <see cref="LevelGeometry.WaterSurfaceY"/> 本身，
        /// 否则会与"落到地表"分支同时命中而不是判定落水）。余量随格世界尺寸（半格 = 1 单位；TileWorldSize=2）。
        /// </summary>
        public static float WaterVoidY => LevelGeometry.WaterSurfaceY - LevelGeometry.TileWorldSize * 0.5f;

        /// <summary>
        /// 世界 XZ 处的**游戏性**地表世界 Y：平台模式的水格返回 <see cref="WaterVoidY"/>（落水哨兵）；
        /// 越出竞技场返回基础地面（与旧口径一致）。
        /// </summary>
        public float SurfaceWorldYAtWorld(float worldX, float worldZ)
        {
            int gx = LevelGeometry.WorldToTileIndex(worldX);
            int gy = LevelGeometry.WorldToTileIndex(worldZ);
            int index = IndexOf(gx, gy);
            if (index < 0)
                return LevelGeometry.GroundTopY;
            if (_platformMode && !IsGroundAt(gx, gy))
                return WaterVoidY;
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
    }
}
