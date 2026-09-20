using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 死水区礁石场（纯 C#，确定性，无头可测）。
    ///
    /// 【为什么需要它】M4 八图的紧凑化把地形件全挤到图心条带，实测站面只占图面
    /// 3.9%–13.8%（其余 86%–96% 是无人可到的空海，见 docs/审计/地图设计审计报告.md §一.2）。
    /// 用户已裁决"图跨度维持现状、空尺度用**内容填充**解决"（方案 D），故这里补一层
    /// **不参与玩法的礁石/浅滩视觉填充**：把大片纯色海面读成"有礁、有浅滩、有残骸的海"。
    ///
    /// 【为什么不是站面】填站面会连带改连通性、AI 落点与出生高度（站面 box 是玩法真值，
    /// 见 WorldMapRules）。礁石场刻意只产出 <see cref="WorldPropPlacement"/>（纯视觉件，
    /// 无碰撞、无栅格），与 <see cref="HorizonFeatureRules"/> 同类——规则层出确定性摆放，
    /// 装配层走既有的道具实例化路径。要"能站上去"的填充件仍由 WorldMapCatalog 手摆（语义建图）。
    ///
    /// 【每图一个 profile】8 张图若用同一套密度与混比，会加重"俯视同质化"
    /// （审计 §二.6）。故密度/混比按图的语义给：沉船墓场多船板残片、火山海多大海蚀岩、
    /// 红树林多小礁根、沉都用断柱残拱。种子取 <see cref="WorldMapDefinition.HorizonSeed"/>
    /// （逐图唯一），保证同图每次装配完全一致、测试可钉。
    /// </summary>
    public static class ReefFieldRules
    {
        /// <summary>礁石与站面边缘的净空（u）：太贴边会从 kit 裙边探进可玩面，读成"地面长石头"。</summary>
        public const float StandClearance = 2.5f;

        /// <summary>出生点周围的留空半径（u）：出生群脚下不放礁石，保证"出生群"读得出来。</summary>
        public const float SpawnClearRadius = 11f;

        /// <summary>出生视廊：沿「本队质心→敌方质心」轴、从出生点起的这段长度内不放礁石（u）。
        /// 这是"出生点面向战场、画面 ≥1/3 是可读战场"这条构图规则在**海面**上的落实——
        /// 视廊里只有海，玩家的视线不会被近处礁石切断。</summary>
        public const float SightCorridorLength = 72f;

        /// <summary>出生视廊的半宽（u）。</summary>
        public const float SightCorridorHalfWidth = 11f;

        /// <summary>地图内缩（u）：贴图边的礁石会被裙边海面/相机远裁剪掉，浪费预算。</summary>
        public const float MapInset = 7f;

        /// <summary>候选点采样格边长（u）与同格内的抖动幅度——抖动让点阵不像网格。</summary>
        public const float CellSize = 15f;

        /// <summary>礁石之间的最小间距（u），避免叠成一坨。</summary>
        public const float MinSpacing = 6.5f;

        /// <summary>
        /// 该资产是否允许落在**无站面的开阔水面**上（半潜/漂浮读法）。
        ///
        /// 白名单两类：
        /// ① 石质件（Rock* / Driftwood / 遗迹断柱残拱）——基部沉进水里读成礁石/残迹，是礁石场的本体；
        /// ② 浮件（浮标、未搁浅的船）——由 FloatingPropView 负责随浪起伏。
        /// 其余（篝火/木箱/棕榈/草/炮位…）落水一律视为数据漂移：浮在浪上的篝火是最刺眼的穿帮，
        /// 装配层会**跳过并告警**，测试 <c>WorldMapPropPlacementTests</c> 把这条钉死。
        /// </summary>
        public static bool AllowedOnOpenWater(string asset)
        {
            if (string.IsNullOrEmpty(asset))
                return false;
            if (asset.StartsWith("Rock"))
                return true;
            if (asset == "Driftwood" || asset == "RuinColumnBroken" || asset == "RuinArch")
                return true;
            // 浮件只认"浮标"这一族：<see cref="Water.FloatingPropView"/> 的判据里还有
            // "名字含 Boat/Ship 且不含 Beached"，那是回答「已经在贴水位置的东西该不该随浪起伏」
            // 的宽松启发式；搬到"该不该放在开阔水面"这个问题上会把 ShipWheelPost（船轮柱）
            // 判成浮件——它是钉在甲板上的陆地道具，落水就是穿帮。两处口径刻意不同，故不委托。
            return asset.Contains("Buoy");
        }

        /// <summary>每图的礁石场档案：目标覆盖密度（每 1000 u² 开阔水面放几块）+ 资产混比。</summary>
        readonly struct Profile
        {
            /// <summary>每 1000 u² 开阔水面的礁石数。</summary>
            public readonly float DensityPer1000;
            /// <summary>资产名权重表（同名多份 = 更高概率）。</summary>
            public readonly string[] Mix;

            public Profile(float densityPer1000, string[] mix)
            {
                DensityPer1000 = densityPer1000;
                Mix = mix;
            }
        }

        /// <summary>
        /// 逐图档案。混比按语义给（注释即设计意图，改这里请同步 WorldMapCatalog 的图注）：
        ///   wreck_hymn   沉船墓场——碎礁 + 船板残片，密度高（"整片海都是碎料"）；
        ///   atoll_ring   环礁——礁盘（RockFlat）为主，泻湖内浅滩感；
        ///   ghost_harbor 鬼火港——港湾内留航道（密度中），断柱残拱混入（沉港遗迹）；
        ///   turtle_back  巨龟环脊——环脊外碎礁环绕，大石少；
        ///   mangrove_veil 红树帷幔——小礁（RockS）密布，读成红树根系与泥滩点礁；
        ///   spiral_throne 螺旋王座——火山岩，大石（RockL）为主，稀疏而块头大；
        ///   storm_cape   雷暴岬——海蚀柱，大石 + 断柱，密度中；
        ///   sunken_gate  沉都之门——沉没城区，礁盘 + 断柱残拱最多。
        /// </summary>
        static Profile ProfileOf(WorldMapDefinition map)
        {
            switch (map.Id)
            {
                case "wreck_hymn":
                    return new Profile(5.2f, new[] { "RockS", "RockS", "RockM", "RockFlat", "Driftwood" });
                case "atoll_ring":
                    return new Profile(4.4f, new[] { "RockFlat", "RockFlat", "RockS", "RockM" });
                case "ghost_harbor":
                    return new Profile(4.0f, new[] { "RockM", "RockFlat", "RuinColumnBroken", "RuinArch", "RockS" });
                case "turtle_back":
                    return new Profile(4.2f, new[] { "RockS", "RockM", "RockM", "RockFlat" });
                case "mangrove_veil":
                    return new Profile(5.0f, new[] { "RockS", "RockS", "RockS", "RockM", "Driftwood" });
                case "spiral_throne":
                    return new Profile(3.4f, new[] { "RockL", "RockL", "RockM", "RockFlat" });
                case "storm_cape":
                    return new Profile(3.8f, new[] { "RockL", "RockM", "RuinColumnBroken", "RockS" });
                case "sunken_gate":
                    return new Profile(4.6f, new[] { "RockFlat", "RuinColumnBroken", "RuinArch", "RockM", "RockS" });
                default:
                    return new Profile(4.0f, new[] { "RockS", "RockM", "RockFlat" });
            }
        }

        /// <summary>
        /// 产出整图的礁石场摆放（确定性）。候选点 = 内缩图面后的抖动格点，逐点过滤：
        /// 离站面边缘 ≥ <see cref="StandClearance"/>、离出生点 ≥ <see cref="SpawnClearRadius"/>、
        /// 不落在出生视廊内、与已选点间距 ≥ <see cref="MinSpacing"/>；按图面面积算目标数量后取前 N。
        /// </summary>
        public static List<WorldPropPlacement> Place(WorldMapDefinition map)
        {
            var result = new List<WorldPropPlacement>();
            var boxes = WorldMapRules.AllStandBoxes(map);
            Profile profile = ProfileOf(map);
            if (profile.Mix.Length == 0)
                return result;

            // ---- 出生视廊（两条：两队各一条，互为目标）----
            var centroids = new Vector2[2];
            var centroidValid = new bool[2];
            for (int team = 0; team < 2; team++)
            {
                Vector2 sum = Vector2.zero;
                int n = 0;
                for (int i = 0; i < map.Spawns.Count; i++)
                {
                    if (map.Spawns[i].TeamIndex != team)
                        continue;
                    sum += new Vector2(map.Spawns[i].X, map.Spawns[i].Z);
                    n++;
                }
                if (n > 0)
                {
                    centroids[team] = sum / n;
                    centroidValid[team] = true;
                }
            }

            // ---- 目标数量：开阔水面面积 × 密度 ----
            float openWater = OpenWaterArea(map, boxes);
            int target = Mathf.RoundToInt(openWater / 1000f * profile.DensityPer1000);
            if (target <= 0)
                return result;

            // ---- 抖动格点扫描（确定性：种子 = 图种子）----
            uint h = (uint)(map.HorizonSeed * 2246822519u + 374761393u);
            float inset = MapInset;
            var chosen = new List<Vector2>(target);
            for (float gz = inset; gz <= map.SpanZ - inset && chosen.Count < target; gz += CellSize)
            {
                for (float gx = inset; gx <= map.SpanX - inset && chosen.Count < target; gx += CellSize)
                {
                    h = h * 1664525u + 1013904223u;
                    float jx = (((h >> 9) % 1000u) / 1000f - 0.5f) * CellSize * 0.85f;
                    h = h * 1664525u + 1013904223u;
                    float jz = (((h >> 9) % 1000u) / 1000f - 0.5f) * CellSize * 0.85f;
                    var p = new Vector2(gx + jx, gz + jz);

                    if (!Accept(p, boxes, chosen, map, centroids, centroidValid))
                        continue;

                    h = h * 1664525u + 1013904223u;
                    string asset = profile.Mix[(int)((h >> 11) % (uint)profile.Mix.Length)];
                    h = h * 1664525u + 1013904223u;
                    float yaw = ((h >> 7) % 360u);

                    chosen.Add(p);
                    // 目录 y 给静水面：装配层对无站面的点会吸附到 WaterSurfaceY，两边同值即"半潜"读法。
                    result.Add(new WorldPropPlacement(asset, p.x, LevelGeometry.WaterSurfaceY, p.y, yaw));
                }
            }
            return result;
        }

        /// <summary>候选点是否可放礁石。</summary>
        static bool Accept(Vector2 p, List<WorldMapRules.WorldBox> boxes, List<Vector2> chosen,
            WorldMapDefinition map, Vector2[] centroids, bool[] centroidValid)
        {
            // ① 不压站面（含净空）
            for (int i = 0; i < boxes.Count; i++)
            {
                if (WorldMapRules.Distance(boxes[i], p) < StandClearance)
                    return false;
            }

            // ② 出生群脚下留空
            for (int i = 0; i < map.Spawns.Count; i++)
            {
                var sp = new Vector2(map.Spawns[i].X, map.Spawns[i].Z);
                if (Vector2.Distance(sp, p) < SpawnClearRadius)
                    return false;
            }

            // ③ 出生视廊留空（只对朝敌方向那条轴判定：以两队质心连线为中轴，
            //    从各自出生质心出发的 SightCorridorLength 段内、半宽 SightCorridorHalfWidth 的矩形内不放）
            if (centroidValid[0] && centroidValid[1])
            {
                var axis = centroids[1] - centroids[0];
                if (axis.sqrMagnitude > 1e-4f)
                {
                    axis.Normalize();
                    var normal = new Vector2(-axis.y, axis.x);
                    for (int team = 0; team < 2; team++)
                    {
                        Vector2 rel = p - centroids[team];
                        float along = Vector2.Dot(rel, axis) * (team == 0 ? 1f : -1f);
                        float lateral = Mathf.Abs(Vector2.Dot(rel, normal));
                        if (along >= -SpawnClearRadius && along <= SightCorridorLength
                            && lateral <= SightCorridorHalfWidth)
                            return false;
                    }
                }
            }

            // ④ 与已选点保持间距
            for (int i = 0; i < chosen.Count; i++)
            {
                if (Vector2.Distance(chosen[i], p) < MinSpacing)
                    return false;
            }
            return true;
        }

        /// <summary>开阔水面面积（图面 − 站面并集，用 2u 栅格近似；与连通性测试同一套栅格口径）。</summary>
        public static float OpenWaterArea(WorldMapDefinition map, List<WorldMapRules.WorldBox> boxes)
        {
            if (!WorldMapRules.TryRasterize(map, out int w, out int d, out int[] blocks))
                return map.SpanX * map.SpanZ;
            int ground = 0;
            for (int i = 0; i < blocks.Length; i++)
                if (blocks[i] > 0)
                    ground++;
            float tileArea = WorldMapRules.RasterTileSize * WorldMapRules.RasterTileSize;
            return map.SpanX * map.SpanZ - ground * tileArea;
        }
    }
}
