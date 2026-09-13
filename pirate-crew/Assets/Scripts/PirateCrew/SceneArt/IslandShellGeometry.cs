using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>地形视觉壳的可调参数（默认值 = 场景文档 §3.1 的【AI 提案】取值）。</summary>
    public struct IslandShellSettings
    {
        /// <summary>顶面边缘倒角宽度（世界单位）。【依据场景文档 §3.1「0.15 单位宽、45° 倒角」】</summary>
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

        /// <summary>默认参数（= 场景文档 §3.1 的取值）。</summary>
        public static IslandShellSettings Default
        {
            get
            {
                return new IslandShellSettings
                {
                    ChamferWidth = 0.15f,
                    ChamferHeight = 0.15f,
                    SideLayers = 3,
                    LayerRecess = 0.04f,
                    BoundaryJitter = 0.08f,
                    SkirtBottomY = -0.6f,
                    LowPlateYOffset = 0.006f,
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
    ///   边缘 0.15 宽 45° 倒角、侧面 3 段岩层带凹缝、同列沿 Z 的剪影扰动、边界台地外侧下延成裙边（到 y=-0.6）。
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

            float x0 = gx, x1 = gx + 1f, z0 = gy, z1 = gy + 1f;

            // 顶面（内缩 c）：与该格地表严格等高。
            b.AddQuad(
                new Vector3(x0 + c, surfaceY, z0 + c),
                new Vector3(x1 - c, surfaceY, z0 + c),
                new Vector3(x1 - c, surfaceY, z1 - c),
                new Vector3(x0 + c, surfaceY, z1 - c),
                Vector3.up);

            float chamferBottomY = surfaceY - ch;
            float skirtY = Mathf.Min(chamferBottomY, s.SkirtBottomY);

            // ---- 四面：倒角带 + 岩层侧壁 + 裙边 ----
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                c, layers, s, ShellSide.North);
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                c, layers, s, ShellSide.South);
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                c, layers, s, ShellSide.West);
            AddSide(b, grid, gx, gy, x0, x1, z0, z1, surfaceY, chamferBottomY, skirtY,
                c, layers, s, ShellSide.East);
        }

        enum ShellSide { North, South, West, East }

        static void AddSide(MeshBuffers b, TileTerrainGrid grid, int gx, int gy,
            float x0, float x1, float z0, float z1,
            float surfaceY, float chamferBottomY, float skirtY,
            float c, int layers, in IslandShellSettings s, ShellSide side)
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
            if (outside)
            {
                // 边界台地的外侧：下延成裙边，任何角度都看不到方块底面的悬空边。
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

                // 剪影扰动只作用于最下一层：沿**墙面的法线方向**（inwardAxis）进/出抖动，
                // 让同列沿 Z 的 17 格长墙不再是"一堵直线墙"（场景文档 §3.1 ③ 的 X 向 ±0.05-0.12）。
                float jitterA = layer == layers - 1
                    ? SceneArtHash.SignedHash(gx, gy, sideIndex * 97) * s.BoundaryJitter
                    : 0f;
                float jitterD = layer == layers - 1
                    ? SceneArtHash.SignedHash(gx, gy, sideIndex * 97 + 7) * s.BoundaryJitter
                    : 0f;

                Vector3 jitterOffsetA = inwardAxis * jitterA;
                Vector3 jitterOffsetD = inwardAxis * jitterD;

                Vector3 topA = a + inwardAxis * insetTop + Vector3.up * (yTop - chamferBottomY) + jitterOffsetA;
                Vector3 topD = d + inwardAxis * insetTop + Vector3.up * (yTop - chamferBottomY) + jitterOffsetD;
                Vector3 botA = a + inwardAxis * insetBot + Vector3.up * (yBot - chamferBottomY) + jitterOffsetA;
                Vector3 botD = d + inwardAxis * insetBot + Vector3.up * (yBot - chamferBottomY) + jitterOffsetD;

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
            float x0 = gx, x1 = gx + 1f, z0 = gy, z1 = gy + 1f;
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

        /// <summary>把整张网格的 0 块格建成一个合并的湿沙贴片层（潮沟）。</summary>
        public static MeshBuffers BuildLowZone(TileTerrainGrid grid, float yOffset)
        {
            var b = new MeshBuffers();
            if (grid == null)
                return b;

            for (int gy = 0; gy < grid.DepthTiles; gy++)
            {
                for (int gx = 0; gx < grid.WidthTiles; gx++)
                {
                    if (grid.BlocksAt(gx, gy) <= 0)
                        AddLowZonePlate(b, gx, gy, yOffset);
                }
            }

            return b;
        }

        /// <summary>
        /// 竞技场外一圈"潮间带坡"：由边界 <paramref name="innerOffset"/> 处（y=innerY）
        /// 缓降到 <paramref name="outerOffset"/> 处（y=outerY）。四边各成一条带，拐角由南北带补满。
        /// 【依据场景文档 §3.3「1.5-3 单位宽湿沙坡，从 y=0 缓降到 y=-0.6」】
        /// </summary>
        public static void AddOffsetBand(MeshBuffers b, float arenaWidth, float arenaDepth,
            float innerOffset, float outerOffset, float innerY, float outerY, float segmentLength)
        {
            if (b == null || outerOffset <= innerOffset)
                return;

            float seg = Mathf.Max(0.25f, segmentLength);
            float ix0 = -innerOffset, ix1 = arenaWidth + innerOffset;
            float iz0 = -innerOffset, iz1 = arenaDepth + innerOffset;
            float ox0 = -outerOffset, ox1 = arenaWidth + outerOffset;
            float oz0 = -outerOffset, oz1 = arenaDepth + outerOffset;

            // 北 / 南带（沿 X 铺，含四角的延伸段）。
            AddStripAlongX(b, ix0, ix1, iz0, oz0, innerY, outerY, seg, -1f);
            AddStripAlongX(b, ix0, ix1, iz1, oz1, innerY, outerY, seg, 1f);

            // 西 / 东带（沿 Z 铺，只覆盖竞技场纵深，拐角交给南北带）。
            AddStripAlongZ(b, iz0, iz1, ix0, ox0, innerY, outerY, seg, -1f);
            AddStripAlongZ(b, iz0, iz1, ix1, ox1, innerY, outerY, seg, 1f);
        }

        static void AddStripAlongX(MeshBuffers b, float xFrom, float xTo, float innerZ, float outerZ,
            float innerY, float outerY, float seg, float outwardSign)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(xTo - xFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                float xa = Mathf.Lerp(xFrom, xTo, i / (float)steps);
                float xb = Mathf.Lerp(xFrom, xTo, (i + 1) / (float)steps);
                var ia = new Vector3(xa, innerY, innerZ);
                var ib = new Vector3(xb, innerY, innerZ);
                var ob = new Vector3(xb, outerY, outerZ);
                var oa = new Vector3(xa, outerY, outerZ);
                b.AddQuad(ia, ib, ob, oa, new Vector3(0f, 1f, outwardSign));
            }
        }

        static void AddStripAlongZ(MeshBuffers b, float zFrom, float zTo, float innerX, float outerX,
            float innerY, float outerY, float seg, float outwardSign)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(zTo - zFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                float za = Mathf.Lerp(zFrom, zTo, i / (float)steps);
                float zb = Mathf.Lerp(zFrom, zTo, (i + 1) / (float)steps);
                var ia = new Vector3(innerX, innerY, za);
                var ib = new Vector3(innerX, innerY, zb);
                var ob = new Vector3(outerX, outerY, zb);
                var oa = new Vector3(outerX, outerY, za);
                b.AddQuad(ia, ib, ob, oa, new Vector3(outwardSign, 1f, 0f));
            }
        }

        /// <summary>
        /// 环形**水平**带（泡沫线/暗水带）：在边界外 <paramref name="innerOffset"/> ~
        /// <paramref name="outerOffset"/> 之间铺一条 y 恒定的环带。四边各一条，拐角由南北带补满。
        /// </summary>
        public static void AddFlatRingBand(MeshBuffers b, float arenaWidth, float arenaDepth,
            float innerOffset, float outerOffset, float y, float segmentLength)
        {
            if (b == null || outerOffset <= innerOffset)
                return;

            float seg = Mathf.Max(0.25f, segmentLength);
            float ix0 = -innerOffset, ix1 = arenaWidth + innerOffset;
            float iz0 = -innerOffset, iz1 = arenaDepth + innerOffset;
            float ox0 = -outerOffset, ox1 = arenaWidth + outerOffset;
            float oz0 = -outerOffset, oz1 = arenaDepth + outerOffset;

            AddRingStripAlongX(b, ix0, ix1, iz0, oz0, y, seg, -1f);
            AddRingStripAlongX(b, ix0, ix1, iz1, oz1, y, seg, 1f);
            AddRingStripAlongZ(b, iz0, iz1, ix0, ox0, y, seg, -1f);
            AddRingStripAlongZ(b, iz0, iz1, ix1, ox1, y, seg, 1f);
        }

        static void AddRingStripAlongX(MeshBuffers b, float xFrom, float xTo, float innerZ, float outerZ,
            float y, float seg, float outwardSign)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(xTo - xFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                float xa = Mathf.Lerp(xFrom, xTo, i / (float)steps);
                float xb = Mathf.Lerp(xFrom, xTo, (i + 1) / (float)steps);
                b.AddQuad(
                    new Vector3(xa, y, innerZ), new Vector3(xb, y, innerZ),
                    new Vector3(xb, y, outerZ), new Vector3(xa, y, outerZ),
                    new Vector3(0f, 1f, outwardSign));
            }
        }

        static void AddRingStripAlongZ(MeshBuffers b, float zFrom, float zTo, float innerX, float outerX,
            float y, float seg, float outwardSign)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(zTo - zFrom) / seg));
            for (int i = 0; i < steps; i++)
            {
                float za = Mathf.Lerp(zFrom, zTo, i / (float)steps);
                float zb = Mathf.Lerp(zFrom, zTo, (i + 1) / (float)steps);
                b.AddQuad(
                    new Vector3(innerX, y, za), new Vector3(innerX, y, zb),
                    new Vector3(outerX, y, zb), new Vector3(outerX, y, za),
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
    }
}
