using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界地图规则层（纯 C#，无头可测）：站面 box 的世界展开、栅格化、连通性与布阵校验。
    /// 契约：docs/M4-大海域世界化.md §2.2（连通性硬约束）与 §4.2（站面平直机制）。
    ///
    /// 【连通性判据】节点 = 站面 box；无向边 = 两 box 的水平间隙 ≤ <see cref="MaxJumpGap"/>
    /// 且 |顶面高差| ≤ <see cref="MaxUpStep"/>（来回都能跳）。全图所有承载出生点的 box
    /// 必须落在同一连通分量。水平间隙 = 两旋转矩形间的最小平面距离（分离距离，重叠时为 0）。
    /// </summary>
    public static class WorldMapRules
    {
        /// <summary>跳跃链允许的最大水平间隙（u）。主链目标 6–12，极限 13（§2.2）。</summary>
        public const float MaxJumpGap = 13f;

        /// <summary>上行方向允许的最大顶面高差（u）（§1）。</summary>
        public const float MaxUpStep = 3.5f;

        /// <summary>栅格化：世界地图的 1 瓦片 = <see cref="LevelGeometry.TileWorldSize"/>（2u）。</summary>
        public const float RasterTileSize = 2f;

        /// <summary>出生点离站面边缘的最小余量（u）；角色占地 ≈0.5。</summary>
        public const float SpawnEdgeMargin = 1.2f;

        // ------------------------------------------------------------------
        // box 世界展开
        // ------------------------------------------------------------------

        /// <summary>世界系站面矩形（旋转 + 平移已合成）。</summary>
        public readonly struct WorldBox
        {
            public readonly Vector2 Center;
            public readonly Vector2 Size;
            public readonly float TopY;
            /// <summary>总转角（度）：placement.Yaw + box.Yaw。</summary>
            public readonly float YawDeg;

            public WorldBox(Vector2 center, Vector2 size, float topY, float yawDeg)
            {
                Center = center;
                Size = size;
                TopY = topY;
                YawDeg = yawDeg;
            }
        }

        /// <summary>把一个摆放件的资产系 box 表展开到世界系（矩形中心 = pos + R(yaw)·c，总转角 = yaw+boxYaw）。</summary>
        public static List<WorldBox> ExpandPlacement(in WorldKitPlacement placement)
        {
            var boxes = WorldMapStandables.BoxesOf(placement.Asset);
            var result = new List<WorldBox>(boxes == null ? 0 : boxes.Count);
            if (boxes == null)
                return result;

            float rad = placement.YawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            for (int i = 0; i < boxes.Count; i++)
            {
                WorldStandBox box = boxes[i];
                Vector2 c = box.Center;
                result.Add(new WorldBox(
                    new Vector2(placement.Position.x + (c.x * cos + c.y * sin),
                                placement.Position.z + (-c.x * sin + c.y * cos)),
                    box.Size,
                    box.TopY,
                    placement.YawDeg + box.YawDeg));
            }
            return result;
        }

        /// <summary>整图全部地形/船坞件的站面 box（世界系，顺序 = 摆放顺序）。</summary>
        public static List<WorldBox> AllStandBoxes(WorldMapDefinition map)
        {
            var all = new List<WorldBox>();
            for (int i = 0; i < map.Terrain.Count; i++)
                all.AddRange(ExpandPlacement(map.Terrain[i]));
            return all;
        }

        // ------------------------------------------------------------------
        // 几何：旋转矩形距离 / 点包含
        // ------------------------------------------------------------------

        /// <summary>点是否在（旋转）矩形内。</summary>
        public static bool Contains(in WorldBox box, Vector2 p)
        {
            Vector2 d = p - box.Center;
            float rad = box.YawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            // 逆变换（world→local）：Unity 正变换是 (x,z)→(x·cos+z·sin, −x·sin+z·cos)，
            // 其逆为 (x·cos−z·sin, x·sin+z·cos)。
            Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            return Mathf.Abs(local.x) <= box.Size.x * 0.5f && Mathf.Abs(local.y) <= box.Size.y * 0.5f;
        }

        /// <summary>点到（旋转）矩形的平面距离（在矩形内为 0）。</summary>
        public static float Distance(in WorldBox box, Vector2 p)
        {
            Vector2 d = p - box.Center;
            float rad = box.YawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            float dx = Mathf.Max(Mathf.Abs(local.x) - box.Size.x * 0.5f, 0f);
            float dy = Mathf.Max(Mathf.Abs(local.y) - box.Size.y * 0.5f, 0f);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>点在 box 内时到四条边的最小净空（box 外返回负值）。</summary>
        public static float EdgeClearance(in WorldBox box, Vector2 p)
        {
            Vector2 d = p - box.Center;
            float rad = box.YawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            float cx = box.Size.x * 0.5f - Mathf.Abs(local.x);
            float cy = box.Size.y * 0.5f - Mathf.Abs(local.y);
            return Mathf.Min(cx, cy);
        }

        /// <summary>两个旋转矩形的平面分离距离（重叠/相接为 0）。精确值：凸多边形最近点对必含至少一个顶点，
        /// 故取「A 各顶点到 B 的距离」与「B 各顶点到 A 的距离」的最小值。</summary>
        public static float RectDistance(in WorldBox a, in WorldBox b)
        {
            if (RectsOverlap(a, b))
                return 0f;
            float best = float.MaxValue;
            var cornersA = Corners(a);
            var cornersB = Corners(b);
            for (int i = 0; i < 4; i++)
            {
                best = Mathf.Min(best, Distance(b, cornersA[i]));
                best = Mathf.Min(best, Distance(a, cornersB[i]));
            }
            return best;
        }

        /// <summary>旋转矩形的 4 个角点（世界系）。正变换：Unity yaw P 把 (x,z) 转到
        /// (x·cosP + z·sinP, −x·sinP + z·cosP)。</summary>
        static Vector2[] Corners(in WorldBox box)
        {
            float rad = box.YawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float hx = box.Size.x * 0.5f, hy = box.Size.y * 0.5f;
            return new Vector2[]
            {
                box.Center + new Vector2(hx * cos + hy * sin, -hx * sin + hy * cos),
                box.Center + new Vector2(-hx * cos + hy * sin, hx * sin + hy * cos),
                box.Center + new Vector2(-hx * cos - hy * sin, hx * sin - hy * cos),
                box.Center + new Vector2(hx * cos - hy * sin, -hx * sin - hy * cos),
            };
        }

        /// <summary>SAT 重叠判定：两旋转矩形在彼此 2+2 条轴法线上的投影均有交集才算重叠。</summary>
        public static bool RectsOverlap(in WorldBox a, in WorldBox b)
        {
            var cornersA = Corners(a);
            var cornersB = Corners(b);
            return SeparatedOnAxis(cornersA, cornersB, a.YawDeg) == false
                && SeparatedOnAxis(cornersA, cornersB, a.YawDeg + 90f) == false
                && SeparatedOnAxis(cornersA, cornersB, b.YawDeg) == false
                && SeparatedOnAxis(cornersA, cornersB, b.YawDeg + 90f) == false;
        }

        /// <summary>两矩形在给定轴法线方向上是否分离（True = 分离）。</summary>
        static bool SeparatedOnAxis(Vector2[] cornersA, Vector2[] cornersB, float axisYawDeg)
        {
            float rad = axisYawDeg * Mathf.Deg2Rad;
            var axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float minA = float.MaxValue, maxA = float.MinValue;
            float minB = float.MaxValue, maxB = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float pa = Vector2.Dot(cornersA[i], axis);
                float pb = Vector2.Dot(cornersB[i], axis);
                minA = Mathf.Min(minA, pa); maxA = Mathf.Max(maxA, pa);
                minB = Mathf.Min(minB, pb); maxB = Mathf.Max(maxB, pb);
            }
            return maxA < minB || maxB < minA;
        }

        // ------------------------------------------------------------------
        // 连通性（§2.2）
        // ------------------------------------------------------------------

        /// <summary>两 box 之间能否来回跳（无向边判据）。</summary>
        public static bool CanHop(in WorldBox a, in WorldBox b)
        {
            // 平面重叠（如梯田/甲板分层）视为直接相邻，只看高差。
            float gap = RectsOverlap(a, b) ? 0f : RectDistance(a, b);
            return gap <= MaxJumpGap && Mathf.Abs(a.TopY - b.TopY) <= MaxUpStep;
        }

        /// <summary>
        /// 连通性校验：返回不可达的「承载出生点的 box」索引列表（空 = 通过）。
        /// 以出生点所在 box 为必达集，从 0 号 box BFS。
        /// </summary>
        public static List<int> UnreachableSpawnBoxes(WorldMapDefinition map, List<WorldBox> boxes)
        {
            int n = boxes.Count;
            var adjacency = new List<int>[n];
            for (int i = 0; i < n; i++)
                adjacency[i] = new List<int>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (CanHop(boxes[i], boxes[j]))
                    {
                        adjacency[i].Add(j);
                        adjacency[j].Add(i);
                    }
                }
            }

            var visited = new bool[n];
            var queue = new Queue<int>();
            if (n > 0)
            {
                visited[0] = true;
                queue.Enqueue(0);
            }
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                for (int k = 0; k < adjacency[cur].Count; k++)
                {
                    int next = adjacency[cur][k];
                    if (!visited[next])
                    {
                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }

            var missing = new List<int>();
            for (int s = 0; s < map.Spawns.Count; s++)
            {
                int boxIndex = BoxIndexOf(boxes, new Vector2(map.Spawns[s].X, map.Spawns[s].Z));
                if (boxIndex < 0)
                    continue; // 出生点不在任何 box 上 → 由 ValidateSpawns 单独报
                if (!visited[boxIndex] && !missing.Contains(boxIndex))
                    missing.Add(boxIndex);
            }
            return missing;
        }

        /// <summary>出生点校验：返回问题描述列表（空 = 全部出生点落在站面顶面且离边有余量）。
        /// 只查「离边余量」——出生 y 由 <see cref="HeightAtWorld"/> 取最高覆盖面，无"更高覆盖"问题可言。</summary>
        public static List<string> ValidateSpawns(WorldMapDefinition map, List<WorldBox> boxes)
        {
            var problems = new List<string>();
            for (int s = 0; s < map.Spawns.Count; s++)
            {
                WorldMapSpawn spawn = map.Spawns[s];
                var p = new Vector2(spawn.X, spawn.Z);
                int boxIndex = BoxIndexOf(boxes, p);
                if (boxIndex < 0)
                {
                    problems.Add(string.Format("[{0}] spawn#{1} ({2:F1},{3:F1}) 不在任何站面 box 上",
                        map.Id, s, p.x, p.y));
                    continue;
                }
                WorldBox box = boxes[boxIndex];
                float clearance = EdgeClearance(box, p);
                if (clearance < SpawnEdgeMargin)
                {
                    problems.Add(string.Format(
                        "[{0}] spawn#{1} ({2:F1},{3:F1}) 净空 {4:F2} < {5}u（box c=({6:F1},{7:F1}) s=({8:F1},{9:F1}) top={10:F1} yaw={11:F0}）",
                        map.Id, s, p.x, p.y, clearance, SpawnEdgeMargin,
                        box.Center.x, box.Center.y, box.Size.x, box.Size.y, box.TopY, box.YawDeg));
                }
            }
            return problems;
        }

        /// <summary>点所在的「顶面最高」box 索引；不在任何 box 内返回 -1。</summary>
        public static int BoxIndexOf(List<WorldBox> boxes, Vector2 p)
        {
            int best = -1;
            float bestTop = float.MinValue;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (Contains(boxes[i], p) && boxes[i].TopY > bestTop)
                {
                    best = i;
                    bestTop = boxes[i].TopY;
                }
            }
            return best;
        }

        /// <summary>世界点的地表顶高（该点覆盖的最高 box 顶面）；无 box 覆盖返回 <see cref="LevelGeometry.WaterSurfaceY"/>。</summary>
        public static float HeightAtWorld(List<WorldBox> boxes, Vector2 p, int excludeIndex = -1)
        {
            float top = LevelGeometry.WaterSurfaceY;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (i != excludeIndex && Contains(boxes[i], p) && boxes[i].TopY > top)
                    top = boxes[i].TopY;
            }
            return top;
        }

        // ------------------------------------------------------------------
        // 栅格化（进 TileTerrainGrid 块表）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把整图站面栅格化为块表：格中心落在 box 内 → blocks = round(TopY / 0.5)（1 块 = 0.5u），
        /// 重叠区取最高 box。返回 (widthTiles, depthTiles, blocks)；blocks 为 null 表示地图无效。
        /// </summary>
        public static bool TryRasterize(WorldMapDefinition map, out int widthTiles, out int depthTiles, out int[] blocks)
        {
            widthTiles = Mathf.CeilToInt(map.SpanX / RasterTileSize);
            depthTiles = Mathf.CeilToInt(map.SpanZ / RasterTileSize);
            blocks = null;
            var boxes = AllStandBoxes(map);
            if (boxes.Count == 0)
                return false;

            blocks = new int[widthTiles * depthTiles];
            for (int gz = 0; gz < depthTiles; gz++)
            {
                for (int gx = 0; gx < widthTiles; gx++)
                {
                    var center = new Vector2((gx + 0.5f) * RasterTileSize, (gz + 0.5f) * RasterTileSize);
                    float top = HeightAtWorld(boxes, center);
                    if (top > LevelGeometry.WaterSurfaceY)
                        blocks[gx + gz * widthTiles] = Mathf.Max(1, Mathf.RoundToInt(top / 0.5f));
                }
            }
            return true;
        }
    }
}
