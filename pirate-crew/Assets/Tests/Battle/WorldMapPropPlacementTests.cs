using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle;
using PirateCrew.Battle.WorldMaps;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 世界地图**摆件层**的硬约束（docs/审计/地图设计审计报告.md §二.6/§二.8 的测试化）：
    ///
    /// 1. 陆地道具不许落水——紧凑化后目录里的坐标会漂到海里，而装配层会把它们吸附到静水面，
    ///    于是"浮在浪上的篝火/木箱"成了实拍里最刺眼的一类穿帮。白名单（石质件/浮件）之外一律判死。
    /// 2. 死水礁石场必须确定性、且不侵占站面/出生群/出生视廊——它填补的是"无人可到的空海"，
    ///    一旦压到可玩面或挡住出生视线，就变成玩法障碍而不是背景。
    /// 3. 密度与构图规则：站面覆盖率、内容跨度、双方接敌距离——对应"空尺度用内容填充解决"
    ///    （方案 D）与"出生点面向战场"两条裁决。阈值是【提案/待定】，实拍验收后再定稿。
    /// </summary>
    public class WorldMapPropPlacementTests
    {
        static IEnumerable<WorldMapDefinition> AllMaps()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
                yield return map;
        }

        // ------------------------------------------------------------------
        // 1. 陆地道具不许落水
        // ------------------------------------------------------------------

        [Test]
        public void AllMaps_NoLandPropLandsInOpenWater([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            var problems = new List<string>();
            foreach (WorldPropPlacement prop in map.Props)
            {
                float ground = WorldMapRules.HeightAtWorld(
                    boxes, new Vector2(prop.Position.x, prop.Position.z));
                if (ground > LevelGeometry.WaterSurfaceY + 0.001f)
                    continue;
                if (ReefFieldRules.AllowedOnOpenWater(prop.Asset))
                    continue;
                problems.Add(string.Format("{0} ({1:F1},{2:F1})", prop.Asset, prop.Position.x, prop.Position.z));
            }
            Assert.IsEmpty(problems,
                map.Id + " 有陆地道具落在无站面的水面上（装配层会跳过它们，等于目录里的死件）: "
                + string.Join("; ", problems));
        }

        [Test]
        public void AllowedOnOpenWater_ClassifiesStoneFloatersAndLandProps()
        {
            // 石质件：落水读成礁石/残迹
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("RockS"));
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("RockM"));
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("RockL"));
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("RockFlat"));
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("Driftwood"));
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("RuinColumnBroken"));
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("RuinArch"));
            // 浮件：浮标族（FloatingPropView 负责随浪起伏）。注意口径刻意比
            // FloatingPropView 的宽松启发式**更窄**——后者的 "Ship/Boat" 分支会把
            // ShipWheelPost 判成浮件（那是"该不该起伏"的问题，不是"能不能放水里"）。
            Assert.IsTrue(ReefFieldRules.AllowedOnOpenWater("BuoyRing"));
            // 陆地道具：浮在浪上即穿帮
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("ShipWheelPost"));
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("Campfire"));
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("CrateStack"));
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("PalmTall"));
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("GrassTuft"));
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("CannonEmplacement"));
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("TreasureMound"));
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater(""));
            // 搁浅船件是明确反例（钉在滩上，不随浪）
            Assert.IsFalse(ReefFieldRules.AllowedOnOpenWater("RowboatBeached"));
        }

        // ------------------------------------------------------------------
        // 2. 礁石场：确定性 + 不侵占玩法面
        // ------------------------------------------------------------------

        [Test]
        public void ReefField_IsDeterministic([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            List<WorldPropPlacement> a = ReefFieldRules.Place(map);
            List<WorldPropPlacement> b = ReefFieldRules.Place(map);
            Assert.AreEqual(a.Count, b.Count, map.Id + " 礁石场两次生成数量不一致");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Asset, b[i].Asset, map.Id + " 礁石场第 " + i + " 件资产不一致");
                Assert.AreEqual(a[i].Position.x, b[i].Position.x, 1e-4f, map.Id);
                Assert.AreEqual(a[i].Position.z, b[i].Position.z, 1e-4f, map.Id);
            }
        }

        [Test]
        public void ReefField_HasContentOnEveryMap([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            // 每图至少铺满三位数级别的礁石：太少就还是"空海"（死水 86-96% 的图面必须有东西可看）。
            int count = ReefFieldRules.Place(map).Count;
            Assert.GreaterOrEqual(count, 60,
                map.Id + " 礁石场只有 " + count + " 件，填不满死水区");
        }

        [Test]
        public void ReefField_KeepsOffStandsSpawnsAndSightCorridor(
            [ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            List<WorldPropPlacement> reef = ReefFieldRules.Place(map);

            foreach (WorldPropPlacement p in reef)
            {
                var xz = new Vector2(p.Position.x, p.Position.z);

                // 不压站面（含净空）
                for (int i = 0; i < boxes.Count; i++)
                {
                    float d = WorldMapRules.Distance(boxes[i], xz);
                    Assert.GreaterOrEqual(d, ReefFieldRules.StandClearance - 1e-3f,
                        string.Format("{0} 礁石 {1} ({2:F1},{3:F1}) 距站面 box#{4} 只有 {5:F2}u（< {6}u 净空）",
                            map.Id, p.Asset, xz.x, xz.y, i, d, ReefFieldRules.StandClearance));
                }

                // 出生群脚下留空
                foreach (WorldMapSpawn spawn in map.Spawns)
                {
                    float d = Vector2.Distance(new Vector2(spawn.X, spawn.Z), xz);
                    Assert.GreaterOrEqual(d, ReefFieldRules.SpawnClearRadius - 1e-3f,
                        string.Format("{0} 礁石 {1} ({2:F1},{3:F1}) 距出生点只有 {4:F2}u（< {5}u）",
                            map.Id, p.Asset, xz.x, xz.y, d, ReefFieldRules.SpawnClearRadius));
                }

                // 出生视廊内不放（"出生点面向战场、画面 ≥1/3 是可读战场"的海面落实）
                Assert.IsFalse(InSightCorridor(map, xz),
                    string.Format("{0} 礁石 {1} ({2:F1},{3:F1}) 落在出生视廊内，会挡住出生视线",
                        map.Id, p.Asset, xz.x, xz.y));
            }
        }

        /// <summary>点是否落在任一条出生视廊内（口径与 ReefFieldRules.Accept 的③一致）。</summary>
        static bool InSightCorridor(WorldMapDefinition map, Vector2 p)
        {
            Vector2[] centroids = { Vector2.zero, Vector2.zero };
            bool[] valid = { false, false };
            for (int team = 0; team < 2; team++)
            {
                Vector2 sum = Vector2.zero;
                int n = 0;
                foreach (WorldMapSpawn spawn in map.Spawns)
                {
                    if (spawn.TeamIndex != team)
                        continue;
                    sum += new Vector2(spawn.X, spawn.Z);
                    n++;
                }
                if (n > 0)
                {
                    centroids[team] = sum / n;
                    valid[team] = true;
                }
            }
            if (!valid[0] || !valid[1])
                return false;

            Vector2 axis = centroids[1] - centroids[0];
            if (axis.sqrMagnitude < 1e-4f)
                return false;
            axis.Normalize();
            var normal = new Vector2(-axis.y, axis.x);
            for (int team = 0; team < 2; team++)
            {
                Vector2 rel = p - centroids[team];
                float along = Vector2.Dot(rel, axis) * (team == 0 ? 1f : -1f);
                float lateral = Mathf.Abs(Vector2.Dot(rel, normal));
                if (along >= -ReefFieldRules.SpawnClearRadius
                    && along <= ReefFieldRules.SightCorridorLength
                    && lateral <= ReefFieldRules.SightCorridorHalfWidth)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 3. 密度与构图规则（阈值【提案/待定】，实拍验收后定稿）
        // ------------------------------------------------------------------

        /// <summary>站面覆盖率下限：原值 3.9%–13.8%（死水 86–96%，审计 §一.2）。</summary>
        public const float MinStandCoverage = 0.15f;

        /// <summary>内容跨度下限（站面包围盒占图跨的比例）：原值最窄的图内容只占一条中央条带。</summary>
        public const float MinContentSpanRatio = 0.55f;

        /// <summary>双方出生质心间距上限（u）：接敌节奏约束，原值 40–174u。</summary>
        public const float MaxSpawnCentroidDistance = 120f;

        [Test]
        public void AllMaps_StandCoverageMeetsFloor([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            float open = ReefFieldRules.OpenWaterArea(map, boxes);
            float coverage = 1f - open / (map.SpanX * map.SpanZ);
            Assert.GreaterOrEqual(coverage, MinStandCoverage,
                string.Format("{0} 站面覆盖 {1:F1}%（下限 {2:P0}）：图面大部分是无人可到的死水",
                    map.Id, coverage * 100f, MinStandCoverage));
        }

        [Test]
        public void AllMaps_ContentSpansMostOfTheMap([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            List<WorldMapRules.WorldBox> boxes = WorldMapRules.AllStandBoxes(map);
            Assert.Greater(boxes.Count, 0, map.Id);

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (WorldMapRules.WorldBox b in boxes)
            {
                minX = Mathf.Min(minX, b.Center.x - b.Size.x * 0.5f);
                maxX = Mathf.Max(maxX, b.Center.x + b.Size.x * 0.5f);
                minZ = Mathf.Min(minZ, b.Center.y - b.Size.y * 0.5f);
                maxZ = Mathf.Max(maxZ, b.Center.y + b.Size.y * 0.5f);
            }
            float rx = (maxX - minX) / map.SpanX;
            float rz = (maxZ - minZ) / map.SpanZ;
            Assert.GreaterOrEqual(rx, MinContentSpanRatio,
                string.Format("{0} 内容只覆盖 x 方向 {1:P0} 的图面（下限 {2:P0}）", map.Id, rx, MinContentSpanRatio));
            Assert.GreaterOrEqual(rz, MinContentSpanRatio,
                string.Format("{0} 内容只覆盖 z 方向 {1:P0} 的图面（下限 {2:P0}）", map.Id, rz, MinContentSpanRatio));
        }

        [Test]
        public void AllMaps_SpawnCentroidsWithinContactDistance(
            [ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            Vector2[] centroids = { Vector2.zero, Vector2.zero };
            int[] counts = { 0, 0 };
            foreach (WorldMapSpawn spawn in map.Spawns)
            {
                int t = spawn.TeamIndex == 0 ? 0 : 1;
                centroids[t] += new Vector2(spawn.X, spawn.Z);
                counts[t]++;
            }
            Assert.Greater(counts[0], 0, map.Id);
            Assert.Greater(counts[1], 0, map.Id);
            centroids[0] /= counts[0];
            centroids[1] /= counts[1];
            float d = Vector2.Distance(centroids[0], centroids[1]);
            Assert.LessOrEqual(d, MaxSpawnCentroidDistance,
                string.Format("{0} 双方出生质心相距 {1:F1}u（上限 {2:F0}u）：接火前要跑太多回合",
                    map.Id, d, MaxSpawnCentroidDistance));
        }
    }
}
