using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt.Showcase
{
    /// <summary>一个建议出生位（脚底贴草皮面；站位朝向由装配器决定）。</summary>
    public readonly struct IslandSpawnPoint
    {
        /// <summary>红队 = 0 / 蓝队 = 1（与 SpawnPlanEntry.TeamIndex 同口径）。</summary>
        public readonly int TeamIndex;

        /// <summary>队内槽位（0 起）。</summary>
        public readonly int Slot;

        /// <summary>脚底所在草皮面高度已含（装配器接 PirateBase 时自行加 LevelGeometry.UnitPivotHeight）。</summary>
        public readonly Vector3 Position;

        /// <summary>构造出生位。</summary>
        public IslandSpawnPoint(int teamIndex, int slot, Vector3 position)
        {
            TeamIndex = teamIndex;
            Slot = slot;
            Position = position;
        }
    }

    /// <summary>
    /// 空岛顶面的**建议出生点表**（纯 C#，无头可测）：第 3 关把角色直接生在空岛上，
    /// 红蓝各 3-4 个、间距 ≥3 单位（用户验收口径），且不得压进任何构图预留区
    /// （遗迹 / 瞭望台 / 老树 / 水潭，<see cref="FloatingIslandComposer.BuildKeepOutZones"/>）。
    ///
    /// 【算法（确定性，拒绝采样）】草皮穹顶的平缓带（r ∈ 0.52-0.72 椭圆比例）上按方位角扫锚点，
    /// 锚点有效 = 离所有预留区 ≥ 簇半径 + 预留区半径 + 点净距；红队取东半区最优锚点、
    /// 蓝队取西半区最优且与红队相距 ≥ <see cref="MinTeamSeparation"/>；每队在锚点周围铺
    /// 2×2 网格（间距 <see cref="Spacing"/>），逐点贴 <see cref="FloatingIslandComposer.PlateauY"/>
    /// 并复检净距。同 spec 必得同一张表（无头断言可复现）。
    /// </summary>
    public static class FloatingIslandSpawnTable
    {
        /// <summary>同队相邻出生位间距（验收要求 ≥3；取 3.2 留一点余量）。</summary>
        public const float Spacing = 3.2f;

        /// <summary>每队格位数（红蓝各 3-4 个角色，表按 4 个给满，装配器可只用前 3 个）。</summary>
        public const int PerTeam = 4;

        /// <summary>出生点离预留区边缘的最小净距。</summary>
        public const float PointClearance = 1.2f;

        /// <summary>2×2 簇的外接半径（对角 ≈2.26，取 2.4 留余量）。</summary>
        public const float ClusterRadius = 2.4f;

        /// <summary>两簇锚点的最小距离（两支小队开局不贴脸）。</summary>
        public const float MinTeamSeparation = 8f;

        /// <summary>
        /// 单个出生位的椭圆半径上限（r = |(x/RX, z/RZ)|）：0.78 保证落在草皮穹顶的平缓带内、
        /// 不贴崖沿（rim 在 r=1.0，带 ±13% 抖动）。
        /// </summary>
        public const float PointMaxR = 0.78f;

        /// <summary>
        /// 生成完整出生点表（红队 <see cref="PerTeam"/> 个在前、蓝队在后）。
        /// 搜索空间内无解时（调参把顶面塞满后可能出现）只返回找到的合法点，调用方按数量兜底。
        /// </summary>
        public static IslandSpawnPoint[] Build(FloatingIslandSpec spec)
        {
            if (spec == null)
                spec = FloatingIslandSpec.Default;

            IslandKeepOutZone[] zones = FloatingIslandComposer.BuildKeepOutZones(spec);
            Vector2 red = PickAnchor(spec, zones, requireEast: true, Vector2.zero);
            Vector2 blue = PickAnchor(spec, zones, requireEast: false, red);

            var result = new List<IslandSpawnPoint>(PerTeam * 2);
            AppendCluster(result, spec, zones, 0, red);
            AppendCluster(result, spec, zones, 1, blue);
            return result.ToArray();
        }

        /// <summary>
        /// 扫描平缓带锚点，取"离所有预留区最远"的一个；
        /// <paramref name="requireEast"/> = true 时只在东半区找（红队），否则只在西半区找
        /// （蓝队）并保证与 <paramref name="otherAnchor"/> 相距 ≥ <see cref="MinTeamSeparation"/>。
        /// 全部候选被预留区挡住时逐级放宽净距（0.6 步长降到 0），保底给出一个可站锚点。
        /// </summary>
        static Vector2 PickAnchor(FloatingIslandSpec spec, IslandKeepOutZone[] zones,
            bool requireEast, Vector2 otherAnchor)
        {
            const int angleSteps = 24;
            float[] scales = { 0.62f, 0.70f, 0.54f };   // 平缓带优先，其次外圈（穹顶外缘略斜但仍是顶面）

            Vector2 best = new Vector2(spec.RadiusX * 0.62f, 0f);
            for (float relax = 0f; relax <= PointClearance + 0.01f; relax += 0.6f)
            {
                float bestScore = float.MinValue;

                for (int a = 0; a < angleSteps; a++)
                {
                    float ang = Mathf.PI * 2f * a / angleSteps;
                    if (requireEast ? Mathf.Cos(ang) < 0.25f : Mathf.Cos(ang) > -0.25f)
                        continue;   // 红队偏东 / 蓝队偏西（含一点中线排斥，避免两簇挤在正南正北）

                    for (int s = 0; s < scales.Length; s++)
                    {
                        float x = Mathf.Cos(ang) * spec.RadiusX * scales[s];
                        float z = Mathf.Sin(ang) * spec.RadiusZ * scales[s];

                        if (otherAnchor.sqrMagnitude > 1e-6f
                            && new Vector2(x - otherAnchor.x, z - otherAnchor.y).magnitude < MinTeamSeparation)
                            continue;

                        // 整簇评估：簇心离预留区 ≥ 簇半径，且 **4 个角点**都在椭圆界内、
                        // 净距 ≥ PointClearance——按整簇打分，不会选出"要裁角"的锚点。
                        if (ClearanceScore(x, z, zones, ClusterRadius, relax) < 0f)
                            continue;

                        float cornerWorst = float.MaxValue;
                        bool cornersOk = true;
                        for (int c = 0; c < 4 && cornersOk; c++)
                        {
                            float cx = x + (c % 2 == 0 ? -1f : 1f) * Spacing * 0.5f;
                            float cz = z + (c < 2 ? -1f : 1f) * Spacing * 0.5f;
                            float nx = cx / spec.RadiusX;
                            float nz = cz / spec.RadiusZ;
                            if (Mathf.Sqrt(nx * nx + nz * nz) > PointMaxR)
                                cornersOk = false;
                            else
                                cornerWorst = Mathf.Min(cornerWorst,
                                    ClearanceScore(cx, cz, zones, 0f, relax));
                        }

                        if (!cornersOk)
                            continue;

                        if (cornerWorst > bestScore)
                        {
                            bestScore = cornerWorst;
                            best = new Vector2(x, z);
                        }
                    }
                }

                if (bestScore >= PointClearance)
                    return best;
            }

            // 全候选被预留区压顶（调参把顶面塞满才会发生）：返回净距最大的那个保底位。
            return best;
        }

        /// <summary>
        /// 锚点净距得分：min(离预留区边缘的距离 − 簇半径 − 放宽量)；≥0 表示可放簇。
        /// 返回 float.MinValue 表示有预留区压顶（不应发生——relax 循环兜底）。
        /// </summary>
        static float ClearanceScore(float x, float z, IslandKeepOutZone[] zones,
            float clusterRadius, float relax)
        {
            float worst = float.MaxValue;
            for (int i = 0; i < zones.Length; i++)
            {
                float dx = x - zones[i].Center.x;
                float dz = z - zones[i].Center.z;
                float gap = Mathf.Sqrt(dx * dx + dz * dz) - zones[i].Radius - clusterRadius - relax;
                if (gap < worst)
                    worst = gap;
            }
            return worst;
        }

        /// <summary>在锚点周围铺 2×2 网格，逐点贴草皮面并复检净距（个别点被挡就丢弃，不断言）。</summary>
        static void AppendCluster(List<IslandSpawnPoint> result, FloatingIslandSpec spec,
            IslandKeepOutZone[] zones, int teamIndex, Vector2 anchor)
        {
            float half = Spacing * 0.5f;
            var offsets = new Vector2[]
            {
                new Vector2(-half, -half), new Vector2(half, -half),
                new Vector2(-half, half), new Vector2(half, half),
            };

            for (int slot = 0; slot < PerTeam; slot++)
            {
                float x = anchor.x + offsets[slot].x;
                float z = anchor.y + offsets[slot].y;

                // 椭圆半径界：簇锚点合格不代表每个角点合格（外角会比锚点多出半格半径）。
                float nx = x / spec.RadiusX;
                float nz = z / spec.RadiusZ;
                if (Mathf.Sqrt(nx * nx + nz * nz) > PointMaxR)
                    continue;

                if (ClearanceScore(x, z, zones, 0f, 0f) < PointClearance)
                    continue;   // 簇级已保证 ≥ClusterRadius+净距，这里只是兜底（数学上不会触发）

                result.Add(new IslandSpawnPoint(teamIndex, slot,
                    new Vector3(x, FloatingIslandComposer.PlateauY(spec, x, z), z)));
            }
        }
    }
}
