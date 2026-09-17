using NUnit.Framework;
using PirateCrew.PirateCrew.Battle.WorldMaps;

namespace PirateCrew.PirateCrew.Battle.Tests
{
    /// <summary>
    /// 站面分带材质（批次 E：PirateTerrain 化）的阈值口径钉值。
    ///
    /// 【钉什么】WorldMapComposer 把三档站面（沙/草/岩）接入按**世界高度**混色的
    /// PirateTerrain 时，每档材质的 _HeightSandGrass / _HeightGrassRock 必须把本档
    /// TopY 域整体推入对应主色的权重平台区——否则同档站面会因高度不同混出别的色
    /// （例：band0 的 TopY=1.5 站面在旧阈值 0.6 下草权重 ≈1.0，整面变草），破坏
    /// 「三档有限色板、同档同观感」纪律。这里把推导用的常量（过渡带、噪声抖动上界）
    /// 与不等式钉进测试：材质阈值或 shader 噪声强度改动导致平台破裂时，跑测试即报。
    /// 【推导式】shader 混色（PirateTerrain.shader:386-390）：
    ///   grass = smoothstep(HSG-soft, HSG+soft, y+jitter)·(1-rock)
    ///   rockH = smoothstep(HGR-soft, HGR+soft, y+jitter)
    /// 平台条件：纯沙 ⇔ HSG-soft ≥ maxTop+jitter 且 HGR-soft ≥ maxTop+jitter；
    ///           纯草 ⇔ HSG+soft ≤ minTop-jitter 且 HGR-soft ≥ maxTop+jitter；
    ///           纯岩 ⇔ HGR+soft ≤ minTop-jitter（grass 被 (1-rock) 归零）。
    /// </summary>
    public class WorldMapStandMaterialTests
    {
        // ---- 与材质口径同源的常量（改 WorldMapComposer / BattleSceneLighting 时须同步）----

        /// <summary>_BlendSoftness 材质值（= BattleSceneLighting.cs:544 = shader 默认）：smoothstep 过渡半带宽。</summary>
        const float BlendSoftness = 0.5f;

        /// <summary>噪声抖动上界：shader 里 jitter=(nBig-0.5)·_NoiseStrength·2，nBig∈[0,1]
        /// → |jitter| ≤ _NoiseStrength（材质值 0.35 = BattleSceneLighting.cs:546 = shader 默认）。</summary>
        const float MaxHeightJitter = 0.35f;

        // 档位定义域（BandOf 边界）：band0 ≤1.5、band1 (1.5,3.0]、band2 >3.0。
        const float Band0MaxTop = 1.5f;
        const float Band1MinTop = 1.5f;
        const float Band1MaxTop = 3.0f;
        const float Band2MinTop = 3.0f;

        /// <summary>现役数据最小顶面（WorldMapStandables 全表最低 0.5 档）；出现更低站面时平台推导需复核。</summary>
        const float CurrentMinTop = 0.5f;

        [Test]
        public void BandOf_MatchesThreeBandBoundaries()
        {
            Assert.AreEqual(0, WorldMapComposer.BandOf(0.5f));
            Assert.AreEqual(0, WorldMapComposer.BandOf(1.5f));      // 1.5 归沙（与装饰 lowPool 划分同式）
            Assert.AreEqual(1, WorldMapComposer.BandOf(2.0f));
            Assert.AreEqual(1, WorldMapComposer.BandOf(3.0f));      // 3.0 归草
            Assert.AreEqual(2, WorldMapComposer.BandOf(3.5f));
            Assert.AreEqual(2, WorldMapComposer.BandOf(31.6f));     // 目录最高站面（灯塔类）
        }

        [Test]
        public void Band0_ThresholdsKeepTopFacePureSand()
        {
            float worstCase = Band0MaxTop + MaxHeightJitter;
            Assert.GreaterOrEqual(WorldMapComposer.HeightSandGrassOf(0) - BlendSoftness, worstCase,
                "band0 顶面出现草权重：HSG 平台破裂");
            Assert.GreaterOrEqual(WorldMapComposer.HeightGrassRockOf(0) - BlendSoftness, worstCase,
                "band0 顶面出现岩权重：HGR 平台破裂");
        }

        [Test]
        public void Band1_ThresholdsKeepTopFacePureGrass()
        {
            // 纯草下界：band1 定义域开区间极限 1.5（未来出现 1.55 的站面也须成立）。
            float grassBound = Band1MinTop - MaxHeightJitter;
            Assert.LessOrEqual(WorldMapComposer.HeightSandGrassOf(1) + BlendSoftness, grassBound,
                "band1 顶面残留沙权重：HSG 平台破裂");
            float rockBound = Band1MaxTop + MaxHeightJitter;
            Assert.GreaterOrEqual(WorldMapComposer.HeightGrassRockOf(1) - BlendSoftness, rockBound,
                "band1 顶面混入岩权重：HGR 平台破裂");
        }

        [Test]
        public void Band2_ThresholdsKeepTopFacePureRock()
        {
            // 纯岩下界：band2 定义域开区间极限 3.0。
            float bound = Band2MinTop - MaxHeightJitter;
            Assert.LessOrEqual(WorldMapComposer.HeightGrassRockOf(2) + BlendSoftness, bound,
                "band2 顶面残留草权重：HGR 平台破裂");
        }

        [Test]
        public void CatalogStandTops_FallInsideBandDefinitions()
        {
            // 现役全部站面：档位值必须与定义一致、顶面不低于现役最小值
            // （防某张图出现负顶面/超低滩涂站面后，平台推导在未复核的情况下静默失效）。
            foreach (WorldMapDefinition map in WorldMapCatalog.All)
            {
                var boxes = WorldMapRules.AllStandBoxes(map);
                Assert.Greater(boxes.Count, 0, map.Id + " 无站面");
                foreach (var box in boxes)
                {
                    int band = WorldMapComposer.BandOf(box.TopY);
                    bool inRange = band == 0
                        ? box.TopY <= Band0MaxTop
                        : band == 1
                            ? box.TopY > Band1MinTop && box.TopY <= Band1MaxTop
                            : box.TopY > Band2MinTop;
                    Assert.IsTrue(inRange,
                        map.Id + " 站面 TopY=" + box.TopY + " 归档 " + band + " 但越出定义域");
                    Assert.GreaterOrEqual(box.TopY, CurrentMinTop - 0.001f,
                        map.Id + " 站面 TopY=" + box.TopY + " 低于现役最小顶面 "
                        + CurrentMinTop + "：阈值平台推导需复核（含湿沙带触发边界）");
                }
            }
        }
    }
}
