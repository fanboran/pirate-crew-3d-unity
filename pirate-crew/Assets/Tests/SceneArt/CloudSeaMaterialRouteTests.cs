using NUnit.Framework;
using PirateCrew.SceneArt.CloudSea;

namespace PirateCrew.SceneArt.Tests
{
    /// <summary>
    /// 云端漫步场景件分流规则的纯 C# 用例（无头可跑，不 new 任何 Unity 对象）。
    ///
    /// 【覆盖】
    ///   · 对象 → 路线：FBX 里的每个对象名都落在预期路线上（对象名是 Blender 出库脚本的
    ///     契约；拼错或改名会让海床 / 水下物走错 shader，且不报错——所以把清单钉在这里）；
    ///   · 路线 → 资产名：单路线占原名、多路线共用时默认路线占原名、其余加路线后缀。
    /// </summary>
    public class CloudSeaMaterialRouteTests
    {
        // ------------------------------------------------------------------
        // 对象 → 路线
        // ------------------------------------------------------------------

        [Test]
        public void Of_SurfaceAndBed_GoToTheirOwnShaders_NotUnderwater()
        {
            // 两者都带 Cloud_Sea_ 前缀：精确名必须压过前缀，否则海床会被当成水下静物。
            Assert.AreEqual(CloudSeaRoute.ClearWater, CloudSeaMaterialRoute.Of("Cloud_Sea_Surface"));
            Assert.AreEqual(CloudSeaRoute.Seabed, CloudSeaMaterialRoute.Of("Cloud_Sea_Bed"));
        }

        [Test]
        public void Of_SeaStatics_AreUnderwater()
        {
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Sea_Weeds"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Sea_Rocks"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Sea_Coral"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Sea_Debris"));
        }

        [Test]
        public void Of_CreaturesAndBubbles_AreUnderwater()
        {
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Creature_Clownfish_0"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Creature_Sword_1"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Creature_Jelly_4"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Creature_CrabWalk_5"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Creature_StarGold_3"));
            Assert.AreEqual(CloudSeaRoute.Underwater, CloudSeaMaterialRoute.Of("Cloud_Bubbles_2"));
        }

        [Test]
        public void Of_OnlyTheExactPrefixCounts()
        {
            // 前缀匹配从名字开头算：带前缀片段但不是开头的名字不算水下件。
            Assert.AreEqual(CloudSeaRoute.Default, CloudSeaMaterialRoute.Of("Gull_Cloud_Creature_Sword_0"));
        }

        [Test]
        public void Of_AboveWaterObjects_AreDefault()
        {
            // 云台 / 云瀑 / 远景云 / 停栖鸥 / 鸟巢 / 羽毛 / 云台顶的贝壳 / 跃出水面的鱼：都走水上件路线。
            string[] aboveWater =
            {
                "Cloud_Platforms", "Cloud_Waterfall", "Cloud_Far_W", "Cloud_Far_Tower",
                "Cloud_GullPerch_0", "Cloud_Nest_1", "Cloud_Feather_0",
                "Cloud_Shell_0", "Cloud_FishJump_0", "Cloud_FishSplash_0",
            };
            foreach (string name in aboveWater)
                Assert.AreEqual(CloudSeaRoute.Default, CloudSeaMaterialRoute.Of(name), name);
        }

        [Test]
        public void Of_UnknownOrDefaultSafe_DoesNotThrow()
        {
            Assert.AreEqual(CloudSeaRoute.Default, CloudSeaMaterialRoute.Of("Cloud_Seabed"));
            Assert.AreEqual(CloudSeaRoute.Default, CloudSeaMaterialRoute.Of(string.Empty));
            Assert.AreEqual(CloudSeaRoute.Default, CloudSeaMaterialRoute.Of(null));
        }

        // ------------------------------------------------------------------
        // 路线 → 资产名
        // ------------------------------------------------------------------

        [Test]
        public void AssetName_SingleRoute_KeepsPlainName()
        {
            // 岩石色只在海底用：直接占用 Cloud_Rock，不另起后缀资产。
            Assert.AreEqual("Cloud_Rock",
                CloudSeaMaterialRoute.AssetName("Cloud_Rock", CloudSeaRoute.Underwater, 1));
        }

        [Test]
        public void AssetName_DefaultRoute_KeepsPlainName_EvenWhenShared()
        {
            Assert.AreEqual("Cloud_Coral",
                CloudSeaMaterialRoute.AssetName("Cloud_Coral", CloudSeaRoute.Default, 2));
        }

        [Test]
        public void AssetName_SharedNonDefaultRoute_GetsRouteSuffix()
        {
            Assert.AreEqual("Cloud_Coral_Underwater",
                CloudSeaMaterialRoute.AssetName("Cloud_Coral", CloudSeaRoute.Underwater, 2));
            Assert.AreEqual("Cloud_Sand_Seabed",
                CloudSeaMaterialRoute.AssetName("Cloud_Sand", CloudSeaRoute.Seabed, 2));
            Assert.AreEqual("Cloud_Sand_Underwater",
                CloudSeaMaterialRoute.AssetName("Cloud_Sand", CloudSeaRoute.Underwater, 2));
        }
    }
}
