using System.Collections.Generic;
using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 远景特征接线（HorizonSeed/HorizonFeatures 消费）的规则层测试。
    /// 全部跑纯 C# 的 <see cref="HorizonFeatureRules.Place"/>（不实例化 GameObject，无头可测），
    /// 对目录里全部 8 张图断言：
    /// a) 确定性——同 seed 两次生成逐件逐分量完全一致；
    /// b) 环带不变量——所有件到图心的平面距离 ∈ [InnerRadius, OuterRadius]；
    /// c) 特征映射完整性——每图 HorizonFeatures 的每类至少实例化一件（且类名都已登记规格）；
    /// d) 件数上限——单图总量 ≤ MaxTotalInstances；
    /// e) 间距不变量——特征件之间、特征件与手摆 Horizon 件之间的中心距 ≥ 两件足印之和。
    /// </summary>
    public class WorldMapHorizonFeatureTests
    {
        static IEnumerable<WorldMapDefinition> AllMaps()
        {
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
                yield return map;
        }

        static void AssertSameLayout(List<HorizonFeatureRules.HorizonFeaturePlacement> a,
            List<HorizonFeatureRules.HorizonFeaturePlacement> b, string mapId)
        {
            Assert.AreEqual(a.Count, b.Count, mapId + " 两次生成的件数不同");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Asset, b[i].Asset, mapId + " 第" + i + "件资产不同");
                Assert.AreEqual(a[i].Position.x, b[i].Position.x, mapId + " 第" + i + "件 x 不同（确定性破产）");
                Assert.AreEqual(a[i].Position.y, b[i].Position.y, mapId + " 第" + i + "件 y 不同（确定性破产）");
                Assert.AreEqual(a[i].Position.z, b[i].Position.z, mapId + " 第" + i + "件 z 不同（确定性破产）");
                Assert.AreEqual(a[i].YawDeg, b[i].YawDeg, mapId + " 第" + i + "件 yaw 不同（确定性破产）");
            }
        }

        [Test]
        public void AllMaps_PlaceIsDeterministicForSameSeed([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            var first = HorizonFeatureRules.Place(map);
            var second = HorizonFeatureRules.Place(map);
            AssertSameLayout(first, second, map.Id);
        }

        [Test]
        public void AllMaps_PlacementsStayInsideRingBand([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            var placements = HorizonFeatureRules.Place(map);
            float cx = map.SpanX * 0.5f, cz = map.SpanZ * 0.5f;
            for (int i = 0; i < placements.Count; i++)
            {
                Vector3 p = placements[i].Position;
                float dist = Mathf.Sqrt((p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz));
                Assert.GreaterOrEqual(dist, HorizonFeatureRules.InnerRadius,
                    string.Format("{0} 第{1}件 {2} 距图心 {3:F1} < 环带内界", map.Id, i, placements[i].Asset, dist));
                Assert.LessOrEqual(dist, HorizonFeatureRules.OuterRadius,
                    string.Format("{0} 第{1}件 {2} 距图心 {3:F1} > 环带外界", map.Id, i, placements[i].Asset, dist));
            }
        }

        [Test]
        public void AllMaps_FeatureNamesAreRegisteredSpecs([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            for (int i = 0; i < map.HorizonFeatures.Count; i++)
            {
                Assert.IsTrue(HorizonFeatureRules.IsKnownFeature(map.HorizonFeatures[i]),
                    map.Id + " 的特征 \"" + map.HorizonFeatures[i]
                    + "\" 未登记规格（kit 里没有或规格表漏了）——不会生成任何实例");
            }
        }

        [Test]
        public void AllMaps_EveryFeatureClassHasAtLeastOneInstance(
            [ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            var placements = HorizonFeatureRules.Place(map);
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < placements.Count; i++)
            {
                counts.TryGetValue(placements[i].Asset, out int n);
                counts[placements[i].Asset] = n + 1;
            }
            for (int f = 0; f < map.HorizonFeatures.Count; f++)
            {
                string feature = map.HorizonFeatures[f];
                Assert.IsTrue(counts.TryGetValue(feature, out int count) && count >= 1,
                    map.Id + " 的特征 \"" + feature + "\" 一件都没实例化（签名缺失）");
            }
        }

        [Test]
        public void AllMaps_TotalInstanceCountIsCapped([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            var placements = HorizonFeatureRules.Place(map);
            Assert.LessOrEqual(placements.Count, HorizonFeatureRules.MaxTotalInstances,
                map.Id + " 远景件总量超上限（喧宾夺主）");
        }

        [Test]
        public void AllMaps_PlacementsKeepFootprintSpacing([ValueSource(nameof(AllMaps))] WorldMapDefinition map)
        {
            var placements = HorizonFeatureRules.Place(map);
            for (int i = 0; i < placements.Count; i++)
            {
                // 特征件之间
                for (int j = i + 1; j < placements.Count; j++)
                {
                    float min = HorizonFeatureRules.FootprintOf(placements[i].Asset)
                        + HorizonFeatureRules.FootprintOf(placements[j].Asset);
                    float d = Vector2.Distance(
                        new Vector2(placements[i].Position.x, placements[i].Position.z),
                        new Vector2(placements[j].Position.x, placements[j].Position.z));
                    Assert.GreaterOrEqual(d, min,
                        string.Format("{0} 特征件 {1}#{2} 与 {3}#{4} 间距 {5:F1} < 足印和 {6:F1}",
                            map.Id, placements[i].Asset, i, placements[j].Asset, j, d, min));
                }
                // 与手摆 Horizon 件（避让目标，全都不在玩法区外环带内也会被规则层检查）
                for (int h = 0; h < map.Horizon.Count; h++)
                {
                    float min = HorizonFeatureRules.FootprintOf(placements[i].Asset)
                        + HorizonFeatureRules.FootprintOf(map.Horizon[h].Asset);
                    float d = Vector2.Distance(
                        new Vector2(placements[i].Position.x, placements[i].Position.z),
                        new Vector2(map.Horizon[h].Position.x, map.Horizon[h].Position.z));
                    Assert.GreaterOrEqual(d, min,
                        string.Format("{0} 特征件 {1}#{2} 与手摆 {3} 间距 {4:F1} < 足印和 {5:F1}",
                            map.Id, placements[i].Asset, i, map.Horizon[h].Asset, d, min));
                }
            }
        }
    }
}
