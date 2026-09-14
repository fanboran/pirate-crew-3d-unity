using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>地形视觉壳的可调参数（默认值 = 场景文档 §3.1 的【AI 提案】取值 + 用户裁决的厚底参数）。</summary>
    public struct IslandShellSettings
    {
        /// <summary>
        /// 顶面边缘倒角宽度（世界单位）。
        /// 【2026-09-14 用户裁决 1「顶面边缘圆润」：0.15 → 0.35（区间 0.3-0.5）】
        /// 【格 1→2 单位 ×2】0.35 → 0.7：倒角是"占一格的比例"，格放大后同比例即同观感。
        /// </summary>
        public float ChamferWidth;

        /// <summary>倒角竖直高度（45° 倒角时 = 宽度）。</summary>
        public float ChamferHeight;

        /// <summary>岩层侧面的分段数（2-3 段）。【依据场景文档 §3.1「分 2-3 段岩层」】</summary>
        public int SideLayers;

        /// <summary>岩层间水平凹缝的进深（世界单位）。【依据场景文档 §3.1「0.03-0.06」】</summary>
        public float LayerRecess;

        /// <summary>同列沿 Z 的侧面剪影扰动幅度（世界单位）。【依据场景文档 §3.1「±0.05-0.12」】</summary>
        public float BoundaryJitter;

        /// <summary>裙边下沿世界 Y：边界台地外侧下延过水面到此处。【依据场景文档 §3.1「y=-0.6」】</summary>
        public float SkirtBottomY;

        /// <summary>0 块列"潮沟"薄水膜的抬高量（避免与基础地面 z-fighting）。【AI 提案】</summary>
        public float LowPlateYOffset;

        /// <summary>
        /// 悬空平台**岛底平面**的下限世界 Y（岛体侧壁/收形锥的最低点不能低于它）。
        /// 【AI 提案】-2.3 = 水面（-0.4）以下 1.9，让贴水档（基准 0）的岛底略沉入水面
        /// 读作"礁/滩头"，而基准 ≥3 的岛底完全露出水面读作"悬浮"（格 ×2 后由 -1.15 乘 2）。
        /// </summary>
        public float UndersideBottomY;

        /// <summary>
        /// **岛体侧壁的下延厚度**（世界单位）：从该簇最低地表往下到"岛底平面"的距离。
        /// 【2026-09-14 用户裁决 1：厚重底部收形，取 2.4（任务区间 2-4 的中值）】
        /// 该厚度是"能看到厚实岛体"的关键——旧实现侧壁一路下延到基础地面（黄盒柱），
        /// 悬浮基准高度一加就变成"柱子"而不是"岛"。
        /// </summary>
        public float SideThickness;

        /// <summary>
        /// 岛底**收尖深度**（世界单位）：从岛底平面再往下锥形收尖到此。
        /// 【2026-09-14 用户裁决 1：底尖化。岛体总厚度 = <see cref="SideThickness"/> + 本值 ≈ 3.5】
        /// </summary>
        public float UndersideTaperDepth;

        /// <summary>岩锥底部的收尖半径比例（相对岛形轮廓环，0 = 收到一点）。【AI 提案】</summary>
        public float UndersideTipRatio;

        /// <summary>
        /// 收形环数（含岛形轮廓那一环；≥2）。环数越多收形越圆润、面数越多。
        /// 【AI 提案：4 环 = 直裙 + 两段锥 + 尖，低多边形下已足够圆润】
        /// </summary>
        public int UndersideRings;

        /// <summary>默认参数（场景文档 §3.1 取值 + 用户裁决的厚底/圆角参数）。</summary>
        public static IslandShellSettings Default
        {
            get
            {
                return new IslandShellSettings
                {
                    // 用户裁决 1：顶面边缘圆润，倒角 0.15 → 0.35（格 1→2 单位后再 ×2 = 0.7）。
                    ChamferWidth = 0.7f,
                    ChamferHeight = 0.7f,
                    SideLayers = 3,              // 段数（非距离量）不动
                    LayerRecess = 0.08f,         // 凹缝进深 ×2
                    BoundaryJitter = 0.16f,      // 剪影扰动 ×2
                    SkirtBottomY = -1.2f,        // 下沿世界 Y ×2（水面 -0.4 之下）
                    LowPlateYOffset = 0.012f,    // z-fighting 抬高量 ×2
                    UndersideBottomY = -2.3f,    // 岛底下限世界 Y ×2
                    SideThickness = 4.8f,        // 厚底收形厚度 ×2（用户裁决 1 的 2.4 随之 ×2）
                    UndersideTaperDepth = 2.2f,  // 收尖深度 ×2
                    UndersideTipRatio = 0.22f,   // 比例（无量纲）不动
                    UndersideRings = 4,          // 环数（非距离量）不动
                };
            }
        }
    }

    /// <summary>
    /// 海岛地块的**视觉壳**几何生成（纯 C#，无头可测）。
    ///
    /// 【它在整个地形分层里的位置】
    ///   碰撞层 = <see cref="BattleTerrainView"/> 每实心格一个 Cube + BoxCollider，本类**不碰**；
    ///   视觉层 = 本类生成的"台地壳"：顶面与该格 <c>SurfaceWorldY</c> 严格等高（偏差 ≤ ±0.02）、
    ///   边缘 0.35 宽 45° 倒角、侧面 3 段岩层带凹缝、同列沿 Z 的剪影扰动。
    ///   平台簇模式（悬浮岛）时侧壁下延到**岛底平面**（簇最低地表 − 4.8 单位、下限 y=-2.3；格 ×2 前为 −2.4/−1.15），
    ///   再由 <see cref="AddClusterUnderside"/> 的收形锥沿**不规则岛形轮廓**继续往下收尖
    ///   （用户裁决 1：厚重底部收形、悬空可见）。列式旧地形仍走"下延成裙边到 y=-0.6"的旧路径。
    ///   破坏流程不变：整格摧毁后该格不再参与建壳（见 <see cref="BattleTerrainView.ApplyDestruction"/>）。
    ///   （方案出处：`docs/场景设计-战斗竞技场.md` §3.1「视觉层与碰撞层解耦」与 §9.1。）
    ///
    /// 【为什么不改碰撞高度】单位出生高度叠在 <c>Terrain.SurfaceWorldY</c> 上
    ///   （<c>BattleController.cs:253-257</c>），任何抬高都会让单位悬空/陷地（场景文档 §9.1）。
    ///   本类的顶面就是该格地表，不加任何抬高。
    /// </summary>
    public static class IslandShellGeometry
    {
        /// <summary>
        /// 单格台地壳：倒角顶面 + 侧面岩层 + 剪影扰动 + 边界裙边。
        ///
        /// 【平台簇模式（悬浮岛）】侧壁不再一路下延到基础地面，而是下延到该簇的
        /// <b>岛底平面</b>（<see cref="IslandBottomWorldY"/> = 簇最低地表 − <see cref="IslandShellSettings.SideThickness"/>，
        /// 下限 <see cref="IslandShellSettings.UndersideBottomY"/>），再由
        /// <see cref="AddClusterUnderside"/> 的收形锥继续往下收尖 —— 这样基准高度 ≥3 的岛
        /// 会读成"悬浮 + 厚底"，而不是"从海底长上来的柱子"（旧实现的侧壁到地面就是柱子）。
        /// </summary>
        public static void AddSolidCell(MeshBuffers b, TileTerrainGrid grid, int gx, int gy, in IslandShellSettings s)
        {
            if (b == null || grid == null)
                return;

            int blocks = grid.BlocksAt(gx, gy);
            if (blocks <= 0)
                return;

            float surfaceY = grid.SurfaceWorldY(gx, gy);
            float c = Mathf.Max(0.01f, s.ChamferWidth);
            float ch = Mathf.Max(0.01f, s.ChamferHeight);
            int layers = Mathf.Max(1, s.SideLayers);

            // 格号 → 世界坐标（1 格 = LevelGeometry.TileWorldSize 单位；不要写成 gx..gx+1）。
            float x0 = LevelGeometry.TileToWorld(gx), x1 = LevelGeometry.TileToWorld(gx + 1);
            float z0 = LevelGeometry.TileToWorld(gy), z1 = LevelGeometry.TileToWorld(gy + 1);

            // 顶面（内缩 c）：与该格地表严格等高。
            b.AddQuad(
                new Vector3(x0 + c, surfaceY, z0 + c),
                new Vector3(x1 - c, surfaceY, z0 + c),
                new Vector3(x1 - c, surfaceY, z1 - c),
                new Vector3(x0 + c, surfaceY, z1 - c),
                Vector3.up);

            float chamferBottomY = surfaceY - ch;
            float skirtY = Mathf.Min(chamferBottomY, s.SkirtBottomY);

            bool platform = grid.IsPlatformMode;
            int clusterIndex = platform ? grid.ClusterIndexOf(gx, gy) : -1;
            float islandBottomY = platform && clusterIndex >= 0
                ? Mathf.Min(IslandBottomWorldY(grid, clusterIndex, s), chamferBottomY)
                : skirtY;

            // ---- 四面：倒角带 + 岩层侧壁 + 裙边 ----
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                islandBottomY, platform, clusterIndex, c, layers, s, ShellSide.North);
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                islandBottomY, platform, clusterIndex, c, layers, s, ShellSide.South);
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                islandBottomY, platform, clusterIndex, c, layers, s, ShellSide.West);
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                islandBottomY, platform, clusterIndex, c, layers, s, ShellSide.East);
        }

        /// <summary>
        /// 该簇的**岛底平面**世界 Y：簇最低地表 − <see cref="IslandShellSettings.SideThickness"/>，
        /// 下限 <see cref="IslandShellSettings.UndersideBottomY"/>（低于水面，保证贴水档也有厚度可看）。
        /// </summary>
        public static float IslandBottomWorldY(TileTerrainGrid grid, int clusterIndex, in IslandShellSettings s)
        {
            if (grid == null)
                return s.UndersideBottomY;

            float minSurface = grid.ClusterSurfaceMinWorldY(clusterIndex);
        // 侧壁最小厚度 0.4（旧 0.2 ×2）。
            return Mathf.Max(minSurface - Mathf.Max(0.4f, s.SideThickness), s.UndersideBottomY);
        }

        enum ShellSide { North, South, West, East }

        static void AddSide(MeshBuffers b, TileTerrainGrid grid, int gx, int gy,
            float x0, float x1, float z0, float z1,
            float surfaceY, float chamferBottomY, float skirtY, float islandBottomY,
            bool platform, int clusterIndex, float c, int layers, in IslandShellSettings s, ShellSide side)
        {
            int nx = gx, ny = gy;
            bool outside = false;

            switch (side)
            {
                case ShellSide.North:
                    ny = gy - 1;
                    outside = ny < 0;
                    break;
                case ShellSide.South:
                    ny = gy + 1;
                    outside = ny >= grid.DepthTiles;
                    break;
                case ShellSide.West:
                    nx = gx - 1;
                    outside = nx < 0;
                    break;
                default:
                    nx = gx + 1;
                    outside = nx >= grid.WidthTiles;
                    break;
            }

            float wallBottom;
            if (platform)
            {
                // 平台簇模式：同簇邻格 → 台阶差（只看局部台阶面）；跨簇/水/场外 → 岛底平面。
                bool sameIsland = !outside && grid.IsGroundAt(nx, ny)
                    && grid.ClusterIndexOf(nx, ny) == clusterIndex;

                if (sameIsland)
                {
                    float neighborSurface = grid.SurfaceWorldY(nx, ny);
                    wallBottom = Mathf.Min(chamferBottomY,
                        Mathf.Max(LevelGeometry.GroundTopY + grid.BaseWorldYAt(gx, gy), neighborSurface - c));
                }
                else
                {
                    wallBottom = islandBottomY;
                }
            }
            else if (outside)
            {
                // 列式旧地形：边界台地的外侧下延成裙边，任何角度都看不到方块底面的悬空边。
                wallBottom = skirtY;
            }
            else
            {
                float neighborSurface = grid.SurfaceWorldY(nx, ny);
                wallBottom = Mathf.Min(chamferBottomY, Mathf.Max(LevelGeometry.GroundTopY, neighborSurface - c));
            }

            if (wallBottom > chamferBottomY - 1e-4f)
                wallBottom = chamferBottomY;   // 同高邻居：侧壁退化为零高（相邻格倒角在边界正好相接）

            // 每侧两个"沿墙轴"的端点（倒角带的外沿角点）。
            Vector3 a, d;              // 倒角外沿（y = chamferBottomY）
            Vector3 outward;           // 该侧外法线提示
            Vector3 inwardAxis;        // 由边界向内的方向

            switch (side)
            {
                case ShellSide.North:
                    a = new Vector3(x0, chamferBottomY, z0);
                    d = new Vector3(x1, chamferBottomY, z0);
                    outward = new Vector3(0f, 1f, -1f);
                    inwardAxis = Vector3.forward;
                    break;
                case ShellSide.South:
                    a = new Vector3(x1, chamferBottomY, z1);
                    d = new Vector3(x0, chamferBottomY, z1);
                    outward = new Vector3(0f, 1f, 1f);
                    inwardAxis = Vector3.back;
                    break;
                case ShellSide.West:
                    a = new Vector3(x0, chamferBottomY, z1);
                    d = new Vector3(x0, chamferBottomY, z0);
                    outward = new Vector3(-1f, 1f, 0f);
                    inwardAxis = Vector3.right;
                    break;
                default:
                    a = new Vector3(x1, chamferBottomY, z0);
                    d = new Vector3(x1, chamferBottomY, z1);
                    outward = new Vector3(1f, 1f, 0f);
                    inwardAxis = Vector3.back;
                    break;
            }

            // ---- 倒角带：从顶面内缩边斜向外下到格边界 ----
            Vector3 innerA = a + inwardAxis * c + Vector3.up * (surfaceY - chamferBottomY);
            Vector3 innerD = d + inwardAxis * c + Vector3.up * (surfaceY - chamferBottomY);
            b.AddQuad(innerA, innerD, d, a, outward);

            if (wallBottom >= chamferBottomY - 1e-4f)
                return;   // 同高邻居：无可见侧壁

            // ---- 岩层侧壁：layers 段，中间段内凹形成"岩层缝" ----
            int sideIndex = (int)side + 1;
            for (int layer = 0; layer < layers; layer++)
            {
                float t0 = layer / (float)layers;
                float t1 = (layer + 1) / (float)layers;

                float yTop = Mathf.Lerp(chamferBottomY, wallBottom, t0);
                float yBot = Mathf.Lerp(chamferBottomY, wallBottom, t1);

                // 段间凹缝：段上沿/下沿按"缝进深"内缩；最外侧两沿不缩（与倒角/地面接平）。
                float insetTop = layer == 0 ? 0f : s.LayerRecess;
                float insetBot = layer == layers - 1 ? 0f : s.LayerRecess;

                // 剪影扰动（**连续剪切**）：沿墙面向下逐渐加大横向进/出偏移，让同列沿 Z 的长墙
                // 不是"一堵直线墙"（场景文档 §3.1 ③ 的 X 向 ±0.05-0.12）。
                //
                // 【为什么改成"按深度渐变"而不是只抖最下一层】用户裁决 2 让岛体侧壁从 0.15 单位
                // 变成 2.4 单位厚（悬浮岛的厚底），只抖最下层会让上半截侧壁仍是一条笔直的长墙。
                // 渐变剪切让每层的下沿 = 下一层的上沿（连续性），且最上沿偏移恒为 0
                // （与倒角带的外沿严格对齐，不产生接缝）。
                float ratioTop = layer / (float)layers;
                float ratioBot = (layer + 1) / (float)layers;

                float jitterA = SceneArtHash.SignedHash(gx, gy, sideIndex * 97) * s.BoundaryJitter;
                float jitterD = SceneArtHash.SignedHash(gx, gy, sideIndex * 97 + 7) * s.BoundaryJitter;

                Vector3 jitterOffsetTopA = inwardAxis * (jitterA * ratioTop);
                Vector3 jitterOffsetTopD = inwardAxis * (jitterD * ratioTop);
                Vector3 jitterOffsetBotA = inwardAxis * (jitterA * ratioBot);
                Vector3 jitterOffsetBotD = inwardAxis * (jitterD * ratioBot);

                Vector3 topA = a + inwardAxis * insetTop + Vector3.up * (yTop - chamferBottomY) + jitterOffsetTopA;
                Vector3 topD = d + inwardAxis * insetTop + Vector3.up * (yTop - chamferBottomY) + jitterOffsetTopD;
                Vector3 botA = a + inwardAxis * insetBot + Vector3.up * (yBot - chamferBottomY) + jitterOffsetBotA;
                Vector3 botD = d + inwardAxis * insetBot + Vector3.up * (yBot - chamferBottomY) + jitterOffsetBotD;

                b.AddQuad(topA, topD, botD, botA, outward);
            }
        }

        /// <summary>
        /// 0 块列（原版水道列 → 本工程的"潮沟/沙洼"）的薄水膜视觉层：
        /// 一格格心高度的单面片，用湿沙/薄水色表示"刚退潮"。**不是挖洞**——
        /// 基础地面永远存在（<see cref="TileTerrainGrid"/> 类头），本片只是贴在地面上的一层观感。
        /// </summary>
        public static void AddLowZonePlate(MeshBuffers b, int gx, int gy, float yOffset)
        {
            float y = LevelGeometry.GroundTopY + yOffset;
            float x0 = LevelGeometry.TileToWorld(gx), x1 = LevelGeometry.TileToWorld(gx + 1);
            float z0 = LevelGeometry.TileToWorld(gy), z1 = LevelGeometry.TileToWorld(gy + 1);
            b.AddQuad(
                new Vector3(x0, y, z0),
                new Vector3(x1, y, z0),
                new Vector3(x1, y, z1),
                new Vector3(x0, y, z1),
                Vector3.up);
        }

        /// <summary>把整张网格的实心格建成一个合并壳（每关 1 个网格 → 1 个 DrawCall）。</summary>
        public static MeshBuffers BuildSolidShell(TileTerrainGrid grid, in IslandShellSettings s)
        {
            var b = new MeshBuffers();
            if (grid == null)
                return b;

            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                    AddSolidCell(b, grid, gx, gy, s);
            }

            return b;
        }

        /// <summary>
        /// 0 块**地面**格（列式旧地形的"潮沟/沙洼"）的薄水膜视觉层：一格格心高度的单面片。
        /// 【平台化修正】只铺在**有地面**且块高为 0 的格；平台簇模式的水格（无地面）不铺——
        /// 那里是真正的海水，由水面 shader 的泡沫承担接水面观感，不能画成沙面。
        /// </summary>
        public static MeshBuffers BuildLowZone(TileTerrainGrid grid, float yOffset)
        {
            var b = new MeshBuffers();
            if (grid == null)
                return b;

            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    if (grid.IsGroundAt(gx, gy) && grid.BlocksAt(gx, gy) <= 0)
                        AddLowZonePlate(b, gx, gy, yOffset);
                }
            }

            return b;
        }

        /// <summary>竞技场外缘"岸线抖动"幅度（世界单位）。【AI 提案：r3 白框修复】</summary>
        const float EdgeJitter = 0.7f;      // 距离类 ×2（格 1→2 单位）

        /// <summary>
        /// 环形水平带的最大带宽（世界单位）。【AI 提案：r3 白框修复】
        /// 原实现允许 1.0-1.2 宽的实心直边环带，45° 相机下读成"混凝土跑道"；收窄到 0.5 后
        /// 结合开缺抖动读作浪沫/暗水斑，而非一条盖在水上的跑道。</summary>
        const float MaxRingBandWidth = 1.0f;    // 距离类 ×2

        /// <summary>
        /// 竞技场外一圈"潮间带坡"：由边界 <paramref name="innerOffset"/> 处（y=innerY）
        /// 缓降到 <paramref name="outerOffset"/> 处（y=outerY）。四边各成一条带，拐角由南北带补满。
        /// 【依据场景文档 §3.3「1.5-3 单位宽湿沙坡，从 y=0 缓降到 y=-0.6」】
        ///
        /// 【提案/待定（r3 白框修复）】内/外缘沿墙轴按确定性哈希做 ±<see cref="EdgeJitter"/> 的
        /// "岸线抖动"，使湿沙坡与水面的交界不再是一条笔直切线（r2 诊断：直切线 + 平色读成
        /// "混凝土跑道/一张方纸的边"）。竖直端点仍严格是 <paramref name="innerY"/> /
        /// <paramref name="outerY"/>（既有用例口径不变）；内缘只向外让，绝不进可玩区。
        /// </summary>
        public static void AddOffsetBand(MeshBuffers b, float arenaWidth, float arenaDepth,
            float innerOffset, float outerOffset, float innerY, float outerY, float segmentLength)
        {
            if (b == null || outerOffset <= innerOffset)
                return;

            float seg = Mathf.Max(0.5f, segmentLength);   // 分段长度类 ×2（格 1→2 单位）

            // 北 / 南带（沿 X 铺，含四角的延伸段）。
            AddStripAlongX(b, -innerOffset, arenaWidth + innerOffset, 0f, -1f, innerOffset, outerOffset,
                innerY, outerY, seg, 11);
            AddStripAlongX(b, -innerOffset, arenaWidth + innerOffset, arenaDepth, 1f, innerOffset, outerOffset,
                innerY, outerY, seg, 12);

            // 西 / 东带（沿 Z 铺，只覆盖竞技场纵深，拐角交给南北带）。
            AddStripAlongZ(b, -innerOffset, arenaDepth + innerOffset, 0f, -1f, innerOffset, outerOffset,
                innerY, outerY, seg, 13);
            AddStripAlongZ(b, -innerOffset, arenaDepth + innerOffset, arenaWidth, 1f, innerOffset, outerOffset,
                innerY, outerY, seg, 14);
        }

        static void AddStripAlongX(MeshBuffers b, float xFrom, float xTo, float edgeAt, float outwardSign,
            float innerOffset, float outerOffset, float innerY, float outerY, float seg, int salt)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(xTo - xFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                float xa = Mathf.Lerp(xFrom, xTo, i / (float)steps);
                float xb = Mathf.Lerp(xFrom, xTo, (i + 1) / (float)steps);

                // 逐顶点抖动：相邻段共享端点 → 折线连续，不会裂开。
                float inA = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(i, 0, salt)) * EdgeJitter;
                float inB = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(i + 1, 0, salt)) * EdgeJitter;
                float outA = outerOffset + SceneArtHash.SignedHash(i, 1, salt) * EdgeJitter;
                float outB = outerOffset + SceneArtHash.SignedHash(i + 1, 1, salt) * EdgeJitter;

                var ia = new Vector3(xa, innerY, edgeAt + outwardSign * inA);
                var ib = new Vector3(xb, innerY, edgeAt + outwardSign * inB);
                var ob = new Vector3(xb, outerY, edgeAt + outwardSign * outB);
                var oa = new Vector3(xa, outerY, edgeAt + outwardSign * outA);
                b.AddQuad(ia, ib, ob, oa, new Vector3(0f, 1f, outwardSign));
            }
        }

        static void AddStripAlongZ(MeshBuffers b, float zFrom, float zTo, float edgeAt, float outwardSign,
            float innerOffset, float outerOffset, float innerY, float outerY, float seg, int salt)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(zTo - zFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                float za = Mathf.Lerp(zFrom, zTo, i / (float)steps);
                float zb = Mathf.Lerp(zFrom, zTo, (i + 1) / (float)steps);

                float inA = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(i, 0, salt)) * EdgeJitter;
                float inB = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(i + 1, 0, salt)) * EdgeJitter;
                float outA = outerOffset + SceneArtHash.SignedHash(i, 1, salt) * EdgeJitter;
                float outB = outerOffset + SceneArtHash.SignedHash(i + 1, 1, salt) * EdgeJitter;

                var ia = new Vector3(edgeAt + outwardSign * inA, innerY, za);
                var ib = new Vector3(edgeAt + outwardSign * inB, innerY, zb);
                var ob = new Vector3(edgeAt + outwardSign * outB, outerY, zb);
                var oa = new Vector3(edgeAt + outwardSign * outA, outerY, za);
                b.AddQuad(ia, ib, ob, oa, new Vector3(outwardSign, 1f, 0f));
            }
        }

        /// <summary>
        /// 环形**水平**带（泡沫线/暗水带）：在边界外 <paramref name="innerOffset"/> ~
        /// <paramref name="outerOffset"/> 之间铺一条 y 恒定的环带。四边各一条，拐角由南北带补满。
        ///
        /// 【提案/待定（r3 白框修复）】原实现是一圈**均匀实心的直边环带**，45° 相机下与湿沙坡、
        /// 危险带叠成"混凝土跑道/方纸边"（r2 诊断：全宽浅灰白围框 y≈700-830）。现改为
        /// **贴边的不规则泡沫斑块**：
        ///   · 带宽收窄到 ≤ <see cref="MaxRingBandWidth"/>；
        ///   · 沿周长按确定性哈希开缺（有斑有缝）+ 内外缘抖动 → 读作浪沫而非切边；
        ///   · 顶点仍严格在 y、仍全部落在竞技场矩形之外（既有用例口径不变）。
        /// </summary>
        public static void AddFlatRingBand(MeshBuffers b, float arenaWidth, float arenaDepth,
            float innerOffset, float outerOffset, float y, float segmentLength)
        {
            if (b == null || outerOffset <= innerOffset)
                return;

            float seg = Mathf.Max(0.5f, segmentLength);   // 分段长度类 ×2（格 1→2 单位）
            float width = Mathf.Min(outerOffset - innerOffset, MaxRingBandWidth);

            AddRingStripAlongX(b, -innerOffset, arenaWidth + innerOffset, 0f, -1f, innerOffset, width, y, seg, 31);
            AddRingStripAlongX(b, -innerOffset, arenaWidth + innerOffset, arenaDepth, 1f, innerOffset, width, y, seg, 32);
            AddRingStripAlongZ(b, -innerOffset, arenaDepth + innerOffset, 0f, -1f, innerOffset, width, y, seg, 33);
            AddRingStripAlongZ(b, -innerOffset, arenaDepth + innerOffset, arenaWidth, 1f, innerOffset, width, y, seg, 34);
        }

        static void AddRingStripAlongX(MeshBuffers b, float xFrom, float xTo, float edgeAt, float outwardSign,
            float innerOffset, float width, float y, float seg, int salt)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(xTo - xFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                // 生成-消散：按确定性哈希开缺，把实心环带打散成泡沫斑块（约一半段留下）。
                if (SceneArtHash.Hash01(i, 0, salt) < 0.48f)
                    continue;

                float xa = Mathf.Lerp(xFrom, xTo, i / (float)steps);
                float xb = Mathf.Lerp(xFrom, xTo, (i + 1) / (float)steps);
                int sa = salt + i, sb = salt + i + 1;

                float inA = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(sa, 1, 7)) * width * 0.45f;
                float inB = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(sb, 1, 7)) * width * 0.45f;
                float outA = Mathf.Max(inA + 0.24f, innerOffset + width + SceneArtHash.SignedHash(sa, 2, 9) * width * 0.5f);
                float outB = Mathf.Max(inB + 0.24f, innerOffset + width + SceneArtHash.SignedHash(sb, 2, 9) * width * 0.5f);

                b.AddQuad(
                    new Vector3(xa, y, edgeAt + outwardSign * inA),
                    new Vector3(xb, y, edgeAt + outwardSign * inB),
                    new Vector3(xb, y, edgeAt + outwardSign * outB),
                    new Vector3(xa, y, edgeAt + outwardSign * outA),
                    new Vector3(0f, 1f, outwardSign));
            }
        }

        static void AddRingStripAlongZ(MeshBuffers b, float zFrom, float zTo, float edgeAt, float outwardSign,
            float innerOffset, float width, float y, float seg, int salt)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(zTo - zFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                if (SceneArtHash.Hash01(i, 0, salt) < 0.48f)
                    continue;

                float za = Mathf.Lerp(zFrom, zTo, i / (float)steps);
                float zb = Mathf.Lerp(zFrom, zTo, (i + 1) / (float)steps);
                int sa = salt + i, sb = salt + i + 1;

                float inA = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(sa, 1, 7)) * width * 0.45f;
                float inB = innerOffset + Mathf.Max(0f, SceneArtHash.SignedHash(sb, 1, 7)) * width * 0.45f;
                float outA = Mathf.Max(inA + 0.24f, innerOffset + width + SceneArtHash.SignedHash(sa, 2, 9) * width * 0.5f);
                float outB = Mathf.Max(inB + 0.24f, innerOffset + width + SceneArtHash.SignedHash(sb, 2, 9) * width * 0.5f);

                b.AddQuad(
                    new Vector3(edgeAt + outwardSign * inA, y, za),
                    new Vector3(edgeAt + outwardSign * inB, y, zb),
                    new Vector3(edgeAt + outwardSign * outB, y, zb),
                    new Vector3(edgeAt + outwardSign * outA, y, za),
                    new Vector3(outwardSign, 1f, 0f));
            }
        }

        /// <summary>
        /// 边界**虚线**（落水危险提示，场景文档 §5.4）：在边界外 <paramref name="offset"/> 处
        /// 铺一条由短划线组成的环带，y = <paramref name="y"/>。四边各走一遍。
        /// </summary>
        /// <returns>划线段数（供报告/测试核对）。</returns>
        public static int AddDashedBorder(MeshBuffers b, float arenaWidth, float arenaDepth,
            float offset, float y, float dashLength, float gap, float width)
        {
            if (b == null || dashLength <= 0f || width <= 0f)
                return 0;

            int total = 0;
            float half = width * 0.5f;

            // 南北边：沿 X 走，横跨（含拐角外扩）。
            float xFrom = -offset, xTo = arenaWidth + offset;
            // 东西边：沿 Z 走，仅竞技场纵深。
            float zFrom = 0f, zTo = arenaDepth;

            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float sign = (side % 2 == 0) ? -1f : 1f;
                float lineAt = alongX
                    ? (side == 0 ? -offset : arenaDepth + offset)
                    : (side == 2 ? -offset : arenaWidth + offset);
                float from = alongX ? xFrom : zFrom;
                float to = alongX ? xTo : zTo;

                total += AddDashesAlong(b, from, to, lineAt, sign, alongX, y, dashLength, gap, half);
            }

            return total;
        }

        static int AddDashesAlong(MeshBuffers b, float from, float to, float lineAt, float outwardSign,
            bool alongX, float y, float dashLength, float gap, float half)
        {
            float length = Mathf.Abs(to - from);
            if (length <= 0f)
                return 0;

            float step = dashLength + gap;
            int count = 0;
            for (float t = 0f; t + dashLength <= length + 1e-4f; t += step)
            {
                float a = from + Mathf.Sign(to - from) * t;
                float c = from + Mathf.Sign(to - from) * (t + dashLength);
                float inner = lineAt - outwardSign * half;
                float outer = lineAt + outwardSign * half;

                Vector3 p0, p1, p2, p3;
                if (alongX)
                {
                    p0 = new Vector3(a, y, inner);
                    p1 = new Vector3(c, y, inner);
                    p2 = new Vector3(c, y, outer);
                    p3 = new Vector3(a, y, outer);
                }
                else
                {
                    p0 = new Vector3(inner, y, a);
                    p1 = new Vector3(inner, y, c);
                    p2 = new Vector3(outer, y, c);
                    p3 = new Vector3(outer, y, a);
                }

                b.AddQuad(p0, p1, p2, p3, Vector3.up);
                count++;
            }

            return count;
        }

        // ==================================================================
        // 悬空平台底部（船体侧板+龙骨 / 岩锥收尖 / 梯田岩层）
        // ==================================================================

        /// <summary>
        /// 把所有平台簇的**底部**几何写进一个缓冲（运行时 <see cref="BattleTerrainView"/> 用，
        /// 单材质）。编辑器构建期若要按材质分组（船=暗木、岛=岩），改用
        /// <see cref="AddPlatformUndersides"/>。
        /// </summary>
        public static void AddPlatformUnderside(MeshBuffers b, TileTerrainGrid grid, in IslandShellSettings s)
        {
            if (b == null || grid == null)
                return;

            for (int c = 0; c < grid.ClusterCount; c++)
                AddClusterUnderside(b, grid, grid.ClusterAt(c), c, s);
        }

        /// <summary>
        /// 把所有平台簇底部按材质写入：<see cref="PlatformClusterKind.Ship"/> → 暗木（水线以下船板/龙骨），
        /// 空岛 / 梯田岛 → 岩。编辑器 <c>SceneArtBuilder</c> 用这个，使底部与既有材质组（DrawCall）合并。
        /// 同时沿每个簇包络在**水线**处补一圈窄暗部（并入暗木/岩）与一条细泡沫线（并入 Foam 组）：
        /// 修 r2 诊断「平台与水面交界是平直切边、无底面感、无接触暗部、无浪」。
        /// </summary>
        public static void AddPlatformUndersides(ScenePropBuffers buffers, TileTerrainGrid grid,
            in IslandShellSettings s)
        {
            if (buffers == null || grid == null)
                return;

            for (int c = 0; c < grid.ClusterCount; c++)
            {
                PlatformClusterInfo info = grid.ClusterAt(c);
                MeshBuffers target = info.Kind == PlatformClusterKind.Ship ? buffers.WoodDark : buffers.Rock;
                AddClusterUnderside(target, grid, info, c, s);

                // 簇包络（格）→ 世界（1 格 = LevelGeometry.TileWorldSize 单位）。
                float cx0 = LevelGeometry.TileToWorld(info.X0), cx1 = LevelGeometry.TileToWorld(info.X1 + 1);
                float cz0 = LevelGeometry.TileToWorld(info.Z0), cz1 = LevelGeometry.TileToWorld(info.Z1 + 1);
                int clusterSalt = 101 + c * 17;

                // 【用户裁决 2：悬浮岛】水线暗带 / 湿沙暗带 / 泡沫线都只在**岛底确实入水**时才画
                // （岛底平面 < 水面 + 0.05）：基准高度 ≥3 的岛悬在水面上方，若还画一圈贴水泡沫，
                // 会读成"水面上浮着一圈白边"的错位。贴水档（基准 0）照旧保留四段过渡
                // （沙 → 暗湿沙 → 泡沫 → 水）。
                float islandBottomY = IslandBottomWorldY(grid, c, s);
                if (islandBottomY >= LevelGeometry.WaterSurfaceY + 0.1f)
                    continue;

                // 湿沙暗带（r5 新增）：贴在簇包络外沿、比平台材质暗的一圈斜带，
                // 写进 SandWet 组复用湿沙材质 → 岸线横切面得到「沙 → 暗湿沙 → 泡沫 → 水」的暗湿段。
                AddWaterlineWetSand(buffers.SandWet, cx0, cx1, cz0, cz1,
                    LevelGeometry.WaterSurfaceY, clusterSalt);

                // 细泡沫线：写在独立 Foam 组（unlit 半透明白，与水面泡沫同材质），
                // 贴在簇包络外的水面上（平台入水处的一圈浪沫）。
                // y 取 WaterSurfaceY+0.14（= -0.26）：水立方体细分网格的顶面在 WaterSurfaceY+0.1=-0.30，
                // 与 SceneArtBuilder 的岸边泡沫带同口径（WaterTopY+0.01），低于它会被水面盖住。
                // 【r5】内侧由 0.02 外移到 0.07，让湿沙暗带不被泡沫整段盖住；开缺后泡沫缝隙里
                // 露出暗湿沙，四段过渡才成立。
                AddWaterlineFoam(buffers.Foam, cx0, cx1, cz0, cz1,
                    LevelGeometry.WaterSurfaceY + 0.14f, 0.14f, 0.60f, clusterSalt);
            }
        }

        /// <summary>
        /// 单簇底部：按伪装类型选形（船 → V 形船壳；岛 → **贴岛形轮廓**的收形锥），
        /// 并在岛底入水时补一圈窄暗部（接触暗部）。
        ///
        /// 【2026-09-14 用户裁决 1「厚重底部收形、悬空可见」的改动】旧实现是"矩形包络的圆台"
        /// （<c>AddFrustum</c>：外接椭圆 + 固定收尖比），既与不规则岛形轮廓对不上（四角悬空），
        /// 也没有"厚度"可言。现改为：
        ///   1. 由岛形轮廓（岛缘格边）构造收形环 0，逐环朝岛心缩放到尖 → 底部轮廓 = 真岛形；
        ///   2. 岛底平面 = 簇最低地表 − <see cref="IslandShellSettings.SideThickness"/>（侧壁负责这段厚度），
        ///      收形锥从岛底平面再往下 <see cref="IslandShellSettings.UndersideTaperDepth"/> 收尖。
        /// </summary>
        public static void AddClusterUnderside(MeshBuffers b, TileTerrainGrid grid, in PlatformClusterInfo cluster,
            int clusterIndex, in IslandShellSettings s)
        {
            if (b == null || grid == null)
                return;

            // 岛底平面（侧壁与收形锥的分界）与锥尖。
            float bottomY = IslandBottomWorldY(grid, clusterIndex, s);
            float tipY = bottomY - Mathf.Max(0.1f, s.UndersideTaperDepth);

            // 簇包络（格）→ 世界（1 格 = LevelGeometry.TileWorldSize 单位）。
            float x0 = LevelGeometry.TileToWorld(cluster.X0), x1 = LevelGeometry.TileToWorld(cluster.X1 + 1);
            float z0 = LevelGeometry.TileToWorld(cluster.Z0), z1 = LevelGeometry.TileToWorld(cluster.Z1 + 1);

            if (cluster.Kind == PlatformClusterKind.Ship)
            {
                // 船体：矩形船壳是对的（船就是一整艘船），只是不再从基础地面起算，
                // 而是从岛底平面起算（上面那段厚度由单格壳侧壁负责）。
                AddShipHullUnderside(b, x0, x1, z0, z1, bottomY, tipY, clusterIndex);
            }
            else
            {
                AddIslandTaperUnderside(b, grid, cluster, clusterIndex, bottomY, tipY, s);
            }

            // 水线暗部环：只在岛底真的入水时画（从台顶下缘罩到水面之下），
            // 把"平台底部悬空"的缝隙收口，读作"入水的接触暗部"（r2 诊断问题 3）。
            if (bottomY < LevelGeometry.WaterSurfaceY + 0.1f)
            {
                AddWaterlineBand(b, x0, x1, z0, z1,
                    LevelGeometry.GroundTopY + 0.1f, LevelGeometry.WaterSurfaceY - 0.24f, 0.06f);
            }
        }

        /// <summary>
        /// **贴岛形轮廓**的收形锥（空岛 / 梯田岛）：取岛缘的每一条格边作为轮廓环 0，
        /// 再朝岛心逐环缩放（<see cref="IslandShellSettings.UndersideRings"/> 环）到锥尖。
        ///
        /// 【为什么用"轮廓环 + 逐环缩放"而不是"逐格小锥"】逐格锥会在相邻格之间留下裂缝
        /// （各自朝岛心收缩，缝越往下越宽）；而"同一个线性缩放"对共享端点给出同一位置，
        /// 环与环之间天然连续，无裂缝，且轮廓与 <see cref="AddSolidCell"/> 的侧壁底边**逐点对齐**。
        /// </summary>
        static void AddIslandTaperUnderside(MeshBuffers b, TileTerrainGrid grid, in PlatformClusterInfo cluster,
            int clusterIndex, float bottomY, float tipY, in IslandShellSettings s)
        {
            float cx = LevelGeometry.TileToWorld((cluster.X0 + cluster.X1 + 1) * 0.5f);
            float cz = LevelGeometry.TileToWorld((cluster.Z0 + cluster.Z1 + 1) * 0.5f);

            int rings = Mathf.Max(2, s.UndersideRings);
            int cells = 0;

            for (int gz = cluster.Z0; gz <= cluster.Z1; gz++)
            {
                for (int gx = cluster.X0; gx <= cluster.X1; gx++)
                {
                    if (grid.ClusterIndexOf(gx, gz) != clusterIndex || grid.BlocksAt(gx, gz) <= 0)
                        continue;

                    cells++;

                    // 四条格边：邻居不属于本簇（水 / 他簇 / 场外）→ 是岛缘边。
                    AddTaperEdge(b, grid, clusterIndex, cx, cz, bottomY, tipY, rings, s,
                        gx, gz, gx + 1, gz);
                    AddTaperEdge(b, grid, clusterIndex, cx, cz, bottomY, tipY, rings, s,
                        gx + 1, gz + 1, gx, gz + 1);
                    AddTaperEdge(b, grid, clusterIndex, cx, cz, bottomY, tipY, rings, s,
                        gx + 1, gz, gx + 1, gz + 1);
                    AddTaperEdge(b, grid, clusterIndex, cx, cz, bottomY, tipY, rings, s,
                        gx, gz + 1, gx, gz);
                }
            }

            if (cells <= 0)
                return;

            // 钟乳 / 垂藤：3-5 根细锥从岛底不同 xz 垂到不同深度（保留 r2 修复的细节层）。
            float halfX = LevelGeometry.TileToWorld(Mathf.Max(0.5f, (cluster.X1 - cluster.X0 + 1) * 0.5f));
            float halfZ = LevelGeometry.TileToWorld(Mathf.Max(0.5f, (cluster.Z1 - cluster.Z0 + 1) * 0.5f));
            int drips = 3 + (int)(SceneArtHash.Hash01(clusterIndex, 5, 23) * 3f);
            for (int i = 0; i < drips; i++)
            {
                float dx = cx + SceneArtHash.SignedHash(clusterIndex, i, 29) * halfX * 0.6f;
                float dz = cz + SceneArtHash.SignedHash(clusterIndex, i, 31) * halfZ * 0.6f;
                float dripLen = 0.36f + SceneArtHash.Hash01(clusterIndex, i, 37) * 0.84f;
                b.AddFrustum(new Vector3(dx, tipY + 0.1f, dz), 0.06f, 0.14f, dripLen, 5,
                    i * 47f, capTop: false, capBottom: true);
            }
        }

        /// <summary>
        /// 一条岛缘边在相邻两环之间生成的侧裙面（退化边 / 非岛缘边自动跳过）。
        /// <paramref name="ax"/>/<paramref name="az"/> → <paramref name="bx"/>/<paramref name="bz"/> 为边的两个端点（格角坐标）。
        /// </summary>
        static void AddTaperEdge(MeshBuffers b, TileTerrainGrid grid, int clusterIndex,
            float cx, float cz, float bottomY, float tipY, int rings, in IslandShellSettings s,
            int ax, int az, int bx, int bz)
        {
            // 只保留岛缘边：该边相邻的两个格里，至少有一个不属于本簇。
            if (IsEdgeInterior(grid, clusterIndex, ax, az, bx, bz))
                return;

            float tipRatio = Mathf.Clamp(s.UndersideTipRatio, 0f, 0.95f);

            for (int r = 0; r < rings - 1; r++)
            {
                float t0 = r / (float)(rings - 1);
                float t1 = (r + 1) / (float)(rings - 1);

                float s0 = Mathf.Lerp(1f, tipRatio, t0);
                float s1 = Mathf.Lerp(1f, tipRatio, t1);
                float y0 = Mathf.Lerp(bottomY, tipY, t0);
                float y1 = Mathf.Lerp(bottomY, tipY, t1);

                Vector3 a0 = new Vector3(cx + (ax - cx) * s0, y0, cz + (az - cz) * s0);
                Vector3 b0 = new Vector3(cx + (bx - cx) * s0, y0, cz + (bz - cz) * s0);
                Vector3 a1 = new Vector3(cx + (ax - cx) * s1, y1, cz + (az - cz) * s1);
                Vector3 b1 = new Vector3(cx + (bx - cx) * s1, y1, cz + (bz - cz) * s1);

                if (Vector3.SqrMagnitude(b1 - a1) < 1e-8f)
                {
                    // 最后一环收到一点：用三角形（a0-b0-尖）收口。
                    Vector3 hint = new Vector3((a0.x + b0.x) * 0.5f - cx, 0.35f, (a0.z + b0.z) * 0.5f - cz);
                    AddOrientedTriangle(b, a0, b0, a1, hint);
                    continue;
                }

                Vector3 outward = new Vector3((a1.x + b1.x) * 0.5f - cx, 0.35f,
                    (a1.z + b1.z) * 0.5f - cz);
                b.AddQuad(a0, b0, b1, a1, outward.sqrMagnitude > 1e-9f ? outward.normalized : Vector3.up);
            }
        }

        /// <summary>按外法线提示决定绕序的三角形（MeshBuffers 只有四边形版的自动翻面，这里补三角形版）。</summary>
        static void AddOrientedTriangle(MeshBuffers b, Vector3 a, Vector3 c, Vector3 apex, Vector3 outwardHint)
        {
            Vector3 normal = Vector3.Cross(c - a, apex - a);
            if (normal.sqrMagnitude < 1e-16f)
                return;

            if (Vector3.Dot(normal, outwardHint) < 0f)
                b.AddTriangle(a, apex, c);
            else
                b.AddTriangle(a, c, apex);
        }

        /// <summary>
        /// 格边是否属于**簇内部**（两侧的格都属于本簇）。
        /// 边由两个格角坐标给出，用"边中点两侧各偏 0.1 格"的两个采样点判定。
        /// </summary>
        static bool IsEdgeInterior(TileTerrainGrid grid, int clusterIndex, int ax, int az, int bx, int bz)
        {
            float mx = (ax + bx) * 0.5f;
            float mz = (az + bz) * 0.5f;

            // 边的法线方向（沿 X 的边法线在 Z，反之在 X）。
            bool alongX = Mathf.Abs(bx - ax) >= Mathf.Abs(bz - az);
            float nx = alongX ? 0f : 1f;
            float nz = alongX ? 1f : 0f;

            int side1 = grid.ClusterIndexOf(LevelGeometry.WorldToTileIndex(mx + nx * 0.5f),
                LevelGeometry.WorldToTileIndex(mz + nz * 0.5f));
            int side2 = grid.ClusterIndexOf(LevelGeometry.WorldToTileIndex(mx - nx * 0.5f),
                LevelGeometry.WorldToTileIndex(mz - nz * 0.5f));

            return side1 == clusterIndex && side2 == clusterIndex;
        }

        /// <summary>
        /// 水线暗部环（竖直薄带，绕簇包络矩形一圈）：顶 <paramref name="topY"/>、底
        /// <paramref name="bottomY"/>，向内缩 <paramref name="inset"/> 避免与地块壳外壁 z-fight。
        /// 【提案/待定】尺寸为 AI 取值，意图是"平台入水处一圈窄暗部"（r2 诊断问题 3）。
        /// </summary>
        public static void AddWaterlineBand(MeshBuffers b, float x0, float x1, float z0, float z1,
            float topY, float bottomY, float inset)
        {
            if (b == null || bottomY >= topY)
                return;

            float ax0 = x0 + inset, ax1 = x1 - inset, az0 = z0 + inset, az1 = z1 - inset;

            // 北(-Z) / 南(+Z)：沿 X 的竖直薄带。
            b.AddQuad(new Vector3(ax0, topY, az0), new Vector3(ax1, topY, az0),
                new Vector3(ax1, bottomY, az0), new Vector3(ax0, bottomY, az0), new Vector3(0f, 0f, -1f));
            b.AddQuad(new Vector3(ax1, topY, az1), new Vector3(ax0, topY, az1),
                new Vector3(ax0, bottomY, az1), new Vector3(ax1, bottomY, az1), new Vector3(0f, 0f, 1f));
            // 西(-X) / 东(+X)：沿 Z 的竖直薄带。
            b.AddQuad(new Vector3(ax0, topY, az1), new Vector3(ax0, topY, az0),
                new Vector3(ax0, bottomY, az0), new Vector3(ax0, bottomY, az1), new Vector3(-1f, 0f, 0f));
            b.AddQuad(new Vector3(ax1, topY, az0), new Vector3(ax1, topY, az1),
                new Vector3(ax1, bottomY, az1), new Vector3(ax1, bottomY, az0), new Vector3(1f, 0f, 0f));
        }

        // ---- 水线泡沫碎斑参数（【AI 提案：r5 泡沫碎斑化】）----
        // r4 诊断：簇级 AddWaterlineFoam 是"连续、等宽、纯色"的环，45° 相机下读成
        // 混凝土跑道/跑道白线（sea-shore 横切面灰白带 std≈1.3、像素数 r3→r4 完全未变）。
        // 现改为与 AddRingStripAlongX/Z 同款的"分段 + 确定性开缺 + 带宽扰动 + 内外缘抖动"。
        /// <summary>每段泡沫的长度下限（世界单位）。</summary>
        const float FoamSegmentMin = 1.2f;      // 距离类 ×2
        /// <summary>每段泡沫的长度上限（世界单位）。</summary>
        const float FoamSegmentMax = 2.4f;      // 距离类 ×2
        /// <summary>段保留率（≈55%，其余开缺）——泡沫本来就该断续。</summary>
        const float FoamKeepRatio = 0.55f;
        /// <summary>单段带宽下限（世界单位）。</summary>
        const float FoamBandWidthMin = 0.16f;   // 距离类 ×2
        /// <summary>单段带宽上限（世界单位）。</summary>
        const float FoamBandWidthMax = 0.84f;   // 距离类 ×2
        /// <summary>泡沫内外缘沿法线的抖动幅度（世界单位）。</summary>
        const float FoamEdgeJitter = 0.20f;     // 距离类 ×2

        /// <summary>
        /// 水线细泡沫线（水平薄带，绕簇包络一圈）：从包络外 <paramref name="innerGap"/> 起、
        /// 到 <paramref name="outerGap"/> 为止的名义带宽内铺贴（场景文档 §5.3 泡沫环宽
        /// 0.4-1.2 的下限再做窄化，作为"平台入水"的浪沫）。【提案/待定】
        ///
        /// 【r5 碎斑化】不再是四条等宽实心直带，而是沿边分段铺贴：逐段按确定性哈希
        /// **开缺**（保留 ≈55%）、**带宽在 0.08-0.42 间扰动**、**内外缘各 ±0.1 抖动**、
        /// 段长 0.6-1.2 → 读作"浪沫碎斑贴岸"而非连续条。所有顶点仍严格在
        /// <paramref name="y"/>（水平带），且都在包络之外（<c>innerGap ≥ 0</c>，不进平台可玩区）。
        /// </summary>
        public static void AddWaterlineFoam(MeshBuffers b, float x0, float x1, float z0, float z1,
            float y, float innerGap, float outerGap)
        {
            AddWaterlineFoam(b, x0, x1, z0, z1, y, innerGap, outerGap, 0);
        }

        /// <summary>
        /// <see cref="AddWaterlineFoam(MeshBuffers, float, float, float, float, float, float, float)"/>
        /// 的带盐重载：<paramref name="salt"/> 让不同平台簇得到不同的泡沫斑块分布。
        /// </summary>
        public static void AddWaterlineFoam(MeshBuffers b, float x0, float x1, float z0, float z1,
            float y, float innerGap, float outerGap, int salt)
        {
            if (b == null || outerGap <= innerGap)
                return;

            float ex = 1.2f;   // 角部外延，避免四条带在角上留缝（距离类 ×2）

            // 北(-Z) / 南(+Z)
            AddWaterlineFoamStrip(b, x0 - ex, x1 + ex, z0, -1f, innerGap, y, true, salt + 61);
            AddWaterlineFoamStrip(b, x0 - ex, x1 + ex, z1, 1f, innerGap, y, true, salt + 62);
            // 西(-X) / 东(+X)
            AddWaterlineFoamStrip(b, z0 - ex, z1 + ex, x0, -1f, innerGap, y, false, salt + 63);
            AddWaterlineFoamStrip(b, z0 - ex, z1 + ex, x1, 1f, innerGap, y, false, salt + 64);
        }

        /// <summary>
        /// 单边泡沫带：沿边按 0.6-1.2 的段长逐段推进，逐段开缺（保留 ≈<see cref="FoamKeepRatio"/>）、
        /// 带宽扰动（<see cref="FoamBandWidthMin"/>-<see cref="FoamBandWidthMax"/>）、内外缘 ±<see cref="FoamEdgeJitter"/>。
        /// <paramref name="outwardSign"/>：由包络指向水的方向（北/西 = -1，南/东 = +1）；
        /// 偏移量恒为正 → 顶点始终在包络之外。段间端点由同一哈希派生，连续保留的段不会裂开。
        /// </summary>
        static void AddWaterlineFoamStrip(MeshBuffers b, float alongFrom, float alongTo, float edgeAt,
            float outwardSign, float innerGap, float y, bool alongX, int salt)
        {
            float t = alongFrom;
            int i = 0;
            while (t < alongTo - 1e-4f)
            {
                float segLen = Mathf.Lerp(FoamSegmentMin, FoamSegmentMax, SceneArtHash.Hash01(salt, i, 3));
                float ta = t;
                float tb = Mathf.Min(alongTo, t + segLen);
                t = tb;

                // 生成-消散：约 45% 的段开缺，泡沫断续而非一条实心边。
                if (SceneArtHash.Hash01(salt, i, 5) >= FoamKeepRatio)
                {
                    i++;
                    continue;
                }

                float widthA = Mathf.Lerp(FoamBandWidthMin, FoamBandWidthMax, SceneArtHash.Hash01(salt, i, 7));
                float widthB = Mathf.Lerp(FoamBandWidthMin, FoamBandWidthMax, SceneArtHash.Hash01(salt, i + 1, 7));

                float inA = Mathf.Max(0f, innerGap + SceneArtHash.SignedHash(salt, i, 11) * FoamEdgeJitter);
                float inB = Mathf.Max(0f, innerGap + SceneArtHash.SignedHash(salt, i + 1, 11) * FoamEdgeJitter);
                float outA = Mathf.Max(inA + 0.1f, inA + widthA + SceneArtHash.SignedHash(salt, i, 13) * FoamEdgeJitter);
                float outB = Mathf.Max(inB + 0.1f, inB + widthB + SceneArtHash.SignedHash(salt, i + 1, 13) * FoamEdgeJitter);

                if (alongX)
                {
                    b.AddQuad(
                        new Vector3(ta, y, edgeAt + outwardSign * inA),
                        new Vector3(tb, y, edgeAt + outwardSign * inB),
                        new Vector3(tb, y, edgeAt + outwardSign * outB),
                        new Vector3(ta, y, edgeAt + outwardSign * outA),
                        new Vector3(0f, 1f, outwardSign));
                }
                else
                {
                    b.AddQuad(
                        new Vector3(edgeAt + outwardSign * inA, y, ta),
                        new Vector3(edgeAt + outwardSign * inB, y, tb),
                        new Vector3(edgeAt + outwardSign * outB, y, tb),
                        new Vector3(edgeAt + outwardSign * outA, y, ta),
                        new Vector3(outwardSign, 1f, 0f));
                }

                i++;
            }
        }

        // ---- 湿沙暗带参数（【AI 提案：r5】）----
        /// <summary>湿沙暗带陆侧（靠平台）相对水面的抬高 → y = WaterSurfaceY+0.09。</summary>
        const float WetSandLandRise = 0.18f;    // 距离类 ×2
        /// <summary>湿沙暗带水侧（靠外）相对水面的抬高 → y = WaterSurfaceY+0.06。</summary>
        const float WetSandSeaRise = 0.12f;     // 距离类 ×2
        /// <summary>湿沙暗带内侧相对包络的名义外扩（0.0 = 贴包络）。</summary>
        const float WetSandInnerOffset = 0.0f;
        /// <summary>湿沙暗带内侧外扩的抖动幅度（世界单位）。</summary>
        const float WetSandInnerJitter = 0.06f; // 距离类 ×2
        /// <summary>湿沙暗带外侧名义外扩（包络外 0.0-0.25 的中值）。</summary>
        const float WetSandOuterOffset = 0.40f; // 距离类 ×2
        /// <summary>外侧外扩的抖动幅度（世界单位）→ 实际落在 0.15-0.25。</summary>
        const float WetSandOuterJitter = 0.10f; // 距离类 ×2
        /// <summary>湿沙带段保留率（湿沙是连续潮区，只做轻微开缺打断"跑道"读感）。</summary>
        const float WetSandKeepRatio = 0.78f;

        /// <summary>
        /// 湿沙暗带（绕簇包络一圈的近水平浅滩）：以比平台材质暗的湿沙
        /// （写进 <c>SandWet</c> 组复用湿沙材质）铺在**渲染水面之上、泡沫陆侧**，
        /// 陆缘 y = <paramref name="waterY"/>+<see cref="WetSandLandRise"/>（-0.11）、
        /// 水缘 y = <paramref name="waterY"/>+<see cref="WetSandSeaRise"/>（-0.14），
        /// 外侧水平外扩 0.15-0.25 并随位置抖动；段长 0.6-1.2、保留 ≈78%。【提案/待定：r5】
        ///
        /// 【为什么是"贴水面的浅滩"而不是爬到平台顶的斜坡】两条硬约束把带子夹在水面附近：
        ///   · 水立方体细分网格的顶面在 <c>WaterSurfaceY+0.05 = -0.15</c>（见本文件泡沫注释），
        ///     低于它会被水面盖住、看不见；
        ///   · 任务约束"顶点 y 不得超过 <c>WaterSurfaceY+0.1 = -0.1</c>"（避免挡弹道/被当掩体）。
        /// 所以湿沙带只能落在 y ∈ [-0.15, -0.1] 的窄窗口里；取 <c>-0.14..-0.11</c>。
        /// 它贴在平台包络外沿、泡沫内侧，与泡沫/水面拼成
        /// 「沙 → 暗湿沙 → 泡沫（断续）→ 水」四段过渡（场景设计 §3.3 判据）。
        /// </summary>
        public static void AddWaterlineWetSand(MeshBuffers b, float x0, float x1, float z0, float z1,
            float waterY, int salt)
        {
            if (b == null)
                return;

            float landY = waterY + WetSandLandRise;
            float seaY = waterY + WetSandSeaRise;
            float ex = 0.6f;   // 与泡沫带同口径的角部外延

            AddWetSandStrip(b, x0 - ex, x1 + ex, z0, -1f, landY, seaY, true, salt + 1);
            AddWetSandStrip(b, x0 - ex, x1 + ex, z1, 1f, landY, seaY, true, salt + 2);
            AddWetSandStrip(b, z0 - ex, z1 + ex, x0, -1f, landY, seaY, false, salt + 3);
            AddWetSandStrip(b, z0 - ex, z1 + ex, x1, 1f, landY, seaY, false, salt + 4);
        }

        /// <summary>单边湿沙暗带：内缘外扩 0.0±<see cref="WetSandInnerJitter"/>、
        /// 外缘 <see cref="WetSandOuterOffset"/>±<see cref="WetSandOuterJitter"/>，
        /// y 由 <paramref name="landY"/> 缓降到 <paramref name="seaY"/>（都夹在水面窗口内）。</summary>
        static void AddWetSandStrip(MeshBuffers b, float alongFrom, float alongTo, float edgeAt,
            float outwardSign, float landY, float seaY, bool alongX, int salt)
        {
            float t = alongFrom;
            int i = 0;
            while (t < alongTo - 1e-4f)
            {
                float segLen = Mathf.Lerp(FoamSegmentMin, FoamSegmentMax, SceneArtHash.Hash01(salt, i, 3));
                float ta = t;
                float tb = Mathf.Min(alongTo, t + segLen);
                t = tb;

                if (SceneArtHash.Hash01(salt, i, 5) >= WetSandKeepRatio)
                {
                    i++;
                    continue;
                }

                float outA = WetSandOuterOffset + SceneArtHash.SignedHash(salt, i, 7) * WetSandOuterJitter;
                float outB = WetSandOuterOffset + SceneArtHash.SignedHash(salt, i + 1, 7) * WetSandOuterJitter;
                float inA = Mathf.Max(0f, WetSandInnerOffset + SceneArtHash.SignedHash(salt, i, 9) * WetSandInnerJitter);
                float inB = Mathf.Max(0f, WetSandInnerOffset + SceneArtHash.SignedHash(salt, i + 1, 9) * WetSandInnerJitter);
                float outMaxA = Mathf.Max(outA, inA + 0.10f);
                float outMaxB = Mathf.Max(outB, inB + 0.10f);

                if (alongX)
                {
                    b.AddQuad(
                        new Vector3(ta, landY, edgeAt + outwardSign * inA),
                        new Vector3(tb, landY, edgeAt + outwardSign * inB),
                        new Vector3(tb, seaY, edgeAt + outwardSign * outMaxB),
                        new Vector3(ta, seaY, edgeAt + outwardSign * outMaxA),
                        new Vector3(0f, 1f, outwardSign));
                }
                else
                {
                    b.AddQuad(
                        new Vector3(edgeAt + outwardSign * inA, landY, ta),
                        new Vector3(edgeAt + outwardSign * inB, landY, tb),
                        new Vector3(edgeAt + outwardSign * outMaxB, seaY, tb),
                        new Vector3(edgeAt + outwardSign * outMaxA, seaY, ta),
                        new Vector3(outwardSign, 1f, 0f));
                }

                i++;
            }
        }

        /// <summary>
        /// 船体底部：顶面矩形（甲板底缘，y=0）向底部龙骨线（中心轴，y=bottomY）收拢，
        /// 艏艉各一点 —— 低多边形 V 形船体侧板，再叠一根龙骨木。
        /// </summary>
        static void AddShipHullUnderside(MeshBuffers b, float x0, float x1, float z0, float z1,
            float topY, float bottomY, int seed)
        {
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            float inset = 0.12f;      // 距离类 ×2

            Vector3 tl = new Vector3(x0 + inset, topY, z1 - inset);   // -X,+Z
            Vector3 tr = new Vector3(x1 - inset, topY, z1 - inset);   // +X,+Z
            Vector3 br = new Vector3(x1 - inset, topY, z0 + inset);
            Vector3 bl = new Vector3(x0 + inset, topY, z0 + inset);

            float keelHalf = Mathf.Min((x1 - x0) * 0.32f, (z1 - z0) * 0.9f);
            Vector3 bow = new Vector3(cx - keelHalf, bottomY, cz);
            Vector3 stern = new Vector3(cx + keelHalf, bottomY, cz);

            // 艏（-X 端）：三角侧板；艉（+X 端）：三角侧板。
            b.AddQuad(tl, bl, bow, bow, new Vector3(-1f, 0.4f, 0f));
            b.AddQuad(tr, br, stern, stern, new Vector3(1f, 0.4f, 0f));

            // 北 / 南舷：顶边 → 龙骨线。
            b.AddQuad(tl, tr, stern, bow, new Vector3(0f, 0.4f, -1f));
            b.AddQuad(br, bl, bow, stern, new Vector3(0f, 0.4f, 1f));

            // 龙骨木（沿中心轴，强化"船"读感）。
            Vector3 k0 = bow + Vector3.down * 0.12f;
            Vector3 k1 = stern + Vector3.down * 0.12f;
            b.AddBentTube(new[] { k0, k1 }, 0.16f, 0.16f, 5);

            // 水线木带：沿两舷顶缘一圈薄条（暗木，强化侧板分层）。
            float bandY = topY - 0.32f;   // 距离类 ×2
            b.AddQuad(
                new Vector3(x0 + inset, topY, z1 - inset), new Vector3(x1 - inset, topY, z1 - inset),
                new Vector3(x1 - inset, bandY, z1 - inset + 0.04f), new Vector3(x0 + inset, bandY, z1 - inset + 0.04f),
                new Vector3(0f, 0.4f, 1f));
        }

        // 【已删除的旧实现】AddIslandConeUnderside / AddTerraceRockUnderside：
        // 两者都是"矩形包络的圆台/台锥"（AddFrustum 外接椭圆），与不规则岛形轮廓对不上
        // （四角悬空）、也没有"厚底"可言。2026-09-14 起统一走 AddIslandTaperUnderside
        // 的"岛缘轮廓环逐环缩放"，不要再写回矩形包络的锥。
    }
}
