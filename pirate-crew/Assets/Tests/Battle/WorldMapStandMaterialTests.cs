using System.Reflection;
using NUnit.Framework;
using PirateCrew.Battle.WorldMaps;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// 站面分带材质（像素化路径）的口径钉值。
    ///
    /// 【钉什么】<c>WorldMapComposer</c> 把三档站面（沙/草/岩）接进本路径时，每一档材质必须：
    /// <list type="number">
    ///   <item>用 <see cref="PixelartPath.ObjectShaderName"/> —— 退回旧 Terrain / URP Lit 就是回归；</item>
    ///   <item>`_BaseColor` 等于 <see cref="PixelartMaterialFactory"/> 的**对应档**色
    ///         （StandSand / StandGrass / StandRock）—— 三色对调就是回归；</item>
    ///   <item>色带档数 `_MainLightLevel` = 工厂默认 3 —— 档数被改就是回归；</item>
    ///   <item>材质名逐字是海图试点装配器的**显式映射键**
    ///         （<c>PixelartWorldMapPilotSetup.BandSandName/BandGrassName/BandRockName</c>）
    ///         —— 改名则试点场景的站面换装落空。</item>
    /// </list>
    ///
    /// 【接缝为什么是反射】<c>CreateTerrainMaterial</c> 是 private（测试程序集没有 InternalsVisibleTo，
    /// 同仓既有做法见 ShowcaseLevelSelectionTests）。站面材质是**运行时 new 出来的原生对象**，
    /// 只有拿到它才能一次验 shader / 颜色 / 档数三件事。旧实现留下的纯函数接缝
    /// （`HeightSandGrassOf` / `HeightGrassRockOf`）随旧 Terrain shader 一起退役。
    ///
    /// 【旧口径为什么整体退役】旧实现按世界高度/坡度在 PirateTerrain 里混三色，这组测试的主题曾是
    /// 「阈值平台推导」；本路径的物体 shader **没有高度/坡度/噪声/湿沙任何属性**（色带着色发生在
    /// 低分辨率域那几趟），站面改由「每档一份平色」承担，故阈值断言不再成立、也不再需要。
    /// </summary>
    public class WorldMapStandMaterialTests
    {
        // ---- 档位定义域（BandOf 边界）：band0 ≤1.5、band1 (1.5,3.0]、band2 >3.0 ----
        const float Band0MaxTop = 1.5f;
        const float Band1MinTop = 1.5f;
        const float Band1MaxTop = 3.0f;
        const float Band2MinTop = 3.0f;

        /// <summary>现役数据最小顶面（WorldMapStandables 全表最低 0.5 档）；出现更低站面时须复核
        /// 「站面 vs 替身海面（WaterSurfaceY = −0.4）的高度关系」。</summary>
        const float CurrentMinTop = 0.5f;

        /// <summary>颜色比较容差（材质属性 float32 往返；工厂 hex → Color 后写入再读回）。</summary>
        const float ColorEpsilon = 0.001f;

        /// <summary>
        /// 三档材质名 = 试点装配器的显式映射键。**冻结在此**：改名是改契约，本测试必须同时改、
        /// 且 <c>PixelartWorldMapPilotSetup</c> 的三个常量也要改（否则试点站面换装静默落空）。
        /// </summary>
        static readonly string[] ExpectedMaterialNames =
        {
            "WorldMapStand_Band0_Sand",
            "WorldMapStand_Band1_Grass",
            "WorldMapStand_Band2_Rock",
        };

        /// <summary>三档对应的站面主色（唯一来源 = 工厂常量，测试不另写一份 hex）。</summary>
        static Color[] StandColors()
        {
            return new[]
            {
                PixelartMaterialFactory.StandSand,
                PixelartMaterialFactory.StandGrass,
                PixelartMaterialFactory.StandRock,
            };
        }

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
        public void BandMaterials_UsePixelartObjectShader()
        {
            for (int band = 0; band < 3; band++)
            {
                Material mat = CreateTerrainMaterial(band);
                try
                {
                    Assert.That(mat, Is.Not.Null, MissingMaterial(band));
                    Assert.That(mat.shader.name, Is.EqualTo(PixelartPath.ObjectShaderName),
                        "band" + band + " 站面没用本路径物体 shader（退回旧 Terrain / URP Lit = 回归）");
                }
                finally
                {
                    DestroyMaterial(mat);
                }
            }
        }

        [Test]
        public void BandMaterials_UseFactoryColorsPerBand()
        {
            Color[] expected = StandColors();

            for (int band = 0; band < 3; band++)
            {
                Material mat = CreateTerrainMaterial(band);
                try
                {
                    Assert.That(mat, Is.Not.Null, MissingMaterial(band));
                    AssertColor(mat.GetColor("_BaseColor"), expected[band],
                        "band" + band + " 站面主色与工厂档色不一致（三色对调 / 色值改错 = 回归）");

                    // 三档必须两两不同：对调之外的另一种退化是「三档写成同一个色」。
                    for (int other = 0; other < 3; other++)
                    {
                        if (other == band)
                            continue;
                        bool sameAsOther =
                            Mathf.Abs(mat.GetColor("_BaseColor").r - expected[other].r) < ColorEpsilon
                            && Mathf.Abs(mat.GetColor("_BaseColor").g - expected[other].g) < ColorEpsilon
                            && Mathf.Abs(mat.GetColor("_BaseColor").b - expected[other].b) < ColorEpsilon;
                        Assert.That(sameAsOther, Is.False,
                            "band" + band + " 用了 band" + other + " 的色（三档颜色对调/合并 = 回归）");
                    }
                }
                finally
                {
                    DestroyMaterial(mat);
                }
            }
        }

        [Test]
        public void BandMaterials_UseFactoryDefaultBandCount()
        {
            for (int band = 0; band < 3; band++)
            {
                Material mat = CreateTerrainMaterial(band);
                try
                {
                    Assert.That(mat, Is.Not.Null, MissingMaterial(band));
                    Assert.That(mat.GetFloat("_MainLightLevel"),
                        Is.EqualTo(PixelartMaterialFactory.DefaultBandCount),
                        "band" + band + " 站面的色带档数被改了（站面三档一律走工厂默认 3 档）");
                }
                finally
                {
                    DestroyMaterial(mat);
                }
            }
        }

        [Test]
        public void BandMaterials_KeepPilotMappingNames()
        {
            for (int band = 0; band < 3; band++)
            {
                Material mat = CreateTerrainMaterial(band);
                try
                {
                    Assert.That(mat, Is.Not.Null, MissingMaterial(band));
                    Assert.That(mat.name, Is.EqualTo(ExpectedMaterialNames[band]),
                        "站面材质名改了 = 海图试点装配器（PixelartWorldMapPilotSetup）的显式映射键落空，"
                        + "三档站面在这种场景里会白/花屏");
                }
                finally
                {
                    DestroyMaterial(mat);
                }
            }
        }

        [Test]
        public void CatalogStandTops_FallInsideBandDefinitions()
        {
            // 现役全部站面：档位值必须与定义一致、顶面不低于现役最小值
            // （防某张图出现负顶面/超低滩涂站面后，站面与替身海面的高度关系在未复核的情况下静默失效）。
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
                        map.Id + " 站面 TopY=" + box.TopY + " 低于现役最小顶面 " + CurrentMinTop
                        + "：本路径每档一份平色不受高度影响，但贴近/低于水位线（"
                        + LevelGeometry.WaterSurfaceY + "）的站面须复核与替身海面的深度读法");
                }
            }
        }

        // ------------------------------------------------------------------
        // 反射接缝与工具
        // ------------------------------------------------------------------

        /// <summary>
        /// 调 <c>WorldMapComposer.CreateTerrainMaterial(int)</c>（private static，反射调用）。
        /// 返回 null = 物体 shader 不在包里——那是真失败，各断言会带消息叫（见 <see cref="MissingMaterial"/>）。
        /// </summary>
        static Material CreateTerrainMaterial(int band)
        {
            MethodInfo method = typeof(WorldMapComposer).GetMethod(
                "CreateTerrainMaterial", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null,
                "WorldMapComposer.CreateTerrainMaterial 被改名/删除——本测试的接缝断了（请同步改测试）");
            return (Material)method.Invoke(null, new object[] { band });
        }

        static string MissingMaterial(int band)
        {
            return "band" + band + " 的站面材质没造出来（物体 shader \""
                + PixelartPath.ObjectShaderName + "\" 缺失 / 未导入？）";
        }

        /// <summary>EditMode 下材质是原生对象，跑完即释放（Destroy 在 EditMode 不可用）。</summary>
        static void DestroyMaterial(Material mat)
        {
            if (mat != null)
                Object.DestroyImmediate(mat);
        }

        static void AssertColor(Color actual, Color expected, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(ColorEpsilon), message + "（R）");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(ColorEpsilon), message + "（G）");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(ColorEpsilon), message + "（B）");
        }
    }
}
