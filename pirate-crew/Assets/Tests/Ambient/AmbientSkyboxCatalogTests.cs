using NUnit.Framework;
using UnityEngine;
using PirateCrew.PirateCrew.SceneArt;

namespace PirateCrew.PirateCrew.Ambient.Tests
{
    /// <summary>
    /// 三档天空盒预设的纯 C# 用例（视觉审计遗留 #6 的参数方案）。
    ///
    /// 【为什么要有这些测试】天空盒三色一旦接进 <c>RenderSettings.ambientMode = Skybox</c>，
    /// 环境光的 SH 就完全由它卷积而来——改一个色值等于改全场景的间接光。这些断言把"方案里
    /// 有意的形状"钉住（地平线=雾色、下半球是暖反弹、三档单调性），防止后续调参时把设计意图改掉。
    ///
    /// 【数值全部是【提案/待定】】本文件钉的是"关系"而非"观感"：关系错了是真错，
    /// 观感好不好要实拍（判据见 docs/隔壁交接-3-天空盒环境光驱动.md）。
    /// </summary>
    [TestFixture]
    public class AmbientSkyboxCatalogTests
    {
        static readonly AmbientTimeOfDay[] AllTiers =
        {
            AmbientTimeOfDay.Noon,
            AmbientTimeOfDay.Dusk,
            AmbientTimeOfDay.Overcast,
        };

        /// <summary>
        /// 最硬的一条：**地平线色必须逐值等于该档雾色**。
        ///
        /// 出处：<see cref="AmbientLightingPreset.FogColor"/> 的定义就是"该档天空地平线色，
        /// 保证大气透视而非盖灰"（AmbientTimeOfDayCatalog 类头）。二者不等时，远景物体在雾里
        /// 淡出的终点会与它身后的天空对不上，出现"远岛贴在另一块天幕上"的接缝。
        /// 改动这条契约前先想清楚"远景色 → 天空色"的连续性由谁保证。
        /// </summary>
        [Test]
        public void Presets_HorizonColorEqualsFogColor()
        {
            for (int i = 0; i < AllTiers.Length; i++)
            {
                AmbientTimeOfDay tier = AllTiers[i];
                AmbientSkyboxPreset sky = AmbientSkyboxCatalog.For(tier);
                AmbientLightingPreset lighting = AmbientTimeOfDayCatalog.For(tier);

                string label = AmbientTimeOfDayCatalog.DisplayName(tier);
                Assert.AreEqual(lighting.FogColor.r, sky.HorizonColor.r, 1e-4f, label + "：地平线色 R 必须等于雾色");
                Assert.AreEqual(lighting.FogColor.g, sky.HorizonColor.g, 1e-4f, label + "：地平线色 G 必须等于雾色");
                Assert.AreEqual(lighting.FogColor.b, sky.HorizonColor.b, 1e-4f, label + "：地平线色 B 必须等于雾色");
            }
        }

        /// <summary>
        /// 天顶色 / 地面回照色 / 太阳盘色**必须取自 <see cref="SkyTierCatalog"/>**（不另起一套色值）。
        /// 出处：SkyTierCatalog 类头自述用途之一就是"给协调者/渲染波次一个三档完整参数表，
        /// 以便切换关卡时同步天空+雾+光照"。天空盒若自造色值，同一档天空就有两套数字了。
        /// </summary>
        [Test]
        public void Presets_ZenithGroundAndSunColors_ComeFromSkyTierCatalog()
        {
            for (int i = 0; i < AllTiers.Length; i++)
            {
                AmbientTimeOfDay tier = AllTiers[i];
                AmbientSkyboxPreset sky = AmbientSkyboxCatalog.For(tier);
                SkyTierCatalog.SkyTier source = SkyTierCatalog.All[sky.SkyTierIndex - 1];

                string label = AmbientTimeOfDayCatalog.DisplayName(tier);
                AssertColorEqual(AmbientTimeOfDayCatalog.Hex(source.ZenithHex), sky.ZenithColor, label + "：天顶色");
                AssertColorEqual(AmbientTimeOfDayCatalog.Hex(source.GroundBounceHex), sky.GroundColor, label + "：地面回照色");
                AssertColorEqual(AmbientTimeOfDayCatalog.Hex(source.SunHex), sky.SunDiskColor, label + "：太阳盘色");
            }
        }

        /// <summary>
        /// 档位 → 原版天空档号（1/2/3）的映射：正午=1、黄昏=2、阴云=3。
        /// 出处：三档本就照原版 <c>skyColour</c> 分配（docs/参考游戏逆向-海盗军团抢宝藏-静态.md:676
        /// 与 SkyTierCatalog 类头：1 = 关卡 1-5/16-21、2 = 6-10/22-27、3 = 11-15/28-33）。
        /// </summary>
        [Test]
        public void Presets_SkyTierIndexMatchesOriginalSkyColourMapping()
        {
            Assert.AreEqual(1, AmbientSkyboxCatalog.For(AmbientTimeOfDay.Noon).SkyTierIndex);
            Assert.AreEqual(2, AmbientSkyboxCatalog.For(AmbientTimeOfDay.Dusk).SkyTierIndex);
            Assert.AreEqual(3, AmbientSkyboxCatalog.For(AmbientTimeOfDay.Overcast).SkyTierIndex);

            Assert.AreEqual(1, AmbientSkyboxCatalog.SkyTierIndex(AmbientTimeOfDay.Noon));
            Assert.AreEqual(2, AmbientSkyboxCatalog.SkyTierIndex(AmbientTimeOfDay.Dusk));
            Assert.AreEqual(3, AmbientSkyboxCatalog.SkyTierIndex(AmbientTimeOfDay.Overcast));
        }

        /// <summary>
        /// 曝光必须严格递减（正午 &gt; 黄昏 &gt; 阴云）——与主光强度、环境光强度、雾距的
        /// 单调性同向（见 AmbientTimeOfDayTests 的 Presets_FogEndAndAmbientAreMonotonicDecreasing）。
        /// 正午取 1.10 是刻意等于现役 BattleSky 材质的 <c>_Exposure</c>：
        /// 换天空盒时正午亮度不该跳变，观感差异应全部来自"有方向的环境光"本身。
        /// </summary>
        [Test]
        public void Presets_ExposureIsStrictlyMonotonicDecreasing()
        {
            float noon = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Noon).Exposure;
            float dusk = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Dusk).Exposure;
            float overcast = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Overcast).Exposure;

            Assert.AreEqual(1.10f, noon, 1e-4f, "正午曝光必须等于现役 BattleSky 的 _Exposure=1.1");
            Assert.Greater(noon, dusk, "正午必须比黄昏亮");
            Assert.Greater(dusk, overcast, "黄昏必须比阴云亮");
        }

        /// <summary>天顶色亮度严格递减：正午的蓝要最亮，阴云的天顶是压暗的铅灰蓝。</summary>
        [Test]
        public void Presets_ZenithLuminanceIsStrictlyMonotonicDecreasing()
        {
            float noon = Luminance(AmbientSkyboxCatalog.For(AmbientTimeOfDay.Noon).ZenithColor);
            float dusk = Luminance(AmbientSkyboxCatalog.For(AmbientTimeOfDay.Dusk).ZenithColor);
            float overcast = Luminance(AmbientSkyboxCatalog.For(AmbientTimeOfDay.Overcast).ZenithColor);

            Assert.Greater(noon, dusk, "正午天顶必须比黄昏亮");
            Assert.Greater(dusk, overcast, "黄昏天顶必须比阴云亮");
        }

        /// <summary>
        /// 每档的地平线色都必须比天顶色亮（大气散射的方向性：视线越接近地平线穿过的大气越厚、
        /// 越亮越白）。这条是"渐变有没有做对方向"的判据——反过来会让天空看起来像倒扣的碗。
        /// </summary>
        [Test]
        public void Presets_HorizonIsBrighterThanZenith()
        {
            for (int i = 0; i < AllTiers.Length; i++)
            {
                AmbientTimeOfDay tier = AllTiers[i];
                AmbientSkyboxPreset sky = AmbientSkyboxCatalog.For(tier);

                Assert.Greater(Luminance(sky.HorizonColor), Luminance(sky.ZenithColor),
                    AmbientTimeOfDayCatalog.DisplayName(tier) + "：地平线必须比天顶亮");
            }
        }

        /// <summary>
        /// 地平线过渡带宽严格递增：云越厚，地平线越糊（正午 0.14 &lt; 黄昏 0.20 &lt; 阴云 0.30）。
        /// 单位是 <c>|dir.y|</c>：0.14 ≈ 上下各 8°。
        /// </summary>
        [Test]
        public void Presets_HorizonBlendIsStrictlyMonotonicIncreasing()
        {
            float noon = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Noon).HorizonBlend;
            float dusk = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Dusk).HorizonBlend;
            float overcast = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Overcast).HorizonBlend;

            Assert.Less(noon, dusk, "阴天的地平线必须比晴天糊");
            Assert.Less(dusk, overcast, "暴风雨的地平线必须比黄昏更糊");
        }

        /// <summary>
        /// 下半球在**有阳光的两档**必须是暖反弹（R ≥ B），阴云档则允许转冷灰。
        ///
        /// 切 Skybox 环境光后下半球约占环境光贡献的一半，它若变冷，整场会失去"暖光冷影"的三灯分层
        /// （现状 Trilight 的下半球 = #C9A268 暖沙，见 BattleSceneLighting.ApplyThreePointAmbient）。
        /// 阴云档例外：<see cref="SkyTierCatalog"/> 的档 3 地面色本就是冷灰 #6E7A82（乌云蔽日没有暖反弹），
        /// 该档"变冷"是有意的——所以这里只钉"有太阳的两档偏暖"，阴云档钉"地面亮度随档递减"。
        /// </summary>
        [Test]
        public void Presets_GroundBounceIsWarmInSunlight_AndDimsThroughTiers()
        {
            AmbientSkyboxPreset noon = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Noon);
            AmbientSkyboxPreset dusk = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Dusk);
            AmbientSkyboxPreset overcast = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Overcast);

            Assert.GreaterOrEqual(noon.GroundColor.r, noon.GroundColor.b,
                "正午：地面回照必须偏暖（R ≥ B）——下半球是暖反弹，不是天蓝");
            Assert.GreaterOrEqual(dusk.GroundColor.r, dusk.GroundColor.b,
                "黄昏：地面回照必须偏暖（R ≥ B）");

            // 地面回照亮度随档递减（有太阳的暖沙 → 阴云的冷灰）。
            Assert.Greater(Luminance(noon.GroundColor), Luminance(dusk.GroundColor), "正午地面必须比黄昏亮");
            Assert.Greater(Luminance(dusk.GroundColor), Luminance(overcast.GroundColor), "黄昏地面必须比阴云亮");

            // 三档共同：地面回照必须比天顶亮（托底光不能比天顶暗）。
            for (int i = 0; i < AllTiers.Length; i++)
            {
                AmbientTimeOfDay tier = AllTiers[i];
                AmbientSkyboxPreset sky = AmbientSkyboxCatalog.For(tier);

                Assert.Greater(Luminance(sky.GroundColor), Luminance(sky.ZenithColor),
                    AmbientTimeOfDayCatalog.DisplayName(tier) + "：地面回照必须比天顶亮");
            }
        }

        /// <summary>正午/黄昏画太阳盘，阴云不画（乌云蔽日）；有盘时强度必须 &gt; 1 才可能喂到 Bloom（阈值 0.85）。</summary>
        [Test]
        public void Presets_OvercastHasNoSunDisk_NoonAndDuskDo()
        {
            AmbientSkyboxPreset noon = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Noon);
            AmbientSkyboxPreset dusk = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Dusk);
            AmbientSkyboxPreset overcast = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Overcast);

            Assert.IsTrue(noon.HasSunDisk, "正午必须有太阳盘");
            Assert.IsTrue(dusk.HasSunDisk, "黄昏必须有太阳盘");
            Assert.IsFalse(overcast.HasSunDisk, "阴云不得有太阳盘（乌云蔽日）");

            Assert.Greater(noon.SunDiskIntensity, 1f, "太阳盘强度须 > 1 才能溢出到 Bloom");
            Assert.Greater(dusk.SunDiskIntensity, 1f, "太阳盘强度须 > 1 才能溢出到 Bloom");
            Assert.Greater(noon.SunDiskSize, 0.001f, "太阳盘角半径必须为正（0 = 关闭）");
            Assert.Greater(noon.SunDiskSoftness, 0f, "边缘柔度必须为正，否则是硬圆盘");
        }

        /// <summary>
        /// 黄昏的太阳盘比正午更大更暖（低空散射的视觉惯例：日出日落时太阳视直径显得更大、
        /// 且被大气染暖）。真实太阳角半径约 0.0047 rad（0.27°），本工程是风格化"大日"，
        /// 量级与现役 Skybox/Procedural 的 _SunSize 0.065 同档——故这里只钉相对关系，不钉绝对角径。
        /// </summary>
        [Test]
        public void Presets_DuskSunDiskIsLargerThanNoon()
        {
            AmbientSkyboxPreset noon = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Noon);
            AmbientSkyboxPreset dusk = AmbientSkyboxCatalog.For(AmbientTimeOfDay.Dusk);

            Assert.Greater(dusk.SunDiskSize, noon.SunDiskSize, "黄昏的日盘必须比正午大");
            Assert.Greater(dusk.SunDiskSoftness, noon.SunDiskSoftness, "黄昏的日盘必须比正午柔（更像「发光」而非硬圆盘）");
        }

        /// <summary>
        /// **接线开关当前默认是 Skybox**（2026-09-17 用户拍板翻转）。
        ///
        /// 【这条测试的历史】它最初叫 <c>Switch_DefaultsToTrilight_SoBaselineIsUnchanged</c>，钉的是
        /// 任务书前置门——"视觉批次 A–F 实拍转正前不动全局参数"。用户拍板"你自己干"后翻转；
        /// A–F 与天空盒由此改为**同一轮实拍验收**（此前没有已转正的基线图，不存在被作废的调参轮）。
        /// 若要把环境光整体回退到三灯分层，把 <see cref="AmbientSkyboxCatalog.DefaultAmbientSource"/>
        /// 改回 <see cref="AmbientSkySource.Trilight"/> 即可（Trilight 路径完整保留，见
        /// BattleSceneLighting.ApplyThreePointAmbient），本测试随之改回旧断言。
        /// </summary>
        [Test]
        public void Switch_DefaultsToSkybox_AmbientUpgradeLanded()
        {
            Assert.AreEqual(AmbientSkySource.Skybox, AmbientSkyboxCatalog.DefaultAmbientSource,
                "环境光来源开关当前默认是天空盒驱动（2026-09-17 翻转，见测试注释）");
            Assert.IsTrue(AmbientSkyboxCatalog.SkyboxAmbientEnabled,
                "开关与 DefaultAmbientSource 必须一致（SkyboxAmbientEnabled 是它的派生读法）");
        }

        /// <summary>
        /// <see cref="AmbientSkyboxCatalog.Tiers"/> 的下标必须等于 <see cref="AmbientTimeOfDay"/> 的枚举值
        /// ——<c>AmbientDirector.skyboxMaterials[]</c> 是**按枚举值索引**的序列化数组，
        /// 顺序错了会"切黄昏用正午的天"，且不会报任何错。
        /// </summary>
        [Test]
        public void Tiers_IndexMatchesAmbientTimeOfDayEnumValues()
        {
            Assert.AreEqual(AmbientTimeOfDayCatalog.Count, AmbientSkyboxCatalog.Tiers.Length,
                "Tiers 必须覆盖全部档位");

            for (int i = 0; i < AmbientSkyboxCatalog.Tiers.Length; i++)
            {
                Assert.AreEqual(i, (int)AmbientSkyboxCatalog.Tiers[i],
                    "Tiers[" + i + "] 必须是枚举值为 " + i + " 的档位（序列化数组按下标取档）");
            }
        }

        /// <summary>
        /// 命令行档位覆盖解析（纯函数）：大小写不敏感、Storm 是 Overcast 的别名（与世界地图
        /// <c>AmbientTier</c> 字段同一套叫法）、未提供或档名不合法都返回 false——后者由调用方告警，
        /// 不静默吞（拼错档名却继续跑会让人以为在拍目标档）。
        /// </summary>
        [Test]
        public void CommandLineTier_ParsesNamesCaseInsensitive_AndRejectsGarbage()
        {
            Assert.IsTrue(AmbientTimeOfDayCatalog.TryParseCommandLineTier(
                new[] { "PirateCrew3D.exe", "-ambientTimeOfDay", "Noon" }, out AmbientTimeOfDay tier));
            Assert.AreEqual(AmbientTimeOfDay.Noon, tier);

            Assert.IsTrue(AmbientTimeOfDayCatalog.TryParseCommandLineTier(
                new[] { "exe", "-ambientTimeOfDay", "dusk" }, out tier));
            Assert.AreEqual(AmbientTimeOfDay.Dusk, tier);

            // Storm 别名：与世界地图 AmbientTier 的写法对齐。
            Assert.IsTrue(AmbientTimeOfDayCatalog.TryParseCommandLineTier(
                new[] { "exe", "-ambientTimeOfDay", "STORM" }, out tier));
            Assert.AreEqual(AmbientTimeOfDay.Overcast, tier);

            // 缺值 / 未知档名 / 开关不存在：都不覆盖。
            Assert.IsFalse(AmbientTimeOfDayCatalog.TryParseCommandLineTier(
                new[] { "exe", "-ambientTimeOfDay" }, out _));
            Assert.IsFalse(AmbientTimeOfDayCatalog.TryParseCommandLineTier(
                new[] { "exe", "-ambientTimeOfDay", "sunset" }, out _));
            Assert.IsFalse(AmbientTimeOfDayCatalog.TryParseCommandLineTier(
                new[] { "exe", "-worldMap", "wreck_hymn" }, out _));
            Assert.IsFalse(AmbientTimeOfDayCatalog.TryParseCommandLineTier(null, out _));

            // 开关存在性探针：区分"没给"与"给了但不合法"（后者要告警）。
            Assert.IsTrue(AmbientTimeOfDayCatalog.CommandLineTierSwitchPresent(
                new[] { "exe", "-ambientTimeOfDay", "sunset" }));
            Assert.IsFalse(AmbientTimeOfDayCatalog.CommandLineTierSwitchPresent(new[] { "exe" }));
            Assert.IsFalse(AmbientTimeOfDayCatalog.CommandLineTierSwitchPresent(null));
        }

        // ------------------------------------------------------------------
        // 辅助
        // ------------------------------------------------------------------

        /// <summary>sRGB 域近似相对亮度（同基准下比较单调性用；不做线性化——三档都在同一基准上比）。</summary>
        static float Luminance(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        static void AssertColorEqual(Color expected, Color actual, string label)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f, label + " R");
            Assert.AreEqual(expected.g, actual.g, 1e-4f, label + " G");
            Assert.AreEqual(expected.b, actual.b, 1e-4f, label + " B");
        }
    }
}
